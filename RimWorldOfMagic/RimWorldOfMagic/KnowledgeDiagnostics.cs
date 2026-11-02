using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TorannMagic
{
    // Anti-spam reporting for "sleeping knowledge": knowledge entries whose def
    // no longer resolves (spell removed or renamed). One summary line per pawn
    // per in-game day; never throws, never per-entry spam.
    public static class KnowledgeDiagnostics
    {
        private const int ReportIntervalTicks = 60000;
        private static readonly Dictionary<int, int> LastReportTick = new Dictionary<int, int>();

        public static void ReportSleeping(Pawn pawn, AbilityKnowledgeComp knowledge)
        {
            if (pawn == null || knowledge == null) return;
            int now = Find.TickManager?.TicksGame ?? 0;
            if (LastReportTick.TryGetValue(pawn.thingIDNumber, out int last) && now - last < ReportIntervalTicks) return;
            int sleeping = 0;
            foreach (SpellKnowledge spell in knowledge.Spells)
            {
                if (spell == null || !spell.learned) continue;
                if (!CoreBridge.TryResolve(spell.spellId, out _)) sleeping++;
            }
            if (sleeping > 0)
            {
                LastReportTick[pawn.thingIDNumber] = now;
                Log.Message("[RWoM] " + pawn.LabelShortCap + ": " + sleeping
                    + " known spell(s) currently have no matching def (sleeping knowledge).");
            }
        }
    }
}
