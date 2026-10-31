// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Godot's input vocabulary: the Key, MouseButton, JoyButton and JoyAxis enums (with Godot's numbers) and the InputEvent classes a
// script's _Input receives. The engine's own events (Aver.Framework.InputEvent, G1) are bridged into these by InputBridge.

using System.Runtime.CompilerServices;

namespace Aver.Fabric;

/// <summary>Keyboard keys, with Godot's key codes (letters and digits are their ASCII codes, special keys are 1 &lt;&lt; 22 plus an index).</summary>
public enum Key : long
{
    None = 0, Special = 4194304,
    Escape = 4194305, Tab = 4194306, Backtab = 4194307, Backspace = 4194308, Enter = 4194309, KpEnter = 4194310, Insert = 4194311,
    Delete = 4194312, Pause = 4194313, Print = 4194314, Sysreq = 4194315, Clear = 4194316, Home = 4194317, End = 4194318,
    Left = 4194319, Up = 4194320, Right = 4194321, Down = 4194322, Pageup = 4194323, Pagedown = 4194324,
    Shift = 4194325, Ctrl = 4194326, Meta = 4194327, Alt = 4194328, Capslock = 4194329, Numlock = 4194330, Scrolllock = 4194331,
    F1 = 4194332, F2 = 4194333, F3 = 4194334, F4 = 4194335, F5 = 4194336, F6 = 4194337, F7 = 4194338, F8 = 4194339,
    F9 = 4194340, F10 = 4194341, F11 = 4194342, F12 = 4194343,
    // F13 to F24 have no engine key slot (Aver's key set stops at F12): a script can name them, and nothing presses them.
    F13 = 4194344, F14 = 4194345, F15 = 4194346, F16 = 4194347, F17 = 4194348, F18 = 4194349, F19 = 4194350, F20 = 4194351,
    F21 = 4194352, F22 = 4194353, F23 = 4194354, F24 = 4194355,
    Menu = 4194370, Hyper = 4194371, Help = 4194373,
    KpMultiply = 4194433, KpDivide = 4194434, KpSubtract = 4194435, KpPeriod = 4194436, KpAdd = 4194437,
    Kp0 = 4194438, Kp1 = 4194439, Kp2 = 4194440, Kp3 = 4194441, Kp4 = 4194442, Kp5 = 4194443, Kp6 = 4194444, Kp7 = 4194445, Kp8 = 4194446, Kp9 = 4194447,
    Space = 32,
    // Punctuation is its ASCII code (the US layout's key).
    Apostrophe = 39, Comma = 44, Minus = 45, Period = 46, Slash = 47, Semicolon = 59, Equal = 61,
    Bracketleft = 91, Backslash = 92, Bracketright = 93, Quoteleft = 96,
    Key0 = 48, Key1 = 49, Key2 = 50, Key3 = 51, Key4 = 52, Key5 = 53, Key6 = 54, Key7 = 55, Key8 = 56, Key9 = 57,
    A = 65, B = 66, C = 67, D = 68, E = 69, F = 70, G = 71, H = 72, I = 73, J = 74, K = 75, L = 76, M = 77,
    N = 78, O = 79, P = 80, Q = 81, R = 82, S = 83, T = 84, U = 85, V = 86, W = 87, X = 88, Y = 89, Z = 90,
}

/// <summary>Modifier keys held with a key or a click.</summary>
[Flags]
public enum KeyModifierMask : long
{
    None = 0, CodeMask = 8388607, ModifierMask = 532676608,
    Shift = 33554432, Alt = 67108864, Meta = 134217728, Ctrl = 268435456, Kpad = 536870912, GroupSwitch = 1073741824,
}

public enum MouseButton { None = 0, Left = 1, Right = 2, Middle = 3, WheelUp = 4, WheelDown = 5, WheelLeft = 6, WheelRight = 7, Xbutton1 = 8, Xbutton2 = 9 }

[Flags]
public enum MouseButtonMask { Left = 1, Right = 2, Middle = 4, MbXbutton1 = 128, MbXbutton2 = 256 }

