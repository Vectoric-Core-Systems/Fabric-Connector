// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Content preload: loading a scene, mesh or material resource and instantiating a scene tell the engine what is about to be shown
// (Aver.Framework.Preload), so its texture workers start before the first draw. The requests go to ContentPreload.Sink here; the engine
// side (the queue, the workers) is the engine's own tests. Nothing is sent outside the engine.

using Aver.Fabric;

static class PreloadTests
{
    public static int RunAll() => T.Suite(() =>
    {
        T.Run("preload: nothing is sent outside the engine", NotInEngine);
        T.Run("preload: a scene's meshes, a mesh and a material are sent in the engine", InEngine);
    });

    private sealed class EngineFlagHost : INodeHost
    {
        public bool IsEngine => true;
        public void Enter(Node subtreeRoot) => NodeHost.ManagedEnter(subtreeRoot);
        public void Exit(Node subtreeRoot) => NodeHost.ManagedExit(subtreeRoot);
        public void Destroy(Node node) => NodeHost.ManagedExit(node);
    }

    private const string Level =
        "OCWORLD 1\n" +
        "PLACE none 0 0 0 0 0 0 1 class Node3D name Root\n" +
        "BEGIN\n" +
        "  CHILD Meshes/box.ocmesh 0 0 0 0 0 0 1 nocollide\n" +
        "  CHILD Meshes/Props/lamp.ocmesh 0 0 0 0 0 0 1 nocollide\n" +
        "  CHILD none 0 0 0 0 0 0 1 class Marker3D name Tip\n" +
        "END\n";

    private static string MakeContent()
    {
        string root = Path.Combine(Path.GetTempPath(), "fabric-preload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Meshes"));
        Directory.CreateDirectory(Path.Combine(root, "Materials"));
        File.WriteAllText(Path.Combine(root, "Meshes", "crate.ocmesh"), "x");
        File.WriteAllText(Path.Combine(root, "Materials", "Brick.ocmat"), "OCMAT 1\nNAME Brick\n");
        File.WriteAllText(Path.Combine(root, "level.ocworld"), Level);
        return root;
    }

    private static void NotInEngine()
    {
        var sent = new List<string>();
        ContentPreload.Sink = (k, n) => sent.Add($"{k}:{n}");
        string root = MakeContent();
        try
        {
            NodeHost.Current = new ManagedNodeHost();
            ResourceLoader.ContentRootOverride = root;
            PackedScene.FromText(Level).Instantiate();
            ResourceLoader.Load("res://Meshes/crate.ocmesh");
            ResourceLoader.Load("res://Materials/Brick.ocmat");
            T.Check("a scene, a mesh and a material loaded with no engine send nothing", sent.Count == 0, string.Join(",", sent));
        }
        finally
        {
            ContentPreload.Sink = null;
            ResourceLoader.ContentRootOverride = null;
            ResourceLoader.ClearCache();
            Directory.Delete(root, true);
        }
    }

    private static void InEngine()
    {
        var sent = new List<string>();
        ContentPreload.Sink = (k, n) => sent.Add($"{k}:{n}");
        string root = MakeContent();
        try
        {
            NodeHost.Current = new EngineFlagHost();
            ResourceLoader.ContentRootOverride = root;

            var scene = PackedScene.FromText(Level);
            scene.PreloadContent();
            T.CheckSeq("a scene's placed meshes are sent, nested ones too, markers not", new[] { "0:Meshes/box.ocmesh", "0:Meshes/Props/lamp.ocmesh" }, sent);

            sent.Clear();
            scene.Instantiate();
            T.Check("instantiating asks again (the engine drops what is already resident)", sent.Count == 2, string.Join(",", sent));

            sent.Clear();
            ResourceLoader.Load("res://Meshes/crate.ocmesh");
            T.CheckSeq("loading a mesh sends its content-relative asset path", new[] { "0:Meshes/crate.ocmesh" }, sent);

            sent.Clear();
            ResourceLoader.Load("res://Materials/Brick.ocmat");
            T.CheckSeq("loading a material sends its name", new[] { "1:Brick" }, sent);

            sent.Clear();
            var holder = new Node3D();
            holder.AddChild(new MeshInstance3D { Mesh = new FileMesh { ResourcePath = "res://Meshes/crate.ocmesh" } });
            ContentPreload.Tree(holder);
            T.CheckSeq("the meshes below a node tree (a scene packed from nodes) are sent", new[] { "0:Meshes/crate.ocmesh" }, sent);
        }
        finally
        {
            ContentPreload.Sink = null;
            NodeHost.Current = new ManagedNodeHost();
            ResourceLoader.ContentRootOverride = null;
            ResourceLoader.ClearCache();
            Directory.Delete(root, true);
        }
    }
}
