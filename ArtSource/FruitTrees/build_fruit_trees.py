"""Builds the apple, orange, sour cherry and pear trees: all the same Lux Art Studios stylised tree (the orange tree,
S_Tree_D from Assets/Tree Assets/Trees.blend) with PolyOne Free Fruits (or the pear made here) hanging in its crown.

Run (Blender 5.x, background):
    blender -b --factory-startup --python ArtSource/FruitTrees/build_fruit_trees.py -- <project root> <lux textures dir> [--export] [--old-fruitless]
(--old-fruitless builds OLD_FRUITLESS instead: the first designs without fruit, as Old*Tree, into FruitTreesOldFruitless.blend;
 --new-designs builds NEW_DESIGNS: more trees in the same style and new colours, into NewTreeDesigns.blend.)

<lux textures dir> holds the source PNGs pulled out of the Unreal kit (T_Trunk_D.png, T_Leaf_*.png).

- Each tree is copied out of Trees.blend with its transform applied and its origin at the foot of the trunk.
- The leaf cards keep their UVs (one leaf cluster per card); every tree gets its own leaf texture: the kit's white leaf
  mask as alpha, painted in that tree's greens with lighter leaf centres and darker rims.
- The crown is shaded through the leaf texture: it holds the leaves in four tones (2x2 atlas) and every card is pointed
  at one, darker low and inside, lighter at the top and outer shell, so a plain lit cut-out material is enough.
- Material order: trunk first, leaves second (the Unity importer, FruitTreeSamples.cs, relies on it); the fruits are a
  child mesh using the Free Fruits material.
- Fruits are placed on the outer shell of the crown (rays from outside towards the crown's middle stop at the first
  leaf card), mostly on the sides and underside, spaced apart, stem up with a little random tilt.
Writes ArtSource/FruitTrees/FruitTrees.blend and the textures to ArtSource/FruitTrees/Textures; with --export also
Assets/Art/FruitTrees/<Tree>.fbx and Assets/Art/FruitTrees/Textures.
"""
import bpy, bmesh, math, os, random, shutil, sys
import numpy as np
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
from mathutils.interpolate import poly_3d_calc

ARGS = sys.argv[sys.argv.index("--") + 1:]
PROJECT, LUX = ARGS[0], ARGS[1]
HERE = os.path.join(PROJECT, "ArtSource", "FruitTrees")
TEXTURES = os.path.join(HERE, "Textures")
TREES_BLEND = os.path.join(PROJECT, "Assets", "Tree Assets", "Trees.blend")
UNITY = os.path.join(PROJECT, "Assets", "Art", "FruitTrees")
EXPORT = "--export" in ARGS
TONES = (0.66, 0.80, 0.93, 1.06)   # leaf atlas brightness, darkest first
FRUIT_MAX_TRIANGLES = 260
FRUITS = os.path.join(PROJECT, "Assets", "PolyOne", "Free Fruits")
os.makedirs(TEXTURES, exist_ok=True)

