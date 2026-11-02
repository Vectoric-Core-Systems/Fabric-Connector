# Wave 6: particles, decals, sprites (Fabric side)

Status: built and tested headless on branch `parity/wave-6` (not merged). The engine side is in the engine repository's `docs/plans/GODOT_PARITY.md` (wave 6), `docs/PARTICLES.md` ("The module stack"), `docs/rendering/DECALS.md` and `docs/ABI.md`, on its branch `parity/wave-6`. Items here: F27 ParticleProcessMaterial / GPUParticles3D, F28 CPUParticles3D, F29 the collision and attractor nodes, F30 particle sprite sheets, P15 (emission shapes and modules) and P16 (trails) as the converter uses them, F25 the Decal node and its axes, P13 the decal properties, and F19 AnimatedSprite3D.

The rule of the plan holds: the engine only gained neutral capabilities with unchanged defaults (a ring shape, eight appended particle modules, a hue turn in the draw shader, decal emission / depth fade / normal fade / albedo mix); every Godot value, formula and mapping below is this repository's. A GPUParticles3D whose process material is a particles shader keeps the wave-3 path (`emitGpuParticles`, the shader as the custom stage); a project without any of these nodes converts as it did in wave 4.

## Where the numbers live

`native/formats.godot/include/aver/formats/godot/GodotParticles.hpp` holds the pure functions (the converter and `FabricWave6Test` use the same ones); `native/tools/fabric/FabricParticles.cpp` is the converter. Nothing below needs an engine to check.

| Function | Godot input | Result | Basis |
|---|---|---|---|
| `direction` | a direction, or a node-local vector | `(x, y, z) -> (-z, x, y)` | `FabricSpace.ToAverDirection`; determinant -1 |
| `rotationAxis` | a spin axis, a cross product | `(x, y, z) -> (z, -x, -y)` | the pseudovector rule the determinant forces: `cross(map a, map b) = rotationAxis(cross(a, b))` (a test pins it) |
| `extents` | a box's half sizes | `(abs z, abs x, abs y)` | the engine's local X is Godot's Z, Y is X, Z is Y |
| `reduceCurve` / `reduceGradient` | a Curve or Gradient | at most four stops: the points themselves when there are four or fewer, else four samples at even times | the engine's curves have four stops; **the shape between the samples, tangents and interpolation modes are lost** |
| `subUvPlan` | `particles_anim_h_frames` / `v_frames` / `loop`, `anim_speed_min/max`, `anim_offset_min/max`, the lifetime | a sheet played once over the life (speed 1, offset 0, no loop), else a frames-per-second rate at the **mean** speed (`speed * frames / lifetime`), a still sheet being 0.0001 fps, plus a start frame range | **judgement**: the engine has one rate per emitter, so a spread of speeds is lost; the offset range is kept as `spawn.animstart` |
| `trailPlan` | `trail_lifetime`, the mean initial speed | a ring of `points` positions, one pushed per `segment` cm, `segment = speed * trail_lifetime / (points - 1)` (at least 1 cm) | **judgement**: Godot's trail is time, the engine's is distance |
| `attractorStrengthCm` | `strength` | metres to centimetres | Godot's strength is a force in m/s^2 toward the centre |

## What the converter writes

### A GPUParticles3D with a ParticleProcessMaterial, and a CPUParticles3D (F27, F28)

One `Particles/<Project>_<Node>_<hash>.ocparticle` (version 2, `SIM auto`) and a `ParticleEmitter` component on the placement. A CPUParticles3D reads the same values from the node's own properties (`scale_amount_*` for `scale_*`, a `Curve` and a `Gradient` where the material has a `CurveTexture` and a `GradientTexture1D`); it takes no collider and no attractor, as in Godot. A GPUParticles3D whose material is neither of these (or none) emits nothing and says so.

