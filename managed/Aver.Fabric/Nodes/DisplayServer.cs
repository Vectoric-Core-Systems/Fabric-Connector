// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Godot's DisplayServer and the window controls of Window, over the engine's window, pointer and present requests (Aver.Framework's DisplayWindow, Pointer
// and Display). The shipped game applies the requests once a frame and publishes the window's real state; the editor owns its window and ignores them, and
// with no engine (tests, tools) the calls keep what was set so a script reads it back. Aver has one window and one monitor.

using Aver.Framework;

namespace Aver.Fabric;

/// <summary>Godot's DisplayServer singleton: the window, the screen, the pointer's shape, vsync and the touch screen, for the one window Aver has.</summary>
public static class DisplayServer
{
    public const int MainWindowId = 0;
    public const int InvalidWindowId = -1;
    public const int ScreenOfMainWindow = -1;
    public const int ScreenPrimary = -2;

    public enum WindowMode { Windowed = 0, Minimized = 1, Maximized = 2, Fullscreen = 3, ExclusiveFullscreen = 4 }

    public enum WindowFlags
    {
        ResizeDisabled = 0, Borderless = 1, AlwaysOnTop = 2, Transparent = 3, NoFocus = 4, Popup = 5, ExtendToTitle = 6, MousePassthrough = 7,
        SharpCorners = 8, ExcludeFromCapture = 9, PopupWmHint = 10, MinimizeDisabled = 11, MaximizeDisabled = 12,
    }

    public enum VSyncMode { Disabled = 0, Enabled = 1, Adaptive = 2, Mailbox = 3 }

    public enum CursorShape { Arrow = 0, Ibeam = 1, PointingHand = 2, Cross = 3, Wait = 4, Busy = 5, Drag = 6, CanDrop = 7, Forbidden = 8, Vsize = 9, Hsize = 10, Bdiagsize = 11, Fdiagsize = 12, Move = 13, Vsplit = 14, Hsplit = 15, Help = 16 }

    /// <summary>The display server's name: Aver draws and reads input through its own window layer.</summary>
    public static string GetName() => "Aver";

    // The clipboard the text controls share (UiClipboard).
    public static void ClipboardSet(string clipboard) => UiClipboard.Text = clipboard;
    public static string ClipboardGet() => UiClipboard.Text;
    public static bool ClipboardHas() => UiClipboard.Text.Length > 0;

    // Keyboard layouts: the engine reports keys by their US-layout code (Key), so there is one layout and a physical key is its own key.
    public static int KeyboardGetLayoutCount() => 1;
    public static int KeyboardGetCurrentLayout() => 0;
    public static void KeyboardSetCurrentLayout(int index) { }
    public static string KeyboardGetLayoutLanguage(int index) => "en";
    public static string KeyboardGetLayoutName(int index) => "English (US)";
    public static Key KeyboardGetKeycodeFromPhysical(Key keycode) => keycode;
    public static Key KeyboardGetLabelFromPhysical(Key keycode) => keycode;

