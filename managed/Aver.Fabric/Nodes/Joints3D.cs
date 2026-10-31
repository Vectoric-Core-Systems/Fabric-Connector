// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Godot's joint nodes: Joint3D and PinJoint3D, HingeJoint3D, SliderJoint3D, ConeTwistJoint3D, Generic6DOFJoint3D. A node keeps Godot's parameters under
// Godot's names (a scene sets "angular_limit/upper" as well as a script's SetParam) and, once both bodies it joins exist, builds the Aver joint
// through GodotJoints, which holds the table of what maps exactly and what does not.
//
// FRAME. The joint's own transform is the frame: the anchor is its origin, a hinge turns about its Z, a slider moves along its X, a cone twist
// twists about its X, a six-DOF's axes are its X, Y and Z. Aver measures a limit from the pose the two bodies have when the joint is built, Godot
// from the joint's frame, so a joint authored with its bodies off the limit centre (a hinge already open 30 degrees) has its range shifted by that
// much; the notes say so. Godot's softness, bias and relaxation parameters have no Jolt counterpart and are stored, as in Godot's own Jolt module.

using System.Globalization;
using PhysBody = Aver.Physics.Body;
using PhysFloat3 = Aver.Physics.Float3;

namespace Aver.Fabric;

/// <summary>Base of the joint nodes (Godot's Joint3D): two bodies named by path, a solver priority and whether the pair may touch.</summary>
public abstract class Joint3D : Node3D
{
    private Aver.Physics.Joint _joint;
    private int _builtA = -1, _builtB = -1;
    private NodePath _nodeA = new(), _nodeB = new();
    private bool _exclude = true;
    private readonly List<string> _notes = new();

    /// <summary>The first body, a path from this node; empty for the world.</summary>
    public NodePath NodeA { get => _nodeA; set { _nodeA = value ?? new NodePath(); Invalidate(); } }

    /// <summary>The second body, a path from this node; empty for the world.</summary>
    public NodePath NodeB { get => _nodeB; set { _nodeB = value ?? new NodePath(); Invalidate(); } }

    /// <summary>Kept for scenes: the solver's own order for joints is the engine's.</summary>
    public int SolverPriority { get; set; } = 1;

    /// <summary>Stops the two bodies colliding with each other while joined.</summary>
    public bool ExcludeNodesFromCollision
    {
        get => _exclude;
        set { _exclude = value; if (_joint.IsValid) _joint.ExcludeCollision = value; }
    }

    /// <summary>The joint as a physics id (0 until it is built).</summary>
    public Rid GetRid() => new(_joint.IsValid ? (ulong)_joint.Handle : 0UL);

    /// <summary>True once the Aver joint exists.</summary>
    internal bool IsBuilt => _joint.IsValid;

    /// <summary>What was set but could not be carried over, from the last build.</summary>
    internal IReadOnlyList<string> Notes => _notes;

    internal Aver.Physics.Joint Joint => _joint;

    internal override bool UsesInternalPhysicsProcess => true;

    internal override void OnReadying()
    {
        base.OnReadying();
        if (Self.IsValid && NodeHost.Current.IsEngine) TryBuild();
    }

    internal override void OnExitingTree()
    {
        base.OnExitingTree();
        DestroyJoint();
    }

    // Bodies are made when their nodes become ready, in tree order, and again when a shape changes: build when both are there and follow them.
    internal override void InternalPhysicsProcess(double delta)
    {
        if (!NodeHost.Current.IsEngine) return;
        Resolve(out PhysBody a, out PhysBody b, out bool any);
        if (!any) { DestroyJoint(); return; }
        if (_joint.IsValid && a.Handle == _builtA && b.Handle == _builtB) return;
        TryBuild();
    }

    /// <summary>Makes the joint again from the current parameters, at the end of the pass.</summary>
    protected void Invalidate()
    {
        if (!_joint.IsValid) return;
        Aver.Framework.Deferred.Call(() => { if (IsAlive && IsInsideTree()) { DestroyJoint(); TryBuild(); } });
    }

