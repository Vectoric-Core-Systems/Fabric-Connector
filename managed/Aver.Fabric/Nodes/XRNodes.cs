// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// XR nodes and XRServer as inert carriers (approximated). Aver has no XR path (OpenXR is moved to a later project): a scene with an XROrigin3D, an XRCamera3D or
// controllers still loads and its scripts still compile and run, and XRServer finds no interface, so a script that tests for one takes its non-XR branch.

namespace Aver.Fabric;

/// <summary>A node a tracker drives (Godot's XRNode3D). Nothing tracks it here: it stays where the scene put it.</summary>
public class XRNode3D : Node3D
{
    /// <summary>The tracker's name ("left_hand", "right_hand", "head").</summary>
    public StringName Tracker { get; set; } = "";

    /// <summary>The pose of the tracker this node follows ("default", "aim", "grip").</summary>
    public StringName Pose { get; set; } = "default";

    public bool ShowWhenTracked { get; set; }

    /// <summary>Always false: no XR interface is running.</summary>
    public bool GetIsActive() => false;

    /// <summary>Always false: there is no tracking data.</summary>
    public bool GetHasTrackingData() => false;
}

/// <summary>A tracked controller (Godot's XRController3D). It reads no button, trigger or stick.</summary>
public class XRController3D : XRNode3D
{
    public bool IsButtonPressed(StringName name) => false;

    public float GetFloat(StringName name) => 0f;

    public Vector2 GetVector2(StringName name) => Vector2.Zero;

    /// <summary>Always <see cref="XRPositionalTracker.TrackerHandEnum.Unknown"/>.</summary>
    public XRPositionalTracker.TrackerHandEnum GetTrackerHand() => XRPositionalTracker.TrackerHandEnum.Unknown;
}

/// <summary>A tracked anchor in the real world (Godot's XRAnchor3D).</summary>
public class XRAnchor3D : XRNode3D
{
    public Vector3 GetSize() => Vector3.Zero;
}

/// <summary>The camera of an XR session (Godot's XRCamera3D): an ordinary camera here.</summary>
public class XRCamera3D : Camera3D
{
}

/// <summary>The centre of the play space (Godot's XROrigin3D): an ordinary node here.</summary>
public class XROrigin3D : Node3D
{
    /// <summary>Kept as set: no XR interface scales the world.</summary>
    public float WorldScale { get; set; } = 1f;

    /// <summary>Whether this is the origin in use. Kept as set.</summary>
    public bool Current { get; set; } = true;
}

/// <summary>A tracked thing (Godot's XRPositionalTracker). None exists here.</summary>
public class XRPositionalTracker : RefCounted
{
    public enum TrackerHandEnum { Unknown = 0, Left = 1, Right = 2, Max = 3 }

    public StringName Name { get; set; } = "";
}

/// <summary>An XR interface (Godot's XRInterface). <see cref="XRServer.FindInterface"/> never returns one, so a script holds null.</summary>
public class XRInterface : RefCounted
{
    public virtual string GetName() => "";

    public virtual bool IsInitialized() => false;

    public virtual bool Initialize() => false;

    public virtual void Uninitialize() { }
}

/// <summary>Godot's XRServer singleton, with no interface to find.</summary>
public static class XRServer
{
    /// <summary>Kept as set; nothing scales the world.</summary>
    public static float WorldScale { get; set; } = 1f;

    public static Transform3D WorldOrigin { get; set; } = Transform3D.Identity;

    public static Transform3D ReferenceFrame => Transform3D.Identity;

    /// <summary>Always null: Aver has no XR interface. A script that checks for one takes its non-XR branch.</summary>
    public static XRInterface? FindInterface(string name) => null;

    public static XRInterface? PrimaryInterface { get; set; }

    public static int GetInterfaceCount() => 0;

    public static XRInterface? GetInterface(int index) => null;

    public static Collections.Array<Collections.Dictionary> GetInterfaces() => new();

    public static XRPositionalTracker? GetTracker(StringName name) => null;
}