| Godot | Engine | Rating and what is lost |
|---|---|---|
| `amount`, `lifetime`, `one_shot`, `emitting`, `amount_ratio` | `EMISSION rate burst max`, `LIFETIME` | rate `amount / lifetime`; one-shot is one burst; `explosiveness` (a burst every lifetime), `randomness`, `speed_scale`, `preprocess`, `local_coords`, `fixed_fps`, `visibility_aabb`, `draw_order` are reported and not applied |
| `lifetime_randomness` | `LIFETIME lifetime*(1 - r) lifetime` | converted |
| `emission_shape` point / sphere / sphere surface / box / ring | `SHAPE point / sphere / sphere shell / box / ring` | converted; `emission_shape_scale` multiplies the size; `emission_shape_offset` is not kept; **points and directed points (a texture) are unsupported**; a ring's `cone_angle` is not kept |
| `direction`, `spread`, `initial_velocity_min/max` | `DIRECTION`, `SPEED` (cm/s) | converted; `flatness`, `directional_velocity`, `radial_velocity` (4.3) are reported unsupported |
| `gravity` | `GRAVITY` (cm/s^2, world space) | converted |
| `scale_min/max`, `scale_curve` | `spawn.sizescale`, `SIZECURVE` over the draw quad's size | converted; the curve is cut to four stops (approximated when it had more); `scale_over_velocity_*` unsupported |
| `color`, `color_ramp`, the draw material's `albedo_color` | `COLOR` / `COLORCURVE` | the ramp times the colour times the material's albedo (sRGB to linear); the material's `vertex_color_use_as_albedo` off means the particle shows only the albedo, as in Godot; `color_initial_ramp` unsupported |
| `hue_variation_min/max` | `spawn.huevariation` (a per-particle turn of the colour wheel, drawn by the draw shader) | **approximated**: 1.0 is read as a full turn (Godot's range is -1..1); the rotation is about the grey axis in linear RGB, not Godot's exact matrix |
| `angle_min/max`, `angular_velocity_min/max` | `spawn.rotation` | converted |
| `linear_accel_*`, `radial_accel_*`, `damping_*` | `update.linearaccel`, `update.radialaccel`, `update.damping` | converted: a per-particle value drawn from the pair; damping is a constant deceleration that stops at zero, as Godot's |
| `tangential_accel_*` | `update.tangentialaccel` about the gravity axis (a spin axis, so `rotationAxis` of the normalised gravity; vertical when gravity is zero) | approximated: Godot's `cross(direction from the origin, gravity)` |
| `orbit_velocity_*` | `update.orbit` about the node's vertical axis (`rotationAxis(+Y)`) | approximated: the engine turns about the node's origin in world space |
| `turbulence_*` | `update.curlnoise` (frequency `1 / (noise_scale m)`, strength `noise_strength` m/s^2) | **approximated, for tuning by eye**: a curl-noise field, not Godot's simplex displacement |
| `collision_mode`, `collision_friction`, `collision_bounce` with the collision nodes | `update.collidesphere`, `update.collidebox`, a plane, or (hide on contact) kills | below |
| `attractor_interaction_enabled` with the attractor nodes | `update.attractor` | below |
| `sub_emitter_*` | not converted | unsupported: a sub emitter is another node, the engine's death and collide events spawn into another emitter of the same effect |
| draw pass: QuadMesh / PlaneMesh | a camera-facing sprite of the larger side | the quad is square; the other shapes are reported |
| draw pass: SphereMesh / BoxMesh | `RENDER mesh Meshes/sphere.ocmesh` / `cube.ocmesh` at the mesh's size | approximated: the engine's unit mesh, not the mesh's own geometry or material |
| draw pass: RibbonTrailMesh / TubeTrailMesh with `trail_enabled` | `RENDER ribbon trail <points> <segment> fade 1` | **approximated (P16 stand-in)**: a ribbon along each particle's last positions, as wide as the mesh, tail fading linearly; the mesh's width curve and a tube's roundness are not drawn; the length is the mean initial speed times `trail_lifetime` |
| any other mesh | a camera-facing sprite 1 m across | reported |
| `blend_mode` add / mix / sub / mul | additive / premultiplied / premultiplied / premultiplied | sub and mul are reported |
| `shading_mode` unshaded | `LIGHTING unlit`, `GI off` | converted |
| `billboard_mode`, `transform_align`, `particle_flag_align_y` | camera sprite; `SPRITE velocity stretch 0` for Y to velocity | approximated: a quad with billboarding off is drawn camera-facing |
| `albedo_texture` | `TEX {guid:...}` (the copied texture's asset id) | converted |
| `particles_anim_h_frames`, `v_frames`, `loop` (billboard mode particles) | `SUBUV c r frames life | fps n` + `spawn.animstart` (F30) | see `subUvPlan` above |

Modules are written in this order (velocity and position modules run in authored order): size scale, rotation, hue, animation start, linear / radial / tangential acceleration, damping, orbit, turbulence, then the scene's colliders and attractors. The emitter holds 16 modules; any beyond are dropped and reported.

### The collision and attractor nodes (F29)

`GPUParticlesCollisionSphere3D / Box3D / HeightField3D / SDF3D` and `GPUParticlesAttractorSphere3D / Box3D / VectorField3D` are Aver.Fabric classes that keep their properties. The converter finds them in the particle node's scene, takes their global pose and bakes them into the effect of **every GPUParticles3D whose process material enables them** (`collision_mode`, `attractor_interaction_enabled`). The cull mask and the follow-camera options are not read. Each node is reported once, on itself, with what it became:

| Node | Engine | Rating |
|---|---|---|
| CollisionSphere3D (`radius`, scaled by the node) | `update.collidesphere`; hide on contact is `kill=1` | converted (a uniform scale; the largest axis is used) |
| CollisionBox3D (`size`) | `update.collidebox` (a solid axis-aligned box), or `update.killvolume` for hide on contact | converted when the box is axis aligned; a rotated box is replaced by the axis-aligned box that bounds it (approximated) |
| CollisionHeightField3D | `update.collideplane` at the floor of its box | **approximated**: a height field needs a height texture on the compute layout, which the engine's kernels do not bind |
| CollisionSDF3D | none | **unsupported**: the same, for a 3D distance texture |
| AttractorSphere3D (`strength`, `attenuation`, `radius`) | `update.attractor` toward the node's centre, falloff `(1 - d / radius)^attenuation` | approximated: `directionality` is not kept |
| AttractorBox3D (`size`) | `update.attractor` as a sphere of the longest half-extent | approximated |
| AttractorVectorField3D | none | **unsupported**: a 3D texture of forces |

A collider or attractor moved at run time changes nothing: the effect is baked at convert time at the pose in the file.

### Decals (F25, P13)

The Decal node has been a pooled engine decal since wave 3; this wave settles its axes and adds the fields.

**The axes, derived** (from Godot's decal shader and the class reference, not guessed): the box's transform maps a surface point into 0..1; the albedo is read at `(x, z)`, the depth is `y`, the projector looks down local `-Y`, and the fade is `(1 - |y - 0.5| * 2)^fade` with `upper_fade` on the `+Y` half and `lower_fade` on the other. So the image's u runs along `+X`, its v (down the picture) along `+Z`: image up is `-Z`. The engine's decal projects along its `+X` with u along `+Y` and v-up along `+Z`, and `FabricSpace` maps Godot's `-Z` (forward) to the engine's `+X`, `+X` to `+Y` and `+Y` to `+Z`. The engine frame is therefore: projection `+X` = Godot `-Y`, u `+Y` = Godot `+X`, up `+Z` = Godot `-Z`; as a rotation applied to the node's frame, a quarter turn about the engine's Y. `Wave6Tests.DecalAxes` checks all three axes of the descriptor's rotation against that rule for eight node rotations (the existing wave-3 code had the right quarter turn; it was checked for the projection axis only).

| Godot | Engine (`DecalDesc`) | Rating |
|---|---|---|
| `size` | `SizeCm` (x depth = Godot y, y width = Godot x, z height = Godot z), scaled by the node | converted |
| `texture_albedo / normal / orm` | the three images; with no albedo the decal paints no flat colour (`Disable = Colour`) | converted |
| `texture_emission`, `emission_energy` | `EmissionTexture`, `EmissionEnergy` | converted (plain raster only, below) |
| `modulate` | tint (linear) and opacity = its alpha, for every channel | converted |
| `albedo_mix` | `AlbedoMix` (the base colour's own weight) | converted: earlier it was folded into the opacity, which also faded the normal and ORM |
| `upper_fade`, `lower_fade` | `UpperFade`, `LowerFade` as a depth curve in place of the soft edge on the projection axis | converted; the sides get a 2% soft edge where Godot has a hard one |
| `normal_fade` | `NormalFade` | converted |
| `distance_fade_*` | `FadeDistanceCm` | as before |
| `cull_mask` | kept | not applied: the engine paints every surface |

**The new shading is drawn by the plain raster pipeline only** (the engine's `AVER_DECAL_EXT`); ray-driven mode draws a decal as it did in wave 3, without emission, the two fades or the albedo mix.

### AnimatedSprite3D (F19)

The converter bakes one quad mesh and one StandardMaterial3D per distinct frame texture (the Sprite3D recipe: `pixel_size`, `centered`, `offset`, flips, `axis`, `modulate`, billboard, alpha cut, shading, sidedness, a frame being its whole texture), lists them in `frame_assets` (`texture|mesh|material`) and gives the node the quad of the frame it starts on. `SpriteFrames` converts as a value (`SpriteFrames.Animations` takes Godot's stored form: name, speed, loop and `{duration, texture}` frames) and the node swaps the entity's mesh and material when the frame changes. A frame texture a script adds later has no quad and keeps the previous picture. Sprite3D's own size reading now decodes JPEG and WebP (it read PNG headers only, so those sprites got a 64 x 64 stand-in).

## What the runtime does

`ParticleProcessMaterial`, `CPUParticles3D`, the four collision classes and three attractor classes are Aver.Fabric classes under Godot's names and defaults; the material and the CPU node keep their range pairs (`Set/GetParamMin/Max/Texture`, `Set/GetParticleFlag`) for scripts. A value changed at run time does not reach the running emitter: the effect is written at convert time. `GPUParticles3D.ProcessMaterial` returns a ParticleProcessMaterial that was set (it drives no shader registers). `Emitting`, `OneShot`, `Restart()` keep their wave-3 behaviour on the engine emitter.

## Not done

- Height-field and signed-distance-field collision, vector-field attractors, emission from a point texture (`points`, `directed_points`): each needs a texture or buffer on the particle compute layout (nine UAVs and one constant buffer, shared by every kernel), a GPU-tested change of its own. The converter approximates a height field by a plane and ignores the other three.
- Sub emitters; `flatness`; per-particle `anim_speed` spread; trail meshes bent along the path (a ribbon stands in); `local_coords`; `preprocess`.
- Run-time changes of a process material or a collider reaching the emitter.

## Results (2026-11-02, headless)

- **Corpus.** The 32 Godot 3D demos reconverted report-only (`w6-reconvert.sh`) against the wave 4 outputs: every demo still converts, and no demo has a new script error (`ik` 13, `variable_rate_shading` 3, one each in `physics_tests`, `procedural_materials`, `tonemap_color_correction`, as in wave 4). What moved: `particles` unsupported rows 41 to 22 and approximated 60 to 85 (the 18 ParticleProcessMaterial emitters and 12 CPUParticles3D nodes now write effects; the 9 collision and 1 attractor nodes are placed and baked); `sprites` approximated 124 to 110 (the 20 "texture size could not be read" rows are gone: WebP and JPEG are decoded; the AnimatedSprite3D draws); `antialiasing` unsupported 15 to 7, `platformer` 44 to 41, `truck_town` 19 to 16, `soft_body_physics` 4 to 3, `physics_interpolation` 2 to 1 (CPUParticles3D nodes); `lights_and_shadows` approximated 65 to 61 (Sprite3D sizes). The 69 Decal rows stay approximated by design (their wording changed). What the `particles` demo still reports unsupported: 7 `proximity_fade` rows (not this wave), 2 sub emitters (4 rows), 1 signed-distance-field collider, 1 `preprocess`, a second draw pass and a colour-initial ramp.
- **Fabric ctest:** 38 pass (37 before: `FabricWave6Test` is new). `FabricManagedTests` runs `Wave6Tests` (the decal axes for eight rotations, the P13 fields, the new classes, `SpriteFrames` in Godot's stored form).
- **Engine, for the record:** ctest with the GPU exclusion list, 512 pass (510 before: `ParticleRegressionTest` and `ParticleSpirvCompileTest`).
- **Nothing was run with a GPU or the editor.** The pictures (a ring, a hue variation, a flipbook, a ribbon trail, a decal's emission and fades, the animated sprite) are the headed checks below.

## Tests

- `FabricWave6Test` (tests/fabric): the pure mappings (the axis maps and their determinant, curves and gradients cut to four stops, the sprite sheet, the trail), then `Wave6Game` with each written `.ocparticle` read back by the engine's parser: a ring emitter with every range pair, a size curve, a colour ramp, a flipbook, three colliders and an attractor; a one-shot box emitter that dies on contact drawn as spheres; a trail; a CPUParticles3D; a default process material; a Decal; an AnimatedSprite3D; and the report rows.
- `Aver.Fabric.Tests` `Wave6Tests`: the decal's three axes for eight rotations, its P13 fields in the descriptor, the new classes under Godot's names, `GPUParticles3D` keeping a material, `SpriteFrames` taking Godot's stored form.
- Changed: `ConverterGapTests` (the decal's opacity and mix are separate), `EnvShaderTests` (a `ParticleProcessMaterial` is kept now).

## Headed checks for the owner

1. The `particles` demo converted: the fountain, flame and smoke emitters against Godot's (colour ramps, size curves, sprite sheets, hue variation), on a GPU tier and, with `SIM cpu`, on the CPU tier.
2. A ring emitter, an emitter with orbit and tangential acceleration, and one with turbulence: the turbulence frequency and strength are guesses to tune by eye.
3. Hide on contact and rigid collision against a sphere, a box and a plane collider; a sphere attractor pulling a fountain.
4. A trail particle (`Streak` in the fixture): the ribbon length and width against Godot's trail.
5. The `decals` demo: the axes (an asymmetric image on a floor, a wall and a ceiling; image up should match Godot's), the upper / lower fade, the normal fade, emission, `albedo_mix` below 1, in the plain raster pipeline and then in ray-driven mode (where the new fields do not show).
6. The `sprites` demo: the AnimatedSprite3D animating, and a Sprite3D with a WebP texture at the right size.

## Owner decisions

- The Decal node's look changed: a hard-ish 2% side edge and the depth fade curve replace the 10% soft edge, because Godot has no soft edge. Say if the soft edge should stay.
- Hue variation reads Godot's -1..1 as turns of the colour wheel; confirm against the demo, or give the exact matrix.
- The height-field, signed-distance-field and vector-field features need a change to the particle compute layout (a texture slot on every kernel). It is a GPU-risk change; say if it should be a wave of its own.
