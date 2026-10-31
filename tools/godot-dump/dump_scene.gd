extends SceneTree
# What a Godot scene is, as Godot itself says it: the Godot half of the converter check (tools/godot-dump/README.md).
#
#   godot --headless --path <project> --script dump_scene.gd -- <out.json> [--with-tree] <res://scene.tscn> [<res://scene2.tscn> ...]
#
# Each scene is loaded and instantiated and then walked. By default the instance is never added to the tree, so no script's _ready, _process or signal runs; a
# node's global transform is worked out from the local ones the way Node3D does it (its immediate Node3D parent only, identity above anything else, top_level
# nodes on their own). --with-tree adds the instance below the root with every node's process mode disabled and waits two frames, for the nodes that build their
# data when they enter the tree (CSG roots); scripts' _ready still runs.
#
# The output is one JSON object: { "godot": version, "scenes": { "<res path>": { "nodes": [ ... ] } | { "error": text } } }. A node entry has path (relative to the
# scene root, "." for the root), name, class, script, is3d, local / world (a Transform3D as 12 numbers: the x, y and z axis, then the origin), visible,
# visible_in_tree, groups, instance (scene_file_path) and, by class, mesh, material, multimesh, gridmap, shape, light, camera and body entries.

const MAX_LISTED := 60000

var _out_path := ""
var _scene_paths: Array = []
var _with_tree := false
var _results := {}
var _pending: Array = []
var _frame := 0


func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	for a in args:
		if a == "--with-tree":
			_with_tree = true
		elif _out_path == "":
			_out_path = a
		else:
			_scene_paths.append(a)
	if _out_path == "" or _scene_paths.is_empty():
		printerr("dump_scene.gd: usage: -- <out.json> [--with-tree] <res://scene.tscn>...")
		quit(2)
		return
	if not _with_tree:
		for s in _scene_paths:
			_results[s] = _dump_scene(s, null)
		_finish()
		return
	for s in _scene_paths:
		var inst := _instantiate(s)
		if inst == null:
			_results[s] = {"error": "cannot load or instantiate"}
			continue
		_disable(inst)
		root.add_child(inst)
		_pending.append([s, inst])


func _process(_delta: float) -> bool:
	if not _with_tree:
		return false
	_frame += 1
	if _frame < 3:
		return false
	for p in _pending:
		_results[p[0]] = _dump_scene(p[0], p[1])
	_finish()
	return false


func _finish() -> void:
	var f := FileAccess.open(_out_path, FileAccess.WRITE)
	if f == null:
		printerr("dump_scene.gd: cannot write ", _out_path)
		quit(1)
		return
	f.store_string(JSON.stringify({"godot": Engine.get_version_info()["string"], "scenes": _results}))
	f.close()
	quit(0)


func _instantiate(path: String) -> Node:
	var res = load(path)
	if res == null or not (res is PackedScene):
		return null
	return (res as PackedScene).instantiate()


func _disable(n: Node) -> void:
	n.process_mode = Node.PROCESS_MODE_DISABLED
	for c in n.get_children():
		_disable(c)


func _dump_scene(path: String, inst: Node) -> Dictionary:
	var own := false
	if inst == null:
		inst = _instantiate(path)
		own = true
	if inst == null:
		return {"error": "cannot load or instantiate"}
	var nodes: Array = []
	_walk(inst, inst, Transform3D.IDENTITY, true, nodes)
	if own:
		inst.free()
	else:
		inst.get_parent().remove_child(inst)
		inst.free()
	return {"nodes": nodes}


func _rel_path(root_node: Node, n: Node) -> String:
	if n == root_node:
		return "."
	return str(root_node.get_path_to(n))


