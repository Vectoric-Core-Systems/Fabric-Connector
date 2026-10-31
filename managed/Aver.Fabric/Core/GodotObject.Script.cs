// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Object.set_script and get_script. A converted script is a C# class, and an object cannot change its class after it is built, so a script given at run time
// is kept for get_script and its code is not attached.

namespace Aver.Fabric;

public abstract partial class GodotObject
{
    private Variant _script;

    /// <summary>The script given to <see cref="SetScript"/>; nil for an object that was never given one. The script a converted class was written as is not
    /// reported here: it is the class of the object.</summary>
    public Variant GetScript() => _script;

    /// <summary>Approximated, kept as set: Godot swaps the object's behaviour for the script's. Here the object keeps its class, so the script's methods and
    /// properties are not attached (the first call says so in the log); a script that sets itself up for an object it built needs to be a class the
    /// object is made from instead.</summary>
    public void SetScript(Variant script)
    {
        _script = script;
        if (!script.IsNil) Approximated.Once("Object.SetScript", "the object keeps its class; the script is kept for get_script and its code is not attached");
    }

    /// <summary>How many objects have been given an instance id and are still alive: what <see cref="Performance"/> reports as the object count.</summary>
    internal static int TrackedObjectCount
    {
        get { lock (s_byId) return s_byId.Count; }
    }
}
