// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// CodeEdit and the syntax highlighters (parity wave 8a, F50), built on Fabric's TextEdit. A highlighter colours runs of a line (TextEdit draws them); CodeEdit adds
// the code-shaped API on top: string and comment delimiters, indent-based line folding, the line operations (indent, unindent, duplicate, move, delete) and the
// switches a code editor has. Not drawn: the gutters (line numbers, fold arrows, breakpoints), the minimap, code completion's popup and brace auto-completion on
// typing; the properties are kept and the helpers work, so a script that drives the editor itself runs.

namespace Aver.Fabric;

/// <summary>Colours runs of a TextEdit's lines (Godot's SyntaxHighlighter).</summary>
public abstract class SyntaxHighlighter : Resource
{
    internal TextEdit? Owner;

    public TextEdit? GetTextEdit() => Owner;

    /// <summary>A GDScript-style override: the colour runs of a line as <c>{ column: { "color": Color } }</c>. The default asks <see cref="Runs"/>.</summary>
    public virtual Collections.Dictionary _GetLineSyntaxHighlighting(int line)
    {
        var d = new Collections.Dictionary();
        if (Owner == null) return d;
        foreach ((int col, Color? c) in Runs(line, Owner.GetLine(line)))
            if (c.HasValue) d[col] = new Collections.Dictionary { ["color"] = Variant.CreateFrom(c.Value) };
        return d;
    }

    public Collections.Dictionary GetLineSyntaxHighlighting(int line) => _GetLineSyntaxHighlighting(line);

    public void ClearHighlightingCache() { }
    public void UpdateCache() { }

    /// <summary>The runs of a line: each starts at a column and has a colour, or none (the text's own colour). Sorted by column and covering the line from 0.</summary>
    internal virtual List<(int Start, Color? Color)> Runs(int line, string text) => new() { (0, null) };
}

/// <summary>A highlighter driven by keyword lists, symbols, numbers, functions and delimited regions (Godot's CodeHighlighter).</summary>
public class CodeHighlighter : SyntaxHighlighter
{
    private readonly Dictionary<string, Color> _keywords = new(StringComparer.Ordinal), _memberKeywords = new(StringComparer.Ordinal);
    private readonly List<(string Start, string End, Color Color, bool LineOnly)> _regions = new();

    public Color NumberColor { get; set; } = new(0.6f, 0.8f, 1f, 1f);
    public Color SymbolColor { get; set; } = new(0.8f, 0.8f, 0.8f, 1f);
    public Color FunctionColor { get; set; } = new(0.7f, 0.9f, 0.7f, 1f);
    public Color MemberVariableColor { get; set; } = new(0.9f, 0.8f, 0.6f, 1f);

    public Collections.Dictionary KeywordColors
    {
        get { var d = new Collections.Dictionary(); foreach (var kv in _keywords) d[kv.Key] = Variant.CreateFrom(kv.Value); return d; }
        set { _keywords.Clear(); foreach (var k in value.Keys) _keywords[k.AsString()] = value[k].AsColor(); }
    }

    public Collections.Dictionary MemberKeywordColors
    {
        get { var d = new Collections.Dictionary(); foreach (var kv in _memberKeywords) d[kv.Key] = Variant.CreateFrom(kv.Value); return d; }
        set { _memberKeywords.Clear(); foreach (var k in value.Keys) _memberKeywords[k.AsString()] = value[k].AsColor(); }
    }

    /// <summary>Delimited regions as <c>"start end"</c> (an empty end runs to the end of the line) to colour, as Godot stores them.</summary>
    public Collections.Dictionary ColorRegions
    {
        get { var d = new Collections.Dictionary(); foreach (var r in _regions) d[r.Start + " " + r.End] = Variant.CreateFrom(r.Color); return d; }
        set
        {
            _regions.Clear();
            foreach (var k in value.Keys)
            {
                string s = k.AsString();
                int sp = s.IndexOf(' ');
                AddColorRegion(sp < 0 ? s : s[..sp], sp < 0 ? "" : s[(sp + 1)..], value[k].AsColor(), sp < 0 || s[(sp + 1)..].Length == 0);
            }
        }
    }

