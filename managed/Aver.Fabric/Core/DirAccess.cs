// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Godot's DirAccess: a handle on a folder to list, create, copy, rename and remove files and folders. res:// and user:// paths name the project's content folder and the
// player's folder (FabricPaths); any other path is the operating system's.

namespace Aver.Fabric;

/// <summary>A folder opened for listing and file management (Godot's DirAccess).</summary>
public class DirAccess : RefCounted
{
    private static Error s_openError = Error.Ok;

    // The folder in Godot's spelling, without a trailing slash (but "res://" and "user://" keep theirs); every operation globalizes it.
    private string _current = "";
    private List<string>? _listing;
    private int _listAt;

    public bool IncludeNavigational { get; set; }
    public bool IncludeHidden { get; set; }

    // ---- opening ----

    /// <summary>Opens a folder; null when it does not exist (<see cref="GetOpenError"/> says why).</summary>
    public static DirAccess? Open(string path)
    {
        string real = FabricPaths.Globalize(path);
        if (!Directory.Exists(real))
        {
            s_openError = Error.CantOpen;
            return null;
        }
        s_openError = Error.Ok;
        return new DirAccess { _current = Tidy(path) };
    }

    public static Error GetOpenError() => s_openError;

    private static string Tidy(string path)
    {
        path = path.Replace('\\', '/');
        if (path.EndsWith("://", StringComparison.Ordinal)) return path;
        return path.Length > 1 ? path.TrimEnd('/') : path;
    }

    // A path given to an operation: absolute (or virtual) as it is, else relative to the folder this handle is in.
    private string Resolve(string path)
    {
        if (path.Length == 0) return FabricPaths.Globalize(_current);
        bool rooted = FabricPaths.IsVirtual(path) || System.IO.Path.IsPathRooted(path);
        return FabricPaths.Globalize(rooted ? path : _current.EndsWith('/') ? _current + path : _current + "/" + path);
    }

    private static Error ErrorOf(Exception ex) => ex switch
    {
        FileNotFoundException or DirectoryNotFoundException => Error.DoesNotExist,
        UnauthorizedAccessException => Error.Unauthorized,
        PathTooLongException => Error.FileBadPath,
        _ => Error.Failed,
    };

    // ---- listing ----

    private bool Visible(string name, string full)
    {
        if (IncludeHidden) return true;
        if (name.StartsWith('.')) return false;
        try { return (File.GetAttributes(full) & FileAttributes.Hidden) == 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }
    }

