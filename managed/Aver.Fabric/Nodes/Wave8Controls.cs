// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Controls that are compositions of what exists (parity wave 8a, F52): ReferenceRect, FoldableContainer with its FoldableGroup, and MenuBar. Each is Godot's
// behaviour on the engine's draw list: the look comes from the theme and the style boxes already there, and the layout is the container machinery.

namespace Aver.Fabric;

/// <summary>A rectangle outline for reference while building a layout (Godot's ReferenceRect). It never takes the pointer.</summary>
public class ReferenceRect : Control
{
    public Color BorderColor { get; set; } = new(1, 0, 0, 1);
    public float BorderWidth { get; set; } = 1f;
    /// <summary>Godot draws it in the editor only by default; at run time it is invisible unless this is on.</summary>
    public bool EditorOnly { get; set; } = true;

    public ReferenceRect() { MouseFilter = MouseFilterEnum.Ignore; }

    public override void _Draw()
    {
        if (EditorOnly) return;
        DrawRect(new Rect2(Vector2.Zero, Size), BorderColor, false, BorderWidth);
    }
}

/// <summary>A set of FoldableContainers of which at most one is open (Godot's FoldableGroup).</summary>
public class FoldableGroup : Resource
{
    private readonly List<FoldableContainer> _members = new();

    public new class SignalName : Resource.SignalName
    {
        public static readonly StringName Expanded = "expanded";
    }

    public event Action<FoldableContainer> Expanded { add => Connect(SignalName.Expanded, Callable.From(value)); remove => Disconnect(SignalName.Expanded, Callable.From(value)); }

    /// <summary>When on, the open container can be folded by clicking its title (Godot's allow_folding_all).</summary>
    public bool AllowFoldingAll { get; set; }

    public FoldableContainer? GetExpandedContainer() => _members.FirstOrDefault(m => !m.Folded);

    public Collections.Array GetContainers()
    {
        var a = new Collections.Array();
        foreach (FoldableContainer m in _members) a.Add(Variant.CreateFrom(m));
        return a;
    }

    internal void Add(FoldableContainer c) { if (!_members.Contains(c)) _members.Add(c); }
    internal void Remove(FoldableContainer c) => _members.Remove(c);

    /// <summary>A container opened: the others in the group close.</summary>
    internal void Opened(FoldableContainer open)
    {
        foreach (FoldableContainer m in _members.ToArray()) if (m != open && !m.Folded) m.SetFoldedQuiet(true);
        EmitSignal(SignalName.Expanded, Variant.CreateFrom(open));
    }
}

/// <summary>A container with a title bar that folds its content away (Godot's FoldableContainer, 4.5).</summary>
public class FoldableContainer : Container
{
    public new class SignalName : Container.SignalName
    {
        public static readonly StringName FoldingChanged = "folding_changed";
    }

    public event Action<bool> FoldingChanged { add => Connect(SignalName.FoldingChanged, Callable.From(value)); remove => Disconnect(SignalName.FoldingChanged, Callable.From(value)); }

    public enum TitlePosition { Top = 0, Bottom = 1 }

    private bool _folded;
    private string _title = "";
    private FoldableGroup? _group;
    private readonly HashSet<Control> _hiddenByFold = new();

    public FoldableContainer()
    {
        MouseFilter = MouseFilterEnum.Stop;
        Connect(Node.SignalName.ChildOrderChanged, Callable.From(ApplyFold));
    }

    public string Title { get => _title; set { _title = value ?? ""; UpdateMinimumSize(); QueueRedraw(); } }
    public HorizontalAlignment TitleAlignment { get; set; } = HorizontalAlignment.Left;
    public TextServer.OverrunBehavior TitleTextOverrunBehavior { get; set; } = TextServer.OverrunBehavior.NoTrimming;
    public TitlePosition TitlePositionValue { get; set; } = TitlePosition.Top;
    public string Language { get; set; } = "";

    public bool Folded
    {
        get => _folded;
        set => SetFolded(value);
    }

    public FoldableGroup? FoldableGroup
    {
        get => _group;
        set
        {
            _group?.Remove(this);
            _group = value;
            if (_group != null)
            {
                _group.Add(this);
                // A group has one open container: joining open while another is open closes this one (Godot keeps the first).
                if (!_folded && _group.GetExpandedContainer() is { } other && other != this) SetFoldedQuiet(true);
            }
        }
    }

    internal void SetFoldedQuiet(bool folded)
    {
        if (_folded == folded) return;
        _folded = folded;
        ApplyFold();
        UpdateMinimumSize();
        QueueSort();
        QueueRedraw();
        EmitSignal(SignalName.FoldingChanged, folded);
    }

