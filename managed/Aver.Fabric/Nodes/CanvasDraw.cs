// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// The shapes of CanvasItem's draw API that are not a rectangle (parity wave 8a, F56 and F58): polygons, thick lines and polylines, arcs, circles, primitives and
// the radial fill of a TextureProgressBar. Each is turned into triangles here, in plain arithmetic a test can check, and the GUI hands them to the engine
// through <c>aver_ui_mesh</c>. The tessellation is this repository's: the engine knows triangles, not a canvas.

namespace Aver.Fabric;

/// <summary>Triangles for the engine's UI draw list: x, y, u, v and a straight 0xAABBGGRR colour per vertex.</summary>
internal sealed class CanvasMesh
{
    public float[] Xyuv = System.Array.Empty<float>();
    public uint[] Colors = System.Array.Empty<uint>();
    public uint[] Indices = System.Array.Empty<uint>();
    public ulong Texture;
    public int VertexCount => Colors.Length;
    public int TriangleCount => Indices.Length / 3;

    /// <summary>The same triangles moved by a transform (the draw command stores them in the item's space).</summary>
    public CanvasMesh Transformed(Transform2D t)
    {
        var m = new CanvasMesh { Xyuv = (float[])Xyuv.Clone(), Colors = Colors, Indices = Indices, Texture = Texture };
        for (int i = 0; i < m.VertexCount; i++)
        {
            var p = t * new Vector2(m.Xyuv[i * 4], m.Xyuv[i * 4 + 1]);
            m.Xyuv[i * 4] = p.X;
            m.Xyuv[i * 4 + 1] = p.Y;
        }
        return m;
    }
}

/// <summary>Builds shapes as triangles.</summary>
internal static class CanvasTess
{
    /// <summary>The most vertices one shape may produce; a request for more is cut, never grown without bound.</summary>
    public const int MaxVertices = 16384;

    private sealed class Builder
    {
        private readonly List<float> _xyuv = new();
        private readonly List<uint> _col = new();
        private readonly List<uint> _idx = new();
        public int Count => _col.Count;

        public int Vertex(Vector2 p, Vector2 uv, uint c)
        {
            _xyuv.Add(p.X); _xyuv.Add(p.Y); _xyuv.Add(uv.X); _xyuv.Add(uv.Y);
            _col.Add(c);
            return _col.Count - 1;
        }

        public bool Tri(int a, int b, int c)
        {
            if (_col.Count > MaxVertices) return false;
            _idx.Add((uint)a); _idx.Add((uint)b); _idx.Add((uint)c);
            return true;
        }

        public CanvasMesh? ToMesh(ulong texture) => _idx.Count < 3 ? null : new CanvasMesh { Xyuv = _xyuv.ToArray(), Colors = _col.ToArray(), Indices = _idx.ToArray(), Texture = texture };
    }

    private static uint Pack(Color c) => StyleBoxFlat.Pack(c);

    private static Color ColorAt(Color[] colors, int i) => colors.Length == 0 ? new Color(1, 1, 1, 1) : colors[Math.Min(i, colors.Length - 1)];

    private static bool Finite(Vector2 v) => float.IsFinite(v.X) && float.IsFinite(v.Y);

    /// <summary>A filled polygon, triangulated by ear clipping. One colour per point (or one colour for all) and optional texture coordinates. Null when the polygon
    /// has fewer than three points, a non-finite point, or cannot be triangulated.</summary>
    public static CanvasMesh? Polygon(Vector2[] points, Color[] colors, Vector2[]? uvs = null, ulong texture = 0)
    {
        if (points.Length < 3 || points.Length > MaxVertices) return null;
        foreach (Vector2 p in points) if (!Finite(p)) return null;
        int[] tris = Geometry2D.TriangulatePolygon(points);
        if (tris.Length < 3) return null;
        var b = new Builder();
        for (int i = 0; i < points.Length; i++)
            b.Vertex(points[i], uvs != null && i < uvs.Length ? uvs[i] : Vector2.Zero, Pack(ColorAt(colors, i)));
        for (int i = 0; i + 2 < tris.Length; i += 3) b.Tri(tris[i], tris[i + 1], tris[i + 2]);
        return b.ToMesh(texture);
    }

