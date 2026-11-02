// The node tree Godot's glTF importer makes for a model file. See FabricModelTree.hpp.
#include "FabricModelTree.hpp"

#include "aver/formats/Json.hpp"
#include "aver/platform/FileSystem.hpp"

#include <algorithm>
#include <cctype>
#include <cmath>
#include <cstring>
#include <functional>
#include <map>
#include <numeric>
#include <set>
#include <tuple>

namespace aver::fabric {

namespace {

using fmt::JsonValue;

// Godot's String::validate_node_name: the characters a node name cannot hold become underscores ("Cube.001" is "Cube_001").
std::string validNodeName(std::string s) {
    for (char& c : s)
        if (c == '.' || c == ':' || c == '@' || c == '/' || c == '"' || c == '%') c = '_';
    return s;
}

// A glTF node's local transform: its matrix, or translation, rotation and scale. `has` tells whether the node states one.
Xform nodeLocal(const JsonValue& n, bool& has) {
    Xform x;
    has = false;
    if (n.has("matrix") && n["matrix"].size() >= 16) {
        const JsonValue& m = n["matrix"];   // column-major
        for (int c = 0; c < 3; ++c)
            for (int r = 0; r < 3; ++r) x.basis.m[r][c] = m[static_cast<usize>(c * 4 + r)].asDouble();
        x.pos = {m[12].asDouble(), m[13].asDouble(), m[14].asDouble()};
        has = true;
        return x;
    }
    V3 s{1, 1, 1};
    if (n.has("scale") && n["scale"].size() >= 3) { s = {n["scale"][0].asDouble(1), n["scale"][1].asDouble(1), n["scale"][2].asDouble(1)}; has = true; }
    if (n.has("rotation") && n["rotation"].size() >= 4) {
        const JsonValue& q = n["rotation"];
        x.basis = basisFromQuat({q[0].asDouble(), q[1].asDouble(), q[2].asDouble(), q[3].asDouble(1)});
        has = true;
    }
    x.basis = x.basis * M3::diag(s);
    if (n.has("translation") && n["translation"].size() >= 3) {
        x.pos = {n["translation"][0].asDouble(), n["translation"][1].asDouble(), n["translation"][2].asDouble()};
        has = true;
    }
    return x;
}

std::vector<u8> base64Decode(std::string_view s) {
    std::vector<u8> out;
    u32 acc = 0;
    int bits = 0;
    for (const char c : s) {
        int v;
        if (c >= 'A' && c <= 'Z') v = c - 'A';
        else if (c >= 'a' && c <= 'z') v = c - 'a' + 26;
        else if (c >= '0' && c <= '9') v = c - '0' + 52;
        else if (c == '+') v = 62;
        else if (c == '/') v = 63;
        else continue;
        acc = (acc << 6) | static_cast<u32>(v);
        bits += 6;
        if (bits >= 8) { bits -= 8; out.push_back(static_cast<u8>((acc >> bits) & 0xFF)); }
    }
    return out;
}

// The buffers of a glTF file (the GLB's binary chunk, a data URI, or a file beside the model), read when a physics shape needs a mesh's triangles.
struct GltfBuffers {
    const JsonValue* doc = nullptr;
    std::string dir;
    std::vector<u8> glbBin;
    std::map<int, std::vector<u8>> cache;

    const std::vector<u8>* get(int i) {
        if (const auto it = cache.find(i); it != cache.end()) return &it->second;
        const JsonValue& b = (*doc)["buffers"][static_cast<usize>(i < 0 ? 0 : i)];
        if (i < 0 || !b.isObject()) return nullptr;
        std::vector<u8> data;
        const std::string uri(b["uri"].asString());
        if (uri.empty()) data = glbBin;
        else if (uri.rfind("data:", 0) == 0) {
            const size_t comma = uri.find(',');
            if (comma != std::string::npos) data = base64Decode(std::string_view(uri).substr(comma + 1));
        } else {
            std::string name;
            for (size_t k = 0; k < uri.size(); ++k) {   // %20 and the like
                if (uri[k] == '%' && k + 2 < uri.size() && std::isxdigit(static_cast<unsigned char>(uri[k + 1])) && std::isxdigit(static_cast<unsigned char>(uri[k + 2]))) {
                    name += static_cast<char>(std::stoi(uri.substr(k + 1, 2), nullptr, 16));
                    k += 2;
                } else {
                    name += uri[k];
                }
            }
            if (!readFileBytes(dir + "/" + name, data)) return nullptr;
        }
        return &(cache[i] = std::move(data));
    }

