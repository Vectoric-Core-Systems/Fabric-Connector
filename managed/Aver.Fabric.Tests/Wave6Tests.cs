// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Wave 6 (particles, decals, sprites), the managed side: the Decal's axes derived from Godot's projection, its P13 fields reaching the engine's descriptor, the particle classes
// (ParticleProcessMaterial, CPUParticles3D, the collider and attractor nodes) and the animated sprite. What needs a GPU (the picture) is a headed check.

using Aver.Fabric;
using Collections = Aver.Fabric.Collections;

static class Wave6Tests
{
    public static int RunAll() => T.Suite(() =>
    {
        T.Run("wave 6: a decal's three axes come from Godot's projection (F25)", DecalAxes);
        T.Run("wave 6: a decal's emission, fades and albedo mix reach the descriptor (P13)", DecalFields);
        T.Run("wave 6: ParticleProcessMaterial, CPUParticles3D and the collider and attractor nodes exist under Godot's names (F27 to F29)", ParticleClasses);
        T.Run("wave 6: a GPUParticles3D keeps a ParticleProcessMaterial for scripts (F27)", ProcessMaterialKept);
        T.Run("wave 6: SpriteFrames takes Godot's stored form and an AnimatedSprite3D plays it (F19)", SpriteFramesStoredForm);
    });

    private static void SpriteFramesStoredForm()
    {
        var texA = new Texture2D { ResourcePath = "Textures/a.png" };
        var texB = new Texture2D { ResourcePath = "Textures/b.png" };
        var walk = new Collections.Array
        {
            new Collections.Dictionary { ["duration"] = 1.0f, ["texture"] = Variant.CreateFrom(texA) },
            new Collections.Dictionary { ["duration"] = 2.0f, ["texture"] = Variant.CreateFrom(texB) },
        };
        var stored = new Collections.Array
        {
            new Collections.Dictionary { ["frames"] = walk, ["loop"] = true, ["name"] = new StringName("walk"), ["speed"] = 8.0 },
            new Collections.Dictionary { ["frames"] = new Collections.Array(), ["loop"] = false, ["name"] = new StringName("idle"), ["speed"] = 5.0 },
        };
        var frames = new SpriteFrames { Animations = stored };
        T.Check("the stored animations replace the default one, in order", frames.GetAnimationNames().SequenceEqual(new[] { "walk", "idle" }) && !frames.HasAnimation("default"));
        T.Check("speeds, loops and frames are read", T.Near((float)frames.GetAnimationSpeed("walk"), 8f) && frames.GetAnimationLoop("walk") && !frames.GetAnimationLoop("idle") && frames.GetFrameCount("walk") == 2);
        T.Check("each frame keeps its texture and relative duration", ReferenceEquals(frames.GetFrameTexture("walk", 1), texB) && T.Near(frames.GetFrameDuration("walk", 1), 2f));
        Collections.Array back = frames.Animations;
        T.Check("reading them back gives the same shape", back.Count == 2 && back[0].AsGodotDictionary()["frames"].AsGodotArray().Count == 2);

        var sprite = new AnimatedSprite3D { SpriteFrames = frames, Animation = "walk" };
        sprite.FrameAssets = new[] { "Textures/a.png|Meshes/Generated/SpriteQuad_1.ocmesh|Materials/A.ocmat", "res://b.png|Meshes/Generated/SpriteQuad_2.ocmesh|Materials/B.ocmat" };
        SceneTree.Instance.Root.AddChild(sprite);
        sprite.Play();
        T.Frame(0.2);
        T.Check("without an engine the picture is not swapped and nothing throws", sprite.PictureSwaps == 0 && sprite.IsPlaying());
    }

    private static (float X, float Y, float Z) Rotate(Aver.Scene.Quat q, float x, float y, float z)
    {
        (float cx, float cy, float cz) = (q.Y * z - q.Z * y + q.W * x, q.Z * x - q.X * z + q.W * y, q.X * y - q.Y * x + q.W * z);
        return (x + 2 * (q.Y * cz - q.Z * cy), y + 2 * (q.Z * cx - q.X * cz), z + 2 * (q.X * cy - q.Y * cx));
    }

