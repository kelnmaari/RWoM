using System.Reflection;
using System.Runtime.CompilerServices;
using AbilityUser;
using RimWorld;
using TorannMagic;
using TorannMagic.ClassProgression;
using Verse;
using Verse.AI;
using static RWoM.RegressionTests.GrimoireProgressionTests;
namespace RWoM.RegressionTests;

internal static partial class SpiritMapCollapseTests
{
    private sealed class TestMartialTend : HediffComp_TendDuration
    {
        public float RequestedQuality;
        public override void CompTended(float quality, float maxQuality, int batchPosition = 0)
        {
            RequestedQuality = quality;
            base.CompTended(quality, maxQuality, 1); // Skip only the headless text mote.
        }
    }

    private static DamageInfo? pommelHit;
    private static bool CapturePommel(Thing __instance, DamageInfo dinfo, ref DamageWorker.DamageResult __result)
    {
        pommelHit = dinfo;
        __result = new DamageWorker.DamageResult();
        return false;
    }
    private static IEnumerable<TestCase> HostedMartialCases()
    {
        yield return new("SpiritProjectedCast.Martial.Sniper.AllStockRoundsCarryOriginalOwner", f =>
        {
            var xml=System.Xml.Linq.XDocument.Load(Path.Combine(f.Repository,"v1.7/Defs/AbilityDefs/Abilities_TMagic_Sniper.xml"));
            var defs=xml.Root!.Elements().Where(d=>d.Element("defName")!=null).ToDictionary(d=>(string)d.Element("defName")!);
            var shots=defs.Values.Where(d=>(string?)d.Element("MainVerb")?.Element("verbClass")=="TorannMagic.Verb_SniperShot").ToList();
            Check.Equal(6,shots.Count,"verify new sniper families explicitly when adding another shot");
            var assembly=typeof(Verb_SniperShot).Assembly;
            var savedRound=assembly.GetType("TorannMagic.Projectile_SniperRound",true)!;
            foreach(var shot in shots)
            {
                var projectile=defs[(string)shot.Element("MainVerb")!.Element("defaultProjectile")!];
                var actual=assembly.GetType((string)projectile.Element("thingClass")!,true)!;
                Check.Equal(true,savedRound.IsAssignableFrom(actual),"registered shot preserves ownership: "+shot.Element("defName")!.Value);
            }
        });
        foreach (var (id, job) in new[] {
            ("TM_Headshot","TM_SpiritCastHeadshot"), ("TM_AntiArmor","TM_SpiritCastAntiArmor"),
            ("TM_DisablingShot","TM_SpiritCastDisablingShot"), ("TM_DisablingShot_I","TM_SpiritCastDisablingShot"),
            ("TM_DisablingShot_II","TM_SpiritCastDisablingShot"), ("TM_DisablingShot_III","TM_SpiritCastDisablingShot") })
        yield return new("SpiritProjectedCast.Martial.Sniper."+id, f =>
            SpiritRebirthRitualTransactionTests.Run(f, c => {
                using var possession = new Fixture(f,c);
                var lookup = Production("SpiritHostedMartial").GetMethod("Find", BindingFlags.Static | BindingFlags.NonPublic)!;
                var source = c.Original.GetCompAbilityUserMight();
                var def=(TMAbilityDef)typeof(TorannMagicDefOf).GetField(id)!.GetValue(null)!;
                var original = new MightAbility(source) { Def = def, CooldownTicksLeft = 87 };
                source.AbilityData.Powers.Add(original);
                source.AbilityData.AllPowers = null;
                object? Find() => lookup.Invoke(null,[c.Creator,c.Original,job]);
                Check.Same(original,Find()!,"retained shot resolves to the soul's exact runtime power");
                Check.Equal(87,original.CooldownTicksLeft,"looking up the command retains its cooldown");
                var host = c.Creator.GetCompAbilityUserMight();
                host.AbilityData.Powers.Add(new MightAbility(host) { Def = def });
                host.AbilityData.AllPowers = null;
                Check.Equal(true,Find()==null,"local shot takes precedence without adding a duplicate");
            }));

        foreach (string id in new[]{"TM_SniperAimedShot","TM_SniperArmorFracture"})
        yield return new("SpiritProjectedCast.Martial.Reforged."+id,f =>
            SpiritRebirthRitualTransactionTests.Run(f,c => {
                using var held=new Fixture(f,c);
                var def=DefDatabase<TMAbilityDef>.GetNamedSilentFail(id)??f.AddAbility(id);
                var source=c.Original.GetCompAbilityUserMight();
                var power=new MightAbility(source){Def=def};
                source.AbilityData.Powers.Add(power);source.AbilityData.AllPowers=null;
                string job=id=="TM_SniperAimedShot"?"TM_SpiritCastAimedShot":"TM_SpiritCastArmorFracture";
                const string file = "TM_SpiritSniperBallistics.xml";
                var xml = System.Xml.Linq.XDocument.Load(Path.Combine(f.Repository,"v1.7/Defs/JobDefs",file));
                var order = xml.Root!.Elements().Single(e => (string?)e.Element("defName") == job);
                Check.Equal(typeof(JobDriver_SpiritHostedMartial).FullName,(string?)order.Element("driverClass"),"order uses the targeted cast job");
                foreach (var language in new[] {"English","Russian","Spanish","Portuguese","PortugueseBrazilian","Ukrainian","ChineseSimplified"})
                {
                    var translations = System.Xml.Linq.XDocument.Load(Path.Combine(f.Repository,"v1.7/Languages",language,"DefInjected/JobDef",file));
                    Check.Equal(true,translations.Root!.Element(job+".reportString")?.Value.Contains("TargetB")==true,
                        "translated report names the actual target in "+language);
                }
                var find=Production("SpiritHostedMartial").GetMethod("Find",BindingFlags.Static|BindingFlags.NonPublic)!;
                Check.Same(power,find.Invoke(null,[c.Creator,c.Original,job])!,"borrowed weapon job resolves the exact discipline power");
            }));

        yield return new("SpiritProjectedCast.Martial.SniperOpening.UsesOriginalThreeShotJob", f =>
            SpiritRebirthRitualTransactionTests.Run(f,c => {
                using var held = new Fixture(f,c);
                const string id = "TM_SniperSingleOpening";
                var def = DefDatabase<TMAbilityDef>.GetNamedSilentFail(id) ?? f.AddAbility(id);
                var source = c.Original.GetCompAbilityUserMight();
                var power = new SniperOpeningAbility(source) { Def = def };
                source.AbilityData.Powers.Add(power);
                source.AbilityData.AllPowers = null;
                var find = Production("SpiritHostedMartial").GetMethod("Find",BindingFlags.Static|BindingFlags.NonPublic)!;
                Check.Same(power,find.Invoke(null,[c.Creator,c.Original,"TM_SniperSingleOpeningCast"])!,
                    "only the original's learned ascension provides the three-shot job");
            }));

        yield return new("SpiritProjectedCast.Martial.ShadowSlayer.UsesOriginalCloak", f =>
            SpiritRebirthRitualTransactionTests.Run(f,c => {
                using var held = new Fixture(f,c);
                var source = c.Original.GetCompAbilityUserMight();
                var power = new MightAbility(source) { Def=TorannMagicDefOf.TM_ShadowSlayer };
                source.AbilityData.Powers.Add(power); source.AbilityData.AllPowers=null;
                var find=Production("SpiritHostedMartial").GetMethod("Find",BindingFlags.Static|BindingFlags.NonPublic)!;
                Check.Same(power,find.Invoke(null,[c.Creator,c.Original,"TM_SpiritCastShadowSlayer"])!,
                    "the host can project the retained cloak without learning it itself");
            }));

        yield return new("SpiritProjectedCast.Martial.PommelStrike", f =>
            SpiritRebirthRitualTransactionTests.Run(f, c => {
                using var possession = new Fixture(f,c);
                var source = c.Original.GetCompAbilityUserMight();
                _ = source.MightData;
                var power = new MightAbility(source) { Def = TorannMagicDefOf.TM_PommelStrike };
                source.AbilityData.Powers.Add(power); source.AbilityData.AllPowers = null;
                var api = Production("SpiritHostedMartial");
                const BindingFlags methods = BindingFlags.Static | BindingFlags.NonPublic;
                Check.Same(power,api.GetMethod("Find",methods)!.Invoke(null,[c.Creator,c.Original,"TM_SpiritCastPommelStrike"])!,
                    "physical technique belongs to original with the exact learned ability");
                var target = PawnWithComps(false,false).pawn;
                Set(target.def.race,"fleshType",new FleshTypeDef { isOrganic = false });
                Check.Equal(false,(bool)api.GetMethod("CanStrike",methods)!.Invoke(null,[c.Creator,target,power])!,
                    "off-map enemy cannot be hit by retained skill");
                Check.Equal(false,(bool)api.GetMethod("CanStrike",methods)!.Invoke(null,[c.Creator,c.Creator,power])!,
                    "host cannot select itself to generate a hit");
                using var capture = new Patch(typeof(Thing).GetMethod(nameof(Thing.TakeDamage))!,nameof(CapturePommel));
                try
                {
                    pommelHit = null;
                    typeof(Verb_PommelStrike).GetMethod("Strike",methods)!.Invoke(null,[c.Creator,target,source]);
                    Check.Equal(true,pommelHit.HasValue,"one native damage packet");
                    Check.Same(c.Creator,pommelHit!.Value.Instigator!,"host body is the physical attacker");
                    Check.Same(DamageDefOf.Stun,pommelHit.Value.Def,"native nonlethal attack");
                    Check.Equal(10f,pommelHit.Value.Amount,"native stun strength");
                }
                finally { pommelHit=null; }
            }));
        yield return new("SpiritProjectedCast.Martial.RetainedSelfAidAtZero", f =>
            SpiritRebirthRitualTransactionTests.Run(f,c => {
                using var possession = new Fixture(f,c);
                var might = c.Original.GetCompAbilityUserMight();
                var def = TorannMagicDefOf.TM_FirstAid;
                var row = might.MightData.MightPowersSS.Single(p=>p.TMabilityDefs.Contains(def));
                var priorClass = def.abilityClass;
                try
                {
                    def.abilityClass = typeof(MightAbility);
                    row.learned = true;
                    var original = new MightAbility(might) {Def=def};
                    might.AbilityData.Powers.Add(original);
                    var clock = (IExposable)Activator.CreateInstance(Production("SpiritRuntimeClock"))!;
                    const BindingFlags methods = BindingFlags.Instance | BindingFlags.NonPublic;
                    clock.GetType().GetMethod("CaptureCooldowns",methods)!.Invoke(clock,[might]);
                    string file=Path.Combine(f.WorkingDirectory,"retained-self-aid.xml");
                    try { Scribe.saver.InitSaving(file,"test"); Scribe_Deep.Look(ref clock,"clock"); Scribe.saver.FinalizeSaving(); }
                    finally { Scribe.ForceStop(); }
                    might.AbilityData.Powers.Remove(original);
                    foreach(bool learned in new[]{false,true})
                    {
                        row.learned = learned;
                        clock=null!;
                        try { Scribe.loader.InitLoading(file); Scribe_Deep.Look(ref clock,"clock"); Scribe.loader.FinalizeLoading(); }
                        finally { Scribe.ForceStop(); }
                        clock.GetType().GetMethod("RestoreCooldowns",methods)!.Invoke(clock,[might]);
                        Check.Equal(learned,might.AbilityData.Powers.Any(p=>p.Def==def),"only known and still learned techniques return after a zero-cooldown save");
                    }
                }
                finally { def.abilityClass = priorClass; }
            }));
        yield return new("SpiritProjectedCast.Martial.CooldownsBothOwners", f =>
            SpiritRebirthRitualTransactionTests.Run(f, c => {
                using var possession = new Fixture(f,c);
                foreach (bool magic in new[] {true,false})
                {
                    CompAbilityUser owner = magic ? c.Original.GetCompAbilityUserMagic() : c.Original.GetCompAbilityUserMight();
                    var def = magic ? TorannMagicDefOf.TM_Shield : TorannMagicDefOf.TM_FirstAid;
                    var original = magic ? (PawnAbility)new MagicAbility(owner) : new MightAbility(owner);
                    original.Def = def; original.CooldownTicksLeft = 731;
                    owner.AbilityData.Powers.Add(original);
                    var clock = (IExposable)Activator.CreateInstance(Production("SpiritRuntimeClock"))!;
                    var capture = clock.GetType().GetMethod("CaptureCooldowns", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    capture.Invoke(clock,[owner]);
                    string path = Path.Combine(f.WorkingDirectory, magic ? "cooldown-magic.xml" : "cooldown-might.xml");
                    void RoundTrip()
                    {
                        try { Scribe.saver.InitSaving(path,"test"); Scribe_Deep.Look(ref clock,"clock"); Scribe.saver.FinalizeSaving(); }
                        finally { Scribe.ForceStop(); }
                        clock = null!;
                        try { Scribe.loader.InitLoading(path); Scribe_Deep.Look(ref clock,"clock"); Scribe.loader.FinalizeLoading(); }
                        finally { Scribe.ForceStop(); }
                    }
                    RoundTrip();
                    owner.AbilityData.Powers.Remove(original);
                    bool beta = ClassProgressionFeature.Enabled;
                    try
                    {
                        ClassProgressionFeature.Enabled = false;
                        Set(c.Creator.AllComps.OfType<CompClassProgression>().Single(),"state",new ClassProgressionState(Vocation.Warrior));
                        Check.Equal(true, (bool)clock.GetType().GetMethod("TracksCooldowns",BindingFlags.Static|BindingFlags.NonPublic)!
                            .Invoke(null,[c.Creator])!, "ordinary enrolled pawn retains its receipt while beta is off");
                        clock.GetType().GetMethod("RestoreCooldowns",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(clock,[owner]);
                        Check.Equal(false,owner.AbilityData.Powers.Any(p=>p.Def==def),"no grant while beta is disabled");
                        capture.Invoke(clock,[owner]);
                        RoundTrip();
                    }
                    finally { ClassProgressionFeature.Enabled = beta; }
                    var restored = magic ? (PawnAbility)new MagicAbility(owner) : new MightAbility(owner);
                    restored.Def = def; owner.AbilityData.Powers.Add(restored);
                    var restore = clock.GetType().GetMethod("RestoreCooldowns", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    restore.Invoke(clock,[owner]);
                    Check.Equal(731, restored.CooldownTicksLeft, "Scribe restores actual rank on reconstructed original");
                    restored.CooldownTicksLeft = 730;
                    restore.Invoke(clock,[owner]);
                    Check.Equal(730, restored.CooldownTicksLeft, "restoration is consumed and cannot reset progress");
                }
            }));
        foreach (bool aid in new[] { false, true })
            yield return new("SpiritProjectedCast.Martial." + (aid ? "SuperSoldierSelfAid" : "GladiatorManeuver"),
                f => SpiritRebirthRitualTransactionTests.Run(f, c => {
                    using var possession = new Fixture(f, c);
                    var soul = c.Original;
                    var body = c.Creator;
                    var hediffs = DefDatabase<HediffDef>.AllDefsListForReading.ToArray();
                    var abilities = DefDatabase<TMAbilityDef>.AllDefsListForReading.ToArray();
                    var priorAid = TorannMagicDefOf.TM_FirstAid;
                    var events = DefDatabase<HistoryEventDef>.AllDefsListForReading.ToArray();
                    var oldEvent = TorannMagicDefOf.TM_UsedManeuver;
                    var settings = TorannMagic.ModOptions.Settings.Instance;
                    try
                    {
                        TorannMagic.ModOptions.Settings.Instance = new TorannMagic.ModOptions.Settings();
                        var state = new ClassProgressionState(Vocation.Warrior);
                        state.GetType().GetMethod("ChooseMartialFoundation", BindingFlags.NonPublic | BindingFlags.Instance)!
                            .Invoke(state, ["rwom.gladiator", true]);
                        Set(ClassIdentity.Component(soul), "state", state);
                        var def = (TMAbilityDef)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!
                            .Invoke(TorannMagicDefOf.TM_Disguise, null)!;
                        def.defName = aid ? "TM_FirstAid" : "TM_GladiatorManeuver";
                        def.consumeEnergy = true;
                        def.staminaCost = aid ? .4f : .12f;
                        def.MainVerb = new VerbProperties_Ability { warmupTime = 1, SecondsToRecharge = 25 };
                        DefDatabase<TMAbilityDef>.Clear();
                        DefDatabase<TMAbilityDef>.Add(def);
                        if (aid) TorannMagicDefOf.TM_FirstAid = def;
                        var effect = new HediffDef { defName = "TM_GladiatorManeuverHD", hediffClass = typeof(HediffWithComps),
                            comps = [new HediffCompProperties { compClass = typeof(HediffComp_SpiritCastOrigin) }] };
                        DefDatabase<HediffDef>.Add(effect);
                        if (DefDatabase<HediffDef>.GetNamedSilentFail("TM_SyrriumSenseHD") == null)
                            DefDatabase<HediffDef>.Add(new HediffDef { defName = "TM_SyrriumSenseHD" });
                        TorannMagicDefOf.TM_UsedManeuver = new HistoryEventDef { defName = "HostedMartialUse" };
                        DefDatabase<HistoryEventDef>.Add(TorannMagicDefOf.TM_UsedManeuver);
                        var history = (History)RuntimeHelpers.GetUninitializedObject(typeof(History));
                        history.historyEventsManager = new HistoryEventsManager();
                        Set(Current.Game, "history", history);

                        using var notify = new Patch(typeof(IdeoUtility).GetMethod(nameof(IdeoUtility.Notify_HistoryEvent))!, nameof(CancelNative));
                        using var physiology = new Patch(typeof(Pawn_HealthTracker).GetMethod("CheckForStateChange", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!, nameof(CancelNative));
                        var source = soul.GetCompAbilityUserMight();
                        soul.ageTracker.AgeBiologicalTicks = 25L * GenDate.TicksPerYear;
                        source.maxSP = 1;
                        _ = source.MightData;
                        if (source.Stamina == null) soul.needs.AllNeeds.Add(new Need_Stamina(soul) { def = TorannMagicDefOf.TM_Stamina });
                        var ability = new MightAbility(source) { Def = def };
                        source.AbilityData.Powers.Add(ability);
                        source.AbilityData.AllPowers = null;
                        var api = Production("SpiritHostedMartial");
                        var name = aid ? "TM_SpiritCastFirstAid" : "TM_SpiritCastManeuver";
                        object? Call(string method, params object[] args) =>
                            api.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
                        Check.Same(ability, Call("Find", body, soul, name)!, "soul owns original power");
                        var local = body.GetCompAbilityUserMagic();
                        var overlap = new PawnAbility(local) { Def = def };
                        local.AbilityData.Powers.Add(overlap); local.AbilityData.AllPowers = null;
                        Check.Equal(true, Call("Find", body, soul, name) == null, "other host component blocks duplicate");
                        local.AbilityData.Powers.Remove(overlap); local.AbilityData.AllPowers = null;
                        source.Stamina.CurLevel = .9f;
                        var wounds = new List<Hediff_Injury>();
                        if (aid)
                        {
                            Check.Equal(false, (bool)Call("Apply", body, soul, name)!, "no wound cannot consume");
                            Set(body.def.race, "fleshType", new FleshTypeDef { isOrganic = true });
                            Set(body.def.race, "bloodDef", body.def);
                            body.def.race.bleedRateFactor = 1f;
                            var skills = source.MightData.MightPowerSkill_FirstAid;
                            skills.Single(s => s.label == "TM_FirstAid_pwr").level = 1;
                            skills.Single(s => s.label == "TM_FirstAid_ver").level = 2;
                            for (int i = 0; i < 4; i++)
                            {
                                var wound = new Hediff_Injury { pawn = body, Part = body.RaceProps.body.corePart,
                                    def = new HediffDef { defName = "MartialCut", injuryProps = new InjuryProps(), tendable = true } };
                                Set(wound, "severityInt", 2f);
                                wound.comps = [new TestMartialTend { parent = wound, props = new HediffCompProperties_TendDuration() }];
                                body.health.hediffSet.hediffs.Add(wound);
                                wounds.Add(wound);
                            }
                        }
                        Set(ClassIdentity.Component(soul).SpiritRebirth, "depletionPending", true);
                        Check.Equal(false, (bool)Call("Apply", body, soul, name)!, "pending collapse rejects new actions");
                        Set(ClassIdentity.Component(soul).SpiritRebirth, "depletionPending", false);
                        source.Stamina.CurLevel = 0;
                        Check.Equal(false, (bool)Call("Apply", body, soul, name)!, "no credit for an unaffordable action");
                        source.Stamina.CurLevel = .9f;
                        float before = source.Stamina.CurLevel, price = source.ActualStaminaCost(def);
                        Check.Equal(true, (bool)Call("Ready", body, ability, name)!, "body can perform learned action");
                        var job = JobMaker.MakeJob(new JobDef { defName = name }, soul);
                        var driver = new JobDriver_SpiritHostedMartial { pawn = body, job = job };
                        Set(body.jobs, "curJob", job); Set(body.jobs, "curDriver", driver);
                        var toils = ((IEnumerable<Toil>)driver.GetType().GetMethod("MakeNewToils", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(driver, null)!).ToArray();
                        toils[0].initAction();
                        Check.Equal(before, source.Stamina.CurLevel, "no payment during warmup");
                        toils[1].initAction();
                        Check.Equal(true, price > 0 && Math.Abs(before - price - source.Stamina.CurLevel) < .0001f, "original owner pays once");
                        Check.Equal(true, ability.CooldownTicksLeft > 0, "original cooldown");
                        Check.Equal(false, (bool)Call("Apply", body, soul, name)!, "repeat cannot spend");
                        var file = Path.Combine(f.WorkingDirectory, "martial-driver.xml");
                        try
                        {
                            Scribe.saver.InitSaving(file, "test");
                            Scribe_Deep.Look(ref driver, "driver");
                            Scribe.saver.FinalizeSaving();
                        }
                        finally { Scribe.ForceStop(); }
                        driver = null!;
                        try
                        {
                            Scribe.loader.InitLoading(file);
                            Scribe_Deep.Look(ref driver, "driver");
                            driver.pawn = body; driver.job = job;
                            var directory = (LoadedObjectDirectory)typeof(CrossRefHandler)
                                .GetField("loadedObjectDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Scribe.loader.crossRefs)!;
                            directory.RegisterLoaded(body); directory.RegisterLoaded(soul);
                            Scribe.loader.FinalizeLoading();
                        }
                        finally { Scribe.ForceStop(); }
                        Check.Equal(true, (bool)typeof(JobDriver_SpiritHostedMartial).GetField("attempted",
                            BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(driver)!, "loaded driver retains one-shot receipt");
                        if (aid)
                        {
                            Check.Equal(3, wounds.Count(w => w.TryGetComp<HediffComp_TendDuration>().tendTicksLeft > 0), "source rank affects body wound count");
                            Check.Equal(true, wounds.Take(3).All(w => w.TryGetComp<TestMartialTend>().RequestedQuality >= .2f), "source versatility affects tend quality");
                        }
                        else
                        {
                            var applied = body.health.hediffSet.GetFirstHediffOfDef(effect);
                            Check.NotNull(applied, "effect on body");
                            Check.Equal(false, soul.health.hediffSet.HasHediff(effect), "soul never wears effect");
                            var origin = applied.TryGetComp<HediffComp_SpiritCastOrigin>();
                            Check.Equal(false, origin.CompShouldRemove, "active possession");
                            var localState = new ClassProgressionState(Vocation.Warrior);
                            localState.GetType().GetMethod("ChooseMartialFoundation", BindingFlags.NonPublic | BindingFlags.Instance)!
                                .Invoke(localState, ["rwom.gladiator", true]);
                            Set(ClassIdentity.Component(body), "state", localState);
                            var native = new Verb_GladiatorManeuver { caster = body };
                            Set(native, "currentTarget", new LocalTargetInfo(body));
                            Check.Equal(true, (bool)native.GetType().GetMethod("TryCastShot", BindingFlags.NonPublic | BindingFlags.Instance)!
                                .Invoke(native, null)!, "native recast succeeds while projected effect still exists");
                            var own = body.health.hediffSet.GetFirstHediffOfDef(effect);
                            Check.Equal(false, ReferenceEquals(applied, own), "native maneuver has independent owner and duration");
                            ability.CooldownTicksLeft = 0;
                            Check.Equal(false, (bool)Call("Apply", body, soul, name)!, "cannot overwrite host's active native maneuver");
                            body.health.SetDead();
                            Check.Equal(false, own.TryGetComp<HediffComp_SpiritCastOrigin>().CompShouldRemove, "native maneuver is unbound");
                            Check.Equal(true, origin.CompShouldRemove, "effect ends on host death");
                        }
                    }
                    finally
                    {
                        DefDatabase<HistoryEventDef>.Clear(); foreach (var d in events) DefDatabase<HistoryEventDef>.Add(d);
                        TorannMagicDefOf.TM_FirstAid = priorAid;
                        TorannMagicDefOf.TM_UsedManeuver = oldEvent;
                        TorannMagic.ModOptions.Settings.Instance = settings;
                        DefDatabase<TMAbilityDef>.Clear(); foreach (var d in abilities) DefDatabase<TMAbilityDef>.Add(d);
                        DefDatabase<HediffDef>.Clear(); foreach (var d in hediffs) DefDatabase<HediffDef>.Add(d);
                    }
                }));
    }
}
