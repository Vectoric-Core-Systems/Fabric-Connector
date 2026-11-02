// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Godot 4's own classes, from the extension_api.json the extension ships beside the converter (data/): what the emitter asks when Aver.Fabric has no
// member a typed script names, to tell one Godot lacks too (a Godot 3 name, `mat.flags_unshaded`) from one Aver.Fabric lacks.

using System.Text.Json;

namespace Aver.Fabric.Convert.GdScript;

public static class GodotApi
{
    private sealed class Cls
    {
        public string? Inherits;
        public readonly HashSet<string> Members = new(StringComparer.Ordinal);
        public readonly Dictionary<string, string> Accessors = new(StringComparer.Ordinal);   // set_albedo -> albedo_color
    }

    /// <summary>The file to read instead of the one beside the converter (tests).</summary>
    public static string? PathOverride { get; set; }

    private static Dictionary<string, Cls>? s_classes;
    private static string? s_loadedFrom;

    private static Dictionary<string, Cls>? Classes()
    {
        string path = PathOverride ?? Path.Combine(Path.GetDirectoryName(typeof(GodotApi).Assembly.Location) ?? AppContext.BaseDirectory, "..", "data", "extension_api.json");
        if (s_loadedFrom == path) return s_classes;
        s_loadedFrom = path;
        s_classes = null;
        try
        {
            if (!File.Exists(path)) return null;
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(path));
            var all = new Dictionary<string, Cls>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonElement c in doc.RootElement.GetProperty("classes").EnumerateArray())
            {
                var cls = new Cls { Inherits = c.TryGetProperty("inherits", out JsonElement i) ? i.GetString() : null };
                foreach (string list in new[] { "methods", "properties", "signals", "constants" })
                    if (c.TryGetProperty(list, out JsonElement items))
                        foreach (JsonElement m in items.EnumerateArray()) cls.Members.Add(m.GetProperty("name").GetString() ?? "");
                if (c.TryGetProperty("properties", out JsonElement props))
                    foreach (JsonElement pr in props.EnumerateArray())
                        foreach (string acc in new[] { "setter", "getter" })
                            if (pr.TryGetProperty(acc, out JsonElement a) && a.GetString() is { Length: > 0 } method)
                                cls.Accessors.TryAdd(method, pr.GetProperty("name").GetString() ?? "");
                if (c.TryGetProperty("enums", out JsonElement enums))
                    foreach (JsonElement e in enums.EnumerateArray())
                    {
                        cls.Members.Add(e.GetProperty("name").GetString() ?? "");
                        foreach (JsonElement v in e.GetProperty("values").EnumerateArray()) cls.Members.Add(v.GetProperty("name").GetString() ?? "");
                    }
                all[c.GetProperty("name").GetString() ?? ""] = cls;
            }
            s_classes = all;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException) { s_classes = null; }
        return s_classes;
    }

    /// <summary>Whether Godot's class (or one of its bases) has the member: null when the API is not at hand or does not know the class.</summary>
    public static bool? Has(string className, string member)
    {
        Dictionary<string, Cls>? all = Classes();
        if (all == null || !all.ContainsKey(className)) return null;
        for (string? c = className; c != null && all.TryGetValue(c, out Cls? cls); c = cls.Inherits)
            if (cls.Members.Contains(member)) return true;
        return false;
    }

    /// <summary>The property a Godot accessor method sets or reads (BaseMaterial3D.set_albedo is albedo_color's setter), or null.</summary>
    public static string? AccessorProperty(string className, string method)
    {
        Dictionary<string, Cls>? all = Classes();
        if (all == null) return null;
        for (string? c = className; c != null && all.TryGetValue(c, out Cls? cls); c = cls.Inherits)
            if (cls.Accessors.TryGetValue(method, out string? prop)) return prop;
        return null;
    }
}
