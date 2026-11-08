// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Wave 4 (rendering settings, lights, shadows, environment), the managed side: RenderMapping (the same numbers as the converter's FabricWave4Test), a window viewport's
// render properties reaching the engine's RenderSettings through a recording backend (F1), the stored RenderingServer calls (F37), the Camera3D / World3D environment and
// attributes order (F31), physical light units, temperature and projector textures (F21, F22), per-instance render flags (P5, F32) and the glow, ambient and fog values
// of an Environment. What needs a GPU (the picture) is a headed check; what runs here is what decides it.

using Aver.Fabric;
using RenderKey = Aver.Framework.RenderKey;
using Env = Aver.Fabric.Environment;

static class Wave4Tests
{
    public static int RunAll() => T.Suite(() =>
    {
        T.Run("wave 4: RenderMapping, the Godot numbers as Aver's", Mapping);
        T.Run("wave 4: a window viewport's render properties reach the engine (F1)", ViewportProperties);
        T.Run("wave 4: the window starts from the project's settings (F1)", ProjectDefaults);
        T.Run("wave 4: a SubViewport keeps its render values and leaves the window alone (F1)", SubViewportKeeps);
        T.Run("wave 4: the stored RenderingServer calls (F37)", StoredCalls);
        T.Run("wave 4: physical light units, temperature and the exposure normalization (F21)", LightUnits);
        T.Run("wave 4: a light projector is the light's cookie (F22)", Projector);
        T.Run("wave 4: the camera's environment and attributes win over the world's (F31)", CameraEnvironment);
        T.Run("wave 4: Environment glow, ambient and volumetric fog values (P8, P9, P10, F24)", EnvironmentValues);
        T.Run("wave 4: per-instance shadow, GI, cull and LOD flags (P5, F32)", InstanceFlagValues);
    });

    private sealed class Rec : IRenderBackend
    {
        public readonly List<string> Calls = new();
        public bool Set(RenderKey key, float value) { Calls.Add($"{key} {value:0.####}"); return true; }
        public bool Post(string key, float value) { Calls.Add($"post {key} {value:0.####}"); return true; }
        public Aver.Framework.RenderStats? Stats() => new Aver.Framework.RenderStats(12, 3456, 7, 1, 0, 9);
    }

    // A relative comparison, for the light and exposure numbers (some are 1e-5, some 1e5).
    private static bool R(float a, float b, float rel = 1e-4f) => MathF.Abs(a - b) <= rel * MathF.Max(1e-12f, MathF.Abs(b));

    private static Rec Recording()
    {
        RenderEnvironment.ResetForTests();
        var rec = new Rec();
        RenderBackend.Current = rec;
        return rec;
    }

