// FabricWave6Test -- wave 6 of the Godot parity plan (particles, decals, sprites) through `AverAssetC fabric`. First the pure mappings of GodotParticles.hpp (the axis maps and their
// determinant, curves and gradients cut to four stops, the sprite sheet, the trail), then the Wave6Game fixture: a GPUParticles3D with a ParticleProcessMaterial (a ring, every range pair,
// a size curve and a colour ramp, a flipbook, colliders and an attractor baked in), a one-shot box emitter that dies on contact, a trail, a CPUParticles3D, a default process material,
// a Decal with the wave 6 fields and an AnimatedSprite3D. Each written .ocparticle is read back with the engine's own parser.
#include "aver/core/ErrorCodes.hpp"
#include "aver/core/Log.hpp"
#include "aver/formats/OcParticle.hpp"
#include "aver/formats/OcWorld.hpp"
#include "aver/formats/godot/GodotParticles.hpp"
#include "aver/particles/ParticleModules.hpp"
#include "aver/platform/FileSystem.hpp"

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <filesystem>
#include <iterator>
#include <sstream>
#include <string>
#include <vector>
#if !defined(_WIN32)
#include <sys/wait.h>
#endif

using namespace aver;
namespace gp = aver::fmt::godotparticles;
namespace pt = aver::particles;

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

static int findPlacement(const fmt::OcWorldData& w, const char* name) {
    for (usize i = 0; i < w.placements.size(); ++i)
        if (w.placements[i].name == name && !w.placements[i].className.empty()) return static_cast<int>(i);
    return -1;
}

static const std::string* component(const fmt::OcWorldData& w, int idx, const char* type, const char* key) {
    if (idx < 0) return nullptr;
    for (const fmt::OcPlacedComponent& c : w.placements[static_cast<usize>(idx)].components)
        if (c.type == type)
            if (const std::string* v = c.find(key)) return v;
    return nullptr;
}

static bool reportHas(const std::string& jsonl, const std::string& a, const std::string& b, const std::string& c = {}) {
    std::istringstream is(jsonl);
    std::string line;
    while (std::getline(is, line))
        if (line.find(a) != std::string::npos && line.find(b) != std::string::npos && line.find(c) != std::string::npos) return true;
    return false;
}

static bool convert(const std::string& exe, const std::string& src, const std::string& out, const char* extra, std::string& log) {
    const int code = runCapture("\"" + exe + "\" fabric \"" + src + "\" \"" + out + "\" --scenes-only " + extra, log);
    if (code != 0) AVER_INFO("{}", log);
    return code == 0;
}

static bool loadEffect(const std::string& path, pt::ParticleSystemDef& def) {
    std::string err;
    const bool ok = fmt::loadOcparticleSystem(path, def, nullptr, &err);
    if (!ok) AVER_ERROR("  {}: {}", path, err);
    return ok && !def.emitters.empty();
}

static std::string effectPathOf(const std::string& content, const fmt::OcWorldData& w, const char* node) {
    const std::string* e = component(w, findPlacement(w, node), "ParticleEmitter", "effect");
    return e ? content + "/" + *e : std::string();
}

static const pt::Module* moduleOf(const pt::ParticleEffect& e, pt::ModuleKind kind, int nth = 0) {
    for (const pt::Module& m : e.modules)
        if (m.kind == kind && nth-- == 0) return &m;
    return nullptr;
}

