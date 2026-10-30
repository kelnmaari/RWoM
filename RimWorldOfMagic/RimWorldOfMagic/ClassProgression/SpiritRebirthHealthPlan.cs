using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace TorannMagic.ClassProgression
{
    // Conservative, explicit physical cleanup. Unknown effects require an adapter;
    // never silently discard another mod's identity, links or progression.
    internal sealed class SpiritRebirthHealthPlan
    {
        private readonly Pawn pawn;
        private readonly Hediff[] original;
        private readonly Hediff[] physical;
        private bool attempted;

        private static readonly HashSet<string> PhysicalDefs = new HashSet<string> {
            "BloodLoss", "Anesthetic", "FoodPoisoning", "ToxicBuildup", "DrugOverdose",
            "Malnutrition", "Heatstroke", "Hypothermia", "BadBack", "Frail", "Cataract",
            "Asthma", "Carcinoma", "Cirrhosis", "Alzheimers", "Dementia"
        };
        private static readonly HashSet<string> PhysicalComps = new HashSet<string> {
            "Verse.HediffComp_TendDuration", "Verse.HediffComp_GetsPermanent",
            "Verse.HediffComp_Infecter", "Verse.HediffComp_Immunizable",
            "Verse.HediffComp_SeverityPerDay", "Verse.HediffComp_GrowthMode",
            "Verse.HediffComp_SkillDecay"
        };
        private static readonly HashSet<string> PhysicalImplants = new HashSet<string> {
            "ArchotechEye", "ArchotechArm", "ArchotechLeg", "BionicEye", "BionicArm",
            "BionicLeg", "BionicSpine", "BionicHeart", "BionicStomach", "BionicEar",
            "BionicTongue", "BionicJaw", "PegLeg", "WoodenHand", "WoodenFoot", "Denture",
            "SimpleProstheticLeg", "SimpleProstheticArm", "SimpleProstheticHeart",
            "CochlearImplant", "PowerClaw", "Joywire", "Painstopper"
        };

        private SpiritRebirthHealthPlan(Pawn pawn, Hediff[] original, Hediff[] physical)
        { this.pawn = pawn; this.original = original; this.physical = physical; }

        internal static bool TryPrepare(Pawn pawn, out SpiritRebirthHealthPlan plan, out Hediff unsupported)
        {
            plan = null;
            unsupported = null;
            if (pawn == null || pawn.Destroyed || !pawn.Dead || pawn.health?.hediffSet?.hediffs == null) return false;
            var snapshot = pawn.health.hediffSet.hediffs.ToArray();
            var removals = new List<Hediff>();
            foreach (var effect in snapshot)
            {
                if (effect == null || effect.pawn != pawn || effect.def == null) { unsupported = effect; return false; }
                if (effect.GetType() == typeof(Hediff_ClassDiscipline)
                    && ReferenceEquals(effect.def, DefDatabase<HediffDef>.GetNamedSilentFail("TM_ClassDiscipline"))
                    && effect.def.hediffClass == typeof(Hediff_ClassDiscipline)
                    && (effect.def.stages == null || effect.def.stages.Count == 0)
                    && !effect.def.forceRemoveOnResurrection
                    && effect.def.lethalSeverity <= 0f) continue;
                if (!IsKnownPhysical(effect)) { unsupported = effect; return false; }
                removals.Add(effect);
            }
            plan = new SpiritRebirthHealthPlan(pawn, snapshot, removals.ToArray());
            return true;
        }

        private static bool IsKnownPhysical(Hediff effect)
        {
            var type = effect.GetType();
            bool structural = type == typeof(Hediff_Injury) || type == typeof(Hediff_MissingPart)
                || ((type == typeof(Hediff_AddedPart) || type == typeof(Hediff_Implant))
                    && PhysicalImplants.Contains(effect.def.defName)
                    && ReferenceEquals(DefDatabase<HediffDef>.GetNamedSilentFail(effect.def.defName), effect.def));
            if (!structural && !((type == typeof(Hediff) || type == typeof(HediffWithComps))
                && PhysicalDefs.Contains(effect.def.defName)
                && ReferenceEquals(DefDatabase<HediffDef>.GetNamedSilentFail(effect.def.defName), effect.def))) return false;
            var comps = (effect as HediffWithComps)?.comps;
            return comps == null || comps.All(c => c != null
                && c.GetType().Assembly == typeof(Hediff).Assembly && PhysicalComps.Contains(c.GetType().FullName));
        }

        // Called once at the irreversible conversion boundary, never during menu
        // validation or ritual work. Removal callbacks are native and not rolled back.
        internal bool Apply()
        {
            if (attempted || pawn.Destroyed || !pawn.Dead || pawn.health?.hediffSet?.hediffs == null
                || !pawn.health.hediffSet.hediffs.SequenceEqual(original)
                || physical.Any(h => h.pawn != pawn || !IsKnownPhysical(h))) return false;
            attempted = true;
            foreach (var effect in physical) pawn.health.RemoveHediff(effect);
            return original.Except(physical).All(h => pawn.health.hediffSet.hediffs.Contains(h))
                && physical.All(h => !pawn.health.hediffSet.hediffs.Contains(h));
        }
    }
}
