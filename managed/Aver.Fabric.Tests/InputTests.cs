// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// The Input singleton and InputMap with Godot's names, driven by synthetic events: action mapping, just-pressed frames, strengths, dead
// zones, exact match, the engine-event bridge, and _Input / _UnhandledInput delivery with set_input_as_handled.

using Aver.Fabric;

static class InputTests
{
    public static int RunAll() => T.Suite(() =>
    {
        T.Run("InputMap: actions, events, dead zones, queries", MapQueries);
        T.Run("Input: key events press and release an action; just_pressed lasts one frame", KeyActions);
        T.Run("Input: modifiers and exact match", Modifiers);
        T.Run("Input: joypad axes, strength, dead zone, get_axis and get_vector", JoypadAxes);
        T.Run("Input: action_press and action_release, keys, buttons, mouse", Direct);
        T.Run("Input: just_pressed in a physics step is per physics frame", PhysicsFrames);
        T.Run("InputEvent: is_action_pressed, is_action_released, get_action_strength, echo", EventQueries);
        T.Run("Input events reach _Input and _UnhandledInput in Godot's order, and set_input_as_handled stops them", Delivery);
        T.Run("The engine's events bridge to Godot's, and back", Bridge);
        T.Run("Input: the mouse mode and velocity", Mouse);
        T.Run("Input: the wider key set, side buttons, wheel directions and pad extras bridge both ways", WiderSet);
        T.Run("Input: touch events bridge, gamepads, rumble, cursor shape and sensors", Devices);
    });

    private static InputEventKey Key(Aver.Fabric.Key k, bool pressed, bool ctrl = false, bool echo = false) =>
        new() { Keycode = k, PhysicalKeycode = k, Pressed = pressed, CtrlPressed = ctrl, Echo = echo };

    private static void MapQueries()
    {
        T.Check("no action yet", !InputMap.HasAction("jump"));
        InputMap.AddAction("jump");
        T.Check("add_action", InputMap.HasAction("jump") && InputMap.GetActions().Contains("jump") && InputMap.ActionGetDeadzone("jump") == 0.5f);
        InputMap.AddAction("jump");
        T.Check("adding it again is an error that changes nothing", T.ErrorCount("exists already") == 1 && InputMap.GetActions().Count(a => a == "jump") == 1);
        InputEventKey space = Key(Aver.Fabric.Key.Space, true);
        InputMap.ActionAddEvent("jump", space);
        T.Check("action_add_event, action_has_event, action_get_events", InputMap.ActionHasEvent("jump", space) && InputMap.ActionGetEvents("jump").Count == 1);
        T.Check("an event matches the action it was added to", InputMap.EventIsAction(Key(Aver.Fabric.Key.Space, true), "jump") && !InputMap.EventIsAction(Key(Aver.Fabric.Key.A, true), "jump"));
        InputMap.ActionSetDeadzone("jump", 0.2f);
        T.Check("the dead zone is settable", InputMap.ActionGetDeadzone("jump") == 0.2f);
        InputMap.ActionEraseEvent("jump", space);
        T.Check("action_erase_event", InputMap.ActionGetEvents("jump").Count == 0);
        InputMap.ActionAddEvent("jump", space);
        InputMap.ActionEraseEvents("jump");
        T.Check("action_erase_events", InputMap.ActionGetEvents("jump").Count == 0);
        InputMap.EraseAction("jump");
        T.Check("erase_action", !InputMap.HasAction("jump"));
        InputMap.ActionAddEvent("ghost", space);
        T.Check("an event for an action that does not exist is an error", T.ErrorCount("no action") == 1);
    }

    private static void KeyActions()
    {
        InputMap.AddAction("jump");
        InputMap.ActionAddEvent("jump", Key(Aver.Fabric.Key.Space, true));
        T.Check("not pressed at first", !Input.IsActionPressed("jump") && !Input.IsActionJustPressed("jump") && Input.GetActionStrength("jump") == 0f);
        Input.ParseInputEvent(Key(Aver.Fabric.Key.Space, true));
        T.Check("a key press presses the action, at full strength", Input.IsActionPressed("jump") && Input.GetActionStrength("jump") == 1f);
        T.Check("and it is just pressed in the frame it came", Input.IsActionJustPressed("jump") && !Input.IsActionJustReleased("jump"));
        T.Check("the key itself is held", Input.IsKeyPressed(Aver.Fabric.Key.Space) && Input.IsAnythingPressed());
        T.Frame();
        T.Check("one frame later it is still pressed but no longer just pressed", Input.IsActionPressed("jump") && !Input.IsActionJustPressed("jump"));
        Input.ParseInputEvent(Key(Aver.Fabric.Key.Space, false));
        T.Check("the release ends it and is just released", !Input.IsActionPressed("jump") && Input.IsActionJustReleased("jump") && Input.GetActionStrength("jump") == 0f);
        T.Frame();
        T.Check("just released lasts one frame", !Input.IsActionJustReleased("jump") && !Input.IsKeyPressed(Aver.Fabric.Key.Space) && !Input.IsAnythingPressed());

        // Echo (key repeat) does not re-press.
        Input.ParseInputEvent(Key(Aver.Fabric.Key.Space, true));
        T.Frame();
        Input.ParseInputEvent(Key(Aver.Fabric.Key.Space, true, echo: true));
        T.Check("an echo does not make it just pressed again", Input.IsActionPressed("jump") && !Input.IsActionJustPressed("jump"));

        // Two keys on one action: the action follows the event that came last.
        InputMap.ActionAddEvent("jump", Key(Aver.Fabric.Key.Enter, true));
        Input.ParseInputEvent(Key(Aver.Fabric.Key.Space, false));
        Input.ParseInputEvent(Key(Aver.Fabric.Key.Enter, true));
        T.Check("a second key bound to the action presses it too", Input.IsActionPressed("jump"));
    }

