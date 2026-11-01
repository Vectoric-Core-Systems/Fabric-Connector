// FabricConverterBugsTest -- the converter bugs of docs/plans/GODOT_PARITY.md section 3.4 that live in the scene and project converter (C1, C12 to C20), through
// `AverAssetC fabric` on a Godot project the test builds in a temp folder (every file is written here, none is a copy of a real one):
//   C1   scripts named System, Math and Task get class names that do not hide what converted code uses
//   C12  a file with the binary resource magic is read like a .res whatever its extension (a .mesh, a .phys)
//   C13  a PhysicsMaterial becomes Object(PhysicsMaterial) and its body properties are rated converted
//   C14  a uid:// in a project setting is resolved (the default bus layout)
//   C15  editor and runtime-only settings are rated approximated, physics_engine reads as Jolt
//   C16  Jolt's limits reach the .ocproject
//   C17  a Radiance .hdr texture is tone-mapped to a PNG
//   C18  an InputEventAction in a Shortcut becomes Object(InputEventAction)
//   C19  key modifiers in the input map become chord triggers; wheel notches are the engine's wheel key slots
//   C20  cast_shadow, gi_mode, extra_cull_margin and ignore_occlusion_culling are reported
// The converter is run as a process and what it wrote is read back. Exits 0 or 1.
#include "../../formats.godot/src/GodotBinaryFixtures.hpp"

#include "aver/core/ErrorCodes.hpp"
#include "aver/core/Log.hpp"
#include "aver/formats/OcInput.hpp"
#include "aver/formats/OcProject.hpp"
#include "aver/formats/OcWorld.hpp"
#include "aver/platform/FileSystem.hpp"
#if AVER_FABRIC_TEST_OCMAT
#include "aver/platform/Image.hpp"
#endif

#include <cstdio>
#include <filesystem>
#include <initializer_list>
#include <string>
#if !defined(_WIN32)
#include <sys/wait.h>
#endif

using namespace aver;
using namespace gbfix;

static int g_checks = 0, g_failures = 0;

