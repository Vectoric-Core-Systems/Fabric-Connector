// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Wave 2 (images, textures and materials), the managed side: Image and ImageTexture (E8), the procedural and alias texture classes (F26), the material values a script
// changes reaching the engine's material (P14, with a recording backend), the sampler and colour fields read back from an .ocmat (P1, F35) and the per-surface
// overrides of a MeshInstance3D (P2). What needs a GPU (the pixels on screen) is a headed check; what runs here is what decides it.

using Aver.Fabric;

static class Wave2Tests
{
    public static int RunAll() => T.Suite(() =>
    {
        T.Run("wave 2: Image pixels, fill, blit, resize, convert and PNG", ImagePixels);
        T.Run("wave 2: ImageTexture keys, updates and its material path", ImageTextureHost);
        T.Run("wave 2: gradient, curve and atlas textures draw their pictures", ProceduralPictures);
        T.Run("wave 2: FastNoiseLite is deterministic, in range and drives a NoiseTexture2D", NoiseTextures);
        T.Run("wave 2: texture aliases and CanvasTexture", TextureAliases);
        T.Run("wave 2: a BaseMaterial3D's changes reach the engine material", MaterialPush);
        T.Run("wave 2: a change made before the engine has the material is given later", MaterialPending);
        T.Run("wave 2: texture slots take file, runtime and view textures", MaterialTextureSlots);
        T.Run("wave 2: an .ocmat's sampler, channels and emissive energy read back", OcmatReadBack);
        T.Run("wave 2: surface overrides of a MeshInstance3D", SurfaceOverrides);
    });

    // ---- E8 ----

    private static void ImagePixels()
    {
        var img = Image.Create(4, 3, false, Image.Format.Rgba8);
        T.Check("a new image has its size and format", img.GetWidth() == 4 && img.GetHeight() == 3 && img.GetFormat() == Image.Format.Rgba8 && !img.IsEmpty());
        img.SetPixel(1, 1, new Color(1, 0, 0, 1));
        T.Check("a pixel reads back", img.GetPixel(1, 1).R == 1f && img.GetPixel(1, 1).G == 0f && img.GetPixel(0, 0).A == 0f);
        img.SetPixel(99, 99, new Color(1, 1, 1, 1));
        T.Check("a pixel outside is ignored", img.GetPixel(99, 99).A == 0f);
        img.Fill(new Color(0, 1, 0, 1));
        img.FillRect(new Rect2I(0, 0, 2, 1), new Color(0, 0, 1, 1));
        T.Check("fill and fill_rect", img.GetPixel(3, 2).G == 1f && img.GetPixel(1, 0).B == 1f && img.GetPixel(2, 0).B == 0f);

        var src = Image.Create(2, 2, false, Image.Format.Rgba8);
        src.Fill(new Color(1, 1, 0, 1));
        var dst = Image.Create(4, 4, false, Image.Format.Rgba8);
        dst.BlitRect(src, new Rect2I(0, 0, 2, 2), new Vector2I(1, 1));
        T.Check("blit_rect lands at the destination and clips", dst.GetPixel(1, 1).R == 1f && dst.GetPixel(2, 2).G == 1f && dst.GetPixel(3, 3).A == 0f);

        var small = Image.Create(8, 4, false, Image.Format.Rgba8);
        small.Fill(new Color(1, 1, 1, 1));
        small.Resize(4, 2, Image.Interpolation.Nearest);
        T.Check("resize", small.GetWidth() == 4 && small.GetHeight() == 2 && small.GetPixel(3, 1).R == 1f);

        var l8 = Image.Create(2, 2, false, Image.Format.L8);
        l8.SetPixel(0, 0, new Color(1, 1, 1, 1));
        l8.Convert(Image.Format.Rgba8);
        T.Check("convert keeps the picture and changes the format", l8.GetFormat() == Image.Format.Rgba8 && l8.GetPixel(0, 0).R == 1f && l8.GetPixel(0, 0).A == 1f);

        var data = Image.CreateFromData(2, 1, false, Image.Format.Rgba8, new byte[] { 255, 0, 0, 255, 0, 255, 0, 255 });
        T.Check("create_from_data", data.GetPixel(0, 0).R == 1f && data.GetPixel(1, 0).G == 1f);
        T.Check("a wrong byte count gives an empty image", Image.CreateFromData(2, 2, false, Image.Format.Rgba8, new byte[3]).IsEmpty());
        T.Check("rg8 is held in the nearest native format and reports its own", Image.Create(2, 2, false, Image.Format.Rg8).GetFormat() == Image.Format.Rg8);
        T.Check("get_data is a copy", data.GetData().Length == 8);

        var region = img.GetRegion(new Rect2I(1, 1, 2, 2));
        T.Check("get_region", region.GetWidth() == 2 && region.GetHeight() == 2);
        T.Check("an empty image answers neutrally", new Image().IsEmpty() && new Image().GetWidth() == 0 && new Image().GetPixel(0, 0).A == 0f);

        byte[] png = data.SavePngToBuffer();
        T.Check("the PNG has the signature and an IHDR with the size", png.Length > 40 && png[0] == 0x89 && png[1] == (byte)'P' && png[16] == 0 && png[19] == 2 && png[23] == 1);
        string tmp = Path.Combine(Path.GetTempPath(), "aver-fabric-w2-image.png");
        T.Check("save_png writes a file", data.SavePng(tmp) == Error.Ok && File.Exists(tmp));
        try { File.Delete(tmp); } catch (IOException) { }
    }

