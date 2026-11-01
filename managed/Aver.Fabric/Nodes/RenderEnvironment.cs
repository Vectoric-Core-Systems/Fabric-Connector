// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Which Environment and CameraAttributes the engine shows (wave 4, F31) and the physical light units that depend on the exposure (F21).
// Godot's order: the current camera's own environment and attributes, else the World3D's (a WorldEnvironment node sets it), else the world's fallback. The level carries
// a WorldEnvironment's values from load (the converter wrote them); a camera's own are pushed when that camera becomes current and withdrawn when it stops.

namespace Aver.Fabric;

/// <summary>Physical light units (rendering/lights_and_shadows/use_physical_light_units): the project's switch and the exposure normalization of the camera attributes in force.</summary>
internal static class PhysicalLights
{
    private static bool? s_enabled;

    /// <summary>A test's choice; null reads the project's setting.</summary>
    internal static bool? Override { get; set; }

    internal static bool Enabled
    {
        get
        {
            if (Override is bool o) return o;
            return s_enabled ??= ProjectSettings.GetSetting("rendering/lights_and_shadows/use_physical_light_units", false).AsBool();
        }
    }

    /// <summary>The factor Godot scales every light by (1 without physical units, or without camera attributes).</summary>
    internal static float Normalization { get; private set; } = 1f;

    /// <summary>The attributes in force changed: lights take the new exposure.</summary>
    internal static void Use(CameraAttributes? attributes)
    {
        float n = Enabled && attributes != null ? attributes.GetExposureNormalization() : 1f;
        if (n == Normalization) return;
        Normalization = n;
        Light3D.ReapplyAll();
    }

    internal static void Reset()
    {
        s_enabled = null;
        Override = null;
        Normalization = 1f;
    }
}

/// <summary>Resolves and pushes the environment in force. Pure resolution, so a test can check the order without an engine.</summary>
internal static class RenderEnvironment
{
    /// <summary>The environment and attributes that win: the current camera's, else the root world's.</summary>
    internal static (Environment? Env, CameraAttributes? Attributes) Resolve()
    {
        Camera3D? cam = Camera3D.CurrentCamera;
        if (cam != null && !cam.IsInsideTree()) cam = null;
        World3D world = SceneTree.Instance.Root.World3D;
        return (cam?.Environment ?? world.Environment ?? world.FallbackEnvironment, cam?.Attributes ?? world.CameraAttributes);
    }

    /// <summary>True once the level has loaded and something may change the picture: a change before that is already in the level's own records.</summary>
    internal static bool Live { get; set; }

    internal static void WorldChanged() { if (Live) Refresh(); }

    internal static int Pushes { get; private set; }
    private static bool s_pushed;

    /// <summary>Pushes the environment in force (the post layer, the fog, the sky light, the ambient override) and tells the lights which exposure applies. With no
    /// environment in force, what an earlier push set is released and the level's own values return.</summary>
    internal static void Refresh()
    {
        (Environment? env, CameraAttributes? attrs) = Resolve();
        PhysicalLights.Use(attrs);
        Pushes++;
        if (!NodeHost.Current.IsEngine) return;
        if (env == null)
        {
            if (!s_pushed) return;
            s_pushed = false;
            Aver.Framework.PostFx.ClearLayer(WorldEnvironment.Layer);
            foreach (string name in new[] { "fog.density", "fog.color", "fog.falloff", "fog.height", "sky.light", "ambient.color", "ambient.energy", "ambient.sky" })
                Aver.Framework.Sky.ClearOverride(name);   // not the sun's: a script or a DirectionalLight3D may own those
            return;
        }
        s_pushed = true;
        foreach ((string key, float value) in EnvironmentMapping.PostValues(env, attrs))
            Aver.Framework.PostFx.SetValue(WorldEnvironment.Layer, key, value);
        Aver.Framework.PostFx.SetLayerWeight(WorldEnvironment.Layer, 1f, 0f);
        Aver.Framework.Sky.SetFogDensity(EnvironmentMapping.FogDensity(env));
        // The environment's fog colour is a nominal sRGB value; the fog override takes linear light.
        Color c = env.FogEnabled ? env.FogLightColor : env.VolumetricFogAlbedo;
        Aver.Framework.Sky.SetFogColor(MathF.Pow(c.R, 2.2f), MathF.Pow(c.G, 2.2f), MathF.Pow(c.B, 2.2f));
        if (env.FogEnabled && env.FogHeightDensity != 0f)
            Aver.Framework.Sky.SetFogShape(Math.Abs(env.FogHeightDensity) / 100f, env.FogHeight * 100f, 0f);
        Aver.Framework.Sky.SetSkyLight(EnvironmentMapping.SkyLight(env));
        if (EnvironmentMapping.AmbientOverride(env) is { } a) Aver.Framework.Sky.SetAmbient(a.R, a.G, a.B, a.Energy, a.SkyContribution);
        else
        {
            Aver.Framework.Sky.ClearOverride("ambient.color");
            Aver.Framework.Sky.ClearOverride("ambient.energy");
            Aver.Framework.Sky.ClearOverride("ambient.sky");
        }
    }

    /// <summary>A camera started or stopped being current, or changed its own environment or attributes: if that matters, the picture changes.</summary>
    internal static void CameraChanged(Camera3D? from, Camera3D? to)
    {
        if ((from?.HasRenderOverrides ?? false) || (to?.HasRenderOverrides ?? false)) Refresh();
    }

    internal static void ResetForTests()
    {
        Pushes = 0;
        Live = false;
        s_pushed = false;
        PhysicalLights.Reset();
        Light3D.ResetLights();
    }
}
