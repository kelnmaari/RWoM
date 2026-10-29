using System.Linq;
using AbilityUser;
using Verse;

namespace TorannMagic.ClassProgression
{
    public static class TechnomancerTrials
    {
        public const string FoundationTrialId = "rwom.technomancer.foundation", MasteryTrialId = "rwom.technomancer.mastery";
        static CompAbilityUserMagic Magic(Pawn pawn) => pawn?.AllComps.OfType<CompAbilityUserMagic>().FirstOrDefault();
        static MagicPower Owner(Pawn pawn, TMAbilityDef def) => Magic(pawn)?.MagicData.MagicPowersT.FirstOrDefault(p => p.learned && p.TMabilityDefs.Contains(def));
        static DisciplineProgress Owned(Pawn caster, bool shield)
        {
            if (caster == null || caster.Dead || !caster.Spawned || caster.Faction == null
                || caster.story?.traits?.HasTrait(TorannMagicDefOf.Faceless) == true
                || !ClassIdentity.TryGetState(caster, out var state) || (state.Vocation & Vocation.Mage) == 0) return null;
            var path = state.Find("rwom.technomancer");
            return path?.Domain == DisciplineDomain.Magic && path.Stage == (shield ? InitiationStage.Adept : InitiationStage.Foundation)
                && !path.CompletedTrials.Contains(shield ? MasteryTrialId : FoundationTrialId)
                && Owner(caster, shield ? TorannMagicDefOf.TM_TechnoShield : TorannMagicDefOf.TM_TechnoBit) != null ? path : null;
        }
        static bool ActiveBit(HediffComp source, Pawn caster)
            => source is HediffComp_TechnoBit && source.parent?.pawn == caster && source.parent.def?.defName == "TM_TechnoBitHD"
                && caster.health?.hediffSet?.hediffs.Contains(source.parent) == true && source.parent.comps?.Contains(source) == true
                && Magic(caster)?.useTechnoBitToggle == true && Magic(caster).useTechnoBitRepairToggle;
        static bool BuildingTarget(Pawn caster, Building target) => target != null && !target.Destroyed && target.Spawned
            && target.Map == caster.Map && target.Faction == caster.Faction;
        static bool RuntimeOwned(PawnAbility ability, Pawn caster) => ability != null && ability.Pawn == caster && ability.Def == TorannMagicDefOf.TM_TechnoShield
            && ability.AbilityUser == Magic(caster) && Magic(caster)?.AbilityData.Powers.Any(p => ReferenceEquals(p, ability)) == true;
        static bool Ally(Pawn caster, Pawn target) => target != null && target != caster && !target.Dead && target.Spawned
            && target.Map == caster.Map && target.RaceProps?.Humanlike == true && target.Faction == caster.Faction
            && !target.IsPrisoner && !target.IsSlave && !TM_Calc.IsUndead(target) && target.health?.hediffSet != null;
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static float Shield(Pawn target) => target.health.hediffSet.hediffs.Where(h => h.def == TorannMagicDefOf.TM_TechnoShieldHD
            || h.def == TorannMagicDefOf.TM_TechnoShieldHD_I || h.def == TorannMagicDefOf.TM_TechnoShieldHD_II || h.def == TorannMagicDefOf.TM_TechnoShieldHD_III).Sum(h => h.Severity);
        public sealed class RepairSnapshot
        {
            internal Pawn caster;
            internal HediffComp source;
            internal HediffWithComps parent;
            internal Building target;
            internal DisciplineProgress path;
            internal MagicPower owner;
            internal int hp;
            internal bool used;
        }
        public static RepairSnapshot CaptureRepair(HediffComp source, Building target)
        {
            var caster = source?.parent?.pawn;
            var path = Owned(caster, false);
            return path != null && ActiveBit(source, caster) && BuildingTarget(caster, target) && target.HitPoints > 0 && target.HitPoints < target.MaxHitPoints
                ? new RepairSnapshot { caster = caster, source = source, parent = source.parent, target = target, path = path,
                    owner = Owner(caster, TorannMagicDefOf.TM_TechnoBit), hp = target.HitPoints } : null;
        }
        public static bool CompleteRepair(RepairSnapshot snapshot)
        {
            if (snapshot == null || snapshot.used) return false;
            snapshot.used = true;
            return Owned(snapshot.caster, false) == snapshot.path && Owner(snapshot.caster, TorannMagicDefOf.TM_TechnoBit) == snapshot.owner
                && snapshot.source.parent == snapshot.parent && ActiveBit(snapshot.source, snapshot.caster) && BuildingTarget(snapshot.caster, snapshot.target)
                && snapshot.target.HitPoints > snapshot.hp && snapshot.path.RecordCompletedTrial(FoundationTrialId);
        }
        public sealed class ShieldSnapshot
        {
            internal Pawn caster, target;
            internal PawnAbility ability;
            internal MagicPower owner;
            internal DisciplineProgress path;
            internal float severity;
            internal bool used;
        }
        public static ShieldSnapshot CaptureShield(PawnAbility ability, Pawn target)
        {
            var caster = ability?.Pawn;
            var path = Owned(caster, true);
            if (path == null || !RuntimeOwned(ability, caster) || !Ally(caster, target)) return null;
            float severity = Shield(target);
            return Finite(severity) ? new ShieldSnapshot { caster = caster, target = target, ability = ability, path = path,
                owner = Owner(caster, TorannMagicDefOf.TM_TechnoShield), severity = severity } : null;
        }
        public static bool CompleteShield(ShieldSnapshot snapshot)
        {
            if (snapshot == null || snapshot.used) return false;
            snapshot.used = true;
            float after = Ally(snapshot.caster, snapshot.target) ? Shield(snapshot.target) : float.NaN;
            return Owned(snapshot.caster, true) == snapshot.path && Owner(snapshot.caster, TorannMagicDefOf.TM_TechnoShield) == snapshot.owner
                && RuntimeOwned(snapshot.ability, snapshot.caster) && Finite(after) && after > 0 && after > snapshot.severity
                && snapshot.path.RecordCompletedTrial(MasteryTrialId);
        }
    }
}
