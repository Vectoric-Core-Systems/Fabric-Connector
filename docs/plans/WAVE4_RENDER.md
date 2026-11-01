# Wave 4: rendering settings, lights, shadows, environment (Fabric side)

Status: built and tested headless on branch `parity/wave-4` (not merged, not pushed). The engine side is in the engine repository's `docs/plans/GODOT_PARITY.md` (wave 4) and `docs/ABI.md` ("World effects", `framework_render_abi.h`), on its branch `parity/wave-4`. Items here: F1 Viewport 3D rendering properties, F37 stored RenderingServer calls, F31 Camera3D / World3D environment and attributes, F21 physical light units and camera exposure, F22 light projector textures, F24 volumetric fog parameters, F32 mesh LOD threshold and bias, F23 the procedural sky as an opt-in sky shader, and the converter / managed mappings for the engine's wave 4 capabilities (sun cascades and shadow filter, per-instance shadow / GI / cull flags, ambient, glow, debanding).

The rule of the plan holds: the engine only gained neutral capabilities with unchanged defaults (project keys `RENDER.SHADOW*`, `RENDER.VOLFOG*`, post keys `glowblend`/`glowstrength`/`glowcap`/`deband`, the SKY ambient override, `RenderSettings`, `CMeshRenderer.renderFlags`/`cullMarginCm`/`lodBias`); every Godot value, formula and mapping below is this repository's. A project that states none of these settings converts as it did in wave 2, apart from the new report rows (`FabricWave4Test` checks it on `EnvGame`, and `LightsGame` for the one thing it does state).

## Where the numbers live

`native/formats.godot/include/aver/formats/godot/GodotRender.hpp` (the converter and its tests) and `managed/Aver.Fabric/Nodes/RenderMapping.cs` (the runtime) hold the same pure functions; `FabricWave4Test` and `Wave4Tests` pin the same values. Nothing below needs an engine to check.

| Function | Godot input | Result | Basis |
|---|---|---|---|
| `msaaSamples` | `msaa_3d` 0..3 | 1, 2, 4, 8 samples | the enum |
| `chooseAa` | `msaa_3d`, `use_taa` | the consistent set (below) | Aver's TAA needs MSAA 1 |
| `fsrSharpness` | `fsr_sharpness` | `2^-x`, 0..1 (1 sharpest) | the documented "halves with every 1.0" |
| `averSrPreset` | FSR scale below 1 | `RENDER.AVERSR` 1 quality (>= 0.63), 2 balanced (>= 0.545), 3 performance | **guess**: midpoints of AMD's FSR 1 ratios (0.67, 0.59, 0.5); AverSR's own ratios were not checked |
| `lodThresholdPixels` | `mesh_lod_threshold` | pixels, 0..64 (`RENDER.LODTHRESHOLD`) | both are the screen-space error in pixels |
| `lodBiasStops` | `lod_bias` multiplier | `log2`, -8 for 0, clamped to 8 (`CMeshRenderer.lodBias`) | +1 stop halves the engine's threshold, a larger multiplier keeps finer levels |
| `cascadeCount` | `directional_shadow_mode` 0/1/2 | 1, 2, 4 cascades | the enum |
| `shadowResolution` | `directional_shadow/size` | one cascade's edge: half the atlas (the whole atlas for one cascade), 512..4096, power of two | **judgement**: Godot's atlas holds the splits in a 2x2 or 1x2 division; `RENDER.SHADOWRES` is one cascade |
| `shadowFilterWidth` | `soft_shadow_filter_quality` 0..5 | 0, 0.5, 0.75, 1, 2, 3 texels | **judgement**: Godot's default (3, medium) is the engine's fixed 3x3 (1.0); the sample counts differ |
| `fitSplitLambda` / `fitSunShadow` | split fractions, max distance, camera near | the least-squares `RENDER.SHADOWSPLIT` | below |
| `physicalExposureNormalization` | aperture, shutter speed (1/s), ISO | `1 / (1.2 * N^2 * shutter * (100 / ISO))` | Godot's `CameraAttributesPhysical::calculate_exposure_normalization` (camera_attributes.cpp, read for its semantics); f/16, 1/100, ISO 100 is 1/30720 |
| `practicalExposureNormalization` | ISO | `ISO / 3072007` | the same file; the physical default at ISO 100 |
| `physicalShadingEnergy` | type, energy, lumens / lux, normalization | directional `E*I*n`, omni `E*I/(4 pi)*n`, spot `E*I/pi*n` | Godot's light storage (light_storage.cpp): the factors when physical units are on; off, every type is `E * pi` |
| `physicalAverIntensity` | the same | shading energy x 100000/3 (lux for the sun, candela at 1 m for a local light) | one engine radiance unit is 100000/3 lux; this is the calibration G4 R2 already used |
| `temperatureTint` | `light_temperature` K | linear rgb, brightest channel 1 | Godot's `_color_from_temperature`: the Krystek / Filament fit of the Planckian locus; the coefficients were read from its source |
| `glowBlend` | `glow_blend_mode` | the same number: 0 additive, 1 screen, 2 soft light, 3 replace, 4 mix | the enum order of both |

