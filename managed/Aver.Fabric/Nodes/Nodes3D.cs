// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// The 3D node classes of a converted scene: markers, meshes, lights, cameras and collision shapes. Each keeps Godot's properties
// as plain values and, once it has an entity, pushes the ones the engine can use into the entity's components.

using Aver.Framework;

namespace Aver.Fabric;

/// <summary>A point in space to mark a position (Godot's Marker3D).</summary>
public class Marker3D : Node3D
{
    public float GizmoExtents { get; set; } = 0.25f;
}

/// <summary>Anything drawn in 3D (Godot's VisualInstance3D).</summary>
public class VisualInstance3D : Node3D
{
    /// <summary>Godot's layers and cull masks are 20 bits.</summary>
    internal const uint AllLayers = 0xFFFFFu;

    /// <summary>A layer mask as the engine stores it: inverted, so that zero (an entity that never heard of layers) means every layer.</summary>
    internal static int StoreMask(uint mask) => (int)(~mask & AllLayers);

    private uint _layers = 1;

    /// <summary>The box the instance occupies, in its own space (empty for one that draws nothing).</summary>
    public virtual Aabb GetAabb() => default;

    /// <summary>The render layers this instance is on (default layer 1). A camera draws it when the camera's <c>CullMask</c> shares a layer.</summary>
    public uint Layers { get => _layers; set { _layers = value; ApplyRenderMasks(); } }
    public bool GetLayerMaskValue(int layerNumber) => (Layers & (1u << (layerNumber - 1))) != 0;
    public void SetLayerMaskValue(int layerNumber, bool value) => Layers = value ? Layers | (1u << (layerNumber - 1)) : Layers & ~(1u << (layerNumber - 1));

    /// <summary>Writes the layers (and, for geometry, the visibility range) into the entity's mesh renderer when it has one.</summary>
    internal virtual void ApplyRenderMasks()
    {
        if (!Self.IsValid || !NodeHost.Current.IsEngine || !Self.HasComponent(Component.MeshRenderer)) return;
        Self.SetInt("CMeshRenderer.layersInv", StoreMask(_layers));
    }
}

/// <summary>A visibility range in engine terms: centimetres and the fade mode, apart from the push so it can be checked without an engine.</summary>
internal readonly struct RangeFields
{
    public float BeginCm { get; init; }
    public float EndCm { get; init; }
    public float BeginMarginCm { get; init; }
    public float EndMarginCm { get; init; }
    public int FadeMode { get; init; }
}

/// <summary>A mesh instance's per-instance render flags in engine terms (CMeshRenderer.renderFlags, cullMarginCm, lodBias), apart from the push so they can be checked
/// without an engine. <c>ShadowMode</c> 0 casts, 1 casts none, 2 shadows only; <c>GiMode</c> 0 static, 1 dynamic, 2 disabled.</summary>
internal readonly struct InstanceFlags
{
    public int ShadowMode { get; init; }
    public int GiMode { get; init; }
    public bool IgnoreOcclusion { get; init; }
    /// <summary>Bit 5 (<c>kRenderFirstPerson</c>): the engine draws the mesh with the first-person projection and depth range.</summary>
    public bool FirstPerson { get; init; }
    public float CullMarginCm { get; init; }
    public float LodBiasStops { get; init; }

    /// <summary>The renderFlags word: bits 0-1 the shadow mode, bits 2-3 the GI mode, bit 4 the occlusion opt-out, bit 5 first person.</summary>
    public int RenderFlags => (ShadowMode & 3) | ((GiMode & 3) << 2) | (IgnoreOcclusion ? 0x10 : 0) | (FirstPerson ? 0x20 : 0);

    /// <summary>True when every field is the ordinary mesh's: nothing needs writing.</summary>
    public bool IsDefault => RenderFlags == 0 && CullMarginCm == 0f && LodBiasStops == 0f;
}

public class GeometryInstance3D : VisualInstance3D
{
    public enum ShadowCastingSetting { Off = 0, On = 1, DoubleSided = 2, ShadowsOnly = 3 }

    // Per-instance shader uniforms (Godot's instance uniforms): kept for scripts to read back; the engine's materials have no per-instance values to give them to.
    private Dictionary<StringName, Variant>? _instanceParams;

    public void SetInstanceShaderParameter(StringName name, Variant value)
    {
        _instanceParams ??= new Dictionary<StringName, Variant>();
        if (value.IsNil) _instanceParams.Remove(name); else _instanceParams[name] = value;
        Approximated.Once("GeometryInstance3D.SetInstanceShaderParameter", "instance shader parameters are kept for get_instance_shader_parameter; the material does not see them");
    }

    public Variant GetInstanceShaderParameter(StringName name) => _instanceParams != null && _instanceParams.TryGetValue(name, out Variant v) ? v : default;
    public enum VisibilityRangeFadeModeEnum { Disabled = 0, Self = 1, Dependencies = 2 }

    private float _visBegin, _visEnd, _visBeginMargin, _visEndMargin;
    private VisibilityRangeFadeModeEnum _visFade;

    /// <summary>Metres from the camera at which the instance appears (0: no lower bound).</summary>
    public float VisibilityRangeBegin { get => _visBegin; set { _visBegin = value; ApplyRenderMasks(); } }
    /// <summary>Metres from the camera at which the instance disappears (0: no upper bound).</summary>
    public float VisibilityRangeEnd { get => _visEnd; set { _visEnd = value; ApplyRenderMasks(); } }
    /// <summary>The distance past the begin threshold the instance must cross to change state (hysteresis). The fade modes are a hard cut here.</summary>
    public float VisibilityRangeBeginMargin { get => _visBeginMargin; set { _visBeginMargin = value; ApplyRenderMasks(); } }
    public float VisibilityRangeEndMargin { get => _visEndMargin; set { _visEndMargin = value; ApplyRenderMasks(); } }
    public VisibilityRangeFadeModeEnum VisibilityRangeFadeMode { get => _visFade; set { _visFade = value; ApplyRenderMasks(); } }
    /// <summary>Kept, not applied: a visibility dependency is not faded in.</summary>
    public GeometryInstance3D? VisibilityParent { get; set; }

    /// <summary>What the visibility range means in engine terms. Pure: no engine needed.</summary>
    internal RangeFields ComputeRangeFields() => new()
    {
        BeginCm = FabricSpace.ToAverLength(Mathf.Max(_visBegin, 0f)),
        EndCm = FabricSpace.ToAverLength(Mathf.Max(_visEnd, 0f)),
        BeginMarginCm = FabricSpace.ToAverLength(Mathf.Max(_visBeginMargin, 0f)),
        EndMarginCm = FabricSpace.ToAverLength(Mathf.Max(_visEndMargin, 0f)),
        FadeMode = (int)_visFade,
    };

    internal override void ApplyRenderMasks()
    {
        if (!Self.IsValid || !NodeHost.Current.IsEngine) return;
        RangeFields r = ComputeRangeFields();
        int layersInv = StoreMask(Layers);
        foreach (Entity e in RenderEntities)
        {
            if (!e.HasComponent(Component.MeshRenderer)) continue;
            e.SetInt("CMeshRenderer.layersInv", layersInv);
            e.SetFloat("CMeshRenderer.visBeginCm", r.BeginCm);
            e.SetFloat("CMeshRenderer.visEndCm", r.EndCm);
            e.SetFloat("CMeshRenderer.visBeginMarginCm", r.BeginMarginCm);
            e.SetFloat("CMeshRenderer.visEndMarginCm", r.EndMarginCm);
            e.SetInt("CMeshRenderer.visFade", r.FadeMode);
        }
        ApplyInstanceFlags();
    }

    private bool _flagsPushed;

