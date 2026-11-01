// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Wave 7 (docs/plans/GODOT_PARITY.md): skeleton, animation and ragdoll. The pose mapping between Godot's bones and the engine's, PhysicalBone3D and the
// ragdoll nodes' defaults, the animation marker and section API, state machine and blend tree signals, Animation.copy_track / optimize, and the
// skeleton and mesh API remainder. The engine's own behaviour (the pose seam, the ragdoll, the player) is tested in C++; here is the Godot surface.

using Aver.Fabric;
using Aver.Framework;
using Collections = Aver.Fabric.Collections;

static class SkeletonWaveTests
{
    public static int RunAll()
    {
        return T.Suite(() =>
        {
            T.Run("Skeleton3D: the pose mapping to the engine's bones (identity, nodes above a bone, round trip)", PoseMapping);
            T.Run("Skeleton3D: bone metadata, skins, concatenated names, unparent and the aver_skeleton property", SkeletonApi);
            T.Run("PhysicalBone3D and PhysicalBoneSimulator3D: scene values, joint constraints, no body of their own, simulation without an engine", PhysicalBones);
            T.Run("MeshInstance3D: blend shape names and indices, skeleton path, skin", MeshApi);
            T.Run("LookAtModifier3D and the bone constraints: look at, copy, aim, convert", BoneConstraints);
            T.Run("AnimationNodeStateMachinePlayback: state_started and state_finished", StateSignals);
            T.Run("AnimationNodeBlendTree: node_changed", BlendTreeSignal);
            T.Run("AnimationNodeExtension: info array helpers", ExtensionNode);
            if (!AnimationTests.EnsureNative())
            {
                Console.WriteLine("Test: wave 7 animation API (SKIP: Aver.Anim.Abi was not found; build the Aver.Anim.Abi target)");
                return;
            }
            T.Run("Animation: markers (add, find, order, colour, remove) and AnimationPlayer sections", MarkersAndSections);
            T.Run("Animation: copy_track copies every key and its settings; optimize drops keys that interpolation reproduces", CopyAndOptimize);
        });
    }

    private static void PoseMapping()
    {
        var pose = new Transform3D(Basis.FromQuaternionScale(new Quaternion(new Vector3(0, 1, 0), 0.7f), new Vector3(1f, 1.5f, 1f)), new Vector3(0.1f, 0.4f, -0.2f));

        // A bone with nothing above it: its engine rest is its Godot rest, the map is the identity, and the pose is just the axes and units.
        Transform3D rest = new(Basis.FromQuaternionScale(new Quaternion(new Vector3(1, 0, 0), 0.3f), Vector3.One), new Vector3(0, 0.5f, 0));
        FabricSpace.ToAver(rest, out var rp, out var rq, out var rs);
        Transform3D restAsGodot = FabricSpace.FromAver(rp, rq, rs);
        Transform3D map = restAsGodot * rest.AffineInverse();
        BonePose p = Skeleton3D.ToEngine(map, pose);
        FabricSpace.ToAver(pose, out var ep, out var eq, out var es);
        T.Check("no nodes above: the engine pose is the pose in the engine's axes and centimetres",
            T.Near(p.Position.X, ep.X, 1e-3f) && T.Near(p.Position.Y, ep.Y, 1e-3f) && T.Near(p.Position.Z, ep.Z, 1e-3f) && T.Near(p.Scale.Y, es.Y, 1e-4f));
        T.Check("and its position is in centimetres (0.4 m up is 40)", T.Near(p.Position.Z, 40f, 1e-2f));

        // A bone at the top of its skin carries what is above it in the engine rest: the quotient removes it from the pose's change.
        Transform3D above = new(Basis.FromQuaternionScale(new Quaternion(new Vector3(0, 0, 1), 1.1f), Vector3.One), new Vector3(2f, 0f, 1f));
        FabricSpace.ToAver(above * rest, out var tp, out var tq, out var ts);
        Transform3D topRestAsGodot = FabricSpace.FromAver(tp, tq, ts);
        Transform3D topMap = topRestAsGodot * rest.AffineInverse();
        BonePose top = Skeleton3D.ToEngine(topMap, rest);
        T.Check("a top joint posed at its rest keeps the engine's rest (position)", T.Near(top.Position.X, tp.X, 1e-2f) && T.Near(top.Position.Y, tp.Y, 1e-2f) && T.Near(top.Position.Z, tp.Z, 1e-2f));
        BonePose moved = Skeleton3D.ToEngine(topMap, pose);
        FabricSpace.ToAver(above * pose, out var mp, out _, out _);
        T.Check("and posed away from it lands where the whole chain says", T.Near(moved.Position.X, mp.X, 1e-2f) && T.Near(moved.Position.Y, mp.Y, 1e-2f) && T.Near(moved.Position.Z, mp.Z, 1e-2f));

        // The way back.
        Transform3D back = Skeleton3D.FromEngine(topMap, moved);
        T.Check("the way back restores the Godot pose (origin)", T.Near(back.Origin, pose.Origin, 1e-3f));
        T.Check("and its rotation", T.Near(back.Basis.Orthonormalized().GetRotationQuaternion(), pose.Basis.Orthonormalized().GetRotationQuaternion(), 1e-3f));
        T.Check("and its scale", T.Near(back.Basis.GetScale(), pose.Basis.GetScale(), 1e-3f));
    }