    public void AddKeywordColor(string keyword, Color color) => _keywords[keyword] = color;
    public void RemoveKeywordColor(string keyword) => _keywords.Remove(keyword);
    public bool HasKeywordColor(string keyword) => _keywords.ContainsKey(keyword);
    public Color GetKeywordColor(string keyword) => _keywords.TryGetValue(keyword, out Color c) ? c : new Color(1, 1, 1, 1);
    public void ClearKeywordColors() => _keywords.Clear();
    public void AddMemberKeywordColor(string keyword, Color color) => _memberKeywords[keyword] = color;
    public void RemoveMemberKeywordColor(string keyword) => _memberKeywords.Remove(keyword);
    public bool HasMemberKeywordColor(string keyword) => _memberKeywords.ContainsKey(keyword);
    public Color GetMemberKeywordColor(string keyword) => _memberKeywords.TryGetValue(keyword, out Color c) ? c : new Color(1, 1, 1, 1);
    public void ClearMemberKeywordColors() => _memberKeywords.Clear();

    public void AddColorRegion(string startKey, string endKey, Color color, bool lineOnly = false)
    {
        if (startKey.Length == 0) return;
        _regions.RemoveAll(r => r.Start == startKey);
        _regions.Add((startKey, endKey, color, lineOnly || endKey.Length == 0));
    }

    public void RemoveColorRegion(string startKey) => _regions.RemoveAll(r => r.Start == startKey);
    public bool HasColorRegion(string startKey) => _regions.Any(r => r.Start == startKey);
    public void ClearColorRegions() => _regions.Clear();

    private static bool IsIdStart(char c) => char.IsLetter(c) || c == '_';
    private static bool IsId(char c) => char.IsLetterOrDigit(c) || c == '_';

    // The region a line starts inside (a multi-line one left open by the lines before it), as an index into _regions, or -1.
    private int OpenRegionAt(int line)
    {
        if (Owner == null) return -1;
        int open = -1;
        for (int l = 0; l < line; l++)
        {
            string t = Owner.GetLine(l);
            int i = 0;
            while (i <= t.Length)
            {
                if (open >= 0)
                {
                    var r = _regions[open];
                    int e = r.End.Length == 0 ? -1 : t.IndexOf(r.End, i, StringComparison.Ordinal);
                    if (e < 0) { i = t.Length + 1; break; }
                    i = e + r.End.Length;
                    open = -1;
                }
                else
                {
                    int found = StartAt(t, i, out int idx);
                    if (found < 0) break;
                    var r = _regions[idx];
                    i = found + r.Start.Length;
                    if (!r.LineOnly) open = idx;
                    else break;   // a line-only region runs to the end of its line
                }
            }
            if (open >= 0 && _regions[open].LineOnly) open = -1;
        }
        return open;
    }

    // The earliest region start at or after column i in t: its column and index, or -1.
    private int StartAt(string t, int i, out int regionIndex)
    {
        int best = -1;
        regionIndex = -1;
        for (int k = 0; k < _regions.Count; k++)
        {
            int p = i <= t.Length ? t.IndexOf(_regions[k].Start, i, StringComparison.Ordinal) : -1;
            if (p >= 0 && (best < 0 || p < best || (p == best && _regions[k].Start.Length > _regions[regionIndex].Start.Length))) { best = p; regionIndex = k; }
        }
        return best;
    }

