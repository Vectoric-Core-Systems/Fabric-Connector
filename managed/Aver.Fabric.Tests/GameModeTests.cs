// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// FabricGameMode (GAME.MODE of a converted project): it registers, declares no pawn, and the view follows Godot's current Camera3D (the one made current,
// else the first in the tree). Headless: the engine's own view seam (aver_fw_set_view_entity) is not reached; FabricView.Followed is what it would be given.

using System.Reflection;
using Aver.Fabric;
using Aver.Framework;

static class GameModeTests
{
    public static int RunAll() => T.Suite(() =>
    {
        T.Run("FabricGameMode: registers under the name the converter writes, with a controller and no default pawn", Registration);
        T.Run("FabricGameMode: the extension declares the mode, its controller and the view pawn", Declared);
        T.Run("FabricGameMode: the view follows the first camera, a make_current switch and the next camera", FollowsCamera);
        T.Run("Camera pose: a top-level follow camera is a root entity at its global pose, looking along -Z (the engine's +X)", TopLevelFollowCamera);
        T.Run("FabricGameMode: a camera that asks to be current, one that leaves and comes back, and the session end", CurrentFlags);
    });

    private static void Registration()
    {
        Type mode = typeof(FabricGameMode);
        AverGameModeAttribute? a = mode.GetCustomAttribute<AverGameModeAttribute>(inherit: false);
        T.Check("it is a game mode", typeof(AverGameMode).IsAssignableFrom(mode) && !mode.IsAbstract && a != null);
        T.Check("registered as FabricGameMode, the project key the converter writes", a?.Name == "FabricGameMode" && FabricGameMode.ClassName == "FabricGameMode" && ClassNames.Of(mode) == "FabricGameMode");
        T.Check("it names no default pawn: the engine spawns none and starts no drone", a != null && a.DefaultPawnClass.Length == 0);
        T.Check("its controller is declared, a concrete one (the engine's PlayerController is abstract)",
                a?.PlayerControllerClass == "FabricPlayerController" && ClassNames.Of(typeof(FabricPlayerController)) == "FabricPlayerController" &&
                typeof(AverPlayerController).IsAssignableFrom(typeof(FabricPlayerController)) && !typeof(FabricPlayerController).IsAbstract);
        T.Check("it has a public parameterless constructor, as the bridge needs", mode.GetConstructor(Type.EmptyTypes) != null);
        T.Check("the pawn the view is published through is a pawn named as the mode spawns it", typeof(AverPawn).IsAssignableFrom(typeof(FabricViewPawn)) && ClassNames.Of(typeof(FabricViewPawn)) == FabricGameMode.ViewPawnClass);
    }

    private static void Declared()
    {
        T.Check("FabricExtension is the manifest's entry", typeof(Aver.Framework.Hosting.IAverExtension).IsAssignableFrom(typeof(FabricExtension)) && typeof(FabricExtension).GetConstructor(Type.EmptyTypes) != null);
        T.Check("it declares the mode, the controller and the view pawn", FabricExtension.IsGameClass(typeof(FabricGameMode)) && FabricExtension.IsGameClass(typeof(FabricPlayerController)) && FabricExtension.IsGameClass(typeof(FabricViewPawn)));
        T.Check("and nothing else", !FabricExtension.IsGameClass(typeof(Node3D)) && !FabricExtension.IsGameClass(typeof(Camera3D)));
        // The bridge resolves a mode's controller by name when it seals the mode, in the order of the assembly's types: the controller has to come first.
        T.Check("the controller comes before the mode in the assembly", typeof(FabricPlayerController).MetadataToken < typeof(FabricGameMode).MetadataToken);
        string manifest = File.ReadAllText(Path.Combine(T.DataDir(), "..", "..", "..", "extension.json"));
        T.Check("the manifest names FabricExtension as the managed entry", manifest.Contains("\"entry\": \"Aver.Fabric.FabricExtension\""));
    }

    private static Camera3D Add(Node parent, string name, bool current = false)
    {
        var cam = new Camera3D { Name = name };
        if (current) cam.Current = true;
        parent.AddChild(cam);
        return cam;
    }

    private static void FollowsCamera()
    {
        T.Reset();
        Node3D level = new() { Name = "Level" };
        SceneTree.Instance.Root.AddChild(level);
        T.Check("no camera: the view follows nothing", FabricView.Followed == null);
        Camera3D a = Add(level, "A"), b = Add(level, "B"), c = Add(level, "C");
        T.Check("none made current: the first camera in the tree is", FabricView.Followed == a && a.IsCurrent() && !b.IsCurrent() && !c.IsCurrent() && SceneTree.Instance.Root.GetCamera3D() == a);
        b.MakeCurrent();
        T.Check("make_current switches the view and clears the old camera", FabricView.Followed == b && b.IsCurrent() && !a.IsCurrent());
        c.Current = true;
        T.Check("setting current does the same", FabricView.Followed == c && !b.IsCurrent());
        c.ClearCurrent();
        T.Check("clear_current hands the view to the first other camera in the tree", FabricView.Followed == a && a.IsCurrent() && !c.IsCurrent());
        a.ClearCurrent(enableNext: false);
        T.Check("without enable_next it follows nothing", FabricView.Followed == null && SceneTree.Instance.Root.GetCamera3D() == null);
        level.RemoveChild(a);
        T.Check("a camera outside the tree is not a candidate", FabricView.Followed == null);
        b.MakeCurrent();
        level.RemoveChild(b);
        T.Check("the current camera leaving the tree passes the view on", FabricView.Followed == c && c.IsCurrent());
        level.RemoveChild(c);
        T.Check("and with no camera left, to nothing", FabricView.Followed == null);
    }

