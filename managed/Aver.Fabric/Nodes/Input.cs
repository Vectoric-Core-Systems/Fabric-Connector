// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// The Input singleton and InputMap, with Godot's names. Two sources feed them: the engine (the project's input scheme, which the
// converter wrote from project.godot, read through the framework's actions and polled keys) and a managed layer that takes events
// scripts feed in (ParseInputEvent, ActionPress) the way Godot's input map does. Without the engine only the managed layer exists.

using Aver.Framework;

namespace Aver.Fabric;

/// <summary>The project's actions and the events that trigger them (Godot's InputMap).</summary>
public static partial class InputMap
{
    private sealed class ManagedAction
    {
        public float Deadzone = 0.5f;
        public readonly List<InputEvent> Events = new();
    }

    private static readonly Dictionary<string, ManagedAction> s_actions = new(StringComparer.Ordinal);

    internal static void ResetForTests()
    {
        s_actions.Clear();
        s_erasedBuiltins.Clear();
    }

    private static bool EngineHas(string name)
    {
        if (!NodeHost.Current.IsEngine) return false;
        try { return InputAction.Find(name) != null; }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    public static bool HasAction(StringName action)
    {
        string name = action.ToString();
        return s_actions.ContainsKey(name) || EngineHas(name) || IsBuiltin(name);
    }

    /// <summary>Adds an action with no events. A name that exists already is an error and changes nothing.</summary>
    public static void AddAction(StringName action, float deadzone = 0.5f)
    {
        if (HasAction(action))
        {
            Aver.Scripting.Log.Error($"[Fabric] InputMap.AddAction: the action \"{action}\" exists already");
            return;
        }
        s_actions[action.ToString()] = new ManagedAction { Deadzone = deadzone };
    }

    /// <summary>Removes an action: one script added, or one of Godot's built-in ui_* actions. The project's own actions (the engine's) cannot be removed here.</summary>
    public static void EraseAction(StringName action)
    {
        string name = action.ToString();
        if (s_actions.Remove(name)) { if (Builtins.ContainsKey(name)) s_erasedBuiltins.Add(name); return; }
        if (IsBuiltin(name) && !EngineHas(name)) { s_erasedBuiltins.Add(name); return; }
        Aver.Scripting.Log.Error($"[Fabric] InputMap.EraseAction: no action \"{action}\"");
    }

    /// <summary>Every action: the project's, Godot's built-in ui_* ones, and the ones script added.</summary>
    public static Collections.Array<StringName> GetActions()
    {
        var names = new List<string>();
        if (NodeHost.Current.IsEngine)
            names.AddRange(Aver.Framework.Hosting.ExtensionHost.RegisteredActionNames());
        foreach (string k in s_actions.Keys) if (!names.Contains(k)) names.Add(k);
        foreach (string k in Builtins.Keys) if (!s_erasedBuiltins.Contains(k) && !names.Contains(k)) names.Add(k);
        return new Collections.Array<StringName>(names.Select(s => new StringName(s)));
    }

    // The managed record of an action that exists. A built-in action no script has touched has none yet: it gets one holding Godot's default events, which
    // the script's change then applies to. False when there is no such action.
    private static bool TryManaged(string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ManagedAction? action)
    {
        if (s_actions.TryGetValue(name, out action)) return true;
        if (IsBuiltinActive(name))
        {
            action = s_actions[name] = new ManagedAction();
            action.Events.AddRange(CreateBuiltinEvents(name));
            return true;
        }
        action = null;
        return false;
    }

    public static void ActionSetDeadzone(StringName action, float deadzone)
    {
        if (TryManaged(action.ToString(), out ManagedAction? a)) a.Deadzone = deadzone;
        else Aver.Scripting.Log.Error($"[Fabric] InputMap.ActionSetDeadzone: no action \"{action}\" that script added");
    }

    public static float ActionGetDeadzone(StringName action) =>
        s_actions.TryGetValue(action.ToString(), out ManagedAction? a) ? a.Deadzone : 0.5f;

    public static void ActionAddEvent(StringName action, InputEvent @event)
    {
        string name = action.ToString();
        if (!TryManaged(name, out ManagedAction? a))
        {
            if (!HasAction(action)) { Aver.Scripting.Log.Error($"[Fabric] InputMap.ActionAddEvent: no action \"{action}\""); return; }
            a = s_actions[name] = new ManagedAction();
        }
        if (!a.Events.Contains(@event)) a.Events.Add(@event);
    }

    public static bool ActionHasEvent(StringName action, InputEvent @event)
    {
        string name = action.ToString();
        IEnumerable<InputEvent> events = s_actions.TryGetValue(name, out ManagedAction? a) ? a.Events : IsBuiltinActive(name) ? BuiltinEvents(name) : System.Array.Empty<InputEvent>();
        return events.Any(e => ReferenceEquals(e, @event) || (e.GetType() == @event.GetType() && e.IsMatch(@event, true) && @event.IsMatch(e, true)));
    }

    public static void ActionEraseEvent(StringName action, InputEvent @event)
    {
        if (TryManaged(action.ToString(), out ManagedAction? a))
            a.Events.RemoveAll(e => ReferenceEquals(e, @event) || (e.GetType() == @event.GetType() && e.IsMatch(@event, true) && @event.IsMatch(e, true)));
    }

    public static void ActionEraseEvents(StringName action)
    {
        if (TryManaged(action.ToString(), out ManagedAction? a)) a.Events.Clear();
    }

    /// <summary>The events that trigger the action: the ones script added (or, for a built-in ui_* action no script has changed, Godot's defaults), then the project's
    /// bindings (the engine's).</summary>
    public static Collections.Array<InputEvent> ActionGetEvents(StringName action)
    {
        string name = action.ToString();
        var list = new List<InputEvent>();
        if (s_actions.TryGetValue(name, out ManagedAction? a)) list.AddRange(a.Events);
        else if (IsBuiltinActive(name)) list.AddRange(BuiltinEvents(name));
        list.AddRange(NativeEvents(name));
        return new Collections.Array<InputEvent>(list);
    }

    // The project's bindings of an action, as Godot events.
    private static IEnumerable<InputEvent> NativeEvents(string name)
    {
        if (!NodeHost.Current.IsEngine) yield break;
        for (int slot = 0; slot < 16; slot++)
        {
            if (!EnhancedInput.TryGetBindingKey(name, slot, out int raw, out InputSource source)) yield break;
            InputEvent? e = source switch
            {
                InputSource.Key when InputBridge.TryGodotKey((Aver.Framework.Key)raw, out Key g) => new InputEventKey { Keycode = g, PhysicalKeycode = g },
                InputSource.Key when InputBridge.ToMouseButton((Aver.Framework.Key)raw) is var mb && mb != MouseButton.None => new InputEventMouseButton { ButtonIndex = mb },
                InputSource.MouseWheel => new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp },
                InputSource.GamepadButton => new InputEventJoypadButton { ButtonIndex = InputBridge.ToJoyButton((Aver.Framework.GamepadButton)raw) },
                InputSource.GamepadAxis => new InputEventJoypadMotion { Axis = InputBridge.ToJoyAxis((Aver.Framework.GamepadAxis)raw), AxisValue = 1f },
                _ => null,
            };
            if (e != null) yield return e;
        }
    }