    /// <summary>Whether the device has a touch screen: the window layer has reported a finger (SDL), or touch emulation from the mouse is on.</summary>
    public static bool IsTouchscreenAvailable()
    {
        if (!NodeHost.Current.IsEngine) return false;
        try { return Aver.Framework.Touch.Available || Aver.Framework.Touch.Emulation; }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    // ---- windows: one ----

    public static int[] GetWindowList() => new[] { MainWindowId };

    public static int GetScreenCount() => 1;

    public static int GetPrimaryScreen() => 0;

    public static int WindowGetCurrentScreen(int windowId = MainWindowId) => 0;

    public static void WindowSetCurrentScreen(int screen, int windowId = MainWindowId) { }

    public static WindowMode WindowGetMode(int windowId = MainWindowId) => WindowControl.GetMode() switch
    {
        Window.ModeEnum.Minimized => WindowMode.Minimized,
        Window.ModeEnum.Maximized => WindowMode.Maximized,
        Window.ModeEnum.Fullscreen => WindowMode.Fullscreen,
        Window.ModeEnum.ExclusiveFullscreen => WindowMode.ExclusiveFullscreen,
        _ => WindowMode.Windowed,
    };

    /// <summary>Sets the window's mode. Exclusive fullscreen is borderless fullscreen here (approximated): Aver never takes over the display mode, which would
    /// break alt-tab; the mode reads back as the one asked for, and the first use says so in the log.</summary>
    public static void WindowSetMode(WindowMode mode, int windowId = MainWindowId) => WindowControl.SetMode((Window.ModeEnum)(int)mode);

    /// <summary>The client area in pixels.</summary>
    public static Vector2I WindowGetSize(int windowId = MainWindowId) => WindowControl.GetSize();

    /// <summary>The window with its frame: the client area, as Aver does not report the frame's size.</summary>
    public static Vector2I WindowGetSizeWithDecorations(int windowId = MainWindowId) => WindowControl.GetSize();

    public static void WindowSetSize(Vector2I size, int windowId = MainWindowId) => WindowControl.SetSize(size);

    public static Vector2I WindowGetPosition(int windowId = MainWindowId) => WindowControl.GetPosition();

    public static Vector2I WindowGetPositionWithDecorations(int windowId = MainWindowId) => WindowControl.GetPosition();

    public static void WindowSetPosition(Vector2I position, int windowId = MainWindowId) => WindowControl.SetPosition(position);

    public static bool WindowGetFlag(WindowFlags flag, int windowId = MainWindowId) => WindowControl.GetFlag(flag);

    /// <summary>Resizable (the inverse of ResizeDisabled), Borderless and AlwaysOnTop reach the window; the other flags are kept as set (approximated).</summary>
    public static void WindowSetFlag(WindowFlags flag, bool enabled, int windowId = MainWindowId) => WindowControl.SetFlag(flag, enabled);

    public static void WindowSetTitle(string title, int windowId = MainWindowId) => WindowControl.SetTitle(title);

    public static bool WindowIsFocused(int windowId = MainWindowId) => WindowControl.Focused;

    public static void WindowMoveToForeground(int windowId = MainWindowId) { }

    public static void WindowRequestAttention(int windowId = MainWindowId) { }

    private static Vector2I s_minSize, s_maxSize;

    /// <summary>Kept as set (approximated): the engine's window has no size limits.</summary>
    public static void WindowSetMinSize(Vector2I size, int windowId = MainWindowId) => s_minSize = size;
    public static Vector2I WindowGetMinSize(int windowId = MainWindowId) => s_minSize;
    public static void WindowSetMaxSize(Vector2I size, int windowId = MainWindowId) => s_maxSize = size;
    public static Vector2I WindowGetMaxSize(int windowId = MainWindowId) => s_maxSize;

    // ---- vsync ----

    public static void WindowSetVsyncMode(VSyncMode mode, int windowId = MainWindowId) => WindowControl.SetVSync(mode);

    /// <summary>The vsync mode in force: what the engine published once the shipped game has applied a request, else what was last set (Enabled by default).
    /// A mode the display path lacks reads back as the one it fell to.</summary>
    public static VSyncMode WindowGetVsyncMode(int windowId = MainWindowId) => WindowControl.GetVSync();

    // ---- screens: one ----

    private static Vector2I MonitorOr(Vector2I fallback)
    {
        (int w, int h) = WindowControl.MonitorSize();
        return w > 0 && h > 0 ? new Vector2I(w, h) : fallback;
    }

    /// <summary>The monitor's size in pixels (the window's own size when the engine cannot tell).</summary>
    public static Vector2I ScreenGetSize(int screen = ScreenOfMainWindow) => MonitorOr(WindowControl.GetSize());

    public static Vector2I ScreenGetPosition(int screen = ScreenOfMainWindow) => Vector2I.Zero;

    public static Rect2I ScreenGetUsableRect(int screen = ScreenOfMainWindow) => new(Vector2I.Zero, ScreenGetSize(screen));

    /// <summary>Dots per inch: 96 times the display scale.</summary>
    public static int ScreenGetDpi(int screen = ScreenOfMainWindow) => (int)MathF.Round(96f * ScreenGetScale(screen));

    public static float ScreenGetScale(int screen = ScreenOfMainWindow) => WindowControl.DpiScale();

    /// <summary>Approximated: the engine does not read the refresh rate; -1, as Godot answers when it cannot tell.</summary>
    public static float ScreenGetRefreshRate(int screen = ScreenOfMainWindow) => -1f;

    private static bool s_keepOn;

    /// <summary>Kept as set (approximated): the engine does not manage the screen saver.</summary>
    public static void ScreenSetKeepOn(bool enable) => s_keepOn = enable;
    public static bool ScreenIsKeptOn() => s_keepOn;

    // ---- the pointer ----

    /// <summary>The pointer in screen pixels: the window's position plus the pointer in the window.</summary>
    public static Vector2I MouseGetPosition()
    {
        Vector2 p = Input.MousePosition;
        Vector2I w = WindowControl.GetPosition();
        return new Vector2I(w.X + (int)p.X, w.Y + (int)p.Y);
    }

    public static MouseButtonMask MouseGetButtonState() => Input.GetMouseButtonMask();

    /// <summary>Moves the pointer to a screen position.</summary>
    public static void WarpMouse(Vector2I position)
    {
        Vector2I w = WindowControl.GetPosition();
        Input.WarpMouse(new Vector2(position.X - w.X, position.Y - w.Y));
    }

    public static void CursorSetShape(CursorShape shape) => Input.SetDefaultCursorShape((Input.CursorShape)(int)shape);

    public static CursorShape CursorGetShape() => (CursorShape)(int)Input.GetCurrentCursorShape();

    /// <summary>Replaces a shape with an image (see <see cref="Input.SetCustomMouseCursor"/> for what is approximated).</summary>
    public static void CursorSetCustomImage(Resource? cursor, CursorShape shape = CursorShape.Arrow, Vector2 hotspot = default) =>
        Input.SetCustomMouseCursor(cursor, (Input.CursorShape)(int)shape, hotspot);
}

/// <summary>What the window, DisplayServer and Window share: the engine's window requests and published state, and the values a script set (the answer
/// where the engine has not published one yet).</summary>
internal static class WindowControl
{
    private static Window.ModeEnum s_mode = Window.ModeEnum.Windowed;
    private static Vector2I? s_size, s_position;
    private static string s_title = "";
    private static DisplayServer.VSyncMode s_vsync = DisplayServer.VSyncMode.Enabled;
    private static bool s_borderless, s_alwaysOnTop, s_unresizable, s_transparent, s_noFocus;
    private static bool s_exclusiveLogged;

