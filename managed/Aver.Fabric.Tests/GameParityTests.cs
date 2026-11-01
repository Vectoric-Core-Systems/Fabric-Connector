// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// What running a converted game turned up, as general behaviours: a mesh file's material slots as surfaces, Skin's bind properties, MeshInstance3D.skeleton and the
// other renamed properties, and one log line per downgraded node class instead of one per property. Nothing here names a game.

using Aver.Fabric;

static class GameParityTests
{
    public static int RunAll() => T.Suite(() =>
    {
        T.Run("game parity: an .ocmesh reports its material slots as surfaces, each with its .ocmat", MeshSurfaces);
        T.Run("game parity: a material from a mesh survives duplicate, override and surface writes like Godot's", MaterialRoundTrip);
        T.Run("game parity: a file that is not an .ocmesh has no surfaces and nothing throws", NotAMesh);
        T.Run("game parity: Skin takes bind_count and bind/N/name, bone and pose from a scene", SkinBinds);
        T.Run("game parity: MeshInstance3D.skeleton is the property, and the other renames Godot's C# names need", RenamedProperties);
        T.Run("game parity: a downgraded node keeps its properties without a warning each", StandInNode);
    });

    // An AVR1 container with the STRT and MADR chunks of an .ocmesh: what the engine's writer lays out, header CRC and chunk hashes left empty.
    private static byte[] MeshFile(params string[] slots)
    {
        var strt = new List<byte> { 0, 0 };   // offset 0 is the empty string
        var refs = new List<uint>();
        foreach (string s in slots)
        {
            refs.Add(s.Length == 0 ? 0u : (uint)strt.Count);
            if (s.Length == 0) continue;
            byte[] b = System.Text.Encoding.UTF8.GetBytes(s);
            strt.AddRange(BitConverter.GetBytes((ushort)b.Length));
            strt.AddRange(b);
        }
        var madr = new List<byte>(BitConverter.GetBytes((uint)slots.Length));
        foreach (uint r in refs) madr.AddRange(BitConverter.GetBytes(r));
        var chunks = new[] { ("MADR", madr.ToArray()), ("STRT", strt.ToArray()) };
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("AVR1"u8.ToArray());
        w.Write("MESH"u8.ToArray());
        w.Write(new byte[56]);   // the rest of the 64-byte header
        long dir = ms.Position;
        w.Write(new byte[40 * chunks.Length]);
        var at = new List<(long Offset, int Size)>();
        foreach ((string _, byte[] data) in chunks)
        {
            at.Add((ms.Position, data.Length));
            w.Write(data);
        }
        ms.Position = 20; w.Write((uint)chunks.Length); w.Write((ulong)dir);
        for (int i = 0; i < chunks.Length; i++)
        {
            ms.Position = dir + i * 40;
            w.Write(System.Text.Encoding.ASCII.GetBytes(chunks[i].Item1));
            ms.Position += 4;
            w.Write((ulong)at[i].Offset); w.Write((ulong)at[i].Size); w.Write((ulong)at[i].Size);
        }
        return ms.ToArray();
    }

