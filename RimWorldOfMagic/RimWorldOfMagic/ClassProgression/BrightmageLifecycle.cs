using Verse;
using RimWorld;

namespace TorannMagic.ClassProgression
{
    public static class BrightmageLifecycle
    {
        // True means new-model maintenance handled this pawn; classic paths remain native.
        public static bool Restore(CompAbilityUserMagic comp)
        {
            if (comp?.Pawn == null || !ClassIdentity.TryGetState(comp.Pawn, out var state)) return false;
            var path = state.Find("rwom.brightmage");
            if ((state.Vocation & Vocation.Mage) != 0 && path?.Domain == DisciplineDomain.Magic
                && path.Stage >= InitiationStage.Foundation && path.Stage <= InitiationStage.Ascended
                && ClassIdentity.Profile("rwom.brightmage")?.domain == DisciplineDomain.Magic
                && TorannMagicDefOf.TM_LightCapacitanceHD != null
                && comp.Pawn.health?.hediffSet != null
                && !comp.Pawn.health.hediffSet.HasHediff(TorannMagicDefOf.TM_LightCapacitanceHD))
            {
                HealthUtility.AdjustSeverity(comp.Pawn, TorannMagicDefOf.TM_LightCapacitanceHD, 1f);
            }

            // A saved entity owns this control even after class changes or missing profiles.
            // A despawned entity may be in transit; only destroyed entities are discarded.
            if (comp.SoL != null && comp.SoL.Destroyed) comp.SoL = null;
            if (comp.SoL != null)
            {
                TM_ClassState.AddRuntimeAbility(comp, TorannMagicDefOf.TM_SoL_Equalize);
                comp.spell_EqualizeLight = true;
            }
            else
            {
                comp.RemovePawnAbility(TorannMagicDefOf.TM_SoL_Equalize);
                comp.spell_EqualizeLight = false;
            }
            return true;
        }
    }
}