    // Godot's shader maps a surface point into the box's 0..1 and reads the texture at (x, z), the depth along y, and the projector looks down -Y. So in the node's own frame the
    // projection is -Y, the image's u is +X and the image's v (down the picture) is +Z: image up is -Z. The engine's decal projects along its +X, u along +Y, up along +Z. FabricSpace
    // maps a Godot direction to the engine's by (x, y, z) -> (-z, x, y). Whatever the node's rotation, the three engine axes of the descriptor's rotation are those images.
    private static void DecalAxes()
    {
        var rotations = new[]
        {
            Vector3.Zero, new Vector3(90, 0, 0), new Vector3(0, 90, 0), new Vector3(0, 0, 90), new Vector3(30, 40, 50), new Vector3(-75, 12, 160), new Vector3(180, 0, 0), new Vector3(10, -120, 33),
        };
        foreach (Vector3 deg in rotations)
        {
            var d = new Decal { Size = new Vector3(2, 1, 2), RotationDegrees = deg };
            Aver.Scene.Quat q = d.ComputeDesc(false).Rotation;
            Basis basis = d.Transform.Basis;
            static Aver.Scene.Vec3 Aver3(Vector3 v) => FabricSpace.ToAverDirection(v);
            Aver.Scene.Vec3 down = Aver3(basis * new Vector3(0, -1, 0)), right = Aver3(basis * new Vector3(1, 0, 0)), up = Aver3(basis * new Vector3(0, 0, -1));
            (float px, float py, float pz) = Rotate(q, 1, 0, 0);
            (float ux, float uy, float uz) = Rotate(q, 0, 1, 0);
            (float vx, float vy, float vz) = Rotate(q, 0, 0, 1);
            T.Check($"rotation {deg}: the engine's projection axis is Godot's -Y", T.Near(px, down.X, 1e-4f) && T.Near(py, down.Y, 1e-4f) && T.Near(pz, down.Z, 1e-4f), $"{px} {py} {pz} vs {down.X} {down.Y} {down.Z}");
            T.Check($"rotation {deg}: the image's u (right) is Godot's +X", T.Near(ux, right.X, 1e-4f) && T.Near(uy, right.Y, 1e-4f) && T.Near(uz, right.Z, 1e-4f), $"{ux} {uy} {uz} vs {right.X} {right.Y} {right.Z}");
            T.Check($"rotation {deg}: the image's up is Godot's -Z", T.Near(vx, up.X, 1e-4f) && T.Near(vy, up.Y, 1e-4f) && T.Near(vz, up.Z, 1e-4f), $"{vx} {vy} {vz} vs {up.X} {up.Y} {up.Z}");
        }
        var s = new Decal { Size = new Vector3(2, 0.5f, 4) };
        Aver.Scene.DecalDesc desc = s.ComputeDesc(false);
        T.Check("the box: depth is Godot's y, the width its x and the height its z", T.Near(desc.SizeCm.X, 50f) && T.Near(desc.SizeCm.Y, 200f) && T.Near(desc.SizeCm.Z, 400f));
    }

    private static void DecalFields()
    {
        var d = new Decal();
        Aver.Scene.DecalDesc def = d.ComputeDesc(false);
        T.Check("Godot's defaults: upper and lower fade 0.3 as a depth curve, no normal fade, albedo mix 1, emission energy 1",
            def.UseDepthFade && T.Near(def.UpperFade, 0.3f) && T.Near(def.LowerFade, 0.3f) && T.Near(def.NormalFade, 0f) && def.UseAlbedoMix && T.Near(def.AlbedoMix, 1f) && T.Near(def.EmissionEnergy, 1f));
        T.Check("with no albedo image the decal paints no flat colour", (def.Disable & Aver.Scene.DecalChannels.Colour) != 0);
        d.UpperFade = 2f; d.LowerFade = 0f; d.NormalFade = 0.4f; d.AlbedoMix = 0.25f; d.EmissionEnergy = 3f; d.Modulate = new Color(1, 1, 1, 0.5f);
        d.SetTexture(Decal.DecalTexture.Albedo, new Texture2D());
        Aver.Scene.DecalDesc set = d.ComputeDesc(false);
        T.Check("upper_fade, lower_fade, normal_fade, albedo_mix and emission_energy are carried", T.Near(set.UpperFade, 2f) && T.Near(set.LowerFade, 0f) && T.Near(set.NormalFade, 0.4f) && T.Near(set.AlbedoMix, 0.25f) && T.Near(set.EmissionEnergy, 3f));
        T.Check("the modulate alpha is the opacity of every channel; the mix is separate", T.Near(set.Transparency, 0.5f) && (set.Disable & Aver.Scene.DecalChannels.Colour) == 0);
        d.NormalFade = 5f; d.UpperFade = -1f;
        Aver.Scene.DecalDesc clamped = d.ComputeDesc(false);
        T.Check("normal_fade is held under 1 and a negative fade is 0", clamped.NormalFade < 1f && clamped.NormalFade > 0.99f && clamped.UpperFade == 0f);
        T.Check("the emission texture slot is kept", d.GetTexture(Decal.DecalTexture.Emission) == null);
        var tex = new Texture2D();
        d.TextureEmission = tex;
        T.Check("and set by name", ReferenceEquals(d.TextureEmission, tex));
    }

