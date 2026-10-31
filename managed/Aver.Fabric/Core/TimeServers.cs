// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Godot's Time and Performance singletons: clocks, calendar conversions in Godot's dictionary and string forms, and the monitors a debug overlay reads. Over the BCL
// and the framework's counters; what the engine does not measure reports zero and the monitor's comment says so.

using System.Diagnostics;
using System.Globalization;

namespace Aver.Fabric;

/// <summary>Godot's Time singleton: the clock, and conversions between unix time, datetime dictionaries and ISO 8601 strings. Dictionaries use Godot's keys
/// (year, month, day, weekday, hour, minute, second, and dst for the system's local time); strings are "YYYY-MM-DDTHH:MM:SS" (a space instead of the T on request).</summary>
public static class Time
{
    /// <summary>Godot's Time.Month (MONTH_JANUARY = 1).</summary>
    public enum Month { January = 1, February = 2, March = 3, April = 4, May = 5, June = 6, July = 7, August = 8, September = 9, October = 10, November = 11, December = 12 }

    /// <summary>Godot's Time.Weekday (WEEKDAY_SUNDAY = 0).</summary>
    public enum Weekday { Sunday = 0, Monday = 1, Tuesday = 2, Wednesday = 3, Thursday = 4, Friday = 5, Saturday = 6 }

    private static readonly long s_startStamp = Stopwatch.GetTimestamp();
    private static readonly long s_ageAtStartUsec = ProcessAgeUsec();

    private static long ProcessAgeUsec()
    {
        try
        {
            using Process p = Process.GetCurrentProcess();
            return Math.Max(0L, (long)(DateTime.Now - p.StartTime).TotalMicroseconds);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception or PlatformNotSupportedException) { return 0L; }
    }

    // ---- clocks ----

    /// <summary>Microseconds since the program started.</summary>
    public static ulong GetTicksUsec() => (ulong)(s_ageAtStartUsec + (long)((Stopwatch.GetTimestamp() - s_startStamp) * 1_000_000.0 / Stopwatch.Frequency));

    /// <summary>Milliseconds since the program started.</summary>
    public static ulong GetTicksMsec() => GetTicksUsec() / 1000UL;

