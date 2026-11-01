// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// The resource types the 3D node stubs hold: meshes, materials and collision shapes. Values only; F2b gives them loaders.

namespace Aver.Fabric;

/// <summary>A mesh resource (Godot's Mesh). <see cref="Resource.ResourcePath"/> names the Aver mesh asset ("res://x.ocmesh" or a content path).</summary>
public class Mesh : Resource
{
    public enum PrimitiveType { Points = 0, Lines = 1, LineStrip = 2, Triangles = 3, TriangleStrip = 4 }

    /// <summary>The slots of the arrays an ArrayMesh surface is made from (RenderingServer.ArrayType).</summary>
    public enum ArrayType { Vertex = 0, Normal = 1, Tangent = 2, Color = 3, TexUV = 4, TexUV2 = 5, Custom0 = 6, Custom1 = 7, Custom2 = 8, Custom3 = 9, Bones = 10, Weights = 11, Index = 12, Max = 13 }

    /// <summary>Surface format flags (RenderingServer.ArrayFormat). Accepted and not needed: the arrays say what a surface has.</summary>
    [Flags]
    public enum ArrayFormat : long
    {
        FormatVertex = 1, FormatNormal = 2, FormatTangent = 4, FormatColor = 8, FormatTexUV = 16, FormatTexUV2 = 32, FormatBones = 1024, FormatWeights = 2048, FormatIndex = 4096,
        FlagUse2DVertices = 1L << 25, FlagUseDynamicUpdate = 1L << 26, FlagUse8BoneWeights = 1L << 27, FlagCompressAttributes = 1L << 29,
    }

    public virtual int GetSurfaceCount() => 0;
    public virtual Material? SurfaceGetMaterial(int surfIdx) => null;
    public virtual void SurfaceSetMaterial(int surfIdx, Material? material) { }
    public virtual Aabb GetAabb() => default;

    /// <summary>The width, in metres, a line or a point is drawn at: lines and points are drawn as crossed quads this wide (see MeshSurface).</summary>
    public static float ThinPrimitiveWidth { get; set; } = 0.02f;

    /// <summary>Every triangle as three points, in order (Godot's get_faces). Empty for a mesh whose triangles are not held in memory.</summary>
    public virtual Vector3[] GetFaces() => System.Array.Empty<Vector3>();

    /// <summary>A shape that collides like the mesh: its triangles, hollow (Godot's create_trimesh_shape). Null when the mesh has none readable.</summary>
    public ConcavePolygonShape3D? CreateTrimeshShape()
    {
        Vector3[] faces = GetFaces();
        return faces.Length == 0 ? null : new ConcavePolygonShape3D { Data = faces };
    }

    /// <summary>A solid convex shape around the mesh's points (Godot's create_convex_shape). Null when the mesh has none readable.</summary>
    public ConvexPolygonShape3D? CreateConvexShape()
    {
        Vector3[] faces = GetFaces();
        if (faces.Length == 0) return null;
        return new ConvexPolygonShape3D { Points = faces.Distinct().ToArray() };
    }

    /// <summary>The asset path an entity draws this mesh by when the engine holds it as a runtime mesh ("@rtmesh/..."), else null (a file mesh is named by
    /// <see cref="Resource.ResourcePath"/>).</summary>
    internal virtual string? EngineAssetPath => null;
}

public class Material : Resource
{
    public int RenderPriority { get; set; }
}

public partial class BaseMaterial3D : Material
{
    // The values a script changes at run time reach the engine's material of the same name (MaterialRuntime.cs, P14): the setters below mark what changed.
    private Color _albedoColor = new(1, 1, 1, 1);
    private float _metallic, _roughness = 1f;
    private bool _vertexColorAsAlbedo, _vertexColorSrgb;
    private Texture2D? _albedoTexture, _emissionTexture;
    private Vector3 _uv1Scale = Vector3.One, _uv1Offset = Vector3.Zero, _uv2Scale = Vector3.One, _uv2Offset = Vector3.Zero;

