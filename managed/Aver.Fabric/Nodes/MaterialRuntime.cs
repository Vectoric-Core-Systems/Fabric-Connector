// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// P14: a script that changes a BaseMaterial3D (albedo_color, a texture, emission, uv1_offset, the filter ...) reaches the engine's material of the same name. The converter writes
// a Godot material as Materials/<Name>.ocmat; the engine keeps one live material per name, so the change is seen by every mesh that draws it, as it is in Godot where the
// resource is shared. A material the script made with `new StandardMaterial3D()` has no file and no engine material: its values are kept and not drawn.

namespace Aver.Fabric;

/// <summary>The calls a BaseMaterial3D makes on the engine's material library (Aver.Render.PBR, through Aver.Scripting.Pbr). A test installs a recording one.</summary>
internal interface IMaterialBackend
{
    int Find(string name);   // the library handle of the material named so, 0 when it is not loaded
    bool SetBaseColor(int m, float r, float g, float b, float a);
    bool SetEmissive(int m, float r, float g, float b, float energy);
    bool SetMetallic(int m, float v);
    bool SetRoughness(int m, float v);
    bool SetNormalScale(int m, float v);
    bool SetUv(int m, int set, float scaleX, float scaleY, float offsetX, float offsetY);
    bool SetSampler(int m, int filter, int address);   // Aver.Scripting.PbrSamplerFilter / PbrSamplerAddress
    bool SetChannels(int m, int roughness, int metallic);   // Aver.Scripting.PbrChannel
    bool SetVertexColor(int m, bool on, bool srgb);
    bool SetAlphaExtension(int m, int extension);   // Aver.Scripting.PbrAlphaExtension
    bool SetTexture(int m, int slot, string? path);   // Aver.Scripting.PbrTextureSlot; null clears the slot
}

internal sealed class NativeMaterialBackend : IMaterialBackend
{
    private static int s_state;   // 0 unknown, 1 loaded, 2 missing

    private static bool Available
    {
        get
        {
            if (s_state == 0)
            {
                try { _ = Aver.Scripting.Pbr.Count; s_state = 1; }
                catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { s_state = 2; }
            }
            return s_state == 1;
        }
    }

    private static bool Guard(Action<Aver.Scripting.Pbr.PbrMaterial> act, int m)
    {
        if (m <= 0 || !Available) return false;
        try { act(Aver.Scripting.Pbr.FromHandle(m)); return true; }
        catch (EntryPointNotFoundException) { s_state = 2; return false; }   // an engine from before aver_pbr_find / the sampler calls
    }

    public int Find(string name)
    {
        if (!Available) return 0;
        try { return Aver.Scripting.Pbr.Find(name).Handle; }
        catch (EntryPointNotFoundException) { s_state = 2; return 0; }
    }

    public bool SetBaseColor(int m, float r, float g, float b, float a) => Guard(p => p.BaseColorFactor = (r, g, b, a), m);
    public bool SetEmissive(int m, float r, float g, float b, float energy) => Guard(p => { p.EmissiveFactor = (r, g, b); p.EmissiveEnergy = energy; }, m);
    public bool SetMetallic(int m, float v) => Guard(p => p.MetallicFactor = v, m);
    public bool SetRoughness(int m, float v) => Guard(p => p.RoughnessFactor = v, m);
    public bool SetNormalScale(int m, float v) => Guard(p => p.NormalScale = v, m);
    public bool SetUv(int m, int set, float sx, float sy, float ox, float oy) =>
        Guard(p => { if (set == 2) p.Uv2Transform = (sx, sy, ox, oy); else p.Uv1Transform = (sx, sy, ox, oy); }, m);
    public bool SetSampler(int m, int filter, int address) =>
        Guard(p => { p.SamplerFilter = (Aver.Scripting.PbrSamplerFilter)filter; p.SamplerAddress = (Aver.Scripting.PbrSamplerAddress)address; }, m);
    public bool SetChannels(int m, int roughness, int metallic) =>
        Guard(p => { p.RoughnessChannel = (Aver.Scripting.PbrChannel)roughness; p.MetallicChannel = (Aver.Scripting.PbrChannel)metallic; }, m);
    public bool SetVertexColor(int m, bool on, bool srgb) => Guard(p => { p.VertexColor = on; p.VertexColorSrgb = srgb; }, m);
    public bool SetAlphaExtension(int m, int extension) => Guard(p => p.AlphaExtension = (Aver.Scripting.PbrAlphaExtension)extension, m);
    public bool SetTexture(int m, int slot, string? path) =>
        Guard(p => { if (path == null) p.ClearTexture((Aver.Scripting.PbrTextureSlot)slot); else p.SetTexturePath((Aver.Scripting.PbrTextureSlot)slot, path); }, m);
}

