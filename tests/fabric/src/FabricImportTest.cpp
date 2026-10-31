// FabricImportTest -- File > Import Godot Project... as the editor's generic extension import dialog drives it (sandbox/src/ExtensionImport.cpp,
// compiled here without ImGui): the importer read from the installed extension.json through the loader, the folder checks, the command line,
// reading the converter's progress and result lines (the engine's command protocol), the Godot 3 refusal the command makes itself, and the
// importer running the real AverAssetC on a worker thread and reporting through the editor's notification queue. The generic logic itself is
// covered by the engine's ExtensionImportTest. The editor itself is not run.
#include "EditorNotifications.hpp"
#include "ExtensionImport.hpp"
#include "aver/core/ErrorCodes.hpp"
#include "aver/core/Log.hpp"
#include "aver/platform/FileSystem.hpp"

#include <algorithm>
#include <chrono>
#include <filesystem>
#include <string>
#include <thread>

using namespace aver;
using namespace aver::editor;
namespace fsys = std::filesystem;

static int g_checks = 0;
static int g_failures = 0;

static void check(bool cond, const std::string& what) {
    ++g_checks;
    if (cond) { AVER_INFO("  ok    {}", what); return; }
    ++g_failures;
    AVER_ERROR("  FAIL  {}", what);
}

static bool contains(const std::string& hay, const std::string& needle) { return hay.find(needle) != std::string::npos; }

