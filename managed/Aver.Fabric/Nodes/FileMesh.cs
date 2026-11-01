// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// A mesh loaded from an .ocmesh file: its material slots (the file's MADR chunk) are the surfaces, each with the .ocmat of that name.

namespace Aver.Fabric;

/// <summary>What ResourceLoader makes of an .ocmesh. The geometry stays with the engine; the surfaces are the file's material slots, so a script that asks the
/// mesh for its surface count or a surface's material gets what Godot would give it.</summary>
internal sealed class FileMesh : Mesh
{
    private string[]? _slots;
    private Dictionary<int, Material?>? _set;
    private string? _sharedAsset;   // set on a copy, which has no path of its own but draws the same file

    public FileMesh() { }

    private string[] Slots()
    {
        if (_slots != null) return _slots;
        string? file = ResourcePath.Length == 0 ? null : ResourceLoader.FindFile(ResourcePath);
        return _slots = (file == null ? null : OcMeshFile.ReadMaterialSlots(file)) ?? System.Array.Empty<string>();
    }

    public override int GetSurfaceCount() => Slots().Length;

    public override Material? SurfaceGetMaterial(int surfIdx)
    {
        string[] slots = Slots();
        if (surfIdx < 0 || surfIdx >= slots.Length) return null;
        if (_set != null && _set.TryGetValue(surfIdx, out Material? chosen)) return chosen;
        return slots[surfIdx].Length == 0 ? null : ResourceLoader.Load(MaterialPath(slots[surfIdx]), typeof(Material)) as Material;
    }

    public override void SurfaceSetMaterial(int surfIdx, Material? material)
    {
        if (surfIdx < 0 || surfIdx >= Slots().Length) return;
        (_set ??= new())[surfIdx] = material;
    }

    // Where the engine looks for a slot's material: Materials/<name>.ocmat, else the name as a content path.
    private static string MaterialPath(string slot)
    {
        string asMaterial = "Materials/" + slot + ".ocmat";
        return ResourceLoader.FindFile(asMaterial) == null && ResourceLoader.FindFile(slot) != null ? slot : asMaterial;
    }

    public override Resource Duplicate(bool subresources = false)
    {
        Slots();
        var copy = (FileMesh)base.Duplicate(subresources);
        copy._sharedAsset = _sharedAsset ?? (ResourcePath.Length == 0 ? null : MeshInstance3D.AssetPath(ResourcePath));
        if (_set != null) copy._set = new Dictionary<int, Material?>(_set);
        return copy;
    }

    internal override string? EngineAssetPath => _sharedAsset;
}

/// <summary>Reads the material slot names out of an .ocmesh (an AVR1 container: a 64-byte header, a directory of 40-byte chunk entries, the MADR chunk of
/// slot names as string-table references, and the STRT string table). Uncompressed chunks only, which is all the container allows.</summary>
internal static class OcMeshFile
{
    /// <summary>The slot names in slot order, or null when the file cannot be read as an .ocmesh.</summary>
    internal static string[]? ReadMaterialSlots(string file)
    {
        try
        {
            using var s = new FileStream(file, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite);
            using var r = new BinaryReader(s);
            if (s.Length < 64 || r.ReadUInt32() != Fourcc("AVR1")) return null;
            s.Position = 20;
            uint count = r.ReadUInt32();
            ulong dir = r.ReadUInt64();
            if (count > 4096 || dir > (ulong)s.Length || (ulong)count * 40 > (ulong)s.Length - dir) return null;
            byte[]? strt = null, madr = null;
            for (uint i = 0; i < count; i++)
            {
                s.Position = (long)dir + i * 40;
                uint id = r.ReadUInt32();
                s.Position += 4;   // version, compression, flags
                ulong offset = r.ReadUInt64(), size = r.ReadUInt64();
                if (id != Fourcc("STRT") && id != Fourcc("MADR")) continue;
                if (offset > (ulong)s.Length || size > (ulong)s.Length - offset) return null;
                s.Position = (long)offset;
                byte[] data = r.ReadBytes((int)size);
                if (id == Fourcc("STRT")) strt = data; else madr = data;
            }
            if (madr == null || madr.Length < 4) return System.Array.Empty<string>();
            uint n = BitConverter.ToUInt32(madr, 0);
            if (n > 65536 || (ulong)n * 4 > (ulong)madr.Length - 4) return null;
            var names = new string[n];
            for (int i = 0; i < n; i++) names[i] = StringAt(strt, BitConverter.ToUInt32(madr, 4 + i * 4));
            return names;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OverflowException) { return null; }
    }

    private static uint Fourcc(string s) => s[0] | (uint)s[1] << 8 | (uint)s[2] << 16 | (uint)s[3] << 24;

    // The string table: entries of a u16 length and UTF-8 bytes, found by byte offset; offset 0 is the empty string.
    private static string StringAt(byte[]? table, uint offset)
    {
        if (table == null || offset == 0xFFFFFFFFu || offset > table.Length || table.Length - offset < 2) return "";
        int len = BitConverter.ToUInt16(table, (int)offset);
        return table.Length - offset - 2 < len ? "" : System.Text.Encoding.UTF8.GetString(table, (int)offset + 2, len);
    }
}