public enum JoyButton
{
    Invalid = -1, A = 0, B = 1, X = 2, Y = 3, Back = 4, Guide = 5, Start = 6, LeftStick = 7, RightStick = 8,
    LeftShoulder = 9, RightShoulder = 10, DpadUp = 11, DpadDown = 12, DpadLeft = 13, DpadRight = 14, Misc1 = 15,
    Paddle1 = 16, Paddle2 = 17, Paddle3 = 18, Paddle4 = 19, Touchpad = 20,
}

public enum JoyAxis { Invalid = -1, LeftX = 0, LeftY = 1, RightX = 2, RightY = 3, TriggerLeft = 4, TriggerRight = 5 }

/// <summary>Base of every input event (Godot's InputEvent).</summary>
public abstract class InputEvent : RefCounted
{
    /// <summary>The event being delivered right now (set while a node's _Input or _UnhandledInput runs).</summary>
    [ThreadStatic] internal static InputEvent? Current;

    /// <summary>The engine event this one was made from, if any (a handled mark travels back to it).</summary>
    internal Aver.Framework.InputEvent? Source;
    private bool _handled;

    public int Device { get; set; }

    internal bool IsHandled => _handled || (Source?.Handled ?? false);

    internal void MarkHandled()
    {
        _handled = true;
        if (Source != null) Aver.Framework.Hosting.ExtensionHost.MarkHandled(Source);
    }

    /// <summary>True for a press (a key, button or action going down, or an axis past the action's dead zone is a separate question).</summary>
    public virtual bool IsPressed() => false;

    /// <summary>True for the release of a key, button or action.</summary>
    public virtual bool IsReleased() => false;

    public virtual bool IsEcho() => false;

    /// <summary>Whether the event is a press or release rather than motion.</summary>
    public virtual bool IsActionType() => false;

    /// <summary>Whether this event triggers the action (it is one of the action's events, with the modifiers it needs).</summary>
    public bool IsAction(StringName action, bool exactMatch = false) => InputMap.EventIsAction(this, action, exactMatch);

    public bool IsActionPressed(StringName action, bool allowEcho = false, bool exactMatch = false) =>
        InputMap.EventGetActionStatus(this, action, exactMatch, out bool pressed, out _, out _) && pressed && IsPressed() && (allowEcho || !IsEcho());

    public bool IsActionReleased(StringName action, bool exactMatch = false) =>
        InputMap.EventGetActionStatus(this, action, exactMatch, out bool pressed, out _, out _) && !pressed && IsReleased();

    public float GetActionStrength(StringName action, bool exactMatch = false) =>
        InputMap.EventGetActionStatus(this, action, exactMatch, out _, out float strength, out _) ? strength : 0f;

    /// <summary>Whether <paramref name="other"/> is the same input (the same key, button or axis).</summary>
    public virtual bool IsMatch(InputEvent other, bool exactMatch = true) => false;

    /// <summary>How an action's stored event reads an incoming one: matches, whether it is a press, and with what strength.</summary>
    internal virtual bool ActionMatch(InputEvent incoming, bool exactMatch, float deadzone, out bool pressed, out float strength, out float rawStrength)
    {
        pressed = false; strength = 0f; rawStrength = 0f;
        return false;
    }

    public virtual string AsText() => GetClass();

    public override string ToString() => AsText();
}

/// <summary>An event that may carry modifier keys (Godot's InputEventWithModifiers).</summary>
public abstract class InputEventWithModifiers : InputEvent
{
    public bool ShiftPressed { get; set; }
    public bool CtrlPressed { get; set; }
    public bool AltPressed { get; set; }
    public bool MetaPressed { get; set; }

    /// <summary>The modifiers as a mask.</summary>
    public KeyModifierMask GetModifiersMask() =>
        (ShiftPressed ? KeyModifierMask.Shift : 0) | (CtrlPressed ? KeyModifierMask.Ctrl : 0) |
        (AltPressed ? KeyModifierMask.Alt : 0) | (MetaPressed ? KeyModifierMask.Meta : 0);

    // The stored event's modifiers must all be held in the incoming one; an exact match wants the same set.
    internal bool ModifiersAccept(InputEventWithModifiers incoming, bool exactMatch)
    {
        KeyModifierMask mine = GetModifiersMask(), theirs = incoming.GetModifiersMask();
        return exactMatch ? mine == theirs : (mine & theirs) == mine;
    }
}

