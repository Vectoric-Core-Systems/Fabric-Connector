// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Gaps that kept a real game's scripts from compiling (one error stops every script from being built): a member called on a declared base type that only a subclass or a
// script has, a signal reached through a base type, String methods on a StringName, a physics object where a RID is wanted, a signal emitted with more arguments than it
// declares, MeshInstance3D.get_active_material, and the Decal, AnimatedSprite3D and 2D classes. The sources are written for this suite; each is converted, compiled against
// this runtime and, where it computes something, run.

using System.Reflection;
using Aver.Fabric;
using Aver.Fabric.Convert;

static class ConverterGapTests
{
    public static int RunAll() => T.Suite(() =>
    {
        T.Run("converter gap: a member the declared base type lacks is called, read and written by name", MembersByName);
        T.Run("converter gap: a signal of a script reached through Control connects and fires", SignalThroughBase);
        T.Run("converter gap: emit() with more arguments than the signal declares delivers them", EmitWithExtraArguments);
        T.Run("converter gap: String methods run on a StringName, and an is-pattern local stays assigned", StringNameMethods);
        T.Run("converter gap: [self] where Array[RID] is wanted is the object's RID", RidFromObject);
        T.Run("converter gap: MeshInstance3D.get_active_material and the surface override materials", ActiveMaterial);
        T.Run("converter gap: Decal, AnimatedSprite3D, SpriteFrames and the 2D placeholder types compile", NewClassesCompile);
        T.Run("converter gap: Decal maps its box, pose, colour and fade to the engine's decal", DecalFields);
        T.Run("converter gap: AnimatedSprite3D plays SpriteFrames with Godot's frame clock and signals", AnimatedSpritePlayback);
        T.Run("converter gap: ProjectSettings.get_setting returns every setting of project.godot, with Godot's type", ProjectSettingsKept);
        T.Run("converter gap: a second Play does not leave the first session's autoloads and scene in the tree", SecondSessionTree);
        T.Run("converter gap: a stopped session's tree, GUI, pointer and resources are gone before the next session's nodes enter", StoppedSessionSlate);
        T.Run("converter gap: custom Resource classes in a scene (an Array[WeaponSlot] of slots holding weapon data) are built as the script classes", CustomResourcesInScene);
        T.Run("converter gap: a script's load() of a custom resource file builds its class from the value the converter wrote", CustomResourceLoadedByPath);
        T.Run("converter gap: a 3D node under a non-3D parent (a SubViewport's camera below a CanvasLayer of the moving player) is placed in the world directly", ThreeDUnderNonThreeD);
        T.Run("converter gap: an exported Array[Node3D] stored as NodePaths holds the nodes by _ready (bukkbeek's weapons_array)", NodeArrayExport);
        T.Run("converter gap: Godot 4.5 @abstract classes and functions, and BaseMaterial3D's flags and features by number (COGITO)", AbstractAndMaterialFlags);
        T.Run("converter gap: an addon's editor-only scripts (EditorPlugin, EditorInterface, and what loads them) are left out of the game (COGITO)", EditorOnlyScripts);
        T.Run("converter gap: preload(\"./x.gd\") from the script's own folder, and `var _name` with a setter (COGITO)", RelativePreloadAndUnderscoreSetter);
        T.Run("converter gap: twelve GDScript shapes that wrote C# that did not compile (COGITO round two)", GdScriptShapesRoundTwo);
        T.Run("converter gap: classes as values, Signal parameters, Godot 3 names, nested enums and Array conversions (COGITO round three)", GdScriptShapesRoundThree);
        T.Run("converter gap: the runtime calls COGITO makes (key names, settings, singletons, weights, images, zips, clipboard)", RuntimeCallsRoundThree);
        T.Run("converter gap: a script on a more derived node type (extends Node3D on a RigidBody3D) is based on that type; RigidBody3D stands in for static bodies", ScriptBaseFromScenes);
        T.Run("converter gap: by-name accessors (set_visible, set_albedo), editor-only calls, and Aver.Fabric internals out of a name's reach", ByNameAccessors);
        T.Run("converter gap: a base class's @onready is set when a subclass's own _ready does not call super", OnreadyThroughSubclass);
        T.Run("converter gap: ResourceSaver.save writes a game's own resource and ResourceLoader.load reads it back", ResourceSaverRoundTrip);
        T.Run("converter gap: get_node by name, a .gdshader held as a value, and Godot 4 properties scenes set (COGITO lobby)", ByNameRoundFour);
        T.Run("converter gap: a script that does not compile is left out of the build, with what depends on it, and the rest builds (Molten Aim's addon tools)", LeaveOutFailing);
    });

    private static string Refs => AppContext.BaseDirectory;

    private static ConversionResult Many(params (string Cls, string Gd)[] scripts) => ManyWith(false, scripts);

    private static ConversionResult ManyWith(bool leaveOutFailing, params (string Cls, string Gd)[] scripts) =>
        ScriptConverter.Convert(
            scripts.Select(s => new ScriptSource { ResPath = "res://" + s.Cls.ToLowerInvariant() + ".gd", Text = s.Gd, Kind = "gdscript", ClassName = s.Cls, Output = "Content/Scripts/" + s.Cls + ".cs" }).ToList(),
            new ConvertSettings { RefsDir = Refs, KeepAssembly = true, ExtraSources = System.Array.Empty<(string, string)>(), LeaveOutFailing = leaveOutFailing });

    private static void LeaveOutFailing()
    {
        // A plugin class Aver has not got (a GDExtension's), a script that uses the broken one, and one that has nothing to do with either.
        var scripts = new[]
        {
            ("GapLeaveGood", "extends Node\nfunc answer() -> int:\n\treturn 42\n"),
            ("GapLeaveBroken", "extends Node\nfunc go():\n\tvar t: NoSuchPluginClass = null\n\tprint(t)\n"),
            ("GapLeaveUser", "extends Node\nfunc go(b: GapLeaveBroken):\n\tb.go()\n"),   // typed by the broken class: it goes in the second round
        };
        ConversionResult strict = ManyWith(false, scripts);
        T.Check("without it, one script that does not compile fails the whole set", !strict.Success && strict.LeftOutScripts.Count == 0);
        ConversionResult r = ManyWith(true, scripts);
        T.Check("with it, the set succeeds", r.Success, Errors(r));
        T.Check("the broken script and the one that used it are left out, the other is not",
                r.LeftOutScripts.Contains("res://gapleavebroken.gd") && r.LeftOutScripts.Contains("res://gapleaveuser.gd") && !r.LeftOutScripts.Contains("res://gapleavegood.gd"),
                string.Join(", ", r.LeftOutScripts));
        string broken = r.Scripts.First(s => s.ResPath == "res://gapleavebroken.gd").Text;
        T.Check("a left-out file is kept, marked, inside #if FABRIC_LEFT_OUT", broken.StartsWith("// FABRIC: left out of the game's build") && broken.Contains("#if FABRIC_LEFT_OUT") &&
                broken.TrimEnd().EndsWith("#endif") && broken.Contains("NoSuchPluginClass"));
        Assembly asm = Assembly.Load(r.Assembly!);
        T.Check("and the built assembly has the good script's class only", asm.GetType("GapLeaveGood") != null && asm.GetType("GapLeaveBroken") == null);
        T.Check("the report names why", r.Items.Any(i => i.File == "res://gapleavebroken.gd" && i.Reason.Contains("left out of the game's build")));
    }

    private static string Errors(ConversionResult r) => string.Join("; ", r.Errors.Take(4).Select(e => e.ToString())) + string.Join("; ", r.GeneralErrors);

    private static string Text(ConversionResult r, int i = 0) => r.Scripts[i].Text;

    // One load per conversion: a type loaded twice is two types.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ConversionResult, Assembly> s_loaded = new();

    private static T New<T>(ConversionResult r, string cls) where T : GodotObject =>
        (T)Activator.CreateInstance(s_loaded.GetValue(r, x => Assembly.Load(x.Assembly!)).GetType(cls)!)!;

