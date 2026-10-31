// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// The bone constraint modifiers (wave 7, F15): LookAtModifier3D, and the BoneConstraint3D family (CopyTransformModifier3D, AimModifier3D,
// ConvertTransformModifier3D). They edit the Skeleton3D's pose model, which poses the skinned meshes of a bound model through the engine's pose seam.
// Approximated against Godot's: the settings are kept, and the pose they make is the direct one (no secondary-rotation solve for LookAt beyond the shortest
// arc, no Euler-limited aim, no damping curves beyond the exponential smoothing of `duration`).

namespace Aver.Fabric;

/// <summary>Turns a bone to face a node (Godot's LookAtModifier3D).</summary>
public class LookAtModifier3D : SkeletonModifier3D
{
    public enum OriginFromEnum { Self = 0, SpecificBone = 1, ExternalNode = 2 }

    public NodePath TargetNode { get; set; } = new();
    public StringName BoneName { get; set; } = StringName.Empty;
    public BoneAxis ForwardAxis { get; set; } = BoneAxis.PlusZ;
    public Vector3.Axis PrimaryRotationAxis { get; set; } = Vector3.Axis.Y;
    public bool UseSecondaryRotation { get; set; } = true;
    public OriginFromEnum OriginFrom { get; set; }
    public StringName OriginBoneName { get; set; } = StringName.Empty;
    public NodePath OriginExternalNode { get; set; } = new();
    public Vector3 OriginOffset { get; set; }
    public float OriginSafeMargin { get; set; } = 0.1f;
    /// <summary>Seconds to turn to a new target (0: at once); approximated by exponential smoothing.</summary>
    public float Duration { get; set; }
    public bool UseAngleLimitation { get; set; }
    /// <summary>The widest the bone may turn from its rest direction, radians (the symmetric primary limit).</summary>
    public float PrimaryLimitAngle { get; set; } = MathF.Tau;
    public bool SymmetryLimitation { get; set; } = true;
    public Tween.TransitionType TransitionType { get; set; } = Tween.TransitionType.Linear;
    public Tween.EaseType EaseType { get; set; } = Tween.EaseType.InOut;

    private Quaternion _smoothed = Quaternion.Identity;
    private bool _hasSmoothed;
    private bool _interpolating;

    public float GetInterpolationRemaining() => _interpolating ? Duration : 0f;
    public bool IsInterpolating() => _interpolating;

    internal override void ProcessModificationWithDelta(double delta)
    {
        _dt = (float)delta;
        base.ProcessModificationWithDelta(delta);
    }

    private float _dt;

    public override void _ProcessModification()
    {
        Skeleton3D? skel = GetSkeleton();
        if (skel == null || GetNodeOrNull(TargetNode) is not Node3D target) return;
        int bone = skel.FindBone(BoneName.ToString());
        if (bone < 0) return;
        Transform3D global = skel.GetBoneGlobalPoseNoOverride(bone);
        Vector3 origin = global.Origin;
        if (OriginFrom == OriginFromEnum.SpecificBone && skel.FindBone(OriginBoneName.ToString()) is int ob && ob >= 0) origin = skel.GetBoneGlobalPoseNoOverride(ob).Origin;
        else if (OriginFrom == OriginFromEnum.ExternalNode && GetNodeOrNull(OriginExternalNode) is Node3D on) origin = (skel.GlobalTransform.AffineInverse() * on.GlobalTransform).Origin;
        origin += OriginOffset;

        Vector3 to = (skel.GlobalTransform.AffineInverse() * target.GlobalTransform).Origin - origin;
        if (to.Length() <= OriginSafeMargin) return;
        to = to.Normalized();
        Vector3 forward = (global.Basis * AxisVector(ForwardAxis)).Normalized();
        Quaternion turn = new(forward, to);

        if (UseAngleLimitation)
        {
            // Measured from where the bone points at rest.
            Vector3 rest = (skel.GetBoneGlobalRest(bone).Basis * AxisVector(ForwardAxis)).Normalized();
            Vector3 limited = to;
            float angle = MathF.Acos(Math.Clamp(rest.Dot(to), -1f, 1f));
            if (angle > PrimaryLimitAngle && angle > 1e-5f) limited = rest.Slerp(to, PrimaryLimitAngle / angle);
            turn = new Quaternion(forward, limited.Normalized());
        }

        Quaternion goal = turn;
        if (Duration > 1e-4f && _dt > 0f)
        {
            if (!_hasSmoothed) { _smoothed = Quaternion.Identity; _hasSmoothed = true; }
            float k = 1f - MathF.Exp(-_dt / Duration);
            _smoothed = _smoothed.Slerp(goal, k).Normalized();
            _interpolating = _smoothed.AngleTo(goal) > 1e-3f;
            goal = _smoothed;
        }
        else _interpolating = false;

        global.Basis = new Basis(goal) * global.Basis;
        skel.SetBoneGlobalPose(bone, global);
    }
}

