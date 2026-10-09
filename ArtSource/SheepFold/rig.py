"""Armature, skin weights and looping animations for the sheep and lamb.

Bones (same names on both animals so Unity clips line up):
root > body > neck > head > ear.L / ear.R, body > tail, body > leg.FL / leg.FR / leg.BL / leg.BR
Weights come from the per-face 'part' ids written by sheep.py; the front of the fleece blends into the neck.
Clips are in place (no root motion) at 30 fps: Idle, Walk, Graze, Run, Lie.
Rotations are authored in model space (pitch about X, yaw about Z, roll about Y), independent of bone rolls.
"""
import math

import bpy
from mathutils import Quaternion, Vector

import sheep

FPS = 30
PART_BONES = {0: "body", 1: "head", 2: "ear.L", 3: "ear.R", 4: "leg.FL", 5: "leg.FR", 6: "leg.BL", 7: "leg.BR",
              8: "tail"}


def _v(t, k):
    return Vector(t) * k


def build(mesh_obj, params, name):
    """Creates '<name>_Rig' at the mesh's location, skins the mesh to it and parents it."""
    p, k = params, params["scale"]
    old = bpy.data.objects.get(name + "_Rig")
    if old is not None:
        old_data = old.data
        bpy.data.objects.remove(old, do_unlink=True)
        if old_data.users == 0:
            bpy.data.armatures.remove(old_data)
    data = bpy.data.armatures.new(name + "_Rig")
    arm = bpy.data.objects.new(name + "_Rig", data)
    for col in mesh_obj.users_collection:
        col.objects.link(arm)
    arm.location = mesh_obj.location.copy()
    arm.show_in_front = True

    body_c = _v(p["body_center"], k)
    head_c = _v(p["head_center"], k)
    pitch = math.radians(p["head_pitch"])
    ha = _v(p["head_axes"], k)
    muzzle = head_c + Vector((0, -math.cos(pitch) * ha.y * 1.05, -math.sin(pitch) * ha.y * 1.05 - ha.z * .2))
    head_base = head_c + Vector((0, ha.y * .55, -ha.z * .35))
    neck_root = body_c + Vector((0, -_v(p["body_axes"], k).y * .45, .06 * k))

    bpy.context.view_layer.objects.active = arm
    for o in bpy.context.selected_objects:
        o.select_set(False)
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = data.edit_bones

    def bone(bname, head, tail, parent=None, connect=False):
        b = eb.new(bname)
        b.head, b.tail = Vector(head), Vector(tail)
        b.roll = 0
        if parent is not None:
            b.parent = eb[parent]
            b.use_connect = connect
        return b

    bone("root", (0, 0, 0), (0, .3 * k, 0))
    bone("body", body_c, body_c + Vector((0, -.25 * k, 0)), "root")
    bone("neck", neck_root, head_base, "body")
    bone("head", head_base, muzzle, "neck", connect=True)
    for side, bname in ((-1, "ear.L"), (1, "ear.R")):
        root_pt = head_c + Vector((side * ha.x * .95, ha.y * .12 * math.cos(pitch), ha.z * .45))
        bone(bname, root_pt, root_pt + Vector((side * .15 * k, .02 * k, -.04 * k)), "head")
    tail_c = _v(p["tail"], k)
    bone("tail", tail_c + Vector((0, -.06 * k, .05 * k)), tail_c + Vector((0, .05 * k, -.08 * k)), "body")
    for bname, side, y in (("leg.FL", -1, p["leg_front"]), ("leg.FR", 1, p["leg_front"]),
                           ("leg.BL", -1, p["leg_back"]), ("leg.BR", 1, p["leg_back"])):
        x = side * p["leg_x"] * k
        bone(bname, (x, y * k, (p["leg_top"] + .04) * k), (x, y * k, 0), "body")
    bpy.ops.object.mode_set(mode="OBJECT")
    for pb in arm.pose.bones:
        pb.rotation_mode = "QUATERNION"

    # Skin weights from part ids; the fleece in front of the shoulders follows the neck.
    mesh = mesh_obj.data
    mesh_obj.vertex_groups.clear()
    groups = {b.name: mesh_obj.vertex_groups.new(name=b.name) for b in data.bones if b.name != "root"}
    parts = mesh.attributes["part"].data
    owner = {}
    for poly in mesh.polygons:
        for vi in poly.vertices:
            owner.setdefault(vi, parts[poly.index].value)
    neck_from, neck_to = -_v(p["body_axes"], k).y * .5, -_v(p["body_axes"], k).y * 1.05
    # Each wool tuft is its own mesh island: weight it as a whole (by its centre) so tufts follow
    # the neck without stretching into ovals.
    island_y = _island_centres(mesh)
    for vi, part in owner.items():
        bname = PART_BONES[part]
        if bname == "body":
            y = island_y.get(vi, mesh.vertices[vi].co.y)
            t = max(0.0, min(1.0, (y - neck_from) / (neck_to - neck_from)))
            t = t * t * (3 - 2 * t)
            if t > 0:
                groups["neck"].add([vi], t, "REPLACE")
            if t < 1:
                groups["body"].add([vi], 1 - t, "REPLACE")
        else:
            groups[bname].add([vi], 1.0, "REPLACE")
    mesh_obj.parent = arm
    mesh_obj.matrix_parent_inverse.identity()
    mesh_obj.location = (0, 0, 0)
    for mod in list(mesh_obj.modifiers):
        if mod.type == "ARMATURE":
            mesh_obj.modifiers.remove(mod)
    mod = mesh_obj.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    return arm


