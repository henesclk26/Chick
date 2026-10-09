"""Renders the journal icons for the egg economy into Assets/Resources/Journal (transparent PNGs).

Coin.png: the gold coin shown on prices and the balance. BasketArt.png: the egg basket card art.
Run inside Blender after build_sheepfold.build(("props",)):  import icons; icons.render_all()
"""
import math
import os

import bmesh
import bpy
from mathutils import Matrix, Vector

OUT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "Resources", "Journal"))


def _material(name, color, metallic=0.0, roughness=.5):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*color, 1)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    return mat


def _coin(collection):
    gold = _material("Icon Gold", (.95, .6, .07), .15, .35)
    dark = _material("Icon Gold Dark", (.72, .38, .04), .15, .45)
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=48, radius1=1, radius2=1, depth=.2)
    bmesh.ops.bevel(bm, geom=list(bm.edges), offset=.05, segments=2, affect="EDGES")
    for face in bm.faces:
        face.material_index = 1 if abs(face.normal.z) < .5 else 0
    mesh = bpy.data.meshes.new("IconCoin")
    bm.to_mesh(mesh)
    bm.free()
    mesh.materials.append(gold)
    mesh.materials.append(dark)
    coin = bpy.data.objects.new("IconCoin", mesh)
    collection.objects.link(coin)
    # A raised darker disc and an egg on the face.
    ring = bpy.data.objects.new("IconCoinRing", bpy.data.meshes.new("IconCoinRing"))
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=48, radius1=.8, radius2=.8, depth=.04,
                          matrix=Matrix.Translation((0, 0, .11)))
    bm.to_mesh(ring.data)
    bm.free()
    ring.data.materials.append(dark)
    collection.objects.link(ring)
    egg = bpy.data.objects.new("IconCoinEgg", bpy.data.meshes.new("IconCoinEgg"))
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=14, radius=1)
    for v in bm.verts:
        v.co.y *= 1.0 + .18 * v.co.y
    bm.to_mesh(egg.data)
    bm.free()
    for p in egg.data.polygons:
        p.use_smooth = True
    egg.data.materials.append(gold)
    egg.scale = (.32, .42, .1)
    egg.location = (0, .02, .13)
    collection.objects.link(egg)
    return [coin, ring, egg]


def _render(path, target, distance, elevation, size=256):
    scene = bpy.context.scene
    cam = scene.camera
    t = Vector(target)
    cam.location = t + Vector((0, -math.cos(elevation), math.sin(elevation))) * distance
    cam.rotation_euler = (t - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.film_transparent = True
    scene.render.resolution_x = scene.render.resolution_y = size
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def render_all():
    scene = bpy.context.scene
    col = bpy.data.collections.get("Icons") or bpy.data.collections.new("Icons")
    if col.name not in scene.collection.children:
        scene.collection.children.link(col)
    for obj in list(col.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    hidden = {c.name: c.hide_render for c in bpy.data.collections}
    os.makedirs(OUT, exist_ok=True)
    try:
        for c in bpy.data.collections:
            c.hide_render = c.name != "Icons"
        parts = _coin(col)
        for p in parts:
            p.location.x += 100
        scene.camera.data.lens = 50
        _render(os.path.join(OUT, "Coin.png"), (100, 0, 0), 3.9, math.radians(62))
        for p in parts:
            bpy.data.objects.remove(p, do_unlink=True)
        basket = bpy.data.objects["EggBasket"]
        copy = bpy.data.objects.new("IconBasket", basket.data)
        copy.location = (200, 0, 0)
        col.objects.link(copy)
        egg_mat = _material("Icon Egg", (1, .96, .88), 0, .45)
        gold_mat = _material("Icon Golden Egg", (1, .74, .2), .7, .3)
        spots = [(0, 0), (.13, 0), (-.13, 0), (.065, .11), (-.065, .11), (.065, -.11), (-.065, -.11)]
        for i, (x, y) in enumerate(spots):
            ob = bpy.data.objects.new("IconEgg%d" % i, bpy.data.meshes.new("IconEgg%d" % i))
            bm = bmesh.new()
            bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=10, radius=1)
            bm.to_mesh(ob.data)
            bm.free()
            for p in ob.data.polygons:
                p.use_smooth = True
            ob.data.materials.append(gold_mat if i == 3 else egg_mat)
            ob.scale = (.068, .05, .05)
            ob.location = (200 + x, y, .12)
            ob.rotation_euler = (0, 0, i * .7)
            col.objects.link(ob)
        _render(os.path.join(OUT, "BasketArt.png"), (200, 0, .2), 1.65, math.radians(38))
    finally:
        for name, value in hidden.items():
            if name in bpy.data.collections:
                bpy.data.collections[name].hide_render = value
        for obj in list(col.objects):
            bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.collections.remove(col)
    return OUT
