// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Godot's JSON: text to a Variant and back. Parsing is strict JSON (System.Text.Json) and, as in Godot, every number comes out as a float; stringifying writes
// the types JSON has and the text of anything else.

using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Aver.Fabric;

/// <summary>Reads and writes JSON (Godot's JSON).</summary>
public class JSON : Resource
{
    private string _text = "";
    private int _errorLine;
    private string _errorMessage = "";

    /// <summary>The parsed data, or whatever a script set.</summary>
    public Variant Data { get; set; }

    /// <summary>Parses <paramref name="jsonText"/> into <see cref="Data"/>. <see cref="Error.ParseError"/> for bad text (the line and message are then kept).</summary>
    public Error Parse(string jsonText, bool keepText = false)
    {
        _text = keepText ? jsonText : "";
        _errorLine = 0;
        _errorMessage = "";
        Data = default;
        if (TryParse(jsonText, out Variant v, out int line, out string message))
        {
            Data = v;
            return Error.Ok;
        }
        _errorLine = line;
        _errorMessage = message;
        return Error.ParseError;
    }

    /// <summary>The text given to <see cref="Parse"/> with keepText, else empty.</summary>
    public string GetParsedText() => _text;

    /// <summary>The line (counting from 1) where the last <see cref="Parse"/> failed; 0 when it worked.</summary>
    public int GetErrorLine() => _errorLine;

    public string GetErrorMessage() => _errorMessage;

    /// <summary>The data a JSON text holds, or nil if it is not JSON. Numbers are floats.</summary>
    public static Variant ParseString(string jsonString) => TryParse(jsonString, out Variant v, out _, out _) ? v : default;

    private static bool TryParse(string text, out Variant value, out int line, out string message)
    {
        value = default;
        line = 0;
        message = "";
        try
        {
            using JsonDocument doc = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 512 });
            value = FromElement(doc.RootElement);
            return true;
        }
        catch (JsonException ex)
        {
            line = (int)(ex.LineNumber ?? 0) + 1;
            message = ex.Message;
            return false;
        }
    }

    private static Variant FromElement(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.True: return true;
            case JsonValueKind.False: return false;
            case JsonValueKind.Number: return e.GetDouble();
            case JsonValueKind.String: return e.GetString() ?? "";
            case JsonValueKind.Array:
            {
                var a = new Collections.Array();
                foreach (JsonElement item in e.EnumerateArray()) a.Add(FromElement(item));
                return a;
            }
            case JsonValueKind.Object:
            {
                var d = new Collections.Dictionary();
                foreach (JsonProperty p in e.EnumerateObject()) d[p.Name] = FromElement(p.Value);
                return d;
            }
            default: return default;
        }
    }

    // ---- writing ----

    /// <summary>The data as JSON text. With an <paramref name="indent"/> (say "  " or "\t") the text is laid out over lines; without, it is compact. Dictionary keys are
    /// sorted unless <paramref name="sortKeys"/> is off. Floats are written with 14 digits, or as many as it takes to read back exactly with <paramref name="fullPrecision"/>.
    /// A type JSON does not have (a vector, a color) is written as its text in a string.</summary>
    public static string Stringify(Variant data, string indent = "", bool sortKeys = true, bool fullPrecision = false)
    {
        var sb = new StringBuilder();
        Write(sb, data, indent, 0, sortKeys, fullPrecision);
        return sb.ToString();
    }

    private static void Newline(StringBuilder sb, string indent, int depth)
    {
        if (indent.Length == 0) return;
        sb.Append('\n');
        for (int i = 0; i < depth; i++) sb.Append(indent);
    }

    private static void Quote(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }

    private static string Number(double d, bool full)
    {
        if (double.IsNaN(d) || double.IsInfinity(d)) return "null";
        string t = d.ToString(full ? "R" : "G14", CultureInfo.InvariantCulture);
        if (t.IndexOfAny(new[] { '.', 'E', 'e' }) < 0) t += ".0";
        return t;
    }

    private static void Write(StringBuilder sb, Variant v, string indent, int depth, bool sortKeys, bool full)
    {
        switch (v.VariantType)
        {
            case Variant.Type.Nil: sb.Append("null"); return;
            case Variant.Type.Bool: sb.Append(v.AsBool() ? "true" : "false"); return;
            case Variant.Type.Int: sb.Append(v.AsInt64().ToString(CultureInfo.InvariantCulture)); return;
            case Variant.Type.Float: sb.Append(Number(v.AsDouble(), full)); return;
            case Variant.Type.String:
            case Variant.Type.StringName:
            case Variant.Type.NodePath: Quote(sb, v.AsString()); return;
            case Variant.Type.Array:
            {
                Collections.Array a = v.AsGodotArray();
                if (a.Count == 0) { sb.Append("[]"); return; }
                sb.Append('[');
                for (int i = 0; i < a.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    Newline(sb, indent, depth + 1);
                    Write(sb, a[i], indent, depth + 1, sortKeys, full);
                }
                Newline(sb, indent, depth);
                sb.Append(']');
                return;
            }
            case Variant.Type.Dictionary:
            {
                Collections.Dictionary d = v.AsGodotDictionary();
                if (d.Count == 0) { sb.Append("{}"); return; }
                var keys = d.Keys.Select(k => (Key: k, Text: k.AsString())).ToList();
                if (sortKeys) keys.Sort((x, y) => string.CompareOrdinal(x.Text, y.Text));
                sb.Append('{');
                for (int i = 0; i < keys.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    Newline(sb, indent, depth + 1);
                    Quote(sb, keys[i].Text);
                    sb.Append(indent.Length == 0 ? ":" : ": ");
                    Write(sb, d[keys[i].Key], indent, depth + 1, sortKeys, full);
                }
                Newline(sb, indent, depth);
                sb.Append('}');
                return;
            }
            default: Quote(sb, v.AsString()); return;
        }
    }
}