def _island_centres(mesh):
    """Vertex index -> Y of the centre of the connected island it belongs to."""
    links = [[] for _ in mesh.vertices]
    for e in mesh.edges:
        a, b = e.vertices
        links[a].append(b)
        links[b].append(a)
    seen = [False] * len(mesh.vertices)
    result = {}
    for start in range(len(mesh.vertices)):
        if seen[start]:
            continue
        stack, island = [start], []
        seen[start] = True
        while stack:
            v = stack.pop()
            island.append(v)
            for n in links[v]:
                if not seen[n]:
                    seen[n] = True
                    stack.append(n)
        y = sum(mesh.vertices[v].co.y for v in island) / len(island)
        for v in island:
            result[v] = y
    return result


# ---------- animation ----------

X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))


def _model_rot(pb, pitch=0.0, yaw=0.0, roll=0.0):
    q = Quaternion(Z, math.radians(yaw)) @ Quaternion(X, math.radians(pitch)) @ Quaternion(Y, math.radians(roll))
    m = pb.bone.matrix_local.to_3x3()
    return (m.inverted() @ q.to_matrix() @ m).to_quaternion()


def _model_loc(pb, offset):
    return pb.bone.matrix_local.to_3x3().inverted() @ Vector(offset)


class Clip:
    def __init__(self, arm, name, length):
        self.arm, self.length = arm, length
        old = bpy.data.actions.get(name)
        if old is not None:
            bpy.data.actions.remove(old)
        self.action = bpy.data.actions.new(name)
        self.action.use_fake_user = True
        arm.animation_data_create()
        arm.animation_data.action = self.action
        for pb in arm.pose.bones:
            pb.rotation_quaternion = (1, 0, 0, 0)
            pb.location = (0, 0, 0)

    def rot(self, bone, frame, pitch=0.0, yaw=0.0, roll=0.0):
        pb = self.arm.pose.bones[bone]
        pb.rotation_quaternion = _model_rot(pb, pitch, yaw, roll)
        pb.keyframe_insert("rotation_quaternion", frame=frame)

    def loc(self, bone, frame, offset):
        pb = self.arm.pose.bones[bone]
        pb.location = _model_loc(pb, offset)
        pb.keyframe_insert("location", frame=frame)

    def cycle(self, bone, keys, kind="rot"):
        """keys: list of (frame, value); the first key is repeated at the clip end so the loop is seamless."""
        keys = list(keys) + [(self.length, keys[0][1])]
        for frame, value in keys:
            if kind == "rot":
                self.rot(bone, frame, *value)
            else:
                self.loc(bone, frame, value)

    def finish(self):
        self.action.frame_range = (0, self.length)
        self.action.use_frame_range = True
        for fc in _fcurves(self.action):
            for kp in fc.keyframe_points:
                kp.interpolation = "BEZIER"
                kp.easing = "AUTO"
        return self.action


def _fcurves(action):
    if hasattr(action, "fcurves") and action.fcurves is not None:
        try:
            return list(action.fcurves)
        except Exception:
            pass
    curves = []
    for layer in getattr(action, "layers", []):
        for strip in layer.strips:
            for bag in strip.channelbags:
                curves.extend(bag.fcurves)
    return curves


