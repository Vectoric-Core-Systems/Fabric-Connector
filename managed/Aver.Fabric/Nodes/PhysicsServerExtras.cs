// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// The rest of Godot's 3D physics API that sits on the G2 engine layer: shape owners on a collision object, test_move, the physics server's space parameters and body
// test motion, point queries, and the tree's collision outline. Godot units at the boundary (metres, Godot's axes); FabricPhysics converts.

using Aver.Scripting;
using PhysBody = Aver.Physics.Body;
using PhysFloat3 = Aver.Physics.Float3;

namespace Aver.Fabric;

// ---- shape owners on a collision object ----

public partial class CollisionObject3D
{
    private sealed class ShapeOwnerData
    {
        public GodotObject? Owner;
        public Transform3D Xform = Transform3D.Identity;
        public bool Disabled;
        public readonly List<Shape3D> Shapes = new();
    }

    // A CollisionShape3D child is an owner too, numbered by its place among the children from 1; the owners a script makes start above them.
    private const uint FirstScriptOwner = 1u << 20;
    private readonly Dictionary<uint, ShapeOwnerData> _shapeOwners = new();
    private uint _nextShapeOwner = FirstScriptOwner;

    private List<CollisionShape3D> ShapeChildren()
    {
        var list = new List<CollisionShape3D>();
        foreach (Node c in ChildrenSnapshot()) if (c is CollisionShape3D cs) list.Add(cs);
        return list;
    }

    private CollisionShape3D? ChildOwner(uint ownerId)
    {
        if (ownerId == 0 || ownerId >= FirstScriptOwner) return null;
        List<CollisionShape3D> kids = ShapeChildren();
        return ownerId <= kids.Count ? kids[(int)ownerId - 1] : null;
    }

    /// <summary>Makes an owner for shapes a script adds (Godot's create_shape_owner); returns its id. The body is made again with them.</summary>
    public uint CreateShapeOwner(GodotObject owner)
    {
        uint id = _nextShapeOwner++;
        _shapeOwners[id] = new ShapeOwnerData { Owner = owner };
        return id;
    }

    public void RemoveShapeOwner(uint ownerId)
    {
        if (_shapeOwners.Remove(ownerId)) QueueRebuild();
    }

    /// <summary>The ids of every owner: the CollisionShape3D children, then the ones made by <see cref="CreateShapeOwner"/>.</summary>
    public int[] GetShapeOwners()
    {
        var ids = new List<int>();
        int kids = ShapeChildren().Count;
        for (int i = 1; i <= kids; i++) ids.Add(i);
        foreach (uint id in _shapeOwners.Keys) ids.Add((int)id);
        return ids.ToArray();
    }

    public GodotObject? ShapeOwnerGetOwner(uint ownerId) => ChildOwner(ownerId) ?? (_shapeOwners.TryGetValue(ownerId, out ShapeOwnerData? d) ? d.Owner : null);

    public void ShapeOwnerSetTransform(uint ownerId, Transform3D transform)
    {
        if (ChildOwner(ownerId) is { } cs) { cs.Transform = transform; QueueRebuild(); }
        else if (_shapeOwners.TryGetValue(ownerId, out ShapeOwnerData? d)) { d.Xform = transform; QueueRebuild(); }
    }

    public Transform3D ShapeOwnerGetTransform(uint ownerId) =>
        ChildOwner(ownerId)?.Transform ?? (_shapeOwners.TryGetValue(ownerId, out ShapeOwnerData? d) ? d.Xform : Transform3D.Identity);

    public void ShapeOwnerSetDisabled(uint ownerId, bool disabled)
    {
        if (ChildOwner(ownerId) is { } cs) cs.Disabled = disabled;
        else if (_shapeOwners.TryGetValue(ownerId, out ShapeOwnerData? d)) { d.Disabled = disabled; QueueRebuild(); }
    }

    public bool IsShapeOwnerDisabled(uint ownerId) => ChildOwner(ownerId)?.Disabled ?? (_shapeOwners.TryGetValue(ownerId, out ShapeOwnerData? d) && d.Disabled);

