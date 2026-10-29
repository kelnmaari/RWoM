using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using RimWorld;
using TorannMagic;
using Verse;
using static RWoM.RegressionTests.GrimoireProgressionTests;

namespace RWoM.RegressionTests;

internal static class TechnomancerLifecycleTests
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static object? Call(object target, string method) => target.GetType().GetMethod(method, Fields)!.Invoke(target, null);
    static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Fields)!.GetValue(target)!;
    public static IEnumerable<TestCase> Cases()
    {
        yield return new("TechnomancerLifecycle.TurretNativeScribePreservesOwnerAndRemainingState", TurretSave);
        yield return new("TechnomancerLifecycle.TurretMissingOwnerAndLegacySnapshot", TurretLegacy);
        yield return new("TechnomancerLifecycle.OverdriveNativeScribeAndAbsentFields", OverdriveSave);
        yield return new("TechnomancerLifecycle.OverdriveMultiOwnerExpiryAndDetachedPause", OverdriveRegistry);
        yield return new("TechnomancerLifecycle.OverdriveNativeFinalPulsePreservesOtherOwner", OverdriveFinalPulse);
    }

    public sealed class Envelope : IExposable
    {
        public static object Target = null!;
        public static string Method = "";
        public void ExposeData() => Call(Target, Method);
    }
    static string Save(RuntimeFixture f, object target, string method, string name)
    {
        string path = Path.Combine(f.WorkingDirectory, name + ".xml");
        Envelope.Target = target; Envelope.Method = method; var envelope = new Envelope();
        try { Scribe.saver.InitSaving(path, "regression"); Scribe_Deep.Look(ref envelope, "native"); Scribe.saver.FinalizeSaving(); }
        finally { Scribe.ForceStop(); Envelope.Target = null!; }
        return path;
    }
    static void Load(string path, object target, string method, params ILoadReferenceable[] refs)
    {
        Envelope.Target = target; Envelope.Method = method; Envelope? envelope = null;
        try
        {
            Scribe.loader.InitLoading(path); Scribe_Deep.Look(ref envelope, "native");
            var directory = (LoadedObjectDirectory)typeof(CrossRefHandler).GetField("loadedObjectDirectory", Fields)!.GetValue(Scribe.loader.crossRefs)!;
            foreach (var item in refs) directory.RegisterLoaded(item);
            Scribe.loader.FinalizeLoading();
        }
        finally { Scribe.ForceStop(); Envelope.Target = null!; }
    }
    static Building Building(int id, bool spawned = true)
    {
        var def = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        def.defName = "TechnomancerLifecycleBuilding"; def.category = ThingCategory.Building;
        var b = new LifecycleBuilding { def = def, thingIDNumber = id };
        Set(b, "mapIndexOrState", (sbyte)(spawned ? 0 : -1));
        return b;
    }
    sealed class LifecycleBuilding : Building
    {
        public override UnityEngine.Vector3 DrawPos => UnityEngine.Vector3.zero;
    }
    sealed class LifecycleWorld : IDisposable
    {
        readonly Game previous = Current.Game;
        public LifecycleWorld()
        {
            Current.Game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
            Set(Current.Game, "maps", new List<Map> { (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map)) });
        }
        public void Dispose() => Current.Game = previous;
    }
    static bool SkipPulsePresentationOrDamage() => false;
    sealed class PulseBoundaries : IDisposable
    {
        readonly Assembly assembly = Assembly.Load("0Harmony");
        readonly object harmony;
        readonly string id = "rwom.techno.lifecycle.pulse." + Guid.NewGuid();
        public PulseBoundaries()
        {
            harmony = Activator.CreateInstance(assembly.GetType("HarmonyLib.Harmony")!, id)!;
            Patch(typeof(TM_MoteMaker), "ThrowGenericFleck");
            Patch(typeof(FleckMaker), "ThrowSmoke");
            Patch(typeof(Verse.Sound.SoundStarter), "PlayOneShot");
            Patch(typeof(TM_Action), "DamageEntities");
        }
        void Patch(Type type, string name)
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Where(m => m.Name == name))
            {
                var prefix = Activator.CreateInstance(assembly.GetType("HarmonyLib.HarmonyMethod")!, typeof(TechnomancerLifecycleTests).GetMethod(nameof(SkipPulsePresentationOrDamage), BindingFlags.Static | BindingFlags.NonPublic)!)!;
                harmony.GetType().GetMethod("Patch")!.Invoke(harmony, new object?[] { method, prefix, null, null, null });
            }
        }
        public void Dispose() => harmony.GetType().GetMethod("UnpatchAll")!.Invoke(harmony, new object[] { id });
    }
    static void OverdriveFinalPulse(RuntimeFixture f)
    {
        LoadBaseDefinitions(f);
        var oldGame = Current.Game;
        var owners = TorannMagic.ModOptions.Constants.GetOverdrivePawnList(); var previous = owners.ToArray(); owners.Clear();
        Rand.PushState(74105);
        try
        {
            Current.Game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
            Set(Current.Game, "maps", new List<Map> { (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map)) });
            using var boundaries = new PulseBoundaries();
            var a = PawnWithComps(true, false); var b = PawnWithComps(true, false);
            a.pawn.thingIDNumber = 74107; b.pawn.thingIDNumber = 74108;
            a.magic.overdriveBuilding = Building(74105); a.magic.overdriveDuration = 1; a.magic.overdrivePowerOutput = 999;
            b.magic.overdriveBuilding = Building(74106); b.magic.overdriveDuration = 7;
            Call(a.magic, "RefreshTechnomancerOverdrive"); Call(b.magic, "RefreshTechnomancerOverdrive");
            // Only presentation and external damage are intercepted: execute the
            // actual final pulse, native decrement and expiry registry branch.
            a.magic.ResolveTechnomancerOverdrive();
            Check.Equal(0, a.magic.overdriveDuration, "actual final native pulse decrements to zero");
            Check.Equal(true, a.magic.overdriveBuilding == null, "final pulse releases target");
            Check.Equal(0f, a.magic.overdrivePowerOutput, "final pulse clears output");
            Check.Equal(1, owners.Count, "native expiry does not clear other registrations");
            Check.Same(b.pawn, owners[0], "other owner's actual registry entry survives");
            Check.Equal(7, b.magic.overdriveDuration, "other effect duration untouched");
        }
        finally { Rand.PopState(); owners.Clear(); owners.AddRange(previous); Current.Game = oldGame; }
    }
    static void TurretSave(RuntimeFixture f)
    {
        LoadBaseDefinitions(f); var p = PawnWithComps(true, false); p.pawn.thingIDNumber = 74101;
        foreach (var skill in p.magic.MagicData.MagicPowerSkill_TechnoTurret) skill.level = 12;
        var turret = new Building_TechnoTurret { manPawn = p.pawn };
        Call(turret, "InitializeTechnoTurret");
        Check.Equal(7200, turret.duration, "native efficiency sets fresh lifetime");
        turret.age = 1700; turret.duration = 6800;
        Set(turret, "rocketCount", 1); Set(turret, "nextRocketFireTick", 1777); Set(turret, "mortarTicksToFire", 2100);
        int range = Get<int>(turret, "mortarMaxRange");
        string path = Save(f, turret, "ExposeTechnoTurretData", "techno-turret");
        var loaded = new Building_TechnoTurret(); Load(path, loaded, "ExposeTechnoTurretData", p.pawn);
        Check.Same(p.pawn, loaded.manPawn, "real Scribe resolves original owner");
        foreach (var skill in p.magic.MagicData.MagicPowerSkill_TechnoTurret) skill.level = 15;
        Call(loaded, "InitializeTechnoTurret"); Call(loaded, "InitializeTechnoTurret");
        Check.Equal(1700, loaded.age, "age preserved"); Check.Equal(6800, loaded.duration, "remaining lifetime not recalculated from changed owner skill");
        Check.Equal(1, Get<int>(loaded, "rocketCount"), "partial salvo preserved");
        Check.Equal(1777, Get<int>(loaded, "nextRocketFireTick"), "rocket deadline preserved");
        Check.Equal(2100, Get<int>(loaded, "mortarTicksToFire"), "mortar deadline preserved");
        Check.Equal(range, Get<int>(loaded, "mortarMaxRange"), "range never compounds on load");
        Check.Equal(12, Get<int>(loaded, "effVal"), "destruction efficiency remains original");
    }
    static void TurretLegacy(RuntimeFixture f)
    {
        var turret = new Building_TechnoTurret { age = 1200, duration = 5100 };
        Call(turret, "InitializeTechnoTurret");
        Check.Equal(false, Get<bool>(turret, "initialized"), "missing owner cannot initialize");
        string path = Save(f, turret, "ExposeTechnoTurretData", "techno-turret-legacy");
        var xml = XDocument.Load(path); var native = xml.Root!.Element("native")!;
        native.Element("technoTurretInitialized")?.Remove(); native.Element("manPawnReference")?.Remove();
        native.Add(new XElement("manPawn", "Legacy pawn display text is not a load reference")); xml.Save(path);
        var loaded = new Building_TechnoTurret(); Load(path, loaded, "ExposeTechnoTurretData");
        Call(loaded, "InitializeTechnoTurret");
        Check.Equal(true, loaded.manPawn == null, "malformed legacy value never invents an owner");
        Check.Equal(true, Get<bool>(loaded, "initialized"), "existing snapshot treated as initialized");
        Check.Equal(1200, loaded.age, "legacy age preserved"); Check.Equal(5100, loaded.duration, "legacy expiry preserved");
    }
    static void OverdriveSave(RuntimeFixture f)
    {
        using var world = new LifecycleWorld();
        LoadBaseDefinitions(f); var p = PawnWithComps(true, false); var building = Building(74102);
        var owners = TorannMagic.ModOptions.Constants.GetOverdrivePawnList(); var previous = owners.ToArray(); owners.Clear();
        try
        {
            p.magic.overdriveBuilding = building; p.magic.overdriveDuration = 13; p.magic.overdrivePowerOutput = 2468; p.magic.overdriveFrequency = 65;
            string path = Save(f, p.magic, "ExposeTechnomancerOverdrive", "techno-overdrive");
            p.magic.overdriveBuilding = null; p.magic.overdriveDuration = 0; p.magic.overdrivePowerOutput = 0;
            Load(path, p.magic, "ExposeTechnomancerOverdrive", building);
            Check.Same(building, p.magic.overdriveBuilding, "building cross-reference resolves"); Check.Equal(13, p.magic.overdriveDuration, "remaining pulses preserved");
            Check.Equal(2468f, p.magic.overdrivePowerOutput, "native output preserved"); Check.Equal(65, p.magic.overdriveFrequency, "native frequency preserved");
            Check.Equal(1, owners.Count(x => x == p.pawn), "postload registers exact owner once");
            var xml = XDocument.Load(path); xml.Root!.Element("native")!.RemoveNodes(); xml.Save(path);
            Load(path, p.magic, "ExposeTechnomancerOverdrive");
            Check.Equal(true, p.magic.overdriveBuilding == null, "absent old reference safe"); Check.Equal(0, p.magic.overdriveDuration, "absent old effect not invented");
            Check.Equal(100, p.magic.overdriveFrequency, "absent frequency native default"); Check.Equal(0, owners.Count, "absent effect unregisters only own stale entry");
        }
        finally { owners.Clear(); owners.AddRange(previous); }
    }
    static void OverdriveRegistry(RuntimeFixture f)
    {
        using var world = new LifecycleWorld();
        LoadBaseDefinitions(f); var a = PawnWithComps(true, false); var b = PawnWithComps(true, false);
        a.pawn.thingIDNumber = 74109; b.pawn.thingIDNumber = 74110;
        var owners = TorannMagic.ModOptions.Constants.GetOverdrivePawnList(); var previous = owners.ToArray(); owners.Clear();
        try
        {
            a.magic.overdriveBuilding = Building(74103); a.magic.overdriveDuration = 1;
            b.magic.overdriveBuilding = Building(74104); b.magic.overdriveDuration = 7;
            Call(a.magic, "RefreshTechnomancerOverdrive"); Call(b.magic, "RefreshTechnomancerOverdrive"); Call(b.magic, "RefreshTechnomancerOverdrive");
            Check.Equal(2, owners.Count, "independent owners register once");
            a.magic.overdriveDuration = 0; a.magic.ResolveTechnomancerOverdrive();
            Check.Equal(1, owners.Count, "one expiry preserves other registration"); Check.Same(b.pawn, owners[0], "other owner retained");
            Set(b.magic.overdriveBuilding, "mapIndexOrState", (sbyte)-1); b.magic.overdrivePowerOutput = 500;
            b.magic.ResolveTechnomancerOverdrive();
            Check.Equal(7, b.magic.overdriveDuration, "detached effect pauses without VFX or damage"); Check.Equal(500f, b.magic.overdrivePowerOutput, "paused output retained");
            Check.Equal(0, owners.Count, "detached target unregisters");
            Set(b.magic.overdriveBuilding, "mapIndexOrState", (sbyte)0); Call(b.magic, "RefreshTechnomancerOverdrive");
            Check.Equal(1, owners.Count, "respawn resumes registration");
            b.magic.overdriveBuilding = null; b.magic.ResolveTechnomancerOverdrive();
            Check.Equal(0, owners.Count, "lost target safely clears"); Check.Equal(0f, b.magic.overdrivePowerOutput, "lost target clears output");
        }
        finally { owners.Clear(); owners.AddRange(previous); }
    }
}
