// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// ColorPicker and ColorPickerButton (G5 U2): a simple picker, as the package allows. A saturation / value square, a hue bar, an alpha bar and a row of
// presets; the colour is held as hue, saturation, value and alpha so a grey keeps its hue. There is no hex box, no RGB sliders and no wheel (picker_shape and
// the visibility flags are stored); color_changed, preset_added and preset_removed are Godot's.

namespace Aver.Fabric;

/// <summary>Picks a colour (Godot's ColorPicker).</summary>
public class ColorPicker : Control
{
    public enum PickerShapeType { HsvRectangle = 0, HsvWheel = 1, VhsCircle = 2, OkhslCircle = 3, None = 4 }

    public enum ColorModeType { Rgb = 0, Hsv = 1, Raw = 2, Okhsl = 3 }

    public new class SignalName : Control.SignalName
    {
        public static readonly StringName ColorChanged = "color_changed";
        public static readonly StringName PresetAdded = "preset_added";
        public static readonly StringName PresetRemoved = "preset_removed";
    }

    public event Action<Color> ColorChanged { add => Connect(SignalName.ColorChanged, Callable.From(value)); remove => Disconnect(SignalName.ColorChanged, Callable.From(value)); }
    public event Action<Color> PresetAdded { add => Connect(SignalName.PresetAdded, Callable.From(value)); remove => Disconnect(SignalName.PresetAdded, Callable.From(value)); }
    public event Action<Color> PresetRemoved { add => Connect(SignalName.PresetRemoved, Callable.From(value)); remove => Disconnect(SignalName.PresetRemoved, Callable.From(value)); }

    private float _h, _s, _v = 1f, _a = 1f;
    private readonly List<Color> _presets = new();
    private int _drag;   // 0 none, 1 square, 2 hue, 3 alpha

    public bool EditAlpha { get; set; } = true;
    public bool EditIntensity { get; set; } = true;
    public bool SamplerVisible { get; set; } = true;
    public bool DeferredMode { get; set; }
    public bool CanAddSwatches { get; set; } = true;
    public bool SwatchesVisible { get; set; } = true;
    public bool SlidersVisible { get; set; } = true;
    public bool HexVisible { get; set; } = true;
    public bool PresetsVisible { get; set; } = true;
    public bool ColorModesVisible { get; set; } = true;
    public ColorModeType ColorMode { get; set; }
    public PickerShapeType PickerShape { get; set; }

    public ColorPicker() { MouseFilter = MouseFilterEnum.Stop; }

    /// <summary>The picked colour. Setting it does not emit color_changed (Godot's set_pick_color).</summary>
    public Color Color
    {
        get => Color.FromHsv(_h, _s, _v, _a);
        set
        {
            if (value.S > 0.0001f) _h = value.H;   // a grey keeps the hue it had
            _s = value.S;
            _v = value.V;
            _a = value.A;
            QueueRedraw();
        }
    }

    public float Hue => _h;
    public float Saturation => _s;
    public float Value => _v;
    public float Alpha => _a;

    private void Emit()
    {
        QueueRedraw();
        EmitSignal(SignalName.ColorChanged, Color);
    }

    public void SetHsv(float h, float s, float v)
    {
        _h = Math.Clamp(h, 0f, 1f);
        _s = Math.Clamp(s, 0f, 1f);
        _v = Math.Clamp(v, 0f, 1f);
        Emit();
    }

    public void SetAlpha(float a)
    {
        _a = Math.Clamp(a, 0f, 1f);
        Emit();
    }

    // ---- presets ----

    public void AddPreset(Color color)
    {
        if (_presets.Contains(color)) return;
        _presets.Add(color);
        UpdateMinimumSize();
        QueueRedraw();
        EmitSignal(SignalName.PresetAdded, color);
    }

    public void ErasePreset(Color color)
    {
        if (!_presets.Remove(color)) return;
        UpdateMinimumSize();
        QueueRedraw();
        EmitSignal(SignalName.PresetRemoved, color);
    }

    public Color[] GetPresets() => _presets.ToArray();

    private readonly List<Color> _recent = new();

    /// <summary>Remembers a colour the user picked (Godot keeps the last 32, newest first).</summary>
    public void AddRecentPreset(Color color)
    {
        _recent.Remove(color);
        _recent.Insert(0, color);
        if (_recent.Count > 32) _recent.RemoveAt(_recent.Count - 1);
        QueueRedraw();
    }

