// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// GPUParticles3D under Godot's names (docs/plans/fabric/G6_SHADERS.md, SH3).
//
// The converter turns a GPUParticles3D whose process material is a ShaderMaterial over a particles shader into a placement carrying a ParticleEmitter component
// (the .ocparticle it plays, simulated on the GPU with the shader as the custom stage). This node keeps Godot's properties and drives the shader's uniforms: the
// shader's registers are the effect's user parameters reg0, reg1, ... (colour kind, four floats each), so set_shader_parameter reaches the running emitter. A
// ParticleProcessMaterial is not converted; such a node keeps its properties and emits nothing.

using Aver.Framework;

namespace Aver.Fabric;

/// <summary>Where a particle node's shader registers go. The default writes the entity's user parameters; tests install a recording one.</summary>
internal interface IParticleParamBackend
{
    bool SetRegister(Entity entity, int register, float x, float y, float z, float w);
}

internal sealed class NativeParticleParamBackend : IParticleParamBackend
{
    public bool SetRegister(Entity entity, int register, float x, float y, float z, float w)
    {
        try { return entity.SetParticleColor("reg" + register, x, y, z, w); }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }
}

internal static class ParticleParamBackend
{
    internal static IParticleParamBackend Current = new NativeParticleParamBackend();
}

/// <summary>Godot's GPUParticles3D.</summary>
public class GPUParticles3D : GeometryInstance3D
{
    public enum DrawOrderEnum { Index = 0, Lifetime = 1, ReverseLifetime = 2, ViewDepth = 3 }
    public enum TransformAlignEnum { Disabled = 0, ZBillboard = 1, YToVelocity = 2, ZBillboardYToVelocity = 3 }

    private ShaderMaterial? _process;
    private bool _emitting = true;


    /// <summary>Whether the emitter is playing.</summary>
    public bool Emitting { get => _emitting; set { _emitting = value; PushState(); ArmFinish(); } }
    public int Amount { get; set; } = 8;
    public double Lifetime { get; set; } = 1.0;
    public bool OneShot { get; set; }
    public double Preprocess { get; set; }
    public double SpeedScale { get; set; } = 1.0;
    public double Explosiveness { get; set; }
    public double Randomness { get; set; }
    public bool LocalCoords { get; set; }
    public int FixedFps { get; set; }
    public bool Interpolate { get; set; } = true;
    public bool FractDelta { get; set; } = true;
    public DrawOrderEnum DrawOrder { get; set; } = DrawOrderEnum.Index;
    public TransformAlignEnum TransformAlign { get; set; } = TransformAlignEnum.Disabled;
    public Aabb VisibilityAabb { get; set; } = new Aabb(new Vector3(-4, -4, -4), new Vector3(8, 8, 8));
    public Mesh? DrawPass1 { get; set; }
    public Mesh? DrawPass2 { get; set; }
    public Mesh? DrawPass3 { get; set; }
    public Mesh? DrawPass4 { get; set; }
    public int DrawPasses { get; set; } = 1;
    /// <summary>The fraction of <see cref="Amount"/> that is emitted (kept; the converter bakes it into the effect).</summary>
    public double AmountRatio { get; set; } = 1.0;
    public bool TrailEnabled { get; set; }
    public double TrailLifetime { get; set; } = 0.3;
    public float CollisionBaseSize { get; set; } = 0.01f;
    public NodePath SubEmitter { get; set; } = new NodePath("");
    public uint Seed { get; set; }
    public bool UseFixedSeed { get; set; }
    public double InterpToEnd { get; set; }

    /// <summary>A <see cref="ShaderMaterial"/> over a particles shader (G6 SH3) drives the emitter's custom stage and its uniforms. A <see cref="ParticleProcessMaterial"/>
    /// is kept for scripts: the converter wrote its values into the emitter's effect, and a value changed at run time does not reach the running emitter.</summary>
    public Material? ProcessMaterial
    {
        get => _process ?? _otherProcess;
        set
        {
            if (_process != null) _process.EnvRegistersChanged -= OnRegisters;
            _process = value as ShaderMaterial;
            _otherProcess = _process == null ? value : null;
            if (_process is { EnvironmentKind: not "particles" }) { _otherProcess = _process; _process = null; }
            if (_process != null) { _process.EnvRegistersChanged += OnRegisters; PushRegisters(); }
        }
    }
    private Material? _otherProcess;

