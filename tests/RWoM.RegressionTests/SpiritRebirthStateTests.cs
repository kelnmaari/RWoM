using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using RimWorld;
using TorannMagic;
using TorannMagic.ClassProgression;
using Verse;
using static RWoM.RegressionTests.GrimoireProgressionTests;

namespace RWoM.RegressionTests;

internal static class SpiritRebirthStateTests
{
    public static IEnumerable<TestCase> Cases()
    {
        yield return new("SpiritRebirthState.ExplicitMarkerPreservesOriginalData", Identity);
        yield return new("SpiritRebirthState.ExactNativeHostCustody", Custody);
        yield return new("SpiritRebirthState.NativeScribeReferencesAndLegacyNull", Save);
        yield return new("SpiritRebirthState.UnsupportedStateNeverAuthorizesHost", Invalid);
    }

    static bool Invoke(object target, string name, params object[] args)
        => (bool)target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args)!;

    sealed class Fixture : IDisposable
    {
        readonly HediffDef oldPossession = TorannMagicDefOf.TM_SpiritPossessionHD;
        readonly Game previousGame = Current.Game;
        readonly DesignationDef[] designations = DefDatabase<DesignationDef>.AllDefsListForReading.ToArray();
        public readonly Pawn Original, Host, Other;
        public readonly CompClassProgression Comp;
        public readonly CompAbilityUserMagic Magic;
        public readonly CompAbilityUserMight Might;
        public Fixture(RuntimeFixture f)
        {
            LoadBaseDefinitions(f);
            Current.Game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
            if (DefDatabase<DesignationDef>.DefCount == 0) DefDatabase<DesignationDef>.Add(new DesignationDef { defName = "SpiritRebirthTestDesignation" });
            var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
            map.designationManager = new DesignationManager(map);
            Set(Current.Game, "maps", new List<Map> { map });
            var original = PawnWithComps(true, true);
            Original = original.pawn; Magic = original.magic; Might = original.might;
            Host = PawnWithComps(true, true).pawn; Other = PawnWithComps(false, false).pawn;
            Original.thingIDNumber = 67101; Host.thingIDNumber = 67102; Other.thingIDNumber = 67103;
            Comp = new CompClassProgression { parent = Original }; Original.AllComps.Add(Comp);
            TorannMagicDefOf.TM_SpiritPossessionHD = new HediffDef { defName = "SpiritRebirthTestPossession" };
        }
        public SpiritRebirthState Begin()
        {
            Check.Equal(true, Invoke(Comp, "BeginSpiritRebirth"), "explicit transaction creates marker once");
            return Comp.SpiritRebirth;
        }
        public HediffComp_SpiritPossession Hold(Pawn host, Pawn original)
        {
            var hediff = new HediffWithComps { def = TorannMagicDefOf.TM_SpiritPossessionHD, pawn = host };
            var possession = new HediffComp_SpiritPossession { parent = hediff };
            hediff.comps = [possession]; host.health.hediffSet.hediffs.Add(hediff);
            var holder = new ThingOwner<Thing>(possession);
            Set(possession, "innerContainer", holder);
            Check.Equal(true, holder.TryAdd(original, false), "native holder accepts original pawn");
            return possession;
        }
        public void Dispose()
        {
            TorannMagicDefOf.TM_SpiritPossessionHD = oldPossession; Current.Game = previousGame;
            DefDatabase<DesignationDef>.Clear(); foreach (var def in designations) DefDatabase<DesignationDef>.Add(def);
        }
    }

    static void Identity(RuntimeFixture r)
    {
        using var f = new Fixture(r);
        f.Comp.BeginNewProgression(Vocation.WarriorMage);
        f.Comp.State.TryAddFoundation("rwom.necromancer", DisciplineDomain.Magic);
        var progression = f.Comp.State; var magic = f.Magic.MagicData; var might = f.Might.MightData;
        var power = magic.MagicPowersN[0]; power.learned = true; power.level = 1;
        magic.MagicAbilityPoints = 17; magic.MagicUserXP = 123;
        var skill = new SkillRecord(f.Original, new SkillDef { defName = "SpiritIdentitySkill" }) { passion = Passion.Major, levelInt = 12 };
        f.Original.skills = new Pawn_SkillTracker(f.Original); f.Original.skills.skills = [skill];
        var traits = f.Original.story.traits; var name = f.Original.Name; var race = f.Original.def; string id = f.Original.GetUniqueLoadID();
        var state = f.Begin();
        Check.Equal(false, Invoke(f.Comp, "BeginSpiritRebirth"), "cannot reset existing marker");
        Check.Equal(true, SpiritRebirthState.TryGet(f.Original, out var found), "original resolves explicit marker");
        Check.Same(state, found, "same marker"); Check.Equal(SpiritRebirthForm.Ethereal, found.Form, "initial form");
        Check.Same(race, f.Original.def, "race untouched"); Check.Equal(id, f.Original.GetUniqueLoadID(), "native load ID untouched");
        Check.Same(name, f.Original.Name, "name identity untouched"); Check.Same(traits, f.Original.story.traits, "trait owner untouched");
        Check.Same(skill, f.Original.skills.skills.Single(), "ordinary skill owner untouched"); Check.Equal(Passion.Major, skill.passion, "passion untouched"); Check.Equal(12, skill.levelInt, "ordinary level untouched");
        Check.Same(progression, f.Comp.State, "progression owner untouched"); Check.Same(magic, f.Magic.MagicData, "magic data untouched"); Check.Same(might, f.Might.MightData, "might data untouched");
        Check.Same(power, magic.MagicPowersN[0], "class power owner untouched"); Check.Equal(true, power.learned, "learned retained"); Check.Equal(1, power.level, "rank retained");
        Check.Equal(17, magic.MagicAbilityPoints, "points retained"); Check.Equal(123, magic.MagicUserXP, "XP retained");
        Check.Equal(false, state.HasValidHost(f.Original), "free form cannot authorize a host");
    }

    static void Custody(RuntimeFixture r)
    {
        using var f = new Fixture(r); var state = f.Begin();
        Check.Equal(false, Invoke(state, "TryEnterHost", f.Original, f.Host), "host field alone cannot authorize entry");
        var possession = f.Hold(f.Host, f.Original);
        Check.Equal(false, SpiritRebirthState.TryGetPossessingSpirit(f.Host, out _), "container alone cannot authorize new possession");
        Check.Equal(true, Invoke(state, "TryEnterHost", f.Original, f.Host), "actual custody permits explicit entry");
        Check.Equal(true, state.HasValidHost(f.Original), "reciprocal state and holder agree");
        Check.Equal(true, SpiritRebirthState.TryGetPossessingSpirit(f.Host, out var original), "reverse lookup validated"); Check.Same(f.Original, original, "actual original resolved");
        Check.Equal(false, Invoke(state, "TryEnterHost", f.Original, f.Other), "cannot rebind active host");
        Check.Equal(false, Invoke(state, "TryLeaveHost", f.Original, f.Host), "cannot leave while still held");
        var holder = possession.GetDirectlyHeldThings(); holder.Remove(f.Original);
        Check.Equal(false, state.HasValidHost(f.Original), "stale saved host loses authority immediately");
        Check.Equal(false, SpiritRebirthState.TryGetPossessingSpirit(f.Host, out _), "empty actual host rejected");
        Check.Equal(false, Invoke(state, "TryLeaveHost", f.Original, f.Host), "unplaced original cannot complete release");
        Set(f.Original, "mapIndexOrState", (sbyte)0);
        Check.Equal(false, Invoke(state, "TryLeaveHost", f.Original, f.Other), "foreign release rejected");
        Check.Equal(true, Invoke(state, "TryLeaveHost", f.Original, f.Host), "placed original explicitly returns to ethereal");
        Check.Equal(SpiritRebirthForm.Ethereal, state.Form, "release form"); Check.Equal(true, state.Host == null, "release clears host only");
        Set(f.Original, "mapIndexOrState", (sbyte)-1);
        holder.TryAdd(f.Original, false); Check.Equal(true, Invoke(state, "TryEnterHost", f.Original, f.Host), "repeated entry uses actual custody");
        possession.parent.pawn = f.Other;
        Check.Equal(false, state.HasValidHost(f.Original), "foreign hediff parent rejected");
        possession.parent.pawn = f.Host;
        holder.Remove(f.Original);
        var ownerless = new ThingOwner<Thing>(); ownerless.TryAdd(f.Original, false); Set(possession, "innerContainer", ownerless);
        Check.Equal(false, state.HasValidHost(f.Original), "legacy ownerless container cannot authorize new possession");
    }

    public sealed class Envelope(Pawn original) : IExposable
    {
        public CompClassProgression comp = new() { parent = original };
        public void ExposeData() => comp.PostExposeData();
    }

    static Envelope RoundTrip(RuntimeFixture r, Fixture f, string name, Action<XDocument>? edit = null)
    {
        string path = Path.Combine(r.WorkingDirectory, name + ".xml");
        var data = new Envelope(f.Original) { comp = f.Comp };
        try { Scribe.saver.InitSaving(path, "regression"); Scribe_Deep.Look(ref data, "data"); Scribe.saver.FinalizeSaving(); }
        finally { Scribe.ForceStop(); }
        if (edit != null) { var xml = XDocument.Load(path); edit(xml); xml.Save(path); }
        Envelope loaded = null!;
        try
        {
            Scribe.loader.InitLoading(path); Scribe_Deep.Look(ref loaded, "data", f.Original);
            var directory = (LoadedObjectDirectory)typeof(CrossRefHandler).GetField("loadedObjectDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Scribe.loader.crossRefs)!;
            directory.RegisterLoaded(f.Original); directory.RegisterLoaded(f.Host);
            Scribe.loader.FinalizeLoading();
        }
        finally { Scribe.ForceStop(); }
        RuntimeLog.AssertClean(); return loaded;
    }

    static void Save(RuntimeFixture r)
    {
        using (var legacy = new Fixture(r))
        {
            var loaded = RoundTrip(r, legacy, "spirit-legacy-null");
            Check.Equal(true, loaded.comp.SpiritRebirth == null, "old absent state remains absent");
            Check.Equal(true, loaded.comp.UsesClassicProgression, "old progression unchanged");
            Check.Equal(false, XDocument.Load(Path.Combine(r.WorkingDirectory, "spirit-legacy-null.xml")).Descendants("TM_spiritRebirth").Any(), "no marker written for old pawn");
            legacy.Hold(legacy.Host, legacy.Original);
            Check.Equal(false, SpiritRebirthState.TryGetPossessingSpirit(legacy.Host, out _), "legacy native possession remains outside new system");
        }
        foreach (bool possessed in new[] { false, true })
        {
            using var f = new Fixture(r); var state = f.Begin(); var magic = f.Magic.MagicData; var might = f.Might.MightData;
            if (possessed) { f.Hold(f.Host, f.Original); Check.Equal(true, Invoke(state, "TryEnterHost", f.Original, f.Host), "entry before save"); }
            var loaded = RoundTrip(r, f, "spirit-state-" + possessed);
            f.Original.AllComps.Remove(f.Comp); f.Original.AllComps.Add(loaded.comp);
            Check.Equal(true, SpiritRebirthState.TryGet(f.Original, out var restored), "native original pawn cross reference resolves");
            Check.Equal(SpiritRebirthState.CurrentVersion, restored.Version, "version survives");
            Check.Equal(possessed ? SpiritRebirthForm.Possessed : SpiritRebirthForm.Ethereal, restored.Form, "form survives");
            Check.Equal(possessed, restored.HasValidHost(f.Original), "loaded record still requires actual holder");
            if (possessed) Check.Same(f.Host, restored.Host, "native host pawn cross reference resolves");
            Check.Same(magic, f.Magic.MagicData, "state save never copies magic data"); Check.Same(might, f.Might.MightData, "state save never copies might data");
            var xml = XDocument.Load(Path.Combine(r.WorkingDirectory, "spirit-state-" + possessed + ".xml"));
            Check.Equal(false, xml.Descendants("magicData").Any() || xml.Descendants("mightData").Any(), "state serialization owns no class snapshot");
        }
    }

    static void Invalid(RuntimeFixture r)
    {
        using var f = new Fixture(r); var state = f.Begin(); f.Hold(f.Host, f.Original); Invoke(state, "TryEnterHost", f.Original, f.Host);
        foreach (int version in new[] { 0, SpiritRebirthState.CurrentVersion + 1 })
        {
            Set(state, "version", version); Check.Equal(false, SpiritRebirthState.TryGet(f.Original, out _), "unsupported version denied");
            Check.Equal(false, SpiritRebirthState.TryGetPossessingSpirit(f.Host, out _), "unknown version cannot borrow host authority");
        }
        Set(state, "version", SpiritRebirthState.CurrentVersion); Set(state, "form", SpiritRebirthForm.Dormant);
        Check.Equal(false, state.HasValidHost(f.Original), "reserved Dormant form has no host authority");
        Set(state, "form", (SpiritRebirthForm)99); Check.Equal(false, SpiritRebirthState.TryGet(f.Original, out _), "invalid form rejected");
        Set(state, "form", SpiritRebirthForm.Possessed); Set(state, "original", f.Other);
        Check.Equal(false, SpiritRebirthState.TryGet(f.Original, out _), "marker copied to different original is rejected");
        Check.Equal(false, state.HasValidHost(f.Original), "foreign original loses host authority");
    }
}
