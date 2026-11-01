// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// WorldEnvironment and the resources it holds (Environment, Sky and its materials, CameraAttributes), under Godot's property names.
// docs/plans/fabric/G4_RENDER.md R3. THE LEVEL IS THE SOURCE OF TRUTH AT LOAD: the converter writes the same values as the level's SKY / FOG
// records and a level-wide post volume, so the node pushes nothing when it enters the tree. A change made after that (a script fading the fog,
// switching the tonemapper) is forwarded: post values to a script post layer, fog and sky light to the sky overrides.

namespace Aver.Fabric;

public class SkyMaterial : Material
{
}

/// <summary>Godot's ProceduralSkyMaterial. The converter turns it into the level's authored dome; these values are what a script reads.</summary>
public class ProceduralSkyMaterial : SkyMaterial
{
    public Color SkyTopColor { get; set; } = new(0.385f, 0.454f, 0.55f, 1f);
    public Color SkyHorizonColor { get; set; } = new(0.6463f, 0.6558f, 0.6708f, 1f);
    public float SkyCurve { get; set; } = 0.15f;
    public float SkyEnergyMultiplier { get; set; } = 1f;
    public Color GroundBottomColor { get; set; } = new(0.2f, 0.169f, 0.133f, 1f);
    public Color GroundHorizonColor { get; set; } = new(0.6463f, 0.6558f, 0.6708f, 1f);
    public float GroundCurve { get; set; } = 0.02f;
    public float GroundEnergyMultiplier { get; set; } = 1f;
    public float SunAngleMax { get; set; } = 30f;
    public float SunCurve { get; set; } = 0.15f;
    public bool UseDebanding { get; set; } = true;
}

public class PhysicalSkyMaterial : SkyMaterial
{
    public float RayleighCoefficient { get; set; } = 2f;
    public Color RayleighColor { get; set; } = new(0.3f, 0.405f, 0.6f, 1f);
    public float MieCoefficient { get; set; } = 0.005f;
    public float MieEccentricity { get; set; } = 0.8f;
    public Color MieColor { get; set; } = new(0.69f, 0.729f, 0.812f, 1f);
    public float Turbidity { get; set; } = 10f;
    public float SunDiskScale { get; set; } = 1f;
    public Color GroundColor { get; set; } = new(0.1f, 0.07f, 0.034f, 1f);
    public float EnergyMultiplier { get; set; } = 1f;
    public bool UseDebanding { get; set; } = true;
}

/// <summary>An equirectangular sky. The renderers do not sample the texture: the converter fits it onto the authored dome.</summary>
public class PanoramaSkyMaterial : SkyMaterial
{
    public Texture2D? Panorama { get; set; }
    public bool Filter { get; set; } = true;
    public float EnergyMultiplier { get; set; } = 1f;
}

public class Sky : Resource
{
    public enum RadianceSizeEnum { Size32, Size64, Size128, Size256, Size512, Size1024, Size2048 }
    public enum ProcessModeEnum { Automatic, Quality, Incremental, Realtime }

    /// <summary>A <see cref="SkyMaterial"/>, or a <see cref="ShaderMaterial"/> over a sky shader (G6 SH3), which replaces the sky.</summary>
    public Material? SkyMaterial { get; set; }
    public RadianceSizeEnum RadianceSize { get; set; } = RadianceSizeEnum.Size256;
    public ProcessModeEnum ProcessMode { get; set; } = ProcessModeEnum.Automatic;
}

/// <summary>Godot's Environment. Every property change emits <c>changed</c>; a live WorldEnvironment forwards it to the engine.</summary>
public class Environment : Resource
{
    public enum BGMode { Clear = 0, Color = 1, Sky = 2, Canvas = 3, Keep = 4, CameraFeed = 5 }
    public enum AmbientSource { Bg = 0, Disabled = 1, Color = 2, Sky = 3 }
    public enum ToneMapper { Linear = 0, Reinhard = 1, Filmic = 2, Aces = 3, Agx = 4 }
    public enum FogModeEnum { Exponential = 0, Depth = 1 }

