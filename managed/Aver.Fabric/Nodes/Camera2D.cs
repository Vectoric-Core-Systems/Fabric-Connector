// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Camera2D, Parallax2D and the older ParallaxBackground / ParallaxLayer on the engine's 2D camera and parallax (docs/plans/RENDER_2D.md in the engine). The engine moves the view and
// the layers; these nodes keep the Godot properties and write them to the entity's CCamera2D / CParallax2D once a frame.

using Aver.Framework;

namespace Aver.Fabric;

/// <summary>A 2D camera (Godot's Camera2D) on the engine's 2D camera: the zoom, offset, anchor, the four limits, position smoothing and the drag margins move the view of the default canvas and
/// of the layers that follow the viewport (a plain <see cref="CanvasLayer"/> stays fixed, like a HUD). One camera is current at a time: the first enabled camera, or the one
/// <see cref="MakeCurrent"/> chose last. Rotation smoothing, limit smoothing and a custom viewport are kept and not used.</summary>
public class Camera2D : Node2D
{
    public enum AnchorModeEnum { FixedTopLeft = 0, DragCenter = 1 }
    public enum Camera2DProcessCallback { Physics = 0, Idle = 1 }

    private bool _makeCurrent;

    public Vector2 Offset { get; set; }
    public AnchorModeEnum AnchorMode { get; set; } = AnchorModeEnum.DragCenter;
    /// <summary>True (the default): the view does not turn with the node.</summary>
    public bool IgnoreRotation { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public Vector2 Zoom { get; set; } = Vector2.One;
    public Camera2DProcessCallback ProcessCallback { get; set; } = Camera2DProcessCallback.Idle;
    public int LimitLeft { get; set; } = -10000000;
    public int LimitTop { get; set; } = -10000000;
    public int LimitRight { get; set; } = 10000000;
    public int LimitBottom { get; set; } = 10000000;
    public bool LimitSmoothed { get; set; }
    public bool PositionSmoothingEnabled { get; set; }
    public float PositionSmoothingSpeed { get; set; } = 5f;
    public bool RotationSmoothingEnabled { get; set; }
    public float RotationSmoothingSpeed { get; set; } = 5f;
    public bool DragHorizontalEnabled { get; set; }
    public bool DragVerticalEnabled { get; set; }
    public float DragHorizontalOffset { get; set; }
    public float DragVerticalOffset { get; set; }
    public float DragLeftMargin { get; set; } = 0.2f;
    public float DragTopMargin { get; set; } = 0.2f;
    public float DragRightMargin { get; set; } = 0.2f;
    public float DragBottomMargin { get; set; } = 0.2f;

    /// <summary>Godot 4.0 and 4.1's spelling of "this is the current camera".</summary>
    public bool Current { get => IsCurrent(); set { if (value) MakeCurrent(); } }

    public int GetLimit(Side side) => side switch { Side.Left => LimitLeft, Side.Top => LimitTop, Side.Right => LimitRight, _ => LimitBottom };

    public void SetLimit(Side side, int limit)
    {
        switch (side)
        {
            case Side.Left: LimitLeft = limit; break;
            case Side.Top: LimitTop = limit; break;
            case Side.Right: LimitRight = limit; break;
            default: LimitBottom = limit; break;
        }
    }

    public float GetDragMargin(Side side) => side switch { Side.Left => DragLeftMargin, Side.Top => DragTopMargin, Side.Right => DragRightMargin, _ => DragBottomMargin };

    public void SetDragMargin(Side side, float margin)
    {
        switch (side)
        {
            case Side.Left: DragLeftMargin = margin; break;
            case Side.Top: DragTopMargin = margin; break;
            case Side.Right: DragRightMargin = margin; break;
            default: DragBottomMargin = margin; break;
        }
    }

    /// <summary>Makes this the camera that moves the view. The engine applies it with the next frame's update; the camera it replaces stops being current.</summary>
    public void MakeCurrent()
    {
        _makeCurrent = true;
        Enabled = true;
        int e = Canvas2D.Peek(this);
        if (e != 0 && Link2D.HasCamera && Canvas2D.Backend.MakeCurrent(e)) _makeCurrent = false;
    }

    public bool IsCurrent()
    {
        int e = Canvas2D.Peek(this);
        return e != 0 && Link2D.HasCamera ? Canvas2D.Backend.CurrentCamera == e : _makeCurrent;
    }

    /// <summary>The centre of the view in the canvas, after easing and the limits (Godot's get_screen_center_position).</summary>
    public Vector2 GetScreenCenterPosition()
    {
        int e = Canvas2D.Peek(this);
        if (e != 0 && Link2D.HasCamera && Canvas2D.Backend.TryGetCameraCenter(e, out System.Numerics.Vector2 c)) return new Vector2(c.X, c.Y);
        return GetTargetPosition();
    }

    /// <summary>Where the camera wants its centre: its position in the canvas plus the offset.</summary>
    public Vector2 GetTargetPosition() => GlobalPosition + Offset;

    /// <summary>Kept for scripts. The engine updates the camera once a frame; this writes the node's state now.</summary>
    public void ForceUpdateScroll() => Push2D();

    /// <summary>Kept for scripts. A camera made current starts on its target; smoothing is not restarted otherwise.</summary>
    public void ResetSmoothing() { }

    /// <summary>Kept for scripts. The view is centred on the target each frame.</summary>
    public void Align() { }

    internal override void Push2D()
    {
        int e = Canvas2D.Backing(this);
        if (e == 0) return;
        base.Push2D();
        Camera2DFlags flags = Camera2DFlags.None;
        if (!Enabled) flags |= Camera2DFlags.Disabled;
        if (!IgnoreRotation) flags |= Camera2DFlags.FollowRotation;
        if (PositionSmoothingEnabled) flags |= Camera2DFlags.Smoothing;
        if (DragHorizontalEnabled) flags |= Camera2DFlags.DragHorizontal;
        if (DragVerticalEnabled) flags |= Camera2DFlags.DragVertical;
        if (AnchorMode == AnchorModeEnum.FixedTopLeft) flags |= Camera2DFlags.AnchorTopLeft;
        if (LimitLeft != -10000000) flags |= Camera2DFlags.LimitLeft;
        if (LimitTop != -10000000) flags |= Camera2DFlags.LimitTop;
        if (LimitRight != 10000000) flags |= Camera2DFlags.LimitRight;
        if (LimitBottom != 10000000) flags |= Camera2DFlags.LimitBottom;
        // The current flag belongs to the engine (making another camera current clears it there): keep what it says.
        if (!_makeCurrent && Canvas2D.Backend.CurrentCamera == e) flags |= Camera2DFlags.Current;
        var c = new Aver.Framework.Camera2D
        {
            Zoom = Canvas2D.N(Zoom),
            Offset = Canvas2D.N(Offset),
            Limit = new System.Numerics.Vector4(LimitLeft, LimitTop, LimitRight, LimitBottom),
            SmoothSpeed = PositionSmoothingSpeed,
            Drag = new System.Numerics.Vector4(DragLeftMargin, DragTopMargin, DragRightMargin, DragBottomMargin),
            Flags = flags,
        };
        Canvas2D.Write(e, Link2D, c);
        if (_makeCurrent && Link2D.HasCamera && Canvas2D.Backend.MakeCurrent(e)) _makeCurrent = false;
    }
}

/// <summary>Scrolling layers (Godot 4.3's Parallax2D) on the engine's parallax: the sprites below it are drawn shifted by the camera's position times (1 - <see cref="ScrollScale"/>) plus
/// <see cref="ScrollOffset"/> and <see cref="Autoscroll"/> times the time, and tiled by <see cref="RepeatSize"/>. <see cref="ScreenOffset"/> and <see cref="IgnoreCameraScroll"/> are kept
/// and not used.</summary>
public class Parallax2D : Node2D
{
    public Vector2 ScrollScale { get; set; } = Vector2.One;
    public Vector2 ScrollOffset { get; set; }
    public Vector2 RepeatSize { get; set; }
    public int RepeatTimes { get; set; } = 1;
    public Vector2 Autoscroll { get; set; }
    public Vector2 ScreenOffset { get; set; }
    /// <summary>With a limit end above the begin, the camera position the scroll uses stays between them. Both zero (the default) is no limit.</summary>
    public Vector2 LimitBegin { get; set; }
    public Vector2 LimitEnd { get; set; }
    public bool FollowViewport { get; set; } = true;
    public bool IgnoreCameraScroll { get; set; }

