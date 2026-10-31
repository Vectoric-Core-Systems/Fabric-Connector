// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Godot's primitive meshes (PrimitiveMesh and BoxMesh, SphereMesh, CapsuleMesh, CylinderMesh, PlaneMesh, QuadMesh, PrismMesh, TorusMesh) made at run time.
// A converted level names them by an .ocmesh asset (the converter's own generators, native/tools/fabric/FabricMeshGen.cpp); a mesh a script makes with `new
// CapsuleMesh()` has no asset, so it builds its triangles here in Godot's space and hands them to the runtime mesh store like an ArrayMesh, redone when a
// property changes. The two generators are separate code and agree on size, subdivision and winding, not on every vertex order. Godot's front faces run clockwise
// seen from outside, so a triangle is wound against its outward normal. Tangents are not made; UVs follow Godot's layout for each shape except the box, whose six
// faces use one cell of a 3 by 2 sheet each.

namespace Aver.Fabric;

/// <summary>Base of the generated meshes (Godot's PrimitiveMesh): one triangle surface, built on demand.</summary>
public abstract class PrimitiveMesh : Mesh
{
    private MeshSurface? _surface;
    private readonly EngineMeshLink _link = new();
    private Material? _material;
    private bool _flipFaces;

    /// <summary>The material of the one surface.</summary>
    public Material? Material { get => _material; set { _material = value; Invalidate(); } }

    /// <summary>Turns every triangle inside out.</summary>
    public bool FlipFaces { get => _flipFaces; set { _flipFaces = value; Invalidate(); } }

    /// <summary>Makes the triangles again after a property changed.</summary>
    protected void Invalidate()
    {
        _surface = null;
        if (_link.Key != null) _link.Reset(new List<MeshSurface> { Surface() });
        EmitChanged();
    }

    internal MeshSurface Surface()
    {
        if (_surface != null) return _surface;
        var b = new PrimBuilder();
        Build(b);
        MeshSurface s = b.ToSurface(_flipFaces);
        s.Material = _material;
        _surface = s;
        return s;
    }

    /// <summary>Adds the shape's vertices and triangles to the builder, in metres.</summary>
    protected abstract void Build(PrimBuilder b);

    public override int GetSurfaceCount() => 1;
    public override Material? SurfaceGetMaterial(int surfIdx) => surfIdx == 0 ? _material : null;
    public override void SurfaceSetMaterial(int surfIdx, Material? material) { if (surfIdx == 0) Material = material; }
    public override Aabb GetAabb() => Surface().Bounds();

    /// <summary>The surface's arrays in Godot's layout (vertex, normal, uv, index).</summary>
    public Collections.Array GetMeshArrays() => Surface().ToArrays();

    /// <summary>Every triangle as three points, in order (Godot's get_faces).</summary>
    public override Vector3[] GetFaces()
    {
        MeshSurface s = Surface();
        int n = s.Indices?.Length ?? 0;
        var faces = new Vector3[n];
        for (int i = 0; i < n; i++) faces[i] = s.Vertices[s.Indices![i]];
        return faces;
    }

    internal override string? EngineAssetPath => ResourcePath.Length > 0 ? null : _link.Ensure("primitive", GetInstanceId(), new List<MeshSurface> { Surface() });
}

/// <summary>Collects the vertices and triangles of a generated mesh. Triangles are wound against the vertices' normals.</summary>
public sealed class PrimBuilder
{
    internal readonly List<Vector3> P = new(), N = new();
    internal readonly List<Vector2> Uv = new();
    internal readonly List<int> I = new();

    public int Vertex(Vector3 p, Vector3 n, Vector2 uv)
    {
        P.Add(p); N.Add(n); Uv.Add(uv);
        return P.Count - 1;
    }

    public void Tri(int a, int b, int c)
    {
        Vector3 geometric = (P[b] - P[a]).Cross(P[c] - P[a]);
        // Front faces are clockwise in Godot: the counter-clockwise normal points inward.
        if (geometric.Dot(N[a] + N[b] + N[c]) > 0f) (b, c) = (c, b);
        I.Add(a); I.Add(b); I.Add(c);
    }

    public void Quad(int a, int b, int c, int d) { Tri(a, b, c); Tri(a, c, d); }