    private BGMode _bgMode; private Color _bgColor = new(0, 0, 0, 1); private float _bgEnergy = 1f; private Sky? _sky;
    private AmbientSource _ambientSource; private Color _ambientColor = new(0, 0, 0, 1); private float _ambientEnergy = 1f;
    private ToneMapper _tonemap; private float _exposure = 1f, _white = 1f;
    private bool _glow; private float _glowIntensity = 0.8f, _glowThreshold = 1f;
    private bool _fog; private FogModeEnum _fogMode; private Color _fogColor = new(0.518f, 0.553f, 0.608f, 1f);
    private float _fogDensity = 0.01f, _fogHeight, _fogHeightDensity, _fogDepthBegin = 10f, _fogDepthEnd = 100f;
    private bool _volFog; private float _volFogDensity = 0.05f; private Color _volFogAlbedo = new(1, 1, 1, 1);
    private bool _adjust; private float _adjBrightness = 1f, _adjContrast = 1f, _adjSaturation = 1f;

    private void Assign<T>(ref T field, T value) { field = value; EmitChanged(); }

    public BGMode BackgroundMode { get => _bgMode; set => Assign(ref _bgMode, value); }
    public Color BackgroundColor { get => _bgColor; set => Assign(ref _bgColor, value); }
    public float BackgroundEnergyMultiplier { get => _bgEnergy; set => Assign(ref _bgEnergy, value); }
    public Sky? Sky { get => _sky; set => Assign(ref _sky, value); }
    public Vector3 SkyRotation { get; set; }
    public float SkyCustomFov { get; set; }
    public AmbientSource AmbientLightSource { get => _ambientSource; set => Assign(ref _ambientSource, value); }
    public Color AmbientLightColor { get => _ambientColor; set => Assign(ref _ambientColor, value); }
    public float AmbientLightEnergy { get => _ambientEnergy; set => Assign(ref _ambientEnergy, value); }
    public float AmbientLightSkyContribution { get; set; } = 1f;

    public ToneMapper TonemapMode { get => _tonemap; set => Assign(ref _tonemap, value); }
    public float TonemapExposure { get => _exposure; set => Assign(ref _exposure, value); }
    public float TonemapWhite { get => _white; set => Assign(ref _white, value); }

    // Kept for scripts and reported by the converter: Aver has no screen-space or SDF version of these (its ray-traced GI covers them).
    public bool SsrEnabled { get; set; }
    /// <summary>The most steps a screen-space reflection ray takes. Stored, not applied: Aver has no screen-space reflections.</summary>
    public int SsrMaxSteps { get; set; } = 64;
    /// <summary>Stored, not applied (see <see cref="SsrMaxSteps"/>).</summary>
    public float SsrFadeIn { get; set; } = 0.15f;
    /// <summary>Stored, not applied (see <see cref="SsrMaxSteps"/>).</summary>
    public float SsrFadeOut { get; set; } = 2f;
    /// <summary>Stored, not applied (see <see cref="SsrMaxSteps"/>).</summary>
    public float SsrDepthTolerance { get; set; } = 0.2f;
    public bool SsaoEnabled { get; set; }
    public bool SsilEnabled { get; set; }
    public bool SdfgiEnabled { get; set; }

    /// <summary>Godot's Environment.GlowBlendMode: how the glow joins the frame. Godot's default is soft light.</summary>
    public enum GlowBlendModeEnum { Additive = 0, Screen = 1, Softlight = 2, Replace = 3, Mix = 4 }

    /// <summary>Godot's Environment.ReflectionSource (REFLECTION_SOURCE_*).</summary>
    public enum ReflectionSourceEnum { Bg = 0, Disabled = 1, Sky = 2 }

    private GlowBlendModeEnum _glowBlend = GlowBlendModeEnum.Softlight;
    private float _glowStrength = 1f;
    private float? _glowCap;
    private readonly float[] _glowLevels = { 0f, 0f, 1f, 0f, 1f, 0f, 0f };   // Godot's defaults: levels 3 and 5 on

