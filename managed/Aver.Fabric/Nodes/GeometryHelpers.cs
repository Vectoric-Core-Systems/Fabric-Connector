// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Godot's Geometry3D and Geometry2D helper classes and TriangleMesh: segment and ray tests, plane sets of simple shapes, polygon clipping and triangulation.
// Pure managed maths in Godot's space (metres, +Y up). A test that finds nothing returns nil (a default Variant), as Godot's do.

namespace Aver.Fabric;

/// <summary>Godot's Geometry3D: intersection tests, planes of boxes, capsules and cylinders, and clipping.</summary>
public static class Geometry3D
{
    private const float Eps = 1e-5f;

    /// <summary>The point where a ray meets the triangle (a, b, c), or nil.</summary>
    public static Variant RayIntersectsTriangle(Vector3 from, Vector3 dir, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 e1 = b - a, e2 = c - a;
        Vector3 h = dir.Cross(e2);
        float det = e1.Dot(h);
        if (MathF.Abs(det) < 1e-12f) return default;
        float inv = 1f / det;
        Vector3 s = from - a;
        float u = inv * s.Dot(h);
        if (u < 0f || u > 1f) return default;
        Vector3 q = s.Cross(e1);
        float v = inv * dir.Dot(q);
        if (v < 0f || u + v > 1f) return default;
        float t = inv * e2.Dot(q);
        return t > Eps ? (Variant)(from + dir * t) : default;
    }

    /// <summary>The point where the segment from→to meets the triangle, or nil.</summary>
    public static Variant SegmentIntersectsTriangle(Vector3 from, Vector3 to, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 d = to - from;
        float len = d.Length();
        if (len < 1e-12f) return default;
        Variant hit = RayIntersectsTriangle(from, d / len, a, b, c);
        if (hit.VariantType == Variant.Type.Nil) return default;
        Vector3 p = hit.AsVector3();
        return from.DistanceTo(p) <= len + Eps ? (Variant)p : default;
    }

    /// <summary>[point, normal] where the segment first enters the sphere, or an empty array.</summary>
    public static Vector3[] SegmentIntersectsSphere(Vector3 from, Vector3 to, Vector3 spherePosition, float sphereRadius)
    {
        Vector3 d = to - from, m = from - spherePosition;
        float a = d.Dot(d);
        if (a < 1e-12f) return System.Array.Empty<Vector3>();
        float b = m.Dot(d), c = m.Dot(m) - sphereRadius * sphereRadius;
        float disc = b * b - a * c;
        if (disc < 0f) return System.Array.Empty<Vector3>();
        float t = (-b - MathF.Sqrt(disc)) / a;
        if (t < 0f || t > 1f) return System.Array.Empty<Vector3>();
        Vector3 p = from + d * t;
        return new[] { p, (p - spherePosition).Normalized() };
    }

    /// <summary>[point, normal] where the segment first meets a cylinder along Y, centred on the origin, or an empty array.</summary>
    public static Vector3[] SegmentIntersectsCylinder(Vector3 from, Vector3 to, float height, float radius)
    {
        Vector3 d = to - from;
        float best = float.MaxValue;
        Vector3 normal = Vector3.Zero;
        float half = height * 0.5f;
        // The curved side.
        float a = d.X * d.X + d.Z * d.Z, b = from.X * d.X + from.Z * d.Z, c = from.X * from.X + from.Z * from.Z - radius * radius;
        if (a > 1e-12f)
        {
            float disc = b * b - a * c;
            if (disc >= 0f)
            {
                float t = (-b - MathF.Sqrt(disc)) / a;
                float y = from.Y + d.Y * t;
                if (t >= 0f && t <= 1f && y >= -half && y <= half) { best = t; Vector3 p = from + d * t; normal = new Vector3(p.X, 0, p.Z).Normalized(); }
            }
        }
        // The caps.
        if (MathF.Abs(d.Y) > 1e-12f)
            foreach (float cap in new[] { half, -half })
            {
                float t = (cap - from.Y) / d.Y;
                if (t < 0f || t > 1f || t >= best) continue;
                Vector3 p = from + d * t;
                if (p.X * p.X + p.Z * p.Z <= radius * radius) { best = t; normal = cap > 0 ? Vector3.Up : Vector3.Down; }
            }
        if (best == float.MaxValue) return System.Array.Empty<Vector3>();
        return new[] { from + d * best, normal };
    }

