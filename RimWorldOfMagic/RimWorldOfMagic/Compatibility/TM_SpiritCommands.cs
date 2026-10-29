using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace TorannMagic.Compatibility
{
    public static class TM_SpiritCommands
    {
        private sealed class LeaveBodyCommand : Command_Action
        {
            public Pawn Host;
        }

        public static HediffComp_SpiritPossession Possession(Pawn pawn)
        {
            // Match the exact hediff consumed by RemovePossession, not a trait or
            // a class-data marker that may survive after possession has ended.
            Hediff hediff = pawn?.health?.hediffSet?.GetFirstHediffOfDef(TorannMagicDefOf.TM_SpiritPossessionHD);
            HediffComp_SpiritPossession possession = hediff?.TryGetComp<HediffComp_SpiritPossession>();
            Pawn spirit = possession?.SpiritPawn;
            return spirit != null && !spirit.Destroyed && spirit != pawn && possession.parent?.pawn == pawn
                && possession.GetDirectlyHeldThings()?.Contains(spirit) == true ? possession : null;
        }

        public static bool CanLeaveBody(Pawn pawn)
        {
            return pawn != null && pawn.Spawned && !pawn.Destroyed && pawn.Faction?.IsPlayer == true
                && Find.Selector?.NumSelected == 1 && Find.Selector.SingleSelectedThing == pawn
                && Possession(pawn) != null;
        }

        public static void LeaveBody(Pawn pawn)
        {
            // Gizmos can outlive a selection or possession change. Never release
            // an unrelated/missing spirit or assume an animal has a drafter.
            if (!CanLeaveBody(pawn)) return;
            if (pawn.drafter != null) pawn.drafter.Drafted = false;
            TM_Action.RemovePossession(pawn, pawn.Position, false);
        }

        public static IEnumerable<Gizmo> AppendLeaveBody(IEnumerable<Gizmo> gizmos, Pawn pawn)
        {
            if (gizmos == null || !CanLeaveBody(pawn)) return gizmos;
            List<Gizmo> result = gizmos.ToList();
            if (result.OfType<LeaveBodyCommand>().Any(command => command.Host == pawn)) return result;
            result.Add(new LeaveBodyCommand
            {
                Host = pawn,
                action = () => LeaveBody(pawn),
                Order = 49,
                defaultLabel = TM_TextPool.TM_RemovePossess,
                defaultDesc = TM_TextPool.TM_RemovePossessDesc,
                icon = ContentFinder<Texture2D>.Get("UI/remove_spiritpossession", true)
            });
            return result;
        }
    }
}
