using System.Linq;
using RimWorld;
using Verse;

namespace TorannMagic.ClassProgression
{
    public static class EmpathLifecycle
    {
        public static bool Restore(CompAbilityUserMagic comp)
        {
            if (comp?.Pawn == null || !ClassIdentity.TryGetState(comp.Pawn, out var state)) return false;
            var path = state.Find("rwom.empath");
            if ((state.Vocation & Vocation.Mage) != 0 && path?.Domain == DisciplineDomain.Magic
                && path.Stage >= InitiationStage.Foundation && path.Stage <= InitiationStage.Ascended
                && ClassIdentity.Profile("rwom.empath")?.domain == DisciplineDomain.Magic
                && TorannMagicDefOf.TM_EmpathHD != null && comp.Pawn.health?.hediffSet != null
                && !comp.Pawn.health.hediffSet.HasHediff(TorannMagicDefOf.TM_EmpathHD)
                && EmpathOwners.Resolve(comp.MagicData)?.Any(p => p.learned && p.TMabilityDefs.Contains(TorannMagicDefOf.TM_Empathy)) == true)
                HealthUtility.AdjustSeverity(comp.Pawn, TorannMagicDefOf.TM_EmpathHD, 1f);
            return true;
        }
    }
}