# Every fruit tree is the same tree: the orange tree's crown (S_Tree_D) with its oval leaves in its deep green; only the
# fruit differs.
SAME_TREE = dict(tree="S_Tree_D", leaf="T_Leaf_4", dark=(0.09, 0.28, 0.11), light=(0.27, 0.50, 0.19))
SPECS = [
    # name, fruit model, fruit height (m), count, seed (+ the shared tree: source tree, leaf mask, leaf dark/light sRGB)
    dict(name="AppleTree", fruit="SM_Fruits_Apple_R1", height=0.22, count=24, seed=11, **SAME_TREE),
    dict(name="OrangeTree", fruit="SM_Fruits_Orange_1", height=0.24, count=20, seed=23, **SAME_TREE),
    dict(name="CherryTree", fruit="SM_Fruits_Cherries_2", height=0.24, count=30, seed=37, **SAME_TREE),
    # Pear: modelled here (make_pear).
    dict(name="PearTree", fruit="Pear", height=0.20, count=22, seed=41, **SAME_TREE),
]
# With --old-fruitless: the trees as they first were, each its own Lux tree in its own greens, without fruit (sample
# trees on the test plane; the game's fruit trees above stay as they are).
OLD_FRUITLESS = [
    dict(name="OldAppleTree", tree="S_Tree_G", leaf="T_Leaf_4", dark=(0.20, 0.42, 0.13), light=(0.50, 0.72, 0.25),
         fruit=None, height=0.22, count=0, seed=11),
    dict(name="OldCherryTree", tree="S_Tree_C", leaf="T_Leaf_2", dark=(0.24, 0.46, 0.14), light=(0.58, 0.77, 0.30),
         fruit=None, height=0.24, count=0, seed=37),
    dict(name="OldPearTree", tree="S_Tree_H", leaf="T_Leaf_4", dark=(0.15, 0.36, 0.15), light=(0.44, 0.65, 0.27),
         fruit=None, height=0.20, count=0, seed=41),
]
# With --new-designs: more trees in the same style, from the kit's other trees and leaf shapes, in new colours, without
# fruit (height_m scales the kit's tree to that height).
NEW_DESIGNS = [
    dict(name="BlossomTree", tree="S_Tree_F", leaf="T_Leaf_1", dark=(0.72, 0.38, 0.52), light=(0.99, 0.84, 0.90),
         fruit=None, height=0.2, count=0, seed=51, height_m=5.8),
    dict(name="AutumnTree", tree="S_Tree_A", leaf="T_Leaf_3", dark=(0.55, 0.16, 0.06), light=(0.98, 0.62, 0.16),
         fruit=None, height=0.2, count=0, seed=61, height_m=8.0),
    dict(name="OliveTree", tree="S_Tree_B", leaf="T_Leaf_5", dark=(0.24, 0.32, 0.22), light=(0.62, 0.70, 0.52),
         fruit=None, height=0.2, count=0, seed=71, height_m=6.5),
    dict(name="GoldenTree", tree="S_Tree_E", leaf="T_Leaf_6", dark=(0.36, 0.46, 0.08), light=(0.92, 0.88, 0.30),
         fruit=None, height=0.2, count=0, seed=81, height_m=7.2),
    # Fuller crowns (dense: extra layers of leaf cards, see densify).
    dict(name="LushTree", tree="S_Tree_J", leaf="T_Leaf_5", dark=(0.10, 0.30, 0.08), light=(0.42, 0.66, 0.20),
         fruit=None, height=0.2, count=0, seed=91, height_m=6.8, dense=[(37, 0.86), (-23, 0.72)]),
    dict(name="CrimsonTree", tree="S_Tree_I", leaf="T_Leaf_3", dark=(0.40, 0.06, 0.06), light=(0.88, 0.26, 0.16),
         fruit=None, height=0.2, count=0, seed=101, height_m=7.0, dense=[(41, 0.86), (-19, 0.74)]),
    # A pine: the kit has none, so it is built here (make_pine) and dressed like the others (spiky leaf mask as
    # needles, deep pine greens, the same bark and tones).
    # Pines stand on a bare trunk (clear_m) so their lowest branches stay off the ground; reach = crown width.
    dict(name="PineTree", tree="PINE", leaf="T_Leaf_3", dark=(0.04, 0.17, 0.09), light=(0.20, 0.40, 0.16),
         fruit=None, height=0.2, count=0, seed=111, height_m=12.5, clear_m=4.6, reach=0.30, whorls=15, dense=[(30, 0.9)]),
    dict(name="TallPineTree", tree="PINE", leaf="T_Leaf_3", dark=(0.04, 0.16, 0.13), light=(0.17, 0.37, 0.30),
         fruit=None, height=0.2, count=0, seed=121, height_m=14.0, clear_m=5.4, reach=0.24, whorls=16, dense=[(28, 0.9)]),
    dict(name="BushyPineTree", tree="PINE", leaf="T_Leaf_3", dark=(0.06, 0.21, 0.08), light=(0.29, 0.50, 0.19),
         fruit=None, height=0.2, count=0, seed=131, height_m=12.0, clear_m=4.2, reach=0.34, whorls=15, dense=[(33, 0.9), (-18, 0.78)]),
]
BLEND_NAME = "FruitTrees.blend"
if "--old-fruitless" in ARGS:
    SPECS, BLEND_NAME = OLD_FRUITLESS, "FruitTreesOldFruitless.blend"
if "--new-designs" in ARGS:
    SPECS, BLEND_NAME = NEW_DESIGNS, "NewTreeDesigns.blend"


def srgb_to_linear(c):
    c = np.asarray(c, dtype=np.float32)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def blur(a, radius):
    """Separable box blur, applied twice (close to a gaussian)."""
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


