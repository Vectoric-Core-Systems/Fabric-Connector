// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// The texture classes of Godot besides the file texture and the ImageTexture (F26): the aliases a project names (Texture, CompressedTexture2D, PlaceholderTexture2D), the
// procedural ones (GradientTexture1D/2D, NoiseTexture2D with FastNoiseLite, CurveTexture) and the two that re-use another texture (AtlasTexture, CanvasTexture). A scene's
// procedural textures are baked to PNG by the converter; these classes are what a script that builds one at run time gets, drawn from the pixels GetImage makes.

namespace Aver.Fabric;

/// <summary>Godot's Texture: the base of every texture. It reports a size and draws nothing by itself.</summary>
public class Texture : Resource
{
    public virtual Vector2 GetSize() => Vector2.Zero;
    public int GetWidth() => (int)GetSize().X;
    public int GetHeight() => (int)GetSize().Y;
}

/// <summary>A file texture that the project's import compressed (Godot's CompressedTexture2D). The converter copies the picture file, so this is a file texture.</summary>
public class CompressedTexture2D : Texture2D
{
    /// <summary>Points the texture at a picture file (Godot's load).</summary>
    public Error Load(string path)
    {
        ResourcePath = path;
        return ResourceLoader.Exists(path) ? Error.Ok : Error.FileNotFound;
    }

    public string GetLoadPath() => ResourcePath;
}

/// <summary>A texture that stands in for another of a given size (Godot's PlaceholderTexture2D): it has a size and no picture.</summary>
public class PlaceholderTexture2D : Texture2D
{
    public Vector2 Size { get; set; } = new(1, 1);
    public override Vector2 GetSize() => Size;
}

/// <summary>A texture whose picture a script's data decides, held as an <see cref="ImageTexture"/> made on first use. A material names the made texture; the UI draws it.</summary>
public abstract class GeneratedTexture : Texture2D
{
    private ImageTexture? _made;
    private bool _stale = true;

    /// <summary>Makes the picture. Called when the texture is first used and after <see cref="Invalidate"/>.</summary>
    public abstract override Image GetImage();

    /// <summary>Says a property changed: the picture is made again the next time the texture is used.</summary>
    protected void Invalidate()
    {
        _stale = true;
        EmitSignal(Resource.SignalName.Changed);
    }

    internal ImageTexture Made()
    {
        if (_made == null || _stale)
        {
            Image img = GetImage();
            if (_made == null) _made = ImageTexture.CreateFromImage(img);
            else _made.SetImage(img);
            _stale = false;
        }
        return _made;
    }

    internal override ulong EngineHandle => Made().EngineHandle;

    /// <summary>The texture-slot path a material names the made texture by.</summary>
    internal string MaterialPath => Made().MaterialPath;
}

public class GradientTexture1D : GeneratedTexture
{
    private Gradient? _gradient;
    private int _width = 256;
    public Gradient? Gradient { get => _gradient; set { _gradient = value; Invalidate(); } }
    public int Width { get => _width; set { _width = Math.Clamp(value, 1, 4096); Invalidate(); } }
    public bool UseHdr { get; set; }
    public override Vector2 GetSize() => new(_width, 1);

    public override Image GetImage()
    {
        var img = Image.Create(_width, 1, false, Image.Format.Rgba8);
        Gradient g = _gradient ?? new Gradient();
        for (int x = 0; x < _width; x++) img.SetPixel(x, 0, g.Sample(_width > 1 ? x / (float)(_width - 1) : 0f));
        return img;
    }
}

public class GradientTexture2D : GeneratedTexture
{
    public enum FillEnum { Linear = 0, Radial = 1, Square = 2 }
    public enum RepeatEnum { None = 0, Repeat = 1, Mirror = 2 }

    private Gradient? _gradient;
    private int _width = 64, _height = 64;
    private FillEnum _fill;
    private RepeatEnum _repeat;
    private Vector2 _from = Vector2.Zero, _to = new(1, 0);

