// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Label3D (parity wave 8a, E6 and F47): text in the world, drawn by the engine's world text (ui_abi.h "text in the world"). The engine lays the string out with its
// text engine, builds glyph quads in a plane and draws them in the scene's transparent pass with a plain raster shader; this class is the Godot half: it
// turns the node's properties and transform into the engine's descriptor every frame the label is visible, which is how an immediate mode draw list is fed.
//
// What the descriptor carries, and where Godot's look is approximated (the calibration lives here, not in the engine):
//  - the plane's matrix is the node's global transform in the engine's frame (FabricSpace), scaled from metres to centimetres, so pixel_size stays Godot's;
//  - colours are Godot's sRGB values turned into the linear radiance the scene shades in;
//  - shaded is not lit: a Label3D is drawn unlit, like an unshaded one (approximated); alpha_cut hash and opaque prepass draw as ordinary blended text
//    (approximated), discard drops pixels below alpha_scissor_threshold; render_priority is not applied;
//  - fixed_size keeps the label's screen size by the engine's constant-pixel scaling; the pixels per plane unit are inferred from Godot's shader (half the
//    viewport height, so a plane unit is half the screen) and want a look by eye;
//  - billboard particles draws as enabled.

using Aver.Scene;

namespace Aver.Fabric;

/// <summary>Text in the world (Godot's Label3D).</summary>
public class Label3D : GeometryInstance3D
{
    private string _text = "";

    public string Text { get => _text; set { _text = value ?? ""; } }
    /// <summary>The font (a <see cref="Font"/>, F47: <see cref="Font.GetStringSize"/> measures with it); null draws with the project's default.</summary>
    public Font? Font { get; set; }
    public int FontSize { get; set; } = 32;
    public int OutlineSize { get; set; } = 12;
    public float PixelSize { get; set; } = 0.005f;
    public Vector2 Offset { get; set; }
    public Color Modulate { get; set; } = new(1, 1, 1, 1);
    public Color OutlineModulate { get; set; } = new(0, 0, 0, 1);
    public HorizontalAlignment HorizontalAlignment { get; set; } = HorizontalAlignment.Center;
    public VerticalAlignment VerticalAlignment { get; set; } = VerticalAlignment.Center;
    /// <summary>Extra pixels between lines (Godot's line_spacing, default 0 for a Label3D).</summary>
    public float LineSpacing { get; set; }
    /// <summary>Godot's autowrap_mode: 0 off, 1 arbitrary, 2 word, 3 word smart. Every mode but off wraps at words.</summary>
    public int AutowrapMode { get; set; }
    public float Width { get; set; } = 500f;
    public bool Uppercase { get; set; }
    public BaseMaterial3D.BillboardModeEnum Billboard { get; set; }
    public bool Shaded { get; set; }
    public bool DoubleSided { get; set; } = true;
    public bool NoDepthTest { get; set; }
    public bool FixedSize { get; set; }
    public int AlphaCut { get; set; }
    public float AlphaScissorThreshold { get; set; } = 0.5f;
    public float AlphaHashScale { get; set; } = 1f;
    public int TextureFilter { get; set; } = 3;
    public int RenderPriority { get; set; }
    public int OutlineRenderPriority { get; set; } = -1;
    public string Language { get; set; } = "";

    /// <summary>How many times the label was handed to the engine (a test or a stat; zero without an engine).</summary>
    internal int Submissions { get; private set; }

    /// <summary>A test replacement for the engine's draw call: gets the text and the descriptor, answers whether it was drawn. Null uses the engine.</summary>
    internal static Func<string, Text3dDesc, bool>? Submitter;

    /// <summary>The pixels per plane unit of a fixed-size label for a viewport this tall: half of it (inferred from Godot's shader).</summary>
    internal static float FixedSizePixelsPerUnit(float viewportHeight) => viewportHeight * 0.5f;

    private static float Linear(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

    /// <summary>Godot's colour (sRGB values) as the straight 0xAABBGGRR the engine takes, with the rgb converted to linear radiance.</summary>
    internal static uint PackLinear(Color c)
    {
        static uint B(float v) => (uint)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);
        return B(Linear(Math.Clamp(c.R, 0f, 1f))) | (B(Linear(Math.Clamp(c.G, 0f, 1f))) << 8) | (B(Linear(Math.Clamp(c.B, 0f, 1f))) << 16) | (B(c.A) << 24);
    }

    /// <summary>The text as shown: upper-cased when asked.</summary>
    internal string ShownText => Uppercase ? _text.ToUpperInvariant() : _text;

