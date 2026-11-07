// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// The 2D family. Node2D and Sprite2D are real: they keep their state on the engine's 2D components, which the engine's canvas draws (docs/plans/RENDER_2D.md in the engine).
// The rest of the family exists so that a converted script which names a 2D type (a field, a cast, an `is` check) compiles; those nodes keep their properties and
// do nothing else (no body is simulated), and the converter reports a script's use of one as unsupported. Path2D and PathFollow2D are the exception: 3D games use a
// Curve2D as data (recoil and spray patterns), so its points, baking and sampling are real.

using Aver.Framework;

namespace Aver.Fabric;

/// <summary>A node with a 2D transform (Godot's Node2D). The position, rotation, scale, z_index, z_as_relative, top_level and visible flag are kept on the engine's 2D
/// transform component, which composes them down the tree; a script that changes one is seen by the next frame. A node that is not a sprite draws nothing by itself.</summary>
public class Node2D : CanvasItem, ICanvas2DNode
{
    private Vector2 _position, _scale = Vector2.One;
    private float _rotation;
    private readonly Canvas2DLink _link = new();

    public Vector2 Position { get => _position; set => _position = value; }
    public float Rotation { get => _rotation; set => _rotation = value; }
    public float RotationDegrees { get => _rotation * (180f / Mathf.Pi); set => _rotation = value * (Mathf.Pi / 180f); }
    public Vector2 Scale { get => _scale; set => _scale = value; }
    /// <summary>Godot's skew: kept, and not drawn (the engine's 2D transform has none).</summary>
    public float Skew { get; set; }

    /// <summary>The position, rotation and scale as one transform, relative to the parent.</summary>
    public Transform2D Transform
    {
        get => new(_rotation, _scale, Skew, _position);
        set { _position = value.Origin; _rotation = value.GetRotation(); _scale = value.GetScale(); Skew = value.GetSkew(); }
    }

    /// <summary>The transform in the canvas: the parents' composed with this one, stopping at a top-level node.</summary>
    public Transform2D GlobalTransform
    {
        get
        {
            Transform2D t = Transform;
            if (TopLevel) return t;
            return GetParent() is Node2D p ? p.GlobalTransform * t : t;
        }
        set
        {
            Transform2D parent = !TopLevel && GetParent() is Node2D p ? p.GlobalTransform : Transform2D.Identity;
            Transform = parent.AffineInverse() * value;
        }
    }

    public Vector2 GlobalPosition { get => GlobalTransform.Origin; set { Transform2D t = GlobalTransform; t.Origin = value; GlobalTransform = t; } }
    public float GlobalRotation { get => GlobalTransform.GetRotation(); set => GlobalTransform = new Transform2D(value, GlobalTransform.GetScale(), 0f, GlobalPosition); }
    public float GlobalRotationDegrees { get => GlobalRotation * (180f / Mathf.Pi); set => GlobalRotation = value * (Mathf.Pi / 180f); }
    public Vector2 GlobalScale { get => GlobalTransform.GetScale(); set => GlobalTransform = new Transform2D(GlobalRotation, value, 0f, GlobalPosition); }

    /// <summary>Moves by an offset in the node's own axes.</summary>
    public void Translate(Vector2 offset) => _position += Transform.BasisXform(offset);
    public void GlobalTranslate(Vector2 offset) => GlobalPosition += offset;
    public void Rotate(float radians) => _rotation += radians;
    public void ApplyScale(Vector2 ratio) => _scale *= ratio;
    public Vector2 ToLocal(Vector2 globalPoint) => GlobalTransform.AffineInverse() * globalPoint;
    public Vector2 ToGlobal(Vector2 localPoint) => GlobalTransform * localPoint;

    internal override Transform2D GlobalCanvasTransform => GlobalTransform;

    Node ICanvas2DNode.Node => this;
    Canvas2DLink ICanvas2DNode.Link => _link;
    internal Canvas2DLink Link2D => _link;

    internal override void OnEnteringTree()
    {
        base.OnEnteringTree();
        Canvas2D.Enter(this);
    }

    internal override void OnExitingTree()
    {
        Canvas2D.Exit(this);
        base.OnExitingTree();
    }

    void ICanvas2DNode.Push2D() => Push2D();

    /// <summary>Writes this node's state to its entity if it changed.</summary>
    internal virtual void Push2D()
    {
        int e = Canvas2D.Backing(this);
        if (e != 0) PushTransform(e);
    }

    private void PushTransform(int e)
    {
        var flags = Transform2DFlags.None;
        if (!ZAsRelative) flags |= Transform2DFlags.ZAbsolute;
        if (TopLevel) flags |= Transform2DFlags.TopLevel;
        if (!Visible) flags |= Transform2DFlags.Hidden;
        var t = new Aver.Framework.Transform2D
        {
            Position = Canvas2D.N(_position),
            Rotation = _rotation,
            Scale = Canvas2D.NScale(_scale),
            ZIndex = Math.Clamp(ZIndex, -4096, 4096),
            Flags = flags,
            TreeOrder = (uint)Math.Max(0, _link.Order),
        };
        Canvas2D.Write(e, _link, t);
    }
}

/// <summary>A node holding a curve in 2D (Godot's Path2D). The curve is data a script can read (spray patterns, waypoints); nothing is drawn.</summary>
public class Path2D : Node2D
{
    public Curve2D? Curve { get; set; }
}

/// <summary>Rides the curve of the Path2D it is a child of (Godot's PathFollow2D): <see cref="Node2D.Position"/> and <see cref="Node2D.Rotation"/> follow
/// <see cref="Progress"/> along the baked curve. Nothing is drawn.</summary>
public class PathFollow2D : Node2D
{
    private float _progress;