/// <summary>A key going down or up (Godot's InputEventKey).</summary>
public class InputEventKey : InputEventWithModifiers
{
    public Key Keycode { get; set; }
    public Key PhysicalKeycode { get; set; }
    public Key KeyLabel { get; set; }
    public uint Unicode { get; set; }
    public bool Pressed { get; set; }
    public bool Echo { get; set; }

    public override bool IsPressed() => Pressed;
    public override bool IsReleased() => !Pressed;
    public override bool IsEcho() => Echo;
    public override bool IsActionType() => true;

    /// <summary>The key code the event is about: the key itself, else the physical key.</summary>
    public Key GetKeycodeWithModifiers() => Keycode != Key.None ? Keycode : PhysicalKeycode;

    public override bool IsMatch(InputEvent other, bool exactMatch = true) =>
        other is InputEventKey k && KeyOf(this) == KeyOf(k) && ModifiersAccept(k, exactMatch);

    private static Key KeyOf(InputEventKey k) => k.Keycode != Key.None ? k.Keycode : k.PhysicalKeycode;

    internal override bool ActionMatch(InputEvent incoming, bool exactMatch, float deadzone, out bool pressed, out float strength, out float rawStrength)
    {
        pressed = false; strength = 0f; rawStrength = 0f;
        if (incoming is not InputEventKey k) return false;
        bool match = Keycode == Key.None ? PhysicalKeycode == k.PhysicalKeycode : Keycode == k.Keycode;
        if (Keycode == Key.None && PhysicalKeycode == Key.None) return false;
        match = match && ModifiersAccept(k, exactMatch);
        if (!match) return false;
        pressed = k.Pressed;
        strength = rawStrength = pressed ? 1f : 0f;
        return true;
    }

    public override string AsText()
    {
        string text = KeyOf(this).ToString();
        if (AltPressed) text = "Alt+" + text;
        if (ShiftPressed) text = "Shift+" + text;
        if (CtrlPressed) text = "Ctrl+" + text;
        if (MetaPressed) text = "Meta+" + text;
        return text;
    }
}

/// <summary>Base of the pointer events (Godot's InputEventMouse).</summary>
public abstract class InputEventMouse : InputEventWithModifiers
{
    public MouseButtonMask ButtonMask { get; set; }
    public Vector2 Position { get; set; }
    public Vector2 GlobalPosition { get; set; }
}

/// <summary>A mouse button going down or up, or a wheel notch (Godot's InputEventMouseButton).</summary>
public class InputEventMouseButton : InputEventMouse
{
    public MouseButton ButtonIndex { get; set; }
    public bool Pressed { get; set; }
    public bool DoubleClick { get; set; }
    public float Factor { get; set; } = 1f;
    public bool Canceled { get; set; }

    public override bool IsPressed() => Pressed;
    public override bool IsReleased() => !Pressed;
    public override bool IsActionType() => true;

    public override bool IsMatch(InputEvent other, bool exactMatch = true) =>
        other is InputEventMouseButton b && ButtonIndex == b.ButtonIndex && ModifiersAccept(b, exactMatch);

    internal override bool ActionMatch(InputEvent incoming, bool exactMatch, float deadzone, out bool pressed, out float strength, out float rawStrength)
    {
        pressed = false; strength = 0f; rawStrength = 0f;
        if (incoming is not InputEventMouseButton b || ButtonIndex != b.ButtonIndex || !ModifiersAccept(b, exactMatch)) return false;
        pressed = b.Pressed;
        strength = rawStrength = pressed ? 1f : 0f;
        return true;
    }

    public override string AsText() => "Mouse Button " + ButtonIndex;
}

/// <summary>The pointer moving (Godot's InputEventMouseMotion).</summary>
public class InputEventMouseMotion : InputEventMouse
{
    /// <summary>The movement since the last motion event, in viewport pixels.</summary>
    public Vector2 Relative { get; set; }
    public Vector2 ScreenRelative { get; set; }
    /// <summary>Pixels per second.</summary>
    public Vector2 Velocity { get; set; }
    public Vector2 ScreenVelocity { get; set; }
    public float Pressure { get; set; }

