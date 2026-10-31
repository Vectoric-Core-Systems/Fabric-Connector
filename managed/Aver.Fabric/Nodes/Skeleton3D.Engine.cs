// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Skeleton3D's link to the engine: the skinned meshes below an imported model's skeleton are posed from this node's bone poses (wave 7, F13), and a
// ragdoll can take the pose over (F12). The node stays the model of record -- scripts and AnimationPlayer bone tracks write its poses as before; once a
// frame the poses are pushed through the engine's pose seam (Entity.SetBonePoses) to every skinned mesh entity.
//
// THE POSE MAPPING. A Godot bone pose is a full local transform in metres and Godot's axes. The engine's bone is a local transform in centimetres in
// the engine's axes, and for a joint at the top of its skin it also carries the nodes above it. Both are the same pose of the same bone, so the
// mapping goes through the rest poses: engineLocal = toEngine(engineRestAsGodot * godotRest^-1 * godotPose). For a bone with no nodes above it
// engineRestAsGodot equals godotRest and this is toEngine(godotPose); for a top joint the rests carry what is above and cancel it in the quotient.

using Aver.Framework;
using Aver.Scene;

namespace Aver.Fabric;

public partial class Skeleton3D
{
    private string _engineSkeleton = "";
    private List<Entity>? _skins;
    private int[]? _godotOfEngine;           // engine bone -> this node's bone, -1 when the engine bone has no namesake here
    private Transform3D[]? _restMap;         // engine bone -> engineRestAsGodot * godotRest^-1
    private BonePose[]? _engineRest;         // the engine's own rest pose per engine bone, for the ones no Godot bone drives
    private bool _engineDirty = true;
    private bool _simulating;

    /// <summary>The content path of the engine skeleton (.ocskel) the imported model's skinned meshes are bound to; empty when the node is not bound.</summary>
    public string EngineSkeletonAsset => _engineSkeleton;

    /// <summary>True while a ragdoll drives the bones (PhysicalBoneSimulator3D.PhysicalBonesStartSimulation).</summary>
    internal bool EngineSimulating => _simulating;

    internal int SkinCount => _skins?.Count ?? 0;

    /// <summary>A skinned mesh below this node asks to be posed by it: its entity gets the engine skeleton and an animator.</summary>
    internal void AttachSkin(Entity e)
    {
        if (_engineSkeleton.Length == 0 || !e.IsValid) return;
        _skins ??= new List<Entity>();
        if (_skins.Contains(e)) return;
        e.SetSkeleton(_engineSkeleton);
        if (!e.HasComponent(Component.Animator)) e.AddComponent(Component.Animator);
        _skins.Add(e);
        _engineDirty = true;
    }

    /// <summary>Godot's bone pose as the engine's local transform for the same bone. <paramref name="restMap"/> is engineRestAsGodot * godotRest^-1.</summary>
    internal static BonePose ToEngine(Transform3D restMap, Transform3D godotPose)
    {
        FabricSpace.ToAver(restMap * godotPose, out Vec3 p, out Quat q, out Vec3 s);
        return new BonePose(p, q, s);
    }

    /// <summary>The inverse: the engine's local transform as Godot's bone pose.</summary>
    internal static Transform3D FromEngine(Transform3D restMap, BonePose enginePose) =>
        restMap.AffineInverse() * FabricSpace.FromAver(enginePose.Position, enginePose.Rotation, enginePose.Scale);

    // The pose a bone has after the overrides: its own local pose, or the override's blend taken back into the parent's frame.
    private Transform3D LocalForEngine(int i)
    {
        Bone b = _bones[i];
        Transform3D local = _showRestOnly || !b.Enabled ? b.Rest : new Transform3D(Basis.FromQuaternionScale(b.Rotation, b.Scale), b.Position);
        if (b.OverrideAmount <= 0) return local;
        return b.Parent < 0 ? b.Global : _bones[b.Parent].Global.AffineInverse() * b.Global;
    }

    // Matches the engine's bones to this node's by name (the importer sorts bones parents first and leaves out the nodes between joints).
    private bool BindEngine(Entity e) => _godotOfEngine != null || Rebind(e);

