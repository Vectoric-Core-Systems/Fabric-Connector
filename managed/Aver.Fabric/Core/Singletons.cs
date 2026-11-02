// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Engine, OS and ProjectSettings with Godot's names, over the G1 framework statics. Only what converted scripts reach for.

using System.Globalization;

namespace Aver.Fabric;

/// <summary>Godot's Engine singleton.</summary>
public static class Engine
{
    /// <summary>Always false: Aver runs no editor scripts, so a script's <c>is_editor_hint()</c> branch never runs.</summary>
    public static bool IsEditorHint() => false;

    // The objects scripts register by name (Engine.register_singleton). Godot's own singletons are static classes here, so get_singleton("Input")
    // has no object to give and answers null, as Godot does for a name it does not know.
    private static readonly Dictionary<string, GodotObject> s_singletons = new(StringComparer.Ordinal);

    public static void RegisterSingleton(StringName name, GodotObject instance) => s_singletons[name] = instance;
    public static void UnregisterSingleton(StringName name) => s_singletons.Remove(name);
    public static bool HasSingleton(StringName name) => s_singletons.ContainsKey(name);
    public static GodotObject? GetSingleton(StringName name) => s_singletons.TryGetValue(name, out GodotObject? o) ? o : null;
    public static string[] GetSingletonList() => s_singletons.Keys.ToArray();
    internal static void ResetSingletons() => s_singletons.Clear();

    public static double GetFramesPerSecond() => Aver.Framework.Engine.FramesPerSecond;
    public static long GetProcessFrames() => Aver.Framework.Engine.ProcessFrames;
    public static long GetPhysicsFrames() => Aver.Framework.Engine.PhysicsFrames;

    public static double TimeScale
    {
        get => Aver.Framework.Game.TimeScale;
        set => Aver.Framework.Game.TimeScale = (float)value;
    }

    private static int s_physicsTicks;

    /// <summary>The physics step rate: 1 / the engine's fixed step (the project's PHYSICS.FIXEDSTEP, 60 when it states none). Setting it changes the engine's
    /// fixed step (0.5 s at most, so 2 ticks a second at least). Unverified with bodies alive: change it before the first body is built.</summary>
    public static int PhysicsTicksPerSecond
    {
        get
        {
            if (s_physicsTicks > 0) return s_physicsTicks;
            try
            {
                if (Aver.Physics.Physics.Ready) return Math.Max(1, (int)Math.Round(1.0 / Aver.Physics.Physics.FixedStep));
            }
            catch (Exception ex) when (PhysicsClockNative.Missing(ex)) { }
            return (int)ProjectSettings.GetSetting("physics/common/physics_ticks_per_second", 60L).AsInt64();
        }
        set
        {
            if (value < 1) return;
            s_physicsTicks = value;
            try { Aver.Physics.Physics.SetFixedStep(1f / value); }
            catch (Exception ex) when (PhysicsClockNative.Missing(ex)) { }
        }
    }

    private static int s_maxSteps = 8;

    /// <summary>The most physics steps one frame runs to catch up (the engine's PHYSICS.MAXSTEPS, 1 to 64; a larger value is held at 64).</summary>
    public static int MaxPhysicsStepsPerFrame
    {
        get
        {
            try { if (Aver.Physics.Physics.Ready) return Aver.Physics.Physics.MaxSteps; }
            catch (Exception ex) when (PhysicsClockNative.Missing(ex)) { }
            return s_maxSteps;
        }
        set
        {
            if (value < 1) return;
            s_maxSteps = Math.Min(value, 64);
            try { Aver.Physics.Physics.SetMaxSteps(s_maxSteps); }
            catch (Exception ex) when (PhysicsClockNative.Missing(ex)) { }
        }
    }

    private static int s_maxFps;

    /// <summary>The frame rate cap, 0 for none. The shipped game's loop waits out the rest of 1 / fps after presenting, on top of whatever the present mode
    /// paces to (vsync); the editor ignores it. The project's <c>application/run/max_fps</c> sets it at start (WINDOW.MAXFPS).</summary>
    public static int MaxFps
    {
        get => s_maxFps;
        set
        {
            s_maxFps = Math.Max(0, value);
            if (!NodeHost.Current.IsEngine) return;
            try { Aver.Framework.Display.MaxFps = s_maxFps; }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        }
    }

