// Wave 6 (docs/plans/WAVE6_PARTICLES.md): GPUParticles3D with a ParticleProcessMaterial and CPUParticles3D become a ParticleEmitter component and the .ocparticle it plays,
// F27 to F30, P15 and P16. A GPUParticles3D whose process material is a particles shader keeps its own path (FabricShader.cpp, emitGpuParticles).
//
// The engine takes neutral capabilities and the numbers are this file's: Godot's range pairs become the min/max pairs the engine draws per particle, its curves become
// four-stop curves (the shape between stops and the tangents are lost), its units become centimetres and its axes the engine's (GodotParticles.hpp). Every property is
// reported with the rating it got; what the engine cannot do is said, not dropped.
#include "FabricInternal.hpp"

#include "aver/core/Hash.hpp"
#include "aver/formats/godot/GodotParticles.hpp"
#include "aver/platform/FileSystem.hpp"

#include <algorithm>
#include <cmath>
#include <cstdio>

namespace aver::fabric {

namespace gp = aver::fmt::godotparticles;

namespace {

const Var* findIn(const std::vector<gd::GodotProperty>& props, const char* name) {
    for (const gd::GodotProperty& p : props)
        if (p.name == name) return &p.value;
    return nullptr;
}

// A resource value (sub-resource or external .tres) as its class and properties.
struct Res {
    std::string type;
    const std::vector<gd::GodotProperty>* props = nullptr;
    const gd::GodotDocBase* doc = nullptr;
    const Var* get(const char* k) const { return props ? findIn(*props, k) : nullptr; }
};

bool resolve(Converter& c, const Var* v, const gd::GodotDocBase* doc, Res& out) {
    if (!v || !doc) return false;
    if (v->type == VType::SubResource) {
        const gd::GodotSubResource* sr = doc->findSub(v->s);
        if (!sr) return false;
        out = {sr->type, &sr->props, doc};
        return true;
    }
    if (v->type == VType::ExtResource) {
        const gd::GodotExtResource* e = doc->findExt(v->s);
        if (!e) return false;
        const std::string res = c.fs_.resolve(*e, &c.diag_, doc->file);
        if (res.empty()) return false;
        const gd::GodotResource* r = c.resourceDoc(res);
        if (!r) return false;
        out = {r->header.type, &r->props, r};
        return true;
    }
    return false;
}

double srgbToLinear(double c) {
    c = std::max(0.0, std::min(1.0, c));
    return c <= 0.04045 ? c / 12.92 : std::pow((c + 0.055) / 1.055, 2.4);
}

// The curve points of a Curve resource, x and y as stored (Godot 4's _data is five values per point: the position, two tangents, two modes).
bool curvePoints(const Res& curve, std::vector<gp::CurvePoint>& out, double& minV, double& maxV, double& minD, double& maxD) {
    out.clear();
    const Var* d = curve.get("_data");
    if (!d || d->type != VType::Array) return false;
    for (size_t i = 0; i < d->items.size(); i += 5) out.push_back({d->items[i].num(0), d->items[i].num(1)});
    minV = numOf(curve.get("min_value"), 0.0);
    maxV = numOf(curve.get("max_value"), 1.0);
    minD = numOf(curve.get("min_domain"), 0.0);
    maxD = numOf(curve.get("max_domain"), 1.0);
    return !out.empty();
}

// The multiplier curve a property holds: a CurveTexture (value and time normalised to 0..1, as the texture bakes it) or a Curve (as authored). Empty when none.
std::vector<gp::CurvePoint> curveOf(Converter& c, const Var* v, const gd::GodotDocBase* doc) {
    Res r;
    if (!resolve(c, v, doc, r)) return {};
    bool texture = false;
    if (r.type == "CurveTexture") {
        Res inner;
        if (!resolve(c, r.get("curve"), r.doc, inner)) return {};
        r = inner;
        texture = true;
    }
    if (r.type != "Curve") return {};
    std::vector<gp::CurvePoint> pts;
    double minV, maxV, minD, maxD;
    if (!curvePoints(r, pts, minV, maxV, minD, maxD)) return {};
    if (texture) {
        const double dv = maxV - minV, dd = maxD - minD;
        for (gp::CurvePoint& p : pts) {
            p.v = dv > 1e-12 ? (p.v - minV) / dv : p.v;
            p.t = dd > 1e-12 ? (p.t - minD) / dd : p.t;
        }
    }
    std::sort(pts.begin(), pts.end(), [](const gp::CurvePoint& a, const gp::CurvePoint& b) { return a.t < b.t; });
    return pts;
}

// A gradient: a GradientTexture1D (its gradient) or a Gradient.
std::vector<gp::GradientStop> gradientOf(Converter& c, const Var* v, const gd::GodotDocBase* doc) {
    Res r;
    if (!resolve(c, v, doc, r)) return {};
    if (r.type == "GradientTexture1D") {
        Res inner;
        if (!resolve(c, r.get("gradient"), r.doc, inner)) return {};
        r = inner;
    }
    if (r.type != "Gradient") return {};
    const Var* offs = r.get("offsets");
    const Var* cols = r.get("colors");
    std::vector<gp::GradientStop> out;
    if (!offs || !cols || offs->nums.empty() || cols->nums.size() < offs->nums.size() * 4) return out;
    for (size_t i = 0; i < offs->nums.size(); ++i) {
        gp::GradientStop s;
        s.t = offs->nums[i];
        for (int k = 0; k < 4; ++k) s.rgba[k] = cols->nums[i * 4 + static_cast<size_t>(k)];
        out.push_back(s);
    }
    std::sort(out.begin(), out.end(), [](const gp::GradientStop& a, const gp::GradientStop& b) { return a.t < b.t; });
    return out;
}

gp::Vec3 vec3Prop(const Var* v, const gp::Vec3& def) {
    if (!v || v->type != VType::Vector3 || v->nums.size() < 3) return def;
    return {v->nums[0], v->nums[1], v->nums[2]};
}

// A node of the scene tree below `root` with its global Godot transform.
struct Found {
    const FNode* node = nullptr;
    Xform global;
};

void collect(const FNode& n, const Xform& parent, bool top, std::vector<Found>& out) {
    Xform g = top ? n.xf : parent.composed(n.xf);
    if (n.type.rfind("GPUParticlesCollision", 0) == 0 || n.type.rfind("GPUParticlesAttractor", 0) == 0) out.push_back({&n, g});
    for (const auto& ch : n.children) collect(*ch, g, false, out);
}

const Prop* nodeProp(const FNode& n, const char* key) {
    for (const Prop& p : n.props)
        if (p.key == key) return &p;
    return nullptr;
}
double nodeNum(const FNode& n, const char* key, double def) { const Prop* p = nodeProp(n, key); return p ? numOf(&p->value, def) : def; }

}  // namespace

void Converter::emitParticles(const FNode& fn, const std::vector<std::pair<std::string, Var>>& props, fmt::OcWorldPlacement& p, bool quiet) {
    const bool cpu = fn.type == "CPUParticles3D";
    // A particles shader keeps the shader path.
    if (!cpu) {
        for (const auto& kv : props)
            if (kv.first == "process_material" && kv.second.type == VType::Resource) { emitGpuParticles(fn, props, p, quiet); return; }
    }
    const auto note = [&](const std::string& key, Rating r, const std::string& why) {
        if (!quiet) report_.add("property", fn.reportFile, fn.reportPath, fn.type, key, r, why, nodeProp(fn, key.c_str()) ? nodeProp(fn, key.c_str())->line : 0);
    };
    const Where w{fn.reportFile, fn.reportPath, fn.type};

    // ---- the property source: the process material of a GPUParticles3D, or the CPUParticles3D node itself ----
    Res mat;
    const gd::GodotDocBase* doc = nullptr;
    if (!cpu) {
        const Prop* pm = nodeProp(fn, "process_material");
        if (!pm || !pm->doc || !resolve(*this, &pm->value, pm->doc, mat) || mat.type != "ParticleProcessMaterial") {
            note("process_material", Rating::Unsupported,
                 pm ? "only a ParticleProcessMaterial or a ShaderMaterial over a particles shader is converted: the node keeps its properties and emits nothing"
                    : "no process material: the node emits nothing");
            return;
        }
        doc = mat.doc;
    } else {
        for (const Prop& pr : fn.props)
            if (pr.doc) { doc = pr.doc; break; }
    }
    // Property lookup with the CPUParticles3D spellings as aliases: scale_amount_* for scale_*, and so on.
    const auto get = [&](const char* name, const char* alias = nullptr) -> const Var* {
        if (!cpu) return mat.get(name);
        if (const Prop* pr = nodeProp(fn, name)) return &pr->value;
        if (alias) if (const Prop* pr = nodeProp(fn, alias)) return &pr->value;
        return nullptr;
    };
    const gd::GodotDocBase* resDoc = cpu ? doc : mat.doc;
    const auto num = [&](const char* name, double def, const char* alias = nullptr) { return numOf(get(name, alias), def); };
    const auto nodeN = [&](const char* name, double def) { return nodeNum(fn, name, def); };
    const auto pm = [&](const std::string& k, Rating r, const std::string& why) {
        // A property of the process material is reported against the node, under the material's own name.
        if (!quiet) report_.add("property", fn.reportFile, fn.reportPath, fn.type, cpu ? k : std::string("process_material/") + k, r, why, 0);
    };
    // A collider or attractor is reported once, on its own node, however many emitters it is baked into.
    const auto nodeRow = [&](const FNode& cn, Rating r, const std::string& why) {
        if (quiet || !particleReported_.insert(cn.reportFile + ":" + cn.reportPath).second) return;
        report_.add("property", cn.reportFile, cn.reportPath, cn.type, "baked", r, why, 0);
    };

    // ---- timing and emission ----
    const int amount = std::max(1, static_cast<int>(nodeN("amount", 8.0)));
    const double lifetime = std::max(0.01, nodeN("lifetime", 1.0));
    const bool oneShot = boolOf(nodeProp(fn, "one_shot") ? &nodeProp(fn, "one_shot")->value : nullptr, false);
    const bool emitting = boolOf(nodeProp(fn, "emitting") ? &nodeProp(fn, "emitting")->value : nullptr, true);
    const double amountRatio = std::max(0.0, std::min(1.0, nodeN("amount_ratio", 1.0)));
    const int live = std::max(1, static_cast<int>(std::lround(amount * (cpu ? 1.0 : amountRatio))));
    const double lifeRand = std::max(0.0, std::min(1.0, num("lifetime_randomness", 0.0, "lifetime_randomness")));
    std::string t = "OCPARTICLE 2\nNAME " + fn.name + "\nEMITTER " + fn.name + "\nSIM auto\n";

    // ---- the draw pass: size, blend, texture, billboard, sprite sheet, shading, trails ----
    double sizeCm = 100.0;
    bool sprite = true, sphereMesh = false, boxMesh = false, trailMesh = false;
    int trailPoints = 12;
    Res drawMesh, drawMat;
    const Prop* dp = nodeProp(fn, cpu ? "mesh" : "draw_pass_1");
    if (dp && dp->doc && resolve(*this, &dp->value, dp->doc, drawMesh)) {
        const std::string& mt = drawMesh.type;
        if (mt == "QuadMesh" || mt == "PlaneMesh") {
            const Var* sz = drawMesh.get("size");
            if (sz && sz->nums.size() >= 2) sizeCm = std::max(sz->nums[0], sz->nums[1]) * 100.0;
            else if (mt == "QuadMesh") sizeCm = 100.0;
            else sizeCm = 200.0;
            if (sz && sz->nums.size() >= 2 && std::fabs(sz->nums[0] - sz->nums[1]) > 1e-6)
                note(cpu ? "mesh" : "draw_pass_1", Rating::Approximated, "the quad is drawn square at its larger side (the sprite's edge is one length)");
        } else if (mt == "SphereMesh") {
            sphereMesh = true; sprite = false;
            sizeCm = numOf(drawMesh.get("radius"), 0.5) * 200.0;
        } else if (mt == "BoxMesh") {
            boxMesh = true; sprite = false;
            const Var* sz = drawMesh.get("size");
            sizeCm = (sz && sz->nums.size() >= 3 ? std::max(sz->nums[0], std::max(sz->nums[1], sz->nums[2])) : 1.0) * 100.0;
        } else if (mt == "RibbonTrailMesh") {
            trailMesh = true;
            sizeCm = numOf(drawMesh.get("size"), 1.0) * 100.0;
            trailPoints = std::min(32, std::max(2, static_cast<int>(numOf(drawMesh.get("sections"), 5.0)) * std::max(1, static_cast<int>(numOf(drawMesh.get("section_segments"), 3.0))) + 1));
        } else if (mt == "TubeTrailMesh") {
            trailMesh = true;
            sizeCm = numOf(drawMesh.get("radius"), 0.5) * 200.0;
            trailPoints = std::min(32, std::max(2, static_cast<int>(numOf(drawMesh.get("sections"), 5.0)) * std::max(1, static_cast<int>(numOf(drawMesh.get("section_rings"), 3.0))) + 1));
        } else {
            note(cpu ? "mesh" : "draw_pass_1", Rating::Approximated, "only a quad, a plane, a sphere, a box and the trail meshes are drawn as particles; " + mt + " is drawn as a camera-facing sprite 1 m across");
        }
        resolve(*this, drawMesh.get("material"), drawMesh.doc, drawMat);
    }
    if (dp) for (int pass = 2; pass <= 4; ++pass) {
        const std::string k = "draw_pass_" + std::to_string(pass);
        if (!cpu && nodeProp(fn, k.c_str())) note(k, Rating::Unsupported, "only the first draw pass is drawn");
    }
    // The draw material (a StandardMaterial3D on the mesh, or the node's own material_override).
    if (drawMat.type.empty()) resolve(*this, nodeProp(fn, "material_override") ? &nodeProp(fn, "material_override")->value : nullptr,
                                      nodeProp(fn, "material_override") ? nodeProp(fn, "material_override")->doc : nullptr, drawMat);
    const bool haveMat = drawMat.type == "StandardMaterial3D" || drawMat.type == "ORMMaterial3D";
    const bool vertexColour = !haveMat || boolOf(drawMat.get("vertex_color_use_as_albedo"), false);
    double albedo[4] = {1, 1, 1, 1};
    if (haveMat) {
        const Var* ac = drawMat.get("albedo_color");
        if (ac && ac->nums.size() >= 4) {
            for (int k = 0; k < 3; ++k) albedo[k] = srgbToLinear(ac->nums[static_cast<size_t>(k)]);
            albedo[3] = ac->nums[3];
        }
    }
    const int blendMode = haveMat ? static_cast<int>(numOf(drawMat.get("blend_mode"), 0.0)) : 0;
    const bool additive = blendMode == 1;
    if (haveMat && (blendMode == 2 || blendMode == 3))
        note(cpu ? "mesh" : "draw_pass_1", Rating::Approximated, std::string(blendMode == 2 ? "subtract" : "multiply") + " blending has no particle blend: drawn with the premultiplied alpha blend");
    t += std::string("BLEND ") + (additive ? "additive" : "premultiplied") + "\n";

    // The shape.
    const int shapeKind = static_cast<int>(num("emission_shape", 0.0));
    const gp::Vec3 shapeScale = vec3Prop(get("emission_shape_scale"), {1, 1, 1});
    if (get("emission_shape_offset")) {
        const gp::Vec3 off = vec3Prop(get("emission_shape_offset"), {0, 0, 0});
        if (std::fabs(off.x) + std::fabs(off.y) + std::fabs(off.z) > 1e-9) pm("emission_shape_offset", Rating::Unsupported, "the emission shape sits on the node's origin (the engine's shapes have no offset)");
    }
    switch (shapeKind) {
        case 1: case 2: {
            const double r = num("emission_sphere_radius", 1.0) * std::fabs(shapeScale.x) * 100.0;
            t += "SHAPE sphere " + gp::num(r) + (shapeKind == 2 ? " shell\n" : "\n");
            pm("emission_shape", Rating::Converted, shapeKind == 2 ? "a sphere surface (a shell)" : "a solid sphere");
            break;
        }
        case 3: {
            const gp::Vec3 e = vec3Prop(get("emission_box_extents"), {1, 1, 1});
            const gp::Vec3 m = gp::extents({e.x * shapeScale.x, e.y * shapeScale.y, e.z * shapeScale.z});
            t += "SHAPE box " + gp::num(m.x * 100.0) + " " + gp::num(m.y * 100.0) + " " + gp::num(m.z * 100.0) + "\n";
            pm("emission_shape", Rating::Converted, "a box of Godot's half-extents");
            break;
        }
        case 4: case 5:
            t += "SHAPE point 0 0 0\n";
            pm("emission_shape", Rating::Unsupported, "emission from a point texture needs a point buffer the engine's shapes do not have: every particle starts at the node's origin");
            break;
        case 6: {
            const gp::Vec3 ax = gp::direction(vec3Prop(get("emission_ring_axis"), {0, 0, 1}));
            const double radius = num("emission_ring_radius", 1.0) * 100.0, height = num("emission_ring_height", 1.0) * 100.0, inner = num("emission_ring_inner_radius", 0.0) * 100.0;
            t += "SHAPE ring " + gp::num(radius) + " " + gp::num(height) + " " + gp::num(inner) + " axis " + gp::num(ax.x) + " " + gp::num(ax.y) + " " + gp::num(ax.z) + "\n";
            const double cone = num("emission_ring_cone_angle", 90.0);
            pm("emission_shape", cone != 90.0 ? Rating::Approximated : Rating::Converted,
               cone != 90.0 ? "a ring; the cone angle (a ring that widens along its axis) is not kept" : "a ring about its axis");
            break;
        }
        default:
            t += "SHAPE point 0 0 0\n";
            break;
    }

    t += "EMISSION " + gp::num(oneShot ? 0.0 : live / lifetime) + " " + std::to_string(oneShot ? live : 0) + " " + std::to_string(live) + "\n";
    t += "LIFETIME " + gp::num(lifetime * (1.0 - lifeRand)) + " " + gp::num(lifetime) + "\n";

    // Direction, spread and speed.
    const gp::Vec3 dir = gp::direction(vec3Prop(get("direction"), {1, 0, 0}));
    const double spread = std::max(0.0, std::min(180.0, num("spread", 45.0)));
    const double vMin = num("initial_velocity_min", 0.0) * 100.0, vMax = num("initial_velocity_max", 0.0) * 100.0;
    t += "DIRECTION " + gp::num(dir.x) + " " + gp::num(dir.y) + " " + gp::num(dir.z) + " " + gp::num(spread) + "\n";
    t += "SPEED " + gp::num(std::min(vMin, vMax)) + " " + gp::num(std::max(vMin, vMax)) + "\n";
    const gp::Vec3 gravityG = vec3Prop(get("gravity"), {0, -9.8, 0});
    const gp::Vec3 grav = gp::direction(gravityG);
    t += "GRAVITY " + gp::num(grav.x * 100.0) + " " + gp::num(grav.y * 100.0) + " " + gp::num(grav.z * 100.0) + "\nDAMPING 0\n";
    if (num("flatness", 0.0) > 0.0) pm("flatness", Rating::Unsupported, "the spread cone is not flattened toward a plane");

    // ---- size over life ----
    std::vector<gp::CurvePoint> scaleCurve = curveOf(*this, get("scale_curve", "scale_amount_curve"), resDoc);
    double scaleMin = num("scale_min", 1.0, "scale_amount_min"), scaleMax = num("scale_max", 1.0, "scale_amount_max");
    if (scaleMin > scaleMax) std::swap(scaleMin, scaleMax);
    t += "SIZE " + gp::num(sizeCm) + " " + gp::num(sizeCm) + "\n";
    if (!scaleCurve.empty()) {
        const std::vector<gp::CurvePoint> red = gp::reduceCurve(scaleCurve);
        std::string s = "SIZECURVE";
        for (const gp::CurvePoint& cp : red) s += " " + gp::num(std::min(std::max(cp.t, 0.0), 1.0)) + " " + gp::num(sizeCm * cp.v);
        t += s + "\n";
        pm(cpu ? "scale_amount_curve" : "scale_curve", scaleCurve.size() > red.size() ? Rating::Approximated : Rating::Converted,
           scaleCurve.size() > red.size() ? "the engine's size curve has four stops: the curve is sampled at four times (the shape between them and the tangents are lost)" : "a size curve of the same points joined by straight lines (tangents are not kept)");
    }

    // ---- colour over life ----
    const Var* colorV = get("color");
    double base[4] = {1, 1, 1, 1};
    if (colorV && colorV->nums.size() >= 4) for (int k = 0; k < 4; ++k) base[k] = colorV->nums[static_cast<size_t>(k)];
    const std::vector<gp::GradientStop> ramp = gradientOf(*this, get("color_ramp"), resDoc);
    double mulRgb[4] = {albedo[0], albedo[1], albedo[2], albedo[3]};
    if (!vertexColour) {
        // The material does not read the particle's colour: the particle is the material's albedo for its whole life.
        t += "COLOR start " + gp::num(mulRgb[0]) + " " + gp::num(mulRgb[1]) + " " + gp::num(mulRgb[2]) + " " + gp::num(mulRgb[3]) + "\nCOLOR end " + gp::num(mulRgb[0]) + " " + gp::num(mulRgb[1]) + " " +
             gp::num(mulRgb[2]) + " " + gp::num(mulRgb[3]) + "\n";
        if (colorV || !ramp.empty()) pm("color", Rating::Approximated, "the draw material does not use the vertex colour (vertex_color_use_as_albedo is off), so the particle's own colour does not show, as in Godot");
    } else if (!ramp.empty()) {
        const std::vector<gp::GradientStop> red = gp::reduceGradient(ramp);
        std::string s = "COLORCURVE";
        for (const gp::GradientStop& gs : red)
            s += " " + gp::num(std::min(std::max(gs.t, 0.0), 1.0)) + " " + gp::num(gs.rgba[0] * base[0] * mulRgb[0]) + " " + gp::num(gs.rgba[1] * base[1] * mulRgb[1]) + " " +
                 gp::num(gs.rgba[2] * base[2] * mulRgb[2]) + " " + gp::num(gs.rgba[3] * base[3] * mulRgb[3]);
        t += s + "\n";
        t += "COLOR start " + gp::num(base[0]) + " " + gp::num(base[1]) + " " + gp::num(base[2]) + " " + gp::num(base[3]) + "\nCOLOR end " + gp::num(base[0]) + " " + gp::num(base[1]) + " " + gp::num(base[2]) +
             " " + gp::num(base[3]) + "\n";
        pm("color_ramp", ramp.size() > red.size() ? Rating::Approximated : Rating::Converted,
           ramp.size() > red.size() ? "the engine's colour curve has four stops: the gradient is sampled at four times" : "the gradient's stops multiply the colour over the particle's life");
    } else {
        t += "COLOR start " + gp::num(base[0] * mulRgb[0]) + " " + gp::num(base[1] * mulRgb[1]) + " " + gp::num(base[2] * mulRgb[2]) + " " + gp::num(base[3] * mulRgb[3]) + "\nCOLOR end " +
             gp::num(base[0] * mulRgb[0]) + " " + gp::num(base[1] * mulRgb[1]) + " " + gp::num(base[2] * mulRgb[2]) + " " + gp::num(base[3] * mulRgb[3]) + "\n";
    }
    if (get("color_initial_ramp")) pm("color_initial_ramp", Rating::Unsupported, "a colour picked from a gradient at birth has no engine counterpart; the colour curve is used");

    // ---- how a particle draws ----
    const int billboard = haveMat ? static_cast<int>(numOf(drawMat.get("billboard_mode"), 0.0)) : 0;
    const int align = static_cast<int>(nodeN("transform_align", 0.0));
    const bool alignY = boolOf(get("particle_flag_align_y"), false) || align == 2 || align == 3;
    if (trailMesh && boolOf(nodeProp(fn, "trail_enabled") ? &nodeProp(fn, "trail_enabled")->value : nullptr, false)) {
        const double speed = 0.5 * (std::fabs(vMin) + std::fabs(vMax));
        const gp::TrailPlan tp = gp::trailPlan(nodeN("trail_lifetime", 0.3), speed > 0.0 ? speed : 100.0, trailPoints);
        t += "RENDER ribbon trail " + std::to_string(tp.points) + " " + gp::num(tp.segmentCm) + " fade 1\n";
        note("trail_enabled", Rating::Approximated,
             "a ribbon along each particle's last positions stands in for the bent trail mesh: its length is the mean initial speed times trail_lifetime (" + gp::num(tp.points * tp.segmentCm) +
                 " cm), its width the mesh's size, its tail fades linearly; the mesh's width curve and a tube's roundness are not drawn");
        note("trail_lifetime", Rating::Approximated, "converted to a trail length from the initial speed; a particle that is accelerated or damped has a shorter or longer trail than Godot's");
    } else if (sphereMesh || boxMesh) {
        t += std::string("RENDER mesh Meshes/") + (sphereMesh ? "sphere" : "cube") + ".ocmesh spin\n";
        note(cpu ? "mesh" : "draw_pass_1", Rating::Approximated, std::string("a ") + (sphereMesh ? "sphere" : "box") + " particle is the engine's unit mesh scaled to the size; the mesh's own segments and material are not used");
    } else if (alignY) {
        t += "SPRITE velocity stretch 0\n";
        note(cpu ? "particle_flag_align_y" : "transform_align", Rating::Approximated, "the sprite is aligned along its velocity (Y to velocity)");
    }
    if (sprite && billboard == 0 && haveMat && !trailMesh)
        note(cpu ? "mesh" : "draw_pass_1", Rating::Approximated, "the draw material has billboarding off; the particle quad is drawn camera-facing");
    // Texture.
    if (haveMat) {
        const Var* tex = drawMat.get("albedo_texture");
        if (tex) {
            const std::string src = textureSource(tex, *drawMat.doc, w);
            std::string asset;
            if (!src.empty()) asset = src.rfind("gen://", 0) == 0 ? generatedTextureAsset(src) : copyTexture(src, w);
            if (!asset.empty()) {
                char hex[32];
                std::snprintf(hex, sizeof hex, "%llx", static_cast<unsigned long long>(fnv1a64(asset)));
                t += std::string("TEX {guid:0x") + hex + "}\n";
            }
        }
        if (!boolOf(drawMat.get("vertex_color_use_as_albedo"), false) && get("color"))
            ;   // reported with the colour above
        if (static_cast<int>(numOf(drawMat.get("shading_mode"), 1.0)) == 0) t += "LIGHTING unlit\nGI off\n";
        if (boolOf(drawMat.get("use_particle_trails"), false) && !trailMesh)
            note("trail_enabled", Rating::Unsupported, "use_particle_trails without a trail mesh draws nothing special");
    }

    // ---- sprite sheet (F30) ----
    std::string animModule;
    if (haveMat && billboard == 3) {
        const int h = static_cast<int>(numOf(drawMat.get("particles_anim_h_frames"), 1.0)), v = static_cast<int>(numOf(drawMat.get("particles_anim_v_frames"), 1.0));
        if (h * v > 1) {
            const bool loop = boolOf(drawMat.get("particles_anim_loop"), false);
            const gp::SubUvPlan sp = gp::subUvPlan(h, v, loop, num("anim_speed_min", 0.0), num("anim_speed_max", 0.0), num("anim_offset_min", 0.0), num("anim_offset_max", 0.0), lifetime);
            t += "SUBUV " + std::to_string(sp.cols) + " " + std::to_string(sp.rows) + " " + std::to_string(sp.frames) + (sp.perSecond ? " fps " + gp::num(sp.fps) : " life") + "\n";
            if (sp.perSecond)
                animModule = "MODULE spawn.animstart min=" + gp::num(sp.startMin) + " max=" + gp::num(sp.startMax) + " frames=" + std::to_string(sp.frames) + "\n";
            const double smin = num("anim_speed_min", 0.0), smax = num("anim_speed_max", 0.0);
            report_.add("property", fn.reportFile, fn.reportPath, fn.type, "particles_anim_h_frames", Rating::Converted, "a " + std::to_string(sp.cols) + " x " + std::to_string(sp.rows) + " flipbook", 0);
            if (std::fabs(smax - smin) > 1e-9)
                pm("anim_speed_min", Rating::Approximated, "every particle plays at the mean of anim_speed_min and anim_speed_max (the engine has one rate per emitter)");
            pm("anim_offset_min", Rating::Converted, "the first frame is drawn per particle from [anim_offset_min, anim_offset_max] of the sheet (spawn.animstart)");
        }
    } else if (haveMat && (boolOf(drawMat.get("particles_anim_loop"), false) || numOf(drawMat.get("particles_anim_h_frames"), 1.0) > 1 || numOf(drawMat.get("particles_anim_v_frames"), 1.0) > 1)) {
        note(cpu ? "mesh" : "draw_pass_1", Rating::Approximated, "a sprite sheet plays only with the material's billboard mode set to particles; here the sheet's first frame is drawn");
    }

    // ---- the module stack ----
    std::vector<std::string> mods;
    if (scaleMin != 1.0 || scaleMax != 1.0) mods.push_back("MODULE spawn.sizescale min=" + gp::num(scaleMin) + " max=" + gp::num(scaleMax) + "\n");
    const double angMin = num("angle_min", 0.0), angMax = num("angle_max", 0.0), avMin = num("angular_velocity_min", 0.0), avMax = num("angular_velocity_max", 0.0);
    if (angMin != 0.0 || angMax != 0.0 || avMin != 0.0 || avMax != 0.0)
        mods.push_back("MODULE spawn.rotation minDeg=" + gp::num(std::min(angMin, angMax)) + " maxDeg=" + gp::num(std::max(angMin, angMax)) + " spinMin=" + gp::num(std::min(avMin, avMax)) +
                       " spinMax=" + gp::num(std::max(avMin, avMax)) + "\n");
    const double hueMin = num("hue_variation_min", 0.0), hueMax = num("hue_variation_max", 0.0);
    if (hueMin != 0.0 || hueMax != 0.0) {
        mods.push_back("MODULE spawn.huevariation min=" + gp::num(std::min(hueMin, hueMax)) + " max=" + gp::num(std::max(hueMin, hueMax)) + "\n");
        pm("hue_variation_min", Rating::Approximated, "a hue turn per particle (1.0 is a full turn of the colour wheel, the reading of Godot's range -1..1 that its shader uses); the rotation is about the grey axis in linear RGB, not Godot's exact matrix");
    }
    if (!animModule.empty()) mods.push_back(animModule);
    const auto range = [&](const char* lo, const char* hi, double scale, double& a, double& b) {
        a = num(lo, 0.0) * scale; b = num(hi, 0.0) * scale;
        if (a > b) std::swap(a, b);
        return a != 0.0 || b != 0.0;
    };
    double a, b;
    if (range("linear_accel_min", "linear_accel_max", 100.0, a, b))
        mods.push_back("MODULE update.linearaccel min=" + gp::num(a) + " max=" + gp::num(b) + "\n");
    if (range("radial_accel_min", "radial_accel_max", 100.0, a, b))
        mods.push_back("MODULE update.radialaccel min=" + gp::num(a) + " max=" + gp::num(b) + "\n");
    if (range("tangent_accel_min", "tangent_accel_max", 100.0, a, b) || range("tangential_accel_min", "tangential_accel_max", 100.0, a, b)) {
        gp::Vec3 g = gravityG;
        const double gl = std::sqrt(g.x * g.x + g.y * g.y + g.z * g.z);
        if (gl > 1e-9) { g.x /= gl; g.y /= gl; g.z /= gl; } else { g = {0, -1, 0}; }
        const gp::Vec3 axis = gp::rotationAxis(g);
        mods.push_back("MODULE update.tangentialaccel min=" + gp::num(a) + " max=" + gp::num(b) + " ax=" + gp::num(axis.x) + " ay=" + gp::num(axis.y) + " az=" + gp::num(axis.z) + "\n");
        pm("tangential_accel_min", Rating::Approximated, "acceleration around the gravity axis through the node's origin, as Godot's shader computes it (cross(direction from the origin, gravity)); a zero gravity falls back to the vertical");
    }
    if (range("damping_min", "damping_max", 100.0, a, b)) {
        mods.push_back("MODULE update.damping min=" + gp::num(a) + " max=" + gp::num(b) + "\n");
        pm("damping_min", Rating::Converted, "a constant per-particle deceleration that stops at zero speed, as Godot's");
    }
    if (range("orbit_velocity_min", "orbit_velocity_max", 1.0, a, b)) {
        const gp::Vec3 axis = gp::rotationAxis({0, 1, 0});
        mods.push_back("MODULE update.orbit min=" + gp::num(a) + " max=" + gp::num(b) + " ax=" + gp::num(axis.x) + " ay=" + gp::num(axis.y) + " az=" + gp::num(axis.z) + "\n");
        pm("orbit_velocity_min", Rating::Approximated, "turns the position about the node's vertical axis (revolutions a second); Godot turns about the particle system's own origin in its simulation space");
    }
    if (boolOf(get("turbulence_enabled"), false)) {
        const double strength = num("turbulence_noise_strength", 1.0), scale = std::max(num("turbulence_noise_scale", 9.0), 0.01);
        const gp::Vec3 spd = vec3Prop(get("turbulence_noise_speed"), {0, 0, 0});
        const double speedLen = std::sqrt(spd.x * spd.x + spd.y * spd.y + spd.z * spd.z);
        mods.push_back("MODULE update.curlnoise freq=" + gp::num(1.0 / (100.0 * scale)) + " strength=" + gp::num(strength * 100.0) + " scroll=" + gp::num(speedLen * 100.0 * (1.0 / (100.0 * scale))) +
                       " octaves=1 seed=0\n");
        pm("turbulence_enabled", Rating::Approximated, "the noise force is a curl-noise field (frequency 1 / (noise_scale m), strength noise_strength m/s^2): its look is not Godot's simplex displacement and is for tuning by eye");
    }

    // Colliders and attractors elsewhere in the scene (F29).
    // Godot's colliders and attractors act on GPUParticles3D only; a CPUParticles3D ignores them.
    const int collisionMode = cpu ? 0 : static_cast<int>(num("collision_mode", 0.0));
    const bool attractorsOn = !cpu && boolOf(get("attractor_interaction_enabled"), true);
    std::vector<Found> found;
    if (!cpu) {
        const FNode* root = &fn;
        while (root->parent) root = root->parent;
        collect(*root, Xform{}, true, found);
    }
    const double friction = std::max(0.0, std::min(1.0, num("collision_friction", 0.0))), bounce = std::max(0.0, std::min(1.0, num("collision_bounce", 0.0)));
    for (const Found& f : found) {
        const FNode& cn = *f.node;
        const V3 pos = f.global.pos;
        const V3 sc{f.global.scale().x, f.global.scale().y, f.global.scale().z};
        const gp::Vec3 apos = gp::direction({pos.x, pos.y, pos.z});
        const std::string cx = gp::num(apos.x * 100.0), cy = gp::num(apos.y * 100.0), cz = gp::num(apos.z * 100.0);
        const bool attractor = cn.type.rfind("GPUParticlesAttractor", 0) == 0;
        const std::string tag = cn.reportPath.empty() ? cn.name : cn.reportPath;
        const double uniform = std::max(std::fabs(sc.x), std::max(std::fabs(sc.y), std::fabs(sc.z)));
        if (attractor) {
            if (!attractorsOn) continue;
            if (cn.type == "GPUParticlesAttractorVectorField3D") {
                nodeRow(cn,Rating::Unsupported, "a vector-field attractor reads a 3D texture of forces the engine's compute layout cannot bind: ignored");
                continue;
            }
            double radius = 0.0;
            if (cn.type == "GPUParticlesAttractorSphere3D") radius = nodeNum(cn, "radius", 1.0) * uniform;
            else {
                const Prop* sp = nodeProp(cn, "size");
                const gp::Vec3 s = sp && sp->value.nums.size() >= 3 ? gp::Vec3{sp->value.nums[0], sp->value.nums[1], sp->value.nums[2]} : gp::Vec3{2, 2, 2};
                radius = 0.5 * std::max(std::fabs(s.x * sc.x), std::max(std::fabs(s.y * sc.y), std::fabs(s.z * sc.z)));
            }
            const double strength = gp::attractorStrengthCm(nodeNum(cn, "strength", 1.0)), atten = std::max(0.0, nodeNum(cn, "attenuation", 1.0));
            mods.push_back("MODULE update.attractor x=" + cx + " y=" + cy + " z=" + cz + " strength=" + gp::num(strength) + " radius=" + gp::num(radius * 100.0) + " falloff=" + gp::num(atten) +
                           " local=0\n");
            nodeRow(cn,Rating::Approximated, std::string("pulls toward the node's centre inside ") + gp::num(radius) + " m with (1 - d / radius)^attenuation" +
                                                             (cn.type == "GPUParticlesAttractorBox3D" ? "; a box is a sphere of its longest half-extent" : "") +
                                                             (nodeNum(cn, "directionality", 0.0) > 0.0 ? "; directionality (a push along the attractor's axis) is not kept" : ""));
            continue;
        }
        if (collisionMode == 0) continue;
        const bool hide = collisionMode == 2;
        if (cn.type == "GPUParticlesCollisionSphere3D") {
            const double radius = nodeNum(cn, "radius", 1.0) * uniform * 100.0;
            mods.push_back("MODULE update.collidesphere x=" + cx + " y=" + cy + " z=" + cz + " r=" + gp::num(radius) + " restitution=" + gp::num(bounce) + " friction=" + gp::num(friction) +
                           " kill=" + (hide ? "1" : "0") + "\n");
            nodeRow(cn,Rating::Converted, "a solid sphere in world space");
        } else if (cn.type == "GPUParticlesCollisionBox3D") {
            const Prop* sp = nodeProp(cn, "size");
            const gp::Vec3 s = sp && sp->value.nums.size() >= 3 ? gp::Vec3{sp->value.nums[0], sp->value.nums[1], sp->value.nums[2]} : gp::Vec3{2, 2, 2};
            // The world box of a rotated box: |R| times the half sizes, in Godot axes, then mapped.
            const M3 rot = f.global.rotation();
            double hx = 0, hy = 0, hz = 0;
            const double hs[3] = {0.5 * std::fabs(s.x) * std::fabs(sc.x), 0.5 * std::fabs(s.y) * std::fabs(sc.y), 0.5 * std::fabs(s.z) * std::fabs(sc.z)};
            for (int k = 0; k < 3; ++k) { hx += std::fabs(rot.m[0][k]) * hs[k]; hy += std::fabs(rot.m[1][k]) * hs[k]; hz += std::fabs(rot.m[2][k]) * hs[k]; }
            const bool aligned = std::fabs(hx - hs[0]) + std::fabs(hy - hs[1]) + std::fabs(hz - hs[2]) < 1e-6;
            const gp::Vec3 e = gp::extents({hx, hy, hz});
            if (hide)
                mods.push_back("MODULE update.killvolume shape=0 cx=" + cx + " cy=" + cy + " cz=" + cz + " hx=" + gp::num(e.x * 100.0) + " hy=" + gp::num(e.y * 100.0) + " hz=" + gp::num(e.z * 100.0) + " invert=0\n");
            else
                mods.push_back("MODULE update.collidebox cx=" + cx + " cy=" + cy + " cz=" + cz + " hx=" + gp::num(e.x * 100.0) + " hy=" + gp::num(e.y * 100.0) + " hz=" + gp::num(e.z * 100.0) +
                               " restitution=" + gp::num(bounce) + " friction=" + gp::num(friction) + "\n");
            nodeRow(cn,aligned ? Rating::Converted : Rating::Approximated,
               aligned ? "a solid axis-aligned box in world space" : "the engine's box collider is axis-aligned: a rotated box is replaced by the box that bounds it");
        } else if (cn.type == "GPUParticlesCollisionHeightField3D") {
            const Prop* sp = nodeProp(cn, "size");
            const double sy = sp && sp->value.nums.size() >= 3 ? sp->value.nums[1] : 2.0;
            const double floorZ = (pos.y - 0.5 * sy * std::fabs(sc.y)) * 100.0;
            mods.push_back("MODULE update.collideplane nx=0 ny=0 nz=1 d=" + gp::num(floorZ) + " restitution=" + gp::num(bounce) + " friction=" + gp::num(friction) + " kill=" + (hide ? "1" : "0") + "\n");
            nodeRow(cn,Rating::Approximated, "a height field needs a height texture the engine's compute layout cannot bind: a flat plane at the floor of its box stands in");
        } else if (cn.type == "GPUParticlesCollisionSDF3D") {
            nodeRow(cn,Rating::Unsupported, "a signed-distance-field collider needs a 3D texture the engine's compute layout cannot bind: ignored");
        }
    }
    if (mods.size() > 16) {
        pm("modules", Rating::Approximated, "the emitter holds at most 16 modules: the last " + std::to_string(mods.size() - 16) + " (colliders and attractors, in scene order) are dropped");
        mods.resize(16);
    }
    for (const std::string& m : mods) t += m;

    // ---- the properties the engine cannot do ----
    if (get("sub_emitter_mode") && static_cast<int>(num("sub_emitter_mode", 0.0)) != 0) pm("sub_emitter_mode", Rating::Unsupported, "sub emitters are not converted (the engine's death and collide events spawn into another emitter of the same effect, which a separate node cannot be)");
    if (nodeProp(fn, "sub_emitter")) note("sub_emitter", Rating::Unsupported, "sub emitters are not converted");
    if (get("directional_velocity_min") || get("radial_velocity_min") || get("velocity_pivot")) pm("directional_velocity_min", Rating::Unsupported, "directional and radial velocity (4.3) are not mapped");
    if (get("scale_over_velocity_min") || get("scale_over_velocity_max")) pm("scale_over_velocity_min", Rating::Unsupported, "scale over velocity is not mapped");
    note("amount", Rating::Converted, "the emitter's particle cap; the rate is amount / lifetime (one burst of amount with one_shot)");
    note("lifetime", Rating::Converted, "seconds; with lifetime_randomness the shortest life is lifetime * (1 - lifetime_randomness)");
    note("one_shot", Rating::Converted, "one burst of amount particles, then nothing");
    note("emitting", Rating::Converted, "the emitter starts playing or stopped");
    if (nodeN("explosiveness", 0.0) > 0.0) note("explosiveness", Rating::Approximated, "particles are emitted at an even rate; explosiveness (a burst per lifetime) is not applied");
    if (nodeN("randomness", 0.0) > 0.0) note("randomness", Rating::Approximated, "spawn times are not randomised");
    if (nodeN("speed_scale", 1.0) != 1.0) note("speed_scale", Rating::Approximated, "the simulation runs at the game's time scale; this node's speed_scale is not applied");
    if (nodeN("preprocess", 0.0) > 0.0) note("preprocess", Rating::Unsupported, "the simulation does not run ahead before the first frame");
    if (boolOf(nodeProp(fn, "local_coords") ? &nodeProp(fn, "local_coords")->value : nullptr, false)) note("local_coords", Rating::Approximated, "particles simulate in world space; local_coords is not applied");
    if (nodeProp(fn, "visibility_aabb")) note("visibility_aabb", Rating::Approximated, "particles are not culled by a box");
    if (nodeProp(fn, "draw_order")) note("draw_order", Rating::Approximated, "the engine sorts translucent particles by depth");
    if (nodeN("amount_ratio", 1.0) != 1.0) note("amount_ratio", Rating::Converted, "scales the particle count");

    // ---- write the effect and the component ----
    createDirectories(contentDir_ + "/Particles");
    const std::string file = safeName(name_ + "_" + fn.name, "Particles") + "_" + fnvHex(fn.reportFile + ":" + fn.reportPath) + ".ocparticle";
    if (!writeFileText(contentDir_ + "/Particles/" + file, t)) { note(cpu ? "amount" : "process_material", Rating::Unsupported, "could not write Particles/" + file); return; }
    fmt::OcPlacedComponent pe;
    pe.type = "ParticleEmitter";
    pe.kv.emplace_back("effect", "Particles/" + file);
    pe.kv.emplace_back("playing", emitting ? "1" : "0");
    pe.kv.emplace_back("sim", "auto");
    p.components.push_back(std::move(pe));
    note(cpu ? "emitting" : "process_material", Rating::Converted, "written as Particles/" + file + " (" + std::string(cpu ? "the node's own parameters" : "a ParticleProcessMaterial") + ")");
}

}  // namespace aver::fabric
