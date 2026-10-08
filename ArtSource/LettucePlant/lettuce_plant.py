"""LettucePlant: a low-poly lettuce head on a rocky dirt mound (after the four reference views the team chose):
a rosette of broad, ruffled, crumpled lime-green leaves with pale midribs, outer leaves leaning out and curling
back, inner leaves wrapping a tight heart.

It is an edible plant like StrawberryBush (see Assets/Editor/Eating/BerryPlantImporter.cs), except that what
drops and gets eaten is a leaf: every leaf is its own object Lettuce_NN (the heart's small leaves too), Mound is
the base. Each leaf is modelled flat in its own frame (blade in local XY, length along +Y, face normal +Z, origin
at the centre of its bounds) and turned into the rosette by its object rotation, so in the game it can lie down
flat on the ground when it falls (BerryPlant's layFlat).

Look: flat-shaded facets; leaves sample a smooth gradient band of T_LettucePlant_Palette (pale midrib -> lime
blade -> lighter edge, darker at the base), the mound and rocks flat swatches.
Units are metres at 1/SCALE size (the head is ~1 m across there), Z up, origin at the plant's foot.
Run inside Blender: exec(code, {"__file__": path}); build(); export().
"""
import math
import os
import random
import shutil

import bmesh
import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.abspath(os.path.join(HERE, "..", "..", "Assets", "Art", "LettucePlant"))
SCALE = 0.85  # exported ~0.85 m across: a big head next to the 0.25 m chick, smaller than the berry bushes

PALETTE = [  # sRGB flat swatches (top half of the texture, 8 x 4 cells of 32 px)
    ("dirt", (0.45, 0.31, 0.23)), ("dirt_dark", (0.39, 0.27, 0.20)), ("dirt_light", (0.50, 0.35, 0.26)),
    ("rock_light", (0.66, 0.50, 0.38)), ("rock", (0.58, 0.42, 0.32)), ("rock_dark", (0.49, 0.35, 0.27)),
    ("core", (0.62, 0.80, 0.36)),
]
GRID, ROWS, CELL = 8, 4, 32
SIZE = GRID * CELL
# Gradient bands (pixel rows from the top). U: midrib -> edge, V: base -> tip.
GRADIENTS = {"leaf": (128, 192), "heart": (192, 256)}
INDEX = {name: i for i, (name, _) in enumerate(PALETTE)}

rng = random.Random(11)
Z = Vector((0, 0, 1))
MOUND_TOP = 0.12


