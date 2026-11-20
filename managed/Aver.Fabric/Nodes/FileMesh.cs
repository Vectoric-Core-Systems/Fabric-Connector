// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// A mesh loaded from an .ocmesh file: its material slots (the file's MADR chunk) are the surfaces, each with the .ocmat of that name, and its first
// level's triangles (MHDR, VTXS, IDXS) are what get_faces, the shapes made from the mesh and a soft body read.

namespace Aver.Fabric;

/// <summary>What ResourceLoader makes of an .ocmesh. The engine draws the geometry; the surfaces are the file's material slots, so a script that asks the
/// mesh for its surface count or a surface's material gets what Godot would give it, and the triangles are read from the file when something asks for them
/// (get_faces, create_trimesh_shape, a SoftBody3D), as Godot reads an imported mesh's arrays.</summary>
internal sealed class FileMesh : Mesh
{
    private string[]? _slots;
    private MeshSurface? _triangles;
    private bool _trianglesRead;
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

    /// <summary>The first level's triangles, every material slot together, in Godot's space (metres, Y up). Null when the file cannot be read for them.</summary>
    internal MeshSurface? Triangles()
    {
        if (_trianglesRead) return _triangles;
        _trianglesRead = true;
        string? file = ResourcePath.Length == 0 ? null : ResourceLoader.FindFile(ResourcePath);
        if (file == null || OcMeshFile.ReadTriangles(file) is not { } t) return null;
        return _triangles = new MeshSurface { Vertices = t.Positions, Indices = t.Indices };
    }

    public override Vector3[] GetFaces()
    {
        if (Triangles() is not { Indices: { } idx } s) return System.Array.Empty<Vector3>();
        var faces = new Vector3[idx.Length];
        for (int i = 0; i < idx.Length; i++) faces[i] = s.Vertices[idx[i]];
        return faces;
    }

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
        if (ReadChunks(file, "STRT", "MADR") is not { } chunks) return null;
        chunks.TryGetValue("STRT", out byte[]? strt);
        if (!chunks.TryGetValue("MADR", out byte[]? madr) || madr.Length < 4) return System.Array.Empty<string>();
        uint n = BitConverter.ToUInt32(madr, 0);
        if (n > 65536 || (ulong)n * 4 > (ulong)madr.Length - 4) return null;
        var names = new string[n];
        for (int i = 0; i < n; i++) names[i] = StringAt(strt, BitConverter.ToUInt32(madr, 4 + i * 4));
        return names;
    }

    /// <summary>The first level of detail's triangles (docs/formats/FORMAT_SPECS.md, .ocmesh: MHDR, the LOD table, the submesh ranges, VTXS, IDXS), every
    /// submesh with its base vertex added, in Godot's space and winding. Null when the file is not an .ocmesh or its positions are quantized.</summary>
    internal static (Vector3[] Positions, int[] Indices)? ReadTriangles(string file)
    {
        if (ReadChunks(file, "MHDR", "VTXS", "IDXS") is not { } c || !c.TryGetValue("MHDR", out byte[]? h) || !c.TryGetValue("VTXS", out byte[]? vtx) ||
            !c.TryGetValue("IDXS", out byte[]? idx) || h.Length < 0x38)
            return null;
        try
        {
            uint flags = BitConverter.ToUInt32(h, 0);
            int lods = h[4], submeshes = h[5], streams = h[6];
            if (lods < 1 || (flags & (1u << 4)) != 0) return null;   // quantized positions are not read
            bool index32 = (flags & (1u << 3)) != 0;
            int lodTable = 0x38 + streams * 8;
            int rangeTable = lodTable + lods * 56 + submeshes * 12;
            if (h.Length < lodTable + 56) return null;
            uint vertexCount = BitConverter.ToUInt32(h, lodTable), indexCount = BitConverter.ToUInt32(h, lodTable + 4);
            ulong vtxOffset = BitConverter.ToUInt64(h, lodTable + 8), idxOffset = BitConverter.ToUInt64(h, lodTable + 0x18);
            int indexSize = index32 ? 4 : 2;
            if (vertexCount == 0 || indexCount < 3 || vtxOffset + vertexCount * 12UL > (ulong)vtx.Length || idxOffset + indexCount * (ulong)indexSize > (ulong)idx.Length)
                return null;
            var positions = new Vector3[vertexCount];
            for (int v = 0; v < positions.Length; v++)
            {
                int o = (int)vtxOffset + v * 12;
                positions[v] = FabricSpace.FromAverPosition(new Aver.Scene.Vec3(BitConverter.ToSingle(vtx, o), BitConverter.ToSingle(vtx, o + 4), BitConverter.ToSingle(vtx, o + 8)));
            }
            uint Index(uint k) => index32 ? BitConverter.ToUInt32(idx, (int)(idxOffset + k * 4)) : BitConverter.ToUInt16(idx, (int)(idxOffset + k * 2));
            var indices = new List<int>((int)indexCount);
            void Range(uint start, uint count, uint baseVertex)
            {
                for (uint t = 0; t + 2 < count; t += 3)
                {
                    uint a = Index(start + t) + baseVertex, b = Index(start + t + 1) + baseVertex, cc = Index(start + t + 2) + baseVertex;
                    if (a >= vertexCount || b >= vertexCount || cc >= vertexCount) continue;
                    indices.Add((int)a); indices.Add((int)cc); indices.Add((int)b);   // the axis map mirrors the space: the winding turns back
                }
            }
            if (submeshes == 0 || h.Length < rangeTable + submeshes * 40) Range(0, indexCount, 0);
            else
                for (int s = 0; s < submeshes; s++)
                {
                    int o = rangeTable + s * 40;   // level 0's ranges come first
                    uint start = BitConverter.ToUInt32(h, o), count = BitConverter.ToUInt32(h, o + 4), baseVertex = BitConverter.ToUInt32(h, o + 8);
                    if ((ulong)start + count <= indexCount) Range(start, count, baseVertex);
                }
            return indices.Count < 3 ? null : (positions, indices.ToArray());
        }
        catch (ArgumentException) { return null; }
    }

    // The chunks named, by their four-character id; null when the file cannot be read as an AVR1 container (a 64-byte header, then a directory of 40-byte
    // entries). Uncompressed chunks only, which is all the container allows.
    private static Dictionary<string, byte[]>? ReadChunks(string file, params string[] ids)
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
            var want = ids.ToDictionary(Fourcc, id => id);
            var found = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            for (uint i = 0; i < count; i++)
            {
                s.Position = (long)dir + i * 40;
                uint id = r.ReadUInt32();
                s.Position += 4;   // version, compression, flags
                ulong offset = r.ReadUInt64(), size = r.ReadUInt64();
                if (!want.TryGetValue(id, out string? name)) continue;
                if (offset > (ulong)s.Length || size > (ulong)s.Length - offset || size > int.MaxValue) return null;
                s.Position = (long)offset;
                found[name] = r.ReadBytes((int)size);
            }
            return found;
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
