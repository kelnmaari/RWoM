using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace TorannMagic.Odyssey
{
    // Hauls chemfuel from ground/stockpile stacks into the magofuel reservoir's
    // synthesis hopper through the "Magic" work type. The hopper feeds the
    // auto-synthesis conversion (mana + chemfuel -> magofuel, 50/day).
    public class WorkGiver_LoadSynthChemfuel : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.ClosestTouch;
        public override Danger MaxPathDanger(Pawn pawn) => Danger.Deadly;

        private const float HopperLowThreshold = 50f;
        private const float HopperCapacity = 100f;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            if (pawn.Map == null) yield break;
            foreach (var building in pawn.Map.listerBuildings.allBuildingsColonist)
                if (building.def == DefDatabase<ThingDef>.GetNamedSilentFail("TM_MagofuelReservoir")) yield return building;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (thing.IsForbidden(pawn) || !pawn.CanReserve(thing, 1, -1, null, forced)) return false;
            var bank = (thing as ThingWithComps)?.GetComp<CompMagofuelReservoir>();
            if (bank == null || !MagofuelReserve.IsStorage(bank) || bank.ChemfuelHopper >= HopperCapacity) return false;
            if (bank.ChemfuelHopper > HopperLowThreshold && !forced) return false;
            return FindNearbyChemfuelStack(pawn, thing.Position) != null
                && pawn.CanReserveAndReach(thing, PathEndMode.ClosestTouch, Danger.Deadly);
        }

        public override Job JobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            var stack = FindNearbyChemfuelStack(pawn, thing.Position);
            if (stack == null) return null;
            var job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("TM_LoadSynthChemfuel"), stack, thing);
            job.count = Math.Min(stack.stackCount, (int)(HopperCapacity - HopperLowThreshold));
            return job;
        }

        private static Thing FindNearbyChemfuelStack(Pawn pawn, IntVec3 near)
        {
            Thing best = null;
            float bestDist = float.MaxValue;
            foreach (var stack in pawn.Map.listerThings.ThingsOfDef(ThingDefOf.Chemfuel))
            {
                if (stack.IsForbidden(pawn) || !pawn.CanReserve(stack, 1, -1, null, false)) continue;
                if (!pawn.Map.reachability.CanReach(pawn.Position, stack, PathEndMode.ClosestTouch, TraverseParms.For(pawn, Danger.Deadly))) continue;
                float dist = (stack.Position - near).LengthHorizontalSquared;
                if (dist < bestDist) { bestDist = dist; best = stack; }
            }
            return best;
        }
    }

    public class JobDriver_LoadSynthChemfuel : JobDriver
    {
        private CompMagofuelReservoir Bank => (TargetB.Thing as ThingWithComps)?.GetComp<CompMagofuelReservoir>();

        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(TargetA, job, 1, -1, null, errorOnFailed) &&
            pawn.Reserve(TargetB, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOnDespawnedNullOrForbidden(TargetIndex.B);
            this.FailOn(() => Bank == null || Bank.ChemfuelHopper >= 100f);

            // Walk to the chemfuel stack and take up to the free hopper space.
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);
            var take = new Toil
            {
                initAction = () =>
                {
                    var stack = TargetA.Thing;
                    var bank = Bank;
                    if (stack == null || bank == null) { EndJobWith(JobCondition.Incompletable); return; }
                    int space = (int)(100f - bank.ChemfuelHopper);
                    int take = Math.Min(stack.stackCount, space);
                    if (take <= 0) { EndJobWith(JobCondition.Succeeded); return; }
                    var carried = stack.SplitOff(take);
                    pawn.carryTracker.TryStartCarry(carried);
                }
            };
            yield return take;

            // Walk to the reservoir and deposit into the hopper.
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.InteractionCell);
            yield return new Toil
            {
                initAction = () =>
                {
                    var carried = pawn.carryTracker.CarriedThing;
                    var bank = Bank;
                    if (carried == null || bank == null || carried.def != ThingDefOf.Chemfuel) { EndJobWith(JobCondition.Incompletable); return; }
                    int count = carried.stackCount;
                    carried.Destroy(DestroyMode.Vanish);
                    // LoadChemfuel validates def; add directly to the hopper float.
                    bank.GetType().GetMethod("LoadChemfuel", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                        ?.Invoke(bank, new object[] { null, count });
                    // LoadChemfuel expects a stack; use the direct float instead.
                    bank.GetType().GetField("chemfuelHopper", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        ?.SetValue(bank, System.Math.Min(bank.ChemfuelHopper + count, 100f));
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }
}
