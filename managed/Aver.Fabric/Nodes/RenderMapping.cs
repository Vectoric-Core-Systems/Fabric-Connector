// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Wave 4 of the Godot parity plan (docs/plans/WAVE4_RENDER.md): Godot's rendering settings, light units, exposure, shadow cascades and glow as Aver's values. Pure
// functions; native/formats.godot/include/aver/formats/godot/GodotRender.hpp is the converter's twin and a test pins the same numbers.

namespace Aver.Fabric;

/// <summary>Godot's rendering numbers to Aver's. Everything here is pure: no engine is needed to run it.</summary>
public static class RenderMapping
{
    // ---- anti-aliasing ----

    /// <summary>Viewport.MSAA (0 off, 1 2x, 2 4x, 3 8x) as a sample count.</summary>
    public static int MsaaSamples(int godotMsaa) => godotMsaa switch { <= 0 => 1, 1 => 2, 2 => 4, _ => 8 };

    /// <summary>The consistent set of anti-aliasing switches: Aver's TAA needs MSAA off, so a viewport that asks for TAA gets one sample whatever msaa_3d says.
    /// MSAA samples are the engine's own sample count; <c>Taa</c> is 0 or 1.</summary>
    public static (int Msaa, int Taa) ChooseAa(int msaa3d, bool useTaa)
    {
        int samples = MsaaSamples(msaa3d);
        return useTaa ? (1, 1) : (samples, 0);
    }

    /// <summary>scaling_3d_scale clamped to Godot's 0.25 to 2.</summary>
    public static float Scale3D(float godotScale) => Math.Clamp(godotScale, 0.25f, 2f);

    /// <summary>fsr_sharpness: 0 is the sharpest and the sharpness halves with every 1.0; Aver's sharpness is 0 to 1 with 1 the sharpest.</summary>
    public static float FsrSharpness(float godot) => Math.Clamp(MathF.Pow(2f, -Math.Max(godot, 0f)), 0f, 1f);

    /// <summary>The AverSR preset (1 quality, 2 balanced, 3 performance; 0 none) nearest an FSR-style scale below 1.</summary>
    public static int AverSrPreset(float scale) => scale >= 0.99f ? 0 : scale >= 0.63f ? 1 : scale >= 0.545f ? 2 : 3;

    // ---- level of detail ----

    /// <summary>mesh_lod_threshold is the screen-space error in pixels, as RENDER.LODTHRESHOLD (0 to 64).</summary>
    public static float LodThresholdPixels(float godot) => Math.Clamp(godot, 0f, 64f);

    /// <summary>GeometryInstance3D.lod_bias is a multiplier (1: the default settings); CMeshRenderer.lodBias is in stops, the base-2 logarithm, -8 for 0 and below.</summary>
    public static float LodBiasStops(float multiplier) => multiplier <= 0f ? -8f : Math.Clamp(MathF.Log2(multiplier), -8f, 8f);

    // ---- sun shadows ----

    /// <summary>One cascade's edge (RENDER.SHADOWRES) from Godot's directional shadow atlas size: half the atlas with two or more splits, all of it for one,
    /// clamped to 512 to 4096 and snapped down to a power of two.</summary>
    public static int ShadowResolution(int atlasSize, int cascades)
    {
        int edge = Math.Clamp(cascades <= 1 ? atlasSize : atlasSize / 2, 512, 4096);
        int p = 512;
        while (p * 2 <= edge) p *= 2;
        return p;
    }

    /// <summary>soft_shadow_filter_quality (0 hard ... 3 medium, Godot's default ... 5 ultra) as the PCF kernel width in shadow texels; medium is the engine's fixed 3x3.</summary>
    public static float ShadowFilterWidth(int quality) => quality switch { <= 0 => 0f, 1 => 0.5f, 2 => 0.75f, 3 => 1f, 4 => 2f, _ => 3f };

    /// <summary>directional_shadow_mode (0 orthogonal, 1 two splits, 2 four splits) as a cascade count.</summary>
    public static int CascadeCount(int godotMode) => godotMode <= 0 ? 1 : godotMode == 1 ? 2 : 4;

    /// <summary>The least-squares split lambda for Aver's slice formula against Godot's split fractions (independent fractions of the shadow range): -1 when there is
    /// nothing to fit. See GodotRender.hpp for the reasoning.</summary>
    public static float FitSplitLambda(int cascades, ReadOnlySpan<float> godotFractions, float nearCm, float farCm)
    {
        if (cascades < 2 || !(farCm > nearCm) || nearCm <= 0f) return -1f;
        double num = 0, den = 0;
        for (int i = 1; i < cascades; i++)
        {
            double p = (double)i / cascades;
            double logf = (nearCm * Math.Pow(farCm / nearCm, p) - nearCm) / (farCm - nearCm);
            double g = Math.Clamp(godotFractions[i - 1], 0f, 1f);
            num += (g - p) * (logf - p);
            den += (logf - p) * (logf - p);
        }
        return den > 1e-12 ? (float)Math.Clamp(num / den, 0, 1) : -1f;
    }