    public bool GlowEnabled { get => _glow; set => Assign(ref _glow, value); }
    public float GlowIntensity { get => _glowIntensity; set => Assign(ref _glowIntensity, value); }
    public float GlowStrength { get => _glowStrength; set => Assign(ref _glowStrength, value); }
    /// <summary>Stored, not applied: Aver's bloom has no separate bloom term.</summary>
    public float GlowBloom { get; set; }
    public float GlowHdrThreshold { get => _glowThreshold; set => Assign(ref _glowThreshold, value); }
    /// <summary>Stored, not applied: Aver's bloom pyramid is summed by the blender, with no per-level multiplier.</summary>
    public float GlowHdrScale { get; set; } = 2f;
    /// <summary>The glow's luminance cap (Godot's default 12). Pushed only once a value has been given, so a level that never stated one keeps Aver's uncapped glow.</summary>
    public float GlowHdrLuminanceCap { get => _glowCap ?? 12f; set { _glowCap = value; EmitChanged(); } }
    internal bool GlowCapStated => _glowCap.HasValue;
    public GlowBlendModeEnum GlowBlendMode { get => _glowBlend; set => Assign(ref _glowBlend, value); }
    /// <summary>Stored, not applied (see <see cref="GlowHdrScale"/>).</summary>
    public bool GlowNormalized { get; set; }
    /// <summary>Stored, not applied: the glow's seven levels are not weighted separately.</summary>
    public float GetGlowLevel(int idx) => idx is >= 0 and < 7 ? _glowLevels[idx] : 0f;
    public void SetGlowLevel(int idx, float level) { if (idx is >= 0 and < 7) _glowLevels[idx] = level; }
    /// <summary>Stored, not applied: Aver's reflections come from ray tracing, the voxel trace and the sky.</summary>
    public ReflectionSourceEnum ReflectedLightSource { get; set; }

    public bool FogEnabled { get => _fog; set => Assign(ref _fog, value); }
    public FogModeEnum FogMode { get => _fogMode; set => Assign(ref _fogMode, value); }
    public Color FogLightColor { get => _fogColor; set => Assign(ref _fogColor, value); }
    public float FogLightEnergy { get; set; } = 1f;
    public float FogDensity { get => _fogDensity; set => Assign(ref _fogDensity, value); }
    public float FogAerialPerspective { get; set; }
    public float FogSkyAffect { get; set; } = 1f;
    public float FogHeight { get => _fogHeight; set => Assign(ref _fogHeight, value); }
    public float FogHeightDensity { get => _fogHeightDensity; set => Assign(ref _fogHeightDensity, value); }
    public float FogDepthBegin { get => _fogDepthBegin; set => Assign(ref _fogDepthBegin, value); }
    public float FogDepthEnd { get => _fogDepthEnd; set => Assign(ref _fogDepthEnd, value); }
    public float FogDepthCurve { get; set; } = 1f;
    /// <summary>Stored, not applied: the sun's in-scatter share is not a separate setting in Aver's fog.</summary>
    public float FogSunScatter { get; set; }
    public bool VolumetricFogEnabled { get => _volFog; set => Assign(ref _volFog, value); }
    public float VolumetricFogDensity { get => _volFogDensity; set => Assign(ref _volFogDensity, value); }
    public Color VolumetricFogAlbedo { get => _volFogAlbedo; set => Assign(ref _volFogAlbedo, value); }
    /// <summary>Stored: the project key RENDER.VOLFOGLENGTH carries it (metres here; Godot's default is 64).</summary>
    public float VolumetricFogLength { get; set; } = 64f;
    /// <summary>Stored: RENDER.VOLFOGANISO carries it.</summary>
    public float VolumetricFogAnisotropy { get; set; } = 0.2f;
    /// <summary>Stored: RENDER.VOLFOGTEMPORAL carries it (off is a weight of 0).</summary>
    public bool VolumetricFogTemporalReprojectionEnabled { get; set; } = true;
    public float VolumetricFogTemporalReprojectionAmount { get; set; } = 0.9f;
    /// <summary>Stored, not applied: the volume's local-light in-scatter, emission and ambient terms are not modelled.</summary>
    public Color VolumetricFogEmission { get; set; } = new(0, 0, 0, 1);
    public float VolumetricFogEmissionEnergy { get; set; } = 1f;
    public float VolumetricFogGiInject { get; set; } = 1f;
    public float VolumetricFogAmbientInject { get; set; }
    public float VolumetricFogDetailSpread { get; set; } = 2f;
    public float VolumetricFogSkyAffect { get; set; } = 1f;