public partial class BaseMaterial3D
{
    public enum TextureParam
    {
        Albedo = 0, Metallic = 1, Roughness = 2, Emission = 3, Normal = 4, Rim = 5, Clearcoat = 6, Flowmap = 7, AmbientOcclusion = 8, Heightmap = 9,
        SubsurfaceScattering = 10, SubsurfaceTransmittance = 11, Backlight = 12, Refraction = 13, DetailMask = 14, DetailAlbedo = 15, DetailNormal = 16, Orm = 17, Max = 18,
    }

    public enum TextureFilterEnum { Nearest = 0, Linear = 1, NearestWithMipmaps = 2, LinearWithMipmaps = 3, NearestWithMipmapsAnisotropic = 4, LinearWithMipmapsAnisotropic = 5, Max = 6 }
    public enum TextureChannel { Red = 0, Green = 1, Blue = 2, Alpha = 3, Grayscale = 4, Max = 5 }

    /// <summary>What a script changed since the engine last took it (and what a later push still owes).</summary>
    [Flags]
    private enum Changed_
    {
        None = 0, Albedo = 1, Metallic = 2, Roughness = 4, Emission = 8, Normal = 16, Uv1 = 32, Uv2 = 64, Sampler = 128, Channels = 256, VertexColor = 512, Alpha = 1024,
        AlbedoTexture = 2048, EmissionTexture = 4096, NormalTexture = 8192, RoughnessTexture = 16384, MetallicTexture = 32768, AoTexture = 65536,
    }

    internal static IMaterialBackend Backend = new NativeMaterialBackend();

    private bool _loading;
    private Changed_ _owed;
    private int _engineHandle;

    private bool _emissionEnabled;
    private Color _emission = new(0, 0, 0, 1);
    private float _emissionEnergy = 1f, _normalScale = 1f;
    private bool _normalEnabled, _aoEnabled, _textureRepeat = true;
    private TextureFilterEnum _textureFilter = TextureFilterEnum.LinearWithMipmaps;
    private TextureChannel _roughnessChannel = TextureChannel.Red, _metallicChannel = TextureChannel.Red, _aoChannel = TextureChannel.Red;
    private Texture2D? _normalTexture, _roughnessTexture, _metallicTexture, _aoTexture;
    private AlphaAntiAliasingEnum _alphaAa;

    public bool EmissionEnabled { get => _emissionEnabled; set { _emissionEnabled = value; Touch(Changed_.Emission); } }
    /// <summary>The glow colour (sRGB, as Godot stores it).</summary>
    public Color Emission { get => _emission; set { _emission = value; Touch(Changed_.Emission); } }
    public float EmissionEnergyMultiplier { get => _emissionEnergy; set { _emissionEnergy = value; Touch(Changed_.Emission); } }
    public bool NormalEnabled { get => _normalEnabled; set { _normalEnabled = value; Touch(Changed_.Normal | Changed_.NormalTexture); } }
    public float NormalScale { get => _normalScale; set { _normalScale = value; Touch(Changed_.Normal); } }
    public Texture2D? NormalTexture { get => _normalTexture; set { _normalTexture = value; Touch(Changed_.NormalTexture); } }
    public Texture2D? RoughnessTexture { get => _roughnessTexture; set { _roughnessTexture = value; Touch(Changed_.RoughnessTexture | Changed_.Channels); } }
    public Texture2D? MetallicTexture { get => _metallicTexture; set { _metallicTexture = value; Touch(Changed_.MetallicTexture | Changed_.Channels); } }
    public TextureChannel RoughnessTextureChannel { get => _roughnessChannel; set { _roughnessChannel = value; Touch(Changed_.Channels); } }
    public TextureChannel MetallicTextureChannel { get => _metallicChannel; set { _metallicChannel = value; Touch(Changed_.Channels); } }
    public bool AoEnabled { get => _aoEnabled; set { _aoEnabled = value; Touch(Changed_.AoTexture); } }
    public Texture2D? AoTexture { get => _aoTexture; set { _aoTexture = value; Touch(Changed_.AoTexture); } }
    public TextureChannel AoTextureChannel { get => _aoChannel; set { _aoChannel = value; } }

    /// <summary>How the material's textures are filtered (Godot's texture_filter). The engine's sampler is the raster draw's; reflections and global illumination keep the engine sampler.</summary>
    public TextureFilterEnum TextureFilter { get => _textureFilter; set { _textureFilter = value; Touch(Changed_.Sampler); } }
    /// <summary>False clamps a texture lookup outside 0..1 to the edge (Godot's texture_repeat).</summary>
    public bool TextureRepeat { get => _textureRepeat; set { _textureRepeat = value; Touch(Changed_.Sampler); } }