    public Color AlbedoColor { get => _albedoColor; set { _albedoColor = value; Touch(Changed_.Albedo); } }
    public float Metallic { get => _metallic; set { _metallic = value; Touch(Changed_.Metallic); } }
    public float Roughness { get => _roughness; set { _roughness = value; Touch(Changed_.Roughness); } }
    /// <summary>Multiplies the albedo and opacity by the mesh's vertex colour. The converter bakes it into the .ocmat <c>vertexcolor</c> flag
    /// (docs/plans/fabric/G4_RENDER.md R1); the engine applies it in the raster scene pass for meshes that carry a colour stream.</summary>
    public bool VertexColorUseAsAlbedo { get => _vertexColorAsAlbedo; set { _vertexColorAsAlbedo = value; Touch(Changed_.VertexColor); } }
    /// <summary>The vertex colour is sRGB-encoded and is decoded to linear before it multiplies the albedo (FX vcsrgb; raster draws).</summary>
    public bool VertexColorIsSrgb { get => _vertexColorSrgb; set { _vertexColorSrgb = value; Touch(Changed_.VertexColor); } }
    /// <summary>The albedo map. The converter bakes a <see cref="ViewportTexture"/> into the .ocmat as <c>@rt:viewport.&lt;key&gt;</c>; a texture assigned here at run time (a file
    /// texture, an <see cref="ImageTexture"/>, a view's texture) is named in the engine material's albedo slot.</summary>
    public Texture2D? AlbedoTexture { get => _albedoTexture; set { _albedoTexture = value; Touch(Changed_.AlbedoTexture); } }
    public Texture2D? EmissionTexture { get => _emissionTexture; set { _emissionTexture = value; Touch(Changed_.EmissionTexture); } }

    // ---- G4 R6: BaseMaterial3D features, under Godot's property names. The converter bakes them into the .ocmat FX record (see FxRecord);
    // the engine draws the baked material, so a value changed at run time is stored and not pushed (like AlbedoTexture). Distances are metres, as in Godot.
    public enum TransparencyEnum { Disabled = 0, Alpha = 1, AlphaScissor = 2, AlphaHash = 3, AlphaDepthPrePass = 4 }
    public enum ShadingModeEnum { Unshaded = 0, PerPixel = 1, PerVertex = 2 }
    public enum BlendModeEnum { Mix = 0, Add = 1, Sub = 2, Mul = 3, PremultAlpha = 4 }
    public enum CullModeEnum { Back = 0, Front = 1, Disabled = 2 }
    public enum DepthDrawModeEnum { OpaqueOnly = 0, Always = 1, Disabled = 2 }
    public enum BillboardModeEnum { Disabled = 0, Enabled = 1, FixedY = 2, Particles = 3 }
    public enum DiffuseModeEnum { Burley = 0, Lambert = 1, LambertWrap = 2, Toon = 3 }
    public enum SpecularModeEnum { SchlickGgx = 0, Toon = 1, Disabled = 2 }
    public enum DistanceFadeModeEnum { Disabled = 0, PixelAlpha = 1, PixelDither = 2, ObjectDither = 3 }
    public enum DetailUVEnum { DetailUv1 = 0, DetailUv2 = 1 }
    public enum EmissionOperatorEnum { Add = 0, Multiply = 1 }
    public enum AlphaAntiAliasingEnum { Off = 0, AlphaToCoverage = 1, AlphaToCoverageAndToOne = 2 }

    // UV transforms and triplanar
    public Vector3 Uv1Scale { get => _uv1Scale; set { _uv1Scale = value; Touch(Changed_.Uv1); } }
    public Vector3 Uv1Offset { get => _uv1Offset; set { _uv1Offset = value; Touch(Changed_.Uv1); } }
    public Vector3 Uv2Scale { get => _uv2Scale; set { _uv2Scale = value; Touch(Changed_.Uv2); } }
    public Vector3 Uv2Offset { get => _uv2Offset; set { _uv2Offset = value; Touch(Changed_.Uv2); } }
    public bool Uv1Triplanar { get; set; }
    public float Uv1TriplanarSharpness { get; set; } = 1f;
    public bool Uv1WorldTriplanar { get; set; }
    public bool Uv2Triplanar { get; set; }
    public float Uv2TriplanarSharpness { get; set; } = 1f;
    public bool Uv2WorldTriplanar { get; set; }

