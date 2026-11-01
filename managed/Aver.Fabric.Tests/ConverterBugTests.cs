// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Converter bugs (docs/plans/GODOT_PARITY.md section 3.4, C1 to C10): each GDScript snippet below failed to compile or converted to the wrong code before the fix.
// The sources are written for this suite; every one is converted, compiled against this runtime, and where it computes something it is run.

using System.Reflection;
using Aver.Fabric;
using Aver.Fabric.Convert;

static class ConverterBugTests
{
    public static int RunAll() => T.Suite(() =>
    {
        T.Run("converter C1: a script named System, Math or Task does not hide what converted code uses; await gives Task", ScriptNamedLikeTheBcl);
        T.Run("converter C2: a singleton's get_x() / set_x(v) is its static property", SingletonAccessors);
        T.Run("converter C3: enum constants resolve by the prefix GodotSharp drops, bare ones against the base class, and names keep GodotSharp's digit casing", EnumConstants);
        T.Run("converter C4: AABB and other acronym spellings reach the Aver.Fabric type", AcronymNames);
        T.Run("converter C5: value type, integer and bool coercions", Coercions);
        T.Run("converter C5b: Basis(), Transform3D(), Quaternion() are GDScript's identity and Color() opaque black, not C#'s zeros", IdentityConstructors);
        T.Run("converter C6: a member only a subclass has is reached through a cast", BaseTypedReceivers);
        T.Run("converter C7: an autoload names its script as a type, its enum travels in a signal", AutoloadAsType);
        T.Run("converter C8: a static function that needs an instance is written as an instance method", StaticNeedsInstance);
        T.Run("converter C9: packed array methods that change the length", PackedArrayMethods);
        T.Run("converter C10: utility functions with a runtime or BCL twin", UtilityFunctions);
        T.Run("converter C13, C18: the Object(PhysicsMaterial) and Object(InputEventAction) values the converter writes reach a body and a shortcut", ConvertedResourceValues);
    });

    // ---- helpers ----

    private static string Refs => AppContext.BaseDirectory;

    private static ConversionResult Convert(IReadOnlyList<ScriptSource> sources, bool keep = false, string? projectDir = null) =>
        ScriptConverter.Convert(sources, new ConvertSettings { RefsDir = Refs, KeepAssembly = keep, ExtraSources = System.Array.Empty<(string, string)>(), ProjectDir = projectDir });

    private static ConversionResult One(string gd, string cls = "Sample", bool keep = false) =>
        Convert(new[] { new ScriptSource { ResPath = "res://sample.gd", Text = gd, Kind = "gdscript", ClassName = cls, Output = "Content/Scripts/" + cls + ".cs" } }, keep);

    private static string Text(ConversionResult r, int i = 0) => r.Scripts[i].Text;
    private static string Errors(ConversionResult r) => string.Join("; ", r.Errors.Take(4).Select(e => e.ToString())) + string.Join("; ", r.GeneralErrors);
    private static bool Has(string text, params string[] parts) => parts.All(text.Contains);

