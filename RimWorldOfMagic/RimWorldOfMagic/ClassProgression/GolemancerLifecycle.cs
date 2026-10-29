using System.Linq;
using Verse;

namespace TorannMagic.ClassProgression
{
    public static class GolemancerLifecycle
    {
        public static MagicPower GetLearnedNativeOwner(CompAbilityUserMagic comp, TMAbilityDef ability)
        {
            if (comp?.Pawn == null || ability == null) return null;
            var owner = ClassIdentity.TryGetState(comp.Pawn, out _)
                ? comp.MagicData.MagicPowersGolemancer.FirstOrDefault(p => p.TMabilityDefs.Contains(ability))
                : comp.MagicData.ReturnMatchingMagicPower(ability);
            return owner?.learned == true ? owner : null;
        }

        public static bool Restore(CompAbilityUserMagic comp)
        {
            if (comp?.Pawn == null || !ClassIdentity.TryGetState(comp.Pawn, out _)) return false;
            var pawns = comp.BrandPawns;
            var defs = comp.BrandDefs;
            // Keep native pairs together. Never infer a missing association or alter
            // another caster's hediff when a saved pair is stale after transfer.
            int paired = System.Math.Min(pawns.Count, defs.Count);
            if (pawns.Count > paired) pawns.RemoveRange(paired, pawns.Count - paired);
            if (defs.Count > paired) defs.RemoveRange(paired, defs.Count - paired);
            for (int i = paired - 1; i >= 0; i--)
            {
                var pawn = pawns[i];
                var source = defs[i] == null ? null : pawn?.health?.hediffSet?.GetFirstHediffOfDef(defs[i])?.TryGetComp<HediffComp_BrandingBase>();
                if (pawn == null || pawn.Destroyed || pawn.Dead || defs[i] == null || source?.BranderPawn != comp.Pawn)
                { pawns.RemoveAt(i); defs.RemoveAt(i); }
            }
            SetControl(comp, TorannMagicDefOf.TM_DispelBranding, pawns.Count > 0);
            if (comp.livingWall != null && comp.livingWall.Destroyed) comp.livingWall = null;
            SetControl(comp, TorannMagicDefOf.TM_DispelLivingWall,
                comp.livingWall != null && comp.livingWall.CasterPawn == comp.Pawn);
            return true;
        }

        static void SetControl(CompAbilityUserMagic comp, TMAbilityDef ability, bool active)
        {
            if (ability == null) return;
            if (active) TM_ClassState.AddRuntimeAbility(comp, ability);
            else comp.RemovePawnAbility(ability);
        }
    }
}
