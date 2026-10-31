// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Parity wave 5 (physics, geometry, navigation): the joint nodes, SoftBody3D's weld map and pins, the primitive meshes (checked against vertices dumped from Godot 4.7.2),
// lines and points as crossed quads, shape owners and shape ids, the physics server's space parameters, test motion and point queries, Geometry3D / Geometry2D / TriangleMesh,
// physics interpolation modes, and the navigation leftovers. Nothing here needs the engine: what does is a physics world, reported as unverified in docs/plans/fabric.

using Aver.Fabric;
using Collections = Aver.Fabric.Collections;

static class Wave5Tests
{
    public static int RunAll() => T.Suite(() =>
    {
        T.Run("wave 5: joint nodes keep Godot's parameters, defaults and names", Joints);
        T.Run("wave 5: the primitive meshes match Godot's vertices and wind clockwise", PrimitiveVertices);
        T.Run("wave 5: the primitive meshes enclose the volume Godot's do", PrimitiveVolumes);
        T.Run("wave 5: lines and points are drawn as crossed quads", ThinPrimitives);
        T.Run("wave 5: SoftBody3D's weld map, pins and forces", SoftBody);
        T.Run("wave 5: shape owners, shape ids, new shape classes and debug meshes", Shapes);
        T.Run("wave 5: space parameters, test motion, point queries and test_move without a world", ServerExtras);
        T.Run("wave 5: Geometry3D, Geometry2D and TriangleMesh", GeometryHelpers);
        T.Run("wave 5: physics interpolation modes and the step cap", Interpolation);
        T.Run("wave 5: RigidBody3D settings and the navigation leftovers", BodiesAndNav);
    });

    private static void Joints()
    {
        var hinge = new HingeJoint3D();
        T.Check("a hinge's limit is off and spans a quarter turn each way", !hinge.GetFlag(HingeJoint3D.Flag.UseLimit) && Math.Abs(hinge.GetParam(HingeJoint3D.Param.LimitUpper) - MathF.PI / 2) < 1e-5f
            && Math.Abs(hinge.GetParam(HingeJoint3D.Param.LimitLower) + MathF.PI / 2) < 1e-5f);
        hinge.Set("angular_limit/upper", 0.5f);
        hinge.Set("angular_limit/enable", true);
        hinge.Set("motor/target_velocity", 3f);
        T.Check("a scene's slash names set the same parameters", hinge.GetParam(HingeJoint3D.Param.LimitUpper) == 0.5f && hinge.GetFlag(HingeJoint3D.Flag.UseLimit)
            && hinge.GetParam(HingeJoint3D.Param.MotorTargetVelocity) == 3f);
        T.Check("and read back by those names", hinge.Get("angular_limit/upper").AsSingle() == 0.5f && hinge.Get("angular_limit/enable").AsBool());
        hinge.SetFlag(HingeJoint3D.Flag.EnableMotor, true);
        T.Check("flags", hinge.GetFlag(HingeJoint3D.Flag.EnableMotor));

        var pin = new PinJoint3D();
        T.Check("a pin's defaults: bias 0.3, damping 1, impulse clamp 0", pin.GetParam(PinJoint3D.Param.Bias) == 0.3f && pin.GetParam(PinJoint3D.Param.Damping) == 1f && pin.GetParam(PinJoint3D.Param.ImpulseClamp) == 0f);
        pin.Set("params/bias", 0.9f);
        T.Check("params/bias", pin.GetParam(PinJoint3D.Param.Bias) == 0.9f);

        var slider = new SliderJoint3D();
        T.Check("a slider's limit is one metre each way and its angular limit is shut", slider.GetParam(SliderJoint3D.Param.LinearLimitUpper) == 1f && slider.GetParam(SliderJoint3D.Param.LinearLimitLower) == -1f
            && slider.GetParam(SliderJoint3D.Param.AngularLimitUpper) == 0f);
        slider.Set("linear_limit/upper_distance", 2.5f);
        T.Check("linear_limit/upper_distance", slider.GetParam(SliderJoint3D.Param.LinearLimitUpper) == 2.5f);

        var cone = new ConeTwistJoint3D();
        T.Check("a cone twist's spans: swing a quarter of a half turn, twist a half turn", Math.Abs(cone.GetParam(ConeTwistJoint3D.Param.SwingSpan) - MathF.PI / 4) < 1e-5f && Math.Abs(cone.GetParam(ConeTwistJoint3D.Param.TwistSpan) - MathF.PI) < 1e-5f);

        var six = new Generic6DOFJoint3D();
        T.Check("a six-DOF is locked until limits are opened: every limit enabled, upper and lower 0", six.GetFlagX(Generic6DOFJoint3D.Flag.EnableLinearLimit) && six.GetFlagZ(Generic6DOFJoint3D.Flag.EnableAngularLimit)
            && six.GetParamY(Generic6DOFJoint3D.Param.LinearUpperLimit) == 0f);
        six.Set("linear_limit_x/upper_distance", 4f);
        six.Set("angular_motor_z/target_velocity", 2f);
        six.Set("linear_spring_y/enabled", true);
        T.Check("the scene's per-axis names reach the right axis", six.GetParamX(Generic6DOFJoint3D.Param.LinearUpperLimit) == 4f && six.GetParamY(Generic6DOFJoint3D.Param.LinearUpperLimit) == 0f
            && six.GetParamZ(Generic6DOFJoint3D.Param.AngularMotorTargetVelocity) == 2f && six.GetFlagY(Generic6DOFJoint3D.Flag.EnableLinearSpring));

        T.Check("a joint exists with no engine: no body, no physics id, and it excludes its bodies from colliding by default", hinge.GetRid().Id == 0 && hinge.ExcludeNodesFromCollision && hinge.NodeA.IsEmpty);
        var root = new Node3D();
        SceneTree.Instance.Root.AddChild(root);
        root.AddChild(hinge);
        hinge.NodeA = new NodePath("../A");
        T.Check("it joins a tree and keeps the paths it was given", hinge.IsInsideTree() && hinge.NodeA.ToString() == "../A" && !hinge.IsBuilt);
    }

