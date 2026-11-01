# Wave 2: images, textures and materials (Fabric side)

Status: built and tested headless on branch `parity/wave-2`. The engine side is in the engine repository's `docs/plans/GODOT_PARITY.md` (wave 2) and `docs/ABI.md` ("Finding an instance, sampler state, and the FX record's texture decode"), on its branch `parity/wave-2b`. Items: P1 sampler state, P2 surface material overrides, P14 script material parameters, F26 procedural texture resources and texture aliases, F35 smaller BaseMaterial3D conversions, E8 runtime `Image` / `ImageTexture`. E1 (WebP) is done in the main session, not here.

The rule of the plan holds: the engine gained neutral capabilities with unchanged defaults (a sampler word in the `.ocmat` FX record, an emissive scale, a channel choice, a per-entity override list, runtime textures); every Godot value and mapping below is this repository's.

## What the converter writes

| Godot | Written | Notes |
|---|---|---|
| `texture_filter` (BaseMaterial3D) | FX `texfilter=` `point` (0), `bilinear` (1), `pointmip` (2 and 4), `trilinear` (3); 5 writes nothing | Only a stated value is written: a material that does not name a filter keeps the engine's anisotropic sampler (Godot's default would be trilinear; the engine's is the better filter and costs no pipeline variant). Raster draws only: reflections, GI and the ray-driven pass keep the engine sampler, so a nearest-filtered surface looks smooth in a reflection of itself. 4 (nearest with anisotropy) is approximated as 2. |
| `texture_repeat` | FX `texaddr=clamp` when false | Stated `true` writes nothing (the engine wraps). |
| `rendering/textures/default_filters/anisotropic_filtering_level` | `RENDER.TEXTUREANISO` 1, 2, 4, 8, 16 | Read when the renderer starts. Unstated keeps the engine's 8. |
| `rendering/textures/default_filters/texture_mipmap_bias` | `RENDER.TEXTUREMIPBIAS` | -2 to 2. |
| `emission_energy_multiplier` | `PARAM emissiveEnergy` | The colour stays the `emissiveFactor`; their product is what the factor alone held before, so no emissive look changed (no calibration: decision 15 of the plan is still open). |
| `roughness_texture_channel` of a lone roughness map | FX `roughchan=` `r g b a gray` | A map used together with a metallic map is repacked into one image, as before. Godot's default is red, so a lone roughness map now reads its red channel, as Godot does (it was read as green before). |
| `vertex_color_is_srgb` | FX `vcsrgb=1` | With `vertex_color_use_as_albedo`. Raster draws. |
| `alpha_antialiasing_mode` | FX `alphaext=aa` | Existing. |
| `surface_material_override/N` | see below | |
| `GradientTexture1D`, `GradientTexture2D`, `NoiseTexture2D` (with `FastNoiseLite` and `color_ramp`), `CurveTexture`, `AtlasTexture` | a PNG under `Content/Textures/Generated/` (or, in a material slot, an image the material cook writes) | Gradients, curves and the atlas region are exact. The noise is this converter's own (`FabricProcTex.cpp`; value, cubic value, Perlin, simplex, cellular, the fractal modes, `invert`, `normalize`, `as_normal_map`, a colour ramp): Godot's character and range, not its texels. `seamless` blends four samples across the edges. `NoiseTexture3D` and `CurveXYZTexture` have no 2D picture and are reported unsupported. |
| `CanvasTexture` | its `diffuse_texture` | The normal and specular maps are not used. |
| `CompressedTexture2D`, `Texture` and other `Texture*` classes with a picture file | the file, as before | The runtime has the classes for scripts (below). |

A procedural texture is reachable wherever a texture goes: a material slot, a node property (`TextureRect.texture`), a `.tres` held by either.

### Surface overrides (P2)

On a mesh with one surface, `surface_material_override/0` is the `material_override` (it was before, now rated converted); another index is reported. On a mesh with several surfaces every `surface_material_override/N` stays a property of the node, and the plain mesh placement below the node carries a `COMPONENT SurfaceMaterials` record (`slot0=<material>` ... up to 15). `MeshInstance3D` applies the property to the same slots at run time (`SetSurfaceOverrideMaterial`). A `material_override` replaces every surface, as in Godot, so the per-surface records are not placed and not applied while one is set. Not covered: a level-of-detail, soft-body or GPU-cluster draw (a single draw of the whole mesh), instanced foliage, a streamed level's chunk-owned placements, a slot past 15, and an override material that is not an `.ocmat` file.

