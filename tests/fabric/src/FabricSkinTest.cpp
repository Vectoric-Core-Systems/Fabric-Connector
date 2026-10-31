// FabricSkinTest -- wave 7 through the Godot converter: `AverAssetC fabric` on tests/fabric/data/SkinGame, then the level and the assets it wrote read back.
// A skinned glTF model must come out with its Skeleton3D bound to the importer's .ocskel (the aver_skeleton property) and its skinned mesh marked, with the
// file's three animations (LINEAR, CUBICSPLINE and STEP) written as property clips on bone paths in the engine's axes, and a scene's PhysicalBone3D nodes
// must become the skeleton's .ocragdoll. An Animation resource's markers must be saved with its clip.
#include "aver/core/ErrorCodes.hpp"
#include "aver/core/Log.hpp"
#include "aver/formats/OcAnim.hpp"
#include "aver/formats/OcMesh.hpp"
#include "aver/formats/OcWorld.hpp"
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

static std::string propOf(const fmt::OcWorldData& w, std::initializer_list<const char*> path, const char* key) {
    const int i = find(w, path);
    if (i < 0) return "(missing node)";
    for (const fmt::OcPlacedComponent& c : w.placements[static_cast<usize>(i)].components)
        if (c.type == "Props")
            if (const std::string* t = c.find(key)) return *t;
    return "(missing property)";
}

