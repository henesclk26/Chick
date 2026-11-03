"""Builds the loose grain foods: golden wheat (WheatGrain), red wheat (RedWheatGrain) and white rice (RiceGrain).

Each grain is a low ellipsoid (16 x 10 segments, 288 triangles, long axis Y in Blender = Unity Z) shaped by a few
formulas and vertex painted (crease, germ, ends, faint ridges), then the paint is baked into a small texture on the
sphere's own UVs so Unity can use a plain URP Lit material. Sizes match the prefabs' colliders (metres).

Writes Assets/Art/Edibles/Grains/<Name>.fbx and <Name>_Albedo.png.
Run (Blender 5.x, background):
    blender -b --factory-startup --python ArtSource/Grains/grains.py -- <project root>
"""
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

PROJECT = sys.argv[sys.argv.index("--") + 1]
OUT = os.path.join(PROJECT, "Assets", "Art", "Edibles", "Grains")
SEG, RINGS, TEX = 16, 10, 128


def smooth(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def mix(a, b, t):
    return tuple(x + (y - x) * t for x, y in zip(a, b))


# Wheat: plump oval with a crease along its top, a pale germ at one end and a darker brush tip at the other.
def wheat_shape(c):
    c.z -= math.exp(-(c.x / .22) ** 2) * smooth(c.z * 1.6) * .45
    c.y *= 1 + .08 * c.z
    if c.y > .7:
        c.x *= 1 - .35 * (c.y - .7) / .3
        c.z *= 1 - .25 * (c.y - .7) / .3
    return c


def wheat_paint(base, dark, light):
    def paint(c):
        rgb = mix(base, light, smooth(c.z * 1.4) * .7)
        rgb = mix(rgb, dark, math.exp(-(c.x / .2) ** 2) * smooth(c.z * 2.2) * .9)
        rgb = mix(rgb, mix(light, (1, 1, 1), .25), smooth((-c.y - .62) / .3) * smooth(c.z * 3) * .8)
        return mix(rgb, dark, smooth((c.y - .8) / .2) * .5)
    return paint


# Rice: slender and slightly flattened, with the oblique notch where the germ was.
def rice_shape(c):
    c.x *= 1 - .12 * c.z * c.z
    if c.y < -.55 and c.x > 0:
        c.x -= .35 * smooth((-c.y - .55) / .45) * smooth(c.x / .6)
    if c.y > .75:
        c.x *= 1 - .2 * (c.y - .75) / .25
    return c


def rice_paint(c):
    rgb = mix((.9, .88, .8), (1.0, .99, .95), smooth(c.z * 1.2 + .3))
    rgb = mix(rgb, (.84, .82, .74), math.exp(-((abs(c.x) - .45) / .1) ** 2) * smooth(c.z * 2) * .5)
    rgb = mix(rgb, (.86, .82, .7), smooth((abs(c.y) - .75) / .25) * .6)
    return mix(rgb, (.83, .77, .6), smooth((-c.y - .6) / .3) * smooth(c.x * 2) * .7)


GRAINS = [
    ("WheatGrain", (.044, .028, .022), wheat_shape, wheat_paint((.86, .6, .24), (.5, .3, .1), (.98, .78, .4))),
    ("RedWheatGrain", (.044, .028, .022), wheat_shape, wheat_paint((.72, .4, .17), (.4, .2, .07), (.88, .58, .3))),
    ("RiceGrain", (.032, .016, .013), rice_shape, rice_paint),
]


def build(name, size, shape, paint):
    length, width, height = size
    bm = bmesh.new()
    bm.loops.layers.uv.new("UVMap")      # create_uvsphere fills the active UV layer only if one exists
    bmesh.ops.create_uvsphere(bm, u_segments=SEG, v_segments=RINGS, radius=1.0, calc_uvs=True)
    for v in bm.verts:
        v.co = shape(Vector(v.co))
        v.co.x *= width / 2
        v.co.y *= length / 2
        v.co.z *= height / 2
    zs = [v.co.z for v in bm.verts]
    bmesh.ops.translate(bm, verts=bm.verts, vec=(0, 0, -(max(zs) + min(zs)) / 2))   # centred, like the prefabs
    bm.normal_update()
    col = bm.loops.layers.color.new("Col")
    for f in bm.faces:
        for loop in f.loops:
            co = loop.vert.co
            loop[col] = (*paint(Vector((co.x / (width / 2), co.y / (length / 2), co.z / (height / 2)))), 1.0)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    for p in mesh.polygons:
        p.use_smooth = True
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def bake(obj, path):
    """Bakes the vertex paint into a TEX x TEX image on the mesh's UVs (Cycles emission bake)."""
    image = bpy.data.images.new(obj.name + "_Albedo", TEX, TEX)
    mat = bpy.data.materials.new(obj.name + "_Bake")
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    nodes.clear()
    vc = nodes.new("ShaderNodeVertexColor")
    vc.layer_name = "Col"
    emit = nodes.new("ShaderNodeEmission")
    out = nodes.new("ShaderNodeOutputMaterial")
    links.new(vc.outputs["Color"], emit.inputs["Color"])
    links.new(emit.outputs["Emission"], out.inputs["Surface"])
    target = nodes.new("ShaderNodeTexImage")
    target.image = image
    nodes.active = target
    obj.data.materials.append(mat)
    for o in bpy.context.scene.objects:
        o.select_set(o is obj)
    bpy.context.view_layer.objects.active = obj
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 4
    scene.render.bake.margin = 4
    bpy.ops.object.bake(type="EMIT")
    image.filepath_raw = path
    image.file_format = "PNG"
    image.save()


def export(obj, path):
    for o in bpy.context.scene.objects:
        o.select_set(o is obj)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, apply_unit_scale=True,
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                             bake_space_transform=True, mesh_smooth_type="FACE", object_types={"MESH"},
                             bake_anim=False, path_mode="STRIP", embed_textures=False)


os.makedirs(OUT, exist_ok=True)
for o in list(bpy.context.scene.objects):
    bpy.data.objects.remove(o)
for name, size, shape, paint in GRAINS:
    obj = build(name, size, shape, paint)
    bake(obj, os.path.join(OUT, name + "_Albedo.png"))
    obj.data.materials.clear()
    export(obj, os.path.join(OUT, name + ".fbx"))
    print("GRAIN", name, sum(len(p.vertices) - 2 for p in obj.data.polygons), tuple(round(v, 4) for v in obj.dimensions))