    public override string AsText() => $"Mouse motion relative ({Relative.X}, {Relative.Y})";
}

/// <summary>A finger touching or leaving the screen (Godot's InputEventScreenTouch). They come from a real touch screen where the window layer has one (SDL),
/// from the mouse when <see cref="Input.EmulateTouchFromMouse"/> is on, and from <see cref="Input.ParseInputEvent"/>.</summary>
public class InputEventScreenTouch : InputEvent
{
    /// <summary>Which finger (0 for the first).</summary>
    public int Index { get; set; }
    public Vector2 Position { get; set; }
    public bool Pressed { get; set; }
    public bool Canceled { get; set; }
    public bool DoubleTap { get; set; }

    public override bool IsPressed() => Pressed;
    public override bool IsReleased() => !Pressed;
    public override bool IsActionType() => true;

    public override string AsText() => $"Screen {(Pressed ? "touched" : "released")} at ({Position.X}, {Position.Y}) index {Index}";
}

/// <summary>A finger moving on the screen (Godot's InputEventScreenDrag).</summary>
public class InputEventScreenDrag : InputEvent
{
    public int Index { get; set; }
    public Vector2 Position { get; set; }
    /// <summary>The movement since the last drag event, in viewport pixels.</summary>
    public Vector2 Relative { get; set; }
    public Vector2 ScreenRelative { get; set; }
    /// <summary>Pixels per second.</summary>
    public Vector2 Velocity { get; set; }
    public Vector2 ScreenVelocity { get; set; }
    public float Pressure { get; set; }
    public Vector2 Tilt { get; set; }
    public bool PenInverted { get; set; }

    public override string AsText() => $"Screen dragged index {Index} at ({Position.X}, {Position.Y}) relative ({Relative.X}, {Relative.Y})";
}

/// <summary>A gamepad button going down or up (Godot's InputEventJoypadButton).</summary>
public class InputEventJoypadButton : InputEvent
{
    public JoyButton ButtonIndex { get; set; }
    public float Pressure { get; set; }
    public bool Pressed { get; set; }

    public override bool IsPressed() => Pressed;
    public override bool IsReleased() => !Pressed;
    public override bool IsActionType() => true;

    public override bool IsMatch(InputEvent other, bool exactMatch = true) => other is InputEventJoypadButton b && ButtonIndex == b.ButtonIndex;

    internal override bool ActionMatch(InputEvent incoming, bool exactMatch, float deadzone, out bool pressed, out float strength, out float rawStrength)
    {
        pressed = false; strength = 0f; rawStrength = 0f;
        if (incoming is not InputEventJoypadButton b || ButtonIndex != b.ButtonIndex) return false;
        pressed = b.Pressed;
        strength = rawStrength = pressed ? 1f : 0f;
        return true;
    }

    public override string AsText() => "Joypad Button " + ButtonIndex;
}

/// <summary>A gamepad axis moving (Godot's InputEventJoypadMotion). The value is -1 to 1; a stick pushed down reads positive.</summary>
public class InputEventJoypadMotion : InputEvent
{
    public JoyAxis Axis { get; set; }
    public float AxisValue { get; set; }

    public override bool IsMatch(InputEvent other, bool exactMatch = true) =>
        other is InputEventJoypadMotion m && Axis == m.Axis && ((AxisValue < 0) == (m.AxisValue < 0) || m.AxisValue == 0);

    internal override bool ActionMatch(InputEvent incoming, bool exactMatch, float deadzone, out bool pressed, out float strength, out float rawStrength)
    {
        pressed = false; strength = 0f; rawStrength = 0f;
        if (incoming is not InputEventJoypadMotion m || Axis != m.Axis) return false;
        float abs = MathF.Abs(m.AxisValue);
        bool sameDirection = (AxisValue < 0) == (m.AxisValue < 0) || m.AxisValue == 0;
        pressed = sameDirection && abs >= deadzone;
        strength = pressed ? (deadzone == 1f ? 1f : Math.Clamp((abs - deadzone) / (1f - deadzone), 0f, 1f)) : 0f;
        rawStrength = sameDirection ? abs : 0f;
        return true;
    }

    public override string AsText() => $"Joypad Motion Axis {Axis} value {AxisValue}";
}

