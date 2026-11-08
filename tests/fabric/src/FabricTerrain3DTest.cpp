// FabricTerrain3DTest -- a Terrain3D node (the GDExtension addon) through the converter, on tests/fabric/data/TerrainGame: three generated Terrain3DRegion resources
// (Terrain3DFixtures.hpp; one of them RSCC-compressed) become .ocland sections with the right size, spacing, origin, heights and paint, one LANDSCAPE record each, and a
// layered terrain material from the texture list; what the engine's landscape cannot hold is in the report. Runs the tool as a process, like FabricCanvas2DTest.
#include "Terrain3DFixtures.hpp"

#include "aver/core/ErrorCodes.hpp"
#include "aver/core/Log.hpp"
#include "aver/formats/AxisMap.hpp"
#include "aver/formats/OcLand.hpp"
#include "aver/formats/OcWorld.hpp"
#include "aver/platform/FileSystem.hpp"
#if AVER_FABRIC_TEST_OCMAT
#include "aver/formats/OcMat.hpp"
#endif

#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <filesystem>
#include <string>
#include <vector>
#if !defined(_WIN32)
#include <sys/wait.h>
#endif

using namespace aver;

static int g_checks = 0;
static int g_failures = 0;

static void check(bool cond, const std::string& what) {
    ++g_checks;
    if (cond) { AVER_INFO("  ok    {}", what); return; }
    ++g_failures;
    AVER_ERROR("  FAIL  {}", what);
}

static bool near(double a, double b, double eps = 1e-3) { return std::fabs(a - b) <= eps; }

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

static bool put(const std::string& path, const std::vector<u8>& bytes) {
    std::filesystem::create_directories(std::filesystem::path(path).parent_path());
    return writeFileBytes(path, bytes.data(), bytes.size());
}

