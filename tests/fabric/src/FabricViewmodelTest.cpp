// FabricViewmodelTest -- through the converter: `AverAssetC fabric` on tests/fabric/data/ViewmodelGame, which has a viewmodel shader (its own projection and a
// POSITION.z depth push) and an ordinary vertex shader. The viewmodel's clip-space work is dropped, so every mesh that draws with it (a material_override, a surface
// override, a mesh resource's own material, a model whose import replaces its material) converts as an ordinary mesh: no node carries a first-person flag.
#include "aver/core/ErrorCodes.hpp"
#include "aver/core/Hash.hpp"
#include "aver/core/Log.hpp"
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
    AVER_INFO("FabricViewmodelTest");
    const std::filesystem::path exe = AVER_ASSETC_EXE;
    const std::string dataDir = std::string(AVER_FABRIC_DATA) + "/ViewmodelGame";
    std::error_code ec;
    check(std::filesystem::exists(exe, ec) && std::filesystem::exists(dataDir + "/project.godot", ec), "AverAssetC and the ViewmodelGame fixture exist");
    if (g_failures) return exitCode(ExitCode::Failed);

    const std::filesystem::path root = std::filesystem::temp_directory_path() / "aver-fabric-viewmodel-test";
    std::filesystem::remove_all(root, ec);
    std::filesystem::create_directories(root, ec);
    std::string log;
    const int code = runCapture("\"" + exe.string() + "\" fabric \"" + dataDir + "\" \"" + (root / "out").string() + "\" --scenes-only", log);
    check(code == 0, "the converter exits 0 (got " + std::to_string(code) + ")");
    if (code != 0) AVER_INFO("{}", log);
    const std::string content = (root / "out" / "ViewmodelGame" / "Content").string();

    fmt::OcWorldData w;
    std::string err;
    check(fmt::loadOcworld(content + "/Maps/main.ocworld", w, &err), "the level loads: " + err);

    const auto exists = [&](std::initializer_list<const char*> path) { return find(w, path) >= 0; };
    AVER_INFO("-- the meshes that draw with the viewmodel material, and the ones that do not --");
    check(exists({"Main", "Override"}) && exists({"Main", "Surface"}) && exists({"Main", "OwnMaterial"}) && exists({"Main", "Gun", "GunMesh"}),
          "the four meshes drawn with the viewmodel material are converted");
    check(exists({"Main", "PlainOverride"}) && exists({"Main", "OwnPlain"}) && exists({"Main", "Bare"}) && exists({"Main", "Rock", "RockMesh"}),
          "the ordinary meshes are converted");
    check(propOf(w, {"Main", "Override"}, "material_override").find("viewmodel") != std::string::npos, "material_override keeps the converted viewmodel material");
    int marked = 0;
    for (const fmt::OcWorldPlacement& p : w.placements)
        for (const fmt::OcPlacedComponent& c : p.components)
            if (c.type == "Props" && c.find("aver_first_person")) ++marked;
    check(marked == 0, "no node carries a first-person property (" + std::to_string(marked) + ")");

    AVER_INFO("-- the report --");
    {
        std::string report;
        readFileText((root / "out" / "FabricReport.jsonl").string(), report);
        check(report.find("a viewmodel shader: its own projection and depth push are dropped") != std::string::npos,
              "the report says the shader is approximated as a viewmodel");
        check(report.find("\"unsupported\"") == std::string::npos || report.find("writing POSITION is not supported") == std::string::npos, "and POSITION is not reported unsupported");
    }
    AVER_INFO("FabricViewmodelTest: {} checks, {} failures", g_checks, g_failures);
    return g_failures == 0 ? 0 : exitCode(ExitCode::Failed);
}
