// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Godot's Image and ImageTexture (E8) over the engine's CPU image (Aver.Framework.Image) and runtime texture (Aver.Framework.RuntimeTexture). An Image is plain
// pixels and runs without an engine; an ImageTexture uploads its pixels to the GPU the frame after they are set, and a material or a UI draw names it.

using System.IO.Compression;
using FwFormat = Aver.Framework.ImageFormat;
using FwImage = Aver.Framework.Image;
using FwTexture = Aver.Framework.RuntimeTexture;

namespace Aver.Fabric;

/// <summary>A picture in memory (Godot's Image). The formats the engine stores natively (L8, LA8, RGB8, RGBA8, RF, RGBAF) are kept as they are; the other Godot formats are held
/// in the nearest of those and report the format they were asked for. Compressed formats and mip chains are not stored: <see cref="GenerateMipmaps"/> only marks the image, and the
/// ImageTexture builds the chain when it uploads.</summary>
public class Image : Resource
{
    public enum Format
    {
        L8 = 0, La8 = 1, R8 = 2, Rg8 = 3, Rgb8 = 4, Rgba8 = 5, Rgba4444 = 6, Rgb565 = 7, Rf = 8, Rgf = 9, Rgbf = 10, Rgbaf = 11, Rh = 12, Rgh = 13, Rgbh = 14, Rgbah = 15, Rgbe9995 = 16, Max = 17,
    }

    public enum Interpolation { Nearest = 0, Bilinear = 1, Cubic = 2, Trilinear = 3, Lanczos = 4 }

    private FwImage? _img;
    private Format _format = Format.Rgba8;
    private bool _mipmaps;

    public Image() { }

    private Image(FwImage img, Format format, bool mipmaps) { _img = img; _format = format; _mipmaps = mipmaps; }

    // The engine format a Godot format is held in.
    internal static FwFormat Store(Format f) => f switch
    {
        Format.L8 or Format.R8 => FwFormat.L8,
        Format.La8 => FwFormat.LA8,
        Format.Rg8 or Format.Rgb8 or Format.Rgb565 => FwFormat.RGB8,
        Format.Rgba8 or Format.Rgba4444 => FwFormat.RGBA8,
        Format.Rf => FwFormat.RF,
        _ => FwFormat.RGBAF,
    };

    /// <summary>A new image of the given size, every channel 0 (Godot's Image.Create).</summary>
    public static Image Create(int width, int height, bool useMipmaps, Format format)
    {
        if (width < 1 || height < 1 || width > Aver.Framework.Image.MaxSide || height > Aver.Framework.Image.MaxSide) return new Image();
        return new Image(new FwImage(width, height, Store(format)), format, useMipmaps);
    }

    /// <summary>Godot's Image.CreateEmpty: the same as <see cref="Create"/>.</summary>
    public static Image CreateEmpty(int width, int height, bool useMipmaps, Format format) => Create(width, height, useMipmaps, format);

