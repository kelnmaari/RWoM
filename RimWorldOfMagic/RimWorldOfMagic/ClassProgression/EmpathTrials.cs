using System.Linq;
using AbilityUser;
using RimWorld;
using Verse;
namespace TorannMagic.ClassProgression
{
    public static class EmpathTrials
    {
        public const string FoundationTrialId = "rwom.empath.foundation", MasteryTrialId = "rwom.empath.mastery";
        static CompAbilityUserMagic Magic(Pawn p) => p?.AllComps.OfType<CompAbilityUserMagic>().FirstOrDefault();
        static MagicPower Owner(Pawn p, bool aura) => EmpathOwners.Resolve(Magic(p)?.MagicData)?.SingleOrDefault(x => x.learned && x.TMabilityDefs.Contains(aura ? TorannMagicDefOf.TM_SuppressiveAura : TorannMagicDefOf.TM_MindKiller));
        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        static bool Caster(Pawn p) => p != null && !p.Dead && p.Spawned && p.Faction != null && p.story?.traits?.HasTrait(TorannMagicDefOf.Faceless) != true;
        static DisciplineProgress Path(Pawn p, bool aura)
        {
            if (!Caster(p) || !ClassIdentity.TryGetState(p, out var state) || (state.Vocation & Vocation.Mage) == 0) return null;
            var path = state.Find("rwom.empath");
            return path?.Domain == DisciplineDomain.Magic && path.Stage == (aura ? InitiationStage.Adept : InitiationStage.Foundation)
                && !path.CompletedTrials.Contains(aura ? MasteryTrialId : FoundationTrialId) && Owner(p, aura) != null ? path : null;
        }
        static bool Runtime(PawnAbility a, bool aura) => a != null && a.Def == (aura ? TorannMagicDefOf.TM_SuppressiveAura : TorannMagicDefOf.TM_MindKiller)
            && a.AbilityUser == Magic(a.Pawn) && Magic(a.Pawn)?.AbilityData.Powers.Any(p => ReferenceEquals(p, a)) == true;
        static bool Target(Pawn c, Pawn p) => p != null && p != c && !p.Dead && p.Spawned && p.Map == c.Map && !p.IsPrisoner && !p.IsSlave
            && p.RaceProps?.Humanlike == true && p.RaceProps.IsFlesh && !TM_Calc.IsUndead(p) && p.needs?.mood != null && p.health?.hediffSet != null;
        static bool Enemy(Pawn c, Pawn p) => Target(c, p) && p.HostileTo(c) && (p.Faction == null ? p.InAggroMentalState : p.Faction != c.Faction && p.Faction.HostileTo(c.Faction));
        public sealed class MoodSnapshot
        {
            internal Pawn caster, target;
            internal PawnAbility ability;
            internal Need_Mood mood;
            internal float before;
            internal MagicPower owner;
            internal DisciplineProgress path;
            internal bool used;
        }
        public static MoodSnapshot CaptureMood(PawnAbility ability, Pawn target)
        {
            var caster = ability?.Pawn; var path = Path(caster, false);
            return path != null && Runtime(ability, false) && Enemy(caster, target) && Finite(target.needs.mood.CurLevel) && target.needs.mood.CurLevel > 0
                ? new MoodSnapshot { caster = caster, target = target, ability = ability, mood = target.needs.mood, before = target.needs.mood.CurLevel, owner = Owner(caster, false), path = path } : null;
        }
        public static bool CompleteMood(MoodSnapshot s)
        {
            if (s == null || s.used) return false;
            s.used = true;
            return Path(s.caster, false) == s.path && Owner(s.caster, false) == s.owner && Runtime(s.ability, false) && Enemy(s.caster, s.target)
                && s.target.needs.mood == s.mood && Finite(s.mood.CurLevel) && s.mood.CurLevel >= 0 && s.mood.CurLevel < s.before && s.path.RecordCompletedTrial(FoundationTrialId);
        }
        public sealed class Activation
        {
            internal Pawn caster;
            internal PawnAbility ability;
            internal MagicPower owner;
            internal bool used;
        }
        public static Activation CaptureAura(PawnAbility ability)
        {
            var c = ability?.Pawn;
            return Caster(c) && Runtime(ability, true) && Owner(c, true) != null && !c.health.hediffSet.HasHediff(TorannMagicDefOf.TM_SuppressiveAuraHD)
                ? new Activation { caster = c, ability = ability, owner = Owner(c, true) } : null;
        }
        public static bool CompleteAura(Activation s)
        {
            if (s == null || s.used) return false;
            s.used = true;
            if (!Caster(s.caster) || Owner(s.caster, true) != s.owner || !Runtime(s.ability, true)) return false;
            var hd = s.caster.health.hediffSet.GetFirstHediffOfDef(TorannMagicDefOf.TM_SuppressiveAuraHD); var comp = hd?.TryGetComp<HediffComp_SuppressiveAura>();
            if (comp == null || hd.pawn != s.caster || !Finite(hd.Severity) || hd.Severity <= 0) return false;
            comp.trialCaster = s.caster; comp.ownTrialActivation = true; comp.trialOwner = s.owner;
            return true;
        }
        static bool Source(HediffComp_SuppressiveAura source)
        {
            var c = source?.Pawn;
            return Caster(c) && source.ownTrialActivation && source.trialCaster == c && !source.CompShouldRemove && source.parent.def == TorannMagicDefOf.TM_SuppressiveAuraHD
                && c.health.hediffSet.hediffs.Contains(source.parent) && Finite(source.parent.Severity) && source.parent.Severity > 0
                && Owner(c, true) != null && (source.trialOwner == null || source.trialOwner == Owner(c, true)) && Magic(c).AbilityData.Powers.Any(a => Runtime(a, true));
        }
        public sealed class SuppressionSnapshot
        {
            internal HediffComp_SuppressiveAura source;
            internal Pawn caster, target;
            internal MagicPower owner;
            internal DisciplineProgress path;
            internal bool used;
        }
        public static SuppressionSnapshot CaptureSuppression(HediffComp_SuppressiveAura source, Pawn target)
        {
            var c = source?.Pawn; var path = Path(c, true);
            return path != null && Source(source) && Target(c, target) && target.Faction == c.Faction && !target.health.hediffSet.HasHediff(TorannMagicDefOf.TM_EmotionSuppressionHD)
                ? new SuppressionSnapshot { source = source, caster = c, target = target, owner = Owner(c, true), path = path } : null;
        }
        public static bool CompleteSuppression(SuppressionSnapshot s)
        {
            if (s == null || s.used) return false;
            s.used = true;
            if (Path(s.caster, true) != s.path || Owner(s.caster, true) != s.owner || !Source(s.source) || !Target(s.caster, s.target) || s.target.Faction != s.caster.Faction) return false;
            var hd = s.target.health.hediffSet.GetFirstHediffOfDef(TorannMagicDefOf.TM_EmotionSuppressionHD);
            return hd != null && hd.pawn == s.target && Finite(hd.Severity) && hd.Severity > 0 && s.path.RecordCompletedTrial(MasteryTrialId);
        }
    }
}