    private static void SkeletonApi()
    {
        T.Reset();
        var sk = new Skeleton3D();
        int hip = sk.AddBone("hip");
        int leg = sk.AddBone("leg");
        sk.SetBoneParent(leg, hip);
        sk.SetBoneRest(hip, new Transform3D(Basis.Identity, new Vector3(0, 1, 0)));
        sk.SetBoneRest(leg, new Transform3D(Basis.Identity, new Vector3(0, -0.5f, 0)));
        T.Check("no metadata to begin with", sk.GetBoneMetaList(leg).Length == 0 && !sk.HasBoneMeta(leg, "tag"));
        sk.SetBoneMeta(leg, "tag", 3);
        sk.SetBoneMeta(leg, "side", "left");
        T.Check("metadata is listed and found", sk.GetBoneMetaList(leg).Select(n => n.ToString()).OrderBy(n => n).SequenceEqual(new[] { "side", "tag" }) && sk.HasBoneMeta(leg, "tag") && !sk.HasBoneMeta(hip, "tag"));
        T.Check("get_concatenated_bone_names", sk.GetConcatenatedBoneNames().ToString() == "hip,leg");

        Skin skin = sk.CreateSkinFromRestTransforms();
        T.Check("create_skin_from_rest_transforms: one bind per bone, named, inverse of the global rest", skin.GetBindCount() == 2 && skin.GetBindName(1).ToString() == "leg" && skin.GetBindBone(1) == 1 &&
            T.Near(skin.GetBindPose(1).Origin, new Vector3(0, -0.5f, 0), 1e-4f));
        SkinReference reference = sk.RegisterSkin(skin);
        T.Check("register_skin hands the skin back", reference.GetSkin() == skin);

        sk.UnparentBoneAndRest(leg);
        T.Check("unparent_bone_and_rest keeps the place in the world", sk.GetBoneParent(leg) < 0 && T.Near(sk.GetBoneRest(leg).Origin, new Vector3(0, 0.5f, 0), 1e-4f));

        T.Check("a Skeleton3D has no engine skeleton until the scene binds one", sk.EngineSkeletonAsset.Length == 0);
        sk.Set("aver_skeleton", "Meshes/hero/hero.ocskel");
        T.Check("the aver_skeleton property binds it", sk.EngineSkeletonAsset == "Meshes/hero/hero.ocskel");
        T.Check("and a managed host leaves the skinned meshes alone (no engine)", sk.SkinCount == 0 && !sk.IsSimulatingPhysics());
        sk.PhysicalBonesStartSimulation();
        T.Check("starting a simulation without an engine does nothing and says so", !sk.IsSimulatingPhysics() && T.Logs.Any(l => l.Message.Contains("physical_bones_start_simulation")));
        sk.PhysicalBonesStopSimulation();
        bool animated = sk.AnimatePhysicalBones;
        sk.SetAnimatePhysicalBones(false);
        T.Check("animate_physical_bones is stored", animated && !sk.GetAnimatePhysicalBones());
    }

