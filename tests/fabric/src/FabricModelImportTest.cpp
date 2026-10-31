// FabricModelImportTest -- a model's own import settings through the Godot converter: `AverAssetC fabric` on tests/fabric/data/ModelGame, whose tri.gltf has an
// import file with nodes/root_scale = 0.5 (applied to the meshes and the node translations, as Godot's apply_root_scale does) and a material the import replaces with
// materials/red.tres; and a body.gltf that uses OMI_physics_body / OMI_physics_shape (a static body, box, trimesh and sphere colliders). Checked on the files the
// converter wrote, read with the engine's own loaders. Runs the tool as a process, like FabricConvertTest.
#include "aver/core/ErrorCodes.hpp"
#include "aver/core/Log.hpp"
#include "aver/formats/OcMesh.hpp"
#include "aver/formats/OcWorld.hpp"
#include "aver/formats/godot/GodotVariant.hpp"
#include "aver/platform/FileSystem.hpp"

#include <cmath>
#include <cstdio>
#include <filesystem>
#include <initializer_list>
#include <string>
#if !defined(_WIN32)
#include <sys/wait.h>
#endif

using namespace aver;
namespace gd = aver::fmt::godot;

static int g_checks = 0;
static int g_failures = 0;

static void check(bool cond, const std::string& what) {
    ++g_checks;
    if (cond) { AVER_INFO("  ok    {}", what); return; }
    ++g_failures;
    AVER_ERROR("  FAIL  {}", what);
}

static bool near(double a, double b, double tol = 1e-3) { return std::fabs(a - b) <= tol; }

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

static int find(const fmt::OcWorldData& w, std::initializer_list<const char*> path) {
    int parent = -1;
    for (const char* name : path) {
        int found = -1;
        for (usize i = 0; i < w.placements.size(); ++i)
            if (w.placements[i].parent == parent && w.placements[i].name == name) { found = static_cast<int>(i); break; }
        if (found < 0) return -1;
        parent = found;
    }
    return parent;
}