    // Pipeline state
    public TransparencyEnum Transparency { get; set; } = TransparencyEnum.Disabled;
    public float AlphaScissorThreshold { get; set; } = 0.5f;
    public float AlphaHashScale { get; set; } = 1f;
    public AlphaAntiAliasingEnum AlphaAntialiasingMode { get => _alphaAa; set { _alphaAa = value; Touch(Changed_.Alpha); } }
    public float AlphaAntialiasingEdge { get; set; } = 0.3f;
    public BlendModeEnum BlendMode { get; set; } = BlendModeEnum.Mix;
    public CullModeEnum CullMode { get; set; } = CullModeEnum.Back;
    public DepthDrawModeEnum DepthDrawMode { get; set; } = DepthDrawModeEnum.OpaqueOnly;
    public bool NoDepthTest { get; set; }
    public ShadingModeEnum ShadingMode { get; set; } = ShadingModeEnum.PerPixel;
    public bool ShadowToOpacity { get; set; }
    public bool DisableReceiveShadows { get; set; }
    public bool DisableAmbientLight { get; set; }
    public bool DisableFog { get; set; }

    // Vertex stage and fades
    public BillboardModeEnum BillboardMode { get; set; } = BillboardModeEnum.Disabled;
    public bool BillboardKeepScale { get; set; }
    public bool FixedSize { get; set; }
    public bool Grow { get; set; }
    public float GrowAmount { get; set; }
    public int ParticlesAnimHFrames { get; set; } = 1;
    public int ParticlesAnimVFrames { get; set; } = 1;
    public bool ParticlesAnimLoop { get; set; }
    public DistanceFadeModeEnum DistanceFadeMode { get; set; } = DistanceFadeModeEnum.Disabled;
    public float DistanceFadeMinDistance { get; set; }
    public float DistanceFadeMaxDistance { get; set; } = 10f;
    public bool ProximityFadeEnabled { get; set; }
    public float ProximityFadeDistance { get; set; } = 1f;

    // Surface
    public bool RimEnabled { get; set; }
    public float Rim { get; set; } = 1f;
    public float RimTint { get; set; } = 0.5f;
    public bool ClearcoatEnabled { get; set; }
    public float Clearcoat { get; set; } = 1f;
    public float ClearcoatRoughness { get; set; } = 0.5f;
    public bool AnisotropyEnabled { get; set; }
    public float Anisotropy { get; set; }
    public bool HeightmapEnabled { get; set; }
    public float HeightmapScale { get; set; } = 5f;
    public bool HeightmapDeepParallax { get; set; }
    public int HeightmapMinLayers { get; set; } = 8;
    public int HeightmapMaxLayers { get; set; } = 32;
    public bool HeightmapFlipTangent { get; set; }
    public bool HeightmapFlipBinormal { get; set; }
    public bool HeightmapFlipTexture { get; set; }
    public Texture2D? HeightmapTexture { get; set; }
    public bool SubsurfScatterEnabled { get; set; }
    public float SubsurfScatterStrength { get; set; }
    public bool BacklightEnabled { get; set; }
    public Color Backlight { get; set; } = new(0, 0, 0, 1);
    public bool RefractionEnabled { get; set; }
    public float RefractionScale { get; set; } = 0.05f;
    public bool DetailEnabled { get; set; }
    public BlendModeEnum DetailBlendMode { get; set; } = BlendModeEnum.Mix;
    public DetailUVEnum DetailUvLayer { get; set; } = DetailUVEnum.DetailUv1;
    public Texture2D? DetailAlbedo { get; set; }
    public Texture2D? DetailNormal { get; set; }
    public Texture2D? DetailMask { get; set; }
    public EmissionOperatorEnum EmissionOperator { get; set; } = EmissionOperatorEnum.Add;
    public bool EmissionOnUv2 { get; set; }
    public bool AoOnUv2 { get; set; }
    public float AoLightAffect { get; set; }
    public float MetallicSpecular { get; set; } = 0.5f;
    public DiffuseModeEnum DiffuseMode { get; set; } = DiffuseModeEnum.Burley;
    public SpecularModeEnum SpecularMode { get; set; } = SpecularModeEnum.SchlickGgx;

