using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace TorannMagic.Odyssey
{
    public class JobDriver_LoadSynthChemfuel : JobDriver
    {
        private CompMagofuelReservoir Bank => (TargetB.Thing as ThingWithComps)?.GetComp<CompMagofuelReservoir>();

        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(TargetB, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.B);
            this.FailOn(() => Bank == null || Bank.ChemfuelHopper >= 100f);

            // Find the nearest reachable chemfuel stack.
            var chemStack = FindChemfuelStack();
            if (chemStack == null)
            {
                // No chemfuel reachable: end without consuming anything.
                yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.InteractionCell);
                yield return Toils_General.Wait(120);
                yield break;
            }

            yield return Toils_Reserve.Reserve(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);

            var take = new Toil
            {
                initAction = () =>
                {
                    var stack = TargetA.Thing;
                    var bank = Bank;
                    if (stack == null || stack.Destroyed || bank == null) { EndJobWith(JobCondition.Incompletable); return; }
                    int space = (int)(100f - bank.ChemfuelHopper);
                    int count = Math.Min(stack.stackCount, space);
                    if (count <= 0) { EndJobWith(JobCondition.Succeeded); return; }
                    var carried = stack.SplitOff(count);
                    pawn.carryTracker.TryStartCarry(carried);
                }
            };
            yield return take;

            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.InteractionCell);

            var deposit = new Toil
            {
                initAction = () =>
                {
                    var carried = pawn.carryTracker.CarriedThing;
                    var bank = Bank;
                    if (carried == null || bank == null || carried.def != ThingDefOf.Chemfuel)
                    {
                        // Return chemfuel to the map so it is not lost.
                        if (carried != null) GenPlace.TryPlaceThing(carried, pawn.Position, pawn.Map, ThingPlaceMode.Near);
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }
                    bank.AddChemfuelToHopper(carried.stackCount);
                    carried.Destroy(DestroyMode.Vanish);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
            yield return deposit;
        }

        private Thing FindChemfuelStack()
        {
            if (pawn.Map == null) return null;
            Thing best = null;
            float bestDist = float.MaxValue;
            foreach (var stack in pawn.Map.listerThings.ThingsOfDef(ThingDefOf.Chemfuel))
            {
                if (stack.IsForbidden(pawn) || !pawn.CanReserve(stack, 1, -1, null, false)) continue;
                if (!pawn.Map.reachability.CanReach(pawn.Position, stack, PathEndMode.ClosestTouch, TraverseParms.For(pawn, Danger.Deadly))) continue;
                var d = pawn.Position.DistanceToSquared(stack.Position);
                if (d < bestDist) { bestDist = d; best = stack; }
            }
            return best;
        }
    }
}
