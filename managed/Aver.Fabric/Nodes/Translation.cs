// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// The translation API (parity wave 8a, F48): Godot's Translation, TranslationServer and Object.tr / tr_n over the engine's localisation. A Translation is a locale and
// its messages; the server keeps the translations a game adds, picks the one for the current locale (the exact locale, then its language, then the fallback)
// and, for text no added translation has, asks the engine's Loc (docs/LOCALIZATION.md) so a game that uses the engine's string tables keeps working. A `.po` file
// is read by <see cref="PoFile"/>; the project's translation list is not loaded by itself: a script (or the host) adds what it wants.

using System.Globalization;
using System.Text;

namespace Aver.Fabric;

/// <summary>The messages of one locale (Godot's Translation).</summary>
public class Translation : Resource
{
    private readonly Dictionary<(string Context, string Source), string[]> _messages = new();

    /// <summary>The locale, as a code like <c>en</c>, <c>pt_BR</c> or <c>fr-CA</c>.</summary>
    public string Locale { get; set; } = "en";

    public void AddMessage(StringName srcMessage, StringName xlatedMessage, StringName? context = null)
        => _messages[((context?.ToString() ?? ""), srcMessage.ToString())] = new[] { xlatedMessage.ToString() };

    /// <summary>A plural message: <paramref name="xlatedMessages"/> are the forms in order.</summary>
    public void AddPluralMessage(StringName srcMessage, string[] xlatedMessages, StringName? context = null)
        => _messages[((context?.ToString() ?? ""), srcMessage.ToString())] = xlatedMessages.Length == 0 ? new[] { "" } : (string[])xlatedMessages.Clone();

    public StringName GetMessage(StringName srcMessage, StringName? context = null)
        => _messages.TryGetValue(((context?.ToString() ?? ""), srcMessage.ToString()), out string[]? m) ? new StringName(m[0]) : new StringName("");

    /// <summary>The form of a plural message for <paramref name="n"/> (English rule: form 0 for one, form 1 otherwise, or the last form that exists).</summary>
    public StringName GetPluralMessage(StringName srcMessage, StringName srcPluralMessage, int n, StringName? context = null)
    {
        if (!_messages.TryGetValue(((context?.ToString() ?? ""), srcMessage.ToString()), out string[]? m)) return new StringName("");
        int form = PluralForm(Locale, n);
        return new StringName(m[Math.Min(Math.Max(form, 0), m.Length - 1)]);
    }

    public void EraseMessage(StringName srcMessage, StringName? context = null) => _messages.Remove(((context?.ToString() ?? ""), srcMessage.ToString()));

    public string[] GetMessageList() => _messages.Keys.Select(k => k.Source).ToArray();

    public string[] GetTranslatedMessageList() => _messages.Values.Select(v => v[0]).ToArray();

    public int GetMessageCount() => _messages.Count;

    public void Clear() => _messages.Clear();

    internal bool TryGet(string source, string context, out string[] forms)
    {
        if (_messages.TryGetValue((context, source), out string[]? m) && m.Length > 0 && (m[0].Length > 0 || m.Length > 1)) { forms = m; return true; }
        forms = System.Array.Empty<string>();
        return false;
    }

    /// <summary>The plural form index a locale uses for <paramref name="n"/>: the engine's CLDR category when the language is one it knows (one = 0, anything else = 1,
    /// and the zero / two / few / many forms of languages that have them follow in that order), else English's.</summary>
    internal static int PluralForm(string locale, long n)
    {
        string lang = TranslationServer.LanguageOf(locale);
        return lang switch
        {
            "ja" or "zh" or "ko" or "vi" or "th" or "id" => 0,                       // one form
            "fr" or "pt" => n == 0 || n == 1 ? 0 : 1,                                   // zero and one are the singular
            "ru" or "uk" or "be" => n % 10 == 1 && n % 100 != 11 ? 0 : (n % 10 >= 2 && n % 10 <= 4 && (n % 100 < 10 || n % 100 >= 20) ? 1 : 2),
            "pl" => n == 1 ? 0 : (n % 10 >= 2 && n % 10 <= 4 && (n % 100 < 10 || n % 100 >= 20) ? 1 : 2),
            "ar" => n == 0 ? 0 : n == 1 ? 1 : n == 2 ? 2 : n % 100 >= 3 && n % 100 <= 10 ? 3 : n % 100 >= 11 ? 4 : 5,
            _ => n == 1 ? 0 : 1,
        };
    }
}

