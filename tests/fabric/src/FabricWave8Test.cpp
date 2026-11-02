// FabricWave8Test -- wave 8a of the Godot parity plan (text and UI) through `AverAssetC fabric`, on tests/fabric/data/Wave8Game: a Label3D (its properties are rated for the world text that now
// draws it), a WOFF font (copied and named as a family of the engine's font set, which rebuilds the sfnt when it loads) and a WOFF2 font (unsupported: it needs Brotli). Runs the tool as a
// process, like FabricWave6Test.
#include "aver/core/Log.hpp"
#include "aver/platform/FileSystem.hpp"

#include <cstdio>
#include <filesystem>
#include <fstream>
#include <sstream>
#include <string>
#if !defined(_WIN32)
#include <sys/wait.h>
#endif

using namespace aver;

static int g_failures = 0;

static void check(bool cond, const std::string& what) {
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
    std::ifstream f(path, std::ios::binary);
    std::ostringstream ss;
    ss << f.rdbuf();
    return ss.str();
}

// True when one report line holds all three fragments.
static bool reportHas(const std::string& jsonl, const std::string& a, const std::string& b, const std::string& c = {}) {
    std::istringstream is(jsonl);
    std::string line;
    while (std::getline(is, line))
        if (line.find(a) != std::string::npos && line.find(b) != std::string::npos && line.find(c) != std::string::npos) return true;
    return false;
}

int main() {
    const std::string data = AVER_FABRIC_DATA;
    const std::filesystem::path exe = AVER_ASSETC_EXE;
    std::error_code ec;
    check(std::filesystem::exists(exe, ec), "AverAssetC was built: " + exe.string());
    const std::filesystem::path outRoot = std::filesystem::temp_directory_path() / "aver-fabric-wave8-test";
    std::filesystem::remove_all(outRoot, ec);
    std::filesystem::create_directories(outRoot, ec);

    std::string log;
    const std::string out = (outRoot / "game").string();
    const int code = runCapture("\"" + exe.string() + "\" fabric \"" + data + "/Wave8Game\" \"" + out + "\"", log);
    if (code != 0) AVER_INFO("{}", log);
    check(code == 0, "the converter exits 0");
    const std::string jsonl = slurp(out + "/FabricReport.jsonl");
    const std::string content = out + "/Wave8Game/Content";

    AVER_INFO("-- fonts (P37) --");
    check(std::filesystem::exists(content + "/Fonts/fonts/Sample.woff", ec), "the WOFF file is copied into the content");
    check(reportHas(jsonl, "Sample.woff", "\"rating\":\"converted\"", "rebuilt to its sfnt"), "and rated converted, saying the engine rebuilds it when it loads");
    check(!std::filesystem::exists(content + "/Fonts/fonts/Sample2.woff2", ec), "the WOFF2 file is not copied");
    check(reportHas(jsonl, "Sample2.woff2", "\"rating\":\"unsupported\"", "Brotli"), "and rated unsupported, saying why and what to do");
    const std::string fonts = slurp(content + "/UI/Fonts.ocfonts");
    check(fonts.find("Sample.woff") != std::string::npos && fonts.find("Sample2") == std::string::npos, "the font set names the WOFF and not the WOFF2");

    AVER_INFO("-- Label3D (E6) --");
    check(reportHas(jsonl, "\"node\":\"Sign\"", "\"rating\":\"approximated\"", "world text"), "the Label3D is approximated, and the reason names the world text");
    check(reportHas(jsonl, "\"node\":\"Sign\"", "\"property\":\"text\"", "\"rating\":\"converted\""), "its text is converted");
    check(reportHas(jsonl, "\"node\":\"Sign\"", "\"property\":\"pixel_size\"", "\"rating\":\"converted\"") && reportHas(jsonl, "\"node\":\"Sign\"", "\"property\":\"font_size\"", "\"rating\":\"converted\"") &&
          reportHas(jsonl, "\"node\":\"Sign\"", "\"property\":\"outline_size\"", "\"rating\":\"converted\"") && reportHas(jsonl, "\"node\":\"Sign\"", "\"property\":\"width\"", "\"rating\":\"converted\""),
          "its size, outline and width are converted");
    check(reportHas(jsonl, "\"node\":\"Sign\"", "\"property\":\"billboard\"", "\"rating\":\"approximated\""), "billboard is approximated (particles draws as enabled)");
    check(reportHas(jsonl, "\"node\":\"Sign\"", "\"property\":\"shaded\"", "unlit"), "shaded is approximated: the label is drawn unlit");
    check(reportHas(jsonl, "\"node\":\"Sign\"", "\"property\":\"fixed_size\"", "inferred"), "fixed size says its scale is inferred");
    check(reportHas(jsonl, "\"node\":\"Sign\"", "\"property\":\"alpha_cut\"", "opaque prepass and hash"), "alpha cut says which modes draw as blended text");
    check(reportHas(jsonl, "\"node\":\"Sign\"", "\"property\":\"autowrap_mode\"", "words"), "autowrap says every mode wraps at words");
    check(reportHas(jsonl, "\"node\":\"Sign\"", "\"property\":\"render_priority\"", "\"rating\":\"unsupported\""), "render priority is unsupported");

    if (g_failures == 0) AVER_INFO("=== all wave 8a converter tests passed ===");
    else                 AVER_ERROR("=== {} FAILED ===", g_failures);
    return g_failures == 0 ? 0 : 1;
}
