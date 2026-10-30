using System.Linq;

namespace TorannMagic.ClassProgression
{
    public static class ShamanLifecycle
    {
        public static bool Restore(CompAbilityUserMagic comp)
        {
            if (SpiritRebirthBodyEffects.SuppressFormerBodyEffects(comp?.Pawn)) return true;
            if (comp?.Pawn == null || !ClassIdentity.TryGetState(comp.Pawn, out _)) return false;
            var controls = new[] { TorannMagicDefOf.TM_Hex_Pain, TorannMagicDefOf.TM_Hex_MentalAssault, TorannMagicDefOf.TM_Hex_CriticalFail };
            var owner = comp.MagicData.MagicPowersShaman.FirstOrDefault(p => p.TMabilityDefs.Contains(TorannMagicDefOf.TM_Hex));
            // Native copied/item Hex can create these controls without permanent
            // ownership. Preserve that runtime proof while its actual targets remain.
            bool hasRuntimeSource = comp.AbilityData.Powers.Any(p => p != null
                && (controls.Contains(p.Def) || owner?.TMabilityDefs.Contains(p.Def) == true));
            bool active = comp.HexedPawns.Count > 0 && (owner?.learned == true || hasRuntimeSource);
            foreach (var control in controls)
            {
                if (control == null) continue;
                if (active) TM_ClassState.AddRuntimeAbility(comp, control);
                else comp.RemovePawnAbility(control);
            }
            return true;
        }
    }
}