    private Curve2D? PathCurve => (GetParent() as Path2D)?.Curve;

    public float Progress
    {
        get => _progress;
        set
        {
            if (!float.IsFinite(value)) return;
            _progress = value;
            if (PathCurve is not { } c) return;
            float length = c.GetBakedLength();
            _progress = Loop && length > 0 ? Mathf.PosMod(value, length) : Math.Clamp(value, 0f, length);
            // As Godot: rotating, the offsets are in the path's frame; not rotating, they are added as they are.
            Transform2D t = c.SampleBakedWithRotation(_progress, CubicInterp);
            if (Rotates) { Position = t.Origin + t.X * HOffset + t.Y * VOffset; Rotation = t.GetRotation(); }
            else Position = t.Origin + new Vector2(HOffset, VOffset);
        }
    }

    public float ProgressRatio
    {
        get => PathCurve is { } c && c.GetBakedLength() > 0 ? _progress / c.GetBakedLength() : 0f;
        set { if (PathCurve is { } c) Progress = value * c.GetBakedLength(); }
    }

    public float HOffset { get; set; }
    public float VOffset { get; set; }
    public bool Rotates { get; set; } = true;
    public bool CubicInterp { get; set; } = true;
    public bool Loop { get; set; } = true;
}

/// <summary>What a 2D body or area derives from (Godot's CollisionObject2D). Unsupported: the layers are kept, no collision happens.</summary>
public class CollisionObject2D : Node2D
{
    public uint CollisionLayer { get; set; } = 1;
    public uint CollisionMask { get; set; } = 1;
}

/// <summary>The base of the 2D bodies (Godot's PhysicsBody2D). Unsupported.</summary>
public class PhysicsBody2D : CollisionObject2D
{
}

/// <summary>A body that does not move (Godot's StaticBody2D). Unsupported: kept so a script can name the type; it collides with nothing.</summary>
public class StaticBody2D : PhysicsBody2D
{
    public Vector2 ConstantLinearVelocity { get; set; }
    public float ConstantAngularVelocity { get; set; }
}

/// <summary>A 2D image node (Godot's Sprite2D), drawn by the engine's 2D canvas in one draw order with the Controls: canvas layer, then z, then tree order. The texture
/// must be a file in the project (a generated or atlas texture draws nothing); the region, frames, flips, offset, centering, nearest filtering, modulate and self_modulate
/// apply, and the modulate of every ancestor multiplies in.</summary>
public class Sprite2D : Node2D
{
    public new class SignalName : Node2D.SignalName
    {
        public static readonly StringName FrameChanged = "frame_changed";
        public static readonly StringName TextureChanged = "texture_changed";
    }

    private Texture2D? _texture;
    private int _frame;

    public Texture2D? Texture { get => _texture; set { if (ReferenceEquals(_texture, value)) return; _texture = value; EmitSignal(SignalName.TextureChanged); } }
    public bool Centered { get; set; } = true;
    public Vector2 Offset { get; set; }
    public bool FlipH { get; set; }
    public bool FlipV { get; set; }
    public int Hframes { get; set; } = 1;
    public int Vframes { get; set; } = 1;
    public int Frame
    {
        get => _frame;
        set { int f = Math.Clamp(value, 0, Math.Max(0, Hframes * Vframes - 1)); if (f == _frame) return; _frame = f; EmitSignal(SignalName.FrameChanged); }
    }
    public Vector2I FrameCoords { get => new(Frame % Math.Max(1, Hframes), Frame / Math.Max(1, Hframes)); set => Frame = value.Y * Hframes + value.X; }
    public bool RegionEnabled { get; set; }
    public Rect2 RegionRect { get; set; }
    public bool RegionFilterClipEnabled { get; set; }

    internal override void Push2D()
    {
        int e = Canvas2D.Backing(this);
        if (e == 0) return;
        base.Push2D();

        var flags = Sprite2DFlags.None;
        if (FlipH) flags |= Sprite2DFlags.FlipH;
        if (FlipV) flags |= Sprite2DFlags.FlipV;
        if (!Centered) flags |= Sprite2DFlags.NotCentered;
        if (ResolvedFilter() == UiFilterId.Nearest) flags |= Sprite2DFlags.Nearest;
        uint packed = Canvas2D.Pack(GetCombinedModulate());
        if (!IsVisibleInTree() || (packed >> 24) == 0)
        {
            flags |= Sprite2DFlags.Hidden;   // alpha 0 packs to 0 at worst, which the engine reads as white
            if (packed == 0) packed = 0xFFFFFFFFu;
        }
        Rect2 r = RegionEnabled ? RegionRect : default;
        var s = new Aver.Framework.Sprite2D
        {
            Texture = Canvas2D.AssetIdOf(_texture),
            Region = new System.Numerics.Vector4(r.Position.X, r.Position.Y, Math.Max(0f, r.Size.X), Math.Max(0f, r.Size.Y)),
            Offset = Canvas2D.N(Offset),
            Modulate = packed,
            Flags = flags,
            HFrames = Math.Max(1, Hframes),
            VFrames = Math.Max(1, Vframes),
            Frame = _frame,
            Blend = ResolvedBlend() switch { UiBlendId.Add => Sprite2DBlend.Add, UiBlendId.Sub => Sprite2DBlend.Sub, UiBlendId.Mul => Sprite2DBlend.Mul, _ => Sprite2DBlend.Mix },
        };
        Canvas2D.Write(e, Link2D, s);
    }
}