    internal int RegisterPushes { get; private set; }

    private void OnRegisters(ShaderMaterial m) => PushRegisters();

    // The registers the shader declares become the emitter's user parameters (reg<i>, colour kind).
    private void PushRegisters()
    {
        if (_process is null || !IsInsideTree()) return;
        float[] regs = _process.EnvRegisters;
        for (int r = 0; r * 4 + 3 < regs.Length && r < 16; r++)
            ParticleParamBackend.Current.SetRegister(Self, r, regs[r * 4], regs[r * 4 + 1], regs[r * 4 + 2], regs[r * 4 + 3]);
        RegisterPushes++;
    }

    private void PushState()
    {
        if (!IsInsideTree()) return;
        try { if (_emitting) Self.BurstEmitter(0); else Self.StopEmitter(false); }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { }
    }

    internal override void OnEnteringTree()
    {
        base.OnEnteringTree();
        if (_process != null) { _process.RegisterWithEngine(); PushRegisters(); }
    }

    public new class SignalName : GeometryInstance3D.SignalName
    {
        public static readonly StringName Finished = "finished";
    }

    // A one-shot emission ends when its last particle dies (the emission cycle, then one lifetime): Godot then clears emitting and sends finished.
    private double _finishIn = -1;

    private void ArmFinish() =>
        _finishIn = _emitting && OneShot && IsInsideTree() ? Lifetime * (2.0 - Math.Clamp(Explosiveness, 0.0, 1.0)) / Math.Max(SpeedScale, 1e-6) : -1;

    internal override bool UsesInternalProcess => true;

    internal override void InternalProcess(double delta)
    {
        if (_finishIn < 0) return;
        _finishIn -= delta;
        if (_finishIn > 0) return;
        _finishIn = -1;
        _emitting = false;
        EmitSignal(SignalName.Finished);
    }

    internal override void OnReadying()
    {
        base.OnReadying();
        ArmFinish();
    }

    /// <summary>Restarts the emission (a burst of <see cref="Amount"/> for a one-shot emitter).</summary>
    public void Restart()
    {
        if (!IsInsideTree()) return;
        _emitting = true;
        ArmFinish();
        try { Self.BurstEmitter(OneShot ? Amount : 1); }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { }
    }
}

/// <summary>Godot's ParticleProcessMaterial: the motion, size and colour of a GPUParticles3D's particles. The converter reads it and writes the values into the emitter's
/// .ocparticle (docs/plans/WAVE6_PARTICLES.md); this class holds them for scripts, and a value changed at run time does not reach the running emitter.</summary>
public class ParticleProcessMaterial : Material
{
    public enum EmissionShapeEnum { Point = 0, Sphere = 1, SphereSurface = 2, Box = 3, Points = 4, DirectedPoints = 5, Ring = 6, Max = 7 }
    public enum ParameterEnum
    {
        InitialLinearVelocity = 0, AngularVelocity = 1, OrbitVelocity = 2, LinearAccel = 3, RadialAccel = 4, TangentialAccel = 5, Damping = 6, Angle = 7, Scale = 8,
        HueVariation = 9, AnimSpeed = 10, AnimOffset = 11, TurbVelInfluence = 12, TurbInitDisplacement = 13, TurbInfluenceOverLife = 14, Max = 15
    }
    public enum ParticleFlagsEnum { AlignYToVelocity = 0, RotateY = 1, DisableZ = 2, DampingAsFriction = 3, Max = 4 }
    public enum CollisionModeEnum { Disabled = 0, Rigid = 1, HideOnContact = 2, Max = 3 }
    public enum SubEmitterModeEnum { Disabled = 0, Constant = 1, AtEnd = 2, AtCollision = 3, AtStart = 4, Max = 5 }

    private readonly float[] _min = new float[15], _max = new float[15];
    private readonly Texture2D?[] _curve = new Texture2D?[15];
    private readonly bool[] _flags = new bool[4];

    public ParticleProcessMaterial()
    {
        _min[(int)ParameterEnum.Scale] = _max[(int)ParameterEnum.Scale] = 1f;
    }