    internal override List<(int Start, Color? Color)> Runs(int line, string text)
    {
        var runs = new List<(int, Color?)>();
        void Add(int at, Color? c) { if (runs.Count > 0 && runs[^1].Item2 == c) return; if (runs.Count > 0 && runs[^1].Item1 == at) runs[^1] = (at, c); else runs.Add((at, c)); }
        Add(0, null);
        int open = OpenRegionAt(line);
        int pos = 0;
        while (pos < text.Length)
        {
            if (open >= 0)
            {
                var r = _regions[open];
                Add(pos, r.Color);
                int e = r.End.Length == 0 ? -1 : text.IndexOf(r.End, pos, StringComparison.Ordinal);
                if (e < 0) { pos = text.Length; break; }
                pos = e + r.End.Length;
                open = -1;
                Add(pos, null);
                continue;
            }
            int regionStart = StartAt(text, pos, out int idx);
            int token = pos;
            // Plain tokens up to the next region start (or the end of the line).
            int limit = regionStart < 0 ? text.Length : regionStart;
            while (token < limit)
            {
                char c = text[token];
                if (IsIdStart(c))
                {
                    int e = token + 1;
                    while (e < limit && IsId(text[e])) e++;
                    string word = text.Substring(token, e - token);
                    bool member = token > 0 && text[token - 1] == '.';
                    Color? col = null;
                    if (member && _memberKeywords.TryGetValue(word, out Color mc)) col = mc;
                    else if (!member && _keywords.TryGetValue(word, out Color kc)) col = kc;
                    else if (member) col = MemberVariableColor;
                    else if (e < text.Length && text[e] == '(') col = FunctionColor;
                    Add(token, col);
                    token = e;
                }
                else if (char.IsDigit(c))
                {
                    int e = token + 1;
                    while (e < limit && (char.IsLetterOrDigit(text[e]) || text[e] == '.' || text[e] == '_')) e++;
                    Add(token, NumberColor);
                    token = e;
                }
                else if (char.IsWhiteSpace(c)) { Add(token, null); token++; }
                else { Add(token, SymbolColor); token++; }
            }
            if (regionStart < 0) break;
            open = idx;
            pos = regionStart + _regions[idx].Start.Length;
            Add(regionStart, _regions[idx].Color);
            if (_regions[idx].LineOnly) { pos = text.Length; open = -1; }
        }
        return runs;
    }
}

/// <summary>A text editor for code (Godot's CodeEdit).</summary>
public class CodeEdit : TextEdit
{
    private readonly List<(string Start, string End, bool LineOnly)> _strings = new(), _comments = new();
    private readonly HashSet<int> _folded = new();

    public new class SignalName : TextEdit.SignalName
    {
        public static readonly StringName BreakpointToggled = "breakpoint_toggled";
        public static readonly StringName CodeCompletionRequested = "code_completion_requested";
        public static readonly StringName SymbolLookup = "symbol_lookup";
        public static readonly StringName SymbolValidate = "symbol_validate";
        public static readonly StringName SymbolHovered = "symbol_hovered";
    }

    public bool GuttersDrawBreakpointsGutter { get; set; }
    public bool GuttersDrawBookmarks { get; set; }
    public bool GuttersDrawExecutingLines { get; set; }
    public bool GuttersDrawLineNumbers { get; set; }
    public bool GuttersZeroPadLineNumbers { get; set; }
    public bool GuttersDrawFoldGutter { get; set; }
    public bool LineFolding { get; set; }
    public bool AutoIndentEnabled { get; set; } = true;
    public bool AutoBraceCompletionEnabled { get; set; }
    public bool AutoBraceCompletionHighlightMatching { get; set; }
    public Collections.Dictionary AutoBraceCompletionPairs { get; set; } = new() { ["("] = ")", ["["] = "]", ["{"] = "}", ["\""] = "\"", ["'"] = "'" };
    public bool CodeCompletionEnabled { get; set; }
    public string[] CodeCompletionPrefixes { get; set; } = System.Array.Empty<string>();
    public bool SymbolLookupOnClick { get; set; }
    public bool SymbolTooltipOnHover { get; set; }
    public bool IndentAutomatic { get; set; } = true;
    public string[] IndentAutomaticPrefixes { get; set; } = { ":", "{", "[", "(" };
    public bool DrawBreakpointsGutter { get; set; }

    public bool HasStringDelimiter(string startKey) => _strings.Any(s => s.Start == startKey);
    public bool HasCommentDelimiter(string startKey) => _comments.Any(s => s.Start == startKey);

    public void AddStringDelimiter(string startKey, string endKey = "", bool lineOnly = false)
    {
        if (startKey.Length == 0) return;
        _strings.RemoveAll(s => s.Start == startKey);
        _strings.Add((startKey, endKey, lineOnly || endKey.Length == 0));
    }