    /// <summary>A flat grid of (su+1) by (sv+1) vertices from <paramref name="origin"/> along two edges, facing <paramref name="normal"/>, with UVs spanning the given rectangle.</summary>
    public void Face(Vector3 origin, Vector3 uEdge, Vector3 vEdge, Vector3 normal, int su, int sv, Vector2 uv0, Vector2 uv1)
    {
        su = Math.Max(su, 1); sv = Math.Max(sv, 1);
        int baseIdx = P.Count;
        for (int j = 0; j <= sv; j++)
            for (int i = 0; i <= su; i++)
            {
                float fu = (float)i / su, fv = (float)j / sv;
                Vertex(origin + uEdge * fu + vEdge * fv, normal, new Vector2(uv0.X + (uv1.X - uv0.X) * fu, uv0.Y + (uv1.Y - uv0.Y) * fv));
            }
        for (int j = 0; j < sv; j++)
            for (int i = 0; i < su; i++)
            {
                int a = baseIdx + j * (su + 1) + i, b = a + 1, c = b + (su + 1), d = a + (su + 1);
                Quad(a, b, c, d);
            }
    }

    internal MeshSurface ToSurface(bool flip)
    {
        int[] idx = I.ToArray();
        if (flip) for (int i = 0; i + 2 < idx.Length; i += 3) (idx[i + 1], idx[i + 2]) = (idx[i + 2], idx[i + 1]);
        Vector3[] n = N.ToArray();
        if (flip) for (int i = 0; i < n.Length; i++) n[i] = -n[i];
        return new MeshSurface { Primitive = Mesh.PrimitiveType.Triangles, Vertices = P.ToArray(), Normals = n, Uv = Uv.ToArray(), Indices = idx };
    }
}

/// <summary>A box mesh (Godot's BoxMesh), size in metres.</summary>
public class BoxMesh : PrimitiveMesh
{
    private Vector3 _size = Vector3.One;
    private int _subW, _subH, _subD;
    public Vector3 Size { get => _size; set { _size = value; Invalidate(); } }
    public int SubdivideWidth { get => _subW; set { _subW = Math.Max(value, 0); Invalidate(); } }
    public int SubdivideHeight { get => _subH; set { _subH = Math.Max(value, 0); Invalidate(); } }
    public int SubdivideDepth { get => _subD; set { _subD = Math.Max(value, 0); Invalidate(); } }

    protected override void Build(PrimBuilder b)
    {
        Vector3 h = _size * 0.5f;
        int w = _subW + 1, hh = _subH + 1, d = _subD + 1;
        const float t = 1f / 3f, s = 0.5f;
        // +Z, -Z, -X, +X, +Y, -Y in the cells of a 3 by 2 sheet.
        b.Face(new Vector3(-h.X, -h.Y, h.Z), new Vector3(_size.X, 0, 0), new Vector3(0, _size.Y, 0), Vector3.Back, w, hh, new Vector2(0, s), new Vector2(t, 0));
        b.Face(new Vector3(h.X, -h.Y, -h.Z), new Vector3(-_size.X, 0, 0), new Vector3(0, _size.Y, 0), Vector3.Forward, w, hh, new Vector2(t, s), new Vector2(2 * t, 0));
        b.Face(new Vector3(-h.X, -h.Y, -h.Z), new Vector3(0, 0, _size.Z), new Vector3(0, _size.Y, 0), Vector3.Left, d, hh, new Vector2(2 * t, s), new Vector2(1, 0));
        b.Face(new Vector3(h.X, -h.Y, h.Z), new Vector3(0, 0, -_size.Z), new Vector3(0, _size.Y, 0), Vector3.Right, d, hh, new Vector2(0, 1), new Vector2(t, s));
        b.Face(new Vector3(-h.X, h.Y, h.Z), new Vector3(_size.X, 0, 0), new Vector3(0, 0, -_size.Z), Vector3.Up, w, d, new Vector2(t, 1), new Vector2(2 * t, s));
        b.Face(new Vector3(-h.X, -h.Y, -h.Z), new Vector3(_size.X, 0, 0), new Vector3(0, 0, _size.Z), Vector3.Down, w, d, new Vector2(2 * t, 1), new Vector2(1, s));
    }
}

/// <summary>A flat rectangle in the XZ plane facing up by default (Godot's PlaneMesh).</summary>
public class PlaneMesh : PrimitiveMesh
{
    public enum OrientationEnum { FaceX = 0, FaceY = 1, FaceZ = 2 }
    private Vector2 _size = new(2, 2);
    private int _subW, _subD;
    private Vector3 _offset;
    private OrientationEnum _orientation = OrientationEnum.FaceY;
    public Vector2 Size { get => _size; set { _size = value; Invalidate(); } }
    public int SubdivideWidth { get => _subW; set { _subW = Math.Max(value, 0); Invalidate(); } }
    public int SubdivideDepth { get => _subD; set { _subD = Math.Max(value, 0); Invalidate(); } }
    public Vector3 CenterOffset { get => _offset; set { _offset = value; Invalidate(); } }
    public OrientationEnum Orientation { get => _orientation; set { _orientation = value; Invalidate(); } }

