#pragma once
// Wave 4 of the Godot parity plan (docs/plans/WAVE4_RENDER.md): Godot's rendering settings, light units, exposure, cascades and glow -> Aver's keys. Pure functions,
// shared by the converter (tools/fabric) and its tests; Aver.Fabric's RenderMapping repeats them and a managed test pins the same numbers. The engine only gained neutral
// capabilities with unchanged defaults; every Godot value and formula lives here.
#include <algorithm>
#include <array>
#include <cmath>

namespace aver::fmt::godotrender {

inline constexpr double kPi = 3.14159265358979323846;

// ---- anti-aliasing (F1) ----------------------------------------------------------------------------------------------------------------------------------
// Godot's Viewport.MSAA (0 off, 1 2x, 2 4x, 3 8x) as a sample count.
inline int msaaSamples(int godotMsaa) {
    static const int kSamples[] = {1, 2, 4, 8};
    return kSamples[std::clamp(godotMsaa, 0, 3)];
}

// The consistent set. Aver's TAA needs MSAA off (RENDER.TAA 1 "needs MSAA 1; otherwise FSR 1 is used"), so a project that asks for TAA gets MSAA 1 whatever msaa_3d says,
// and the report names the dropped MSAA. -1 is "not stated" (nothing is written). A denoiser (the ray-traced tiers) works on the ray-traced signal, not on MSAA's coverage
// samples: MSAA only multiplies the raster pass, so it is left as stated.
struct AaChoice {
    int msaa = -1;            // RENDER.MSAA sample count, or -1
    int taa = -1;             // RENDER.TAA, or -1
    bool msaaDropped = false; // the project stated MSAA above 1 and TAA won
};
inline AaChoice chooseAa(int msaa3d, int useTaa) {
    AaChoice c;
    if (msaa3d >= 0) c.msaa = msaaSamples(msaa3d);
    if (useTaa >= 0) c.taa = useTaa > 0 ? 1 : 0;
    if (c.taa == 1) {
        c.msaaDropped = c.msaa > 1;
        c.msaa = 1;
    }
    return c;
}

// ---- 3D scaling (F1) -------------------------------------------------------------------------------------------------------------------------------------
inline double scale3d(double godotScale) { return std::clamp(godotScale, 0.25, 2.0); }

// Godot's fsr_sharpness: 0 is the sharpest and the sharpness halves with every 1.0 (docs of Viewport.fsr_sharpness). Aver's sharpness runs 0 to 1 with 1 the sharpest.
inline double fsrSharpness(double godot) { return std::clamp(std::pow(2.0, -std::max(godot, 0.0)), 0.0, 1.0); }

// FSR-style scaling (mode 1 or 2) at a scale below 1 becomes the nearest AverSR preset (1 quality, 2 balanced, 3 performance); 0 is "no upscaler" (scale about 1 or above).
// The thresholds are the midpoints of AMD's FSR 1 ratios (0.67, 0.59, 0.50); Aver's own AverSR ratios were not checked.
inline int averSrPreset(double scale) {
    if (scale >= 0.99) return 0;
    if (scale >= 0.63) return 1;
    if (scale >= 0.545) return 2;
    return 3;
}

// ---- LOD (F32) -------------------------------------------------------------------------------------------------------------------------------------------
// Godot's mesh_lod_threshold is the screen-space error in pixels (project default 1.0), like RENDER.LODTHRESHOLD; 0 means automatic LOD is off.
inline double lodThresholdPixels(double godot) { return std::clamp(godot, 0.0, 64.0); }
// GeometryInstance3D.lod_bias is a multiplier (1 = the default settings, larger keeps finer levels farther, 0 forces the coarsest). CMeshRenderer.lodBias is in stops:
// +1 halves the pixel-error threshold, so the stop count is log2 of the multiplier, -8 (the clamp) for 0 and below.
inline double lodBiasStops(double multiplier) { return multiplier <= 0.0 ? -8.0 : std::clamp(std::log2(multiplier), -8.0, 8.0); }

// ---- sun shadows (P3, P4) --------------------------------------------------------------------------------------------------------------------------------
// directional_shadow/size: Godot's atlas edge for all cascades together. RENDER.SHADOWRES is one cascade's edge: half the atlas with two or more splits (a 2x2 or 1x2
// division), the whole atlas for one. Clamped to 512..4096 and snapped to a power of two.
inline int shadowResolution(int atlasSize, int cascades) {
    int edge = cascades <= 1 ? atlasSize : atlasSize / 2;
    edge = std::clamp(edge, 512, 4096);
    int p = 512;
    while (p * 2 <= edge) p *= 2;
    return p;
}

// soft_shadow_filter_quality 0 hard, 1 very low, 2 low, 3 medium (Godot's default), 4 high, 5 ultra -> RENDER.SHADOWFILTER, the PCF kernel width in shadow texels. Medium
// is the engine's fixed 3x3 (1.0), so a project that states Godot's default keeps today's filter; the others bracket it. A judgement: the sample counts differ.
inline double shadowFilterWidth(int quality) {
    static const double kWidth[] = {0.0, 0.5, 0.75, 1.0, 2.0, 3.0};
    return kWidth[std::clamp(quality, 0, 5)];
}

// DirectionalLight3D.directional_shadow_mode (0 orthogonal, 1 two splits, 2 four splits) -> the cascade count.
inline int cascadeCount(int godotMode) { return godotMode <= 0 ? 1 : godotMode == 1 ? 2 : 4; }

// Aver slices the shadow range with sliceFar(p) = lambda * n * (f/n)^p + (1 - lambda) * (n + (f - n) * p), p = i / N (VoxiRenderer.cpp), n the camera near plane and f the
// farthest shadowed distance. Godot places split i at n + split_i * (max_distance - n) (the splits are independent fractions of the range, not cumulative;
// renderer_scene_cull.cpp). The fit is the least-squares lambda over the N - 1 inner splits: the slice position is linear in lambda, so the solution is closed form.
// With one cascade there is nothing to fit and `lambda` keeps the engine's default (returns -1: not written).
struct SunShadowFit {
    int cascades = 4;
    double lambda = -1.0;     // -1: leave the engine's
    double distanceCm = 0.0;  // 0: automatic
};
inline double fitSplitLambda(int cascades, const double* godotFractions /* cascades - 1 values */, double nearCm, double farCm) {
    if (cascades < 2 || !(farCm > nearCm) || nearCm <= 0.0) return -1.0;
    double num = 0.0, den = 0.0;
    for (int i = 1; i < cascades; ++i) {
        const double p = static_cast<double>(i) / cascades;
        const double even = p;                                                       // fraction of the range at lambda 0
        const double logf = (nearCm * std::pow(farCm / nearCm, p) - nearCm) / (farCm - nearCm);   // at lambda 1
        const double g = std::clamp(godotFractions[i - 1], 0.0, 1.0);
        num += (g - even) * (logf - even);
        den += (logf - even) * (logf - even);
    }
    return den > 1e-12 ? std::clamp(num / den, 0.0, 1.0) : -1.0;
}
inline SunShadowFit fitSunShadow(int godotMode, double split1, double split2, double split3, double maxDistanceM, double cameraNearM) {
    SunShadowFit r;
    r.cascades = cascadeCount(godotMode);
    r.distanceCm = maxDistanceM > 0.0 ? maxDistanceM * 100.0 : 0.0;
    if (r.cascades >= 2 && r.distanceCm > 0.0) {
        const double fractions[3] = {split1, split2, split3};
        // Two splits: Godot's only inner split is the first; four: the first three.
        r.lambda = fitSplitLambda(r.cascades, fractions, std::max(cameraNearM, 0.001) * 100.0, r.distanceCm);
    }
    return r;
}

// ---- glow (P10, E7) --------------------------------------------------------------------------------------------------------------------------------------
// Environment.glow_blend_mode: 0 additive, 1 screen, 2 soft light, 3 replace, 4 mix; the same numbers as the POSTVOLUME key `glowblend`.
inline int glowBlend(int godotMode) { return std::clamp(godotMode, 0, 4); }

// ---- physical light units and exposure (F21) -------------------------------------------------------------------------------------------------------------
// With rendering/lights_and_shadows/use_physical_light_units on, Godot scales a light's energy by (light_storage.cpp): directional 1, omni 1/(4 pi), spot 1/pi, and then by
// the camera's exposure normalization; with it off every type is energy * pi. light_intensity_lumens (omni, spot) and light_intensity_lux (directional) are the
// intensity; light_energy keeps multiplying it. Aver's units: one engine radiance unit is 100000/3 lux (godotlight::kEngineUnitCdm2).
inline constexpr double kEngineUnitCdm2 = 100000.0 / 3.0;
enum class LightKind { Directional, Omni, Spot };
// The shading energy Godot would use (the number that multiplies albedo / pi).
inline double physicalShadingEnergy(LightKind kind, double lightEnergy, double intensity, double exposureNormalization) {
    const double k = kind == LightKind::Directional ? 1.0 : kind == LightKind::Omni ? 1.0 / (4.0 * kPi) : 1.0 / kPi;
    return lightEnergy * intensity * k * exposureNormalization;
}
// ... and as an Aver CLight intensity (lux for the sun, candela at 1 m for a local light).
inline double physicalAverIntensity(LightKind kind, double lightEnergy, double intensity, double exposureNormalization) {
    return physicalShadingEnergy(kind, lightEnergy, intensity, exposureNormalization) * kEngineUnitCdm2;
}

// CameraAttributesPhysical::calculate_exposure_normalization (camera_attributes.cpp): 1 / (1.2 * aperture^2 * shutter_speed * (100 / ISO)), the shutter speed in 1/s.
// Filament's EV100 form: exposure = 1 / (1.2 * 2^EV100), EV100 = log2(N^2 / t * 100 / S). At f/16, 1/100 s, ISO 100: 1 / 30720.
inline double physicalExposureNormalization(double aperture, double shutterSpeed, double sensitivity) {
    const double e = aperture * aperture * shutterSpeed * (100.0 / std::max(sensitivity, 1e-3));
    return e > 0.0 ? 1.0 / (e * 1.2) : 1.0;
}
// CameraAttributesPractical: the sensitivity over 3072007.0 (the physical default at ISO 100).
inline double practicalExposureNormalization(double sensitivity) { return sensitivity / 3072007.0; }

// Light3D._color_from_temperature: the Krystek / Filament fit of the Planckian locus, normalised so the brightest linear channel is 1 and clamped. Returns LINEAR rgb;
// Godot multiplies it with the light colour in linear light when physical units are on.
inline std::array<double, 3> temperatureTint(double kelvin) {
    const double t = std::clamp(kelvin, 1000.0, 40000.0), t2 = t * t;
    const double u = (0.860117757 + 1.54118254e-4 * t + 1.28641212e-7 * t2) / (1.0 + 8.42420235e-4 * t + 7.08145163e-7 * t2);
    const double v = (0.317398726 + 4.22806245e-5 * t + 4.20481691e-8 * t2) / (1.0 - 2.89741816e-5 * t + 1.61456053e-7 * t2);
    const double d = 1.0 / (2.0 * u - 8.0 * v + 4.0);
    const double x = 3.0 * u * d, y = 2.0 * v * d;
    const double a = 1.0 / std::max(y, 1e-5);
    const double X = x * a, Y = 1.0, Z = (1.0 - x - y) * a;
    std::array<double, 3> c = {3.2404542 * X - 1.5371385 * Y - 0.4985314 * Z, -0.9692660 * X + 1.8760108 * Y + 0.0415560 * Z, 0.0556434 * X - 0.2040259 * Y + 1.0572252 * Z};
    const double m = std::max({c[0], c[1], c[2], 1e-5});
    for (double& ch : c) ch = std::clamp(ch / m, 0.0, 1.0);
    return c;
}

}  // namespace aver::fmt::godotrender
