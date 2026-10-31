// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Package API, part three: the language-level members. String methods (F73), the vector, colour, collection and callable leftovers (F74), members and methods of
// values whose type is not known (F75), and GDScript snippets converted, compiled and run to show the converter reaches all of it.

using Aver.Fabric;
using Aver.Fabric.Convert;
using Collections = Aver.Fabric.Collections;

static partial class FabricApiTests
{
    // ---- F73 ----

    private static void StringMethods()
    {
        T.Check("casecmp_to compares by code, nocasecmp_to ignores case", "Hello".CasecmpTo("hello") == -1 && "b".CasecmpTo("a") == 1 && "a".CasecmpTo("a") == 0 && "Hello".NocasecmpTo("hELLO") == 0);
        T.Check("the natural comparisons read digit runs as numbers", "file2".NaturalcasecmpTo("file10") == -1 && "file10".NaturalcasecmpTo("file2") == 1 && "a1".NaturalnocasecmpTo("A01") == 0 && "File3".NaturalnocasecmpTo("file20") == -1);
        T.Check("the file comparisons put '.' and '_' first", ".git".FilecasecmpTo("abc") == -1 && "_x".FilenocasecmpTo("A") == -1 && "abc".FilecasecmpTo(".git") == 1);

        T.Check("get_slice, get_slicec and get_slice_count", "a,b,c".GetSlice(",", 1) == "b" && "a,b,c".GetSlice(",", 5) == "" && "a,b,c".GetSlicec(',', 2) == "c" && "a,b,c".GetSliceCount(",") == 3 && "abc".GetSliceCount("") == 0);
        T.Check("find, count and rfind with the case ignored", "Hello World".Findn("WORLD") == 6 && "Hello World".Countn("L") == 3 && "abcabc".Rfindn("BC") == 4 && "Hello".Containsn("ELL") && "Hello World".Findn("o", 5) == 7);
        T.Check("rsplit cuts from the right", "a,b,c,d".Rsplit(",", true, 1).SequenceEqual(new[] { "a,b,c", "d" }) && "a,b,c,d".Rsplit(",").SequenceEqual(new[] { "a", "b", "c", "d" }) &&
            "a,,b".Rsplit(",", false).SequenceEqual(new[] { "a", "b" }) && "a,b,c".Rsplit(",", true, 2).SequenceEqual(new[] { "a", "b", "c" }));
        T.Check("split_floats", "1.5;2;x".SplitFloats(";").SequenceEqual(new[] { 1.5, 2.0, 0.0 }));

        T.Check("match and matchn take * and ?", "abc123".Match("abc*") && "abc".Match("a?c") && !"abcd".Match("a?c") && "ABC".Matchn("a*c") && !"ABC".Match("a*c") && "x".Match("*") && "xyz".Match("*z") && !"xyz".Match("*y"));
        T.Check("subsequences", "hlo".IsSubsequenceOf("hello") && !"olh".IsSubsequenceOf("hello") && "HLO".IsSubsequenceOfn("hello"));
        T.Check("bigrams and similarity", "Hello".Bigrams().SequenceEqual(new[] { "He", "el", "ll", "lo" }) && Math.Abs("night".Similarity("nacht") - 0.25) < 1e-9 && "same".Similarity("same") == 1.0);

        T.Check("replacing and removing", "Hello".Replacen("L", "_") == "He__o" && "a-b-c".ReplaceChar('-', '_') == "a_b_c" && "a-b_c".ReplaceChars("-_", '.') == "a.b.c" &&
            "banana".RemoveChar('a') == "bnn" && "hello".RemoveChars("lo") == "he");
        T.Check("reverse, trim_prefix, trim_suffix, lstrip, rstrip", "abc".Reverse() == "cba" && "prefix_name".TrimPrefix("prefix_") == "name" && "name.txt".TrimSuffix(".txt") == "name" && "xxabcxx".Lstrip("x") == "abcxx" && "xxabcxx".Rstrip("x") == "xxabc" &&
            "no".TrimPrefix("zzz") == "no");
        T.Check("strip_escapes drops control characters, not spaces", "a\tb\nc d".StripEscapes() == "abc d");
        T.Check("lpad and rpad", "7".Lpad(3, "0") == "007" && "7".Rpad(3, "ab") == "7ab" && "long".Lpad(2) == "long" && "x".Lpad(3) == "  x");
        T.Check("case styles", "snake_case_name".ToCamelCase() == "snakeCaseName" && "SnakeCase".ToKebabCase() == "snake-case" && "snake_case".ToPascalCase() == "SnakeCase");
        T.Check("indent and dedent", "a\n\nb".Indent("> ") == "> a\n\n> b" && "  a\n   b".Dedent() == "a\n b" && "\ta\n\t\tb".Dedent() == "a\n\tb");

        T.Check("get_basename, path_join, simplify_path", "res://a/b.txt".GetBasename() == "res://a/b" && "dir.d/file".GetBasename() == "dir.d/file" && "a".PathJoin("b") == "a/b" && "a/".PathJoin("b") == "a/b" &&
            "a/./b/../c".SimplifyPath() == "a/c" && "res://a/../b".SimplifyPath() == "res://b" && "/x//y/".SimplifyPath() == "/x/y");
        T.Check("absolute and relative paths", "/x".IsAbsolutePath() && "res://x".IsAbsolutePath() && "C:/x".IsAbsolutePath() && "x/y".IsRelativePath());

        T.Check("xml escapes", "<a&b>".XmlEscape() == "&lt;a&amp;b&gt;" && "\"q\"".XmlEscape(true) == "&quot;q&quot;" && "&lt;x&amp;".XmlUnescape() == "<x&");
        T.Check("uri escapes", "a b/é".UriEncode() == "a%20b%2F%C3%A9" && "a%20b+c".UriDecode() == "a b c" && "a+b%2B".UriFileDecode() == "a+b+" && "x-._~".UriEncode() == "x-._~");
        T.Check("c escapes round-trip", "a\n\"b\"\t".CEscape() == "a\\n\\\"b\\\"\\t" && "a\\n\\\"b\\\"\\t".CUnescape() == "a\n\"b\"\t" && "\\x41\\u0042".CUnescape() == "AB");
        T.Check("json_escape", "a\"b\\\n".JsonEscape() == "a\\\"b\\\\\\n");

        T.Check("validate_node_name and validate_filename", "a.b:c@d/e\"f%g".ValidateNodeName() == "abcdefg" && "a:b*c".ValidateFilename() == "a_b_c");
        T.Check("identifiers", "abc_1".IsValidIdentifier() && !"1abc".IsValidIdentifier() && !"a b".IsValidIdentifier() && "naïve".IsValidUnicodeIdentifier() && !"naïve".IsValidAsciiIdentifier());
        T.Check("hex numbers, colours, addresses, file names", "ff".IsValidHexNumber() && "-0xFF".IsValidHexNumber(true) && !"ff".IsValidHexNumber(true) && !"fg".IsValidHexNumber() && "#ff0".IsValidHtmlColor() && "ff00ff80".IsValidHtmlColor() &&
            !"#ff".IsValidHtmlColor() && "192.168.0.1".IsValidIpAddress() && !"256.1.1.1".IsValidIpAddress() && "::1".IsValidIpAddress() && !"1.2.3".IsValidIpAddress() && "a.txt".IsValidFilename() &&
            !"a:b".IsValidFilename() && !" a".IsValidFilename());

        T.Check("hex_to_int and bin_to_int", "0xff".HexToInt() == 255 && "-0x10".HexToInt() == -16 && "FF".HexToInt() == 255 && "zz".HexToInt() == 0 && "0b101".BinToInt() == 5 && "-0b11".BinToInt() == -3 && "12".BinToInt() == 0);
        T.Check("num, num_int64 and chr", StringExtensions.Num(3.14159, 3) == "3.142" && StringExtensions.Num(5.0) == "5" && StringExtensions.Num(1.5) == "1.5" && StringExtensions.NumInt64(255, 16) == "ff" &&
            StringExtensions.NumInt64(255, 16, true) == "FF" && StringExtensions.NumInt64(-10, 2) == "-1010" && StringExtensions.NumUint64(-1, 16) == "ffffffffffffffff" && StringExtensions.Chr(65) == "A");
        T.Check("humanize_size", StringExtensions.HumanizeSize(512) == "512 B" && StringExtensions.HumanizeSize(1536) == "1.50 KiB" && StringExtensions.HumanizeSize(5L * 1024 * 1024) == "5.00 MiB" && StringExtensions.HumanizeSize(300L * 1024 * 1024) == "300.0 MiB");
        T.Check("pad_decimals now cuts as well as pads", "1.23456".PadDecimals(2) == "1.23" && "1.2".PadDecimals(3) == "1.200" && "5".PadDecimals(2) == "5.00" && "1.9".PadDecimals(0) == "1");

        T.Check("hashes", "abc".Md5Text() == "900150983cd24fb0d6963f7d28e17f72" && "abc".Sha1Text() == "a9993e364706816aba3e25717850c26c9cd0d89d" &&
            "abc".Sha256Text() == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad" && "abc".Md5Buffer().Length == 16 && "abc".Sha1Buffer().Length == 20 && "abc".Sha256Buffer().Length == 32);
        T.Check("byte buffers", "é".ToUtf8Buffer().SequenceEqual(new byte[] { 0xC3, 0xA9 }) && "aé".ToAsciiBuffer().SequenceEqual(new byte[] { 97, (byte)'?' }) && "a".ToUtf16Buffer().SequenceEqual(new byte[] { 97, 0 }) &&
            "a".ToUtf32Buffer().Length == 4 && "ff00".HexDecode().SequenceEqual(new byte[] { 255, 0 }) && "xyz".HexDecode().Length == 0 && "é".ToMultibyteCharBuffer("utf-8").Length == 2);
    }

    private static void StringMethodsConverted()
    {
        ConversionResult r = Convert(
            "extends Node\n\nfunc typed() -> Array:\n\tvar s := \"a,b,c,d\"\n\tvar out := []\n\tout.append(s.rsplit(\",\", true, 1))\n\tout.append(s.get_slice(\",\", 1))\n\tout.append(s.get_slice_count(\",\"))\n" +
            "\tout.append(\"7\".lpad(3, \"0\"))\n\tout.append(\"Hello\".casecmp_to(\"hello\"))\n\tout.append(\"prefix_name\".trim_prefix(\"prefix_\"))\n\tout.append(\"snake_case\".to_camel_case())\n" +
            "\tout.append(\"a/./b/../c\".simplify_path())\n\tout.append(\"abc\".md5_text())\n\tout.append(String.num(1.5))\n\tvar n: int = \"0xff\".hex_to_int()\n\tout.append(n + 1)\n\treturn out\n\n" +
            "func untyped(v) -> Array:\n\tvar out := []\n\tout.append(v.rsplit(\",\", true, 1))\n\tout.append(v.get_slice(\",\", 2))\n\tout.append(v.to_pascal_case())\n\tout.append(v.trim_suffix(\"d\"))\n\treturn out\n");
        T.Check("it compiles (before: every one of these was an error)", r.Success, Errors(r));
        if (!r.Success) return;
        var typed = (Collections.Array)Call(r, "Sample", "Typed")!;
        T.Check("rsplit with a limit and get_slice on a typed string", typed[0].AsStringArray().SequenceEqual(new[] { "a,b,c", "d" }) && typed[1].AsString() == "b" && typed[2].AsInt64() == 4);
        T.Check("lpad, casecmp_to, trim_prefix, to_camel_case", typed[3].AsString() == "007" && typed[4].AsInt64() == -1 && typed[5].AsString() == "name" && typed[6].AsString() == "snakeCase");
        T.Check("simplify_path, md5_text, hex_to_int", typed[7].AsString() == "a/c" && typed[8].AsString() == "900150983cd24fb0d6963f7d28e17f72" && typed[10].AsInt64() == 256);
        var untyped = (Collections.Array)Call(r, "Sample", "Untyped", Variant.From("a,b,cd"))!;
        T.Check("the same by name on a string whose type is not known", untyped[0].AsStringArray().SequenceEqual(new[] { "a,b", "cd" }) && untyped[1].AsString() == "cd" && untyped[2].AsString() == "A,b,cd" && untyped[3].AsString() == "a,b,c");
    }

    // ---- F74 ----

    private static void MathLeftovers()
    {
        var v3 = new Vector3(1.4f, 2.6f, -3.5f);
        T.Check("Vector3 snappedf, clampf, minf, maxf, posmodv", v3.Snappedf(0.5f) == new Vector3(1.5f, 2.5f, -3.5f) && v3.Clampf(0f, 2f) == new Vector3(1.4f, 2f, 0f) && v3.Minf(2f) == new Vector3(1.4f, 2f, -3.5f) &&
            v3.Maxf(0f) == new Vector3(1.4f, 2.6f, 0f) && new Vector3(5, -1, 7).Posmodv(new Vector3(3, 3, 4)) == new Vector3(2, 2, 3));
        T.Check("Vector2 and Vector4 too", new Vector2(1.4f, 2.6f).Snappedf(0.5f) == new Vector2(1.5f, 2.5f) && new Vector2(5, -1).Clampf(0, 2) == new Vector2(2, 0) && new Vector4(1, 5, 9, -2).Minf(4) == new Vector4(1, 4, 4, -2) &&
            new Vector4(1.2f, 2.7f, 0f, 9f).Snappedf(1f) == new Vector4(1, 3, 0, 9) && new Vector4(5, 6, 7, 8).Posmodv(new Vector4(2, 4, 5, 3)) == new Vector4(1, 2, 2, 2));
        T.Check("integer vectors: clampi, mini, maxi, snappedi", new Vector2I(5, -5).Clampi(-2, 2) == new Vector2I(2, -2) && new Vector3I(5, -5, 1).Mini(2) == new Vector3I(2, -5, 1) && new Vector3I(5, -5, 1).Maxi(0) == new Vector3I(5, 0, 1) &&
            new Vector2I(7, 12).Snappedi(5) == new Vector2I(5, 10) && new Vector4I(1, 9, 3, 7).Clampi(2, 5) == new Vector4I(2, 5, 3, 5) && new Vector4I(1, 2, 3, 4).DistanceSquaredTo(new Vector4I(2, 2, 3, 6)) == 5);

        // Barry-Goldman with times (-1, 0, 1, 2) is the uniform Catmull-Rom curve cubic_interpolate draws.
        float inTime = Mathf.CubicInterpolateInTime(2f, 5f, 1f, 9f, 0.3f, 1f, -1f, 2f), plain = Mathf.CubicInterpolate(2f, 5f, 1f, 9f, 0.3f);
        T.Check("cubic_interpolate_in_time at even spacing equals cubic_interpolate", MathF.Abs(inTime - plain) < 1e-4f, $"{inTime} vs {plain}");
        T.Check("and at the ends of the span it passes through the points", MathF.Abs(Mathf.CubicInterpolateInTime(2f, 5f, 1f, 9f, 0f, 1f, -1f, 2f) - 2f) < 1e-5f && MathF.Abs(Mathf.CubicInterpolateInTime(2f, 5f, 1f, 9f, 1f, 1f, -1f, 2f) - 5f) < 1e-5f);
        var v2 = new Vector3(0, 0, 0);
        Vector3 slope = v2.BezierDerivative(new Vector3(1, 0, 0), new Vector3(2, 0, 0), new Vector3(3, 0, 0), 0.5f);
        T.Check("bezier_derivative of a straight curve is its constant slope", MathF.Abs(slope.X - 3f) < 1e-4f && MathF.Abs(new Vector2(0, 0).BezierDerivative(new Vector2(1, 0), new Vector2(2, 0), new Vector2(3, 0), 0.25f).X - 3f) < 1e-4f);

        Vector3 dir = new Vector3(0.3f, -0.4f, 0.866f).Normalized();
        Vector3 back = Vector3.OctahedronDecode(dir.OctahedronEncode());
        T.Check("octahedron_encode packs a unit vector into 0..1 and decode restores it", dir.OctahedronEncode() is { X: >= 0f and <= 1f, Y: >= 0f and <= 1f } && (back - dir).Length() < 1e-4f);
        Vector3 down = new Vector3(0.2f, 0.1f, -0.9f).Normalized();
        T.Check("also for the far hemisphere", (Vector3.OctahedronDecode(down.OctahedronEncode()) - down).Length() < 1e-4f);

        var red = new Color(1f, 0f, 0f, 1f);
        T.Check("Color get_luminance, to_argb64 and to_abgr64", MathF.Abs(new Color(1, 1, 1).GetLuminance() - 1f) < 1e-5f && red.ToArgb64() == 0xFFFFFFFF00000000UL && new Color(0, 0, 1, 1).ToAbgr64() == 0xFFFFFFFF00000000UL);
        T.Check("hex64 and hex", Color.Hex64(0xFFFF000000008000UL).R == 1f && MathF.Abs(Color.Hex64(0xFFFF000000008000UL).A - 0.5f) < 1e-3f && Color.Hex(0xFF0000FFu) == red);
        T.Check("from_rgba8 and from_string", Color.FromRgba8(255, 128, 0).R == 1f && MathF.Abs(Color.FromRgba8(255, 128, 0).G - 128f / 255f) < 1e-6f && Color.FromString("red", Colors.Blue) == Colors.Red && Color.FromString("nonsense", Colors.Blue) == Colors.Blue);
        T.Check("from_rgbe9995", Color.FromRgbe9995(256u | (15u << 27)) is { R: 0.5f, G: 0f, A: 1f });

        var rot = new Quaternion(Vector3.Back, MathF.PI / 2f);
        Quaternion log = rot.Log();
        T.Check("quaternion log is the axis times the angle with no real part", MathF.Abs(log.Z - MathF.PI / 2f) < 1e-4f && log.W == 0f && MathF.Abs(log.X) < 1e-5f);
        T.Check("and exp undoes it", T.Near(log.Exp(), rot) && new Quaternion(0, 0, 0, 0).Exp() == Quaternion.Identity);

        var rect = new Rect2(10, 20, 30, 40);
        T.Check("Rect2 grow_side and get_support", rect.GrowSide(Side.Left, 5) == new Rect2(5, 20, 35, 40) && rect.GrowSide(Side.Bottom, 5) == new Rect2(10, 20, 30, 45) && rect.GetSupport(new Vector2(1, -1)) == new Vector2(40, 20));
        var ri = new Rect2I(0, 0, 4, 4);
        T.Check("Rect2I grow_individual, grow_side, expand, intersection", ri.GrowIndividual(1, 2, 3, 4) == new Rect2I(-1, -2, 8, 10) && ri.GrowSide(Side.Top, 2) == new Rect2I(0, -2, 4, 6) && ri.Expand(new Vector2I(6, -1)) == new Rect2I(0, -1, 6, 5) &&
            ri.Intersection(new Rect2I(2, 2, 6, 6)) == new Rect2I(2, 2, 2, 2) && ri.Intersection(new Rect2I(9, 9, 1, 1)) == default);

        var t2 = new Transform2D(0.5f, new Vector2(3, 4));
        T.Check("Transform2D get_origin and is_conformal", t2.GetOrigin() == new Vector2(3, 4) && t2.IsConformal() && !new Transform2D(new Vector2(1, 0), new Vector2(1, 1), Vector2.Zero).IsConformal());
    }

    private static void CollectionLeftovers()
    {
        var a = new Collections.Array { 1L, 3L, 5L, 7L, 9L };
        T.Check("pop_at takes from the middle or counts from the end", a.PopAt(1).AsInt64() == 3 && a.Count == 4 && a.PopAt(-1).AsInt64() == 9 && a.Count == 3);
        T.Logs.Clear();
        T.Check("past either end it is an error and the default", a.PopAt(9).IsNil && T.ErrorCount("out of range") == 1);
        T.Check("find_custom and rfind_custom take a test and a start", a.FindCustom(Callable.From<long, bool>(x => x > 2)) == 1 && a.FindCustom(Callable.From<long, bool>(x => x > 2), 2) == 2 && a.RfindCustom(Callable.From<long, bool>(x => x < 6)) == 1 &&
            a.FindCustom(Callable.From<long, bool>(x => x > 100)) == -1);
        var sorted = new Collections.Array { 1L, 3L, 3L, 5L };
        T.Check("bsearch finds the place before or after equal elements", sorted.Bsearch(3L) == 1 && sorted.Bsearch(3L, false) == 3 && sorted.Bsearch(0L) == 0 && sorted.Bsearch(9L) == 4);
        T.Check("bsearch_custom orders by a callable", sorted.BsearchCustom(4L, Callable.From<long, long, bool>((x, y) => x < y)) == 3);
        var typed = new Collections.Array<int> { 10, 20, 30 };
        T.Check("typed arrays have them too", typed.PopAt(0) == 10 && typed.FindCustom(x => x == 30) == 1 && typed.Bsearch(25) == 1);
        T.Check("hash agrees for equal arrays and differs for different ones", new Collections.Array { 1L, 2L }.Hash() == new Collections.Array { 1L, 2L }.Hash() && new Collections.Array { 1L, 2L }.Hash() != new Collections.Array { 2L, 1L }.Hash());

        var d1 = new Collections.Dictionary { { "a", 1L }, { "b", 2L } };
        var d2 = new Collections.Dictionary { { "b", 20L }, { "c", 3L } };
        Collections.Dictionary merged = d1.Merged(d2);
        Collections.Dictionary overwritten = d1.Merged(d2, true);
        T.Check("merged keeps both operands whole and lets the first win unless told otherwise", merged.Count == 3 && merged["b"].AsInt64() == 2 && overwritten["b"].AsInt64() == 20 && d1.Count == 2 && d2["b"].AsInt64() == 20);
        T.Check("has_all, find_key, get_or_add", d1.HasAll(new Collections.Array { "a", "b" }) && !d1.HasAll(new Collections.Array { "a", "z" }) && d1.FindKey(2L).AsString() == "b" && d1.FindKey(99L).IsNil &&
            d1.GetOrAdd("n", 5L).AsInt64() == 5 && d1["n"].AsInt64() == 5 && d1.GetOrAdd("n", 6L).AsInt64() == 5);
        T.Check("a dictionary's hash does not depend on the order of its entries", new Collections.Dictionary { { "x", 1L }, { "y", 2L } }.Hash() == new Collections.Dictionary { { "y", 2L }, { "x", 1L } }.Hash());
        var typedDict = new Collections.Dictionary<string, int> { { "a", 1 } };
        Collections.Dictionary<string, int> typedMerged = typedDict.Merged(new Collections.Dictionary<string, int> { { "a", 9 }, { "b", 2 } }, true);
        T.Check("a typed dictionary merges to a typed dictionary", typedMerged.Count == 2 && typedMerged["a"] == 9 && typedDict["a"] == 1);

        var node = new Node { Name = "N" };
        Callable c = new(node, "queue_free");
        T.Check("callable: object, object id, method, standard", ReferenceEquals(c.GetObject(), node) && c.GetObjectId() == node.GetInstanceId() && c.GetMethod() == "queue_free" && c.IsStandard());
        T.Check("callable: argument count of a method and of a delegate", c.GetArgumentCount() == 0 && Callable.From<long, long, long>((x, y) => x + y).GetArgumentCount() == 2);
        Callable bound = Callable.From<long, long, long>((x, y) => x * 10 + y).Bindv(new Collections.Array { 7L });
        T.Check("bindv appends the array's values; the call then needs one argument less", bound.Call(4L).AsInt64() == 47 && bound.GetArgumentCount() == 1 && bound.GetBoundArgumentsCount() == 1 && bound.GetBoundArguments()[0].AsInt64() == 7);
        Callable unbound = bound.Unbind(2);
        T.Check("unbind adds the arguments it drops", unbound.GetUnboundArgumentsCount() == 2 && unbound.GetArgumentCount() == 3);
        T.Check("equal callables hash alike; create makes a standard one; a lambda is not standard", c.Hash() == new Callable(node, "queue_free").Hash() && Callable.Create(node, "free").GetMethod() == "free" &&
            !Callable.From(() => { }).IsStandard() && Callable.Create(5L, "x").IsNull);

        var path = new NodePath("a/b/c:p:q");
        T.Check("NodePath slice counts names, then property parts", path.Slice(1, 3).ToString() == "b/c" && path.Slice(1).ToString() == "b/c:p:q" && path.Slice(-2).ToString() == ":p:q" && path.Slice(0, 2).ToString() == "a/b");
        T.Check("NodePath hash agrees for equal paths", path.Hash() == new NodePath("a/b/c:p:q").Hash());
    }

    // ---- F75 ----

    private static void DynamicDispatch()
    {
        var xf = new Transform3D(Basis.Identity, new Vector3(1, 2, 3));
        T.Check("a transform's origin and basis", GDScript.GetMember(xf, "origin").AsVector3() == new Vector3(1, 2, 3) && GDScript.GetMember(xf, "basis").AsBasis().X == Vector3.Right);
        var box = new Aabb(new Vector3(1, 1, 1), new Vector3(2, 4, 6));
        T.Check("a box's position, size and end", GDScript.GetMember(box, "position").AsVector3() == new Vector3(1, 1, 1) && GDScript.GetMember(box, "size").AsVector3() == new Vector3(2, 4, 6) && GDScript.GetMember(box, "end").AsVector3() == new Vector3(3, 5, 7));
        T.Check("a rectangle's end, a plane's normal and distance, a colour's bytes", GDScript.GetMember(new Rect2(1, 2, 3, 4), "end").AsVector2() == new Vector2(4, 6) && GDScript.GetMember(new Plane(0, 1, 0, 5), "normal").AsVector3() == Vector3.Up &&
            GDScript.GetMember(new Plane(0, 1, 0, 5), "d").AsDouble() == 5.0 && GDScript.GetMember(new Color(1, 0, 0), "r8").AsInt64() == 255);
        T.Check("integer vectors and quaternions", GDScript.GetMember(new Vector2I(3, 4), "y").AsInt64() == 4 && GDScript.GetMember(new Vector3I(1, 2, 3), "z").AsInt64() == 3 && GDScript.GetMember(new Quaternion(0, 0, 0, 1), "w").AsDouble() == 1.0 &&
            GDScript.GetMember(new Vector4I(1, 2, 3, 4), "w").AsInt64() == 4);
        T.Check("a name a value does not have is still an error", Throws(() => GDScript.GetMember(box, "nonsense")) && Throws(() => GDScript.GetMember(new Vector2(1, 2), "z")));

        Variant moved = GDScript.WithMember(xf, "origin", new Vector3(9, 8, 7));
        T.Check("with_member gives the value with the member changed, leaving the original", moved.AsTransform3D().Origin == new Vector3(9, 8, 7) && xf.Origin == new Vector3(1, 2, 3));
        T.Check("a vector's component, a box's size, a colour's alpha, a rectangle's end", GDScript.WithMember(new Vector3(1, 2, 3), "y", 9).AsVector3() == new Vector3(1, 9, 3) && GDScript.WithMember(box, "size", new Vector3(1, 1, 1)).AsAabb().Size == Vector3.One &&
            GDScript.WithMember(new Color(1, 0, 0, 1), "a", 0.25).AsColor().A == 0.25f && GDScript.WithMember(new Rect2(0, 0, 1, 1), "end", new Vector2(5, 5)).AsRect2().Size == new Vector2(5, 5));
        var dict = new Collections.Dictionary();
        T.Check("a dictionary and an object are changed in place and given back", GDScript.WithMember(dict, "k", 3).AsGodotDictionary() == dict && dict["k"].AsInt64() == 3);
        var node = new Node3D();
        GDScript.WithMember(node, "position", new Vector3(1, 1, 1));
        T.Check("an object's property is set by name", node.Position == new Vector3(1, 1, 1));
        T.Check("a value with no such member, or a number, is an error", Throws(() => GDScript.WithMember(box, "nonsense", 1)) && Throws(() => GDScript.WithMember(5, "x", 1)));
        T.Check("set_member on a value is still refused (it cannot change the caller's copy)", Throws(() => GDScript.SetMember(box, "size", Vector3.One)));

        T.Check("methods of a box", GDScript.CallMethod(box, "has_point", new Vector3(2, 2, 2)).AsBool() && !GDScript.CallMethod(box, "has_point", new Vector3(0, 0, 0)).AsBool() && GDScript.CallMethod(box, "get_volume").AsDouble() == 48.0);
        T.Check("methods of a plane, a rectangle, a transform, an integer vector", Math.Abs(GDScript.CallMethod(new Plane(0, 1, 0, 0), "distance_to", new Vector3(0, 3, 0)).AsDouble() - 3.0) < 1e-6 &&
            GDScript.CallMethod(new Rect2(0, 0, 4, 2), "get_center").AsVector2() == new Vector2(2, 1) && GDScript.CallMethod(xf, "inverse").AsTransform3D().Origin == new Vector3(-1, -2, -3) &&
            GDScript.CallMethod(new Vector2I(3, 4), "length_squared").AsInt64() == 25 && GDScript.CallMethod(new Rect2I(0, 0, 2, 3), "get_area").AsInt64() == 6);
        T.Check("methods of a vector whose scalar forms are new", GDScript.CallMethod(new Vector3(1.4f, 2.6f, 0), "snappedf", 0.5).AsVector3() == new Vector3(1.5f, 2.5f, 0) && GDScript.CallMethod(new Color(1, 1, 1), "get_luminance").AsDouble() > 0.99);
        T.Check("string methods beyond the short list", GDScript.CallMethod("a,b,c,d", "rsplit", ",", true, 1).AsStringArray().SequenceEqual(new[] { "a,b,c", "d" }) && GDScript.CallMethod("snake_case", "to_pascal_case").AsString() == "SnakeCase" &&
            GDScript.CallMethod("x", "lpad", 3, "0").AsString() == "00x" && GDScript.CallMethod(new StringName("Hello"), "casecmp_to", "hello").AsInt64() == -1 && GDScript.CallMethod("abc", "md5_text").AsString().Length == 32);
        var arr = new Collections.Array { 1L, 3L, 5L };
        T.Check("array methods beyond the short list", GDScript.CallMethod(arr, "bsearch", 4).AsInt64() == 2 && GDScript.CallMethod(arr, "pop_at", 0).AsInt64() == 1 && GDScript.CallMethod(arr, "duplicate_deep").AsGodotArray().Count == 2 &&
            GDScript.CallMethod(arr, "find_custom", Callable.From<long, bool>(x => x == 5)).AsInt64() == 1);
        T.Check("dictionary methods beyond the short list", GDScript.CallMethod(new Collections.Dictionary { { "a", 1L } }, "merged", new Collections.Dictionary { { "b", 2L } }).AsGodotDictionary().Count == 2 &&
            GDScript.CallMethod(new Collections.Dictionary { { "a", 1L } }, "find_key", 1).AsString() == "a");
        T.Check("callable and path methods", GDScript.CallMethod(Callable.From<long, long>(x => x), "get_argument_count").AsInt64() == 1 && GDScript.CallMethod(Callable.From<long, long, long>((x, y) => x + y), "bindv", new Collections.Array { 1L }).AsCallable().Call(2L).AsInt64() == 3 &&
            GDScript.CallMethod(new NodePath("a/b/c"), "get_name", 1).AsString() == "b" && GDScript.CallMethod(new NodePath("a/b/c"), "slice", 1).AsNodePath().ToString() == "b/c");
        T.Check("an unknown method on any of them is an error", Throws(() => GDScript.CallMethod(box, "nonsense")) && Throws(() => GDScript.CallMethod("s", "nonsense")) && Throws(() => GDScript.CallMethod(arr, "nonsense")));
    }

    private static bool Throws(Action a)
    {
        try { a(); return false; }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException or InvalidCastException) { return true; }
    }

