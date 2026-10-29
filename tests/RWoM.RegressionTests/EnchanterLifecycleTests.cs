using System.Reflection;
using System.Runtime.CompilerServices;
using TorannMagic;
using TorannMagic.ClassProgression;
using Verse;
using static RWoM.RegressionTests.GrimoireProgressionTests;

namespace RWoM.RegressionTests;

internal static class EnchanterLifecycleTests
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    public static IEnumerable<TestCase> Cases()
    {
        yield return new("EnchanterLifecycle.SustainedReferencesAndNativeControlsSurviveRestore", Sustained);
        yield return new("EnchanterLifecycle.EmptySavedStateDoesNotInventSustainedEffects", Empty);
        yield return new("EnchanterLifecycle.EnchantedWeaponNativeScribeKeepsBothSourceReferences", WeaponSave);
        yield return new("EnchanterLifecycle.PolymorphCancelPreservesOriginalIdentity", Cancel);
    }

    sealed class Fixture : IDisposable
    {
        readonly List<(TMAbilityDef def, Type type, bool initialize)> abilities = new();
        public readonly Pawn Pawn;
        public readonly CompAbilityUserMagic Magic;
        public Fixture(RuntimeFixture f)
        {
            LoadBaseDefinitions(f);
            foreach (var a in DefDatabase<TMAbilityDef>.AllDefsListForReading)
            { abilities.Add((a, a.abilityClass, a.shouldInitialize)); a.abilityClass = typeof(AbilityUser.PawnAbility); a.shouldInitialize = true; }
            var p = PawnWithComps(true, false); Pawn = p.pawn; Magic = p.magic;
            var progression = new CompClassProgression { parent = Pawn }; Pawn.AllComps.Add(progression);
            progression.BeginNewProgression(Vocation.Mage);
            Check.Equal(true, progression.State.TryAddFoundation("rwom.enchanter", DisciplineDomain.Magic), "saved traitless foundation");
        }
        public void Dispose()
        {
            foreach (var a in abilities) { a.def.abilityClass = a.type; a.def.shouldInitialize = a.initialize; }
        }
    }
    static Thing Item(int id)
    {
        var def = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        def.defName = "EnchanterLifecycleItem"; def.category = ThingCategory.Item;
        return new Thing { def = def, thingIDNumber = id };
    }
    static int Count(CompAbilityUserMagic magic, TMAbilityDef def) => magic.AbilityData.Powers.Count(a => a.Def == def);
    static void Sustained(RuntimeFixture f)
    {
        using var fixture = new Fixture(f); var magic = fixture.Magic;
        var stone = Item(74501); var target = PawnWithComps(false, false).pawn;
        magic.enchanterStones.Add(stone); magic.weaponEnchants.Add(target);
        var stones = magic.enchanterStones; var enchants = magic.weaponEnchants;
        magic.maxMP = .57f; magic.mpRegenRate = .83f;
        for (int i = 0; i < 2; i++)
        {
            Check.Equal(true, ClassSpellAccess.RestoreLearned(magic), "native saved owner restoration");
            magic.ResolveSustainers();
        }
        Check.Same(stones, magic.enchanterStones, "original stone list retained");
        Check.Same(enchants, magic.weaponEnchants, "original weapon list retained");
        Check.Equal(1, stones.Count, "no extra stones"); Check.Same(stone, stones[0], "actual stone retained");
        Check.Equal(1, enchants.Count, "no extra targets"); Check.Same(target, enchants[0], "actual enchant target retained");
        Check.Equal(.57f, magic.maxMP, "restoration does not reset existing upkeep-adjusted capacity");
        Check.Equal(.83f, magic.mpRegenRate, "restoration does not reset existing upkeep-adjusted regeneration");
        Check.Equal(1, Count(magic, TorannMagicDefOf.TM_DismissEnchanterStones), "native stone control restored once");
        Check.Equal(1, Count(magic, TorannMagicDefOf.TM_DispelEnchantWeapon), "native weapon control restored once");
        stones.Clear(); enchants.Clear(); magic.ResolveSustainers();
        Check.Equal(0, Count(magic, TorannMagicDefOf.TM_DismissEnchanterStones), "native control removed when stones end");
        Check.Equal(0, Count(magic, TorannMagicDefOf.TM_DispelEnchantWeapon), "native control removed when enchantments end");
    }
    static void Empty(RuntimeFixture f)
    {
        using var fixture = new Fixture(f); var magic = fixture.Magic;
        foreach (var power in magic.MagicData.MagicPowersE) power.learned = true;
        for (int i = 0; i < 2; i++) { ClassSpellAccess.RestoreLearned(magic); magic.ResolveSustainers(); }
        Check.Equal(0, magic.enchanterStones.Count, "even saved learned native owners do not create stones");
        Check.Equal(0, magic.weaponEnchants.Count, "saved owners do not create weapon effects");
        Check.Equal(0, Count(magic, TorannMagicDefOf.TM_DismissEnchanterStones), "no empty stone control");
        Check.Equal(0, Count(magic, TorannMagicDefOf.TM_DispelEnchantWeapon), "no empty weapon control");
    }
    public sealed class WeaponEnvelope : IExposable
    {
        public static HediffComp_EnchantedWeapon Target = null!;
        public void ExposeData() => Target.CompExposeData();
    }
    static void WeaponSave(RuntimeFixture f)
    {
        LoadBaseDefinitions(f); var owner = PawnWithComps(true, false).pawn; owner.thingIDNumber = 74503;
        var weapon = Item(74504);
        var source = new HediffComp_EnchantedWeapon { enchanterPawn = owner, enchantedWeapon = weapon };
        Set(source, "initialized", true);
        string path = Path.Combine(f.WorkingDirectory, "enchanter-weapon-source.xml");
        var envelope = new WeaponEnvelope(); WeaponEnvelope.Target = source;
        try { Scribe.saver.InitSaving(path, "regression"); Scribe_Deep.Look(ref envelope, "native"); Scribe.saver.FinalizeSaving(); }
        finally { Scribe.ForceStop(); }
        var restored = new HediffComp_EnchantedWeapon(); WeaponEnvelope.Target = restored;
        try
        {
            Scribe.loader.InitLoading(path); Scribe_Deep.Look(ref envelope, "native");
            var directory = (LoadedObjectDirectory)typeof(CrossRefHandler).GetField("loadedObjectDirectory", Fields)!.GetValue(Scribe.loader.crossRefs)!;
            directory.RegisterLoaded(owner); directory.RegisterLoaded(weapon); Scribe.loader.FinalizeLoading();
        }
        finally { Scribe.ForceStop(); WeaponEnvelope.Target = null!; }
        Check.Same(owner, restored.enchanterPawn, "native Scribe resolves exact source caster");
        Check.Same(weapon, restored.enchantedWeapon, "native Scribe resolves exact enchanted weapon");
        Check.Equal(true, (bool)typeof(HediffComp_EnchantedWeapon).GetField("initialized", Fields)!.GetValue(restored)!, "initialized saved effect retained");
    }
    static void Cancel(RuntimeFixture f)
    {
        using var fixture = new Fixture(f); var form = PawnWithComps(false, false).pawn;
        var poly = new CompPolymorph { ParentPawn = form, Original = fixture.Pawn, Spawner = fixture.Pawn, Temporary = false, TicksLeft = 1200, TicksToDestroy = 1800 };
        Set(poly, "initialized", true);
        ClassSpellAccess.RestoreLearned(fixture.Magic);
        poly.CancelPolymorph(); poly.CancelPolymorph();
        Check.Same(fixture.Pawn, poly.Original, "cancel keeps single original until native reversion");
        Check.Same(fixture.Pawn, poly.Spawner, "self-shift retains source identity");
        Check.Same(form, poly.ParentPawn, "cancel does not synthesize a replacement form");
        Check.Equal(true, poly.Temporary, "cancel schedules native expiry");
        Check.Equal(1, poly.TicksLeft, "repeated cancel schedules once");
        Check.Equal(1800, poly.TicksToDestroy, "original lifetime snapshot unchanged");
    }
}