    private static Vector3[] Positions(PrimitiveMesh m) => m.Surface().Vertices;

    private static bool Near(Vector3 a, Vector3 b, float eps = 1e-5f) => a.DistanceTo(b) < eps;

    private static void PrimitiveVertices()
    {
        // Dumped from Godot 4.7.2: PlaneMesh size (4, 2) subdivide 1 and 1, SphereMesh radial_segments 4, rings 2.
        var plane = new PlaneMesh { Size = new Vector2(4, 2), SubdivideWidth = 1, SubdivideDepth = 1 };
        Vector3[] p = Positions(plane);
        Vector3[] expect =
        {
            new(2, 0, 1), new(0, 0, 1), new(-2, 0, 1), new(2, 0, 0), new(0, 0, 0), new(-2, 0, 0), new(2, 0, -1), new(0, 0, -1), new(-2, 0, -1),
        };
        bool same = p.Length == expect.Length;
        for (int i = 0; same && i < expect.Length; i++) same = Near(p[i], expect[i]);
        T.Check("a plane's nine vertices are in Godot's order: +X +Z first, X then Z falling", same);
        MeshSurface ps = plane.Surface();
        T.Check("with Godot's UVs falling from (1, 1)", Math.Abs(ps.Uv![0].X - 1) < 1e-6f && Math.Abs(ps.Uv[0].Y - 1) < 1e-6f && Math.Abs(ps.Uv[4].X - 0.5f) < 1e-6f && Math.Abs(ps.Uv[8].Y) < 1e-6f);
        T.Check("normals up and eight triangles", ps.Normals![0].Y > 0.99f && ps.Indices!.Length == 24);

        var sphere = new SphereMesh { RadialSegments = 4, Rings = 2 };
        Vector3[] s = Positions(sphere);
        T.Check("a sphere has rings + 2 rows of radial + 1 vertices (Godot: 20)", s.Length == 20);
        T.Check("the first row is the pole", Near(s[0], new Vector3(0, 0.5f, 0)) && Near(s[4], new Vector3(0, 0.5f, 0)));
        T.Check("the next row starts toward +Z and turns toward +X", Near(s[5], new Vector3(0, 0.25f, 0.433013f), 1e-4f) && Near(s[6], new Vector3(0.433013f, 0.25f, 0), 1e-4f) && Near(s[7], new Vector3(0, 0.25f, -0.433013f), 1e-4f));

        T.Check("a box's size, subdivisions and 24 vertices", Positions(new BoxMesh { Size = new Vector3(1, 2, 3) }).Length == 24 && new BoxMesh { Size = new Vector3(1, 2, 3), SubdivideWidth = 1 }.Surface().Vertices.Length == 32);
        var quad = new QuadMesh();
        T.Check("a quad faces +Z and is one metre across", quad.Surface().Vertices.Length == 4 && quad.Surface().Normals![0].Z > 0.99f && Math.Abs(quad.GetAabb().Size.X - 1f) < 1e-5f);
        var shaded = new SphereMesh { FlipFaces = true };
        T.Check("flip_faces turns the normals", shaded.Surface().Normals![0].LengthSquared() > 0.5f && new SphereMesh().Surface().Normals![40].Dot(shaded.Surface().Normals![40]) < 0);
    }