    /// <summary>cast_shadow, gi_mode, extra_cull_margin, ignore_occlusion_culling and lod_bias as the engine stores them. Pure: no engine needed.</summary>
    internal InstanceFlags ComputeInstanceFlags() => new()
    {
        // Off 0 -> 1, On 1 and DoubleSided 2 -> 0 (the double-sided variant is not modelled), ShadowsOnly 3 -> 2.
        ShadowMode = _castShadow switch { ShadowCastingSetting.Off => 1, ShadowCastingSetting.ShadowsOnly => 2, _ => 0 },
        // Godot's Static is Aver's default; Dynamic and Disabled both leave the voxel bake.
        GiMode = _giMode switch { GIModeEnum.Dynamic => 1, GIModeEnum.Disabled => 2, _ => 0 },
        IgnoreOcclusion = _ignoreOcclusion,
        FirstPerson = _firstPerson,
        CullMarginCm = FabricSpace.ToAverLength(Mathf.Max(_extraCullMargin, 0f)),
        LodBiasStops = RenderMapping.LodBiasStops(_lodBias),
    };

    // Written only when an instance leaves the ordinary mesh's values (or came back to them), so a project that states none of these touches nothing.
    private void ApplyInstanceFlags()
    {
        InstanceFlags f = ComputeInstanceFlags();
        if (f.IsDefault && !_flagsPushed) return;
        _flagsPushed = !f.IsDefault;
        foreach (Entity e in RenderEntities)
        {
            if (!e.HasComponent(Component.MeshRenderer)) continue;
            e.SetInt("CMeshRenderer.renderFlags", f.RenderFlags);
            e.SetFloat("CMeshRenderer.cullMarginCm", f.CullMarginCm);
            e.SetFloat("CMeshRenderer.lodBias", f.LodBiasStops);
        }
    }

    // ---- the drawing the level placed ----
    // The converter writes what a node draws as plain mesh placements below its class placement (the engine builds no mesh for a class placement, and
    // the editor shows placements without playing). The Props key `_placed` counts them. At run time the node adopts those entities instead of giving its
    // own entity a second mesh renderer: they carry the mesh, the material, the render masks and the visibility. They are not children in the node tree.
    private List<Entity>? _placedEntities;
    private HashSet<Entity>? _placedSet;
    private bool _placedScanned, _entered;

    /// <summary>The content path of the mesh the placed entities show (null while the node does not know its mesh yet).</summary>
    internal virtual string? PlacedAsset => null;

    /// <summary>The entities that draw this node: the placed ones when it adopted them, else its own entity.</summary>
    internal IEnumerable<Entity> RenderEntities
    {
        get
        {
            if (_placedEntities is { Count: > 0 }) return _placedEntities;
            return Self.IsValid ? new[] { Self } : System.Array.Empty<Entity>();
        }
    }

    internal bool HasPlacedEntities => _placedEntities is { Count: > 0 };

    /// <summary>True while the node knows the level placed its drawing but has not looked for it yet (it has no entity of its own to draw on until then).</summary>
    internal bool PlacedPending => PlacedVisuals > 0 && !_placedScanned && Self.IsValid && NodeHost.Current.IsEngine;

    internal override bool IsPlacedVisual(Entity child) => _placedSet != null && _placedSet.Contains(child);

    // The level attaches the placed entities under their node after every class placement has spawned, so they are looked for when the node enters the tree.
    internal override void AdoptPlacedVisuals()
    {
        if (_placedScanned || PlacedVisuals <= 0 || !Self.IsValid || !NodeHost.Current.IsEngine) return;
        string? asset = PlacedAsset;
        if (asset == null && !_entered) return;
        List<Entity> found = new();
        if (asset != null)
        {
            var kids = new List<(Entity Item, string Name, bool IsActor, bool HasMesh)>();
            foreach (Entity c in Self.Children) kids.Add((c, c.Name, c.IsActor, c.HasComponent(Component.MeshRenderer)));
            found = PlacedMatch.Find(asset, kids);
        }
        if (found.Count == 0 && !_entered) return;
        _placedScanned = true;
        if (found.Count == 0) return;   // none below it (a level edited since, or a node made from a PackedScene): the node draws with its own entity
        _placedEntities = found;
        _placedSet = new HashSet<Entity>(found);
        OnPlacedAdopted(found);
    }

    /// <summary>The placed entities were found. A node that needs exactly the count it was written with (a MultiMesh) may give them up here.</summary>
    internal virtual void OnPlacedAdopted(List<Entity> placed) { }

    /// <summary>Gives the placed entities up and destroys them: the node draws with its own entity from now on.</summary>
    internal void ReleasePlaced()
    {
        if (_placedEntities == null) return;
        List<Entity> gone = _placedEntities;
        _placedEntities = null;
        _placedSet = null;
        PlacedVisuals = 0;
        foreach (Entity e in gone) if (e.IsAlive) e.Destroy();
    }

    internal override void OnEnteringTree()
    {
        base.OnEnteringTree();
        _entered = true;
        AdoptPlacedVisuals();
    }

    /// <summary>Shows or hides everything that draws this node.</summary>
    internal void ApplyVisibleToRender(bool effective)
    {
        foreach (Entity e in RenderEntities)
            if (e.IsValid && e.HasComponent(Component.MeshRenderer)) e.SetVisible(effective);
    }

    private Material? _materialOverride;

    /// <summary>Replaces the mesh's own material; its asset name reaches the entity's mesh renderer.</summary>
    public Material? MaterialOverride
    {
        get => _materialOverride;
        set { _materialOverride = value; OnMaterialOverrideChanged(); }
    }

    internal virtual void OnMaterialOverrideChanged() { }

    /// <summary>Gives the entity's mesh renderer the mesh at <paramref name="assetPath"/> and the material override's asset name. Does nothing without an engine entity.</summary>
    internal void PushMeshAndMaterial(string? assetPath)
    {
        if (!Self.IsValid || !NodeHost.Current.IsEngine || string.IsNullOrEmpty(assetPath)) return;
        AdoptPlacedVisuals();
        if (PlacedPending) return;   // the level's placements are attached when the node enters the tree, which applies the mesh again
        ContentPreload.Mesh(assetPath);
        ContentPreload.Material(MaterialOverride?.ResourcePath);
        foreach (Entity e in RenderEntities)
        {
            e.SetMesh(assetPath);
            if (MaterialOverride is { ResourcePath.Length: > 0 }) e.SetMaterial(System.IO.Path.GetFileNameWithoutExtension(MaterialOverride.ResourcePath));
        }
        (MaterialOverride as BaseMaterial3D)?.SyncToEngine();   // what a script changed before the engine had the material (P14)
        ApplyRenderMasks();
    }

    public Material? MaterialOverlay { get; set; }
    private ShadowCastingSetting _castShadow = ShadowCastingSetting.On;
    private GIModeEnum _giMode = GIModeEnum.Static;
    private float _extraCullMargin, _lodBias = 1f;
    private bool _ignoreOcclusion;
    private bool _firstPerson;

    /// <summary>Off casts no shadow into the sun's cascade and global-illumination shadow maps (the ray-traced sun shadow still sees the mesh); ShadowsOnly draws nothing for
    /// a camera and keeps the mesh for shadows, global illumination and reflections; DoubleSided casts as On does.</summary>
    public ShadowCastingSetting CastShadow { get => _castShadow; set { _castShadow = value; ApplyRenderMasks(); } }
    public float Transparency { get; set; }

    /// <summary>A multiplier on how long the mesh keeps its finer levels of detail (1: the default; 0 forces the coarsest). The engine stores it in stops, the base-2
    /// logarithm; applied by the editor's Play (the shipped runtime selects no levels).</summary>
    public float LodBias { get => _lodBias; set { _lodBias = value; ApplyRenderMasks(); } }

    /// <summary>How an instance takes part in global illumination (Godot's GeometryInstance3D.GIMode).</summary>
    public enum GIModeEnum { Disabled = 0, Static = 1, Dynamic = 2 }

    /// <summary>The resolution of an instance's lightmap relative to the scene's (Godot's GeometryInstance3D.LightmapScale).</summary>
    public enum LightmapScaleEnum { Scale1X = 0, Scale2X = 1, Scale4X = 2, Scale8X = 3, Max = 4 }

    /// <summary>Static is Aver's ordinary mesh (part of the voxel GI bake); Dynamic and Disabled keep the mesh out of the bake as a mover is, and a Disabled one still takes
    /// part in the lighting like any mesh (Aver has no baked lightmaps).</summary>
    public GIModeEnum GIMode { get => _giMode; set { _giMode = value; ApplyRenderMasks(); } }

