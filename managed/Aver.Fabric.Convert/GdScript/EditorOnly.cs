namespace Aver.Fabric.Convert.GdScript;

// The scripts an exported Godot game cannot load: the ones that name a class of Godot's editor (EditorPlugin, EditorInterface, ...), which exists only
// in the editor, and the ones that name, extend or load such a script. Godot compiles a script when it is first loaded, so an addon's editor plugin
// costs a game nothing; C# compiles every script together, so they are left out of the game instead of failing it.
public static class EditorOnly
{
    // The editor's classes whose names do not start with "Editor".
    private static readonly HashSet<string> s_unprefixed = new(StringComparer.Ordinal)
    {
        "ScriptEditor", "ScriptEditorBase", "FileSystemDock", "ScriptCreateDialog", "GDScriptSyntaxHighlighter",
    };

    private static bool IsEditorClass(string name) =>
        s_unprefixed.Contains(name) || (name.Length > 6 && name.StartsWith("Editor", StringComparison.Ordinal) && char.IsUpper(name[6]));

    private sealed class Scan
    {
        public string? ClassName;
        public readonly HashSet<string> Idents = new(StringComparer.Ordinal);
        public readonly HashSet<string> Strings = new(StringComparer.Ordinal);
    }

    /// <summary>The scripts to leave out, by res:// path, each with the name that makes it editor-only (an editor class or another such script).</summary>
    public static Dictionary<string, string> Find(IReadOnlyList<GdSource> sources)
    {
        var scans = new Dictionary<string, Scan>(StringComparer.Ordinal);
        var declared = new HashSet<string>(StringComparer.Ordinal);   // every name the project declares: its classes are not Godot's
        foreach (GdSource s in sources)
        {
            var scan = new Scan();
            List<Token> toks = Lexer.Tokenize(s.Text).Tokens;
            for (int i = 0; i < toks.Count; i++)
            {
                Token t = toks[i];
                if (t.Kind == TokKind.Ident) scan.Idents.Add(t.Text);
                else if (t.Kind == TokKind.String && t.Value is string v) scan.Strings.Add(v);
                else if (t.Kind == TokKind.Keyword && t.Text is "class_name" or "class" or "const" or "var" or "enum" or "func" or "signal" &&
                         i + 1 < toks.Count && toks[i + 1].Kind == TokKind.Ident)
                {
                    declared.Add(toks[i + 1].Text);
                    if (t.Text == "class_name") scan.ClassName = toks[i + 1].Text;
                }
            }
            scans[s.ResPath] = scan;
        }

        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string path, Scan scan) in scans)
            if (scan.Idents.FirstOrDefault(n => IsEditorClass(n) && !declared.Contains(n)) is { } editorClass) found[path] = editorClass;

        // What names, extends or loads a left-out script cannot load either.
        for (bool grew = found.Count > 0; grew;)
        {
            grew = false;
            foreach ((string path, Scan scan) in scans)
            {
                if (found.ContainsKey(path)) continue;
                foreach (string other in found.Keys)
                {
                    string? cls = scans[other].ClassName;
                    if ((cls != null && scan.Idents.Contains(cls)) || scan.Strings.Contains(other))
                    {
                        found[path] = cls ?? other;
                        grew = true;
                        break;
                    }
                }
            }
        }
        return found;
    }
}
