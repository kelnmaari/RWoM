using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using RimWorld;
using TorannMagic;
using Verse;

namespace RWoM.RegressionTests;

// Ф3: AbilityKnowledgeComp — unified knowledge store. Skills by role (no label
// strings), missing entries return 0, store round-trips through save/load.
internal static class AbilityKnowledgeTests
{
    public static IEnumerable<TestCase> Cases()
    {
        yield return new("AbilityKnowledge.StoreRetrieveSkillRanks", StoreRetrieve);
        yield return new("AbilityKnowledge.MissingSpellReturnsZero", MissingSpell);
        yield return new("AbilityKnowledge.RemoveSpell", RemoveSpell);
        yield return new("AbilityKnowledge.SaveLoadRoundTrip", SaveLoad);
        yield return new("AbilityKnowledge.SchemaV2Reconciles", SchemaV2Reconciles);
    }

    // Ф4 step 4 stage 2: v1 saves drifted from legacy (toggles and ranks moved
    // after the one-shot import, before dual-write existed). Bumping the schema
    // triggers a one-shot reconcile on the next tick: flags AND ranks are
    // re-read from legacy, then the version stamps current.
    private static void SchemaV2Reconciles(RuntimeFixture f)
    {
        if (Current.Game == null)
        {
            Current.Game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        }
        // An existing shell from an earlier suite may lack the managers the
        // Pawn ctor needs — patch them in individually.
        if (Current.Game.tickManager == null)
            typeof(Game).GetField("tickManager", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(Current.Game, new TickManager());
        if (Current.Game.uniqueIDsManager == null)
            typeof(Game).GetField("uniqueIDsManager", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(Current.Game, new UniqueIDsManager());
        var pawnDef = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        pawnDef.defName = "Regression_Reconcile";
        pawnDef.race = new RaceProperties { intelligence = Intelligence.Humanlike };
        var pawn = new Pawn { def = pawnDef };
        for (Type type = pawn.GetType(); type != null; type = type.BaseType)
        {
            var compsField = type.GetField("comps", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (compsField == null) continue;
            compsField.SetValue(pawn, new List<ThingComp>());
            break;
        }

        var def = (TMAbilityDef)RuntimeHelpers.GetUninitializedObject(typeof(TMAbilityDef));
        def.defName = "TM_ReconcileA";
        def.label = "TM_ReconcileA";

        var magic = new CompAbilityUserMagic { parent = pawn };
        // Blank the class-list backings first (their lazy getters build from
        // DefOfs that are null headless — the EmptyMagic pattern), then plant
        // the standalone list with the power under test.
        var data = new MagicData();
        foreach (var field in typeof(MagicData).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            if (field.FieldType == typeof(List<MagicPower>) && !field.Name.Contains("Custom"))
                field.SetValue(data, new List<MagicPower>());
        // The MagicPowersP getter pads a short list with a DefOf-built paladin
        // buff (NRE headless) — fill 6 blanks like EmptyMagic does.
        var pList = (List<MagicPower>)typeof(MagicData).GetField("magicPowerP", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .GetValue(data);
        pList.AddRange(Enumerable.Range(0, 6).Select(_ => new MagicPower { TMabilityDefs = [] }));
        var power = new MagicPower(new List<AbilityUser.AbilityDef> { def }) { learned = true, autocast = true, level = 2 };
        typeof(MagicData).GetField("magicPowerStandalone", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(data, new List<MagicPower> { power });
        typeof(CompAbilityUserMagic).GetField("magicData", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .SetValue(magic, data);
        pawn.AllComps.Add(magic);

        var knowledge = new AbilityKnowledgeComp { parent = pawn };
        pawn.AllComps.Add(knowledge);
        // Simulate the imported-and-drifted v1 state: import already ran, the
        // store holds stale values.
        typeof(AbilityKnowledgeComp).GetField("legacyImported", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .SetValue(knowledge, true);
        var stale = knowledge.GetOrCreate(TorannMagic.SpellIds.Of(def), 0);
        stale.learned = true;
        stale.autocast = false; // legacy says true — drifted
        stale.pwr = 0; // legacy skill says 2 — never imported at v1
        typeof(AbilityKnowledgeComp).GetField("schemaVersion", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .SetValue(knowledge, 1);

        // Round-trip through the real Scribe (PostLoadInit arms the reconcile).
        string path = Path.Combine(f.WorkingDirectory, "reconcile-v1.xml");
        var wrap = new KnowledgeWrap { Comp = knowledge };
        try
        {
            Scribe.saver.InitSaving(path, "regression");
            Scribe_Deep.Look(ref wrap, "wrap");
            Scribe.saver.FinalizeSaving();
        }
        finally { Scribe.ForceStop(); }
        KnowledgeWrap loaded = null!;
        try
        {
            Scribe.loader.InitLoading(path);
            Scribe_Deep.Look(ref loaded, "wrap");
            Scribe.loader.FinalizeLoading();
        }
        finally { Scribe.ForceStop(); }
        Check.NotNull(loaded?.Comp, "comp survives the round-trip");
        // The game's comp loader restores `parent`; a bare Scribe wrap does not.
        loaded.Comp.parent = pawn;

        loaded.Comp.CompTick(); // reconcile runs here
        Check.Equal(2, loaded.Comp.SchemaVersion, "schema stamped current");
        var entry = loaded.Comp.Find(TorannMagic.SpellIds.Of(def));
        Check.NotNull(entry, "reconciled entry present");
        Check.Equal(true, entry.autocast, "drifted autocast re-read from legacy");
        Check.Equal(2, entry.tier, "tier re-read from legacy level");
        Check.Equal(0, loaded.Comp.GetSkillRank(TorannMagic.SpellIds.Of(def), 0), "rank defaults to 0 while legacy has no skill list");

        // The reconcile is one-shot per load: a second tick does not re-import.
        entry.autocast = false;
        loaded.Comp.CompTick();
        Check.Equal(false, loaded.Comp.Find(TorannMagic.SpellIds.Of(def)).autocast, "post-reconcile edits are preserved");
    }

    static AbilityKnowledgeComp Comp() => new() { parent = new ThingWithComps() };

    static void StoreRetrieve(RuntimeFixture _)
    {
        var comp = Comp();
        comp.GetOrCreate("rwom.firebolt", 0);
        comp.SetSkillRank("rwom.firebolt", 0, 5);
        comp.SetSkillRank("rwom.firebolt", 1, 3);
        comp.SetSkillRank("rwom.firebolt", 2, 2);
        Check.Equal(5, comp.GetSkillRank("rwom.firebolt", 0), "pwr rank stored");
        Check.Equal(3, comp.GetSkillRank("rwom.firebolt", 1), "eff rank stored");
        Check.Equal(2, comp.GetSkillRank("rwom.firebolt", 2), "ver rank stored");
        Check.Equal(0, comp.GetSkillRank("rwom.nonexistent", 0), "missing spell returns 0 pwr");
    }

    static void MissingSpell(RuntimeFixture _)
    {
        var comp = Comp();
        Check.Equal(0, comp.GetSkillRank("rwom.never_existed", 2), "missing spell ver returns 0");
        Check.Equal(false, comp.Remove("rwom.never_existed"), "removing a missing spell returns false");
    }

    static void RemoveSpell(RuntimeFixture _)
    {
        var comp = Comp();
        comp.GetOrCreate("rwom.test", 0);
        comp.SetSkillRank("rwom.test", 0, 7);
        Check.Equal(true, comp.Remove("rwom.test"), "spell removed");
        Check.Equal(0, comp.GetSkillRank("rwom.test", 0), "removed spell returns 0");
    }

    static void SaveLoad(RuntimeFixture f)
    {
        var original = new KnowledgeWrap { Comp = Comp() };
        original.Comp.GetOrCreate("rwom.save_test", 0);
        original.Comp.SetSkillRank("rwom.save_test", 0, 4);
        original.Comp.SetSkillRank("rwom.save_test", 1, 2);
        string path = Path.Combine(f.WorkingDirectory, "ability-knowledge.xml");
        try
        {
            Scribe.saver.InitSaving(path, "regression");
            Scribe_Deep.Look(ref original, "wrap");
            Scribe.saver.FinalizeSaving();
        }
        finally { Scribe.ForceStop(); }
        KnowledgeWrap loaded = null!;
        try
        {
            Scribe.loader.InitLoading(path);
            Scribe_Deep.Look(ref loaded, "wrap");
            Scribe.loader.FinalizeLoading();
        }
        finally { Scribe.ForceStop(); }
        Check.NotNull(loaded?.Comp, "knowledge comp survives save/load");
        Check.Equal(4, loaded.Comp.GetSkillRank("rwom.save_test", 0), "pwr rank survives");
        Check.Equal(2, loaded.Comp.GetSkillRank("rwom.save_test", 1), "eff rank survives");
    }

    public sealed class KnowledgeWrap : IExposable
    {
        public AbilityKnowledgeComp Comp;
        public void ExposeData() => Scribe_Deep.Look(ref Comp, "comp");
    }
}
