using System;
using AbilityUser;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace TorannMagic
{
    // Vanilla tracker entry for an RWoM ability. The active JecsLite owner
    // owns the verb, targeting, costs and cooldown throughout its lifetime.
    public class TMBridgeAbility : Ability
    {
        public TMBridgeAbility()
        {
        }

        public TMBridgeAbility(Pawn pawn, RimWorld.AbilityDef def) : base(pawn, def)
        {
        }

        // The JecsLite cast engine for the resolved original. Null = sleeping
        // knowledge (def gone); every consumer gates on this, never throwing.
        public PawnAbility Companion
        {
            get
            {
                var bridge = def as TMBridgeAbilityDef;
                var original = bridge?.Original;
                if (original == null || pawn?.TryGetComp<AbilityKnowledgeComp>()?.Find(bridge.spellId)?.learned != true)
                    return null;
                foreach (ThingComp comp in pawn.AllComps)
                {
                    if (comp is CompAbilityUserTMBase user)
                    {
                        var powers = user.AbilityData?.AllPowers;
                        if (powers == null) continue;
                        foreach (var power in powers)
                            if (power?.Def == original) return power;
                    }
                }
                return null;
            }
        }

        public override IEnumerable<Command> GetGizmos()
        {
            var owner = Companion;
            if (owner == null || !VanillaBridgeGizmoVisibilityPatch.VisibleThroughTracker(owner)) yield break;
            gizmo ??= new BridgeCommand(this, pawn);
            yield return gizmo;
        }

        // A vanilla command remains visible to the tracker and integrations. Use the
        // owner's targeting UI for self, local, world and multi-stage spells.
        private sealed class BridgeCommand : Command_Ability
        {
            private readonly TMBridgeAbility bridge;
            public BridgeCommand(TMBridgeAbility bridge, Pawn pawn) : base(bridge, pawn)
                => this.bridge = bridge;

            public override void ProcessInput(Event ev)
            {
                if (!bridge.GizmoDisabled(out _)) bridge.Companion?.GetGizmo().ProcessInput(ev);
            }

            public override void GizmoUpdateOnMouseover()
                => bridge.Companion?.GetGizmo().GizmoUpdateOnMouseover();
        }

        public override bool GizmoDisabled(out string reason)
        {
            // The vanilla gizmo greys out on the REAL resource/cooldown state
            // through the companion's own admission check. Both that check and
            // the vanilla base logic probe ambient state (faction, needs,
            // story, world) a generated or exotic pawn may not carry — the
            // grey-out is cosmetic and must never break the gizmo; the cast
            // itself re-checks admission.
            try
            {
                PawnAbility owner = Companion;
                if (owner == null)
                {
                    reason = "TM_SleepingSpellsDesc".Translate();
                    return true;
                }
                if (owner.Verb == null || !pawn.Spawned)
                {
                    reason = "TM_SleepingSpellsDesc".Translate();
                    return true;
                }
                if (!owner.CanCastPowerCheck(AbilityContext.Player, out string why))
                {
                    reason = why;
                    return true;
                }
                return base.GizmoDisabled(out reason);
            }
            catch (Exception)
            {
                reason = "TM_SleepingSpellsDesc".Translate();
                return true;
            }
        }

        // Phased out of vanilla AI targeting on purpose: our ResolveAIAutoCast
        // keeps AI ownership, so the bridge must not double-cast.
        public override bool AICanTargetNow(LocalTargetInfo target)
        {
            return false;
        }

        // The bridge must not run the vanilla verb/warmup machinery — the cast
        // path owns ticking through the companion's PawnAbility. Vanilla
        // WarmupTick derefs job/verb state the bridge never sets (crashed on
        // world pawns).
        public override void AbilityTick()
        {
        }

        public override bool Activate(LocalTargetInfo target, LocalTargetInfo dest)
        {
            return !GizmoDisabled(out _) && Companion.UseAbility(AbilityContext.Player, target) != null;
        }

        public override void QueueCastingJob(LocalTargetInfo target, LocalTargetInfo destination)
        {
            Activate(target, destination);
        }

    }
}
