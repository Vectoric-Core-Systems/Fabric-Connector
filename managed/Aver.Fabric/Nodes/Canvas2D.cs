// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// The bridge between the 2D nodes and the engine's 2D canvas (docs/plans/RENDER_2D.md in the engine). The engine owns 2D: a Node2D, a Sprite2D and a CanvasLayer each
// keep a C2DTransform / CSprite2D / C2DLayer component on an entity (an AnimatedSprite2D also a CSpriteAnim2D, a Camera2D a CCamera2D, a Parallax2D a CParallax2D), and the host flushes the canvas into the UI draw list after the frame. Once a frame the
// nodes tell the engine what changed (a script that moves or retints a sprite is seen the next frame), and the tree is numbered once so that sprites and Controls
// share one draw order: (canvas layer, z, tree order).

using Aver.Framework;

namespace Aver.Fabric;

/// <summary>A node the 2D canvas keeps an entity for.</summary>
internal interface ICanvas2DNode
{
    Node Node { get; }
    Canvas2DLink Link { get; }

    /// <summary>Writes the node's state to its entity if it changed since the last time.</summary>
    void Push2D();
}

/// <summary>The engine calls the bridge makes. The engine's own implementation is the default; a test installs a recording one, because the native scene library cannot sit
/// beside the managed assembly of the same name in a test process.</summary>
internal interface ICanvas2DBackend
{
    bool Available { get; }
    bool Register();
    int CreateEntity(string name);
    bool IsAlive(int entity);
    void DestroyEntity(int entity);
    void SetParent(int entity, int parent);
    bool Set(int entity, in Aver.Framework.Transform2D t);
    bool Set(int entity, in Aver.Framework.Sprite2D s);
    bool Set(int entity, in Aver.Framework.Layer2D l);
    void SetCanvas(CanvasStretchMode mode, CanvasStretchAspect aspect, CanvasScaleMode scaleMode, float baseWidth, float baseHeight, float scale, System.Numerics.Vector4 view);
    bool SubmitExternal(int layer, int z, long order, Action draw);

    // The animation, camera and parallax calls. The defaults are for a backend that has none (every older test double): they do nothing and report failure.
    bool Set(int entity, in Aver.Framework.Camera2D c) => false;
    bool Set(int entity, in Aver.Framework.Parallax2D p) => false;
    /// <summary>Writes the playback settings (speed scale, autoplay), adding the animation component.</summary>
    bool SetAnimation(int entity, in Aver.Framework.SpriteAnim2D a) => false;
    bool SetClip(int entity, int index, string name, float fps, bool loop, ReadOnlySpan<Aver.Framework.SpriteAnimFrame2D> frames) => false;
    void RemoveAnimation(int entity) { }
    bool Play(int entity, string? name, bool restart) => false;
    void Stop(int entity) { }
    void Pause(int entity) { }
    void SetFrame(int entity, int frame, float progress) { }
    bool TryGetAnimation(int entity, out Aver.Framework.SpriteAnim2D a) { a = default; return false; }
    bool TryGetClipName(int entity, int index, out string name) { name = string.Empty; return false; }
    IReadOnlyList<Aver.Framework.SpriteAnimEvent2D> PollAnimationEvents() => System.Array.Empty<Aver.Framework.SpriteAnimEvent2D>();
    bool MakeCurrent(int entity) => false;
    /// <summary>The entity of the camera that applies, 0 for none.</summary>
    int CurrentCamera => 0;
    bool TryGetCameraCenter(int entity, out System.Numerics.Vector2 center) { center = default; return false; }
}

