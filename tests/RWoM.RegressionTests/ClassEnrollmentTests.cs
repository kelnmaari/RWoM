using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using AbilityUser;
using RimWorld;
using TorannMagic;
using TorannMagic.ClassProgression;
using TorannMagic.TMDefs;
using Verse;
using Verse.AI;
using static RWoM.RegressionTests.GrimoireProgressionTests;

namespace RWoM.RegressionTests;

internal static class ClassEnrollmentTests
{
    public static IEnumerable<TestCase> Cases()
    {
        yield return new("ClassEnrollment.GiftedEligibilityAndNoLazyMutation", Eligibility);
        yield return new("ClassEnrollment.NeutralVocationDefinitions", Definitions);
        yield return new("ClassEnrollment.NativeEnrollmentCreatesManaWithoutSpells", NativeEnrollment);
    }
    public static bool SkipPhysiology() => false;

    static void NativeEnrollment(RuntimeFixture f)
    {
        LoadBaseDefinitions(f);
        RuntimeLog.EnsureHeadlessDefOfInitialized(typeof(NeedDefOf));
        var previousGame = Current.Game;
        var oldMana = TorannMagicDefOf.TM_Mana; var oldHediff = TorannMagicDefOf.TM_MagicUserHD;
        var oldGifted = TorannMagicDefOf.TM_Gifted;
        var traits = DefDatabase<TraitDef>.AllDefsListForReading.ToArray();
        var capacities = DefDatabase<PawnCapacityDef>.AllDefsListForReading.ToArray();
        var classes = TM_ClassUtility.CustomClasses;
        var harmony = Assembly.Load("0Harmony"); var ht = harmony.GetType("HarmonyLib.Harmony")!;
        var id = "rwom.regression.enrollment-native." + Guid.NewGuid(); var instance = Activator.CreateInstance(ht, id)!;
        try
        {
            Current.Game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
            Set(Current.Game, "tickManager", new TickManager()); Set(Current.Game, "uniqueIDsManager", new UniqueIDsManager());
            var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
            Set(Current.Game, "maps", new List<Map> { map });
            TM_ClassUtility.CustomClasses = [];
            TorannMagicDefOf.TM_Gifted = new TraitDef { defName = "TM_Gifted", degreeDatas = [new TraitDegreeData { degree = 0 }] };
            var vocation = new TraitDef { defName = "TM_VocationMage", degreeDatas = [new TraitDegreeData { degree = 0 }] };
            if (DefDatabase<TraitDef>.GetNamedSilentFail(vocation.defName) == null) DefDatabase<TraitDef>.Add(vocation);
            else vocation = DefDatabase<TraitDef>.GetNamed(vocation.defName);
            DefDatabase<PawnCapacityDef>.Add(new PawnCapacityDef { defName = "EnrollmentCapacity" });
            var p = PawnWithComps(false, false);
            p.pawn.kindDef = new PawnKindDef { defName = "EnrollmentColonist" };
            p.pawn.ageTracker = new Pawn_AgeTracker(p.pawn);
            p.pawn.mindState = new Pawn_MindState(p.pawn);
            p.pawn.def.race.lifeStageAges = [new LifeStageAge { minAge = 0, def = new LifeStageDef { defName = "EnrollmentAdult", developmentalStage = DevelopmentalStage.Adult } }];
            Set(p.pawn.ageTracker, "cachedLifeStageIndex", 0);
            p.pawn.def.race.body = new BodyDef { corePart = new BodyPartRecord { def = new BodyPartDef { defName = "EnrollmentBody" } } };
            Set(p.pawn, "factionInt", new Faction { def = new FactionDef { isPlayer = true } });
            Set(p.pawn, "mapIndexOrState", (sbyte)0);
            p.pawn.story.traits.allTraits.Add(new Trait(TorannMagicDefOf.TM_Gifted));
            var comp = new CompClassProgression { parent = p.pawn }; p.pawn.AllComps.Add(comp);
            TorannMagicDefOf.TM_Mana = new NeedDef { defName = "EnrollmentMana", needClass = typeof(Need_Mana), onlyIfCausedByHediff = true };
            TorannMagicDefOf.TM_MagicUserHD = new HediffDef { defName = "EnrollmentMagicUser", hediffClass = typeof(Hediff), initialSeverity = 1,
                stages = [new HediffStage { enablesNeeds = [TorannMagicDefOf.TM_Mana] }] };
            var prefix = Activator.CreateInstance(harmony.GetType("HarmonyLib.HarmonyMethod")!, typeof(ClassEnrollmentTests).GetMethod(nameof(SkipPhysiology))!)!;
            ht.GetMethod("Patch")!.Invoke(instance, [typeof(Pawn_HealthTracker).GetMethod("CheckForStateChange"), prefix, null, null, null]);
            Check.Equal(true, ClassEnrollment.CanEnrollMage(p.pawn), "native controlled gifted pawn eligible");
            Check.Equal(true, ClassEnrollment.TryEnrollMage(p.pawn), "native enrollment succeeds");
            Check.Equal(Vocation.Mage, comp.State.Vocation, "saved vocation committed");
            Check.Equal(false, p.pawn.story.traits.HasTrait(TorannMagicDefOf.TM_Gifted), "gifted removed through native tracker");
            Check.Equal(true, p.pawn.story.traits.HasTrait(vocation), "neutral vocation installed through native tracker");
            var mana = Check.NotNull(p.pawn.needs.TryGetNeed(TorannMagicDefOf.TM_Mana), "native recovery creates mana");
            var data = p.magic.MagicData;
            Check.Equal(0, data.MagicUserXP, "new data starts at zero XP");
            Check.Equal(0, data.AllMagicPowersWithSkills.Count(x => x.learned), "enrollment grants no spells");
            Check.Equal(0, p.magic.AbilityData.Powers.Count, "runtime spells remain empty");
            Check.Equal(true, p.magic.TickConditionsMet, "magic tick cache enabled");
            mana.CurLevel = 0;
            Check.Equal(false, ClassEnrollment.TryEnrollMage(p.pawn), "repeat enrollment refused");
            Check.Same(data, p.magic.MagicData, "repeat preserves data owner");
            Check.Same(mana, p.pawn.needs.TryGetNeed(TorannMagicDefOf.TM_Mana), "repeat preserves mana owner");
            Check.Equal(0f, mana.CurLevel, "repeat never refills mana");
        }
        finally
        {
            ht.GetMethod("UnpatchAll")!.Invoke(instance, [id]); Current.Game = previousGame;
            TorannMagicDefOf.TM_Mana = oldMana; TorannMagicDefOf.TM_MagicUserHD = oldHediff;
            TorannMagicDefOf.TM_Gifted = oldGifted;
            TM_ClassUtility.CustomClasses = classes;
            DefDatabase<TraitDef>.Clear(); foreach (var trait in traits) DefDatabase<TraitDef>.Add(trait);
            DefDatabase<PawnCapacityDef>.Clear(); foreach (var cap in capacities) DefDatabase<PawnCapacityDef>.Add(cap);
        }
    }

