// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Shared harness: assertions, approximate comparison and a clean slate (managed node host, fresh tree, captured log) per test.

using Aver.Fabric;
using Aver.Scripting;

static class T
{
    public static readonly List<(Log.Level Level, string Message)> Logs = new();
    private static int s_failures;
    private static int s_checks;

    public static int Checks => s_checks;

    /// <summary>A clean slate: the managed node host, a new scene tree, an empty deferred queue and a captured log.</summary>
    public static void Reset()
    {
        NodeHost.Current = new ManagedNodeHost();
        SceneTree.Reset();
        Aver.Framework.Hosting.ExtensionHost.ResetDeferred();
        Logs.Clear();
        Rec.Trail.Clear();
        Log.SetSink((l, m) => Logs.Add((l, m)));
        GD.PrintSink = null;
        GD.ErrorSink = null;
        ResourceLoader.ClearCache();
        ResourceLoader.Provider = null;
        ResourceLoader.ContentRootOverride = null;
    }

    /// <summary>Runs one frame of the managed loop: <paramref name="physicsSteps"/> fixed steps, then the process pass.</summary>
    public static void Frame(double dt = 1.0 / 60.0, int physicsSteps = 0) => SceneTree.Instance.RunManagedFrame(dt, physicsSteps);

    public static void Frames(int count, double dt, int physicsStepsPerFrame = 0)
    {
        for (int i = 0; i < count; i++) Frame(dt, physicsStepsPerFrame);
    }

    /// <summary>The folder of the hand-written Godot-text and level fixtures (tests/fabric/data), found above the test's output folder.</summary>
    public static string DataDir()
    {
        for (DirectoryInfo? d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            string p = Path.Combine(d.FullName, "tests", "fabric", "data");
            if (Directory.Exists(p)) return p;
        }
        throw new DirectoryNotFoundException("tests/fabric/data was not found above " + AppContext.BaseDirectory);
    }

    private static string? BakedMetadata(string key) =>
        typeof(T).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .Cast<System.Reflection.AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value;

    /// <summary>The Aver Engine checkout this build uses: AVER_ENGINE_DIR, else the path the project file baked in (AverEngineDir).</summary>
    public static string EngineDir()
    {
        string? dir = System.Environment.GetEnvironmentVariable("AVER_ENGINE_DIR");
        if (string.IsNullOrEmpty(dir)) dir = BakedMetadata("AverEngineDir");
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(Path.Combine(dir, "scripting", "csharp", "Aver.Framework")))
            throw new DirectoryNotFoundException("the Aver Engine checkout was not found (AVER_ENGINE_DIR, or AverEngineDir at build time): " + dir);
        return dir;
    }

    /// <summary>The CMake build's bin folders to look in, best first: AVER_BIN_DIR, the AverBinDir the project file baked in, then a build/bin above the test's output folder.</summary>
    public static IEnumerable<string> BinDirs()
    {
        string? env = System.Environment.GetEnvironmentVariable("AVER_BIN_DIR");
        if (!string.IsNullOrEmpty(env)) yield return env;
        string? baked = BakedMetadata("AverBinDir");
        if (!string.IsNullOrEmpty(baked)) yield return baked;
        for (DirectoryInfo? d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            yield return Path.Combine(d.FullName, "build", "bin");
    }

    /// <summary>The engine's AverAssetC from the build's bin folder, or null when the build has none.</summary>
    public static string? AssetCExe()
    {
        string name = "AverAssetC" + (OperatingSystem.IsWindows() ? ".exe" : "");
        foreach (string bin in BinDirs())
            if (File.Exists(Path.Combine(bin, name))) return Path.Combine(bin, name);
        return null;
    }

    /// <summary>The extension's runtime test content (test-content/FabricRuntime/Content), found above the fixtures folder.</summary>
    public static string TestContentDir()
    {
        for (DirectoryInfo? d = new DirectoryInfo(DataDir()); d != null; d = d.Parent)
        {
            string p = Path.Combine(d.FullName, "test-content", "FabricRuntime", "Content");
            if (Directory.Exists(p)) return p;
        }
        throw new DirectoryNotFoundException("test-content/FabricRuntime/Content was not found above " + DataDir());
    }

    public static bool Near(float a, float b, float eps = 1e-4f) => MathF.Abs(a - b) <= eps;
    public static bool Near(Vector3 a, Vector3 b, float eps = 1e-4f) => Near(a.X, b.X, eps) && Near(a.Y, b.Y, eps) && Near(a.Z, b.Z, eps);
    public static bool Near(Quaternion a, Quaternion b, float eps = 1e-4f) =>
        (Near(a.X, b.X, eps) && Near(a.Y, b.Y, eps) && Near(a.Z, b.Z, eps) && Near(a.W, b.W, eps)) ||
        (Near(a.X, -b.X, eps) && Near(a.Y, -b.Y, eps) && Near(a.Z, -b.Z, eps) && Near(a.W, -b.W, eps));

    public static int ErrorCount(string fragment = "") => Logs.Count(l => l.Level == Log.Level.Error && l.Message.Contains(fragment));

    public static void Check(string what, bool ok, string detail = "")
    {
        s_checks++;
        if (ok) { Console.WriteLine($"  PASS: {what}"); return; }
        Console.WriteLine($"  FAIL: {what}{(detail.Length > 0 ? " -- " + detail : "")}");
        s_failures++;
    }

    public static void CheckVec(string what, Vector3 expected, Vector3 actual, float eps = 1e-4f) =>
        Check(what, Near(expected, actual, eps), $"expected {expected}, got {actual}");

    public static void CheckNum(string what, float expected, float actual, float eps = 1e-4f) =>
        Check(what, Near(expected, actual, eps), $"expected {expected}, got {actual}");

    public static void CheckSeq<TItem>(string what, IEnumerable<TItem> expected, IEnumerable<TItem> actual) =>
        Check(what, expected.SequenceEqual(actual), $"expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]");

    public static void Run(string name, Action test)
    {
        Console.WriteLine($"Test: {name}");
        Reset();
        try { test(); }
        catch (Exception ex)
        {
            Console.WriteLine($"  FAIL: threw {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            s_failures++;
        }
    }

    /// <summary>Runs a file's tests and returns how many checks failed in them.</summary>
    public static int Suite(Action tests)
    {
        int before = s_failures;
        tests();
        return s_failures - before;
    }
}
