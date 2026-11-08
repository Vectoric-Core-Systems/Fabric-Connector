// Terrain3D (the TokisanGames GDExtension) onto the engine's own landscape (modules/landscape), not onto the GDExtension host.
//
// A Terrain3D node names a data_directory of Terrain3DRegion resources (terrain3d<sx><XX><sy><YY>.res, sign '-' or '_'); each holds region_size x region_size
// height_map (RF, metres), control_map (RF carrying 32 packed bits) and color_map (RGBA8) images. Each region becomes one .ocland section, one LANDSCAPE record
// and, from the Terrain3DAssets texture list, one layered terrain material (TERRAIN / LAYER records) shared by every section of the node.
//
// SPACE. Terrain3D vertex (px, pz) of region (rx, ry) is at Godot ((rx*R + px)*vs, h, (ry*R + pz)*vs). The engine's axis map (aver/formats/AxisMap.hpp) sends Godot
// +X to engine +Y and Godot +Z to engine -X, so a section's ix runs along Godot -Z and iy along Godot +X: sample (ix, iy) = pixel (px = iy, pz = R - ix). A section
// has R + 1 samples a side (neighbouring sections share an edge row), the last row and column being the neighbour region's first pixel, which is the gap Terrain3D
// stitches between regions; so a section spans exactly the region's R * vs metres and the sections tile edge to edge.
#include "FabricInternal.hpp"

#include "FabricProcTex.hpp"

#include "aver/formats/AxisMap.hpp"
#include "aver/formats/OcLand.hpp"
#include "aver/formats/OcWorld.hpp"
#include "aver/platform/FileSystem.hpp"

#if AVER_HAVE_MATERIAL_COMPILE
#include "aver/formats/OcMat.hpp"
#endif

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <filesystem>

