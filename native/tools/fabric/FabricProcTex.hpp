#pragma once
// F26: the procedural texture resources of Godot (GradientTexture1D/2D, NoiseTexture2D with FastNoiseLite, CurveTexture) baked to RGBA8 pictures at convert time.
// Pure maths, no Godot types: FabricResources.cpp reads the properties and calls these. The noise is Godot's family (value, cubic value, Perlin, simplex,
// cellular, with the fractal modes) with this converter's own hash, so a picture matches Godot's in character and range, not texel for texel.
#include "aver/core/Types.hpp"

#include <vector>

namespace aver::fabric::proctex {

struct Rgba { float r = 0, g = 0, b = 0, a = 1; };

// Godot's Gradient: offsets ascending, one colour each; mode 0 linear, 1 constant, 2 cubic (blended as smoothstep here).
struct Gradient {
    std::vector<float> offsets;
    std::vector<Rgba> colors;
    int mode = 0;
    Rgba sample(float t) const;
};

struct Picture {
    int width = 0, height = 0;
    std::vector<u8> rgba;   // width * height * 4, straight alpha
    bool valid() const { return width > 0 && height > 0 && rgba.size() == static_cast<usize>(width) * static_cast<usize>(height) * 4; }
};

struct NoiseParams {
    int type = 5;              // FastNoiseLite.NoiseType: 0 value, 1 value cubic, 2 perlin, 3 cellular, 4 simplex, 5 simplex smooth
    int seed = 0;
    float frequency = 0.01f;
    float offset[3] = {0, 0, 0};
    int fractal = 1;           // 0 none, 1 fbm, 2 ridged, 3 ping pong
    int octaves = 5;
    float lacunarity = 2.0f;
    float gain = 0.5f;
    float weightedStrength = 0.0f;
    float pingPongStrength = 2.0f;
    int cellularDistance = 0;  // 0 euclidean, 1 euclidean squared, 2 manhattan, 3 hybrid
    float cellularJitter = 1.0f;
    int cellularReturn = 1;    // 0 cell value, 1 distance, 2 distance2, 3 distance2 add, 4 distance2 sub, 5 distance2 mul, 6 distance2 div
};

// The noise at a point, -1..1.
float noise2(const NoiseParams& p, float x, float y);

Picture gradient1D(const Gradient& g, int width);
// fill: 0 linear, 1 radial, 2 square; repeat: 0 none, 1 repeat, 2 mirror. from/to are in 0..1 of the picture.
Picture gradient2D(const Gradient& g, int width, int height, int fill, float fromX, float fromY, float toX, float toY, int repeat);

struct NoiseOptions {
    int width = 512, height = 512;
    bool invert = false, normalize = true, seamless = false, asNormalMap = false;
    float bumpStrength = 8.0f;
    const Gradient* ramp = nullptr;   // null: grey
};
Picture noiseImage(const NoiseParams& p, const NoiseOptions& o);

// A Curve as its sampled value: points are (x, y, leftTangent, rightTangent); a linear mode makes the tangent follow the neighbouring point.
struct CurvePoint { float x = 0, y = 0, leftTangent = 0, rightTangent = 0; int leftMode = 0, rightMode = 0; };
float sampleCurve(const std::vector<CurvePoint>& pts, float x);
// A one-row picture of the curve over its domain, the value mapped from [minValue, maxValue] to 0..1: red only (CurveTexture's RED mode) or grey in all three colour channels.
Picture curve1D(const std::vector<CurvePoint>& pts, float minDomain, float maxDomain, float minValue, float maxValue, int width, bool redOnly);

// A rectangle of a picture (the AtlasTexture region), clipped to it; the atlas texture's margin is added around it as transparent.
Picture crop(const Picture& src, int x, int y, int w, int h, int marginLeft, int marginTop, int marginRight, int marginBottom);

bool encodePng(const Picture& pic, std::vector<u8>& out);

}  // namespace aver::fabric::proctex