int main() {
    AVER_INFO("FabricSkinTest");
    const std::filesystem::path exe = AVER_ASSETC_EXE;
    const std::string dataDir = std::string(AVER_FABRIC_DATA) + "/SkinGame";
    std::error_code ec;
    check(std::filesystem::exists(exe, ec) && std::filesystem::exists(dataDir + "/project.godot", ec), "AverAssetC and the SkinGame fixture exist");
    if (g_failures) return exitCode(ExitCode::Failed);

    const std::filesystem::path root = std::filesystem::temp_directory_path() / "aver-fabric-skin-test";
    std::filesystem::remove_all(root, ec);
    std::filesystem::create_directories(root, ec);
    std::string log;
    const int code = runCapture("\"" + exe.string() + "\" fabric \"" + dataDir + "\" \"" + (root / "out").string() + "\" --scenes-only", log);
    check(code == 0, "the converter exits 0 (got " + std::to_string(code) + ")");
    if (code != 0) AVER_INFO("{}", log);
    const std::string content = (root / "out" / "SkinGame" / "Content").string();
    const std::string skelAsset = "Meshes/models/skin/models_skin.ocskel";

    fmt::OcWorldData w;
    std::string err;
    check(fmt::loadOcworld(content + "/Maps/main.ocworld", w, &err), "the level loads: " + err);

    AVER_INFO("-- the skeleton and the skinned mesh --");
    {
        fmt::OcSkeleton sk;
        check(fmt::loadOcSkel(content + "/" + skelAsset, sk, &err) && sk.bones.size() == 2, "the importer's skeleton has the two joints: " + err);
        const std::string bound = propOf(w, {"Main", "Hero", "Armature", "Skeleton3D"}, "aver_skeleton");
        check(bound.find(skelAsset) != std::string::npos, "the Skeleton3D is bound to it: " + bound);
        check(propOf(w, {"Main", "Hero", "Armature", "Skeleton3D", "Body"}, "aver_skinned").find("true") != std::string::npos, "the skinned mesh below it is marked: " +
              propOf(w, {"Main", "Hero", "Armature", "Skeleton3D", "Body"}, "aver_skinned"));
        const std::string meshProp = propOf(w, {"Main", "Hero", "Armature", "Skeleton3D", "Body"}, "mesh");
        fmt::OcMeshData m;
        const std::string meshAsset = "Meshes/models/skin/models_skin.ocmesh";
        check(fmt::loadOcMesh(content + "/" + meshAsset, m, &err) && m.hasSkin(), "the mesh carries its skinning weights");
    }

    AVER_INFO("-- the animations the file carries --");
    {
        const std::string libs = propOf(w, {"Main", "Hero", "AnimationPlayer"}, "libraries");
        check(libs.find("Rise") != std::string::npos && libs.find("Spin") != std::string::npos && libs.find("Pop") != std::string::npos, "the model's AnimationPlayer holds the three clips: " + libs);

        fmt::OcAnimation rise;
        check(fmt::loadOcAnim(content + "/Animations/models_skin_Rise.ocanim", rise, &err) && rise.propClip && rise.propTracks.size() == 1, "Rise is a property clip of one track: " + err);
        if (!rise.propTracks.empty()) {
            const fmt::OcPropTrack& t = rise.propTracks[0];
            check(t.type == fmt::OcPropTrackType::Position3D && t.path == "Armature/Skeleton3D:Spine", "on the Spine bone, by its path from the model root: " + t.path);
            check(t.keys.size() == 2 && near(t.keys[0].time, 0) && near(t.keys[1].time, 1) && t.interp == fmt::OcPropInterp::Linear, "with the file's own two keys, linear");
            check(near(t.keys[1].value.n[0], 0) && near(t.keys[1].value.n[1], 0) && near(t.keys[1].value.n[2], 200), "the last key is 2 m up, in the engine's centimetres and axes (0, 0, 200)");
            check(near(rise.duration, 1.0), "its length is the file's");
        }

        fmt::OcAnimation spin;
        check(fmt::loadOcAnim(content + "/Animations/models_skin_Spin.ocanim", spin, &err) && spin.propTracks.size() == 1, "Spin is one track: " + err);
        if (!spin.propTracks.empty()) {
            const fmt::OcPropTrack& t = spin.propTracks[0];
            check(t.type == fmt::OcPropTrackType::Rotation3D && t.path == "Armature/Skeleton3D:Hips", "a rotation on the Hips");
            check(t.keys.size() == 31, "the cubic-spline channel is sampled at 30 frames per second: 31 keys, got " + std::to_string(t.keys.size()));
            if (t.keys.size() == 31) {
                check(near(t.keys[0].value.n[3], 1.0) && near(t.keys[0].value.n[2], 0.0), "it starts at the identity");
                // glTF (0, s, 0, c) is the engine's (z, -x, -y, w) = (0, 0, -s, c) at the last key.
                const double s = std::sin(3.14159265358979 / 4);
                check(near(t.keys[30].value.n[2], -s) && near(t.keys[30].value.n[3], s), "and ends at the quarter turn about Y, in the engine's axes");
                // Zero tangents make the Hermite a smoothstep: halfway is half the turn (22.5 degrees of the 45 in the quaternion's half angle).
                check(near(t.keys[15].value.n[2], -std::sin(3.14159265358979 / 8)) && near(t.keys[15].value.n[3], std::cos(3.14159265358979 / 8)), "halfway through it is half the way round");
            }
        }

        fmt::OcAnimation pop;
        check(fmt::loadOcAnim(content + "/Animations/models_skin_Pop.ocanim", pop, &err) && pop.propTracks.size() == 1, "Pop is one track: " + err);
        if (!pop.propTracks.empty()) {
            const fmt::OcPropTrack& t = pop.propTracks[0];
            check(t.type == fmt::OcPropTrackType::Scale3D && t.interp == fmt::OcPropInterp::Nearest && t.keys.size() == 2, "a STEP scale channel holds its key (nearest)");
            check(near(t.keys[1].value.n[0], 2) && near(t.keys[1].value.n[1], 2) && near(t.keys[1].value.n[2], 2), "with the second key's scale");
        }
    }

    AVER_INFO("-- the ragdoll the scene authors --");
    {
        std::string text;
        check(readFileText(content + "/Meshes/models/skin/models_skin.ocragdoll", text), "the skeleton has an .ocragdoll");
        check(text.find("bone Hips on") != std::string::npos && text.find("radius 20.0000") != std::string::npos, "the Hips part has the capsule's radius in centimetres");
        check(text.find("mass 12.0000") != std::string::npos && text.find("friction 0.5000") != std::string::npos && text.find("bounce 0.2500") != std::string::npos, "and its mass, friction and bounce");
        check(text.find("bone Spine on") != std::string::npos && text.find("joint swingtwist limits 25.0000 25.0000 15.0000") != std::string::npos, "the Spine part is a cone joint with the scene's spans");
        check(text.find("joint ball") != std::string::npos, "a bone with no joint type is free to turn");
    }

    AVER_INFO("-- markers --");
    {
        fmt::OcAnimation marks;
        check(fmt::loadOcAnim(content + "/Animations/main_marks.ocanim", marks, &err), "the Animation resource's clip loads: " + err);
        check(marks.markers.size() == 2, "its two markers are saved with it");
        const fmt::OcMarker* hit = marks.marker("hit");
        check(hit && near(hit->time, 1.0) && near(hit->color[0], 1.0) && near(hit->color[1], 0.0), "hit: a time and the red colour");
        const fmt::OcMarker* end = marks.marker("end");
        check(end && near(end->time, 2.5), "end: its time");
    }

    AVER_INFO("-- the report --");
    {
        std::string report;   // converted rows are in the JSON lines, only the approximated and unsupported ones in the markdown
        readFileText((root / "out" / "FabricReport.jsonl").string(), report);
        check(report.find("bound to Meshes/models/skin/models_skin.ocskel") != std::string::npos, "the report says the skeleton is bound");
        check(report.find("3 glTF animation(s)") != std::string::npos, "and how many animations were read");
        check(report.find("2 marker(s) saved with the clip") != std::string::npos, "and the markers");
        check(report.find("physical bone(s) written to") != std::string::npos, "and the ragdoll file");
    }

    std::filesystem::remove_all(root, ec);
    if (g_failures == 0) AVER_INFO("=== {} assertions, 0 failed ===", g_checks);
    else AVER_ERROR("=== {} assertions, {} FAILED ===", g_checks, g_failures);
    return exitCode(g_failures ? ExitCode::Failed : ExitCode::Ok);
}
