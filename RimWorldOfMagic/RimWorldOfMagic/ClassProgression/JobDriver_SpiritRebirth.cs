using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace TorannMagic.ClassProgression
{
    public sealed class JobDriver_SpiritRebirth : JobDriver
    {
        private SpiritRebirthStaging staging;
        private int tripIndex;
        private int workTicks, lastWorkTick = -1;
        private bool completionStarted;
        internal SpiritRebirthStaging Staging => staging;
        internal int WorkTicks => workTicks;

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
            var approach = ToilMaker.MakeToil("SpiritRitualApproachCircle");
            AddFinishAction(condition => SecurePendingOriginal());
            select.initAction = () => {
                if (staging == null) { EndJobWith(JobCondition.Incompletable); return; }
                if (tripIndex >= staging.TripCount) { JumpToToil(approach); return; }
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
            approach.defaultCompleteMode = ToilCompleteMode.Instant;
            approach.initAction = () => job.SetTarget(TargetIndex.C, staging.Circle.InteractionCell);
            yield return approach;
            yield return Toils_Goto.GotoCell(TargetIndex.C, PathEndMode.OnCell);
            work.defaultCompleteMode = ToilCompleteMode.Never;
            work.tickAction = ObserveWork;
            yield return work;
        }

        private void ObserveWork()
        {
            if (completionStarted) { FinishExistingReceipt(); return; }
            if (staging == null || workTicks < 0 || workTicks > SpiritRebirthRitualEligibility.RitualTicks || !staging.WorkValid(pawn, job))
            { EndJobWith(JobCondition.Incompletable); return; }
            int now = Find.TickManager.TicksGame;
            if (now <= lastWorkTick) return;
            lastWorkTick = now;
            if (++workTicks < SpiritRebirthRitualEligibility.RitualTicks) return;
            completionStarted = true;
            SpiritRebirthRitualTransaction.Execute(pawn, staging.Circle, staging.Corpse, staging.Original, job, staging.StagedMaterials(), workTicks);
            FinishExistingReceipt();
        }

        private void FinishExistingReceipt()
        {
            var original = staging?.Original;
            var receipt = ClassIdentity.Component(original)?.SpiritRitual;
            bool released = receipt != null && receipt.Paid && receipt.Converted
                && receipt.TryRelease(pawn, pawn.Map, staging.ReleaseCell) == SpiritReleaseResult.Released
                && SpiritRebirthFormPolicy.IsSpawnedEthereal(original);
            EndJobWith(released ? JobCondition.Succeeded : JobCondition.Incompletable);
        }

        private void SecurePendingOriginal()
        {
            var receipt = ClassIdentity.Component(staging?.Original)?.SpiritRitual;
            if (receipt != null && receipt.Attempted && !SpiritRebirthFormPolicy.IsSpawnedEthereal(staging.Original))
                receipt.TryRetainReleaseForCleanup(pawn);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref staging, "spiritStaging"); Scribe_Values.Look(ref tripIndex, "spiritTripIndex");
            Scribe_Values.Look(ref workTicks, "spiritWorkTicks"); Scribe_Values.Look(ref lastWorkTick, "spiritLastWorkTick", -1);
            Scribe_Values.Look(ref completionStarted, "spiritCompletionStarted");
        }
    }
}