    /// <summary>Whether <paramref name="event"/> triggers the action.</summary>
    public static bool EventIsAction(InputEvent @event, StringName action, bool exactMatch = false) =>
        EventGetActionStatus(@event, action, exactMatch, out _, out _, out _);

    /// <summary>The action's reading of an event: whether any of its events matches it, and if so whether it is a press and how strong.</summary>
    internal static bool EventGetActionStatus(InputEvent @event, StringName action, bool exactMatch, out bool pressed, out float strength, out float rawStrength)
    {
        pressed = false; strength = 0f; rawStrength = 0f;
        string name = action.ToString();
        float deadzone = ActionGetDeadzone(name);
        bool found = false;
        if (@event is InputEventAction ia)
        {
            // An action event names its action; no stored event is needed.
            if (ia.Action != action) return false;
            pressed = ia.Pressed;
            strength = rawStrength = pressed ? ia.Strength : 0f;
            return true;
        }
        foreach (InputEvent stored in ActionGetEvents(name))
        {
            if (!stored.ActionMatch(@event, exactMatch, deadzone, out bool p, out float s, out float r)) continue;
            // Among several matching events a press wins over a release, the stronger over the weaker.
            if (!found || (p && !pressed) || (p == pressed && s > strength)) { pressed = p; strength = s; rawStrength = r; }
            found = true;
        }
        return found;
    }
}

/// <summary>Keyboard, mouse, gamepad and action state with Godot's names (Godot's Input singleton).</summary>
public static class Input
{
    public enum MouseModeEnum { Visible = 0, Hidden = 1, Captured = 2, Confined = 3, ConfinedHidden = 4 }

    private sealed class ActionState
    {
        public bool Pressed, Exact;
        public float Strength, RawStrength;
        public long PressedProcessFrame = -1, PressedPhysicsFrame = -1, ReleasedProcessFrame = -1, ReleasedPhysicsFrame = -1;
    }

