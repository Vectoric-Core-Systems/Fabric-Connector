// FabricPlacedTest -- the placed visuals (docs/plans/fabric/G3_SCENES.md "Placed visuals") through the Godot converter: `AverAssetC fabric` on
// tests/fabric/data/PlacedGame, then the level it wrote read back with the engine's own loader. The engine builds no mesh for a class placement (the asset
// column is never read), so what a node draws must be a plain placement below it, or the editor shows nothing until the level plays. Checked: every node that
// draws a mesh has exactly that placement, with the mesh and material the node names, no collision, hidden when the node or an ancestor is; a MultiMesh has one
// per instance in table order; a node that draws nothing has none. Runs the tool as a process, like FabricConvertTest.
#include "aver/core/ErrorCodes.hpp"
#include "aver/core/Log.hpp"
#include "aver/formats/OcWorld.hpp"
#include "aver/formats/godot/GodotVariant.hpp"
#include "aver/platform/FileSystem.hpp"

#include <cmath>
#include <cstdio>
#include <filesystem>
#include <initializer_list>
#include <string>
#include <vector>
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

// The class placement reached by walking names from the top.
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

static const std::string* prop(const fmt::OcWorldData& w, int idx, const char* key) {
    if (idx < 0) return nullptr;
    for (const fmt::OcPlacedComponent& c : w.placements[static_cast<usize>(idx)].components)
        if (c.type == "Props")
            if (const std::string* v = c.find(key)) return v;
    return nullptr;
}

static std::string resourceProp(const fmt::OcWorldData& w, int idx, const char* key) {
    gd::GodotVariant v;
    const std::string* t = prop(w, idx, key);
    gd::GodotDiagnostics d;
    return t && gd::parseGodotVariant(*t, v, &d, key) && v.type == gd::GodotVType::Resource ? v.s : std::string();
}

// The plain placements directly below a placement, in file order.
static std::vector<const fmt::OcWorldPlacement*> visualsOf(const fmt::OcWorldData& w, int idx) {
    std::vector<const fmt::OcWorldPlacement*> out;
    for (const fmt::OcWorldPlacement& p : w.placements)
        if (p.parent == idx && p.className.empty()) out.push_back(&p);
    return out;
}

static std::string stem(const std::string& path) {
    const size_t s = path.find_last_of('/'), d = path.find_last_of('.');
    return path.substr(s == std::string::npos ? 0 : s + 1, d == std::string::npos || (s != std::string::npos && d < s) ? std::string::npos : d - (s == std::string::npos ? 0 : s + 1));
}