def paint_leaf_texture(spec, size=1024):
    """The kit's leaf mask as alpha, painted with the tree's greens; returns the saved PNG path."""
    src = bpy.data.images.load(os.path.join(LUX, spec["leaf"] + ".png"))
    w, h = src.size
    px = np.empty(w * h * 4, dtype=np.float32); src.pixels.foreach_get(px)
    px = px.reshape(h, w, 4)
    # Masks are white leaves on black (alpha unused) or carry a real alpha: take whichever varies.
    mask = px[..., 3] if px[..., 3].std() > 0.05 else px[..., 0]
    f = w // size
    mask = mask[:size * f, :size * f].reshape(size, f, size, f).mean(axis=(1, 3))
    bpy.data.images.remove(src)
    mask = np.clip((mask - 0.35) / 0.3, 0, 1)                        # crisp edges, as the kit's clip value
    inside = blur(mask, 6)                                           # ~1 at a leaf's middle, falls off at its rim
    centre = np.clip((inside - 0.45) / 0.5, 0, 1)
    patches = value_noise(size, 7, spec["seed"]) * 0.6 + value_noise(size, 23, spec["seed"] + 1) * 0.4
    t = np.clip(0.15 + centre * 0.55 + (patches - 0.5) * 0.5, 0, 1)
    # Byte images store their pixels as sRGB values, so the painting happens in sRGB.
    dark, light = np.asarray(spec["dark"], np.float32), np.asarray(spec["light"], np.float32)
    rgb = dark[None, None, :] * (1 - t[..., None]) + light[None, None, :] * t[..., None]
    rgb *= (0.82 + 0.18 * np.clip(inside * 1.6, 0, 1))[..., None]    # painted rim line
    # Transparent texels take the mid colour, so mipmaps do not bleed black into the leaf edges.
    rgb = np.where(mask[..., None] > 0.01, rgb, (dark + light)[None, None, :] / 2)
    # Atlas of the same leaves in four tones (2x2, darkest bottom left); assign_tones points each card at one, so
    # the crown is shaded with a plain lit material.
    out = np.zeros((size * 2, size * 2, 4), np.float32)
    for i, tone in enumerate(TONES):
        row, col = divmod(i, 2)
        out[row * size:(row + 1) * size, col * size:(col + 1) * size] = np.dstack([np.clip(rgb * tone, 0, 1), mask])
    img = bpy.data.images.new(spec["name"] + "_Leaves", size * 2, size * 2, alpha=True)
    img.colorspace_settings.name = "sRGB"
    img.pixels.foreach_set(out.ravel())
    path = os.path.join(TEXTURES, "T_%s_Leaves.png" % spec["name"])
    img.filepath_raw = path; img.file_format = "PNG"; img.save()
    img.reload()
    spec["mask"] = mask   # place_fruits only hangs fruit where a leaf really is
    return path


def leaf_material(spec, texture_path):
    mat = bpy.data.materials.new("M_%s_Leaves" % spec["name"])
    mat.use_nodes = True
    nt = mat.node_tree; nodes = nt.nodes; links = nt.links
    bsdf = nodes["Principled BSDF"]
    tex = nodes.new("ShaderNodeTexImage"); tex.image = bpy.data.images.load(texture_path)
    links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    clip = nodes.new("ShaderNodeMath"); clip.operation = "GREATER_THAN"; clip.inputs[1].default_value = 0.5
    links.new(tex.outputs["Alpha"], clip.inputs[0]); links.new(clip.outputs[0], bsdf.inputs["Alpha"])
    bsdf.inputs["Roughness"].default_value = 0.75
    bsdf.inputs["Subsurface Weight"].default_value = 0.0
    mat.surface_render_method = "DITHERED"
    mat.use_backface_culling = False
    return mat


def trunk_material():
    mat = bpy.data.materials.get("M_FruitTree_Trunk")
    if mat: return mat
    mat = bpy.data.materials.new("M_FruitTree_Trunk"); mat.use_nodes = True
    nt = mat.node_tree
    tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = bpy.data.images.load(os.path.join(TEXTURES, "T_FruitTree_Trunk.png"))
    uv = nt.nodes.new("ShaderNodeUVMap"); uv.uv_map = "UVMap"
    nt.links.new(uv.outputs["UV"], tex.inputs["Vector"])
    nt.links.new(tex.outputs["Color"], nt.nodes["Principled BSDF"].inputs["Base Color"])
    nt.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.85
    return mat


def fruit_material():
    mat = bpy.data.materials.get("Free Fruits_M")
    if mat: return mat
    mat = bpy.data.materials.new("Free Fruits_M"); mat.use_nodes = True
    nt = mat.node_tree
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(os.path.join(FRUITS, "Texture", "Texture_BaseColor_v02.png"))
    nt.links.new(tex.outputs["Color"], nt.nodes["Principled BSDF"].inputs["Base Color"])
    nt.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.45
    return mat