    /// <summary>A segment of the given width as a quad (Godot's draw_line). A width below one is one pixel.</summary>
    public static CanvasMesh? Line(Vector2 from, Vector2 to, Color color, float width)
    {
        var b = new Builder();
        AddSegment(b, from, to, Pack(color), Pack(color), width < 0f ? 1f : Math.Max(width, 0.0001f));
        return b.ToMesh(0);
    }

    private static bool AddSegment(Builder b, Vector2 a, Vector2 c, uint ca, uint cb, float width)
    {
        if (!Finite(a) || !Finite(c)) return false;
        Vector2 d = c - a;
        float len = d.Length();
        if (len < 1e-6f) return false;
        Vector2 n = new Vector2(-d.Y, d.X) / len * (width * 0.5f);
        int i0 = b.Vertex(a + n, Vector2.Zero, ca), i1 = b.Vertex(a - n, Vector2.Zero, ca), i2 = b.Vertex(c - n, Vector2.Zero, cb), i3 = b.Vertex(c + n, Vector2.Zero, cb);
        b.Tri(i0, i1, i2);
        b.Tri(i0, i2, i3);
        return true;
    }

    /// <summary>A polyline of the given width: a quad per segment and a bevel on the outside of every joint (Godot's draw_polyline / draw_polyline_colors).
    /// <paramref name="colors"/> has one colour or one per point. Null when nothing could be drawn.</summary>
    public static CanvasMesh? Polyline(Vector2[] points, Color[] colors, float width, bool closed = false)
    {
        if (points.Length < 2 || points.Length > MaxVertices / 4) return null;
        float w = width < 0f ? 1f : Math.Max(width, 0.0001f);
        var b = new Builder();
        int n = points.Length;
        int segs = closed ? n : n - 1;
        for (int i = 0; i < segs; i++)
        {
            int j = (i + 1) % n;
            AddSegment(b, points[i], points[j], Pack(ColorAt(colors, i)), Pack(ColorAt(colors, j)), w);
        }
        int first = closed ? 0 : 1, last = closed ? n : n - 1;
        for (int i = first; i < last; i++)
        {
            Vector2 p = points[i], prev = points[(i + n - 1) % n], next = points[(i + 1) % n];
            Vector2 d0 = p - prev, d1 = next - p;
            if (d0.LengthSquared() < 1e-12f || d1.LengthSquared() < 1e-12f) continue;
            d0 = d0.Normalized(); d1 = d1.Normalized();
            float cross = d0.X * d1.Y - d0.Y * d1.X;
            if (MathF.Abs(cross) < 1e-4f) continue;
            float side = cross > 0f ? -1f : 1f;   // the outside of the turn
            Vector2 n0 = new Vector2(-d0.Y, d0.X) * (w * 0.5f * side), n1 = new Vector2(-d1.Y, d1.X) * (w * 0.5f * side);
            uint c = Pack(ColorAt(colors, i));
            b.Tri(b.Vertex(p, Vector2.Zero, c), b.Vertex(p + n0, Vector2.Zero, c), b.Vertex(p + n1, Vector2.Zero, c));
        }
        return b.ToMesh(0);
    }

    /// <summary>Independent segments (Godot's draw_multiline): points 0-1, 2-3, ...</summary>
    public static CanvasMesh? Multiline(Vector2[] points, Color[] colors, float width, bool perSegmentColors)
    {
        if (points.Length < 2 || points.Length > MaxVertices / 4) return null;
        float w = width < 0f ? 1f : Math.Max(width, 0.0001f);
        var b = new Builder();
        for (int i = 0; i + 1 < points.Length; i += 2)
        {
            Color c = ColorAt(colors, perSegmentColors ? i / 2 : 0);
            AddSegment(b, points[i], points[i + 1], Pack(c), Pack(c), w);
        }
        return b.ToMesh(0);
    }

