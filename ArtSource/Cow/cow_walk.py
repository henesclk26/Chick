"""Cow (Sketchfab, kenchoo, Sketchfab Standard licence): adds an in-place Walk cycle to the source glTF's
Idle and Eating clips and exports everything as Assets/Art/Cow/Cow.glb.

Run inside Blender: exec(open(path).read(), {"__file__": path}).
The rig faces -Y with Z up. Hooves are skinned to the free IK bones (IKFrontLeg/IKBackLeg + FF/FFB), not to
the leg chain, so each frame those are pinned to the lower leg's posed tip after the leg is swung.
"""
import math
import os

import bpy
from mathutils import Matrix

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SOURCE = os.path.join(ROOT, "ArtSource", "Cow", "source", "scene.gltf")
OUTPUT = os.path.join(ROOT, "Assets", "Art", "Cow", "Cow.glb")

FRAMES = 30  # 1.25 s at 24 fps
STANCE = .65  # share of the cycle each hoof is on the ground
# Lateral-sequence walk: left hind, left fore, right hind, right fore, a quarter cycle apart.
LEGS = {
    # name: (swinging bone, flexing bone, hoof IK bone, phase, swing deg, flex deg)
    "BL": ("BackLeg.L_15", "BackLowerLeg.L_13", "IKBackLeg.L_35", 0.0, 13.0, 30.0),
    "FL": ("FrontUpperLeg.L_6", "FrontLowerLeg.L_5", "IKFrontLeg.L_37", .25, 15.0, 45.0),
    "BR": ("BackLeg.R_19", "BackLowerLeg.R_17", "IKBackLeg.R_39", .5, 13.0, 30.0),
    "FR": ("FrontUpperLeg.R_9", "FrontLowerLeg.R_8", "IKFrontLeg.R_41", .75, 15.0, 45.0),
}


def leg_angles(phase, swing, flex):
    """Swing about the side axis (+ = hoof backwards) and the knee flex for one leg at a cycle phase."""
    if phase < STANCE:  # planted: the body walks over the hoof at constant speed
        return -swing + 2 * swing * phase / STANCE, 0.0
    t = (phase - STANCE) / (1 - STANCE)
    return swing * math.cos(math.pi * t), flex * math.sin(math.pi * t)


def turn(bone, axis, degrees):
    """Pose-space quaternion turning a bone about an armature-space axis."""
    rest = bone.matrix_local.to_3x3()
    return (rest.inverted() @ Matrix.Rotation(math.radians(degrees), 3, axis) @ rest).to_quaternion()


def import_source():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o)
    for collection in (bpy.data.armatures, bpy.data.meshes, bpy.data.actions, bpy.data.materials):
        for item in list(collection):
            collection.remove(item)
    bpy.ops.import_scene.gltf(filepath=SOURCE, disable_bone_shape=True)
    return next(o for o in bpy.data.objects if o.type == "ARMATURE")


def build_walk(arm):
    # Pose with nothing animating (so updates cannot overwrite it), record the keys, then write them.
    ad = arm.animation_data
    for track in ad.nla_tracks:
        track.mute = True
    ad.action = None
    bones = arm.pose.bones
    keyed = set()
    keys = []
    for pb in bones:
        pb.matrix_basis = Matrix.Identity(4)
    for frame in range(FRAMES + 1):
        p = (frame % FRAMES) / FRAMES
        for swing_name, flex_name, hoof_name, phase, swing, flex in LEGS.values():
            a, f = leg_angles((p + phase) % 1, swing, flex)
            bones[swing_name].rotation_quaternion = turn(bones[swing_name].bone, "X", a)
            bones[flex_name].rotation_quaternion = turn(bones[flex_name].bone, "X", f)
            keyed.update((swing_name, flex_name))
        # Two steps per side per cycle: the back bobs and the head nods twice, the tail sways once.
        bob = math.cos(4 * math.pi * p)
        body = bones["Body_33"]
        body.location = body.bone.matrix_local.to_3x3().inverted() @ (Matrix.Identity(3).col[2] * .05 * bob)
        bones["Neck1_3"].rotation_quaternion = turn(bones["Neck1_3"].bone, "X", 2.5 * bob)
        bones["Head_0"].rotation_quaternion = turn(bones["Head_0"].bone, "X", -2 * bob)
        bones["Tail1_27"].rotation_quaternion = turn(bones["Tail1_27"].bone, "Z", 7 * math.sin(2 * math.pi * p))
        keyed.update(("Neck1_3", "Head_0", "Tail1_27"))
        bpy.context.view_layer.update()
        # Pin each hoof rigidly to its lower leg's posed frame.
        for _, flex_name, hoof_name, *_ in LEGS.values():
            lower, hoof = bones[flex_name], bones[hoof_name]
            hoof.matrix = lower.matrix @ lower.bone.matrix_local.inverted() @ hoof.bone.matrix_local
            bpy.context.view_layer.update()
        hoofs = [h for _, _, h, *_ in LEGS.values()]
        keys.append({name: (bones[name].location.copy(), bones[name].rotation_quaternion.copy())
                     for name in keyed | {"Body_33"} | set(hoofs)})
    action = bpy.data.actions.new("Walk")
    ad.action = action
    for frame, pose in enumerate(keys):
        for name, (location, rotation) in pose.items():
            bones[name].location = location
            bones[name].rotation_quaternion = rotation
            bones[name].keyframe_insert("location", frame=frame)
            bones[name].keyframe_insert("rotation_quaternion", frame=frame)
    ad.action = None
    for pb in bones:
        pb.matrix_basis = Matrix.Identity(4)
    track = ad.nla_tracks.new()
    track.name = "Walk"
    track.strips.new("Walk", 0, action)
    return action


def stride_speed():
    """Ground speed (model units/s) the cycle is authored for: the planted hoof sweeps 2*L*sin(swing) per stance."""
    swing_name, flex_name, *_ = LEGS["FL"]
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    bone = arm.data.bones[flex_name]
    hip = arm.data.bones[swing_name].head_local
    length = hip.z - bone.tail_local.z
    return 2 * length * math.sin(math.radians(LEGS["FL"][4])) / (STANCE * FRAMES / bpy.context.scene.render.fps)


def export():
    for o in bpy.data.objects:
        o.select_set(True)
    bpy.ops.export_scene.gltf(filepath=OUTPUT, export_format="GLB", export_animation_mode="NLA_TRACKS",
                              export_anim_single_armature=True, export_yup=True)


def main():
    arm = import_source()
    for track in arm.animation_data.nla_tracks:
        track.mute = False
    build_walk(arm)
    for track in arm.animation_data.nla_tracks:
        track.mute = False
    export()
    return {"output": OUTPUT, "walk_units_per_s": round(stride_speed(), 3),
            "tracks": [t.name for t in arm.animation_data.nla_tracks]}


result = main()