    private static readonly Dictionary<string, ActionState> s_actions = new(StringComparer.Ordinal);
    private static readonly HashSet<Key> s_keys = new();
    private static readonly HashSet<MouseButton> s_mouseButtons = new();
    private static readonly HashSet<JoyButton> s_joyButtons = new();
    private static readonly Dictionary<JoyAxis, float> s_joyAxes = new();
    // Godot's pointer starts visible; Aver's own rule (captured while a session plays) is what a project gets until a script sets a mode.
    private static MouseModeEnum s_mouseMode = MouseModeEnum.Visible;
    private static bool s_pointerApplied;
    private static IDisposable? s_cursorScope;
    private static Vector2 s_mouseVelocity, s_mousePosition;
    private static readonly bool[] s_padSeen = new bool[Aver.Framework.Gamepads.MaxPads];
    private static CursorShape s_cursorShape;

    /// <summary>True while a physics pass runs (set by the driver): "just pressed" then means this physics frame, not this process frame.</summary>
    internal static bool InPhysicsFrame;

    /// <summary>The seconds of the frame being processed.</summary>
    internal static double FrameDelta { get; private set; }

    /// <summary>Where the pointer is, in window pixels: the engine's own reading (the position the UI sees), else where script-fed events last put it.</summary>
    internal static Vector2 MousePosition => EnginePointer(out Vector2 p) ? p : s_mousePosition;

    private static readonly float[] s_pointerBuf = new float[2];