    /// <summary>Stored, not applied: there is no lightmap to size.</summary>
    public float GILightmapTexelScale { get; set; } = 1f;

    /// <summary>Stored, not applied: there is no lightmap to size.</summary>
    public LightmapScaleEnum GILightmapScale { get; set; } = LightmapScaleEnum.Scale1X;

    /// <summary>Metres the box the frustum and occlusion tests use grows by, so a vertex shader that moves geometry is not culled early.</summary>
    public float ExtraCullMargin { get => _extraCullMargin; set { _extraCullMargin = value; ApplyRenderMasks(); } }

    /// <summary>Opts the instance out of the occlusion test (the editor's Play; the shipped runtime has no occlusion culling).</summary>
    /// <summary>Aver-specific (Godot has no such property): the engine draws this mesh in its first-person pass, with its own projection and its depth squeezed in
    /// front of the world, so it never clips into walls. The converter sets it (<c>aver_first_person</c>) on meshes that use a viewmodel shader, whose
    /// POSITION / PROJECTION_MATRIX work is dropped. Sets CMeshRenderer.renderFlags bit 0x20.</summary>
    public bool FirstPerson { get => _firstPerson; set { _firstPerson = value; ApplyRenderMasks(); } }
    public bool IgnoreOcclusionCulling { get => _ignoreOcclusion; set { _ignoreOcclusion = value; ApplyRenderMasks(); } }

    /// <summary>Stored, not applied: the engine uses the mesh's own bounds.</summary>
    public Aabb CustomAabb { get; set; }
}

/// <summary>A node that draws a mesh (Godot's MeshInstance3D). On entering the tree it gives its entity a mesh renderer for
/// <see cref="Mesh"/>'s asset path.</summary>
public partial class MeshInstance3D : GeometryInstance3D
{
    private Mesh? _mesh;

    public override Aabb GetAabb() => _mesh?.GetAabb() ?? default;

    public Mesh? Mesh
    {
        get => _mesh;
        set { _mesh = value; ApplyMesh(); }
    }

    // Blend shapes by name (an animation's blend-shape track writes them). The engine's morph weight of that name follows once the node has an entity.
    private readonly Dictionary<string, float> _blendShapes = new(StringComparer.Ordinal);

    public void SetBlendShapeValueByName(string name, float value)
    {
        _blendShapes[name] = value;
        ShapeIndex(name);
        if (NodeHost.Current.IsEngine && Self.IsValid) Self.SetMorphWeight(name, value);
    }

    public float GetBlendShapeValueByName(string name) => _blendShapes.TryGetValue(name, out float v) ? v : 0f;

    internal override bool TryDynamicSet(string name, Variant value)
    {
        if (name == "aver_blend_shapes") { SetShapeNames(value.AsString()); return true; }   // the mesh's blend shape names, '|' between them
        if (name == "aver_first_person") { FirstPerson = value.AsBool(); return true; }   // the converter marks meshes drawn with a viewmodel shader
        if (name == "aver_skinned") { _skinned = value.AsBool(); return true; }   // the converter marks the meshes the importer skinned to a bound Skeleton3D
        if (name.StartsWith("surface_material_override/", StringComparison.Ordinal) && int.TryParse(name.AsSpan("surface_material_override/".Length), out int surface))
        {
            SetSurfaceOverrideMaterial(surface, value.As<Material?>());
            return true;
        }
        if (!name.StartsWith("blend_shapes/", StringComparison.Ordinal)) return base.TryDynamicSet(name, value);
        SetBlendShapeValueByName(name["blend_shapes/".Length..], value.AsSingle());
        return true;
    }

    // Skinning (wave 7, F13): a skinned mesh below a Skeleton3D that is bound to the engine's skeleton is posed by that node.
    private bool _skinned;

    private void BindSkin()
    {
        if (!_skinned || !NodeHost.Current.IsEngine || GetParent() is not Skeleton3D sk || sk.EngineSkeletonAsset.Length == 0) return;
        foreach (Entity e in RenderEntities)
            if (e.IsValid && e.HasComponent(Component.MeshRenderer)) sk.AttachSkin(e);
    }

    internal override bool TryDynamicGet(string name, out Variant value)
    {
        if (name.StartsWith("blend_shapes/", StringComparison.Ordinal)) { value = GetBlendShapeValueByName(name["blend_shapes/".Length..]); return true; }
        return base.TryDynamicGet(name, out value);
    }

    // Per-surface overrides (P2). Surface N is the mesh's material slot N; the engine draws that slot of the mesh with the override (a CSurfaceMaterials component on each
    // entity that draws the node). A material_override replaces every surface, as in Godot, so the overrides are not applied while one is set. A level-of-detail, soft-body or
    // GPU-cluster draw is a single draw of the whole mesh and keeps the entity's own material.
    private readonly Dictionary<int, Material?> _surfaceOverrides = new();
    private readonly HashSet<int> _surfaceApplied = new();

    /// <summary>The surfaces of the mesh (at least 1).</summary>
    public int GetSurfaceOverrideMaterialCount() => Math.Max(1, _mesh?.GetSurfaceCount() ?? 0);

    // A mesh with one surface draws one material, so its surface 0 override is the material override (which is how the converter writes it).
    private bool SingleSurface => _mesh != null && _mesh.GetSurfaceCount() <= 1;

    public Material? GetSurfaceOverrideMaterial(int surface) =>
        surface == 0 && SingleSurface ? MaterialOverride : _surfaceOverrides.TryGetValue(surface, out Material? m) ? m : null;

    public void SetSurfaceOverrideMaterial(int surface, Material? material)
    {
        if (surface < 0) return;
        if (surface == 0 && SingleSurface) { MaterialOverride = material; return; }
        if (material == null) _surfaceOverrides.Remove(surface);
        else _surfaceOverrides[surface] = material;
        ApplySurfaceOverrides();
    }

    /// <summary>The slots to override and the engine material name each is drawn with, in slot order: nothing while a material_override is set, and only the materials that
    /// are files (an .ocmat) the engine can find by name.</summary>
    internal List<(int Slot, string Name)> SurfaceOverrideNames()
    {
        var names = new List<(int, string)>();
        if (MaterialOverride != null) return names;
        foreach (var kv in _surfaceOverrides.OrderBy(k => k.Key))
        {
            if (kv.Key >= Entity.SurfaceOverrideSlots || kv.Value is not { ResourcePath.Length: > 0 } m) continue;
            names.Add((kv.Key, System.IO.Path.GetFileNameWithoutExtension(m.ResourcePath)));
        }
        return names;
    }

    private void ApplySurfaceOverrides()
    {
        if (!Self.IsValid || !NodeHost.Current.IsEngine || PlacedPending || MeshAssetOf(_mesh) == null) return;
        List<(int Slot, string Name)> want = SurfaceOverrideNames();
        if (want.Count == 0 && _surfaceApplied.Count == 0) return;
        foreach (Entity e in RenderEntities)
        {
            if (!e.IsValid || !e.HasComponent(Component.MeshRenderer)) continue;
            foreach (int slot in _surfaceApplied) if (want.All(w => w.Slot != slot)) e.ClearSurfaceMaterial(slot);
            foreach ((int slot, string name) in want) e.SetSurfaceMaterial(slot, name);
        }
        _surfaceApplied.Clear();
        foreach ((int slot, _) in want) _surfaceApplied.Add(slot);
        foreach (Material? m in _surfaceOverrides.Values) (m as BaseMaterial3D)?.SyncToEngine();
    }

    /// <summary>The material a surface is drawn with: the material override, else the surface's override, else the mesh's own material for it (Godot's get_active_material).</summary>
    public Material? GetActiveMaterial(int surface)
    {
        if (surface < 0) return null;
        return MaterialOverride ?? GetSurfaceOverrideMaterial(surface) ?? _mesh?.SurfaceGetMaterial(surface);
    }

    // "res://meshes/a.ocmesh" and "meshes/a.ocmesh" both name the content asset "meshes/a.ocmesh".
    internal static string AssetPath(string resourcePath) =>
        resourcePath.StartsWith("res://", StringComparison.Ordinal) ? resourcePath["res://".Length..] : resourcePath;

