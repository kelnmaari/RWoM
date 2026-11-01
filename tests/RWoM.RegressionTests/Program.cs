using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;

namespace RWoM.RegressionTests;

internal static class Program
{
    private static int Main(string[] args)
    {
        // Install resolution before JIT-compiling any method that mentions game/mod types.
        string repo = Option(args, "--repo");
        string game = Option(args, "--game");
        string jecs = Option(args, "--jecs");
        string mod = Option(args, "--mod");
        string work = Option(args, "--work");
        string filter = Option(args, "--filter", "");
        string[] search = [Path.GetDirectoryName(mod)!, jecs, game, Option(args, "--refs"), Option(args, "--harmony")];
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            string? path = name.Name == "TorannMagic" ? mod : search
                .Select(directory => Path.Combine(directory, name.Name + ".dll"))
                .FirstOrDefault(File.Exists);
            return path == null ? null : context.LoadFromAssemblyPath(Path.GetFullPath(path));
        };
        Console.WriteLine($"Production DLL: {mod}");
        Console.WriteLine($"SHA256: {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(mod)))}");
        Console.WriteLine($"Game: {File.ReadAllText(Path.Combine(game, "Version.txt")).Trim()}");
        Console.WriteLine($"Assembly-CSharp: {AssemblyName.GetAssemblyName(Path.Combine(game, "Assembly-CSharp.dll"))}");
        Console.WriteLine($"JecsLite: {jecs}");
        Console.WriteLine($"Artifacts: {work}");
        try
        {
            return Run(repo, work, filter, args.Contains("--reverse"));
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"HARNESS ERROR: {exception}");
            return 2;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string repo, string work, string filter, bool reverse)
    {
        // The regression suite exercises the beta progression, so its kill switch must
        // be on before any TorannMagic ClassProgression type is JIT-compiled. The
        // disabled-mode contract has dedicated cases that toggle the flag locally.
        TorannMagic.ClassProgression.ClassProgressionFeature.Enabled = true;
        RuntimeFixture.Initialize();
        GrimoireTests.Initialize(repo);
        AutocastTests.Initialize(repo);
        DamageTests.Initialize();
        SelfCastTests.Initialize();
        CastSafetyTests.Initialize();
        FleshTargetingTests.Initialize();
        FleshBiteTests.Initialize();
        UndeadReleaseTests.Initialize();
        GrimoireProgressionTests.Initialize();
        AutocastCombatTests.Initialize();
        var tests = SpiritTerminalLossTests.Cases().Concat(SpiritMapCollapseTests.Cases()).Concat(SpiritCaravanLossNativeTests.Cases()).Concat(SpiritRebirthHealthTransferTests.Cases()).Concat(SpiritRebirthRecoveryJobTests.Cases()).Concat(SpiritRebirthRitualJobTests.Cases()).Concat(SpiritRebirthReleaseTests.Cases()).Concat(SpiritRebirthCommandTests.Cases()).Concat(SpiritRebirthRuntimeTests.Cases()).Concat(SpiritRebirthActionTests.Cases()).Concat(SpiritRebirthHealthFormTests.Cases()).Concat(SpiritRebirthBodyEffectsTests.Cases()).Concat(SpiritRebirthBodyTests.Cases()).Concat(SpiritRebirthEnergyTests.Cases()).Concat(SoulStoneMapCustodyTests.Cases()).Concat(SpiritRebirthFormTests.Cases()).Concat(SpiritRebirthRitualTransactionTests.Cases()).Concat(SoulStoneDefinitionTests.Cases()).Concat(SpiritRebirthHealthPlanTests.Cases()).Concat(SpiritRebirthRitualEligibilityTests.Cases()).Concat(SpiritNativeResurrectionTests.Cases()).Concat(SoulStoneHolderTests.Cases()).Concat(SpiritRebirthCastingTests.Cases()).Concat(SpiritRebirthStateTests.Cases()).Concat(EmpathAscensionTests.Cases()).Concat(EmpathProgressionTests.Cases()).Concat(EmpathLifecycleTests.Cases()).Concat(EmpathTrialTests.Cases()).Concat(MimicProgressionTests.Cases()).Concat(GeomancerTrialTests.Cases()).Concat(GeomancerProgressionTests.Cases()).Concat(GeomancerAscensionTests.Cases()).Concat(TechnomancerAscensionTests.Cases()).Concat(BloodMageAscensionTests.Cases()).Concat(EnchanterAscensionTests.Cases()).Concat(ChronomancerAscensionTests.Cases()).Concat(BrightmageAscensionTests.Cases()).Concat(ShamanAscensionTests.Cases()).Concat(GolemancerAscensionTests.Cases()).Concat(GolemancerProgressionTests.Cases()).Concat(GolemancerTrialTests.Cases()).Concat(GolemancerLifecycleTests.Cases()).Concat(ShamanProgressionTests.Cases()).Concat(ShamanTrialTests.Cases()).Concat(ShamanLifecycleTests.Cases()).Concat(BrightmageProgressionTests.Cases()).Concat(BrightmageTrialTests.Cases()).Concat(BrightmageLifecycleTests.Cases()).Concat(ChronomancerProgressionTests.Cases()).Concat(ChronomancerTrialTests.Cases()).Concat(ChronomancerLifecycleTests.Cases()).Concat(EnchanterProgressionTests.Cases()).Concat(EnchanterTrialTests.Cases()).Concat(EnchanterLifecycleTests.Cases()).Concat(BloodMageProgressionTests.Cases()).Concat(BloodMageTrialTests.Cases()).Concat(BloodMageResourceTests.Cases()).Concat(TechnomancerProgressionTests.Cases()).Concat(TechnomancerTrialTests.Cases()).Concat(TechnomancerLifecycleTests.Cases()).Concat(ClassInitiationJobTests.Cases()).Concat(DemonOwnershipTests.Cases()).Concat(DemonTrialTests.Cases()).Concat(WarlockProgressionTests.Cases()).Concat(SuccubusProgressionTests.Cases()).Concat(WarlockAscensionTests.Cases()).Concat(SuccubusAscensionTests.Cases()).Concat(DruidTrialTests.Cases()).Concat(BardTrialTests.Cases()).Concat(DruidProgressionTests.Cases()).Concat(BardProgressionTests.Cases()).Concat(DruidAscensionTests.Cases()).Concat(BardAscensionTests.Cases()).Concat(PaladinTrialTests.Cases()).Concat(SummonerTrialTests.Cases()).Concat(PaladinProgressionTests.Cases()).Concat(SummonerProgressionTests.Cases()).Concat(PaladinAscensionTests.Cases()).Concat(SummonerAscensionTests.Cases()).Concat(ArcanistTrialTests.Cases()).Concat(ArcanistProgressionTests.Cases()).Concat(ArcanistAscensionTests.Cases()).Concat(LightningTrialTests.Cases()).Concat(LightningProgressionTests.Cases()).Concat(LightningAscensionTests.Cases()).Concat(IceTrialTests.Cases()).Concat(IceAttunementTests.Cases()).Concat(IceProgressionTests.Cases()).Concat(IceAscensionTests.Cases()).Concat(SharedClassScrollTests.Cases()).Concat(ClassTomeOfferTests.Cases()).Concat(ClassTomeRaidTests.Cases()).Concat(FireclawTrialTests.Cases()).Concat(FireAscensionTests.Cases()).Concat(FireProgressionTests.Cases()).Concat(LichTransformationTests.Cases()).Concat(LichPassiveTests.Cases()).Concat(NecromancerAscensionTests.Cases()).Concat(NecromancerDeathMarkTrialTests.Cases().Concat(NecromancerControlTrialTests.Cases()).Concat(ServantControlProgressTests.Cases()).Concat(NecromancerFoundationTests.Cases()).Concat(NecromancerIdentityTests.Cases()).Concat(ClassAscensionJobTests.Cases()).Concat(MasterTomeDefinitionTests.Cases()).Concat(PriestPurificationTrialTests.Cases()).Concat(PriestHealingTrialTests.Cases()).Concat(ClassSpellRestoreTests.Cases()).Concat(ClassEnrollmentTests.Cases()).Concat(ClassPassiveTests.Cases()).Concat(ClassInitiationTests.Cases()).Concat(BloodTimingTests.Cases()).Concat(ElementalDefinitionTests.Cases()).Concat(ElementalVisualTests.Cases().Concat(OdysseyBeaconTests.FuelReserveCases()).Concat(OdysseyBeaconTests.PriorityCases()).Concat(StellarObservatoryDefinitionTests.Cases()).Concat(PridestalkerCompatibilityTests.Cases()).Concat(StellarObservatoryEffectsTests.Cases()).Concat(StellarObservatoryTests.Cases()).Concat(OdysseyBeaconTests.StellarPoolCases()).Concat(OdysseyGrandDomeTests.StellarRitualCases()).Concat(OdysseyBeaconTests.StellarSaveCases()).Concat(OdysseyBeaconTests.StellarLifecycleCases()).Concat(OdysseyBeaconTests.StellarPaymentCases()).Concat(EtherMovementDoorTests.Cases()).Concat(EtherMovementTests.Cases()).Concat(EtherWorkBehaviorTests.Cases()).Concat(EtherServantWorkTests.Cases()).Concat(EtherGateTests.Cases()).Concat(EtherCaravanTests.Cases()).Concat(TeleportSpiritTests.CoreReturnCases()).Concat(TeleportSpiritTests.ProductionBuildingEffectsCases()).Concat(TeleportSpiritTests.LegacyBuildingEffectsCases()).Concat(OdysseyBeaconTests.ExtenderEffectCases()).Concat(ThrusterEffectsTests.Cases()).Concat(OdysseyPowerTests.GlowCases()).Concat(OdysseyGrandDomeTests.GlowCases()).Concat(CrystalGlowTests.Cases()).Concat(OdysseyBeaconTests.GlowCases()).Concat(OdysseyCombatTests.GlowCases()).Concat(MagicGuidanceTests.Cases()).Concat(SpellItemEffectTests.FloatMenuCases()).Concat(BreachSealDefinitionTests.Cases()).Concat(SpellItemEffectTests.BreachSealCases()).Concat(EtherShellDefinitionTests.Cases()).Concat(OdysseyBeaconTests.SiphonCases()).Concat(OdysseySiphonDefinitionTests.Cases()).Concat(MagicalSurfaceStartTests.Cases()).Concat(OdysseyMagicalScenarioDefinitionTests.Cases()).Concat(MagicalStarterPlacementTests.Cases().Concat(MagicalStarterShipTests.Cases())).Concat(MagicDataTests.Cases()).Concat(AutocastTests.Cases()).Concat(CombatLogTests.Cases()).Concat(GrimoireTests.Cases()).Concat(InitializationTests.Cases()).Concat(MagicShieldTests.Cases()).Concat(MagicalCrewTests.Cases()).Concat(DamageTests.Cases()).Concat(SelfCastTests.Cases()).Concat(AbilityProjectileTests.RoofCases().Concat(AbilityProjectileTests.Cases())).Concat(CastSafetyTests.Cases()).Concat(FleshProgressTests.Cases()).Concat(FleshUiTests.Cases()).Concat(FleshPresentationTests.Cases()).Concat(FleshTargetingTests.Cases()).Concat(FleshGameplayTests.Cases()).Concat(FleshHealthEffectsTests.Cases()).Concat(FleshCastingTests.Cases()).Concat(FleshStatTests.Cases()).Concat(FleshBiteTests.Cases()).Concat(UndeadReleaseTests.Cases()).Concat(GolemEnergyTests.Cases()).Concat(SpiritClassTests.Cases()).Concat(ClassGroupTests.Cases()).Concat(CompBootstrapTests.Cases()).Concat(GrimoireProgressionTests.Cases()).Concat(GrimoireKureTests.Cases()).Concat(SpellItemTests.Cases()).Concat(SpellItemCraftingTests.Cases()).Concat(MagicyteOreTests.Cases()).Concat(MagicyteGenerationTests.Cases()).Concat(OdysseyMagofuelTests.Cases()).Concat(OdysseyReservoirTests.Cases()).Concat(OdysseyReservoirDefinitionTests.Cases()).Concat(OdysseyReservoirTransportTests.Cases()).Concat(OdysseyPowerDefinitionTests.Cases()).Concat(OdysseyPowerTests.Cases()).Concat(OdysseyBeaconTests.Cases()).Concat(OdysseyOxygenTests.Cases()).Concat(OdysseyVgeOxygenTests.Cases()).Concat(OdysseyThrusterContractTests.Cases()).Concat(OdysseyThrusterDefinitionTests.Cases()).Concat(OdysseyBeaconTests.NativeThrusterCases()).Concat(OdysseyBeaconTests.GravcoreCases()).Concat(OdysseyBeaconTests.GravcoreVgeCases()).Concat(OdysseyGravcoreDefinitionTests.Cases()).Concat(OdysseyBeaconTests.VgeLaunchCases()).Concat(OdysseyBeaconTests.LifecycleCases()).Concat(OdysseyAtmosphereTests.Cases()).Concat(OdysseyGrandDomeTests.Cases()).Concat(OdysseyGrandDomeDefinitionTests.Cases()).Concat(OdysseyCombatTests.Cases()).Concat(OdysseyCombatBeamTests.Cases()).Concat(OdysseyCombatWarpodTests.Cases()).Concat(OdysseyCombatDefinitionTests.Cases()).Concat(OdysseyThermalTests.Cases()).Concat(OdysseyThermalDefinitionTests.Cases()).Concat(OdysseyBeaconDefinitionTests.Cases()).Concat(OdysseyNetworkOverlayTests.Cases()).Concat(OdysseyOxygenDefinitionTests.Cases()).Concat(SpellItemEffectTests.OdysseyCreatureCases()).Concat(SpellItemEffectTests.Cases()).Concat(LegacyDataRefreshTests.Cases()).Concat(LegacyRecoveryTests.Cases()).Concat(LoggingCompatTests.Cases()).Concat(TeleportSpiritTests.Cases()).Concat(ClassIdentityTests.Cases().Concat(ClassStateTests.Cases())).Concat(ChaosProgressionTests.Cases()).Concat(AutocastCombatTests.Cases()).Concat(VpeAutocastTests.Cases()).Concat(ChromaProgressionTests.Cases()).Concat(TeleportCameraCoverageTests.Cases()).Concat(ClassProgressionKillSwitchTests.Cases()).Concat(StaggerPatchTests.Cases()))).Where(test => filter.Split(',').Any(part => test.Name.Contains(part.Trim(), StringComparison.OrdinalIgnoreCase))).ToList();
        if (tests.Count == 0)
            throw new ArgumentException($"No tests match '{filter}'.");
        if (reverse) tests.Reverse();
        int failures = 0;
        foreach (var test in tests)
        {
            var fixture = new RuntimeFixture(repo, Path.Combine(work, test.Name));
            try
            {
                RuntimeLog.Clear();
                fixture.ResetDefinitions();
                test.Body(fixture);
                RuntimeLog.AssertClean();
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL {test.Name}: {exception.GetType().Name}: {exception.Message}");
                Console.Error.WriteLine(exception);
            }
            finally
            {
                Verse.Scribe.ForceStop();
            }
        }
        Console.WriteLine($"RESULT: {tests.Count - failures} passed, {failures} failed, {tests.Count} total");
        return failures == 0 ? 0 : 1;
    }

    private static string Option(string[] args, string name, string? fallback = null)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1]
            : fallback ?? throw new ArgumentException($"Missing {name}.");
    }
}

internal sealed record TestCase(string Name, Action<RuntimeFixture> Body);

internal static class Check
{
    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}.");
    }

    public static void NotEqual<T>(T notExpected, T actual, string message)
    {
        if (EqualityComparer<T>.Default.Equals(notExpected, actual))
            throw new InvalidOperationException($"{message}: unexpectedly {actual}.");
    }

    public static T NotNull<T>(T? actual, string message) where T : class =>
        actual ?? throw new InvalidOperationException(message + ": got null.");

    public static void Same(object expected, object? actual, string message)
    {
        if (!ReferenceEquals(expected, actual))
            throw new InvalidOperationException(message + ": not the same object.");
    }
}