    private sealed class FakeTextures : IRuntimeTextureHost
    {
        public readonly List<string> Calls = new();
        public bool Create(string key, Image image, bool srgb, bool mipmaps) { Calls.Add($"create {key} {image.GetWidth()}x{image.GetHeight()} srgb={srgb} mips={mipmaps}"); return true; }
        public bool Update(string key, Image image, bool srgb) { Calls.Add($"update {key} {image.GetWidth()}x{image.GetHeight()}"); return true; }
        public bool Destroy(string key) { Calls.Add("destroy " + key); return true; }
        public ulong GpuHandle(string key) => 77;
    }

    private static void ImageTextureHost()
    {
        var fake = new FakeTextures();
        IRuntimeTextureHost saved = ImageTexture.Host;
        ImageTexture.Host = fake;
        try
        {
            var img = Image.Create(8, 4, true, Image.Format.Rgba8);
            var tex = ImageTexture.CreateFromImage(img);
            T.Check("the texture is created with its size and the image's mip request", fake.Calls.Count == 1 && fake.Calls[0].Contains("8x4") && fake.Calls[0].Contains("mips=True"), string.Join(";", fake.Calls));
            T.Check("a material names it by the runtime reference", tex.MaterialPath.StartsWith("@rt:rtex.ft", StringComparison.Ordinal));
            T.Check("size, width and height", tex.GetSize() == new Vector2(8, 4) && tex.GetWidth() == 8 && tex.GetHeight() == 4);
            T.Check("the UI handle is the engine's GPU handle", tex.EngineHandle == 77);
            var bigger = Image.Create(16, 16, false, Image.Format.Rgba8);
            tex.SetImage(bigger);
            T.Check("set_image on a made texture updates it (and its size)", fake.Calls[^1].StartsWith("update", StringComparison.Ordinal) && tex.GetWidth() == 16);
            tex.Update(Image.Create(16, 16, false, Image.Format.Rgba8));
            T.Check("update goes to the engine", fake.Calls[^1].StartsWith("update", StringComparison.Ordinal));
            T.Check("get_image returns a copy", !ReferenceEquals(tex.GetImage(), tex.GetImage()) && tex.GetImage().GetWidth() == 16);
            T.Check("an empty image changes nothing", fake.Calls.Count == 3 && Act(() => tex.SetImage(new Image())) && fake.Calls.Count == 3);
            tex.Release();
            T.Check("release frees the engine texture", fake.Calls[^1].StartsWith("destroy ft", StringComparison.Ordinal));
        }
        finally { ImageTexture.Host = saved; }
    }

    private static bool Act(Action a) { a(); return true; }

    // ---- F26 ----