    public void EraseRecentPreset(Color color) { if (_recent.Remove(color)) QueueRedraw(); }

    public Color[] GetRecentPresets() => _recent.ToArray();

    /// <summary>The colour as the hex field shows it: RRGGBB, with AA after it when alpha is editable (F55).</summary>
    public string Hex
    {
        get => Color.ToHtml(EditAlpha);
        set { if (Aver.Fabric.Color.HtmlIsValid(value)) { Color = Aver.Fabric.Color.FromString(value, Color); Emit(); } }
    }

    // ---- the wheel shapes (F55) ----

    private bool Wheel => PickerShape is PickerShapeType.HsvWheel or PickerShapeType.VhsCircle or PickerShapeType.OkhslCircle;

    /// <summary>The wheel's centre and outer radius, the ring's inner radius, and the square inside the ring (a wheel shape draws a hue ring with the saturation / value square in it).</summary>
    internal (Vector2 Center, float Outer, float Inner, Rect2 Square) WheelGeometry()
    {
        float d = Math.Min(SvW, SvH);
        var center = new Vector2(Margin + d / 2f, Margin + d / 2f);
        float outer = d / 2f, inner = outer - Math.Max(10f, d * 0.12f);
        float half = inner / MathF.Sqrt(2f) * 0.95f;
        return (center, outer, inner, new Rect2(center - new Vector2(half, half), new Vector2(half * 2f, half * 2f)));
    }

    /// <summary>The picker's reading of a point on the wheel: 0 outside, 1 on the ring (sets the hue), 2 inside the square (sets saturation and value).</summary>
    internal int WheelHit(Vector2 p)
    {
        var (c, outer, inner, sq) = WheelGeometry();
        if (sq.HasPoint(p)) return 2;
        float r = (p - c).Length();
        return r <= outer && r >= inner ? 1 : 0;
    }

    private void WheelPick(Vector2 p, int part)
    {
        var (c, _, _, sq) = WheelGeometry();
        if (part == 1)
        {
            Vector2 d = p - c;
            float a = MathF.Atan2(d.X, -d.Y) / MathF.Tau;   // 0 at the top, clockwise
            _h = a < 0f ? a + 1f : a;
        }
        else if (part == 2)
        {
            _s = Math.Clamp((p.X - sq.Position.X) / sq.Size.X, 0f, 1f);
            _v = 1f - Math.Clamp((p.Y - sq.Position.Y) / sq.Size.Y, 0f, 1f);
        }
    }

    private void DrawWheel()
    {
        var (c, outer, inner, sq) = WheelGeometry();
        const int steps = 72;
        for (int i = 0; i < steps; i++)
        {
            float a0 = MathF.Tau * i / steps, a1 = MathF.Tau * (i + 1) / steps;
            Vector2 d0 = new(MathF.Sin(a0), -MathF.Cos(a0)), d1 = new(MathF.Sin(a1), -MathF.Cos(a1));
            Color c0 = Color.FromHsv((float)i / steps, 1f, 1f), c1 = Color.FromHsv((float)(i + 1) / steps, 1f, 1f);
            DrawPolygon(new[] { c + d0 * inner, c + d0 * outer, c + d1 * outer, c + d1 * inner }, new[] { c0, c0, c1, c1 });
        }
        // The square: the corners carry the colours at saturation 0 / 1 and value 1 / 0, which interpolate to the field.
        Color hue = Color.FromHsv(_h, 1f, 1f);
        DrawPolygon(new[] { sq.Position, new Vector2(sq.End.X, sq.Position.Y), sq.End, new Vector2(sq.Position.X, sq.End.Y) },
                    new[] { new Color(1, 1, 1, 1), hue, new Color(0, 0, 0, 1), new Color(0, 0, 0, 1) });
        Vector2 hp = c + new Vector2(MathF.Sin(_h * MathF.Tau), -MathF.Cos(_h * MathF.Tau)) * ((inner + outer) / 2f);
        DrawRect(new Rect2(hp.X - 3, hp.Y - 3, 6, 6), new Color(1, 1, 1, 1), false, 1f);
        var sp = new Vector2(sq.Position.X + _s * sq.Size.X, sq.Position.Y + (1f - _v) * sq.Size.Y);
        DrawRect(new Rect2(sp.X - 4, sp.Y - 4, 8, 8), new Color(1, 1, 1, 1), false, 1f);
    }

    // ---- geometry ----