    public void ShapeOwnerAddShape(uint ownerId, Shape3D shape)
    {
        if (ChildOwner(ownerId) is { } cs) cs.Shape = shape;   // a CollisionShape3D holds one shape; adding replaces it
        else if (_shapeOwners.TryGetValue(ownerId, out ShapeOwnerData? d)) { d.Shapes.Add(shape); QueueRebuild(); }
    }

    public int ShapeOwnerGetShapeCount(uint ownerId) => ChildOwner(ownerId) is { } cs ? (cs.Shape == null ? 0 : 1) : (_shapeOwners.TryGetValue(ownerId, out ShapeOwnerData? d) ? d.Shapes.Count : 0);

    public Shape3D? ShapeOwnerGetShape(uint ownerId, int shapeId)
    {
        if (ChildOwner(ownerId) is { } cs) return shapeId == 0 ? cs.Shape : null;
        return _shapeOwners.TryGetValue(ownerId, out ShapeOwnerData? d) && shapeId >= 0 && shapeId < d.Shapes.Count ? d.Shapes[shapeId] : null;
    }

    public void ShapeOwnerRemoveShape(uint ownerId, int shapeId)
    {
        if (ChildOwner(ownerId) is { } cs) { if (shapeId == 0) cs.Shape = null; }
        else if (_shapeOwners.TryGetValue(ownerId, out ShapeOwnerData? d) && shapeId >= 0 && shapeId < d.Shapes.Count) { d.Shapes.RemoveAt(shapeId); QueueRebuild(); }
    }

    public void ShapeOwnerClearShapes(uint ownerId)
    {
        if (ChildOwner(ownerId) is { } cs) cs.Shape = null;
        else if (_shapeOwners.TryGetValue(ownerId, out ShapeOwnerData? d)) { d.Shapes.Clear(); QueueRebuild(); }
    }

    /// <summary>The index the body gives a shape of this owner (Godot's shape_owner_get_shape_index): the shapes count from the first child's.</summary>
    public int ShapeOwnerGetShapeIndex(uint ownerId, int shapeId)
    {
        int index = 0;
        foreach (int id in GetShapeOwners())
        {
            if ((uint)id == ownerId) return shapeId >= 0 && shapeId < ShapeOwnerGetShapeCount(ownerId) ? index + shapeId : -1;
            index += ShapeOwnerGetShapeCount((uint)id);
        }
        return -1;
    }

    /// <summary>The owner of the shape at this index of the body (Godot's shape_find_owner).</summary>
    public uint ShapeFindOwner(int shapeIndex)
    {
        foreach (int id in GetShapeOwners())
        {
            int count = ShapeOwnerGetShapeCount((uint)id);
            if (shapeIndex < count) return (uint)id;
            shapeIndex -= count;
        }
        return 0;
    }

    /// <summary>The shapes the script added, with the owner's transform, for the body builder.</summary>
    internal IEnumerable<(Shape3D Shape, Transform3D Local)> OwnedShapes()
    {
        foreach (ShapeOwnerData d in _shapeOwners.Values)
        {
            if (d.Disabled) continue;
            foreach (Shape3D s in d.Shapes) yield return (s, d.Xform);
        }
    }
}

// ---- moving a body without a character controller ----