def swatch_uv(name):
    i = INDEX[name]
    return ((i % GRID + .5) / GRID, 1 - (i // GRID + .5) * CELL / SIZE)


def grad_uv(band, u, v):
    y0, y1 = GRADIENTS[band]
    u, v = min(max(u, 0.0), 1.0), min(max(v, 0.0), 1.0)
    return ((4 + u * (SIZE - 8)) / SIZE, 1 - (y0 + 3 + (1 - v) * (y1 - y0 - 6)) / SIZE)


def _mix(a, b, t):
    t = min(max(t, 0.0), 1.0)
    return tuple(x + (y - x) * t for x, y in zip(a, b))


def palette_image(folder):
    import numpy as np
    name = "T_LettucePlant_Palette"
    image = bpy.data.images.get(name)
    if image is not None and (tuple(image.size) != (SIZE, SIZE) or len(image.pixels) != SIZE * SIZE * 4):
        image.name = name + "_old"  # never remove an image the viewport draws (that crashed the GPU driver)
        image = None
    if image is None:
        image = bpy.data.images.new(name, SIZE, SIZE, alpha=False)
    img = np.zeros((SIZE, SIZE, 3))  # row 0 is the top of the image
    for i in range(GRID * ROWS):
        color = PALETTE[i][1] if i < len(PALETTE) else (1.0, 0.0, 1.0)
        img[(i // GRID) * CELL:(i // GRID + 1) * CELL, (i % GRID) * CELL:(i % GRID + 1) * CELL] = color
    bands = {
        # pale rib, blade at the base, blade at the top, edge
        "leaf": ((0.74, 0.88, 0.46), (0.33, 0.56, 0.12), (0.50, 0.77, 0.17), (0.64, 0.88, 0.27)),
        "heart": ((0.82, 0.92, 0.56), (0.50, 0.74, 0.22), (0.64, 0.86, 0.30), (0.74, 0.92, 0.40)),
    }
    for band, (rib, base, top, edge) in bands.items():
        y0, y1 = GRADIENTS[band]
        for row in range(y1 - y0):
            v = 1 - row / (y1 - y0 - 1)  # band top = leaf tip
            blade = _mix(base, top, v ** 0.8)
            for col in range(SIZE):
                u = col / (SIZE - 1)
                c = _mix(blade, edge, max(u - 0.35, 0.0) / 0.65)
                rib_w = max(0.0, 1 - u / 0.1) * (1 - 0.7 * v)  # the midrib fades towards the tip
                img[y0 + row, col] = _mix(c, rib, rib_w)
    rgba = np.concatenate([np.clip(img, 0, 1), np.ones((SIZE, SIZE, 1))], axis=2)[::-1]
    image.colorspace_settings.name = "sRGB"
    image.pixels.foreach_set(rgba.astype(np.float32).ravel())
    image.filepath_raw = os.path.join(folder, name + ".png")
    image.file_format = "PNG"
    image.save()
    return image


class Part:
    def __init__(self, name):
        self.name = name
        self.verts, self.faces, self.uvs = [], [], []

    def add(self, verts, faces, color=None, vert_uv=None, two_sided=False):
        """color: a swatch name, or a function (face centre, normal) -> name; vert_uv: per-vertex UVs."""
        verts = [Vector(v) for v in verts]
        if vert_uv is not None:
            face_uvs = [[vert_uv[i] for i in f] for f in faces]
        else:
            face_uvs = []
            for f in faces:
                if callable(color):
                    pts = [verts[i] for i in f]
                    nrm = (pts[1] - pts[0]).cross(pts[2] - pts[0])
                    c = color(sum(pts, Vector()) / len(pts), nrm.normalized() if nrm.length > 1e-12 else Z)
                else:
                    c = color
                face_uvs.append([swatch_uv(c)] * len(f))
        base = len(self.verts)
        self.verts += verts
        self.faces += [[base + i for i in f] for f in faces]
        self.uvs += face_uvs
        if two_sided:  # the back side on its own vertices (Unity culls back faces)
            back = len(self.verts)
            self.verts += [v.copy() for v in verts]
            self.faces += [[back + i for i in reversed(f)] for f in faces]
            self.uvs += [list(reversed(uv)) for uv in face_uvs]


def icosphere(subdiv):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    bm.verts.index_update()
    verts = [v.co.copy() for v in bm.verts]
    faces = [[v.index for v in f.verts] for f in bm.faces]
    bm.free()
    return verts, faces


def rock(part, center, radii):
    verts, faces = icosphere(1)
    c = Vector(center)
    yaw = rng.uniform(0, math.pi)
    pts = []
    for d in verts:
        j = 1 + rng.uniform(-0.16, 0.16)
        x, y = d.x * math.cos(yaw) - d.y * math.sin(yaw), d.x * math.sin(yaw) + d.y * math.cos(yaw)
        pts.append(c + Vector((x * radii[0], y * radii[1], max(d.z, -0.45) * radii[2])) * j)
    part.add(pts, faces, lambda _c, n: "rock_light" if n.z > 0.55 else rng.choice(("rock", "rock", "rock_dark")))


def mound(part):
    """Low crumpled dirt mound with a short steep edge, big angular rocks around the rim, a few on top."""
    rings = [(0.0, MOUND_TOP), (0.1, MOUND_TOP), (0.2, MOUND_TOP - 0.005), (0.3, MOUND_TOP - 0.015),
             (0.39, MOUND_TOP - 0.035), (0.45, MOUND_TOP - 0.06), (0.49, 0.015), (0.51, -0.04)]
    seg = 18
    verts = []
    for r, z in rings:
        if r == 0:
            verts.append(Vector((0, 0, z)))
            continue
        for k in range(seg):
            a = 2 * math.pi * (k + rng.uniform(-0.25, 0.25)) / seg
            rr = r * rng.uniform(0.94, 1.06)
            verts.append(Vector((rr * math.cos(a), rr * math.sin(a), z + rng.uniform(-0.015, 0.015))))
    faces = [(0, 1 + k, 1 + (k + 1) % seg) for k in range(seg)]
    for i in range(len(rings) - 2):
        b0, b1 = 1 + i * seg, 1 + (i + 1) * seg
        for k in range(seg):
            j = (k + 1) % seg
            faces.append((b0 + k, b1 + k, b1 + j, b0 + j))
    part.add(verts, faces, lambda _c, n: rng.choice(("dirt", "dirt", "dirt_dark", "dirt_light"))
             if n.z > 0.6 else "dirt_dark")
    for k in range(11):
        a = 2 * math.pi * (k + rng.uniform(-0.3, 0.3)) / 11
        r = rng.uniform(0.4, 0.47)
        size = rng.uniform(0.075, 0.11)
        rock(part, (r * math.cos(a), r * math.sin(a), 0.05), (size * 1.35, size, size * 0.8))
    for k in range(5):
        a = rng.uniform(0, 2 * math.pi)
        r = rng.uniform(0.28, 0.36)
        size = rng.uniform(0.04, 0.06)
        rock(part, (r * math.cos(a), r * math.sin(a), MOUND_TOP), (size * 1.2, size, size * 0.8))


# ----------------------------------------------------------------------------------------------- leaves

def leaf_shape(length, width, cup, curl, ruffle, band):
    """One lettuce leaf in its own frame: base at the origin, length along +Y, blade across X, front face +Z.
    Obovate outline (narrow stalk, wide rounded top), cupped across (cup > 0 lifts the edges towards +Z), curled
    along its length (curl > 0 bends the top towards +Z), the edge ruffled in waves and every point nudged so the
    facets crumple. Returns (verts, faces, uvs) with the UVs on the gradient band."""
    rows, cols = 12, 6  # rows along the leaf, columns per side of the midrib (fine, even facets)
    verts, uvs, grid = [], [], []
    phase = rng.uniform(0, 2 * math.pi)
    waves = rng.uniform(6.0, 8.0)
    for i in range(rows + 1):
        v = 0.96 * i / rows
        y = v * length
        half = width * math.sin(math.pi * (0.06 + 0.94 * v)) ** 0.42 * (0.35 + 0.65 * v)
        row = []
        for j in range(-cols, cols + 1):
            u = j / cols
            x = u * half
            z = cup * width * 0.45 * u * u + curl * length * 0.35 * v * v
            z += -0.02 * length * (1 - abs(u))  # midrib sits a little lower: a groove down the leaf
            edge = abs(u) ** 3 * v ** 1.5  # ruffles only along the rim, strongest at the top
            z += ruffle * length * 0.08 * edge * math.sin(waves * (v + 0.35 * u) * math.pi + phase)
            y_ = y + ruffle * length * 0.02 * edge * math.cos(waves * v * math.pi + phase)
            p = Vector((x, y_, z)) + Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))) * (
                0.006 * length)
            row.append(len(verts))
            verts.append(p)
            uvs.append(grad_uv(band, abs(u), v))
        grid.append(row)
    tip = Vector((rng.uniform(-0.02, 0.02) * width, length * 1.0, curl * length * 0.35 + cup * 0.02))
    tip_i = len(verts)
    verts.append(tip)
    uvs.append(grad_uv(band, 0.5, 1.0))
    faces = []
    for i in range(rows):
        for k in range(2 * cols):
            faces.append((grid[i][k], grid[i][k + 1], grid[i + 1][k + 1], grid[i + 1][k]))
    for k in range(2 * cols):
        faces.append((grid[rows][k], grid[rows][k + 1], tip_i))
    return verts, faces, uvs


def rosette():
    """Leaf placements, outer ring first: (base point, yaw, tilt from vertical, length, width, cup, curl, band).
    The front of each leaf (+Z of its frame) faces the middle of the head."""
    leaves = []
    rings = [
        # count, base radius, tilt deg, length, width, cup, curl, band
        (7, 0.09, 60, 0.62, 0.46, 0.55, -0.7, "leaf"),  # outer: broad spoons leaning out, tips curling down
        (6, 0.07, 35, 0.62, 0.42, 0.7, -0.2, "leaf"),   # middle
        (5, 0.05, 18, 0.56, 0.36, 0.9, 0.15, "leaf"),   # inner: wrapping round the heart
        (4, 0.02, 6, 0.4, 0.26, 1.1, 0.45, "heart"),    # heart: smaller leaves curled tight
    ]
    for r, (count, base_r, tilt, length, width, cup, curl, band) in enumerate(rings):
        offset = 0.4 + r * math.pi / count  # each ring sits between the leaves of the one outside it
        for k in range(count):
            yaw = offset + 2 * math.pi * k / count + rng.uniform(-0.12, 0.12)
            leaves.append(dict(
                base=Vector((base_r * math.cos(yaw), base_r * math.sin(yaw), MOUND_TOP - 0.02 + 0.01 * r)),
                yaw=yaw, tilt=math.radians(tilt + rng.uniform(-5, 5)),
                length=length * rng.uniform(0.92, 1.08), width=width * rng.uniform(0.92, 1.08),
                cup=cup, curl=curl, band=band, ring=r))
    return leaves


def leaf_matrix(yaw, tilt):
    """Rotation taking the leaf frame (length +Y, face +Z) to the rosette: length leaning out by `tilt` from the
    vertical towards `yaw`, front face turned towards the middle."""
    out = Vector((math.cos(yaw), math.sin(yaw), 0))
    d = (out * math.sin(tilt) + Z * math.cos(tilt)).normalized()
    w = out.cross(Z).normalized()  # across the leaf, chosen so that w x d (the front) points inwards
    n = w.cross(d)                 # front face: inwards and up
    return Matrix((w, d, n)).transposed()


# ----------------------------------------------------------------------------------------------- assembly

def material(image):
    mat = bpy.data.materials.get("M_LettucePlant") or bpy.data.materials.new("M_LettucePlant")
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = image
    tex.interpolation = "Linear"
    bsdf.inputs["Roughness"].default_value = 0.6
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    out.location, bsdf.location, tex.location = (300, 0), (0, 0), (-350, 0)
    return mat


def make_mesh(name, verts, faces, uvs, mat):
    me = bpy.data.meshes.get(name)
    if me:
        me.name = name + "_old"
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in verts], [], faces)
    uv = me.uv_layers.new(name="UVMap")
    for poly, loop_uvs in zip(me.polygons, uvs):
        for li, u in zip(poly.loop_indices, loop_uvs):
            uv.data[li].uv = u
    me.materials.append(mat)
    me.validate()
    me.shade_flat()
    me.update()
    return me


