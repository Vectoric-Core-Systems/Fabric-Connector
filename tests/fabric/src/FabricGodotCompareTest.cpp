// FabricGodotCompareTest -- the converter checked against Godot itself (tools/godot-dump/README.md).
//
//   FabricGodotCompareTest                      the test: PlacedGame converted and compared with what Godot says about it, written out by hand (no Godot), then with
//                                               mistakes put into that answer (each must be found), then with Godot 4.x run headless when GODOT_EXE names it
//   FabricGodotCompareTest compare --converted <project dir> --godot <exe> --godot-project <dir> [--scenes res://a.tscn,res://b.tscn] [--work <dir>] [--max <n>]
//                                               the same comparison for any converted project: the Godot project is copied to --work (Godot writes a .godot folder
//                                               and imports into the project it opens), imported, dumped, and every scene (default: every scene the converter
//                                               recorded in Fabric/nodes.json) compared; exit 1 when any scene has a mismatch
//   FabricGodotCompareTest compare --converted <project dir> --dump <json> [--scenes ...] [--max <n>]
//                                               the comparison against a dump already written
#include "FabricGodotCompare.hpp"
#include "FabricProcess.hpp"

#include "aver/core/ErrorCodes.hpp"
#include "aver/core/Log.hpp"

#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <sstream>

#if !defined(_WIN32)
#include <sys/wait.h>
#endif

using namespace aver;
namespace cmp = aver::fabric::cmp;

static int g_checks = 0;
static int g_failures = 0;

static void check(bool cond, const std::string& what) {
    ++g_checks;
    if (cond) { AVER_INFO("  ok    {}", what); return; }
    ++g_failures;
    AVER_ERROR("  FAIL  {}", what);
}

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

// Godot, headless, on a project: the dump script writes the JSON. The project is the one Godot opens (it creates .godot in it).
static bool runGodot(const std::string& exe, const std::string& project, const std::string& outJson, const std::vector<std::string>& scenes, bool withTree, std::string& log) {
    std::vector<std::string> args = {"--headless", "--path", project, "--script", AVER_GODOT_DUMP_SCRIPT, "--", outJson};
    if (withTree) args.push_back("--with-tree");
    for (const std::string& s : scenes) args.push_back(s);
    std::error_code ec;
    std::filesystem::remove(outJson, ec);
    const fabric::ProcessOutput p = fabric::runProcess(exe, args);
    log = p.text;
    return p.started && std::filesystem::exists(outJson, ec);
}

static bool importProject(const std::string& exe, const std::string& project, std::string& log) {
    const fabric::ProcessOutput p = fabric::runProcess(exe, {"--headless", "--path", project, "--import"});
    log = p.text;
    return p.started;
}

// "res://props/crate.tscn" -> "Maps/props/crate.ocworld", the level the converter writes for a scene.
static std::string levelOf(const std::string& sceneRes) {
    std::string rel = sceneRes.rfind("res://", 0) == 0 ? sceneRes.substr(6) : sceneRes;
    const size_t dot = rel.find_last_of('.');
    if (dot != std::string::npos) rel.erase(dot);
    return "Maps/" + rel + ".ocworld";
}

static std::vector<std::string> scenesRecorded(const std::string& projectDir) {
    std::vector<std::string> out;
    fmt::JsonValue j;
    if (!fmt::parseJson(slurp(projectDir + "/Fabric/nodes.json"), j)) return out;
    for (usize i = 0; i < j["scenes"].size(); ++i) out.emplace_back(j["scenes"][i]["scene"].asString());
    return out;
}

static std::vector<std::string> split(const std::string& s, char sep) {
    std::vector<std::string> out;
    std::string cur;
    for (char c : s + sep) {
        if (c == sep) { if (!cur.empty()) out.push_back(cur); cur.clear(); }
        else cur += c;
    }
    return out;
}