    /// <summary>Godot's jitter fix for fixed steps. Stored, not applied: the engine's physics interpolation (PHYSICS.INTERPOLATION) does the smoothing.</summary>
    public static double PhysicsJitterFix { get; set; } = 0.5;

    /// <summary>Whether the engine prints errors from scripts. Stored, not applied: the log always carries them.</summary>
    public static bool PrintErrorMessages { get; set; } = true;

    /// <summary>The frames drawn so far: the engine draws once per process frame, so this is the process frame count.</summary>
    public static long GetFramesDrawn() => Aver.Framework.Engine.ProcessFrames;

    /// <summary>How far the physics clock is into the current step, 0 to 1: what a renderer blends the last two physics states by (Godot's
    /// get_physics_interpolation_fraction). 0 when the engine's physics is not running.</summary>
    public static double GetPhysicsInterpolationFraction()
    {
        try { return PhysicsClockNative.aver_phys_step_alpha(); }
        catch (Exception ex) when (PhysicsClockNative.Missing(ex)) { return 0.0; }
    }

    /// <summary>The Godot version whose API this runtime follows, as Godot's get_version_info gives it: major, minor, patch, hex, status, build, hash, timestamp and
    /// string. Aver is not Godot, so build is "aver" and the hash is empty.</summary>
    public static Collections.Dictionary GetVersionInfo() => new()
    {
        { "major", 4L }, { "minor", 5L }, { "patch", 0L }, { "hex", 0x040500L }, { "status", "stable" }, { "build", "aver" },
        { "hash", "" }, { "timestamp", 0L }, { "string", "4.5-stable (aver)" },
    };

    /// <summary>The processor architecture in Godot's spelling ("x86_64", "arm64" ...).</summary>
    public static string GetArchitectureName() => System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
    {
        System.Runtime.InteropServices.Architecture.X64 => "x86_64",
        System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
        System.Runtime.InteropServices.Architecture.X86 => "x86_32",
        System.Runtime.InteropServices.Architecture.Arm => "arm32",
        var other => other.ToString().ToLowerInvariant(),
    };
}

/// <summary>The one export of the physics library a script reaches directly: the position inside the current step.</summary>
internal static class PhysicsClockNative
{
    [System.Runtime.InteropServices.DllImport("Aver.Physics")] internal static extern float aver_phys_step_alpha();

    /// <summary>Whether an exception says the physics library is not there (a host without it: tests, tools) rather than that a call failed.</summary>
    internal static bool Missing(Exception ex) => ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException;
}

/// <summary>Godot's OS singleton.</summary>
public static class OS
{
    private static ulong s_staticPeak;

    public static string GetName() => Aver.Framework.OS.Name;
    public static bool HasFeature(string tagName)
    {
        try { return Aver.Framework.OS.HasFeature(tagName); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return tagName.ToLowerInvariant() is "pc" or "64"; }
    }
    public static ulong GetTicksMsec() => Time.GetTicksMsec();
    public static ulong GetTicksUsec() => Time.GetTicksUsec();
    public static bool IsDebugBuild() => Aver.Framework.OS.HasFeature("debug");
    public static string GetEnvironment(string variable) => System.Environment.GetEnvironmentVariable(variable) ?? "";
    public static bool HasEnvironment(string variable) => System.Environment.GetEnvironmentVariable(variable) != null;
    public static void SetEnvironment(string variable, string value) => System.Environment.SetEnvironmentVariable(variable, value);
    public static void UnsetEnvironment(string variable) => System.Environment.SetEnvironmentVariable(variable, null);

    /// <summary>The number of logical processors.</summary>
    public static int GetProcessorCount() => System.Environment.ProcessorCount;

    /// <summary>The CPU's model name (the registry on Windows, /proc/cpuinfo on Linux), or "" when the system does not say.</summary>
    public static string GetProcessorName()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return (Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", "") as string ?? "").Trim();
            if (OperatingSystem.IsLinux() && File.Exists("/proc/cpuinfo"))
                foreach (string line in File.ReadLines("/proc/cpuinfo"))
                    if (line.StartsWith("model name", StringComparison.Ordinal) && line.IndexOf(':') is int c and > 0) return line[(c + 1)..].Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        return "";
    }

