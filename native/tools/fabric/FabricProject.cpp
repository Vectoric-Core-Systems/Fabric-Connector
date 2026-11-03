// project.godot to .ocproject, the [input] map to .ocinput, translations to Content/Localization.
#include "FabricInternal.hpp"

#include "aver/core/Version.hpp"
#include "aver/formats/AxisMap.hpp"
#include "aver/formats/godot/GodotEnvironment.hpp"
#include "aver/formats/godot/GodotRender.hpp"
#include "aver/formats/Json.hpp"
#include "aver/formats/OcInput.hpp"
#include "aver/formats/OcProject.hpp"
#include "aver/platform/FileSystem.hpp"

#include <cmath>
#include <cstdio>
#include <filesystem>

namespace aver::fabric {

double Converter::setting(const char* path, double def) const {
    const Var* v = project_.setting(path);
    return numOf(v, def);
}

std::string Converter::settingString(const char* path, const std::string& def) const {
    const std::string value = stringOf(project_.setting(path), def);
    // A setting that names a resource may write it as uid://...; the project's uid table says which file that is.
    if (gd::GodotProjectFs::isUid(value))
        if (const std::string res = fs_.resolve(value, ""); !res.empty()) return res;
    return value;
}

bool Converter::settingBool(const char* path, bool def) const { return boolOf(project_.setting(path), def); }

namespace {

constexpr const char* kProject = "res://project.godot";

// The "version" of <extension dir>/extension.json, or empty when the manifest cannot be read.
std::string manifestVersion(const std::string& extensionDir) {
    std::string text;
    fmt::JsonValue root;
    if (!readFileText(extensionDir + "/extension.json", text) || !fmt::parseJson(text, root)) return {};
    return std::string(root["version"].asString());
}

// Godot's Key enum values for the keys Aver's Key enum has (scripting/csharp/Aver.Framework/Input.cs).
const char* averKeyName(long long code) {
    static char buf[4];
    if (code >= 65 && code <= 90) { buf[0] = static_cast<char>(code); buf[1] = 0; return buf; }
    if (code >= 48 && code <= 57) { buf[0] = 'D'; buf[1] = static_cast<char>(code); buf[2] = 0; return buf; }
    switch (code) {
        case 32: return "Space";
        case 4194325: return "LeftShift";
        case 4194326: return "LeftCtrl";
        case 4194328: return "LeftAlt";
        case 4194327: return "Meta";
        case 4194309: return "Enter";
        case 4194310: return "KpEnter";
        case 4194305: return "Escape";
        case 4194306: return "Tab";
        case 4194319: return "Left";
        case 4194320: return "Up";
        case 4194321: return "Right";
        case 4194322: return "Down";
        // Navigation and editing keys.
        case 4194308: return "Backspace";
        case 4194311: return "Insert";
        case 4194312: return "Delete";
        case 4194313: return "Pause";
        case 4194314: return "PrintScreen";
        case 4194317: return "Home";
        case 4194318: return "End";
        case 4194323: return "PageUp";
        case 4194324: return "PageDown";
        case 4194329: return "CapsLock";
        case 4194330: return "NumLock";
        case 4194331: return "ScrollLock";
        case 4194370: return "Menu";
        // Punctuation is its ASCII code in Godot (the US layout's key).
        case 45: return "Minus";
        case 61: return "Equal";
        case 91: return "LBracket";
        case 93: return "RBracket";
        case 92: return "Backslash";
        case 59: return "Semicolon";
        case 39: return "Apostrophe";
        case 44: return "Comma";
        case 46: return "Period";
        case 47: return "Slash";
        case 96: return "Grave";
        // The numpad.
        case 4194433: return "KpMultiply";
        case 4194434: return "KpDivide";
        case 4194435: return "KpSubtract";
        case 4194436: return "KpDecimal";
        case 4194437: return "KpAdd";
        default: break;
    }
    // F1..F12 (Godot has F13 to F35 as well; Aver's Key enum stops at F12) and Kp0..Kp9.
    static char fbuf[8];
    if (code >= 4194332 && code <= 4194343) { std::snprintf(fbuf, sizeof fbuf, "F%d", static_cast<int>(code - 4194331)); return fbuf; }
    if (code >= 4194438 && code <= 4194447) { std::snprintf(fbuf, sizeof fbuf, "Kp%d", static_cast<int>(code - 4194438)); return fbuf; }
    return nullptr;
}

// Godot 3 wrote a special key (arrows, Enter, Tab, ...) as 1<<24 | n and Godot 4 writes 1<<22 | n; the low bits are the same.
// Old projects keep the old codes in their [input] section. Returns the Godot 4 code, or 0 when `code` is not a Godot 3 special key.
long long legacySpecialKey(long long code) {
    constexpr long long kOld = 1LL << 24, kNew = 1LL << 22;
    return (code >> 24) == 1 ? (code & (kOld - 1)) | kNew : 0;
}

// The names of Godot 4's special keys (1<<22 | n), for the report.
const char* godotSpecialKeyName(long long n) {
    static const char* const names[] = {"", "Escape", "Tab", "Backtab", "Backspace", "Enter", "KP Enter", "Insert", "Delete", "Pause", "Print", "SysReq",
                                        "Clear", "Home", "End", "Left", "Up", "Right", "Down", "PageUp", "PageDown", "Shift", "Ctrl", "Meta", "Alt",
                                        "CapsLock", "NumLock", "ScrollLock"};
    return n >= 1 && n < static_cast<long long>(sizeof(names) / sizeof(names[0])) ? names[n] : nullptr;
}

// Godot 3's joypad buttons, which the same old projects keep: the d-pad is 12 to 15 there and 11 to 14 in Godot 4, the shoulders 4 and 5,
// the triggers buttons 6 and 7 (axes in Godot 4), the sticks 8 and 9, Select 10, Start 11. Null for a trigger (see the caller).
const char* averPadButtonLegacy(long long idx) {
    switch (idx) {
        case 0: return "A";
        case 1: return "B";
        case 2: return "X";
        case 3: return "Y";
        case 4: return "LeftShoulder";
        case 5: return "RightShoulder";
        case 8: return "LeftThumb";
        case 9: return "RightThumb";
        case 10: return "Back";
        case 11: return "Start";
        case 12: return "DPadUp";
        case 13: return "DPadDown";
        case 14: return "DPadLeft";
        case 15: return "DPadRight";
        default: return nullptr;
    }
}

const char* averPadButton(long long idx) {
    switch (idx) {
        case 0: return "A";
        case 1: return "B";
        case 2: return "X";
        case 3: return "Y";
        case 4: return "Back";
        case 6: return "Start";
        case 7: return "LeftThumb";
        case 8: return "RightThumb";
        case 9: return "LeftShoulder";
        case 10: return "RightShoulder";
        case 11: return "DPadUp";
        case 12: return "DPadDown";
        case 13: return "DPadLeft";
        case 14: return "DPadRight";
        case 5: return "Guide";
        case 15: return "Misc1";
        case 16: return "Paddle1";
        case 17: return "Paddle2";
        case 18: return "Paddle3";
        case 19: return "Paddle4";
        case 20: return "Touchpad";
        default: return nullptr;   // 21 and up: no such Godot button
    }
}

const char* averPadAxis(long long idx) {
    switch (idx) {
        case 0: return "LeftX";
        case 1: return "LeftY";
        case 2: return "RightX";
        case 3: return "RightY";
        case 4: return "LeftTrigger";
        case 5: return "RightTrigger";
        default: return nullptr;
    }
}

std::string keyDescription(long long code) {
    if (const char* n = averKeyName(code)) return n;
    if ((code >> 22) == 1)
        if (const char* s = godotSpecialKeyName(code & ((1LL << 22) - 1))) return s;
    if ((code >> 22) == 1 && (code & ((1LL << 22) - 1)) >= 28 && (code & ((1LL << 22) - 1)) < 28 + 35) return "F" + std::to_string((code & ((1LL << 22) - 1)) - 27);
    if (code > 32 && code < 127) return std::string("'") + static_cast<char>(code) + "'";
    return "keycode " + std::to_string(code);
}

const char* gamepadButtonName(long long idx, bool legacy) {
    if (legacy) return idx == 6 ? "L2 (a trigger)" : idx == 7 ? "R2 (a trigger)" : "unknown";
    switch (idx) {
        case 5: return "Guide";
        case 15: return "Misc 1";
        case 16: return "Paddle 1";
        case 17: return "Paddle 2";
        case 18: return "Paddle 3";
        case 19: return "Paddle 4";
        case 20: return "Touchpad";
        default: return "unknown";
    }
}

constexpr const char* kAverKeySet =
    "A-Z, 0-9, F1-F12, Space, Enter, Escape, Tab, Backspace, the arrows, Home, End, Page Up and Down, Insert, Delete, the punctuation keys, the numpad, "
    "the lock keys, Shift, Ctrl, Alt, Meta, Menu, and the mouse buttons";

}  // namespace

void Converter::convertInput() {
    if (project_.inputActions.empty()) return;
    fmt::OcInputData data;
    data.name = name_;
    data.contextName = name_ + "Input";
    data.contextPriority = 0;

    // A key or mouse button that needs Shift, Ctrl or Alt held keeps that need: its binding carries a chord trigger on an action bound to the modifier key.
    // Those actions are written after the project's own, one per modifier that turned up (Aver's Key enum has the left Shift, Ctrl and Alt keys only).
    std::map<std::string, std::string> modifierKeys;   // action name -> Aver key name
    const auto requireModifiers = [&](const Var& ev, fmt::OcInputBinding& b, const gd::GodotInputAction& act, const std::string& cls) {
        const struct { const char* flag; const char* word; const char* key; } mods[] = {
            {"shift_pressed", "shift", "LeftShift"}, {"ctrl_pressed", "ctrl", "LeftCtrl"}, {"alt_pressed", "alt", "LeftAlt"}, {"meta_pressed", "meta", "Meta"}};
        std::string held;
        for (const auto& m : mods) {
            if (!boolOf(ev.find(m.flag), false)) continue;
            const std::string name = std::string("fabric_modifier_") + m.word;
            modifierKeys[name] = m.key;
            fmt::OcTriggerSpec chord;
            chord.kind = fmt::OcTriggerKind::Chord;
            chord.refs.push_back(name);
            b.triggers.push_back(fmt::formatTriggerSpec(chord));
            held += (held.empty() ? "" : ", ") + std::string(m.word);
        }
        if (!held.empty())
            report_.add("input", kProject, act.name, cls, "", Rating::Approximated,
                        held + " must be held: a chord trigger on an action bound to the " + held + " key (the left Shift, Ctrl and Alt only; either Meta key counts)", act.line);
    };

    for (const gd::GodotInputAction& act : project_.inputActions) {
        fmt::OcInputAction a;
        a.name = act.name;
        std::vector<fmt::OcInputBinding> binds;
        bool axis = false;
        int converted = 0, unmapped = 0, empty = 0;
        const auto skip = [&](const std::string& why, int line) {
            ++unmapped;
            report_.add("input", kProject, act.name, "InputEvent", "", Rating::Unsupported, why, line);
        };
        const auto keyCodeOf = [](const Var& ev) {
            long long code = static_cast<long long>(numOf(ev.find("keycode"), 0));
            if (code == 0) code = static_cast<long long>(numOf(ev.find("physical_keycode"), 0));
            if (code == 0) code = static_cast<long long>(numOf(ev.find("key_label"), 0));
            return code;
        };
        // An action that still holds Godot 3 key codes holds Godot 3 joypad numbers too (the old d-pad is 12 to 15).
        bool legacyPad = false;
        for (const Var& ev : act.events)
            if (ev.type == VType::Object && ev.s == "InputEventKey" && legacySpecialKey(keyCodeOf(ev)) != 0) legacyPad = true;
        for (const Var& ev : act.events) {
            if (ev.type != VType::Object) { skip("an event that is not an InputEvent object", act.line); continue; }
            const std::string& cls = ev.s;
            fmt::OcInputBinding b;
            b.action = act.name;
            if (cls == "InputEventKey") {
                long long code = keyCodeOf(ev);
                if (code == 0) {
                    ++empty;
                    report_.add("input", kProject, act.name, cls, "", Rating::Approximated,
                                "an empty key event (no keycode, physical keycode or key label) binds no key in Godot either; nothing is written for it", act.line);
                    continue;
                }
                const long long modern = legacySpecialKey(code);
                if (modern) code = modern;
                const char* n = averKeyName(code);
                if (!n) {
                    skip(std::string("the ") + keyDescription(code) + " key" + (modern ? " (in Godot 3's key encoding)" : "") + " is not in Aver's Key enum (" + kAverKeySet + ")", act.line);
                    continue;
                }
                if (modern)
                    report_.add("input", kProject, act.name, cls, "", Rating::Approximated,
                                "the key code is in Godot 3's encoding (special-key flag 1<<24, Godot 4 uses 1<<22); read as the " + keyDescription(code) + " key", act.line);
                b.source = fmt::OcInputSource::Key;
                b.key = n;
                requireModifiers(ev, b, act, cls);
            } else if (cls == "InputEventMouseButton") {
                const int idx = static_cast<int>(numOf(ev.find("button_index"), 0));
                // Godot's button indices: 1 to 3 the three buttons, 4 to 7 the wheel up, down, left, right (each notch a press), 8 and 9 the side buttons.
                static const char* const kMouseKeys[] = {"", "MouseLeft", "MouseRight", "MouseMiddle", "MouseWheelUp", "MouseWheelDown", "MouseWheelLeft",
                                                         "MouseWheelRight", "MouseX1", "MouseX2"};
                if (idx >= 1 && idx <= 9) {
                    b.source = fmt::OcInputSource::Key;
                    b.key = kMouseKeys[idx];
                    requireModifiers(ev, b, act, cls);
                } else {
                    skip("mouse button " + std::to_string(idx) + " is not one of Godot's nine mouse buttons", act.line);
                    continue;
                }
            } else if (cls == "InputEventJoypadButton") {
                const long long idx = static_cast<long long>(numOf(ev.find("button_index"), -1));
                if (legacyPad && (idx == 6 || idx == 7)) {   // Godot 3's L2 and R2 are buttons; Aver reads the triggers as axes
                    b.source = fmt::OcInputSource::GamepadAxis;
                    b.key = idx == 6 ? "LeftTrigger" : "RightTrigger";
                    axis = true;
                    report_.add("input", kProject, act.name, cls, "", Rating::Approximated,
                                "Godot 3 joypad numbering (this action holds Godot 3 key codes): the trigger button is bound as Aver's trigger axis", act.line);
                    ++converted;
                    binds.push_back(std::move(b));
                    continue;
                }
                const char* n = legacyPad ? averPadButtonLegacy(idx) : averPadButton(idx);
                if (!n) {
                    skip("gamepad button " + std::to_string(idx) + " (" + gamepadButtonName(idx, legacyPad) + ") is not in Aver's GamepadButton enum (A, B, X, Y, Back, Guide, Start, the two thumbs, the two shoulders, the d-pad, Misc1, the four paddles, the touchpad)",
                         act.line);
                    continue;
                }
                if (legacyPad)
                    report_.add("input", kProject, act.name, cls, "", Rating::Approximated,
                                "Godot 3 joypad numbering (this action holds Godot 3 key codes): button " + std::to_string(idx) + " read as " + n, act.line);
                b.source = fmt::OcInputSource::GamepadButton;
                b.key = n;
            } else if (cls == "InputEventJoypadMotion") {
                const long long idx = static_cast<long long>(numOf(ev.find("axis"), -1));
                const char* n = averPadAxis(idx);
                if (!n) { skip("gamepad axis " + std::to_string(idx) + " is not in Aver's gamepad set", act.line); continue; }
                b.source = fmt::OcInputSource::GamepadAxis;
                b.key = n;
                // Godot's stick Y runs down; XInput's (and so Aver's) runs up. A motion event names one HALF of the axis (its axis_value's sign), and
                // the action's strength is never negative: the binding is signed to read that half positive and clamped so the other half reads nothing.
                const double value = numOf(ev.find("axis_value"), 1.0);
                const bool yAxis = idx == 1 || idx == 3;
                b.scale = static_cast<float>((value < 0 ? -1.0 : 1.0) * (yAxis ? -1.0 : 1.0));
                fmt::OcModifierSpec half;
                half.kind = fmt::OcModifierKind::Clamp;
                half.v[0] = 0.0f;
                half.v[1] = 1.0f;
                b.modifiers.push_back(fmt::formatModifierSpec(half));
                axis = true;
                if (yAxis)
                    report_.add("input", kProject, act.name, cls, "", Rating::Converted, "the stick's Y direction is flipped to XInput's; the binding reads the half of the axis the event names", act.line);
            } else {
                skip(cls + " is not an input the converter maps (mouse motion, touch and shortcuts are not bound by action)", act.line);
                continue;
            }
            // Two events can name one Aver input (Enter and KP Enter): one binding is enough.
            bool duplicate = false;
            for (const fmt::OcInputBinding& o : binds)
                duplicate = duplicate || (o.source == b.source && o.key == b.key && o.scale == b.scale && o.triggers == b.triggers);
            ++converted;
            if (!duplicate) binds.push_back(std::move(b));
        }
        a.type = axis ? fmt::OcInputValueType::Axis1D : fmt::OcInputValueType::Digital;
        if (axis) {
            fmt::OcModifierSpec dz;
            dz.kind = fmt::OcModifierKind::DeadZone;
            dz.v[0] = static_cast<f32>(act.deadzone);
            dz.v[1] = 1.0f;
            a.modifiers.push_back(fmt::formatModifierSpec(dz));
        }
        const size_t written = binds.size();
        data.actions.push_back(std::move(a));
        for (auto& b : binds) data.bindings.push_back(std::move(b));
        if (converted > 0) report_.add("input", kProject, act.name, "InputAction", "", unmapped ? Rating::Approximated : Rating::Converted,
                                       std::to_string(written) + " binding(s)" + (unmapped ? ", " + std::to_string(unmapped) + " not mapped" : ""), act.line);
        else if (empty > 0 && unmapped == 0)
            report_.add("input", kProject, act.name, "InputAction", "", Rating::Approximated, "its only event is an empty key event; the action is written with no bindings", act.line);
        else report_.add("input", kProject, act.name, "InputAction", "", Rating::Unsupported,
                         "none of its events could be mapped (" + std::to_string(unmapped) + " event(s) are outside Aver's input set, see the rows above)", act.line);
    }
    for (const auto& [name, key] : modifierKeys) {
        fmt::OcInputAction a;
        a.name = name;
        a.type = fmt::OcInputValueType::Digital;
        data.actions.push_back(std::move(a));
        fmt::OcInputBinding b;
        b.action = name;
        b.source = fmt::OcInputSource::Key;
        b.key = key;
        data.bindings.push_back(std::move(b));
    }
    report_.add("input", kProject, "", "InputMap", "", Rating::Approximated,
                "only the actions the project overrides are written; Godot's built-in ui_* actions are not", 0);

    const std::string path = contentDir_ + "/Input/" + name_ + ".ocinput";
    createDirectories(contentDir_ + "/Input");
    if (!writeFileText(path, fmt::writeOcinput(data))) {
        report_.add("input", kProject, "", "InputMap", "", Rating::Unsupported, "could not write " + path, 0);
        return;
    }
    fmt::OcInputData back;
    std::string err;
    if (!fmt::loadOcinput(path, back, &err))
        report_.add("input", kProject, "", "InputMap", "", Rating::Unsupported, "the written .ocinput did not read back: " + err, 0);
}

void Converter::convertLocalization() {
    const Var* list = project_.setting("internationalization/locale/translations");
    if (!list) return;
    for (const std::string& ref : list->strs) {
        const std::string res = gd::GodotProjectFs::normalizeRes(ref);
        const std::string disk = fs_.toDiskPath(res);
        const size_t d = res.find_last_of('.');
        const std::string ext = d == std::string::npos ? std::string{} : lower(res.substr(d));
        if (ext != ".po") {
            report_.add("setting", kProject, "", "Translation", "internationalization/locale/translations", Rating::Unsupported,
                        res + ": only .po translations are converted (resave a .translation or .csv as .po)", 0);
            continue;
        }
        std::string text;
        if (disk.empty() || !readFileText(disk, text)) {
            report_.add("setting", kProject, "", "Translation", "internationalization/locale/translations", Rating::Unsupported, res + " is missing", 0);
            continue;
        }
        // The culture is the file's Language header, else its name.
        std::string culture;
        const size_t at = text.find("Language:");
        if (at != std::string::npos) {
            size_t b = at + 9;
            while (b < text.size() && text[b] == ' ') ++b;
            size_t e = b;
            while (e < text.size() && text[e] != '\\' && text[e] != '\n' && text[e] != '"' && text[e] != ' ') ++e;
            culture = text.substr(b, e - b);
        }
        if (culture.empty()) culture = stemOf(res);
        culture = safeName(culture, "en");
        const std::string dst = contentDir_ + "/Localization/" + culture + "/" + stemOf(res) + ".po";
        createDirectories(std::filesystem::path(dst).parent_path().string());
        if (!writeFileText(dst, text)) {
            report_.add("setting", kProject, "", "Translation", "", Rating::Unsupported, "could not write " + dst, 0);
            continue;
        }
        report_.add("setting", kProject, "", "Translation", "", Rating::Converted, res + " copied to Localization/" + culture + "/", 0);
    }
}

bool Converter::convertProject(const std::string& startLevelAsset) {
    fmt::ProjectDesc d;
    d.name = name_;
    d.engineName = std::string(kEngineName);
    d.engineMinVersion = std::string(kEngineVersion);
    d.createdWith = std::string(kEngineVersion);
    d.contentRoot = "Content";
    d.startMap = startLevelAsset;
    // The engine's lighting as a new Aver project has it (ray-traced GI, ray-traced shadows and reflections, the denoiser, the upscaler); what the Godot
    // project states (tonemap, exposure, glow, anti-aliasing, 3D scale, LOD) is set over it below. Godot exposes a fixed amount unless the start scene's
    // CameraAttributes ask for auto exposure, so that starts off.
    fmt::applyNewProjectRenderDefaults(d);
    d.postAutoExposure = 0;
    report_.add("setting", kProject, "", "", "rendering", Rating::Converted,
                "RENDER.GI / RAYTRACING / GIMODE / DENOISER / AVERSR: the engine's own lighting, as a new Aver project starts with; the settings below that "
                "the project states are applied over it", 0);
    // The project's scripts and nodes run on this extension's managed runtime; opening it without the extension installed says so by name.
    d.extensions.push_back({"FabricConnector", manifestVersion(extensionDir())});
    // Godot's rules: no default pawn and no drone, the view follows the current Camera3D (Aver.Fabric's FabricGameMode, declared by the extension).
    d.gameMode = "FabricGameMode";
    if (!project_.inputActions.empty()) d.inputScheme = "Input/" + name_ + ".ocinput";
    d.localeDefault = settingString("internationalization/locale/fallback", "en");
    d.audioBusLayout = audioBusLayout_;   // G5 S1: the default AudioBusLayout converted by convertStandaloneResources, "" when the project has none

    // Window.
    d.windowTitle = project_.name;
    d.windowWidth = static_cast<int>(setting("display/window/size/viewport_width", 1152));
    d.windowHeight = static_cast<int>(setting("display/window/size/viewport_height", 648));
    d.windowResizable = settingBool("display/window/size/resizable", true) ? 1 : 0;
    const int mode = static_cast<int>(setting("display/window/size/mode", 0));
    d.windowFullscreen = (mode == 3 || mode == 4) ? 1 : 0;
    report_.add("setting", kProject, "", "", "display/window/size", Rating::Converted, "WINDOW.SIZE / RESIZABLE / FULLSCREEN", 0);
    if (mode == 4)
        report_.add("setting", kProject, "", "", "display/window/size/mode", Rating::Approximated,
                    "exclusive fullscreen is borderless fullscreen: Aver never takes over the display mode (it breaks alt-tab)", 0);
    // Vsync: Godot's modes are 0 disabled, 1 enabled (the default), 2 adaptive, 3 mailbox; Aver's WINDOW.VSYNC counts 0 fifo, 1 immediate, 2 adaptive, 3 mailbox.
    if (project_.setting("display/window/vsync/vsync_mode")) {
        const int vsync = static_cast<int>(setting("display/window/vsync/vsync_mode", 1));
        if (vsync >= 0 && vsync <= 3) {
            d.windowVsync = vsync == 0 ? 1 : vsync == 1 ? 0 : vsync;
            report_.add("setting", kProject, "", "", "display/window/vsync/vsync_mode", Rating::Converted, "WINDOW.VSYNC " + std::to_string(d.windowVsync), 0);
        }
    }
    if (project_.setting("application/run/max_fps")) {
        const int maxFps = static_cast<int>(setting("application/run/max_fps", 0));
        if (maxFps > 0) {
            d.windowMaxFps = maxFps;
            report_.add("setting", kProject, "", "", "application/run/max_fps", Rating::Converted, "WINDOW.MAXFPS " + std::to_string(maxFps), 0);
        }
    }
    if (project_.setting("display/window/size/borderless") && boolOf(project_.setting("display/window/size/borderless"), false))
        report_.add("setting", kProject, "", "", "display/window/size/borderless", Rating::Approximated,
                    "a borderless window is not an .ocproject key; a script can set it with Window.Borderless", 0);
    if (project_.setting("display/window/size/always_on_top") && boolOf(project_.setting("display/window/size/always_on_top"), false))
        report_.add("setting", kProject, "", "", "display/window/size/always_on_top", Rating::Approximated,
                    "an always-on-top window is not an .ocproject key; a script can set it with Window.AlwaysOnTop", 0);
    convertProjectUi(d);   // G5 U1: stretch mode, aspect, scale, the project theme, the tooltip delay

    // Physics: Godot's rules, stated.
    const V3 gdir = vec3Of(project_.setting("physics/3d/default_gravity_vector"), {0, -1, 0});
    const double gmag = setting("physics/3d/default_gravity", 9.8);
    const Vec3 g = fmt::axis::position(static_cast<f32>(gdir.x * gmag), static_cast<f32>(gdir.y * gmag), static_cast<f32>(gdir.z * gmag));
    d.hasGravity = true;
    d.gravity[0] = g.x + 0.0f; d.gravity[1] = g.y + 0.0f; d.gravity[2] = g.z + 0.0f;   // + 0.0f: no negative zero in the file
    const double ticks = std::max(1.0, setting("physics/common/physics_ticks_per_second", 60));
    d.fixedStep = static_cast<f32>(1.0 / ticks);
    d.physCombine = 1;   // PHYSICS.COMBINE material: friction min or max when rough, bounce a clamped sum
    d.physInterpolation = settingBool("physics/common/physics_interpolation", false) ? 1 : 0;
    report_.add("setting", kProject, "", "", "physics/3d/default_gravity", Rating::Converted, "PHYSICS.GRAVITY, converted to centimetres and Aver's axes", 0);
    report_.add("setting", kProject, "", "", "physics/common/physics_ticks_per_second", Rating::Converted, "PHYSICS.FIXEDSTEP", 0);
    report_.add("setting", kProject, "", "", "physics/common/physics_interpolation", Rating::Converted, "PHYSICS.INTERPOLATION", 0);
    report_.add("setting", kProject, "", "", "physics", Rating::Converted, "PHYSICS.COMBINE material", 0);

    // Jolt's limits are the engine's own PHYSICS.* keys. Both count the temporary memory buffer in MiB (Godot's default is 32).
    const struct { const char* setting; int* field; const char* key; } joltLimits[] = {
        {"physics/jolt_physics_3d/limits/max_bodies", &d.physMaxBodies, "PHYSICS.MAXBODIES"},
        {"physics/jolt_physics_3d/limits/max_body_pairs", &d.physMaxBodyPairs, "PHYSICS.MAXBODYPAIRS"},
        {"physics/jolt_physics_3d/limits/max_contact_constraints", &d.physMaxContacts, "PHYSICS.MAXCONTACTS"},
        {"physics/jolt_physics_3d/limits/temporary_memory_buffer_size", &d.physTempAllocatorMb, "PHYSICS.TEMPALLOCMB"}};
    for (const auto& limit : joltLimits) {
        const Var* v = project_.setting(limit.setting);
        if (!v) continue;
        const double n = numOf(v, 0);
        if (n >= 1 && n <= 2147483647.0) {
            *limit.field = static_cast<int>(n);
            report_.add("setting", kProject, "", "", limit.setting, Rating::Converted, std::string(limit.key) + " " + std::to_string(*limit.field), 0);
        } else {
            report_.add("setting", kProject, "", "", limit.setting, Rating::Unsupported, "not a positive whole number; the engine's own limit stays", 0);
        }
    }
    // Godot has no limit on the meshes a script builds; the engine's guard of 256 is raised for a converted project (voxel worlds build a mesh per chunk).
    d.runtimeMeshes = 1024;
    report_.add("setting", kProject, "", "", "", Rating::Converted, "RENDER.RUNTIMEMESHES 1024: the most meshes scripts may build at run time (the engine's guard is 256); unmeasured per mesh", 0);

    // Godot's catch-up cap per frame (default 8) is the engine's PHYSICS.MAXSTEPS, which takes 1 to 64.
    if (const Var* steps = project_.setting("physics/common/max_physics_steps_per_frame")) {
        const double n = numOf(steps, 8);
        if (n >= 1 && n <= 64) {
            d.physMaxSteps = static_cast<int>(n);
            report_.add("setting", kProject, "", "", "physics/common/max_physics_steps_per_frame", Rating::Converted, "PHYSICS.MAXSTEPS " + std::to_string(d.physMaxSteps), 0);
        } else {
            d.physMaxSteps = n < 1 ? 1 : 64;
            report_.add("setting", kProject, "", "", "physics/common/max_physics_steps_per_frame", Rating::Approximated, "PHYSICS.MAXSTEPS takes 1 to 64; " + std::to_string(d.physMaxSteps) + " is used", 0);
        }
    }
    // Aver always simulates with Jolt: the project's choice of engine changes nothing but how closely the bodies match what Godot's own engine would do.
    if (const Var* engine = project_.setting("physics/3d/physics_engine")) {
        const std::string which = stringOf(engine, "");
        if (which == "Jolt Physics")
            report_.add("setting", kProject, "", "", "physics/3d/physics_engine", Rating::Converted, "Jolt Physics: Aver's physics is Jolt too", 0);
        else
            report_.add("setting", kProject, "", "", "physics/3d/physics_engine", Rating::Approximated,
                        "the project asks for " + (which.empty() ? std::string("Godot's default engine") : which) + "; Aver always simulates with Jolt, so contacts and stacking behave as Jolt's do", 0);
    }

    // The start scene's environment: bloom and exposure are project-wide in Aver.
    // Godot's exposure is fixed unless CameraAttributes asks for auto exposure, so eye adaptation is off; the tonemapper is Godot's.
    if (projectEnv_.found) {
        namespace ge = fmt::godotenv;
        d.postBloom = projectEnv_.glow ? static_cast<f32>(projectEnv_.glowIntensity) : 0.0f;
        const double exposure = ge::exposure(projectEnv_.tonemapExposure, projectEnv_.cameraExposure);
        if (exposure != 1.0) d.postExposure = static_cast<f32>(exposure);
        d.postAutoExposure = projectEnv_.autoExposure ? 1 : 0;
        d.postTonemap = ge::tonemapMode(projectEnv_.tonemapMode);
        d.postTonemapWhite = static_cast<f32>(projectEnv_.tonemapWhite);
        if (projectEnv_.adjust) {
            d.postAdjustBrightness = static_cast<f32>(projectEnv_.adjBrightness);
            d.postAdjustContrast = static_cast<f32>(projectEnv_.adjContrast);
            d.postAdjustSaturation = static_cast<f32>(projectEnv_.adjSaturation);
        }
        if (projectEnv_.volumetricFog) {
            d.volFog = 1;   // G4 R8: the light-shaft fog, where FogVolumes draw. The uniform medium stays 0: the level's fog already carries the density
            d.volFogDensity = 0.0f;
            // F24: the light-shaft fog's reach, anisotropy and history weight, when the environment states them.
            if (projectEnv_.volFogLengthCm >= 0.0) d.volFogLength = static_cast<f32>(projectEnv_.volFogLengthCm);
            if (projectEnv_.hasVolFogAniso) { d.hasVolFogAniso = true; d.volFogAniso = static_cast<f32>(projectEnv_.volFogAniso); }
            if (projectEnv_.volFogTemporal >= 0.0) d.volFogTemporal = static_cast<f32>(projectEnv_.volFogTemporal);
            report_.add("setting", kProject, "", "Environment", "volumetric_fog_enabled", Rating::Approximated,
                        "RENDER.VOLFOG 1 and RENDER.VOLFOGDENSITY 0: sun shafts through the level's fog, and FogVolume nodes add their own density", 0);
        }
        report_.add("setting", kProject, "", "Environment", "tonemap", Rating::Converted,
                    "RENDER.TONEMAP " + std::to_string(d.postTonemap) + " / TONEMAPWHITE / EXPOSURE / AUTOEXPOSURE / ADJUST* from the start scene's WorldEnvironment", 0);
    }

    // Anti-aliasing: Godot's screen-space pass (FXAA, SMAA) has no twin; NeuRAA is the engine's edge anti-aliasing and the closest. Its TAA switch is the engine's TAA.
    for (const char* key : {"rendering/anti_aliasing/quality/screen_space_aa", "rendering/textures/quality/screen_space_aa"}) {
        const Var* v = project_.setting(key);
        if (!v) continue;
        const int aaMode = static_cast<int>(numOf(v, 0));
        d.neuraa = aaMode > 0 ? 1 : 0;
        if (aaMode > 0)
            report_.add("setting", kProject, "", "", key, Rating::Approximated, "RENDER.NEURAA 1: the engine's edge anti-aliasing stands in for Godot's FXAA / SMAA pass; it is a different filter", 0);
        else
            report_.add("setting", kProject, "", "", key, Rating::Converted, "RENDER.NEURAA 0", 0);
    }
    // F1: the consistent anti-aliasing set. Aver's TAA needs MSAA off, so a project that asks for TAA gets RENDER.MSAA 1 whatever msaa_3d says (godotrender::chooseAa).
    {
        namespace gr = fmt::godotrender;
        const Var* msaaV = project_.setting("rendering/anti_aliasing/quality/msaa_3d");
        const Var* taaV = project_.setting("rendering/anti_aliasing/quality/use_taa");
        const gr::AaChoice aa = gr::chooseAa(msaaV ? static_cast<int>(numOf(msaaV, 0)) : -1, taaV ? (settingBool("rendering/anti_aliasing/quality/use_taa", false) ? 1 : 0) : -1);
        if (aa.msaa >= 0) d.msaa = aa.msaa;
        if (aa.taa >= 0) d.taa = aa.taa;
        if (msaaV)
            report_.add("setting", kProject, "", "", "rendering/anti_aliasing/quality/msaa_3d", aa.msaaDropped ? Rating::Approximated : Rating::Converted,
                        aa.msaaDropped ? "RENDER.MSAA 1: use_taa is on, and Aver's TAA needs one sample, so the multisampling is dropped"
                                       : "RENDER.MSAA " + std::to_string(aa.msaa) + " samples; the raster pass only (a denoised ray-traced frame has no coverage samples to resolve)", 0);
        if (taaV)
            report_.add("setting", kProject, "", "", "rendering/anti_aliasing/quality/use_taa", Rating::Approximated,
                        std::string("RENDER.TAA ") + std::to_string(d.taa) + (d.taa ? " with RENDER.MSAA 1 (TAA needs one sample)" : "") + ": the engine's temporal anti-aliasing (AverSR) is not Godot's TAA", 0);
        // E7: the dither is a post key, written into every level's post volume (FabricScene.cpp).
        if (project_.setting("rendering/anti_aliasing/quality/use_debanding"))
            report_.add("setting", kProject, "", "", "rendering/anti_aliasing/quality/use_debanding", Rating::Converted,
                        "the POSTVOLUME key deband in each level's post volume: half an 8-bit step of noise before the quantise", 0);
        // F1: 3D scaling. FSR modes below scale 1 pick the nearest AverSR preset; a bilinear scale is applied at run time by Aver.Fabric's root Viewport.
        const Var* sm = project_.setting("rendering/scaling_3d/mode");
        const Var* ss = project_.setting("rendering/scaling_3d/scale");
        if (sm || ss) {
            const int mode = static_cast<int>(numOf(sm, 0));
            const double scale = gr::scale3d(numOf(ss, 1.0));
            if (mode >= 1 && scale < 0.99) {
                d.averSr = gr::averSrPreset(scale);
                report_.add("setting", kProject, "", "", sm ? "rendering/scaling_3d/mode" : "rendering/scaling_3d/scale", Rating::Approximated,
                            "RENDER.AVERSR " + std::to_string(d.averSr) + " (1 quality, 2 balanced, 3 performance), the nearest of AverSR's presets to FSR at scale " + std::to_string(scale) + "; AverSR is not FSR", 0);
            } else if (scale != 1.0) {
                report_.add("setting", kProject, "", "", ss ? "rendering/scaling_3d/scale" : "rendering/scaling_3d/mode", Rating::Approximated,
                            "no project key for a fractional render scale: Aver.Fabric's window viewport starts from this setting and pushes RenderSettings.SetScale when the game starts", 0);
            } else {
                report_.add("setting", kProject, "", "", sm ? "rendering/scaling_3d/mode" : "rendering/scaling_3d/scale", Rating::Converted, "scale 1: nothing to upscale", 0);
            }
        }
        if (project_.setting("rendering/scaling_3d/fsr_sharpness"))
            report_.add("setting", kProject, "", "", "rendering/scaling_3d/fsr_sharpness", Rating::Approximated,
                        "kept; the viewport pushes 2^-x as the upscaler's sharpness at run time, which the engine stores (the upscaler is chosen when the project starts)", 0);
        // F32: the automatic LOD choice and occlusion culling.
        if (const Var* v = project_.setting("rendering/mesh_lod/lod_change/threshold_pixels")) {
            d.lodThresholdPx = static_cast<f32>(gr::lodThresholdPixels(numOf(v, 1.0)));
            if (d.lodThresholdPx <= 0.0f) d.lodSelect = 0;
            report_.add("setting", kProject, "", "", "rendering/mesh_lod/lod_change/threshold_pixels", Rating::Converted,
                        "RENDER.LODTHRESHOLD " + std::to_string(d.lodThresholdPx) + " pixels (the same screen-space error)" + (d.lodThresholdPx <= 0.0f ? " and RENDER.LODSELECT 0, as 0 turns Godot's automatic choice off" : "") +
                        "; the editor's Play selects levels with it, the shipped runtime selects none", 0);
        }
        if (project_.setting("rendering/occlusion_culling/use_occlusion_culling")) {
            d.occlusionCull = settingBool("rendering/occlusion_culling/use_occlusion_culling", false) ? 1 : 0;
            report_.add("setting", kProject, "", "", "rendering/occlusion_culling/use_occlusion_culling", Rating::Converted,
                        "RENDER.OCCLUSIONCULL " + std::to_string(d.occlusionCull) + "; the editor's Play culls with it, the shipped runtime has no occlusion culling", 0);
        }
        // P3, P4, F21: the sun's cascades from the start level's sun, the atlas size and filter quality, and the light units.
        if (sunShadow_.found && sunShadow_.authored) {
            const gr::SunShadowFit fit = gr::fitSunShadow(sunShadow_.mode, sunShadow_.split1, sunShadow_.split2, sunShadow_.split3, sunShadow_.maxDistanceM, sunShadow_.cameraNearM);
            d.shadowCascades = fit.cascades;
            if (fit.distanceCm > 0.0) d.shadowDist = static_cast<f32>(fit.distanceCm);
            if (fit.lambda >= 0.0) d.shadowSplit = static_cast<f32>(fit.lambda);
            report_.add("setting", kProject, "", "DirectionalLight3D", "directional_shadow_*", Rating::Approximated,
                        "RENDER.SHADOWCASCADES " + std::to_string(fit.cascades) + (fit.distanceCm > 0.0 ? ", RENDER.SHADOWDIST " + std::to_string(static_cast<int>(fit.distanceCm)) + " cm" : "") +
                        (fit.lambda >= 0.0 ? ", RENDER.SHADOWSPLIT " + std::to_string(fit.lambda) + " (least-squares fit of Godot's split fractions)" : "") +
                        "; the raster cascades only: ray-traced shadows (the default from Medium) do not use them", 0);
        }
        if (const Var* v = project_.setting("rendering/lights_and_shadows/directional_shadow/size")) {
            const int cascades = d.shadowCascades > 0 ? d.shadowCascades : 4;
            d.shadowRes = gr::shadowResolution(static_cast<int>(numOf(v, 4096)), cascades);
            report_.add("setting", kProject, "", "", "rendering/lights_and_shadows/directional_shadow/size", Rating::Approximated,
                        "RENDER.SHADOWRES " + std::to_string(d.shadowRes) + ": one cascade's edge is half Godot's atlas (a single cascade, all of it), at most 4096; read when the renderer starts", 0);
        }
        if (const Var* v = project_.setting("rendering/lights_and_shadows/directional_shadow/soft_shadow_filter_quality")) {
            d.shadowFilter = static_cast<f32>(gr::shadowFilterWidth(static_cast<int>(numOf(v, 3))));
            report_.add("setting", kProject, "", "", "rendering/lights_and_shadows/directional_shadow/soft_shadow_filter_quality", Rating::Approximated,
                        "RENDER.SHADOWFILTER " + std::to_string(d.shadowFilter) + " texels (Godot's medium, its default, is Aver's fixed 3x3 filter); a judgement, the sample counts differ", 0);
        }
        if (project_.setting("rendering/lights_and_shadows/use_physical_light_units"))
            report_.add("setting", kProject, "", "", "rendering/lights_and_shadows/use_physical_light_units", Rating::Approximated,
                        settingBool("rendering/lights_and_shadows/use_physical_light_units", false)
                            ? "lights carry Godot's physical units: lumens or lux, the light type's factor and the camera attributes' exposure normalization, folded into the light's energy and tinted by its temperature; the sky, emission and fog are not in physical units"
                            : "off: lights keep Godot's plain energy (x pi)", 0);
    }
    // P1: the project's texture sampling defaults. Godot's anisotropic level is 0 (off) to 4 (16x); the engine's samplers read RENDER.TEXTUREANISO (1 to 16) when the renderer
    // starts. A project that states neither keeps the engine's own (8x, no bias).
    if (const Var* v = project_.setting("rendering/textures/default_filters/anisotropic_filtering_level")) {
        static const int kLevels[] = {1, 2, 4, 8, 16};
        const int level = std::clamp(static_cast<int>(numOf(v, 2)), 0, 4);
        d.textureAniso = kLevels[level];
        report_.add("setting", kProject, "", "", "rendering/textures/default_filters/anisotropic_filtering_level", Rating::Converted,
                    "RENDER.TEXTUREANISO " + std::to_string(d.textureAniso) + " (read when the renderer starts)", 0);
    }
    if (const Var* v = project_.setting("rendering/textures/default_filters/texture_mipmap_bias")) {
        d.hasTextureMipBias = true;
        d.textureMipBias = static_cast<float>(std::clamp(numOf(v, 0.0), -2.0, 2.0));
        report_.add("setting", kProject, "", "", "rendering/textures/default_filters/texture_mipmap_bias", Rating::Converted, "RENDER.TEXTUREMIPBIAS (read when the renderer starts)", 0);
    }
    if (project_.setting("rendering/textures/default_filters/use_nearest_mipmap_filter"))
        report_.add("setting", kProject, "", "", "rendering/textures/default_filters/use_nearest_mipmap_filter", Rating::Approximated,
                    "a material that names nearest filtering with mipmaps gets it through its own sampler; this default for the 2D canvas is not used", 0);
    // The clear colour is read where the start scene's Environment is written (a Clear Color background becomes a flat sky of this colour).
    if (project_.setting("rendering/environment/defaults/default_clear_color"))
        report_.add("setting", kProject, "", "", "rendering/environment/defaults/default_clear_color", Rating::Approximated,
                    "an Environment whose background is Clear Color is written as a flat sky of this colour; a level with no Environment keeps the engine's own background", 0);

    // Comments the writer keeps verbatim: the layer names, which have no key of their own.
    std::string comments = "# Converted from a Godot " + (project_.engineVersion.empty() ? std::string("4") : project_.engineVersion) + " project by AverAssetC fabric.\n";
    comments += "# The conversion report is FabricReport.md next to this folder; the contract is docs/plans/fabric/G3_SCENES.md.\n";
    for (const auto& [key, val] : project_.layerNames) {
        // "3d_physics/layer_3" -> "FABRIC.LAYER 3d_physics 3 <name>"
        const size_t slash = key.find('/');
        const size_t us = key.rfind('_');
        if (slash == std::string::npos || us == std::string::npos || us < slash) continue;
        comments += "# FABRIC.LAYER " + key.substr(0, slash) + " " + key.substr(us + 1) + " " + val + "\n";
        report_.add("setting", kProject, "", "", "layer_names/" + key, Rating::Approximated,
                    "kept as a FABRIC.LAYER comment in the .ocproject and in Fabric/project.json; the engine has no layer names", 0);
    }
    const std::string text = fmt::writeOcproject(d, comments);
    createDirectories(projectDir_);
    const std::string path = projectDir_ + "/" + name_ + ".ocproject";
    if (!writeFileText(path, text)) {
        report_.add("project", kProject, "", "", "", Rating::Unsupported, "could not write " + path, 0);
        return false;
    }
    fmt::ProjectDesc back;
    std::string err;
    if (!fmt::loadOcproject(path, back, &err)) {
        report_.add("project", kProject, "", "", "", Rating::Unsupported, "the written .ocproject did not read back: " + err, 0);
        return false;
    }
    report_.add("project", kProject, "", "", "", Rating::Converted, "written as " + name_ + ".ocproject (start level " + (startLevelAsset.empty() ? "none" : startLevelAsset) + ", GAME.MODE " + d.gameMode + ")", 0);

    // Settings nobody converted, each named.
    static const char* handled[] = {"application/config/name", "application/config/description", "application/config/features",
                                    "application/config/icon", "application/run/main_scene", "application/run/max_fps", "display/window/size/", "display/window/stretch/", "display/window/vsync/",
                                    "gui/theme/custom", "gui/timers/tooltip_delay_sec", "physics/3d/default_gravity",
                                    "physics/3d/default_gravity_vector", "physics/3d/default_linear_damp", "physics/3d/default_angular_damp",
                                    "physics/common/physics_ticks_per_second", "physics/common/physics_interpolation", "physics/common/max_physics_steps_per_frame",
                                    "internationalization/locale/",
                                    "input/", "autoload/", "layer_names/", "editor_plugins/", "dotnet/project/", "config_version",
                                    "physics/3d/physics_engine", "physics/jolt_physics_3d/limits/max_bodies", "physics/jolt_physics_3d/limits/max_body_pairs",
                                    "physics/jolt_physics_3d/limits/max_contact_constraints", "physics/jolt_physics_3d/limits/temporary_memory_buffer_size",
                                    "rendering/anti_aliasing/quality/screen_space_aa", "rendering/textures/quality/screen_space_aa", "rendering/anti_aliasing/quality/use_taa",
                                    "rendering/anti_aliasing/quality/msaa_3d", "rendering/anti_aliasing/quality/use_debanding", "rendering/scaling_3d/", "rendering/mesh_lod/lod_change/threshold_pixels",
                                    "rendering/occlusion_culling/use_occlusion_culling", "rendering/lights_and_shadows/directional_shadow/size",
                                    "rendering/lights_and_shadows/directional_shadow/soft_shadow_filter_quality", "rendering/lights_and_shadows/use_physical_light_units",
                                    "rendering/environment/defaults/default_clear_color", "rendering/textures/default_filters/anisotropic_filtering_level",
                                    "rendering/textures/default_filters/texture_mipmap_bias", "rendering/textures/default_filters/use_nearest_mipmap_filter"};
    for (const gd::GodotConfigSection& sec : project_.config.sections) {
        for (const gd::GodotProperty& e : sec.entries) {
            const std::string key = sec.name.empty() ? e.name : sec.name + "/" + e.name;
            bool ok = false;
            for (const char* h : handled)
                if (key.rfind(h, 0) == 0) { ok = true; break; }
            if (ok) continue;
            if (key.rfind("shader_globals/", 0) == 0) {   // G6 SH2: a global shader uniform's default rides in each shader material's uniform table
                report_.add("setting", kProject, "", "", key, Rating::Approximated,
                            "the default is written into every shader material that declares this global uniform; Fabric/shaders.json lists the globals "
                            "and RenderingServer.global_shader_parameter_set updates them at run time", e.line);
                continue;
            }
            // The default bus layout was written (FabricAudio.cpp says so, under its resolved path): its setting row is that one.
            if (key == "audio/buses/default_bus_layout" && !audioBusLayout_.empty()) continue;
            // Editor and import-time settings change nothing a running game does.
            static const char* editorOnly[] = {"application/config/tags", "debug/gdscript/warnings/", "debug/shader_language/warnings/",
                                               "rendering/textures/vram_compression/", "rendering/vram_compression/", "filesystem/import/"};
            bool editorSetting = false;
            for (const char* h : editorOnly)
                if (key.rfind(h, 0) == 0) { editorSetting = true; break; }
            if (editorSetting) {
                report_.add("setting", kProject, "", "", key, Rating::Approximated,
                            "an editor or import-time setting (project tags, script warnings, texture compression formats, the Blender importer); it changes nothing a running game does, "
                            "so no Aver key is written; the value is kept for ProjectSettings (Fabric/project.json)", e.line);
                continue;
            }
            // Settings of the Godot runtime that have no counterpart to set: each says why nothing is lost.
            static const struct { const char* prefix; const char* why; } inert[] = {
                {"memory/multithread/thread_rid_pool_prealloc", "sizes Godot's pool of resource ids for threads; Aver's tables grow on demand, so it changes nothing"},
                {"global_group/", "an entry of the editor's list of group names; a node's groups come from its scene, which is converted, so nothing reads this list"},
                {"application/run/low_processor_mode", "makes Godot sleep between frames to save power; Aver draws every frame, so it changes nothing a player sees"},
                {"debug/settings/physics_interpolation/enable_warnings", "switches Godot's debug warnings for physics interpolation; it changes nothing a running game does"},
                {"gui/theme/default_font_generate_mipmaps", "a rasteriser option of Godot's default font; the engine draws coverage glyphs at the drawn size, so it changes nothing"},
                {"gui/theme/default_font_multichannel_signed_distance_field", "turns Godot's default font into an MSDF font; the engine rasterises coverage glyphs at the drawn size (no MSDF), so it changes nothing"}};
            bool inertSetting = false;
            for (const auto& s : inert)
                if (key.rfind(s.prefix, 0) == 0) {
                    report_.add("setting", kProject, "", "", key, Rating::Approximated, std::string(s.why) + "; the value is kept for ProjectSettings (Fabric/project.json)", e.line);
                    inertSetting = true;
                    break;
                }
            if (inertSetting) continue;
            // The engine has no key for it, but nothing is dropped: Fabric/project.json holds the value and ProjectSettings.get_setting returns it.
            report_.add("setting", kProject, "", "", key, Rating::Approximated,
                        "no Aver project key; kept for ProjectSettings (Fabric/project.json), so get_setting returns Godot's value and type, and nothing else reads it", e.line);
        }
    }
    return true;
}

}  // namespace aver::fabric