    // A mesh a script built is named by its runtime key; a file mesh by its path.
    internal static string? MeshAssetOf(Mesh? mesh) =>
        mesh == null ? null : mesh.EngineAssetPath ?? (mesh.ResourcePath.Length == 0 ? null : AssetPath(mesh.ResourcePath));

    private void ApplyMesh()
    {
        PushMeshAndMaterial(MeshAssetOf(_mesh));
        ApplySurfaceOverrides();
        BindSkin();
    }

    internal override void OnMaterialOverrideChanged() => ApplyMesh();

    internal override string? PlacedAsset => MeshAssetOf(_mesh);

    internal override void OnEnteringTree()
    {
        base.OnEnteringTree();
        ApplyMesh();
    }

    internal override void OnVisibilityApplied(bool effective) => ApplyVisibleToRender(effective);
}

/// <summary>Finds the plain mesh placements a level holds below a node among the node's child entities (pure, so it runs without an engine).</summary>
internal static class PlacedMatch
{
    /// <summary>The children that are the node's own drawing: no actor behind them, a mesh renderer, and the name a plain placement of <paramref name="asset"/> gets (its asset path), in order.</summary>
    internal static List<T> Find<T>(string asset, IEnumerable<(T Item, string Name, bool IsActor, bool HasMesh)> children)
    {
        var found = new List<T>();
        foreach ((T item, string name, bool isActor, bool hasMesh) in children)
            if (!isActor && hasMesh && name == asset) found.Add(item);
        return found;
    }
}

/// <summary>The values a light writes into the engine, worked out from Godot's properties and kept apart from the push so they can be checked
/// without an engine (docs/plans/fabric/G4_RENDER.md R2). Lengths in centimetres, angles in degrees, colour linear.</summary>
internal readonly struct LightFields
{
    public int Kind { get; init; }
    public Aver.Scene.Vec3 Colour { get; init; }
    /// <summary>Candela (lux at 1 m) for an omni or spot light; lux for the sun.</summary>
    public float Intensity { get; init; }
    public float RangeCm { get; init; }
    public float InnerDeg { get; init; }
    public float OuterDeg { get; init; }
    public bool Shadows { get; init; }
    public bool Negative { get; init; }
    public float Attenuation { get; init; }
    public float SpotAttenuation { get; init; }
    public float SpecularScale { get; init; }
    public float SourceRadiusCm { get; init; }
    public float ShadowBias { get; init; }
    public float ShadowNormalBias { get; init; }
    public float FadeBeginCm { get; init; }
    public float FadeLengthCm { get; init; }
    public float FadeShadowCm { get; init; }
    /// <summary>light_cull_mask and shadow_caster_mask as the engine stores them (inverted, 20 bits: 0 is every layer).</summary>
    public int CullInv { get; init; }
    public int ShadowCasterInv { get; init; }
    /// <summary>The engine id of the projector image's content path (CLight.cookie); 0 for none.</summary>
    public long Cookie { get; init; }
}

/// <summary>Any light (Godot's Light3D). Its properties reach the entity's light component whenever they change.
/// Calibration (G4_RENDER.md R2): Godot multiplies a light's energy by pi and its diffuse BRDF carries 1/pi, so a white surface under energy E
/// returns E; Aver's BRDF carries 1/pi too and its lights are irradiance in engine units (the 100000 lux sun is 3.0, so one unit is
/// 100000/3 lux or candela), hence <see cref="LuxPerEnergy"/> = pi * 100000 / 3 for the sun and <see cref="CandelaPerEnergy"/> (the
/// irradiance at 1 m, which Godot's attenuation is relative to) the same number for an omni or spot light. Colours are sRGB in Godot and
/// linear in Aver. The local-light shaders read Godot's curves through <c>CLight.kLightParametric</c>.</summary>
public class Light3D : VisualInstance3D
{
    /// <summary>Aver lux of the sun for a Godot energy of 1: pi engine radiance units of 100000/3 lux.</summary>
    public const float LuxPerEnergy = 104719.755f;
    /// <summary>Aver candela of an omni or spot light for a Godot energy of 1: pi engine units of irradiance at 1 m.</summary>
    public const float CandelaPerEnergy = 104719.755f;

    // Godot's CLight flag bits (Components.hpp).
    internal const int FlagNoShadows = 0x1, FlagGodot = 0x4, FlagNegative = 0x8;

    private Color _color = new(1, 1, 1, 1);
    private float _energy = 1f, _specular = 0.5f, _size, _bias = 0.1f, _normalBias = 2f;
    private float _fadeBegin = 40f, _fadeShadow = 50f, _fadeLength = 10f;
    private bool _shadow, _negative, _fade;

    public Color LightColor { get => _color; set { _color = value; ApplyLight(); } }
    public float LightEnergy { get => _energy; set { _energy = value; ApplyLight(); } }

    private float? _intensity;
    private float _temperature = 6500f;
    private Texture2D? _projector;

    /// <summary>The intensity with physical light units on (rendering/lights_and_shadows/use_physical_light_units): lumens for an omni or spot light (default 1000),
    /// lux for the sun (default 100000). It is one stored value in Godot, so <see cref="LightIntensityLux"/> is the same number. With physical units off it is unused.</summary>
    public float LightIntensityLumens { get => _intensity ?? (LightKind == 2 ? 100000f : 1000f); set { _intensity = value; ApplyLight(); } }
    public float LightIntensityLux { get => LightIntensityLumens; set => LightIntensityLumens = value; }

    /// <summary>The colour temperature in kelvin (6500: nearly white). Godot applies it only with physical light units on, and so does Aver.Fabric: a tint multiplied into the
    /// light colour in linear light.</summary>
    public float LightTemperature { get => _temperature; set { _temperature = value; ApplyLight(); } }

    /// <summary>The image projected through a spot or omni light (the engine's cookie). Directional lights ignore it.</summary>
    public Texture2D? LightProjector { get => _projector; set { _projector = value; ApplyLight(); } }

    // Every light in the tree, so a change of exposure (physical light units fold it into the light) reaches them all.
    private static readonly List<Light3D> s_lights = new();
    internal static void ReapplyAll() { foreach (Light3D l in s_lights.ToArray()) l.ApplyLight(); }
    internal static void ResetLights() => s_lights.Clear();
    /// <summary>Kept, not applied: local lights add no indirect light, and the sun's indirect light is Aver's global illumination.</summary>
    public float LightIndirectEnergy { get; set; } = 1f;
    public float LightVolumetricFogEnergy { get; set; } = 1f;

    /// <summary>Whether a light is baked into lightmaps and the voxel GI (Godot's Light3D.BakeMode).</summary>
    public enum BakeMode { Disabled = 0, Static = 1, Dynamic = 2 }

    /// <summary>Stored, not applied (approximated): nothing is baked, every light is dynamic whatever this says.</summary>
    public BakeMode LightBakeMode { get; set; } = BakeMode.Dynamic;

    /// <summary>Stored, not applied: Aver runs no editor, so a light never counts as editor-only.</summary>
    public bool EditorOnly { get; set; }
    /// <summary>Godot's default is 0.5; the engine's specular multiplier is twice this (Godot's specular_amount).</summary>
    public float LightSpecular { get => _specular; set { _specular = value; ApplyLight(); } }
    public bool LightNegative { get => _negative; set { _negative = value; ApplyLight(); } }
    /// <summary>The emitter radius in metres: widens the highlight and the shadow filter.</summary>
    public float LightSize { get => _size; set { _size = value; ApplyLight(); } }
    public bool ShadowEnabled { get => _shadow; set { _shadow = value; ApplyLight(); } }
    public float ShadowBias { get => _bias; set { _bias = value; ApplyLight(); } }
    public float ShadowNormalBias { get => _normalBias; set { _normalBias = value; ApplyLight(); } }
    public float ShadowBlur { get; set; } = 1f;
    /// <summary>Kept, not applied: Godot's shadow darkness scale; Aver's shadows are always full strength.</summary>
    public float ShadowOpacity { get; set; } = 1f;
    /// <summary>Kept, not applied: Godot's switch to draw back faces into the shadow map (a bias fix for the OpenGL renderer); Aver's shadows have no such mode.</summary>
    public bool ShadowReverseCullFace { get; set; }
    public bool DistanceFadeEnabled { get => _fade; set { _fade = value; ApplyLight(); } }
    public float DistanceFadeBegin { get => _fadeBegin; set { _fadeBegin = value; ApplyLight(); } }
    public float DistanceFadeShadow { get => _fadeShadow; set { _fadeShadow = value; ApplyLight(); } }
    public float DistanceFadeLength { get => _fadeLength; set { _fadeLength = value; ApplyLight(); } }
    private uint _cullMask = 0xFFFFFFFFu, _shadowCasterMask = 0xFFFFFFFFu;