func _walk(root_node: Node, n: Node, parent_world: Transform3D, parent_visible: bool, out: Array) -> void:
	var d := {}
	d["path"] = _rel_path(root_node, n)
	d["name"] = str(n.name)
	d["class"] = n.get_class()
	d["script"] = _script_info(n)
	d["instance"] = n.scene_file_path
	var gs: Array = []
	for g in n.get_groups():
		if not str(g).begins_with("_"):
			gs.append(str(g))
	d["groups"] = gs
	var world := Transform3D.IDENTITY
	var vis_in_tree := true
	var is3d := n is Node3D
	d["is3d"] = is3d
	if is3d:
		var n3 := n as Node3D
		var local := n3.transform
		d["local"] = _xf(local)
		world = local if n3.top_level else parent_world * local
		d["world"] = _xf(world)
		d["visible"] = n3.visible
		vis_in_tree = n3.visible and parent_visible
		d["visible_in_tree"] = vis_in_tree
		d["top_level"] = n3.top_level
	_extras(n, world, d)
	out.append(d)
	for c in n.get_children():
		# A child sees its parent's transform only when the parent is a Node3D; above a plain Node it stands alone.
		if is3d:
			_walk(root_node, c, world, vis_in_tree, out)
		else:
			_walk(root_node, c, Transform3D.IDENTITY, true, out)


func _script_info(n: Node) -> Dictionary:
	var s = n.get_script()
	if s == null:
		return {}
	var r := {"path": s.resource_path}
	if s.has_method("get_global_name"):
		r["class_name"] = str(s.get_global_name())
	return r


func _xf(t: Transform3D) -> Array:
	return [t.basis.x.x, t.basis.x.y, t.basis.x.z, t.basis.y.x, t.basis.y.y, t.basis.y.z, t.basis.z.x, t.basis.z.y, t.basis.z.z, t.origin.x, t.origin.y, t.origin.z]


func _v3(v: Vector3) -> Array:
	return [v.x, v.y, v.z]


func _col(c: Color) -> Array:
	return [c.r, c.g, c.b, c.a]


func _aabb(a: AABB) -> Array:
	return [a.position.x, a.position.y, a.position.z, a.size.x, a.size.y, a.size.z]


func _simple(v):
	match typeof(v):
		TYPE_BOOL, TYPE_INT, TYPE_FLOAT, TYPE_STRING:
			return v
		TYPE_STRING_NAME:
			return str(v)
		TYPE_VECTOR2:
			return [v.x, v.y]
		TYPE_VECTOR3:
			return _v3(v)
		TYPE_VECTOR2I:
			return [v.x, v.y]
		TYPE_VECTOR3I:
			return [v.x, v.y, v.z]
		TYPE_COLOR:
			return _col(v)
		TYPE_PACKED_VECTOR2_ARRAY, TYPE_PACKED_VECTOR3_ARRAY, TYPE_PACKED_FLOAT32_ARRAY:
			return {"count": v.size()}
	return null


# The stored scalar properties of a resource, by name (a primitive mesh's size, radius, rings ...).
func _stored_props(res: Resource) -> Dictionary:
	var p := {}
	for info in res.get_property_list():
		if not (int(info["usage"]) & PROPERTY_USAGE_STORAGE):
			continue
		var name := str(info["name"])
		if name in ["resource_local_to_scene", "resource_name", "resource_path", "script", "material"]:
			continue
		var s = _simple(res.get(name))
		if s != null:
			p[name] = s
	return p


func _material_info(m: Material) -> Dictionary:
	if m == null:
		return {}
	var d := {"class": m.get_class(), "path": m.resource_path, "name": m.resource_name}
	if m is BaseMaterial3D:
		var b := m as BaseMaterial3D
		d["albedo_color"] = _col(b.albedo_color)
		d["albedo_texture"] = b.albedo_texture.resource_path if b.albedo_texture != null else ""
		d["metallic"] = b.metallic
		d["roughness"] = b.roughness
		d["transparency"] = int(b.transparency)
		d["cull_mode"] = int(b.cull_mode)
		d["emission_enabled"] = b.emission_enabled
		d["shading_mode"] = int(b.shading_mode)
	elif m is ShaderMaterial:
		var sm := m as ShaderMaterial
		d["shader"] = sm.shader.resource_path if sm.shader != null else ""
	return d


