// `AverAssetC fabric`: the driver, the script registry and the manifests (Fabric/scripts.json, Fabric/project.json).
#include "FabricConvert.hpp"
#include "FabricInternal.hpp"

#include "aver/core/ErrorCodes.hpp"
#include "aver/core/Log.hpp"
#include "aver/extensions/aver_ext_protocol.h"
#include "aver/platform/FileSystem.hpp"

#include <algorithm>
#include <cctype>
#include <cstdio>
#include <filesystem>
#include <sstream>

namespace aver::fabric {

namespace {

std::string trim(const std::string& s) {
    size_t a = 0, b = s.size();
    while (a < b && std::isspace(static_cast<unsigned char>(s[a]))) ++a;
    while (b > a && std::isspace(static_cast<unsigned char>(s[b - 1]))) --b;
    return s.substr(a, b - a);
}

std::vector<std::string> words(const std::string& line) {
    std::vector<std::string> out;
    std::string cur;
    for (const char c : line) {
        if (std::isspace(static_cast<unsigned char>(c)) || c == ',') {
            if (!cur.empty()) { out.push_back(cur); cur.clear(); }
        } else {
            cur.push_back(c);
        }
    }
    if (!cur.empty()) out.push_back(cur);
    return out;
}

std::string identifierAt(const std::string& s, size_t from) {
    while (from < s.size() && std::isspace(static_cast<unsigned char>(s[from]))) ++from;
    size_t e = from;
    while (e < s.size() && (std::isalnum(static_cast<unsigned char>(s[e])) || s[e] == '_')) ++e;
    return s.substr(from, e - from);
}

void scanGdScript(const std::string& text, ScriptInfo& si) {
    std::istringstream in(text);
    std::string line;
    while (std::getline(in, line)) {
        line = trim(line);
        if (line.empty() || line[0] == '#') continue;
        const std::vector<std::string> w = words(line);
        for (size_t i = 0; i < w.size(); ++i) {
            if (w[i] == "class_name" && i + 1 < w.size() && si.className.empty()) {
                si.className = identifierAt(w[i + 1], 0);
                si.declaredClassName = !si.className.empty();
            } else if (w[i] == "extends" && i + 1 < w.size() && si.extends.empty() && si.extendsPath.empty()) {
                const std::string& v = w[i + 1];
                if (v[0] == '"' || v[0] == '\'') si.extendsPath = v.substr(1, v.size() >= 2 ? v.size() - 2 : 0);
                else si.extends = identifierAt(v, 0);
            }
        }
        if (w[0] == "func" || w[0] == "var" || w[0] == "const" || w[0] == "signal" || w[0] == "@export" || w[0] == "enum") break;
    }
}

void scanCsScript(const std::string& text, const std::string& stem, ScriptInfo& si) {
    std::istringstream in(text);
    std::string line;
    std::string bestName, bestBase;
    while (std::getline(in, line)) {
        const std::string t = trim(line);
        if (t.rfind("//", 0) == 0) continue;
        size_t at = 0;
        while ((at = t.find("class ", at)) != std::string::npos) {
            if (at > 0 && !std::isspace(static_cast<unsigned char>(t[at - 1]))) { at += 6; continue; }
            const std::string name = identifierAt(t, at + 6);
            if (name.empty()) break;
            std::string base;
            const size_t colon = t.find(':', at + 6 + name.size());
            if (colon != std::string::npos) base = identifierAt(t, colon + 1);
            if (bestName.empty() || name == stem) { bestName = name; bestBase = base; }
            break;
        }
    }
    si.className = bestName;
    si.declaredClassName = !bestName.empty();
    si.extends = bestBase;
}

bool reservedClassName(const std::string& n) {
    static const std::set<std::string> extra = {"Object", "GodotObject", "RefCounted", "Resource", "Mesh", "Material", "Shape3D", "Texture2D", "PackedScene",
                                                "SceneTree", "Variant", "Callable", "Signal", "StringName", "NodePath", "Vector2", "Vector3",
                                                "Transform3D", "Basis", "Quaternion", "Color", "Aabb", "Plane", "Projection", "GD", "Mathf", "Input",
                                                // What converted scripts name unqualified: a script class of the same name hides it (a class System breaks `using System;`).
                                                "System", "Math", "Task", "Enum", "Action", "Func", "Delegate", "Exception", "Array", "MidpointRounding", "StringComparison",
                                                "GDScript", "PackedArrays", "Collections"};
    return extra.count(n) || isControlClass(n) || is2DClass(n) || isNode3DClass(n) || n == "Node" || n == "Timer" || n == "Viewport" || n == "CanvasLayer" ||
           n == "Window" || n == "CanvasItem" || n == "WorldEnvironment" || n == "AnimationPlayer" || n == "AudioStreamPlayer" ||
           n == "AudioStreamPlayer3D" || n == "AudioListener3D" || n == "AudioServer" || n == "AudioStream" || n == "NavigationAgent3D" ||
           n == "AStar3D" || n == "AStar2D" || n == "AStarGrid2D" || n == "NavigationMesh";
}

}  // namespace

Converter::Converter(std::string godotDir, std::string outDir, Host host)
    : godotDir_(std::move(godotDir)), outDir_(std::move(outDir)), host_(std::move(host)) {}

// ---- scripts -------------------------------------------------------------------------------------------------------

void Converter::registerScripts() {
    for (const std::string& res : fs_.files()) {
        const size_t d = res.find_last_of('.');
        if (d == std::string::npos) continue;
        const std::string ext = lower(res.substr(d));
        if (ext != ".gd" && ext != ".cs") continue;
        script(res);
    }
    // Resolve `extends` through other scripts to the Godot class at the bottom.
    for (auto& [path, si] : scripts_) {
        std::string ext = si.extends;
        std::string viaPath = si.extendsPath;
        for (int hop = 0; hop < 16; ++hop) {
            const ScriptInfo* next = nullptr;
            if (!viaPath.empty()) {
                const auto it = scripts_.find(gd::GodotProjectFs::normalizeRes(viaPath));
                if (it != scripts_.end()) next = &it->second;
            } else if (!ext.empty()) {
                for (const auto& [p2, other] : scripts_)
                    if (other.declaredClassName && other.className == ext) { next = &other; break; }
            }
            if (!next) break;
            ext = next->extends;
            viaPath = next->extendsPath;
            if (ext.empty() && viaPath.empty()) break;
        }
        si.extends = ext.empty() ? "Node" : ext;
    }
}

ScriptInfo* Converter::script(const std::string& resPath) {
    if (resPath.empty()) return nullptr;
    if (const auto it = scripts_.find(resPath); it != scripts_.end()) return &it->second;
    ScriptInfo si;
    si.path = resPath;
    const size_t d = resPath.find_last_of('.');
    const std::string ext = d == std::string::npos ? std::string{} : lower(resPath.substr(d));
    si.kind = ext == ".cs" ? "csharp" : "gdscript";
    std::string text;
    const std::string disk = fs_.toDiskPath(resPath);
    si.exists = !disk.empty() && readFileText(disk, text);
    if (si.exists) {
        if (si.kind == "csharp") scanCsScript(text, stemOf(resPath), si);
        else scanGdScript(text, si);
    }
    // A sidecar uid, when the project has one.
    std::string uidText;
    if (!disk.empty() && readFileText(disk + ".uid", uidText)) si.uid = trim(uidText);

    std::string name = si.className.empty() ? pascalCase(stemOf(resPath)) : si.className;
    if (reservedClassName(name)) name += "Script";
    std::string unique = name;
    for (int n = 2; usedClassNames_.count(unique); ++n) unique = name + std::to_string(n);
    usedClassNames_.insert(unique);
    si.className = unique;
    if (si.extends.empty() && si.extendsPath.empty()) si.extends = "Node";   // a script with no extends, or a missing file
    return &(scripts_[resPath] = std::move(si));
}

namespace {

std::string jsonList(const std::vector<std::string>& v) {
    std::string out = "[";
    for (size_t i = 0; i < v.size(); ++i) out += (i ? "," : "") + jsonString(v[i]);
    return out + "]";
}

}  // namespace

bool Converter::writeScriptsManifest() {
    std::string j = "{\n  \"schema\": 1,\n  \"project\": " + jsonString(name_) + ",\n  \"scripts\": [";
    bool first = true;
    for (const auto& [path, si] : scripts_) {
        j += first ? "\n" : ",\n";
        first = false;
        j += "    {\"path\": " + jsonString(si.path) + ", \"kind\": " + jsonString(si.kind) + ", \"className\": " + jsonString(si.className) +
             ", \"declaredClassName\": " + (si.declaredClassName ? "true" : "false") + ", \"extends\": " + jsonString(si.extends) +
             ", \"extendsPath\": " + jsonString(si.extendsPath) + ", \"exists\": " + (si.exists ? "true" : "false") +
             ", \"autoload\": " + (si.autoload ? "true" : "false") + ", \"uid\": " + jsonString(si.uid) +
             ", \"output\": " + jsonString("Content/Scripts/" + si.className + ".cs") + ", \"uses\": " + jsonList(si.uses) + "}";
    }
    j += first ? "]\n}\n" : "\n  ]\n}\n";
    return writeFileText(fabricDir_ + "/scripts.json", j);
}

// Fabric/nodes.json: for every scene written, its nodes as the runtime places them (path from the scene's root, the class placed, the script, the
// owner and %Unique flag, and the .tscn files assigned to a scripted node's properties). The script converter reads it to type $Path, get_node("path"),
// %Unique and PackedScene.instantiate() from the scene the script is attached to.
bool Converter::writeNodesManifest() {
    std::string j = "{\n  \"schema\": 1,\n  \"scenes\": [";
    bool firstScene = true;
    for (const auto& [scene, nodes] : sceneNodes_) {
        j += firstScene ? "\n" : ",\n";
        firstScene = false;
        j += "    {\"scene\": " + jsonString(scene) + ", \"nodes\": [";
        for (size_t i = 0; i < nodes.size(); ++i) {
            const SceneNodeRecord& n = nodes[i];
            j += std::string(i ? ",\n" : "\n") + "      {\"path\": " + jsonString(n.path) + ", \"base\": " + jsonString(n.baseClass) + ", \"class\": " + jsonString(n.cls) +
                 ", \"script\": " + jsonString(n.script) + ", \"owner\": " + jsonString(n.owner) + ", \"unique\": " + (n.unique ? "true" : "false") + ", \"packed\": {";
            for (size_t k = 0; k < n.packedScenes.size(); ++k)
                j += std::string(k ? ", " : "") + jsonString(n.packedScenes[k].first) + ": " + jsonString(n.packedScenes[k].second);
            j += "}}";
        }
        j += nodes.empty() ? "]}" : "\n    ]}";
    }
    j += firstScene ? "]\n}\n" : "\n  ]\n}\n";
    return writeFileText(fabricDir_ + "/nodes.json", j);
}

bool Converter::writeProjectManifest() {
    std::string j = "{\n  \"schema\": 1,\n  \"name\": " + jsonString(name_) + ",\n  \"godotName\": " + jsonString(project_.name) +
                    ",\n  \"godotVersion\": " + jsonString(project_.engineVersion) + ",\n  \"usesCSharp\": " + (project_.usesCSharp ? "true" : "false") +
                    ",\n  \"renderingMethod\": " + jsonString(project_.renderingMethod) + ",\n  \"startScene\": " + jsonString(startSceneRes_) +
                    ",\n  \"startLevel\": " + jsonString(startLevelAsset_) + ",\n  \"levels\": " + jsonList(sceneLevels_) +
                    ",\n  \"prefabs\": " + jsonList(prefabs_) + ",\n  \"autoloads\": [";
    for (size_t i = 0; i < project_.autoloads.size(); ++i) {
        const gd::GodotAutoload& a = project_.autoloads[i];
        j += std::string(i ? "," : "") + "\n    {\"name\": " + jsonString(a.name) + ", \"path\": " + jsonString(a.path) + ", \"singleton\": " +
             (a.singleton ? "true" : "false") + "}";
    }
    j += project_.autoloads.empty() ? "]" : "\n  ]";
    j += ",\n  \"layerNames\": {";
    for (size_t i = 0; i < project_.layerNames.size(); ++i)
        j += std::string(i ? ", " : "") + jsonString(project_.layerNames[i].first) + ": " + jsonString(project_.layerNames[i].second);
    j += "},\n  \"enabledPlugins\": " + jsonList(project_.enabledPlugins);
    // Every setting of project.godot, as Godot's own text for the value ("section/key": "<variant text>"): ProjectSettings.get_setting reads them at run
    // time, so a setting with no Aver key still answers with Godot's value and type.
    j += ",\n  \"settings\": {";
    bool firstSetting = true;
    for (const gd::GodotConfigSection& sec : project_.config.sections) {
        for (const gd::GodotProperty& e : sec.entries) {
            const std::string key = sec.name.empty() ? e.name : sec.name + "/" + e.name;
            j += std::string(firstSetting ? "\n    " : ",\n    ") + jsonString(key) + ": " + jsonString(gd::godotVariantToText(e.value));
            firstSetting = false;
        }
    }
    j += firstSetting ? "}" : "\n  }";
    j += ",\n  \"environment\": {\"found\": " + std::string(projectEnv_.found ? "true" : "false") + ", \"tonemapMode\": " + std::to_string(projectEnv_.tonemapMode) +
         ", \"tonemapExposure\": " + std::to_string(projectEnv_.tonemapExposure) + ", \"glow\": " + (projectEnv_.glow ? "true" : "false") +
         ", \"source\": " + jsonString(projectEnv_.source) + "}";
    j += ",\n  \"extensions\": [";
    for (size_t i = 0; i < extensions_.size(); ++i) {
        const ExtensionInfo& x = extensions_[i];
        j += std::string(i ? "," : "") + "\n    {\"path\": " + jsonString(x.resPath) + ", \"content\": " + jsonString("Content/" + x.contentPath) +
             ", \"entrySymbol\": " + jsonString(x.entrySymbol) + ", \"compatibilityMinimum\": " + jsonString(x.compatibilityMinimum) +
             ", \"compatibilityMaximum\": " + jsonString(x.compatibilityMaximum) + ", \"hostLibrary\": " + (x.hostLibrary ? "true" : "false") +
             ", \"classes\": " + jsonList(x.classes) + ", \"files\": " + jsonList(x.libraries) + ", \"missing\": " + jsonList(x.missing) + "}";
    }
    j += extensions_.empty() ? "]" : "\n  ]";
    j += "\n}\n";
    return writeFileText(fabricDir_ + "/project.json", j);
}

// ---- the run ---------------------------------------------------------------------------------------------------------------

namespace {
double g_progressFraction = 0.0;   // where the bar is, so a step inside a stage can say what it is doing without moving it
}  // namespace

// The engine's command protocol (aver_ext_protocol.h): one JSON line each, for the tool that runs this command.
void emitProgress(double fraction, const std::string& text) {
    g_progressFraction = fraction;
    std::printf("{\"" AVER_EXT_MSG_KEY "\":\"" AVER_EXT_MSG_PROGRESS "\",\"fraction\":%.2f,\"text\":%s}\n", fraction, jsonString(text).c_str());
    std::fflush(stdout);
}

void emitProgressText(const std::string& text) { emitProgress(g_progressFraction, text); }

namespace {

void emitResult(bool ok, const std::string& open, const std::string& report, size_t warnings, size_t errors, const std::string& message) {
    std::printf("{\"" AVER_EXT_MSG_KEY "\":\"" AVER_EXT_MSG_RESULT "\",\"ok\":%s,\"open\":%s,\"report\":%s,\"warnings\":%zu,\"errors\":%zu,\"message\":%s}\n",
                ok ? "true" : "false", jsonString(open).c_str(), jsonString(report).c_str(), warnings, errors, jsonString(message).c_str());
    std::fflush(stdout);
}

// A failed run: the log line people read, and the result line tools read.
int failRun(const std::string& message) {
    AVER_ERROR("fabric: {}", message);
    emitResult(false, "", "", 0, 1, message);
    return 1;
}

}  // namespace

int Converter::run() {
    gd::GodotDiagnostics diag;
    if (!fs_.open(godotDir_, diag)) return failRun(diag.format());
    if (!fs_.scan(diag)) AVER_WARN("fabric: scanning the project reported errors");
    emitProgress(0.02, "reading the Godot project");

    gd::GodotDiagnostics pdiag;
    if (!gd::loadGodotProject(godotDir_, project_, pdiag)) {
        if (project_.configVersion > 0 && project_.configVersion < 5)
            return failRun("This is a Godot 3 project (config_version " + std::to_string(project_.configVersion) + "). Fabric converts Godot 4.1 and later.");
        return failRun("project.godot: " + pdiag.format());
    }

    std::string folder = std::filesystem::path(godotDir_).filename().string();
    if (folder.empty()) folder = std::filesystem::path(godotDir_).parent_path().filename().string();
    // The project's folder and file name: its title, not a tagline ("Molten Aim 25 , An OpenSource Realistic Shooter made in Godot 4.4 ..."), and short,
    // because it is in every path the project writes twice (<out>/<name>/<name>.ocproject, <out>/<name>/Content/...) and Windows stops at 260 characters.
    {
        constexpr size_t kMaxName = 40;
        std::string title = project_.name;
        for (const char* sep : {",", ":", " - ", " | ", "(", " \xE2\x80\x94 ", " \xE2\x80\x93 "})
            if (const size_t at = title.find(sep); at != std::string::npos && !safeName(title.substr(0, at), "").empty()) title.resize(at);
        name_ = safeName(title, safeName(folder, "FabricProject"));
        if (name_.size() > kMaxName) name_.resize(kMaxName);
        if (name_ != safeName(project_.name, name_))
            report_.add("setting", "project.godot", "", "", "application/config/name", Rating::Converted,
                        "the project folder and file are named '" + name_ + "' (the title, short enough for Windows paths); the window title is the full name", 0);
    }

    // A dry run converts into a scratch folder and keeps only the report.
    const std::string requestedOut = outDir_;
    if (options_.dryRun) {
        std::error_code ec;
        outDir_ = (std::filesystem::temp_directory_path(ec) / ("aver-fabric-dry-" + fnvHex(godotDir_ + "|" + requestedOut))).string();
        std::filesystem::remove_all(outDir_, ec);
    }
    projectDir_ = outDir_ + "/" + name_;
    contentDir_ = projectDir_ + "/Content";
    fabricDir_ = projectDir_ + "/Fabric";
    const std::string jsonlPath = outDir_ + "/FabricReport.jsonl", mdPath = outDir_ + "/FabricReport.md";

    if (!options_.scriptsOnly && !options_.overwrite && fileExists(projectDir_ + "/" + name_ + ".ocproject"))
        return failRun(projectDir_ + " already holds a converted project; pass --overwrite to replace it (or --scripts-only to convert only its scripts)");
    if (options_.scriptsOnly) {
        if (!fileExists(projectDir_ + "/" + name_ + ".ocproject"))
            return failRun("--scripts-only needs a project converted before: " + projectDir_ + " has no " + name_ + ".ocproject");
        report_.readJsonl(jsonlPath);   // the earlier run's entries; the script step replaces its own
    }

    for (const gd::GodotDiag& x : diag.items)
        if (x.severity != gd::GodotSeverity::Info)
            report_.add("file", x.file, "", "", "", x.severity == gd::GodotSeverity::Error ? Rating::Unsupported : Rating::Approximated, x.message, x.line);
    for (const gd::GodotDiag& x : pdiag.items)
        if (x.severity != gd::GodotSeverity::Info)
            report_.add("project", "res://project.godot", "", "", "", x.severity == gd::GodotSeverity::Error ? Rating::Unsupported : Rating::Approximated, x.message, x.line);
    if (project_.engineVersion.empty()) {
        report_.add("project", "res://project.godot", "", "", "config/features", Rating::Approximated, "the project does not say which Godot version made it; 4.1 or later is assumed", 0);
    }

    createDirectories(contentDir_);
    // The folders the editor's project upgrade expects (ProjectScaffold inspectProject), so a converted project opens without the upgrade prompt.
    for (const char* standard : {"Maps", "Meshes", "Materials", "Textures", "Sounds", "Scripts"}) createDirectories(contentDir_ + "/" + standard);
    createDirectories(fabricDir_);
    AVER_INFO("fabric: {} -> {}", godotDir_, options_.dryRun ? requestedOut + " (report only)" : projectDir_);

    emitProgress(0.05, "registering scripts");
    registerScripts();
    bool ok = true;
    if (options_.scriptsOnly) {
        if (!fileExists(fabricDir_ + "/scripts.json")) writeScriptsManifest();
    } else {
        emitProgress(0.10, "GDExtensions");
        convertExtensions();
        emitProgress(0.15, "scenes");
        ok = convertScenes();
        emitProgress(0.60, "resources");
        convertStandaloneResources();
        reportProjectFiles();
        emitProgress(0.70, "project settings");
        convertInput();
        convertLocalization();
        ok = convertProject(startLevelAsset_) && ok;
        if (options_.scenesOnly)
            for (const auto& [path, si] : scripts_)
                report_.add("script", path, "", si.kind, "", Rating::Unsupported,
                            "script conversion was skipped (--scenes-only); its class is " + si.className + " (extends " + (si.extends.empty() ? "?" : si.extends) +
                                "), recorded in Fabric/scripts.json" + (si.uses.empty() ? "; no scene uses it" : ""),
                            0);
        writeScriptsManifest();
        writeNodesManifest();
        writeProjectManifest();
        writeShadersManifest();   // G6 SH2
    }

    emitProgress(0.80, "report");
    const std::string reportTitle = project_.name.empty() ? name_ : project_.name;
    if (!report_.writeMarkdown(mdPath, reportTitle) || !report_.writeJsonl(jsonlPath)) {
        AVER_ERROR("fabric: could not write the report to {}", outDir_);
        ok = false;
    }

    // Scripts last: the tool merges its entries into the report just written, and the final FabricReport.md is made from the merged file.
    if (!options_.scenesOnly) {
        emitProgress(0.85, "converting scripts");
        ok = convertScripts() && ok;
        if (!report_.writeMarkdown(mdPath, reportTitle) || !report_.writeJsonl(jsonlPath)) {
            AVER_ERROR("fabric: could not write the report to {}", outDir_);
            ok = false;
        }
    }

    std::string reportOut = outDir_;
    if (options_.dryRun) {
        std::error_code ec;
        createDirectories(requestedOut);
        std::filesystem::copy_file(mdPath, requestedOut + "/FabricReport.md", std::filesystem::copy_options::overwrite_existing, ec);
        std::filesystem::copy_file(jsonlPath, requestedOut + "/FabricReport.jsonl", std::filesystem::copy_options::overwrite_existing, ec);
        std::filesystem::remove_all(outDir_, ec);
        reportOut = requestedOut;
    }
    AVER_INFO("fabric: {} converted, {} approximated, {} unsupported ({} level(s), {} prefab(s), {} script(s), {} GDExtension(s))", report_.count(Rating::Converted),
              report_.count(Rating::Approximated), report_.count(Rating::Unsupported), sceneLevels_.size(), prefabs_.size(), scripts_.size(), extensions_.size());
    emitProgress(1.0, "done");
    std::string message = std::to_string(report_.count(Rating::Converted)) + " converted, " + std::to_string(report_.count(Rating::Approximated)) + " approximated, " +
                          std::to_string(report_.count(Rating::Unsupported)) + " unsupported; " + std::to_string(sceneLevels_.size()) + " level(s), " +
                          std::to_string(scripts_.size()) + " script(s), " + std::to_string(extensions_.size()) + " GDExtension(s).";
    if (!scripts_.empty() && !scriptsRan_) message += " The scripts were not converted; see the report.";
    if (options_.dryRun) message += " (report only)";
    emitResult(ok, options_.dryRun || !ok ? std::string() : projectDir_ + "/" + name_ + ".ocproject", reportOut + "/FabricReport.md", report_.count(Rating::Unsupported),
               ok ? 0 : 1, ok ? message : "The conversion did not finish; see the report. " + message);
    return ok ? 0 : 1;
}

namespace {
constexpr const char* kFabricUsage =
    "usage: AverAssetC fabric <godot-project-dir> <out-dir> [--dry-run] [--scenes-only] [--scripts-only] [--overwrite] [--procedural-sky]\n"
    "  converts a Godot 4.1+ project into <out-dir>/<Name>/ (an Aver project) and writes <out-dir>/FabricReport.md and .jsonl\n"
    "  --dry-run      convert into a scratch folder, keep only the report\n"
    "  --scenes-only  scenes, assets, project settings and GDExtension files; do not convert scripts\n"
    "  --scripts-only convert only the scripts, into a project converted before\n"
    "  --overwrite    replace a project already converted into <out-dir>\n"
    "  --procedural-sky  OFF by default: a ProceduralSkyMaterial becomes a sky shader that follows Godot's own sky (costs at every sky lookup)\n"
    "exit: 0 converted (the report lists what was approximated or unsupported), 1 the conversion failed, 2 bad usage";
}

int run(int argc, char** argv, const Host& host) {
    Options opt;
    std::vector<std::string> positional;
    for (int i = 2; i < argc; ++i) {
        const std::string a = argv[i];
        if (a == "--dry-run") opt.dryRun = true;
        else if (a == "--scenes-only") opt.scenesOnly = true;
        else if (a == "--scripts-only") opt.scriptsOnly = true;
        else if (a == "--overwrite") opt.overwrite = true;
        else if (a == "--procedural-sky") opt.proceduralSky = true;
        else if (a.rfind("--", 0) == 0) {
            AVER_ERROR("fabric: unknown option '{}'\n{}", a, kFabricUsage);
            emitResult(false, "", "", 0, 1, "unknown option '" + a + "'");
            return exitCode(ExitCode::Usage);
        } else {
            positional.push_back(a);
        }
    }
    if (positional.size() != 2 || (opt.scenesOnly && opt.scriptsOnly)) {
        AVER_ERROR("{}", kFabricUsage);
        emitResult(false, "", "", 0, 1, "bad usage: fabric <godot-project-dir> <out-dir> [options]");
        return exitCode(ExitCode::Usage);
    }
    std::string in = positional[0], out = positional[1];
    while (in.size() > 1 && (in.back() == '/' || in.back() == '\\')) in.pop_back();
    while (out.size() > 1 && (out.back() == '/' || out.back() == '\\')) out.pop_back();
    Converter c(in, out, host);
    c.options_ = opt;
    return c.run() == 0 ? exitCode(ExitCode::Ok) : exitCode(ExitCode::Failed);
}

}  // namespace aver::fabric