    /// <summary>The name of a key, with its modifiers ("Ctrl+Shift+A", "Space", "Kp 5"), as Godot writes it.</summary>
    public static string GetKeycodeString(Key code) => KeyNames.Name(code);

    /// <summary>The key a name stands for (the inverse of <see cref="GetKeycodeString"/>, letter case ignored); <see cref="Key.None"/> when none does.</summary>
    public static Key FindKeycodeFromString(string @string) => KeyNames.Find(@string);

    /// <summary>True when the code is a character key (a Unicode code point), not one of the named special keys.</summary>
    public static bool IsKeycodeUnicode(long code) => code < (long)Key.Special;

    /// <summary>Moves a file or folder (an operating-system path) to the recycle bin. Windows only: elsewhere <see cref="Error.Unavailable"/>, nothing moved.</summary>
    public static Error MoveToTrash(string path)
    {
        if (!OperatingSystem.IsWindows()) return Error.Unavailable;
        try
        {
            if (Directory.Exists(path))
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            else if (File.Exists(path))
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            else return Error.FileNotFound;
            return Error.Ok;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException) { return Error.Failed; }
    }

    /// <summary>The bytes of managed memory in use: Godot's "static" memory is the engine's own allocations, and the managed heap is what this runtime holds.</summary>
    public static ulong GetStaticMemoryUsage()
    {
        ulong now = (ulong)Math.Max(0, GC.GetTotalMemory(false));
        if (now > s_staticPeak) s_staticPeak = now;
        return now;
    }

    /// <summary>The most <see cref="GetStaticMemoryUsage"/> has reported so far.</summary>
    public static ulong GetStaticMemoryPeakUsage()
    {
        GetStaticMemoryUsage();
        return s_staticPeak;
    }

    /// <summary>The folder user:// names: the player's writable folder for this project, created on first use.</summary>
    public static string GetUserDataDir() => FabricPaths.User;

    /// <summary>The folder the platform keeps per-user application data in (Godot's get_data_dir).</summary>
    public static string GetDataDir() => System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);

    /// <summary>The folder the platform keeps per-user configuration in (Godot's get_config_dir).</summary>
    public static string GetConfigDir() => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData)
        : System.Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".config");

    /// <summary>The folder the platform keeps per-user caches in (Godot's get_cache_dir).</summary>
    public static string GetCacheDir() => OperatingSystem.IsWindows()
        ? System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData)
        : OperatingSystem.IsMacOS() ? System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Library", "Caches")
        : System.Environment.GetEnvironmentVariable("XDG_CACHE_HOME") ?? System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".cache");

    public static string GetExecutablePath() => System.Environment.ProcessPath ?? "";

    public static int GetProcessId() => System.Environment.ProcessId;

    /// <summary>The caller's thread id.</summary>
    public static ulong GetThreadCallerId() => (ulong)System.Environment.CurrentManagedThreadId;

    /// <summary>The user's locale in Godot's spelling ("en_US"), "en" when the system gives none.</summary>
    public static string GetLocale()
    {
        string name = System.Globalization.CultureInfo.CurrentCulture.Name.Replace('-', '_');
        return name.Length == 0 ? "en" : name;
    }

    /// <summary>The language part of <see cref="GetLocale"/> ("en").</summary>
    public static string GetLocaleLanguage()
    {
        string name = System.Globalization.CultureInfo.CurrentCulture.TwoLetterISOLanguageName;
        return name.Length == 0 || name == "iv" ? "en" : name;
    }

    /// <summary>Whether user:// survives a restart: it is a folder on disk, so it does.</summary>
    public static bool IsUserfsPersistent() => true;

    /// <summary>Blocks the calling thread for the milliseconds.</summary>
    public static void DelayMsec(int msec) => System.Threading.Thread.Sleep(Math.Max(0, msec));

    /// <summary>Blocks the calling thread for the microseconds (rounded up to whole milliseconds).</summary>
    public static void DelayUsec(int usec) => System.Threading.Thread.Sleep(Math.Max(0, (usec + 999) / 1000));

    /// <summary>Every argument the program was started with, after its own name (Godot's get_cmdline_args).</summary>
    public static string[] GetCmdlineArgs() => System.Environment.GetCommandLineArgs().Skip(1).ToArray();

    /// <summary>The arguments after a lone "--": the ones meant for the game, as Godot's get_cmdline_user_args gives them.</summary>
    public static string[] GetCmdlineUserArgs()
    {
        string[] args = GetCmdlineArgs();
        int at = System.Array.IndexOf(args, "--");
        return at < 0 ? System.Array.Empty<string>() : args[(at + 1)..];
    }

    /// <summary>Opens a web address, a file or a folder in the program the system associates with it (Godot's shell_open). The system's own opener is asked,
    /// so a "file://" folder or an "https://" address both work. <see cref="Error.Ok"/> once the opener started, <see cref="Error.CantOpen"/> if it did not.</summary>
    public static Error ShellOpen(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return Error.InvalidParameter;
        try
        {
            string target = FabricPaths.IsVirtual(uri) ? FabricPaths.Globalize(uri) : uri;
            using System.Diagnostics.Process? p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });
            return p != null || OperatingSystem.IsWindows() ? Error.Ok : Error.CantOpen;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException or System.IO.FileNotFoundException)
        {
            return Error.CantOpen;
        }
    }
}