    public void AddCommentDelimiter(string startKey, string endKey = "", bool lineOnly = false)
    {
        if (startKey.Length == 0) return;
        _comments.RemoveAll(s => s.Start == startKey);
        _comments.Add((startKey, endKey, lineOnly || endKey.Length == 0));
    }

    public void RemoveStringDelimiter(string startKey) => _strings.RemoveAll(s => s.Start == startKey);
    public void RemoveCommentDelimiter(string startKey) => _comments.RemoveAll(s => s.Start == startKey);
    public void ClearStringDelimiters() => _strings.Clear();
    public void ClearCommentDelimiters() => _comments.Clear();

    /// <summary>The delimiter (string or comment) the column is inside, found by scanning from the start of the document. -1 for none, else the index in the matching list.</summary>
    private int DelimiterAt(List<(string Start, string End, bool LineOnly)> list, int line, int column)
    {
        int open = -1;
        for (int l = 0; l <= line; l++)
        {
            string t = GetLine(l);
            int limit = l == line ? Math.Min(column, t.Length) : t.Length;
            int i = 0;
            while (i < limit)
            {
                if (open >= 0)
                {
                    int e = list[open].End.Length == 0 ? -1 : t.IndexOf(list[open].End, i, StringComparison.Ordinal);
                    if (e < 0 || e + list[open].End.Length > limit) { i = limit; break; }
                    i = e + list[open].End.Length;
                    open = -1;
                    continue;
                }
                int best = -1, bi = -1;
                for (int k = 0; k < list.Count; k++)
                {
                    int p = t.IndexOf(list[k].Start, i, StringComparison.Ordinal);
                    if (p >= 0 && p < limit && (best < 0 || p < best)) { best = p; bi = k; }
                }
                if (best < 0) break;
                i = best + list[bi].Start.Length;
                if (i > limit) return bi;   // the cursor is inside the opener itself: not yet in
                if (list[bi].LineOnly)
                {
                    if (l == line) return bi;   // the cursor is after a line-only opener: inside it to the end of the line
                    break;                      // an earlier line: it ends with its line
                }
                open = bi;
            }
            if (open >= 0 && l < line && list[open].LineOnly) open = -1;
        }
        return open;
    }

    // Strings and comments are scanned together, so a comment mark inside a string (or a quote inside a comment) is not a delimiter. The strings come first in the list.
    private int InDelimiter(int line, int column) => DelimiterAt(_strings.Concat(_comments).ToList(), line, column < 0 ? GetLine(line).Length : column);

    public bool IsInString(int line, int column = -1) { int i = InDelimiter(line, column); return i >= 0 && i < _strings.Count; }
    public bool IsInComment(int line, int column = -1) { int i = InDelimiter(line, column); return i >= _strings.Count; }
    public int GetDelimiterStartPositionLine(int line, int column) => IsInString(line, column) || IsInComment(line, column) ? line : -1;

    // ---- folding (by indentation, as Godot's default) ----

    private int IndentOf(int line)
    {
        string t = GetLine(line);
        int n = 0;
        foreach (char c in t) { if (c == ' ') n++; else if (c == '\t') n += Math.Max(1, IndentSize); else break; }
        return n;
    }

    private bool Blank(int line) => GetLine(line).Trim().Length == 0;

    /// <summary>The last line of the block that starts at <paramref name="line"/> (the lines below it that are indented deeper, blank lines inside included), or the line itself.</summary>
    public int GetFoldEnd(int line)
    {
        if (line < 0 || line >= GetLineCount() || Blank(line)) return line;
        int ind = IndentOf(line), end = line;
        for (int l = line + 1; l < GetLineCount(); l++)
        {
            if (Blank(l)) continue;
            if (IndentOf(l) <= ind) break;
            end = l;
        }
        return end;
    }

