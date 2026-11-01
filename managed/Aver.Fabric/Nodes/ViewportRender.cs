// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Viewport's 3D rendering properties (wave 4, F1): msaa_3d, scaling_3d_mode / scale, fsr_sharpness, use_taa, screen_space_aa, mesh_lod_threshold, use_occlusion_culling,
// use_debanding, the anisotropy and shadow atlas settings. The window's own viewport starts from the project's settings (what a graphics menu reads) and forwards a
// change to the engine's RenderSettings and post layer; a SubViewport is drawn by the engine's cheap view configuration and keeps its values (SubViewport.cs).
// docs/plans/WAVE4_RENDER.md carries the choices (which switches go together).

using Aver.Framework;

namespace Aver.Fabric;

/// <summary>What a window viewport's render properties reach. The engine implementation calls <c>Aver.Framework.RenderSettings</c> and the post layer; a test swaps it.</summary>
internal interface IRenderBackend
{
    bool Set(RenderKey key, float value);
    bool Post(string key, float value);
}

internal sealed class EngineRenderBackend : IRenderBackend
{
    public bool Set(RenderKey key, float value) => Guard(() => RenderSettings.Set(key, value));

    public bool Post(string key, float value) => Guard(() => PostFx.SetValue(RenderBackend.Layer, key, value) && PostFx.SetLayerWeight(RenderBackend.Layer, 1f, 0f));

    private static bool Guard(Func<bool> call)
    {
        if (!NodeHost.Current.IsEngine) return false;
        try { return call(); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }
}

internal static class RenderBackend
{
    /// <summary>The script post layer the window viewport's debanding is written to.</summary>
    public const string Layer = "fabric.viewport";

    public static IRenderBackend Current { get; set; } = new EngineRenderBackend();
}

public partial class Viewport
{
    /// <summary>Godot's Viewport.MSAA.</summary>
    public enum Msaa { Disabled = 0, Msaa2X = 1, Msaa4X = 2, Msaa8X = 3, Max = 4 }

    /// <summary>Godot's Viewport.Scaling3DMode.</summary>
    public enum Scaling3DModeEnum { Bilinear = 0, Fsr = 1, Fsr2 = 2, Max = 3 }

    /// <summary>Godot's Viewport.AnisotropicFiltering.</summary>
    public enum AnisotropicFiltering { Disabled = 0, Anisotropy2X = 1, Anisotropy4X = 2, Anisotropy8X = 3, Anisotropy16X = 4, Max = 5 }

    /// <summary>Godot's Viewport.PositionalShadowAtlasQuadrantSubdiv.</summary>
    public enum PositionalShadowAtlasQuadrantSubdiv { Disabled = 0, Subdiv1 = 1, Subdiv4 = 2, Subdiv16 = 3, Subdiv64 = 4, Subdiv256 = 5, Subdiv1024 = 6, Max = 7 }

    /// <summary>Godot's Viewport.DebugDraw.</summary>
    public enum DebugDrawEnum { Disabled = 0, Unshaded = 1, Lighting = 2, Overdraw = 3, Wireframe = 4, NormalBuffer = 5, VoxelGiAlbedo = 6, VoxelGiLighting = 7, VoxelGiEmission = 8,
        ShadowAtlas = 9, DirectionalShadowAtlas = 10, SceneLuminance = 11, Ssao = 12, Ssil = 13, PssmSplits = 14, DecalAtlas = 15, Sdfgi = 16, SdfgiProbes = 17,
        GiBuffer = 18, DisableLodEnum = 19, ClusterOmniLights = 20, ClusterSpotLights = 21, ClusterDecals = 22, ClusterReflectionProbes = 23, Occluders = 24, MotionVectors = 25,
        InternalBuffer = 26 }

    /// <summary>Godot's Viewport.VRSMode.</summary>
    public enum VrsModeEnum { Disabled = 0, Texture = 1, Xr = 2, Max = 3 }

    /// <summary>Godot's Viewport.VRSUpdateMode.</summary>
    public enum VrsUpdateModeEnum { Disabled = 0, Once = 1, Always = 2, Max = 3 }

    /// <summary>Whether this viewport is the window the player sees: its render properties start from the project's settings and reach the engine.</summary>
    internal virtual bool ForwardsToEngine => this is Window && ReferenceEquals(SceneTree.Instance.Root, this);

    // A value this viewport was given, else the project's setting (window), else Godot's default.
    private readonly Dictionary<string, object> _render = new(StringComparer.Ordinal);