/// <summary>Godot's ProjectSettings singleton. The settings that have an Aver key are answered from the Aver project; every other setting of
/// project.godot is answered from <c>Fabric/project.json</c> (the converter keeps them all there as Godot's own text), with Godot's type. A key the
/// project never stated gets the script's own default.</summary>
public static class ProjectSettings
{
    // Fabric/project.json "settings": the project's settings as Godot's variant text, read once per content folder.
    private static Dictionary<string, string>? s_stored;
    private static string? s_storedRoot;
    private static readonly HashSet<string> s_unreadable = new(StringComparer.Ordinal);

    internal static void ResetForTests() { s_stored = null; s_storedRoot = null; s_unreadable.Clear(); }

    private static Dictionary<string, string> Stored()
    {
        string root = ResourceLoader.ContentRoot;
        if (s_stored != null && s_storedRoot == root) return s_stored;
        s_storedRoot = root;
        s_stored = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.Length == 0) return s_stored;
        string file = System.IO.Path.Combine(root, "..", "Fabric", "project.json");
        try
        {
            if (!File.Exists(file)) return s_stored;
            using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object && doc.RootElement.TryGetProperty("settings", out System.Text.Json.JsonElement settings) &&
                settings.ValueKind == System.Text.Json.JsonValueKind.Object)
                foreach (System.Text.Json.JsonProperty p in settings.EnumerateObject())
                    if (p.Value.ValueKind == System.Text.Json.JsonValueKind.String) s_stored[p.Name] = p.Value.GetString() ?? "";
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException or UnauthorizedAccessException)
        {
            Aver.Scripting.Log.Warn($"[Fabric] cannot read {file}: {ex.Message}");
        }
        return s_stored;
    }

    // The project's own value for a key, parsed from Godot's text each time (an array or dictionary is the caller's to change).
    private static bool TryStored(string name, out Variant value)
    {
        value = default;
        if (!Stored().TryGetValue(name, out string? text)) return false;
        if (VariantText.TryParse(text, out value)) return true;
        if (s_unreadable.Add(name)) Aver.Scripting.Log.Warn($"[Fabric] ProjectSettings: cannot read the value of '{name}': {text}");
        return false;
    }

    // The manifest is read through the engine's content path; outside the engine (tests, tools) there is none and the key is absent.
    private static string Manifest(string key, string fallback)
    {
        try { return Aver.Framework.ProjectSettings.Get(key, fallback); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return fallback; }
    }

    // What scripts set while the game runs (set_setting): read before the project's values. save() writes nothing, as an exported Godot game's
    // project.godot is read-only too.
    private static readonly Dictionary<string, Variant> s_set = new(StringComparer.Ordinal);

    public static void SetSetting(string name, Variant value)
    {
        if (value.VariantType == Variant.Type.Nil) s_set.Remove(name);
        else s_set[name] = value;
    }

    public static void Clear(string name) => s_set.Remove(name);
    public static void SetInitialValue(string name, Variant value) { }
    public static void SetAsBasic(string name, bool basic) { }
    public static void SetAsInternal(string name, bool @internal) { }
    public static void SetRestartIfChanged(string name, bool restart) { }
    public static Error Save() => Error.Unavailable;
    internal static void ResetSetForTests() => s_set.Clear();

    public static Variant GetSetting(string name, Variant @default = default)
    {
        if (s_set.TryGetValue(name, out Variant set)) return set;
        // The project's name as Godot has it (the Aver project's NAME is a file-safe form of it).
        if (name == "application/config/name" && TryStored(name, out Variant godotName)) return godotName;
        switch (name)
        {
            case "physics/3d/default_gravity":
            {
                string[] g = Manifest("PHYSICS.GRAVITY", "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (g.Length >= 3 && TryFloat(g[2], out double z)) return Math.Abs(z) / 100.0;
                return 9.8;
            }
            case "physics/3d/default_gravity_vector": return Vector3.Down;
            case "application/config/name": return Manifest("NAME", "");
            case "physics/common/physics_ticks_per_second":
                return TryFloat(Manifest("PHYSICS.FIXEDSTEP", ""), out double step) && step > 0 ? (long)Math.Round(1.0 / step) : 60L;
            case "display/window/size/viewport_width": return WindowSize(0, 1152);
            case "display/window/size/viewport_height": return WindowSize(1, 648);
            // G5 U1: the UI.* project keys (docs/formats/FORMAT_SPECS.md)
            case "gui/theme/custom": return Manifest("UI.THEME", "");
            case "display/window/stretch/mode": return Manifest("UI.STRETCH", "disabled");
            case "display/window/stretch/aspect": return Manifest("UI.ASPECT", "keep");
            case "display/window/stretch/scale_mode": return Manifest("UI.STRETCHSCALEMODE", "fractional");
            case "display/window/stretch/scale": return TryFloat(Manifest("UI.STRETCHSCALE", ""), out double scale) && scale > 0 ? scale : 1.0;
            case "gui/timers/tooltip_delay_sec": return TryFloat(Manifest("UI.TOOLTIPDELAY", ""), out double delay) && delay >= 0 ? delay : 0.5;
        }
        return TryStored(name, out Variant stored) ? stored : @default;
    }

    public static bool HasSetting(string name) => s_set.ContainsKey(name) || name is "physics/3d/default_gravity" or "physics/3d/default_gravity_vector" or "application/config/name"
        or "physics/common/physics_ticks_per_second" or "display/window/size/viewport_width" or "display/window/size/viewport_height"
        or "gui/theme/custom" or "display/window/stretch/mode" or "display/window/stretch/aspect" or "display/window/stretch/scale_mode"
        or "display/window/stretch/scale" or "gui/timers/tooltip_delay_sec" || Stored().ContainsKey(name);

    /// <summary>The operating-system path for a res:// or user:// path (res:// is the project's content folder, user:// the player's writable folder); any other path is
    /// returned unchanged.</summary>
    public static string GlobalizePath(string path) => FabricPaths.Globalize(path);

    /// <summary>The res:// or user:// path for a path inside the project's content folder or the user folder; any other path is returned unchanged.</summary>
    public static string LocalizePath(string path) => FabricPaths.Localize(path);

    private static bool TryFloat(string s, out double d) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d);

    private static long WindowSize(int axis, long fallback)
    {
        string[] p = Manifest("WINDOW.SIZE", "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return p.Length >= 2 && long.TryParse(p[axis], out long v) && v > 0 ? v : fallback;
    }
}

/// <summary>Godot's EngineDebugger. A game runs with no Godot debugger attached, as an exported game does: it is never active and messages go nowhere.</summary>
public static class EngineDebugger
{
    private static readonly Dictionary<string, Callable> s_captures = new(StringComparer.Ordinal);

    public static bool IsActive() => false;
    public static bool IsProfiling(StringName name) => false;
    public static bool HasProfiler(StringName name) => false;
    public static void ProfilerEnable(StringName name, bool enable, Collections.Array? arguments = null) { }
    public static void SendMessage(string message, Collections.Array data) { }
    public static void RegisterMessageCapture(StringName name, Callable callable) => s_captures[name] = callable;
    public static void UnregisterMessageCapture(StringName name) => s_captures.Remove(name);
    public static bool HasCapture(StringName name) => s_captures.ContainsKey(name);
    public static void Debug(bool canContinue = true, bool isErrorBreakpoint = false) { }
    public static void LinePoll() { }
}
