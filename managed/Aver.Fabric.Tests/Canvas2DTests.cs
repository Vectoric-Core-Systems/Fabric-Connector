// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// 2D scenes at run time (docs/plans/RENDER_2D.md in the engine): Node2D, Sprite2D and CanvasLayer keep their state on the engine's 2D components, and the Control replay queues its
// draws in the engine's canvas under the shared key (canvas layer, z, tree order). The native scene library cannot sit beside the managed assembly of the same name in a test process,
// so the engine calls go through a recording backend that also sorts as the engine's canvas does (modules/scene/src/Canvas2D.cpp, tested natively by Scene2dSubmitTest); the
// Control draws use the real Aver.UI.Abi, and the suite skips them without it.

using Aver.Fabric;
using CanvasScaleMode = Aver.Framework.CanvasScaleMode;
using CanvasStretchAspect = Aver.Framework.CanvasStretchAspect;
using CanvasStretchMode = Aver.Framework.CanvasStretchMode;
using Layer2DFlags = Aver.Framework.Layer2DFlags;
using Sprite2DFlags = Aver.Framework.Sprite2DFlags;
using Transform2DFlags = Aver.Framework.Transform2DFlags;

static class Canvas2DTests
{
    /// <summary>Records every engine call and flushes by the engine's rule: sprites and queued external draws sorted by (layer, z, tree order), submission order breaking ties.</summary>
    private sealed class FakeCanvas : ICanvas2DBackend
    {
        public bool Available => true;
        public bool Register() => true;
        private int _next = 100;
        public readonly Dictionary<int, string> Names = new();
        public readonly Dictionary<int, int> Parent = new();
        public readonly HashSet<int> Dead = new();
        public readonly Dictionary<int, Aver.Framework.Transform2D> T2 = new();
        public readonly Dictionary<int, Aver.Framework.Sprite2D> S2 = new();
        public readonly Dictionary<int, Aver.Framework.Layer2D> L2 = new();
        public readonly List<(int Layer, int Z, long Order, Action Draw)> Externals = new();
        public int Writes;
        public (CanvasStretchMode Mode, CanvasStretchAspect Aspect, CanvasScaleMode Scale, float W, float H, float Factor, System.Numerics.Vector4 View)? Canvas;

        public int CreateEntity(string name) { int e = _next++; Names[e] = name; return e; }
        public bool IsAlive(int entity) => Names.ContainsKey(entity) && !Dead.Contains(entity);
        public void DestroyEntity(int entity) { Dead.Add(entity); }
        public void SetParent(int entity, int parent) => Parent[entity] = parent;
        public bool Set(int entity, in Aver.Framework.Transform2D t) { T2[entity] = t; Writes++; return true; }
        public bool Set(int entity, in Aver.Framework.Sprite2D s) { S2[entity] = s; Writes++; return true; }
        public bool Set(int entity, in Aver.Framework.Layer2D l) { L2[entity] = l; Writes++; return true; }
        public void SetCanvas(CanvasStretchMode mode, CanvasStretchAspect aspect, CanvasScaleMode scaleMode, float baseWidth, float baseHeight, float scale, System.Numerics.Vector4 view) =>
            Canvas = (mode, aspect, scaleMode, baseWidth, baseHeight, scale, view);
        public bool SubmitExternal(int layer, int z, long order, Action draw) { Externals.Add((layer, z, order, draw)); return true; }

        private int Up(int e) => Parent.TryGetValue(e, out int p) ? p : 0;

        /// <summary>The sprite's key and whether anything above it hides it, by the engine's rule.</summary>
        private (int Layer, int Z, bool Hidden) KeyOf(int e)
        {
            bool hidden = false;
            int layer = 0;
            bool haveLayer = false;
            for (int a = e; a != 0; a = Up(a))
            {
                if (T2.TryGetValue(a, out var t) && (t.Flags & Transform2DFlags.Hidden) != 0) hidden = true;
                if (!haveLayer && L2.TryGetValue(a, out var l)) { layer = l.Index; haveLayer = true; if ((l.Flags & Layer2DFlags.Hidden) != 0) hidden = true; }
            }
            return (layer, ZOf(e), hidden);
        }

        private int ZOf(int e)
        {
            if (!T2.TryGetValue(e, out var t)) return 0;
            int up = Up(e);
            return (t.Flags & Transform2DFlags.ZAbsolute) != 0 || up == 0 ? t.ZIndex : t.ZIndex + ZOf(up);
        }

