using System;
using System.Collections.Generic;
using System.Linq;
using AbilityUser;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

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
        public const string MultiplayerPackageId = "zetrith.multiplayer";

        public static bool Initialized { get; private set; }

        public static void Setup()
        {
            if (Initialized)
            {
                return;
            }
            Initialized = true;

            PatchAbilityUserFramework();
            PatchGizmoToggles();
            PatchGolemCommands();

            if (Prefs.DevMode)
            {
                Log.Message("[TMPCompat] Multiplayer compatibility initialized.");
            }
        }

        public static bool MultiplayerActive()
        {
            return ModsConfig.IsActive(MultiplayerPackageId);
        }

        #region AbilityUser framework

        private static void PatchAbilityUserFramework()
        {
            // Casting an ability is the core interaction; sync the job creation call
            // and stop local targeting afterwards so the client does not re-enter it.
            MP.RegisterSyncMethod(typeof(PawnAbility), nameof(PawnAbility.UseAbility))
                .SetPostInvoke(StopTargeting);

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
