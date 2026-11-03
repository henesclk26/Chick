"""Fills the chicken yard hay feeder (props.coop_hay_feeder) with the coop's own hand-painted hay.

Copies loose pieces of the coop model's hay mesh ("Object_5", material SENO in Assets/Assets/chicken_coop_handpainted.glb)
with their UVs: its hay tufts, enlarged and scattered at random, cover the trough floor and heap over it (the rack stays empty: tufts there float between the slats),
and flat straw cards lie on the ground around it. Every piece is scaled into the feeder's own space (metres, z up,
long side on X, origin at the feeder's base centre), so the result lines up with CoopHayFeeder.fbx when both sit at
the same transform. Unity gives it the glb's SENO material.

Writes Assets/Art/Coop/CoopHayFeeder_Hay.fbx.
Run (Blender 5.x, background):
    blender -b --factory-startup --python ArtSource/ChickenCoop/feeder_hay.py -- <project root>
"""
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

PROJECT = sys.argv[sys.argv.index("--") + 1]
GLB = os.path.join(PROJECT, "Assets", "Assets", "chicken_coop_handpainted.glb")
OUT = os.path.join(PROJECT, "Assets", "Art", "Coop", "CoopHayFeeder_Hay.fbx")


def loose_parts(obj):
    """The mesh's connected pieces in world space, largest first: lists of face indices."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.faces.ensure_lookup_table()
    seen, parts = set(), []
    for f in bm.faces:
        if f.index in seen:
            continue
        stack, group = [f], []
        seen.add(f.index)
        while stack:
            g = stack.pop()
            group.append(g.index)
            for e in g.edges:
                for n in e.link_faces:
                    if n.index not in seen:
                        seen.add(n.index)
                        stack.append(n)
        parts.append(group)
    bm.free()
    parts.sort(key=len, reverse=True)
    return parts


def piece(src, faces):
    """A bmesh of one piece (world space, UVs kept), recentred on its base centre and scaled to a unit footprint."""
    bm = bmesh.new()
    bm.from_mesh(src.data)
    bm.transform(src.matrix_world)
    keep = set(faces)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.index not in keep], context="FACES")
    xs, ys, zs = zip(*(v.co for v in bm.verts))
    size = Vector((max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs)))
    base = Vector(((max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2, min(zs)))
    bmesh.ops.translate(bm, verts=bm.verts, vec=-base)
    bmesh.ops.scale(bm, verts=bm.verts, vec=Vector((1 / size.x, 1 / size.y, 1 / max(size.z, .01))))
    return bm


def place(out, src_bm, at, size, yaw):
    """Appends a copy of a unit piece scaled to size (x, y, z metres), turned by yaw, with its base at 'at'."""
    m = Matrix.Translation(at) @ Matrix.Rotation(yaw, 4, "Z") @ Matrix.Diagonal((*size, 1))
    tmp = src_bm.copy()
    tmp.transform(m)
    mesh = bpy.data.meshes.new("tmp")
    tmp.to_mesh(mesh)
    tmp.free()
    out.from_mesh(mesh)
    bpy.data.meshes.remove(mesh)


bpy.ops.import_scene.gltf(filepath=GLB)
hay = bpy.data.objects["Object_5"]
parts = loose_parts(hay)
tufts = [piece(hay, p) for p in parts if len(p) == 12]              # small hay tufts
cards = [piece(hay, p) for p in parts if len(p) == 2]               # flat straw cards

rng = random.Random(29)
out = bmesh.new()
# Trough (floor top .14, rim .30, inside about 1.86 x .56): big tufts of mixed size, scattered at random and turned
# at random, added until every point of the floor lies under one (they overlap freely), then a few more heaped on top.
# A tuft is kept only if its turned footprint stays inside the walls (|x| <= .93, |y| <= .27).
def fits(cx, cy, sx, sy, yaw):
    c, s = abs(math.cos(yaw)), abs(math.sin(yaw))
    return abs(cx) + (c * sx + s * sy) / 2 <= .93 and abs(cy) + (s * sx + c * sy) / 2 <= .27


def covers(cx, cy, sx, sy, yaw, px, py):
    dx, dy = px - cx, py - cy
    u, v = dx * math.cos(yaw) + dy * math.sin(yaw), -dx * math.sin(yaw) + dy * math.cos(yaw)
    return (u / (sx * .4)) ** 2 + (v / (sy * .4)) ** 2 <= 1      # .4: the tuft's skirt thins out towards its rim


floor = [(-.82 + i * .082, -.18 + j * .045) for i in range(21) for j in range(9)]
laid = []
for _ in range(40000):
    if not floor or len(laid) >= 60:
        break
    # Mostly lengthwise (the trough is narrow), but each one turned a little differently.
    sx, sy = rng.uniform(.3, .66), rng.uniform(.28, .52)
    yaw = rng.choice((0, math.pi)) + rng.uniform(-.45, .45)
    target = rng.choice(floor)
    cx, cy = target[0] + rng.uniform(-.08, .08), target[1] + rng.uniform(-.05, .05)
    if not fits(cx, cy, sx, sy, yaw):
        continue
    laid.append((cx, cy, sx, sy, yaw))
    floor = [p for p in floor if not covers(cx, cy, sx, sy, yaw, *p)]
    place(out, rng.choice(tufts), (cx, cy, .14), (sx, sy, rng.uniform(.13, .22)), yaw)
# Whatever the random pass missed (tight corners against the walls) gets a small tuft pushed in from the wall.
while floor:
    px, py = floor[0]
    sx, sy, yaw = rng.uniform(.3, .36), rng.uniform(.28, .32), rng.uniform(-.12, .12)
    cx, cy = max(-.93 + sx * .55, min(.93 - sx * .55, px)), max(-.27 + sy * .55, min(.27 - sy * .55, py))
    laid.append((cx, cy, sx, sy, yaw))
    floor = [p for p in floor if not covers(cx, cy, sx, sy, yaw, *p)] if covers(cx, cy, sx, sy, yaw, px, py) else floor[1:]
    place(out, rng.choice(tufts), (cx, cy, .14), (sx, sy, rng.uniform(.12, .18)), yaw)
laid_floor = list(laid)
for _ in range(400):
    if len(laid) >= len(laid_floor) + 8:
        break
    sx, sy, yaw = rng.uniform(.36, .52), rng.uniform(.3, .44), rng.uniform(0, math.tau)
    cx, cy = rng.uniform(-.7, .7), rng.uniform(-.1, .1)
    if fits(cx, cy, sx, sy, yaw):
        laid.append((cx, cy, sx, sy, yaw))
        place(out, rng.choice(tufts), (cx, cy, rng.uniform(.18, .22)), (sx, sy, rng.uniform(.12, .18)), yaw)
print("FEEDERHAY tufts", len(laid), "uncovered floor points", len(floor))
# Loose straw on the ground around it.
for _ in range(10):
    side = rng.choice((-1, 1))
    s = rng.uniform(.18, .3)
    place(out, rng.choice(cards), (rng.uniform(-1.15, 1.15), side * rng.uniform(.42, .65), .005), (s, s, .02),
          rng.uniform(0, math.tau))

mesh = bpy.data.meshes.new("CoopHayFeederHay")
out.to_mesh(mesh)
out.free()
obj = bpy.data.objects.new("CoopHayFeederHay", mesh)
bpy.context.scene.collection.objects.link(obj)
# Same export as the feeder (ArtSource/SheepFold/export_fbx.export_prop): turned to face Unity's +Z, axes baked.
mesh.transform(Matrix.Rotation(math.pi, 4, "Z"))
for o in bpy.context.view_layer.objects:
    o.select_set(o is obj)
bpy.context.view_layer.objects.active = obj
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
                         axis_forward="-Z", axis_up="Y", bake_space_transform=True, mesh_smooth_type="FACE",
                         object_types={"MESH"}, bake_anim=False, path_mode="STRIP", embed_textures=False)
print("FEEDERHAY", len(mesh.polygons), sum(len(p.vertices) - 2 for p in mesh.polygons), OUT)