/// <summary>An action pressed or released by name (Godot's InputEventAction).</summary>
public class InputEventAction : InputEvent
{
    public StringName Action { get; set; } = "";
    public bool Pressed { get; set; }
    public float Strength { get; set; } = 1f;
    public int EventIndex { get; set; } = -1;

    public override bool IsPressed() => Pressed;
    public override bool IsReleased() => !Pressed;
    public override bool IsActionType() => true;

    public override bool IsMatch(InputEvent other, bool exactMatch = true) => other is InputEventAction a && Action == a.Action;

    internal override bool ActionMatch(InputEvent incoming, bool exactMatch, float deadzone, out bool pressed, out float strength, out float rawStrength)
    {
        pressed = false; strength = 0f; rawStrength = 0f;
        if (incoming is not InputEventAction a || Action != a.Action) return false;
        pressed = a.Pressed;
        strength = rawStrength = pressed ? a.Strength : 0f;
        return true;
    }

    public override string AsText() => "InputEventAction : action=\"" + Action + "\", pressed=" + (Pressed ? "true" : "false");
}

/// <summary>Turns the engine's events (G1) into Godot's, and the other way, for the keys, buttons and axes both know.</summary>
internal static class InputBridge
{
    private static readonly ConditionalWeakTable<Aver.Framework.InputEvent, InputEvent> s_wrapped = new();

    // Aver's key set against Godot's key codes.
    private static readonly (Aver.Framework.Key Aver, Key Godot)[] s_keys = BuildKeys();

    private static (Aver.Framework.Key, Key)[] BuildKeys()
    {
        var list = new List<(Aver.Framework.Key, Key)>();
        for (int i = 0; i < 26; i++) list.Add(((Aver.Framework.Key)(int)Aver.Framework.Key.A + i, (Key)((int)Key.A + i)));
        for (int i = 0; i < 10; i++) list.Add(((Aver.Framework.Key)((int)Aver.Framework.Key.D0 + i), (Key)((int)Key.Key0 + i)));
        list.Add((Aver.Framework.Key.Space, Key.Space));
        list.Add((Aver.Framework.Key.LeftShift, Key.Shift));
        list.Add((Aver.Framework.Key.LeftCtrl, Key.Ctrl));
        list.Add((Aver.Framework.Key.LeftAlt, Key.Alt));
        list.Add((Aver.Framework.Key.Enter, Key.Enter));
        list.Add((Aver.Framework.Key.Escape, Key.Escape));
        list.Add((Aver.Framework.Key.Tab, Key.Tab));
        list.Add((Aver.Framework.Key.Left, Key.Left));
        list.Add((Aver.Framework.Key.Right, Key.Right));
        list.Add((Aver.Framework.Key.Up, Key.Up));
        list.Add((Aver.Framework.Key.Down, Key.Down));
        for (int i = 0; i < 12; i++) list.Add(((Aver.Framework.Key)((int)Aver.Framework.Key.F1 + i), (Key)((long)Key.F1 + i)));
        for (int i = 0; i < 10; i++) list.Add(((Aver.Framework.Key)((int)Aver.Framework.Key.Kp0 + i), (Key)((long)Key.Kp0 + i)));
        list.Add((Aver.Framework.Key.PageUp, Key.Pageup));
        list.Add((Aver.Framework.Key.PageDown, Key.Pagedown));
        list.Add((Aver.Framework.Key.Home, Key.Home));
        list.Add((Aver.Framework.Key.End, Key.End));
        list.Add((Aver.Framework.Key.Insert, Key.Insert));
        list.Add((Aver.Framework.Key.Delete, Key.Delete));
        list.Add((Aver.Framework.Key.Backspace, Key.Backspace));
        list.Add((Aver.Framework.Key.CapsLock, Key.Capslock));
        list.Add((Aver.Framework.Key.NumLock, Key.Numlock));
        list.Add((Aver.Framework.Key.ScrollLock, Key.Scrolllock));
        list.Add((Aver.Framework.Key.Pause, Key.Pause));
        list.Add((Aver.Framework.Key.PrintScreen, Key.Print));
        list.Add((Aver.Framework.Key.Menu, Key.Menu));
        list.Add((Aver.Framework.Key.Meta, Key.Meta));
        list.Add((Aver.Framework.Key.Minus, Key.Minus));
        list.Add((Aver.Framework.Key.Equal, Key.Equal));
        list.Add((Aver.Framework.Key.LBracket, Key.Bracketleft));
        list.Add((Aver.Framework.Key.RBracket, Key.Bracketright));
        list.Add((Aver.Framework.Key.Backslash, Key.Backslash));
        list.Add((Aver.Framework.Key.Semicolon, Key.Semicolon));
        list.Add((Aver.Framework.Key.Apostrophe, Key.Apostrophe));
        list.Add((Aver.Framework.Key.Comma, Key.Comma));
        list.Add((Aver.Framework.Key.Period, Key.Period));
        list.Add((Aver.Framework.Key.Slash, Key.Slash));
        list.Add((Aver.Framework.Key.Grave, Key.Quoteleft));
        list.Add((Aver.Framework.Key.KpAdd, Key.KpAdd));
        list.Add((Aver.Framework.Key.KpSubtract, Key.KpSubtract));
        list.Add((Aver.Framework.Key.KpMultiply, Key.KpMultiply));
        list.Add((Aver.Framework.Key.KpDivide, Key.KpDivide));
        list.Add((Aver.Framework.Key.KpDecimal, Key.KpPeriod));
        list.Add((Aver.Framework.Key.KpEnter, Key.KpEnter));
        return list.ToArray();
    }

