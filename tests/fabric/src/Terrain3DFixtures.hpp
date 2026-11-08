#pragma once
// Builders for FabricTerrain3DTest: tiny Terrain3DRegion resources (binary .res, written field by field from the addon's documented layout; nothing is a copy of a
// real file) and a real PNG. The heights and paint are closed-form so the test can compute what a converted sample must hold.
#include "../../formats.godot/src/GodotBinaryFixtures.hpp"

#include <cmath>
#include <cstring>

namespace t3dfix {

using namespace gbfix;

constexpr int kRegion = 64;
constexpr double kSpacing = 2.0;

// Region (0, 0): a gentle ramp; (1, 0): a higher ramp; (0, -1): flat in its first half, steep in the second.
inline double heightOf(int rx, int ry, int px, int pz) {
    if (rx == 0 && ry == 0) return 10.0 + 0.1 * px + 0.01 * pz;
    if (rx == 1 && ry == 0) return 30.0 + 0.2 * px + 0.02 * pz;
    return pz < 32 ? 5.0 : 5.0 + 3.0 * (pz - 31);
}

inline u32 controlOf(int rx, int ry, int px, int pz) {
    const auto pack = [](u32 base, u32 overlay, u32 blend, u32 low) { return (base << 27) | (overlay << 22) | (blend << 14) | low; };
    if (rx == 0 && ry == 0) return pack(0, 1, static_cast<u32>(px * 4), 0);
    if (rx == 1 && ry == 0) {
        if (px == 3 && pz == 3) return pack(2, 0, 128, 1u << 2);   // a hole
        if (px == 4 && pz == 4) return pack(2, 0, 128, 1u << 1);   // a navigation flag
        return pack(2, 0, 128, 0);
    }
    return 1u;   // the auto bit: the material's auto shader picks the textures by slope
}

inline Bytes image(const Fmt& f, int w, int h, const char* format, const Bytes& data) {
    return f.dict({{f.text("data"), f.packedBytes(data)},
                   {f.text("format"), f.text(format)},
                   {f.text("height"), f.integer(h)},
                   {f.text("mipmaps"), f.boolean(false)},
                   {f.text("width"), f.integer(w)}});
}

// A Terrain3DRegion for location (rx, ry). `compressed` wraps it as RSCC.
inline Bytes region(int rx, int ry, bool compressed) {
    FileDef d;
    d.cls = "Terrain3DRegion";
    const Fmt& f = d.f;
    const int n = kRegion * kRegion;
    Bytes hm(static_cast<size_t>(n) * 4), cm(static_cast<size_t>(n) * 4), col(static_cast<size_t>(n) * 4, 255);
    for (int i = 0; i < n; ++i) col[static_cast<size_t>(i) * 4 + 3] = 127;   // Terrain3D's neutral colour: white, alpha 0.5
    for (int z = 0; z < kRegion; ++z)
        for (int x = 0; x < kRegion; ++x) {
            const float h = static_cast<float>(heightOf(rx, ry, x, z));
            const u32 c = controlOf(rx, ry, x, z);
            std::memcpy(&hm[(static_cast<size_t>(z) * kRegion + x) * 4], &h, 4);
            std::memcpy(&cm[(static_cast<size_t>(z) * kRegion + x) * 4], &c, 4);
        }
    if (rx == 0 && ry == 0) col[0] = 200;   // one tinted pixel
    for (const char* name : {"Image_h", "Image_c", "Image_k"}) {
        FileDef::Res im;
        im.path = std::string("local://") + name;
        im.type = "Image";
        d.res.push_back(im);
    }
    d.prop(d.res[0], "data", image(f, kRegion, kRegion, "RFloat", hm));
    d.prop(d.res[1], "data", image(f, kRegion, kRegion, "RF", cm));
    d.prop(d.res[2], "data", image(f, kRegion, kRegion, "RGBA8", col));
    FileDef::Res m;
    m.path = "res://terrain_data/region.res";
    m.type = "Terrain3DRegion";
    d.res.push_back(m);
    FileDef::Res& main = d.res.back();
    d.prop(main, "version", f.flt(1.0f));
    d.prop(main, "region_size", f.integer(kRegion));
    d.prop(main, "vertex_spacing", f.flt(static_cast<f32>(kSpacing)));
    d.prop(main, "height_map", f.subRef(0));
    d.prop(main, "control_map", f.subRef(1));
    d.prop(main, "color_map", f.subRef(2));
    if (rx == 1 && ry == 0) {   // two instanced transforms of mesh asset 0
        const Bytes t = f.reals(kTransform3D, {1, 0, 0, 0, 1, 0, 0, 0, 1, 5, 6, 7});
        d.prop(main, "instances", f.dict({{f.integer(0), f.array({t, t})}}));
    }
    return compressed ? wrapRscc(d.build(false), 32768) : d.build();
}

// A resource that is not a region, named like one.
inline Bytes stray() {
    FileDef d;
    d.cls = "BoxShape3D";
    FileDef::Res m;
    m.path = "res://terrain_data/stray.res";
    m.type = "BoxShape3D";
    d.res.push_back(m);
    d.prop(d.res.back(), "size", d.f.reals(kVector3, {1, 1, 1}));
    return d.build();
}

// A w x h RGBA PNG of one colour: stored (uncompressed) deflate blocks, so no compressor is needed.
inline Bytes png(int w, int h, u8 r, u8 g, u8 b) {
    static u32 table[256];
    static bool made = false;
    if (!made) {
        for (u32 i = 0; i < 256; ++i) {
            u32 c = i;
            for (int k = 0; k < 8; ++k) c = (c & 1) ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        made = true;
    }
    const auto crc = [&](const Bytes& v) {
        u32 c = 0xFFFFFFFFu;
        for (u8 x : v) c = table[(c ^ x) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    };
    const auto be32 = [](Bytes& o, u32 v) { for (int i = 3; i >= 0; --i) o.push_back(u8(v >> (8 * i))); };
    Bytes raw;
    for (int y = 0; y < h; ++y) {
        raw.push_back(0);
        for (int x = 0; x < w; ++x) { raw.push_back(r); raw.push_back(g); raw.push_back(b); raw.push_back(255); }
    }
    Bytes z = {0x78, 0x01};
    for (size_t at = 0; at < raw.size();) {
        const size_t n = std::min<size_t>(65535, raw.size() - at);
        z.push_back(at + n == raw.size() ? 1 : 0);
        z.push_back(u8(n)); z.push_back(u8(n >> 8)); z.push_back(u8(~n)); z.push_back(u8((~n) >> 8));
        z.insert(z.end(), raw.begin() + static_cast<long>(at), raw.begin() + static_cast<long>(at + n));
        at += n;
    }
    u32 a = 1, bsum = 0;
    for (u8 x : raw) { a = (a + x) % 65521; bsum = (bsum + a) % 65521; }
    be32(z, (bsum << 16) | a);
    Bytes out = {0x89, 'P', 'N', 'G', 0x0D, 0x0A, 0x1A, 0x0A};
    const auto chunk = [&](const char* type, const Bytes& body) {
        be32(out, static_cast<u32>(body.size()));
        Bytes tb(type, type + 4);
        tb.insert(tb.end(), body.begin(), body.end());
        out.insert(out.end(), tb.begin(), tb.end());
        be32(out, crc(tb));
    };
    Bytes ihdr;
    be32(ihdr, static_cast<u32>(w)); be32(ihdr, static_cast<u32>(h));
    ihdr.insert(ihdr.end(), {8, 6, 0, 0, 0});
    chunk("IHDR", ihdr);
    chunk("IDAT", z);
    chunk("IEND", {});
    return out;
}

}  // namespace t3dfix
