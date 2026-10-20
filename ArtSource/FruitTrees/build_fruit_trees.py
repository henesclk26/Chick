"""Builds the apple, orange and cherry trees: Lux Art Studios stylised trees (Assets/Tree Assets/Trees.blend) with the
PolyOne Free Fruits hanging in their crowns.

Run (Blender 5.x, background):
    blender -b --factory-startup --python ArtSource/FruitTrees/build_fruit_trees.py -- <project root> <lux textures dir> [--export]

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

SPECS = [
    # name, source tree, leaf mask, leaf dark, leaf light (sRGB), fruit model, fruit height (m), count, seed
    dict(name="AppleTree", tree="S_Tree_G", leaf="T_Leaf_4", dark=(0.20, 0.42, 0.13), light=(0.50, 0.72, 0.25),
         fruit="SM_Fruits_Apple_R1", height=0.22, count=24, seed=11),
    dict(name="OrangeTree", tree="S_Tree_D", leaf="T_Leaf_4", dark=(0.09, 0.28, 0.11), light=(0.27, 0.50, 0.19),
         fruit="SM_Fruits_Orange_1", height=0.24, count=20, seed=23),
    dict(name="CherryTree", tree="S_Tree_C", leaf="T_Leaf_2", dark=(0.24, 0.46, 0.14), light=(0.58, 0.77, 0.30),
         fruit="SM_Fruits_Cherries_2", height=0.24, count=30, seed=37),
]


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


def import_fruit(name):
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
        dst.objects = [s["tree"] for s in SPECS]
    source = type("Src", (), {"objects": {o.name: o for o in dst.objects}})
    save_trunk_texture()
    trunk = trunk_material()
    x = 0.0
    report = []
    built = []
    for spec in SPECS:
        obj, trunk_index = copy_tree(source, spec)
        leaves = leaf_material(spec, paint_leaf_texture(spec))
        obj.data.materials[0] = trunk
        obj.data.materials[1] = leaves
        # Fruits first: they look the leaves up in the cards' original UVs, which assign_tones then moves.
        fobj, count = place_fruits(obj, trunk_index, spec, crown_centre(obj, trunk_index))
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
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "FruitTrees.blend"))


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
