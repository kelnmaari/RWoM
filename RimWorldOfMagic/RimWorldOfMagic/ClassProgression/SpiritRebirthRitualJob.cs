using Verse;
using Verse.AI;

namespace TorannMagic.ClassProgression
{
    internal static class SpiritRebirthRitualJob
    {
        internal const string DefinitionName = "TM_SpiritRebirth";
        internal static Job Create(Pawn creator, Building_TMMagicCircle circle, Corpse corpse)
        {
            var def = DefDatabase<JobDef>.GetNamedSilentFail(DefinitionName);
            if (def?.driverClass != typeof(JobDriver_SpiritRebirth) || creator?.carryTracker?.CarriedThing != null
                || SpiritRebirthRitualEligibility.Rejection(creator, circle, corpse, corpse?.InnerPawn) != SpiritRebirthRitualRejection.None)
                return null;
            return JobMaker.MakeJob(def, circle, corpse);
        }
    }
}
