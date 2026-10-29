using System.Runtime.CompilerServices;
using AbilityUser;
using RimWorld;
using TorannMagic;
using TorannMagic.ClassProgression;
using Verse;
using static RWoM.RegressionTests.GrimoireProgressionTests;
namespace RWoM.RegressionTests;
internal static class GeomancerTrialTests
{
    public static IEnumerable<TestCase> Cases()
    {
        yield return new("GeomancerTrial.FreshWardOwnershipAndNoops", Ward);
        yield return new("GeomancerTrial.EncaseDistinctCellsAndRankedSources", Encase);
        yield return new("GeomancerTrial.SourceAndBoundedCellsScribe", Save);
    }
    sealed class Fixture : IDisposable
    {
        readonly Game previous = Current.Game;
        readonly ClassDisciplineDef[] profiles = DefDatabase<ClassDisciplineDef>.AllDefsListForReading.ToArray();
        public Pawn Caster, Target;
        public CompAbilityUserMagic Magic;
        public CompClassProgression Progression;
        public DisciplineProgress Path => Progression.State.Find("rwom.geomancer");
        public Fixture(RuntimeFixture f)
        {
            LoadBaseDefinitions(f);
            Current.Game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
            Set(Current.Game, "tickManager", new TickManager());
            Set(Current.Game, "maps", new List<Map> { (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map)), (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map)) });
            var c = PawnWithComps(true, false); Caster = c.pawn; Magic = c.magic;
            Target = PawnWithComps(false, false).pawn;
            var faction = new Faction { def = new FactionDef { isPlayer = true } };
            int id = 41000;
            foreach (var pawn in new[] { Caster, Target })
            {
                pawn.thingIDNumber = ++id; pawn.guest = new Pawn_GuestTracker(pawn);
                pawn.kindDef = new PawnKindDef { defName = "GeomancerTrialPawn" };
                Set(pawn, "factionInt", faction); Set(pawn, "mapIndexOrState", (sbyte)0);
            }
            Progression = new CompClassProgression { parent = Caster }; Caster.AllComps.Add(Progression);
            Progression.BeginNewProgression(Vocation.Mage); Progression.State.TryAddFoundation("rwom.geomancer", DisciplineDomain.Magic);
            DefDatabase<ClassDisciplineDef>.Clear();
            DefDatabase<ClassDisciplineDef>.Add(new ClassDisciplineDef { defName = "GeomancerTrialProfile", profileId = "rwom.geomancer", domain = DisciplineDomain.Magic });
        }
        public PawnAbility Ability(TMAbilityDef def, bool owned = true)
        {
            Magic.MagicData.MagicPowersG.Single(p => p.TMabilityDefs.Contains(def)).learned = true;
            var ability = new PawnAbility(Magic); Set(ability, "powerDef", def); ability.CooldownTicksLeft = 91;
            if (owned) Magic.AbilityData.Powers.Add(ability);
            return ability;
        }
        public void Adept() { Path.RecordCompletedTrial(GeomancerTrials.FoundationTrialId); Progression.State.TryAdvanceAfterTrial("rwom.geomancer", InitiationStage.Foundation, GeomancerTrials.FoundationTrialId); }
        public void Dispose() { GeomancerTrials.EndLaunch(null!); Current.Game = previous; DefDatabase<ClassDisciplineDef>.Clear(); foreach(var def in profiles) DefDatabase<ClassDisciplineDef>.Add(def); }
    }
    static void Ward(RuntimeFixture f)
    {
        using var w = new Fixture(f);
        var ability = w.Ability(TorannMagicDefOf.TM_Stoneskin, false);
        Check.Equal(true, GeomancerTrials.CaptureStoneskin(ability, w.Target) == null, "copied/item instance excluded");
        w.Magic.AbilityData.Powers.Add(ability);
        w.Caster.story.traits.allTraits.Add(new Trait(TorannMagicDefOf.Faceless));
        Check.Equal(true, GeomancerTrials.CaptureStoneskin(ability, w.Target) == null, "Faceless excluded even with native learned owner"); w.Caster.story.traits.allTraits.Clear();
        Check.Equal(true, GeomancerTrials.CaptureStoneskin(ability, w.Caster) == null, "self excluded");
        Set(w.Target, "mapIndexOrState", (sbyte)1);
        Check.Equal(true, GeomancerTrials.CaptureStoneskin(ability, w.Target) == null, "cross map excluded"); Set(w.Target, "mapIndexOrState", (sbyte)0);
        w.Target.guest.guestStatusInt = GuestStatus.Prisoner;
        Check.Equal(true, GeomancerTrials.CaptureStoneskin(ability, w.Target) == null, "prisoner excluded"); w.Target.guest.guestStatusInt = GuestStatus.Guest;
        Check.Equal(false, GeomancerTrials.CompleteStoneskin(GeomancerTrials.CaptureStoneskin(ability, w.Target)), "failed capacity/no-op earns nothing");
        var before = GeomancerTrials.CaptureStoneskin(ability, w.Target);
        var ward = new Hediff { pawn = w.Target, def = new HediffDef { defName = "TM_StoneskinHD" } };
        Set(ward, "severityInt", 4f); w.Target.health.hediffSet.hediffs.Add(ward);
        Check.Equal(false, GeomancerTrials.CompleteStoneskin(before), "actual ward without caster upkeep ownership excluded");
        Check.Equal(true, GeomancerTrials.CaptureStoneskin(ability, w.Target) == null, "refresh/toggle/passive regeneration never fresh");
        w.Target.health.hediffSet.hediffs.Clear(); before = GeomancerTrials.CaptureStoneskin(ability, w.Target);
        w.Target.health.hediffSet.hediffs.Add(ward); w.Magic.stoneskinPawns.Add(w.Target);
        Check.Equal(true, GeomancerTrials.CompleteStoneskin(before), "fresh actual native-owned ally ward qualifies");
        Check.Equal(false, GeomancerTrials.CompleteStoneskin(before), "snapshot single use");
        Check.Equal(91, ability.CooldownTicksLeft, "observer preserves cooldown");
        Check.Equal(InitiationStage.Foundation, w.Path.Stage, "ritual still required");
    }
    public sealed class TestProjectile : Projectile_AbilityBase { public void Seed(Pawn caster) => launcher = caster; }
    static TestProjectile Launch(Fixture w, TMAbilityDef def, bool owned = true)
    {
        var ability = w.Ability(def, owned);
        var verb = new Verb_BLOS { Ability = ability }; Set(verb, "caster", w.Caster);
        var projectileDef = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        projectileDef.defName = "Projectile_Encase" + def.defName.Substring("TM_Encase".Length);
        var projectile = new TestProjectile { def = projectileDef };
        projectile.Seed(w.Caster); Set(projectile, "localAbilityDef", def);
        var prior = GeomancerTrials.BeginLaunch(verb, projectile.def);
        try { GeomancerTrials.Launched(projectile, w.Caster, def); } finally { GeomancerTrials.EndLaunch(prior); }
        return projectile;
    }
    static void Cell(Fixture w, TestProjectile projectile, int x, bool spawned = true)
    {
        var wallDef = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        wallDef.defName = "Sandstone"; wallDef.category = ThingCategory.Building;
        var wall = new Thing { def = wallDef };
        var cell = new IntVec3(x, 0, 0); Set(wall, "positionInt", cell); Set(wall, "mapIndexOrState", spawned ? (sbyte)0 : (sbyte)-1);
        GeomancerTrials.WallSpawned(projectile, wall, cell, w.Caster.Map);
    }
    static void Encase(RuntimeFixture f)
    {
        foreach (var rank in new[] { 0, 1, 2, 3 })
        {
            using var w = new Fixture(f); w.Adept();
            var def = new[] { TorannMagicDefOf.TM_Encase, TorannMagicDefOf.TM_Encase_I, TorannMagicDefOf.TM_Encase_II, TorannMagicDefOf.TM_Encase_III }[rank];
            var copy = Launch(w, def, false); Cell(w, copy, 1); Cell(w, copy, 2); Cell(w, copy, 3);
            Check.Equal(false, w.Path.CompletedTrials.Contains(GeomancerTrials.MasteryTrialId), "copied ranked source excluded");
            var p = Launch(w, def); var other = Launch(w, def);
            Cell(w, p, 1); Cell(w, p, 1); Cell(w, p, 2, false); Cell(w, other, 2); Cell(w, other, 3);
            Check.Equal(false, w.Path.CompletedTrials.Contains(GeomancerTrials.MasteryTrialId), "duplicates/failures/separate casts cannot aggregate");
            Cell(w, p, 2); Check.Equal(false, w.Path.CompletedTrials.Contains(GeomancerTrials.MasteryTrialId), "two cells insufficient");
            Cell(w, p, 3); Check.Equal(true, w.Path.CompletedTrials.Contains(GeomancerTrials.MasteryTrialId), "three distinct new cells from one exact ranked cast");
            Check.Equal(InitiationStage.Adept, w.Path.Stage, "no automatic advancement");
        }
    }
    public sealed class Envelope : IExposable
    {
        public GeomancerTrials.Tag tag = new();
        public TestProjectile projectile = new();
        public void ExposeData() { Scribe_Deep.Look(ref tag, "tag"); GeomancerTrials.ExposeProjectile(projectile); }
    }
    static void Save(RuntimeFixture f)
    {
        using var w = new Fixture(f); w.Adept();
        var source = Launch(w, TorannMagicDefOf.TM_Encase_III); Cell(w, source, 1); Cell(w, source, 2);
        var data = new Envelope { tag = new GeomancerTrials.Tag { casterId = 41001, abilityName = "TM_Encase_III", cells = new() { new(1,0,0), new(1,0,0), new(2,0,0), new(3,0,0), new(4,0,0) } } };
        data.projectile = source;
        var file = Path.Combine(f.WorkingDirectory, "geomancer-source.xml");
        try { Scribe.saver.InitSaving(file, "regression"); Scribe_Deep.Look(ref data, "data"); Scribe.saver.FinalizeSaving(); } finally { Scribe.ForceStop(); }
        Envelope loaded = null!;
        try { Scribe.loader.InitLoading(file); Scribe_Deep.Look(ref loaded, "data"); Scribe.loader.FinalizeLoading(); } finally { Scribe.ForceStop(); }
        Check.Equal(41001, loaded.tag.casterId, "caster provenance saved"); Check.Equal("TM_Encase_III", loaded.tag.abilityName, "exact rank saved");
        Check.Equal(3, loaded.tag.cells.Count, "distinct cell collection bounded after load");
        loaded.projectile.def = source.def; loaded.projectile.localAbilityDef = source.localAbilityDef; loaded.projectile.Seed(w.Caster);
        Cell(w, loaded.projectile, 2); Check.Equal(false, w.Path.CompletedTrials.Contains(GeomancerTrials.MasteryTrialId), "duplicate saved cell earns nothing");
        Cell(w, loaded.projectile, 3); Check.Equal(true, w.Path.CompletedTrials.Contains(GeomancerTrials.MasteryTrialId), "source and two cells survive Scribe and complete on third new cell");
    }
}
