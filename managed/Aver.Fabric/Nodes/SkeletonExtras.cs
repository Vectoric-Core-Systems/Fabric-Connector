// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// The skeleton and mesh API remainder (wave 7, F18): bone metadata lists, Skin and SkinReference, MeshInstance3D's skin, skeleton path and blend-shape
// index API, and the AnimationNodeExtension base (F17).

namespace Aver.Fabric;

/// <summary>The bones a mesh is bound to and their bind poses (Godot's Skin). The engine keeps the bind pose with the skeleton, so this only carries the data scripts give it.</summary>
public class Skin : Resource
{
    private readonly List<(int Bone, StringName Name, Transform3D Bind)> _binds = new();

    public void SetBindCount(int count)
    {
        while (_binds.Count < count) _binds.Add((-1, StringName.Empty, Transform3D.Identity));
        if (_binds.Count > count) _binds.RemoveRange(count, _binds.Count - count);
    }

    public int GetBindCount() => _binds.Count;

    public void AddBind(int bone, Transform3D pose) => _binds.Add((bone, StringName.Empty, pose));

    public void AddNamedBind(StringName name, Transform3D pose) => _binds.Add((-1, name, pose));

    public void SetBindBone(int bindIndex, int bone) { if (bindIndex >= 0 && bindIndex < _binds.Count) _binds[bindIndex] = (bone, _binds[bindIndex].Name, _binds[bindIndex].Bind); }

    public int GetBindBone(int bindIndex) => bindIndex >= 0 && bindIndex < _binds.Count ? _binds[bindIndex].Bone : -1;

    public void SetBindName(int bindIndex, StringName name) { if (bindIndex >= 0 && bindIndex < _binds.Count) _binds[bindIndex] = (_binds[bindIndex].Bone, name, _binds[bindIndex].Bind); }

    public StringName GetBindName(int bindIndex) => bindIndex >= 0 && bindIndex < _binds.Count ? _binds[bindIndex].Name : StringName.Empty;

    public void SetBindPose(int bindIndex, Transform3D pose) { if (bindIndex >= 0 && bindIndex < _binds.Count) _binds[bindIndex] = (_binds[bindIndex].Bone, _binds[bindIndex].Name, pose); }

    public Transform3D GetBindPose(int bindIndex) => bindIndex >= 0 && bindIndex < _binds.Count ? _binds[bindIndex].Bind : Transform3D.Identity;

    public void ClearBinds() => _binds.Clear();
}

/// <summary>A skin as bound to one skeleton (Godot's SkinReference).</summary>
public class SkinReference : RefCounted
{
    internal Skeleton3D? Owner;
    internal Skin? SkinData;

    public Skin? GetSkin() => SkinData;

    public Rid GetSkeleton() => default;
}

public partial class Skeleton3D
{
    public StringName[] GetBoneMetaList(int boneIdx) =>
        Valid(boneIdx) && _bones[boneIdx].Meta != null ? _bones[boneIdx].Meta!.Keys.Select(k => new StringName(k)).ToArray() : System.Array.Empty<StringName>();

    public bool HasBoneMeta(int boneIdx, StringName key) => Valid(boneIdx) && _bones[boneIdx].Meta != null && _bones[boneIdx].Meta!.ContainsKey(key.ToString());

    /// <summary>The bone names joined by commas, in bone order (Godot's get_concatenated_bone_names).</summary>
    public StringName GetConcatenatedBoneNames() => string.Join(",", _bones.Select(b => b.Name));

    /// <summary>Takes the bone out of its parent's chain and keeps its place in the world: the rest becomes its global rest.</summary>
    public void UnparentBoneAndRest(int boneIdx)
    {
        if (!Valid(boneIdx)) return;
        Transform3D global = GetBoneGlobalRest(boneIdx);
        _bones[boneIdx].Parent = -1;
        _bones[boneIdx].Rest = global;
        Dirty(true);
    }

    /// <summary>A skin whose binds are the inverses of the bones' global rest poses.</summary>
    public Skin CreateSkinFromRestTransforms()
    {
        var skin = new Skin();
        for (int i = 0; i < _bones.Count; i++) { skin.AddBind(i, GetBoneGlobalRest(i).AffineInverse()); skin.SetBindName(i, _bones[i].Name); }
        return skin;
    }

    /// <summary>The skin as bound to this skeleton. The engine binds skinned meshes to their skeleton itself, so the reference only carries the skin back.</summary>
    public SkinReference RegisterSkin(Skin skin) => new() { Owner = this, SkinData = skin };