    private static object Run(object target, string method, params object?[] args)
    {
        try { return target.GetType().GetMethod(method)!.Invoke(target, args)!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    // ---- by name ----

    private static void MembersByName()
    {
        ConversionResult r = Many(
            ("Victim", "extends Node3D\nclass_name Victim\n\nvar health := 7\nvar last_dir := Vector3.ZERO\n\nfunc take_damage(amount: int, dir: Vector3) -> void:\n\tlast_dir = dir\n\thealth -= amount\n"),
            ("Dummy", "extends Node3D\nclass_name Dummy\n\nvar health := 100\n\nfunc take_damage(amount: int, dir: Vector3) -> void:\n\thealth -= amount\n"),
            ("Hitter", "extends Node\nclass_name Hitter\n\nfunc hit(body: Node3D) -> void:\n\tif body.has_method(\"take_damage\"):\n\t\tbody.take_damage(5, Vector3.UP)\n\n" +
                       "func push(body: Node3D) -> void:\n\tbody.apply_impulse(Vector3(0, 2, 0))\n\nfunc heal(body: Node3D) -> int:\n\tbody.health = body.health + 1\n\treturn body.health\n"));
        string t = Text(r, 2);
        T.Check("it compiles (before: CS1061 for take_damage, apply_impulse and health on Node3D)", r.Success, Errors(r));
        T.Check("take_damage is called by name with its arguments", t.Contains("body.Call(\"take_damage\", 5, Vector3.Up)"), t);
        T.Check("apply_impulse, which several subclasses have, is called by name", t.Contains("body.Call(\"apply_impulse\""), t);
        T.Check("a property a script declares is read and written by name", t.Contains("body.Get(\"health\")") && t.Contains("body.Set(\"health\""), t);
        T.Check("the report says they are called by name, not unsupported",
            r.Items.Any(i => i.Property == "take_damage" && i.Rating == Rating.Approximated) && !r.Items.Any(i => i.Property == "take_damage" && i.Rating == Rating.Unsupported));
        var victim = New<Node3D>(r, "Victim");
        object hitter = New<Node>(r, "Hitter");
        Run(hitter, "Hit", victim);
        T.Check("the method of the converted script class is found by its Godot name", victim.Get("health").AsInt64() == 2 && T.Near(victim.Get("last_dir").AsVector3(), Vector3.Up));
        T.Check("the property is found, read and written", (long)Run(hitter, "Heal", victim) == 3 && victim.Get("health").AsInt64() == 3);
        var body = new RigidBody3D();
        T.Check("a method of the subclass the object really is runs", !Throws(() => Run(hitter, "Push", body)));
        var plain = new Node3D();
        T.Check("a node without the method fails when it runs, as in GDScript", Throws(() => Run(hitter, "Push", plain)));
    }

    private static bool Throws(Action a)
    {
        try { a(); return false; }
        catch (Exception) { return true; }
    }

    // ---- signals ----

    private static void SignalThroughBase()
    {
        ConversionResult r = Many(
            ("PausePanel", "extends Control\nclass_name PausePanel\n\nsignal resume_game\n\nfunc fire() -> void:\n\tresume_game.emit()\n"),
            ("OptionsPanel", "extends Control\nclass_name OptionsPanel\n\nsignal resume_game\n"),
            ("Pauser", "extends Node\nclass_name Pauser\n\nvar resumed := 0\n\nfunc hook(menu: Control) -> void:\n\tif menu.has_signal(\"resume_game\"):\n\t\tmenu.resume_game.connect(on_resume)\n\nfunc on_resume() -> void:\n\tresumed += 1\n"));
        T.Check("it compiles (before: CS1061 'Control' has no ResumeGame)", r.Success, Errors(r));
        var panel = New<Control>(r, "PausePanel");
        var pauser = New<Node>(r, "Pauser");
        Run(pauser, "Hook", panel);
        Run(panel, "Fire");
        T.Check("the signal read by name connects and fires", pauser.Get("resumed").AsInt64() == 1);
        T.Check("Get(signal name) is the Signal", panel.Get("resume_game").VariantType == Variant.Type.Signal);
    }

    private static void EmitWithExtraArguments()
    {
        ConversionResult r = Many(("Health", "extends Node\nclass_name Health\n\nsignal health_updated\n\nvar got := -1\n\nfunc _ready() -> void:\n\thealth_updated.connect(_on_updated)\n\n" +
            "func _on_updated(value: int) -> void:\n\tgot = value\n\nfunc damage(h: int) -> void:\n\thealth_updated.emit(h)\n"));
        T.Check("it compiles (before: CS1501, the generated helper takes no argument)", r.Success, Errors(r));
        T.Check("the emit goes through EmitSignal with the argument", Text(r).Contains("EmitSignal(SignalName.HealthUpdated, h)"), Text(r));
        var node = New<Node>(r, "Health");
        SceneTree.Instance.Root.AddChild(node);
        Run(node, "Damage", 12L);
        T.Check("the handler receives it", node.Get("got").AsInt64() == 12);
    }

    // ---- StringName ----

    private static void StringNameMethods()
    {
        ConversionResult r = Many(("Names", "extends Node\nclass_name Names\n\nvar spots: Array = []\n\n" +
            "func lower(n: Node) -> String:\n\treturn n.name.to_lower()\n\n" +
            "func starts(n: Node) -> bool:\n\treturn n.name.begins_with(\"Cover\") and n.name.to_lower() == \"cover1\"\n\n" +
            "func collect(parent: Node) -> int:\n\tspots.clear()\n\tfor child in parent.get_children():\n\t\tif child is Marker3D and child.name.begins_with(\"Cover\"):\n\t\t\tspots.append({\"marker\": child, \"taken\": false})\n\treturn spots.size()\n"));
        T.Check("it compiles (before: CS1929, CS7036 and CS0165 on the is-pattern local)", r.Success, Errors(r));
        T.Check("the cast is parenthesised before the member access", Text(r).Contains("((string)n.Name).ToLowerInvariant()"), Text(r));
        object names = New<Node>(r, "Names");
        var cover = new Node { Name = "Cover1" };
        T.Check("to_lower and begins_with on node.name", (string)Run(names, "Lower", cover) == "cover1" && (bool)Run(names, "Starts", cover));
        var parent = new Node();
        parent.AddChild(new Marker3D { Name = "CoverA" });
        parent.AddChild(new Marker3D { Name = "CoverB" });
        parent.AddChild(new Marker3D { Name = "Other" });
        parent.AddChild(new Node3D { Name = "CoverNotAMarker" });
        T.Check("the is-pattern local is usable in the branch", (long)Run(names, "Collect", parent) == 2);
    }

    // ---- RID ----

    private static void RidFromObject()
    {
        ConversionResult r = Many(("Caster", "extends CharacterBody3D\nclass_name Caster\n\nfunc ray() -> PhysicsRayQueryParameters3D:\n\tvar query := PhysicsRayQueryParameters3D.create(Vector3.ZERO, Vector3.UP)\n" +
            "\tquery.exclude = [self]\n\treturn query\n\nfunc ray2(other: CollisionObject3D) -> PhysicsRayQueryParameters3D:\n\tvar query := PhysicsRayQueryParameters3D.create(Vector3.ZERO, Vector3.UP)\n\tquery.exclude = [get_rid(), other]\n\treturn query\n"));
        T.Check("it compiles (before: CS1950 and CS1503, Array<Rid> given the object)", r.Success, Errors(r));
        T.Check("the object is written as its RID", Text(r).Contains("this.GetRid()") && Text(r).Contains("other.GetRid()"), Text(r));
        object caster = New<CharacterBody3D>(r, "Caster");
        var query = (PhysicsRayQueryParameters3D)Run(caster, "Ray");
        T.Check("the query excludes it", query.Exclude.Count == 1);
    }

    // ---- materials ----

    private static void ActiveMaterial()
    {
        var own = new StandardMaterial3D();
        var over = new StandardMaterial3D();
        var other = new StandardMaterial3D();
        var mi = new MeshInstance3D { Mesh = new BoxMesh { Material = own } };
        T.Check("the mesh's own material is the active one", ReferenceEquals(mi.GetActiveMaterial(0), own));
        mi.SetSurfaceOverrideMaterial(0, over);
        T.Check("a surface override replaces it and reads back", ReferenceEquals(mi.GetActiveMaterial(0), over) && ReferenceEquals(mi.GetSurfaceOverrideMaterial(0), over));
        T.Check("and is the material override the engine draws", ReferenceEquals(mi.MaterialOverride, over));
        mi.SetSurfaceOverrideMaterial(0, null);
        T.Check("clearing it gives the mesh's material back", ReferenceEquals(mi.GetActiveMaterial(0), own));
        mi.SetSurfaceOverrideMaterial(2, other);
        T.Check("the override of another surface is kept and read back", ReferenceEquals(mi.GetSurfaceOverrideMaterial(2), other) && ReferenceEquals(mi.GetActiveMaterial(2), other));
        T.Check("a surface without any is null", mi.GetActiveMaterial(1) == null && mi.GetActiveMaterial(-1) == null);
        mi.MaterialOverride = over;
        T.Check("the material override wins over a surface override", ReferenceEquals(mi.GetActiveMaterial(2), over));
        ConversionResult r = Many(("Flasher", "extends Node\nclass_name Flasher\n\n@export var mesh: MeshInstance3D\n\nfunc original() -> Material:\n\tvar m = mesh.get_active_material(0)\n\tmesh.set_surface_override_material(0, m.duplicate())\n" +
            "\treturn mesh.get_surface_override_material(0)\n"));
        T.Check("a script that reads and writes them compiles (before: CS1061 GetActiveMaterial)", r.Success, Errors(r));
    }

    // ---- new classes ----

    private static void NewClassesCompile()
    {
        ConversionResult r = Many(("Spawner", "extends Node3D\nclass_name Spawner\n\nvar target: StaticBody2D\n\n" +
            "func make() -> Decal:\n\tvar d := Decal.new()\n\td.size = Vector3(0.2, 0.1, 0.2)\n\td.modulate = Color(1, 1, 1, 0.5)\n\td.albedo_mix = 0.8\n\td.cull_mask = 1\n\td.distance_fade_enabled = true\n\tadd_child(d)\n\treturn d\n\n" +
            "func is_body(n: Node) -> bool:\n\treturn n is StaticBody2D\n\n" +
            "func animate(s: AnimatedSprite3D) -> void:\n\ts.speed_scale = 2.0\n\ts.play(\"walk\")\n\ts.animation_finished.connect(on_done)\n\tprint(s.frame, s.animation, s.is_playing())\n\ts.stop()\n\n" +
            "func frames() -> SpriteFrames:\n\tvar f := SpriteFrames.new()\n\tf.add_animation(\"walk\")\n\tf.set_animation_speed(\"walk\", 12.0)\n\tf.set_animation_loop(\"walk\", false)\n\treturn f\n\nfunc on_done() -> void:\n\tpass\n"));
        T.Check("it compiles (before: CS0246 for Decal, AnimatedSprite3D, SpriteFrames and StaticBody2D)", r.Success, Errors(r));
        T.Check("the 2D type is reported unsupported", r.Items.Any(i => i.Rating == Rating.Unsupported && i.Reason.Contains("2D is not supported") && i.Property == "StaticBody2D"),
            string.Join("; ", r.Items.Select(i => i.Type + "/" + i.Property + "/" + i.Rating)));
        object spawner = New<Node3D>(r, "Spawner");
        var decal = (Decal)Run(spawner, "Make");
        T.Check("the decal is a child with the properties set", decal.GetParent() == spawner && T.Near(decal.Size, new Vector3(0.2f, 0.1f, 0.2f)) && T.Near(decal.AlbedoMix, 0.8f) && decal.DistanceFadeEnabled);
        T.Check("a StaticBody2D is a node", (bool)Run(spawner, "IsBody", new StaticBody2D()) && !(bool)Run(spawner, "IsBody", new Node()));
        var f = (SpriteFrames)Run(spawner, "Frames");
        T.Check("the frames were set up", f.HasAnimation("walk") && f.GetAnimationSpeed("walk") == 12.0 && !f.GetAnimationLoop("walk") && f.HasAnimation("default"));
    }

    // ---- Decal ----

    private static (float X, float Y, float Z) Rotate(Aver.Scene.Quat q, float x, float y, float z)
    {
        // v' = v + 2 * cross(u, cross(u, v) + w * v)
        (float cx, float cy, float cz) = (q.Y * z - q.Z * y + q.W * x, q.Z * x - q.X * z + q.W * y, q.X * y - q.Y * x + q.W * z);
        return (x + 2 * (q.Y * cz - q.Z * cy), y + 2 * (q.Z * cx - q.X * cz), z + 2 * (q.X * cy - q.Y * cx));
    }

    private static void DecalFields()
    {
        var d = new Decal { Size = new Vector3(2, 1, 2), Modulate = new Color(1, 1, 1, 0.5f), AlbedoMix = 0.8f, DistanceFadeEnabled = true, DistanceFadeBegin = 40, DistanceFadeLength = 10 };
        Aver.Scene.DecalDesc desc = d.ComputeDesc(withImages: false);
        T.Check("the box: depth is Godot's y, width its x, height its z, in centimetres", T.Near(desc.SizeCm.X, 100f) && T.Near(desc.SizeCm.Y, 200f) && T.Near(desc.SizeCm.Z, 200f), $"{desc.SizeCm.X} {desc.SizeCm.Y} {desc.SizeCm.Z}");
        T.Check("opacity is the modulate alpha, and the mix is its own weight on the colour (wave 6)", T.Near(desc.Transparency, 0.5f) && T.Near(desc.AlbedoMix, 0.8f), desc.Transparency.ToString());
        T.Check("the distance fade ends where it was set, in centimetres", T.Near(desc.FadeDistanceCm, 5000f));
        T.Check("white modulate is a white tint", T.Near(desc.Tint.X, 1f) && T.Near(desc.Tint.Z, 1f));
        (float x, float y, float z) = Rotate(desc.Rotation, 1, 0, 0);
        T.Check("it projects down Godot's -Y: the engine's +X turned to the engine's -Z", T.Near(x, 0f) && T.Near(y, 0f) && T.Near(z, -1f), $"{x} {y} {z}");
        d.RotationDegrees = new Vector3(90, 0, 0);
        (x, y, z) = Rotate(d.ComputeDesc(false).Rotation, 1, 0, 0);
        T.Check("a node turned about X to look along Godot's -Z projects along the engine's +X", T.Near(x, 1f) && T.Near(y, 0f) && T.Near(z, 0f), $"{x} {y} {z}");
        d.Position = new Vector3(1, 2, 3);
        Aver.Scene.DecalDesc moved = d.ComputeDesc(false);
        T.Check("the position is in the engine's axes and centimetres", T.Near(moved.Position.X, -300f) && T.Near(moved.Position.Y, 100f) && T.Near(moved.Position.Z, 200f));
        var tex = new Texture2D();
        d.SetTexture(Decal.DecalTexture.Orm, tex);
        T.Check("the textures are kept by slot", ReferenceEquals(d.TextureOrm, tex) && ReferenceEquals(d.GetTexture(Decal.DecalTexture.Orm), tex) && d.TextureAlbedo == null);
        SceneTree.Instance.Root.AddChild(d);
        T.Frame();
        T.Check("without an engine nothing is placed and nothing throws", d.PublishCount == 0 && d.DecalEntity == 0);
    }

    // ---- AnimatedSprite3D ----

    private static void AnimatedSpritePlayback()
    {
        var frames = new SpriteFrames();
        frames.SetAnimationSpeed("default", 10.0);
        frames.SetAnimationLoop("default", false);
        for (int i = 0; i < 3; i++) frames.AddFrame("default", null);
        var s = new AnimatedSprite3D { SpriteFrames = frames };
        int finished = 0, looped = 0, changed = 0;
        s.AnimationFinished += () => finished++;
        s.AnimationLooped += () => looped++;
        s.FrameChanged += () => changed++;
        SceneTree.Instance.Root.AddChild(s);
        T.Check("a new SpriteFrames has 'default' at 5 fps, looping", new SpriteFrames().GetAnimationSpeed("default") == 5.0 && new SpriteFrames().GetAnimationLoop("default"));
        T.Check("it is not playing until told to", !s.IsPlaying() && s.Frame == 0);
        s.Play();
        T.Check("play starts the current animation", s.IsPlaying() && T.Near(s.GetPlayingSpeed(), 10f));
        T.Frame(0.11);
        T.Check("a frame lasts 1/fps seconds", s.Frame == 1 && changed == 1, $"frame {s.Frame}");
        T.Frame(0.11);
        T.Check("and the next one follows", s.Frame == 2 && finished == 0);
        T.Frame(0.11);
        T.Check("a non-looping animation stops on its last frame and says so once", s.Frame == 2 && !s.IsPlaying() && finished == 1 && looped == 0);
        T.Frame(0.5);
        T.Check("it stays there", s.Frame == 2 && finished == 1);

        frames.SetAnimationLoop("default", true);
        s.Play();
        s.SetFrameAndProgress(2, 0.5f);
        T.Frame(0.1);
        T.Check("a looping one wraps to the first frame and says so", s.Frame == 0 && looped == 1 && s.IsPlaying(), $"frame {s.Frame} looped {looped}");

        s.SpeedScale = 2f;
        s.Frame = 0;
        T.Frame(0.06);
        T.Check("speed_scale shortens the frame", s.Frame == 1, $"frame {s.Frame}");

        s.SpeedScale = 1f;
        s.Pause();
        int frame = s.Frame;
        T.Frame(1.0);
        T.Check("pause keeps the frame", !s.IsPlaying() && s.Frame == frame);
        s.Stop();
        T.Check("stop goes back to the first frame", s.Frame == 0 && !s.IsPlaying());

        s.PlayBackwards();
        T.Check("playing backwards starts at the end", s.Frame == 2 && s.IsPlaying() && s.GetPlayingSpeed() < 0f);
        T.Frame(0.11);
        T.Check("and counts down", s.Frame == 1, $"frame {s.Frame}");

        frames.AddAnimation("jump");
        frames.AddFrame("jump", null);
        int changedAnim = 0;
        s.AnimationChanged += () => changedAnim++;
        s.Play("jump");
        T.Check("play(name) switches the animation to its first frame", s.Animation.ToString() == "jump" && s.Frame == 0 && changedAnim == 1);
        T.Check("the signals are Godot's", s.HasSignal("animation_finished") && s.HasSignal("frame_changed") && s.HasSignal("animation_looped") && s.HasSignal("sprite_frames_changed"));
        var auto = new AnimatedSprite3D { SpriteFrames = frames, Autoplay = "jump" };
        SceneTree.Instance.Root.AddChild(auto);
        T.Check("autoplay starts when it is ready", auto.IsPlaying());
    }

    // ---- ProjectSettings ----

    // MainMenuScene adds "rel v" to get_setting("application/config/version"); the converter keeps every setting in Fabric/project.json.
    private static void ProjectSettingsKept()
    {
        T.Reset();
        string root = Path.Combine(Path.GetTempPath(), "aver-fabric-settings-" + Guid.NewGuid().ToString("N"));
        string content = Path.Combine(root, "Content");
        Directory.CreateDirectory(content);
        Directory.CreateDirectory(Path.Combine(root, "Fabric"));
        try
        {
            ResourceLoader.ContentRootOverride = content;
            T.Check("no project.json: a setting with no Aver key is the caller's default", ProjectSettings.GetSetting("application/config/version", "none").AsString() == "none" && !ProjectSettings.HasSetting("application/config/version"));
            Variant nothing = ProjectSettings.GetSetting("application/config/version");
            T.Check("and without a default it is Nil, which is what made the string addition throw",
                    nothing.VariantType == Variant.Type.Nil && Throws(() => GDScript.Add("godot engine 4.x | rel v", nothing)));

            File.WriteAllText(Path.Combine(root, "Fabric", "project.json"), """
                { "schema": 1, "name": "G", "settings": {
                    "config_version": "5",
                    "application/config/name": "\"FPS Prototype\"",
                    "application/config/version": "\"0.3.1\"",
                    "game/difficulty": "3",
                    "game/scale": "1.5",
                    "game/hard": "true",
                    "game/tint": "Color(1, 0, 0, 1)",
                    "game/list": "[1, \"two\", 3.0]",
                    "physics/common/physics_ticks_per_second": "120",
                    "game/broken": "Vector3(1, 2"
                } }
                """);
            ProjectSettings.ResetForTests();
            Variant version = ProjectSettings.GetSetting("application/config/version");
            T.Check("a string setting is a String", version.VariantType == Variant.Type.String && version.AsString() == "0.3.1");
            T.Check("the script's addition now works: the footer text", GDScript.Add("godot engine 4.x | rel v", version).AsString() == "godot engine 4.x | rel v0.3.1");
            T.Check("an int stays an Int, a float a Float, a bool a Bool, a colour a Color",
                    ProjectSettings.GetSetting("game/difficulty").VariantType == Variant.Type.Int && ProjectSettings.GetSetting("game/difficulty").AsInt64() == 3 &&
                    ProjectSettings.GetSetting("game/scale").VariantType == Variant.Type.Float && ProjectSettings.GetSetting("game/scale").AsDouble() == 1.5 &&
                    ProjectSettings.GetSetting("game/hard").VariantType == Variant.Type.Bool && ProjectSettings.GetSetting("game/hard").AsBool() &&
                    ProjectSettings.GetSetting("game/tint").VariantType == Variant.Type.Color && ProjectSettings.GetSetting("game/tint").AsColor() == new Color(1, 0, 0, 1) &&
                    ProjectSettings.GetSetting("config_version").AsInt64() == 5);
            Variant list = ProjectSettings.GetSetting("game/list");
            T.Check("an array is an Array of Godot's types, a new one on each read",
                    list.VariantType == Variant.Type.Array && list.AsGodotArray().Count == 3 && !ReferenceEquals(list.AsGodotArray(), ProjectSettings.GetSetting("game/list").AsGodotArray()));
            T.Check("HasSetting knows them, and the caller's default still answers for a key the project never stated",
                    ProjectSettings.HasSetting("game/hard") && !ProjectSettings.HasSetting("game/none") && ProjectSettings.GetSetting("game/none", 7).AsInt64() == 7);
            T.Check("the project's name is Godot's own, not the Aver project's file-safe NAME", ProjectSettings.GetSetting("application/config/name").AsString() == "FPS Prototype");
            T.Check("a setting that has an Aver key keeps that mapping (physics ticks: the engine's step, not the stored text)", ProjectSettings.GetSetting("physics/common/physics_ticks_per_second").AsInt64() == 60);
            T.Logs.Clear();
            T.Check("a value that cannot be read is the caller's default", ProjectSettings.GetSetting("game/broken", 9).AsInt64() == 9);
            ProjectSettings.GetSetting("game/broken");
            T.Check("and the log says so once", T.Logs.Count(l => l.Message.Contains("game/broken")) == 1);
        }
        finally
        {
            T.Reset();
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    // ---- a second Play ----

    // The editor's Stop respawns the level's class actors, then destroys them again with a raw world destroy (no end edge) when it reloads the level: their nodes
    // stay in the tree. The next session's autoloads and scene must not be shadowed by them: current_scene is the new MainRoot, /root/GameManager the new autoload.
    private static void SecondSessionTree()
    {
        T.Reset();
        var gone = new HashSet<Node>();
        Node.EntityGoneProbe = n => gone.Contains(n);
        Window root = SceneTree.Instance.Root;

        (Node Gm, Node Sm, Node Main, Node Ui) Session()
        {
            Node gm = new() { Name = "GameManager", Persistent = true };
            Node sm = new() { Name = "SettingsManager", Persistent = true };
            Node main = new() { Name = "MainRoot" };
            Node ui = new() { Name = "UIRoot" };
            main.AddChild(ui);
            root.AddChild(gm);
            root.AddChild(sm);
            root.AddChild(main);
            return (gm, sm, main, ui);
        }

        var first = Session();
        T.Check("session 1: current_scene is MainRoot, never an autoload", SceneTree.Instance.CurrentScene == first.Main);
        T.Check("session 1: UIRoot is found under it", SceneTree.Instance.CurrentScene!.GetNodeOrNull("UIRoot") == first.Ui);

        // Stop: the engine drops the entities without telling the nodes.
        gone.UnionWith(new[] { first.Gm, first.Sm, first.Main, first.Ui });
        T.Check("after Stop nothing is the current scene (the dead MainRoot is not)", SceneTree.Instance.CurrentScene == null);
        T.Check("and the dead nodes left the tree", !first.Main.IsInsideTree() && !first.Gm.IsInsideTree() && root.GetChildCount() == 0);

        // The same again, but with the dead nodes still in the tree when the new session's nodes enter (the order the editor's reload produces).
        gone.Clear();
        var stale = Session();
        gone.UnionWith(new[] { stale.Gm, stale.Sm, stale.Main, stale.Ui });
        var second = Session();
        T.Check("session 2: the autoload keeps its name (no @GameManager@2) and is the one /root/GameManager finds",
                second.Gm.Name == "GameManager" && root.GetNodeOrNull("GameManager") == second.Gm && second.Gm.GetPath().ToString() == "/root/GameManager");
        T.Check("session 2: current_scene is the new MainRoot, whichever order the nodes entered in", SceneTree.Instance.CurrentScene == second.Main);
        T.Check("session 2: get_node(\"UIRoot\") under it is the new UIRoot", SceneTree.Instance.CurrentScene!.GetNode<Node>("UIRoot") == second.Ui);
        T.Check("session 2: the top level holds exactly the three live nodes, in order",
                string.Join(",", root.GetChildren().Select(c => c.Name.ToString())) == "GameManager,SettingsManager,MainRoot");
        T.Check("the dead session's nodes were retired, not left half alive", !stale.Gm.IsInsideTree() && !stale.Main.IsInsideTree() && !stale.Ui.IsInsideTree());
        T.Reset();
    }

    // godot-fps's second Play kept the first session's live nodes (the level, its HUD, autoloads that had readied): no _ready, no menu.
    private static void StoppedSessionSlate()
    {
        T.Reset();
        SceneTree first = SceneTree.Instance;
        Node gm = new() { Name = "GameManager", Persistent = true };
        first.Root.AddChild(gm);
        first.Root.AddChild(new Control { Name = "Hud" });
        Input.MouseMode = Input.MouseModeEnum.Captured;
        int deferred = 0;
        Callable.From(() => deferred++).CallDeferred();

        SceneTree.StartAfterEndedSession();
        T.Check("without a Stop nothing is wiped", SceneTree.Instance == first && first.Root.GetChildCount() == 2);

        SceneTree.EndSession();
        T.Check("Stop gives the pointer back at once", Input.MouseMode == Input.MouseModeEnum.Visible);
        T.Check("the tree waits for the session's other actors to retire", SceneTree.Instance == first);
        SceneTree.StartAfterEndedSession();
        SceneTree next = SceneTree.Instance;
        T.Check("the next session starts from an empty tree", next != first && next.Root.GetChildCount() == 0 && next.Root.GetNodeOrNull("GameManager") == null);
        T.Check("with no Control left from the last one", !Gui.AllControls().Any());
        T.Frame();
        T.Check("and none of its deferred calls", deferred == 0);
        SceneTree.StartAfterEndedSession();
        T.Check("the wipe happens once per Stop", SceneTree.Instance == next);
        T.Reset();
    }

    // As the converter writes Godot4-FPS-Template's weapon_stack: each slot and its weapon a "__class" dictionary naming the class its script converts to.
    private static void CustomResourcesInScene()
    {
        Variant v = VariantText.Parse("Array[ExtResource(\"6_x\")]([{\"__class\": \"GapWeaponSlot\", \"current_ammo\": 5, " +
                                      "\"weapon\": {\"__class\": \"GapWeaponData\", \"weapon_name\": \"blasterI\", \"magazine\": 30}}])");
        var holder = new GapWeaponHolder();
        T.Check("the scene value sets the typed list", MemberAccess.TrySet(holder, "weapon_stack", v));
        T.Check("the entry is built as the script class", holder.WeaponStack.Count == 1 && holder.WeaponStack[0] is GapWeaponSlot);
        GapWeaponSlot? slot = holder.WeaponStack.Count == 1 ? holder.WeaponStack[0] : null;
        T.Check("with its own values and a nested custom resource", slot is { CurrentAmmo: 5, Weapon: { WeaponName: "blasterI", Magazine: 30 } });
        T.Check("every read gives the same object, so a change to it stays (current_ammo -= 1) and find() works",
                slot != null && ReferenceEquals(holder.WeaponStack[0], slot) && holder.WeaponStack.IndexOf(slot) == 0);
        Variant one = VariantText.Parse("{\"__class\": \"GapWeaponData\", \"weapon_name\": \"blasterQ\"}");
        T.Check("a single custom resource field is built too", one.As<GapWeaponData>() is { WeaponName: "blasterQ" });
        holder.Free();
    }

    // COGITO: cogito_globals.gd checks ResourceLoader.exists() on CogitoSettings.tres, then load()s it; a null result left every menu without its settings.
    private static void CustomResourceLoadedByPath()
    {
        string dir = Path.Combine(Path.GetTempPath(), "aver-fabric-ocres-test-" + System.Environment.ProcessId);
        Directory.CreateDirectory(Path.Combine(dir, "Resources", "data"));
        string? before = ResourceLoader.ContentRootOverride;
        try
        {
            File.WriteAllText(Path.Combine(dir, "Resources", "data", "kit.ocres"), "{\n\"__class\": \"GapWeaponData\",\n\"weapon_name\": \"blasterK\",\n\"magazine\": 12\n}\n");
            ResourceLoader.ContentRootOverride = dir;
            T.Check("exists() finds it", ResourceLoader.Exists("res://data/kit.tres"));
            Resource? r = ResourceLoader.Load("res://data/kit.tres", (System.Type?)null, ResourceLoader.CacheMode.Ignore);
            T.Check("load() builds the script class with its values", r is GapWeaponData { WeaponName: "blasterK", Magazine: 12 }, r?.GetType().Name ?? "null");
            T.Check("and knows its path", r?.ResourcePath == "res://data/kit.tres");
            T.Check("a path with no file behind it is still not there", !ResourceLoader.Exists("res://data/none.tres"));
        }
        finally
        {
            ResourceLoader.ContentRootOverride = before;
            Directory.Delete(dir, true);
        }
    }

    // Godot4-FPS-Template copies the main camera's global transform onto a camera under CanvasLayer/SubViewportContainer/SubViewport every frame. The engine
    // composes an entity with all its parents, so that camera's entity keeps its parent (a path to it resolves after a level reload) and ignores the parent's
    // transform (Entity.SetTransformAbsolute, set when it enters the tree with the engine; SceneTest covers the engine side).
    private static void ThreeDUnderNonThreeD()
    {
        var player = new Node3D();
        var layer = new CanvasLayer { Name = "CanvasLayer" };
        var container = new SubViewportContainer { Name = "SubViewportContainer" };
        var view = new SubViewport { Name = "SubViewport" };
        var cam = new Camera3D { Name = "Camera3D" };
        var held = new Node3D();
        var top = new Node3D { TopLevel = true };
        player.AddChild(layer);
        layer.AddChild(container);
        container.AddChild(view);
        view.AddChild(cam);
        player.AddChild(held);
        player.AddChild(top);
        T.Check("a camera under a SubViewport keeps its parent, so its path resolves", NodeHost.EngineParent(cam) == view && player.GetNodeOrNull("CanvasLayer/SubViewportContainer/SubViewport/Camera3D") == cam);
        T.Check("a 3D child of a 3D node hangs under it", NodeHost.EngineParent(held) == player);
        // COGITO's player: SwimmingHeadShapeCast (top_level) under the camera. A detached entity was saved by the Play snapshot as a level root, so
        // after Stop the player's $Body/Neck/Head/Eyes/Camera/SwimmingHeadShapeCast was gone.
        T.Check("a top-level node keeps its parent too (only its transform is its own)", NodeHost.EngineParent(top) == player);
        T.Check("non-3D nodes keep their parent", NodeHost.EngineParent(container) == layer && NodeHost.EngineParent(view) == container);
        player.Position = new Vector3(10, 0, 0);
        cam.GlobalTransform = new Transform3D(Basis.Identity, new Vector3(10, 2, 0));
        T.Check("its global transform is its own, whatever the player above does", cam.GlobalTransform.Origin == new Vector3(10, 2, 0) && cam.Transform.Origin == new Vector3(10, 2, 0));
        player.Free();
    }

    // bukkbeek/GodotFPS-Template: weapons_array = [NodePath("Knife"), NodePath("Pistol")], its own children, which enter the tree after it.
    private static void NodeArrayExport()
    {
        var manager = new GapWeaponsManager { Name = "WeaponsManager" };
        var knife = new Node3D { Name = "Knife" };
        var pistol = new Node3D { Name = "Pistol" };
        manager.PendingValues = new List<(string, string)> { ("weapons_array", "[NodePath(\"Knife\"), NodePath(\"Pistol\"), NodePath(\"Missing\")]") };
        manager.AddChild(knife);
        manager.AddChild(pistol);
        SceneTree.Instance.Root.AddChild(manager);
        T.Check("the list holds the nodes when _Ready runs (a missing one is left out)", manager.SeenAtReady == 2, manager.SeenAtReady.ToString());
        T.Check("in their order", manager.WeaponsArray.Count == 2 && ReferenceEquals(manager.WeaponsArray[0], knife) && ReferenceEquals(manager.WeaponsArray[1], pistol));
        manager.Free();
    }

    // COGITO: `@abstract class_name CogitoPlayer` lost its `extends CharacterBody3D` (the parser rejected the annotation and skipped the next line), and
    // `@abstract func create_new_state() -> CogitoPlayerState` has no body. Its material tool reads FLAG_* / FEATURE_* inherited from BaseMaterial3D.
    private static void AbstractAndMaterialFlags()
    {
        ConversionResult r = Many(
            ("GapShape", "@abstract class_name GapShape\nextends Node3D\n\n@abstract func area() -> float\n\n@abstract func reset()\n\nfunc doubled() -> float:\n\treturn area() * 2.0\n"),
            ("GapSquare", "class_name GapSquare\nextends GapShape\n\nvar side := 3.0\n\nfunc area() -> float:\n\treturn side * side\n\nfunc reset():\n\tside = 1.0\n"),
            ("GapMatFlags", "class_name GapMatFlags\nextends StandardMaterial3D\n\nfunc run() -> int:\n\tset_flag(FLAG_DISABLE_DEPTH_TEST, true)\n\tset_flag(FLAG_USE_Z_CLIP_SCALE, true)\n" +
                "\tset_feature(FEATURE_RIM, true)\n\tvar n := 0\n\tif no_depth_test: n += 1\n\tif get_flag(FLAG_USE_Z_CLIP_SCALE): n += 2\n\tif rim_enabled and get_feature(FEATURE_RIM): n += 4\n" +
                "\tif not get_feature(FEATURE_REFRACTION): n += 8\n\treturn n\n"));
        T.Check("it compiles (before: GapShape became a RefCounted, the bodiless functions and FLAG_* did not compile)", r.Success, Errors(r));
        if (!r.Success) return;
        Node3D sq = New<Node3D>(r, "GapSquare");
        T.Check("the abstract class keeps its base (GapSquare is a Node3D through GapShape)", sq.GetType().BaseType?.Name == "GapShape");
        T.Check("the subclass's function is what the base's call reaches: 3 * 3 * 2", (double)sq.GetType().GetMethod("Doubled")!.Invoke(sq, null)! == 18.0);
        sq.GetType().GetMethod("Reset")!.Invoke(sq, null);
        T.Check("a bodiless function without a return value is overridden too", (double)sq.GetType().GetMethod("Doubled")!.Invoke(sq, null)! == 2.0);
        sq.Free();
        BaseMaterial3D mat = (BaseMaterial3D)Activator.CreateInstance(s_loaded.GetValue(r, x => Assembly.Load(x.Assembly!)).GetType("GapMatFlags")!)!;
        T.Check("set_flag / set_feature reach the properties, a flag without one is kept: 1 + 2 + 4 + 8", (long)mat.GetType().GetMethod("Run")!.Invoke(mat, null)! == 15);
    }

    // COGITO's addons carry editor plugins (EditorPlugin, EditorInterface, EditorUndoRedoManager): about 200 of its compile errors. A project class whose
    // own name starts with Editor (its EditorSidebar) is not Godot's.
    private static void EditorOnlyScripts()
    {
        ConversionResult r = Many(
            ("GapPlugin", "@tool\nclass_name GapPlugin\nextends EditorPlugin\n\nfunc _enter_tree():\n\tEditorInterface.get_selection()\n"),
            ("GapDock", "@tool\nclass_name GapDock\nextends Control\n\nvar plugin: GapPlugin\n"),
            ("GapLoader", "extends Node\n\nconst DOCK = preload(\"res://gapplugin.gd\")\n"),
            ("EditorLikeThing", "class_name EditorLikeThing\nextends Node\n\nfunc value() -> int:\n\treturn 7\n"),
            ("GapGame", "class_name GapGame\nextends Node\n\nvar thing: EditorLikeThing\n\nfunc run() -> int:\n\treturn 1\n"));
        T.Check("it compiles (before: EditorPlugin and EditorInterface did not exist)", r.Success, Errors(r));
        string[] kept = r.Scripts.Select(s => s.ClassName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        T.Check("the game keeps its own scripts, a class named Editor... included", string.Join(",", kept) == "EditorLikeThing,GapGame", string.Join(",", kept));
        string[] left = r.LeftOut.Select(i => i.File).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        T.Check("the plugin, the dock that names it and the script that preloads it are left out, each reported",
            string.Join(",", left) == "res://gapdock.gd,res://gaploader.gd,res://gapplugin.gd" && r.Items.Count(i => i.Reason.StartsWith("left out of the game")) == 3, string.Join(",", left));
    }

    // COGITO: `const InputMapUpdater = preload("./input_map_updater.gd")` then InputMapUpdater.new() (a plain Resource before), and
    // `var _interactable = null: set = _set_interactable`, whose property and backing field were both named _interactable.
    private static void RelativePreloadAndUnderscoreSetter()
    {
        ConversionResult r = Many(
            ("GapHelper", "extends RefCounted\n\nfunc value() -> int:\n\treturn 5\n"),
            ("GapUser", "extends Node\n\nconst Helper = preload(\"./gaphelper.gd\")\nconst Same = preload(\"../gaphelper.gd\")\n\nvar sets := 0\n" +
                "var _target = null:\n\tset = _set_target\n\nfunc _set_target(v) -> void:\n\t_target = v\n\tsets += 1\n\n" +
                "func run() -> int:\n\t_target = 3\n\treturn Helper.new().value() * 100 + Same.new().value() * 10 + sets + int(_target)\n"));
        T.Check("it compiles (before: Resource has no New, and _target was declared twice)", r.Success, Errors(r));
        if (!r.Success) return;
        Node user = New<Node>(r, "GapUser");
        T.Check("both preloads are the script, and the setter runs once: 500 + 50 + 1 + 3", (long)user.GetType().GetMethod("Run")!.Invoke(user, null)! == 554);
        user.Free();
    }

    // COGITO, round two: shapes GDScript accepts that the converter wrote as C# that did not compile.
    private static void GdScriptShapesRoundTwo()
    {
        ConversionResult r = Many(
            ("GapGroup", "class_name GapGroup\nextends Node\n\nvar pool := \"\"\n\nfunc _init(pool_name: String):\n\tpool = pool_name\n"),
            ("GapActive", "class_name GapActive\nextends GapGroup\n"),
            ("GapAlerts", "class_name GapAlerts\nextends Node3D\n\nsignal done\n\nconst LIMIT := 64\n\nenum Mode { CALM, ALERT, PANIC }\n" +
                "enum Mask { NONE = 0, A = 1 << (Mode.ALERT - 1), B = 1 << (Mode.PANIC - 1) }\n\nvar seen := 0\n\n" +
                "func on_input(e: InputEvent) -> void:\n\tseen += 1\n\nfunc _done() -> void:\n\tdone.emit()\n"),
            ("GapShapes", "class_name GapShapes\nextends Control\n\nconst Group = preload(\"./gapactive.gd\")\nvar modes = GapAlerts.Mode\nvar hits := 0\n" +
                "var flag := false:\n\tset(v):\n\t\tflag = v\n\t\tawait get_tree().process_frame\n\t\thits += 100\n\n" +
                "func pick(m: int) -> String:\n\tmatch m:\n\t\tmodes.CALM:\n\t\t\treturn \"calm\"\n\t\tmodes.CALM:\n\t\t\treturn \"never\"\n\t\tmodes.ALERT:\n\t\t\treturn \"alert\"\n\treturn \"other\"\n\n" +
                "func nothing() -> Node:\n\tif hits > 1000:\n\t\treturn get_parent()\n\treturn\n\n" +
                "func tagged(tag: StringName = \"\") -> String:\n\treturn str(tag)\n\n" +
                "func run() -> int:\n\tvar g: Group = Group.new(\"pool\")\n\tvar body := StaticBody3D.new()\n\tbody.collision_mask = GapAlerts.Mask.B\n\tvar n := 0\n" +
                "\tif g.pool == \"pool\": n += 1\n\tif pick(GapAlerts.Mode.ALERT) == \"alert\" and pick(0) == \"calm\": n += 2\n\tif nothing() == null: n += 4\n" +
                "\tif tagged() == \"\" and tagged(\"x\") == \"x\": n += 8\n\tif body.collision_mask == 2: n += 16\n\tif not (self is RigidBody3D): n += 32\n" +
                "\tvar a := GapAlerts.new()\n\ta.on_input(InputEventKey.new())\n\tif a.seen == 1 and a.LIMIT == 64: n += 64\n\tbody.free()\n\ta.free()\n\tg.free()\n" +
                "\tflag = true\n\treturn n + hits\n"));
        T.Check("it compiles (before: override OnInput, an enum in a var, uint from long, a long enum, a repeated match value, a bare return, " +
                "self is an unrelated class, a const as a type, Sub.new(args) with no _init, await in a setter, StringName = \"\", signal done beside _done)",
                r.Success, Errors(r));
        if (!r.Success) return;
        var shapes = New<Control>(r, "GapShapes");
        SceneTree.Instance.Root.AddChild(shapes);
        T.Check("and runs as GDScript does: 1 + 2 + 4 + 8 + 16 + 32 + 64, the setter stopped at its await", (long)shapes.GetType().GetMethod("Run")!.Invoke(shapes, null)! == 127);
        shapes.Free();
    }

    private static string SceneNode(string path, string @base, string cls, string script) =>
        $"{{\"path\":\"{path}\",\"base\":\"{@base}\",\"class\":\"{cls}\",\"script\":\"{script}\",\"owner\":\"\",\"unique\":false,\"packed\":{{}}}}";

    // COGITO: cogito_object.gd (extends Node3D) is on RigidBody3D pickups and StaticBody3D props, cogito_door.gd on static and animatable bodies,
    // the options menu (extends Control) on a MarginContainer. Godot keeps each node's type; the class is the node, so it takes the widest one.
    private static void ScriptBaseFromScenes()
    {
        string nodes = "{\"schema\":1,\"scenes\":[{\"scene\":\"res://a.tscn\",\"nodes\":[" + string.Join(",",
            SceneNode(".", "RigidBody3D", "GapPickup", "res://gappickup.gd"), SceneNode("Shelf", "StaticBody3D", "GapPickup", "res://gappickup.gd"),
            SceneNode("Bolt", "RigidBody3D", "GapBolt", "res://gapbolt.gd"),
            SceneNode("Door", "StaticBody3D", "GapDoor", "res://gapdoor.gd"), SceneNode("Lift", "AnimatableBody3D", "GapDoor", "res://gapdoor.gd"),
            SceneNode("Menu", "MarginContainer", "GapMenu", "res://gapmenu.gd"), SceneNode("Plain", "Node3D", "GapPlain", "res://gapplain.gd")) + "]}]}";
        var sources = new (string Cls, string Gd)[]
        {
            ("GapPickup", "extends Node3D\nclass_name GapPickup\nfunc weight() -> float:\n\treturn 1.0\n"),
            ("GapBolt", "extends GapPickup\nclass_name GapBolt\n"),
            ("GapDoor", "extends Node3D\nclass_name GapDoor\n"),
            ("GapMenu", "extends Control\nclass_name GapMenu\n"),
            ("GapPlain", "extends Node3D\nclass_name GapPlain\n"),
        };
        ConversionResult r = ScriptConverter.Convert(
            sources.Select(x => new ScriptSource { ResPath = "res://" + x.Cls.ToLowerInvariant() + ".gd", Text = x.Gd, Kind = "gdscript", ClassName = x.Cls, Output = "Content/Scripts/" + x.Cls + ".cs" }).ToList(),
            new ConvertSettings { RefsDir = Refs, KeepAssembly = true, ExtraSources = System.Array.Empty<(string, string)>(), SceneNodes = nodes });
        T.Check("compiles", r.Errors.Count == 0 && r.GeneralErrors.Count == 0, Errors(r));
        string Cs(string cls) => r.Scripts.First(x => x.ClassName == cls).Text;
        T.Check("rigid and static bodies (counting a derived script's nodes) share RigidBody3D", Cs("GapPickup").Contains(": RigidBody3D") && Cs("GapBolt").Contains(": GapPickup"), Cs("GapPickup"));
        T.Check("static and animatable bodies are one line: AnimatableBody3D", Cs("GapDoor").Contains(": AnimatableBody3D"));
        T.Check("a Control script on a MarginContainer is a MarginContainer", Cs("GapMenu").Contains(": MarginContainer"));
        T.Check("a script placed on its own type stays as declared", Cs("GapPlain").Contains(": Node3D"));
        if (r.Errors.Count > 0) return;
        var shelf = New<RigidBody3D>(r, "GapPickup");
        shelf.ApplyValues(new[] { ("_native", "\"StaticBody3D\"") });
        T.Check("where the scene had a static body the RigidBody3D is frozen, static", shelf.Freeze && shelf.FreezeMode == RigidBody3D.FreezeModeEnum.Static);
        var lift = new RigidBody3D();
        lift.ApplyValues(new[] { ("_native", "\"AnimatableBody3D\"") });
        T.Check("an animatable body: frozen, kinematic", lift.Freeze && lift.FreezeMode == RigidBody3D.FreezeModeEnum.Kinematic);
        var zone = new RigidBody3D();
        zone.ApplyValues(new[] { ("_native", "\"Area3D\"") });
        T.Check("an area: frozen and a sensor", zone.Freeze && zone.IsSensor);
        var rigid = new RigidBody3D();
        rigid.ApplyValues(new[] { ("_native", "\"RigidBody3D\"") });
        T.Check("a rigid body stays free", !rigid.Freeze && !rigid.IsSensor);
        foreach (GodotObject o in new GodotObject[] { shelf, lift, zone, rigid }) o.Free();
    }

    // COGITO: HoldInteraction has `@onready var parent_node = get_parent()`; DualInteraction extends it with its own _ready and no super().
    private static void OnreadyThroughSubclass()
    {
        ConversionResult r = Many(
            ("GapHold", "extends Node3D\nclass_name GapHold\n@onready var parent_node = get_parent()\nvar base_ready := 0\nfunc _ready():\n\tbase_ready += 1\n"),
            ("GapDual", "extends GapHold\nclass_name GapDual\n@onready var own := get_parent()\nvar seen: Node\nfunc _ready():\n\tseen = parent_node\n"));
        T.Check("compiles", r.Errors.Count == 0 && r.GeneralErrors.Count == 0, Errors(r));
        if (r.Errors.Count > 0) return;
        var holder = new Node3D();
        var dual = New<Node3D>(r, "GapDual");
        holder.AddChild(dual);
        SceneTree.Instance.Root.AddChild(holder);
        T.Check("the base's @onready ran before the subclass's _ready read it", dual.Get("seen").AsGodotObject() == holder);
        T.Check("and the subclass's own", dual.Get("own").AsGodotObject() == holder);
        T.Check("the base's _ready did not run (no super())", dual.Get("base_ready").AsInt64() == 0);
        holder.Free();
    }

    // COGITO: cogito_player_state.gd saves itself with ResourceSaver.save(self, "user://...tres", ResourceSaver.FLAG_CHANGE_PATH) and loads it back.
    private static void ResourceSaverRoundTrip()
    {
        ConversionResult r = Many(("GapSaver", "extends Resource\nclass_name GapSaver\nfunc store(p: String) -> int:\n\treturn ResourceSaver.save(self, p, ResourceSaver.FLAG_CHANGE_PATH)\n"));
        string cs = r.Scripts.First(x => x.ClassName == "GapSaver").Text;
        T.Check("ResourceSaver.FLAG_CHANGE_PATH is SaverFlags.ChangePath, and the call compiles", r.Errors.Count == 0 && cs.Contains("ResourceSaver.SaverFlags.ChangePath"), Errors(r) + cs);
        string dir = Path.Combine(Path.GetTempPath(), "aver-fabric-saver-test-" + System.Environment.ProcessId);
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "slot_a.tres");
            var state = new GapSaveState { Label = "lobby", Score = 7, Items = new() { new GapSaveItem { Count = 2 }, new GapSaveItem { Count = 5 } } };
            T.Check("save writes it", ResourceSaver.Save(state, path, ResourceSaver.SaverFlags.ChangePath) == Error.Ok && File.Exists(path));
            T.Check("FLAG_CHANGE_PATH gives the resource its path", state.ResourcePath == path);
            T.Check("exists() finds the saved file", ResourceLoader.Exists(path));
            var back = ResourceLoader.Load(path, (System.Type?)null, ResourceLoader.CacheMode.Ignore) as GapSaveState;
            T.Check("load() builds the class with its exported values", back is { Label: "lobby", Score: 7 }, back?.Label ?? "null");
            T.Check("and the resources it held, written inside it", back != null && back.Items.Count == 2 && back.Items[1].Count == 5);
        }
        finally { Directory.Delete(dir, true); }
    }

    private static void ByNameAccessors()
    {
        var mesh = new MeshInstance3D();
        mesh.Call("set_visible", false);
        T.Check("call(\"set_visible\", false) sets the property", !mesh.Visible);
        T.Check("call(\"is_visible\") reads it", !mesh.Call("is_visible").AsBool());
        mesh.Free();
        Aver.Fabric.GodotAccessors.PathOverride = GodotApiFile();
        var mat = new StandardMaterial3D();
        mat.Call("set_albedo", Colors.Red);
        T.Check("set_albedo, Godot's setter of albedo_color, sets it", mat.AlbedoColor == Colors.Red, mat.AlbedoColor.ToString());
        var fog = new FogMaterial();
        fog.Call("set_albedo", Colors.Blue);
        T.Check("and a FogMaterial's own albedo", fog.Albedo == Colors.Blue);
        var body = new GapBodyHolder();
        body.Body = new Node3D();
        T.Check("a script's `body` is what get(\"body\") reads, not the engine body Aver.Fabric keeps", body.Get("body").AsGodotObject() == body.Body);
        body.Body.Free();
        body.Free();
        var node = new Node();
        node.UpdateConfigurationWarnings();
        node.NotifyPropertyListChanged();
        T.Check("update_configuration_warnings and notify_property_list_changed are there and do nothing", true);
        // COGITO's player: find_children("", "CogitoAttribute", false) lists its attributes; an empty pattern is any name.
        var attr = new Node3D { Name = "HealthAttribute" };
        var other = new Node { Name = "Timer" };
        node.AddChild(attr);
        node.AddChild(other);
        attr.Owner = node;
        other.Owner = node;
        Aver.Fabric.Collections.Array<Node> found = node.FindChildren("", "Node3D", false);
        T.Check("find_children with an empty pattern matches every name, filtered by type", found.Count == 1 && found[0] == attr);
        node.Free();
    }

    // Godot's own API (third_party/godot-gdextension/extension_api.json), found from the test's folder up.
    private static string? GodotApiFile()
    {
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            string f = Path.Combine(d.FullName, "third_party", "godot-gdextension", "extension_api.json");
            if (File.Exists(f)) return f;
        }
        return null;
    }

    // COGITO, round three.
    private static void GdScriptShapesRoundThree()
    {
        string? api = GodotApiFile();
        T.Check("Godot's extension_api.json is found for the Godot 3 name check", api != null);
        Aver.Fabric.Convert.GdScript.GodotApi.PathOverride = api;
        try
        {
            ConversionResult r = Many(
                ("GapThreeHelper", "extends RefCounted\n\nfunc v() -> int:\n\treturn 3\n"),
                ("GapThree", "class_name GapThree\nextends Node3D\n\nsignal poked\n\n@export_category(\"A\")\n@export_category(\"B\")\n@export var tagged := 1\n\n" +
                    "var hits := 0\nvar kinds := [Node, GapThree]\n\n" +
                    "func debug_node(n: Node) -> int:\n\treturn 1\n\nfunc _debug_node(n: Node) -> int:\n\treturn 2\n\nfunc ping() -> void:\n\thits += 1\n\n" +
                    "func names() -> PackedStringArray:\n\tvar list = [\"a\", \"b\"]\n\treturn list\n\n" +
                    "func listen(sig: Signal, cb: Callable) -> int:\n\tif not sig.is_connected(cb):\n\t\tsig.connect(cb)\n\tsig.emit()\n\treturn sig.get_connections().size()\n\n" +
                    "static func quiet() -> bool:\n\treturn EngineDebugger.is_active()\n\n" +
                    "func godot3(mat: StandardMaterial3D, label: RichTextLabel) -> void:\n\tmat.flags_unshaded = true\n\tlabel.bbcode_text = \"x\"\n\n" +
                    "func exclude_self() -> void:\n\tvar q := PhysicsRayQueryParameters3D.new()\n\tq.exclude = [self]\n\n" +
                    "func takes(m: Material) -> void:\n\tpass\n\nfunc pass_bool(a: Material, b: Material) -> void:\n\tvar m = a or b\n\ttakes(m)\n\n" +
                    "func run() -> int:\n\tvar n := 0\n" +
                    "\tif Input.MouseMode.MOUSE_MODE_VISIBLE == Input.MOUSE_MODE_VISIBLE: n += 1\n" +
                    "\tif get_parent() != GapThree and kinds.size() == 2: n += 2\n" +
                    "\tvar holder = {\"obj\": self}\n\tvar cb := Callable(holder[\"obj\"], \"ping\")\n\tcb.call()\n\tif hits == 1: n += 4\n" +
                    "\tif names().size() == 2: n += 8\n" +
                    "\tvar typed: Array[int] = [1, 2, 3, 4]\n\tvar part: Array[int] = typed.slice(1, 3)\n\tvar more = [5, 6]\n\tpart.append_array(more)\n\tif part.size() == 4 and part[0] == 2: n += 16\n" +
                    "\tif call(\"_debug_node\", self) == 2 and debug_node(self) == 1: n += 32\n" +
                    "\tif listen(poked, ping) == 1 and hits == 2: n += 64\n" +
                    "\tif not quiet(): n += 128\n" +
                    "\tvar ch := BaseMaterial3D.TEXTURE_CHANNEL_GREEN\n\tif ch == 1: n += 256\n" +
                    "\tvar nm = \"x\" if hits > 100 else &\"y\"\n\tif str(nm) == \"y\": n += 512\n" +
                    "\tvar body := StaticBody3D.new()\n\tvar layer := 4\n\tbody.set_collision_layer(layer)\n\tif body.collision_layer == 4: n += 1024\n\tbody.free()\n" +
                    "\tif load(\"res://gapthreehelper.gd\").new().v() == 3: n += 2048\n" +
                    "\tvar vv = [Vector2i(3, 4)]\n\tvar v := Vector2i(vv[0])\n\tif v.x == 3: n += 4096\n" +
                    "\tvar pm: ProcessMode = process_mode\n\tif pm == process_mode: n += 8192\n" +
                    "\treturn n\n"));
            T.Check("it compiles (before: a class as a value, Signal.Call, flags_unshaded, Input.MouseMode, ProcessMode, Array to string[], typed slice, " +
                    "Callable(untyped, m), _debug_node beside debug_node, EngineDebugger, two export categories, [self] as a RID, a bool as a Material)", r.Success, Errors(r));
            if (!r.Success) return;
            T.Check("the Godot 3 names are made by name (Godot lacks them too), not compile errors", Text(r, 1).Contains("mat.Set(\"flags_unshaded\", true)") && Text(r, 1).Contains("label.Set(\"bbcode_text\", \"x\")"), Text(r, 1));
            var three = New<Node3D>(r, "GapThree");
            SceneTree.Instance.Root.AddChild(three);
            long got = (long)three.GetType().GetMethod("Run")!.Invoke(three, null)!;
            T.Check("and runs as GDScript does: every check holds (16383)", got == 16383, got.ToString());
            three.Free();
        }
        finally { Aver.Fabric.Convert.GdScript.GodotApi.PathOverride = null; }
    }

    private static void ByNameRoundFour()
    {
        var player = new Node3D { Name = "Player" };
        var body = new Node3D { Name = "Body", Position = new Vector3(1, 2, 3) };
        player.AddChild(body);
        Variant found = GDScript.CallMethod(Variant.From(player), "get_node", "Body");
        T.Check("get_node called by name is GetNode(path), not the generic GetNode<T>",
                ReferenceEquals(found.AsGodotObject(), body) && GDScript.GetMember(found, "transform").AsTransform3D().Origin == new Vector3(1, 2, 3));
        player.Free();
        Resource? shader = ResourceLoader.Load("res://gap/tint.gdshader");
        T.Check("a .gdshader loads as a Shader that knows its path", shader is Shader && shader.ResourcePath == "res://gap/tint.gdshader", shader?.GetType().Name ?? "null");
        var light = new OmniLight3D();
        var ray = new RayCast3D();
        var cast = new ShapeCast3D();
        var view = new SubViewport();
        T.Check("shadow_opacity, debug_shape_custom_color and handle_input_locally are kept",
                MemberAccess.TrySet(light, "shadow_opacity", 0.5f) && light.ShadowOpacity == 0.5f &&
                MemberAccess.TrySet(ray, "debug_shape_custom_color", new Color(1, 0, 0)) && ray.DebugShapeCustomColor.R == 1f &&
                MemberAccess.TrySet(cast, "debug_shape_custom_color", new Color(0, 1, 0)) && cast.DebugShapeCustomColor.G == 1f &&
                MemberAccess.TrySet(view, "handle_input_locally", false) && !view.HandleInputLocally);
        light.Free();
        ray.Free();
        cast.Free();
        view.Free();
    }

    private static void RuntimeCallsRoundThree()
    {
        T.Check("key names as Godot writes them", OS.GetKeycodeString(Key.A | (Key)KeyModifierMask.Shift | (Key)KeyModifierMask.Ctrl) == "Shift+Ctrl+A" &&
                OS.GetKeycodeString(Key.Kp5) == "Kp 5" && OS.GetKeycodeString(Key.Space) == "Space", OS.GetKeycodeString(Key.A | (Key)KeyModifierMask.Shift | (Key)KeyModifierMask.Ctrl));
        T.Check("and back, letter case ignored", OS.FindKeycodeFromString("ctrl+space") == (Key.Space | (Key)KeyModifierMask.Ctrl) && OS.FindKeycodeFromString("Escape") == Key.Escape);
        var panel = new Control { AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled, EditorDescription = "note" };
        var title = new Label();
        panel.AddChild(title);
        T.Check("auto_translate_mode: Inherit takes the parent's, and the old auto_translate reads it", !title.CanAutoTranslate() && !panel.AutoTranslate && new Label().CanAutoTranslate());
        title.AutoTranslate = true;
        T.Check("setting auto_translate picks Always", title.AutoTranslateMode == Node.AutoTranslateModeEnum.Always && title.CanAutoTranslate());
        T.Check("editor_description is kept", MemberAccess.TrySet(panel, "editor_description", "x") && panel.EditorDescription == "x");
        panel.Free();
        ProjectSettings.SetSetting("gap/test/value", 7);
        T.Check("a setting a script sets is read back", ProjectSettings.HasSetting("gap/test/value") && ProjectSettings.GetSetting("gap/test/value").AsInt64() == 7);
        ProjectSettings.SetSetting("gap/test/value", default);
        T.Check("and a nil value clears it", !ProjectSettings.HasSetting("gap/test/value"));
        var holder = new Node { Name = "Single" };
        Engine.RegisterSingleton("GapSingle", holder);
        T.Check("a script's singleton is registered by name", Engine.HasSingleton("GapSingle") && ReferenceEquals(Engine.GetSingleton("GapSingle"), holder) && Engine.GetSingleton("Nope") == null);
        Engine.UnregisterSingleton("GapSingle");
        holder.Free();
        var rng = new RandomNumberGenerator();
        T.Check("rand_weighted picks the only weighted index, and -1 with no weight", rng.RandWeighted(new[] { 0f, 2f, 0f }) == 1 && rng.RandWeighted(new[] { 0f, 0f }) == -1);
        Image img = Image.Create(2, 1, false, Image.Format.Rgba8);
        img.SetPixel(0, 0, new Color(1, 0, 0));
        img.Rotate90(ClockDirection.Clockwise);
        T.Check("rotate_90 swaps the size and turns the pixels", img.GetWidth() == 1 && img.GetHeight() == 2 && img.GetPixel(0, 0).R > 0.9f);
        string zipPath = Path.Combine(Path.GetTempPath(), "fabric_gap_" + Guid.NewGuid().ToString("N") + ".zip");
        using (var z = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
        using (var w = new StreamWriter(z.CreateEntry("dir/a.txt").Open())) w.Write("hi");
        var reader = new ZipReader();
        bool zipOk = reader.Open(zipPath) == Error.Ok && reader.GetFiles().Contains("dir/a.txt") && System.Text.Encoding.UTF8.GetString(reader.ReadFile("dir/a.txt")) == "hi"
            && reader.ReadFile("DIR/A.TXT", caseSensitive: false).Length == 2;
        reader.Close();
        File.Delete(zipPath);
        T.Check("ZIPReader lists and reads an archive's files", zipOk);
        var sprite = new Sprite2D { Hframes = 4, Vframes = 2 };
        sprite.Frame = 20;
        T.Check("Sprite2D keeps its frame inside its grid", sprite.Frame == 7 && sprite.FrameCoords == new Vector2I(3, 1));
        sprite.Free();
        DisplayServer.ClipboardSet("copied");
        T.Check("the clipboard round-trips", DisplayServer.ClipboardGet() == "copied" && DisplayServer.ClipboardHas());
        T.Check("Array and packed arrays convert element by element",
            PackedArrays.FromArray<string>(new Aver.Fabric.Collections.Array { "a", "b" }).SequenceEqual(new[] { "a", "b" }) && PackedArrays.ToArray(new[] { 1L, 2L }).Count == 2);
        // A game's saved bindings name Godot's .macos variants (COGITO's defaults were taken from InputMap.get_actions()).
        InputMap.ActionAddEvent("ui_text_caret_line_end.macos", new InputEventKey { Keycode = Key.F9 });
        T.Check("Godot's .macos built-in actions exist on every platform, as in Godot",
            InputMap.HasAction("ui_text_caret_line_end.macos") && InputMap.GetActions().Contains("ui_text_select_word_under_caret.macos") &&
            InputMap.ActionGetEvents("ui_text_caret_line_end.macos").Count == 4);
        InputMap.LoadFromProjectSettings();
        ClassValue cls = GDScript.ClassObject(typeof(Node3D));
        Variant made = cls.New();
        T.Check("a class used as a value is one object, and new() builds an instance", ReferenceEquals(cls, GDScript.ClassObject(typeof(Node3D))) && made.AsGodotObject() is Node3D);
        (made.AsGodotObject() as Node)?.Free();
    }
}

// The weapons manager of NodeArrayExport: what its list holds when its _Ready runs.
public class GapWeaponsManager : Node3D
{
    public Aver.Fabric.Collections.Array<Node3D> WeaponsArray { get; set; } = new();
    public int SeenAtReady = -1;
    public override void _Ready() => SeenAtReady = WeaponsArray.Count;
}

// The script classes of ResourceSaverRoundTrip: a save state holding a typed list of resources.
public class GapSaveState : Resource
{
    [Export] public string Label = "";
    [Export] public long Score;
    [Export] public Aver.Fabric.Collections.Array<GapSaveItem> Items = new();
    public long NotSaved = 3;
}

public class GapSaveItem : Resource
{
    [Export] public long Count;
}

// A player script's `var body` on a CharacterBody3D (whose Aver.Fabric class keeps an internal Body of its own).
public class GapBodyHolder : CharacterBody3D
{
    public Node3D? Body;
}

// The script classes of CustomResourcesInScene: a custom Resource holding another, and the node whose typed list holds them.
public class GapWeaponData : Resource
{
    public string WeaponName { get; set; } = "";
    public int Magazine { get; set; }
}

public class GapWeaponSlot : Resource
{
    public GapWeaponData? Weapon { get; set; }
    public int CurrentAmmo { get; set; }
}

public class GapWeaponHolder : Node
{
    public Aver.Fabric.Collections.Array<GapWeaponSlot> WeaponStack { get; set; } = new();
}
