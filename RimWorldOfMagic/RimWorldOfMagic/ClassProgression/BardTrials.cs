using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace TorannMagic.ClassProgression
{
    public static class BardTrials
    {
        public const string FoundationTrialId = "rwom.bard.foundation", MasteryTrialId = "rwom.bard.mastery";
        static DisciplineProgress Owned(Pawn caster, bool inspiration)
        {
            if (caster == null || caster.Dead || !caster.Spawned || caster.Faction == null
                || caster.story?.traits?.HasTrait(TorannMagicDefOf.Faceless) == true
                || !ClassIdentity.TryGetState(caster, out var state) || (state.Vocation & Vocation.Mage) == 0) return null;
            var path = state.Find("rwom.bard");
            if (path?.Domain != DisciplineDomain.Magic || path.Stage != (inspiration ? InitiationStage.Adept : InitiationStage.Foundation)
                || path.CompletedTrials.Contains(inspiration ? MasteryTrialId : FoundationTrialId)) return null;
            var magic = caster.AllComps.OfType<CompAbilityUserMagic>().FirstOrDefault();
            var powers = magic?.MagicData.MagicPowersB;
            return powers != null && (inspiration
                ? powers.Any(p => p.learned && p.TMabilityDefs.Contains(TorannMagicDefOf.TM_Inspire))
                : powers.Any(p => p.learned && p.TMabilityDefs.Contains(TorannMagicDefOf.TM_BardTraining))
                    && powers.Any(p => p.learned && p.TMabilityDefs.Contains(TorannMagicDefOf.TM_Entertain))) ? path : null;
        }
        static bool Ally(Pawn caster, Pawn target)
            => caster != null && target != null && target != caster && !target.Dead && target.Spawned && target.Map == caster.Map
                && target.RaceProps?.Humanlike == true && target.Faction != null && target.Faction == caster.Faction
                && !target.IsPrisoner && !target.IsSlave && !TM_Calc.IsUndead(target);
        static Hediff Active(Pawn caster, string name)
            => caster?.health?.hediffSet?.hediffs.FirstOrDefault(h => h.def?.defName == name);
        static IEnumerable<Thought_Memory> Memories(Pawn caster, Pawn target)
            => target?.needs?.mood?.thoughts?.memories?.Memories.Where(m => m.def?.defName == "TM_EntertainedTD" && m.otherPawn == caster)
                ?? Enumerable.Empty<Thought_Memory>();
        public sealed class Snapshot
        {
            internal Pawn caster, target;
            internal DisciplineProgress path;
            internal Hediff source;
            internal Dictionary<Thought_Memory, int> memories;
            internal bool used;
        }
        public static Snapshot CaptureEntertainment(Pawn caster, Pawn target)
        {
            var path = Ally(caster, target) ? Owned(caster, false) : null;
            var source = path != null ? Active(caster, "TM_EntertainingHD") : null;
            return source == null || target.needs?.mood == null ? null : new Snapshot { caster = caster, target = target, path = path, source = source,
                memories = Memories(caster, target).ToDictionary(m => m, m => m.age) };
        }
        public static bool CompleteEntertainment(Snapshot snapshot, bool success)
        {
            if (snapshot == null || snapshot.used) return false;
            snapshot.used = true;
            return success && Ally(snapshot.caster, snapshot.target) && Owned(snapshot.caster, false) == snapshot.path
                && Active(snapshot.caster, "TM_EntertainingHD") == snapshot.source
                && Memories(snapshot.caster, snapshot.target).Any(m => !snapshot.memories.TryGetValue(m, out var age) || m.age < age)
                && snapshot.path.RecordCompletedTrial(FoundationTrialId);
        }
        // Called only around the native Entertain job's interaction; preserve its one native call and result.
        public static bool Entertain(Pawn caster, Pawn target)
        {
            var snapshot = CaptureEntertainment(caster, target);
            bool result = caster.interactions.TryInteractWith(target, TorannMagicDefOf.TM_EntertainID);
            CompleteEntertainment(snapshot, result);
            return result;
        }
        public static Snapshot CaptureInspiration(Hediff source, Pawn target)
        {
            var caster = source?.pawn;
            var path = Ally(caster, target) && target.mindState?.inspirationHandler != null && target.Inspiration == null
                && source.def?.defName == "TM_InspirationalHD" && Active(caster, "TM_InspirationalHD") == source ? Owned(caster, true) : null;
            return path == null ? null : new Snapshot { caster = caster, target = target, path = path, source = source };
        }
        public static bool CompleteInspiration(Snapshot snapshot)
        {
            if (snapshot == null || snapshot.used) return false;
            snapshot.used = true;
            return Ally(snapshot.caster, snapshot.target) && Owned(snapshot.caster, true) == snapshot.path
                && Active(snapshot.caster, "TM_InspirationalHD") == snapshot.source && snapshot.target.Inspiration != null
                && snapshot.path.RecordCompletedTrial(MasteryTrialId);
        }
        // Observe the actual successful recipient state, not the attempted random selection or mote.
        public static void Inspire(Hediff source, Pawn target, InspirationDef inspiration)
        {
            var snapshot = CaptureInspiration(source, target);
            target.mindState.inspirationHandler.TryStartInspiration(inspiration);
            CompleteInspiration(snapshot);
        }
    }
}
