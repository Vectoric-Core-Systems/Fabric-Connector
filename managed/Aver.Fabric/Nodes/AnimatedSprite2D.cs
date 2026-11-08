// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Godot's AnimatedSprite2D on the engine's sprite animation (docs/plans/RENDER_2D.md in the engine). The engine plays the animation: this node uploads the SpriteFrames as the
// entity's CSpriteAnim2D clips and forwards play(), pause(), stop() and frame to it; the engine's events come back as Godot's signals.

using Aver.Framework;
using Aver.Scripting;

namespace Aver.Fabric;

/// <summary>A 2D sprite that plays a <see cref="SpriteFrames"/> animation (Godot's AnimatedSprite2D), drawn by the engine's 2D canvas in one order with the other sprites and the
/// Controls. The engine runs the frame clock (<see cref="SpeedScale"/>, the animation's speed, a frame's duration, looping, backwards play) and this node mirrors it:
/// <see cref="Play"/>, <see cref="PlayBackwards"/>, <see cref="Pause"/>, <see cref="Stop"/>, <see cref="Frame"/> and <see cref="FrameProgress"/> act on the engine's animation, and its
/// <c>frame_changed</c>, <c>animation_changed</c>, <c>animation_looped</c> and <c>animation_finished</c> events are raised as the signals, once a frame (a signal for something a script
/// just did arrives on the next frame, not inside the call). Frame textures must be files in the project, the engine holds 16 animations and 128 frames per sprite, and the flips,
/// offset, centering, nearest filtering and modulate apply as for a <see cref="Sprite2D"/>.</summary>
public class AnimatedSprite2D : Node2D
{
    public new class SignalName : Node2D.SignalName
    {
        public static readonly StringName SpriteFramesChanged = "sprite_frames_changed";
        public static readonly StringName AnimationChanged = "animation_changed";
        public static readonly StringName FrameChanged = "frame_changed";
        public static readonly StringName AnimationLooped = "animation_looped";
        public static readonly StringName AnimationFinished = "animation_finished";
    }

    public event Action SpriteFramesChanged { add => Connect(SignalName.SpriteFramesChanged, Callable.From(value)); remove => Disconnect(SignalName.SpriteFramesChanged, Callable.From(value)); }
    public event Action AnimationChanged { add => Connect(SignalName.AnimationChanged, Callable.From(value)); remove => Disconnect(SignalName.AnimationChanged, Callable.From(value)); }
    public event Action FrameChanged { add => Connect(SignalName.FrameChanged, Callable.From(value)); remove => Disconnect(SignalName.FrameChanged, Callable.From(value)); }
    public event Action AnimationLooped { add => Connect(SignalName.AnimationLooped, Callable.From(value)); remove => Disconnect(SignalName.AnimationLooped, Callable.From(value)); }
    public event Action AnimationFinished { add => Connect(SignalName.AnimationFinished, Callable.From(value)); remove => Disconnect(SignalName.AnimationFinished, Callable.From(value)); }

    private SpriteFrames? _frames;
    private StringName _animation = "default";
    private float _speedScale = 1f;
    private float _customSpeed = 1f;
    private bool _playing;
    private int _frame;
    private float _progress;
    private int _uploaded;      // the entity the clips were uploaded to; 0 = none
    private bool _dirty = true; // the clips need uploading again

    public StringName Autoplay { get; set; } = "";
    public bool Centered { get; set; } = true;
    public Vector2 Offset { get; set; }
    public bool FlipH { get; set; }
    public bool FlipV { get; set; }

    public SpriteFrames? SpriteFrames
    {
        get => _frames;
        set
        {
            if (_frames != null) _frames.Changed -= OnFramesChanged;
            _frames = value;
            if (_frames != null) _frames.Changed += OnFramesChanged;
            if (_frames != null && !_frames.HasAnimation(_animation))
            {
                string[] names = _frames.GetAnimationNames();
                _animation = names.Length > 0 ? names[0] : "default";
            }
            _dirty = true;
            ClampFrame();
            EmitSignal(SignalName.SpriteFramesChanged);
        }
    }

    private void OnFramesChanged()
    {
        _dirty = true;
        ClampFrame();
        EmitSignal(SignalName.SpriteFramesChanged);
    }

    private void ClampFrame()
    {
        int n = _frames?.GetFrameCount(_animation) ?? 0;
        if (_frame >= n) _frame = Math.Max(n - 1, 0);
    }

    // ---- the engine side ----

    private static ICanvas2DBackend Backend2D => Canvas2D.Backend;

    /// <summary>The engine speed: the scale times the speed given to play(); 0 stands for "not moving", which the engine would read as 1.</summary>
    private float EngineSpeed()
    {
        float s = _speedScale * _customSpeed;
        return s == 0f ? 1e-9f : s;
    }