public partial class PhysicsBody3D
{
    /// <summary>Whether the body would hit something if it moved by <paramref name="motion"/> from <paramref name="from"/>; the body stays where it is. What it would
    /// touch goes into <paramref name="collision"/> when one is given (Godot's test_move).</summary>
    public bool TestMove(Transform3D from, Vector3 motion, KinematicCollision3D? collision = null, float safeMargin = 0.001f, bool recoveryAsCollision = false, int maxCollisions = 1)
    {
        if (!Body.IsValid) return false;
        try
        {
            var flags = Aver.Physics.SpaceQueryFlags.Bodies | (recoveryAsCollision ? Aver.Physics.SpaceQueryFlags.RecoveryAsCollision : 0);
            Aver.Physics.MotionTestResult? r = Body.TestMotion(ToPhysDirection(motion, FabricSpace.CmPerMetre), FabricSpace.ToAverLength(safeMargin), flags, null, Math.Max(maxCollisions, 1),
                (FabricPhysics.ToPhysPosition(from.Origin), FabricPhysics.ToPhysRotation(from.Basis.Orthonormalized().GetRotationQuaternion())));
            if (r is not { } result || !result.Collided) return false;
            if (collision != null) FabricPhysics.FillCollision(collision, result);
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }
}

// ---- the physics server: space parameters and test motion ----

public static partial class PhysicsServer3D
{
    public enum SpaceParameter
    {
        ContactRecycleRadius = 0, ContactMaxSeparation = 1, ContactMaxAllowedPenetration = 2, ContactDefaultBias = 3,
        BodyLinearVelocitySleepThreshold = 4, BodyAngularVelocitySleepThreshold = 5, BodyTimeToSleep = 6, SolverIterations = 7,
    }

    // Godot's defaults, the values a script reads back until it sets one.
    private static readonly float[] s_space = { 0.01f, 0.05f, 0.01f, 0.8f, 0.1f, 8f * MathF.PI / 180f, 0.5f, 16f };

