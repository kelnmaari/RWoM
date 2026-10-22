using System.Reflection;
using System.Runtime.CompilerServices;
using AbilityUser;
using RimWorld;
using TorannMagic;
using TorannMagic.TMDefs;
using Verse;
using Verse.AI;

namespace RWoM.RegressionTests;

internal static class CastSafetyTests
{
    private static int MessagesSeen;
    public static void Initialize()
    {
        RuntimeHelpers.RunClassConstructor(typeof(MessageTypeDefOf).TypeHandle);
        RuntimeLog.AcceptHeadlessDefOfInitialization("MessageTypeDefOf");
    }
    public static IEnumerable<TestCase> Cases()
    {
        foreach (bool manual in new[] { false, true })
            yield return new("CastSafety.ForbiddenSelf." + (manual ? "Player" : "AI"), f => World(f, w =>
            {
                w.Job.count = manual ? 1 : 0;
                Check.Equal(false, w.Verb.TryStartCastOn(w.Caster), "A permissive custom verb cannot bypass the definition's self ban");
                Check.Equal(0, w.Verb.Shots, "Forbidden self cast never reaches the shot");
            }));
        yield return new("CastSafety.AutocastPolicyRejectsSelf", f => World(f, w =>
            Check.Equal(false, TM_Calc.AutocastTargetAcceptable(w.Caster, w.Def, w.Caster), "Autocast rejects self before creating a job")));
        yield return new("CastSafety.AllowedSelfBuff", f => World(f, w =>
        {
            w.Def.MainVerb.targetParams.canTargetSelf = true;
            Check.Equal(true, w.Verb.TryStartCastOn(w.Caster), "Explicitly allowed self buffs still cast");
            Check.Equal(1, w.Verb.Shots, "Allowed self buff casts once");
        }));
        yield return new("CastSafety.LegacySelfCategory", f => World(f, w =>
        {
            w.Def.MainVerb.AbilityTargetCategory = AbilityTargetCategory.TargetSelf;
            Check.Equal(true, w.Verb.TryStartCastOn(w.Caster), "An explicit self-category ability preserves its legacy semantics");
        }));
        yield return new("CastSafety.AIBlockedAtStart", f => World(f, w =>
        {
            w.Wall(); w.Def.MainVerb.requireLineOfSight = true;
            Check.Equal(false, w.Verb.TryFindShootLineFromTo(w.Caster.Position, w.Target, out _), "Real engine rejects the wall fixture");
            Check.Equal(false, w.Verb.TryStartCastOn(w.Target), "AI cannot start a blocked spell even when its custom CanHit is permissive");
            Check.Equal(0, w.Verb.Shots, "Blocked AI spell does not fire");
        }));
        yield return new("CastSafety.AIDirectPreCastBlocked", f => World(f, w =>
        {
            w.Wall(); w.Def.MainVerb.requireLineOfSight = true;
            Check.Equal(false, w.Verb.PreCastShotCheck(w.Target, LocalTargetInfo.Invalid), "Direct JecsLite start observes the same guard");
            Check.Equal(0, w.Verb.Shots, "Direct pre-cast rejection has no shot");
        }));
        yield return new("CastSafety.AILostSightBeforeProjectileIsQuiet", f => World(f, w =>
        {
            w.Wall(); w.Def.MainVerb.requireLineOfSight = true; w.Def.MainVerb.stopBurstWithoutLos = true;
            Check.Equal(false, w.Verb.TryLaunchProjectileCheck(Blank<ThingDef>(), w.Target), "A newly blocked launch is rejected");
            Check.Equal(0, MessagesSeen, "An automatic LoS rejection must not display player messages");
        }));
        yield return new("CastSafety.ManualBlockedLaunchKeepsFeedback", f => World(f, w =>
        {
            w.Job.count = 1; w.Wall(); w.Def.MainVerb.requireLineOfSight = true; w.Def.MainVerb.stopBurstWithoutLos = true;
            Check.Equal(false, w.Verb.TryLaunchProjectileCheck(Blank<ThingDef>(), w.Target), "Manual blocked launch retains native rejection");
            Check.Equal(1, MessagesSeen, "Manual casts retain their native LoS feedback");
        }));
        yield return new("CastSafety.AIBLOSStillWorks", f => World(f, w =>
        {
            w.Wall(); w.Def.MainVerb.requireLineOfSight = false;
            Check.Equal(true, w.Verb.TryStartCastOn(w.Target), "A spell that ignores LoS still casts through the wall");
            Check.Equal(1, w.Verb.Shots, "BLOS spell reaches its shot");
        }));
        yield return new("CastSafety.AutocastSelectionRejectsBlocked", f => World(f, w =>
        {
            w.Wall(); w.Def.MainVerb.requireLineOfSight = true;
            Check.Equal(false, Select(w, new TM_Autocast { type = AutocastType.OnTarget }, w.Target).IsValid, "Invalid shot is rejected during selection");
        }));
        yield return new("CastSafety.NearbyChoosesVisibleCandidate", f => World(f, w =>
        {
            w.Wall(); w.Def.MainVerb.requireLineOfSight = true;
            var visible = w.Pawn("VisibleAlly", new IntVec3(4, 0, 8));
            var config = new TM_Autocast { type = AutocastType.OnNearby, targetFriendly = true, targetType = "Pawn", includeSelf = false, maxRange = 30f };
            // Put an invalid candidate beside a valid one: filtering must happen before random selection.
            Rand.PushState(81);
            try { for (int i = 0; i < 20; i++) Check.Same(visible, Select(w, config, w.Target).Thing, "Nearby selection excludes blocked candidates"); }
            finally { Rand.PopState(); }
        }));
        yield return new("CastSafety.RejectedJobDoesNotCharge", f => World(f, w =>
        {
            w.Def.MainVerb.requireLineOfSight = true;
            var ability = new RecordingAbility { Def = w.Def }; w.Verb.Ability = ability;
            var driver = new Driver { pawn = w.Caster, job = new Job { def = w.Job.def, targetA = w.Target, verbToUse = w.Verb } };
            var toil = driver.Toils().Last(); toil.actor = w.Caster;
            w.Wall(); // Sight can be lost after toils are prepared, before init.
            toil.initAction(); toil.Cleanup(0, driver);
            Check.Equal(0, w.Verb.Shots, "A rejected job never fires");
            Check.Equal(0, ability.Attempts, "A rejected start cannot charge mana/stamina or set cooldown");
        }));
        yield return new("CastSafety.ColonistAutocastCellRejectionIsQuiet", f => World(f, w =>
        {
            w.Wall(); w.Def.MainVerb.requireLineOfSight = true;
            w.Caster.Faction.def.isPlayer = true;
            var driver = new Driver { pawn = w.Caster, job = new Job { def = w.Job.def, targetA = w.Target.Position, verbToUse = w.Verb } };
            _ = driver.Toils().ToList();
            Check.Equal(0, MessagesSeen, "A colonist's automatic blocked cell cast does not show player-facing LoS messages");
        }));
        foreach (string trait in new[] { "TM_Psionic", "DeathKnight" })
        {
            var traitName = trait;
            yield return new("CastSafety.RejectedSpecialResource." + traitName, f => World(f, w =>
            {
                w.Def.MainVerb.requireLineOfSight = true;
                var traitDef = (TraitDef)typeof(TorannMagicDefOf).GetField(traitName)!.GetValue(null)!;
                w.Caster.story.traits.allTraits.Add(new Trait(traitDef));
                var driver = new Driver { pawn = w.Caster, job = new Job { def = w.Job.def, targetA = w.Target, verbToUse = w.Verb } };
                var toil = driver.Toils().Last(); toil.actor = w.Caster;
                w.Wall(); toil.initAction(); toil.Cleanup(0, driver);
                Check.Equal(0, SpecialCostsSeen, "Rejected start cannot reach the special-resource deduction");
            }));
        }
        yield return new("CastSafety.AcceptedJobStillCharges", f => World(f, w =>
        {
            var ability = new RecordingAbility { Def = w.Def }; w.Verb.Ability = ability;
            var driver = new Driver { pawn = w.Caster, job = new Job { def = w.Job.def, targetA = w.Target, verbToUse = w.Verb } };
            var toil = driver.Toils().Last(); toil.actor = w.Caster; toil.initAction(); toil.Cleanup(0, driver);
            Check.Equal(1, w.Verb.Shots, "Accepted job casts once");
            Check.Equal(1, ability.Attempts, "Accepted job retains its cost/cooldown attempt");
        }));
    }