    public Gradient? Gradient { get => _gradient; set { _gradient = value; Invalidate(); } }
    public int Width { get => _width; set { _width = Math.Clamp(value, 1, 4096); Invalidate(); } }
    public int Height { get => _height; set { _height = Math.Clamp(value, 1, 4096); Invalidate(); } }
    public FillEnum Fill { get => _fill; set { _fill = value; Invalidate(); } }
    public RepeatEnum Repeat { get => _repeat; set { _repeat = value; Invalidate(); } }
    public Vector2 FillFrom { get => _from; set { _from = value; Invalidate(); } }
    public Vector2 FillTo { get => _to; set { _to = value; Invalidate(); } }
    public bool UseHdr { get; set; }
    public override Vector2 GetSize() => new(_width, _height);

    public override Image GetImage()
    {
        var img = Image.Create(_width, _height, false, Image.Format.Rgba8);
        Gradient g = _gradient ?? new Gradient();
        float dx = _to.X - _from.X, dy = _to.Y - _from.Y, len2 = dx * dx + dy * dy;
        for (int y = 0; y < _height; y++)
            for (int x = 0; x < _width; x++)
            {
                float px = _width > 1 ? x / (float)(_width - 1) : 0f, py = _height > 1 ? y / (float)(_height - 1) : 0f;
                float ofs;
                if (_fill == FillEnum.Radial) ofs = len2 > 1e-9f ? MathF.Sqrt(((px - _from.X) * (px - _from.X) + (py - _from.Y) * (py - _from.Y)) / len2) : 0f;
                else if (_fill == FillEnum.Square)
                {
                    float m = MathF.Max(MathF.Abs(dx), MathF.Abs(dy));
                    ofs = m > 1e-9f ? MathF.Max(MathF.Abs(px - _from.X), MathF.Abs(py - _from.Y)) / m : 0f;
                }
                else ofs = len2 > 1e-9f ? ((px - _from.X) * dx + (py - _from.Y) * dy) / len2 : 0f;
                if (_repeat == RepeatEnum.Repeat) ofs -= MathF.Floor(ofs);
                else if (_repeat == RepeatEnum.Mirror) { ofs = MathF.Abs(ofs); ofs -= MathF.Floor(ofs * 0.5f) * 2f; if (ofs > 1f) ofs = 2f - ofs; }
                img.SetPixel(x, y, g.Sample(Math.Clamp(ofs, 0f, 1f)));
            }
        return img;
    }
}

public class CurveTexture : GeneratedTexture
{
    public enum TextureModeEnum { Rgb = 0, Red = 1 }

    private Curve? _curve;
    private int _width = 256;
    private TextureModeEnum _mode;
    public Curve? Curve { get => _curve; set { _curve = value; Invalidate(); } }
    public int Width { get => _width; set { _width = Math.Clamp(value, 1, 4096); Invalidate(); } }
    public TextureModeEnum TextureMode { get => _mode; set { _mode = value; Invalidate(); } }
    public override Vector2 GetSize() => new(_width, 1);

    public override Image GetImage()
    {
        var img = Image.Create(_width, 1, false, Image.Format.Rgba8);
        if (_curve == null) return img;
        float range = _curve.MaxValue - _curve.MinValue;
        for (int x = 0; x < _width; x++)
        {
            float t = _width > 1 ? x / (float)(_width - 1) : 0f;
            float v = range > 1e-9f ? (_curve.SampleBaked(t) - _curve.MinValue) / range : 0f;
            img.SetPixel(x, 0, _mode == TextureModeEnum.Red ? new Color(v, 0, 0, 1) : new Color(v, v, v, 1));
        }
        return img;
    }
}

