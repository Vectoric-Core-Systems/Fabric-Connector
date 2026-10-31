// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Godot's SoftBody3D on the engine's soft-body physics (Aver.Physics.SoftBodyBuilder). The node reads its mesh's vertices, welds those at one place into one simulated
// point each (a mesh repeats a vertex for every UV seam; the simulation must not tear along them), makes a body of the points and their triangle edges, and every physics
// step writes the points back into a live copy of the mesh that the node draws. A point is named the way Godot names it, by the index of a vertex of the mesh, and the
// weld map turns that into the simulated point.
//
// UNITS DIFFER AND ARE TUNED BY EYE. Godot's stiffness (0 to 1), pressure coefficient and damping coefficient are not Jolt's: an edge's compliance is (1 - linear_stiffness)
// times 0.001 m/N, the pressure is the coefficient times 20 kPa, the damping is the coefficient times 10 per second (Godot's default 0.01 is the engine's 0.1), and the drag
// is the aerodynamic drag. A node needs a mesh it can read: a primitive mesh, an ArrayMesh or a mesh a script builds; a mesh that is only a file on disk (an imported model)
// has no vertices in the runtime and the node stays an ordinary mesh with a warning. Bending is not resisted, as in Godot's own soft bodies.

using Aver.Scripting;
using PhysBody = Aver.Physics.Body;
using PhysFloat3 = Aver.Physics.Float3;

namespace Aver.Fabric;

/// <summary>A mesh that deforms under physics (Godot's SoftBody3D).</summary>
public class SoftBody3D : MeshInstance3D
{
    public enum DisableModeEnum { Remove = 0, KeepActive = 1 }

    private sealed class Pin
    {
        public int Point;
        public NodePath Path = new();
        public Vector3 Offset;
    }

    private readonly List<Pin> _pins = new();
    private readonly List<CollisionObject3D> _exceptions = new();
    private int _precision = 5;
    private float _mass = 1f, _stiffness = 0.5f, _pressure, _damping = 0.01f, _drag;
    private uint _layer = 1, _mask = 1;

    /// <summary>The solver's iterations a step (the engine's, 5 by default).</summary>
    public int SimulationPrecision { get => _precision; set { _precision = Math.Max(value, 1); Rebuild(); } }
    public float TotalMass { get => _mass; set { _mass = MathF.Max(value, 1e-4f); Rebuild(); } }
    public float LinearStiffness { get => _stiffness; set { _stiffness = Math.Clamp(value, 0f, 1f); Rebuild(); } }
    public float PressureCoefficient { get => _pressure; set { _pressure = value; if (_body.IsValid) ApplyPressure(); } }
    public float DampingCoefficient { get => _damping; set { _damping = value; Rebuild(); } }
    public float DragCoefficient { get => _drag; set { _drag = value; if (_body.IsValid) ApplyDrag(); } }
    public NodePath ParentCollisionIgnore { get; set; } = new();
    public bool RayPickable { get; set; } = true;
    public DisableModeEnum DisableMode { get; set; } = DisableModeEnum.Remove;

    public uint CollisionLayer { get => _layer; set { _layer = value; Rebuild(); } }
    public uint CollisionMask { get => _mask; set { _mask = value; Rebuild(); } }
    public bool GetCollisionLayerValue(int layerNumber) => (_layer & (1u << (layerNumber - 1))) != 0;
    public void SetCollisionLayerValue(int layerNumber, bool value) => CollisionLayer = value ? _layer | (1u << (layerNumber - 1)) : _layer & ~(1u << (layerNumber - 1));
    public bool GetCollisionMaskValue(int layerNumber) => (_mask & (1u << (layerNumber - 1))) != 0;
    public void SetCollisionMaskValue(int layerNumber, bool value) => CollisionMask = value ? _mask | (1u << (layerNumber - 1)) : _mask & ~(1u << (layerNumber - 1));

