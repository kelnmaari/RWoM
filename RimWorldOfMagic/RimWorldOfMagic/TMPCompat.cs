using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using AbilityUser;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;
using Verse.AI;

namespace TorannMagic
{
    /// <summary>
    /// Native Multiplayer (rwmt) compatibility layer.
    /// Activated from <see cref="TorannMagicMod"/> only when the Multiplayer mod is loaded,
    /// so the mod keeps working without it. The AbilityUser sync mirrors the upstream
    /// rwmt/Multiplayer-Compatibility JecsTools patch, which this replaces for RWoM.
    /// </summary>
    public static class TMPCompat
    {
        // The 1.7 fork "(1.7)Multiplayer-temp" keeps the original packageId; some
        // forks rename it — check every known id.
        public static readonly string[] MultiplayerPackageIds =
        {
            "zetrith.multiplayer",
            "rwmt.multiplayer",
        };

        public const string MultiplayerPackageId = "zetrith.multiplayer";

        public static bool Initialized { get; private set; }

        // Called from TMPCompatStartup (StaticConstructorOnStartup) — by that point
        // every active mod class (including the MP fork's) has been constructed and
        // the Multiplayer API is initialized, so RegisterSyncMethod hits the real API.
        public static void Setup()
        {
            if (Initialized)
            {
                return;
            }
            Initialized = true;

            try
            {
                PatchAbilityUserFramework();
                PatchGizmoToggles();
                PatchGolemCommands();
                PatchJobVerbRegistration();
            }
            catch (Exception ex)
            {
                Log.Error("[TMPCompat] Multiplayer compatibility setup failed: " + ex);
                return;
            }

            Log.Message("[TMPCompat] Multiplayer compatibility initialized.");
        }

        // MP sync of TryTakeOrderedJob serializes the Job via ExposeData; verbToUse is
        // a Scribe_References cross-reference that only resolves against the session's
        // SharedCrossRefs. Ability verbs are created at runtime, so register each one
        // there as soon as it is taken (both peers run this prefix during their local
        // UseAbility command, before the host's job-sync command arrives).
        private static void TryTakeOrderedJob_VerbRegister_Prefix(Job job)
        {
            try
            {
                var v = job?.verbToUse;
                if (v != null && !v.loadID.NullOrEmpty())
                {
                    SharedCrossRefsRegister(v);
                }
            }
            catch (Exception)
            {
            }
        }

        private static void InitVerb_Register_Postfix(PawnAbility __instance)
        {
            try
            {
                SharedCrossRefsRegister(__instance.Verb);
            }
            catch (Exception)
            {
            }
        }

        internal static void SharedCrossRefsRegister(Verse.ILoadReferenceable obj)
        {
            if (obj == null || mpSharedCrossRefsRegister == null)
            {
                return;
            }
            try
            {
                mpSharedCrossRefsRegister.Invoke(null, new object[] { obj });
            }
            catch (Exception)
            {
            }
        }

        private static MethodInfo mpSharedCrossRefsGetter;
        private static MethodInfo mpSharedCrossRefsRegister;