/// <summary>The base of the noise resources (Godot's Noise): values from -1 to 1 at a point.</summary>
public class Noise : Resource
{
    public virtual float GetNoise1D(float x) => GetNoise2D(x, 0f);
    public virtual float GetNoise2D(float x, float y) => 0f;
    public virtual float GetNoise3D(float x, float y, float z) => GetNoise2D(x + z * 0.7f, y - z * 0.7f);
    public float GetNoise2Dv(Vector2 v) => GetNoise2D(v.X, v.Y);
    public float GetNoise3Dv(Vector3 v) => GetNoise3D(v.X, v.Y, v.Z);

    /// <summary>A picture of the noise: 0..1 grey, normalised to the picture's own range when <paramref name="normalize"/> is set.</summary>
    public Image GetImage(int width, int height, bool invert = false, bool in3dSpace = false, bool normalize = true)
    {
        var values = new float[width * height];
        float lo = float.MaxValue, hi = float.MinValue;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float n = in3dSpace ? GetNoise3D(x, y, 0f) : GetNoise2D(x, y);
                values[y * width + x] = n;
                lo = MathF.Min(lo, n); hi = MathF.Max(hi, n);
            }
        if (!normalize || hi - lo < 1e-6f) { lo = -1f; hi = 1f; }
        var img = Image.Create(width, height, false, Image.Format.L8);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float v = (values[y * width + x] - lo) / (hi - lo);
                if (invert) v = 1f - v;
                v = Math.Clamp(v, 0f, 1f);
                img.SetPixel(x, y, new Color(v, v, v, 1));
            }
        return img;
    }
}

/// <summary>Godot's FastNoiseLite. The same noise family and settings (value, cubic value, Perlin, simplex, cellular, the fractal modes), computed with this runtime's own hash:
/// the values have Godot's character and range and are not Godot's numbers. The converter's baked pictures use the same functions.</summary>
public class FastNoiseLite : Noise
{
    public enum NoiseTypeEnum { Simplex = 0, SimplexSmooth = 1, Cellular = 2, Perlin = 3, ValueCubic = 4, Value = 5 }
    public enum FractalTypeEnum { None = 0, Fbm = 1, Ridged = 2, PingPong = 3 }
    public enum CellularDistanceFunctionEnum { Euclidean = 0, EuclideanSquared = 1, Manhattan = 2, Hybrid = 3 }
    public enum CellularReturnTypeEnum { CellValue = 0, Distance = 1, Distance2 = 2, Distance2Add = 3, Distance2Sub = 4, Distance2Mul = 5, Distance2Div = 6 }
    public enum DomainWarpTypeEnum { Simplex = 0, SimplexReduced = 1, BasicGrid = 2 }

    public NoiseTypeEnum NoiseType { get; set; } = NoiseTypeEnum.SimplexSmooth;
    public int Seed { get; set; }
    public float Frequency { get; set; } = 0.01f;
    public Vector3 Offset { get; set; }
    public FractalTypeEnum FractalType { get; set; } = FractalTypeEnum.Fbm;
    public int FractalOctaves { get; set; } = 5;
    public float FractalLacunarity { get; set; } = 2f;
    public float FractalGain { get; set; } = 0.5f;
    public float FractalWeightedStrength { get; set; }
    public float FractalPingPongStrength { get; set; } = 2f;
    public CellularDistanceFunctionEnum CellularDistanceFunction { get; set; } = CellularDistanceFunctionEnum.Euclidean;
    public float CellularJitter { get; set; } = 1f;
    public CellularReturnTypeEnum CellularReturnType { get; set; } = CellularReturnTypeEnum.Distance;
    /// <summary>Stored: domain warping is not applied.</summary>
    public bool DomainWarpEnabled { get; set; }

    private static uint Hash(int seed, int x, int y)
    {
        unchecked
        {
            uint h = (uint)seed * 374761393u + (uint)x * 668265263u + (uint)y * 2246822519u;
            h = (h ^ (h >> 13)) * 1274126177u;
            return h ^ (h >> 16);
        }
    }