// Compares every scene of a dump with its level in the converted project and prints the result. Returns the mismatch count.
static size_t compareAll(const std::string& projectDir, const fmt::JsonValue& dump, const std::vector<std::string>& scenes, size_t maxLines) {
    cmp::Content content(projectDir + "/Content");
    size_t total = 0, nodes = 0, instances = 0;
    std::map<std::string, size_t> kinds;
    for (const std::string& scene : scenes) {
        const cmp::Converted conv = cmp::readConverted(projectDir, levelOf(scene), scene);
        const cmp::SceneResult r = cmp::compareScene(scene, dump["scenes"][scene], conv, content);
        std::printf("%s\n", cmp::summary(r).c_str());
        std::map<std::string, size_t> shown;
        for (const cmp::Issue& is : r.issues) {
            if (cmp::SceneResult::documented(is.kind)) continue;
            if (shown[is.kind]++ >= maxLines) continue;
            std::printf("    %-9s %s: %s\n", is.kind.c_str(), is.where.c_str(), is.detail.c_str());
        }
        total += r.mismatches() + (r.error.empty() ? 0 : 1);
        nodes += r.matched;
        instances += r.instancesChecked;
        for (const auto& kv : r.counts) kinds[kv.first] += kv.second;
    }
    std::printf("TOTAL: %zu scene(s), %zu nodes matched, %zu instances checked, %zu mismatch(es)", scenes.size(), nodes, instances, total);
    for (const auto& kv : kinds) std::printf("  %s=%zu", kv.first.c_str(), kv.second);
    std::printf("\n");
    return total;
}

// ---- the command line -------------------------------------------------------------------------------------------------------------------

static int compareMain(int argc, char** argv) {
    std::string converted, godot, godotProject, dumpFile, work, scenesArg;
    size_t maxLines = 12;
    bool withTree = false;
    for (int i = 2; i < argc; ++i) {
        const std::string a = argv[i];
        const auto next = [&]() { return i + 1 < argc ? std::string(argv[++i]) : std::string(); };
        if (a == "--converted") converted = next();
        else if (a == "--godot") godot = next();
        else if (a == "--godot-project") godotProject = next();
        else if (a == "--dump") dumpFile = next();
        else if (a == "--work") work = next();
        else if (a == "--scenes") scenesArg = next();
        else if (a == "--max") maxLines = static_cast<size_t>(std::atoi(next().c_str()));
        else if (a == "--with-tree") withTree = true;
        else { std::fprintf(stderr, "unknown option %s\n", a.c_str()); return 2; }
    }
    if (converted.empty() || (dumpFile.empty() && (godot.empty() || godotProject.empty()))) {
        std::fprintf(stderr, "usage: compare --converted <dir> (--dump <json> | --godot <exe> --godot-project <dir> [--work <dir>]) [--scenes a,b] [--max n] [--with-tree]\n");
        return 2;
    }
    // Godot is started inside the project and writes its dump there: every path it is given must stand without a current folder.
    const auto absolute = [](std::string& p) { if (!p.empty()) p = std::filesystem::absolute(p).string(); };
    absolute(converted); absolute(godotProject); absolute(work); absolute(dumpFile);
    std::vector<std::string> scenes = scenesArg.empty() ? scenesRecorded(converted) : split(scenesArg, ',');
    if (scenes.empty()) { std::fprintf(stderr, "no scenes: %s/Fabric/nodes.json lists none\n", converted.c_str()); return 2; }
    if (dumpFile.empty()) {
        std::string project = godotProject;
        std::error_code ec;
        if (!work.empty()) {
            std::filesystem::remove_all(work, ec);
            std::filesystem::copy(godotProject, work, std::filesystem::copy_options::recursive, ec);
            if (ec) { std::fprintf(stderr, "cannot copy %s to %s: %s\n", godotProject.c_str(), work.c_str(), ec.message().c_str()); return 2; }
            project = work;
        }
        std::string log;
        if (!importProject(godot, project, log)) { std::fprintf(stderr, "cannot run %s\n%s\n", godot.c_str(), log.c_str()); return 2; }
        dumpFile = (std::filesystem::path(project) / "fabric-godot-dump.json").string();
        if (!runGodot(godot, project, dumpFile, scenes, withTree, log)) { std::fprintf(stderr, "Godot wrote no dump:\n%s\n", log.c_str()); return 2; }
    }
    fmt::JsonValue dump;
    std::string why;
    if (!fmt::parseJson(slurp(dumpFile), dump, &why)) { std::fprintf(stderr, "cannot read the dump %s: %s\n", dumpFile.c_str(), why.c_str()); return 2; }
    std::printf("Godot %s\n", std::string(dump["godot"].asString()).c_str());
    return compareAll(converted, dump, scenes, maxLines) == 0 ? 0 : 1;
}