    /// <summary>The entity holding the clips, uploaded and current; 0 when the node is not in the tree or the engine has no 2D library.</summary>
    private int Ready()
    {
        if (!IsInsideTree() || !Backend2D.Available) return 0;
        int e = Canvas2D.Backing(this);
        if (e == 0) return 0;
        if (_uploaded != e || _dirty) Upload(e);
        return _uploaded == e ? e : 0;
    }

    private void Upload(int e)
    {
        Backend2D.RemoveAnimation(e);
        if (_frames != null)
        {
            int clip = 0, used = 0;
            foreach (string name in _frames.GetAnimationNames())
            {
                if (clip >= Scene2D.MaxClips) { Log.Warn($"[Fabric] {Name}: only the first {Scene2D.MaxClips} animations of the SpriteFrames reach the engine"); break; }
                int n = _frames.GetFrameCount(name);
                var frames = new SpriteAnimFrame2D[Math.Min(n, Scene2D.MaxClipFrames - used)];
                for (int i = 0; i < frames.Length; i++)
                    frames[i] = new SpriteAnimFrame2D { Texture = Canvas2D.AssetIdOf(_frames.GetFrameTexture(name, i)), Weight = _frames.GetFrameDuration(name, i) };
                if (frames.Length < n) Log.Warn($"[Fabric] {Name}: animation '{name}' keeps {frames.Length} of its {n} frames (the engine holds {Scene2D.MaxClipFrames} per sprite)");
                if (Backend2D.SetClip(e, clip, name, (float)_frames.GetAnimationSpeed(name), _frames.GetAnimationLoop(name), frames))
                {
                    clip++;
                    used += frames.Length;
                }
            }
        }
        Backend2D.SetAnimation(e, new SpriteAnim2D { SpeedScale = EngineSpeed() });
        _uploaded = e;
        _dirty = false;
        Canvas2D.Listen(e, this);
        // The engine starts with no clip playing: put it where the node says it is.
        if (_frames != null && _frames.HasAnimation(_animation) && _frames.GetFrameCount(_animation) > 0)
        {
            Backend2D.Play(e, _animation.ToString(), true);
            Backend2D.SetFrame(e, _frame, _progress);
            if (!_playing) Backend2D.Pause(e);
        }
    }

    private bool TryRead(out SpriteAnim2D a)
    {
        a = default;
        return _uploaded != 0 && !_dirty && Backend2D.TryGetAnimation(_uploaded, out a);
    }

    internal override void OnEnteringTree()
    {
        base.OnEnteringTree();
        _uploaded = 0;
        _dirty = true;
    }

    internal override void OnReadying()
    {
        base.OnReadying();
        if (Autoplay.ToString().Length > 0) Play(Autoplay);
    }

    internal void OnEngineEvent(SpriteAnimEvent2D ev)
    {
        switch (ev.Kind)
        {
            case SpriteAnimEventKind.FrameChanged: EmitSignal(SignalName.FrameChanged); break;
            case SpriteAnimEventKind.AnimationChanged: EmitSignal(SignalName.AnimationChanged); break;
            case SpriteAnimEventKind.Looped: EmitSignal(SignalName.AnimationLooped); break;
            case SpriteAnimEventKind.Finished:
                _playing = false;
                EmitSignal(SignalName.AnimationFinished);
                break;
        }
    }

    // ---- Godot's API ----

    /// <summary>The animation shown. A different one starts at its first frame (its last when playing backwards); whether it plays is unchanged.</summary>
    public StringName Animation
    {
        get => _animation;
        set
        {
            if (_animation == value) return;
            bool wasPlaying = IsPlaying();
            _animation = value;
            _frame = 0;
            _progress = 0f;
            int e = Ready();
            if (e == 0) return;
            if (Backend2D.Play(e, value.ToString(), false) && !wasPlaying) Backend2D.Pause(e);
        }
    }

    public int Frame
    {
        get => TryRead(out SpriteAnim2D a) ? Math.Max(a.Frame, 0) : _frame;
        set => SetFrameAndProgress(value, 0f);
    }

    /// <summary>How far through the current frame, 0 to 1.</summary>
    public float FrameProgress
    {
        get => TryRead(out SpriteAnim2D a) ? a.Progress : _progress;
        set => SetFrameAndProgress(Frame, value);
    }

    public float SpeedScale
    {
        get => _speedScale;
        set
        {
            _speedScale = value;
            int e = Ready();
            if (e != 0) Backend2D.SetAnimation(e, new SpriteAnim2D { SpeedScale = EngineSpeed() });
        }
    }

