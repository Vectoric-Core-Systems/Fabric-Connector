// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// PhysicalBone3D and PhysicalBoneSimulator3D (wave 7, F12): Godot's ragdoll nodes over the engine's ragdoll. The engine builds one ragdoll for a skinned
// skeleton from the skeleton's bones; what the PhysicalBone3D nodes author (which bones are parts, their mass, friction, bounce, joint kind and limits,
// the capsule's radius) is written by the converter as the skeleton's .ocragdoll file, which the engine's ragdoll builder reads. The nodes here carry the
// scene's values, start and stop the simulation, and report the simulated bones' motion. They are not physics bodies of their own.
// docs/plans/WAVE7_SKELETON.md.

namespace Aver.Fabric;

public partial class Skeleton3D
{
    /// <summary>Makes the skeleton a ragdoll (Godot 4.2's Skeleton3D method; 4.3 moved it to PhysicalBoneSimulator3D). Which bones simulate comes from the PhysicalBone3D
    /// nodes the converter wrote into the skeleton's .ocragdoll file, so a list of bone names is not honoured: every authored bone simulates. False/no-op without an engine.</summary>
    public void PhysicalBonesStartSimulation(Collections.Array<StringName>? bones = null)
    {
        if (!StartEngineRagdoll()) Aver.Scripting.Log.Warn($"[Fabric] Skeleton3D '{Name}': physical_bones_start_simulation did nothing (the skeleton is not bound to a skinned model in the engine)");
    }

    public void PhysicalBonesStopSimulation() => StopEngineRagdoll();

    /// <summary>Bodies the ragdoll never collides with; the engine's ragdoll parts never collide with each other, and exceptions against other bodies are not kept.</summary>
    public void PhysicalBonesAddCollisionException(Rid body) { }

    public void PhysicalBonesRemoveCollisionException(Rid body) { }

    public bool IsSimulatingPhysics() => EngineSimulating;
}

/// <summary>The node that runs a skeleton's physical bones (Godot 4.3). Put it under the Skeleton3D with the PhysicalBone3D nodes below it.</summary>
public class PhysicalBoneSimulator3D : SkeletonModifier3D
{
    public PhysicalBoneSimulator3D() { Active = true; }

    public void PhysicalBonesStartSimulation(Collections.Array<StringName>? bones = null) => GetSkeleton()?.PhysicalBonesStartSimulation(bones);

    public void PhysicalBonesStopSimulation() => GetSkeleton()?.PhysicalBonesStopSimulation();

    public bool IsSimulatingPhysics() => GetSkeleton()?.EngineSimulating ?? false;

    public void PhysicalBonesAddCollisionException(Rid body) { }

    public void PhysicalBonesRemoveCollisionException(Rid body) { }
}

/// <summary>One bone of a ragdoll (Godot's PhysicalBone3D). Its CollisionShape3D child shapes the part; the engine's ragdoll builds the part from the converted file.</summary>
public class PhysicalBone3D : PhysicsBody3D
{
    public enum JointTypeEnum { None = 0, Pin = 1, Cone = 2, Hinge = 3, Slider = 4, Joint6Dof = 5 }
    public enum DampMode { Combine = 0, Replace = 1 }

    internal override bool MakesOwnBody => false;

    public StringName BoneName { get; set; } = StringName.Empty;
    public JointTypeEnum JointType { get; set; } = JointTypeEnum.None;
    public Transform3D JointOffset { get; set; } = Transform3D.Identity;
    public Transform3D BodyOffset { get; set; } = Transform3D.Identity;
    public float Mass { get; set; } = 1f;
    public float Friction { get; set; } = 1f;
    public float Bounce { get; set; }
    public float GravityScale { get; set; } = 1f;
    public bool CanSleep { get; set; } = true;
    public bool CustomIntegrator { get; set; }
    public DampMode LinearDampMode { get; set; }
    public DampMode AngularDampMode { get; set; }
    public float LinearDamp { get; set; }
    public float AngularDamp { get; set; }
    public Vector3 LinearVelocity { get; set; }
    public Vector3 AngularVelocity { get; set; }

    private readonly Dictionary<string, Variant> _jointConstraints = new(StringComparer.Ordinal);
    private Vector3 _lastOrigin;
    private bool _hasLast;

    /// <summary>The joint's constraint values as the scene stores them (joint_constraints/swing_span and so on); the converter turns the ones the engine has into the .ocragdoll limits.</summary>
    public Variant GetJointConstraint(string name) => _jointConstraints.TryGetValue(name, out Variant v) ? v : default;

    internal override bool TryDynamicSet(string name, Variant value)
    {
        if (name.StartsWith("joint_constraints/", StringComparison.Ordinal)) { _jointConstraints[name["joint_constraints/".Length..]] = value; return true; }
        return base.TryDynamicSet(name, value);
    }

    internal override bool TryDynamicGet(string name, out Variant value)
    {
        if (name.StartsWith("joint_constraints/", StringComparison.Ordinal)) { value = GetJointConstraint(name["joint_constraints/".Length..]); return true; }
        return base.TryDynamicGet(name, out value);
    }

    private Skeleton3D? TheSkeleton()
    {
        for (Node? p = GetParent(); p != null; p = p.GetParent()) if (p is Skeleton3D s) return s;
        return null;
    }

    public int GetBoneId() => TheSkeleton()?.FindBone(BoneName.ToString()) ?? -1;

    /// <summary>True while the skeleton's ragdoll is running.</summary>
    public bool IsSimulatingPhysics() => TheSkeleton()?.EngineSimulating ?? false;

    public bool GetSimulatePhysics() => IsSimulatingPhysics();

    /// <summary>Pushes the bone's part; a bone the ragdoll has no part for passes it to the nearest ancestor part. Metres and kilograms.</summary>
    public void ApplyCentralImpulse(Vector3 impulse) => TheSkeleton()?.ApplyImpulseToBone(BoneName.ToString(), impulse);

    /// <summary>The engine applies an impulse at the part's centre, so the point is not used.</summary>
    public void ApplyImpulse(Vector3 impulse, Vector3 position = default) => ApplyCentralImpulse(impulse);

    // The bone's velocity is the difference of its world position between physics steps; the engine ragdoll does not report part velocities.
    internal override void InternalPhysicsProcess(double delta)
    {
        base.InternalPhysicsProcess(delta);
        Skeleton3D? sk = TheSkeleton();
        int bone = sk?.FindBone(BoneName.ToString()) ?? -1;
        if (sk == null || bone < 0 || !sk.EngineSimulating || delta <= 0) { _hasLast = false; LinearVelocity = default; return; }
        Vector3 now = (sk.GlobalTransform * sk.GetBoneGlobalPose(bone)).Origin;
        LinearVelocity = _hasLast ? (now - _lastOrigin) / (float)delta : default;
        _lastOrigin = now;
        _hasLast = true;
    }
}