    // The volume of a closed mesh from its triangles with the counter-clockwise formula: negative for Godot's clockwise front faces.
    private static float SignedVolume(Vector3[] faces)
    {
        float v = 0f;
        for (int i = 0; i + 2 < faces.Length; i += 3) v += faces[i].Dot(faces[i + 1].Cross(faces[i + 2])) / 6f;
        return v;
    }

    private static void PrimitiveVolumes()
    {
        void Volume(string what, PrimitiveMesh m, float expected, float tolerance)
        {
            float v = SignedVolume(m.GetFaces());
            T.Check(what + $" encloses about {expected} (got {-v}) with clockwise front faces", v < 0 && Math.Abs(-v - expected) <= tolerance);
        }
        Volume("a 1 by 2 by 3 box", new BoxMesh { Size = new Vector3(1, 2, 3) }, 6f, 1e-4f);
        Volume("a sphere of radius 1", new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 48, Rings = 24 }, 4f / 3f * MathF.PI, 0.1f);
        Volume("a cylinder of radius 1 and height 2", new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 2f, RadialSegments = 64 }, MathF.PI * 2f, 0.08f);
        Volume("a cone", new CylinderMesh { TopRadius = 0f, BottomRadius = 1f, Height = 3f, RadialSegments = 64 }, MathF.PI, 0.08f);
        Volume("a capsule of radius 0.5 and height 2", new CapsuleMesh { Radius = 0.5f, Height = 2f, RadialSegments = 48, Rings = 12 }, MathF.PI * 0.25f * 1f + 4f / 3f * MathF.PI * 0.125f, 0.05f);
        Volume("a torus with radii 0.5 and 1.5", new TorusMesh { InnerRadius = 0.5f, OuterRadius = 1.5f, Rings = 48, RingSegments = 24 }, 2f * MathF.PI * MathF.PI * 1f * 0.25f, 0.2f);
        Volume("a prism 2 wide, 1 tall and 3 deep", new PrismMesh { Size = new Vector3(2, 1, 3), LeftToRight = 0.25f }, 3f, 1e-3f);

        var capsule = new CapsuleMesh();
        T.Check("a capsule is its height tall", Math.Abs(capsule.GetAabb().Size.Y - 2f) < 1e-4f && Math.Abs(capsule.GetAabb().Size.X - 1f) < 1e-3f);
        T.Check("its trimesh and convex shapes come from its triangles", capsule.CreateTrimeshShape() is { Data.Length: > 0 } && capsule.CreateConvexShape() is { Points.Length: > 0 });
        var asset = new BoxMesh { ResourcePath = "res://Meshes/Rock.ocmesh" };
        T.Check("a mesh that names an asset is drawn from the asset", asset.EngineAssetPath == null);
        var change = new BoxMesh();
        int changes = 0;
        change.Changed += () => changes++;
        change.Size = new Vector3(2, 2, 2);
        T.Check("a changed property says so, and the triangles follow", changes == 1 && Math.Abs(change.GetAabb().Size.X - 2f) < 1e-5f);
    }

    private static void ThinPrimitives()
    {
        var v = new[] { new Vector3(0, 0, 0), new Vector3(0, 0, -3), new Vector3(1, 0, 0) };
        var arrays = new Collections.Array();
        for (int i = 0; i < (int)Mesh.ArrayType.Max; i++) arrays.Add(default);
        arrays[(int)Mesh.ArrayType.Vertex] = (Variant)v;
        var lines = new ArrayMesh();
        lines.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
        EngineSurface? e = lines.Surfaces[0].ToEngine(out string why);
        T.Check("a line is two crossed quads, each drawn from both sides: 8 vertices, 24 indices (one segment of a Lines surface of three vertices)", e != null && e.Pos.Length / 3 == 8 && e.Idx!.Length == 24 && why == "");
        var strip = new ArrayMesh();
        strip.AddSurfaceFromArrays(Mesh.PrimitiveType.LineStrip, arrays);
        EngineSurface? es = strip.Surfaces[0].ToEngine(out _);
        T.Check("a strip of three vertices is two segments", es != null && es.Pos.Length / 3 == 16 && es.Idx!.Length == 48);
        Mesh.ThinPrimitiveWidth = 0.1f;
        EngineSurface? wide = lines.Surfaces[0].ToEngine(out _);
        float span = 0f;
        for (int i = 0; wide != null && i < 8; i++) span = Math.Max(span, Math.Abs(wide.Pos[i * 3 + 1]));   // engine Y is Godot's X
        Mesh.ThinPrimitiveWidth = 0.02f;
        T.Check("the width is the mesh's setting, in metres (0.1 m is 10 cm across: 5 cm out from the line)", Math.Abs(span - 5f) < 0.6f);
        var pts = new ArrayMesh();
        pts.AddSurfaceFromArrays(Mesh.PrimitiveType.Points, arrays);
        EngineSurface? ep = pts.Surfaces[0].ToEngine(out _);
        T.Check("a point is three crossed squares", ep != null && ep.Pos.Length / 3 == 3 * 12 && ep.Idx!.Length == 3 * 36);
        var degenerate = new ArrayMesh();
        var same = new Collections.Array();
        for (int i = 0; i < (int)Mesh.ArrayType.Max; i++) same.Add(default);
        same[(int)Mesh.ArrayType.Vertex] = (Variant)new[] { Vector3.Zero, Vector3.Zero };
        degenerate.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, same);
        T.Check("a segment of no length draws nothing and says why", degenerate.Surfaces[0].ToEngine(out string reason) == null && reason.Contains("no line"));
    }

    private static void SoftBody()
    {
        // A plane of 3 by 3 vertices welds to itself; a sphere's repeated vertices (the pole and the seam) weld away; a plane with a doubled vertex too.
        var plane = new PlaneMesh { Size = new Vector2(1, 1), SubdivideWidth = 2, SubdivideDepth = 2 };
        SoftBody3D.Weld wp = SoftBody3D.BuildWeld(plane.Surface().Vertices, plane.Surface().Indices);
        T.Check("a 4 by 4 plane has 16 points and 18 triangles", wp.Points.Length == 16 && wp.Triangles.Length == 18 * 3 && wp.RenderToPoint.Length == 16);
        T.Check("with 33 edges, once each, and it is not closed", wp.Edges.Length == 33 * 2 && !wp.Closed);
        T.Check("an edge is as long as its two points are apart", Math.Abs(wp.EdgeLength[0] - wp.Points[wp.Edges[0]].DistanceTo(wp.Points[wp.Edges[1]])) < 1e-6f);

        var sphere = new SphereMesh { RadialSegments = 8, Rings = 4 };
        SoftBody3D.Weld ws = SoftBody3D.BuildWeld(sphere.Surface().Vertices, sphere.Surface().Indices);
        T.Check("a sphere welds its seam and poles: points are fewer than mesh vertices", ws.Points.Length < sphere.Surface().Vertices.Length && ws.Points.Length == 2 + 4 * 8);
        T.Check("and it is closed: every edge has two triangles", ws.Closed);
        T.Check("two vertices at one place share a point", ws.RenderToPoint[0] == ws.RenderToPoint[3] && ws.RenderToPoint[0] != ws.RenderToPoint[10]);

        T.Check("stiffness 1 is inextensible and 0 the softest", SoftBody3D.EdgeCompliance(1f) == 0f && SoftBody3D.EdgeCompliance(0f) > SoftBody3D.EdgeCompliance(0.5f) && SoftBody3D.EdgeCompliance(0.5f) > 0f);
        T.Check("the mass is shared by the free points and a pin has none", Math.Abs(SoftBody3D.PointInverseMass(10f, 20, false) - 2f) < 1e-6f && SoftBody3D.PointInverseMass(10f, 20, true) == 0f);
        Vector3[] normals = SoftBody3D.SmoothNormals(plane.Surface().Vertices, plane.Surface().Indices);
        T.Check("smooth normals of a flat plane point up", normals.All(n => n.Y > 0.99f));

        var cloth = new SoftBody3D { Mesh = plane };
        T.Check("Godot's defaults", cloth.SimulationPrecision == 5 && cloth.TotalMass == 1f && cloth.LinearStiffness == 0.5f && cloth.PressureCoefficient == 0f && cloth.DampingCoefficient == 0.01f && cloth.DragCoefficient == 0f && cloth.RayPickable);
        cloth.SetPointPinned(3, true);
        cloth.SetPointPinned(5, true, new NodePath("../Hook"));
        T.Check("pins are kept by vertex index", cloth.IsPointPinned(3) && cloth.IsPointPinned(5) && !cloth.IsPointPinned(4));
        cloth.SetPointPinned(3, false);
        T.Check("and freed", !cloth.IsPointPinned(3) && cloth.IsPointPinned(5));
        cloth.Set("attachments/2/point_index", 7);
        cloth.Set("attachments/2/spatial_attachment_path", new NodePath("../Other"));
        cloth.Set("attachments/2/offset", new Vector3(0, 1, 0));
        T.Check("a scene's attachments/N names pin too", cloth.IsPointPinned(7));
        T.Check("the layer helpers work as a body's", cloth.GetCollisionLayerValue(1) && !cloth.GetCollisionLayerValue(2));
        cloth.ApplyImpulse(2, new Vector3(1, 0, 0));
        cloth.ApplyCentralForce(Vector3.Up);
        T.Check("forces before a body exists are quiet and the point reads at its mesh place", cloth.GetPointTransform(2).DistanceTo(plane.Surface().Vertices[2]) < 1e-4f && cloth.GetPhysicsRid().Id == 0);
    }

    private static void Shapes()
    {
        var body = new StaticBody3D();
        var owner = new Node3D();
        uint id = body.CreateShapeOwner(owner);
        var box = new BoxShape3D { Size = new Vector3(2, 2, 2) };
        body.ShapeOwnerAddShape(id, box);
        T.Check("a script's owner holds the shapes it was given", body.ShapeOwnerGetShapeCount(id) == 1 && body.ShapeOwnerGetShape(id, 0) == box && body.ShapeOwnerGetOwner(id) == owner);
        body.ShapeOwnerSetTransform(id, new Transform3D(Basis.Identity, new Vector3(1, 0, 0)));
        body.ShapeOwnerSetDisabled(id, true);
        T.Check("with its transform and its disabled flag", body.ShapeOwnerGetTransform(id).Origin.X == 1f && body.IsShapeOwnerDisabled(id) && !body.OwnedShapes().Any());
        body.ShapeOwnerSetDisabled(id, false);
        T.Check("an enabled owner's shapes are the body's", body.OwnedShapes().Count() == 1);
        T.Check("the shape index and its owner agree", body.ShapeOwnerGetShapeIndex(id, 0) == 0 && body.ShapeFindOwner(0) == id && body.GetShapeOwners().Contains((int)id));

        var child = new CollisionShape3D { Shape = new SphereShape3D() };
        body.AddChild(child);
        T.Check("a CollisionShape3D child is an owner too, numbered from 1, and comes first", body.GetShapeOwners()[0] == 1 && body.ShapeOwnerGetShape(1, 0) == child.Shape && body.ShapeFindOwner(0) == 1 && body.ShapeFindOwner(1) == id);
        body.ShapeOwnerClearShapes(id);
        body.RemoveShapeOwner(id);
        T.Check("an owner can be cleared and removed", body.ShapeOwnerGetShapeCount(id) == 0 && body.GetShapeOwners().Length == 1);

        Rid rid = box.GetRid();
        T.Check("a shape has one id for good, and a query takes the shape by it", rid.Id != 0 && box.GetRid() == rid && new PhysicsShapeQueryParameters3D { ShapeRid = rid }.Shape == box);
        T.Check("a body's id is another thing", rid.Id > (1UL << 32));
        var query = new PhysicsShapeQueryParameters3D { Shape = box };
        T.Check("and reads back", query.ShapeRid == rid);

        var hm = new HeightMapShape3D { MapWidth = 3, MapDepth = 3 };
        T.Check("a height map resizes its data with its size", hm.MapData.Length == 9 && hm.GetMaxHeight() == 0f);
        hm.MapData = new float[] { 0, 1, 0, 2, 0, 0, 0, 0, -1 };
        T.Check("and knows its range", hm.GetMinHeight() == -1f && hm.GetMaxHeight() == 2f && hm.GetDebugMesh() != null);

        var ray = new SeparationRayShape3D { Length = 2f };
        T.Check("a separation ray has a length and a debug line", ray.Length == 2f && ray.GetDebugMesh()!.GetSurfaceCount() == 1);
        T.Check("Godot 3's extents are half the size", Math.Abs(new BoxShape3D { Extents = new Vector3(1, 2, 3) }.Size.Z - 6f) < 1e-6f && new BoxShape3D { Size = new Vector3(2, 4, 6) }.Extents.Y == 2f);

        var convexPoly = new CollisionPolygon3D { Depth = 2f, Polygon = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) } };
        T.Check("a convex polygon makes a convex shape of its corners at both depths", convexPoly.Shape is ConvexPolygonShape3D { Points.Length: 8 } cp && cp.Points.Any(p => p.Z == 1f) && cp.Points.Any(p => p.Z == -1f));
        var concave = new CollisionPolygon3D { Depth = 1f, Polygon = new[] { new Vector2(0, 0), new Vector2(2, 0), new Vector2(2, 2), new Vector2(1, 1), new Vector2(0, 2) } };
        T.Check("an L or arrow polygon makes a triangle mesh: both caps and the sides", concave.Shape is ConcavePolygonShape3D { Data.Length: > 0 } cc && cc.Data.Length % 3 == 0 && cc.Data.Length == 3 * (2 * 3 + 5 * 2));

        var boxDebug = new BoxShape3D().GetDebugMesh();
        T.Check("a box's outline is twelve edges", boxDebug != null && boxDebug.SurfaceGetArrayLen(0) == 24 && boxDebug.SurfaceGetPrimitiveType(0) == Mesh.PrimitiveType.Lines);
        T.Check("a sphere, a capsule and a cylinder have outlines too", new SphereShape3D().GetDebugMesh() != null && new CapsuleShape3D().GetDebugMesh() != null && new CylinderShape3D().GetDebugMesh() != null);
        var tri = new ConcavePolygonShape3D { Data = new[] { Vector3.Zero, Vector3.Right, Vector3.Up } };
        T.Check("a mesh shape's outline is its triangle edges", tri.GetDebugMesh()!.SurfaceGetArrayLen(0) == 6 && tri.GetFaces().Length == 3);
        var coll = new CollisionShape3D();
        T.Check("a shape node's debug colour is 'by kind' until set", coll.DebugColor.A == 0f && coll.DebugFill);
        SceneTree.Instance.DebugCollisionsHint = true;
        T.Check("the tree's collision outline can be asked for", SceneTree.Instance.DebugCollisionsHint);
    }

    private static void ServerExtras()
    {
        Rid space = SceneTree.Instance.Root.FindWorld3D().Space;
        T.Check("the space starts at Godot's defaults", PhysicsServer3D.SpaceGetParam(space, PhysicsServer3D.SpaceParameter.SolverIterations) == 16f
            && PhysicsServer3D.SpaceGetParam(space, PhysicsServer3D.SpaceParameter.ContactDefaultBias) == 0.8f);
        PhysicsServer3D.SpaceSetParam(space, PhysicsServer3D.SpaceParameter.SolverIterations, 4f);
        PhysicsServer3D.SpaceSetParam(space, PhysicsServer3D.SpaceParameter.ContactDefaultBias, 0.2f);
        T.Check("and reads back what was set", PhysicsServer3D.SpaceGetParam(space, PhysicsServer3D.SpaceParameter.SolverIterations) == 4f && PhysicsServer3D.SpaceGetParam(space, PhysicsServer3D.SpaceParameter.ContactDefaultBias) == 0.2f);
        PhysicsServer3D.SpaceSetParam(space, PhysicsServer3D.SpaceParameter.SolverIterations, 16f);
        PhysicsServer3D.SpaceSetParam(space, PhysicsServer3D.SpaceParameter.ContactDefaultBias, 0.8f);

        var parameters = new PhysicsTestMotionParameters3D { Motion = Vector3.Forward, Margin = 0.01f, MaxCollisions = 3 };
        var result = new PhysicsTestMotionResult3D();
        T.Check("test motion defaults and, without a body, finds nothing", parameters.Margin == 0.01f && parameters.MaxCollisions == 3 && !parameters.RecoveryAsCollision && !PhysicsServer3D.BodyTestMotion(default, parameters, result));
        T.Check("an empty result", result.GetCollisionCount() == 0 && result.GetTravel() == Vector3.Zero && result.GetCollisionSafeFraction() == 1f && result.GetCollider() == null);

        var point = new PhysicsPointQueryParameters3D { Position = Vector3.One };
        T.Check("a point query's defaults are bodies on, areas off", point.CollideWithBodies && !point.CollideWithAreas && point.CollisionMask == 0xFFFFFFFF);
        T.Check("and, with no world, hits nothing", SceneTree.Instance.Root.World3D.DirectSpaceState.IntersectPoint(point).Count == 0);

        var b = new CharacterBody3D();
        var hit = new KinematicCollision3D();
        T.Check("test_move without a body is false", !b.TestMove(Transform3D.Identity, Vector3.Down, hit));
        T.Check("the ray's face index is -1 where there is no mesh body", FabricPhysics.FaceIndexOf(default, 0) == -1);
    }

    private static void GeometryHelpers()
    {
        var a = new Vector3(0, 0, 0); var bb = new Vector3(1, 0, 0); var c = new Vector3(0, 1, 0);
        Variant hit = Geometry3D.RayIntersectsTriangle(new Vector3(0.25f, 0.25f, 1), new Vector3(0, 0, -1), a, bb, c);
        T.Check("a ray through a triangle meets it", hit.VariantType != Variant.Type.Nil && hit.AsVector3().DistanceTo(new Vector3(0.25f, 0.25f, 0)) < 1e-5f);
        T.Check("a ray beside it does not", Geometry3D.RayIntersectsTriangle(new Vector3(2, 2, 1), new Vector3(0, 0, -1), a, bb, c).VariantType == Variant.Type.Nil);
        T.Check("a segment too short misses", Geometry3D.SegmentIntersectsTriangle(new Vector3(0.25f, 0.25f, 1), new Vector3(0.25f, 0.25f, 0.5f), a, bb, c).VariantType == Variant.Type.Nil
            && Geometry3D.SegmentIntersectsTriangle(new Vector3(0.25f, 0.25f, 1), new Vector3(0.25f, 0.25f, -1), a, bb, c).VariantType != Variant.Type.Nil);

        Vector3[] sph = Geometry3D.SegmentIntersectsSphere(new Vector3(-5, 0, 0), new Vector3(5, 0, 0), Vector3.Zero, 1f);
        T.Check("a segment through a sphere: where it enters and the normal there", sph.Length == 2 && sph[0].DistanceTo(new Vector3(-1, 0, 0)) < 1e-4f && sph[1].DistanceTo(new Vector3(-1, 0, 0)) < 1e-4f);
        Vector3[] cyl = Geometry3D.SegmentIntersectsCylinder(new Vector3(-5, 0, 0), new Vector3(5, 0, 0), 2f, 1f);
        T.Check("a segment through a cylinder", cyl.Length == 2 && cyl[0].DistanceTo(new Vector3(-1, 0, 0)) < 1e-4f && cyl[1].X < -0.99f);
        T.Check("one above it misses", Geometry3D.SegmentIntersectsCylinder(new Vector3(-5, 3, 0), new Vector3(5, 3, 0), 2f, 1f).Length == 0);
        Collections.Array<Plane> boxPlanes = Geometry3D.BuildBoxPlanes(new Vector3(1, 1, 1));
        Vector3[] conv = Geometry3D.SegmentIntersectsConvex(new Vector3(0, 5, 0), new Vector3(0, -5, 0), boxPlanes);
        T.Check("a segment through the box planes enters the top face", conv.Length == 2 && conv[0].DistanceTo(new Vector3(0, 1, 0)) < 1e-4f && conv[1].Y > 0.99f);
        T.Check("a box has eight corners", Geometry3D.ComputeConvexMeshPoints(boxPlanes).Length == 8);
        T.Check("a cylinder's planes bound a prism of corners", Geometry3D.ComputeConvexMeshPoints(Geometry3D.BuildCylinderPlanes(1f, 2f, 8)).Length == 16);

        T.Check("the nearest point of a segment is clamped to its ends", Geometry3D.GetClosestPointToSegment(new Vector3(5, 1, 0), Vector3.Zero, new Vector3(2, 0, 0)).DistanceTo(new Vector3(2, 0, 0)) < 1e-5f);
        Vector3[] pair = Geometry3D.GetClosestPointsBetweenSegments(new Vector3(0, 0, 0), new Vector3(2, 0, 0), new Vector3(1, 1, -1), new Vector3(1, 1, 1));
        T.Check("the nearest points between two crossing segments are a unit apart", pair.Length == 2 && Math.Abs(pair[0].DistanceTo(pair[1]) - 1f) < 1e-4f);

        Vector3[] quad = { new(-1, 0, 0), new(1, 0, 0), new(1, 1, 0), new(-1, 1, 0) };
        Vector3[] clipped = Geometry3D.ClipPolygon(quad, new Plane(new Vector3(1, 0, 0), 0f));
        T.Check("clipping keeps what is behind the plane: half the quad", clipped.Length == 4 && clipped.All(p => p.X <= 1e-5f));

        var square = new[] { new Vector2(0, 0), new Vector2(4, 0), new Vector2(4, 4), new Vector2(0, 4) };
        T.Check("a square triangulates into two triangles", Geometry2D.TriangulatePolygon(square).Length == 6);
        var arrow = new[] { new Vector2(0, 0), new Vector2(2, 0), new Vector2(2, 2), new Vector2(1, 1), new Vector2(0, 2) };
        int[] t = Geometry2D.TriangulatePolygon(arrow);
        float area = 0f;
        for (int i = 0; i + 2 < t.Length; i += 3) area += MathF.Abs((arrow[t[i + 1]] - arrow[t[i]]).Cross(arrow[t[i + 2]] - arrow[t[i]])) * 0.5f;
        T.Check("a concave polygon's triangles cover its area (3)", t.Length == 9 && Math.Abs(area - 3f) < 1e-4f);
        T.Check("points in and out of a polygon", Geometry2D.IsPointInPolygon(new Vector2(2, 2), square) && !Geometry2D.IsPointInPolygon(new Vector2(5, 2), square));
        T.Check("the hull of a square with a point inside is the square, closed", Geometry2D.ConvexHull(new[] { new Vector2(0, 0), new Vector2(2, 0), new Vector2(2, 2), new Vector2(0, 2), new Vector2(1, 1) }).Length == 5);
        T.Check("segments cross at a point", Geometry2D.SegmentIntersectsSegment(new Vector2(0, 0), new Vector2(2, 2), new Vector2(0, 2), new Vector2(2, 0)).AsVector2().DistanceTo(new Vector2(1, 1)) < 1e-5f
            && Geometry2D.SegmentIntersectsSegment(new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1)).VariantType == Variant.Type.Nil);

        var mesh = new TriangleMesh();
        T.Check("faces must be whole triangles", !mesh.CreateFromFaces(new[] { a, bb }) && mesh.CreateFromFaces(new[] { a, bb, c, new Vector3(0, 0, -1), new Vector3(1, 0, -1), new Vector3(0, 1, -1) }));
        Collections.Dictionary ray = mesh.IntersectRay(new Vector3(0.2f, 0.2f, 5), Vector3.Forward);
        T.Check("a ray hits the nearer face: position, normal and face index", ray.Count == 3 && ((Vector3)ray["position"].AsVector3()).DistanceTo(new Vector3(0.2f, 0.2f, 0)) < 1e-4f && ray["face_index"].AsInt32() == 0);
        T.Check("a segment short of the faces finds nothing", mesh.IntersectSegment(new Vector3(0.2f, 0.2f, 5), new Vector3(0.2f, 0.2f, 3)).Count == 0);
    }

    private static void Interpolation()
    {
        var tree = SceneTree.Instance;
        T.Check("physics interpolation is off until the project or a script says so", !tree.PhysicsInterpolation);
        var root = new Node3D();
        var child = new Node3D();
        tree.Root.AddChild(root);
        root.AddChild(child);
        T.Check("a node inherits by default", root.PhysicsInterpolationMode == Node.PhysicsInterpolationModeEnum.Inherit && child.IsPhysicsInterpolated() && !child.IsPhysicsInterpolatedAndEnabled());
        tree.PhysicsInterpolation = true;
        T.Check("the tree's switch turns it on for what inherits", child.IsPhysicsInterpolatedAndEnabled());
        root.PhysicsInterpolationMode = Node.PhysicsInterpolationModeEnum.Off;
        T.Check("a node's own mode wins over its parent's: an Off parent turns its Inherit child off", !child.IsPhysicsInterpolated());
        child.PhysicsInterpolationMode = Node.PhysicsInterpolationModeEnum.On;
        T.Check("and a child set On is on under an Off parent", child.IsPhysicsInterpolated() && !root.IsPhysicsInterpolated());
        root.ResetPhysicsInterpolation();
        T.Check("a reset with no engine does nothing and the interpolated transform is the global one", child.GetGlobalTransformInterpolated().Equals(child.GlobalTransform));
        tree.PhysicsInterpolation = false;
        T.Check("the switch can be turned off again", !tree.PhysicsInterpolation);

        Aver.Fabric.Engine.MaxPhysicsStepsPerFrame = 4;
        T.Check("the step cap is kept", Aver.Fabric.Engine.MaxPhysicsStepsPerFrame == 4);
        Aver.Fabric.Engine.MaxPhysicsStepsPerFrame = 200;
        T.Check("and held at the engine's 64", Aver.Fabric.Engine.MaxPhysicsStepsPerFrame == 64);
        Aver.Fabric.Engine.MaxPhysicsStepsPerFrame = 0;
        T.Check("0 is refused", Aver.Fabric.Engine.MaxPhysicsStepsPerFrame == 64);
        Aver.Fabric.Engine.MaxPhysicsStepsPerFrame = 8;
    }

    private static void BodiesAndNav()
    {
        var rb = new RigidBody3D();
        T.Check("a rigid body's new settings have Godot's defaults", rb.CanSleep && !rb.ContinuousCd && !rb.LockRotation && rb.CenterOfMassMode == RigidBody3D.CenterOfMassModeEnum.Auto
            && rb.CenterOfMass == Vector3.Zero && rb.Inertia == Vector3.Zero && rb.LinearDampMode == RigidBody3D.DampMode.Combine);
        rb.CanSleep = false; rb.ContinuousCd = true; rb.LockRotation = true; rb.Inertia = new Vector3(1, 2, 3); rb.AxisLockLinearY = true;
        T.Check("and keep what is set before the body exists", !rb.CanSleep && rb.ContinuousCd && rb.LockRotation && rb.Inertia.Z == 3f && rb.AxisLockLinearY);
        rb.CenterOfMassMode = RigidBody3D.CenterOfMassModeEnum.Custom;
        rb.CenterOfMass = new Vector3(0, -0.5f, 0);
        T.Check("a custom centre of mass", rb.CenterOfMassMode == RigidBody3D.CenterOfMassModeEnum.Custom && rb.CenterOfMass.Y == -0.5f);

        NavigationServer3D.SetDebugEnabled(true);
        T.Check("the navigation debug switch is kept", NavigationServer3D.GetDebugEnabled());
        NavigationServer3D.SetDebugEnabled(false);
        T.Check("and the agent getters answer without a navigation library", NavigationServer3D.AgentGetMaxNeighbors(default) >= 0 && NavigationServer3D.AgentGetRadius(default) >= 0f);
    }
}