    internal static bool TryGodotKey(Aver.Framework.Key key, out Key godot)
    {
        foreach (var (a, g) in s_keys) if (a == key) { godot = g; return true; }
        godot = Key.None;
        return false;
    }

    internal static bool TryAverKey(Key key, out Aver.Framework.Key aver)
    {
        foreach (var (a, g) in s_keys) if (g == key) { aver = a; return true; }
        aver = default;
        return false;
    }

    internal static JoyButton ToJoyButton(Aver.Framework.GamepadButton b) => b switch
    {
        Aver.Framework.GamepadButton.A => JoyButton.A,
        Aver.Framework.GamepadButton.B => JoyButton.B,
        Aver.Framework.GamepadButton.X => JoyButton.X,
        Aver.Framework.GamepadButton.Y => JoyButton.Y,
        Aver.Framework.GamepadButton.Back => JoyButton.Back,
        Aver.Framework.GamepadButton.Start => JoyButton.Start,
        Aver.Framework.GamepadButton.LeftThumb => JoyButton.LeftStick,
        Aver.Framework.GamepadButton.RightThumb => JoyButton.RightStick,
        Aver.Framework.GamepadButton.LeftShoulder => JoyButton.LeftShoulder,
        Aver.Framework.GamepadButton.RightShoulder => JoyButton.RightShoulder,
        Aver.Framework.GamepadButton.DPadUp => JoyButton.DpadUp,
        Aver.Framework.GamepadButton.DPadDown => JoyButton.DpadDown,
        Aver.Framework.GamepadButton.DPadLeft => JoyButton.DpadLeft,
        Aver.Framework.GamepadButton.DPadRight => JoyButton.DpadRight,
        Aver.Framework.GamepadButton.Guide => JoyButton.Guide,
        Aver.Framework.GamepadButton.Misc1 => JoyButton.Misc1,
        Aver.Framework.GamepadButton.Paddle1 => JoyButton.Paddle1,
        Aver.Framework.GamepadButton.Paddle2 => JoyButton.Paddle2,
        Aver.Framework.GamepadButton.Paddle3 => JoyButton.Paddle3,
        Aver.Framework.GamepadButton.Paddle4 => JoyButton.Paddle4,
        Aver.Framework.GamepadButton.Touchpad => JoyButton.Touchpad,
        _ => JoyButton.Invalid,
    };

    internal static bool TryAverButton(JoyButton b, out Aver.Framework.GamepadButton aver)
    {
        foreach (Aver.Framework.GamepadButton g in Enum.GetValues<Aver.Framework.GamepadButton>())
            if (ToJoyButton(g) == b) { aver = g; return true; }
        aver = default;
        return false;
    }

