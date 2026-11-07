// What makes an instanced glTF model move: its skeleton bound to the engine's, its skinned meshes marked, and the file's animations
// written as the clips of its AnimationPlayer (docs/plans/G5_PARITY.md A2, wave 7 F13 and F14).
//
// The model tree (FabricModelTree.cpp) gives the nodes Godot's importer makes. The engine's importer wrote the geometry, the skinning weights
// and an .ocskel beside it. Binding connects the two: the Skeleton3D node gets the .ocskel (by bone name, since the importer sorts bones
// parents-first and leaves out the nodes between joints), and the Skeleton3D / MeshInstance3D classes of Aver.Fabric put the engine's skinning
// to work at run time. The clips are the file's channels read from its buffers (not the importer's engine-space clips), so a bone's key is
// the value Godot's own importer stores.
#include "FabricInternal.hpp"
#include "FabricModelTree.hpp"

#include "aver/formats/AxisMap.hpp"
#include "aver/formats/OcAnim.hpp"
#include "aver/formats/OcMesh.hpp"
#include "aver/platform/FileSystem.hpp"

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <filesystem>
#include <functional>
#include <map>
#include <set>

namespace aver::fabric {

namespace {

void collectSkeletons(FNode& n, std::vector<FNode*>& out) {
    if (n.fromModel && n.type == "Skeleton3D") out.push_back(&n);
    for (auto& c : n.children) collectSkeletons(*c, out);
}

std::vector<std::string> boneNames(const FNode& sk) {
    std::vector<std::string> names;
    for (const Prop& p : sk.props) {
        if (p.key.rfind("bones/", 0) != 0 || p.key.size() < 5 || p.key.compare(p.key.size() - 5, 5, "/name") != 0) continue;
        names.push_back(p.value.asString());
    }
    return names;
}

// A track's value at time t: linear (rotations slerped) between the keys either side, the nearest earlier key for a step track,
// and the first or last key outside them.
fmt::OcPropValue sampleTrack(const fmt::OcPropTrack& t, double time) {
    const auto& k = t.keys;
    if (time <= k.front().time) return k.front().value;
    if (time >= k.back().time) return k.back().value;
    size_t i = 1;
    while (i < k.size() && k[i].time < time) ++i;
    const fmt::OcPropKey& a = k[i - 1];
    const fmt::OcPropKey& b = k[i];
    if (t.interp == fmt::OcPropInterp::Nearest || b.time <= a.time) return a.value;
    const double f = (time - a.time) / (b.time - a.time);
    fmt::OcPropValue v = a.value;
    if (a.value.kind == fmt::OcPropKind::Quat) {
        double d = 0;
        for (int c = 0; c < 4; ++c) d += a.value.n[c] * b.value.n[c];
        const double sign = d < 0 ? -1.0 : 1.0;
        d = std::min(1.0, std::abs(d));
        double wa = 1.0 - f, wb = f;
        if (d < 0.9995) {
            const double th = std::acos(d), st = std::sin(th);
            wa = std::sin((1.0 - f) * th) / st;
            wb = std::sin(f * th) / st;
        }
        double len = 0;
        for (int c = 0; c < 4; ++c) { v.n[c] = wa * a.value.n[c] + wb * sign * b.value.n[c]; len += v.n[c] * v.n[c]; }
        len = std::sqrt(len);
        if (len > 0) for (int c = 0; c < 4; ++c) v.n[c] /= len;
    } else {
        for (int c = 0; c < 4; ++c) v.n[c] = a.value.n[c] + (b.value.n[c] - a.value.n[c]) * f;
    }
    return v;
}

// Godot's import slice (ResourceImporterScene::_create_slices with bake_all): each track's keys in [from, to) moved to start at 0, a key
// at 0 when the first one in range comes later and an earlier key exists, a key at the end when the track goes on past it, and a track
// with no key in range given its values at both ends.
fmt::OcAnimation sliceClip(const fmt::OcAnimation& whole, double from, double to, fmt::OcPropLoop loop) {
    fmt::OcAnimation c;
    c.propClip = true;
    c.duration = static_cast<float>(to - from);
    c.propLoop = loop;
    c.propStep = whole.propStep;
    for (const fmt::OcPropTrack& t : whole.propTracks) {
        if (t.keys.empty()) continue;
        fmt::OcPropTrack nt = t;
        nt.keys.clear();
        auto at = [&](double time, double when) {
            fmt::OcPropKey key;
            key.time = when;
            key.transition = 1.0;
            key.value = sampleTrack(t, time);
            nt.keys.push_back(std::move(key));
        };
        bool any = false;
        for (size_t k = 0; k < t.keys.size(); ++k) {
            const double kt = t.keys[k].time;
            if (kt >= from && kt < to) {
                if (!any && kt > from + 0.01 && k > 0) at(from, 0.0);
                any = true;
                fmt::OcPropKey key = t.keys[k];
                key.time = kt - from;
                nt.keys.push_back(std::move(key));
            }
            if (any && kt >= to) { at(to, to - from); break; }
        }
        if (!any) { at(from, 0.0); at(to, to - from); }
        c.propTracks.push_back(std::move(nt));
    }
    return c;
}

fmt::OcPropLoop loopOf(int mode) { return mode == 1 ? fmt::OcPropLoop::Linear : mode == 2 ? fmt::OcPropLoop::PingPong : fmt::OcPropLoop::None; }

}  // namespace

void Converter::bindModelRuntime(FNode& root, const ModelTree* tree, const ModelResult& model, const std::string& modelRes) {
    if (!tree) return;
    const auto note = [&](const std::string& node, const std::string& type, const std::string& what, Rating r, const std::string& why) {
        report_.add("resource", modelRes, node, type, what, r, why, 0);
    };

    // ---- skeletons: the importer's .ocskel for each Skeleton3D, matched by bone names ----
    std::vector<FNode*> skeletons;
    collectSkeletons(root, skeletons);
    std::vector<fmt::OcSkeleton> engine;
    for (const std::string& path : model.skeletons) {
        fmt::OcSkeleton sk;
        std::string why;
        if (!fmt::loadOcSkel(contentDir_ + "/" + path, sk, &why)) { engine.emplace_back(); continue; }
        engine.push_back(std::move(sk));
    }
    for (FNode* sk : skeletons) {
        const std::vector<std::string> names = boneNames(*sk);
        const std::set<std::string> have(names.begin(), names.end());
        int best = -1;
        size_t bestCount = 0;
        for (size_t i = 0; i < engine.size(); ++i) {
            const size_t count = engine[i].bones.size();
            if (count == 0 || count <= bestCount) continue;
            bool all = true;
            for (const fmt::OcBone& b : engine[i].bones) all = all && have.count(b.name) > 0;
            if (all) { best = static_cast<int>(i); bestCount = count; }
        }
        if (best < 0) {
            note(sk->reportPath, "Skeleton3D", "engine skeleton", Rating::Approximated,
                 "no skeleton the importer wrote has all its bones in this one's; the skinned meshes below it stay in their bind pose");
            continue;
        }
        sk->props.push_back(Prop{"aver_skeleton", makeString(model.skeletons[static_cast<size_t>(best)]), nullptr, 0});
        for (auto& c : sk->children)
            if (c->fromModel && c->type == "MeshInstance3D") c->props.push_back(Prop{"aver_skinned", makeBool(true), nullptr, 0});
        note(sk->reportPath, "Skeleton3D", "engine skeleton", Rating::Converted,
             "bound to " + model.skeletons[static_cast<size_t>(best)] + " (" + std::to_string(bestCount) + " of " + std::to_string(names.size()) +
                 " bones are skinned): the meshes below it are skinned by the pose that scripts and bone tracks give this node");
    }

    // ---- blend shapes: the names of each mesh's morph targets, so MeshInstance3D can answer get_blend_shape_count and find_blend_shape_by_name ----
    std::function<void(FNode&)> shapes = [&](FNode& n) {
        if (n.fromModel && n.type == "MeshInstance3D") {
            for (const Prop& p : n.props) {
                if (p.key != "mesh" || p.value.type != VType::Resource) continue;
                fmt::OcMeshData m;
                std::string err;
                if (!fmt::loadOcMesh(contentDir_ + "/" + p.value.s, m, &err) || m.morphTargets.empty()) break;
                std::string joined;
                for (const fmt::OcMorphTarget& t : m.morphTargets) joined += (joined.empty() ? "" : "|") + t.name;
                n.props.push_back(Prop{"aver_blend_shapes", makeString(joined), nullptr, 0});
                break;
            }
        }
        for (auto& c : n.children) shapes(*c);
    };
    shapes(root);

    // ---- animations: the file's channels as the clips of the model's AnimationPlayer ----
    if (tree->anims.empty()) return;
    FNode* player = nullptr;
    for (auto& c : root.children)
        if (c->fromModel && c->type == "AnimationPlayer") { player = c.get(); break; }
    if (!player) return;

    const std::string base = modelFileBase(modelRes);
    createDirectories(contentDir_ + "/Animations");

    // The model's import settings: its frame rate and, per glTF animation (_subresources > animations > <name>), a loop mode and the slices
    // Godot cuts it into.
    gd::GodotImport imp;
    gd::GodotDiagnostics impDiag;
    const bool haveImport = fs_.loadImport(modelRes, imp, impDiag);
    const double fps = haveImport ? std::max(1.0, numOf(imp.param("animation/fps"), 30.0)) : 30.0;
    const Var* subres = haveImport ? imp.param("_subresources") : nullptr;
    const Var* animSettings = subres && subres->type == VType::Dictionary ? subres->find("animations") : nullptr;
    auto writeClip = [&](const std::string& name, const fmt::OcAnimation& clip) -> std::string {
        const std::string stem = safeName(base + "_" + name, "animation");
        std::string unique = stem;
        for (int n = 2; animationStemsUsed_.count(unique); ++n) unique = stem + "_" + std::to_string(n);
        animationStemsUsed_.insert(unique);
        const std::string asset = "Animations/" + unique + ".ocanim";
        std::string err;
        if (!fmt::saveOcAnim(contentDir_ + "/" + asset, clip, &err)) {
            note(player->reportPath, "AnimationPlayer", name, Rating::Unsupported, "the .ocanim could not be written: " + err);
            return "";
        }
        return asset;
    };

    std::vector<std::pair<std::string, Var>> entries;
    size_t tracksWritten = 0;
    for (const ModelAnim& a : tree->anims) {
        fmt::OcAnimation clip;
        clip.propClip = true;
        clip.duration = static_cast<float>(a.length);
        for (const ModelAnimTrack& t : a.tracks) {
            fmt::OcPropTrack pt;
            pt.type = t.type == 1 ? fmt::OcPropTrackType::Position3D : t.type == 2 ? fmt::OcPropTrackType::Rotation3D : fmt::OcPropTrackType::Scale3D;
            pt.path = t.path;
            pt.interp = t.step ? fmt::OcPropInterp::Nearest : fmt::OcPropInterp::Linear;
            pt.imported = true;
            const size_t comps = t.type == 2 ? 4 : 3;
            for (size_t k = 0; k < t.times.size(); ++k) {
                fmt::OcPropKey key;
                key.time = t.times[k];
                key.transition = 1.0;
                const double* v = &t.values[k * comps];
                if (t.type == 1) {
                    const Vec3 p = fmt::axis::position(static_cast<f32>(v[0] * model.rootScale), static_cast<f32>(v[1] * model.rootScale), static_cast<f32>(v[2] * model.rootScale));
                    key.value.kind = fmt::OcPropKind::Vec3; key.value.n[0] = p.x; key.value.n[1] = p.y; key.value.n[2] = p.z;
                } else if (t.type == 3) {
                    const Vec3 s = fmt::axis::scale(static_cast<f32>(v[0]), static_cast<f32>(v[1]), static_cast<f32>(v[2]));
                    key.value.kind = fmt::OcPropKind::Vec3; key.value.n[0] = s.x; key.value.n[1] = s.y; key.value.n[2] = s.z;
                } else {
                    const Quat q = fmt::axis::rotation(static_cast<f32>(v[0]), static_cast<f32>(v[1]), static_cast<f32>(v[2]), static_cast<f32>(v[3]));
                    key.value.kind = fmt::OcPropKind::Quat; key.value.n[0] = q.x; key.value.n[1] = q.y; key.value.n[2] = q.z; key.value.n[3] = q.w;
                }
                pt.keys.push_back(std::move(key));
            }
            clip.propTracks.push_back(std::move(pt));
        }
        if (clip.propTracks.empty()) continue;
        tracksWritten += clip.propTracks.size();
        const Var* cfg = animSettings && animSettings->type == VType::Dictionary ? animSettings->find(a.name) : nullptr;
        if (cfg && cfg->type != VType::Dictionary) cfg = nullptr;
        if (cfg) clip.propLoop = loopOf(static_cast<int>(numOf(cfg->find("settings/loop_mode"), 0)));
        const int slices = cfg ? static_cast<int>(numOf(cfg->find("slices/amount"), 0)) : 0;
        if (slices <= 0) {
            const std::string asset = writeClip(a.name, clip);
            if (!asset.empty()) entries.emplace_back(a.name, makeResource(asset));
            continue;
        }
        // Sliced, the clips are the slices and the whole animation is dropped, as Godot's importer does. A slice saved to a file is that
        // file's clip: Godot loads the saved animation back, with the tracks its author added (sounds, method calls) when it keeps custom tracks.
        size_t made = 0, fromFile = 0;
        for (int i = 1; i <= slices; ++i) {
            const std::string key = "slice_" + std::to_string(i) + "/";
            const std::string name = stringOf(cfg->find(key + "name"));
            const double from = numOf(cfg->find(key + "start_frame"), 0) / fps;
            const double to = numOf(cfg->find(key + "end_frame"), 0) / fps;
            if (name.empty() || from >= to) continue;
            std::string asset;
            const std::string saved = boolOf(cfg->find(key + "save_to_file/enabled"), false) ? stringOf(cfg->find(key + "save_to_file/path")) : std::string{};
            if (!saved.empty()) {
                const std::string res = gd::GodotProjectFs::isUid(saved) ? fs_.resolve(saved, "") : gd::GodotProjectFs::normalizeRes(saved);
                const gd::GodotResource* r = res.empty() || !fs_.exists(res) ? nullptr : resourceDoc(res);
                if (r && r->header.type == "Animation") {
                    asset = animationAsset(res, r->props, *r, stemOf(res), Where{res, "", "Animation"});
                    if (!asset.empty()) ++fromFile;
                }
            }
            if (asset.empty()) asset = writeClip(name, sliceClip(clip, from, to, loopOf(static_cast<int>(numOf(cfg->find(key + "loop_mode"), 0)))));
            if (asset.empty()) continue;
            entries.emplace_back(name, makeResource(asset));
            ++made;
        }
        note(player->reportPath, "AnimationPlayer", a.name, Rating::Converted,
             "sliced by the import settings into " + std::to_string(made) + " clip(s) at " + std::to_string(static_cast<int>(fps)) + " frames per second" +
                 (fromFile ? ", " + std::to_string(fromFile) + " of them the animation file the slice is saved to" : std::string{}));
    }
    if (entries.empty()) return;
    const size_t clipCount = entries.size();
    std::vector<std::pair<std::string, Var>> libs;
    libs.emplace_back("", makeDict(std::move(entries)));
    player->props.push_back(Prop{"libraries", makeDict(std::move(libs)), nullptr, 0});
    note(player->reportPath, "AnimationPlayer", "animations", Rating::Approximated,
         std::to_string(clipCount) + " glTF animation(s), " + std::to_string(tracksWritten) +
             " track(s) read from the file's buffers: the file's own keys for linear and step channels, cubic-spline channels sampled at 30 frames per second; Godot's importer"
             " resamples and optimises its tracks, so in-between values and key counts can differ");
}

// ---- the ragdoll the scene authors (wave 7, F12 and P24) ----
//
// PhysicalBone3D nodes below a bound Skeleton3D say which bones are ragdoll parts and how each behaves. The engine's ragdoll builder takes that
// from a text file beside the skeleton (<skeleton>.ocragdoll, RagdollBuilder.hpp), so the converter writes one: `off` for every bone no node names,
// and for the named ones the capsule radius, mass, friction, bounce, the joint kind and its limits. One file per skeleton: a second scene that
// authors a different ragdoll for the same imported skeleton replaces it (the last converted wins).

namespace {

const Var* propOf(const FNode& n, const char* key) {
    for (const Prop& p : n.props)
        if (p.key == key) return &p.value;
    return nullptr;
}

void collectPhysicalBones(FNode& n, std::vector<FNode*>& out) {
    if (n.type == "PhysicalBone3D") out.push_back(&n);
    for (auto& c : n.children) collectPhysicalBones(*c, out);
}

// The radius of the shape a PhysicalBone3D's collision child holds, in metres, and its height (0 for a sphere); false when it has none the engine can use.
bool shapeSize(const FNode& pb, double& radius, double& height) {
    for (const auto& c : pb.children) {
        if (c->type != "CollisionShape3D") continue;
        const Prop* sp = nullptr;
        for (const Prop& p : c->props)
            if (p.key == "shape") sp = &p;
        if (!sp) continue;
        // The scene states the shape as a sub-resource (read here from its document) or as a value already converted.
        std::string cls;
        std::map<std::string, const Var*> v;
        if (sp->value.type == VType::SubResource && sp->doc) {
            const gd::GodotSubResource* sr = sp->doc->findSub(sp->value.s);
            if (!sr) continue;
            cls = sr->type;
            for (const gd::GodotProperty& gp : sr->props) v[gp.name] = &gp.value;
        } else if (sp->value.type == VType::Object) {
            cls = sp->value.s;
            for (size_t i = 0; i < sp->value.keys.size() && i < sp->value.items.size(); ++i) v[sp->value.keys[i].asString()] = &sp->value.items[i];
        } else {
            continue;
        }
        const auto get = [&](const char* k) -> const Var* {
            const auto it = v.find(k);
            return it == v.end() ? nullptr : it->second;
        };
        if (cls == "CapsuleShape3D" || cls == "CylinderShape3D") {
            radius = numOf(get("radius"), 0.5);
            height = numOf(get("height"), 2.0);
            return true;
        }
        if (cls == "SphereShape3D") {
            radius = numOf(get("radius"), 0.5);
            height = 0.0;
            return true;
        }
        if (cls == "BoxShape3D") {
            const Var* size = get("size");
            if (size && size->nums.size() >= 3) {
                radius = 0.5 * std::min(size->nums[0], size->nums[2]);
                height = size->nums[1];
                return true;
            }
        }
    }
    return false;
}

std::string num(double v) {
    char b[48];
    std::snprintf(b, sizeof b, "%.4f", v);
    return b;
}

}  // namespace

void Converter::writeRagdollAuthoring(FNode& root, const std::string& sceneRes) {
    std::vector<FNode*> skeletons;
    collectSkeletons(root, skeletons);
    for (FNode* sk : skeletons) {
        std::string asset;
        for (const Prop& p : sk->props)
            if (p.key == "aver_skeleton") asset = p.value.asString();
        if (asset.empty()) continue;
        std::vector<FNode*> bones;
        collectPhysicalBones(*sk, bones);
        if (bones.empty()) continue;

        fmt::OcSkeleton eng;
        std::string why;
        if (!fmt::loadOcSkel(contentDir_ + "/" + asset, eng, &why)) continue;
        std::map<std::string, FNode*> byBone;
        for (FNode* pb : bones) {
            const std::string name = stringOf(propOf(*pb, "bone_name"));
            if (name.empty() || name.find(' ') != std::string::npos || byBone.count(name)) {
                report_.add("node", pb->reportFile, pb->reportPath, "PhysicalBone3D", "bone_name", Rating::Approximated,
                            "a physical bone with no bone name, a name with a space, or a second one for the same bone is left out of the ragdoll file", 0);
                continue;
            }
            byBone[name] = pb;
        }

        std::string text = "# Written by the Fabric converter from the PhysicalBone3D nodes of " + sceneRes + "; the bones no node names are not parts.\n";
        size_t parts = 0;
        for (const fmt::OcBone& b : eng.bones) {
            if (b.name.find(' ') != std::string::npos) continue;
            const auto it = byBone.find(b.name);
            if (it == byBone.end()) { text += "bone " + b.name + " off\n"; continue; }
            const FNode& pb = *it->second;
            ++parts;
            std::string line = "bone " + b.name + " on";
            double radius = 0, height = 0;
            const bool hasShape = shapeSize(pb, radius, height);
            if (hasShape && radius > 0) line += " radius " + num(radius * 100.0);
            bool leaf = true;
            for (const fmt::OcBone& o : eng.bones) leaf = leaf && o.parent != static_cast<i32>(&b - eng.bones.data());
            if (leaf) line += " length " + num(std::max(hasShape ? height * 100.0 : 0.0, 5.0));   // a leaf needs a segment of its own
            if (hasShape && height <= 0.0) line += " shape sphere";
            line += " mass " + num(std::max(numOf(propOf(pb, "mass"), 1.0), 1e-3));
            line += " friction " + num(std::max(numOf(propOf(pb, "friction"), 1.0), 0.0));
            line += " bounce " + num(std::max(numOf(propOf(pb, "bounce"), 0.0), 0.0));
            const int jt = static_cast<int>(numOf(propOf(pb, "joint_type"), 0));
            double swing = numOf(propOf(pb, "joint_constraints/swing_span"), 45.0), twist = numOf(propOf(pb, "joint_constraints/twist_span"), 45.0);
            if (jt == 2) {   // cone
                line += " joint swingtwist limits " + num(swing) + " " + num(swing) + " " + num(twist);
            } else if (jt == 3) {   // hinge: radians in the scene
                double lim = 180.0;
                if (boolOf(propOf(pb, "joint_constraints/angular_limit_enabled"), false))
                    lim = std::max(std::fabs(numOf(propOf(pb, "joint_constraints/angular_limit_lower"), 0.0)), std::fabs(numOf(propOf(pb, "joint_constraints/angular_limit_upper"), 0.0))) * 180.0 / 3.14159265358979;
                line += " joint hinge limits " + num(std::min(lim, 180.0)) + " 0 0";
            } else if (jt == 4) {   // slider: no sliding joint in the ragdoll; held rigid
                line += " joint fixed";
            } else {   // none, pin and 6DOF: free to turn
                line += " joint ball";
            }
            text += line + "\n";
        }
        std::string path = asset;
        if (const size_t dot = path.find_last_of('.'); dot != std::string::npos) path.erase(dot);
        path += ".ocragdoll";
        if (!writeFileText(contentDir_ + "/" + path, text)) {
            report_.add("node", sk->reportFile, sk->reportPath, "PhysicalBoneSimulator3D", "", Rating::Unsupported, "the ragdoll file " + path + " could not be written", 0);
            continue;
        }
        report_.add("node", sk->reportFile, sk->reportPath, "PhysicalBoneSimulator3D", "", Rating::Approximated,
                    std::to_string(parts) + " physical bone(s) written to " + path + " for the engine's ragdoll (mass, friction, bounce, capsule radius, joint kind and limits; "
                    "joint and body offsets, slider and 6DOF limits and the collision shape's own placement are not kept)",
                    0);
    }
}

}  // namespace aver::fabric