    /// <summary>[point, normal] where the segment first enters the convex shape bounded by the planes (normals point outward), or an empty array.</summary>
    public static Vector3[] SegmentIntersectsConvex(Vector3 from, Vector3 to, Collections.Array<Plane> planes)
    {
        Vector3 d = to - from;
        float tEnter = 0f, tExit = 1f;
        Vector3 normal = Vector3.Zero;
        bool entered = false;
        foreach (Plane pl in planes)
        {
            float dist = pl.DistanceTo(from), den = pl.Normal.Dot(d);
            if (MathF.Abs(den) < 1e-12f) { if (dist > 0f) return System.Array.Empty<Vector3>(); continue; }
            float t = -dist / den;
            if (den < 0f) { if (t > tEnter) { tEnter = t; normal = pl.Normal; entered = true; } }
            else if (t < tExit) tExit = t;
            if (tEnter > tExit) return System.Array.Empty<Vector3>();
        }
        return entered ? new[] { from + d * tEnter, normal } : System.Array.Empty<Vector3>();
    }

    /// <summary>The nearest point of the segment s1→s2 to the point.</summary>
    public static Vector3 GetClosestPointToSegment(Vector3 point, Vector3 s1, Vector3 s2)
    {
        Vector3 d = s2 - s1;
        float l = d.LengthSquared();
        if (l < 1e-12f) return s1;
        float t = Math.Clamp((point - s1).Dot(d) / l, 0f, 1f);
        return s1 + d * t;
    }

    /// <summary>The nearest point of the infinite line through s1 and s2 to the point.</summary>
    public static Vector3 GetClosestPointToSegmentUncapped(Vector3 point, Vector3 s1, Vector3 s2)
    {
        Vector3 d = s2 - s1;
        float l = d.LengthSquared();
        return l < 1e-12f ? s1 : s1 + d * ((point - s1).Dot(d) / l);
    }

    /// <summary>The nearest pair of points between two segments: [on p1→p2, on q1→q2].</summary>
    public static Vector3[] GetClosestPointsBetweenSegments(Vector3 p1, Vector3 p2, Vector3 q1, Vector3 q2)
    {
        Vector3 d1 = p2 - p1, d2 = q2 - q1, r = p1 - q1;
        float a = d1.Dot(d1), e = d2.Dot(d2), f = d2.Dot(r);
        float s, t;
        if (a <= 1e-12f && e <= 1e-12f) { s = t = 0f; }
        else if (a <= 1e-12f) { s = 0f; t = Math.Clamp(f / e, 0f, 1f); }
        else
        {
            float c = d1.Dot(r);
            if (e <= 1e-12f) { t = 0f; s = Math.Clamp(-c / a, 0f, 1f); }
            else
            {
                float b = d1.Dot(d2), denom = a * e - b * b;
                s = denom > 1e-12f ? Math.Clamp((b * f - c * e) / denom, 0f, 1f) : 0f;
                t = (b * s + f) / e;
                if (t < 0f) { t = 0f; s = Math.Clamp(-c / a, 0f, 1f); }
                else if (t > 1f) { t = 1f; s = Math.Clamp((b - c) / a, 0f, 1f); }
            }
        }
        return new[] { p1 + d1 * s, q1 + d2 * t };
    }

