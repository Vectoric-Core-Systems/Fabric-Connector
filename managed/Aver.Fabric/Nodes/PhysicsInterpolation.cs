// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Godot's physics interpolation (Node.physics_interpolation_mode, SceneTree.physics_interpolation, reset_physics_interpolation, get_global_transform_interpolated)
// on the engine's pose interpolation (Aver.Framework.PoseInterpolation): the engine reads an entity's world pose around each fixed step, puts it back on its real pose
// before gameplay runs and draws it between its last two steps.
//
// WHO IS REGISTERED. The engine already blends dynamic rigid bodies when the switch is on. A node is put on the engine's list (mode ON) when it is interpolated in
// Godot's terms and is a kind that moves in the physics step: a CharacterBody3D or AnimatableBody3D, or any node whose own mode is On. Every other node follows
// the node above it, because a child entity moves with its parent. A node under an interpolated parent is therefore never listed itself, and a child set to Off still
// follows an interpolated parent's drawn pose. Move a listed node in _PhysicsProcess; a move made in _Process is overwritten by the blend (call
// ResetPhysicsInterpolation after a teleport). Mode Off is told to the engine too, which keeps a rigid body out.

using Aver.Framework;
using Aver.Scene;

namespace Aver.Fabric;

public partial class Node
{
    /// <summary>Whether a node is drawn between its last two physics steps: On, Off, or whatever its parent is (Godot's physics_interpolation_mode).</summary>
    public enum PhysicsInterpolationModeEnum { Inherit = 0, On = 1, Off = 2 }

    private PhysicsInterpolationModeEnum _interpolationMode;

    public PhysicsInterpolationModeEnum PhysicsInterpolationMode
    {
        get => _interpolationMode;
        set { _interpolationMode = value; ApplyInterpolation(); }
    }

    /// <summary>True when this node's mode, or the first one above it that is not Inherit, is On (the tree's switch not asked).</summary>
    public bool IsPhysicsInterpolated()
    {
        for (Node? n = this; n != null; n = n.GetParent())
            if (n._interpolationMode != PhysicsInterpolationModeEnum.Inherit) return n._interpolationMode == PhysicsInterpolationModeEnum.On;
        return true;
    }

    /// <summary>True when <see cref="IsPhysicsInterpolated"/> and the tree's switch (<see cref="SceneTree.PhysicsInterpolation"/>) is on.</summary>
    public bool IsPhysicsInterpolatedAndEnabled() => SceneTree.Instance.PhysicsInterpolation && IsPhysicsInterpolated();

    /// <summary>The next position of the node is a teleport: it is drawn there at once instead of sliding from where it was (Godot's reset_physics_interpolation). Its children too.</summary>
    public void ResetPhysicsInterpolation()
    {
        ResetInterpolationNow();
        foreach (Node c in ChildrenSnapshot()) c.ResetPhysicsInterpolation();
    }

    internal virtual void ResetInterpolationNow() { }

    /// <summary>Tells the engine which of this node and the ones below it to interpolate. Re-run when a mode, the tree's switch or the parent changes.</summary>
    internal virtual void ApplyInterpolation()
    {
        foreach (Node c in ChildrenSnapshot()) c.ApplyInterpolation();
    }
}

public partial class Node3D
{
    private bool _interpolationListed;

    // A node the engine lists (mode ON) is the root of what it moves.
    internal bool InterpolationListed => _interpolationListed;

    private bool MovesInPhysicsStep => this is CharacterBody3D or AnimatableBody3D || PhysicsInterpolationMode == PhysicsInterpolationModeEnum.On;

    internal override void ApplyInterpolation()
    {
        if (Self.IsValid && NodeHost.Current.IsEngine && IsInsideTree())
        {
            bool covered = false;
            for (Node3D? p = ParentNode3D; p != null; p = p.ParentNode3D) if (p._interpolationListed) { covered = true; break; }
            bool list = !covered && IsPhysicsInterpolatedAndEnabled() && MovesInPhysicsStep;
            InterpolationMode mode = list ? InterpolationMode.On
                : PhysicsInterpolationMode == PhysicsInterpolationModeEnum.Off ? InterpolationMode.Off : InterpolationMode.Inherit;
            _interpolationListed = list;
            try { PoseInterpolation.SetMode(Self.Handle, mode); }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        }
        base.ApplyInterpolation();
    }

    internal override void ResetInterpolationNow()
    {
        if (!Self.IsValid || !NodeHost.Current.IsEngine) return;
        try { PoseInterpolation.Reset(Self.Handle); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    /// <summary>Gives the engine the entity back when the node leaves the tree.</summary>
    internal void ForgetInterpolation()
    {
        if (!_interpolationListed && PhysicsInterpolationMode != PhysicsInterpolationModeEnum.Off) return;
        _interpolationListed = false;
        if (!Self.IsValid || !NodeHost.Current.IsEngine) return;
        try { PoseInterpolation.SetMode(Self.Handle, InterpolationMode.Inherit); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    /// <summary>The global transform as it is drawn this frame, between the last two physics steps (Godot's get_global_transform_interpolated). Without interpolation, or
    /// outside the engine, the global transform.</summary>
    public Transform3D GetGlobalTransformInterpolated()
    {
        Transform3D real = GlobalTransform;
        if (!NodeHost.Current.IsEngine) return real;
        // The listed node at or above this one moves it; this node's place under it is kept.
        Node3D? root = this;
        while (root != null && !root._interpolationListed) root = root.ParentNode3D;
        if (root == null || !root.Self.IsValid) return real;
        try
        {
            if (!PoseInterpolation.TryGetDrawnPose(root.Self.Handle, out Vec3 position, out Quat rotation)) return real;
            Transform3D realRoot = root.GlobalTransform;
            var drawnRoot = new Transform3D(Basis.FromQuaternionScale(FabricSpace.FromAverRotation(rotation), realRoot.Basis.GetScale()), FabricSpace.FromAverPosition(position));
            return root == this ? drawnRoot : drawnRoot * (realRoot.AffineInverse() * real);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return real; }
    }
}

public partial class SceneTree
{
    private bool? _physicsInterpolation;

    /// <summary>Whether nodes are drawn between their last two physics steps (Godot's physics_interpolation, the project's physics/common/physics_interpolation until set).</summary>
    public bool PhysicsInterpolation
    {
        get => _physicsInterpolation ?? ProjectSettings.GetSetting("physics/common/physics_interpolation", false).AsBool();
        set
        {
            _physicsInterpolation = value;
            if (!NodeHost.Current.IsEngine) return;
            try { PoseInterpolation.Enabled = value; }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
            Root.ApplyInterpolation();
        }
    }
}