    private static void CurrentFlags()
    {
        T.Reset();
        Node3D level = new() { Name = "Level" };
        SceneTree.Instance.Root.AddChild(level);
        Camera3D first = Add(level, "First");
        Camera3D wanted = Add(level, "Wanted", current: true);
        T.Check("a camera with current set takes the view from the first one", FabricView.Followed == wanted && !first.IsCurrent());

        level.RemoveChild(wanted);
        T.Check("it leaves: the first camera follows", FabricView.Followed == first);
        level.AddChild(wanted);
        T.Check("it comes back: Godot keeps current on a camera that left, so it takes the view again", FabricView.Followed == wanted && wanted.IsCurrent() && !first.IsCurrent());

        var mode = new FabricGameMode();
        mode.OnBeginPlay(BeginReason.Play);
        T.Check("beginning play leaves the view where it is", FabricView.Followed == wanted);
        mode.OnEndPlay(EndReason.Stop);
        T.Check("the session ending stops following (the cameras go with the level)", FabricView.Followed == null);

        T.Reset();
        var sub = new SubViewport { Name = "Sub" };
        SceneTree.Instance.Root.AddChild(sub);
        var inside = new Camera3D { Name = "Inside" };
        sub.AddChild(inside);
        T.Check("a camera inside a SubViewport never takes the window's view", FabricView.Followed == null && SceneTree.Instance.Root.GetCamera3D() == null);
    }

    // Aver's +X axis rotated by an entity rotation quaternion (the engine's forward): the numbers the engine reads for the view.
    private static Vector3 EngineForwardOf(float[] q) // x, y, z, w
    {
        (float x, float y, float z, float w) = (q[0], q[1], q[2], q[3]);
        (float vx, float vy, float vz) = (1f, 0f, 0f);
        (float tx, float ty, float tz) = (2 * (y * vz - z * vy), 2 * (z * vx - x * vz), 2 * (x * vy - y * vx));
        (float rx, float ry, float rz) = (vx + w * tx + (y * tz - z * ty), vy + w * ty + (z * tx - x * tz), vz + w * tz + (x * ty - y * tx));
        return new Vector3(rx, ry, rz);
    }

    private static void TopLevelFollowCamera()
    {
        T.Reset();
        // Godot's kinematic_character: Cubio > Target > Camera3D, the camera script sets top_level in _ready and calls look_at_from_position each physics frame.
        Node3D cubio = new() { Name = "Cubio", Position = new Vector3(-1.5f, 2f, 4f) };
        Node3D target = new() { Name = "Target", Position = new Vector3(0, 0.8f, 0) };
        Camera3D cam = new() { Name = "Camera3D", Position = new Vector3(2f, 0.5f, 0.913381f) };
        cubio.AddChild(target);
        target.AddChild(cam);
        SceneTree.Instance.Root.AddChild(cubio);
        Vector3 start = cam.GlobalPosition;
        T.CheckVec("the camera starts at Target + its local offset", new Vector3(0.5f, 3.3f, 4.913381f), start, 1e-4f);

        cam.TopLevel = true;
        T.CheckVec("top_level keeps the global position", start, cam.GlobalPosition);
        T.Check("a top-level camera's entity keeps its parent and ignores its transform (TransformAbsolute), as Godot keeps it in the tree", NodeHost.EngineParent(cam) == target);
        T.Check("and an ordinary child under its parent", NodeHost.EngineParent(target) == cubio);

        cubio.Position = new Vector3(-8f, 2f, 1f);   // the player walks off
        Vector3 targetPos = target.GlobalPosition;
        Vector3 camPos = new Vector3(-6f, 3.5f, 4f);
        cam.LookAtFromPosition(camPos, targetPos, Vector3.Up);

        var pos = new float[3]; var quat = new float[4]; var scale = new float[3];
        cam.GetLocalForEngine(pos, quat, scale);
        var expectPos = FabricSpace.ToAverPosition(camPos);
        T.Check("the entity's local position IS the camera's global one, whatever the parent does", T.Near(pos[0], expectPos.X, 1e-2f) && T.Near(pos[1], expectPos.Y, 1e-2f) && T.Near(pos[2], expectPos.Z, 1e-2f));
        Vector3 godotDir = (targetPos - camPos).Normalized();
        var want = FabricSpace.ToAverDirection(godotDir);
        Vector3 fwd = EngineForwardOf(quat);
        T.Check("look_at: the entity's +X points at the target (Godot's -Z through the axis map)", T.Near(fwd, new Vector3(want.X, want.Y, want.Z), 1e-4f));
        T.CheckVec("and the camera's -Z in Godot space is the same direction", godotDir, -cam.GlobalTransform.Basis.Z, 1e-4f);

        // A camera that is not top-level is placed through its parent as before.
        cam.TopLevel = false;
        T.Check("top_level off: the entity goes back under its parent", NodeHost.EngineParent(cam) == target);
        T.CheckVec("and the camera keeps its global position", camPos, cam.GlobalPosition, 1e-3f);
        T.CheckVec("so its local position is relative to the Target", camPos - target.GlobalPosition, cam.Position, 1e-3f);
        T.Reset();
    }
}
