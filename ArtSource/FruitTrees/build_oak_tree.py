"""Builds the oak tree from "Stylized Tree" by yonimantz (Sketchfab, CC-BY-4.0), Assets/Tree Assets/stylized_tree.glb,
to match the fruit trees (build_fruit_trees.py).

    blender -b --factory-startup --python ArtSource/FruitTrees/build_oak_tree.py -- <project root> [--export]

- Scaled to OakHeight with its origin at the foot of the trunk.
- The trunk (122k triangles in three pieces) is joined and decimated; its plain brown stays a material colour.
- The leaf cards keep their shape; the white leaf mask of the model becomes the alpha of a leaf texture painted in
  the oak's greens, in four tones (2x2 atlas) like the fruit trees, every card pointed at a tone by its place in the
  crown (darker low and inside, lighter high and outside).
- Two meshes, OakTree_Trunk and OakTree_Leaves: the Unity importer (FruitTreeSamples.cs) gives them their materials
  by name.
Writes ArtSource/FruitTrees/OakTree.blend and Textures/T_OakTree_Leaves.png; with --export also
Assets/Art/FruitTrees/OakTree.fbx and the texture next to the fruit trees'.
"""
import bpy, math, os, random, shutil, sys
import numpy as np
from mathutils import Vector, Matrix

ARGS = sys.argv[sys.argv.index("--") + 1:]
PROJECT = ARGS[0]
EXPORT = "--export" in ARGS
HERE = os.path.join(PROJECT, "ArtSource", "FruitTrees")
TEXTURES = os.path.join(HERE, "Textures")
UNITY = os.path.join(PROJECT, "Assets", "Art", "FruitTrees")
SOURCE = os.path.join(PROJECT, "Assets", "Tree Assets", "stylized_tree.glb")
OAK_HEIGHT = 9.0
TRUNK_RATIO = 0.15
DARK, LIGHT = (0.15, 0.34, 0.10), (0.46, 0.67, 0.21)   # sRGB
TONES = (0.66, 0.80, 0.93, 1.06)
SEED = 53



def blur(a, radius):
    for _ in range(2):
        for axis in (0, 1):
            c = np.cumsum(np.pad(a, [(radius + 1, radius) if i == axis else (0, 0) for i in range(2)], mode="edge"), axis=axis)
            hi = np.take(c, range(2 * radius + 1, c.shape[axis]), axis=axis)
            lo = np.take(c, range(0, c.shape[axis] - 2 * radius - 1), axis=axis)
            a = (hi - lo) / (2 * radius + 1)
    return a


def value_noise(size, cells, seed):
    rng = np.random.default_rng(seed)
    grid = rng.random((cells + 1, cells + 1)).astype(np.float32)
    t = np.linspace(0, cells, size, endpoint=False, dtype=np.float32)
    i = t.astype(int); f = t - i; f = f * f * (3 - 2 * f)
    a = grid[np.ix_(i, i)]; b = grid[np.ix_(i, i + 1)]; c = grid[np.ix_(i + 1, i)]; d = grid[np.ix_(i + 1, i + 1)]
    fx = f[None, :]; fy = f[:, None]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def paint_leaves(image, size=1024):
    w, h = image.size
    px = np.empty(w * h * 4, np.float32); image.pixels.foreach_get(px)
    alpha = px.reshape(h, w, 4)[..., 3]
    f = w // size
    mask = alpha[:size * f, :size * f].reshape(size, f, size, f).mean(axis=(1, 3))
    inside = blur(mask, 5)
    centre = np.clip((inside - 0.45) / 0.5, 0, 1)
    patches = value_noise(size, 6, SEED) * 0.6 + value_noise(size, 21, SEED + 1) * 0.4
    t = np.clip(0.15 + centre * 0.55 + (patches - 0.5) * 0.5, 0, 1)
    dark, light = np.asarray(DARK, np.float32), np.asarray(LIGHT, np.float32)
    rgb = dark * (1 - t[..., None]) + light * t[..., None]
    rgb *= (0.82 + 0.18 * np.clip(inside * 1.6, 0, 1))[..., None]
    rgb = np.where(mask[..., None] > 0.01, rgb, (dark + light) / 2)
    out = np.zeros((size * 2, size * 2, 4), np.float32)
    for i, tone in enumerate(TONES):
        row, col = divmod(i, 2)
        out[row * size:(row + 1) * size, col * size:(col + 1) * size] = np.dstack([np.clip(rgb * tone, 0, 1), mask])
    img = bpy.data.images.new("T_OakTree_Leaves", size * 2, size * 2, alpha=True)
    img.pixels.foreach_set(out.ravel())
    path = os.path.join(TEXTURES, "T_OakTree_Leaves.png")
    img.filepath_raw = path; img.file_format = "PNG"; img.save()
    return path


