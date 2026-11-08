// FabricCanvas2DTest -- 2D scenes (docs/plans/RENDER_2D.md in the engine) through the converter: `AverAssetC fabric` on tests/fabric/data/Menu2DGame (a neutral menu: a canvas layer,
// sprites behind, between and over its buttons, a nested Node2D, flips, a region and a sheet; the PNGs are written here). The Node2D, Sprite2D and CanvasLayer nodes must be placed as their
// classes with the engine's COMPONENT C2DTransform / CSprite2D / C2DLayer records, every record must read back with the engine's own parser, and a 2D class with no equivalent stays a plain node.
#include "aver/core/ErrorCodes.hpp"
#include "aver/core/Log.hpp"
#include "aver/formats/OcWorld.hpp"
#include "aver/platform/FileSystem.hpp"

#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <filesystem>
#include <initializer_list>
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

static bool near(double a, double b, double eps = 1e-4) { return std::fabs(a - b) <= eps; }

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

// A PNG signature and IHDR: the converter reads a texture's size from the header.
static std::vector<u8> pngHeader(u8 w, u8 h) {
    return {0x89, 'P', 'N', 'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 'I', 'H', 'D', 'R', 0, 0, 0, w, 0, 0, 0, h, 8, 6, 0, 0, 0, 0, 0, 0, 0};
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

static const fmt::OcPlacedComponent* record(const fmt::OcWorldData& w, int idx, const char* type) {
    if (idx < 0) return nullptr;
    for (const fmt::OcPlacedComponent& c : w.placements[static_cast<usize>(idx)].components)
        if (c.type == type) return &c;
    return nullptr;
}

static std::string val(const fmt::OcPlacedComponent* r, const char* key) {
    if (!r) return "(no record)";
    const std::string* v = r->find(key);
    return v ? *v : std::string();
}

static double num(const fmt::OcPlacedComponent* r, const char* key, double def = 0.0) {
    const std::string v = val(r, key);
    return v.empty() || v == "(no record)" ? def : std::atof(v.c_str());
}

int main() {
    AVER_INFO("FabricCanvas2DTest");
    const std::filesystem::path exe = AVER_ASSETC_EXE;
    const std::string fixture = std::string(AVER_FABRIC_DATA) + "/Menu2DGame";
    std::error_code ec;
    check(std::filesystem::exists(exe, ec), "AverAssetC was built: " + exe.string());
    check(std::filesystem::exists(fixture + "/project.godot", ec), "the Godot fixture exists: " + fixture);
    if (g_failures) return exitCode(ExitCode::Failed);

    const std::filesystem::path outRoot = std::filesystem::temp_directory_path() / "aver-fabric-canvas2d-test";
    std::filesystem::remove_all(outRoot, ec);
    std::filesystem::create_directories(outRoot, ec);
    const std::string src = (outRoot / "Menu2DGame").string();
    std::filesystem::copy(fixture, src, std::filesystem::copy_options::recursive, ec);
    check(!ec, "the fixture was copied");
    check(put(src + "/art/bg.png", pngHeader(200, 100)) && put(src + "/art/sheet.png", pngHeader(64, 32)), "the PNGs were written");
    const std::string out = (outRoot / "out").string();

    std::string log;
    const int code = runCapture("\"" + exe.string() + "\" fabric \"" + src + "\" \"" + out + "\" --scenes-only", log);
    check(code == 0, "the converter exits 0 (got " + std::to_string(code) + ")");
    if (code != 0) AVER_INFO("{}", log);

    const std::string content = out + "/Menu2DGame/Content";
    const std::string levelPath = content + "/Maps/main.ocworld";
    fmt::OcWorldData w;
    std::string err;
    check(fmt::loadOcworld(levelPath, w, &err), "the level loads with the engine's parser: " + err);

    const int back = find(w, {"Main", "Back"});
    const int menu = find(w, {"Main", "Menu"});
    const int bg = find(w, {"Main", "Menu", "Bg"});
    const int wrap = find(w, {"Main", "Menu", "Wrap"});
    const int logo = find(w, {"Main", "Menu", "Wrap", "Logo"});
    const int crop = find(w, {"Main", "Menu", "Wrap", "Crop"});
    const int mid = find(w, {"Main", "Menu", "Panel", "Mid"});
    const int top = find(w, {"Main", "Menu", "Top"});
    const int ghost = find(w, {"Main", "Menu", "Ghost"});
    const int veil = find(w, {"Main", "Menu", "Veil"});
    const int fx = find(w, {"Main", "Fx"});
    const int play = find(w, {"Main", "Menu", "Panel", "Play"});
    check(back >= 0 && menu >= 0 && bg >= 0 && wrap >= 0 && logo >= 0 && crop >= 0 && mid >= 0 && top >= 0 && ghost >= 0 && veil >= 0 && fx >= 0 && play >= 0, "every node of the fixture is a placement");
    auto cls = [&](int i) { return i < 0 ? std::string() : w.placements[static_cast<usize>(i)].className; };
    check(cls(back) == "Sprite2D" && cls(wrap) == "Node2D" && cls(menu) == "CanvasLayer" && cls(play) == "Button", "Sprite2D, Node2D and CanvasLayer are placed as their classes");
    check(cls(fx) == "Node", "a TileMapLayer, which has no equivalent, stays a plain node");
    check(w.placements[static_cast<usize>(back)].asset == "none" && w.placements[static_cast<usize>(menu)].asset == "none", "a 2D placement has no mesh");

    // ---- the layer ----
    const auto* layer = record(w, menu, "C2DLayer");
    check(layer && near(num(layer, "index"), 2) && near(num(layer, "x"), 8) && near(num(layer, "y"), 4) && val(layer, "hidden").empty(), "CanvasLayer: index 2, offset (8, 4), visible");
    check(!record(w, menu, "C2DTransform") && !record(w, menu, "CSprite2D"), "a layer has no transform or sprite record");

    // ---- transforms ----
    const auto* backT = record(w, back, "C2DTransform");
    check(backT && near(num(backT, "x"), 640) && near(num(backT, "y"), 360) && near(num(backT, "z"), -5) && val(backT, "zabsolute").empty(), "Sprite2D: position and a relative z_index");
    const auto* wrapT = record(w, wrap, "C2DTransform");
    check(wrapT && near(num(wrapT, "x"), 100) && near(num(wrapT, "y"), 50) && near(num(wrapT, "rotation"), 0.5) && near(num(wrapT, "sx"), 2) && near(num(wrapT, "sy"), 3) && near(num(wrapT, "z"), 1),
          "Node2D: position, rotation (radians), scale and z_index");
    check(!record(w, wrap, "CSprite2D"), "a Node2D has no sprite record");
    const auto* topT = record(w, top, "C2DTransform");
    check(topT && near(num(topT, "z"), 5) && val(topT, "zabsolute") == "1", "z_as_relative off becomes zabsolute");
    const auto* cropT = record(w, crop, "C2DTransform");
    check(cropT && val(cropT, "toplevel") == "1", "top_level becomes toplevel");
    check(val(record(w, ghost, "C2DTransform"), "hidden") == "1" && val(record(w, bg, "C2DTransform"), "hidden").empty(), "visible = false becomes hidden");

    // The tree order is the node's place in the tree, counted from 1, so a later node is higher.
    const double oBack = num(backT, "order"), oBg = num(record(w, bg, "C2DTransform"), "order"), oLogo = num(record(w, logo, "C2DTransform"), "order"), oMid = num(record(w, mid, "C2DTransform"), "order");
    const double oTop = num(topT, "order");
    check(oBack >= 1 && oBg > oBack && oLogo > oBg && oMid > oLogo && oTop > oMid, "the tree order counts up in the tree: Back < Bg < Logo < Mid < Top (" + std::to_string(static_cast<int>(oBack)) + "," + std::to_string(static_cast<int>(oBg)) + "," +
                                                                                      std::to_string(static_cast<int>(oLogo)) + "," + std::to_string(static_cast<int>(oMid)) + "," + std::to_string(static_cast<int>(oTop)) + ")");

    // ---- sprites ----
    const auto* backS = record(w, back, "CSprite2D");
    check(backS && val(backS, "texture") == "Textures/art/bg.png", "the texture is the copied file's content path");
    check(std::filesystem::exists(content + "/Textures/art/bg.png", ec) && std::filesystem::exists(content + "/Textures/art/sheet.png", ec), "both textures were copied under Textures/");
    const auto* bgS = record(w, bg, "CSprite2D");
    check(bgS && val(bgS, "centered") == "0" && val(bgS, "filter") == "nearest", "centered = false and texture_filter nearest");
    const auto* logoS = record(w, logo, "CSprite2D");
    check(logoS && near(num(logoS, "hframes"), 4) && near(num(logoS, "vframes"), 2) && near(num(logoS, "frame"), 5) && val(logoS, "fliph") == "1" && near(num(logoS, "ox"), 4) && val(logoS, "flipv").empty(),
          "a sheet: hframes, vframes, frame, flip_h and offset");
    // modulate (1, .5, .25, .5) times self_modulate (1, 1, 1, .5): alpha .25 -> 0x40, green .5 -> 0x80, blue .25 -> 0x40.
    check(logoS && val(logoS, "modulate") == "ff804040", "modulate times self_modulate, as RRGGBBAA: " + val(logoS, "modulate"));
    const auto* cropS = record(w, crop, "CSprite2D");
    check(cropS && near(num(cropS, "rx"), 8) && near(num(cropS, "ry"), 8) && near(num(cropS, "rw"), 32) && near(num(cropS, "rh"), 16) && val(cropS, "flipv") == "1", "region_rect and flip_v");
    const auto* midS = record(w, mid, "CSprite2D");
    check(midS && near(num(midS, "frame"), 6) && near(num(midS, "hframes"), 4), "frame_coords (2, 1) in a 4 x 2 sheet is frame 6 (row 1 * 4 + column 2)");
    check(val(record(w, veil, "CSprite2D"), "hidden") == "1" && val(record(w, veil, "CSprite2D"), "modulate").empty(), "a fully transparent modulate is written as hidden: the engine reads modulate 0 as white");

    // ---- the file round trip ----
    const std::string text = slurp(levelPath);
    check(text.find("COMPONENT C2DLayer") != std::string::npos && text.find("COMPONENT C2DTransform") != std::string::npos && text.find("COMPONENT CSprite2D") != std::string::npos,
          "the level file holds the three record types");
    std::string saved;
    fmt::OcWorldData again;
    const std::string rewritten = outRoot.string() + "/again.ocworld";
    check(fmt::saveOcworld(rewritten, w, &err) && fmt::loadOcworld(rewritten, again, &err), "the level saves and loads again: " + err);
    check(again.placements.size() == w.placements.size() && val(record(again, logo, "CSprite2D"), "modulate") == val(logoS, "modulate"), "the records survive a save");

    // ---- the report ----
    const std::string report = slurp(out + "/FabricReport.jsonl");
    auto reported = [&](const std::string& a, const std::string& b, const std::string& c = std::string()) {
        size_t pos = 0;
        while (pos < report.size()) {
            size_t end = report.find('\n', pos);
            if (end == std::string::npos) end = report.size();
            const std::string line = report.substr(pos, end - pos);
            if (line.find(a) != std::string::npos && line.find(b) != std::string::npos && (c.empty() || line.find(c) != std::string::npos)) return true;
            pos = end + 1;
        }
        return false;
    };
    check(reported("\"type\":\"Sprite2D\"", "\"rating\":\"approximated\"", "CSprite2D"), "a Sprite2D is rated approximated, with its limits in the reason");
    check(reported("\"type\":\"Node2D\"", "\"rating\":\"converted\"", "C2DTransform"), "a Node2D is rated converted");
    check(reported("\"type\":\"CanvasLayer\"", "\"rating\":\"approximated\"", "C2DLayer"), "a CanvasLayer names its record");
    check(reported("\"type\":\"TileMapLayer\"", "\"rating\":\"unsupported\"", "no equivalent yet"), "a TileMapLayer is unsupported, with the reason");

    AVER_INFO("==================================================");
    if (g_failures == 0) AVER_INFO("=== {} checks, 0 failed ===", g_checks);
    else AVER_ERROR("=== {} checks, {} FAILED ===", g_checks, g_failures);
    return g_failures == 0 ? exitCode(ExitCode::Ok) : exitCode(ExitCode::Failed);
}