/// <summary>The base of the modifiers that apply a bone from a reference bone or node, one setting per apply bone (Godot's BoneConstraint3D).</summary>
public class BoneConstraint3D : SkeletonModifier3D
{
    public enum ReferenceTypeEnum { Bone = 0, Node = 1 }

    /// <summary>One apply bone with its reference and amount; subclasses add their own fields.</summary>
    public class Setting
    {
        public string ApplyBone = "";
        public string ReferenceBone = "";
        public ReferenceTypeEnum ReferenceType;
        public NodePath ReferenceNode = new();
        public float Amount = 1f;
        public Dictionary<string, Variant> Extra = new(StringComparer.Ordinal);
    }

    private readonly List<Setting> _settings = new();

    internal IReadOnlyList<Setting> Settings => _settings;

    public int SettingCount
    {
        get => _settings.Count;
        set
        {
            value = Math.Max(0, value);
            while (_settings.Count < value) _settings.Add(NewSetting());
            if (_settings.Count > value) _settings.RemoveRange(value, _settings.Count - value);
        }
    }

    internal virtual Setting NewSetting() => new();

    public int GetSettingCount() => SettingCount;
    public void SetSettingCount(int count) => SettingCount = count;
    public void ClearSetting(int index) { if (index >= 0 && index < _settings.Count) _settings[index] = NewSetting(); }

    public void SetAmount(int index, float amount) { if (Has(index)) _settings[index].Amount = amount; }
    public float GetAmount(int index) => Has(index) ? _settings[index].Amount : 0f;
    public void SetApplyBoneName(int index, StringName name) { if (Has(index)) _settings[index].ApplyBone = name.ToString(); }
    public StringName GetApplyBoneName(int index) => Has(index) ? _settings[index].ApplyBone : StringName.Empty;
    public void SetApplyBone(int index, int bone) { if (Has(index) && GetSkeleton() is { } s) _settings[index].ApplyBone = s.GetBoneName(bone); }
    public int GetApplyBone(int index) => Has(index) ? GetSkeleton()?.FindBone(_settings[index].ApplyBone) ?? -1 : -1;
    public void SetReferenceBoneName(int index, StringName name) { if (Has(index)) { _settings[index].ReferenceBone = name.ToString(); _settings[index].ReferenceType = ReferenceTypeEnum.Bone; } }
    public StringName GetReferenceBoneName(int index) => Has(index) ? _settings[index].ReferenceBone : StringName.Empty;
    public void SetReferenceBone(int index, int bone) { if (Has(index) && GetSkeleton() is { } s) SetReferenceBoneName(index, s.GetBoneName(bone)); }
    public int GetReferenceBone(int index) => Has(index) ? GetSkeleton()?.FindBone(_settings[index].ReferenceBone) ?? -1 : -1;
    public void SetReferenceNode(int index, NodePath path) { if (Has(index)) { _settings[index].ReferenceNode = path; _settings[index].ReferenceType = ReferenceTypeEnum.Node; } }
    public NodePath GetReferenceNode(int index) => Has(index) ? _settings[index].ReferenceNode : new NodePath();