/// <summary>A message catalog read from a gettext <c>.po</c> text file.</summary>
public static class PoFile
{
    /// <summary>Reads PO text into a translation: msgid / msgstr pairs, msgctxt contexts, msgid_plural with msgstr[n] forms, and the header's Language. Comments and
    /// the empty-id header are skipped; the usual escapes (\n, \t, \", \\) are decoded; adjacent string literals continue the same field.</summary>
    public static Translation Parse(string text, string? locale = null)
    {
        var tr = new Translation();
        string? ctx = null, id = null, plural = null;
        var forms = new SortedDictionary<int, string>();
        string? field = null;   // the field a continuation line belongs to
        int formIndex = 0;
        string header = "";

        void Flush()
        {
            if (id != null)
            {
                string[] arr = forms.Count == 0 ? new[] { "" } : forms.OrderBy(k => k.Key).Select(k => k.Value).ToArray();
                if (id.Length == 0) header = arr[0];
                else if (plural != null) tr.AddPluralMessage(id, arr, ctx ?? "");
                else tr.AddMessage(id, arr[0], ctx ?? "");
            }
            ctx = id = plural = null;
            forms.Clear();
            field = null;
        }

        foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0) { Flush(); continue; }
            if (line[0] == '#') continue;
            if (line[0] == '"')
            {
                string s = Unquote(line);
                switch (field)
                {
                    case "ctx": ctx += s; break;
                    case "id": id += s; break;
                    case "plural": plural += s; break;
                    case "str": forms[formIndex] = (forms.TryGetValue(formIndex, out string? cur) ? cur : "") + s; break;
                }
                continue;
            }
            int sp = line.IndexOf(' ');
            if (sp < 0) continue;
            string key = line[..sp], value = line[(sp + 1)..].Trim();
            if (key == "msgctxt") { if (id != null) Flush(); ctx = Unquote(value); field = "ctx"; }
            else if (key == "msgid") { if (id != null) Flush(); id = Unquote(value); field = "id"; }
            else if (key == "msgid_plural") { plural = Unquote(value); field = "plural"; }
            else if (key == "msgstr") { formIndex = 0; forms[0] = Unquote(value); field = "str"; }
            else if (key.StartsWith("msgstr[", StringComparison.Ordinal) && key.EndsWith(']') && int.TryParse(key.AsSpan(7, key.Length - 8), NumberStyles.None, CultureInfo.InvariantCulture, out int fi) && fi >= 0 && fi < 8)
            { formIndex = fi; forms[fi] = Unquote(value); field = "str"; }
        }
        Flush();

        if (locale != null) tr.Locale = locale;
        else
        {
            foreach (string h in header.Split('\n'))
                if (h.StartsWith("Language:", StringComparison.OrdinalIgnoreCase)) { string l = h[9..].Trim(); if (l.Length > 0) tr.Locale = l; }
        }
        return tr;
    }

    private static string Unquote(string s)
    {
        s = s.Trim();
        if (s.Length < 2 || s[0] != '"' || s[^1] != '"') return s;
        var sb = new StringBuilder(s.Length);
        for (int i = 1; i < s.Length - 1; i++)
        {
            char c = s[i];
            if (c != '\\' || i + 1 >= s.Length - 1) { sb.Append(c); continue; }
            char n = s[++i];
            sb.Append(n switch { 'n' => '\n', 't' => '\t', 'r' => '\r', '"' => '"', '\\' => '\\', _ => n });
        }
        return sb.ToString();
    }
}

/// <summary>Godot's TranslationServer: the locale, the translations added to it, and the lookup.</summary>
public static class TranslationServer
{
    private static readonly List<Translation> s_translations = new();
    private static string s_locale = "en";
    private static string s_fallback = "en";

    /// <summary>When on, a message with no translation is shown as the source text wrapped in pseudolocalisation marks (Godot's pseudolocalization); off by default.</summary>
    public static bool PseudolocalizationEnabled { get; set; }

