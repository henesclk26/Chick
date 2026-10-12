"""PepperPlant: a low-poly chili pepper bush on a rocky dirt mound, modelled after the reference picture the team
chose: a thick short stem that splits into several branches arching up and out (no two alike), forking again,
pointed leaves in clusters at the branch ends and singly along the branches, long curved peppers (green, yellow and
one red) hanging on hooked stalks with a green star-shaped cap, a wide dirt mound ringed with big angular rocks.

Built the way the strawberry bush is (ArtSource/StrawberryBush.blend, the team's quality bar), at its size:
- leaves: each a 17-vertex, 20-triangle blade (a creased midrib zipped to the rim), with a separate underside layer
  a few millimetres below in a paler green, so a leaf reads as a thin sheet with a dark edge;
- stems: parallel-transported tubes, darker at the foot, lighter towards the tips;
- mound: a faceted dirt dome with three rings of chunky rocks (big ones on the rim, smaller ones up the slope);
- colour: a per-corner "Paint" attribute (soft gradients, a little per-vertex variation, facets shaded by how they
  face the light), times object-space noise (0.88..1.1) and a Hue/Saturation/Value node, baked into one atlas
  (T_PepperPlant_Albedo, 2048) on a smart-projected UV map. Unity gets one material.
Structure follows the other edible plants (Assets/Editor/Eating/BerryPlantImporter.cs): Mound, Stems, Leaves and
one object per pepper, Pepper_NN, each with its origin at its centre (not hooked into the game yet).
Units are metres (exported at EXPORT_SCALE), Z up, origin at the stem foot.
Run inside Blender: exec(code, {"__file__": path}); build(); bake(); export().
"""
import math
import os
import random
import shutil

import bmesh
import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.abspath(os.path.join(HERE, "..", "..", "Assets", "Art", "PepperPlant"))
SCALE = 1.0  # built at the strawberry bush's size in its .blend (~2.3 m wide)
EXPORT_SCALE = 0.52  # exported at the strawberry bush's in-game size (~1.3 m wide), like its FBX

COLORS = {  # sRGB
    "stem_foot": (0.27, 0.42, 0.16), "stem_tip": (0.47, 0.68, 0.27),
    "leaf_base": (0.22, 0.42, 0.13), "leaf_mid": (0.35, 0.59, 0.19), "leaf_tip": (0.49, 0.73, 0.26),
    "leaf_back": (0.60, 0.72, 0.42),
    "calyx": (0.30, 0.52, 0.17), "calyx_tip": (0.42, 0.64, 0.23),
    "yellow_light": (1.0, 0.93, 0.40), "yellow": (0.96, 0.82, 0.14), "yellow_dark": (0.84, 0.62, 0.06),
    "green_light": (0.50, 0.75, 0.25), "green": (0.33, 0.58, 0.15), "green_dark": (0.22, 0.43, 0.10),
    "red_light": (0.98, 0.34, 0.22), "red": (0.88, 0.12, 0.07), "red_dark": (0.64, 0.06, 0.04),
    "dirt_light": (0.50, 0.34, 0.25), "dirt": (0.42, 0.28, 0.20), "dirt_dark": (0.30, 0.20, 0.14),
    "rock_light": (0.67, 0.48, 0.37), "rock": (0.56, 0.39, 0.29), "rock_dark": (0.40, 0.27, 0.20),
}
ATLAS = "T_PepperPlant_Albedo"

rng = random.Random(11)
Z = Vector((0, 0, 1))
KEY = Vector((-0.4, -0.5, 0.75)).normalized()
MOUND_TOP = 0.30
LEAF_THICKNESS = 0.009


def lin(c):
    """sRGB -> linear (the Paint attribute is a linear float colour, like the strawberry plants')."""
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)


def mix(a, b, t):
    t = min(max(t, 0.0), 1.0)
    return tuple(x + (y - x) * t for x, y in zip(a, b))


def ramp(stops, t):
    t = min(max(t, 0.0), 1.0)
    for (t0, c0), (t1, c1) in zip(stops, stops[1:]):
        if t <= t1:
            return mix(c0, c1, (t - t0) / max(t1 - t0, 1e-6))
    return stops[-1][1]