    private bool Has(int index) => index >= 0 && index < _settings.Count;

    // settings/N/<name>: as scenes store them.
    internal override bool TryDynamicSet(string name, Variant value)
    {
        if (!name.StartsWith("settings/", StringComparison.Ordinal)) return base.TryDynamicSet(name, value);
        string[] p = name.Split('/');
        if (p.Length != 3 || !int.TryParse(p[1], out int i) || i < 0 || i > 4096) return false;
        if (i >= _settings.Count) SettingCount = i + 1;
        Setting s = _settings[i];
        switch (p[2])
        {
            case "apply_bone_name": s.ApplyBone = value.AsString(); break;
            case "reference_bone_name": s.ReferenceBone = value.AsString(); break;
            case "reference_type": s.ReferenceType = (ReferenceTypeEnum)value.AsInt32(); break;
            case "reference_node": s.ReferenceNode = value.AsNodePath(); break;
            case "amount": s.Amount = value.AsSingle(); break;
            default: s.Extra[p[2]] = value; break;
        }
        return true;
    }

    /// <summary>The reference's transform in the skeleton's space, or false when it cannot be found.</summary>
    protected bool ReferenceOf(Skeleton3D skel, Setting s, out Transform3D reference)
    {
        reference = Transform3D.Identity;
        if (s.ReferenceType == ReferenceTypeEnum.Node)
        {
            if (GetNodeOrNull(s.ReferenceNode) is not Node3D n) return false;
            reference = skel.GlobalTransform.AffineInverse() * n.GlobalTransform;
            return true;
        }
        int b = skel.FindBone(s.ReferenceBone);
        if (b < 0) return false;
        reference = skel.GetBoneGlobalPose(b);
        return true;
    }

    protected static Transform3D Blend(Transform3D from, Transform3D to, float amount) => amount >= 1f ? to : from.InterpolateWith(to, Math.Clamp(amount, 0f, 1f));

    internal float Flag(Setting s, string key, float def) => s.Extra.TryGetValue(key, out Variant v) ? v.AsSingle() : def;

    public override void _ProcessModification()
    {
        Skeleton3D? skel = GetSkeleton();
        if (skel == null) return;
        foreach (Setting s in _settings)
        {
            int apply = skel.FindBone(s.ApplyBone);
            if (apply < 0 || s.Amount <= 0f || !ReferenceOf(skel, s, out Transform3D reference)) continue;
            Transform3D current = skel.GetBoneGlobalPoseNoOverride(apply);
            skel.SetBoneGlobalPose(apply, Blend(current, Constrain(skel, s, apply, current, reference), s.Amount));
        }
    }

    /// <summary>The bone's new global pose for one setting.</summary>
    protected virtual Transform3D Constrain(Skeleton3D skel, Setting s, int applyBone, Transform3D current, Transform3D reference) => current;
}

/// <summary>Copies a reference's position, rotation or scale onto a bone, per axis (Godot's CopyTransformModifier3D).</summary>
public class CopyTransformModifier3D : BoneConstraint3D
{
    public const int TransformFlagPosition = 1, TransformFlagRotation = 2, TransformFlagScale = 4, TransformFlagAll = 7;
    public const int AxisFlagX = 1, AxisFlagY = 2, AxisFlagZ = 4, AxisFlagAll = 7;

    private static int Copy(Setting s) => (int)s.Extra.GetValueOrDefault("copy_flags", TransformFlagAll).AsInt32();
    private static int Axes(Setting s) => (int)s.Extra.GetValueOrDefault("axis_flags", AxisFlagAll).AsInt32();
    private static int Invert(Setting s) => (int)s.Extra.GetValueOrDefault("invert_flags", 0).AsInt32();
    private static bool Rel(Setting s) => s.Extra.GetValueOrDefault("relative", true).AsBool();
    private static bool Add(Setting s) => s.Extra.GetValueOrDefault("additive", false).AsBool();

