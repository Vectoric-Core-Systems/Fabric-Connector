// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// A member Aver cannot honour in full keeps what a script gave it and says so once in the log, so a game that depends on it shows up in a run.

namespace Aver.Fabric;

internal static class Approximated
{
    private static readonly HashSet<string> s_said = new(StringComparer.Ordinal);

    /// <summary>Logs, the first time only, that <paramref name="member"/> is approximated and why.</summary>
    internal static void Once(string member, string why)
    {
        lock (s_said)
        {
            if (!s_said.Add(member)) return;
        }
        Aver.Scripting.Log.Info($"[Fabric] {member}: {why}");
    }

    internal static void ResetForTests()
    {
        lock (s_said) s_said.Clear();
    }
}
