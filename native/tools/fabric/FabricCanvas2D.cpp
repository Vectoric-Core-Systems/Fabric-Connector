// 2D scenes (docs/plans/RENDER_2D.md in the engine): Node2D, Sprite2D and CanvasLayer become the engine's COMPONENT C2DTransform / CSprite2D / C2DLayer
// records on a placement with no mesh. The managed nodes (Nodes2D.cs, CanvasLayer.cs) drive the same components at run time.
#include "FabricInternal.hpp"

#include <algorithm>
#include <cmath>
#include <cstdio>

namespace aver::fabric {

bool is2DSceneClass(const std::string& t) { return t == "Node2D" || t == "Sprite2D" || t == "CanvasLayer"; }

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

}  // namespace

std::vector<Note2D> emit2DRecords(const std::string& type, const std::vector<std::pair<std::string, Var>>& props, int treeIndex, fmt::OcWorldPlacement& p) {
    std::vector<Note2D> notes;
    const auto note = [&](const char* key, Rating r, const char* why) { notes.push_back({key, r, why}); };

    if (type == "CanvasLayer") {
        fmt::OcPlacedComponent rec;
        rec.type = "C2DLayer";
        rec.kv.emplace_back("index", numText(numOf(find(props, "layer"), 1)));   // Godot's CanvasLayer defaults to layer 1
        if (!boolOf(find(props, "visible"), true)) rec.kv.emplace_back("hidden", "1");
        double ox, oy, sx, sy;
        vec2(props, "offset", 0, 0, ox, oy);
        vec2(props, "scale", 1, 1, sx, sy);
        if (ox != 0) put(rec, "x", ox);
        if (oy != 0) put(rec, "y", oy);
        if (const double rot = numOf(find(props, "rotation"), 0); rot != 0) put(rec, "rotation", rot);
        if (sx != 1) put(rec, "sx", sx);
        if (sy != 1) put(rec, "sy", sy);
        if (boolOf(find(props, "follow_viewport_enabled"), false)) {
            rec.kv.emplace_back("followviewport", "1");
            note("follow_viewport_enabled", Rating::Approximated, "kept on the layer for the 2D camera that comes later; the layer does not follow a camera yet");
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