    public void SetCopyFlags(int index, int flags) { if (index >= 0 && index < SettingCount) Settings[index].Extra["copy_flags"] = flags; }
    public int GetCopyFlags(int index) => index >= 0 && index < SettingCount ? Copy(Settings[index]) : 0;
    public void SetAxisFlags(int index, int flags) { if (index >= 0 && index < SettingCount) Settings[index].Extra["axis_flags"] = flags; }
    public int GetAxisFlags(int index) => index >= 0 && index < SettingCount ? Axes(Settings[index]) : 0;
    public void SetInvertFlags(int index, int flags) { if (index >= 0 && index < SettingCount) Settings[index].Extra["invert_flags"] = flags; }
    public int GetInvertFlags(int index) => index >= 0 && index < SettingCount ? Invert(Settings[index]) : 0;
    public void SetRelative(int index, bool relative) { if (index >= 0 && index < SettingCount) Settings[index].Extra["relative"] = relative; }
    public bool IsRelative(int index) => index >= 0 && index < SettingCount && Rel(Settings[index]);
    public void SetAdditive(int index, bool additive) { if (index >= 0 && index < SettingCount) Settings[index].Extra["additive"] = additive; }
    public bool IsAdditive(int index) => index >= 0 && index < SettingCount && Add(Settings[index]);

    protected override Transform3D Constrain(Skeleton3D skel, Setting s, int applyBone, Transform3D current, Transform3D reference)
    {
        int copy = Copy(s), axes = Axes(s), inv = Invert(s);
        Transform3D result = current;
        if ((copy & TransformFlagPosition) != 0)
        {
            Vector3 p = reference.Origin;
            if (Rel(s) && ReferenceRest(skel, s, out Transform3D rest)) p -= rest.Origin;
            Vector3 d = Masked(p, axes, inv);
            result.Origin = Add(s) || Rel(s) ? current.Origin + d : new Vector3((axes & AxisFlagX) != 0 ? d.X : current.Origin.X, (axes & AxisFlagY) != 0 ? d.Y : current.Origin.Y, (axes & AxisFlagZ) != 0 ? d.Z : current.Origin.Z);
        }
        if ((copy & TransformFlagRotation) != 0)
        {
            Basis r = reference.Basis.Orthonormalized();
            if (Rel(s) && ReferenceRest(skel, s, out Transform3D rest)) r = r * rest.Basis.Orthonormalized().Inverse();
            Vector3 euler = Masked(r.GetEuler(), axes, inv);
            Basis turn = Basis.FromEuler(euler);
            result.Basis = Basis.FromQuaternionScale((Add(s) || Rel(s) ? turn * current.Basis.Orthonormalized() : turn).GetRotationQuaternion(), current.Basis.GetScale());
        }
        if ((copy & TransformFlagScale) != 0)
        {
            Vector3 sc = reference.Basis.GetScale();
            Vector3 m = new((axes & AxisFlagX) != 0 ? sc.X : 1f, (axes & AxisFlagY) != 0 ? sc.Y : 1f, (axes & AxisFlagZ) != 0 ? sc.Z : 1f);
            result.Basis = result.Basis.Orthonormalized() * Basis.FromScale(Add(s) ? current.Basis.GetScale() * m : m);
        }
        return result;
    }

    private bool ReferenceRest(Skeleton3D skel, Setting s, out Transform3D rest)
    {
        rest = Transform3D.Identity;
        int b = s.ReferenceType == ReferenceTypeEnum.Bone ? skel.FindBone(s.ReferenceBone) : -1;
        if (b < 0) return false;
        rest = skel.GetBoneGlobalRest(b);
        return true;
    }

    private static Vector3 Masked(Vector3 v, int axes, int invert) => new(
        (axes & AxisFlagX) == 0 ? 0f : (invert & AxisFlagX) != 0 ? -v.X : v.X,
        (axes & AxisFlagY) == 0 ? 0f : (invert & AxisFlagY) != 0 ? -v.Y : v.Y,
        (axes & AxisFlagZ) == 0 ? 0f : (invert & AxisFlagZ) != 0 ? -v.Z : v.Z);
}

