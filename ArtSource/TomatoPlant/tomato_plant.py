"""TomatoPlant: a low-poly tomato plant on a rocky dirt mound, modelled after the reference picture the team chose
(thick faceted light-green stem, crumpled deeply lobed leaflets hanging from the branch ends, glossy red
tomatoes in clusters with long star calyxes, a wide crumpled dirt mound ringed with big angular rocks).

It is the same kind of edible plant as StrawberryBush and BlackberryBush (see
Assets/Editor/Eating/BerryPlantImporter.cs): Mound, Stems, Leaves, Flowers and one object per fruit; ripe fruit are
Tomato_NN (green ones would be Tomato_Unripe_NN, decoration). Each fruit's origin is its centre so it drops alone.

Look: flat-shaded facets in flat colours from a small palette (T_TomatoPlant_Palette). Faces pick their shade from
where they sit: tomatoes are banded darker underneath and lighter on the shoulders, leaflets are darker by the
midrib and lighter towards the edge with a little random variation, rock tops are lighter than their sides.
Layout (the reference seen from the front, the camera at -Y): branch, cluster and leaf positions are measured
from the picture. Units are metres at 1/SCALE size (the plant is ~1.3 tall there), Z up, origin at the stem foot.
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
SCALE = 0.7  # exported at the berry bushes' size (~1.2 m wide, ~0.95 m tall)

PALETTE = [  # sRGB, row by row in an 8 x 8 grid of 16 px cells
    ("leaf_light", (0.56, 0.80, 0.32)), ("leaf", (0.47, 0.73, 0.27)), ("leaf_mid", (0.42, 0.67, 0.24)),
    ("leaf_dark", (0.35, 0.58, 0.20)),
    ("stem_light", (0.52, 0.76, 0.33)), ("stem", (0.45, 0.70, 0.28)), ("stem_dark", (0.36, 0.60, 0.22)),
    ("calyx", (0.30, 0.56, 0.18)),
    ("tomato_light", (0.96, 0.40, 0.23)), ("tomato", (0.90, 0.25, 0.14)), ("tomato_mid", (0.84, 0.19, 0.11)),
    ("tomato_dark", (0.74, 0.13, 0.09)), ("unripe", (0.55, 0.74, 0.28)),
    ("dirt", (0.45, 0.31, 0.23)), ("dirt_dark", (0.39, 0.27, 0.20)), ("dirt_light", (0.50, 0.35, 0.26)),
    ("rock_light", (0.66, 0.50, 0.38)), ("rock", (0.58, 0.42, 0.32)), ("rock_dark", (0.49, 0.35, 0.27)),
    ("flower", (1.0, 0.85, 0.25)), ("flower_center", (0.95, 0.60, 0.14)),
]
GRID, ROWS, CELL = 8, 4, 32
SIZE = GRID * CELL  # 256 px: flat swatches on top, smooth colour gradients (GRADIENTS) below
# Gradient bands (pixel rows from the top). Leaves: U midrib -> edge, V base -> tip. Tomatoes: V bottom -> top.
GRADIENTS = {"leaf": (128, 192), "tomato": (192, 224)}
INDEX = {name: i for i, (name, _) in enumerate(PALETTE)}

rng = random.Random(7)
Z = Vector((0, 0, 1))
MOUND_TOP = 0.15


def swatch_uv(name):
    i = INDEX[name]
    return ((i % GRID + .5) / GRID, 1 - (i // GRID + .5) * CELL / SIZE)


def grad_uv(band, u, v):
    y0, y1 = GRADIENTS[band]
    u, v = min(max(u, 0.0), 1.0), min(max(v, 0.0), 1.0)
    return ((4 + u * (SIZE - 8)) / SIZE, 1 - (y0 + 3 + (1 - v) * (y1 - y0 - 6)) / SIZE)


def _ramp(stops, t):
    for (t0, c0), (t1, c1) in zip(stops, stops[1:]):
        if t <= t1:
            k = (t - t0) / max(t1 - t0, 1e-6)
            return tuple(x + (y - x) * k for x, y in zip(c0, c1))
    return stops[-1][1]


def palette_image(folder):
    import numpy as np
    name = "T_TomatoPlant_Palette"
    image = bpy.data.images.get(name)
    if image is not None and (tuple(image.size) != (SIZE, SIZE) or len(image.pixels) != SIZE * SIZE * 4):
        # Set the old one aside rather than removing it: removing an image the viewport draws crashed the
        # GPU driver. Unused, it is dropped when the file is saved and reopened.
        image.name = name + "_old"
        image = None
    if image is None:
        image = bpy.data.images.new(name, SIZE, SIZE, alpha=False)
    img = np.zeros((SIZE, SIZE, 3))  # row 0 is the top of the image
    for i in range(GRID * ROWS):
        color = PALETTE[i][1] if i < len(PALETTE) else (1.0, 0.0, 1.0)
        cx, cy = i % GRID, i // GRID
        img[cy * CELL:(cy + 1) * CELL, cx * CELL:(cx + 1) * CELL] = color
    # Leaves: deep green along the midrib, fresh light green at the edge, a touch yellower towards the tip.
    y0, y1 = GRADIENTS["leaf"]
    for row in range(y1 - y0):
        v = 1 - row / (y1 - y0 - 1)  # band top = leaf tip
        for col in range(SIZE):
            u = col / (SIZE - 1)
            e = u * u * (3 - 2 * u)
            base = _ramp(((0.0, (0.30, 0.54, 0.18)), (0.5, (0.42, 0.68, 0.24)), (1.0, (0.56, 0.80, 0.32))), e)
            tip = 0.12 * v * v
            img[y0 + row, col] = (base[0] + tip * 0.6, base[1] + tip * 0.15, base[2])
    # Tomatoes: deep red underneath, bright red, warm orange-red on the shoulders.
    y0, y1 = GRADIENTS["tomato"]
    for row in range(y1 - y0):
        v = 1 - row / (y1 - y0 - 1)
        img[y0 + row, :] = _ramp(((0.0, (0.66, 0.10, 0.07)), (0.4, (0.84, 0.19, 0.11)),
                                  (0.75, (0.93, 0.30, 0.16)), (1.0, (0.98, 0.46, 0.26))), v)
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

    def add(self, verts, faces, color, two_sided=False, vert_uv=None):
        """color: one palette name for every face, a list with one name per face, or a function
        (face centre, face normal) -> name; vert_uv: per-vertex UVs into a gradient band instead.
        two_sided adds a back side on its own vertices."""
        verts = [Vector(v) for v in verts]
        if vert_uv is not None:
            face_uvs = [[vert_uv[i] for i in f] for f in faces]
        else:
            if isinstance(color, str):
                cols = [color] * len(faces)
            elif callable(color):
                cols = []
                for f in faces:
                    pts = [verts[i] for i in f]
                    c = sum(pts, Vector()) / len(pts)
                    nrm = (pts[1] - pts[0]).cross(pts[2] - pts[0])
                    cols.append(color(c, nrm.normalized() if nrm.length > 1e-12 else Z))
            else:
                cols = list(color)
            face_uvs = [[swatch_uv(c)] * len(f) for f, c in zip(faces, cols)]
        base = len(self.verts)
        self.verts += [tuple(v) for v in verts]
        self.faces += [[base + i for i in f] for f in faces]
        self.uvs += face_uvs
        if two_sided:
            back = len(self.verts)
            self.verts += [tuple(v) for v in verts]
            self.faces += [[back + i for i in reversed(f)] for f in faces]
            self.uvs += [list(reversed(uv)) for uv in face_uvs]


def frame(direction, up=Z):
    """Orthonormal (t, s, n): t along direction, n as close to `up` as possible."""
    t = Vector(direction).normalized()
    s = up.cross(t)
    if s.length < 1e-6:
        s = Vector((1, 0, 0)).cross(t)
    s.normalize()
    return t, s, t.cross(s)


def by_light(light, mid, dark):
    """Face colour from its normal: faces turned up/towards the key light lighter, down-turned faces darker."""
    key = Vector((-0.4, -0.5, 0.75)).normalized()

    def pick(_c, n):
        d = n.dot(key) + rng.uniform(-0.12, 0.12)
        return light if d > 0.45 else (mid if d > -0.05 else dark)
    return pick


def tube(part, points, radii, sides, colors=("stem_light", "stem", "stem_dark")):
    """Faceted tube along a polyline, closed at both ends; facets shaded by their facing."""
    verts, faces = [], []
    pts = [Vector(p) for p in points]
    roll = rng.uniform(0, math.pi)
    for i, (p, r) in enumerate(zip(pts, radii)):
        d = pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]
        t, s, n = frame(d, Z if abs(d.normalized().z) < 0.95 else Vector((1, 0, 0)))
        for k in range(sides):
            a = roll + 2 * math.pi * k / sides
            verts.append(p + (s * math.cos(a) + n * math.sin(a)) * r)
    for i in range(len(pts) - 1):
        for k in range(sides):
            j = (k + 1) % sides
            faces.append((i * sides + k, i * sides + j, (i + 1) * sides + j, (i + 1) * sides + k))
    faces.append(tuple(reversed(range(sides))))
    last = (len(pts) - 1) * sides
    faces.append(tuple(last + k for k in range(sides)))
    part.add(verts, faces, by_light(*colors))


def curve(a, b, bend, steps):
    """Points from a to b bowed by `bend` (a vector) in the middle."""
    a, b, bend = Vector(a), Vector(b), Vector(bend)
    return [a.lerp(b, i / steps) + bend * math.sin(math.pi * i / steps) for i in range(steps + 1)]


def icosphere(subdiv):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    bm.verts.index_update()
    verts = [v.co.copy() for v in bm.verts]
    faces = [[v.index for v in f.verts] for f in bm.faces]
    bm.free()
    return verts, faces


def uv_sphere(segments, rings):
    verts = [Vector((0, 0, -1))]
    for r in range(1, rings):
        phi = math.pi * r / rings - math.pi / 2
        for k in range(segments):
            a = 2 * math.pi * k / segments
            verts.append(Vector((math.cos(phi) * math.cos(a), math.cos(phi) * math.sin(a), math.sin(phi))))
    verts.append(Vector((0, 0, 1)))
    top = len(verts) - 1
    faces = [(0, 1 + (k + 1) % segments, 1 + k) for k in range(segments)]
    for r in range(rings - 2):
        b0, b1 = 1 + r * segments, 1 + (r + 1) * segments
        for k in range(segments):
            j = (k + 1) % segments
            faces.append((b0 + k, b0 + j, b1 + j, b1 + k))
    b = 1 + (rings - 2) * segments
    faces += [(b + k, b + (k + 1) % segments, top) for k in range(segments)]
    return verts, faces


def rock(part, center, radii):
    """Big angular rock: a jittered icosahedron sitting half in the ground; tops lighter than the sides."""
    verts, faces = icosphere(1)
    c = Vector(center)
    yaw = rng.uniform(0, math.pi)
    pts = []
    for d in verts:
        j = 1 + rng.uniform(-0.16, 0.16)
        x, y = d.x * math.cos(yaw) - d.y * math.sin(yaw), d.x * math.sin(yaw) + d.y * math.cos(yaw)
        pts.append(c + Vector((x * radii[0], y * radii[1], max(d.z, -0.45) * radii[2])) * j)
    part.add(pts, faces, lambda _c, n: "rock_light" if n.z > 0.55 else rng.choice(("rock", "rock", "rock_dark")))


# ----------------------------------------------------------------------------------------------- pieces

def leaflet(part, base, direction, length, width):
    """Lobed tomato leaflet with a pointed tip, folded in a V along the midrib: a midrib and an edge row of
    points, the edge alternating big lobes and notches, a slight random lift per point. Faces take light,
    mid or dark green from how they face the light, so the two halves of the V read in two tones with a
    few big facets, like the reference. `width` is the half width."""
    t, s, n = frame(direction)
    steps = 7
    mids, lefts, rights = [], [], []
    for i in range(steps):
        u = (i + 0.6) / (steps + 0.3)
        w = width * math.sin(math.pi * u ** 0.85) ** 0.55
        lobe = i % 2 == 1
        w *= rng.uniform(0.95, 1.06) if lobe else rng.uniform(0.7, 0.78)
        fwd = (0.09 if lobe else 0.0) * length
        mid = Vector(base) + t * (u * length) + n * (-0.16 * length * u * u)
        mids.append(mid + n * (rng.uniform(-0.01, 0.01) * length))
        for sgn, row in ((1, lefts), (-1, rights)):
            row.append(mid + s * (sgn * w) + n * (0.3 * w + rng.uniform(-0.03, 0.03) * length) + t * fwd)
    tip = Vector(base) + t * (length * 1.12) + n * (-0.2 * length)
    verts = [Vector(base)] + mids + lefts + rights + [tip]
    m0, l0, r0, tp = 1, 1 + steps, 1 + 2 * steps, 1 + 3 * steps
    faces = [(0, l0, m0), (0, m0, r0)]
    for k in range(steps - 1):
        faces.append((m0 + k, l0 + k, l0 + k + 1, m0 + k + 1))
        faces.append((m0 + k, m0 + k + 1, r0 + k + 1, r0 + k))
    faces.append((m0 + steps - 1, l0 + steps - 1, tp))
    faces.append((m0 + steps - 1, tp, r0 + steps - 1))
    reach = rng.uniform(0.78, 1.0)  # how far towards the light edge colour this leaflet goes
    vs = [(i + 0.6) / (steps + 0.3) for i in range(steps)]
    uv = ([grad_uv("leaf", 0.0, 0.0)] + [grad_uv("leaf", 0.0, v) for v in vs]
          + [grad_uv("leaf", reach, v) for v in vs] * 2 + [grad_uv("leaf", 0.4, 1.0)])
    part.add(verts, faces, None, two_sided=True, vert_uv=uv)


def compound_leaf(leaves, stems, start, out, size, hang=0.65):
    """Tomato leaf: a rachis arching outwards, a big terminal leaflet and two pairs of broad leaflets, all
    hanging out and down by `hang` (0 flat .. 1 steep), like the leaf tufts at the reference's branch ends."""
    out = Vector((out.x, out.y, 0)).normalized()
    side = Vector((-out.y, out.x, 0))
    start = Vector(start)
    end = start + out * (size * 0.75) - Z * (size * 0.25 * hang)
    pts = curve(start, end, Z * (size * 0.1), 4)
    tube(stems, pts, [0.013 - 0.005 * k / 4 for k in range(5)], 5)
    term = size * 0.62
    leaflet(leaves, pts[-1], out - Z * (0.15 + 0.6 * hang), term, term * 0.42)
    for f, scale in ((0.35, 0.52), (0.72, 0.6)):
        p = pts[0].lerp(pts[-1], f) + Z * (size * 0.1 * math.sin(math.pi * f))
        for sgn in (-1, 1):
            d = out * 0.4 + side * (sgn * 1.1) - Z * (0.12 + 0.45 * hang + rng.uniform(-0.08, 0.08))
            ln = size * scale * rng.uniform(0.92, 1.06)
            leaflet(leaves, p, d, ln, ln * 0.42)