internal sealed class EngineCanvas2DBackend : ICanvas2DBackend
{
    public bool Available => Scene2D.Available;
    public bool Register() => Scene2D.Register();
    public int CreateEntity(string name) => Entity.Create(name).Handle;
    public bool IsAlive(int entity) => entity != 0 && Aver.Framework.Hosting.ExtensionHost.EntityFromHandle(entity).IsAlive;
    public void DestroyEntity(int entity) => Aver.Framework.Hosting.ExtensionHost.EntityFromHandle(entity).Destroy();
    public void SetParent(int entity, int parent)
    {
        Entity e = Aver.Framework.Hosting.ExtensionHost.EntityFromHandle(entity);
        if (parent != 0) e.SetParent(Aver.Framework.Hosting.ExtensionHost.EntityFromHandle(parent));
        else e.Detach();
    }
    public bool Set(int entity, in Aver.Framework.Transform2D t) => Scene2D.Set(Aver.Framework.Hosting.ExtensionHost.EntityFromHandle(entity), in t);
    public bool Set(int entity, in Aver.Framework.Sprite2D s) => Scene2D.Set(Aver.Framework.Hosting.ExtensionHost.EntityFromHandle(entity), in s);
    public bool Set(int entity, in Aver.Framework.Layer2D l) => Scene2D.Set(Aver.Framework.Hosting.ExtensionHost.EntityFromHandle(entity), in l);
    public void SetCanvas(CanvasStretchMode mode, CanvasStretchAspect aspect, CanvasScaleMode scaleMode, float baseWidth, float baseHeight, float scale, System.Numerics.Vector4 view) =>
        Scene2D.SetCanvas(mode, aspect, scaleMode, baseWidth, baseHeight, scale, view);
    public bool SubmitExternal(int layer, int z, long order, Action draw) => Scene2D.SubmitExternal(layer, z, order, draw);

    private static Entity E(int handle) => Aver.Framework.Hosting.ExtensionHost.EntityFromHandle(handle);
    public bool Set(int entity, in Aver.Framework.Camera2D c) => Scene2D.Set(E(entity), in c);
    public bool Set(int entity, in Aver.Framework.Parallax2D p) => Scene2D.Set(E(entity), in p);
    public bool SetAnimation(int entity, in Aver.Framework.SpriteAnim2D a) => Scene2D.Set(E(entity), in a);
    public bool SetClip(int entity, int index, string name, float fps, bool loop, ReadOnlySpan<Aver.Framework.SpriteAnimFrame2D> frames) => Scene2D.SetClip(E(entity), index, name, fps, loop, frames);
    public void RemoveAnimation(int entity) => Scene2D.Remove(E(entity), Scene2DComponent.SpriteAnim);
    public bool Play(int entity, string? name, bool restart) => Scene2D.Play(E(entity), name, restart);
    public void Stop(int entity) => Scene2D.Stop(E(entity));
    public void Pause(int entity) => Scene2D.Pause(E(entity));
    public void SetFrame(int entity, int frame, float progress) => Scene2D.SetFrame(E(entity), frame, progress);
    public bool TryGetAnimation(int entity, out Aver.Framework.SpriteAnim2D a) => Scene2D.TryGet(E(entity), out a);
    public bool TryGetClipName(int entity, int index, out string name)
    {
        bool ok = Scene2D.TryGetClip(E(entity), index, out name, out _, out _, out _);
        return ok;
    }
    public IReadOnlyList<Aver.Framework.SpriteAnimEvent2D> PollAnimationEvents() => Scene2D.PollAnimationEvents();
    public bool MakeCurrent(int entity) => Scene2D.MakeCurrent(E(entity));
    public int CurrentCamera => Scene2D.CurrentCamera.Handle;
    public bool TryGetCameraCenter(int entity, out System.Numerics.Vector2 center) => Scene2D.TryGetCameraCenter(E(entity), out center);
}

/// <summary>What the bridge remembers per node: the entity made for it when the engine gave the node none (a managed host, a test), and the last state written.</summary>
internal sealed class Canvas2DLink
{
    internal int Private;
    internal int PrivateParent;
    /// <summary>The node's place in the whole tree, counted from 1.</summary>
    internal int Order;
    internal bool HasTransform, HasSprite, HasLayer, HasCamera, HasParallax;
    internal Aver.Framework.Transform2D Transform;
    internal Aver.Framework.Sprite2D Sprite;
    internal Aver.Framework.Layer2D Layer;
    internal Aver.Framework.Camera2D Camera;
    internal Aver.Framework.Parallax2D Parallax;

    internal void Release(ICanvas2DBackend backend)
    {
        if (Private != 0 && backend.IsAlive(Private)) backend.DestroyEntity(Private);
        Private = 0;
        PrivateParent = 0;
        HasTransform = HasSprite = HasLayer = HasCamera = HasParallax = false;
    }
}

internal static class Canvas2D
{
    private static readonly List<ICanvas2DNode> s_nodes = new();
    private static readonly Dictionary<int, AnimatedSprite2D> s_animated = new();
    private static int s_orderSeen = -1;

    /// <summary>The engine calls; a test swaps it.</summary>
    internal static ICanvas2DBackend Backend { get; set; } = new EngineCanvas2DBackend();