    private static void PhysicalBones()
    {
        T.Reset();
        var sk = new Skeleton3D();
        sk.AddBone("pelvis");
        var sim = new PhysicalBoneSimulator3D { Name = "Sim" };
        var bone = new PhysicalBone3D { Name = "Physical Bone pelvis" };
        sk.AddChild(sim);
        sim.AddChild(bone);
        SceneTree.Instance.Root.AddChild(sk);

        T.Check("defaults match Godot's: no joint, mass 1, friction 1, bounce 0", bone.JointType == PhysicalBone3D.JointTypeEnum.None && bone.Mass == 1f && bone.Friction == 1f && bone.Bounce == 0f);
        bone.Set("bone_name", "pelvis");
        bone.Set("joint_type", 2);
        bone.Set("joint_constraints/swing_span", 20f);
        bone.Set("joint_constraints/twist_span", 30f);
        T.Check("a scene sets the name, the joint kind and the constraint values", bone.BoneName.ToString() == "pelvis" && bone.JointType == PhysicalBone3D.JointTypeEnum.Cone &&
            T.Near(bone.GetJointConstraint("swing_span").AsSingle(), 20f) && T.Near((float)bone.Get("joint_constraints/twist_span"), 30f));
        T.Check("get_bone_id finds the bone in the skeleton above", bone.GetBoneId() == 0);
        T.Check("it is not a body of the engine's physics", !bone.MakesOwnBody);
        T.Check("not simulating without an engine", !bone.IsSimulatingPhysics() && !sim.IsSimulatingPhysics());
        bone.ApplyCentralImpulse(new Vector3(0, 1, 0));
        bone.ApplyImpulse(new Vector3(0, 1, 0), new Vector3(0, 0, 1));
        T.Check("impulses while not simulating are ignored without an error", T.ErrorCount() == 0);
        sim.PhysicalBonesStartSimulation(new Collections.Array<StringName> { "pelvis" });
        sim.PhysicalBonesStopSimulation();
        sim.PhysicalBonesAddCollisionException(default);
        T.Check("the simulator's calls are accepted", T.ErrorCount() == 0);
    }

    private static void MeshApi()
    {
        T.Reset();
        var mi = new MeshInstance3D();
        T.Check("no blend shapes to begin with", mi.GetBlendShapeCount() == 0 && mi.FindBlendShapeByName("smile") == -1);
        mi.Set("aver_blend_shapes", "smile|blink|jaw");
        T.Check("the converter lists the mesh's blend shapes", mi.GetBlendShapeCount() == 3 && mi.FindBlendShapeByName("blink") == 1 && mi.GetBlendShapeName(2) == "jaw");
        mi.SetBlendShapeValue(1, 0.75f);
        T.Check("set and get by index and by name agree", T.Near(mi.GetBlendShapeValue(1), 0.75f) && T.Near(mi.GetBlendShapeValueByName("blink"), 0.75f));
        mi.SetBlendShapeValueByName("frown", 0.2f);
        T.Check("a name set by a script or animation joins the list", mi.GetBlendShapeCount() == 4 && mi.FindBlendShapeByName("frown") == 3);
        T.Check("a bad index reads 0", mi.GetBlendShapeValue(9) == 0f && mi.GetBlendShapeName(-1) == "");
        T.Check("the skeleton path defaults to the parent", mi.Skeleton.ToString() == "..");
        var sk = new Skeleton3D();
        sk.AddBone("b");
        sk.AddChild(mi);
        mi.Skin = sk.CreateSkinFromRestTransforms();
        T.Check("get_skin_reference reaches the skeleton's registration", mi.GetSkinReference()?.GetSkin() == mi.Skin);
    }

