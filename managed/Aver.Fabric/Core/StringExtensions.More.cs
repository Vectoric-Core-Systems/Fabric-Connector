// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// The rest of GDScript's String methods: comparisons, slices, case-blind searches, wildcard matching, paths, escapes, hashes and byte buffers. Names are GDScript's
// in PascalCase (rsplit is Rsplit); integers are long and a character is its code, as in GDScript.

using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Aver.Fabric;

public static partial class StringExtensions
{
    // ---- comparison ----

    private static int Sign(int v) => v < 0 ? -1 : v > 0 ? 1 : 0;

    /// <summary>Compares by character code: -1, 0 or 1.</summary>
    public static long CasecmpTo(this string s, string to) => Sign(string.CompareOrdinal(s, to));

    /// <summary>Compares by character code ignoring case: -1, 0 or 1.</summary>
    public static long NocasecmpTo(this string s, string to) => Sign(string.Compare(s, to, StringComparison.OrdinalIgnoreCase));

    // A run of digits compares as a number, anything else by character code. A leading '.' or '_' sorts first when file is set.
    private static int Natural(string a, string b, bool ignoreCase, bool file)
    {
        int i = 0, j = 0;
        if (file)
        {
            bool aFirst = a.Length > 0 && (a[0] == '.' || a[0] == '_'), bFirst = b.Length > 0 && (b[0] == '.' || b[0] == '_');
            if (aFirst != bFirst) return aFirst ? -1 : 1;
        }
        while (i < a.Length && j < b.Length)
        {
            if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsAsciiDigit(a[i])) i++;
                while (j < b.Length && char.IsAsciiDigit(b[j])) j++;
                string na = a[si..i].TrimStart('0'), nb = b[sj..j].TrimStart('0');
                if (na.Length != nb.Length) return na.Length < nb.Length ? -1 : 1;
                int c = string.CompareOrdinal(na, nb);
                if (c != 0) return Sign(c);
                continue;
            }
            char ca = ignoreCase ? char.ToLowerInvariant(a[i]) : a[i], cb = ignoreCase ? char.ToLowerInvariant(b[j]) : b[j];
            if (ca != cb) return ca < cb ? -1 : 1;
            i++;
            j++;
        }
        if (i < a.Length) return 1;
        return j < b.Length ? -1 : 0;
    }

    /// <summary>Compares with runs of digits taken as numbers ("file2" before "file10"): -1, 0 or 1.</summary>
    public static long NaturalcasecmpTo(this string s, string to) => Natural(s, to, false, false);

    public static long NaturalnocasecmpTo(this string s, string to) => Natural(s, to, true, false);

    /// <summary>Like <see cref="NaturalcasecmpTo"/>, with names that start with '.' or '_' first: for sorting files.</summary>
    public static long FilecasecmpTo(this string s, string to) => Natural(s, to, false, true);

    public static long FilenocasecmpTo(this string s, string to) => Natural(s, to, true, true);

    // ---- slices and searches ----

    /// <summary>The slice-th part of the string split at <paramref name="delimiter"/>; empty past the last, or for an empty delimiter.</summary>
    public static string GetSlice(this string s, string delimiter, long slice)
    {
        if (delimiter.Length == 0 || slice < 0) return "";
        string[] parts = s.Split(delimiter, StringSplitOptions.None);
        return slice < parts.Length ? parts[slice] : "";
    }

    /// <summary>Like <see cref="GetSlice"/> with a one-character delimiter given by its code.</summary>
    public static string GetSlicec(this string s, long delimiter, long slice) => s.GetSlice(char.ConvertFromUtf32((int)delimiter), slice);

    /// <summary>How many parts the string has when split at the delimiter; 0 for an empty delimiter.</summary>
    public static long GetSliceCount(this string s, string delimiter) => delimiter.Length == 0 ? 0 : s.Split(delimiter, StringSplitOptions.None).Length;

    public static long Findn(this string s, string what, long from = 0) =>
        from < 0 || from > s.Length ? -1 : s.IndexOf(what, (int)from, StringComparison.OrdinalIgnoreCase);

    public static long Rfindn(this string s, string what, long from = -1) =>
        from < 0 ? s.LastIndexOf(what, StringComparison.OrdinalIgnoreCase) : s.LastIndexOf(what, (int)Math.Min(from + what.Length - 1, s.Length - 1), StringComparison.OrdinalIgnoreCase);

    public static long Countn(this string s, string what, long from = 0, long to = 0)
    {
        if (what.Length == 0) return 0;
        string area = s.Substr(from, to <= 0 ? -1 : to - from);
        long n = 0;
        for (int i = area.IndexOf(what, StringComparison.OrdinalIgnoreCase); i >= 0; i = area.IndexOf(what, i + what.Length, StringComparison.OrdinalIgnoreCase)) n++;
        return n;
    }

    public static bool Containsn(this string s, string what) => s.Contains(what, StringComparison.OrdinalIgnoreCase);

    // '*' matches any run of characters, '?' any one; the rest must be equal.
    private static bool Glob(string expr, int e, string text, int t, bool ignoreCase)
    {
        while (e < expr.Length)
        {
            char c = expr[e];
            if (c == '*')
            {
                while (e < expr.Length && expr[e] == '*') e++;
                if (e == expr.Length) return true;
                for (int k = t; k <= text.Length; k++) if (Glob(expr, e, text, k, ignoreCase)) return true;
                return false;
            }
            if (t >= text.Length) return false;
            if (c != '?')
            {
                char x = ignoreCase ? char.ToLowerInvariant(text[t]) : text[t], y = ignoreCase ? char.ToLowerInvariant(c) : c;
                if (x != y) return false;
            }
            e++;
            t++;
        }
        return t == text.Length;
    }

    /// <summary>Whether the string matches a pattern with '*' (any run of characters) and '?' (any one).</summary>
    public static bool Match(this string s, string expr) => expr.Length > 0 && s.Length > 0 && Glob(expr, 0, s, 0, false);

    public static bool Matchn(this string s, string expr) => expr.Length > 0 && s.Length > 0 && Glob(expr, 0, s, 0, true);

    private static bool Subsequence(string small, string big, bool ignoreCase)
    {
        if (small.Length == 0) return true;
        if (small.Length > big.Length) return false;
        int at = 0;
        foreach (char c in big)
            if (ignoreCase ? char.ToLowerInvariant(c) == char.ToLowerInvariant(small[at]) : c == small[at])
                if (++at == small.Length) return true;
        return false;
    }

    /// <summary>Whether all of this string's characters appear in <paramref name="text"/>, in order.</summary>
    public static bool IsSubsequenceOf(this string s, string text) => Subsequence(s, text, false);

    public static bool IsSubsequenceOfn(this string s, string text) => Subsequence(s, text, true);

    /// <summary>Every pair of neighbouring characters.</summary>
    public static string[] Bigrams(this string s)
    {
        var pairs = new string[Math.Max(0, s.Length - 1)];
        for (int i = 0; i < pairs.Length; i++) pairs[i] = s.Substring(i, 2);
        return pairs;
    }

    /// <summary>How alike two strings are, 0 to 1, from the pairs of neighbouring characters they share (the Sørensen–Dice coefficient). 1 for equal strings.</summary>
    public static double Similarity(this string s, string text)
    {
        if (s == text) return 1.0;
        string[] a = s.Bigrams(), b = text.Bigrams();
        double sum = a.Length + b.Length;
        if (sum == 0) return 0.0;
        double inter = 0;
        foreach (string x in a)
            if (b.Contains(x)) inter++;
        return 2.0 * inter / sum;
    }

    // ---- replacing and removing ----

    public static string Replacen(this string s, string what, string forwhat) => what.Length == 0 ? s : s.Replace(what, forwhat, StringComparison.OrdinalIgnoreCase);

    public static string ReplaceChar(this string s, long key, long with) =>
        s.Replace(char.ConvertFromUtf32((int)key), char.ConvertFromUtf32((int)with), StringComparison.Ordinal);

    public static string ReplaceChars(this string s, string keys, long with)
    {
        string w = char.ConvertFromUtf32((int)with);
        var sb = new StringBuilder(s.Length);
        foreach (char c in s) sb.Append(keys.Contains(c) ? w : c.ToString());
        return sb.ToString();
    }

    public static string RemoveChar(this string s, long what) => s.Replace(char.ConvertFromUtf32((int)what), "", StringComparison.Ordinal);

    public static string RemoveChars(this string s, string chars) => new(s.Where(c => !chars.Contains(c)).ToArray());

    public static string Reverse(this string s)
    {
        char[] c = s.ToCharArray();
        System.Array.Reverse(c);
        return new string(c);
    }

    public static string TrimPrefix(this string s, string prefix) => prefix.Length > 0 && s.StartsWith(prefix, StringComparison.Ordinal) ? s[prefix.Length..] : s;

    public static string TrimSuffix(this string s, string suffix) => suffix.Length > 0 && s.EndsWith(suffix, StringComparison.Ordinal) ? s[..^suffix.Length] : s;

    public static string Lstrip(this string s, string chars) => s.TrimStart(chars.ToCharArray());

    public static string Rstrip(this string s, string chars) => s.TrimEnd(chars.ToCharArray());

    /// <summary>Removes the control characters (codes 0 to 31) such as tabs and line breaks; spaces stay.</summary>
    public static string StripEscapes(this string s) => new(s.Where(c => c >= 32).ToArray());

    public static string Lpad(this string s, long minLength, string character = " ") => Pad(s, minLength, character, left: true);

    public static string Rpad(this string s, long minLength, string character = " ") => Pad(s, minLength, character, left: false);

    private static string Pad(string s, long minLength, string character, bool left)
    {
        if (character.Length == 0 || s.Length >= minLength) return s;
        var fill = new StringBuilder();
        while (s.Length + fill.Length < minLength) fill.Append(character);
        string f = fill.ToString()[..(int)(minLength - s.Length)];
        return left ? f + s : s + f;
    }

    // ---- case ----

    /// <summary>"snake_case_name" or "PascalCase" as "snakeCaseName".</summary>
    public static string ToCamelCase(this string s)
    {
        string pascal = s.ToPascalCase();
        return pascal.Length == 0 ? pascal : char.ToLowerInvariant(pascal[0]) + pascal[1..];
    }

    /// <summary>"PascalCase" or "snake_case" as "kebab-case".</summary>
    public static string ToKebabCase(this string s) => s.ToSnakeCase().Replace('_', '-');

    // ---- splitting ----

    /// <summary>Like Split, cutting from the right: with <paramref name="maxsplit"/> cuts the rest stays whole in the first part.</summary>
    public static string[] Rsplit(this string s, string delimiter, bool allowEmpty = true, long maxsplit = 0)
    {
        if (delimiter.Length == 0) return s.Select(c => c.ToString()).ToArray();
        var parts = new List<string>();
        int end = s.Length;
        while (maxsplit <= 0 || parts.Count < maxsplit)
        {
            int i = end - delimiter.Length < 0 ? -1 : s.LastIndexOf(delimiter, end - delimiter.Length, StringComparison.Ordinal);
            if (i < 0) break;
            parts.Add(s[(i + delimiter.Length)..end]);
            end = i;
        }
        parts.Add(s[..end]);
        parts.Reverse();
        return allowEmpty ? parts.ToArray() : parts.Where(p => p.Length > 0).ToArray();
    }

    /// <summary>The numbers in the string split at the delimiter (the PackedFloat64Array of split_floats); a part that is not a number is 0.</summary>
    public static double[] SplitFloats(this string s, string delimiter, bool allowEmpty = true) =>
        s.Split(delimiter, allowEmpty).Select(p => p.ToFloat()).ToArray();

    // ---- layout ----

    /// <summary>Puts <paramref name="prefix"/> in front of every line that is not empty.</summary>
    public static string Indent(this string s, string prefix) =>
        string.Join("\n", s.Split('\n').Select(l => l.Length == 0 ? l : prefix + l));

    /// <summary>Removes the leading whitespace every non-empty line shares.</summary>
    public static string Dedent(this string s)
    {
        string[] lines = s.Split('\n');
        string? common = null;
        foreach (string l in lines)
        {
            if (l.Trim().Length == 0) continue;
            string lead = new(l.TakeWhile(c => c == ' ' || c == '\t').ToArray());
            if (common == null) { common = lead; continue; }
            int n = 0;
            while (n < common.Length && n < lead.Length && common[n] == lead[n]) n++;
            common = common[..n];
        }
        if (string.IsNullOrEmpty(common)) return s;
        return string.Join("\n", lines.Select(l => l.StartsWith(common, StringComparison.Ordinal) ? l[common.Length..] : l.Trim().Length == 0 ? "" : l));
    }

    // ---- paths ----

    /// <summary>The path without the extension.</summary>
    public static string GetBasename(this string s)
    {
        int dot = s.LastIndexOf('.');
        return dot < 0 || dot < s.LastIndexOfAny(new[] { '/', '\\' }) ? s : s[..dot];
    }

    /// <summary>The path and the file joined by exactly one slash.</summary>
    public static string PathJoin(this string s, string file)
    {
        if (s.Length > 0 && s[^1] != '/' && s[^1] != '\\') return s + "/" + file;
        return s + file;
    }

    public static bool IsAbsolutePath(this string s) =>
        s.StartsWith('/') || s.StartsWith('\\') || s.Contains(":/", StringComparison.Ordinal) || s.Contains(":\\", StringComparison.Ordinal);

    public static bool IsRelativePath(this string s) => !s.IsAbsolutePath();

    /// <summary>The path with "." and ".." resolved and double slashes joined.</summary>
    public static string SimplifyPath(this string s)
    {
        string p = s.Replace('\\', '/');
        string root = "";
        int scheme = p.IndexOf("://", StringComparison.Ordinal);
        if (scheme > 0) { root = p[..(scheme + 3)]; p = p[(scheme + 3)..]; }
        else if (p.Length >= 2 && p[1] == ':' && char.IsAsciiLetter(p[0])) { int len = p.Length > 2 && p[2] == '/' ? 3 : 2; root = p[..len]; p = p[len..]; }
        else if (p.StartsWith('/')) { root = "/"; p = p.TrimStart('/'); }
        var stack = new List<string>();
        foreach (string part in p.Split('/'))
        {
            if (part.Length == 0 || part == ".") continue;
            if (part == ".." && stack.Count > 0 && stack[^1] != "..") { stack.RemoveAt(stack.Count - 1); continue; }
            if (part == ".." && root.Length > 0) continue;
            stack.Add(part);
        }
        return root + string.Join("/", stack);
    }

    // ---- escapes ----

    public static string XmlEscape(this string s, bool escapeQuotes = false)
    {
        string r = s.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);
        return escapeQuotes ? r.Replace("\"", "&quot;", StringComparison.Ordinal).Replace("'", "&apos;", StringComparison.Ordinal) : r;
    }

    public static string XmlUnescape(this string s) =>
        s.Replace("&lt;", "<", StringComparison.Ordinal).Replace("&gt;", ">", StringComparison.Ordinal).Replace("&quot;", "\"", StringComparison.Ordinal)
         .Replace("&apos;", "'", StringComparison.Ordinal).Replace("&amp;", "&", StringComparison.Ordinal);

    /// <summary>Percent-encodes everything but letters, digits and "-._~", as UTF-8.</summary>
    public static string UriEncode(this string s)
    {
        var sb = new StringBuilder();
        foreach (byte b in Encoding.UTF8.GetBytes(s))
        {
            char c = (char)b;
            if (b < 128 && (char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~')) sb.Append(c);
            else sb.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    private static string Percent(string s, bool plusIsSpace)
    {
        var bytes = new List<byte>();
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '%' && i + 2 < s.Length && Uri.IsHexDigit(s[i + 1]) && Uri.IsHexDigit(s[i + 2]))
            {
                bytes.Add(byte.Parse(s.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                i += 2;
            }
            else if (s[i] == '+' && plusIsSpace) bytes.Add((byte)' ');
            else bytes.AddRange(Encoding.UTF8.GetBytes(s[i].ToString()));
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    public static string UriDecode(this string s) => Percent(s, plusIsSpace: true);

    /// <summary>Like <see cref="UriDecode"/> for a file name: a "+" stays a plus.</summary>
    public static string UriFileDecode(this string s) => Percent(s, plusIsSpace: false);

    public static string CEscape(this string s) =>
        s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\a", "\\a", StringComparison.Ordinal).Replace("\b", "\\b", StringComparison.Ordinal)
         .Replace("\f", "\\f", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal)
         .Replace("\t", "\\t", StringComparison.Ordinal).Replace("\v", "\\v", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal)
         .Replace("\"", "\\\"", StringComparison.Ordinal);

    public static string CUnescape(this string s)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != '\\' || i + 1 >= s.Length) { sb.Append(s[i]); continue; }
            char e = s[++i];
            switch (e)
            {
                case 'a': sb.Append('\a'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'v': sb.Append('\v'); break;
                case 'x' when i + 2 < s.Length && Uri.IsHexDigit(s[i + 1]) && Uri.IsHexDigit(s[i + 2]):
                    sb.Append((char)byte.Parse(s.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    i += 2;
                    break;
                case 'u' when i + 4 < s.Length && int.TryParse(s.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int cp):
                    sb.Append((char)cp);
                    i += 4;
                    break;
                default: sb.Append(e); break;
            }
        }
        return sb.ToString();
    }

    public static string JsonEscape(this string s) =>
        s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\b", "\\b", StringComparison.Ordinal).Replace("\f", "\\f", StringComparison.Ordinal)
         .Replace("\n", "\\n", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal)
         .Replace("\v", "\\v", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    // ---- validity ----

    /// <summary>The string with the characters a node name may not have (. : @ / " %) removed.</summary>
    public static string ValidateNodeName(this string s) => new(s.Where(c => c is not ('.' or ':' or '@' or '/' or '"' or '%')).ToArray());

    private const string InvalidFileNameChars = "\\/:*?\"<>|";

    /// <summary>The string with every character a file name may not have (\ / : * ? " &lt; &gt; |) replaced by "_", and blanks trimmed from the ends.</summary>
    public static string ValidateFilename(this string s) => new string(s.Select(c => InvalidFileNameChars.Contains(c) ? '_' : c).ToArray()).Trim();

    public static bool IsValidFilename(this string s) =>
        s.Length > 0 && s == s.Trim() && !s.Any(c => InvalidFileNameChars.Contains(c) || c < 32);

    public static bool IsValidAsciiIdentifier(this string s) =>
        s.Length > 0 && !char.IsAsciiDigit(s[0]) && s.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    public static bool IsValidUnicodeIdentifier(this string s) =>
        s.Length > 0 && !char.IsDigit(s[0]) && s.All(c => char.IsLetterOrDigit(c) || c == '_');

    public static bool IsValidIdentifier(this string s) => s.IsValidAsciiIdentifier();

    public static bool IsValidHexNumber(this string s, bool withPrefix = false)
    {
        string t = s;
        if (t.StartsWith('-') || t.StartsWith('+')) t = t[1..];
        if (withPrefix)
        {
            if (!t.StartsWith("0x", StringComparison.Ordinal)) return false;
            t = t[2..];
        }
        return t.Length > 0 && t.All(Uri.IsHexDigit);
    }

    /// <summary>Whether the string is a colour in HTML form: "#rgb", "#rgba", "#rrggbb" or "#rrggbbaa" (the "#" is optional).</summary>
    public static bool IsValidHtmlColor(this string s)
    {
        string t = s.StartsWith('#') ? s[1..] : s;
        return t.Length is 3 or 4 or 6 or 8 && t.All(Uri.IsHexDigit);
    }

    public static bool IsValidIpAddress(this string s)
    {
        if (s.Length == 0) return false;
        if (s.Contains(':')) return IPAddress.TryParse(s, out IPAddress? a) && a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;
        string[] parts = s.Split('.');
        return parts.Length == 4 && parts.All(p => p.Length is >= 1 and <= 3 && p.All(char.IsAsciiDigit) && int.Parse(p, CultureInfo.InvariantCulture) <= 255);
    }

    // ---- numbers ----

    /// <summary>A hexadecimal number, with or without "0x" and a sign: "0xff" is 255; 0 if the text is not one.</summary>
    public static long HexToInt(this string s)
    {
        string t = s.Trim();
        bool negative = t.StartsWith('-');
        if (negative || t.StartsWith('+')) t = t[1..];
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t[2..];
        if (t.Length == 0 || t.Length > 16 || !t.All(Uri.IsHexDigit)) return 0;
        long v = unchecked((long)ulong.Parse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        return negative ? -v : v;
    }

    /// <summary>A binary number, with or without "0b" and a sign: "0b101" is 5; 0 if the text is not one.</summary>
    public static long BinToInt(this string s)
    {
        string t = s.Trim();
        bool negative = t.StartsWith('-');
        if (negative || t.StartsWith('+')) t = t[1..];
        if (t.StartsWith("0b", StringComparison.OrdinalIgnoreCase)) t = t[2..];
        if (t.Length == 0 || t.Length > 64 || !t.All(c => c is '0' or '1')) return 0;
        long v = unchecked((long)Convert.ToUInt64(t, 2));
        return negative ? -v : v;
    }

    /// <summary>A number as text: with <paramref name="decimals"/> given, exactly that many decimals; by default up to 14 significant digits with trailing zeros left off
    /// (5 is "5", 1.50 is "1.5").</summary>
    public static string Num(double number, long decimals = -1)
    {
        if (double.IsNaN(number)) return "nan";
        if (double.IsPositiveInfinity(number)) return "inf";
        if (double.IsNegativeInfinity(number)) return "-inf";
        if (decimals < 0) return number.ToString("G14", CultureInfo.InvariantCulture).Replace("E+", "e+", StringComparison.Ordinal).Replace("E-", "e-", StringComparison.Ordinal);
        return number.ToString("F" + Math.Min(decimals, 30).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    /// <summary>A number in scientific notation.</summary>
    public static string NumScientific(double number) => number.ToString("0.#####e+0", CultureInfo.InvariantCulture);

    /// <summary>A whole number in the given base (2 to 36).</summary>
    public static string NumInt64(long number, long @base = 10, bool capitalizeHex = false)
    {
        if (@base < 2 || @base > 36) return "";
        if (number == 0) return "0";
        bool negative = number < 0;
        ulong v = negative ? (ulong)(-(number + 1)) + 1UL : (ulong)number;
        return Digits(v, (int)@base, capitalizeHex, negative);
    }

    /// <summary>An unsigned whole number (the bits of <paramref name="number"/>) in the given base (2 to 36).</summary>
    public static string NumUint64(long number, long @base = 10, bool capitalizeHex = false) =>
        @base < 2 || @base > 36 ? "" : Digits(unchecked((ulong)number), (int)@base, capitalizeHex, false);

    private static string Digits(ulong v, int @base, bool upper, bool negative)
    {
        if (v == 0) return "0";
        var sb = new StringBuilder();
        while (v > 0)
        {
            int d = (int)(v % (ulong)@base);
            sb.Insert(0, (char)(d < 10 ? '0' + d : (upper ? 'A' : 'a') + d - 10));
            v /= (ulong)@base;
        }
        if (negative) sb.Insert(0, '-');
        return sb.ToString();
    }

    /// <summary>The character with the given code, as a string.</summary>
    public static string Chr(long code) => code is < 0 or > 0x10FFFF || (code >= 0xD800 && code <= 0xDFFF) ? "" : char.ConvertFromUtf32((int)code);

    /// <summary>A byte count as text in the largest fitting unit: "512 B", "1.5 KiB", "20 MiB".</summary>
    public static string HumanizeSize(long size)
    {
        string[] units = { "B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB" };
        ulong div = 1;
        int idx = 0;
        ulong bytes = (ulong)Math.Max(0, size);
        while (idx < units.Length - 1 && bytes > div * 1024UL) { div *= 1024UL; idx++; }
        long whole = idx > 0 ? (long)(bytes / div) : 0;
        int digits = idx == 0 ? 0 : whole < 100 ? 2 : whole < 1024 ? 1 : 0;
        double shown = idx > 0 ? (double)bytes / div : bytes;
        return Num(shown).PadDecimals(digits) + " " + units[idx];
    }

    // ---- hashes and buffers ----

    public static string Md5Text(this string s) => Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(s)));
    public static string Sha1Text(this string s) => Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(s)));
    public static string Sha256Text(this string s) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
    public static byte[] Md5Buffer(this string s) => MD5.HashData(Encoding.UTF8.GetBytes(s));
    public static byte[] Sha1Buffer(this string s) => SHA1.HashData(Encoding.UTF8.GetBytes(s));
    public static byte[] Sha256Buffer(this string s) => SHA256.HashData(Encoding.UTF8.GetBytes(s));

    /// <summary>The characters as ASCII bytes; a character outside ASCII becomes '?'.</summary>
    public static byte[] ToAsciiBuffer(this string s) => s.Select(c => c < 128 ? (byte)c : (byte)'?').ToArray();

    public static byte[] ToUtf8Buffer(this string s) => Encoding.UTF8.GetBytes(s);
    public static byte[] ToUtf16Buffer(this string s) => Encoding.Unicode.GetBytes(s);
    public static byte[] ToUtf32Buffer(this string s) => Encoding.UTF32.GetBytes(s);

    /// <summary>The wide characters of the platform: UTF-16 on Windows, UTF-32 elsewhere.</summary>
    public static byte[] ToWcharBuffer(this string s) => OperatingSystem.IsWindows() ? s.ToUtf16Buffer() : s.ToUtf32Buffer();

    /// <summary>The bytes of the string in a character set by name ("utf-8", "latin1" ...); UTF-8 for an empty or unknown name.</summary>
    public static byte[] ToMultibyteCharBuffer(this string s, string encoding = "")
    {
        try { return (encoding.Length == 0 ? Encoding.UTF8 : Encoding.GetEncoding(encoding)).GetBytes(s); }
        catch (ArgumentException) { return Encoding.UTF8.GetBytes(s); }
    }

    /// <summary>The bytes named by hex digit pairs ("ff00a1"); empty for text that is not even-length hex.</summary>
    public static byte[] HexDecode(this string s) =>
        s.Length % 2 == 0 && s.All(Uri.IsHexDigit) ? Convert.FromHexString(s) : System.Array.Empty<byte>();
}