func _mesh_info(m: Mesh) -> Dictionary:
	if m == null:
		return {}
	var d := {"class": m.get_class(), "path": m.resource_path, "name": m.resource_name, "aabb": _aabb(m.get_aabb()), "surfaces": m.get_surface_count()}
	if m is PrimitiveMesh:
		d["props"] = _stored_props(m)
		d["material"] = _material_info((m as PrimitiveMesh).material)
	var mats: Array = []
	for i in m.get_surface_count():
		mats.append(_material_info(m.surface_get_material(i)))
	d["surface_materials"] = mats
	return d


func _extras(n: Node, world: Transform3D, d: Dictionary) -> void:
	if n is GeometryInstance3D:
		var g := n as GeometryInstance3D
		d["material_override"] = _material_info(g.material_override)
		d["cast_shadow"] = int(g.cast_shadow)
		d["layers"] = g.layers
	if n is MeshInstance3D:
		var mi := n as MeshInstance3D
		d["mesh"] = _mesh_info(mi.mesh)
		var ov: Array = []
		for i in mi.get_surface_override_material_count():
			ov.append(_material_info(mi.get_surface_override_material(i)))
		d["surface_overrides"] = ov
		if mi.mesh != null:
			d["world_aabb"] = _aabb(world * mi.mesh.get_aabb())
	if n is CSGShape3D:
		var c := n as CSGShape3D
		d["csg"] = {"operation": int(c.operation), "root": c.is_root_shape(), "use_collision": c.use_collision}
		if c.is_root_shape():
			var meshes = c.get_meshes()
			if meshes is Array and meshes.size() >= 2 and meshes[1] is Mesh:
				d["csg"]["mesh"] = _mesh_info(meshes[1])
				d["csg"]["world_aabb"] = _aabb(world * (meshes[1] as Mesh).get_aabb())
	if n is MultiMeshInstance3D:
		d["multimesh"] = _multimesh_info((n as MultiMeshInstance3D).multimesh, world)
	if n is GridMap:
		d["gridmap"] = _gridmap_info(n as GridMap, world)
	if n is Light3D:
		var l := n as Light3D
		var li := {"energy": l.light_energy, "color": _col(l.light_color), "shadow": l.shadow_enabled}
		if l is OmniLight3D:
			li["range"] = (l as OmniLight3D).omni_range
		if l is SpotLight3D:
			li["range"] = (l as SpotLight3D).spot_range
			li["angle"] = (l as SpotLight3D).spot_angle
		d["light"] = li
	if n is Camera3D:
		var cam := n as Camera3D
		d["camera"] = {"fov": cam.fov, "near": cam.near, "far": cam.far, "current": cam.current, "projection": int(cam.projection), "keep_aspect": int(cam.keep_aspect)}
	if n is CollisionShape3D:
		var cs := n as CollisionShape3D
		var sh := {"disabled": cs.disabled}
		if cs.shape != null:
			sh["class"] = cs.shape.get_class()
			sh["props"] = _stored_props(cs.shape)
		d["shape"] = sh
	if n is CollisionObject3D:
		var co := n as CollisionObject3D
		var b := {"layer": co.collision_layer, "mask": co.collision_mask}
		if n is RigidBody3D:
			b["mass"] = (n as RigidBody3D).mass
			b["gravity_scale"] = (n as RigidBody3D).gravity_scale
		d["body"] = b