def build():
    global rng
    rng = random.Random(11)
    mat = material(palette_image(HERE))
    coll = bpy.data.collections.get("LettucePlant")
    if coll:
        for o in list(coll.objects):
            bpy.data.objects.remove(o, do_unlink=True)
    else:
        coll = bpy.data.collections.new("LettucePlant")
        bpy.context.scene.collection.children.link(coll)
    root = bpy.data.objects.new("LettucePlant_Root", None)
    coll.objects.link(root)

    ground = Part("Mound")
    mound(ground)
    me = make_mesh("Mound", [v * SCALE for v in ground.verts], ground.faces, ground.uvs, mat)
    obj = bpy.data.objects.new("Mound", me)
    coll.objects.link(obj)
    obj.parent = root

    # A short pale stalk in the middle the leaves grow from (not edible).
    stalk = Part("Stalk")
    sv, sf = icosphere(1)
    stalk.add([Vector((v.x * 0.07, v.y * 0.07, MOUND_TOP + 0.02 + v.z * 0.06)) for v in sv], sf, "core")
    me = make_mesh("Stalk", [v * SCALE for v in stalk.verts], stalk.faces, stalk.uvs, mat)
    obj = bpy.data.objects.new("Stalk", me)
    coll.objects.link(obj)
    obj.parent = root

    for n, leaf in enumerate(rosette(), 1):
        name = "Lettuce_%02d" % n
        verts, faces, uvs = leaf_shape(leaf["length"], leaf["width"], leaf["cup"], leaf["curl"], 1.0, leaf["band"])
        part = Part(name)
        part.add(verts, faces, vert_uv=uvs, two_sided=True)
        # Origin at the centre of the leaf's own bounds, so it can turn about itself when it lies down.
        lo = Vector([min(v[i] for v in part.verts) for i in range(3)])
        hi = Vector([max(v[i] for v in part.verts) for i in range(3)])
        centre = (lo + hi) / 2
        me = make_mesh(name, [(v - centre) * SCALE for v in part.verts], part.faces, part.uvs, mat)
        obj = bpy.data.objects.new(name, me)
        coll.objects.link(obj)
        rot = leaf_matrix(leaf["yaw"], leaf["tilt"])
        obj.matrix_world = Matrix.Translation((leaf["base"] + rot @ centre) * SCALE) @ rot.to_4x4()
        obj.parent = root
        obj.matrix_parent_inverse = root.matrix_world.inverted()
    objs = [o for o in coll.objects if o.type == "MESH"]
    for me in [m for m in bpy.data.meshes if m.users == 0]:
        bpy.data.meshes.remove(me)
    return {"leaves": len([o for o in objs if o.name.startswith("Lettuce_")]),
            "faces": sum(len(o.data.polygons) for o in objs)}


def export():
    """FBX of the plant's meshes (axis conversion baked) and the palette into Assets/Art/LettucePlant."""
    textures = os.path.join(OUT, "Textures")
    os.makedirs(textures, exist_ok=True)
    shutil.copyfile(os.path.join(HERE, "T_LettucePlant_Palette.png"), os.path.join(textures, "T_LettucePlant_Palette.png"))
    objs = [o for o in bpy.data.collections["LettucePlant"].objects if o.type == "MESH"]
    bpy.context.view_layer.update()
    for o in bpy.context.scene.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    path = os.path.join(OUT, "LettucePlant.fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"}, apply_unit_scale=True,
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                             bake_space_transform=True, mesh_smooth_type="FACE", use_mesh_modifiers=True,
                             bake_anim=False, path_mode="STRIP", embed_textures=False)
    return {"fbx": path, "objects": len(objs)}
