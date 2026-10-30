using System.Reflection;
using System.Runtime.CompilerServices;
using RimWorld;
using TorannMagic;
using TorannMagic.ClassProgression;
using Verse;
using Verse.AI;
using static RWoM.RegressionTests.GrimoireProgressionTests;

namespace RWoM.RegressionTests;

internal static class SpiritRebirthRitualJobTests
{
    static Type Production(string name) => Check.NotNull(typeof(SpiritRebirthState).Assembly.GetType("TorannMagic.ClassProgression." + name), "internal ordered ritual " + name + " exists");
    static object? Call(object target, string name, params object[] args)
    {
        try { return target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args); }
        catch (TargetInvocationException e) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException!).Throw(); throw; }
    }
    public static IEnumerable<TestCase> Cases()
    {
        yield return new("SpiritRitualJob.NativePostSplitCallbackException", r => SplitCallback(r, true));
        yield return new("SpiritRitualJob.NativePreAbsorbCallbackException", r => SplitCallback(r, false));
        yield return new("SpiritRitualJob.WholeSourceNativeDespawnException", r => SplitCallback(r, false, true));
        yield return new("SpiritRitualJob.CompetingReservationNotOverridden", r => SpiritRebirthRitualTransactionTests.Run(r, c =>
        {
            using var setup = new Fixture(c);
            c.Job.def.driverClass = typeof(JobDriver_Wait);
            var other = PawnWithComps(false, false).pawn;
            Set(other, "mapIndexOrState", (sbyte)0); Set(other, "factionInt", c.Creator.Faction);
            Check.Equal(true, c.Map.spawnedThings.TryAdd(other, false), "native map owns competitor");
            var competingJob = JobMaker.MakeJob(c.Job.def);
            Check.Equal(true, c.Map.reservationManager.Reserve(other, competingJob, c.Circle), "competitor reserves circle first");
            var job = (Job)Production("SpiritRebirthRitualJob").GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [c.Creator, c.Circle, c.Corpse])!;
            job.playerForced = true;
            Check.Equal(false, job.TryMakePreToilReservations(c.Creator, false), "forced order cannot override ordinary competing reservation");
            Check.Equal(true, c.Map.reservationManager.ReservationsReadOnly.Any(x => x.Job == competingJob && x.Claimant == other && x.Target.Thing == c.Circle), "competing claim retained");
            Check.Equal(false, c.Map.reservationManager.ReservationsReadOnly.Any(x => x.Job == job), "rejected plan leaves no partial claims");
            RuntimeLog.AssertClean();
        }));
        yield return new("SpiritRitualJob.OrderedPreReservationBeforeCurrentJob", r => SpiritRebirthRitualTransactionTests.Run(r, c =>
        {
            using var setup = new Fixture(c);
            c.Job.def.driverClass = typeof(JobDriver_Wait);
            var job = (Job)Production("SpiritRebirthRitualJob").GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [c.Creator, c.Circle, c.Corpse])!;
            Check.Same(c.Job, c.Creator.CurJob, "request still has prior current job");
            Check.Equal(true, job.TryMakePreToilReservations(c.Creator, false), "native ordered-job pre-reservation works before current-job installation");
            var driver = job.GetCachedDriver(c.Creator);
            var staging = driver.GetType().GetProperty("Staging", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(driver)!;
            Check.Equal(false, (bool)Call(staging, "Pickup", c.Creator, job, 0)!, "non-current reserved job cannot execute hauling");
            Set(c.Creator.jobs, "curJob", job);
            var current = job.MakeDriver(c.Creator);
            Check.Equal(false, ReferenceEquals(driver, current), "native start constructs a fresh driver");
            Check.Equal(true, current.TryMakePreToilReservations(false), "fresh current driver replaces provisional claims with execution manifest");
            RuntimeLog.AssertClean();
        }));
        yield return new("SpiritRitualJob.KnownSplitRefusalAndOccupiedCell", r => SpiritRebirthRitualTransactionTests.Run(r, c =>
        {
            using var setup = new Fixture(c);
            c.Magicyte.stackCount = 420; c.Silver.stackCount = 80;
            var (job, staging) = Start(c);
            refusedOwner = c.Creator.carryTracker.GetDirectlyHeldThings();
            try
            {
                using var fault = new Fault();
                Check.Equal(false, (bool)Call(staging, "Pickup", c.Creator, job, 0)!, "injected native carry rejection fails pickup");
                Check.Equal(420, c.Magicyte.stackCount, "known split reabsorbed into exact unchanged source");
                Check.Equal(null, c.Creator.carryTracker.CarriedThing, "no orphan in carry");
            }
            finally { refusedOwner = null; }
            Check.Equal(true, (bool)Call(staging, "Pickup", c.Creator, job, 0)!, "retry performs one exact pickup");
            var carried = c.Creator.carryTracker.CarriedThing;
            Check.Equal(true, (bool)Call(staging, "Pickup", c.Creator, job, 0)!, "duplicate pickup observes actual same owner");
            Check.Same(carried, c.Creator.carryTracker.CarriedThing, "duplicate cannot split another object");
            var cell = (IntVec3)Call(staging, "CellAt", 0)!;
            var blocker = ThingMaker.MakeThing(c.Silver.def); blocker.stackCount = 1;
            GenSpawn.Spawn(blocker, cell, c.Map);
            Check.Equal(false, (bool)Call(staging, "Drop", c.Creator, job, 0)!, "new native occupant rejects drop before merging or wiping");
            Check.Same(carried, c.Creator.carryTracker.CarriedThing, "blocked drop preserves native carry custody");
            Check.Same(c.Map.spawnedThings, blocker.holdingOwner, "blocker remains untouched");
            Check.Equal(320, c.Magicyte.stackCount, "one pickup only");
            RuntimeLog.AssertClean();
        }));
        yield return new("SpiritRitualJob.NativeDiscoveryReservationAndSplitStaging", r =>
        {
            Production("JobDriver_SpiritRebirth");
            SpiritRebirthRitualTransactionTests.Run(r, c =>
            {
                using var setup = new Fixture(c);
                c.Magicyte.stackCount = 420; c.Silver.stackCount = 80;
                var job = (Job)Production("SpiritRebirthRitualJob").GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [c.Creator, c.Circle, c.Corpse])!;
                Check.NotNull(job, "internal factory creates unstarted native job");
                Set(c.Creator.jobs, "curJob", job);
                var driver = job.MakeDriver(c.Creator);
                Check.Equal(true, driver.TryMakePreToilReservations(false), "native region discovery and exact reservations succeed");
                var staging = driver.GetType().GetProperty("Staging", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(driver)!;
                int trips = (int)staging.GetType().GetProperty("TripCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(staging)!;
                Check.Equal(5, trips, "100 native carry capacity plans three magicyte trips, silver and corpse");
                for (int i = 0; i < trips; i++)
                {
                    Check.Equal(true, (bool)Call(staging, "Pickup", c.Creator, job, i)!, "known native split enters exact carry owner");
                    Check.Equal(true, (bool)Call(staging, "Drop", c.Creator, job, i)!, "native whole-object drop stages exact owner");
                }
                var stacks = (List<ThingCountClass>)Call(staging, "StagedMaterials")!;
                Check.Equal(300, stacks.Where(s => s.thing.def == c.Magicyte.def).Sum(s => s.Count), "exact magicyte staged");
                Check.Equal(50, stacks.Where(s => s.thing.def == c.Silver.def).Sum(s => s.Count), "exact silver staged");
                Check.Equal(120, c.Magicyte.stackCount, "same native oversized source keeps surplus");
                Check.Equal(30, c.Silver.stackCount, "same native silver source keeps surplus");
                Check.Equal(stacks.Count, stacks.Select(s => s.thing.Position).Distinct().Count(), "no merged destination stacks");
                foreach (var stack in stacks)
                {
                    Check.Equal(stack.Count, stack.thing.stackCount, "staged entry is complete actual stack");
                    Check.Same(c.Map.spawnedThings, stack.thing.holdingOwner, "native map custody for each staged object");
                }
                Check.Same(c.Original, c.Corpse.InnerPawn, "corpse still owns exact original before work");
                Check.Equal(true, c.Original.Dead, "hauling cannot resurrect or pay");
                RuntimeLog.AssertClean();
            });
        });
    }
    static void SplitCallback(RuntimeFixture r, bool postSplit, bool whole = false)
    {
        SpiritRebirthRitualTransactionTests.Run(r, c =>
        {
            using var setup = new Fixture(c);
            using var productionPatches = new FleshStatTests.ProductionPatches();
            SpiritRebirthActionTests.Patch(productionPatches, "SpiritRebirthNativeSplitCapturePatch");
            c.Magicyte.stackCount = 420;
            var old = whole ? c.Silver : c.Magicyte; var position = old.Position; old.DeSpawn();
            old.def.thingClass = typeof(ThingWithComps);
            old.def.comps = [new CompProperties { compClass = typeof(ThrowingHaulComp) }];
            var source = ThingMaker.MakeThing(old.def); source.stackCount = whole ? 50 : 420;
            if (whole) c.Silver = source; else c.Magicyte = source;
            GenSpawn.Spawn(source, position, c.Map);
            var (job, staging) = Start(c);
            ThrowingHaulComp.source = source; ThrowingHaulComp.split = null;
            ThrowingHaulComp.throwSplit = postSplit;
            ThrowingHaulComp.throwAbsorb = true;
            ThrowingHaulComp.throwDespawn = whole;
            try
            {
                refusedOwner = postSplit ? null : c.Creator.carryTracker.GetDirectlyHeldThings();
                using var fault = new Fault();
                Check.Equal(false, (bool)Call(staging, "Pickup", c.Creator, job, whole ? 3 : 0)!, "native comp exception cannot report successful pickup");
                var split = Check.NotNull(ThrowingHaulComp.split, "native PostSplitOff observed actual base split");
                Check.Same(c.Creator.inventory.innerContainer, split.holdingOwner, "exact native split survives in creator inventory after recovery callback throws");
                Check.Equal(true, c.Creator.inventory.innerContainer.Contains(split), "fallback ownership reciprocal");
                if (whole) { Check.Same(source, split, "whole-source fallback retains exact source object"); Check.Equal(50, split.stackCount, "whole source count preserved"); }
                else Check.Equal(420, c.Magicyte.stackCount + split.stackCount, "native total conserved without replacement objects");
                Check.Equal(null, Production("SpiritRebirthSplitCapture").GetField("current", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null), "exception releases scoped capture reference");
                var problems = (List<string>)typeof(RuntimeLog).GetField("Problems", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                Check.Equal(true, problems.Count > 0 && problems.All(p => p.Contains("injected ritual comp")), "only injected comp diagnostics");
                RuntimeLog.Clear();
            }
            finally { refusedOwner = null; ThrowingHaulComp.source = null; ThrowingHaulComp.split = null; ThrowingHaulComp.throwSplit = ThrowingHaulComp.throwAbsorb = ThrowingHaulComp.throwDespawn = false; }
        });
    }
    public sealed class ThrowingHaulComp : ThingComp
    {
        internal static Thing? source, split;
        internal static bool throwSplit, throwAbsorb, throwDespawn;
        public override void PostDeSpawn(Map map, DestroyMode mode)
        {
            if (!ReferenceEquals(parent, source) || !throwDespawn) return;
            split = parent;
            throw new InvalidOperationException("injected ritual comp PostDeSpawn");
        }
        public override void PostSplitOff(Thing piece)
        {
            if (!ReferenceEquals(parent, source)) return;
            split = piece;
            if (throwSplit) throw new InvalidOperationException("injected ritual comp PostSplitOff");
        }
        public override void PreAbsorbStack(Thing other, int count)
        {
            if (ReferenceEquals(parent, source) && throwAbsorb) throw new InvalidOperationException("injected ritual comp PreAbsorbStack");
        }
    }
    static (Job, object) Start(SpiritRebirthRitualTransactionTests.Context c)
    {
        var job = (Job)Production("SpiritRebirthRitualJob").GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [c.Creator, c.Circle, c.Corpse])!;
        Set(c.Creator.jobs, "curJob", job);
        var driver = job.MakeDriver(c.Creator);
        Check.Equal(true, driver.TryMakePreToilReservations(false), "native reservations acquired");
        return (job, driver.GetType().GetProperty("Staging", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(driver)!);
    }
    static ThingOwner? refusedOwner;
    static bool RefuseCarry(ThingOwner __instance, ref bool __result)
    {
        if (!ReferenceEquals(__instance, refusedOwner)) return true;
        __result = false; return false;
    }
    sealed class Fault : IDisposable
    {
        readonly object harmony; readonly string id = "rwom.ritual-haul-fault." + Guid.NewGuid();
        public Fault()
        {
            var assembly = Assembly.Load("0Harmony"); harmony = Activator.CreateInstance(assembly.GetType("HarmonyLib.Harmony")!, id)!;
            var method = Activator.CreateInstance(assembly.GetType("HarmonyLib.HarmonyMethod")!, typeof(SpiritRebirthRitualJobTests).GetMethod(nameof(RefuseCarry), BindingFlags.NonPublic | BindingFlags.Static)!)!;
            harmony.GetType().GetMethod("Patch")!.Invoke(harmony, [typeof(ThingOwner<Thing>).GetMethod("TryAdd", [typeof(Thing), typeof(bool)])!, method, null, null, null]);
        }
        public void Dispose() => harmony.GetType().GetMethod("UnpatchAll")!.Invoke(harmony, [id]);
    }
    sealed class Fixture : IDisposable
    {
        readonly JobDef[] jobs = DefDatabase<JobDef>.AllDefsListForReading.ToArray();
        readonly StatDef oldCarry = StatDefOf.CarryingCapacity;
        readonly FieldInfo mainThread = typeof(UnityData).GetField("mainThreadId", BindingFlags.Static | BindingFlags.NonPublic)!;
        readonly object? priorThread;
        public Fixture(SpiritRebirthRitualTransactionTests.Context c)
        {
            priorThread = mainThread.GetValue(null); mainThread.SetValue(null, Environment.CurrentManagedThreadId);
            DefDatabase<JobDef>.Add(new JobDef { defName = "TM_SpiritRebirth", driverClass = Production("JobDriver_SpiritRebirth") });
            StatDefOf.CarryingCapacity = new StatDef { defName = "RitualCarryCapacity", defaultBaseValue = 100f, minValue = 0, maxValue = 1000, workerClass = typeof(StatWorker) };
            c.Creator.ageTracker = new Pawn_AgeTracker(c.Creator);
            c.Creator.def.race.lifeStageAges = [new LifeStageAge { def = new LifeStageDef { developmentalStage = DevelopmentalStage.Adult } }];
            Set(c.Creator.ageTracker, "cachedLifeStageIndex", 0);
            var map = c.Map;
            map.resourceCounter = new ResourceCounter(map); map.fogGrid = new FogGrid(map);
            map.reachability = new Reachability(map);
            var region = Region.MakeNewUnfilled(new IntVec3(0, 0, 0), map);
            region.extentsClose = region.extentsLimit = new CellRect(0, 0, 30, 30);
            var grid = (Region[])typeof(RegionGrid).GetField("regionGrid", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(map.regionGrid)!;
            for (int i = 0; i < grid.Length; i++) grid[i] = region;
            foreach (var item in new[] { c.Magicyte, c.Silver })
            {
                map.listerThings.Remove(item); item.def.alwaysHaulable = true; map.listerThings.Add(item);
                region.ListerThings.Add(item);
            }
            c.Corpse.def.thingClass = typeof(Corpse); c.Corpse.def.comps ??= []; c.Corpse.def.size = new IntVec2(1, 1);
            c.Corpse.def.drawerType = DrawerType.RealtimeOnly;
            Check.Equal(true, c.Creator.carryTracker.GetDirectlyHeldThings().TryDrop(c.Corpse, new IntVec3(16, 0, 10), map, ThingPlaceMode.Direct, out _, playDropSound: false), "native corpse staged as source with empty creator carry");
            region.ListerThings.Add(c.Corpse);
            map.reservationManager.ReleaseClaimedBy(c.Creator, c.Job);
        }
        public void Dispose()
        {
            DefDatabase<JobDef>.Clear(); foreach (var d in jobs) DefDatabase<JobDef>.Add(d);
            StatDefOf.CarryingCapacity = oldCarry; mainThread.SetValue(null, priorThread);
        }
    }
}