    private List<(string Name, bool IsDir)> Entries(string real)
    {
        var all = new List<(string, bool)>();
        if (IncludeNavigational) { all.Add((".", true)); all.Add(("..", true)); }
        var found = new List<(string Name, bool IsDir)>();
        try
        {
            foreach (string path in Directory.EnumerateFileSystemEntries(real))
            {
                string name = System.IO.Path.GetFileName(path);
                if (Visible(name, path)) found.Add((name, Directory.Exists(path)));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        found.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        all.AddRange(found);
        return all;
    }

    /// <summary>Starts a listing; <see cref="GetNext"/> then gives the names one by one.</summary>
    public Error ListDirBegin()
    {
        string real = FabricPaths.Globalize(_current);
        if (!Directory.Exists(real)) return Error.Failed;
        _listing = Entries(real).Select(e => e.Name + (e.IsDir ? "/" : "")).ToList();
        _listAt = 0;
        return Error.Ok;
    }

    private bool _currentIsDir;

    /// <summary>The next name of the listing, or an empty string at the end.</summary>
    public string GetNext()
    {
        if (_listing == null || _listAt >= _listing.Count) return "";
        string entry = _listing[_listAt++];
        _currentIsDir = entry.EndsWith('/');
        return _currentIsDir ? entry[..^1] : entry;
    }

    /// <summary>Whether the name <see cref="GetNext"/> gave last is a folder.</summary>
    public bool CurrentIsDir() => _currentIsDir;

    public void ListDirEnd()
    {
        _listing = null;
        _listAt = 0;
    }

    /// <summary>The files of the folder, by name.</summary>
    public string[] GetFiles() => ListOf(Resolve(""), directories: false);

    /// <summary>The folders in the folder, by name.</summary>
    public string[] GetDirectories() => ListOf(Resolve(""), directories: true);

    public static string[] GetFilesAt(string path) => Open(path)?.GetFiles() ?? System.Array.Empty<string>();

    public static string[] GetDirectoriesAt(string path) => Open(path)?.GetDirectories() ?? System.Array.Empty<string>();

    private string[] ListOf(string real, bool directories) =>
        Directory.Exists(real) ? Entries(real).Where(e => e.IsDir == directories).Select(e => e.Name).ToArray() : System.Array.Empty<string>();

    public static int GetDriveCount() => DriveInfo.GetDrives().Length;

    public static string GetDriveName(int idx)
    {
        DriveInfo[] drives = DriveInfo.GetDrives();
        return idx >= 0 && idx < drives.Length ? drives[idx].Name.TrimEnd('\\', '/') : "";
    }

    /// <summary>The index of the drive the folder is on; 0 where there is only one root.</summary>
    public int GetCurrentDrive()
    {
        string real = System.IO.Path.GetFullPath(FabricPaths.Globalize(_current));
        DriveInfo[] drives = DriveInfo.GetDrives();
        for (int i = 0; i < drives.Length; i++)
            if (real.StartsWith(drives[i].Name, StringComparison.OrdinalIgnoreCase) && drives[i].Name.Length > 1) return i;
        return 0;
    }

    // ---- moving around ----

    /// <summary>Moves into a folder, given relative to the current one, or absolute ("res://", "user://" or a system path). "..": up one.</summary>
    public Error ChangeDir(string toDir)
    {
        string target;
        if (toDir == "..")
        {
            if (FabricPaths.IsVirtual(_current) && (_current == "res://" || _current == "user://")) return Error.Failed;
            int slash = _current.LastIndexOf('/');
            target = slash <= 0 ? _current : _current[..slash];
            if (target.EndsWith(":/", StringComparison.Ordinal)) target += "/";
        }
        else if (toDir == "." || toDir.Length == 0) return Error.Ok;
        else
        {
            bool rooted = FabricPaths.IsVirtual(toDir) || System.IO.Path.IsPathRooted(toDir);
            target = Tidy(rooted ? toDir : _current.EndsWith('/') ? _current + toDir : _current + "/" + toDir);
        }
        if (!Directory.Exists(FabricPaths.Globalize(target))) return Error.Failed;
        _current = target;
        return Error.Ok;
    }

    /// <summary>The folder this handle is in, in Godot's spelling.</summary>
    public string GetCurrentDir(bool includeDrive = true)
    {
        if (includeDrive || FabricPaths.IsVirtual(_current)) return _current;
        string full = _current;
        int colon = full.IndexOf(':');
        return colon >= 0 ? full[(colon + 1)..] : full;
    }

    // ---- creating, copying, removing ----

    public Error MakeDir(string path)
    {
        string real = Resolve(path);
        if (Directory.Exists(real)) return Error.AlreadyExists;
        string? parent = System.IO.Path.GetDirectoryName(real.TrimEnd('/', '\\'));
        if (parent != null && !Directory.Exists(parent)) return Error.CantCreate;
        try { Directory.CreateDirectory(real); return Error.Ok; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return Error.CantCreate; }
    }

    public Error MakeDirRecursive(string path)
    {
        string real = Resolve(path);
        if (Directory.Exists(real)) return Error.Ok;
        try { Directory.CreateDirectory(real); return Error.Ok; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return Error.CantCreate; }
    }

    public static Error MakeDirAbsolute(string path) => new DirAccess { _current = "" }.MakeDir(path);

    public static Error MakeDirRecursiveAbsolute(string path) => new DirAccess { _current = "" }.MakeDirRecursive(path);

    public bool FileExists(string path) => File.Exists(Resolve(path));

    public bool DirExists(string path) => Directory.Exists(Resolve(path));

    public static bool DirExistsAbsolute(string path) => Directory.Exists(FabricPaths.Globalize(path));

    /// <summary>Copies a file. <paramref name="chmodFlags"/> (Unix permissions for the copy) is applied where the platform has them; -1 keeps the source's.</summary>
    public Error Copy(string from, string to, int chmodFlags = -1)
    {
        string src = Resolve(from), dst = Resolve(to);
        if (!File.Exists(src)) return Error.DoesNotExist;
        try
        {
            File.Copy(src, dst, overwrite: true);
            if (chmodFlags >= 0 && !OperatingSystem.IsWindows()) File.SetUnixFileMode(dst, (UnixFileMode)chmodFlags);
            return Error.Ok;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return Error.CantCreate; }
    }

    public static Error CopyAbsolute(string from, string to, int chmodFlags = -1) => new DirAccess { _current = "" }.Copy(from, to, chmodFlags);

    /// <summary>Renames a file or a folder; <paramref name="to"/> must not exist.</summary>
    public Error Rename(string from, string to)
    {
        string src = Resolve(from), dst = Resolve(to);
        try
        {
            if (Directory.Exists(src)) { if (Directory.Exists(dst) || File.Exists(dst)) return Error.AlreadyExists; Directory.Move(src, dst); return Error.Ok; }
            if (File.Exists(src)) { if (Directory.Exists(dst) || File.Exists(dst)) return Error.AlreadyExists; File.Move(src, dst); return Error.Ok; }
            return Error.DoesNotExist;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return ErrorOf(ex); }
    }

    public static Error RenameAbsolute(string from, string to) => new DirAccess { _current = "" }.Rename(from, to);

    /// <summary>Removes a file or an empty folder.</summary>
    public Error Remove(string path)
    {
        string real = Resolve(path);
        try
        {
            if (Directory.Exists(real)) { Directory.Delete(real, recursive: false); return Error.Ok; }
            if (File.Exists(real)) { File.Delete(real); return Error.Ok; }
            return Error.DoesNotExist;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return ErrorOf(ex); }
    }

    public static Error RemoveAbsolute(string path) => new DirAccess { _current = "" }.Remove(path);

    // ---- links and properties of the file system ----

    public bool IsLink(string path)
    {
        try { return new FileInfo(Resolve(path)).LinkTarget != null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    public string ReadLink(string path)
    {
        try { return new FileInfo(Resolve(path)).LinkTarget ?? ""; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return ""; }
    }

    /// <summary>The bytes free on the drive this folder is on; 0 when unknown.</summary>
    public long GetSpaceLeft()
    {
        try { return new DriveInfo(System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(FabricPaths.Globalize(_current))) ?? "").AvailableFreeSpace; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return 0; }
    }

    /// <summary>The file system's type ("NTFS", "ext4" ...), or "Unknown".</summary>
    public string GetFilesystemType()
    {
        try { return new DriveInfo(System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(FabricPaths.Globalize(_current))) ?? "").DriveFormat; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return "Unknown"; }
    }

    /// <summary>Whether the file system tells "A" from "a": no on Windows and macOS, yes elsewhere.</summary>
    public bool IsCaseSensitive(string path) => !(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS());

    /// <summary>Whether two paths name the same file or folder.</summary>
    public bool IsEquivalent(string pathA, string pathB)
    {
        string a = System.IO.Path.GetFullPath(Resolve(pathA)), b = System.IO.Path.GetFullPath(Resolve(pathB));
        return string.Equals(a, b, IsCaseSensitive("") ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
    }
}
