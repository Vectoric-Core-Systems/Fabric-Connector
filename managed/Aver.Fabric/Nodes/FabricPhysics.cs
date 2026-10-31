// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// FabricPhysics: builds an Aver.Physics body for a collision node from its CollisionShape3D children, keeps the body-to-node map,
// and turns the G2 contact and area event queues into Godot's body_entered / area_entered signals. Engine side: nothing here runs
// headless; the event routing (Dispatch*) is separate so it can be tested without a physics world.

using Aver.Scripting;
using PhysBody = Aver.Physics.Body;
using PhysShape = Aver.Physics.CollisionShape;
using PhysFloat3 = Aver.Physics.Float3;
using PhysQuat = Aver.Physics.Quaternion;

namespace Aver.Fabric;

internal static class FabricPhysics
{
    private static readonly Dictionary<int, WeakReference<CollisionObject3D>> s_byBody = new();
    private static long s_pumpedFrame = -1;

    internal static void Register(int bodyHandle, CollisionObject3D node)
    {
        lock (s_byBody) s_byBody[bodyHandle] = new WeakReference<CollisionObject3D>(node);
    }

    internal static void Unregister(int bodyHandle)
    {
        lock (s_byBody) s_byBody.Remove(bodyHandle);
    }

    internal static CollisionObject3D? NodeOf(int bodyHandle)
    {
        lock (s_byBody)
            return s_byBody.TryGetValue(bodyHandle, out var wr) && wr.TryGetTarget(out CollisionObject3D? n) && n.IsAlive ? n : null;
    }

    // ---- events -> signals ----

    /// <summary>One area event: a body or another area entered or left an area.</summary>
    internal static void DispatchArea(int areaHandle, int otherHandle, bool entered, bool otherIsArea)
    {
        if (NodeOf(areaHandle) is not Area3D area) return;
        CollisionObject3D? other = NodeOf(otherHandle);
        if (other == null) return;
        if (otherIsArea) { if (other is Area3D a) area.RaiseAreaOverlap(a, entered); }
        else area.RaiseBodyOverlap(other, entered);
    }

    /// <summary>One contact-monitor record: a monitored body began or stopped touching another.</summary>
    internal static void DispatchContact(int bodyHandle, int otherHandle, bool began)
    {
        if (NodeOf(bodyHandle) is not RigidBody3D body) return;
        CollisionObject3D? other = NodeOf(otherHandle);
        if (other == null) return;
        body.RaiseContact(other, began);
    }

    /// <summary>Reads the engine's event queues once per physics frame, whichever node asks first.</summary>
    internal static void Pump()
    {
        long frame = Aver.Framework.Engine.PhysicsFrames;
        if (frame == s_pumpedFrame) return;
        s_pumpedFrame = frame;
        try
        {
            foreach (var ev in Aver.Physics.Physics.AreaEvents)
                DispatchArea(ev.Area.Handle, ev.Other, ev.Entered, ev.OtherIsArea);
            foreach (var ev in Aver.Physics.Physics.BodyContactEvents)
                DispatchContact(ev.Body.Handle, ev.Other.Handle, ev.Began);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // No physics library (a tool or test host): there are no events to deliver.
        }
    }

    // ---- units and axes at the physics boundary ----

    internal static PhysFloat3 ToPhysPosition(Vector3 metres)
    {
        Aver.Scene.Vec3 a = FabricSpace.ToAverPosition(metres);
        return new PhysFloat3(a.X, a.Y, a.Z);
    }

    internal static Vector3 FromPhysPosition(PhysFloat3 cm) => FabricSpace.FromAverPosition(new Aver.Scene.Vec3(cm.X, cm.Y, cm.Z));

    internal static PhysQuat ToPhysRotation(Quaternion q)
    {
        Aver.Scene.Quat a = FabricSpace.ToAverRotation(q);
        return new PhysQuat(a.X, a.Y, a.Z, a.W);
    }

    private static PhysFloat3 ToPhys(Aver.Scene.Vec3 v) => new(v.X, v.Y, v.Z);
    private static PhysQuat ToPhys(Aver.Scene.Quat q) => new(q.X, q.Y, q.Z, q.W);

    // ---- bodies ----

