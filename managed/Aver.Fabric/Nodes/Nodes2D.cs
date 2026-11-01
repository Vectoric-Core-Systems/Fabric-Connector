// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// A minimal 2D family, so that a converted script which names a 2D type (a field, a cast, an `is` check) compiles. 2D is out of scope: these nodes keep their properties and
// do nothing else (nothing is drawn, no body is simulated), and the converter reports a script's use of one as unsupported.

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