    private static void Mapping()
    {
        T.Check("msaa_3d 0 to 3 is 1, 2, 4, 8 samples", RenderMapping.MsaaSamples(0) == 1 && RenderMapping.MsaaSamples(1) == 2 && RenderMapping.MsaaSamples(2) == 4 && RenderMapping.MsaaSamples(3) == 8);
        T.Check("MSAA 4x without TAA stays 4 samples", RenderMapping.ChooseAa(2, false) == (4, 0));
        T.Check("TAA on is one sample whatever msaa_3d says", RenderMapping.ChooseAa(3, true) == (1, 1));
        T.Check("fsr_sharpness halves with every 1.0", T.Near(RenderMapping.FsrSharpness(0f), 1f) && T.Near(RenderMapping.FsrSharpness(1f), 0.5f) && T.Near(RenderMapping.FsrSharpness(2f), 0.25f));
        T.Check("scaling_3d_scale is clamped to 0.25 .. 2", RenderMapping.Scale3D(0.1f) == 0.25f && RenderMapping.Scale3D(5f) == 2f);
        T.Check("AverSR presets by FSR scale", RenderMapping.AverSrPreset(1f) == 0 && RenderMapping.AverSrPreset(0.67f) == 1 && RenderMapping.AverSrPreset(0.59f) == 2 && RenderMapping.AverSrPreset(0.5f) == 3);
        T.Check("mesh_lod_threshold is pixels, 0 .. 64", RenderMapping.LodThresholdPixels(-1f) == 0f && RenderMapping.LodThresholdPixels(2f) == 2f && RenderMapping.LodThresholdPixels(99f) == 64f);
        T.Check("lod_bias is log2 of the multiplier, -8 for 0", T.Near(RenderMapping.LodBiasStops(1f), 0f) && T.Near(RenderMapping.LodBiasStops(4f), 2f) && T.Near(RenderMapping.LodBiasStops(0.5f), -1f) && RenderMapping.LodBiasStops(0f) == -8f);
        T.Check("a cascade is half the atlas, 512 .. 4096", RenderMapping.ShadowResolution(4096, 4) == 2048 && RenderMapping.ShadowResolution(8192, 4) == 4096 && RenderMapping.ShadowResolution(2048, 1) == 2048 && RenderMapping.ShadowResolution(1024, 4) == 512);
        T.Check("the shadow filter is monotonic and Godot's default is the engine's 3x3", RenderMapping.ShadowFilterWidth(0) == 0f && RenderMapping.ShadowFilterWidth(3) == 1f && RenderMapping.ShadowFilterWidth(5) == 3f);
        T.Check("cascade counts", RenderMapping.CascadeCount(0) == 1 && RenderMapping.CascadeCount(1) == 2 && RenderMapping.CascadeCount(2) == 4);

        // The split fit: Aver's own slicing at lambda comes back as that lambda.
        const double n = 10, f = 8000;
        foreach (double lambda in new[] { 0.2, 0.5, 0.85 })
        {
            var g = new float[3];
            for (int i = 1; i < 4; i++)
            {
                double p = i / 4.0;
                g[i - 1] = (float)((lambda * n * Math.Pow(f / n, p) + (1 - lambda) * (n + (f - n) * p) - n) / (f - n));
            }
            T.Check($"splits that are Aver's scheme at lambda {lambda} fit back to it", T.Near(RenderMapping.FitSplitLambda(4, g, (float)n, (float)f), (float)lambda, 1e-3f));
        }
        T.Check("one cascade has nothing to fit", RenderMapping.FitSplitLambda(1, new float[] { 0.5f }, 10f, 8000f) < 0f);
        var sun = RenderMapping.FitSunShadow(2, 0.1f, 0.2f, 0.5f, 100f, 0.05f);
        T.Check("Godot's default splits at 100 m: four cascades, 10000 cm, a lambda between the schemes", sun.Cascades == 4 && T.Near(sun.DistanceCm, 10000f, 1f) && sun.Lambda > 0.3f && sun.Lambda < 0.7f);
        T.Check("max distance 0 is automatic", RenderMapping.FitSunShadow(1, 0.3f, 0.2f, 0.5f, 0f, 0.05f).DistanceCm == 0f);

        T.Check("Godot's physical camera at f/16, 1/100 s, ISO 100 is 1 / 30720", R(RenderMapping.PhysicalExposureNormalization(16f, 100f, 100f), 1f / 30720f));
        T.Check("f/8, 1/200 s, ISO 200 is 1 / 7680", R(RenderMapping.PhysicalExposureNormalization(8f, 200f, 200f), 1f / 7680f));
        T.Check("the practical camera at ISO 100 matches the physical default", R(RenderMapping.PracticalExposureNormalization(100f), 100f / 3072007f));
        T.Check("the default sun at the default camera shades with 3.255", R(RenderMapping.PhysicalShadingEnergy(RenderMapping.LightClass.Directional, 1f, 100000f, 1f / 30720f), 100000f / 30720f));
        T.Check("an omni light is intensity / (4 pi), a spot light intensity / pi",
            R(RenderMapping.PhysicalShadingEnergy(RenderMapping.LightClass.Omni, 2f, 1000f, 1f), 2000f / (4f * MathF.PI)) &&
            R(RenderMapping.PhysicalShadingEnergy(RenderMapping.LightClass.Spot, 1f, 1000f, 1f), 1000f / MathF.PI));
        var warm = RenderMapping.TemperatureTint(2700f);
        var cool = RenderMapping.TemperatureTint(10000f);
        var white = RenderMapping.TemperatureTint(6500f);
        T.Check("2700 K is warm (red 1, then green, then blue), 10000 K cool (blue 1), 6500 K nearly white",
            T.Near(warm.R, 1f) && warm.G > warm.B && T.Near(cool.B, 1f) && cool.R < cool.B && white.R > 0.9f && white.G > 0.9f && white.B > 0.9f);
        T.Check("glow blend keeps Godot's numbers", RenderMapping.GlowBlend(2) == 2 && RenderMapping.GlowBlend(9) == 4);
    }

