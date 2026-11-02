// Wave 6 (particles, decals, sprites): Godot's particle values as the engine's. Pure functions, no engine and no file access, so the converter and
// FabricWave6Test hold the same numbers. The engine side is docs/PARTICLES.md ("The module stack", wave 6); the plan is docs/plans/WAVE6_PARTICLES.md.
//
// Godot is metres, +Y up, right-handed; the engine is centimetres, +Z up, left-handed. A direction or a node-local vector maps as (x, y, z) -> (-z, x, y)
// (AxisMap.hpp); that map has determinant -1, so a rotation axis (a pseudovector: the axis of a spin, the cross product of two vectors) maps as
// (x, y, z) -> (z, -x, -y), the sign the determinant gives it.
#pragma once

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <string>
#include <vector>

namespace aver::fmt::godotparticles {

struct Vec3 {
    double x = 0, y = 0, z = 0;
};

// A direction, or a node-local vector in metres or unitless, as the engine's axes (no unit change).
inline Vec3 direction(const Vec3& v) { return {-v.z, v.x, v.y}; }
// A rotation axis (cross products and spins).
inline Vec3 rotationAxis(const Vec3& v) { return {v.z, -v.x, -v.y}; }
// A box's half-extents: the engine's local X is Godot's Z, Y is Godot's X, Z is Godot's Y; sizes are unsigned.
inline Vec3 extents(const Vec3& v) { return {std::fabs(v.z), std::fabs(v.x), std::fabs(v.y)}; }
inline double toCm(double metres) { return metres * 100.0; }

// ---- curves ------------------------------------------------------------------------------------------------------------------------------------------

struct CurvePoint {
    double t = 0, v = 0;
};

// A Godot Curve's points joined by straight lines (its tangents and modes are not kept), clamped outside the first and last point. The points are in order of t.
inline double sampleCurve(const std::vector<CurvePoint>& pts, double t) {
    if (pts.empty()) return 1.0;
    if (t <= pts.front().t) return pts.front().v;
    if (t >= pts.back().t) return pts.back().v;
    for (size_t i = 0; i + 1 < pts.size(); ++i) {
        if (t <= pts[i + 1].t) {
            const double span = pts[i + 1].t - pts[i].t;
            const double u = span > 1e-9 ? (t - pts[i].t) / span : 1.0;
            return pts[i].v + (pts[i + 1].v - pts[i].v) * u;
        }
    }
    return pts.back().v;
}

// The engine's four-stop curves hold at most `maxStops` points. A curve with that many or fewer keeps its own points; a longer one is sampled at evenly spaced
// times from its first point's to its last (the shape between the samples is lost, and so are tangents).
inline std::vector<CurvePoint> reduceCurve(const std::vector<CurvePoint>& pts, int maxStops = 4) {
    if (pts.empty() || maxStops < 2) return {};
    if (static_cast<int>(pts.size()) <= maxStops) return pts;
    std::vector<CurvePoint> out;
    const double a = pts.front().t, b = pts.back().t;
    for (int i = 0; i < maxStops; ++i) {
        const double t = a + (b - a) * static_cast<double>(i) / static_cast<double>(maxStops - 1);
        out.push_back({t, sampleCurve(pts, t)});
    }
    return out;
}

struct GradientStop {
    double t = 0;
    double rgba[4] = {1, 1, 1, 1};
};

// A gradient's colour at t (linear interpolation, clamped).
inline void sampleGradient(const std::vector<GradientStop>& g, double t, double out[4]) {
    if (g.empty()) { for (int k = 0; k < 4; ++k) out[k] = 1.0; return; }
    if (t <= g.front().t) { for (int k = 0; k < 4; ++k) out[k] = g.front().rgba[k]; return; }
    if (t >= g.back().t) { for (int k = 0; k < 4; ++k) out[k] = g.back().rgba[k]; return; }
    for (size_t i = 0; i + 1 < g.size(); ++i) {
        if (t <= g[i + 1].t) {
            const double span = g[i + 1].t - g[i].t;
            const double u = span > 1e-9 ? (t - g[i].t) / span : 1.0;
            for (int k = 0; k < 4; ++k) out[k] = g[i].rgba[k] + (g[i + 1].rgba[k] - g[i].rgba[k]) * u;
            return;
        }
    }
    for (int k = 0; k < 4; ++k) out[k] = g.back().rgba[k];
}

// At most `maxStops` stops over the particle's life 0..1: a gradient with that many or fewer keeps its stops; a longer one is sampled evenly (stops between are lost).
inline std::vector<GradientStop> reduceGradient(const std::vector<GradientStop>& g, int maxStops = 4) {
    if (g.empty() || maxStops < 2) return {};
    if (static_cast<int>(g.size()) <= maxStops) return g;
    std::vector<GradientStop> out;
    for (int i = 0; i < maxStops; ++i) {
        GradientStop s;
        s.t = static_cast<double>(i) / static_cast<double>(maxStops - 1);
        sampleGradient(g, s.t, s.rgba);
        out.push_back(s);
    }
    return out;
}

// ---- sprite sheets (particles_anim_h_frames / v_frames / loop with anim_speed and anim_offset) -----------------------------------------------------------

struct SubUvPlan {
    int cols = 1, rows = 1, frames = 1;
    bool perSecond = false;   // true: SUBUV ... fps <fps>; false: SUBUV ... life (the sheet once over the particle's life)
    double fps = 0;           // frames per second when perSecond
    double startMin = 0, startMax = 0;   // the first frame as a fraction of the sheet, per particle (spawn.animstart)
};

// Godot reads frame = (anim_offset + anim_speed * lifeFraction) * frames, wrapping with particles_anim_loop and clamping without it, with anim_speed and anim_offset drawn
// per particle in [min, max]. The engine has one rate per emitter, so the mean speed is used (the spread of speeds is lost); the offset range is kept as the start frame.
// A still sheet (speed 0) is a very slow rate, so the start frame is the frame.
inline SubUvPlan subUvPlan(int hFrames, int vFrames, bool loop, double speedMin, double speedMax, double offsetMin, double offsetMax, double lifetimeSeconds) {
    SubUvPlan p;
    p.cols = std::max(1, hFrames);
    p.rows = std::max(1, vFrames);
    p.frames = p.cols * p.rows;
    const double speed = 0.5 * (speedMin + speedMax);
    p.startMin = std::min(std::max(offsetMin, 0.0), 1.0);
    p.startMax = std::min(std::max(offsetMax, 0.0), 1.0);
    const double life = std::max(lifetimeSeconds, 1e-3);
    if (!loop && std::fabs(speed - 1.0) < 1e-6 && p.startMin == 0.0 && p.startMax == 0.0) {
        p.perSecond = false;   // once over the life, the engine's own life-driven mode
    } else {
        p.perSecond = true;
        p.fps = speed == 0.0 ? 1e-4 : std::fabs(speed) * p.frames / life;
    }
    return p;
}

// ---- trails (trail_enabled, trail_lifetime, RibbonTrailMesh / TubeTrailMesh) -----------------------------------------------------------------------------

struct TrailPlan {
    int points = 12;
    double segmentCm = 4;
};

// A Godot trail is the path of the last `trailLifetime` seconds. The engine's is a ring of recent positions, one pushed per `segmentCm` of travel, so the trail is as long
// as the particle's typical speed times that time, cut into `points - 1` segments. A particle that barely moves gets the smallest segment.
inline TrailPlan trailPlan(double trailLifetimeSeconds, double speedCmPerSecond, int points = 12) {
    TrailPlan t;
    t.points = std::min(std::max(points, 2), 32);
    const double length = std::fabs(speedCmPerSecond) * std::max(trailLifetimeSeconds, 0.0);
    t.segmentCm = std::max(length / static_cast<double>(t.points - 1), 1.0);
    return t;
}

// ---- attractors and colliders --------------------------------------------------------------------------------------------------------------------------

// Godot's attractor strength is a force in m/s^2 toward the centre; the engine's update.attractor pulls with (1 - d / radius)^falloff, so the strength is metres to
// centimetres and the attenuation is the exponent. Directionality (a push along the attractor's own axis) is not kept.
inline double attractorStrengthCm(double strength) { return toCm(strength); }

// "%.6g", the format the .ocparticle writer uses.
inline std::string num(double v) {
    char buf[40];
    if (v == 0.0) v = 0.0;   // a negative zero (the axis map negates) is written as 0
    std::snprintf(buf, sizeof buf, "%.6g", v);
    return buf;
}

}  // namespace aver::fmt::godotparticles
