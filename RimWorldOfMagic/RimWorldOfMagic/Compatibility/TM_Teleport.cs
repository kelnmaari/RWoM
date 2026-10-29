using Verse;

namespace TorannMagic.Compatibility
{
    /// <summary>Same-map relocation without despawning, changing selection or moving the camera.</summary>
    public static class TM_Teleport
    {
        public static bool CanMove(Pawn pawn, IntVec3 destination)
        {
            Map map = pawn?.Map;
            return pawn != null && pawn.Spawned && !pawn.Destroyed && !pawn.Dead && map != null
                && destination.IsValid && destination != pawn.Position && destination.InBounds(map)
                && destination.Standable(map);
        }

        public static bool Move(Pawn pawn, IntVec3 destination)
        {
            // Repeat destination validation at completion, not just during target selection.
            if (!CanMove(pawn, destination)) return false;
            pawn.Position = destination;
            pawn.Notify_Teleported(endCurrentJob: false);
            return true;
        }
    }
}