    private static void ProceduralPictures()
    {
        var g = new Gradient { Offsets = new[] { 0f, 1f }, Colors = new[] { new Color(0, 0, 0, 1), new Color(1, 1, 1, 1) } };
        var t1 = new GradientTexture1D { Gradient = g, Width = 5 };
        Image i1 = t1.GetImage();
        T.Check("a 1D gradient is width x 1 from the first colour to the last", i1.GetWidth() == 5 && i1.GetHeight() == 1 && i1.GetPixel(0, 0).R == 0f && i1.GetPixel(4, 0).R == 1f && T.Near(i1.GetPixel(2, 0).R, 0.5f, 0.01f));
        var t2 = new GradientTexture2D { Gradient = g, Width = 5, Height = 3, Fill = GradientTexture2D.FillEnum.Linear, FillFrom = new Vector2(0, 0), FillTo = new Vector2(1, 0) };
        Image i2 = t2.GetImage();
        T.Check("a linear 2D gradient runs left to right", i2.GetPixel(0, 1).R == 0f && i2.GetPixel(4, 1).R == 1f && i2.GetPixel(2, 0).R == i2.GetPixel(2, 2).R);
        var radial = new GradientTexture2D { Gradient = g, Width = 9, Height = 9, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f) };
        Image ir = radial.GetImage();
        T.Check("a radial gradient is dark in the middle and light at the edge", ir.GetPixel(4, 4).R < 0.05f && ir.GetPixel(0, 4).R > 0.95f);

        var curve = new Curve();
        curve.AddPoint(new Vector2(0, 0));
        curve.AddPoint(new Vector2(1, 1));
        var ct = new CurveTexture { Curve = curve, Width = 8, TextureMode = CurveTexture.TextureModeEnum.Red };
        Image ic = ct.GetImage();
        T.Check("a curve texture samples the curve across its width, red only", ic.GetPixel(0, 0).R < 0.01f && ic.GetPixel(7, 0).R > 0.99f && ic.GetPixel(7, 0).G == 0f);