    public double LifetimeRandomness { get; set; }
    public EmissionShapeEnum EmissionShape { get; set; } = EmissionShapeEnum.Point;
    public Vector3 EmissionShapeOffset { get; set; } = Vector3.Zero;
    public Vector3 EmissionShapeScale { get; set; } = Vector3.One;
    public float EmissionSphereRadius { get; set; } = 1f;
    public Vector3 EmissionBoxExtents { get; set; } = Vector3.One;
    public Texture2D? EmissionPointTexture { get; set; }
    public Texture2D? EmissionNormalTexture { get; set; }
    public Texture2D? EmissionColorTexture { get; set; }
    public int EmissionPointCount { get; set; } = 1;
    public Vector3 EmissionRingAxis { get; set; } = new Vector3(0, 0, 1);
    public float EmissionRingHeight { get; set; } = 1f;
    public float EmissionRingRadius { get; set; } = 1f;
    public float EmissionRingInnerRadius { get; set; }
    public float EmissionRingConeAngle { get; set; } = 90f;
    public Vector3 Direction { get; set; } = new Vector3(1, 0, 0);
    public float Spread { get; set; } = 45f;
    public float Flatness { get; set; }
    public Vector3 Gravity { get; set; } = new Vector3(0, -9.8f, 0);
    public Color Color { get; set; } = new Color(1, 1, 1, 1);
    public Texture2D? ColorRamp { get; set; }
    public Texture2D? ColorInitialRamp { get; set; }
    public Texture2D? AlphaCurve { get; set; }
    public Texture2D? EmissionCurve { get; set; }
    public bool AttractorInteractionEnabled { get; set; } = true;
    public CollisionModeEnum CollisionMode { get; set; } = CollisionModeEnum.Disabled;
    public float CollisionFriction { get; set; }
    public float CollisionBounce { get; set; }
    public bool CollisionUseScale { get; set; }
    public SubEmitterModeEnum SubEmitterMode { get; set; } = SubEmitterModeEnum.Disabled;
    public double SubEmitterFrequency { get; set; }
    public int SubEmitterAmountAtEnd { get; set; }
    public int SubEmitterAmountAtCollision { get; set; }
    public int SubEmitterAmountAtStart { get; set; }
    public bool SubEmitterKeepVelocity { get; set; }
    public bool TurbulenceEnabled { get; set; }
    public float TurbulenceNoiseStrength { get; set; } = 1f;
    public float TurbulenceNoiseScale { get; set; } = 9f;
    public float TurbulenceNoiseSpeedRandom { get; set; }
    public Vector3 TurbulenceNoiseSpeed { get; set; } = Vector3.Zero;
    public float TurbulenceInfluenceMin { get; set; } = 0.1f;
    public float TurbulenceInfluenceMax { get; set; } = 0.1f;
    public float TurbulenceInitialDisplacementMin { get; set; }
    public float TurbulenceInitialDisplacementMax { get; set; }
    public Texture2D? TurbulenceInfluenceOverLife { get; set; }