    /// <summary>The instance layers this light affects (the raster local-light path; the ray-traced light path and the sun ignore it).</summary>
    public uint LightCullMask { get => _cullMask; set { _cullMask = value; ApplyLight(); } }
    /// <summary>The instance layers that cast this light's shadow (the raster local-light path).</summary>
    public uint ShadowCasterMask { get => _shadowCasterMask; set { _shadowCasterMask = value; ApplyLight(); } }

    /// <summary>The light component's kind: 0 point, 1 spot, 2 directional.</summary>
    internal virtual int LightKind => 0;

    internal override void OnEnteringTree()
    {
        base.OnEnteringTree();
        if (!s_lights.Contains(this)) s_lights.Add(this);
        ApplyLight();
    }

    internal override void OnExitingTree()
    {
        s_lights.Remove(this);
        base.OnExitingTree();
    }

    // "res://a/b.png" is the converted project's Textures/a/b.png (as for decals and shader textures); the converter writes the projector as that path already.
    internal static string ProjectorContentPath(Texture2D? t)
    {
        if (t == null || t.ResourcePath.Length == 0) return "";
        return t.ResourcePath.StartsWith("res://", StringComparison.Ordinal) ? "Textures/" + t.ResourcePath["res://".Length..] : t.ResourcePath;
    }

    private static long ContentId(Texture2D? t)
    {
        string rel = ProjectorContentPath(t);
        if (rel.Length == 0) return 0;
        try { return Aver.Scene.Assets.ObjectIdOf(rel); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return 0; }
    }

    /// <summary>What this light's properties mean in engine terms. Pure: no engine needed. With physical light units on the intensity is Godot's
    /// (light_energy x lumens or lux x its per-type factor x the camera's exposure normalization) and the colour carries the temperature; with them off the
    /// energy is Godot's plain one.</summary>
    internal LightFields ComputeFields()
    {
        Color lin = _color.SrgbToLinear();
        bool sun = LightKind == 2;
        float intensity = _energy * (sun ? LuxPerEnergy : CandelaPerEnergy);
        if (PhysicalLights.Enabled)
        {
            (float tr, float tg, float tb) = RenderMapping.TemperatureTint(_temperature);
            lin = new Color(lin.R * tr, lin.G * tg, lin.B * tb, lin.A);
            var kind = LightKind == 2 ? RenderMapping.LightClass.Directional : LightKind == 1 ? RenderMapping.LightClass.Spot : RenderMapping.LightClass.Omni;
            intensity = RenderMapping.PhysicalAverIntensity(kind, _energy, LightIntensityLumens, PhysicalLights.Normalization);
        }
        var f = new LightFields
        {
            Kind = LightKind,
            Colour = new Aver.Scene.Vec3(lin.R, lin.G, lin.B),
            Intensity = intensity,
            Cookie = sun ? 0 : ContentId(_projector),
            Shadows = _shadow,
            Negative = _negative,
            SpecularScale = Mathf.Max(_specular, 0f) * 2f,
            SourceRadiusCm = FabricSpace.ToAverLength(Mathf.Max(_size, 0f)),
            ShadowBias = _bias,
            ShadowNormalBias = _normalBias,
            FadeBeginCm = _fade ? FabricSpace.ToAverLength(_fadeBegin) : 0f,
            FadeLengthCm = _fade ? FabricSpace.ToAverLength(_fadeLength) : 0f,
            FadeShadowCm = _fade ? FabricSpace.ToAverLength(_fadeShadow) : 0f,
            CullInv = StoreMask(_cullMask),
            ShadowCasterInv = StoreMask(_shadowCasterMask),
        };
        return WithKindValues(f);
    }

    // The values only one kind of light has (range, cone, distance curve).
    internal virtual LightFields WithKindValues(LightFields f) => f;

    /// <summary>Writes this light's values into the entity's light component (adding it first).</summary>
    internal virtual void ApplyLight()
    {
        if (!Self.IsValid || !NodeHost.Current.IsEngine) return;
        if (!Self.HasComponent(Component.Light) && !Self.AddComponent(Component.Light)) return;
        LightFields f = ComputeFields();
        Self.SetInt("CLight.kind", f.Kind);
        Self.SetLightColour(f.Colour);
        Self.SetLightIntensity(f.Intensity);
        Self.SetLightRange(f.RangeCm);
        if (f.Kind == 1) Self.SetLightCone(f.InnerDeg, f.OuterDeg);
        int flags = FlagGodot | (f.Shadows ? 0 : FlagNoShadows) | (f.Negative ? FlagNegative : 0);
        Self.SetInt("CLight.flags", flags);
        Self.SetFloat("CLight.attenuation", f.Attenuation);
        Self.SetFloat("CLight.spotAttenuation", f.SpotAttenuation);
        Self.SetFloat("CLight.specularScale", f.SpecularScale);
        Self.SetFloat("CLight.sourceRadiusCm", f.SourceRadiusCm);
        Self.SetFloat("CLight.shadowBias", f.ShadowBias);
        Self.SetFloat("CLight.shadowNormalBias", f.ShadowNormalBias);
        Self.SetFloat("CLight.fadeBeginCm", f.FadeBeginCm);
        Self.SetFloat("CLight.fadeLengthCm", f.FadeLengthCm);
        Self.SetFloat("CLight.fadeShadowCm", f.FadeShadowCm);
        Self.SetInt("CLight.cullInv", f.CullInv);
        Self.SetInt("CLight.shadowCasterInv", f.ShadowCasterInv);
        if (f.Cookie != 0 || _projectorPushed) { Self.SetInt64("CLight.cookie", f.Cookie); _projectorPushed = f.Cookie != 0; }
    }

    private bool _projectorPushed;
}

/// <summary>A sun (Godot's DirectionalLight3D). THE LEVEL'S <c>SUN</c> RECORD IS THE SUN: the converter writes it from the first directional light
/// in the scene, and the renderers take their sun from the sky state alone (a light component of kind 2 is not drawn). So this node adds no
/// light component. The first directional light to enter the tree is the sun node; its later changes (energy, colour, a script turning it
/// to make a day cycle) are forwarded to the sky's sun overrides (<c>Sky.SetSunIntensity</c>, <c>SetSunColor</c>, <c>SetSunDirection</c>), so
/// there is exactly one sun, owned by the sky. At load nothing is pushed: the record already holds the same values. Any further directional
/// light is inert (the converter reports it).</summary>
public class DirectionalLight3D : Light3D
{
    public float DirectionalShadowMaxDistance { get; set; } = 100f;
    public float LightAngularDistance { get; set; }
    public enum ShadowModeEnum { Orthogonal = 0, Parallel2Splits = 1, Parallel4Splits = 2 }
    public ShadowModeEnum DirectionalShadowMode { get; set; } = ShadowModeEnum.Parallel4Splits;

    /// <summary>What the light does besides lighting the scene: LightOnly lights without drawing in the sky, SkyOnly draws in the sky without lighting.</summary>
    public enum SkyModeEnum { LightAndSky = 0, LightOnly = 1, SkyOnly = 2 }

    /// <summary>Kept as set and not applied: the sun the sky draws is the level's one SUN record, lit and drawn together.</summary>
    public SkyModeEnum SkyMode { get; set; } = SkyModeEnum.LightAndSky;

    private bool _live;

    internal override int LightKind => 2;

    /// <summary>The node that owns the sun, or null.</summary>
    internal static DirectionalLight3D? Sun { get; private set; }
    internal static void ResetSun() => Sun = null;

    /// <summary>True for the one directional light that drives the sky's sun.</summary>
    public bool IsSun => ReferenceEquals(Sun, this);

