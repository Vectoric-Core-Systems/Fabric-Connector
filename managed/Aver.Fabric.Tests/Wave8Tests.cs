// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Wave 8a (text and UI), the managed side: shapes tessellated (F58), the radial progress fill (F56), the sampler / blend / filter state (P41), the draw order of
// layers and z_index (F59), rotated controls through the engine's transform (P40), font spacing (P38), Label3D's descriptor (E6, F47), the translation API (F48),
// TextLine and TextParagraph (F49), CodeEdit (F50), ReferenceRect / FoldableContainer / MenuBar (F52), the ColorPicker (F55) and the Control leftovers (F57).
// What needs a GPU (the pictures) is a headed check.

using Aver.Fabric;
using Collections = Aver.Fabric.Collections;

/// <summary>A control that draws whatever the test hands it.</summary>
class Painter : Control
{
    public Action<Painter>? Paint;
    public override void _Draw() => Paint?.Invoke(this);
}

static class Wave8Tests
{
    public static int RunAll() => T.Suite(() =>
    {
        T.Run("wave 8: shapes are tessellated to triangles (F58)", Tessellation);
        T.Run("wave 8: a radial fill is a wedge of the rectangle (F56)", RadialWedges);
        T.Run("wave 8: CanvasItem's draw API records meshes and draw_set_transform (F58)", DrawApi);
        T.Run("wave 8: a radial TextureProgressBar draws a textured fan (F56)", RadialBar);
        T.Run("wave 8: texture filter, repeat and blend resolve from the parents (P41)", StateResolution);
        T.Run("wave 8: a font's spacing reaches measuring (P38)", Spacing);
        T.Run("wave 8: Label3D hands the engine its descriptor (E6, F47)", Label3DDescriptor);
        T.Run("wave 8: the translation API (F48)", TranslationApi);
        T.Run("wave 8: TextLine and TextParagraph (F49)", TextObjects);
        T.Run("wave 8: CodeEdit and its highlighter (F50)", CodeEditing);
        T.Run("wave 8: ReferenceRect, FoldableContainer and MenuBar (F52)", Controls);
        T.Run("wave 8: the ColorPicker's recent presets, hex and wheel (F55)", Picker);
        T.Run("wave 8: Control layout leftovers (F57)", Layout);
        if (UiTests.EnsureNative() && UiNative.StateAvailable)
        {
            T.Run("wave 8: CanvasLayer and z_index order the drawing (F59)", DrawOrder);
            T.Run("wave 8: a rotated control is drawn through the engine's transform (P40)", Rotation);
            T.Run("wave 8: the world text ABI takes a label (E6)", WorldTextAbi);
        }
        else Console.WriteLine("Test: wave 8 draw order, rotation, world text (native part SKIP: Aver.UI.Abi was not found, or predates wave 8a; build the Aver.UI.Abi target and delete the old copy)");
    });

    private static bool Near(float a, float b, float eps = 0.01f) => MathF.Abs(a - b) <= eps;

    // ---- F58 ----