    protected override void Build(PrimBuilder b)
    {
        float hx = _size.X * 0.5f, hy = _size.Y * 0.5f;
        switch (_orientation)
        {
            case OrientationEnum.FaceX:
                b.Face(_offset + new Vector3(0, hx, hy), new Vector3(0, -_size.X, 0), new Vector3(0, 0, -_size.Y), Vector3.Right, _subW + 1, _subD + 1, new Vector2(0, 0), new Vector2(1, 1));
                break;
            case OrientationEnum.FaceZ:
                b.Face(_offset + new Vector3(-hx, hy, 0), new Vector3(_size.X, 0, 0), new Vector3(0, -_size.Y, 0), Vector3.Back, _subW + 1, _subD + 1, new Vector2(0, 0), new Vector2(1, 1));
                break;
            default:
                // Godot's vertex order, found by dumping its own PlaneMesh: the first vertex is at +X +Z, X falls along a row and Z falls from row to row, and the
                // UV falls from (1, 1) the same way. A pinned point of a cloth is such an index.
                b.Face(_offset + new Vector3(hx, 0, hy), new Vector3(-_size.X, 0, 0), new Vector3(0, 0, -_size.Y), Vector3.Up, _subW + 1, _subD + 1, new Vector2(1, 1), new Vector2(0, 0));
                break;
        }
    }
}

/// <summary>A flat rectangle in the XY plane facing +Z by default (Godot's QuadMesh, a PlaneMesh that faces the viewer).</summary>
public class QuadMesh : PlaneMesh
{
    public QuadMesh()
    {
        Size = new Vector2(1, 1);
        Orientation = OrientationEnum.FaceZ;
    }
}

/// <summary>A sphere or a hemisphere (Godot's SphereMesh).</summary>
public class SphereMesh : PrimitiveMesh
{
    private float _radius = 0.5f, _height = 1f;
    private int _radial = 64, _rings = 32;
    private bool _hemisphere;
    public float Radius { get => _radius; set { _radius = value; Invalidate(); } }
    public float Height { get => _height; set { _height = value; Invalidate(); } }
    public int RadialSegments { get => _radial; set { _radial = Math.Max(value, 3); Invalidate(); } }
    public int Rings { get => _rings; set { _rings = Math.Max(value, 2); Invalidate(); } }
    public bool IsHemisphere { get => _hemisphere; set { _hemisphere = value; Invalidate(); } }

    protected override void Build(PrimBuilder b)
    {
        // Godot's layout, found by dumping its own SphereMesh: rings + 2 rows of radial_segments + 1 vertices, the first row the pole, the angle from the pole
        // i / (rings + 1) of a half turn, and the angle round the axis starting at +Z and turning toward +X.
        float ry = _hemisphere ? _height : _height * 0.5f;
        int baseIdx = 0, rows = _rings + 1;
        for (int i = 0; i <= rows; i++)
        {
            float v = (float)i / rows, w = MathF.Sin(MathF.PI * v), y = MathF.Cos(MathF.PI * v);
            bool flat = _hemisphere && y < 0f;
            if (flat) { y = 0f; w = MathF.Min(1f, w); }
            for (int j = 0; j <= _radial; j++)
            {
                float u = (float)j / _radial, x = MathF.Sin(MathF.Tau * u), z = MathF.Cos(MathF.Tau * u);
                var p = new Vector3(x * _radius * w, y * ry, z * _radius * w);
                Vector3 n = flat ? Vector3.Down : new Vector3(x * w / MathF.Max(_radius, 1e-6f), y / MathF.Max(ry, 1e-6f), z * w / MathF.Max(_radius, 1e-6f)).Normalized();
                b.Vertex(p, n, new Vector2(u, v));
            }
        }
        for (int i = 0; i < rows; i++)
            for (int j = 0; j < _radial; j++)
            {
                int a = baseIdx + i * (_radial + 1) + j, bq = a + 1, c = bq + _radial + 1, d = a + _radial + 1;
                if (i != 0) b.Tri(a, bq, c);
                if (i != rows - 1) b.Tri(a, c, d);
            }
    }
}

