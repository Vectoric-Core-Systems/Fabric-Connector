// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Package API (docs/plans/GODOT_PARITY.md section 3.3, wave 1): the Godot members Aver.Fabric did not expose although the engine can do it. Part one: the render
// enums and stored-only calls (F20, F33, F36, F37), the compatibility aliases (F43), the built-in ui_* actions (F45), Label3D.font (F47), and the singletons
// (F60 Engine, F61 OS, F62 Time and Performance). Files, threads and the language-level members are in FabricApiTests.Files.cs and FabricApiTests.Lang.cs.
// Everything here is checked without an engine; the sources and values are written for this suite.

using System.Reflection;
using Aver.Fabric;
using Aver.Fabric.Convert;
using Collections = Aver.Fabric.Collections;

static partial class FabricApiTests
{
    public static int RunAll() => T.Suite(() =>
    {
        T.Run("F20: Light3D.light_bake_mode and GeometryInstance3D.gi_mode are stored with Godot's enums", BakeAndGiModes);
        T.Run("F33: the occluder classes are accepted and kept", Occluders);
        T.Run("F36: Viewport.screen_space_aa and the default clear colour are kept", ScreenSpaceAndClearColor);
        T.Run("F37: the stored-only RenderingServer quality calls keep their values and say so once", StoredQualityCalls);
        T.Run("F43: playback_process_mode, set_script, get_cmdline_user_args", CompatibilityAliases);
        T.Run("F45: Godot's built-in ui_* actions exist, can be changed, and are read from the devices", BuiltinUiActions);
        T.Run("F47: Label3D.font is a Font", LabelFont);
        T.Run("F60: Engine members", EngineMembers);
        T.Run("F61: OS members", OsMembers);
        T.Run("F62: Time", TimeSingleton);
        T.Run("F62: Performance", PerformanceSingleton);
        T.Run("F63: FileAccess", FileAccessTests);
        T.Run("F63: DirAccess and ProjectSettings paths", DirAccessTests);
        T.Run("F63: JSON", JsonTests);
        T.Run("F63: ConfigFile", ConfigFileTests);
        T.Run("F64: Thread, Mutex, Semaphore, WorkerThreadPool", ThreadingTests);
        T.Run("F73: String methods", StringMethods);
        T.Run("F73: String methods through converted GDScript, typed and untyped", StringMethodsConverted);
        T.Run("F74: vector, colour, rectangle and quaternion leftovers", MathLeftovers);
        T.Run("F74: Array, Dictionary, Callable and NodePath leftovers", CollectionLeftovers);
        T.Run("F75: members and methods of values whose type is not known", DynamicDispatch);
        T.Run("converted GDScript reaches the new members (enum constants, setters, singletons)", ConvertedScripts);
    });

    // ---- helpers ----

    private static string Refs => AppContext.BaseDirectory;

    private static ConversionResult Convert(string gd, string cls = "Sample") =>
        ScriptConverter.Convert(new[] { new ScriptSource { ResPath = "res://sample.gd", Text = gd, Kind = "gdscript", ClassName = cls, Output = "Content/Scripts/" + cls + ".cs" } },
                                new ConvertSettings { RefsDir = Refs, KeepAssembly = true, ExtraSources = System.Array.Empty<(string, string)>() });

    private static string Errors(ConversionResult r) => string.Join("; ", r.Errors.Take(4).Select(e => e.ToString())) + string.Join("; ", r.GeneralErrors);

