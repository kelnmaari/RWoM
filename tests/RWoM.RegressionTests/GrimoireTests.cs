using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using RimWorld;
using TorannMagic;
using Verse;
namespace RWoM.RegressionTests;
internal static class GrimoireTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static void Set(object target, string name, object value) {
        for (Type? t = target as Type ?? target.GetType(); t != null; t = t.BaseType) {
            var field = t.GetField(name, Flags); if (field != null) { field.SetValue(target is Type ? null : target, value); return; }
        }
        throw new Exception("Missing fixture field " + name);
    }
    static T Blank<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    public static IEnumerable<TestCase> Cases() {
        foreach (var item in new[] { ("Fighter", false,true,false, false,true), ("Mage",true,false,false,true,false), ("Hybrid",true,true,false,true,false), ("Faceless",true,true,true,false,true), ("Neither",false,false,false,false,false) }) {
            var c = item; yield return new("Grimoire.Visibility." + c.Item1, f => Visibility(c.Item2,c.Item3,c.Item4,c.Item5,c.Item6));
        }
        yield return new("Grimoire.Visibility.FacelessWithSharedRaceMagic", f => Visibility(true,true,true,false,true,true));
        yield return new("Grimoire.DuplicateClassAbilitiesRenderOnce", DuplicateAbilitiesRenderOnce);
        yield return new("Grimoire.Metadata", Metadata);
        yield return new("Grimoire.LocalizationContracts", Localization);
        yield return new("Grimoire.InstalledIsekaiExactNameGate", IsekaiGate);
    }
    static void Visibility(bool magic, bool might, bool faceless, bool showMagic, bool showMight, bool sharedRaceMagic = false) {
        var root = Blank<Root_Play>(); var ui = Blank<UIRoot_Play>(); var map = Blank<MapInterface>(); var selector = Blank<Selector>();
        root.uiRoot = ui; ui.mapUI = map; map.selector = selector; Set(typeof(Current), "rootInt", root);
        var def = Blank<ThingDef>(); def.defName="GrimoireFixtureHuman"; def.race = new RaceProperties { intelligence=Intelligence.Humanlike };
        var pawn = new Pawn { def=def }; pawn.story = new Pawn_StoryTracker(pawn) { traits = new TraitSet(pawn) };
        Set(pawn, "factionInt", new Faction { def = new FactionDef { isPlayer=true } }); Set(pawn,"comps",new List<ThingComp>());
        if (faceless) pawn.story.traits.allTraits.Add(new Trait(TorannMagicDefOf.Faceless));
        if (magic) { var comp = new CompAbilityUserMagic { parent=pawn }; Set(comp,"initializedIsMagicUser",true); Set(comp,"isMagicUser",true); pawn.AllComps.Add(comp); }
        if (might) { var comp = new CompAbilityUserMight { parent=pawn }; Set(comp,"initializedIsMightUser",true); Set(comp,"isMightUser",true); pawn.AllComps.Add(comp); }
        TM_Calc.InvalidateCompCache(pawn); Set(selector,"selected",new List<object> { pawn });
        var mt = Blank<ITab_Pawn_Magic>(); var ft = Blank<ITab_Pawn_Might>();
        // ResolveMagicTab deliberately skips Faceless; a different mage of the same
        // race can nevertheless have registered the shared Magic tab earlier.
        def.inspectorTabsResolved = new List<InspectTabBase>();
        if ((magic && !faceless) || sharedRaceMagic) def.inspectorTabsResolved.Add(mt);
        if (might) def.inspectorTabsResolved.Add(ft);
        TorannMagic.ModOptions.Settings.Instance = new TorannMagic.ModOptions.Settings { grimoireOnAbilityBar=false };
        Check.Equal(showMight,ft.IsVisible,"Might owns Faceless even without a race Magic registration"); Check.Equal(showMagic,mt.IsVisible,"Magic owns ordinary hybrids only");
        Check.Equal(magic || might ? 1 : 0,pawn.GetInspectTabs().Count(t => t.IsVisible),"Exactly one visible registered entry in inspect mode");
        TorannMagic.ModOptions.Settings.Instance.grimoireOnAbilityBar=true;
        Check.Equal(false,mt.IsVisible,"Ability bar suppresses Magic tab"); Check.Equal(false,ft.IsVisible,"Ability bar suppresses Might tab");
        Check.Equal(0,pawn.GetInspectTabs().Count(t => t.IsVisible),"Ability-bar mode has no inspect entry");
        TorannMagic.ModOptions.Settings.Instance.grimoireOnAbilityBar=false; Set(pawn,"factionInt",new Faction { def = new FactionDef() });
        Check.Equal(false,mt.IsVisible,"Non-colonist Magic gate"); Check.Equal(false,ft.IsVisible,"Non-colonist Might gate");
        Set(selector,"selected",new List<object>()); Check.Equal(false,mt.IsVisible,"No selection Magic gate"); Check.Equal(false,ft.IsVisible,"No selection Might gate");
    }
    // Warlock, Succubus and Shadow collections each define their own power objects
    // for the same abilities (Soul Bond, Shadow Bolt, Dominate); the grimoire must
    // render one card per ability, preferring the pawn's own class collection.
    static void DuplicateAbilitiesRenderOnce(RuntimeFixture f) {
        var addRenderable = typeof(Window_TMGrimoire).GetMethod("AddRenderable", Flags, null,
            new[] { typeof(HashSet<MagicPower>), typeof(HashSet<AbilityUser.AbilityDef>),
                    typeof(System.Collections.Generic.IEnumerable<MagicPower>),
                    typeof(System.Collections.Generic.List<TMAbilityDef>),
                    typeof(System.Collections.Generic.List<TMAbilityDef>) }, null);
        var dom = f.AddAbility("TM_Dominate");
        var powerWD = new MagicPower(new System.Collections.Generic.List<AbilityUser.AbilityDef> { dom });
        var powerSD = new MagicPower(new System.Collections.Generic.List<AbilityUser.AbilityDef> { dom });
        var allowed = new HashSet<MagicPower>();
        var claimed = new HashSet<AbilityUser.AbilityDef>();
        var filter = new System.Collections.Generic.List<TMAbilityDef> { dom };
        addRenderable.Invoke(null, new object[] { allowed, claimed, new System.Collections.Generic.List<MagicPower> { powerWD }, filter, null });
        addRenderable.Invoke(null, new object[] { allowed, claimed, new System.Collections.Generic.List<MagicPower> { powerSD, powerWD }, filter, null });
        Check.Equal(1, allowed.Count, "shared class ability renders once");
        Check.Same(powerWD, System.Linq.Enumerable.First(allowed), "own class collection object preferred");
    }

    static void Metadata(RuntimeFixture f) {
        var doc=XDocument.Load(Path.Combine(f.Repository,"About/About.xml"));
        Check.Equal("A RimWorld of Magic",doc.Root!.Element("name")!.Value,"Canonical ecosystem identity");
        Check.Equal("Torann.ARimworldOfMagic",doc.Root.Element("packageId")!.Value,"Stable package ID");
        Check.Equal(true,System.Text.Encoding.UTF8.GetByteCount(doc.Root.Element("description")!.Value)<=8000,"Workshop description byte budget");
    }
    // Redirect all engine metadata/config paths before first ModLister/ModsConfig access.
    // The real gate runs with controlled active metadata; installation XML is read only.
    public static void Initialize(string repository) {
        string sandbox = Path.Combine(Path.GetDirectoryName(typeof(GrimoireTests).Assembly.Location)!, "metadata-fixture");
        Directory.CreateDirectory(Path.Combine(sandbox,"Data")); Directory.CreateDirectory(Path.Combine(sandbox,"Mods")); Directory.CreateDirectory(Path.Combine(sandbox,"Config"));
        Set(typeof(GenFilePaths),"saveDataPath",sandbox); Set(typeof(GenFilePaths),"modsFolderPath",Path.Combine(sandbox,"Mods")); Set(typeof(GenFilePaths),"officialModsFolderPath",Path.Combine(sandbox,"Data"));
        string aboutDir = Path.Combine(sandbox,"Mods","Fixture","About"); Directory.CreateDirectory(aboutDir);
        var about = XDocument.Load(Path.Combine(repository,"About/About.xml"));
        string name = about.Root!.Element("name")!.Value;
        void WriteIdentity(string identity) => new XDocument(new XElement("ModMetaData",new XElement("name",identity),new XElement("author","Regression"),new XElement("packageId","Torann.ARimworldOfMagic"),new XElement("supportedVersions",new XElement("li","1.7")))).Save(Path.Combine(aboutDir,"About.xml"));
        WriteIdentity(name);
        new XDocument(new XElement("ModsConfigData", new XElement("version",VersionControl.CurrentVersionString), new XElement("activeMods",new XElement("li","torann.arimworldofmagic")),new XElement("knownExpansions"))).Save(Path.Combine(sandbox,"Config","ModsConfig.xml"));
        ModLister.EnsureInit();
    }
    static void IsekaiGate(RuntimeFixture f) {
        string sandbox = Path.Combine(Path.GetDirectoryName(typeof(GrimoireTests).Assembly.Location)!, "metadata-fixture");
        string aboutDir = Path.Combine(sandbox,"Mods","Fixture","About");
        string name = XDocument.Load(Path.Combine(f.Repository,"About/About.xml")).Root!.Element("name")!.Value;
        void WriteIdentity(string identity) => new XDocument(new XElement("ModMetaData",new XElement("name",identity),new XElement("author","Regression"),new XElement("packageId","Torann.ARimworldOfMagic"),new XElement("supportedVersions",new XElement("li","1.7")))).Save(Path.Combine(aboutDir,"About.xml"));
        var mods = (List<ModMetaData>)typeof(ModLister).GetField("mods",Flags)!.GetValue(null)!;
        var original = mods.ToList();
        var originalActive = typeof(ModsConfig).GetField("activeModsHashSet",Flags)!.GetValue(null)!;
        // Jecs assemblies live under <workshop>/<id>/1.7/Assemblies.
        string installed = Path.Combine(Directory.GetParent(Directory.GetParent(Directory.GetParent(Path.GetDirectoryName(typeof(AbilityUser.AbilityDef).Assembly.Location)!)!.FullName)!.FullName)!.FullName,"3657580708/Patches/RoMTreePatches.xml");
        var source = new XmlDocument(); source.Load(installed);
        var gateNode = source.SelectSingleNode("/Patch/Operation[@Class='PatchOperationFindMod']")!;
        Check.Equal("A RimWorld of Magic",gateNode.SelectSingleNode("mods/li")!.InnerText,"Installed native gate identity");
        // Retain the installed gate and its exact-name criteria; use a marker branch
        // instead of importing the entire unrelated Isekai passive-tree definition set.
        var gateDoc = new XmlDocument(); gateDoc.LoadXml(gateNode.OuterXml);
        var branch=gateDoc.SelectSingleNode("/Operation/match")!;
        var marker=gateDoc.CreateElement("match"); marker.SetAttribute("Class","PatchOperationAdd");
        marker.InnerXml="<xpath>/Defs</xpath><value><GateMatched /></value>"; branch.ParentNode!.ReplaceChild(marker,branch);
        try {
            bool Match(string identity, bool active) {
                WriteIdentity(identity); mods.Clear(); mods.Add(new ModMetaData(Path.GetDirectoryName(aboutDir)!));
                Set(typeof(ModsConfig),"activeModsHashSet",active ? new HashSet<string> { "torann.arimworldofmagic" } : new HashSet<string>());
                var operation=DirectXmlToObject.ObjectFromXml<PatchOperation>(gateDoc.DocumentElement!,false);
                var defs=new XmlDocument(); defs.LoadXml("<Defs />"); Check.Equal(true,operation.Apply(defs),"Real PatchOperationFindMod execution");
                return defs.SelectSingleNode("/Defs/GateMatched")!=null;
            }
            Check.Equal(false,Match("A RimWorld of Magic — 1.7 (Unofficial)",true),"Former unofficial identity does not match installed exact-name gate");
            Check.Equal(false,Match(name,false),"Inactive canonical mod does not match");
            Check.Equal(true,Match(name,true),"Current About identity matches installed gate");
        } finally { WriteIdentity(name); mods.Clear(); mods.AddRange(original); Set(typeof(ModsConfig),"activeModsHashSet",originalActive); }
    }
    static void Localization(RuntimeFixture f) {
        string[] keys = "TM_Grimoire TM_Learn TM_GrimoireDesc TM_GrimoireOpen TM_GrimoireTabHint TM_GrimoireIsekaiHint TM_GrimoireTabModeDesc TM_AIMarkingDesc TM_AIFighterMarkingDesc TM_AIFriendlyMarkingDesc XPMultiplierDesc NeedMultiplierDesc MagicyteChanceDesc TM_DeathRetaliationIsLethalDesc TM_DeathRetaliationChanceDesc TM_DeathRetaliationDelayDesc TM_enableAutocastDesc TM_autocastUndraftedThresholdDesc TM_autocastDraftedThresholdDesc TM_autocastEvaluationFrequencyDesc TM_autocastAnimalsDesc AICanCastDesc AIHardModeDesc TM_AggressiveAICastingDesc TM_SlavesCanScribeDesc TM_UndeadUpkeepMultiplierDesc TM_ParacyteSoftCapDesc TM_ParacyteMagesCountDesc unrestrictedBloodTypesForBloodMagicDesc TM_UnrestrictedWeaponCopyDesc TM_ClassOptionsDesc TM_FactionOptionsDesc TM_EventOptionsDesc TM_GolemOptionsDesc TM_DisplayOptionsDesc".Split(' ');
        var languages=Directory.GetDirectories(Path.Combine(f.Repository,"Languages")).ToDictionary(d => Path.GetFileName(d)!, d=>Directory.GetFiles(Path.Combine(d,"Keyed"),"*.xml").SelectMany(p=>XDocument.Load(p).Root!.Elements()).ToList());
        foreach(var language in languages) foreach(string key in keys) {
            var entries=language.Value.Where(x=>x.Name.LocalName==key).ToList(); Check.Equal(1,entries.Count,language.Key+" unique "+key);
            Check.Equal(true,!string.IsNullOrWhiteSpace(entries[0].Value),language.Key+" nonempty "+key);
            string Slots(string text)=>string.Join(",",Regex.Matches(text,@"(?<!\{)\{\d+(?:[^{}]*)\}(?!\})").Select(m=>m.Value).Order());
            Check.Equal(Slots(languages["English"].First(x=>x.Name.LocalName==key).Value),Slots(entries[0].Value),language.Key+" placeholders "+key);
        }
    }
}
