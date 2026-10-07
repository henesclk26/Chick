"""TomatoPlant: a clean low-poly tomato plant on a rocky dirt mound (the reference: broad toothed leaflets,
a thick light-green stem, big round red tomatoes with star calyxes, chunky brown rocks).

It is the same kind of edible plant as StrawberryBush and BlackberryBush (see
Assets/Editor/Eating/BerryPlantImporter.cs): Mound, Stems, Leaves, Flowers and one object per fruit; ripe fruit are
Tomato_NN, green ones Tomato_Unripe_NN (decoration). Each fruit's origin is its centre so it can drop on its own.

Look: flat-shaded facets in clean flat colours; every piece takes one swatch of a small palette
(T_TomatoPlant_Palette), so the light alone shades the facets.
Units are metres (the chick is ~0.25 m), Z up, the origin is the foot of the stem on the ground; everything is
built at 1/SCALE size and scaled on the way out.
Run inside Blender: exec(code, {"__file__": path}); build(); export().
"""
import math
import os
import random
import shutil

import bmesh
import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.abspath(os.path.join(HERE, "..", "..", "Assets", "Art", "TomatoPlant"))
SCALE = 0.7  # built at 1.0 for readable numbers; exported at the berry bushes' size (~1.4 m wide, ~0.95 m tall)

PALETTE = [  # sRGB, row by row in an 8 x 8 grid of 16 px cells
    ("leaf", (0.42, 0.70, 0.22)), ("leaf_b", (0.47, 0.75, 0.25)), ("leaf_c", (0.38, 0.65, 0.20)),
    ("stem", (0.46, 0.71, 0.27)), ("stem_dark", (0.36, 0.60, 0.21)), ("calyx", (0.27, 0.54, 0.16)),
    ("tomato", (0.90, 0.19, 0.11)), ("tomato_b", (0.94, 0.27, 0.14)),
    ("unripe", (0.55, 0.74, 0.28)), ("turning", (0.93, 0.56, 0.18)),
    ("dirt", (0.47, 0.33, 0.24)), ("rock", (0.55, 0.40, 0.30)), ("rock_b", (0.61, 0.46, 0.35)),
    ("flower", (1.0, 0.85, 0.25)), ("flower_center", (0.95, 0.60, 0.14)),
]
GRID, CELL = 8, 16
INDEX = {name: i for i, (name, _) in enumerate(PALETTE)}

rng = random.Random(3)
Z = Vector((0, 0, 1))