        var atlasSource = Image.Create(4, 4, false, Image.Format.Rgba8);
        atlasSource.FillRect(new Rect2I(2, 2, 2, 2), new Color(1, 0, 0, 1));
        var atlas = new AtlasTexture { Atlas = new ImageTexture(), Region = new Rect2(2, 2, 2, 2) };
        ((ImageTexture)atlas.Atlas).SetImage(atlasSource);
        Image cut = atlas.GetImage();
        T.Check("an atlas texture cuts its region out (and its size is the region's)", cut.GetWidth() == 2 && cut.GetPixel(0, 0).R == 1f && atlas.GetSize() == new Vector2(2, 2));
    }

    private static void NoiseTextures()
    {
        var n = new FastNoiseLite { Seed = 7, Frequency = 0.05f };
        float a = n.GetNoise2D(10.5f, 20.25f), b = n.GetNoise2D(10.5f, 20.25f);
        T.Check("the same point gives the same value", a == b);
        var other = new FastNoiseLite { Seed = 8, Frequency = 0.05f };
        bool differs = false;
        for (int i = 0; i < 10 && !differs; i++) differs = n.GetNoise2D(i * 3.1f, i * 1.7f) != other.GetNoise2D(i * 3.1f, i * 1.7f);
        T.Check("another seed gives another field", differs);
        bool inRange = true, varies = false;
        float first = n.GetNoise2D(0, 0);
        foreach (FastNoiseLite.NoiseTypeEnum type in Enum.GetValues<FastNoiseLite.NoiseTypeEnum>())
        {
            var t = new FastNoiseLite { NoiseType = type, Frequency = 0.1f, Seed = 3 };
            for (int y = 0; y < 20; y++)
                for (int x = 0; x < 20; x++)
                {
                    float v = t.GetNoise2D(x, y);
                    inRange &= v >= -1f && v <= 1f && !float.IsNaN(v);
                    varies |= v != first;
                }
        }
        T.Check("every noise type stays in -1..1 and varies", inRange && varies);
        var tex = new NoiseTexture2D { Noise = n, Width = 32, Height = 16 };
        Image img = tex.GetImage();
        float lo = 1, hi = 0;
        for (int y = 0; y < 16; y++) for (int x = 0; x < 32; x++) { float v = img.GetPixel(x, y).R; lo = MathF.Min(lo, v); hi = MathF.Max(hi, v); }
        T.Check("a noise texture is the size asked, normalised to 0..1", img.GetWidth() == 32 && img.GetHeight() == 16 && lo < 0.05f && hi > 0.95f, $"{lo} {hi}");
        var inverted = new NoiseTexture2D { Noise = n, Width = 32, Height = 16, Invert = true }.GetImage();
        T.Check("invert flips it", T.Near(inverted.GetPixel(3, 3).R, 1f - img.GetPixel(3, 3).R, 0.01f));
        var normal = new NoiseTexture2D { Noise = n, Width = 16, Height = 16, AsNormalMap = true }.GetImage();
        T.Check("as_normal_map points mostly up (blue high)", normal.GetPixel(8, 8).B > 0.5f);
        var ramp = new Gradient { Offsets = new[] { 0f, 1f }, Colors = new[] { new Color(1, 0, 0, 1), new Color(0, 0, 1, 1) } };
        var coloured = new NoiseTexture2D { Noise = n, Width = 8, Height = 8, ColorRamp = ramp }.GetImage();
        T.Check("a colour ramp colours it", T.Near(coloured.GetPixel(2, 2).R + coloured.GetPixel(2, 2).B, 1f, 0.02f));
    }

    private static void TextureAliases()
    {
        T.Check("the aliases are textures", typeof(Texture).IsAssignableFrom(typeof(Texture2D)) && typeof(Texture2D).IsAssignableFrom(typeof(CompressedTexture2D)) &&
                                            typeof(Texture2D).IsAssignableFrom(typeof(PlaceholderTexture2D)) && typeof(Texture2D).IsAssignableFrom(typeof(ImageTexture)));
        var placeholder = new PlaceholderTexture2D { Size = new Vector2(4, 8) };
        T.Check("a placeholder has its size and no picture", placeholder.GetWidth() == 4 && placeholder.GetHeight() == 8 && placeholder.GetImage().IsEmpty());
        var compressed = new CompressedTexture2D();
        T.Check("load on a missing file reports it", compressed.Load("res://nowhere.png") == Error.FileNotFound);
        var fake = new FakeTextures();
        IRuntimeTextureHost saved = ImageTexture.Host;
        ImageTexture.Host = fake;
        try
        {
            var diffuse = ImageTexture.CreateFromImage(Image.Create(2, 2, false, Image.Format.Rgba8));
            var canvas = new CanvasTexture { DiffuseTexture = diffuse };
            T.Check("a canvas texture draws as its diffuse texture", canvas.GetSize() == diffuse.GetSize() && canvas.EngineHandle == 77);
        }
        finally { ImageTexture.Host = saved; }
        T.Check("the classes are in the class database", ClassDB.ClassExists("ImageTexture") && ClassDB.ClassExists("GradientTexture2D") && ClassDB.ClassExists("NoiseTexture2D") &&
                                                         ClassDB.ClassExists("FastNoiseLite") && ClassDB.ClassExists("CurveTexture") && ClassDB.ClassExists("AtlasTexture") && ClassDB.ClassExists("Image"));
    }

    // ---- P14 ----

    private sealed class FakeMaterials : IMaterialBackend
    {
        public readonly List<string> Calls = new();
        public int Handle = 5;
        public int Finds;
        public bool Alive = true;   // false: the engine dropped the material, every setter answers false
        private bool Ok(int m) => Alive && m > 0;
        public int Find(string name) { Finds++; Calls.Add("find " + name); return Handle; }
        public bool SetBaseColor(int m, float r, float g, float b, float a) { Calls.Add($"color {m} {r} {g} {b} {a}"); return Ok(m); }
        public bool SetEmissive(int m, float r, float g, float b, float energy) { Calls.Add($"emissive {m} {r:0.###} {g:0.###} {b:0.###} {energy}"); return Ok(m); }
        public bool SetMetallic(int m, float v) { Calls.Add($"metallic {m} {v}"); return Ok(m); }
        public bool SetRoughness(int m, float v) { Calls.Add($"roughness {m} {v}"); return Ok(m); }
        public bool SetNormalScale(int m, float v) { Calls.Add($"normalscale {m} {v}"); return Ok(m); }
        public bool SetUv(int m, int set, float sx, float sy, float ox, float oy) { Calls.Add($"uv{set} {m} {sx} {sy} {ox} {oy}"); return Ok(m); }
        public bool SetSampler(int m, int filter, int address) { Calls.Add($"sampler {m} {filter} {address}"); return Ok(m); }
        public bool SetChannels(int m, int roughness, int metallic) { Calls.Add($"channels {m} {roughness} {metallic}"); return Ok(m); }
        public bool SetVertexColor(int m, bool on, bool srgb) { Calls.Add($"vertexcolor {m} {on} {srgb}"); return Ok(m); }
        public bool SetAlphaExtension(int m, int extension) { Calls.Add($"alphaext {m} {extension}"); return Ok(m); }
        public bool SetTexture(int m, int slot, string? path) { Calls.Add($"texture {m} {slot} {path ?? "-"}"); return Ok(m); }
    }

    private static T WithBackend<T>(FakeMaterials fake, Func<T> body)
    {
        IMaterialBackend saved = BaseMaterial3D.Backend;
        BaseMaterial3D.Backend = fake;
        try { return body(); }
        finally { BaseMaterial3D.Backend = saved; }
    }

    private static BaseMaterial3D Wood() => new StandardMaterial3D { ResourcePath = "res://Materials/M_Wood.ocmat" };

    private static void MaterialPush()
    {
        var fake = new FakeMaterials();
        WithBackend(fake, () =>
        {
            BaseMaterial3D m = Wood();
            m.AlbedoColor = new Color(0.5f, 0.25f, 1f, 1f);
            T.Check("albedo_color finds the material by its file name and sets the colour", fake.Calls.SequenceEqual(new[] { "find M_Wood", "color 5 0.5 0.25 1 1" }), string.Join(";", fake.Calls));
            fake.Calls.Clear();
            m.Metallic = 0.75f;
            m.Roughness = 0.1f;
            T.Check("metallic and roughness", fake.Calls.SequenceEqual(new[] { "metallic 5 0.75", "roughness 5 0.1" }), string.Join(";", fake.Calls));
            fake.Calls.Clear();
            m.EmissionEnabled = true;
            m.Emission = new Color(1, 1, 1, 1);
            m.EmissionEnergyMultiplier = 3f;
            T.Check("emission sends the linear colour and the energy", fake.Calls[^1] == "emissive 5 1 1 1 3", string.Join(";", fake.Calls));
            fake.Calls.Clear();
            m.EmissionEnabled = false;
            T.Check("a disabled emission is black", fake.Calls[^1] == "emissive 5 0 0 0 3", string.Join(";", fake.Calls));
            fake.Calls.Clear();
            m.Uv1Offset = new Vector3(0.5f, 0.25f, 0);
            T.Check("uv1_offset sends the whole transform", fake.Calls[^1] == "uv1 5 1 1 0.5 0.25", string.Join(";", fake.Calls));
            fake.Calls.Clear();
            m.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest;
            m.TextureRepeat = false;
            T.Check("texture_filter nearest is the point sampler; repeat off is clamp", fake.Calls[^1] == "sampler 5 4 1", string.Join(";", fake.Calls));
            fake.Calls.Clear();
            m.VertexColorUseAsAlbedo = true;
            m.VertexColorIsSrgb = true;
            T.Check("vertex colour use and sRGB", fake.Calls[^1] == "vertexcolor 5 True True", string.Join(";", fake.Calls));
            fake.Calls.Clear();
            m.RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Blue;
            T.Check("the roughness channel", fake.Calls[^1] == "channels 5 2 0", string.Join(";", fake.Calls));
            fake.Calls.Clear();
            int finds = fake.Finds;
            m.Metallic = 0.5f;
            T.Check("the handle is remembered", fake.Finds == finds);
            fake.Calls.Clear();
            var anon = new StandardMaterial3D();
            anon.AlbedoColor = new Color(1, 0, 0, 1);
            T.Check("a material with no file is not the engine's", fake.Calls.Count == 0);
            var dup = (BaseMaterial3D)m.Duplicate();
            dup.AlbedoColor = new Color(0, 1, 0, 1);
            T.Check("a duplicate has no path and no engine material", fake.Calls.Count == 0);
            return 0;
        });
    }

    private static void MaterialPending()
    {
        var fake = new FakeMaterials { Handle = 0 };
        WithBackend(fake, () =>
        {
            BaseMaterial3D m = Wood();
            m.AlbedoColor = new Color(1, 0, 0, 1);
            m.Metallic = 0.5f;
            T.Check("an unloaded material keeps the changes owed", fake.Calls.All(c => c.StartsWith("find", StringComparison.Ordinal)));
            fake.Calls.Clear();
            fake.Handle = 9;
            m.SyncToEngine();
            T.Check("a later sync gives them all", fake.Calls.Contains("color 9 1 0 0 1") && fake.Calls.Contains("metallic 9 0.5"), string.Join(";", fake.Calls));
            fake.Calls.Clear();
            m.SyncToEngine();
            T.Check("and only once", fake.Calls.Count == 0);

            // A handle the engine dropped: the next change finds the material again.
            var failing = new FakeMaterials { Handle = 4 };
            BaseMaterial3D.Backend = failing;
            BaseMaterial3D k = Wood();
            k.Roughness = 0.2f;
            failing.Alive = false;   // the engine dropped the material: the setters answer false
            k.Roughness = 0.3f;
            failing.Alive = true;
            failing.Handle = 6;
            failing.Calls.Clear();
            k.Roughness = 0.4f;
            T.Check("a stale handle is found again", failing.Calls[0] == "find M_Wood" && failing.Calls[^1] == "roughness 6 0.4", string.Join(";", failing.Calls));
            return 0;
        });
    }

    private static void MaterialTextureSlots()
    {
        var fake = new FakeMaterials();
        var host = new FakeTextures();
        IRuntimeTextureHost savedHost = ImageTexture.Host;
        ImageTexture.Host = host;
        try
        {
            WithBackend(fake, () =>
            {
                BaseMaterial3D m = Wood();
                var runtime = ImageTexture.CreateFromImage(Image.Create(2, 2, false, Image.Format.Rgba8));
                m.AlbedoTexture = runtime;
                T.Check("a runtime texture is named by its @rt reference", fake.Calls[^1] == $"texture 5 0 {runtime.MaterialPath}", string.Join(";", fake.Calls));
                fake.Calls.Clear();
                m.AlbedoTexture = null;
                T.Check("null clears the slot", fake.Calls[^1] == "texture 5 0 -");
                fake.Calls.Clear();
                var view = new ViewportTexture();
                m.EmissionTexture = view;
                T.Check("a view's texture without a viewport has no reference and clears", fake.Calls[^1] == "texture 5 4 -");
                fake.Calls.Clear();
                m.NormalEnabled = true;
                m.NormalTexture = runtime;
                T.Check("a normal map goes in the normal slot once enabled", fake.Calls[^1] == $"texture 5 2 {runtime.MaterialPath}", string.Join(";", fake.Calls));
                fake.Calls.Clear();
                var noise = new NoiseTexture2D { Noise = new FastNoiseLite(), Width = 4, Height = 4 };
                m.SetTexture(BaseMaterial3D.TextureParam.Roughness, noise);
                T.Check("a generated texture is made and named; set_texture reads back", fake.Calls.Any(c => c.StartsWith("texture 5 1 @rt:rtex.ft", StringComparison.Ordinal)) && ReferenceEquals(m.GetTexture(BaseMaterial3D.TextureParam.Roughness), noise), string.Join(";", fake.Calls));
                return 0;
            });
        }
        finally { ImageTexture.Host = savedHost; }
    }

    // ---- P1, F35 ----

    private static void OcmatReadBack()
    {
        string dir = Path.Combine(Path.GetTempPath(), "aver-fabric-w2-ocmat");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "M_Pixel.ocmat");
        File.WriteAllText(file,
            "OCMAT 1\nNAME M_Pixel\nPARAM baseColorFactor 0.5 0.5 0.5 1\nPARAM emissiveFactor 1 0 0\nPARAM emissiveEnergy 4\nPARAM metallicFactor 0.2\nPARAM roughnessFactor 0.9\n" +
            "FX cull=back texfilter=point texaddr=clamp roughchan=b metalchan=r vcsrgb=1\n");
        try
        {
            var m = new StandardMaterial3D { ResourcePath = "res://Materials/M_Pixel.ocmat" };
            m.ReadOcmat(file);
            T.Check("the filter and addressing read back", m.TextureFilter == BaseMaterial3D.TextureFilterEnum.Nearest && !m.TextureRepeat);
            T.Check("the channels and the sRGB vertex colour read back", m.RoughnessTextureChannel == BaseMaterial3D.TextureChannel.Blue && m.MetallicTextureChannel == BaseMaterial3D.TextureChannel.Red && m.VertexColorIsSrgb);
            T.Check("the emissive energy and colour read back", m.EmissionEnergyMultiplier == 4f && m.EmissionEnabled && m.Emission.R > 0.99f && m.Emission.G < 0.01f);
            T.Check("the values the engine draws read back", T.Near(m.Metallic, 0.2f) && T.Near(m.Roughness, 0.9f) && T.Near(m.AlbedoColor.R, 0.5f));
            var fake = new FakeMaterials();
            WithBackend(fake, () => { m.SyncToEngine(); return 0; });
            T.Check("reading a file pushes nothing to the engine", fake.Calls.Count == 0);
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
        T.Check("every Godot filter maps to a sampler", BaseMaterial3D.SamplerFilterOf(BaseMaterial3D.TextureFilterEnum.Nearest) == 4 && BaseMaterial3D.SamplerFilterOf(BaseMaterial3D.TextureFilterEnum.Linear) == 2 &&
                                                       BaseMaterial3D.SamplerFilterOf(BaseMaterial3D.TextureFilterEnum.NearestWithMipmaps) == 3 && BaseMaterial3D.SamplerFilterOf(BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps) == 1 &&
                                                       BaseMaterial3D.SamplerFilterOf(BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic) == 0);
    }

    // ---- P2 ----

    private static void SurfaceOverrides()
    {
        var mi = new MeshInstance3D();
        var a = new StandardMaterial3D { ResourcePath = "res://Materials/M_A.ocmat" };
        var b = new StandardMaterial3D { ResourcePath = "res://Materials/M_B.ocmat" };
        mi.SetSurfaceOverrideMaterial(2, b);
        mi.SetSurfaceOverrideMaterial(0, a);
        T.Check("the overrides are listed by slot with the material names", mi.SurfaceOverrideNames().SequenceEqual(new[] { (0, "M_A"), (2, "M_B") }));
        T.Check("surface 0 no longer is the material override", mi.MaterialOverride == null && ReferenceEquals(mi.GetSurfaceOverrideMaterial(0), a));
        mi.MaterialOverride = new StandardMaterial3D { ResourcePath = "res://Materials/M_All.ocmat" };
        T.Check("a material_override replaces every surface, so no slot is overridden", mi.SurfaceOverrideNames().Count == 0 && ReferenceEquals(mi.GetActiveMaterial(2), mi.MaterialOverride));
        mi.MaterialOverride = null;
        mi.SetSurfaceOverrideMaterial(2, null);
        T.Check("clearing a slot drops it", mi.SurfaceOverrideNames().SequenceEqual(new[] { (0, "M_A") }));
        mi.SetSurfaceOverrideMaterial(20, b);
        T.Check("a slot past the engine's sixteen is kept and not applied", mi.SurfaceOverrideNames().Count == 1 && ReferenceEquals(mi.GetSurfaceOverrideMaterial(20), b));
        mi.SetSurfaceOverrideMaterial(5, new StandardMaterial3D());
        T.Check("a material with no file is kept and not applied", mi.SurfaceOverrideNames().Count == 1);

        var viaScene = new MeshInstance3D();
        viaScene.ApplyValues(new List<(string Key, string Text)> { ("surface_material_override/1", "Resource(\"Materials/M_C.ocmat\")") });
        T.Check("a scene's surface_material_override/N property reaches the node", viaScene.GetSurfaceOverrideMaterial(1) is { ResourcePath: "Materials/M_C.ocmat" } && viaScene.SurfaceOverrideNames().Count == 1);
    }
}