        /// <summary>Everything the canvas would draw, in order: each sprite by its entity's name, each external draw by the name of the Control it drew.</summary>
        public List<string> Flush()
        {
            var items = new List<(int Layer, int Z, long Order, int Seq, string Name, Action? Draw)>();
            int seq = 0;
            foreach (var kv in S2)
            {
                if (Dead.Contains(kv.Key) || (kv.Value.Flags & Sprite2DFlags.Hidden) != 0 || kv.Value.Texture == 0) continue;
                var k = KeyOf(kv.Key);
                if (k.Hidden) continue;
                items.Add((k.Layer, k.Z, T2.TryGetValue(kv.Key, out var t) ? t.TreeOrder : 0, seq++, Names[kv.Key], null));
            }
            foreach (var x in Externals) items.Add((x.Layer, x.Z, x.Order, seq++, "", x.Draw));
            Externals.Clear();
            items.Sort((a, b) => a.Layer != b.Layer ? a.Layer.CompareTo(b.Layer) : a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.Seq.CompareTo(b.Seq));
            var order = new List<string>();
            foreach (var it in items)
            {
                if (it.Draw == null) { order.Add(it.Name); continue; }
                int before = Gui.DrawLog?.Count ?? 0;
                it.Draw();
                if (Gui.DrawLog != null) for (int i = before; i < Gui.DrawLog.Count; i++) order.Add(Gui.DrawLog[i]);
            }
            return order;
        }
    }

    public static int RunAll() => T.Suite(() =>
    {
        T.Run("2D: the nodes' state reaches the engine's components, parented like the tree", State);
        T.Run("2D: a script that moves or retints a sprite is seen by the next frame, and an unchanged one is not written again", Updates);
        T.Run("2D: visibility, modulate and transparency", Visibility);
        T.Run("2D: the stretch rules and the window rectangle reach the canvas", Stretch);
        T.Run("2D: Node2D keeps Godot's transform API", TransformApi);
        if (UiTests.EnsureNative() && UiNative.StateAvailable)
        {
            T.Run("2D: a sprite draws under, between and over Controls by layer and z", DrawOrder);
            T.Run("2D: the converter's output for the Menu2DGame fixture instantiates and draws in order (AverAssetC)", ConverterOutput);
        }
        else Console.WriteLine("Test: 2D draw order (native part SKIP: Aver.UI.Abi was not found; build the Aver.UI.Abi target)");
    });

    private static FakeCanvas Setup()
    {
        T.Reset();
        Canvas2D.ResetForTests();
        var f = new FakeCanvas();
        Canvas2D.Backend = f;
        return f;
    }

    private static Window Root => SceneTree.Instance.Root;

    private static int EntityOf(Node n) => ((ICanvas2DNode)n).Link.Private;

    private static long Id(string path) => Aver.Scene.Assets.ObjectIdOf(path);