    // An accessor's elements as doubles (`comps` per element); false when it cannot be read.
    bool read(int acc, int comps, std::vector<double>& out, bool normalize = false) {
        const JsonValue& a = (*doc)["accessors"][static_cast<usize>(acc < 0 ? 0 : acc)];
        if (acc < 0 || !a.isObject() || !a.has("bufferView")) return false;
        const JsonValue& bv = (*doc)["bufferViews"][static_cast<usize>(a["bufferView"].asInt(0))];
        const std::vector<u8>* buf = get(static_cast<int>(bv["buffer"].asInt(0)));
        if (!buf) return false;
        const int ct = static_cast<int>(a["componentType"].asInt(0));
        const size_t csz = ct == 5126 || ct == 5125 ? 4 : (ct == 5123 || ct == 5122 ? 2 : 1);
        const size_t count = static_cast<size_t>(a["count"].asInt(0));
        const size_t stride = bv.has("byteStride") ? static_cast<size_t>(bv["byteStride"].asInt(0)) : csz * static_cast<size_t>(comps);
        const size_t base = static_cast<size_t>(bv["byteOffset"].asInt(0)) + static_cast<size_t>(a["byteOffset"].asInt(0));
        if (count == 0 || base + (count - 1) * stride + csz * static_cast<size_t>(comps) > buf->size()) return false;
        out.reserve(out.size() + count * static_cast<size_t>(comps));
        for (size_t e = 0; e < count; ++e)
            for (int c = 0; c < comps; ++c) {
                const u8* p = buf->data() + base + e * stride + static_cast<size_t>(c) * csz;
                double v;
                if (ct == 5126) { f32 f; std::memcpy(&f, p, 4); v = f; }
                else if (ct == 5125) { u32 u; std::memcpy(&u, p, 4); v = u; }
                else if (ct == 5123) { u16 u; std::memcpy(&u, p, 2); v = u; }
                else if (ct == 5122) { i16 u; std::memcpy(&u, p, 2); v = u; }
                else if (ct == 5120) v = static_cast<i8>(*p);
                else v = *p;
                if (normalize) {   // glTF's normalised integers
                    if (ct == 5120) v = std::max(v / 127.0, -1.0);
                    else if (ct == 5121) v = v / 255.0;
                    else if (ct == 5122) v = std::max(v / 32767.0, -1.0);
                    else if (ct == 5123) v = v / 65535.0;
                }
                out.push_back(v);
            }
        return true;
    }