**The cascade fit.** Aver slices the shadow range at `sliceFar(p) = lambda * n * (f/n)^p + (1 - lambda) * (n + (f - n) * p)`, `p = i / N` (`VoxiRenderer.cpp`), `n` the camera near plane and `f` the shadow distance. Godot places split `i` at `n + split_i * (max_distance - n)`: the three `directional_shadow_split_*` values are independent fractions of the range, not cumulative (renderer_scene_cull.cpp). A slice position is linear in `lambda`, so the best fit over the `N - 1` inner splits is closed form and is exact for a scheme Aver can express. Godot's default splits (0.1, 0.2, 0.5) at 100 m with a 5 cm near plane fit to `lambda` about 0.51: more even than Aver's 0.85. One cascade has nothing to fit and writes no `RENDER.SHADOWSPLIT`. The camera near plane is the first camera's `near` (0.05 m when it states none).

## The consistent anti-aliasing set (F1)

Aver's TAA needs MSAA off (`RENDER.TAA 1` "needs MSAA 1; otherwise FSR 1 is used") and the engine's default MSAA is 4x. The mapping therefore is:

- `use_taa` on: `RENDER.TAA 1` **and** `RENDER.MSAA 1`, whatever `msaa_3d` says. If `msaa_3d` was above 0 the report says the multisampling was dropped. `use_taa` stated alone also writes `RENDER.MSAA 1`, or the engine's default 4x would block the TAA.
- `use_taa` off or unstated, `msaa_3d` stated: `RENDER.MSAA` 1, 2, 4 or 8, the raster pass only.
- A denoiser (the ray-traced tiers) works on the ray-traced signal; MSAA only multiplies the raster pass, so it is left as stated, not forced off. Unstated keys write nothing (Godot's own default is no MSAA; Aver's is 4x, unchanged).
- `screen_space_aa` stays `RENDER.NEURAA` as in wave 1.

At run time the window viewport (`Viewport.Msaa3D`, `UseTaa`) pushes the same pair through `RenderSettings.Set(Msaa/Taa)`; the engine applies MSAA at the next frame and stores TAA (the upscaler is chosen when the project starts).

## What the converter writes

### Project (`FabricProject.cpp`)

