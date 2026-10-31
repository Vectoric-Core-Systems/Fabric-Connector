// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// DisplayTests: DisplayServer and Window's controls, vsync modes, the frame cap and the viewport's size and signal. With no engine the calls keep what was
// set (the window reads back what a script asked), which is what is checked here; the engine side (a request becoming a window change) is the shipped
// game's and is on the owner's headed list.

using Aver.Fabric;

static class DisplayTests
{
    public static int RunAll() => T.Suite(() =>
    {
        T.Run("DisplayServer: window mode, size, position, title and flags read back", Window_);
        T.Run("DisplayServer: exclusive fullscreen reads back as asked, vsync modes and the screen", ModesAndScreen);
        T.Run("Window: mode, flags, title and position are the same window as DisplayServer's", WindowClass);
        T.Run("Viewport: size_changed when the size changes, the pointer in content space", SizeSignal);
        T.Run("Engine.max_fps is kept", FrameCap);
        T.Run("XR: the nodes load as inert carriers and XRServer finds no interface", Xr);
    });

    private static void Xr()
    {
        T.Check("XRServer finds no interface", XRServer.FindInterface("OpenXR") == null && XRServer.GetInterfaceCount() == 0 && XRServer.PrimaryInterface == null && XRServer.GetInterfaces().Count == 0);
        T.Check("the node classes exist and derive from Godot's bases", new XROrigin3D() is Node3D && new XRCamera3D() is Camera3D && new XRController3D() is XRNode3D && new XRAnchor3D() is XRNode3D && new XRNode3D() is Node3D);
        T.Check("ClassDB can make them (a scene names them by class)", ClassDB.CanInstantiate("XROrigin3D") && ClassDB.CanInstantiate("XRCamera3D") && ClassDB.CanInstantiate("XRController3D") && ClassDB.CanInstantiate("XRAnchor3D"));
        var hand = new XRController3D { Tracker = "left_hand" };
        T.Check("a controller reads nothing, is not tracked, keeps its tracker name", !hand.IsButtonPressed("trigger_click") && hand.GetFloat("trigger") == 0f && hand.GetVector2("primary") == Vector2.Zero &&
                                                                                       !hand.GetIsActive() && !hand.GetHasTrackingData() && hand.Tracker == "left_hand");
        var origin = new XROrigin3D { WorldScale = 2f };
        T.Check("the origin keeps its world scale", origin.WorldScale == 2f && origin.Current);
        XRServer.WorldScale = 3f;
        T.Check("XRServer keeps its world scale", XRServer.WorldScale == 3f);
        XRServer.WorldScale = 1f;
    }

