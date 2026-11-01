// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// P39: a Texture2D's content path becomes the engine's UI texture id. The wiring (lazy resolve, caching, session epochs, the host's own handle winning)
// with a fake resolver, then the real Aver.UI.Abi with a fake image table installed the way the editor and the shipped game install theirs.

using System.Runtime.InteropServices;
using Aver.Fabric;

static class UiImageTests
{
    public static int RunAll() => T.Suite(() =>
    {
        T.Run("UI textures: the default resolver is the engine's and does nothing outside the engine", DefaultResolver);
        T.Run("UI textures: a handle is asked for once, the host's own wins, a new session asks again", ResolverWiring);
        if (UiTests.EnsureNative() && HasImageTable())
        {
            T.Run("UI textures: a content path reaches the engine's image table once and is released with the session", EngineTable);
            T.Run("UI textures: a missing file and an engine without a table draw nothing and say so once", EngineRefuses);
        }
        else Console.WriteLine("Test: ui images (native part SKIP: Aver.UI.Abi was not found, or the copy beside the tests predates the image table; build the Aver.UI.Abi target and delete the old copy)");
    });

    // An Aver.UI.Abi from before the image service has no table to install (the library beside the tests is replaced only when the process has not loaded it yet).
    private static bool HasImageTable()
    {
        try { UiNative.aver_ui_set_image_host(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero); return true; }
        catch (EntryPointNotFoundException) { return false; }
    }

