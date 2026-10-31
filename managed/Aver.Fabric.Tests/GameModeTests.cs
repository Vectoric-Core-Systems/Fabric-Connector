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
}