/// <summary>A cylinder with its own top and bottom radius (Godot's CylinderMesh; a cone when one is 0).</summary>
public class CylinderMesh : PrimitiveMesh
{
    private float _top = 0.5f, _bottom = 0.5f, _height = 2f;
    private int _radial = 64, _rings = 4;
    private bool _capTop = true, _capBottom = true;
    public float TopRadius { get => _top; set { _top = value; Invalidate(); } }
    public float BottomRadius { get => _bottom; set { _bottom = value; Invalidate(); } }
    public float Height { get => _height; set { _height = value; Invalidate(); } }
    public int RadialSegments { get => _radial; set { _radial = Math.Max(value, 3); Invalidate(); } }
    public int Rings { get => _rings; set { _rings = Math.Max(value, 1); Invalidate(); } }
    public bool CapTop { get => _capTop; set { _capTop = value; Invalidate(); } }
    public bool CapBottom { get => _capBottom; set { _capBottom = value; Invalidate(); } }

    protected override void Build(PrimBuilder b)
    {
        float h = _height * 0.5f;
        float slope = _height > 1e-6f ? (_bottom - _top) / _height : 0f;
        int first = b.P.Count;
        for (int i = 0; i <= _rings; i++)
        {
            float v = (float)i / _rings, r = _top + (_bottom - _top) * v, y = h - _height * v;
            for (int j = 0; j <= _radial; j++)
            {
                float u = (float)j / _radial, x = MathF.Sin(MathF.Tau * u), z = MathF.Cos(MathF.Tau * u);
                b.Vertex(new Vector3(x * r, y, z * r), new Vector3(x, slope, z).Normalized(), new Vector2(u, v * 0.5f));
            }
        }
        for (int i = 0; i < _rings; i++)
            for (int j = 0; j < _radial; j++)
            {
                int a = first + i * (_radial + 1) + j, bq = a + 1, c = bq + _radial + 1, d = a + _radial + 1;
                b.Quad(a, bq, c, d);
            }
        Cap(b, _capTop, h, _top, Vector3.Up);
        Cap(b, _capBottom, -h, _bottom, Vector3.Down);
    }

    private void Cap(PrimBuilder b, bool on, float y, float r, Vector3 normal)
    {
        if (!on || r <= 0f) return;
        int centre = b.Vertex(new Vector3(0, y, 0), normal, new Vector2(0.5f, 0.75f));
        int first = b.P.Count;
        for (int j = 0; j <= _radial; j++)
        {
            float u = (float)j / _radial, x = MathF.Sin(MathF.Tau * u), z = MathF.Cos(MathF.Tau * u);
            b.Vertex(new Vector3(x * r, y, z * r), normal, new Vector2(0.5f + x * 0.5f, 0.75f + z * 0.25f));
        }
        for (int j = 0; j < _radial; j++) b.Tri(centre, first + j, first + j + 1);
    }
}

/// <summary>A cylinder with a hemisphere on each end (Godot's CapsuleMesh); the height is the whole length.</summary>
public class CapsuleMesh : PrimitiveMesh
{
    private float _radius = 0.5f, _height = 2f;
    private int _radial = 64, _rings = 8;
    public float Radius { get => _radius; set { _radius = value; Invalidate(); } }
    public float Height { get => _height; set { _height = value; Invalidate(); } }
    public int RadialSegments { get => _radial; set { _radial = Math.Max(value, 3); Invalidate(); } }
    public int Rings { get => _rings; set { _rings = Math.Max(value, 1); Invalidate(); } }

    protected override void Build(PrimBuilder b)
    {
        float straight = MathF.Max(_height - 2f * _radius, 0f) * 0.5f;
        int rows = 0;
        int first = b.P.Count;
        // Top hemisphere from the pole down to the equator, then the bottom one from the equator to the pole.
        void Row(float lat, float yOffset, float v)
        {
            float cy = MathF.Sin(lat), cr = MathF.Cos(lat);
            for (int j = 0; j <= _radial; j++)
            {
                float u = (float)j / _radial, x = MathF.Sin(MathF.Tau * u), z = MathF.Cos(MathF.Tau * u);
                b.Vertex(new Vector3(x * cr * _radius, cy * _radius + yOffset, z * cr * _radius), new Vector3(x * cr, cy, z * cr), new Vector2(u, v));
            }
            rows++;
        }
        for (int i = 0; i <= _rings; i++) Row(MathF.PI * 0.5f * (1f - (float)i / _rings), straight, 0.25f * i / _rings);
        for (int i = 0; i <= _rings; i++) Row(-MathF.PI * 0.5f * (float)i / _rings, -straight, 0.75f + 0.25f * i / _rings);
        for (int i = 0; i + 1 < rows; i++)
            for (int j = 0; j < _radial; j++)
            {
                int a = first + i * (_radial + 1) + j, bq = a + 1, c = bq + _radial + 1, d = a + _radial + 1;
                b.Quad(a, bq, c, d);
            }
    }
}