    internal override void OnEnteringTree()
    {
        if (Sun == null || !Sun.IsInsideTree()) Sun = this;
        base.OnEnteringTree();
        _live = true;   // what follows is a change, not the load
    }

    internal override void ApplyLight()
    {
        if (!_live || !IsSun || !NodeHost.Current.IsEngine) return;
        LightFields f = ComputeFields();
        Aver.Framework.Sky.SetSunIntensity(f.Intensity);
        Aver.Framework.Sky.SetSunColor(f.Colour.X, f.Colour.Y, f.Colour.Z);
    }

    internal override void OnTransformPushed()
    {
        if (!_live || !IsSun || !NodeHost.Current.IsEngine) return;
        Aver.Framework.Sky.SetSunDirection(SunDirection());
    }

    /// <summary>The world direction toward the light in Aver space: the light shines along its -Z, so the sun sits on +Z.</summary>
    internal Aver.Scene.Vec3 SunDirection() => FabricSpace.ToAverDirection(GlobalTransform.Basis.Z.Normalized());
}

public class OmniLight3D : Light3D
{
    private float _range = 5f, _attenuation = 1f;

    public float OmniRange { get => _range; set { _range = value; ApplyLight(); } }
    /// <summary>The distance falloff exponent: 1 (Godot's default) is linear, 2 is inverse square, 0 is constant until the range's edge.</summary>
    public float OmniAttenuation { get => _attenuation; set { _attenuation = value; ApplyLight(); } }
    public enum ShadowMode { DualParaboloid = 0, Cube = 1 }
    /// <summary>Kept: the engine always shadows an omni light with a cube.</summary>
    public ShadowMode OmniShadowMode { get; set; } = ShadowMode.Cube;

    internal override int LightKind => 0;

    internal override LightFields WithKindValues(LightFields f) => f with
    {
        RangeCm = FabricSpace.ToAverLength(_range), Attenuation = _attenuation, SpotAttenuation = 1f,
    };
}

public class SpotLight3D : Light3D
{
    private float _range = 5f, _angle = 45f, _attenuation = 1f, _angleAttenuation = 1f;

    public float SpotRange { get => _range; set { _range = value; ApplyLight(); } }
    /// <summary>The cone's half-angle in degrees.</summary>
    public float SpotAngle { get => _angle; set { _angle = value; ApplyLight(); } }
    /// <summary>The distance falloff exponent (as <see cref="OmniLight3D.OmniAttenuation"/>).</summary>
    public float SpotAttenuation { get => _attenuation; set { _attenuation = value; ApplyLight(); } }
    /// <summary>The cone-edge curve: the cone fades as 1 - rim^(1/this), rim 0 on the axis and 1 at the edge.</summary>
    public float SpotAngleAttenuation { get => _angleAttenuation; set { _angleAttenuation = value; ApplyLight(); } }

    internal override int LightKind => 1;

    // Godot has no inner cone; the ray-traced light path's smooth edge is approximated with one at 80 percent (the raster path uses Godot's curve).
    internal override LightFields WithKindValues(LightFields f) => f with
    {
        RangeCm = FabricSpace.ToAverLength(_range), InnerDeg = _angle * 0.8f, OuterDeg = _angle,
        Attenuation = _attenuation, SpotAttenuation = _angleAttenuation,
    };
}

/// <summary>A camera in engine terms (docs/plans/fabric/G4_RENDER.md R4), worked out from Godot's properties and kept apart from the push.
/// Lengths in centimetres; <c>CullInv</c> is the cull mask stored inverted (0: every layer).</summary>
internal readonly struct CameraFields
{
    public float FovRad { get; init; }
    public float NearCm { get; init; }
    public float FarCm { get; init; }
    public int Projection { get; init; }
    public int Flags { get; init; }
    public float SizeCm { get; init; }
    public float OffsetXCm { get; init; }
    public float OffsetYCm { get; init; }
    public float HOffsetCm { get; init; }
    public float VOffsetCm { get; init; }
    public int CullInv { get; init; }
}

/// <summary>A camera (Godot's Camera3D). The field of view is in degrees, vertical unless <c>KeepAspect</c> is Width; near and far are in metres.
/// The current camera is the engine's view node: it sets the lens (perspective, orthogonal or frustum), the cull mask and the h/v offset. Orthogonal
/// views are drawn by the rasteriser only; see G4_RENDER.md R4.</summary>
public class Camera3D : Node3D
{
    public enum ProjectionType { Perspective = 0, Orthogonal = 1, Frustum = 2 }
    public enum KeepAspectEnum { Width = 0, Height = 1 }
    public enum DopplerTrackingEnum { Disabled = 0, IdleStep = 1, PhysicsStep = 2 }

    /// <summary>Whether the camera's own motion is tracked for the Doppler shift of AudioStreamPlayer3D sounds (G5 S1).</summary>
    public DopplerTrackingEnum DopplerTracking { get; set; } = DopplerTrackingEnum.Disabled;

    private float _fov = 75f, _near = 0.05f, _far = 4000f;

    public float Fov { get => _fov; set { _fov = value; ApplyCamera(); } }
    public float Near { get => _near; set { _near = value; ApplyCamera(); } }
    public float Far { get => _far; set { _far = value; ApplyCamera(); } }
    private float _size = 1f, _hOffset, _vOffset;
    private ProjectionType _projection = ProjectionType.Perspective;
    private Environment? _environment;
    private CameraAttributes? _attributes;
    private KeepAspectEnum _keepAspect = KeepAspectEnum.Height;
    private uint _cullMask = 0xFFFFFu;
    private Vector2 _frustumOffset = Vector2.Zero;

    /// <summary>Metres across the width or height (per <see cref="KeepAspect"/>) of an orthogonal view, or of a frustum at its near plane.</summary>
    public float Size { get => _size; set { _size = value; ApplyCamera(); } }
    public ProjectionType Projection { get => _projection; set { _projection = value; ApplyCamera(); } }
    /// <summary>Which dimension <see cref="Fov"/> and <see cref="Size"/> describe; the other follows the window's aspect.</summary>
    public KeepAspectEnum KeepAspect { get => _keepAspect; set { _keepAspect = value; ApplyCamera(); } }
    /// <summary>The instance layers this camera draws. An instance outside it is hidden from the camera but still casts shadows and lights the scene.</summary>
    public uint CullMask { get => _cullMask; set { _cullMask = value; ApplyCamera(); } }
    /// <summary>Slides the window's frustum sideways at the near plane (metres); frustum projection only.</summary>
    public Vector2 FrustumOffset { get => _frustumOffset; set { _frustumOffset = value; ApplyCamera(); } }
    /// <summary>The camera slides this far along its own right axis (metres).</summary>
    public float HOffset { get => _hOffset; set { _hOffset = value; ApplyCamera(); } }
    /// <summary>The camera slides this far along its own up axis (metres).</summary>
    public float VOffset { get => _vOffset; set { _vOffset = value; ApplyCamera(); } }
    private bool _current;

    /// <summary>Whether this is the camera that is drawn. Set it to take over from the current one.</summary>
    public bool Current
    {
        get => _current;
        set { if (value) MakeCurrent(); else ClearCurrent(); }
    }

    /// <summary>The camera that is drawn: the one made current last; when none was, the first camera to enter the tree.</summary>
    internal static Camera3D? CurrentCamera { get; private set; }

    // The window's cameras in the order they entered the tree (Godot's camera list; a SubViewport's cameras are not in it). The next camera made
    // current when the current one leaves is the first of these, and the first to enter a tree with none becomes current.
    private static readonly List<Camera3D> s_inTree = new();

    // Set when the current camera left the tree: it takes the view again if it comes back (Godot keeps `current` on a camera that exits).
    private bool _wasCurrent;

    internal static void ResetCurrent()
    {
        CurrentCamera = null;
        s_inTree.Clear();
        FabricView.Restore();
    }

    /// <summary>The <see cref="SubViewport"/> this camera is inside (its nearest viewport above), or null for the window's own view. A camera inside one
    /// is that viewport's camera and never takes over the engine's view (G4_RENDER.md R5).</summary>
    internal SubViewport? OwningSubViewport()
    {
        for (Node? n = GetParent(); n != null; n = n.GetParent())
            if (n is Viewport v) return v as SubViewport;
        return null;
    }