def save_trunk_texture():
    """The kit's 16-bit trunk colour map, saved as an 8-bit 1024 PNG."""
    path = os.path.join(TEXTURES, "T_FruitTree_Trunk.png")
    src = bpy.data.images.load(os.path.join(LUX, "T_Trunk_D.png"))
    src.scale(1024, 1024)
    settings = bpy.context.scene.render.image_settings
    settings.file_format = "PNG"; settings.color_mode = "RGB"; settings.color_depth = "8"
    src.save_render(path)
    bpy.data.images.remove(src)
    return path


def copy_tree(source, spec):
    src = source.objects[spec["tree"]]
    obj = src.copy(); obj.data = src.data.copy(); obj.name = spec["name"]
    bpy.context.scene.collection.objects.link(obj)
    # Objects loaded from a library but not in a scene have no evaluated matrix_world: build it from the transform.
    obj.data.transform(Matrix.LocRotScale(None, src.rotation_euler.to_quaternion(), src.scale))
    obj.location = (0, 0, 0); obj.rotation_euler = (0, 0, 0); obj.scale = (1, 1, 1)
    me = obj.data
    trunk_index = next(i for i, m in enumerate(me.materials) if m.name.lower().startswith("mi_trunk"))
    trunk_verts = {v for p in me.polygons if p.material_index == trunk_index for v in p.vertices}
    zs = [me.vertices[v].co.z for v in trunk_verts]
    floor = min(zs)
    foot = [me.vertices[v].co for v in trunk_verts if me.vertices[v].co.z < floor + 0.25]
    base = Vector((sum(c.x for c in foot) / len(foot), sum(c.y for c in foot) / len(foot), floor))
    me.transform(Matrix.Translation(-base))
    # Trunk always first, leaves second: the Unity importer assigns its materials in that order.
    if trunk_index != 0:
        # (Assign the slots in place: clearing the list would also reset every face's material index.)
        mats = list(me.materials)
        me.materials[0], me.materials[1] = mats[trunk_index], mats[1 - trunk_index]
        for p in me.polygons: p.material_index = 1 - p.material_index
    for attr in list(me.color_attributes): me.color_attributes.remove(attr)
    return obj, 0


def crown_centre(obj, trunk_index):
    me = obj.data
    pts = [me.vertices[v].co for p in me.polygons if p.material_index != trunk_index for v in p.vertices]
    return sum(pts, Vector()) / len(pts)