int main() {
    AVER_INFO("FabricTerrain3DTest");
    const std::filesystem::path exe = AVER_ASSETC_EXE;
    const std::string fixture = std::string(AVER_FABRIC_DATA) + "/TerrainGame";
    std::error_code ec;
    check(std::filesystem::exists(exe, ec), "AverAssetC was built: " + exe.string());
    check(std::filesystem::exists(fixture + "/project.godot", ec), "the Godot fixture exists: " + fixture);
    if (g_failures) return exitCode(ExitCode::Failed);

    const std::filesystem::path outRoot = std::filesystem::temp_directory_path() / "aver-fabric-terrain3d-test";
    std::filesystem::remove_all(outRoot, ec);
    std::filesystem::create_directories(outRoot, ec);
    const std::string src = (outRoot / "TerrainGame").string();
    std::filesystem::copy(fixture, src, std::filesystem::copy_options::recursive, ec);
    check(!ec, "the fixture was copied");
    // Terrain3D names a region file by its location: '_' for a positive coordinate, '-' for a negative one.
    check(put(src + "/terrain_data/terrain3d_00_00.res", t3dfix::region(0, 0, false)) && put(src + "/terrain_data/terrain3d_01_00.res", t3dfix::region(1, 0, true)) &&
              put(src + "/terrain_data/terrain3d_00-01.res", t3dfix::region(0, -1, false)) && put(src + "/terrain_data/terrain3d_05_05.res", t3dfix::stray()) &&
              put(src + "/textures/rock.png", t3dfix::png(4, 4, 90, 80, 70)),
          "the regions and the texture were written");
    const std::string out = (outRoot / "out").string();

    std::string log;
    const int code = runCapture("\"" + exe.string() + "\" fabric \"" + src + "\" \"" + out + "\" --scenes-only", log);
    check(code == 0, "the converter exits 0 (got " + std::to_string(code) + ")");
    if (code != 0) AVER_INFO("{}", log);

    const std::string content = out + "/TerrainGame/Content";
    fmt::OcWorldData w;
    std::string err;
    check(fmt::loadOcworld(content + "/Maps/main.ocworld", w, &err), "the level loads with the engine's parser: " + err);

    // ---- the node and the extension ----
    int terrain = -1;
    for (usize i = 0; i < w.placements.size(); ++i)
        if (w.placements[i].name == "Terrain") terrain = static_cast<int>(i);
    check(terrain >= 0 && w.placements[static_cast<usize>(terrain)].className == "Node3D", "the Terrain3D node is a plain Node3D, not a GDExtension node");
    check(!std::filesystem::exists(content + "/addons", ec), "the addon's files are not copied into the content");

    // ---- the LANDSCAPE records: nearest the origin first, then by row and column ----
    check(w.landscapes.size() == 3, "three LANDSCAPE records (the stray resource is not a region): " + std::to_string(w.landscapes.size()));
    const char* names[3] = {"Terrain_xp00_yp00", "Terrain_xp00_yn01", "Terrain_xp01_yp00"};
    const double at[3][2] = {{-12800, 0}, {0, 0}, {-12800, 12800}};
    for (usize i = 0; i < 3 && i < w.landscapes.size(); ++i) {
        const fmt::OcLandscapePlacement& lp = w.landscapes[i];
        check(lp.name == names[i] && lp.section == std::string("Landscape/main/") + names[i] + ".ocland", "record " + std::to_string(i) + " names " + names[i] + ": " + lp.section);
        check(near(lp.x, at[i][0]) && near(lp.y, at[i][1]) && near(lp.z, 0.0) && near(lp.extentCm, 12800), "record " + std::to_string(i) + " sits at its region's corner (" + std::to_string(lp.x) + ", " + std::to_string(lp.y) + ")");
        check(lp.material == "TerrainGame_Terrain_terrain", "record " + std::to_string(i) + " names the terrain material: " + lp.material);
    }

    // ---- the sections ----
    fmt::OcLandData a, b, c;
    std::string why;
    const bool la = fmt::loadOcLand(content + "/Landscape/main/Terrain_xp00_yp00.ocland", a, &why);
    const bool lb = fmt::loadOcLand(content + "/Landscape/main/Terrain_xp01_yp00.ocland", b, &why);
    const bool lc = fmt::loadOcLand(content + "/Landscape/main/Terrain_xp00_yn01.ocland", c, &why);
    check(la && lb && lc, "the three .ocland files load with the engine's parser: " + why);
    if (!(la && lb && lc)) return exitCode(ExitCode::Failed);
    check(a.sampleCount == 65 && b.sampleCount == 65 && c.sampleCount == 65, "a 64 pixel region is a 65 sample section (its edge row is the neighbour's first pixel)");
    check(near(a.spacingCm, 200) && near(b.spacingCm, 200) && near(c.spacingCm, 200), "2 m vertex spacing is 200 cm");
    check(near(a.originCm[0], -12800) && near(a.originCm[1], 0) && near(b.originCm[0], -12800) && near(b.originCm[1], 12800) && near(c.originCm[0], 0) && near(c.originCm[1], 0),
          "the origins are the regions' corners through the axis map");
    check(near(a.extentCm(), 12800), "a section spans exactly R * spacing");
    const auto h = [](const fmt::OcLandData& d, u32 ix, u32 iy) { return static_cast<double>(d.heightAt(ix, iy)); };
    const double tol = 0.5;   // heights are stored in 16 bits across the section's own range
    check(near(h(a, 64, 0), 1000, tol), "A (64, 0) is pixel (0, 0): 10 m");
    check(near(h(a, 32, 20), 1232, tol), "A (32, 20) is pixel (20, 32): 12.32 m (iy runs along Godot +X, ix against Godot +Z)");
    check(near(h(a, 0, 10), 1163, tol), "A (0, 10) repeats the last row where no region lies beyond it");
    check(near(h(a, 64, 64), 3000, tol), "A (64, 64) is the east neighbour's first pixel: 30 m");
    check(near(h(a, 0, 64), 3126, tol), "A (0, 64) takes the diagonal fallback to the east neighbour's last row");
    check(near(h(b, 64, 0), 3000, tol) && near(h(b, 10, 30), 3708, tol), "B reads its own ramp");
    check(near(h(c, 54, 10), 500, tol) && near(h(c, 14, 10), 6200, tol), "C is flat then steep");
    check(near(h(c, 0, 0), 1000, tol), "C (0, 0) is the region beyond its +Z edge: A's first pixel, 10 m");
    {
        // A sample's world position is the axis map of its Godot position.
        f32 world[3];
        a.worldAt(32, 20, world);
        const Vec3 expect = fmt::axis::position(40.0f, 0.0f, 64.0f);
        check(near(world[0], expect.x) && near(world[1], expect.y), "A (32, 20) is at the engine position of Godot (40, y, 64)");
        c.worldAt(14, 10, world);
        const Vec3 expectC = fmt::axis::position(20.0f, 0.0f, -128.0f + 100.0f);
        check(near(world[0], expectC.x) && near(world[1], expectC.y), "C (14, 10) is at the engine position of Godot (20, y, -28)");
    }

    // ---- the paint ----
    const std::vector<std::string> layers = {"Texture0", "Rock_stone", "Sand"};
    check(a.layerNames == layers && b.layerNames == layers && c.layerNames == layers && a.layersValid(), "three layers by texture id; the unused fourth texture is not a layer");
    check(a.weightAt(1, 32, 10) == 40 && a.weightAt(0, 32, 10) == 215 && a.weightAt(2, 32, 10) == 0, "A: base 0, overlay 1, blend 40 at pixel (10, 32)");
    check(a.weightAt(1, 32, 20) == 80, "A: the blend ramps with the pixel column");
    check(b.weightAt(2, 10, 30) == 127 && b.weightAt(0, 10, 30) == 128 && b.weightAt(1, 10, 30) == 0, "B: base 2 and overlay 0 at blend 128");
    check(c.weightAt(2, 54, 10) == 255 && c.weightAt(0, 54, 10) == 0, "C: flat ground takes the auto base texture");
    check(c.weightAt(1, 14, 10) >= 220 && c.weightAt(1, 14, 10) <= 230 && c.weightAt(2, 14, 10) + c.weightAt(1, 14, 10) == 255, "C: steep ground takes mostly the auto overlay texture");

    // ---- the material and textures ----
#if AVER_FABRIC_TEST_OCMAT
    pbr::MaterialDesc d;
    fmt::OcMatExtras extras;
    check(fmt::loadOcmat(content + "/Materials/TerrainGame_Terrain_terrain.ocmat", d, &extras, &err), "the terrain material loads: " + err);
    check(d.terrain.layers.size() == 3 && d.terrain.layers[0].name == "Texture0" && d.terrain.layers[1].name == "Rock_stone" && d.terrain.layers[2].name == "Sand", "the material has the three layers in order");
    check(d.terrain.layers.size() == 3 && near(d.terrain.layers[0].tilingCm, 1000) && near(d.terrain.layers[1].tilingCm, 400), "uv_scale 0.1 is 1000 cm a tile and 0.25 is 400 cm");
    check(d.terrain.layers.size() == 3 && d.terrain.layers[1].baseColor.path == "Textures/textures/rock.png" && d.terrain.layers[0].baseColor.path.rfind("Textures/Generated/", 0) == 0,
          "a picture file is copied and an image kept in the scene is written as a PNG");
    check(near(d.terrain.blendDepth, 0.25, 1e-4), "blend_sharpness 0.5 maps to a blend depth of 0.25");
#endif
    check(std::filesystem::exists(content + "/Textures/textures/rock.png", ec), "the texture file is under Textures/");

    // ---- the report ----
    const std::string report = slurp(out + "/FabricReport.jsonl");
    auto reported = [&](const std::string& a1, const std::string& b1, const std::string& c1 = std::string()) {
        size_t pos = 0;
        while (pos < report.size()) {
            size_t end = report.find('\n', pos);
            if (end == std::string::npos) end = report.size();
            const std::string line = report.substr(pos, end - pos);
            if (line.find(a1) != std::string::npos && line.find(b1) != std::string::npos && (c1.empty() || line.find(c1) != std::string::npos)) return true;
            pos = end + 1;
        }
        return false;
    };
    check(reported("\"type\":\"GDExtension\"", "\"rating\":\"approximated\"", "converted onto the engine's landscape"), "the addon's .gdextension is reported as mapped, not hosted");
    check(reported("\"type\":\"Terrain3D\"", "\"rating\":\"converted\"", "3 region(s)"), "the node reports its three regions");
    check(reported("\"type\":\"Terrain3DRegion\"", "\"rating\":\"converted\"", "Landscape/main/Terrain_xp01_yp00.ocland"), "each region reports its section");
    check(reported("\"type\":\"BoxShape3D\"", "\"rating\":\"unsupported\"", "not a Terrain3DRegion"), "a resource that is not a region is reported and skipped");
    check(reported("\"property\":\"control_map\"", "\"rating\":\"unsupported\"", "1 texel(s) are holes"), "holes are reported");
    check(reported("\"property\":\"control_map\"", "\"rating\":\"unsupported\"", "navigation flag"), "the navigation flag is reported");
    check(reported("\"property\":\"color_map\"", "\"rating\":\"unsupported\"", "tint"), "a colour map is reported dropped");
    check(reported("\"property\":\"instancer\"", "\"rating\":\"unsupported\"", "2 instanced mesh transform"), "the instancer is reported dropped, with its count");
    check(reported("\"property\":\"assets\"", "\"rating\":\"unsupported\"", "Terrain3DMeshAsset"), "the mesh list is reported dropped");
    check(reported("\"property\":\"collision_mode\"", "\"rating\":\"approximated\"", "PhysicsBridge"), "collision names the engine landscape's heightfield");
    check(reported("\"property\":\"mesh_lods\"", "\"rating\":\"approximated\""), "the level-of-detail settings are approximated");
    check(reported("\"property\":\"show_heightmap\"", "\"rating\":\"unsupported\""), "a debug view is unsupported");
    check(reported("\"property\":\"vertical_projection\"", "\"rating\":\"unsupported\""), "a texture's vertical projection is unsupported");
    check(reported("\"property\":\"macro variation\"", "\"rating\":\"unsupported\""), "the material's macro variation is unsupported");
    check(reported("\"property\":\"world_background\"", "\"rating\":\"unsupported\""), "the world background is unsupported");
    check(reported("\"property\":\"auto_shader\"", "\"rating\":\"approximated\""), "the auto shader is approximated");
    check(reported("\"type\":\"Terrain3D\"", "\"rating\":\"approximated\"", "first LANDSCAPE record"), "the runtime's one-section limit is reported");

    AVER_INFO("==================================================");
    if (g_failures == 0) AVER_INFO("=== {} checks, 0 failed ===", g_checks);
    else AVER_ERROR("=== {} checks, {} FAILED ===", g_checks, g_failures);
    return g_failures == 0 ? exitCode(ExitCode::Ok) : exitCode(ExitCode::Failed);
}
