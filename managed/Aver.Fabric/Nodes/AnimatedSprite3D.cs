// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Godot's SpriteFrames and AnimatedSprite3D. The frame clock, the animations and the signals work as in Godot; nothing is drawn (see AnimatedSprite3D).

using Aver.Scripting;

namespace Aver.Fabric;

/// <summary>Named animations made of textures with a speed in frames a second and a loop flag (Godot's SpriteFrames). A new one has the animation "default" at 5 fps, looping.</summary>
public class SpriteFrames : Resource
{
    private sealed class Anim
    {
        public double Speed = 5.0;
        public bool Loop = true;
        public readonly List<(Texture2D? Texture, float Duration)> Frames = new();
    }

    private readonly Dictionary<string, Anim> _anims = new(StringComparer.Ordinal);
    private readonly List<string> _order = new();

    public SpriteFrames() => AddAnimation("default");

    private Anim? Find(StringName anim) => _anims.TryGetValue(anim.ToString(), out Anim? a) ? a : null;

    public void AddAnimation(StringName anim)
    {
        string name = anim.ToString();
        if (_anims.ContainsKey(name)) { Log.Error($"[Fabric] SpriteFrames already has the animation '{name}'"); return; }
        _anims[name] = new Anim();
        _order.Add(name);
        EmitChanged();
    }

    public bool HasAnimation(StringName anim) => _anims.ContainsKey(anim.ToString());

    public void RemoveAnimation(StringName anim)
    {
        string name = anim.ToString();
        if (_anims.Remove(name)) { _order.Remove(name); EmitChanged(); }
    }

    public void RenameAnimation(StringName anim, StringName newName)
    {
        string from = anim.ToString(), to = newName.ToString();
        if (!_anims.TryGetValue(from, out Anim? a) || _anims.ContainsKey(to)) return;
        _anims.Remove(from);
        _anims[to] = a;
        _order[_order.IndexOf(from)] = to;
        EmitChanged();
    }

    /// <summary>The names of the animations, in the order they were added.</summary>
    public string[] GetAnimationNames() => _order.ToArray();

    public void SetAnimationSpeed(StringName anim, double fps)
    {
        if (Find(anim) is { } a) { a.Speed = Math.Max(fps, 0.0); EmitChanged(); }
    }

    public double GetAnimationSpeed(StringName anim) => Find(anim)?.Speed ?? 0.0;

    public void SetAnimationLoop(StringName anim, bool loop)
    {
        if (Find(anim) is { } a) { a.Loop = loop; EmitChanged(); }
    }

    public bool GetAnimationLoop(StringName anim) => Find(anim)?.Loop ?? false;

    public int GetFrameCount(StringName anim) => Find(anim)?.Frames.Count ?? 0;

    /// <summary>Adds a frame; <paramref name="duration"/> is relative to the animation's speed (1 is one tick of it). A negative position appends.</summary>
    public void AddFrame(StringName anim, Texture2D? texture, float duration = 1f, int atPosition = -1)
    {
        if (Find(anim) is not { } a) return;
        if (atPosition < 0 || atPosition >= a.Frames.Count) a.Frames.Add((texture, duration));
        else a.Frames.Insert(atPosition, (texture, duration));
        EmitChanged();
    }

    public void SetFrame(StringName anim, int idx, Texture2D? texture, float duration = 1f)
    {
        if (Find(anim) is not { } a || idx < 0 || idx >= a.Frames.Count) return;
        a.Frames[idx] = (texture, duration);
        EmitChanged();
    }

    public void RemoveFrame(StringName anim, int idx)
    {
        if (Find(anim) is not { } a || idx < 0 || idx >= a.Frames.Count) return;
        a.Frames.RemoveAt(idx);
        EmitChanged();
    }

    public Texture2D? GetFrameTexture(StringName anim, int idx) =>
        Find(anim) is { } a && idx >= 0 && idx < a.Frames.Count ? a.Frames[idx].Texture : null;

    public float GetFrameDuration(StringName anim, int idx) =>
        Find(anim) is { } a && idx >= 0 && idx < a.Frames.Count ? a.Frames[idx].Duration : 1f;

    /// <summary>Removes every frame of an animation.</summary>
    public void Clear(StringName anim)
    {
        if (Find(anim) is { } a) { a.Frames.Clear(); EmitChanged(); }
    }

    /// <summary>Removes every animation and every frame.</summary>
    public void ClearAll()
    {
        _anims.Clear();
        _order.Clear();
        EmitChanged();
    }
}

/// <summary>A sprite in the world that plays a <see cref="SpriteFrames"/> animation (Godot's AnimatedSprite3D). The animation state is Godot's: <see cref="Play"/>, <see cref="Stop"/>,
/// <see cref="Frame"/>, <see cref="FrameProgress"/>, <see cref="SpeedScale"/>, looping, backwards play, and the <c>animation_finished</c>, <c>animation_looped</c>, <c>frame_changed</c>,
/// <c>animation_changed</c> and <c>sprite_frames_changed</c> signals, driven by the scaled game time. Approximated: the picture is not drawn. A textured quad is baked at convert
/// time and a texture assigned at run time does not reach the engine's material, so there is nothing to show the current frame with.</summary>
public class AnimatedSprite3D : SpriteBase3D
{
    public new class SignalName : SpriteBase3D.SignalName
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
    private int _frame;
    private float _progress;
    private float _speedScale = 1f;
    private float _customSpeed = 1f;
    private bool _playing;

