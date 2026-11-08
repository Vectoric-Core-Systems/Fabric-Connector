// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// AnimatedSprite2D, Camera2D and Parallax2D at run time (docs/plans/RENDER_2D.md in the engine): the nodes upload the SpriteFrames as the engine's clips, forward play(), stop(), pause()
// and frame to it, write the camera and the parallax to their components, and raise the engine's animation events as Godot's signals. The native scene library cannot sit beside the managed
// assembly of the same name in a test process, so the engine calls go to a recording backend with a small playhead of its own (the real clock is tested natively by Scene2dAnimTest).

using Aver.Fabric;
using Layer2DFlags = Aver.Framework.Layer2DFlags;
using SpriteAnimEvent2D = Aver.Framework.SpriteAnimEvent2D;
using SpriteAnimEventKind = Aver.Framework.SpriteAnimEventKind;
using Camera2DFlags = Aver.Framework.Camera2DFlags;
using Parallax2DFlags = Aver.Framework.Parallax2DFlags;
using Sprite2DFlags = Aver.Framework.Sprite2DFlags;

static class Anim2DTests
{
    private sealed class FakeEngine : ICanvas2DBackend
    {
        public bool Available => true;
        public bool Register() => true;
        private int _next = 500;
        public readonly HashSet<int> Dead = new();
        public readonly Dictionary<int, Aver.Framework.Sprite2D> S2 = new();
        public readonly Dictionary<int, Aver.Framework.Camera2D> Cam = new();
        public readonly Dictionary<int, Aver.Framework.Parallax2D> Par = new();
        public readonly Dictionary<int, Aver.Framework.Layer2D> Layers = new();
        public readonly Dictionary<int, List<(string Name, float Fps, bool Loop, Aver.Framework.SpriteAnimFrame2D[] Frames)>> Clips = new();
        public readonly Dictionary<int, Aver.Framework.SpriteAnim2D> Anim = new();
        public readonly List<string> Calls = new();
        public readonly List<Aver.Framework.SpriteAnimEvent2D> Events = new();
        public int Current;
        public System.Numerics.Vector2 Center = new(11, 22);

        public int CreateEntity(string name) => _next++;
        public bool IsAlive(int entity) => !Dead.Contains(entity);
        public void DestroyEntity(int entity) => Dead.Add(entity);
        public void SetParent(int entity, int parent) { }
        public bool Set(int entity, in Aver.Framework.Transform2D t) => true;
        public bool Set(int entity, in Aver.Framework.Sprite2D s) { S2[entity] = s; return true; }
        public bool Set(int entity, in Aver.Framework.Layer2D l) { Layers[entity] = l; return true; }
        public void SetCanvas(Aver.Framework.CanvasStretchMode mode, Aver.Framework.CanvasStretchAspect aspect, Aver.Framework.CanvasScaleMode scaleMode, float baseWidth, float baseHeight, float scale, System.Numerics.Vector4 view) { }
        public bool SubmitExternal(int layer, int z, long order, Action draw) => true;