    /// <summary>Sets the current locale. A code is normalised the way Godot does (<c>fr-CA</c> and <c>fr_CA</c> are the same), and the engine's culture follows when it has that
    /// culture, so engine string tables and added translations switch together.</summary>
    public static void SetLocale(string locale)
    {
        s_locale = StandardizeLocale(locale);
        try { if (Aver.Framework.Loc.Cultures.Contains(s_locale.Replace('_', '-')) || Aver.Framework.Loc.Cultures.Contains(LanguageOf(s_locale))) Aver.Framework.Loc.SetCulture(Aver.Framework.Loc.Cultures.Contains(s_locale.Replace('_', '-')) ? s_locale.Replace('_', '-') : LanguageOf(s_locale), false); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { /* no engine: the added translations still switch */ }
    }

    public static string GetLocale() => s_locale;

    public static void SetFallbackLocale(string locale) => s_fallback = StandardizeLocale(locale);

    public static string GetFallbackLocale() => s_fallback;

    /// <summary>The locale as Godot spells it: language lower case, an underscore, the script or region as written (<c>pt_BR</c>). An empty or unusable code is "en".</summary>
    public static string StandardizeLocale(string locale)
    {
        string s = (locale ?? "").Trim().Replace('-', '_');
        int dot = s.IndexOf('.');
        if (dot >= 0) s = s[..dot];
        int at = s.IndexOf('@');
        if (at >= 0) s = s[..at];
        if (s.Length == 0) return "en";
        string[] parts = s.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "en";
        parts[0] = parts[0].ToLowerInvariant();
        for (int i = 1; i < parts.Length; i++) parts[i] = parts[i].Length == 4 ? char.ToUpperInvariant(parts[i][0]) + parts[i][1..].ToLowerInvariant() : parts[i].ToUpperInvariant();
        return string.Join('_', parts);
    }

    internal static string LanguageOf(string locale)
    {
        string s = StandardizeLocale(locale);
        int us = s.IndexOf('_');
        return us < 0 ? s : s[..us];
    }

    public static void AddTranslation(Translation translation)
    {
        if (translation != null && !s_translations.Contains(translation)) s_translations.Add(translation);
    }

    public static void RemoveTranslation(Translation translation) => s_translations.Remove(translation);

    public static void Clear() => s_translations.Clear();

    public static string[] GetLoadedLocales() => s_translations.Select(t => StandardizeLocale(t.Locale)).Distinct().ToArray();

    public static Translation? GetTranslationObject(string locale)
    {
        string want = StandardizeLocale(locale);
        return s_translations.FirstOrDefault(t => StandardizeLocale(t.Locale) == want) ?? s_translations.FirstOrDefault(t => LanguageOf(t.Locale) == LanguageOf(want));
    }

    /// <summary>How well two locales match: 2 for the same code, 1 for the same language, 0 otherwise (Godot's compare_locales returns a similar score).</summary>
    public static int CompareLocales(string localeA, string localeB)
    {
        string a = StandardizeLocale(localeA), b = StandardizeLocale(localeB);
        return a == b ? 2 : LanguageOf(a) == LanguageOf(b) ? 1 : 0;
    }

    public static string GetLocaleName(string locale) => StandardizeLocale(locale);

    // The translations to try for the current locale, best first: exact, same language, then the fallback's.
    private static IEnumerable<Translation> Candidates()
    {
        foreach (Translation t in s_translations) if (StandardizeLocale(t.Locale) == s_locale) yield return t;
        foreach (Translation t in s_translations) if (StandardizeLocale(t.Locale) != s_locale && LanguageOf(t.Locale) == LanguageOf(s_locale)) yield return t;
        foreach (Translation t in s_translations) if (LanguageOf(t.Locale) != LanguageOf(s_locale) && StandardizeLocale(t.Locale) == s_fallback) yield return t;
    }

    public static StringName Translate(StringName message, StringName? context = null)
    {
        string src = message.ToString(), ctx = (context?.ToString() ?? "");
        foreach (Translation t in Candidates())
            if (t.TryGet(src, ctx, out string[] forms)) return new StringName(forms[0]);
        return new StringName(EngineText(src, ctx) ?? (PseudolocalizationEnabled ? Pseudo(src) : src));
    }

    public static StringName TranslatePlural(StringName message, StringName pluralMessage, int n, StringName? context = null)
    {
        string src = message.ToString(), ctx = (context?.ToString() ?? "");
        foreach (Translation t in Candidates())
            if (t.TryGet(src, ctx, out string[] forms)) return new StringName(forms[Math.Min(Math.Max(Translation.PluralForm(t.Locale, n), 0), forms.Length - 1)]);
        string pl = pluralMessage.ToString();
        try { return new StringName(Aver.Framework.Loc.TrN(src, pl, n, ctx)); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { return new StringName(n == 1 ? src : pl); }
    }

    // The engine's own string tables, asked last: the same text back means "no translation", and the caller keeps the source.
    private static string? EngineText(string src, string ctx)
    {
        try
        {
            string got = Aver.Framework.Loc.Tr(src, ctx);
            return got.Length > 0 && got != src ? got : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { return null; }
    }

    private static string Pseudo(string s) => "[" + s + "]";

    /// <summary>Forgets everything (a test that wants a clean server).</summary>
    internal static void Reset()
    {
        s_translations.Clear();
        s_locale = "en";
        s_fallback = "en";
        PseudolocalizationEnabled = false;
    }
}

public abstract partial class GodotObject
{
    /// <summary>Translates a message through the TranslationServer (Godot's Object.tr).</summary>
    public StringName Tr(StringName message, StringName? context = null) => TranslationServer.Translate(message, context);

    /// <summary>Translates a plural message (Godot's Object.tr_n).</summary>
    public StringName TrN(StringName message, StringName pluralMessage, int n, StringName? context = null) => TranslationServer.TranslatePlural(message, pluralMessage, n, context);
}
