// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Godot's Decal: a box that paints an image onto the surfaces inside it. Drawn as one pooled engine decal (docs/rendering/DECALS.md), approximated.

using Aver.Scene;

namespace Aver.Fabric;

/// <summary>Godot's Decal. The box projects down its local -Y, as in Godot. Drawn as one pooled engine decal (Aver.Scene.Decals) placed when the node enters the tree and
/// placed again once a frame when a property or the global transform changed (approximated): the albedo image and its alpha, the modulate colour, albedo mix, size, the
/// distance fade, the normal and ORM images, and (wave 6, P13) the emission image and energy, the upper and lower fade and the normal fade are applied; the cull mask is kept for
/// scripts and not drawn (the engine paints every surface). The emission, the depth and normal fades and the albedo mix are drawn by the plain raster pipeline only, not by
/// ray-driven mode (docs/rendering/DECALS.md). The engine pool holds 256 decals and recycles the oldest, so a script that spawns a decal per bullet hole keeps the latest 256.
/// Without an engine nothing is drawn.
///
/// The axes, derived from Godot's decal shader (the box's xform maps the surface into 0..1; the texture coordinate is the local x and z, the depth is y, and the projector looks
/// along -Y): Godot's image u runs along local +X, its v along local +Z (down the image), the projection along -Y. The engine's decal projects along its +X with u along +Y and
/// v-up along +Z. With Godot's -Z forward mapped to the engine's +X (FabricSpace), the engine's decal frame is the node's frame turned a quarter about the engine's Y:
/// engine X = Godot -Y (down), engine Y = Godot +X (right), engine Z = Godot -Z (image up). <c>Wave6Tests</c> checks all three for arbitrary rotations.</summary>
public class Decal : VisualInstance3D
{
    public enum DecalTexture { Albedo = 0, Normal = 1, Orm = 2, Emission = 3, Max = 4 }

    private readonly Texture2D?[] _textures = new Texture2D?[4];
    private Vector3 _size = new(2, 2, 2);
    private Color _modulate = new(1, 1, 1, 1);
    private float _emissionEnergy = 1f, _albedoMix = 1f, _normalFade, _upperFade = 0.3f, _lowerFade = 0.3f, _fadeBegin = 40f, _fadeLength = 10f;
    private bool _fadeEnabled;
    private uint _cullMask = 0xFFFFFu;
    private int _entity;
    private bool _dirty;
    private Transform3D _published;
    private bool _havePublished;

    /// <summary>The box in metres: x and z are the extent on the surface, y how far the projection reaches.</summary>
    public Vector3 Size { get => _size; set { _size = value; _dirty = true; } }
    public Texture2D? TextureAlbedo { get => _textures[0]; set => SetTexture(DecalTexture.Albedo, value); }
    public Texture2D? TextureNormal { get => _textures[1]; set => SetTexture(DecalTexture.Normal, value); }
    public Texture2D? TextureOrm { get => _textures[2]; set => SetTexture(DecalTexture.Orm, value); }
    public Texture2D? TextureEmission { get => _textures[3]; set => SetTexture(DecalTexture.Emission, value); }
    public float EmissionEnergy { get => _emissionEnergy; set { _emissionEnergy = value; _dirty = true; } }
    /// <summary>Multiplies the albedo image (sRGB); its alpha fades the decal.</summary>
    public Color Modulate { get => _modulate; set { _modulate = value; _dirty = true; } }
    public float AlbedoMix { get => _albedoMix; set { _albedoMix = value; _dirty = true; } }
    public float NormalFade { get => _normalFade; set { _normalFade = value; _dirty = true; } }
    public float UpperFade { get => _upperFade; set { _upperFade = value; _dirty = true; } }
    public float LowerFade { get => _lowerFade; set { _lowerFade = value; _dirty = true; } }
    public bool DistanceFadeEnabled { get => _fadeEnabled; set { _fadeEnabled = value; _dirty = true; } }
    /// <summary>Metres from the camera where the fade starts.</summary>
    public float DistanceFadeBegin { get => _fadeBegin; set { _fadeBegin = value; _dirty = true; } }
    /// <summary>Metres the fade takes to reach nothing.</summary>
    public float DistanceFadeLength { get => _fadeLength; set { _fadeLength = value; _dirty = true; } }
    /// <summary>The render layers of the surfaces it paints. Kept, not applied: the engine paints every surface.</summary>
    public uint CullMask { get => _cullMask; set { _cullMask = value; _dirty = true; } }

    public void SetTexture(DecalTexture type, Texture2D? texture)
    {
        if ((int)type is < 0 or > 3) return;
        _textures[(int)type] = texture;
        _dirty = true;
    }