## What the runtime does

- **`Image`, `ImageTexture` (E8).** `Image` is the engine's CPU image (`Aver.Framework.Image`) under Godot's names: `Create`, `CreateFromData`, `LoadFromFile`, `GetPixel/SetPixel`, `Fill`, `FillRect`, `BlitRect`, `Resize`, `Convert`, `GetRegion`, `FlipX/Y`, `GenerateMipmaps` (a request: the texture builds the chain), `SavePng`. The formats the engine stores natively (L8, LA8, RGB8, RGBA8, RF, RGBAF) are kept; the others are held in the nearest of those and report the format they were made with. `ImageTexture.CreateFromImage / SetImage / Update` make or replace an engine runtime texture (`aver_fw_rtex_*`); a material names it `@rt:rtex.<key>`, the UI reads its GPU handle. The pixels reach the GPU the frame after they are set. Pixels are sRGB-decoded when sampled by a material; a data texture (a mask, a normal map) is therefore slightly off: a known approximation.
- **Texture classes (F26).** `Texture`, `CompressedTexture2D`, `PlaceholderTexture2D`, `AtlasTexture`, `CanvasTexture`, `GradientTexture1D/2D`, `CurveTexture`, `NoiseTexture2D`, `Noise`, `FastNoiseLite` (`GetNoise1D/2D/3D`, `GetImage`). The generated ones make their picture on first use (`GetImage`) and are drawn through a runtime texture.
- **Material parameters (P14).** `BaseMaterial3D` setters (`AlbedoColor`, `Metallic`, `Roughness`, `Emission*`, `Normal*`, `Uv1/2Scale/Offset`, `TextureFilter`, `TextureRepeat`, the channels, `VertexColor*`, `AlphaAntialiasingMode`, the albedo / emission / normal / roughness / metallic / AO textures and `SetTexture`) push to the engine material of the same name (`Aver.Scripting.Pbr.Find`): the file stem of the material's `.ocmat`. The engine keeps one material per name, so every mesh that draws it changes, as with a shared Godot resource. Changes made before the engine has loaded the material stay owed and are given when a mesh takes the material. `ReadOcmat` reads the file's FX record and emissive energy back into the properties (and pushes nothing).
- **Not done.** A material a script makes with `new StandardMaterial3D()` has no file and no engine material: its values are kept and it is not drawn. Per-instance copies of a material (`resource_local_to_scene`, `Duplicate` of an engine material) share the engine's single material by name. Height, detail, rim, clearcoat and the other FX groups are not pushed at run time (converter-time only). A viewport texture or runtime texture in the metallic slot together with a roughness texture needs the repacking only the converter does: the roughness map wins.

## Tests

- `Aver.Fabric.Tests`, `Wave2Tests.cs`: Image pixels, blit, resize, convert and PNG; ImageTexture with a recording host; gradient, curve, atlas, noise and canvas textures; the material push with a recording backend (find by name, handle caching, pending changes, stale handles, texture slots); the FX read-back; the surface-override names.
- `tests/fabric` `FabricWave2Test` on `tests/fabric/data/Wave2Game` (the converter as a process, real PNGs written over the fixture's stand-ins): sampler, emission, channel and sRGB flags in the `.ocmat`; the baked gradient, noise, atlas, canvas and curve pictures decoded and checked; the surface overrides in the level; the project's anisotropy; the report ratings. `FabricConvertTest` and `FabricMaterialFxTest` were updated for the emissive scale and the converted texture filter.

## Headed checks for the owner

- A pixel-art scene: a material with `texture_filter = nearest` draws crisp texels; the same material in a reflection or GI is smooth (the raster-only limit).
- `procedural_materials` demo: the gradient and noise textures show on their meshes.
- A multi-material mesh with `surface_material_override/1`: only that slot changes; `material_override` replaces all.
- A script that sets `albedo_color`, `uv1_offset` or `albedo_texture` on a loaded material, and an `ImageTexture` updated every frame on a material and in the UI.
