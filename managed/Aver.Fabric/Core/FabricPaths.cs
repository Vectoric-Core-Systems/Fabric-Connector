// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// res:// and user:// paths as the operating system sees them, for FileAccess, DirAccess, ConfigFile and ProjectSettings.globalize_path. In the engine
// the folders are the project's (Aver.Framework.Paths: res:// is the content folder, user:// the player's writable folder). Outside it (tests, tools)
// there is no project, so res:// is the working folder and user:// a folder under the temporary one, unless a host sets them.

namespace Aver.Fabric;

internal static class FabricPaths
{
    /// <summary>What res:// names when there is no engine; null means the working folder. A test or tool may set it.</summary>
    internal static string? ContentOverride;

    /// <summary>What user:// names when there is no engine; null means a folder under the system's temporary one. A test or tool may set it.</summary>
    internal static string? UserOverride;

    internal static string Content
    {
        get
        {
            if (ContentOverride != null) return ContentOverride;
            try
            {
                string d = Aver.Framework.Paths.Content;
                if (d.Length > 0) return d;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { }
            return System.IO.Directory.GetCurrentDirectory();
        }
    }

    internal static string User
    {
        get
        {
            if (UserOverride != null)
            {
                System.IO.Directory.CreateDirectory(UserOverride);
                return UserOverride;
            }
            try
            {
                string d = Aver.Framework.Paths.User;
                if (d.Length > 0) return d;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { }
            string fallback = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aver-fabric-user");
            System.IO.Directory.CreateDirectory(fallback);
            return fallback;
        }
    }

    internal static bool IsVirtual(string path) => path.StartsWith("res://", StringComparison.Ordinal) || path.StartsWith("user://", StringComparison.Ordinal);

    /// <summary>The operating-system path for a Godot path: res:// and user:// are replaced by their folders, anything else is returned as it is.</summary>
    internal static string Globalize(string path)
    {
        if (path.StartsWith("res://", StringComparison.Ordinal)) return Join(Content, path[6..]);
        if (path.StartsWith("user://", StringComparison.Ordinal)) return Join(User, path[7..]);
        return path;
    }

    /// <summary>The res:// or user:// path for an operating-system path inside one of those folders; the path unchanged when it is in neither.</summary>
    internal static string Localize(string path)
    {
        if (IsVirtual(path)) return path;
        string full = Normalize(path);
        if (Inside(Content, full, out string rest)) return "res://" + rest;
        if (Inside(User, full, out rest)) return "user://" + rest;
        return path;
    }

    private static string Join(string root, string rest)
    {
        rest = rest.Replace('/', System.IO.Path.DirectorySeparatorChar).TrimStart(System.IO.Path.DirectorySeparatorChar);
        return rest.Length == 0 ? root : System.IO.Path.Combine(root, rest);
    }

    private static string Normalize(string path)
    {
        try { return System.IO.Path.GetFullPath(path).Replace('\\', '/'); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or System.IO.PathTooLongException) { return path.Replace('\\', '/'); }
    }

    private static bool Inside(string root, string full, out string rest)
    {
        rest = "";
        string r = Normalize(root).TrimEnd('/');
        if (r.Length == 0 || !full.StartsWith(r + "/", StringComparison.OrdinalIgnoreCase)) return full.Equals(r, StringComparison.OrdinalIgnoreCase);
        rest = full[(r.Length + 1)..];
        return true;
    }
}