/// <summary>Points a bone's forward axis at a reference (Godot's AimModifier3D).</summary>
public class AimModifier3D : BoneConstraint3D
{
    public void SetForwardAxis(int index, BoneAxis axis) { if (index >= 0 && index < SettingCount) Settings[index].Extra["forward_axis"] = (int)axis; }
    public BoneAxis GetForwardAxis(int index) => index >= 0 && index < SettingCount ? (BoneAxis)Settings[index].Extra.GetValueOrDefault("forward_axis", (int)BoneAxis.PlusY).AsInt32() : BoneAxis.PlusY;
    public void SetUseEuler(int index, bool on) { if (index >= 0 && index < SettingCount) Settings[index].Extra["use_euler"] = on; }
    public bool IsUsingEuler(int index) => index >= 0 && index < SettingCount && Settings[index].Extra.GetValueOrDefault("use_euler", false).AsBool();
    public void SetPrimaryRotationAxis(int index, Vector3.Axis axis) { if (index >= 0 && index < SettingCount) Settings[index].Extra["primary_rotation_axis"] = (int)axis; }
    public Vector3.Axis GetPrimaryRotationAxis(int index) => index >= 0 && index < SettingCount ? (Vector3.Axis)Settings[index].Extra.GetValueOrDefault("primary_rotation_axis", (int)Vector3.Axis.Y).AsInt32() : Vector3.Axis.Y;
    public void SetUseSecondaryRotation(int index, bool on) { if (index >= 0 && index < SettingCount) Settings[index].Extra["use_secondary_rotation"] = on; }
    public bool IsUsingSecondaryRotation(int index) => index >= 0 && index < SettingCount && Settings[index].Extra.GetValueOrDefault("use_secondary_rotation", true).AsBool();

    protected override Transform3D Constrain(Skeleton3D skel, Setting s, int applyBone, Transform3D current, Transform3D reference)
    {
        Vector3 to = reference.Origin - current.Origin;
        if (to.LengthSquared() < 1e-10f) return current;
        BoneAxis axis = (BoneAxis)s.Extra.GetValueOrDefault("forward_axis", (int)BoneAxis.PlusY).AsInt32();
        Vector3 forward = (current.Basis * AxisVector(axis)).Normalized();
        Transform3D result = current;
        result.Basis = new Basis(new Quaternion(forward, to.Normalized())) * current.Basis;
        return result;
    }
}

/// <summary>Maps a value of the reference's transform onto a value of the bone's, through a range (Godot's ConvertTransformModifier3D).</summary>
public class ConvertTransformModifier3D : BoneConstraint3D
{
    public enum TransformMode { Position = 0, Rotation = 1, Scale = 2 }

    private static TransformMode ModeOf(Setting s, string key) => (TransformMode)s.Extra.GetValueOrDefault(key, (int)(key == "apply_transform_mode" ? TransformMode.Rotation : TransformMode.Rotation)).AsInt32();
    private static int AxisOf(Setting s, string key) => s.Extra.GetValueOrDefault(key, 0).AsInt32();
    private static float RangeOf(Setting s, string key, float def) => s.Extra.TryGetValue(key, out Variant v) ? v.AsSingle() : def;