    private static float Rnd(uint h) => (h & 0xFFFFFFu) / 8388607.5f - 1f;
    private static float Smooth(float t) => t * t * (3f - 2f * t);
    private static float Quintic(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float ValueNoise(int seed, float x, float y, bool cubic)
    {
        int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y);
        float tx = x - xi, ty = y - yi;
        tx = cubic ? Smooth(tx) : Quintic(tx);
        ty = cubic ? Smooth(ty) : Quintic(ty);
        float a = Rnd(Hash(seed, xi, yi)), b = Rnd(Hash(seed, xi + 1, yi)), c = Rnd(Hash(seed, xi, yi + 1)), d = Rnd(Hash(seed, xi + 1, yi + 1));
        return Lerp(Lerp(a, b, tx), Lerp(c, d, tx), ty);
    }

    private static float GradDot(int seed, int xi, int yi, float dx, float dy)
    {
        float ang = (Rnd(Hash(seed, xi, yi)) + 1f) * 3.14159265f;
        return MathF.Cos(ang) * dx + MathF.Sin(ang) * dy;
    }

    private static float Perlin(int seed, float x, float y)
    {
        int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y);
        float fx = x - xi, fy = y - yi, u = Quintic(fx), v = Quintic(fy);
        float n00 = GradDot(seed, xi, yi, fx, fy), n10 = GradDot(seed, xi + 1, yi, fx - 1f, fy);
        float n01 = GradDot(seed, xi, yi + 1, fx, fy - 1f), n11 = GradDot(seed, xi + 1, yi + 1, fx - 1f, fy - 1f);
        return Math.Clamp(Lerp(Lerp(n00, n10, u), Lerp(n01, n11, u), v) * 1.4142f, -1f, 1f);
    }

    private static float Simplex(int seed, float x, float y, bool smoothKernel)
    {
        const float F2 = 0.3660254f, G2 = 0.2113249f;
        float s = (x + y) * F2;
        int i = (int)MathF.Floor(x + s), j = (int)MathF.Floor(y + s);
        float t = (i + j) * G2;
        float x0 = x - (i - t), y0 = y - (j - t);
        int i1 = x0 > y0 ? 1 : 0, j1 = x0 > y0 ? 0 : 1;
        Span<float> xs = stackalloc float[3] { x0, x0 - i1 + G2, x0 - 1f + 2f * G2 };
        Span<float> ys = stackalloc float[3] { y0, y0 - j1 + G2, y0 - 1f + 2f * G2 };
        Span<int> ci = stackalloc int[3] { i, i + i1, i + 1 };
        Span<int> cj = stackalloc int[3] { j, j + j1, j + 1 };
        float n = 0f;
        for (int k = 0; k < 3; k++)
        {
            float f = 0.5f - xs[k] * xs[k] - ys[k] * ys[k];
            if (f <= 0f) continue;
            f = smoothKernel ? f * f * f : f * f * f * f;
            n += f * GradDot(seed, ci[k], cj[k], xs[k], ys[k]);
        }
        return Math.Clamp(n * (smoothKernel ? 24f : 70f), -1f, 1f);
    }

    private float Cellular(int seed, float x, float y)
    {
        int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y);
        float d1 = 1e9f, d2 = 1e9f, cell = 0f;
        for (int oy = -1; oy <= 1; oy++)
            for (int ox = -1; ox <= 1; ox++)
            {
                int cx = xi + ox, cy = yi + oy;
                uint h = Hash(seed, cx, cy);
                float px = cx + 0.5f + Rnd(h) * 0.5f * CellularJitter, py = cy + 0.5f + Rnd(Hash(seed + 1, cx, cy)) * 0.5f * CellularJitter;
                float dx = px - x, dy = py - y;
                float d = CellularDistanceFunction switch
                {
                    CellularDistanceFunctionEnum.EuclideanSquared => dx * dx + dy * dy,
                    CellularDistanceFunctionEnum.Manhattan => MathF.Abs(dx) + MathF.Abs(dy),
                    CellularDistanceFunctionEnum.Hybrid => MathF.Sqrt(dx * dx + dy * dy) + MathF.Abs(dx) + MathF.Abs(dy),
                    _ => MathF.Sqrt(dx * dx + dy * dy),
                };
                if (d < d1) { d2 = d1; d1 = d; cell = Rnd(h); }
                else if (d < d2) d2 = d;
            }
        float r = CellularReturnType switch
        {
            CellularReturnTypeEnum.CellValue => float.NaN,
            CellularReturnTypeEnum.Distance2 => d2,
            CellularReturnTypeEnum.Distance2Add => d1 + d2,
            CellularReturnTypeEnum.Distance2Sub => d2 - d1,
            CellularReturnTypeEnum.Distance2Mul => d1 * d2,
            CellularReturnTypeEnum.Distance2Div => d2 > 1e-6f ? d1 / d2 : 0f,
            _ => d1,
        };
        return float.IsNaN(r) ? cell : Math.Clamp(r * 2f - 1f, -1f, 1f);
    }

    private float Base(float x, float y, int seed) => NoiseType switch
    {
        NoiseTypeEnum.Value => ValueNoise(seed, x, y, false),
        NoiseTypeEnum.ValueCubic => ValueNoise(seed, x, y, true),
        NoiseTypeEnum.Perlin => Perlin(seed, x, y),
        NoiseTypeEnum.Cellular => Cellular(seed, x, y),
        NoiseTypeEnum.Simplex => Simplex(seed, x, y, false),
        _ => Simplex(seed, x, y, true),
    };

    public override float GetNoise2D(float x, float y)
    {
        x = (x + Offset.X) * Frequency;
        y = (y + Offset.Y) * Frequency;
        if (FractalType == FractalTypeEnum.None || FractalOctaves <= 1) return Base(x, y, Seed);
        float sum = 0f, amp = 1f, bound = 0f;
        for (int o = 0; o < Math.Clamp(FractalOctaves, 1, 10); o++)
        {
            float n = Base(x, y, Seed + o);
            if (FractalType == FractalTypeEnum.Ridged) n = 1f - 2f * MathF.Abs(n);
            else if (FractalType == FractalTypeEnum.PingPong)
            {
                n = (n + 1f) * FractalPingPongStrength;
                n -= MathF.Floor(n * 0.5f) * 2f;
                n = (n < 1f ? n : 2f - n) * 2f - 1f;
            }
            sum += n * amp;
            bound += amp;
            amp *= FractalGain * (1f - FractalWeightedStrength + FractalWeightedStrength * (n + 1f) * 0.5f);
            x *= FractalLacunarity;
            y *= FractalLacunarity;
        }
        return bound > 0f ? Math.Clamp(sum / bound, -1f, 1f) : 0f;
    }
}