def tomato(part, center, radius, kind):
    """Glossy faceted tomato: a slightly squat UV sphere (16 x 10) banded darker underneath and lighter on the
    shoulders, with a six-pointed star calyx of long thin sepals on top."""
    c = Vector(center)
    verts, faces = uv_sphere(16, 10)
    pts = []
    for d in verts:
        d = d.copy()
        d.z *= 0.88
        d.z -= 0.06 * max(d.z - 0.7, 0.0)
        pts.append(c + d * radius)
    if kind == "ripe":
        lo, hi = c.z - radius * 0.88, c.z + radius * 0.84
        part.add(pts, faces, None, vert_uv=[grad_uv("tomato", 0.5, (v.z - lo) / (hi - lo)) for v in pts])
    else:
        part.add(pts, faces, "unripe")
    top = c + Z * (radius * 0.86)
    a0 = rng.uniform(0, math.pi)
    for k in range(6):
        a = a0 + 2 * math.pi * k / 6 + rng.uniform(-0.15, 0.15)
        d = Vector((math.cos(a), math.sin(a), 0))
        side = Vector((-d.y, d.x, 0))
        reach = rng.uniform(0.75, 0.95)
        sv = []
        for f, wd in zip((0.0, 0.3, 0.6, 1.0), (0.09, 0.07, 0.04, 0.0)):
            p = top + d * radius * reach * f + Z * (radius * (0.1 - 0.4 * f * f))
            sv += [p + side * radius * wd, p - side * radius * wd]
        part.add(sv, [(1, 3, 2, 0), (3, 5, 4, 2), (5, 7, 6, 4)], "calyx", two_sided=True)