| Godot setting | Written | Rating |
|---|---|---|
| `rendering/anti_aliasing/quality/msaa_3d`, `use_taa` | `RENDER.MSAA`, `RENDER.TAA` (above) | converted; approximated when MSAA is dropped |
| `rendering/anti_aliasing/quality/use_debanding` | `deband 1` in every level's post volume (`WorldEnvironment`'s, or a `Debanding` volume on a level with a sky, a sun or a camera) | converted |
| `rendering/scaling_3d/mode` 1 or 2 with `scale` < 1 | `RENDER.AVERSR` preset | approximated (AverSR is not FSR; the thresholds are a guess) |
| `rendering/scaling_3d/scale` (bilinear) | nothing: no project key. `Viewport.ApplyProjectRender` pushes `RenderSettings.SetScale` when the game starts | approximated |
| `rendering/scaling_3d/fsr_sharpness` | pushed at start as `2^-x` (stored by the engine) | approximated |
| `rendering/mesh_lod/lod_change/threshold_pixels` | `RENDER.LODTHRESHOLD` (and `RENDER.LODSELECT 0` for 0) | converted; the editor's Play selects levels, the shipped runtime selects none |
| `rendering/occlusion_culling/use_occlusion_culling` | `RENDER.OCCLUSIONCULL` | converted; same limit |
| the start level's first sun: `directional_shadow_mode`, `_max_distance`, `_split_1..3` | `RENDER.SHADOWCASCADES`, `RENDER.SHADOWDIST` (m to cm), `RENDER.SHADOWSPLIT` (fit) | approximated: the raster cascades only, ray-traced shadows (the default from Medium) do not use them |
| `directional_shadow/size` | `RENDER.SHADOWRES` | approximated |
| `directional_shadow/soft_shadow_filter_quality` | `RENDER.SHADOWFILTER` | approximated |
| `positional_shadow/*`, `rendering/textures/light_projectors/filter`, `decals/filter` | kept in `Fabric/project.json` (no engine key: P4's local atlas size was not done) | approximated |
| `lights_and_shadows/use_physical_light_units` | read by the light conversion (below) | approximated |
| the start Environment's `volumetric_fog_length`, `_anisotropy`, `_temporal_reprojection_*` | `RENDER.VOLFOGLENGTH` (m to cm), `RENDER.VOLFOGANISO`, `RENDER.VOLFOGTEMPORAL` (0 when disabled) | converted, **only when stated**: Godot's defaults (64 m, 0.2, 0.9) differ from the engine's (200 m, 0.6, 0.85) and an unstated key keeps the engine's |

### Level (`FabricScene.cpp`)

- **Glow (P10).** `glow_blend_mode` -> `glowblend`, `glow_strength` -> `glowstrength` (written when not 1), `glow_hdr_luminance_cap` -> `glowcap` (written only when stated), `glow_intensity` -> `bloom` and `glow_hdr_threshold` -> `bloomthreshold` as before. **Decision for the owner:** Godot's default blend is *soft light*, so every glow-enabled Environment now writes `glowblend 2` (it was additive before); this is the one place a default Godot value changes a converted look. A glow's blend mode names the effect, which is why it is applied; the volumetric fog tunables above are stated-only. To go back, `FabricInternal.hpp` `EnvInfo::glowBlend` and `Environment.GlowBlendMode` default to 2 (and `-1` as "unstated" would drop the key). Kept and rated approximated: `glow_levels/N`, `glow_bloom`, `glow_hdr_scale`, `glow_normalized` (the engine has no per-level weights).
- **Ambient (P8).** `ambient_light_source` Color under Aver's physical sky writes the SKY record's `ambient r g b ambientenergy e ambientsky s` (linear colour, `ambient_light_energy`, `ambient_light_sky_contribution`; 1 is the sky alone, Godot's default) and sets the sky light to 1 so the energy is not applied twice. Under the authored two-colour dome (a flat colour, a procedural or panorama sky: what the converter writes for them) the override does nothing, so it is **not written**, the old mapping stays (the sky light scaled by the energy) and the row says approximated. `reflected_light_source` is kept (approximated).
- **Volumetric fog (F24, P9).** The three keys above. `fog_sun_scatter`, `volumetric_fog_gi_inject`, `_ambient_inject`, `_detail_spread`, `_sky_affect`, `_emission`, `_emission_energy` are kept and rated approximated; `fog_sky_affect`, `fog_aerial_perspective` and `fog_light_energy` are as before. **P6 / P11 fog depth:** the engine did not do the froxel work (local-light in-scatter, medium albedo / emission, a density texture), so `fog_mode` Depth stays what wave 1 made it (exponential fog starting at `fog_depth_begin`, about 95 percent at `fog_depth_end`, `fog_depth_curve` not modelled) and `FogMaterial.density_texture` stays unsupported.
- **Physical light units (F21).** With `use_physical_light_units` on, the sun record is `light_energy x light_intensity_lux x the camera attributes' exposure normalization` at 100000/3 lux an engine unit, tinted by `light_temperature`; local lights do the same in `Light3D.ComputeFields` at run time (`light_intensity_lumens`, `PhysicalLights.Normalization`, which follows the attributes in force: the current camera's, else the world's, and re-applies every light in the tree when it changes). **Approximation, stated:** Godot applies the exposure to everything (lights, emission, sky, in absolute units); here it is folded into the **lights only**, because the converted sky, emission and fog are not in absolute units and an exposure post key would crush them. A project with no camera attributes gets a normalization of 1, as Godot does (its lights are then very bright; so are Godot's). `CameraAttributes.exposure_multiplier` is also read for the physical camera now (it was only read for the practical one) and stays `RENDER.EXPOSURE`. With physical units off Godot ignores aperture, shutter speed, sensitivity and temperature for exposure and colour, and so does the conversion (rated converted).
- **Light projector (F22).** `light_projector` is the picture copied to `Textures/` and `Light3D.LightProjector` sets `CLight.cookie` (the id of its content path); a directional light has none, as in Godot. The engine projects cookies through the light's cone (point lights use the cookie frustum); cookie sampling on the raster local-light path is **unverified on a GPU**. Rated approximated for omni and spot. `RenderingServer.LightProjectorsSetFilter` and `DecalsSetFilter` are kept.
- **Per-instance flags (P5, F32).** `GeometryInstance3D.CastShadow`, `GIMode`, `ExtraCullMargin`, `IgnoreOcclusionCulling` and `LodBias` push `CMeshRenderer.renderFlags`, `cullMarginCm` and `lodBias` on every entity that draws the node (the placed meshes included), through the same `ApplyRenderMasks` the visibility range uses. Nothing is written for an ordinary mesh (the default word is zero and was never touched). Mapping: `cast_shadow` Off -> shadow mode 1, On and DoubleSided -> 0, ShadowsOnly -> 2; `gi_mode` Static -> 0 (Aver's default), Dynamic -> 1, Disabled -> 2 (both leave the voxel bake); margin metres to centimetres; `lod_bias` log2. Limits, each in the report: shadow mode Off acts on the raster cascades and the GI shadow map only, **not** the ray-traced sun shadow; shadows-only draws nothing for cameras but still shows in reflections (the hidden-from-owner semantics); `DoubleSided` is not modelled; `Disabled` GI is still lit by the real-time GI; the shipped runtime has no occlusion culling or LOD selection, so those two flags matter in the editor's Play. `visibility_range_fade_mode` is unchanged: the engine computes `fadeAmount` and does not dither (hard cut), approximated.
- **Camera environment (F31).** `Camera3D.Environment` and `Attributes` (`World3D.Environment`, `CameraAttributes`, `FallbackEnvironment`) resolve in Godot's order in `RenderEnvironment`: the current camera's own, else the world's (a `WorldEnvironment` node sets it), else the fallback. A camera's own environment is pushed when it becomes current (post layer `fabric.environment`, fog, sky light, ambient) and withdrawn when another camera takes over; it is **not** baked into the level (only the `WorldEnvironment`'s is), so on load it appears with the camera. Its sky is not a sky shader (only a `WorldEnvironment`'s `ShaderMaterial` sky is registered).

### The procedural sky, opt in (F23)

`AverAssetC fabric <project> <out> --procedural-sky` (**off by default**) turns a `ProceduralSkyMaterial` into a sky shader program (`Shaders/ProceduralSky.sky.ocgraph`, G6 SH3's `AVER_CUSTOM_SKY` path) instead of the two-colour dome; the level's SKY record stays the physical sky (as for any sky `ShaderMaterial`), the node's `Sky` holds the shader resource, and the report names it opt-in. The shader text (`kProceduralSkyShader` in `FabricShader.cpp`) is written from the property documentation of the class, **not copied from Godot's generated shader**: a vertical blend from the horizon colour to the top colour shaped by `sky_curve`, the same below the horizon with the ground colours, both scaled by `sky_energy_multiplier x background_energy_multiplier`, the first light's disc (`LIGHT0_SIZE` is the angular diameter in radians here) fading out to `sun_angle_max` shaped by `sun_curve`. It does not model the cover texture, the sky's own debanding or a second light. Stated colours, curves and energies are written into the register table; unstated ones use Godot's defaults, which the shader declares. It is measured on no GPU: a sky shader costs at every sky lookup (the background, reflections, ambient light five times, ray-driven mode), so it stays opt-in until the owner has timed it headed.

## What the runtime does

- **`Viewport`** (partial class, `ViewportRender.cs`): `Msaa3D`, `Msaa2D`, `UseTaa`, `ScreenSpaceAA`, `UseDebanding`, `UseOcclusionCulling`, `MeshLodThreshold`, `Scaling3DMode`, `Scaling3DScale`, `FsrSharpness`, `TextureMipmapBias`, `AnisotropicFilteringLevel`, `PositionalShadowAtlasSize`, `PositionalShadowAtlas16Bits`, `Get/SetPositionalShadowAtlasQuadrantSubdiv`, `DebugDraw`, `UseHdr2D`, `VrsMode`, `VrsUpdateMode`, `VrsTexture`, and Godot's enums (`Viewport.Msaa`, `Scaling3DModeEnum`, `AnisotropicFiltering`, `PositionalShadowAtlasQuadrantSubdiv`, `DebugDrawEnum`, `VrsModeEnum`, `VrsUpdateModeEnum`). The window's viewport starts from the project's settings (what a graphics menu reads) and forwards `Msaa3D`/`UseTaa` (the consistent pair), `Scaling3DScale`, `FsrSharpness`, `MeshLodThreshold`, `UseOcclusionCulling` and `UseDebanding` (post layer `fabric.viewport`, key `deband`); the rest is stored. A `SubViewport` keeps its values and forwards nothing (its configuration is the engine's cheap one). `SubViewport.MsaaMode` / `Scaling3DModeType` are gone in favour of `Viewport.Msaa` / `Scaling3DModeEnum`.
- **`RenderingServer`** (F37 and friends, kept as set, rated approximated): `EnvironmentSetSsaoQuality`, `EnvironmentSetSsilQuality`, `EnvironmentSetSsrRoughnessQuality`, `GiSetUseHalfResolution` (wave 1), `Environment.SsrMaxSteps` and the SSR fades (wave 1), now `DirectionalShadowAtlasSetSize` (the cascade size is read when the renderer starts), `DecalsSetFilter`, `LightProjectorsSetFilter`.
- **`Light3D`**: `LightIntensityLumens` / `LightIntensityLux` (one stored value, defaults 1000 and 100000), `LightTemperature` (6500), `LightProjector`.
- **`Environment`**: `GlowBlendMode`, `GlowStrength`, `GlowHdrLuminanceCap`, `GlowHdrScale`, `GlowNormalized`, `Get/SetGlowLevel`, `ReflectedLightSource`, `FogSunScatter`, `VolumetricFogLength`, `Anisotropy`, `TemporalReprojectionEnabled/Amount`, `Emission`, `EmissionEnergy`, `GiInject`, `AmbientInject`, `DetailSpread`, `SkyAffect`. `EnvironmentMapping.PostValues` adds `glowblend` (always with glow on), `glowstrength` (not 1), `glowcap` (once stated); `AmbientOverride` and `UsesPhysicalSky` decide the ambient override.
- **`CameraAttributes.GetExposureNormalization`**, `Camera3D.Environment` / `Attributes`, `World3D.Environment` / `CameraAttributes` / `FallbackEnvironment`.

## Not done by the engine, and so not here

Glow level weights, `glow_hdr_scale`, cascade blending and fade-out (`directional_shadow_blend_splits`, `_fade_start`), the positional (local-light) shadow atlas size and filter, fog sun scatter / sky affect / aerial perspective, volumetric fog local-light in-scatter / albedo / emission / `density_texture` (P6, P9, P11), the dithered fade of `visibility_range_fade_mode` (P20), pixel visibility fade, ambient under the authored dome, runtime TAA / upscaler switching. `Viewport.screen_space_aa` does not switch at run time. Rendering statistics (P18) have a C# class in the engine (`RenderSettings.GetStats`) but no Godot name in this wave: `RenderingServer.GetRenderingInfo` is not added.

## Tests

- `FabricWave4Test` (tests/fabric): the pure mappings (including the split fit recovering Aver's own scheme, the exposure formula, light units and temperature properties), then `Wave4Game` (every project key, the SKY ambient record, the post volume's glow and `deband`, node props, the projector copy, a camera's environment, the report ratings), `Wave4PhysGame` (physical sun, tint, exposure), `Wave4SkyGame` (the procedural sky off and on), and the default path on `EnvGame` and `LightsGame`. Fixtures: `tests/fabric/data/Wave4Game`, `Wave4PhysGame`, `Wave4SkyGame`.
- `Aver.Fabric.Tests` `Wave4Tests`: `RenderMapping`, the window viewport through a recording backend (and the project's settings as its defaults), a `SubViewport` leaving the window alone, the stored calls, light units / temperature / normalization, the projector path, the camera / world environment order, the glow / ambient / fog values and the per-instance flag words.

## Headed checks for the owner

1. `lights_and_shadows`, `global_illumination`, `sky_shaders`, `volumetric_fog`, `physical_light_camera_units` demos converted: the sun's shadows on the Low tier (cascades: count, distance, split) and with `RENDER.RTSHADOWRAYS`-style ray-traced shadows (which ignore them).
2. Glow demos: the bloom now blends as soft light (Godot's default); compare with `glow_blend_mode = 0` (additive) to see the difference, and decide whether to keep it.
3. A Godot project with physical light units and a `CameraAttributesPhysical`: the sun and local lights at the folded exposure (the sky and emissive surfaces are not scaled).
4. A mesh with `cast_shadow = 0` and one with `3`: the first still shadows under ray-traced sun shadows; the second is invisible and still shows in a mirror.
5. A light with a `light_projector` (spot and omni): the cookie on the raster local-light path.
6. `--procedural-sky` on a procedural-sky demo: the look against Godot's, and the cost on the target GPU at the background, in reflections and in ray-driven mode. Nothing in this repository has seen the picture.
7. A `Camera3D` with its own `Environment` made current at run time, then another camera: the fog and tonemap change and change back.
8. A settings-menu script that sets `get_viewport().scaling_3d_scale`, `msaa_3d`, `use_taa`, `mesh_lod_threshold`, `use_debanding`.

## Owner decisions

- Keep Godot's default glow blend (soft light) for a glow-enabled Environment, or keep additive until a headed look says otherwise.
- Physical light units: the exposure is folded into the lights only (the sky, emission and fog are not in absolute units). Say if the sky should follow (needs absolute sky and emission values).
- Make `--procedural-sky` the default only after it has been measured headed.
- Unstated volumetric fog tunables keep the engine's defaults (200 m, 0.6, 0.85), not Godot's (64 m, 0.2, 0.9).
