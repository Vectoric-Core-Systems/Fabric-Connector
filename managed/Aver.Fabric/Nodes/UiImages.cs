// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// The textures of Control drawing (P39): a Texture2D's content path becomes the id the engine's UI draws with, through the image table the host
// installs in Aver.UI.Abi (ui_abi.h "images"). One reference per path per session; a session boundary releases them all.

namespace Aver.Fabric;

internal static class UiImages
{
    // path as the scene named it -> its id; 0 remembers a path that could not be loaded, so it is tried (and reported) once per session.
    private static readonly Dictionary<string, ulong> s_ids = new(StringComparer.Ordinal);
    // content-relative file -> the id acquired for it, one reference each: "res://a.png" and "Textures/a.png" are one file and one reference.
    private static readonly Dictionary<string, ulong> s_files = new(StringComparer.Ordinal);

    /// <summary>Counts session boundaries. A texture whose handle came from here and was taken in an earlier epoch asks again.</summary>
    internal static int Epoch { get; private set; }

    /// <summary>The <see cref="Texture2D.HandleResolver"/> an engine host gets: acquires the texture's file by its content path. 0 outside the engine,
    /// with an engine that has no image table, or when the file does not load.</summary>
    internal static ulong Resolve(Texture2D texture)
    {
        string path = texture.ResourcePath;
        if (path.Length == 0 || !NodeHost.Current.IsEngine || !UiNative.ImageAvailable) return 0;
        if (s_ids.TryGetValue(path, out ulong id)) return id;
        string? rel = ContentRelative(path);
        if (rel == null) Aver.Scripting.Log.Warn($"[Fabric] the UI texture '{path}' is not in the project's Content folder; it draws nothing");
        else if (!s_files.TryGetValue(rel, out id))
        {
            id = UiNative.ImageAcquire(rel);
            s_files[rel] = id;
        }
        s_ids[path] = id;
        return id;
    }

    /// <summary>The file's path relative to the Content folder (what the engine opens), found the way <see cref="ResourceLoader"/> finds it.</summary>
    internal static string? ContentRelative(string resourcePath)
    {
        string root = ResourceLoader.ContentRoot;
        if (root.Length == 0) return null;
        foreach (string c in ResourceLoader.Candidates(resourcePath))
            if (File.Exists(System.IO.Path.Combine(root, c.Replace('/', System.IO.Path.DirectorySeparatorChar)))) return c;
        return null;
    }

    /// <summary>The source size the engine knows for a texture handle taken from <see cref="Resolve"/>, for formats the file header reader cannot size.</summary>
    internal static Vector2? SizeOf(ulong id) => UiNative.ImageSize(id);

    /// <summary>A play session begins or ends: releases every reference, and every texture asks again for its handle.</summary>
    internal static void NewSession()
    {
        foreach (ulong id in s_files.Values) UiNative.ImageRelease(id);
        s_files.Clear();
        s_ids.Clear();
        Epoch++;
    }

    internal static void ResetForTests() => NewSession();
}
