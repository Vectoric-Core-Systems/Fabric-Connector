// F26: procedural texture maths (FabricProcTex.hpp).
#include "FabricProcTex.hpp"

#if AVER_HAVE_MATERIAL_COMPILE
#include "stb_image_write.h"
#endif

#include <algorithm>
#include <cmath>
#include <cstdint>

namespace aver::fabric::proctex {

namespace {

float clamp01(float v) { return v < 0.0f ? 0.0f : (v > 1.0f ? 1.0f : v); }
u8 toByte(float v) { return static_cast<u8>(std::lround(clamp01(v) * 255.0f)); }
float lerpf(float a, float b, float t) { return a + (b - a) * t; }
float smooth(float t) { return t * t * (3.0f - 2.0f * t); }
float quintic(float t) { return t * t * t * (t * (t * 6.0f - 15.0f) + 10.0f); }

std::uint32_t hash2(int seed, int x, int y) {
    std::uint32_t h = static_cast<std::uint32_t>(seed) * 374761393u + static_cast<std::uint32_t>(x) * 668265263u + static_cast<std::uint32_t>(y) * 2246822519u;
    h = (h ^ (h >> 13)) * 1274126177u;
    return h ^ (h >> 16);
}
float rnd(std::uint32_t h) { return static_cast<float>(h & 0xFFFFFFu) / 8388607.5f - 1.0f; }   // -1..1

float valueNoise(int seed, float x, float y, bool cubic) {
    const int xi = static_cast<int>(std::floor(x)), yi = static_cast<int>(std::floor(y));
    float tx = x - static_cast<float>(xi), ty = y - static_cast<float>(yi);
    tx = cubic ? smooth(tx) : quintic(tx);
    ty = cubic ? smooth(ty) : quintic(ty);
    const float a = rnd(hash2(seed, xi, yi)), b = rnd(hash2(seed, xi + 1, yi));
    const float c = rnd(hash2(seed, xi, yi + 1)), d = rnd(hash2(seed, xi + 1, yi + 1));
    return lerpf(lerpf(a, b, tx), lerpf(c, d, tx), ty);
}

float gradDot(int seed, int xi, int yi, float dx, float dy) {
    const float ang = (rnd(hash2(seed, xi, yi)) + 1.0f) * 3.14159265f;
    return std::cos(ang) * dx + std::sin(ang) * dy;
}

float perlin(int seed, float x, float y) {
    const int xi = static_cast<int>(std::floor(x)), yi = static_cast<int>(std::floor(y));
    const float fx = x - static_cast<float>(xi), fy = y - static_cast<float>(yi);
    const float u = quintic(fx), v = quintic(fy);
    const float n00 = gradDot(seed, xi, yi, fx, fy), n10 = gradDot(seed, xi + 1, yi, fx - 1.0f, fy);
    const float n01 = gradDot(seed, xi, yi + 1, fx, fy - 1.0f), n11 = gradDot(seed, xi + 1, yi + 1, fx - 1.0f, fy - 1.0f);
    return std::clamp(lerpf(lerpf(n00, n10, u), lerpf(n01, n11, u), v) * 1.4142f, -1.0f, 1.0f);
}

float simplex(int seed, float x, float y, bool smoothKernel) {
    constexpr float F2 = 0.3660254f, G2 = 0.2113249f;
    const float s = (x + y) * F2;
    const int i = static_cast<int>(std::floor(x + s)), j = static_cast<int>(std::floor(y + s));
    const float t = static_cast<float>(i + j) * G2;
    const float x0 = x - (static_cast<float>(i) - t), y0 = y - (static_cast<float>(j) - t);
    const int i1 = x0 > y0 ? 1 : 0, j1 = x0 > y0 ? 0 : 1;
    const float xs[3] = {x0, x0 - static_cast<float>(i1) + G2, x0 - 1.0f + 2.0f * G2};
    const float ys[3] = {y0, y0 - static_cast<float>(j1) + G2, y0 - 1.0f + 2.0f * G2};
    const int ci[3] = {i, i + i1, i + 1}, cj[3] = {j, j + j1, j + 1};
    float n = 0.0f;
    for (int k = 0; k < 3; ++k) {
        float f = 0.5f - xs[k] * xs[k] - ys[k] * ys[k];
        if (f <= 0.0f) continue;
        f = smoothKernel ? f * f * f : f * f * f * f;
        n += f * gradDot(seed, ci[k], cj[k], xs[k], ys[k]);
    }
    return std::clamp(n * (smoothKernel ? 24.0f : 70.0f), -1.0f, 1.0f);
}

float cellular(const NoiseParams& p, float x, float y) {
    const int xi = static_cast<int>(std::floor(x)), yi = static_cast<int>(std::floor(y));
    float d1 = 1e9f, d2 = 1e9f;
    float cellValue = 0.0f;
    for (int oy = -1; oy <= 1; ++oy)
        for (int ox = -1; ox <= 1; ++ox) {
            const int cx = xi + ox, cy = yi + oy;
            const std::uint32_t h = hash2(p.seed, cx, cy);
            const float px = static_cast<float>(cx) + 0.5f + rnd(h) * 0.5f * p.cellularJitter;
            const float py = static_cast<float>(cy) + 0.5f + rnd(hash2(p.seed + 1, cx, cy)) * 0.5f * p.cellularJitter;
            const float dx = px - x, dy = py - y;
            float d;
            switch (p.cellularDistance) {
                case 1: d = dx * dx + dy * dy; break;
                case 2: d = std::fabs(dx) + std::fabs(dy); break;
                case 3: d = std::sqrt(dx * dx + dy * dy) + std::fabs(dx) + std::fabs(dy); break;
                default: d = std::sqrt(dx * dx + dy * dy); break;
            }
            if (d < d1) { d2 = d1; d1 = d; cellValue = rnd(h); }
            else if (d < d2) d2 = d;
        }
    float r;
    switch (p.cellularReturn) {
        case 0: return cellValue;
        case 2: r = d2; break;
        case 3: r = d1 + d2; break;
        case 4: r = d2 - d1; break;
        case 5: r = d1 * d2; break;
        case 6: r = d2 > 1e-6f ? d1 / d2 : 0.0f; break;
        default: r = d1; break;
    }
    return std::clamp(r * 2.0f - 1.0f, -1.0f, 1.0f);
}

float base(const NoiseParams& p, float x, float y, int seed) {
    NoiseParams q = p;
    q.seed = seed;
    switch (p.type) {
        case 0: return valueNoise(seed, x, y, false);
        case 1: return valueNoise(seed, x, y, true);
        case 2: return perlin(seed, x, y);
        case 3: return cellular(q, x, y);
        case 4: return simplex(seed, x, y, false);
        default: return simplex(seed, x, y, true);
    }
}

}  // namespace

float noise2(const NoiseParams& p, float x, float y) {
    x = (x + p.offset[0]) * p.frequency;
    y = (y + p.offset[1]) * p.frequency;
    if (p.fractal == 0 || p.octaves <= 1) return base(p, x, y, p.seed);
    float sum = 0.0f, amp = 1.0f, bound = 0.0f;
    for (int o = 0; o < p.octaves; ++o) {
        float n = base(p, x, y, p.seed + o);
        if (p.fractal == 2) n = 1.0f - 2.0f * std::fabs(n);
        else if (p.fractal == 3) {
            n = (n + 1.0f) * p.pingPongStrength;
            n = n - std::floor(n * 0.5f) * 2.0f;
            n = (n < 1.0f ? n : 2.0f - n) * 2.0f - 1.0f;
        }
        sum += n * amp;
        bound += amp;
        amp *= p.gain * (1.0f - p.weightedStrength + p.weightedStrength * (n + 1.0f) * 0.5f);
        x *= p.lacunarity;
        y *= p.lacunarity;
    }
    return bound > 0.0f ? std::clamp(sum / bound, -1.0f, 1.0f) : 0.0f;
}

Rgba Gradient::sample(float t) const {
    if (offsets.empty() || colors.empty()) return Rgba{};
    const usize n = std::min(offsets.size(), colors.size());
    if (t <= offsets[0]) return colors[0];
    if (t >= offsets[n - 1]) return colors[n - 1];
    usize i = 0;
    while (i + 1 < n && offsets[i + 1] < t) ++i;
    if (mode == 1) return colors[i];
    const float span = offsets[i + 1] - offsets[i];
    float f = span > 1e-9f ? (t - offsets[i]) / span : 0.0f;
    if (mode == 2) f = smooth(f);
    const Rgba &a = colors[i], &b = colors[i + 1];
    return Rgba{lerpf(a.r, b.r, f), lerpf(a.g, b.g, f), lerpf(a.b, b.b, f), lerpf(a.a, b.a, f)};
}

static void put(Picture& pic, int x, int y, const Rgba& c) {
    u8* o = &pic.rgba[(static_cast<usize>(y) * static_cast<usize>(pic.width) + static_cast<usize>(x)) * 4];
    o[0] = toByte(c.r); o[1] = toByte(c.g); o[2] = toByte(c.b); o[3] = toByte(c.a);
}

static Picture blank(int w, int h) {
    Picture pic;
    pic.width = std::clamp(w, 1, 4096);
    pic.height = std::clamp(h, 1, 4096);
    pic.rgba.assign(static_cast<usize>(pic.width) * static_cast<usize>(pic.height) * 4, 0);
    return pic;
}

Picture gradient1D(const Gradient& g, int width) {
    Picture pic = blank(width, 1);
    for (int x = 0; x < pic.width; ++x) put(pic, x, 0, g.sample(pic.width > 1 ? static_cast<float>(x) / static_cast<float>(pic.width - 1) : 0.0f));
    return pic;
}

Picture gradient2D(const Gradient& g, int width, int height, int fill, float fromX, float fromY, float toX, float toY, int repeat) {
    Picture pic = blank(width, height);
    const float dx = toX - fromX, dy = toY - fromY;
    const float len2 = dx * dx + dy * dy;
    for (int y = 0; y < pic.height; ++y)
        for (int x = 0; x < pic.width; ++x) {
            const float px = pic.width > 1 ? static_cast<float>(x) / static_cast<float>(pic.width - 1) : 0.0f;
            const float py = pic.height > 1 ? static_cast<float>(y) / static_cast<float>(pic.height - 1) : 0.0f;
            float ofs;
            if (fill == 1) ofs = len2 > 1e-9f ? std::sqrt(((px - fromX) * (px - fromX) + (py - fromY) * (py - fromY)) / len2) : 0.0f;
            else if (fill == 2) {
                const float m = std::max(std::fabs(dx), std::fabs(dy));
                ofs = m > 1e-9f ? std::max(std::fabs(px - fromX), std::fabs(py - fromY)) / m : 0.0f;
            } else ofs = len2 > 1e-9f ? ((px - fromX) * dx + (py - fromY) * dy) / len2 : 0.0f;
            if (repeat == 1) ofs -= std::floor(ofs);
            else if (repeat == 2) { ofs = std::fabs(ofs); ofs -= std::floor(ofs * 0.5f) * 2.0f; if (ofs > 1.0f) ofs = 2.0f - ofs; }
            put(pic, x, y, g.sample(clamp01(ofs)));
        }
    return pic;
}

Picture noiseImage(const NoiseParams& p, const NoiseOptions& o) {
    Picture pic = blank(o.width, o.height);
    const usize count = static_cast<usize>(pic.width) * static_cast<usize>(pic.height);
    std::vector<float> v(count);
    // Seamless: four samples blended across the edges, the cheap tiling the engine's noise textures use.
    const float W = static_cast<float>(pic.width), H = static_cast<float>(pic.height);
    for (int y = 0; y < pic.height; ++y)
        for (int x = 0; x < pic.width; ++x) {
            const float fx = static_cast<float>(x), fy = static_cast<float>(y);
            float n = noise2(p, fx, fy);
            if (o.seamless) {
                const float u = fx / W, w = fy / H;
                n = (n * (1 - u) * (1 - w) + noise2(p, fx - W, fy) * u * (1 - w) + noise2(p, fx, fy - H) * (1 - u) * w + noise2(p, fx - W, fy - H) * u * w);
            }
            v[static_cast<usize>(y) * static_cast<usize>(pic.width) + static_cast<usize>(x)] = n;
        }
    float lo = -1.0f, hi = 1.0f;
    if (o.normalize) {
        lo = *std::min_element(v.begin(), v.end());
        hi = *std::max_element(v.begin(), v.end());
        if (hi - lo < 1e-6f) { lo = -1.0f; hi = 1.0f; }
    }
    for (float& n : v) {
        n = (n - lo) / (hi - lo);
        if (o.invert) n = 1.0f - n;
    }
    if (o.asNormalMap) {
        const auto at = [&](int x, int y) { return v[static_cast<usize>(std::clamp(y, 0, pic.height - 1)) * static_cast<usize>(pic.width) + static_cast<usize>(std::clamp(x, 0, pic.width - 1))]; };
        for (int y = 0; y < pic.height; ++y)
            for (int x = 0; x < pic.width; ++x) {
                const float sx = (at(x + 1, y) - at(x - 1, y)) * o.bumpStrength * 0.5f, sy = (at(x, y + 1) - at(x, y - 1)) * o.bumpStrength * 0.5f;
                const float inv = 1.0f / std::sqrt(sx * sx + sy * sy + 1.0f);
                put(pic, x, y, Rgba{-sx * inv * 0.5f + 0.5f, -sy * inv * 0.5f + 0.5f, inv * 0.5f + 0.5f, 1.0f});
            }
        return pic;
    }
    for (int y = 0; y < pic.height; ++y)
        for (int x = 0; x < pic.width; ++x) {
            const float n = clamp01(v[static_cast<usize>(y) * static_cast<usize>(pic.width) + static_cast<usize>(x)]);
            put(pic, x, y, o.ramp ? o.ramp->sample(n) : Rgba{n, n, n, 1.0f});
        }
    return pic;
}

float sampleCurve(const std::vector<CurvePoint>& pts, float x) {
    if (pts.empty()) return 0.0f;
    if (pts.size() == 1 || x <= pts.front().x) return pts.front().y;
    if (x >= pts.back().x) return pts.back().y;
    usize i = 0;
    while (i + 2 < pts.size() && pts[i + 1].x <= x) ++i;
    const CurvePoint &a = pts[i], &b = pts[i + 1];
    const float d = b.x - a.x;
    if (d <= 1e-9f) return b.y;
    float ta = a.rightTangent, tb = b.leftTangent;
    if (a.rightMode == 1) ta = (b.y - a.y) / d;
    if (b.leftMode == 1) tb = (b.y - a.y) / d;
    const float t = (x - a.x) / d, t2 = t * t, t3 = t2 * t;
    return (2 * t3 - 3 * t2 + 1) * a.y + (t3 - 2 * t2 + t) * ta * d + (-2 * t3 + 3 * t2) * b.y + (t3 - t2) * tb * d;
}

Picture curve1D(const std::vector<CurvePoint>& pts, float minDomain, float maxDomain, float minValue, float maxValue, int width, bool redOnly) {
    Picture pic = blank(width, 1);
    const float range = maxValue - minValue;
    for (int x = 0; x < pic.width; ++x) {
        const float t = pic.width > 1 ? static_cast<float>(x) / static_cast<float>(pic.width - 1) : 0.0f;
        const float v = range > 1e-9f ? (sampleCurve(pts, lerpf(minDomain, maxDomain, t)) - minValue) / range : 0.0f;
        put(pic, x, 0, redOnly ? Rgba{v, 0, 0, 1} : Rgba{v, v, v, 1});
    }
    return pic;
}

Picture crop(const Picture& src, int x, int y, int w, int h, int ml, int mt, int mr, int mb) {
    if (!src.valid()) return {};
    const int x0 = std::clamp(x, 0, src.width), y0 = std::clamp(y, 0, src.height);
    const int x1 = std::clamp(x + w, 0, src.width), y1 = std::clamp(y + h, 0, src.height);
    const int cw = std::max(0, x1 - x0), ch = std::max(0, y1 - y0);
    Picture out = blank(cw + ml + mr, ch + mt + mb);
    if (cw == 0 || ch == 0) return out;
    for (int yy = 0; yy < ch; ++yy)
        for (int xx = 0; xx < cw; ++xx) {
            const u8* s = &src.rgba[(static_cast<usize>(y0 + yy) * static_cast<usize>(src.width) + static_cast<usize>(x0 + xx)) * 4];
            u8* d = &out.rgba[(static_cast<usize>(mt + yy) * static_cast<usize>(out.width) + static_cast<usize>(ml + xx)) * 4];
            d[0] = s[0]; d[1] = s[1]; d[2] = s[2]; d[3] = s[3];
        }
    return out;
}

bool encodePng(const Picture& pic, std::vector<u8>& out) {
#if AVER_HAVE_MATERIAL_COMPILE
    if (!pic.valid()) return false;
    out.clear();
    const auto sink = [](void* context, void* data, int size) {
        auto* v = static_cast<std::vector<u8>*>(context);
        v->insert(v->end(), static_cast<const u8*>(data), static_cast<const u8*>(data) + size);
    };
    return stbi_write_png_to_func(sink, &out, pic.width, pic.height, 4, pic.rgba.data(), pic.width * 4) != 0 && !out.empty();
#else
    (void)pic; (void)out;
    return false;
#endif
}

}  // namespace aver::fabric::proctex