    private T Get<T>(string key, string setting, T fallback, Func<Variant, T> read)
    {
        if (_render.TryGetValue(key, out object? v)) return (T)v;
        if (!ForwardsToEngine) return fallback;
        Variant s = ProjectSettings.GetSetting(setting, default);
        return s.VariantType == Variant.Type.Nil ? fallback : read(s);
    }

    private void Put(string key, object value) => _render[key] = value;

    private static int AsInt(Variant v) => v.AsInt32();
    private static float AsFloat(Variant v) => v.AsSingle();
    private static bool AsBoolean(Variant v) => v.AsBool();

    /// <summary>Anti-aliasing by multisampling. The engine's MSAA is the sample count of its raster pass; it is switched off when TAA is on (Aver's TAA needs one sample).</summary>
    public virtual Msaa Msaa3D
    {
        get => (Msaa)Get("msaa3d", "rendering/anti_aliasing/quality/msaa_3d", 0, AsInt);
        set { Put("msaa3d", (int)value); PushAa(); }
    }

    /// <summary>Stored, not applied: the engine draws no 2D with multisampling.</summary>
    public Msaa Msaa2D
    {
        get => (Msaa)Get("msaa2d", "rendering/anti_aliasing/quality/msaa_2d", 0, AsInt);
        set => Put("msaa2d", (int)value);
    }

    /// <summary>Temporal anti-aliasing. Aver chooses its TAA when the project starts (RENDER.TAA), so a change at run time is stored and switches MSAA off for consistency
    /// but does not start the temporal pass.</summary>
    public bool UseTaa
    {
        get => Get("taa", "rendering/anti_aliasing/quality/use_taa", false, AsBoolean);
        set { Put("taa", value); PushAa(); }
    }

    private void PushAa()
    {
        if (!ForwardsToEngine) return;
        (int msaa, int taa) = RenderMapping.ChooseAa((int)Msaa3D, UseTaa);
        RenderBackend.Current.Set(RenderKey.Msaa, msaa);
        RenderBackend.Current.Set(RenderKey.Taa, taa);
    }

    /// <summary>The post-process anti-aliasing mode. Stored: the project's setting becomes RENDER.NEURAA when it is converted; a change at run time does not switch it.</summary>
    public ScreenSpaceAAEnum ScreenSpaceAA
    {
        get => (ScreenSpaceAAEnum)Get("ssaa", "rendering/anti_aliasing/quality/screen_space_aa", 0, AsInt);
        set => Put("ssaa", (int)value);
    }

    /// <summary>Dithers the final colour before it is quantised. On the window it adds half an 8-bit step of noise (the post key <c>deband</c>).</summary>
    public bool UseDebanding
    {
        get => Get("deband", "rendering/anti_aliasing/quality/use_debanding", false, AsBoolean);
        set { Put("deband", value); if (ForwardsToEngine) RenderBackend.Current.Post("deband", value ? 1f : 0f); }
    }

    /// <summary>Occlusion culling of the window's scene. Applied by the editor's Play; the shipped runtime has none (the value is kept).</summary>
    public bool UseOcclusionCulling
    {
        get => Get("occlusion", "rendering/occlusion_culling/use_occlusion_culling", false, AsBoolean);
        set { Put("occlusion", value); if (ForwardsToEngine) RenderBackend.Current.Set(RenderKey.Occlusion, value ? 1f : 0f); }
    }

    /// <summary>The screen-space error in pixels above which a mesh drops to a coarser level (Godot's default 1; 0 turns the automatic choice off). Applied by the editor's
    /// Play; the shipped runtime has no level-of-detail selection (the value is kept).</summary>
    public float MeshLodThreshold
    {
        get => Get("lodthreshold", "rendering/mesh_lod/lod_change/threshold_pixels", 1f, AsFloat);
        set { Put("lodthreshold", value); if (ForwardsToEngine) RenderBackend.Current.Set(RenderKey.LodThreshold, RenderMapping.LodThresholdPixels(value)); }
    }

    /// <summary>How the 3D scene is scaled to the screen. Every mode is Aver's one upscaler at the scale given; the choice of FSR is kept.</summary>
    public virtual Scaling3DModeEnum Scaling3DMode
    {
        get => (Scaling3DModeEnum)Get("scalemode", "rendering/scaling_3d/mode", 0, AsInt);
        set { Put("scalemode", (int)value); PushScale(); }
    }