    private static void Modifiers()
    {
        InputMap.AddAction("save");
        InputMap.ActionAddEvent("save", Key(Aver.Fabric.Key.S, true, ctrl: true));
        Input.ParseInputEvent(Key(Aver.Fabric.Key.S, true));
        T.Check("an action that wants Ctrl+S ignores S alone", !Input.IsActionPressed("save"));
        Input.ParseInputEvent(Key(Aver.Fabric.Key.S, false));
        Input.ParseInputEvent(Key(Aver.Fabric.Key.S, true, ctrl: true));
        T.Check("and takes Ctrl+S", Input.IsActionPressed("save"));
        Input.ParseInputEvent(Key(Aver.Fabric.Key.S, false, ctrl: true));

        InputMap.AddAction("plain");
        InputMap.ActionAddEvent("plain", Key(Aver.Fabric.Key.S, true));
        Input.ParseInputEvent(Key(Aver.Fabric.Key.S, true, ctrl: true));
        T.Check("an action on S alone also takes Ctrl+S (extra modifiers are fine)", Input.IsActionPressed("plain"));
        T.Check("but not with exact_match", !Input.IsActionPressed("plain", exactMatch: true));
        Input.ParseInputEvent(Key(Aver.Fabric.Key.S, false, ctrl: true));
        Input.ParseInputEvent(Key(Aver.Fabric.Key.S, true));
        T.Check("the plain S matches exactly", Input.IsActionPressed("plain", exactMatch: true));
        T.Check("event.is_action with exact_match", Key(Aver.Fabric.Key.S, true).IsAction("plain", exactMatch: true) && !Key(Aver.Fabric.Key.S, true, ctrl: true).IsAction("plain", exactMatch: true));
    }

    private static InputEventJoypadMotion Axis(JoyAxis a, float v) => new() { Axis = a, AxisValue = v };