def make_pine(spec, trunk_material_name="MI_Trunk"):
    """A conical pine: a tapering trunk, whorls of slightly drooping branches that shorten towards the top, and on
    each branch clusters of needle cards (two crossed quads), plus a tuft at the tip. Material 0 is the bark (UVs tile
    well outside 0-1, as the Unity importer tells bark by), material 1 the needles (each card shows the whole leaf
    mask, toned afterwards like the kit's trees). Origin at the foot of the trunk."""
    rng = random.Random(spec["seed"])
    H = spec["height_m"]
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")

    def tube(a, b, r0, r1, sides, mat):
        axis = (b - a).normalized()
        ref = Vector((0, 0, 1)) if abs(axis.z) < 0.95 else Vector((1, 0, 0))
        u = axis.cross(ref).normalized(); v = axis.cross(u)
        length = (b - a).length
        ring = lambda c, r: [bm.verts.new(c + (u * math.cos(2 * math.pi * i / sides) + v * math.sin(2 * math.pi * i / sides)) * r) for i in range(sides)]
        ra, rb = ring(a, r0), ring(b, r1)
        for i in range(sides):
            f = bm.faces.new([ra[i], ra[(i + 1) % sides], rb[(i + 1) % sides], rb[i]])
            f.material_index = mat
            for loop, (s, t) in zip(f.loops, ((i, 0), (i + 1, 0), (i + 1, 1), (i, 1))):
                loop[uv].uv = (1.5 + 2.0 * s / sides, 1.2 + length * t / 0.8)   # bark tiles, all outside 0-1
        return axis

    def card(centre, along, up, size):
        """A needle card: the mask's foot at the branch, growing out along `along`, facing `up`."""
        side = along.cross(up).normalized()
        corners = [centre - side * size * 0.5, centre + side * size * 0.5,
                   centre + side * size * 0.5 + along * size, centre - side * size * 0.5 + along * size]
        f = bm.faces.new([bm.verts.new(c) for c in corners])
        f.material_index = 1
        for loop, st in zip(f.loops, ((0, 0), (1, 0), (1, 1), (0, 1))):
            loop[uv].uv = st

    # Trunk, leaning a touch.
    lean = Vector((rng.uniform(-0.15, 0.15), rng.uniform(-0.15, 0.15), 0))
    top = Vector((0, 0, H)) + lean
    tube(Vector((0, 0, -0.1)), top, 0.026 * H + 0.02, 0.03, 10, 0)
    trunk_at = lambda z: lean * (z / H) + Vector((0, 0, z))

    first, last = spec.get("clear_m", 1.5), H - 0.55          # clear_m: bare trunk below the lowest branches
    whorls = spec.get("whorls", 14)
    max_reach = H * spec.get("reach", 0.33)
    for w in range(whorls):
        t = w / (whorls - 1)
        z = first + (last - first) * t + rng.uniform(-0.12, 0.12)
        reach = max_reach * (1 - t) ** 0.85 + 0.35
        count = rng.randint(5, 7)
        spin = rng.uniform(0, 2 * math.pi)
        for k in range(count):
            a = spin + 2 * math.pi * k / count + rng.uniform(-0.25, 0.25)
            out = Vector((math.cos(a), math.sin(a), 0))
            droop = -0.1 - (0.25 if spec.get("clear_m", 1.5) < 2 else 0.1) * (1 - t) + rng.uniform(-0.05, 0.05)
            base = trunk_at(z)
            tip = base + (out + Vector((0, 0, droop))).normalized() * reach
            axis = tube(base, tip, 0.03 * (1 - t) + 0.012, 0.006, 4, 0)
            # Needle clusters along the branch: two crossed cards each, bigger nearer the trunk.
            n = 4 + int(reach > 1.2) + int(reach > 2.2)
            for c in range(n):
                f = (c + 0.6) / (n + 0.2)
                p = base + (tip - base) * f
                size = reach * (0.75 - 0.35 * f) + 0.25
                card(p - axis * size * 0.35, axis, Vector((0, 0, 1)), size)
                roll = Matrix.Rotation(rng.uniform(0.9, 1.3), 3, axis)
                card(p - axis * size * 0.35, axis, roll @ Vector((0, 0, 1)), size * 0.9)
                card(p - axis * size * 0.35, axis, roll.transposed() @ Vector((0, 0, 1)), size * 0.85)
    # The tip: a tuft of upright cards.
    for k in range(5):
        a = 2 * math.pi * k / 5
        up = Vector((math.cos(a), math.sin(a), 0))
        card(trunk_at(last - 0.35), Vector((0.3 * math.cos(a + 1.5), 0.3 * math.sin(a + 1.5), 1)).normalized(), up, 1.3)

    me = bpy.data.meshes.new(spec["name"])
    bm.to_mesh(me); bm.free()
    me.materials.append(bpy.data.materials.new(trunk_material_name))
    me.materials.append(bpy.data.materials.new("MI_Leaf"))
    obj = bpy.data.objects.new(spec["name"], me)
    bpy.context.scene.collection.objects.link(obj)
    return obj, 0