    /// <summary>"current" of a camera under a SubViewport: the converter writes it under this name so that applying the property before the node is in the
    /// tree cannot take the window's view.</summary>
    internal bool ViewportCurrent { get; set; }

    // Another camera took the viewport's view: this one is no longer current.
    internal void ClearActiveFlag() => _current = false;

    public void MakeCurrent()
    {
        if (IsInsideTree() && OwningSubViewport() is { } sv)
        {
            _current = true;
            sv.SetActiveCamera(this);
            return;
        }
        Camera3D? previous = CurrentCamera;
        if (CurrentCamera is { } old && old != this) old._current = false;
        CurrentCamera = this;
        _current = true;
        _wasCurrent = false;
        FabricView.Follow(this);
        RenderEnvironment.CameraChanged(previous, this);
    }

    /// <summary>Stops being the current camera. With <paramref name="enableNext"/> the first other camera in the tree takes over (Godot's clear_current).</summary>
    public void ClearCurrent(bool enableNext = true)
    {
        _current = false;
        _wasCurrent = false;
        if (IsInsideTree() && OwningSubViewport() is { } sv) { sv.ReleaseCamera(this); return; }
        if (CurrentCamera != this) return;
        CurrentCamera = null;
        FabricView.Follow(null);
        RenderEnvironment.CameraChanged(this, null);
        if (enableNext) s_inTree.FirstOrDefault(c => c != this && c.IsInsideTree())?.MakeCurrent();
    }

    public bool IsCurrent() => _current;

    /// <summary>The environment drawn while this camera is current, instead of the world's. Not drawn for any other camera.</summary>
    public Environment? Environment
    {
        get => _environment;
        set { _environment = value; if (_current) RenderEnvironment.CameraChanged(this, this); }
    }

    /// <summary>The camera attributes in force while this camera is current, instead of the world's (exposure, depth of field, and the physical light units' exposure).</summary>
    public CameraAttributes? Attributes
    {
        get => _attributes;
        set { _attributes = value; if (_current) RenderEnvironment.CameraChanged(this, this); }
    }

    internal bool HasRenderOverrides => _environment != null || _attributes != null;

    // ---- projection (pure maths: the viewport size and the camera's transform) ----

    // Half the width and height of the view at distance 1 (perspective), at any distance (orthogonal) or at the near plane (frustum).
    private (float X, float Y) HalfExtents(Vector2 viewport)
    {
        float aspect = viewport.Y > 0 ? viewport.X / viewport.Y : 1f;
        if (Projection != ProjectionType.Perspective)
            return KeepAspect == KeepAspectEnum.Height ? (Size * 0.5f * aspect, Size * 0.5f) : (Size * 0.5f, Size * 0.5f / aspect);
        float half = MathF.Tan(Mathf.DegToRad(_fov) * 0.5f);
        return KeepAspect == KeepAspectEnum.Height ? (half * aspect, half) : (half, half / aspect);
    }

    // The camera's transform with its h_offset / v_offset slide applied (Godot slides the camera along its own X and Y).
    private Transform3D CameraXf()
    {
        Transform3D t = GlobalTransform.Orthonormalized();
        if (_hOffset != 0f || _vOffset != 0f) t.Origin += t.Basis.Y * _vOffset + t.Basis.X * _hOffset;
        return t;
    }

    private Vector2 ViewportSize() => GetViewport() is { } v ? new Vector2(v.Size.X, v.Size.Y) : new Vector2(1280, 720);

    // A screen point as -1..1 across and up.
    private static Vector2 ToNdc(Vector2 screen, Vector2 viewport) => new(screen.X / viewport.X * 2f - 1f, 1f - screen.Y / viewport.Y * 2f);

    /// <summary>Where the ray through a screen point starts, in world space (the camera's position, or on the view plane when orthogonal).</summary>
    public Vector3 ProjectRayOrigin(Vector2 screenPoint)
    {
        Transform3D t = CameraXf();
        if (Projection != ProjectionType.Orthogonal) return t.Origin;
        Vector2 vp = ViewportSize();
        Vector2 ndc = ToNdc(screenPoint, vp);
        (float hx, float hy) = HalfExtents(vp);
        return t * new Vector3(ndc.X * hx, ndc.Y * hy, -_near);
    }

    /// <summary>The direction of the ray through a screen point, in world space, normalised.</summary>
    public Vector3 ProjectRayNormal(Vector2 screenPoint)
    {
        Transform3D t = CameraXf();
        if (Projection == ProjectionType.Orthogonal) return (t.Basis * new Vector3(0, 0, -1)).Normalized();
        Vector2 vp = ViewportSize();
        Vector2 ndc = ToNdc(screenPoint, vp);
        (float hx, float hy) = HalfExtents(vp);
        if (Projection == ProjectionType.Frustum)   // the window is at the near plane, shifted by the frustum offset
            return (t.Basis * new Vector3(ndc.X * hx + _frustumOffset.X, ndc.Y * hy + _frustumOffset.Y, -_near)).Normalized();
        return (t.Basis * new Vector3(ndc.X * hx, ndc.Y * hy, -1f)).Normalized();
    }

    /// <summary>The world point at <paramref name="zDepth"/> metres in front of the camera that appears at a screen point.</summary>
    public Vector3 ProjectPosition(Vector2 screenPoint, float zDepth)
    {
        Vector2 vp = ViewportSize();
        Vector2 ndc = ToNdc(screenPoint, vp);
        (float hx, float hy) = HalfExtents(vp);
        if (Projection == ProjectionType.Frustum)
        {
            float k = zDepth / _near;
            return CameraXf() * new Vector3((ndc.X * hx + _frustumOffset.X) * k, (ndc.Y * hy + _frustumOffset.Y) * k, -zDepth);
        }
        float scale = Projection == ProjectionType.Orthogonal ? 1f : zDepth;
        return CameraXf() * new Vector3(ndc.X * hx * scale, ndc.Y * hy * scale, -zDepth);
    }

    /// <summary>The screen point a world point appears at. A point behind the camera comes out mirrored: check <see cref="IsPositionBehind"/>.</summary>
    public Vector2 UnprojectPosition(Vector3 worldPoint)
    {
        Vector2 vp = ViewportSize();
        Vector3 p = CameraXf().AffineInverse() * worldPoint;
        (float hx, float hy) = HalfExtents(vp);
        float ndcX, ndcY;
        if (Projection == ProjectionType.Orthogonal) { ndcX = p.X / hx; ndcY = p.Y / hy; }
        else if (Projection == ProjectionType.Frustum)
        {
            float depth = -p.Z;
            ndcX = (p.X / depth * _near - _frustumOffset.X) / hx;
            ndcY = (p.Y / depth * _near - _frustumOffset.Y) / hy;
        }
        else
        {
            float depth = -p.Z;
            ndcX = p.X / depth / hx;
            ndcY = p.Y / depth / hy;
        }
        return new Vector2((ndcX * 0.5f + 0.5f) * vp.X, (0.5f - ndcY * 0.5f) * vp.Y);
    }

    /// <summary>True when the world point is nearer the camera than its near plane along the view direction.</summary>
    public bool IsPositionBehind(Vector3 worldPoint)
    {
        Transform3D t = CameraXf();
        Vector3 forward = (t.Basis * new Vector3(0, 0, -1)).Normalized();
        return forward.Dot(worldPoint - t.Origin) < _near;
    }

    public bool IsPositionInFrustum(Vector3 worldPoint)
    {
        if (IsPositionBehind(worldPoint)) return false;
        Vector2 vp = ViewportSize();
        Vector2 s = UnprojectPosition(worldPoint);
        return s.X >= 0 && s.Y >= 0 && s.X <= vp.X && s.Y <= vp.Y;
    }

    /// <summary>The camera's transform, with h_offset and v_offset applied.</summary>
    public Transform3D GetCameraTransform() => CameraXf();