int main() {
    AVER_INFO("FabricPlacedTest");
    const std::filesystem::path exe = AVER_ASSETC_EXE;
    const std::string dataDir = std::string(AVER_FABRIC_DATA) + "/PlacedGame";
    std::error_code ec;
    check(std::filesystem::exists(exe, ec), "AverAssetC was built: " + exe.string());
    check(std::filesystem::exists(dataDir + "/project.godot", ec), "the Godot fixture exists: " + dataDir);
    if (g_failures) return exitCode(ExitCode::Failed);

    const std::filesystem::path outRoot = std::filesystem::temp_directory_path() / "aver-fabric-placed-test";
    std::filesystem::remove_all(outRoot, ec);
    std::filesystem::create_directories(outRoot, ec);
    std::string log;
    const int code = runCapture("\"" + exe.string() + "\" fabric \"" + dataDir + "\" \"" + (outRoot / "out").string() + "\" --scenes-only", log);
    check(code == 0, "the converter exits 0 (got " + std::to_string(code) + ")");
    if (code != 0) AVER_INFO("{}", log);

    const std::string content = (outRoot / "out" / "PlacedGame" / "Content").string();
    fmt::OcWorldData w;
    std::string err;
    check(fmt::loadOcworld(content + "/Maps/main.ocworld", w, &err), "the level loads: " + err);

    AVER_INFO("-- a mesh node --");
    {
        const int shown = find(w, {"Main", "Shown"});
        check(shown >= 0 && w.placements[static_cast<usize>(shown)].className == "MeshInstance3D", "Shown is a class placement of MeshInstance3D");
        const std::string mesh = resourceProp(w, shown, "mesh");
        check(!mesh.empty() && mesh.size() > 7 && mesh.compare(mesh.size() - 7, 7, ".ocmesh") == 0, "its mesh Prop names an .ocmesh: " + mesh);
        const std::string* placed = prop(w, shown, "_placed");
        check(placed && *placed == "1", "its Props say one placement below it draws it (_placed=1)");
        const auto v = visualsOf(w, shown);
        check(v.size() == 1, "exactly one plain placement below it (got " + std::to_string(v.size()) + ")");
        if (v.size() == 1) {
            check(v[0]->asset == mesh, "it places the node's mesh: " + v[0]->asset);
            check(!v[0]->collide, "it has no collision of its own");
            check(v[0]->visible, "it is visible");
            check(near(v[0]->x, 0) && near(v[0]->y, 0) && near(v[0]->z, 0) && near(v[0]->yaw, 0) && near(v[0]->pitch, 0) && near(v[0]->roll, 0) &&
                      near(v[0]->sx, 1) && near(v[0]->sy, 1) && near(v[0]->sz, 1),
                  "it sits at the node's origin: the node's own placement carries the transform");
            check(v[0]->material.empty(), "no material override, so the mesh's own materials");
        }
        const fmt::OcWorldPlacement& node = w.placements[static_cast<usize>(shown)];
        check(near(node.x, 0) && near(node.y, 500) && near(node.z, 0) && near(node.sx, 4) && near(node.sy, 2) && near(node.sz, 3),
              "the transform stays on the class placement: Godot (5,0,0) is engine (0,500,0), scale (2,3,4) is (4,2,3)");
        const int child = find(w, {"Main", "Shown", "Child"});
        check(child >= 0 && w.placements[static_cast<usize>(child)].className == "Marker3D", "a real child node of the mesh node is still a class placement beside the visual");
    }

    AVER_INFO("-- material, visibility, nothing to draw --");
    {
        const int tinted = find(w, {"Main", "Tinted"});
        const auto v = visualsOf(w, tinted);
        const std::string mat = resourceProp(w, tinted, "material_override");
        check(v.size() == 1 && !mat.empty() && v[0]->material == stem(mat), "material_override is the placement's material: " + (v.size() ? v[0]->material : std::string()) + " from " + mat);
        const auto hidden = visualsOf(w, find(w, {"Main", "Hidden"}));
        check(hidden.size() == 1 && !hidden[0]->visible, "a node with visible = false places a hidden mesh");
        const auto inner = visualsOf(w, find(w, {"Main", "Group", "Inner"}));
        check(inner.size() == 1 && !inner[0]->visible, "a node under a hidden Node3D places a hidden mesh (Godot hides the subtree)");
        const int empty = find(w, {"Main", "Empty"});
        check(empty >= 0 && visualsOf(w, empty).empty() && !prop(w, empty, "_placed"), "a mesh node with no mesh places nothing");
    }

    AVER_INFO("-- a MultiMesh --");
    {
        const int many = find(w, {"Main", "Many"});
        const std::string* placed = prop(w, many, "_placed");
        check(placed && *placed == "3", "Many: three placements draw it (_placed=3)");
        const auto v = visualsOf(w, many);
        check(v.size() == 3, "one plain placement per instance (got " + std::to_string(v.size()) + ")");
        if (v.size() == 3) {
            gd::GodotVariant mm;
            gd::GodotDiagnostics d;
            const std::string* mmText = prop(w, many, "multimesh");
            check(mmText && gd::parseGodotVariant(*mmText, mm, &d, "multimesh") && mm.type == gd::GodotVType::Object, "the table is still on the node as Object(MultiMesh)");
            std::string meshPath;
            for (usize i = 0; i < mm.keys.size(); ++i)
                if (mm.keys[i].asString() == "mesh") meshPath = mm.items[i].s;
            check(!meshPath.empty() && v[0]->asset == meshPath && v[1]->asset == meshPath && v[2]->asset == meshPath, "each instance places the table's mesh");
            check(!v[0]->collide && !v[1]->collide && !v[2]->collide, "none has collision");
            // Instance 0 is at Godot (1, 0, 0): engine (0, 100, 0). Instance 1 at (0, 2, 0): engine (0, 0, 200), turned a quarter about up and doubled. Instance 2 at (0, 0, 3): engine (-300, 0, 0).
            check(near(v[0]->x, 0) && near(v[0]->y, 100) && near(v[0]->z, 0), "instance 0 went through the axis map");
            check(near(v[1]->x, 0) && near(v[1]->y, 0) && near(v[1]->z, 200) && near(v[1]->sx, 2) && near(v[1]->sy, 2) && near(v[1]->sz, 2) && near(std::fabs(v[1]->yaw), 90, 1e-2) &&
                      near(v[1]->pitch, 0, 1e-2) && near(v[1]->roll, 0, 1e-2),
                  "instance 1: position, uniform scale 2 and a quarter turn about the up axis");
            check(near(v[2]->x, -300) && near(v[2]->y, 0) && near(v[2]->z, 0), "instance 2 too");
            check(v[0]->visible && v[1]->visible && !v[2]->visible, "visible_instance_count = 2 places the third instance hidden");
        }
        const int colored = find(w, {"Main", "Colored"});
        const std::string* cp = prop(w, colored, "_placed");
        check(cp && *cp == "2" && visualsOf(w, colored).size() == 2, "a MultiMesh with colours is placed too (the runtime bakes it, since a placement has no per-instance colour)");
    }

    AVER_INFO("-- the level reads back unchanged --");
    {
        const std::string text = writeOcworld(w);
        fmt::OcWorldData again;
        check(fmt::parseOcworld(text, again, &err) && again.placements.size() == w.placements.size(), "writing and parsing the level gives the same placements: " + err);
        usize classes = 0, plain = 0;
        for (const fmt::OcWorldPlacement& p : again.placements) (p.className.empty() ? plain : classes)++;
        check(plain == 9 && classes == 11, "four mesh nodes, three instances and two instances: nine plain placements below eleven nodes (plain " + std::to_string(plain) + ", class " + std::to_string(classes) + ")");
    }

    AVER_INFO("{} checks, {} failed", g_checks, g_failures);
    return g_failures == 0 ? 0 : 1;
}