    private void Resolve(out PhysBody a, out PhysBody b, out bool any)
    {
        a = PhysBody.None; b = PhysBody.None;
        CollisionObject3D? na = _nodeA.IsEmpty ? null : GetNodeOrNull(_nodeA) as CollisionObject3D;
        CollisionObject3D? nb = _nodeB.IsEmpty ? null : GetNodeOrNull(_nodeB) as CollisionObject3D;
        // Godot lets node_a be empty and node_b set: the lone body is the first and the other end is the world.
        if (na == null && nb != null) { na = nb; nb = null; }
        if (na != null) a = na.Body;
        if (nb != null) b = nb.Body;
        bool wantsA = !_nodeA.IsEmpty, wantsB = !_nodeB.IsEmpty;
        any = (na != null && a.IsValid) && (nb == null ? !(wantsA && wantsB) : b.IsValid);
    }

    private void TryBuild()
    {
        try
        {
            Resolve(out PhysBody a, out PhysBody b, out bool any);
            if (!any) return;
            DestroyJoint();
            _notes.Clear();
            GodotJointResult? r = Create(a, b, GlobalTransform);
            if (r == null || !r.Created) return;
            _joint = r.Joint;
            _builtA = a.Handle; _builtB = b.Handle;
            _notes.AddRange(r.Notes);
            _joint.ExcludeCollision = _exclude;
            foreach (string n in _notes) Aver.Scripting.Log.Warn($"[Fabric] {GetClass()} '{Name}': {n}");
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    private void DestroyJoint()
    {
        if (!_joint.IsValid) return;
        try { _joint.Remove(); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        _joint = default;
        _builtA = _builtB = -1;
    }

    /// <summary>Builds the Aver joint for these two bodies (either may be the world) at the node's pose. Null when the parameters make none.</summary>
    internal abstract GodotJointResult? Create(PhysBody a, PhysBody b, Transform3D global);

    // ---- helpers for the subclasses ----

    internal static PhysFloat3 Anchor(Transform3D g) => FabricPhysics.ToPhysPosition(g.Origin);
    internal static PhysFloat3 Axis(Transform3D g, Vector3 local) => CollisionObject3D.ToPhysDirection((g.Basis * local).Normalized());

    // Parameters are stored by Godot's property name; a subclass lists them with their defaults.
    private readonly Dictionary<string, float> _values = new(StringComparer.Ordinal);

    internal abstract IReadOnlyDictionary<string, float> Defaults { get; }

    protected float Value(string name) => _values.TryGetValue(name, out float v) ? v : Defaults[name];

    protected void SetValue(string name, float v)
    {
        if (!Defaults.ContainsKey(name)) return;
        _values[name] = v;
        Invalidate();
    }

    internal override bool TryDynamicSet(string name, Variant value)
    {
        if (!Defaults.ContainsKey(name)) return false;
        SetValue(name, value.VariantType == Variant.Type.Bool ? (value.AsBool() ? 1f : 0f) : value.AsSingle());
        return true;
    }

    internal override bool TryDynamicGet(string name, out Variant value)
    {
        if (!Defaults.ContainsKey(name)) { value = default; return false; }
        float v = Value(name);
        value = name.EndsWith("/enable", StringComparison.Ordinal) || name.EndsWith("/enabled", StringComparison.Ordinal) ? Variant.From(v != 0f) : Variant.From(v);
        return true;
    }

    protected static string Key(params string[] parts) => string.Join('/', parts);

    protected static Dictionary<string, float> Table(params (string Name, float Value)[] rows)
    {
        var d = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach ((string n, float v) in rows) d[n] = v;
        return d;
    }

    protected float? Stated(string name) => Value(name) != Defaults[name] ? Value(name) : null;
}

/// <summary>A point two bodies share (Godot's PinJoint3D, a ball-and-socket).</summary>
public class PinJoint3D : Joint3D
{
    public enum Param { Bias = 0, Damping = 1, ImpulseClamp = 2 }
    private static readonly string[] s_names = { "params/bias", "params/damping", "params/impulse_clamp" };
    private static readonly IReadOnlyDictionary<string, float> s_defaults = Table(("params/bias", 0.3f), ("params/damping", 1f), ("params/impulse_clamp", 0f));

    internal override IReadOnlyDictionary<string, float> Defaults => s_defaults;
    public void SetParam(Param param, float value) => SetValue(s_names[(int)param], value);
    public float GetParam(Param param) => Value(s_names[(int)param]);

    internal override GodotJointResult? Create(PhysBody a, PhysBody b, Transform3D g) =>
        GodotJoints.Pin(a, b, Anchor(g), new GodotPinParams { Bias = Stated(s_names[0]), Damping = Stated(s_names[1]), ImpulseClamp = Stated(s_names[2]) });
}

/// <summary>A hinge about the node's Z axis (Godot's HingeJoint3D).</summary>
public class HingeJoint3D : Joint3D
{
    public enum Param { Bias = 0, LimitUpper = 1, LimitLower = 2, LimitBias = 3, LimitSoftness = 4, LimitRelaxation = 5, MotorTargetVelocity = 6, MotorMaxImpulse = 7, Max = 8 }
    public enum Flag { UseLimit = 0, EnableMotor = 1, Max = 2 }

    private static readonly string[] s_params =
    {
        "params/bias", "angular_limit/upper", "angular_limit/lower", "angular_limit/bias", "angular_limit/softness", "angular_limit/relaxation",
        "motor/target_velocity", "motor/max_impulse",
    };
    private static readonly string[] s_flags = { "angular_limit/enable", "motor/enable" };
    private static readonly IReadOnlyDictionary<string, float> s_defaults = Table(
        ("params/bias", 0.3f), ("angular_limit/upper", MathF.PI * 0.5f), ("angular_limit/lower", -MathF.PI * 0.5f), ("angular_limit/bias", 0.3f),
        ("angular_limit/softness", 0.9f), ("angular_limit/relaxation", 1f), ("motor/target_velocity", 1f), ("motor/max_impulse", 1f),
        ("angular_limit/enable", 0f), ("motor/enable", 0f));

    internal override IReadOnlyDictionary<string, float> Defaults => s_defaults;
    public void SetParam(Param param, float value) { if (param < Param.Max) SetValue(s_params[(int)param], value); }
    public float GetParam(Param param) => param < Param.Max ? Value(s_params[(int)param]) : 0f;
    public void SetFlag(Flag flag, bool enabled) { if (flag < Flag.Max) SetValue(s_flags[(int)flag], enabled ? 1f : 0f); }
    public bool GetFlag(Flag flag) => flag < Flag.Max && Value(s_flags[(int)flag]) != 0f;

    internal override GodotJointResult? Create(PhysBody a, PhysBody b, Transform3D g) =>
        GodotJoints.Hinge(a, b, Anchor(g), Axis(g, Vector3.Back), new GodotHingeParams
        {
            UseLimit = GetFlag(Flag.UseLimit), LimitUpper = GetParam(Param.LimitUpper), LimitLower = GetParam(Param.LimitLower),
            EnableMotor = GetFlag(Flag.EnableMotor), MotorTargetVelocity = GetParam(Param.MotorTargetVelocity), MotorMaxImpulse = GetParam(Param.MotorMaxImpulse),
            LimitBias = Stated(s_params[3]), LimitSoftness = Stated(s_params[4]), LimitRelaxation = Stated(s_params[5]), Bias = Stated(s_params[0]),
        }, (float)Engine.PhysicsTicksPerSecond);
}

/// <summary>A slider along the node's X axis (Godot's SliderJoint3D).</summary>
public class SliderJoint3D : Joint3D
{
    public enum Param
    {
        LinearLimitUpper = 0, LinearLimitLower = 1, LinearLimitSoftness = 2, LinearLimitRestitution = 3, LinearLimitDamping = 4,
        LinearMotionSoftness = 5, LinearMotionRestitution = 6, LinearMotionDamping = 7,
        LinearOrthogonalSoftness = 8, LinearOrthogonalRestitution = 9, LinearOrthogonalDamping = 10,
        AngularLimitUpper = 11, AngularLimitLower = 12, AngularLimitSoftness = 13, AngularLimitRestitution = 14, AngularLimitDamping = 15,
        AngularMotionSoftness = 16, AngularMotionRestitution = 17, AngularMotionDamping = 18,
        AngularOrthogonalSoftness = 19, AngularOrthogonalRestitution = 20, AngularOrthogonalDamping = 21, Max = 22,
    }

    private static readonly string[] s_params =
    {
        "linear_limit/upper_distance", "linear_limit/lower_distance", "linear_limit/softness", "linear_limit/restitution", "linear_limit/damping",
        "linear_motion/softness", "linear_motion/restitution", "linear_motion/damping",
        "linear_ortho/softness", "linear_ortho/restitution", "linear_ortho/damping",
        "angular_limit/upper_angle", "angular_limit/lower_angle", "angular_limit/softness", "angular_limit/restitution", "angular_limit/damping",
        "angular_motion/softness", "angular_motion/restitution", "angular_motion/damping",
        "angular_ortho/softness", "angular_ortho/restitution", "angular_ortho/damping",
    };

    // Godot's defaults, in the order of the table above.
    private static readonly float[] s_values =
    {
        1f, -1f, 1f, 0.7f, 1f,
        1f, 0.7f, 0f,
        1f, 0.7f, 1f,
        0f, 0f, 1f, 0.7f, 0f,
        1f, 0.7f, 0f,
        1f, 0.7f, 1f,
    };

    private static readonly IReadOnlyDictionary<string, float> s_defaults = BuildDefaults();

    private static IReadOnlyDictionary<string, float> BuildDefaults()
    {
        var d = new Dictionary<string, float>(StringComparer.Ordinal);
        for (int i = 0; i < s_params.Length; i++) d[s_params[i]] = s_values[i];
        return d;
    }

    internal override IReadOnlyDictionary<string, float> Defaults => s_defaults;
    public void SetParam(Param param, float value) { if (param < Param.Max) SetValue(s_params[(int)param], value); }
    public float GetParam(Param param) => param < Param.Max ? Value(s_params[(int)param]) : 0f;

    internal override GodotJointResult? Create(PhysBody a, PhysBody b, Transform3D g) =>
        GodotJoints.Slider(a, b, Anchor(g), Axis(g, Vector3.Right), new GodotSliderParams
        {
            LinearLimitUpper = GetParam(Param.LinearLimitUpper), LinearLimitLower = GetParam(Param.LinearLimitLower),
            LinearLimitSoftness = Stated(s_params[2]), LinearLimitRestitution = Stated(s_params[3]), LinearLimitDamping = Stated(s_params[4]),
            AngularLimitUpper = Stated(s_params[11]), AngularLimitLower = Stated(s_params[12]),
        });
}

/// <summary>A cone and a twist about the node's X axis (Godot's ConeTwistJoint3D).</summary>
public class ConeTwistJoint3D : Joint3D
{
    public enum Param { SwingSpan = 0, TwistSpan = 1, Bias = 2, Softness = 3, Relaxation = 4, Max = 5 }
    private static readonly string[] s_params = { "swing_span", "twist_span", "bias", "softness", "relaxation" };
    private static readonly IReadOnlyDictionary<string, float> s_defaults = Table(
        ("swing_span", MathF.PI * 0.25f), ("twist_span", MathF.PI), ("bias", 0.3f), ("softness", 0.8f), ("relaxation", 1f));

    internal override IReadOnlyDictionary<string, float> Defaults => s_defaults;
    public void SetParam(Param param, float value) { if (param < Param.Max) SetValue(s_params[(int)param], value); }
    public float GetParam(Param param) => param < Param.Max ? Value(s_params[(int)param]) : 0f;

    /// <summary>Godot's spans in this version are half angles on each side of the axis (Godot's own Jolt module reads them that way).</summary>
    internal override GodotJointResult? Create(PhysBody a, PhysBody b, Transform3D g) =>
        GodotJoints.ConeTwist(a, b, Anchor(g), Axis(g, Vector3.Right), new GodotConeTwistParams
        {
            SwingSpan = GetParam(Param.SwingSpan), TwistSpan = GetParam(Param.TwistSpan),
            Bias = Stated(s_params[2]), Softness = Stated(s_params[3]), Relaxation = Stated(s_params[4]),
        });
}

/// <summary>Six axes, each locked, limited, sprung or driven (Godot's Generic6DOFJoint3D). The node's X, Y and Z are the axes.</summary>
public class Generic6DOFJoint3D : Joint3D
{
    public enum Param
    {
        LinearLowerLimit = 0, LinearUpperLimit = 1, LinearLimitSoftness = 2, LinearRestitution = 3, LinearDamping = 4, LinearMotorTargetVelocity = 5, LinearMotorForceLimit = 6,
        LinearSpringStiffness = 7, LinearSpringDamping = 8, LinearSpringEquilibriumPoint = 9,
        AngularLowerLimit = 10, AngularUpperLimit = 11, AngularLimitSoftness = 12, AngularDamping = 13, AngularRestitution = 14, AngularForceLimit = 15, AngularErp = 16,
        AngularMotorTargetVelocity = 17, AngularMotorForceLimit = 18, AngularSpringStiffness = 19, AngularSpringDamping = 20, AngularSpringEquilibriumPoint = 21, Max = 22,
    }

    public enum Flag { EnableLinearLimit = 0, EnableAngularLimit = 1, EnableAngularSpring = 2, EnableLinearSpring = 3, EnableLinearMotor = 4, EnableMotor = 5, Max = 6 }

    // The scene's name of each parameter and flag with the axis letter left out: "linear_limit_{x}/upper_distance".
    private static readonly string[] s_params =
    {
        "linear_limit_{0}/lower_distance", "linear_limit_{0}/upper_distance", "linear_limit_{0}/softness", "linear_limit_{0}/restitution", "linear_limit_{0}/damping",
        "linear_motor_{0}/target_velocity", "linear_motor_{0}/force_limit", "linear_spring_{0}/stiffness", "linear_spring_{0}/damping", "linear_spring_{0}/equilibrium_point",
        "angular_limit_{0}/lower_angle", "angular_limit_{0}/upper_angle", "angular_limit_{0}/softness", "angular_limit_{0}/damping", "angular_limit_{0}/restitution",
        "angular_limit_{0}/force_limit", "angular_limit_{0}/erp", "angular_motor_{0}/target_velocity", "angular_motor_{0}/force_limit",
        "angular_spring_{0}/stiffness", "angular_spring_{0}/damping", "angular_spring_{0}/equilibrium_point",
    };

    private static readonly string[] s_flags =
    {
        "linear_limit_{0}/enabled", "angular_limit_{0}/enabled", "angular_spring_{0}/enabled", "linear_spring_{0}/enabled", "linear_motor_{0}/enabled", "angular_motor_{0}/enabled",
    };

    private static readonly float[] s_paramDefaults =
    {
        0f, 0f, 0.7f, 0.5f, 1f, 0f, 300f, 0f, 0.01f, 0f,
        0f, 0f, 0.5f, 1f, 0f, 0f, 0.5f, 0f, 300f, 0f, 0.01f, 0f,
    };

    private static readonly float[] s_flagDefaults = { 1f, 1f, 0f, 0f, 0f, 0f };

    private static readonly IReadOnlyDictionary<string, float> s_defaults = BuildDefaults();

    private static IReadOnlyDictionary<string, float> BuildDefaults()
    {
        var d = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (char axis in "xyz")
        {
            for (int i = 0; i < s_params.Length; i++) d[string.Format(CultureInfo.InvariantCulture, s_params[i], axis)] = s_paramDefaults[i];
            for (int i = 0; i < s_flags.Length; i++) d[string.Format(CultureInfo.InvariantCulture, s_flags[i], axis)] = s_flagDefaults[i];
        }
        return d;
    }

    internal override IReadOnlyDictionary<string, float> Defaults => s_defaults;

    private static string ParamName(Param p, char axis) => string.Format(CultureInfo.InvariantCulture, s_params[(int)p], axis);
    private static string FlagName(Flag f, char axis) => string.Format(CultureInfo.InvariantCulture, s_flags[(int)f], axis);

    private void SetP(char axis, Param p, float v) { if (p < Param.Max) SetValue(ParamName(p, axis), v); }
    private float GetP(char axis, Param p) => p < Param.Max ? Value(ParamName(p, axis)) : 0f;
    private void SetF(char axis, Flag f, bool on) { if (f < Flag.Max) SetValue(FlagName(f, axis), on ? 1f : 0f); }
    private bool GetF(char axis, Flag f) => f < Flag.Max && Value(FlagName(f, axis)) != 0f;

    public void SetParamX(Param param, float value) => SetP('x', param, value);
    public void SetParamY(Param param, float value) => SetP('y', param, value);
    public void SetParamZ(Param param, float value) => SetP('z', param, value);
    public float GetParamX(Param param) => GetP('x', param);
    public float GetParamY(Param param) => GetP('y', param);
    public float GetParamZ(Param param) => GetP('z', param);
    public void SetFlagX(Flag flag, bool value) => SetF('x', flag, value);
    public void SetFlagY(Flag flag, bool value) => SetF('y', flag, value);
    public void SetFlagZ(Flag flag, bool value) => SetF('z', flag, value);
    public bool GetFlagX(Flag flag) => GetF('x', flag);
    public bool GetFlagY(Flag flag) => GetF('y', flag);
    public bool GetFlagZ(Flag flag) => GetF('z', flag);

    private GodotLinearAxis Linear(char a) => new()
    {
        LimitEnabled = GetF(a, Flag.EnableLinearLimit), LimitLower = GetP(a, Param.LinearLowerLimit), LimitUpper = GetP(a, Param.LinearUpperLimit),
        SpringEnabled = GetF(a, Flag.EnableLinearSpring), SpringStiffness = GetP(a, Param.LinearSpringStiffness), SpringDamping = GetP(a, Param.LinearSpringDamping),
        SpringEquilibriumPoint = GetP(a, Param.LinearSpringEquilibriumPoint),
        MotorEnabled = GetF(a, Flag.EnableLinearMotor), MotorTargetVelocity = GetP(a, Param.LinearMotorTargetVelocity), MotorForceLimit = GetP(a, Param.LinearMotorForceLimit),
        LimitSoftness = Stated(ParamName(Param.LinearLimitSoftness, a)), LimitRestitution = Stated(ParamName(Param.LinearRestitution, a)), LimitDamping = Stated(ParamName(Param.LinearDamping, a)),
    };

    private GodotAngularAxis Angular(char a) => new()
    {
        LimitEnabled = GetF(a, Flag.EnableAngularLimit), LimitLower = GetP(a, Param.AngularLowerLimit), LimitUpper = GetP(a, Param.AngularUpperLimit),
        SpringEnabled = GetF(a, Flag.EnableAngularSpring), SpringStiffness = GetP(a, Param.AngularSpringStiffness), SpringDamping = GetP(a, Param.AngularSpringDamping),
        SpringEquilibriumPoint = GetP(a, Param.AngularSpringEquilibriumPoint),
        MotorEnabled = GetF(a, Flag.EnableMotor), MotorTargetVelocity = GetP(a, Param.AngularMotorTargetVelocity), MotorForceLimit = GetP(a, Param.AngularMotorForceLimit),
        LimitSoftness = Stated(ParamName(Param.AngularLimitSoftness, a)), LimitRestitution = Stated(ParamName(Param.AngularRestitution, a)),
        LimitForceLimit = Stated(ParamName(Param.AngularForceLimit, a)), LimitErp = Stated(ParamName(Param.AngularErp, a)),
    };

    internal override GodotJointResult? Create(PhysBody a, PhysBody b, Transform3D g)
    {
        var p = new GodotGeneric6DofParams
        {
            LinearX = Linear('x'), LinearY = Linear('y'), LinearZ = Linear('z'),
            AngularX = Angular('x'), AngularY = Angular('y'), AngularZ = Angular('z'),
            // The inertia an angular spring acts on: the lighter body's, taken as a unit box of its mass.
            EffectiveInertiaKgM2 = EffectiveInertia(a, b),
        };
        return GodotJoints.Generic6Dof(a, b, Anchor(g), Axis(g, Vector3.Right), Axis(g, Vector3.Up), p);
    }

    private static float? EffectiveInertia(PhysBody a, PhysBody b)
    {
        float m = GodotJoints.ReducedMass(a, b);
        return m > 0f ? m / 6f : null;   // a unit cube of that mass: m * s^2 / 6 about its centre
    }
}
