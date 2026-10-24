using System.Reflection;
using System.Runtime.CompilerServices;
using RimWorld;
using TorannMagic;
using TorannMagic.Golems;
using Verse;

namespace RWoM.RegressionTests;

internal static class GolemEnergyTests
{
    public static IEnumerable<TestCase> Cases()
    {
        yield return new("GolemEnergy.UpgradedBatterySurvivesSaveLoad", SaveLoad);
        yield return new("GolemEnergy.ActivationUsesUpgradedCapacity", Activate);
    }

    private static void SaveLoad(RuntimeFixture fixture)
    {
        var original = new BatterySave();
        original.Energy.Upgrade_StoredEnergy(3);
        original.Energy.SetEnergy(2200f);
        string path = Path.Combine(fixture.WorkingDirectory, "golem-energy.xml");
        try
        {
            Scribe.saver.InitSaving(path, "regression");
            Scribe_Deep.Look(ref original, "battery");
            Scribe.saver.FinalizeSaving();
        }
        finally { Scribe.ForceStop(); }
        Check.Equal(2200f, original.Energy.StoredEnergy, "saving preserves live energy");
        BatterySave loaded = null!;
        try
        {
            Scribe.loader.InitLoading(path);
            Scribe_Deep.Look(ref loaded, "battery");
            Scribe.loader.FinalizeLoading();
        }
        finally { Scribe.ForceStop(); }
        Check.Equal(2500f, loaded.Energy.StoredEnergyMax, "capacity upgrades survive load");
        Check.Equal(2200f, loaded.Energy.StoredEnergy, "energy above base capacity survives every load pass");
        Check.Equal(1000f, ((CompProperties_Battery)loaded.Energy.props).storedEnergyMax, "shared battery properties stay unchanged");
    }

    private static void Activate(RuntimeFixture fixture)
    {
        var pawn = new Pawn();
        typeof(ThingWithComps).GetField("comps", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(pawn, new List<ThingComp>());
        pawn.needs = new Pawn_NeedsTracker(pawn);
        var definition = new NeedDef { defName = "TM_GolemEnergy", needClass = typeof(Need_GolemEnergy) };
        var need = (Need_GolemEnergy)RuntimeHelpers.GetUninitializedObject(typeof(Need_GolemEnergy));
        need.def = definition;
        need.maxEnergy = 1000f;
        need.energyEfficiency = .5f;
        pawn.needs.AllNeeds.Add(need);
        NeedDef previous = TorannMagicDefOf.TM_GolemEnergy;
        TorannMagicDefOf.TM_GolemEnergy = definition;
        try
        {
            var station = (Building_TMGolemBase)RuntimeHelpers.GetUninitializedObject(typeof(Building_TMGolemBase));
            var energy = new BatterySave().Energy;
            energy.Upgrade_StoredEnergy(3);
            energy.SetEnergy(2200f);
            energy.parent = station;
            typeof(ThingWithComps).GetField("comps", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(station, new List<ThingComp> { energy });
            var golem = new CompGolem { parent = pawn };
            typeof(CompGolem).GetField("dormantThing", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(golem, station);
            typeof(CompGolem).GetMethod("ApplyNeeds", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(golem, null);
            Check.Equal(2500f, need.MaxLevel, "active golem receives upgraded capacity");
            Check.Equal(2200f, need.CurLevel, "activation does not clamp charge to constructor capacity");
        }
        finally { TorannMagicDefOf.TM_GolemEnergy = previous; }
    }

    public sealed class BatterySave : IExposable
    {
        public CompGolemEnergyHandler Energy = new() { props = new CompProperties_GolemEnergyHandler { storedEnergyMax = 1000f, storedEnergyMaxUpgradeIncrease = 500f } };
        public void ExposeData() => Energy.PostExposeData();
    }
}
