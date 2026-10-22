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
            return Run(repo, work, filter);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"HARNESS ERROR: {exception}");
            return 2;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string repo, string work, string filter)
    {
        RuntimeFixture.Initialize();
        GrimoireTests.Initialize(repo);
        AutocastTests.Initialize(repo);
        DamageTests.Initialize();
        SelfCastTests.Initialize();
        CastSafetyTests.Initialize();
        FleshTargetingTests.Initialize();
        FleshBiteTests.Initialize();
        UndeadReleaseTests.Initialize();
        var tests = MagicDataTests.Cases().Concat(AutocastTests.Cases()).Concat(CombatLogTests.Cases()).Concat(GrimoireTests.Cases()).Concat(InitializationTests.Cases()).Concat(MagicShieldTests.Cases()).Concat(DamageTests.Cases()).Concat(SelfCastTests.Cases()).Concat(AbilityProjectileTests.Cases()).Concat(CastSafetyTests.Cases()).Concat(FleshProgressTests.Cases()).Concat(FleshUiTests.Cases()).Concat(FleshPresentationTests.Cases()).Concat(FleshTargetingTests.Cases()).Concat(FleshGameplayTests.Cases()).Concat(FleshHealthEffectsTests.Cases()).Concat(FleshCastingTests.Cases()).Concat(FleshStatTests.Cases()).Concat(FleshBiteTests.Cases()).Concat(UndeadReleaseTests.Cases()).Where(test => test.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        if (tests.Count == 0)
            throw new ArgumentException($"No tests match '{filter}'.");
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

    public static T NotNull<T>(T? actual, string message) where T : class =>
        actual ?? throw new InvalidOperationException(message + ": got null.");

    public static void Same(object expected, object? actual, string message)
    {
        if (!ReferenceEquals(expected, actual))
            throw new InvalidOperationException(message + ": not the same object.");
    }
}
