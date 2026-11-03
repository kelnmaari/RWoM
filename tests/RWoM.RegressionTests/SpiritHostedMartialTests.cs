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

        yield return new("SpiritProjectedCast.Martial.GladiatorSweep.UsesLearnedSource", f =>
            SpiritRebirthRitualTransactionTests.Run(f,c => {
                using var held = new Fixture(f,c);
                var def=f.AddAbility("TM_GladiatorSweep");
                var source=c.Original.GetCompAbilityUserMight();
                var power=new MightAbility(source){Def=def};
                source.AbilityData.Powers.Add(power); source.AbilityData.AllPowers=null;
                var find=Production("SpiritHostedMartial").GetMethod("Find",BindingFlags.Static|BindingFlags.NonPublic)!;
                Check.Same(power,find.Invoke(null,[c.Creator,c.Original,"TM_SpiritCastGladiatorSweep"])!,
                    "the original learned Reforged technique provides the host's melee order");
            }));

        yield return new("GladiatorWhirlwind.AirbornePawnHasPhysicalOwner", f =>
        {
            Check.Equal(true, typeof(IThingHolder).IsAssignableFrom(typeof(FlyingObject_Whirlwind)),
                "an airborne pawn and the pawn's inventory must remain reachable from the map's holder hierarchy");
        });
        foreach(var (defName,order) in new[] {("TM_BladeSpin","TM_SpiritCastBladeSpin"),("TM_SeismicSlash","TM_SpiritCastSeismicSlash"),("TM_BladePassage","TM_SpiritCastBladePassage")})
            yield return new("BladeProjection.OriginalOwner."+defName,f =>
                SpiritRebirthRitualTransactionTests.Run(f,c => {
                    using var held=new Fixture(f,c);
                    var def=typeof(TorannMagicDefOf).GetField(defName)?.GetValue(null) as TMAbilityDef ??
                        DefDatabase<TMAbilityDef>.GetNamedSilentFail(defName) ?? f.AddAbility(defName);
                    var source=c.Original.GetCompAbilityUserMight();
                    var original=new MightAbility(source){Def=def};source.AbilityData.Powers.Add(original);source.AbilityData.AllPowers=null;
                    var find=Production("SpiritHostedMartial").GetMethod("Find",BindingFlags.NonPublic|BindingFlags.Static)!;
                    object? Power()=>find.Invoke(null,[c.Creator,c.Original,order]);
                    Check.Same(original,Power()!,"project the exact learned blade technique");
                    var local=c.Creator.GetCompAbilityUserMight();local.AbilityData.Powers.Add(new MightAbility(local){Def=def});local.AbilityData.AllPowers=null;
                    Check.Equal(true,Power()==null,"the host's own technique has priority");
                }));
        foreach(string id in new[]{"TM_ArrowStorm","TM_ArrowStorm_I","TM_ArrowStorm_II","TM_ArrowStorm_III"})
            yield return new("RangerProjection.VolleyOwner."+id,f=>
                SpiritRebirthRitualTransactionTests.Run(f,c=>{
                    using var held=new Fixture(f,c);
                    var source=c.Original.GetCompAbilityUserMight();
                    var def=(TMAbilityDef)typeof(TorannMagicDefOf).GetField(id)!.GetValue(null)!;
                    var power=new MightAbility(source){Def=def,CooldownTicksLeft=31};
                    source.AbilityData.Powers.Add(power);source.AbilityData.AllPowers=null;
                    var find=Production("SpiritHostedMartial").GetMethod("Find",BindingFlags.Static|BindingFlags.NonPublic)!;
                    object? Owner()=>find.Invoke(null,[c.Creator,c.Original,"TM_SpiritArrowStorm"]);
                    Check.Same(power,Owner()!,"volley order must use the original exact rank");
                    var host=c.Creator.GetCompAbilityUserMight();host.AbilityData.Powers.Add(new MightAbility(host){Def=TorannMagicDefOf.TM_ArrowStorm});
                    host.AbilityData.AllPowers=null;
                    Check.Equal(true,Owner()==null,"a lower native rank takes priority");
                }));
        yield return new("FacelessProjection.ReversalDecayAfterReload", f =>
            SpiritRebirthRitualTransactionTests.Run(f,c => {
                using var held=new Fixture(f,c);
                var hediff=new HediffWithComps {pawn=c.Creator};
                HediffComp Make()
                {
                    var tracker=(HediffComp)Activator.CreateInstance(typeof(MightAbility).Assembly.GetType("TorannMagic.HediffComp_Reversal",true)!,true)!;
                    tracker.parent=hediff;
                    return tracker;
                }
                var effect=Make();
                float delta=0;
                for(int i=0;i<58;i++)effect.CompPostTick(ref delta);
                Check.Equal(0f,delta,"no decay before sixty ticks");
                var file=Path.Combine(f.WorkingDirectory,"faceless-reversal-phase.xml");
                try { Scribe.saver.InitSaving(file,"test");effect.CompExposeData();Scribe.saver.FinalizeSaving(); }
                finally { Scribe.ForceStop(); }
                var loaded=Make();
                try { Scribe.loader.InitLoading(file);loaded.CompExposeData();Scribe.loader.FinalizeLoading(); }
                finally { Scribe.ForceStop(); }
                float after=0;
                for(int i=0;i<3;i++){effect.CompPostTick(ref delta);loaded.CompPostTick(ref after);}
                Check.Equal(-1f,delta,"ordinary decay after sixty ticks");
                Check.Equal(delta,after,"loaded shield decays on the same tick");
            }));
        yield return new("FacelessProjection.StudyOwner", f =>
            SpiritRebirthRitualTransactionTests.Run(f,c => {
                using var held=new Fixture(f,c);
                var state=new ClassProgressionState(Vocation.Warrior);
                Set(ClassIdentity.Component(c.Original),"state",state);
                state.TryAddFoundation("rwom.faceless",DisciplineDomain.Might);
                using var defs=FacelessCareerTests.Setup(f);
                var source=c.Original.GetCompAbilityUserMight();
                var power=new MightAbility(source){Def=TorannMagicDefOf.TM_Mimic};
                source.AbilityData.Powers.Add(power);source.AbilityData.AllPowers=null;
                var find=Production("SpiritHostedMartial").GetMethod("Find",BindingFlags.Static|BindingFlags.NonPublic)!;
                Check.Same(power,find.Invoke(null,[c.Creator,c.Original,"TM_SpiritStudyFaceless"])!,"projected study must use the original Mimic");
                var model=PawnWithComps(true,false);
                Set(model.pawn,"mapIndexOrState",(sbyte)0);Set(model.pawn,"positionInt",c.Creator.Position+IntVec3.East);
                model.magic.AddPawnAbility(TorannMagicDefOf.TM_Firebolt);
                var learn=Production("FacelessImitation").GetMethod("LearnFrom",BindingFlags.Static|BindingFlags.NonPublic)!;
                Check.Equal(true,(bool)learn.Invoke(null,[c.Creator,c.Original,model.pawn])!,"learn from the carrier's map");
                var echo=DefDatabase<TMAbilityDef>.GetNamed("TM_FacelessEcho");
                Check.Equal(1,source.AbilityData.Powers.Count(p=>p.Def==echo),"soul owns echo");
                Check.Equal(0,c.Creator.GetCompAbilityUserMight().AbilityData.Powers.Count(p=>p.Def==echo),"host does not acquire echo");
                Check.Equal(false,(bool)learn.Invoke(null,[c.Creator,c.Original,model.pawn])!,"same family cannot refresh the echo");
            }));
        yield return new("FacelessProjection.RescueCredit", f =>
            SpiritRebirthRitualTransactionTests.Run(f,c => {
                using var held=new Fixture(f,c);
                DisciplineProgress Enroll(Pawn pawn)
                {
                    var state=new ClassProgressionState(Vocation.Warrior);
                    Set(ClassIdentity.Component(pawn),"state",state);
                    state.TryAddFoundation("rwom.faceless",DisciplineDomain.Might);
                    return state.Find("rwom.faceless")!;
                }
                var soul=Enroll(c.Original);
                var rescue=Production("FacelessMartialPath").GetMethod("Rescue",BindingFlags.Static|BindingFlags.NonPublic)!;
                var patients=Enumerable.Range(0,6).Select(i=>{
                    var patient=PawnWithComps(false,false).pawn;
                    patient.thingIDNumber=47120+i;
                    return patient;
                }).ToArray();
                void Credit(Pawn p)=>rescue.Invoke(null,[c.Creator,p]);
                bool Complete(DisciplineProgress path)=>path.CompletedTrials.Contains("rwom.faceless.Foundation");
                Credit(patients[0]);Credit(patients[0]);Credit(patients[1]);
                Check.Equal(false,Complete(soul),"same rescued pawn counts only once");
                Credit(patients[2]);
                Check.Equal(true,Complete(soul),"third distinct rescue by carrier advances original Faceless");
                var host=Enroll(c.Creator);
                foreach(var patient in patients.Skip(3))Credit(patient);
                Check.Equal(true,Complete(host),"host's own Faceless path takes precedence");
            }));
        yield return new("RangerProjection.BreakthroughOrder",f=>
            SpiritRebirthRitualTransactionTests.Run(f,c=>{
                using var held=new Fixture(f,c);
                var source=c.Original.GetCompAbilityUserMight();
                var def=DefDatabase<TMAbilityDef>.GetNamedSilentFail("TM_RangerBreakthrough") ?? f.AddAbility("TM_RangerBreakthrough");
                if(DefDatabase<JobDef>.GetNamedSilentFail("TM_RangerBreakthroughCast")==null)
                    DefDatabase<JobDef>.Add(new JobDef{defName="TM_RangerBreakthroughCast",driverClass=typeof(JobDriver_RangerBreakthrough)});
                var ability=new RangerHuntAbility(source){Def=def};
                source.AbilityData.Powers.Add(ability);source.AbilityData.AllPowers=null;
                var find=Production("SpiritHostedMartial").GetMethod("Find",BindingFlags.Static|BindingFlags.NonPublic)!;
                Check.Same(ability,find.Invoke(null,[c.Creator,c.Original,"TM_RangerBreakthroughCast"])!,"projected hunt belongs to the ranger");
                var job=(Verse.AI.Job)typeof(RangerHuntAbility).GetMethod("MakeJobFor",BindingFlags.Instance|BindingFlags.NonPublic)!
                    .Invoke(ability,[c.Creator,new LocalTargetInfo(c.Creator)])!;
                Check.Same(c.Original,job.targetQueueB.Single().Pawn,"saved order identifies original ranger");
                var host=c.Creator.GetCompAbilityUserMight();
                host.AbilityData.Powers.Add(new RangerHuntAbility(host){Def=def});host.AbilityData.AllPowers=null;
                Check.Equal(true,find.Invoke(null,[c.Creator,c.Original,"TM_RangerBreakthroughCast"])==null,"native hunt has precedence");
            }));
        yield return new("RangerProjection.ArrowStormNoBow", f =>
        {
            var pawn=PawnWithComps(false,false).pawn;
            pawn.equipment=new Pawn_EquipmentTracker(pawn);
            Check.Equal(0f,Projectile_ArrowStorm.GetWeaponAccuracy(pawn),"no bow cannot supply arrow accuracy");
            Check.Equal(0,Projectile_ArrowStorm.GetWeaponDmg(pawn),"no bow cannot supply arrow damage");
        });
        yield return new("RangerProjection.SafeTrailAfterLoad", f =>
            SpiritRebirthRitualTransactionTests.Run(f,c => {
                using var held=new Fixture(f,c);
                var state=new ClassProgressionState(Vocation.Warrior);
                Set(ClassIdentity.Component(c.Original),"state",state);
                state.TryAddFoundation("rwom.ranger",DisciplineDomain.Might);
                var path=state.Find("rwom.ranger");Set(path,"stage",InitiationStage.Master);
                path.RecordCompletedTrial("rwom.ranger.routes");
                var might=c.Original.GetCompAbilityUserMight();
                var def=DefDatabase<TMAbilityDef>.GetNamedSilentFail("TM_RangerSafeTrail")??f.AddAbility("TM_RangerSafeTrail");
                var power=new MightAbility(might){Def=def};might.AbilityData.Powers.Add(power);might.AbilityData.AllPowers=null;
                var foe=new Faction{def=new FactionDef()};
                Set(foe,"relations",new List<FactionRelation>{new(){other=c.Creator.Faction,kind=FactionRelationKind.Hostile}});
                var rel=typeof(Faction).GetField("relations",BindingFlags.Instance|BindingFlags.NonPublic)!;
                var previous=rel.GetValue(c.Creator.Faction);
                var pos=c.Creator.Position;
                Set(c.Creator.Faction,"relations",new List<FactionRelation>{new(){other=foe,kind=FactionRelationKind.Hostile}});
                try
                {
                    Pawn Traveler(int id,Faction faction)
                    {
                        var pawn=PawnWithComps(false,false).pawn;
                        pawn.kindDef=new PawnKindDef{race=pawn.def};pawn.def.category=ThingCategory.Pawn;pawn.def.size=new IntVec2(1,1);
                        pawn.mindState=new Pawn_MindState(pawn);pawn.guest=new Pawn_GuestTracker(pawn);pawn.thingIDNumber=id;
                        Set(pawn,"mapIndexOrState",(sbyte)0);Set(pawn,"factionInt",faction);
                        return pawn;
                    }
                    Set(c.Creator,"positionInt",new IntVec3(7,0,7));
                    RangerTrailTests.Trail(f,c.Creator,Traveler(21924+1,c.Creator.Faction),Traveler(21926,foe),power);
                }
                finally { rel.SetValue(c.Creator.Faction,previous);Set(c.Creator,"positionInt",pos); }
            }));
        foreach(bool trails in new[]{false,true})
            yield return new(trails?"RangerProjection.OutdoorHaul":"RangerProjection.ClassicTraining", f =>
                SpiritRebirthRitualTransactionTests.Run(f,c => {
                    using var held=new Fixture(f,c);
                    var state=new ClassProgressionState(Vocation.Warrior);
                    Set(ClassIdentity.Component(c.Original),"state",state);
                    state.TryAddFoundation("rwom.ranger",DisciplineDomain.Might);
                    Set(state.Find("rwom.ranger"),"RangerTrailLore",trails);
                    var source=c.Original.GetCompAbilityUserMight();
                    source.MightData.MightPowerSkill_RangerTraining.Single().levelMax=trails?4:3;
                    var original=TorannMagicDefOf.Ranger;
                    TorannMagicDefOf.Ranger=new TraitDef{defName="Ranger"};
                    try { RangerFoundationTests.VerifyPassive(f,c.Creator,source,trails); }
                    finally { TorannMagicDefOf.Ranger=original; }
                }));
        foreach (bool treatment in new[]{false,true})
            yield return new("RangerProjection."+ (treatment?"VeterinaryShift":"StockpileFire"), f =>
                SpiritRebirthRitualTransactionTests.Run(f,c => {
                    using var held=new Fixture(f,c);
                    var state=new ClassProgressionState(Vocation.Warrior);
                    Set(ClassIdentity.Component(c.Original),"state",state);
                    state.TryAddFoundation("rwom.ranger",DisciplineDomain.Might);
                    Set(state.Find("rwom.ranger"),"stage",treatment?InitiationStage.Master:InitiationStage.Adept);
                    if(treatment)RangerAnimalCareTrialTests.Verify(c.Creator,c.Original);
                    else RangerFireTrialTests.Verify(f,c.Creator,c.Original);
                }));
        yield return new("RangerProjection.RetainedTrapPlanter", f =>
            SpiritRebirthRitualTransactionTests.Run(f,c => {
                using var held=new Fixture(f,c);
                var state=new ClassProgressionState(Vocation.Warrior);
                Set(ClassIdentity.Component(c.Original),"state",state);
                state.TryAddFoundation("rwom.ranger",DisciplineDomain.Might);
                Set(state.Find("rwom.ranger"),"stage",InitiationStage.Adept);
                var foe=new Faction{def=new FactionDef()};
                Set(foe,"relations",new List<FactionRelation>{new(){other=c.Creator.Faction,kind=FactionRelationKind.Hostile}});
                var old=typeof(Faction).GetField("relations",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(c.Creator.Faction);
                Set(c.Creator.Faction,"relations",new List<FactionRelation>{new(){other=foe,kind=FactionRelationKind.Hostile}});
                try
                {
                    var victim=PawnWithComps(false,false).pawn;
                    victim.kindDef=new PawnKindDef{race=victim.def};victim.mindState=new Pawn_MindState(victim);victim.guest=new Pawn_GuestTracker(victim);
                    Set(victim,"mapIndexOrState",(sbyte)0);Set(victim,"factionInt",foe);
                    RangerTrailTests.Trap(c.Creator,victim,c.Original.GetCompAbilityUserMight());
                }
                finally { Set(c.Creator.Faction,"relations",old!); }
            }));
        yield return new("RangerProjection.PoisonTrapOrder", f =>
            SpiritRebirthRitualTransactionTests.Run(f,c => {
                using var held=new Fixture(f,c);
                var comp=c.Original.GetCompAbilityUserMight();
                var power=new MightAbility(comp){Def=TorannMagicDefOf.TM_PoisonTrap,CooldownTicksLeft=45};
                comp.AbilityData.Powers.Add(power);comp.AbilityData.AllPowers=null;
                const string job="TM_SpiritCastPoisonTrap";
                var find=Production("SpiritHostedMartial").GetMethod("Find",BindingFlags.NonPublic|BindingFlags.Static)!;
                Check.Same(power,find.Invoke(null,[c.Creator,c.Original,job])!,"learned trap gets an order bound to the original");
                Check.Equal(45,power.CooldownTicksLeft,"no payment when displaying the trap");
            }));
        yield return new("RangerProjection.AnimalFriendOwner", f =>
            SpiritRebirthRitualTransactionTests.Run(f,c => {
                using var held=new Fixture(f,c);
                const string job="TM_SpiritCastAnimalFriend";
                var def=TorannMagicDefOf.TM_AnimalFriend ?? f.AddAbility("TM_AnimalFriend");
                var source=c.Original.GetCompAbilityUserMight();
                var power=new MightAbility(source){Def=def,CooldownTicksLeft=73};
                source.AbilityData.Powers.Add(power);source.AbilityData.AllPowers=null;
                var find=Production("SpiritHostedMartial").GetMethod("Find",BindingFlags.NonPublic|BindingFlags.Static)!;
                object? FindPower()=>find.Invoke(null,[c.Creator,c.Original,job]);
                Check.Same(power,FindPower()!,"original's bond ability supplies the command");
                Check.Equal(73,power.CooldownTicksLeft,"command enumeration preserves cooldown");
                const string file="TM_SpiritRanger.xml";
                var jobs=System.Xml.Linq.XDocument.Load(Path.Combine(f.Repository,"v1.7/Defs/JobDefs",file));
                var order=jobs.Root!.Elements().Single(node=>(string?)node.Element("defName")==job);
                Check.Equal(typeof(JobDriver_SpiritHostedMartial).FullName,(string?)order.Element("driverClass"),"bond order uses the saved job");
                foreach(var language in new[]{"English","Russian","Spanish","Portuguese","PortugueseBrazilian","Ukrainian","ChineseSimplified"})
                {
                    var strings=System.Xml.Linq.XDocument.Load(Path.Combine(f.Repository,"v1.7/Languages",language,"DefInjected/JobDef",file));
                    Check.Equal(true,strings.Root!.Element(job+".reportString")?.Value.Contains("TargetB")==true,"animal remains named in "+language);
                }
                var host=c.Creator.GetCompAbilityUserMight();
                host.AbilityData.Powers.Add(new MightAbility(host){Def=def});host.AbilityData.AllPowers=null;
                Check.Equal(true,FindPower()==null,"host's own bond takes precedence");
            }));
        yield return new("RangerProjection.CareFromHost", f =>
            SpiritRebirthRitualTransactionTests.Run(f,c => {
                using var held=new Fixture(f,c);
                var state=new ClassProgressionState(Vocation.WarriorMage);
                Set(ClassIdentity.Component(c.Original),"state",state);
                state.TryAddFoundation("rwom.ranger",DisciplineDomain.Might);
                var might=c.Original.GetCompAbilityUserMight();
                var previous=TorannMagicDefOf.TM_RangerBondHD;
                try
                {
                    var pet=PawnWithComps(false,false).pawn;
                    pet.thingIDNumber=78124;
                    pet.def=(ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
                    pet.def.category=ThingCategory.Pawn; pet.def.size=new IntVec2(1,1);
                    pet.def.race=new RaceProperties {intelligence=Intelligence.Animal};
                    pet.kindDef=new PawnKindDef {race=pet.def};
                    Set(pet.def.race,"fleshType",new FleshTypeDef {isOrganic=true});
                    Set(pet,"mapIndexOrState",(sbyte)0); Set(pet,"positionInt",c.Creator.Position);
                    Set(pet,"factionInt",c.Creator.Faction);
                    TorannMagicDefOf.TM_RangerBondHD=new HediffDef {defName="RangerCareTrialBond"};
                    pet.health.hediffSet.hediffs.Add(new Hediff {def=TorannMagicDefOf.TM_RangerBondHD,pawn=pet});
                    might.bondedPet=pet;
                    var job=new Job {def=new JobDef{defName="TM_RangerCompanionCare"},targetA=pet,targetB=c.Original,targetC=c.Creator.Position};
                    var driver=new JobDriver_RangerCompanionCare {job=job,pawn=c.Creator};
                    var toils=(IEnumerable<Toil>)driver.GetType().GetMethod("MakeNewToils",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(driver,null)!;
                    var finish=toils.Last();
                    finish.initAction();
                    int Visits() => (int)Production("RangerCareer").GetMethod("Days",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[c.Original])!;
                    Check.Equal(1,Visits(),"physical care by host advances retained bond");
                    finish.initAction();
                    Check.Equal(1,Visits(),"same-day repeated completion cannot double count");
                    int now=Find.TickManager.TicksGame;
                    var markerHediff=held.Possession.parent;
                    Set(Find.TickManager,"ticksGameInt",now+GenDate.TicksPerDay);
                    c.Creator.health.hediffSet.hediffs.Remove(markerHediff);
                    try
                    {
                        finish.initAction();
                        Check.Equal(1,Visits(),"a former host cannot earn the next daily visit");
                    }
                    finally { c.Creator.health.hediffSet.hediffs.Add(markerHediff); Set(Find.TickManager,"ticksGameInt",now); }
                    Check.Same(pet,might.bondedPet,"the same original still owns the pet");
                    Check.Equal(true,c.Creator.GetCompAbilityUserMight().bondedPet==null,"host did not acquire the soul's companion");
                    var bondType=typeof(CompAbilityUserMight).Assembly.GetType("TorannMagic.HediffComp_RangerBond",true)!;
                    var marker=new HediffWithComps{pawn=pet,def=TorannMagicDefOf.TM_RangerBondHD};
                    var bond=(HediffComp)Activator.CreateInstance(bondType)!;bond.parent=marker;marker.comps=[bond];
                    using var draw=new Patch(typeof(Pawn).GetProperty(nameof(Pawn.DrawPos))!.GetMethod!,nameof(CancelNative));
                    using var glow=new Patch(typeof(FleckMaker).GetMethods().Single(m=>m.Name=="ThrowHeatGlow" && m.GetParameters()[0].ParameterType==typeof(IntVec3)),nameof(CancelNative));
                    var roster=(IList<Pawn>)c.Map.mapPawns.AllPawnsSpawned;
                    if(!roster.Contains(c.Creator))roster.Add(c.Creator);
                    bondType.GetMethod("Initialize",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(bond,null);
                    Check.Same(c.Original,bondType.GetField("bonderPawn")!.GetValue(bond)!,"old unlinked marker finds the held ranger with this exact pet");
                    var versatility=might.MightData.MightPowerSkill_AnimalFriend.Single(s=>s.label=="TM_AnimalFriend_ver");
                    versatility.level=2;
                    bondType.GetMethod("UpdateBond",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(bond,null);
                    Check.Equal(2.5f,marker.Severity,"the retained pet uses Animal Friend training even if the original also has a magic component");
                }
                finally { TorannMagicDefOf.TM_RangerBondHD=previous; }
            }));
        yield return new("BladeProjection.SevenStepsOrder", f =>
            SpiritRebirthRitualTransactionTests.Run(f,c => {
                using var held = new Fixture(f,c);
                var def = DefDatabase<TMAbilityDef>.GetNamedSilentFail("TM_BladeSevenSteps") ?? f.AddAbility("TM_BladeSevenSteps");
                var source = c.Original.GetCompAbilityUserMight();
                var power = new BladeFormAbility(source) { Def=def };
                source.AbilityData.Powers.Add(power);
                source.AbilityData.AllPowers=null;
                var state = new ClassProgressionState(Vocation.Warrior);
                Set(ClassIdentity.Component(c.Original),"state",state);
                state.TryAddFoundation("rwom.bladedancer",DisciplineDomain.Might);
                var path = state.Find("rwom.bladedancer");
                Set(path,"stage",InitiationStage.Ascended);
                Set(path,"BladeUltimate",Enum.Parse(Production("BladeUltimate"),"SevenSteps"));
                const string name="TM_BladeSevenStepsCast";
                var find=Production("SpiritHostedMartial").GetMethod("Find",BindingFlags.NonPublic|BindingFlags.Static)!;
                Check.Same(power,find.Invoke(null,[c.Creator,c.Original,name])!,"retained Seven Steps uses the exact learned form");
                if (DefDatabase<JobDef>.GetNamedSilentFail(name)==null)
                    DefDatabase<JobDef>.Add(new JobDef{defName=name,driverClass=typeof(JobDriver_BladeSevenSteps)});
                var order=(Job)typeof(BladeFormAbility).GetMethod("MakeJobFor",BindingFlags.NonPublic|BindingFlags.Instance)!
                    .Invoke(power,[c.Creator,new LocalTargetInfo(c.Creator.Position+IntVec3.North)])!;
                Check.Same(c.Original,order.targetQueueB.Single().Pawn,"source survives independently of later possession changes");
                Check.Equal(c.Creator.Position,order.targetC.Cell,"route starts at the physical host");
                c.Original.ageTracker.AgeBiologicalTicks = 25L * GenDate.TicksPerYear;
                source.maxSP = 1;
                _ = source.MightData;
                if (source.Stamina == null) c.Original.needs.AllNeeds.Add(new Need_Stamina(c.Original){def=TorannMagicDefOf.TM_Stamina});
                source.Stamina.CurLevelPercentage=1;
                var driver=new JobDriver_BladeSevenSteps{pawn=c.Creator,job=order};
                Set(driver,"preparing",true);
                var finish=driver.GetType().GetMethod("FinishForm",BindingFlags.Instance|BindingFlags.NonPublic)!;
                c.Creator.health.hediffSet.hediffs.Remove(held.Possession.parent);
                try
                {
                    finish.Invoke(driver,null);
                    finish.Invoke(driver,null);
                    Check.Equal(true,Math.Abs(source.Stamina.CurLevelPercentage-.94f)<.001f,"exit during preparation charges the original once");
                    Check.Equal(true,power.CooldownTicksLeft>0,"interrupted form cooldown remains with source");
                    var paidDriver=new JobDriver_BladeSevenSteps{pawn=c.Creator,job=order};
                    Set(paidDriver,"paid",true);
                    float before=source.Stamina.CurLevel;
                    finish.Invoke(paidDriver,null);
                    Check.Equal(before,source.Stamina.CurLevel,"an already paid series has no second exit price");
                }
                finally { c.Creator.health.hediffSet.hediffs.Add(held.Possession.parent); }
                var exhausted=typeof(JobDriver_BladeSevenSteps).GetMethod("Exhausted",BindingFlags.Static|BindingFlags.NonPublic);
                Check.NotNull(exhausted,"movement fatigue must use the held discipline");
                Check.Equal(true,(bool)exhausted!.Invoke(null,[c.Creator])!,"the occupied body moves more slowly after Seven Steps");
                c.Creator.health.hediffSet.hediffs.Remove(held.Possession.parent);
                try { Check.Equal(false,(bool)exhausted.Invoke(null,[c.Creator])!,"former body no longer receives class fatigue"); }
                finally { c.Creator.health.hediffSet.hediffs.Add(held.Possession.parent); }
            }));
        yield return new("BladeProjection.ServiceWornWeapon", f =>
            SpiritRebirthRitualTransactionTests.Run(f,c => {
                using var held = new Fixture(f,c);
                var state = new ClassProgressionState(Vocation.Warrior);
                Set(ClassIdentity.Component(c.Original),"state",state);
                state.TryAddFoundation("rwom.bladedancer",DisciplineDomain.Might);
                BladeServiceTests.Verify(f,c.Creator,c.Original);
            }));
        yield return new("BladeProjection.RiposteAfterSave", f =>
            SpiritRebirthRitualTransactionTests.Run(f,c => {
                using var held=new Fixture(f,c);
                var state=new ClassProgressionState(Vocation.Warrior);
                Set(ClassIdentity.Component(c.Original),"state",state);
                state.TryAddFoundation("rwom.bladedancer",DisciplineDomain.Might);
                Set(state.Find("rwom.bladedancer"),"stage",InitiationStage.Adept);
                BladeRiposteTests.Verify(f,c.Creator,c.Original);
            }));
        yield return new("BladeProjection.MemoryAlternatingTargets", f =>
            SpiritRebirthRitualTransactionTests.Run(f,c => {
                using var held = new Fixture(f,c);
                var state=new ClassProgressionState(Vocation.Warrior);
                Set(ClassIdentity.Component(c.Original),"state",state);
                state.TryAddFoundation("rwom.bladedancer",DisciplineDomain.Might);
                var path=state.Find("rwom.bladedancer");
                Set(path,"BladeMemory",true);
                var skill=c.Original.GetCompAbilityUserMight().MightData.MightPowerSkill_BladeFocus.Single();
                skill.levelMax=4; skill.level=4;
                BladeFoundationTests.Melee(c.Creator);
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                Check.Equal(1720,(int)path.GetType().GetField("BladeMemoryReadyAt",flags)!.GetValue(path)!,
                    "last switch writes the cooldown to the original discipline");
            }));
        yield return new("BladeProjection.TrainedFocus", f =>
            SpiritRebirthRitualTransactionTests.Run(f,c => {
                using var held=new Fixture(f,c);
                var state=new ClassProgressionState(Vocation.Warrior);
                Set(ClassIdentity.Component(c.Original),"state",state);
                state.TryAddFoundation("rwom.bladedancer",DisciplineDomain.Might);
                var old=TorannMagicDefOf.Bladedancer;
                TorannMagicDefOf.Bladedancer=new TraitDef {defName="Bladedancer"};
                try
                {
                    var skill=c.Original.GetCompAbilityUserMight().MightData.MightPowerSkill_BladeFocus.Single();
                    skill.levelMax=3;
                    BladeFoundationTests.Verify(f,c.Creator,c.Original.GetCompAbilityUserMight(),false);
                }
                finally { TorannMagicDefOf.Bladedancer=old; }
            }));
        yield return new("BladeProjection.DeliverMedicine", f =>
            SpiritRebirthRitualTransactionTests.Run(f,c => {
                using var held = new Fixture(f,c);
                var state=new ClassProgressionState(Vocation.Warrior);
                Set(ClassIdentity.Component(c.Original),"state",state);
                state.TryAddFoundation("rwom.bladedancer",DisciplineDomain.Might);
                Set(state.Find("rwom.bladedancer"),"stage",InitiationStage.Master);
                BladeMedicalSupplyTests.Verify(f,c.Creator,c.Original);
            }));
        yield return new("BladeProjection.RescueTrialOwnership", f =>
            SpiritRebirthRitualTransactionTests.Run(f,c => {
                using var held = new Fixture(f,c);
                void Train(Pawn pawn)
                {
                    var state = new ClassProgressionState(Vocation.Warrior);
                    Set(ClassIdentity.Component(pawn),"state",state);
                    state.TryAddFoundation("rwom.bladedancer",DisciplineDomain.Might);
                    Set(state.Find("rwom.bladedancer"),"stage",InitiationStage.Adept);
                }
                var api = Production("BladeMastery");
                var delivered = api.GetMethod("Delivered",BindingFlags.NonPublic|BindingFlags.Static)!;
                var rescues = api.GetMethod("Rescues",BindingFlags.NonPublic|BindingFlags.Static)!;
                int Count(Pawn pawn) => (int)rescues.Invoke(null,[pawn])!;
                var ally = new Pawn { thingIDNumber = 100001 };
                Train(c.Original);
                delivered.Invoke(null,[c.Creator,ally]);
                delivered.Invoke(null,[c.Creator,ally]);
                Check.Equal(1,Count(c.Original),"one delivery per casualty per day on the retained path");
                var possession = held.Possession.parent;
                c.Creator.health.hediffSet.hediffs.Remove(possession);
                try
                {
                    delivered.Invoke(null,[c.Creator,new Pawn { thingIDNumber = 100002 }]);
                    Check.Equal(1,Count(c.Original),"former host cannot credit a different casualty");
                }
                finally { c.Creator.health.hediffSet.hediffs.Add(possession); }
                Train(c.Creator);
                delivered.Invoke(null,[c.Creator,ally]);
                Check.Equal(1,Count(c.Creator),"local discipline owns the next delivery");
                Check.Equal(1,Count(c.Original),"local rescue does not double-credit the soul");
            }));
        foreach(var id in new[]{"TM_PhaseStrike","TM_PhaseStrike_I","TM_PhaseStrike_II","TM_PhaseStrike_III"})
            yield return new("BladeProjection.PhaseStrike."+id,f =>
                SpiritRebirthRitualTransactionTests.Run(f,c => {
                    using var held=new Fixture(f,c);
                    var def=(TMAbilityDef)typeof(TorannMagicDefOf).GetField(id)!.GetValue(null)!;
                    var source=c.Original.GetCompAbilityUserMight();
                    var original=new MightAbility(source){Def=def};source.AbilityData.Powers.Add(original);source.AbilityData.AllPowers=null;
                    var find=Production("SpiritHostedMartial").GetMethod("Find",BindingFlags.NonPublic|BindingFlags.Static)!;
                    object? Power()=>find.Invoke(null,[c.Creator,c.Original,"TM_SpiritCastPhaseStrike"]);
                    Check.Same(original,Power()!,"use the real acquired rank and its targeting range");
                    var host=c.Creator.GetCompAbilityUserMight();host.AbilityData.Powers.Add(new MightAbility(host){Def=TorannMagicDefOf.TM_PhaseStrike});
                    host.AbilityData.AllPowers=null;Check.Equal(true,Power()==null,"a native lower rank takes precedence");
                }));
        yield return new("GladiatorLastCircle.ProjectOnlyChosenAscension", f =>
            SpiritRebirthRitualTransactionTests.Run(f,c => {
                using var held=new Fixture(f,c);
                void Ascend(Pawn p)
                {
                    var state=new ClassProgressionState(Vocation.Warrior);
                    Set(p.GetComp<CompClassProgression>(),"state",state);
                    state.TryAddFoundation("rwom.gladiator",DisciplineDomain.Might);
                    var path=state.Find("rwom.gladiator");
                    Set(path,"stage",InitiationStage.Ascended);
                    Set(path,"Ultimate",GladiatorUltimate.LastCircle);
                }
                var owns=typeof(JobDriver_LastCircle).GetMethod("CanProject",BindingFlags.NonPublic|BindingFlags.Static);
                Check.NotNull(owns,"the native stance requires an explicit projected command");
                bool Projects()=>(bool)owns!.Invoke(null,[c.Creator,c.Original])!;
                Check.Equal(false,Projects(),"a spirit without the ascension does not receive the command");
                Ascend(c.Original);
                Check.Equal(true,Projects(),"only the soul has the chosen stance");
                Ascend(c.Creator);
                Check.Equal(false,Projects(),"the host's own stance has priority");
            }));
        yield return new("GladiatorCover.HostUsesLearnedDefenseAndTraining", f =>
            SpiritRebirthRitualTransactionTests.Run(f,c => {
                using var held=new Fixture(f,c);
                var api=typeof(JobDriver_GladiatorDefense).GetMethod("CanOfferProjected",BindingFlags.NonPublic|BindingFlags.Static);
                Check.NotNull(api,"the ally guard needs a source-aware job");
                var state=new ClassProgressionState(Vocation.Warrior);
                Set(c.Original.GetComp<CompClassProgression>(),"state",state);
                state.TryAddFoundation("rwom.gladiator",DisciplineDomain.Might);
                bool Offers(bool drill)=>(bool)api!.Invoke(null,[c.Creator,c.Original,drill])!;
                Check.Equal(true,Offers(true),"a foundation gladiator can run the relief drill from its host");
                Check.Equal(false,Offers(false),"training alone cannot unlock a combat defense");
                var path=state.Find("rwom.gladiator");
                Set(path,"stage",InitiationStage.Adept);Set(path,"Defense",GladiatorDefense.Cover);
                Check.Equal(true,Offers(false),"a learned defense can be projected");
            }));
        yield return new("GladiatorWhirlwind.OriginalAbilityOwnsHostFlight", f =>
            SpiritRebirthRitualTransactionTests.Run(f,c => {
                using var held=new Fixture(f,c);
                var def=TorannMagicDefOf.TM_Whirlwind;
                var source=c.Original.GetCompAbilityUserMight();
                var original=new MightAbility(source){ Def=def };
                source.AbilityData.Powers.Add(original);source.AbilityData.AllPowers=null;
                var find=Production("SpiritHostedMartial").GetMethod("Find",BindingFlags.NonPublic|BindingFlags.Static)!;
                object? Power()=>find.Invoke(null,[c.Creator,c.Original,"TM_SpiritCastWhirlwind"]);
                Check.Same(original,Power()!,"takeoff must use the retained ultimate");
                Check.Equal(false,TorannMagic.Odyssey.ManualTravelSafety.TakePayment(original),"no charge before actual takeoff");
                var host=c.Creator.GetCompAbilityUserMight();
                host.AbilityData.Powers.Add(new MightAbility(host){Def=def});host.AbilityData.AllPowers=null;
                Check.Equal(true,Power()==null,"native whirlwind prevents a second projected command");
            }));

        foreach (var id in new[] { "TM_Grapple","TM_Grapple_I","TM_Grapple_II","TM_Grapple_III" })
            yield return new("SpiritProjectedCast.Martial.Grapple."+id, f =>
                SpiritRebirthRitualTransactionTests.Run(f,c => {
                    using var held = new Fixture(f,c);
                    var def=(TMAbilityDef)typeof(TorannMagicDefOf).GetField(id)!.GetValue(null)!;
                    var source=c.Original.GetCompAbilityUserMight();
                    var power=new MightAbility(source){Def=def};
                    source.AbilityData.Powers.Add(power); source.AbilityData.AllPowers=null;
                    var find=Production("SpiritHostedMartial").GetMethod("Find",BindingFlags.Static|BindingFlags.NonPublic)!;
                    object? Held() => find.Invoke(null,[c.Creator,c.Original,"TM_SpiritCastGrapple"]);
                    Check.Same(power,Held()!,"retained rank supplies its own grapple range and cooldown");
                    var local=c.Creator.GetCompAbilityUserMight();
                    local.AbilityData.Powers.Add(new MightAbility(local){Def=TorannMagicDefOf.TM_Grapple});
                    local.AbilityData.AllPowers=null;
                    Check.Equal(true,Held()==null,"one family command when the body already knows grapple");
                }));

        yield return new("SpiritProjectedCast.Martial.Grapple.RestoreRetainedRank",f =>
            SpiritRebirthRitualTransactionTests.Run(f,c => {
                using var held=new Fixture(f,c);
                var state=new ClassProgressionState(Vocation.Warrior);
                Set(c.Original.GetComp<CompClassProgression>(),"state",state);
                state.TryAddFoundation("rwom.gladiator",DisciplineDomain.Might);
                var might=c.Original.GetCompAbilityUserMight();
                var row=might.MightData.MightPowersG.Single(r=>r.TMabilityDefs.Contains(TorannMagicDefOf.TM_Grapple_III));
                var restore=typeof(JobDriver_GladiatorInitiation).GetMethod("Restore",BindingFlags.NonPublic|BindingFlags.Static)!;
                void Refresh()=>restore.Invoke(null,[might]);
                row.level=3;row.learned=false;Refresh();
                Check.Equal(false,might.AbilityData.Powers.Any(p=>p.Def==TorannMagicDefOf.TM_Grapple_III),"no free rank for an unlearned row");
                var rank=TorannMagicDefOf.TM_Grapple_III;
                var before=rank.abilityClass;
                try
                {
                    rank.abilityClass=typeof(MightAbility);
                    row.learned=true;Refresh();
                    var replay=might.AbilityData.Powers.Single(p=>p.Def==rank);
                    replay.CooldownTicksLeft=72;Refresh();
                    Check.Same(replay,might.AbilityData.Powers.Single(p=>p.Def==rank),"exact learned rank restored once");
                    Check.Equal(72,replay.CooldownTicksLeft,"refresh preserves the cooldown");
                }
                finally { rank.abilityClass=before; }
            }));

        yield return new("SpiritProjectedCast.Martial.GladiatorCleave.PassiveOwnerPrecedence", f =>
            SpiritRebirthRitualTransactionTests.Run(f,c => {
                using var held=new Fixture(f,c);
                var api=Production("GladiatorMastery").GetMethod("AutomaticSource",BindingFlags.NonPublic|BindingFlags.Static);
                Check.NotNull(api,"native passive hit must resolve the owner before rolling");
                void SelectCleave(Pawn p)
                {
                    var state=new ClassProgressionState(Vocation.Warrior);
                    Set(p.GetComp<CompClassProgression>(),"state",state);
                    state.TryAddFoundation("rwom.gladiator",DisciplineDomain.Might);
                    Set(state.Find("rwom.gladiator"),"Attack",GladiatorAttack.Cleave);
                }
                SelectCleave(c.Original);
                var local=c.Creator.GetCompAbilityUserMight();
                object? Source() => api!.Invoke(null,[c.Creator,local]);
                Check.Same(c.Original.GetCompAbilityUserMight(),Source()!,"retained passive uses the original's skill, toggle and resource");
                SelectCleave(c.Creator);
                Check.Same(local,Source()!,"a native host passive wins without stacking the soul's hit");
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
