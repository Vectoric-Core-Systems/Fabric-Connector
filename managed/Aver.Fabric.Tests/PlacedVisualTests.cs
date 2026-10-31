// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// PlacedVisualTests: the plain mesh placements a converted level holds below a node (Props `_placed`, docs/plans/fabric/G3_SCENES.md "Placed visuals"). The engine side
// (adopting the child entities) needs a world and is checked by the owner's editor run; what runs here is the part that decides it: which children are the node's own
// drawing, that a PackedScene does not turn them into extra nodes, and that a MultiMesh forwards only the changes the placed entities can follow.

using Aver.Fabric;

static class PlacedVisualTests
{
    public static int RunAll() => T.Suite(() =>
    {
        T.Run("placed visuals: which child entities are a node's own drawing", Matching);
        T.Run("placed visuals: a PackedScene builds the node, not its placed meshes", PackedSceneSkipsPlaced);
        T.Run("placed visuals: the Props key reaches the node", PropsKey);
        T.Run("placed visuals: a MultiMesh forwards what the placed entities can follow", MultiMeshForwards);
    });

    private static void Matching()
    {
        const string asset = "Meshes/Generated/BoxMesh_1.ocmesh";
        var kids = new List<(string Item, string Name, bool IsActor, bool HasMesh)>
        {
            ("a", asset, false, true),            // the placed mesh
            ("b", "Meshes/Other.ocmesh", false, true),   // another mesh below the node
            ("c", asset, true, true),             // an actor that happens to be named alike
            ("d", asset, false, false),           // no renderer
            ("e", asset, false, true),            // a second instance of the same mesh
        };
        T.CheckSeq("the mesh entities named by the asset path, in order, and nothing else", new[] { "a", "e" }, PlacedMatch.Find(asset, kids));
        T.Check("no match gives an empty list", PlacedMatch.Find("Meshes/None.ocmesh", kids).Count == 0);
    }

    private const string Level =
        "OCWORLD 1\n" +
        "PLACE none 0 0 0 0 0 0 1 class MeshInstance3D name Crate\n" +
        "  COMPONENT Props _name=\"Crate\" mesh=Resource(\"Meshes/box.ocmesh\") _placed=1\n" +
        "BEGIN\n" +
        "  CHILD Meshes/box.ocmesh 0 0 0 0 0 0 1 nocollide\n" +
        "  CHILD none 0 0 0 0 0 0 1 class Marker3D name Tip\n" +
        "    COMPONENT Props _name=\"Tip\"\n" +
        "END\n";

    private static void PackedSceneSkipsPlaced()
    {
        Node root = PackedScene.FromText(Level).Instantiate();
        T.Check("the root is the MeshInstance3D", root is MeshInstance3D);
        T.Check("the placed mesh is not a node: only the real child is", root.GetChildCount() == 1 && root.GetChild(0).GetClass() == "Marker3D", $"{root.GetChildCount()} children");
        T.Check("the node was built as a plain node: no placed count left on it", root.PlacedVisuals == 0);

        // A level without the key keeps its plain placement as a node (how a hand-written level has always read).
        Node plain = PackedScene.FromText(Level.Replace(" _placed=1", "")).Instantiate();
        T.Check("without _placed a plain mesh placement below a node is a node", plain.GetChildCount() == 2);
    }

    private static void PropsKey()
    {
        var n = new MeshInstance3D();
        n.ApplyValues(new List<(string Key, string Text)> { ("mesh", "Resource(\"Meshes/box.ocmesh\")"), ("_placed", "1") });
        T.Check("_placed is read whatever its place in the list (the mesh before it must already know)", n.PlacedVisuals == 1);
        T.Check("the key is not metadata", !n.HasMeta("_placed"));
        T.Check("the node knows its mesh asset", n.PlacedAsset == "Meshes/box.ocmesh", n.PlacedAsset ?? "null");
        T.Check("without an engine nothing is pending", !n.PlacedPending);
    }

    private sealed class Fake : IPlacedInstances
    {
        public readonly List<(int, Transform3D)> Moves = new();
        public int Visible, Releases;
        public void SetTransform(int instance, Transform3D transform) => Moves.Add((instance, transform));
        public void VisibleCountChanged() => Visible++;
        public void Release() => Releases++;
    }

    private static void MultiMeshForwards()
    {
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, InstanceCount = 3 };
        var fake = new Fake();
        T.Check("three placed entities follow a table of three", mm.CanFollowPlaced(3));
        T.Check("two placed entities do not follow a table of three", !mm.CanFollowPlaced(2));

        mm.Placed = fake;
        var t = new Transform3D(Basis.Identity, new Vector3(1, 2, 3));
        mm.SetInstanceTransform(1, t);
        T.Check("a transform reaches the entity of that instance and the table", fake.Moves.Count == 1 && fake.Moves[0].Item1 == 1 && fake.Moves[0].Item2.Origin == new Vector3(1, 2, 3) && mm.GetInstanceTransform(1).Origin == new Vector3(1, 2, 3));
        mm.VisibleInstanceCount = 2;
        T.Check("the visible count is forwarded", fake.Visible == 1 && fake.Releases == 0);

        var buf = mm.Buffer;
        buf[3] = 9;   // instance 0, origin x
        fake.Moves.Clear();
        mm.Buffer = buf;
        T.Check("a buffer of the same size moves every entity", fake.Moves.Count == 3 && fake.Releases == 0 && mm.GetInstanceTransform(0).Origin.X == 9);

        mm.InstanceCount = 3;
        T.Check("the same count changes nothing", fake.Releases == 0);
        mm.InstanceCount = 4;
        T.Check("another count cannot be followed: the entities are given up", fake.Releases == 1);
        mm.UseColors = true;
        T.Check("colours cannot be followed either", fake.Releases == 2);
        mm.UseColors = false;
        mm.UseCustomData = true;
        T.Check("nor can custom data", fake.Releases == 3);
        mm.Mesh = null;
        T.Check("nor another mesh", fake.Releases == 4);
        T.Check("a table with colours is not followed", !new MultiMesh { InstanceCount = 2, UseColors = true }.CanFollowPlaced(2));
    }
}
