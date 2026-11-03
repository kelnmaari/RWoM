using RimWorld;
using System;
using Verse;
using AbilityUser;
using System.Linq;

namespace TorannMagic
{
    class Verb_Reversal : Verb_UseAbility  
    {
        internal const string Order = "TM_SpiritCastReversal";

        protected override bool TryCastShot() => Apply(CasterPawn, CasterPawn);

        internal static bool Apply(Pawn body, Pawn source)
        {
            var comp = source?.GetCompAbilityUserMight();
            if (body == null || body.Dead || comp == null) return false;
            int rank = comp.MightData.MightPowerSkill_Reversal.FirstOrDefault(skill => skill.label == "TM_Reversal_pwr")?.level ?? 0;
            float strength = (8 + 2 * rank) * comp.mightPwr;
            if (body == source)
            {
                HealthUtility.AdjustSeverity(body,TorannMagicDefOf.TM_ReversalHD,strength);
                return true;
            }
            if (body.health.hediffSet.HasHediff(TorannMagicDefOf.TM_ReversalHD)) return false;
            var stance=HediffMaker.MakeHediff(TorannMagicDefOf.TM_ReversalHD,body);
            var marker=stance.TryGetComp<HediffComp_Reversal>();
            if (marker == null) return false;
            marker.Bind(source);
            stance.Severity=strength;
            body.health.AddHediff(stance);
            return body.health.hediffSet.hediffs.Contains(stance);
        }
    }
}