    public float InitialVelocityMin { get => GetParamMin(ParameterEnum.InitialLinearVelocity); set => SetParamMin(ParameterEnum.InitialLinearVelocity, value); }
    public float InitialVelocityMax { get => GetParamMax(ParameterEnum.InitialLinearVelocity); set => SetParamMax(ParameterEnum.InitialLinearVelocity, value); }
    public float AngularVelocityMin { get => GetParamMin(ParameterEnum.AngularVelocity); set => SetParamMin(ParameterEnum.AngularVelocity, value); }
    public float AngularVelocityMax { get => GetParamMax(ParameterEnum.AngularVelocity); set => SetParamMax(ParameterEnum.AngularVelocity, value); }
    public Texture2D? AngularVelocityCurve { get => GetParamTexture(ParameterEnum.AngularVelocity); set => SetParamTexture(ParameterEnum.AngularVelocity, value); }
    public float OrbitVelocityMin { get => GetParamMin(ParameterEnum.OrbitVelocity); set => SetParamMin(ParameterEnum.OrbitVelocity, value); }
    public float OrbitVelocityMax { get => GetParamMax(ParameterEnum.OrbitVelocity); set => SetParamMax(ParameterEnum.OrbitVelocity, value); }
    public Texture2D? OrbitVelocityCurve { get => GetParamTexture(ParameterEnum.OrbitVelocity); set => SetParamTexture(ParameterEnum.OrbitVelocity, value); }
    public float LinearAccelMin { get => GetParamMin(ParameterEnum.LinearAccel); set => SetParamMin(ParameterEnum.LinearAccel, value); }
    public float LinearAccelMax { get => GetParamMax(ParameterEnum.LinearAccel); set => SetParamMax(ParameterEnum.LinearAccel, value); }
    public Texture2D? LinearAccelCurve { get => GetParamTexture(ParameterEnum.LinearAccel); set => SetParamTexture(ParameterEnum.LinearAccel, value); }
    public float RadialAccelMin { get => GetParamMin(ParameterEnum.RadialAccel); set => SetParamMin(ParameterEnum.RadialAccel, value); }
    public float RadialAccelMax { get => GetParamMax(ParameterEnum.RadialAccel); set => SetParamMax(ParameterEnum.RadialAccel, value); }
    public Texture2D? RadialAccelCurve { get => GetParamTexture(ParameterEnum.RadialAccel); set => SetParamTexture(ParameterEnum.RadialAccel, value); }
    public float TangentialAccelMin { get => GetParamMin(ParameterEnum.TangentialAccel); set => SetParamMin(ParameterEnum.TangentialAccel, value); }
    public float TangentialAccelMax { get => GetParamMax(ParameterEnum.TangentialAccel); set => SetParamMax(ParameterEnum.TangentialAccel, value); }
    public Texture2D? TangentialAccelCurve { get => GetParamTexture(ParameterEnum.TangentialAccel); set => SetParamTexture(ParameterEnum.TangentialAccel, value); }
    public float DampingMin { get => GetParamMin(ParameterEnum.Damping); set => SetParamMin(ParameterEnum.Damping, value); }
    public float DampingMax { get => GetParamMax(ParameterEnum.Damping); set => SetParamMax(ParameterEnum.Damping, value); }
    public Texture2D? DampingCurve { get => GetParamTexture(ParameterEnum.Damping); set => SetParamTexture(ParameterEnum.Damping, value); }
    public float AngleMin { get => GetParamMin(ParameterEnum.Angle); set => SetParamMin(ParameterEnum.Angle, value); }
    public float AngleMax { get => GetParamMax(ParameterEnum.Angle); set => SetParamMax(ParameterEnum.Angle, value); }
    public Texture2D? AngleCurve { get => GetParamTexture(ParameterEnum.Angle); set => SetParamTexture(ParameterEnum.Angle, value); }
    public float ScaleMin { get => GetParamMin(ParameterEnum.Scale); set => SetParamMin(ParameterEnum.Scale, value); }
    public float ScaleMax { get => GetParamMax(ParameterEnum.Scale); set => SetParamMax(ParameterEnum.Scale, value); }
    public Texture2D? ScaleCurve { get => GetParamTexture(ParameterEnum.Scale); set => SetParamTexture(ParameterEnum.Scale, value); }
    public float HueVariationMin { get => GetParamMin(ParameterEnum.HueVariation); set => SetParamMin(ParameterEnum.HueVariation, value); }
    public float HueVariationMax { get => GetParamMax(ParameterEnum.HueVariation); set => SetParamMax(ParameterEnum.HueVariation, value); }
    public Texture2D? HueVariationCurve { get => GetParamTexture(ParameterEnum.HueVariation); set => SetParamTexture(ParameterEnum.HueVariation, value); }
    public float AnimSpeedMin { get => GetParamMin(ParameterEnum.AnimSpeed); set => SetParamMin(ParameterEnum.AnimSpeed, value); }
    public float AnimSpeedMax { get => GetParamMax(ParameterEnum.AnimSpeed); set => SetParamMax(ParameterEnum.AnimSpeed, value); }
    public Texture2D? AnimSpeedCurve { get => GetParamTexture(ParameterEnum.AnimSpeed); set => SetParamTexture(ParameterEnum.AnimSpeed, value); }
    public float AnimOffsetMin { get => GetParamMin(ParameterEnum.AnimOffset); set => SetParamMin(ParameterEnum.AnimOffset, value); }
    public float AnimOffsetMax { get => GetParamMax(ParameterEnum.AnimOffset); set => SetParamMax(ParameterEnum.AnimOffset, value); }
    public Texture2D? AnimOffsetCurve { get => GetParamTexture(ParameterEnum.AnimOffset); set => SetParamTexture(ParameterEnum.AnimOffset, value); }