    // ---- a fake image table, installed in the real library ----
    // Through Fabric's own binding: a DllImport of this assembly can resolve to another copy of the library, whose table Fabric would never see.

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong AcquireFn([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ReleaseFn(ulong id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SizeFn(ulong id, IntPtr outWH);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong UseFn([MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    private static void aver_ui_set_image_host(AcquireFn? acquire, ReleaseFn? release, SizeFn? size, UseFn? use) =>
        UiNative.aver_ui_set_image_host(Ptr(acquire), Ptr(release), Ptr(size), Ptr(use));

    private static IntPtr Ptr(Delegate? d) => d == null ? IntPtr.Zero : Marshal.GetFunctionPointerForDelegate(d);

    private static readonly List<string> s_acquired = new();
    private static readonly List<ulong> s_released = new();
    private static AcquireFn? s_acquireFn;
    private static ReleaseFn? s_releaseFn;
    private static SizeFn? s_sizeFn;
    private static UseFn? s_useFn;

    /// <summary>Installs a table that knows ids by path. A path it is not given fails like an image that does not load.</summary>
    private static void InstallTable(Dictionary<string, (ulong Id, int W, int H)> known)
    {
        s_acquired.Clear();
        s_released.Clear();
        s_acquireFn = path => { s_acquired.Add(path); return known.TryGetValue(path, out var k) ? k.Id : 0; };
        s_releaseFn = id => s_released.Add(id);
        s_sizeFn = (id, wh) =>
        {
            foreach (var k in known.Values)
                if (k.Id == id) { Marshal.WriteInt32(wh, 0, k.W); Marshal.WriteInt32(wh, 4, k.H); return 1; }
            return 0;
        };
        s_useFn = path => known.TryGetValue(path, out var k) ? k.Id : 0;
        aver_ui_set_image_host(s_acquireFn, s_releaseFn, s_sizeFn, s_useFn);
    }

    private static void RemoveTable()
    {
        aver_ui_set_image_host(null, null, null, null);
        s_acquireFn = null; s_releaseFn = null; s_sizeFn = null; s_useFn = null;
    }

    /// <summary>A node host that says it is the engine, so textures ask the native table; nodes are still managed.</summary>
    private sealed class EngineFlagHost : INodeHost
    {
        public bool IsEngine => true;
        public void Enter(Node subtreeRoot) => NodeHost.ManagedEnter(subtreeRoot);
        public void Exit(Node subtreeRoot) => NodeHost.ManagedExit(subtreeRoot);
        public void Destroy(Node node) => NodeHost.ManagedExit(node);
    }

    private static string MakeContent(params string[] files)
    {
        string root = Path.Combine(Path.GetTempPath(), "fabric-uiimg-" + Guid.NewGuid().ToString("N"));
        foreach (string f in files)
        {
            string full = Path.Combine(root, f.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, new byte[] { 1, 2, 3 });
        }
        Directory.CreateDirectory(root);
        return root;
    }

    // ---- the wiring, no engine ----

    private static void DefaultResolver()
    {
        T.Check("HandleResolver is the engine resolver out of the box, so a converted game needs no setup", Texture2D.HandleResolver == UiImages.Resolve);
        var tex = new Texture2D { ResourcePath = "Textures/ui/a.png" };
        T.Check("outside the engine a texture has no engine handle, and asking costs nothing", tex.EngineHandle == 0);
        T.Check("a texture with no path has none either", new Texture2D().EngineHandle == 0);
    }

    private static void ResolverWiring()
    {
        var saved = Texture2D.HandleResolver;
        try
        {
            int calls = 0;
            ulong next = 100;
            Texture2D.HandleResolver = t => { calls++; return next++; };

            var tex = new Texture2D { ResourcePath = "Textures/ui/a.png" };
            T.Check("nothing is resolved until the handle is wanted", calls == 0);
            ulong first = tex.EngineHandle;
            T.Check("the first ask resolves by the texture's path", first == 100 && calls == 1);
            T.Check("and later asks reuse it", tex.EngineHandle == 100 && tex.EngineHandle == 100 && calls == 1);

            var host = new Texture2D { ResourcePath = "Textures/ui/b.png", HostHandle = 7 };
            T.Check("a handle the host set is never replaced by the resolver", host.EngineHandle == 7 && calls == 1);

            var pathless = new Texture2D();
            T.Check("a texture with no resource path is not resolved", pathless.EngineHandle == 0 && calls == 1);

            UiImages.NewSession();
            T.Check("after a session boundary the texture asks again (its old handle died with the session)", tex.EngineHandle == 101 && calls == 2);
            T.Check("a handle the host set survives the boundary", host.EngineHandle == 7);

            var failing = new Texture2D { ResourcePath = "Textures/ui/c.png" };
            Texture2D.HandleResolver = t => 0;
            T.Check("a resolver that finds nothing leaves the handle at 0", failing.EngineHandle == 0);
        }
        finally { Texture2D.HandleResolver = saved; }
    }

    // ---- the real library ----

    private static void EngineTable()
    {
        string root = MakeContent("Textures/ui/a.png", "Textures/ui/photo.jpg", "Sounds/click.wav");
        try
        {
            NodeHost.Current = new EngineFlagHost();
            ResourceLoader.ContentRootOverride = root;
            InstallTable(new()
            {
                ["Textures/ui/a.png"] = (0x1001, 64, 32),
                ["Textures/ui/photo.jpg"] = (0x1002, 800, 600),
            });

            var tex = new Texture2D { ResourcePath = "Textures/ui/a.png" };
            T.Check("a texture's path is acquired from the engine's table", tex.EngineHandle == 0x1001, $"{tex.EngineHandle:X}");
            var again = new Texture2D { ResourcePath = "Textures/ui/a.png" };
            T.Check("a second texture of the same file shares the handle and does not acquire again", again.EngineHandle == 0x1001 && s_acquired.Count == 1);

            var res = new Texture2D { ResourcePath = "res://Textures/ui/a.png" };
            T.Check("a res:// path is the same file: the same handle, one reference", res.EngineHandle == 0x1001 && s_acquired.Count == 1);

            var bare = new Texture2D { ResourcePath = "ui/photo.jpg" };
            T.Check("a path without its Textures folder finds the file the way the loader does, and the engine is given the content-relative path",
                    bare.EngineHandle == 0x1002 && s_acquired.Contains("Textures/ui/photo.jpg"));
            T.Check("a picture that is not a PNG is sized by the engine", bare.GetSize() is { X: 800, Y: 600 });

            var box = new StyleBoxTexture { Texture = tex };
            T.Check("a texture style box carries the handle into the native struct", box.ToNative().Texture == 0x1001);

            UiImages.NewSession();
            T.Check("the session boundary releases each reference the texture table gave, once", s_released.Count == 2 && s_released.Contains(0x1001) && s_released.Contains(0x1002),
                    string.Join(",", s_released));
            ulong before = tex.EngineHandle;
            T.Check("and the next ask acquires afresh", before == 0x1001 && s_acquired.Count > 2);

            RemoveTable();
            T.Check("with the table gone a texture draws nothing", new Texture2D { ResourcePath = "Textures/ui/never.png" }.EngineHandle == 0);
        }
        finally
        {
            RemoveTable();
            UiImages.NewSession();
            Directory.Delete(root, true);
        }
    }

    private static void EngineRefuses()
    {
        string root = MakeContent("Textures/ui/a.png");
        try
        {
            NodeHost.Current = new EngineFlagHost();
            ResourceLoader.ContentRootOverride = root;
            InstallTable(new());   // the engine could not load anything

            var tex = new Texture2D { ResourcePath = "Textures/ui/a.png" };
            T.Check("an image the engine cannot load gives 0", tex.EngineHandle == 0 && s_acquired.Count == 1);
            T.Check("and is not asked for again every frame", tex.EngineHandle == 0 && new Texture2D { ResourcePath = "Textures/ui/a.png" }.EngineHandle == 0 && s_acquired.Count == 1);

            T.Logs.Clear();
            var missing = new Texture2D { ResourcePath = "Textures/ui/not_there.png" };
            T.Check("a file that is not in Content is not sent to the engine", missing.EngineHandle == 0 && !s_acquired.Contains("Textures/ui/not_there.png"));
            _ = missing.EngineHandle;
            int warnings = T.Logs.Count(l => l.Message.Contains("not_there.png"));
            T.Check("it is reported once, naming the file", warnings == 1, $"{warnings}");
            T.Check("a texture the engine could not load has no size", new Texture2D { ResourcePath = "Textures/ui/a.jpg" }.GetSize() == Vector2.Zero);

            RemoveTable();
            UiImages.NewSession();
            T.Check("without a table nothing is acquired, and asking is safe", new Texture2D { ResourcePath = "Textures/ui/a.png" }.EngineHandle == 0);
        }
        finally
        {
            RemoveTable();
            UiImages.NewSession();
            Directory.Delete(root, true);
        }
    }
}
