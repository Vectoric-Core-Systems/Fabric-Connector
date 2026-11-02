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
        T.Run("converter gap: a 3D node under a non-3D parent (a SubViewport's camera below a CanvasLayer of the moving player) is placed in the world directly", ThreeDUnderNonThreeD);
        T.Run("converter gap: an exported Array[Node3D] stored as NodePaths holds the nodes by _ready (bukkbeek's weapons_array)", NodeArrayExport);
        T.Run("converter gap: Godot 4.5 @abstract classes and functions, and BaseMaterial3D's flags and features by number (COGITO)", AbstractAndMaterialFlags);
        T.Run("converter gap: an addon's editor-only scripts (EditorPlugin, EditorInterface, and what loads them) are left out of the game (COGITO)", EditorOnlyScripts);
        T.Run("converter gap: preload(\"./x.gd\") from the script's own folder, and `var _name` with a setter (COGITO)", RelativePreloadAndUnderscoreSetter);
    });

    private static string Refs => AppContext.BaseDirectory;

    private static ConversionResult Many(params (string Cls, string Gd)[] scripts) =>
        ScriptConverter.Convert(
            scripts.Select(s => new ScriptSource { ResPath = "res://" + s.Cls.ToLowerInvariant() + ".gd", Text = s.Gd, Kind = "gdscript", ClassName = s.Cls, Output = "Content/Scripts/" + s.Cls + ".cs" }).ToList(),
            new ConvertSettings { RefsDir = Refs, KeepAssembly = true, ExtraSources = System.Array.Empty<(string, string)>() });

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
        T.Check("a top-level node hangs under none, as before", NodeHost.EngineParent(top) == null);
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
}

// The weapons manager of NodeArrayExport: what its list holds when its _Ready runs.
public class GapWeaponsManager : Node3D
{
    public Aver.Fabric.Collections.Array<Node3D> WeaponsArray { get; set; } = new();
    public int SeenAtReady = -1;
    public override void _Ready() => SeenAtReady = WeaponsArray.Count;
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