    // R / Skeleton (base, arm 1 m up) with the modifier below it, and a target T at (3, 1, 4).
    private static (Skeleton3D Sk, Node3D Target) ConstraintScene(SkeletonModifier3D mod)
    {
        T.Reset();
        var root = new Node3D { Name = "R" };
        var sk = new Skeleton3D { Name = "Skeleton" };
        int b = sk.AddBone("base");
        int a = sk.AddBone("arm");
        sk.SetBoneParent(a, b);
        sk.SetBoneRest(a, new Transform3D(Basis.Identity, new Vector3(0, 1, 0)));
        sk.ResetBonePoses();
        var target = new Node3D { Name = "T", Position = new Vector3(3, 1, 4) };
        mod.Name = "Mod";
        sk.AddChild(mod);
        root.AddChild(sk);
        root.AddChild(target);
        SceneTree.Instance.Root.AddChild(root);
        return (sk, target);
    }

    private static void BoneConstraints()
    {
        var look = new LookAtModifier3D { BoneName = "arm", ForwardAxis = SkeletonModifier3D.BoneAxis.PlusX, TargetNode = new NodePath("../../T") };
        var (sk, _) = ConstraintScene(look);
        sk.AdvanceModifiers(0.016);
        Vector3 pointing = sk.GetBoneGlobalPose(sk.FindBone("arm")).Basis * new Vector3(1, 0, 0);
        T.Check("look_at turns the bone's forward axis to the target (0.6, 0, 0.8)", T.Near(pointing, new Vector3(0.6f, 0, 0.8f), 1e-3f));
        T.Check("and leaves the bone where it was", T.Near(sk.GetBoneGlobalPose(sk.FindBone("arm")).Origin, new Vector3(0, 1, 0), 1e-4f));
        look.UseAngleLimitation = true;
        look.PrimaryLimitAngle = 0.2f;
        sk.ResetBonePoses();
        sk.AdvanceModifiers(0.016);
        Vector3 limited = sk.GetBoneGlobalPose(sk.FindBone("arm")).Basis * new Vector3(1, 0, 0);
        T.Check("an angle limit stops the turn that far from the rest direction", MathF.Acos(Math.Clamp(limited.Dot(new Vector3(1, 0, 0)), -1f, 1f)) <= 0.2f + 1e-3f);

        var copy = new CopyTransformModifier3D();
        (sk, _) = ConstraintScene(copy);
        copy.SettingCount = 1;
        copy.SetApplyBoneName(0, "arm");
        copy.SetReferenceNode(0, new NodePath("../../T"));
        copy.SetCopyFlags(0, CopyTransformModifier3D.TransformFlagPosition);
        copy.SetRelative(0, false);
        sk.AdvanceModifiers(0.016);
        T.Check("copy_transform puts the bone at the reference's position", T.Near(sk.GetBoneGlobalPose(sk.FindBone("arm")).Origin, new Vector3(3, 1, 4), 1e-3f));
        sk.ResetBonePoses();
        copy.SetAmount(0, 0.5f);
        sk.AdvanceModifiers(0.016);
        T.Check("and amount 0.5 goes half the way", T.Near(sk.GetBoneGlobalPose(sk.FindBone("arm")).Origin, new Vector3(1.5f, 1, 2), 1e-3f));
        T.Check("the settings read back", copy.GetSettingCount() == 1 && copy.GetApplyBoneName(0) == "arm" && copy.GetCopyFlags(0) == 1 && !copy.IsRelative(0) && T.Near(copy.GetAmount(0), 0.5f));

        var aim = new AimModifier3D();
        (sk, _) = ConstraintScene(aim);
        aim.SettingCount = 1;
        aim.SetApplyBoneName(0, "arm");
        aim.SetReferenceNode(0, new NodePath("../../T"));
        aim.SetForwardAxis(0, SkeletonModifier3D.BoneAxis.PlusY);
        sk.AdvanceModifiers(0.016);
        Vector3 up = sk.GetBoneGlobalPose(sk.FindBone("arm")).Basis * new Vector3(0, 1, 0);
        T.Check("aim points the bone's forward axis at the reference (0.6, 0, 0.8)", T.Near(up, new Vector3(0.6f, 0, 0.8f), 1e-3f));

        var convert = new ConvertTransformModifier3D();
        (sk, _) = ConstraintScene(convert);
        convert.SettingCount = 1;
        convert.SetApplyBoneName(0, "arm");
        convert.SetReferenceNode(0, new NodePath("../../T"));
        convert.SetReferenceTransformMode(0, ConvertTransformModifier3D.TransformMode.Position);
        convert.SetReferenceAxis(0, Vector3.Axis.X);
        convert.SetReferenceRangeMin(0, 0f);
        convert.SetReferenceRangeMax(0, 6f);
        convert.SetApplyTransformMode(0, ConvertTransformModifier3D.TransformMode.Scale);
        convert.SetApplyAxis(0, Vector3.Axis.Y);
        convert.SetApplyRangeMin(0, 1f);
        convert.SetApplyRangeMax(0, 3f);
        sk.AdvanceModifiers(0.016);
        T.CheckNum("convert_transform maps the reference's x (3 of 0..6) to the bone's y scale (2 of 1..3)", 2f, sk.GetBoneGlobalPose(sk.FindBone("arm")).Basis.GetScale().Y, 1e-3f);

        var scene = new CopyTransformModifier3D();
        scene.Set("setting_count", 2);
        scene.Set("settings/1/apply_bone_name", "arm");
        scene.Set("settings/1/amount", 0.25f);
        T.Check("a scene's settings/N/... properties fill the settings", scene.SettingCount == 2 && scene.GetApplyBoneName(1) == "arm" && T.Near(scene.GetAmount(1), 0.25f));
    }