    public bool AdjustmentEnabled { get => _adjust; set => Assign(ref _adjust, value); }
    public float AdjustmentBrightness { get => _adjBrightness; set => Assign(ref _adjBrightness, value); }
    public float AdjustmentContrast { get => _adjContrast; set => Assign(ref _adjContrast, value); }
    public float AdjustmentSaturation { get => _adjSaturation; set => Assign(ref _adjSaturation, value); }
    public Resource? AdjustmentColorCorrection { get; set; }
}

public class CameraAttributes : Resource
{
    private float _multiplier = 1f; private bool _auto;

    public float ExposureSensitivity { get; set; } = 100f;
    public float ExposureMultiplier { get => _multiplier; set { _multiplier = value; EmitChanged(); } }

    /// <summary>The factor lights are scaled by when physical light units are on (Godot's exposure normalization); 1 for the base class.</summary>
    public virtual float GetExposureNormalization() => 1f;
    public bool AutoExposureEnabled { get => _auto; set { _auto = value; EmitChanged(); } }
    public float AutoExposureScale { get; set; } = 0.4f;
    public float AutoExposureSpeed { get; set; } = 0.5f;
}

public class CameraAttributesPractical : CameraAttributes
{
    private bool _near, _far; private float _nearDist = 2f, _farDist = 10f, _amount = 0.1f;

    public bool DofBlurFarEnabled { get => _far; set { _far = value; EmitChanged(); } }
    public float DofBlurFarDistance { get => _farDist; set { _farDist = value; EmitChanged(); } }
    public float DofBlurFarTransition { get; set; } = 5f;
    public bool DofBlurNearEnabled { get => _near; set { _near = value; EmitChanged(); } }
    public float DofBlurNearDistance { get => _nearDist; set { _nearDist = value; EmitChanged(); } }
    public float DofBlurNearTransition { get; set; } = 1f;
    public float DofBlurAmount { get => _amount; set { _amount = value; EmitChanged(); } }
    public float AutoExposureMinSensitivity { get; set; } = 0.0f;
    public float AutoExposureMaxSensitivity { get; set; } = 800.0f;

    /// <summary>The sensitivity over 3072007 (the physical camera's value at ISO 100).</summary>
    public override float GetExposureNormalization() => RenderMapping.PracticalExposureNormalization(ExposureSensitivity);
}

public class CameraAttributesPhysical : CameraAttributes
{
    private float _focus = 10f, _focal = 35f, _aperture = 16f;

    public float FrustumFocusDistance { get => _focus; set { _focus = value; EmitChanged(); } }
    public float FrustumFocalLength { get => _focal; set { _focal = value; EmitChanged(); } }
    public float FrustumNear { get; set; } = 0.05f;
    public float FrustumFar { get; set; } = 4000f;
    public float ExposureAperture { get => _aperture; set { _aperture = value; EmitChanged(); } }
    public float ExposureShutterSpeed { get; set; } = 100f;
    public float AutoExposureMinExposureValue { get; set; } = -8f;
    public float AutoExposureMaxExposureValue { get; set; } = 10f;

    /// <summary>1 / (1.2 x aperture^2 x shutter speed x (100 / ISO)): Godot's exposure normalization for a physical camera.</summary>
    public override float GetExposureNormalization() => RenderMapping.PhysicalExposureNormalization(_aperture, ExposureShutterSpeed, ExposureSensitivity);
}

/// <summary>The mapping from Godot's environment to the engine's post volume keys, fog and sky light. Pure, so it is testable; the converter
/// (tools/fabric, <c>aver/formats/GodotEnvironment.hpp</c>) writes the same numbers into the level.</summary>
public static class EnvironmentMapping
{
    /// <summary>Aver's tonemap mode for Godot's tonemap_mode: 3 + mode.</summary>
    public static int TonemapModeOf(Environment.ToneMapper mode) => 3 + Math.Clamp((int)mode, 0, 4);

    /// <summary>The fog density per centimetre for a density per metre.</summary>
    public static float FogDensityPerCm(float perMetre) => Math.Max(perMetre, 0f) / 100f;

