# Wave 7: skeleton, animation, ragdoll

The Fabric half of wave 7 of the Godot parity plan (the engine's plan is `docs/plans/GODOT_PARITY.md` in the engine repository; the engine features are described in its
`docs/ANIM_POSE_SEAM.md`). Everything here maps Godot's API onto the neutral engine features; no Godot name is in the engine.

## What changed

| Item | What Fabric does |
|---|---|
| F13 skinned mesh binding | The converter finds the skeletons the engine's model importer wrote beside a glTF model's meshes, matches each `Skeleton3D` node to one by bone name, and writes the property `aver_skeleton` on the node and `aver_skinned` on the skinned meshes below it. At run time `MeshInstance3D` hands its entity to the skeleton (`Skeleton3D.AttachSkin`: the engine skeleton and an animator), and once a frame `Skeleton3D` pushes its bone poses to every skinned entity through the engine's pose seam (`Entity.SetBonePoses`). Scripts and `AnimationPlayer` bone tracks write the node's poses exactly as before; they now move vertices. |
| F14 glTF clips into the AnimationPlayer | `buildModelTree` reads the glTF animation channels from the file's buffers (not the importer's engine-space clips) and the converter writes each animation as an `.ocanim` property clip on the model's `AnimationPlayer` (`libraries`), with bone tracks on `Armature/Skeleton3D:Bone` paths and node tracks on node paths. |
| F12 PhysicalBone3D | `PhysicalBone3D` and `PhysicalBoneSimulator3D` classes. They are not physics bodies of their own: `physical_bones_start_simulation` starts the engine's ragdoll on the skeleton's first skinned mesh (the other skins follow), and the node's poses then follow the simulation. `PhysicalBone3D.linear_velocity` is the bone's velocity from its movement between physics steps. |
| P24 authored ragdoll data | The converter writes the scene's `PhysicalBone3D` nodes as the skeleton's `.ocragdoll` file (the engine's ragdoll override file): `off` for every bone no node names; for the named ones the capsule radius, mass, friction, bounce, joint kind (cone with its spans, hinge with its angle, slider as fixed, none/pin/6DOF as ball). |
| P26 markers and sections | `Animation.AddMarker` and the rest of the marker API; `AnimationPlayer.PlaySection`, `PlaySectionBackwards`, `PlaySectionWithMarkers`, `PlaySectionWithMarkersBackwards`, `SetSection`, `SetSectionWithMarkers`, `ResetSection`, `HasSection`, `GetSectionStartTime`, `GetSectionEndTime`. The converter keeps an Animation resource's `markers`. |
| F15 bone constraints | `LookAtModifier3D`, `CopyTransformModifier3D`, `AimModifier3D`, `ConvertTransformModifier3D` on the skeleton's pose model. |
| F16 | `AnimationNodeStateMachinePlayback.StateStarted` / `StateFinished`, `AnimationNodeBlendTree.NodeChanged`, `Animation.CopyTrack`, `Optimize`, `Compress` (a no-op), `TrackIsCompressed`. |
| F17 | `AnimationNodeExtension` (the base class and its info-array helpers; a script's processing hook is kept but a tree node's evaluation is the engine's, so it does not mix). `root_motion_local`, `reset_ends` and the custom timeline were already stored. |
| F18 | `Skeleton3D.GetBoneMetaList`, `HasBoneMeta`, `GetConcatenatedBoneNames`, `UnparentBoneAndRest`, `CreateSkinFromRestTransforms`, `RegisterSkin`, `Advance`, `ForceUpdateBoneChildTransform`, `AnimatePhysicalBones`; `Skin`, `SkinReference`; `MeshInstance3D.SkeletonPath`, `Skin`, `GetSkinReference`, `GetBlendShapeCount`, `FindBlendShapeByName`, `GetBlendShapeValue` / `SetBlendShapeValue` (the converter lists the mesh's blend shape names in `aver_blend_shapes`). |

## The pose mapping

A Godot bone pose is a full local transform in metres and Godot's axes; the engine's bone is a local transform in centimetres in the engine's axes, and for a joint at the
top of its skin it carries the nodes above it. The mapping goes through the rest poses: `engineLocal = toEngine(engineRestAsGodot * godotRest^-1 * godotPose)`
(`Skeleton3D.ToEngine` / `FromEngine`). For a bone with nothing above it that is `toEngine(godotPose)`; for a top joint the rests cancel what is above in the quotient.
`nodes/root_scale` is applied to the importer's skeleton (rest translations, inverse binds, sockets) as well as to the meshes, so the two stay in step.

## Limits

- glTF animation keys are the file's own for linear and step channels; cubic-spline channels are sampled at 30 frames per second. Godot's importer resamples and
  optimises its tracks, so in-between values and key counts can differ. Morph-weight and pointer channels are not read.
- A skeleton binds only where the importer's skeleton has all its bones among the node's bones by name; otherwise the skinned meshes stay in their bind pose (reported).
- Several skinned meshes under one skeleton are posed from the same node poses; a ragdoll runs on the first and the others follow it.
- `physical_bones_start_simulation` ignores its bone list: every bone the `.ocragdoll` makes a part simulates. Joint and body offsets, slider and 6DOF limits and the
  collision shape's own placement are not kept; box, convex and trimesh shapes become capsules by their radius.
- A ragdoll's `.ocragdoll` belongs to the skeleton asset: two scenes that author different ragdolls for one model replace each other (the last converted wins).
- The bone constraint modifiers apply the direct pose, not Godot's secondary-rotation solve, Euler limits or damping curves.
- Not done in this wave: spring bones (E24, moved to a later project) and COLLADA import (E23: the `ik` demo still needs it; reported, not built).

## Tests

`managed/Aver.Fabric.Tests/SkeletonWaveTests.cs` (the pose mapping, bone metadata, the ragdoll nodes, markers and sections, the constraint modifiers, copy_track and
optimize, the signals), `AnimationTreeTests` (state_started / state_finished through a real machine), `tests/fabric/src/FabricSkinTest.cpp` on
`tests/fabric/data/SkinGame` (a skinned glTF with three animations and a scene ragdoll through the converter).

Headed checks for the owner: the platformer player animating (skin and clips), the `ragdoll_physics` demo starting and settling, a bone attachment following a
posed bone.