    private static object? Call(ConversionResult r, string cls, string method, params object?[] args)
    {
        System.Type type = Assembly.Load(r.Assembly!).GetType(cls)!;
        object instance = Activator.CreateInstance(type)!;
        try { return type.GetMethod(method)!.Invoke(instance, args); }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static string TempDir(string what)
    {
        string p = Path.Combine(Path.GetTempPath(), "fabric_api_" + what + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(p);
        return p;
    }

    // ---- F20 ----

    private static void BakeAndGiModes()
    {
        var light = new OmniLight3D();
        T.Check("a light is dynamic by default", light.LightBakeMode == Light3D.BakeMode.Dynamic && !light.EditorOnly);
        light.LightBakeMode = Light3D.BakeMode.Static;
        T.Check("the bake mode is kept as set", light.LightBakeMode == Light3D.BakeMode.Static);
        T.Check("the enum carries Godot's numbers (disabled 0, static 1, dynamic 2)", (int)Light3D.BakeMode.Disabled == 0 && (int)Light3D.BakeMode.Static == 1 && (int)Light3D.BakeMode.Dynamic == 2);

        var mesh = new MeshInstance3D();
        T.Check("an instance's GI mode is static by default (Godot's default)", mesh.GIMode == GeometryInstance3D.GIModeEnum.Static);
        mesh.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
        mesh.GILightmapTexelScale = 2f;
        mesh.GILightmapScale = GeometryInstance3D.LightmapScaleEnum.Scale4X;
        mesh.ExtraCullMargin = 3f;
        mesh.IgnoreOcclusionCulling = true;
        mesh.CustomAabb = new Aabb(new Vector3(1, 2, 3), new Vector3(4, 5, 6));
        T.Check("the lightmap, margin, occlusion and bounds properties are kept", mesh.GIMode == GeometryInstance3D.GIModeEnum.Disabled && mesh.GILightmapTexelScale == 2f &&
            mesh.GILightmapScale == GeometryInstance3D.LightmapScaleEnum.Scale4X && mesh.ExtraCullMargin == 3f && mesh.IgnoreOcclusionCulling && mesh.CustomAabb.Size == new Vector3(4, 5, 6));
        mesh.Set("gi_mode", 2L);
        T.Check("and a scene's integer reaches them by Godot's name", mesh.GIMode == GeometryInstance3D.GIModeEnum.Dynamic);
    }

    // ---- F33 ----

    private static void Occluders()
    {
        T.Check("the occluder classes are known by their Godot names", ClassDB.ClassExists("OccluderInstance3D") && ClassDB.ClassExists("ArrayOccluder3D") && ClassDB.ClassExists("BoxOccluder3D") &&
            ClassDB.ClassExists("QuadOccluder3D") && ClassDB.ClassExists("SphereOccluder3D") && ClassDB.ClassExists("PolygonOccluder3D"));
        T.Check("OccluderInstance3D is a VisualInstance3D, the shapes are resources", ClassDB.IsParentClass("OccluderInstance3D", "VisualInstance3D") && ClassDB.IsParentClass("BoxOccluder3D", "Occluder3D") && ClassDB.IsParentClass("Occluder3D", "Resource"));

        var box = new BoxOccluder3D { Size = new Vector3(2, 4, 6) };
        Vector3[] corners = box.GetVertices();
        T.Check("a box occluder reports its eight corners and twelve triangles", corners.Length == 8 && box.GetIndices().Length == 36 && corners.Max(c => c.Y) == 2f && corners.Min(c => c.Z) == -3f);
        var quad = new QuadOccluder3D { Size = new Vector2(2, 2) };
        T.Check("a quad reports four corners", quad.GetVertices().Length == 4 && quad.GetIndices().Length == 6);
        var array = new ArrayOccluder3D();
        array.SetArrays(new[] { Vector3.Zero, Vector3.Right, Vector3.Up }, new[] { 0, 1, 2 });
        T.Check("an array occluder keeps what it was given", array.Vertices.Length == 3 && array.Indices.SequenceEqual(new[] { 0, 1, 2 }));
        var poly = new PolygonOccluder3D { Polygon = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) } };
        T.Check("a polygon occluder fans its outline into triangles", poly.GetIndices().Length == 6 && poly.GetVertices()[2] == new Vector3(1, 1, 0));
        int changed = 0;
        box.Changed += () => changed++;
        box.Size = Vector3.One;
        T.Check("changing a shape emits changed", changed == 1);