    /// <summary>The 2D nodes in the tree (a test reads it).</summary>
    internal static int Count => s_nodes.Count;

    internal static void Enter(ICanvas2DNode n)
    {
        if (!s_nodes.Contains(n)) s_nodes.Add(n);
        s_orderSeen = -1;
    }

    internal static void Exit(ICanvas2DNode n)
    {
        if (n is AnimatedSprite2D a) Unlisten(a);
        s_nodes.Remove(n);
        n.Link.Release(Backend);
        s_orderSeen = -1;
    }

    internal static void ResetForTests()
    {
        foreach (ICanvas2DNode n in s_nodes.ToArray()) n.Link.Release(Backend);
        s_nodes.Clear();
        s_animated.Clear();
        s_orderSeen = -1;
        Backend = new EngineCanvas2DBackend();
    }

    /// <summary>The entity a node already has, without making one: the engine's own, else the one made for it. 0 for none.</summary>
    internal static int Peek(ICanvas2DNode n) => NodeHost.Current.IsEngine && n.Node.Self.IsValid ? n.Node.Self.Handle : n.Link.Private;

    /// <summary>Routes the engine's animation events for an entity to the node (its signals).</summary>
    internal static void Listen(int entity, AnimatedSprite2D node) => s_animated[entity] = node;

    private static void Unlisten(AnimatedSprite2D node)
    {
        foreach (var kv in s_animated.Where(kv => ReferenceEquals(kv.Value, node)).ToArray()) s_animated.Remove(kv.Key);
    }

    /// <summary>The entity that carries a 2D node's components: the engine's own entity for the node, else one made for it (parented like the node, so the engine composes
    /// the chain). 0 when there is no 2D library or the world refused.</summary>
    internal static int Backing(ICanvas2DNode n)
    {
        ICanvas2DBackend backend = Backend;
        if (!backend.Available) return 0;
        Node node = n.Node;
        if (NodeHost.Current.IsEngine && node.Self.IsValid) return node.Self.Handle;
        Canvas2DLink link = n.Link;
        if (link.Private == 0 || !backend.IsAlive(link.Private))
        {
            link.HasTransform = link.HasSprite = link.HasLayer = link.HasCamera = link.HasParallax = false;
            link.Private = backend.CreateEntity(node.Name.ToString());
            link.PrivateParent = 0;
            if (link.Private == 0) return 0;
        }
        ICanvas2DNode? above = null;
        for (Node? a = node.GetParent(); a != null && above == null; a = a.GetParent()) above = a as ICanvas2DNode;
        int parent = above != null ? Backing(above) : 0;
        if (parent != link.PrivateParent)
        {
            backend.SetParent(link.Private, parent);
            link.PrivateParent = parent;
        }
        return link.Private;
    }

    /// <summary>Numbers the tree from 1 in tree order, when its shape changed since the last numbering. Controls and sprites read the numbers as their tree order.</summary>
    internal static void RefreshOrder()
    {
        if (Node.OrderVersion == s_orderSeen) return;
        s_orderSeen = Node.OrderVersion;
        int order = 1;
        Number(SceneTree.Instance.Root, ref order);
    }

    private static void Number(Node n, ref int order)
    {
        int mine = order++;
        if (n is CanvasItem ci) ci.GuiOrder = mine;
        if (n is ICanvas2DNode c) c.Link.Order = mine;
        foreach (Node child in n.ChildrenSnapshot()) Number(child, ref order);
    }

    /// <summary>Once an idle frame, before the host flushes the canvas.</summary>
    internal static void FrameUpdate()
    {
        if (s_nodes.Count == 0 || !Backend.Available) return;
        Backend.Register();
        RefreshOrder();
        if (s_animated.Count > 0)
            foreach (Aver.Framework.SpriteAnimEvent2D ev in Backend.PollAnimationEvents())
                if (s_animated.TryGetValue(ev.Entity.Handle, out AnimatedSprite2D? sprite)) sprite.OnEngineEvent(ev);
        foreach (ICanvas2DNode n in s_nodes.ToArray())
            if (n.Node.IsInsideTree()) n.Push2D();
        SceneTree.Instance.Root.PublishCanvas();
    }

    // ---- the writes: set the component only when the value moved ----

    internal static void Write(int e, Canvas2DLink link, in Aver.Framework.Transform2D t)
    {
        if (link.HasTransform && Same(link.Transform, t)) return;
        if (Backend.Set(e, in t)) { link.Transform = t; link.HasTransform = true; }
    }