    /// <summary>Recomputes the global poses of the children of a bone; poses are always recomputed when read, so this only tells followers.</summary>
    public void ForceUpdateBoneChildTransform(int boneIdx)
    {
        if (!Valid(boneIdx)) return;
        Update();
        EmitSignal(SignalName.PoseUpdated);
    }

    /// <summary>Runs the modifiers once for <paramref name="delta"/> seconds now (Godot's advance).</summary>
    public void Advance(double delta) => AdvanceModifiers(delta);

    /// <summary>Stored: physical bones follow the animation through the engine's ragdoll, which has no per-bone animation switch.</summary>
    public bool AnimatePhysicalBones { get; set; } = true;

    public void SetAnimatePhysicalBones(bool enabled) => AnimatePhysicalBones = enabled;

    public bool GetAnimatePhysicalBones() => AnimatePhysicalBones;
}

public partial class MeshInstance3D
{
    private readonly List<string> _shapeNames = new();
    private readonly Dictionary<string, int> _shapeIndex = new(StringComparer.Ordinal);

    /// <summary>The path to the Skeleton3D that poses this mesh (default "..", the node's parent).</summary>
    public NodePath SkeletonPath { get; set; } = new NodePath("..");

    public Skin? Skin { get; set; }

    public SkinReference? GetSkinReference() => (GetNodeOrNull(SkeletonPath) as Skeleton3D)?.RegisterSkin(Skin ?? new Skin());

    // The blend shapes the node knows by name: the ones the converter listed from the mesh (aver_blend_shapes), then any a script or animation set.
    private int ShapeIndex(string name)
    {
        if (_shapeIndex.TryGetValue(name, out int i)) return i;
        _shapeNames.Add(name);
        return _shapeIndex[name] = _shapeNames.Count - 1;
    }

    internal void SetShapeNames(string joined)
    {
        foreach (string n in joined.Split('|', StringSplitOptions.RemoveEmptyEntries)) ShapeIndex(n);
    }

    public int GetBlendShapeCount() => Math.Max(_shapeNames.Count, NodeHost.Current.IsEngine && Self.IsValid ? Self.MorphTargetCount : 0);

    /// <summary>The index of a blend shape by name, or -1 (a name the node has not seen is not a blend shape of its mesh).</summary>
    public int FindBlendShapeByName(StringName name) => _shapeIndex.TryGetValue(name.ToString(), out int i) ? i : -1;

    public float GetBlendShapeValue(int blendShape) => blendShape >= 0 && blendShape < _shapeNames.Count ? GetBlendShapeValueByName(_shapeNames[blendShape]) : 0f;

    public void SetBlendShapeValue(int blendShape, float value)
    {
        if (blendShape >= 0 && blendShape < _shapeNames.Count) SetBlendShapeValueByName(_shapeNames[blendShape], value);
    }

    public string GetBlendShapeName(int blendShape) => blendShape >= 0 && blendShape < _shapeNames.Count ? _shapeNames[blendShape] : "";
}

/// <summary>The base of a custom animation node written in script (Godot's AnimationNodeExtension). The helpers read the info arrays the processing hook receives;
/// the node is kept in a tree but does not mix animations (a tree node's evaluation is the engine's, not scriptable): approximated.</summary>
public class AnimationNodeExtension : AnimationNode
{
    public enum PlaybackInfoIndex { Time = 0, Delta = 1, SeekTime = 2, IsExternalSeeking = 3, Looped = 4, Weight = 5 }

    public override string GetCaption() => "Extension";

    /// <summary>Overridden by scripts to process one pass; the result is not used.</summary>
    public virtual float[] _ProcessAnimationNode(double[] playbackInfo, bool testOnly) => System.Array.Empty<float>();

    /// <summary>Whether the node info (the array a processing pass returns) says the node loops.</summary>
    public static bool IsLooping(float[] nodeInfo) => nodeInfo.Length > 2 && nodeInfo[2] != 0f;

    /// <summary>The time left in the node, from its info array (length minus position).</summary>
    public static float GetRemainingTime(float[] nodeInfo, bool breakLoop = false)
    {
        if (nodeInfo.Length < 2) return 0f;
        float remaining = nodeInfo[1] - nodeInfo[0];
        return IsLooping(nodeInfo) && !breakLoop ? float.PositiveInfinity : Math.Max(remaining, 0f);
    }
}