    public Texture2D? GetTexture(DecalTexture type) => (int)type is >= 0 and <= 3 ? _textures[(int)type] : null;

    /// <summary>Times the engine decal was placed (0 without an engine).</summary>
    internal int PublishCount { get; private set; }

    /// <summary>The engine decal's entity, 0 when it has none.</summary>
    internal int DecalEntity => _entity;

    // "res://a/b.png" is the converted project's Textures/a/b.png, as for shader textures.
    private static long ImageId(Texture2D? t)
    {
        if (t == null || t.ResourcePath.Length == 0) return 0;
        string rel = t.ResourcePath.StartsWith("res://", StringComparison.Ordinal) ? "Textures/" + t.ResourcePath["res://".Length..] : t.ResourcePath;
        try { return Assets.ObjectIdOf(rel); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return 0; }
    }

    /// <summary>What this decal means in engine terms. Godot projects down local -Y with the image's u along local +X; the engine projects along local +X with u along
    /// local Y, so the pose is the node's turned a quarter about Y.</summary>
    internal DecalDesc ComputeDesc(bool withImages = true)
    {
        Transform3D g = IsInsideTree() ? GlobalTransform : Transform;
        Vector3 s = g.Basis.GetScale();
        Quat quarter = Quat.FromAxisAngle(new Vec3(0, 1, 0), MathF.PI / 2f);
        Color lin = _modulate.SrgbToLinear();
        float mix = Math.Clamp(_albedoMix, 0f, 1f);
        var d = new DecalDesc
        {
            Position = FabricSpace.ToAverPosition(g.Origin),
            Rotation = FabricSpace.ToAverRotation(g.Basis.GetRotationQuaternion()) * quarter,
            SizeCm = new Vec3(Math.Abs(_size.Y * s.Y) * FabricSpace.CmPerMetre, Math.Abs(_size.X * s.X) * FabricSpace.CmPerMetre, Math.Abs(_size.Z * s.Z) * FabricSpace.CmPerMetre),
            Tint = new Vec3(lin.R, lin.G, lin.B),
            // Godot multiplies the modulate alpha into the decal's alpha (which weighs the albedo, normal and ORM alike) and weighs the albedo again by albedo_mix.
            Transparency = 1f - Math.Clamp(_modulate.A, 0f, 1f),
            UseAlbedoMix = true,
            AlbedoMix = mix,
            // Godot fades along the projection by (1 - |y| / half)^fade on each side of the centre, and has no soft edge on the sides: a hard 2% edge stands in for that.
            UseDepthFade = true,
            UpperFade = Math.Max(_upperFade, 0f),
            LowerFade = Math.Max(_lowerFade, 0f),
            NormalFade = Math.Clamp(_normalFade, 0f, 0.999f),
            EdgeFade = 0.02f,
            EmissionEnergy = _emissionEnergy,
            FadeDistanceCm = _fadeEnabled ? (_fadeBegin + _fadeLength) * FabricSpace.CmPerMetre : 0f,
            LifetimeSeconds = 0f,
        };
        // A decal with no albedo image does not paint a flat colour (Godot paints the albedo only when it has one).
        if (_textures[0] == null) d.Disable |= DecalChannels.Colour;
        if (withImages)
        {
            d.BaseTexture = ImageId(_textures[0]);
            d.NormalTexture = ImageId(_textures[1]);
            d.OrmTexture = ImageId(_textures[2]);
            d.EmissionTexture = ImageId(_textures[3]);
        }
        return d;
    }

    private void Release()
    {
        if (_entity == 0) return;
        int e = _entity;
        _entity = 0;
        try { Decals.Release(e); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    private void Publish()
    {
        _dirty = false;
        if (!IsInsideTree() || !NodeHost.Current.IsEngine) return;
        Release();
        _published = GlobalTransform;
        _havePublished = true;
        if (!IsVisibleInTree()) return;
        DecalDesc d = ComputeDesc();
        try { _entity = Decals.Spawn(d); PublishCount++; }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { _entity = 0; }
    }

    internal override void OnEnteringTree()
    {
        base.OnEnteringTree();
        Publish();
    }

    internal override void OnExitingTree()
    {
        Release();
        _havePublished = false;
        base.OnExitingTree();
    }

    internal override void OnVisibilityApplied(bool effective)
    {
        if (!effective) Release();
        else if (IsInsideTree()) Publish();
    }

    // One placement a frame at most: a script that sets five properties after adding the node places the decal once.
    internal override bool UsesInternalProcess => true;

    internal override void InternalProcess(double delta)
    {
        if (!_havePublished) return;
        if (_dirty || !GlobalTransform.Equals(_published)) Publish();
    }
}
