// FabricWave2Test -- wave 2 of the Godot parity plan (images, textures and materials) through `AverAssetC fabric` on tests/fabric/data/Wave2Game.
//
// The fixture's picture files are text stand-ins (the converter's other tests never decode them); this one copies the project to a scratch folder and writes real PNGs
// over them, because a procedural texture is baked from pixels and an AtlasTexture is cut out of a real picture. What is checked: P1 (a material's sampler and the
// project's anisotropy), F35 (the emissive scale, the roughness channel, the sRGB vertex colour), F26 (gradient, noise, curve, atlas and canvas textures become pictures),
// P2 (surface_material_override/N kept for a multi-surface mesh and carried on the placed mesh as a SurfaceMaterials record; a single-surface mesh collapses).
#include "aver/core/ErrorCodes.hpp"
#include "aver/core/Log.hpp"
#include "aver/formats/OcMat.hpp"
#include "aver/formats/OcProject.hpp"
#include "aver/formats/OcWorld.hpp"
#include "aver/platform/FileSystem.hpp"
#include "aver/platform/Image.hpp"

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <filesystem>
#include <functional>
#include <sstream>
#include <string>
#include <vector>
#if !defined(_WIN32)
#include <sys/wait.h>
#endif

using namespace aver;

static int g_checks = 0;
static int g_failures = 0;

static void check(bool cond, const std::string& what) {
    ++g_checks;
    if (cond) { AVER_INFO("  ok    {}", what); return; }
    ++g_failures;
    AVER_ERROR("  FAIL  {}", what);
}

static bool near(double a, double b, double abs = 1e-3) { return std::fabs(a - b) <= abs; }

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

// ---- a PNG writer: stored (uncompressed) deflate blocks, enough for a fixture ---------------------------------------------------