    private static string F(double v) => v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The pairs of the .ocmat <c>FX</c> record this material's properties bake to: the grammar the converter writes and
    /// <c>aver::fmt::parseOcmatFx</c> reads (docs/plans/fabric/G4_RENDER.md R6). Only what differs from Godot's defaults is listed, plus the cull mode.</summary>
    public string FxRecord()
    {
        var p = new System.Collections.Generic.List<string>();
        p.Add(CullMode == CullModeEnum.Front ? "cull=front" : CullMode == CullModeEnum.Disabled ? "cull=none" : "cull=back");
        if (Uv1Scale.X != 1 || Uv1Scale.Y != 1) p.Add($"uv1scale={F(Uv1Scale.X)},{F(Uv1Scale.Y)}");
        if (Uv1Offset.X != 0 || Uv1Offset.Y != 0) p.Add($"uv1offset={F(Uv1Offset.X)},{F(Uv1Offset.Y)}");
        if (Uv2Scale.X != 1 || Uv2Scale.Y != 1) p.Add($"uv2scale={F(Uv2Scale.X)},{F(Uv2Scale.Y)}");
        if (Uv2Offset.X != 0 || Uv2Offset.Y != 0) p.Add($"uv2offset={F(Uv2Offset.X)},{F(Uv2Offset.Y)}");
        if (Uv1Triplanar) { p.Add("uv1tri=1"); if (Uv1WorldTriplanar) p.Add("uv1world=1"); if (Uv1TriplanarSharpness != 1) p.Add($"uv1sharp={F(Uv1TriplanarSharpness)}"); }
        if (Transparency == TransparencyEnum.AlphaHash) { p.Add("alphaext=hash"); if (AlphaHashScale != 1) p.Add($"hashscale={F(AlphaHashScale)}"); }
        if (Transparency == TransparencyEnum.AlphaDepthPrePass) p.Add("depthdraw=always");
        if (AlphaAntialiasingMode != AlphaAntiAliasingEnum.Off && (Transparency == TransparencyEnum.AlphaScissor || Transparency == TransparencyEnum.AlphaHash)) p.Add("alphaext=aa");
        if (BlendMode != BlendModeEnum.Mix) p.Add("blend=" + new[] { "mix", "add", "sub", "mul", "premult" }[(int)BlendMode]);
        if (DepthDrawMode == DepthDrawModeEnum.Always) p.Add("depthdraw=always");
        if (DepthDrawMode == DepthDrawModeEnum.Disabled) p.Add("depthdraw=never");
        if (NoDepthTest) p.Add("nodepthtest=1");
        if (RenderPriority != 0) p.Add($"priority={RenderPriority}");
        if (ShadingMode == ShadingModeEnum.Unshaded) p.Add("unshaded=1");
        if (DisableReceiveShadows) p.Add("noshadows=1");
        if (DisableAmbientLight) p.Add("noambient=1");
        if (BillboardMode != BillboardModeEnum.Disabled)
        {
            p.Add("billboard=" + (BillboardMode == BillboardModeEnum.Enabled ? "on" : BillboardMode == BillboardModeEnum.FixedY ? "y" : "particles"));
            if (BillboardKeepScale) p.Add("keepscale=1");
            if (BillboardMode == BillboardModeEnum.Particles) p.Add($"frames={ParticlesAnimHFrames},{ParticlesAnimVFrames}");
        }
        if (FixedSize) p.Add("fixedsize=1");
        if (Grow) { p.Add("grow=1"); p.Add($"growcm={F(GrowAmount * 100.0)}"); }
        if (DistanceFadeMode != DistanceFadeModeEnum.Disabled)
        {
            p.Add("fade=" + (DistanceFadeMode == DistanceFadeModeEnum.PixelAlpha ? "alpha" : DistanceFadeMode == DistanceFadeModeEnum.PixelDither ? "dither" : "object"));
            p.Add($"fademin={F(DistanceFadeMinDistance * 100.0)}");
            p.Add($"fademax={F(DistanceFadeMaxDistance * 100.0)}");
        }
        if (RimEnabled) { p.Add("rim=1"); p.Add($"rimamount={F(Rim)}"); p.Add($"rimtint={F(RimTint)}"); }
        if (AnisotropyEnabled) { p.Add("aniso=1"); p.Add($"anisoamount={F(Anisotropy)}"); }
        if (HeightmapEnabled)
        {
            p.Add("height=1"); p.Add($"heightscale={F(HeightmapScale)}");
            if (HeightmapDeepParallax) { p.Add("deepparallax=1"); p.Add($"layers={HeightmapMinLayers},{HeightmapMaxLayers}"); }
            if (HeightmapFlipTangent) p.Add("flipt=1");
            if (HeightmapFlipBinormal) p.Add("flipb=1");
            if (HeightmapFlipTexture) p.Add("invertheight=1");
        }
        if (BacklightEnabled) { p.Add("backlight=1"); p.Add($"backlightcolor={F(Backlight.R)},{F(Backlight.G)},{F(Backlight.B)}"); }
        if (DetailEnabled)
        {
            p.Add("detail=1");
            if (DetailBlendMode != BlendModeEnum.Mix) p.Add($"detailblend={System.Math.Min((int)DetailBlendMode, 3)}");
            if (DetailUvLayer == DetailUVEnum.DetailUv2) p.Add("detailuv=2");
        }
        if (EmissionOperator == EmissionOperatorEnum.Multiply) p.Add("emitmul=1");
        if (EmissionOnUv2) p.Add("emituv2=1");
        if (AoOnUv2) p.Add("aouv2=1");
        if (AoLightAffect != 0) p.Add($"aoaffect={F(AoLightAffect)}");
        if (SpecularMode != SpecularModeEnum.SchlickGgx) p.Add("spec=" + (SpecularMode == SpecularModeEnum.Toon ? "toon" : "off"));
        if (DiffuseMode != DiffuseModeEnum.Burley) p.Add("diffuse=" + (DiffuseMode == DiffuseModeEnum.Lambert ? "lambert" : DiffuseMode == DiffuseModeEnum.LambertWrap ? "wrap" : "toon"));
        return string.Join(' ', p);
    }

