// FabricWave4Test -- wave 4 of the Godot parity plan (rendering settings, lights, shadows, environment) through `AverAssetC fabric`. First the pure mappings of GodotRender.hpp
// (anti-aliasing set, scaling, LOD, cascades, filter, exposure, light units, temperature), then three fixtures: Wave4Game (the project's viewport and shadow settings, the
// glow, the ambient override, the volumetric fog extras, per-instance flags, a light projector, a camera's own environment), Wave4PhysGame (physical light units with a
// physical camera) and Wave4SkyGame (the opt-in procedural sky, off and on); and the default path: a project that states none of it writes none of it.
#include "aver/core/ErrorCodes.hpp"
#include "aver/core/Log.hpp"
#include "aver/formats/OcProject.hpp"
#include "aver/formats/OcWorld.hpp"
#include "aver/formats/godot/GodotRender.hpp"
#include "aver/platform/FileSystem.hpp"

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <filesystem>
#include <sstream>
#include <string>
#include <vector>
#if !defined(_WIN32)
#include <sys/wait.h>
#endif

using namespace aver;
namespace gr = aver::fmt::godotrender;

static int g_checks = 0;
static int g_failures = 0;

static void check(bool cond, const std::string& what) {
    ++g_checks;
    if (cond) { AVER_INFO("  ok    {}", what); return; }
    ++g_failures;
    AVER_ERROR("  FAIL  {}", what);
}

static bool near(double a, double b, double tol = 1e-4) { return std::fabs(a - b) <= tol * std::max(1.0, std::fabs(b)); }

static int runCapture(const std::string& cmd, std::string& out) {
    out.clear();
#if defined(_WIN32)
    const std::string wrapped = "\"" + cmd + " 2>&1\"";
    FILE* pipe = _popen(wrapped.c_str(), "r");
#else
    const std::string wrapped = cmd + " 2>&1";
    FILE* pipe = popen(wrapped.c_str(), "r");
#endif
    if (!pipe) return -1;
    char buf[4096];
    while (std::fgets(buf, sizeof buf, pipe)) out += buf;
#if defined(_WIN32)
    return _pclose(pipe);
#else
    const int status = pclose(pipe);
    return WIFEXITED(status) ? WEXITSTATUS(status) : -1;
#endif
}

static std::string slurp(const std::string& path) {
    std::string s;
    readFileText(path, s);
    return s;
}

static double postValue(const fmt::OcPostVolume& v, const char* key, double def = -1e30) {
    const fmt::OcPostValue* p = v.find(key);
    return p ? p->v[0] : def;
}

static int findPlacement(const fmt::OcWorldData& w, const char* name) {
    for (usize i = 0; i < w.placements.size(); ++i)
        if (w.placements[i].name == name && !w.placements[i].className.empty()) return static_cast<int>(i);
    return -1;
}

static const std::string* prop(const fmt::OcWorldData& w, int idx, const char* key) {
    if (idx < 0) return nullptr;
    for (const fmt::OcPlacedComponent& c : w.placements[static_cast<usize>(idx)].components)
        if (c.type == "Props")
            if (const std::string* v = c.find(key)) return v;
    return nullptr;
}

// Whether a report line holds all three fragments.
static bool reportHas(const std::string& jsonl, const std::string& a, const std::string& b, const std::string& c = {}) {
    std::istringstream is(jsonl);
    std::string line;
    while (std::getline(is, line))
        if (line.find(a) != std::string::npos && line.find(b) != std::string::npos && line.find(c) != std::string::npos) return true;
    return false;
}

static bool ratedAs(const std::string& jsonl, const char* type, const char* property, const char* rating) {
    return reportHas(jsonl, std::string("\"type\":\"") + type + "\"", std::string("\"property\":\"") + property + "\"", std::string("\"rating\":\"") + rating + "\"");
}

static bool convert(const std::string& exe, const std::string& src, const std::string& out, const char* extra, std::string& log) {
    const int code = runCapture("\"" + exe + "\" fabric \"" + src + "\" \"" + out + "\" --scenes-only " + extra, log);
    if (code != 0) AVER_INFO("{}", log);
    return code == 0;
}