    /// <summary>Seconds since 1970-01-01 UTC, with the fraction.</summary>
    public static double GetUnixTimeFromSystem() => (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;

    // ---- from unix time ----

    private static DateTime FromUnix(long unix)
    {
        try { return DateTime.UnixEpoch.AddSeconds(unix); }
        catch (ArgumentOutOfRangeException) { return DateTime.UnixEpoch; }
    }

    private static Collections.Dictionary DatetimeDict(DateTime t, bool dst = false, bool withDst = false)
    {
        var d = new Collections.Dictionary
        {
            { "year", (long)t.Year }, { "month", (long)t.Month }, { "day", (long)t.Day }, { "weekday", (long)(int)t.DayOfWeek },
            { "hour", (long)t.Hour }, { "minute", (long)t.Minute }, { "second", (long)t.Second },
        };
        if (withDst) d["dst"] = dst;
        return d;
    }

    private static Collections.Dictionary DateDict(DateTime t, bool dst = false, bool withDst = false)
    {
        var d = new Collections.Dictionary { { "year", (long)t.Year }, { "month", (long)t.Month }, { "day", (long)t.Day }, { "weekday", (long)(int)t.DayOfWeek } };
        if (withDst) d["dst"] = dst;
        return d;
    }

    private static Collections.Dictionary TimeDict(DateTime t) =>
        new() { { "hour", (long)t.Hour }, { "minute", (long)t.Minute }, { "second", (long)t.Second } };

    public static Collections.Dictionary GetDatetimeDictFromUnixTime(long unixTimeVal) => DatetimeDict(FromUnix(unixTimeVal));
    public static Collections.Dictionary GetDateDictFromUnixTime(long unixTimeVal) => DateDict(FromUnix(unixTimeVal));
    public static Collections.Dictionary GetTimeDictFromUnixTime(long unixTimeVal) => TimeDict(FromUnix(unixTimeVal));

    private static string DateText(DateTime t) => t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string TimeText(DateTime t) => t.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    public static string GetDatetimeStringFromUnixTime(long unixTimeVal, bool useSpace = false)
    {
        DateTime t = FromUnix(unixTimeVal);
        return DateText(t) + (useSpace ? " " : "T") + TimeText(t);
    }

    public static string GetDateStringFromUnixTime(long unixTimeVal) => DateText(FromUnix(unixTimeVal));
    public static string GetTimeStringFromUnixTime(long unixTimeVal) => TimeText(FromUnix(unixTimeVal));

    // ---- strings and dictionaries ----

    private static bool Key(Collections.Dictionary d, string key, out long value)
    {
        value = 0;
        if (!d.TryGetValue(key, out Variant v)) return false;
        value = v.AsInt64();
        return true;
    }

    private static long KeyOr(Collections.Dictionary d, string key, long fallback) => Key(d, key, out long v) ? v : fallback;

    /// <summary>The datetime dictionary as a point in time, read as UTC. A key that is missing counts as the start of 1970.</summary>
    private static DateTime FromDict(Collections.Dictionary d)
    {
        long year = KeyOr(d, "year", 1970), month = Math.Clamp(KeyOr(d, "month", 1), 1, 12), day = Math.Max(1, KeyOr(d, "day", 1));
        long hour = KeyOr(d, "hour", 0), minute = KeyOr(d, "minute", 0), second = KeyOr(d, "second", 0);
        try
        {
            year = Math.Clamp(year, 1, 9999);
            DateTime start = new((int)year, (int)month, 1, 0, 0, 0, DateTimeKind.Utc);
            // A day past the end of the month carries over, as it does in Godot's arithmetic.
            return start.AddDays(day - 1).AddHours(hour).AddMinutes(minute).AddSeconds(second);
        }
        catch (ArgumentOutOfRangeException) { return DateTime.UnixEpoch; }
    }

    public static long GetUnixTimeFromDatetimeDict(Collections.Dictionary datetime)
    {
        if (datetime.Count == 0)
        {
            Aver.Scripting.Log.Error("[Fabric] Time.GetUnixTimeFromDatetimeDict: the dictionary is empty");
            return 0;
        }
        return (long)Math.Floor((FromDict(datetime) - DateTime.UnixEpoch).TotalSeconds);
    }

    public static string GetDatetimeStringFromDatetimeDict(Collections.Dictionary datetime, bool useSpace)
    {
        DateTime t = FromDict(datetime);
        return DateText(t) + (useSpace ? " " : "T") + TimeText(t);
    }

    /// <summary>Reads "YYYY-MM-DDTHH:MM:SS", "YYYY-MM-DD HH:MM:SS", a date alone or a time alone into a dictionary of the keys that were given;
    /// <paramref name="weekday"/> adds the weekday of a date. A string that does not read gives an empty dictionary and an error in the log.</summary>
    public static Collections.Dictionary GetDatetimeDictFromDatetimeString(string datetime, bool weekday)
    {
        var d = new Collections.Dictionary();
        string text = datetime.Trim();
        int split = text.IndexOfAny(new[] { 'T', ' ' });
        string date = "", time = "";
        if (split >= 0) { date = text[..split]; time = text[(split + 1)..]; }
        else if (text.Contains('-')) date = text;
        else time = text;
        if (date.Length > 0)
        {
            string[] p = date.Split('-');
            if (p.Length != 3 || !int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y) ||
                !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int m) || !int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int day))
            {
                Aver.Scripting.Log.Error($"[Fabric] Time.GetDatetimeDictFromDatetimeString: \"{datetime}\" is not a date");
                return new Collections.Dictionary();
            }
            d["year"] = (long)y;
            d["month"] = (long)m;
            d["day"] = (long)day;
            if (weekday)
            {
                try { d["weekday"] = (long)(int)new DateTime(Math.Clamp(y, 1, 9999), Math.Clamp(m, 1, 12), 1, 0, 0, 0, DateTimeKind.Utc).AddDays(Math.Max(1, day) - 1).DayOfWeek; }
                catch (ArgumentOutOfRangeException) { d["weekday"] = 0L; }
            }
        }
        if (time.Length > 0)
        {
            string[] p = time.Split(':');
            if (p.Length != 3 || !int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int h) ||
                !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int mi) || !int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int s))
            {
                Aver.Scripting.Log.Error($"[Fabric] Time.GetDatetimeDictFromDatetimeString: \"{datetime}\" is not a time");
                return new Collections.Dictionary();
            }
            d["hour"] = (long)h;
            d["minute"] = (long)mi;
            d["second"] = (long)s;
        }
        return d;
    }

    public static long GetUnixTimeFromDatetimeString(string datetime) => GetUnixTimeFromDatetimeDict(GetDatetimeDictFromDatetimeString(datetime, false));

    /// <summary>"+HH:MM" or "-HH:MM" for an offset in minutes from UTC.</summary>
    public static string GetOffsetStringFromOffsetMinutes(long offsetMinutes)
    {
        long a = Math.Abs(offsetMinutes);
        return (offsetMinutes < 0 ? "-" : "+") + (a / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (a % 60).ToString("00", CultureInfo.InvariantCulture);
    }

    // ---- the system's clock ----

    private static DateTime Now(bool utc) => utc ? DateTime.UtcNow : DateTime.Now;

    private static bool IsDst(bool utc) => !utc && TimeZoneInfo.Local.IsDaylightSavingTime(DateTime.Now);

    public static Collections.Dictionary GetDatetimeDictFromSystem(bool utc = false) => DatetimeDict(Now(utc), IsDst(utc), withDst: true);
    public static Collections.Dictionary GetDateDictFromSystem(bool utc = false) => DateDict(Now(utc), IsDst(utc), withDst: true);
    public static Collections.Dictionary GetTimeDictFromSystem(bool utc = false) => TimeDict(Now(utc));

    public static string GetDatetimeStringFromSystem(bool utc = false, bool useSpace = false)
    {
        DateTime t = Now(utc);
        return DateText(t) + (useSpace ? " " : "T") + TimeText(t);
    }

    public static string GetDateStringFromSystem(bool utc = false) => DateText(Now(utc));
    public static string GetTimeStringFromSystem(bool utc = false) => TimeText(Now(utc));

    /// <summary>The local time zone: "bias" is its offset from UTC in minutes (east is positive, as in Godot 4) and "name" its name.</summary>
    public static Collections.Dictionary GetTimeZoneFromSystem()
    {
        TimeZoneInfo zone = TimeZoneInfo.Local;
        DateTime now = DateTime.Now;
        bool dst = zone.IsDaylightSavingTime(now);
        return new Collections.Dictionary { { "bias", (long)zone.GetUtcOffset(now).TotalMinutes }, { "name", dst ? zone.DaylightName : zone.StandardName } };
    }
}