    public bool CanFoldLine(int line) => line >= 0 && line < GetLineCount() && GetFoldEnd(line) > line && !_folded.Contains(line);
    public bool IsLineFolded(int line) => _folded.Contains(line);
    public int[] GetFoldedLines() => _folded.OrderBy(x => x).ToArray();
    public void FoldLine(int line) { if (CanFoldLine(line)) { _folded.Add(line); QueueRedraw(); } }
    public void UnfoldLine(int line) { if (_folded.Remove(line)) QueueRedraw(); }
    public void ToggleFoldableLine(int line) { if (_folded.Contains(line)) UnfoldLine(line); else FoldLine(line); }
    public void FoldAllLines() { for (int l = 0; l < GetLineCount(); l++) if (CanFoldLine(l)) _folded.Add(l); QueueRedraw(); }
    public void UnfoldAllLines() { _folded.Clear(); QueueRedraw(); }

    // ---- line operations ----

    private string IndentUnit() => IndentUseSpaces ? new string(' ', Math.Max(1, IndentSize)) : "\t";

    private (int From, int To) SelectedLines()
    {
        int a = GetSelectionFromLine(), b = GetSelectionToLine();
        return a < 0 ? (GetCaretLine(), GetCaretLine()) : (Math.Min(a, b), Math.Max(a, b));
    }

    /// <summary>Indents the selected lines (or the caret's line) by one level.</summary>
    public void IndentLines()
    {
        (int from, int to) = SelectedLines();
        string unit = IndentUnit();
        for (int l = from; l <= to; l++) SetLine(l, unit + GetLine(l));
    }

    /// <summary>Removes one level of indentation from the selected lines.</summary>
    public void UnindentLines()
    {
        (int from, int to) = SelectedLines();
        for (int l = from; l <= to; l++)
        {
            string t = GetLine(l);
            if (t.StartsWith('\t')) SetLine(l, t[1..]);
            else
            {
                int n = 0;
                while (n < t.Length && n < Math.Max(1, IndentSize) && t[n] == ' ') n++;
                if (n > 0) SetLine(l, t[n..]);
            }
        }
    }

    public void DuplicateLines()
    {
        (int from, int to) = SelectedLines();
        for (int l = from; l <= to; l++) InsertLineAt(to + 1 + (l - from), GetLine(l));
    }

    public void MoveLinesUp()
    {
        (int from, int to) = SelectedLines();
        if (from <= 0) return;
        string above = GetLine(from - 1);
        RemoveLineAt(from - 1);
        InsertLineAt(to, above);
    }

    public void MoveLinesDown()
    {
        (int from, int to) = SelectedLines();
        if (to >= GetLineCount() - 1) return;
        string below = GetLine(to + 1);
        RemoveLineAt(to + 1);
        InsertLineAt(from, below);
    }

    public void DeleteLines()
    {
        (int from, int to) = SelectedLines();
        for (int l = to; l >= from; l--)
        {
            if (GetLineCount() == 1) SetLine(0, "");
            else RemoveLineAt(l);
        }
    }

    /// <summary>Comments the selected lines with the first line comment delimiter (or uncomments them when they all have it).</summary>
    public void ToggleLineComments()
    {
        string? prefix = _comments.Where(c => c.LineOnly).Select(c => c.Start).FirstOrDefault();
        if (prefix == null) return;
        (int from, int to) = SelectedLines();
        bool all = true;
        for (int l = from; l <= to; l++) if (!GetLine(l).TrimStart().StartsWith(prefix, StringComparison.Ordinal) && GetLine(l).Trim().Length > 0) all = false;
        for (int l = from; l <= to; l++)
        {
            string t = GetLine(l);
            if (t.Trim().Length == 0) continue;
            int ind = t.Length - t.TrimStart().Length;
            SetLine(l, all ? t[..ind] + t[(ind + prefix.Length)..].TrimStart(' ') : t[..ind] + prefix + " " + t[ind..]);
        }
    }

    public int GetCodeCompletionSelectedIndex() => -1;
    public Collections.Array GetCodeCompletionOptions() => new();
    public void CancelCodeCompletion() { }
    public void AddCodeCompletionOption(int type, string displayText, string insertText, Color? textColor = null, Resource? icon = null, Variant value = default, int location = 1024) { }
    public void UpdateCodeCompletionOptions(bool force) { }
    public void RequestCodeCompletion(bool force = false) { }
}