    private static LocalTargetInfo Select(WorldState w, TM_Autocast config, LocalTargetInfo target)
    {
        var method = typeof(TM_Calc).GetMethod("GetAutocastTarget", [typeof(Pawn), typeof(TM_Autocast), typeof(LocalTargetInfo), typeof(TMAbilityDef), typeof(Verb)]);
        return method == null ? TM_Calc.GetAutocastTarget(w.Caster, config, target, w.Def)
            : (LocalTargetInfo)method.Invoke(null, [w.Caster, config, target, w.Def, w.Verb])!;
    }

    private static void World(RuntimeFixture fixture, Action<WorldState> body)
    {
        var old = Current.Game;
        var oldLanguage = LanguageDatabase.activeLanguage; var oldDefault = LanguageDatabase.defaultLanguage;
        var language = Blank<LoadedLanguage>();
        language.folderName = "English"; Set(language, "dataIsLoaded", true); Set(language, "workerInt", new LanguageWorker_English());
        language.keyedReplacements = new Dictionary<string, LoadedLanguage.KeyedReplacement>
        {
            ["AU_NoLineOfSight"] = new() { key = "AU_NoLineOfSight", value = "No line of sight" },
            ["TM_OutOfLOS"] = new() { key = "TM_OutOfLOS", value = "No line of sight: {0}" }
        };
        LanguageDatabase.activeLanguage = LanguageDatabase.defaultLanguage = language;
        var game = Blank<Game>(); Set(game, "tickManager", new TickManager()); Set(game, "uniqueIDsManager", new UniqueIDsManager()); Current.Game = game;
        using var patches = new Patches();
        try
        {
            MessagesSeen = SpecialCostsSeen = 0; patches.RecordMessages(); patches.RecordSpecialCosts();
            foreach (string name in new[] { "AbilityCastStartPatch", "AbilityPreCastStartPatch", "AbilityProjectileLaunchSafetyPatch" })
                if (typeof(MagicAbility).Assembly.GetType("TorannMagic.Compatibility." + name) is Type type) patches.Add(type);
            body(new WorldState(fixture, game));
        }
        finally { Current.Game = old; LanguageDatabase.activeLanguage = oldLanguage; LanguageDatabase.defaultLanguage = oldDefault; }
    }