    private float Margin => GetThemeConstant("margin") > 0 ? GetThemeConstant("margin") : 4;
    private float SvW => GetThemeConstant("sv_width") > 0 ? GetThemeConstant("sv_width") : 256;
    private float SvH => GetThemeConstant("sv_height") > 0 ? GetThemeConstant("sv_height") : 256;
    private float HueW => GetThemeConstant("h_width") > 0 ? GetThemeConstant("h_width") : 30;

    internal Rect2 SquareRect => new(Margin, Margin, SvW, SvH);
    internal Rect2 HueRect => new(Margin * 2f + SvW, Margin, HueW, SvH);
    internal Rect2 AlphaRect => new(Margin, Margin * 2f + SvH, SvW + Margin + HueW, EditAlpha ? 20f : 0f);

    private const float Swatch = 20f;

    private Rect2 PresetRect(int i)
    {
        float width = SvW + Margin + HueW;
        int perRow = Math.Max(1, (int)((width + 2f) / (Swatch + 2f)));
        float top = AlphaRect.End.Y + (EditAlpha ? Margin : 0f);
        return new Rect2(Margin + i % perRow * (Swatch + 2f), top + i / perRow * (Swatch + 2f), Swatch, Swatch);
    }

    internal override Vector2 NativeMinimumSize()
    {
        float h = AlphaRect.End.Y + Margin;
        if (PresetsVisible && _presets.Count > 0) h = PresetRect(_presets.Count - 1).End.Y + Margin;
        return new Vector2(SvW + HueW + Margin * 3f, h);
    }

    // ---- input ----

    private void Pick(Vector2 p)
    {
        switch (_drag)
        {
            case 11:
            case 12:
                WheelPick(p, _drag - 10);
                break;
            case 1:
            {
                Rect2 r = SquareRect;
                _s = Math.Clamp((p.X - r.Position.X) / r.Size.X, 0f, 1f);
                _v = 1f - Math.Clamp((p.Y - r.Position.Y) / r.Size.Y, 0f, 1f);
                break;
            }
            case 2:
                _h = Math.Clamp((p.Y - HueRect.Position.Y) / HueRect.Size.Y, 0f, 1f);
                break;
            case 3:
                _a = Math.Clamp((p.X - AlphaRect.Position.X) / AlphaRect.Size.X, 0f, 1f);
                break;
            default: return;
        }
        Emit();
    }

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.Left:
                if (mb.Pressed)
                {
                    int wheelPart = Wheel ? WheelHit(mb.Position) : 0;
                    if (wheelPart != 0) { _drag = 10 + wheelPart; WheelPick(mb.Position, wheelPart); Emit(); AcceptEvent(); return; }
                    if (!Wheel && SquareRect.HasPoint(mb.Position)) _drag = 1;   // a wheel has no square or hue bar of this kind
                    else if (!Wheel && HueRect.HasPoint(mb.Position)) _drag = 2;
                    else if (EditAlpha && AlphaRect.HasPoint(mb.Position)) _drag = 3;
                    else
                    {
                        for (int i = 0; i < _presets.Count && PresetsVisible; i++)
                            if (PresetRect(i).HasPoint(mb.Position))
                            {
                                Color c = _presets[i];
                                _h = c.S > 0.0001f ? c.H : _h;
                                _s = c.S; _v = c.V; _a = c.A;
                                Emit();
                                AcceptEvent();
                                return;
                            }
                        break;
                    }
                    Pick(mb.Position);
                }
                else _drag = 0;
                AcceptEvent();
                break;
            case InputEventMouseMotion mm when _drag != 0:
                Pick(mm.Position);
                AcceptEvent();
                break;
        }
    }

    // ---- drawing ----

    public override void _Draw()
    {
        if (Wheel) { DrawWheel(); DrawRest(); return; }
        Rect2 sq = SquareRect;
        const int cells = 16;
        float cw = sq.Size.X / cells, ch = sq.Size.Y / cells;
        for (int y = 0; y < cells; y++)
            for (int x = 0; x < cells; x++)
                DrawRect(new Rect2(sq.Position.X + x * cw, sq.Position.Y + y * ch, cw + 0.5f, ch + 0.5f), Color.FromHsv(_h, (x + 0.5f) / cells, 1f - (y + 0.5f) / cells));
        var cross = new Vector2(sq.Position.X + _s * sq.Size.X, sq.Position.Y + (1f - _v) * sq.Size.Y);
        DrawRect(new Rect2(cross.X - 4, cross.Y - 4, 8, 8), new Color(1, 1, 1, 1), false, 1f);
        Rect2 hr = HueRect;
        const int bands = 32;
        for (int i = 0; i < bands; i++)
            DrawRect(new Rect2(hr.Position.X, hr.Position.Y + i * hr.Size.Y / bands, hr.Size.X, hr.Size.Y / bands + 0.5f), Color.FromHsv((i + 0.5f) / bands, 1f, 1f));
        DrawRect(new Rect2(hr.Position.X - 2, hr.Position.Y + _h * hr.Size.Y - 1, hr.Size.X + 4, 3), new Color(1, 1, 1, 1));
        DrawRest();
    }

    // The alpha bar and the presets, which every shape has.
    private void DrawRest()
    {
        if (EditAlpha)
        {
            Rect2 ar = AlphaRect;
            DrawRect(ar, new Color(0.3f, 0.3f, 0.3f, 1));
            for (int i = 0; i < 32; i++)
                DrawRect(new Rect2(ar.Position.X + i * ar.Size.X / 32f, ar.Position.Y, ar.Size.X / 32f + 0.5f, ar.Size.Y), Color.FromHsv(_h, _s, _v, (i + 0.5f) / 32f));
            DrawRect(new Rect2(ar.Position.X + _a * ar.Size.X - 1, ar.Position.Y - 2, 3, ar.Size.Y + 4), new Color(1, 1, 1, 1));
        }
        if (PresetsVisible)
            for (int i = 0; i < _presets.Count; i++)
            {
                Rect2 pr = PresetRect(i);
                DrawRect(pr, new Color(0.3f, 0.3f, 0.3f, 1));
                DrawRect(pr, _presets[i]);
            }
    }
}

