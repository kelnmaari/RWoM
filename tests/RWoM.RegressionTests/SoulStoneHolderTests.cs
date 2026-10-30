using System.Reflection;
using System.Runtime.CompilerServices;
using TorannMagic;
using TorannMagic.ClassProgression;
using Verse;
using RimWorld;
using static RWoM.RegressionTests.GrimoireProgressionTests;

namespace RWoM.RegressionTests;

internal static class SoulStoneHolderTests
{
    public static IEnumerable<TestCase> Cases()
    {
        yield return new("SoulStoneHolder.ExactNativeCaptureImplantAndRecovery", CaptureImplant);
        yield return new("SoulStoneHolder.RejectForeignUnmarkedOccupiedAndRetry", Rejections);
        yield return new("SoulStoneHolder.OccupiedDestroyStackAndSplitSafety", Safety);
        yield return new("SoulStoneHolder.NativeStonePawnDeepScribe", Save);
        yield return new("SoulStoneHolder.FailedStateCommitRestoresExactOwner", Rollback);
    }

    static bool Invoke(object target, string method, params object[] args)
        => (bool)target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args)!;
    static Thing_SoulStone Stone()
    {
        var def = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        def.defName = "RegressionSoulStone"; def.stackLimit = 1;
        def.thingClass = typeof(Thing_SoulStone); def.category = ThingCategory.Item;
        def.comps = new List<CompProperties>(); def.size = new IntVec2(1, 1);
        return new Thing_SoulStone { def = def, thingIDNumber = 67401 };
    }
    static HediffComp_SpiritPossession EmptyHost(SpiritRebirthStateTests.Fixture f, Pawn host)
    {
        var holder = f.Hold(host, f.Other);
        holder.GetDirectlyHeldThings().Remove(f.Other);
        return holder;
    }

    static void CaptureImplant(RuntimeFixture r)
    {
        using var f = new SpiritRebirthStateTests.Fixture(r);
        var state = f.Begin(); var native = f.Hold(f.Host, f.Original);
        Check.Equal(true, Invoke(state, "TryEnterHost", f.Original, f.Host), "native initial entry");
        var magic = f.Magic.MagicData; var might = f.Might.MightData; var identity = f.Original.GetUniqueLoadID();
        var stone = Stone();
        Check.Equal(true, Invoke(stone, "TryCapture", f.Original, f.Host, false), "capture transfers actual original");
        Check.Equal(0, native.GetDirectlyHeldThings().Count, "source empty");
        Check.Same(f.Original, stone.ContainedSpirit, "single same pawn");
        Check.Same(stone, f.Original.holdingOwner.Owner, "native owner points to stone");
        Check.Equal(true, state.HasValidStone(f.Original), "state agrees with actual custody");
        Check.Equal(false, Invoke(stone, "TryImplant", f.Other, native), "foreign expected soul denied");
        Check.Equal(true, Invoke(stone, "TryImplant", f.Original, native), "implant actual soul into native empty host");
        Check.Equal(true, stone.ContainedSpirit == null, "shell empty after commit");
        Check.Same(f.Original, native.SpiritPawn, "host owns actual pawn");
        Check.Equal(true, state.HasValidHost(f.Original), "native host authoritative");
        Check.Equal(false, state.CanVoluntarilyLeaveHost(f.Original), "week recovery gates voluntary exit");
        Check.Equal(true, Invoke(stone, "TryCapture", f.Original, f.Host, false), "forced depletion remains safe during recovery");
        Check.Same(stone.GetDirectlyHeldThings(), f.Original.holdingOwner, "forced depletion preserves native soul owner");
        Check.Equal(0, state.RecoveryTicks, "forced depletion does not bypass recovery");
        Check.Same(magic, f.Magic.MagicData, "magic data remains on original");
        Check.Same(might, f.Might.MightData, "might data remains on original");
        Check.Equal(identity, f.Original.GetUniqueLoadID(), "load identity unchanged");
    }

    static void Rejections(RuntimeFixture r)
    {
        using var f = new SpiritRebirthStateTests.Fixture(r); var stone = Stone();
        var native = f.Hold(f.Host, f.Original);
        Check.Equal(false, Invoke(stone, "TryCapture", f.Original, f.Host, false), "unmarked legacy spirit rejected");
        Check.Same(native.GetDirectlyHeldThings(), f.Original.holdingOwner, "legacy custody preserved");
        var state = f.Begin();
        Check.Equal(false, Invoke(stone, "TryCapture", f.Original, f.Host, false), "uncommitted native custody cannot act as ethereal");
        Check.Equal(true, Invoke(state, "TryEnterHost", f.Original, f.Host), "establish state");
        Check.Equal(false, Invoke(stone, "TryCapture", f.Original, f.Other, false), "foreign host rejected");
        Check.Equal(false, Invoke(stone, "TryCapture", f.Original, f.Host, true), "live host cannot claim death");
        Check.Equal(true, Invoke(stone, "TryCapture", f.Original, f.Host, false), "valid retry succeeds");
        Check.Equal(false, Invoke(stone, "TryCapture", f.Original, f.Host, false), "duplicate capture rejected");
        Check.Equal(false, Invoke(stone, "TryCapture", f.Other, f.Host, false), "second soul rejected");
        var badTarget = EmptyHost(f, f.Other); badTarget.parent.pawn = f.Host;
        Check.Equal(false, Invoke(stone, "TryImplant", f.Original, badTarget), "foreign hediff parent rejected");
        Check.Same(stone.GetDirectlyHeldThings(), f.Original.holdingOwner, "failed implant preserves stone owner");
        Check.Equal(true, Invoke(stone, "TryImplant", f.Original, native), "valid implant retry succeeds");
    }

    static void Safety(RuntimeFixture r)
    {
        using var f = new SpiritRebirthStateTests.Fixture(r); var state = f.Begin();
        f.Hold(f.Host, f.Original); Invoke(state, "TryEnterHost", f.Original, f.Host);
        var stone = Stone(); Invoke(stone, "TryCapture", f.Original, f.Host, false);
        foreach (var mode in new[] { DestroyMode.Vanish, DestroyMode.KillFinalize }) stone.Destroy(mode);
        Check.Equal(false, stone.Destroyed, "occupied stone survives destruction attempts");
        Check.Same(f.Original, stone.ContainedSpirit, "soul retained after repeated destruction");
        var empty = Stone();
        Check.Equal(false, stone.CanStackWith(empty), "occupied cannot stack");
        Check.Equal(false, empty.CanStackWith(stone), "reverse stack also denied");
        Check.Same(stone, stone.SplitOff(1), "native singleton movement keeps exact stone");
        stone.stackCount = 2;
        Check.Equal(true, stone.SplitOff(1) == null, "malformed occupied stack cannot split soul");
        Check.Same(f.Original, stone.ContainedSpirit, "malformed split keeps soul");
        stone.stackCount = 1;
        var carrier = new ThingOwner<Thing>();
        Check.Equal(true, carrier.TryAdd(stone, false), "native off-map inventory holds actual stone");
        Check.Same(stone.GetDirectlyHeldThings(), f.Original.holdingOwner, "nested inventory keeps soul owner");
        Check.Equal(true, state.HasValidStone(f.Original), "nested stone custody valid");
    }

    static void Save(RuntimeFixture r)
    {
        using var f = new SpiritRebirthStateTests.Fixture(r);
        var defs = DefDatabase<ThingDef>.AllDefsListForReading.ToArray();
        var capacities = DefDatabase<PawnCapacityDef>.AllDefsListForReading.ToArray();
        try
        {
            // Minimal native Pawn graph, as in CombatWarpod.FullHolderScribe;
            // serialize the actual Pawn and its actual progression comp, no envelope.
            var pawn = f.Original;
            pawn.def.thingClass = typeof(Pawn); pawn.def.category = ThingCategory.Pawn;
            pawn.def.race = new RaceProperties { body = new BodyDef { corePart = new BodyPartRecord { def = new BodyPartDef() } } };
            pawn.def.comps = new List<CompProperties> { new CompProperties { compClass = typeof(CompClassProgression) } };
            Set(pawn, "comps", new List<ThingComp> { f.Comp });
            pawn.story = null; pawn.needs = null; pawn.Name = null;
            f.Comp.BeginNewProgression(Vocation.WarriorMage);
            f.Comp.State.TryAddFoundation("rwom.necromancer", DisciplineDomain.Magic);
            f.Comp.DelayNextTomeQuest(1234);
            var state = f.Begin(); f.Hold(f.Host, pawn); Invoke(state, "TryEnterHost", pawn, f.Host);
            var stone = Stone(); Check.Equal(true, Invoke(stone, "TryCapture", pawn, f.Host, false), "save capture");
            if (capacities.Length == 0) DefDatabase<PawnCapacityDef>.Add(new PawnCapacityDef { defName = "SoulStoneFixtureCapacity" });
            DefDatabase<ThingDef>.Add(pawn.def); DefDatabase<ThingDef>.Add(stone.def);
            Set(Current.Game, "uniqueIDsManager", new UniqueIDsManager());
            var world = (RimWorld.Planet.World)RuntimeHelpers.GetUninitializedObject(typeof(RimWorld.Planet.World));
            world.factionManager = new FactionManager();
            Set(Current.Game, "worldInt", world);
            var path = Path.Combine(r.WorkingDirectory, "soul-stone-native-pawn.xml");
            try { Scribe.saver.InitSaving(path, "regression"); Scribe_Deep.Look(ref stone, "stone"); Scribe.saver.FinalizeSaving(); }
            finally { Scribe.ForceStop(); }
            Thing_SoulStone loaded = null!;
            try { Scribe.loader.InitLoading(path); Scribe_Deep.Look(ref loaded, "stone"); Scribe.loader.FinalizeLoading(); }
            finally { Scribe.ForceStop(); }
            var soul = Check.NotNull(loaded.ContainedSpirit, "native graph restores contained Pawn");
            Check.Equal(false, ReferenceEquals(pawn, soul), "loaded graph rebuilt from deep Pawn data");
            Check.Equal(pawn.GetUniqueLoadID(), soul.GetUniqueLoadID(), "native pawn ID survives");
            Check.Equal(1, loaded.GetDirectlyHeldThings().Count, "exactly one contained pawn");
            Check.Same(loaded, soul.holdingOwner.Owner, "loaded native owner is loaded stone");
            Check.Equal(true, SpiritRebirthState.TryGet(soul, out var restored), "original reference resolves to loaded Pawn");
            Check.Same(loaded, restored.Stone, "state reference resolves to actual loaded stone");
            Check.Equal(true, restored.HasValidStone(soul), "loaded state and holder reciprocal");
            Check.Equal(true, restored.RecoveryRequired, "recovery debt survives native save");
            Check.Equal(0, restored.RecoveryTicks, "stone never advances recovery");
            var comp = soul.GetComp<CompClassProgression>();
            Check.Equal(true, comp.State.Find("rwom.necromancer") != null, "class discipline survives on original comp");
            Check.Equal(f.Comp.TomeQuestAvailableAtTick, comp.TomeQuestAvailableAtTick, "progression investment remains owned by pawn");
            loaded.Destroy(); Check.Equal(false, loaded.Destroyed, "restored occupied shell remains protected");
            RuntimeLog.AssertClean();
        }
        finally
        {
            DefDatabase<ThingDef>.Clear(); foreach (var def in defs) DefDatabase<ThingDef>.Add(def);
            DefDatabase<PawnCapacityDef>.Clear(); foreach (var def in capacities) DefDatabase<PawnCapacityDef>.Add(def);
        }
    }

    static bool RejectCommit(ref bool __result) { __result = false; return false; }
    sealed class RejectedCommit : IDisposable
    {
        readonly object harmony;
        readonly string id = "rwom.soulstone.commit." + Guid.NewGuid();
        public RejectedCommit(string method)
        {
            var assembly = Assembly.Load("0Harmony");
            harmony = Activator.CreateInstance(assembly.GetType("HarmonyLib.Harmony")!, id)!;
            var target = typeof(SpiritRebirthState).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!;
            var patch = Activator.CreateInstance(assembly.GetType("HarmonyLib.HarmonyMethod")!,
                typeof(SoulStoneHolderTests).GetMethod(nameof(RejectCommit), BindingFlags.Static | BindingFlags.NonPublic)!)!;
            harmony.GetType().GetMethod("Patch")!.Invoke(harmony, [target, patch, null, null, null]);
        }
        public void Dispose() => harmony.GetType().GetMethod("UnpatchAll")!.Invoke(harmony, [id]);
    }

    static void Rollback(RuntimeFixture r)
    {
        using var f = new SpiritRebirthStateTests.Fixture(r); var state = f.Begin();
        var native = f.Hold(f.Host, f.Original); Invoke(state, "TryEnterHost", f.Original, f.Host);
        var stone = Stone(); var source = f.Original.holdingOwner;
        using (new RejectedCommit("TryEnterStone"))
            Check.Equal(false, Invoke(stone, "TryCapture", f.Original, f.Host, false), "injected commit refusal after native transfer");
        Check.Same(source, f.Original.holdingOwner, "failed capture restores exact prior native owner");
        Check.Same(f.Original, native.SpiritPawn, "original is restored without clone");
        Check.Equal(0, stone.GetDirectlyHeldThings().Count, "failed capture leaves empty stone");
        Check.Equal(true, state.HasValidHost(f.Original), "failed capture leaves source state valid");
        Check.Equal(true, Invoke(stone, "TryCapture", f.Original, f.Host, false), "capture retry succeeds");
        using (new RejectedCommit("TryEnterHost"))
            Check.Equal(false, Invoke(stone, "TryImplant", f.Original, native), "injected implant commit refusal");
        Check.Same(stone.GetDirectlyHeldThings(), f.Original.holdingOwner, "failed implant restores exact stone owner");
        Check.Equal(0, native.GetDirectlyHeldThings().Count, "failed implant empties destination");
        Check.Equal(true, state.HasValidStone(f.Original), "failed implant leaves valid stone state");
        Check.Equal(true, Invoke(stone, "TryImplant", f.Original, native), "implant retry succeeds");
    }
}
