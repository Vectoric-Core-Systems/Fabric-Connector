// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Godot's accessor methods whose property has another name (BaseMaterial3D.set_albedo sets albedo_color), from the extension_api.json the
// extension ships (data/). Read once, the first time a call by name finds no method: call("set_albedo", c) then sets the property.

using System.Text.Json;

namespace Aver.Fabric;

internal static class GodotAccessors
{
    private static Dictionary<string, (string? Inherits, Dictionary<string, string> Map)>? s_classes;
    private static string? s_loadedFrom;

    internal static string? PathOverride;

    /// <summary>The property Godot's <paramref name="method"/> sets or reads on <paramref name="t"/> (or a class above it), or null.</summary>
    internal static string? PropertyOf(System.Type t, string method)
    {
        Dictionary<string, (string? Inherits, Dictionary<string, string> Map)>? all = Classes();
        if (all == null) return null;
        for (System.Type? k = t; k != null && k != typeof(object); k = k.BaseType)
        {
            if (k.Assembly != typeof(GodotAccessors).Assembly) continue;
            for (string? c = k.Name; c != null && all.TryGetValue(c, out var cls); c = cls.Inherits)
                if (cls.Map.TryGetValue(method, out string? prop)) return prop;
        }
        return null;
    }

    private static Dictionary<string, (string? Inherits, Dictionary<string, string> Map)>? Classes()
    {
        string path = PathOverride ?? Path.Combine(Path.GetDirectoryName(typeof(GodotAccessors).Assembly.Location) ?? AppContext.BaseDirectory, "..", "data", "extension_api.json");
        if (s_loadedFrom == path) return s_classes;
        s_loadedFrom = path;
        s_classes = null;
        try
        {
            if (!File.Exists(path)) return null;
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(path));
            var all = new Dictionary<string, (string?, Dictionary<string, string>)>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonElement c in doc.RootElement.GetProperty("classes").EnumerateArray())
            {
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                if (c.TryGetProperty("properties", out JsonElement props))
                    foreach (JsonElement pr in props.EnumerateArray())
                        foreach (string acc in new[] { "setter", "getter" })
                            if (pr.TryGetProperty(acc, out JsonElement a) && a.GetString() is { Length: > 0 } method)
                                map.TryAdd(method, pr.GetProperty("name").GetString() ?? "");
                all[c.GetProperty("name").GetString() ?? ""] = (c.TryGetProperty("inherits", out JsonElement i) ? i.GetString() : null, map);
            }
            s_classes = all;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException) { s_classes = null; }
        return s_classes;
    }
}