    public void SetTexture(TextureParam param, Texture2D? texture)
    {
        switch (param)
        {
            case TextureParam.Albedo: AlbedoTexture = texture; break;
            case TextureParam.Emission: EmissionTexture = texture; break;
            case TextureParam.Normal: NormalTexture = texture; break;
            case TextureParam.Roughness: RoughnessTexture = texture; break;
            case TextureParam.Metallic: MetallicTexture = texture; break;
            case TextureParam.AmbientOcclusion: AoTexture = texture; break;
            case TextureParam.Heightmap: HeightmapTexture = texture; break;
            case TextureParam.DetailAlbedo: DetailAlbedo = texture; break;
            case TextureParam.DetailNormal: DetailNormal = texture; break;
            case TextureParam.DetailMask: DetailMask = texture; break;
        }
    }

    public Texture2D? GetTexture(TextureParam param) => param switch
    {
        TextureParam.Albedo => AlbedoTexture,
        TextureParam.Emission => EmissionTexture,
        TextureParam.Normal => NormalTexture,
        TextureParam.Roughness => RoughnessTexture,
        TextureParam.Metallic => MetallicTexture,
        TextureParam.AmbientOcclusion => AoTexture,
        TextureParam.Heightmap => HeightmapTexture,
        TextureParam.DetailAlbedo => DetailAlbedo,
        TextureParam.DetailNormal => DetailNormal,
        TextureParam.DetailMask => DetailMask,
        _ => null,
    };

    // The .ocmat file's FX record, so a material read from the engine's files reports the sampler and the colour handling it is drawn with.
    private void ReadFx(string[] tokens)
    {
        for (int i = 1; i < tokens.Length; i++)
        {
            int eq = tokens[i].IndexOf('=');
            if (eq < 0) continue;
            string k = tokens[i][..eq], v = tokens[i][(eq + 1)..];
            switch (k)
            {
                case "texfilter":
                    _textureFilter = v switch { "point" => TextureFilterEnum.Nearest, "bilinear" => TextureFilterEnum.Linear, "pointmip" => TextureFilterEnum.NearestWithMipmaps,
                                                "trilinear" => TextureFilterEnum.LinearWithMipmaps, _ => TextureFilterEnum.LinearWithMipmapsAnisotropic };
                    break;
                case "texaddr": _textureRepeat = v == "wrap"; break;
                case "roughchan": _roughnessChannel = ChannelOf(v); break;
                case "metalchan": _metallicChannel = ChannelOf(v); break;
                case "vcsrgb": _vertexColorSrgb = v != "0"; break;
            }
        }
    }

    private static TextureChannel ChannelOf(string v) => v switch { "r" => TextureChannel.Red, "g" => TextureChannel.Green, "b" => TextureChannel.Blue, "a" => TextureChannel.Alpha, _ => TextureChannel.Grayscale };

    private void ReadEmissiveFactor(float r, float g, float b)
    {
        if (r <= 0 && g <= 0 && b <= 0) return;
        _emissionEnabled = true;
        _emission = new Color(Srgb(r), Srgb(g), Srgb(b), 1f);
    }

    private static float Srgb(float linear) { linear = Math.Clamp(linear, 0f, 1f); return linear <= 0.0031308f ? linear * 12.92f : 1.055f * MathF.Pow(linear, 1f / 2.4f) - 0.055f; }
    private static float Linear(float srgb) { srgb = Math.Clamp(srgb, 0f, 1f); return srgb <= 0.04045f ? srgb / 12.92f : MathF.Pow((srgb + 0.055f) / 1.055f, 2.4f); }

    // ---- the push ----

    /// <summary>The name of the engine's material this one is: the stem of its .ocmat file, or null for a material that has no file.</summary>
    internal string? EngineName =>
        ResourcePath.EndsWith(".ocmat", StringComparison.OrdinalIgnoreCase) ? System.IO.Path.GetFileNameWithoutExtension(ResourcePath) : null;

    private void Touch(Changed_ what)
    {
        if (_loading) return;
        _owed |= what;
        SyncToEngine();
    }

