using System.Reflection;
using System.Runtime.CompilerServices;
using RimWorld;
using TorannMagic;
using Verse;
using AbilityDef = AbilityUser.AbilityDef;

namespace RWoM.RegressionTests;

// Ф5 spike (card 60): the vanilla Ability bridge. A bridge def carries a
// stable spellId, resolves the original TMAbilityDef through the core
// registry, and drives the EXISTING JecsLite cast path via a transient
// companion PawnAbility (rebuilt from knowledge + defs, never scribed).
// The warm cast path (verbs, projectiles, resources) is not rewritten.
internal static class VanillaBridgeTests
{
    public static IEnumerable<TestCase> Cases()
    {
        yield return new("VanillaBridge.DefResolvesOriginal", DefResolvesOriginal);
        yield return new("VanillaBridge.CompanionVerbWiring", CompanionVerbWiring);
        yield return new("VanillaBridge.TrackerGainAndLookup", TrackerGainAndLookup);
        yield return new("VanillaBridge.SleepingDefDegrades", SleepingDefDegrades);
    }

    private static void EnsureCoreLoaded(RuntimeFixture f)
    {
        string coreDll = Path.Combine(f.Repository, "TorannMagic.Core", "Assemblies", "TorannMagic.Core.dll");
        if (File.Exists(coreDll)) Assembly.LoadFrom(coreDll);
    }

    private static TMAbilityDef Original(string name)
    {
        var def = (TMAbilityDef)RuntimeHelpers.GetUninitializedObject(typeof(TMAbilityDef));
        def.defName = name;
        def.label = name;
        def.MainVerb = new AbilityUser.VerbProperties_Ability { verbClass = typeof(AbilityUser.Verb_UseAbility) };
        return def;
    }

    private static TMBridgeAbilityDef BridgeDef(string name, string spellId)
    {
        var def = (TMBridgeAbilityDef)RuntimeHelpers.GetUninitializedObject(typeof(TMBridgeAbilityDef));
        def.defName = name;
        def.label = name;
        def.abilityClass = typeof(TMBridgeAbility);
        def.spellId = spellId;
        // GetUninitializedObject skips field initializers; the vanilla Ability
        // ctor reads def.comps and the tracker reads def.verbProperties. A real
        // XML bridge def carries a VerbProperties_Ability copy of the original.
        def.comps = new List<AbilityCompProperties>();
        def.verbProperties = new AbilityUser.VerbProperties_Ability { verbClass = typeof(AbilityUser.Verb_UseAbility) };
        return def;
    }

    private static Pawn BridgePawn()
    {
        if (Current.Game == null)
        {
            Current.Game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
            typeof(Game).GetField("tickManager", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
                .SetValue(Current.Game, new TickManager());
            typeof(Game).GetField("uniqueIDsManager", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
                .SetValue(Current.Game, new UniqueIDsManager());
        }
        var pawnDef = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        pawnDef.defName = "Regression_BridgeHuman";
        pawnDef.race = new RaceProperties { intelligence = Intelligence.Humanlike };
        var pawn = new Pawn { def = pawnDef };
        // The companion PawnAbility binds to the pawn's CompAbilityUser
        // (CompAbilityUserMagic is a CompAbilityUser subclass). comps lives on
        // ThingWithComps, not Pawn.
        for (Type type = pawn.GetType(); type != null; type = type.BaseType)
        {
            var comps = type.GetField("comps", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (comps == null) continue;
            comps.SetValue(pawn, new List<ThingComp> { new CompAbilityUserMagic { parent = pawn } });
            break;
        }
        return pawn;
    }

    // RED 1: the bridge def resolves its original through the stable spellId
    // via the core registry.
    private static void DefResolvesOriginal(RuntimeFixture f)
    {
        EnsureCoreLoaded(f);
        var original = Original("TM_Heal");
        CoreBridge.RegisterSpell(original); // convention id rwom.tm_heal
        var def = BridgeDef("TM_Bridge_Heal", "rwom.tm_heal");
        Check.Same(original, def.Original, "bridge def resolves the original TMAbilityDef");

        var defMissing = BridgeDef("TM_Bridge_Lost", "rwom.tm_removed");
        Check.Equal(true, defMissing.Original == null, "missing original resolves null (sleeping), never throws");
    }

    // RED 2: the companion PawnAbility wires the real verb: Ability backref,
    // caster, and the original's MainVerb props.
    private static void CompanionVerbWiring(RuntimeFixture f)
    {
        EnsureCoreLoaded(f);
        CoreBridge.RegisterSpell(Original("TM_Heal"));
        var def = BridgeDef("TM_Bridge_Heal2", "rwom.tm_heal");
        var pawn = BridgePawn();
        var ability = (TMBridgeAbility)AbilityUtility.MakeAbility(def, pawn);
        Check.NotNull(ability, "MakeAbility constructs the bridge ability");
        var companion = ability.Companion;
        Check.NotNull(companion, "companion PawnAbility exists");
        Check.Same(def.Original, companion.Def, "companion wraps the original def");
        var verb = companion.Verb;
        Check.NotNull(verb, "companion verb initialized");
        Check.Same(companion, verb.Ability, "verb points back at the companion");
        Check.Same(pawn, verb.caster, "verb caster is the pawn");
        Check.Same(def.Original.MainVerb, verb.verbProps, "verb uses the original MainVerb props");
    }

    // RED 3: GainAbility into the vanilla tracker; the bridge ability is
    // findable through the vanilla lookup.
    private static void TrackerGainAndLookup(RuntimeFixture f)
    {
        EnsureCoreLoaded(f);
        CoreBridge.RegisterSpell(Original("TM_Heal"));
        var def = BridgeDef("TM_Bridge_Heal3", "rwom.tm_heal");
        var pawn = BridgePawn();
        var tracker = new Pawn_AbilityTracker(pawn);
        tracker.GainAbility(def);
        var gained = tracker.GetAbility(def);
        Check.NotNull(gained, "vanilla tracker holds the bridge ability");
        Check.Equal(true, gained is TMBridgeAbility, "gained ability is the bridge subclass");
        tracker.RemoveAbility(def);
        Check.Equal(true, tracker.GetAbility(def) == null, "removal works");
    }

    // RED 4: a bridge whose original is gone reports disabled through the
    // vanilla gate without a companion and without throwing.
    private static void SleepingDefDegrades(RuntimeFixture f)
    {
        EnsureCoreLoaded(f);
        var def = BridgeDef("TM_Bridge_Lost2", "rwom.tm_removed");
        var pawn = BridgePawn();
        var ability = (TMBridgeAbility)AbilityUtility.MakeAbility(def, pawn);
        Check.Equal(true, ability.Companion == null, "no companion without the original def");
        bool disabled;
        try
        {
            disabled = ability.GizmoDisabled(out _);
        }
        finally
        {
            // Headless runs have no language pack, so the vanilla disabled-reason
            // Translate() logs "No active language!" — an environment limitation.
            RuntimeLog.Clear();
        }
        Check.Equal(true, disabled, "sleeping bridge is gizmo-disabled");
    }
}