def jit(c, amount):
    """A little value variation, so a gradient never looks machine-flat."""
    k = 1 + rng.uniform(-amount, amount)
    return tuple(min(x * k, 1.0) for x in c)


class Part:
    def __init__(self, name):
        self.name = name
        self.verts, self.faces, self.cols = [], [], []  # cols: one sRGB colour per face corner

    def add(self, verts, faces, vert_colors, shade=0.14):
        """vert_colors: one sRGB colour per vertex; each facet is shaded a little by how it faces the light."""
        verts = [Vector(v) for v in verts]
        base = len(self.verts)
        for f in faces:
            pts = [verts[i] for i in f]
            nrm = (pts[1] - pts[0]).cross(pts[2] - pts[0])
            nrm = nrm.normalized() if nrm.length > 1e-12 else Z
            k = 1 - shade * 0.5 + shade * max(min(nrm.dot(KEY), 1.0), -1.0)
            self.cols.append([tuple(min(x * k, 1.0) for x in vert_colors[i]) for i in f])
            self.faces.append([base + i for i in f])
        self.verts += verts


def frame(direction, up=Z):
    """(along, side, normal): `normal` leans towards `up`."""
    t = Vector(direction).normalized()
    s = up.cross(t)
    if s.length < 1e-4:
        s = Vector((1, 0, 0)).cross(t)
    s.normalize()
    return t, s, t.cross(s)


def bezier(p0, p1, p2, p3, steps):
    p0, p1, p2, p3 = (Vector(p) for p in (p0, p1, p2, p3))
    pts = []
    for i in range(steps + 1):
        u = i / steps
        a = 1 - u
        pts.append(p0 * a ** 3 + p1 * 3 * a * a * u + p2 * 3 * a * u * u + p3 * u ** 3)
    return pts


