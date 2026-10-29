using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using AbilityUser;
using RimWorld;
using TorannMagic;
using TorannMagic.Compatibility;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RWoM.RegressionTests;

internal static partial class TeleportSpiritTests
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static WorldState? active;
    sealed class FixtureComp : ThingComp { }
    sealed class DefinitionScope<T> : IDisposable where T : Def, new()
    {
        readonly List<T> saved = DefDatabase<T>.AllDefsListForReading.ToList();
        public DefinitionScope(T definition) => DefDatabase<T>.Add(definition);
        public void Dispose()
        {
            DefDatabase<T>.Clear();
            foreach (var definition in saved) DefDatabase<T>.Add(definition);
        }
    }
    static Type VerbType(string name) => typeof(MagicAbility).Assembly.GetType("TorannMagic." + name, true)!;
    static MethodInfo Shot(Verb verb) => verb.GetType().GetMethod("TryCastShot", Flags)!;
    static bool Fire(Verb verb) => (bool)Shot(verb).Invoke(verb, null)!;

    public static IEnumerable<TestCase> Cases()
    {
        foreach (var test in ManualTravelCases()) yield return test;
        foreach (var test in TravelEnvironmentAutoCases()) yield return test;
        foreach (var test in FlightCases()) yield return test;
        foreach (var test in TravelCases()) yield return test;
        yield return new("TeleportSpirit.MoveUsesPositionNotificationAndPreservesSelectionJobDraft", f => World(f, w =>
        {
            using var patches = w.MovementBoundaries();
            var destination = new IntVec3(12, 0, 10);
            Check.Equal(true, TM_Teleport.CanMove(w.Caster, destination), "real map accepts a distinct standable endpoint");
            Check.Equal(true, TM_Teleport.Move(w.Caster, destination), "real helper reaches the native relocation boundaries");
            Check.Equal(destination, w.Caster.Position, "Position setter receives the requested endpoint");
            Check.Equal("Position,Notify", string.Join(",", w.Events), "exactly one position assignment followed by native teleport notification");
            Check.Equal(false, w.EndsJob, "teleport notification cannot terminate the executing spell job");
            w.AssertPreserved();
        }));
        yield return new("TeleportSpirit.MoveRechecksRealLandingAndRejectsSameCell", f => World(f, w =>
        {
            using var patches = w.MovementBoundaries();
            var endpoint = new IntVec3(12, 0, 10);
            Check.Equal(true, TM_Teleport.CanMove(w.Caster, endpoint), "endpoint starts valid");
            w.Building(endpoint);
            Check.Equal(false, endpoint.Standable(w.Map), "real thing grid now rejects the blocked destination");
            foreach (var cell in new[] { endpoint, w.Caster.Position, IntVec3.Invalid, new IntVec3(20, 0, 4) })
                Check.Equal(false, TM_Teleport.Move(w.Caster, cell), "completion rejects changed, same-cell and invalid targets");
            Set(w.Caster, "mapIndexOrState", (sbyte)-1);
            Check.Equal(false, TM_Teleport.Move(w.Caster, new IntVec3(8, 0, 8)), "despawned caster cannot relocate");
            Set(w.Caster, "mapIndexOrState", (sbyte)0);
            Check.Equal(false, TM_Teleport.Move(null!, new IntVec3(8, 0, 8)), "missing caster cannot relocate");
            Check.Equal(0, w.Events.Count, "rejections never touch position or teleport notification");
        }));
        foreach (string name in new[] { "Verb_Blink", "Verb_PhaseStrike" })
        {
            string verbName = name;
            yield return new("TeleportSpirit.Targets." + name, f => World(f, w =>
            {
                var verb = w.Verb(verbName, f);
                var root = w.Caster.Position; var endpoint = new IntVec3(12, 0, 10);
                Check.Equal(true, verb.CanHitTargetFrom(root, endpoint), "real verb accepts the distinct standable cell");
                Check.Equal(false, verb.CanHitTargetFrom(root, root), "real verb excludes its own cell during selection");
                Check.Equal(false, verb.CanHitTargetFrom(root, new IntVec3(20, 0, 4)), "real verb excludes out-of-map cells");
                w.Building(endpoint);
                Check.Equal(false, verb.CanHitTargetFrom(root, endpoint), "real verb excludes an impassable endpoint");
                using var patches = w.MovementBoundaries();
                Set(verb, "currentTarget", new LocalTargetInfo(endpoint));
                Check.Equal(false, Fire(verb), "shot revalidates a destination blocked after target selection");
                Check.Equal(0, w.Events.Count, "rejected shot never relocates or emits damage");
            }));
            foreach (bool cameraSnap in new[] { false, true })
            {
                bool setting = cameraSnap;
                yield return new("TeleportSpirit.CastPreservesSelection." + name + ".CameraSetting" + setting, f => World(f, w =>
                {
                    TorannMagic.ModOptions.Settings.Instance.cameraSnap = setting;
                    var verb = w.Verb(verbName, f); using var patches = w.MovementBoundaries();
                    var endpoint = new IntVec3(12, 0, 10); Set(verb, "currentTarget", new LocalTargetInfo(endpoint));
                    Check.Equal(true, Fire(verb), "real shot completes without a despawn/spawn cycle");
                    Check.Equal(endpoint, w.Caster.Position, "spell uses its actual selected destination");
                    Check.Equal(false, w.EndsJob, "spell retains its executing job");
                    Check.Equal("Position,Notify", string.Join(",", w.Events.Take(2)), "relocation precedes all secondary effects");
                    w.AssertPreserved();
                }));
            }
            yield return new("TeleportSpirit.NoCameraSelectionOrRespawnCalls." + name, _ =>
            {
                var roots = new[] { VerbType(verbName).GetMethod("TryCastShot", Flags)!, typeof(TM_Teleport).GetMethod(nameof(TM_Teleport.Move))! };
                var calls = roots.SelectMany(Calls).ToArray();
                Check.Equal(false, calls.Any(c => c.DeclaringType?.Name.Contains("Camera", StringComparison.Ordinal) == true
                    || c.DeclaringType?.Name.Contains("Selector", StringComparison.Ordinal) == true
                    || c.DeclaringType == typeof(ThingSelectionUtility)
                    || c.DeclaringType == typeof(Find) && (c.Name.Contains("Camera", StringComparison.Ordinal) || c.Name.Contains("Selector", StringComparison.Ordinal))),
                    "compiled relocation path never invokes camera/focus/selection APIs, independent of settings");
                Check.Equal(false, calls.Any(c => c.Name == nameof(Thing.DeSpawn) || c.DeclaringType == typeof(GenSpawn)),
                    "same-map movement never despawns or respawns the pawn");
                Check.Equal(true, calls.Any(c => c.DeclaringType == typeof(TM_Teleport) && c.Name == nameof(TM_Teleport.Move)), "actual spell routes through shared relocation");
            });
        }
        yield return new("TeleportSpirit.BlinkRetainsCantripStunAfterMove", f => World(f, w =>
        {
            var verb = w.Verb("Verb_Blink", f); using var patches = w.MovementBoundaries();
            var comp = new CompAbilityUserMagic { parent = w.Caster }; var data = new MagicData();
            Set(comp, "magicData", data); w.Caster.AllComps.Add(comp); TM_Calc.InvalidateCompCache(w.Caster);
            data.MagicPowerSkill_Cantrips.Single(skill => skill.label == "TM_Cantrips_ver").level = 1;
            patches.Prefix(typeof(TM_Action).GetMethod(nameof(TM_Action.DamageEntities), [typeof(Thing), typeof(BodyPartRecord), typeof(float), typeof(DamageDef), typeof(Thing)])!, nameof(BlinkDamage));
            var endpoint = new IntVec3(12, 0, 10);
            var victim = w.Pawn("BlinkNearbyHostile", endpoint + new IntVec3(1, 0, 0), player: false); victim.kindDef.hostileToAll = true;
            Check.Equal(true, victim.HostileTo(w.Caster.Faction), "real hostility policy recognizes the nearby enemy");
            Set(verb, "currentTarget", new LocalTargetInfo(endpoint));
            Check.Equal(true, Fire(verb), "real upgraded Blink completes");
            var hit = w.BlinkHits.Single(); Check.Same(victim, hit.Victim, "existing nearby-target search finds the enemy after moving");
            Check.Equal(5f, hit.Amount, "cantrip versatility retains five stun damage");
            Check.Same(DamageDefOf.Stun, hit.Damage, "stun damage definition is unchanged");
            Check.Equal("Position,Notify,Stun", string.Join(",", w.Events), "move and notification precede secondary damage");
        }));
        yield return new("TeleportSpirit.PhaseKeepsDestinationVictimAndDamageFormula", f => World(f, w =>
        {
            var verb = w.Verb("Verb_PhaseStrike", f); using var patches = w.MovementBoundaries();
            var endpoint = new IntVec3(12, 0, 10);
            // Register caster first at the destination, matching the formerly dangerous
            // post-move GetFirstPawn case, then an opposing pawn at that same cell.
            w.Map.thingGrid.Deregister(w.Caster); Set(w.Caster, "positionInt", endpoint); w.Map.thingGrid.Register(w.Caster);
            var victim = w.Pawn("PhaseDestinationVictim", endpoint, player: false);
            Check.Same(w.Caster, endpoint.GetFirstPawn(w.Map), "fixture would hide the victim with the old GetFirstPawn lookup");
            Set(verb, "dmgNum", 20); Set(verb, "pwrVal", -1); // Negative chance deterministically disables critical hits only in this fixture.
            verb.GetType().GetMethod("SearchForTargets")!.Invoke(verb, [endpoint, 2f, w.Map, w.Caster]);
            var hit = w.PhaseHits.Single(); Check.Same(victim, hit.Victim, "real search excludes the spawned caster, not the destination enemy");
            Check.Equal(20, hit.Amount, "weapon-derived damage still reaches the existing damageEntities boundary");
            var phaseDamage = typeof(MagicAbility).Assembly.GetType("TorannMagic.TMDamageDefOf+DamageDefOf", true)!
                .GetField("TM_PhaseStrike", BindingFlags.Public | BindingFlags.Static)!.GetValue(null);
            Check.Same(phaseDamage, hit.Damage, "existing Phase Strike damage definition is retained");
        }));
        foreach (bool animal in new[] { false, true })
        {
            bool hostAnimal = animal;
            yield return new("TeleportSpirit.LeaveBodyAppendsOnceAndUsesExistingAction." + (animal ? "Animal" : "Human"), f => World(f, w =>
            {
                w.Caster.RaceProps.intelligence = hostAnimal ? Intelligence.Animal : Intelligence.Humanlike;
                if (hostAnimal) w.Caster.drafter = null;
                var possession = w.Possess(w.Caster);
                using var patches = new Patches(); patches.Prefix(typeof(TM_Action).GetMethod(nameof(TM_Action.RemovePossession))!, nameof(RemovePossession));
                patches.Prefix(typeof(Pawn_DraftController).GetProperty(nameof(Pawn_DraftController.Drafted))!.SetMethod!, nameof(Undraft));
                var sentinel = new Command_Action(); IEnumerable<Gizmo> commands = new Gizmo[] { sentinel };
                commands = TM_SpiritCommands.AppendLeaveBody(commands, w.Caster);
                commands = TM_SpiritCommands.AppendLeaveBody(commands, w.Caster);
                Check.Equal(2, commands.Count(), "one existing command plus exactly one owner-specific Leave Body command");
                Check.Same(sentinel, commands.First(), "preexisting commands are preserved");
                Check.Same(possession, TM_SpiritCommands.Possession(w.Caster), "eligibility recognizes the actual containing possession comp");
                ((Command_Action)commands.Last()).action();
                Check.Equal(1, w.ReleaseCalls, "real command invokes existing release action once (full possession teardown intentionally bounded)");
                Check.Same(w.Caster, w.ReleasedHost, "existing release receives this host");
                Check.Equal(w.Caster.Position, w.ReleasedAt, "existing release receives the live host position");
                Check.Equal(false, w.DestroySpirit, "manual exit must not destroy its spirit");
            }));
        }
        yield return new("TeleportSpirit.LeaveBodyRequiresActualOwnerAndLiveSelection", f => World(f, w =>
        {
            Check.Equal(false, TM_SpiritCommands.CanLeaveBody(w.Caster), "a bare host has no exit command");
            var possession = w.Possess(w.Caster); var spirit = possession.SpiritPawn;
            Check.Equal(true, TM_SpiritCommands.CanLeaveBody(w.Caster), "stored spirit owner qualifies");
            var commands = TM_SpiritCommands.AppendLeaveBody(Array.Empty<Gizmo>(), w.Caster).ToArray();
            w.Selected.Add(spirit);
            Check.Equal(false, TM_SpiritCommands.CanLeaveBody(w.Caster), "multi-selection is excluded");
            w.Selected.Clear(); w.Selected.Add(spirit);
            Check.Equal(false, TM_SpiritCommands.CanLeaveBody(w.Caster), "one unrelated selected pawn is still excluded");
            w.Selected.Clear(); w.Selected.Add(w.Caster);
            Set(w.Caster, "factionInt", new Faction { def = new FactionDef() });
            Check.Equal(false, TM_SpiritCommands.CanLeaveBody(w.Caster), "non-player host is excluded");
            Set(w.Caster, "factionInt", w.Player);
            Set(w.Caster, "mapIndexOrState", (sbyte)-1);
            Check.Equal(false, TM_SpiritCommands.CanLeaveBody(w.Caster), "despawned host is excluded");
            Set(w.Caster, "mapIndexOrState", (sbyte)0);
            possession.GetDirectlyHeldThings().Remove(spirit);
            Check.Equal(false, TM_SpiritCommands.CanLeaveBody(w.Caster), "stale possession marker without a held spirit is excluded");
            using var patches = new Patches(); patches.Prefix(typeof(TM_Action).GetMethod(nameof(TM_Action.RemovePossession))!, nameof(RemovePossession));
            ((Command_Action)commands.Single()).action();
            Check.Equal(0, w.ReleaseCalls, "an old gizmo revalidates owner state before executing");
            Check.Equal(0, TM_SpiritCommands.AppendLeaveBody(Array.Empty<Gizmo>(), w.Caster).Count(), "query never reconstructs missing spirit state");
        }));
    }

    static void World(RuntimeFixture fixture, Action<WorldState> body)
    {
        var oldGame = Current.Game; var oldRoot = typeof(Current).GetField("rootInt", Flags)!.GetValue(null);
        var oldLanguage = LanguageDatabase.activeLanguage; var oldDefaultLanguage = LanguageDatabase.defaultLanguage;
        var oldSettings = TorannMagic.ModOptions.Settings.Instance; var oldHediff = TorannMagicDefOf.TM_SpiritPossessionHD;
        var oldActive = active;
        Rand.PushState(2207);
        try
        {
            Current.Game = Blank<Game>(); Set(Current.Game, "tickManager", new TickManager()); Set(Current.Game, "uniqueIDsManager", new UniqueIDsManager());
            Current.Game.Scenario = new Scenario();
            // Payment may have inlined Find's history getter before this fixture runs.
            // Back it with real state; individual payment tests still stub event recording.
            Current.Game.history = Blank<History>();
            Current.Game.history.historyEventsManager = Blank<HistoryEventsManager>();
            TorannMagic.ModOptions.Settings.Instance = new TorannMagic.ModOptions.Settings();
            TorannMagicDefOf.TM_SpiritPossessionHD = new HediffDef { defName = "TM_SpiritPossessionHD" };
            var language = Blank<LoadedLanguage>(); language.folderName = "English"; Set(language, "dataIsLoaded", true); Set(language, "workerInt", new LanguageWorker_English());
            language.keyedReplacements = new Dictionary<string, LoadedLanguage.KeyedReplacement>();
            foreach (string key in new[] { "InvalidTargetLocation", "MustHaveMeleeWeapon", "TM_PromoteWanderer", "TM_PromoteWandererDesc", "TM_PromoteWayfarer", "TM_PromoteWayfarerDesc", "TM_RemovePossession", "TM_RemovePossessionDesc", "TM_EndSymbiosis", "TM_EndSymbiosisDesc" })
                language.keyedReplacements[key] = new LoadedLanguage.KeyedReplacement { key = key, value = key };
            LanguageDatabase.activeLanguage = LanguageDatabase.defaultLanguage = language;
            using var patches = new Patches();
            // Graphics are not under test. Intercept only material/icon loading and
            // rendering boundaries; real target grids, verb logic and commands run.
            foreach (var method in typeof(MaterialPool).GetMethods().Where(m => m.Name == nameof(MaterialPool.MatFrom) && m.ReturnType == typeof(Material)))
                patches.Prefix(method, nameof(SkipGraphics));
            patches.Prefix(typeof(ContentFinder<Texture2D>).GetMethod("Get", [typeof(string), typeof(bool)])!, nameof(SkipGraphics));
            patches.Prefix(typeof(Messages).GetMethods().Single(m => m.Name == "Message" && m.GetParameters() is var p && p.Length == 3 && p[1].ParameterType == typeof(MessageTypeDef) && p[2].ParameterType == typeof(bool)), nameof(SkipGraphics));
            using var designation = DefDatabase<DesignationDef>.DefCount == 0
                ? new DefinitionScope<DesignationDef>(new DesignationDef { defName = "TeleportFixtureDesignation" }) : null;
            using var world = new WorldState(); active = world;
            using var environment = new TravelEnvironmentFixture(world.Caster, world.Map) { Space = false };
            var root = Blank<Root_Play>(); root.uiRoot = Blank<UIRoot_Play>(); ((UIRoot_Play)root.uiRoot).mapUI = Blank<MapInterface>();
            ((UIRoot_Play)root.uiRoot).mapUI.selector = Blank<Selector>(); Set(((UIRoot_Play)root.uiRoot).mapUI.selector, "selected", world.Selected);
            typeof(Current).GetField("rootInt", Flags)!.SetValue(null, root);
            body(world);
        }
        finally
        {
            active = oldActive; Current.Game = oldGame; typeof(Current).GetField("rootInt", Flags)!.SetValue(null, oldRoot);
            LanguageDatabase.activeLanguage = oldLanguage; LanguageDatabase.defaultLanguage = oldDefaultLanguage;
            TorannMagic.ModOptions.Settings.Instance = oldSettings; TorannMagicDefOf.TM_SpiritPossessionHD = oldHediff; Rand.PopState();
        }
    }
    sealed class WorldState : IDisposable
    {
        public readonly Map Map;
        public readonly Pawn Caster;
        public readonly Faction Player = new() { def = new FactionDef { isPlayer = true } };
        public readonly List<object> Selected = [];
        public readonly List<string> Events = [];
        public readonly List<(Pawn Victim, int Amount, DamageDef Damage)> PhaseHits = [];
        public readonly List<(Thing Victim, float Amount, DamageDef Damage)> BlinkHits = [];
        public readonly Job Job = new() { def = new JobDef { defName = "TeleportRegressionCast" }, count = 1 };
        public bool? EndsJob, DestroySpirit;
        public int ReleaseCalls;
        public Pawn? ReleasedHost;
        public IntVec3 ReleasedAt;
        readonly PathGrid pathGrid;
        int pawnId;
        public WorldState()
        {
            Map = Blank<Map>(); Map.info = new MapInfo { Size = new IntVec3(20, 1, 20) };
            Set(Current.Game, "maps", new List<Map> { Map });
            Map.cellIndices = new CellIndices(Map); Map.thingGrid = new ThingGrid(Map); Map.edificeGrid = new EdificeGrid(Map); Map.fogGrid = new FogGrid(Map);
            Map.mapPawns = new MapPawns(Map);
            Map.designationManager = new DesignationManager(Map);
            pathGrid = new PathGrid(Map, new PathGridDef { defName = "TeleportRegressionPath" });
            Map.pathing = Blank<Pathing>(); var context = new PathingContext(Map, pathGrid); Set(Map.pathing, "normal", context); Set(Map.pathing, "fenceBlocked", context);
            Caster = Pawn("TeleportRegressionCaster", new IntVec3(4, 0, 4));
            Caster.drafter = new Pawn_DraftController(Caster); Set(Caster.drafter, "draftedInt", true);
            Set(Caster.jobs, "curJob", Job); Selected.Add(Caster);
        }
        public Pawn Pawn(string name, IntVec3 position, bool player = true)
        {
            var race = Blank<ThingDef>(); race.defName = name; race.category = ThingCategory.Pawn; race.size = new IntVec2(1, 1);
            race.passability = Traversability.Standable; race.race = new RaceProperties { intelligence = Intelligence.Humanlike };
            var pawn = new Pawn { def = race, kindDef = new PawnKindDef { defName = name, race = race }, thingIDNumber = ++pawnId };
            pawn.health = new Pawn_HealthTracker(pawn); pawn.guest = new Pawn_GuestTracker(pawn); pawn.jobs = new Pawn_JobTracker(pawn);
            pawn.equipment = new Pawn_EquipmentTracker(pawn); pawn.mindState = Blank<Pawn_MindState>(); pawn.mindState.mentalStateHandler = new MentalStateHandler(pawn);
            Set(pawn, "factionInt", player ? Player : new Faction { def = new FactionDef() });
            // Native 1.7 GetComp assumes a non-null component list is non-empty.
            Set(pawn, "comps", new List<ThingComp> { new FixtureComp { parent = pawn, props = new CompProperties() } });
            Set(pawn, "positionInt", position); Set(pawn, "mapIndexOrState", (sbyte)0); Map.thingGrid.Register(pawn);
            ((IList<Pawn>)Map.mapPawns.AllPawnsSpawned).Add(pawn); return pawn;
        }
        public Verb Verb(string name, RuntimeFixture fixture)
        {
            var type = VerbType(name); var definition = fixture.AddAbility("TM_TeleportRegression_" + name);
            definition.MainVerb.range = 30; definition.weaponDamageFactor = .4f;
            var verb = (Verb_UseAbility)Activator.CreateInstance(type, nonPublic: true)!;
            verb.caster = Caster; verb.verbProps = definition.MainVerb; verb.Ability = new MagicAbility { Def = definition };
            Job.verbToUse = verb;
            if (name == "Verb_PhaseStrike")
            {
                var comp = new CompAbilityUserMight { parent = Caster, weaponDamage = 50 };
                Set(comp, "initializedIsMightUser", true); Set(comp, "isMightUser", false);
                Caster.AllComps.Add(comp); TM_Calc.InvalidateCompCache(Caster);
                var weaponDef = Blank<ThingDef>(); weaponDef.defName = "TeleportRegressionSword"; weaponDef.category = ThingCategory.Item;
                weaponDef.equipmentType = EquipmentType.Primary; weaponDef.comps = [];
                var weapon = new ThingWithComps { def = weaponDef };
                Set(weapon, "comps", new List<ThingComp> { new CompEquippable { parent = weapon, props = new CompProperties(typeof(CompEquippable)) } });
                // Exercise real holder admission and equipment notifications.
                var holder = (ThingOwner<ThingWithComps>)Get(Caster.equipment, "equipment")!;
                Check.Equal(true, holder.TryAdd(weapon, false), "real equipment holder accepts a melee weapon");
                Check.Same(weapon, Caster.equipment.Primary, "real Primary lookup sees the weapon");
            }
            return verb;
        }
        public HediffComp_SpiritPossession Possess(Pawn host)
        {
            var hediff = new HediffWithComps { pawn = host, def = TorannMagicDefOf.TM_SpiritPossessionHD };
            var comp = new HediffComp_SpiritPossession { parent = hediff, props = new HediffCompProperties { compClass = typeof(HediffComp_SpiritPossession) } };
            hediff.comps = [comp]; host.health.hediffSet.hediffs.Add(hediff);
            var spirit = Pawn("HeldSpirit", new IntVec3(5, 0, 5)); Map.thingGrid.Deregister(spirit); Set(spirit, "mapIndexOrState", (sbyte)-1);
            var owner = new ThingOwner<Thing>(); Check.Equal(true, owner.TryAdd(spirit, false), "real owner holds the exact existing spirit");
            Set(comp, "innerContainer", owner); return comp;
        }
        public void Building(IntVec3 cell)
        {
            var def = Blank<ThingDef>(); def.defName = "TeleportRegressionWall"; def.category = ThingCategory.Building;
            def.size = new IntVec2(1, 1); def.passability = Traversability.Impassable;
            var thing = new Building { def = def }; Set(thing, "positionInt", cell); Set(thing, "mapIndexOrState", (sbyte)0);
            Map.thingGrid.Register(thing); Map.edificeGrid.Register(thing);
        }
        public Patches MovementBoundaries(bool stubSettlement = true)
        {
            var patches = new Patches();
            if (stubSettlement) patches.Prefix(typeof(TorannMagic.Odyssey.ManualTravelSafety).GetMethod("Settle")!, nameof(SkipGraphics));
            patches.Prefix(typeof(Thing).GetProperty(nameof(Thing.Position))!.SetMethod!, nameof(Position));
            patches.Prefix(typeof(Pawn).GetMethod(nameof(Verse.Pawn.Notify_Teleported))!, nameof(Notify));
            patches.Prefix(typeof(Pawn).GetProperty(nameof(Verse.Pawn.DrawPos))!.GetMethod!, nameof(DrawPos));
            foreach (var method in typeof(FleckMaker).GetMethods().Where(m => m.Name == nameof(FleckMaker.ThrowDustPuff))) patches.Prefix(method, nameof(SkipGraphics));
            patches.Prefix(typeof(TM_MoteMaker).GetMethod(nameof(TM_MoteMaker.ThrowGenericMote))!, nameof(SkipGraphics));
            var phase = VerbType("Verb_PhaseStrike"); patches.Prefix(phase.GetMethod("DrawBlade", Flags)!, nameof(SkipGraphics));
            patches.Prefix(phase.GetMethod("DrawStrike")!, nameof(SkipGraphics)); patches.Prefix(phase.GetMethod("damageEntities")!, nameof(PhaseDamage));
            // Recompile these callers after patching native notification to avoid an
            // earlier test's JIT-inlined notification bypassing the capture seam.
            patches.Prefix(typeof(TM_Teleport).GetMethod(nameof(TM_Teleport.Move))!, nameof(RunReal));
            return patches;
        }
        public void AssertPreserved()
        {
            Check.Same(Caster, Find.Selector.SingleSelectedThing, "selected pawn remains exactly the same");
            Check.Equal(1, Find.Selector.NumSelected, "selection count remains unchanged");
            Check.Same(Job, Caster.CurJob, "same spell job object remains active");
            Check.Equal(true, Caster.Drafted, "drafted state is unchanged");
            Check.Equal(true, Caster.Spawned, "pawn remains spawned on its original map");
            Check.Same(Map, Caster.Map, "map identity is unchanged");
        }
        public void Dispose() { pathGrid.Dispose(); Map.fogGrid.Dispose(); TM_Calc.InvalidateCompCache(Caster); }
    }
    static bool RunReal() => true;
    static bool SkipGraphics() => false;
    static bool Position(Thing __instance, IntVec3 value)
    {
        if (__instance != active?.Caster) return true;
        active.Map.thingGrid.Deregister(__instance); Set(__instance, "positionInt", value); active.Map.thingGrid.Register(__instance);
        active.Events.Add("Position"); return false;
    }
    static bool Notify(Pawn __instance, bool endCurrentJob)
    {
        if (__instance != active?.Caster) return true;
        active.EndsJob = endCurrentJob; active.Events.Add("Notify"); return false;
    }
    static bool DrawPos(Pawn __instance, ref Vector3 __result)
    {
        if (__instance != active?.Caster) return true;
        __result = __instance.Position.ToVector3Shifted(); return false;
    }
    static bool BlinkDamage(Thing victim, float amt, DamageDef type)
    {
        active!.Events.Add("Stun"); active.BlinkHits.Add((victim, amt, type)); return false;
    }
    static bool PhaseDamage(Pawn victim, int amt, DamageDef type)
    {
        active!.Events.Add("Damage"); active.PhaseHits.Add((victim, amt, type)); return false;
    }
    static bool Undraft(Pawn_DraftController __instance, bool value)
    {
        Set(__instance, "draftedInt", value); return false;
    }
    static bool RemovePossession(Pawn p, IntVec3 loc, bool destroySpirit)
    {
        active!.ReleaseCalls++; active.ReleasedHost = p; active.ReleasedAt = loc; active.DestroySpirit = destroySpirit; return false;
    }
    static IEnumerable<MethodBase> Calls(MethodBase root)
    {
        var processor = Assembly.Load("0Harmony").GetType("HarmonyLib.PatchProcessor")!;
        var read = processor.GetMethods().Single(m => m.Name == "GetOriginalInstructions" && m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType.Name == "ILGenerator");
        var seen = new HashSet<MethodBase>(); var pending = new Stack<MethodBase>(); pending.Push(root);
        while (pending.Count != 0)
        {
            var caller = pending.Pop(); if (!seen.Add(caller)) continue;
            foreach (var instruction in (IEnumerable)read.Invoke(null, [caller, null])!)
            {
                if (instruction.GetType().GetField("operand")!.GetValue(instruction) is not MethodBase called) continue;
                yield return called;
                if ((called.DeclaringType == typeof(TM_Teleport) || called.DeclaringType?.Name == "TM_TravelAutocast") && called.GetMethodBody() != null) pending.Push(called);
            }
        }
    }
    sealed class Patches : IDisposable
    {
        readonly Assembly assembly = Assembly.Load("0Harmony"); readonly object harmony;
        readonly string id = "rwom.regression.teleport-spirit." + Guid.NewGuid();
        public Patches() => harmony = Activator.CreateInstance(assembly.GetType("HarmonyLib.Harmony")!, id)!;
        public void Prefix(MethodBase original, string method)
        {
            var prefix = Activator.CreateInstance(assembly.GetType("HarmonyLib.HarmonyMethod")!, typeof(TeleportSpiritTests).GetMethod(method, Flags)!)!;
            harmony.GetType().GetMethod("Patch")!.Invoke(harmony, [original, prefix, null, null, null]);
        }
        public void Dispose() => harmony.GetType().GetMethod("UnpatchAll")!.Invoke(harmony, [id]);
        public void PatchType(Type type)
        {
            var processor = harmony.GetType().GetMethod("CreateClassProcessor")!.Invoke(harmony, [type])!;
            processor.GetType().GetMethod("Patch")!.Invoke(processor, null);
        }
    }
    static T Blank<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    static object? Get(object target, string name) => Field(target, name).GetValue(target);
    static void Set(object target, string name, object? value) => Field(target, name).SetValue(target, value);
    static FieldInfo Field(object target, string name)
    {
        for (Type? type = target.GetType(); type != null; type = type.BaseType)
            if (type.GetField(name, Flags) is FieldInfo field) return field;
        throw new Exception("Missing fixture field " + name);
    }
}
