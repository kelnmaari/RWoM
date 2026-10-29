using RimWorld;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using TorannMagic;
using TorannMagic.ClassProgression;
using Verse;

namespace RWoM.RegressionTests;

internal static class ClassInitiationTests
{
    sealed class MapScope : IDisposable
    {
        readonly Game previous = Current.Game;
        public MapScope()
        {
            Current.Game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
            FleshStatTests.Set(Current.Game, "maps", new List<Map> { (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map)) });
            FleshStatTests.Set(Current.Game, "tickManager", new TickManager());
            FleshStatTests.Set(Current.Game, "uniqueIDsManager", new UniqueIDsManager());
        }
        public void Dispose() => Current.Game = previous;
    }
    static bool SkipPhysiology() => false;

    public sealed class Envelope : IExposable
    {
        public MagicData magic = new();
        public CompClassProgression progression = new();
        public void ExposeData()
        {
            Scribe_Deep.Look(ref magic, "magicData");
            progression.PostExposeData();
        }
    }

    static Envelope RoundTrip(RuntimeFixture f, Envelope data, string name, Action<XDocument>? edit = null)
    {
        var path = Path.Combine(f.WorkingDirectory, name + ".xml");
        try { Scribe.saver.InitSaving(path, "regression"); Scribe_Deep.Look(ref data, "pawnData"); Scribe.saver.FinalizeSaving(); }
        finally { Scribe.ForceStop(); }
        if (edit != null) { var xml = XDocument.Load(path); edit(xml); xml.Save(path); }
        Envelope loaded = null!;
        try { Scribe.loader.InitLoading(path); Scribe_Deep.Look(ref loaded, "pawnData"); Scribe.loader.FinalizeLoading(); }
        finally { Scribe.ForceStop(); }
        RuntimeLog.AssertClean();
        return loaded;
    }

    public static IEnumerable<TestCase> Cases()
    {
        yield return new("ClassInitiation.EmptyComponentsRemainClassic", f =>
        {
            GrimoireProgressionTests.LoadBaseDefinitions(f);
            var p = GrimoireProgressionTests.PawnWithComps(false, false).pawn;
            p.AllComps.Clear();
            Check.Equal(true, ClassIdentity.Component(p) == null, "empty component list has no enrollment");
            Check.Equal(false, ClassIdentity.TryGetState(p, out _), "editor-created pawn remains classic");
            Check.Equal(false, ClassIdentity.TryGetState(null, out _), "absent pawn has no state");
            Check.Equal(true, PriestHealingTrial.Capture(p, new Pawn(), null) == null, "classic healing ignores trial target internals");
            Check.Equal(true, PriestPurificationTrial.Capture(p, new Pawn(), null) == null, "classic purification ignores trial target internals");
        });
        yield return new("ClassInitiation.NoviceChoicesPersistAndDoNotDuplicate", f =>
        {
            using var map = new MapScope();
            GrimoireProgressionTests.LoadBaseDefinitions(f);
            foreach (var a in DefDatabase<TMAbilityDef>.AllDefsListForReading) { a.abilityClass = typeof(AbilityUser.PawnAbility); a.shouldInitialize = true; }
            var p = GrimoireProgressionTests.PawnWithComps(true, false);
            FleshStatTests.Set(p.pawn, "mapIndexOrState", (sbyte)0);
            var c = new CompClassProgression { parent = p.pawn }; p.pawn.AllComps.Add(c); c.BeginNewProgression(Vocation.Mage);
            p.magic.MagicData.MagicAbilityPoints = 7;
            p.magic.spell_Heater = true;
            Check.Equal(false, NoviceLearning.TryLearn(p.pawn, "TM_Heater"), "prior scroll ownership cannot waste a novice choice");
            Check.Equal(0, c.State.NoviceSpells.Count, "known spell costs no choice");
            Check.Equal(false, NoviceLearning.TryLearn(p.pawn, "TM_Resurrection"), "class ultimate not a novice choice");
            Check.Equal(true, p.pawn.Spawned, "novice spawned");
            Check.Equal(false, p.pawn.Downed, "novice mobile");
            Check.Equal(false, p.pawn.Dead, "novice alive");
            Check.Equal(true, ClassIdentity.TryGetState(p.pawn, out _), "novice enrolled");
            Check.Equal(true, NoviceLearning.Options.Any(d => d.defName == "TM_ArcaneBolt"), "novice arcane bolt definition available");
            Check.Equal(true, p.pawn.GetCompAbilityUserMagic()?.MagicData != null, "novice owner available");
            Check.Equal(true, NoviceLearning.TryLearn(p.pawn, "TM_ArcaneBolt"), "first chosen cantrip");
            var runtime = p.magic.AbilityData.Powers.Single(a => a.Def == TorannMagicDefOf.TM_ArcaneBolt); runtime.CooldownTicksLeft = 99;
            Check.Equal(false, NoviceLearning.TryLearn(p.pawn, "TM_ArcaneBolt"), "repeated selection refused");
            Check.Equal(true, NoviceLearning.TryLearn(p.pawn, "TM_Extinguish"), "flag-owned support spell is a valid second choice");
            Check.Equal(false, NoviceLearning.TryLearn(p.pawn, "TM_MageLight"), "two choices exhaust novice budget");
            Check.Equal(7, p.magic.MagicData.MagicAbilityPoints, "novice choices do not spend or multiply XP points");
            Check.Equal(99, runtime.CooldownTicksLeft, "second choice retains first spell cooldown");
            var loaded = RoundTrip(f, new Envelope { progression = c }, "novice-choices");
            Check.Equal(2, loaded.progression.State.NoviceSpells.Count, "choice budget persists");
            Check.Equal(false, loaded.progression.State.TryChooseNoviceSpell("TM_MageLight"), "load cannot refund choices");
        });
        yield return new("ClassInitiation.BookPaymentAtCompletionOnly", f =>
        {
            GrimoireProgressionTests.LoadBaseDefinitions(f);
            foreach (var a in DefDatabase<TMAbilityDef>.AllDefsListForReading) { a.abilityClass = typeof(AbilityUser.PawnAbility); a.shouldInitialize = true; }
            var saved = DefDatabase<ClassDisciplineDef>.AllDefsListForReading.ToList();
            var previousGame = Current.Game;
            var savedDesignations = DefDatabase<DesignationDef>.AllDefsListForReading.ToList();
            DefDatabase<DesignationDef>.Add(new DesignationDef { defName = "InitiationTestDesignation" });
            try
            {
                Current.Game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
                FleshStatTests.Set(Current.Game, "uniqueIDsManager", new UniqueIDsManager());
                FleshStatTests.Set(Current.Game, "tickManager", new TickManager());
                var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
                map.listerMergeables = new ListerMergeables(map); map.designationManager = new DesignationManager(map);
                map.reservationManager = new Verse.AI.ReservationManager(map);
                map.physicalInteractionReservationManager = new PhysicalInteractionReservationManager();
                map.events = new MapEvents(map); map.lordManager = new Verse.AI.Group.LordManager(map);
                FleshStatTests.Set(Current.Game, "maps", new List<Map> { map });
                var p = GrimoireProgressionTests.PawnWithComps(true, false);
                FleshStatTests.Set(p.pawn, "mapIndexOrState", (sbyte)0);
                FleshStatTests.Set(p.pawn, "positionInt", new IntVec3(5, 0, 5));
                var comp = new CompClassProgression { parent = p.pawn }; p.pawn.AllComps.Add(comp); comp.BeginNewProgression(Vocation.Mage);
                var bookDef = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
                bookDef.defName = "InitiationBook"; bookDef.thingClass = typeof(Thing); bookDef.category = ThingCategory.Item; bookDef.destroyable = true; bookDef.stackLimit = 75; bookDef.drawerType = DrawerType.RealtimeOnly;
                var book = new Thing { def = bookDef, stackCount = 2 };
                var adeptDef = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
                adeptDef.defName = "AdeptBook"; adeptDef.thingClass = typeof(Thing); adeptDef.category = ThingCategory.Item;
                adeptDef.destroyable = true; adeptDef.stackLimit = 75; adeptDef.drawerType = DrawerType.RealtimeOnly;
                var masterDef = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
                masterDef.defName = "MasterBook"; masterDef.thingClass = typeof(Thing); masterDef.category = ThingCategory.Item;
                masterDef.destroyable = true; masterDef.stackLimit = 75; masterDef.drawerType = DrawerType.RealtimeOnly;
                FleshStatTests.Set(book, "mapIndexOrState", (sbyte)0); FleshStatTests.Set(book, "positionInt", new IntVec3(6, 0, 5));
                DefDatabase<ClassDisciplineDef>.Add(new ClassDisciplineDef { defName = "InitiationPriest", profileId = "rwom.priest", domain = DisciplineDomain.Magic,
                    legacyTrait = TorannMagicDefOf.Priest, magicPowerList = "magicPowerPR", foundationBook = bookDef,
                    adeptBook = adeptDef, adeptTrialId = "rwom.priest.heal-ally", masterBook = masterDef, masterTrialId = "rwom.priest.purify-ally",
                    spells = [new DisciplineSpell { ability = TorannMagicDefOf.TM_AdvancedHeal, stage = InitiationStage.Foundation }, new DisciplineSpell { ability = TorannMagicDefOf.TM_Purify, stage = InitiationStage.Foundation }, new DisciplineSpell { ability = (TMAbilityDef)p.magic.MagicData.MagicPowersPR[2].TMabilityDefs[0], stage = InitiationStage.Adept }, new DisciplineSpell { ability = (TMAbilityDef)p.magic.MagicData.MagicPowersPR[3].TMabilityDefs[0], stage = InitiationStage.Master }, new DisciplineSpell { ability = TorannMagicDefOf.TM_Resurrection, stage = InitiationStage.Ascended }] });
                Check.Equal(true, ClassInitiation.CanUseFoundation(p.pawn, book, out _), "preflight succeeds");
                Check.Equal(2, book.stackCount, "preflight and interrupted job pay nothing");
                Check.Equal(0, comp.State.Disciplines.Count, "preflight grants nothing");
                FleshStatTests.Set(book, "positionInt", new IntVec3(15, 0, 15));
                Check.Equal(false, ClassInitiation.CompleteBook(p.pawn, book), "stale distant target refused");
                FleshStatTests.Set(book, "mapIndexOrState", (sbyte)-1);
                p.pawn.inventory = new Pawn_InventoryTracker(p.pawn);
                Check.Equal(true, p.pawn.inventory.innerContainer.TryAdd(book), "native inventory receives the book");
                Check.Equal(true, ClassInitiation.CompleteBook(p.pawn, book), "completion grants class");
                Check.Equal(1, book.stackCount, "exactly one book spent");
                Check.Equal(2, p.magic.MagicData.MagicPowersPR.Count(x => x.learned), "two foundation spells taught");
                Check.Equal(false, ClassInitiation.CompleteBook(p.pawn, book), "repeat is refused");
                Check.Equal(1, book.stackCount, "repeat never pays twice");
                var fullBook = new Thing { def = adeptDef, stackCount = 2 };
                Check.Equal(true, p.pawn.inventory.innerContainer.TryAdd(fullBook), "native inventory receives full tome");
                Check.Equal(false, ClassInitiation.CompleteBook(p.pawn, fullBook), "trial cannot be bypassed by a full tome");
                Check.Equal(2, fullBook.stackCount, "failed trial gate costs nothing");
                comp.State.Find("rwom.priest").RecordCompletedTrial("rwom.priest.heal-ally");
                Check.Equal(true, ClassInitiation.CompleteBook(p.pawn, fullBook), "completed trial unlocks full tome ritual");
                Check.Equal(InitiationStage.Adept, comp.State.Find("rwom.priest").Stage, "full tome advances only one stage");
                Check.Equal(1, fullBook.stackCount, "one full tome consumed");
                Check.Equal(true, p.magic.MagicData.MagicPowersPR[2].learned, "healing circle learned");
                Check.Equal(false, p.magic.MagicData.MagicPowersPR[3].learned, "master spell remains locked");
                Check.Equal(false, p.magic.MagicData.MagicPowersPR[4].learned, "ultimate remains locked");
                Check.Equal(false, ClassInitiation.CompleteBook(p.pawn, fullBook), "repeated adept ritual refused");
                Check.Equal(1, fullBook.stackCount, "repeat preserves remaining tome");
                var loaded = RoundTrip(f, new Envelope { progression = comp }, "priest-adept");
                Check.Equal(InitiationStage.Adept, loaded.progression.State.Find("rwom.priest").Stage, "paid stage survives Scribe");
                var masterBook = new Thing { def = masterDef, stackCount = 2 };
                Check.Equal(true, p.pawn.inventory.innerContainer.TryAdd(masterBook), "master tome native ownership");
                Check.Equal(false, ClassInitiation.CompleteBook(p.pawn, masterBook), "first trial cannot unlock master stage");
                Check.Equal(2, masterBook.stackCount, "locked master tome retained");
                comp.State.Find("rwom.priest").RecordCompletedTrial("rwom.priest.purify-ally");
                Check.Equal(true, ClassInitiation.CompleteBook(p.pawn, masterBook), "purification trial unlocks master tome");
                Check.Equal(1, masterBook.stackCount, "one master tome spent");
                Check.Equal(InitiationStage.Master, comp.State.Find("rwom.priest").Stage, "master stage committed");
                Check.Equal(true, p.magic.MagicData.MagicPowersPR[3].learned, "Bestow Might unlocked");
                Check.Equal(false, p.magic.MagicData.MagicPowersPR[4].learned, "master tome does not bypass ascension");
                Check.Equal(false, ClassInitiation.CompleteBook(p.pawn, masterBook), "master ritual cannot repeat");
                Check.Equal(1, masterBook.stackCount, "repeat never charges master tome twice");
                loaded = RoundTrip(f, new Envelope { progression = comp }, "priest-master");
                Check.Equal(InitiationStage.Master, loaded.progression.State.Find("rwom.priest").Stage, "master stage persists");
                var circleDef = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
                circleDef.defName = "TestLargeCircle"; circleDef.thingClass = typeof(Building_TMMagicCircle);
                circleDef.hasInteractionCell = true; circleDef.interactionCellOffset = new IntVec3(0, 0, -1);
                circleDef.size = new IntVec2(3, 3);
                var circle = new Thing { def = circleDef };
                p.pawn.mindState = new Verse.AI.Pawn_MindState(p.pawn);
                var playerFaction = new Faction { def = new FactionDef { isPlayer = true } };
                var world = (RimWorld.Planet.World)RuntimeHelpers.GetUninitializedObject(typeof(RimWorld.Planet.World));
                world.factionManager = new FactionManager(); world.factionManager.AllFactionsListForReading.Add(playerFaction);
                FleshStatTests.Set(world.factionManager, "ofPlayer", playerFaction); FleshStatTests.Set(Current.Game, "worldInt", world);
                FleshStatTests.Set(p.pawn, "factionInt", playerFaction); FleshStatTests.Set(circle, "factionInt", playerFaction);
                FleshStatTests.Set(circle, "mapIndexOrState", (sbyte)0); FleshStatTests.Set(circle, "positionInt", new IntVec3(5, 0, 6));
                var scrollDef = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
                scrollDef.defName = "SpellOf_Resurrection"; scrollDef.thingClass = typeof(Thing); scrollDef.category = ThingCategory.Item;
                scrollDef.destroyable = true; scrollDef.stackLimit = 75; scrollDef.drawerType = DrawerType.RealtimeOnly;
                var scroll = new Thing { def = scrollDef, stackCount = 2 };
                p.pawn.carryTracker = new Pawn_CarryTracker(p.pawn);
                Check.Equal(true, p.pawn.carryTracker.innerContainer.TryAdd(scroll), "native carry owns offering");
                Check.Equal(true, ClassAscension.CanBegin(p.pawn, circle, scroll), "master can begin ritual");
                Check.Equal(2, scroll.stackCount, "preflight/interruption never pays");
                FleshStatTests.Set(p.pawn, "positionInt", new IntVec3(15, 0, 15));
                Check.Equal(false, ClassAscension.Complete(p.pawn, circle, scroll), "remote offering refused");
                FleshStatTests.Set(p.pawn, "positionInt", circle.InteractionCell);
                Check.Equal(true, ClassAscension.Complete(p.pawn, circle, scroll), "ritual consumes offering and ascends");
                Check.Equal(1, scroll.stackCount, "only one offering spent");
                Check.Equal(true, p.magic.MagicData.MagicPowersPR[4].learned, "ascension teaches resurrection");
                Check.Equal(InitiationStage.Ascended, comp.State.Find("rwom.priest").Stage, "ascension state committed");
                Check.Equal(false, ClassAscension.Complete(p.pawn, circle, scroll), "ascension cannot repeat");
                Check.Equal(1, scroll.stackCount, "repeat preserves second offering");
                loaded = RoundTrip(f, new Envelope { progression = comp }, "priest-ascended");
                Check.Equal(InitiationStage.Ascended, loaded.progression.State.Find("rwom.priest").Stage, "ascension survives Scribe");
            }
            finally { DefDatabase<DesignationDef>.Clear(); foreach (var d in savedDesignations) DefDatabase<DesignationDef>.Add(d); Current.Game = previousGame; DefDatabase<ClassDisciplineDef>.Clear(); foreach (var d in saved) DefDatabase<ClassDisciplineDef>.Add(d); }
        });
        yield return new("ClassInitiation.PriestStagesAndLearnedPreservation", f =>
        {
            GrimoireProgressionTests.LoadBaseDefinitions(f);
            var prior = DefDatabase<ClassDisciplineDef>.AllDefsListForReading.ToList();
            var xml = XDocument.Load(Path.Combine(f.Repository, "v1.7/Defs/CustomDefs/TM_ClassDisciplines.xml"));
            var node = xml.Root!.Elements().Single(e => e.Element("profileId")?.Value == "rwom.priest");
            var profile = new ClassDisciplineDef { defName = "TM_PathPriest", profileId = "rwom.priest", domain = DisciplineDomain.Magic,
                magicPowerList = node.Element("magicPowerList")!.Value,
                spells = node.Element("spells")!.Elements().Select(e => new DisciplineSpell { ability = DefDatabase<TMAbilityDef>.GetNamed(e.Element("ability")!.Value), stage = Enum.Parse<InitiationStage>(e.Element("stage")!.Value) }).ToList() };
            DefDatabase<ClassDisciplineDef>.Add(profile);
            try
            {
                var p = GrimoireProgressionTests.PawnWithComps(true, false);
                var c = new CompClassProgression { parent = p.pawn }; p.pawn.AllComps.Add(c); c.BeginNewProgression(Vocation.Mage);
                Check.Equal(false, ClassSpellAccess.CanLearn(p.pawn, p.magic.MagicData.MagicPowersPR[0]), "novice cannot bypass class initiation");
                c.State.TryAddFoundation("rwom.priest", DisciplineDomain.Magic);
                var powers = TM_ClassGroups.GetMagicClassGroup(p.pawn, p.magic.MagicData);
                Check.Equal(5, powers.Count, "all five path spells visible without trait");
                Check.Same(p.magic.MagicData.MagicPowersPR[0], powers[0], "original saved owner is retained");
                Check.Equal(true, ClassSpellAccess.CanLearn(p.pawn, powers[0]), "foundation heal available");
                Check.Equal(true, ClassSpellAccess.CanLearn(p.pawn, powers[1]), "foundation purify available");
                for (int n = 2; n < 5; n++) Check.Equal(false, ClassSpellAccess.CanLearn(p.pawn, powers[n]), "later spells locked");
                powers[4].learned = true;
                Check.Equal(true, ClassSpellAccess.CanLearn(p.pawn, powers[4]), "preserved ultimate remains usable below new stage");
                FleshStatTests.Set(c.State.Find("rwom.priest"), "stage", (InitiationStage)99);
                Check.Equal(false, ClassSpellAccess.CanLearn(p.pawn, powers[2]), "unknown saved stage cannot unlock new abilities");
                Check.Equal(true, ClassSpellAccess.CanLearn(p.pawn, powers[4]), "unknown stage does not strip a learned ultimate");
                var old = GrimoireProgressionTests.PawnWithComps(true, false);
                Check.Equal(true, ClassSpellAccess.CanLearn(old.pawn, old.magic.MagicData.MagicPowersPR[4]), "classic ultimate rules not replaced");
            }
            finally { DefDatabase<ClassDisciplineDef>.Clear(); foreach (var d in prior) DefDatabase<ClassDisciplineDef>.Add(d); }
        });
        yield return new("ClassInitiation.InvalidOptionalProfileDoesNotErasePath", f =>
        {
            GrimoireProgressionTests.LoadBaseDefinitions(f);
            var prior = DefDatabase<ClassDisciplineDef>.AllDefsListForReading.ToList();
            try
            {
                DefDatabase<ClassDisciplineDef>.Add(new ClassDisciplineDef { defName = "MalformedOptionalPath", profileId = "addon.malformed", domain = DisciplineDomain.Magic, magicPowerList = "x" });
                var p = GrimoireProgressionTests.PawnWithComps(true, false);
                var comp = new CompClassProgression { parent = p.pawn }; p.pawn.AllComps.Add(comp); comp.BeginNewProgression(Vocation.Mage);
                comp.State.TryAddFoundation("addon.malformed", DisciplineDomain.Magic);
                Check.Equal(0, ClassSpellAccess.ClassPowers(p.pawn, p.magic.MagicData).Count, "malformed optional profile cannot break grimoire lookup");
                Check.Equal(1, comp.State.Disciplines.Count, "unresolved path remains saved for repair");
            }
            finally { DefDatabase<ClassDisciplineDef>.Clear(); foreach (var d in prior) DefDatabase<ClassDisciplineDef>.Add(d); }
        });
        yield return new("ClassInitiation.MissingPriestProfileCannotBypassAscension", f =>
        {
            GrimoireProgressionTests.LoadBaseDefinitions(f);
            var prior = DefDatabase<ClassDisciplineDef>.AllDefsListForReading.ToList();
            try
            {
                DefDatabase<ClassDisciplineDef>.Clear();
                var p = GrimoireProgressionTests.PawnWithComps(true, false);
                var comp = new CompClassProgression { parent = p.pawn }; p.pawn.AllComps.Add(comp); comp.BeginNewProgression(Vocation.Mage);
                comp.State.TryAddFoundation("rwom.priest", DisciplineDomain.Magic);
                var ultimate = p.magic.MagicData.MagicPowersPR[4];
                Check.Equal(false, ClassSpellAccess.CanLearn(p.pawn, ultimate), "missing metadata does not unlock a scroll");
                DefDatabase<ClassDisciplineDef>.Add(new ClassDisciplineDef { defName = "PartialPriest", profileId = "rwom.priest", domain = DisciplineDomain.Magic });
                Check.Equal(false, ClassSpellAccess.CanLearn(p.pawn, ultimate), "partial metadata does not unlock a scroll");
                ultimate.learned = true;
                Check.Equal(true, ClassSpellAccess.CanLearn(p.pawn, ultimate), "missing metadata never removes existing ownership");
            }
            finally { DefDatabase<ClassDisciplineDef>.Clear(); foreach (var d in prior) DefDatabase<ClassDisciplineDef>.Add(d); }
        });
        yield return new("ClassInitiation.RestoreAndRecruitNeverReroll", f =>
        {
            GrimoireProgressionTests.LoadBaseDefinitions(f);
            foreach (var ability in DefDatabase<TMAbilityDef>.AllDefsListForReading) { ability.abilityClass = typeof(AbilityUser.PawnAbility); ability.shouldInitialize = true; }
            var p = GrimoireProgressionTests.PawnWithComps(true, false);
            var c = new CompClassProgression { parent = p.pawn }; p.pawn.AllComps.Add(c); c.BeginNewProgression(Vocation.Mage);
            c.State.TryAddFoundation("rwom.priest", DisciplineDomain.Magic);
            var heal = p.magic.MagicData.MagicPowersPR[0]; heal.learned = true;
            p.magic.MagicData.MagicAbilityPoints = 9;
            TM_ClassState.RestoreCurrentMagicRuntime(p.magic, true);
            var runtime = p.magic.AbilityData.Powers.Single(); runtime.CooldownTicksLeft = 123;
            p.magic.InitializeSpell(); p.magic.ResolveFactionChange(); TM_ClassState.RestoreCurrentMagicRuntime(p.magic);
            Check.Same(runtime, p.magic.AbilityData.Powers.Single(), "restore/recruit keeps runtime and cooldown");
            Check.Equal(123, runtime.CooldownTicksLeft, "cooldown retained");
            Check.Equal(1, p.magic.MagicData.AllMagicPowersWithSkills.Count(x => x.learned), "no random new spells on recovery or recruitment");
            Check.Equal(9, p.magic.MagicData.MagicAbilityPoints, "no payment or reward on restore");
            p.magic.ResetSkills();
            Check.Equal(true, heal.learned, "legacy reset cannot erase enrolled learned ownership");
            Check.Same(runtime, p.magic.AbilityData.Powers.Single(), "legacy reset cannot strand an enrolled priest without healing");
            Check.Equal(9, p.magic.MagicData.MagicAbilityPoints, "legacy reset cannot erase enrolled points");
        });
        yield return new("ClassInitiation.IdentityOverridesOnlyEnrolledPawns", f =>
        {
            GrimoireProgressionTests.LoadBaseDefinitions(f);
            foreach (var vocation in new[] { Vocation.Mage, Vocation.Warrior, Vocation.WarriorMage })
            {
                var p = GrimoireProgressionTests.PawnWithComps(false, false);
                var comp = new CompClassProgression { parent = p.pawn };
                p.pawn.AllComps.Add(comp);
                Check.Equal(false, p.magic.IsMagicUser, "passive component does not awaken magic");
                Check.Equal(false, p.might.IsMightUser, "passive component does not awaken might");
                comp.BeginNewProgression(vocation);
                Check.Equal((vocation & Vocation.Mage) != 0, p.magic.IsMagicUser, "saved vocation overrides stale magic cache");
                Check.Equal((vocation & Vocation.Warrior) != 0, p.might.IsMightUser, "saved vocation overrides stale might cache");
                Check.Equal(0, p.pawn.story.traits.allTraits.Count, "identity does not fabricate legacy class traits");
            }
            var classic = GrimoireProgressionTests.PawnWithComps(true, false);
            classic.pawn.AllComps.Add(new CompClassProgression { parent = classic.pawn });
            Check.Equal(true, classic.magic.IsMagicUser, "classic/addon cached identity remains intact");
            Check.Equal(false, classic.might.IsMightUser, "classic inactive domain remains inactive");
        });
        yield return new("ClassInitiation.HealthMarkersAreDisplayOnly", f =>
        {
            GrimoireProgressionTests.LoadBaseDefinitions(f);
            var p = GrimoireProgressionTests.PawnWithComps(true, false);
            var comp = new CompClassProgression { parent = p.pawn };
            p.pawn.AllComps.Add(comp);
            comp.BeginNewProgression(Vocation.Mage);
            comp.State.TryAddFoundation("rwom.fire", DisciplineDomain.Magic);
            comp.State.TryAddFoundation("rwom.ice", DisciplineDomain.Magic);
            p.pawn.def.race.body = new BodyDef { corePart = new BodyPartRecord { def = new BodyPartDef { defName = "ProgressionBody" } } };
            var def = new HediffDef { defName = "TM_ClassDiscipline", hediffClass = typeof(Hediff_ClassDiscipline), initialSeverity = 1 };
            var savedCapacities = DefDatabase<PawnCapacityDef>.AllDefsListForReading.ToList();
            DefDatabase<PawnCapacityDef>.Add(new PawnCapacityDef { defName = "ProgressionTestCapacity" });
            var savedDefs = DefDatabase<HediffDef>.AllDefsListForReading.ToList();
            DefDatabase<HediffDef>.Add(def);
            var previousGame = Current.Game;
            Current.Game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
            FleshStatTests.Set(Current.Game, "uniqueIDsManager", new UniqueIDsManager());
            FleshStatTests.Set(Current.Game, "tickManager", new TickManager());
            var assembly = Assembly.Load("0Harmony");
            var harmony = Activator.CreateInstance(assembly.GetType("HarmonyLib.Harmony")!, "rwom.tests.class-marker")!;
            // Preserve real hediff ownership/callbacks; headless fixture lacks the physiological databases.
            var prefix = Activator.CreateInstance(assembly.GetType("HarmonyLib.HarmonyMethod")!, typeof(ClassInitiationTests).GetMethod(nameof(SkipPhysiology), BindingFlags.NonPublic | BindingFlags.Static)!)!;
            harmony.GetType().GetMethod("Patch")!.Invoke(harmony, [typeof(Pawn_HealthTracker).GetMethod("CheckForStateChange"), prefix, null, null, null]);
            try
            {
                comp.SynchronizeHealthDisplay(); comp.SynchronizeHealthDisplay();
                var markers = p.pawn.health.hediffSet.hediffs.OfType<Hediff_ClassDiscipline>().ToList();
                Check.Equal(2, markers.Count, "repeated sync creates one marker per direction");
                p.pawn.health.RemoveHediff(markers[0]);
                Check.Equal(2, comp.State.Disciplines.Count, "removing display cannot erase class data");
                comp.SynchronizeHealthDisplay();
                Check.Equal(2, p.pawn.health.hediffSet.hediffs.OfType<Hediff_ClassDiscipline>().Count(), "missing display is restored");
            }
            finally { harmony.GetType().GetMethod("UnpatchAll")!.Invoke(harmony, ["rwom.tests.class-marker"]); Current.Game = previousGame; DefDatabase<HediffDef>.Clear(); foreach (var d in savedDefs) DefDatabase<HediffDef>.Add(d); DefDatabase<PawnCapacityDef>.Clear(); foreach (var d in savedCapacities) DefDatabase<PawnCapacityDef>.Add(d); }
        });
        yield return new("ClassInitiation.OldSaveRemainsClassic", f =>
        {
            GrimoireProgressionTests.LoadBaseDefinitions(f);
            var data = new Envelope(); data.magic.MagicUserLevel = 12; data.magic.MagicUserXP = 42; data.magic.MagicAbilityPoints = 7;
            data.magic.MagicPowersIF[0].learned = true; data.magic.MagicPowersIF[0].level = 2;
            data.magic.MagicPowerSkill_Firebolt[0].level = 4;
            var loaded = RoundTrip(f, data, "classic");
            Check.Equal(false, XDocument.Load(Path.Combine(f.WorkingDirectory, "classic.xml")).Descendants("TM_classProgression").Any(), "classic save gains no enrollment payload");
            Check.Equal(true, loaded.progression.UsesClassicProgression, "missing new field is not a novice enrollment");
            Check.Equal(12, loaded.magic.MagicUserLevel, "old level retained");
            Check.Equal(42, loaded.magic.MagicUserXP, "old XP retained");
            Check.Equal(7, loaded.magic.MagicAbilityPoints, "old points retained");
            Check.Equal(true, loaded.magic.MagicPowersIF[0].learned, "old learned flag retained");
            Check.Equal(2, loaded.magic.MagicPowersIF[0].level, "old spell rank retained");
            Check.Equal(4, loaded.magic.MagicPowerSkill_Firebolt[0].level, "old skill investment retained");
        });
        yield return new("ClassInitiation.FireIceIndependentAndIdempotent", f =>
        {
            GrimoireProgressionTests.LoadBaseDefinitions(f);
            var data = new Envelope(); data.magic.MagicAbilityPoints = 8;
            Check.Equal(true, data.progression.BeginNewProgression(Vocation.Mage), "explicit enrollment");
            var state = data.progression.State;
            Check.Equal(true, state.TryAddFoundation("rwom.fire", DisciplineDomain.Magic), "fire foundation");
            Check.Equal(true, state.TryAddFoundation("rwom.ice", DisciplineDomain.Magic), "ice foundation coexists");
            Check.Equal(false, state.TryAddFoundation("rwom.fire", DisciplineDomain.Magic), "retry does not duplicate path");
            Check.Equal(false, data.progression.BeginNewProgression(Vocation.Warrior), "enrollment retry cannot erase paths");
            Check.Equal(true, state.Find("rwom.fire").RecordCompletedTrial("first-trial"), "fire task completes");
            Check.Equal(false, state.Find("rwom.fire").RecordCompletedTrial("first-trial"), "duplicate completion ignored");
            Check.Equal(0, state.Find("rwom.ice").CompletedTrials.Count, "fire does not finish ice task");
            Check.Equal(InitiationStage.Foundation, state.Find("rwom.fire").Stage, "task is not an automatic ritual or ascension");
            var loaded = RoundTrip(f, data, "hybrid"); loaded = RoundTrip(f, loaded, "hybrid-repeated");
            Check.Equal(2, loaded.progression.State.Disciplines.Count, "both directions survive repeated Scribe");
            Check.Equal(1, loaded.progression.State.Find("rwom.fire").CompletedTrials.Count, "one completion retained");
            Check.Equal(0, loaded.progression.State.Find("rwom.ice").CompletedTrials.Count, "ice remains independent after load");
            Check.Equal(8, loaded.magic.MagicAbilityPoints, "paths and loading do not grant/spend skill points");
        });
        yield return new("ClassInitiation.VocationsAndMissingAddonProfile", f =>
        {
            GrimoireProgressionTests.LoadBaseDefinitions(f);
            var mage = new ClassProgressionState(Vocation.Mage);
            Check.Equal(false, mage.TryAddFoundation("warrior", DisciplineDomain.Might), "mage does not admit fighter path");
            var warrior = new ClassProgressionState(Vocation.Warrior);
            Check.Equal(false, warrior.TryAddFoundation("mage", DisciplineDomain.Magic), "warrior does not admit mage path");
            var data = new Envelope(); data.progression.BeginNewProgression(Vocation.WarriorMage);
            Check.Equal(true, data.progression.State.TryAddFoundation("missing-addon.class", DisciplineDomain.Might), "storage does not require installed profile");
            Check.Equal(true, data.progression.State.TryAddFoundation("rwom.fire", DisciplineDomain.Magic), "hybrid vocation allows both domains");
            Check.Equal(false, data.progression.State.TryAddFoundation("", DisciplineDomain.Magic), "reject empty identity");
            var loaded = RoundTrip(f, data, "missing-profile");
            Check.Equal("missing-addon.class", loaded.progression.State.Find("missing-addon.class").ClassId, "unresolved profile is preserved, not replaced");
            Check.Equal(Vocation.WarriorMage, loaded.progression.State.Vocation, "vocation survives load independently of traits");
        });
        yield return new("ClassInitiation.SavedAdvancedStageIsNotReset", f =>
        {
            GrimoireProgressionTests.LoadBaseDefinitions(f);
            var data = new Envelope(); data.progression.BeginNewProgression(Vocation.Mage);
            data.progression.State.TryAddFoundation("rwom.fire", DisciplineDomain.Magic);
            data.progression.State.TryAddFoundation("rwom.ice", DisciplineDomain.Magic);
            var loaded = RoundTrip(f, data, "advanced-stage", xml =>
                xml.Descendants("classId").Single(e => e.Value == "rwom.fire").Parent!.SetElementValue("stage", "Master"));
            loaded = RoundTrip(f, loaded, "advanced-stage-again");
            Check.Equal(InitiationStage.Master, loaded.progression.State.Find("rwom.fire").Stage, "advanced saved stage retained");
            Check.Equal(InitiationStage.Foundation, loaded.progression.State.Find("rwom.ice").Stage, "secondary path not promoted");
        });
        yield return new("ClassInitiation.BootstrapIsPassiveAndIdempotent", f =>
        {
            var human = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
            human.race = new RaceProperties { intelligence = Intelligence.Humanlike }; human.comps = [];
            ClassProgressionBootstrap.EnsureComp(human); ClassProgressionBootstrap.EnsureComp(human);
            Check.Equal(1, human.comps.Count(p => p.compClass == typeof(CompClassProgression)), "no duplicate pawn component");
            var animal = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef)); animal.race = new RaceProperties { intelligence = Intelligence.Animal }; animal.comps = [];
            ClassProgressionBootstrap.EnsureComp(animal);
            Check.Equal(0, animal.comps.Count, "animals not enrolled");
            var comp = new CompClassProgression();
            Check.Equal(true, comp.UsesClassicProgression, "adding the component does not convert old or addon pawns");
            Check.Equal(false, comp.BeginNewProgression(Vocation.None), "invalid enrollment remains passive");
        });
    }
}