def tube(part, points, radii, sides, foot, tip, cap_end=True, shade=0.2):
    """A tube along `points` (parallel-transported rings, so it never twists), coloured `foot` -> `tip`."""
    pts = [Vector(p) for p in points]
    n = len(pts)
    tans = [(pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized() for i in range(n)]
    ref = Z if abs(tans[0].z) < 0.9 else Vector((1, 0, 0))
    s = ref.cross(tans[0]).normalized()
    roll = rng.uniform(0, 2 * math.pi)
    verts, cols = [], []
    for i, (p, r, t) in enumerate(zip(pts, radii, tans)):
        if i:
            s = tans[i - 1].rotation_difference(t) @ s
            s = (s - t * s.dot(t)).normalized()
        b = t.cross(s)
        c = mix(foot, tip, i / (n - 1))
        for k in range(sides):
            a = roll + 2 * math.pi * k / sides
            verts.append(p + (s * math.cos(a) + b * math.sin(a)) * r * rng.uniform(0.94, 1.06))
            cols.append(jit(c, 0.05))
    faces = []
    for i in range(n - 1):
        for k in range(sides):
            j = (k + 1) % sides
            faces.append((i * sides + k, i * sides + j, (i + 1) * sides + j, (i + 1) * sides + k))
    faces.append(tuple(reversed(range(sides))))
    if cap_end:
        last = (n - 1) * sides
        verts.append(pts[-1] + tans[-1] * radii[-1] * 0.7)
        cols.append(jit(tip, 0.05))
        faces += [(last + k, last + (k + 1) % sides, len(verts) - 1) for k in range(sides)]
    part.add(verts, faces, cols, shade)


def sample(path, t):
    """Point and tangent at `t` (0..1) along a polyline."""
    f = min(max(t, 0.0), 1.0) * (len(path) - 1)
    i = min(int(f), len(path) - 2)
    return path[i].lerp(path[i + 1], f - i), (path[i + 1] - path[i]).normalized()


# ----------------------------------------------------------------------------------------------- mound

def icosphere(subdiv):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    bm.verts.index_update()
    verts = [v.co.copy() for v in bm.verts]
    faces = [[v.index for v in f.verts] for f in bm.faces]
    bm.free()
    return verts, faces


def rock(part, center, radii, sink=0.4):
    """A chunky angular rock: a jittered, flattened icosphere, lighter on top, darker underneath."""
    verts, faces = icosphere(1)
    c = Vector(center)
    rot = Matrix.Rotation(rng.uniform(0, math.pi), 3, "Z") @ Matrix.Rotation(rng.uniform(-0.25, 0.25), 3, "X")
    tint = jit((1, 1, 1), 0.08)
    pts, cols = [], []
    for d in verts:
        d = d * (1 + rng.uniform(-0.2, 0.2))
        d = Vector((d.x * radii[0], d.y * radii[1], max(d.z, -sink) * radii[2]))
        pts.append(c + rot @ d)
        h = d.z / radii[2]
        col = ramp(((-0.5, COLORS["rock_dark"]), (0.1, COLORS["rock"]), (0.8, COLORS["rock_light"])), (h + 0.5) / 1.3)
        cols.append(jit(tuple(x * y for x, y in zip(col, tint)), 0.04))
    part.add(pts, faces, cols, shade=0.32)


def dome_height(r):
    """Height of the dirt dome at radius r."""
    t = min(r / 0.9, 1.0)
    return MOUND_TOP * (1 - t ** 2.4) * (1 - 0.15 * t)


def mound(part):
    rings = [0.0, 0.14, 0.28, 0.42, 0.55, 0.67, 0.77, 0.86, 0.93]
    seg = 28
    verts, cols = [], []
    for ri, r in enumerate(rings):
        if r == 0:
            verts.append(Vector((0, 0, MOUND_TOP)))
            cols.append(COLORS["dirt"])
            continue
        off = 0.5 * (ri % 2)
        for k in range(seg):
            a = 2 * math.pi * (k + off + rng.uniform(-0.25, 0.25)) / seg
            rr = r * rng.uniform(0.95, 1.05)
            z = dome_height(rr) + rng.uniform(-0.018, 0.018) if ri < len(rings) - 1 else -0.03
            verts.append(Vector((rr * math.cos(a), rr * math.sin(a), z)))
            cols.append(jit(ramp(((0, COLORS["dirt_light"]), (0.5, COLORS["dirt"]), (1, COLORS["dirt_dark"])),
                                 r / 0.93), 0.07))
    faces = [(0, 1 + k, 1 + (k + 1) % seg) for k in range(seg)]
    for i in range(len(rings) - 2):
        b0, b1 = 1 + i * seg, 1 + (i + 1) * seg
        for k in range(seg):
            j = (k + 1) % seg
            if (i + 1) % 2:  # the next ring is turned half a segment: zig-zag triangles
                faces += [(b0 + k, b1 + k, b0 + j), (b0 + j, b1 + k, b1 + j)]
            else:
                faces += [(b0 + k, b1 + k, b1 + j), (b0 + k, b1 + j, b0 + j)]
    part.add(verts, faces, cols, shade=0.22)
    # Three rings of rocks like the strawberry bush: big ones on the rim, smaller ones up the slope.
    for count, (r0, r1), (s0, s1), flat in ((13, (0.74, 0.86), (0.17, 0.25), 0.55),
                                            (10, (0.5, 0.62), (0.1, 0.15), 0.55),
                                            (8, (0.2, 0.36), (0.05, 0.08), 0.6)):
        for k in range(count):
            a = 2 * math.pi * (k + rng.uniform(-0.3, 0.3)) / count + r0
            r = rng.uniform(r0, r1)
            size = rng.uniform(s0, s1)
            z = max(dome_height(r), 0.0) + (size * 0.05 if r0 < 0.7 else size * 0.15)
            rock(part, (r * math.cos(a), r * math.sin(a), z),
                 (size * rng.uniform(1.0, 1.35), size * rng.uniform(0.8, 1.1), size * flat * rng.uniform(0.9, 1.2)))


# ----------------------------------------------------------------------------------------------- leaves

def leaf(part, base, direction, length, width, droop):
    """A pointed pepper leaf like the strawberry bush's blades: a creased midrib (7 points) zipped to a 10-point rim,
    20 triangles, bending down towards the tip; plus its underside a few millimetres below in a paler green.
    `width` is the half width at the widest point."""
    t, s, n = frame(direction)
    roll = rng.uniform(-0.2, 0.2)
    rot = Matrix.Rotation(roll, 3, t)
    s, n = rot @ s, rot @ n
    steps = 6
    base = Vector(base)

    def at(u, lateral, lift):
        return base + t * (u * length) + s * lateral + n * (lift - droop * length * u * u)

    mids = [at(u, 0, 0) for u in (i / steps for i in range(steps + 1))]
    lefts, rights = [], []
    for i in range(1, steps):
        u = i / steps
        w = width * math.sin(math.pi * min(u ** 0.8 + 0.04, 1.0)) ** 0.85
        for sgn, row in ((1, lefts), (-1, rights)):
            row.append(at(u + 0.03, sgn * w * rng.uniform(0.9, 1.08), 0.14 * w + rng.uniform(-0.12, 0.12) * w))
    verts = mids + lefts + rights
    L, R = steps + 1, steps + 1 + (steps - 1)
    faces = [(0, 1, L), (0, R, 1)]
    for i in range(1, steps - 1):
        m, l, r = i, L + i - 1, R + i - 1
        faces += [(m, m + 1, l + 1), (m, l + 1, l), (m, r, r + 1), (m, r + 1, m + 1)]
    faces += [(steps - 1, steps, L + steps - 2), (steps - 1, R + steps - 2, steps)]
    tint = jit((1, 1, 1), 0.07)
    stops = ((0.0, COLORS["leaf_base"]), (0.45, COLORS["leaf_mid"]), (1.0, COLORS["leaf_tip"]))

    def colour(u, rim):
        c = ramp(stops, u * 0.9 + rim * 0.12)
        return jit(tuple(x * y for x, y in zip(c, tint)), 0.04)

    cols = ([colour(i / steps, 0) for i in range(steps + 1)] + [colour(i / steps, 1) for i in range(1, steps)] * 2)
    part.add(verts, faces, cols, shade=0.16)
    # Underside: every facet on its own vertices, offset below and facing down.
    back_v, back_f, back_c = [], [], []
    for f in faces:
        k = len(back_v)
        for i in reversed(f):
            back_v.append(verts[i] - n * LEAF_THICKNESS)
            back_c.append(mix(cols[i], COLORS["leaf_back"], 0.35))
        back_f.append((k, k + 1, k + 2))
    part.add(back_v, back_f, back_c, shade=0.1)


def leaf_on_stalk(leaves, stems, at, direction, length, droop=None):
    d = Vector(direction).normalized()
    end = Vector(at) + d * (length * 0.1)
    tube(stems, [at, end], [0.011, 0.009], 5, COLORS["stem_tip"], COLORS["stem_tip"], cap_end=False)
    leaf(leaves, end, d, length, length * rng.uniform(0.27, 0.31),
         rng.uniform(0.12, 0.32) if droop is None else droop)


def leaf_cluster(leaves, stems, tip, along, size):
    """The leaves at a branch end, a loose rosette: one rising on along the branch, the others spreading round it
    nearly flat (facing up, like the reference), the ones turned back drooping a little."""
    heading = Vector((along.x, along.y, 0))
    heading = heading.normalized() if heading.length > 0.05 else Vector((1, 0, 0))
    count = rng.choice((4, 5, 5))
    leaf_on_stalk(leaves, stems, tip, heading + Z * rng.uniform(0.3, 0.55), size * rng.uniform(0.9, 1.05), droop=0.15)
    for k in range(count - 1):
        yaw = math.radians(-115 + 230 * (k + rng.uniform(-0.2, 0.2)) / max(count - 2, 1))
        d = Matrix.Rotation(yaw, 3, "Z") @ heading
        back = max(-d.dot(heading), 0.0)  # turned back towards the stem: lower and droopier
        d += Z * (rng.uniform(-0.1, 0.35) - 0.35 * back)
        leaf_on_stalk(leaves, stems, tip, d, size * rng.uniform(0.82, 1.0), droop=rng.uniform(0.15, 0.3) + 0.1 * back)


# ----------------------------------------------------------------------------------------------- peppers

def pepper(part, top, direction, length, radius, kind):
    """Long chili hanging from `top` along `direction`: a rounded shoulder, gently curved, tapering to a point,
    with a green star-shaped cap."""
    light, mid, dark = (COLORS[kind + x] for x in ("_light", "", "_dark"))
    top, d = Vector(top), Vector(direction).normalized()
    side = d.cross(Z if abs(d.z) < 0.95 else Vector((1, 0, 0))).normalized()
    bend = side * (length * rng.uniform(-0.14, 0.14)) + d.cross(side) * (length * rng.uniform(-0.06, 0.06))
    rings, sides = 10, 8
    pts, radii = [], []
    for i in range(rings + 1):
        f = i / rings
        pts.append(top + d * (length * f) + bend * (f * f))
        shoulder = math.sin(0.5 * math.pi * min(f * 3.5, 1.0)) ** 0.55
        radii.append(radius * (0.5 + 0.5 * shoulder) * (1 - f) ** 0.7 + 0.003)
    verts, cols = [], []
    t0 = (pts[1] - pts[0]).normalized()
    s = side
    roll = rng.uniform(0, math.pi)
    prev = t0
    for i, (p, r) in enumerate(zip(pts, radii)):
        t = (pts[min(i + 1, rings)] - pts[max(i - 1, 0)]).normalized()
        s = prev.rotation_difference(t) @ s
        s = (s - t * s.dot(t)).normalized()
        prev = t
        b = t.cross(s)
        c = ramp(((0.0, light), (0.3, mid), (0.8, mid), (1.0, dark)), i / rings)
        for k in range(sides):
            a = roll + 2 * math.pi * k / sides
            verts.append(p + (s * math.cos(a) + b * math.sin(a)) * r)
            cols.append(jit(c, 0.03))
    faces = []
    for i in range(rings):
        for k in range(sides):
            j = (k + 1) % sides
            faces.append((i * sides + k, i * sides + j, (i + 1) * sides + j, (i + 1) * sides + k))
    faces.append(tuple(reversed(range(sides))))
    verts.append(pts[-1] + (pts[-1] - pts[-2]).normalized() * radius * 0.35)
    cols.append(dark)
    faces += [(rings * sides + k, rings * sides + (k + 1) % sides, len(verts) - 1) for k in range(sides)]
    part.add(verts, faces, cols, shade=0.26)
    # Cap: a short cup over the shoulder and five pointed sepals lying on it.
    b = t0.cross(side)
    tube(part, [top - t0 * radius * 0.15, top + t0 * radius * 0.2], [radius * 0.42, radius * 0.62], 7,
         COLORS["calyx"], COLORS["calyx"], cap_end=False)
    sv, sf, sc = [], [], []
    for k in range(5):
        a = 2 * math.pi * k / 5 + roll
        for da, rr, dz, col in ((-0.38, 0.62, 0.15, "calyx"), (0.38, 0.62, 0.15, "calyx"),
                                (0.0, 1.12, 0.55, "calyx_tip")):
            rad = s * math.cos(a + da) + b * math.sin(a + da)
            sv.append(top + rad * (radius * rr) + t0 * (radius * dz))
            sc.append(COLORS[col])
        sf.append((len(sv) - 3, len(sv) - 2, len(sv) - 1))
    part.add(sv, sf, sc, shade=0.2)


# ----------------------------------------------------------------------------------------------- plant

# Main branches leaving the stem: yaw (deg; 0 = +X, -90 = towards the front camera), height on the stem, elevation
# at the start and at the end (deg; they arch upwards), length, and how far they swing sideways. Deliberately uneven.
MAINS = [
    (8, 0.47, 40, 64, 0.86, 0.12),
    (66, 0.53, 50, 72, 0.96, -0.1),
    (131, 0.44, 34, 60, 0.82, 0.14),
    (197, 0.5, 44, 68, 0.92, -0.08),
    (247, 0.42, 30, 56, 0.8, 0.12),
    (304, 0.52, 48, 74, 0.98, -0.12),
    (160, 0.55, 72, 86, 1.0, 0.06),  # the leader, nearly upright
]
# Side shoots per main branch: where along it (0..1), which way (sideways deg), elevation (deg), length. They
# spread outwards, so the canopy ends up about as wide as the mound.
SHOOTS = [
    [(0.5, 46, 22, 0.6)],
    [(0.42, -38, 18, 0.62), (0.7, 52, 34, 0.48)],
    [(0.55, -50, 14, 0.56)],
    [(0.4, 36, 20, 0.62), (0.68, -58, 36, 0.46)],
    [(0.5, -44, 12, 0.54)],
    [(0.45, 40, 24, 0.6), (0.75, -30, 40, 0.42)],
    [(0.45, -62, 30, 0.58), (0.7, 54, 38, 0.5)],
]
PEPPER_KINDS = ["green", "yellow", "green", "green", "yellow", "green", "yellow", "green", "yellow", "green",
                "yellow", "green"]  # the red one is placed separately, front right


def branch_path(start, out, side, e0, e1, length, swing):
    d0 = out * math.cos(math.radians(e0)) + Z * math.sin(math.radians(e0))
    d1 = out * math.cos(math.radians(e1)) + Z * math.sin(math.radians(e1))
    end = start + (d0 + d1).normalized() * length + side * (swing * length)
    p1 = start + d0 * (length * 0.38) + side * (swing * length * 0.3)
    p2 = end - d1 * (length * 0.36)
    return bezier(start, p1, p2, end, 8)


def build_plant():
    stems, leaves = Part("Stems"), Part("Leaves")
    foot, crown = Vector((0, 0, MOUND_TOP - 0.1)), Vector((0.03, -0.01, 0.56))
    trunk = bezier(foot, foot + Z * 0.15, crown - Z * 0.15 + Vector((0.025, 0, 0)), crown, 6)
    tube(stems, trunk, [0.085, 0.08, 0.075, 0.07, 0.066, 0.062, 0.058], 8, COLORS["stem_foot"],
         mix(COLORS["stem_foot"], COLORS["stem_tip"], 0.25), cap_end=False)
    ends, nodes = [], []  # ends: (tip, tangent, leaf size); nodes: (point, tangent, outward) for leaves/peppers
    for m, (yaw, h, e0, e1, length, swing) in enumerate(MAINS):
        a = math.radians(yaw + rng.uniform(-6, 6))
        out = Vector((math.cos(a), math.sin(a), 0))
        side = Vector((-out.y, out.x, 0))
        start, _ = sample(trunk, (h - foot.z) / (crown.z - foot.z))
        path = branch_path(start, out, side, e0, e1, length * 1.15, swing)
        n = len(path)
        r0 = 0.06 if m < 6 else 0.052
        radii = [r0 * (1 - 0.68 * i / (n - 1)) for i in range(n)]
        tube(stems, path, radii, 7, mix(COLORS["stem_foot"], COLORS["stem_tip"], 0.2), COLORS["stem_tip"])
        ends.append((path[-1], (path[-1] - path[-2]).normalized(), rng.uniform(0.48, 0.56)))
        for t in (0.32, 0.56, 0.8):
            p, tan = sample(path, t + rng.uniform(-0.04, 0.04))
            nodes.append((p, tan, out))
        for t, turn, elev, slen in SHOOTS[m]:
            p, tan = sample(path, t)
            turn_out = Matrix.Rotation(math.radians(turn), 3, "Z") @ Vector((tan.x, tan.y, 0)).normalized()
            sside = Vector((-turn_out.y, turn_out.x, 0))
            spath = branch_path(p, turn_out, sside, elev - 12, elev + 15, slen * 1.15, rng.uniform(-0.1, 0.1))
            k = len(spath)
            r1 = radii[int(t * (n - 1))] * 0.78
            tube(stems, spath, [r1 * (1 - 0.6 * i / (k - 1)) for i in range(k)], 6,
                 mix(COLORS["stem_foot"], COLORS["stem_tip"], 0.5), COLORS["stem_tip"])
            ends.append((spath[-1], (spath[-1] - spath[-2]).normalized(), rng.uniform(0.42, 0.5)))
            for st in (0.35, 0.72):
                q, qt = sample(spath, st + rng.uniform(-0.05, 0.05))
                nodes.append((q, qt, turn_out))

    for tip, along, size in ends:
        leaf_cluster(leaves, stems, tip, along, size)
    rng.shuffle(nodes)
    # Peppers hang on the outside of the bush, well apart, where they show (the reference's are all in view).
    candidates = [(p, out) for p, _t, out in nodes if 0.88 < p.z < 1.55 and Vector((p.x, p.y)).length > 0.5]
    red_at = max(candidates, key=lambda c: c[0].x * 0.6 - c[0].y * 0.8 - abs(c[0].z - 1.15) * 0.8)
    chosen = [red_at]
    for c in sorted(candidates, key=lambda c: rng.random()):
        if len(chosen) > len(PEPPER_KINDS):
            break
        if all((c[0] - o[0]).length > 0.22 for o in chosen):
            chosen.append(c)
    peppers = []
    for i, (p, out) in enumerate(chosen):
        kind = "red" if i == 0 else PEPPER_KINDS[(i - 1) % len(PEPPER_KINDS)]
        horiz = Vector((p.x, p.y, 0))
        away = (horiz.normalized() if horiz.length > 0.05 else out) * 0.7 + out * 0.3
        cap = p + away * 0.11 - Z * 0.09
        tube(stems, bezier(p, p + away * 0.05 + Z * 0.04, cap + Z * 0.05, cap, 4), [0.014, 0.013, 0.012, 0.011, 0.01],
             5, COLORS["stem_tip"], COLORS["calyx"], cap_end=False)
        length = rng.uniform(0.36, 0.44) * (1.1 if kind == "red" else 1.0)
        hang = (-Z + away * rng.uniform(0.12, 0.3)).normalized()
        peppers.append((cap, hang, length, length * rng.uniform(0.1, 0.115), kind))
    # Single leaves at the other nodes (alternating sides, not every one), clear of the peppers.
    for i, (p, tan, out) in enumerate(nodes):
        if rng.random() < 0.3 or any((p - c[0]).length < 0.2 for c in chosen):
            continue
        side = tan.cross(Z).normalized() * (1 if i % 2 else -1)
        leaf_on_stalk(leaves, stems, p, side + out * 0.4 + Z * rng.uniform(-0.15, 0.25), rng.uniform(0.38, 0.46))
    return stems, leaves, peppers


# ----------------------------------------------------------------------------------------------- assembly

def paint_material(image):
    """The strawberry plants' material: Paint x object-space noise (0.88..1.1) -> HSV (sat .95, val .92), with an
    image node as the bake target."""
    mat = bpy.data.materials.get("PP_Paint") or bpy.data.materials.new("PP_Paint")
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Roughness"].default_value = 0.95
    paint = nt.nodes.new("ShaderNodeVertexColor")
    paint.layer_name = "Paint"
    coords = nt.nodes.new("ShaderNodeTexCoord")
    mapping = nt.nodes.new("ShaderNodeMapping")
    mapping.inputs["Scale"].default_value = (1, 1, 2.5)
    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 9
    noise.inputs["Detail"].default_value = 3
    noise.inputs["Roughness"].default_value = 0.55
    remap = nt.nodes.new("ShaderNodeMapRange")
    remap.inputs[3].default_value = 0.88
    remap.inputs[4].default_value = 1.1
    mul = nt.nodes.new("ShaderNodeMix")
    mul.data_type = "RGBA"
    mul.blend_type = "MULTIPLY"
    mul.inputs[0].default_value = 1.0
    hsv = nt.nodes.new("ShaderNodeHueSaturation")
    hsv.inputs["Saturation"].default_value = 0.95
    hsv.inputs["Value"].default_value = 0.92
    target = nt.nodes.new("ShaderNodeTexImage")
    target.name = target.label = "BakeTarget"
    target.image = image
    links = nt.links
    links.new(coords.outputs["Object"], mapping.inputs["Vector"])
    links.new(mapping.outputs["Vector"], noise.inputs["Vector"])
    links.new(noise.outputs["Fac"], remap.inputs["Value"])
    links.new(paint.outputs["Color"], mul.inputs[6])
    links.new(remap.outputs["Result"], mul.inputs[7])
    links.new(mul.outputs[2], hsv.inputs["Color"])
    links.new(hsv.outputs["Color"], bsdf.inputs["Base Color"])
    links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    nt.nodes.active = target
    for i, node in enumerate((coords, mapping, noise, remap, paint, mul, hsv, bsdf, out)):
        node.location = (-1400 + i * 200, 0)
    target.location = (-400, -300)
    return mat


def atlas_material(image):
    """What Unity gets: the baked atlas straight into the base colour."""
    mat = bpy.data.materials.get("M_PepperPlant") or bpy.data.materials.new("M_PepperPlant")
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Roughness"].default_value = 0.6
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = image
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    out.location, bsdf.location, tex.location = (300, 0), (0, 0), (-350, 0)
    return mat


def atlas_image():
    image = bpy.data.images.get(ATLAS)
    if image is None:
        image = bpy.data.images.new(ATLAS, 2048, 2048, alpha=False)
    image.colorspace_settings.name = "sRGB"
    image.filepath_raw = os.path.join(HERE, ATLAS + ".png")
    image.file_format = "PNG"
    return image


def to_object(part, coll, mat, parent, origin=Vector()):
    me = bpy.data.meshes.get(part.name)
    if me:
        me.name = part.name + "_old"
    me = bpy.data.meshes.new(part.name)
    origin = Vector(origin) * SCALE
    me.from_pydata([Vector(v) * SCALE - origin for v in part.verts], [], part.faces)
    me.validate()
    paint = me.color_attributes.new("Paint", "FLOAT_COLOR", "CORNER")
    for poly, corner_cols in zip(me.polygons, part.cols):
        for li, c in zip(poly.loop_indices, corner_cols):
            paint.data[li].color = (*lin(c), 1.0)
    me.uv_layers.new(name="UVMap")
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
    rng = random.Random(11)
    mat = paint_material(atlas_image())
    coll = bpy.data.collections.get("PepperPlant")
    if coll:
        for o in list(coll.objects):
            bpy.data.objects.remove(o, do_unlink=True)
    else:
        coll = bpy.data.collections.new("PepperPlant")
        bpy.context.scene.collection.children.link(coll)
    root = bpy.data.objects.new("PepperPlant_Root", None)
    coll.objects.link(root)
    ground = Part("Mound")
    mound(ground)
    stems, leaves, peppers = build_plant()
    for part in (ground, stems, leaves):
        to_object(part, coll, mat, root)
    for n, (cap, hang, length, radius, kind) in enumerate(sorted(peppers, key=lambda p: (-p[0].z, p[0].x)), 1):
        part = Part("Pepper_%02d" % n)
        pepper(part, cap, hang, length, radius, kind)
        centre = sum((Vector(v) for v in part.verts), Vector()) / len(part.verts)
        to_object(part, coll, mat, root, origin=centre)
    for me in [m for m in bpy.data.meshes if m.users == 0]:
        bpy.data.meshes.remove(me)
    objs = [o for o in coll.objects if o.type == "MESH"]
    return {"peppers": len(peppers), "kinds": sorted(p[4] for p in peppers),
            "faces": {o.name: len(o.data.polygons) for o in objs if not o.name.startswith("Pepper_")},
            "total": sum(len(o.data.polygons) for o in objs)}


def _meshes():
    return [o for o in bpy.data.collections["PepperPlant"].objects if o.type == "MESH"]


def _select(objs):
    bpy.context.view_layer.update()
    for o in bpy.context.scene.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]