    public bool ParticleFlagAlignY { get => GetParticleFlag(ParticleFlagsEnum.AlignYToVelocity); set => SetParticleFlag(ParticleFlagsEnum.AlignYToVelocity, value); }
    public bool ParticleFlagRotateY { get => GetParticleFlag(ParticleFlagsEnum.RotateY); set => SetParticleFlag(ParticleFlagsEnum.RotateY, value); }
    public bool ParticleFlagDisableZ { get => GetParticleFlag(ParticleFlagsEnum.DisableZ); set => SetParticleFlag(ParticleFlagsEnum.DisableZ, value); }
    public bool ParticleFlagDampingAsFriction { get => GetParticleFlag(ParticleFlagsEnum.DampingAsFriction); set => SetParticleFlag(ParticleFlagsEnum.DampingAsFriction, value); }

    private static bool InRange(ParameterEnum p) => (int)p >= 0 && (int)p < 15;
    public void SetParamMin(ParameterEnum param, float value) { if (InRange(param)) _min[(int)param] = value; }
    public float GetParamMin(ParameterEnum param) => InRange(param) ? _min[(int)param] : 0f;
    public void SetParamMax(ParameterEnum param, float value) { if (InRange(param)) _max[(int)param] = value; }
    public float GetParamMax(ParameterEnum param) => InRange(param) ? _max[(int)param] : 0f;
    public void SetParamTexture(ParameterEnum param, Texture2D? texture) { if (InRange(param)) _curve[(int)param] = texture; }
    public Texture2D? GetParamTexture(ParameterEnum param) => InRange(param) ? _curve[(int)param] : null;
    public void SetParticleFlag(ParticleFlagsEnum particleFlag, bool enable) { if ((int)particleFlag is >= 0 and < 4) _flags[(int)particleFlag] = enable; }
    public bool GetParticleFlag(ParticleFlagsEnum particleFlag) => (int)particleFlag is >= 0 and < 4 && _flags[(int)particleFlag];
}

/// <summary>Godot's CPUParticles3D under its names. The simulation is the engine's (the converter writes the node's parameters into an .ocparticle and the emitter plays it, on the
/// CPU or the GPU tier by its size); the properties are kept for scripts and a value changed at run time does not reach the running emitter.</summary>
public class CPUParticles3D : GeometryInstance3D
{
    public enum DrawOrderEnum { Index = 0, Lifetime = 1, ViewDepth = 2 }
    public enum EmissionShapeEnum { Point = 0, Sphere = 1, SphereSurface = 2, Box = 3, Points = 4, DirectedPoints = 5, Ring = 6, Max = 7 }
    public enum ParameterEnum { InitialLinearVelocity = 0, AngularVelocity = 1, OrbitVelocity = 2, LinearAccel = 3, RadialAccel = 4, TangentialAccel = 5, Damping = 6, Angle = 7, Scale = 8, HueVariation = 9, AnimSpeed = 10, AnimOffset = 11, Max = 12 }
    public enum ParticleFlagsEnum { AlignYToVelocity = 0, RotateY = 1, DisableZ = 2, Max = 3 }

    private bool _emitting = true;
    private readonly float[] _min = new float[12], _max = new float[12];
    private readonly Curve?[] _curve = new Curve?[12];
    private readonly bool[] _flags = new bool[3];

    public CPUParticles3D()
    {
        _min[(int)ParameterEnum.Scale] = _max[(int)ParameterEnum.Scale] = 1f;
    }