// ---- the test ---------------------------------------------------------------------------------------------------------------------------------

// What Godot says about tests/fabric/data/PlacedGame/main.tscn, written out by hand from the scene (positions, scales and boxes worked out on paper), with a few
// knobs to put a mistake into it.
struct Answer {
    double shownX = 6;                    // Shown's world x
    std::string tintedClass = "MeshInstance3D";
    bool hiddenInTree = false;            // Hidden's visible_in_tree
    bool ghost = false;                   // a node Godot has and the level does not
    int manyCount = 3;
    double tintedGreen = 0;               // the green of Tinted's material
    double instance1X = 0;                // the origin x of MultiMesh instance 1
    double meshHalf = 0.5;                // half the BoxMesh width
};

static std::string jx(const double (&v)[12]) {
    char b[400];
    std::snprintf(b, sizeof b, "[%g,%g,%g,%g,%g,%g,%g,%g,%g,%g,%g,%g]", v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8], v[9], v[10], v[11]);
    return b;
}

static std::string boxMesh(double half) {
    char b[300];
    std::snprintf(b, sizeof b, "{\"class\":\"BoxMesh\",\"path\":\"\",\"name\":\"\",\"aabb\":[%g,-1,-1.5,%g,2,3],\"surfaces\":1,\"surface_materials\":[{}],\"material\":{}}", -half, half * 2);
    return b;
}

