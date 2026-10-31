# godot-dump: the converter checked against Godot itself

`dump_scene.gd` asks Godot what a scene is. `FabricGodotCompareTest` (in `tests/fabric`) reads a converted level back into Godot space and compares it with that answer. Together they say whether the converter put every node, transform, mesh, material and instance where Godot has it.

## The Godot half: `dump_scene.gd`

```
godot --headless --path <project> --script tools/godot-dump/dump_scene.gd -- <out.json> [--with-tree] res://main.tscn [res://other.tscn ...]
```

Only ever `--headless`. Each scene is loaded and instantiated, not added to the tree, so no script's `_ready`, `_process` or signal runs. For every node the JSON has its path, class, script, local and global transform (the global one worked out from the local ones the way `Node3D` does: its immediate `Node3D` parent only, `top_level` nodes on their own), `visible` and visibility in the tree, groups, and by class the mesh (class, resource path, primitive mesh properties, local box, surface materials), the material override and surface overrides, a mesh node's box in world space, CSG flags, lights, cameras, collision shapes and bodies. `MultiMeshInstance3D` gets its instance table and `GridMap` its cells, items and the cell transforms.

- `--with-tree` adds the instance below the root with every node's process mode disabled and waits two frames, for the nodes that build their data on entering the tree (a CSG root's baked mesh). A script's `_ready` still runs, so a script that moves a node in `_ready` moves it in the dump too.
- A headless Godot has a dummy renderer that keeps no instance data: `MultiMesh.get_instance_transform` answers identity and the buffer is empty. The table is read from the resource's own file instead (`transforms_source: "file"` in the dump). The comparison of MultiMesh and GridMap transforms therefore checks the converter's reading of the buffer layout against the file, not against Godot's engine.
- Godot writes a `.godot` folder and imports into the project it opens. Run it on a **copy** of the project (the comparer's `--work` does that).

## The converter half: `FabricGodotCompareTest`

```
FabricGodotCompareTest compare --converted <out>/<Project> --godot <godot exe> --godot-project <project> --work <scratch dir> [--scenes res://a.tscn,res://b.tscn] [--max 12] [--with-tree]
FabricGodotCompareTest compare --converted <out>/<Project> --dump <json> [--scenes ...]
FabricGodotCompareTest meshinfo <file.ocmesh>
```

The first form copies the Godot project to `--work`, imports it, runs `dump_scene.gd` on every scene the converter recorded in `Fabric/nodes.json` (or `--scenes`), and compares each with `Content/Maps/<scene>.ocworld`. Exit 1 when any scene has a mismatch. `run_compare.ps1` and `run_compare.sh` are the same call.

What is compared, per scene, with tolerances of 1e-3 m, 1e-3 rad and 1e-3 in scale:

| Kind | Meaning |
|---|---|
| `missing` / `extra` | a node Godot has and the level lacks, or the other way round |
| `class` / `script` | the Godot class (a carrier's `_class`, else the class `Fabric/nodes.json` records), and whether a script is attached |
| `xform` | the global transform, built from the placement lines through the inverse of the axis map (`AxisMap.hpp`). Not compared for a mesh node of an imported model: the Aver importer bakes the node's rotation and scale into the vertices, so its box is compared instead |
| `visible` | visibility in the tree, and the placed mesh's `hidden` token |
| `mesh` | the placed mesh's box in world space against Godot's (a node turned off the axes: the level's box must lie inside Godot's box of the turned local box and be at least half of it) |
| `material` | the first surface's albedo against the `.ocmat` the level names |
| `instances` | a MultiMesh's count, every instance transform and `visible_instance_count`; a GridMap's cells as a set of transforms |
| `prop` | numbers both sides state: light energy and range, camera fov / near / far, collision layer, mask and mass, shape class and size |

Documented differences are listed apart and are not mismatches: `synthetic` (the converter's own `_CSGCollision` and GridMap item nodes), `folded` (a CSG shape below another CSG shape is part of its root's mesh), `substituted` (`CollisionPolygon3D` is a `CollisionShape3D`), `approximated` (a model's material colour is the engine importer's 1/2.2 gamma of Godot's sRGB colour) and `inferred`.

## As a test

`FabricGodotCompareTest` with no argument converts `tests/fabric/data/PlacedGame` and compares it with what Godot says, written out by hand; then makes that answer wrong in each way and checks that each is found. With the environment variable `GODOT_EXE` set to a Godot 4 editor binary it also runs Godot `--headless` on the fixture and compares for real; without it that part is skipped.