    internal override void Push2D()
    {
        int e = Canvas2D.Backing(this);
        if (e == 0) return;
        base.Push2D();
        Parallax2DFlags flags = Parallax2DFlags.None;
        if (!FollowViewport) flags |= Parallax2DFlags.NoFollow;
        if (LimitBegin != Vector2.Zero) flags |= Parallax2DFlags.LimitBegin;
        if (LimitEnd != Vector2.Zero) flags |= Parallax2DFlags.LimitEnd;
        Canvas2D.Write(e, Link2D, new Aver.Framework.Parallax2D
        {
            ScrollScale = Canvas2D.N(ScrollScale),
            ScrollOffset = Canvas2D.N(ScrollOffset),
            RepeatSize = new System.Numerics.Vector2(Math.Max(0f, RepeatSize.X), Math.Max(0f, RepeatSize.Y)),
            RepeatTimes = Math.Max(0, RepeatTimes),
            Autoscroll = Canvas2D.N(Autoscroll),
            LimitBegin = LimitBegin == Vector2.Zero ? new System.Numerics.Vector2(-1e9f) : Canvas2D.N(LimitBegin),
            LimitEnd = LimitEnd == Vector2.Zero ? new System.Numerics.Vector2(1e9f) : Canvas2D.N(LimitEnd),
            Flags = flags,
        });
    }
}

/// <summary>The older scrolling background (Godot's ParallaxBackground), approximated: a canvas layer (the layer number -100 unless set) that follows the 2D camera, whose
/// <see cref="ParallaxLayer"/> children scroll by their motion scale. The scroll offset, base offset and scale, the scroll limits and ignoring the camera's zoom are kept and not used.</summary>
public class ParallaxBackground : CanvasLayer
{
    public ParallaxBackground()
    {
        Layer = -100;
        FollowViewportEnabled = true;
    }

