// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// TextLine and TextParagraph (parity wave 8a, F49): Godot's laid-out text objects. A TextLine is one line of runs with an alignment and an overrun behaviour; a
// TextParagraph is a block broken into lines at a width. Both are built over the line breaking and measuring the text controls share (TextLines, UiText), so a
// size they report is the size a Label of the same font would draw, and Draw puts the text on a CanvasItem inside its draw pass. The RID-based TextServer calls
// (shaped_text_*) are not here: those are the engine's UiTextLayout queries, which the UI ABI does not export yet.

namespace Aver.Fabric;

/// <summary>One line of text with a font and size per run (Godot's TextLine).</summary>
public class TextLine : RefCounted
{
    private readonly List<(string Text, Font? Font, int Size)> _runs = new();
    private float _width = -1f;
    private HorizontalAlignment _align = HorizontalAlignment.Left;
    private TextServer.OverrunBehavior _overrun = TextServer.OverrunBehavior.NoTrimming;

    public TextServer.Direction Direction { get; set; } = TextServer.Direction.Auto;
    public TextServer.Orientation Orientation { get; set; } = TextServer.Orientation.Horizontal;
    public TextServer.JustificationFlag Flags { get; set; } = TextServer.JustificationFlag.Kashida | TextServer.JustificationFlag.WordBound;

    public void Clear() => _runs.Clear();

    public bool AddString(string text, Font font, int fontSize = 16, string language = "", Variant meta = default)
    {
        if (font == null || fontSize <= 0) return false;
        _runs.Add((text ?? "", font, fontSize));
        return true;
    }

    public float GetWidth() => _width;
    public void SetWidth(float width) => _width = width;
    public HorizontalAlignment GetHorizontalAlignment() => _align;
    public void SetHorizontalAlignment(HorizontalAlignment alignment) => _align = alignment;
    public TextServer.OverrunBehavior GetTextOverrunBehavior() => _overrun;
    public void SetTextOverrunBehavior(TextServer.OverrunBehavior behavior) => _overrun = behavior;
    public Collections.Array GetObjects() => new();

    /// <summary>The runs' text as one string.</summary>
    public string GetText() => string.Concat(_runs.Select(r => r.Text));

    private string Shown(out Font? font, out int size)
    {
        font = _runs.Count > 0 ? _runs[0].Font : null;
        size = _runs.Count > 0 ? _runs[0].Size : 16;
        string s = GetText();
        if (_width > 0f && _overrun != TextServer.OverrunBehavior.NoTrimming && UiText.Width(font, size, s) > _width)
        {
            bool word = _overrun is TextServer.OverrunBehavior.TrimWord or TextServer.OverrunBehavior.TrimWordEllipsis or TextServer.OverrunBehavior.TrimWordEllipsisForce;
            bool ellipsis = _overrun is not (TextServer.OverrunBehavior.TrimChar or TextServer.OverrunBehavior.TrimWord);
            s = TextLines.Trim(s, font, size, _width, word, ellipsis ? "…" : "");
        }
        return s;
    }

    private float RunWidth()
    {
        // The runs are measured one by one (each has its own font and size); a trim applies to the first run's metrics, as a single-font line does.
        if (_runs.Count <= 1) { string s = Shown(out Font? f, out int px); return UiText.Width(f, px, s); }
        float w = 0f;
        foreach (var r in _runs) w += UiText.Width(r.Font, r.Size, r.Text);
        return w;
    }

    /// <summary>The width of the line's text, trimmed when an overrun behaviour is set and a width.</summary>
    public float GetLineWidth() => RunWidth();

    public float GetLineAscent() => _runs.Count == 0 ? 0f : _runs.Max(r => UiText.Ascent(r.Font, r.Size));
    public float GetLineDescent() => _runs.Count == 0 ? 0f : _runs.Max(r => UiText.LineHeight(r.Font, r.Size) - UiText.Ascent(r.Font, r.Size));
    public float GetLineUnderlinePosition() => _runs.Count == 0 ? 0f : _runs.Max(r => r.Font?.GetUnderlinePosition(r.Size) ?? 0f);
    public float GetLineUnderlineThickness() => _runs.Count == 0 ? 0f : _runs.Max(r => r.Font?.GetUnderlineThickness(r.Size) ?? 1f);
    public Vector2 GetSize() => new(_width > 0f && _align != HorizontalAlignment.Left ? Math.Max(_width, RunWidth()) : RunWidth(), GetLineAscent() + GetLineDescent());