    private void SetFolded(bool folded)
    {
        if (_folded == folded) return;
        // A group keeps one container open: folding the last open one is refused unless the group allows it.
        if (folded && _group != null && !_group.AllowFoldingAll && ReferenceEquals(_group.GetExpandedContainer(), this)) return;
        SetFoldedQuiet(folded);
        if (!folded) _group?.Opened(this);
    }

    public void Fold() => Folded = true;
    public void Expand() => Folded = false;

    // The content of a folded container is hidden (and shown again when it unfolds) without touching what a script set: only what this hid is restored.
    private void ApplyFold()
    {
        if (_folded)
        {
            foreach (Node n in ChildrenSnapshot())
                if (n is Control c && c.Visible) { _hiddenByFold.Add(c); c.Visible = false; }
        }
        else
        {
            foreach (Control c in _hiddenByFold) if (c.IsInsideTree() || c.GetParent() == this) c.Visible = true;
            _hiddenByFold.Clear();
        }
    }

    private float TitleHeight()
    {
        Font? font = GetThemeFont("font");
        int px = Math.Max(1, GetThemeFontSize("font_size"));
        return UiText.LineHeight(font, px) + 8f;
    }

    internal override Vector2 NativeMinimumSize()
    {
        Font? font = GetThemeFont("font");
        int px = Math.Max(1, GetThemeFontSize("font_size"));
        var ms = new Vector2(UiText.Width(font, px, _title) + 24f, TitleHeight());
        if (_folded) return ms;
        foreach (Control c in LaidOutChildren())
        {
            Vector2 s = c.GetCombinedMinimumSize();
            ms = new Vector2(Math.Max(ms.X, s.X), ms.Y + s.Y);
        }
        return ms;
    }

    internal override void SortChildren()
    {
        if (_folded) return;
        float th = TitleHeight();
        float y = TitlePositionValue == TitlePosition.Top ? th : 0f;
        foreach (Control c in LaidOutChildren()) FitChildInRect(c, new Rect2(0, y, Size.X, Math.Max(0f, Size.Y - th)));
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } b)
        {
            float th = TitleHeight();
            bool onTitle = TitlePositionValue == TitlePosition.Top ? b.Position.Y < th : b.Position.Y > Size.Y - th;
            if (onTitle)
            {
                Folded = !_folded;
                AcceptEvent();
            }
        }
    }

    public override void _Draw()
    {
        float th = TitleHeight();
        Font? font = GetThemeFont("font");
        int px = Math.Max(1, GetThemeFontSize("font_size"));
        Color color = GetThemeColor("font_color");
        float top = TitlePositionValue == TitlePosition.Top ? 0f : Size.Y - th;
        DrawStyleBox(GetThemeStylebox(_folded ? "title_collapsed_panel" : "title_panel"), new Rect2(0, top, Size.X, th));
        if (!_folded) DrawStyleBox(GetThemeStylebox("panel"), new Rect2(0, TitlePositionValue == TitlePosition.Top ? th : 0f, Size.X, Math.Max(0f, Size.Y - th)));
        // The arrow: a small triangle that points right when folded and down when open.
        var c = new Vector2(10f, top + th / 2f);
        DrawColoredPolygon(_folded ? new[] { c + new Vector2(-3, -4), c + new Vector2(3, 0), c + new Vector2(-3, 4) } : new[] { c + new Vector2(-4, -3), c + new Vector2(4, -3), c + new Vector2(0, 3) }, color);
        float w = UiText.Width(font, px, _title);
        float x = TitleAlignment switch { HorizontalAlignment.Center => (Size.X - w) / 2f, HorizontalAlignment.Right => Size.X - w - 8f, _ => 20f };
        DrawTextLine(new Vector2(x, top + (th - UiText.LineHeight(font, px)) / 2f), _title, font, px, color);
    }
}

/// <summary>A row of menus along the top of a window (Godot's MenuBar). Each child PopupMenu is a menu; its name (or <see cref="SetMenuTitle"/>) is the title shown.</summary>
public class MenuBar : Control
{
    private readonly Dictionary<int, string> _titles = new();
    private readonly Dictionary<int, string> _tooltips = new();
    private readonly HashSet<int> _hidden = new(), _disabled = new();
    private int _open = -1;

    public bool Flat { get; set; }
    public int StartIndex { get; set; } = -1;
    public bool SwitchOnHover { get; set; } = true;
    public bool PreferGlobalMenu { get; set; } = true;
    public string Language { get; set; } = "";

    public MenuBar() { MouseFilter = MouseFilterEnum.Stop; }