def idle(arm, prefix, k):
    c = Clip(arm, prefix + "_Idle", 120)
    c.cycle("body", [(0, (0, 0, 0)), (30, (0, 0, .005 * k)), (60, (0, 0, 0)), (90, (0, 0, .005 * k))], "loc")
    c.cycle("neck", [(0, (0, 0, 0)), (40, (0, 0, 0)), (55, (-4, 14, 0)), (75, (-4, 14, 0)), (92, (2, -6, 0)),
                     (105, (0, 0, 0))])
    c.cycle("head", [(0, (0, 0, 0)), (50, (0, 0, 0)), (60, (3, 6, 8)), (76, (3, 6, 8)), (88, (0, 0, 0))])
    c.cycle("ear.L", [(0, (0, 0, 0)), (44, (0, 0, 0)), (47, (0, -10, 28)), (51, (0, 4, -6)), (55, (0, 0, 0))])
    c.cycle("ear.R", [(0, (0, 0, 0)), (96, (0, 0, 0)), (99, (0, 10, -28)), (103, (0, -4, 6)), (107, (0, 0, 0))])
    c.cycle("tail", [(0, (0, 0, 0)), (70, (0, 0, 0)), (74, (0, 18, 0)), (78, (0, -16, 0)), (82, (0, 12, 0)),
                     (87, (0, 0, 0))])
    for leg in ("leg.FL", "leg.FR", "leg.BL", "leg.BR"):
        c.cycle(leg, [(0, (0, 0, 0))])
    return c.finish()


def walk(arm, prefix, k):
    c = Clip(arm, prefix + "_Walk", 30)
    swing = 24
    c.cycle("leg.FL", [(0, (-swing, 0, 0)), (15, (swing, 0, 0))])
    c.cycle("leg.BR", [(0, (-swing, 0, 0)), (15, (swing, 0, 0))])
    c.cycle("leg.FR", [(0, (swing, 0, 0)), (15, (-swing, 0, 0))])
    c.cycle("leg.BL", [(0, (swing, 0, 0)), (15, (-swing, 0, 0))])
    c.cycle("body", [(0, (0, 0, 0)), (8, (0, 0, .018 * k)), (15, (0, 0, 0)), (23, (0, 0, .018 * k))], "loc")
    c.cycle("root", [(0, (0, 0, 0)), (8, (0, 0, 2.2)), (15, (0, 0, 0)), (23, (0, 0, -2.2))])
    c.cycle("neck", [(0, (2, 0, 0)), (8, (-3, 1.5, 0)), (15, (2, 0, 0)), (23, (-3, -1.5, 0))])
    c.cycle("head", [(0, (-2, 0, 0)), (8, (3, 0, 0)), (15, (-2, 0, 0)), (23, (3, 0, 0))])
    c.cycle("ear.L", [(0, (0, 0, -4)), (8, (0, 0, 6)), (15, (0, 0, -4)), (23, (0, 0, 6))])
    c.cycle("ear.R", [(0, (0, 0, 4)), (8, (0, 0, -6)), (15, (0, 0, 4)), (23, (0, 0, -6))])
    c.cycle("tail", [(0, (0, 9, 0)), (15, (0, -9, 0))])
    return c.finish()


def run(arm, prefix, k):
    c = Clip(arm, prefix + "_Run", 20)
    c.cycle("leg.FL", [(0, (-38, 0, 0)), (10, (34, 0, 0))])
    c.cycle("leg.FR", [(0, (-32, 0, 0)), (10, (38, 0, 0))])
    c.cycle("leg.BL", [(0, (34, 0, 0)), (10, (-36, 0, 0))])
    c.cycle("leg.BR", [(0, (38, 0, 0)), (10, (-30, 0, 0))])
    c.cycle("body", [(0, (0, 0, 0)), (5, (0, 0, .06 * k)), (10, (0, 0, .01 * k)), (15, (0, 0, .045 * k))], "loc")
    c.cycle("root", [(0, (-5, 0, 0)), (5, (0, 0, 0)), (10, (6, 0, 0)), (15, (0, 0, 0))])
    c.cycle("neck", [(0, (-12, 0, 0)), (10, (-6, 0, 0))])
    c.cycle("head", [(0, (6, 0, 0)), (10, (0, 0, 0))])
    c.cycle("ear.L", [(0, (-25, -20, 0)), (10, (-15, -10, 0))])
    c.cycle("ear.R", [(0, (-25, 20, 0)), (10, (-15, 10, 0))])
    c.cycle("tail", [(0, (-25, 0, 0)), (10, (-10, 0, 0))])
    return c.finish()