    /// <summary>Where the line starts in a box of the width, by its alignment.</summary>
    private float StartX()
    {
        if (_width <= 0f) return 0f;
        float w = RunWidth();
        return _align switch { HorizontalAlignment.Center => (_width - w) / 2f, HorizontalAlignment.Right => _width - w, _ => 0f };
    }

    /// <summary>Draws the line with its top-left corner at <paramref name="pos"/>, on a canvas item that is in its draw pass.</summary>
    public void Draw(CanvasItem canvas, Vector2 pos, Color? color = null)
    {
        float x = pos.X + StartX();
        float baseline = pos.Y + GetLineAscent();
        if (_runs.Count == 1)
        {
            string s = Shown(out Font? f, out int px);
            canvas.DrawString(f, new Vector2(x, baseline), s, HorizontalAlignment.Left, -1f, px, color);
            return;
        }
        foreach (var r in _runs)
        {
            canvas.DrawString(r.Font, new Vector2(x, baseline), r.Text, HorizontalAlignment.Left, -1f, r.Size, color);
            x += UiText.Width(r.Font, r.Size, r.Text);
        }
    }

    public void DrawOutline(CanvasItem canvas, Vector2 pos, int outlineSize = 1, Color? color = null)
    {
        float x = pos.X + StartX();
        float baseline = pos.Y + GetLineAscent();
        foreach (var r in _runs)
        {
            canvas.DrawStringOutline(r.Font, new Vector2(x, baseline), r.Text, HorizontalAlignment.Left, -1f, r.Size, outlineSize, color);
            x += UiText.Width(r.Font, r.Size, r.Text);
        }
    }

    /// <summary>The character index at a point (x measured from the line's start): the boundary nearest to it, or -1 above or below the line.</summary>
    public int HitTest(float coords)
    {
        if (_runs.Count == 0) return -1;
        coords -= StartX();
        string all = GetText();
        if (coords <= 0f) return 0;
        float x = 0f;
        int i = 0;
        foreach (var r in _runs)
        {
            for (int k = 0; k < r.Text.Length; k++)
            {
                float w = UiText.Width(r.Font, r.Size, r.Text.Substring(k, 1));
                if (coords < x + w / 2f) return i;
                x += w;
                i++;
            }
        }
        return all.Length;
    }
}

/// <summary>A block of text broken into lines at a width (Godot's TextParagraph).</summary>
public class TextParagraph : RefCounted
{
    private readonly List<(string Text, Font? Font, int Size)> _runs = new();
    private float _width = -1f;
    private TextLines? _lines;

    public HorizontalAlignment Alignment { get; set; } = HorizontalAlignment.Left;
    public TextServer.Direction Direction { get; set; } = TextServer.Direction.Auto;
    public TextServer.Orientation Orientation { get; set; } = TextServer.Orientation.Horizontal;
    public TextServer.LineBreakFlag BreakFlags { get; set; } = TextServer.LineBreakFlag.WordBound;
    public TextServer.OverrunBehavior TextOverrunBehavior { get; set; } = TextServer.OverrunBehavior.NoTrimming;
    public int MaxLinesVisible { get; set; } = -1;
    public int Dropcap { get; private set; }

    public void Clear() { _runs.Clear(); _lines = null; }

    public bool AddString(string text, Font font, int fontSize = 16, string language = "", Variant meta = default)
    {
        if (font == null || fontSize <= 0) return false;
        _runs.Add((text ?? "", font, fontSize));
        _lines = null;
        return true;
    }