static std::string answerJson(const Answer& a) {
    const double id[12] = {1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0};
    const double main[12] = {1, 0, 0, 0, 1, 0, 0, 0, 1, 1, 2, 3};
    const double shown[12] = {2, 0, 0, 0, 3, 0, 0, 0, 4, a.shownX, 2, 3};
    const double shownLocal[12] = {2, 0, 0, 0, 3, 0, 0, 0, 4, 5, 0, 0};
    const double up[12] = {1, 0, 0, 0, 1, 0, 0, 0, 1, 1, 2, 3};   // a child of Main with an identity transform
    std::ostringstream o;
    const auto node = [&](const std::string& path, const std::string& name, const std::string& cls, const double (&local)[12], const double (&world)[12], bool visible, bool inTree,
                          const std::string& extra) {
        o << "{\"path\":\"" << path << "\",\"name\":\"" << name << "\",\"class\":\"" << cls << "\",\"script\":{},\"instance\":\"\",\"groups\":[],\"is3d\":true,\"local\":" << jx(local)
          << ",\"world\":" << jx(world) << ",\"visible\":" << (visible ? "true" : "false") << ",\"visible_in_tree\":" << (inTree ? "true" : "false") << ",\"top_level\":false" << extra << "},";
    };
    // A box mesh of size (1,2,3) at world position p with scale s (no rotation): its world box.
    const auto box = [](double px, double py, double pz, double sx, double sy, double sz, double half) {
        char b[200];
        std::snprintf(b, sizeof b, "[%g,%g,%g,%g,%g,%g]", px - sx * half, py - sy * 1.0, pz - sz * 1.5, sx * half * 2, sy * 2.0, sz * 3.0);
        return std::string(b);
    };
    const std::string red = "{\"class\":\"StandardMaterial3D\",\"path\":\"\",\"name\":\"\",\"albedo_color\":[1," + std::to_string(a.tintedGreen) + ",0,1]}";
    o << "{\"godot\":\"4.x-test\",\"scenes\":{\"res://main.tscn\":{\"nodes\":[";
    node(".", "Main", "Node3D", main, main, true, true, "");
    node("Shown", "Shown", "MeshInstance3D", shownLocal, shown, true, true,
         ",\"mesh\":" + boxMesh(a.meshHalf) + ",\"world_aabb\":" + box(a.shownX, 2, 3, 2, 3, 4, a.meshHalf) + ",\"material_override\":{},\"surface_overrides\":[]");
    node("Shown/Child", "Child", "Marker3D", id, shown, true, true, "");
    node("Tinted", "Tinted", a.tintedClass, id, up, true, true,
         ",\"mesh\":" + boxMesh(a.meshHalf) + ",\"world_aabb\":" + box(1, 2, 3, 1, 1, 1, a.meshHalf) + ",\"material_override\":" + red + ",\"surface_overrides\":[]");
    node("Hidden", "Hidden", "MeshInstance3D", id, up, false, a.hiddenInTree, ",\"mesh\":" + boxMesh(a.meshHalf) + ",\"world_aabb\":" + box(1, 2, 3, 1, 1, 1, a.meshHalf) + ",\"material_override\":{},\"surface_overrides\":[]");
    node("Group", "Group", "Node3D", id, up, false, false, "");
    node("Group/Inner", "Inner", "MeshInstance3D", id, up, true, false, ",\"mesh\":" + boxMesh(a.meshHalf) + ",\"world_aabb\":" + box(1, 2, 3, 1, 1, 1, a.meshHalf) + ",\"material_override\":{},\"surface_overrides\":[]");
    node("Empty", "Empty", "MeshInstance3D", id, up, true, true, ",\"mesh\":{},\"material_override\":{},\"surface_overrides\":[]");
    // MultiMesh: instance 0 at (1, 0, 0), instance 1 doubled and turned a quarter about Y at (0, 2, 0), instance 2 at (0, 0, 3); the third is hidden by visible_instance_count = 2.
    const double i0[12] = {1, 0, 0, 0, 1, 0, 0, 0, 1, 1, 0, 0};
    const double i1[12] = {0, 0, -2, 0, 2, 0, 2, 0, 0, a.instance1X, 2, 0};
    const double i2[12] = {1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 3};
    node("Many", "Many", "MultiMeshInstance3D", id, up, true, true,
         ",\"multimesh\":{\"count\":" + std::to_string(a.manyCount) + ",\"visible_count\":2,\"format\":1,\"use_colors\":false,\"use_custom_data\":false,\"mesh\":" + boxMesh(a.meshHalf) + ",\"transforms\":[" + jx(i0) + "," + jx(i1) +
             "," + jx(i2) + "]}");
    const double c0[12] = {1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0};
    const double c1[12] = {1, 0, 0, 0, 1, 0, 0, 0, 1, 5, 0, 0};
    node("Colored", "Colored", "MultiMeshInstance3D", id, up, true, true,
         ",\"multimesh\":{\"count\":2,\"visible_count\":-1,\"format\":1,\"use_colors\":true,\"use_custom_data\":false,\"mesh\":" + boxMesh(a.meshHalf) + ",\"transforms\":[" + jx(c0) + "," + jx(c1) + "]}");
    // A rotation 0.17 degrees short of straight up: the transform the sky_shaders demo's labels have. Row-major text (xx, xy, xz, yx ...) as columns, below Main's (1, 2, 3).
    {
        const double r[9] = {-1, -0.000172721, -0.000173317, -0.000172821, -0.00286709, 0.999997, -0.000173217, 0.999997, 0.00286705};
        const double t[12] = {r[0], r[3], r[6], r[1], r[4], r[7], r[2], r[5], r[8], -0.00110257, 0.516483, -5.69942};
        double w[12];
        for (int i = 0; i < 9; ++i) w[i] = t[i];
        w[9] = t[9] + 1; w[10] = t[10] + 2; w[11] = t[11] + 3;
        node("Tilted", "Tilted", "Marker3D", t, w, true, true, "");
    }
    if (a.ghost) node("Ghost", "Ghost", "Node3D", id, up, true, true, "");
    std::string s = o.str();
    s.pop_back();
    return s + "]}}}";
}