def graze(arm, prefix, k, neck=40.0, head=32.0):
    """Head down tearing grass: a few quick nibbles, a chew, and a lazy ear and tail."""
    c = Clip(arm, prefix + "_Graze", 90)
    c.cycle("root", [(0, (3, 0, 0))])
    # The neck reaches forward as well as down, so the muzzle lands ahead of the front legs.
    c.cycle("neck", [(0, (neck, 0, 0)), (30, (neck - 2, 3, 0)), (60, (neck + 1, -3, 0))])
    c.cycle("neck", [(0, (0, -.1 * k, -.05 * k))], "loc")
    c.cycle("head", [(0, (head, 0, 0)), (6, (head + 8, 2, 0)), (12, (head, 0, 0)), (18, (head + 8, -2, 0)),
                     (24, (head, 0, 0)), (40, (head - 4, 0, 4)), (52, (head - 2, 0, -4)), (64, (head, 0, 0)),
                     (70, (head + 8, 2, 0)), (76, (head, 0, 0))])
    c.cycle("ear.L", [(0, (0, 0, -14)), (48, (0, 0, -14)), (51, (0, -8, 10)), (56, (0, 0, -14))])
    c.cycle("ear.R", [(0, (0, 0, 14))])
    c.cycle("tail", [(0, (0, 0, 0)), (20, (0, 0, 0)), (24, (0, 14, 0)), (28, (0, -12, 0)), (33, (0, 0, 0))])
    c.cycle("leg.FL", [(0, (-6, 0, 0))])
    c.cycle("leg.FR", [(0, (-4, 0, 0))])
    c.cycle("body", [(0, (0, 0, -.015 * k))], "loc")
    return c.finish()


def lie(arm, prefix, k, drop):
    """Lying down to rest: belly on the ground, legs folded underneath, slow breathing and a drowsy head."""
    c = Clip(arm, prefix + "_Lie", 150)
    c.cycle("body", [(0, (0, 0, -drop)), (75, (0, 0, -drop + .007 * k))], "loc")
    c.cycle("root", [(0, (2, 0, 0))])
    # Leg bones point down; +pitch folds a front leg back under the chest, -pitch a back leg forward.
    c.cycle("leg.FL", [(0, (86, 0, 0))])
    c.cycle("leg.FR", [(0, (84, 0, 0))])
    c.cycle("leg.BL", [(0, (-84, 0, 0))])
    c.cycle("leg.BR", [(0, (-86, 0, 0))])
    c.cycle("neck", [(0, (10, 0, 0)), (60, (12, 6, 0)), (100, (12, 6, 0)), (125, (10, 0, 0))])
    c.cycle("head", [(0, (6, 0, 0)), (60, (9, 4, 4)), (100, (9, 4, 4)), (125, (6, 0, 0))])
    c.cycle("ear.L", [(0, (0, 0, -18)), (80, (0, 0, -18)), (83, (0, -8, 8)), (88, (0, 0, -18))])
    c.cycle("ear.R", [(0, (0, 0, 18))])
    c.cycle("tail", [(0, (0, 0, 0))])
    return c.finish()


def animate(arm, prefix, params):
    k = params["scale"]
    bpy.context.scene.render.fps = FPS
    # Lower the body until its belly rests just above the ground.
    drop = (params["body_center"][2] - params["body_axes"][2] - .04) * k
    clips = [idle(arm, prefix, k), walk(arm, prefix, k), graze(arm, prefix, k), run(arm, prefix, k),
             lie(arm, prefix, k, drop)]
    arm.animation_data.action = clips[0]
    return clips


def rig_all():
    out = {}
    for name, params in (("Sheep", sheep.ADULT), ("Lamb", sheep.LAMB)):
        mesh_obj = bpy.data.objects[name]
        arm = build(mesh_obj, params, name)
        out[name] = (arm, animate(arm, name, params))
    return out