    /// <summary>The engine descriptor for the label as it is now. <paramref name="viewportHeight"/> is the window height in pixels (a fixed-size label needs it).</summary>
    internal Text3dDesc BuildDescriptor(float viewportHeight)
    {
        Text3dDesc d = Text3dDesc.Create();
        Transform3D t = GlobalTransform;
        // The plane's x, y and z axes are the node's, moved to the engine's frame; the plane is in metres and the engine's world in centimetres.
        for (int axis = 0; axis < 3; axis++)
        {
            Vec3 a = FabricSpace.ToAverDirection(t.Basis[axis]);
            d.World[axis * 4 + 0] = a.X * FabricSpace.CmPerMetre;
            d.World[axis * 4 + 1] = a.Y * FabricSpace.CmPerMetre;
            d.World[axis * 4 + 2] = a.Z * FabricSpace.CmPerMetre;
        }
        Vec3 o = FabricSpace.ToAverPosition(t.Origin);
        d.World[12] = o.X; d.World[13] = o.Y; d.World[14] = o.Z; d.World[15] = 1f;

        d.SetFamily(Font?.Family ?? "");
        d.Px = Math.Max(4, FontSize);
        d.PixelSize = PixelSize > 0f ? PixelSize : 0.005f;
        int flags = Font?.StyleFlags ?? 0;
        bool wrap = AutowrapMode != 0 && Width > 0f;
        if (wrap) { flags |= 16; d.Width = Width; }   // AVER_UI_TEXT_FLAG_WRAP
        d.Flags = flags;
        d.HAlign = HorizontalAlignment switch { HorizontalAlignment.Left => 0, HorizontalAlignment.Right => 2, _ => 1 };
        d.VAlign = VerticalAlignment switch { VerticalAlignment.Top => 0, VerticalAlignment.Bottom => 2, _ => 1 };
        float[] sp = Font?.SpacingBlock ?? new float[4];
        d.Spacing[1] = sp[1];
        d.Spacing[2] = sp[2];
        d.Spacing[3] = LineSpacing + (Font?.LineExtra ?? 0f);
        d.Rgba = PackLinear(Modulate);
        d.OutlineRgba = PackLinear(OutlineModulate);
        d.OutlineSize = Math.Max(0, OutlineSize);
        d.OffsetX = Offset.X;
        d.OffsetY = Offset.Y;
        int draw = 0;
        if (NoDepthTest) draw |= 1;       // AVER_TEXT3D_NO_DEPTH
        if (Billboard == BaseMaterial3D.BillboardModeEnum.Enabled || Billboard == BaseMaterial3D.BillboardModeEnum.Particles) draw |= 4;   // BILLBOARD
        else if (Billboard == BaseMaterial3D.BillboardModeEnum.FixedY) draw |= 8;                                                          // BILLBOARD_Y
        if (!DoubleSided) draw |= 16;     // ONE_SIDED
        d.DrawFlags = draw;
        d.AlphaCut = AlphaCut == 1 ? Math.Clamp(AlphaScissorThreshold, 0f, 1f) : 0f;
        d.Intensity = 1f;
        d.FixedPxPerUnit = FixedSize ? FixedSizePixelsPerUnit(viewportHeight) : 0f;
        return d;
    }

    /// <summary>Hands the label to the engine for this frame. Nothing when it is empty, hidden, outside the tree, or there is no engine to draw it.</summary>
    internal bool Submit()
    {
        if (_text.Length == 0 || !IsInsideTree() || !IsVisibleInTree()) return false;
        float h = SceneTree.Instance?.Root is { } root ? root.WindowPixels.Y : 0f;
        Text3dDesc d = BuildDescriptor(h);
        string text = ShownText;
        bool drawn;
        if (Submitter != null) drawn = Submitter(text, d);
        else if (NodeHost.Current.IsEngine && UiNative.Text3dAvailable) drawn = UiNative.Text3dDraw(text, ref d, out _, out _);
        else return false;
        Submissions++;
        if (drawn) IsDrawn = true;
        return drawn;
    }

    /// <summary>True once the engine has taken the label to draw (false without an engine, with no font, or while empty or hidden).</summary>
    public bool IsDrawn { get; private set; }

    /// <summary>The size of the laid-out text in metres (before the node's scale) and its line count, or zeros when the engine cannot measure it.</summary>
    public Vector2 GetTextSize()
    {
        Text3dDesc d = BuildDescriptor(0f);
        d.World = new float[16] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };
        if (NodeHost.Current.IsEngine && UiNative.Text3dMeasure(ShownText, ref d, out Vector2 size, out _)) return size;
        // No engine: the fallback width and line height the controls use, in the label's pixel size.
        TextLines lines = TextLines.Build(ShownText, Font, FontSize, AutowrapMode != 0 ? Width : 0f, TextLines.Wrap.Word);
        return new Vector2(lines.MaxWidth(), lines.Count * (lines.LineHeight + LineSpacing)) * PixelSize;
    }

    internal override bool UsesInternalProcess => true;

    internal override void InternalProcess(double delta) => Submit();
}