    /// <summary>Builds the Aver body for a collision node from its CollisionShape3D children and returns its handle (0 if none could be made).</summary>
    internal static PhysBody CreateBody(CollisionObject3D node, Aver.Physics.MotionType motion, float massKg, bool sensor)
    {
        var parts = new List<Aver.Physics.ShapeChild>();
        foreach (Node c in node.ChildrenSnapshot())
        {
            if (c is not CollisionShape3D cs || cs.Disabled || cs.Shape == null) continue;
            PhysShape shape = BuildShape(cs.Shape, cs.Scale);
            if (!shape.IsValid) continue;
            Transform3D local = cs.Transform;
            parts.Add(new Aver.Physics.ShapeChild(shape, ToPhys(FabricSpace.ToAverPosition(local.Origin)), ToPhys(FabricSpace.ToAverRotation(local.Basis.Orthonormalized().GetRotationQuaternion()))));
        }
        // Shapes a script added through the shape-owner API (create_shape_owner, shape_owner_add_shape).
        foreach ((Shape3D owned, Transform3D at) in node.OwnedShapes())
        {
            PhysShape shape = BuildShape(owned, at.Basis.GetScale());
            if (!shape.IsValid) continue;
            parts.Add(new Aver.Physics.ShapeChild(shape, ToPhys(FabricSpace.ToAverPosition(at.Origin)), ToPhys(FabricSpace.ToAverRotation(at.Basis.Orthonormalized().GetRotationQuaternion()))));
        }
        if (parts.Count == 0) return PhysBody.None;
        PhysShape whole = parts.Count == 1 && IsIdentity(parts[0]) ? parts[0].Shape : PhysShape.Compound(parts.ToArray());
        // A custom centre of mass (RigidBody3D.center_of_mass_mode custom) moves the body's centre of mass off the shape's; the geometry stays.
        if (node is RigidBody3D rb && rb.CenterOfMassMode == RigidBody3D.CenterOfMassModeEnum.Custom && whole.TryGetInfo(out Aver.Physics.ShapeInfo info))
        {
            PhysFloat3 want = ToPhysPosition(rb.CenterOfMass);   // in the body's own space, as Godot states it
            whole = whole.OffsetCenterOfMass(new PhysFloat3(want.X - info.CenterOfMass.X, want.Y - info.CenterOfMass.Y, want.Z - info.CenterOfMass.Z));
        }
        Transform3D g = node.GlobalTransform;
        return whole.CreateBody(ToPhys(FabricSpace.ToAverPosition(g.Origin)), ToPhys(FabricSpace.ToAverRotation(g.Basis.Orthonormalized().GetRotationQuaternion())), motion, massKg, 0, sensor);
    }

    private static bool IsIdentity(Aver.Physics.ShapeChild c) =>
        c.Position.X == 0 && c.Position.Y == 0 && c.Position.Z == 0 && c.Rotation.X == 0 && c.Rotation.Y == 0 && c.Rotation.Z == 0 && c.Rotation.W == 1;

    private static PhysFloat3 ToPhysDirection(Vector3 v) => CollisionObject3D.ToPhysDirection(v);

    // The answer of a test motion as a KinematicCollision3D: travel, remainder and one hit per contact.
    internal static void FillCollision(KinematicCollision3D into, Aver.Physics.MotionTestResult result)
    {
        into.Hits.Clear();
        into.TravelVector = CollisionObject3D.FromPhys(result.Travel, 1f / FabricSpace.CmPerMetre);
        into.RemainderVector = CollisionObject3D.FromPhys(result.Remainder, 1f / FabricSpace.CmPerMetre);
        foreach (Aver.Physics.MotionContact c in result.Contacts)
        {
            GodotObject? collider = CollisionObject3D.NodeOfBody(c.Collider);
            into.Hits.Add(new KinematicCollision3D.Hit(FromPhysPosition(c.Point), CollisionObject3D.FromPhys(c.Normal), collider, collider?.GetInstanceId() ?? 0,
                CollisionObject3D.FromPhys(c.ColliderVelocity, 1f / FabricSpace.CmPerMetre), c.SubShape, c.Depth / FabricSpace.CmPerMetre));
        }
    }

    // The triangle a ray hit on a ConcavePolygonShape3D body, or -1 (the user data is the triangle number; other shapes carry none).
    internal static int FaceIndexOf(PhysBody body, int subShape)
    {
        try
        {
            Aver.Physics.SurfaceHit h = Aver.Physics.Physics.SubShapeInfo(body, subShape);
            return h.Hit ? h.Triangle : -1;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return -1; }
    }