    /// <summary>Godot's tonemap_exposure times the camera attributes' exposure multiplier.</summary>
    public static float Exposure(float tonemapExposure, float cameraMultiplier) => Math.Max(tonemapExposure, 0f) * Math.Max(cameraMultiplier, 0f);

    /// <summary>The POSTVOLUME key values this environment states, in the order the converter writes them.</summary>
    public static List<(string Key, float Value)> PostValues(Environment env, CameraAttributes? camera)
    {
        var v = new List<(string, float)>
        {
            ("tonemap", TonemapModeOf(env.TonemapMode)),
            ("tonemapwhite", Math.Max(env.TonemapWhite, 1e-3f)),
            ("exposure", Exposure(env.TonemapExposure, camera?.ExposureMultiplier ?? 1f)),
            ("autoexposure", camera != null && camera.AutoExposureEnabled ? 1f : 0f),
            ("bloom", env.GlowEnabled ? env.GlowIntensity : 0f),
        };
        if (env.GlowEnabled && env.GlowHdrThreshold != 1f) v.Add(("bloomthreshold", env.GlowHdrThreshold));
        if (env.GlowEnabled)
        {
            // The glow's blend (Godot's default is soft light), strength and cap ride the extended composite; Godot's default strength 1 is Aver's, and a cap is stated or absent.
            v.Add(("glowblend", RenderMapping.GlowBlend((int)env.GlowBlendMode)));
            if (env.GlowStrength != 1f) v.Add(("glowstrength", env.GlowStrength));
            if (env.GlowCapStated) v.Add(("glowcap", env.GlowHdrLuminanceCap));
        }
        v.Add(("adjbrightness", env.AdjustmentEnabled ? env.AdjustmentBrightness : 1f));
        v.Add(("adjcontrast", env.AdjustmentEnabled ? env.AdjustmentContrast : 1f));
        v.Add(("adjsaturation", env.AdjustmentEnabled ? env.AdjustmentSaturation : 1f));
        bool dof = false; float focusCm = 1000f, stop = 4f, mm = 35f;
        switch (camera)
        {
            case CameraAttributesPractical p when p.DofBlurFarEnabled || p.DofBlurNearEnabled:
                dof = true;
                focusCm = Math.Max(p.DofBlurFarEnabled ? p.DofBlurFarDistance : p.DofBlurNearDistance, 0.1f) * 100f;
                stop = Math.Clamp(0.4f / Math.Max(p.DofBlurAmount, 1e-3f), 0.7f, 32f);
                break;
            case CameraAttributesPhysical ph:
                dof = true;
                focusCm = Math.Max(ph.FrustumFocusDistance, 0.1f) * 100f;
                stop = Math.Clamp(ph.ExposureAperture, 0.7f, 32f);
                mm = Math.Clamp(ph.FrustumFocalLength, 10f, 300f);
                break;
        }
        v.Add(("dof", dof ? 1f : 0f));
        if (dof) { v.Add(("doffocus", focusCm)); v.Add(("dofstop", stop)); v.Add(("dofmm", mm)); }
        return v;
    }

    /// <summary>The fog density per centimetre the environment states (0 when fog is off); volumetric fog counts as distance fog.</summary>
    public static float FogDensity(Environment env)
    {
        if (env.FogEnabled)
        {
            if (env.FogMode == Environment.FogModeEnum.Depth)
                return 3f / (Math.Max(env.FogDepthEnd - env.FogDepthBegin, 0.1f) * 100f);
            return FogDensityPerCm(env.FogDensity);
        }
        return env.VolumetricFogEnabled ? FogDensityPerCm(env.VolumetricFogDensity) : 0f;
    }

    /// <summary>True when the level's sky is Aver's physical sky (a Sky background with no authored colours): the only model the ambient override works under. A flat colour,
    /// a procedural or panorama sky are Aver's two-colour dome.</summary>
    public static bool UsesPhysicalSky(Environment env) =>
        env.BackgroundMode == Environment.BGMode.Sky && env.Sky?.SkyMaterial is null or PhysicalSkyMaterial or ShaderMaterial;

