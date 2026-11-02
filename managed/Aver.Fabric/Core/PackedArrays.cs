// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// GDScript's Packed*Array methods over the C# arrays that stand for them. A C# array cannot grow, so the methods that change the length return the
// new array and the converted script assigns it back to the variable it came from (a.push_back(x) becomes a = PackedArrays.Append(a, x)).

namespace Aver.Fabric;

/// <summary>The methods of GDScript's packed arrays (PackedByteArray, PackedInt32Array, PackedStringArray, PackedVector3Array ...) for the arrays Aver.Fabric uses for them.
/// Methods that change the length return the new array; the others work in place. Indices follow GDScript: a negative index counts from the end.</summary>
public static class PackedArrays
{
    private static long Index(long i, int length) => i < 0 ? i + length : i;

    /// <summary>An Array where a packed array is wanted (Expression.parse(text, names), a function returning PackedStringArray): each element converted, as Godot does.</summary>
    public static T[] FromArray<T>(Collections.Array array) => array.Select(v => v.As<T>()).ToArray();

    /// <summary>A packed array where an Array is wanted.</summary>
    public static Collections.Array ToArray<T>(T[] packed) => new(packed.Select(x => Variant.From(x)));

    /// <summary>The array with <paramref name="value"/> added at the end (<c>push_back</c>, <c>append</c>).</summary>
    public static T[] Append<T>(T[] array, T value)
    {
        var r = new T[array.Length + 1];
        System.Array.Copy(array, r, array.Length);
        r[^1] = value;
        return r;
    }

    /// <summary>The array with all of <paramref name="other"/> added at the end (<c>append_array</c>).</summary>
    public static T[] AppendArray<T>(T[] array, T[] other)
    {
        var r = new T[array.Length + other.Length];
        System.Array.Copy(array, r, array.Length);
        System.Array.Copy(other, 0, r, array.Length, other.Length);
        return r;
    }

    /// <summary>The array with <paramref name="value"/> inserted before <paramref name="at"/> (<c>insert</c>); an index outside the array leaves it unchanged.</summary>
    public static T[] Insert<T>(T[] array, long at, T value)
    {
        long i = Index(at, array.Length);
        if (i < 0 || i > array.Length) return array;
        var r = new T[array.Length + 1];
        System.Array.Copy(array, 0, r, 0, (int)i);
        r[i] = value;
        System.Array.Copy(array, (int)i, r, (int)i + 1, array.Length - (int)i);
        return r;
    }

    /// <summary>The array without the element at <paramref name="at"/> (<c>remove_at</c>); an index outside the array leaves it unchanged.</summary>
    public static T[] RemoveAt<T>(T[] array, long at)
    {
        long i = Index(at, array.Length);
        if (i < 0 || i >= array.Length) return array;
        var r = new T[array.Length - 1];
        System.Array.Copy(array, 0, r, 0, (int)i);
        System.Array.Copy(array, (int)i + 1, r, (int)i, array.Length - (int)i - 1);
        return r;
    }

    /// <summary>The array cut or padded with default values to <paramref name="size"/> elements (<c>resize</c>); a negative size gives an empty array.</summary>
    public static T[] Resize<T>(T[] array, long size)
    {
        int n = (int)Math.Max(0, Math.Min(size, int.MaxValue));
        if (n == array.Length) return array;
        var r = new T[n];
        System.Array.Copy(array, r, Math.Min(n, array.Length));
        return r;
    }

    /// <summary>Sets every element to <paramref name="value"/> in place (<c>fill</c>).</summary>
    public static void Fill<T>(T[] array, T value) => System.Array.Fill(array, value);

    /// <summary>Sorts the numbers or strings in ascending order, in place (<c>sort</c>); strings compare by code unit, as Godot compares them.</summary>
    public static void Sort<T>(T[] array)
    {
        if (typeof(T) == typeof(string)) System.Array.Sort(array, (IComparer<T>)(object)StringComparer.Ordinal);
        else System.Array.Sort(array);
    }

    /// <summary>Reverses the order in place (<c>reverse</c>).</summary>
    public static void Reverse<T>(T[] array) => System.Array.Reverse(array);

    /// <summary>The index of the first element equal to <paramref name="value"/> at or after <paramref name="from"/>, or -1 (<c>find</c>).</summary>
    public static long Find<T>(T[] array, T value, long from = 0)
    {
        long start = Math.Max(0, Index(from, array.Length));
        for (long i = start; i < array.Length; i++)
            if (EqualityComparer<T>.Default.Equals(array[i], value)) return i;
        return -1;
    }

    /// <summary>How many elements equal <paramref name="value"/> (<c>count</c>).</summary>
    public static long Count<T>(T[] array, T value)
    {
        long n = 0;
        foreach (T x in array) if (EqualityComparer<T>.Default.Equals(x, value)) n++;
        return n;
    }

    /// <summary>A copy of the elements from <paramref name="begin"/> up to, not including, <paramref name="end"/> (<c>slice</c>); a negative index counts from the end.</summary>
    public static T[] Slice<T>(T[] array, long begin, long end = int.MaxValue)
    {
        long b = Math.Clamp(Index(begin, array.Length), 0, array.Length);
        long e = Math.Clamp(end == int.MaxValue ? array.Length : Index(end, array.Length), b, array.Length);
        var r = new T[e - b];
        System.Array.Copy(array, b, r, 0, r.Length);
        return r;
    }

    /// <summary>A copy of the array (<c>duplicate</c>).</summary>
    public static T[] Duplicate<T>(T[] array) => (T[])array.Clone();
}
