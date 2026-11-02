using AbilityUser;
using RimWorld;
using Verse.AI;
using Verse;

namespace TorannMagic
{
    // Ф5 spike: a vanilla Ability that stands in for an RWoM spell. The bridge
    // contributes the vanilla surfaces (tracker storage, gizmo grid, AI
    // lookups, cooldown ticking); execution stays on the proven JecsLite path
    // through a transient companion PawnAbility rebuilt from the knowledge
    // store + defs. The companion is never scribed — it is pure runtime cache.
    public class TMBridgeAbility : Ability
    {
        private PawnAbility companion;

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
                if (companion == null)
                {
                    TMAbilityDef original = ((TMBridgeAbilityDef)def).Original;
                    CompAbilityUser abilityUser = pawn?.GetCompAbilityUser();
                    if (original == null || abilityUser == null)
                    {
                        return null;
                    }
                    companion = new PawnAbility { Def = original, AbilityUser = abilityUser };
                }
                return companion;
            }
        }

        public override bool GizmoDisabled(out string reason)
        {
            if (Companion == null)
            {
                reason = "TM_SleepingSpellsDesc".Translate();
                return true;
            }
            return base.GizmoDisabled(out reason);
        }

        public override bool Activate(LocalTargetInfo target, LocalTargetInfo dest)
        {
            PawnAbility owner = Companion;
            if (owner == null)
            {
                return false;
            }
            return owner.UseAbility(AbilityContext.Player, target) != null;
        }

        public override void QueueCastingJob(LocalTargetInfo target, LocalTargetInfo destination)
        {
            // Vanilla queueing routes through the same JecsLite owner so the
            // job pipeline, warmup and resources stay identical to manual casts.
            PawnAbility owner = Companion;
            Job job = owner?.UseAbility(AbilityContext.Player, target);
            if (job != null)
            {
                pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }
        }
    }
}