    public StringName Autoplay { get; set; } = "";

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
            ClampFrame();
            EmitSignal(SignalName.SpriteFramesChanged);
        }
    }

    private void OnFramesChanged()
    {
        ClampFrame();
        EmitSignal(SignalName.SpriteFramesChanged);
    }

    private void ClampFrame()
    {
        int n = _frames?.GetFrameCount(_animation) ?? 0;
        if (_frame >= n) _frame = Math.Max(n - 1, 0);
    }

    /// <summary>The animation shown. A different one starts at its first frame.</summary>
    public StringName Animation
    {
        get => _animation;
        set
        {
            if (_animation == value) return;
            _animation = value;
            _frame = 0;
            _progress = 0f;
            EmitSignal(SignalName.AnimationChanged);
            EmitSignal(SignalName.FrameChanged);
        }
    }

    public int Frame
    {
        get => _frame;
        set => SetFrameAndProgress(value, 0f);
    }

    /// <summary>How far through the current frame, 0 to 1.</summary>
    public float FrameProgress
    {
        get => _progress;
        set => _progress = Math.Clamp(value, 0f, 1f);
    }

    public float SpeedScale { get => _speedScale; set => _speedScale = value; }

    public void SetFrameAndProgress(int frame, float progress)
    {
        int n = _frames?.GetFrameCount(_animation) ?? 0;
        int f = n == 0 ? 0 : Math.Clamp(frame, 0, n - 1);
        bool changed = f != _frame;
        _frame = f;
        _progress = Math.Clamp(progress, 0f, 1f);
        if (changed) EmitSignal(SignalName.FrameChanged);
    }

    public bool IsPlaying() => _playing;

    /// <summary>The speed in frames a second the animation runs at now: its speed times <see cref="SpeedScale"/> and the speed given to <see cref="Play"/>; negative when playing backwards.</summary>
    public float GetPlayingSpeed() => _playing ? (float)((_frames?.GetAnimationSpeed(_animation) ?? 0.0) * _speedScale * _customSpeed) : 0f;

    /// <summary>Plays an animation (the current one when no name is given). A different name starts at its first frame; playing backwards from the end starts at the last.</summary>
    public void Play(StringName? name = null, float customSpeed = 1f, bool fromEnd = false)
    {
        if (name != null && name.ToString().Length > 0)
        {
            if (_frames == null || !_frames.HasAnimation(name)) { Log.Error($"[Fabric] AnimatedSprite3D has no animation '{name}'"); return; }
            if (_animation != name) Animation = name;
        }
        int n = _frames?.GetFrameCount(_animation) ?? 0;
        _customSpeed = customSpeed;
        bool backwards = GetSignedSpeed() < 0f;
        if (fromEnd && n > 0) SetFrameAndProgress(n - 1, 1f);
        else if (backwards && fromEnd == false && n > 0 && _frame == 0 && _progress == 0f) SetFrameAndProgress(n - 1, 1f);
        _playing = n > 0;
    }

    public void PlayBackwards(StringName? name = null) => Play(name, -1f, true);

    public void Pause() => _playing = false;

    /// <summary>Stops and returns to the first frame.</summary>
    public void Stop()
    {
        _playing = false;
        SetFrameAndProgress(0, 0f);
        _customSpeed = 1f;
    }

    private float GetSignedSpeed() => _speedScale * _customSpeed;

    internal override void OnReadying()
    {
        base.OnReadying();
        if (Autoplay.ToString().Length > 0) Play(Autoplay);
    }

    internal override bool UsesInternalProcess => true;

    internal override void InternalProcess(double delta)
    {
        if (!_playing || _frames == null || !CanProcess()) return;
        Advance(delta);
    }

    // A frame lasts duration / speed seconds, so the clock is the fraction of the current frame, moved on by delta * speed / duration.
    private void Advance(double delta)
    {
        double fps = _frames!.GetAnimationSpeed(_animation);
        float signed = GetSignedSpeed();
        int count = _frames.GetFrameCount(_animation);
        if (count == 0 || fps <= 0.0 || signed == 0f) return;
        double speed = fps * Math.Abs(signed);
        bool backwards = signed < 0f;
        double remaining = delta;
        int guard = 0;
        while (remaining > 0 && _playing && guard++ < 10000)
        {
            double duration = Math.Max(_frames.GetFrameDuration(_animation, _frame), 0.0001f);
            double perSecond = speed / duration;
            // The part of the current frame still to run in the direction of play.
            double left = backwards ? _progress : 1.0 - _progress;
            double needed = left / perSecond;
            if (remaining < needed)
            {
                _progress += (float)((backwards ? -1 : 1) * remaining * perSecond);
                _progress = Math.Clamp(_progress, 0f, 1f);
                return;
            }
            remaining -= needed;
            int next = _frame + (backwards ? -1 : 1);
            if (next >= 0 && next < count)
            {
                _frame = next;
                _progress = backwards ? 1f : 0f;
                EmitSignal(SignalName.FrameChanged);
                continue;
            }
            if (_frames.GetAnimationLoop(_animation))
            {
                _frame = backwards ? count - 1 : 0;
                _progress = backwards ? 1f : 0f;
                EmitSignal(SignalName.FrameChanged);
                EmitSignal(SignalName.AnimationLooped);
                continue;
            }
            _progress = backwards ? 0f : 1f;
            _playing = false;
            EmitSignal(SignalName.AnimationFinished);
            return;
        }
    }
}
