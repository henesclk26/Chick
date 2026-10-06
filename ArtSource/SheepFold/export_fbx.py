"""Exports the sheep fold models to Assets/Art/SheepFold/Models as FBX (one file per model).

Animals: armature + skinned mesh, every '<Name>_*' action as its own take (via NLA strips).
Props: static meshes exported with their origin at the world origin; the gate keeps its leaf as a child.
Models face -Y in Blender; each is turned 180 degrees about Z only while exporting so it faces Unity's +Z
(animals through the armature object, props through their mesh data so the root stays unrotated)
(Unity normalises the FBX axis system, so the exporter's axis options alone cannot change the facing).
Animals leave axis conversion to Unity's importer (the experimental space bake breaks armatures); props bake it.
"""
import math
import os

import bpy
from mathutils import Matrix

MODELS = r"C:\unityProjects\Chick\Assets\Art\SheepFold\Models"
PROPS = ("FoldWall_A", "FoldWall_B", "FoldShelter", "FoldWaterTrough", "FoldHayRack", "HayBale", "HayBaleStack",
         "FoldGate_Posts")


def _select(objs):
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in objs:
        o.hide_set(False)
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]


def _fbx(path, **extra):
    settings = dict(filepath=path, use_selection=True, apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
                    axis_forward="-Z", axis_up="Y", bake_space_transform=False, mesh_smooth_type="FACE",
                    use_mesh_modifiers=True, add_leaf_bones=False, primary_bone_axis="Y", secondary_bone_axis="X",
                    use_armature_deform_only=True, path_mode="STRIP", embed_textures=False)
    settings.update(extra)
    bpy.ops.export_scene.fbx(**settings)


def export_animal(name):
    arm = bpy.data.objects[name + "_Rig"]
    mesh = bpy.data.objects[name]
    data = arm.animation_data
    data.action = None
    for track in list(data.nla_tracks):
        data.nla_tracks.remove(track)
    clips = sorted((a for a in bpy.data.actions if a.name.startswith(name + "_")), key=lambda a: a.name)
    for action in clips:
        track = data.nla_tracks.new()
        take = action.name.split("_", 1)[1]
        track.name = take
        strip = track.strips.new(take, 0, action)
        strip.name = take
        if hasattr(strip, "action_slot") and strip.action_slot is None and len(action.slots):
            strip.action_slot = action.slots[0]
    location, rotation = arm.location.copy(), arm.rotation_euler.copy()
    arm.location = (0, 0, 0)
    arm.rotation_euler = (0, 0, math.pi)
    _select([arm, mesh])
    path = os.path.join(MODELS, name + ".fbx")
    _fbx(path, object_types={"ARMATURE", "MESH"}, bake_anim=True, bake_anim_use_all_actions=False,
         bake_anim_use_nla_strips=True, bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
         bake_anim_step=1.0)
    arm.location, arm.rotation_euler = location, rotation
    for track in list(data.nla_tracks):
        data.nla_tracks.remove(track)
    data.action = bpy.data.actions.get(name + "_Idle")
    return path, [a.name for a in clips]


def export_prop(name):
    """Turns the mesh data itself (not the object) so the prop arrives in Unity with an unrotated root."""
    obj = bpy.data.objects[name]
    turn = Matrix.Rotation(math.pi, 4, "Z")
    objs = [obj] + list(obj.children)
    location = obj.location.copy()
    obj.location = (0, 0, 0)
    for o in objs:
        o.data.transform(turn)
    for child in obj.children:
        child.location = turn @ child.location
    _select(objs)
    path = os.path.join(MODELS, name.replace("_Posts", "") + ".fbx")
    # Static meshes: bake the axis conversion into the vertices too.
    _fbx(path, object_types={"MESH", "EMPTY"}, bake_anim=False, bake_space_transform=True)
    for o in objs:
        o.data.transform(turn)
    for child in obj.children:
        child.location = turn @ child.location
    obj.location = location
    return path


def export_all():
    os.makedirs(MODELS, exist_ok=True)
    out = {}
    for name in ("Sheep", "Lamb"):
        out[name] = export_animal(name)
    for name in PROPS:
        out[name] = export_prop(name)
    return out
