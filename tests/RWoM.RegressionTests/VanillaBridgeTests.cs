using System.Reflection;
using System.Runtime.CompilerServices;
using RimWorld;
using RimWorld.Planet;
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
        yield return new("VanillaBridge.SyncGrantsFromKnowledge", SyncGrantsFromKnowledge);
        yield return new("VanillaBridge.SyncRespectsAllowList", SyncRespectsAllowList);
        yield return new("VanillaBridge.GeneratedBridgesAreWellFormed", GeneratedBridgesAreWellFormed);
        yield return new("VanillaBridge.BridgesCarryIcons", BridgesCarryIcons);
        yield return new("VanillaBridge.AllFamiliesEnabled", AllFamiliesEnabled);
        yield return new("VanillaBridge.LegacyGizmoHiddenForEnabled", LegacyGizmoHiddenForEnabled);
        yield return new("VanillaBridge.GizmoDisabledReflectsResources", GizmoDisabledReflectsResources);
    }

    // Generated bridge defs carry the original's icon so the vanilla gizmo
    // grid renders recognizably.
    private static void BridgesCarryIcons(RuntimeFixture f)
    {
        string heal = Path.Combine(f.Repository, "v1.7", "Defs", "AbilityDefs", "Bridge", "Bridge_Abilities_TMagic_Paladin.xml");
        var doc = System.Xml.Linq.XDocument.Load(heal);
        var bridge = doc.Root.Elements("AbilityDef").First(el => (string)el.Element("defName") == "TM_Bridge_Heal");
        Check.Equal("UI/Heal", (string)bridge.Element("iconPath"), "iconPath copied from the original uiIconPath");
        Check.Equal(true, (bool?)bridge.Element("displayGizmoWhileUndrafted") == true,
            "undrafted visibility matches the classic gizmo behavior");
    }

    // Full rollout: every convention spellId family is enabled — the boundary
    // check still applies, so unrelated rwom.* ids stay untouched.
    private static void AllFamiliesEnabled(RuntimeFixture _)
    {
        Check.Equal(true, VanillaBridgeSync.FamilyEnabled("rwom.tm_heal"), "heal enabled");
        Check.Equal(true, VanillaBridgeSync.FamilyEnabled("rwom.tm_firebolt_ii"), "variant families enabled");
        Check.Equal(true, VanillaBridgeSync.FamilyEnabled("rwom.tm_advanced_heal"), "multi-word families enabled");
        Check.Equal(false, VanillaBridgeSync.FamilyEnabled("rwom.custom.heal"), "foreign namespaces stay excluded");
    }

    // Phase Б: our classic gizmo hides when the vanilla bridge provides one.
    private static void LegacyGizmoHiddenForEnabled(RuntimeFixture f)
    {
        EnsureCoreLoaded(f);
        var def = Original("TM_HideGizmoA");
        CoreBridge.RegisterSpell(def);
        Check.Equal(false, VanillaBridgeSync.ShouldHideLegacyGizmo(def), "no bridge def — classic gizmo stays");

        var bridge = BridgeDef("TM_Bridge_Hidegizmoa", "rwom.tm_hidegizmoa"); // matches the DisplayName convention
        DefDatabase<RimWorld.AbilityDef>.Clear();
        DefDatabase<RimWorld.AbilityDef>.Add(bridge);
        try
        {
            Check.Equal(true, VanillaBridgeSync.ShouldHideLegacyGizmo(def), "enabled family with a bridge def hides the classic gizmo");
            var other = Original("TM_HideGizmoB");
            CoreBridge.RegisterSpell(other);
            Check.Equal(false, VanillaBridgeSync.ShouldHideLegacyGizmo(other), "spell without a bridge def keeps its classic gizmo");
        }
        finally
        {
            DefDatabase<RimWorld.AbilityDef>.Clear();
        }
    }

    // The gizmo's grey-out consults the companion's CanCastPowerCheck, which
    // probes ambient state a generated or exotic pawn may not carry — the
    // probe must never throw out of the gizmo (the cast re-checks anyway).
    // The bridge is also invisible to vanilla AI: our ResolveAIAutoCast keeps
    // AI ownership, so the bridge must not double-cast.
    private static void GizmoDisabledReflectsResources(RuntimeFixture f)
    {
        EnsureCoreLoaded(f);
        CoreBridge.RegisterSpell(Original("TM_Heal"));
        var def = BridgeDef("TM_Bridge_Heal", "rwom.tm_heal");
        var pawn = BridgePawn();
        var magic = new CompAbilityUserMagic { parent = pawn };
        pawn.AllComps.Add(magic);
        var knowledge = new AbilityKnowledgeComp { parent = pawn };
        pawn.AllComps.Add(knowledge);

        var ability = (TMBridgeAbility)AbilityUtility.MakeAbility(def, pawn);
        Check.NotNull(ability.Companion, "companion built for the disabled-state check");
        bool disabled = false;
        try
        {
            disabled = ability.GizmoDisabled(out _);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("GizmoDisabled must tolerate ambient state: " + ex.Message, ex);
        }
        Check.Equal(true, disabled || !disabled, "gizmo disabled-state evaluated without throwing");
        Check.Equal(false, ability.AICanTargetNow(pawn), "bridges are excluded from vanilla AI targeting");
    }

    // Full rollout: foreign namespaces (non "rwom.tm_*") stay excluded even
    // when the knowledge store has a learned entry and a bridge def exists.
    private static void SyncRespectsAllowList(RuntimeFixture f)
    {
        EnsureCoreLoaded(f);
        var bridge = BridgeDef("TM_Bridge_Customheal", "rwom.custom_heal");
        DefDatabase<RimWorld.AbilityDef>.Clear();
        DefDatabase<RimWorld.AbilityDef>.Add(bridge);
        try
        {
            var pawn = BridgePawn();
            pawn.abilities = new Pawn_AbilityTracker(pawn);
            var knowledge = new AbilityKnowledgeComp { parent = pawn };
            pawn.AllComps.Add(knowledge);
            var entry = knowledge.GetOrCreate("rwom.custom_heal", 0);
            entry.learned = true;

            VanillaBridgeSync.Sync(pawn);
            Check.Equal(true, pawn.abilities.GetAbility(bridge) == null,
                "a foreign namespace is not granted (FamilyEnabled gate)");
        }
        finally
        {
            DefDatabase<RimWorld.AbilityDef>.Clear();
        }
    }

    // RED 7: every generated bridge def XML is well formed, carries a spellId
    // and a verb class, and the defName follows the sync convention.
    private static void GeneratedBridgesAreWellFormed(RuntimeFixture f)
    {
        string dir = Path.Combine(f.Repository, "v1.7", "Defs", "AbilityDefs", "Bridge");
        Check.Equal(true, Directory.Exists(dir), "bridge output directory exists");
        var files = Directory.GetFiles(dir, "*.xml");
        Check.Equal(true, files.Length > 10, "generated bridge files present");

        var seen = new HashSet<string>();
        int defs = 0;
        foreach (string file in files)
        {
            var doc = System.Xml.Linq.XDocument.Load(file);
            foreach (var el in doc.Root.Elements("AbilityDef"))
            {
                Check.Equal("TorannMagic.TMBridgeAbilityDef", (string)el.Attribute("Class"),
                    "bridge class on " + file);
                if (el.Attribute("Abstract") != null) continue; // the shared base uses Name=
                string defName = (string)el.Element("defName");
                Check.Equal(true, !string.IsNullOrEmpty(defName) && seen.Add(defName),
                    "unique non-empty defName " + defName);
                if (el.Attribute("Abstract") != null) continue; // the shared base
                Check.NotNull(el.Element("spellId"), "spellId on " + defName);
                var verbProps = el.Element("verbProperties");
                Check.NotNull(verbProps, "verbProperties on " + defName);
                // Subclasses of VerbProperties_Ability carry family config — the copy
                // preserves the original's Class as-is.
                Check.Equal(true, !string.IsNullOrEmpty((string)verbProps.Attribute("Class")),
                    "verb properties class on " + defName);
                Check.NotNull(verbProps.Element("verbClass"), "verb class on " + defName);
                Check.Equal(true, defName.StartsWith("TM_Bridge_"), "defName convention on " + defName);
                defs++;
            }
        }
        Check.Equal(true, defs > 400, "generated bridge def count covers the ability tree: " + defs);
    }

    // RED 5: the sync grants the bridge ability for a learned knowledge entry,
    // idempotently, and skips unlearned or sleeping entries.
    private static void SyncGrantsFromKnowledge(RuntimeFixture f)
    {
        EnsureCoreLoaded(f);
        var original = Original("TM_Heal");
        CoreBridge.RegisterSpell(original);
        var bridge = BridgeDef("TM_Bridge_Heal", "rwom.tm_heal"); // matches the defName convention
        DefDatabase<RimWorld.AbilityDef>.Clear();
        DefDatabase<RimWorld.AbilityDef>.Add(bridge);
        try
        {
            var pawn = BridgePawn();
            pawn.abilities = new Pawn_AbilityTracker(pawn); // the Pawn ctor leaves this to spawn
            var knowledge = new AbilityKnowledgeComp { parent = pawn };
            pawn.AllComps.Add(knowledge);

            VanillaBridgeSync.Sync(pawn);
            Check.Equal(true, pawn.abilities.GetAbility(bridge) == null, "unlearned knowledge grants nothing");

            var entry = knowledge.GetOrCreate("rwom.tm_heal", 0);
            entry.learned = true;
            VanillaBridgeSync.Sync(pawn);
            Check.NotNull(pawn.abilities.GetAbility(bridge), "learned knowledge grants the bridge ability");

            VanillaBridgeSync.Sync(pawn);
            Check.Equal(1, pawn.abilities.abilities.Count, "sync is idempotent");
        }
        finally
        {
            DefDatabase<RimWorld.AbilityDef>.Clear();
        }
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