    private static void ViewportProperties()
    {
        Rec rec = Recording();
        Window root = SceneTree.Instance.Root;
        T.Check("the window starts from Godot's defaults: no MSAA, no TAA, scale 1, sharpness 0.2, LOD threshold 1, atlas 4096",
            root.Msaa3D == Viewport.Msaa.Disabled && !root.UseTaa && root.Scaling3DScale == 1f && T.Near(root.FsrSharpness, 0.2f) && root.MeshLodThreshold == 1f && root.PositionalShadowAtlasSize == 4096 &&
            root.Scaling3DMode == Viewport.Scaling3DModeEnum.Bilinear && !root.UseDebanding && !root.UseOcclusionCulling);
        T.Check("reading a value touches nothing", rec.Calls.Count == 0);

        root.Msaa3D = Viewport.Msaa.Msaa4X;
        T.Check("MSAA 4x is the engine's four samples, TAA off", rec.Calls.Contains("Msaa 4") && rec.Calls.Contains("Taa 0") && root.Msaa3D == Viewport.Msaa.Msaa4X);
        rec.Calls.Clear();
        root.UseTaa = true;
        T.Check("TAA on switches the multisampling off: the consistent set", rec.Calls.Contains("Msaa 1") && rec.Calls.Contains("Taa 1") && root.Msaa3D == Viewport.Msaa.Msaa4X);
        rec.Calls.Clear();
        root.Scaling3DScale = 0.5f;
        T.Check("the 3D scale is RenderSettings.Scale", rec.Calls.SequenceEqual(new[] { "Scale 0.5" }));
        rec.Calls.Clear();
        root.Scaling3DScale = 9f;
        T.Check("and is clamped to the engine's range", rec.Calls.SequenceEqual(new[] { "Scale 2" }) && root.Scaling3DScale == 9f);
        rec.Calls.Clear();
        root.FsrSharpness = 1f;
        T.Check("fsr_sharpness 1 is the engine's sharpness 0.5", rec.Calls.SequenceEqual(new[] { "Sharpness 0.5" }));
        rec.Calls.Clear();
        root.MeshLodThreshold = 3f;
        root.UseOcclusionCulling = true;
        root.UseDebanding = true;
        T.Check("LOD threshold, occlusion culling and debanding reach the engine", rec.Calls.SequenceEqual(new[] { "LodThreshold 3", "Occlusion 1", "post deband 1" }));
        root.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
        root.TextureMipmapBias = -1f;
        root.AnisotropicFilteringLevel = Viewport.AnisotropicFiltering.Anisotropy8X;
        root.SetPositionalShadowAtlasQuadrantSubdiv(1, Viewport.PositionalShadowAtlasQuadrantSubdiv.Subdiv64);
        root.PositionalShadowAtlasSize = 1024;
        root.DebugDraw = Viewport.DebugDrawEnum.Wireframe;
        T.Check("the stored values read back and push nothing more",
            root.ScreenSpaceAA == Viewport.ScreenSpaceAAEnum.Fxaa && root.TextureMipmapBias == -1f && root.AnisotropicFilteringLevel == Viewport.AnisotropicFiltering.Anisotropy8X &&
            root.GetPositionalShadowAtlasQuadrantSubdiv(1) == Viewport.PositionalShadowAtlasQuadrantSubdiv.Subdiv64 && root.PositionalShadowAtlasSize == 1024 && root.DebugDraw == Viewport.DebugDrawEnum.Wireframe &&
            rec.Calls.Count == 3);
        T.Check("FindWorld3D is the viewport's world", ReferenceEquals(root.FindWorld3D(), root.World3D));
        RenderBackend.Current = new EngineRenderBackend();
    }

