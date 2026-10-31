// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Godot's ConfigFile: sections of key=value lines, each value in Godot's text form (VariantText), in the .ini-like layout Godot reads and writes. Encrypted files are
// not supported.

using System.Text;

namespace Aver.Fabric;

/// <summary>A settings file of sections and keys (Godot's ConfigFile).</summary>
public class ConfigFile : RefCounted
{
    // Sections and keys keep the order they were added in, which is the order the file lists them in.
    private readonly List<string> _sections = new();
    private readonly Dictionary<string, List<string>> _keys = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Section, string Key), Variant> _values = new();

    public void SetValue(string section, string key, Variant value)
    {
        if (value.IsNil)
        {
            EraseSectionKey(section, key);
            return;
        }
        if (!_keys.TryGetValue(section, out List<string>? keys))
        {
            _sections.Add(section);
            _keys[section] = keys = new List<string>();
        }
        if (!keys.Contains(key)) keys.Add(key);
        _values[(section, key)] = value;
    }

    public Variant GetValue(string section, string key, Variant @default = default)
    {
        if (_values.TryGetValue((section, key), out Variant v)) return v;
        if (@default.IsNil) Aver.Scripting.Log.Error($"[Fabric] ConfigFile.GetValue: no value for \"{key}\" in section \"{section}\" and no default was given");
        return @default;
    }

    public bool HasSection(string section) => _keys.ContainsKey(section);

    public bool HasSectionKey(string section, string key) => _values.ContainsKey((section, key));

    public string[] GetSections() => _sections.ToArray();

    public string[] GetSectionKeys(string section) => _keys.TryGetValue(section, out List<string>? keys) ? keys.ToArray() : System.Array.Empty<string>();

    public void EraseSection(string section)
    {
        if (!_keys.TryGetValue(section, out List<string>? keys)) return;
        foreach (string k in keys) _values.Remove((section, k));
        _keys.Remove(section);
        _sections.Remove(section);
    }

    public void EraseSectionKey(string section, string key)
    {
        if (!_keys.TryGetValue(section, out List<string>? keys) || !keys.Remove(key)) return;
        _values.Remove((section, key));
        if (keys.Count == 0)
        {
            _keys.Remove(section);
            _sections.Remove(section);
        }
    }

    public void Clear()
    {
        _sections.Clear();
        _keys.Clear();
        _values.Clear();
    }

    // ---- text ----

    /// <summary>The file's text: values with no section first, then each section as "[name]" and its "key=value" lines, a blank line after each.</summary>
    public string EncodeToText()
    {
        var sb = new StringBuilder();
        bool first = true;
        foreach (string section in _sections.OrderBy(s => s.Length == 0 ? 0 : 1))
        {
            if (section.Length > 0)
            {
                if (!first) sb.Append('\n');
                sb.Append('[').Append(section).Append("]\n\n");
            }
            foreach (string key in _keys[section])
                sb.Append(key).Append('=').Append(VariantText.Write(_values[(section, key)])).Append('\n');
            first = false;
        }
        return sb.ToString();
    }

    /// <summary>Reads settings from text, adding to what is held. <see cref="Error.ParseError"/> for a line that is neither a section, a key=value nor a comment (";" starts one).</summary>
    public Error Parse(string data)
    {
        string[] lines = data.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        string section = "";
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
            if (line[0] == '[' && line[^1] == ']')
            {
                section = line[1..^1];
                continue;
            }
            int eq = EqualsAt(line);
            if (eq <= 0) return Error.ParseError;
            string key = line[..eq].Trim();
            if (key.Length >= 2 && key[0] == '"' && key[^1] == '"') key = key[1..^1];
            string text = line[(eq + 1)..].Trim();
            // A value may go over several lines (a dictionary, a long array): add lines until the text reads.
            Variant value;
            while (!VariantText.TryParse(text, out value))
            {
                if (++i >= lines.Length) return Error.ParseError;
                text += "\n" + lines[i];
            }
            SetValue(section, key, value);
        }
        return Error.Ok;
    }

    // The "=" that ends the key: the first one outside quotes.
    private static int EqualsAt(string line)
    {
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') quoted = !quoted;
            else if (line[i] == '=' && !quoted) return i;
        }
        return -1;
    }

    /// <summary>Reads a file. <see cref="Error.FileNotFound"/> if there is none, <see cref="Error.ParseError"/> if it does not read (what was read before the bad line is kept).</summary>
    public Error Load(string path)
    {
        string real = FabricPaths.Globalize(path);
        string text;
        try { text = File.ReadAllText(real); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return Error.FileNotFound; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return Error.FileCantOpen; }
        Clear();
        return Parse(text);
    }

    /// <summary>Writes the settings to a file, replacing it.</summary>
    public Error Save(string path)
    {
        try { File.WriteAllText(FabricPaths.Globalize(path), EncodeToText(), new UTF8Encoding(false)); return Error.Ok; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return Error.FileCantWrite; }
    }

    /// <summary>Not supported: Godot's encrypted file format is not read here.</summary>
    public Error LoadEncrypted(string path, byte[] key) => Error.Unavailable;

    /// <summary>Not supported (see <see cref="LoadEncrypted"/>).</summary>
    public Error LoadEncryptedPass(string path, string password) => Error.Unavailable;

    /// <summary>Not supported: Godot's encrypted file format is not written here.</summary>
    public Error SaveEncrypted(string path, byte[] key) => Error.Unavailable;

    /// <summary>Not supported (see <see cref="SaveEncrypted"/>).</summary>
    public Error SaveEncryptedPass(string path, string password) => Error.Unavailable;
}