        var node = new OccluderInstance3D { Occluder = box };
        T.Check("the instance holds its shape; bake mask and distance have Godot's defaults", ReferenceEquals(node.Occluder, box) && node.BakeMask == 0xFFFFFFFFu && node.BakeSimplificationDistance == 0.1f);
        node.SetBakeMaskValue(2, false);
        T.Check("bake mask layers can be switched", !node.GetBakeMaskValue(2) && node.GetBakeMaskValue(1));
        object? made = ClassDB.Instantiate("OccluderInstance3D").AsGodotObject();
        T.Check("ClassDB makes one by name (a scene can place it)", made is OccluderInstance3D);

        // What the converter writes for a scene's occluder resources.
        Variant boxValue = VariantText.Parse("Object(BoxOccluder3D, \"size\": Vector3(2, 3, 0.2))");
        T.Check("Object(BoxOccluder3D, ...) builds the shape with its size", boxValue.AsGodotObject() is BoxOccluder3D { Size: var s } && s == new Vector3(2, 3, 0.2f));
        Variant arrayValue = VariantText.Parse("Object(ArrayOccluder3D, \"vertices\": PackedVector3Array(0, 0, 0, 1, 0, 0, 0, 1, 0), \"indices\": PackedInt32Array(0, 1, 2))");
        T.Check("Object(ArrayOccluder3D, ...) takes packed vertices and indices", arrayValue.AsGodotObject() is ArrayOccluder3D { Vertices.Length: 3, Indices.Length: 3 });
        var placed = new OccluderInstance3D();
        placed.Set("occluder", boxValue);
        placed.Set("bake_mask", 4L);
        T.Check("and the node takes the shape and the mask by Godot's names", placed.Occluder is BoxOccluder3D && placed.BakeMask == 4u);
    }

    // ---- F36 ----

    private static void ScreenSpaceAndClearColor()
    {
        var vp = new Viewport();
        T.Check("screen_space_aa is off by default and holds what is set", vp.ScreenSpaceAA == Viewport.ScreenSpaceAAEnum.Disabled);
        vp.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
        T.Check("FXAA is 1, SMAA 2 (Godot's numbers)", vp.ScreenSpaceAA == Viewport.ScreenSpaceAAEnum.Fxaa && (int)Viewport.ScreenSpaceAAEnum.Smaa == 2);
        Color grey = RenderingServer.GetDefaultClearColor();
        T.Check("the default clear colour is Godot's 0.3 grey", grey == new Color(0.3f, 0.3f, 0.3f, 1f));
        RenderingServer.SetDefaultClearColor(new Color(0.1f, 0.2f, 0.3f, 1f));
        T.Check("and keeps a new one", RenderingServer.GetDefaultClearColor() == new Color(0.1f, 0.2f, 0.3f, 1f));
        T.Check("setting it says once that the sky is not repainted", T.Logs.Count(l => l.Message.Contains("SetDefaultClearColor")) == 1);
        RenderingServer.SetDefaultClearColor(grey);
    }

    // ---- F37 ----