    private static void ProjectDefaults()
    {
        Rec rec = Recording();
        string dir = Path.Combine(Path.GetTempPath(), "aver-fabric-wave4-managed", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "Content"));
        Directory.CreateDirectory(Path.Combine(dir, "Fabric"));
        File.WriteAllText(Path.Combine(dir, "Fabric", "project.json"),
            "{\"settings\":{\"rendering/anti_aliasing/quality/msaa_3d\":\"2\",\"rendering/anti_aliasing/quality/use_taa\":\"false\",\"rendering/scaling_3d/scale\":\"0.5\"," +
            "\"rendering/scaling_3d/mode\":\"1\",\"rendering/scaling_3d/fsr_sharpness\":\"1.0\",\"rendering/mesh_lod/lod_change/threshold_pixels\":\"2.0\"," +
            "\"rendering/anti_aliasing/quality/use_debanding\":\"true\",\"rendering/lights_and_shadows/positional_shadow/atlas_size\":\"2048\"}}");
        try
        {
            ResourceLoader.ContentRootOverride = Path.Combine(dir, "Content");
            ProjectSettings.ResetForTests();
            Window root = SceneTree.Instance.Root;
            T.Check("the window's values are the project's: MSAA 4x, FSR at 0.5, sharpness 1, LOD 2, debanding, atlas 2048",
                root.Msaa3D == Viewport.Msaa.Msaa4X && !root.UseTaa && root.Scaling3DMode == Viewport.Scaling3DModeEnum.Fsr && T.Near(root.Scaling3DScale, 0.5f) && root.FsrSharpness == 1f &&
                root.MeshLodThreshold == 2f && root.UseDebanding && root.PositionalShadowAtlasSize == 2048);
            T.Check("a SubViewport does not inherit them", new SubViewport().Scaling3DScale == 1f && new SubViewport().Msaa3D == Viewport.Msaa.Disabled);
            T.Check("reading them pushes nothing", rec.Calls.Count == 0);
            root.ApplyProjectRender();
            T.Check("when the game starts the project's 3D scale and sharpness reach the engine (the other settings are .ocproject keys)", rec.Calls.SequenceEqual(new[] { "Scale 0.5", "Sharpness 0.5" }));
        }
        finally
        {
            ResourceLoader.ContentRootOverride = null;
            ProjectSettings.ResetForTests();
            RenderBackend.Current = new EngineRenderBackend();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    private static void SubViewportKeeps()
    {
        Rec rec = Recording();
        var sv = new SubViewport();
        SceneTree.Instance.Root.AddChild(sv);
        sv.UseTaa = true;
        sv.MeshLodThreshold = 4f;
        sv.UseDebanding = true;
        sv.Msaa3D = Viewport.Msaa.Msaa2X;
        T.Check("a SubViewport's values are kept", sv.UseTaa && sv.MeshLodThreshold == 4f && sv.UseDebanding && sv.Msaa3D == Viewport.Msaa.Msaa2X);
        T.Check("and none of them reaches the window's settings", rec.Calls.Count == 0);
        T.Check("its defaults are Godot's, not the project's", new SubViewport().MeshLodThreshold == 1f && new SubViewport().Scaling3DScale == 1f && new SubViewport().Scaling3DMode == Viewport.Scaling3DModeEnum.Bilinear);
        RenderBackend.Current = new EngineRenderBackend();
    }

    private static void StoredCalls()
    {
        Recording();
        T.Check("P18: rendering info reads the engine's statistics, memory counters read 0",
            RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalObjectsInFrame) == 12UL && RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame) == 3456UL &&
            RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame) == 7UL && RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed) == 0UL);
        RenderBackend.Current = new EngineRenderBackend();
        T.Check("and read 0 without an engine", RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalObjectsInFrame) == 0UL);
        RenderingServer.DirectionalShadowAtlasSetSize(8192, false);
        RenderingServer.DecalsSetFilter(RenderingServer.DecalFilter.Nearest);
        RenderingServer.LightProjectorsSetFilter(RenderingServer.LightProjectorFilter.Linear);
        T.Check("the directional atlas size, the decal and projector filters are kept as set",
            RenderingServer.DirectionalShadowAtlas == (8192, false) && RenderingServer.DecalsFilter == RenderingServer.DecalFilter.Nearest && RenderingServer.LightProjectorsFilter == RenderingServer.LightProjectorFilter.Linear);
        RenderingServer.EnvironmentSetSsaoQuality(RenderingServer.EnvironmentSsaoQuality.High, true, 0.4f, 3, 40f, 200f);
        RenderingServer.EnvironmentSetSsilQuality(RenderingServer.EnvironmentSsilQuality.Low, false, 0.5f, 2, 50f, 300f);
        RenderingServer.GiSetUseHalfResolution(true);
        var env = new Env { SsrMaxSteps = 12 };
        T.Check("F37: the screen-space quality calls and ssr_max_steps are stored", RenderingServer.SsaoQuality.Quality == 3 && RenderingServer.SsaoQuality.HalfSize && RenderingServer.SsilQuality.Quality == 1 &&
            RenderingServer.GiHalfResolution && env.SsrMaxSteps == 12);
    }

    private static void LightUnits()
    {
        RenderEnvironment.ResetForTests();
        var omni = new OmniLight3D { LightEnergy = 2f };
        T.Check("Godot's defaults: 1000 lumens, 100000 lux, 6500 K", omni.LightIntensityLumens == 1000f && new DirectionalLight3D().LightIntensityLux == 100000f && omni.LightTemperature == 6500f);
        omni.LightIntensityLux = 3000f;
        T.Check("lux and lumens are one stored value", omni.LightIntensityLumens == 3000f);
        omni.LightIntensityLumens = 2000f;
        omni.LightTemperature = 3000f;

        LightFields plain = omni.ComputeFields();
        T.Check("without physical units the energy is Godot's plain one and the temperature does nothing", R(plain.Intensity, 2f * Light3D.CandelaPerEnergy) && T.Near(plain.Colour.X, 1f) && T.Near(plain.Colour.Z, 1f));

        PhysicalLights.Override = true;
        var attrs = new CameraAttributesPhysical { ExposureAperture = 8f, ExposureShutterSpeed = 200f, ExposureSensitivity = 200f };
        T.Check("the physical camera's normalization is Godot's formula", R(attrs.GetExposureNormalization(), 1f / 7680f));
        T.Check("a practical camera's is its sensitivity over 3072007", R(new CameraAttributesPractical { ExposureSensitivity = 200f }.GetExposureNormalization(), 200f / 3072007f));
        PhysicalLights.Use(attrs);
        LightFields phys = omni.ComputeFields();
        float expect = RenderMapping.PhysicalAverIntensity(RenderMapping.LightClass.Omni, 2f, 2000f, 1f / 7680f);
        T.Check("with physical units: energy x lumens / (4 pi) x the normalization, at 100000/3 candela a unit", R(phys.Intensity, expect));
        var tint = RenderMapping.TemperatureTint(3000f);
        T.Check("and the colour carries the temperature (linear, the brightest channel 1)", T.Near(phys.Colour.X, tint.R, 1e-4f) && T.Near(phys.Colour.Z, tint.B, 1e-4f) && phys.Colour.X > phys.Colour.Z);
        var sun = new DirectionalLight3D { LightEnergy = 0.5f, LightIntensityLux = 80000f };
        T.Check("the sun is energy x lux x the normalization", R(sun.ComputeFields().Intensity, RenderMapping.PhysicalAverIntensity(RenderMapping.LightClass.Directional, 0.5f, 80000f, 1f / 7680f)));
        var spot = new SpotLight3D { LightIntensityLumens = 1500f };
        T.Check("a spot light is intensity / pi", R(spot.ComputeFields().Intensity, RenderMapping.PhysicalAverIntensity(RenderMapping.LightClass.Spot, 1f, 1500f, 1f / 7680f)));
        PhysicalLights.Use(null);
        T.Check("with no camera attributes the normalization is 1", PhysicalLights.Normalization == 1f);
        PhysicalLights.Reset();
    }

    private static void Projector()
    {
        RenderEnvironment.ResetForTests();
        var tex = new Texture2D();
        tex.ResourcePath = "Textures/lights/cone.png";
        T.Check("a converted projector path is the content path as it is", Light3D.ProjectorContentPath(tex) == "Textures/lights/cone.png");
        tex.ResourcePath = "res://lights/cone.png";
        T.Check("a res:// path is the project's Textures/ copy", Light3D.ProjectorContentPath(tex) == "Textures/lights/cone.png");
        T.Check("no picture is no cookie", Light3D.ProjectorContentPath(null) == "");
        var spot = new SpotLight3D { LightProjector = tex };
        T.Check("the projector reads back", ReferenceEquals(spot.LightProjector, tex));
    }

    private static void CameraEnvironment()
    {
        RenderEnvironment.ResetForTests();
        PhysicalLights.Override = true;
        World3D world = SceneTree.Instance.Root.World3D;
        var worldEnv = new Env { BackgroundMode = Env.BGMode.Sky };
        var worldAttrs = new CameraAttributesPractical { ExposureSensitivity = 100f };
        var node = new WorldEnvironment { Environment = worldEnv, CameraAttributes = worldAttrs };
        SceneTree.Instance.Root.AddChild(node);
        T.Check("a WorldEnvironment in the tree sets its world's environment and attributes", ReferenceEquals(world.Environment, worldEnv) && ReferenceEquals(world.CameraAttributes, worldAttrs));
        (Env? e, CameraAttributes? a) = RenderEnvironment.Resolve();
        T.Check("with no camera of its own the world's are in force", ReferenceEquals(e, worldEnv) && ReferenceEquals(a, worldAttrs));
        T.Check("and the lights have their exposure from the moment the node is in the tree", R(PhysicalLights.Normalization, worldAttrs.GetExposureNormalization()));

        var camEnv = new Env { BackgroundMode = Env.BGMode.Color };
        var camAttrs = new CameraAttributesPhysical { ExposureAperture = 8f };
        var camera = new Camera3D { Environment = camEnv, Attributes = camAttrs };
        SceneTree.Instance.Root.AddChild(camera);
        (e, a) = RenderEnvironment.Resolve();
        T.Check("the current camera's own environment and attributes win", camera.IsCurrent() && ReferenceEquals(e, camEnv) && ReferenceEquals(a, camAttrs));
        T.Check("the exposure normalization follows the attributes in force", R(PhysicalLights.Normalization, camAttrs.GetExposureNormalization()));
        var plain = new Camera3D();
        SceneTree.Instance.Root.AddChild(plain);
        plain.MakeCurrent();
        (e, a) = RenderEnvironment.Resolve();
        T.Check("another camera made current brings the world's back (a camera's environment applies only while it is current)", ReferenceEquals(e, worldEnv) && ReferenceEquals(a, worldAttrs));
        T.Check("and the normalization goes back with it", R(PhysicalLights.Normalization, worldAttrs.GetExposureNormalization()));
        camera.MakeCurrent();
        (e, _) = RenderEnvironment.Resolve();
        T.Check("making it current again takes the camera's back", ReferenceEquals(e, camEnv));
        T.Check("a camera that has neither is not an override", !plain.HasRenderOverrides && camera.HasRenderOverrides);

        world.Environment = null;
        node.Environment = null;
        camera.Environment = null;
        (e, _) = RenderEnvironment.Resolve();
        T.Check("with no environment anywhere there is none in force", e == null);
        world.FallbackEnvironment = worldEnv;
        (e, _) = RenderEnvironment.Resolve();
        T.Check("the world's fallback environment is the last resort", ReferenceEquals(e, worldEnv));
        PhysicalLights.Reset();
    }

    private static float Value(List<(string Key, float Value)> values, string key) => values.First(x => x.Key == key).Value;
    private static bool Has(List<(string Key, float Value)> values, string key) => values.Any(x => x.Key == key);

    private static void EnvironmentValues()
    {
        var off = new Env();
        T.Check("a glow that is off writes no glow keys", !Has(EnvironmentMapping.PostValues(off, null), "glowblend"));
        var glow = new Env { GlowEnabled = true };
        var v = EnvironmentMapping.PostValues(glow, null);
        T.Check("Godot's glow defaults: blend soft light (2), strength 1 and no cap are not written beyond the blend", Value(v, "glowblend") == 2f && !Has(v, "glowstrength") && !Has(v, "glowcap"));
        glow.GlowBlendMode = Env.GlowBlendModeEnum.Screen;
        glow.GlowStrength = 1.4f;
        glow.GlowHdrLuminanceCap = 8f;
        v = EnvironmentMapping.PostValues(glow, null);
        T.Check("a stated blend, strength and cap are the post keys glowblend, glowstrength, glowcap", Value(v, "glowblend") == 1f && T.Near(Value(v, "glowstrength"), 1.4f) && Value(v, "glowcap") == 8f);
        T.Check("the glow levels and the rest are stored", glow.GetGlowLevel(2) == 1f && glow.GetGlowLevel(3) == 0f && glow.GlowHdrScale == 2f && !glow.GlowNormalized);
        glow.SetGlowLevel(3, 0.5f);
        T.Check("and read back", glow.GetGlowLevel(3) == 0.5f);

        var amb = new Env { AmbientLightSource = Env.AmbientSource.Color, AmbientLightColor = new Color(0.2f, 0.4f, 0.6f, 1f), AmbientLightEnergy = 1.5f, AmbientLightSkyContribution = 0.25f };
        var a = EnvironmentMapping.AmbientOverride(amb);
        T.Check("a colour ambient source is the override: linear colour, energy, sky share", a is { } o && T.Near(o.Energy, 1.5f) && T.Near(o.SkyContribution, 0.25f) && T.Near(o.B, 0.3185f, 1e-2f) && T.Near(o.R, 0.0331f, 1e-2f));
        T.Check("another source states none", EnvironmentMapping.AmbientOverride(new Env()) == null && EnvironmentMapping.AmbientOverride(new Env { AmbientLightSource = Env.AmbientSource.Disabled }) == null);
        amb.BackgroundMode = Env.BGMode.Sky;
        T.Check("under the physical sky the energy rides the override and the sky light is 1", EnvironmentMapping.UsesPhysicalSky(amb) && EnvironmentMapping.SkyLight(amb) == 1f);
        amb.Sky = new Aver.Fabric.Sky { SkyMaterial = new ProceduralSkyMaterial() };
        T.Check("under the authored dome (a procedural sky) the old sky light scaling stays", !EnvironmentMapping.UsesPhysicalSky(amb) && EnvironmentMapping.SkyLight(amb) == 1.5f);
        T.Check("a flat colour background is the dome too", !EnvironmentMapping.UsesPhysicalSky(new Env { BackgroundMode = Env.BGMode.Color }));
        T.Check("disabled ambient light is a sky light of 0", EnvironmentMapping.SkyLight(new Env { AmbientLightSource = Env.AmbientSource.Disabled }) == 0f);

        var fog = new Env { VolumetricFogEnabled = true };
        T.Check("the volumetric fog values keep Godot's defaults and read back", fog.VolumetricFogLength == 64f && T.Near(fog.VolumetricFogAnisotropy, 0.2f) && fog.VolumetricFogTemporalReprojectionEnabled &&
            T.Near(fog.VolumetricFogTemporalReprojectionAmount, 0.9f) && fog.FogSunScatter == 0f && fog.ReflectedLightSource == Env.ReflectionSourceEnum.Bg);
    }

    private static void InstanceFlagValues()
    {
        var def = new MeshInstance3D();
        InstanceFlags d = def.ComputeInstanceFlags();
        T.Check("an ordinary mesh writes nothing: shadows on, static GI, no margin, no bias", d.IsDefault && d.RenderFlags == 0 && d.ShadowMode == 0 && d.GiMode == 0);
        var m = new MeshInstance3D
        {
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, GIMode = GeometryInstance3D.GIModeEnum.Dynamic, ExtraCullMargin = 2f, IgnoreOcclusionCulling = true, LodBias = 4f,
        };
        InstanceFlags f = m.ComputeInstanceFlags();
        T.Check("shadows off, dynamic GI, 2 m margin, occlusion opt-out and a bias of 4 are the engine's flag word 0x15, 200 cm and 2 stops",
            f.ShadowMode == 1 && f.GiMode == 1 && f.IgnoreOcclusion && f.RenderFlags == 0x15 && T.Near(f.CullMarginCm, 200f) && T.Near(f.LodBiasStops, 2f) && !f.IsDefault);
        T.Check("shadows only is mode 2, double sided casts as on", new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly }.ComputeInstanceFlags().ShadowMode == 2 &&
            new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.DoubleSided }.ComputeInstanceFlags().ShadowMode == 0);
        T.Check("disabled GI is mode 2 and static is the default", new MeshInstance3D { GIMode = GeometryInstance3D.GIModeEnum.Disabled }.ComputeInstanceFlags().GiMode == 2 && def.GIMode == GeometryInstance3D.GIModeEnum.Static);
        T.Check("a negative margin is clamped to nothing and lod_bias 0 is the coarsest level", new MeshInstance3D { ExtraCullMargin = -3f }.ComputeInstanceFlags().CullMarginCm == 0f &&
            new MeshInstance3D { LodBias = 0f }.ComputeInstanceFlags().LodBiasStops == -8f);
    }
}