static void check(bool cond, const std::string& what) {
    ++g_checks;
    if (cond) {
        AVER_INFO("  ok    {}", what);
        return;
    }
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

static void writeText(const std::string& path, const std::string& s) {
    std::filesystem::create_directories(std::filesystem::path(path).parent_path());
    writeFileBytes(path, s.data(), s.size());
}
static void writeBinary(const std::string& path, const Bytes& b) { writeFileBytes(path, b.data(), b.size()); }

// True when one line of the report holds every needle.
static bool reported(const std::string& jsonl, std::initializer_list<const char*> needles) {
    usize at = 0;
    while (at < jsonl.size()) {
        usize end = jsonl.find('\n', at);
        if (end == std::string::npos) end = jsonl.size();
        const std::string line = jsonl.substr(at, end - at);
        bool all = true;
        for (const char* n : needles)
            if (line.find(n) == std::string::npos) { all = false; break; }
        if (all) return true;
        at = end + 1;
    }
    return false;
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

// A property of a placement as the level stores it (Props component), percent-decoded.
static std::string propOf(const fmt::OcWorldData& w, int idx, const char* key) {
    if (idx < 0) return {};
    for (const fmt::OcPlacedComponent& c : w.placements[static_cast<usize>(idx)].components)
        if (c.type == "Props")
            if (const std::string* v = c.find(key)) return *v;
    return {};
}

// A binary PhysicsMaterial with friction 0.5 and the combine flags off, in a file whose name says nothing about being a resource.
static Bytes gripMaterial() {
    FileDef d;
    d.cls = "PhysicsMaterial";
    d.uid = 0x4a8d2c6e1f03b975ULL;
    FileDef::Res m;
    m.path = "res://grip.phys";
    m.type = "PhysicsMaterial";
    d.res.push_back(m);
    d.prop(d.res.back(), "friction", d.f.flt(0.5f));
    d.prop(d.res.back(), "bounce", d.f.flt(0.0f));
    return d.build();
}

// A one-triangle ArrayMesh (vertex, normal, index; format version 1) saved with the extension Godot's own importer uses for meshes.
static Bytes triangleMesh() {
    FileDef d;
    d.cls = "ArrayMesh";
    d.uid = 0x5b1e7d3a9c20f486ULL;
    FileDef::Res m;
    m.path = "res://tri.mesh";
    m.type = "ArrayMesh";
    d.res.push_back(m);
    Enc vd, id;
    const f32 pos[9] = {0, 0, 0, 1, 0, 0, 0, 0, 1};
    for (f32 f : pos) vd.f32v(f);
    for (int i = 0; i < 3; ++i) { vd.u16v(32768); vd.u16v(32768); }
    for (int i : {0, 1, 2}) id.u16v(u16(i));
    const i64 format = 1 | 2 | 4096 | (1ll << 35);   // vertex, normal, index, format version 1
    const Fmt& f = d.f;
    const Bytes surface = f.dict({{f.text("aabb"), f.reals(kAabb, {0, 0, 0, 1, 0, 1})},
                                  {f.text("format"), f.int64(format)},
                                  {f.text("index_count"), f.integer(3)},
                                  {f.text("index_data"), f.packedBytes(id.b)},
                                  {f.text("primitive"), f.integer(3)},
                                  {f.text("uv_scale"), f.reals(kVector4, {0, 0, 0, 0})},
                                  {f.text("vertex_count"), f.integer(3)},
                                  {f.text("vertex_data"), f.packedBytes(vd.b)}});
    d.prop(d.res.back(), "_surfaces", f.array({surface}));
    return d.build();
}

static std::string keyEvent(int keycode, bool ctrl) {
    return std::string("Object(InputEventKey,\"resource_local_to_scene\":false,\"resource_name\":\"\",\"device\":0,\"window_id\":0,\"alt_pressed\":false,\"shift_pressed\":false,\"ctrl_pressed\":") +
           (ctrl ? "true" : "false") + ",\"meta_pressed\":false,\"pressed\":false,\"keycode\":" + std::to_string(keycode) +
           ",\"physical_keycode\":0,\"key_label\":0,\"unicode\":0,\"location\":0,\"echo\":false,\"script\":null)";
}

static std::string wheelEvent(int button) {
    return "Object(InputEventMouseButton,\"resource_local_to_scene\":false,\"resource_name\":\"\",\"device\":0,\"window_id\":0,\"alt_pressed\":false,\"shift_pressed\":false,"
           "\"ctrl_pressed\":false,\"meta_pressed\":false,\"button_mask\":0,\"position\":Vector2(0, 0),\"global_position\":Vector2(0, 0),\"factor\":1.0,\"button_index\":" +
           std::to_string(button) + ",\"canceled\":false,\"pressed\":true,\"double_click\":false,\"script\":null)";
}

static std::string joyMotionEvent(int axis, double value) {
    return "Object(InputEventJoypadMotion,\"resource_local_to_scene\":false,\"resource_name\":\"\",\"device\":0,\"axis\":" + std::to_string(axis) +
           ",\"axis_value\":" + std::to_string(value) + ",\"script\":null)";
}

static std::string joyButtonEvent(int button) {
    return "Object(InputEventJoypadButton,\"resource_local_to_scene\":false,\"resource_name\":\"\",\"device\":0,\"button_index\":" + std::to_string(button) +
           ",\"pressure\":0.0,\"pressed\":false,\"script\":null)";
}

int main() {
    AVER_INFO("FabricConverterBugsTest");
    namespace fs = std::filesystem;
    const fs::path exe = AVER_ASSETC_EXE;
    std::error_code ec;
    check(fs::exists(exe, ec), "AverAssetC was built: " + exe.string());
    if (g_failures) return exitCode(ExitCode::Failed);

    const fs::path root = fs::temp_directory_path() / "aver-fabric-converter-bugs-test";
    fs::remove_all(root, ec);
    fs::create_directories(root / "game", ec);
    const std::string game = (root / "game").generic_string();

    writeText(game + "/project.godot",
              "config_version=5\n\n"
              "[application]\n\nconfig/name=\"ConverterBugs\"\nconfig/version=\"0.9.1\"\nrun/main_scene=\"res://main.tscn\"\nrun/low_processor_mode=true\n\n"
              "[game]\n\ndifficulty/level=3\ndifficulty/scale=1.5\ndifficulty/title=\"Hard \\\"mode\\\"\"\ndifficulty/tint=Color(1, 0, 0, 1)\n\n"
              "[audio]\n\nbuses/default_bus_layout=\"uid://c0nvbugsbuslayout\"\n\n"
              "[autoload]\n\nSystem=\"*res://system.gd\"\n\n"
              "[debug]\n\nsettings/physics_interpolation/enable_warnings=false\n\n"
              "[filesystem]\n\nimport/blender/enabled=false\n\n"
              "[global_group]\n\ncrates=\"Everything that can be pushed\"\n\n"
              "[gui]\n\ntheme/default_font_generate_mipmaps=true\ntheme/default_font_multichannel_signed_distance_field=true\n\n"
              "[input]\n\n"
              "save={\n\"deadzone\": 0.2,\n\"events\": [" + keyEvent(83, true) + ", " + keyEvent(83, false) + "]\n}\n"
              "zoom_in={\n\"deadzone\": 0.2,\n\"events\": [" + wheelEvent(4) + "]\n}\n"
              "plain={\n\"deadzone\": 0.2,\n\"events\": [" + keyEvent(67, false) + "]\n}\n"
              "look_left={\n\"deadzone\": 0.2,\n\"events\": [" + joyMotionEvent(0, -1.0) + "]\n}\n"
              "look_right={\n\"deadzone\": 0.2,\n\"events\": [" + joyMotionEvent(0, 1.0) + "]\n}\n"
              "look_down={\n\"deadzone\": 0.2,\n\"events\": [" + joyMotionEvent(1, 1.0) + "]\n}\n"
              "extras={\n\"deadzone\": 0.5,\n\"events\": [" + keyEvent(4194336, false) + ", " + keyEvent(4194443, false) + ", " + keyEvent(46, false) + ", " +
                  keyEvent(4194310, false) + ", " + wheelEvent(8) + ", " + wheelEvent(6) + ", " + joyButtonEvent(20) + ", " + joyButtonEvent(5) + "]\n}\n\n"
              "[memory]\n\nmultithread/thread_rid_pool_prealloc=60\n\n"
              "[physics]\n\n3d/physics_engine=\"Jolt Physics\"\n"
              "jolt_physics_3d/limits/max_bodies=4096\njolt_physics_3d/limits/max_body_pairs=8192\n"
              "jolt_physics_3d/limits/max_contact_constraints=16384\njolt_physics_3d/limits/temporary_memory_buffer_size=64\n\n"
              "[rendering]\n\nvram_compression/import_etc2_astc=true\n"
              "anti_aliasing/quality/screen_space_aa=1\nanti_aliasing/quality/use_taa=true\n"
              "environment/defaults/default_clear_color=Color(0.1, 0.2, 0.4, 1)\n");
    for (const char* name : {"system", "math", "task"}) writeText(game + "/" + name + ".gd", "extends Node\n\nvar ready_once := false\n");
    writeBinary(game + "/grip.phys", gripMaterial());
    writeBinary(game + "/tri.mesh", triangleMesh());
    writeText(game + "/default_bus_layout.tres",
              "[gd_resource type=\"AudioBusLayout\" format=3 uid=\"uid://c0nvbugsbuslayout\"]\n\n[resource]\n"
              "bus/1/name = &\"Music\"\nbus/1/solo = false\nbus/1/mute = false\nbus/1/bypass_fx = false\nbus/1/volume_db = -3.0\nbus/1/send = &\"Master\"\n");
    // A 2 by 2 Radiance picture, not run-length coded: the header, a blank line, the size line, then four RGBE pixels.
    {
        std::string hdr = "#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y 2 +X 2\n";
        const unsigned char pixels[16] = {128, 64, 32, 129, 64, 128, 32, 129, 32, 64, 128, 129, 200, 200, 200, 130};
        hdr.append(reinterpret_cast<const char*>(pixels), sizeof pixels);
        writeText(game + "/sky.hdr", hdr);     // used by a material
        writeText(game + "/probe.hdr", hdr);   // used by nothing: copied as a texture of its own
    }
    writeText(game + "/main.tscn",
              "[gd_scene load_steps=8 format=3]\n\n"
              "[ext_resource type=\"Resource\" path=\"res://grip.phys\" id=\"1_grip\"]\n"
              "[ext_resource type=\"ArrayMesh\" path=\"res://tri.mesh\" id=\"2_tri\"]\n"
              "[ext_resource type=\"Texture2D\" path=\"res://sky.hdr\" id=\"3_sky\"]\n\n"
              "[sub_resource type=\"PhysicsMaterial\" id=\"PhysicsMaterial_slick\"]\nfriction = 0.2\nbounce = 0.6\n\n"
              "[sub_resource type=\"StandardMaterial3D\" id=\"StandardMaterial3D_sky\"]\nalbedo_texture = ExtResource(\"3_sky\")\n\n"
              "[sub_resource type=\"InputEventAction\" id=\"InputEventAction_quit\"]\naction = &\"ui_cancel\"\n\n"
              "[sub_resource type=\"Shortcut\" id=\"Shortcut_quit\"]\nevents = [SubResource(\"InputEventAction_quit\")]\n\n"
              "[sub_resource type=\"Environment\" id=\"Environment_clear\"]\nbackground_mode = 0\n\n"
              "[sub_resource type=\"BoxOccluder3D\" id=\"BoxOccluder3D_door\"]\nsize = Vector3(2, 3, 0.2)\n\n"
              "[node name=\"Main\" type=\"Node3D\"]\n\n"
              "[node name=\"Env\" type=\"WorldEnvironment\" parent=\".\"]\nenvironment = SubResource(\"Environment_clear\")\n\n"
              "[node name=\"Door\" type=\"OccluderInstance3D\" parent=\".\"]\noccluder = SubResource(\"BoxOccluder3D_door\")\nbake_mask = 4\n\n"
              "[node name=\"Crate\" type=\"RigidBody3D\" parent=\".\"]\nphysics_material_override = ExtResource(\"1_grip\")\naxis_lock_angular_y = true\n\n"
              "[node name=\"Wall\" type=\"StaticBody3D\" parent=\".\"]\nphysics_material_override = SubResource(\"PhysicsMaterial_slick\")\n\n"
              "[node name=\"Floor\" type=\"MeshInstance3D\" parent=\".\"]\nmesh = ExtResource(\"2_tri\")\nmaterial_override = SubResource(\"StandardMaterial3D_sky\")\n"
              "cast_shadow = 0\ngi_mode = 0\nextra_cull_margin = 2.0\nignore_occlusion_culling = true\n\n"
              "[node name=\"Quit\" type=\"Button\" parent=\".\"]\nshortcut = SubResource(\"Shortcut_quit\")\n\n"
              "[node name=\"Rig\" type=\"XROrigin3D\" parent=\".\"]\nworld_scale = 2.0\n\n"
              "[node name=\"Eyes\" type=\"XRCamera3D\" parent=\"Rig\"]\n\n"
              "[node name=\"LeftHand\" type=\"XRController3D\" parent=\"Rig\"]\ntracker = &\"left_hand\"\n");

    const std::string out = (root / "out").generic_string();
    std::string log;
    const int code = runCapture("\"" + exe.string() + "\" fabric \"" + game + "\" \"" + out + "\" --scenes-only", log);
    check(code == 0, "the converter exits 0 (got " + std::to_string(code) + ")");
    if (code != 0) AVER_INFO("{}", log);
    const std::string project = out + "/ConverterBugs";
    const std::string jsonl = slurp(out + "/FabricReport.jsonl");
    check(!jsonl.empty(), "a report was written");

    // ---- C1: script class names ---------------------------------------------------------------------------------------------------------
    AVER_INFO("-- C1 class names --");
    const std::string scripts = slurp(project + "/Fabric/scripts.json");
    check(scripts.find("\"className\": \"SystemScript\"") != std::string::npos && scripts.find("\"className\": \"MathScript\"") != std::string::npos &&
              scripts.find("\"className\": \"TaskScript\"") != std::string::npos,
          "system.gd, math.gd and task.gd are classes SystemScript, MathScript and TaskScript");
    check(scripts.find("\"className\": \"System\"") == std::string::npos && scripts.find("\"className\": \"Math\"") == std::string::npos &&
              scripts.find("\"className\": \"Task\"") == std::string::npos,
          "and no class is named System, Math or Task");
    check(reported(jsonl, {"\"node\":\"System\"", "is class SystemScript"}), "the autoload node System names the renamed class");

    // ---- C12, C13: binary resources and the physics material ----------------------------------------------------------------------------
    AVER_INFO("-- C12 binary files by magic, C13 PhysicsMaterial --");
    check(reported(jsonl, {"\"kind\":\"file\"", "res://grip.phys", "\"rating\":\"converted\"", "RSRC"}), "grip.phys (not a .res) is reported as a binary resource that was read");
    check(reported(jsonl, {"\"kind\":\"file\"", "res://tri.mesh", "\"rating\":\"converted\"", "RSRC"}), "tri.mesh is reported as a binary resource that was read");
    check(!reported(jsonl, {"is not converted yet"}), "no resource is 'not converted yet'");
    check(reported(jsonl, {"\"type\":\"ArrayMesh\"", "decoded 1 surface(s), 3 vertices, 1 triangles"}), "the binary .mesh was decoded: one surface, 3 vertices, 1 triangle");
    check(reported(jsonl, {"\"type\":\"PhysicsMaterial\"", "\"rating\":\"converted\""}) && !reported(jsonl, {"\"type\":\"PhysicsMaterial\"", "\"rating\":\"approximated\""}),
          "a PhysicsMaterial resource is rated converted, not kept as a dictionary");
    check(reported(jsonl, {"\"node\":\"Crate\"", "\"property\":\"physics_material_override\"", "\"rating\":\"converted\""}) &&
              reported(jsonl, {"\"node\":\"Wall\"", "\"property\":\"physics_material_override\"", "\"rating\":\"converted\""}),
          "physics_material_override is rated converted on the rigid body and the static body");
    check(reported(jsonl, {"\"node\":\"Crate\"", "\"property\":\"axis_lock_angular_y\"", "\"rating\":\"converted\""}), "an axis lock is rated converted");
    fmt::OcWorldData w;
    std::string why;
    check(fmt::loadOcworld(project + "/Content/Maps/main.ocworld", w, &why), "the level loads: " + why);
    const std::string crate = propOf(w, find(w, {"Main", "Crate"}), "physics_material_override");
    const std::string wall = propOf(w, find(w, {"Main", "Wall"}), "physics_material_override");
    check(crate.rfind("Object(PhysicsMaterial", 0) == 0 && crate.find("\"friction\": 0.5") != std::string::npos, "the rigid body holds Object(PhysicsMaterial) with the binary file's friction: " + crate);
    check(wall.rfind("Object(PhysicsMaterial", 0) == 0 && wall.find("0.2") != std::string::npos && wall.find("0.6") != std::string::npos,
          "the static body holds the inline material's friction and bounce: " + wall);
    check(find(w, {"Main", "Floor"}) >= 0 && !propOf(w, find(w, {"Main", "Floor"}), "mesh").empty(), "the mesh node that uses the binary mesh has it");

    // ---- C14, C15, C16: project settings --------------------------------------------------------------------------------------------------
    AVER_INFO("-- C14 uid in a setting, C15 editor-only settings, C16 Jolt limits --");
    check(reported(jsonl, {"\"kind\":\"setting\"", "\"property\":\"audio/buses/default_bus_layout\"", "\"rating\":\"converted\"", "AUDIO.BUSLAYOUT"}),
          "the default bus layout named by uid:// is found and converted");
    check(!reported(jsonl, {"\"kind\":\"setting\"", "\"property\":\"audio/buses/default_bus_layout\"", "\"rating\":\"unsupported\""}), "and its setting is not also reported as having no key");
    fmt::ProjectDesc proj;
    std::string err;
    check(fmt::loadOcproject(project + "/ConverterBugs.ocproject", proj, &err), "the project loads: " + err);
    check(proj.audioBusLayout == "Audio/default_bus_layout.ocbus", "AUDIO.BUSLAYOUT names the layout: " + proj.audioBusLayout);
    check(proj.physMaxBodies == 4096 && proj.physMaxBodyPairs == 8192 && proj.physMaxContacts == 16384 && proj.physTempAllocatorMb == 64,
          "PHYSICS.MAXBODIES, MAXBODYPAIRS, MAXCONTACTS and TEMPALLOCMB hold Jolt's four limits (the buffer in MiB)");
    for (const char* key : {"physics/jolt_physics_3d/limits/max_bodies", "physics/jolt_physics_3d/limits/temporary_memory_buffer_size"})
        check(reported(jsonl, {"\"kind\":\"setting\"", key, "\"rating\":\"converted\""}), std::string(key) + " is rated converted");
    check(reported(jsonl, {"\"property\":\"physics/3d/physics_engine\"", "\"rating\":\"converted\"", "Jolt"}), "physics_engine = Jolt Physics reads as Jolt");
    for (const char* key : {"filesystem/import/blender/enabled", "rendering/vram_compression/import_etc2_astc", "memory/multithread/thread_rid_pool_prealloc", "global_group/crates",
                            "application/run/low_processor_mode", "debug/settings/physics_interpolation/enable_warnings", "gui/theme/default_font_generate_mipmaps",
                            "gui/theme/default_font_multichannel_signed_distance_field"}) {
        check(reported(jsonl, {"\"kind\":\"setting\"", (std::string("\"property\":\"") + key + "\"").c_str(), "\"rating\":\"approximated\""}), std::string(key) + " is rated approximated");
    }
    check(reported(jsonl, {"\"kind\":\"setting\"", "\"property\":\"application/config/version\"", "\"rating\":\"approximated\"", "kept for ProjectSettings"}),
          "a setting with no Aver key (application/config/version) is kept for ProjectSettings, not dropped");
    check(reported(jsonl, {"\"kind\":\"setting\"", "\"property\":\"game/difficulty/level\"", "\"rating\":\"approximated\"", "kept for ProjectSettings"}),
          "so is a setting of a section of the game's own");
    check(reported(jsonl, {"\"kind\":\"setting\"", "\"property\":\"filesystem/import/blender/enabled\"", "kept for ProjectSettings"}),
          "an editor-only setting says its value is kept too");
    {
        // Fabric/project.json holds every setting as Godot's own text, mapped ones included, for ProjectSettings.get_setting at run time.
        const std::string pj = slurp(project + "/Fabric/project.json");
        check(pj.find("\"settings\": {") != std::string::npos, "project.json has a settings object");
        check(pj.find("\"application/config/version\": \"\\\"0.9.1\\\"\"") != std::string::npos, "the version is a Godot string: " + pj);
        check(pj.find("\"game/difficulty/level\": \"3\"") != std::string::npos, "an int is Godot's int text");
        check(pj.find("\"game/difficulty/scale\": \"1.5\"") != std::string::npos, "a float is Godot's float text");
        check(pj.find("\"game/difficulty/tint\": \"Color(1, 0, 0, 1)\"") != std::string::npos, "a colour is Godot's constructor text");
        check(pj.find("\"game/difficulty/title\": \"\\\"Hard \\\\\\\"mode\\\\\\\"\\\"\"") != std::string::npos, "an escaped string stays escaped twice over (Godot text, then JSON)");
        check(pj.find("\"application/config/name\": \"\\\"ConverterBugs\\\"\"") != std::string::npos, "a setting that has an Aver key is kept as well");
        check(pj.find("\"config_version\": \"5\"") != std::string::npos, "the file's top-level key is kept");
        check(pj.find("\"input/save\"") != std::string::npos && pj.find("\"autoload/System\"") != std::string::npos, "input actions and autoloads are kept");
    }

    // ---- C17: a Radiance picture ----------------------------------------------------------------------------------------------------------
#if AVER_FABRIC_TEST_OCMAT
    AVER_INFO("-- C17 .hdr --");
    check(reported(jsonl, {"res://sky.hdr", "\"rating\":\"approximated\"", "Radiance HDR"}) && !reported(jsonl, {"res://sky.hdr", "\"rating\":\"unsupported\""}),
          "a .hdr texture is converted (approximated: tone-mapped to 8 bits), not refused");
    for (const char* png : {"/Content/Textures/probe.hdr.png", "/Content/Textures/ConverterBugs/sky.png"}) {
        ImageData img;
        std::string pngErr;
        check(fs::exists(project + png, ec) && decodeImage(project + png, img, &pngErr) && img.width == 2 && img.height == 2, std::string(png) + " is a 2 by 2 picture: " + pngErr);
    }
#endif

    // ---- C18: an input action held by a shortcut -----------------------------------------------------------------------------------------
    AVER_INFO("-- C18 InputEventAction in a Shortcut --");
    const std::string quit = propOf(w, find(w, {"Main", "Quit"}), "shortcut");
    check(quit.find("Object(Shortcut") != std::string::npos && quit.find("Object(InputEventAction") != std::string::npos && quit.find("ui_cancel") != std::string::npos,
          "the shortcut holds Object(InputEventAction) with its action, not a dictionary: " + quit);
    check(reported(jsonl, {"\"type\":\"InputEventAction\"", "\"rating\":\"converted\""}) && !reported(jsonl, {"\"type\":\"InputEventAction\"", "\"rating\":\"approximated\""}),
          "an InputEventAction resource is rated converted");

    // ---- C19: modifiers and wheel notches --------------------------------------------------------------------------------------------------
    AVER_INFO("-- C19 modifiers and the wheel --");
    fmt::OcInputData in;
    check(fmt::loadOcinput(project + "/Content/Input/ConverterBugs.ocinput", in, &err), "the input scheme loads: " + err);
    int ctrlS = 0, bareS = 0, wheelPressed = 0, plainC = 0;
    for (const fmt::OcInputBinding& b : in.bindings) {
        if (b.action == "save" && b.key == "S") {
            if (b.triggers.size() == 1 && b.triggers[0] == "chord:fabric_modifier_ctrl") ++ctrlS;
            else if (b.triggers.empty()) ++bareS;
        }
        if (b.action == "zoom_in" && b.source == fmt::OcInputSource::Key && b.key == "MouseWheelUp" && b.triggers.empty()) ++wheelPressed;
        if (b.action == "plain" && b.key == "C" && b.triggers.empty()) ++plainC;
    }
    check(ctrlS == 1 && bareS == 1, "Ctrl+S and plain S are two bindings of save: the first carries a chord trigger on the Ctrl action, the second none");
    check(wheelPressed == 1, "the wheel notch is the engine's wheel-up key slot (a notch presses it for a frame), with no trigger");
    check(plainC == 1, "a key with no modifier gets no trigger");
    bool modifierAction = false, modifierBound = false;
    for (const fmt::OcInputAction& a : in.actions) modifierAction = modifierAction || a.name == "fabric_modifier_ctrl";
    for (const fmt::OcInputBinding& b : in.bindings) modifierBound = modifierBound || (b.action == "fabric_modifier_ctrl" && b.source == fmt::OcInputSource::Key && b.key == "LeftCtrl");
    check(modifierAction && modifierBound, "the Ctrl action exists and is bound to the left Ctrl key");
    check(reported(jsonl, {"\"node\":\"save\"", "\"rating\":\"approximated\"", "chord trigger"}), "the modifier is reported (left key only) instead of being dropped");

    // ---- F68: XR nodes load as inert carriers ---------------------------------------------------------------------------------------------
    check(reported(jsonl, {"\"type\":\"XROrigin3D\"", "\"rating\":\"approximated\"", "inert carrier"}) && reported(jsonl, {"\"type\":\"XRCamera3D\"", "\"rating\":\"approximated\""}) &&
              reported(jsonl, {"\"type\":\"XRController3D\"", "\"rating\":\"approximated\""}),
          "the XR nodes are placed as their own classes and rated approximated, with the reason");
    check(!reported(jsonl, {"\"type\":\"XROrigin3D\"", "\"rating\":\"unsupported\""}), "an XROrigin3D is not dropped");

    // ---- P43 / P44: the wider key set, the side buttons and sideways wheel, joypad extras, and half axes -------------------------------------
    AVER_INFO("-- the wider input set and half axes --");
    {
        const auto hasKey = [&](const char* action, const char* key) {
            for (const fmt::OcInputBinding& b : in.bindings)
                if (b.action == action && b.source == fmt::OcInputSource::Key && b.key == key && b.triggers.empty()) return true;
            return false;
        };
        const auto hasPad = [&](const char* action, const char* button) {
            for (const fmt::OcInputBinding& b : in.bindings)
                if (b.action == action && b.source == fmt::OcInputSource::GamepadButton && b.key == button) return true;
            return false;
        };
        check(hasKey("extras", "F5") && hasKey("extras", "Kp5") && hasKey("extras", "Period") && hasKey("extras", "KpEnter"), "F5, numpad 5, the period and KP Enter each get a key binding");
        check(hasKey("extras", "MouseX1") && hasKey("extras", "MouseWheelLeft"), "mouse button 8 (side) and 6 (wheel left) become the engine's key slots");
        check(hasPad("extras", "Touchpad") && hasPad("extras", "Guide"), "joypad buttons 20 and 5 are the touchpad and Guide");
        const auto half = [&](const char* action, const char* axisName, float scale) {
            for (const fmt::OcInputBinding& b : in.bindings)
                if (b.action == action && b.source == fmt::OcInputSource::GamepadAxis && b.key == axisName && b.scale == scale && b.modifiers.size() == 1 && b.modifiers[0] == "clamp") return true;
            return false;
        };
        check(half("look_left", "LeftX", -1.0f) && half("look_right", "LeftX", 1.0f), "the two halves of an axis are a negated and a plain binding, each clamped to 0..1");
        check(half("look_down", "LeftY", -1.0f), "Godot's stick Y (down positive) is flipped to XInput's, still one clamped half");
        std::string raw = slurp(project + "/Content/Input/ConverterBugs.ocinput");
        check(raw.rfind("OCINPUT 2\n", 0) == 0, "a scheme with a clamp modifier is written as OCINPUT 2");
        check(!reported(jsonl, {"\"node\":\"look_left\"", "\"rating\":\"approximated\"", "never negative"}), "the half axis is not reported as approximated any more");
    }

    // ---- C20: per-instance rendering flags -----------------------------------------------------------------------------------------------
    AVER_INFO("-- C20 dropped rendering properties --");
    // Wave 4 (P5): the engine has the per-instance flags now, so none is unsupported: shadows off and GI disabled are approximated (the ray-traced sun shadow still sees the mesh; it is
    // still lit), the margin and the occlusion opt-out are converted.
    for (const char* prop : {"cast_shadow", "gi_mode"})
        check(reported(jsonl, {"\"node\":\"Floor\"", (std::string("\"property\":\"") + prop + "\"").c_str(), "\"rating\":\"approximated\""}), std::string(prop) + " has a report row, rated approximated");
    for (const char* prop : {"extra_cull_margin", "ignore_occlusion_culling"})
        check(reported(jsonl, {"\"node\":\"Floor\"", (std::string("\"property\":\"") + prop + "\"").c_str(), "\"rating\":\"converted\""}), std::string(prop) + " has a report row, rated converted");

    // ---- F33, F36: occluders, anti-aliasing and the clear colour (the package API; the project and scene half of what Aver.Fabric gained) ----------------------
    AVER_INFO("-- F33 occluders, F36 anti-aliasing and clear colour --");
    check(reported(jsonl, {"\"node\":\"Door\"", "\"type\":\"OccluderInstance3D\"", "\"rating\":\"approximated\"", "occlusion culling is automatic"}) &&
              !reported(jsonl, {"\"node\":\"Door\"", "not in the Aver.Fabric class table"}),
          "an OccluderInstance3D is placed as its own class, rated approximated (kept, unused)");
    check(reported(jsonl, {"\"type\":\"BoxOccluder3D\"", "\"rating\":\"approximated\"", "occlusion culling is automatic"}) && !reported(jsonl, {"\"type\":\"BoxOccluder3D\"", "kept as a dictionary"}),
          "its BoxOccluder3D is an Object, not a dictionary");
    const int door = find(w, {"Main", "Door"});
    const std::string occluder = propOf(w, door, "occluder");
    check(occluder.rfind("Object(BoxOccluder3D", 0) == 0 && occluder.find("Vector3(2, 3, 0.2)") != std::string::npos, "the node holds Object(BoxOccluder3D) with the size: " + occluder);
    check(fmt::loadOcproject(project + "/ConverterBugs.ocproject", proj, &err) && proj.neuraa == 1 && proj.taa == 1, "RENDER.NEURAA and RENDER.TAA are 1 (screen_space_aa = FXAA, use_taa)");
    check(reported(jsonl, {"\"kind\":\"setting\"", "rendering/anti_aliasing/quality/screen_space_aa", "\"rating\":\"approximated\"", "RENDER.NEURAA 1"}), "screen_space_aa is rated approximated");
    check(reported(jsonl, {"\"kind\":\"setting\"", "rendering/anti_aliasing/quality/use_taa", "\"rating\":\"approximated\"", "RENDER.TAA 1"}), "use_taa is rated approximated");
    check(reported(jsonl, {"\"kind\":\"setting\"", "rendering/environment/defaults/default_clear_color", "\"rating\":\"approximated\""}), "the default clear colour has a row");
    check(w.hasSky && !w.skyPhysical && w.skyZenith[2] > w.skyZenith[0] * 2.0 && w.skyZenith[1] > w.skyZenith[0],
          "a Clear Color Environment becomes a flat sky of the project's clear colour (blue above green above red), not the default grey");

    AVER_INFO("=== {} checks, {} failed ===", g_checks, g_failures);
    if (!g_failures) fs::remove_all(root, ec);
    return exitCode(g_failures ? ExitCode::Failed : ExitCode::Ok);
}