def join(objects, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objects: o.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    obj.name = obj.data.name = name
    return obj


def main():
    os.makedirs(TEXTURES, exist_ok=True)
    for o in list(bpy.data.objects): bpy.data.objects.remove(o)
    bpy.ops.import_scene.gltf(filepath=SOURCE)
    bpy.context.view_layer.update()
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    for o in meshes:
        o.data = o.data.copy()
        o.data.transform(o.matrix_world)
        o.parent = None
        o.matrix_world = Matrix.Identity(4)
    for o in list(bpy.data.objects):
        if o.type != "MESH": bpy.data.objects.remove(o)
    trunk_parts = [o for o in meshes if "trunk" in o.data.materials[0].name]
    leaf_parts = [o for o in meshes if "leaf" in o.data.materials[0].name]   # (listed before joining removes objects)
    trunk = join(trunk_parts, "OakTree_Trunk")
    leaves = join(leaf_parts, "OakTree_Leaves")

    # Scale to height, origin at the foot of the trunk.
    zs = [v.co.z for v in trunk.data.vertices]
    floor, top = min(zs), max(max(zs), max(v.co.z for v in leaves.data.vertices))
    foot = [v.co for v in trunk.data.vertices if v.co.z < floor + (top - floor) * 0.03]
    base = Vector((sum(c.x for c in foot) / len(foot), sum(c.y for c in foot) / len(foot), floor))
    k = OAK_HEIGHT / (top - floor)
    m = Matrix.Diagonal((k, k, k, 1)) @ Matrix.Translation(-base)
    for o in (trunk, leaves): o.data.transform(m)

    before = sum(len(p.vertices) - 2 for p in trunk.data.polygons)
    mod = trunk.modifiers.new("Decimate", "DECIMATE"); mod.ratio = TRUNK_RATIO
    bpy.context.view_layer.objects.active = trunk
    bpy.ops.object.modifier_apply(modifier=mod.name)
    after = sum(len(p.vertices) - 2 for p in trunk.data.polygons)

    # Leaf texture and tones.
    leaf_image = bpy.data.images["Image_0"]
    texture = paint_leaves(leaf_image)
    me = leaves.data
    uv = me.uv_layers[0]
    us = [d.uv for d in uv.data]
    print("leaf UV range u %.3f..%.3f v %.3f..%.3f" % (min(u.x for u in us), max(u.x for u in us), min(u.y for u in us), max(u.y for u in us)))
    rng = random.Random(SEED)
    pts = [v.co for v in me.vertices]
    zmin, zmax = min(p.z for p in pts), max(p.z for p in pts)
    centre = sum(pts, Vector()) / len(pts)
    squash = lambda p: Vector((p.x - centre.x, p.y - centre.y, (p.z - centre.z) * 1.3))
    reach = max(squash(p).length for p in pts)
    for poly in me.polygons:
        c = poly.center
        height = (c.z - zmin) / max(zmax - zmin, 1e-3)
        outer = min(squash(c).length / reach, 1.0)
        tone = min(3, max(0, int((0.45 * height + 0.55 * outer + rng.uniform(-0.18, 0.18)) * 4)))
        row, col = divmod(tone, 2)
        for li in poly.loop_indices:
            u, v = uv.data[li].uv
            uv.data[li].uv = (col * 0.5 + 0.004 + min(max(u, 0), 1) * 0.492, row * 0.5 + 0.004 + min(max(v, 0), 1) * 0.492)
    # Unity takes the first UV map; drop the second ones so it cannot pick the wrong one.
    for o in (trunk, leaves):
        while len(o.data.uv_layers) > 1: o.data.uv_layers.remove(o.data.uv_layers[-1])

    leaf_mat = bpy.data.materials.new("M_OakTree_Leaves")
    nt = leaf_mat.node_tree; bsdf = nt.nodes["Principled BSDF"]
    tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = bpy.data.images.load(texture)
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    clip = nt.nodes.new("ShaderNodeMath"); clip.operation = "GREATER_THAN"; clip.inputs[1].default_value = 0.5
    nt.links.new(tex.outputs["Alpha"], clip.inputs[0]); nt.links.new(clip.outputs[0], bsdf.inputs["Alpha"])
    bsdf.inputs["Roughness"].default_value = 0.75
    leaf_mat.surface_render_method = "DITHERED"; leaf_mat.use_backface_culling = False
    leaves.data.materials.clear(); leaves.data.materials.append(leaf_mat)
    trunk_mat = bpy.data.materials.new("M_OakTree_Trunk")
    trunk_mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.20, 0.11, 0.06, 1)
    trunk_mat.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.9
    trunk.data.materials.clear(); trunk.data.materials.append(trunk_mat)

    print("OakTree: %.1f m tall, trunk %d -> %d triangles, leaves %d triangles" % (
        OAK_HEIGHT, before, after, sum(len(p.vertices) - 2 for p in leaves.data.polygons)))
    for img in list(bpy.data.images):
        if img.name == "Image_0": bpy.data.images.remove(img)
    if EXPORT:
        bpy.ops.object.select_all(action="DESELECT")
        trunk.select_set(True); leaves.select_set(True)
        bpy.ops.export_scene.fbx(filepath=os.path.join(UNITY, "OakTree.fbx"), use_selection=True, object_types={"MESH"},
                                 apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z",
                                 axis_up="Y", bake_space_transform=True, mesh_smooth_type="FACE", use_triangles=True,
                                 add_leaf_bones=False, path_mode="STRIP", embed_textures=False, colors_type="NONE")
        os.makedirs(os.path.join(UNITY, "Textures"), exist_ok=True)
        shutil.copyfile(texture, os.path.join(UNITY, "Textures", "T_OakTree_Leaves.png"))
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "OakTree.blend"))


main()
