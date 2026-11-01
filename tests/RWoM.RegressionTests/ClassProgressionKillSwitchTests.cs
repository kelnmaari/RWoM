using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using RimWorld;
using TorannMagic;
using TorannMagic.ClassProgression;
using Verse;
using static RWoM.RegressionTests.GrimoireProgressionTests;

namespace RWoM.RegressionTests;

// Contract for the "new progression (beta)" kill switch: with
// ClassProgressionFeature.Enabled = false the mod must behave like the classic
// system. Program.Main enables the flag for the whole suite, so every case here
// disables it locally and restores it via a finally block.
internal static class ClassProgressionKillSwitchTests
{
    public static IEnumerable<TestCase> Cases()
    {
        yield return new("KillSwitch.EnsureCompSkipsInjection", EnsureCompSkips);
        yield return new("KillSwitch.IdentityFallsBackToClassicTraits", IdentityFallback);
        yield return new("KillSwitch.AuraChainAppliesClassicSeverity", AuraSeverity);
        yield return new("KillSwitch.GravitySlowStillApplied", GravitySlow);
        yield return new("KillSwitch.WeatherTransitionStillNative", WeatherNative);
        yield return new("KillSwitch.SpellAccessClassicDefaults", SpellAccessDefaults);
    }

    private sealed class Flag : IDisposable
    {
        public Flag() => ClassProgressionFeature.Enabled = false;
        public void Dispose() => ClassProgressionFeature.Enabled = true;
    }

