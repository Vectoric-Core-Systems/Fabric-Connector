// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// A class used as a value. GDScript lets a script hold a class like any object (`var kind = Enemy`, `[Node]`, `parent == Enemy`, `set_script(Tool)`);
// C# cannot, so the converter writes GDScript.ClassObject(typeof(X)): one object per class, equal only to itself, that builds an instance on new().

using System.Collections.Concurrent;

namespace Aver.Fabric;

/// <summary>The object standing for one class (Godot's Script or GDScriptNativeClass when used as a value).</summary>
public sealed class ClassValue : RefCounted
{
    internal ClassValue(Type type) { ClassType = type; }

    /// <summary>The C# class it stands for.</summary>
    public Type ClassType { get; }

    /// <summary>A new instance of the class, as `Class.new()` makes one.</summary>
    public Variant New() => Activator.CreateInstance(ClassType) is GodotObject o ? o : default;

    public string GetGlobalName() => ClassType.Name;
    public bool CanInstantiate() => !ClassType.IsAbstract && ClassType.GetConstructor(Type.EmptyTypes) != null;

    public override string ToString() => "<" + ClassType.Name + ">";
}

public static partial class GDScript
{
    private static readonly ConcurrentDictionary<Type, ClassValue> s_classValues = new();

    /// <summary>The object for a class used as a value: the same one each time for one class.</summary>
    public static ClassValue ClassObject(Type type) => s_classValues.GetOrAdd(type, t => new ClassValue(t));
}