static std::uint32_t crc32(const std::uint8_t* d, size_t n, std::uint32_t crc = 0xFFFFFFFFu) {
    static std::uint32_t table[256];
    static bool made = false;
    if (!made) {
        for (std::uint32_t i = 0; i < 256; ++i) {
            std::uint32_t c = i;
            for (int k = 0; k < 8; ++k) c = (c & 1u) ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        made = true;
    }
    for (size_t i = 0; i < n; ++i) crc = table[(crc ^ d[i]) & 0xFFu] ^ (crc >> 8);
    return crc;
}

static void put32(std::vector<std::uint8_t>& v, std::uint32_t x) { for (int s = 24; s >= 0; s -= 8) v.push_back(static_cast<std::uint8_t>(x >> s)); }

static void chunk(std::vector<std::uint8_t>& png, const char* type, const std::vector<std::uint8_t>& data) {
    put32(png, static_cast<std::uint32_t>(data.size()));
    std::vector<std::uint8_t> body(type, type + 4);
    body.insert(body.end(), data.begin(), data.end());
    png.insert(png.end(), body.begin(), body.end());
    put32(png, crc32(body.data(), body.size()) ^ 0xFFFFFFFFu);
}

// An RGBA PNG: px(x, y, rgba) fills the four bytes of a pixel.
static std::vector<std::uint8_t> makePng(int w, int h, const std::function<void(int, int, std::uint8_t*)>& px) {
    std::vector<std::uint8_t> raw;
    for (int y = 0; y < h; ++y) {
        raw.push_back(0);
        for (int x = 0; x < w; ++x) {
            std::uint8_t p[4] = {0, 0, 0, 255};
            px(x, y, p);
            raw.insert(raw.end(), p, p + 4);
        }
    }
    std::vector<std::uint8_t> z = {0x78, 0x01};
    for (size_t at = 0; at < raw.size();) {
        const size_t n = std::min<size_t>(65535, raw.size() - at);
        z.push_back(at + n == raw.size() ? 1 : 0);
        z.push_back(static_cast<std::uint8_t>(n & 0xFF)); z.push_back(static_cast<std::uint8_t>(n >> 8));
        z.push_back(static_cast<std::uint8_t>(~n & 0xFF)); z.push_back(static_cast<std::uint8_t>((~n >> 8) & 0xFF));
        z.insert(z.end(), raw.begin() + static_cast<std::ptrdiff_t>(at), raw.begin() + static_cast<std::ptrdiff_t>(at + n));
        at += n;
    }
    std::uint32_t a = 1, b = 0;
    for (std::uint8_t c : raw) { a = (a + c) % 65521u; b = (b + a) % 65521u; }
    put32(z, (b << 16) | a);
    std::vector<std::uint8_t> png = {0x89, 'P', 'N', 'G', 0x0D, 0x0A, 0x1A, 0x0A};
    std::vector<std::uint8_t> ihdr;
    put32(ihdr, static_cast<std::uint32_t>(w)); put32(ihdr, static_cast<std::uint32_t>(h));
    ihdr.insert(ihdr.end(), {8, 6, 0, 0, 0});
    chunk(png, "IHDR", ihdr);
    chunk(png, "IDAT", z);
    chunk(png, "IEND", {});
    return png;
}

static void writePng(const std::string& path, int w, int h, const std::function<void(int, int, std::uint8_t*)>& px) {
    const std::vector<std::uint8_t> png = makePng(w, h, px);
    writeFileBytes(path, png.data(), png.size());
}

// ---- WebP vectors from a reference encoder (the same 8x8 colour ramp the engine's ImageDecodeTest uses: r = 32 + 24x, g = 40 + 24y, b = 200 - 10x; alpha 40 + 28y) ----------------

static const std::uint8_t kLossy[] = {
    0x52, 0x49, 0x46, 0x46, 0x64, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50, 0x56, 0x50, 0x38, 0x20,
    0x58, 0x00, 0x00, 0x00, 0x50, 0x02, 0x00, 0x9d, 0x01, 0x2a, 0x08, 0x00, 0x08, 0x00, 0x00, 0x80,
    0x08, 0x25, 0xb0, 0x02, 0x74, 0x72, 0x00, 0xab, 0xff, 0x03, 0xd1, 0x32, 0xc0, 0x04, 0x00, 0x00,
    0xfe, 0xfe, 0xc1, 0xeb, 0xbd, 0x5d, 0xe6, 0x77, 0xbc, 0x48, 0x77, 0x6e, 0xef, 0xa6, 0xc6, 0x8f,
    0xfe, 0xcc, 0x6f, 0x75, 0xfe, 0xf8, 0x73, 0x1b, 0xe8, 0xdf, 0xf7, 0x59, 0x0b, 0xff, 0xb7, 0x94,
    0x4b, 0xfc, 0x19, 0xdf, 0xe7, 0xff, 0x2a, 0xff, 0xf0, 0x47, 0x91, 0xd3, 0xff, 0xfc, 0x2b, 0x8f,
    0xff, 0x03, 0xd9, 0xbf, 0xff, 0xd0, 0x47, 0xf2, 0x83, 0x3e, 0x60, 0x00,
};
static const std::uint8_t kLossless[] = {
    0x52, 0x49, 0x46, 0x46, 0x3a, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50, 0x56, 0x50, 0x38, 0x4c,
    0x2e, 0x00, 0x00, 0x00, 0x2f, 0x07, 0xc0, 0x01, 0x00, 0xb9, 0x32, 0x44, 0xf4, 0x3f, 0x76, 0x11,
    0xd1, 0xff, 0x00, 0x61, 0xb6, 0xd1, 0x30, 0x86, 0x70, 0xfe, 0x8c, 0x7b, 0x1d, 0x81, 0x58, 0x30,
    0xc5, 0xc3, 0xe8, 0x4c, 0x2c, 0x98, 0xcc, 0x9f, 0x2c, 0xa2, 0xee, 0x98, 0x80, 0x1d, 0xdd, 0x02,
    0xfd, 0x07,
};
static const std::uint8_t kLossyAlpha[] = {
    0x52, 0x49, 0x46, 0x46, 0x94, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50, 0x56, 0x50, 0x38, 0x58,
    0x0a, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x07, 0x00, 0x00, 0x07, 0x00, 0x00, 0x41, 0x4c,
    0x50, 0x48, 0x16, 0x00, 0x00, 0x00, 0x01, 0xb9, 0x8c, 0xe8, 0x7f, 0x80, 0x20, 0xdb, 0xa6, 0x20,
    0x03, 0xb8, 0xbf, 0xe6, 0x05, 0x22, 0x26, 0x20, 0x16, 0x00, 0xc8, 0x0d, 0x56, 0x50, 0x38, 0x20,
    0x58, 0x00, 0x00, 0x00, 0x10, 0x02, 0x00, 0x9d, 0x01, 0x2a, 0x08, 0x00, 0x08, 0x00, 0x00, 0x80,
    0x08, 0x25, 0xb0, 0x02, 0x74, 0x47, 0xff, 0x81, 0xe8, 0x99, 0x60, 0x12, 0xa0, 0x00, 0xfe, 0xfe,
    0xc1, 0xeb, 0xbd, 0x5d, 0xe6, 0x77, 0xbc, 0x48, 0x77, 0x6e, 0xef, 0xa6, 0xc6, 0x8f, 0xfe, 0xd4,
    0x47, 0xfb, 0xd7, 0xfb, 0xe1, 0xcc, 0x6f, 0x40, 0xbf, 0xce, 0xac, 0xb7, 0xff, 0xed, 0xe5, 0x12,
    0xfe, 0xef, 0xff, 0x9b, 0xff, 0xe5, 0x5f, 0xfe, 0x08, 0xf2, 0x3a, 0x7f, 0xff, 0x85, 0x71, 0xff,
    0xe0, 0x7b, 0x37, 0xff, 0xf9, 0xe0, 0xff, 0xf2, 0x83, 0x3e, 0x60, 0x00,
};
static const std::uint8_t kLosslessAlpha[] = {
    0x52, 0x49, 0x46, 0x46, 0x40, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50, 0x56, 0x50, 0x38, 0x4c,
    0x34, 0x00, 0x00, 0x00, 0x2f, 0x07, 0xc0, 0x01, 0x10, 0xb9, 0x32, 0x44, 0xf4, 0x3f, 0x76, 0x11,
    0xd1, 0xff, 0x00, 0x61, 0xb6, 0xd1, 0x30, 0x86, 0x70, 0xfe, 0x8c, 0x7b, 0x1d, 0x81, 0x58, 0x30,
    0xc5, 0xc3, 0xe8, 0x4c, 0x2c, 0x98, 0xcc, 0x9f, 0x2c, 0xa2, 0x6e, 0x81, 0x40, 0x8a, 0x03, 0x59,
    0xe0, 0x09, 0xd8, 0x4d, 0x6b, 0x09, 0xf4, 0x1f,
};
// A RIFF/WEBP header around nothing: not a picture.
static const std::uint8_t kJunk[] = {'R', 'I', 'F', 'F', 4, 0, 0, 0, 'W', 'E', 'B', 'P'};

template <size_t N>
static void writeBytes(const std::string& path, const std::uint8_t (&bytes)[N]) { writeFileBytes(path, bytes, N); }

static bool within(int got, int want, int tol) { return std::abs(got - want) <= tol; }

// ---- lookups -------------------------------------------------------------------------------------------------------------------

static int findPlacement(const fmt::OcWorldData& w, const char* name) {
    for (usize i = 0; i < w.placements.size(); ++i)
        if (w.placements[i].name == name && !w.placements[i].className.empty()) return static_cast<int>(i);
    return -1;
}

static const std::string* prop(const fmt::OcWorldData& w, int idx, const char* key) {
    if (idx < 0) return nullptr;
    for (const fmt::OcPlacedComponent& c : w.placements[static_cast<usize>(idx)].components)
        if (c.type == "Props")
            if (const std::string* v = c.find(key)) return v;
    return nullptr;
}

// The SurfaceMaterials record on the plain mesh placement below a node, or null.
static const fmt::OcPlacedComponent* surfaceRecord(const fmt::OcWorldData& w, int node) {
    for (const fmt::OcWorldPlacement& p : w.placements)
        if (p.parent == node && p.className.empty())
            for (const fmt::OcPlacedComponent& c : p.components)
                if (c.type == "SurfaceMaterials") return &c;
    return nullptr;
}

static bool decodePicture(const std::string& path, ImageData& img) {
    std::string why;
    return decodeImage(path, img, &why) && img.valid();
}

static std::uint8_t channel(const ImageData& img, u32 x, u32 y, int c) { return img.pixels[(static_cast<usize>(y) * img.width + x) * 4 + static_cast<usize>(c)]; }

int main() {
    AVER_INFO("FabricWave2Test");
    const std::filesystem::path exe = AVER_ASSETC_EXE;
    const std::string dataDir = std::string(AVER_FABRIC_DATA) + "/Wave2Game";
    std::error_code ec;
    check(std::filesystem::exists(exe, ec), "AverAssetC was built: " + exe.string());
    check(std::filesystem::exists(dataDir + "/main.tscn", ec), "the Godot fixture exists: " + dataDir);
    if (g_failures) return exitCode(ExitCode::Failed);

    const std::filesystem::path outRoot = std::filesystem::temp_directory_path() / "aver-fabric-wave2-test";
    std::filesystem::remove_all(outRoot, ec);
    std::filesystem::create_directories(outRoot, ec);
    const std::string src = (outRoot / "Wave2Game").string();
    std::filesystem::copy(dataDir, src, std::filesystem::copy_options::recursive, ec);
    check(!ec, "the fixture was copied to a scratch folder");
    // Real pictures over the stand-ins.
    writePng(src + "/textures/pixel.png", 4, 4, [](int, int, std::uint8_t* p) { p[0] = p[1] = p[2] = 128; });
    writePng(src + "/textures/rough.png", 2, 2, [](int, int, std::uint8_t* p) { p[0] = 10; p[1] = 20; p[2] = 200; });
    writePng(src + "/textures/atlas.png", 4, 4, [](int x, int y, std::uint8_t* p) {   // green, except a red block in the lower right
        const bool red = x >= 2 && y >= 2;
        p[0] = red ? 255 : 0; p[1] = red ? 0 : 255; p[2] = 0;
    });
    writeBytes(src + "/textures/lossy.webp", kLossy);
    writeBytes(src + "/textures/lossless.webp", kLossless);
    writeBytes(src + "/textures/lossyalpha.webp", kLossyAlpha);
    writeBytes(src + "/textures/losslessalpha.webp", kLosslessAlpha);
    writeBytes(src + "/textures/junk.webp", kJunk);

    const std::string out = (outRoot / "out").string();
    std::string log;
    const int code = runCapture("\"" + exe.string() + "\" fabric \"" + src + "\" \"" + out + "\" --scenes-only", log);
    check(code == 0, "the converter exits 0 (got " + std::to_string(code) + ")");
    if (code != 0) AVER_INFO("{}", log);
    const std::string proj = out + "/Wave2Game";
    const std::string content = proj + "/Content";

    // ---- the project: P1 -------------------------------------------------------------------------------------------------------------
    AVER_INFO("-- the project's texture defaults --");
    {
        fmt::ProjectDesc pd;
        std::string err;
        check(fmt::loadOcproject(proj + "/Wave2Game.ocproject", pd, &err), "the .ocproject loads: " + err);
        check(pd.textureAniso == 4, "anisotropic_filtering_level 2 is 4x: RENDER.TEXTUREANISO 4 (got " + std::to_string(pd.textureAniso) + ")");
        check(pd.hasTextureMipBias && near(pd.textureMipBias, -0.5), "texture_mipmap_bias is RENDER.TEXTUREMIPBIAS -0.5");
    }

    // ---- the materials ----------------------------------------------------------------------------------------------------------------
    std::vector<std::pair<std::string, pbr::MaterialDesc>> mats;
    for (const auto& e : std::filesystem::directory_iterator(content + "/Materials", ec)) {
        if (e.path().extension() != ".ocmat") continue;
        pbr::MaterialDesc d;
        std::string err;
        if (fmt::loadOcmat(e.path().string(), d, nullptr, &err)) mats.emplace_back(e.path().stem().string(), d);
        else check(false, "loads " + e.path().string() + ": " + err);
    }
    const auto findMat = [&](const std::function<bool(const pbr::MaterialDesc&)>& pred) -> const pbr::MaterialDesc* {
        for (const auto& m : mats) if (pred(m.second)) return &m.second;
        return nullptr;
    };
    const auto texPath = [&](const pbr::MaterialDesc& d, pbr::TextureSlot s) { return content + "/" + d.textures[static_cast<u32>(s)].path; };

    AVER_INFO("-- P1 and F35 in a material --");
    {
        const pbr::MaterialDesc* px = findMat([](const pbr::MaterialDesc& m) { return m.fx.texFilter == pbr::FxFilter::Point; });
        check(px != nullptr, "texture_filter 0 (nearest) is the point sampler");
        if (px) {
            check(px->fx.texAddress == pbr::FxAddress::Clamp, "texture_repeat off is clamp addressing");
            check(px->vertexColor && px->fx.vertexColorSrgb, "vertex_color_is_srgb is the vcsrgb flag, with the vertex colour on");
            check(px->fx.roughSel() == pbr::FxChannel::B, "roughness_texture_channel 2 (blue) is the roughness channel of the lone map");
            check(near(px->emissiveEnergy, 3.0), "emission_energy_multiplier is the material's emissive energy");
            check(near(px->emissiveFactor[0], 1.0) && near(px->emissiveFactor[1], 0.2140, 2e-3) && near(px->emissiveFactor[2], 0.0), "the emission colour is the linear factor, without the energy folded in");
            check(pbr::fxVariantKey(px->fx) != pbr::fxVariantKey(pbr::MaterialFx{}), "the sampler moves the material to its own pipeline variant");
        }
        const pbr::MaterialDesc* plain = findMat([](const pbr::MaterialDesc& m) { return m.fx.texFilter == pbr::FxFilter::Anisotropic && m.fx.texAddress == pbr::FxAddress::Wrap && m.emissiveEnergy == 1.0f; });
        check(plain != nullptr, "a material that states no sampler keeps the engine's (anisotropic, wrap) and an energy of 1");
    }

    AVER_INFO("-- F26 procedural textures in a material --");
    {
        const pbr::MaterialDesc* proc = findMat([](const pbr::MaterialDesc& m) { return !m.textures[static_cast<u32>(pbr::TextureSlot::Normal)].empty() && !m.textures[static_cast<u32>(pbr::TextureSlot::Emissive)].empty(); });
        check(proc != nullptr, "the material with a gradient albedo, a noise normal map and a gradient emission map was written");
        if (proc) {
            ImageData albedo, normal, emissive;
            check(decodePicture(texPath(*proc, pbr::TextureSlot::BaseColor), albedo) && albedo.width == 16 && albedo.height == 8, "GradientTexture2D: a 16 x 8 picture");
            if (albedo.valid()) check(channel(albedo, 0, 4, 0) < 8 && channel(albedo, 15, 4, 0) > 247 && channel(albedo, 0, 0, 1) == 0, "it runs from black to red, left to right (fill_to (1, 0))");
            check(decodePicture(texPath(*proc, pbr::TextureSlot::Normal), normal) && normal.width == 32 && normal.height == 16, "NoiseTexture2D: a 32 x 16 picture");
            if (normal.valid()) {
                int lo = 255, hi = 0;
                for (u32 y = 0; y < normal.height; ++y) for (u32 x = 0; x < normal.width; ++x) { lo = std::min<int>(lo, channel(normal, x, y, 0)); hi = std::max<int>(hi, channel(normal, x, y, 0)); }
                check(lo < 12 && hi > 243, "the noise is normalised across the full range (cellular noise, seed 5)");
            }
            check(decodePicture(texPath(*proc, pbr::TextureSlot::Emissive), emissive) && emissive.width == 8 && emissive.height == 1, "GradientTexture1D: an 8 x 1 picture");
        }
        const pbr::MaterialDesc* atlas = findMat([&](const pbr::MaterialDesc& m) {
            ImageData img;
            return !m.textures[static_cast<u32>(pbr::TextureSlot::BaseColor)].empty() && decodePicture(texPath(m, pbr::TextureSlot::BaseColor), img) && img.width == 2 && img.height == 2;
        });
        check(atlas != nullptr, "AtlasTexture: the region (2, 2, 2, 2) is cut out as a 2 x 2 picture");
        if (atlas) {
            ImageData img;
            decodePicture(texPath(*atlas, pbr::TextureSlot::BaseColor), img);
            check(channel(img, 0, 0, 0) == 255 && channel(img, 1, 1, 1) == 0, "and it is the red block");
        }
        const pbr::MaterialDesc* canvas = findMat([&](const pbr::MaterialDesc& m) {
            ImageData img;
            return !m.textures[static_cast<u32>(pbr::TextureSlot::BaseColor)].empty() && decodePicture(texPath(m, pbr::TextureSlot::BaseColor), img) && img.width == 4 && img.height == 4 &&
                   channel(img, 1, 1, 0) == 128 && m.fx.texFilter == pbr::FxFilter::Anisotropic;
        });
        check(canvas != nullptr, "CanvasTexture: the material takes its diffuse picture");
    }

    AVER_INFO("-- E1: WebP pictures --");
    {
        const auto endsWebp = [](const std::string& p) { return p.size() > 5 && p.compare(p.size() - 5, 5, ".webp") == 0; };
        const pbr::MaterialDesc* plain = findMat([&](const pbr::MaterialDesc& m) {
            return endsWebp(m.textures[static_cast<u32>(pbr::TextureSlot::BaseColor)].path) && !m.textures[static_cast<u32>(pbr::TextureSlot::Emissive)].empty();
        });
        check(plain != nullptr, "a material with a lossy WebP albedo and a lossless WebP emission map was cooked with both as .webp files");
        if (plain) {
            ImageData lossy, lossless;
            check(decodePicture(texPath(*plain, pbr::TextureSlot::BaseColor), lossy) && lossy.width == 8 && lossy.height == 8, "the lossy WebP decodes through the engine's loader (8 x 8)");
            if (lossy.valid())
                check(within(channel(lossy, 0, 0, 0), 35, 3) && within(channel(lossy, 0, 0, 2), 186, 3) && within(channel(lossy, 3, 4, 1), 136, 3), "and has the reference encoder's pixels");
            check(decodePicture(texPath(*plain, pbr::TextureSlot::Emissive), lossless) && lossless.width == 8, "the lossless WebP decodes");
            if (lossless.valid())
                check(channel(lossless, 0, 0, 0) == 32 && channel(lossless, 0, 0, 1) == 40 && channel(lossless, 0, 0, 2) == 200 && channel(lossless, 7, 7, 1) == 208, "and is exact");
        }
        const pbr::MaterialDesc* alpha = findMat([&](const pbr::MaterialDesc& m) {
            return endsWebp(m.textures[static_cast<u32>(pbr::TextureSlot::BaseColor)].path) && !m.textures[static_cast<u32>(pbr::TextureSlot::Normal)].empty();
        });
        check(alpha != nullptr, "a material with a lossy-with-alpha albedo and a lossless-with-alpha normal map was cooked");
        if (alpha) {
            ImageData la, ll;
            check(decodePicture(texPath(*alpha, pbr::TextureSlot::BaseColor), la) && la.width == 8, "the lossy WebP with alpha decodes");
            if (la.valid()) check(within(channel(la, 0, 0, 3), 40, 3) && within(channel(la, 7, 7, 3), 236, 3), "and keeps its alpha");
            check(decodePicture(texPath(*alpha, pbr::TextureSlot::Normal), ll) && ll.width == 8, "the lossless WebP with alpha decodes");
            if (ll.valid()) check(channel(ll, 0, 0, 3) == 40 && channel(ll, 3, 4, 3) == 152, "and keeps its alpha exactly");
        }
    }

    // ---- the level: P2 and F26 for a node ----------------------------------------------------------------------------------------------
    AVER_INFO("-- the level --");
    fmt::OcWorldData w;
    std::string err;
    check(fmt::loadOcworld(content + "/Maps/main.ocworld", w, &err), "the level loads: " + err);
    {
        const int pixel = findPlacement(w, "Pixel");
        check(pixel >= 0 && prop(w, pixel, "material_override") != nullptr && prop(w, pixel, "surface_material_override/0") == nullptr,
              "a single-surface mesh: surface 0's override is its material_override");
        check(pixel >= 0 && surfaceRecord(w, pixel) == nullptr, "and its placed mesh carries no per-surface record");

        const int two = findPlacement(w, "Two");
        check(two >= 0 && prop(w, two, "surface_material_override/0") != nullptr && prop(w, two, "surface_material_override/1") != nullptr && prop(w, two, "material_override") == nullptr,
              "a two-surface mesh keeps both surface overrides as properties of the node");
        const fmt::OcPlacedComponent* rec = two >= 0 ? surfaceRecord(w, two) : nullptr;
        check(rec != nullptr && rec->find("slot0") != nullptr && rec->find("slot1") != nullptr && *rec->find("slot1") == "Wave2Game_blue",
              "and its placed mesh carries them as a SurfaceMaterials record (slot 1 is the blue material)");

        const int whole = findPlacement(w, "Whole");
        check(whole >= 0 && prop(w, whole, "material_override") != nullptr && surfaceRecord(w, whole) == nullptr, "a material_override replaces every surface: no per-surface record is placed");

        const int webpRect = findPlacement(w, "WebpRect");
        const std::string* wt = prop(w, webpRect, "texture");
        check(wt != nullptr && wt->find(".webp") != std::string::npos, "a WebP held by a node is copied into the content: " + (wt ? *wt : std::string("(none)")));
        if (wt) {
            const size_t a = wt->find('"'), b = wt->rfind('"');
            ImageData img;
            check(a != std::string::npos && b > a && decodePicture(content + "/" + wt->substr(a + 1, b - a - 1), img) && img.width == 8, "and it decodes");
        }
        check(findPlacement(w, "BadRect") >= 0 && prop(w, findPlacement(w, "BadRect"), "texture") == nullptr, "a WebP the loader cannot read is not placed as a texture");

        const int curve = findPlacement(w, "Curve");
        const std::string* tex = prop(w, curve, "texture");
        check(tex != nullptr && tex->find("Textures/Generated/") != std::string::npos, "a CurveTexture held by a node is baked to a picture: " + (tex ? *tex : std::string("(none)")));
        if (tex) {
            const size_t a = tex->find('"'), b = tex->rfind('"');
            ImageData img;
            check(a != std::string::npos && b > a && decodePicture(content + "/" + tex->substr(a + 1, b - a - 1), img) && img.width == 16 && img.height == 1, "it is a 16 x 1 picture");
            if (img.valid()) check(channel(img, 0, 0, 0) < 12 && channel(img, 15, 0, 0) > 243, "running from 0 to 1 with the curve");
        }
    }

    // ---- the report -------------------------------------------------------------------------------------------------------------------
    AVER_INFO("-- the report --");
    {
        const std::string jsonl = slurp(out + "/FabricReport.jsonl");
        const auto rated = [&](const char* prop, const char* rating) {
            std::istringstream is(jsonl);
            std::string line;
            const std::string a = std::string("\"property\":\"") + prop + "\"", b = std::string("\"rating\":\"") + rating + "\"";
            while (std::getline(is, line))
                if (line.find(a) != std::string::npos && line.find(b) != std::string::npos) return true;
            return false;
        };
        const auto resourceRated = [&](const char* type, const char* rating) {
            std::istringstream is(jsonl);
            std::string line;
            const std::string a = std::string("\"type\":\"") + type + "\"", b = std::string("\"rating\":\"") + rating + "\"";
            while (std::getline(is, line))
                if (line.find(a) != std::string::npos && line.find(b) != std::string::npos) return true;
            return false;
        };
        check(jsonl.find("copied to Textures/textures/lossless.webp") != std::string::npos && jsonl.find("cannot read this .webp file") != std::string::npos,
              "a WebP is reported as copied (converted), and a broken one as unreadable (unsupported)");
        check(rated("texture_filter", "converted") && rated("texture_repeat", "converted"), "texture_filter and texture_repeat are converted");
        check(rated("vertex_color_is_srgb", "converted") && rated("roughness_texture_channel", "converted"), "vertex_color_is_srgb and the roughness channel are converted");
        check(rated("emission_energy_multiplier", "approximated"), "the emission energy stays approximated (no unit calibration)");
        check(rated("surface_material_override/1", "converted") && rated("surface_material_override/0", "converted"), "the surface overrides are rated");
        check(resourceRated("GradientTexture2D", "converted") && resourceRated("NoiseTexture2D", "approximated") && resourceRated("AtlasTexture", "converted") &&
                  resourceRated("CurveTexture", "converted") && resourceRated("CanvasTexture", "approximated"),
              "every procedural texture class is reported, noise as approximated");
        check(rated("rendering/textures/default_filters/anisotropic_filtering_level", "converted"), "the project's anisotropy setting is reported");
    }

    AVER_INFO("FabricWave2Test: {} checks, {} failures", g_checks, g_failures);
    std::filesystem::remove_all(outRoot, ec);
    return g_failures == 0 ? 0 : 1;
}