int main() {
    AVER_INFO("FabricWave4Test");

    // ---- the pure mappings ---------------------------------------------------------------------------------------------------------------
    AVER_INFO("-- anti-aliasing, scaling, LOD --");
    {
        check(gr::msaaSamples(0) == 1 && gr::msaaSamples(1) == 2 && gr::msaaSamples(2) == 4 && gr::msaaSamples(3) == 8 && gr::msaaSamples(9) == 8, "msaa_3d 0 to 3 is 1, 2, 4, 8 samples");
        const gr::AaChoice a = gr::chooseAa(2, 0);
        check(a.msaa == 4 && a.taa == 0 && !a.msaaDropped, "MSAA 4x without TAA stays 4 samples");
        const gr::AaChoice b = gr::chooseAa(3, 1);
        check(b.msaa == 1 && b.taa == 1 && b.msaaDropped, "TAA on drops MSAA to one sample and says so");
        const gr::AaChoice c = gr::chooseAa(-1, 1);
        check(c.msaa == 1 && c.taa == 1 && !c.msaaDropped, "TAA stated alone still writes MSAA 1 (the engine's default 4x would block the TAA)");
        const gr::AaChoice d = gr::chooseAa(-1, -1);
        check(d.msaa == -1 && d.taa == -1, "nothing stated writes nothing");
        check(near(gr::fsrSharpness(0.0), 1.0) && near(gr::fsrSharpness(1.0), 0.5) && near(gr::fsrSharpness(0.2), std::pow(2.0, -0.2)) && near(gr::fsrSharpness(2.0), 0.25), "fsr_sharpness halves the sharpness with every 1.0");
        check(near(gr::scale3d(0.1), 0.25) && near(gr::scale3d(5.0), 2.0) && near(gr::scale3d(0.67), 0.67), "scaling_3d_scale is clamped to 0.25 .. 2");
        check(gr::averSrPreset(1.0) == 0 && gr::averSrPreset(0.67) == 1 && gr::averSrPreset(0.59) == 2 && gr::averSrPreset(0.5) == 3, "FSR scales pick AverSR's nearest preset (none at 1)");
        check(near(gr::lodThresholdPixels(1.0), 1.0) && near(gr::lodThresholdPixels(-3), 0.0) && near(gr::lodThresholdPixels(500), 64.0), "mesh_lod_threshold is pixels, 0 .. 64");
        check(near(gr::lodBiasStops(1.0), 0.0) && near(gr::lodBiasStops(4.0), 2.0) && near(gr::lodBiasStops(0.5), -1.0) && near(gr::lodBiasStops(0.0), -8.0) && near(gr::lodBiasStops(1e9), 8.0), "lod_bias is log2 of the multiplier, -8 for 0, clamped to 8");
    }

    AVER_INFO("-- sun shadows --");
    {
        check(gr::cascadeCount(0) == 1 && gr::cascadeCount(1) == 2 && gr::cascadeCount(2) == 4, "directional_shadow_mode 0, 1, 2 is 1, 2, 4 cascades");
        check(gr::shadowResolution(4096, 4) == 2048 && gr::shadowResolution(8192, 4) == 4096 && gr::shadowResolution(16384, 4) == 4096 && gr::shadowResolution(1024, 4) == 512 && gr::shadowResolution(2048, 1) == 2048,
              "a cascade is half the atlas (the whole atlas for one), 512 .. 4096, a power of two");
        check(gr::shadowFilterWidth(0) == 0.0 && gr::shadowFilterWidth(3) == 1.0 && gr::shadowFilterWidth(5) == 3.0 && gr::shadowFilterWidth(2) < gr::shadowFilterWidth(3) && gr::shadowFilterWidth(3) < gr::shadowFilterWidth(4),
              "the filter quality is monotonic and Godot's default (medium) is the engine's 3x3");
        // The fit: independent splits of the range, Aver's slice formula. A scheme Aver can express exactly comes back exactly.
        const double n = 10.0, f = 8000.0;
        for (const double lambda : {0.2, 0.5, 0.85}) {
            double g[3];
            for (int i = 1; i < 4; ++i) {
                const double p = i / 4.0;
                const double slice = lambda * n * std::pow(f / n, p) + (1.0 - lambda) * (n + (f - n) * p);
                g[i - 1] = (slice - n) / (f - n);
            }
            check(near(gr::fitSplitLambda(4, g, n, f), lambda, 1e-6), "Godot splits that are Aver's own scheme at lambda " + std::to_string(lambda) + " fit back to lambda");
        }
        const double even[3] = {0.25, 0.5, 0.75};
        check(near(gr::fitSplitLambda(4, even, n, f), 0.0, 1e-6), "even fractions are lambda 0");
        const double logish[3] = {0.0, 0.0, 0.0};
        check(gr::fitSplitLambda(4, logish, n, f) > 0.9, "splits crowded near the camera are close to logarithmic");
        check(gr::fitSplitLambda(1, even, n, f) < 0.0 && gr::fitSplitLambda(4, even, n, n) < 0.0, "one cascade, or no range, has nothing to fit");
        const gr::SunShadowFit s = gr::fitSunShadow(2, 0.1, 0.2, 0.5, 100.0, 0.05);
        check(s.cascades == 4 && near(s.distanceCm, 10000.0) && s.lambda > 0.3 && s.lambda < 0.7, "Godot's default splits (0.1, 0.2, 0.5) at 100 m fit to a lambda between the schemes");
        const gr::SunShadowFit o = gr::fitSunShadow(0, 0.1, 0.2, 0.5, 100.0, 0.05);
        check(o.cascades == 1 && o.lambda < 0.0, "an orthogonal sun is one cascade and no lambda");
        check(gr::fitSunShadow(1, 0.3, 0.2, 0.5, 0.0, 0.05).distanceCm == 0.0, "max distance 0 is the engine's automatic reach");
    }

    AVER_INFO("-- light units, exposure, temperature --");
    {
        check(near(gr::physicalExposureNormalization(16.0, 100.0, 100.0), 1.0 / 30720.0, 1e-9), "f/16, 1/100 s, ISO 100: Godot's 1 / 30720");
        check(near(gr::practicalExposureNormalization(100.0), 100.0 / 3072007.0, 1e-12) && near(gr::practicalExposureNormalization(100.0), gr::physicalExposureNormalization(16.0, 100.0, 100.0), 1e-3),
              "the practical camera at ISO 100 matches the physical default");
        check(near(gr::physicalExposureNormalization(8.0, 200.0, 200.0), 1.0 / 7680.0, 1e-9), "f/8, 1/200 s, ISO 200: 1 / 7680");
        check(near(gr::physicalExposureNormalization(16.0, 100.0, 200.0), 2.0 * gr::physicalExposureNormalization(16.0, 100.0, 100.0), 1e-9), "double the ISO doubles the exposure");
        const double sunny = gr::physicalShadingEnergy(gr::LightKind::Directional, 1.0, 100000.0, gr::physicalExposureNormalization(16.0, 100.0, 100.0));
        check(near(sunny, 100000.0 / 30720.0, 1e-9), "the default sun at the default camera shades with 3.255 (sunny 16)");
        check(near(gr::physicalShadingEnergy(gr::LightKind::Omni, 2.0, 1000.0, 1.0), 2.0 * 1000.0 / (4.0 * gr::kPi), 1e-9) && near(gr::physicalShadingEnergy(gr::LightKind::Spot, 1.0, 1000.0, 1.0), 1000.0 / gr::kPi, 1e-9),
              "an omni light is intensity / (4 pi), a spot light intensity / pi, both times the energy");
        check(near(gr::physicalAverIntensity(gr::LightKind::Directional, 1.0, 100000.0, 1.0 / 30720.0), sunny * 100000.0 / 3.0, 1e-6), "and as Aver lux: 100000/3 lux per shading unit");
        const auto warm = gr::temperatureTint(2700.0), white = gr::temperatureTint(6500.0), cool = gr::temperatureTint(10000.0);
        check(near(*std::max_element(warm.begin(), warm.end()), 1.0) && warm[0] > warm[1] && warm[1] > warm[2], "2700 K is warm: red 1, then green, then blue");
        check(white[0] > 0.9 && white[1] > 0.9 && white[2] > 0.9, "6500 K is nearly white");
        check(near(cool[2], 1.0) && cool[2] > cool[0], "10000 K is cool: blue 1");
        check(gr::temperatureTint(1.0)[0] >= 0.0 && gr::temperatureTint(1e9)[2] <= 1.0, "the temperature is clamped to a range the fit holds for");
        check(gr::glowBlend(2) == 2 && gr::glowBlend(9) == 4 && gr::glowBlend(-1) == 0, "glow_blend_mode keeps Godot's numbers, 0 .. 4");
    }

    // ---- through the converter -------------------------------------------------------------------------------------------------------
    const std::filesystem::path exe = AVER_ASSETC_EXE;
    std::error_code ec;
    check(std::filesystem::exists(exe, ec), "AverAssetC was built: " + exe.string());
    if (g_failures) return exitCode(ExitCode::Failed);
    const std::filesystem::path outRoot = std::filesystem::temp_directory_path() / "aver-fabric-wave4-test";
    std::filesystem::remove_all(outRoot, ec);
    std::filesystem::create_directories(outRoot, ec);
    const std::string data = AVER_FABRIC_DATA;
    std::string log, err;

    AVER_INFO("-- Wave4Game: the project, the level and the report --");
    {
        const std::string out = (outRoot / "game").string();
        check(convert(exe.string(), data + "/Wave4Game", out, "", log), "the converter exits 0");
        const std::string proj = out + "/Wave4Game";
        fmt::ProjectDesc pd;
        check(fmt::loadOcproject(proj + "/Wave4Game.ocproject", pd, &err), "the .ocproject loads: " + err);
        check(pd.taa == 1 && pd.msaa == 1, "use_taa with msaa_3d 8x is RENDER.TAA 1 and RENDER.MSAA 1 (the consistent set)");
        check(pd.averSr == 1, "FSR at scale 0.67 is RENDER.AVERSR 1");
        check(near(pd.lodThresholdPx, 2.0) && pd.occlusionCull == 1, "mesh_lod threshold 2 pixels and occlusion culling on");
        check(pd.shadowCascades == 2 && near(pd.shadowDist, 8000.0) && pd.shadowSplit > 0.35 && pd.shadowSplit < 0.5, "the sun's two splits, 80 m and the fitted lambda (" + std::to_string(pd.shadowSplit) + ")");
        check(pd.shadowRes == 4096 && near(pd.shadowFilter, 2.0), "the 8192 atlas is a 4096 cascade; soft filter quality 4 is a width of 2");
        check(near(pd.volFogLength, 4000.0) && pd.hasVolFogAniso && near(pd.volFogAniso, -0.3) && near(pd.volFogTemporal, 0.7), "volumetric fog: 40 m reach, anisotropy -0.3, history weight 0.7");
        check(pd.volFog == 1, "and the light-shaft fog is on");

        fmt::OcWorldData w;
        check(fmt::loadOcworld(proj + "/Content/Maps/main.ocworld", w, &err), "the level loads: " + err);
        check(w.skyPhysical && near(w.skyAmbientEnergy, 1.5) && near(w.skyAmbientSky, 0.25) && near(w.skyAmbientColor[2], 0.3185, 2e-3) && near(w.skyAmbientColor[0], 0.0331, 2e-3),
              "ambient_light_source Color under the physical sky is the SKY record's ambient override (linear colour, energy, sky share)");
        check(near(w.skyLight, 1.0), "and the sky light stays 1: the energy rides the override");
        check(w.postVolumes.size() == 1, "one post volume");
        if (!w.postVolumes.empty()) {
            const fmt::OcPostVolume& v = w.postVolumes[0];
            check(postValue(v, "glowblend") == 1 && near(postValue(v, "glowstrength"), 1.4) && near(postValue(v, "glowcap"), 8.0), "glow: screen blend, strength 1.4, luminance cap 8");
            check(postValue(v, "deband") == 1, "use_debanding is the post volume's deband");
            check(near(postValue(v, "bloom"), 0.9), "and the bloom intensity as before");
        }
        const int none = findPlacement(w, "NoShadow"), only = findPlacement(w, "ShadowsOnly"), dyn = findPlacement(w, "Dynamic"), lamp = findPlacement(w, "Lamp");
        check(prop(w, none, "cast_shadow") && *prop(w, none, "cast_shadow") == "0", "cast_shadow reaches the node (the node class pushes the shadow mode)");
        check(prop(w, only, "cast_shadow") && *prop(w, only, "cast_shadow") == "3" && prop(w, only, "lod_bias") && prop(w, only, "ignore_occlusion_culling") && prop(w, only, "extra_cull_margin"),
              "shadows only, lod_bias, ignore_occlusion_culling and extra_cull_margin reach the node");
        check(prop(w, dyn, "gi_mode") && *prop(w, dyn, "gi_mode") == "2", "gi_mode reaches the node");
        const std::string* proj0 = prop(w, lamp, "light_projector");
        check(proj0 && proj0->find("Textures/projector.png") != std::string::npos, "light_projector is a resource path under Textures/");
        check(std::filesystem::exists(proj + "/Content/Textures/projector.png", ec), "and the picture was copied there");
        const int cam = findPlacement(w, "Camera");
        const std::string* camEnv = prop(w, cam, "environment");
        check(camEnv && camEnv->find("Object(Environment") != std::string::npos, "a Camera3D's own environment is carried on the node");

        const std::string jsonl = slurp(out + "/FabricReport.jsonl");
        check(ratedAs(jsonl, "MeshInstance3D", "cast_shadow", "approximated"), "cast_shadow off and shadows only are approximated, each with its limits");
        check(ratedAs(jsonl, "MeshInstance3D", "gi_mode", "converted") && ratedAs(jsonl, "MeshInstance3D", "extra_cull_margin", "converted") && ratedAs(jsonl, "MeshInstance3D", "ignore_occlusion_culling", "converted") &&
                  ratedAs(jsonl, "MeshInstance3D", "lod_bias", "converted"),
              "gi_mode, extra_cull_margin, ignore_occlusion_culling and lod_bias are converted");
        check(!ratedAs(jsonl, "MeshInstance3D", "cast_shadow", "unsupported") && !ratedAs(jsonl, "MeshInstance3D", "extra_cull_margin", "unsupported"), "none of them is unsupported any more");
        check(ratedAs(jsonl, "OmniLight3D", "light_projector", "approximated") && ratedAs(jsonl, "OmniLight3D", "light_temperature", "converted"), "the projector is approximated, the temperature rated");
        check(ratedAs(jsonl, "DirectionalLight3D", "directional_shadow_mode", "converted") && ratedAs(jsonl, "DirectionalLight3D", "directional_shadow_max_distance", "converted") &&
                  ratedAs(jsonl, "DirectionalLight3D", "directional_shadow_split_1", "approximated"),
              "the sun's cascade settings are rated: mode and distance converted, the splits fitted");
        check(ratedAs(jsonl, "Environment", "ambient_light_color", "converted") && ratedAs(jsonl, "Environment", "glow_blend_mode", "converted") && ratedAs(jsonl, "Environment", "glow_strength", "converted") &&
                  ratedAs(jsonl, "Environment", "glow_hdr_luminance_cap", "converted") && ratedAs(jsonl, "Environment", "volumetric_fog_length", "converted") &&
                  ratedAs(jsonl, "Environment", "volumetric_fog_anisotropy", "converted") && ratedAs(jsonl, "Environment", "volumetric_fog_temporal_reprojection_amount", "converted"),
              "the glow, ambient and volumetric fog settings are converted");
        check(ratedAs(jsonl, "Environment", "glow_levels/3", "approximated") && ratedAs(jsonl, "Environment", "glow_bloom", "approximated") && ratedAs(jsonl, "Environment", "fog_sun_scatter", "approximated") &&
                  ratedAs(jsonl, "Environment", "volumetric_fog_gi_inject", "approximated") && ratedAs(jsonl, "Environment", "reflected_light_source", "approximated"),
              "what the engine does not model is kept and rated approximated, not unsupported");
        check(ratedAs(jsonl, "Camera3D", "environment", "converted"), "a camera's environment is rated");
        check(reportHas(jsonl, "rendering/anti_aliasing/quality/msaa_3d", "\"rating\":\"approximated\"", "TAA") && reportHas(jsonl, "rendering/anti_aliasing/quality/use_debanding", "\"rating\":\"converted\"") &&
                  reportHas(jsonl, "rendering/mesh_lod/lod_change/threshold_pixels", "\"rating\":\"converted\"") && reportHas(jsonl, "rendering/lights_and_shadows/directional_shadow/size", "RENDER.SHADOWRES"),
              "the project settings are rated and the dropped MSAA is named");
    }

    AVER_INFO("-- Wave4PhysGame: physical light units --");
    {
        const std::string out = (outRoot / "phys").string();
        check(convert(exe.string(), data + "/Wave4PhysGame", out, "", log), "the converter exits 0");
        fmt::OcWorldData w;
        check(fmt::loadOcworld(out + "/Wave4PhysGame/Content/Maps/main.ocworld", w, &err), "the level loads: " + err);
        const double norm = gr::physicalExposureNormalization(8.0, 200.0, 200.0);
        const double lux = gr::physicalAverIntensity(gr::LightKind::Directional, 0.5, 80000.0, norm);
        check(w.hasSun && near(w.sunLux, lux, 1e-5), "the sun is light_energy x lux x the physical camera's normalization at 100000/3 lux a unit (" + std::to_string(w.sunLux) + ")");
        const auto tint = gr::temperatureTint(5000.0);
        check(near(w.sunColor[0], tint[0], 1e-5) && near(w.sunColor[1], tint[1], 1e-5) && near(w.sunColor[2], tint[2], 1e-5), "and tinted by light_temperature 5000 K");
        fmt::ProjectDesc pd;
        check(fmt::loadOcproject(out + "/Wave4PhysGame/Wave4PhysGame.ocproject", pd, &err), "the project loads");
        check(near(pd.postExposure, 1.5, 1e-4), "the camera's exposure_multiplier is the project's exposure (the physical camera's too)");
        const std::string jsonl = slurp(out + "/FabricReport.jsonl");
        check(ratedAs(jsonl, "DirectionalLight3D", "light_intensity_lux", "converted") && ratedAs(jsonl, "OmniLight3D", "light_intensity_lumens", "converted") && ratedAs(jsonl, "OmniLight3D", "light_temperature", "converted"),
              "the light units are rated converted");
        check(ratedAs(jsonl, "CameraAttributesPhysical", "exposure_aperture", "approximated") && ratedAs(jsonl, "CameraAttributesPhysical", "exposure_shutter_speed", "approximated"),
              "the physical exposure is folded into the lights and rated approximated");
        check(reportHas(jsonl, "use_physical_light_units", "\"rating\":\"approximated\""), "and so is the project switch");
    }

    AVER_INFO("-- Wave4SkyGame: the procedural sky is opt-in --");
    {
        const std::string off = (outRoot / "sky-off").string(), on = (outRoot / "sky-on").string();
        check(convert(exe.string(), data + "/Wave4SkyGame", off, "", log), "default conversion exits 0");
        check(convert(exe.string(), data + "/Wave4SkyGame", on, "--procedural-sky", log), "--procedural-sky exits 0");
        fmt::OcWorldData a, b;
        check(fmt::loadOcworld(off + "/Wave4SkyGame/Content/Maps/main.ocworld", a, &err) && fmt::loadOcworld(on + "/Wave4SkyGame/Content/Maps/main.ocworld", b, &err), "both levels load");
        check(!a.skyPhysical && near(a.skyZenith[2], 0.8 * std::pow(1.5, 1.0 / 2.2), 1e-3 * 4) , "off: the sky is the authored two-colour dome, as before");
        check(b.skyPhysical, "on: the level's sky record is the physical sky the shader sits over");
        std::error_code ec2;
        bool shaderOff = false, shaderOn = false;
        for (const auto& e : std::filesystem::directory_iterator(off + "/Wave4SkyGame/Content/Shaders", ec2)) shaderOff = shaderOff || e.path().string().find(".sky.ocgraph") != std::string::npos;
        for (const auto& e : std::filesystem::directory_iterator(on + "/Wave4SkyGame/Content/Shaders", ec2)) shaderOn = shaderOn || e.path().string().find(".sky.ocgraph") != std::string::npos;
        check(!shaderOff, "off: no sky shader is written");
        check(shaderOn, "on: a .sky.ocgraph is written");
        if (shaderOn)
            for (const auto& e : std::filesystem::directory_iterator(on + "/Wave4SkyGame/Content/Shaders", ec2))
                if (e.path().string().find(".sky.ocgraph") != std::string::npos) {
                    const std::string g = slurp(e.path().string());
                    check(g.find("kind=sky") != std::string::npos && g.find("regs=6") != std::string::npos && g.find("3F1A946E") != std::string::npos, "the program is a sky shader with Godot's six parameter registers (1.5 as the sky energy rides in them)");
                }
        const int env = findPlacement(b, "Env");
        const std::string* e = prop(b, env, "environment");
        check(e && e->find(".sky.ocgraph") != std::string::npos && e->find("Object(ProceduralSkyMaterial") == std::string::npos, "on: the node's Sky holds the shader resource, not the material");
        const std::string* e0 = prop(a, findPlacement(a, "Env"), "environment");
        check(e0 && e0->find("Object(ProceduralSkyMaterial") != std::string::npos, "off: the node's Sky holds the ProceduralSkyMaterial as before");
        const std::string jsonlOn = slurp(on + "/FabricReport.jsonl");
        check(reportHas(jsonlOn, "ProceduralSkyMaterial", "opt-in (--procedural-sky)"), "on: the report says it is opt-in and costs at every sky lookup");
    }

    AVER_INFO("-- the default path --");
    {
        // LightsGame's sun states a shadow distance (80 m): that is stated, so the cascades follow it, with Godot's default splits.
        const std::string outL = (outRoot / "lights").string();
        check(convert(exe.string(), data + "/LightsGame", outL, "", log), "LightsGame converts");
        fmt::ProjectDesc pl;
        check(fmt::loadOcproject(outL + "/LightsGame/LightsGame.ocproject", pl, &err), "its project loads");
        check(pl.shadowCascades == 4 && near(pl.shadowDist, 8000.0) && pl.shadowSplit > 0.4 && pl.shadowSplit < 0.65, "a sun that states 80 m has four cascades to 8000 cm and the lambda fitted to Godot's default splits");
        // What a project does not state is a new Aver project's (fmt::applyNewProjectRenderDefaults); what is not among those stays unwritten.
        fmt::ProjectDesc fresh;
        fmt::applyNewProjectRenderDefaults(fresh);
        check(pl.msaa == fresh.msaa && pl.shadowRes < 0 && pl.shadowFilter < 0.0f && pl.volFogLength < 0.0f, "and nothing it did not state beyond a new project's defaults");
        // EnvGame states no rendering setting of this wave: a new project's values for them, and no other key.
        const std::string out = (outRoot / "env").string();
        check(convert(exe.string(), data + "/EnvGame", out, "", log), "EnvGame converts");
        fmt::ProjectDesc pd;
        check(fmt::loadOcproject(out + "/EnvGame/EnvGame.ocproject", pd, &err), "its project loads");
        check(pd.msaa == fresh.msaa && pd.taa == fresh.taa && pd.averSr == fresh.averSr && pd.lodThresholdPx == fresh.lodThresholdPx && pd.occlusionCull == fresh.occlusionCull &&
                  pd.shadowCascades < 0 && pd.shadowSplit < 0.0f && pd.shadowDist < 0.0f && pd.shadowRes < 0 &&
                  pd.shadowFilter < 0.0f && pd.volFogLength < 0.0f && !pd.hasVolFogAniso && pd.volFogTemporal < 0.0f,
              "a project that states none of the new settings has a new project's values for them, and writes no other key");
        fmt::OcWorldData w;
        check(fmt::loadOcworld(out + "/EnvGame/Content/Maps/main.ocworld", w, &err) && w.skyAmbientEnergy < 0.0, "its level has no ambient override");
        check(!w.postVolumes.empty() && postValue(w.postVolumes[0], "glowblend") == 2 && postValue(w.postVolumes[0], "deband", -1) == -1 && postValue(w.postVolumes[0], "glowstrength", -1) == -1 &&
                  postValue(w.postVolumes[0], "glowcap", -1) == -1,
              "and its glow carries Godot's default blend (soft light) and nothing else of the new glow keys");
    }

    AVER_INFO("FabricWave4Test: {} checks, {} failures", g_checks, g_failures);
    return g_failures == 0 ? 0 : 1;
}
