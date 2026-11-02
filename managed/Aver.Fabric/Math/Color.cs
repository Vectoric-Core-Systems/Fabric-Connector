// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Color (non-premultiplied, float channels) and the named colours scripts use most.

using System.Globalization;

namespace Aver.Fabric;

/// <summary>An RGBA colour with float channels in 0..1 (Godot's Color).</summary>
public struct Color : IEquatable<Color>
{
    public float R;
    public float G;
    public float B;
    public float A;

    public int R8 { readonly get => To8(R); set => R = value / 255f; }
    public int G8 { readonly get => To8(G); set => G = value / 255f; }
    public int B8 { readonly get => To8(B); set => B = value / 255f; }
    public int A8 { readonly get => To8(A); set => A = value / 255f; }

    public float H
    {
        readonly get
        {
            float min = Math.Min(R, Math.Min(G, B)), max = Math.Max(R, Math.Max(G, B)), delta = max - min;
            if (delta == 0f) return 0f;
            float h;
            if (R == max) h = (G - B) / delta;
            else if (G == max) h = 2f + (B - R) / delta;
            else h = 4f + (R - G) / delta;
            h /= 6f;
            if (h < 0f) h += 1f;
            return h;
        }
        set => this = FromHsv(value, S, V, A);
    }

    public float S
    {
        readonly get
        {
            float max = Math.Max(R, Math.Max(G, B)), min = Math.Min(R, Math.Min(G, B));
            return max == 0f ? 0f : (max - min) / max;
        }
        set => this = FromHsv(H, value, V, A);
    }

    public float V
    {
        readonly get => Math.Max(R, Math.Max(G, B));
        set => this = FromHsv(H, S, value, A);
    }