    /// <summary>A sun's shadow settings as RENDER.SHADOW* values: cascades, split lambda (-1: the engine's), distance in cm (0: automatic).</summary>
    public static (int Cascades, float Lambda, float DistanceCm) FitSunShadow(int godotMode, float split1, float split2, float split3, float maxDistanceM, float cameraNearM)
    {
        int cascades = CascadeCount(godotMode);
        float distance = maxDistanceM > 0f ? maxDistanceM * 100f : 0f;
        float lambda = -1f;
        if (cascades >= 2 && distance > 0f) lambda = FitSplitLambda(cascades, stackalloc float[] { split1, split2, split3 }, Math.Max(cameraNearM, 0.001f) * 100f, distance);
        return (cascades, lambda, distance);
    }

    // ---- glow ----

    /// <summary>glow_blend_mode (0 additive, 1 screen, 2 soft light, 3 replace, 4 mix): the POSTVOLUME key glowblend has the same numbers.</summary>
    public static int GlowBlend(int godotMode) => Math.Clamp(godotMode, 0, 4);

    // ---- physical light units and exposure ----

    /// <summary>One engine radiance unit in lux (the 100000 lux sun is 3.0 of them).</summary>
    public const float EngineUnitLux = 100000f / 3f;

    public enum LightClass { Directional, Omni, Spot }

    /// <summary>The shading energy Godot uses with physical units: light_energy times the intensity (lux, or lumens) times 1 (directional), 1/(4 pi) (omni) or 1/pi (spot)
    /// times the camera's exposure normalization.</summary>
    public static float PhysicalShadingEnergy(LightClass kind, float lightEnergy, float intensity, float exposureNormalization)
    {
        float k = kind == LightClass.Directional ? 1f : kind == LightClass.Omni ? 1f / (4f * MathF.PI) : 1f / MathF.PI;
        return lightEnergy * intensity * k * exposureNormalization;
    }

    /// <summary>The same as an Aver CLight intensity (lux for the sun, candela at 1 m for a local light).</summary>
    public static float PhysicalAverIntensity(LightClass kind, float lightEnergy, float intensity, float exposureNormalization) =>
        PhysicalShadingEnergy(kind, lightEnergy, intensity, exposureNormalization) * EngineUnitLux;

    /// <summary>CameraAttributesPhysical: 1 / (1.2 aperture^2 shutter_speed (100 / ISO)), the shutter speed in 1/s. f/16, 1/100 s, ISO 100 gives 1/30720.</summary>
    public static float PhysicalExposureNormalization(float aperture, float shutterSpeed, float sensitivity)
    {
        double e = (double)aperture * aperture * shutterSpeed * (100.0 / Math.Max(sensitivity, 1e-3f));
        return e > 0 ? (float)(1.0 / (e * 1.2)) : 1f;
    }

    /// <summary>CameraAttributesPractical: the sensitivity over 3072007 (the physical default at ISO 100).</summary>
    public static float PracticalExposureNormalization(float sensitivity) => sensitivity / 3072007f;

    /// <summary>Light3D.light_temperature as a LINEAR tint (the brightest channel 1): Godot multiplies it with the light colour in linear light when physical units are on.</summary>
    public static (float R, float G, float B) TemperatureTint(float kelvin)
    {
        double t = Math.Clamp(kelvin, 1000f, 40000f), t2 = t * t;
        double u = (0.860117757 + 1.54118254e-4 * t + 1.28641212e-7 * t2) / (1.0 + 8.42420235e-4 * t + 7.08145163e-7 * t2);
        double v = (0.317398726 + 4.22806245e-5 * t + 4.20481691e-8 * t2) / (1.0 - 2.89741816e-5 * t + 1.61456053e-7 * t2);
        double d = 1.0 / (2.0 * u - 8.0 * v + 4.0);
        double x = 3.0 * u * d, y = 2.0 * v * d;
        double a = 1.0 / Math.Max(y, 1e-5);
        double X = x * a, Y = 1.0, Z = (1.0 - x - y) * a;
        double r = 3.2404542 * X - 1.5371385 * Y - 0.4985314 * Z;
        double g = -0.9692660 * X + 1.8760108 * Y + 0.0415560 * Z;
        double b = 0.0556434 * X - 0.2040259 * Y + 1.0572252 * Z;
        double m = Math.Max(Math.Max(r, g), Math.Max(b, 1e-5));
        return ((float)Math.Clamp(r / m, 0, 1), (float)Math.Clamp(g / m, 0, 1), (float)Math.Clamp(b / m, 0, 1));
    }
}
