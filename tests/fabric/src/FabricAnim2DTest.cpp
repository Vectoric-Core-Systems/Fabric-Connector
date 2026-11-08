// FabricAnim2DTest -- AnimatedSprite2D, Camera2D, Parallax2D, ParallaxBackground and ParallaxLayer (docs/plans/RENDER_2D.md in the engine) through the converter: `AverAssetC fabric` on
// tests/fabric/data/Anim2DGame (a neutral scene: an animated sprite with two animations, an atlas frame, two cameras, a parallax with a sprite, an old parallax background with a layer;
// the PNGs are written here). The nodes must be placed as their classes with the engine's COMPONENT CSpriteAnim2D / CCamera2D / CParallax2D / C2DLayer records, every record must read
// back with the engine's own parser, and what the engine cannot do must be in the report with its reason.
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
    AVER_INFO("FabricAnim2DTest");
    const std::filesystem::path exe = AVER_ASSETC_EXE;
    const std::string fixture = std::string(AVER_FABRIC_DATA) + "/Anim2DGame";
    std::error_code ec;
    check(std::filesystem::exists(exe, ec), "AverAssetC was built: " + exe.string());
    check(std::filesystem::exists(fixture + "/project.godot", ec), "the Godot fixture exists: " + fixture);
    if (g_failures) return exitCode(ExitCode::Failed);

    const std::filesystem::path outRoot = std::filesystem::temp_directory_path() / "aver-fabric-anim2d-test";
    std::filesystem::remove_all(outRoot, ec);
    std::filesystem::create_directories(outRoot, ec);
    const std::string src = (outRoot / "Anim2DGame").string();
    std::filesystem::copy(fixture, src, std::filesystem::copy_options::recursive, ec);
    check(!ec, "the fixture was copied");
    check(put(src + "/art/hero.png", pngHeader(32, 16)) && put(src + "/art/sky.png", pngHeader(64, 32)), "the PNGs were written");
    const std::string out = (outRoot / "out").string();

    std::string log;
    const int code = runCapture("\"" + exe.string() + "\" fabric \"" + src + "\" \"" + out + "\" --scenes-only", log);
    check(code == 0, "the converter exits 0 (got " + std::to_string(code) + ")");
    if (code != 0) AVER_INFO("{}", log);

    const std::string content = out + "/Anim2DGame/Content";
    const std::string levelPath = content + "/Maps/main.ocworld";
    fmt::OcWorldData w;
    std::string err;
    check(fmt::loadOcworld(levelPath, w, &err), "the level loads with the engine's parser: " + err);

    const int hero = find(w, {"Main", "World", "Hero"});
    const int cam = find(w, {"Main", "World", "Hero", "Cam"});
    const int spare = find(w, {"Main", "World", "Spare"});
    const int sky = find(w, {"Main", "World", "Sky"});
    const int skyPic = find(w, {"Main", "World", "Sky", "SkyPic"});
    const int back = find(w, {"Main", "Back"});
    const int hills = find(w, {"Main", "Back", "Hills"});
    const int empty = find(w, {"Main", "Empty"});
    const int tiles = find(w, {"Main", "Tiles"});
    check(hero >= 0 && cam >= 0 && spare >= 0 && sky >= 0 && skyPic >= 0 && back >= 0 && hills >= 0 && empty >= 0 && tiles >= 0, "every node of the fixture is a placement");
    if (g_failures) return exitCode(ExitCode::Failed);
    auto cls = [&](int i) { return i < 0 ? std::string() : w.placements[static_cast<usize>(i)].className; };
    check(cls(hero) == "AnimatedSprite2D" && cls(cam) == "Camera2D" && cls(sky) == "Parallax2D" && cls(back) == "ParallaxBackground" && cls(hills) == "ParallaxLayer" && cls(empty) == "AnimatedSprite2D",
          "the nodes are placed as their classes");
    check(cls(tiles) == "Node", "a TileMapLayer, which has no equivalent, stays a plain node");
    check(w.placements[static_cast<usize>(hero)].asset == "none" && w.placements[static_cast<usize>(cam)].asset == "none", "a 2D placement has no mesh");

    // ---- the animated sprite ----
    const auto* heroT = record(w, hero, "C2DTransform");
    check(heroT && near(num(heroT, "x"), 100) && near(num(heroT, "y"), 50) && !val(heroT, "order").empty(), "the sprite has a transform with its tree order");
    const auto* heroS = record(w, hero, "CSprite2D");
    check(heroS && val(heroS, "centered") == "0" && val(heroS, "fliph") == "1" && near(num(heroS, "ox"), 2) && near(num(heroS, "oy"), 3) && val(heroS, "filter") == "nearest" &&
              val(heroS, "texture").empty(),
          "its sprite record: not centered, flip_h, offset, nearest, and no texture of its own (the animation sets it)");
    check(val(heroS, "modulate") == "ff804080", "modulate as RRGGBBAA: " + val(heroS, "modulate"));
    const auto* anim = record(w, hero, "CSpriteAnim2D");
    check(anim != nullptr, "an AnimatedSprite2D has a CSpriteAnim2D record");
    check(near(num(anim, "speed"), 1.5), "speed_scale is the record's speed");
    check(val(anim, "clip0") == "idle" && near(num(anim, "clip0fps"), 6) && val(anim, "clip0loop").empty() && val(anim, "clip1") == "hit" && near(num(anim, "clip1fps"), 12) && val(anim, "clip1loop") == "0",
          "the animations in order, with their speeds and loop flags");
    check(val(anim, "clip0f0") == "Textures/art/hero.png" && val(anim, "clip0f1") == "Textures/art/sky.png||2" && val(anim, "clip0f2").empty() && val(anim, "clip1f0") == "Textures/art/sky.png",
          "the frames are copied texture paths, a duration other than 1 as the weight: " + val(anim, "clip0f1"));
    check(val(anim, "clip1f1") == "||0.5", "a frame with no texture file (an atlas texture of a test picture) keeps its place and its duration: " + val(anim, "clip1f1"));
    check(val(anim, "animation") == "hit" && val(anim, "autoplay") == "idle" && near(num(anim, "frame"), 1) && val(anim, "playing").empty(), "animation, autoplay and frame");
    const auto* emptyAnim = record(w, empty, "CSpriteAnim2D");
    check(emptyAnim && emptyAnim->kv.empty(), "an AnimatedSprite2D with no SpriteFrames has an empty animation record");

    // ---- the cameras ----
    const auto* camR = record(w, cam, "CCamera2D");
    check(camR && near(num(camR, "zx"), 2) && near(num(camR, "zy"), 3) && near(num(camR, "ox"), 8) && near(num(camR, "oy"), -4), "Camera2D: zoom and offset");
    check(camR && near(num(camR, "left"), 0) && !val(camR, "left").empty() && near(num(camR, "top"), -50) && near(num(camR, "right"), 1280) && near(num(camR, "bottom"), 720), "the four limits (0 is a limit)");
    check(camR && near(num(camR, "smooth"), 7) && val(camR, "draghorizontal") == "1" && val(camR, "dragvertical").empty() && near(num(camR, "dragleft"), 0.1) && near(num(camR, "dragright"), 0.3) &&
              val(camR, "dragtop").empty(),
          "position smoothing speed, the horizontal drag and its margins (a margin left at 0.2 is not written)");
    check(camR && val(camR, "anchor") == "topleft" && val(camR, "rotation") == "1" && val(camR, "enabled").empty(), "anchor_mode fixed top-left and ignore_rotation off");
    check(near(num(record(w, cam, "C2DTransform"), "y"), -10), "the camera has its transform");
    const auto* spareR = record(w, spare, "CCamera2D");
    check(spareR && val(spareR, "enabled") == "0" && val(spareR, "dragvertical") == "1" && val(spareR, "left").empty() && val(spareR, "zx").empty(), "a disabled camera, with defaults left out");

    // ---- the parallax ----
    const auto* par = record(w, sky, "CParallax2D");
    check(par && near(num(par, "sx"), 0.5) && near(num(par, "sy"), 0.25) && near(num(par, "ox"), 3) && near(num(par, "oy"), -4) && near(num(par, "rx"), 512) && near(num(par, "ry"), 256) &&
              near(num(par, "repeat"), 3) && near(num(par, "ax"), -20) && near(num(par, "ay"), 5),
          "Parallax2D: scroll scale, offset, repeat size and times, autoscroll");
    check(par && near(num(par, "beginx"), -100) && near(num(par, "beginy"), -50) && near(num(par, "endx"), 900) && near(num(par, "endy"), 700) && val(par, "follow").empty(), "limits, and follow_viewport left on");
    check(record(w, skyPic, "CSprite2D") && val(record(w, skyPic, "CSprite2D"), "texture") == "Textures/art/sky.png", "a sprite below it is an ordinary sprite");
    const auto* bg = record(w, back, "C2DLayer");
    check(bg && near(num(bg, "index"), -100) && val(bg, "followviewport") == "1", "ParallaxBackground: a layer at -100 that follows the camera");
    const auto* layerPar = record(w, hills, "CParallax2D");
    check(layerPar && near(num(layerPar, "sx"), 0.3) && near(num(layerPar, "sy"), 0.6) && near(num(layerPar, "ox"), 4) && near(num(layerPar, "oy"), 5) && near(num(layerPar, "rx"), 640) && val(layerPar, "ry").empty(),
          "ParallaxLayer: motion scale, offset and mirroring as a parallax");

    // ---- the file round trip ----
    const std::string text = slurp(levelPath);
    check(text.find("COMPONENT CSpriteAnim2D") != std::string::npos && text.find("COMPONENT CCamera2D") != std::string::npos && text.find("COMPONENT CParallax2D") != std::string::npos,
          "the level file holds the three new record types");
    fmt::OcWorldData again;
    const std::string rewritten = outRoot.string() + "/again.ocworld";
    check(fmt::saveOcworld(rewritten, w, &err) && fmt::loadOcworld(rewritten, again, &err), "the level saves and loads again: " + err);
    check(again.placements.size() == w.placements.size() && val(record(again, hero, "CSpriteAnim2D"), "clip1f1") == "||0.5" && val(record(again, cam, "CCamera2D"), "anchor") == "topleft",
          "the records survive a save");

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
    check(reported("\"type\":\"AnimatedSprite2D\"", "\"rating\":\"approximated\"", "CSpriteAnim2D"), "an AnimatedSprite2D is rated approximated and names its record");
    check(reported("\"type\":\"AnimatedSprite2D\"", "\"property\":\"sprite_frames\"", "no texture file"), "a frame without a texture file is reported");
    check(reported("\"type\":\"AnimatedSprite2D\"", "\"property\":\"sprite_frames\"", "no SpriteFrames"), "an AnimatedSprite2D with no SpriteFrames is reported");
    check(reported("\"type\":\"Camera2D\"", "\"rating\":\"approximated\"", "CCamera2D"), "a Camera2D is rated approximated");
    check(reported("\"type\":\"Camera2D\"", "\"property\":\"rotation_smoothing_enabled\"", "\"rating\":\"unsupported\""), "rotation smoothing is unsupported, with a reason");
    check(reported("\"type\":\"Camera2D\"", "\"property\":\"limit_smoothed\"", "\"rating\":\"approximated\""), "limit smoothing is approximated");
    check(reported("\"type\":\"Parallax2D\"", "\"rating\":\"approximated\"", "CParallax2D"), "a Parallax2D is rated approximated");
    check(reported("\"type\":\"Parallax2D\"", "\"property\":\"screen_offset\"", "\"rating\":\"unsupported\"") &&
              reported("\"type\":\"Parallax2D\"", "\"property\":\"ignore_camera_scroll\"", "\"rating\":\"unsupported\""),
          "screen_offset and ignore_camera_scroll are unsupported");
    check(reported("\"type\":\"ParallaxBackground\"", "\"rating\":\"approximated\"", "C2DLayer") && reported("\"type\":\"ParallaxBackground\"", "\"property\":\"scroll_offset\"", "\"rating\":\"unsupported\""),
          "a ParallaxBackground is an approximation, its own scrolling unsupported");
    check(reported("\"type\":\"ParallaxLayer\"", "\"rating\":\"approximated\"", "CParallax2D") && reported("\"type\":\"ParallaxLayer\"", "\"property\":\"motion_mirroring\"", "\"rating\":\"approximated\""),
          "a ParallaxLayer is an approximation");
    check(reported("\"type\":\"TileMapLayer\"", "\"rating\":\"unsupported\"", "no equivalent yet"), "a TileMapLayer is unsupported, with the reason");

    AVER_INFO("==================================================");
    if (g_failures == 0) AVER_INFO("=== {} checks, 0 failed ===", g_checks);
    else AVER_ERROR("=== {} checks, {} FAILED ===", g_checks, g_failures);
    return g_failures == 0 ? exitCode(ExitCode::Ok) : exitCode(ExitCode::Failed);
}