    public Vector2 ScrollOffset { get; set; }
    public Vector2 ScrollBaseOffset { get; set; }
    public Vector2 ScrollBaseScale { get; set; } = Vector2.One;
    public bool ScrollIgnoreCameraZoom { get; set; }
    public Vector2 ScrollLimitBegin { get; set; }
    public Vector2 ScrollLimitEnd { get; set; }
}

/// <summary>A layer of a <see cref="ParallaxBackground"/> (Godot's ParallaxLayer), approximated by a parallax: <see cref="MotionScale"/> is the scroll scale, <see cref="MotionOffset"/> the scroll
/// offset and <see cref="MotionMirroring"/> the repeat size.</summary>
public class ParallaxLayer : Node2D
{
    public Vector2 MotionScale { get; set; } = Vector2.One;
    public Vector2 MotionOffset { get; set; }
    public Vector2 MotionMirroring { get; set; }

    internal override void Push2D()
    {
        int e = Canvas2D.Backing(this);
        if (e == 0) return;
        base.Push2D();
        Canvas2D.Write(e, Link2D, new Aver.Framework.Parallax2D
        {
            ScrollScale = Canvas2D.N(MotionScale),
            ScrollOffset = Canvas2D.N(MotionOffset),
            RepeatSize = new System.Numerics.Vector2(Math.Max(0f, MotionMirroring.X), Math.Max(0f, MotionMirroring.Y)),
            RepeatTimes = 1,
            LimitBegin = new System.Numerics.Vector2(-1e9f),
            LimitEnd = new System.Numerics.Vector2(1e9f),
        });
    }
}