    /// <summary>Sets one of the space's solver parameters (Godot's space_set_param). Those with a counterpart go to the engine's live solver settings
    /// (iterations to velocity steps, default bias to the Baumgarte fraction, allowed penetration and separation to the penetration slop and speculative distance,
    /// the sleep thresholds and time to the sleep settings); the angular sleep threshold and recycle radius are kept only.</summary>
    public static void SpaceSetParam(Rid space, SpaceParameter param, float value)
    {
        if ((int)param < 0 || (int)param >= s_space.Length) return;
        s_space[(int)param] = value;
        try
        {
            switch (param)
            {
                case SpaceParameter.SolverIterations: Aver.Physics.Physics.SetSetting(Aver.Physics.PhysicsSetting.VelocitySteps, value); break;
                case SpaceParameter.ContactDefaultBias: Aver.Physics.Physics.SetSetting(Aver.Physics.PhysicsSetting.Baumgarte, Math.Clamp(value, 0f, 1f)); break;
                case SpaceParameter.ContactMaxAllowedPenetration: Aver.Physics.Physics.SetSetting(Aver.Physics.PhysicsSetting.PenetrationSlop, value * FabricSpace.CmPerMetre); break;
                case SpaceParameter.ContactMaxSeparation: Aver.Physics.Physics.SetSetting(Aver.Physics.PhysicsSetting.SpeculativeDistance, value * FabricSpace.CmPerMetre); break;
                case SpaceParameter.BodyLinearVelocitySleepThreshold: Aver.Physics.Physics.SetSetting(Aver.Physics.PhysicsSetting.SleepSpeed, value * FabricSpace.CmPerMetre); break;
                case SpaceParameter.BodyTimeToSleep: Aver.Physics.Physics.SetSetting(Aver.Physics.PhysicsSetting.SleepTime, value); break;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    public static float SpaceGetParam(Rid space, SpaceParameter param) => (int)param >= 0 && (int)param < s_space.Length ? s_space[(int)param] : 0f;

    /// <summary>Where a body would get to if it moved (Godot's body_test_motion): fills <paramref name="result"/> and returns true when something is in the way.</summary>
    public static bool BodyTestMotion(Rid body, PhysicsTestMotionParameters3D parameters, PhysicsTestMotionResult3D? result = null)
    {
        PhysBody b = Of(body);
        if (!b.IsValid) return false;
        try
        {
            var flags = Aver.Physics.SpaceQueryFlags.Bodies | (parameters.RecoveryAsCollision ? Aver.Physics.SpaceQueryFlags.RecoveryAsCollision : 0);
            var exclude = new List<PhysBody>();
            foreach (Rid r in parameters.ExcludeBodies) if (r.Id != 0) exclude.Add(new PhysBody((int)r.Id));
            foreach (ulong id in parameters.ExcludeObjects) if (GodotObject.InstanceFromId(id) is CollisionObject3D co && co.Body.IsValid) exclude.Add(co.Body);
            Aver.Physics.MotionTestResult? r2 = b.TestMotion(CollisionObject3D.ToPhysDirection(parameters.Motion, FabricSpace.CmPerMetre), FabricSpace.ToAverLength(parameters.Margin), flags, exclude,
                Math.Max(parameters.MaxCollisions, 1), (FabricPhysics.ToPhysPosition(parameters.From.Origin), FabricPhysics.ToPhysRotation(parameters.From.Basis.Orthonormalized().GetRotationQuaternion())));
            if (r2 is not { } res) return false;
            result?.Load(res);
            return res.Collided;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }
}

/// <summary>The question for PhysicsServer3D.BodyTestMotion (Godot's PhysicsTestMotionParameters3D).</summary>
public class PhysicsTestMotionParameters3D : RefCounted
{
    public Transform3D From { get; set; } = Transform3D.Identity;
    public Vector3 Motion { get; set; }
    public float Margin { get; set; } = 0.001f;
    public int MaxCollisions { get; set; } = 1;
    public bool CollideSeparationRay { get; set; }
    public bool RecoveryAsCollision { get; set; }
    public Collections.Array<Rid> ExcludeBodies { get; set; } = new();
    public Collections.Array<ulong> ExcludeObjects { get; set; } = new();
}

/// <summary>The answer of PhysicsServer3D.BodyTestMotion (Godot's PhysicsTestMotionResult3D).</summary>
public class PhysicsTestMotionResult3D : RefCounted
{
    private Aver.Physics.MotionTestResult _r;
    private bool _has;

    internal void Load(Aver.Physics.MotionTestResult r) { _r = r; _has = true; }

    private Aver.Physics.MotionContact? At(int i) => _has && i >= 0 && i < _r.Contacts.Length ? _r.Contacts[i] : null;

    public Vector3 GetTravel() => _has ? CollisionObject3D.FromPhys(_r.Travel + _r.Recovery, 1f / FabricSpace.CmPerMetre) : Vector3.Zero;
    public Vector3 GetRemainder() => _has ? CollisionObject3D.FromPhys(_r.Remainder, 1f / FabricSpace.CmPerMetre) : Vector3.Zero;
    public int GetCollisionCount() => _has ? _r.Contacts.Length : 0;
    public float GetCollisionSafeFraction() => _has ? _r.SafeFraction : 1f;
    public float GetCollisionUnsafeFraction() => _has ? _r.UnsafeFraction : 1f;
    public Vector3 GetCollisionPoint(int collisionIndex = 0) => At(collisionIndex) is { } c ? FabricPhysics.FromPhysPosition(c.Point) : Vector3.Zero;
    public Vector3 GetCollisionNormal(int collisionIndex = 0) => At(collisionIndex) is { } c ? CollisionObject3D.FromPhys(c.Normal) : Vector3.Zero;
    public Vector3 GetColliderVelocity(int collisionIndex = 0) => At(collisionIndex) is { } c ? CollisionObject3D.FromPhys(c.ColliderVelocity, 1f / FabricSpace.CmPerMetre) : Vector3.Zero;
    public float GetCollisionDepth(int collisionIndex = 0) => At(collisionIndex) is { } c ? c.Depth / FabricSpace.CmPerMetre : 0f;
    public Rid GetColliderRid(int collisionIndex = 0) => At(collisionIndex) is { } c ? new Rid((ulong)c.Collider.Handle) : default;
    public GodotObject? GetCollider(int collisionIndex = 0) => At(collisionIndex) is { } c ? CollisionObject3D.NodeOfBody(c.Collider) : null;
    public ulong GetColliderId(int collisionIndex = 0) => GetCollider(collisionIndex)?.GetInstanceId() ?? 0UL;
    public int GetColliderShape(int collisionIndex = 0) => At(collisionIndex)?.SubShape ?? 0;
    public int GetCollisionLocalShape(int collisionIndex = 0) => 0;
}

// ---- point queries ----

/// <summary>A point query (Godot's PhysicsPointQueryParameters3D).</summary>
public class PhysicsPointQueryParameters3D : RefCounted
{
    public Vector3 Position { get; set; }
    public uint CollisionMask { get; set; } = 0xFFFFFFFF;
    public bool CollideWithBodies { get; set; } = true;
    public bool CollideWithAreas { get; set; }
    public Collections.Array<Rid> Exclude { get; set; } = new();
}

public partial class PhysicsDirectSpaceState3D
{
    /// <summary>The objects whose shape holds the point: a dictionary each with collider, collider_id, rid and shape. A triangle mesh holds nothing.</summary>
    public Collections.Array<Collections.Dictionary> IntersectPoint(PhysicsPointQueryParameters3D parameters, int maxResults = 32)
    {
        var result = new Collections.Array<Collections.Dictionary>();
        if (!NodeHost.Current.IsEngine || maxResults <= 0) return result;
        try
        {
            var exclude = new HashSet<int>();
            foreach (Rid r in parameters.Exclude) exclude.Add((int)r.Id);
            PhysBody[] hits = Aver.Physics.Physics.BodiesAtPoint(FabricPhysics.ToPhysPosition(parameters.Position), parameters.CollisionMask, default,
                parameters.CollideWithAreas ? Aver.Physics.QueryFlags.IncludeSensors : Aver.Physics.QueryFlags.None, maxResults + exclude.Count);
            foreach (PhysBody b in hits)
            {
                if (exclude.Contains(b.Handle) || result.Count >= maxResults) continue;
                CollisionObject3D? node = CollisionObject3D.NodeOfBody(b) as CollisionObject3D;
                if (node == null) continue;
                bool isArea = node is Area3D;
                if (isArea ? !parameters.CollideWithAreas : !parameters.CollideWithBodies) continue;
                var d = new Collections.Dictionary();
                AddCollider(d, b);
                d["shape"] = 0;
                result.Add(d);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        return result;
    }
}

// ---- the tree's collision outline ----

public partial class SceneTree
{
    private bool _debugCollisions;

    /// <summary>Draws the physics shapes as lines over the scene (Godot's debug_collisions_hint). The lines are the engine's own debug renderer's, coloured by how the
    /// body moves, within a few dozen metres of the camera; a shape's debug_color is not used. Needs a physics library built with the debug renderer, else nothing shows.</summary>
    public bool DebugCollisionsHint { get => _debugCollisions; set => _debugCollisions = value; }

    // Once per idle frame from EndPass.
    internal void DrawCollisionOutline()
    {
        if (!_debugCollisions || !NodeHost.Current.IsEngine) return;
        try
        {
            if (!Aver.Physics.PhysicsDebugDraw.Available) return;
            Camera3D? cam = Camera3D.CurrentCamera;
            PhysFloat3 at = cam != null ? FabricPhysics.ToPhysPosition(cam.GlobalPosition) : default;
            int n = Aver.Physics.PhysicsDebugDraw.Collect(Aver.Physics.PhysicsDebugFlags.Bodies | Aver.Physics.PhysicsDebugFlags.StaticTriangles, at, 6000f, 20000);
            if (n <= 0) return;
            ReadOnlySpan<float> l = Aver.Physics.PhysicsDebugDraw.Lines;
            for (int i = 0; i + 11 < l.Length; i += 12)
                Aver.Framework.DebugDraw.Line(new Aver.Scene.Vec3(l[i], l[i + 1], l[i + 2]), new Aver.Scene.Vec3(l[i + 6], l[i + 7], l[i + 8]), new Aver.Scene.Vec3(l[i + 3], l[i + 4], l[i + 5]));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }
}