    // Minimal game shell (same pattern as IceTrialTests.Fixture) so HealthUtility
    // and the weather manager can run headless.
    private sealed class GameEnv : IDisposable
    {
        readonly Game previous = Current.Game;
        public GameEnv()
        {
            Current.Game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
            Set(Current.Game, "tickManager", new TickManager());
            Set(Current.Game, "uniqueIDsManager", new UniqueIDsManager());
            Set(Find.TickManager, "gameStartAbsTick", 1);
            Set(Current.Game, "battleLog", new BattleLog());
            var world = (RimWorld.Planet.World)RuntimeHelpers.GetUninitializedObject(typeof(RimWorld.Planet.World));
            world.factionManager = new FactionManager();
            Set(Current.Game, "worldInt", world);
            Set(Current.Game, "maps", new List<Map> { (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map)) });
        }
        public void Dispose() => Current.Game = previous;
    }

    private static ThingDef HumanDef()
    {
        var def = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        def.defName = "KillSwitchHuman";
        def.race = new RaceProperties { intelligence = Intelligence.Humanlike };
        return def;
    }

    private static bool HasProgressionComp(ThingDef def)
        => def.comps?.Any(c => c?.compClass == typeof(CompClassProgression)) == true;

    private static void EnsureCompSkips(RuntimeFixture f)
    {
        LoadBaseDefinitions(f);
        // Positive control: with the suite default (enabled) the comp is injected.
        var enabledDef = HumanDef();
        ClassProgressionBootstrap.EnsureComp(enabledDef);
        Check.Equal(true, HasProgressionComp(enabledDef), "enabled feature injects the progression comp");

        using (new Flag())
        {
            var def = HumanDef();
            ClassProgressionBootstrap.EnsureComp(def);
            Check.Equal(false, HasProgressionComp(def), "disabled feature must not inject the progression comp");
        }
        Check.Equal(true, ClassProgressionFeature.Enabled, "flag must be restored after the case");
    }

    private static void IdentityFallback(RuntimeFixture f)
    {
        LoadBaseDefinitions(f);
        var traitPawn = PawnWithComps(true, false).pawn;
        traitPawn.story.traits.allTraits.Add(new Trait(TorannMagicDefOf.Priest));

        // No progression state anywhere: the classic trait fallback applies either way.
        Check.Equal(true, ClassIdentity.IsPriest(traitPawn), "trait fallback works while enabled");
        using (new Flag())
        {
            Check.Equal(true, ClassIdentity.IsPriest(traitPawn), "trait fallback preserved while disabled");
        }

        // A pawn with ONLY progression state (no legacy trait) must resolve classic
        // (not a priest) while disabled, and as a priest again once enabled.
        var statePawn = PawnWithComps(true, false).pawn;
        var progression = new CompClassProgression { parent = statePawn };
        statePawn.AllComps.Add(progression);
        progression.BeginNewProgression(Vocation.Mage);
        progression.State.TryAddFoundation("rwom.priest", DisciplineDomain.Magic);
        Check.Equal(true, ClassIdentity.IsPriest(statePawn), "state counts while enabled");
        using (new Flag())
        {
            Check.Equal(false, ClassIdentity.IsPriest(statePawn), "disabled flag ignores progression state");
        }
        Check.Equal(true, ClassIdentity.IsPriest(statePawn), "state counts again once the flag is restored");
    }

    private static void AuraSeverity(RuntimeFixture f)
    {
        LoadBaseDefinitions(f);
        var previousAura = TorannMagicDefOf.TM_SoothingBreeze_AuraHD;
        var previousEffect = TorannMagicDefOf.SoothingBreeze;
        try
        {
            TorannMagicDefOf.TM_SoothingBreeze_AuraHD = new HediffDef { defName = "KillSwitchAura", hediffClass = typeof(Hediff) };
            TorannMagicDefOf.SoothingBreeze = new HediffDef { defName = "KillSwitchSoothing", hediffClass = typeof(Hediff), maxSeverity = 1 };
            using var env = new GameEnv();
            var caster = PawnWithComps(true, false).pawn;  // no progression comp: the trial path is null
            var target = PawnWithComps(false, false).pawn;
            // A detached source hediff: no aura tag exists, so the trial path is null
            // and only the classic mutation must run.
            var source = new Hediff { def = TorannMagicDefOf.TM_SoothingBreeze_AuraHD, pawn = caster };
            var effect = new Hediff { def = TorannMagicDefOf.SoothingBreeze, pawn = target };
            target.health.hediffSet.hediffs.Add(effect);
            Set(effect, "severityInt", 0f);
            using (new Flag())
            {
                // The chain head runs: the classic mutation inside IceTrials.ApplyAura
                // must still fire even though no trial can be recorded.
                PaladinTrials.ApplyAura(source, target, effect.def);
            }
            Check.Equal(1f, effect.Severity, "classic aura severity still applied while disabled");
        }
        finally
        {
            TorannMagicDefOf.TM_SoothingBreeze_AuraHD = previousAura;
            TorannMagicDefOf.SoothingBreeze = previousEffect;
        }
    }

    private static void GravitySlow(RuntimeFixture f)
    {
        LoadBaseDefinitions(f);
        var previousSlow = TorannMagicDefOf.TM_GravitySlowHD;
        try
        {
            TorannMagicDefOf.TM_GravitySlowHD = new HediffDef { defName = "KillSwitchGravity", hediffClass = typeof(Hediff), maxSeverity = 1 };
            using var env = new GameEnv();
            var victim = PawnWithComps(false, false).pawn;
            var slow = new Hediff { def = TorannMagicDefOf.TM_GravitySlowHD, pawn = victim };
            victim.health.hediffSet.hediffs.Add(slow);
            Set(slow, "severityInt", 0f);
            using (new Flag())
            {
                DemonTrials.AdjustSlow(null, victim, 0.4f);
            }
            Check.Equal(0.4f, slow.Severity, "classic gravity slow severity still applied while disabled");
        }
        finally { TorannMagicDefOf.TM_GravitySlowHD = previousSlow; }
    }

    private static void WeatherNative(RuntimeFixture f)
    {
        LoadBaseDefinitions(f);
        RuntimeLog.EnsureHeadlessDefOfInitialized(typeof(WeatherDefOf));
        var clear = new WeatherDef { defName = "KillSwitchClear" };
        var rain = new WeatherDef { defName = "KillSwitchRain" };
        using var env = new GameEnv();
        var caster = PawnWithComps(true, false).pawn;
        var map = Find.Maps[0];
        map.weatherManager = new WeatherManager(map) { curWeather = clear };
        using (new Flag())
        {
            IceTrials.TransitionWeather(caster, map, rain);
        }
        Check.Same(rain, map.weatherManager.curWeather, "native weather transition still runs while disabled");
    }

    private static void SpellAccessDefaults(RuntimeFixture f)
    {
        LoadBaseDefinitions(f);
        var (pawn, magic, _) = PawnWithComps(true, false);
        var power = magic.MagicData.AllMagicPowersWithSkills.First(p => !p.learned);
        power.learned = false;
        using (new Flag())
        {
            Check.Equal(true, ClassSpellAccess.CanLearn(pawn, power), "disabled flag keeps classic learnability");
            Check.Equal(false, ClassSpellAccess.RestoreLearned(magic), "disabled flag defers to the classic restore path");
        }
        Check.Equal(true, ClassSpellAccess.CanLearn(pawn, power), "learnability unchanged after restore");
    }
}
