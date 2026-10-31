#pragma once
// The node tree Godot's glTF importer makes for a model file (names, classes, transforms, skeletons), so a .tscn that instances the model with
// editable children can name nodes inside it ("Player/Skeleton/Skeleton3D/Robot") and hang children on them. Behaviour as Godot 4 shows it in
// scenes; docs/plans/fabric/G3_SCENES.md.
#include "FabricMath.hpp"

#include <string>
#include <vector>

namespace aver::fabric {

struct ModelBone {
    std::string name;
    int parent = -1;   // index into the skeleton's bones, -1 for a root bone
    Xform rest;        // local rest pose, metres
    int gltfNode = -1; // the glTF node this bone is
};

struct ModelTreeNode {
    std::string name;
    std::string type;                 // Node3D, MeshInstance3D, Skeleton3D, BoneAttachment3D, AnimationPlayer
    int parent = -1;
    std::vector<int> children;        // in Godot's child order
    int gltfNode = -1;                // the glTF node this stands for; -1 for the nodes the importer adds
    Xform xf;                         // local transform in Godot's metres; identity for a node with no transform
    bool skinned = false;             // a mesh node that is moved below its Skeleton3D (the node's own transform is dropped)
    std::vector<ModelBone> bones;     // Skeleton3D
    std::string boneName;             // BoneAttachment3D
    // A CollisionShape3D made from the file's glTF physics (OMI_physics_body / OMI_physics_shape), in Godot's metres: BoxShape3D (size x y z), SphereShape3D (radius),
    // CapsuleShape3D and CylinderShape3D (radius, height), ConvexPolygonShape3D (the points) or ConcavePolygonShape3D (the triangles' vertices, three numbers each).
    std::string shapeClass;
    std::vector<double> shapeNums;
};

// One track of a glTF animation, decoded: a node's (or a bone's) translation, rotation or scale over time, in the file's metres and glTF axes
// (which are Godot's). `path` is Godot's track path from the model's root node: "Armature/Skeleton3D:Hips" for a bone, "Armature/Fan" for a node.
// A CUBICSPLINE channel is already sampled at 30 frames per second into a linear one; `step` marks a STEP channel (nearest key).
struct ModelAnimTrack {
    std::string path;
    int type = 1;                 // 1 position, 2 rotation (x y z w), 3 scale
    bool step = false;
    std::vector<double> times;    // seconds, ascending
    std::vector<double> values;   // 3 (4 for a rotation) numbers per time
};

struct ModelAnim {
    std::string name;
    double length = 0;            // the longest channel's last time
    std::vector<ModelAnimTrack> tracks;
};

struct ModelTree {
    std::vector<ModelAnim> anims;       // the file's animations, as Godot's importer puts them in the AnimationPlayer
    std::vector<ModelTreeNode> nodes;   // [0] is the root (its name is the instancing node's); creation order, not child order
    std::vector<std::string> notes;     // what the tree could not keep, for the report
    bool singleRoot = false;
};

// Reads a .glb or .gltf file's JSON (the buffers are not touched). False with `why` when the file is not glTF 2.
bool buildModelTree(const std::string& path, ModelTree& out, std::string& why);

}  // namespace aver::fabric