    // The triangles of a mesh, three numbers per vertex, in file order (indexed triangle lists only).
    bool triangles(int mesh, std::vector<double>& out) {
        const JsonValue& prims = (*doc)["meshes"][static_cast<usize>(mesh < 0 ? 0 : mesh)]["primitives"];
        if (mesh < 0 || prims.size() == 0) return false;
        for (usize k = 0; k < prims.size(); ++k) {
            const JsonValue& p = prims[k];
            if (p["mode"].asInt(4) != 4) return false;
            std::vector<double> pos, idx;
            if (!read(static_cast<int>(p["attributes"]["POSITION"].asInt(-1)), 3, pos)) return false;
            if (p.has("indices")) {
                if (!read(static_cast<int>(p["indices"].asInt(-1)), 1, idx)) return false;
            } else {
                for (size_t i = 0; i < pos.size() / 3; ++i) idx.push_back(static_cast<double>(i));
            }
            for (size_t i = 0; i + 2 < idx.size(); i += 3)
                for (int c = 0; c < 3; ++c) {
                    const size_t v = static_cast<size_t>(idx[i + static_cast<size_t>(c)]);
                    if (v * 3 + 2 >= pos.size()) return false;
                    out.insert(out.end(), {pos[v * 3], pos[v * 3 + 1], pos[v * 3 + 2]});
                }
        }
        return !out.empty();
    }
};

}  // namespace

bool buildModelTree(const std::string& path, ModelTree& out, std::string& why) {
    out = ModelTree{};
    std::vector<u8> bytes;
    if (!readFileBytes(path, bytes)) { why = "could not read " + path; return false; }
    std::string_view text(reinterpret_cast<const char*>(bytes.data()), bytes.size());
    if (bytes.size() >= 20 && std::memcmp(bytes.data(), "glTF", 4) == 0) {
        const auto le32 = [&](size_t at) {
            return static_cast<u32>(bytes[at]) | static_cast<u32>(bytes[at + 1]) << 8 | static_cast<u32>(bytes[at + 2]) << 16 | static_cast<u32>(bytes[at + 3]) << 24;
        };
        const u32 len = le32(12);
        if (le32(16) != 0x4E4F534Au || static_cast<size_t>(len) + 20 > bytes.size()) { why = "the GLB has no JSON chunk"; return false; }
        text = text.substr(20, len);
    }
    GltfBuffers gb;
    if (bytes.size() >= 20 && std::memcmp(bytes.data(), "glTF", 4) == 0) {   // the GLB's binary chunk follows the JSON chunk
        const size_t after = 20 + text.size();
        if (after + 8 <= bytes.size()) {
            const size_t binLen = static_cast<size_t>(bytes[after]) | static_cast<size_t>(bytes[after + 1]) << 8 | static_cast<size_t>(bytes[after + 2]) << 16 | static_cast<size_t>(bytes[after + 3]) << 24;
            if (after + 8 + binLen <= bytes.size()) gb.glbBin.assign(bytes.begin() + static_cast<std::ptrdiff_t>(after + 8), bytes.begin() + static_cast<std::ptrdiff_t>(after + 8 + binLen));
        }
    }
    gb.dir = path.substr(0, path.find_last_of("/\\") == std::string::npos ? 0 : path.find_last_of("/\\"));
    JsonValue doc;
    if (!fmt::parseJson(text, doc, &why)) return false;
    if (!doc.isObject()) { why = "not a glTF document"; return false; }
    gb.doc = &doc;

    const JsonValue& jn = doc["nodes"];
    const int N = static_cast<int>(jn.size());
    const auto at = [](int i) { return static_cast<size_t>(i); };

    // ---- the node graph ----
    std::vector<int> par(at(N), -1);
    std::vector<std::vector<int>> kids(at(N));
    for (int i = 0; i < N; ++i) {
        const JsonValue& ch = jn[at(i)]["children"];
        for (usize k = 0; k < ch.size(); ++k) {
            const int c = static_cast<int>(ch[k].asInt(-1));
            if (c < 0 || c >= N || c == i || par[at(c)] >= 0) continue;
            par[at(c)] = i;
            kids[at(i)].push_back(c);
        }
    }
    std::vector<int> roots;
    const int sceneIdx = static_cast<int>(doc["scene"].asInt(0));
    const JsonValue& scenes = doc["scenes"];
    if (sceneIdx >= 0 && static_cast<usize>(sceneIdx) < scenes.size()) {
        const JsonValue& rn = scenes[static_cast<usize>(sceneIdx)]["nodes"];
        for (usize k = 0; k < rn.size(); ++k) {
            const int c = static_cast<int>(rn[k].asInt(-1));
            if (c >= 0 && c < N) roots.push_back(c);
        }
    } else {
        for (int i = 0; i < N; ++i)
            if (par[at(i)] < 0) roots.push_back(i);
    }

    // ---- names: unique across the file, in node order (a repeat gets a number after it) ----
    std::set<std::string> used;
    const auto uniqueName = [&](const std::string& raw) {
        const std::string base = validNodeName(raw);
        std::string cand = base;
        int n = 1;
        while (used.count(cand)) cand = base + std::to_string(++n);
        used.insert(cand);
        return cand;
    };
    std::vector<std::string> nm(at(N));
    for (int i = 0; i < N; ++i) {
        std::string raw(jn[at(i)]["name"].asString());
        if (raw.empty()) raw = jn[at(i)].has("mesh") ? "MeshInstance3D" : "Node3D";
        nm[at(i)] = uniqueName(raw);
    }

    // ---- skeletons: skins that share a joint are one skeleton; its bones are the joints and the nodes between them ----
    const JsonValue& skins = doc["skins"];
    const int S = static_cast<int>(skins.size());
    std::vector<int> uf(at(S));
    std::iota(uf.begin(), uf.end(), 0);
    const std::function<int(int)> find = [&](int a) { return uf[at(a)] == a ? a : uf[at(a)] = find(uf[at(a)]); };
    std::vector<int> jointSkin(at(N), -1);
    for (int s = 0; s < S; ++s) {
        const JsonValue& js = skins[at(s)]["joints"];
        for (usize k = 0; k < js.size(); ++k) {
            const int j = static_cast<int>(js[k].asInt(-1));
            if (j < 0 || j >= N) continue;
            if (jointSkin[at(j)] < 0) jointSkin[at(j)] = s;
            else uf[at(find(s))] = find(jointSkin[at(j)]);
        }
    }
    std::map<int, std::vector<int>> groupJoints;   // skeleton (its skin group) -> joint nodes
    for (int s = 0; s < S; ++s) {
        const JsonValue& js = skins[at(s)]["joints"];
        std::vector<int>& list = groupJoints[find(s)];
        for (usize k = 0; k < js.size(); ++k) {
            const int j = static_cast<int>(js[k].asInt(-1));
            if (j >= 0 && j < N && std::find(list.begin(), list.end(), j) == list.end()) list.push_back(j);
        }
    }
    const auto ancestorOrSelf = [&](int a, int b) {
        for (int n = b, guard = 0; n >= 0 && guard <= N; n = par[at(n)], ++guard)
            if (n == a) return true;
        return false;
    };
    std::vector<int> boneGroup(at(N), -1);
    std::map<int, int> groupParent;   // skeleton -> the node its bones hang from (-1: the scene root)
    for (const auto& [g, js] : groupJoints) {
        if (js.empty()) continue;
        int lca = js[0];
        for (const int j : js)
            while (lca >= 0 && !ancestorOrSelf(lca, j)) lca = par[at(lca)];
        const int p = lca >= 0 && std::find(js.begin(), js.end(), lca) != js.end() ? par[at(lca)] : lca;
        groupParent[g] = p;
        for (const int j : js)
            for (int n = j, guard = 0; n >= 0 && n != p && guard <= N; n = par[at(n)], ++guard) {
                if (boneGroup[at(n)] >= 0 && boneGroup[at(n)] != g) break;
                boneGroup[at(n)] = g;
            }
    }
    // A bone's name is not a node name: Godot's importer only replaces ':' and '/' ("Holder.R" stays), unique within its skeleton (a repeat
    // gets _2, _3, ...). The engine's .ocskel keeps the file's names too, which is how the skeleton is bound to it.
    std::vector<std::string> bn(at(N));
    std::map<int, std::set<std::string>> boneNamesUsed;
    for (int i = 0; i < N; ++i) {
        if (boneGroup[at(i)] < 0) continue;
        std::string base(jn[at(i)]["name"].asString());
        if (base.empty()) base = "bone";
        for (char& c : base)
            if (c == ':' || c == '/') c = '_';
        std::set<std::string>& used = boneNamesUsed[boneGroup[at(i)]];
        std::string cand = base;
        for (int k = 2; used.count(cand); ++k) cand = base + "_" + std::to_string(k);
        used.insert(cand);
        bn[at(i)] = cand;
    }

    // ---- the tree ----
    ModelTree& T = out;
    T.nodes.emplace_back();
    T.nodes[0].type = "Node3D";
    const auto add = [&](int parent, const std::string& name, const std::string& type) {
        ModelTreeNode n;
        n.name = name;
        n.type = type;
        n.parent = parent;
        T.nodes.push_back(std::move(n));
        const int idx = static_cast<int>(T.nodes.size()) - 1;
        T.nodes[at(parent)].children.push_back(idx);
        return idx;
    };
    // Godot's add_child(legible_unique_name): the class name, then the class name with 2, 3, ...
    const auto siblingName = [&](int parent, const std::string& base) {
        std::string cand = base;
        for (int n = 2;; ++n) {
            bool taken = false;
            for (const int c : T.nodes[at(parent)].children) taken = taken || T.nodes[at(c)].name == cand;
            if (!taken) return cand;
            cand = base + std::to_string(n);
        }
    };

    std::vector<int> treeOf(at(N), -1), boneIndex(at(N), -1);
    std::vector<char> seen(at(N), 0);
    std::map<int, int> skelNode;                    // skeleton -> its Skeleton3D tree node
    std::map<int, std::vector<Xform>> boneWorld;    // skeleton -> each bone's rest pose in the skeleton's space
    bool foreignNode = false;

    // ---- glTF physics (OMI_physics_body / OMI_physics_shape): Godot's importer makes a body of a node with a motion and a CollisionShape3D of a node with a collider ----
    const JsonValue& shapeDefs = doc["extensions"]["OMI_physics_shape"]["shapes"];
    bool physicsUsed = false, physicsUnread = false;
    const auto shapeOn = [&](const JsonValue& collider, ModelTreeNode& tn) -> bool {
        const int idx = static_cast<int>(collider["shape"].asInt(-1));
        if (idx < 0 || static_cast<usize>(idx) >= shapeDefs.size()) return false;
        const JsonValue& s = shapeDefs[static_cast<usize>(idx)];
        const std::string type(s["type"].asString());
        const JsonValue& d = s[type];
        tn.shapeNums.clear();
        if (type == "box") {
            tn.shapeClass = "BoxShape3D";
            const JsonValue& sz = d["size"];
            tn.shapeNums = {sz.size() >= 3 ? sz[0].asDouble(1) : 1.0, sz.size() >= 3 ? sz[1].asDouble(1) : 1.0, sz.size() >= 3 ? sz[2].asDouble(1) : 1.0};
        } else if (type == "sphere") {
            tn.shapeClass = "SphereShape3D";
            tn.shapeNums = {d["radius"].asDouble(0.5)};
        } else if (type == "capsule" || type == "cylinder") {
            tn.shapeClass = type == "capsule" ? "CapsuleShape3D" : "CylinderShape3D";
            tn.shapeNums = {d["radius"].asDouble(0.5), d["height"].asDouble(2.0)};
        } else if (type == "trimesh" || type == "convex") {
            std::vector<double> tris;
            if (!gb.triangles(static_cast<int>(d["mesh"].asInt(-1)), tris)) return false;
            if (type == "trimesh") {
                tn.shapeClass = "ConcavePolygonShape3D";
                tn.shapeNums = std::move(tris);
            } else {
                tn.shapeClass = "ConvexPolygonShape3D";
                std::set<std::tuple<double, double, double>> seenPoints;
                for (size_t i = 0; i + 2 < tris.size(); i += 3)
                    if (seenPoints.insert({tris[i], tris[i + 1], tris[i + 2]}).second) tn.shapeNums.insert(tn.shapeNums.end(), {tris[i], tris[i + 1], tris[i + 2]});
            }
        } else {
            return false;
        }
        return true;
    };
    const auto physicsOf = [&](const JsonValue& jnode, int t) {
        const JsonValue& body = jnode["extensions"]["OMI_physics_body"];
        if (!body.isObject()) return;
        physicsUsed = true;
        const bool hasMesh = jnode.has("mesh");
        bool isBody = false;
        if (body.has("motion") && !hasMesh) {
            const std::string_view mt = body["motion"]["type"].asString();
            T.nodes[at(t)].type = mt == "dynamic" ? "RigidBody3D" : (mt == "kinematic" ? "AnimatableBody3D" : "StaticBody3D");
            isBody = true;
        } else if (body.has("motion")) {
            physicsUnread = true;   // a body that is also a mesh: Godot makes a body with the mesh below it; not made here
        }
        if (body.has("trigger")) physicsUnread = true;
        if (!body.has("collider")) return;
        ModelTreeNode shape;
        if (!shapeOn(body["collider"], shape)) { physicsUnread = true; return; }
        int target = t;
        if (isBody || hasMesh) {
            target = static_cast<int>(T.nodes.size());
            ModelTreeNode child;
            child.name = siblingName(t, "CollisionShape3D");
            child.type = "CollisionShape3D";
            child.parent = t;
            T.nodes.push_back(std::move(child));
            T.nodes[at(t)].children.push_back(target);
        } else {
            T.nodes[at(t)].type = "CollisionShape3D";
        }
        T.nodes[at(target)].shapeClass = shape.shapeClass;
        T.nodes[at(target)].shapeNums = std::move(shape.shapeNums);
    };

    std::function<void(int, int, int)> gen;
    gen = [&](int n, int sceneParent, int depth) {
        if (depth > 4096 || seen[at(n)]) return;
        seen[at(n)] = 1;
        if (boneGroup[at(n)] >= 0) {
            // A joint is a bone, not a node. The skeleton node appears where its first bone is met.
            const int g = boneGroup[at(n)];
            if (!skelNode.count(g)) {
                const int sk = add(sceneParent, uniqueName("Skeleton3D"), "Skeleton3D");
                skelNode[g] = sk;
                std::vector<ModelBone> bones;
                std::vector<Xform> world;
                std::function<void(int, int)> walk;
                walk = [&](int b, int pb) {
                    if (boneGroup[at(b)] != g || boneIndex[at(b)] >= 0 || bones.size() >= 4096) return;   // a hostile file's chain must not run the stack out
                    bool has = false;
                    ModelBone mb;
                    mb.name = bn[at(b)];
                    mb.gltfNode = b;
                    mb.parent = pb;
                    mb.rest = nodeLocal(jn[at(b)], has);
                    const int idx = static_cast<int>(bones.size());
                    boneIndex[at(b)] = idx;
                    world.push_back(pb >= 0 ? world[at(pb)].composed(mb.rest) : mb.rest);
                    bones.push_back(std::move(mb));
                    for (const int c : kids[at(b)]) walk(c, idx);
                };
                const int p = groupParent[g];
                if (p >= 0) for (const int c : kids[at(p)]) walk(c, -1);
                else for (const int r : roots) walk(r, -1);
                T.nodes[at(sk)].bones = std::move(bones);
                boneWorld[g] = std::move(world);
            }
            // The nodes hung on a bone share one BoneAttachment3D below the skeleton, named for the bone as Godot's importer names it
            // (scenes override them by that path: Skeleton3D/hand_R/pistol).
            const int sk = skelNode[g];
            int att = -1;
            for (const int c : kids[at(n)]) {
                if (boneGroup[at(c)] == g || att >= 0) continue;
                att = add(sk, siblingName(sk, validNodeName(bn[at(n)])), "BoneAttachment3D");
                T.nodes[at(att)].boneName = bn[at(n)];
                const std::vector<Xform>& bw = boneWorld[g];
                if (boneIndex[at(n)] >= 0 && at(boneIndex[at(n)]) < bw.size()) T.nodes[at(att)].xf = bw[at(boneIndex[at(n)])];
            }
            for (const int c : kids[at(n)]) gen(c, boneGroup[at(c)] == g ? sceneParent : att, depth + 1);
            return;
        }
        const JsonValue& jnode = jn[at(n)];
        const int t = add(sceneParent, nm[at(n)], jnode.has("mesh") ? "MeshInstance3D" : "Node3D");
        bool has = false;
        T.nodes[at(t)].xf = nodeLocal(jnode, has);
        T.nodes[at(t)].gltfNode = n;
        treeOf[at(n)] = t;
        physicsOf(jnode, t);
        if (jnode.has("camera") || (jnode.has("extensions") && jnode["extensions"].has("KHR_lights_punctual"))) foreignNode = true;
        for (const int c : kids[at(n)]) gen(c, t, depth + 1);
    };

    // The file's one root can be the scene root itself (Godot writes GODOT_single_root); anything else gets a root named for the file.
    bool single = false;
    const JsonValue& ext = doc["extensionsUsed"];
    for (usize k = 0; k < ext.size(); ++k) single = single || ext[k].asString() == "GODOT_single_root";
    if (single && roots.size() == 1) {
        const int r = roots[0];
        bool has = false;
        nodeLocal(jn[at(r)], has);
        single = !has && !jn[at(r)].has("mesh") && boneGroup[at(r)] < 0;
    } else {
        single = false;
    }
    T.singleRoot = single;
    if (single) {
        const int r = roots[0];
        seen[at(r)] = 1;
        T.nodes[0].gltfNode = r;
        for (const int c : kids[at(r)]) gen(c, 0, 0);
    } else {
        for (const int r : roots) gen(r, 0, 0);
    }

    // A skinned mesh is moved below its Skeleton3D, after the bone attachments, with its own transform dropped.
    for (int n = 0; n < N; ++n) {
        if (treeOf[at(n)] < 0 || !jn[at(n)].has("mesh") || !jn[at(n)].has("skin")) continue;
        const int s = static_cast<int>(jn[at(n)]["skin"].asInt(-1));
        if (s < 0 || s >= S) continue;
        const auto it = skelNode.find(find(s));
        if (it == skelNode.end()) continue;
        const int t = treeOf[at(n)], sk = it->second;
        bool below = false;   // the skeleton must not sit below the mesh being moved
        for (int a = sk, guard = 0; a >= 0 && guard <= static_cast<int>(T.nodes.size()); a = T.nodes[at(a)].parent, ++guard) below = below || a == t;
        if (below) continue;
        std::vector<int>& old = T.nodes[at(T.nodes[at(t)].parent)].children;
        old.erase(std::remove(old.begin(), old.end(), t), old.end());
        T.nodes[at(sk)].children.push_back(t);
        T.nodes[at(t)].parent = sk;
        T.nodes[at(t)].skinned = true;
        T.nodes[at(t)].xf = Xform{};
    }

    if (doc["animations"].size() > 0) add(0, siblingName(0, "AnimationPlayer"), "AnimationPlayer");

    // ---- the file's animations: each channel's keys, read from the buffers, as tracks on the nodes and bones of this tree ----
    // The values are the glTF's own (metres, Godot's axes), so a bone's pose key is exactly what Godot's importer puts in the track.
    // LINEAR keeps the file's keys, STEP is a nearest-key track, CUBICSPLINE is sampled at 30 frames per second (Godot bakes it too, so the in-between values differ a little).
    const JsonValue& anims = doc["animations"];
    const std::function<std::string(int)> pathOf = [&](int t) {
        std::string p;
        for (int a = t; a > 0; a = T.nodes[at(a)].parent) p = p.empty() ? T.nodes[at(a)].name : T.nodes[at(a)].name + "/" + p;
        return p;
    };
    size_t weightChannels = 0, unreadChannels = 0;
    for (usize ai = 0; ai < anims.size(); ++ai) {
        const JsonValue& an = anims[ai];
        ModelAnim ma;
        ma.name = an.has("name") ? validNodeName(std::string(an["name"].asString())) : "animation_" + std::to_string(ai);
        if (ma.name.empty()) ma.name = "animation_" + std::to_string(ai);
        const JsonValue& samplers = an["samplers"];
        const JsonValue& chans = an["channels"];
        for (usize k = 0; k < chans.size(); ++k) {
            const JsonValue& ch = chans[k];
            const int n = static_cast<int>(ch["target"]["node"].asInt(-1));
            const std::string_view pn = ch["target"]["path"].asString();
            if (n < 0 || n >= N) continue;
            int type = 0;
            if (pn == "translation") type = 1;
            else if (pn == "rotation") type = 2;
            else if (pn == "scale") type = 3;
            else { ++weightChannels; continue; }
            const int comps = type == 2 ? 4 : 3;
            const int si = static_cast<int>(ch["sampler"].asInt(-1));
            if (si < 0 || static_cast<usize>(si) >= samplers.size()) { ++unreadChannels; continue; }
            const JsonValue& smp = samplers[static_cast<usize>(si)];
            const std::string_view interp = smp["interpolation"].asString("LINEAR");
            const bool cubic = interp == "CUBICSPLINE";
            std::vector<double> times, vals;
            const int outAcc = static_cast<int>(smp["output"].asInt(-1));
            const bool norm = outAcc >= 0 && doc["accessors"][static_cast<usize>(outAcc)]["normalized"].asBool(false);
            if (!gb.read(static_cast<int>(smp["input"].asInt(-1)), 1, times) || !gb.read(outAcc, comps, vals, norm)) { ++unreadChannels; continue; }
            const size_t K = times.size();
            if (K == 0 || vals.size() != K * static_cast<size_t>(comps) * (cubic ? 3 : 1)) { ++unreadChannels; continue; }

            ModelAnimTrack tr;
            if (boneIndex[at(n)] >= 0 && boneGroup[at(n)] >= 0 && skelNode.count(boneGroup[at(n)])) tr.path = pathOf(skelNode[boneGroup[at(n)]]) + ":" + bn[at(n)];
            else if (treeOf[at(n)] >= 0) tr.path = pathOf(treeOf[at(n)]);
            else continue;
            tr.type = type;
            tr.step = interp == "STEP";
            const auto unit = [&](double* q) {   // a rotation key stays a unit quaternion
                const double l = std::sqrt(q[0] * q[0] + q[1] * q[1] + q[2] * q[2] + q[3] * q[3]);
                if (l > 1e-12) for (int c = 0; c < 4; ++c) q[c] /= l;
            };
            if (!cubic) {
                tr.times = times;
                tr.values = vals;
                if (type == 2) for (size_t i = 0; i < K; ++i) unit(&tr.values[i * 4]);
            } else {
                // Key i holds [in-tangent, value, out-tangent]; the segment i -> i+1 is the cubic Hermite of the two values with those tangents.
                const size_t stride = static_cast<size_t>(comps) * 3;
                const auto value = [&](size_t i, int c) { return vals[i * stride + static_cast<size_t>(comps) + static_cast<size_t>(c)]; };
                const auto outT = [&](size_t i, int c) { return vals[i * stride + static_cast<size_t>(comps) * 2 + static_cast<size_t>(c)]; };
                const auto inT = [&](size_t i, int c) { return vals[i * stride + static_cast<size_t>(c)]; };
                const double t0 = times.front(), t1 = times.back();
                std::vector<double> sample;
                for (double t = t0; t < t1 - 1e-9; t += 1.0 / 30.0) sample.push_back(t);
                sample.push_back(t1);
                size_t seg = 0;
                for (const double t : sample) {
                    while (seg + 2 < K && t >= times[seg + 1]) ++seg;
                    double q[4] = {0, 0, 0, 0};
                    if (K == 1) {
                        for (int c = 0; c < comps; ++c) q[c] = value(0, c);
                    } else {
                        const double dt = times[seg + 1] - times[seg];
                        const double u = dt > 1e-12 ? std::clamp((t - times[seg]) / dt, 0.0, 1.0) : 0.0;
                        const double u2 = u * u, u3 = u2 * u;
                        const double h00 = 2 * u3 - 3 * u2 + 1, h10 = u3 - 2 * u2 + u, h01 = -2 * u3 + 3 * u2, h11 = u3 - u2;
                        for (int c = 0; c < comps; ++c)
                            q[c] = h00 * value(seg, c) + h10 * dt * outT(seg, c) + h01 * value(seg + 1, c) + h11 * dt * inT(seg + 1, c);
                    }
                    if (type == 2) unit(q);
                    tr.times.push_back(t);
                    tr.values.insert(tr.values.end(), q, q + comps);
                }
            }
            ma.length = std::max(ma.length, tr.times.back());
            ma.tracks.push_back(std::move(tr));
        }
        T.anims.push_back(std::move(ma));
    }
    if (weightChannels > 0)
        T.notes.push_back("the file animates " + std::to_string(weightChannels) + " morph-weight or other channel(s) (glTF 'weights' or pointer targets); the animation player does not get them");
    if (unreadChannels > 0)
        T.notes.push_back(std::to_string(unreadChannels) + " animation channel(s) could not be read from the file's buffers and are left out");

    if (foreignNode) T.notes.push_back("a glTF camera or light node is kept as a plain Node3D; Godot would make a Camera3D or a light node");
    if (physicsUsed)
        T.notes.push_back(std::string("the model's glTF physics (OMI_physics_body / OMI_physics_shape) is made into bodies (a node with a motion) and CollisionShape3D nodes (a node with a collider), as Godot's importer does: box, sphere, capsule, cylinder, "
                                      "convex and trimesh shapes, in the file's metres") +
                          (physicsUnread ? "; a trigger, a body that is also a mesh, or a shape that could not be read is left out" : ""));
    for (usize k = 0; k < ext.size(); ++k)
        if (ext[k].asString() == "KHR_physics_rigid_bodies")
            T.notes.push_back("the model uses KHR_physics_rigid_bodies: Godot's importer makes bodies and shapes from it; this converter does not, so those nodes stay plain Node3D and the model has no collision here");
    if (!groupJoints.empty())
        T.notes.push_back("a skeleton's bones are written as Skeleton3D bone properties (name, parent, rest pose); the skinned meshes below it follow its pose where the importer's skeleton matches (see the Skeleton3D note)");
    return true;
}

}  // namespace aver::fabric