    private static void StoredQualityCalls()
    {
        Approximated.ResetForTests();
        T.Logs.Clear();
        RenderingServer.EnvironmentSetSsaoQuality(RenderingServer.EnvironmentSsaoQuality.High, true, 0.25f, 3, 40f, 200f);
        RenderingServer.EnvironmentSetSsaoQuality(RenderingServer.EnvironmentSsaoQuality.Low, false, 0.5f, 2, 50f, 300f);
        RenderingServer.EnvironmentSetSsilQuality(RenderingServer.EnvironmentSsilQuality.Ultra, true, 0.5f, 1, 10f, 20f);
        RenderingServer.EnvironmentSetSsrRoughnessQuality(RenderingServer.EnvironmentSsrRoughnessQuality.High);
        RenderingServer.GiSetUseHalfResolution(true);
        RenderingServer.EnvironmentGlowSetUseBicubicUpscale(true);
        RenderingServer.EnvironmentSetVolumetricFogFilterActive(false);
        RenderingServer.EnvironmentSetVolumetricFogVolumeSize(128, 96);
        T.Check("the last SSAO quality is kept whole", RenderingServer.SsaoQuality is { Quality: 1, HalfSize: false, BlurPasses: 2, FadeoutTo: 300f });
        T.Check("SSIL, SSR roughness, GI half resolution, glow and fog switches are kept", RenderingServer.SsilQuality is { Quality: 4, HalfSize: true } && RenderingServer.SsrRoughnessQuality == RenderingServer.EnvironmentSsrRoughnessQuality.High &&
            RenderingServer.GiHalfResolution && RenderingServer.GlowBicubicUpscale && !RenderingServer.VolumetricFogFilterActive && RenderingServer.VolumetricFogVolumeSize == (128, 96));
        T.Check("each call says it is approximated once, however often it is made",
            T.Logs.Count(l => l.Message.Contains("EnvironmentSetSsaoQuality")) == 1 && T.Logs.Count(l => l.Message.Contains("GiSetUseHalfResolution")) == 1 && T.Logs.Count(l => l.Message.Contains("[Fabric]")) == 7);

        var env = new Aver.Fabric.Environment();
        T.Check("Environment keeps ssr_max_steps (Godot's default 64) and its fades", env.SsrMaxSteps == 64 && env.SsrFadeIn == 0.15f && env.SsrFadeOut == 2f && env.SsrDepthTolerance == 0.2f);
        env.SsrMaxSteps = 8;
        T.Check("set_ssr_max_steps changes it", env.SsrMaxSteps == 8);
    }

    // ---- F43 ----

    private static void CompatibilityAliases()
    {
        var player = new AnimationPlayer();
        player.PlaybackProcessMode = AnimationPlayer.AnimationProcessCallback.Physics;
        T.Check("playback_process_mode is the callback mode of the mixer", player.CallbackModeProcess == AnimationMixer.AnimationCallbackModeProcess.Physics || (int)player.CallbackModeProcess == 0);
        player.PlaybackProcessMode = AnimationPlayer.AnimationProcessCallback.Idle;
        T.Check("and reads back", player.PlaybackProcessMode == AnimationPlayer.AnimationProcessCallback.Idle && (int)player.CallbackModeProcess == 1);
        player.MethodCallMode = AnimationPlayer.AnimationMethodCallMode.Immediate;
        T.Check("method_call_mode too", player.CallbackModeMethod == AnimationMixer.AnimationCallbackModeMethod.Immediate);

        Approximated.ResetForTests();
        T.Logs.Clear();
        var node = new Node3D();
        T.Check("an object has no script until one is set", node.GetScript().IsNil);
        var script = new Resource { ResourcePath = "res://tool.gd" };
        node.SetScript(script);
        node.SetScript(script);
        T.Check("set_script keeps the script for get_script", ReferenceEquals(node.GetScript().AsGodotObject(), script));
        T.Check("and says once that its code is not attached", T.Logs.Count(l => l.Message.Contains("Object.SetScript")) == 1);
        node.SetScript(default);
        T.Check("setting nil clears it", node.GetScript().IsNil);

        string[] user = OS.GetCmdlineUserArgs();
        string[] all = OS.GetCmdlineArgs();
        T.Check("the user arguments are the ones after \"--\" (none in the test host)", user.Length == 0 || all.Contains("--"));
    }

    // ---- F45 ----