    private sealed class WorldState
    {
        public readonly Map Map;
        public readonly Pawn Caster, Target;
        public readonly TMAbilityDef Def;
        public readonly RecordingVerb Verb;
        public readonly Job Job;
        private readonly Faction faction = new() { def = new FactionDef() };
        public WorldState(RuntimeFixture fixture, Game game)
        {
            Map = Blank<Map>(); Map.info = new MapInfo { Size = new IntVec3(20, 1, 20) };
            Set(game, "maps", new List<Map> { Map });
            Map.cellIndices = new CellIndices(Map); Map.thingGrid = new ThingGrid(Map);
            Map.edificeGrid = new EdificeGrid(Map); Map.mapPawns = new MapPawns(Map);
            Map.coverGrid = new CoverGrid(Map);
            Map.fogGrid = new FogGrid(Map);
            Map.reservationManager = new ReservationManager(Map); Map.physicalInteractionReservationManager = new PhysicalInteractionReservationManager();
            Map.pawnDestinationReservationManager = new PawnDestinationReservationManager(); Map.enrouteManager = new EnrouteManager(Map);
            Map.attackTargetReservationManager = new AttackTargetReservationManager(Map);
            Caster = Pawn("CastSafetyCaster", new IntVec3(4, 0, 4)); Target = Pawn("CastSafetyTarget", new IntVec3(15, 0, 8));
            Def = fixture.AddAbility("TM_CastSafetyRegression");
            Def.MainVerb.AbilityTargetCategory = AbilityTargetCategory.TargetThing;
            Def.MainVerb.targetParams = new TargetingParameters { canTargetPawns = true, canTargetSelf = false };
            Def.MainVerb.requireLineOfSight = false; Def.MainVerb.warmupTime = 0f; Def.MainVerb.range = 30f;
            Def.MainVerb.isViolent = false;
            Def.MainVerb.ai_IsWeapon = false; Def.MainVerb.verbClass = typeof(RecordingVerb);
            Verb = new RecordingVerb { caster = Caster, verbProps = Def.MainVerb, verbTracker = new VerbTracker(Caster), Ability = new MagicAbility { Def = Def } };
            TorannMagicDefOf.TMCastAbilityVerb ??= new JobDef { defName = "TMCastAbilityVerb" };
            Job = new Job { def = TorannMagicDefOf.TMCastAbilityVerb, verbToUse = Verb, count = 0, targetA = Target };
            Set(Caster.jobs, "curJob", Job);
        }
        public Pawn Pawn(string name, IntVec3 position)
        {
            var def = Blank<ThingDef>(); def.defName = name; def.category = ThingCategory.Pawn; def.size = new IntVec2(1, 1);
            def.race = new RaceProperties { intelligence = Intelligence.Humanlike };
            var pawn = new Pawn { def = def }; pawn.health = new Pawn_HealthTracker(pawn); pawn.jobs = new Pawn_JobTracker(pawn);
            pawn.mindState = new Pawn_MindState(pawn); Set(pawn, "factionInt", faction);
            pawn.kindDef = new PawnKindDef { defName = name, race = def };
            pawn.guest = new Pawn_GuestTracker(pawn);
            pawn.story = new Pawn_StoryTracker(pawn) { traits = new TraitSet(pawn) };
            Set(pawn, "comps", new List<ThingComp>());
            Set(pawn, "mapIndexOrState", (sbyte)0); Set(pawn, "positionInt", position);
            Map.thingGrid.Register(pawn); ((IList<Pawn>)Map.mapPawns.AllPawnsSpawned).Add(pawn);
            return pawn;
        }
        public void Wall()
        {
            var def = Blank<ThingDef>(); def.defName = "CastSafetyWall"; def.size = new IntVec2(1, 1); def.fillPercent = 1f;
            def.blockLight = true; def.passability = Traversability.Impassable; def.category = ThingCategory.Building;
            for (int z = 0; z < 20; z++)
            {
                var building = new Building { def = def }; Set(building, "positionInt", new IntVec3(10, 0, z));
                Set(building, "mapIndexOrState", (sbyte)0); Map.edificeGrid.Register(building);
            }
        }
    }
    private sealed class RecordingVerb : Verb_UseAbility
    {
        public int Shots;
        public override float EffectiveRange => 30f;
        public override bool CanHitTargetFrom(IntVec3 root, LocalTargetInfo target) => target.IsValid;
        public override void WarmupComplete() => TryCastShot();
        protected override bool TryCastShot() { Shots++; return true; }
    }
    private sealed class RecordingAbility : PawnAbility
    {
        public int Attempts;
        public override void PostAbilityAttempt() => Attempts++;
    }
    private sealed class Driver : TMJobDriver_CastAbilityVerb
    {
        public IEnumerable<Toil> Toils() => MakeNewToils();
    }
    private static bool RecordMessage() { MessagesSeen++; return false; }
    private static int SpecialCostsSeen;
    private static bool RecordSpecialCost() { SpecialCostsSeen++; return false; }
    private sealed class Patches : IDisposable
    {
        private readonly Assembly assembly = Assembly.Load("0Harmony");
        private readonly object harmony;
        private readonly string id = "rwom.cast-safety-test." + Guid.NewGuid();
        public Patches() => harmony = Activator.CreateInstance(assembly.GetType("HarmonyLib.Harmony")!, id)!;
        public void Add(Type type)
        {
            var processor = harmony.GetType().GetMethod("CreateClassProcessor")!.Invoke(harmony, [type])!;
            processor.GetType().GetMethod("Patch")!.Invoke(processor, null);
        }
        public void RecordMessages()
        {
            var hook = Activator.CreateInstance(assembly.GetType("HarmonyLib.HarmonyMethod")!, typeof(CastSafetyTests).GetMethod(nameof(RecordMessage), BindingFlags.NonPublic | BindingFlags.Static)!)!;
            var original = typeof(Messages).GetMethods().Single(m => m.Name == "Message" && m.GetParameters() is var p && p.Length == 3 && p[1].ParameterType == typeof(MessageTypeDef) && p[2].ParameterType == typeof(bool));
            harmony.GetType().GetMethod("Patch")!.Invoke(harmony, [original, hook, null, null, null]);
        }
        public void RecordSpecialCosts()
        {
            var hook = Activator.CreateInstance(assembly.GetType("HarmonyLib.HarmonyMethod")!, typeof(CastSafetyTests).GetMethod(nameof(RecordSpecialCost), BindingFlags.NonPublic | BindingFlags.Static)!)!;
            foreach (string name in new[] { "PsionicEnergyCost", "HateCost" })
                harmony.GetType().GetMethod("Patch")!.Invoke(harmony, [typeof(TMJobDriver_CastAbilityVerb).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!, hook, null, null, null]);
        }
        public void Dispose() => harmony.GetType().GetMethod("UnpatchAll")!.Invoke(harmony, [id]);
    }
    private static T Blank<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    private static void Set(object target, string name, object value) => FleshStatTests.Set(target, name, value);
}