    private static void StateSignals()
    {
        T.Reset();
        var pb = new AnimationNodeStateMachinePlayback();
        var log = new List<string>();
        pb.StateStarted += s => log.Add("start " + s);
        pb.StateFinished += s => log.Add("finish " + s);
        pb.EmitSignal(AnimationNodeStateMachinePlayback.SignalName.StateStarted, "x");
        T.Check("the signals exist and carry the state name", log.SequenceEqual(new[] { "start x" }));
    }

    private static void BlendTreeSignal()
    {
        T.Reset();
        var bt = new AnimationNodeBlendTree();
        var log = new List<string>();
        bt.NodeChanged += n => log.Add(n.ToString());
        bt.AddNode("anim", new AnimationNodeAnimation());
        bt.ConnectNode("output", 0, "anim");
        bt.RenameNode("anim", "walk");
        bt.DisconnectNode("output", 0);
        bt.RemoveNode("walk");
        T.CheckSeq("node_changed names the node for add, connect, rename (new name), disconnect and remove", new[] { "anim", "output", "walk", "output", "walk" }, log);
    }

    private static void ExtensionNode()
    {
        T.Check("is_looping reads the info array's third value", AnimationNodeExtension.IsLooping(new[] { 0f, 2f, 1f }) && !AnimationNodeExtension.IsLooping(new[] { 0f, 2f, 0f }));
        T.CheckNum("get_remaining_time is length minus position", 1.5f, AnimationNodeExtension.GetRemainingTime(new[] { 0.5f, 2f, 0f }));
        T.Check("a looping node never runs out, unless the loop is broken", float.IsPositiveInfinity(AnimationNodeExtension.GetRemainingTime(new[] { 0.5f, 2f, 1f })) &&
            T.Near(AnimationNodeExtension.GetRemainingTime(new[] { 0.5f, 2f, 1f }, true), 1.5f));
    }

