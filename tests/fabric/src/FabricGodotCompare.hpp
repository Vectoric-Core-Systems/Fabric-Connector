#pragma once
// The converter check against Godot itself (tools/godot-dump/README.md): what a converted .ocworld says its nodes are, mapped back to Godot space through the inverse of
// the axis map (aver/formats/AxisMap.hpp: (x, y, z) -> (-z, x, y) * 100, rotation (x, y, z, w) -> (z, -x, -y, w), scale (x, y, z) -> (z, x, y)), compared with the
// JSON tools/godot-dump/dump_scene.gd wrote from the same scene loaded in Godot. Header-only; FabricGodotCompareTest is its test and its command line.
#include "FabricMath.hpp"

#include "aver/core/Types.hpp"
#include "aver/formats/Json.hpp"
#include "aver/formats/OcMesh.hpp"
#include "aver/formats/OcWorld.hpp"
#include "aver/formats/godot/GodotVariant.hpp"
#include "aver/platform/FileSystem.hpp"
#include "aver/world/LevelTransform.hpp"

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <map>
#include <set>
#include <string>
#include <vector>

namespace aver::fabric::cmp {

using fabric::M3;
using fabric::Q4;
using fabric::V3;
using fabric::Xform;
namespace gd = aver::fmt::godot;

inline constexpr double kPosTol = 1e-3;     // metres
inline constexpr double kRotTol = 1e-3;     // radians
inline constexpr double kScaleTol = 1e-3;
inline constexpr double kBoxTol = 2e-3;     // metres: a world bounding box, which a centimetre-rounded position moves by up to a millimetre

struct Box {
    bool valid = false;
    V3 lo, hi;
    void add(const V3& p) {
        if (!valid) { lo = hi = p; valid = true; return; }
        lo = {std::min(lo.x, p.x), std::min(lo.y, p.y), std::min(lo.z, p.z)};
        hi = {std::max(hi.x, p.x), std::max(hi.y, p.y), std::max(hi.z, p.z)};
    }
};

// A box moved by a transform: the box around its eight corners.
inline Box transformed(const Xform& x, const Box& b) {
    Box out;
    if (!b.valid) return out;
    for (int i = 0; i < 8; ++i) out.add(x.pos + x.basis * V3{(i & 1) ? b.hi.x : b.lo.x, (i & 2) ? b.hi.y : b.lo.y, (i & 4) ? b.hi.z : b.lo.z});
    return out;
}

inline double boxDiff(const Box& a, const Box& b) {
    return std::max({std::fabs(a.lo.x - b.lo.x), std::fabs(a.lo.y - b.lo.y), std::fabs(a.lo.z - b.lo.z), std::fabs(a.hi.x - b.hi.x), std::fabs(a.hi.y - b.hi.y), std::fabs(a.hi.z - b.hi.z)});
}

inline Xform compose(const Xform& parent, const Xform& child) { return parent.composed(child); }

// ---- a transform's difference, in the three tolerances ----------------------------------------------------------------------

struct XformDiff {
    double pos = 0, rot = 0, scale = 0;
    bool mirror = false;   // one is a mirror and the other is not
    bool within() const { return !mirror && pos <= kPosTol && rot <= kRotTol && scale <= kScaleTol; }
};

inline XformDiff diff(const Xform& a, const Xform& b) {
    XformDiff d;
    d.pos = (a.pos - b.pos).length();
    const V3 sa = a.scale(), sb = b.scale();
    d.mirror = (a.basis.determinant() < 0) != (b.basis.determinant() < 0);
    d.scale = std::max({std::fabs(sa.x - sb.x), std::fabs(sa.y - sb.y), std::fabs(sa.z - sb.z)});
    const M3 ra = a.rotation(), rb = b.rotation();
    const M3 rel = ra.transposed() * rb;
    const double c = std::min(1.0, std::max(-1.0, (rel.m[0][0] + rel.m[1][1] + rel.m[2][2] - 1.0) * 0.5));
    d.rot = std::acos(c);
    return d;
}

inline std::string fmtXf(const Xform& x) {
    char buf[200];
    const V3 s = x.scale();
    std::snprintf(buf, sizeof buf, "pos (%.4f %.4f %.4f) scale (%.4f %.4f %.4f) x-axis (%.4f %.4f %.4f)", x.pos.x, x.pos.y, x.pos.z, s.x, s.y, s.z, x.basis.m[0][0], x.basis.m[1][0], x.basis.m[2][0]);
    return buf;
}

// ---- Godot's side ----------------------------------------------------------------------------------------------------------------

struct Mat {
    bool present = false;
    std::string cls;
    bool hasAlbedo = false;
    double albedo[4] = {1, 1, 1, 1};
};

struct GNode {
    std::string path, name, cls, scriptPath, instance;
    bool is3d = false, visible = true, visibleInTree = true;
    Xform local, world;
    const fmt::JsonValue* json = nullptr;   // the entry in the dump, for the class extras
    bool hasMesh = false;
    Box worldBox;
    Mat material;
    bool skinnedOrShader = false;
};

inline Xform xfOf(const fmt::JsonValue& a) {
    Xform x;
    if (!a.isArray() || a.size() < 12) return x;
    for (int c = 0; c < 3; ++c) x.basis.setColumn(c, V3{a[static_cast<usize>(c * 3)].asDouble(), a[static_cast<usize>(c * 3 + 1)].asDouble(), a[static_cast<usize>(c * 3 + 2)].asDouble()});
    x.pos = {a[9].asDouble(), a[10].asDouble(), a[11].asDouble()};
    return x;
}

inline Box boxOf(const fmt::JsonValue& a) {
    Box b;
    if (!a.isArray() || a.size() < 6) return b;
    b.add({a[0].asDouble(), a[1].asDouble(), a[2].asDouble()});
    b.add({a[0].asDouble() + a[3].asDouble(), a[1].asDouble() + a[4].asDouble(), a[2].asDouble() + a[5].asDouble()});
    return b;
}

inline double srgbToLinear(double c) { return c <= 0.04045 ? c / 12.92 : std::pow((c + 0.055) / 1.055, 2.4); }

inline Mat matOf(const fmt::JsonValue& m) {
    Mat out;
    if (!m.isObject() || m.size() == 0) return out;
    out.present = true;
    out.cls = std::string(m["class"].asString());
    const fmt::JsonValue& c = m["albedo_color"];
    if (c.isArray() && c.size() >= 4) {
        out.hasAlbedo = true;
        for (usize i = 0; i < 4; ++i) out.albedo[i] = c[i].asDouble();
    }
    return out;
}

// The material Godot draws surface 0 of a mesh node with: the node's override, its surface override, the mesh's own surface material, the primitive's.
inline Mat effectiveMaterial(const fmt::JsonValue& n) {
    Mat m = matOf(n["material_override"]);
    if (m.present) return m;
    const fmt::JsonValue& ov = n["surface_overrides"];
    if (ov.isArray() && ov.size() > 0) { m = matOf(ov[0]); if (m.present) return m; }
    const fmt::JsonValue& mesh = n["mesh"];
    const fmt::JsonValue& sm = mesh["surface_materials"];
    if (sm.isArray() && sm.size() > 0) { m = matOf(sm[0]); if (m.present) return m; }
    return matOf(mesh["material"]);
}

inline std::string sanitizeName(const std::string& n) {
    std::string out;
    for (char c : n) if (c != '.' && c != ':' && c != '/' && c != '@' && c != '%') out += c;
    return out;
}

// "A/B/C" with every name sanitised as the converter does.
inline std::string sanitizePath(const std::string& p) {
    if (p == ".") return p;
    std::string out, cur;
    for (char c : p + "/") {
        if (c == '/') { out += (out.empty() ? "" : "/") + sanitizeName(cur); cur.clear(); }
        else cur += c;
    }
    return out;
}

// ---- the converted side ------------------------------------------------------------------------------------------------------------

struct CVisual {
    std::string asset, material;
    Xform local, world;
    bool visible = true;
};

struct CNode {
    std::string path, name, cls, baseCls, scriptClass;
    int placement = -1, parent = -1;
    bool is3d = true, visibleOwn = true, visibleInTree = true;
    bool synthetic = false;
    Xform local, world;
    std::vector<CVisual> visuals;
};

struct Converted {
    fmt::OcWorldData data;
    std::vector<CNode> nodes;
    std::map<std::string, int> byPath;
    size_t strayPlain = 0;   // plain placements whose parent is not a class placement
    std::string error;
};

inline Xform placementLocal(const fmt::OcWorldPlacement& p) {
    const Quat q = world::quatFromEulerDeg(Vec3{static_cast<f32>(p.roll), static_cast<f32>(p.pitch), static_cast<f32>(p.yaw)});
    // The engine quaternion (z, -x, -y, w) of a Godot (x, y, z, w) read back.
    const Q4 g{-static_cast<double>(q.y), -static_cast<double>(q.z), static_cast<double>(q.x), static_cast<double>(q.w)};
    Xform x;
    x.basis = fabric::basisFromQuat(g) * M3::diag(V3{p.sy, p.sz, p.sx});
    x.pos = {p.y / 100.0, p.z / 100.0, -p.x / 100.0};
    return x;
}

inline bool parseProp(const fmt::OcWorldPlacement& p, const char* key, gd::GodotVariant& out) {
    for (const fmt::OcPlacedComponent& c : p.components) {
        if (c.type != "Props") continue;
        if (const std::string* t = c.find(key)) {
            gd::GodotDiagnostics d;
            return gd::parseGodotVariant(*t, out, &d, key);
        }
    }
    return false;
}

inline const gd::GodotVariant* objField(const gd::GodotVariant& o, const char* key) {
    for (usize i = 0; i < o.keys.size() && i < o.items.size(); ++i)
        if (o.keys[i].asString() == key) return &o.items[i];
    return nullptr;
}

// nodes.json: the Godot class each node of a scene has (a script node's placement is its script's class).
inline std::map<std::string, std::pair<std::string, std::string>> readBaseClasses(const std::string& projectDir, const std::string& sceneRes) {
    std::map<std::string, std::pair<std::string, std::string>> out;   // path -> {base class, placed class}
    std::string text;
    if (!readFileText(projectDir + "/Fabric/nodes.json", text)) return out;
    fmt::JsonValue j;
    if (!fmt::parseJson(text, j)) return out;
    const fmt::JsonValue& scenes = j["scenes"];
    for (usize i = 0; i < scenes.size(); ++i)
        if (scenes[i]["scene"].asString() == sceneRes) {
            const fmt::JsonValue& nodes = scenes[i]["nodes"];
            for (usize k = 0; k < nodes.size(); ++k)
                out[sanitizePath(std::string(nodes[k]["path"].asString()))] = {std::string(nodes[k]["base"].asString()), std::string(nodes[k]["class"].asString())};
        }
    return out;
}

inline Converted readConverted(const std::string& projectDir, const std::string& levelAsset, const std::string& sceneRes) {
    Converted c;
    std::string err;
    if (!fmt::loadOcworld(projectDir + "/Content/" + levelAsset, c.data, &err)) { c.error = "cannot read " + levelAsset + ": " + err; return c; }
    const fmt::OcWorldData& w = c.data;
    const auto bases = readBaseClasses(projectDir, sceneRes);
    std::vector<int> nodeOf(w.placements.size(), -1);
    int sceneRoot = -1;
    for (usize i = 0; i < w.placements.size(); ++i) {
        const fmt::OcWorldPlacement& p = w.placements[i];
        const int parentNode = p.parent >= 0 ? nodeOf[static_cast<usize>(p.parent)] : -1;
        if (p.className.empty()) {
            if (parentNode < 0) { ++c.strayPlain; continue; }
            CVisual v;
            v.asset = p.asset;
            v.material = p.material;
            v.local = placementLocal(p);
            v.visible = p.visible;
            c.nodes[static_cast<usize>(parentNode)].visuals.push_back(std::move(v));
            continue;
        }
        gd::GodotVariant persistent;
        if (parseProp(p, "_persistent", persistent) && persistent.asInt() == 1 && p.parent < 0) continue;   // an autoload is not part of the scene
        if (p.parent < 0 && sceneRoot >= 0) continue;   // one scene, one root
        CNode n;
        n.placement = static_cast<int>(i);
        n.parent = parentNode;
        gd::GodotVariant nm;
        n.name = sanitizeName(parseProp(p, "_name", nm) ? nm.s : p.name);
        n.path = parentNode < 0 ? "." : (c.nodes[static_cast<usize>(parentNode)].path == "." ? n.name : c.nodes[static_cast<usize>(parentNode)].path + "/" + n.name);
        gd::GodotVariant cl;
        n.baseCls = p.className;
        n.cls = p.className;
        // The node's Godot class: the carrier's _class, else the class the converter recorded as placed for the node (a script node's placement is its script's class).
        const auto rec = bases.find(n.path);
        if (parseProp(p, "_class", cl) && !cl.s.empty()) n.cls = cl.s;
        else if (rec != bases.end() && !rec->second.first.empty()) n.cls = rec->second.first;
        if (rec != bases.end() && rec->second.first != rec->second.second) n.scriptClass = rec->second.second;
        n.local = placementLocal(p);
        gd::GodotVariant xf;
        if (parseProp(p, "transform", xf) && xf.type == gd::GodotVType::Transform3D && xf.nums.size() >= 12) {   // a sheared basis is exact in the Prop
            for (int r = 0; r < 3; ++r)
                for (int col = 0; col < 3; ++col) n.local.basis.m[r][col] = xf.nums[static_cast<usize>(r * 3 + col)];
            n.local.pos = {xf.nums[9], xf.nums[10], xf.nums[11]};
        }
        gd::GodotVariant vis;
        if (parseProp(p, "visible", vis)) n.visibleOwn = vis.b || vis.asInt() != 0;
        n.is3d = !(n.cls == "Node" || n.cls == "Viewport" || n.cls.rfind("Control", 0) == 0);   // refined from the Godot side when compared
        // Godot gives a Node3D its parent's transform only when the parent is a Node3D itself.
        const CNode* par = parentNode >= 0 ? &c.nodes[static_cast<usize>(parentNode)] : nullptr;
        const bool parentIs3d = par && par->is3d;
        n.world = parentIs3d ? compose(par->world, n.local) : n.local;
        n.visibleInTree = n.visibleOwn && (!parentIs3d || par->visibleInTree);
        if (parentNode < 0) sceneRoot = static_cast<int>(c.nodes.size());
        nodeOf[i] = static_cast<int>(c.nodes.size());
        c.byPath[n.path] = static_cast<int>(c.nodes.size());
        c.nodes.push_back(std::move(n));
    }
    for (CNode& n : c.nodes) {
        for (CVisual& v : n.visuals) v.world = compose(n.world, v.local);
        // The converter's own nodes: a GridMap's instanced items, the collision bodies it and CSG add.
        const std::string& nm = n.name;
        n.synthetic = (nm.rfind("_", 0) == 0 && (nm == "_CSGCollision" || nm == "_GridCollision")) ||
                      (n.parent >= 0 && c.nodes[static_cast<usize>(n.parent)].cls == "GridMap" && nm.rfind("Item", 0) == 0);
    }
    // A node below a synthetic one is synthetic too (the collision shapes of a grid).
    for (CNode& n : c.nodes)
        if (!n.synthetic && n.parent >= 0 && c.nodes[static_cast<usize>(n.parent)].synthetic) n.synthetic = true;
    return c;
}

// ---- meshes and materials of the converted content ----------------------------------------------------------------------------------

class Content {
public:
    explicit Content(std::string dir) : dir_(std::move(dir)) {}