/// <summary>A button that shows a colour and opens a picker (Godot's ColorPickerButton).</summary>
public class ColorPickerButton : Button
{
    public new class SignalName : Button.SignalName
    {
        public static readonly StringName ColorChanged = "color_changed";
        public static readonly StringName PopupClosed = "popup_closed";
        public static readonly StringName PickerCreated = "picker_created";
    }

    public event Action<Color> ColorChanged { add => Connect(SignalName.ColorChanged, Callable.From(value)); remove => Disconnect(SignalName.ColorChanged, Callable.From(value)); }
    public event Action PopupClosed { add => Connect(SignalName.PopupClosed, Callable.From(value)); remove => Disconnect(SignalName.PopupClosed, Callable.From(value)); }

    private readonly ColorPicker _picker = new();
    private readonly PopupPanel _popup = new() { Name = "PopupPanel" };
    private Color _color = new(0, 0, 0, 1);

    public ColorPickerButton()
    {
        _popup.AddChild(_picker);
        _picker.ColorChanged += c => { _color = c; QueueRedraw(); EmitSignal(SignalName.ColorChanged, c); };
        _popup.PopupHide += () => { EmitSignal(SignalName.PopupClosed); };
        Pressed += ShowPicker;
        Alignment = HorizontalAlignment.Center;
    }

    public Color Color
    {
        get => _color;
        set
        {
            _color = value;
            _picker.Color = value;
            QueueRedraw();
        }
    }

    public bool EditAlpha { get => _picker.EditAlpha; set => _picker.EditAlpha = value; }

    public bool EditIntensity { get => _picker.EditIntensity; set => _picker.EditIntensity = value; }

    public ColorPicker GetPicker() => _picker;

    public PopupPanel GetPopup() => _popup;

    public void ShowPicker()
    {
        if (_popup.GetParent() == null) SceneTree.Instance.Root.AddChild(_popup);
        Vector2 at = GetGlobalTransform().Origin + new Vector2(0, Size.Y);
        Vector2 size = _picker.GetCombinedMinimumSize() + _popup.GetThemeStylebox("panel").GetMinimumSize();
        _popup.Popup(new Rect2I((int)at.X, (int)at.Y, (int)size.X, (int)size.Y));
    }

    internal override Vector2 NativeMinimumSize()
    {
        Vector2 b = base.NativeMinimumSize();
        return new Vector2(MathF.Max(b.X, 32f), b.Y);
    }

    public override void _Draw()
    {
        base._Draw();
        Rect2 content = StyleFor(GetDrawMode()).GetContentRect(LocalRect);
        var swatch = content.Grow(-1f);
        DrawRect(swatch, new Color(0.3f, 0.3f, 0.3f, 1));
        DrawRect(swatch, _color);
    }
}
