// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// CanvasItemMaterial (Godot's): the blend mode of a canvas item. The engine's UI draw list takes a blend per draw (parity wave 8a, P41), so the five blend modes
// are all drawn; the light mode and the particle animation fields are kept for scripts and change nothing (there is no 2D lighting here).

namespace Aver.Fabric;

/// <summary>A material that sets how a CanvasItem blends with what is behind it (Godot's CanvasItemMaterial).</summary>
public class CanvasItemMaterial : Material
{
    public enum BlendModeEnum { Mix = 0, Add = 1, Sub = 2, Mul = 3, PremultAlpha = 4 }

    public enum LightModeEnum { Normal = 0, Unshaded = 1, LightOnly = 2 }

    public BlendModeEnum BlendMode { get; set; }
    public LightModeEnum LightMode { get; set; }
    public bool ParticlesAnimation { get; set; }
    public int ParticlesAnimHFrames { get; set; } = 1;
    public int ParticlesAnimVFrames { get; set; } = 1;
    public bool ParticlesAnimLoop { get; set; }

    /// <summary>The engine blend for this mode. The UI's colours are premultiplied on the way in, so mix and premultiplied alpha are one blend.</summary>
    internal UiBlendId ToEngineBlend() => BlendMode switch
    {
        BlendModeEnum.Add => UiBlendId.Add,
        BlendModeEnum.Sub => UiBlendId.Sub,
        BlendModeEnum.Mul => UiBlendId.Mul,
        _ => UiBlendId.Mix,
    };
}
