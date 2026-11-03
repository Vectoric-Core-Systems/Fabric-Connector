// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// `Aver.Fabric.Convert scripts`: one Godot project's C# scripts into an Aver project, and the report that goes with them.

using System.Text;
using System.Xml.Linq;

namespace Aver.Fabric.Convert;

public sealed class ProjectOptions
{
    /// <summary>The Godot project folder (holds project.godot).</summary>
    public string ProjectDir { get; set; } = "";
    /// <summary>The Aver project folder the scene converter wrote (holds Fabric/scripts.json and Content/).</summary>
    public string OutDir { get; set; } = "";
    public string? ManifestPath { get; set; }
    /// <summary>The folder with Aver.Fabric.dll, or a list of folders separated by <see cref="Path.PathSeparator"/> (default: <see cref="DefaultRefs"/>).</summary>
    public string? RefsDir { get; set; }
    public string? ReportPath { get; set; }
    /// <summary>The engine checkout: when given, a missing Content/Scripts/Scripts.csproj is written with references into it.</summary>
    public string? EnginePath { get; set; }
}

public sealed class ProjectResult
{
    public int ExitCode { get; set; }
    public ConversionResult? Conversion { get; set; }
    public List<string> Written { get; } = new();
    public List<ReportItem> Items { get; } = new();
    public string ReportPath { get; set; } = "";
}