    // A grid of heights as a triangle mesh: width points along X and depth along Z, one metre apart, centred on the origin (Godot's HeightMapShape3D).
    private static PhysShape BuildHeightMap(HeightMapShape3D hm, Vector3 scale)
    {
        int w = hm.MapWidth, d = hm.MapDepth;
        float[] data = hm.MapData;
        if (w < 2 || d < 2 || data.Length < w * d) return PhysShape.None;
        var verts = new PhysFloat3[w * d];
        for (int z = 0; z < d; z++)
            for (int x = 0; x < w; x++)
                verts[z * w + x] = ToPhysPosition(new Vector3((x - (w - 1) * 0.5f) * scale.X, data[z * w + x] * scale.Y, (z - (d - 1) * 0.5f) * scale.Z));
        var idx = new List<int>((w - 1) * (d - 1) * 6);
        for (int z = 0; z + 1 < d; z++)
            for (int x = 0; x + 1 < w; x++)
            {
                int a = z * w + x, b = a + 1, c = a + w, e = c + 1;
                // The axis map mirrors the space: wound b-c-a and b-e-c face up.
                idx.AddRange(new[] { a, c, b, b, c, e });
            }
        return PhysShape.Mesh(verts, idx.ToArray());
    }

    // A Godot shape resource as a physics shape in Aver units (metres to centimetres; the axes of the shape follow its node).
    internal static PhysShape BuildShape(Shape3D s, Vector3 scale)
    {
        const float cm = FabricSpace.CmPerMetre;
        switch (s)
        {
            case BoxShape3D b:
            {
                // Godot's size is (x, y, z) with Y up; the Aver half extents are (depth, width, height) = (z, x, y).
                Vector3 h = b.Size * 0.5f;
                return PhysShape.Box(new PhysFloat3(h.Z * cm * scale.Z, h.X * cm * scale.X, h.Y * cm * scale.Y));
            }
            case SphereShape3D sp:
                return PhysShape.Sphere(sp.Radius * cm * Math.Max(scale.X, Math.Max(scale.Y, scale.Z)));
            case CapsuleShape3D cap:
                // Godot's capsule axis is Y, which is Aver Z, the physics capsule's own axis; both heights are the total.
                return PhysShape.Capsule(cap.Radius * cm, cap.Height * cm);
            case CylinderShape3D cyl:
                return PhysShape.Cylinder(cyl.Radius * cm, cyl.Height * cm);
            case ConvexPolygonShape3D convex:
            {
                var pts = new PhysFloat3[convex.Points.Length];
                for (int i = 0; i < pts.Length; i++) pts[i] = ToPhysPosition(convex.Points[i] * scale);
                return PhysShape.ConvexHull(pts);
            }
            case ConcavePolygonShape3D concave:
            {
                // Triangles, three points each. The axis map mirrors the space, so each triangle's winding is reversed to keep its normal outward.
                int n = concave.Data.Length / 3 * 3;
                var verts = new PhysFloat3[n];
                var idx = new int[n];
                for (int i = 0; i < n; i++) verts[i] = ToPhysPosition(concave.Data[i] * scale);
                for (int t = 0; t < n; t += 3) { idx[t] = t; idx[t + 1] = t + 2; idx[t + 2] = t + 1; }
                // The triangle's number rides along as its user data: a ray hit reads it back as face_index.
                var faces = new int[n / 3];
                for (int t = 0; t < faces.Length; t++) faces[t] = t;
                return PhysShape.Mesh(verts, idx, triUserData: faces);
            }
            case HeightMapShape3D hm:
                return BuildHeightMap(hm, scale);
            case SeparationRayShape3D ray:
                // Godot's ray runs along the shape's local +Z; the engine's stand-in is a thin capsule along that direction.
                return PhysShape.SeparationRay(ray.Length * cm, ToPhysDirection(Vector3.Back));
            case WorldBoundaryShape3D wb:
            {
                Aver.Scene.Vec3 normal = FabricSpace.ToAverDirection(new Vector3(wb.Plane.Normal.X, wb.Plane.Normal.Y, wb.Plane.Normal.Z));
                return PhysShape.Plane(ToPhys(normal), wb.Plane.D * cm, 100000f);
            }
            default:
                return PhysShape.None;
        }
    }
}
