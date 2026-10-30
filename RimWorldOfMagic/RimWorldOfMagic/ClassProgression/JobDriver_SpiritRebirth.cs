using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace TorannMagic.ClassProgression
{
    public sealed class JobDriver_SpiritRebirth : JobDriver
    {
        private SpiritRebirthStaging staging;
        private int tripIndex;
        internal SpiritRebirthStaging Staging => staging;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (staging == null)
            {
                // Ordered jobs reserve using a cached driver, but StartJob
                // constructs a fresh driver. Discard only this job's provisional
                // claims before selecting its execution-time frozen manifest.
                if (pawn.CurJob == job) pawn.Map?.reservationManager.ReleaseClaimedBy(pawn, job);
                staging = SpiritRebirthStaging.Discover(pawn, job, job.targetA.Thing, job.targetB.Thing as Corpse);
            }
            return staging != null && staging.Reserve(pawn, job);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            var select = ToilMaker.MakeToil("SpiritRitualSelectTrip");
            var work = ToilMaker.MakeToil("SpiritRitualWork");
            select.initAction = () => {
                if (staging == null) { EndJobWith(JobCondition.Incompletable); return; }
                if (tripIndex >= staging.TripCount) { JumpToToil(work); return; }
                job.SetTarget(TargetIndex.B, staging.SourceAt(tripIndex)); job.SetTarget(TargetIndex.C, staging.CellAt(tripIndex));
            };
            select.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return select;
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);
            var pickup = ToilMaker.MakeToil("SpiritRitualKnownPickup"); pickup.defaultCompleteMode = ToilCompleteMode.Instant;
            pickup.initAction = () => { if (!staging.Pickup(pawn, job, tripIndex)) EndJobWith(JobCondition.Incompletable); };
            yield return pickup;
            yield return Toils_Goto.GotoCell(TargetIndex.C, PathEndMode.Touch);
            var drop = ToilMaker.MakeToil("SpiritRitualExactDrop"); drop.defaultCompleteMode = ToilCompleteMode.Instant;
            drop.initAction = () => { if (staging.Drop(pawn, job, tripIndex)) tripIndex++; else EndJobWith(JobCondition.Incompletable); };
            yield return drop;
            yield return Toils_Jump.Jump(select);
            work.defaultCompleteMode = ToilCompleteMode.Never;
            // Work/payment is the next separately verified slice. Collection
            // alone never marks success or invokes irreversible conversion.
            work.initAction = () => EndJobWith(JobCondition.Incompletable);
            yield return work;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref staging, "spiritStaging"); Scribe_Values.Look(ref tripIndex, "spiritTripIndex");
        }
    }
}