public class NoiseTexture2D : GeneratedTexture
{
    private Noise? _noise;
    private int _width = 512, _height = 512;
    private bool _invert, _seamless, _normalMap, _normalize = true;
    private float _bump = 8f;
    private Gradient? _ramp;

    public Noise? Noise { get => _noise; set { _noise = value; Invalidate(); } }
    public int Width { get => _width; set { _width = Math.Clamp(value, 1, 4096); Invalidate(); } }
    public int Height { get => _height; set { _height = Math.Clamp(value, 1, 4096); Invalidate(); } }
    public bool Invert { get => _invert; set { _invert = value; Invalidate(); } }
    /// <summary>Stored: the picture is not made tileable.</summary>
    public bool Seamless { get => _seamless; set { _seamless = value; Invalidate(); } }
    public float SeamlessBlendSkirt { get; set; } = 0.1f;
    public bool In3dSpace { get; set; }
    public bool AsNormalMap { get => _normalMap; set { _normalMap = value; Invalidate(); } }
    public float BumpStrength { get => _bump; set { _bump = value; Invalidate(); } }
    public bool Normalize { get => _normalize; set { _normalize = value; Invalidate(); } }
    public Gradient? ColorRamp { get => _ramp; set { _ramp = value; Invalidate(); } }
    public bool GenerateMipmaps { get; set; } = true;
    public override Vector2 GetSize() => new(_width, _height);

