// 2D scenes (docs/plans/RENDER_2D.md in the engine): Node2D, Sprite2D and CanvasLayer become the engine's COMPONENT C2DTransform / CSprite2D / C2DLayer
// records on a placement with no mesh; AnimatedSprite2D adds CSpriteAnim2D, Camera2D becomes CCamera2D and Parallax2D (and, as an approximation, ParallaxLayer)
// CParallax2D; a ParallaxBackground is a C2DLayer that follows the camera. The managed nodes (Nodes2D.cs, CanvasLayer.cs, Animated2D.cs) drive the same
// components at run time.
#include "FabricInternal.hpp"

#include <algorithm>
#include <cmath>
#include <cstdio>

namespace aver::fabric {

bool is2DSceneClass(const std::string& t) {
    return t == "Node2D" || t == "Sprite2D" || t == "CanvasLayer" || t == "AnimatedSprite2D" || t == "Camera2D" || t == "Parallax2D" || t == "ParallaxBackground" || t == "ParallaxLayer";
}

namespace {

std::string numText(double v) {
    char b[40];
    std::snprintf(b, sizeof b, "%.9g", v);
    return b;
}

const Var* find(const std::vector<std::pair<std::string, Var>>& props, const char* key) {
    for (const auto& kv : props)
        if (kv.first == key) return &kv.second;
    return nullptr;
}

bool has(const std::vector<std::pair<std::string, Var>>& props, const char* key) { return find(props, key) != nullptr; }

// A Vector2 property, or `def` when it is missing or not a Vector2.
void vec2(const std::vector<std::pair<std::string, Var>>& props, const char* key, double def0, double def1, double& x, double& y) {
    x = def0;
    y = def1;
    if (const Var* v = find(props, key); v && v->type == VType::Vector2 && v->nums.size() >= 2) { x = v->nums[0]; y = v->nums[1]; }
}

void put(fmt::OcPlacedComponent& rec, const char* key, double v) { rec.kv.emplace_back(key, numText(v)); }

// The engine reads RRGGBBAA (red first). Godot colours are used as they are by the canvas, so the bytes are the components times 255.
std::string colourText(double r, double g, double b, double a) {
    const auto byte = [](double c) { return static_cast<unsigned>(std::lround(std::max(0.0, std::min(1.0, c)) * 255.0)); };
    char t[12];
    std::snprintf(t, sizeof t, "%02x%02x%02x%02x", byte(r), byte(g), byte(b), byte(a));
    return t;
}

bool colourOf(const std::vector<std::pair<std::string, Var>>& props, const char* key, double c[4]) {
    c[0] = c[1] = c[2] = c[3] = 1.0;
    const Var* v = find(props, key);
    if (!v || v->type != VType::Color || v->nums.size() < 4) return false;
    for (int i = 0; i < 4; ++i) c[i] = v->nums[i];
    return true;
}

constexpr int kMaxClips = 16, kMaxFrames = 128;   // the engine's CSpriteAnim2D capacity (scene2d::kMaxAnimClips / kMaxAnimFrames)
constexpr double kLimitDefault = 10000000.0;       // Godot's Camera2D limits

// The CSpriteAnim2D record for an AnimatedSprite2D's SpriteFrames (the converted resource dictionary), and the notes about what did not fit.
void emitAnimation(const std::vector<std::pair<std::string, Var>>& props, fmt::OcWorldPlacement& p, std::vector<Note2D>& notes) {
    const auto note = [&](const char* key, Rating r, const std::string& why) { notes.push_back({key, r, why}); };
    fmt::OcPlacedComponent rec;
    rec.type = "CSpriteAnim2D";
    const double speedScale = numOf(find(props, "speed_scale"), 1.0);
    if (speedScale != 1.0) put(rec, "speed", speedScale);

    const Var* sf = find(props, "sprite_frames");
    const Var* anims = sf && sf->type == VType::Dictionary ? sf->find("animations") : nullptr;
    if (!anims || anims->type != VType::Array) {
        if (sf && sf->type != VType::Nil) note("sprite_frames", Rating::Unsupported, "the SpriteFrames could not be read, so the sprite has no animations");
        else note("sprite_frames", Rating::Approximated, "an AnimatedSprite2D with no SpriteFrames draws nothing");
        p.components.push_back(std::move(rec));
        return;
    }
    std::vector<std::string> names;
    int clips = 0, frames = 0, droppedClips = 0, droppedFrames = 0, noTexture = 0;
    for (const Var& anim : anims->items) {
        const Var* nameV = anim.find("name");
        const Var* fr = anim.find("frames");
        const std::string name = nameV ? nameV->asString() : std::string();
        if (name.empty() || name.size() >= 24 || std::find(names.begin(), names.end(), name) != names.end()) {
            ++droppedClips;
            continue;
        }
        if (clips >= kMaxClips) { ++droppedClips; continue; }
        const std::string ck = "clip" + std::to_string(clips);
        rec.kv.emplace_back(ck, name);
        names.push_back(name);
        if (const double fps = numOf(anim.find("speed"), 5.0); fps != 5.0) put(rec, (ck + "fps").c_str(), fps);
        if (!boolOf(anim.find("loop"), true)) rec.kv.emplace_back(ck + "loop", "0");
        int n = 0;
        if (fr && fr->type == VType::Array)
            for (const Var& f : fr->items) {
                if (frames >= kMaxFrames) { ++droppedFrames; continue; }
                const Var* tex = f.find("texture");
                std::string value;
                if (tex && tex->type == VType::Resource && !tex->s.empty()) value = tex->s;
                else ++noTexture;
                if (const double d = numOf(f.find("duration"), 1.0); d != 1.0) value += "||" + numText(d);
                rec.kv.emplace_back(ck + "f" + std::to_string(n), value);
                ++n;
                ++frames;
            }
        ++clips;
    }
    if (droppedClips > 0) note("sprite_frames", Rating::Approximated, std::to_string(droppedClips) + " animation(s) left out: the engine holds 16 animations per sprite, and a name must be unique and under 24 bytes");
    if (droppedFrames > 0) note("sprite_frames", Rating::Approximated, std::to_string(droppedFrames) + " frame(s) left out: the engine holds 128 frames per sprite across its animations");
    if (noTexture > 0) note("sprite_frames", Rating::Approximated, std::to_string(noTexture) + " frame(s) have no texture file (a generated or atlas texture), so they draw nothing");

    const auto clipNamed = [&](const std::string& n) { return std::find(names.begin(), names.end(), n) != names.end(); };
    if (const Var* a = find(props, "animation"); a && !a->asString().empty()) {
        const std::string cur = a->asString();
        if (!clipNamed(cur)) note("animation", Rating::Approximated, "'" + cur + "' is not one of the sprite's animations; the first one is shown");
        else if (cur != names.front()) rec.kv.emplace_back("animation", cur);
    }
    if (const Var* a = find(props, "autoplay"); a && !a->asString().empty()) {
        if (clipNamed(a->asString())) rec.kv.emplace_back("autoplay", a->asString());
        else note("autoplay", Rating::Approximated, "'" + a->asString() + "' is not one of the sprite's animations; nothing plays at start");
    }
    if (const int fr = static_cast<int>(numOf(find(props, "frame"), 0)); fr != 0) rec.kv.emplace_back("frame", std::to_string(fr));
    if (boolOf(find(props, "playing"), false)) rec.kv.emplace_back("playing", "1");
    if (has(props, "frame_progress")) note("frame_progress", Rating::Approximated, "a saved position inside a frame is not kept; the animation starts at the frame's beginning");
    p.components.push_back(std::move(rec));
}

}  // namespace

std::vector<Note2D> emit2DRecords(const std::string& type, const std::vector<std::pair<std::string, Var>>& props, int treeIndex, fmt::OcWorldPlacement& p) {
    std::vector<Note2D> notes;
    const auto note = [&](const char* key, Rating r, const char* why) { notes.push_back({key, r, why}); };

    if (type == "CanvasLayer" || type == "ParallaxBackground") {
        const bool background = type == "ParallaxBackground";
        fmt::OcPlacedComponent rec;
        rec.type = "C2DLayer";
        rec.kv.emplace_back("index", numText(numOf(find(props, "layer"), background ? -100 : 1)));   // Godot's CanvasLayer defaults to layer 1, a ParallaxBackground to -100
        if (!boolOf(find(props, "visible"), true)) rec.kv.emplace_back("hidden", "1");
        double ox, oy, sx, sy;
        vec2(props, "offset", 0, 0, ox, oy);
        vec2(props, "scale", 1, 1, sx, sy);
        if (ox != 0) put(rec, "x", ox);
        if (oy != 0) put(rec, "y", oy);
        if (const double rot = numOf(find(props, "rotation"), 0); rot != 0) put(rec, "rotation", rot);
        if (sx != 1) put(rec, "sx", sx);
        if (sy != 1) put(rec, "sy", sy);
        if (background) {
            rec.kv.emplace_back("followviewport", "1");   // a parallax background scrolls with the camera; its layers' scroll scales do the rest
            for (const char* k : {"scroll_offset", "scroll_base_offset", "scroll_base_scale", "scroll_limit_begin", "scroll_limit_end", "scroll_ignore_camera_zoom"})
                if (has(props, k)) note(k, Rating::Unsupported, "a parallax background's own scrolling is not used; the camera scrolls its layers (their motion scales) instead");
        } else if (boolOf(find(props, "follow_viewport_enabled"), false)) {
            rec.kv.emplace_back("followviewport", "1");
            if (has(props, "follow_viewport_scale") && numOf(find(props, "follow_viewport_scale"), 1.0) != 1.0)
                note("follow_viewport_scale", Rating::Unsupported, "the layer follows the 2D camera one to one; a scale for the following is not supported");
        }
        if (has(props, "transform")) note("transform", Rating::Approximated, "the layer's combined transform is not read; its offset, rotation and scale are");
        if (has(props, "custom_viewport")) note("custom_viewport", Rating::Unsupported, "a layer drawn into another viewport is not supported; it draws in the window");
        p.components.push_back(std::move(rec));
        return notes;
    }

    // Node2D and Sprite2D share the transform.
    fmt::OcPlacedComponent tr;
    tr.type = "C2DTransform";
    double px, py, sx, sy;
    vec2(props, "position", 0, 0, px, py);
    vec2(props, "scale", 1, 1, sx, sy);
    if (px != 0) put(tr, "x", px);
    if (py != 0) put(tr, "y", py);
    if (const double rot = numOf(find(props, "rotation"), 0); rot != 0) put(tr, "rotation", rot);
    if (sx != 1) put(tr, "sx", sx);
    if (sy != 1) put(tr, "sy", sy);
    if (const int z = static_cast<int>(numOf(find(props, "z_index"), 0)); z != 0) put(tr, "z", z);
    if (!boolOf(find(props, "z_as_relative"), true)) tr.kv.emplace_back("zabsolute", "1");
    if (boolOf(find(props, "top_level"), false)) tr.kv.emplace_back("toplevel", "1");
    if (!boolOf(find(props, "visible"), true)) tr.kv.emplace_back("hidden", "1");
    tr.kv.emplace_back("order", std::to_string(treeIndex + 1));   // one number space with the Controls: the node's place in the tree, counted from 1
    p.components.push_back(std::move(tr));

    if (has(props, "skew")) note("skew", Rating::Unsupported, "the 2D transform has no skew; the node is drawn unsheared");
    if (has(props, "light_mask") || has(props, "visibility_layer")) note("light_mask", Rating::Unsupported, "2D lights and canvas cull masks are not supported");
    if (has(props, "clip_children")) note("clip_children", Rating::Unsupported, "a sprite does not clip its children");
    if (has(props, "texture_repeat")) note("texture_repeat", Rating::Unsupported, "a sprite's texture is not repeated");
    if (const Var* m = find(props, "material"); m && m->type != VType::Nil) note("material", Rating::Approximated, "a CanvasItemMaterial's blend mode applies at run time; a shader material does not");

    if (type == "Camera2D") {
        fmt::OcPlacedComponent cam;
        cam.type = "CCamera2D";
        double zx, zy, ox, oy;
        vec2(props, "zoom", 1, 1, zx, zy);
        vec2(props, "offset", 0, 0, ox, oy);
        if (zx != 1) put(cam, "zx", zx);
        if (zy != 1) put(cam, "zy", zy);
        if (ox != 0) put(cam, "ox", ox);
        if (oy != 0) put(cam, "oy", oy);
        const char* const sides[4] = {"left", "top", "right", "bottom"};
        const double defaults[4] = {-kLimitDefault, -kLimitDefault, kLimitDefault, kLimitDefault};
        for (int i = 0; i < 4; ++i) {
            const double v = numOf(find(props, (std::string("limit_") + sides[i]).c_str()), defaults[i]);
            if (v != defaults[i]) put(cam, sides[i], v);
        }
        if (boolOf(find(props, "position_smoothing_enabled"), false)) put(cam, "smooth", numOf(find(props, "position_smoothing_speed"), 5.0));
        if (boolOf(find(props, "drag_horizontal_enabled"), false)) cam.kv.emplace_back("draghorizontal", "1");
        if (boolOf(find(props, "drag_vertical_enabled"), false)) cam.kv.emplace_back("dragvertical", "1");
        const char* const margins[4] = {"dragleft", "dragtop", "dragright", "dragbottom"};
        for (int i = 0; i < 4; ++i) {
            const double v = numOf(find(props, (std::string("drag_") + sides[i] + "_margin").c_str()), 0.2);
            if (std::fabs(v - 0.2) > 1e-9) put(cam, margins[i], v);
        }
        if (static_cast<int>(numOf(find(props, "anchor_mode"), 1)) == 0) cam.kv.emplace_back("anchor", "topleft");
        if (!boolOf(find(props, "enabled"), true)) cam.kv.emplace_back("enabled", "0");
        if (boolOf(find(props, "current"), false)) cam.kv.emplace_back("current", "1");   // Godot 4.0 and 4.1 spelled it so
        if (!boolOf(find(props, "ignore_rotation"), true)) cam.kv.emplace_back("rotation", "1");
        p.components.push_back(std::move(cam));
        if (boolOf(find(props, "rotation_smoothing_enabled"), false)) note("rotation_smoothing_enabled", Rating::Unsupported, "the camera turns with its node at once; rotation smoothing is not supported");
        if (boolOf(find(props, "limit_smoothed"), false)) note("limit_smoothed", Rating::Approximated, "the limits stop the view hard; easing into them is not supported");
        if (numOf(find(props, "drag_horizontal_offset"), 0) != 0 || numOf(find(props, "drag_vertical_offset"), 0) != 0)
            note("drag_horizontal_offset", Rating::Unsupported, "the drag margins start centred; an initial drag offset is not supported");
        if (has(props, "process_callback") && numOf(find(props, "process_callback"), 1) != 1) note("process_callback", Rating::Approximated, "the camera is updated once a frame, not in the physics step");
        if (has(props, "custom_viewport")) note("custom_viewport", Rating::Unsupported, "a camera for another viewport is not supported; it applies to the window");
        return notes;
    }

    if (type == "Parallax2D" || type == "ParallaxLayer") {
        const bool layer = type == "ParallaxLayer";
        fmt::OcPlacedComponent par;
        par.type = "CParallax2D";
        double sx, sy, ox, oy, rx, ry;
        vec2(props, layer ? "motion_scale" : "scroll_scale", 1, 1, sx, sy);
        vec2(props, layer ? "motion_offset" : "scroll_offset", 0, 0, ox, oy);
        vec2(props, layer ? "motion_mirroring" : "repeat_size", 0, 0, rx, ry);
        if (sx != 1) put(par, "sx", sx);
        if (sy != 1) put(par, "sy", sy);
        if (ox != 0) put(par, "ox", ox);
        if (oy != 0) put(par, "oy", oy);
        if (rx != 0) put(par, "rx", rx);
        if (ry != 0) put(par, "ry", ry);
        if (!layer) {
            if (const int times = static_cast<int>(numOf(find(props, "repeat_times"), 1)); times > 1) par.kv.emplace_back("repeat", std::to_string(times));
            double ax, ay;
            vec2(props, "autoscroll", 0, 0, ax, ay);
            if (ax != 0) put(par, "ax", ax);
            if (ay != 0) put(par, "ay", ay);
            double bx, by, ex, ey;
            vec2(props, "limit_begin", 0, 0, bx, by);
            vec2(props, "limit_end", 0, 0, ex, ey);
            if (bx != 0 || by != 0) { put(par, "beginx", bx); put(par, "beginy", by); }
            if (ex != 0 || ey != 0) { put(par, "endx", ex); put(par, "endy", ey); }
            if (!boolOf(find(props, "follow_viewport"), true)) par.kv.emplace_back("follow", "0");
            double sox, soy;
            vec2(props, "screen_offset", 0, 0, sox, soy);
            if (sox != 0 || soy != 0) note("screen_offset", Rating::Unsupported, "the layer is not shifted by a screen offset; scroll_offset moves it in the world");
            if (boolOf(find(props, "ignore_camera_scroll"), false)) note("ignore_camera_scroll", Rating::Unsupported, "the layer always scrolls with the camera");
        } else if (rx != 0 || ry != 0) {
            note("motion_mirroring", Rating::Approximated, "the layer is tiled one copy each side of the original at the mirroring size, as a repeat size");
        }
        p.components.push_back(std::move(par));
        return notes;
    }

    if (type == "AnimatedSprite2D") {
        fmt::OcPlacedComponent sp;
        sp.type = "CSprite2D";
        double ox, oy;
        vec2(props, "offset", 0, 0, ox, oy);
        if (ox != 0) put(sp, "ox", ox);
        if (oy != 0) put(sp, "oy", oy);
        if (!boolOf(find(props, "centered"), true)) sp.kv.emplace_back("centered", "0");
        if (boolOf(find(props, "flip_h"), false)) sp.kv.emplace_back("fliph", "1");
        if (boolOf(find(props, "flip_v"), false)) sp.kv.emplace_back("flipv", "1");
        if (const int f = static_cast<int>(numOf(find(props, "texture_filter"), 0)); f == 1 || f == 3 || f == 5) sp.kv.emplace_back("filter", "nearest");
        double m[4], sm[4];
        const bool hasM = colourOf(props, "modulate", m), hasS = colourOf(props, "self_modulate", sm);
        if (hasM || hasS) {
            double c[4];
            for (int i = 0; i < 4; ++i) c[i] = m[i] * sm[i];
            if (std::lround(std::max(0.0, std::min(1.0, c[3])) * 255.0) == 0) sp.kv.emplace_back("hidden", "1");
            else sp.kv.emplace_back("modulate", colourText(c[0], c[1], c[2], c[3]));
        }
        p.components.push_back(std::move(sp));
        emitAnimation(props, p, notes);
        return notes;
    }

    if (type != "Sprite2D") return notes;

    fmt::OcPlacedComponent sp;
    sp.type = "CSprite2D";
    const Var* tex = find(props, "texture");
    if (tex && tex->type == VType::Resource && !tex->s.empty()) sp.kv.emplace_back("texture", tex->s);
    else if (tex && tex->type != VType::Nil) note("texture", Rating::Unsupported, "only a texture file reaches the engine's canvas; a generated or atlas texture draws nothing");

    if (boolOf(find(props, "region_enabled"), false)) {
        if (const Var* r = find(props, "region_rect"); r && r->type == VType::Rect2 && r->nums.size() >= 4) {
            put(sp, "rx", r->nums[0]); put(sp, "ry", r->nums[1]); put(sp, "rw", r->nums[2]); put(sp, "rh", r->nums[3]);
        }
    }
    const int hf = static_cast<int>(numOf(find(props, "hframes"), 1)), vf = static_cast<int>(numOf(find(props, "vframes"), 1));
    if (hf > 1) put(sp, "hframes", hf);
    if (vf > 1) put(sp, "vframes", vf);
    int frame = static_cast<int>(numOf(find(props, "frame"), 0));
    if (const Var* fc = find(props, "frame_coords"); fc && fc->nums.size() >= 2) frame = static_cast<int>(fc->nums[1]) * std::max(1, hf) + static_cast<int>(fc->nums[0]);
    if (frame != 0) put(sp, "frame", frame);
    double ox, oy;
    vec2(props, "offset", 0, 0, ox, oy);
    if (ox != 0) put(sp, "ox", ox);
    if (oy != 0) put(sp, "oy", oy);
    if (!boolOf(find(props, "centered"), true)) sp.kv.emplace_back("centered", "0");
    if (boolOf(find(props, "flip_h"), false)) sp.kv.emplace_back("fliph", "1");
    if (boolOf(find(props, "flip_v"), false)) sp.kv.emplace_back("flipv", "1");
    if (const int f = static_cast<int>(numOf(find(props, "texture_filter"), 0)); f == 1 || f == 3 || f == 5) sp.kv.emplace_back("filter", "nearest");

    // Own modulate times self_modulate. A modulate of 0 means white in the engine, so a fully transparent one is written as hidden, which is what it draws.
    double m[4], s[4];
    const bool hasM = colourOf(props, "modulate", m), hasS = colourOf(props, "self_modulate", s);
    if (hasM || hasS) {
        double c[4];
        for (int i = 0; i < 4; ++i) c[i] = m[i] * s[i];
        if (std::lround(std::max(0.0, std::min(1.0, c[3])) * 255.0) == 0) sp.kv.emplace_back("hidden", "1");
        else sp.kv.emplace_back("modulate", colourText(c[0], c[1], c[2], c[3]));
        if (c[3] > 1.0 || c[0] > 1.0 || c[1] > 1.0 || c[2] > 1.0) note("modulate", Rating::Approximated, "a component above 1 (a brightening modulate) is clamped to 1");
    }
    p.components.push_back(std::move(sp));
    return notes;
}

}  // namespace aver::fabric