func _multimesh_info(mm: MultiMesh, _world: Transform3D) -> Dictionary:
	if mm == null:
		return {}
	var d := {"count": mm.instance_count, "visible_count": mm.visible_instance_count, "format": int(mm.transform_format), "use_colors": mm.use_colors,
		"use_custom_data": mm.use_custom_data, "mesh": _mesh_info(mm.mesh)}
	if mm.transform_format == MultiMesh.TRANSFORM_3D:
		# A headless Godot has a dummy renderer, which keeps no instance data: get_instance_transform and buffer answer identity and empty. The table is read from the
		# resource's own file instead (the buffer property, in Godot's documented layout: three rows of the basis with the origin as the fourth column, then the colour
		# and the custom data when used), so the numbers are the file's and not the engine's. "transforms_source" says which.
		var from_file := _buffer_from_file(mm)
		var list: Array = []
		if from_file.size() > 0:
			var stride: int = 12 + (4 if mm.use_colors else 0) + (4 if mm.use_custom_data else 0)
			var n_file: int = from_file.size() / stride
			for i in min(n_file, MAX_LISTED):
				var o: int = i * stride
				var basis := Basis(Vector3(from_file[o], from_file[o + 4], from_file[o + 8]), Vector3(from_file[o + 1], from_file[o + 5], from_file[o + 9]), Vector3(from_file[o + 2], from_file[o + 6], from_file[o + 10]))
				list.append(_xf(Transform3D(basis, Vector3(from_file[o + 3], from_file[o + 7], from_file[o + 11]))))
			d["transforms_source"] = "file"
			d["file_instances"] = n_file
		else:
			for i in min(mm.instance_count, MAX_LISTED):
				list.append(_xf(mm.get_instance_transform(i)))
			d["transforms_source"] = "engine"
		d["transforms"] = list
	return d


# The buffer = PackedFloat32Array(...) of a MultiMesh sub-resource (path "res://scene.tscn::id") or of a .tres, read as text.
func _buffer_from_file(mm: MultiMesh) -> PackedFloat32Array:
	var out := PackedFloat32Array()
	var rp := mm.resource_path
	if rp == "":
		return out
	var file := rp
	var rid := ""
	if "::" in rp:
		file = rp.get_slice("::", 0)
		rid = rp.get_slice("::", 1)
	var text := FileAccess.get_file_as_string(file)
	if text == "":
		return out
	var lines := text.split("\n")
	var inside := rid == ""
	for line in lines:
		if line.begins_with("["):
			inside = rid == "" or (line.begins_with("[sub_resource") and (("id=\"" + rid + "\"") in line or ("id=" + rid + "]") in line or ("id=" + rid + " ") in line))
			continue
		if inside and line.begins_with("buffer = PackedFloat32Array("):
			var body := line.substr(line.find("(") + 1)
			body = body.substr(0, body.rfind(")"))
			for tok in body.split(","):
				var t := tok.strip_edges()
				if t != "":
					out.append(float(t))
			return out
	return out


func _gridmap_info(g: GridMap, _world: Transform3D) -> Dictionary:
	var d := {"cell_size": _v3(g.cell_size), "cell_scale": g.cell_scale, "center": [g.cell_center_x, g.cell_center_y, g.cell_center_z]}
	var lib := g.mesh_library
	var items := {}
	if lib != null:
		for id in lib.get_item_list():
			items[str(id)] = {"name": lib.get_item_name(id), "mesh": _mesh_info(lib.get_item_mesh(id)), "mesh_transform": _xf(lib.get_item_mesh_transform(id))}
	d["items"] = items
	var cells: Array = []
	var used := g.get_used_cells()
	for pos in used:
		if cells.size() >= MAX_LISTED:
			break
		var item := g.get_cell_item(pos)
		var basis := g.get_cell_item_basis(pos).scaled(Vector3(g.cell_scale, g.cell_scale, g.cell_scale))
		var t := Transform3D(basis, g.map_to_local(pos))
		cells.append({"pos": [pos.x, pos.y, pos.z], "item": item, "orientation": g.get_cell_item_orientation(pos), "xform": _xf(t)})
	d["cells"] = cells
	d["cell_count"] = used.size()
	return d