    /// <summary>The sky light multiplier: 0 with ambient light disabled, else the ambient energy. With a colour ambient under the physical sky the energy rides the
    /// ambient override and the sky light stays 1.</summary>
    public static float SkyLight(Environment env)
    {
        if (env.AmbientLightSource == Environment.AmbientSource.Disabled) return 0f;
        if (env.AmbientLightSource == Environment.AmbientSource.Color && UsesPhysicalSky(env)) return 1f;
        return env.AmbientLightEnergy;
    }

    /// <summary>The ambient override a colour ambient source states (linear colour, energy, the share of the sky's own ambient: Godot's sky contribution), or null.
    /// Works under the physical sky only; the level's SKY record carries the same three numbers.</summary>
    public static (float R, float G, float B, float Energy, float SkyContribution)? AmbientOverride(Environment env)
    {
        if (env.AmbientLightSource != Environment.AmbientSource.Color) return null;
        Color lin = env.AmbientLightColor.SrgbToLinear();
        return (lin.R, lin.G, lin.B, Math.Max(env.AmbientLightEnergy, 0f), Math.Clamp(env.AmbientLightSkyContribution, 0f, 1f));
    }
}

/// <summary>Godot's WorldEnvironment. It holds the Environment and CameraAttributes; at load the level already carries their values, so nothing
/// is pushed until one of them changes.</summary>
public class WorldEnvironment : Node
{
    /// <summary>The script post layer the node writes to.</summary>
    public const string Layer = "fabric.environment";

    private Environment? _environment;
    private CameraAttributes? _camera;
    private bool _live;

    public Environment? Environment
    {
        get => _environment;
        set { Rewire(_environment, value); _environment = value; Push(); }
    }

    public CameraAttributes? CameraAttributes
    {
        get => _camera;
        set { Rewire(_camera, value); _camera = value; Push(); }
    }

    private void Rewire(Resource? old, Resource? next)
    {
        if (old != null) old.Changed -= Push;
        if (next != null) next.Changed += Push;
    }

    internal override void OnEnteringTree()
    {
        base.OnEnteringTree();
        _live = true;   // what follows is a change, not the load
        RenderEnvironment.Live = true;
        (GetViewport()?.FindWorld3D() ?? SceneTree.Instance.Root.World3D).SetEnvironmentQuiet(_environment, _camera);
        if (_environment != null) PushSkyShader();   // G6 SH3: a converted sky shader is the sky from the moment the environment is in the tree
    }

    internal int PushCount { get; private set; }

    /// <summary>Forwards the current values to the engine: the environment in force (this node's, unless the current camera has its own) and its camera attributes.
    /// Does nothing before the node is in the tree, outside the engine, or with no Environment.</summary>
    internal void Push()
    {
        if (!_live) return;
        if (_environment == null) { SyncWorld(); return; }
        PushCount++;
        PushSkyShader();
        SyncWorld();
        RenderEnvironment.Refresh();
    }

    // The world this node sits in carries its environment and attributes, which a camera's own override (F31).
    private void SyncWorld()
    {
        World3D world = GetViewport()?.FindWorld3D() ?? SceneTree.Instance.Root.World3D;
        world.SetEnvironmentQuiet(_environment, _camera);
        if (_environment == null) RenderEnvironment.Refresh();
    }

    private ShaderMaterial? _skyShader;

    // G6 SH3: a Sky whose material is a converted sky shader is registered with the engine and made the sky; the engine's own sky returns when it is taken away.
    private void PushSkyShader()
    {
        ShaderMaterial? want = _environment?.BackgroundMode == Environment.BGMode.Sky ? _environment.Sky?.SkyMaterial as ShaderMaterial : null;
        if (want != null && want.EnvironmentKind != "sky") want = null;
        if (!ReferenceEquals(want, _skyShader))
        {
            if (_skyShader != null) _skyShader.EnvRegistersChanged -= OnSkyRegisters;
            _skyShader = want;
            if (_skyShader != null) _skyShader.EnvRegistersChanged += OnSkyRegisters;
        }
        if (_skyShader != null) _skyShader.UseAsSky();
        else EnvShaderBackend.Current.UseSky(null);
    }

    private void OnSkyRegisters(ShaderMaterial m) { if (_live) PushSkyShader(); }
}
