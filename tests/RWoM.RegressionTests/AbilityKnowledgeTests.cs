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