    private static Animation Clip(float length)
    {
        var a = new Animation { Length = length };
        int t = a.AddTrack(Animation.TrackType.Value);
        a.TrackSetPath(t, "Target:energy");
        a.TrackInsertKey(t, 0, 0f);
        a.TrackInsertKey(t, length, length * 10f);
        return a;
    }

    private static void MarkersAndSections()
    {
        T.Reset();
        Animation a = Clip(4f);
        a.AddMarker("windup", 0.5);
        a.AddMarker("strike", 2.0);
        a.AddMarker("recover", 3.0);
        T.CheckSeq("markers are listed by time", new[] { "windup", "strike", "recover" }, a.GetMarkerNames());
        T.Check("has_marker and get_marker_time", a.HasMarker("strike") && !a.HasMarker("nope") && Math.Abs(a.GetMarkerTime("strike") - 2.0) < 1e-9);
        T.Check("get_marker_at_time, get_next_marker, get_prev_marker", a.GetMarkerAtTime(2.0) == "strike" && a.GetMarkerAtTime(2.5) == "" && a.GetNextMarker(2.0) == "recover" && a.GetPrevMarker(2.0) == "windup" &&
            a.GetNextMarker(3.5) == "" && a.GetPrevMarker(0.1) == "");
        a.SetMarkerColor("strike", new Color(1, 0, 0, 1));
        T.Check("marker colours", T.Near(a.GetMarkerColor("strike").R, 1f) && T.Near(a.GetMarkerColor("windup").G, 1f));
        a.AddMarker("strike", 9.0);
        T.Check("a taken name is refused with an error", T.ErrorCount("marker 'strike'") == 1 && Math.Abs(a.GetMarkerTime("strike") - 2.0) < 1e-9);
        a.RemoveMarker("windup");
        T.Check("remove_marker", !a.HasMarker("windup") && a.GetMarkerNames().Length == 2);
        a.AddMarker("windup", 0.5);

        var root = new Node3D { Name = "Root" };
        var p = new AnimationPlayer { Name = "P" };
        var lib = new AnimationLibrary();
        lib.AddAnimation("swing", a);
        p.AddAnimationLibrary("", lib);
        root.AddChild(p);
        SceneTree.Instance.Root.AddChild(root);

        T.Check("no section before one is asked for", !p.HasSection());
        p.PlaySection("swing", 1.0, 3.0);
        p.Advance(0);
        T.Check("play_section starts at the section's start", p.HasSection() && T.Near(p.CurrentAnimationPosition, 1f) && T.Near(p.GetSectionStartTime(), 1f) && T.Near(p.GetSectionEndTime(), 3f));
        p.Advance(1.0);
        T.Check("it advances inside the section", T.Near(p.CurrentAnimationPosition, 2f) && p.IsPlaying());
        p.Advance(5.0);
        T.Check("and stops at the section's end, not the animation's", T.Near(p.CurrentAnimationPosition, 3f) && !p.IsPlaying());

        p.PlaySectionBackwards("swing", 1.0, 3.0);
        T.Check("play_section_backwards starts at the end", T.Near(p.CurrentAnimationPosition, 3f));
        p.Advance(0); p.Advance(9.0);
        T.Check("and runs back to the start", T.Near(p.CurrentAnimationPosition, 1f));

        p.PlaySectionWithMarkers("swing", "strike", "recover");
        T.Check("play_section_with_markers uses the markers' times", T.Near(p.GetSectionStartTime(), 2f) && T.Near(p.GetSectionEndTime(), 3f));
        p.SetSectionWithMarkers("windup", "strike");
        T.Check("set_section_with_markers changes it in place", T.Near(p.GetSectionStartTime(), 0.5f) && T.Near(p.GetSectionEndTime(), 2f));
        p.ResetSection();
        T.Check("reset_section plays the whole animation again", !p.HasSection() && T.Near(p.GetSectionEndTime(), 4f));

        T.Logs.Clear();
        p.PlaySection("swing", 3.0, 2.0);
        p.PlaySectionWithMarkers("swing", "missing", "strike");
        p.PlaySection("nothing", 0, 1);
        T.Check("an empty section, a missing marker and a missing animation are errors", T.ErrorCount() == 3);
    }

