using System.Reflection;
using System.Runtime.CompilerServices;
using RimWorld;
using TorannMagic;
using Verse;

namespace RWoM.RegressionTests;

// Ф4 step 4: the grimoire's knowledge adapter. Sleeping knowledge — learned
// entries whose def no longer resolves — is surfaced for a grey section;
// convention ids prettify into readable names. Classic cards keep reading
// their legacy owners.
internal static class GrimoireKnowledgeTests
{
    public static IEnumerable<TestCase> Cases()
    {
        yield return new("GrimoireKnowledge.DisplayNamePrettifies", DisplayNamePrettifies);
        yield return new("GrimoireKnowledge.SleepingFiltersByResolvability", SleepingFiltersByResolvability);
        yield return new("GrimoireKnowledge.SleepingNullSafe", SleepingNullSafe);
    }

    // RED 1: convention ids read as spell names; unknown shapes come back sane.
    private static void DisplayNamePrettifies(RuntimeFixture _)
    {
        Check.Equal("Heal I", GrimoireKnowledge.DisplayName("rwom.tm_heal_i"), "variant suffix becomes a roman numeral word");
        Check.Equal("Advanced Heal II", GrimoireKnowledge.DisplayName("rwom.tm_advanced_heal_ii"), "multi-word base prettifies");
        Check.Equal("Magic Missile", GrimoireKnowledge.DisplayName("rwom.tm_magic_missile"), "plain base prettifies");
        Check.Equal("custom.family", GrimoireKnowledge.DisplayName("custom.family"), "non-convention id returned as-is");
        Check.Equal("???", GrimoireKnowledge.DisplayName(null), "null id has a placeholder");
        Check.Equal("???", GrimoireKnowledge.DisplayName(""), "empty id has a placeholder");
    }

    // RED 2: only learned entries whose def fails to resolve are sleeping.
    private static void SleepingFiltersByResolvability(RuntimeFixture f)
    {
        string coreDll = Path.Combine(f.Repository, "TorannMagic.Core", "Assemblies", "TorannMagic.Core.dll");
        if (File.Exists(coreDll)) Assembly.LoadFrom(coreDll);

        var def = (TMAbilityDef)RuntimeHelpers.GetUninitializedObject(typeof(TMAbilityDef));
        def.defName = "TM_KnowledgeGrim";
        def.label = "TM_KnowledgeGrim";
        def.MainVerb = new AbilityUser.VerbProperties_Ability();
        CoreBridge.RegisterSpell(def); // resolvable

        var knowledge = new AbilityKnowledgeComp { parent = new ThingWithComps() };
        var alive = knowledge.GetOrCreate("rwom.tm_knowledgegrim", 0);
        alive.learned = true;
        var sleeping = knowledge.GetOrCreate("rwom.tm_removed_spell_ii", 0);
        sleeping.learned = true;
        var forgotten = knowledge.GetOrCreate("rwom.tm_forgotten_spell", 0);
        forgotten.learned = false; // unlearned entries are never listed

        var result = GrimoireKnowledge.SleepingSpells(knowledge);
        Check.Equal(1, result.Count, "exactly the unresolvable learned entry is sleeping");
        Check.Same(sleeping, result[0], "the sleeping entry is the one whose def is gone");
    }

    // RED 3: missing pawn/comp/store never throws.
    private static void SleepingNullSafe(RuntimeFixture _)
    {
        Check.Equal(0, GrimoireKnowledge.SleepingSpells((AbilityKnowledgeComp)null).Count, "null comp yields an empty list");
        var empty = new AbilityKnowledgeComp { parent = new ThingWithComps() };
        Check.Equal(0, GrimoireKnowledge.SleepingSpells(empty).Count, "empty store yields an empty list");
    }
}