    public float GetWidth() => _width;
    public void SetWidth(float width) { _width = width; _lines = null; }
    public HorizontalAlignment GetAlignment() => Alignment;
    public void SetAlignment(HorizontalAlignment alignment) => Alignment = alignment;
    public TextServer.LineBreakFlag GetBreakFlags() => BreakFlags;
    public void SetBreakFlags(TextServer.LineBreakFlag flags) { BreakFlags = flags; _lines = null; }
    public int GetMaxLinesVisible() => MaxLinesVisible;
    public void SetMaxLinesVisible(int max) => MaxLinesVisible = max;

    private TextLines Lines()
    {
        if (_lines != null) return _lines;
        Font? f = _runs.Count > 0 ? _runs[0].Font : null;
        int px = _runs.Count > 0 ? _runs[0].Size : 16;
        TextLines.Wrap wrap = _width <= 0f ? TextLines.Wrap.Off : (BreakFlags & TextServer.LineBreakFlag.Adaptive) != 0 ? TextLines.Wrap.Smart : (BreakFlags & TextServer.LineBreakFlag.WordBound) != 0 ? TextLines.Wrap.Word : TextLines.Wrap.Arbitrary;
        return _lines = TextLines.Build(string.Concat(_runs.Select(r => r.Text)), f, px, _width, wrap);
    }

    private int Visible() => MaxLinesVisible > 0 ? Math.Min(MaxLinesVisible, Lines().Count) : Lines().Count;

    public int GetLineCount() => Lines().Count;
    public float GetLineWidth(int line) => line >= 0 && line < Lines().Count ? Lines().Width(line) : 0f;
    public Vector2 GetLineSize(int line) => new(GetLineWidth(line), Lines().LineHeight);
    public float GetLineAscent(int line) => UiText.Ascent(Lines().Font, Lines().Px);
    public float GetLineDescent(int line) => Lines().LineHeight - UiText.Ascent(Lines().Font, Lines().Px);

    /// <summary>The character range of a line: its start and end index in the whole text.</summary>
    public Vector2I GetLineRange(int line)
    {
        if (line < 0 || line >= Lines().Count) return new Vector2I(0, 0);
        TextLines.Line l = Lines().Items[line];
        return new Vector2I(l.Start, l.Start + l.Length);
    }

    public Vector2 GetSize()
    {
        float w = 0f;
        for (int i = 0; i < Visible(); i++) w = Math.Max(w, Lines().Width(i));
        return new Vector2(_width > 0f && Alignment != HorizontalAlignment.Left ? Math.Max(w, _width) : w, Visible() * Lines().LineHeight);
    }

    private float LineStartX(int line)
    {
        if (_width <= 0f) return 0f;
        float w = Lines().Width(line);
        return Alignment switch { HorizontalAlignment.Center => (_width - w) / 2f, HorizontalAlignment.Right => _width - w, _ => 0f };
    }

    public void DrawLine(CanvasItem canvas, Vector2 pos, int line, Color? color = null)
    {
        if (line < 0 || line >= Visible()) return;
        TextLines l = Lines();
        canvas.DrawString(l.Font, new Vector2(pos.X + LineStartX(line), pos.Y + UiText.Ascent(l.Font, l.Px)), l.LineText(line), HorizontalAlignment.Left, -1f, (int)l.Px, color);
    }

    public void Draw(CanvasItem canvas, Vector2 pos, Color? color = null)
    {
        for (int i = 0; i < Visible(); i++) DrawLine(canvas, new Vector2(pos.X, pos.Y + i * Lines().LineHeight), i, color);
    }

    /// <summary>The character index at a point in the block, or -1 outside it.</summary>
    public int HitTest(Vector2 coords)
    {
        TextLines l = Lines();
        int line = (int)MathF.Floor(coords.Y / Math.Max(l.LineHeight, 1f));
        if (line < 0 || line >= Visible()) return -1;
        var tl = new TextLine();
        tl.AddString(l.LineText(line), l.Font ?? new Font(), (int)l.Px);
        return GetLineRange(line).X + tl.HitTest(coords.X - LineStartX(line));
    }
}