    /// <summary>An image over a copy of <paramref name="data"/>, which must hold the bytes of the stored format (the first level only). An empty image when the size or the byte count is wrong.</summary>
    public static Image CreateFromData(int width, int height, bool useMipmaps, Format format, byte[] data)
    {
        try { return new Image(FwImage.FromData(width, height, Store(format), data), format, useMipmaps); }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or OverflowException) { return new Image(); }
    }

    /// <summary>Reads a picture file by the engine's decoder (PNG, JPEG, TGA, BMP, ...), res:// and user:// paths included. An empty image when it cannot be read.</summary>
    public static Image LoadFromFile(string path)
    {
        string file = FabricPaths.IsVirtual(path) ? ResourceLoader.FindFile(path) ?? FabricPaths.Globalize(path) : path;
        FwImage? loaded = FwImage.Load(file);
        return loaded == null ? new Image() : new Image(loaded, Format.Rgba8, false);
    }

    /// <summary>The error code of <see cref="Load"/> (Godot's instance method).</summary>
    public Error Load(string path)
    {
        Image made = LoadFromFile(path);
        if (made.IsEmpty()) return Error.FileCantOpen;
        _img = made._img;
        _format = made._format;
        _mipmaps = false;
        return Error.Ok;
    }

    public bool IsEmpty() => _img == null;
    public int GetWidth() => _img?.Width ?? 0;
    public int GetHeight() => _img?.Height ?? 0;
    public Vector2I GetSize() => new(GetWidth(), GetHeight());
    public Rect2I GetUsedRect() => IsEmpty() ? default : new Rect2I(0, 0, GetWidth(), GetHeight());
    public Format GetFormat() => _format;
    public bool HasMipmaps() => _mipmaps;
    public int GetMipmapCount() => _mipmaps && _img != null ? 1 + (int)Math.Floor(Math.Log2(Math.Max(_img.Width, _img.Height))) : 0;

    /// <summary>Marks the image as wanting a mip chain; the texture made from it builds the chain.</summary>
    public void GenerateMipmaps(bool renormalize = false) { if (_img != null) _mipmaps = true; }
    public void ClearMipmaps() => _mipmaps = false;

    /// <summary>The pixel bytes of the stored format, rows top first.</summary>
    public byte[] GetData() => _img?.GetData() ?? Array.Empty<byte>();

    /// <summary>Replaces the whole image (Godot's set_data).</summary>
    public void SetData(int width, int height, bool useMipmaps, Format format, byte[] data)
    {
        Image made = CreateFromData(width, height, useMipmaps, format, data);
        _img = made._img;
        _format = made._format;
        _mipmaps = made._mipmaps;
    }

    private static Aver.Framework.ImageColor ToFw(Color c) => new(c.R, c.G, c.B, c.A);
    private static Color FromFw(Aver.Framework.ImageColor c) => new(c.R, c.G, c.B, c.A);

    public Color GetPixel(int x, int y) => _img != null && _img.Contains(x, y) ? FromFw(_img.GetPixel(x, y)) : default;
    public Color GetPixelv(Vector2I point) => GetPixel(point.X, point.Y);

    /// <summary>Writes a pixel; a point outside the image is ignored (Godot reports an error and does nothing).</summary>
    public void SetPixel(int x, int y, Color color) { if (_img != null && _img.Contains(x, y)) _img.SetPixel(x, y, ToFw(color)); }
    public void SetPixelv(Vector2I point, Color color) => SetPixel(point.X, point.Y, color);

    public void Fill(Color color) => _img?.Fill(ToFw(color));
    public void FillRect(Rect2I rect, Color color) => _img?.FillRect(rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y, ToFw(color));

    /// <summary>Copies <paramref name="srcRect"/> of <paramref name="src"/> to <paramref name="dst"/>; whatever falls outside either image is skipped.</summary>
    public void BlitRect(Image src, Rect2I srcRect, Vector2I dst)
    {
        if (_img == null || src._img == null) return;
        _img.Blit(src._img, srcRect.Position.X, srcRect.Position.Y, srcRect.Size.X, srcRect.Size.Y, dst.X, dst.Y);
    }

    public void CopyFrom(Image src)
    {
        _img = src._img?.Clone();
        _format = src._format;
        _mipmaps = src._mipmaps;
    }

    /// <summary>Godot's Image.get_region: the clipped rectangle as a new image (empty when nothing is left).</summary>
    public Image GetRegion(Rect2I region)
    {
        FwImage? part = _img?.GetRegion(region.Position.X, region.Position.Y, region.Size.X, region.Size.Y);
        return part == null ? new Image() : new Image(part, _format, false);
    }

    public void Resize(int width, int height, Interpolation interpolation = Interpolation.Bilinear)
    {
        if (_img == null || width < 1 || height < 1 || width > FwImage.MaxSide || height > FwImage.MaxSide) return;
        _img.Resize(width, height, interpolation == Interpolation.Nearest ? Aver.Framework.ImageInterpolation.Nearest : Aver.Framework.ImageInterpolation.Bilinear);
    }

    public void ResizeToPo2(bool square = false, Interpolation interpolation = Interpolation.Bilinear)
    {
        if (_img == null) return;
        static int Up(int v) { int p = 1; while (p < v) p <<= 1; return p; }
        int w = Up(_img.Width), h = Up(_img.Height);
        if (square) w = h = Math.Max(w, h);
        Resize(w, h, interpolation);
    }

    /// <summary>Changes the pixel format, keeping the picture.</summary>
    public void Convert(Format format)
    {
        if (_img == null) return;
        _img.Convert(Store(format));
        _format = format;
    }

    public void FlipX() => _img?.FlipX();
    public void FlipY() => _img?.FlipY();

    public override Resource Duplicate(bool subresources = false)
    {
        var copy = (Image)base.Duplicate(subresources);
        copy._img = _img?.Clone();
        return copy;
    }

    internal FwImage? Engine => _img;

    /// <summary>The image as a PNG file (8-bit RGBA, or grey for L8/R8). Float and sub-byte formats are saved as the 8-bit picture of their values.</summary>
    public byte[] SavePngToBuffer()
    {
        if (_img == null) return Array.Empty<byte>();
        int w = _img.Width, h = _img.Height;
        var raw = new byte[h * (1 + w * 4)];
        for (int y = 0; y < h; y++)
        {
            int row = y * (1 + w * 4);
            raw[row] = 0;
            for (int x = 0; x < w; x++)
            {
                var c = _img.GetPixel(x, y);
                int o = row + 1 + x * 4;
                raw[o] = ToByte(c.R); raw[o + 1] = ToByte(c.G); raw[o + 2] = ToByte(c.B); raw[o + 3] = ToByte(c.A);
            }
        }
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        WriteBe(ihdr, 0, w); WriteBe(ihdr, 4, h);
        ihdr[8] = 8; ihdr[9] = 6;   // 8-bit RGBA
        Chunk(ms, "IHDR", ihdr);
        using (var z = new MemoryStream())
        {
            using (var zs = new ZLibStream(z, CompressionLevel.Optimal, true)) zs.Write(raw, 0, raw.Length);
            Chunk(ms, "IDAT", z.ToArray());
        }
        Chunk(ms, "IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    public Error SavePng(string path)
    {
        byte[] png = SavePngToBuffer();
        if (png.Length == 0) return Error.Failed;
        try
        {
            string file = FabricPaths.Globalize(path);
            string? dir = System.IO.Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(file, png);
            return Error.Ok;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Error.FileCantWrite; }
    }

    private static byte ToByte(float f) => !(f > 0f) ? (byte)0 : f >= 1f ? (byte)255 : (byte)(f * 255f + 0.5f);
    private static void WriteBe(byte[] b, int at, int v) { b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v; }

    private static uint[]? s_crc;
    private static uint Crc(byte[] type, byte[] data)
    {
        if (s_crc == null)
        {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; t[n] = c; }
            s_crc = t;
        }
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in type) crc = s_crc[(crc ^ b) & 0xFF] ^ (crc >> 8);
        foreach (byte b in data) crc = s_crc[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    private static void Chunk(Stream s, string name, byte[] data)
    {
        var len = new byte[4];
        WriteBe(len, 0, data.Length);
        s.Write(len);
        byte[] type = System.Text.Encoding.ASCII.GetBytes(name);
        s.Write(type);
        s.Write(data);
        var crc = new byte[4];
        WriteBe(crc, 0, unchecked((int)Crc(type, data)));
        s.Write(crc);
    }
}

/// <summary>Where an <see cref="ImageTexture"/>'s pixels live. The engine's runtime texture; a test installs a recording one.</summary>
internal interface IRuntimeTextureHost
{
    bool Create(string key, Image image, bool srgb, bool mipmaps);                 // makes (or replaces) the texture named key
    bool Update(string key, Image image, bool srgb);
    bool Destroy(string key);
    ulong GpuHandle(string key);
}

internal sealed class EngineTextureHost : IRuntimeTextureHost
{
    public bool Create(string key, Image image, bool srgb, bool mipmaps) => image.Engine != null && FwTexture.Create(key, image.Engine, srgb, mipmaps) != null;
    public bool Update(string key, Image image, bool srgb) => image.Engine != null && FwTexture.Find(key) is { } t && t.Update(image.Engine, srgb);
    public bool Destroy(string key) => FwTexture.Find(key)?.Destroy() ?? false;
    public ulong GpuHandle(string key) => FwTexture.Find(key)?.GpuHandle ?? 0;
}

/// <summary>A texture made from an <see cref="Image"/> (Godot's ImageTexture). The pixels reach the GPU on the next frame after they are set; the texture is read by a material
/// (it is named in the material's texture slot) and by the UI, never by a script that wants its pixels back (<see cref="GetImage"/> returns the copy kept here).</summary>
public class ImageTexture : Texture2D
{
    internal static IRuntimeTextureHost Host = new EngineTextureHost();
    private static int s_next;

    private Image? _image;
    private string _key = "";
    private Vector2I _sizeOverride;

    /// <summary>The runtime texture key this texture is known by in the engine ("ft<i>n</i>").</summary>
    internal string RuntimeKey => _key;

    /// <summary>The texture-slot path a material names it by ("@rt:rtex.&lt;key&gt;").</summary>
    internal string MaterialPath => _key.Length == 0 ? "" : "@rt:rtex." + _key;

    public static ImageTexture CreateFromImage(Image image)
    {
        var t = new ImageTexture();
        t.SetImage(image);
        return t;
    }

    /// <summary>Makes the texture of the image, or replaces the pixels (and the size) when it has one already. An empty image leaves the texture as it was.</summary>
    public void SetImage(Image image)
    {
        if (image == null || image.IsEmpty()) return;
        _image = (Image)image.Duplicate();
        if (_key.Length == 0)
        {
            _key = "ft" + System.Threading.Interlocked.Increment(ref s_next);
            // The colour bytes are sRGB-encoded like any picture file: a material samples them as colour. A texture of data (a mask, a normal map) is set up by naming
            // its slot, not by the texture.
            Host.Create(_key, _image, true, _image.HasMipmaps());
        }
        else Host.Update(_key, _image, true);
        EmitChanged();
    }

    /// <summary>Replaces the pixels with those of <paramref name="image"/>, which must be the size and format this texture was made with (Godot's update).</summary>
    public void Update(Image image)
    {
        if (image == null || image.IsEmpty() || _key.Length == 0) return;
        _image = (Image)image.Duplicate();
        Host.Update(_key, _image, true);
        EmitChanged();
    }

    public override Image GetImage() => _image == null ? new Image() : (Image)_image.Duplicate();

    public void SetSizeOverride(Vector2I size) => _sizeOverride = size;

    public override Vector2 GetSize() => _sizeOverride.X > 0 && _sizeOverride.Y > 0 ? new Vector2(_sizeOverride.X, _sizeOverride.Y) : _image == null ? Vector2.Zero : new Vector2(_image.GetWidth(), _image.GetHeight());

    public Image.Format GetFormat() => _image?.GetFormat() ?? Image.Format.Rgba8;

    internal override ulong EngineHandle => _key.Length == 0 ? 0 : Host.GpuHandle(_key);

    /// <summary>Frees the engine texture. Materials that name it fall back to their default.</summary>
    public void Release()
    {
        if (_key.Length > 0) Host.Destroy(_key);
        _key = "";
    }

    private void EmitChanged() => EmitSignal(Resource.SignalName.Changed);
}