/// <summary>A triangular prism (Godot's PrismMesh): the apex sits <see cref="LeftToRight"/> of the width from the left edge.</summary>
public class PrismMesh : PrimitiveMesh
{
    private float _leftToRight = 0.5f;
    private Vector3 _size = Vector3.One;
    public float LeftToRight { get => _leftToRight; set { _leftToRight = value; Invalidate(); } }
    public Vector3 Size { get => _size; set { _size = value; Invalidate(); } }
    public int SubdivideWidth { get; set; }
    public int SubdivideHeight { get; set; }
    public int SubdivideDepth { get; set; }

    protected override void Build(PrimBuilder b)
    {
        Vector3 h = _size * 0.5f;
        Vector3 l = new(-h.X, -h.Y, 0), r = new(h.X, -h.Y, 0), apex = new(-h.X + _size.X * _leftToRight, h.Y, 0);
        Vector3 zOff = new(0, 0, h.Z);
        // The two triangular ends.
        void End(float z, Vector3 normal)
        {
            int a = b.Vertex(new Vector3(l.X, l.Y, z), normal, new Vector2(0, 1)), c = b.Vertex(new Vector3(r.X, r.Y, z), normal, new Vector2(1, 1)), d = b.Vertex(new Vector3(apex.X, apex.Y, z), normal, new Vector2(_leftToRight, 0));
            b.Tri(a, c, d);
        }
        End(h.Z, Vector3.Back);
        End(-h.Z, Vector3.Forward);
        // The bottom and the two slopes.
        b.Face(new Vector3(l.X, l.Y, -h.Z), new Vector3(_size.X, 0, 0), new Vector3(0, 0, _size.Z), Vector3.Down, 1, 1, new Vector2(0, 0), new Vector2(1, 1));
        Vector3 nl = new Vector3(-_size.Y, apex.X - l.X, 0).Normalized(), nr = new Vector3(_size.Y, r.X - apex.X, 0).Normalized();
        b.Face(new Vector3(l.X, l.Y, h.Z), apex - l, -zOff * 2f, nl, 1, 1, new Vector2(0, 0), new Vector2(1, 1));
        b.Face(new Vector3(apex.X, apex.Y, h.Z), r - apex, -zOff * 2f, nr, 1, 1, new Vector2(0, 0), new Vector2(1, 1));
    }
}

/// <summary>A torus (Godot's TorusMesh): <see cref="Rings"/> around the axis, <see cref="RingSegments"/> around the tube.</summary>
public class TorusMesh : PrimitiveMesh
{
    private float _inner = 0.5f, _outer = 1f;
    private int _rings = 64, _segments = 32;
    public float InnerRadius { get => _inner; set { _inner = value; Invalidate(); } }
    public float OuterRadius { get => _outer; set { _outer = value; Invalidate(); } }
    public int Rings { get => _rings; set { _rings = Math.Max(value, 3); Invalidate(); } }
    public int RingSegments { get => _segments; set { _segments = Math.Max(value, 3); Invalidate(); } }

    protected override void Build(PrimBuilder b)
    {
        float major = (_inner + _outer) * 0.5f, minor = MathF.Abs(_outer - _inner) * 0.5f;
        for (int i = 0; i <= _rings; i++)
        {
            float u = (float)i / _rings, a = MathF.Tau * u;
            Vector3 radial = new(MathF.Sin(a), 0, MathF.Cos(a));
            for (int j = 0; j <= _segments; j++)
            {
                float v = (float)j / _segments, c = MathF.Tau * v;
                Vector3 n = radial * MathF.Cos(c) + Vector3.Up * MathF.Sin(c);
                b.Vertex(radial * major + n * minor, n, new Vector2(u, v));
            }
        }
        for (int i = 0; i < _rings; i++)
            for (int j = 0; j < _segments; j++)
            {
                int a = i * (_segments + 1) + j, bq = a + 1, c = bq + _segments + 1, d = a + _segments + 1;
                b.Quad(a, bq, c, d);
            }
    }
}