    public void SetFrameAndProgress(int frame, float progress)
    {
        int n = _frames?.GetFrameCount(_animation) ?? 0;
        _frame = n == 0 ? 0 : Math.Clamp(frame, 0, n - 1);
        _progress = Math.Clamp(progress, 0f, 1f);
        int e = Ready();
        if (e != 0) Backend2D.SetFrame(e, _frame, _progress);
    }

    public bool IsPlaying() => TryRead(out SpriteAnim2D a) ? a.Playing : _playing;

    public bool Playing
    {
        get => IsPlaying();
        set { if (value) Play(); else Pause(); }
    }

    /// <summary>The speed in frames a second the animation runs at now: its speed times <see cref="SpeedScale"/> and the speed given to <see cref="Play"/>; negative when playing backwards.</summary>
    public float GetPlayingSpeed() => IsPlaying() ? (float)((_frames?.GetAnimationSpeed(_animation) ?? 0.0) * _speedScale * _customSpeed) : 0f;

    /// <summary>Plays an animation (the current one when no name is given). A different name starts at its first frame; one that finished starts over; playing backwards from the end starts at the last.</summary>
    public void Play(StringName? name = null, float customSpeed = 1f, bool fromEnd = false)
    {
        bool named = name != null && name.ToString().Length > 0;
        if (named)
        {
            if (_frames == null || !_frames.HasAnimation(name!)) { Log.Error($"[Fabric] AnimatedSprite2D has no animation '{name}'"); return; }
        }
        else if (_frames == null || !_frames.HasAnimation(_animation)) { Log.Error($"[Fabric] AnimatedSprite2D has no animation '{_animation}'"); return; }
        _customSpeed = customSpeed;
        bool changed = named && _animation != name!;
        int e = Ready();
        if (e != 0)
        {
            Backend2D.SetAnimation(e, new SpriteAnim2D { SpeedScale = EngineSpeed() });
            bool atStart = TryRead(out SpriteAnim2D before) && before.Frame == 0 && before.Progress <= 0f;
            if (changed) _animation = name!;
            Backend2D.Play(e, _animation.ToString(), false);
            int n = _frames.GetFrameCount(_animation);
            if (!changed && fromEnd && EngineSpeed() < 0f && atStart && n > 0) Backend2D.SetFrame(e, n - 1, 1f);
        }
        else if (changed) { _animation = name!; _frame = EngineSpeed() < 0f ? Math.Max(0, _frames.GetFrameCount(_animation) - 1) : 0; _progress = 0f; }
        _playing = _frames.GetFrameCount(_animation) > 0;
    }

    public void PlayBackwards(StringName? name = null) => Play(name, -1f, true);

    public void Pause()
    {
        _playing = false;
        int e = Ready();
        if (e != 0) Backend2D.Pause(e);
    }

    /// <summary>Stops and returns to the first frame.</summary>
    public void Stop()
    {
        _playing = false;
        _customSpeed = 1f;
        _frame = 0;
        _progress = 0f;
        int e = Ready();
        if (e == 0) return;
        Backend2D.SetAnimation(e, new SpriteAnim2D { SpeedScale = EngineSpeed() });
        Backend2D.Stop(e);
    }

    // ---- drawing ----

    internal override void Push2D()
    {
        int e = Canvas2D.Backing(this);
        if (e == 0) return;
        base.Push2D();
        if (_uploaded != e || _dirty) Upload(e);

        var flags = Sprite2DFlags.None;
        if (FlipH) flags |= Sprite2DFlags.FlipH;
        if (FlipV) flags |= Sprite2DFlags.FlipV;
        if (!Centered) flags |= Sprite2DFlags.NotCentered;
        if (ResolvedFilter() == UiFilterId.Nearest) flags |= Sprite2DFlags.Nearest;
        uint packed = Canvas2D.Pack(GetCombinedModulate());
        if (!IsVisibleInTree() || (packed >> 24) == 0)
        {
            flags |= Sprite2DFlags.Hidden;
            if (packed == 0) packed = 0xFFFFFFFFu;
        }
        // The texture, region and cell belong to the animation: the engine writes them from the current frame.
        var s = new Aver.Framework.Sprite2D
        {
            Offset = Canvas2D.N(Offset),
            Modulate = packed,
            Flags = flags,
            HFrames = 1,
            VFrames = 1,
            Blend = ResolvedBlend() switch { UiBlendId.Add => Sprite2DBlend.Add, UiBlendId.Sub => Sprite2DBlend.Sub, UiBlendId.Mul => Sprite2DBlend.Mul, _ => Sprite2DBlend.Mix },
        };
        Canvas2D.Write(e, Link2D, s);
        if (TryRead(out SpriteAnim2D a))
        {
            _frame = Math.Max(a.Frame, 0);
            _progress = a.Progress;
            _playing = a.Playing;
        }
    }
}
