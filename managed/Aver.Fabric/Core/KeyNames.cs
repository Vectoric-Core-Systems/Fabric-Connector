// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// The names Godot gives keys (OS.get_keycode_string / find_keycode_from_string): its table, its modifier prefixes in its order.

namespace Aver.Fabric;

internal static class KeyNames
{
    private static readonly (long Code, string Name)[] s_table =
    {
        ((long)Key.Escape, "Escape"), ((long)Key.Tab, "Tab"), ((long)Key.Backtab, "Backtab"), ((long)Key.Backspace, "Backspace"), ((long)Key.Enter, "Enter"),
        ((long)Key.KpEnter, "Kp Enter"), ((long)Key.Insert, "Insert"), ((long)Key.Delete, "Delete"), ((long)Key.Pause, "Pause"), ((long)Key.Print, "Print"),
        ((long)Key.Sysreq, "SysReq"), ((long)Key.Clear, "Clear"), ((long)Key.Home, "Home"), ((long)Key.End, "End"), ((long)Key.Left, "Left"), ((long)Key.Up, "Up"),
        ((long)Key.Right, "Right"), ((long)Key.Down, "Down"), ((long)Key.Pageup, "PageUp"), ((long)Key.Pagedown, "PageDown"), ((long)Key.Shift, "Shift"),
        ((long)Key.Ctrl, "Ctrl"), ((long)Key.Meta, "Meta"), ((long)Key.Alt, "Alt"), ((long)Key.Capslock, "CapsLock"), ((long)Key.Numlock, "NumLock"),
        ((long)Key.Scrolllock, "ScrollLock"),
        ((long)Key.KpMultiply, "Kp Multiply"), ((long)Key.KpDivide, "Kp Divide"), ((long)Key.KpSubtract, "Kp Subtract"), ((long)Key.KpPeriod, "Kp Period"),
        ((long)Key.KpAdd, "Kp Add"), ((long)Key.Menu, "Menu"), ((long)Key.Hyper, "Hyper"), ((long)Key.Help, "Help"),
        (32, "Space"), (33, "Exclam"), (34, "QuoteDbl"), (35, "NumberSign"), (36, "Dollar"), (37, "Percent"), (38, "Ampersand"), (39, "Apostrophe"),
        (40, "ParenLeft"), (41, "ParenRight"), (42, "Asterisk"), (43, "Plus"), (44, "Comma"), (45, "Minus"), (46, "Period"), (47, "Slash"),
        (58, "Colon"), (59, "Semicolon"), (60, "Less"), (61, "Equal"), (62, "Greater"), (63, "Question"), (64, "At"),
        (91, "BracketLeft"), (92, "BackSlash"), (93, "BracketRight"), (94, "AsciiCircum"), (95, "UnderScore"), (96, "QuoteLeft"),
        (123, "BraceLeft"), (124, "Bar"), (125, "BraceRight"), (126, "AsciiTilde"), (165, "Yen"), (167, "Section"),
    };

    private static readonly Dictionary<long, string> s_byCode = Build();

    private static Dictionary<long, string> Build()
    {
        var d = new Dictionary<long, string>();
        foreach ((long code, string name) in s_table) d[code] = name;
        for (int i = 0; i < 35; i++) d[(long)Key.F1 + i] = "F" + (i + 1);
        for (int i = 0; i < 10; i++) { d[(long)Key.Kp0 + i] = "Kp " + i; d['0' + i] = ((char)('0' + i)).ToString(); }
        for (char c = 'A'; c <= 'Z'; c++) d[c] = c.ToString();
        if (OperatingSystem.IsWindows()) d[(long)Key.Meta] = "Windows";
        else if (OperatingSystem.IsMacOS()) { d[(long)Key.Meta] = "Command"; d[(long)Key.Alt] = "Option"; }
        return d;
    }

    private static string NameOf(long code) => s_byCode.TryGetValue(code, out string? n) ? n : char.ConvertFromUtf32((int)Math.Clamp(code, 0, 0x10FFFF));

    public static string Name(Key key)
    {
        long code = (long)key;
        var sb = new System.Text.StringBuilder();
        if ((code & (long)KeyModifierMask.Shift) != 0) sb.Append(NameOf((long)Key.Shift)).Append('+');
        if ((code & (long)KeyModifierMask.Alt) != 0) sb.Append(NameOf((long)Key.Alt)).Append('+');
        if ((code & (long)KeyModifierMask.Ctrl) != 0) sb.Append(NameOf((long)Key.Ctrl)).Append('+');
        if ((code & (long)KeyModifierMask.Meta) != 0) sb.Append(NameOf((long)Key.Meta)).Append('+');
        code &= (long)KeyModifierMask.CodeMask;
        return sb.Append(NameOf(code)).ToString();
    }

    public static Key Find(string text)
    {
        string[] parts = text.Split('+');
        long code = 0;
        foreach ((long c, string n) in s_byCode)
            if (string.Equals(n, parts[^1], StringComparison.OrdinalIgnoreCase)) { code = c; break; }
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (string.Equals(parts[i], NameOf((long)Key.Shift), StringComparison.OrdinalIgnoreCase)) code |= (long)KeyModifierMask.Shift;
            else if (string.Equals(parts[i], NameOf((long)Key.Ctrl), StringComparison.OrdinalIgnoreCase)) code |= (long)KeyModifierMask.Ctrl;
            else if (string.Equals(parts[i], NameOf((long)Key.Meta), StringComparison.OrdinalIgnoreCase)) code |= (long)KeyModifierMask.Meta;
            else if (string.Equals(parts[i], NameOf((long)Key.Alt), StringComparison.OrdinalIgnoreCase)) code |= (long)KeyModifierMask.Alt;
        }
        return (Key)code;
    }
}
