// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Godot's FileAccess: a file opened for reading or writing, with Godot's typed reads and writes. res:// and user:// paths name the project's content folder and the
// player's folder (FabricPaths); any other path is the operating system's. Encrypted and Godot-compressed files are not supported (open returns null).

using System.Security.Cryptography;
using System.Text;

namespace Aver.Fabric;

/// <summary>A file open for reading, writing or both (Godot's FileAccess).</summary>
public class FileAccess : RefCounted
{
    /// <summary>Godot's FileAccess.ModeFlags.</summary>
    public enum ModeFlags { Read = 1, Write = 2, ReadWrite = 3, WriteRead = 7 }

    /// <summary>Godot's FileAccess.CompressionMode.</summary>
    public enum CompressionMode { Fastlz = 0, Deflate = 1, Zstd = 2, Gzip = 3, Brotli = 4 }

    /// <summary>Godot's FileAccess.UnixPermissionFlags.</summary>
    [Flags]
    public enum UnixPermissionFlags
    {
        ExecuteOther = 1, WriteOther = 2, ReadOther = 4, ExecuteGroup = 8, WriteGroup = 16, ReadGroup = 32, ExecuteOwner = 64, WriteOwner = 128, ReadOwner = 256,
        RestrictedDelete = 512, SetGroupId = 1024, SetUserId = 2048,
    }

    private static Error s_openError = Error.Ok;

    private FileStream? _stream;
    private string _path = "";
    private bool _canRead, _canWrite, _eof;
    private Error _error = Error.Ok;

    /// <summary>Whether multi-byte numbers are read and written most significant byte first. False (little endian) by default.</summary>
    public bool BigEndian { get; set; }

    // ---- opening ----