def flower(part, center, radius):
    c = Vector(center)
    for k in range(5):
        a = 2 * math.pi * k / 5
        d = Vector((math.cos(a), math.sin(a), -0.35)).normalized()
        w = Vector((-d.y, d.x, 0)).normalized() * radius * 0.35
        part.add([c, c + d * radius + w, c + d * radius * 1.3, c + d * radius - w], [(0, 1, 2, 3)], "flower",
                 two_sided=True)
    verts, faces = icosphere(1)
    part.add([c + Z * 0.004 + Vector((v.x * radius * 0.28, v.y * radius * 0.28, v.z * radius * 0.4)) for v in verts],
             faces, "flower_center")


# ----------------------------------------------------------------------------------------------- plant

def mound(part):
    """Wide, low mound: a crumpled dirt top with a short steep edge, big angular rocks half buried in the rim
    and a few smaller ones on top."""
    rings = [(0.0, MOUND_TOP), (0.12, MOUND_TOP), (0.24, MOUND_TOP - 0.005), (0.36, MOUND_TOP - 0.015),
             (0.48, MOUND_TOP - 0.035), (0.56, MOUND_TOP - 0.06), (0.61, 0.02), (0.63, -0.04)]
    seg = 20
    verts = []
    for r, z in rings:
        if r == 0:
            verts.append(Vector((0, 0, z)))
            continue
        for k in range(seg):
            a = 2 * math.pi * (k + rng.uniform(-0.25, 0.25)) / seg
            rr = r * rng.uniform(0.94, 1.06)
            verts.append(Vector((rr * math.cos(a), rr * math.sin(a), z + rng.uniform(-0.018, 0.018))))
    faces = [(0, 1 + k, 1 + (k + 1) % seg) for k in range(seg)]
    for i in range(len(rings) - 2):
        b0, b1 = 1 + i * seg, 1 + (i + 1) * seg
        for k in range(seg):
            j = (k + 1) % seg
            faces.append((b0 + k, b1 + k, b1 + j, b0 + j))
    part.add(verts, faces, lambda _c, n: rng.choice(("dirt", "dirt", "dirt_dark", "dirt_light"))
             if n.z > 0.6 else "dirt_dark")
    for k in range(12):
        a = 2 * math.pi * (k + rng.uniform(-0.3, 0.3)) / 12
        r = rng.uniform(0.5, 0.6)
        size = rng.uniform(0.09, 0.13)
        rock(part, (r * math.cos(a), r * math.sin(a), 0.06), (size * 1.35, size, size * 0.8))
    for k in range(6):
        a = rng.uniform(0, 2 * math.pi)
        r = rng.uniform(0.22, 0.42)
        size = rng.uniform(0.04, 0.065)
        rock(part, (r * math.cos(a), r * math.sin(a), MOUND_TOP), (size * 1.2, size, size * 0.8))