    internal static void Write(int e, Canvas2DLink link, in Aver.Framework.Sprite2D s)
    {
        if (link.HasSprite && Same(link.Sprite, s)) return;
        if (Backend.Set(e, in s)) { link.Sprite = s; link.HasSprite = true; }
    }

    internal static void Write(int e, Canvas2DLink link, in Aver.Framework.Layer2D l)
    {
        if (link.HasLayer && Same(link.Layer, l)) return;
        if (Backend.Set(e, in l)) { link.Layer = l; link.HasLayer = true; }
    }

    internal static void Write(int e, Canvas2DLink link, in Aver.Framework.Camera2D c)
    {
        if (link.HasCamera && Same(link.Camera, c)) return;
        if (Backend.Set(e, in c)) { link.Camera = c; link.HasCamera = true; }
    }

    internal static void Write(int e, Canvas2DLink link, in Aver.Framework.Parallax2D p)
    {
        if (link.HasParallax && Same(link.Parallax, p)) return;
        if (Backend.Set(e, in p)) { link.Parallax = p; link.HasParallax = true; }
    }

    private static bool Same(in Aver.Framework.Camera2D a, in Aver.Framework.Camera2D b) =>
        a.Zoom == b.Zoom && a.Offset == b.Offset && a.Limit == b.Limit && a.SmoothSpeed == b.SmoothSpeed && a.Drag == b.Drag && a.Flags == b.Flags;

    private static bool Same(in Aver.Framework.Parallax2D a, in Aver.Framework.Parallax2D b) =>
        a.ScrollScale == b.ScrollScale && a.ScrollOffset == b.ScrollOffset && a.RepeatSize == b.RepeatSize && a.RepeatTimes == b.RepeatTimes && a.Autoscroll == b.Autoscroll &&
        a.LimitBegin == b.LimitBegin && a.LimitEnd == b.LimitEnd && a.Flags == b.Flags;

    private static bool Same(in Aver.Framework.Transform2D a, in Aver.Framework.Transform2D b) =>
        a.Position == b.Position && a.Rotation == b.Rotation && a.Scale == b.Scale && a.ZIndex == b.ZIndex && a.Flags == b.Flags && a.TreeOrder == b.TreeOrder;

    private static bool Same(in Aver.Framework.Sprite2D a, in Aver.Framework.Sprite2D b) =>
        a.Texture == b.Texture && a.Region == b.Region && a.Offset == b.Offset && a.Modulate == b.Modulate && a.Flags == b.Flags && a.HFrames == b.HFrames &&
        a.VFrames == b.VFrames && a.Frame == b.Frame && a.Blend == b.Blend;

    private static bool Same(in Aver.Framework.Layer2D a, in Aver.Framework.Layer2D b) =>
        a.Index == b.Index && a.Flags == b.Flags && a.Offset == b.Offset && a.Rotation == b.Rotation && a.Scale == b.Scale;

    // ---- conversions ----

    internal static System.Numerics.Vector2 N(Vector2 v) => new(v.X, v.Y);

    /// <summary>The engine reads a scale of 0 as 1; a node scaled to nothing is scaled to almost nothing instead.</summary>
    internal static System.Numerics.Vector2 NScale(Vector2 v) => new(v.X == 0f ? 1e-6f : v.X, v.Y == 0f ? 1e-6f : v.Y);

    /// <summary>Straight RGBA as the engine packs it (0xAABBGGRR). A fully transparent colour packs to 0, which the engine reads as white: callers hide the sprite instead.</summary>
    internal static uint Pack(Color c)
    {
        static uint B(float v) => (uint)Math.Round(Math.Clamp(v, 0f, 1f) * 255f);
        return (B(c.A) << 24) | (B(c.B) << 16) | (B(c.G) << 8) | B(c.R);
    }

    /// <summary>The asset id the engine knows a texture file by: fnv1a64 of its content-relative path. 0 for a texture that is not a file.</summary>
    internal static long AssetIdOf(Texture2D? texture)
    {
        if (texture == null) return 0;
        string path = texture.ResourcePath;
        if (path.Length == 0) return 0;
        string rel = UiImages.ContentRelative(path) ?? path;
        if (rel.StartsWith("res://", StringComparison.Ordinal)) rel = rel[6..];
        return Aver.Scene.Assets.ObjectIdOf(rel.Replace('\\', '/'));
    }
}