    private static string Temp(string what)
    {
        string p = Path.Combine(Path.GetTempPath(), "fabric_cb_" + what + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(p);
        return p;
    }

    private static object? Call(ConversionResult r, string cls, string method, params object?[] args)
    {
        Type type = Assembly.Load(r.Assembly!).GetType(cls)!;
        object instance = Activator.CreateInstance(type)!;
        return type.GetMethod(method)!.Invoke(instance, args);
    }

    // ---- C1 ----

    private static void ScriptNamedLikeTheBcl()
    {
        string root = Temp("bcl");
        try
        {
            string proj = Path.Combine(root, "Game");
            string outDir = Path.Combine(root, "Out");
            Directory.CreateDirectory(proj);
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(proj, "project.godot"), "config_version=5\n\n[autoload]\n\nSystem=\"*res://system.gd\"\n");
            File.WriteAllText(Path.Combine(proj, "system.gd"),
                "extends Node\n\nenum Engine_Kind { FAST, SLOW }\nvar kind := Engine_Kind.FAST\n\nfunc get_kind() -> int:\n\treturn kind\n");
            File.WriteAllText(Path.Combine(proj, "math.gd"), "extends Node\n\nfunc twice(x: float) -> float:\n\treturn x * 2.0\n");
            File.WriteAllText(Path.Combine(proj, "task.gd"), "extends Node\n\nvar done := false\n");
            File.WriteAllText(Path.Combine(proj, "user.gd"),
                "extends Node\n\nfunc wait_a_bit() -> void:\n\tawait get_tree().create_timer(0.01).timeout\n\tprint(\"done\")\n\n" +
                "func wait_and_report() -> int:\n\tawait wait_a_bit()\n\tif System.get_kind() == System.Engine_Kind.SLOW:\n\t\treturn -1\n\treturn int(round(2.5)) + int(roundf(3.5))\n");
            var so = new StringWriter();
            var se = new StringWriter();
            int code = ConvertCli.Run(new[] { "scripts", "--project", proj, "--out", outDir, "--refs", Refs }, so, se);
            T.Check("a project with scripts named System, Math and Task converts and compiles (before: CS0138 in every file)", code == 0, se.ToString());
            string scripts = Path.Combine(outDir, "Content", "Scripts");
            T.Check("the colliding classes are written with Script after the name",
                    File.Exists(Path.Combine(scripts, "SystemScript.cs")) && File.Exists(Path.Combine(scripts, "MathScript.cs")) && File.Exists(Path.Combine(scripts, "TaskScript.cs")) && !File.Exists(Path.Combine(scripts, "System.cs")));
            string user = File.ReadAllText(Path.Combine(scripts, "User.cs"));
            T.Check("the autoload named System reaches its renamed class", user.Contains("GetNode<SystemScript>(\"/root/System\")"), user);
            T.Check("an awaited function returns Task, an awaiting one is async", user.Contains("public async Task WaitABit()") && user.Contains("await WaitABit()"), user);
            T.Check("the file still opens with the plain using System", user.Contains("using System;") && user.Contains("using System.Threading.Tasks;"));
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }

    // ---- C2 ----

    private static void SingletonAccessors()
    {
        ConversionResult r = One(
            "extends Node\n\nfunc grab() -> bool:\n\tInput.set_mouse_mode(Input.MOUSE_MODE_CAPTURED)\n\tif Input.get_mouse_mode() == Input.MOUSE_MODE_CAPTURED:\n\t\tInput.set_mouse_mode(Input.MOUSE_MODE_VISIBLE)\n" +
            "\treturn Input.get_mouse_mode() == Input.MOUSE_MODE_VISIBLE\n", keep: true);
        string t = Text(r);
        T.Check("it compiles", r.Success, Errors(r));
        T.Check("set_mouse_mode(m) is the assignment, get_mouse_mode() the read", Has(t, "Input.MouseMode = Input.MouseModeEnum.Captured;", "Input.MouseMode == Input.MouseModeEnum.Captured", "Input.MouseMode = Input.MouseModeEnum.Visible;"), t);
        T.Check("and runs: the mode ends up Visible", r.Success && (bool)Call(r, "Sample", "Grab")! && Input.MouseMode == Input.MouseModeEnum.Visible);
        T.Check("no unsupported row for the singleton calls", !r.Items.Any(i => i.Rating == Rating.Unsupported && i.Property is "set_mouse_mode" or "get_mouse_mode"));
    }

    // ---- C3 ----

    private static void EnumConstants()
    {
        ConversionResult r = One(
            "extends Node\n\nfunc f(m: AnimationPlayer, v: SubViewport) -> int:\n\tprocess_mode = PROCESS_MODE_ALWAYS\n\tvar p := Mesh.PRIMITIVE_LINES\n\tvar k := KEY_1\n\tvar k2 := KEY_SPACE\n" +
            "\tm.callback_mode_process = AnimationPlayer.ANIMATION_CALLBACK_MODE_PROCESS_PHYSICS\n\tv.msaa_3d = Viewport.MSAA_4X\n\tv.scaling_3d_scale = 0.5\n\tv.scaling_3d_mode = Viewport.SCALING_3D_MODE_FSR\n" +
            "\tvar t := Tween.TRANS_SINE\n\tvar mb := MOUSE_BUTTON_LEFT\n\treturn int(p) + int(k) + int(k2) + int(t) + int(mb)\n");
        string t = Text(r);
        T.Check("a bare PROCESS_MODE_ constant is the base class's enum", t.Contains("ProcessMode = Node.ProcessModeEnum.Always;"), t);
        T.Check("PRIMITIVE_LINES is the Lines of PrimitiveType: the prefix is dropped up to the enum's own name", t.Contains("Mesh.PrimitiveType.Lines"), t);
        T.Check("KEY_1 keeps its prefix (Key.Key1), KEY_SPACE drops it", Has(t, "Key.Key1", "Key.Space"), t);
        T.Check("TRANS_SINE still reaches TransitionType", t.Contains("Tween.TransitionType.Sine"), t);
        T.Check("MOUSE_BUTTON_LEFT is MouseButton.Left", t.Contains("MouseButton.Left"), t);
        T.Check("MSAA_4X is the Msaa4X member and msaa_3d, scaling_3d_scale are Msaa3D, Scaling3DScale", Has(t, "Msaa4X", ".Msaa3D", ".Scaling3DScale", ".Scaling3DMode"), t);
        T.Check("no 'is not declared' guess is left for a constant", !r.Items.Any(i => i.Rating == Rating.Unsupported && i.Property is "PROCESS_MODE_ALWAYS" or "KEY_1" or "PRIMITIVE_LINES"), string.Join("; ", r.Items.Where(i => i.Rating == Rating.Unsupported).Select(i => i.Property)));
    }

    // ---- C4 ----

    private static void AcronymNames()
    {
        ConversionResult r = One(
            "extends Node3D\n\nvar bounds: AABB = AABB(Vector3(0, 0, 0), Vector3(1, 2, 3))\nvar wave: AudioStreamWAV\n\nfunc volume() -> float:\n\tvar grown: AABB = bounds.grow(1.0)\n\treturn grown.size.x * grown.size.y * grown.size.z\n", keep: true);
        string t = Text(r);
        T.Check("it compiles", r.Success, Errors(r));
        T.Check("AABB is Aabb and AudioStreamWAV is AudioStreamWav", Has(t, "public Aabb Bounds", "public AudioStreamWav Wave"), t);
        T.Check("and the method runs: (1+2)*(2+2)*(3+2) = 60", r.Success && Math.Abs((double)Call(r, "Sample", "Volume")! - 60.0) < 1e-6);
        T.Check("a lower-case name is never taken for a type", !Has(Text(One("extends Node\n\nfunc f():\n\tvar color = 3\n\treturn color\n")), "Color color"));
    }

    // ---- C5 ----

    private static void Coercions()
    {
        ConversionResult r = One(
            "extends Node\n\nconst CHUNK := 16\n\nfunc grid(v: Vector3) -> Vector3i:\n\treturn Vector3i((v / 2.0).floor())\n\nfunc back(c: Vector3i) -> Vector3:\n\treturn Vector3(c) * 2.0\n\n" +
            "func edge(x: int, y: int) -> Vector3i:\n\treturn Vector3i(x, y, CHUNK - 1)\n\nfunc whole(n: int) -> float:\n\treturn round(n) + floor(n) + ceil(n)\n\n" +
            "func rounded(f: float) -> int:\n\treturn int(round(f * 100))\n\nfunc transparent(id: int) -> int:\n\treturn id == 0 or (id > 25 and id < 30)\n\nfunc flag(b: bool) -> int:\n\tvar n: int = b\n\treturn n\n", keep: true);
        string t = Text(r);
        T.Check("it compiles (before: CS7036, CS1503, CS0121, CS0029)", r.Success, Errors(r));
        T.Check("Vector3i(vector3) and Vector3(vector3i) are the conversions the types declare", Has(t, "(Vector3I)", "(Vector3)c"), t);
        T.Check("a long constant expression narrowed for an int parameter is cast", t.Contains("(int)(Chunk - 1)"), t);
        T.Check("round / floor / ceil of an int are the int", !t.Contains("Math.Round(n") && !t.Contains("Math.Floor(n") && !t.Contains("Math.Ceiling(n"), t);
        T.Check("a bool where an int is wanted is 1 or 0", Has(t, "? 1 : 0"), t);
        if (!r.Success) return;
        var cell = (Vector3I)Call(r, "Sample", "Grid", new Vector3(5.2f, -3.5f, 9f))!;
        T.Check("grid(5.2, -3.5, 9) = (2, -2, 4): floor of half", cell.X == 2 && cell.Y == -2 && cell.Z == 4, cell.ToString());
        T.Check("back((1, 2, 3)) = (2, 4, 6)", (Vector3)Call(r, "Sample", "Back", new Vector3I(1, 2, 3))! == new Vector3(2, 4, 6));
        T.Check("edge(3, 4) = (3, 4, 15)", (Vector3I)Call(r, "Sample", "Edge", 3L, 4L)! == new Vector3I(3, 4, 15));
        T.Check("whole(7) = 21", Math.Abs((double)Call(r, "Sample", "Whole", 7L)! - 21.0) < 1e-9);
        T.Check("rounded(2.675) = 268: half away from zero", (long)Call(r, "Sample", "Rounded", 2.675)! == 268);
        T.Check("transparent(27) = 1, transparent(10) = 0, flag(true) = 1", (long)Call(r, "Sample", "Transparent", 27L)! == 1 && (long)Call(r, "Sample", "Transparent", 10L)! == 0 && (long)Call(r, "Sample", "Flag", true)! == 1);
    }

    // godot-fps's look reset its rotation with `transform.basis = Basis()`: a zero basis left its player unable to turn or walk.
    private static void IdentityConstructors()
    {
        ConversionResult r = One(
            "extends Node\n\nfunc b() -> Basis:\n\treturn Basis()\n\nfunc t() -> Transform3D:\n\treturn Transform3D()\n\nfunc q() -> Quaternion:\n\treturn Quaternion()\n\n" +
            "func t2() -> Transform2D:\n\treturn Transform2D()\n\nfunc c() -> Color:\n\treturn Color()\n\nfunc v() -> Vector3:\n\treturn Vector3()\n\n" +
            "func turned(a: float) -> Vector3:\n\tvar n := Node3D.new()\n\tn.transform.basis = Basis()\n\tn.rotate_y(a)\n\tvar d := n.transform.basis * Vector3(0, 0, -1)\n\tn.free()\n\treturn d\n", keep: true);
        string t = Text(r);
        T.Check("it compiles", r.Success, Errors(r));
        T.Check("the zero-argument constructors are the identities and opaque black", Has(t, "Basis.Identity", "Transform3D.Identity", "Quaternion.Identity", "Transform2D.Identity", "new Color(0, 0, 0, 1)") && !t.Contains("new Basis()"), t);
        if (!r.Success) return;
        T.Check("Basis() is the identity", (Basis)Call(r, "Sample", "B")! == Basis.Identity);
        T.Check("Transform3D() and Quaternion() are the identity", (Transform3D)Call(r, "Sample", "T")! == Transform3D.Identity && (Quaternion)Call(r, "Sample", "Q")! == Quaternion.Identity);
        T.Check("Color() is opaque black", (Color)Call(r, "Sample", "C")! == new Color(0, 0, 0, 1));
        T.Check("Vector3() stays zero", (Vector3)Call(r, "Sample", "V")! == Vector3.Zero);
        var fwd = (Vector3)Call(r, "Sample", "Turned", 0.0)!;
        T.Check("a node reset to Basis() still turns and points forward", T.Near(fwd, new Vector3(0, 0, -1), 1e-5f), fwd.ToString());
    }

    // ---- C6 ----

    private static void BaseTypedReceivers()
    {
        ConversionResult r = One(
            "extends Node\n\n@export var holder: PackedScene\nvar mat: Material = ShaderMaterial.new()\nvar res: Resource = PackedScene.new()\n\nfunc f(vp: Viewport) -> void:\n" +
            "\tmat.set_shader_parameter(&\"strength\", 0.5)\n\tvar n := res.instantiate()\n\tvp.content_scale_size = Vector2i(320, 200)\n\tvar s := vp.content_scale_size\n\tprint(n, s)\n");
        string t = Text(r);
        T.Check("it compiles (before: Material, Resource and Viewport lacked the members)", r.Success, Errors(r));
        T.Check("Material.set_shader_parameter goes through ShaderMaterial", t.Contains("((ShaderMaterial)Mat).SetShaderParameter("), t);
        T.Check("Resource.instantiate goes through PackedScene", t.Contains("((PackedScene)Res).Instantiate()"), t);
        T.Check("Viewport.content_scale_size is the Window's, read and written", Has(t, "((Window)vp).ContentScaleSize = ", "= ((Window)vp).ContentScaleSize;"), t);
    }

    // ---- C7 ----

    private static void AutoloadAsType()
    {
        string root = Temp("autoload");
        try
        {
            string proj = Path.Combine(root, "Game");
            string outDir = Path.Combine(root, "Out");
            Directory.CreateDirectory(proj);
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(proj, "project.godot"), "config_version=5\n\n[autoload]\n\nLog=\"*res://log.gd\"\n");
            File.WriteAllText(Path.Combine(proj, "log.gd"),
                "extends Node\n\nenum LogType { INFO, WARNING }\n\nsignal entry(message: String, type: LogType)\n\nfunc write(message: String, type: LogType = LogType.INFO) -> void:\n\tentry.emit(message, type)\n");
            File.WriteAllText(Path.Combine(proj, "panel.gd"),
                "extends Node\n\nvar last_type: Log.LogType = Log.LogType.INFO\n\nfunc show_entry(message: String, type: Log.LogType) -> void:\n\tlast_type = type\n\nfunc _ready() -> void:\n\tLog.entry.connect(show_entry)\n\tLog.write(\"hello\", Log.LogType.WARNING)\n");
            var so = new StringWriter();
            var se = new StringWriter();
            int code = ConvertCli.Run(new[] { "scripts", "--project", proj, "--out", outDir, "--refs", Refs }, so, se);
            T.Check("an autoload's enum is a type in another script, and a script enum can be a signal argument (before: CS0246, CS1503)", code == 0, se.ToString());
            string panel = File.Exists(Path.Combine(outDir, "Content", "Scripts", "Panel.cs")) ? File.ReadAllText(Path.Combine(outDir, "Content", "Scripts", "Panel.cs")) : "";
            T.Check("the enum is written as the script's nested type", Has(panel, "public Log.LogType LastType", "Log.LogType.Warning"), panel);
            string log = File.Exists(Path.Combine(outDir, "Content", "Scripts", "Log.cs")) ? File.ReadAllText(Path.Combine(outDir, "Content", "Scripts", "Log.cs")) : "";
            T.Check("the signal helper passes the enum as the integer", log.Contains("EmitSignal(SignalName.Entry, message, (long)type)"), log);
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }

    // ---- C8 ----

    private static void StaticNeedsInstance()
    {
        ConversionResult r = One(
            "extends Node\n\nvar hits := 0\n\nfunc ping() -> void:\n\thits += 1\n\nstatic func helper(n: Node) -> void:\n\tcall(\"ping\")\n\nstatic func plain(a: int, b: int) -> int:\n\treturn a + b\n\nfunc run() -> int:\n\thelper(self)\n\thelper(self)\n\treturn hits + plain(1, 2)\n", keep: true);
        string t = Text(r);
        T.Check("it compiles (before: CS0120, an instance call from a static method)", r.Success, Errors(r));
        T.Check("the function that calls by name is an instance method, the plain one stays static", Has(t, "public void Helper(Node n)", "public static long Plain(long a, long b)") && !t.Contains("static void Helper"), t);
        T.Check("the change is reported", r.Items.Any(i => i.Type == "function" && i.Property == "helper" && i.Rating == Rating.Approximated));
        T.Check("and it runs: two pings and 1 + 2 = 5", r.Success && (long)Call(r, "Sample", "Run")! == 5);
    }

    // ---- C9 ----

    private static void PackedArrayMethods()
    {
        ConversionResult r = One(
            "extends Node\n\nvar names := PackedStringArray()\nvar points: PackedVector3Array = PackedVector3Array()\nvar bytes: PackedByteArray\n\nfunc run() -> int:\n\tvar a := PackedInt32Array()\n" +
            "\ta.push_back(3)\n\ta.append(1)\n\ta.append(2)\n\ta.sort()\n\ta.resize(5)\n\ta.insert(0, 9)\n\ta.remove_at(1)\n\treturn a[0] * 100 + a[1] * 10 + a.size()\n\n" +
            "func collect() -> String:\n\tnames.push_back(\"pear\")\n\tnames.append(\"apple\")\n\tnames.sort()\n\tpoints.push_back(Vector3(1, 2, 3))\n\tpoints.resize(2)\n\tbytes.append(200)\n\tbytes.append_array(PackedByteArray([1, 2]))\n" +
            "\treturn \",\".join(names) + str(points.size()) + str(bytes.size()) + str(names.find(\"pear\"))\n", keep: true);
        string t = Text(r);
        T.Check("it compiles (before: the methods were left as PushBack / Append on a C# array)", r.Success, Errors(r));
        T.Check("a method that changes the length assigns the new array back", Has(t, "a = PackedArrays.Append(a, 3);", "a = PackedArrays.Resize(a, 5);", "Names = PackedArrays.Append(Names, \"pear\");"), t);
        T.Check("sort works in place", t.Contains("PackedArrays.Sort(a);"), t);
        T.Check("the growth is reported as approximated: other holders of the old array do not see it", r.Items.Any(i => i.Type == "call" && i.Property == "push_back" && i.Rating == Rating.Approximated));
        if (!r.Success) return;
        T.Check("run() = 925: [3,1,2] sorted, resized to 5, 9 inserted at 0, element 1 removed -> [9,2,3,0,0]", (long)Call(r, "Sample", "Run")! == 925);
        T.Check("collect(): strings sorted, vectors resized, bytes appended", (string)Call(r, "Sample", "Collect")! == "apple,pear231", (string)Call(r, "Sample", "Collect")!);
    }

    // ---- C13, C18: the values the scene converter writes (tests/fabric FabricConverterBugsTest checks the text) ----

    private static void ConvertedResourceValues()
    {
        var rb = new RigidBody3D();
        rb.PendingValues = new List<(string, string)>
        {
            ("physics_material_override", "Object(PhysicsMaterial, \"friction\": 0.5, \"rough\": true, \"bounce\": 0.25)"),
            ("axis_lock_linear_x", "true"), ("axis_lock_angular_z", "true"),
        };
        SceneTree.Instance.Root.AddChild(rb);
        T.Check("a rigid body takes the material's friction, bounce and rough flag from the converted value",
                rb.PhysicsMaterialOverride is { Friction: 0.5f, Bounce: 0.25f, Rough: true, Absorbent: false }, rb.PhysicsMaterialOverride?.Friction.ToString() ?? "no material");
        T.Check("and the axis locks the converter rates converted", rb.AxisLockLinearX && rb.AxisLockAngularZ && !rb.AxisLockLinearY && rb.AnyAxisLock);

        var wall = new StaticBody3D();
        wall.PendingValues = new List<(string, string)> { ("physics_material_override", "Object(PhysicsMaterial, \"friction\": 0.2, \"bounce\": 0.6)") };
        SceneTree.Instance.Root.AddChild(wall);
        T.Check("a static body the same, with the flags it left out at Godot's defaults", wall.PhysicsMaterialOverride is { Friction: 0.2f, Bounce: 0.6f, Rough: false, Absorbent: false });

        var button = new Button();
        button.PendingValues = new List<(string, string)> { ("shortcut", "Object(Shortcut, \"events\": [Object(InputEventAction, \"action\": &\"ui_cancel\")])") };
        SceneTree.Instance.Root.AddChild(button);
        Shortcut? shortcut = button.Shortcut;
        T.Check("a button's shortcut holds an InputEventAction with its action name", shortcut != null && shortcut.HasValidEvent() && shortcut.Events[0].AsGodotObject() is InputEventAction { Action: var a } && a == "ui_cancel");
        T.Check("and it matches that action when it is pressed", shortcut != null && shortcut.MatchesEvent(new InputEventAction { Action = "ui_cancel", Pressed = true }) && !shortcut.MatchesEvent(new InputEventAction { Action = "ui_accept", Pressed = true }));
    }

    // ---- C10 ----

    private static void UtilityFunctions()
    {
        ConversionResult r = One(
            "extends Node\n\nfunc run() -> Array:\n\tvar out := []\n\tout.append(asinh(1.0))\n\tout.append(acosh(1.0))\n\tout.append(atanh(0.0))\n\tout.append(rotate_toward(0.0, 1.0, 0.25))\n\tout.append(rotate_toward(0.0, 3.0, 5.0))\n" +
            "\tout.append(type_convert(\"42\", TYPE_INT))\n\tout.append(type_convert(7, TYPE_STRING))\n\tout.append(bytes_to_var(var_to_bytes(Vector2(1, 2))))\n\tout.append(Color8(255, 0, 0).r)\n\tout.append(Color8(0, 51, 0, 255).g)\n\treturn out\n", keep: true);
        string t = Text(r);
        T.Check("it compiles (before: each was called by name on the node)", r.Success, Errors(r));
        T.Check("each maps to its twin", Has(t, "Math.Asinh(1.0)", "GDScript.RotateToward(", "GDScript.TypeConvert(", "GD.VarToBytes(", "GD.BytesToVar(", "Color.Color8(255, 0, 0)"), t);
        if (!r.Success) return;
        var v = (Aver.Fabric.Collections.Array)Call(r, "Sample", "Run")!;
        T.Check("asinh(1) = 0.88137, acosh(1) = 0, atanh(0) = 0", Math.Abs(v[0].AsDouble() - 0.881373587) < 1e-6 && v[1].AsDouble() == 0.0 && v[2].AsDouble() == 0.0);
        T.Check("rotate_toward stops at the target: 0.25 and 3.0", Math.Abs(v[3].AsDouble() - 0.25) < 1e-9 && Math.Abs(v[4].AsDouble() - 3.0) < 1e-9);
        T.Check("type_convert(\"42\", TYPE_INT) = 42 and type_convert(7, TYPE_STRING) = \"7\"", v[5].AsInt64() == 42 && v[6].VariantType == Variant.Type.String && v[6].AsString() == "7", $"{v[5]} ({v[5].VariantType}), {v[6]} ({v[6].VariantType})");
        T.Check("var_to_bytes then bytes_to_var returns the vector", v[7].AsVector2() == new Vector2(1, 2));
        T.Check("Color8 channels: 255 is 1.0, 51 is 0.2", Math.Abs(v[8].AsDouble() - 1.0) < 1e-6 && Math.Abs(v[9].AsDouble() - 0.2) < 1e-6);
    }
}
