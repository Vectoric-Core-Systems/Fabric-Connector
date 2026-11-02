// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// The Aver.Fabric API as the converter sees it: Roslyn symbols read from the reference folder's assemblies, so type inference
// follows whatever runtime is staged (no table to keep in step) and a missing class or member shows up as a compile error.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Aver.Fabric.Convert.GdScript;

internal enum MemberKind { Property, Field, Method, Signal, Constant, EnumValue, NestedType }

/// <summary>One thing a GDScript name resolved to in the API.</summary>
internal sealed class ApiMember
{
    public MemberKind Kind;
    public string CsName = "";
    public GdType Type = GdType.Variant;
    public List<IMethodSymbol> Methods = new();
    public bool CanWrite;
    public bool IsStatic;
    public INamedTypeSymbol? Owner;
    public ISymbol? Symbol;
}

internal sealed class ApiModel
{
    private readonly CSharpCompilation _comp;
    private readonly Dictionary<string, INamedTypeSymbol?> _types = new();

    private ApiModel(CSharpCompilation comp) => _comp = comp;

    public static ApiModel Create(IEnumerable<MetadataReference> refs, IReadOnlyList<(string Path, string Text)>? extra = null)
    {
        var trees = (extra ?? Array.Empty<(string, string)>()).Select(e => CSharpSyntaxTree.ParseText(e.Text, path: e.Path)).ToList();
        return new ApiModel(CSharpCompilation.Create("FabricApi", trees, refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)));
    }

    // ---- types ----

    /// <summary>Aver.Fabric.&lt;name&gt; (nested names with a dot), else a System type used by name.</summary>
    public INamedTypeSymbol? Fabric(string name)
    {
        if (_types.TryGetValue(name, out INamedTypeSymbol? hit)) return hit;
        // GDScript spells the integer vectors and rectangle Vector2i / Vector3i / Vector4i / Rect2i; GodotSharp (and so Aver.Fabric) spells them with a capital I.
        string lookup = name is "Vector2i" or "Vector3i" or "Vector4i" or "Rect2i" ? name[..^1] + "I" : name;
        INamedTypeSymbol? t = _comp.GetTypeByMetadataName("Aver.Fabric." + lookup.Replace('.', '+')) ?? CasedFabric(lookup);
        _types[name] = t;
        return t;
    }

    private Dictionary<string, INamedTypeSymbol>? _byFoldedName;

    /// <summary>Every public top-level type of Aver.Fabric by name, case-blind.</summary>
    private Dictionary<string, INamedTypeSymbol> TypeIndex()
    {
        if (_byFoldedName != null) return _byFoldedName;
        _byFoldedName = new Dictionary<string, INamedTypeSymbol>(StringComparer.OrdinalIgnoreCase);
        INamespaceSymbol? ns = _comp.GlobalNamespace.GetNamespaceMembers().FirstOrDefault(n => n.Name == "Aver")?.GetNamespaceMembers().FirstOrDefault(n => n.Name == "Fabric");
        if (ns != null)
            foreach (INamedTypeSymbol t in ns.GetTypeMembers())
                if (t.DeclaredAccessibility == Accessibility.Public) _byFoldedName.TryAdd(t.Name, t);
        return _byFoldedName;
    }

    private readonly Dictionary<(INamedTypeSymbol, string), INamedTypeSymbol?> _subclassMembers = new(new BaseNameComparer());

    private sealed class BaseNameComparer : IEqualityComparer<(INamedTypeSymbol, string)>
    {
        public bool Equals((INamedTypeSymbol, string) a, (INamedTypeSymbol, string) b) => SymbolEqualityComparer.Default.Equals(a.Item1, b.Item1) && a.Item2 == b.Item2;
        public int GetHashCode((INamedTypeSymbol, string) k) => SymbolEqualityComparer.Default.GetHashCode(k.Item1) * 31 + k.Item2.GetHashCode();
    }

    /// <summary>GDScript reaches a member through a variable typed as a base class (a Material that is a ShaderMaterial, a Window typed Viewport) and C# does not.
    /// The class below <paramref name="baseType"/> that first has the member, when exactly one does; the caller casts to it.</summary>
    public INamedTypeSymbol? SubclassWithMember(INamedTypeSymbol baseType, string gd)
    {
        if (_subclassMembers.TryGetValue((baseType, gd), out INamedTypeSymbol? hit)) return hit;
        var found = new List<INamedTypeSymbol>();
        foreach (INamedTypeSymbol t in TypeIndex().Values)
        {
            if (SymbolEqualityComparer.Default.Equals(t, baseType) || t.TypeKind != TypeKind.Class || !IsSubclassOf(t, baseType)) continue;
            if (HasMemberOrAccessor(t, gd)) found.Add(t);
        }
        // The least derived: a class whose own base does not have the member too.
        var first = found.Where(t => t.BaseType == null || !found.Any(f => SymbolEqualityComparer.Default.Equals(f, t.BaseType))).ToList();
        INamedTypeSymbol? result = first.Count == 1 ? first[0] : null;
        _subclassMembers[(baseType, gd)] = result;
        return result;
    }

    /// <summary>True when some class below <paramref name="baseType"/> has the member (or its get_ / set_ accessor): GDScript finds it on the object when the code runs,
    /// even where no single class can be named to cast to (RigidBody3D, PhysicalBone3D and SoftBody3D all have apply_impulse, none is a Node3D).</summary>
    public bool AnySubclassWithMember(INamedTypeSymbol baseType, string gd)
    {
        foreach (INamedTypeSymbol t in TypeIndex().Values)
            if (!SymbolEqualityComparer.Default.Equals(t, baseType) && t.TypeKind == TypeKind.Class && IsSubclassOf(t, baseType) && HasMemberOrAccessor(t, gd)) return true;
        return false;
    }

    private static bool IsSubclassOf(INamedTypeSymbol t, INamedTypeSymbol baseType)
    {
        for (INamedTypeSymbol? b = t.BaseType; b != null; b = b.BaseType) if (SymbolEqualityComparer.Default.Equals(b, baseType)) return true;
        return false;
    }

    private bool HasMemberOrAccessor(INamedTypeSymbol t, string gd)
    {
        if (Lookup(t, gd) is { Kind: not MemberKind.NestedType }) return true;
        foreach (string pre in new[] { "get_", "set_", "is_", "has_" })
            if (gd.StartsWith(pre, StringComparison.Ordinal) && gd.Length > pre.Length && Lookup(t, gd[pre.Length..]) is { Kind: MemberKind.Property or MemberKind.Field }) return true;
        return false;
    }

    /// <summary>A Godot class spelled with an acronym (AABB, AudioStreamWAV, MultiplayerAPI) is Aabb, AudioStreamWav, MultiplayerApi in GodotSharp and so here:
    /// the one type that differs from the name in letter case only. A nested enum (Viewport.MSAA is Viewport.Msaa) may also carry GodotSharp's Enum suffix,
    /// which it takes when the enum's name is a member name of its class (Viewport.DebugDraw is DebugDrawEnum). Lower-case names never match.</summary>
    private INamedTypeSymbol? CasedFabric(string dotted)
    {
        string[] parts = dotted.Split('.');
        if (parts.Any(p => p.Length < 2 || !char.IsUpper(p[0]))) return null;
        if (!TypeIndex().TryGetValue(parts[0], out INamedTypeSymbol? cur)) return null;
        for (int i = 1; i < parts.Length; i++)
        {
            INamedTypeSymbol? next = null;
            for (INamedTypeSymbol? c = cur; c != null && next == null; c = c.BaseType)
                next = c.GetTypeMembers().FirstOrDefault(n => string.Equals(n.Name, parts[i], StringComparison.OrdinalIgnoreCase) || string.Equals(n.Name, parts[i] + "Enum", StringComparison.OrdinalIgnoreCase));
            if (next == null) return null;
            cur = next;
        }
        return cur;
    }

    public INamedTypeSymbol? Collections(string name) => _comp.GetTypeByMetadataName("Aver.Fabric.Collections." + name);
    public INamedTypeSymbol? Meta(string metadataName) => _comp.GetTypeByMetadataName(metadataName);

    private INamedTypeSymbol? _godotObject;
    public INamedTypeSymbol? GodotObject => _godotObject ??= Fabric("GodotObject");

    /// <summary>Maps a C# type of the API to the converter's type.</summary>
    public GdType FromSymbol(ITypeSymbol? t)
    {
        if (t == null) return GdType.Variant;
        switch (t.SpecialType)
        {
            case SpecialType.System_Void: return GdType.Void;
            case SpecialType.System_Boolean: return GdType.Bool;
            case SpecialType.System_SByte: case SpecialType.System_Byte: case SpecialType.System_Int16: case SpecialType.System_UInt16:
            case SpecialType.System_Int32: return GdType.Int32;
            case SpecialType.System_UInt32: case SpecialType.System_Int64: case SpecialType.System_UInt64: return GdType.Int;
            case SpecialType.System_Single: return GdType.Float32;
            case SpecialType.System_Double: return GdType.Float;
            case SpecialType.System_String: return GdType.String;
            case SpecialType.System_Object: return GdType.Variant;
        }
        if (t is IArrayTypeSymbol arr) return GdType.PackedOf(FromSymbol(arr.ElementType), t.ToDisplayString(s_cs));
        if (t is not INamedTypeSymbol n) return GdType.Variant;
        if (n.TypeKind == TypeKind.Enum) return GdType.Obj(n);
        string full = n.OriginalDefinition.ToDisplayString();
        switch (full)
        {
            case "Aver.Fabric.Variant": return GdType.Variant;
            case "Aver.Fabric.StringName": return GdType.StringName;
            case "Aver.Fabric.NodePath": return GdType.NodePath;
            case "Aver.Fabric.Callable": return GdType.Callable;
            case "Aver.Fabric.Signal": return GdType.Signal;
            case "Aver.Fabric.Collections.Array": return GdType.UntypedArray;
            case "Aver.Fabric.Collections.Array<T>": return GdType.ArrayOf(FromSymbol(n.TypeArguments[0]));
            case "Aver.Fabric.Collections.Dictionary": return GdType.UntypedDict;
            case "Aver.Fabric.Collections.Dictionary<TKey, TValue>": return GdType.DictOf(FromSymbol(n.TypeArguments[0]), FromSymbol(n.TypeArguments[1]));
        }
        if (n.IsGenericType && n.TypeArguments.Any(a => a.TypeKind == TypeKind.TypeParameter)) return GdType.Variant;
        if (n.TypeKind is TypeKind.Class or TypeKind.Struct or TypeKind.Interface) return GdType.Obj(n);
        return GdType.Variant;
    }

    private static readonly SymbolDisplayFormat s_cs = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    public static bool IsGodotDerived(INamedTypeSymbol? t)
    {
        for (; t != null; t = t.BaseType) if (t.Name == "GodotObject" && t.ContainingNamespace?.ToDisplayString() == "Aver.Fabric") return true;
        return false;
    }

    // ---- members ----

    private static IEnumerable<INamedTypeSymbol> Chain(INamedTypeSymbol? t)
    {
        for (; t != null; t = t.BaseType) yield return t;
    }

    private static bool Visible(ISymbol s) => s.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal;

    private static string Fold(string s) => s.Replace("_", "").ToLowerInvariant();

    /// <summary>The candidate C# names for a GDScript identifier: Godot's snake_case turned Pascal, then a case- and underscore-blind match.</summary>
    private static IEnumerable<string> Candidates(string gd)
    {
        yield return Naming.Pascal(gd);
        if (gd.StartsWith('_')) yield return "_" + Naming.Pascal(gd);
    }

    // Pascal(snake_case) first; a case- and underscore-blind match only between names that are both private-looking or both not
    // (so the identifier Input is the class, not the virtual _Input).
    private static bool NameMatches(ISymbol s, string gd, string pascal) =>
        s.Name == pascal || (s.Name.Length == pascal.Length && string.Equals(s.Name, pascal, StringComparison.OrdinalIgnoreCase))
        || (s.Name.StartsWith('_') == gd.StartsWith('_') && Fold(s.Name) == Fold(gd));

    /// <summary>Looks a GDScript member name up on a type: property, field, method group, signal, constant. Null when absent.</summary>
    public ApiMember? Lookup(INamedTypeSymbol? type, string gd, bool wantStatic = false)
    {
        if (type == null) return null;
        string pascal = Naming.Pascal(gd);
        var methods = new List<IMethodSymbol>();
        foreach (INamedTypeSymbol t in Chain(type))
        {
            foreach (ISymbol m in t.GetMembers())
            {
                if (!Visible(m) || !NameMatches(m, gd, pascal)) continue;
                switch (m)
                {
                    case IPropertySymbol p when !p.IsIndexer:
                        return new ApiMember
                        {
                            Kind = MemberKind.Property, CsName = p.Name, Type = FromSymbol(p.Type), CanWrite = p.SetMethod != null && Visible(p.SetMethod),
                            IsStatic = p.IsStatic, Owner = t, Symbol = p,
                        };
                    case IFieldSymbol f when f.IsConst:
                        return new ApiMember { Kind = MemberKind.Constant, CsName = f.Name, Type = FromSymbol(f.Type), IsStatic = true, Owner = t, Symbol = f };
                    case IFieldSymbol f:
                        return new ApiMember { Kind = MemberKind.Field, CsName = f.Name, Type = FromSymbol(f.Type), CanWrite = !f.IsReadOnly, IsStatic = f.IsStatic, Owner = t, Symbol = f };
                    case IMethodSymbol ms when ms.MethodKind == MethodKind.Ordinary:
                        if (!methods.Any(x => x.Name == ms.Name && SameSignature(x, ms))) methods.Add(ms);
                        break;
                    case INamedTypeSymbol nested when nested.TypeKind is TypeKind.Enum or TypeKind.Class or TypeKind.Struct:
                        if (methods.Count == 0) return new ApiMember { Kind = MemberKind.NestedType, CsName = nested.Name, Owner = t, Symbol = nested, Type = GdType.Obj(nested), IsStatic = true };
                        break;
                }
            }
        }
        if (methods.Count > 0)
            return new ApiMember { Kind = MemberKind.Method, CsName = methods[0].Name, Methods = methods, IsStatic = methods.All(x => x.IsStatic), Owner = methods[0].ContainingType, Symbol = methods[0] };
        return null;
    }

    /// <summary>The element type of the type's one-int indexer (Vector3[i] is a float, Basis[i] a Vector3), or null when it has none.</summary>
    public GdType? IndexerType(INamedTypeSymbol? type)
    {
        foreach (INamedTypeSymbol t in Chain(type))
            foreach (IPropertySymbol p in t.GetMembers().OfType<IPropertySymbol>())
                if (p.IsIndexer && Visible(p) && p.Parameters.Length == 1 && p.Parameters[0].Type.SpecialType == SpecialType.System_Int32)
                    return FromSymbol(p.Type);
        return null;
    }

    private static bool SameSignature(IMethodSymbol a, IMethodSymbol b)
    {
        if (a.Parameters.Length != b.Parameters.Length || a.TypeParameters.Length != b.TypeParameters.Length) return false;
        for (int i = 0; i < a.Parameters.Length; i++)
            if (!SymbolEqualityComparer.Default.Equals(a.Parameters[i].Type, b.Parameters[i].Type)) return false;
        return true;
    }

    /// <summary>A signal of the type (or a base): the nested SignalName class declares a field for it.</summary>
    public bool IsSignal(INamedTypeSymbol? type, string gd, out string csName)
    {
        csName = Naming.Pascal(gd);
        foreach (INamedTypeSymbol t in Chain(type))
            foreach (INamedTypeSymbol n in t.GetTypeMembers("SignalName"))
                foreach (IFieldSymbol f in n.GetMembers().OfType<IFieldSymbol>())
                    if (f.Name == csName || Fold(f.Name) == Fold(gd)) { csName = f.Name; return true; }
        return false;
    }

    /// <summary>A constant written SCREAMING_SNAKE in GDScript (Input.MOUSE_MODE_CAPTURED, KEY_SPACE) found in a nested enum of the type
    /// or its bases, or among the global enums: the enum and its member.</summary>
    public bool FindEnumConstant(INamedTypeSymbol? type, string gd, out INamedTypeSymbol? enumType, out string member)
    {
        enumType = null;
        member = "";
        foreach (INamedTypeSymbol t in Chain(type))
            foreach (INamedTypeSymbol e in t.GetTypeMembers().Where(x => x.TypeKind == TypeKind.Enum))
                if (MatchEnum(e, gd, out member, wholeName: true)) { enumType = e; return true; }
        return false;
    }

    private static readonly string[] s_globalEnums = { "Key", "MouseButton", "JoyButton", "JoyAxis", "Error", "Side", "Corner", "Orientation", "ClockDirection", "HorizontalAlignment", "VerticalAlignment", "KeyModifierMask", "MouseButtonMask", "PropertyHint" };

    public bool FindGlobalEnumConstant(string gd, out INamedTypeSymbol? enumType, out string member)
    {
        enumType = null;
        member = "";
        if (gd == "OK") return Fixed("Error", "Ok", out enumType, out member);
        if (gd == "FAILED") return Fixed("Error", "Failed", out enumType, out member);
        foreach (string name in s_globalEnums)
        {
            INamedTypeSymbol? e = Fabric(name);
            if (e != null && e.TypeKind == TypeKind.Enum && MatchEnum(e, gd, out member)) { enumType = e; return true; }
        }
        if (gd.StartsWith("TYPE_", StringComparison.Ordinal) && Fabric("Variant.Type") is { } vt && MatchEnum(vt, gd, out member)) { enumType = vt; return true; }
        return false;
    }

    private bool Fixed(string enumName, string member, out INamedTypeSymbol? enumType, out string m)
    {
        enumType = Fabric(enumName);
        m = member;
        return enumType != null;
    }

    /// <summary>A GDScript constant (BAKE_DYNAMIC) against one enum. GodotSharp drops the prefix the constants share and keeps the rest, so the member is
    /// either the whole constant (Msaa4X, Key1) or its tail after a prefix that begins the enum's name: Dynamic after BAKE (Light3D.BakeMode), Lines after
    /// PRIMITIVE (Mesh.PrimitiveType), Physics after ANIMATION_PROCESS (AnimationProcessCallback), Sine after TRANS (TransitionType). The longest member wins.</summary>
    private static bool MatchEnum(INamedTypeSymbol e, string gd, out string member, bool wholeName = false)
    {
        member = "";
        string want = Fold(gd);
        string enumName = Fold(e.Name.EndsWith("Enum", StringComparison.Ordinal) ? e.Name[..^4] : e.Name);
        IFieldSymbol? best = null;
        foreach (IFieldSymbol f in e.GetMembers().OfType<IFieldSymbol>())
        {
            if (!f.HasConstantValue) continue;
            string fm = Fold(f.Name);
            // The whole constant, when its constants share no prefix GodotSharp could strip: Performance.TIME_FPS is Monitor.TimeFps, FileAccess.READ is ModeFlags.Read.
            // Only inside a class's own enums: among the global ones TYPE_STRING would meet PropertyHint.TypeString.
            if (wholeName && fm == want && fm.Length >= 4) { member = f.Name; return true; }
            if (fm == want && fm.StartsWith(enumName, StringComparison.Ordinal)) { member = f.Name; return true; }
            if (fm.Length == 0 || !want.EndsWith(fm, StringComparison.Ordinal)) continue;
            string prefix = want[..^fm.Length];
            // RenderingServer's ENV_SSAO_QUALITY_* are EnvironmentSsaoQuality: the prefix may abbreviate the enum's "environment".
            if (prefix.StartsWith("env", StringComparison.Ordinal) && !prefix.StartsWith("environment", StringComparison.Ordinal) && enumName.StartsWith("environment", StringComparison.Ordinal))
                prefix = "environment" + prefix[3..];
            if (prefix.Length < 2 || !enumName.StartsWith(prefix, StringComparison.Ordinal) && !prefix.StartsWith(enumName, StringComparison.Ordinal) &&
                !PrefixNamesEnum(prefix, enumName)) continue;
            if (best == null || fm.Length > Fold(best.Name).Length) best = f;
        }
        if (best == null) return false;
        member = best.Name;
        return true;
    }

    /// <summary>The member of one enum a GDScript constant names (MOUSE_MODE_VISIBLE is MouseModeEnum.Visible).</summary>
    public static bool EnumMember(INamedTypeSymbol e, string gd, out string member) => MatchEnum(e, gd, out member, wholeName: true);

    /// <summary>A nested type of the class or a base, by GDScript's name (Input.MouseMode is Input.MouseModeEnum, Node.ProcessMode is ProcessModeEnum).</summary>
    public INamedTypeSymbol? NestedType(INamedTypeSymbol? type, string name)
    {
        foreach (INamedTypeSymbol t in Chain(type))
            if ((t.GetTypeMembers(name).FirstOrDefault() ?? t.GetTypeMembers(name + "Enum").FirstOrDefault()) is { } n) return n;
        return null;
    }

    // Two more of GodotSharp's prefixes: one that ends the enum's name (Control.PRESET_FULL_RECT is LayoutPreset.FullRect) and one that differs from
    // it in the word's ending only (Image.INTERPOLATE_LANCZOS is Interpolation.Lanczos).
    private static bool PrefixNamesEnum(string prefix, string enumName)
    {
        if (prefix.Length >= 4 && enumName.EndsWith(prefix, StringComparison.Ordinal)) return true;
        int common = 0;
        while (common < prefix.Length && common < enumName.Length && prefix[common] == enumName[common]) common++;
        return common >= 5 && common >= prefix.Length - 1;
    }

    /// <summary>The virtual a GDScript function of that name overrides in the API base chain: the C# name and the method.</summary>
    public IMethodSymbol? FindVirtual(INamedTypeSymbol? baseType, string gd)
    {
        string name = gd.StartsWith('_') ? Naming.VirtualName(gd) : Naming.MethodName(gd);
        foreach (INamedTypeSymbol t in Chain(baseType))
            foreach (ISymbol m in t.GetMembers(name))
                if (m is IMethodSymbol ms && (ms.IsVirtual || ms.IsAbstract || ms.IsOverride) && Visible(ms) && !HostVirtual(ms)) return ms;
        return null;
    }

    // A virtual first declared by the engine host or .NET (Node.OnInput overrides Aver.Framework's): Aver.Fabric uses it to run the node, it is not the
    // Godot API, so a GDScript function of that name (func on_input) is the script's own method.
    private static bool HostVirtual(IMethodSymbol m)
    {
        IMethodSymbol root = m;
        while (root.OverriddenMethod is { } o) root = o;
        string ns = root.ContainingNamespace?.ToDisplayString() ?? "";
        return ns == "System" || (ns.StartsWith("Aver", StringComparison.Ordinal) && !ns.StartsWith("Aver.Fabric", StringComparison.Ordinal));
    }

    public List<IMethodSymbol> Constructors(INamedTypeSymbol? t) =>
        t == null ? new() : t.Constructors.Where(c => !c.IsStatic && c.DeclaredAccessibility == Accessibility.Public).ToList();

    /// <summary>Operator methods (op_Multiply ...) of the types, for typed arithmetic on structs.</summary>
    public IEnumerable<IMethodSymbol> Operators(string opName, params INamedTypeSymbol?[] types)
    {
        foreach (INamedTypeSymbol? t in types)
            if (t != null)
                foreach (IMethodSymbol m in t.GetMembers(opName).OfType<IMethodSymbol>()) yield return m;
    }

    /// <summary>True when a conversion operator, implicit or explicit, goes from one type to the other (Vector3 to Vector3I, back again).</summary>
    public bool HasConversion(INamedTypeSymbol? from, INamedTypeSymbol? to) =>
        from != null && to != null && new[] { from, to }.SelectMany(t => t.GetMembers().OfType<IMethodSymbol>()).Any(m =>
            m.MethodKind == MethodKind.Conversion && m.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, from) &&
            SymbolEqualityComparer.Default.Equals(m.ReturnType, to));

    public bool HasImplicitConversion(INamedTypeSymbol? from, string toFabricName) =>
        from != null && from.GetMembers("op_Implicit").OfType<IMethodSymbol>().Any(m => m.ReturnType.Name == toFabricName);

    /// <summary>Methods and properties named like <paramref name="csName"/> found in the nested type chain (for SignalName lookups).</summary>
    public bool HasMember(INamedTypeSymbol? type, string csName) => Chain(type).Any(t => t.GetMembers(csName).Any());
}