    public bool Emitting { get => _emitting; set { _emitting = value; PushState(); ArmFinish(); } }
    public int Amount { get; set; } = 8;
    public double Lifetime { get; set; } = 1.0;
    public bool OneShot { get; set; }
    public double Preprocess { get; set; }
    public double SpeedScale { get; set; } = 1.0;
    public double Explosiveness { get; set; }
    public double Randomness { get; set; }
    public double LifetimeRandomness { get; set; }
    public bool LocalCoords { get; set; }
    public int FixedFps { get; set; }
    public bool FractDelta { get; set; } = true;
    public DrawOrderEnum DrawOrder { get; set; } = DrawOrderEnum.Index;
    public Mesh? Mesh { get; set; }
    public Aabb VisibilityAabb { get; set; } = new Aabb(new Vector3(-4, -4, -4), new Vector3(8, 8, 8));
    public EmissionShapeEnum EmissionShape { get; set; } = EmissionShapeEnum.Point;
    public float EmissionSphereRadius { get; set; } = 1f;
    public Vector3 EmissionBoxExtents { get; set; } = Vector3.One;
    public Vector3[] EmissionPoints { get; set; } = System.Array.Empty<Vector3>();
    public Vector3[] EmissionNormals { get; set; } = System.Array.Empty<Vector3>();
    public Color[] EmissionColors { get; set; } = System.Array.Empty<Color>();
    public Vector3 EmissionRingAxis { get; set; } = new Vector3(0, 0, 1);
    public float EmissionRingHeight { get; set; } = 1f;
    public float EmissionRingRadius { get; set; } = 1f;
    public float EmissionRingInnerRadius { get; set; }
    public Vector3 Direction { get; set; } = new Vector3(1, 0, 0);
    public float Spread { get; set; } = 45f;
    public float Flatness { get; set; }
    public Vector3 Gravity { get; set; } = new Vector3(0, -9.8f, 0);
    public Color Color { get; set; } = new Color(1, 1, 1, 1);
    public Gradient? ColorRamp { get; set; }
    public Gradient? ColorInitialRamp { get; set; }

    public float InitialVelocityMin { get => GetParamMin(ParameterEnum.InitialLinearVelocity); set => SetParamMin(ParameterEnum.InitialLinearVelocity, value); }
    public float InitialVelocityMax { get => GetParamMax(ParameterEnum.InitialLinearVelocity); set => SetParamMax(ParameterEnum.InitialLinearVelocity, value); }
    public float AngularVelocityMin { get => GetParamMin(ParameterEnum.AngularVelocity); set => SetParamMin(ParameterEnum.AngularVelocity, value); }
    public float AngularVelocityMax { get => GetParamMax(ParameterEnum.AngularVelocity); set => SetParamMax(ParameterEnum.AngularVelocity, value); }
    public Curve? AngularVelocityCurve { get => GetParamCurve(ParameterEnum.AngularVelocity); set => SetParamCurve(ParameterEnum.AngularVelocity, value); }
    public float OrbitVelocityMin { get => GetParamMin(ParameterEnum.OrbitVelocity); set => SetParamMin(ParameterEnum.OrbitVelocity, value); }
    public float OrbitVelocityMax { get => GetParamMax(ParameterEnum.OrbitVelocity); set => SetParamMax(ParameterEnum.OrbitVelocity, value); }
    public Curve? OrbitVelocityCurve { get => GetParamCurve(ParameterEnum.OrbitVelocity); set => SetParamCurve(ParameterEnum.OrbitVelocity, value); }
    public float LinearAccelMin { get => GetParamMin(ParameterEnum.LinearAccel); set => SetParamMin(ParameterEnum.LinearAccel, value); }
    public float LinearAccelMax { get => GetParamMax(ParameterEnum.LinearAccel); set => SetParamMax(ParameterEnum.LinearAccel, value); }
    public Curve? LinearAccelCurve { get => GetParamCurve(ParameterEnum.LinearAccel); set => SetParamCurve(ParameterEnum.LinearAccel, value); }
    public float RadialAccelMin { get => GetParamMin(ParameterEnum.RadialAccel); set => SetParamMin(ParameterEnum.RadialAccel, value); }
    public float RadialAccelMax { get => GetParamMax(ParameterEnum.RadialAccel); set => SetParamMax(ParameterEnum.RadialAccel, value); }
    public Curve? RadialAccelCurve { get => GetParamCurve(ParameterEnum.RadialAccel); set => SetParamCurve(ParameterEnum.RadialAccel, value); }
    public float TangentialAccelMin { get => GetParamMin(ParameterEnum.TangentialAccel); set => SetParamMin(ParameterEnum.TangentialAccel, value); }
    public float TangentialAccelMax { get => GetParamMax(ParameterEnum.TangentialAccel); set => SetParamMax(ParameterEnum.TangentialAccel, value); }
    public Curve? TangentialAccelCurve { get => GetParamCurve(ParameterEnum.TangentialAccel); set => SetParamCurve(ParameterEnum.TangentialAccel, value); }
    public float DampingMin { get => GetParamMin(ParameterEnum.Damping); set => SetParamMin(ParameterEnum.Damping, value); }
    public float DampingMax { get => GetParamMax(ParameterEnum.Damping); set => SetParamMax(ParameterEnum.Damping, value); }
    public Curve? DampingCurve { get => GetParamCurve(ParameterEnum.Damping); set => SetParamCurve(ParameterEnum.Damping, value); }
    public float AngleMin { get => GetParamMin(ParameterEnum.Angle); set => SetParamMin(ParameterEnum.Angle, value); }
    public float AngleMax { get => GetParamMax(ParameterEnum.Angle); set => SetParamMax(ParameterEnum.Angle, value); }
    public Curve? AngleCurve { get => GetParamCurve(ParameterEnum.Angle); set => SetParamCurve(ParameterEnum.Angle, value); }
    public float ScaleAmountMin { get => GetParamMin(ParameterEnum.Scale); set => SetParamMin(ParameterEnum.Scale, value); }
    public float ScaleAmountMax { get => GetParamMax(ParameterEnum.Scale); set => SetParamMax(ParameterEnum.Scale, value); }
    public Curve? ScaleAmountCurve { get => GetParamCurve(ParameterEnum.Scale); set => SetParamCurve(ParameterEnum.Scale, value); }
    public float HueVariationMin { get => GetParamMin(ParameterEnum.HueVariation); set => SetParamMin(ParameterEnum.HueVariation, value); }
    public float HueVariationMax { get => GetParamMax(ParameterEnum.HueVariation); set => SetParamMax(ParameterEnum.HueVariation, value); }
    public Curve? HueVariationCurve { get => GetParamCurve(ParameterEnum.HueVariation); set => SetParamCurve(ParameterEnum.HueVariation, value); }
    public float AnimSpeedMin { get => GetParamMin(ParameterEnum.AnimSpeed); set => SetParamMin(ParameterEnum.AnimSpeed, value); }
    public float AnimSpeedMax { get => GetParamMax(ParameterEnum.AnimSpeed); set => SetParamMax(ParameterEnum.AnimSpeed, value); }
    public Curve? AnimSpeedCurve { get => GetParamCurve(ParameterEnum.AnimSpeed); set => SetParamCurve(ParameterEnum.AnimSpeed, value); }
    public float AnimOffsetMin { get => GetParamMin(ParameterEnum.AnimOffset); set => SetParamMin(ParameterEnum.AnimOffset, value); }
    public float AnimOffsetMax { get => GetParamMax(ParameterEnum.AnimOffset); set => SetParamMax(ParameterEnum.AnimOffset, value); }
    public Curve? AnimOffsetCurve { get => GetParamCurve(ParameterEnum.AnimOffset); set => SetParamCurve(ParameterEnum.AnimOffset, value); }

