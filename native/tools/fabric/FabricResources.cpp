// Godot resources to Aver assets: textures, materials, primitive meshes, imported models, collision shapes.
#include "FabricInternal.hpp"
#include "aver/core/Log.hpp"
#include "FabricMeshGen.hpp"
#include "FabricProcTex.hpp"

#include "aver/core/Hash.hpp"
#include "aver/formats/OcAnim.hpp"
#include "aver/formats/OcMesh.hpp"
#include "aver/platform/FileSystem.hpp"

#if AVER_HAVE_MATERIAL_COMPILE
#include "aver/formats/ImportCook.hpp"
#include "aver/formats/MaterialCook.hpp"
#include "aver/platform/Image.hpp"
#include "stb_image_write.h"
#endif

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <chrono>
#include <filesystem>

namespace aver::fabric {

// ---- small helpers -------------------------------------------------------------------------------------------------

Var makeNil() { return Var{}; }

Var makeString(const std::string& s) {
    Var v;
    v.type = VType::String;
    v.s = s;
    return v;
}

Var makeResource(const std::string& assetPath) {
    Var v;
    v.type = VType::Resource;
    v.s = assetPath;
    return v;
}

Var makeInt(long long i) {
    Var v;
    v.type = VType::Int;
    v.i = i;
    return v;
}

Var makeFloat(double f) {
    Var v;
    v.type = VType::Float;
    v.f = f;
    return v;
}

Var makeBool(bool b) {
    Var v;
    v.type = VType::Bool;
    v.b = b;
    return v;
}

Var makeVector3(double x, double y, double z) {
    Var v;
    v.type = VType::Vector3;
    v.nums = {x, y, z};
    return v;
}

Var makeObject(const std::string& cls, std::vector<std::pair<std::string, Var>> props) {
    Var v;
    v.type = VType::Object;
    v.s = cls;
    for (auto& [k, val] : props) {
        v.keys.push_back(makeString(k));
        v.items.push_back(std::move(val));
    }
    return v;
}

Var makeDict(std::vector<std::pair<std::string, Var>> entries) {
    Var v;
    v.type = VType::Dictionary;
    for (auto& [k, val] : entries) {
        v.keys.push_back(makeString(k));
        v.items.push_back(std::move(val));
    }
    return v;
}

double numOf(const Var* v, double def) {
    if (!v) return def;
    if (v->type == VType::Int) return static_cast<double>(v->i);
    if (v->type == VType::Float) return v->f;
    if (v->type == VType::Bool) return v->b ? 1.0 : 0.0;
    return def;
}

bool boolOf(const Var* v, bool def) {
    if (!v) return def;
    if (v->type == VType::Bool) return v->b;
    if (v->type == VType::Int) return v->i != 0;
    return def;
}

V3 vec3Of(const Var* v, const V3& def) {
    if (v && (v->type == VType::Vector3 || v->type == VType::Vector3i) && v->nums.size() >= 3) return {v->nums[0], v->nums[1], v->nums[2]};
    return def;
}

std::string stringOf(const Var* v, const std::string& def) {
    if (v && v->isStringLike()) return v->s;
    return def;
}

std::string lower(std::string s) {
    for (char& c : s) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
    return s;
}

std::string safeName(const std::string& in, const std::string& fallback) {
    std::string out;
    for (const char c : in)
        if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-') out.push_back(c);
    return out.empty() ? fallback : out;
}

std::string pascalCase(const std::string& in) {
    std::string out;
    bool up = true;
    for (const char c : in) {
        if (std::isalnum(static_cast<unsigned char>(c))) {
            out.push_back(up ? static_cast<char>(std::toupper(static_cast<unsigned char>(c))) : c);
            up = false;
        } else {
            up = true;
        }
    }
    if (out.empty() || std::isdigit(static_cast<unsigned char>(out[0]))) out = "N" + out;
    return out;
}

std::string stemOf(const std::string& path) {
    size_t a = path.find_last_of("/\\");
    a = a == std::string::npos ? 0 : a + 1;
    const size_t b = path.find_last_of('.');
    return path.substr(a, (b == std::string::npos || b < a) ? std::string::npos : b - a);
}

std::string fnvHex(const std::string& s) {
    char buf[16];
    std::snprintf(buf, sizeof buf, "%08x", static_cast<unsigned>(fnv1a64(s) & 0xffffffffu));
    return buf;
}

namespace {

std::string extOf(const std::string& path) {
    const size_t b = path.find_last_of('.');
    const size_t s = path.find_last_of("/\\");
    if (b == std::string::npos || (s != std::string::npos && b < s)) return {};
    return lower(path.substr(b));
}

bool isModelExt(const std::string& e) { return e == ".glb" || e == ".gltf" || e == ".fbx" || e == ".obj"; }
// Pictures the engine's image loader decodes by content (WebP, SVG and OpenEXR as well as stb_image's formats), so they are copied and cooked as they are.
bool isImageExt(const std::string& e) { return e == ".png" || e == ".jpg" || e == ".jpeg" || e == ".tga" || e == ".bmp" || e == ".webp" || e == ".svg" || e == ".exr"; }
bool isOtherTextureExt(const std::string& e) {
    return e == ".hdr" || e == ".dds" || e == ".ktx" || e == ".ktx2" || e == ".basis";
}
// What converting a picture of this extension loses, or "" when nothing.
const char* imageNote(const std::string& e) {
    if (e == ".svg") return "an SVG is rasterised by the engine's loader at its own size (no text, filters or masks)";
    if (e == ".exr") return "an OpenEXR picture is read as 8 bits a channel (values outside 0..1 are clamped)";
    return "";
}

bool isMaterial3DType(const std::string& t) { return t == "StandardMaterial3D" || t == "ORMMaterial3D"; }

bool isPrimitiveMeshType(const std::string& t) {
    return t == "BoxMesh" || t == "SphereMesh" || t == "CapsuleMesh" || t == "CylinderMesh" || t == "PlaneMesh" || t == "QuadMesh" ||
           t == "PrismMesh" || t == "TorusMesh" || t == "TubeTrailMesh" || t == "RibbonTrailMesh";
}

// The collision shape classes Aver.Fabric's ClassDB can build from an Object(...) value (G3_RUNTIME 1.2).
bool isRuntimeShapeType(const std::string& t) {
    return t == "BoxShape3D" || t == "SphereShape3D" || t == "CapsuleShape3D" || t == "CylinderShape3D" ||
           t == "WorldBoundaryShape3D" || t == "ConvexPolygonShape3D" || t == "ConcavePolygonShape3D";
}

bool isShapeType(const std::string& t) {
    return isRuntimeShapeType(t) || t == "HeightMapShape3D" || t == "SeparationRayShape3D";
}

bool ignorableResourceProp(const std::string& n) {
    return n == "resource_local_to_scene" || n == "resource_name" || n == "resource_path" || n == "script" || n == "metadata/_custom_type_script";
}

float srgbToLinear(double c) {
    c = std::max(0.0, std::min(1.0, c));
    return static_cast<float>(c <= 0.04045 ? c / 12.92 : std::pow((c + 0.055) / 1.055, 2.4));
}

const Var* findProp(const std::vector<gd::GodotProperty>& props, const char* name) {
    for (const gd::GodotProperty& p : props)
        if (p.name == name) return &p.value;
    return nullptr;
}

// A Radiance .hdr picture as PNG bytes. stb reads the floating point pixels and maps them to 8 bits (gamma 2.2, nothing above 1.0 kept), which is
// all the engine's image loader holds anyway.
bool hdrToPng(const std::string& disk, std::vector<u8>& png, std::string& why) {
#if AVER_HAVE_MATERIAL_COMPILE
    ImageData img;
    if (!decodeImage(disk, img, &why) || !img.valid()) {
        if (why.empty()) why = "the picture could not be decoded";
        return false;
    }
    png.clear();
    const auto sink = [](void* context, void* data, int size) {
        auto* out = static_cast<std::vector<u8>*>(context);
        out->insert(out->end(), static_cast<const u8*>(data), static_cast<const u8*>(data) + size);
    };
    if (!stbi_write_png_to_func(sink, &png, static_cast<int>(img.width), static_cast<int>(img.height), 4, img.pixels.data(), static_cast<int>(img.width) * 4)) {
        why = "the PNG could not be written";
        return false;
    }
    return true;
#else
    (void)disk; (void)png;
    why = "this build of the converter has no PNG encoder";
    return false;
#endif
}

// Whether the engine's image loader reads a WebP, SVG or OpenEXR picture (by its bytes, as the loader does); the formats stb_image reads were never checked here.
bool pictureDecodes(const std::string& ext, const std::vector<u8>& bytes, std::string& why) {
#if AVER_HAVE_MATERIAL_COMPILE
    if (ext != ".webp" && ext != ".svg" && ext != ".exr") return true;
    ImageData img;
    if (decodeImage(bytes.data(), bytes.size(), img, &why) && img.valid()) return true;
    if (why.empty()) why = "the picture could not be decoded";
    return false;
#else
    (void)ext; (void)bytes; (void)why;
    return true;
#endif
}

constexpr const char* kHdrNote =
    "a Radiance HDR picture: the engine's image loader holds 8 bits per channel, so it was tone-mapped to 8 bits (values above 1.0 are clamped) and written as a PNG";

}  // namespace

// ---- textures ------------------------------------------------------------------------------------------------------

std::string Converter::copyTexture(const std::string& resPath, const Where& w) {
    if (const auto it = textureAssets_.find(resPath); it != textureAssets_.end()) return it->second;
    const std::string e = extOf(resPath);
    std::string asset;
    if (isImageExt(e)) {
        const std::string disk = fs_.toDiskPath(resPath);
        std::vector<u8> bytes;
        if (disk.empty() || !readFileBytes(disk, bytes)) {
            report_.add("resource", resPath, w.node, "Texture2D", "", Rating::Unsupported, "the texture file is missing", 0);
        } else if (std::string why; !pictureDecodes(e, bytes, why)) {
            report_.add("resource", resPath, w.node, "Texture2D", "", Rating::Unsupported, "the engine's image loader cannot read this " + e + " file: " + why, 0);
        } else {
            std::string rel = resPath.rfind("res://", 0) == 0 ? resPath.substr(6) : resPath;
            asset = "Textures/" + rel;
            const std::string dst = contentDir_ + "/" + asset;
            createDirectories(std::filesystem::path(dst).parent_path().string());
            if (!writeFileBytes(dst, bytes.data(), bytes.size())) {
                report_.add("resource", resPath, w.node, "Texture2D", "", Rating::Unsupported, "could not write " + dst, 0);
                asset.clear();
            } else if (*imageNote(e)) {
                report_.add("resource", resPath, "", "Texture2D", "", Rating::Approximated, "copied to " + asset + ": " + imageNote(e), 0);
            } else {
                report_.add("resource", resPath, "", "Texture2D", "", Rating::Converted, "copied to " + asset, 0);
            }
        }
    } else if (e == ".hdr") {
        const std::string disk = fs_.toDiskPath(resPath);
        std::vector<u8> png;
        std::string why;
        if (disk.empty() || !hdrToPng(disk, png, why)) {
            report_.add("resource", resPath, w.node, "Texture2D", "", Rating::Unsupported, "the Radiance HDR picture could not be converted" + (why.empty() ? std::string() : ": " + why), 0);
        } else {
            asset = "Textures/" + (resPath.rfind("res://", 0) == 0 ? resPath.substr(6) : resPath) + ".png";
            const std::string dst = contentDir_ + "/" + asset;
            createDirectories(std::filesystem::path(dst).parent_path().string());
            if (!writeFileBytes(dst, png.data(), png.size())) {
                report_.add("resource", resPath, w.node, "Texture2D", "", Rating::Unsupported, "could not write " + dst, 0);
                asset.clear();
            } else {
                report_.add("resource", resPath, "", "Texture2D", "", Rating::Approximated, std::string(kHdrNote) + ": " + asset, 0);
            }
        }
    } else if (isOtherTextureExt(e)) {
        report_.add("resource", resPath, w.node, "Texture2D", "", Rating::Unsupported,
                    "a " + e + " texture is not decoded by the engine's image loader; convert it to PNG", 0);
    } else {
        report_.add("resource", resPath, w.node, "Texture2D", "", Rating::Unsupported, "unknown texture file type " + e, 0);
    }
    textureAssets_[resPath] = asset;
    if (!asset.empty()) AVER_INFO("fabric: texture {} -> {}", resPath, asset);
    return asset;
}

// ---- F26: procedural texture resources -----------------------------------------------------------------------------------

bool isProceduralTextureType(const std::string& t) {
    return t == "GradientTexture1D" || t == "GradientTexture2D" || t == "NoiseTexture2D" || t == "CurveTexture" || t == "AtlasTexture" || t == "CanvasTexture" ||
           t == "NoiseTexture3D" || t == "CurveXYZTexture";
}

namespace {

// A resource value (sub-resource or external .tres) as its class and properties.
struct ResRef {
    std::string type, key, res;
    const std::vector<gd::GodotProperty>* props = nullptr;
    const gd::GodotDocBase* doc = nullptr;
};

bool resolveRef(Converter& c, const Var& v, const gd::GodotDocBase& doc, ResRef& out) {
    if (v.type == VType::SubResource) {
        const gd::GodotSubResource* sr = doc.findSub(v.s);
        if (!sr) return false;
        out = {sr->type, doc.file + "#" + sr->id, {}, &sr->props, &doc};
        return true;
    }
    if (v.type == VType::ExtResource) {
        const gd::GodotExtResource* e = doc.findExt(v.s);
        if (!e) return false;
        const std::string res = c.fs_.resolve(*e, &c.diag_, doc.file);
        const std::string ext = extOf(res);
        if (res.empty() || (ext != ".tres" && ext != ".res")) return false;
        const gd::GodotResource* r = c.resourceDoc(res);
        if (!r) return false;
        out = {r->header.type, res, res, &r->props, r};
        return true;
    }
    return false;
}

proctex::Gradient gradientOf(Converter& c, const Var* v, const gd::GodotDocBase& doc, bool* found = nullptr) {
    proctex::Gradient g;
    g.offsets = {0.0f, 1.0f};
    g.colors = {{0, 0, 0, 1}, {1, 1, 1, 1}};
    ResRef r;
    if (found) *found = false;
    if (!v || !resolveRef(c, *v, doc, r) || r.type != "Gradient") return g;
    if (found) *found = true;
    const Var* offs = findProp(*r.props, "offsets");
    const Var* cols = findProp(*r.props, "colors");
    if (offs && cols && !offs->nums.empty() && cols->nums.size() >= offs->nums.size() * 4) {
        g.offsets.clear();
        g.colors.clear();
        for (size_t i = 0; i < offs->nums.size(); ++i) {
            g.offsets.push_back(static_cast<float>(offs->nums[i]));
            g.colors.push_back({static_cast<float>(cols->nums[i * 4]), static_cast<float>(cols->nums[i * 4 + 1]), static_cast<float>(cols->nums[i * 4 + 2]),
                                static_cast<float>(cols->nums[i * 4 + 3])});
        }
    }
    g.mode = static_cast<int>(numOf(findProp(*r.props, "interpolation_mode"), 0));
    return g;
}

// Godot's FastNoiseLite.NoiseType numbers to proctex's.
int noiseTypeOf(int godot) {
    switch (godot) {
        case 0: return 4;   // simplex
        case 1: return 5;   // simplex smooth
        case 2: return 3;   // cellular
        case 3: return 2;   // perlin
        case 4: return 1;   // value cubic
        default: return 0;  // value
    }
}

}  // namespace

std::string Converter::textureSource(const Var* v, const gd::GodotDocBase& doc, const Where& w) {
    if (!v) return {};
    if (v->type == VType::ExtResource) {
        const gd::GodotExtResource* e = doc.findExt(v->s);
        if (!e) return {};
        const std::string res = fs_.resolve(*e, &diag_, doc.file);
        ResRef r;
        if (resolveRef(*this, *v, doc, r) && isProceduralTextureType(r.type)) return proceduralTexture(*v, doc, w);
        return res;
    }
    if (v->type == VType::SubResource) return proceduralTexture(*v, doc, w);
    return {};
}

std::string Converter::proceduralTexture(const Var& v, const gd::GodotDocBase& doc, const Where& w) {
    ResRef r;
    if (!resolveRef(*this, v, doc, r) || !isProceduralTextureType(r.type)) return {};
    if (const auto it = proceduralRefs_.find(r.key); it != proceduralRefs_.end()) return it->second;
    proceduralRefs_[r.key] = {};   // a failure below is final
#if AVER_HAVE_MATERIAL_COMPILE
    const auto& P = *r.props;
    const auto get = [&](const char* n) { return findProp(P, n); };
    const auto rate = [&](Rating rating, const std::string& why) { report_.add("resource", r.doc->file, w.node, r.type, "", rating, why, 0); };
    const std::string base = r.res.empty() ? stemOf(doc.file) + "_" + r.key.substr(r.key.find('#') + 1) : stemOf(r.res);
    const std::string name = "gen://" + safeName(base, "texture") + "_" + fnvHex(r.key);
    proctex::Picture pic;
    std::string alias;   // CanvasTexture: the file it stands for
    const auto intOf = [&](const char* n, int def) { return static_cast<int>(numOf(get(n), def)); };

    if (r.type == "GradientTexture1D") {
        pic = proctex::gradient1D(gradientOf(*this, get("gradient"), *r.doc), intOf("width", 256));
        rate(Rating::Converted, "baked to a PNG at convert time (the gradient sampled at the texture's width)");
    } else if (r.type == "GradientTexture2D") {
        double fx = 0, fy = 0, tx = 1, ty = 0;
        if (const Var* f = get("fill_from"); f && f->nums.size() >= 2) { fx = f->nums[0]; fy = f->nums[1]; }
        if (const Var* t = get("fill_to"); t && t->nums.size() >= 2) { tx = t->nums[0]; ty = t->nums[1]; }
        pic = proctex::gradient2D(gradientOf(*this, get("gradient"), *r.doc), intOf("width", 64), intOf("height", 64), intOf("fill", 0), static_cast<float>(fx),
                                  static_cast<float>(fy), static_cast<float>(tx), static_cast<float>(ty), intOf("repeat", 0));
        rate(Rating::Converted, "baked to a PNG at convert time (linear, radial or square fill, with the repeat mode)");
    } else if (r.type == "NoiseTexture2D") {
        ResRef nr;
        const Var* nv = get("noise");
        if (!nv || !resolveRef(*this, *nv, *r.doc, nr) || nr.type != "FastNoiseLite") {
            rate(Rating::Unsupported, "the texture has no FastNoiseLite noise to bake");
            return {};
        }
        const auto& NP = *nr.props;
        const auto n = [&](const char* k, double d) { return numOf(findProp(NP, k), d); };
        proctex::NoiseParams np;
        np.type = noiseTypeOf(static_cast<int>(n("noise_type", 1)));
        np.seed = static_cast<int>(n("seed", 0));
        np.frequency = static_cast<float>(n("frequency", 0.01));
        if (const Var* o = findProp(NP, "offset"); o && o->nums.size() >= 3) for (int k = 0; k < 3; ++k) np.offset[k] = static_cast<float>(o->nums[static_cast<size_t>(k)]);
        np.fractal = static_cast<int>(n("fractal_type", 1));
        np.octaves = std::clamp(static_cast<int>(n("fractal_octaves", 5)), 1, 10);
        np.lacunarity = static_cast<float>(n("fractal_lacunarity", 2.0));
        np.gain = static_cast<float>(n("fractal_gain", 0.5));
        np.weightedStrength = static_cast<float>(n("fractal_weighted_strength", 0.0));
        np.pingPongStrength = static_cast<float>(n("fractal_ping_pong_strength", 2.0));
        np.cellularDistance = static_cast<int>(n("cellular_distance_function", 0));
        np.cellularJitter = static_cast<float>(n("cellular_jitter", 1.0));
        np.cellularReturn = static_cast<int>(n("cellular_return_type", 1));
        bool haveRamp = false;
        const proctex::Gradient ramp = gradientOf(*this, get("color_ramp"), *r.doc, &haveRamp);
        proctex::NoiseOptions o;
        o.width = intOf("width", 512);
        o.height = intOf("height", 512);
        o.invert = boolOf(get("invert"), false);
        o.normalize = boolOf(get("normalize"), true);
        o.seamless = boolOf(get("seamless"), false);
        o.asNormalMap = boolOf(get("as_normal_map"), false);
        o.bumpStrength = static_cast<float>(numOf(get("bump_strength"), 8.0));
        o.ramp = haveRamp ? &ramp : nullptr;
        if (boolOf(get("in_3d_space"), false)) rate(Rating::Approximated, "in_3d_space noise is baked as the flat slice (z = 0)");
        pic = proctex::noiseImage(np, o);
        rate(Rating::Approximated, "baked to a PNG at convert time with this converter's own noise (the same noise family and settings, not Godot's texels)");
    } else if (r.type == "CurveTexture") {
        ResRef cr;
        const Var* cv = get("curve");
        std::vector<proctex::CurvePoint> pts;
        float minV = 0, maxV = 1, minD = 0, maxD = 1;
        if (cv && resolveRef(*this, *cv, *r.doc, cr) && cr.type == "Curve") {
            if (const Var* d = findProp(*cr.props, "_data"); d && d->type == VType::Array)
                for (size_t i = 0; i + 4 < d->items.size(); i += 5) {
                    proctex::CurvePoint p;
                    p.x = static_cast<float>(d->items[i].num(0));
                    p.y = static_cast<float>(d->items[i].num(1));
                    p.leftTangent = static_cast<float>(d->items[i + 1].asNumber());
                    p.rightTangent = static_cast<float>(d->items[i + 2].asNumber());
                    p.leftMode = static_cast<int>(d->items[i + 3].asInt());
                    p.rightMode = static_cast<int>(d->items[i + 4].asInt());
                    pts.push_back(p);
                }
            minV = static_cast<float>(numOf(findProp(*cr.props, "min_value"), 0));
            maxV = static_cast<float>(numOf(findProp(*cr.props, "max_value"), 1));
            minD = static_cast<float>(numOf(findProp(*cr.props, "min_domain"), 0));
            maxD = static_cast<float>(numOf(findProp(*cr.props, "max_domain"), 1));
        }
        if (pts.empty()) {
            rate(Rating::Unsupported, "the texture has no curve points to bake");
            return {};
        }
        pic = proctex::curve1D(pts, minD, maxD, minV, maxV, intOf("width", 256), intOf("texture_mode", 0) == 1);
        rate(Rating::Converted, "baked to a PNG at convert time (the curve sampled across its domain; the value mapped from its range to 0..1)");
    } else if (r.type == "AtlasTexture") {
        const Var* av = get("atlas");
        const std::string src = textureSource(av, *r.doc, w);
        std::string why;
        ImageData img;
        const std::string disk = src.rfind("gen://", 0) == 0 ? std::string() : fs_.toDiskPath(src);
        if (disk.empty() || !decodeImage(disk, img, &why) || !img.valid()) {
            rate(Rating::Unsupported, "the atlas picture could not be read" + (why.empty() ? std::string() : ": " + why));
            return {};
        }
        proctex::Picture whole;
        whole.width = static_cast<int>(img.width);
        whole.height = static_cast<int>(img.height);
        whole.rgba = img.pixels;
        double rx = 0, ry = 0, rw = img.width, rh = img.height, mx = 0, my = 0, mw = 0, mh = 0;
        if (const Var* rg = get("region"); rg && rg->nums.size() >= 4 && rg->nums[2] > 0 && rg->nums[3] > 0) { rx = rg->nums[0]; ry = rg->nums[1]; rw = rg->nums[2]; rh = rg->nums[3]; }
        if (const Var* mg = get("margin"); mg && mg->nums.size() >= 4) { mx = mg->nums[0]; my = mg->nums[1]; mw = mg->nums[2]; mh = mg->nums[3]; }
        pic = proctex::crop(whole, static_cast<int>(rx), static_cast<int>(ry), static_cast<int>(rw), static_cast<int>(rh), static_cast<int>(mx), static_cast<int>(my),
                            std::max(0, static_cast<int>(mw - mx)), std::max(0, static_cast<int>(mh - my)));
        rate(Rating::Converted, "the region of the atlas cut out into a picture of its own");
    } else if (r.type == "CanvasTexture") {
        alias = textureSource(get("diffuse_texture"), *r.doc, w);
        if (alias.empty()) {
            rate(Rating::Unsupported, "the canvas texture has no diffuse picture");
            return {};
        }
        rate(Rating::Approximated, "stands for its diffuse texture; the normal and specular maps are not used");
        proceduralRefs_[r.key] = alias;
        return alias;
    } else {
        rate(Rating::Unsupported, r.type + " has no 2D picture a material slot can read (a volume or three-channel curve)");
        return {};
    }
    std::vector<u8> png;
    if (!pic.valid() || !proctex::encodePng(pic, png)) {
        rate(Rating::Unsupported, "the picture could not be encoded as a PNG");
        return {};
    }
    generatedTextures_[name] = std::move(png);
    proceduralRefs_[r.key] = name;
    return name;
#else
    report_.add("resource", r.doc->file, w.node, r.type, "", Rating::Unsupported, "this build of the converter has no PNG encoder", 0);
    return {};
#endif
}

// A procedural texture held as a node or script value: the picture it bakes to, written into the content as a texture file.
Var Converter::bakedTextureValue(const Var& v, const gd::GodotDocBase& doc, const Where& w) {
    const std::string src = proceduralTexture(v, doc, w);
    if (src.empty()) return makeNil();
    const std::string asset = src.rfind("gen://", 0) == 0 ? generatedTextureAsset(src) : copyTexture(src, w);
    return asset.empty() ? makeNil() : makeResource(asset);
}

// A baked texture written under Content/Textures/Generated; the asset path or "".
std::string Converter::generatedTextureAsset(const std::string& gen) {
    if (const auto it = generatedAssets_.find(gen); it != generatedAssets_.end()) return it->second;
    std::string asset;
    if (const auto b = generatedTextures_.find(gen); b != generatedTextures_.end()) {
        asset = "Textures/Generated/" + gen.substr(6) + ".png";
        const std::string dst = contentDir_ + "/" + asset;
        createDirectories(std::filesystem::path(dst).parent_path().string());
        if (!writeFileBytes(dst, b->second.data(), b->second.size())) asset.clear();
    }
    generatedAssets_[gen] = asset;
    return asset;
}

namespace {

// A texture file as the material cook wants it: encoded bytes verbatim.
bool loadImage(Converter& c, const std::string& resPath, const Where& w, fmt::ImportedImage& img) {
    const std::string e = extOf(resPath);
    img.ok = false;
    if (resPath.rfind("gen://", 0) == 0) {   // F26: a procedural texture baked in memory
        const auto it = c.generatedTextures_.find(resPath);
        if (it == c.generatedTextures_.end()) return false;
        img.bytes = it->second;
        img.ext = ".png";
        img.suggestedName = safeName(resPath.substr(6), "texture");
        img.sourcePath = resPath;
        img.ok = true;
        return true;
    }
    if (e == ".hdr") {
        // The cook takes encoded bytes: the picture goes in as a PNG.
        const std::string disk = c.fs_.toDiskPath(resPath);
        std::string why;
        if (disk.empty() || !hdrToPng(disk, img.bytes, why)) {
            c.report_.add("resource", resPath, w.node, "Texture2D", "", Rating::Unsupported, "the Radiance HDR picture could not be converted" + (why.empty() ? std::string() : ": " + why), 0);
            return false;
        }
        c.report_.add("resource", resPath, w.node, "Texture2D", "", Rating::Approximated, kHdrNote, 0);
        c.materialTextureSources_.insert(resPath);
        img.ext = ".png";
        img.suggestedName = safeName(stemOf(resPath), "texture");
        img.sourcePath = resPath;
        img.ok = true;
        return true;
    }
    if (!isImageExt(e)) {
        c.report_.add("resource", resPath, w.node, "Texture2D", "", Rating::Unsupported,
                      isOtherTextureExt(e) ? "a " + e + " texture is not decoded by the engine's image loader; convert it to PNG"
                                           : "unknown texture file type " + e,
                      0);
        return false;
    }
    const std::string disk = c.fs_.toDiskPath(resPath);
    if (disk.empty() || !readFileBytes(disk, img.bytes)) {
        c.report_.add("resource", resPath, w.node, "Texture2D", "", Rating::Unsupported, "the texture file is missing", 0);
        return false;
    }
    if (std::string why; !pictureDecodes(e, img.bytes, why)) {
        c.report_.add("resource", resPath, w.node, "Texture2D", "", Rating::Unsupported, "the engine's image loader cannot read this " + e + " file: " + why, 0);
        return false;
    }
    if (*imageNote(e)) c.report_.add("resource", resPath, w.node, "Texture2D", "", Rating::Approximated, imageNote(e), 0);
    c.materialTextureSources_.insert(resPath);
    img.ext = e == ".jpg" ? ".jpeg" : e;
    img.suggestedName = safeName(stemOf(resPath), "texture");
    img.sourcePath = resPath;
    img.ok = true;
    return true;
}

char channelOf(double v) {
    switch (static_cast<int>(std::lround(v))) {
        case 0: return 'r';
        case 1: return 'g';
        case 2: return 'b';
        case 3: return 'a';
        default: return 0;
    }
}

}  // namespace

// ---- materials -----------------------------------------------------------------------------------------------------

std::string Converter::materialAsset(const std::string& materialKey, const std::string& type, const std::vector<gd::GodotProperty>& props,
                                     const gd::GodotDocBase& doc, const std::string& nameHint, const Where& w) {
    // G4 R5: a ViewportTexture in a texture slot names the SubViewport of the node that uses the material, so the same material resource used from two
    // nodes (or two instances of a scene) can bake two different views: the views are part of the cache key. Counted on every use, cached or not.
    const std::string vpAlbedo = viewportReference(findProp(props, "albedo_texture"), doc, w, "albedo_texture");
    const std::string vpEmission = viewportReference(findProp(props, "emission_texture"), doc, w, "emission_texture");
    const std::string key = vpAlbedo.empty() && vpEmission.empty() ? materialKey : materialKey + "|" + vpAlbedo + "|" + vpEmission;
    if (const auto it = materialAssets_.find(key); it != materialAssets_.end()) return it->second;
    materialAssets_[key] = {};   // a failure below is final; the entry is replaced on success

#if AVER_HAVE_MATERIAL_COMPILE
    const std::string& file = doc.file;
    const auto get = [&](const char* n) { return findProp(props, n); };
    const auto tex = [&](const char* n) -> std::string { return textureSource(get(n), doc, w); };   // a file, or a baked procedural texture (F26)

    fmt::ImportedMaterial m;
    std::vector<fmt::ImportedImage> images;
    const auto bind = [&](fmt::ImportedTexture& slot, const std::string& resPath, char channel = 0) {
        if (resPath.empty()) return;
        fmt::ImportedImage img;
        if (!loadImage(*this, resPath, w, img)) return;
        images.push_back(std::move(img));
        slot.imageIndex = static_cast<i32>(images.size() - 1);
        slot.channel = channel;
    };

    std::string name = stringOf(get("resource_name"));
    if (name.empty()) name = nameHint;
    if (name.empty()) name = "Material";
    m.name = safeName(name, "Material");
    // The cook prefixes the project name; two different Godot materials with one name get distinct files.
    {
        std::string unique = m.name;
        for (int n = 2; materialStemsUsed_.count(name_ + "_" + unique); ++n) unique = m.name + "_" + std::to_string(n);
        m.name = unique;
        materialStemsUsed_.insert(name_ + "_" + unique);
    }

    // G4 R6: the BaseMaterial3D features, as the pairs of the .ocmat FX record (OcMat.hpp, parseOcmatFx). Each property below is rated where it is read.
    std::string fx;
    const auto addFx = [&](const std::string& kv) { if (!fx.empty()) fx += ' '; fx += kv; };
    const auto fnum = [](double v) { char b[40]; std::snprintf(b, sizeof b, "%.6g", v); return std::string(b); };
    std::set<std::string> rated;   // properties this block rated, so the leftover loop at the end skips them
    const auto rate = [&](const std::string& prop, Rating r, const std::string& why) { rated.insert(prop); report_.add("property", file, w.node, type, prop, r, why, 0); };
    const auto has = [&](const char* n) { return get(n) != nullptr; };

    // Albedo: Godot stores the colour as displayed (sRGB); the .ocmat holds sRGB too.
    m.baseColorFactorLinear = false;
    if (const Var* c = get("albedo_color"); c && c->type == VType::Color && c->nums.size() >= 4)
        for (int k = 0; k < 4; ++k) m.baseColorFactor[k] = static_cast<float>(c->nums[k]);
    bind(m.baseColorTex, tex("albedo_texture"));
    if (!vpAlbedo.empty()) m.baseColorTex.runtimePath = vpAlbedo;   // G4 R5: the view's picture, supplied by the engine

    m.metallicFactor = static_cast<float>(numOf(get("metallic"), 0.0));
    m.roughnessFactor = static_cast<float>(numOf(get("roughness"), 1.0));

    const std::string ormTex = tex("orm_texture");
    if (!ormTex.empty()) {
        // ORMMaterial3D: R occlusion, G roughness, B metallic, the layout the .ocmat packs.
        bind(m.metalRoughTex, ormTex);
        if (m.metalRoughTex.imageIndex >= 0) {
            m.occlusionTex = m.metalRoughTex;
            m.occlusionTex.channel = 0;
        }
    } else {
        const std::string rough = tex("roughness_texture"), metal = tex("metallic_texture");
        const char rc = channelOf(numOf(get("roughness_texture_channel"), 0)), mc = channelOf(numOf(get("metallic_texture_channel"), 0));
        bind(m.metalRoughTex, rough, rc);
        bind(m.metallicTex, metal, mc);
        // F35: a lone roughness map is read through the material's own channel choice (FX roughchan). A pair is repacked above, so each scalar already sits in its channel.
        if (m.metalRoughTex.imageIndex >= 0 && m.metallicTex.imageIndex < 0) {
            static const char* const kChannel[] = {"r", "g", "b", "a", "gray"};
            const int godotChannel = std::clamp(static_cast<int>(numOf(get("roughness_texture_channel"), 0)), 0, 4);
            if (godotChannel != 1) addFx(std::string("roughchan=") + kChannel[godotChannel]);
            rate("roughness_texture_channel", Rating::Converted, godotChannel == 1 ? "green, the packed map's own channel" : std::string("read from the ") + kChannel[godotChannel] + " channel of the map (FX roughchan)");
            if (m.metalRoughTex.imageIndex >= 0) m.metalRoughTex.channel = 0;
        }
    }

    if (boolOf(get("normal_enabled"), false)) {
        bind(m.normalTex, tex("normal_texture"));
        m.normalScale = static_cast<float>(numOf(get("normal_scale"), 1.0));
    }
    if (boolOf(get("ao_enabled"), false) && ormTex.empty()) {
        bind(m.occlusionTex, tex("ao_texture"), channelOf(numOf(get("ao_texture_channel"), 0)));
        // The map darkens the ambient light fully; ao_light_affect (how much it also darkens direct light) is the material's `aoaffect`.
        m.occlusionStrength = 1.0f;
    }
    if (const double affect = numOf(get("ao_light_affect"), 0.0); affect > 0.0) {
        addFx("aoaffect=" + fnum(affect));
        rate("ao_light_affect", Rating::Approximated, "the occlusion map also darkens the diffuse light that arrives directly (the diffuse albedo is scaled), not the specular");
    }
    // Godot's unshaded mode shows the texture as it is, lit by nothing. The .ocmat has no unshaded switch, so a view's picture in an unshaded material is also its
    // emissive map: it glows by itself on top of whatever light falls on it.
    const bool unshadedView = !vpAlbedo.empty() && vpEmission.empty() && static_cast<int>(numOf(get("shading_mode"), 1)) == 0;
    if (unshadedView) {
        m.emissiveTex.runtimePath = vpAlbedo;
        for (int k = 0; k < 3; ++k) m.emissiveFactor[k] = 1.0f;
    }
    if (boolOf(get("emission_enabled"), false)) {
        const double energy = numOf(get("emission_energy_multiplier"), 1.0);
        // F35: the colour is the factor and the energy is the material's own scale (PARAM emissiveEnergy), whose product is what the factor alone held before, so
        // the look is unchanged and a script can still drive the energy.
        if (const Var* c = get("emission"); c && c->type == VType::Color && c->nums.size() >= 3)
            for (int k = 0; k < 3; ++k) m.emissiveFactor[k] = srgbToLinear(c->nums[k]);
        else
            for (int k = 0; k < 3; ++k) m.emissiveFactor[k] = 1.0f;   // Godot's emission default is black, so this is only for a texture
        m.emissiveEnergy = static_cast<float>(energy);
        bind(m.emissiveTex, tex("emission_texture"));
        if (!vpEmission.empty()) m.emissiveTex.runtimePath = vpEmission;
        report_.add("property", file, w.node, type, "emission_energy_multiplier", Rating::Approximated,
                    "Godot's emission energy is scene-referred; the value is carried over as the .ocmat emissiveEnergy scale without a unit calibration", 0);
    }

    // ---- transparency, blend, cull, depth (G4 R6) ----
    const int transparency = static_cast<int>(numOf(get("transparency"), 0));
    switch (transparency) {
        case 1: m.alphaMode = "BLEND"; break;
        case 2: m.alphaMode = "MASK"; break;
        case 3:   // alpha hash: a noise threshold (opaque pipeline, late depth), not a scissor
            m.alphaMode = "OPAQUE";
            addFx("alphaext=hash");
            if (const double hs = numOf(get("alpha_hash_scale"), 1.0); hs != 1.0) addFx("hashscale=" + fnum(hs));
            rate("transparency", Rating::Approximated, "alpha hash is a stable per-texel noise threshold on an opaque surface (the noise pattern is not Godot's)");
            break;
        case 4:
            m.alphaMode = "BLEND";
            addFx("depthdraw=always");
            rate("transparency", Rating::Approximated, "alpha depth pre-pass is an alpha blend that also writes depth (Godot writes depth for the opaque part only)");
            break;
        default: break;
    }
    if (transparency == 1 || transparency == 2) rate("transparency", Rating::Converted, transparency == 1 ? "alpha blend" : "alpha scissor (the .ocmat masked mode with the threshold)");
    m.alphaCutoff = static_cast<float>(numOf(get("alpha_scissor_threshold"), 0.5));
    if (has("alpha_scissor_threshold")) rate("alpha_scissor_threshold", Rating::Converted, "the .ocmat cut-off");
    if (has("alpha_hash_scale")) rate("alpha_hash_scale", transparency == 3 ? Rating::Converted : Rating::Approximated, transparency == 3 ? "the noise grid scale" : "only alpha hash reads it");
    if (const int aa = static_cast<int>(numOf(get("alpha_antialiasing_mode"), 0)); aa != 0) {
        addFx("alphaext=aa");
        if (transparency == 2 || transparency == 3) {
            if (transparency == 3) m.alphaMode = "MASK";
            rate("alpha_antialiasing_mode", Rating::Approximated, "alpha to coverage needs MSAA; the edge is dithered over the width of the alpha's screen derivative instead");
        } else {
            rate("alpha_antialiasing_mode", Rating::Unsupported, "alpha antialiasing only applies to alpha scissor and alpha hash");
        }
    }
    if (has("alpha_antialiasing_edge")) rate("alpha_antialiasing_edge", Rating::Approximated, "the edge softness is the alpha's screen derivative, not a setting");

    if (const int bm = static_cast<int>(numOf(get("blend_mode"), 0)); bm != 0) {
        static const char* const kModes[] = {"mix", "add", "sub", "mul", "premult"};
        addFx(std::string("blend=") + kModes[std::min(bm, 4)]);
        if (m.alphaMode != "BLEND") m.alphaMode = "BLEND";   // any mode but mix puts the surface in the transparent pass, as in Godot
        rate("blend_mode", bm == 4 ? Rating::Approximated : Rating::Converted,
             bm == 4 ? "premultiplied alpha is drawn with the engine's own premultiplied blend" : std::string("a dedicated blend state (") + kModes[std::min(bm, 4)] + ")");
    }
    if (const int dd = static_cast<int>(numOf(get("depth_draw_mode"), 0)); dd == 1 || dd == 2) {
        addFx(dd == 1 ? "depthdraw=always" : "depthdraw=never");
        rate("depth_draw_mode", Rating::Converted, dd == 1 ? "depth is written for every pixel" : "no depth is written");
    }
    if (boolOf(get("no_depth_test"), false)) { addFx("nodepthtest=1"); rate("no_depth_test", Rating::Converted, "the depth test is off (the surface draws over everything)"); }
    if (has("depth_test")) rate("depth_test", Rating::Unsupported, "an inverted depth test has no pipeline state in the engine");
    if (has("render_priority")) {
        addFx("priority=" + std::to_string(static_cast<int>(numOf(get("render_priority"), 0))));
        rate("render_priority", Rating::Converted, "sorts translucent draws (higher draws later, before the distance sort); opaque draws have no order");
    }
    if (has("next_pass")) rate("next_pass", Rating::Unsupported, "a second material pass has no equivalent");

    const int cull = static_cast<int>(numOf(get("cull_mode"), 0));
    m.doubleSided = cull == 2;
    addFx(cull == 1 ? "cull=front" : (cull == 2 ? "cull=none" : "cull=back"));
    rate("cull_mode", Rating::Converted, cull == 0 ? "back faces are culled (the pipeline's cull state, new for the material variants)"
                                       : (cull == 1 ? "front faces are culled" : "both faces draw"));

    // R1: the mesh's vertex colour multiplies the albedo (the mesh must carry the colour stream; the glTF, FBX and OBJ importers keep it).
    if (boolOf(get("vertex_color_use_as_albedo"), false)) {
        m.vertexColor = true;
        report_.add("property", file, w.node, type, "vertex_color_use_as_albedo", Rating::Converted,
                    "written as the .ocmat vertexcolor flag; the albedo and opacity are multiplied by the mesh's vertex colour in the raster scene pass "
                    "(ray-traced reflections and GI hits do not see it yet)", 0);
        if (boolOf(get("vertex_color_is_srgb"), false)) {
            addFx("vcsrgb=1");
            rate("vertex_color_is_srgb", Rating::Converted, "the vertex colour is decoded from sRGB to linear before it multiplies the albedo (raster draws; ray hits see the colour as stored)");
        }
    }

    // P1: the sampler. Godot's texture_filter 0..5 (nearest, linear, nearest + mipmaps, linear + mipmaps, and the two anisotropic forms) and texture_repeat. A value that is
    // not stated keeps the engine's own sampler (anisotropic, wrap): the default material then costs nothing extra. Raster draws only; ray hits keep the engine sampler.
    if (has("texture_filter")) {
        static const char* const kFilter[] = {"point", "bilinear", "pointmip", "trilinear", "pointmip", "aniso"};
        static const char* const kFilterNote[] = {"nearest, no mipmaps", "linear, no mipmaps", "nearest with mipmaps", "linear with mipmaps", "nearest with mipmaps (anisotropy applies to the project level)",
                                                  "linear with mipmaps and the project's anisotropy"};
        const int tf = std::clamp(static_cast<int>(numOf(get("texture_filter"), 3)), 0, 5);
        if (tf != 5) addFx(std::string("texfilter=") + kFilter[tf]);
        rate("texture_filter", tf == 4 ? Rating::Approximated : Rating::Converted, std::string("the material sampler: ") + kFilterNote[tf] + " (raster draws; reflections and GI keep the engine sampler)");
    }
    if (has("texture_repeat")) {
        const bool repeat = boolOf(get("texture_repeat"), true);
        if (!repeat) addFx("texaddr=clamp");
        rate("texture_repeat", Rating::Converted, repeat ? "wrap, the engine sampler's addressing" : "clamp to the edge (raster draws)");
    }

    // ---- subsurface scattering (existing subsurface fields) ----
    if (boolOf(get("subsurf_scatter_enabled"), false)) {
        m.subsurfaceWeight = static_cast<float>(std::min(1.0, numOf(get("subsurf_scatter_strength"), 0.0)));
        m.subsurfaceRadius = 0.5f;
        rate("subsurf_scatter_strength", Rating::Approximated, "mapped to the .ocmat subsurface weight with a fixed radius");
        rate("subsurf_scatter_enabled", Rating::Approximated, "wrap and thin-surface transmission, not a screen-space blur");
        if (boolOf(get("subsurf_scatter_transmittance_enabled"), false)) {
            if (const Var* c = get("subsurf_scatter_transmittance_color"); c && c->type == VType::Color && c->nums.size() >= 3)
                for (int k = 0; k < 3; ++k) m.subsurfaceColor[k] = static_cast<float>(c->nums[k]);
            const double depthCm = numOf(get("subsurf_scatter_transmittance_depth"), 0.1) * 100.0;
            m.subsurfaceRadius = static_cast<float>(std::max(0.0, std::min(1.0, (depthCm - 0.25) / 4.75)));
            rate("subsurf_scatter_transmittance_enabled", Rating::Approximated, "the transmittance colour tints the scattered light and the depth sets the radius (0.25 to 5 cm)");
        }
    }
    if (has("subsurf_scatter_skin_mode")) rate("subsurf_scatter_skin_mode", Rating::Approximated, "the skin profile is not modelled; the generic wrap is used");
    for (const char* n : {"subsurf_scatter_texture", "subsurf_scatter_transmittance_texture", "subsurf_scatter_transmittance_boost"})
        if (has(n)) rate(n, Rating::Unsupported, "no per-texel or boost control; the scalar subsurface weight is used");

    // ---- reflectance, clearcoat, refraction (existing material fields) ----
    if (has("metallic_specular")) {
        m.reflectance = static_cast<float>(numOf(get("metallic_specular"), 0.5) * 0.08);
        rate("metallic_specular", Rating::Converted, "F0 of the dielectric base is 0.08 * metallic_specular, the rule Godot uses");
    }
    if (boolOf(get("clearcoat_enabled"), false)) {
        m.coatWeight = static_cast<float>(std::max(0.0, std::min(1.0, numOf(get("clearcoat"), 1.0))));
        m.coatRoughness = static_cast<float>(std::max(0.0, std::min(1.0, numOf(get("clearcoat_roughness"), 0.5))));
        rate("clearcoat_enabled", Rating::Approximated, "the layered BSDF's coat lobe; it draws only with RENDER.LAYEREDBSDF on");
        rate("clearcoat", Rating::Approximated, "the coat weight");
        rate("clearcoat_roughness", Rating::Approximated, "the coat roughness");
    }
    if (has("clearcoat_texture")) rate("clearcoat_texture", Rating::Unsupported, "no per-texel coat; the scalar weight is used");
    if (boolOf(get("refraction_enabled"), false)) {
        const double scale = numOf(get("refraction_scale"), 0.05);
        m.transmission = 0.85f;
        m.ior = static_cast<float>(std::max(1.0, std::min(2.0, 1.0 + 5.0 * scale)));
        rate("refraction_enabled", Rating::Approximated, "transmission 0.85 and ior = 1 + 5 * refraction_scale: Aver's screen-space or traced refraction (RENDER.REFRACTION) bends the backdrop; it is not Godot's offset");
        rate("refraction_scale", Rating::Approximated, "the ior above");
    }
    for (const char* n : {"refraction_texture", "refraction_texture_channel"})
        if (has(n)) rate(n, Rating::Unsupported, "no per-texel refraction strength");

    // ---- UV transforms and triplanar (R6) ----
    {
        const V3 s1 = vec3Of(get("uv1_scale"), V3{1, 1, 1}), o1 = vec3Of(get("uv1_offset"), V3{0, 0, 0});
        const V3 s2 = vec3Of(get("uv2_scale"), V3{1, 1, 1}), o2 = vec3Of(get("uv2_offset"), V3{0, 0, 0});
        if (s1.x != 1 || s1.y != 1) addFx("uv1scale=" + fnum(s1.x) + "," + fnum(s1.y));
        if (o1.x != 0 || o1.y != 0) addFx("uv1offset=" + fnum(o1.x) + "," + fnum(o1.y));
        if (s2.x != 1 || s2.y != 1) addFx("uv2scale=" + fnum(s2.x) + "," + fnum(s2.y));
        if (o2.x != 0 || o2.y != 0) addFx("uv2offset=" + fnum(o2.x) + "," + fnum(o2.y));
        if (has("uv1_scale")) rate("uv1_scale", Rating::Converted, "UV * scale + offset in the material variant's pixel shader (x and y; both feed the triplanar position)");
        if (has("uv1_offset")) rate("uv1_offset", Rating::Converted, "UV * scale + offset in the material variant's pixel shader");
        if (has("uv2_scale")) rate("uv2_scale", Rating::Converted, "applied to the maps that read UV2 (detail on UV2, AO and emission on UV2); needs the mesh's second UV set, else UV1 is used");
        if (has("uv2_offset")) rate("uv2_offset", Rating::Converted, "as uv2_scale");
        if (boolOf(get("uv1_triplanar"), false)) {
            addFx("uv1tri=1");
            if (boolOf(get("uv1_world_triplanar"), false)) addFx("uv1world=1");
            if (has("uv1_triplanar_sharpness")) addFx("uv1sharp=" + fnum(numOf(get("uv1_triplanar_sharpness"), 1.0)));
            rate("uv1_triplanar", Rating::Converted, "three projections of the base maps blended by the normal; position in metres (object space, or world with uv1_world_triplanar) * uv1_scale + uv1_offset");
            rate("uv1_world_triplanar", Rating::Converted, "world-space position instead of object space");
            rate("uv1_triplanar_sharpness", Rating::Converted, "the blend exponent");
        } else {
            for (const char* n : {"uv1_world_triplanar", "uv1_triplanar_sharpness"}) if (has(n)) rate(n, Rating::Approximated, "only read with uv1_triplanar");
        }
        if (boolOf(get("uv2_triplanar"), false)) {
            addFx("uv2tri=1");
            rate("uv2_triplanar", Rating::Unsupported, "stored; the engine has one triplanar projection (uv1) and the UV2 maps stay planar");
        }
        for (const char* n : {"uv2_world_triplanar", "uv2_triplanar_sharpness"}) if (has(n)) rate(n, Rating::Unsupported, "see uv2_triplanar");
    }

    // ---- shading, shadows, fog, fades (R6) ----
    {
        const int sm = static_cast<int>(numOf(get("shading_mode"), 1));
        if (sm == 0) {
            addFx("unshaded=1");
            rate("shading_mode", unshadedView ? Rating::Approximated : Rating::Converted, unshadedView ? "unshaded: the view picture is also written as the emissive map; the material variant shows the albedo without lighting" : "unshaded: the albedo and glow reach the target without lighting");
        } else if (sm == 2) {
            rate("shading_mode", Rating::Approximated, "per-vertex shading is drawn per pixel");
        }
        if (boolOf(get("disable_receive_shadows"), false)) { addFx("noshadows=1"); rate("disable_receive_shadows", Rating::Converted, "the sun reaches the surface unshadowed"); }
        if (boolOf(get("disable_ambient_light"), false)) { addFx("noambient=1"); rate("disable_ambient_light", Rating::Approximated, "the sky ambient term and the specular occlusion are zeroed"); }
        if (has("disable_fog")) rate("disable_fog", Rating::Unsupported, "fog is applied to every lit surface");
        if (boolOf(get("shadow_to_opacity"), false)) { addFx("shadowtoopacity=1"); rate("shadow_to_opacity", Rating::Unsupported, "stored; the opacity does not follow the shadow"); }
        if (has("disable_specular_occlusion")) rate("disable_specular_occlusion", Rating::Unsupported, "specular occlusion is not a per-material switch");
        const int dm = static_cast<int>(numOf(get("diffuse_mode"), 0));
        if (has("diffuse_mode")) {
            if (dm == 2) addFx("diffuse=wrap");
            if (dm == 3) addFx("diffuse=toon");
            if (dm == 1) addFx("diffuse=lambert");
            rate("diffuse_mode", dm == 1 ? Rating::Converted : Rating::Approximated,
                 dm == 1 ? "the engine's diffuse lobe is Lambert" : (dm == 2 ? "lambert wrap is the subsurface wrap term" : (dm == 3 ? "toon diffuse is drawn as Lambert" : "burley is drawn as the engine's Lambert")));
        }
        const int spm = static_cast<int>(numOf(get("specular_mode"), 0));
        if (has("specular_mode")) {
            if (spm == 1) addFx("spec=toon");
            if (spm == 2) addFx("spec=off");
            rate("specular_mode", spm == 2 ? Rating::Converted : Rating::Approximated, spm == 2 ? "no specular reflection" : (spm == 1 ? "toon specular is drawn as GGX" : "Schlick-GGX"));
        }
        if (const int fm = static_cast<int>(numOf(get("distance_fade_mode"), 0)); fm != 0) {
            addFx(fm == 1 ? "fade=alpha" : (fm == 2 ? "fade=dither" : "fade=object"));
            addFx("fademin=" + fnum(numOf(get("distance_fade_min_distance"), 0.0) * 100.0));
            addFx("fademax=" + fnum(numOf(get("distance_fade_max_distance"), 10.0) * 100.0));
            rate("distance_fade_mode", fm == 1 ? Rating::Approximated : Rating::Converted,
                 fm == 1 ? "pixel alpha fades the opacity of a blended material; an opaque one dithers instead" : (fm == 2 ? "screen-door dither by the pixel's distance" : "screen-door dither by the object's distance"));
            rate("distance_fade_min_distance", Rating::Converted, "metres to centimetres: invisible closer than this");
            rate("distance_fade_max_distance", Rating::Converted, "metres to centimetres: fully opaque beyond this");
        } else {
            for (const char* n : {"distance_fade_min_distance", "distance_fade_max_distance"}) if (has(n)) rate(n, Rating::Approximated, "only read with distance_fade_mode");
        }
        if (boolOf(get("proximity_fade_enabled"), false)) rate("proximity_fade_enabled", Rating::Unsupported, "fading near other geometry needs the opaque depth, which a translucent draw cannot read");
        if (has("proximity_fade_distance")) rate("proximity_fade_distance", Rating::Unsupported, "see proximity_fade_enabled");
    }

    // ---- vertex stage (R6) ----
    {
        const int bb = static_cast<int>(numOf(get("billboard_mode"), 0));
        if (bb != 0) {
            addFx(bb == 1 ? "billboard=on" : (bb == 2 ? "billboard=y" : "billboard=particles"));
            if (boolOf(get("billboard_keep_scale"), false)) addFx("keepscale=1");
            rate("billboard_mode", bb == 3 ? Rating::Approximated : Rating::Converted,
                 bb == 3 ? "on a mesh, billboard like mode 1 with the sprite sheet's first frame (a mesh draw carries no particle age); as the draw pass of a particle emitter the emitter plays the sheet itself (the emitter's own rows say how)"
                         : "the vertex stage turns the mesh toward the camera (VSMainFx); its shadow and depth prepass are the un-billboarded mesh's");
            if (bb == 3) {
                addFx("frames=" + std::to_string(static_cast<int>(numOf(get("particles_anim_h_frames"), 1))) + "," + std::to_string(static_cast<int>(numOf(get("particles_anim_v_frames"), 1))));
                for (const char* n : {"particles_anim_h_frames", "particles_anim_v_frames", "particles_anim_loop"}) if (has(n)) rate(n, Rating::Approximated, "on a mesh the sheet's first frame is shown; as a particle draw pass the emitter plays the sheet (SUBUV)");
            }
        }
        if (has("billboard_keep_scale")) rate("billboard_keep_scale", bb != 0 ? Rating::Converted : Rating::Approximated, "the node's scale survives the billboard");
        if (boolOf(get("fixed_size"), false)) { addFx("fixedsize=1"); rate("fixed_size", Rating::Converted, "the object keeps one apparent size (offsets from its origin scale with view depth; 1 m of depth is the size authored)"); }
        if (boolOf(get("grow"), false)) {
            addFx("grow=1");
            addFx("growcm=" + fnum(numOf(get("grow_amount"), 0.0) * 100.0));
            rate("grow", Rating::Converted, "vertices are pushed along their normals (metres to centimetres); shadows and the depth prepass use the ungrown mesh");
            rate("grow_amount", Rating::Converted, "metres to centimetres");
        } else if (has("grow_amount")) {
            rate("grow_amount", Rating::Approximated, "only read with grow");
        }
        for (const char* n : {"use_point_size", "point_size"}) if (has(n)) rate(n, Rating::Unsupported, "point rendering has no equivalent");
        for (const char* n : {"use_particle_trails"}) if (has(n)) rate(n, Rating::Approximated, "on a particle emitter's trail draw pass the emitter draws a ribbon along each particle's recent positions (the emitter's trail_enabled row); on a mesh it does nothing");
        for (const char* n : {"use_z_clip_scale", "z_clip_scale", "use_fov_override", "fov_override"}) if (has(n)) rate(n, Rating::Unsupported, "no per-material projection override");
    }

    // ---- surface features (R6) ----
    {
        if (boolOf(get("rim_enabled"), false)) {
            addFx("rim=1");
            addFx("rimamount=" + fnum(numOf(get("rim"), 1.0)));
            addFx("rimtint=" + fnum(numOf(get("rim_tint"), 0.5)));
            rate("rim_enabled", Rating::Approximated, "Godot's per-light rim, added as glow from the sun only (local lights and ambient add none)");
            rate("rim", Rating::Approximated, "the rim amount");
            rate("rim_tint", Rating::Converted, "mix of white and the albedo");
        }
        if (has("rim_texture")) rate("rim_texture", Rating::Unsupported, "no per-texel rim; the scalar amount is used");
        if (boolOf(get("anisotropy_enabled"), false)) {
            addFx("aniso=1");
            addFx("anisoamount=" + fnum(numOf(get("anisotropy"), 0.0)));
            rate("anisotropy_enabled", Rating::Approximated, "the shared BRDF is isotropic: the highlight is widened by the amount, not stretched along the tangent");
            rate("anisotropy", Rating::Approximated, "the widening amount");
        }
        if (has("anisotropy_flowmap")) rate("anisotropy_flowmap", Rating::Unsupported, "no flow direction texture");
        if (boolOf(get("heightmap_enabled"), false)) {
            addFx("height=1");
            addFx("heightscale=" + fnum(numOf(get("heightmap_scale"), 5.0)));
            bind(m.heightTex, tex("heightmap_texture"));
            if (boolOf(get("heightmap_deep_parallax"), false)) {
                addFx("deepparallax=1");
                addFx("layers=" + std::to_string(static_cast<int>(numOf(get("heightmap_min_layers"), 8))) + "," + std::to_string(static_cast<int>(numOf(get("heightmap_max_layers"), 32))));
            }
            if (boolOf(get("heightmap_flip_tangent"), false)) addFx("flipt=1");
            if (boolOf(get("heightmap_flip_binormal"), false)) addFx("flipb=1");
            if (boolOf(get("heightmap_flip_texture"), false)) addFx("invertheight=1");
            rate("heightmap_enabled", Rating::Converted, "parallax offset mapping from the heightmap's red channel (the layer-1 metal-rough texture slot)");
            rate("heightmap_scale", Rating::Approximated, "a UV depth of scale / 100; Godot's scale is in its own units");
            rate("heightmap_deep_parallax", Rating::Approximated, "steep parallax with the layer count by view angle; not Godot's occlusion refinement");
            for (const char* n : {"heightmap_min_layers", "heightmap_max_layers", "heightmap_flip_tangent", "heightmap_flip_binormal", "heightmap_flip_texture", "heightmap_texture"})
                if (has(n)) rate(n, Rating::Converted, "read by the parallax");
        } else {
            for (const char* n : {"heightmap_texture", "heightmap_scale", "heightmap_deep_parallax", "heightmap_min_layers", "heightmap_max_layers", "heightmap_flip_tangent",
                                  "heightmap_flip_binormal", "heightmap_flip_texture"})
                if (has(n)) rate(n, Rating::Approximated, "only read with heightmap_enabled");
        }
        if (boolOf(get("backlight_enabled"), false)) {
            addFx("backlight=1");
            if (const Var* c = get("backlight"); c && c->type == VType::Color && c->nums.size() >= 3)
                addFx("backlightcolor=" + fnum(srgbToLinear(c->nums[0])) + "," + fnum(srgbToLinear(c->nums[1])) + "," + fnum(srgbToLinear(c->nums[2])));
            rate("backlight_enabled", Rating::Approximated, "light from behind the surface, from the sun only, added as glow");
            rate("backlight", Rating::Approximated, "the tint (linear)");
        }
        if (has("backlight_texture")) rate("backlight_texture", Rating::Unsupported, "no per-texel backlight; the colour is used");
        if (boolOf(get("detail_enabled"), false)) {
            addFx("detail=1");
            bind(m.detailAlbedoTex, tex("detail_albedo"));
            bind(m.detailNormalTex, tex("detail_normal"));
            const int db = static_cast<int>(numOf(get("detail_blend_mode"), 0));
            if (db > 0) addFx("detailblend=" + std::to_string(std::min(db, 3)));
            if (static_cast<int>(numOf(get("detail_uv_layer"), 0)) == 1) addFx("detailuv=2");
            rate("detail_enabled", Rating::Converted, "the detail albedo and normal (the layer-1 texture slots) blended over the base maps");
            rate("detail_blend_mode", db == 4 ? Rating::Approximated : Rating::Converted, "mix, add, subtract, multiply; premultiplied alpha is drawn as mix");
            rate("detail_uv_layer", Rating::Converted, "UV1 or UV2 (UV2 needs the mesh's second UV set)");
            rate("detail_albedo", Rating::Converted, "bound to the layer-1 base colour slot");
            rate("detail_normal", Rating::Converted, "bound to the layer-1 normal slot");
            if (has("detail_mask")) rate("detail_mask", Rating::Unsupported, "the mask texture is not read; the detail applies everywhere");
        } else {
            for (const char* n : {"detail_albedo", "detail_normal", "detail_mask", "detail_blend_mode", "detail_uv_layer"}) if (has(n)) rate(n, Rating::Approximated, "only read with detail_enabled");
        }
        if (static_cast<int>(numOf(get("emission_operator"), 0)) == 1) { addFx("emitmul=1"); rate("emission_operator", Rating::Converted, "multiply: the emission is multiplied by the albedo"); }
        else if (has("emission_operator")) rate("emission_operator", Rating::Converted, "add (the engine's emissive)");
        if (boolOf(get("emission_on_uv2"), false)) { addFx("emituv2=1"); rate("emission_on_uv2", Rating::Converted, "the emission map is sampled with UV2 (needs the second UV set)"); }
        if (boolOf(get("ao_on_uv2"), false)) { addFx("aouv2=1"); rate("ao_on_uv2", Rating::Converted, "the occlusion map is sampled with UV2 (needs the second UV set)"); }
        if (has("emission_intensity")) rate("emission_intensity", Rating::Approximated, "physical emission units are not calibrated; emission_energy_multiplier carries the strength");
        for (const char* n : {"albedo_texture_force_srgb"}) if (has(n)) rate(n, Rating::Approximated, "the albedo slot is always sRGB");
        for (const char* n : {"albedo_texture_msdf", "msdf_pixel_range", "msdf_outline_size"}) if (has(n)) rate(n, Rating::Unsupported, "no MSDF text rendering in materials");
        for (const char* n : {"bent_normal_enabled", "bent_normal_texture"}) if (has(n)) rate(n, Rating::Unsupported, "no bent normal map");
        for (const char* n : {"stencil_mode", "stencil_flags", "stencil_compare", "stencil_reference", "stencil_color", "stencil_outline_thickness"})
            if (has(n)) rate(n, Rating::Unsupported, "no stencil buffer in the engine");
    }
    m.fxRecord = fx;   // `cull=back` alone is a record: the engine culled nothing before

    // Features the material cook has no field for; each one is listed rather than dropped silently.
    for (const gd::GodotProperty& p : props) {
        const std::string& n = p.name;
        static const std::set<std::string> handled = {
            "albedo_color", "albedo_texture", "metallic", "roughness", "orm_texture", "roughness_texture", "metallic_texture",
            "roughness_texture_channel", "metallic_texture_channel", "normal_enabled", "normal_scale", "normal_texture",
            "ao_enabled", "ao_texture", "ao_texture_channel", "emission_enabled", "emission", "emission_energy_multiplier",
            "emission_texture", "vertex_color_use_as_albedo", "vertex_color_is_srgb"};
        if (handled.count(n) || rated.count(n) || ignorableResourceProp(n)) continue;
        report_.add("property", file, w.node, type, n, Rating::Unsupported, "the .ocmat has no equivalent", p.line);
    }

    // Separate roughness and metallic maps become the one packed map the .ocmat has.
    std::vector<fmt::ImportedMaterial> mats{m};
    fmt::packSeparateMetalRough(images, mats);

    fmt::MaterialCookOptions opt;
    opt.contentDir = contentDir_;
    opt.assetBase = name_;
    opt.overwriteExisting = true;
    fmt::MaterialCookResult res;
    std::vector<std::string> warnings;
    std::string err;
    if (!fmt::cookMaterials(mats, images, opt, res, &warnings, &err) || res.materialSlotNames.empty() || res.materialSlotNames[0].empty()) {
        report_.add("resource", file, w.node, type, "", Rating::Unsupported, "the material could not be written: " + err, 0);
        return {};
    }
    for (const std::string& warning : warnings) report_.add("resource", file, w.node, type, "", Rating::Approximated, warning, 0);
    const std::string asset = "Materials/" + res.materialSlotNames[0] + ".ocmat";
    report_.add("resource", file, "", type, "", Rating::Converted, key + " written as " + asset, 0);
    materialAssets_[key] = asset;
    AVER_INFO("fabric: material {} -> {}", key, asset);
    return asset;
#else
    (void)type; (void)props; (void)doc; (void)nameHint;
    report_.add("resource", doc.file, w.node, type, "", Rating::Unsupported, "this build of AverAssetC has no material compiler (AVER_MODULE_PBR is off)", 0);
    return {};
#endif
}

// ---- sub-viewports (docs/plans/fabric/G4_RENDER.md R5) ----------------------------------------------------------------

std::string Converter::viewportReference(const Var* value, const gd::GodotDocBase& doc, const Where& w, const std::string& prop) {
    if (!value || value->type != VType::SubResource) return {};
    const gd::GodotSubResource* sr = doc.findSub(value->s);
    if (!sr || sr->type != "ViewportTexture") return {};
    const std::string path = stringOf(findProp(sr->props, "viewport_path"));
    if (!emitting_ || path.empty() || path[0] == '/') {
        report_.add("property", doc.file, w.node, w.type, prop, Rating::Unsupported,
                    emitting_ ? "the ViewportTexture's viewport_path is empty or absolute; only a path relative to its scene's root is resolved"
                              : "a ViewportTexture outside a node cannot be resolved to a SubViewport",
                    value->line);
        return {};
    }
    // The path is relative to the root of the scene the using node was authored in (for a node of an instanced scene, the instance's root).
    const FNode* cur = emitting_->owner ? emitting_->owner : emitting_;
    size_t at = 0;
    while (cur && at <= path.size()) {
        const size_t slash = path.find('/', at);
        const std::string seg = path.substr(at, slash == std::string::npos ? std::string::npos : slash - at);
        at = slash == std::string::npos ? path.size() + 1 : slash + 1;
        if (seg.empty() || seg == ".") continue;
        cur = seg == ".." ? cur->parent : cur->findChild(seg);
    }
    if (!cur || cur->type != "SubViewport") {
        report_.add("property", doc.file, w.node, w.type, prop, Rating::Unsupported,
                    cur ? "the ViewportTexture's viewport_path '" + path + "' names a " + cur->type + ", not a SubViewport"
                        : "the ViewportTexture's viewport_path '" + path + "' names a node that does not exist",
                    value->line);
        return {};
    }
    const std::string key = viewportKeyOf(emittingLevel_, *cur);
    if (++viewportUsers_[key] == 1)
        report_.add("property", doc.file, w.node, w.type, prop, Rating::Converted,
                    "a ViewportTexture: written as the texture reference '@rt:viewport." + key + "', which the engine resolves to the SubViewport's picture "
                    "(drawn by the cheap raster configuration, linear values; the view is drawn only while a material, a container or a script's UI shows it)",
                    value->line);
    return "@rt:viewport." + key;
}

// ---- primitive meshes ----------------------------------------------------------------------------------------------

Var Converter::inlinePrimitiveMesh(const Var& value, const gd::GodotDocBase& doc, const Where& w) {
    if (value.type != VType::SubResource) return makeNil();
    const gd::GodotSubResource* sr = doc.findSub(value.s);
    if (!sr || !isPrimitiveMeshType(sr->type) || sr->type == "TubeTrailMesh" || sr->type == "RibbonTrailMesh") return makeNil();
    std::vector<std::pair<std::string, Var>> entries;
    for (const gd::GodotProperty& p : sr->props) {
        if (ignorableResourceProp(p.name)) continue;
        Var v = convertValue(p.value, doc, w, p.name);
        if (v.isNil() && !p.value.isNil()) continue;   // a nested resource that could not be converted; already reported
        entries.emplace_back(p.name, std::move(v));
    }
    report_.add("resource", doc.file, w.node, sr->type, "", Rating::Converted, "kept as Object(" + sr->type + ") so the node can read the vertices Aver.Fabric's own generator makes (it matches Godot's vertex order)", sr->line);
    return makeObject(sr->type, std::move(entries));
}

std::string Converter::primitiveMeshAsset(const std::string& type, const std::vector<gd::GodotProperty>& props,
                                          const gd::GodotDocBase& doc, const Where& w) {
    // A key from the type and every property's canonical text: equal meshes share one file.
    std::string canon = type;
    for (const gd::GodotProperty& p : props) canon += "|" + p.name + "=" + gd::godotVariantToText(p.value);
    const std::string key = canon + "|" + doc.file;
    if (const auto it = meshAssets_.find(key); it != meshAssets_.end()) return it->second;
    meshAssets_[key] = {};

    std::string slot = "M_Default";
    if (const Var* mat = findProp(props, "material")) {
        const Var cv = convertValue(*mat, doc, w, "material");
        if (cv.type == VType::Resource) slot = stemOf(cv.s);
    }
    canon += "|" + slot;

    SoupMesh soup;
    std::string why;
    const PropGetter get = [&](const char* n) { return findProp(props, n); };
    if (!buildPrimitiveMesh(type, get, soup, &why)) {
        report_.add("resource", doc.file, w.node, type, "", Rating::Unsupported, why, 0);
        return {};
    }
    fmt::OcMeshData mesh = toOcMesh(soup, slot);
    const std::string asset = "Meshes/Generated/" + type + "_" + fnvHex(canon) + ".ocmesh";
    createDirectories(contentDir_ + "/Meshes/Generated");
    std::string err;
    if (!fmt::saveOcMesh(contentDir_ + "/" + asset, mesh, &err)) {
        report_.add("resource", doc.file, w.node, type, "", Rating::Unsupported, "could not write " + asset + ": " + err, 0);
        return {};
    }
    // Properties the generator does not read.
    for (const gd::GodotProperty& p : props) {
        static const std::set<std::string> known = {
            "size", "subdivide_width", "subdivide_height", "subdivide_depth", "radius", "height", "radial_segments", "rings",
            "is_hemisphere", "top_radius", "bottom_radius", "cap_top", "cap_bottom", "orientation", "center_offset", "left_to_right",
            "inner_radius", "outer_radius", "ring_segments", "subdivide_depth", "material", "flip_faces",
            "radial_steps", "sections", "section_length", "section_rings", "shape", "section_segments"};
        if (known.count(p.name) || ignorableResourceProp(p.name)) continue;
        report_.add("property", doc.file, w.node, type, p.name, Rating::Approximated, "not used by the generated mesh", p.line);
    }
    if (type == "TubeTrailMesh" || type == "RibbonTrailMesh")
        report_.add("resource", doc.file, "", type, "", Rating::Approximated,
                    "a trail mesh is bent along a particle's path by the particle system; it is generated as a static straight " + std::string(type == "TubeTrailMesh" ? "tube" : "ribbon") +
                        " along Y, centred, and its curve (width over length) is not applied",
                    0);
    else
        report_.add("resource", doc.file, "", type, "", Rating::Converted, "generated " + asset + " with Godot's dimensions", 0);
    meshAssets_[key] = asset;
    return asset;
}

// ---- imported model files --------------------------------------------------------------------------------------------

namespace {
// Where a model's meshes go (Meshes/<rel>) and the stem its files share.
struct ModelNames { std::string rel, base; };
ModelNames modelNames(const std::string& resPath) {
    ModelNames n;
    n.rel = resPath.rfind("res://", 0) == 0 ? resPath.substr(6) : resPath;
    const size_t dot = n.rel.find_last_of('.');
    if (dot != std::string::npos) n.rel.erase(dot);
    n.base = n.rel;
    for (char& c : n.base) if (c == '/') c = '_';
    n.base = safeName(n.base, "model");
    return n;
}
}  // namespace

ModelResult Converter::importModelOnly(const std::string& resPath) const {
    ModelResult r;
    if (!host_.importModel) {
        r.error = "this AverAssetC has no model importer";
        return r;
    }
    const ModelNames names = modelNames(resPath);
    const std::string disk = fs_.toDiskPath(resPath);
    // A dense model takes minutes (meshlets and LODs for every mesh): say which one, and afterwards how long it took.
    std::error_code sizeEc;
    const auto bytes = std::filesystem::file_size(disk, sizeEc);
    AVER_INFO("fabric: importing model {} ({:.1f} MB)", resPath, sizeEc ? 0.0 : static_cast<double>(bytes) / (1024.0 * 1024.0));
    const auto t0 = std::chrono::steady_clock::now();
    r = host_.importModel(disk, contentDir_, "Meshes/" + names.rel, names.base);
    AVER_INFO("fabric: model {} {} in {:.1f} s", resPath, r.ok ? "imported" : "failed",
              std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count());
    if (r.ok) {
        // The importer writes the model's skeletons beside its meshes; the converter finds them there (the host contract has no field for them).
        std::error_code ec;
        const std::string sub = "Meshes/" + names.rel;
        for (const auto& entry : std::filesystem::directory_iterator(contentDir_ + "/" + sub, ec)) {
            if (!entry.is_regular_file(ec) || entry.path().extension() != ".ocskel") continue;
            r.skeletons.push_back(sub + "/" + entry.path().filename().string());
        }
        std::sort(r.skeletons.begin(), r.skeletons.end());
    }
    return r;
}

void Converter::prefetchModels() {
    // The model files the scenes (and resources) name: each is imported into its own Meshes/<rel> and Textures/<stem> folders, so they can run at once.
    std::set<std::string> wanted;
    const auto collect = [&](const gd::GodotDocBase& doc) {
        for (const gd::GodotExtResource& e : doc.extResources) {
            const std::string res = fs_.resolve(e, nullptr, doc.file);
            if (res.empty() || !isModelExt(extOf(res)) || models_.count(res) || prefetchedModels_.count(res) || !fs_.exists(res)) continue;
            wanted.insert(res);
        }
    };
    for (const auto& [res, doc] : sceneDocs_) if (doc) collect(*doc);
    for (const auto& [res, doc] : resourceDocs_) if (doc) collect(*doc);
    if (wanted.empty()) return;
    std::vector<std::pair<uintmax_t, std::string>> list;
    for (const std::string& res : wanted) {
        std::error_code ec;
        const uintmax_t bytes = std::filesystem::file_size(fs_.toDiskPath(res), ec);
        list.emplace_back(ec ? 0 : bytes, res);
    }
    std::sort(list.begin(), list.end(), [](const auto& a, const auto& b) { return a.first > b.first; });   // the largest first
    // Fewer threads than cores: a dense model holds gigabytes while its LODs are built.
    const unsigned jobs = fabricJobs(4);
    AVER_INFO("fabric: importing {} model(s) on {} thread(s)", list.size(), jobs);
    std::vector<ModelResult> results(list.size());
    std::atomic<size_t> done{0};
    const std::string total = std::to_string(list.size());
    parallelFor(list.size(), jobs, [&](size_t i) {
        results[i] = importModelOnly(list[i].second);
        const size_t n = ++done;
        emitProgress(0.13 + 0.27 * static_cast<double>(n) / static_cast<double>(list.size()), "model " + std::to_string(n) + "/" + total + ": " + list[i].second);
    });
    for (size_t i = 0; i < list.size(); ++i) prefetchedModels_[list[i].second] = std::move(results[i]);
}

const ModelResult* Converter::modelFor(const std::string& resPath, const Where& w) {
    if (const auto it = models_.find(resPath); it != models_.end()) return it->second.ok ? &it->second : nullptr;
    ModelResult r;
    if (const auto pre = prefetchedModels_.find(resPath); pre != prefetchedModels_.end()) {
        r = std::move(pre->second);
        prefetchedModels_.erase(pre);
    } else {
        emitProgressText("model: " + resPath);
        r = importModelOnly(resPath);
    }
    // What reads the converter's own state (the model's .import settings, the project's materials), in the order the scenes reach the model.
    if (r.ok) {
        applyRootScale(resPath, r);
        applyMaterialOverrides(resPath, r, modelNames(resPath).base);
    }
    models_[resPath] = r;
    if (!r.ok) {
        report_.add("resource", resPath, w.node, "PackedScene", "", Rating::Unsupported, "the model could not be imported: " + r.error, 0);
        return nullptr;
    }
    for (const std::string& n : r.notes) report_.add("resource", resPath, "", "PackedScene", "", Rating::Approximated, n, 0);
    report_.add("resource", resPath, "", "PackedScene", "", Rating::Converted, std::to_string(r.parts.size()) + " mesh(es) imported", 0);
    return &models_[resPath];
}

// The model's import settings can replace a material of the file with a material file of the project (`_subresources` > materials > <glTF material name> >
// use_external/*): the engine's importer knows nothing of that, so a mesh's slot named for the file's material is renamed here to the converted project material.
void Converter::applyMaterialOverrides(const std::string& resPath, ModelResult& r, const std::string& base) {
    gd::GodotImport imp;
    gd::GodotDiagnostics d;
    if (!fs_.loadImport(resPath, imp, d)) return;
    const Var* sub = imp.param("_subresources");
    const Var* mats = sub && sub->type == VType::Dictionary ? sub->find("materials") : nullptr;
    if (!mats || mats->type != VType::Dictionary) return;
    std::map<std::string, std::string> remap;   // the slot name the importer wrote -> the converted material's name
    for (size_t i = 0; i < mats->keys.size() && i < mats->items.size(); ++i) {
        const Var& cfg = mats->items[i];
        if (cfg.type != VType::Dictionary || !boolOf(cfg.find("use_external/enabled"), false)) continue;
        const std::string name = mats->keys[i].asString();
        const std::string uidOrPath = stringOf(cfg.find("use_external/path"));
        std::string res = uidOrPath.empty() ? std::string{} : (gd::GodotProjectFs::isUid(uidOrPath) ? fs_.resolve(uidOrPath, "") : gd::GodotProjectFs::normalizeRes(uidOrPath));
        if (res.empty()) {
            const std::string fb = stringOf(cfg.find("use_external/fallback_path"));
            if (!fb.empty()) res = gd::GodotProjectFs::normalizeRes(fb);
        }
        const Where w{resPath, "", "PackedScene"};
        const gd::GodotResource* mr = res.empty() ? nullptr : resourceDoc(res);
        if (!mr || !isMaterial3DType(mr->header.type)) {
            report_.add("resource", resPath, "", "PackedScene", "_subresources/materials/" + name, Rating::Approximated,
                        "the import replaces this material with " + (res.empty() ? std::string("a file that could not be found") : res + " (not a StandardMaterial3D or ORMMaterial3D)") + "; the model keeps the material of the file", 0);
            continue;
        }
        const std::string asset = materialAsset(res, mr->header.type, mr->props, *mr, stemOf(res), w);
        if (asset.empty()) continue;
        remap[base + "_" + name] = stemOf(asset);
        report_.add("resource", resPath, "", "PackedScene", "_subresources/materials/" + name, Rating::Converted, "the import uses the project material " + res + " (" + asset + ") in place of the file's", 0);
    }
    if (remap.empty()) return;
    std::set<std::string> done;
    for (const ModelPart& part : r.parts) {
        if (!done.insert(part.meshAsset).second) continue;
        fmt::OcMeshData m;
        std::string err;
        if (!fmt::loadOcMesh(contentDir_ + "/" + part.meshAsset, m, &err)) continue;
        bool changed = false;
        for (std::string& slot : m.materialSlots)
            if (const auto it = remap.find(slot); it != remap.end()) { slot = it->second; changed = true; }
        if (changed && !fmt::saveOcMesh(contentDir_ + "/" + part.meshAsset, m, &err))
            report_.add("resource", resPath, "", "PackedScene", "_subresources/materials", Rating::Approximated, "the mesh " + part.meshAsset + " could not be written with the project's material: " + err, 0);
    }
}

// The model's own import settings: the root scale is the one that changes geometry. With nodes/apply_root_scale (Godot's default) it is applied to the
// meshes, the nodes' translations, the bones and the animations; the meshes and the parts' positions are scaled here, the node tree (buildModelNodes) scales
// the translations. Skeletons and clips are written by the importer and not scaled. Without apply_root_scale Godot scales the imported root node, which the
// instancing scene's own transform replaces.
void Converter::applyRootScale(const std::string& resPath, ModelResult& r) {
    gd::GodotImport imp;
    gd::GodotDiagnostics d;
    if (!fs_.loadImport(resPath, imp, d)) return;
    const Var* sv = imp.param("nodes/root_scale");
    const double s = numOf(sv, 1.0);
    if (!sv || std::fabs(s - 1.0) <= 1e-9) return;
    if (!boolOf(imp.param("nodes/apply_root_scale"), true)) {
        report_.add("resource", resPath, "", "PackedScene", "nodes/root_scale", Rating::Approximated,
                    "apply_root_scale is off: Godot scales the imported root node, whose transform the instancing scene replaces, so the scale is not applied", 0);
        return;
    }
    size_t scaled = 0;
    std::set<std::string> done;
    for (ModelPart& part : r.parts) {
        part.x *= s; part.y *= s; part.z *= s;
        if (!done.insert(part.meshAsset).second) continue;
        fmt::OcMeshData m;
        std::string err;
        if (!fmt::loadOcMesh(contentDir_ + "/" + part.meshAsset, m, &err)) {
            report_.add("resource", resPath, "", "PackedScene", "nodes/root_scale", Rating::Approximated, "the mesh " + part.meshAsset + " could not be read to scale it: " + err, 0);
            continue;
        }
        const f32 k = static_cast<f32>(s);
        for (f32& v : m.positions) v *= k;
        const auto scaleMeshlets = [k](std::vector<fmt::OcMeshMeshlet>& list) {
            for (fmt::OcMeshMeshlet& ml : list) {
                ml.sphereCenter = ml.sphereCenter * k;
                ml.sphereRadius *= k;
                ml.coneApex = ml.coneApex * k;
            }
        };
        scaleMeshlets(m.meshlets);
        for (fmt::OcMeshLod& lod : m.coarserLods) scaleMeshlets(lod.meshlets);
        for (fmt::OcMorphTarget& t : m.morphTargets) {
            for (f32& v : t.positionDeltas) v *= k;
            t.finalize();
        }
        m.computeBounds();
        if (!fmt::saveOcMesh(contentDir_ + "/" + part.meshAsset, m, &err)) {
            report_.add("resource", resPath, "", "PackedScene", "nodes/root_scale", Rating::Approximated, "the scaled mesh " + part.meshAsset + " could not be written: " + err, 0);
            continue;
        }
        ++scaled;
    }
    // The skeletons the importer wrote scale with the meshes they skin (rest translations, inverse binds, sockets), or a skinned mesh would not fit its bones.
    size_t skeletonsScaled = 0;
    for (const std::string& sp : r.skeletons) {
        fmt::OcSkeleton sk;
        std::string err;
        if (!fmt::loadOcSkel(contentDir_ + "/" + sp, sk, &err)) continue;
        const f32 k = static_cast<f32>(s);
        for (fmt::OcBone& b : sk.bones) {
            b.translation = b.translation * k;
            for (int i = 12; i < 15; ++i) b.inverseBind[i] *= k;
        }
        for (fmt::OcSocket& so : sk.sockets) so.translation = so.translation * k;
        if (fmt::saveOcSkel(contentDir_ + "/" + sp, sk, &err)) ++skeletonsScaled;
    }
    r.rootScale = s;
    report_.add("resource", resPath, "", "PackedScene", "nodes/root_scale", Rating::Converted,
                "the import's root scale " + std::to_string(s) + " is applied to " + std::to_string(scaled) + " mesh(es), " + std::to_string(skeletonsScaled) +
                    " skeleton(s) and the node translations and animation tracks the converter writes (the importer's own engine-space clips are not scaled and not used)", 0);
}

const ModelTree* Converter::modelTreeFor(const std::string& resPath, const Where& w) {
    if (const auto it = modelTrees_.find(resPath); it != modelTrees_.end()) return it->second.get();
    const std::string ext = [&] { const size_t d = resPath.find_last_of('.'); return d == std::string::npos ? std::string{} : lower(resPath.substr(d)); }();
    std::unique_ptr<ModelTree> tree;
    if (ext == ".glb" || ext == ".gltf") {
        tree = std::make_unique<ModelTree>();
        std::string why;
        if (!buildModelTree(fs_.toDiskPath(resPath), *tree, why)) {
            report_.add("resource", resPath, w.node, "PackedScene", "", Rating::Approximated,
                        "the model's node tree could not be read (" + why + "); its meshes are placed directly below the instance, so children hung on nodes inside the model are dropped", 0);
            tree.reset();
        } else {
            for (const std::string& n : tree->notes) report_.add("resource", resPath, "", "PackedScene", "", Rating::Approximated, n, 0);
        }
    }
    const ModelTree* p = tree.get();
    modelTrees_[resPath] = std::move(tree);
    return p;
}

const gd::GodotResource* Converter::resourceDoc(const std::string& resPath) {
    if (const auto it = resourceDocs_.find(resPath); it != resourceDocs_.end()) return it->second.get();
    auto doc = std::make_unique<gd::GodotResource>();
    gd::GodotDiagnostics d;
    const bool ok = fs_.loadResource(resPath, *doc, d);
    return storeResourceDoc(resPath, std::move(doc), ok, d);
}

const gd::GodotResource* Converter::storeResourceDoc(const std::string& resPath, std::unique_ptr<gd::GodotResource> doc, bool ok, const gd::GodotDiagnostics& d) {
    if (!ok) {
        for (const gd::GodotDiag& x : d.items) report_.add("resource", resPath, "", "", "", Rating::Unsupported, x.message, x.line);
        resourceDocs_[resPath] = nullptr;
        return nullptr;
    }
    for (const gd::GodotDiag& x : d.items)
        if (x.severity != gd::GodotSeverity::Info) report_.add("resource", resPath, "", "", "", Rating::Approximated, x.message, x.line);
    const gd::GodotResource* p = doc.get();
    resourceDocs_[resPath] = std::move(doc);
    return p;
}

// ---- shapes and other resources -------------------------------------------------------------------------------------------

Var Converter::physicsMaterialObject(const std::vector<gd::GodotProperty>& props, const gd::GodotDocBase& doc, const Where& w, int line) {
    // Godot stores the properties that differ from its defaults; the runtime class has the same defaults (friction 1, the rest off or 0).
    std::vector<std::pair<std::string, Var>> entries;
    for (const char* key : {"friction", "rough", "bounce", "absorbent"})
        if (const Var* p = findProp(props, key)) entries.emplace_back(key, convertValue(*p, doc, w, key));
    report_.add("resource", doc.file, w.node, "PhysicsMaterial", "", Rating::Converted,
                "friction, bounce, rough and absorbent become Aver.Fabric's PhysicsMaterial, which the body applies to its surface when it is built", line);
    return makeObject("PhysicsMaterial", std::move(entries));
}

namespace {
bool isOccluderType(const std::string& t) {
    return t == "BoxOccluder3D" || t == "SphereOccluder3D" || t == "QuadOccluder3D" || t == "PolygonOccluder3D" || t == "ArrayOccluder3D";
}
}  // namespace

Var Converter::occluderObject(const std::string& type, const std::vector<gd::GodotProperty>& props, const gd::GodotDocBase& doc, const Where& w, int line) {
    std::vector<std::pair<std::string, Var>> entries;
    for (const gd::GodotProperty& p : props) {
        if (ignorableResourceProp(p.name)) continue;
        entries.emplace_back(p.name, convertValue(p.value, doc, w, p.name));
    }
    report_.add("resource", doc.file, w.node, type, "", Rating::Approximated,
                "kept as Aver.Fabric's " + type + " and not used: the renderer's occlusion culling is automatic, so an authored occluder shape changes nothing that is drawn", line);
    return makeObject(type, std::move(entries));
}

ScriptInfo* Converter::resourceScript(const std::vector<gd::GodotProperty>& props, const gd::GodotDocBase& doc) {
    const Var* s = findProp(props, "script");
    if (!s || s->type != VType::ExtResource) return nullptr;
    const gd::GodotExtResource* e = doc.findExt(s->s);
    if (!e || e->type != "Script") return nullptr;
    ScriptInfo* si = script(e->path);
    return si && si->exists && !si->className.empty() ? si : nullptr;
}

Var Converter::genericResourceDict(const std::string& type, const std::vector<gd::GodotProperty>& props, const gd::GodotDocBase& doc,
                                   const Where& w, const char* why) {
    // A custom Resource class (a script on the resource) is built as the class its script converts to, so a typed field or list holds it.
    ScriptInfo* si = resourceScript(props, doc);
    std::vector<std::pair<std::string, Var>> entries;
    entries.emplace_back("__class", makeString(si ? si->className : type));
    for (const gd::GodotProperty& p : props) {
        if (ignorableResourceProp(p.name)) continue;
        entries.emplace_back(p.name, convertValue(p.value, doc, w, p.name));
    }
    if (si) {
        si->uses.push_back(doc.file + (w.node.empty() ? std::string() : ":" + w.node));
        report_.add("resource", doc.file, w.node, type, "", Rating::Converted,
                    "a custom resource: built at run time as " + si->className + ", the class its script " + si->path + " converts to", 0);
    } else if (type == "ParticleProcessMaterial")
        report_.add("resource", doc.file, w.node, type, "", Rating::Converted,
                    "held as a ParticleProcessMaterial for scripts; the emitter's .ocparticle is written from its values (the particle node's own rows say what each property became)", 0);
    else if (type == "SpriteFrames")
        report_.add("resource", doc.file, w.node, type, "", Rating::Converted, "held as a SpriteFrames: its animations, speeds, loops and frame durations are kept and each frame texture is a copied texture", 0);
    else
        report_.add("resource", doc.file, w.node, type, "", Rating::Approximated, why, 0);
    return makeDict(std::move(entries));
}

// A custom resource file is also written as its value, the "__class" dictionary a scene holds, to Resources/<path>.ocres, so a
// script's load() of the path builds the class (ResourceLoader.Load reads it).
void Converter::customResourceFile(const std::string& res, const std::string& type, const gd::GodotResource& r, const Where& w) {
    std::string path = "Resources/" + (res.rfind("res://", 0) == 0 ? res.substr(6) : res);
    if (const size_t dot = path.find_last_of('.'); dot != std::string::npos && path.find('/', dot) == std::string::npos) path.erase(dot);
    path += ".ocres";
    const Var value = genericResourceDict(type, r.props, r, w, "");
    createDirectories(std::filesystem::path(contentDir_ + "/" + path).parent_path().string());
    if (!writeFileText(contentDir_ + "/" + path, gd::godotVariantToText(value) + "\n"))
        report_.add("resource", res, "", type, "", Rating::Unsupported, "the custom resource file " + path + " could not be written", 0);
}

Var Converter::shapeObject(const std::string& type, const std::vector<gd::GodotProperty>& props, const gd::GodotDocBase& doc, const Where& w) {
    if (isRuntimeShapeType(type)) {
        std::vector<std::pair<std::string, Var>> entries;
        for (const gd::GodotProperty& p : props) {
            if (ignorableResourceProp(p.name)) continue;
            entries.emplace_back(p.name, convertValue(p.value, doc, w, p.name));
        }
        report_.add("resource", doc.file, w.node, type, "", Rating::Converted, "", 0);
        return makeObject(type, std::move(entries));
    }
    if (type == "HeightMapShape3D") {
        const int width = static_cast<int>(numOf(findProp(props, "map_width"), 2)), depth = static_cast<int>(numOf(findProp(props, "map_depth"), 2));
        const Var* data = findProp(props, "map_data");
        if (width < 2 || depth < 2 || width * depth > 64 * 64 || !data || data->nums.size() < static_cast<size_t>(width) * depth) {
            return genericResourceDict(type, props, doc, w,
                                       "kept as a dictionary: only height maps up to 64 x 64 with all their data become a triangle shape");
        }
        // Godot centres the grid on the origin; one triangle pair per cell.
        Var tri;
        tri.type = VType::PackedVector3Array;
        const auto height = [&](int x, int z) { return data->nums[static_cast<size_t>(z) * width + x]; };
        const auto pt = [&](int x, int z) { return V3{x - (width - 1) / 2.0, height(x, z), z - (depth - 1) / 2.0}; };
        const auto push = [&](const V3& p) { tri.nums.insert(tri.nums.end(), {p.x, p.y, p.z}); };
        for (int z = 0; z + 1 < depth; ++z)
            for (int x = 0; x + 1 < width; ++x) {
                push(pt(x, z)); push(pt(x + 1, z)); push(pt(x, z + 1));
                push(pt(x + 1, z)); push(pt(x + 1, z + 1)); push(pt(x, z + 1));
            }
        report_.add("resource", doc.file, w.node, type, "", Rating::Approximated,
                    "written as a ConcavePolygonShape3D of the height map's triangles (no height-field shape in the runtime class table)", 0);
        return makeObject("ConcavePolygonShape3D", {{"data", std::move(tri)}});
    }
    return genericResourceDict(type, props, doc, w, "this shape class has no Aver.Fabric counterpart yet; its parameters are kept as a dictionary");
}

namespace {

bool isEnvironmentType(const std::string& t) {
    static const std::set<std::string> s = {"Environment", "Sky", "ProceduralSkyMaterial", "PhysicalSkyMaterial", "PanoramaSkyMaterial",
                                            "CameraAttributes", "CameraAttributesPractical", "CameraAttributesPhysical",
                                            "FogMaterial"};   // G4 R8: a FogVolume's material is held as a value like the environment's resources
    return s.count(t) != 0;
}

}  // namespace

Var Converter::environmentObject(const std::string& type, const std::vector<gd::GodotProperty>& props, const gd::GodotDocBase& doc, const Where& w) {
    std::vector<std::pair<std::string, Var>> entries;
    if (type == "Environment") skyBackgroundEnergy_ = numOf(findProp(props, "background_energy_multiplier"), 1.0);   // F23: handed to the Sky converted below
    for (const gd::GodotProperty& p : props) {
        if (ignorableResourceProp(p.name)) continue;
        if (type == "FogMaterial") {
            // G4 R8: what each FogMaterial key becomes in the engine's fog volume (framework_envfx_abi.h; shader: voxi_froxel.hlsl, AVER_FOG_VOLUMES).
            const char* why = nullptr;
            Rating r = Rating::Converted;
            if (p.name == "density") why = "extinction per metre, per centimetre in the engine (/100); may be negative, which carves fog out of the volumes it overlaps";
            else if (p.name == "albedo") why = "the scattering colour: the sun and the sky light the volume's fog with it (sRGB to linear)";
            else if (p.name == "emission") { why = "radiance the fog adds per unit density (sRGB to linear), not occluded and not lit"; r = Rating::Approximated; }
            else if (p.name == "height_falloff") { why = "density falls by e^(-falloff x height in metres) above the volume's lowest point (Godot's exact reference height is not documented; this one is assumed)"; r = Rating::Approximated; }
            else if (p.name == "edge_fade") { why = "a soft edge: full density from this fraction of the half size inside the shape's surface, ramping linearly to nothing at the surface (Godot's exact falloff curve is not documented)"; r = Rating::Approximated; }
            else if (p.name == "density_texture") { report_.add("resource", doc.file, w.node, type, p.name, Rating::Unsupported, "a 3D density texture is not sampled; the density is uniform inside the shape", p.line); continue; }
            if (why) report_.add("resource", doc.file, w.node, type, p.name, r, why, p.line);
        }
        if (type == "PanoramaSkyMaterial" && p.name == "panorama" && p.value.type == VType::ExtResource) {
            // The file is kept as it is (the renderers do not sample it, but the node holds the reference); .hdr and .exr are not images the engine decodes.
            const gd::GodotExtResource* e = doc.findExt(p.value.s);
            const std::string res = e ? fs_.resolve(*e, &diag_, doc.file) : std::string();
            std::vector<u8> bytes;
            const std::string disk = res.empty() ? std::string() : fs_.toDiskPath(res);
            if (disk.empty() || !readFileBytes(disk, bytes)) {
                report_.add("resource", doc.file, w.node, type, "panorama", Rating::Unsupported, "the panorama file is missing", p.line);
                continue;
            }
            const std::string asset = "Textures/" + (res.rfind("res://", 0) == 0 ? res.substr(6) : res);
            const std::string dst = contentDir_ + "/" + asset;
            createDirectories(std::filesystem::path(dst).parent_path().string());
            if (!writeFileBytes(dst, bytes.data(), bytes.size())) {
                report_.add("resource", doc.file, w.node, type, "panorama", Rating::Unsupported, "could not write " + dst, p.line);
                continue;
            }
            entries.emplace_back(p.name, makeResource(asset));
            continue;
        }
        Var cv = convertValue(p.value, doc, w, p.name);
        if (cv.isNil() && !p.value.isNil()) continue;   // already reported
        entries.emplace_back(p.name, std::move(cv));
    }
    return makeObject(type, std::move(entries));
}

Var Converter::convertValue(const Var& v, const gd::GodotDocBase& doc, const Where& w, const std::string& prop) {
    switch (v.type) {
        case VType::Array: {
            Var out = v;
            for (Var& item : out.items) item = convertValue(item, doc, w, prop);
            return out;
        }
        case VType::Dictionary: {
            Var out = v;
            for (Var& k : out.keys) k = convertValue(k, doc, w, prop);
            for (Var& item : out.items) item = convertValue(item, doc, w, prop);
            return out;
        }
        case VType::Object: {
            Var out = v;
            for (Var& item : out.items) item = convertValue(item, doc, w, prop);
            return out;
        }
        case VType::SubResource: {
            const gd::GodotSubResource* sr = doc.findSub(v.s);
            if (!sr) {
                report_.add("property", doc.file, w.node, w.type, prop, Rating::Unsupported, "the sub-resource " + v.s + " is not declared", v.line);
                return makeNil();
            }
            const std::string& t = sr->type;
            if (isMaterial3DType(t)) {
                const std::string a = materialAsset(doc.file + "#" + sr->id, t, sr->props, doc, stemOf(doc.file) + "_" + sr->id, w);
                return a.empty() ? makeNil() : makeResource(a);
            }
            if (t == "ShaderMaterial") {   // G6 SH2
                const std::string a = shaderMaterialAsset(doc.file + "#" + sr->id, sr->props, doc, stemOf(doc.file) + "_" + sr->id, w);
                return a.empty() ? makeNil() : makeResource(a);
            }
            if (isProceduralTextureType(t)) return bakedTextureValue(v, doc, w);   // F26
            if (t == "ViewportTexture") {
                // G4 R5: a ViewportTexture held as a node or script value keeps its path for Aver.Fabric's ViewportTexture. (A material's texture slot is
                // handled in materialAsset, which writes the engine's texture reference into the .ocmat.)
                const Var* path = findProp(sr->props, "viewport_path");
                report_.add("resource", doc.file, w.node, t, "", Rating::Converted,
                            "kept as Object(ViewportTexture) with its viewport_path; it names the SubViewport relative to its scene and its picture is the view's", sr->line);
                return makeObject("ViewportTexture", {{"viewport_path", path ? *path : makeNil()}});
            }
            if (isPrimitiveMeshType(t)) {
                const std::string a = primitiveMeshAsset(t, sr->props, doc, w);
                return a.empty() ? makeNil() : makeResource(a);
            }
            if (t == "ArrayMesh") {
                const std::string a = arrayMeshAsset(sr->props, doc, w);
                return a.empty() ? makeNil() : makeResource(a);
            }
            if (t == "MultiMesh") return multiMeshObject(sr->props, doc, w);
            if (t == "Animation") {
                const Var* rn = findProp(sr->props, "resource_name");
                const std::string a = animationAsset(doc.file + "#" + sr->id, sr->props, doc, stemOf(doc.file) + "_" + (rn && !rn->asString().empty() ? rn->asString() : sr->id), w);
                return a.empty() ? makeNil() : makeResource(a);
            }
            if (t == "AnimationLibrary") return animationLibraryValue(sr->props, doc, w);
            if (isAnimTreeResourceType(t)) return animTreeObject(t, sr->props, doc, w, sr->line);
            if (isNavigationResourceType(t)) return navigationMeshObject(t, sr->props, doc, w, sr->line);
            if (isReplicationResourceType(t)) return replicationConfigObject(sr->props, doc, w, sr->line);   // G6 NET2
            if (isAudioStreamResourceType(t)) return audioStreamObject(t, sr->props, doc, w, sr->line);
            if (t == "Theme") {   // G5 U1: a theme resource held by a scene becomes an .octheme file
                const std::string a = themeAsset(doc.file + "#" + sr->id, sr->props, doc, stemOf(doc.file) + "_" + sr->id, w);
                return a.empty() ? makeNil() : makeResource(a);
            }
            if (isStyleBoxType(t)) return styleBoxObject(t, sr->props, doc, w, sr->line);
            if (isWidgetResourceType(t)) return widgetResource(t, sr->props, doc, w, sr->line, sr->id);   // G5 U2
            if (t == "ImmediateMesh" || t == "PointMesh" || t == "TextMesh") {
                report_.add("resource", doc.file, w.node, t, "", Rating::Unsupported,
                            t == "TextMesh" ? "TextMesh extrudes glyph outlines; the converter has no font tessellator (a Label3D or a UI label shows text instead)"
                            : t == "ImmediateMesh" ? "an ImmediateMesh holds no stored surfaces: a script fills it every frame (Aver.Fabric's ImmediateMesh does the same at run time)"
                                                   : "a point mesh has no triangles",
                            sr->line);
                return makeNil();
            }
            if (t == "PhysicsMaterial") return physicsMaterialObject(sr->props, doc, w, sr->line);
            if (isOccluderType(t)) return occluderObject(t, sr->props, doc, w, sr->line);
            if (isShapeType(t)) return shapeObject(t, sr->props, doc, w);
            if (t == "ProceduralSkyMaterial" && options_.proceduralSky) {   // F23, opt in
                const std::string a = proceduralSkyAsset(doc.file + "#" + sr->id, sr->props, doc, w);
                if (!a.empty()) return makeResource(a);
            }
            if (isEnvironmentType(t)) return environmentObject(t, sr->props, doc, w);
            return genericResourceDict(t, sr->props, doc, w, "no Aver counterpart for this resource class; its properties are kept as a dictionary");
        }
        case VType::ExtResource: {
            const gd::GodotExtResource* e = doc.findExt(v.s);
            if (!e) {
                report_.add("property", doc.file, w.node, w.type, prop, Rating::Unsupported, "the external resource " + v.s + " is not declared", v.line);
                return makeNil();
            }
            const std::string res = fs_.resolve(*e, &diag_, doc.file);
            const std::string ext = extOf(res);
            const std::string& t = e->type;
            if (res.empty() || !fs_.exists(res)) {
                report_.add("property", doc.file, w.node, w.type, prop, Rating::Unsupported, "the file " + e->path + " does not exist", v.line);
                return makeNil();
            }
            if (t == "Script") {
                report_.add("property", doc.file, w.node, w.type, prop, Rating::Unsupported, "a script held as a value is not converted", v.line);
                return makeNil();
            }
            if (isFontExt(ext)) {   // G5 U1
                const std::string a = fontAsset(res, w);
                return a.empty() ? makeNil() : makeResource(a);
            }
            if (isModelExt(ext)) {
                const ModelResult* m = modelFor(res, w);
                if (!m || m->parts.empty()) return makeNil();
                if (m->parts.size() > 1)
                    report_.add("property", doc.file, w.node, w.type, prop, Rating::Approximated,
                                "the model has " + std::to_string(m->parts.size()) + " meshes; a single mesh property takes the first", v.line);
                return makeResource(m->parts[0].meshAsset);
            }
            if (ext == ".tscn" || ext == ".scn") {
                report_.add("property", doc.file, w.node, w.type, prop, Rating::Approximated,
                            "a PackedScene value points at the level written for that scene; PackedScene.Instantiate is not in the runtime yet", v.line);
                return makeResource(levelAssetFor(res));
            }
            if (isAudioExt(ext)) return audioFileValue(res, t, w, prop, v.line);
            if ((ext == ".tres" || ext == ".res") && isProceduralTextureType(t)) return bakedTextureValue(v, doc, w);   // F26
            if (isImageExt(ext) || isOtherTextureExt(ext) || t.find("Texture") != std::string::npos) {
                const std::string a = copyTexture(res, w);
                return a.empty() ? makeNil() : makeResource(a);
            }
            if (ext == ".tres" || ext == ".res" || fs_.isBinaryResource(res)) {   // a .mesh, .occ or .lmbake is a binary resource like a .res
                const gd::GodotResource* r = resourceDoc(res);
                if (!r) return makeNil();
                const std::string& rt = r->header.type;
                if (isProceduralTextureType(rt)) return bakedTextureValue(v, doc, w);   // F26
                if (isMaterial3DType(rt)) {
                    const std::string a = materialAsset(res, rt, r->props, *r, stemOf(res), w);
                    return a.empty() ? makeNil() : makeResource(a);
                }
                if (rt == "ShaderMaterial") {   // G6 SH2
                    const std::string a = shaderMaterialAsset(res, r->props, *r, stemOf(res), w);
                    return a.empty() ? makeNil() : makeResource(a);
                }
                if (isPrimitiveMeshType(rt)) {
                    const std::string a = primitiveMeshAsset(rt, r->props, *r, w);
                    return a.empty() ? makeNil() : makeResource(a);
                }
                if (rt == "PhysicsMaterial") return physicsMaterialObject(r->props, *r, w, 0);
                if (isOccluderType(rt)) return occluderObject(rt, r->props, *r, w, 0);
                if (isShapeType(rt)) return shapeObject(rt, r->props, *r, w);
                if (rt == "ProceduralSkyMaterial" && options_.proceduralSky) {   // F23, opt in
                    const std::string a = proceduralSkyAsset(res, r->props, *r, w);
                    if (!a.empty()) return makeResource(a);
                }
                if (isEnvironmentType(rt)) return environmentObject(rt, r->props, *r, w);
                if (rt == "ArrayMesh") {
                    const std::string a = arrayMeshAsset(r->props, *r, w);
                    return a.empty() ? makeNil() : makeResource(a);
                }
                if (rt == "MultiMesh") return multiMeshObject(r->props, *r, w);
                if (rt == "Animation") {
                    const std::string a = animationAsset(res, r->props, *r, stemOf(res), w);
                    return a.empty() ? makeNil() : makeResource(a);
                }
                if (rt == "AnimationLibrary") return animationLibraryValue(r->props, *r, w);
                if (isAnimTreeResourceType(rt)) return animTreeObject(rt, r->props, *r, w, 0);
                if (isNavigationResourceType(rt)) return navigationMeshObject(rt, r->props, *r, w, 0);
                if (isReplicationResourceType(rt)) return replicationConfigObject(r->props, *r, w, 0);   // G6 NET2
                if (isAudioStreamResourceType(rt)) return audioStreamObject(rt, r->props, *r, w, 0);
                if (rt == "Theme") {   // G5 U1
                    const std::string a = themeAsset(res, r->props, *r, stemOf(res), w);
                    return a.empty() ? makeNil() : makeResource(a);
                }
                if (isStyleBoxType(rt)) return styleBoxObject(rt, r->props, *r, w, 0);
                if (isWidgetResourceType(rt)) return widgetResource(rt, r->props, *r, w, 0, "");   // G5 U2
                if (rt == "TextMesh" || rt == "ImmediateMesh" || rt == "PointMesh") {
                    report_.add("resource", res, w.node, rt, "", Rating::Unsupported,
                                rt == "TextMesh" ? "TextMesh extrudes glyph outlines; the converter has no font tessellator" : "this mesh class holds no stored triangles to convert", 0);
                    return makeNil();
                }
                return genericResourceDict(rt, r->props, *r, w, "no Aver counterpart for this resource class; its properties are kept as a dictionary");
            }
            if (ext == ".gdshader" || ext == ".shader") return makeResource(res);   // a text shader held as a value (Array[Shader]): its res:// path, loaded as a Shader
            report_.add("resource", res, w.node, t, "", Rating::Unsupported, "a " + t + " resource (" + ext + ") is not converted yet", v.line);
            return makeNil();
        }
        default:
            return v;
    }
}

// ---- resources that no scene had to ask for -------------------------------------------------------------------------------

void Converter::convertStandaloneResources() {
    for (const std::string& res : fs_.files()) {
        const std::string ext = extOf(res);
        const Where w{res, "", ""};
        if (ext == ".tres" || ext == ".res" || fs_.isBinaryResource(res)) {   // a binary .res (or a .mesh, .occ ... with its magic) reads into the same model (GodotBinary.hpp)
            const gd::GodotResource* r = resourceDoc(res);
            if (!r) continue;
            const std::string& t = r->header.type;
            if (isMaterial3DType(t)) { materialAsset(res, t, r->props, *r, stemOf(res), w); continue; }
            if (isPrimitiveMeshType(t)) { primitiveMeshAsset(t, r->props, *r, w); continue; }
            if (t == "ArrayMesh") { arrayMeshAsset(r->props, *r, w); continue; }
            if (t == "Animation") { animationAsset(res, r->props, *r, stemOf(res), w); continue; }
            if (t == "AnimationLibrary") { animationLibraryValue(r->props, *r, w); continue; }
            if (isAnimTreeResourceType(t)) {
                report_.add("resource", res, "", t, "", Rating::Converted, "converted where a scene uses it", 0);
                continue;
            }
            if (isNavigationResourceType(t) || isReplicationResourceType(t)) {
                report_.add("resource", res, "", t, "", Rating::Converted, "converted where a scene uses it", 0);
                continue;
            }
            if (t == "AudioBusLayout") { convertBusLayout(res, *r); continue; }
            if (t == "Theme") { themeAsset(res, r->props, *r, stemOf(res), w); continue; }   // G5 U1: every theme is written, whether or not a scene names it
            if (isStyleBoxType(t) || isWidgetResourceType(t)) {
                report_.add("resource", res, "", t, "", Rating::Converted, "converted where a scene or theme uses it", 0);
                continue;
            }
            if (isAudioStreamResourceType(t)) {
                report_.add("resource", res, "", t, "", Rating::Converted, "converted where a scene uses it", 0);
                continue;
            }
            if (t.rfind("AudioEffect", 0) == 0) {
                report_.add("resource", res, "", t, "", Rating::Converted, "an effect resource converts inside the bus layout that holds it", 0);
                continue;
            }
            if (t == "MultiMesh" || t == "MeshLibrary") {
                report_.add("resource", res, "", t, "", Rating::Converted, "converted where a scene uses it", 0);
                continue;
            }
            if (t == "ShaderMaterial")
                shaderMaterialAsset(res, r->props, *r, stemOf(res), w);   // G6 SH2: every shader material is written, whether or not a scene names it
            else if (isShapeType(t) || t == "PhysicsMaterial" || t == "SpriteFrames" || t == "ParticleProcessMaterial")
                report_.add("resource", res, "", t, "", Rating::Converted, "converted where a scene uses it", 0);
            else if (isOccluderType(t))
                report_.add("resource", res, "", t, "", Rating::Approximated, "kept as Aver.Fabric's " + t + " where a scene uses it; occlusion culling is automatic", 0);
            else if (resourceScript(r->props, *r))
                customResourceFile(res, t, *r, w);
            else
                report_.add("resource", res, "", t, "", Rating::Unsupported, "a " + t + " resource has no Aver counterpart yet", 0);
        } else if (isModelExt(ext)) {
            modelFor(res, w);
        } else if (isAudioExt(ext)) {
            audioFileAsset(res, w);   // every sound is copied, so a script's load("res://x.wav") finds it too
        } else if (isFontExt(ext)) {
            fontAsset(res, w);        // G5 U1: every font file is copied, so a theme or a script's load("res://ui.ttf") finds it
        }
    }
    // Textures last, so the ones a material already carried into Content/Textures/<Name>/ are not copied a second time.
    for (const std::string& res : fs_.files()) {
        const std::string ext = extOf(res);
        if ((isImageExt(ext) || isOtherTextureExt(ext)) && !materialTextureSources_.count(res)) copyTexture(res, Where{res, "", ""});
    }
}

void Converter::reportProjectFiles() {
    // Binary .scn / .res files are read by the binary reader into the text model; the scene and resource rows say what became of their contents.
    for (const std::string& res : fs_.binaryResources()) {
        const bool scene = extOf(res) == ".scn";
        const bool read = scene ? sceneDoc(res) != nullptr : resourceDoc(res) != nullptr;
        if (read)
            report_.add("file", res, "", scene ? "PackedScene" : "", "", Rating::Converted, "a binary Godot " + std::string(scene ? "scene" : "resource") + " (RSRC): read like its text form (.tscn / .tres)", 0);
        else
            report_.add("file", res, "", "", "", Rating::Unsupported, "a binary Godot resource that could not be read (see the entries above); resave it as text (.tscn / .tres) and convert again", 0);
    }
    for (const std::string& res : fs_.files()) {
        const std::string ext = extOf(res);
        if (ext == ".gdshader") {   // G6 SH2: the shaders a ShaderMaterial used are reported where they were prepared
            if (!shaders_.count(res))
                report_.add("file", res, "", "Shader", "", Rating::Approximated, "no ShaderMaterial uses this shader, so nothing was written for it", 0);
        } else if (ext == ".gdshaderinc") {
            report_.add("file", res, "", "Shader", "", Rating::Converted, "included into the shaders that name it", 0);
        } else if (ext == ".gdextension")
            continue;   // FabricExtensions.cpp reports it
        else if (ext == ".wav" || ext == ".ogg" || ext == ".mp3")
            continue;   // FabricAudio.cpp copies and reports it
        else if (ext == ".ttf" || ext == ".otf" || ext == ".woff" || ext == ".woff2")
            continue;   // FabricUi.cpp copies and reports it
        else if (ext == ".fnt")
            report_.add("file", res, "", "Font", "", Rating::Unsupported, "bitmap font files (.fnt) are not converted", 0);
        else if (ext == ".ogv" || ext == ".webm")
            report_.add("file", res, "", "VideoStream", "", Rating::Unsupported,
                        "the engine has no video decoder (modules/media only encodes H.264 through Media Foundation); a VideoStreamPlayer needs a decoder dependency, see docs/AUDIO.md", 0);
        else if (ext == ".translation" || ext == ".csv")
            report_.add("file", res, "", "Translation", "", Rating::Unsupported, "only .po translations are converted", 0);
        else if (ext == ".theme")
            report_.add("file", res, "", "Theme", "", Rating::Unsupported, "a binary Godot theme file: resave it as a text .tres and convert again", 0);
        else if (ext == ".anim" || ext == ".animlib")
            report_.add("file", res, "", "Animation", "", Rating::Unsupported, "a binary Godot animation file: resave it as a text .tres and convert again", 0);
    }
}

}  // namespace aver::fabric