    private bool Rebind(Entity e)
    {
        int n = e.PosedBoneCount;
        if (n == 0) return false;   // the engine has not posed the entity yet; try again next frame
        _godotOfEngine = new int[n];
        _restMap = new Transform3D[n];
        _engineRest = new BonePose[n];
        for (int j = 0; j < n; j++)
        {
            _godotOfEngine[j] = -1;
            _restMap[j] = Transform3D.Identity;
            BoneInfo? info = e.GetBoneInfo(j);
            if (info is null) continue;
            _engineRest[j] = info.Value.Rest;
            int i = FindBone(info.Value.Name);
            if (i < 0) continue;
            _godotOfEngine[j] = i;
            Transform3D restAsGodot = FabricSpace.FromAver(info.Value.Rest.Position, info.Value.Rest.Rotation, info.Value.Rest.Scale);
            _restMap[j] = restAsGodot * _bones[i].Rest.AffineInverse();
        }
        return true;
    }

    // Writes the node's bone poses to the skinned entities; the master (the first skin) is left alone while a ragdoll drives it.
    private void PushToEngine()
    {
        if (_skins == null || _skins.Count == 0 || !NodeHost.Current.IsEngine) return;
        if (!_engineDirty) return;
        Update();
        bool allBound = true;
        for (int k = 0; k < _skins.Count; k++)
        {
            Entity e = _skins[k];
            if (!e.IsValid) continue;
            if (_simulating && k == 0) continue;
            if (!BindEngine(e)) { allBound = false; continue; }
            var poses = new BonePose[_godotOfEngine!.Length];
            for (int j = 0; j < poses.Length; j++)
            {
                int i = _godotOfEngine[j];
                poses[j] = i < 0 ? _engineRest![j] : ToEngine(_restMap![j], LocalForEngine(i));
            }
            e.SetBonePoses(0, poses);
        }
        if (allBound) _engineDirty = false;
    }

    // While a ragdoll drives the first skin, this node's poses follow what the engine ended up with, so bone attachments and scripts see the simulation.
    private void RefreshFromEngine()
    {
        if (!_simulating || _skins == null || _skins.Count == 0 || _godotOfEngine == null) return;
        Entity master = _skins[0];
        if (!master.IsValid) { _simulating = false; return; }
        for (int j = 0; j < _godotOfEngine.Length; j++)
        {
            int i = _godotOfEngine[j];
            BonePose? p = master.GetBonePose(j);
            if (i < 0 || p is null) continue;
            Transform3D g = FromEngine(_restMap![j], p.Value);
            Bone b = _bones[i];
            b.Position = g.Origin;
            b.Scale = g.Basis.GetScale();
            b.Rotation = g.Basis.Orthonormalized().GetRotationQuaternion();
        }
        Dirty(false);
        _engineDirty = true;   // the other skins are pushed the simulated pose
    }

    /// <summary>Starts the engine ragdoll on the first skinned mesh (the other skins follow it). False when the node is not bound or the engine builds none.</summary>
    internal bool StartEngineRagdoll()
    {
        if (_skins == null || _skins.Count == 0 || !NodeHost.Current.IsEngine) return false;
        Entity master = _skins[0];
        if (!master.IsValid || !master.StartRagdoll(RagdollMode.Limp, 0.0f)) return false;
        _simulating = true;
        _engineDirty = true;
        return true;
    }

    /// <summary>Hands the pose back to the animation: the ragdoll blends out and the node's own poses are pushed again.</summary>
    internal void StopEngineRagdoll(float blendSeconds = 0.0f)
    {
        if (!_simulating) return;
        _simulating = false;
        if (_skins != null && _skins.Count > 0 && _skins[0].IsValid) _skins[0].StopRagdoll(blendSeconds);
        _engineDirty = true;
    }

    internal void ApplyImpulseToBone(string boneName, Vector3 impulseGodot)
    {
        if (!_simulating || _skins == null || _skins.Count == 0) return;
        _skins[0].AddRagdollImpulse(boneName, FabricSpace.ToAverDirection(impulseGodot) * FabricSpace.CmPerMetre);
    }

    internal bool TrySetEngineProperty(string name, Variant value)
    {
        if (name != "aver_skeleton") return false;
        _engineSkeleton = value.AsString();
        return true;
    }
}