    private static void ParticleClasses()
    {
        foreach (string cls in new[]
                 {
                     "ParticleProcessMaterial", "CPUParticles3D", "GPUParticles3D", "GPUParticlesCollision3D", "GPUParticlesCollisionSphere3D", "GPUParticlesCollisionBox3D",
                     "GPUParticlesCollisionHeightField3D", "GPUParticlesCollisionSDF3D", "GPUParticlesAttractor3D", "GPUParticlesAttractorSphere3D", "GPUParticlesAttractorBox3D",
                     "GPUParticlesAttractorVectorField3D",
                 })
            T.Check(cls + " is a class", ClassDB.ClassExists(cls));
        T.Check("the colliders are VisualInstance3D", ClassDB.IsParentClass("GPUParticlesCollisionSphere3D", "VisualInstance3D") && ClassDB.IsParentClass("GPUParticlesAttractorBox3D", "GPUParticlesAttractor3D"));

        var m = new ParticleProcessMaterial();
        T.Check("Godot's defaults: direction +X, spread 45, gravity -9.8, scale 1..1, white", m.Direction == new Vector3(1, 0, 0) && T.Near(m.Spread, 45f) && T.Near(m.Gravity.Y, -9.8f) && T.Near(m.ScaleMin, 1f) && T.Near(m.ScaleMax, 1f) && T.Near(m.Color.R, 1f));
        m.InitialVelocityMin = 2; m.InitialVelocityMax = 5; m.SetParamMin(ParticleProcessMaterial.ParameterEnum.Damping, 0.5f); m.SetParamMax(ParticleProcessMaterial.ParameterEnum.Damping, 1.5f);
        T.Check("the range pairs are kept by name and by parameter", T.Near(m.InitialVelocityMin, 2f) && T.Near(m.GetParamMax(ParticleProcessMaterial.ParameterEnum.InitialLinearVelocity), 5f) && T.Near(m.DampingMin, 0.5f) && T.Near(m.DampingMax, 1.5f));
        m.ParticleFlagAlignY = true;
        T.Check("particle flags", m.GetParticleFlag(ParticleProcessMaterial.ParticleFlagsEnum.AlignYToVelocity) && !m.ParticleFlagRotateY);
        var tex = new Texture2D();
        m.ScaleCurve = tex;
        T.Check("a curve texture is kept by parameter", ReferenceEquals(m.GetParamTexture(ParticleProcessMaterial.ParameterEnum.Scale), tex));
        T.Check("an out of range parameter is ignored", m.GetParamMin((ParticleProcessMaterial.ParameterEnum)99) == 0f);

        var c = new CPUParticles3D { Amount = 24, Lifetime = 2.0 };
        c.ScaleAmountMin = 0.5f; c.InitialVelocityMax = 4f; c.ParticleFlagAlignY = true;
        T.Check("CPUParticles3D keeps Godot's own names", c.Amount == 24 && T.Near(c.ScaleAmountMin, 0.5f) && T.Near(c.ScaleAmountMax, 1f) && T.Near(c.InitialVelocityMax, 4f) && c.ParticleFlagAlignY && c.Emitting);
        var box = new GPUParticlesCollisionBox3D();
        var atl = new GPUParticlesAttractorSphere3D { Strength = 3f };
        T.Check("the collider and attractor nodes keep their sizes and strengths", box.Size == new Vector3(2, 2, 2) && T.Near(atl.Radius, 1f) && T.Near(atl.Strength, 3f) && T.Near(atl.Attenuation, 1f));
    }

    private static void ProcessMaterialKept()
    {
        var p = new GPUParticles3D();
        var m = new ParticleProcessMaterial { Spread = 10f };
        p.ProcessMaterial = m;
        T.Check("the material is returned as set", ReferenceEquals(p.ProcessMaterial, m));
        p.ProcessMaterial = null;
        T.Check("and cleared", p.ProcessMaterial == null);
        p.TrailEnabled = true; p.TrailLifetime = 0.5; p.AmountRatio = 0.5;
        T.Check("the trail and ratio properties are kept", p.TrailEnabled && T.Near((float)p.TrailLifetime, 0.5f) && T.Near((float)p.AmountRatio, 0.5f));
    }
}