    public float this[int index]
    {
        readonly get => index switch { 0 => R, 1 => G, 2 => B, 3 => A, _ => throw new ArgumentOutOfRangeException(nameof(index)) };
        set
        {
            switch (index)
            {
                case 0: R = value; break;
                case 1: G = value; break;
                case 2: B = value; break;
                case 3: A = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
    }

    private static int To8(float v) => (int)(Math.Clamp(v, 0f, 1f) * 255f + 0.5f);

    public Color(float r, float g, float b, float a = 1f) { R = r; G = g; B = b; A = a; }
    public Color(Color c, float a = 1f) { R = c.R; G = c.G; B = c.B; A = a; }

    /// <summary>From a name ("red") or an HTML hex code ("#ff8800", "ff880080", "#f80").</summary>
    public Color(string code)
    {
        if (!TryParse(code, out Color c)) throw new ArgumentException($"Invalid color code: '{code}'.", nameof(code));
        this = c;
    }

    /// <summary>From a name or hex code, or <paramref name="fallback"/> when it is neither.</summary>
    public Color(string code, Color fallback)
    {
        this = TryParse(code, out Color c) ? c : fallback;
    }

    /// <summary>From 0..255 channels.</summary>
    public static Color Color8(byte r8, byte g8, byte b8, byte a8 = 255) => new(r8 / 255f, g8 / 255f, b8 / 255f, a8 / 255f);

    public static Color FromHtml(string html) => TryParse(html, out Color c) ? c : throw new ArgumentException($"Invalid color code: '{html}'.", nameof(html));
    public static bool HtmlIsValid(string color) => TryParse(color, out _);

    /// <summary>From a 32-bit 0xRRGGBBAA value.</summary>
    public static Color FromRgba32(uint rgba) => Color8((byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba);

    public static Color FromHsv(float hue, float saturation, float value, float alpha = 1f)
    {
        if (saturation == 0f) return new Color(value, value, value, alpha);
        hue = (hue * 6f) % 6f;
        if (hue < 0f) hue += 6f;
        int i = (int)MathF.Floor(hue);
        float f = hue - i, p = value * (1f - saturation), q = value * (1f - saturation * f), t = value * (1f - saturation * (1f - f));
        return i switch
        {
            0 => new Color(value, t, p, alpha),
            1 => new Color(q, value, p, alpha),
            2 => new Color(p, value, t, alpha),
            3 => new Color(p, q, value, alpha),
            4 => new Color(t, p, value, alpha),
            _ => new Color(value, p, q, alpha),
        };
    }

    private static bool TryParse(string? code, out Color c)
    {
        c = default;
        if (string.IsNullOrEmpty(code)) return false;
        string s = code.Trim();
        if (s.StartsWith('#')) s = s[1..];
        else if (Colors.TryGetNamed(s, out c)) return true;
        if (s.Length != 3 && s.Length != 4 && s.Length != 6 && s.Length != 8) return false;
        int[] v = new int[s.Length];
        for (int i = 0; i < s.Length; i++)
        {
            if (!Uri.IsHexDigit(s[i])) return false;
            v[i] = Convert.ToInt32(s[i].ToString(), 16);
        }
        if (s.Length <= 4)
        {
            c = new Color(v[0] * 17 / 255f, v[1] * 17 / 255f, v[2] * 17 / 255f, s.Length == 4 ? v[3] * 17 / 255f : 1f);
        }
        else
        {
            c = new Color((v[0] * 16 + v[1]) / 255f, (v[2] * 16 + v[3]) / 255f, (v[4] * 16 + v[5]) / 255f, s.Length == 8 ? (v[6] * 16 + v[7]) / 255f : 1f);
        }
        return true;
    }

    public readonly Color Blend(Color over)
    {
        float sa = 1f - over.A, a = A * sa + over.A;
        if (a == 0f) return new Color(0, 0, 0, 0);
        return new Color((R * A * sa + over.R * over.A) / a, (G * A * sa + over.G * over.A) / a, (B * A * sa + over.B * over.A) / a, a);
    }

    public readonly Color Clamp(Color? min = null, Color? max = null)
    {
        Color lo = min ?? new Color(0, 0, 0, 0), hi = max ?? new Color(1, 1, 1, 1);
        return new Color(Math.Clamp(R, lo.R, hi.R), Math.Clamp(G, lo.G, hi.G), Math.Clamp(B, lo.B, hi.B), Math.Clamp(A, lo.A, hi.A));
    }

    public readonly Color Darkened(float amount) => new(R * (1f - amount), G * (1f - amount), B * (1f - amount), A);
    public readonly Color Lightened(float amount) => new(R + (1f - R) * amount, G + (1f - G) * amount, B + (1f - B) * amount, A);
    public readonly Color Inverted() => new(1f - R, 1f - G, 1f - B, A);
    public readonly float Luminance() => 0.2126f * R + 0.7152f * G + 0.0722f * B;
    public readonly Color Lerp(Color to, float weight) =>
        new(Mathf.Lerp(R, to.R, weight), Mathf.Lerp(G, to.G, weight), Mathf.Lerp(B, to.B, weight), Mathf.Lerp(A, to.A, weight));
    public readonly bool IsEqualApprox(Color other) =>
        Mathf.IsEqualApprox(R, other.R) && Mathf.IsEqualApprox(G, other.G) && Mathf.IsEqualApprox(B, other.B) && Mathf.IsEqualApprox(A, other.A);

    public readonly Color SrgbToLinear() => new(Lin(R), Lin(G), Lin(B), A);
    public readonly Color LinearToSrgb() => new(Srgb(R), Srgb(G), Srgb(B), A);
    private static float Lin(float c) => c < 0.04045f ? c * (1f / 12.92f) : MathF.Pow((c + 0.055f) * (1f / 1.055f), 2.4f);
    private static float Srgb(float c) => c < 0.0031308f ? 12.92f * c : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;

    public readonly uint ToRgba32() => ((uint)R8 << 24) | ((uint)G8 << 16) | ((uint)B8 << 8) | (uint)A8;
    public readonly uint ToArgb32() => ((uint)A8 << 24) | ((uint)R8 << 16) | ((uint)G8 << 8) | (uint)B8;
    public readonly uint ToAbgr32() => ((uint)A8 << 24) | ((uint)B8 << 16) | ((uint)G8 << 8) | (uint)R8;
    public readonly ulong ToRgba64() => ((ulong)To16(R) << 48) | ((ulong)To16(G) << 32) | ((ulong)To16(B) << 16) | To16(A);
    public readonly ulong ToArgb64() => ((ulong)To16(A) << 48) | ((ulong)To16(R) << 32) | ((ulong)To16(G) << 16) | To16(B);
    public readonly ulong ToAbgr64() => ((ulong)To16(A) << 48) | ((ulong)To16(B) << 32) | ((ulong)To16(G) << 16) | To16(R);
    private static ushort To16(float v) => (ushort)(Math.Clamp(v, 0f, 1f) * 65535f + 0.5f);

    /// <summary>The same as <see cref="Luminance"/>: GDScript's get_luminance.</summary>
    public readonly float GetLuminance() => Luminance();

    /// <summary>From a 32-bit 0xRRGGBBAA value (GDScript's Color.hex).</summary>
    public static Color Hex(uint hex) => FromRgba32(hex);

    /// <summary>From a 64-bit 0xRRRRGGGGBBBBAAAA value.</summary>
    public static Color Hex64(ulong hex) => new(
        ((hex >> 48) & 0xFFFF) / 65535f, ((hex >> 32) & 0xFFFF) / 65535f, ((hex >> 16) & 0xFFFF) / 65535f, (hex & 0xFFFF) / 65535f);

    /// <summary>From 0..255 channels (GDScript's Color.from_rgba8).</summary>
    public static Color FromRgba8(long r8, long g8, long b8, long a8 = 255) => new(r8 / 255f, g8 / 255f, b8 / 255f, a8 / 255f);

    /// <summary>From a name or an HTML hex code, or <paramref name="default"/> when it is neither.</summary>
    public static Color FromString(string str, Color @default) => TryParse(str, out Color c) ? c : @default;

    /// <summary>From a shared-exponent value (RGB9E5: 9 bits for each of r, g, b and 5 for the exponent), as the engine stores HDR textures.</summary>
    public static Color FromRgbe9995(uint rgbe)
    {
        float r = rgbe & 0x1ff, g = (rgbe >> 9) & 0x1ff, b = (rgbe >> 18) & 0x1ff, e = rgbe >> 27;
        float m = MathF.Pow(2f, e - 15f - 9f);
        return new Color(r * m, g * m, b * m, 1f);
    }

    /// <summary>The colour as lowercase hex, "rrggbbaa" or "rrggbb".</summary>
    public readonly string ToHtml(bool includeAlpha = true)
    {
        string s = $"{R8:x2}{G8:x2}{B8:x2}";
        return includeAlpha ? s + $"{A8:x2}" : s;
    }

    public static Color operator +(Color l, Color r) => new(l.R + r.R, l.G + r.G, l.B + r.B, l.A + r.A);
    public static Color operator -(Color l, Color r) => new(l.R - r.R, l.G - r.G, l.B - r.B, l.A - r.A);
    public static Color operator -(Color c) => new(1f - c.R, 1f - c.G, 1f - c.B, 1f - c.A);
    public static Color operator *(Color c, float s) => new(c.R * s, c.G * s, c.B * s, c.A * s);
    public static Color operator *(float s, Color c) => new(c.R * s, c.G * s, c.B * s, c.A * s);
    public static Color operator *(Color l, Color r) => new(l.R * r.R, l.G * r.G, l.B * r.B, l.A * r.A);
    public static Color operator /(Color c, float s) => new(c.R / s, c.G / s, c.B / s, c.A / s);
    public static bool operator ==(Color l, Color r) => l.Equals(r);
    public static bool operator !=(Color l, Color r) => !l.Equals(r);

    public readonly bool Equals(Color other) => R == other.R && G == other.G && B == other.B && A == other.A;
    public override readonly bool Equals(object? obj) => obj is Color c && Equals(c);
    public override readonly int GetHashCode() => HashCode.Combine(R, G, B, A);
    public override readonly string ToString() => string.Create(CultureInfo.InvariantCulture, $"({R}, {G}, {B}, {A})");
}

/// <summary>The named colours of Godot's palette that scripts use most (a subset of its full list).</summary>
public static class Colors
{
    private static readonly Dictionary<string, Color> s_named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["aliceblue"] = new(0.94f, 0.97f, 1f), ["aqua"] = new(0f, 1f, 1f), ["azure"] = new(0.94f, 1f, 1f), ["beige"] = new(0.96f, 0.96f, 0.86f),
        ["black"] = new(0f, 0f, 0f), ["blue"] = new(0f, 0f, 1f), ["brown"] = new(0.65f, 0.16f, 0.16f), ["chocolate"] = new(0.82f, 0.41f, 0.12f),
        ["coral"] = new(1f, 0.5f, 0.31f), ["crimson"] = new(0.86f, 0.08f, 0.24f), ["cyan"] = new(0f, 1f, 1f), ["darkblue"] = new(0f, 0f, 0.55f),
        ["darkgray"] = new(0.66f, 0.66f, 0.66f), ["darkgreen"] = new(0f, 0.39f, 0f), ["darkorange"] = new(1f, 0.55f, 0f), ["darkred"] = new(0.55f, 0f, 0f),
        ["deeppink"] = new(1f, 0.08f, 0.58f), ["dimgray"] = new(0.41f, 0.41f, 0.41f), ["dodgerblue"] = new(0.12f, 0.56f, 1f),
        ["forestgreen"] = new(0.13f, 0.55f, 0.13f), ["fuchsia"] = new(1f, 0f, 1f), ["gold"] = new(1f, 0.84f, 0f), ["gray"] = new(0.75f, 0.75f, 0.75f),
        ["green"] = new(0f, 1f, 0f), ["hotpink"] = new(1f, 0.41f, 0.71f), ["indigo"] = new(0.29f, 0f, 0.51f), ["ivory"] = new(1f, 1f, 0.94f),
        ["khaki"] = new(0.94f, 0.9f, 0.55f), ["lavender"] = new(0.9f, 0.9f, 0.98f), ["lightblue"] = new(0.68f, 0.85f, 0.9f), ["lightgray"] = new(0.83f, 0.83f, 0.83f),
        ["lightgreen"] = new(0.56f, 0.93f, 0.56f), ["lime"] = new(0f, 1f, 0f), ["magenta"] = new(1f, 0f, 1f), ["maroon"] = new(0.69f, 0.19f, 0.38f),
        ["navyblue"] = new(0f, 0f, 0.5f), ["olive"] = new(0.5f, 0.5f, 0f), ["orange"] = new(1f, 0.65f, 0f), ["orchid"] = new(0.85f, 0.44f, 0.84f),
        ["pink"] = new(1f, 0.75f, 0.8f), ["purple"] = new(0.63f, 0.13f, 0.94f), ["red"] = new(1f, 0f, 0f), ["salmon"] = new(0.98f, 0.5f, 0.45f),
        ["silver"] = new(0.75f, 0.75f, 0.75f), ["skyblue"] = new(0.53f, 0.81f, 0.92f), ["tan"] = new(0.82f, 0.71f, 0.55f), ["teal"] = new(0f, 0.5f, 0.5f),
        ["tomato"] = new(1f, 0.39f, 0.28f), ["transparent"] = new(1f, 1f, 1f, 0f), ["turquoise"] = new(0.25f, 0.88f, 0.82f), ["violet"] = new(0.93f, 0.51f, 0.93f),
        ["webgray"] = new(0.5f, 0.5f, 0.5f), ["webgreen"] = new(0f, 0.5f, 0f), ["webmaroon"] = new(0.5f, 0f, 0f), ["webpurple"] = new(0.5f, 0f, 0.5f),
        ["antiquewhite"] = new(0.98f, 0.92f, 0.84f), ["aquamarine"] = new(0.5f, 1f, 0.83f), ["bisque"] = new(1f, 0.89f, 0.77f), ["blanchedalmond"] = new(1f, 0.92f, 0.8f),
        ["blueviolet"] = new(0.54f, 0.17f, 0.89f), ["burlywood"] = new(0.87f, 0.72f, 0.53f), ["cadetblue"] = new(0.37f, 0.62f, 0.63f), ["chartreuse"] = new(0.5f, 1f, 0f),
        ["cornflowerblue"] = new(0.39f, 0.58f, 0.93f), ["cornsilk"] = new(1f, 0.97f, 0.86f), ["darkcyan"] = new(0f, 0.55f, 0.55f), ["darkgoldenrod"] = new(0.72f, 0.53f, 0.04f),
        ["darkkhaki"] = new(0.74f, 0.72f, 0.42f), ["darkmagenta"] = new(0.55f, 0f, 0.55f), ["darkolivegreen"] = new(0.33f, 0.42f, 0.18f), ["darkorchid"] = new(0.6f, 0.2f, 0.8f),
        ["darksalmon"] = new(0.91f, 0.59f, 0.48f), ["darkseagreen"] = new(0.56f, 0.74f, 0.56f), ["darkslateblue"] = new(0.28f, 0.24f, 0.55f), ["darkslategray"] = new(0.18f, 0.31f, 0.31f),
        ["darkturquoise"] = new(0f, 0.81f, 0.82f), ["darkviolet"] = new(0.58f, 0f, 0.83f), ["deepskyblue"] = new(0f, 0.75f, 1f), ["firebrick"] = new(0.7f, 0.13f, 0.13f),
        ["floralwhite"] = new(1f, 0.98f, 0.94f), ["gainsboro"] = new(0.86f, 0.86f, 0.86f), ["ghostwhite"] = new(0.97f, 0.97f, 1f), ["goldenrod"] = new(0.85f, 0.65f, 0.13f),
        ["greenyellow"] = new(0.68f, 1f, 0.18f), ["honeydew"] = new(0.94f, 1f, 0.94f), ["indianred"] = new(0.8f, 0.36f, 0.36f), ["lavenderblush"] = new(1f, 0.94f, 0.96f),
        ["lawngreen"] = new(0.49f, 0.99f, 0f), ["lemonchiffon"] = new(1f, 0.98f, 0.8f), ["lightcoral"] = new(0.94f, 0.5f, 0.5f), ["lightcyan"] = new(0.88f, 1f, 1f),
        ["lightgoldenrod"] = new(0.98f, 0.98f, 0.82f), ["lightpink"] = new(1f, 0.71f, 0.76f), ["lightsalmon"] = new(1f, 0.63f, 0.48f), ["lightseagreen"] = new(0.13f, 0.7f, 0.67f),
        ["lightskyblue"] = new(0.53f, 0.81f, 0.98f), ["lightslategray"] = new(0.47f, 0.53f, 0.6f), ["lightsteelblue"] = new(0.69f, 0.77f, 0.87f), ["lightyellow"] = new(1f, 1f, 0.88f),
        ["limegreen"] = new(0.2f, 0.8f, 0.2f), ["linen"] = new(0.98f, 0.94f, 0.9f), ["mediumaquamarine"] = new(0.4f, 0.8f, 0.67f), ["mediumblue"] = new(0f, 0f, 0.8f),
        ["mediumorchid"] = new(0.73f, 0.33f, 0.83f), ["mediumpurple"] = new(0.58f, 0.44f, 0.86f), ["mediumseagreen"] = new(0.24f, 0.7f, 0.44f), ["mediumslateblue"] = new(0.48f, 0.41f, 0.93f),
        ["mediumspringgreen"] = new(0f, 0.98f, 0.6f), ["mediumturquoise"] = new(0.28f, 0.82f, 0.8f), ["mediumvioletred"] = new(0.78f, 0.08f, 0.52f), ["midnightblue"] = new(0.1f, 0.1f, 0.44f),
        ["mintcream"] = new(0.96f, 1f, 0.98f), ["mistyrose"] = new(1f, 0.89f, 0.88f), ["moccasin"] = new(1f, 0.89f, 0.71f), ["navajowhite"] = new(1f, 0.87f, 0.68f),
        ["oldlace"] = new(0.99f, 0.96f, 0.9f), ["olivedrab"] = new(0.42f, 0.56f, 0.14f), ["orangered"] = new(1f, 0.27f, 0f), ["palegoldenrod"] = new(0.93f, 0.91f, 0.67f),
        ["palegreen"] = new(0.6f, 0.98f, 0.6f), ["paleturquoise"] = new(0.69f, 0.93f, 0.93f), ["palevioletred"] = new(0.86f, 0.44f, 0.58f), ["papayawhip"] = new(1f, 0.94f, 0.84f),
        ["peachpuff"] = new(1f, 0.85f, 0.73f), ["peru"] = new(0.8f, 0.52f, 0.25f), ["plum"] = new(0.87f, 0.63f, 0.87f), ["powderblue"] = new(0.69f, 0.88f, 0.9f),
        ["rebeccapurple"] = new(0.4f, 0.2f, 0.6f), ["rosybrown"] = new(0.74f, 0.56f, 0.56f), ["royalblue"] = new(0.25f, 0.41f, 0.88f), ["saddlebrown"] = new(0.55f, 0.27f, 0.07f),
        ["sandybrown"] = new(0.96f, 0.64f, 0.38f), ["seagreen"] = new(0.18f, 0.55f, 0.34f), ["seashell"] = new(1f, 0.96f, 0.93f), ["sienna"] = new(0.63f, 0.32f, 0.18f),
        ["slateblue"] = new(0.42f, 0.35f, 0.8f), ["slategray"] = new(0.44f, 0.5f, 0.56f), ["snow"] = new(1f, 0.98f, 0.98f), ["springgreen"] = new(0f, 1f, 0.5f),
        ["steelblue"] = new(0.27f, 0.51f, 0.71f), ["thistle"] = new(0.85f, 0.75f, 0.85f), ["wheat"] = new(0.96f, 0.87f, 0.7f), ["whitesmoke"] = new(0.96f, 0.96f, 0.96f),
        ["yellowgreen"] = new(0.6f, 0.8f, 0.2f),
        ["white"] = new(1f, 1f, 1f), ["yellow"] = new(1f, 1f, 0f),
    };

    internal static bool TryGetNamed(string name, out Color color) =>
        s_named.TryGetValue(name.Replace("_", "").Replace(" ", ""), out color);

    public static Color AliceBlue => s_named["aliceblue"];
    public static Color Aqua => s_named["aqua"];
    public static Color Azure => s_named["azure"];
    public static Color Beige => s_named["beige"];
    public static Color Black => s_named["black"];
    public static Color Blue => s_named["blue"];
    public static Color Brown => s_named["brown"];
    public static Color Chocolate => s_named["chocolate"];
    public static Color Coral => s_named["coral"];
    public static Color Crimson => s_named["crimson"];
    public static Color Cyan => s_named["cyan"];
    public static Color DarkBlue => s_named["darkblue"];
    public static Color DarkGray => s_named["darkgray"];
    public static Color DarkGreen => s_named["darkgreen"];
    public static Color DarkOrange => s_named["darkorange"];
    public static Color DarkRed => s_named["darkred"];
    public static Color DeepPink => s_named["deeppink"];
    public static Color DimGray => s_named["dimgray"];
    public static Color DodgerBlue => s_named["dodgerblue"];
    public static Color ForestGreen => s_named["forestgreen"];
    public static Color Fuchsia => s_named["fuchsia"];
    public static Color Gold => s_named["gold"];
    public static Color Gray => s_named["gray"];
    public static Color Green => s_named["green"];
    public static Color HotPink => s_named["hotpink"];
    public static Color Indigo => s_named["indigo"];
    public static Color Ivory => s_named["ivory"];
    public static Color Khaki => s_named["khaki"];
    public static Color Lavender => s_named["lavender"];
    public static Color LightBlue => s_named["lightblue"];
    public static Color LightGray => s_named["lightgray"];
    public static Color LightGreen => s_named["lightgreen"];
    public static Color Lime => s_named["lime"];
    public static Color Magenta => s_named["magenta"];
    public static Color Maroon => s_named["maroon"];
    public static Color NavyBlue => s_named["navyblue"];
    public static Color Olive => s_named["olive"];
    public static Color Orange => s_named["orange"];
    public static Color Orchid => s_named["orchid"];
    public static Color Pink => s_named["pink"];
    public static Color Purple => s_named["purple"];
    public static Color Red => s_named["red"];
    public static Color Salmon => s_named["salmon"];
    public static Color Silver => s_named["silver"];
    public static Color SkyBlue => s_named["skyblue"];
    public static Color Tan => s_named["tan"];
    public static Color Teal => s_named["teal"];
    public static Color Tomato => s_named["tomato"];
    public static Color Transparent => s_named["transparent"];
    public static Color Turquoise => s_named["turquoise"];
    public static Color Violet => s_named["violet"];
    public static Color WebGray => s_named["webgray"];
    public static Color WebGreen => s_named["webgreen"];
    public static Color WebMaroon => s_named["webmaroon"];
    public static Color WebPurple => s_named["webpurple"];
    public static Color White => s_named["white"];
    public static Color Yellow => s_named["yellow"];
    public static Color AntiqueWhite => s_named["antiquewhite"];
    public static Color Aquamarine => s_named["aquamarine"];
    public static Color Bisque => s_named["bisque"];
    public static Color BlanchedAlmond => s_named["blanchedalmond"];
    public static Color BlueViolet => s_named["blueviolet"];
    public static Color Burlywood => s_named["burlywood"];
    public static Color CadetBlue => s_named["cadetblue"];
    public static Color Chartreuse => s_named["chartreuse"];
    public static Color CornflowerBlue => s_named["cornflowerblue"];
    public static Color Cornsilk => s_named["cornsilk"];
    public static Color DarkCyan => s_named["darkcyan"];
    public static Color DarkGoldenrod => s_named["darkgoldenrod"];
    public static Color DarkKhaki => s_named["darkkhaki"];
    public static Color DarkMagenta => s_named["darkmagenta"];
    public static Color DarkOliveGreen => s_named["darkolivegreen"];
    public static Color DarkOrchid => s_named["darkorchid"];
    public static Color DarkSalmon => s_named["darksalmon"];
    public static Color DarkSeaGreen => s_named["darkseagreen"];
    public static Color DarkSlateBlue => s_named["darkslateblue"];
    public static Color DarkSlateGray => s_named["darkslategray"];
    public static Color DarkTurquoise => s_named["darkturquoise"];
    public static Color DarkViolet => s_named["darkviolet"];
    public static Color DeepSkyBlue => s_named["deepskyblue"];
    public static Color Firebrick => s_named["firebrick"];
    public static Color FloralWhite => s_named["floralwhite"];
    public static Color Gainsboro => s_named["gainsboro"];
    public static Color GhostWhite => s_named["ghostwhite"];
    public static Color Goldenrod => s_named["goldenrod"];
    public static Color GreenYellow => s_named["greenyellow"];
    public static Color Honeydew => s_named["honeydew"];
    public static Color IndianRed => s_named["indianred"];
    public static Color LavenderBlush => s_named["lavenderblush"];
    public static Color LawnGreen => s_named["lawngreen"];
    public static Color LemonChiffon => s_named["lemonchiffon"];
    public static Color LightCoral => s_named["lightcoral"];
    public static Color LightCyan => s_named["lightcyan"];
    public static Color LightGoldenrod => s_named["lightgoldenrod"];
    public static Color LightPink => s_named["lightpink"];
    public static Color LightSalmon => s_named["lightsalmon"];
    public static Color LightSeaGreen => s_named["lightseagreen"];
    public static Color LightSkyBlue => s_named["lightskyblue"];
    public static Color LightSlateGray => s_named["lightslategray"];
    public static Color LightSteelBlue => s_named["lightsteelblue"];
    public static Color LightYellow => s_named["lightyellow"];
    public static Color LimeGreen => s_named["limegreen"];
    public static Color Linen => s_named["linen"];
    public static Color MediumAquamarine => s_named["mediumaquamarine"];
    public static Color MediumBlue => s_named["mediumblue"];
    public static Color MediumOrchid => s_named["mediumorchid"];
    public static Color MediumPurple => s_named["mediumpurple"];
    public static Color MediumSeaGreen => s_named["mediumseagreen"];
    public static Color MediumSlateBlue => s_named["mediumslateblue"];
    public static Color MediumSpringGreen => s_named["mediumspringgreen"];
    public static Color MediumTurquoise => s_named["mediumturquoise"];
    public static Color MediumVioletRed => s_named["mediumvioletred"];
    public static Color MidnightBlue => s_named["midnightblue"];
    public static Color MintCream => s_named["mintcream"];
    public static Color MistyRose => s_named["mistyrose"];
    public static Color Moccasin => s_named["moccasin"];
    public static Color NavajoWhite => s_named["navajowhite"];
    public static Color OldLace => s_named["oldlace"];
    public static Color OliveDrab => s_named["olivedrab"];
    public static Color OrangeRed => s_named["orangered"];
    public static Color PaleGoldenrod => s_named["palegoldenrod"];
    public static Color PaleGreen => s_named["palegreen"];
    public static Color PaleTurquoise => s_named["paleturquoise"];
    public static Color PaleVioletRed => s_named["palevioletred"];
    public static Color PapayaWhip => s_named["papayawhip"];
    public static Color PeachPuff => s_named["peachpuff"];
    public static Color Peru => s_named["peru"];
    public static Color Plum => s_named["plum"];
    public static Color PowderBlue => s_named["powderblue"];
    public static Color RebeccaPurple => s_named["rebeccapurple"];
    public static Color RosyBrown => s_named["rosybrown"];
    public static Color RoyalBlue => s_named["royalblue"];
    public static Color SaddleBrown => s_named["saddlebrown"];
    public static Color SandyBrown => s_named["sandybrown"];
    public static Color SeaGreen => s_named["seagreen"];
    public static Color Seashell => s_named["seashell"];
    public static Color Sienna => s_named["sienna"];
    public static Color SlateBlue => s_named["slateblue"];
    public static Color SlateGray => s_named["slategray"];
    public static Color Snow => s_named["snow"];
    public static Color SpringGreen => s_named["springgreen"];
    public static Color SteelBlue => s_named["steelblue"];
    public static Color Thistle => s_named["thistle"];
    public static Color Wheat => s_named["wheat"];
    public static Color WhiteSmoke => s_named["whitesmoke"];
    public static Color YellowGreen => s_named["yellowgreen"];
}