    public Projection GetCameraProjection()
    {
        Vector2 vp = ViewportSize();
        float aspect = vp.Y > 0 ? vp.X / vp.Y : 1f;
        if (Projection == ProjectionType.Orthogonal)
        {
            (float hx, float hy) = HalfExtents(vp);
            return Aver.Fabric.Projection.CreateOrthogonal(-hx, hx, -hy, hy, _near, _far);
        }
        if (Projection == ProjectionType.Frustum)
        {
            (float hx, float hy) = HalfExtents(vp);
            return Aver.Fabric.Projection.CreateFrustum(-hx + _frustumOffset.X, hx + _frustumOffset.X, -hy + _frustumOffset.Y, hy + _frustumOffset.Y, _near, _far);
        }
        return Aver.Fabric.Projection.CreatePerspective(_fov, aspect, _near, _far, KeepAspect == KeepAspectEnum.Width);
    }

    internal override void OnEnteringTree()
    {
        base.OnEnteringTree();
        ApplyCamera();
        if (OwningSubViewport() != null || s_inTree.Contains(this)) return;
        s_inTree.Add(this);
        // Godot: a camera that enters becomes current when it asks to be, or when it is the only camera in the tree.
        if (_current || _wasCurrent || s_inTree.Count == 1) MakeCurrent();
    }

    // CCamera::flags bits (Components.hpp).
    internal const int FlagKeepWidth = 1, FlagDrivesView = 2;

    /// <summary>What this camera's properties mean in engine terms. Pure: no engine needed. Lengths in centimetres; a perspective camera's size is unused.</summary>
    internal CameraFields ComputeFields() => new()
    {
        FovRad = Mathf.DegToRad(_fov),
        NearCm = FabricSpace.ToAverLength(_near),
        FarCm = FabricSpace.ToAverLength(_far),
        Projection = (int)_projection,
        Flags = FlagDrivesView | (_keepAspect == KeepAspectEnum.Width ? FlagKeepWidth : 0),
        SizeCm = FabricSpace.ToAverLength(_size),
        OffsetXCm = FabricSpace.ToAverLength(_frustumOffset.X),
        OffsetYCm = FabricSpace.ToAverLength(_frustumOffset.Y),
        HOffsetCm = FabricSpace.ToAverLength(_hOffset),
        VOffsetCm = FabricSpace.ToAverLength(_vOffset),
        CullInv = VisualInstance3D.StoreMask(_cullMask),
    };

    private void ApplyCamera()
    {
        if (!Self.IsValid || !NodeHost.Current.IsEngine) return;
        if (!Self.HasComponent(Component.Camera) && !Self.AddComponent(Component.Camera)) return;
        CameraFields f = ComputeFields();
        Self.SetFloat("CCamera.fovYRad", f.FovRad);
        Self.SetFloat("CCamera.nearCm", f.NearCm);
        Self.SetFloat("CCamera.farCm", f.FarCm);
        Self.SetInt("CCamera.projection", f.Projection);
        Self.SetInt("CCamera.flags", f.Flags);
        Self.SetFloat("CCamera.sizeCm", f.SizeCm);
        Self.SetFloat("CCamera.offsetXCm", f.OffsetXCm);
        Self.SetFloat("CCamera.offsetYCm", f.OffsetYCm);
        Self.SetFloat("CCamera.hOffsetCm", f.HOffsetCm);
        Self.SetFloat("CCamera.vOffsetCm", f.VOffsetCm);
        Self.SetInt("CCamera.cullInv", f.CullInv);
    }

    internal override void OnReadying()
    {
        base.OnReadying();
        if (_current) MakeCurrent();
        if (ViewportCurrent && OwningSubViewport() is { } sv) { _current = true; sv.SetActiveCamera(this); }
    }

    internal override void OnExitingTree()
    {
        if (OwningSubViewport() is { } sv) sv.ReleaseCamera(this);
        else
        {
            s_inTree.Remove(this);
            if (CurrentCamera == this)
            {
                _wasCurrent = true;
                _current = false;
                CurrentCamera = null;
                FabricView.Follow(null);
                RenderEnvironment.CameraChanged(this, null);
                s_inTree.FirstOrDefault(c => c.IsInsideTree())?.MakeCurrent();
            }
        }
        base.OnExitingTree();
    }
}

/// <summary>A shape for the collision node above it (Godot's CollisionShape3D). The parent builds its body from these children
/// when it becomes ready.</summary>
public class CollisionShape3D : Node3D
{
    private Shape3D? _shape;
    private bool _disabled;

    public Shape3D? Shape { get => _shape; set { _shape = value; (GetParent() as CollisionObject3D)?.QueueRebuild(); } }

    /// <summary>A disabled shape is not part of its parent's body. Changing it after the parent is ready makes the body again.</summary>
    public bool Disabled { get => _disabled; set { _disabled = value; (GetParent() as CollisionObject3D)?.QueueRebuild(); } }

    /// <summary>The colour the shape is drawn in when the tree shows collisions (SceneTree.DebugCollisionsHint); (0, 0, 0, 0) takes the engine's own by body kind.</summary>
    public Color DebugColor { get; set; } = new(0, 0, 0, 0);

    /// <summary>Whether the debug drawing fills the shape. Stored: the drawing is the outline.</summary>
    public bool DebugFill { get; set; } = true;
}

/// <summary>A polygon in the node's XY plane extruded along Z, centred, as one collision shape (Godot's CollisionPolygon3D): a convex polygon makes a convex shape,
/// any other a triangle mesh. Set <see cref="Polygon"/> and <see cref="Depth"/>; the shape is made again when either changes.</summary>
public class CollisionPolygon3D : CollisionShape3D
{
    private Vector2[] _polygon = System.Array.Empty<Vector2>();
    private float _depth = 1f;

    public Vector2[] Polygon { get => _polygon; set { _polygon = value ?? System.Array.Empty<Vector2>(); Rebuild(); } }
    public float Depth { get => _depth; set { _depth = value; Rebuild(); } }
    public float Margin { get; set; } = 0.04f;

    private void Rebuild() => Shape = _polygon.Length < 3 ? null : Extrude(_polygon, _depth);

    internal static Shape3D? Extrude(Vector2[] polygon, float depth)
    {
        float h = depth * 0.5f;
        int n = polygon.Length;
        float area = 0f;
        bool convex = true;
        for (int i = 0; i < n; i++)
        {
            Vector2 a = polygon[i], b = polygon[(i + 1) % n], c = polygon[(i + 2) % n];
            area += a.Cross(b);
        }
        float sign = area >= 0f ? 1f : -1f;
        for (int i = 0; i < n && convex; i++)
        {
            Vector2 a = polygon[i], b = polygon[(i + 1) % n], c = polygon[(i + 2) % n];
            if ((b - a).Cross(c - b) * sign < -1e-6f) convex = false;
        }
        if (convex)
        {
            var pts = new Vector3[n * 2];
            for (int i = 0; i < n; i++) { pts[i] = new Vector3(polygon[i].X, polygon[i].Y, h); pts[n + i] = new Vector3(polygon[i].X, polygon[i].Y, -h); }
            return new ConvexPolygonShape3D { Points = pts };
        }
        int[] tris = Geometry2D.TriangulatePolygon(polygon);
        if (tris.Length == 0) return null;
        var faces = new List<Vector3>();
        for (int t = 0; t + 2 < tris.Length; t += 3)
        {
            Vector2 a = polygon[tris[t]], b = polygon[tris[t + 1]], c = polygon[tris[t + 2]];
            faces.AddRange(new[] { new Vector3(a.X, a.Y, h), new Vector3(b.X, b.Y, h), new Vector3(c.X, c.Y, h) });
            faces.AddRange(new[] { new Vector3(a.X, a.Y, -h), new Vector3(c.X, c.Y, -h), new Vector3(b.X, b.Y, -h) });
        }
        for (int i = 0; i < n; i++)
        {
            Vector2 a = polygon[i], b = polygon[(i + 1) % n];
            var a0 = new Vector3(a.X, a.Y, h); var a1 = new Vector3(a.X, a.Y, -h);
            var b0 = new Vector3(b.X, b.Y, h); var b1 = new Vector3(b.X, b.Y, -h);
            faces.AddRange(new[] { a0, b0, b1, a0, b1, a1 });
        }
        return new ConcavePolygonShape3D { Data = faces.ToArray() };
    }
}
