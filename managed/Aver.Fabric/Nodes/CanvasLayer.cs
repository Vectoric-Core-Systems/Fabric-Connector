// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// CanvasLayer (Godot's CanvasLayer): the node a game's UI hangs from so it is drawn over the 3D view and can be shown and hidden as one. The
// Controls below it are laid out and drawn as they are below any other node; the layer adds its visibility to theirs (CanvasItem.IsVisibleInTree). The layer's
// number, visibility, offset, rotation and scale are also kept on the engine's C2DLayer component, which orders and places the sprites on it.

using Aver.Framework;

namespace Aver.Fabric;

/// <summary>A group of UI drawn together (Godot's CanvasLayer). Its <see cref="Visible"/> flag hides everything drawn on it. The draw order
/// (<see cref="Layer"/>), the offset, rotation and scale place and order the Controls and the sprites on it.</summary>
public class CanvasLayer : Node, ICanvas2DNode
{
    public new class SignalName : Node.SignalName
    {
        public static readonly StringName VisibilityChanged = "visibility_changed";
    }

    public event Action VisibilityChanged { add => Connect(SignalName.VisibilityChanged, Callable.From(value)); remove => Disconnect(SignalName.VisibilityChanged, Callable.From(value)); }

    private bool _visible = true;
    private float _rotation;
    private Vector2 _offset, _scale = Vector2.One;

    /// <summary>The draw order among layers: higher is drawn over lower.</summary>
    public int Layer { get; set; } = 1;

    public bool Visible
    {
        get => _visible;
        set
        {
            if (_visible == value) return;
            _visible = value;
            EmitSignal(SignalName.VisibilityChanged);
        }
    }

    public void Show() => Visible = true;
    public void Hide() => Visible = false;

    /// <summary>True when this layer and every layer above it is shown and the node is in the tree.</summary>
    public bool IsVisibleInTree()
    {
        if (!IsInsideTree()) return false;
        for (Node? n = this; n != null; n = n.GetParent())
            if (n is CanvasLayer { _visible: false }) return false;
        return true;
    }

    public Vector2 Offset { get => _offset; set => _offset = value; }
    public float Rotation { get => _rotation; set => _rotation = value; }
    public float RotationDegrees { get => _rotation * (180f / Mathf.Pi); set => _rotation = value * (Mathf.Pi / 180f); }
    public Vector2 Scale { get => _scale; set => _scale = value; }
    public bool FollowViewportEnabled { get; set; }
    public float FollowViewportScale { get; set; } = 1f;

    /// <summary>The offset, rotation and scale as one transform.</summary>
    public Transform2D Transform
    {
        get => new(_rotation, _scale, 0f, _offset);
        set { _offset = value.Origin; _rotation = value.GetRotation(); _scale = value.GetScale(); }
    }

    public Transform2D GetFinalTransform() => Transform;

    private readonly Canvas2DLink _link = new();
    Node ICanvas2DNode.Node => this;
    Canvas2DLink ICanvas2DNode.Link => _link;

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

    void ICanvas2DNode.Push2D()
    {
        int e = Canvas2D.Backing(this);
        if (e == 0) return;
        var l = new Layer2D
        {
            Index = Layer,
            Flags = (_visible ? Layer2DFlags.None : Layer2DFlags.Hidden) | (FollowViewportEnabled ? Layer2DFlags.FollowViewport : Layer2DFlags.None),
            Offset = Canvas2D.N(_offset),
            Rotation = _rotation,
            Scale = Canvas2D.NScale(_scale),
        };
        Canvas2D.Write(e, _link, l);
    }
}
