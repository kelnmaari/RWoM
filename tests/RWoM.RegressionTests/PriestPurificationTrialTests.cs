using System.Reflection;
using System.Runtime.CompilerServices;
using AbilityUser;
using RimWorld;
using TorannMagic;
using TorannMagic.ClassProgression;
using Verse;
using static RWoM.RegressionTests.GrimoireProgressionTests;

namespace RWoM.RegressionTests;

internal static class PriestPurificationTrialTests
{
    public static IEnumerable<TestCase> Cases()
    {
        yield return new("PriestPurificationTrial.CompleteScarOnlyAndIdempotent", Completion);
        yield return new("PriestPurificationTrial.OwnedAdeptSpellOnly", Eligibility);
        yield return new("PriestPurificationTrial.NativeHookAttaches", Hook);
    }

    sealed class Fixture : IDisposable
    {
        readonly Game previous = Current.Game;
        readonly ClassDisciplineDef[] profiles = DefDatabase<ClassDisciplineDef>.AllDefsListForReading.ToArray();
        public Pawn Caster, Target;
        public CompAbilityUserMagic Magic;
        public CompClassProgression Progression;
        public DisciplineProgress Path;
        public PawnAbility Ability;
        public Hediff_Injury Scar;
        public Fixture(RuntimeFixture f, bool adept = true)
        {
            LoadBaseDefinitions(f);
            DefDatabase<ClassDisciplineDef>.Clear();
            DefDatabase<ClassDisciplineDef>.Add(new ClassDisciplineDef { defName = "PurificationPriest", profileId = "rwom.priest", domain = DisciplineDomain.Magic });
            Current.Game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
            Set(Current.Game, "tickManager", new TickManager());
            Set(Current.Game, "maps", new List<Map> { (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map)) });
            var c = PawnWithComps(true, false); Caster = c.pawn; Magic = c.magic;
            Target = PawnWithComps(false, false).pawn;
            var faction = new Faction { def = new FactionDef { isPlayer = true } };
            foreach (var pawn in new[] { Caster, Target })
            {
                pawn.mindState = new Verse.AI.Pawn_MindState(pawn);
                pawn.kindDef = new PawnKindDef { defName = "PurificationPawn" };
                Set(pawn, "factionInt", faction); Set(pawn, "mapIndexOrState", (sbyte)0);
            }
            Progression = new CompClassProgression { parent = Caster }; Caster.AllComps.Add(Progression);
            Progression.BeginNewProgression(Vocation.Mage); Progression.State.TryAddFoundation("rwom.priest", DisciplineDomain.Magic);
            Path = Progression.State.Find("rwom.priest");
            if (adept)
            {
                Path.RecordCompletedTrial(PriestHealingTrial.TrialId);
                Progression.State.TryAdvanceAfterTrial("rwom.priest", InitiationStage.Foundation, PriestHealingTrial.TrialId);
            }
            Ability = new PawnAbility(Magic); Set(Ability, "powerDef", TorannMagicDefOf.TM_Purify);
            Ability.CooldownTicksLeft = 77; Magic.AbilityData.Powers.Add(Ability);
            Scar = new Hediff_Injury { pawn = Target, def = new HediffDef { defName = "PurificationScar", injuryProps = new InjuryProps() } };
            var permanent = new HediffComp_GetsPermanent { parent = Scar, props = new HediffCompProperties_GetsPermanent() };
            Set(permanent, "isPermanentInt", true); Scar.comps = [permanent];
            Target.health.hediffSet.hediffs.Add(Scar); Severity(8f);
        }
        public void Severity(float value) => Set(Scar, "severityInt", value);
        public PriestPurificationTrial.Snapshot Capture() => PriestPurificationTrial.Capture(Caster, Target, Ability);
        public void Dispose()
        {
            Current.Game = previous;
            DefDatabase<ClassDisciplineDef>.Clear(); foreach (var profile in profiles) DefDatabase<ClassDisciplineDef>.Add(profile);
        }
    }

    static void Completion(RuntimeFixture f)
    {
        foreach (bool remove in new[] { false, true })
        {
            using var w = new Fixture(f);
            w.Magic.MagicData.MagicUserXP = 67; w.Magic.MagicData.MagicAbilityPoints = 4;
            var skill = w.Magic.MagicData.MagicPowerSkill_Purify[0]; skill.level = 2;
            Check.Equal(false, PriestPurificationTrial.Complete(w.Capture(), true), "successful no-op cannot finish trial");
            var partial = w.Capture(); w.Severity(4f);
            Check.Equal(false, PriestPurificationTrial.Complete(partial, true), "partly healed scar cannot finish trial");
            var failed = w.Capture(); w.Severity(0f);
            Check.Equal(false, PriestPurificationTrial.Complete(failed, false), "failed spell cannot credit scar removal");
            w.Severity(2f); var cured = w.Capture();
            if (remove) w.Target.health.hediffSet.hediffs.Remove(w.Scar); else w.Severity(0f);
            Check.Equal(true, PriestPurificationTrial.Complete(cured, true), "pre-existing scar removed or zeroed completes trial");
            Check.Equal(false, PriestPurificationTrial.Complete(cured, true), "duplicate callback is inert");
            Check.Equal(1, w.Path.CompletedTrials.Count(t => t == PriestPurificationTrial.TrialId), "one persisted purification marker");
            Check.Equal(InitiationStage.Adept, w.Path.Stage, "trial does not advance stage");
            Check.Equal(67, w.Magic.MagicData.MagicUserXP, "XP unchanged");
            Check.Equal(4, w.Magic.MagicData.MagicAbilityPoints, "points unchanged");
            Check.Equal(2, skill.level, "skill unchanged");
            Check.Equal(77, w.Ability.CooldownTicksLeft, "cooldown unchanged");
        }
    }

    static void Eligibility(RuntimeFixture f)
    {
        using var w = new Fixture(f, false);
        Check.Same(null!, w.Capture()!, "Foundation not eligible for Adept trial");
        w.Path.RecordCompletedTrial(PriestHealingTrial.TrialId);
        w.Progression.State.TryAdvanceAfterTrial("rwom.priest", InitiationStage.Foundation, PriestHealingTrial.TrialId);
        Check.Same(null!, PriestPurificationTrial.Capture(w.Caster, w.Caster, w.Ability)!, "self treatment excluded");
        var fake = new PawnAbility(w.Magic); Set(fake, "powerDef", TorannMagicDefOf.TM_Purify);
        Check.Same(null!, PriestPurificationTrial.Capture(w.Caster, w.Target, fake)!, "definition-equal fake instance excluded");
        w.Magic.AbilityData.Powers.Clear(); w.Magic.AbilityData.TemporaryWeaponPowers.Add(w.Ability);
        Check.Same(null!, w.Capture()!, "item-only spell excluded");
        w.Magic.AbilityData.TemporaryWeaponPowers.Clear(); w.Magic.AbilityData.Powers.Add(w.Ability);
        var comps = w.Scar.comps; w.Scar.comps = [];
        Check.Same(null!, w.Capture()!, "ordinary injury is not a permanent scar");
        w.Scar.comps = comps;
        var faction = w.Target.Faction; Set(w.Target, "factionInt", null);
        Check.Same(null!, w.Capture()!, "factionless target excluded");
        Set(w.Target, "factionInt", faction);
        var stale = w.Capture(); w.Severity(0f); w.Magic.AbilityData.Powers.Clear();
        Check.Equal(false, PriestPurificationTrial.Complete(stale, true), "lost ownership cannot finish trial");
        Check.Equal(false, w.Path.CompletedTrials.Contains(PriestPurificationTrial.TrialId), "rejected casts preserve trial state");
    }

    static void Hook(RuntimeFixture f)
    {
        var native = typeof(Verb_Purify).GetMethod("TryCastShot", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!;
        Check.Equal(typeof(bool), native.ReturnType, "hook reads native cast success");
        var assembly = Assembly.Load("0Harmony"); var ht = assembly.GetType("HarmonyLib.Harmony")!;
        var id = "rwom.regression.purification." + Guid.NewGuid(); var harmony = Activator.CreateInstance(ht, id)!;
        try
        {
            var processor = ht.GetMethod("CreateClassProcessor")!.Invoke(harmony, [typeof(PriestPurificationTrialPatch)])!;
            processor.GetType().GetMethod("Patch")!.Invoke(processor, null);
            var patched = ((IEnumerable<MethodBase>)ht.GetMethod("GetAllPatchedMethods")!.Invoke(null, null)!).ToList();
            Check.Equal(true, patched.Contains(native), "prefix/postfix emit and attach to original Purify");
        }
        finally { ht.GetMethod("UnpatchAll")!.Invoke(harmony, [id]); }
    }
}