    /// <summary>The polygon cut by the plane, keeping what is behind it (Godot's inside).</summary>
    public static Vector3[] ClipPolygon(Vector3[] points, Plane plane)
    {
        var result = new List<Vector3>();
        int n = points.Length;
        for (int i = 0; i < n; i++)
        {
            Vector3 cur = points[i], next = points[(i + 1) % n];
            float dc = plane.DistanceTo(cur), dn = plane.DistanceTo(next);
            bool curIn = dc <= Eps, nextIn = dn <= Eps;
            if (curIn) result.Add(cur);
            if (curIn != nextIn && MathF.Abs(dc - dn) > 1e-12f) result.Add(cur + (next - cur) * (dc / (dc - dn)));
        }
        return result.ToArray();
    }

    /// <summary>The six planes of a box with these half extents, outward normals.</summary>
    public static Collections.Array<Plane> BuildBoxPlanes(Vector3 extents) => new(new[]
    {
        new Plane(new Vector3(1, 0, 0), extents.X), new Plane(new Vector3(-1, 0, 0), extents.X),
        new Plane(new Vector3(0, 1, 0), extents.Y), new Plane(new Vector3(0, -1, 0), extents.Y),
        new Plane(new Vector3(0, 0, 1), extents.Z), new Plane(new Vector3(0, 0, -1), extents.Z),
    });

    private static Vector3 AxisVector(Vector3.Axis axis) => axis == Vector3.Axis.X ? Vector3.Right : (axis == Vector3.Axis.Y ? Vector3.Up : Vector3.Back);

    // A frame whose Z is the shape's axis, for building the planes in a fixed layout and turning them to the axis asked for.
    private static Basis AxisBasis(Vector3.Axis axis) => axis switch
    {
        Vector3.Axis.X => new Basis(new Vector3(0, 0, 1), new Vector3(0, 1, 0), new Vector3(-1, 0, 0)),
        Vector3.Axis.Y => new Basis(new Vector3(1, 0, 0), new Vector3(0, 0, 1), new Vector3(0, -1, 0)),
        _ => Basis.Identity,
    };

    /// <summary>The planes of a cylinder: <paramref name="sides"/> around its axis and a cap at each end.</summary>
    public static Collections.Array<Plane> BuildCylinderPlanes(float radius, float height, int sides, Vector3.Axis axis = Vector3.Axis.Z)
    {
        var planes = new Collections.Array<Plane>();
        Basis b = AxisBasis(axis);
        for (int i = 0; i < sides; i++)
        {
            float a = MathF.Tau * i / sides;
            Vector3 n = b * new Vector3(MathF.Cos(a), MathF.Sin(a), 0);
            planes.Add(new Plane(n, radius));
        }
        Vector3 up = b * new Vector3(0, 0, 1);
        planes.Add(new Plane(up, height * 0.5f));
        planes.Add(new Plane(-up, height * 0.5f));
        return planes;
    }

    /// <summary>The planes of a capsule: <paramref name="sides"/> around its axis and <paramref name="lats"/> latitude rings on each cap.</summary>
    public static Collections.Array<Plane> BuildCapsulePlanes(float radius, float height, int sides, int lats, Vector3.Axis axis = Vector3.Axis.Z)
    {
        var planes = new Collections.Array<Plane>();
        Basis b = AxisBasis(axis);
        float half = height * 0.5f - radius;
        for (int i = 0; i < sides; i++)
        {
            float a = MathF.Tau * i / sides;
            Vector3 n = b * new Vector3(MathF.Cos(a), MathF.Sin(a), 0);
            planes.Add(new Plane(n, radius));
            for (int j = 1; j <= lats; j++)
            {
                float lat = MathF.PI * 0.5f * j / (lats + 1);
                Vector3 up = b * new Vector3(0, 0, 1);
                Vector3 tilted = n * MathF.Cos(lat) + up * MathF.Sin(lat);
                planes.Add(new Plane(tilted, tilted.Dot(up * half) + radius));
                planes.Add(new Plane(n * MathF.Cos(lat) - up * MathF.Sin(lat), (n * MathF.Cos(lat) - up * MathF.Sin(lat)).Dot(-up * half) + radius));
            }
        }
        Vector3 axisUp = b * new Vector3(0, 0, 1);
        planes.Add(new Plane(axisUp, height * 0.5f));
        planes.Add(new Plane(-axisUp, height * 0.5f));
        return planes;
    }

