// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// A minimal 2D family, so that a converted script which names a 2D type (a field, a cast, an `is` check) compiles. 2D is out of scope: these nodes keep their properties and
// do nothing else (nothing is drawn, no body is simulated), and the converter reports a script's use of one as unsupported. Path2D and PathFollow2D are the exception:
// 3D games use a Curve2D as data (recoil and spray patterns), so its points, baking and sampling are real.

namespace Aver.Fabric;

/// <summary>A node with a 2D transform (Godot's Node2D). Unsupported: the values are kept for scripts and nothing uses them.</summary>
public class Node2D : CanvasItem
{
    public Vector2 Position { get; set; }
    public float Rotation { get; set; }
    public Vector2 Scale { get; set; } = Vector2.One;
    public int ZIndex { get; set; }

    /// <summary>Same as <see cref="Position"/>: there is no 2D parent chain to compose with.</summary>
    public Vector2 GlobalPosition { get => Position; set => Position = value; }
    public float GlobalRotation { get => Rotation; set => Rotation = value; }
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