    /// <summary>Opens a file. Read needs the file to exist; Write creates it or empties it; ReadWrite needs it to exist and keeps its contents; WriteRead creates or empties it.
    /// Null when the file cannot be opened; <see cref="GetOpenError"/> says why.</summary>
    public static FileAccess? Open(string path, ModeFlags flags)
    {
        string real = FabricPaths.Globalize(path);
        try
        {
            if (flags == ModeFlags.Read && Directory.Exists(real)) { s_openError = Error.FileCantOpen; return null; }
            if (flags is ModeFlags.Write or ModeFlags.WriteRead)
            {
                string? dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(real));
                if (dir != null && !Directory.Exists(dir)) { s_openError = Error.FileNotFound; return null; }
            }
            FileMode mode = flags is ModeFlags.Write or ModeFlags.WriteRead ? FileMode.Create : FileMode.Open;
            System.IO.FileAccess access = flags == ModeFlags.Read ? System.IO.FileAccess.Read : flags == ModeFlags.Write ? System.IO.FileAccess.Write : System.IO.FileAccess.ReadWrite;
            var stream = new FileStream(real, mode, access, flags == ModeFlags.Read ? FileShare.Read : FileShare.ReadWrite);
            s_openError = Error.Ok;
            return new FileAccess { _stream = stream, _path = path, _canRead = flags != ModeFlags.Write, _canWrite = flags != ModeFlags.Read };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            s_openError = ErrorOf(ex);
            return null;
        }
    }

    /// <summary>Not supported: Godot's encrypted file format is not read or written here. Returns null with <see cref="Error.Unavailable"/>.</summary>
    public static FileAccess? OpenEncrypted(string path, ModeFlags modeFlags, byte[] key, byte[]? iv = null) => Unsupported("OpenEncrypted");

    /// <summary>Not supported (see <see cref="OpenEncrypted"/>).</summary>
    public static FileAccess? OpenEncryptedWithPass(string path, ModeFlags modeFlags, string pass) => Unsupported("OpenEncryptedWithPass");

    /// <summary>Not supported: Godot's compressed file format is not read or written here. Returns null with <see cref="Error.Unavailable"/>.</summary>
    public static FileAccess? OpenCompressed(string path, ModeFlags modeFlags, CompressionMode compressionMode = CompressionMode.Fastlz) => Unsupported("OpenCompressed");

    private static FileAccess? Unsupported(string what)
    {
        Aver.Scripting.Log.Error($"[Fabric] FileAccess.{what}: Godot's encrypted and compressed file formats are not supported");
        s_openError = Error.Unavailable;
        return null;
    }

    /// <summary>The error of the last <see cref="Open"/>: <see cref="Error.Ok"/> when it worked.</summary>
    public static Error GetOpenError() => s_openError;

    private static Error ErrorOf(Exception ex) => ex switch
    {
        FileNotFoundException or DirectoryNotFoundException => Error.FileNotFound,
        UnauthorizedAccessException => Error.FileNoPermission,
        PathTooLongException => Error.FileBadPath,
        ArgumentException => Error.FileBadPath,
        _ => Error.FileCantOpen,
    };

    // ---- state ----

    public string GetPath() => _path;

    public string GetPathAbsolute() => _stream?.Name ?? FabricPaths.Globalize(_path);

    public bool IsOpen() => _stream != null;

    /// <summary>The error of the last read or write: <see cref="Error.FileEof"/> after a read ran off the end.</summary>
    public Error GetError() => _error;

    /// <summary>True once a read has gone past the end of the file.</summary>
    public bool EofReached() => _eof;

    public long GetPosition() => _stream?.Position ?? 0;

    public long GetLength() => _stream?.Length ?? 0;

    public void Seek(long position)
    {
        if (_stream == null) return;
        _stream.Position = Math.Clamp(position, 0, _stream.Length);
        _eof = false;
    }

    /// <summary>Moves to <paramref name="position"/> bytes before the end (0, the default, is the end itself).</summary>
    public void SeekEnd(long position = 0)
    {
        if (_stream == null) return;
        _stream.Position = Math.Clamp(_stream.Length + position, 0, _stream.Length);
        _eof = false;
    }

    public Error Resize(long length)
    {
        if (_stream == null || !_canWrite) return Error.Unconfigured;
        try { _stream.SetLength(Math.Max(0, length)); return Error.Ok; }
        catch (Exception ex) when (ex is IOException or NotSupportedException) { return Error.FileCantWrite; }
    }

    public void Flush() => _stream?.Flush();

    public void Close()
    {
        _stream?.Dispose();
        _stream = null;
    }

    public override void Free()
    {
        Close();
        base.Free();
    }

    // ---- reading ----

    private byte[]? Read(int count)
    {
        _error = Error.Ok;
        if (_stream == null || !_canRead) { _error = Error.Unconfigured; return null; }
        var buffer = new byte[count];
        int got = 0;
        while (got < count)
        {
            int n = _stream.Read(buffer, got, count - got);
            if (n <= 0) break;
            got += n;
        }
        if (got < count)
        {
            _eof = true;
            _error = Error.FileEof;
            return null;
        }
        return buffer;
    }

    private byte[]? ReadOrdered(int count)
    {
        byte[]? b = Read(count);
        if (b != null && BigEndian != !BitConverter.IsLittleEndian) System.Array.Reverse(b);
        return b;
    }

    public long Get8() => Read(1) is { } b ? b[0] : 0;

    public long Get16() => ReadOrdered(2) is { } b ? BitConverter.ToUInt16(b, 0) : 0;

    public long Get32() => ReadOrdered(4) is { } b ? BitConverter.ToUInt32(b, 0) : 0;

    public long Get64() => ReadOrdered(8) is { } b ? BitConverter.ToInt64(b, 0) : 0;

    public float GetHalf() => ReadOrdered(2) is { } b ? (float)BitConverter.ToHalf(b, 0) : 0f;

    public float GetFloat() => ReadOrdered(4) is { } b ? BitConverter.ToSingle(b, 0) : 0f;

    public double GetDouble() => ReadOrdered(8) is { } b ? BitConverter.ToDouble(b, 0) : 0.0;

    /// <summary>A real number as the engine stores it: a 32-bit float (Godot's default build).</summary>
    public float GetReal() => GetFloat();

    /// <summary>The next <paramref name="length"/> bytes; fewer when the file ends first.</summary>
    public byte[] GetBuffer(long length)
    {
        _error = Error.Ok;
        if (_stream == null || !_canRead || length <= 0) return System.Array.Empty<byte>();
        var buffer = new byte[Math.Min(length, int.MaxValue)];
        int got = 0;
        while (got < buffer.Length)
        {
            int n = _stream.Read(buffer, got, buffer.Length - got);
            if (n <= 0) break;
            got += n;
        }
        if (got < buffer.Length) { _eof = true; _error = Error.FileEof; System.Array.Resize(ref buffer, got); }
        return buffer;
    }

    /// <summary>One line without its line break (a "\r" before the "\n" is dropped). The end of the file is reached when the last line has no break.</summary>
    public string GetLine()
    {
        _error = Error.Ok;
        if (_stream == null || !_canRead) return "";
        var line = new List<byte>();
        while (true)
        {
            int b = _stream.ReadByte();
            if (b < 0) { _eof = true; break; }
            if (b == '\n') break;
            line.Add((byte)b);
        }
        if (line.Count > 0 && line[^1] == '\r') line.RemoveAt(line.Count - 1);
        return Encoding.UTF8.GetString(line.ToArray());
    }

    /// <summary>One line of comma-separated values (or another <paramref name="delim"/>): a value in quotes may hold the delimiter and line breaks, and "" in quotes is a quote.</summary>
    public string[] GetCsvLine(string delim = ",")
    {
        string line = GetLine();
        var values = new List<string>();
        var cur = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length || quoted;)
        {
            if (i >= line.Length)
            {
                // A quoted value goes on to the next line.
                if (_eof) break;
                cur.Append('\n');
                line = GetLine();
                i = 0;
                continue;
            }
            char c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i += 2; continue; }
                if (c == '"') { quoted = false; i++; continue; }
                cur.Append(c);
                i++;
            }
            else if (c == '"') { quoted = true; i++; }
            else if (delim.Length > 0 && string.CompareOrdinal(line, i, delim, 0, delim.Length) == 0) { values.Add(cur.ToString()); cur.Clear(); i += delim.Length; }
            else { cur.Append(c); i++; }
        }
        values.Add(cur.ToString());
        return values.ToArray();
    }

    /// <summary>The whole file as text (UTF-8), from the start; the position is left where it was. <paramref name="skipCr"/> drops the "\r" of "\r\n".</summary>
    public string GetAsText(bool skipCr = false)
    {
        if (_stream == null || !_canRead) return "";
        long at = _stream.Position;
        _stream.Position = 0;
        var bytes = new byte[_stream.Length];
        int got = 0;
        while (got < bytes.Length)
        {
            int n = _stream.Read(bytes, got, bytes.Length - got);
            if (n <= 0) break;
            got += n;
        }
        _stream.Position = at;
        string text = Encoding.UTF8.GetString(bytes, 0, got);
        return skipCr ? text.Replace("\r", "", StringComparison.Ordinal) : text;
    }

    /// <summary>A string written by <see cref="StorePascalString"/>: a 32-bit length, then that many UTF-8 bytes.</summary>
    public string GetPascalString()
    {
        long length = Get32();
        if (_error != Error.Ok) return "";
        return Encoding.UTF8.GetString(GetBuffer(length));
    }

    /// <summary>A value written by <see cref="StoreVar"/>. Objects are not decoded unless <paramref name="allowObjects"/>.</summary>
    public Variant GetVar(bool allowObjects = false)
    {
        long length = Get32();
        if (_error != Error.Ok) return default;
        byte[] bytes = GetBuffer(length);
        return allowObjects ? GD.BytesToVarWithObjects(bytes) : GD.BytesToVar(bytes);
    }

    // ---- writing ----

    private bool Write(byte[] bytes, bool ordered)
    {
        _error = Error.Ok;
        if (_stream == null || !_canWrite) { _error = Error.Unconfigured; return false; }
        if (ordered && BigEndian != !BitConverter.IsLittleEndian) System.Array.Reverse(bytes);
        try { _stream.Write(bytes, 0, bytes.Length); return true; }
        catch (Exception ex) when (ex is IOException or NotSupportedException) { _error = Error.FileCantWrite; return false; }
    }

    public bool Store8(long value) => Write(new[] { (byte)value }, false);
    public bool Store16(long value) => Write(BitConverter.GetBytes((ushort)value), true);
    public bool Store32(long value) => Write(BitConverter.GetBytes((uint)value), true);
    public bool Store64(long value) => Write(BitConverter.GetBytes(value), true);
    public bool StoreHalf(float value) => Write(BitConverter.GetBytes((Half)value), true);
    public bool StoreFloat(float value) => Write(BitConverter.GetBytes(value), true);
    public bool StoreDouble(double value) => Write(BitConverter.GetBytes(value), true);
    public bool StoreReal(float value) => StoreFloat(value);
    public bool StoreBuffer(byte[] buffer) => Write((byte[])buffer.Clone(), false);

    /// <summary>The text as UTF-8, with no length and no line break.</summary>
    public bool StoreString(string @string) => Write(Encoding.UTF8.GetBytes(@string), false);

    /// <summary>The text and a "\n".</summary>
    public bool StoreLine(string line) => StoreString(line + "\n");

    /// <summary>A line of values separated by <paramref name="delim"/>; a value with the delimiter, a quote or a line break is put in quotes.</summary>
    public bool StoreCsvLine(string[] values, string delim = ",")
    {
        var sb = new StringBuilder();
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) sb.Append(delim);
            string v = values[i];
            bool quote = v.Contains('"') || v.Contains('\n') || (delim.Length > 0 && v.Contains(delim, StringComparison.Ordinal));
            sb.Append(quote ? "\"" + v.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : v);
        }
        return StoreLine(sb.ToString());
    }

    public bool StorePascalString(string @string)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(@string);
        return Store32(bytes.Length) && Write(bytes, false);
    }

    /// <summary>A value in Godot's binary form, with its length in front so <see cref="GetVar"/> can read it back.</summary>
    public bool StoreVar(Variant value, bool fullObjects = false)
    {
        byte[] bytes = fullObjects ? GD.VarToBytesWithObjects(value) : GD.VarToBytes(value);
        return Store32(bytes.Length) && Write(bytes, false);
    }

    // ---- files by path ----

    public static bool FileExists(string path) => File.Exists(FabricPaths.Globalize(path));

    public static byte[] GetFileAsBytes(string path)
    {
        try { s_openError = Error.Ok; return File.ReadAllBytes(FabricPaths.Globalize(path)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            s_openError = ErrorOf(ex);
            return System.Array.Empty<byte>();
        }
    }

    public static string GetFileAsString(string path) => Encoding.UTF8.GetString(GetFileAsBytes(path));

    /// <summary>The MD5 hash of the file as 32 lower-case hex digits; empty if it cannot be read.</summary>
    public static string GetMd5(string path)
    {
        try { using FileStream s = File.OpenRead(FabricPaths.Globalize(path)); return Convert.ToHexStringLower(MD5.HashData(s)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return ""; }
    }

    /// <summary>The SHA-256 hash of the file as 64 lower-case hex digits; empty if it cannot be read.</summary>
    public static string GetSha256(string path)
    {
        try { using FileStream s = File.OpenRead(FabricPaths.Globalize(path)); return Convert.ToHexStringLower(SHA256.HashData(s)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return ""; }
    }

    private static long UnixOf(DateTime utc) => (long)Math.Floor((utc - DateTime.UnixEpoch).TotalSeconds);

    /// <summary>When the file was last changed, in unix seconds; 0 if there is no such file.</summary>
    public static long GetModifiedTime(string file)
    {
        string real = FabricPaths.Globalize(file);
        return File.Exists(real) ? UnixOf(File.GetLastWriteTimeUtc(real)) : 0;
    }

    /// <summary>When the file was last read, in unix seconds; 0 if there is no such file.</summary>
    public static long GetAccessTime(string file)
    {
        string real = FabricPaths.Globalize(file);
        return File.Exists(real) ? UnixOf(File.GetLastAccessTimeUtc(real)) : 0;
    }

    /// <summary>The file's size in bytes; -1 if there is no such file.</summary>
    public static long GetSize(string file)
    {
        string real = FabricPaths.Globalize(file);
        return File.Exists(real) ? new FileInfo(real).Length : -1;
    }

    public static bool GetHiddenAttribute(string file)
    {
        try { return (File.GetAttributes(FabricPaths.Globalize(file)) & FileAttributes.Hidden) != 0 || System.IO.Path.GetFileName(file).StartsWith('.'); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    public static Error SetHiddenAttribute(string file, bool hidden) => SetAttribute(file, FileAttributes.Hidden, hidden);

    public static bool GetReadOnlyAttribute(string file)
    {
        try { return (File.GetAttributes(FabricPaths.Globalize(file)) & FileAttributes.ReadOnly) != 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    public static Error SetReadOnlyAttribute(string file, bool ro) => SetAttribute(file, FileAttributes.ReadOnly, ro);

    private static Error SetAttribute(string file, FileAttributes attribute, bool on)
    {
        try
        {
            string real = FabricPaths.Globalize(file);
            FileAttributes now = File.GetAttributes(real);
            File.SetAttributes(real, on ? now | attribute : now & ~attribute);
            return Error.Ok;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return Error.FileCantWrite; }
    }

    /// <summary>The Unix permission bits of the file; 0 where the platform has none (Windows) or the file is missing.</summary>
    public static UnixPermissionFlags GetUnixPermissions(string file)
    {
        if (OperatingSystem.IsWindows()) return 0;
        try { return (UnixPermissionFlags)(int)File.GetUnixFileMode(FabricPaths.Globalize(file)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return 0; }
    }

    public static Error SetUnixPermissions(string file, UnixPermissionFlags permissions)
    {
        if (OperatingSystem.IsWindows()) return Error.Unavailable;
        try { File.SetUnixFileMode(FabricPaths.Globalize(file), (UnixFileMode)(int)permissions); return Error.Ok; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return Error.FileCantWrite; }
    }
}
