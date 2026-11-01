// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// RenderingServer's quality switches for effects Aver does not draw as Godot does (screen-space occlusion and reflections, the voxel and SDF GI quality,
// the volumetric fog volume): a graphics-settings menu calls them, so they exist, keep what they were given and say so once in the log. Aver's ray-traced
// lighting has its own quality ladder (the RENDER.* project keys); none of these moves it.

namespace Aver.Fabric;

public static partial class RenderingServer
{
    /// <summary>Godot's RenderingServer.EnvironmentSSAOQuality (ENV_SSAO_QUALITY_*).</summary>
    public enum EnvironmentSsaoQuality { VeryLow = 0, Low = 1, Medium = 2, High = 3, Ultra = 4 }

    /// <summary>Godot's RenderingServer.EnvironmentSSILQuality (ENV_SSIL_QUALITY_*).</summary>
    public enum EnvironmentSsilQuality { VeryLow = 0, Low = 1, Medium = 2, High = 3, Ultra = 4 }

    /// <summary>Godot's RenderingServer.EnvironmentSSRRoughnessQuality (ENV_SSR_ROUGHNESS_QUALITY_*).</summary>
    public enum EnvironmentSsrRoughnessQuality { Disabled = 0, Low = 1, Medium = 2, High = 3 }

    /// <summary>The settings of a screen-space effect's quality as a script gave them.</summary>
    internal readonly record struct ScreenSpaceQuality(int Quality, bool HalfSize, float AdaptiveTarget, int BlurPasses, float FadeoutFrom, float FadeoutTo);

    private static ScreenSpaceQuality s_ssao = new(2, false, 0.5f, 2, 50f, 300f);
    private static ScreenSpaceQuality s_ssil = new(2, false, 0.5f, 2, 50f, 300f);
    private static EnvironmentSsrRoughnessQuality s_ssrRoughness = EnvironmentSsrRoughnessQuality.Low;
    private static bool s_giHalfResolution, s_glowBicubic, s_volFogFilter = true;
    private static int s_volFogSize = 64, s_volFogDepth = 64;
    private static Color s_defaultClearColor = new(0.3f, 0.3f, 0.3f, 1f);

    /// <summary>Approximated, kept as set: Aver has no screen-space ambient occlusion; its ray-traced GI occludes ambient light, at its own quality.</summary>
    public static void EnvironmentSetSsaoQuality(EnvironmentSsaoQuality quality, bool halfSize, float adaptiveTarget, int blurPasses, float fadeoutFrom, float fadeoutTo)
    {
        s_ssao = new((int)quality, halfSize, adaptiveTarget, blurPasses, fadeoutFrom, fadeoutTo);
        Approximated.Once("RenderingServer.EnvironmentSetSsaoQuality", "kept; Aver has no screen-space ambient occlusion, so the quality changes nothing that is drawn");
    }

    /// <summary>Approximated, kept as set: Aver has no screen-space indirect lighting; its GI is ray traced.</summary>
    public static void EnvironmentSetSsilQuality(EnvironmentSsilQuality quality, bool halfSize, float adaptiveTarget, int blurPasses, float fadeoutFrom, float fadeoutTo)
    {
        s_ssil = new((int)quality, halfSize, adaptiveTarget, blurPasses, fadeoutFrom, fadeoutTo);
        Approximated.Once("RenderingServer.EnvironmentSetSsilQuality", "kept; Aver has no screen-space indirect lighting, so the quality changes nothing that is drawn");
    }

    /// <summary>Approximated, kept as set: reflections are ray traced or probe based, not screen-space.</summary>
    public static void EnvironmentSetSsrRoughnessQuality(EnvironmentSsrRoughnessQuality quality)
    {
        s_ssrRoughness = quality;
        Approximated.Once("RenderingServer.EnvironmentSetSsrRoughnessQuality", "kept; Aver has no screen-space reflections, so the quality changes nothing that is drawn");
    }

    /// <summary>Approximated, kept as set: Aver's GI resolution follows its GI quality level (RENDER.GI), not this switch.</summary>
    public static void GiSetUseHalfResolution(bool halfResolution)
    {
        s_giHalfResolution = halfResolution;
        Approximated.Once("RenderingServer.GiSetUseHalfResolution", "kept; the resolution of Aver's global illumination follows the project's GI quality, so this changes nothing that is drawn");
    }

    /// <summary>Approximated, kept as set: the glow upscale filter is Aver's own.</summary>
    public static void EnvironmentGlowSetUseBicubicUpscale(bool enable)
    {
        s_glowBicubic = enable;
        Approximated.Once("RenderingServer.EnvironmentGlowSetUseBicubicUpscale", "kept; Aver's bloom has its own upscale filter");
    }

    /// <summary>Approximated, kept as set: Aver's volumetric fog is filtered as the renderer decides.</summary>
    public static void EnvironmentSetVolumetricFogFilterActive(bool active)
    {
        s_volFogFilter = active;
        Approximated.Once("RenderingServer.EnvironmentSetVolumetricFogFilterActive", "kept; Aver's volumetric fog chooses its own filtering");
    }

    /// <summary>Approximated, kept as set: Aver's volumetric fog grid is sized by the renderer.</summary>
    public static void EnvironmentSetVolumetricFogVolumeSize(int size, int depth)
    {
        s_volFogSize = size;
        s_volFogDepth = depth;
        Approximated.Once("RenderingServer.EnvironmentSetVolumetricFogVolumeSize", "kept; Aver's volumetric fog grid is sized by the renderer");
    }