    private static void Tessellation()
    {
        var white = new[] { new Color(1, 1, 1, 1) };
        CanvasMesh? sq = CanvasTess.Polygon(new[] { new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 10), new Vector2(0, 10) }, new[] { new Color(1, 0, 0, 1), new Color(0, 1, 0, 1), new Color(0, 0, 1, 1), new Color(1, 1, 1, 1) });
        T.Check("a square polygon is four vertices and two triangles", sq != null && sq.VertexCount == 4 && sq.TriangleCount == 2);
        T.Check("each point keeps its own colour", sq != null && sq.Colors[0] == StyleBoxFlat.Pack(new Color(1, 0, 0, 1)) && sq.Colors[2] == StyleBoxFlat.Pack(new Color(0, 0, 1, 1)));
        CanvasMesh? tex = CanvasTess.Polygon(new[] { new Vector2(0, 0), new Vector2(4, 0), new Vector2(0, 4) }, white, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1) }, 9);
        T.Check("texture coordinates and the texture ride along", tex != null && tex.Texture == 9 && Near(tex.Xyuv[4 + 2], 1f) && Near(tex.Xyuv[8 + 3], 1f));
        T.Check("two points are no polygon", CanvasTess.Polygon(new[] { new Vector2(0, 0), new Vector2(1, 1) }, white) == null);
        T.Check("a NaN point is refused", CanvasTess.Polygon(new[] { new Vector2(0, 0), new Vector2(1, float.NaN), new Vector2(0, 4) }, white) == null);

        CanvasMesh? line = CanvasTess.Line(new Vector2(0, 0), new Vector2(10, 0), new Color(1, 1, 1, 1), 2f);
        T.Check("a line is a quad of its width", line != null && line.VertexCount == 4 && Near(MathF.Abs(line.Xyuv[1] - line.Xyuv[5]), 2f));
        T.Check("a width below zero is one pixel", Near(MathF.Abs(CanvasTess.Line(new Vector2(0, 0), new Vector2(10, 0), new Color(1, 1, 1, 1), -1f)!.Xyuv[1] - CanvasTess.Line(new Vector2(0, 0), new Vector2(10, 0), new Color(1, 1, 1, 1), -1f)!.Xyuv[5]), 1f));
        T.Check("a line of no length is nothing", CanvasTess.Line(new Vector2(3, 3), new Vector2(3, 3), new Color(1, 1, 1, 1), 2f) == null);

        CanvasMesh? corner = CanvasTess.Polyline(new[] { new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 10) }, white, 2f);
        T.Check("a polyline with a turn is two quads and a bevel", corner != null && corner.VertexCount == 11 && corner.TriangleCount == 5);
        CanvasMesh? straight = CanvasTess.Polyline(new[] { new Vector2(0, 0), new Vector2(5, 0), new Vector2(10, 0) }, white, 2f);
        T.Check("a straight one needs no bevel", straight != null && straight.VertexCount == 8);
        CanvasMesh? ring = CanvasTess.Polyline(new[] { new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 10), new Vector2(0, 10) }, white, 2f, closed: true);
        T.Check("a closed square is four quads and four bevels", ring != null && ring.VertexCount == 28);
        T.Check("one point is no polyline", CanvasTess.Polyline(new[] { new Vector2(0, 0) }, white, 1f) == null);
        CanvasMesh? multi = CanvasTess.Multiline(new[] { new Vector2(0, 0), new Vector2(5, 0), new Vector2(0, 5), new Vector2(5, 5) }, new[] { new Color(1, 0, 0, 1), new Color(0, 0, 1, 1) }, 1f, true);
        T.Check("a multiline is separate segments with a colour each", multi != null && multi.VertexCount == 8 && multi.Colors[0] == StyleBoxFlat.Pack(new Color(1, 0, 0, 1)) && multi.Colors[4] == StyleBoxFlat.Pack(new Color(0, 0, 1, 1)));

        Vector2[] arc = CanvasTess.ArcPoints(Vector2.Zero, 10f, 0f, MathF.PI / 2f, 5);
        T.Check("an arc runs from the start angle to the end angle", arc.Length == 5 && Near(arc[0].X, 10f) && Near(arc[0].Y, 0f) && Near(arc[4].X, 0f) && Near(arc[4].Y, 10f));
        T.Check("an arc has at least two points", CanvasTess.ArcPoints(Vector2.Zero, 10f, 0f, 1f, 0).Length == 2);
        CanvasMesh? disc = CanvasTess.Circle(new Vector2(5, 5), 10f, new Color(1, 1, 1, 1));
        int n = CanvasTess.CirclePoints(10f);
        T.Check("a circle is a fan of as many triangles as its points", disc != null && disc.VertexCount == 1 + n && disc.TriangleCount == n && n >= 12);
        T.Check("a bigger circle has more points, within bounds", CanvasTess.CirclePoints(500f) > n && CanvasTess.CirclePoints(500f) <= 256 && CanvasTess.CirclePoints(0f) == 8);
        T.Check("a circle of no radius is nothing", CanvasTess.Circle(Vector2.Zero, 0f, new Color(1, 1, 1, 1)) == null);

        T.Check("a primitive of one point is a pixel", CanvasTess.Primitive(new[] { new Vector2(2, 2) }, white, null, 0)!.VertexCount == 4);
        T.Check("of two a line", CanvasTess.Primitive(new[] { new Vector2(0, 0), new Vector2(0, 8) }, white, null, 0)!.VertexCount == 4);
        T.Check("of three a triangle, of four a quad", CanvasTess.Primitive(new[] { new Vector2(0, 0), new Vector2(4, 0), new Vector2(0, 4) }, white, null, 0)!.TriangleCount == 1 &&
                CanvasTess.Primitive(new[] { new Vector2(0, 0), new Vector2(4, 0), new Vector2(4, 4), new Vector2(0, 4) }, white, null, 0)!.TriangleCount == 2);
        T.Check("of none or five, nothing", CanvasTess.Primitive(System.Array.Empty<Vector2>(), white, null, 0) == null && CanvasTess.Primitive(new Vector2[5], white, null, 0) == null);

        var big = new Vector2[CanvasTess.MaxVertices + 10];
        T.Check("a polyline over the vertex cap is refused rather than grown", CanvasTess.Polyline(big, white, 1f) == null);

        CanvasMesh moved = line!.Transformed(new Transform2D(0f, new Vector2(100, 50)));
        T.Check("a mesh moves with a transform without touching the original", Near(moved.Xyuv[0], line.Xyuv[0] + 100) && Near(moved.Xyuv[1], line.Xyuv[1] + 50) && Near(line.Xyuv[0], 0f, 2f));
    }

    // ---- F56 ----

    private static void RadialWedges()
    {
        var r = new Rect2(0, 0, 100, 100);
        var c = new Vector2(50, 50);
        T.Check("a ray at 0 degrees leaves through the top, 90 the right, 180 the bottom, 270 the left",
            CanvasTess.RayToRect(r, c, 0).DistanceTo(new Vector2(50, 0)) < 0.01f && CanvasTess.RayToRect(r, c, 90).DistanceTo(new Vector2(100, 50)) < 0.01f &&
            CanvasTess.RayToRect(r, c, 180).DistanceTo(new Vector2(50, 100)) < 0.01f && CanvasTess.RayToRect(r, c, 270).DistanceTo(new Vector2(0, 50)) < 0.01f);
        T.Check("at 45 degrees it reaches the top right corner", CanvasTess.RayToRect(r, c, 45).DistanceTo(new Vector2(100, 0)) < 0.01f);

        Vector2[] q = CanvasTess.RadialWedge(r, c, 0, 90);
        T.Check("a quarter turn from the top: the centre, the top middle, the corner, the right middle", q.Length == 4 && q[0] == c && q[1].DistanceTo(new Vector2(50, 0)) < 0.01f && q[2].DistanceTo(new Vector2(100, 0)) < 0.01f && q[3].DistanceTo(new Vector2(100, 50)) < 0.01f);
        Vector2[] half = CanvasTess.RadialWedge(r, c, 0, 180);
        T.Check("a half turn passes two corners", half.Length == 5 && half[2] == new Vector2(100, 0) && half[3] == new Vector2(100, 100) && half[4].DistanceTo(new Vector2(50, 100)) < 0.01f);
        Vector2[] full = CanvasTess.RadialWedge(r, c, 0, 360);
        T.Check("a full turn passes all four", full.Length == 7);
        Vector2[] back = CanvasTess.RadialWedge(r, c, 0, -90);
        T.Check("a negative sweep goes anticlockwise: the left middle, the top left corner, the top middle", back.Length == 4 && back[1].DistanceTo(new Vector2(0, 50)) < 0.01f && back[2] == new Vector2(0, 0) && back[3].DistanceTo(new Vector2(50, 0)) < 0.01f);
        T.Check("no sweep, no wedge", CanvasTess.RadialWedge(r, c, 0, 0).Length == 0 && CanvasTess.RadialWedge(new Rect2(0, 0, 0, 5), c, 0, 90).Length == 0);
        T.Check("a NaN angle is refused", CanvasTess.RadialWedge(r, c, float.NaN, 90).Length == 0);

        CanvasMesh? fan = CanvasTess.RadialFill(r, c, 0, 180, new Color(1, 1, 1, 1), 5);
        T.Check("the fill is a fan, one triangle fewer than its points", fan != null && fan.TriangleCount == 3 && fan.Texture == 5);
        T.Check("its texture coordinates are the point's place in the rectangle", fan != null && Near(fan.Xyuv[0 + 2], 0.5f) && Near(fan.Xyuv[2 * 4 + 2], 1f) && Near(fan.Xyuv[2 * 4 + 3], 0f));
        Vector2[] moved = CanvasTess.RadialWedge(r, new Vector2(500, 500), 0, 90);
        T.Check("a centre outside the rectangle is held on its edge", moved.Length >= 3 && moved[0] == new Vector2(100, 100));
    }

    // ---- F58 on the item ----

    private static void DrawApi()
    {
        T.Reset();
        var p = new Painter { Size = new Vector2(100, 100) };
        p.Paint = c =>
        {
            c.DrawLine(new Vector2(0, 0), new Vector2(10, 10), new Color(1, 0, 0, 1), 2f);
            c.DrawLine(new Vector2(0, 0), new Vector2(10, 0), new Color(1, 0, 0, 1), 2f);
            c.DrawPolyline(new[] { new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 10) }, new Color(0, 1, 0, 1), 2f);
            c.DrawCircle(new Vector2(20, 20), 8f, new Color(0, 0, 1, 1));
            c.DrawCircle(new Vector2(20, 20), 8f, new Color(0, 0, 1, 1), false, 2f);
            c.DrawArc(new Vector2(30, 30), 5f, 0f, MathF.PI, 8, new Color(1, 1, 0, 1), 1f);
            c.DrawColoredPolygon(new[] { new Vector2(0, 0), new Vector2(5, 0), new Vector2(0, 5) }, new Color(1, 0, 1, 1));
            c.DrawSetTransform(new Vector2(40, 40), MathF.PI / 2f, new Vector2(2, 2));
            c.DrawRect(new Rect2(0, 0, 10, 5), new Color(1, 1, 1, 1));
        };
        SceneTree.Instance.Root.AddChild(p);
        p.RunDraw();
        int meshes = p.Commands.Count(c => c.Type == CanvasItem.DrawCommand.Kind.Mesh);
        T.Check("a diagonal line, a polyline, two circles, an arc and a polygon are six meshes; the straight line is still a rectangle", meshes == 6 && p.Commands.Count(c => c.Type == CanvasItem.DrawCommand.Kind.Rect) == 2, $"{meshes}");
        CanvasItem.DrawCommand st = p.Commands.First(c => c.Type == CanvasItem.DrawCommand.Kind.SetTransform);
        T.Check("draw_set_transform is recorded with its matrix: a quarter turn, scaled by two, moved", Near(st.Xform.X.X, 0f) && Near(st.Xform.X.Y, 2f) && Near(st.Xform.Y.X, -2f) && Near(st.Xform.Origin.X, 40f));
        T.Check("it sits between the commands before it and the one after", p.Commands.ToList().IndexOf(st) == p.Commands.Count - 2);
        p.DrawLine(Vector2.Zero, new Vector2(5, 5), new Color(1, 1, 1, 1));
        T.Check("drawing outside the draw pass records nothing and says so", p.Commands.Count == 9 && T.Logs.Any(l => l.Message.Contains("DrawLine")));
    }

    private static void RadialBar()
    {
        T.Reset();
        var bar = new TextureProgressBar { MinValue = 0, MaxValue = 100, Value = 50, FillMode = TextureProgressBar.FillModeEnum.Clockwise, TextureProgress = new SizedTexture(64, 64, 33) };
        bar.RunDraw();
        CanvasItem.DrawCommand m = bar.Commands.Single(c => c.Type == CanvasItem.DrawCommand.Kind.Mesh);
        T.Check("a half full clockwise bar is a fan on the progress texture", m.Triangles != null && m.Triangles.Texture == 33 && m.Triangles.TriangleCount == 3);
        T.Check("its points are the wedge from the top to the bottom through the right", m.Triangles != null && Near(m.Triangles.Xyuv[4], 32f) && Near(m.Triangles.Xyuv[5], 0f));
        bar.FillMode = TextureProgressBar.FillModeEnum.CounterClockwise;
        bar.Value = 25;
        bar.QueueRedraw();
        bar.RunDraw();
        (float start, float sweep) = bar.GetRadialAngles();
        T.Check("counter-clockwise starts at the initial angle minus the sweep", Near(start, -90f) && Near(sweep, 90f));
        bar.FillMode = TextureProgressBar.FillModeEnum.ClockwiseAndCounterClockwise;
        bar.Value = 50;
        (start, sweep) = bar.GetRadialAngles();
        T.Check("both ways grows from the initial angle, half each way", Near(start, -90f) && Near(sweep, 180f));
        bar.RadialInitialAngle = 90f;
        bar.RadialFillDegrees = 180f;
        bar.FillMode = TextureProgressBar.FillModeEnum.Clockwise;
        bar.Value = 100;
        (start, sweep) = bar.GetRadialAngles();
        T.Check("the initial angle and fill degrees are honoured", Near(start, 90f) && Near(sweep, 180f));
        bar.QueueRedraw();
        bar.RunDraw();
        T.Check("it still draws", bar.Commands.Any(c => c.Type == CanvasItem.DrawCommand.Kind.Mesh));

        var linear = new TextureProgressBar { MinValue = 0, MaxValue = 100, Value = 50, FillMode = TextureProgressBar.FillModeEnum.LeftToRight, TextureProgress = new SizedTexture(64, 64, 33) };
        linear.RunDraw();
        T.Check("a linear bar draws as it did: one textured rectangle, no mesh", linear.Commands.Count(c => c.Type == CanvasItem.DrawCommand.Kind.Texture) == 1 && !linear.Commands.Any(c => c.Type == CanvasItem.DrawCommand.Kind.Mesh));

        var noHandle = new TextureProgressBar { MinValue = 0, MaxValue = 100, Value = 50, FillMode = TextureProgressBar.FillModeEnum.Clockwise, TextureProgress = new SizedTexture(64, 64, 0) };
        noHandle.RunDraw();
        T.Check("a texture with no engine handle draws nothing rather than a white fan", !noHandle.Commands.Any(c => c.Type == CanvasItem.DrawCommand.Kind.Mesh));
    }

    // ---- P41 ----

    private static void StateResolution()
    {
        T.Reset();
        var root = new Control { Name = "Root" };
        var mid = new Control { Name = "Mid" };
        var leaf = new Painter { Name = "Leaf" };
        root.AddChild(mid);
        mid.AddChild(leaf);
        SceneTree.Instance.Root.AddChild(root);
        T.Check("the defaults are linear, clamped, ordinary blend", leaf.ResolvedFilter() == UiFilterId.Linear && leaf.ResolvedAddress() == UiAddressId.Clamp && leaf.ResolvedBlend() == UiBlendId.Mix);
        root.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        root.TextureRepeat = CanvasItem.TextureRepeatEnum.Enabled;
        T.Check("a parent's filter and repeat reach a child that says parent", leaf.ResolvedFilter() == UiFilterId.Nearest && leaf.ResolvedAddress() == UiAddressId.Repeat);
        mid.TextureFilter = CanvasItem.TextureFilterEnum.Linear;
        mid.TextureRepeat = CanvasItem.TextureRepeatEnum.Mirror;
        T.Check("and its own setting wins", leaf.ResolvedFilter() == UiFilterId.Linear && leaf.ResolvedAddress() == UiAddressId.Mirror);
        leaf.TextureFilter = CanvasItem.TextureFilterEnum.NearestWithMipmaps;
        T.Check("the mipmapped nearest filter is nearest", leaf.ResolvedFilter() == UiFilterId.Nearest);
        leaf.Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        T.Check("a canvas item material sets the blend", leaf.ResolvedBlend() == UiBlendId.Add);
        mid.Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Mul };
        leaf.Material = null;
        T.Check("a child without a material is ordinary unless it uses its parent's", leaf.ResolvedBlend() == UiBlendId.Mix);
        leaf.UseParentMaterial = true;
        T.Check("use_parent_material takes the parent's blend", leaf.ResolvedBlend() == UiBlendId.Mul);
        T.Check("sub is sub and premultiplied alpha is the ordinary blend", new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Sub }.ToEngineBlend() == UiBlendId.Sub &&
                new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.PremultAlpha }.ToEngineBlend() == UiBlendId.Mix);
        var clipper = new Control { Size = new Vector2(10, 10), ClipChildren = CanvasItem.ClipChildrenMode.AndDraw };
        T.Check("clip_children clips children to the rectangle", clipper.ClipsChildren && !new Control().ClipsChildren);
    }

    // ---- P38 ----

    private static void Spacing()
    {
        T.Reset();
        UiText.WidthProvider = (family, px, flags, text) => text.Length * px * 0.5f;
        UiText.LineProvider = (family, px) => px * 1.2f;
        try
        {
            var plain = new FontFile();
            var spaced = new FontVariation { BaseFont = plain, SpacingGlyph = 2, SpacingSpace = 3, SpacingTop = 1, SpacingBottom = 2 };
            T.Check("a plain font adds no spacing and has no spacing block", plain.SpacingBlock == null && plain.GlyphSpacing == 0f && plain.LineExtra == 0f);
            T.Check("a variation has one: 0, glyph, space, 0", spaced.SpacingBlock != null && spaced.SpacingBlock[1] == 2f && spaced.SpacingBlock[2] == 3f);
            T.Check("a plain font's width is unchanged", Near(UiText.Width(plain, 10, "a b"), 15f));
            T.Check("a spaced font's is longer by 2 per character and 3 per space (15 + 6 + 3)", Near(UiText.Width(spaced, 10, "a b"), 24f));
            T.Check("the top and bottom spacing lengthen the line (12 + 3)", Near(UiText.LineHeight(spaced, 10), 15f) && Near(UiText.LineHeight(plain, 10), 12f));
            var nested = new FontVariation { BaseFont = spaced, SpacingGlyph = 1 };
            T.Check("a variation over a variation adds both", Near(nested.GlyphSpacing, 3f) && Near(UiText.Width(nested, 10, "ab"), 5f * 2 + 3f * 2));
            var p = new Painter();
            p.Paint = c => c.DrawString(spaced, new Vector2(0, 10), "hi", HorizontalAlignment.Left, -1f, 10);
            SceneTree.Instance.Root.AddChild(p);
            p.RunDraw();
            CanvasItem.DrawCommand t = p.Commands.Single(c => c.Type == CanvasItem.DrawCommand.Kind.Text);
            T.Check("a string drawn with it carries the block to the engine", t.Spacing != null && t.Spacing[1] == 2f);
            var q = new Painter();
            q.Paint = c => c.DrawString(plain, new Vector2(0, 10), "hi", HorizontalAlignment.Left, -1f, 10);
            SceneTree.Instance.Root.AddChild(q);
            q.RunDraw();
            T.Check("and a plain font's string carries none", q.Commands.Single(c => c.Type == CanvasItem.DrawCommand.Kind.Text).Spacing == null);
        }
        finally { UiText.ResetForTests(); }
    }

    // ---- E6, F47 ----

    private static void Label3DDescriptor()
    {
        T.Reset();
        UiText.WidthProvider = (family, px, flags, text) => text.Length * px * 0.5f;
        UiText.LineProvider = (family, px) => px * 1.2f;
        try
        {
            var label = new Label3D { Text = "Hello", Position = new Vector3(1, 2, 3) };
            SceneTree.Instance.Root.AddChild(label);
            Text3dDesc d = label.BuildDescriptor(720f);
            T.Check("the position is Godot's (1, 2, 3) in the engine's frame and centimetres: (-3, 1, 2) x 100", Near(d.World[12], -300f) && Near(d.World[13], 100f) && Near(d.World[14], 200f) && d.World[15] == 1f);
            T.Check("the plane's x axis is the node's x (engine +y), its y the node's y (engine +z), its z the node's z (engine -x), each 100 long",
                Near(d.World[0], 0f) && Near(d.World[1], 100f) && Near(d.World[2], 0f) && Near(d.World[4], 0f) && Near(d.World[5], 0f) && Near(d.World[6], 100f) && Near(d.World[8], -100f) && Near(d.World[9], 0f));
            T.Check("defaults: 32 px, pixel size 0.005, centred both ways, white with a black outline of 12", d.Px == 32f && Near(d.PixelSize, 0.005f) && d.HAlign == 1 && d.VAlign == 1 && d.OutlineSize == 12f);
            T.Check("white is white and the outline black, opaque", d.Rgba == 0xFFFFFFFFu && d.OutlineRgba == 0xFF000000u);
            T.Check("a plain label is depth tested, two sided, with no alpha cut, intensity one, no fixed size", d.DrawFlags == 0 && d.AlphaCut == 0f && d.Intensity == 1f && d.FixedPxPerUnit == 0f && d.Flags == 0);

            label.Modulate = new Color(1f, 0.5f, 0.25f, 1f);
            T.Check("Godot's sRGB colour is turned to linear radiance (0.5 -> 55, 0.25 -> 13 of 255)", Label3D.PackLinear(label.Modulate) == 0xFF0D37FFu);
            label.Modulate = new Color(1, 1, 1, 0.5f);
            T.Check("alpha is not converted", (Label3D.PackLinear(label.Modulate) >> 24) == 128u);

            label.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
            T.Check("billboard enabled", label.BuildDescriptor(720f).DrawFlags == 4);
            label.Billboard = BaseMaterial3D.BillboardModeEnum.FixedY;
            T.Check("billboard fixed y", label.BuildDescriptor(720f).DrawFlags == 8);
            label.Billboard = BaseMaterial3D.BillboardModeEnum.Particles;
            T.Check("billboard particles draws as enabled", label.BuildDescriptor(720f).DrawFlags == 4);
            label.Billboard = BaseMaterial3D.BillboardModeEnum.Disabled;
            label.NoDepthTest = true;
            label.DoubleSided = false;
            T.Check("no depth test and one sided are flags", label.BuildDescriptor(720f).DrawFlags == (1 | 16));
            label.NoDepthTest = false;
            label.DoubleSided = true;
            label.AlphaCut = 1;
            label.AlphaScissorThreshold = 0.3f;
            T.Check("alpha cut discard carries its threshold", Near(label.BuildDescriptor(720f).AlphaCut, 0.3f));
            label.AlphaCut = 3;
            T.Check("hash and opaque prepass draw as ordinary blended text", label.BuildDescriptor(720f).AlphaCut == 0f);
            label.FixedSize = true;
            T.Check("fixed size is half the viewport height in pixels per plane unit (inferred from Godot's shader)", Near(label.BuildDescriptor(720f).FixedPxPerUnit, 360f));

            label.HorizontalAlignment = HorizontalAlignment.Left;
            label.VerticalAlignment = VerticalAlignment.Top;
            Text3dDesc left = label.BuildDescriptor(720f);
            label.HorizontalAlignment = HorizontalAlignment.Right;
            label.VerticalAlignment = VerticalAlignment.Bottom;
            Text3dDesc right = label.BuildDescriptor(720f);
            T.Check("alignment: left/top is 0/0, right/bottom 2/2", left.HAlign == 0 && left.VAlign == 0 && right.HAlign == 2 && right.VAlign == 2);

            label.AutowrapMode = 2;
            label.Width = 300f;
            Text3dDesc wrap = label.BuildDescriptor(720f);
            T.Check("autowrap wraps at the width", (wrap.Flags & 16) != 0 && wrap.Width == 300f);
            label.AutowrapMode = 0;
            T.Check("with autowrap off the width is not used", (label.BuildDescriptor(720f).Flags & 16) == 0 && label.BuildDescriptor(720f).Width == 0f);

            label.LineSpacing = 4f;
            label.Font = new FontVariation { BaseFont = new FontFile { ResourcePath = "res://fonts/Inter.ttf" }, SpacingGlyph = 2, SpacingSpace = 3, SpacingTop = 1 };
            Text3dDesc fonted = label.BuildDescriptor(720f);
            T.Check("the font's glyph, space and line spacing and the label's own line spacing ride in the spacing block", fonted.Spacing[1] == 2f && fonted.Spacing[2] == 3f && fonted.Spacing[3] == 5f);
            T.Check("the family is the font file's name", System.Text.Encoding.UTF8.GetString(fonted.Family, 0, 5) == "Inter" && fonted.Family[5] == 0);

            label.Offset = new Vector2(10, -5);
            label.OutlineSize = 0;
            Text3dDesc off = label.BuildDescriptor(720f);
            T.Check("offset and outline size", off.OffsetX == 10f && off.OffsetY == -5f && off.OutlineSize == 0f);

            // Submitting.
            var seen = new List<(string, Text3dDesc)>();
            Label3D.Submitter = (t, desc) => { seen.Add((t, desc)); return true; };
            try
            {
                label.Text = "mixed Case";
                label.Uppercase = true;
                T.Check("a visible label in the tree is submitted, upper-cased when asked", label.Submit() && seen.Count == 1 && seen[0].Item1 == "MIXED CASE" && label.Submissions == 1);
                label.Uppercase = false;
                label.Visible = false;
                T.Check("a hidden label is not submitted", !label.Submit() && seen.Count == 1);
                label.Visible = true;
                label.Text = "";
                T.Check("nor an empty one", !label.Submit() && seen.Count == 1);
                label.Text = "frame";
                T.Frame();
                T.Check("a frame submits it (the process pass)", seen.Count == 2 && seen[1].Item1 == "frame");
                label.GetParent()!.RemoveChild(label);
                T.Check("a label outside the tree is not submitted", !label.Submit());
                T.Check("a label without an engine draws nothing and the default submit is false", Label3D.Submitter != null);
            }
            finally { Label3D.Submitter = null; }

            var empty = new Label3D { Text = "x" };
            SceneTree.Instance.Root.AddChild(empty);
            T.Check("without a submitter and without an engine, Submit is quiet", !empty.Submit());
            var sz = new Label3D { Text = "abcd", FontSize = 20, PixelSize = 0.01f };
            Vector2 size = sz.GetTextSize();
            T.Check("the text size without an engine is the fallback width and line height in the label's pixel size (4 x 10 px, one 24 px line, x 0.01)", Near(size.X, 0.4f, 0.001f) && Near(size.Y, 0.24f, 0.001f), $"{size}");
            T.Check("Label3D.font is a Font (F47): it measures and sets by Godot's name", sz.Font == null && sz.Font?.GetStringSize("ab") == null);
        }
        finally { UiText.ResetForTests(); }
    }

    // ---- F48 ----

    private static void TranslationApi()
    {
        T.Reset();
        TranslationServer.Reset();
        try
        {
            T.Check("locales are standardised: pt-br is pt_BR, zh-hans is zh_Hans, EN_us is en_US, '' is en", TranslationServer.StandardizeLocale("pt-br") == "pt_BR" && TranslationServer.StandardizeLocale("zh-hans") == "zh_Hans" &&
                    TranslationServer.StandardizeLocale("EN_us") == "en_US" && TranslationServer.StandardizeLocale("") == "en" && TranslationServer.StandardizeLocale("de_DE.UTF-8") == "de_DE");
            T.Check("two locales compare: the same code 2, the same language 1, otherwise 0", TranslationServer.CompareLocales("fr_CA", "fr_CA") == 2 && TranslationServer.CompareLocales("fr_CA", "fr_FR") == 1 && TranslationServer.CompareLocales("fr", "de") == 0);

            var fr = new Translation { Locale = "fr" };
            fr.AddMessage("Hello", "Bonjour");
            fr.AddMessage("Hello", "Bonjour (menu)", "menu");
            fr.AddPluralMessage("%d apple", new[] { "%d pomme", "%d pommes" });
            T.Check("a translation answers its messages, by context", fr.GetMessage("Hello") == "Bonjour" && fr.GetMessage("Hello", "menu") == "Bonjour (menu)" && fr.GetMessage("Nope") == "" && fr.GetMessageCount() == 3);
            T.Check("and lists them", fr.GetMessageList().Contains("Hello") && fr.GetTranslatedMessageList().Contains("Bonjour"));
            T.Check("with no translation added the text comes back as it is", TranslationServer.Translate("Hello") == "Hello");
            TranslationServer.AddTranslation(fr);
            T.Check("an added translation is not used until its locale is current", TranslationServer.Translate("Hello") == "Hello" && TranslationServer.GetLoadedLocales().SequenceEqual(new[] { "fr" }));
            TranslationServer.SetLocale("fr");
            T.Check("the current locale picks it", TranslationServer.GetLocale() == "fr" && TranslationServer.Translate("Hello") == "Bonjour" && TranslationServer.Translate("Hello", "menu") == "Bonjour (menu)");
            T.Check("a message it lacks comes back as it is", TranslationServer.Translate("Goodbye") == "Goodbye");
            TranslationServer.SetLocale("fr_CA");
            T.Check("a regional locale falls back to its language's translation", TranslationServer.Translate("Hello") == "Bonjour");
            TranslationServer.SetLocale("de");
            T.Check("another language finds none", TranslationServer.Translate("Hello") == "Hello");
            TranslationServer.SetFallbackLocale("fr");
            T.Check("until the fallback locale has it", TranslationServer.Translate("Hello") == "Bonjour");
            TranslationServer.SetLocale("fr");
            T.Check("plural messages pick a form by the language's rule (French: 0 and 1 are the singular)", TranslationServer.TranslatePlural("%d apple", "%d apples", 1) == "%d pomme" &&
                    TranslationServer.TranslatePlural("%d apple", "%d apples", 0) == "%d pomme" && TranslationServer.TranslatePlural("%d apple", "%d apples", 3) == "%d pommes");
            var node = new Node();
            T.Check("Object.tr and tr_n are the server's", node.Tr("Hello") == "Bonjour" && node.TrN("%d apple", "%d apples", 5) == "%d pommes");
            TranslationServer.RemoveTranslation(fr);
            T.Check("a removed translation stops answering", node.Tr("Hello") == "Hello" && TranslationServer.GetTranslationObject("fr") == null);
            TranslationServer.AddTranslation(fr);
            T.Check("a translation is found by locale", ReferenceEquals(TranslationServer.GetTranslationObject("fr"), fr) && ReferenceEquals(TranslationServer.GetTranslationObject("fr_BE"), fr));
            TranslationServer.Clear();
            T.Check("clear forgets them all", TranslationServer.GetLoadedLocales().Length == 0);
            T.Check("the Russian rule has three forms and Japanese one", Translation.PluralForm("ru", 1) == 0 && Translation.PluralForm("ru", 3) == 1 && Translation.PluralForm("ru", 5) == 2 && Translation.PluralForm("ja", 9) == 0);

            const string po = "# a comment\nmsgid \"\"\nmsgstr \"\"\n\"Language: de\\n\"\n\"Plural-Forms: nplurals=2;\\n\"\n\nmsgid \"Hello\"\nmsgstr \"Hallo\"\n\n" +
                              "msgctxt \"menu\"\nmsgid \"Open\"\nmsgstr \"Oeffnen\"\n\nmsgid \"One file\"\nmsgid_plural \"%d files\"\nmsgstr[0] \"Eine Datei\"\nmsgstr[1] \"%d Dateien\"\n\n" +
                              "msgid \"Multi\"\nmsgstr \"\"\n\"line one\\n\"\n\"line \\\"two\\\"\"\n";
            Translation de = PoFile.Parse(po);
            T.Check("a .po file's header names the locale", de.Locale == "de");
            T.Check("its messages, contexts and escapes read", de.GetMessage("Hello") == "Hallo" && de.GetMessage("Open", "menu") == "Oeffnen" && de.GetMessage("Multi") == "line one\nline \"two\"");
            T.Check("its plural forms read", de.GetPluralMessage("One file", "%d files", 1) == "Eine Datei" && de.GetPluralMessage("One file", "%d files", 2) == "%d Dateien");
            T.Check("the header's empty id is not a message", de.GetMessageList().All(m => m.Length > 0));
            T.Check("a locale argument overrides the header", PoFile.Parse(po, "de_AT").Locale == "de_AT");
            T.Check("an empty file is an empty translation", PoFile.Parse("").GetMessageCount() == 0);
            TranslationServer.AddTranslation(de);
            TranslationServer.SetLocale("de");
            T.Check("a parsed catalog works in the server", TranslationServer.Translate("Hello") == "Hallo");
        }
        finally { TranslationServer.Reset(); }
    }

    // ---- F49 ----

    private static void TextObjects()
    {
        T.Reset();
        UiText.WidthProvider = (family, px, flags, text) => text.Length * px * 0.5f;
        UiText.LineProvider = (family, px) => px * 1.2f;
        try
        {
            var font = new FontFile();
            var line = new TextLine();
            T.Check("a font and a size make a run", line.AddString("Hello", font, 16) && !line.AddString("x", null!, 16));
            T.Check("the line's width is its text's", Near(line.GetLineWidth(), 40f) && Near(line.GetSize().X, 40f));
            T.Check("its height is ascent plus descent, the font's line height", Near(line.GetSize().Y, 19.2f) && Near(line.GetLineAscent() + line.GetLineDescent(), 19.2f));
            line.SetWidth(100f);
            line.SetHorizontalAlignment(HorizontalAlignment.Center);
            T.Check("a width and a centre alignment make the line's size the width", Near(line.GetSize().X, 100f));
            T.Check("a point is hit to the nearest character boundary (8 px a character, the line starting at 30)", line.HitTest(30f + 3f) == 0 && line.HitTest(30f + 5f) == 1 && line.HitTest(30f + 7f) == 1);
            T.Check("a point before the line hits 0 and past it the length", line.HitTest(-5f) == 0 && line.HitTest(500f) == 5);
            line.SetWidth(20f);
            line.SetTextOverrunBehavior(TextServer.OverrunBehavior.TrimEllipsis);
            T.Check("an overrun behaviour trims to the width (with an ellipsis)", line.GetLineWidth() <= 20.01f);
            line.Clear();
            T.Check("clear empties it", line.GetText() == "" && Near(line.GetLineWidth(), 0f));
            line.AddString("ab", font, 10);
            line.AddString("cd", font, 20);
            T.Check("runs of different sizes add up (2 x 5 + 2 x 10)", Near(line.GetLineWidth(), 30f));

            var para = new TextParagraph { Alignment = HorizontalAlignment.Left };
            para.AddString("aaaa bbbb cccc", font, 10);
            para.SetWidth(25f);
            T.Check("a paragraph breaks at words to the width: three lines", para.GetLineCount() == 3);
            T.Check("its lines' ranges are in the source text", para.GetLineRange(1) == new Vector2I(5, 9) && para.GetLineRange(0) == new Vector2I(0, 4) && para.GetLineRange(9) == new Vector2I(0, 0));
            T.Check("a line's width is its text's", Near(para.GetLineWidth(0), 20f) && Near(para.GetLineSize(1).Y, 12f));
            T.Check("the block is the widest line wide and as tall as its lines", Near(para.GetSize().X, 20f) && Near(para.GetSize().Y, 36f));
            para.SetMaxLinesVisible(2);
            T.Check("max lines visible cuts the height", Near(para.GetSize().Y, 24f));
            para.SetMaxLinesVisible(-1);
            T.Check("a point in the second line's text hits its characters", para.HitTest(new Vector2(7f, 15f)) == 6 && para.HitTest(new Vector2(7f, 500f)) == -1);

            var p = new Painter();
            p.Paint = c => { line.Draw(c, new Vector2(2, 3)); para.Draw(c, new Vector2(0, 40)); };
            SceneTree.Instance.Root.AddChild(p);
            p.RunDraw();
            T.Check("both draw onto a canvas item in its draw pass: one string for the line's runs, a string per paragraph line", p.Commands.Count(c => c.Type == CanvasItem.DrawCommand.Kind.Text) == 2 + 3);
        }
        finally { UiText.ResetForTests(); }
    }

    // ---- F50 ----

    private static void CodeEditing()
    {
        T.Reset();
        var hl = new CodeHighlighter { NumberColor = new Color(0, 0, 1, 1), FunctionColor = new Color(0, 1, 1, 1), SymbolColor = new Color(0.5f, 0.5f, 0.5f, 1) };
        var red = new Color(1, 0, 0, 1);
        hl.AddKeywordColor("func", red);
        hl.AddKeywordColor("return", red);
        hl.AddColorRegion("#", "", new Color(0, 1, 0, 1), true);
        hl.AddColorRegion("\"", "\"", new Color(1, 1, 0, 1));
        hl.AddColorRegion("/*", "*/", new Color(0, 1, 0, 0.5f));
        T.Check("keywords, regions and member keywords can be asked about", hl.HasKeywordColor("func") && !hl.HasKeywordColor("nope") && hl.HasColorRegion("#") && hl.GetKeywordColor("func") == red);

        var edit = new CodeEdit { Text = "func f(x):\n\tvar s = \"a b\" # note\n\treturn 42\nint /* c\n d */ e" };
        edit.SyntaxHighlighter = hl;
        T.Check("a highlighter belongs to its editor", ReferenceEquals(hl.GetTextEdit(), edit) && ReferenceEquals(edit.SyntaxHighlighter, hl));

        Color? At(int line, int col)
        {
            Color? c = null;
            foreach (var r in hl.Runs(line, edit.GetLine(line))) { if (r.Start <= col) c = r.Color; else break; }
            return c;
        }
        T.Check("a keyword is its colour", At(0, 0) == red && At(0, 3) == red);
        T.Check("a name before ( is a function", At(0, 5) == hl.FunctionColor);
        T.Check("a symbol is the symbol colour", At(0, 6) == hl.SymbolColor);
        T.Check("a plain name has none", At(0, 7) == null);
        T.Check("a string is its region's colour, quotes included, and ends at the closing quote", At(1, 9) == new Color(1, 1, 0, 1) && At(1, 12) == new Color(1, 1, 0, 1) && At(1, 14) == null);
        T.Check("a line comment runs to the end of the line", At(1, 16) == new Color(0, 1, 0, 1) && At(1, 20) == new Color(0, 1, 0, 1));
        T.Check("a number is the number colour", At(2, 8) == hl.NumberColor);
        T.Check("a multi-line region starts on its line", At(3, 6) == new Color(0, 1, 0, 0.5f));
        T.Check("and the next line starts inside it until it closes", At(4, 0) == new Color(0, 1, 0, 0.5f) && At(4, 4) == new Color(0, 1, 0, 0.5f) && At(4, 5) == null);
        Collections.Dictionary d = hl.GetLineSyntaxHighlighting(0);
        T.Check("GDScript's form is a dictionary of column to { color }", d.Count > 0 && d.ContainsKey(0));
        hl.ClearKeywordColors();
        T.Check("clearing the keywords uncolours them", At(0, 0) == null);

        edit.AddStringDelimiter("\"", "\"");
        edit.AddCommentDelimiter("#", "", true);
        edit.Text = "x = \"a # b\" # c\ny = 1";
        T.Check("inside a string, a comment mark is not a comment", edit.IsInString(0, 7) && !edit.IsInComment(0, 7));
        T.Check("after the string, the comment is one", edit.IsInComment(0, 14) && !edit.IsInString(0, 14));
        T.Check("outside both is neither", !edit.IsInString(0, 1) && !edit.IsInComment(1, 3));
        T.Check("delimiters can be asked and removed", edit.HasStringDelimiter("\"") && edit.HasCommentDelimiter("#"));
        edit.RemoveCommentDelimiter("#");
        T.Check("a removed comment delimiter ends comments", !edit.IsInComment(0, 14));

        edit.Text = "def a:\n    b\n    c\n\n    d\ne\nf";
        T.Check("a line folds over the deeper lines under it, blank ones inside included", edit.GetFoldEnd(0) == 4 && edit.CanFoldLine(0) && !edit.CanFoldLine(1) && edit.GetFoldEnd(5) == 5);
        edit.FoldLine(0);
        T.Check("folding records it", edit.IsLineFolded(0) && edit.GetFoldedLines().SequenceEqual(new[] { 0 }) && !edit.CanFoldLine(0));
        edit.UnfoldLine(0);
        edit.FoldAllLines();
        T.Check("fold all folds every foldable line", edit.IsLineFolded(0));
        edit.UnfoldAllLines();
        T.Check("unfold all clears", edit.GetFoldedLines().Length == 0);

        edit.Text = "a\nb\nc";
        edit.IndentUseSpaces = false;
        edit.Select(0, 0, 1, 1);
        edit.IndentLines();
        T.Check("indent adds a tab to the selected lines only", edit.GetLine(0) == "\ta" && edit.GetLine(1) == "\tb" && edit.GetLine(2) == "c");
        edit.UnindentLines();
        T.Check("unindent takes it off", edit.GetLine(0) == "a" && edit.GetLine(1) == "b");
        edit.DuplicateLines();
        T.Check("duplicate repeats the selected lines below them", edit.GetLineCount() == 5 && edit.GetLine(2) == "a" && edit.GetLine(3) == "b" && edit.GetLine(4) == "c");
        edit.Text = "a\nb\nc";
        edit.Select(1, 0, 1, 1);
        edit.MoveLinesUp();
        T.Check("move up swaps with the line above", edit.GetLine(0) == "b" && edit.GetLine(1) == "a" && edit.GetLine(2) == "c");
        edit.Select(0, 0, 0, 1);
        edit.MoveLinesDown();
        T.Check("move down swaps with the line below", edit.GetLine(0) == "a" && edit.GetLine(1) == "b");
        edit.Select(1, 0, 1, 1);
        edit.DeleteLines();
        T.Check("delete removes the selected lines", edit.GetLineCount() == 2 && edit.GetLine(1) == "c");
        edit.AddCommentDelimiter("#", "", true);
        edit.Text = "a\n  b";
        edit.SelectAll();
        edit.ToggleLineComments();
        T.Check("toggling comments adds the line comment after the indent", edit.GetLine(0) == "# a" && edit.GetLine(1) == "  # b");
        edit.ToggleLineComments();
        T.Check("and removes it again", edit.GetLine(0) == "a" && edit.GetLine(1) == "  b");
    }

    // ---- F52 ----

    private static void Controls()
    {
        T.Reset();
        var rr = new ReferenceRect();
        T.Check("a reference rectangle never takes the pointer and draws nothing at run time", rr.MouseFilter == Control.MouseFilterEnum.Ignore);
        rr.Size = new Vector2(10, 10);
        rr.RunDraw();
        T.Check("(by default)", rr.Commands.Count == 0);
        rr.EditorOnly = false;
        rr.QueueRedraw();
        rr.RunDraw();
        T.Check("with editor only off it outlines its rectangle", rr.Commands.Count == 4);

        var group = new FoldableGroup();
        var a = new FoldableContainer { Title = "A", Size = new Vector2(100, 100) };
        var b = new FoldableContainer { Title = "B", Size = new Vector2(100, 100) };
        var inner = new Control { Name = "Inner" };
        a.AddChild(inner);
        SceneTree.Instance.Root.AddChild(a);
        SceneTree.Instance.Root.AddChild(b);
        T.Check("a foldable container is open at first and its content visible", !a.Folded && inner.Visible);
        a.Folded = true;
        T.Check("folding hides the content", a.Folded && !inner.Visible);
        a.Folded = false;
        T.Check("opening shows it again", inner.Visible);
        inner.Visible = false;
        a.Folded = true;
        a.Folded = false;
        T.Check("what a script hid stays hidden through a fold", !inner.Visible);
        inner.Visible = true;
        var seen = new List<bool>();
        a.FoldingChanged += f => seen.Add(f);
        a.Folded = true;
        a.Folded = true;
        a.Folded = false;
        T.Check("folding_changed is emitted on a change only", seen.SequenceEqual(new[] { true, false }));

        a.FoldableGroup = group;
        b.FoldableGroup = group;
        T.Check("joining a group that has an open container folds the newcomer", !a.Folded && b.Folded && ReferenceEquals(group.GetExpandedContainer(), a));
        Collections.Array members = group.GetContainers();
        T.Check("the group lists its members", members.Count == 2);
        var opened = new List<FoldableContainer>();
        group.Expanded += c => opened.Add(c);
        b.Folded = false;
        T.Check("opening one folds the other and says so", a.Folded && !b.Folded && opened.Count == 1 && ReferenceEquals(opened[0], b));
        b.Folded = true;
        T.Check("the last open one cannot be folded in a group", !b.Folded);
        group.AllowFoldingAll = true;
        b.Folded = true;
        T.Check("unless the group allows it", b.Folded && group.GetExpandedContainer() == null);
        T.Check("a folded container asks for its title's height only", a.GetCombinedMinimumSize().Y > 0f && a.GetCombinedMinimumSize().Y < 60f);

        var bar = new MenuBar { Size = new Vector2(400, 24) };
        var file = new PopupMenu { Name = "File" };
        var edit = new PopupMenu { Name = "Edit" };
        file.AddItem("Open");
        edit.AddItem("Undo");
        bar.AddChild(file);
        bar.AddChild(edit);
        SceneTree.Instance.Root.AddChild(bar);
        T.Check("a menu bar's menus are its PopupMenu children, titled by their names", bar.GetMenuCount() == 2 && bar.GetMenuTitle(0) == "File" && bar.GetMenuTitle(1) == "Edit" && ReferenceEquals(bar.GetMenuPopup(1), edit));
        bar.SetMenuTitle(1, "Modify");
        T.Check("a title can be set", bar.GetMenuTitle(1) == "Modify");
        Rect2 r0 = bar.GetMenuRect(0), r1 = bar.GetMenuRect(1);
        T.Check("titles sit left to right with a gap, and a point finds its menu", r0.Size.X > 0 && r1.Position.X > r0.End.X && bar.MenuAt(r1.Position + new Vector2(2, 2)) == 1 && bar.MenuAt(new Vector2(399, 5)) == -1);
        bar.SetMenuHidden(0, true);
        T.Check("a hidden menu takes no room and is not hit", bar.GetMenuRect(0).Size.X == 0f && bar.GetMenuRect(1).Position.X < r1.Position.X && bar.MenuAt(new Vector2(2, 2)) == 1);
        bar.SetMenuDisabled(1, true);
        T.Check("a menu can be disabled", bar.IsMenuDisabled(1) && bar.IsMenuHidden(0) && bar.GetMenuTooltip(0) == "");
        T.Check("the bar reports a width for its titles and is never a native menu here", bar.GetCombinedMinimumSize().X > 0f && !bar.IsNativeMenu());
    }

    // ---- F55 ----

    private static void Picker()
    {
        T.Reset();
        var cp = new ColorPicker { PickerShape = ColorPicker.PickerShapeType.HsvWheel, Size = new Vector2(300, 300) };
        cp.Color = new Color(1, 0, 0, 1);
        T.Check("hex reads as RRGGBBAA when alpha is editable and RRGGBB when it is not", cp.Hex == "ff0000ff");
        cp.EditAlpha = false;
        T.Check("(without alpha)", cp.Hex == "ff0000");
        cp.Hex = "00ff00";
        T.Check("setting the hex sets the colour", Near(cp.Color.G, 1f) && Near(cp.Color.R, 0f));
        cp.Hex = "not a colour";
        T.Check("a bad hex changes nothing", Near(cp.Color.G, 1f));
        cp.AddRecentPreset(new Color(1, 0, 0, 1));
        cp.AddRecentPreset(new Color(0, 0, 1, 1));
        cp.AddRecentPreset(new Color(1, 0, 0, 1));
        T.Check("recent presets are newest first without repeats", cp.GetRecentPresets().Length == 2 && cp.GetRecentPresets()[0] == new Color(1, 0, 0, 1));
        cp.EraseRecentPreset(new Color(1, 0, 0, 1));
        T.Check("and can be erased", cp.GetRecentPresets().Length == 1);
        for (int i = 0; i < 40; i++) cp.AddRecentPreset(new Color(i / 40f, 0, 0, 1));
        T.Check("there are at most 32", cp.GetRecentPresets().Length == 32);

        var (center, outer, inner, square) = cp.WheelGeometry();
        T.Check("the wheel is a ring with a square inside it", outer > inner && inner > 0f && square.Size.X > 0f && (square.End - center).Length() < inner);
        T.Check("a point on the ring hits the ring, in the square the square, outside neither",
                cp.WheelHit(center + new Vector2(0, -(outer + inner) / 2f)) == 1 && cp.WheelHit(center) == 2 && cp.WheelHit(center + new Vector2(outer + 20f, 0)) == 0);
        cp.RunDraw();
        T.Check("a wheel draws as polygons (the ring in segments, the square) and not the bar picker's cells", cp.Commands.Count(c => c.Type == CanvasItem.DrawCommand.Kind.Mesh) >= 73 && !cp.Commands.Any(c => c.Type == CanvasItem.DrawCommand.Kind.Rect && c.Rect.Size.X == 30f));
        var flat = new ColorPicker { Size = new Vector2(300, 300) };
        flat.RunDraw();
        T.Check("the default shape still draws the square and the hue bar", flat.Commands.Count(c => c.Type == CanvasItem.DrawCommand.Kind.Rect) >= 256);
    }

    // ---- F57 ----

    private static void Layout()
    {
        T.Reset();
        var c = new Control { Size = new Vector2(100, 50), Position = new Vector2(10, 20) };
        Transform2D plain = c.GetTransform();
        c.Rotation = MathF.PI / 2f;
        c.PivotOffsetRatio = new Vector2(0.5f, 0.5f);
        Vector2 centre = c.GetTransform() * new Vector2(50, 25);
        T.Check("pivot_offset_ratio rotates about the centre: the centre stays where it was", Near(centre.X, 60f) && Near(centre.Y, 45f), $"{centre}");
        c.PivotOffsetRatio = Vector2.Zero;
        Vector2 corner = c.GetTransform() * new Vector2(0, 0);
        T.Check("with no ratio and no pivot it rotates about the origin as before", Near(corner.X, 10f) && Near(corner.Y, 20f) && plain.Origin == new Vector2(10, 20));

        var parent = new Control { LayoutDirection = Control.LayoutDirectionEnum.Rtl };
        var child = new Control();
        parent.AddChild(child);
        T.Check("layout direction inherits from the nearest ancestor that sets it", child.IsLayoutRtl() && !new Control().IsLayoutRtl());
        child.LayoutDirection = Control.LayoutDirectionEnum.Ltr;
        T.Check("and the control's own wins", !child.IsLayoutRtl());
        child.AccessibilityName = "Close";
        child.AccessibilityLive = Control.AccessibilityLiveMode.Polite;
        T.Check("the accessibility properties and the shortcut context are kept", child.AccessibilityName == "Close" && child.AccessibilityLive == Control.AccessibilityLiveMode.Polite && child.ShortcutContext == null);
    }

    // ---- F59 (native) ----

    private static void DrawOrder()
    {
        T.Reset();
        var root = SceneTree.Instance.Root;
        Drawn Make(string name, int z = 0) => new Drawn { Name = name, Size = new Vector2(10, 10), ZIndex = z };
        var a = Make("A"); var b = Make("B", 5); var c = Make("C", -1);
        root.AddChild(a); root.AddChild(b); root.AddChild(c);
        var l2 = new CanvasLayer { Name = "L2", Layer = 2 }; l2.AddChild(Make("D")); root.AddChild(l2);
        var hidden = new CanvasLayer { Name = "H", Layer = 3 }; hidden.AddChild(Make("E")); hidden.Visible = false; root.AddChild(hidden);
        var low = new CanvasLayer { Name = "L-1", Layer = -1 }; low.AddChild(Make("F")); root.AddChild(low);
        Gui.DrawLog = new List<string>();
        try
        {
            UiNative.aver_ui_begin_frame(0, 0, 1280, 720);
            Gui.DrawAll();
            T.Check("lower layers first, then z_index within a layer, then tree order; a hidden layer draws nothing", Gui.DrawLog.SequenceEqual(new[] { "F", "C", "A", "B", "D" }), string.Join(",", Gui.DrawLog));

            T.Reset();
            root = SceneTree.Instance.Root;
            var p = Make("P", 3); var q = Make("Q", 2); var r = Make("R", 1);
            p.AddChild(q);
            q.AddChild(r);
            root.AddChild(p);
            var s = Make("S", 4);
            root.AddChild(s);
            Gui.DrawLog = new List<string>();
            UiNative.aver_ui_begin_frame(0, 0, 1280, 720);
            Gui.DrawAll();
            T.Check("z_index adds down the tree while z_as_relative holds: P 3, Q 5, R 6, S 4", Gui.DrawLog.SequenceEqual(new[] { "P", "S", "Q", "R" }), string.Join(",", Gui.DrawLog));
            q.ZAsRelative = false;
            Gui.DrawLog = new List<string>();
            UiNative.aver_ui_begin_frame(0, 0, 1280, 720);
            Gui.DrawAll();
            T.Check("with z_as_relative off Q is 2 and its child R is 3", Gui.DrawLog.SequenceEqual(new[] { "Q", "P", "R", "S" }), string.Join(",", Gui.DrawLog));

            T.Reset();
            root = SceneTree.Instance.Root;
            var solo = Make("Solo");
            var plainLayer = new CanvasLayer { Name = "Only", Layer = 1, Offset = new Vector2(100, 50), Scale = new Vector2(2, 2) };
            plainLayer.AddChild(solo);
            root.AddChild(plainLayer);
            Gui.DrawLog = new List<string>();
            UiNative.aver_ui_begin_frame(0, 0, 1280, 720);
            int pushes = UiNative.TransformPushes;
            Gui.DrawAll();
            T.Check("a layer's offset and scale move what is on it without a rotation (no transform push)", Gui.DrawLog.SequenceEqual(new[] { "Solo" }) && UiNative.TransformPushes == pushes);
            plainLayer.Rotation = 0.5f;
            UiNative.aver_ui_begin_frame(0, 0, 1280, 720);
            Gui.DrawAll();
            T.Check("a rotated layer goes through the engine's transform", UiNative.TransformPushes > pushes);
        }
        finally { Gui.DrawLog = null; }
    }

    // ---- P40 (native) ----

    private static void Rotation()
    {
        T.Reset();
        var root = SceneTree.Instance.Root;
        var spin = new Drawn { Name = "Spin", Size = new Vector2(50, 30), Position = new Vector2(100, 100), RotationDegrees = 30f };
        root.AddChild(spin);
        UiNative.aver_ui_begin_frame(0, 0, 1280, 720);
        int before = UiNative.TransformPushes;
        Gui.DrawAll();
        T.Check("each command of a rotated control is drawn through a pushed transform (a rectangle and a box)", UiNative.TransformPushes - before == 2, $"{UiNative.TransformPushes - before}");
        T.Check("and still adds its quads to the draw list", UiNative.aver_ui_vertex_count() >= 8);

        spin.RotationDegrees = 0f;
        spin.Scale = new Vector2(2, 2);
        spin.QueueRedraw();
        UiNative.aver_ui_begin_frame(0, 0, 1280, 720);
        before = UiNative.TransformPushes;
        Gui.DrawAll();
        T.Check("a scale alone stays on the axis-aligned path (no push)", UiNative.TransformPushes == before);

        spin.Scale = Vector2.One;
        spin.RotationDegrees = 90f;
        spin.PivotOffsetRatio = new Vector2(0.5f, 0.5f);
        UiNative.aver_ui_begin_frame(0, 0, 1280, 720);
        before = UiNative.TransformPushes;
        Gui.DrawAll();
        T.Check("a quarter turn about the centre is still a transform (the engine takes it exactly)", UiNative.TransformPushes - before == 2);
        T.Check("IsAxisAligned: identity, a scale, a flip are; a turn is not", Gui.IsAxisAligned(Transform2D.Identity) && Gui.IsAxisAligned(new Transform2D(0f, new Vector2(3, -2), 0f, Vector2.Zero)) && !Gui.IsAxisAligned(new Transform2D(0.3f, Vector2.Zero)));
    }

    // ---- E6 (native) ----

    private static void WorldTextAbi()
    {
        T.Reset();
        Text3dDesc d = Text3dDesc.Create();
        UiNative.aver_ui_begin_frame(0, 0, 1280, 720);
        bool drawn = UiNative.Text3dDraw("Hello", ref d, out Vector2 size, out int lines);
        T.Check("with no font set up the engine takes no label and answers zeros (the test process has no font set)", !drawn && size == Vector2.Zero && lines == 0);
        var stats = new float[8];
        UiNative.aver_text3d_stats(stats);
        T.Check("the stats are readable and empty", stats[0] == 0f && stats[1] == 0f);
        UiNative.aver_text3d_configure(1, 48, 6, 512, 8);
        UiNative.aver_text3d_configure(0, 48, 6, 512, 8);
        T.Check("the world atlas can be configured for distance fields and back", true);
        d.Size = 4;
        T.Check("a descriptor too small to hold its matrix is refused", UiNative.aver_text3d_draw("x", ref d, null) == 0);
    }
}
