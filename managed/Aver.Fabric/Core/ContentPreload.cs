// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Tells the engine what is about to be shown, so its texture workers decode and compress the materials' textures before the first draw
// (Aver.Framework.Preload; docs/ABI.md, content preload). A hint: it never waits and costs nothing outside the engine.

namespace Aver.Fabric;

internal static class ContentPreload
{
    /// <summary>Where requests go instead of the engine; a test sets it. Kind 0 is a mesh asset path, 1 a material name.</summary>
    internal static Action<int, string>? Sink;
    private static bool s_unavailable;   // an engine built before the preload request: the hint is dropped, once

    private static void Send(int kind, string name)
    {
        if (Sink != null) { Sink(kind, name); return; }
        if (s_unavailable) return;
        try
        {
            if (kind == 0) Aver.Framework.Preload.Mesh(name); else Aver.Framework.Preload.Material(name);
        }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException) { s_unavailable = true; }
    }

    /// <summary>The materials of the mesh at an engine asset path ("meshes/a.ocmesh").</summary>
    internal static void Mesh(string? assetPath)
    {
        if (string.IsNullOrEmpty(assetPath) || !NodeHost.Current.IsEngine) return;
        Send(0, assetPath);
    }

    /// <summary>The textures of the material a resource path names ("res://materials/brick.ocmat").</summary>
    internal static void Material(string? resourcePath)
    {
        if (string.IsNullOrEmpty(resourcePath) || !NodeHost.Current.IsEngine) return;
        Send(1, System.IO.Path.GetFileNameWithoutExtension(resourcePath));
    }

    /// <summary>Every mesh a converted scene places.</summary>
    internal static void Scene(IEnumerable<SceneNodeData>? nodes)
    {
        if (nodes == null || !NodeHost.Current.IsEngine) return;
        foreach (SceneNodeData n in nodes)
        {
            if (n.Asset.EndsWith(".ocmesh", StringComparison.OrdinalIgnoreCase)) Mesh(n.Asset);
            Scene(n.Children);
        }
    }

    /// <summary>Every mesh and material override below a node tree (a scene packed from nodes).</summary>
    internal static void Tree(Node? root)
    {
        if (root == null || !NodeHost.Current.IsEngine) return;
        if (root is GeometryInstance3D g)
        {
            Mesh(g.PlacedAsset);
            Material(g.MaterialOverride?.ResourcePath);
        }
        foreach (Node child in root.GetChildren()) Tree(child);
    }
}