    private static void JoypadAxes()
    {
        InputMap.AddAction("right", 0.5f);
        InputMap.ActionAddEvent("right", Axis(JoyAxis.LeftX, 1f));
        InputMap.AddAction("left", 0.5f);
        InputMap.ActionAddEvent("left", Axis(JoyAxis.LeftX, -1f));
        InputMap.AddAction("down", 0.5f);
        InputMap.ActionAddEvent("down", Axis(JoyAxis.LeftY, 1f));
        InputMap.AddAction("up", 0.5f);
        InputMap.ActionAddEvent("up", Axis(JoyAxis.LeftY, -1f));

        Input.ParseInputEvent(Axis(JoyAxis.LeftX, 0.3f));
        T.Check("below the dead zone nothing is pressed and the strength is 0", !Input.IsActionPressed("right") && Input.GetActionStrength("right") == 0f);
        T.CheckNum("but the raw strength is the stick value", 0.3f, Input.GetActionRawStrength("right"));
        Input.ParseInputEvent(Axis(JoyAxis.LeftX, 0.75f));
        T.Check("past it the action is pressed", Input.IsActionPressed("right") && !Input.IsActionPressed("left"));
        T.CheckNum("with the strength rescaled past the dead zone: (0.75 - 0.5) / 0.5", 0.5f, Input.GetActionStrength("right"));
        T.CheckNum("and the raw strength unscaled", 0.75f, Input.GetActionRawStrength("right"));
        T.CheckNum("get_axis is positive minus negative strength", 0.5f, Input.GetAxis("left", "right"));
        Input.ParseInputEvent(Axis(JoyAxis.LeftX, -1f));
        T.Check("the other direction presses the other action", Input.IsActionPressed("left") && !Input.IsActionPressed("right"));
        T.CheckNum("get_axis is negative", -1f, Input.GetAxis("left", "right"));
        Input.ParseInputEvent(Axis(JoyAxis.LeftX, 0f));
        T.Check("back to centre releases both", !Input.IsActionPressed("left") && !Input.IsActionPressed("right"));
        T.Check("the stick value reads back through get_joy_axis", Input.GetJoyAxis(0, JoyAxis.LeftX) == 0f);

        // get_vector: the dead zone is a circle on the raw strengths.
        Input.ParseInputEvent(Axis(JoyAxis.LeftX, 0.6f));
        Input.ParseInputEvent(Axis(JoyAxis.LeftY, 0.8f));
        Vector2 v = Input.GetVector("left", "right", "up", "down");
        T.Check("a stick at length 1 gives the vector as it is", T.Near(v.X, 0.6f) && T.Near(v.Y, 0.8f), v.ToString());
        Input.ParseInputEvent(Axis(JoyAxis.LeftX, 0.3f));
        Input.ParseInputEvent(Axis(JoyAxis.LeftY, 0.3f));
        T.Check("inside the circle dead zone (length 0.42 < 0.5; each axis alone is below its own dead zone too) it is zero", Input.GetVector("left", "right", "up", "down") == Vector2.Zero);
        Input.ParseInputEvent(Axis(JoyAxis.LeftX, 0.4f));
        Input.ParseInputEvent(Axis(JoyAxis.LeftY, 0.4f));
        Vector2 diag = Input.GetVector("left", "right", "up", "down");
        T.Check("the dead zone is on the vector, not on each axis: (0.4, 0.4) is length 0.57 and gets through", diag.X > 0.05f && T.Near(diag.X, diag.Y), diag.ToString());
        Input.ParseInputEvent(Axis(JoyAxis.LeftX, 0.6f));
        Input.ParseInputEvent(Axis(JoyAxis.LeftY, 0f));
        Vector2 w = Input.GetVector("left", "right", "up", "down");
        T.Check("past it the length is rescaled: 0.6 becomes (0.6 - 0.5) / (1 - 0.5) = 0.2", T.Near(w.X, 0.2f) && w.Y == 0f, w.ToString());
        Vector2 custom = Input.GetVector("left", "right", "up", "down", deadzone: 0f);
        T.Check("an explicit dead zone of 0 passes the raw value", T.Near(custom.X, 0.6f));
        Input.ParseInputEvent(Axis(JoyAxis.LeftX, 1f));
        Input.ParseInputEvent(Axis(JoyAxis.LeftY, 1f));
        T.Check("a diagonal past length 1 is normalised", T.Near(Input.GetVector("left", "right", "up", "down").Length(), 1f));

        // A trigger or button bound with the same action.
        InputMap.AddAction("fire");
        InputMap.ActionAddEvent("fire", new InputEventJoypadButton { ButtonIndex = JoyButton.A });
        Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.A, Pressed = true });
        T.Check("a pad button presses its action and reads back", Input.IsActionPressed("fire") && Input.IsJoyButtonPressed(0, JoyButton.A));
        T.CheckNum("the axis reads back through get_joy_axis", 1f, Input.GetJoyAxis(0, JoyAxis.LeftX));
    }

    private static void Direct()
    {
        InputMap.AddAction("sprint");
        Input.ActionPress("sprint", 0.4f);
        T.Check("action_press presses an action by name", Input.IsActionPressed("sprint") && Input.IsActionJustPressed("sprint"));
        T.CheckNum("with the strength given", 0.4f, Input.GetActionStrength("sprint"));
        Input.ActionRelease("sprint");
        T.Check("action_release", !Input.IsActionPressed("sprint") && Input.IsActionJustReleased("sprint") && Input.GetActionStrength("sprint") == 0f);

        Input.ActionPress("never_declared");
        T.Check("an action that is not in the map can still be pressed by name", Input.IsActionPressed("never_declared"));

        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        T.Check("mouse buttons", Input.IsMouseButtonPressed(MouseButton.Left) && !Input.IsMouseButtonPressed(MouseButton.Right) && (Input.GetMouseButtonMask() & MouseButtonMask.Left) != 0);
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
        T.Check("released", !Input.IsMouseButtonPressed(MouseButton.Left));

        Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.B, Pressed = true });
        Input.ParseInputEvent(Axis(JoyAxis.RightY, -0.5f));
        T.Check("the pad is connected once it sends events", Input.GetConnectedJoypads().Count == 1 && Input.GetJoyAxis(0, JoyAxis.RightY) == -0.5f);
        Input.ParseInputEvent(Key(Aver.Fabric.Key.W, true));
        T.Check("is_key_pressed, is_physical_key_pressed and is_key_label_pressed", Input.IsKeyPressed(Aver.Fabric.Key.W) && Input.IsPhysicalKeyPressed(Aver.Fabric.Key.W) && Input.IsKeyLabelPressed(Aver.Fabric.Key.W));
    }

    private static void PhysicsFrames()
    {
        InputMap.AddAction("dash");
        InputMap.ActionAddEvent("dash", Key(Aver.Fabric.Key.D, true));
        var seen = new List<string>();
        var probe = new PhysicsInputProbe { Seen = seen };
        SceneTree.Instance.Root.AddChild(probe);
        T.Frame(0.016, physicsSteps: 2);
        seen.Clear();
        Input.ParseInputEvent(Key(Aver.Fabric.Key.D, true));
        T.Frame(0.016, physicsSteps: 3);
        T.CheckSeq("a press made between frames is just pressed in the first physics step after it and the next process frame only", new[] { "physics true", "physics false", "physics false", "process true" }, seen);
        seen.Clear();
        T.Frame(0.016, physicsSteps: 1);
        T.CheckSeq("and in none of the next ones", new[] { "physics false", "process false" }, seen);
    }

    private static void EventQueries()
    {
        InputMap.AddAction("use");
        InputMap.ActionAddEvent("use", Key(Aver.Fabric.Key.E, true));
        InputEventKey press = Key(Aver.Fabric.Key.E, true), release = Key(Aver.Fabric.Key.E, false), echo = Key(Aver.Fabric.Key.E, true, echo: true);
        T.Check("is_action_pressed on a press", press.IsActionPressed("use") && !release.IsActionPressed("use"));
        T.Check("is_action_released on a release", release.IsActionReleased("use") && !press.IsActionReleased("use"));
        T.Check("an echo is not a press unless allowed", !echo.IsActionPressed("use") && echo.IsActionPressed("use", allowEcho: true));
        T.CheckNum("get_action_strength is 1 for a press and 0 for a release", 1f, press.GetActionStrength("use"));
        T.CheckNum("and 0 for a release", 0f, release.GetActionStrength("use"));
        T.Check("an unrelated event is not the action", !Key(Aver.Fabric.Key.Q, true).IsAction("use") && !Key(Aver.Fabric.Key.Q, true).IsActionPressed("use"));
        var motion = new InputEventMouseMotion { Relative = new Vector2(3, 4) };
        T.Check("mouse motion is no action", !motion.IsAction("use") && !motion.IsPressed() && !motion.IsActionType());
        var viaAction = new InputEventAction { Action = "use", Pressed = true, Strength = 0.7f };
        T.Check("an action event names its action", viaAction.IsAction("use") && viaAction.IsActionPressed("use") && !viaAction.IsAction("other"));
        T.CheckNum("with its strength", 0.7f, viaAction.GetActionStrength("use"));
        Input.ParseInputEvent(viaAction);
        T.Check("and parsing it presses the action", Input.IsActionPressed("use"));
        T.Check("is_match compares keys, buttons and axes", press.IsMatch(Key(Aver.Fabric.Key.E, false)) && !press.IsMatch(Key(Aver.Fabric.Key.F, true)) &&
                                                          new InputEventJoypadButton { ButtonIndex = JoyButton.X }.IsMatch(new InputEventJoypadButton { ButtonIndex = JoyButton.X }));
        T.Check("as_text names the key and its modifiers", Key(Aver.Fabric.Key.S, true, ctrl: true).AsText() == "Ctrl+S" && press.AsText() == "E");
    }

    private static void Delivery()
    {
        var tree = SceneTree.Instance;
        var a = new InputProbe { Name = "A", Tag = "A" };
        var b = new InputProbe { Name = "B", Tag = "B" };
        var child = new InputProbe { Name = "Child", Tag = "Child" };
        tree.Root.AddChild(a); tree.Root.AddChild(b); b.AddChild(child);
        // Only nodes that process input get events.
        var quiet = new Node { Name = "Quiet" };
        tree.Root.AddChild(quiet);
        InputEventKey e = Key(Aver.Fabric.Key.F, true);
        InputProbe.All.Clear();
        Input.ParseInputEvent(e);
        T.CheckSeq("every _Input first (last added, deepest first), then every _UnhandledInput", new[]
        {
            "Child input F", "B input F", "A input F", "Child unhandled F", "B unhandled F", "A unhandled F",
        }, InputProbe.All);

        a.Log.Clear(); b.Log.Clear(); child.Log.Clear();
        child.StopInInput = true;
        Input.ParseInputEvent(Key(Aver.Fabric.Key.F, true));
        T.Check("set_input_as_handled in _Input stops the event: nothing else sees it", child.Log.Count == 1 && b.Log.Count == 0 && a.Log.Count == 0);
        child.StopInInput = false; child.Log.Clear();
        b.StopInUnhandled = true;
        Input.ParseInputEvent(Key(Aver.Fabric.Key.F, true));
        T.Check("in _UnhandledInput it stops the later nodes of that pass", child.Log.Count == 2 && b.Log.Count == 2 && a.Log.Count == 1 && a.Log[0].Contains("input"), string.Join(" | ", child.Log.Concat(b.Log).Concat(a.Log)));

        b.StopInUnhandled = false; a.Log.Clear(); b.Log.Clear(); child.Log.Clear();
        b.SetProcessInput(false);
        Input.ParseInputEvent(Key(Aver.Fabric.Key.F, true));
        T.Check("set_process_input(false) takes a node out of the _Input pass only", !b.Log.Any(l => l.Contains(" input ")) && b.Log.Any(l => l.Contains("unhandled")));

        tree.Paused = true;
        a.Log.Clear(); b.Log.Clear(); child.Log.Clear();
        Input.ParseInputEvent(Key(Aver.Fabric.Key.F, true));
        T.Check("a paused tree delivers to no pausable node", a.Log.Count == 0 && b.Log.Count == 0 && child.Log.Count == 0);
        a.ProcessMode = Node.ProcessModeEnum.Always;
        Input.ParseInputEvent(Key(Aver.Fabric.Key.F, true));
        T.Check("but to one whose process mode is Always", a.Log.Count == 2);
        tree.Paused = false;

        // An engine event (Aver.Framework.InputEvent) reaches the same virtuals through Node.OnInput / OnUnhandledInput.
        a.Log.Clear(); a.ProcessMode = Node.ProcessModeEnum.Inherit;
        var fw = new Aver.Framework.InputEventKey(Aver.Framework.Key.Space, true);
        a.OnInput(fw); a.OnUnhandledInput(fw);
        T.CheckSeq("an engine event arrives as the Godot key event", new[] { "A input Space", "A unhandled Space" }, a.Log);
        a.Log.Clear();
        a.StopInInput = true;
        a.OnInput(fw);
        T.Check("and set_input_as_handled marks the engine's event", fw.Handled);
    }

    private static void Bridge()
    {
        InputEvent k = InputBridgeProbe.FromFramework(new Aver.Framework.InputEventKey(Aver.Framework.Key.A, true));
        T.Check("a key", k is InputEventKey { Keycode: Aver.Fabric.Key.A, Pressed: true });
        T.Check("the digits map to Godot's codes", InputBridgeProbe.FromFramework(new Aver.Framework.InputEventKey(Aver.Framework.Key.D7, true)) is InputEventKey { Keycode: Aver.Fabric.Key.Key7 });
        T.Check("and the special keys", InputBridgeProbe.FromFramework(new Aver.Framework.InputEventKey(Aver.Framework.Key.Escape, true)) is InputEventKey { Keycode: Aver.Fabric.Key.Escape } &&
                                        InputBridgeProbe.FromFramework(new Aver.Framework.InputEventKey(Aver.Framework.Key.LeftShift, false)) is InputEventKey { Keycode: Aver.Fabric.Key.Shift, Pressed: false });
        T.Check("a mouse button", InputBridgeProbe.FromFramework(new Aver.Framework.InputEventMouseButton(Aver.Framework.Key.MouseRight, true)) is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true });
        T.Check("the wheel is a button event", InputBridgeProbe.FromFramework(new Aver.Framework.InputEventMouseWheel(1f)) is InputEventMouseButton { ButtonIndex: MouseButton.WheelUp } &&
                                               InputBridgeProbe.FromFramework(new Aver.Framework.InputEventMouseWheel(-2f)) is InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Factor: 2f });
        T.Check("pad buttons", InputBridgeProbe.FromFramework(new Aver.Framework.InputEventJoypadButton(Aver.Framework.GamepadButton.LeftThumb, true)) is InputEventJoypadButton { ButtonIndex: JoyButton.LeftStick, Pressed: true } &&
                               InputBridgeProbe.FromFramework(new Aver.Framework.InputEventJoypadButton(Aver.Framework.GamepadButton.DPadUp, true)) is InputEventJoypadButton { ButtonIndex: JoyButton.DpadUp });
        T.Check("a stick pushed up is negative in Godot", InputBridgeProbe.FromFramework(new Aver.Framework.InputEventJoypadMotion(Aver.Framework.GamepadAxis.LeftY, 0.8f)) is InputEventJoypadMotion { Axis: JoyAxis.LeftY, AxisValue: -0.8f });
        T.Check("x is as it is, triggers too", InputBridgeProbe.FromFramework(new Aver.Framework.InputEventJoypadMotion(Aver.Framework.GamepadAxis.RightX, 0.25f)) is InputEventJoypadMotion { Axis: JoyAxis.RightX, AxisValue: 0.25f } &&
                                               InputBridgeProbe.FromFramework(new Aver.Framework.InputEventJoypadMotion(Aver.Framework.GamepadAxis.RightTrigger, 1f)) is InputEventJoypadMotion { Axis: JoyAxis.TriggerRight });
        var one = new Aver.Framework.InputEventKey(Aver.Framework.Key.Q, true);
        T.Check("asking twice gives the same Godot event", ReferenceEquals(InputBridgeProbe.FromFramework(one), InputBridgeProbe.FromFramework(one)));
        InputBridgeProbe.FromFramework(one).MarkHandledForTest();
        T.Check("a handled mark reaches the engine event", one.Handled);

        T.Check("back: a key", InputBridgeProbe.ToFramework(Key(Aver.Fabric.Key.Space, true)) is Aver.Framework.InputEventKey { Key: Aver.Framework.Key.Space, IsPressed: true });
        T.Check("back: the numpad enter has its own key now, a key the engine lacks is nothing", InputBridgeProbe.ToFramework(Key(Aver.Fabric.Key.KpEnter, true)) is Aver.Framework.InputEventKey { Key: Aver.Framework.Key.KpEnter } &&
                                                                                                 InputBridgeProbe.ToFramework(Key(Aver.Fabric.Key.F5, true)) is Aver.Framework.InputEventKey { Key: Aver.Framework.Key.F5 } &&
                                                                                                 InputBridgeProbe.ToFramework(Key(Aver.Fabric.Key.F13, true)) == null);
        T.Check("back: mouse button, wheel and pad button", InputBridgeProbe.ToFramework(new InputEventMouseButton { ButtonIndex = MouseButton.Middle, Pressed = true }) is Aver.Framework.InputEventMouseButton { Button: Aver.Framework.Key.MouseMiddle } &&
                                                           InputBridgeProbe.ToFramework(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = true, Factor = 3f }) is Aver.Framework.InputEventMouseWheel { Delta: -3f } &&
                                                           InputBridgeProbe.ToFramework(new InputEventJoypadButton { ButtonIndex = JoyButton.Y, Pressed = true }) is Aver.Framework.InputEventJoypadButton { Button: Aver.Framework.GamepadButton.Y });
        T.Check("back: an action event has no engine twin", InputBridgeProbe.ToFramework(new InputEventAction { Action = "x" }) == null);
    }

    private static void WiderSet()
    {
        // Every Aver key slot past the original set maps to a Godot key and back.
        var pairs = new (Aver.Framework.Key A, Aver.Fabric.Key G)[]
        {
            (Aver.Framework.Key.F1, Aver.Fabric.Key.F1), (Aver.Framework.Key.F12, Aver.Fabric.Key.F12),
            (Aver.Framework.Key.PageUp, Aver.Fabric.Key.Pageup), (Aver.Framework.Key.PageDown, Aver.Fabric.Key.Pagedown),
            (Aver.Framework.Key.Home, Aver.Fabric.Key.Home), (Aver.Framework.Key.End, Aver.Fabric.Key.End),
            (Aver.Framework.Key.Insert, Aver.Fabric.Key.Insert), (Aver.Framework.Key.Delete, Aver.Fabric.Key.Delete),
            (Aver.Framework.Key.Backspace, Aver.Fabric.Key.Backspace), (Aver.Framework.Key.CapsLock, Aver.Fabric.Key.Capslock),
            (Aver.Framework.Key.NumLock, Aver.Fabric.Key.Numlock), (Aver.Framework.Key.ScrollLock, Aver.Fabric.Key.Scrolllock),
            (Aver.Framework.Key.Pause, Aver.Fabric.Key.Pause), (Aver.Framework.Key.PrintScreen, Aver.Fabric.Key.Print),
            (Aver.Framework.Key.Menu, Aver.Fabric.Key.Menu), (Aver.Framework.Key.Meta, Aver.Fabric.Key.Meta),
            (Aver.Framework.Key.Minus, Aver.Fabric.Key.Minus), (Aver.Framework.Key.Equal, Aver.Fabric.Key.Equal),
            (Aver.Framework.Key.LBracket, Aver.Fabric.Key.Bracketleft), (Aver.Framework.Key.RBracket, Aver.Fabric.Key.Bracketright),
            (Aver.Framework.Key.Backslash, Aver.Fabric.Key.Backslash), (Aver.Framework.Key.Semicolon, Aver.Fabric.Key.Semicolon),
            (Aver.Framework.Key.Apostrophe, Aver.Fabric.Key.Apostrophe), (Aver.Framework.Key.Comma, Aver.Fabric.Key.Comma),
            (Aver.Framework.Key.Period, Aver.Fabric.Key.Period), (Aver.Framework.Key.Slash, Aver.Fabric.Key.Slash),
            (Aver.Framework.Key.Grave, Aver.Fabric.Key.Quoteleft), (Aver.Framework.Key.Kp0, Aver.Fabric.Key.Kp0), (Aver.Framework.Key.Kp9, Aver.Fabric.Key.Kp9),
            (Aver.Framework.Key.KpAdd, Aver.Fabric.Key.KpAdd), (Aver.Framework.Key.KpSubtract, Aver.Fabric.Key.KpSubtract),
            (Aver.Framework.Key.KpMultiply, Aver.Fabric.Key.KpMultiply), (Aver.Framework.Key.KpDivide, Aver.Fabric.Key.KpDivide),
            (Aver.Framework.Key.KpDecimal, Aver.Fabric.Key.KpPeriod), (Aver.Framework.Key.KpEnter, Aver.Fabric.Key.KpEnter),
        };
        bool all = true;
        foreach (var (a, g) in pairs)
            all &= InputBridgeProbe.FromFramework(new Aver.Framework.InputEventKey(a, true)) is InputEventKey k && k.Keycode == g &&
                   InputBridgeProbe.ToFramework(Key(g, true)) is Aver.Framework.InputEventKey { } back && back.Key == a;
        T.Check("the appended keys bridge to Godot's codes and back", all);
        // Each engine key slot of the keyboard has a Godot twin.
        int unmapped = 0;
        foreach (Aver.Framework.Key a in Enum.GetValues<Aver.Framework.Key>())
        {
            bool mouse = a is Aver.Framework.Key.MouseLeft or Aver.Framework.Key.MouseRight or Aver.Framework.Key.MouseMiddle or Aver.Framework.Key.MouseX1 or Aver.Framework.Key.MouseX2 or
                         Aver.Framework.Key.MouseWheelUp or Aver.Framework.Key.MouseWheelDown or Aver.Framework.Key.MouseWheelLeft or Aver.Framework.Key.MouseWheelRight;
            if (!mouse && InputBridgeProbe.FromFramework(new Aver.Framework.InputEventKey(a, true)) is InputEventKey { Keycode: Aver.Fabric.Key.None }) unmapped++;
        }
        T.Check("no engine keyboard key reaches a script as Key.None", unmapped == 0);

        T.Check("the side buttons", InputBridgeProbe.FromFramework(new Aver.Framework.InputEventMouseButton(Aver.Framework.Key.MouseX1, true)) is InputEventMouseButton { ButtonIndex: MouseButton.Xbutton1 } &&
                                    InputBridgeProbe.FromFramework(new Aver.Framework.InputEventMouseButton(Aver.Framework.Key.MouseX2, false)) is InputEventMouseButton { ButtonIndex: MouseButton.Xbutton2, Pressed: false });
        T.Check("the sideways wheel", InputBridgeProbe.FromFramework(new Aver.Framework.InputEventMouseWheel(0f, 2f)) is InputEventMouseButton { ButtonIndex: MouseButton.WheelRight, Factor: 2f } &&
                                     InputBridgeProbe.FromFramework(new Aver.Framework.InputEventMouseWheel(0f, -1f)) is InputEventMouseButton { ButtonIndex: MouseButton.WheelLeft });
        T.Check("back: the side buttons and the sideways wheel", InputBridgeProbe.ToFramework(new InputEventMouseButton { ButtonIndex = MouseButton.Xbutton2, Pressed = true }) is Aver.Framework.InputEventMouseButton { Button: Aver.Framework.Key.MouseX2 } &&
                                                              InputBridgeProbe.ToFramework(new InputEventMouseButton { ButtonIndex = MouseButton.WheelLeft, Pressed = true, Factor = 3f }) is Aver.Framework.InputEventMouseWheel { Delta: 0f, Horizontal: -3f });
        T.Check("pad extras", InputBridgeProbe.FromFramework(new Aver.Framework.InputEventJoypadButton(Aver.Framework.GamepadButton.Touchpad, true, 2)) is InputEventJoypadButton { ButtonIndex: JoyButton.Touchpad, Device: 2 } &&
                             InputBridgeProbe.FromFramework(new Aver.Framework.InputEventJoypadButton(Aver.Framework.GamepadButton.Paddle3, true)) is InputEventJoypadButton { ButtonIndex: JoyButton.Paddle3 } &&
                             InputBridgeProbe.FromFramework(new Aver.Framework.InputEventJoypadButton(Aver.Framework.GamepadButton.Guide, true)) is InputEventJoypadButton { ButtonIndex: JoyButton.Guide });
        T.Check("back: a pad extra and the device number", InputBridgeProbe.ToFramework(new InputEventJoypadButton { ButtonIndex = JoyButton.Misc1, Pressed = true, Device = 1 }) is Aver.Framework.InputEventJoypadButton { Button: Aver.Framework.GamepadButton.Misc1, Pad: 1 });
        // Godot's numbers.
        T.Check("Godot's numbers for the new enum members", (long)Aver.Fabric.Key.Kp5 == 4194443 && (long)Aver.Fabric.Key.Menu == 4194370 && (long)Aver.Fabric.Key.Quoteleft == 96 &&
                                                          (int)JoyButton.Paddle1 == 16 && (int)JoyButton.Touchpad == 20 && (long)Aver.Fabric.Key.F24 == 4194355);
    }

    private static void Devices()
    {
        T.Check("a touch event arrives as a screen touch", InputBridgeProbe.FromFramework(new Aver.Framework.InputEventTouch(3, true, 10f, 20f)) is InputEventScreenTouch { Index: 3, Pressed: true } t0 && t0.Position == new Vector2(10, 20));
        T.Check("a finger moving as a screen drag", InputBridgeProbe.FromFramework(new Aver.Framework.InputEventTouchMotion(1, 12f, 20f, 2f, 0f)) is InputEventScreenDrag { Index: 1 } d0 && d0.Position == new Vector2(12, 20) && d0.Relative == new Vector2(2, 0));
        T.Check("back: touch and drag", InputBridgeProbe.ToFramework(new InputEventScreenTouch { Index = 2, Pressed = false, Position = new Vector2(5, 6) }) is Aver.Framework.InputEventTouch { Finger: 2, IsPressed: false, X: 5f, Y: 6f } &&
                                       InputBridgeProbe.ToFramework(new InputEventScreenDrag { Index = 4, Position = new Vector2(1, 2), Relative = new Vector2(3, 4) }) is Aver.Framework.InputEventTouchMotion { Finger: 4, RelativeX: 3f, RelativeY: 4f });

        T.Check("a desktop with no engine has no pads and no names", Input.GetConnectedJoypads().Count == 0 && Input.GetJoyName(0) == "" && !Input.IsJoyKnown(0) && Input.GetJoyInfo(0).Count == 0);
        Input.StartJoyVibration(1, 0.25f, 2f, 1.5f);
        T.Check("a rumble is kept as asked, clamped", Input.GetJoyVibrationStrength(1) == new Vector2(0.25f, 1f) && Input.GetJoyVibrationDuration(1) == 1.5f);
        Input.StopJoyVibration(1);
        T.Check("and stopped", Input.GetJoyVibrationStrength(1) == Vector2.Zero);
        T.Check("the pointer's cursor shape is kept", Input.GetCurrentCursorShape() == Input.CursorShape.Arrow);
        Input.SetDefaultCursorShape(Input.CursorShape.PointingHand);
        T.Check("and changes", Input.GetCurrentCursorShape() == Input.CursorShape.PointingHand);
        Input.SetDefaultCursorShape();
        T.Check("the motion sensors read zero", Input.GetAccelerometer() == Vector3.Zero && Input.GetGravity() == Vector3.Zero && Input.GetGyroscope() == Vector3.Zero && Input.GetMagnetometer() == Vector3.Zero);
        Input.EmulateTouchFromMouse = true;
        T.Check("touch emulation is a switch", Input.EmulateTouchFromMouse);
        Input.EmulateTouchFromMouse = false;
        T.Check("the touch screen is absent on a desktop", !DisplayServer.IsTouchscreenAvailable());
        int seen = 0;
        Input.JoyConnectionChanged += (_, _) => seen++;
        T.Check("the object that carries Input's signals exists", Input.Singleton != null);
        T.Check("no connection change without a pad", seen == 0);
    }

    private static void Mouse()
    {
        T.Check("the mouse starts visible, as in Godot", Input.MouseMode == Input.MouseModeEnum.Visible);
        Input.MouseMode = Input.MouseModeEnum.Captured;
        T.Check("mouse_mode is settable", Input.MouseMode == Input.MouseModeEnum.Captured);
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Input.ParseInputEvent(new InputEventMouseMotion { Relative = new Vector2(10, 0), Velocity = new Vector2(600, 0), Position = new Vector2(100, 50) });
        T.Check("the last motion's velocity is kept", Input.GetLastMouseVelocity() == new Vector2(600, 0));
        T.Check("and the viewport reports the pointer position", SceneTree.Instance.Root.GetMousePosition() == new Vector2(100, 50));
        T.Check("the viewport has a size and a visible rect", SceneTree.Instance.Root.Size.X > 0 && SceneTree.Instance.Root.GetVisibleRect().Size.X == SceneTree.Instance.Root.Size.X);
    }
}

class PhysicsInputProbe : Node
{
    public List<string> Seen = new();
    public override void _PhysicsProcess(double delta) => Seen.Add("physics " + (Input.IsActionJustPressed("dash") ? "true" : "false"));
    public override void _Process(double delta) => Seen.Add("process " + (Input.IsActionJustPressed("dash") ? "true" : "false"));
}

// The bridge is internal to Aver.Fabric; the tests see it through InternalsVisibleTo.
static class InputBridgeProbe
{
    public static InputEvent FromFramework(Aver.Framework.InputEvent e) => InputBridge.FromFramework(e);
    public static Aver.Framework.InputEvent? ToFramework(InputEvent e) => InputBridge.ToFramework(e);
    public static void MarkHandledForTest(this InputEvent e) => e.MarkHandled();
}