    public override Image GetImage()
    {
        if (_noise == null) return Image.Create(_width, _height, false, Image.Format.L8);
        Image grey = _noise.GetImage(_width, _height, _invert, In3dSpace, _normalize);
        if (!_normalMap && _ramp == null) return grey;
        var img = Image.Create(_width, _height, false, Image.Format.Rgba8);
        float H(int x, int y) => grey.GetPixel(Math.Clamp(x, 0, _width - 1), Math.Clamp(y, 0, _height - 1)).R;
        for (int y = 0; y < _height; y++)
            for (int x = 0; x < _width; x++)
            {
                if (_normalMap)
                {
                    float sx = (H(x + 1, y) - H(x - 1, y)) * _bump * 0.5f, sy = (H(x, y + 1) - H(x, y - 1)) * _bump * 0.5f;
                    float inv = 1f / MathF.Sqrt(sx * sx + sy * sy + 1f);
                    img.SetPixel(x, y, new Color(-sx * inv * 0.5f + 0.5f, -sy * inv * 0.5f + 0.5f, inv * 0.5f + 0.5f, 1f));
                }
                else img.SetPixel(x, y, _ramp!.Sample(H(x, y)));
            }
        return img;
    }
}

/// <summary>A region of another texture (Godot's AtlasTexture). Its size is the region's plus the margin; a UI draw uses the whole atlas picture, a script reads the region from <see cref="GetImage"/>.</summary>
public class AtlasTexture : Texture2D
{
    public Texture2D? Atlas { get; set; }
    public Rect2 Region { get; set; }
    public Rect2 Margin { get; set; }
    public bool FilterClip { get; set; }

    public override Vector2 GetSize()
    {
        Vector2 whole = Atlas?.GetSize() ?? Vector2.Zero;
        Vector2 inner = Region.Size.X > 0 && Region.Size.Y > 0 ? Region.Size : whole;
        return inner + Margin.Size;
    }

    public override Image GetImage()
    {
        Image? atlas = Atlas?.GetImage();
        if (atlas == null || atlas.IsEmpty()) return new Image();
        Rect2 r = Region.Size.X > 0 && Region.Size.Y > 0 ? Region : new Rect2(0, 0, atlas.GetWidth(), atlas.GetHeight());
        Image part = atlas.GetRegion(new Rect2I((int)r.Position.X, (int)r.Position.Y, (int)r.Size.X, (int)r.Size.Y));
        if (part.IsEmpty()) return part;
        int w = part.GetWidth() + (int)Margin.Size.X, h = part.GetHeight() + (int)Margin.Size.Y;
        var framed = Image.Create(w, h, false, Image.Format.Rgba8);
        framed.BlitRect(part, new Rect2I(0, 0, part.GetWidth(), part.GetHeight()), new Vector2I((int)Margin.Position.X, (int)Margin.Position.Y));
        return framed;
    }

    internal override ulong EngineHandle => Atlas?.EngineHandle ?? 0;
}

/// <summary>A texture with companion maps for 2D lighting (Godot's CanvasTexture). It draws as its diffuse texture; the normal and specular maps are kept and not used.</summary>
public class CanvasTexture : Texture2D
{
    public Texture2D? DiffuseTexture { get; set; }
    public Texture2D? NormalTexture { get; set; }
    public Texture2D? SpecularTexture { get; set; }
    public Color SpecularColor { get; set; } = new(1, 1, 1, 1);
    public float SpecularShininess { get; set; } = 1f;
    public int TextureFilter { get; set; }
    public int TextureRepeat { get; set; }
    public override Vector2 GetSize() => DiffuseTexture?.GetSize() ?? Vector2.Zero;
    public override Image GetImage() => DiffuseTexture?.GetImage() ?? new Image();
    internal override ulong EngineHandle => DiffuseTexture?.EngineHandle ?? 0;
}