        public bool Set(int entity, in Aver.Framework.Camera2D c) { Cam[entity] = c; if (Current == 0 && (c.Flags & Camera2DFlags.Disabled) == 0) Current = entity; return true; }
        public bool Set(int entity, in Aver.Framework.Parallax2D p) { Par[entity] = p; return true; }
        public bool SetAnimation(int entity, in Aver.Framework.SpriteAnim2D a)
        {
            Anim.TryGetValue(entity, out var old);
            Anim[entity] = old with { SpeedScale = a.SpeedScale, Autoplay = a.Autoplay };
            Calls.Add($"settings {a.SpeedScale}");
            return true;
        }
        public bool SetClip(int entity, int index, string name, float fps, bool loop, ReadOnlySpan<Aver.Framework.SpriteAnimFrame2D> frames)
        {
            if (!Clips.TryGetValue(entity, out var list)) Clips[entity] = list = new();
            var entry = (name, fps, loop, frames.ToArray());
            if (index == list.Count) list.Add(entry); else list[index] = entry;
            Anim.TryGetValue(entity, out var old);
            Anim[entity] = old with { ClipCount = list.Count, Clip = 0 };
            return true;
        }
        public void RemoveAnimation(int entity) { Clips.Remove(entity); Anim.Remove(entity); Calls.Add("remove"); }
        public bool Play(int entity, string? name, bool restart)
        {
            Calls.Add($"play {name} {(restart ? "restart" : "")}".TrimEnd());
            if (!Clips.TryGetValue(entity, out var list)) return false;
            int idx = name == null ? Anim[entity].Clip : list.FindIndex(c => c.Name == name);
            if (idx < 0) return false;
            var a = Anim[entity];
            bool changed = idx != a.Clip;
            Anim[entity] = a with { Clip = idx, Playing = true, Frame = changed || restart ? 0 : a.Frame };
            return true;
        }
        public void Stop(int entity) { Calls.Add("stop"); Anim[entity] = Anim[entity] with { Playing = false, Frame = 0 }; }
        public void Pause(int entity) { Calls.Add("pause"); Anim[entity] = Anim[entity] with { Playing = false }; }
        public void SetFrame(int entity, int frame, float progress) { Calls.Add($"frame {frame} {progress}"); Anim[entity] = Anim[entity] with { Frame = frame, Progress = progress }; }
        public bool TryGetAnimation(int entity, out Aver.Framework.SpriteAnim2D a) => Anim.TryGetValue(entity, out a);
        public bool TryGetClipName(int entity, int index, out string name) { name = ""; return false; }
        public IReadOnlyList<Aver.Framework.SpriteAnimEvent2D> PollAnimationEvents()
        {
            var copy = Events.ToArray();
            Events.Clear();
            return copy;
        }
        public bool MakeCurrent(int entity)
        {
            if (!Cam.ContainsKey(entity)) return false;
            Calls.Add($"current {entity}");
            Current = entity;
            return true;
        }
        int ICanvas2DBackend.CurrentCamera => Current;
        public bool TryGetCameraCenter(int entity, out System.Numerics.Vector2 center) { center = Center; return Cam.ContainsKey(entity); }
    }

    public static int RunAll() => T.Suite(() =>
    {
        T.Run("2D animation: SpriteFrames reach the engine as clips, and play/stop/frame drive it", AnimatedUpload);
        T.Run("2D animation: the engine's events are Godot's signals, and the node mirrors the engine's state", AnimatedEvents);
        T.Run("2D camera: Camera2D writes the engine's camera and make_current moves the flag", CameraWrites);
        T.Run("2D camera: Parallax2D, ParallaxLayer and ParallaxBackground", ParallaxWrites);
    });

    private static FakeEngine Setup()
    {
        T.Reset();
        Canvas2D.ResetForTests();
        var f = new FakeEngine();
        Canvas2D.Backend = f;
        return f;
    }

    private static Window Root => SceneTree.Instance.Root;
    private static int EntityOf(Node n) => ((ICanvas2DNode)n).Link.Private;
    private static Aver.Framework.Entity Hand(int handle) => Aver.Framework.Hosting.ExtensionHost.EntityFromHandle(handle);
    private static long Id(string path) => Aver.Scene.Assets.ObjectIdOf(path);

    private static SpriteFrames MakeFrames()
    {
        var sf = new SpriteFrames();
        sf.RenameAnimation("default", "idle");
        sf.SetAnimationSpeed("idle", 6.0);
        sf.AddFrame("idle", new Texture2D { ResourcePath = "Textures/hero/a.png" });
        sf.AddFrame("idle", new Texture2D { ResourcePath = "res://Textures/hero/b.png" }, 2f);
        sf.AddAnimation("run");
        sf.SetAnimationSpeed("run", 12.0);
        sf.SetAnimationLoop("run", false);
        sf.AddFrame("run", new Texture2D { ResourcePath = "Textures/hero/c.png" });
        sf.AddFrame("run", null, 0.5f);
        return sf;
    }