    /// <summary>Takes the colour, metallic and roughness the engine draws an .ocmat with, so a material a script gets from a mesh reads as authored.</summary>
    internal void ReadOcmat(string file)
    {
        string[] lines;
        try { lines = File.ReadAllLines(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        _loading = true;
        try { ReadOcmatLines(lines); }
        finally { _loading = false; }
    }

    private void ReadOcmatLines(string[] lines)
    {
        foreach (string line in lines)
        {
            string[] t = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length >= 2 && t[0] == "FX") { ReadFx(t); continue; }
            if (t.Length < 3 || t[0] != "PARAM") continue;
            var v = new float[t.Length - 2];
            bool numbers = true;
            for (int i = 0; i < v.Length && numbers; i++)
                numbers = float.TryParse(t[i + 2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[i]);
            if (!numbers) continue;
            switch (t[1])
            {
                case "baseColorFactor" when v.Length >= 4: AlbedoColor = new Color(v[0], v[1], v[2], v[3]); break;
                case "metallicFactor": Metallic = v[0]; break;
                case "roughnessFactor": Roughness = v[0]; break;
                case "emissiveFactor" when v.Length >= 3: ReadEmissiveFactor(v[0], v[1], v[2]); break;
                case "emissiveEnergy": EmissionEnergyMultiplier = v[0]; break;
            }
        }
    }

    /// <summary>The F0 of the dielectric base the converter writes: 0.08 * metallic_specular (Godot's rule).</summary>
    public float Reflectance => MetallicSpecular * 0.08f;
}

public class StandardMaterial3D : BaseMaterial3D
{
}

/// <summary>Godot's ORMMaterial3D: a StandardMaterial3D whose occlusion, roughness and metallic come from one packed texture (R, G, B), the layout the
/// .ocmat packs. The converter maps both classes the same way.</summary>
public class ORMMaterial3D : BaseMaterial3D
{
    public Texture2D? OrmTexture { get; set; }
}

public class Texture2D : Texture
{
    private Vector2? _fileSize;
    private bool _pngRead;

    /// <summary>The pixels of the texture as an <see cref="Image"/> (Godot's get_image): a file texture is read from its file; a texture a script built or generated gives its own pixels;
    /// a texture of a view or one whose file cannot be read gives an empty image.</summary>
    public virtual Image GetImage()
    {
        if (ResourcePath.Length == 0) return new Image();
        return Image.LoadFromFile(ResourcePath);
    }

    public virtual bool HasAlpha() => true;

    /// <summary>The size in pixels. A PNG file texture reads it from the file's header (G5 U1: a 9-patch needs it); any other format is sized by the engine
    /// once it has decoded the picture for the UI (zero until then, and outside the engine); a <see cref="ViewportTexture"/> reports the size its view is drawn at.</summary>
    public override Vector2 GetSize()
    {
        if (_fileSize is { } known) return known;
        if (!_pngRead)
        {
            _pngRead = true;
            Vector2 png = ReadPngSize();
            if (png.X > 0 && png.Y > 0) return (_fileSize = png).Value;
        }
        if (UiImages.SizeOf(EngineHandle) is { } engine) return (_fileSize = engine).Value;
        return Vector2.Zero;
    }

    private Vector2 ReadPngSize()
    {
        if (ResourcePath.Length == 0 || !ResourcePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return Vector2.Zero;
        try
        {
            string? file = ResourceLoader.FindFile(ResourcePath);
            if (file == null) return Vector2.Zero;
            var head = new byte[24];
            using (var s = System.IO.File.OpenRead(file)) if (s.Read(head, 0, 24) < 24) return Vector2.Zero;
            if (head[0] != 0x89 || head[1] != (byte)'P' || head[2] != (byte)'N' || head[3] != (byte)'G') return Vector2.Zero;
            return new Vector2((head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19], (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23]);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { return Vector2.Zero; }
    }

    private ulong _hostHandle;
    private int _resolvedEpoch = -1;   // the UiImages epoch HandleResolver's answer was taken in; -1 when the host set the handle itself

    /// <summary>The engine's texture handle for UI drawing (the id <c>aver_ui_textured_rect</c> takes), 0 when the host has not given the texture one.
    /// The host sets it, or <see cref="HandleResolver"/> finds it by path on first use (G5 U1: style boxes with a texture need it).</summary>
    public ulong HostHandle
    {
        get => _hostHandle;
        set { _hostHandle = value; _resolvedEpoch = -1; }
    }

    /// <summary>Finds a texture's engine handle by its resource path. In the engine it is <see cref="UiImages.Resolve"/>: the project's file, loaded and
    /// premultiplied by the engine's UI image table, one reference per path until the play session ends (P39). A test or another host replaces it.</summary>
    public static Func<Texture2D, ulong>? HandleResolver { get; set; } = UiImages.Resolve;

    internal virtual ulong EngineHandle
    {
        get
        {
            // A handle taken in an earlier play session was released with it; the texture asks again.
            if (_resolvedEpoch >= 0 && _resolvedEpoch != UiImages.Epoch) { _hostHandle = 0; _resolvedEpoch = -1; }
            if (_hostHandle == 0 && HandleResolver != null && ResourcePath.Length > 0)
            {
                _hostHandle = HandleResolver(this);
                _resolvedEpoch = UiImages.Epoch;
            }
            return _hostHandle;
        }
    }
}

/// <summary>A collision shape resource (Godot's Shape3D). Sizes are in metres.</summary>
public class Shape3D : Resource
{
    public float Margin { get; set; } = 0.04f;

    private static readonly Dictionary<ulong, WeakReference<Shape3D>> s_byRid = new();
    private static ulong s_nextRid = 1UL << 40;   // far above the body ids, so a shape's Rid is never taken for a body's
    private ulong _rid;
    private ArrayMesh? _debug;

    /// <summary>The shape's id, the same for as long as the shape lives (Godot's get_rid); what a query's shape_rid names.</summary>
    public new Rid GetRid()
    {
        if (_rid == 0)
        {
            lock (s_byRid) { _rid = s_nextRid++; s_byRid[_rid] = new WeakReference<Shape3D>(this); }
        }
        return new Rid(_rid);
    }

    /// <summary>The shape an id named by <see cref="GetRid"/> belongs to, or null.</summary>
    internal static Shape3D? FromRid(Rid rid)
    {
        lock (s_byRid) return s_byRid.TryGetValue(rid.Id, out var w) && w.TryGetTarget(out Shape3D? s) ? s : null;
    }

    /// <summary>Adds the shape's wireframe as pairs of points (a line each) in the shape's own space, metres. A shape with no drawn form adds none.</summary>
    protected virtual void DebugLines(List<Vector3> lines) { }

    /// <summary>The outline of the shape as a mesh of lines (Godot's get_debug_mesh), for drawing it. Lines are drawn as thin crossed quads
    /// (<see cref="Mesh.ThinPrimitiveWidth"/>). Null when the shape has no outline.</summary>
    public ArrayMesh? GetDebugMesh()
    {
        if (_debug != null) return _debug;
        var pts = new List<Vector3>();
        DebugLines(pts);
        if (pts.Count == 0) return null;
        var arrays = new Collections.Array();
        for (int i = 0; i < (int)Mesh.ArrayType.Max; i++) arrays.Add(default);
        arrays[(int)Mesh.ArrayType.Vertex] = (Variant)pts.ToArray();
        _debug = new ArrayMesh();
        _debug.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
        return _debug;
    }

    // Helpers for the outlines.
    internal static void Circle(List<Vector3> l, Vector3 centre, Vector3 u, Vector3 v, float r, int n = 32)
    {
        for (int i = 0; i < n; i++)
        {
            float a = MathF.Tau * i / n, b = MathF.Tau * (i + 1) / n;
            l.Add(centre + (u * MathF.Cos(a) + v * MathF.Sin(a)) * r);
            l.Add(centre + (u * MathF.Cos(b) + v * MathF.Sin(b)) * r);
        }
    }

    internal static void Arc(List<Vector3> l, Vector3 centre, Vector3 u, Vector3 v, float r, float from, float to, int n = 16)
    {
        for (int i = 0; i < n; i++)
        {
            float a = from + (to - from) * i / n, b = from + (to - from) * (i + 1) / n;
            l.Add(centre + (u * MathF.Cos(a) + v * MathF.Sin(a)) * r);
            l.Add(centre + (u * MathF.Cos(b) + v * MathF.Sin(b)) * r);
        }
    }
}

public class BoxShape3D : Shape3D
{
    /// <summary>The full size of the box (not the half extents).</summary>
    public Vector3 Size { get; set; } = Vector3.One;

    /// <summary>The half extents (Godot 3's name; <see cref="Size"/> is twice this).</summary>
    public Vector3 Extents { get => Size * 0.5f; set => Size = value * 2f; }

    protected override void DebugLines(List<Vector3> l)
    {
        Vector3 h = Size * 0.5f;
        Vector3 Corner(int k) => new((k & 1) == 0 ? -h.X : h.X, (k & 2) == 0 ? -h.Y : h.Y, (k & 4) == 0 ? -h.Z : h.Z);
        for (int i = 0; i < 8; i++)
            foreach (int bit in new[] { 1, 2, 4 })
                if ((i & bit) == 0) { l.Add(Corner(i)); l.Add(Corner(i | bit)); }
    }
}

public class SphereShape3D : Shape3D
{
    public float Radius { get; set; } = 0.5f;

    protected override void DebugLines(List<Vector3> l)
    {
        Circle(l, Vector3.Zero, Vector3.Right, Vector3.Up, Radius);
        Circle(l, Vector3.Zero, Vector3.Right, Vector3.Back, Radius);
        Circle(l, Vector3.Zero, Vector3.Up, Vector3.Back, Radius);
    }
}

public class CapsuleShape3D : Shape3D
{
    public float Radius { get; set; } = 0.5f;
    /// <summary>The whole height, including both caps.</summary>
    public float Height { get; set; } = 2f;

    protected override void DebugLines(List<Vector3> l)
    {
        float half = MathF.Max(Height * 0.5f - Radius, 0f);
        var top = new Vector3(0, half, 0);
        Circle(l, top, Vector3.Right, Vector3.Back, Radius);
        Circle(l, -top, Vector3.Right, Vector3.Back, Radius);
        Arc(l, top, Vector3.Right, Vector3.Up, Radius, 0, MathF.PI);
        Arc(l, top, Vector3.Back, Vector3.Up, Radius, 0, MathF.PI);
        Arc(l, -top, Vector3.Right, Vector3.Down, Radius, 0, MathF.PI);
        Arc(l, -top, Vector3.Back, Vector3.Down, Radius, 0, MathF.PI);
        foreach (Vector3 side in new[] { Vector3.Right, Vector3.Left, Vector3.Back, Vector3.Forward }) { l.Add(top + side * Radius); l.Add(-top + side * Radius); }
    }
}

public class CylinderShape3D : Shape3D
{
    public float Radius { get; set; } = 0.5f;
    public float Height { get; set; } = 2f;

    protected override void DebugLines(List<Vector3> l)
    {
        var top = new Vector3(0, Height * 0.5f, 0);
        Circle(l, top, Vector3.Right, Vector3.Back, Radius);
        Circle(l, -top, Vector3.Right, Vector3.Back, Radius);
        foreach (Vector3 side in new[] { Vector3.Right, Vector3.Left, Vector3.Back, Vector3.Forward }) { l.Add(top + side * Radius); l.Add(-top + side * Radius); }
    }
}

public class WorldBoundaryShape3D : Shape3D
{
    public Plane Plane { get; set; } = new(0, 1, 0, 0);

    protected override void DebugLines(List<Vector3> l)
    {
        Vector3 n = Plane.Normal.Normalized(), u = n.Cross(MathF.Abs(n.Y) < 0.99f ? Vector3.Up : Vector3.Right).Normalized(), v = n.Cross(u);
        Vector3 c = n * Plane.D;
        for (int i = -2; i <= 2; i++)
        {
            l.Add(c + u * i - v * 2f); l.Add(c + u * i + v * 2f);
            l.Add(c + v * i - u * 2f); l.Add(c + v * i + u * 2f);
        }
        l.Add(c); l.Add(c + n);
    }
}

public class ConvexPolygonShape3D : Shape3D
{
    public Vector3[] Points { get; set; } = System.Array.Empty<Vector3>();

    // The corners of the box around the points: a hull's real edges would need its faces, which the engine keeps.
    protected override void DebugLines(List<Vector3> l)
    {
        if (Points.Length == 0) return;
        Vector3 lo = Points[0], hi = Points[0];
        foreach (Vector3 p in Points) { lo = lo.Min(p); hi = hi.Max(p); }
        Vector3 centre = (lo + hi) * 0.5f, h = (hi - lo) * 0.5f;
        Vector3 Corner(int k) => centre + new Vector3((k & 1) == 0 ? -h.X : h.X, (k & 2) == 0 ? -h.Y : h.Y, (k & 4) == 0 ? -h.Z : h.Z);
        for (int i = 0; i < 8; i++)
            foreach (int bit in new[] { 1, 2, 4 })
                if ((i & bit) == 0) { l.Add(Corner(i)); l.Add(Corner(i | bit)); }
    }
}

public class ConcavePolygonShape3D : Shape3D
{
    public Vector3[] Data { get; set; } = System.Array.Empty<Vector3>();

    /// <summary>Whether a ray hits the back of a triangle. Stored: a mesh body takes back-face hits from the query, as Godot's does by default.</summary>
    public bool BackfaceCollision { get; set; }

    protected override void DebugLines(List<Vector3> l)
    {
        for (int i = 0; i + 2 < Data.Length; i += 3)
        {
            l.Add(Data[i]); l.Add(Data[i + 1]);
            l.Add(Data[i + 1]); l.Add(Data[i + 2]);
            l.Add(Data[i + 2]); l.Add(Data[i]);
        }
    }

    /// <summary>The triangles as three points each (Godot's get_faces).</summary>
    public Vector3[] GetFaces() => (Vector3[])Data.Clone();
}

/// <summary>A terrain made of a grid of heights (Godot's HeightMapShape3D): <see cref="MapWidth"/> points along X and <see cref="MapDepth"/> along Z, one metre
/// apart, centred on the origin, <see cref="MapData"/> the heights row by row. The engine holds it as a triangle mesh; scale the node to space the points.</summary>
public class HeightMapShape3D : Shape3D
{
    private int _w = 2, _d = 2;
    private float[] _data = new float[4];

    public int MapWidth { get => _w; set { _w = Math.Max(value, 1); Resize(); } }
    public int MapDepth { get => _d; set { _d = Math.Max(value, 1); Resize(); } }
    public float[] MapData { get => _data; set => _data = value ?? System.Array.Empty<float>(); }

    private void Resize()
    {
        if (_data.Length != _w * _d) System.Array.Resize(ref _data, _w * _d);
    }

    /// <summary>The lowest and highest height (Godot's get_min_value, get_max_value).</summary>
    public float GetMinHeight() => _data.Length == 0 ? 0f : _data.Min();
    public float GetMaxHeight() => _data.Length == 0 ? 0f : _data.Max();

    protected override void DebugLines(List<Vector3> l)
    {
        for (int z = 0; z < _d; z++)
            for (int x = 0; x < _w; x++)
            {
                var p = new Vector3(x - (_w - 1) * 0.5f, _data[z * _w + x], z - (_d - 1) * 0.5f);
                if (x + 1 < _w) { l.Add(p); l.Add(new Vector3(x + 1 - (_w - 1) * 0.5f, _data[z * _w + x + 1], p.Z)); }
                if (z + 1 < _d) { l.Add(p); l.Add(new Vector3(p.X, _data[(z + 1) * _w + x], z + 1 - (_d - 1) * 0.5f)); }
            }
    }
}

/// <summary>A ray that pushes a body out of what it points at (Godot's SeparationRayShape3D), along the shape's local +Z. The engine's version is a thin solid
/// capsule, so the body stops with the tip on the surface and slides along the contact normal; <see cref="SlideOnSlope"/> is stored.</summary>
public class SeparationRayShape3D : Shape3D
{
    public float Length { get; set; } = 1f;
    public bool SlideOnSlope { get; set; }

    protected override void DebugLines(List<Vector3> l) { l.Add(Vector3.Zero); l.Add(new Vector3(0, 0, Length)); }
}