    internal static void ResetForTests()
    {
        s_mode = Window.ModeEnum.Windowed;
        s_size = s_position = null;
        s_title = "";
        s_vsync = DisplayServer.VSyncMode.Enabled;
        s_borderless = s_alwaysOnTop = s_unresizable = s_transparent = s_noFocus = false;
    }

    private static bool Engine => NodeHost.Current.IsEngine;

    private static T Try<T>(Func<T> read, T fallback)
    {
        try { return read(); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return fallback; }
    }

    private static void Do(Action act)
    {
        if (!Engine) return;
        try { act(); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    private readonly record struct WindowState(WindowMode Mode, int W, int H, int X, int Y, Aver.Framework.WindowFlags Flags);

    // The window state the shipped game last published (a width of 0 means it has not published yet, or this is the editor).
    private static bool Published(out WindowState s)
    {
        s = default;
        if (!Engine) return false;
        try
        {
            (int width, int height) = DisplayWindow.Size;
            if (width <= 0 || height <= 0) return false;
            (int x, int y) = DisplayWindow.Position;
            s = new WindowState(DisplayWindow.Mode, width, height, x, y, DisplayWindow.Flags);
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    internal static Window.ModeEnum GetMode()
    {
        // An exclusive-fullscreen request reads back as exclusive, though the window is only borderless fullscreen.
        if (Published(out WindowState s) && !(s_mode == Window.ModeEnum.ExclusiveFullscreen && s.Mode == WindowMode.Fullscreen))
            return (Window.ModeEnum)(int)s.Mode;
        return s_mode;
    }

    internal static void SetMode(Window.ModeEnum mode)
    {
        s_mode = mode;
        if (mode == Window.ModeEnum.ExclusiveFullscreen && !s_exclusiveLogged)
        {
            s_exclusiveLogged = true;
            Aver.Scripting.Log.Info("[Fabric] exclusive fullscreen is borderless fullscreen here: Aver never takes over the display mode (it breaks alt-tab)");
        }
        Do(() => DisplayWindow.Mode = mode == Window.ModeEnum.ExclusiveFullscreen ? WindowMode.Fullscreen : (WindowMode)(int)mode);
    }

    internal static Vector2I GetSize()
    {
        if (Published(out WindowState s)) return new Vector2I(s.W, s.H);
        return s_size ?? ProjectSize();
    }

    internal static void SetSize(Vector2I size)
    {
        s_size = size;
        Do(() => DisplayWindow.SetSize(size.X, size.Y));
    }

    private static Vector2I ProjectSize()
    {
        try
        {
            string[] parts = Aver.Framework.ProjectSettings.Get("WINDOW.SIZE", "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h) && w > 0 && h > 0) return new Vector2I(w, h);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        return new Vector2I(1280, 720);
    }

    internal static Vector2I GetPosition()
    {
        if (Published(out WindowState s)) return new Vector2I(s.X, s.Y);
        return s_position ?? Vector2I.Zero;
    }

    internal static void SetPosition(Vector2I position)
    {
        s_position = position;
        Do(() => DisplayWindow.SetPosition(position.X, position.Y));
    }

    internal static bool GetFlag(DisplayServer.WindowFlags flag)
    {
        if (Published(out WindowState s))
        {
            if (flag == DisplayServer.WindowFlags.Borderless) return (s.Flags & Aver.Framework.WindowFlags.Borderless) != 0;
            if (flag == DisplayServer.WindowFlags.AlwaysOnTop) return (s.Flags & Aver.Framework.WindowFlags.AlwaysOnTop) != 0;
            if (flag == DisplayServer.WindowFlags.ResizeDisabled) return (s.Flags & Aver.Framework.WindowFlags.Resizable) == 0;
        }
        return flag switch
        {
            DisplayServer.WindowFlags.Borderless => s_borderless,
            DisplayServer.WindowFlags.AlwaysOnTop => s_alwaysOnTop,
            DisplayServer.WindowFlags.ResizeDisabled => s_unresizable,
            DisplayServer.WindowFlags.Transparent => s_transparent,
            DisplayServer.WindowFlags.NoFocus => s_noFocus,
            _ => false,
        };
    }

    internal static void SetFlag(DisplayServer.WindowFlags flag, bool on)
    {
        switch (flag)
        {
            case DisplayServer.WindowFlags.Borderless: s_borderless = on; Do(() => DisplayWindow.SetFlag(Aver.Framework.WindowFlags.Borderless, on)); break;
            case DisplayServer.WindowFlags.AlwaysOnTop: s_alwaysOnTop = on; Do(() => DisplayWindow.SetFlag(Aver.Framework.WindowFlags.AlwaysOnTop, on)); break;
            case DisplayServer.WindowFlags.ResizeDisabled: s_unresizable = on; Do(() => DisplayWindow.SetFlag(Aver.Framework.WindowFlags.Resizable, !on)); break;
            case DisplayServer.WindowFlags.Transparent: s_transparent = on; break;
            case DisplayServer.WindowFlags.NoFocus: s_noFocus = on; break;
        }
    }

    internal static string GetTitle() => s_title;

    internal static void SetTitle(string title)
    {
        s_title = title ?? "";
        Do(() => DisplayWindow.SetTitle(s_title));
    }

    internal static bool Focused => Engine ? Try(() => DisplayWindow.Focused, true) : true;

    internal static (int W, int H) MonitorSize()
    {
        if (!Engine) return (0, 0);
        try { (int w, int h) = DisplayWindow.MonitorSize; return (w, h); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return (0, 0); }
    }

    internal static float DpiScale() => Engine ? Try(() => { float d = DisplayWindow.DpiScale; return d > 0f ? d : 1f; }, 1f) : 1f;

    internal static void SetVSync(DisplayServer.VSyncMode mode)
    {
        s_vsync = mode;
        // Godot's Disabled is Aver's Immediate and Enabled its Fifo; Adaptive and Mailbox are the same words.
        Do(() => Display.PresentMode = mode switch
        {
            DisplayServer.VSyncMode.Disabled => PresentMode.Immediate,
            DisplayServer.VSyncMode.Adaptive => PresentMode.Adaptive,
            DisplayServer.VSyncMode.Mailbox => PresentMode.Mailbox,
            _ => PresentMode.Fifo,
        });
    }

    internal static DisplayServer.VSyncMode GetVSync()
    {
        if (Engine && Try(() => Display.PresentMode, (PresentMode?)null) is { } p)
            return p switch
            {
                PresentMode.Immediate => DisplayServer.VSyncMode.Disabled,
                PresentMode.Adaptive => DisplayServer.VSyncMode.Adaptive,
                PresentMode.Mailbox => DisplayServer.VSyncMode.Mailbox,
                _ => DisplayServer.VSyncMode.Enabled,
            };
        return s_vsync;
    }
}