    /// <summary>The corner points of the convex shape the planes bound.</summary>
    public static Vector3[] ComputeConvexMeshPoints(Collections.Array<Plane> planes)
    {
        var list = new List<Plane>(planes);
        var points = new List<Vector3>();
        for (int i = 0; i < list.Count; i++)
            for (int j = i + 1; j < list.Count; j++)
                for (int k = j + 1; k < list.Count; k++)
                {
                    Vector3? p = list[i].Intersect3(list[j], list[k]);
                    if (p is not { } v) continue;
                    bool inside = true;
                    foreach (Plane pl in list) if (pl.DistanceTo(v) > 1e-3f) { inside = false; break; }
                    if (!inside) continue;
                    bool dup = false;
                    foreach (Vector3 q in points) if (q.DistanceSquaredTo(v) < 1e-8f) { dup = true; break; }
                    if (!dup) points.Add(v);
                }
        return points.ToArray();
    }
}

/// <summary>Godot's Geometry2D: polygon and segment helpers in the plane.</summary>
public static class Geometry2D
{
    /// <summary>True when the points run clockwise in Godot's 2D (y down), which is a negative shoelace sum in y up.</summary>
    public static bool IsPolygonClockwise(Vector2[] polygon)
    {
        float sum = 0f;
        for (int i = 0; i < polygon.Length; i++)
        {
            Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Length];
            sum += (b.X - a.X) * (b.Y + a.Y);
        }
        return sum > 0f;
    }

    public static bool IsPointInPolygon(Vector2 point, Vector2[] polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            Vector2 a = polygon[i], b = polygon[j];
            if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }

    /// <summary>The triangles of a simple polygon by ear clipping, as indices into it; empty when it is degenerate.</summary>
    public static int[] TriangulatePolygon(Vector2[] polygon)
    {
        int n = polygon.Length;
        var result = new List<int>();
        if (n < 3) return System.Array.Empty<int>();
        var idx = new List<int>();
        for (int i = 0; i < n; i++) idx.Add(i);
        float area = 0f;
        for (int i = 0; i < n; i++) { Vector2 a = polygon[i], b = polygon[(i + 1) % n]; area += a.Cross(b); }
        if (area < 0f) idx.Reverse();
        int guard = 0;
        while (idx.Count > 3 && guard++ < n * n + 8)
        {
            bool clipped = false;
            for (int i = 0; i < idx.Count; i++)
            {
                int ia = idx[(i + idx.Count - 1) % idx.Count], ib = idx[i], ic = idx[(i + 1) % idx.Count];
                Vector2 a = polygon[ia], b = polygon[ib], c = polygon[ic];
                if ((b - a).Cross(c - a) <= 1e-9f) continue;
                bool ear = true;
                foreach (int q in idx)
                {
                    if (q == ia || q == ib || q == ic) continue;
                    Vector2 p = polygon[q];
                    if ((b - a).Cross(p - a) >= 0f && (c - b).Cross(p - b) >= 0f && (a - c).Cross(p - c) >= 0f) { ear = false; break; }
                }
                if (!ear) continue;
                result.AddRange(new[] { ia, ib, ic });
                idx.RemoveAt(i);
                clipped = true;
                break;
            }
            if (!clipped) return System.Array.Empty<int>();
        }
        if (idx.Count == 3) result.AddRange(idx);
        return result.ToArray();
    }

    /// <summary>The convex hull of the points, counter-clockwise in y up, closed (the first point repeated at the end as Godot does).</summary>
    public static Vector2[] ConvexHull(Vector2[] points)
    {
        var p = new List<Vector2>(points);
        p.Sort((l, r) => l.X == r.X ? l.Y.CompareTo(r.Y) : l.X.CompareTo(r.X));
        if (p.Count < 3) return p.ToArray();
        var hull = new List<Vector2>();
        foreach (Vector2 v in p)
        {
            while (hull.Count >= 2 && (hull[^1] - hull[^2]).Cross(v - hull[^2]) <= 0f) hull.RemoveAt(hull.Count - 1);
            hull.Add(v);
        }
        int lower = hull.Count + 1;
        for (int i = p.Count - 2; i >= 0; i--)
        {
            Vector2 v = p[i];
            while (hull.Count >= lower && (hull[^1] - hull[^2]).Cross(v - hull[^2]) <= 0f) hull.RemoveAt(hull.Count - 1);
            hull.Add(v);
        }
        return hull.ToArray();
    }

    public static Vector2 GetClosestPointToSegment(Vector2 point, Vector2 s1, Vector2 s2)
    {
        Vector2 d = s2 - s1;
        float l = d.Dot(d);
        if (l < 1e-12f) return s1;
        return s1 + d * Math.Clamp((point - s1).Dot(d) / l, 0f, 1f);
    }

    /// <summary>The point where the segments cross, or nil.</summary>
    public static Variant SegmentIntersectsSegment(Vector2 fromA, Vector2 toA, Vector2 fromB, Vector2 toB)
    {
        Vector2 r = toA - fromA, s = toB - fromB;
        float den = r.Cross(s);
        if (MathF.Abs(den) < 1e-12f) return default;
        float t = (fromB - fromA).Cross(s) / den, u = (fromB - fromA).Cross(r) / den;
        return t >= 0f && t <= 1f && u >= 0f && u <= 1f ? (Variant)(fromA + r * t) : default;
    }

    /// <summary>The point where two lines (a point and a direction each) cross, or nil.</summary>
    public static Variant LineIntersectsLine(Vector2 fromA, Vector2 dirA, Vector2 fromB, Vector2 dirB)
    {
        float den = dirA.Cross(dirB);
        if (MathF.Abs(den) < 1e-12f) return default;
        float t = (fromB - fromA).Cross(dirB) / den;
        return fromA + dirA * t;
    }
}