int main() {
    AVER_INFO("FabricWave6Test");

    // ---- the pure mappings ---------------------------------------------------------------------------------------------------------------
    AVER_INFO("-- axes --");
    {
        const gp::Vec3 ex = gp::direction({1, 0, 0}), ey = gp::direction({0, 1, 0}), ez = gp::direction({0, 0, 1});
        check(near(ex.x, 0) && near(ex.y, 1) && near(ex.z, 0) && near(ey.z, 1) && near(ez.x, -1), "a direction maps (x, y, z) to (-z, x, y): Godot's forward -Z is the engine's +X");
        const double det = ex.x * (ey.y * ez.z - ey.z * ez.y) - ey.x * (ex.y * ez.z - ex.z * ez.y) + ez.x * (ex.y * ey.z - ex.z * ey.y);
        check(near(det, -1.0), "the direction map has determinant -1 (it mirrors)");
        // A rotation axis maps as a pseudovector: cross(map a, map b) == -map(cross(a, b)), so the axis of a Godot spin is rotationAxis(axis).
        const gp::Vec3 a{0.3, -0.5, 0.8}, b{-0.7, 0.2, 0.4};
        const gp::Vec3 ma = gp::direction(a), mb = gp::direction(b);
        const gp::Vec3 crossG{a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x};
        const gp::Vec3 crossE{ma.y * mb.z - ma.z * mb.y, ma.z * mb.x - ma.x * mb.z, ma.x * mb.y - ma.y * mb.x};
        const gp::Vec3 pseudo = gp::rotationAxis(crossG);
        check(near(crossE.x, pseudo.x) && near(crossE.y, pseudo.y) && near(crossE.z, pseudo.z), "cross(map a, map b) is rotationAxis(cross(a, b)): spins and cross products use the pseudovector map");
        const gp::Vec3 e = gp::extents({1, -2, 3});
        check(near(e.x, 3) && near(e.y, 1) && near(e.z, 2), "half-extents map to the engine's local axes, unsigned");
        check(gp::num(-0.0) == "0" && gp::num(0.5) == "0.5" && gp::num(100.0) == "100", "a negative zero is written as 0");
    }

    AVER_INFO("-- curves, gradients, sprite sheets, trails --");
    {
        const std::vector<gp::CurvePoint> pts{{0, 0}, {0.25, 1}, {0.5, 0.2}, {0.75, 0.9}, {1, 0}};
        check(near(gp::sampleCurve(pts, 0.125), 0.5) && near(gp::sampleCurve(pts, -1), 0) && near(gp::sampleCurve(pts, 2), 0), "a curve joins its points by straight lines and clamps outside");
        check(gp::reduceCurve({{0, 1}, {1, 0}}).size() == 2 && gp::reduceCurve({}).empty(), "a short curve keeps its points");
        const std::vector<gp::CurvePoint> red = gp::reduceCurve(pts);
        check(red.size() == 4 && near(red[0].t, 0) && near(red[3].t, 1) && near(red[1].t, 1.0 / 3.0) && near(red[1].v, gp::sampleCurve(pts, 1.0 / 3.0)), "a longer one is sampled at four evenly spaced times (the peaks between are lost)");
        std::vector<gp::GradientStop> g(6);
        for (int i = 0; i < 6; ++i) { g[static_cast<size_t>(i)].t = i / 5.0; g[static_cast<size_t>(i)].rgba[0] = i / 5.0; }
        const std::vector<gp::GradientStop> gr = gp::reduceGradient(g);
        check(gr.size() == 4 && near(gr[3].t, 1) && near(gr[1].rgba[0], 1.0 / 3.0) && near(gr[2].rgba[0], 2.0 / 3.0), "a six stop gradient is sampled at four times");

        const gp::SubUvPlan once = gp::subUvPlan(4, 4, false, 1, 1, 0, 0, 2.0);
        check(once.frames == 16 && !once.perSecond, "a sheet played once over the life (speed 1, offset 0) is the engine's life mode");
        const gp::SubUvPlan loop = gp::subUvPlan(4, 2, true, 2, 4, 0, 0, 2.0);
        check(loop.perSecond && near(loop.fps, 3.0 * 8.0 / 2.0), "a looping sheet at the mean speed 3 over a 2 s life is 12 frames a second");
        const gp::SubUvPlan still = gp::subUvPlan(2, 2, false, 0, 0, 0, 1, 1.0);
        check(still.perSecond && still.fps > 0 && still.fps < 0.001 && near(still.startMax, 1), "a still sheet with a random offset is a near-zero rate with a start frame range");

        const gp::TrailPlan t = gp::trailPlan(0.5, 300, 13);
        check(t.points == 13 && near(t.segmentCm, 12.5), "a 0.5 s trail at 300 cm/s is 150 cm over 12 segments");
        check(gp::trailPlan(1.0, 0, 12).segmentCm >= 1.0 && gp::trailPlan(1, 100, 99).points == 32, "a still particle gets the smallest segment, and the ring is at most 32 points");
    }

    // ---- the Wave6Game fixture -----------------------------------------------------------------------------------------------------------
    const std::string data = AVER_FABRIC_DATA;
    const std::filesystem::path exe = AVER_ASSETC_EXE;
    std::error_code ec;
    check(std::filesystem::exists(exe, ec), "AverAssetC was built: " + exe.string());
    const std::filesystem::path outRoot = std::filesystem::temp_directory_path() / "aver-fabric-wave6-test";
    std::filesystem::remove_all(outRoot, ec);
    std::filesystem::create_directories(outRoot, ec);
    std::string log;
    std::string err;

    AVER_INFO("-- Wave6Game --");
    {
        const std::string out = (outRoot / "game").string();
        check(convert(exe.string(), data + "/Wave6Game", out, "", log), "the converter exits 0");
        const std::string content = out + "/Wave6Game/Content";
        const std::string jsonl = slurp(out + "/FabricReport.jsonl");
        fmt::OcWorldData w;
        check(fmt::loadOcworld(content + "/Maps/main.ocworld", w, &err), "the level loads: " + err);

        // Fountain: GPUParticles3D, ring, every range pair, curves, flipbook, colliders and the attractor.
        {
            pt::ParticleSystemDef def;
            const std::string path = effectPathOf(content, w, "Fountain");
            check(!path.empty() && loadEffect(path, def), "Fountain: a ParticleEmitter component names an effect the engine's parser reads");
            if (!def.emitters.empty()) {
                const pt::ParticleEffect& e = def.emitters[0];
                check(e.shape == pt::EmitterShape::Ring && near(e.shapeSize.x, 200) && near(e.shapeSize.y, 20) && near(e.shapeSize.z, 100) && near(e.shapeAxis.x, 0) && near(e.shapeAxis.y, 0) && near(e.shapeAxis.z, 1),
                      "a ring of 2 m radius, 0.2 m high, 1 m inner, its Y axis the engine's +Z");
                check(e.blend == rhi::BlendMode::Additive && e.lighting == pt::Lighting::Unlit && !e.receivesGI, "the draw material's additive blend and unshaded mode");
                check(near(e.emissionRate, 20) && e.burstCount == 0 && e.maxParticles == 40 && near(e.lifetimeMin, 2) && near(e.lifetimeMax, 2), "40 particles over 2 s is 20 a second, capped at 40");
                check(near(e.speedMin, 200) && near(e.speedMax, 400) && near(e.spreadDeg, 20) && near(e.direction.z, 1), "initial velocity 2 to 4 m/s is 200 to 400 cm/s, up along the engine's Z, spread 20");
                check(near(e.gravity.x, 0) && near(e.gravity.y, 0) && near(e.gravity.z, -980), "gravity -9.8 m/s^2 is 980 cm/s^2 down the engine's Z");
                check(near(e.sizeStart, 50) && e.sizeCurve.n == 3 && near(e.sizeCurve.v[0][0], 25) && near(e.sizeCurve.v[1][0], 50) && near(e.sizeCurve.v[2][0], 0), "the quad is 50 cm and the scale curve (0.5, 1, 0) multiplies it");
                check(e.colorCurve.n == 3 && near(e.colorCurve.v[0][1], 0.8) && near(e.colorCurve.v[1][2], 0.0) && near(e.colorCurve.v[2][3], 0.0), "the colour ramp times the colour (1, 0.8, 0.6), fading to alpha 0");
                check(e.subuvCols == 4 && e.subuvRows == 4 && e.subuvFrames == 16 && e.subuvFps > 0 && e.subuvFps < 0.001 && e.textureId != 0, "the 4 x 4 sprite sheet with a still rate, and its texture");
                const pt::ModuleKind order[] = {pt::ModuleKind::SpawnSizeScale, pt::ModuleKind::SpawnRotation, pt::ModuleKind::SpawnHueVariation, pt::ModuleKind::SpawnAnimStart,
                                                pt::ModuleKind::UpdateLinearAccel, pt::ModuleKind::UpdateRadialAccel, pt::ModuleKind::UpdateTangentialAccel, pt::ModuleKind::UpdateDamping,
                                                pt::ModuleKind::UpdateOrbit, pt::ModuleKind::UpdateCurlNoise, pt::ModuleKind::UpdateCollideSphere, pt::ModuleKind::UpdateCollideBox,
                                                pt::ModuleKind::UpdateCollidePlane, pt::ModuleKind::UpdateAttractor};
                bool inOrder = e.modules.size() == std::size(order);
                for (usize i = 0; inOrder && i < std::size(order); ++i) inOrder = e.modules[i].kind == order[i];
                check(inOrder, "the modules are the sizes, rotation, hue, animation start, the four accelerations, the orbit, the turbulence, then the colliders and the attractor");
                if (const pt::Module* m = moduleOf(e, pt::ModuleKind::SpawnSizeScale)) check(near(m->p[0].value, 0.5) && near(m->p[1].value, 1.5), "scale 0.5 to 1.5");
                if (const pt::Module* m = moduleOf(e, pt::ModuleKind::UpdateLinearAccel)) check(near(m->p[0].value, 100) && near(m->p[1].value, 200), "linear acceleration 1 to 2 m/s^2 is 100 to 200 cm/s^2");
                if (const pt::Module* m = moduleOf(e, pt::ModuleKind::UpdateDamping)) check(near(m->p[0].value, 10) && near(m->p[1].value, 20), "damping 0.1 to 0.2 m/s^2 is 10 to 20");
                if (const pt::Module* m = moduleOf(e, pt::ModuleKind::UpdateTangentialAccel))
                    check(near(m->p[2].value, 0) && near(m->p[3].value, 0) && near(m->p[4].value, 1), "tangential acceleration turns about the gravity axis: Godot's down (0,-1,0) as a spin axis is the engine's +Z");
                if (const pt::Module* m = moduleOf(e, pt::ModuleKind::UpdateOrbit)) check(near(m->p[4].value, -1) && near(m->p[0].value, 0.25), "orbit about Godot's +Y is the engine's -Z (a spin axis), 0.25 to 0.5 revolutions a second");
                if (const pt::Module* m = moduleOf(e, pt::ModuleKind::UpdateCollideSphere))
                    check(near(m->p[0].value, 0) && near(m->p[1].value, 300) && near(m->p[2].value, 100) && near(m->p[3].value, 150) && near(m->p[4].value, 0.6) && near(m->p[5].value, 0.1) && near(m->p[6].value, 0),
                          "the sphere collider at Godot (3, 1, 0) m is the engine's (0, 300, 100) cm, radius 150, bounce 0.6, friction 0.1, rigid");
                if (const pt::Module* m = moduleOf(e, pt::ModuleKind::UpdateCollideBox))
                    check(near(m->p[0].value, -300) && near(m->p[1].value, 0) && near(m->p[2].value, 50) && near(m->p[3].value, 200) && near(m->p[4].value, 100) && near(m->p[5].value, 50),
                          "the box collider (size 2 x 1 x 4 m at (0, 0.5, 3)) is a box of half (200, 100, 50) cm at (-300, 0, 50)");
                if (const pt::Module* m = moduleOf(e, pt::ModuleKind::UpdateCollidePlane)) check(near(m->p[2].value, 1) && near(m->p[3].value, -200), "the height field is a plane at the floor of its box (z = -200 cm)");
                if (const pt::Module* m = moduleOf(e, pt::ModuleKind::UpdateAttractor))
                    check(near(m->p[1].value, -200) && near(m->p[2].value, 100) && near(m->p[3].value, 200) && near(m->p[4].value, 300) && near(m->p[5].value, 2), "the sphere attractor at (-2, 1, 0) m: centre, strength 200, radius 300, falloff 2");
            }
            check(*component(w, findPlacement(w, "Fountain"), "ParticleEmitter", "sim") == "auto" && *component(w, findPlacement(w, "Fountain"), "ParticleEmitter", "playing") == "1", "the emitter plays on the tier the engine picks");
        }

        // Dust: a one-shot box emitter whose particles die on contact, drawn as spheres.
        {
            pt::ParticleSystemDef def;
            check(loadEffect(effectPathOf(content, w, "Dust"), def), "Dust: the effect reads");
            if (!def.emitters.empty()) {
                const pt::ParticleEffect& e = def.emitters[0];
                check(e.shape == pt::EmitterShape::Box && near(e.shapeSize.x, 200) && near(e.shapeSize.y, 100) && near(e.shapeSize.z, 50), "a box of half-extents (1, 0.5, 2) m: the engine's (200, 100, 50) cm");
                check(near(e.emissionRate, 0) && e.burstCount == 12 && e.maxParticles == 12, "one_shot is a burst of amount, no rate");
                check(e.render == pt::RenderKind::Mesh && e.meshPath == "Meshes/sphere.ocmesh" && near(e.sizeStart, 20), "a SphereMesh of radius 0.1 m draws the engine's unit sphere at 20 cm");
                const pt::Module* sph = moduleOf(e, pt::ModuleKind::UpdateCollideSphere);
                check(sph && near(sph->p[6].value, 1), "collision_mode hide on contact kills on the sphere");
                const pt::Module* kill = moduleOf(e, pt::ModuleKind::UpdateKillVolume);
                check(kill && near(kill->p[0].value, 0) && near(kill->p[4].value, 200) && near(kill->p[1].value, -300), "...and a box collider is a kill volume");
                const pt::Module* plane = moduleOf(e, pt::ModuleKind::UpdateCollidePlane);
                check(plane && near(plane->p[6].value, 1), "...and the height field's plane kills");
                check(moduleOf(e, pt::ModuleKind::UpdateCollideBox) == nullptr, "no solid box in hide mode");
            }
        }

        // Streak: a trail draw pass is a ribbon.
        {
            pt::ParticleSystemDef def;
            check(loadEffect(effectPathOf(content, w, "Streak"), def), "Streak: the effect reads");
            if (!def.emitters.empty()) {
                const pt::ParticleEffect& e = def.emitters[0];
                check(e.render == pt::RenderKind::Ribbon && !e.linked && e.trailPoints == 13 && near(e.trailSegmentCm, 12.5) && near(e.trailFade, 1) && near(e.sizeStart, 30),
                      "a RibbonTrailMesh of 4 sections is a 13 point ribbon, 12.5 cm a segment (3 m/s over 0.5 s), 30 cm wide");
            }
            check(reportHas(jsonl, "\"node\":\"Streak\"", "trail_enabled", "\"rating\":\"approximated\""), "the trail is reported approximated");
        }

        // Sparks: a CPUParticles3D under its own property names.
        {
            pt::ParticleSystemDef def;
            check(loadEffect(effectPathOf(content, w, "Sparks"), def), "Sparks: the effect reads");
            if (!def.emitters.empty()) {
                const pt::ParticleEffect& e = def.emitters[0];
                check(e.shape == pt::EmitterShape::Sphere && near(e.shapeSize.x, 50), "emission_sphere_radius 0.5 m");
                check(near(e.lifetimeMin, 0.75) && near(e.lifetimeMax, 1.5) && near(e.emissionRate, 20) && e.maxParticles == 30, "lifetime 1.5 s with randomness 0.5: 0.75 to 1.5 s");
                check(near(e.sizeStart, 20) && e.sizeCurve.n == 2 && near(e.sizeCurve.v[0][0], 20) && near(e.sizeCurve.v[1][0], 0), "scale_amount_curve (a Curve, not a CurveTexture) over the 20 cm quad");
                const pt::Module* sz = moduleOf(e, pt::ModuleKind::SpawnSizeScale);
                check(sz && near(sz->p[0].value, 2) && near(sz->p[1].value, 2), "scale_amount_min and max are the size scale module");
                check(e.colorCurve.n == 4 && near(e.colorCurve.v[0][0], 1) && near(e.colorCurve.v[0][1], 0), "a six stop ramp is cut to four");
                check(e.modules.size() == 1, "a CPUParticles3D takes no collider or attractor (Godot's act on GPUParticles3D only)");
            }
        }

        // Plain: a ParticleProcessMaterial with no property set is Godot's defaults.
        {
            pt::ParticleSystemDef def;
            check(loadEffect(effectPathOf(content, w, "Plain"), def), "Plain: the effect reads");
            if (!def.emitters.empty()) {
                const pt::ParticleEffect& e = def.emitters[0];
                check(e.shape == pt::EmitterShape::Point && near(e.spreadDeg, 45) && near(e.direction.x, 0) && near(e.direction.y, 1) && near(e.direction.z, 0), "Godot's default direction +X is the engine's +Y, spread 45");
                check(near(e.speedMin, 0) && near(e.speedMax, 0) && near(e.gravity.z, -980) && near(e.sizeStart, 100) && e.maxParticles == 8, "initial velocity 0, gravity (0, -9.8, 0), a 1 m quad, 8 particles");
                check(near(e.emissionRate, 8) && near(e.lifetimeMin, 1) && near(e.lifetimeMax, 1), "8 particles over 1 s");
                bool onlyColliders = true;
                for (const pt::Module& m : e.modules) onlyColliders = onlyColliders && (m.kind == pt::ModuleKind::UpdateAttractor);
                check(onlyColliders, "no motion or colour module for values left at their defaults (the scene's attractor is the only module)");
            }
        }

        // The decal and the animated sprite.
        {
            const int mark = findPlacement(w, "Mark");
            check(mark >= 0 && w.placements[static_cast<usize>(mark)].className == "Decal", "the Decal is placed as a Decal");
            const std::string* ufade = component(w, mark, "Props", "upper_fade");
            const std::string* mix = component(w, mark, "Props", "albedo_mix");
            check(ufade && mix && std::stod(*ufade) == 0.5 && std::stod(*mix) == 0.5, "its upper_fade and albedo_mix reach the node");

            const int hero = findPlacement(w, "Hero");
            check(hero >= 0 && w.placements[static_cast<usize>(hero)].className == "AnimatedSprite3D", "the AnimatedSprite3D is placed");
            const std::string* assets = component(w, hero, "Props", "frame_assets");
            check(assets && assets->find("Textures/sheet.png|Meshes/Generated/SpriteQuad_") != std::string::npos && assets->find("Textures/frame_b.png|") != std::string::npos,
                  "frame_assets lists a baked quad and material for each of the two frame textures");
            const std::string* mesh = component(w, hero, "Props", "mesh");
            check(mesh && mesh->find("SpriteQuad_") != std::string::npos, "its own mesh is the quad of the frame it starts on");
            check(component(w, hero, "Props", "material_override") != nullptr, "...with that frame's material");
            check(std::filesystem::exists(content + "/Textures/frame_b.png", ec), "and the frame textures were copied");
        }

        // The report.
        check(reportHas(jsonl, "\"node\":\"Field\"", "GPUParticlesAttractorVectorField3D", "\"rating\":\"unsupported\""), "a vector-field attractor is reported unsupported, once, on its node");
        check(reportHas(jsonl, "\"node\":\"Ground\"", "\"property\":\"baked\"", "\"rating\":\"approximated\""), "a height field collider is reported approximated (a plane stands in)");
        check(reportHas(jsonl, "\"node\":\"Fountain\"", "explosiveness", "\"rating\":\"approximated\""), "explosiveness is reported approximated");
        check(reportHas(jsonl, "\"type\":\"ParticleProcessMaterial\"", "\"rating\":\"converted\""), "the process material resource is reported converted");
        check(reportHas(jsonl, "\"type\":\"SpriteFrames\"", "\"rating\":\"converted\""), "the SpriteFrames resource is reported converted");
        check(reportHas(jsonl, "\"node\":\"Hero\"", "AnimatedSprite3D", "the picture is drawn"), "the AnimatedSprite3D node says its picture is drawn");
        check(reportHas(jsonl, "\"node\":\"Mark\"", "emission and upper / lower / normal fade apply"), "the Decal node says the wave 6 fields apply");
    }

    AVER_INFO("FabricWave6Test: {} checks, {} failures", g_checks, g_failures);
    return g_failures == 0 ? 0 : 1;
}
