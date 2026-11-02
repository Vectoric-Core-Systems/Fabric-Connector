// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Godot's ResourceSaver for the resources a game saves itself (a save slot's player state, a custom Resource holding settings): written as
// the value the converter writes for a custom resource, the "__class" dictionary of its exported members, which ResourceLoader.load reads back.

using System.Reflection;

namespace Aver.Fabric;

public static class ResourceSaver
{
    [Flags]
    public enum SaverFlags : long
    {
        None = 0, RelativePaths = 1, BundleResources = 2, ChangePath = 4, OmitEditorProperties = 8, SaveBigEndian = 16, Compress = 32,
        ReplaceSubresourcePaths = 64,
    }

    /// <summary>Writes the resource to <paramref name="path"/> (its own path when empty). Resources it holds that have a path of their own are
    /// referred to by it; the others are written inside it.</summary>
    public static Error Save(Resource resource, string path = "", SaverFlags flags = SaverFlags.None)
    {
        if (resource == null) return Error.InvalidParameter;
        if (path.Length == 0) path = resource.ResourcePath;
        if (path.Length == 0) return Error.InvalidParameter;
        string file = FabricPaths.Globalize(path);
        try
        {
            if (Path.GetDirectoryName(file) is { Length: > 0 } dir) Directory.CreateDirectory(dir);
            File.WriteAllText(file, VariantText.Write(ValueOf(resource, top: true)) + "\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Aver.Scripting.Log.Warn($"[Fabric] ResourceSaver.save: cannot write '{path}': {ex.Message}");
            return Error.FileCantWrite;
        }
        if ((flags & SaverFlags.ChangePath) != 0) resource.ResourcePath = path;
        ResourceLoader.Forget(path);
        return Error.Ok;
    }

    // An object as the value the file holds: a resource with a path of its own (not the one being saved) is that path, any other one its members.
    private static Variant ValueOf(GodotObject o, bool top)
    {
        if (!top && o is Resource { ResourcePath.Length: > 0 }) return Variant.From(o);
        var d = new Collections.Dictionary { { "__class", o.GetType().Name } };
        for (System.Type? t = o.GetType(); t != null && t != typeof(GodotObject); t = t.BaseType)
        {
            foreach (MemberInfo m in t.GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                if (m.GetCustomAttribute<ExportAttribute>() == null || d.ContainsKey(m.Name)) continue;
                object? value = m switch
                {
                    PropertyInfo { CanRead: true } p when p.GetIndexParameters().Length == 0 => p.GetValue(o),
                    FieldInfo f => f.GetValue(o),
                    _ => null,
                };
                d[m.Name] = Nested(Variant.FromObject(value));
            }
        }
        return d;
    }

    private static Variant Nested(Variant v)
    {
        switch (v.VariantType)
        {
            case Variant.Type.Object when v.AsGodotObject() is Resource r: return ValueOf(r, top: false);
            case Variant.Type.Array:
            {
                var a = new Collections.Array();
                foreach (Variant x in v.AsGodotArray()) a.Add(Nested(x));
                return a;
            }
            case Variant.Type.Dictionary:
            {
                var d = new Collections.Dictionary();
                foreach (KeyValuePair<Variant, Variant> kv in v.AsGodotDictionary()) d[kv.Key] = Nested(kv.Value);
                return d;
            }
            default: return v;
        }
    }
}