        private static void InitSharedCrossRefsHook()
        {
            try
            {
                var scribeUtil = AccessTools.TypeByName("Multiplayer.Client.ScribeUtil");
                mpSharedCrossRefsGetter = AccessTools.PropertyGetter(scribeUtil, "sharedCrossRefs");
                var sharedType = AccessTools.TypeByName("Multiplayer.Client.SharedCrossRefs")
                    ?? mpSharedCrossRefsGetter?.ReturnType;
                mpSharedCrossRefsRegister = AccessTools.Method(sharedType, "RegisterLoaded");
                if (mpSharedCrossRefsGetter == null || mpSharedCrossRefsRegister == null)
                {
                    Log.Warning("[TMPCompat] Multiplayer SharedCrossRefs API not found; verb sync hook disabled.");
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[TMPCompat] SharedCrossRefs hook failed: " + ex.Message);
            }
        }

        private static void PatchJobVerbRegistration()
        {
            InitSharedCrossRefsHook();
            if (mpSharedCrossRefsRegister == null)
            {
                return;
            }
            var harmony = new Harmony("rimworld.torann.tmagic");
            // MUST be a prefix: registration has to happen before MP's DoSync serializes
            // the job — as a postfix it ran after the sync and the verb reference went
            // out unresolved.
            harmony.Patch(AccessTools.Method(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJob)),
                prefix: new HarmonyMethod(typeof(TMPCompat), nameof(TryTakeOrderedJob_VerbRegister_Prefix)));
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("AbilityUser.PawnAbility"), "InitVerb"),
                postfix: new HarmonyMethod(typeof(TMPCompat), nameof(InitVerb_Register_Postfix)));
        }

        public static bool MultiplayerActive()
        {
            foreach (string id in MultiplayerPackageIds)
            {
                if (ModsConfig.IsActive(id))
                {
                    return true;
                }
            }
            return false;
        }

        #region AbilityUser framework

        private static void PatchAbilityUserFramework()
        {
            // Casting an ability is the core interaction; sync the job creation call
            // and stop local targeting afterwards so the client does not re-enter it.
            MP.RegisterSyncMethod(typeof(PawnAbility), nameof(PawnAbility.UseAbility))
                .SetPostInvoke(StopTargeting);
            // NOTE: if the Multiplayer API is present but uninitialized (e.g. only the
            // API assembly got loaded), MP.* throws and Setup's caller catches it —
            // the rest of the mod is unaffected.

            // CreateDelegate at runtime instead of a method-group conversion: the
            // compiler would otherwise emit a hidden static field of type
            // SyncWorkerDelegate`1, which cannot resolve when Multiplayer is absent
            // and breaks the game's static-constructor field scan.
            SyncWorkerDelegate<PawnAbility> workerDelegate =
                (SyncWorkerDelegate<PawnAbility>)Delegate.CreateDelegate(
                    typeof(SyncWorkerDelegate<PawnAbility>),
                    AccessTools.Method(typeof(TMPCompat), nameof(SyncPawnAbility)));
            MP.RegisterSyncWorker<PawnAbility>(workerDelegate, typeof(PawnAbility));
        }

        private static void StopTargeting(object instance, object[] args)
        {
            if (MP.IsExecutingSyncCommandIssuedBySelf)
            {
                Find.Targeter.StopTargeting();
            }
        }

        // PawnAbility has no stable identity across peers (no thing ID), so it is
        // synced by its position inside the pawn's CompAbilityUser power lists.
        private static void SyncPawnAbility(SyncWorker sync, ref PawnAbility ability)
        {
            if (sync.isWriting)
            {
                int compIndex = -1;
                int powerIndex = -1;
                Pawn pawn = null;

                var user = ability?.abilityUser;
                if (user?.parent is Pawn p)
                {
                    var comps = p.GetCompAbilityUsers().ToList();
                    for (int i = 0; i < comps.Count; i++)
                    {
                        var powers = comps[i].AbilityData.AllPowers;
                        int idx = powers.IndexOf(ability);
                        if (idx >= 0)
                        {
                            pawn = p;
                            compIndex = i;
                            powerIndex = idx;
                            break;
                        }
                    }
                }

                sync.Write(compIndex);
                if (compIndex < 0)
                {
                    return;
                }
                sync.Write(powerIndex);
                sync.Write(pawn);
            }
            else
            {
                int compIndex = sync.Read<int>();
                if (compIndex < 0)
                {
                    return;
                }
                int powerIndex = sync.Read<int>();
                Pawn pawn = sync.Read<Pawn>();
                var comps = pawn.GetCompAbilityUsers().ToList();
                if (compIndex < comps.Count)
                {
                    var powers = comps[compIndex].AbilityData.AllPowers;
                    if (powerIndex < powers.Count)
                    {
                        ability = powers[powerIndex];
                    }
                }
            }
        }

        #endregion

        #region Gizmo toggles

        private static void PatchGizmoToggles()
        {
            MP.RegisterSyncMethod(typeof(CompAbilityUserMagic), nameof(CompAbilityUserMagic.ToggleTechnoBit));
            MP.RegisterSyncMethod(typeof(CompAbilityUserMagic), nameof(CompAbilityUserMagic.ToggleTechnoBitRepair));
            MP.RegisterSyncMethod(typeof(CompAbilityUserMagic), nameof(CompAbilityUserMagic.ToggleElementalShot));

            MP.RegisterSyncMethod(typeof(CompAbilityUserMight), nameof(CompAbilityUserMight.ToggleCleave));
            MP.RegisterSyncMethod(typeof(CompAbilityUserMight), nameof(CompAbilityUserMight.ToggleCQC));
            MP.RegisterSyncMethod(typeof(CompAbilityUserMight), nameof(CompAbilityUserMight.TogglePsionicAugmentation));
            MP.RegisterSyncMethod(typeof(CompAbilityUserMight), nameof(CompAbilityUserMight.TogglePsionicMindAttack));

            MP.RegisterSyncMethod(typeof(CompPolymorph), nameof(CompPolymorph.CancelPolymorph));

            MP.RegisterSyncMethod(typeof(TM_Action), nameof(TM_Action.RemoveSymbiosisCommand));
            MP.RegisterSyncMethod(typeof(TM_Action), nameof(TM_Action.PromoteWanderer));
        }

        #endregion

        #region Golems

        private static void PatchGolemCommands()
        {
            MP.RegisterSyncMethod(typeof(Golems.Building_TMGolemBase), nameof(Golems.Building_TMGolemBase.ToggleHoldFire));
            MP.RegisterSyncMethod(typeof(Golems.Building_TMGolemBase), nameof(Golems.Building_TMGolemBase.ToggleGlow));
            MP.RegisterSyncMethod(typeof(Golems.TMPawnGolem), nameof(Golems.TMPawnGolem.ToggleRangedAttacks));
        }

        #endregion
    }
}