    private static string Content(Action<string> body)
    {
        string dir = Path.Combine(Path.GetTempPath(), "fabric_parity_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "Meshes"));
        Directory.CreateDirectory(Path.Combine(dir, "Materials"));
        try
        {
            ResourceLoader.ContentRootOverride = dir;
            body(dir);
        }
        finally
        {
            ResourceLoader.ContentRootOverride = null;
            try { Directory.Delete(dir, true); } catch (IOException) { /* a leftover temp folder is harmless */ }
        }
        return dir;
    }

    private static void MeshSurfaces()
    {
        T.Reset();
        Content(dir =>
        {
            File.WriteAllBytes(Path.Combine(dir, "Meshes", "m.ocmesh"), MeshFile("Red", "", "Plain", "Gone"));
            File.WriteAllText(Path.Combine(dir, "Materials", "Red.ocmat"), "OCMAT 1\nNAME Red\nPARAM baseColorFactor 0.5 0.25 0.125 0.75\nPARAM metallicFactor 0.3\nPARAM roughnessFactor 0.6\n");
            File.WriteAllText(Path.Combine(dir, "Materials", "Plain.ocmat"), "OCMAT 1\nNAME Plain\n");
            var mesh = ResourceLoader.Load<Mesh>("res://Meshes/m.ocmesh")!;
            T.Check("the file loads as a Mesh", mesh != null);
            T.Check("its surfaces are the file's material slots", mesh!.GetSurfaceCount() == 4, mesh.GetSurfaceCount().ToString());
            var red = mesh.SurfaceGetMaterial(0) as StandardMaterial3D;
            T.Check("a slot has the .ocmat of its name", red != null && red.ResourcePath.EndsWith("Materials/Red.ocmat"));
            T.Check("with the colour, metallic and roughness the file holds", red != null && red.AlbedoColor == new Color(0.5f, 0.25f, 0.125f, 0.75f) && T.Near(red.Metallic, 0.3f) && T.Near(red.Roughness, 0.6f));
            T.Check("an .ocmat that sets none keeps Godot's defaults", mesh.SurfaceGetMaterial(2) is StandardMaterial3D { Roughness: 1f } plain && plain.AlbedoColor == Colors.White);
            T.Check("a slot with no name has no material", mesh.SurfaceGetMaterial(1) == null);
            T.Check("a slot whose file is missing still gives a material to hold", mesh.SurfaceGetMaterial(3) is StandardMaterial3D);
            T.Check("every ask is the same object, as Godot's resource is", ReferenceEquals(mesh.SurfaceGetMaterial(0), red));
            T.Check("a surface out of range is null", mesh.SurfaceGetMaterial(4) == null && mesh.SurfaceGetMaterial(-1) == null);
            var mi = new MeshInstance3D { Mesh = mesh };
            T.Check("the node's active material is the mesh's (before: null)", ReferenceEquals(mi.GetActiveMaterial(0), red) && mi.GetSurfaceOverrideMaterialCount() == 4);
            T.Check("and a loop over the surfaces finds each", Enumerable.Range(0, mesh.GetSurfaceCount()).Count(i => mi.GetActiveMaterial(i) is StandardMaterial3D) == 3);
        });
    }

    private static void MaterialRoundTrip()
    {
        T.Reset();
        Content(dir =>
        {
            File.WriteAllBytes(Path.Combine(dir, "Meshes", "m.ocmesh"), MeshFile("Red", "Blue"));
            File.WriteAllText(Path.Combine(dir, "Materials", "Red.ocmat"), "OCMAT 1\nNAME Red\nPARAM baseColorFactor 1 0 0 1\n");
            File.WriteAllText(Path.Combine(dir, "Materials", "Blue.ocmat"), "OCMAT 1\nNAME Blue\nPARAM baseColorFactor 0 0 1 1\n");
            var mi = new MeshInstance3D { Mesh = ResourceLoader.Load<Mesh>("res://Meshes/m.ocmesh") };
            Material original = mi.GetActiveMaterial(0)!;
            var copy = (StandardMaterial3D)original.Duplicate();
            mi.MaterialOverride = copy;
            copy.AlbedoColor = new Color(1, 0, 0, 0.1f);
            T.Check("the duplicate is a separate material with no path", !ReferenceEquals(copy, original) && copy.ResourcePath.Length == 0);
            T.Check("it is the override and the active material", ReferenceEquals(mi.MaterialOverride, copy) && ReferenceEquals(mi.GetActiveMaterial(0), copy));
            T.Check("changing it leaves the mesh's material alone", ((StandardMaterial3D)original).AlbedoColor == new Color(1, 0, 0, 1));
            mi.MaterialOverride = null;
            T.Check("clearing the override gives the mesh's material back", ReferenceEquals(mi.GetActiveMaterial(0), original));
            var other = new StandardMaterial3D();
            mi.Mesh!.SurfaceSetMaterial(1, other);
            T.Check("a material set on the mesh's surface replaces the slot's", ReferenceEquals(mi.GetActiveMaterial(1), other));
            var dup = (Mesh)mi.Mesh.Duplicate();
            T.Check("a copy of the mesh has the surfaces and still draws the file", dup.GetSurfaceCount() == 2 && MeshInstance3D.MeshAssetOf(dup) == "Meshes/m.ocmesh" && ReferenceEquals(dup.SurfaceGetMaterial(1), other));
        });
    }

    private static void NotAMesh()
    {
        T.Reset();
        Content(dir =>
        {
            File.WriteAllText(Path.Combine(dir, "Meshes", "text.ocmesh"), "this is not a container");
            byte[] truncated = MeshFile("A", "B");
            File.WriteAllBytes(Path.Combine(dir, "Meshes", "cut.ocmesh"), truncated[..70]);
            T.Check("text in the file gives no surfaces", ResourceLoader.Load<Mesh>("res://Meshes/text.ocmesh")!.GetSurfaceCount() == 0);
            T.Check("a file cut short gives none either", ResourceLoader.Load<Mesh>("res://Meshes/cut.ocmesh")!.GetSurfaceCount() == 0);
            T.Check("a mesh with no file behind it gives none", ResourceLoader.Load<Mesh>("res://Meshes/none.ocmesh")!.GetSurfaceCount() == 0);
            T.Check("and none of it was an error", T.ErrorCount() == 0);
        });
    }

    private static void SkinBinds()
    {
        T.Reset();
        var skin = new Skin();
        skin.Set("bind_count", 3);
        skin.Set("bind/0/name", new StringName("root"));
        skin.Set("bind/0/bone", 0);
        skin.Set("bind/1/name", new StringName("hip"));
        skin.Set("bind/1/bone", 7);
        var pose = new Transform3D(Basis.Identity, new Vector3(1, 2, 3));
        skin.Set("bind/2/pose", pose);
        T.Check("bind_count sizes the skin", skin.GetBindCount() == 3 && skin.BindCount == 3);
        T.Check("the names and bones land on their binds", skin.GetBindName(1) == "hip" && skin.GetBindBone(1) == 7 && skin.GetBindName(0) == "root" && skin.GetBindBone(0) == 0);
        T.Check("the pose lands on its bind", skin.GetBindPose(2) == pose);
        T.Check("the properties read back", skin.Get("bind/1/name").AsString() == "hip" && skin.Get("bind/1/bone").AsInt32() == 7 && skin.Get("bind_count").AsInt32() == 3);
        T.Check("none of it warned", T.Logs.All(l => l.Level != Aver.Scripting.Log.Level.Warn));
        T.Logs.Clear();
        bool parsed = VariantText.TryParse("Object(Skin, \"bind_count\": 2, \"bind/0/name\": &\"a\", \"bind/0/bone\": 4, \"bind/1/pose\": Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 5, 0, 0))", out Variant v);
        var fromText = v.AsGodotObject() as Skin;
        T.Check("a scene's Object(Skin, ...) value builds the binds without a warning",
            parsed && fromText != null && fromText.GetBindCount() == 2 && fromText.GetBindBone(0) == 4 && fromText.GetBindName(0) == "a" && fromText.GetBindPose(1).Origin == new Vector3(5, 0, 0)
            && T.Logs.All(l => l.Level != Aver.Scripting.Log.Level.Warn), string.Join("|", T.Logs.Select(l => l.Message)));
        skin.BindCount = 1;
        T.Check("fewer binds drops the rest", skin.GetBindCount() == 1);
    }

    private static void RenamedProperties()
    {
        T.Reset();
        var mi = new MeshInstance3D();
        T.Check("skeleton defaults to the parent", mi.Skeleton.ToString() == "..");
        mi.Set("skeleton", new NodePath("../.."));
        T.Check("a scene's skeleton property is set, not kept in metadata", mi.Skeleton.ToString() == "../.." && T.Logs.Count == 0, string.Join("|", T.Logs.Select(l => l.Message)));
        mi.ApplyValues(new[] { ("skeleton", "NodePath(\"\")") });
        T.Check("an empty one too, with no warning", mi.Skeleton.IsEmpty && T.Logs.All(l => l.Level != Aver.Scripting.Log.Level.Warn));
        var sk = new Skeleton3D();
        sk.AddBone("b");
        var skinned = new MeshInstance3D { Skeleton = new NodePath("..") };
        sk.AddChild(skinned);
        skinned.Skin = sk.CreateSkinFromRestTransforms();
        T.Check("get_skin_reference follows the property", skinned.GetSkinReference()?.GetSkin() == skinned.Skin);
        var c = new Control();
        c.Set("layout_direction", 3);
        T.Check("Control.layout_direction is Godot's LayoutDirection (the enum is LayoutDirectionEnum)", c.LayoutDirection == Control.LayoutDirectionEnum.Rtl && c.IsLayoutRtl());
        var bar = new ProgressBar();
        bar.Ratio = 0.25;
        T.Check("Range.ratio sets and reads the value as a share", T.Near((float)bar.Value, (float)(bar.MinValue + (bar.MaxValue - bar.MinValue) * 0.25)) && T.Near((float)bar.Ratio, 0.25f));
    }

    private static void StandInNode()
    {
        T.Reset();
        Node.ResetStandInLog();
        int Warns() => T.Logs.Count(l => l.Level == Aver.Scripting.Log.Level.Warn);
        var first = new Node { Name = "Screenshot" };
        first.ApplyValues(new[] { ("_class", "\"Sprite2D\""), ("texture", "1"), ("centered", "true"), ("visible", "false") });
        T.Check("a stand-in logs one line for its three properties", Warns() == 1 && T.Logs.Any(l => l.Message.Contains("Sprite2D") && l.Message.Contains("metadata")), string.Join("|", T.Logs.Select(l => l.Message)));
        T.Check("and keeps them for GetMeta", first.GetMeta("centered").AsBool() && first.HasMeta("texture") && !first.GetMeta("visible").AsBool());
        var second = new Node { Name = "Other" };
        second.ApplyValues(new[] { ("texture", "2"), ("_class", "\"Sprite2D\"") });
        T.Check("a second node of the class adds no line, whatever the order of its keys", Warns() == 1);
        var third = new Node { Name = "Mixed" };
        third.ApplyValues(new[] { ("_class", "\"Camera2D\""), ("zoom", "1") });
        T.Check("another class adds its own line", Warns() == 2);
        var plain = new Node { Name = "Plain" };
        plain.ApplyValues(new[] { ("texture", "1"), ("centered", "true") });
        T.Check("a node that is its own class still warns per property", Warns() == 4 && T.Logs.Count(l => l.Message.Contains("has no property")) == 2);
        var carrier = new Node3D { Name = "Marker" };
        carrier.ApplyValues(new[] { ("_class", "\"Node3D\""), ("nonsense", "1") });
        T.Check("a _class equal to the runtime class is no stand-in", Warns() == 5);
    }
}