    // ---- the converter ----

    private static void ConvertedScripts()
    {
        ConversionResult r = Convert(
            "extends Node\n\n" +
            "func enums() -> Array:\n\tvar light := OmniLight3D.new()\n\tlight.light_bake_mode = Light3D.BAKE_STATIC\n\tvar mesh := MeshInstance3D.new()\n\tmesh.gi_mode = GeometryInstance3D.GI_MODE_DYNAMIC\n" +
            "\tvar vp := Viewport.new()\n\tvp.screen_space_aa = Viewport.SCREEN_SPACE_AA_FXAA\n\treturn [light.light_bake_mode, mesh.gi_mode, vp.screen_space_aa]\n\n" +
            "func rendering() -> void:\n\tRenderingServer.environment_set_ssao_quality(RenderingServer.ENV_SSAO_QUALITY_HIGH, true, 0.5, 2, 50.0, 300.0)\n" +
            "\tRenderingServer.environment_set_ssil_quality(RenderingServer.ENV_SSIL_QUALITY_ULTRA, false, 0.5, 2, 50.0, 300.0)\n\tRenderingServer.gi_set_use_half_resolution(true)\n" +
            "\tvar env := Environment.new()\n\tenv.set_ssr_max_steps(8)\n\tvar label := Label3D.new()\n\tlabel.font = FontFile.new()\n\tRenderingServer.set_default_clear_color(Color(0.1, 0.2, 0.3))\n\n" +
            "func singletons() -> Array:\n\tEngine.physics_ticks_per_second = 30\n\tEngine.max_fps = 90\n\tvar out := []\n\tout.append(Engine.get_version_info()[\"major\"])\n\tout.append(Engine.get_frames_drawn() >= 0)\n" +
            "\tout.append(Engine.get_physics_interpolation_fraction() >= 0.0)\n\tout.append(OS.get_processor_count() > 0)\n\tout.append(OS.get_static_memory_usage() > 0)\n" +
            "\tout.append(Time.get_datetime_string_from_unix_time(1700000000))\n\tout.append(Time.get_ticks_usec() >= 0)\n\tout.append(Performance.get_monitor(Performance.TIME_FPS) >= 0.0)\n" +
            "\tout.append(OS.get_cmdline_user_args().size())\n\tout.append(ProjectSettings.globalize_path(\"user://x\").length() > 0)\n\treturn out\n\n" +
            "func files(dir: String) -> Array:\n\tvar out := []\n\tvar f := FileAccess.open(dir + \"/note.txt\", FileAccess.WRITE)\n\tf.store_line(\"hello\")\n\tf.close()\n" +
            "\tvar g := FileAccess.open(dir + \"/note.txt\", FileAccess.READ)\n\tout.append(g.get_line())\n\tg.close()\n\tout.append(FileAccess.file_exists(dir + \"/note.txt\"))\n" +
            "\tvar cfg := ConfigFile.new()\n\tcfg.set_value(\"a\", \"b\", Vector2(1, 2))\n\tout.append(cfg.get_value(\"a\", \"b\"))\n" +
            "\tout.append(JSON.parse_string('{\"k\": [1, 2]}')[\"k\"].size())\n\tout.append(JSON.stringify({\"x\": 1}))\n" +
            "\tvar d := DirAccess.open(dir)\n\tout.append(d.get_files().size())\n\treturn out\n\n" +
            "func threads() -> Array:\n\tvar m := Mutex.new()\n\tm.lock()\n\tm.unlock()\n\tvar s := Semaphore.new()\n\ts.post()\n\tvar out := []\n\tout.append(s.try_wait())\n" +
            "\tvar t := Thread.new()\n\tt.start(func(): return 5)\n\tout.append(t.wait_to_finish())\n\treturn out\n\n" +
            "func vectors() -> Array:\n\tvar out := []\n\tout.append(Vector3(1.4, 2.6, 0.0).snappedf(0.5))\n\tout.append(Vector2i(5, -5).clampi(-2, 2))\n\tout.append(Color(1, 1, 1).get_luminance() > 0.9)\n" +
            "\tvar arr := [1, 3, 5]\n\tout.append(arr.pop_at(0))\n\tout.append(arr.bsearch(4))\n\tout.append(arr.find_custom(func(x): return x == 5))\n" +
            "\tout.append({\"a\": 1}.merged({\"b\": 2}).size())\n\treturn out\n\n" +
            "func dynamic(t, b):\n\tt.origin = Vector3(4, 5, 6)\n\tvar o = t.origin\n\tb.size = Vector3(1, 2, 3)\n\treturn [t, o, b.end, b]\n\n" +
            "func compat(path: String, body: RigidBody3D, player: AnimationPlayer) -> void:\n\tvar scene = load(path)\n\tvar node = scene.instantiate()\n\tbody.set_script(load(\"res://tool.gd\"))\n" +
            "\tplayer.playback_process_mode = AnimationPlayer.ANIMATION_PROCESS_PHYSICS\n\tprint(node, body.get_script())\n");
        T.Check("the script compiles against the new members (before: dozens of errors)", r.Success, Errors(r));
        if (!r.Success) return;

        var enums = (Collections.Array)Call(r, "Sample", "Enums")!;
        T.Check("BAKE_STATIC, GI_MODE_DYNAMIC and SCREEN_SPACE_AA_FXAA resolve to the nested enums (1, 2, 1)", enums[0].AsInt64() == 1 && enums[1].AsInt64() == 2 && enums[2].AsInt64() == 1);

        Call(r, "Sample", "Rendering");
        T.Check("ENV_SSAO_QUALITY_HIGH and ENV_SSIL_QUALITY_ULTRA resolve to the quality enums", RenderingServer.SsaoQuality.Quality == 3 && RenderingServer.SsilQuality.Quality == 4 && RenderingServer.GiHalfResolution);
        T.Check("the clear colour call reached", RenderingServer.GetDefaultClearColor() == new Color(0.1f, 0.2f, 0.3f, 1f));
        RenderingServer.SetDefaultClearColor(new Color(0.3f, 0.3f, 0.3f, 1f));

        var singles = (Collections.Array)Call(r, "Sample", "Singletons")!;
        T.Check("Engine.physics_ticks_per_second is assignable and Engine.max_fps too", Aver.Fabric.Engine.PhysicsTicksPerSecond == 30 && Aver.Fabric.Engine.MaxFps == 90);
        Aver.Fabric.Engine.PhysicsTicksPerSecond = 60;
        Aver.Fabric.Engine.MaxFps = 0;
        T.Check("version info, frames, interpolation, OS counts, Time and Performance answer", singles[0].AsInt64() == 4 && singles[1].AsBool() && singles[2].AsBool() && singles[3].AsBool() && singles[4].AsBool() &&
            singles[5].AsString() == "2023-11-14T22:13:20" && singles[6].AsBool() && singles[7].AsBool() && singles[8].AsInt64() == 0 && singles[9].AsBool());

        string dir = TempDir("converted");
        try
        {
            var f = (Collections.Array)Call(r, "Sample", "Files", dir)!;
            T.Check("FileAccess.WRITE and READ, file_exists, ConfigFile, JSON and DirAccess work from converted code", f[0].AsString() == "hello" && f[1].AsBool() && f[2].AsVector2() == new Vector2(1, 2) && f[3].AsInt64() == 2 &&
                f[4].AsString() == "{\"x\":1}" && f[5].AsInt64() == 1, string.Join(" | ", f.Select(x => x.AsString())));
        }
        finally { Directory.Delete(dir, true); }

        var th = (Collections.Array)Call(r, "Sample", "Threads")!;
        T.Check("Mutex, Semaphore and Thread work from converted code", th[0].AsBool() && th[1].AsInt64() == 5);

        var vec = (Collections.Array)Call(r, "Sample", "Vectors")!;
        T.Check("snappedf, clampi, get_luminance, pop_at, bsearch, find_custom, merged", vec[0].AsVector3() == new Vector3(1.5f, 2.5f, 0f) && vec[1].AsVector2I() == new Vector2I(2, -2) && vec[2].AsBool() && vec[3].AsInt64() == 1 &&
            vec[4].AsInt64() == 1 && vec[5].AsInt64() == 1 && vec[6].AsInt64() == 2, string.Join(" | ", vec.Select(x => x.AsString())));

        var dyn = (Collections.Array)Call(r, "Sample", "Dynamic", Variant.From(new Transform3D(Basis.Identity, Vector3.Zero)), Variant.From(new Aabb(Vector3.Zero, Vector3.One)))!;
        T.Check("assigning a member of an untyped value changes the variable (origin of a transform, size of a box)", dyn[0].AsTransform3D().Origin == new Vector3(4, 5, 6) && dyn[1].AsVector3() == new Vector3(4, 5, 6) &&
            dyn[2].AsVector3() == new Vector3(1, 2, 3) && dyn[3].AsAabb().Size == new Vector3(1, 2, 3));
    }
}