    private static void CopyAndOptimize()
    {
        T.Reset();
        var src = new Animation { Length = 2f };
        int v = src.AddTrack(Animation.TrackType.Value);
        src.TrackSetPath(v, "N:x");
        src.TrackInsertKey(v, 0, 1f, 2f);
        src.TrackInsertKey(v, 2, 5f);
        src.TrackSetEnabled(v, false);
        int m = src.AddTrack(Animation.TrackType.Method);
        src.TrackSetPath(m, "N");
        src.TrackInsertKey(m, 1.0, new Collections.Dictionary { ["method"] = new StringName("hit"), ["args"] = new Collections.Array(new Variant[] { 7, "x" }) });
        var dst = new Animation { Length = 2f };
        src.CopyTrack(v, dst);
        src.CopyTrack(m, dst);
        T.Check("copy_track adds the tracks at the end, with path, type and flags", dst.GetTrackCount() == 2 && dst.TrackGetType(0) == Animation.TrackType.Value && dst.TrackGetPath(0) == new NodePath("N:x") && !dst.TrackIsEnabled(0));
        T.Check("with every key, its value and transition", dst.TrackGetKeyCount(0) == 2 && T.Near(dst.TrackGetKeyValue(0, 0).AsSingle(), 1f) && T.Near(dst.TrackGetKeyTransition(0, 0), 2f) && T.Near(dst.TrackGetKeyValue(0, 1).AsSingle(), 5f));
        T.Check("and a method key's name and arguments", dst.MethodTrackGetName(1, 0) == "hit" && dst.MethodTrackGetParams(1, 0).Count == 2 && dst.MethodTrackGetParams(1, 0)[0].AsInt32() == 7);
        T.Check("the source is untouched", src.GetTrackCount() == 2);
        src.CopyTrack(9, dst);
        T.Check("a bad track index copies nothing", dst.GetTrackCount() == 2);

        var o = new Animation { Length = 4f };
        int pt = o.AddTrack(Animation.TrackType.Position3D);
        o.TrackSetPath(pt, "N");
        for (int i = 0; i <= 4; i++) o.PositionTrackInsertKey(pt, i, new Vector3(i * 0.1f, 0, 0));   // a straight line: the middle keys carry nothing
        o.PositionTrackInsertKey(pt, 5, new Vector3(1.5f, 0.5f, 0));
        o.Length = 5f;
        int before = o.TrackGetKeyCount(pt);
        o.Optimize();
        T.Check("optimize drops the keys a line through their neighbours reproduces", before == 6 && o.TrackGetKeyCount(pt) == 3);
        T.Check("and keeps the ends and the corner", T.Near(o.PositionTrackInterpolate(pt, 0).X, 0f, 1e-3f) && T.Near(o.PositionTrackInterpolate(pt, 4).X, 0.4f, 1e-3f) && T.Near(o.PositionTrackInterpolate(pt, 5).Y, 0.5f, 1e-3f));
        T.Check("the interpolated values are unchanged", T.Near(o.PositionTrackInterpolate(pt, 2.0).X, 0.2f, 1e-3f));
        o.Compress();
        T.Check("track_is_compressed is false and compress changes nothing", !o.TrackIsCompressed(pt) && o.TrackGetKeyCount(pt) == 3);
    }
}