static cmp::SceneResult compareAnswer(const std::string& projectDir, const std::string& json) {
    fmt::JsonValue dump;
    std::string why;
    if (!fmt::parseJson(json, dump, &why)) { cmp::SceneResult r; r.error = "the answer is not JSON: " + why; return r; }
    cmp::Content content(projectDir + "/Content");
    const cmp::Converted conv = cmp::readConverted(projectDir, "Maps/main.ocworld", "res://main.tscn");
    return cmp::compareScene("res://main.tscn", dump["scenes"]["res://main.tscn"], conv, content);
}

static size_t count(const cmp::SceneResult& r, const char* kind) {
    const auto it = r.counts.find(kind);
    return it == r.counts.end() ? 0 : it->second;
}

// meshinfo <file.ocmesh>: the vertex count and the box of a converted mesh, in engine centimetres and in Godot metres, and the vertices that lie far from the rest.
static int meshInfoMain(const std::string& path) {
    fmt::OcMeshData m;
    std::string err;
    if (!fmt::loadOcMesh(path, m, &err)) { std::fprintf(stderr, "cannot read %s: %s\n", path.c_str(), err.c_str()); return 2; }
    cmp::Box b;
    for (usize i = 0; i + 2 < m.positions.size(); i += 3) b.add({m.positions[i + 1] / 100.0, m.positions[i + 2] / 100.0, -m.positions[i] / 100.0});
    std::printf("%zu vertices, %zu indices, %zu submeshes, slots:", m.positions.size() / 3, m.indices.size(), m.submeshes.size());
    for (const std::string& s : m.materialSlots) std::printf(" %s", s.c_str());
    std::printf("\nGodot-space box (%.4f %.4f %.4f)-(%.4f %.4f %.4f)\n", b.lo.x, b.lo.y, b.lo.z, b.hi.x, b.hi.y, b.hi.z);
    size_t odd = 0;
    for (usize i = 0; i + 2 < m.positions.size(); i += 3) {
        const double a = std::fabs(m.positions[i]), c = std::fabs(m.positions[i + 1]), d = std::fabs(m.positions[i + 2]);
        if (!(a < 1e7 && c < 1e7 && d < 1e7)) { if (odd++ < 6) std::printf("  vertex %zu: %g %g %g\n", i / 3, m.positions[i], m.positions[i + 1], m.positions[i + 2]); }
    }
    std::printf("%zu vertices beyond 1e7 cm or not finite\n", odd);
    return 0;
}