    private static void AnimatedUpload()
    {
        FakeEngine f = Setup();
        var hero = new AnimatedSprite2D { Name = "Hero", SpriteFrames = MakeFrames(), FlipH = true, Centered = false, Offset = new Vector2(3, 4), Position = new Vector2(10, 20) };
        Root.AddChild(hero);
        T.Frame();
        int e = EntityOf(hero);
        T.Check("the sprite has an entity and its clips were uploaded", e != 0 && f.Clips.TryGetValue(e, out var clips) && clips.Count == 2);
        var cl = f.Clips[e];
        T.Check("clip 0 is idle: 6 fps, looping, two frames with their textures and durations",
                cl[0].Name == "idle" && cl[0].Fps == 6f && cl[0].Loop && cl[0].Frames.Length == 2 && cl[0].Frames[0].Texture == Id("Textures/hero/a.png") &&
                cl[0].Frames[1].Texture == Id("Textures/hero/b.png") && cl[0].Frames[0].Weight == 1f && cl[0].Frames[1].Weight == 2f);
        T.Check("clip 1 is run: 12 fps, not looping, a frame with no texture file keeps its place with texture 0",
                cl[1].Name == "run" && cl[1].Fps == 12f && !cl[1].Loop && cl[1].Frames.Length == 2 && cl[1].Frames[1].Texture == 0 && cl[1].Frames[1].Weight == 0.5f);
        var s = f.S2[e];
        T.Check("the sprite component carries the flips, centering and offset, and no texture (the animation owns it)",
                (s.Flags & Sprite2DFlags.FlipH) != 0 && (s.Flags & Sprite2DFlags.NotCentered) != 0 && s.Offset == new System.Numerics.Vector2(3, 4) && s.Texture == 0);
        T.Check("it starts stopped", !hero.IsPlaying() && hero.Frame == 0 && hero.Animation == "idle");

        f.Calls.Clear();
        hero.Play("run");
        T.Check("play(name) plays that clip on the engine", f.Calls.Contains("play run") && f.Anim[e].Clip == 1 && f.Anim[e].Playing);
        T.Check("and the node reads the engine's state", hero.IsPlaying() && hero.Animation == "run" && hero.Frame == 0);
        f.Calls.Clear();
        hero.Play();
        T.Check("play() with no name continues the current clip", f.Calls.Contains("play run"));
        hero.Frame = 1;
        T.Check("setting frame sets the engine's frame", f.Anim[e].Frame == 1 && hero.Frame == 1 && f.Calls.Contains("frame 1 0"));
        hero.Pause();
        T.Check("pause pauses", !hero.IsPlaying() && f.Calls.Contains("pause"));
        hero.Stop();
        T.Check("stop stops and rewinds", f.Calls.Contains("stop") && f.Anim[e].Frame == 0 && !hero.IsPlaying());

        hero.SpeedScale = 2.5f;
        T.Check("speed_scale reaches the engine", f.Anim[e].SpeedScale == 2.5f);
        hero.Play("idle", 2f);
        T.Check("play(name, custom speed) multiplies the speed", f.Anim[e].SpeedScale == 5f && T.Near(hero.GetPlayingSpeed(), 6f * 5f));
        hero.PlayBackwards("idle");
        T.Check("play_backwards runs at a negative speed", f.Anim[e].SpeedScale == -2.5f);
        hero.SpeedScale = 1f;
        hero.Stop();
        T.Check("stop returns the custom speed to 1", f.Anim[e].SpeedScale == 1f);

        hero.Animation = "run";
        T.Check("setting animation switches the clip without playing", f.Anim[e].Clip == 1 && !f.Anim[e].Playing);
        int before = f.Calls.Count;
        hero.Play("nope");
        T.Check("play of an unknown name changes nothing", f.Calls.Count == before);

        hero.SpriteFrames!.AddFrame("idle", new Texture2D { ResourcePath = "Textures/hero/d.png" });
        T.Frame();
        T.Check("changing the SpriteFrames uploads the clips again", f.Clips.TryGetValue(e, out var again) && again[0].Frames.Length == 3 && f.Calls.Contains("remove"));

        var auto = new AnimatedSprite2D { Name = "Auto", SpriteFrames = MakeFrames(), Autoplay = "run" };
        Root.AddChild(auto);
        T.Frame();
        int ae = EntityOf(auto);
        T.Check("autoplay plays that animation when the node is ready", f.Anim[ae].Playing && f.Anim[ae].Clip == 1);

        int ge = EntityOf(hero);
        Root.RemoveChild(hero);
        T.Check("leaving the tree gives the entity back", f.Dead.Contains(ge));
        hero.Free();
    }