    private static bool EnginePointer(out Vector2 p)
    {
        p = default;
        if (!NodeHost.Current.IsEngine || !UiNative.Available) return false;
        try
        {
            UiNative.aver_ui_pointer(s_pointerBuf);
            p = new Vector2(s_pointerBuf[0], s_pointerBuf[1]);
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    // A play session's end: the engine gets its free-cursor request back, and the next session starts from Godot's default pointer.
    internal static void EndSession()
    {
        try { s_cursorScope?.Dispose(); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        s_cursorScope = null;
        ResetForTests();
    }

    internal static void ResetForTests()
    {
        s_actions.Clear(); s_keys.Clear(); s_mouseButtons.Clear(); s_joyButtons.Clear(); s_joyAxes.Clear(); s_builtin.Clear();
        s_mouseVelocity = s_mousePosition = default;
        InPhysicsFrame = false;
        s_mouseMode = MouseModeEnum.Visible;
        s_pointerApplied = false;
        s_cursorShape = CursorShape.Arrow;
        System.Array.Clear(s_padSeen);
        s_joyHooks = null;
        s_touchEmulation = false;
        InputMap.ResetForTests();
    }

    // Per-frame bookkeeping, run first in each process pass by the tree's driver.
    internal static void BeginFrame(double delta)
    {
        FrameDelta = delta;
        InPhysicsFrame = false;
        if (s_builtin.Count > 0)
            foreach (string name in s_builtin.Keys.ToList()) BuiltinPoll(name, out _);
        if (NodeHost.Current.IsEngine)
        {
            // The first frame hands the engine Godot's default pointer (visible), which a script's own mode replaces.
            if (!s_pointerApplied) { s_pointerApplied = true; ApplyMouseMode(); }
            PollJoypads();
            SceneTree.Instance.Root.PollEngineSize();
        }
    }

    private static long ProcessFrameNow => NodeHost.Current.IsEngine ? Aver.Framework.Engine.ProcessFrames : SceneTree.Instance.GetFrame();
    private static long PhysicsFrameNow => NodeHost.Current.IsEngine ? Aver.Framework.Engine.PhysicsFrames : SceneTree.Instance.PhysicsFrameCount;

    internal static bool HeldModifier(KeyModifierMask m) => m switch
    {
        KeyModifierMask.Shift => IsKeyPressed(Key.Shift),
        KeyModifierMask.Ctrl => IsKeyPressed(Key.Ctrl),
        KeyModifierMask.Alt => IsKeyPressed(Key.Alt),
        _ => false,
    };

    // ---- actions ----

    private static InputAction? Native(string name)
    {
        if (!NodeHost.Current.IsEngine) return null;
        try { return InputAction.Find(name); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return null; }
    }

    // Godot's built-in ui_* actions that nobody changed have no entry in the engine or in s_actions: their default keys and buttons are read from the devices here.
    // The state is tracked per action, and refreshed each frame once an action has been asked about, so "just pressed" is the frame the key went down.
    private sealed class BuiltinState
    {
        public bool Held;
        public float Strength;
        public long PressedProcessFrame = -1, PressedPhysicsFrame = -1, ReleasedProcessFrame = -1, ReleasedPhysicsFrame = -1;
    }

    private static readonly Dictionary<string, BuiltinState> s_builtin = new(StringComparer.Ordinal);

    private static bool BuiltinPoll(string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out BuiltinState? state)
    {
        state = null;
        if (!InputMap.IsBuiltinActive(name)) return false;
        if (!s_builtin.TryGetValue(name, out state)) s_builtin[name] = state = new BuiltinState();
        float strength = 0f;
        foreach (InputEvent e in InputMap.BuiltinEvents(name))
        {
            switch (e)
            {
                case InputEventKey k:
                    if (!IsKeyPressed(k.Keycode)) break;
                    if ((k.CtrlPressed && !IsKeyPressed(Key.Ctrl)) || (k.ShiftPressed && !IsKeyPressed(Key.Shift)) || (k.AltPressed && !IsKeyPressed(Key.Alt))) break;
                    strength = 1f;
                    break;
                case InputEventJoypadButton jb:
                    if (IsJoyButtonPressed(0, jb.ButtonIndex)) strength = 1f;
                    break;
                case InputEventJoypadMotion jm:
                {
                    float v = GetJoyAxis(0, jm.Axis) * jm.AxisValue;
                    if (v > InputMap.ActionGetDeadzone(name)) strength = MathF.Max(strength, Math.Clamp(v, 0f, 1f));
                    break;
                }
            }
        }
        bool held = strength > 0f;
        if (held != state.Held)
        {
            state.Held = held;
            if (held) { state.PressedProcessFrame = ProcessFrameNow; state.PressedPhysicsFrame = PhysicsFrameNow; }
            else { state.ReleasedProcessFrame = ProcessFrameNow; state.ReleasedPhysicsFrame = PhysicsFrameNow; }
        }
        state.Strength = strength;
        return true;
    }

    public static bool IsActionPressed(StringName action, bool exactMatch = false)
    {
        string name = action.ToString();
        if (s_actions.TryGetValue(name, out ActionState? st) && st.Pressed && (!exactMatch || st.Exact)) return true;
        if (!exactMatch && BuiltinPoll(name, out BuiltinState? b) && b.Held) return true;
        return Native(name)?.IsHeld ?? false;
    }

    public static bool IsActionJustPressed(StringName action, bool exactMatch = false)
    {
        string name = action.ToString();
        if (s_actions.TryGetValue(name, out ActionState? st) && st.Pressed && (!exactMatch || st.Exact))
        {
            if (InPhysicsFrame ? st.PressedPhysicsFrame == PhysicsFrameNow : st.PressedProcessFrame == ProcessFrameNow) return true;
        }
        if (!exactMatch && BuiltinPoll(name, out BuiltinState? b) && b.Held && (InPhysicsFrame ? b.PressedPhysicsFrame == PhysicsFrameNow : b.PressedProcessFrame == ProcessFrameNow)) return true;
        return Native(name)?.WasPressed ?? false;
    }

    public static bool IsActionJustReleased(StringName action, bool exactMatch = false)
    {
        string name = action.ToString();
        if (s_actions.TryGetValue(name, out ActionState? st) && !st.Pressed && (!exactMatch || st.Exact))
        {
            if (InPhysicsFrame ? st.ReleasedPhysicsFrame == PhysicsFrameNow : st.ReleasedProcessFrame == ProcessFrameNow) return true;
        }
        if (!exactMatch && BuiltinPoll(name, out BuiltinState? b) && !b.Held && (InPhysicsFrame ? b.ReleasedPhysicsFrame == PhysicsFrameNow : b.ReleasedProcessFrame == ProcessFrameNow)) return true;
        return Native(name)?.WasReleased ?? false;
    }

    /// <summary>How strongly the action is pressed, 0 to 1, after the action's dead zone.</summary>
    public static float GetActionStrength(StringName action, bool exactMatch = false)
    {
        string name = action.ToString();
        float managed = s_actions.TryGetValue(name, out ActionState? st) && (!exactMatch || st.Exact) ? st.Strength : 0f;
        if (!exactMatch && BuiltinPoll(name, out BuiltinState? b)) managed = MathF.Max(managed, b.Strength);
        float native = Native(name) is { } a ? Math.Clamp(MathF.Abs(a.Value1D), 0f, 1f) : 0f;
        return MathF.Max(managed, native);
    }

    /// <summary>The strength with no dead zone applied: a stick pushed less far than the dead zone still reads its value here.</summary>
    public static float GetActionRawStrength(StringName action, bool exactMatch = false)
    {
        string name = action.ToString();
        float managed = s_actions.TryGetValue(name, out ActionState? st) && (!exactMatch || st.Exact) ? st.RawStrength : 0f;
        if (!exactMatch && BuiltinPoll(name, out BuiltinState? b)) managed = MathF.Max(managed, b.Strength);
        float native = Native(name) is { } a ? Math.Clamp(MathF.Abs(a.Value1D), 0f, 1f) : 0f;
        return MathF.Max(managed, native);
    }

    /// <summary>Positive strength minus negative strength: -1 to 1.</summary>
    public static float GetAxis(StringName negativeAction, StringName positiveAction) =>
        GetActionStrength(positiveAction) - GetActionStrength(negativeAction);

    /// <summary>A direction from four actions, with a circular dead zone (a negative <paramref name="deadzone"/> averages the four actions' own).</summary>
    public static Vector2 GetVector(StringName negativeX, StringName positiveX, StringName negativeY, StringName positiveY, float deadzone = -1f)
    {
        var v = new Vector2(GetActionRawStrength(positiveX) - GetActionRawStrength(negativeX),
                            GetActionRawStrength(positiveY) - GetActionRawStrength(negativeY));
        if (deadzone < 0f)
            deadzone = 0.25f * (InputMap.ActionGetDeadzone(positiveX) + InputMap.ActionGetDeadzone(negativeX) +
                                InputMap.ActionGetDeadzone(positiveY) + InputMap.ActionGetDeadzone(negativeY));
        float length = v.Length();
        if (length <= deadzone) return Vector2.Zero;
        if (length > 1f) return v / length;
        return v * ((length - deadzone) / (length * (1f - deadzone)));
    }

    /// <summary>Presses an action as if an event had (Godot's action_press). It stays pressed until released.</summary>
    public static void ActionPress(StringName action, float strength = 1f)
    {
        ActionState st = StateOf(action.ToString());
        if (!st.Pressed) MarkPressed(st, pressed: true);
        st.Exact = true;
        st.Strength = st.RawStrength = Math.Clamp(strength, 0f, 1f);
    }

    public static void ActionRelease(StringName action)
    {
        ActionState st = StateOf(action.ToString());
        if (st.Pressed) MarkPressed(st, pressed: false);
        st.Strength = st.RawStrength = 0f;
    }

    private static ActionState StateOf(string name)
    {
        if (!s_actions.TryGetValue(name, out ActionState? st)) s_actions[name] = st = new ActionState();
        return st;
    }

    private static void MarkPressed(ActionState st, bool pressed)
    {
        st.Pressed = pressed;
        if (pressed) { st.PressedProcessFrame = ProcessFrameNow; st.PressedPhysicsFrame = PhysicsFrameNow; }
        else { st.ReleasedProcessFrame = ProcessFrameNow; st.ReleasedPhysicsFrame = PhysicsFrameNow; }
    }

    public static bool IsAnythingPressed()
    {
        if (s_keys.Count > 0 || s_mouseButtons.Count > 0 || s_joyButtons.Count > 0) return true;
        foreach (ActionState st in s_actions.Values) if (st.Pressed) return true;
        return false;
    }

    // ---- keys, mouse, pads ----

    private static bool NativeKey(Key key)
    {
        if (!NodeHost.Current.IsEngine || !InputBridge.TryAverKey(key, out Aver.Framework.Key ak)) return false;
        try { return Aver.Framework.Input.GetKey(ak); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    public static bool IsKeyPressed(Key keycode) => s_keys.Contains(keycode) || NativeKey(keycode);

    public static bool IsPhysicalKeyPressed(Key keycode) => IsKeyPressed(keycode);

    public static bool IsKeyLabelPressed(Key keycode) => IsKeyPressed(keycode);

    public static bool IsMouseButtonPressed(MouseButton button)
    {
        if (s_mouseButtons.Contains(button)) return true;
        if (!NodeHost.Current.IsEngine || !InputBridge.TryAverMouse(button, out Aver.Framework.Key mk)) return false;
        try { return Aver.Framework.Input.GetKey(mk); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    public static bool IsJoyButtonPressed(int device, JoyButton button)
    {
        if (s_joyButtons.Contains(button)) return true;
        if (!NodeHost.Current.IsEngine || !InputBridge.TryAverButton(button, out Aver.Framework.GamepadButton gb)) return false;
        try { return Aver.Framework.Input.GetGamepadButton(gb, device); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    /// <summary>An axis reading, -1 to 1 (a stick pushed down reads positive, as in Godot).</summary>
    public static float GetJoyAxis(int device, JoyAxis axis)
    {
        if (s_joyAxes.TryGetValue(axis, out float v) && v != 0f) return v;
        if (!NodeHost.Current.IsEngine) return 0f;
        foreach (Aver.Framework.GamepadAxis a in Enum.GetValues<Aver.Framework.GamepadAxis>())
        {
            if (InputBridge.ToJoyAxis(a) != axis) continue;
            try { return InputBridge.AxisValueFromAver(axis, Aver.Framework.Input.GetGamepadAxis(a, device)); }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return 0f; }
        }
        return 0f;
    }

    public static MouseButtonMask GetMouseButtonMask()
    {
        MouseButtonMask m = 0;
        if (IsMouseButtonPressed(MouseButton.Left)) m |= MouseButtonMask.Left;
        if (IsMouseButtonPressed(MouseButton.Right)) m |= MouseButtonMask.Right;
        if (IsMouseButtonPressed(MouseButton.Middle)) m |= MouseButtonMask.Middle;
        if (IsMouseButtonPressed(MouseButton.Xbutton1)) m |= MouseButtonMask.MbXbutton1;
        if (IsMouseButtonPressed(MouseButton.Xbutton2)) m |= MouseButtonMask.MbXbutton2;
        return m;
    }

    /// <summary>The pointer's velocity in pixels per second, from the last motion event.</summary>
    public static Vector2 GetLastMouseVelocity() => s_mouseVelocity;

    public static Vector2 GetLastMouseScreenVelocity() => s_mouseVelocity;

    // ---- gamepads ----

    /// <summary>The carrier of the Input singleton's signals (Godot's Input is an object; here it is a static class).</summary>
    private sealed class InputSignals : RefCounted
    {
        public new class SignalName : GodotObject.SignalName
        {
            public static readonly StringName JoyConnectionChanged = "joy_connection_changed";
        }
    }

    private static InputSignals? s_joyHooks;

    /// <summary>The object that carries Input's signals: <c>Input.Singleton.Connect("joy_connection_changed", ...)</c>.</summary>
    public static GodotObject Singleton => s_joyHooks ??= new InputSignals();

    /// <summary>Raised when a gamepad is plugged in or removed (Godot's joy_connection_changed): the device number and whether it is now connected.</summary>
    public static event Action<long, bool>? JoyConnectionChanged;

    // Watches the four pads the engine reads; the first frame reports the pads already plugged in as connected, as Godot does at start.
    private static void PollJoypads()
    {
        for (int pad = 0; pad < s_padSeen.Length; pad++)
        {
            bool now;
            try { now = Aver.Framework.Gamepads.IsConnected(pad); }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return; }
            if (now == s_padSeen[pad]) continue;
            s_padSeen[pad] = now;
            JoyConnectionChanged?.Invoke(pad, now);
            Singleton.EmitSignal(InputSignals.SignalName.JoyConnectionChanged, (long)pad, now);
        }
    }

    private static bool PadIsConnected(int device)
    {
        if (device < 0 || device >= Aver.Framework.Gamepads.MaxPads) return false;
        if (!NodeHost.Current.IsEngine) return device == 0 && (s_joyButtons.Count > 0 || s_joyAxes.Count > 0);
        try { return Aver.Framework.Gamepads.IsConnected(device); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    /// <summary>The connected gamepads' device numbers.</summary>
    public static Collections.Array<int> GetConnectedJoypads()
    {
        var pads = new Collections.Array<int>();
        for (int d = 0; d < Aver.Framework.Gamepads.MaxPads; d++) if (PadIsConnected(d)) pads.Add(d);
        return pads;
    }

    /// <summary>The device's name ("" for a pad that is not connected).</summary>
    public static string GetJoyName(int device)
    {
        if (!PadIsConnected(device) || !NodeHost.Current.IsEngine) return "";
        try { return Aver.Framework.Gamepads.Name(device); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return ""; }
    }

    /// <summary>Approximated: the engine does not read the device's GUID, so this is empty.</summary>
    public static string GetJoyGuid(int device) => "";

    /// <summary>Whether the engine maps the device to the standard layout: every connected pad is read through one.</summary>
    public static bool IsJoyKnown(int device) => PadIsConnected(device);

    /// <summary>Approximated: the vendor, product and mapping details are not read, so the dictionary holds only the pad's name.</summary>
    public static Collections.Dictionary GetJoyInfo(int device)
    {
        var info = new Collections.Dictionary();
        string name = GetJoyName(device);
        if (name.Length > 0) info.Add("name", name);
        return info;
    }

    /// <summary>Rumbles a pad: the weak (high-frequency) and strong motors, 0 to 1, for <paramref name="duration"/> seconds (0 until stopped).</summary>
    public static void StartJoyVibration(int device, float weakMagnitude, float strongMagnitude, float duration = 0f)
    {
        s_vibration[Math.Clamp(device, 0, s_vibration.Length - 1)] = (Math.Clamp(weakMagnitude, 0f, 1f), Math.Clamp(strongMagnitude, 0f, 1f), Math.Max(0f, duration));
        if (!NodeHost.Current.IsEngine) return;
        try { Aver.Framework.Gamepads.Rumble(device, weakMagnitude, strongMagnitude, duration); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    public static void StopJoyVibration(int device)
    {
        s_vibration[Math.Clamp(device, 0, s_vibration.Length - 1)] = default;
        if (!NodeHost.Current.IsEngine) return;
        try { Aver.Framework.Gamepads.StopRumble(device); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    // What the last rumble request asked, so a test (or a script via GetJoyVibrationStrength) can read it back.
    private static readonly (float Weak, float Strong, float Duration)[] s_vibration = new (float, float, float)[Aver.Framework.Gamepads.MaxPads];

    /// <summary>The last rumble asked of the pad: weak and strong magnitude (Godot's get_joy_vibration_strength).</summary>
    public static Vector2 GetJoyVibrationStrength(int device) =>
        device >= 0 && device < s_vibration.Length ? new Vector2(s_vibration[device].Weak, s_vibration[device].Strong) : Vector2.Zero;

    /// <summary>The seconds the last rumble was asked to run (0: until stopped).</summary>
    public static float GetJoyVibrationDuration(int device) => device >= 0 && device < s_vibration.Length ? s_vibration[device].Duration : 0f;

    // ---- motion sensors: no device has one ----

    private static bool s_sensorLogged;

    private static Vector3 NoSensor()
    {
        if (!s_sensorLogged)
        {
            s_sensorLogged = true;
            Aver.Scripting.Log.Info("[Fabric] Input motion sensors (accelerometer, gravity, gyroscope, magnetometer): this device has none; they read zero");
        }
        return Vector3.Zero;
    }

    /// <summary>Approximated: zero. The engine reads no motion sensor; the first call says so in the log.</summary>
    public static Vector3 GetAccelerometer() => NoSensor();
    public static Vector3 GetGravity() => NoSensor();
    public static Vector3 GetGyroscope() => NoSensor();
    public static Vector3 GetMagnetometer() => NoSensor();

    // ---- touch ----

    private static bool s_touchEmulation;

    /// <summary>Whether the left mouse button acts as a finger (Godot's emulate_touch_from_mouse). Off by default; the engine does it where no real touch screen
    /// has reported a finger.</summary>
    public static bool EmulateTouchFromMouse
    {
        get => NodeHost.Current.IsEngine ? TouchEmulationEngine(null) : s_touchEmulation;
        set
        {
            s_touchEmulation = value;
            if (NodeHost.Current.IsEngine) TouchEmulationEngine(value);
        }
    }

    private static bool TouchEmulationEngine(bool? set)
    {
        try
        {
            if (set is { } v) Aver.Framework.Touch.Emulation = v;
            return Aver.Framework.Touch.Emulation;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return s_touchEmulation; }
    }

    /// <summary>Stored, not applied (approximated): on Windows a touch already arrives as a mouse event, and SDL's touch-to-mouse is left at its default.</summary>
    public static bool EmulateMouseFromTouch { get; set; } = true;

    /// <summary>Stored, not applied: the engine delivers each input event as it comes (it never merges motion into one per frame).</summary>
    public static bool UseAccumulatedInput { get; set; } = true;

    /// <summary>Godot's Input.CursorShape (the same shapes as <c>Control.CursorShape</c>).</summary>
    public enum CursorShape { Arrow = 0, Ibeam = 1, PointingHand = 2, Cross = 3, Wait = 4, Busy = 5, Drag = 6, CanDrop = 7, Forbidden = 8, Vsize = 9, Hsize = 10, Bdiagsize = 11, Fdiagsize = 12, Move = 13, Vsplit = 14, Hsplit = 15, Help = 16 }

    /// <summary>The shape the pointer shows while it is over the window.</summary>
    public static void SetDefaultCursorShape(CursorShape shape = CursorShape.Arrow)
    {
        s_cursorShape = shape;
        if (!NodeHost.Current.IsEngine) return;
        try { Aver.Framework.Pointer.Shape = (Aver.Framework.CursorShape)(int)shape; }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    public static CursorShape GetCurrentCursorShape() => s_cursorShape;

    private static bool s_cursorImageLogged;

    /// <summary>Replaces a cursor shape with an image. Approximated: the engine takes the pixels as RGBA, and a texture's pixels are not readable from a script
    /// yet, so a texture is refused (the first time, in the log) and the shape stays as it was; <see cref="SetCustomMouseCursorPixels"/> takes the bytes.</summary>
    public static void SetCustomMouseCursor(Resource? image, CursorShape shape = CursorShape.Arrow, Vector2 hotspot = default)
    {
        if (image == null) { SetCustomMouseCursorPixels(null, 0, 0, shape, hotspot); return; }
        if (s_cursorImageLogged) return;
        s_cursorImageLogged = true;
        Aver.Scripting.Log.Info("[Fabric] Input.SetCustomMouseCursor: the pixels of " + image.GetType().Name + " are not readable here yet; the cursor keeps its shape (SetCustomMouseCursorPixels takes RGBA bytes)");
    }

    /// <summary>Replaces a cursor shape with w x h RGBA bytes (straight alpha, top row first, each side 1 to 256), the hotspot inside the image; null puts the system
    /// shape back.</summary>
    public static void SetCustomMouseCursorPixels(byte[]? rgba, int width, int height, CursorShape shape = CursorShape.Arrow, Vector2 hotspot = default)
    {
        if (!NodeHost.Current.IsEngine) return;
        try { Aver.Framework.Pointer.SetCursorImage((Aver.Framework.CursorShape)(int)shape, rgba, width, height, (int)hotspot.X, (int)hotspot.Y); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    /// <summary>Moves the pointer to a position in viewport pixels at the next frame.</summary>
    public static void WarpMouse(Vector2 position)
    {
        s_mousePosition = position;
        if (!NodeHost.Current.IsEngine) return;
        try
        {
            Vector2 w = SceneTree.Instance.Root.ContentToWindow(position);
            Aver.Framework.Pointer.Warp(w.X, w.Y);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    private static bool s_vibrateLogged;

    /// <summary>Approximated: Aver has no handheld haptics, so this does nothing; the first call says so in the log.</summary>
    public static void VibrateHandheld(int durationMs = 500, float amplitude = -1f)
    {
        if (s_vibrateLogged) return;
        s_vibrateLogged = true;
        Aver.Scripting.Log.Info("[Fabric] Input.VibrateHandheld: this device has no handheld vibration; the call does nothing");
    }

    /// <summary>Whether the pointer is shown, hidden, captured or confined to the game window. The shipped game takes the mode as it is; the editor's Play
    /// knows only a free or a captured pointer, so there a visible, hidden or confined mode frees it.</summary>
    public static MouseModeEnum MouseMode
    {
        get => s_mouseMode;
        set
        {
            s_mouseMode = value;
            s_pointerApplied = true;
            if (NodeHost.Current.IsEngine) ApplyMouseMode();
        }
    }

    // Hands the engine the pointer mode (the game applies it) and, for the editor's Play, the free-cursor request that stands in for it.
    private static void ApplyMouseMode()
    {
        try
        {
            Aver.Framework.Pointer.Mode = s_mouseMode switch
            {
                MouseModeEnum.Visible => Aver.Framework.PointerMode.Visible,
                MouseModeEnum.Hidden => Aver.Framework.PointerMode.Hidden,
                MouseModeEnum.Captured => Aver.Framework.PointerMode.Captured,
                MouseModeEnum.Confined => Aver.Framework.PointerMode.Confined,
                _ => Aver.Framework.PointerMode.ConfinedHidden,
            };
            // Only a visible pointer asks for the free cursor in the shipped game (a free cursor there wins over every mode); the editor, which ignores
            // the mode, gets the request for every mode but a captured one.
            bool free = s_mouseMode == MouseModeEnum.Visible || (s_mouseMode != MouseModeEnum.Captured && Aver.Framework.OS.HasFeature("editor"));
            if (free && s_cursorScope == null) s_cursorScope = Game.FreeCursor();
            else if (!free && s_cursorScope != null) { s_cursorScope.Dispose(); s_cursorScope = null; }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    // ---- feeding events in ----

    /// <summary>Feeds an event to the game as if it had come from a device (Godot's parse_input_event): input and action state change,
    /// and the event goes down the tree to _Input and _UnhandledInput.</summary>
    public static void ParseInputEvent(InputEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        Absorb(@event);
        Aver.Framework.InputEvent? engine = NodeHost.Current.IsEngine ? InputBridge.ToFramework(@event) : null;
        if (engine != null) Aver.Framework.Input.ParseInputEvent(engine);
        else SceneTree.Instance.DispatchInputManaged(@event);
    }

    /// <summary>Updates the held keys and buttons and the actions the event triggers. Events from the engine's own chain are not absorbed
    /// (the engine already polls its devices); this is for events fed by script.</summary>
    internal static void Absorb(InputEvent e)
    {
        switch (e)
        {
            case InputEventKey k:
                Key key = k.GetKeycodeWithModifiers();
                if (k.Pressed) s_keys.Add(key); else s_keys.Remove(key);
                break;
            case InputEventMouseButton b:
                if (b.Pressed) s_mouseButtons.Add(b.ButtonIndex); else s_mouseButtons.Remove(b.ButtonIndex);
                s_mousePosition = b.Position;
                break;
            case InputEventMouseMotion m:
                s_mousePosition = m.Position;
                s_mouseVelocity = m.Velocity;
                break;
            case InputEventJoypadButton jb:
                if (jb.Pressed) s_joyButtons.Add(jb.ButtonIndex); else s_joyButtons.Remove(jb.ButtonIndex);
                break;
            case InputEventJoypadMotion jm:
                s_joyAxes[jm.Axis] = jm.AxisValue;
                break;
        }
        if (e is InputEventMouseMotion) return;
        foreach (StringName action in InputMap.GetActions())
        {
            string name = action.ToString();
            if (!InputMap.EventGetActionStatus(e, name, exactMatch: false, out bool pressed, out float strength, out float raw)) continue;
            ActionState st = StateOf(name);
            if (!e.IsEcho() && st.Pressed != pressed)
            {
                MarkPressed(st, pressed);
                st.Strength = st.RawStrength = 0f;
                st.Exact = InputMap.EventGetActionStatus(e, name, exactMatch: true, out bool exactPressed, out _, out _) && exactPressed == pressed;
            }
            st.Strength = strength;
            st.RawStrength = raw;
        }
    }
}