def bake():
    """Smart-project every piece into one UV atlas and bake the painted colour into T_PepperPlant_Albedo."""
    objs = _meshes()
    image = atlas_image()
    paint = paint_material(image)
    for o in objs:
        o.data.materials.clear()
        o.data.materials.append(paint)
    _select(objs)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.002, area_weight=0.0,
                             scale_to_bounds=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    scene = bpy.context.scene
    engine = scene.render.engine
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 4
    scene.render.bake.margin = 6
    bpy.ops.object.bake(type="DIFFUSE", pass_filter={"COLOR"}, use_clear=True, margin=6)
    scene.render.engine = engine
    image.save()
    final = atlas_material(image)
    for o in objs:
        o.data.materials.clear()
        o.data.materials.append(final)
    return {"atlas": image.filepath_raw, "objects": len(objs)}


def export():
    """FBX of the plant's meshes (axis conversion baked) and the baked atlas into Assets/Art/PepperPlant."""
    textures = os.path.join(OUT, "Textures")
    os.makedirs(textures, exist_ok=True)
    shutil.copyfile(os.path.join(HERE, ATLAS + ".png"), os.path.join(textures, ATLAS + ".png"))
    objs = _meshes()
    _select(objs)
    path = os.path.join(OUT, "PepperPlant.fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"}, global_scale=EXPORT_SCALE,
                             apply_unit_scale=True,
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                             bake_space_transform=True, mesh_smooth_type="FACE", use_mesh_modifiers=True,
                             bake_anim=False, path_mode="STRIP", embed_textures=False, colors_type="NONE")
    return {"fbx": path, "objects": len(objs)}