    private static void AnimatedEvents()
    {
        FakeEngine f = Setup();
        var hero = new AnimatedSprite2D { Name = "Hero", SpriteFrames = MakeFrames() };
        int changed = 0, anim = 0, looped = 0, finished = 0;
        hero.FrameChanged += () => changed++;
        hero.AnimationChanged += () => anim++;
        hero.AnimationLooped += () => looped++;
        hero.AnimationFinished += () => finished++;
        Root.AddChild(hero);
        T.Frame();
        int e = EntityOf(hero);
        hero.Play("run");
        T.Check("a signal does not fire inside the call that caused it", changed == 0 && anim == 0);

        f.Events.Add(new SpriteAnimEvent2D(Hand(e), SpriteAnimEventKind.AnimationChanged, 1, 0));
        f.Events.Add(new SpriteAnimEvent2D(Hand(e), SpriteAnimEventKind.FrameChanged, 1, 0));
        f.Events.Add(new SpriteAnimEvent2D(Hand(e), SpriteAnimEventKind.Looped, 1, 0));
        f.Events.Add(new SpriteAnimEvent2D(Hand(e), SpriteAnimEventKind.FrameChanged, 1, 1));
        T.Frame();
        T.Check("the engine's events are the signals, once each, the next frame", anim == 1 && changed == 2 && looped == 1 && finished == 0);

        f.Anim[e] = f.Anim[e] with { Playing = false, Frame = 1 };
        f.Events.Add(new SpriteAnimEvent2D(Hand(e), SpriteAnimEventKind.Finished, 1, 1));
        T.Frame();
        T.Check("animation_finished fires once and the node is no longer playing", finished == 1 && !hero.IsPlaying() && hero.Frame == 1);

        f.Events.Add(new SpriteAnimEvent2D(Hand(999), SpriteAnimEventKind.FrameChanged, 0, 0));
        T.Frame();
        T.Check("an event for another entity is not this node's", changed == 2);
        T.Frame();
        T.Check("and nothing repeats", changed == 2 && anim == 1 && looped == 1 && finished == 1);
    }

    private static void CameraWrites()
    {
        FakeEngine f = Setup();
        var world = new Node2D { Name = "World" };
        var cam = new Camera2D
        {
            Name = "Cam", Position = new Vector2(100, 50), Offset = new Vector2(8, -4), Zoom = new Vector2(2, 3), LimitLeft = 0, LimitTop = -50, LimitRight = 1280, LimitBottom = 720,
            PositionSmoothingEnabled = true, PositionSmoothingSpeed = 7f, DragHorizontalEnabled = true, DragLeftMargin = 0.1f, DragRightMargin = 0.3f, IgnoreRotation = false,
            AnchorMode = Camera2D.AnchorModeEnum.FixedTopLeft,
        };
        Root.AddChild(world);
        world.AddChild(cam);
        T.Frame();
        int e = EntityOf(cam);
        T.Check("the camera has an entity under its parent's", e != 0 && f.Cam.ContainsKey(e));
        var c = f.Cam[e];
        T.Check("zoom, offset and the smoothing speed", c.Zoom == new System.Numerics.Vector2(2, 3) && c.Offset == new System.Numerics.Vector2(8, -4) && c.SmoothSpeed == 7f);
        T.Check("the four limits, each a flag", c.Limit == new System.Numerics.Vector4(0, -50, 1280, 720) &&
                (c.Flags & (Camera2DFlags.LimitLeft | Camera2DFlags.LimitTop | Camera2DFlags.LimitRight | Camera2DFlags.LimitBottom)) == (Camera2DFlags.LimitLeft | Camera2DFlags.LimitTop | Camera2DFlags.LimitRight | Camera2DFlags.LimitBottom));
        T.Check("drag margins and the horizontal drag flag", c.Drag.X == 0.1f && c.Drag.Z == 0.3f && (c.Flags & Camera2DFlags.DragHorizontal) != 0 && (c.Flags & Camera2DFlags.DragVertical) == 0);
        T.Check("smoothing, follow rotation (ignore_rotation off) and the top-left anchor", (c.Flags & (Camera2DFlags.Smoothing | Camera2DFlags.FollowRotation | Camera2DFlags.AnchorTopLeft)) ==
                (Camera2DFlags.Smoothing | Camera2DFlags.FollowRotation | Camera2DFlags.AnchorTopLeft));
        T.Check("the first enabled camera is current", cam.IsCurrent());

        var plain = new Camera2D { Name = "Plain" };
        world.AddChild(plain);
        T.Frame();
        int pe = EntityOf(plain);
        var pc = f.Cam[pe];
        T.Check("a default camera writes the defaults: no limits, no smoothing, no drag, centred", pc.Flags == Camera2DFlags.None || pc.Flags == Camera2DFlags.Current, $"{pc.Flags}");
        T.Check("and its zoom is 1", pc.Zoom == System.Numerics.Vector2.One && pc.Drag.X == 0.2f);
        T.Check("it is not current while the first one is", !plain.IsCurrent() && cam.IsCurrent());

        plain.MakeCurrent();
        T.Check("make_current moves the flag at once", f.Current == pe && plain.IsCurrent() && !cam.IsCurrent());
        T.Frame();
        T.Check("and a frame keeps it so (the other camera's write does not take it back)", f.Current == pe && plain.IsCurrent() && !cam.IsCurrent());
        cam.Zoom = new Vector2(4, 4);
        T.Frame();
        T.Check("changing the old camera's zoom writes it and does not take the flag", f.Cam[e].Zoom == new System.Numerics.Vector2(4, 4) && f.Current == pe);

        cam.Enabled = false;
        T.Frame();
        T.Check("enabled = false is the disabled flag", (f.Cam[e].Flags & Camera2DFlags.Disabled) != 0);
        T.Check("the screen centre is the engine's", plain.GetScreenCenterPosition() == new Vector2(11, 22));
        T.Check("the target is the position plus the offset", T.Near(cam.GetTargetPosition().X, 108f));
        T.Check("limits by side", cam.GetLimit(Side.Right) == 1280 && cam.GetDragMargin(Side.Left) == 0.1f);
        cam.SetLimit(Side.Bottom, 600);
        cam.SetDragMargin(Side.Top, 0.4f);
        T.Frame();
        T.Check("set_limit and set_drag_margin write through", f.Cam[e].Limit.W == 600 && f.Cam[e].Drag.Y == 0.4f);

        var late = new Camera2D { Name = "Late" };
        late.MakeCurrent();
        world.AddChild(late);
        T.Frame();
        T.Check("make_current before the node is in the tree takes effect when it is", f.Current == EntityOf(late));
    }