    static void Eligibility(RuntimeFixture f)
    {
        LoadBaseDefinitions(f);
        var traits = DefDatabase<TraitDef>.AllDefsListForReading.ToArray();
        var classes = TM_ClassUtility.CustomClasses;
        var previousGame = Current.Game;
        var harmony = Assembly.Load("0Harmony"); var ht = harmony.GetType("HarmonyLib.Harmony")!;
        var id = "rwom.regression.enrollment." + Guid.NewGuid(); var instance = Activator.CreateInstance(ht, id)!;
        try
        {
            Current.Game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
            Set(Current.Game, "tickManager", new TickManager());
            Set(Current.Game, "maps", new List<Map> { (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map)) });
            if (DefDatabase<TraitDef>.GetNamedSilentFail("TM_VocationMage") == null)
                DefDatabase<TraitDef>.Add(new TraitDef { defName = "TM_VocationMage", degreeDatas = [new TraitDegreeData { degree = 0 }] });
            TM_ClassUtility.CustomClasses = [];
            var p = PawnWithComps(false, false);
            p.pawn.mindState = new Pawn_MindState(p.pawn);
            p.pawn.kindDef = new PawnKindDef { defName = "EligibilityColonist" };
            Set(p.pawn, "factionInt", new Faction { def = new FactionDef { isPlayer = true } });
            Set(p.pawn, "mapIndexOrState", (sbyte)0);
            p.pawn.story.traits.allTraits.Add(new Trait(TorannMagicDefOf.TM_Gifted));
            var c = new CompClassProgression { parent = p.pawn }; p.pawn.AllComps.Add(c);
            Check.Equal(true, ClassEnrollment.CanEnrollMage(p.pawn), "pure gifted new pawn eligible");
            Check.Equal(true, ClassEnrollment.CanEnrollMage(p.pawn), "repeated preflight is stable");
            Check.Same(null!, typeof(CompAbilityUserMagic).GetField("magicData", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(p.magic)!, "preflight does not allocate MagicData");
            Check.Same(null!, c.State!, "preflight does not enroll");
            Set(p.magic, "magicData", new MagicData { MagicUserXP = 1234, MagicAbilityPoints = 7 });
            Check.Equal(false, ClassEnrollment.CanEnrollMage(p.pawn), "saved magic progression refused");
            Check.Equal(false, ClassEnrollment.TryEnrollMage(p.pawn), "explicit try cannot migrate saved progression");
            Check.Equal(1234, p.magic.MagicData.MagicUserXP, "old XP untouched");
            Check.Equal(7, p.magic.MagicData.MagicAbilityPoints, "old points untouched");
            Set(p.magic, "magicData", null);
            Set(p.might, "mightData", new MightData());
            Check.Equal(false, ClassEnrollment.CanEnrollMage(p.pawn), "saved martial progression refused");
            Set(p.might, "mightData", null);
            p.magic.spell_Heal = true;
            Check.Equal(false, ClassEnrollment.CanEnrollMage(p.pawn), "orphaned learned spell flag refused");
            Check.Equal(false, ClassEnrollment.TryEnrollMage(p.pawn), "explicit enrollment respects legacy spell flag");
            Check.Equal(true, p.magic.spell_Heal, "refused enrollment preserves learned flag");
            p.magic.spell_Heal = false;
            Set(p.magic, "abilityData", new AbilityData { Powers = [(PawnAbility)RuntimeHelpers.GetUninitializedObject(typeof(PawnAbility))] });
            Check.Equal(false, ClassEnrollment.CanEnrollMage(p.pawn), "runtime power without data refused");
            Set(p.magic, "abilityData", null);
            foreach (var trait in new[] { TorannMagicDefOf.PhysicalProdigy, TorannMagicDefOf.Priest })
            {
                var t = new Trait(trait); p.pawn.story.traits.allTraits.Add(t);
                Check.Equal(false, ClassEnrollment.CanEnrollMage(p.pawn), "legacy or hybrid class refused");
                p.pawn.story.traits.allTraits.Remove(t);
            }
            var addon = new TraitDef { defName = "KureEnrollmentTest", degreeDatas = [new TraitDegreeData { degree = 0 }] };
            TM_ClassUtility.CustomClasses = [new TM_CustomClass { classTrait = addon, traitDegree = 4, isAdvancedClass = true }];
            var addonTrait = new Trait(addon); p.pawn.story.traits.allTraits.Add(addonTrait);
            Check.Equal(false, ClassEnrollment.CanEnrollMage(p.pawn), "addon advanced trait refused even with mismatched degree");
            p.pawn.story.traits.allTraits.Remove(addonTrait);
            Set(p.pawn, "mapIndexOrState", (sbyte)-1);
            Check.Equal(false, ClassEnrollment.CanEnrollMage(p.pawn), "unspawned pawn refused");
            Set(p.pawn, "mapIndexOrState", (sbyte)0);
            c.BeginNewProgression(Vocation.Mage);
            Check.Equal(false, ClassEnrollment.CanEnrollMage(p.pawn), "already enrolled pawn refused");
        }
        finally
        {
            ht.GetMethod("UnpatchAll")!.Invoke(instance, [id]);
            Current.Game = previousGame;
            TM_ClassUtility.CustomClasses = classes;
            DefDatabase<TraitDef>.Clear(); foreach (var trait in traits) DefDatabase<TraitDef>.Add(trait);
        }
    }

    static void Definitions(RuntimeFixture f)
    {
        var root = XDocument.Load(Path.Combine(f.Repository, "v1.7/Defs/TraitDefs/TM_Vocations.xml")).Root!;
        Check.Equal(3, root.Elements("TraitDef").Count(), "all three vocations defined");
        foreach (var def in root.Elements("TraitDef"))
        {
            Check.Equal("0", def.Element("commonality")!.Value, "vocations are never randomly generated");
            Check.Equal(false, def.Descendants().Any(e => new[] { "statOffsets", "statFactors", "skillGains", "disabledWorkTags" }.Contains(e.Name.LocalName)), "vocations have no class passive bonuses");
        }
    }
}