    public bool ParticleFlagAlignY { get => GetParticleFlag(ParticleFlagsEnum.AlignYToVelocity); set => SetParticleFlag(ParticleFlagsEnum.AlignYToVelocity, value); }
    public bool ParticleFlagRotateY { get => GetParticleFlag(ParticleFlagsEnum.RotateY); set => SetParticleFlag(ParticleFlagsEnum.RotateY, value); }
    public bool ParticleFlagDisableZ { get => GetParticleFlag(ParticleFlagsEnum.DisableZ); set => SetParticleFlag(ParticleFlagsEnum.DisableZ, value); }

    private static bool InRange(ParameterEnum p) => (int)p >= 0 && (int)p < 12;
    public void SetParamMin(ParameterEnum param, float value) { if (InRange(param)) _min[(int)param] = value; }
    public float GetParamMin(ParameterEnum param) => InRange(param) ? _min[(int)param] : 0f;
    public void SetParamMax(ParameterEnum param, float value) { if (InRange(param)) _max[(int)param] = value; }
    public float GetParamMax(ParameterEnum param) => InRange(param) ? _max[(int)param] : 0f;
    public void SetParamCurve(ParameterEnum param, Curve? curve) { if (InRange(param)) _curve[(int)param] = curve; }
    public Curve? GetParamCurve(ParameterEnum param) => InRange(param) ? _curve[(int)param] : null;
    public void SetParticleFlag(ParticleFlagsEnum particleFlag, bool enable) { if ((int)particleFlag is >= 0 and < 3) _flags[(int)particleFlag] = enable; }
    public bool GetParticleFlag(ParticleFlagsEnum particleFlag) => (int)particleFlag is >= 0 and < 3 && _flags[(int)particleFlag];

    private void PushState()
    {
        if (!IsInsideTree()) return;
        try { if (_emitting) Self.BurstEmitter(0); else Self.StopEmitter(false); }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { }
    }