    private static void ParallaxWrites()
    {
        FakeEngine f = Setup();
        var sky = new Parallax2D
        {
            Name = "Sky", ScrollScale = new Vector2(0.5f, 0.25f), ScrollOffset = new Vector2(3, -4), RepeatSize = new Vector2(512, 256), RepeatTimes = 3, Autoscroll = new Vector2(-20, 5),
            LimitBegin = new Vector2(-100, -50), LimitEnd = new Vector2(900, 700), FollowViewport = false,
        };
        var layer = new ParallaxLayer { Name = "Hills", MotionScale = new Vector2(0.3f, 0.6f), MotionOffset = new Vector2(4, 5), MotionMirroring = new Vector2(640, 0) };
        var back = new ParallaxBackground { Name = "Back" };
        var plain = new Parallax2D { Name = "Plain" };
        Root.AddChild(sky);
        Root.AddChild(back);
        back.AddChild(layer);
        Root.AddChild(plain);
        T.Frame();
        var p = f.Par[EntityOf(sky)];
        T.Check("scroll scale, offset, repeat size, repeat times and autoscroll", p.ScrollScale == new System.Numerics.Vector2(0.5f, 0.25f) && p.ScrollOffset == new System.Numerics.Vector2(3, -4) &&
                p.RepeatSize == new System.Numerics.Vector2(512, 256) && p.RepeatTimes == 3 && p.Autoscroll == new System.Numerics.Vector2(-20, 5));
        T.Check("the limits are flags, and follow_viewport off is no-follow", p.LimitBegin == new System.Numerics.Vector2(-100, -50) && p.LimitEnd == new System.Numerics.Vector2(900, 700) &&
                (p.Flags & (Parallax2DFlags.LimitBegin | Parallax2DFlags.LimitEnd | Parallax2DFlags.NoFollow)) == (Parallax2DFlags.LimitBegin | Parallax2DFlags.LimitEnd | Parallax2DFlags.NoFollow));
        var d = f.Par[EntityOf(plain)];
        T.Check("a default parallax scrolls with the world, follows the camera and has no limits", d.ScrollScale == System.Numerics.Vector2.One && d.Flags == Parallax2DFlags.None && d.RepeatSize == System.Numerics.Vector2.Zero);
        var l = f.Par[EntityOf(layer)];
        T.Check("a ParallaxLayer is a parallax: motion scale, offset and mirroring", l.ScrollScale == new System.Numerics.Vector2(0.3f, 0.6f) && l.ScrollOffset == new System.Numerics.Vector2(4, 5) &&
                l.RepeatSize == new System.Numerics.Vector2(640, 0));
        var bg = f.Layers[EntityOf(back)];
        T.Check("a ParallaxBackground is a canvas layer at -100 that follows the camera", bg.Index == -100 && (bg.Flags & Layer2DFlags.FollowViewport) != 0);
        sky.ScrollScale = new Vector2(0.9f, 0.9f);
        T.Frame();
        T.Check("changing the scroll scale rewrites it", f.Par[EntityOf(sky)].ScrollScale.X == 0.9f);
    }
}