def densify(obj, trunk_index, layers):
    """A fuller crown: the leaf cards again, turned about the trunk and drawn in towards the crown's middle, once per
    layer (degrees, scale), filling the gaps between the clusters. The copies keep their UVs, so they are toned like
    the rest afterwards."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    leaf = [f for f in bm.faces if f.material_index != trunk_index]
    pts = [v.co.copy() for f in leaf for v in f.verts]
    centre = sum(pts, Vector()) / len(pts)
    for angle, scale in layers:
        ret = bmesh.ops.duplicate(bm, geom=leaf)
        turn = Matrix.Rotation(math.radians(angle), 3, 'Z')
        for v in (g for g in ret["geom"] if isinstance(g, bmesh.types.BMVert)):
            d = turn @ (v.co - centre)
            v.co = centre + Vector((d.x * scale, d.y * scale, d.z * (0.5 + 0.5 * scale)))
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


def assign_tones(obj, trunk_index, seed):
    """Points every leaf card at one of the atlas tones: darker low and inside the crown, lighter high and outside,
    with a little randomness so the steps between tones do not show as bands."""
    rng = random.Random(seed)
    me = obj.data
    leaf = [p for p in me.polygons if p.material_index != trunk_index]
    pts = [me.vertices[v].co for p in leaf for v in p.vertices]
    zmin, zmax = min(p.z for p in pts), max(p.z for p in pts)
    centre = sum(pts, Vector()) / len(pts)
    squash = lambda p: Vector((p.x - centre.x, p.y - centre.y, (p.z - centre.z) * 1.3))
    reach = max(squash(p).length for p in pts)
    uv = me.uv_layers["UVMap"]
    for poly in leaf:
        c = poly.center
        height = (c.z - zmin) / max(zmax - zmin, 1e-3)
        outer = min(squash(c).length / reach, 1.0)
        k = 0.45 * height + 0.55 * outer + rng.uniform(-0.18, 0.18)
        tone = min(len(TONES) - 1, max(0, int(k * len(TONES))))
        row, col = divmod(tone, 2)
        for li in poly.loop_indices:
            u, v = uv.data[li].uv
            # A small inset keeps the bilinear filter inside the tone's quarter.
            uv.data[li].uv = (col * 0.5 + 0.004 + u * 0.492, row * 0.5 + 0.004 + v * 0.492)


PEAR_TEXTURE = "T_Pear.png"


def pear_texture():
    """Skin gradient for the pear (64 x 256): yellow-green belly, greener shoulders and neck, a soft blush on one side
    of the belly (u near 0.25), brown stem at the very top (v above 0.94)."""
    path = os.path.join(TEXTURES, PEAR_TEXTURE)
    w, h = 64, 256
    v = (np.arange(h, dtype=np.float32) + 0.5)[:, None] / h
    u = (np.arange(w, dtype=np.float32) + 0.5)[None, :] / w
    belly = np.array([0.80, 0.80, 0.28], np.float32)      # sRGB
    neck = np.array([0.58, 0.68, 0.22], np.float32)
    t = np.clip((v - 0.15) / 0.7, 0, 1)[..., None]
    rgb = belly * (1 - t) + neck * t
    blush = np.exp(-((u - 0.25) ** 2) / 0.02 - ((v - 0.35) ** 2) / 0.03)[..., None] * 0.35
    rgb = rgb * (1 - blush) + np.array([0.82, 0.48, 0.20], np.float32) * blush
    speck = (value_noise(256, 40, 5)[:h, :w] - 0.5) * 0.06
    rgb = np.clip(rgb + speck[..., None], 0, 1)
    rgb = np.where((v > 0.94)[..., None], np.array([0.36, 0.24, 0.12], np.float32), rgb)
    out = np.dstack([np.broadcast_to(rgb, (h, w, 3)), np.ones((h, w), np.float32)]).astype(np.float32)
    img = bpy.data.images.new("T_Pear", w, h, alpha=True)
    img.pixels.foreach_set(out.ravel())
    img.filepath_raw = path; img.file_format = "PNG"; img.save()
    return path


def make_pear():
    """A low-poly pear in the Free Fruits style: a lathed body (wide belly, narrow neck, a slight lean) and a short
    bent stem, UV-mapped onto the pear gradient (v = height). Origin at the top of the stem, like the other fruit."""
    # A round belly (no flat bottom), a soft waist and a tapering neck.
    profile = [(0.00, 0.00), (0.14, 0.012), (0.27, 0.045), (0.38, 0.10), (0.45, 0.17), (0.49, 0.26), (0.48, 0.35),
               (0.43, 0.45), (0.35, 0.55), (0.29, 0.64), (0.25, 0.73), (0.21, 0.82), (0.15, 0.90), (0.07, 0.96), (0.00, 0.98)]
    segments = 14
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    rings = []
    for r, z in profile:
        lean = 0.05 * z * z                          # pears are seldom straight
        if r == 0.0:
            rings.append([bm.verts.new((lean, 0.0, z))])
            continue
        rings.append([bm.verts.new((math.cos(a) * r + lean, math.sin(a) * r, z))
                      for a in (i * math.tau / segments for i in range(segments))])
    def face(verts, uvs):
        f = bm.faces.new(verts)
        f.smooth = True
        for loop, coord in zip(f.loops, uvs): loop[uv].uv = coord
    for k in range(len(rings) - 1):
        a, b = rings[k], rings[k + 1]
        va, vb = profile[k][1] * 0.9, profile[k + 1][1] * 0.9
        for i in range(segments):
            j = (i + 1) % segments
            ua, ub = i / segments, (i + 1) / segments
            if len(a) == 1:
                face([a[0], b[j], b[i]], [(ua + 0.5 / segments, va), (ub, vb), (ua, vb)])
            elif len(b) == 1:
                face([a[i], a[j], b[0]], [(ua, va), (ub, va), (ua + 0.5 / segments, vb)])
            else:
                face([a[i], a[j], b[j], b[i]], [(ua, va), (ub, va), (ub, vb), (ua, vb)])
    # Stem: a thin, slightly bent six-sided twig out of the top.
    stem_rings = []
    for k, (h, bend) in enumerate(((0.95, 0.0), (1.05, 0.02), (1.15, 0.06))):
        stem_rings.append([bm.verts.new((0.05 * 0.98 + bend + math.cos(a) * 0.025, math.sin(a) * 0.025, h))
                           for a in (i * math.tau / 6 for i in range(6))])
    for k in range(2):
        for i in range(6):
            j = (i + 1) % 6
            face([stem_rings[k][i], stem_rings[k][j], stem_rings[k + 1][j], stem_rings[k + 1][i]],
                 [(i / 6, 0.95), ((i + 1) / 6, 0.95), ((i + 1) / 6, 0.99), (i / 6, 0.99)])
    cap = bm.faces.new(stem_rings[2][::-1])
    for loop in cap.loops: loop[uv].uv = (0.5, 0.99)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new("Pear")
    bm.to_mesh(me); bm.free()
    top = max(v.co.z for v in me.vertices)
    me.transform(Matrix.Translation(Vector((-0.02, 0.0, -top))))
    mat = bpy.data.materials.get("M_Pear") or bpy.data.materials.new("M_Pear")
    if not mat.node_tree.nodes.get("PearTex"):
        tex = mat.node_tree.nodes.new("ShaderNodeTexImage"); tex.name = "PearTex"
        tex.image = bpy.data.images.load(pear_texture())
        mat.node_tree.links.new(tex.outputs["Color"], mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
        mat.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.5
    me.materials.append(mat)
    obj = bpy.data.objects.new("Pear", me)
    return obj, top


def import_fruit(name):
    if name == "Pear":
        return make_pear()
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=os.path.join(FRUITS, "Model", name + ".fbx"))
    new = [o for o in bpy.data.objects if o not in before]
    mesh = next(o for o in new if o.type == "MESH")
    mesh.data.transform(mesh.matrix_world); mesh.matrix_world = Matrix.Identity(4)
    for o in new:
        if o is not mesh: bpy.data.objects.remove(o)
    me = mesh.data
    zs = [v.co.z for v in me.vertices]
    top = max(zs)
    xs = [v.co.x for v in me.vertices]; ys = [v.co.y for v in me.vertices]
    # Pivot at the top middle (where the stem meets the branch), so the fruit hangs from it.
    me.transform(Matrix.Translation(-Vector(((min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2, top))))
    me.materials.clear(); me.materials.append(fruit_material())
    # Seen from the ground among dozens of others, a fruit needs no more than ~260 triangles (the cherry pair has 830).
    faces = sum(len(p.vertices) - 2 for p in me.polygons)
    if faces > FRUIT_MAX_TRIANGLES:
        mod = mesh.modifiers.new("Decimate", "DECIMATE"); mod.ratio = FRUIT_MAX_TRIANGLES / faces
        bpy.context.scene.collection.objects.link(mesh) if mesh.name not in bpy.context.scene.objects else None
        bpy.context.view_layer.objects.active = mesh
        bpy.ops.object.modifier_apply(modifier=mod.name)
    for c in list(mesh.users_collection): c.objects.unlink(mesh)
    return mesh, top - min(zs)


def place_fruits(obj, trunk_index, spec, centre):
    rng = random.Random(spec["seed"])
    template, height = import_fruit(spec["fruit"])
    scale = spec["height"] / height
    me = obj.data
    bm = bmesh.new(); bm.from_mesh(me)
    leaf_faces = [f for f in bm.faces if f.material_index != trunk_index]
    trunk_faces = [f for f in bm.faces if f.material_index == trunk_index]
    for f in trunk_faces: f.tag = True
    bvh = BVHTree.FromBMesh(bm)
    leaf_set = {f.index for f in leaf_faces}
    bm.faces.ensure_lookup_table()
    uv_layer = bm.loops.layers.uv["UVMap"]
    mask = spec["mask"]; size = mask.shape[0]

    def on_leaf(index, point):
        face = bm.faces[index]
        weights = poly_3d_calc([l.vert.co for l in face.loops], point)
        uv = sum((l[uv_layer].uv * w for l, w in zip(face.loops, weights)), Vector((0, 0)))
        x = min(size - 1, max(0, int(uv.x * size))); y = min(size - 1, max(0, int(uv.y * size)))
        return mask[y, x] > 0.5
    pts = [v.co for f in leaf_faces for v in f.verts]
    radius = max((p - centre).length for p in pts)
    zmin = min(p.z for p in pts)
    placed = []
    spacing = spec["height"] * 2.6
    n = 600
    candidates = []
    for i in range(n):
        # Fibonacci sphere, squashed: sides and underside of the crown, little on top.
        z = 1 - 2 * (i + 0.5) / n
        if z > 0.45: continue
        r = math.sqrt(1 - z * z); a = i * 2.399963
        candidates.append(Vector((math.cos(a) * r, math.sin(a) * r, z)))
    rng.shuffle(candidates)
    for d in candidates:
        if len(placed) >= spec["count"]: break
        origin = centre + d * radius * 1.6
        hit, normal, index, dist = bvh.ray_cast(origin, -d, radius * 1.6)
        if hit is None or index not in leaf_set: continue
        if not on_leaf(index, hit): continue
        if hit.z < zmin + 0.4: continue
        # Sink the fruit a little into the foliage so it hangs from it rather than floating in front.
        spot = hit - d * spec["height"] * 0.15
        if any((spot - q).length < spacing for q in placed): continue
        placed.append(spot)
    bm.free()
    # One object per fruit (Fruit_00, Fruit_01, ...), all sharing the fruit's mesh: in the game each one falls from
    # the tree on its own (FruitTree.cs). Its origin is where the stem meets the twig.
    fruit_mesh = template.data
    fruit_mesh.name = spec["name"] + "_Fruit"
    fruits = []
    for i, spot in enumerate(placed):
        rot = (Matrix.Rotation(rng.uniform(0, math.tau), 4, "Z") @ Matrix.Rotation(rng.uniform(-0.25, 0.25), 4, "X")
               @ Matrix.Rotation(rng.uniform(-0.25, 0.25), 4, "Y"))
        s = scale * rng.uniform(0.88, 1.1)
        fobj = bpy.data.objects.new("Fruit_%02d" % i, fruit_mesh)
        bpy.context.scene.collection.objects.link(fobj)
        fobj.matrix_world = Matrix.Translation(spot) @ rot @ Matrix.Diagonal((s, s, s, 1))
        fobj.parent = obj
        fruits.append(fobj)
    bpy.data.objects.remove(template) if template.name in bpy.data.objects else None
    return fruits, len(placed)


def main():
    for o in list(bpy.data.objects): bpy.data.objects.remove(o)
    with bpy.data.libraries.load(TREES_BLEND) as (src, dst):
        dst.objects = list(dict.fromkeys(s["tree"] for s in SPECS if s["tree"] != "PINE"))
    source = type("Src", (), {"objects": {o.name: o for o in dst.objects}})
    save_trunk_texture()
    trunk = trunk_material()
    x = 0.0
    report = []
    built = []
    for spec in SPECS:
        obj, trunk_index = make_pine(spec) if spec["tree"] == "PINE" else copy_tree(source, spec)
        if spec.get("height_m") and spec["tree"] != "PINE":
            # The kit's trees come in very different sizes: bring this one to the wanted height (from its foot).
            zs = [v.co.z for v in obj.data.vertices]
            obj.data.transform(Matrix.Scale(spec["height_m"] / (max(zs) - min(zs)), 4))
            obj.data.update()
        if spec.get("dense"):
            densify(obj, trunk_index, spec["dense"])
        leaves = leaf_material(spec, paint_leaf_texture(spec))
        obj.data.materials[0] = trunk
        obj.data.materials[1] = leaves
        # Fruits first: they look the leaves up in the cards' original UVs, which assign_tones then moves.
        fobj, count = place_fruits(obj, trunk_index, spec, crown_centre(obj, trunk_index)) if spec["count"] > 0 else ([], 0)
        assign_tones(obj, trunk_index, spec["seed"])
        built.append((obj, fobj))
        dims = obj.dimensions
        report.append("%s: %.1f x %.1f x %.1f m, %d faces, %d fruits" % (spec["name"], dims.x, dims.y, dims.z,
                                                                       len(obj.data.polygons), count))
    for o in dst.objects: bpy.data.objects.remove(o)
    if EXPORT:
        export(built)
    # Side by side for the preview renders and the saved file.
    for obj, _ in built:
        obj.location.x = x + obj.dimensions.x / 2
        x += obj.dimensions.x + 1.5
    print("\n".join(report))
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, BLEND_NAME))


def export(built):
    """One FBX per tree into the Unity project (pivot at the foot of the trunk, Unity's axes), plus its textures."""
    os.makedirs(os.path.join(UNITY, "Textures"), exist_ok=True)
    for obj, fobj in built:
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        for f in fobj: f.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.export_scene.fbx(filepath=os.path.join(UNITY, obj.name + ".fbx"), use_selection=True,
                                 object_types={"MESH"}, apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
                                 axis_forward="-Z", axis_up="Y", bake_space_transform=True, mesh_smooth_type="FACE",
                                 use_triangles=True, add_leaf_bones=False, path_mode="STRIP", embed_textures=False,
                                 use_custom_props=False, colors_type="NONE")
    for name in os.listdir(TEXTURES):
        shutil.copyfile(os.path.join(TEXTURES, name), os.path.join(UNITY, "Textures", name))


main()