    internal static JoyAxis ToJoyAxis(Aver.Framework.GamepadAxis a) => a switch
    {
        Aver.Framework.GamepadAxis.LeftX => JoyAxis.LeftX,
        Aver.Framework.GamepadAxis.LeftY => JoyAxis.LeftY,
        Aver.Framework.GamepadAxis.RightX => JoyAxis.RightX,
        Aver.Framework.GamepadAxis.RightY => JoyAxis.RightY,
        Aver.Framework.GamepadAxis.LeftTrigger => JoyAxis.TriggerLeft,
        Aver.Framework.GamepadAxis.RightTrigger => JoyAxis.TriggerRight,
        _ => JoyAxis.Invalid,
    };

    // XInput's stick Y is positive up and Godot's is positive down.
    internal static float AxisValueFromAver(JoyAxis axis, float value) => axis is JoyAxis.LeftY or JoyAxis.RightY ? -value : value;

    internal static MouseButton ToMouseButton(Aver.Framework.Key k) => k switch
    {
        Aver.Framework.Key.MouseLeft => MouseButton.Left,
        Aver.Framework.Key.MouseRight => MouseButton.Right,
        Aver.Framework.Key.MouseMiddle => MouseButton.Middle,
        Aver.Framework.Key.MouseX1 => MouseButton.Xbutton1,
        Aver.Framework.Key.MouseX2 => MouseButton.Xbutton2,
        Aver.Framework.Key.MouseWheelUp => MouseButton.WheelUp,
        Aver.Framework.Key.MouseWheelDown => MouseButton.WheelDown,
        Aver.Framework.Key.MouseWheelLeft => MouseButton.WheelLeft,
        Aver.Framework.Key.MouseWheelRight => MouseButton.WheelRight,
        _ => MouseButton.None,
    };

    /// <summary>The engine key slot a Godot mouse button reads (the wheel directions are slots that a notch presses for a frame).</summary>
    internal static bool TryAverMouse(MouseButton b, out Aver.Framework.Key key)
    {
        key = b switch
        {
            MouseButton.Left => Aver.Framework.Key.MouseLeft, MouseButton.Right => Aver.Framework.Key.MouseRight, MouseButton.Middle => Aver.Framework.Key.MouseMiddle,
            MouseButton.Xbutton1 => Aver.Framework.Key.MouseX1, MouseButton.Xbutton2 => Aver.Framework.Key.MouseX2,
            MouseButton.WheelUp => Aver.Framework.Key.MouseWheelUp, MouseButton.WheelDown => Aver.Framework.Key.MouseWheelDown,
            MouseButton.WheelLeft => Aver.Framework.Key.MouseWheelLeft, MouseButton.WheelRight => Aver.Framework.Key.MouseWheelRight,
            _ => default,
        };
        return b is not MouseButton.None;
    }

    /// <summary>The Godot event for an engine event: the same object each time it is asked, so a handled mark is shared.</summary>
    internal static InputEvent FromFramework(Aver.Framework.InputEvent e)
    {
        if (s_wrapped.TryGetValue(e, out InputEvent? hit)) return hit;
        InputEvent made = Make(e);
        made.Source = e;
        s_wrapped.AddOrUpdate(e, made);
        return made;
    }

