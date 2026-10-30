using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using RimWorld;
using TorannMagic;
using TorannMagic.ClassProgression;
using Verse;
using static RWoM.RegressionTests.GrimoireProgressionTests;

namespace RWoM.RegressionTests;

internal static class SpiritMapCollapseTests
{
    internal static Type Production(string name) => Check.NotNull(typeof(SpiritRebirthState).Assembly.GetType("TorannMagic.ClassProgression." + name), "map collapse " + name + " exists");
    public static IEnumerable<TestCase> Cases()
    {
        yield return new("SpiritMapCollapse.NativePlacementFalseAfterSuccess", r => SpiritRebirthRitualTransactionTests.Run(r, c =>
        {
            using var f = new Fixture(r, c); c.Creator.health.SetDead();
            using (new Patch(PlaceMethod(), nameof(ReturnFalse), postfix: true))
                Check.Equal(true, Begin(f.Keeper, c.Original, c.Creator), "native successful placement remains committed despite callback false");
            var state = ClassIdentity.Component(c.Original).SpiritRebirth;
            var stone = Check.NotNull(state.Stone, "actual candidate committed");
            Check.Same(c.Map.spawnedThings, stone.holdingOwner, "observed native map owner wins over false callback result");
            var registered = c.Map.components;
            try
            {
                c.Map.components = null!;
                Check.Equal(false, (bool)Production("MapComponent_SpiritCollapse").GetMethod("IsPendingAccountingOwner", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [c.Original])!, "committed stone without pending token never needs keeper lookup");
            }
            finally { c.Map.components = registered; }
            Check.Same(c.Original, stone.ContainedSpirit, "same original retained after native placement callback");
            Check.Equal(0, ((IThingHolder)f.Keeper).GetDirectlyHeldThings().Count, "completed graph leaves no duplicate keeper ownership");
            Set(Find.TickManager, "ticksGameInt", 500); MapComponentUtility.MapComponentTick(c.Map);
            Check.Same(stone, state.Stone, "completed placement retry does not recreate or reset");
            RuntimeLog.AssertClean();
        }));
        yield return new("SpiritMapCollapse.RejectedAuthorizationRemovesOnlyEmptyCandidate", r => SpiritRebirthRitualTransactionTests.Run(r, c =>
        {
            using var f = new Fixture(r, c); c.Creator.health.SetDead();
            using (new Patch(typeof(SpiritRebirthState).GetMethod("BeginPendingMapDeath", BindingFlags.Instance | BindingFlags.NonPublic)!, nameof(RefuseOperation)))
                Check.Equal(false, Begin(f.Keeper, c.Original, c.Creator), "refused state authorization aborts before source mutation");
            Check.Same(f.Possession.GetDirectlyHeldThings(), c.Original.holdingOwner, "exact original remains native source-owned");
            Check.Equal(0, ((IThingHolder)f.Keeper).GetDirectlyHeldThings().Count, "new empty rejected shell not leaked in keeper");
            Check.Equal(SpiritRebirthForm.Possessed, ClassIdentity.Component(c.Original).SpiritRebirth.Form, "source state unchanged by rejected begin");
            RuntimeLog.AssertClean();
        }));
        foreach (bool occupied in new[] { false, true })
        yield return new("SpiritMapCollapse.NativePendingSave." + (occupied ? "OccupiedCandidate" : "EmergencyOriginalAndMovedEmpty"), r => SpiritRebirthRitualTransactionTests.Run(r, c =>
        {
            Production("MapComponent_SpiritCollapse");
            using var f = new Fixture(r, c);
            c.Creator.health.SetDead();
            using var placement = new Patch(PlaceMethod(), nameof(RefusePlacement));
            using (new Patch(typeof(SpiritRebirthState).GetMethod(occupied ? "TryCompletePendingMapDeath" : "SecurePendingMapDeath", BindingFlags.Instance | BindingFlags.NonPublic)!, occupied ? nameof(RefuseOperation) : nameof(ReturnFalse), postfix: !occupied))
            {
                bool began = Begin(f.Keeper, c.Original, c.Creator);
                RuntimeLog.AssertClean();
                Check.Equal(true, began, "pending failure still secures exact original in keeper graph");
            }
            var state = ClassIdentity.Component(c.Original).SpiritRebirth;
            Check.Equal(SpiritRebirthForm.Dormant, state.Form, "only secured original becomes inactive pending");
            Check.Equal(null, state.Host, "pending retry no longer depends on source host reference");
            Check.Equal(87, state.RecoveryTicks, "uncommitted capture does not prematurely reset recovery");
            c.Creator.health.RemoveHediff(f.Possession.parent);
            Check.Equal(false, c.Creator.health.hediffSet.hediffs.Contains(f.Possession.parent), "native source possession removed before independent retry");
            c.Original.needs.TryGetNeed<Need_Spirit>().CurLevel = 0f;
            c.Original.needs.TryGetNeed<Need_Mana>().CurLevel = .27f;
            ObservePendingAccounting(c);
            string identity = c.Original.GetUniqueLoadID();
            var keeper = SaveComponents(r, c, f.Keeper, occupied);
            var owner = ((IThingHolder)keeper).GetDirectlyHeldThings();
            var oldStone = owner.OfType<Thing_SoulStone>().Single();
            var original = occupied ? oldStone.ContainedSpirit! : owner.OfType<Pawn>().Single();
            Check.Equal(identity, original.GetUniqueLoadID(), "native deep graph preserves original saved identity");
            Check.Equal(false, ReferenceEquals(original, c.Original), "actual original deep-loaded, no reference-only surrogate");
            Check.Equal(true, (bool)Production("MapComponent_SpiritCollapse").GetMethod("IsPendingAccountingOwner", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [original])!, "loaded native custody and saved authorization agree");
            Check.NotNull(original.needs.TryGetNeed<Need_Spirit>(), "loaded Spirit exists before explicit refresh");
            Check.NotNull(original.needs.TryGetNeed<Need_Mana>(), "loaded Mana exists before explicit refresh");
            original.needs.AddOrRemoveNeedsAsAppropriate();
            Check.Equal(0f, original.needs.TryGetNeed<Need_Spirit>().CurLevel, "existing zero Spirit survives pending save and native need refresh");
            Check.Equal(.27f, original.needs.TryGetNeed<Need_Mana>().CurLevel, "existing pending mana survives native save");
            if (!occupied)
            {
                owner.Remove(oldStone); Check.Equal(true, c.Creator.inventory.innerContainer.TryAdd(oldStone, false), "empty candidate moved to real foreign owner");
            }
            Set(Find.TickManager, "ticksGameInt", 500); MapComponentUtility.MapComponentTick(c.Map);
            var restored = ClassIdentity.Component(original).SpiritRebirth;
            Check.Equal(true, restored.HasValidStone(original), "independent native map tick completes after source removal and load");
            Check.Equal(0, restored.RecoveryTicks, "committed host death resets full week exactly at commit");
            if (occupied) Check.Same(oldStone, restored.Stone, "occupied pending candidate never recreated");
            else
            {
                Check.Equal(false, ReferenceEquals(oldStone, restored.Stone), "only stale empty candidate replaced");
                Check.Same(c.Creator.inventory.innerContainer, oldStone.holdingOwner, "foreign empty shell neither stolen nor destroyed");
            }
            RuntimeLog.AssertClean();
        }));
        yield return new("SpiritMapCollapse.NativeKeeperAndExactDeathCapture", r => SpiritRebirthRitualTransactionTests.Run(r, c =>
        {
            Production("MapComponent_SpiritCollapse");
            using var f = new Fixture(r, c);
            Check.Equal(false, Begin(f.Keeper, c.Original, c.Creator), "live host cannot start death collapse");
            c.Creator.health.SetDead();
            Thing_SoulStone stone;
            using (new Patch(PlaceMethod(), nameof(RefusePlacement)))
            {
                Check.Equal(true, Begin(f.Keeper, c.Original, c.Creator), "actual SetDead host evacuates exact original");
                var state = ClassIdentity.Component(c.Original).SpiritRebirth;
                stone = Check.NotNull(state.Stone, "same-original stone commit completed");
                Check.Equal(true, state.HasValidStone(c.Original), "native stone custody reciprocal");
                Check.Equal(true, state.RecoveryRequired, "death requires full recovery debt");
                Check.Equal(0, state.RecoveryTicks, "death resets prior progress");
                Check.Equal(0, f.Possession.GetDirectlyHeldThings().Count, "source native holder empty");
                Check.Same(f.Keeper, stone.holdingOwner.Owner, "refused placement retains actual map component owner");
                var holders = new List<IThingHolder>(); c.Map.GetChildHolders(holders);
                Check.Equal(1, holders.Count(h => ReferenceEquals(h, f.Keeper)), "native Map.GetChildHolders discovers keeper once");
                var all = new List<Thing>(); ThingOwnerUtility.GetAllThingsRecursively(c.Map, all, false);
                Check.Equal(1, all.Count(x => ReferenceEquals(x, stone)), "native recursive walk finds actual stone once");
                Check.Equal(1, all.Count(x => ReferenceEquals(x, c.Original)), "native recursive walk finds exact original once");
                Check.Same(c.Map, ThingOwnerUtility.GetRootMap((IThingHolder)stone), "native root map follows keeper parent");
                Check.Equal(true, Begin(f.Keeper, c.Original, c.Creator), "repeated death entry reuses pending actual candidate");
                Check.Same(stone, state.Stone, "repeat cannot manufacture candidate");
                Set(Find.TickManager, "ticksGameInt", 249); MapComponentUtility.MapComponentTick(c.Map);
                Check.Same(f.Keeper, stone.holdingOwner.Owner, "placement backoff independent of dead host");
            }
            Set(Find.TickManager, "ticksGameInt", 250); MapComponentUtility.MapComponentTick(c.Map);
            RuntimeLog.AssertClean();
            Check.Same(c.Map.spawnedThings, stone.holdingOwner, "native local retry places existing stone");
            Check.Equal(true, stone.Spawned && c.Map.thingGrid.ThingsListAt(stone.Position).Contains(stone), "native map registration agrees with holder");
            Check.Same(c.Original, stone.ContainedSpirit, "placement retains exact original");
            RuntimeLog.AssertClean();
        }));
    }
    static void ObservePendingAccounting(SpiritRebirthRitualTransactionTests.Context c)
    {
        var pawn = c.Original; var magic = pawn.GetComp<CompAbilityUserMagic>();
        var mana = pawn.needs.TryGetNeed<Need_Mana>(); var spirit = pawn.needs.TryGetNeed<Need_Spirit>();
        var oldSettings = TorannMagic.ModOptions.Settings.Instance;
        var defs = DefDatabase<HediffDef>.AllDefsListForReading.ToArray();
        try
        {
            TorannMagic.ModOptions.Settings.Instance = new TorannMagic.ModOptions.Settings();
            foreach (string name in new[] { "TM_SyrriumSenseHD", "TM_PomanaSenseHD" })
                if (DefDatabase<HediffDef>.GetNamedSilentFail(name) == null) DefDatabase<HediffDef>.Add(new HediffDef { defName = name });
            c.Map.gameConditionManager = new GameConditionManager(c.Map);
            Set(mana, "lastParacyteCheck", int.MaxValue);
            magic.summonedMinions = Enumerable.Range(0, 8).Select(_ => (Thing)PawnWithComps(false, false).pawn).ToList();
            magic.mpRegenRate = 1f; magic.maxMP = 1f;
            int tick = Enumerable.Range(1, 150).First(t => { Set(Find.TickManager, "ticksGameInt", t); return pawn.IsHashIntervalTick(150); });
            MapComponentUtility.MapComponentTick(c.Map);
            Check.Equal(true, mana.CurLevel < .27f, "pending native keeper clock pays existing negative minion upkeep");
            float once = mana.CurLevel; MapComponentUtility.MapComponentTick(c.Map);
            Check.Equal(once, mana.CurLevel, "duplicate observed keeper tick cannot charge twice");
            Check.Same(mana, pawn.needs.TryGetNeed<Need_Mana>(), "pending clock preserves actual Mana owner");
            Check.Equal(0f, spirit.CurLevel, "pending clock does not drain or refill Spirit");
            Check.Equal(87, ClassIdentity.Component(pawn).SpiritRebirth.RecoveryTicks, "pending accounting never advances host recovery");
            mana.CurLevel = .27f;
            RuntimeLog.AssertClean();
        }
        finally { TorannMagic.ModOptions.Settings.Instance = oldSettings; DefDatabase<HediffDef>.Clear(); foreach (var def in defs) DefDatabase<HediffDef>.Add(def); }
    }
    static MapComponent SaveComponents(RuntimeFixture r, SpiritRebirthRitualTransactionTests.Context c, MapComponent keeper, bool occupied)
    {
        var pawn = c.Original; var comp = ClassIdentity.Component(pawn);
        // Native ownership/state/needs graph. Unrelated optional Pawn trackers
        // retain their separate established identity/save coverage.
        foreach (var field in typeof(Pawn).GetFields(BindingFlags.Instance | BindingFlags.Public))
            if (!field.IsInitOnly && field.FieldType.Name.StartsWith("Pawn_") && field.Name != "inventory" && field.Name != "health" && field.Name != "needs" && field.Name != "ageTracker") field.SetValue(pawn, null);
        pawn.ageTracker = new Pawn_AgeTracker(pawn);
        Set(pawn.ageTracker, "ageBiologicalTicksInt", 25L * 3600000L);
        pawn.def.race.lifeStageAges = [new LifeStageAge { minAge = 13, def = new LifeStageDef { developmentalStage = DevelopmentalStage.Adult } }];
        Set(pawn.ageTracker, "cachedLifeStageIndex", 0);
        pawn.Name = null; pawn.kindDef = null; Set(pawn, "factionInt", null); pawn.health.hediffSet.hediffs.Clear();
        pawn.def.comps = [new CompProperties { compClass = typeof(CompClassProgression) }]; Set(pawn, "comps", new List<ThingComp> { comp });
        foreach (var def in new[] { pawn.def, c.Magicyte.def, c.Silver.def })
            if (DefDatabase<ThingDef>.GetNamedSilentFail(def.defName) == null) DefDatabase<ThingDef>.Add(def);
        c.Map.uniqueID = 654;
        var path = Path.Combine(r.WorkingDirectory, "native-map-components-" + occupied + ".xml");
        var components = c.Map.components;
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            try { Scribe.saver.InitSaving(path, "regression"); Scribe_Collections.Look(ref components, "components", LookMode.Deep, c.Map); Scribe.saver.FinalizeSaving(); }
            finally { Scribe.ForceStop(); }
            var xml = XDocument.Load(path);
            Check.Equal(1, xml.Descendants("def").Count(x => x.Value == pawn.def.defName), "one deep native original node");
            Check.Equal(1, xml.Descendants("def").Count(x => x.Value == "TM_SoulStone"), "one deep actual candidate node");
            components = null!;
            try
            {
                Scribe.loader.InitLoading(path); Scribe_Collections.Look(ref components, "components", LookMode.Deep, c.Map); c.Map.components = components;
                var directory = (LoadedObjectDirectory)typeof(CrossRefHandler).GetField("loadedObjectDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Scribe.loader.crossRefs)!;
                foreach (var anchor in new ILoadReferenceable[] { c.Map, c.Creator, c.Circle }) directory.RegisterLoaded(anchor);
                Scribe.loader.FinalizeLoading();
            }
            finally { Scribe.ForceStop(); }
            return components.Single();
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = culture; }
    }
    static bool RefuseOperation(ref bool __result) { __result = false; return false; }
    static void ReturnFalse(ref bool __result) { __result = false; }
    static bool Begin(MapComponent keeper, Pawn original, Pawn host) => SpiritRebirthStateTests.Invoke(keeper, "TryBeginHostDeath", original, host);
    static MethodInfo PlaceMethod() => typeof(GenPlace).GetMethods().Single(m => m.Name == "TryPlaceThing" && m.GetParameters().Any(p => p.IsOut));
    static bool RefusePlacement(Thing thing, ref bool __result) { if (thing is not Thing_SoulStone) return true; __result = false; return false; }
    sealed class Fixture : IDisposable
    {
        readonly SpiritRebirthReleaseTests.ReleaseFixture release;
        readonly IDisposable ui;
        readonly ThingDef[] defs = DefDatabase<ThingDef>.AllDefsListForReading.ToArray();
        readonly HediffDef oldPossession = TorannMagicDefOf.TM_SpiritPossessionHD;
        public MapComponent Keeper;
        public HediffComp_SpiritPossession Possession;
        public Fixture(RuntimeFixture r, SpiritRebirthRitualTransactionTests.Context c)
        {
            Check.Equal("ConvertedHeld", c.Execute(), "actual paid original prepared");
            release = new(r, c);
            ui = (IDisposable)Activator.CreateInstance(typeof(SpiritRebirthRuntimeTests).GetNestedType("UiPauseScope", BindingFlags.NonPublic)!)!;
            var receipt = ClassIdentity.Component(c.Original).GetType().GetProperty("SpiritRitual", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ClassIdentity.Component(c.Original))!;
            Check.Equal("Released", receipt.GetType().GetMethod("TryRelease", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(receipt, [c.Creator, c.Map, new IntVec3(15, 0, 15)])!.ToString(), "actual original native release");
            c.Original.DeSpawn();
            TorannMagicDefOf.TM_SpiritPossessionHD = new HediffDef { defName = "MapCollapsePossession" };
            var hediff = new HediffWithComps { def = TorannMagicDefOf.TM_SpiritPossessionHD, pawn = c.Creator };
            Possession = new HediffComp_SpiritPossession { parent = hediff }; hediff.comps = [Possession]; c.Creator.health.hediffSet.hediffs.Add(hediff);
            var owner = new ThingOwner<Thing>(Possession); Set(Possession, "innerContainer", owner);
            Check.Equal(true, owner.TryAdd(c.Original, false), "native possession holds same original");
            var state = ClassIdentity.Component(c.Original).SpiritRebirth;
            Check.Equal(true, SpiritRebirthStateTests.Invoke(state, "TryEnterHost", c.Original, c.Creator), "exact native host transition");
            Set(state, "recoveryRequired", true); Set(state, "recoveryTicks", 87);
            c.Creator.def.race.body = c.Original.def.race.body;
            var stoneDef = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
            stoneDef.defName = "TM_SoulStone"; stoneDef.thingClass = typeof(Thing_SoulStone); stoneDef.category = ThingCategory.Item; stoneDef.stackLimit = 1;
            stoneDef.size = new IntVec2(1, 1); stoneDef.comps = []; stoneDef.drawerType = DrawerType.RealtimeOnly; stoneDef.altitudeLayer = AltitudeLayer.Item; stoneDef.destroyable = true;
            DefDatabase<ThingDef>.Add(stoneDef);
            c.Map.components ??= [];
            c.Map.passingShipManager = new PassingShipManager(c.Map);
            Keeper = (MapComponent)Activator.CreateInstance(Production("MapComponent_SpiritCollapse"), [c.Map])!; c.Map.components.Add(Keeper);
        }
        public void Dispose() { ui.Dispose(); TorannMagicDefOf.TM_SpiritPossessionHD = oldPossession; DefDatabase<ThingDef>.Clear(); foreach (var def in defs) DefDatabase<ThingDef>.Add(def); release.Dispose(); }
    }
    sealed class Patch : IDisposable
    {
        readonly object harmony; readonly string id = "rwom.map-collapse-fault." + Guid.NewGuid();
        public Patch(MethodBase target, string callback, bool postfix = false)
        {
            var assembly = Assembly.Load("0Harmony"); harmony = Activator.CreateInstance(assembly.GetType("HarmonyLib.Harmony")!, id)!;
            var method = Activator.CreateInstance(assembly.GetType("HarmonyLib.HarmonyMethod")!, typeof(SpiritMapCollapseTests).GetMethod(callback, BindingFlags.NonPublic | BindingFlags.Static)!)!;
            harmony.GetType().GetMethod("Patch")!.Invoke(harmony, [target, postfix ? null : method, postfix ? method : null, null, null]);
        }
        public void Dispose() => harmony.GetType().GetMethod("UnpatchAll")!.Invoke(harmony, [id]);
    }
}