    /// <summary>The points of an arc from <paramref name="startAngle"/> to <paramref name="endAngle"/> (radians, 0 along +x, y down so it turns clockwise on screen).
    /// <paramref name="pointCount"/> is at least two.</summary>
    public static Vector2[] ArcPoints(Vector2 center, float radius, float startAngle, float endAngle, int pointCount)
    {
        int n = Math.Clamp(pointCount, 2, 4096);
        var pts = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float a = startAngle + (endAngle - startAngle) * i / (n - 1);
            pts[i] = center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
        }
        return pts;
    }

    /// <summary>How many points a circle of this radius gets: enough that the chord error stays under about a quarter of a pixel.</summary>
    public static int CirclePoints(float radius)
    {
        if (!(radius > 0f)) return 8;
        double step = 2.0 * Math.Acos(Math.Clamp(1.0 - 0.25 / Math.Max(radius, 0.26f), -1.0, 1.0));
        return Math.Clamp((int)Math.Ceiling(2.0 * Math.PI / Math.Max(step, 1e-3)), 12, 256);
    }

    /// <summary>A filled circle as a fan.</summary>
    public static CanvasMesh? Circle(Vector2 center, float radius, Color color)
    {
        if (!(radius > 0f) || !Finite(center)) return null;
        int n = CirclePoints(radius);
        var b = new Builder();
        uint c = Pack(color);
        int hub = b.Vertex(center, Vector2.Zero, c);
        for (int i = 0; i < n; i++)
        {
            float a = MathF.Tau * i / n;
            b.Vertex(center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius, Vector2.Zero, c);
        }
        for (int i = 0; i < n; i++) b.Tri(hub, 1 + i, 1 + (i + 1) % n);
        return b.ToMesh(0);
    }

    /// <summary>One to four points drawn as Godot's draw_primitive does: a pixel, a line, a triangle or a quad. Texture coordinates optional.</summary>
    public static CanvasMesh? Primitive(Vector2[] points, Color[] colors, Vector2[]? uvs, ulong texture)
    {
        switch (points.Length)
        {
            case 1:
            {
                Vector2 p = points[0];
                var b = new Builder();
                uint c = Pack(ColorAt(colors, 0));
                int i0 = b.Vertex(p, Vector2.Zero, c), i1 = b.Vertex(p + new Vector2(1, 0), Vector2.Zero, c), i2 = b.Vertex(p + new Vector2(1, 1), Vector2.Zero, c), i3 = b.Vertex(p + new Vector2(0, 1), Vector2.Zero, c);
                b.Tri(i0, i1, i2); b.Tri(i0, i2, i3);
                return b.ToMesh(0);
            }
            case 2:
                return Line(points[0], points[1], ColorAt(colors, 0), 1f);
            case 3:
            case 4:
            {
                foreach (Vector2 p in points) if (!Finite(p)) return null;
                var b = new Builder();
                for (int i = 0; i < points.Length; i++) b.Vertex(points[i], uvs != null && i < uvs.Length ? uvs[i] : Vector2.Zero, Pack(ColorAt(colors, i)));
                b.Tri(0, 1, 2);
                if (points.Length == 4) b.Tri(0, 2, 3);
                return b.ToMesh(texture);
            }
            default:
                return null;
        }
    }

    // ---- TextureProgressBar's radial fill (F56) ----

    /// <summary>The ray from <paramref name="center"/> at <paramref name="angleDeg"/> (0 = straight up, increasing clockwise, as Godot measures a radial fill)
    /// meets the rectangle's edge here.</summary>
    public static Vector2 RayToRect(Rect2 rect, Vector2 center, float angleDeg)
    {
        float a = angleDeg * (MathF.PI / 180f);
        Vector2 dir = new(MathF.Sin(a), -MathF.Cos(a));
        float t = float.PositiveInfinity;
        if (dir.X > 1e-6f) t = Math.Min(t, (rect.End.X - center.X) / dir.X);
        else if (dir.X < -1e-6f) t = Math.Min(t, (rect.Position.X - center.X) / dir.X);
        if (dir.Y > 1e-6f) t = Math.Min(t, (rect.End.Y - center.Y) / dir.Y);
        else if (dir.Y < -1e-6f) t = Math.Min(t, (rect.Position.Y - center.Y) / dir.Y);
        if (!float.IsFinite(t) || t < 0f) t = 0f;
        return center + dir * t;
    }

    /// <summary>The outline of the wedge of <paramref name="rect"/> swept from <paramref name="startDeg"/> by <paramref name="sweepDeg"/> clockwise (negative sweeps
    /// go anticlockwise) about <paramref name="center"/>: the centre, the start ray's hit, every rectangle corner the sweep passes, the end ray's hit. A sweep of a full
    /// turn or more gives the whole rectangle as a fan. Empty for a sweep of zero or a rectangle with no area.</summary>
    public static Vector2[] RadialWedge(Rect2 rect, Vector2 center, float startDeg, float sweepDeg)
    {
        if (!(rect.Size.X > 0f) || !(rect.Size.Y > 0f) || !float.IsFinite(startDeg) || !float.IsFinite(sweepDeg) || MathF.Abs(sweepDeg) < 1e-3f) return System.Array.Empty<Vector2>();
        center = new Vector2(Math.Clamp(center.X, rect.Position.X, rect.End.X), Math.Clamp(center.Y, rect.Position.Y, rect.End.Y));
        float s = sweepDeg;
        float from = startDeg, to = startDeg + s;
        if (s < 0f) { from = startDeg + s; to = startDeg; s = -s; }   // always walk clockwise between from and to
        var pts = new List<Vector2> { center, RayToRect(rect, center, from) };
        if (s >= 359.999f)
        {
            to = from + 360f;
        }
        // The corners, as angles from the centre in the same convention, in the sweep.
        var corners = new[] { rect.Position, new Vector2(rect.End.X, rect.Position.Y), rect.End, new Vector2(rect.Position.X, rect.End.Y) };
        var hits = new List<(float Angle, Vector2 P)>();
        foreach (Vector2 c in corners)
        {
            Vector2 d = c - center;
            if (d.LengthSquared() < 1e-9f) continue;
            float ang = MathF.Atan2(d.X, -d.Y) * (180f / MathF.PI);   // 0 up, clockwise
            while (ang < from) ang += 360f;
            while (ang >= from + 360f) ang -= 360f;
            if (ang > from + 1e-3f && ang < to - 1e-3f) hits.Add((ang, c));
        }
        hits.Sort((x, y) => x.Angle.CompareTo(y.Angle));
        foreach (var h in hits) pts.Add(h.P);
        pts.Add(RayToRect(rect, center, to));
        return pts.ToArray();
    }

    /// <summary>The wedge as a textured fan: the texture coordinates are each point's place in <paramref name="rect"/> (0 to 1). The texture is drawn
    /// whole into <paramref name="rect"/>, as the bar's progress texture is.</summary>
    public static CanvasMesh? RadialFill(Rect2 rect, Vector2 center, float startDeg, float sweepDeg, Color tint, ulong texture)
    {
        Vector2[] w = RadialWedge(rect, center, startDeg, sweepDeg);
        if (w.Length < 3) return null;
        var b = new Builder();
        uint c = Pack(tint);
        foreach (Vector2 p in w) b.Vertex(p, new Vector2((p.X - rect.Position.X) / rect.Size.X, (p.Y - rect.Position.Y) / rect.Size.Y), c);
        for (int i = 1; i + 1 < w.Length; i++) b.Tri(0, i, i + 1);
        return b.ToMesh(texture);
    }
}