    private static void State()
    {
        FakeCanvas f = Setup();
        var bgTex = new Texture2D { ResourcePath = "Textures/menu/bg.png" };
        var sheetTex = new Texture2D { ResourcePath = "res://Textures/menu/sheet.png" };
        var back = new Sprite2D { Name = "Back", Texture = bgTex, Position = new Vector2(640, 360), ZIndex = -5 };
        var menu = new CanvasLayer { Name = "Menu", Layer = 2, Offset = new Vector2(8, 4), Scale = new Vector2(1.5f, 1.5f), Rotation = 0.25f };
        var wrap = new Node2D { Name = "Wrap", Position = new Vector2(100, 50), Rotation = 0.5f, Scale = new Vector2(2, 3), ZIndex = 1 };
        var logo = new Sprite2D
        {
            Name = "Logo", Texture = sheetTex, Hframes = 4, Vframes = 2, Frame = 5, FlipH = true, Centered = false, Offset = new Vector2(4, 0),
            Modulate = new Color(1, 0.5f, 0.25f, 0.5f), SelfModulate = new Color(1, 1, 1, 0.5f),
        };
        var crop = new Sprite2D { Name = "Crop", Texture = sheetTex, RegionEnabled = true, RegionRect = new Rect2(8, 8, 32, 16), FlipV = true, TopLevel = true, ZAsRelative = false, ZIndex = 7, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        Root.AddChild(back);
        Root.AddChild(menu);
        menu.AddChild(wrap);
        wrap.AddChild(logo);
        wrap.AddChild(crop);
        T.Frame();

        T.Check("each 2D node has an entity", EntityOf(back) != 0 && EntityOf(menu) != 0 && EntityOf(wrap) != 0 && EntityOf(logo) != 0, $"{EntityOf(back)} {EntityOf(menu)} {EntityOf(wrap)} {EntityOf(logo)}");
        T.Check("entities hang like the nodes: logo under wrap under the layer, back at the root",
                f.Parent.GetValueOrDefault(EntityOf(logo)) == EntityOf(wrap) && f.Parent.GetValueOrDefault(EntityOf(wrap)) == EntityOf(menu) &&
                f.Parent.GetValueOrDefault(EntityOf(menu)) == 0 && f.Parent.GetValueOrDefault(EntityOf(back)) == 0);

        var l = f.L2[EntityOf(menu)];
        T.Check("the layer: index, offset, rotation, scale, visible", l.Index == 2 && l.Offset == new System.Numerics.Vector2(8, 4) && T.Near(l.Rotation, 0.25f) && l.Scale == new System.Numerics.Vector2(1.5f, 1.5f) && l.Flags == Layer2DFlags.None);

        var t = f.T2[EntityOf(wrap)];
        T.Check("the Node2D's transform: position, rotation, scale, z", t.Position == new System.Numerics.Vector2(100, 50) && T.Near(t.Rotation, 0.5f) && t.Scale == new System.Numerics.Vector2(2, 3) && t.ZIndex == 1 && t.Flags == Transform2DFlags.None);
        T.Check("a sprite has a transform record too", f.T2[EntityOf(back)].Position == new System.Numerics.Vector2(640, 360) && f.T2[EntityOf(back)].ZIndex == -5);

        var s = f.S2[EntityOf(logo)];
        T.Check("the sprite's texture is the file's asset id (fnv1a64 of the content path), whether written res:// or not", s.Texture == Id("Textures/menu/sheet.png") && f.S2[EntityOf(back)].Texture == Id("Textures/menu/bg.png"));
        T.Check("the sheet: frames, flip, centering and offset", s.HFrames == 4 && s.VFrames == 2 && s.Frame == 5 && (s.Flags & Sprite2DFlags.FlipH) != 0 && (s.Flags & Sprite2DFlags.NotCentered) != 0 && (s.Flags & Sprite2DFlags.FlipV) == 0 && s.Offset == new System.Numerics.Vector2(4, 0));
        T.Check("modulate times self_modulate, as 0xAABBGGRR", s.Modulate == 0x404080FFu, $"{s.Modulate:X8}");
        T.Check("no region means the whole image", s.Region == System.Numerics.Vector4.Zero);

        var c = f.S2[EntityOf(crop)];
        T.Check("a region, flip_v and the nearest filter", c.Region == new System.Numerics.Vector4(8, 8, 32, 16) && (c.Flags & Sprite2DFlags.FlipV) != 0 && (c.Flags & Sprite2DFlags.Nearest) != 0);
        var ct = f.T2[EntityOf(crop)];
        T.Check("top_level and an absolute z are flags of the transform", (ct.Flags & Transform2DFlags.TopLevel) != 0 && (ct.Flags & Transform2DFlags.ZAbsolute) != 0 && ct.ZIndex == 7);

        T.Check("the tree order counts up in the tree and is one number space for every node",
                f.T2[EntityOf(back)].TreeOrder > 0 && f.T2[EntityOf(wrap)].TreeOrder > f.T2[EntityOf(back)].TreeOrder && f.T2[EntityOf(logo)].TreeOrder > f.T2[EntityOf(wrap)].TreeOrder &&
                f.T2[EntityOf(crop)].TreeOrder > f.T2[EntityOf(logo)].TreeOrder);

        int logoEntity = EntityOf(logo);
        wrap.RemoveChild(logo);
        T.Check("a sprite that leaves the tree gives its entity back", f.Dead.Contains(logoEntity));
        logo.Free();
    }

    private static void Updates()
    {
        FakeCanvas f = Setup();
        var a = new Sprite2D { Name = "A", Texture = new Texture2D { ResourcePath = "Textures/a.png" } };
        var b = new Node2D { Name = "B" };
        Root.AddChild(a);
        Root.AddChild(b);
        T.Frame();
        int writes = f.Writes;
        T.Frame();
        T.Check("an unchanged frame writes nothing", f.Writes == writes);
        a.Position = new Vector2(5, 6);
        T.Frame();
        T.Check("moving a sprite writes its transform once", f.Writes == writes + 1 && f.T2[EntityOf(a)].Position == new System.Numerics.Vector2(5, 6));
        a.Modulate = new Color(0.5f, 0.5f, 0.5f, 1f);
        T.Frame();
        T.Check("retinting it writes its sprite once", f.Writes == writes + 2 && f.S2[EntityOf(a)].Modulate == 0xFF808080u, $"{f.S2[EntityOf(a)].Modulate:X8}");
        a.Frame = 0;
        a.Hframes = 3;
        a.Frame = 2;
        T.Frame();
        T.Check("a frame change reaches the sprite", f.S2[EntityOf(a)].Frame == 2 && f.S2[EntityOf(a)].HFrames == 3);
        a.Texture = new Texture2D { ResourcePath = "Textures/other.png" };
        T.Frame();
        T.Check("a new texture changes the asset id", f.S2[EntityOf(a)].Texture == Id("Textures/other.png"));
        a.Texture = null;
        T.Frame();
        T.Check("no texture is asset 0 (nothing drawn)", f.S2[EntityOf(a)].Texture == 0);
        b.Scale = Vector2.Zero;
        T.Frame();
        T.Check("a scale of exactly 0 is sent as almost 0: the engine reads 0 as 1", f.T2[EntityOf(b)].Scale.X > 0 && f.T2[EntityOf(b)].Scale.X < 1e-3f);

        var kid = new Sprite2D { Name = "Kid", Texture = new Texture2D { ResourcePath = "Textures/k.png" } };
        b.AddChild(kid);
        T.Frame();
        int kidEntity = EntityOf(kid);
        b.RemoveChild(kid);
        int writesAfter = f.Writes;
        T.Frame();
        T.Check("a sprite that left the tree has no entity and is not written again", f.Dead.Contains(kidEntity) && f.Writes == writesAfter);
    }

    private static void Visibility()
    {
        FakeCanvas f = Setup();
        var menu = new CanvasLayer { Name = "Menu" };
        var wrap = new Node2D { Name = "Wrap" };
        var logo = new Sprite2D { Name = "Logo", Texture = new Texture2D { ResourcePath = "Textures/a.png" } };
        var ghost = new Sprite2D { Name = "Ghost", Texture = new Texture2D { ResourcePath = "Textures/a.png" }, Visible = false };
        var veil = new Sprite2D { Name = "Veil", Texture = new Texture2D { ResourcePath = "Textures/a.png" }, Modulate = new Color(0, 0, 0, 0) };
        Root.AddChild(menu);
        menu.AddChild(wrap);
        wrap.AddChild(logo);
        menu.AddChild(ghost);
        menu.AddChild(veil);
        T.Frame();
        T.Check("a visible sprite is not hidden", (f.S2[EntityOf(logo)].Flags & Sprite2DFlags.Hidden) == 0);
        T.Check("visible = false hides the sprite and flags its transform", (f.S2[EntityOf(ghost)].Flags & Sprite2DFlags.Hidden) != 0 && (f.T2[EntityOf(ghost)].Flags & Transform2DFlags.Hidden) != 0);
        T.Check("a modulate of alpha 0 hides it, and is not sent as 0 (which the engine reads as white)", (f.S2[EntityOf(veil)].Flags & Sprite2DFlags.Hidden) != 0 && f.S2[EntityOf(veil)].Modulate != 0);

        wrap.Visible = false;
        T.Frame();
        T.Check("a hidden parent hides its child sprite", (f.S2[EntityOf(logo)].Flags & Sprite2DFlags.Hidden) != 0 && (f.T2[EntityOf(wrap)].Flags & Transform2DFlags.Hidden) != 0);
        wrap.Visible = true;
        menu.Visible = false;
        T.Frame();
        T.Check("a hidden layer hides what is on it and flags the layer", (f.S2[EntityOf(logo)].Flags & Sprite2DFlags.Hidden) != 0 && (f.L2[EntityOf(menu)].Flags & Layer2DFlags.Hidden) != 0);
        menu.Visible = true;
        wrap.Modulate = new Color(1, 1, 1, 0.5f);
        T.Frame();
        T.Check("an ancestor's modulate multiplies in", f.S2[EntityOf(logo)].Modulate == 0x80FFFFFFu && (f.S2[EntityOf(logo)].Flags & Sprite2DFlags.Hidden) == 0, $"{f.S2[EntityOf(logo)].Modulate:X8}");

        var plain = new Node { Name = "Plain" };
        Root.AddChild(plain);
        var late = new Sprite2D { Name = "Late", Texture = new Texture2D { ResourcePath = "Textures/a.png" } };
        var holder = new Control { Name = "Holder", Visible = false };
        plain.AddChild(holder);
        holder.AddChild(late);
        T.Frame();
        T.Check("a sprite under a hidden Control is hidden", (f.S2[EntityOf(late)].Flags & Sprite2DFlags.Hidden) != 0);
    }

    private static void Stretch()
    {
        FakeCanvas f = Setup();
        var s = new Sprite2D { Name = "S", Texture = new Texture2D { ResourcePath = "Textures/a.png" } };
        Root.AddChild(s);
        Root.ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems;
        Root.ContentScaleSize = new Vector2I(640, 360);
        Root.ContentScaleAspect = Window.ContentScaleAspectEnum.Keep;
        Root.Size = new Vector2I(2560, 1080);
        T.Frame();
        var c = f.Canvas;
        T.Check("the rules and the rectangle the Controls' stretch uses reach the canvas",
                c is { Mode: CanvasStretchMode.CanvasItems, Aspect: CanvasStretchAspect.Keep, Scale: CanvasScaleMode.Fractional } && c.Value.W == 640 && c.Value.H == 360 && T.Near(c.Value.Factor, 1f) &&
                c.Value.View == new System.Numerics.Vector4(0, 0, 2560, 1080), $"{c}");
        Root.ContentScaleAspect = Window.ContentScaleAspectEnum.Expand;
        Root.ContentScaleStretch = Window.ContentScaleStretchEnum.Integer;
        Root.ContentScaleFactor = 2f;
        Root.Size = new Vector2I(800, 600);
        T.Frame();
        c = f.Canvas;
        T.Check("a change of aspect, scale mode, factor or window size is sent the next frame", c is { Aspect: CanvasStretchAspect.Expand, Scale: CanvasScaleMode.Integer } && T.Near(c.Value.Factor, 2f) && c.Value.View.Z == 800 && c.Value.View.W == 600, $"{c}");
    }

    private static void TransformApi()
    {
        Setup();
        var parent = new Node2D { Name = "P", Position = new Vector2(10, 20), Scale = new Vector2(2, 2) };
        var child = new Node2D { Name = "C", Position = new Vector2(5, 0) };
        Root.AddChild(parent);
        parent.AddChild(child);
        T.Check("the global position composes the parent's transform", T.Near(child.GlobalPosition.X, 20f) && T.Near(child.GlobalPosition.Y, 20f), $"{child.GlobalPosition}");
        child.GlobalPosition = new Vector2(30, 40);
        T.Check("setting the global position solves for the local one", T.Near(child.Position.X, 10f) && T.Near(child.Position.Y, 10f), $"{child.Position}");
        T.Check("to_global and to_local are inverses", T.Near(child.ToLocal(child.ToGlobal(new Vector2(3, 4))).X, 3f) && T.Near(child.ToLocal(child.ToGlobal(new Vector2(3, 4))).Y, 4f));
        child.TopLevel = true;
        T.Check("a top-level node ignores its parent's transform", T.Near(child.GlobalPosition.X, child.Position.X));
        child.TopLevel = false;
        child.RotationDegrees = 90;
        T.Check("rotation degrees and radians agree", T.Near(child.Rotation, MathF.PI / 2));
        parent.Translate(new Vector2(1, 0));
        T.Check("translate moves along the node's own axes (scaled)", T.Near(parent.Position.X, 12f));
    }

    private static Drawn Ctl(string name, int z = 0) => new() { Name = name, Size = new Vector2(10, 10), ZIndex = z };

    private static Sprite2D Spr(string name, int z = 0) => new() { Name = name, Texture = new Texture2D { ResourcePath = "Textures/" + name + ".png" }, ZIndex = z };

    private static List<string> Draw(FakeCanvas f)
    {
        T.Frame();
        Gui.DrawLog = new List<string>();
        UiNative.aver_ui_begin_frame(0, 0, 1280, 720);
        Gui.DrawAll(canvas: true);
        int immediate = UiNative.aver_ui_vertex_count();
        List<string> order = f.Flush();
        T.Check("the Controls are queued, not drawn, when the canvas is used", immediate == 0, $"{immediate} vertices at once");
        return order;
    }

    private static void DrawOrder()
    {
        FakeCanvas f = Setup();
        try
        {
            // Back sits on the root canvas (layer 0); the menu is layer 2. Frame holds two buttons and a sprite between them; Wrap puts Logo at z 1; Top is z 5, absolute.
            Sprite2D back = Spr("Back", -5);
            var menu = new CanvasLayer { Name = "Menu", Layer = 2 };
            Sprite2D bg = Spr("Bg");
            var wrap = new Node2D { Name = "Wrap", ZIndex = 1 };
            Sprite2D logo = Spr("Logo");
            Drawn frame = Ctl("Frame"), play = Ctl("Play"), quit = Ctl("Quit");
            Sprite2D mid = Spr("Mid");
            Sprite2D top = Spr("Top", 5);
            top.ZAsRelative = false;
            Root.AddChild(back);
            Root.AddChild(menu);
            menu.AddChild(bg);
            menu.AddChild(wrap);
            wrap.AddChild(logo);
            menu.AddChild(frame);
            frame.AddChild(play);
            frame.AddChild(mid);
            frame.AddChild(quit);
            menu.AddChild(top);

            List<string> order = Draw(f);
            T.CheckSeq("a lower canvas layer first, then z, then tree order: a sprite under (Bg), between (Mid) and over (Top) the Controls", new[] { "Back", "Bg", "Frame", "Play", "Mid", "Quit", "Logo", "Top" }, order);

            play.ZIndex = -1;
            order = Draw(f);
            T.CheckSeq("a z_index below the sprites puts a button under them", new[] { "Back", "Play", "Bg", "Frame", "Mid", "Quit", "Logo", "Top" }, order);

            play.ZIndex = 0;
            mid.ZIndex = 3;
            order = Draw(f);
            T.CheckSeq("a sprite with a higher z goes over the buttons that follow it in the tree", new[] { "Back", "Bg", "Frame", "Play", "Quit", "Logo", "Mid", "Top" }, order);

            mid.ZIndex = 0;
            frame.MoveChild(mid, 0);
            order = Draw(f);
            T.CheckSeq("moving the sprite first among its siblings moves it under the buttons (the tree order is numbered again)", new[] { "Back", "Bg", "Frame", "Mid", "Play", "Quit", "Logo", "Top" }, order);

            menu.Layer = -1;
            order = Draw(f);
            T.CheckSeq("a layer below 0 draws before the root canvas", new[] { "Bg", "Frame", "Mid", "Play", "Quit", "Logo", "Top", "Back" }, order);

            menu.Layer = 2;
            menu.Visible = false;
            order = Draw(f);
            T.CheckSeq("a hidden layer draws neither its sprites nor its Controls", new[] { "Back" }, order);

            menu.Visible = true;
            quit.Visible = false;
            order = Draw(f);
            T.CheckSeq("a hidden Control is not queued", new[] { "Back", "Bg", "Frame", "Mid", "Play", "Logo", "Top" }, order);

            Gui.DrawLog = new List<string>();
            UiNative.aver_ui_begin_frame(0, 0, 1280, 720);
            Gui.DrawAll();
            T.Check("without the canvas the Controls are drawn at once, as before", Gui.DrawLog.Count > 0 && f.Externals.Count == 0 && UiNative.aver_ui_vertex_count() > 0);
        }
        finally { Gui.DrawLog = null; }
    }

    private static void ConverterOutput()
    {
        string? exe = T.AssetCExe();
        if (exe == null) { Console.WriteLine("  SKIP: AverAssetC was not found in the build's bin"); return; }
        string tmp = Path.Combine(Path.GetTempPath(), "aver-fabric-2d-conv-" + System.Environment.ProcessId);
        string fixture = Path.Combine(T.DataDir(), "Menu2DGame");
        string src = Path.Combine(tmp, "Menu2DGame");
        string outDir = Path.Combine(tmp, "out");
        try
        {
            foreach (string dir in Directory.GetDirectories(fixture, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(dir.Replace(fixture, src));
            Directory.CreateDirectory(src);
            foreach (string file in Directory.GetFiles(fixture, "*", SearchOption.AllDirectories)) File.Copy(file, file.Replace(fixture, src), true);
            Directory.CreateDirectory(Path.Combine(src, "art"));
            static byte[] Png(byte w, byte h) => new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R', 0, 0, 0, w, 0, 0, 0, h, 8, 6, 0, 0, 0, 0, 0, 0, 0 };
            File.WriteAllBytes(Path.Combine(src, "art", "bg.png"), Png(200, 100));
            File.WriteAllBytes(Path.Combine(src, "art", "sheet.png"), Png(64, 32));

            var psi = new System.Diagnostics.ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            psi.ArgumentList.Add("fabric"); psi.ArgumentList.Add(src); psi.ArgumentList.Add(outDir); psi.ArgumentList.Add("--scenes-only");
            using (var proc = System.Diagnostics.Process.Start(psi)!)
            {
                proc.StandardOutput.ReadToEnd(); proc.StandardError.ReadToEnd();
                proc.WaitForExit();
                T.Check("the converter runs on the Menu2DGame fixture", proc.ExitCode == 0, "exit " + proc.ExitCode);
                if (proc.ExitCode != 0) return;
            }

            FakeCanvas f = Setup();
            ResourceLoader.ContentRootOverride = Path.Combine(outDir, "Menu2DGame", "Content");
            Node main = ResourceLoader.Load<PackedScene>("res://main.tscn")!.Instantiate();
            Root.AddChild(main);
            Gui.FlushLayout();
            T.Check("the level instantiates without an error", T.ErrorCount() == 0, string.Join(" | ", T.Logs.Where(l => l.Level == Aver.Scripting.Log.Level.Error).Select(l => l.Message)));

            var back = main.GetNode<Sprite2D>("Back");
            var menu = main.GetNode<CanvasLayer>("Menu");
            var wrap = main.GetNode<Node2D>("Menu/Wrap");
            var logo = main.GetNode<Sprite2D>("Menu/Wrap/Logo");
            var crop = main.GetNode<Sprite2D>("Menu/Wrap/Crop");
            T.Check("the nodes are their classes with the scene's values", menu.Layer == 2 && menu.Offset == new Vector2(8, 4) && T.Near(wrap.Rotation, 0.5f) && wrap.Scale == new Vector2(2, 3) && wrap.ZIndex == 1 &&
                    back.ZIndex == -5 && logo.Hframes == 4 && logo.Frame == 5 && logo.FlipH && crop.RegionEnabled && crop.RegionRect == new Rect2(8, 8, 32, 16) && crop.TopLevel);
            T.Check("the texture is the copied file", back.Texture?.ResourcePath is { } p && p.EndsWith("Textures/art/bg.png", StringComparison.Ordinal), back.Texture?.ResourcePath ?? "(null)");

            T.Frame();
            T.Check("the state reaches the components", f.S2[EntityOf(back)].Texture == Id("Textures/art/bg.png") && f.S2[EntityOf(logo)].Texture == Id("Textures/art/sheet.png") && f.L2[EntityOf(menu)].Index == 2 &&
                    f.T2[EntityOf(wrap)].ZIndex == 1 && f.S2[EntityOf(logo)].Modulate == 0x404080FFu && f.S2[EntityOf(crop)].Region == new System.Numerics.Vector4(8, 8, 32, 16),
                    $"{f.S2[EntityOf(logo)].Modulate:X8}");
            var mid = main.GetNode<Sprite2D>("Menu/Panel/Mid");
            T.Check("frame_coords (2, 1) of a 4 x 2 sheet is frame 6", mid.Frame == 6 && f.S2[EntityOf(mid)].Frame == 6);
            T.Check("a node with no equivalent is a plain node and takes no part", main.GetNode("Fx").GetType() == typeof(Node));

            // Back is layer 0; in layer 2 at z 0 the tree order runs Bg, Play, Mid, Quit (Veil is transparent and Ghost invisible); Logo is z 1, Top z 5. Crop is left out: it is top-level.
            List<string> order = Draw(f).Where(n => n != "Crop").ToList();
            T.CheckSeq("the converted menu draws its background under its buttons, a sprite between them and others over them", new[] { "Back", "Bg", "Play", "Mid", "Quit", "Logo", "Top" }, order);
        }
        finally
        {
            Gui.DrawLog = null;
            ResourceLoader.ContentRootOverride = null;
            Canvas2D.ResetForTests();
            try { Directory.Delete(tmp, true); } catch (IOException) { }
        }
    }
}