public static class ProjectConverter
{
    /// <summary>0 converted and compiled, 1 compile errors or an unreadable project, 3 the reference folder is unusable (scripts are still written, uncompiled).</summary>
    public static ProjectResult Run(ProjectOptions o, TextWriter log)
    {
        var res = new ProjectResult();
        string projectDir = Path.GetFullPath(o.ProjectDir);
        string outDir = Path.GetFullPath(o.OutDir);
        if (!Directory.Exists(projectDir)) { log.WriteLine($"error: the Godot project folder {projectDir} does not exist"); res.ExitCode = 1; return res; }

        // The manifest: the one given, else the scene converter's, else a scan of the project.
        List<ManifestScript> entries;
        string? manifest = o.ManifestPath;
        if (manifest != null && !File.Exists(manifest)) manifest = Path.Combine(outDir, "Fabric", manifest);
        manifest ??= Path.Combine(outDir, "Fabric", "scripts.json");
        if (File.Exists(manifest))
        {
            List<ManifestScript>? read = Manifest.Read(manifest, out string? err);
            if (read == null) { log.WriteLine("error: " + err); res.ExitCode = 1; return res; }
            entries = read;
        }
        else if (o.ManifestPath != null) { log.WriteLine($"error: the manifest {o.ManifestPath} was not found"); res.ExitCode = 1; return res; }
        else { log.WriteLine($"note: no {manifest}; scanning {projectDir} for .cs and .gd files"); entries = Manifest.Scan(projectDir); }

        var sources = new List<ScriptSource>();
        var handled = new HashSet<string>(StringComparer.Ordinal);
        // Godot builds a project's C# scripts from its .csproj; a GDScript project that ships an addon's C# wrappers without one never compiles them.
        bool buildsCSharp = !File.Exists(Path.Combine(projectDir, "project.godot")) || Directory.EnumerateFiles(projectDir, "*.csproj", SearchOption.TopDirectoryOnly).Any();
        foreach (ManifestScript e in entries.Where(e => e.Kind is "csharp" or "gdscript"))
        {
            handled.Add(e.Path);
            string type = e.Kind == "gdscript" ? "gdscript" : "csharp";
            string disk = Manifest.DiskPath(projectDir, e.Path);
            if (!e.Exists || !File.Exists(disk))
            {
                res.Items.Add(new ReportItem { File = e.Path, Node = e.ClassName, Type = type, Rating = Rating.Unsupported, Reason = "the script file does not exist in the Godot project" });
                continue;
            }
            if (type == "csharp" && !buildsCSharp)
            {
                res.Items.Add(new ReportItem
                {
                    File = e.Path, Node = e.ClassName, Type = type, Rating = Rating.Approximated,
                    Reason = "left out of the game: the project has no .csproj, so Godot does not build its C# scripts either",
                });
                continue;
            }
            sources.Add(new ScriptSource
            {
                ResPath = e.Path, Text = File.ReadAllText(disk), ClassName = e.ClassName, Kind = e.Kind == "gdscript" ? "gdscript" : "csharp",
                Output = e.Output.Length > 0 ? e.Output : "Content/Scripts/" + (e.ClassName.Length > 0 ? e.ClassName : SyntaxUtil.StemOf(e.Path)) + ".cs",
            });
        }
        res.Items.AddRange(PackageReferences(projectDir, handled));

        string? refs = o.RefsDir ?? DefaultRefs();
        // The scene converter's list of the nodes it placed sits beside the manifest.
        string nodesFile = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(manifest)) ?? outDir, "nodes.json");
        string? sceneNodes = null;
        try { if (File.Exists(nodesFile)) sceneNodes = File.ReadAllText(nodesFile); }
        catch (IOException) { }
        ConversionResult conv = ScriptConverter.Convert(sources, new ConvertSettings { RefsDir = refs, ProjectDir = projectDir, SceneNodes = sceneNodes });
        res.Conversion = conv;

        foreach (ConvertedScript s in conv.Scripts)
        {
            string target = Path.Combine(outDir, s.Output.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            Manifest.Utf8NoBom(target, s.Text);
            res.Written.Add(target);
        }
        RemoveStaleScripts(Path.Combine(outDir, "Content", "Scripts"), res.Written);
        if (sources.Count > 0) WriteScriptsProject(outDir, o.EnginePath, refs, res.Written);

        res.Items.AddRange(conv.Items);
        foreach (string g in conv.GeneralErrors)
            res.Items.Add(new ReportItem { Type = "csharp", Rating = Rating.Unsupported, Reason = g });

        // The report: the scene converter's file, with this run's script entries in place of any earlier ones.
        res.ReportPath = ReportFile.Resolve(outDir, o.ReportPath);
        var files = new HashSet<string>(res.Items.Select(i => i.File).Where(f => f.Length > 0).Concat(handled), StringComparer.Ordinal);
        ReportFile.MergeJsonl(res.ReportPath, files, res.Items);
        ReportFile.MergeMarkdown(res.ReportPath, res.Items);

        foreach (CompileError e in conv.Errors) log.WriteLine(e.ToString());
        foreach (string g in conv.GeneralErrors) log.WriteLine("error: " + g);
        res.ExitCode = conv.GeneralErrors.Count > 0 ? 3 : conv.Errors.Count > 0 ? 1 : 0;
        return res;
    }

    /// <summary>The extension's managed folder beside the tool's own folder (Extensions/FabricConnector/tools), then the engine's bin/Scripting three
    /// levels up (Extensions/FabricConnector/tools to the executable's folder).</summary>
    public static string DefaultRefs() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "managed")) + Path.PathSeparator +
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Scripting"));

    // NuGet packages the Godot project's .csproj asks for: an Aver project restores offline, so they are the author's to add.
    private static IEnumerable<ReportItem> PackageReferences(string projectDir, ISet<string> handled)
    {
        var items = new List<ReportItem>();
        foreach (string proj in Directory.EnumerateFiles(projectDir, "*.csproj", SearchOption.TopDirectoryOnly))
        {
            string res = "res://" + Path.GetFileName(proj);
            try
            {
                foreach (XElement pr in XDocument.Load(proj).Descendants().Where(x => x.Name.LocalName == "PackageReference"))
                {
                    string name = (string?)pr.Attribute("Include") ?? "";
                    if (name.Length == 0 || name.StartsWith("Godot", StringComparison.OrdinalIgnoreCase)) continue;
                    handled.Add(res);
                    items.Add(new ReportItem
                    {
                        File = res, Type = "PackageReference", Property = name, Rating = Rating.Unsupported,
                        Reason = $"NuGet package {name} {(string?)pr.Attribute("Version")} is not carried over: Aver restores offline from third_party/nuget, so add the assembly to Scripts.csproj by hand",
                    });
                }
            }
            catch (Exception ex) when (ex is System.Xml.XmlException or IOException) { }
        }
        return items;
    }

    /// <summary>The full path with every symlink along it resolved (the parts that do not exist yet are kept as given).</summary>
    internal static string RealPath(string path)
    {
        string full = Path.GetFullPath(path);
        string root = Path.GetPathRoot(full) ?? "";
        string cur = root;
        var pending = new Stack<string>();
        PushParts(pending, full[root.Length..]);
        for (int hops = 0; pending.Count > 0;)
        {
            string next = Path.Combine(cur, pending.Pop());
            FileSystemInfo info = Directory.Exists(next) ? new DirectoryInfo(next) : new FileInfo(next);
            string? link = null;
            try { link = info.Exists ? info.LinkTarget : null; } catch (IOException) { }
            if (link == null || ++hops > 40) { cur = next; continue; }
            string target = Path.GetFullPath(link, cur);
            string tRoot = Path.GetPathRoot(target) ?? "";
            cur = tRoot;
            PushParts(pending, target[tRoot.Length..]);
        }
        return cur;
    }

    // The folder names of a relative path onto the stack, the first on top.
    private static void PushParts(Stack<string> stack, string relative)
    {
        string[] parts = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = parts.Length - 1; i >= 0; --i) stack.Push(parts[i]);
    }

    // A script an earlier run wrote and this one did not (one now left out of the game, or removed from the project) would still be compiled with
    // the rest. Only files that carry the converter's own header are removed; anything else in the folder is the user's.
    private static void RemoveStaleScripts(string scriptsDir, List<string> written)
    {
        if (!Directory.Exists(scriptsDir)) return;
        var keep = new HashSet<string>(written.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(scriptsDir, "*.cs", SearchOption.AllDirectories))
        {
            if (keep.Contains(Path.GetFullPath(file))) continue;
            try
            {
                using var reader = new StreamReader(file);
                if (reader.ReadLine()?.StartsWith("// FABRIC: converted from ", StringComparison.Ordinal) != true) continue;
            }
            catch (IOException) { continue; }
            try { File.Delete(file); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    // An installed editor's assemblies, for a project converted with no engine source tree: what Aver.Framework's project references bring in
    // with it as well, since a DLL reference brings none.
    private static readonly string[] s_installedEngineAssemblies =
        { "Aver.Scripting", "Aver.Framework", "Aver.Scene", "Aver.Physics", "Aver.UI", "Aver.Synapse", "Aver.Materials" };

    private static void WriteScriptsProject(string outDir, string? engine, string refs, List<string> written)
    {
        string dir = Path.Combine(outDir, "Content", "Scripts");
        string csproj = Path.Combine(dir, "Scripts.csproj");
        if (File.Exists(csproj)) return;
        Directory.CreateDirectory(dir);
        // The relative paths are made between real locations (symlinks followed), so they hold wherever the build resolves them from: macOS's temp folder
        // (/var is /private/var) is a symlink, and a `..` run counted along the link's path leaves the wrong folder.
        string realDir = RealPath(dir);
        var sb = new StringBuilder();
        sb.Append("<Project Sdk=\"Microsoft.NET.Sdk\">\n\n");
        sb.Append("  <!-- Written by Aver.Fabric.Convert for a converted Godot project. Tools > Compile Scripts builds this file; the converted scripts\n");
        sb.Append("       compile against Aver.Fabric (the Godot-compatible node layer, from the installed Fabric Connector extension) and Aver.Framework. -->\n");
        sb.Append("  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n    <Nullable>enable</Nullable>\n    <ImplicitUsings>enable</ImplicitUsings>\n  </PropertyGroup>\n\n");
        sb.Append("  <ItemGroup>\n");
        if (engine != null)
        {
            engine = RealPath(engine);
            foreach (string name in new[] { "Aver.Scripting", "Aver.Framework", "Aver.UI", "Aver.Materials" })
            {
                string target = Path.Combine(engine, "scripting", "csharp", name, name + ".csproj");
                sb.Append("    <ProjectReference Include=\"").Append(Path.GetRelativePath(realDir, target).Replace('/', '\\')).Append("\" />\n");
            }
        }
        else
        {
            // No engine source tree (an installed editor): its shipped assemblies, bin/Scripting and Aver.Materials in bin/Tools. Private=false: the
            // editor loads its own copies.
            string? framework = References.Find(refs, "Aver.Framework.dll");
            string? scripting = framework != null ? Path.GetDirectoryName(framework) : null;
            foreach (string name in s_installedEngineAssemblies)
            {
                string? dll = References.Find(refs, name + ".dll");
                if (dll == null && scripting != null && File.Exists(Path.Combine(scripting, "..", "Tools", name + ".dll"))) dll = Path.Combine(scripting, "..", "Tools", name + ".dll");
                if (dll == null) { sb.Append("    <!-- ").Append(name).Append(".dll was not found beside the editor. -->\n"); continue; }
                sb.Append("    <Reference Include=\"").Append(name).Append("\">\n      <HintPath>").Append(Path.GetRelativePath(realDir, RealPath(Path.GetFullPath(dll))).Replace('/', '\\'))
                  .Append("</HintPath>\n      <Private>false</Private>\n    </Reference>\n");
            }
        }
        sb.Append("  </ItemGroup>\n\n");
        // Aver.Fabric is the extension's file, not an engine project: a HintPath to the installed assembly, relative to this folder like the
        // engine references. Private=false because the host loads that one assembly (a copy in Binaries/Scripts would be a second Aver.Fabric).
        string? fabric = References.Find(refs, "Aver.Fabric.dll");
        if (fabric != null)
            sb.Append("  <ItemGroup>\n    <Reference Include=\"Aver.Fabric\">\n      <HintPath>").Append(Path.GetRelativePath(realDir, RealPath(fabric)).Replace('/', '\\'))
              .Append("</HintPath>\n      <Private>false</Private>\n    </Reference>\n  </ItemGroup>\n\n");
        else
            sb.Append("  <!-- Aver.Fabric.dll was not found when this project was converted: add a Reference to Extensions\\FabricConnector\\managed\\Aver.Fabric.dll. -->\n\n");
        sb.Append("  <ItemGroup>\n    <Compile Include=\"..\\Materials\\**\\*.cs\" />\n  </ItemGroup>\n\n</Project>\n");
        Manifest.Utf8NoBom(csproj, sb.ToString());
        written.Add(csproj);
    }
}