    /// <summary>The scale the 3D scene is drawn at before it is brought to the window's size (0.25 to 2).</summary>
    public virtual float Scaling3DScale
    {
        get => Get("scale", "rendering/scaling_3d/scale", 1f, AsFloat);
        set { Put("scale", value); PushScale(); }
    }

    /// <summary>The upscaler's sharpness as Godot spells it (0 sharpest, halving every 1.0). Stored by the engine: the upscaler is chosen when the project starts.</summary>
    public float FsrSharpness
    {
        get => Get("fsr", "rendering/scaling_3d/fsr_sharpness", 0.2f, AsFloat);
        set { Put("fsr", value); if (ForwardsToEngine) RenderBackend.Current.Set(RenderKey.Sharpness, RenderMapping.FsrSharpness(value)); }
    }

    private void PushScale()
    {
        if (!ForwardsToEngine) return;
        RenderBackend.Current.Set(RenderKey.Scale, RenderMapping.Scale3D(Scaling3DScale));
    }

    /// <summary>Stored: the engine's texture sampling is set per material and by RENDER.TEXTUREMIPBIAS when the project starts.</summary>
    public float TextureMipmapBias
    {
        get => Get("mipbias", "rendering/textures/default_filters/texture_mipmap_bias", 0f, AsFloat);
        set => Put("mipbias", value);
    }

    /// <summary>Stored (RENDER.TEXTUREANISO is read when the renderer starts).</summary>
    public AnisotropicFiltering AnisotropicFilteringLevel
    {
        get => (AnisotropicFiltering)Get("aniso", "rendering/textures/default_filters/anisotropic_filtering_level", 2, AsInt);
        set => Put("aniso", (int)value);
    }

    /// <summary>Stored: the local lights' shadow maps are sized by the renderer.</summary>
    public int PositionalShadowAtlasSize
    {
        get => Get("posatlas", "rendering/lights_and_shadows/positional_shadow/atlas_size", 4096, AsInt);
        set => Put("posatlas", value);
    }

    /// <summary>Stored.</summary>
    public bool PositionalShadowAtlas16Bits
    {
        get => Get("posatlas16", "rendering/lights_and_shadows/positional_shadow/atlas_16_bits", true, AsBoolean);
        set => Put("posatlas16", value);
    }

    private static readonly int[] s_quadrantDefaults = { 2, 2, 3, 4 };   // Godot: 4 shadows, 4, 16, 64 (subdiv enum 2, 2, 3, 4)

    /// <summary>Stored: how many shadows a quadrant of the positional shadow atlas holds.</summary>
    public void SetPositionalShadowAtlasQuadrantSubdiv(int quadrant, PositionalShadowAtlasQuadrantSubdiv subdiv)
    {
        if (quadrant is >= 0 and <= 3) Put("posquad" + quadrant, (int)subdiv);
    }

    public PositionalShadowAtlasQuadrantSubdiv GetPositionalShadowAtlasQuadrantSubdiv(int quadrant) =>
        quadrant is >= 0 and <= 3 ? (PositionalShadowAtlasQuadrantSubdiv)Get("posquad" + quadrant, $"rendering/lights_and_shadows/positional_shadow/atlas_quadrant_{quadrant}_subdiv", s_quadrantDefaults[quadrant], AsInt)
                                  : PositionalShadowAtlasQuadrantSubdiv.Disabled;

    /// <summary>Stored: the engine has no per-viewport debug draw modes in the game.</summary>
    public DebugDrawEnum DebugDraw
    {
        get => (DebugDrawEnum)Get("debugdraw", "", 0, AsInt);
        set => Put("debugdraw", (int)value);
    }

    /// <summary>Stored: the engine renders 2D in the display format.</summary>
    public bool UseHdr2D
    {
        get => Get("hdr2d", "rendering/viewport/hdr_2d", false, AsBoolean);
        set => Put("hdr2d", value);
    }

    /// <summary>Stored: variable-rate shading is not used.</summary>
    public VrsModeEnum VrsMode
    {
        get => (VrsModeEnum)Get("vrs", "rendering/vrs/mode", 0, AsInt);
        set => Put("vrs", (int)value);
    }

    /// <summary>Stored.</summary>
    public VrsUpdateModeEnum VrsUpdateMode
    {
        get => (VrsUpdateModeEnum)Get("vrsupdate", "rendering/vrs/update_mode", 1, AsInt);
        set => Put("vrsupdate", (int)value);
    }

    /// <summary>Stored.</summary>
    public Texture2D? VrsTexture { get; set; }
}