    public void SetApplyTransformMode(int index, TransformMode mode) { if (index >= 0 && index < SettingCount) Settings[index].Extra["apply_transform_mode"] = (int)mode; }
    public TransformMode GetApplyTransformMode(int index) => index >= 0 && index < SettingCount ? ModeOf(Settings[index], "apply_transform_mode") : TransformMode.Rotation;
    public void SetApplyAxis(int index, Vector3.Axis axis) { if (index >= 0 && index < SettingCount) Settings[index].Extra["apply_axis"] = (int)axis; }
    public Vector3.Axis GetApplyAxis(int index) => index >= 0 && index < SettingCount ? (Vector3.Axis)AxisOf(Settings[index], "apply_axis") : Vector3.Axis.X;
    public void SetApplyRangeMin(int index, float v) { if (index >= 0 && index < SettingCount) Settings[index].Extra["apply_range_min"] = v; }
    public float GetApplyRangeMin(int index) => index >= 0 && index < SettingCount ? RangeOf(Settings[index], "apply_range_min", -MathF.PI) : 0f;
    public void SetApplyRangeMax(int index, float v) { if (index >= 0 && index < SettingCount) Settings[index].Extra["apply_range_max"] = v; }
    public float GetApplyRangeMax(int index) => index >= 0 && index < SettingCount ? RangeOf(Settings[index], "apply_range_max", MathF.PI) : 0f;
    public void SetReferenceTransformMode(int index, TransformMode mode) { if (index >= 0 && index < SettingCount) Settings[index].Extra["reference_transform_mode"] = (int)mode; }
    public TransformMode GetReferenceTransformMode(int index) => index >= 0 && index < SettingCount ? ModeOf(Settings[index], "reference_transform_mode") : TransformMode.Rotation;
    public void SetReferenceAxis(int index, Vector3.Axis axis) { if (index >= 0 && index < SettingCount) Settings[index].Extra["reference_axis"] = (int)axis; }
    public Vector3.Axis GetReferenceAxis(int index) => index >= 0 && index < SettingCount ? (Vector3.Axis)AxisOf(Settings[index], "reference_axis") : Vector3.Axis.X;
    public void SetReferenceRangeMin(int index, float v) { if (index >= 0 && index < SettingCount) Settings[index].Extra["reference_range_min"] = v; }
    public float GetReferenceRangeMin(int index) => index >= 0 && index < SettingCount ? RangeOf(Settings[index], "reference_range_min", -MathF.PI) : 0f;
    public void SetReferenceRangeMax(int index, float v) { if (index >= 0 && index < SettingCount) Settings[index].Extra["reference_range_max"] = v; }
    public float GetReferenceRangeMax(int index) => index >= 0 && index < SettingCount ? RangeOf(Settings[index], "reference_range_max", MathF.PI) : 0f;

    private static float Read(Transform3D t, TransformMode mode, int axis)
    {
        Vector3 v = mode switch { TransformMode.Position => t.Origin, TransformMode.Scale => t.Basis.GetScale(), _ => t.Basis.GetEuler() };
        return axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;
    }

    protected override Transform3D Constrain(Skeleton3D skel, Setting s, int applyBone, Transform3D current, Transform3D reference)
    {
        float refMin = RangeOf(s, "reference_range_min", -MathF.PI), refMax = RangeOf(s, "reference_range_max", MathF.PI);
        float appMin = RangeOf(s, "apply_range_min", -MathF.PI), appMax = RangeOf(s, "apply_range_max", MathF.PI);
        float value = Read(reference, ModeOf(s, "reference_transform_mode"), AxisOf(s, "reference_axis"));
        float t = Math.Abs(refMax - refMin) < 1e-9f ? 0f : Math.Clamp((value - refMin) / (refMax - refMin), 0f, 1f);
        float mapped = appMin + (appMax - appMin) * t;
        int axis = AxisOf(s, "apply_axis");
        Transform3D result = current;
        switch (ModeOf(s, "apply_transform_mode"))
        {
            case TransformMode.Position:
                result.Origin = new Vector3(axis == 0 ? mapped : current.Origin.X, axis == 1 ? mapped : current.Origin.Y, axis == 2 ? mapped : current.Origin.Z);
                break;
            case TransformMode.Scale:
            {
                Vector3 sc = current.Basis.GetScale();
                sc = new Vector3(axis == 0 ? mapped : sc.X, axis == 1 ? mapped : sc.Y, axis == 2 ? mapped : sc.Z);
                result.Basis = current.Basis.Orthonormalized() * Basis.FromScale(sc);
                break;
            }
            default:
            {
                Vector3 e = current.Basis.GetEuler();
                e = new Vector3(axis == 0 ? mapped : e.X, axis == 1 ? mapped : e.Y, axis == 2 ? mapped : e.Z);
                result.Basis = Basis.FromEuler(e) * Basis.FromScale(current.Basis.GetScale());
                break;
            }
        }
        return result;
    }
}