    // The box of an .ocmesh in Godot space (the inverse of the axis map).
    const Box& meshBox(const std::string& asset) {
        auto it = boxes_.find(asset);
        if (it != boxes_.end()) return it->second;
        Box b;
        fmt::OcMeshData m;
        std::string err;
        if (fmt::loadOcMesh(dir_ + "/" + asset, m, &err))
            for (usize i = 0; i + 2 < m.positions.size(); i += 3) b.add({m.positions[i + 1] / 100.0, m.positions[i + 2] / 100.0, -m.positions[i] / 100.0});
        slots_[asset] = m.materialSlots;
        return boxes_[asset] = b;
    }

    // The material slot 0 of a mesh names.
    std::string firstSlot(const std::string& asset) {
        meshBox(asset);
        const auto& s = slots_[asset];
        return s.empty() ? std::string() : s[0];
    }

    // baseColorFactor of Materials/<name>.ocmat, or false.
    bool albedo(const std::string& materialName, double out[4]) {
        std::string text;
        if (!readFileText(dir_ + "/Materials/" + materialName + ".ocmat", text)) return false;
        const size_t at = text.find("PARAM baseColorFactor ");
        if (at == std::string::npos) return false;
        return std::sscanf(text.c_str() + at + 22, "%lf %lf %lf %lf", &out[0], &out[1], &out[2], &out[3]) == 4;
    }

private:
    std::string dir_;
    std::map<std::string, Box> boxes_;
    std::map<std::string, std::vector<std::string>> slots_;
};

// ---- the comparison -------------------------------------------------------------------------------------------------------------------

struct Issue {
    std::string kind, where, detail;
};

struct SceneResult {
    std::string scene;
    size_t godotNodes = 0, convertedNodes = 0, matched = 0;
    std::map<std::string, size_t> counts;   // by kind
    std::vector<Issue> issues;
    std::string error;
    size_t instancesChecked = 0;

