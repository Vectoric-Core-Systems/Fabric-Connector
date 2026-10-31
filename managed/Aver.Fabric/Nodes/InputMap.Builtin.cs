// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Godot's built-in ui_* actions (ui_accept, ui_cancel, ui_left ...): every Godot project has them, with the keys below, unless project.godot rebinds them. The
// converter writes the project's own actions into the engine's input scheme; the ones it does not write exist here, so a script that asks for ui_accept finds it. An
// action the project rebound is the engine's and wins; a script that changes a built-in one (action_add_event, erase_action) works on a copy that starts as the defaults.

namespace Aver.Fabric;

public static partial class InputMap
{
    private static readonly HashSet<string> s_erasedBuiltins = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, InputEvent[]> s_builtinCache = new(StringComparer.Ordinal);

    // Key names are Aver.Fabric's Key members; "Joy:" a JoyButton, "Axis:" a JoyAxis with the direction after it. "Ctrl+Shift+Z" has modifiers. The mac variants of Godot's
    // text-editing shortcuts are not listed. Actions with no default event on a PC are listed with an empty spec.
    private static readonly Dictionary<string, string> Builtins = new(StringComparer.Ordinal)
    {
        ["ui_accept"] = "Enter|KpEnter|Space|Joy:A",
        ["ui_select"] = "Space|Joy:Y",
        ["ui_cancel"] = "Escape|Joy:B",
        ["ui_focus_next"] = "Tab",
        ["ui_focus_prev"] = "Shift+Tab",
        ["ui_left"] = "Left|Joy:DpadLeft|Axis:LeftX-",
        ["ui_right"] = "Right|Joy:DpadRight|Axis:LeftX+",
        ["ui_up"] = "Up|Joy:DpadUp|Axis:LeftY-",
        ["ui_down"] = "Down|Joy:DpadDown|Axis:LeftY+",
        ["ui_page_up"] = "Pageup",
        ["ui_page_down"] = "Pagedown",
        ["ui_home"] = "Home",
        ["ui_end"] = "End",
        ["ui_cut"] = "Ctrl+X|Shift+Delete",
        ["ui_copy"] = "Ctrl+C|Ctrl+Insert",
        ["ui_paste"] = "Ctrl+V|Shift+Insert",
        ["ui_undo"] = "Ctrl+Z",
        ["ui_redo"] = "Ctrl+Shift+Z|Ctrl+Y",
        ["ui_text_completion_query"] = "Ctrl+Space",
        ["ui_text_completion_accept"] = "Enter|KpEnter|Tab",
        ["ui_text_completion_replace"] = "Ctrl+Enter",
        ["ui_text_newline"] = "Enter|KpEnter",
        ["ui_text_newline_blank"] = "Ctrl+Enter|Ctrl+KpEnter",
        ["ui_text_newline_above"] = "Ctrl+Shift+Enter|Ctrl+Shift+KpEnter",
        ["ui_text_indent"] = "Tab",
        ["ui_text_dedent"] = "Shift+Tab",
        ["ui_text_backspace"] = "Backspace|Shift+Backspace",
        ["ui_text_backspace_word"] = "Ctrl+Backspace",
        ["ui_text_backspace_all_to_left"] = "",
        ["ui_text_delete"] = "Delete",
        ["ui_text_delete_word"] = "Ctrl+Delete",
        ["ui_text_delete_all_to_right"] = "",
        ["ui_text_caret_left"] = "Left",
        ["ui_text_caret_word_left"] = "Ctrl+Left",
        ["ui_text_caret_right"] = "Right",
        ["ui_text_caret_word_right"] = "Ctrl+Right",
        ["ui_text_caret_up"] = "Up",
        ["ui_text_caret_down"] = "Down",
        ["ui_text_caret_line_start"] = "Home",
        ["ui_text_caret_line_end"] = "End",
        ["ui_text_caret_page_up"] = "Pageup",
        ["ui_text_caret_page_down"] = "Pagedown",
        ["ui_text_caret_document_start"] = "Ctrl+Home",
        ["ui_text_caret_document_end"] = "Ctrl+End",
        ["ui_text_caret_add_below"] = "Ctrl+Alt+Down",
        ["ui_text_caret_add_above"] = "Ctrl+Alt+Up",
        ["ui_text_scroll_up"] = "Ctrl+Up",
        ["ui_text_scroll_down"] = "Ctrl+Down",
        ["ui_text_select_all"] = "Ctrl+A",
        ["ui_text_select_word_under_caret"] = "",
        ["ui_text_add_selection_for_next_occurrence"] = "Ctrl+D",
        ["ui_text_skip_selection_for_next_occurrence"] = "Ctrl+Alt+D",
        ["ui_text_clear_carets_and_selection"] = "Escape",
        ["ui_text_toggle_insert_mode"] = "Insert",
        ["ui_text_submit"] = "Enter|KpEnter",
        ["ui_menu"] = "",
        ["ui_graph_duplicate"] = "Ctrl+D",
        ["ui_graph_delete"] = "Delete",
        ["ui_filedialog_up_one_level"] = "Backspace",
        ["ui_filedialog_refresh"] = "F5",
        ["ui_filedialog_show_hidden"] = "H",
        ["ui_swap_input_direction"] = "",
    };

    /// <summary>The names of Godot's built-in actions.</summary>
    internal static IEnumerable<string> BuiltinNames => Builtins.Keys;

    // A built-in action nobody erased.
    private static bool IsBuiltin(string name) => Builtins.ContainsKey(name) && !s_erasedBuiltins.Contains(name);

    /// <summary>A built-in action in its default form: nobody erased it, no script changed it and the project did not bind it. Only then are its default events in force.</summary>
    internal static bool IsBuiltinActive(string name) => IsBuiltin(name) && !s_actions.ContainsKey(name) && !EngineHas(name);

    private static InputEvent[] CreateBuiltinEvents(string name)
    {
        var events = new List<InputEvent>();
        foreach (string token in Builtins[name].Split('|', StringSplitOptions.RemoveEmptyEntries))
            if (ParseBuiltin(token) is { } e) events.Add(e);
        return events.ToArray();
    }

    /// <summary>The default events of a built-in action; the same objects each time.</summary>
    internal static IReadOnlyList<InputEvent> BuiltinEvents(string name)
    {
        if (!Builtins.ContainsKey(name)) return System.Array.Empty<InputEvent>();
        if (!s_builtinCache.TryGetValue(name, out InputEvent[]? events)) s_builtinCache[name] = events = CreateBuiltinEvents(name);
        return events;
    }

    private static InputEvent? ParseBuiltin(string token)
    {
        if (token.StartsWith("Joy:", StringComparison.Ordinal))
            return Enum.TryParse(token[4..], out JoyButton b) ? new InputEventJoypadButton { ButtonIndex = b } : null;
        if (token.StartsWith("Axis:", StringComparison.Ordinal))
        {
            string t = token[5..];
            return Enum.TryParse(t[..^1], out JoyAxis axis) ? new InputEventJoypadMotion { Axis = axis, AxisValue = t[^1] == '-' ? -1f : 1f } : null;
        }
        string[] parts = token.Split('+');
        if (!Enum.TryParse(parts[^1], out Key key)) return null;
        var e = new InputEventKey { Keycode = key, PhysicalKeycode = key };
        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i])
            {
                case "Ctrl": e.CtrlPressed = true; break;
                case "Shift": e.ShiftPressed = true; break;
                case "Alt": e.AltPressed = true; break;
            }
        }
        return e;
    }
}
