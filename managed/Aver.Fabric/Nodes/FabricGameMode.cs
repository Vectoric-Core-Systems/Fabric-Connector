// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// The GameMode of a converted Godot project (GAME.MODE FabricGameMode): no default pawn and no drone, the view follows Godot's current Camera3D.
// Godot's own input map and the project's scripts drive the game; the engine's pawn, controller and drone play no part. See G3_RUNTIME.md "The game mode".

using System.Runtime.InteropServices;
using Aver.Framework;
using Aver.Scene;

namespace Aver.Fabric;

/// <summary>The view the player sees: the current <see cref="Camera3D"/>. The engine follows one entity, so this publishes the current camera's entity
/// (none when no camera is current) and keeps the view mode at first person with no offset: the camera's own pose, not a boom behind a pawn.</summary>
internal static class FabricView
{
    /// <summary>The camera the view follows, or null. Set whenever a camera becomes or stops being the current one, with or without an engine.</summary>
    internal static Camera3D? Followed { get; private set; }

    /// <summary>The view mode before <see cref="UseCameraPose"/>, for <see cref="Restore"/>.</summary>
    private static (int Mode, float Eye, float Boom)? s_saved;

    internal static void Follow(Camera3D? camera)
    {
        Followed = camera;
        if (!NodeHost.Current.IsEngine) return;
        Aver.Framework.Hosting.ExtensionHost.SetViewEntity(camera != null && camera.Self.IsValid ? camera.Self : Entity.None);
    }

    /// <summary>First person with no eye height and no boom: the engine places the camera exactly at the view entity and looks where it looks.</summary>
    internal static void UseCameraPose()
    {
        if (!NodeHost.Current.IsEngine) return;
        if (s_saved == null)
        {
            ViewNative.aver_fw_view(out int mode, out float eye, out float boom);
            s_saved = (mode, eye, boom);
        }
        ViewNative.aver_fw_set_view(ViewNative.FirstPerson, 0f, 0f);
    }

    /// <summary>Puts the view mode back and stops following a camera, as the session ends (the view mode is process-wide and outlives the game mode).</summary>
    internal static void Restore()
    {
        Followed = null;
        if (!NodeHost.Current.IsEngine) return;
        Aver.Framework.Hosting.ExtensionHost.SetViewEntity(Entity.None);
        if (s_saved is { } v) ViewNative.aver_fw_set_view(v.Mode, v.Eye, v.Boom);
        s_saved = null;
    }
}

// The framework's view seam (framework_abi.h: aver_fw_set_view / aver_fw_view); only the Aver.Framework library is needed.
internal static class ViewNative
{
    private const string Lib = "Aver.Framework";

    /// <summary>AVER_FW_VIEW_FIRST_PERSON.</summary>
    internal const int FirstPerson = 0;

    [DllImport(Lib)] internal static extern void aver_fw_set_view(int mode, float eyeHeight, float boomLength);
    [DllImport(Lib)] internal static extern void aver_fw_view(out int mode, out float eyeHeight, out float boomLength);
}

/// <summary>The controller of a converted project's player. The engine's own PlayerController is abstract, so a game mode needs one that is not. It
/// reads no input and moves nothing: Godot's scripts do.</summary>
public sealed class FabricPlayerController : AverPlayerController
{
}

/// <summary>What the engine's camera follows while a controller possesses it. It has no mesh, no body and no input: the view comes from the current
/// <see cref="Camera3D"/>, and the engine places the camera from the possessed pawn's view entity only (the pawn's own pose is the fallback when no
/// camera is current).</summary>
public sealed class FabricViewPawn : AverPawn
{
}

/// <summary>The rules of a converted Godot project while it plays. Declares no default pawn (the engine spawns none and starts no drone), and makes the
/// view follow Godot's current <see cref="Camera3D"/>: the one made current, else the first in the tree (<see cref="Camera3D.MakeCurrent"/>).</summary>
/// <remarks>The controller class is declared before this class in the file on purpose: the bridge resolves a game mode's controller by name when it
/// declares the mode, in the order of the assembly's types (FabricGameModeTests checks that order).</remarks>
[AverGameMode("FabricGameMode", DefaultPawnClass = "", PlayerControllerClass = "FabricPlayerController")]
public sealed class FabricGameMode : AverGameMode
{
    /// <summary>The registry name the converter writes as the project's GAME.MODE.</summary>
    public const string ClassName = "FabricGameMode";

    /// <summary>The class name of the pawn the engine follows the view through (not a default pawn: nothing in the game uses it).</summary>
    internal const string ViewPawnClass = "FabricViewPawn";

    public override void OnBeginPlay(BeginReason reason)
    {
        if (reason != BeginReason.Reload) FabricView.UseCameraPose();
    }

    public override void OnPostLogin(Entity controller)
    {
        // A level that names its own default pawn keeps it; otherwise the controller possesses the carrier the view is published through.
        if (Actors.Get<AverPlayerController>(controller) is not { } pc || pc.HasPawn) return;
        if (Spawn<FabricViewPawn>(new Vec3(0, 0, 0)) is { } pawn) pc.Possess(pawn);
    }

    public override void OnEndPlay(EndReason reason) => FabricView.Restore();
}