    private static void BuiltinUiActions()
    {
        Input.ResetForTests();
        string[] names = { "ui_accept", "ui_select", "ui_cancel", "ui_focus_next", "ui_focus_prev", "ui_left", "ui_right", "ui_up", "ui_down", "ui_page_up", "ui_page_down", "ui_home", "ui_end",
                           "ui_cut", "ui_copy", "ui_paste", "ui_undo", "ui_redo", "ui_text_submit", "ui_text_backspace", "ui_text_caret_left", "ui_text_select_all", "ui_graph_delete", "ui_filedialog_refresh" };
        T.Check("every built-in action exists", names.All(n => InputMap.HasAction(n)));
        T.Check("and is listed with the project's", InputMap.GetActions().Count(a => a == "ui_accept") == 1 && InputMap.GetActions().Count >= names.Length);

        var accept = InputMap.ActionGetEvents("ui_accept");
        T.Check("ui_accept is Enter, keypad Enter, Space and the A button", accept.Count == 4 && accept.OfType<InputEventKey>().Select(k => k.Keycode).OrderBy(k => (long)k).SequenceEqual(new[] { Key.Space, Key.Enter, Key.KpEnter }.OrderBy(k => (long)k)) &&
            accept.OfType<InputEventJoypadButton>().Single().ButtonIndex == JoyButton.A);
        T.Check("ui_redo has two shortcuts, one with Ctrl+Shift", InputMap.ActionGetEvents("ui_redo").OfType<InputEventKey>().Any(k => k.Keycode == Key.Z && k.CtrlPressed && k.ShiftPressed) &&
            InputMap.ActionGetEvents("ui_redo").OfType<InputEventKey>().Any(k => k.Keycode == Key.Y && k.CtrlPressed));
        T.Check("ui_left has the arrow, the pad and the stick", InputMap.ActionGetEvents("ui_left").Count == 3);
        T.Check("an action with no default key (ui_menu) exists with no events", InputMap.HasAction("ui_menu") && InputMap.ActionGetEvents("ui_menu").Count == 0);

        var enter = new InputEventKey { Keycode = Key.Enter, PhysicalKeycode = Key.Enter, Pressed = true };
        T.Check("an Enter key event is ui_accept and not ui_cancel", enter.IsAction("ui_accept") && !enter.IsAction("ui_cancel"));
        T.Check("the focus keys tell Tab from Shift+Tab by the modifier the action asks for", new InputEventKey { Keycode = Key.Tab, Pressed = true, ShiftPressed = true }.IsAction("ui_focus_prev") &&
            !new InputEventKey { Keycode = Key.Tab, Pressed = true }.IsAction("ui_focus_prev"));

        // Fed events change the action state as for any action.
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Left, PhysicalKeycode = Key.Left, Pressed = true });
        T.Check("a left-arrow press makes ui_left pressed", Input.IsActionPressed("ui_left") && !Input.IsActionPressed("ui_right"));
        T.Check("with full strength", Input.GetActionStrength("ui_left") == 1f);
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Left, PhysicalKeycode = Key.Left, Pressed = false });
        T.Check("and the release lets go", !Input.IsActionPressed("ui_left"));

        // A script may erase a built-in action, and add its own.
        InputMap.EraseAction("ui_accept");
        T.Check("an erased built-in action is gone", !InputMap.HasAction("ui_accept") && !InputMap.GetActions().Contains("ui_accept"));
        InputMap.AddAction("ui_accept");
        InputMap.ActionAddEvent("ui_accept", new InputEventKey { Keycode = Key.A, PhysicalKeycode = Key.A });
        T.Check("and can be added again with its own events only", InputMap.ActionGetEvents("ui_accept").Count == 1);

        // Changing a built-in action in place starts from its defaults.
        InputMap.ActionAddEvent("ui_cancel", new InputEventKey { Keycode = Key.Q, PhysicalKeycode = Key.Q });
        var cancel = InputMap.ActionGetEvents("ui_cancel");
        T.Check("adding an event to ui_cancel keeps Escape and the B button", cancel.Count == 3 && cancel.OfType<InputEventKey>().Any(k => k.Keycode == Key.Escape));
        InputMap.ActionEraseEvents("ui_select");
        T.Check("erasing an action's events empties it, the action stays", InputMap.HasAction("ui_select") && InputMap.ActionGetEvents("ui_select").Count == 0);
        T.Check("a user action cannot be mistaken for one", !InputMap.HasAction("jump"));
        Input.ResetForTests();
    }

    // ---- F47 ----

    private static void LabelFont()
    {
        var label = new Label3D { Text = "hi" };
        T.Check("a Label3D has no font until one is set", label.Font == null);
        label.Font = new FontFile();
        Vector2 size = label.Font.GetStringSize("abcd", fontSize: 16);
        T.Check("its font measures text (half an em a character without an engine face)", size.X > 0f && size.Y > 0f);
        label.Set("font", new FontFile { ResourcePath = "res://fonts/f.ttf" });
        T.Check("a scene sets it by Godot's name", label.Font != null && label.Font.ResourcePath == "res://fonts/f.ttf");
    }

    // ---- F60 ----

    private static void EngineMembers()
    {
        T.Check("physics ticks per second is 60 when nothing states it", Aver.Fabric.Engine.PhysicsTicksPerSecond == 60);
        Aver.Fabric.Engine.PhysicsTicksPerSecond = 30;
        T.Check("the setter keeps the rate", Aver.Fabric.Engine.PhysicsTicksPerSecond == 30);
        Aver.Fabric.Engine.PhysicsTicksPerSecond = 0;
        T.Check("a rate below one is refused", Aver.Fabric.Engine.PhysicsTicksPerSecond == 30);
        Aver.Fabric.Engine.PhysicsTicksPerSecond = 60;

        Aver.Fabric.Engine.MaxFps = 144;
        Aver.Fabric.Engine.MaxPhysicsStepsPerFrame = 4;
        T.Check("max_fps and max_physics_steps_per_frame are kept", Aver.Fabric.Engine.MaxFps == 144 && Aver.Fabric.Engine.MaxPhysicsStepsPerFrame == 4);
        Aver.Fabric.Engine.MaxFps = 0;
        Aver.Fabric.Engine.MaxPhysicsStepsPerFrame = 8;

        double fraction = Aver.Fabric.Engine.GetPhysicsInterpolationFraction();
        T.Check("the interpolation fraction is 0 to 1 (0 with no physics world)", fraction >= 0.0 && fraction <= 1.0);
        T.Check("frames drawn follow the process frame count", Aver.Fabric.Engine.GetFramesDrawn() == Aver.Fabric.Engine.GetProcessFrames());

        Collections.Dictionary info = Aver.Fabric.Engine.GetVersionInfo();
        T.Check("the version info has Godot's keys", new[] { "major", "minor", "patch", "hex", "status", "build", "hash", "timestamp", "string" }.All(k => info.ContainsKey(k)));
        T.Check("and the version of the API this runtime follows (4.5, hex 0x040500)", info["major"].AsInt64() == 4 && info["minor"].AsInt64() == 5 && info["hex"].AsInt64() == 0x040500);
        T.Check("the architecture is named in Godot's spelling", Aver.Fabric.Engine.GetArchitectureName().Length > 0);
    }

    // ---- F61 ----

    private static void OsMembers()
    {
        T.Check("the processor count is at least one", OS.GetProcessorCount() >= 1);
        ulong a = OS.GetStaticMemoryUsage();
        T.Check("static memory is in use, and its peak is not below it", a > 0 && OS.GetStaticMemoryPeakUsage() >= a);

        string dir = TempDir("userdir");
        string? before = FabricPaths.UserOverride;
        FabricPaths.UserOverride = dir;
        try
        {
            T.Check("the user data folder is user://'s folder", OS.GetUserDataDir() == dir);
            T.Check("and ProjectSettings.globalize_path agrees", ProjectSettings.GlobalizePath("user://saves/a.dat") == Path.Combine(dir, "saves", "a.dat"));
        }
        finally { FabricPaths.UserOverride = before; Directory.Delete(dir, true); }

        T.Check("shell_open refuses an empty address without starting anything", OS.ShellOpen("") == Error.InvalidParameter);
        OS.SetEnvironment("FABRIC_API_TEST", "yes");
        T.Check("environment variables can be set, read and unset", OS.HasEnvironment("FABRIC_API_TEST") && OS.GetEnvironment("FABRIC_API_TEST") == "yes");
        OS.UnsetEnvironment("FABRIC_API_TEST");
        T.Check("unset removes it", !OS.HasEnvironment("FABRIC_API_TEST"));
        T.Check("the executable path, locale and process id are given", OS.GetExecutablePath().Length > 0 && OS.GetLocale().Length > 0 && OS.GetLocaleLanguage().Length >= 2 && OS.GetProcessId() > 0);
        T.Check("ticks come from the same clock as Time", OS.GetTicksMsec() <= Aver.Fabric.Time.GetTicksMsec() + 5);
    }

    // ---- F62 ----

    private static void TimeSingleton()
    {
        const long t = 1700000000;   // Tuesday 2023-11-14 22:13:20 UTC
        T.Check("unix time to a datetime string", Aver.Fabric.Time.GetDatetimeStringFromUnixTime(t) == "2023-11-14T22:13:20" && Aver.Fabric.Time.GetDatetimeStringFromUnixTime(t, true) == "2023-11-14 22:13:20");
        T.Check("date and time strings", Aver.Fabric.Time.GetDateStringFromUnixTime(t) == "2023-11-14" && Aver.Fabric.Time.GetTimeStringFromUnixTime(t) == "22:13:20");
        Collections.Dictionary d = Aver.Fabric.Time.GetDatetimeDictFromUnixTime(t);
        T.Check("the datetime dictionary has Godot's keys and a Tuesday", d["year"].AsInt64() == 2023 && d["month"].AsInt64() == 11 && d["day"].AsInt64() == 14 && d["weekday"].AsInt64() == (long)Aver.Fabric.Time.Weekday.Tuesday &&
            d["hour"].AsInt64() == 22 && d["minute"].AsInt64() == 13 && d["second"].AsInt64() == 20 && !d.ContainsKey("dst"));
        T.Check("the date and time dictionaries split it", Aver.Fabric.Time.GetDateDictFromUnixTime(t).Count == 4 && Aver.Fabric.Time.GetTimeDictFromUnixTime(t).Count == 3);
        T.Check("a datetime dictionary gives back the unix time", Aver.Fabric.Time.GetUnixTimeFromDatetimeDict(d) == t);
        T.Check("so does a datetime string, with a T or a space", Aver.Fabric.Time.GetUnixTimeFromDatetimeString("2023-11-14T22:13:20") == t && Aver.Fabric.Time.GetUnixTimeFromDatetimeString("2023-11-14 22:13:20") == t);
        Collections.Dictionary parsed = Aver.Fabric.Time.GetDatetimeDictFromDatetimeString("2024-02-29 01:02:03", true);
        T.Check("a string parses to a dictionary with the weekday on request (a Thursday)", parsed["year"].AsInt64() == 2024 && parsed["day"].AsInt64() == 29 && parsed["weekday"].AsInt64() == 4 && parsed["second"].AsInt64() == 3);
        T.Check("a date alone gives only date keys, a time alone only time keys", !Aver.Fabric.Time.GetDatetimeDictFromDatetimeString("2024-02-29", false).ContainsKey("hour") && !Aver.Fabric.Time.GetDatetimeDictFromDatetimeString("10:20:30", false).ContainsKey("year"));
        T.Check("a datetime dictionary writes a string", Aver.Fabric.Time.GetDatetimeStringFromDatetimeDict(d, false) == "2023-11-14T22:13:20");
        T.Check("a day past the end of the month carries over", Aver.Fabric.Time.GetUnixTimeFromDatetimeDict(new Collections.Dictionary { { "year", 2023L }, { "month", 1L }, { "day", 32L } }) ==
                                                                  Aver.Fabric.Time.GetUnixTimeFromDatetimeString("2023-02-01T00:00:00"));
        T.Check("offset strings", Aver.Fabric.Time.GetOffsetStringFromOffsetMinutes(120) == "+02:00" && Aver.Fabric.Time.GetOffsetStringFromOffsetMinutes(-330) == "-05:30" && Aver.Fabric.Time.GetOffsetStringFromOffsetMinutes(0) == "+00:00");

        T.Logs.Clear();
        long empty = Aver.Fabric.Time.GetUnixTimeFromDatetimeDict(new Collections.Dictionary());
        T.Check("an empty dictionary is an error and 0", empty == 0 && T.ErrorCount("empty") == 1);

        Collections.Dictionary now = Aver.Fabric.Time.GetDatetimeDictFromSystem(true);
        T.Check("the system datetime has the dst key too and a sane year", now.ContainsKey("dst") && now["year"].AsInt64() >= 2024);
        T.Check("the system date and time strings have their shape", Aver.Fabric.Time.GetDatetimeStringFromSystem(true).Length == 19 && Aver.Fabric.Time.GetDateStringFromSystem().Length == 10 && Aver.Fabric.Time.GetTimeStringFromSystem().Length == 8);
        Collections.Dictionary zone = Aver.Fabric.Time.GetTimeZoneFromSystem();
        T.Check("the time zone has a bias and a name", zone.ContainsKey("bias") && zone.ContainsKey("name"));
        double unix = Aver.Fabric.Time.GetUnixTimeFromSystem();
        T.Check("the unix time of now is after 2024", unix > 1704067200.0);
        ulong m1 = Aver.Fabric.Time.GetTicksMsec(), u1 = Aver.Fabric.Time.GetTicksUsec();
        System.Threading.Thread.Sleep(5);
        ulong m2 = Aver.Fabric.Time.GetTicksMsec(), u2 = Aver.Fabric.Time.GetTicksUsec();
        T.Check("ticks only go forward, and microseconds run a thousand to the millisecond", m2 >= m1 && u2 > u1 && u2 / 1000 >= m1);
    }

    private static void PerformanceSingleton()
    {
        Performance.ResetForTests();
        T.Check("memory and node counts are measured", Performance.GetMonitor(Performance.Monitor.MemoryStatic) > 0 && Performance.GetMonitor(Performance.Monitor.ObjectNodeCount) >= 0);
        var root = SceneTree.Instance.Root;
        root.AddChild(new Node { Name = "A" });
        root.AddChild(new Node { Name = "B" });
        T.Check("the node count follows the tree", Performance.GetMonitor(Performance.Monitor.ObjectNodeCount) >= 2 && Performance.GetMonitor(Performance.Monitor.ObjectCount) >= Performance.GetMonitor(Performance.Monitor.ObjectNodeCount));
        T.Check("what the engine does not measure is zero", Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame) == 0.0 && Performance.GetMonitor(Performance.Monitor.Navigation3DAgentCount) == 0.0);
        T.Check("the monitor enum keeps Godot's numbers", (int)Performance.Monitor.TimeFps == 0 && (int)Performance.Monitor.ObjectOrphanNodeCount == 10 && (int)Performance.Monitor.Navigation3DObstacleCount == 58 && (int)Performance.Monitor.Max == 59);

        int calls = 0;
        Performance.AddCustomMonitor("score", Callable.From<long, long>(extra => { calls++; return 40 + extra; }), new Collections.Array { 2L });
        T.Check("a custom monitor is listed", Performance.HasCustomMonitor("score") && Performance.GetCustomMonitorNames().Select(n => n.ToString()).SequenceEqual(new[] { "score" }));
        T.Check("and reading it calls the callable with its arguments", Performance.GetCustomMonitor("score").AsInt64() == 42 && calls == 1);
        ulong stamp = Performance.GetMonitorModificationTime();
        T.Check("adding one stamped the list as modified", stamp > 0);
        T.Logs.Clear();
        Performance.AddCustomMonitor("score", Callable.From<long, long>(x => x), null);
        T.Check("a second monitor under the same name is an error", T.ErrorCount("exists already") == 1);
        Performance.RemoveCustomMonitor("score");
        T.Check("a monitor can be removed", !Performance.HasCustomMonitor("score"));
        T.Check("and reading a missing one is an error giving nil", Performance.GetCustomMonitor("score").IsNil && T.ErrorCount("no monitor") == 1);
    }
}
