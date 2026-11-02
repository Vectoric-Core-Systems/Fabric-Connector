namespace Aver.Fabric;

// BaseMaterial3D's flag and feature switches by number (get_flag/set_flag, get_feature/set_feature): each reaches the property of the same meaning.
// A flag the engine material has no property for is kept, so a script reads back what it set.
public partial class BaseMaterial3D
{
    public enum Flags
    {
        DisableDepthTest = 0, AlbedoFromVertexColor = 1, SrgbVertexColor = 2, UsePointSize = 3, FixedSize = 4, BillboardKeepScale = 5,
        Uv1UseTriplanar = 6, Uv2UseTriplanar = 7, Uv1UseWorldTriplanar = 8, Uv2UseWorldTriplanar = 9, AoOnUv2 = 10, EmissionOnUv2 = 11,
        AlbedoTextureForceSrgb = 12, DontReceiveShadows = 13, DisableAmbientLight = 14, UseShadowToOpacity = 15, UseTextureRepeat = 16,
        InvertHeightmap = 17, SubsurfaceModeSkin = 18, ParticleTrailsMode = 19, AlbedoTextureMsdf = 20, DisableFog = 21,
        DisableSpecularOcclusion = 22, UseZClipScale = 23, UseFovOverride = 24, Max = 25,
    }

    public enum Feature
    {
        Emission = 0, NormalMapping = 1, Rim = 2, Clearcoat = 3, Anisotropy = 4, AmbientOcclusion = 5, Heightmapping = 6,
        SubsurfaceScattering = 7, SubsurfaceTransmittance = 8, Backlight = 9, Refraction = 10, Detail = 11, BentNormalMapping = 12, Max = 13,
    }

    private readonly bool[] _keptFlags = new bool[(int)Flags.Max];
    private readonly bool[] _keptFeatures = new bool[(int)Feature.Max];

    public bool GetFlag(Flags flag) => flag switch
    {
        Flags.DisableDepthTest => NoDepthTest,
        Flags.AlbedoFromVertexColor => VertexColorUseAsAlbedo,
        Flags.SrgbVertexColor => VertexColorIsSrgb,
        Flags.FixedSize => FixedSize,
        Flags.BillboardKeepScale => BillboardKeepScale,
        Flags.Uv1UseTriplanar => Uv1Triplanar,
        Flags.Uv2UseTriplanar => Uv2Triplanar,
        Flags.Uv1UseWorldTriplanar => Uv1WorldTriplanar,
        Flags.Uv2UseWorldTriplanar => Uv2WorldTriplanar,
        Flags.AoOnUv2 => AoOnUv2,
        Flags.EmissionOnUv2 => EmissionOnUv2,
        Flags.DontReceiveShadows => DisableReceiveShadows,
        Flags.DisableAmbientLight => DisableAmbientLight,
        Flags.UseShadowToOpacity => ShadowToOpacity,
        Flags.UseTextureRepeat => TextureRepeat,
        Flags.DisableFog => DisableFog,
        _ => flag is >= 0 and < Flags.Max && _keptFlags[(int)flag],
    };

    public void SetFlag(Flags flag, bool enable)
    {
        switch (flag)
        {
            case Flags.DisableDepthTest: NoDepthTest = enable; break;
            case Flags.AlbedoFromVertexColor: VertexColorUseAsAlbedo = enable; break;
            case Flags.SrgbVertexColor: VertexColorIsSrgb = enable; break;
            case Flags.FixedSize: FixedSize = enable; break;
            case Flags.BillboardKeepScale: BillboardKeepScale = enable; break;
            case Flags.Uv1UseTriplanar: Uv1Triplanar = enable; break;
            case Flags.Uv2UseTriplanar: Uv2Triplanar = enable; break;
            case Flags.Uv1UseWorldTriplanar: Uv1WorldTriplanar = enable; break;
            case Flags.Uv2UseWorldTriplanar: Uv2WorldTriplanar = enable; break;
            case Flags.AoOnUv2: AoOnUv2 = enable; break;
            case Flags.EmissionOnUv2: EmissionOnUv2 = enable; break;
            case Flags.DontReceiveShadows: DisableReceiveShadows = enable; break;
            case Flags.DisableAmbientLight: DisableAmbientLight = enable; break;
            case Flags.UseShadowToOpacity: ShadowToOpacity = enable; break;
            case Flags.UseTextureRepeat: TextureRepeat = enable; break;
            case Flags.DisableFog: DisableFog = enable; break;
            default: if (flag is >= 0 and < Flags.Max) _keptFlags[(int)flag] = enable; break;
        }
    }

    public bool GetFeature(Feature feature) => feature switch
    {
        Feature.Emission => EmissionEnabled,
        Feature.NormalMapping => NormalEnabled,
        Feature.Rim => RimEnabled,
        Feature.Clearcoat => ClearcoatEnabled,
        Feature.Anisotropy => AnisotropyEnabled,
        Feature.AmbientOcclusion => AoEnabled,
        Feature.Heightmapping => HeightmapEnabled,
        Feature.SubsurfaceScattering => SubsurfScatterEnabled,
        Feature.Backlight => BacklightEnabled,
        Feature.Refraction => RefractionEnabled,
        Feature.Detail => DetailEnabled,
        _ => feature is >= 0 and < Feature.Max && _keptFeatures[(int)feature],
    };

    public void SetFeature(Feature feature, bool enable)
    {
        switch (feature)
        {
            case Feature.Emission: EmissionEnabled = enable; break;
            case Feature.NormalMapping: NormalEnabled = enable; break;
            case Feature.Rim: RimEnabled = enable; break;
            case Feature.Clearcoat: ClearcoatEnabled = enable; break;
            case Feature.Anisotropy: AnisotropyEnabled = enable; break;
            case Feature.AmbientOcclusion: AoEnabled = enable; break;
            case Feature.Heightmapping: HeightmapEnabled = enable; break;
            case Feature.SubsurfaceScattering: SubsurfScatterEnabled = enable; break;
            case Feature.Backlight: BacklightEnabled = enable; break;
            case Feature.Refraction: RefractionEnabled = enable; break;
            case Feature.Detail: DetailEnabled = enable; break;
            default: if (feature is >= 0 and < Feature.Max) _keptFeatures[(int)feature] = enable; break;
        }
    }
}