    /// <summary>Gives the engine's material what the script changed since the last time it took it. A material the engine has not loaded yet keeps its changes owed; they are
    /// given when it is next asked to sync (a mesh that takes the material calls this).</summary>
    internal void SyncToEngine()
    {
        if (_owed == Changed_.None || EngineName is not { } name) return;
        IMaterialBackend be = Backend;
        if (_engineHandle <= 0) _engineHandle = be.Find(name);
        if (_engineHandle <= 0) return;
        int m = _engineHandle;
        bool ok = true;
        Changed_ owed = _owed;
        if ((owed & Changed_.Albedo) != 0) ok &= be.SetBaseColor(m, _albedoColor.R, _albedoColor.G, _albedoColor.B, _albedoColor.A);
        if ((owed & Changed_.Metallic) != 0) ok &= be.SetMetallic(m, _metallic);
        if ((owed & Changed_.Roughness) != 0) ok &= be.SetRoughness(m, _roughness);
        if ((owed & Changed_.Emission) != 0)
        {
            // The converter wrote the colour linear and the energy as its own scale; a disabled emission is black.
            bool on = _emissionEnabled;
            ok &= be.SetEmissive(m, on ? Linear(_emission.R) : 0f, on ? Linear(_emission.G) : 0f, on ? Linear(_emission.B) : 0f, _emissionEnergy);
        }
        if ((owed & Changed_.Normal) != 0) ok &= be.SetNormalScale(m, _normalEnabled ? _normalScale : 0f);
        if ((owed & Changed_.Uv1) != 0) ok &= be.SetUv(m, 1, _uv1Scale.X, _uv1Scale.Y, _uv1Offset.X, _uv1Offset.Y);
        if ((owed & Changed_.Uv2) != 0) ok &= be.SetUv(m, 2, _uv2Scale.X, _uv2Scale.Y, _uv2Offset.X, _uv2Offset.Y);
        if ((owed & Changed_.Sampler) != 0) ok &= be.SetSampler(m, SamplerFilterOf(_textureFilter), _textureRepeat ? 0 : 1);
        if ((owed & Changed_.Channels) != 0) ok &= be.SetChannels(m, (int)_roughnessChannel, (int)_metallicChannel);
        if ((owed & Changed_.VertexColor) != 0) ok &= be.SetVertexColor(m, _vertexColorAsAlbedo, _vertexColorSrgb);
        if ((owed & Changed_.Alpha) != 0) ok &= be.SetAlphaExtension(m, _alphaAa == AlphaAntiAliasingEnum.Off ? 0 : 2);
        if ((owed & Changed_.AlbedoTexture) != 0) ok &= be.SetTexture(m, 0, SlotPath(_albedoTexture));
        if ((owed & Changed_.EmissionTexture) != 0) ok &= be.SetTexture(m, 4, SlotPath(_emissionTexture));
        if ((owed & Changed_.NormalTexture) != 0) ok &= be.SetTexture(m, 2, _normalEnabled ? SlotPath(_normalTexture) : null);
        if ((owed & Changed_.AoTexture) != 0) ok &= be.SetTexture(m, 3, _aoEnabled ? SlotPath(_aoTexture) : null);
        // One metal-rough slot holds both scalars' map: the roughness map when there is one, else the metallic map (a pair needs repacking, which the converter does).
        if ((owed & (Changed_.RoughnessTexture | Changed_.MetallicTexture)) != 0) ok &= be.SetTexture(m, 1, SlotPath(_roughnessTexture ?? _metallicTexture));
        if (ok) _owed = Changed_.None;
        else _engineHandle = 0;   // the engine dropped the material (it was idle, or the project reloaded): find it again next time
    }

    internal static int SamplerFilterOf(TextureFilterEnum f) => f switch
    {
        TextureFilterEnum.Nearest => 4,                       // point
        TextureFilterEnum.Linear => 2,                        // bilinear
        TextureFilterEnum.NearestWithMipmaps => 3,            // point with nearest mips
        TextureFilterEnum.NearestWithMipmapsAnisotropic => 3,
        TextureFilterEnum.LinearWithMipmaps => 1,             // trilinear
        _ => 0,                                               // the engine's anisotropic sampler
    };

    /// <summary>The texture-slot path the engine's material names a texture by: a runtime texture or a view by its reference, a file by its content path, null for none.</summary>
    internal static string? SlotPath(Texture2D? t)
    {
        switch (t)
        {
            case null: return null;
            case ImageTexture img: return img.MaterialPath.Length > 0 ? img.MaterialPath : null;
            case GeneratedTexture gen: return gen.MaterialPath;
            case ViewportTexture view: return view.MaterialReference.Length > 0 ? view.MaterialReference : null;
        }
        if (t.ResourcePath.Length == 0) return null;
        string? file = ResourceLoader.FindFile(t.ResourcePath);
        string rel = t.ResourcePath.StartsWith("res://", StringComparison.Ordinal) ? t.ResourcePath["res://".Length..] : t.ResourcePath;
        if (file != null && ResourceLoader.ContentRoot.Length > 0)
        {
            string under = System.IO.Path.GetRelativePath(ResourceLoader.ContentRoot, file).Replace('\\', '/');
            if (!under.StartsWith("..", StringComparison.Ordinal)) return under;
        }
        return rel;
    }
}