    void add(const std::string& kind, const std::string& where, const std::string& detail) {
        ++counts[kind];
        issues.push_back({kind, where, detail});
    }
    // Kinds that are documented differences, not defects.
    static bool documented(const std::string& kind) { return kind == "synthetic" || kind == "folded" || kind == "substituted" || kind == "inferred" || kind == "approximated"; }
    size_t mismatches() const {
        size_t n = 0;
        for (const auto& kv : counts) if (!documented(kv.first)) n += kv.second;
        return n;
    }
};

inline bool isFoldedCsg(const GNode& g, const std::map<std::string, size_t>& byPath) {
    // A CSG shape below another CSG shape is part of its root's baked mesh.
    if (g.cls.rfind("CSG", 0) != 0) return false;
    const size_t cut = g.path.find_last_of('/');
    if (cut == std::string::npos) return false;
    const auto it = byPath.find(g.path.substr(0, cut));
    return it != byPath.end();
}

inline double numberOf(const gd::GodotVariant& v) { return v.type == gd::GodotVType::Bool ? (v.b ? 1.0 : 0.0) : v.asNumber(); }

// A number the dump has, against the Prop of the converted node that states the same thing, where the node states it (Godot's file names only what differs from a
// default, so a value the level leaves out is not a difference).
inline void checkNumber(SceneResult& r, const GNode& g, const CNode& c, const Converted& conv, const char* group, const char* key, const char* prop, double scale = 1.0) {
    const fmt::JsonValue& e = (*g.json)[group];
    if (!e.isObject() || !e.has(key) || !(e[key].isNumber() || e[key].isBool())) return;
    gd::GodotVariant v;
    if (!parseProp(conv.data.placements[static_cast<usize>(c.placement)], prop, v)) return;
    const double want = e[key].isBool() ? (e[key].asBool() ? 1.0 : 0.0) : e[key].asDouble();
    const double have = numberOf(v) * scale;
    if (std::fabs(want - have) > 1e-4 * std::max(1.0, std::fabs(want))) {
        char buf[160];
        std::snprintf(buf, sizeof buf, "%s: Godot %.5g, level %.5g", prop, want, have);
        r.add("prop", g.path, buf);
    }
}

inline void checkProps(SceneResult& r, const GNode& g, const CNode& c, const Converted& conv) {
    const fmt::JsonValue& e = *g.json;
    if (c.placement < 0) return;
    if (e.has("light")) {
        checkNumber(r, g, c, conv, "light", "energy", "light_energy");
        checkNumber(r, g, c, conv, "light", "range", g.cls == "OmniLight3D" ? "omni_range" : "spot_range");
        checkNumber(r, g, c, conv, "light", "angle", "spot_angle");
        checkNumber(r, g, c, conv, "light", "shadow", "shadow_enabled");
    }
    if (e.has("camera"))
        for (const char* k : {"fov", "near", "far"}) checkNumber(r, g, c, conv, "camera", k, k);
    if (e.has("body")) {
        checkNumber(r, g, c, conv, "body", "layer", "collision_layer");
        checkNumber(r, g, c, conv, "body", "mask", "collision_mask");
        checkNumber(r, g, c, conv, "body", "mass", "mass");
    }
    if (e.has("shape") && e["shape"].has("class")) {
        gd::GodotVariant shape;
        if (!parseProp(conv.data.placements[static_cast<usize>(c.placement)], "shape", shape) || shape.type != gd::GodotVType::Object) {
            if (!e["shape"]["class"].asString().empty() && e["shape"]["class"].asString() != "WorldBoundaryShape3D") r.add("prop", g.path, "Godot has a " + std::string(e["shape"]["class"].asString()) + " and the level has no shape Object");
            return;
        }
        if (shape.s != e["shape"]["class"].asString()) r.add("prop", g.path, "shape class: Godot " + std::string(e["shape"]["class"].asString()) + ", level " + shape.s);
        const fmt::JsonValue& props = e["shape"]["props"];
        for (const char* k : {"size", "radius", "height"}) {
            const gd::GodotVariant* have = objField(shape, k);
            if (!have || !props.has(k)) continue;
            const fmt::JsonValue& want = props[k];
            if (want.isNumber() && std::fabs(want.asDouble() - numberOf(*have)) > 1e-4) r.add("prop", g.path, std::string("shape ") + k + ": Godot " + std::to_string(want.asDouble()) + ", level " + std::to_string(numberOf(*have)));
            if (want.isArray() && want.size() == 3 && have->nums.size() == 3)
                for (usize i = 0; i < 3; ++i)
                    if (std::fabs(want[i].asDouble() - have->nums[i]) > 1e-4) { r.add("prop", g.path, std::string("shape ") + k + " differs"); break; }
        }
    }
}

inline SceneResult compareScene(const std::string& sceneRes, const fmt::JsonValue& godot, const Converted& conv, Content& content) {
    SceneResult r;
    r.scene = sceneRes;
    if (!conv.error.empty()) { r.error = conv.error; return r; }
    if (!godot.isObject() || godot.has("error")) { r.error = "Godot could not load the scene: " + std::string(godot["error"].asString()); return r; }
    const fmt::JsonValue& jn = godot["nodes"];
    std::vector<GNode> gn;
    std::map<std::string, size_t> gByPath;
    for (usize i = 0; i < jn.size(); ++i) {
        const fmt::JsonValue& e = jn[i];
        GNode g;
        g.json = &e;
        g.path = sanitizePath(std::string(e["path"].asString()));
        g.name = std::string(e["name"].asString());
        g.cls = std::string(e["class"].asString());
        g.scriptPath = std::string(e["script"]["path"].asString());
        g.instance = std::string(e["instance"].asString());
        g.is3d = e["is3d"].asBool();
        if (g.is3d) {
            g.local = xfOf(e["local"]);
            g.world = xfOf(e["world"]);
            g.visible = e["visible"].asBool(true);
            g.visibleInTree = e["visible_in_tree"].asBool(true);
        }
        gByPath[g.path] = gn.size();
        gn.push_back(std::move(g));
    }
    r.godotNodes = gn.size();
    r.convertedNodes = conv.nodes.size();

    for (const GNode& g : gn) {
        const fmt::JsonValue& e = *g.json;
        const auto it = conv.byPath.find(g.path);
        if (it == conv.byPath.end()) {
            if (isFoldedCsg(g, gByPath)) r.add("folded", g.path, "a " + g.cls + " below a CSG shape is part of its root's baked mesh");
            else r.add("missing", g.path, g.cls + " is in Godot's scene and not in the level");
            continue;
        }
        const CNode& c = conv.nodes[static_cast<usize>(it->second)];
        ++r.matched;

        // ---- class and script ----
        const bool godotHasScript = !g.scriptPath.empty();
        const bool convHasScript = !c.scriptClass.empty();
        if (godotHasScript != convHasScript) r.add("script", g.path, std::string("Godot ") + (godotHasScript ? "has script " + g.scriptPath : "has no script") + "; the level " + (convHasScript ? "places script class " + c.scriptClass : "places no script class"));
        if (c.cls != g.cls) {
            if (g.cls == "CollisionPolygon3D" && c.cls == "CollisionShape3D") r.add("substituted", g.path, "CollisionPolygon3D is extruded into a CollisionShape3D");
            else r.add("class", g.path, "Godot " + g.cls + ", level " + c.cls);
        }

        // ---- transform and visibility ----
        const bool modelMesh = e.has("mesh") && !c.visuals.empty() && c.visuals[0].asset.rfind("Meshes/Generated/", 0) != 0;
        if (g.is3d && c.is3d) {
            if (!modelMesh) {
                const XformDiff d = diff(g.world, c.world);
                if (!d.within()) {
                    char buf[320];
                    const XformDiff dl = diff(g.local, c.local);
                    std::snprintf(buf, sizeof buf, "world pos %.4f m, rot %.5f rad, scale %.4f%s; local %s (%.4f m, %.5f rad, %.4f). Godot %s | level %s", d.pos, d.rot, d.scale, d.mirror ? ", mirror" : "",
                                  dl.within() ? "matches" : "differs", dl.pos, dl.rot, dl.scale, fmtXf(g.world).c_str(), fmtXf(c.world).c_str());
                    r.add("xform", g.path, buf);
                }
            }
            if (g.visibleInTree != c.visibleInTree) r.add("visible", g.path, std::string("Godot visible in tree ") + (g.visibleInTree ? "true" : "false") + ", level " + (c.visibleInTree ? "true" : "false"));
        }

        // ---- the drawing of a mesh node ----
        if (e.has("mesh") && !e["mesh"].isNull() && e["mesh"].size() > 0 && g.cls != "MultiMeshInstance3D") {
            if (c.visuals.empty()) {
                r.add("mesh", g.path, "Godot draws a " + std::string(e["mesh"]["class"].asString()) + " and the level places no mesh below the node");
            } else {
                const CVisual& v = c.visuals[0];
                const Box cb = transformed(v.world, content.meshBox(v.asset));
                const Box gb = boxOf(e["world_aabb"]);
                // Godot's dump has the box of the mesh's local box, turned: for a node turned off the axes that is larger than the box of the vertices, so the level's
                // box must lie inside it (and not be less than half of it on any axis); on the axes the two are the same box.
                const auto aligned = [](const Xform& x) {
                    for (int c = 0; c < 3; ++c) {
                        const V3 a = x.basis.column(c);
                        const double l = a.length();
                        if (l < 1e-9) return false;
                        const double m = std::max({std::fabs(a.x), std::fabs(a.y), std::fabs(a.z)}) / l;
                        if (m < 1.0 - 1e-6) return false;
                    }
                    return true;
                };
                const bool turned = gb.valid && cb.valid && !aligned(g.world);
                const auto inside = [](const Box& in, const Box& out) {
                    return in.lo.x >= out.lo.x - kBoxTol && in.lo.y >= out.lo.y - kBoxTol && in.lo.z >= out.lo.z - kBoxTol && in.hi.x <= out.hi.x + kBoxTol && in.hi.y <= out.hi.y + kBoxTol &&
                           in.hi.z <= out.hi.z + kBoxTol;
                };
                const auto half = [](const Box& in, const Box& out) {
                    return (in.hi.x - in.lo.x) >= 0.5 * (out.hi.x - out.lo.x) && (in.hi.y - in.lo.y) >= 0.5 * (out.hi.y - out.lo.y) && (in.hi.z - in.lo.z) >= 0.5 * (out.hi.z - out.lo.z);
                };
                if (!cb.valid) r.add("mesh", g.path, "the mesh " + v.asset + " cannot be read or is empty");
                else if (turned) {
                    if (!inside(cb, gb) || !half(cb, gb)) r.add("mesh", g.path, "the level's mesh box is not inside Godot's box of the turned mesh, or is under half of it, for " + v.asset);
                } else if (gb.valid && boxDiff(cb, gb) > kBoxTol + 5e-4 * std::max({gb.hi.x - gb.lo.x, gb.hi.y - gb.lo.y, gb.hi.z - gb.lo.z})) {   // Godot's stored box of an imported mesh is a little padded
                    char buf[320];
                    std::snprintf(buf, sizeof buf, "world box differs by %.4f m: Godot (%.3f %.3f %.3f)-(%.3f %.3f %.3f), level (%.3f %.3f %.3f)-(%.3f %.3f %.3f) for %s", boxDiff(cb, gb), gb.lo.x, gb.lo.y,
                                  gb.lo.z, gb.hi.x, gb.hi.y, gb.hi.z, cb.lo.x, cb.lo.y, cb.lo.z, cb.hi.x, cb.hi.y, cb.hi.z, v.asset.c_str());
                    r.add("mesh", g.path, buf);
                }
                if (g.visibleInTree != v.visible && g.is3d) r.add("visible", g.path, std::string("the placed mesh is ") + (v.visible ? "shown" : "hidden") + ", Godot's node is " + (g.visibleInTree ? "visible" : "hidden"));
                // ---- material: the colour of the first surface ----
                const Mat gm = effectiveMaterial(e);
                if (gm.present && gm.hasAlbedo) {
                    const std::string matName = !v.material.empty() ? v.material : content.firstSlot(v.asset);
                    double a[4] = {0, 0, 0, 0};
                    if (matName.empty() || !content.albedo(matName, a)) {
                        // A white material is the engine's default surface too (a glTF mesh with no material gets one of these in Godot).
                        const bool white = gm.albedo[0] > 0.99 && gm.albedo[1] > 0.99 && gm.albedo[2] > 0.99;
                        if (!white) r.add("material", g.path, "Godot's material is " + gm.cls + " (albedo " + std::to_string(gm.albedo[0]).substr(0, 5) + " " + std::to_string(gm.albedo[1]).substr(0, 5) + " " +
                                                                  std::to_string(gm.albedo[2]).substr(0, 5) + ") and the level has none for '" + matName + "'");
                    } else {
                        double lin[3] = {srgbToLinear(gm.albedo[0]), srgbToLinear(gm.albedo[1]), srgbToLinear(gm.albedo[2])};
                        const bool asLinear = std::fabs(a[0] - lin[0]) < 2e-3 && std::fabs(a[1] - lin[1]) < 2e-3 && std::fabs(a[2] - lin[2]) < 2e-3;
                        const bool asRaw = std::fabs(a[0] - gm.albedo[0]) < 2e-3 && std::fabs(a[1] - gm.albedo[1]) < 2e-3 && std::fabs(a[2] - gm.albedo[2]) < 2e-3;
                        // A model's own materials come from the engine's glTF importer, which stores the sRGB colour as the linear factor raised to 1/2.2 (not the sRGB curve).
                        const bool asGamma = std::fabs(a[0] - std::pow(lin[0], 1 / 2.2)) < 3e-3 && std::fabs(a[1] - std::pow(lin[1], 1 / 2.2)) < 3e-3 && std::fabs(a[2] - std::pow(lin[2], 1 / 2.2)) < 3e-3;
                        if (!asLinear && !asRaw && asGamma) {
                            r.add("approximated", g.path, "albedo of " + matName + " is the engine importer's 1/2.2 gamma of Godot's colour, not the sRGB curve");
                        } else if (!asLinear && !asRaw) {
                            char buf[260];
                            std::snprintf(buf, sizeof buf, "albedo: Godot (%.3f %.3f %.3f) sRGB = (%.3f %.3f %.3f) linear, %s has (%.3f %.3f %.3f)", gm.albedo[0], gm.albedo[1], gm.albedo[2], lin[0], lin[1], lin[2],
                                          matName.c_str(), a[0], a[1], a[2]);
                            r.add("material", g.path, buf);
                        }
                    }
                } else if (!gm.present && !c.visuals.empty() && !v.material.empty()) {
                    r.add("material", g.path, "the level overrides the material with " + v.material + " and Godot's mesh has none");
                }
            }
        } else if (!c.visuals.empty() && c.cls != "MultiMeshInstance3D" && !e.has("multimesh") && !(e.has("csg") && e["csg"]["root"].asBool())) {
            r.add("mesh", g.path, "the level places a mesh " + c.visuals[0].asset + " and Godot's node draws none");
        }

        // ---- CSG roots (only with the tree) ----
        if (e.has("csg") && e["csg"].has("world_aabb") && !c.visuals.empty()) {
            const Box cb = transformed(c.visuals[0].world, content.meshBox(c.visuals[0].asset));
            const Box gb = boxOf(e["csg"]["world_aabb"]);
            if (gb.valid && cb.valid && boxDiff(cb, gb) > 1e-2) {
                char buf[200];
                std::snprintf(buf, sizeof buf, "the baked CSG mesh's box differs from Godot's by %.4f m", boxDiff(cb, gb));
                r.add("mesh", g.path, buf);
            }
        }

        // ---- MultiMesh ----
        if (e.has("multimesh") && e["multimesh"].size() > 0) {
            const fmt::JsonValue& mm = e["multimesh"];
            const size_t want = static_cast<size_t>(mm["count"].asInt());
            if (mm["format"].asInt() != 1) {
                r.add("inferred", g.path, "a 2D MultiMesh has no counterpart");
            } else if (c.visuals.size() != want) {
                r.add("instances", g.path, "MultiMesh has " + std::to_string(want) + " instances, the level places " + std::to_string(c.visuals.size()));
            } else {
                const fmt::JsonValue& list = mm["transforms"];
                const int vis = static_cast<int>(mm["visible_count"].asInt(-1));
                size_t bad = 0, hidden = 0;
                std::string first;
                for (size_t i = 0; i < want && i < list.size(); ++i) {
                    ++r.instancesChecked;
                    const Xform gw = g.world.composed(xfOf(list[i]));
                    const XformDiff d = diff(gw, c.visuals[i].world);
                    if (!d.within()) { if (!bad++) first = "instance " + std::to_string(i) + ": " + fmtXf(gw) + " | " + fmtXf(c.visuals[i].world); }
                    const bool shouldShow = g.visibleInTree && (vis < 0 || static_cast<int>(i) < vis);
                    if (c.visuals[i].visible != shouldShow) ++hidden;
                }
                if (bad) r.add("instances", g.path, std::to_string(bad) + " of " + std::to_string(want) + " instance transforms differ; first " + first);
                if (hidden) r.add("instances", g.path, std::to_string(hidden) + " instance(s) shown or hidden against visible_instance_count " + std::to_string(vis));
                if (!c.visuals.empty()) {
                    const fmt::JsonValue& meshBox = mm["mesh"]["aabb"];
                    const Box lb = boxOf(meshBox), cbx = content.meshBox(c.visuals[0].asset);
                    if (lb.valid && cbx.valid && boxDiff(lb, cbx) > kBoxTol) r.add("mesh", g.path, "the instanced mesh's box differs from Godot's by " + std::to_string(boxDiff(lb, cbx)) + " m");
                }
            }
        }

        // ---- GridMap: every cell's mesh, as a set ----
        if (e.has("gridmap") && e["gridmap"].size() > 0) {
            const fmt::JsonValue& gm = e["gridmap"];
            std::vector<Xform> want;
            const fmt::JsonValue& cells = gm["cells"];
            const fmt::JsonValue& items = gm["items"];
            for (usize k = 0; k < cells.size(); ++k) {
                const std::string id = std::to_string(cells[k]["item"].asInt());
                if (!items.has(id) || items[id]["mesh"].size() == 0) continue;
                want.push_back(g.world.composed(xfOf(cells[k]["xform"])).composed(xfOf(items[id]["mesh_transform"])));
            }
            std::vector<Xform> have;
            for (const CNode& s : conv.nodes)
                if (s.synthetic && s.parent == it->second)
                    for (const CVisual& v : s.visuals) have.push_back(v.world);
            r.instancesChecked += want.size();
            if (want.size() != have.size()) {
                r.add("instances", g.path, "GridMap has " + std::to_string(want.size()) + " cells with a mesh, the level places " + std::to_string(have.size()));
            } else {
                std::vector<bool> used(have.size(), false);
                size_t unmatched = 0;
                std::string first;
                for (const Xform& x : want) {
                    bool found = false;
                    for (size_t h = 0; h < have.size() && !found; ++h)
                        if (!used[h] && diff(x, have[h]).within()) { used[h] = true; found = true; }
                    if (!found && !unmatched++) first = fmtXf(x);
                }
                if (unmatched) r.add("instances", g.path, std::to_string(unmatched) + " of " + std::to_string(want.size()) + " cell transforms have no instance in the level; first " + first);
            }
        }

        checkProps(r, g, c, conv);
    }

    // ---- the level's nodes Godot does not have ----
    for (const CNode& c : conv.nodes) {
        if (gByPath.count(c.path)) continue;
        if (c.synthetic) r.add("synthetic", c.path, c.cls + " is the converter's own node");
        else r.add("extra", c.path, c.cls + " is in the level and not in Godot's scene");
    }
    if (conv.strayPlain) r.add("extra", ".", std::to_string(conv.strayPlain) + " plain placement(s) with no node above them");
    return r;
}

inline std::string summary(const SceneResult& r) {
    char head[200];
    std::snprintf(head, sizeof head, "%s: Godot %zu nodes, level %zu, matched %zu, instances checked %zu; mismatches %zu", r.scene.c_str(), r.godotNodes, r.convertedNodes, r.matched, r.instancesChecked, r.mismatches());
    std::string s = head;
    for (const auto& kv : r.counts) s += (SceneResult::documented(kv.first) ? "  [" : "  ") + kv.first + "=" + std::to_string(kv.second) + (SceneResult::documented(kv.first) ? "]" : "");
    if (!r.error.empty()) s += "  ERROR: " + r.error;
    return s;
}

}  // namespace aver::fabric::cmp
