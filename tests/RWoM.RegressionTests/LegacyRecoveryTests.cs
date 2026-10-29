using System.Reflection;
using AbilityUser;
using RimWorld;
using TorannMagic;
using TorannMagic.Compatibility;
using Verse;
using static RWoM.RegressionTests.GrimoireProgressionTests;

namespace RWoM.RegressionTests;

internal static class LegacyRecoveryTests
{
    public static IEnumerable<TestCase> Cases()
    {
        yield return new("LegacyRecovery.ManualRepairReportsSkippedReason", f =>
        {
            Prepare(f); var p = PawnWithComps(false, false);
            var output = new System.Collections.Concurrent.ConcurrentQueue<(string Level, string Text)>();
            using var logs = LoggingCompatTests.Configure(null, null, output);
            TorannMagic.ModOptions.TM_DebugTools.RepairMissingClassData(p.pawn);
            Check.Equal(true, output.Any(x => x.Text.Contains("status=skipped") && x.Text.Contains("no class trait")), "unclassed pawn must not produce a success report");
            output.Clear(); TorannMagic.ModOptions.TM_DebugTools.RepairMissingClassData(null!);
            Check.Equal(true, output.Any(x => x.Text.Contains("status=skipped") && x.Text.Contains("no pawn")), "null pawn has explicit reason");
        });
        yield return new("LegacyRecovery.ManualRepairReportsIncompleteAndFailure", f =>
        {
            Prepare(f); var p = PawnWithComps(true, false);
            p.pawn.story.traits.allTraits.Add(new Trait(TorannMagicDefOf.InnerFire));
            var output = new System.Collections.Concurrent.ConcurrentQueue<(string Level, string Text)>();
            using var logs = LoggingCompatTests.Configure(null, null, output);
            p.pawn.needs = null!;
            TorannMagic.ModOptions.TM_DebugTools.RepairMissingClassData(p.pawn);
            Check.Equal(true, output.Any(x => x.Level == "Warning" && x.Text.Contains("status=incomplete") && x.Text.Contains("mana=missing")), "missing needs tracker cannot be reported as complete");
            output.Clear();
            p.magic.MagicData.MagicPowersStandalone.Add(null!);
            TorannMagic.ModOptions.TM_DebugTools.RepairMissingClassData(p.pawn);
            Check.Equal(true, output.Any(x => x.Level == "Error" && x.Text.Contains("status=failed") && x.Text.Contains("Exception")), "malformed data reports error with exception");
            Check.Equal(false, output.Any(x => x.Text.Contains("status=completed")), "failure cannot emit success");
        });
        yield return new("LegacyRecovery.PartialScribeListsPreserveInvestmentsAndZeros", f =>
        {
            Prepare(f);
            var data = new MagicData { MagicUserLevel = 0, MagicUserXP = 0, MagicAbilityPoints = 0 };
            var rank = data.MagicPowersIF[0]; rank.level = 2; rank.learned = true;
            data.MagicPowersIF.RemoveAt(2);
            var skill = data.MagicPowerSkill_Firebolt[0]; skill.level = 3; skill.costToLevel = 9;
            data.MagicPowerSkill_Firebolt.RemoveAt(1);
            data.MagicPowerSkill_global_regen.Clear();
            var loaded = f.Load(f.Save(data));
            Check.Equal(5, loaded.MagicPowersIF.Count, "missing builtin row restored");
            Check.Equal(2, loaded.MagicPowersIF[0].level, "saved rank retained");
            Check.Equal(3, loaded.MagicPowerSkill_Firebolt[0].level, "skill investment retained");
            Check.Equal(9, loaded.MagicPowerSkill_Firebolt[0].costToLevel, "saved skill cost retained");
            Check.Equal(2, loaded.MagicPowerSkill_Firebolt.Count, "missing skill restored");
            Check.Equal(1, loaded.MagicPowerSkill_global_regen.Count, "empty global list repaired");
            Check.Equal(false, loaded.MagicPowersIF.Single(p => p.TMabilityDefs.Contains(TorannMagicDefOf.TM_Firestorm)).learned, "unlearned master spell remains locked");
            var savedSkill = loaded.MagicPowerSkill_Firebolt[0];
            Check.Equal(0, loaded.RestoreMissingRecords(), "repeat fills nothing");
            Check.Same(savedSkill, loaded.MagicPowerSkill_Firebolt[0], "repeat preserves owner identity");
            loaded = f.Load(f.Save(loaded));
            Check.Equal(0, loaded.MagicUserLevel, "zero level not mistaken for missing");
            Check.Equal(0, loaded.MagicUserXP, "zero XP retained");
            Check.Equal(0, loaded.MagicAbilityPoints, "no awarded/refunded points");
        });
        yield return new("LegacyRecovery.MissingBlockUsesRuntimeRankWithoutInventingXP", f =>
        {
            Prepare(f); var p = PawnWithComps(true, false);
            p.pawn.story.traits.allTraits.Add(new Trait(TorannMagicDefOf.Warlock));
            Set(p.magic, "magicData", null!);
            TM_ClassState.AddRuntimeAbility(p.magic, TorannMagicDefOf.TM_ShadowBolt_II);
            var runtime = p.magic.AbilityData.Powers.Single(); runtime.CooldownTicksLeft = 91;
            Check.Equal(true, p.magic.RecoverMissingProgression(), "missing whole block detected before getter creates it");
            var data = p.magic.MagicData;
            var owner = data.MagicPowersWD.Single(x => x.TMabilityDefs.Contains(TorannMagicDefOf.TM_ShadowBolt));
            Check.Equal(2, owner.level, "saved runtime rank used as evidence");
            Check.Equal(true, owner.learned, "saved runtime proves learned spell");
            Check.Equal(0, data.MagicUserXP, "no XP guessed from rank");
            Check.Equal(0, data.MagicAbilityPoints, "no points guessed from rank");
            Check.Equal(false, p.magic.RecoverMissingProgression(), "repeat keeps same data block");
            TM_ClassState.RestoreCurrentMagicRuntime(p.magic);
            Check.Same(data, p.magic.MagicData, "repeat keeps data instance");
            Check.Same(runtime, p.magic.AbilityData.Powers.Single(x => x.Def == TorannMagicDefOf.TM_ShadowBolt_II), "existing runtime owner retained");
            Check.Equal(91, runtime.CooldownTicksLeft, "cooldown retained");
        });
        yield return new("LegacyRecovery.RuntimeRepairRespectsLearnedFlags", f =>
        {
            Prepare(f); var p = PawnWithComps(true, false);
            p.pawn.story.traits.allTraits.Add(new Trait(TorannMagicDefOf.InnerFire));
            Set(p.pawn, "factionInt", new Faction { def = new FactionDef { isPlayer = true } });
            Check.Equal(true, p.pawn.IsColonist, "loaded colonist fixture");
            var data = p.magic.MagicData; data.MagicUserLevel = 22; data.MagicUserXP = 137; data.MagicAbilityPoints = 6;
            data.MagicPowersIF[0].learned = true; data.MagicPowersIF[0].level = 2;
            var locked = data.MagicPowersIF[1]; locked.learned = false;
            p.magic.RecoverMissingProgression();
            TM_ClassState.RestoreCurrentMagicRuntime(p.magic); TM_ClassState.RestoreCurrentMagicRuntime(p.magic);
            p.magic.ResolveFactionChange();
            Check.Equal(1, p.magic.AbilityData.Powers.Count(x => x.Def == data.MagicPowersIF[0].abilityDef), "learned ranked spell restored exactly once");
            Check.Equal(false, locked.learned, "saved false is preserved even for a basic spell");
            Check.Equal(22, data.MagicUserLevel, "level retained"); Check.Equal(137, data.MagicUserXP, "XP retained"); Check.Equal(6, data.MagicAbilityPoints, "points retained");
        });
        yield return new("LegacyRecovery.MightPartialRecordsRetainOwners", f =>
        {
            Prepare(f); var data = new MightData { MightUserLevel = 17, MightAbilityPoints = 8 };
            var skills = data.AllMightPowerSkills; var saved = skills.First(); saved.level = 2;
            var property = typeof(MightData).GetProperties().First(p => p.Name.StartsWith("MightPowerSkill_") && !p.Name.Contains("Custom") && ((List<MightPowerSkill>)p.GetValue(data)!).Count > 1);
            var list = (List<MightPowerSkill>)property.GetValue(data)!; list.RemoveAt(list.Count - 1);
            Check.Equal(true, data.RestoreMissingRecords() > 0, "partial fighter list repaired");
            Check.Equal(true, data.AllMightPowerSkills.Contains(saved), "fighter owner retained"); Check.Equal(2, saved.level, "fighter investment retained");
            Check.Equal(17, data.MightUserLevel, "fighter level retained"); Check.Equal(8, data.MightAbilityPoints, "fighter points retained");
            Check.Equal(0, data.RestoreMissingRecords(), "fighter repair idempotent");
            var p = PawnWithComps(false, true);
            p.pawn.story.traits.allTraits.Add(new Trait(TorannMagicDefOf.Gladiator));
            Set(p.might, "mightData", data);
            p.might.mightPowersInitialized = false;
            Check.Equal(false, p.might.RecoverMissingProgression(), "existing fighter data retained");
            Check.Equal(true, p.might.mightPowersInitialized, "missing old flag cannot trigger destructive first initialization");
            Check.Same(data, p.might.MightData, "saved fighter data owner retained");
            Check.Equal(17, data.MightUserLevel, "fighter recovery retains saved level");
        });
        yield return new("LegacyRecovery.MissingManaCreatedExistingZeroPreserved", f =>
        {
            using var game = new GameScope();
            Prepare(f); var p = PawnWithComps(true, false); p.pawn.ageTracker = new Pawn_AgeTracker(p.pawn);
            p.pawn.def.race.lifeStageAges = [new LifeStageAge { minAge = 0, def = new LifeStageDef { defName = "RecoveryAdult", developmentalStage = DevelopmentalStage.Adult } }];
            Set(p.pawn.ageTracker, "cachedLifeStageIndex", 0);
            var need = new NeedDef { defName = "RecoveryMana", needClass = typeof(Need_Mana), onlyIfCausedByHediff = true };
            var hd = new HediffDef { defName = "RecoveryMagicUser", stages = [new HediffStage { enablesNeeds = [need] }] };
            p.pawn.health.hediffSet.hediffs.Add(new Hediff { pawn = p.pawn, def = hd });
            typeof(HediffSet).GetMethod("CacheNeeds", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(p.pawn.health.hediffSet, null);
            TM_LegacyRecovery.RestoreNeed(p.pawn, hd, need);
            var mana = Check.NotNull(p.pawn.needs.TryGetNeed(need), "mana created through real NeedsTracker policy"); mana.CurLevel = 0;
            TM_LegacyRecovery.RestoreNeed(p.pawn, hd, need);
            Check.Same(mana, p.pawn.needs.TryGetNeed(need), "existing mana object retained");
            Check.Equal(0f, mana.CurLevel, "zero mana is not refilled by recovery");

            var oldNeed = TorannMagicDefOf.TM_Mana; var oldHd = TorannMagicDefOf.TM_MagicUserHD;
            try
            {
                TorannMagicDefOf.TM_Mana = need; TorannMagicDefOf.TM_MagicUserHD = hd;
                p.pawn.story.traits.allTraits.Add(new Trait(TorannMagicDefOf.InnerFire));
                p.pawn.AllComps.Clear(); TM_Calc.InvalidateCompCache(p.pawn);
                var output = new System.Collections.Concurrent.ConcurrentQueue<(string Level, string Text)>();
                using var logs = LoggingCompatTests.Configure(null, null, output);
                TorannMagic.ModOptions.TM_DebugTools.RepairMissingClassData(p.pawn);
                File.WriteAllLines(Path.Combine(f.WorkingDirectory, "manual-repair.log"), output.Select(x => x.Level + " " + x.Text));
                var report = output.Where(x => x.Text.Contains("Repair Missing Class Data;")).ToArray();
                Check.Equal(2, report.Length, "manual repair logs start and result exactly once alongside component diagnostics");
                Check.Equal(true, report.First().Text.Contains("status=started"), "manual repair start is observable");
                Check.Equal(true, report.Last().Text.Contains("status=completed") && report.Last().Text.Contains("before:") && report.Last().Text.Contains("after:"), "completed repair includes before and after state");
                Check.Equal(true, report.Last().Text.Contains("magic=missing") && report.Last().Text.Contains("mana=0"), "report distinguishes missing component and existing empty mana");
                var restored = Check.NotNull(p.pawn.GetCompAbilityUserMagic(), "full repair injects missing comp");
                Check.Equal(true, restored.MagicData.MagicPowersIF[0].learned, "absent data gets baseline class spell");
                Check.Equal(0, restored.MagicData.MagicUserLevel, "missing level initialized to zero");
                output.Clear(); TorannMagic.ModOptions.TM_DebugTools.RepairMissingClassData(p.pawn);
                Check.Equal(true, output.Last().Text.Contains("status=completed") && output.Last().Text.Contains("summaryChanged=False"), "repeat confirms completion with unchanged summary");
                Check.Equal(1, p.pawn.AllComps.OfType<CompAbilityUserMagic>().Count(), "no duplicate comp on retry");
                Check.Same(mana, p.pawn.needs.TryGetNeed(need), "full repair retains existing mana");
                Check.Equal(0f, mana.CurLevel, "full repair does not refill mana");
            }
            finally { TorannMagicDefOf.TM_Mana = oldNeed; TorannMagicDefOf.TM_MagicUserHD = oldHd; }
        });
        yield return new("LegacyRecovery.UnclassedPawnUntouched", f =>
        {
            Prepare(f); var p = PawnWithComps(false, false); Set(p.magic, "magicData", null!);
            TM_LegacyRecovery.Repair(p.pawn);
            Check.Same(null!, p.magic.MagicData, "no class means no invented progression");
            Check.Equal(0, p.pawn.needs.AllNeeds.Count, "no invented mana");
        });
    }
    static void Prepare(RuntimeFixture f)
    {
        RuntimeLog.EnsureHeadlessDefOfInitialized(typeof(NeedDefOf));
        LoadBaseDefinitions(f); TM_ClassUtility.CustomClasses = []; TM_ClassUtility.CustomAdvancedClasses.Clear();
        foreach (var a in DefDatabase<TMAbilityDef>.AllDefsListForReading) { a.abilityClass = typeof(PawnAbility); a.shouldInitialize = true; }
    }
    sealed class GameScope : IDisposable
    {
        readonly Game previous = Current.Game;
        public GameScope()
        {
            Current.Game = (Game)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Game));
            Set(Current.Game, "tickManager", new TickManager());
        }
        public void Dispose() => Current.Game = previous;
    }
}