namespace aver::fabric {

namespace {

constexpr int kBlendMax = 255;

const Var* propOf(const std::vector<gd::GodotProperty>& props, const char* name) {
    for (const gd::GodotProperty& p : props)
        if (p.name == name) return &p.value;
    return nullptr;
}

const Var* nodeProp(const FNode& fn, const char* name) {
    for (const Prop& p : fn.props)
        if (p.key == name) return &p.value;
    return nullptr;
}

// A resource of a document: its type, properties and the document that owns them.
struct Res {
    std::string type, file;
    const std::vector<gd::GodotProperty>* props = nullptr;
    const gd::GodotDocBase* doc = nullptr;
};

// Godot's Image resource stores {width, height, format, mipmaps, data}.
struct Img {
    int w = 0, h = 0;
    std::string format;
    const std::vector<u8>* data = nullptr;
};

int channelsOf(const std::string& f) {
    if (f == "L8") return 1;
    if (f == "LA8") return 2;
    if (f == "RGB8") return 3;
    if (f == "RGBA8") return 4;
    return 0;
}

float halfToFloat(u16 h) {
    const u32 s = (h >> 15) & 1u, e = (h >> 10) & 0x1Fu, m = h & 0x3FFu;
    float v;
    if (e == 0) v = std::ldexp(static_cast<float>(m), -24);
    else if (e == 31) v = m ? NAN : INFINITY;
    else v = std::ldexp(static_cast<float>(m | 0x400u), static_cast<int>(e) - 25);
    return s ? -v : v;
}

// The engine wants [A-Za-z0-9_]{1,31}, unique.
std::string layerNameOf(const std::string& raw, int id, std::set<std::string>& used) {
    std::string n;
    for (const char c : raw)
        if (std::isalnum(static_cast<unsigned char>(c)) || c == '_') n.push_back(c);
        else if (c == ' ' || c == '-') n.push_back('_');
    if (n.empty()) n = "Texture" + std::to_string(id);
    if (n.size() > 31) n.resize(31);
    if (used.count(n)) {
        const std::string suffix = "_" + std::to_string(id);
        n = n.substr(0, 31 - suffix.size()) + suffix;
    }
    for (int k = 2; used.count(n); ++k) {
        const std::string suffix = "_" + std::to_string(k);
        n = n.substr(0, 31 - suffix.size()) + suffix;
    }
    used.insert(n);
    return n;
}

struct Region {
    int rx = 0, ry = 0;
    int size = 0;
    double spacing = 1.0;
    std::string res;
    std::vector<float> height;   // size * size, metres
    std::vector<u32> control;    // size * size, or empty
    bool colorTinted = false;    // the colour map holds anything but white
    size_t instances = 0;        // the instancer's transforms
};

// Terrain3D's location_to_filename: "terrain3d" + ('-' or '_') + digits + ('-' or '_') + digits.
bool locationOf(const std::string& file, int& x, int& y) {
    std::string base = file.substr(file.find_last_of('/') + 1);
    const size_t dot = base.find_last_of('.');
    if (dot == std::string::npos) return false;
    const std::string ext = lower(base.substr(dot));
    if (ext != ".res" && ext != ".tres") return false;
    base = lower(base.substr(0, dot));
    if (base.rfind("terrain3d", 0) != 0) return false;
    size_t i = 9;
    int v[2] = {0, 0};
    for (int k = 0; k < 2; ++k) {
        if (i >= base.size() || (base[i] != '-' && base[i] != '_')) return false;
        const bool neg = base[i++] == '-';
        size_t digits = 0;
        int n = 0;
        while (i < base.size() && base[i] >= '0' && base[i] <= '9') { n = n * 10 + (base[i++] - '0'); ++digits; }
        if (!digits) return false;
        v[k] = neg ? -n : n;
    }
    if (i != base.size()) return false;
    x = v[0];
    y = v[1];
    return true;
}

std::string locationName(int x, int y) {
    char b[48];
    std::snprintf(b, sizeof b, "x%c%02d_y%c%02d", x < 0 ? 'n' : 'p', std::abs(x), y < 0 ? 'n' : 'p', std::abs(y));
    return b;
}

// One texel's paint: base id, overlay id and the overlay's weight 0..255.
struct Paint { int base = 0, overlay = 0, blend = 0; };

}  // namespace

void Converter::convertTerrain3D(const FNode& fn, fmt::OcWorldData& level) {
    const auto rate = [&](const std::string& prop, Rating r, const std::string& why) { report_.add("property", fn.reportFile, fn.reportPath, "Terrain3D", prop, r, why, fn.line); };
    const auto note = [&](const std::string& prop, Rating r, const std::string& why) { report_.add("node", fn.reportFile, fn.reportPath, "Terrain3D", prop, r, why, fn.line); };

    std::set<std::string> rated;
    const auto has = [&](const char* k) { return nodeProp(fn, k) != nullptr; };
    const auto rateIf = [&](const char* k, Rating r, const std::string& why) { if (has(k)) { rate(k, r, why); rated.insert(k); } };

    std::string dir = stringOf(nodeProp(fn, "data_directory"));
    while (!dir.empty() && dir.back() == '/') dir.pop_back();
    if (dir.empty()) {
        rate("data_directory", Rating::Unsupported, "no data_directory: the terrain is empty or kept in the scene, which has no Aver counterpart; nothing was converted");
        note("", Rating::Unsupported, "Terrain3D has no data_directory; no landscape written");
        return;
    }
    dir = gd::GodotProjectFs::normalizeRes(dir);

    const std::string cacheKey = dir + "|" + fn.name;
    if (terrainResults_.find(cacheKey) == terrainResults_.end()) {
        TerrainResult result;

        // ---- the regions ----
        std::map<std::pair<int, int>, Region> regions;
        size_t skipped = 0, instancerTransforms = 0, holes = 0, navTexels = 0, uvBits = 0, tintedRegions = 0, files = 0;
        std::set<double> spacings;
        std::set<int> sizes;
        const Var* nodeSpacing = nodeProp(fn, "vertex_spacing");
        for (const std::string& res : fs_.files()) {
            if (res.rfind(dir + "/", 0) != 0) continue;
            ++files;
            int rx = 0, ry = 0;
            if (!locationOf(res, rx, ry)) continue;
            const gd::GodotResource* r = resourceDoc(res);
            if (!r || r->header.type != "Terrain3DRegion") {
                if (r) report_.add("resource", res, "", r->header.type, "", Rating::Unsupported, "not a Terrain3DRegion; skipped", 0);
                ++skipped;
                continue;
            }
            terrainRegionFiles_.insert(res);
            Region reg;
            reg.rx = rx;
            reg.ry = ry;
            reg.res = res;
            const auto image = [&](const char* prop, Img& img) {
                const Var* v = propOf(r->props, prop);
                if (!v) return false;
                const Var* d = nullptr;
                if (v->type == VType::SubResource) {
                    if (const gd::GodotSubResource* sr = r->findSub(v->s)) d = propOf(sr->props, "data");
                } else if (v->type == VType::Dictionary) d = v;
                if (!d || d->type != VType::Dictionary) return false;
                const Var* wv = d->find("width");
                const Var* hv = d->find("height");
                const Var* fv = d->find("format");
                const Var* bv = d->find("data");
                if (!wv || !hv || !fv || !bv || bv->type != VType::PackedByteArray) return false;
                img.w = static_cast<int>(wv->asInt());
                img.h = static_cast<int>(hv->asInt());
                img.format = fv->asString();
                img.data = &bv->bytes;
                return true;
            };
            Img hm, cm, col;
            if (!image("height_map", hm)) {
                report_.add("resource", res, "", "Terrain3DRegion", "height_map", Rating::Unsupported, "the region has no readable height_map image; the region is skipped", 0);
                ++skipped;
                continue;
            }
            const Var* sizeProp = propOf(r->props, "region_size");
            reg.size = sizeProp ? static_cast<int>(sizeProp->asInt()) : hm.w;
            const Var* spacingProp = propOf(r->props, "vertex_spacing");
            reg.spacing = nodeSpacing ? numOf(nodeSpacing, 1.0) : (spacingProp ? spacingProp->asNumber(1.0) : 1.0);
            if (reg.size < 2 || hm.w != reg.size || hm.h != reg.size || reg.size + 1 > static_cast<int>(fmt::kOcLandMaxSamples) || !(reg.spacing > 0)) {
                report_.add("resource", res, "", "Terrain3DRegion", "region_size", Rating::Unsupported,
                            "the height map is " + std::to_string(hm.w) + "x" + std::to_string(hm.h) + " for region_size " + std::to_string(reg.size) +
                                "; the engine's section holds 2 to 4097 samples a side (a region of at most 4096); the region is skipped",
                            0);
                ++skipped;
                continue;
            }
            const size_t n = static_cast<size_t>(reg.size) * reg.size;
            const bool half = hm.format == "RH";
            if ((hm.format != "RF" && !half) || hm.data->size() < n * (half ? 2 : 4)) {
                report_.add("resource", res, "", "Terrain3DRegion", "height_map", Rating::Unsupported, "height map format " + hm.format + " is not RF or RH, or the data is short; the region is skipped", 0);
                ++skipped;
                continue;
            }
            reg.height.resize(n);
            for (size_t i = 0; i < n; ++i) {
                if (half) { u16 hv; std::memcpy(&hv, hm.data->data() + i * 2, 2); reg.height[i] = halfToFloat(hv); }
                else std::memcpy(&reg.height[i], hm.data->data() + i * 4, 4);
                if (!std::isfinite(reg.height[i])) reg.height[i] = 0.0f;
            }
            if (image("control_map", cm) && cm.format == "RF" && cm.w == reg.size && cm.h == reg.size && cm.data->size() >= n * 4) {
                reg.control.resize(n);
                std::memcpy(reg.control.data(), cm.data->data(), n * 4);
            }
            if (image("color_map", col) && col.format == "RGBA8" && col.w == reg.size && col.h == reg.size && col.data->size() >= n * 4) {
                // Terrain3D's neutral colour is opaque white (alpha is the roughness change); anything else is a tint or painted roughness.
                for (size_t i = 0; i < n && !reg.colorTinted; ++i) {
                    const u8* p = col.data->data() + i * 4;
                    if (p[0] != 255 || p[1] != 255 || p[2] != 255 || p[3] != 255) reg.colorTinted = true;
                }
                if (reg.colorTinted) ++tintedRegions;
            }
            if (const Var* inst = propOf(r->props, "instances"); inst && inst->type == VType::Dictionary)
                for (const Var& v : inst->items) reg.instances += v.count();
            instancerTransforms += reg.instances;
            spacings.insert(reg.spacing);
            sizes.insert(reg.size);
            if (!regions.emplace(std::make_pair(rx, ry), std::move(reg)).second)
                report_.add("resource", res, "", "Terrain3DRegion", "", Rating::Unsupported, "a second region at the same location; ignored", 0);
        }
        if (regions.empty()) {
            rate("data_directory", Rating::Unsupported, "no Terrain3DRegion resources in " + dir + (files ? "" : " (the folder is missing or empty)") + "; nothing was converted");
            note("", Rating::Unsupported, "no regions found in " + dir);
            return;
        }
        if (spacings.size() > 1 || sizes.size() > 1)
            note("region_size", Rating::Approximated, "regions of different sizes or vertex spacings are written as they are, one section each; they do not tile exactly");

        // ---- the texture list ----
        struct Tex { int id = 0; std::string name; Res res; };
        std::vector<Tex> textures;
        const auto resolve = [&](const Var& v, const gd::GodotDocBase& doc, Res& out) {
            if (v.type == VType::SubResource) {
                const gd::GodotSubResource* sr = doc.findSub(v.s);
                if (!sr) return false;
                out = {sr->type, doc.file + "#" + sr->id, &sr->props, &doc};
                return true;
            }
            if (v.type == VType::ExtResource) {
                const gd::GodotExtResource* e = doc.findExt(v.s);
                if (!e) return false;
                const std::string res = fs_.resolve(*e, &diag_, doc.file);
                const size_t dot = res.find_last_of('.');
                const std::string ext = dot == std::string::npos ? std::string() : lower(res.substr(dot));
                if (res.empty() || (ext != ".tres" && ext != ".res")) return false;
                const gd::GodotResource* r = resourceDoc(res);
                if (!r) return false;
                out = {r->header.type, res, &r->props, r};
                return true;
            }
            return false;
        };
        Res assetsRes, materialRes;
        bool haveAssets = false, haveMaterial = false;
        for (const Prop& p : fn.props) {
            if (p.key == "assets" && p.doc) haveAssets = resolve(p.value, *p.doc, assetsRes) && assetsRes.type == "Terrain3DAssets";
            if (p.key == "material" && p.doc) haveMaterial = resolve(p.value, *p.doc, materialRes) && materialRes.type == "Terrain3DMaterial";
        }
        if (haveAssets)
            if (const Var* list = propOf(*assetsRes.props, "texture_list"))
                for (const Var& item : list->items) {
                    Tex t;
                    if (!resolve(item, *assetsRes.doc, t.res) || t.res.type != "Terrain3DTextureAsset") continue;
                    t.id = static_cast<int>(numOf(propOf(*t.res.props, "id"), 0));
                    t.name = stringOf(propOf(*t.res.props, "name"));
                    textures.push_back(std::move(t));
                }

        // ---- the material's auto shader (flat ground and slopes pick their own textures) ----
        const Var* shaderParams = haveMaterial ? propOf(*materialRes.props, "_shader_parameters") : nullptr;
        const auto shaderParam = [&](const char* k) -> const Var* { return shaderParams && shaderParams->type == VType::Dictionary ? shaderParams->find(k) : nullptr; };
        const bool autoShader = haveMaterial && boolOf(propOf(*materialRes.props, "auto_shader"), false);
        const int autoBase = static_cast<int>(numOf(shaderParam("auto_base_texture"), 0));
        const int autoOverlay = static_cast<int>(numOf(shaderParam("auto_overlay_texture"), 1));
        const double autoSlope = numOf(shaderParam("auto_slope"), 1.0);

        // control_map bits: base 27..31, overlay 22..26, blend 14..21, uv rotation 10..13, uv scale 7..9, hole 2, navigation 1, auto 0.
        const auto paintAt = [&](const Region& g, int x, int z) {
            Paint p;
            const u32 c = g.control.empty() ? 0u : g.control[static_cast<size_t>(z) * g.size + x];
            p.base = static_cast<int>((c >> 27) & 0x1Fu);
            p.overlay = static_cast<int>((c >> 22) & 0x1Fu);
            p.blend = static_cast<int>((c >> 14) & 0xFFu);
            if (autoShader && (c & 1u)) {
                const auto h = [&](int px, int pz) { return static_cast<double>(g.height[static_cast<size_t>(std::clamp(pz, 0, g.size - 1)) * g.size + std::clamp(px, 0, g.size - 1)]); };
                const double dx = (h(x + 1, z) - h(x - 1, z)) / (2.0 * g.spacing), dz = (h(x, z + 1) - h(x, z - 1)) / (2.0 * g.spacing);
                const double ny = 1.0 / std::sqrt(1.0 + dx * dx + dz * dz);
                const double flat = std::clamp(autoSlope * 2.0 * (ny - 1.0) + 1.0, 0.0, 1.0);
                p.base = autoBase;
                p.overlay = autoOverlay;
                p.blend = static_cast<int>(std::lround((1.0 - flat) * kBlendMax));
            }
            return p;
        };

        // ---- which textures are used, and by how much ----
        std::map<int, double> weightOf;
        for (const auto& [loc, g] : regions) {
            (void)loc;
            for (int z = 0; z < g.size; ++z)
                for (int x = 0; x < g.size; ++x) {
                    const u32 c = g.control.empty() ? 0u : g.control[static_cast<size_t>(z) * g.size + x];
                    if ((c >> 2) & 1u) ++holes;
                    if ((c >> 1) & 1u) ++navTexels;
                    if (((c >> 7) & 0x7Fu) != 0) ++uvBits;
                    const Paint p = paintAt(g, x, z);
                    weightOf[p.base] += kBlendMax - p.blend;
                    if (p.blend) weightOf[p.overlay] += p.blend;
                }
        }
        std::vector<int> layerIds;   // texture ids that become layers, ascending; layer 0 is the first
        std::vector<int> dropped;
        {
            std::vector<std::pair<double, int>> byWeight;
            for (const Tex& t : textures)
                if (const auto it = weightOf.find(t.id); it != weightOf.end() && it->second > 0)
                    if (std::none_of(byWeight.begin(), byWeight.end(), [&](const auto& e) { return e.second == t.id; })) byWeight.emplace_back(it->second, t.id);
            std::sort(byWeight.begin(), byWeight.end(), [](const auto& a, const auto& b) { return a.first != b.first ? a.first > b.first : a.second < b.second; });
            for (size_t i = 0; i < byWeight.size(); ++i) (i < fmt::kOcLandMaxLayers ? layerIds : dropped).push_back(byWeight[i].second);
            std::sort(layerIds.begin(), layerIds.end());
            if (layerIds.empty() && !textures.empty()) layerIds.push_back(textures.front().id);   // never painted: the first texture is the base
        }
        const auto layerOf = [&](int id) {
            const auto it = std::find(layerIds.begin(), layerIds.end(), id);
            return it == layerIds.end() ? 0 : static_cast<int>(it - layerIds.begin());   // an id with no layer folds into the base layer
        };

        // ---- the layered terrain material ----
        std::vector<std::string> layerNames;
        {
            std::set<std::string> used;
            for (const int id : layerIds) {
                const auto t = std::find_if(textures.begin(), textures.end(), [&](const Tex& e) { return e.id == id; });
                layerNames.push_back(layerNameOf(t == textures.end() ? std::string() : t->name, id, used));
            }
        }
#if AVER_HAVE_MATERIAL_COMPILE
        if (!layerIds.empty()) {
            pbr::MaterialDesc d;
            const std::string stem = name_ + "_" + safeName(fn.name, "Terrain3D") + "_terrain";
            d.name = stem;
            const double blendSharp = std::clamp(numOf(shaderParam("blend_sharpness"), 0.87), 0.0, 1.0);
            const bool heightBlend = !haveMaterial || boolOf(shaderParam("height_blending"), true);
            d.terrain.blendDepth = heightBlend ? static_cast<float>(0.05 + 0.4 * (1.0 - blendSharp)) : 1.0f;
            const std::string nodeKey = safeName(name_ + "_" + fn.name, "Terrain3D");
            // A texture slot: a picture file, or an image the file keeps, as an asset path under Content ("" when it cannot be read).
            const auto textureAsset = [&](const Tex& t, const char* prop, const char* what) -> std::string {
                const Var* v = propOf(*t.res.props, prop);
                if (!v || v->isNil()) return {};
                const gd::GodotDocBase& doc = *t.res.doc;
                const Where tw{t.res.file, "", "Terrain3DTextureAsset"};
                if (v->type == VType::ExtResource) {
                    const gd::GodotExtResource* e = doc.findExt(v->s);
                    const std::string res = e ? fs_.resolve(*e, &diag_, doc.file) : std::string();
                    if (res.empty()) {
                        report_.add("property", t.res.file, "", "Terrain3DTextureAsset", prop, Rating::Unsupported, std::string("the ") + what + " texture " + v->s + " could not be resolved", 0);
                        return {};
                    }
                    return copyTexture(res, tw);
                }
                const Var* imgDict = nullptr;
                if (v->type == VType::SubResource)
                    if (const gd::GodotSubResource* sr = doc.findSub(v->s)) {
                        if (sr->type == "ImageTexture") {
                            if (const Var* iv = propOf(sr->props, "image"); iv && iv->type == VType::SubResource)
                                if (const gd::GodotSubResource* ir = doc.findSub(iv->s)) imgDict = propOf(ir->props, "data");
                        } else if (sr->type == "Image") imgDict = propOf(sr->props, "data");
                    }
                if (!imgDict || imgDict->type != VType::Dictionary) {
                    report_.add("property", t.res.file, "", "Terrain3DTextureAsset", prop, Rating::Unsupported, std::string("the ") + what + " texture is neither a picture file nor an image kept in the file", 0);
                    return {};
                }
                const Var* wv = imgDict->find("width");
                const Var* hv = imgDict->find("height");
                const Var* fv = imgDict->find("format");
                const Var* bv = imgDict->find("data");
                const int ch = fv ? channelsOf(fv->asString()) : 0;
                const int iw = wv ? static_cast<int>(wv->asInt()) : 0, ih = hv ? static_cast<int>(hv->asInt()) : 0;
                if (!ch || iw <= 0 || ih <= 0 || !bv || bv->bytes.size() < static_cast<size_t>(iw) * ih * ch) {
                    report_.add("property", t.res.file, "", "Terrain3DTextureAsset", prop, Rating::Unsupported,
                                std::string("the ") + what + " image format " + (fv ? fv->asString() : "?") + " is not an uncompressed L8, LA8, RGB8 or RGBA8 picture", 0);
                    return {};
                }
                proctex::Picture pic;
                pic.width = iw;
                pic.height = ih;
                pic.rgba.resize(static_cast<size_t>(iw) * ih * 4);
                for (size_t i = 0; i < static_cast<size_t>(iw) * ih; ++i) {
                    const u8* s = bv->bytes.data() + i * ch;
                    u8* o = pic.rgba.data() + i * 4;
                    o[0] = s[0];
                    o[1] = ch >= 3 ? s[1] : s[0];
                    o[2] = ch >= 3 ? s[2] : s[0];
                    o[3] = ch == 4 ? s[3] : (ch == 2 ? s[1] : 255);
                }
                std::vector<u8> png;
                if (!proctex::encodePng(pic, png)) return {};
                const std::string gen = "gen://" + nodeKey + "_tex" + std::to_string(t.id) + "_" + prop + "_" + fnvHex(t.res.file);
                generatedTextures_[gen] = std::move(png);
                return generatedTextureAsset(gen);
            };
            for (size_t i = 0; i < layerIds.size(); ++i) {
                const auto t = std::find_if(textures.begin(), textures.end(), [&](const Tex& e) { return e.id == layerIds[i]; });
                pbr::TerrainLayerDesc layer;
                layer.name = layerNames[i];
                if (t != textures.end()) {
                    layer.baseColor.path = textureAsset(*t, "albedo_texture", "albedo");
                    layer.normal.path = textureAsset(*t, "normal_texture", "normal");
                    const double uvScale = numOf(propOf(*t->res.props, "uv_scale"), 0.1);
                    layer.tilingCm = static_cast<float>(uvScale > 0 ? 100.0 / uvScale : 1000.0);
                    const std::string where = "texture " + std::to_string(t->id) + (t->name.empty() ? "" : " (" + t->name + ")");
                    report_.add("resource", t->res.file, "", "Terrain3DTextureAsset", "", Rating::Converted,
                                where + " became layer " + std::to_string(i) + " " + layer.name + ", " + std::to_string(static_cast<int>(layer.tilingCm)) + " cm per tile", 0);
                    const auto asset = [&](const char* k, Rating r, const std::string& why) { report_.add("property", t->res.file, "", "Terrain3DTextureAsset", k, r, where + ": " + why, 0); };
                    if (const Var* ac = propOf(*t->res.props, "albedo_color"); ac && ac->nums.size() >= 3 && (ac->nums[0] != 1 || ac->nums[1] != 1 || ac->nums[2] != 1))
                        asset("albedo_color", Rating::Unsupported, "the albedo tint is not applied; the layer draws its picture as it is");
                    if (propOf(*t->res.props, "normal_texture"))
                        asset("normal_texture", Rating::Approximated, "Terrain3D packs roughness into the normal map's alpha; the normal map is used and its alpha ignored, so roughness comes from the material");
                    for (const char* k : {"roughness", "ao_strength", "normal_depth"})
                        if (const Var* v = propOf(*t->res.props, k); v && v->asNumber(0) != 0.0) asset(k, Rating::Unsupported, "the layered terrain material has no per-layer adjustment of this");
                    for (const char* k : {"uv_rotation", "detiling_rotation", "detiling_shift"})
                        if (const Var* v = propOf(*t->res.props, k); v && v->asNumber(0) != 0.0) asset(k, Rating::Unsupported, "no per-layer rotation or detiling; the layer tiles plainly");
                    if (boolOf(propOf(*t->res.props, "vertical_projection"), false)) asset("vertical_projection", Rating::Unsupported, "no triplanar projection on steep slopes");
                }
                d.terrain.layers.push_back(std::move(layer));
            }
            createDirectories(contentDir_ + "/Materials");
            const std::string asset = "Materials/" + stem + ".ocmat";
            std::string err;
            if (fmt::saveOcmat(contentDir_ + "/" + asset, d, nullptr, &err)) {
                result.material = stem;
                report_.add("resource", haveMaterial ? materialRes.file : fn.reportFile, "", "Terrain3DMaterial", "", Rating::Approximated,
                            "written as " + asset + ": a layered terrain material of " + std::to_string(layerIds.size()) +
                                " layer(s) from the texture list, height-blended (blend_sharpness mapped to the blend depth); Terrain3D's own shader is not carried over",
                            0);
            } else {
                report_.add("resource", fn.reportFile, "", "Terrain3DMaterial", "", Rating::Unsupported, "the terrain material could not be written: " + err, 0);
            }
        }
#else
        if (!layerIds.empty()) report_.add("resource", fn.reportFile, "", "Terrain3DMaterial", "", Rating::Unsupported, "this build of the converter has no material writer; the sections are unpainted", 0);
#endif
        if (textures.empty())
            report_.add("resource", fn.reportFile, fn.reportPath, "Terrain3DAssets", "texture_list", Rating::Unsupported,
                        haveAssets ? "the texture list is empty: the landscape has no layers and draws the renderer's flat colour"
                                   : "no Terrain3DAssets resource could be read: the landscape has no layers and draws the renderer's flat colour",
                        fn.line);
        if (!dropped.empty()) {
            std::string names;
            for (const int id : dropped) names += (names.empty() ? "" : ", ") + std::to_string(id);
            report_.add("resource", fn.reportFile, fn.reportPath, "Terrain3DAssets", "texture_list", Rating::Approximated,
                        "the engine's landscape paints at most " + std::to_string(fmt::kOcLandMaxLayers) + " layers; the least used textures (ids " + names + ") fold into the base layer", fn.line);
        }
        if (haveMaterial) {
            static const std::set<std::string> kHandled = {"auto_base_texture", "auto_overlay_texture", "auto_slope", "blend_sharpness", "height_blending"};
            if (shaderParams && shaderParams->type == VType::Dictionary) {
                std::set<std::string> groups;
                for (const Var& k : shaderParams->keys) {
                    const std::string key = k.asString();
                    if (kHandled.count(key)) continue;
                    const std::string group = key.rfind("macro_variation", 0) == 0 || key.rfind("noise", 0) == 0 || key == "enable_macro_variation" ? "macro variation"
                                              : key.rfind("world_noise", 0) == 0                                                                  ? "world noise"
                                              : key.rfind("projection", 0) == 0 || key == "enable_projection"                                    ? "projection"
                                                                                                                                                  : key;
                    if (!groups.insert(group).second) continue;
                    report_.add("property", materialRes.file, "", "Terrain3DMaterial", group, Rating::Unsupported,
                                "Terrain3D's shader setting has no counterpart in the layered terrain material; the engine's own shader draws the landscape", 0);
                }
            }
            if (autoShader)
                report_.add("property", materialRes.file, "", "Terrain3DMaterial", "auto_shader", Rating::Approximated,
                            "texels flagged auto take the auto base texture on flat ground and the auto overlay on slopes by the slope of the heights (auto_slope); auto_height_reduction is not used; the paint is baked at convert time",
                            0);
            if (const Var* v = propOf(*materialRes.props, "world_background"); v && v->asNumber(0) != 0.0)
                report_.add("property", materialRes.file, "", "Terrain3DMaterial", "world_background", Rating::Unsupported, "no background terrain beyond the regions", 0);
        }

        // ---- sections ----
        // Nearest the origin first: the runtime currently holds the first LANDSCAPE record of a level.
        std::vector<const Region*> order;
        for (const auto& [loc, g] : regions) { (void)loc; order.push_back(&g); }
        std::sort(order.begin(), order.end(), [](const Region* a, const Region* b) {
            const int da = std::abs(a->rx) + std::abs(a->ry), db = std::abs(b->rx) + std::abs(b->ry);
            return da != db ? da < db : (a->ry != b->ry ? a->ry < b->ry : a->rx < b->rx);
        });
        const std::string folder = "Landscape/" + safeName(level.name.empty() ? std::string("level") : level.name, "level");
        double minH = 1e30, maxH = -1e30;
        for (const Region* g : order) {
            const int R = g->size, N = R + 1;
            // The neighbours a section's last row and column read; a missing one repeats the region's own edge.
            const auto neighbour = [&](int dx, int dz) -> const Region* {
                const auto it = regions.find({g->rx + dx, g->ry + dz});
                return it == regions.end() ? nullptr : &it->second;
            };
            const Region* nb[2][2] = {{g, neighbour(1, 0)}, {neighbour(0, 1), neighbour(1, 1)}};   // [pz == R][px == R]
            struct Src { const Region* r; int x, z; };
            const auto src = [&](int px, int pz) {
                const int ox = px == R ? 1 : 0, oz = pz == R ? 1 : 0;
                if (const Region* r = nb[oz][ox]) return Src{r, ox ? 0 : px, oz ? 0 : pz};
                if (ox && oz && nb[0][1]) return Src{nb[0][1], 0, R - 1};
                if (ox && oz && nb[1][0]) return Src{nb[1][0], R - 1, 0};
                return Src{g, std::min(px, R - 1), std::min(pz, R - 1)};
            };
            fmt::OcLandData land;
            land.sampleCount = static_cast<u32>(N);
            land.spacingCm = static_cast<f32>(g->spacing) * fmt::axis::kCmPerMetre;
            const double x0 = static_cast<double>(g->rx) * R * g->spacing, z0 = static_cast<double>(g->ry) * R * g->spacing;
            const Vec3 origin = fmt::axis::position(static_cast<f32>(x0), 0.0f, static_cast<f32>(z0 + R * g->spacing));   // engine position of sample (0, 0)
            land.originCm[0] = origin.x;
            land.originCm[1] = origin.y;
            land.originCm[2] = 0.0f;
            land.heights.resize(static_cast<size_t>(N) * N);
            const bool paint = !layerIds.empty();
            const size_t planes = layerIds.size() > 1 ? layerIds.size() - 1 : 0;
            if (paint) {
                land.layerNames = layerNames;
                land.weights.assign(planes * static_cast<size_t>(N) * N, 0);
            }
            for (int ix = 0; ix < N; ++ix)
                for (int iy = 0; iy < N; ++iy) {
                    const Src s = src(iy, R - ix);   // pixel (px = iy, pz = R - ix)
                    const size_t at = static_cast<size_t>(iy) * N + ix;
                    land.heights[at] = fmt::axis::position(0.0f, s.r->height[static_cast<size_t>(s.z) * s.r->size + s.x], 0.0f).z;
                    minH = std::min<double>(minH, land.heights[at]);
                    maxH = std::max<double>(maxH, land.heights[at]);
                    if (planes == 0) continue;
                    const Paint p = paintAt(*s.r, s.x, s.z);
                    int weights[fmt::kOcLandMaxLayers] = {};
                    weights[layerOf(p.base)] += kBlendMax - p.blend;
                    if (p.blend) weights[layerOf(p.overlay)] += p.blend;
                    for (size_t l = 1; l <= planes; ++l) land.weights[(l - 1) * static_cast<size_t>(N) * N + at] = static_cast<u8>(std::min(weights[l], 255));
                }
            TerrainResult::Section sec;
            sec.name = safeName(fn.name, "Terrain3D") + "_" + locationName(g->rx, g->ry);
            sec.asset = folder + "/" + sec.name + ".ocland";
            sec.x = land.originCm[0];
            sec.y = land.originCm[1];
            sec.extent = land.extentCm();
            createDirectories(contentDir_ + "/" + folder);
            std::string why;
            if (!land.valid() || (paint && !land.layersValid()) || !fmt::saveOcLand(contentDir_ + "/" + sec.asset, land, &why)) {
                report_.add("resource", g->res, "", "Terrain3DRegion", "", Rating::Unsupported, "the section could not be written: " + why, 0);
                continue;
            }
            report_.add("resource", g->res, "", "Terrain3DRegion", "", Rating::Converted,
                        "region (" + std::to_string(g->rx) + ", " + std::to_string(g->ry) + ") written as " + sec.asset + ": " + std::to_string(N) + "x" + std::to_string(N) + " samples, " +
                            std::to_string(static_cast<int>(std::lround(land.spacingCm))) + " cm apart (heights are stored in 16 bits across the section's own range)",
                        0);
            result.sections.push_back(std::move(sec));
        }
        note("", Rating::Converted,
             std::to_string(result.sections.size()) + " region(s) of " + std::to_string(*sizes.begin()) + " pixels became .ocland sections with LANDSCAPE records (heights " +
                 std::to_string(static_cast<long long>(std::lround(minH))) + " to " + std::to_string(static_cast<long long>(std::lround(maxH))) +
                 " cm); collision is the engine landscape's own heightfield built from each section (modules/landscape PhysicsBridge)");
        if (result.sections.size() > 1)
            note("", Rating::Approximated, "the engine's runtime draws and collides with the first LANDSCAPE record of a level only (the one nearest the origin here); the other sections are written but not tiled yet");
        if (skipped) note("data_directory", Rating::Unsupported, std::to_string(skipped) + " file(s) in the folder were not readable regions and were skipped");
        if (holes) note("control_map", Rating::Unsupported, std::to_string(holes) + " texel(s) are holes; the landscape has no holes, so the terrain is solid there");
        if (navTexels) note("control_map", Rating::Unsupported, "the navigation flag of " + std::to_string(navTexels) + " texel(s) is dropped; bake navigation from the converted colliders");
        if (uvBits) note("control_map", Rating::Unsupported, "per-texel UV rotation and UV scale bits are dropped; every layer tiles at its texture's own scale");
        if (tintedRegions)
            note("color_map", Rating::Unsupported, std::to_string(tintedRegions) + " region(s) carry a colour map (a per-pixel tint and roughness); the section holds heights and layer weights only, so the tint is dropped");
        if (instancerTransforms)
            note("instancer", Rating::Unsupported, std::to_string(instancerTransforms) + " instanced mesh transform(s) (Terrain3D's instancer) are dropped; the meshes could be rebuilt as a SCATTER species by hand");
        if (haveAssets)
            if (const Var* ml = propOf(*assetsRes.props, "mesh_list"); ml && !ml->items.empty())
                note("assets", Rating::Unsupported, std::to_string(ml->items.size()) + " Terrain3DMeshAsset(s) in the mesh list (the instancer's meshes) are dropped");
        terrainResults_[cacheKey] = std::move(result);
    }
    const TerrainResult& result = terrainResults_[cacheKey];

    rate("data_directory", Rating::Converted, std::to_string(result.sections.size()) + " region(s) read from " + dir + " and converted, not copied");
    rated.insert("data_directory");
    rateIf("assets", Rating::Converted, "the texture list became the layers of the terrain material; the mesh list (the instancer) is dropped");
    rateIf("material", Rating::Approximated, "the Terrain3DMaterial's blending and auto shader became the layered terrain material; its other shader settings are dropped");
    rateIf("vertex_spacing", Rating::Converted, "the distance between height samples (times 100 for centimetres)");
    rateIf("region_size", Rating::Converted, "the region size in pixels");
    for (const char* k : {"mesh_lods", "mesh_size", "lod0_range"})
        rateIf(k, Rating::Approximated, "the engine's landscape has its own level-of-detail rings; this setting is not used");
    for (const char* k : {"collision_mode", "collision_layer", "collision_mask", "collision_priority", "physics_material"})
        rateIf(k, Rating::Approximated, "collision is the engine landscape's own heightfield from each section (modules/landscape PhysicsBridge), on while the landscape is loaded; this setting is not used");
    rateIf("top_level", Rating::Converted, "the sections are placed in world space");
    for (const Prop& p : fn.props)
        if (!rated.count(p.key) && p.key.rfind("metadata/", 0) != 0 && p.key != "unique_name_in_owner")
            rate(p.key, Rating::Unsupported, "no counterpart in the engine's landscape; the setting is dropped");

    // ---- the level's records ----
    for (const TerrainResult::Section& s : result.sections) {
        fmt::OcLandscapePlacement lp;
        lp.name = s.name;
        lp.section = s.asset;
        lp.material = result.material;
        lp.x = s.x;
        lp.y = s.y;
        lp.z = 0.0;
        lp.extentCm = s.extent;
        level.landscapes.push_back(std::move(lp));
    }
}

}  // namespace aver::fabric