def swatch_uv(name):
    i = INDEX[name]
    return ((i % GRID + .5) / GRID, 1 - (i // GRID + .5) / GRID)


def palette_image(folder):
    size = GRID * CELL
    name = "T_TomatoPlant_Palette"
    image = bpy.data.images.get(name)
    if image is not None and (tuple(image.size) != (size, size) or len(image.pixels) != size * size * 4):
        # Set the old one aside rather than removing it: removing an image the viewport draws crashed the
        # GPU driver. Unused, it is dropped when the file is saved and reopened.
        image.name = name + "_old"
        image = None
    if image is None:
        image = bpy.data.images.new(name, size, size, alpha=False)
    pixels = [0.0] * (size * size * 4)
    for i in range(GRID * GRID):
        color = PALETTE[i][1] if i < len(PALETTE) else (1.0, 0.0, 1.0)
        cx, cy = i % GRID, GRID - 1 - i // GRID
        for y in range(cy * CELL, (cy + 1) * CELL):
            for x in range(cx * CELL, (cx + 1) * CELL):
                p = (y * size + x) * 4
                pixels[p:p + 4] = [color[0], color[1], color[2], 1.0]
    image.colorspace_settings.name = "sRGB"
    image.pixels[:] = pixels
    image.filepath_raw = os.path.join(folder, name + ".png")
    image.file_format = "PNG"
    image.save()
    return image


class Part:
    def __init__(self, name):
        self.name = name
        self.verts, self.faces, self.cols = [], [], []

    def add(self, verts, faces, color, two_sided=False):
        """One palette colour for the whole piece; two_sided adds a back side on its own vertices."""
        base = len(self.verts)
        self.verts += [tuple(v) for v in verts]
        self.faces += [[base + i for i in f] for f in faces]
        self.cols += [color] * len(faces)
        if two_sided:
            back = len(self.verts)
            self.verts += [tuple(v) for v in verts]
            self.faces += [[back + i for i in reversed(f)] for f in faces]
            self.cols += [color] * len(faces)


def frame(direction, up=Z):
    """Orthonormal (t, s, n): t along direction, n as close to `up` as possible."""
    t = Vector(direction).normalized()
    s = up.cross(t)
    if s.length < 1e-6:
        s = Vector((1, 0, 0)).cross(t)
    s.normalize()
    return t, s, t.cross(s)


def tube(part, points, radii, sides, color):
    """Faceted tube along a polyline, closed at both ends."""
    verts, faces = [], []
    pts = [Vector(p) for p in points]
    for i, (p, r) in enumerate(zip(pts, radii)):
        d = pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]
        t, s, n = frame(d, Z if abs(d.normalized().z) < 0.95 else Vector((1, 0, 0)))
        for k in range(sides):
            a = 2 * math.pi * k / sides
            verts.append(p + (s * math.cos(a) + n * math.sin(a)) * r)
    for i in range(len(pts) - 1):
        for k in range(sides):
            j = (k + 1) % sides
            faces.append((i * sides + k, i * sides + j, (i + 1) * sides + j, (i + 1) * sides + k))
    faces.append(tuple(reversed(range(sides))))
    last = (len(pts) - 1) * sides
    faces.append(tuple(last + k for k in range(sides)))
    part.add(verts, faces, color)


def curve(a, b, bend, steps):
    """Points from a to b bowed by `bend` (a vector) in the middle."""
    a, b, bend = Vector(a), Vector(b), Vector(bend)
    return [a.lerp(b, i / steps) + bend * math.sin(math.pi * i / steps) for i in range(steps + 1)]


def sphere(subdiv):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    bm.verts.index_update()
    verts = [v.co.copy() for v in bm.verts]
    faces = [[v.index for v in f.verts] for f in bm.faces]
    bm.free()
    return verts, faces


def rock(part, center, radii, color):
    """Chunky angular rock: a lumpy icosahedron with a flat bottom."""
    verts, faces = sphere(1)
    c = Vector(center)
    pts = [c + Vector((d.x * radii[0], d.y * radii[1], max(d.z, -0.3) * radii[2])) * (1 + rng.uniform(-0.12, 0.12))
           for d in verts]
    part.add(pts, faces, color)


# ----------------------------------------------------------------------------------------------- pieces

def leaflet(part, base, direction, length, width, color, droop=0.25):
    """Long pointed tomato leaflet with a finely saw-toothed edge, folded in a V along the midrib (its two
    halves catch the light differently) and bending down towards the tip."""
    t, s, n = frame(direction)
    teeth = 13  # edge points per side: tooth tips alternate with notches
    mids, lefts, rights = [], [], []
    for k in range(teeth):
        u = (k + 0.5) / (teeth + 0.3)
        w = width * math.sin(math.pi * u ** 0.8) ** 0.85
        tooth = k % 2 == 0
        w *= 1.0 if tooth else 0.8
        fwd = (0.045 if tooth else 0.0) * length
        mid = Vector(base) + t * (u * length) + n * (-droop * length * u * u)
        mids.append(mid)
        lift = n * (0.2 * w)  # V fold
        lefts.append(mid + s * w + lift + t * fwd)
        rights.append(mid - s * w + lift + t * fwd)
    tip = Vector(base) + t * (length * 1.06) + n * (-droop * 1.15 * length)
    verts = [Vector(base)] + mids + lefts + rights + [tip]
    m0, l0, r0, tp = 1, 1 + teeth, 1 + 2 * teeth, 1 + 3 * teeth
    faces = [(0, l0, m0), (0, m0, r0)]
    for k in range(teeth - 1):
        faces.append((m0 + k, l0 + k, l0 + k + 1, m0 + k + 1))
        faces.append((m0 + k, m0 + k + 1, r0 + k + 1, r0 + k))
    faces.append((m0 + teeth - 1, l0 + teeth - 1, tp))
    faces.append((m0 + teeth - 1, tp, r0 + teeth - 1))
    part.add(verts, faces, color, two_sided=True)


def leaf_fan(leaves, stems, tip, out, size, rise=0.25):
    """The leaf cluster at a branch end: a terminal leaflet pointing outwards and a little up, flanked by two
    pairs fanning sideways and down, like the reference's leaf tufts."""
    out = Vector((out.x, out.y, 0)).normalized()
    side = Vector((-out.y, out.x, 0))
    color = rng.choice(("leaf", "leaf", "leaf_b", "leaf_c"))
    leaflet(leaves, tip, out + Z * rise, size, size * 0.2, color, droop=0.3)
    for k, (ang, drop, scale) in enumerate(((0.75, 0.05, 0.86), (1.45, -0.35, 0.72))):
        for sgn in (-1, 1):
            d = out * math.cos(ang) + side * (sgn * math.sin(ang)) + Z * (drop + rng.uniform(-0.1, 0.1))
            base = Vector(tip) - out * (0.025 * (k + 1))
            leaflet(leaves, base, d, size * scale * rng.uniform(0.92, 1.05), size * 0.19 * scale + 0.006, color,
                    droop=0.28)


def tomato(part, center, radius, kind):
    """Round, slightly squat faceted fruit (320 faces) with a six-pointed star calyx and a stem nub on top."""
    c = Vector(center)
    verts, faces = sphere(3)
    pts = []
    for d in verts:
        d = d.copy()
        d.z *= 0.9
        d.z -= 0.08 * max(d.z - 0.6, 0.0)  # soft dimple under the calyx
        pts.append(c + d * radius)
    color = {"ripe": rng.choice(("tomato", "tomato", "tomato_b")), "unripe": "unripe", "turning": "turning"}[kind]
    part.add(pts, faces, color)
    top = c + Z * (radius * 0.82)
    a0 = rng.uniform(0, math.pi)
    for k in range(6):  # star calyx: long thin sepals curling out and down over the shoulder
        a = a0 + 2 * math.pi * k / 6
        d = Vector((math.cos(a), math.sin(a), 0))
        side = Vector((-d.y, d.x, 0))
        sv = []
        for f, wd in zip((0.0, 0.25, 0.5, 0.78), (0.1, 0.085, 0.05, 0.0)):
            p = top + d * radius * f + Z * (radius * (0.08 - 0.42 * f * f))
            sv += [p + side * radius * wd, p - side * radius * wd]
        part.add(sv, [(1, 3, 2, 0), (3, 5, 4, 2), (5, 7, 6, 4)], "calyx", two_sided=True)
    nub = [top - Z * 0.005, top + Z * radius * 0.25, top + Z * radius * 0.42 + Vector((0.02, 0, 0)) * radius]
    tube(part, nub, [radius * 0.1, radius * 0.09, radius * 0.08], 6, "calyx")


def flower(part, center, radius):
    c = Vector(center)
    for k in range(5):
        a = 2 * math.pi * k / 5
        d = Vector((math.cos(a), math.sin(a), -0.35)).normalized()
        w = Vector((-d.y, d.x, 0)).normalized() * radius * 0.35
        part.add([c, c + d * radius + w, c + d * radius * 1.3, c + d * radius - w], [(0, 1, 2, 3)], "flower",
                 two_sided=True)
    verts, faces = sphere(1)
    part.add([c + Z * 0.004 + Vector((v.x * radius * 0.28, v.y * radius * 0.28, v.z * radius * 0.4)) for v in verts],
             faces, "flower_center")


# ----------------------------------------------------------------------------------------------- plant

def mound(part):
    """Wide, low faceted dirt mound with big angular rocks of the same brown around and on it."""
    rings = [(0.0, 0.15), (0.18, 0.145), (0.34, 0.12), (0.48, 0.07), (0.6, 0.0), (0.66, -0.05)]
    seg = 16
    verts = []
    for r, z in rings:
        if r == 0:
            verts.append(Vector((0, 0, z)))
            continue
        for k in range(seg):
            a = 2 * math.pi * (k + rng.uniform(-0.2, 0.2)) / seg
            rr = r * rng.uniform(0.92, 1.08)
            verts.append(Vector((rr * math.cos(a), rr * math.sin(a), z + rng.uniform(-0.02, 0.02))))
    faces = [(0, 1 + k, 1 + (k + 1) % seg) for k in range(seg)]
    for i in range(len(rings) - 2):
        b0, b1 = 1 + i * seg, 1 + (i + 1) * seg
        for k in range(seg):
            j = (k + 1) % seg
            faces.append((b0 + k, b1 + k, b1 + j, b0 + j))
    part.add(verts, faces, "dirt")
    for k in range(11):
        a = 2 * math.pi * (k + rng.uniform(-0.3, 0.3)) / 11
        r = rng.uniform(0.5, 0.6)
        size = rng.uniform(0.09, 0.14)
        rock(part, (r * math.cos(a), r * math.sin(a), 0.02), (size * 1.3, size, size * 0.85),
             rng.choice(("rock", "rock_b")))
    for k in range(5):
        a = rng.uniform(0, 2 * math.pi)
        r = rng.uniform(0.2, 0.38)
        size = rng.uniform(0.05, 0.08)
        rock(part, (r * math.cos(a), r * math.sin(a), 0.12), (size * 1.2, size, size * 0.75),
             rng.choice(("rock", "rock_b")))


def build_plant():
    stems, leaves, flowers = Part("Stems"), Part("Leaves"), Part("Flowers")
    fruit = []  # (center, radius, kind)
    # A thick central stem rising straight up with a slight S.
    height = 1.15
    top = Vector((0.0, 0.0, height))

    def on_main(h):
        return Vector((0.025 * math.sin(2 * math.pi * h), 0.02 * math.sin(math.pi * h), 0.08 + (height - 0.08) * h))

    main = [on_main(i / 10) for i in range(11)]
    tube(stems, main, [0.05 - 0.03 * i / 10 for i in range(11)], 8, "stem")

    # Branches alternate around the stem and rise at 40-55 degrees, each ending in a leaf fan.
    branches = []
    hs = [0.28, 0.38, 0.47, 0.56, 0.64, 0.72, 0.8, 0.87]
    for i, h in enumerate(hs):
        a = i * 2.5 + rng.uniform(-0.25, 0.25)
        out = Vector((math.cos(a), math.sin(a), 0))
        elev = math.radians(rng.uniform(38, 52) + 10 * h)
        length = (0.46 - 0.2 * h) * rng.uniform(0.92, 1.08)
        start = on_main(h)
        d = out * math.cos(elev) + Z * math.sin(elev)
        end = start + d * length
        pts = curve(start, end, Z * (length * 0.06) - out * (length * 0.05), 5)
        tube(stems, pts, [0.028 - 0.012 * k / 5 for k in range(6)], 7, "stem")
        leaf_fan(leaves, stems, end, out, 0.26 - 0.06 * h, rise=0.3)
        if i % 2 == 1:  # a small side leaf along the branch
            mid = pts[2]
            sd = out + Vector((-out.y, out.x, 0)) * rng.choice((-1, 1)) * 1.2 - Z * 0.3
            leaflet(leaves, mid, sd, 0.15, 0.035, rng.choice(("leaf", "leaf_b")), droop=0.3)
        branches.append((pts, out, h))
    # Crown: leaflets fanning up and out of the top.
    for k in range(5):
        a = k * 2 * math.pi / 5 + 0.3
        d = Vector((math.cos(a), math.sin(a), 0)) * 0.55 + Z
        leaflet(leaves, top, d, 0.2, 0.045, rng.choice(("leaf", "leaf_b")), droop=0.25)

    # Trusses hang from the lower half of the branches: mostly clusters of 2-4, some single tomatoes.
    plan = [(3, "ripe"), (2, "ripe"), (4, "ripe"), (1, "ripe"), (3, "ripe"), (2, "ripe"), (1, "turning"),
            (2, "unripe")]
    rng.shuffle(plan)
    for (pts, out, h), (count, kind) in zip(branches, plan):
        side = Vector((-out.y, out.x, 0)) * rng.choice((-1, 1))
        start = pts[2]
        knee = start + out * 0.07 + side * 0.03 + Z * 0.03
        hub = knee + out * 0.06 - Z * 0.06
        tube(stems, curve(start, knee, Z * 0.015, 2) + [hub], [0.012, 0.011, 0.01, 0.009], 6, "stem")
        radius = 0.072 if kind == "ripe" else 0.062
        ring = (0.0, 0.055, 0.068, 0.075)[count - 1]
        for k in range(count):
            a = 2 * math.pi * k / count + rng.uniform(-0.25, 0.25)
            offset = out * (math.cos(a) * ring) + side * (math.sin(a) * ring)
            r = radius * rng.uniform(0.9, 1.1)
            c = hub + offset - Z * (r + 0.035 + 0.035 * (k % 2))
            tube(stems, [hub, hub + offset * 0.6 - Z * 0.015, c + Z * r * 1.1], [0.007, 0.0065, 0.006], 6,
                 "stem_dark")
            fruit.append((c, r, kind))
    for k in range(3):  # a few yellow flowers near the top
        a = 0.8 + k * 2.1
        base = on_main(0.93)
        p = base + Vector((math.cos(a), math.sin(a), 0)) * 0.1 + Z * rng.uniform(-0.02, 0.04)
        tube(stems, [base, p + Z * 0.02], [0.006, 0.005], 6, "stem")
        flower(flowers, p, 0.035)
    return stems, leaves, flowers, fruit


# ----------------------------------------------------------------------------------------------- assembly

def material(image):
    mat = bpy.data.materials.get("M_TomatoPlant") or bpy.data.materials.new("M_TomatoPlant")
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = image
    tex.interpolation = "Closest"
    bsdf.inputs["Roughness"].default_value = 0.85
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    out.location, bsdf.location, tex.location = (300, 0), (0, 0), (-350, 0)
    return mat


def to_object(part, coll, mat, parent, origin=Vector()):
    me = bpy.data.meshes.get(part.name)
    if me:
        me.name = part.name + "_old"
    me = bpy.data.meshes.new(part.name)
    origin = origin * SCALE
    me.from_pydata([Vector(v) * SCALE - origin for v in part.verts], [], part.faces)
    uv = me.uv_layers.new(name="UVMap")
    for poly, c in zip(me.polygons, part.cols):
        for li in poly.loop_indices:
            uv.data[li].uv = swatch_uv(c)
    me.materials.append(mat)
    me.validate()
    me.shade_flat()
    me.update()
    obj = bpy.data.objects.new(part.name, me)
    obj.location = origin
    coll.objects.link(obj)
    obj.parent = parent
    return obj


def build():
    global rng
    rng = random.Random(3)
    mat = material(palette_image(HERE))
    coll = bpy.data.collections.get("TomatoPlant")
    if coll:
        for o in list(coll.objects):
            bpy.data.objects.remove(o, do_unlink=True)
    else:
        coll = bpy.data.collections.new("TomatoPlant")
        bpy.context.scene.collection.children.link(coll)
    root = bpy.data.objects.new("TomatoPlant_Root", None)
    coll.objects.link(root)

    ground = Part("Mound")
    mound(ground)
    stems, leaves, flowers, fruit = build_plant()
    for part in (ground, stems, leaves, flowers):
        to_object(part, coll, mat, root)
    ripe_n = unripe_n = 0
    for c, r, kind in sorted(fruit, key=lambda f: (-f[0].z, f[0].x)):
        if kind == "ripe":
            ripe_n += 1
            name = "Tomato_%02d" % ripe_n
        else:
            unripe_n += 1
            name = "Tomato_Unripe_%02d" % unripe_n
        part = Part(name)
        tomato(part, c, r, kind)
        to_object(part, coll, mat, root, origin=c)
    for me in [m for m in bpy.data.meshes if m.users == 0]:
        bpy.data.meshes.remove(me)
    objs = [o for o in coll.objects if o.type == "MESH"]
    return {"ripe": ripe_n, "unripe": unripe_n, "faces": sum(len(o.data.polygons) for o in objs),
            "objects": len(objs)}


def export():
    """FBX of the plant's meshes (axis conversion baked) and the palette into Assets/Art/TomatoPlant."""
    textures = os.path.join(OUT, "Textures")
    os.makedirs(textures, exist_ok=True)
    shutil.copyfile(os.path.join(HERE, "T_TomatoPlant_Palette.png"), os.path.join(textures, "T_TomatoPlant_Palette.png"))
    objs = [o for o in bpy.data.collections["TomatoPlant"].objects if o.type == "MESH"]
    bpy.context.view_layer.update()
    for o in bpy.context.scene.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    path = os.path.join(OUT, "TomatoPlant.fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"}, apply_unit_scale=True,
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                             bake_space_transform=True, mesh_smooth_type="FACE", use_mesh_modifiers=True,
                             bake_anim=False, path_mode="STRIP", embed_textures=False)
    return {"fbx": path, "objects": len(objs)}