    /// <summary>Godot's RenderingServer.RenderingInfo (RENDERING_INFO_*).</summary>
    public enum RenderingInfo
    {
        TotalObjectsInFrame = 0, TotalPrimitivesInFrame = 1, TotalDrawCallsInFrame = 2, TextureMemUsed = 3, BufferMemUsed = 4, VideoMemUsed = 5,
        PipelineCompilationsCanvas = 6, PipelineCompilationsMesh = 7, PipelineCompilationsSurface = 8, PipelineCompilationsDraw = 9, PipelineCompilationsSpecialization = 10,
    }

    /// <summary>What the last frame submitted (P18): objects, triangles and draw calls from the engine's statistics, which are counted only once something has asked (the
    /// first call returns 0, the next frame's numbers follow). The memory and pipeline counters are not kept by Aver and read 0. In a ray-driven frame the draw calls are 0.</summary>
    public static ulong GetRenderingInfo(RenderingInfo info)
    {
        Aver.Framework.RenderStats? s = RenderBackend.Current.Stats();
        if (s is not { } r) return 0;
        return info switch
        {
            RenderingInfo.TotalObjectsInFrame => (ulong)Math.Max(r.Objects, 0),
            RenderingInfo.TotalPrimitivesInFrame => (ulong)Math.Max(r.Triangles, 0),
            RenderingInfo.TotalDrawCallsInFrame => (ulong)Math.Max(r.DrawCalls, 0),
            _ => 0UL,
        };
    }

    /// <summary>Godot's RenderingServer.DecalFilter (DECAL_FILTER_*).</summary>
    public enum DecalFilter { Nearest = 0, NearestMipmaps = 1, NearestMipmapsAnisotropic = 2, Linear = 3, LinearMipmaps = 4, LinearMipmapsAnisotropic = 5 }

    /// <summary>Godot's RenderingServer.LightProjectorFilter (LIGHT_PROJECTOR_FILTER_*).</summary>
    public enum LightProjectorFilter { Nearest = 0, NearestMipmaps = 1, NearestMipmapsAnisotropic = 2, Linear = 3, LinearMipmaps = 4, LinearMipmapsAnisotropic = 5 }

    private static DecalFilter s_decalFilter = DecalFilter.LinearMipmaps;
    private static LightProjectorFilter s_projectorFilter = LightProjectorFilter.LinearMipmaps;
    private static int s_directionalAtlas = 4096;
    private static bool s_directionalAtlas16 = true;

    /// <summary>Approximated, kept as set: decals and light projectors are sampled with the engine's material sampler.</summary>
    public static void DecalsSetFilter(DecalFilter filter)
    {
        s_decalFilter = filter;
        Approximated.Once("RenderingServer.DecalsSetFilter", "kept; decal images are sampled with the engine's own filter");
    }

    /// <summary>Approximated, kept as set: a light's cookie image is sampled with the engine's own filter.</summary>
    public static void LightProjectorsSetFilter(LightProjectorFilter filter)
    {
        s_projectorFilter = filter;
        Approximated.Once("RenderingServer.LightProjectorsSetFilter", "kept; a light's cookie image is sampled with the engine's own filter");
    }

    /// <summary>Approximated, kept as set: the sun's cascade size (RENDER.SHADOWRES) is read when the renderer starts, so a size set while the game runs changes
    /// nothing that is drawn. The converter maps the project's directional_shadow/size to RENDER.SHADOWRES.</summary>
    public static void DirectionalShadowAtlasSetSize(int size, bool is16Bits)
    {
        s_directionalAtlas = size;
        s_directionalAtlas16 = is16Bits;
        Approximated.Once("RenderingServer.DirectionalShadowAtlasSetSize", "kept; the sun's shadow resolution is read from the project (RENDER.SHADOWRES) when the renderer starts");
    }

    internal static DecalFilter DecalsFilter => s_decalFilter;
    internal static LightProjectorFilter LightProjectorsFilter => s_projectorFilter;
    internal static (int Size, bool SixteenBits) DirectionalShadowAtlas => (s_directionalAtlas, s_directionalAtlas16);

    /// <summary>The colour behind everything when nothing else is drawn there (Godot's default clear colour, 0.3 grey unless the project sets it).</summary>
    public static Color GetDefaultClearColor() => s_defaultClearColor;

    /// <summary>Approximated, kept as set: a level's background is its sky, which the converter writes from the Environment (or the project's default clear
    /// colour) at conversion; a colour set here later does not repaint it.</summary>
    public static void SetDefaultClearColor(Color color)
    {
        s_defaultClearColor = color;
        Approximated.Once("RenderingServer.SetDefaultClearColor", "kept; the background is the level's sky, written when the project was converted");
    }

    internal static ScreenSpaceQuality SsaoQuality => s_ssao;
    internal static ScreenSpaceQuality SsilQuality => s_ssil;
    internal static EnvironmentSsrRoughnessQuality SsrRoughnessQuality => s_ssrRoughness;
    internal static bool GiHalfResolution => s_giHalfResolution;
    internal static bool GlowBicubicUpscale => s_glowBicubic;
    internal static bool VolumetricFogFilterActive => s_volFogFilter;
    internal static (int Size, int Depth) VolumetricFogVolumeSize => (s_volFogSize, s_volFogDepth);
}