int main() {
    AVER_INFO("FabricModelImportTest");
    const std::filesystem::path exe = AVER_ASSETC_EXE;
    const std::string dataDir = std::string(AVER_FABRIC_DATA) + "/ModelGame";
    std::error_code ec;
    check(std::filesystem::exists(exe, ec) && std::filesystem::exists(dataDir + "/project.godot", ec), "AverAssetC and the ModelGame fixture exist");
    if (g_failures) return exitCode(ExitCode::Failed);

    const std::filesystem::path root = std::filesystem::temp_directory_path() / "aver-fabric-model-import-test";
    std::filesystem::remove_all(root, ec);
    std::filesystem::create_directories(root, ec);
    std::string log;
    const int code = runCapture("\"" + exe.string() + "\" fabric \"" + dataDir + "\" \"" + (root / "out").string() + "\" --scenes-only", log);
    check(code == 0, "the converter exits 0 (got " + std::to_string(code) + ")");
    if (code != 0) AVER_INFO("{}", log);
    const std::string content = (root / "out" / "ModelGame" / "Content").string();

    fmt::OcWorldData w;
    std::string err;
    check(fmt::loadOcworld(content + "/Maps/main.ocworld", w, &err), "the level loads: " + err);
    const int tri = find(w, {"Main", "Model", "Tri"});
    check(tri >= 0 && w.placements[static_cast<usize>(tri)].className == "MeshInstance3D", "the model's mesh node is a MeshInstance3D placement");
    std::string meshAsset;
    for (const fmt::OcWorldPlacement& p : w.placements)
        if (p.parent == tri && p.className.empty()) meshAsset = p.asset;
    check(!meshAsset.empty(), "its mesh is a placed visual: " + meshAsset);

    AVER_INFO("-- nodes/root_scale = 0.5 --");
    {
        // Godot's node translation (0.5, 0, 0) is halved by the root scale to (0.25, 0, 0): engine (0, 25, 0).
        const fmt::OcWorldPlacement& p = w.placements[static_cast<usize>(tri)];
        check(near(p.x, 0, 1e-2) && near(p.y, 25, 1e-2) && near(p.z, 0, 1e-2), "the node's translation is scaled: engine (" + std::to_string(p.x) + ", " + std::to_string(p.y) + ", " + std::to_string(p.z) + ")");
        fmt::OcMeshData m;
        check(fmt::loadOcMesh(content + "/" + meshAsset, m, &err) && m.vertexCount() == 3, "the mesh reads back: " + err);
        // The triangle (0,0,0) (1,0,0) (0,1,0) of the file, halved: Godot x and y reach 0.5 m, engine y and z 50 cm.
        f32 hi[3] = {-1e9f, -1e9f, -1e9f};
        for (usize i = 0; i < m.positions.size(); i += 3)
            for (int k = 0; k < 3; ++k) hi[k] = std::fmax(hi[k], m.positions[i + static_cast<usize>(k)]);
        check(near(hi[0], 0, 1e-3) && near(hi[1], 50, 1e-2) && near(hi[2], 50, 1e-2), "the vertices are scaled by the root scale: largest engine y and z " + std::to_string(hi[1]) + ", " + std::to_string(hi[2]));
        check(near(m.boundsMax.y, 50, 1e-2) && near(m.boundsMax.z, 50, 1e-2), "and so are the mesh's bounds");
    }

    AVER_INFO("-- a material the import replaces --");
    {
        fmt::OcMeshData m;
        check(fmt::loadOcMesh(content + "/" + meshAsset, m, &err) && !m.materialSlots.empty(), "the mesh has a material slot");
        const std::string slot = m.materialSlots.empty() ? std::string() : m.materialSlots[0];
        check(slot.find("red") != std::string::npos && slot.find("Paint") == std::string::npos, "the slot names the project's red material, not the file's Paint: " + slot);
        check(fileExists(content + "/Materials/" + slot + ".ocmat"), "and that material was written: Materials/" + slot + ".ocmat");
        std::string text;
        readFileText(content + "/Materials/" + slot + ".ocmat", text);
        check(text.find("PARAM baseColorFactor 1 0 0") != std::string::npos, "it is the red of materials/red.tres");
    }

    AVER_INFO("-- glTF physics (OMI_physics_body / OMI_physics_shape) --");
    {
        const auto classOf = [&](std::initializer_list<const char*> path) { const int i = find(w, path); return i < 0 ? std::string("(missing)") : w.placements[static_cast<usize>(i)].className; };
        check(classOf({"Main", "Physics", "Body"}) == "StaticBody3D", "a node with a static motion is a StaticBody3D (" + classOf({"Main", "Physics", "Body"}) + ")");
        check(classOf({"Main", "Physics", "Body", "Slab"}) == "CollisionShape3D", "a node with a collider is a CollisionShape3D (" + classOf({"Main", "Physics", "Body", "Slab"}) + ")");
        const auto shapeOf = [&](std::initializer_list<const char*> path) {
            gd::GodotVariant v;
            const int i = find(w, path);
            if (i >= 0)
                for (const fmt::OcPlacedComponent& c : w.placements[static_cast<usize>(i)].components)
                    if (c.type == "Props")
                        if (const std::string* t = c.find("shape")) { gd::GodotDiagnostics d; gd::parseGodotVariant(*t, v, &d, "shape"); }
            return v;
        };
        const gd::GodotVariant box = shapeOf({"Main", "Physics", "Body", "Slab"});
        const gd::GodotVariant* size = box.find("size");
        check(box.type == gd::GodotVType::Object && box.s == "BoxShape3D" && size && size->nums.size() == 3 && near(size->nums[0], 2) && near(size->nums[1], 1) && near(size->nums[2], 3),
              "its shape is the file's box, in metres");
        const gd::GodotVariant tri = shapeOf({"Main", "Physics", "Body", "Hull"});
        const gd::GodotVariant* data = tri.find("data");
        check(tri.s == "ConcavePolygonShape3D" && data && data->nums.size() == 9 && near(data->nums[3], 1) && near(data->nums[7], 1), "a trimesh shape holds the mesh's triangle from the file's buffer");
        const gd::GodotVariant ball = shapeOf({"Main", "Physics", "Body", "Ball"});
        const gd::GodotVariant* radius = ball.find("radius");
        check(ball.s == "SphereShape3D" && radius && near(radius->asNumber(), 0.75), "a sphere shape has the file's radius");
        check(classOf({"Main", "Physics", "Body", "Visual"}) == "MeshInstance3D", "the mesh node beside the shapes is still a MeshInstance3D");
    }

    AVER_INFO("-- the report --");
    {
        std::string report;
        readFileText((root / "out" / "FabricReport.jsonl").string(), report);
        check(report.find("nodes/root_scale") != std::string::npos && report.find("is applied to 1 mesh") != std::string::npos, "the root scale is reported as applied");
        check(report.find("_subresources/materials/Paint") != std::string::npos && report.find("project material") != std::string::npos, "the replaced material is reported");
    }

    AVER_INFO("{} checks, {} failed", g_checks, g_failures);
    return g_failures == 0 ? 0 : 1;
}