    private static void Window_()
    {
        SceneTree.Reset();
        T.Check("the main window is the only window", DisplayServer.GetWindowList().Length == 1 && DisplayServer.GetScreenCount() == 1);
        T.Check("it starts windowed at the project's size", DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Windowed && DisplayServer.WindowGetSize().X > 0);
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Maximized);
        T.Check("a mode is kept", DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Maximized);
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        DisplayServer.WindowSetSize(new Vector2I(800, 600));
        T.Check("a size is kept", DisplayServer.WindowGetSize() == new Vector2I(800, 600));
        DisplayServer.WindowSetPosition(new Vector2I(40, 50));
        T.Check("a position is kept", DisplayServer.WindowGetPosition() == new Vector2I(40, 50));
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, true);
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.AlwaysOnTop, true);
        T.Check("flags are kept apart", DisplayServer.WindowGetFlag(DisplayServer.WindowFlags.Borderless) && DisplayServer.WindowGetFlag(DisplayServer.WindowFlags.AlwaysOnTop) &&
                                       !DisplayServer.WindowGetFlag(DisplayServer.WindowFlags.ResizeDisabled));
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.ResizeDisabled, true);
        T.Check("unresizable is a flag, the inverse of resizable", DisplayServer.WindowGetFlag(DisplayServer.WindowFlags.ResizeDisabled));
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Transparent, true);
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.NoFocus, true);
        T.Check("the flags the window cannot apply are kept as set", DisplayServer.WindowGetFlag(DisplayServer.WindowFlags.Transparent) && DisplayServer.WindowGetFlag(DisplayServer.WindowFlags.NoFocus));
        DisplayServer.WindowSetMinSize(new Vector2I(320, 200));
        T.Check("a size limit is kept", DisplayServer.WindowGetMinSize() == new Vector2I(320, 200));
        T.Check("the window has the focus with no engine", DisplayServer.WindowIsFocused());
        T.Check("the display server is named", DisplayServer.GetName() == "Aver");
        T.Check("the numbers are Godot's", (int)DisplayServer.WindowFlags.Borderless == 1 && (int)DisplayServer.WindowFlags.AlwaysOnTop == 2 && (int)DisplayServer.WindowFlags.NoFocus == 4 &&
                                           (int)DisplayServer.WindowMode.ExclusiveFullscreen == 4 && (int)DisplayServer.VSyncMode.Mailbox == 3 && (int)DisplayServer.CursorShape.Help == 16);
        SceneTree.Reset();
    }

    private static void ModesAndScreen()
    {
        SceneTree.Reset();
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.ExclusiveFullscreen);
        T.Check("exclusive fullscreen reads back as asked (it is borderless fullscreen underneath)", DisplayServer.WindowGetMode() == DisplayServer.WindowMode.ExclusiveFullscreen);
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
        T.Check("fullscreen reads back", DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen);
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Minimized);
        T.Check("minimized reads back", DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Minimized);
        T.Check("vsync starts Enabled", DisplayServer.WindowGetVsyncMode() == DisplayServer.VSyncMode.Enabled);
        foreach (DisplayServer.VSyncMode m in Enum.GetValues<DisplayServer.VSyncMode>())
        {
            DisplayServer.WindowSetVsyncMode(m);
            T.Check("vsync mode " + m + " reads back", DisplayServer.WindowGetVsyncMode() == m);
        }
        T.Check("one screen, at the window's size when the engine cannot say", DisplayServer.ScreenGetSize().X > 0 && DisplayServer.ScreenGetPosition() == Vector2I.Zero &&
                                                                              DisplayServer.ScreenGetUsableRect().Size == DisplayServer.ScreenGetSize());
        T.Check("96 dpi at scale 1, an unknown refresh rate", DisplayServer.ScreenGetDpi() == 96 && DisplayServer.ScreenGetScale() == 1f && DisplayServer.ScreenGetRefreshRate() == -1f);
        DisplayServer.ScreenSetKeepOn(true);
        T.Check("keep-on is kept", DisplayServer.ScreenIsKeptOn());
        DisplayServer.ScreenSetKeepOn(false);
        DisplayServer.CursorSetShape(DisplayServer.CursorShape.Cross);
        T.Check("the cursor shape goes through Input's", DisplayServer.CursorGetShape() == DisplayServer.CursorShape.Cross && Input.GetCurrentCursorShape() == Input.CursorShape.Cross);
        DisplayServer.CursorSetShape(DisplayServer.CursorShape.Arrow);
        DisplayServer.WindowSetPosition(new Vector2I(10, 20));
        DisplayServer.WarpMouse(new Vector2I(110, 220));
        T.Check("warp_mouse takes a screen position (the window's position is taken off)", Input.MousePosition == new Vector2(100, 200));
        T.Check("and the pointer reads back in screen pixels", DisplayServer.MouseGetPosition() == new Vector2I(110, 220));
        SceneTree.Reset();
    }

    private static void WindowClass()
    {
        SceneTree.Reset();
        Window w = SceneTree.Instance.Root;
        w.Mode = Window.ModeEnum.Fullscreen;
        T.Check("Window.Mode is DisplayServer's window mode", DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen && w.Mode == Window.ModeEnum.Fullscreen);
        w.Mode = Window.ModeEnum.Windowed;
        w.Borderless = true; w.AlwaysOnTop = true; w.Unresizable = true;
        T.Check("the flags are the same flags", DisplayServer.WindowGetFlag(DisplayServer.WindowFlags.Borderless) && DisplayServer.WindowGetFlag(DisplayServer.WindowFlags.AlwaysOnTop) &&
                                               DisplayServer.WindowGetFlag(DisplayServer.WindowFlags.ResizeDisabled) && w.Borderless && w.AlwaysOnTop && w.Unresizable);
        w.Title = "Hello";
        T.Check("the title is kept", w.Title == "Hello");
        w.Position = new Vector2I(7, 9);
        T.Check("the position is DisplayServer's", DisplayServer.WindowGetPosition() == new Vector2I(7, 9) && w.Position == new Vector2I(7, 9));
        T.Check("the window has the focus", w.HasFocus());
        T.Check("the numbers are Godot's", (int)Window.ModeEnum.ExclusiveFullscreen == 4 && (int)Window.ModeEnum.Maximized == 2);
        SceneTree.Reset();
    }

    private static void SizeSignal()
    {
        SceneTree.Reset();
        Window w = SceneTree.Instance.Root;
        int changes = 0;
        w.SizeChanged += () => changes++;
        Vector2I start = w.Size;
        w.Size = start;
        T.Check("setting the same size raises nothing", changes == 0);
        w.Size = new Vector2I(start.X + 10, start.Y);
        T.Check("a new size raises size_changed once", changes == 1 && w.Size.X == start.X + 10);
        w.ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems;
        w.ContentScaleSize = new Vector2I(640, 360);
        w.Size = new Vector2I(1280, 720);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = new Vector2(640, 360), Relative = new Vector2(1, 1) });
        T.Check("the viewport pointer is in content space under a stretch", w.GetMousePosition() == new Vector2(320, 180));
        SceneTree.Reset();
    }

    private static void FrameCap()
    {
        Engine.MaxFps = 144;
        T.Check("max_fps is kept", Engine.MaxFps == 144);
        Engine.MaxFps = -3;
        T.Check("a negative cap is none", Engine.MaxFps == 0);
    }
}