/// <summary>Godot's Performance singleton: numbers a debug overlay shows, and custom monitors a script adds. The engine measures some of these;
/// the rest report zero (each is noted on <see cref="Monitor"/>).</summary>
public static class Performance
{
    /// <summary>Godot's Performance.Monitor. Measured: TimeFps, TimeProcess (the last frame's duration), MemoryStatic and its peak, the node, object and 3D body
    /// counts (<see cref="ObjectCount"/> counts the objects that have an instance id, so it is a lower bound). Not measured, reported as zero: the physics and
    /// navigation process times, resource and orphan counts, everything the renderer counts, pipeline compilations, collision pairs and islands, audio latency
    /// and the navigation map counts.</summary>
    public enum Monitor
    {
        TimeFps = 0, TimeProcess = 1, TimePhysicsProcess = 2, TimeNavigationProcess = 3, MemoryStatic = 4, MemoryStaticMax = 5, MemoryMessageBufferMax = 6,
        ObjectCount = 7, ObjectResourceCount = 8, ObjectNodeCount = 9, ObjectOrphanNodeCount = 10, RenderTotalObjectsInFrame = 11, RenderTotalPrimitivesInFrame = 12,
        RenderTotalDrawCallsInFrame = 13, RenderVideoMemUsed = 14, RenderTextureMemUsed = 15, RenderBufferMemUsed = 16, Physics2DActiveObjects = 17,
        Physics2DCollisionPairs = 18, Physics2DIslandCount = 19, Physics3DActiveObjects = 20, Physics3DCollisionPairs = 21, Physics3DIslandCount = 22,
        AudioOutputLatency = 23, NavigationActiveMaps = 24, NavigationRegionCount = 25, NavigationAgentCount = 26, NavigationLinkCount = 27, NavigationPolygonCount = 28,
        NavigationEdgeCount = 29, NavigationEdgeMergeCount = 30, NavigationEdgeConnectionCount = 31, NavigationEdgeFreeCount = 32, NavigationObstacleCount = 33,
        PipelineCompilationsCanvas = 34, PipelineCompilationsMesh = 35, PipelineCompilationsSurface = 36, PipelineCompilationsDraw = 37, PipelineCompilationsSpecialization = 38,
        Navigation2DActiveMaps = 39, Navigation2DRegionCount = 40, Navigation2DAgentCount = 41, Navigation2DLinkCount = 42, Navigation2DPolygonCount = 43,
        Navigation2DEdgeCount = 44, Navigation2DEdgeMergeCount = 45, Navigation2DEdgeConnectionCount = 46, Navigation2DEdgeFreeCount = 47, Navigation2DObstacleCount = 48,
        Navigation3DActiveMaps = 49, Navigation3DRegionCount = 50, Navigation3DAgentCount = 51, Navigation3DLinkCount = 52, Navigation3DPolygonCount = 53,
        Navigation3DEdgeCount = 54, Navigation3DEdgeMergeCount = 55, Navigation3DEdgeConnectionCount = 56, Navigation3DEdgeFreeCount = 57, Navigation3DObstacleCount = 58,
        Max = 59,
    }