    public new class SignalName : GeometryInstance3D.SignalName
    {
        public static readonly StringName Finished = "finished";
    }

    // A one-shot emission ends when its last particle dies (the emission cycle, then one lifetime): Godot then clears emitting and sends finished.
    private double _finishIn = -1;

    private void ArmFinish() =>
        _finishIn = _emitting && OneShot && IsInsideTree() ? Lifetime * (2.0 - Math.Clamp(Explosiveness, 0.0, 1.0)) / Math.Max(SpeedScale, 1e-6) : -1;

    internal override bool UsesInternalProcess => true;

    internal override void InternalProcess(double delta)
    {
        if (_finishIn < 0) return;
        _finishIn -= delta;
        if (_finishIn > 0) return;
        _finishIn = -1;
        _emitting = false;
        EmitSignal(SignalName.Finished);
    }

    internal override void OnReadying()
    {
        base.OnReadying();
        ArmFinish();
    }

    /// <summary>Restarts the emission (a burst of <see cref="Amount"/> for a one-shot emitter).</summary>
    public void Restart()
    {
        if (!IsInsideTree()) return;
        _emitting = true;
        ArmFinish();
        try { Self.BurstEmitter(OneShot ? Amount : 1); }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { }
    }
}

/// <summary>Godot's GPUParticlesCollision3D (the base of the four colliders). The converter bakes each collider into the effect of the emitters in its scene (docs/plans/WAVE6_PARTICLES.md);
/// moving or resizing it at run time changes nothing.</summary>
public class GPUParticlesCollision3D : VisualInstance3D
{
    public uint CullMask { get; set; } = 0xFFFFFFFFu;
}

public class GPUParticlesCollisionSphere3D : GPUParticlesCollision3D
{
    public float Radius { get; set; } = 1f;
}

public class GPUParticlesCollisionBox3D : GPUParticlesCollision3D
{
    public Vector3 Size { get; set; } = new Vector3(2, 2, 2);
}

public class GPUParticlesCollisionHeightField3D : GPUParticlesCollision3D
{
    public enum ResolutionEnum { Resolution256 = 0, Resolution512 = 1, Resolution1024 = 2, Resolution2048 = 3, Resolution4096 = 4, Resolution8192 = 5, Max = 6 }
    public enum UpdateModeEnum { WhenMoved = 0, Always = 1 }
    public Vector3 Size { get; set; } = new Vector3(2, 2, 2);
    public ResolutionEnum Resolution { get; set; } = ResolutionEnum.Resolution1024;
    public UpdateModeEnum UpdateMode { get; set; } = UpdateModeEnum.WhenMoved;
    public bool FollowCameraEnabled { get; set; }
    public uint HeightfieldMask { get; set; } = 1048575;
}

public class GPUParticlesCollisionSDF3D : GPUParticlesCollision3D
{
    public enum ResolutionEnum { Resolution16 = 0, Resolution32 = 1, Resolution64 = 2, Resolution128 = 3, Resolution256 = 4, Resolution512 = 5, Max = 6 }
    public Vector3 Size { get; set; } = new Vector3(2, 2, 2);
    public ResolutionEnum Resolution { get; set; } = ResolutionEnum.Resolution64;
    public float Thickness { get; set; } = 1f;
    public uint BakeMask { get; set; } = 4294967295;
    public Texture3D? Texture { get; set; }
}

/// <summary>Godot's GPUParticlesAttractor3D (the base of the three attractors), baked into the emitters' effects at convert time like the colliders.</summary>
public class GPUParticlesAttractor3D : VisualInstance3D
{
    public float Strength { get; set; } = 1f;
    public float Attenuation { get; set; } = 1f;
    public float Directionality { get; set; }
    public uint CullMask { get; set; } = 0xFFFFFFFFu;
}

public class GPUParticlesAttractorSphere3D : GPUParticlesAttractor3D
{
    public float Radius { get; set; } = 1f;
}

public class GPUParticlesAttractorBox3D : GPUParticlesAttractor3D
{
    public Vector3 Size { get; set; } = new Vector3(2, 2, 2);
}

public class GPUParticlesAttractorVectorField3D : GPUParticlesAttractor3D
{
    public Vector3 Size { get; set; } = new Vector3(2, 2, 2);
    public Texture3D? Texture { get; set; }
}
