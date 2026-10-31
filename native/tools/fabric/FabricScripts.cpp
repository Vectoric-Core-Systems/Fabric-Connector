// The script step of `AverAssetC fabric`: launches Aver.Fabric.Convert (the Roslyn / GDScript converter, a .NET tool in the extension's
// tools folder) on the manifest the scene step wrote, then reads the merged report back. Contract: docs/plans/fabric/G3_SCRIPTS.md.
#include "FabricInternal.hpp"
#include "FabricProcess.hpp"

#include "aver/core/Log.hpp"
#include "aver/platform/FileSystem.hpp"

#include <cstdio>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <string>

namespace aver::fabric {

namespace {

namespace fsys = std::filesystem;

// Separates the folders of a --refs list, as the tool's Path.PathSeparator does.
#if defined(_WIN32)
constexpr char kPathListSeparator = ';';
#else
constexpr char kPathListSeparator = ':';
#endif

// The engine checkout, where Scripts.csproj's project references point: above the executable (a build tree inside a checkout), or the source
// directory a CMake build tree above it was configured from (its CMakeCache.txt; a build tree anywhere else, as this extension's own build is).
std::string engineCheckout() {
    std::error_code ec;
    const auto isEngine = [&ec](const fsys::path& p) { return fsys::exists(p / "scripting" / "csharp" / "Aver.Framework" / "Aver.Framework.csproj", ec); };
    const std::string homeKey = "CMAKE_HOME_DIRECTORY:INTERNAL=";
    for (fsys::path p = fsys::path(executableDir()); !p.empty(); p = p.parent_path()) {
        if (isEngine(p)) return p.string();
        std::ifstream cache(p / "CMakeCache.txt");
        for (std::string line; std::getline(cache, line);) {
            if (line.compare(0, homeKey.size(), homeKey) != 0) continue;
            const fsys::path home(line.substr(homeKey.size()));
            if (isEngine(home)) return home.string();
            break;
        }
        if (p == p.parent_path()) break;
    }
    return {};
}

// The number after `"key":` in a one-line JSON object, or `def`.
long jsonNumber(const std::string& line, const char* key, long def) {
    const std::string needle = std::string("\"") + key + "\":";
    const size_t at = line.find(needle);
    if (at == std::string::npos) return def;
    return std::strtol(line.c_str() + at + needle.size(), nullptr, 10);
}

}  // namespace

std::string Converter::extensionDir() const {
    return host_.extensionDir.empty() ? executableDir() + "/Extensions/FabricConnector" : host_.extensionDir;
}

bool Converter::convertScripts() {
    scriptsRan_ = false;
    if (scripts_.empty()) return true;

    // The tool and the managed runtime are this extension's (<ext>/tools, <ext>/managed); the engine's own assemblies are the executable's.
    const std::string extDir = extensionDir();
    const std::string tool = extDir + "/tools/Aver.Fabric.Convert.dll";
    const std::string managed = extDir + "/managed";
    const std::string refs = managed + kPathListSeparator + executableDir() + "/Scripting";
    const std::string dotnet = findDotnet();
    std::string why;
    if (dotnet.empty()) why = "dotnet was not found (install the .NET SDK or set DOTNET_ROOT)";
    else if (!fileExists(tool)) why = "Aver.Fabric.Convert.dll is not in the extension's tools folder (" + tool + "): build the Aver.Fabric.Convert target";
    else if (!fileExists(managed + "/Aver.Fabric.dll")) why = "Aver.Fabric.dll is not in " + managed + ": build the Aver.Fabric.Managed target";
    if (!why.empty()) {
        AVER_ERROR("fabric: scripts not converted: {}", why);
        for (const auto& [path, si] : scripts_)
            report_.add("script", path, "", si.kind, "", Rating::Unsupported, "the script was not converted: " + why, 0);
        return false;
    }

    std::error_code ec;
    const std::string godotAbs = fsys::absolute(godotDir_, ec).string();
    std::vector<std::string> args = {tool, "scripts", "--project", godotAbs, "--out", projectDir_, "--manifest", fabricDir_ + "/scripts.json",
                                     "--refs", refs, "--report", outDir_ + "/FabricReport.jsonl"};
    if (const std::string engine = engineCheckout(); !engine.empty()) {
        args.push_back("--engine");
        args.push_back(engine);
    }
    AVER_INFO("fabric: converting {} script(s) with Aver.Fabric.Convert", scripts_.size());
    std::string summary;
    const ProcessOutput out = runProcess(dotnet, args, [&](const std::string& line) {
        if (line.find("\"kind\":\"fabric-scripts\"") != std::string::npos) { summary = line; return; }
        if (!line.empty()) AVER_INFO("fabric: scripts: {}", line);
    });
    if (!out.started) {
        AVER_ERROR("fabric: could not start {}", dotnet);
        for (const auto& [path, si] : scripts_)
            report_.add("script", path, "", si.kind, "", Rating::Unsupported, "the script was not converted: could not start dotnet", 0);
        return false;
    }
    // 0 converted and compiled; 1 with a summary: compile errors remain (written, marked, reported); 3: written, not compiled (reported).
    // Anything else (no summary, usage error, a crash) is the step failing, not the scripts.
    const bool soft = out.exitCode == 0 || out.exitCode == 3 || (out.exitCode == 1 && !summary.empty());
    if (!soft) {
        AVER_ERROR("fabric: the script converter failed (exit {})", out.exitCode);
        for (const auto& [path, si] : scripts_)
            report_.add("script", path, "", si.kind, "", Rating::Unsupported, "the script converter failed (exit " + std::to_string(out.exitCode) + ")", 0);
        return false;
    }
    scriptsRan_ = true;
    if (!summary.empty())
        AVER_INFO("fabric: scripts: {} compiled={} errors={}", jsonNumber(summary, "scripts", 0), summary.find("\"compiled\":true") != std::string::npos ? "yes" : "no",
                  jsonNumber(summary, "errors", 0));
    // The tool merged its entries into the report file; ours is read back from it.
    Report merged;
    if (merged.readJsonl(outDir_ + "/FabricReport.jsonl")) report_ = std::move(merged);
    return true;
}

}  // namespace aver::fabric