# Branches, measured on the reference (front view, the camera at -Y): angle around Z in degrees (0 = right,
# -90 = towards the viewer), where it leaves the stem (z), where it ends (distance from the stem, z), the size
# of its leaf, how steeply the leaf hangs, and the cluster hanging from it (count, 0 = none).
BRANCHES = [
    (175, 0.95, 0.42, 1.12, 0.46, 0.7, 2),    # upper left
    (8, 0.86, 0.48, 1.02, 0.46, 0.75, 3),     # upper right
    (195, 0.62, 0.5, 0.82, 0.48, 0.8, 4),     # middle left
    (-12, 0.5, 0.52, 0.66, 0.48, 0.85, 2),    # lower right
    (160, 0.36, 0.5, 0.48, 0.46, 0.9, 3),     # lower left
    (-65, 0.64, 0.22, 0.74, 0.36, 0.85, 0),   # front, middle
    (-115, 0.32, 0.26, 0.4, 0.44, 0.95, 0),   # front, low, hanging to the ground
    (95, 0.72, 0.36, 0.86, 0.42, 0.75, 1),    # back
    (50, 0.42, 0.42, 0.52, 0.42, 0.85, 0),    # back right
    (235, 1.02, 0.3, 1.14, 0.36, 0.6, 0),     # back left, high
    (-30, 1.08, 0.28, 1.2, 0.32, 0.45, 0),    # front right, high
]
STEM_TOP = 1.32