    private List<PopupMenu> Menus()
    {
        var l = new List<PopupMenu>();
        foreach (Node n in ChildrenSnapshot()) if (n is PopupMenu p) l.Add(p);
        return l;
    }

    public int GetMenuCount() => Menus().Count;
    public void SetMenuTitle(int menu, string title) { _titles[menu] = title; QueueRedraw(); }
    public string GetMenuTitle(int menu) => _titles.TryGetValue(menu, out string? t) ? t : menu >= 0 && menu < Menus().Count ? Menus()[menu].Name.ToString() : "";
    public void SetMenuTooltip(int menu, string tooltip) => _tooltips[menu] = tooltip;
    public string GetMenuTooltip(int menu) => _tooltips.TryGetValue(menu, out string? t) ? t : "";
    public void SetMenuDisabled(int menu, bool disabled) { if (disabled) _disabled.Add(menu); else _disabled.Remove(menu); QueueRedraw(); }
    public bool IsMenuDisabled(int menu) => _disabled.Contains(menu);
    public void SetMenuHidden(int menu, bool hidden) { if (hidden) _hidden.Add(menu); else _hidden.Remove(menu); UpdateMinimumSize(); QueueRedraw(); }
    public bool IsMenuHidden(int menu) => _hidden.Contains(menu);
    public PopupMenu? GetMenuPopup(int menu) => menu >= 0 && menu < Menus().Count ? Menus()[menu] : null;
    public bool IsNativeMenu() => false;

    private const float Gap = 4f;

    private float TitleWidth(int menu) => UiText.Width(GetThemeFont("font"), Math.Max(1, GetThemeFontSize("font_size")), GetMenuTitle(menu)) + 16f;

    /// <summary>The rectangle of a menu's title in the bar's own space, empty for a hidden menu.</summary>
    public Rect2 GetMenuRect(int menu)
    {
        float x = 0f;
        for (int i = 0; i < Menus().Count; i++)
        {
            if (_hidden.Contains(i)) continue;
            float w = TitleWidth(i);
            if (i == menu) return new Rect2(x, 0, w, Size.Y);
            x += w + Gap;
        }
        return new Rect2(0, 0, 0, 0);
    }

    /// <summary>The menu whose title is at the point, or -1.</summary>
    public int MenuAt(Vector2 point)
    {
        for (int i = 0; i < Menus().Count; i++)
            if (!_hidden.Contains(i) && GetMenuRect(i).HasPoint(point)) return i;
        return -1;
    }

    internal override Vector2 NativeMinimumSize()
    {
        float w = 0f;
        for (int i = 0; i < Menus().Count; i++) if (!_hidden.Contains(i)) w += TitleWidth(i) + Gap;
        Font? font = GetThemeFont("font");
        return new Vector2(w, UiText.LineHeight(font, Math.Max(1, GetThemeFontSize("font_size"))) + 8f);
    }

    private void Open(int menu)
    {
        if (menu < 0 || _disabled.Contains(menu)) return;
        PopupMenu? p = GetMenuPopup(menu);
        if (p == null) return;
        if (_open >= 0 && _open != menu) GetMenuPopup(_open)?.Hide();
        _open = menu;
        Rect2 r = GetMenuRect(menu);
        Vector2 at = GetGlobalTransform().Origin + new Vector2(r.Position.X, Size.Y);
        Vector2 size = p.ContentSize();
        p.Popup(new Rect2I((int)at.X, (int)at.Y, (int)MathF.Max(size.X, r.Size.X), (int)size.Y));
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } b:
            {
                int m = MenuAt(b.Position);
                if (m >= 0) { Open(m); AcceptEvent(); }
                break;
            }
            case InputEventMouseMotion mm when SwitchOnHover && _open >= 0:
            {
                int m = MenuAt(mm.Position);
                if (m >= 0 && m != _open) Open(m);
                break;
            }
        }
    }

    public override void _Draw()
    {
        Font? font = GetThemeFont("font");
        int px = Math.Max(1, GetThemeFontSize("font_size"));
        for (int i = 0; i < Menus().Count; i++)
        {
            if (_hidden.Contains(i)) continue;
            Rect2 r = GetMenuRect(i);
            if (!Flat) DrawStyleBox(GetThemeStylebox(i == _open ? "hover" : "normal"), r);
            Color c = _disabled.Contains(i) ? GetThemeColor("font_disabled_color") : GetThemeColor("font_color");
            DrawTextLine(new Vector2(r.Position.X + 8f, (r.Size.Y - UiText.LineHeight(font, px)) / 2f), GetMenuTitle(i), font, px, c, ThemeTextFx());
        }
    }
}