    private static InputEvent Make(Aver.Framework.InputEvent e)
    {
        switch (e)
        {
            case Aver.Framework.InputEventKey k:
            {
                TryGodotKey(k.Key, out Key g);
                return new InputEventKey { Keycode = g, PhysicalKeycode = g, KeyLabel = g, Pressed = k.IsPressed, ShiftPressed = Input.HeldModifier(KeyModifierMask.Shift), CtrlPressed = Input.HeldModifier(KeyModifierMask.Ctrl), AltPressed = Input.HeldModifier(KeyModifierMask.Alt) };
            }
            case Aver.Framework.InputEventMouseButton b:
                return new InputEventMouseButton { ButtonIndex = ToMouseButton(b.Button), Pressed = b.IsPressed, Position = Input.MousePosition, GlobalPosition = Input.MousePosition };
            case Aver.Framework.InputEventMouseWheel w when w.Horizontal != 0f && w.Delta == 0f:
                return new InputEventMouseButton { ButtonIndex = w.Horizontal > 0 ? MouseButton.WheelRight : MouseButton.WheelLeft, Pressed = true, Factor = MathF.Abs(w.Horizontal), Position = Input.MousePosition, GlobalPosition = Input.MousePosition };
            case Aver.Framework.InputEventMouseWheel w:
                return new InputEventMouseButton { ButtonIndex = w.Delta > 0 ? MouseButton.WheelUp : MouseButton.WheelDown, Pressed = true, Factor = MathF.Abs(w.Delta), Position = Input.MousePosition, GlobalPosition = Input.MousePosition };
            case Aver.Framework.InputEventTouch t:
                return new InputEventScreenTouch { Index = t.Finger, Position = new Vector2(t.X, t.Y), Pressed = t.IsPressed };
            case Aver.Framework.InputEventTouchMotion tm:
            {
                var rel = new Vector2(tm.RelativeX, tm.RelativeY);
                float dt = Input.FrameDelta > 0 ? (float)Input.FrameDelta : 1f / 60f;
                return new InputEventScreenDrag { Index = tm.Finger, Position = new Vector2(tm.X, tm.Y), Relative = rel, ScreenRelative = rel, Velocity = rel / dt, ScreenVelocity = rel / dt, Pressure = tm.Pressure };
            }
            case Aver.Framework.InputEventMouseMotion m:
            {
                var rel = new Vector2(m.RelativeX, m.RelativeY);
                float dt = Input.FrameDelta > 0 ? (float)Input.FrameDelta : 1f / 60f;
                return new InputEventMouseMotion { Relative = rel, ScreenRelative = rel, Velocity = rel / dt, ScreenVelocity = rel / dt, Position = Input.MousePosition, GlobalPosition = Input.MousePosition };
            }
            case Aver.Framework.InputEventJoypadButton jb:
                return new InputEventJoypadButton { ButtonIndex = ToJoyButton(jb.Button), Pressed = jb.IsPressed, Pressure = jb.IsPressed ? 1f : 0f, Device = jb.Pad };
            case Aver.Framework.InputEventJoypadMotion jm:
            {
                JoyAxis axis = ToJoyAxis(jm.Axis);
                return new InputEventJoypadMotion { Axis = axis, AxisValue = AxisValueFromAver(axis, jm.Value), Device = jm.Pad };
            }
            default:
                return new InputEventAction();
        }
    }

    /// <summary>The engine event for a Godot one, when the engine has such an event; null otherwise.</summary>
    internal static Aver.Framework.InputEvent? ToFramework(InputEvent e)
    {
        switch (e)
        {
            case InputEventKey k when TryAverKey(k.GetKeycodeWithModifiers(), out Aver.Framework.Key ak):
                return new Aver.Framework.InputEventKey(ak, k.Pressed);
            case InputEventMouseButton w when w.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown:
                return new Aver.Framework.InputEventMouseWheel(w.ButtonIndex == MouseButton.WheelUp ? w.Factor : -w.Factor);
            case InputEventMouseButton w when w.ButtonIndex is MouseButton.WheelLeft or MouseButton.WheelRight:
                return new Aver.Framework.InputEventMouseWheel(0f, w.ButtonIndex == MouseButton.WheelRight ? w.Factor : -w.Factor);
            case InputEventMouseButton b when TryAverMouse(b.ButtonIndex, out Aver.Framework.Key mk):
                return new Aver.Framework.InputEventMouseButton(mk, b.Pressed);
            case InputEventMouseMotion m:
                return new Aver.Framework.InputEventMouseMotion(m.Relative.X, m.Relative.Y);
            case InputEventScreenTouch t:
                return new Aver.Framework.InputEventTouch(t.Index, t.Pressed, t.Position.X, t.Position.Y);
            case InputEventScreenDrag d:
                return new Aver.Framework.InputEventTouchMotion(d.Index, d.Position.X, d.Position.Y, d.Relative.X, d.Relative.Y, d.Pressure);
            case InputEventJoypadButton jb when TryAverButton(jb.ButtonIndex, out Aver.Framework.GamepadButton ab):
                return new Aver.Framework.InputEventJoypadButton(ab, jb.Pressed, jb.Device);
            default:
                return null;
        }
    }
}