def separate(cluster, placed, gap=0.02, rounds=60):
    """Pushes a cluster's tomatoes apart (and away from ones already placed) until no two overlap."""
    for _ in range(rounds):
        moved = False
        for i, (ci, ri) in enumerate(cluster):
            others = [(cj, rj) for j, (cj, rj) in enumerate(cluster) if j != i] + [(f[0], f[1]) for f in placed]
            for cj, rj in others:
                d = ci - cj
                need = ri + rj + gap
                if d.length < need:
                    # Push sideways only, so cluster mates hang next to each other, never stacked.
                    flat = Vector((d.x, d.y, 0))
                    flat = flat.normalized() if flat.length > 1e-6 else Vector((1, 0, 0))
                    ci += flat * ((need - d.length) * 0.6)
                    moved = True
            cluster[i][0] = ci
        if not moved:
            break


def build_plant():
    stems, leaves, flowers = Part("Stems"), Part("Leaves"), Part("Flowers")
    fruit = []  # (center, radius, kind)

    def on_main(z):
        h = (z - MOUND_TOP) / (STEM_TOP - MOUND_TOP)
        return Vector((0.02 * math.sin(2 * math.pi * h), 0.015 * math.sin(math.pi * h), z))

    main = [on_main(MOUND_TOP - 0.05 + (STEM_TOP - MOUND_TOP + 0.05) * i / 10) for i in range(11)]
    tube(stems, main, [0.058 - 0.036 * i / 10 for i in range(11)], 6)
    # Young shoots from the foot of the stem.
    for a, ln in ((150, 0.3), (-40, 0.26)):
        out = Vector((math.cos(math.radians(a)), math.sin(math.radians(a)), 0))
        foot = on_main(MOUND_TOP + 0.02)
        tip = foot + out * ln * 0.55 + Z * ln * 0.75
        tube(stems, curve(foot, tip, out * 0.03, 3), [0.024, 0.02, 0.016, 0.013], 6)
        compound_leaf(leaves, stems, tip, out, 0.26, hang=0.9)

    for a, z0, reach, z1, size, hang, count in BRANCHES:
        # Short leafy branches with big leaves, like the reference (no long bare sticks).
        reach, size = reach * 0.75, size * 1.3
        out = Vector((math.cos(math.radians(a)), math.sin(math.radians(a)), 0))
        start = on_main(z0)
        end = Vector((out.x * reach, out.y * reach, z1))
        pts = curve(start, end, Z * 0.04 - out * 0.03, 4)
        tube(stems, pts, [0.032 - 0.014 * k / 4 for k in range(5)], 6)
        compound_leaf(leaves, stems, end, out, size, hang=hang)
        if count:
            side = Vector((-out.y, out.x, 0)) * (-1 if out.x < 0 else 1)
            at = pts[3]
            knee = at + out * 0.06 + Vector((0, -0.04, 0)) + Z * 0.03
            hub = knee + out * 0.06 + Vector((0, -0.04, 0)) - Z * 0.07
            tube(stems, curve(at, knee, Z * 0.01, 2) + [hub], [0.014, 0.013, 0.012, 0.011], 6)
            radius = 0.09 if z0 > 0.4 else 0.074
            ring = radius * (0.0, 1.25, 1.45, 1.6)[count - 1]
            cluster = []
            for k in range(count):
                ang = math.pi * (0.15 + 0.7 * k / max(count - 1, 1)) + rng.uniform(-0.15, 0.15)
                # Spread the cluster across the view (left-right and front-back) so each tomato shows.
                offset = side * (math.cos(ang) * ring) + Vector((0, -1, 0)) * (math.sin(ang) * ring * 0.8 + 0.03)
                r = radius * rng.uniform(0.9, 1.08)
                c = hub + offset - Z * (r + 0.05 + 0.025 * (k % 2))
                cluster.append([c, r])
            separate(cluster, fruit)
            for c, r in cluster:
                tube(stems, curve(hub, c + Z * r * 1.05, out * 0.015 + Z * 0.01, 2), [0.009, 0.008, 0.0075], 6)
                fruit.append((c, r, "ripe"))
    # Crown: two young leaves rising out of the top and a tuft of upright leaflets.
    top = on_main(STEM_TOP)
    for a in (150, 20):
        out = Vector((math.cos(math.radians(a)), math.sin(math.radians(a)), 0))
        tip = top + out * 0.08 + Z * 0.06
        tube(stems, [top, tip], [0.02, 0.014], 6)
        compound_leaf(leaves, stems, tip, out, 0.3, hang=0.15)
    for k in range(3):
        a = k * 2.1 + 1.0
        leaflet(leaves, top, Vector((math.cos(a), math.sin(a), 0)) * 0.4 + Z, 0.15, 0.06)
    for k in range(3):  # a few yellow flowers near the top
        a = 0.8 + k * 2.1
        base = on_main(STEM_TOP - 0.1)
        p = base + Vector((math.cos(a), math.sin(a), 0)) * 0.1 + Z * rng.uniform(-0.02, 0.04)
        tube(stems, [base, p + Z * 0.02], [0.006, 0.005], 5)
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
    tex.interpolation = "Linear"
    bsdf.inputs["Roughness"].default_value = 0.6
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
    for poly, loop_uvs in zip(me.polygons, part.uvs):
        for li, u in zip(poly.loop_indices, loop_uvs):
            uv.data[li].uv = u
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
    rng = random.Random(7)
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