    // ---- the weld map: pure, so it can be checked without an engine ----

    /// <summary>The simulation a mesh makes: the points, the map from a mesh vertex to its point, the triangles and the edges between points.</summary>
    internal sealed class Weld
    {
        public Vector3[] Points = System.Array.Empty<Vector3>();   // positions in the mesh's own space, metres
        public int[] RenderToPoint = System.Array.Empty<int>();
        public int[] Triangles = System.Array.Empty<int>();        // 3 point indices each, the mesh's winding
        public int[] Edges = System.Array.Empty<int>();            // 2 point indices each, once per pair
        public float[] EdgeLength = System.Array.Empty<float>();   // metres
        public bool Closed;                                        // every edge has two triangles
    }

    /// <summary>Welds vertices that sit at one place (within 0.1 mm) and lists the triangles and unique edges of the result.</summary>
    internal static Weld BuildWeld(Vector3[] vertices, int[]? indices)
    {
        var w = new Weld { RenderToPoint = new int[vertices.Length] };
        var byCell = new Dictionary<(long, long, long), int>();
        var points = new List<Vector3>();
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 v = vertices[i];
            var cell = ((long)MathF.Round(v.X * 1e4f), (long)MathF.Round(v.Y * 1e4f), (long)MathF.Round(v.Z * 1e4f));
            if (!byCell.TryGetValue(cell, out int p)) { p = points.Count; byCell[cell] = p; points.Add(v); }
            w.RenderToPoint[i] = p;
        }
        w.Points = points.ToArray();
        int count = indices?.Length ?? vertices.Length;
        var tris = new List<int>();
        var edgeUse = new Dictionary<(int, int), int>();
        for (int t = 0; t + 2 < count; t += 3)
        {
            int a = w.RenderToPoint[indices != null ? indices[t] : t], b = w.RenderToPoint[indices != null ? indices[t + 1] : t + 1], c = w.RenderToPoint[indices != null ? indices[t + 2] : t + 2];
            if (a == b || b == c || a == c) continue;
            tris.Add(a); tris.Add(b); tris.Add(c);
            foreach ((int x, int y) in new[] { (a, b), (b, c), (c, a) })
            {
                var key = x < y ? (x, y) : (y, x);
                edgeUse[key] = edgeUse.TryGetValue(key, out int n) ? n + 1 : 1;
            }
        }
        w.Triangles = tris.ToArray();
        var edges = new List<int>();
        var lengths = new List<float>();
        bool closed = edgeUse.Count > 0;
        foreach (((int x, int y), int uses) in edgeUse)
        {
            edges.Add(x); edges.Add(y);
            lengths.Add(w.Points[x].DistanceTo(w.Points[y]));
            if (uses != 2) closed = false;
        }
        w.Edges = edges.ToArray();
        w.EdgeLength = lengths.ToArray();
        w.Closed = closed;
        return w;
    }

    /// <summary>An edge's compliance in the engine's m/N from Godot's linear stiffness.</summary>
    internal static float EdgeCompliance(float linearStiffness) => (1f - Math.Clamp(linearStiffness, 0f, 1f)) * 0.001f;

    /// <summary>The inverse mass of one point: the body's mass shared evenly, 0 for a pinned one.</summary>
    internal static float PointInverseMass(float totalMassKg, int points, bool pinned) => pinned ? 0f : points / MathF.Max(totalMassKg, 1e-4f);

    // ---- pinned points ----

    /// <summary>Pins or frees a vertex of the mesh. A pin with an attachment path follows that node (plus the offset in its space); without one it holds the vertex where it is.</summary>
    public void SetPointPinned(int pointIndex, bool pinned, NodePath? attachmentPath = null, int insertAt = -1)
    {
        int at = _pins.FindIndex(p => p.Point == pointIndex);
        if (pinned)
        {
            var pin = new Pin { Point = pointIndex, Path = attachmentPath ?? new NodePath() };
            if (at >= 0) _pins[at] = pin;
            else if (insertAt >= 0 && insertAt <= _pins.Count) _pins.Insert(insertAt, pin);
            else _pins.Add(pin);
        }
        else if (at >= 0) _pins.RemoveAt(at);
        else return;
        ApplyPins();
    }

    public bool IsPointPinned(int pointIndex) => _pins.Exists(p => p.Point == pointIndex);

    internal override bool TryDynamicSet(string name, Variant value)
    {
        if (!name.StartsWith("attachments/", StringComparison.Ordinal)) return base.TryDynamicSet(name, value);
        string[] parts = name.Split('/');
        if (parts.Length != 3 || !int.TryParse(parts[1], out int slot) || slot < 0) return false;
        while (_pins.Count <= slot) _pins.Add(new Pin { Point = -1 });
        Pin pin = _pins[slot];
        switch (parts[2])
        {
            case "point_index": pin.Point = (int)value.AsInt64(); break;
            case "spatial_attachment_path": pin.Path = value.AsNodePath(); break;
            case "offset": pin.Offset = value.AsVector3(); break;
            default: return false;
        }
        return true;
    }

    // ---- the simulation ----

    private PhysBody _body;
    private Aver.Physics.SoftBodyHandle _soft;
    private Weld? _weld;
    private ArrayMesh? _live;
    private Vector3[] _renderRest = System.Array.Empty<Vector3>();
    private int[]? _renderIndices;
    private float[] _positions = System.Array.Empty<float>();
    private bool _warned, _building;

    /// <summary>The body as a physics id (0 until it exists).</summary>
    public Rid GetPhysicsRid() => new(_body.IsValid ? (ulong)_body.Handle : 0UL);

    internal override bool UsesInternalPhysicsProcess => true;

    internal override void OnReadying()
    {
        base.OnReadying();
        if (Self.IsValid && NodeHost.Current.IsEngine) Build();
    }

    internal override void OnExitingTree()
    {
        base.OnExitingTree();
        DestroyBody();
    }

    private void Rebuild()
    {
        if (!_body.IsValid || _building) return;
        Aver.Framework.Deferred.Call(() => { if (IsAlive && IsInsideTree()) { DestroyBody(); Build(); } });
    }

    // The mesh's first surface as vertices and indices, or null when this mesh keeps none in memory.
    private static MeshSurface? ReadableSurface(Mesh? mesh) => mesh switch
    {
        PrimitiveMesh p => p.Surface(),
        ArrayMesh a when a.Surfaces.Count > 0 && a.Surfaces[0].Primitive == Mesh.PrimitiveType.Triangles => a.Surfaces[0],
        _ => null,
    };

    private void Build()
    {
        _building = true;
        try
        {
            // The first time the node has the mesh a scene gave it; later builds keep the original so a rebuild starts from rest.
            Mesh? source = _source ?? Mesh;
            MeshSurface? surface = ReadableSurface(source);
            if (surface == null || surface.Vertices.Length < 3)
            {
                if (!_warned) { _warned = true; Log.Warn($"[Fabric] SoftBody3D '{Name}' has no mesh it can read ({source?.GetClass() ?? "none"}); it stays a plain mesh"); }
                return;
            }
            _source = source;
            _renderIndices = surface.Indices;
            _renderRest = (Vector3[])surface.Vertices.Clone();
            _weld = BuildWeld(_renderRest, _renderIndices);
            if (_weld.Triangles.Length == 0 || _weld.Points.Length > 65536) { Log.Warn($"[Fabric] SoftBody3D '{Name}': the mesh makes no soft body"); return; }

            Transform3D g = GlobalTransform;
            int n = _weld.Points.Length;
            var rest = new float[n * 3];
            var invMass = new float[n];
            var pinned = new HashSet<int>();
            foreach (Pin p in _pins) if (p.Point >= 0 && p.Point < _renderRest.Length) pinned.Add(_weld.RenderToPoint[p.Point]);
            Vector3 centre = Vector3.Zero;
            for (int i = 0; i < n; i++)
            {
                Aver.Scene.Vec3 a = FabricSpace.ToAverPosition(g * _weld.Points[i]);
                rest[i * 3] = a.X; rest[i * 3 + 1] = a.Y; rest[i * 3 + 2] = a.Z;
                invMass[i] = PointInverseMass(_mass, n, pinned.Contains(i));
                centre += g * _weld.Points[i];
            }
            centre /= n;

            using var b = new Aver.Physics.SoftBodyBuilder(n);
            if (!b.IsValid) return;
            b.Vertices(rest, invMass);
            // The axis map mirrors the space, which turns every triangle over.
            var faces = new int[_weld.Triangles.Length];
            for (int t = 0; t + 2 < faces.Length; t += 3) { faces[t] = _weld.Triangles[t]; faces[t + 1] = _weld.Triangles[t + 2]; faces[t + 2] = _weld.Triangles[t + 1]; }
            b.Faces(faces);
            var rl = new float[_weld.EdgeLength.Length];
            for (int e = 0; e < rl.Length; e++) rl[e] = _weld.EdgeLength[e] * FabricSpace.CmPerMetre;
            float compliance = EdgeCompliance(_stiffness);
            var comp = new float[rl.Length];
            System.Array.Fill(comp, compliance);
            b.Edges(_weld.Edges, rl, comp);
            b.Set(Aver.Physics.SoftBodyParam.Iterations, _precision);
            b.Set(Aver.Physics.SoftBodyParam.Damping, _damping * 10f);
            b.Set(Aver.Physics.SoftBodyParam.DoubleSided, 1f);
            b.Set(Aver.Physics.SoftBodyParam.Layer, LowestLayer());
            b.Set(Aver.Physics.SoftBodyParam.Entity, Self.Handle);
            Aver.Scene.Vec3 c = FabricSpace.ToAverPosition(centre);
            int handle = b.Finish(new PhysFloat3(c.X, c.Y, c.Z));
            if (handle == 0) { Log.Warn($"[Fabric] SoftBody3D '{Name}': the engine refused the body"); return; }
            _body = new PhysBody(handle);
            _soft = new Aver.Physics.SoftBodyHandle(handle);
            _positions = new float[n * 3];
            ApplyPressure();
            ApplyDrag();
            ApplyExceptions();
            ApplyPins();

            // The node draws a copy of its mesh that follows the points; the mesh the scene gave stays as it was.
            _live = new ArrayMesh();
            Collections.Array arrays = surface.ToArrays();
            _live.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            if (surface.Material != null) _live.SurfaceSetMaterial(0, surface.Material);
            Mesh = _live;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { _warned = true; }
        finally { _building = false; }
    }

    private Mesh? _source;

    private int LowestLayer()
    {
        for (int i = 0; i < 16; i++) if ((_layer & (1u << i)) != 0) return i;
        return 0;
    }

    private void DestroyBody()
    {
        if (!_body.IsValid) return;
        try { _body.Destroy(); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        _body = default;
        _soft = default;
    }

    private void ApplyPressure()
    {
        if (!_body.IsValid || _weld is not { Closed: true }) return;
        _soft.SetPressure(_pressure * 20f);
    }

    private void ApplyDrag()
    {
        if (_body.IsValid) _soft.SetAero(_drag, 0f);
    }

    private void ApplyExceptions()
    {
        if (!_body.IsValid) return;
        if (!ParentCollisionIgnore.IsEmpty && GetNodeOrNull(ParentCollisionIgnore) is CollisionObject3D parent && parent.Body.IsValid)
            _soft.SetPairPolicy(parent.Body.Handle, Aver.Physics.SoftPairMode.Ignore);
        foreach (CollisionObject3D e in _exceptions) if (e.Body.IsValid) _soft.SetPairPolicy(e.Body.Handle, Aver.Physics.SoftPairMode.Ignore);
    }

    public void AddCollisionExceptionWith(Node body)
    {
        if (body is not CollisionObject3D other || _exceptions.Contains(other)) return;
        _exceptions.Add(other);
        if (_body.IsValid && other.Body.IsValid) _soft.SetPairPolicy(other.Body.Handle, Aver.Physics.SoftPairMode.Ignore);
    }

    public void RemoveCollisionExceptionWith(Node body)
    {
        if (body is not CollisionObject3D other || !_exceptions.Remove(other)) return;
        if (_body.IsValid && other.Body.IsValid) _soft.ClearPairPolicy(other.Body.Handle);
    }

    public Collections.Array<PhysicsBody3D> GetCollisionExceptions()
    {
        var list = new Collections.Array<PhysicsBody3D>();
        foreach (CollisionObject3D e in _exceptions) if (e is PhysicsBody3D pb) list.Add(pb);
        return list;
    }

    // Pins hold their point still, or carry it to the node they name.
    private readonly Dictionary<int, (int Point, Node3D? Target, Vector3 Offset)> _attached = new();

    private void ApplyPins()
    {
        if (!_body.IsValid || _weld == null) return;
        int n = _weld.Points.Length;
        var wanted = new Dictionary<int, (Node3D? Target, Vector3 Offset)>();
        foreach (Pin p in _pins)
        {
            if (p.Point < 0 || p.Point >= _renderRest.Length) continue;
            wanted[_weld.RenderToPoint[p.Point]] = (p.Path.IsEmpty ? null : GetNodeOrNull(p.Path) as Node3D, p.Offset);
        }
        // Points no longer pinned get their mass back; pinned ones lose it.
        foreach (int point in _attached.Keys.ToArray())
            if (!wanted.ContainsKey(point)) { _soft.Detach(point, PointInverseMass(_mass, n, false)); _attached.Remove(point); }
        foreach ((int point, (Node3D? target, Vector3 offset) w) in wanted)
        {
            if (_attached.ContainsKey(point)) { _attached[point] = (point, w.target, w.offset); continue; }
            Vector3 at = w.target != null ? w.target.GlobalTransform * w.offset : GlobalTransform * _weld.Points[point];
            Aver.Scene.Vec3 a = FabricSpace.ToAverPosition(at);
            _soft.Attach(point, new PhysFloat3(a.X, a.Y, a.Z));
            _attached[point] = (point, w.target, w.offset);
        }
    }

    internal override void InternalPhysicsProcess(double delta)
    {
        if (!_body.IsValid || _weld == null || _live == null) return;
        foreach ((int point, (int _, Node3D? target, Vector3 offset)) in _attached)
        {
            if (target == null || !target.IsInsideTree()) continue;
            Aver.Scene.Vec3 a = FabricSpace.ToAverPosition(target.GlobalTransform * offset);
            _soft.MoveAttachment(point, new PhysFloat3(a.X, a.Y, a.Z));
        }
        FabricPhysics.Pump();
        if (!_soft.IsActive) return;
        if (_soft.State(_positions, null, _weld.Points.Length) <= 0) return;
        PullPoints();
    }

    // The points back into the live mesh, in the node's own space, with fresh smooth normals.
    private void PullPoints()
    {
        Transform3D inverse = GlobalTransform.AffineInverse();
        var v = new Vector3[_renderRest.Length];
        for (int r = 0; r < v.Length; r++)
        {
            int p = _weld!.RenderToPoint[r];
            v[r] = inverse * FabricSpace.FromAverPosition(new Aver.Scene.Vec3(_positions[p * 3], _positions[p * 3 + 1], _positions[p * 3 + 2]));
        }
        _live!.ReplaceSurfaceVertices(0, v, SmoothNormals(v, _renderIndices));
    }

    internal static Vector3[] SmoothNormals(Vector3[] v, int[]? indices)
    {
        var n = new Vector3[v.Length];
        int count = indices?.Length ?? v.Length;
        for (int t = 0; t + 2 < count; t += 3)
        {
            int a = indices != null ? indices[t] : t, b = indices != null ? indices[t + 1] : t + 1, c = indices != null ? indices[t + 2] : t + 2;
            Vector3 face = (v[c] - v[a]).Cross(v[b] - v[a]);   // Godot's front faces are clockwise, so this points out
            n[a] += face; n[b] += face; n[c] += face;
        }
        for (int i = 0; i < n.Length; i++) n[i] = n[i].LengthSquared() > 1e-18f ? n[i].Normalized() : Vector3.Up;
        return n;
    }

    // ---- forces by point ----

    private float PointMass => _weld == null || _weld.Points.Length == 0 ? _mass : _mass / _weld.Points.Length;

    private void AddVelocity(int[] points, Vector3 dv)
    {
        if (!_body.IsValid || points.Length == 0) return;
        var list = new PhysFloat3[points.Length];
        System.Array.Fill(list, CollisionObject3D.ToPhysDirection(dv, FabricSpace.CmPerMetre));
        _soft.AddVelocity(points, list);
    }

    private int[] PointsOf(int renderVertex) => _weld != null && renderVertex >= 0 && renderVertex < _weld.RenderToPoint.Length ? new[] { _weld.RenderToPoint[renderVertex] } : System.Array.Empty<int>();

    private int[] AllPoints()
    {
        if (_weld == null) return System.Array.Empty<int>();
        var all = new int[_weld.Points.Length];
        for (int i = 0; i < all.Length; i++) all[i] = i;
        return all;
    }

    /// <summary>A push on one vertex's point, in newton seconds (Godot's apply_impulse).</summary>
    public void ApplyImpulse(int pointIndex, Vector3 impulse) => AddVelocity(PointsOf(pointIndex), impulse / PointMass);

    /// <summary>A push on every point, in newton seconds, shared by the mass (Godot's apply_central_impulse).</summary>
    public void ApplyCentralImpulse(Vector3 impulse) => AddVelocity(AllPoints(), impulse / _mass);

    /// <summary>A force on one vertex's point for one physics step.</summary>
    public void ApplyForce(int pointIndex, Vector3 force) => AddVelocity(PointsOf(pointIndex), force / PointMass * (1f / Engine.PhysicsTicksPerSecond));

    /// <summary>A force on the whole body for one physics step.</summary>
    public void ApplyCentralForce(Vector3 force) => AddVelocity(AllPoints(), force / _mass * (1f / Engine.PhysicsTicksPerSecond));

    /// <summary>Where a vertex's point is now, in global coordinates (Godot's get_point_transform); the mesh's rest place before the body exists.</summary>
    public Vector3 GetPointTransform(int pointIndex)
    {
        if (_weld == null)
        {
            // Before the body exists a vertex is where the mesh has it.
            MeshSurface? rest = ReadableSurface(_source ?? Mesh);
            return rest != null && pointIndex >= 0 && pointIndex < rest.Vertices.Length ? GlobalTransform * rest.Vertices[pointIndex] : GlobalTransform.Origin;
        }
        if (pointIndex < 0 || pointIndex >= _weld.RenderToPoint.Length) return GlobalTransform.Origin;
        int p = _weld.RenderToPoint[pointIndex];
        if (_body.IsValid && _positions.Length >= (p + 1) * 3 && (_positions[p * 3] != 0f || _positions[p * 3 + 1] != 0f || _positions[p * 3 + 2] != 0f))
            return FabricSpace.FromAverPosition(new Aver.Scene.Vec3(_positions[p * 3], _positions[p * 3 + 1], _positions[p * 3 + 2]));
        return GlobalTransform * _weld.Points[p];
    }
}