/// <summary>A triangle soup that answers ray and segment queries (Godot's TriangleMesh), built from a list of faces, three points each.</summary>
public class TriangleMesh : RefCounted
{
    private Vector3[] _faces = System.Array.Empty<Vector3>();

    public bool CreateFromFaces(Vector3[] faces)
    {
        if (faces.Length % 3 != 0) return false;
        _faces = (Vector3[])faces.Clone();
        return true;
    }

    public Vector3[] GetFaces() => (Vector3[])_faces.Clone();

    /// <summary>The nearest hit of the segment: position, normal and face_index; empty when it hits nothing.</summary>
    public Collections.Dictionary IntersectSegment(Vector3 begin, Vector3 end)
    {
        Vector3 d = end - begin;
        float len = d.Length();
        return len < 1e-12f ? new Collections.Dictionary() : Cast(begin, d / len, len);
    }

    /// <summary>The nearest hit of the ray: position, normal and face_index; empty when it hits nothing.</summary>
    public Collections.Dictionary IntersectRay(Vector3 begin, Vector3 dir) => Cast(begin, dir.Normalized(), float.MaxValue);

    private Collections.Dictionary Cast(Vector3 from, Vector3 dir, float maxDist)
    {
        var result = new Collections.Dictionary();
        float best = maxDist;
        int hitFace = -1;
        for (int f = 0; f + 2 < _faces.Length; f += 3)
        {
            Vector3 a = _faces[f], b = _faces[f + 1], c = _faces[f + 2];
            Variant hit = Geometry3D.RayIntersectsTriangle(from, dir, a, b, c);
            if (hit.VariantType == Variant.Type.Nil) continue;
            float t = from.DistanceTo(hit.AsVector3());
            if (t > best) continue;
            best = t;
            hitFace = f / 3;
        }
        if (hitFace < 0) return result;
        Vector3 fa = _faces[hitFace * 3], fb = _faces[hitFace * 3 + 1], fc = _faces[hitFace * 3 + 2];
        result["position"] = from + dir * best;
        result["normal"] = (fb - fa).Cross(fc - fa).Normalized();
        result["face_index"] = hitFace;
        return result;
    }
}