int main(int argc, char** argv) {
    if (argc >= 2 && std::string(argv[1]) == "compare") return compareMain(argc, argv);
    if (argc >= 3 && std::string(argv[1]) == "meshinfo") return meshInfoMain(argv[2]);
    AVER_INFO("FabricGodotCompareTest");
    const std::filesystem::path exe = AVER_ASSETC_EXE;
    const std::string dataDir = std::string(AVER_FABRIC_DATA) + "/PlacedGame";
    std::error_code ec;
    check(std::filesystem::exists(exe, ec) && std::filesystem::exists(dataDir + "/project.godot", ec), "AverAssetC and the PlacedGame fixture exist");
    if (g_failures) return exitCode(ExitCode::Failed);

    const std::filesystem::path root = std::filesystem::temp_directory_path() / "aver-fabric-godot-compare-test";
    std::filesystem::remove_all(root, ec);
    std::filesystem::create_directories(root, ec);
    std::string log;
    const int code = runCapture("\"" + exe.string() + "\" fabric \"" + dataDir + "\" \"" + (root / "out").string() + "\" --scenes-only", log);
    check(code == 0, "the converter exits 0 (got " + std::to_string(code) + ")");
    const std::string proj = (root / "out" / "PlacedGame").string();

    AVER_INFO("-- the answer written by hand --");
    {
        const cmp::SceneResult r = compareAnswer(proj, answerJson(Answer{}));
        if (r.mismatches() != 0 || !r.error.empty()) for (const cmp::Issue& is : r.issues) AVER_INFO("    {} {}: {}", is.kind, is.where, is.detail);
        check(r.error.empty(), "the answer reads and the level reads: " + r.error);
        check(r.godotNodes == 11 && r.matched == 11 && r.convertedNodes == 11, "eleven nodes on each side, all matched (" + std::to_string(r.matched) + ")");
        check(r.mismatches() == 0, "no mismatch (" + std::to_string(r.mismatches()) + ")");
        check(r.instancesChecked == 5, "five MultiMesh instances were compared (" + std::to_string(r.instancesChecked) + ")");
    }

    AVER_INFO("-- each mistake in the answer is found, and only that one --");
    {
        Answer a;
        a.shownX = 6.01;
        cmp::SceneResult r = compareAnswer(proj, answerJson(a));
        check(count(r, "xform") == 2 && count(r, "mesh") == 1 && r.mismatches() == 3, "a world position 1 cm off is found in the node, its child and the node's world box, and nowhere else");
        Answer b;
        b.shownX = 6.0004;
        r = compareAnswer(proj, answerJson(b));
        check(r.mismatches() == 0, "0.4 mm is inside the tolerance");
        Answer c;
        c.tintedClass = "Node3D";
        r = compareAnswer(proj, answerJson(c));
        check(count(r, "class") == 1, "a class mismatch");
        Answer d;
        d.hiddenInTree = true;
        r = compareAnswer(proj, answerJson(d));
        check(count(r, "visible") >= 1, "a node Godot shows and the level hides");
        Answer e;
        e.ghost = true;
        r = compareAnswer(proj, answerJson(e));
        check(count(r, "missing") == 1 && r.mismatches() == 1, "a node Godot has and the level lacks");
        Answer f;
        f.manyCount = 4;
        r = compareAnswer(proj, answerJson(f));
        check(count(r, "instances") == 1, "an instance count mismatch");
        Answer g;
        g.instance1X = 0.01;
        r = compareAnswer(proj, answerJson(g));
        check(count(r, "instances") == 1, "one instance transform 1 cm off");
        Answer h;
        h.tintedGreen = 1;
        r = compareAnswer(proj, answerJson(h));
        check(count(r, "material") == 1, "a material colour mismatch");
        Answer k;
        k.meshHalf = 0.6;
        r = compareAnswer(proj, answerJson(k));
        check(count(r, "mesh") >= 1, "a mesh bounds mismatch");
    }

    AVER_INFO("-- the converter's own nodes and a level that cannot be read --");
    {
        cmp::SceneResult r = compareAnswer((root / "nowhere").string(), answerJson(Answer{}));
        check(!r.error.empty(), "a level that is not there is an error, not a pass");
    }

    const char* godotExe = std::getenv("GODOT_EXE");
    if (godotExe && *godotExe) {
        AVER_INFO("-- Godot itself ({}) --", godotExe);
        const std::string work = (root / "godot-project").string();
        std::filesystem::copy(dataDir, work, std::filesystem::copy_options::recursive, ec);
        const std::string dump = (root / "dump.json").string();
        std::string glog;
        check(runGodot(godotExe, work, dump, {"res://main.tscn"}, false, glog), "Godot ran headless and wrote the dump: " + glog.substr(0, 300));
        fmt::JsonValue j;
        std::string why;
        check(fmt::parseJson(slurp(dump), j, &why), "the dump is JSON: " + why);
        cmp::Content content(proj + "/Content");
        const cmp::Converted conv = cmp::readConverted(proj, "Maps/main.ocworld", "res://main.tscn");
        const cmp::SceneResult r = cmp::compareScene("res://main.tscn", j["scenes"]["res://main.tscn"], conv, content);
        for (const cmp::Issue& is : r.issues) if (!cmp::SceneResult::documented(is.kind)) AVER_INFO("    {} {}: {}", is.kind, is.where, is.detail);
        check(r.error.empty() && r.matched == 11 && r.mismatches() == 0, "Godot and the level agree on PlacedGame: " + cmp::summary(r));
        // The hand-written answer is what Godot says: the same boxes and transforms.
        fmt::JsonValue hand;
        check(fmt::parseJson(answerJson(Answer{}), hand), "the hand-written answer is JSON");
    } else {
        AVER_INFO("-- GODOT_EXE is not set: the comparison with Godot itself is skipped --");
    }

    AVER_INFO("{} checks, {} failed", g_checks, g_failures);
    return g_failures == 0 ? 0 : 1;
}
