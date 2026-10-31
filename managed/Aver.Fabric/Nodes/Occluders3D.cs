// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Godot's occluder classes (OccluderInstance3D and the Occluder3D resources). Aver culls with a depth hierarchy built from what it draws, so an author's
// occluder shapes are not needed: they are accepted, kept and read back as set, and nothing is drawn or culled differently.

namespace Aver.Fabric;

/// <summary>A shape that hides what is behind it from the renderer (Godot's Occluder3D). Kept and ignored: occlusion culling is automatic.</summary>
public class Occluder3D : Resource
{
    /// <summary>The corners of the occluder in its own space; empty for a shape that has none yet.</summary>
    public virtual Vector3[] GetVertices() => System.Array.Empty<Vector3>();

    /// <summary>Triangles of <see cref="GetVertices"/>, three indices each.</summary>
    public virtual int[] GetIndices() => System.Array.Empty<int>();
}

/// <summary>An occluder given as triangles (Godot's ArrayOccluder3D).</summary>
public class ArrayOccluder3D : Occluder3D
{
    private Vector3[] _vertices = System.Array.Empty<Vector3>();
    private int[] _indices = System.Array.Empty<int>();

    /// <summary>The corners; setting them keeps the indices.</summary>
    public Vector3[] Vertices { get => _vertices; set { _vertices = value ?? System.Array.Empty<Vector3>(); EmitChanged(); } }

    /// <summary>Three indices per triangle.</summary>
    public int[] Indices { get => _indices; set { _indices = value ?? System.Array.Empty<int>(); EmitChanged(); } }

    /// <summary>Sets the corners and the triangles together (Godot's set_arrays).</summary>
    public void SetArrays(Vector3[] vertices, int[] indices)
    {
        _vertices = vertices ?? System.Array.Empty<Vector3>();
        _indices = indices ?? System.Array.Empty<int>();
        EmitChanged();
    }

    public override Vector3[] GetVertices() => _vertices;
    public override int[] GetIndices() => _indices;
}

/// <summary>A box occluder (Godot's BoxOccluder3D), centred on the origin.</summary>
public class BoxOccluder3D : Occluder3D
{
    private Vector3 _size = Vector3.One;

    public Vector3 Size { get => _size; set { _size = value; EmitChanged(); } }

    public override Vector3[] GetVertices()
    {
        Vector3 h = _size * 0.5f;
        return new[]
        {
            new Vector3(-h.X, -h.Y, -h.Z), new Vector3(h.X, -h.Y, -h.Z), new Vector3(h.X, h.Y, -h.Z), new Vector3(-h.X, h.Y, -h.Z),
            new Vector3(-h.X, -h.Y, h.Z), new Vector3(h.X, -h.Y, h.Z), new Vector3(h.X, h.Y, h.Z), new Vector3(-h.X, h.Y, h.Z),
        };
    }

    public override int[] GetIndices() => new[] { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 3, 7, 6, 3, 6, 2, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5 };
}

/// <summary>A flat rectangle occluder facing +Z (Godot's QuadOccluder3D).</summary>
public class QuadOccluder3D : Occluder3D
{
    private Vector2 _size = Vector2.One;

    public Vector2 Size { get => _size; set { _size = value; EmitChanged(); } }

    public override Vector3[] GetVertices()
    {
        Vector2 h = _size * 0.5f;
        return new[] { new Vector3(-h.X, -h.Y, 0f), new Vector3(h.X, -h.Y, 0f), new Vector3(h.X, h.Y, 0f), new Vector3(-h.X, h.Y, 0f) };
    }

    public override int[] GetIndices() => new[] { 0, 1, 2, 0, 2, 3 };
}

/// <summary>A sphere occluder (Godot's SphereOccluder3D). Its radius is kept; the shape is not tessellated here, so it reports no corners.</summary>
public class SphereOccluder3D : Occluder3D
{
    private float _radius = 1f;

    public float Radius { get => _radius; set { _radius = value; EmitChanged(); } }
}

/// <summary>A flat occluder outlined by a 2D polygon in the XY plane (Godot's PolygonOccluder3D).</summary>
public class PolygonOccluder3D : Occluder3D
{
    private Vector2[] _polygon = System.Array.Empty<Vector2>();

    /// <summary>The outline, in order.</summary>
    public Vector2[] Polygon { get => _polygon; set { _polygon = value ?? System.Array.Empty<Vector2>(); EmitChanged(); } }

    public override Vector3[] GetVertices() => _polygon.Select(p => new Vector3(p.X, p.Y, 0f)).ToArray();

    /// <summary>A fan from the first corner: right for a convex outline, an approximation for a concave one.</summary>
    public override int[] GetIndices()
    {
        var indices = new List<int>();
        for (int i = 1; i + 1 < _polygon.Length; i++) { indices.Add(0); indices.Add(i); indices.Add(i + 1); }
        return indices.ToArray();
    }
}

/// <summary>A node that places an <see cref="Occluder3D"/> in the world (Godot's OccluderInstance3D). Kept and ignored: Aver's occlusion culling needs no hints.</summary>
public class OccluderInstance3D : VisualInstance3D
{
    /// <summary>The shape; setting it changes nothing that is drawn.</summary>
    public Occluder3D? Occluder { get; set; }

    /// <summary>The layers whose geometry would be baked into an occluder in the editor. Stored; nothing is baked here.</summary>
    public uint BakeMask { get; set; } = 0xFFFFFFFFu;

    /// <summary>How far the baked occluder may deviate from the geometry it came from. Stored; nothing is baked here.</summary>
    public float BakeSimplificationDistance { get; set; } = 0.1f;

    public bool GetBakeMaskValue(int layerNumber) => (BakeMask & (1u << (layerNumber - 1))) != 0;

    public void SetBakeMaskValue(int layerNumber, bool value) =>
        BakeMask = value ? BakeMask | (1u << (layerNumber - 1)) : BakeMask & ~(1u << (layerNumber - 1));
}