    private sealed class CustomMonitor
    {
        public Callable Callable;
        public Collections.Array Arguments = new();
    }

    private static readonly Dictionary<string, CustomMonitor> s_custom = new(StringComparer.Ordinal);
    private static ulong s_modified;

    /// <summary>The monitor's value. Time monitors are seconds, memory monitors bytes, the rest counts; see <see cref="Monitor"/> for what is measured.</summary>
    public static double GetMonitor(Monitor monitor)
    {
        switch (monitor)
        {
            case Monitor.TimeFps: return Engine.GetFramesPerSecond();
            case Monitor.TimeProcess: return Input.FrameDelta;
            case Monitor.MemoryStatic: return OS.GetStaticMemoryUsage();
            case Monitor.MemoryStaticMax: return OS.GetStaticMemoryPeakUsage();
            case Monitor.ObjectCount: return Math.Max(GodotObject.TrackedObjectCount, NodeCount());
            case Monitor.ObjectNodeCount: return NodeCount();
            case Monitor.Physics3DActiveObjects: return BodyCount();
            default: return 0.0;
        }
    }

    private static int NodeCount() => SceneTree.Instance.GetNodeCount();

    // The bodies of the engine's physics world: all of them, awake or not (the engine reports no awake count to scripts).
    private static int BodyCount()
    {
        try { return Aver.Physics.Physics.Ready ? Aver.Physics.Physics.BodyCount : 0; }
        catch (Exception ex) when (PhysicsClockNative.Missing(ex)) { return 0; }
    }

    /// <summary>Adds a monitor whose value is what <paramref name="callable"/> returns, called with <paramref name="arguments"/> each time it is read.</summary>
    public static void AddCustomMonitor(StringName id, Callable callable, Collections.Array? arguments = null)
    {
        string key = id.ToString();
        if (s_custom.ContainsKey(key))
        {
            Aver.Scripting.Log.Error($"[Fabric] Performance.AddCustomMonitor: a monitor named \"{key}\" exists already");
            return;
        }
        s_custom[key] = new CustomMonitor { Callable = callable, Arguments = arguments ?? new Collections.Array() };
        s_modified = Time.GetTicksUsec();
    }

    public static void RemoveCustomMonitor(StringName id)
    {
        if (s_custom.Remove(id.ToString())) s_modified = Time.GetTicksUsec();
        else Aver.Scripting.Log.Error($"[Fabric] Performance.RemoveCustomMonitor: no monitor named \"{id}\"");
    }

    public static bool HasCustomMonitor(StringName id) => s_custom.ContainsKey(id.ToString());

    /// <summary>The value of a custom monitor: its callable's result; nil for a name no monitor has.</summary>
    public static Variant GetCustomMonitor(StringName id)
    {
        if (!s_custom.TryGetValue(id.ToString(), out CustomMonitor? m))
        {
            Aver.Scripting.Log.Error($"[Fabric] Performance.GetCustomMonitor: no monitor named \"{id}\"");
            return default;
        }
        return m.Callable.Callv(m.Arguments);
    }

    public static StringName[] GetCustomMonitorNames() => s_custom.Keys.Select(k => new StringName(k)).ToArray();

    /// <summary>When a custom monitor was last added or removed, in <see cref="Time.GetTicksUsec"/> microseconds: a monitor list redraws when this changes.</summary>
    public static ulong GetMonitorModificationTime() => s_modified;

    internal static void ResetForTests()
    {
        s_custom.Clear();
        s_modified = 0;
    }
}