int main() {
    AVER_INFO("FabricImportTest");
    std::error_code ec;
    const std::string data = AVER_FABRIC_DATA;
    const fsys::path root = fsys::temp_directory_path() / "aver-fabric-import-test";
    fsys::remove_all(root, ec);
    fsys::create_directories(root, ec);

    // ---- the importer the loader lists -------------------------------------------------------------------------------------------
    AVER_INFO("-- the manifest --");
    ExtensionImporter& importer = extensionImporter();
    importer.scan();
    const ImporterEntry* fabric = nullptr;
    for (const ImporterEntry& e : importer.entries()) if (e.info.command == "fabric") fabric = &e;
    check(fabric != nullptr, "the Fabric Connector lists its importer (installed beside the test)");
    if (!fabric) { AVER_INFO("=== {} checks, {} failed ===", g_checks, g_failures); return exitCode(ExitCode::Failed); }
    const ext::ImporterInfo& info = fabric->info;
    check(info.id == "godot-project" && info.menu == "Import Godot Project..." && fabric->text.title == "Import Godot Project" && info.input.detect == "project.godot" &&
              info.input.kind == "folder" && info.outputDefault == "{input}_Aver",
          "the File menu text and the dialog title are the ones the editor always showed");
    check(info.options.size() == 2 && info.options[0].flag == "--overwrite" && info.options[1].flag == "--dry-run" && !fabric->text.description.empty(), "its options and description");

    // ---- folder checks -----------------------------------------------------------------------------------------------------------
    AVER_INFO("-- choosing the project --");
    std::string why;
    check(importInputValid(info, data + "/ConvertGame", &why), "a folder with a Godot 4 project.godot is accepted: " + why);
    check(!importInputValid(info, "", &why) && why == "Choose the input folder.", "an empty path is refused with a reason: " + why);
    check(!importInputValid(info, root.string(), &why) && contains(why, "project.godot"), "a folder without project.godot is refused: " + why);
    {
        // The dialog accepts any folder with a project.godot; refusing a Godot 3 project is the converter's job (checked below, running AverAssetC).
        const fsys::path old = root / "godot3";
        fsys::create_directories(old, ec);
        writeFileText((old / "project.godot").string(), "config_version=4\n\n[application]\nconfig/name=\"Old\"\n");
        check(importInputValid(info, old.string(), &why), "a Godot 3 project.godot passes the dialog's folder check: " + why);
    }
    const fsys::path expected = fsys::path(data + "/ConvertGame");
    check(fsys::path(importInputFrom(info, data + "/ConvertGame/project.godot")) == expected, "the dialog's project.godot gives its folder");
    check(fsys::path(importInputFrom(info, data + "/ConvertGame/props/crate.tscn")) == expected, "a file inside the project gives the project folder");
    check(fsys::path(importInputFrom(info, data + "/ConvertGame/props")) == expected, "and so does a folder inside it");
    check(importInputFrom(info, (root / "nowhere" / "file.txt").string()).empty(), "a path outside any project gives nothing");
    check(fsys::path(importDefaultOutDir(info, "C:/games/My Game")).filename() == "My Game_Aver" && fsys::path(importDefaultOutDir(info, "C:/games/My Game/")).filename() == "My Game_Aver",
          "the default output is a sibling named <folder>_Aver");

    // ---- the command line --------------------------------------------------------------------------------------------------------
    AVER_INFO("-- the command line --");
    {
        ImportRequest r;
        r.command = info.command;
        r.inDir = "/a b/godot";
        r.outDir = "/out";
        check(importArgs(r) == std::vector<std::string>({"fabric", "/a b/godot", "/out"}), "plain: fabric <godot> <out> (paths stay whole arguments)");
        for (const ext::ImporterInfo::Option& o : info.options) r.flags.push_back(o.flag);
        check(importArgs(r) == std::vector<std::string>({"fabric", "/a b/godot", "/out", "--overwrite", "--dry-run"}), "with every option the dialog offers");
    }

    // ---- reading the converter's output -----------------------------------------------------------------------------------------
    AVER_INFO("-- the converter's protocol lines --");
    {
        const std::string out =
            "[INFO ] fabric: a -> b\n"
            "{\"aver\":\"progress\",\"fraction\":0.15,\"text\":\"scenes\"}\n"
            "{\"aver\":\"result\",\"ok\":true,\"open\":\"C:\\\\Out\\\\Game\\\\Game.ocproject\",\"report\":\"C:\\\\Out\\\\FabricReport.md\",\"warnings\":2,\"errors\":0,"
            "\"message\":\"10 converted, 4 approximated, 2 unsupported; 3 level(s), 5 script(s), 1 GDExtension(s).\"}\n";
        const ImportResult r = parseImportOutput(out, 0);
        check(r.ok() && r.openPath == "C:\\Out\\Game\\Game.ocproject" && r.reportPath == "C:\\Out\\FabricReport.md" && r.warnings == 2, "the project to open, the report and the warning count are read");
        check(r.message == "10 converted, 4 approximated, 2 unsupported; 3 level(s), 5 script(s), 1 GDExtension(s)." && r.fraction == 0.15, "and the message and progress");
        const ImportResult f = parseImportOutput("[INFO ] fabric: x\n[ERROR] fabric: it already holds a converted project; pass --overwrite\n", 1);
        check(!f.ok() && contains(f.error, "pass --overwrite") && f.openPath.empty(), "a failed run carries the converter's last error line");
        const ImportResult n = parseImportOutput("nothing useful\n", 0);
        check(!n.ok() && !n.error.empty(), "a clean exit without a result is not a success");
    }

    // ---- the real converter -------------------------------------------------------------------------------------------------------
    AVER_INFO("-- running AverAssetC --");
    const std::string converter = importToolPath();
    check(!converter.empty(), "AverAssetC is beside the test: " + converter);
    if (converter.empty()) { AVER_INFO("=== {} checks, {} failed ===", g_checks, g_failures); return exitCode(ExitCode::Failed); }

    // The script step needs dotnet and the staged tool; without them the import is scenes only (the test is about the importer).
    const bool haveScriptTool = fileExists(executableDir() + "/Extensions/FabricConnector/tools/Aver.Fabric.Convert.dll") &&
                                fileExists(executableDir() + "/Extensions/FabricConnector/managed/Aver.Fabric.dll");

    ImportRequest req;
    req.command = info.command;
    req.inDir = data + "/ConvertGame";
    req.outDir = (root / "out").string();
    if (!haveScriptTool) req.flags.push_back("--scenes-only");
    std::vector<std::string> seen;
    const ImportResult direct = runImport(req, converter, [&](const std::string& line) { seen.push_back(line); });
    check(direct.ok() && contains(direct.openPath, "ConvertGame.ocproject") && contains(direct.message, "2 level(s)"), "runImport converts ConvertGame: " + direct.error);
    check(fileExists(direct.openPath) && fileExists(direct.reportPath), "the project and the report it names exist");
    bool sawLog = false, sawProgress = false;
    for (const std::string& line : seen) {
        sawLog = sawLog || contains(line, "fabric:");
        sawProgress = sawProgress || contains(line, "{\"aver\":\"progress\"");
    }
    check(direct.fraction == 1.0 && sawLog && sawProgress, "its progress ran to 1 and its lines were handed over as they arrived (" + std::to_string(seen.size()) + ")");

    const ImportResult again = runImport(req, converter);
    check(!again.ok() && contains(again.error, "--overwrite"), "importing into the same folder again says to overwrite: " + again.error);
    req.flags.push_back("--overwrite");
    check(runImport(req, converter).ok(), "and does with overwrite");
    ImportRequest bad = req;
    bad.inDir = (root / "godot3").string();
    const ImportResult godot3 = runImport(bad, converter);
    check(!godot3.ok() && contains(godot3.error, "Godot 3 project") && contains(godot3.error, "config_version 4"), "a Godot 3 project is refused by the converter, with its message: " + godot3.error);
    check(!runImport(req, (root / "no-such-tool").string()).started, "a missing converter is reported as not started");

    // ---- the importer on its worker thread -----------------------------------------------------------------------------------------
    AVER_INFO("-- the importer --");
    notifications().clear();
    req.outDir = (root / "threaded").string();
    req.flags.erase(std::remove(req.flags.begin(), req.flags.end(), std::string("--overwrite")), req.flags.end());
    usize index = 0;
    for (usize i = 0; i < importer.entries().size(); ++i) if (importer.entries()[i].info.command == "fabric") index = i;
    std::string startWhy;
    check(importer.start(index, req, &startWhy), "start() launches the converter: " + startWhy);
    std::string second;
    check(!importer.start(index, req, &second) && contains(second, "already running"), "a second start while it runs is refused");
    check(notifications().size() >= 1, "a progress notification is up while it runs");
    for (int i = 0; i < 600 && importer.running(); ++i) std::this_thread::sleep_for(std::chrono::milliseconds(100));
    importer.drawModal(1.0f);   // reaps the finished import
    check(!importer.running() && importer.lastResult().ok() && fileExists(importer.lastResult().openPath), "it finishes and the result names the project to open");
    std::vector<Notification> shown;
    usize hidden = 0;
    notifications().tick(1.0, shown, hidden);
    bool finished = false;
    for (const Notification& n : shown) finished = finished || (n.title == "Import Godot Project finished" && !n.hasProgress && n.severity != NotifySeverity::Error);
    check(finished, "and the notification became 'Import Godot Project finished'");
    check(importer.takeOpenRequest().empty(), "nothing asks the editor to open the project until the user presses the button");

    AVER_INFO("=== {} checks, {} failed ===", g_checks, g_failures);
    if (!g_failures) fsys::remove_all(root, ec);
    return exitCode(g_failures ? ExitCode::Failed : ExitCode::Ok);
}
