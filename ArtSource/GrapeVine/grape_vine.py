"""GrapeVine: a low-poly vineyard row, modelled after the reference picture the team chose: a long rocky dirt bed,
three square wooden posts with pointed tops carrying two wires, and three separate vines: each a gnarled trunk
S-bending up to its own crown, branches spreading every way from it, big five-lobed grape leaves in a round clump
round the crown, and bunches of faceted purple grapes hanging from low under the crown up
to its top.

Built the way the strawberry bush and the pepper plant are (ArtSource/StrawberryBush.blend, PepperPlant.blend):
- leaves: a five-lobed outline with a small tooth on every edge, each lobe folded along its vein (a light and a dark
  half), cupped and drooping, leaning every which way, with a separate underside layer a few millimetres below in a
  paler green, so a leaf reads as a thin sheet with a dark edge;
- trunks and branches: parallel-transported tubes, gnarled (uneven, twisting rings);
- bed: a faceted rounded-rectangle dirt bed with big chunky rocks round the rim and smaller ones on top;
- colour: a per-corner "Paint" attribute (soft gradients, a little per-vertex variation, facets shaded by how they
  face the light), times object-space noise (0.88..1.1) and a Hue/Saturation/Value node, baked into one atlas
  (T_GrapeVine_Albedo, 2048) on a smart-projected UV map. Unity gets one material.
Structure follows the other edible plants (Assets/Editor/Eating/BerryPlantImporter.cs): Mound, Trellis, Stems,
Leaves and one object per bunch, Grape_NN, each with its origin at its centre (not hooked into the game).
Units are metres (exported at EXPORT_SCALE), Z up, origin at the middle of the bed; the row runs along X.
Run inside Blender: exec(code, {"__file__": path}); build(); bake(); export().
"""
import bisect
import math
import os
import random
import shutil

import bmesh
import bpy
from mathutils import Matrix, Vector, geometry

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.abspath(os.path.join(HERE, "..", "..", "Assets", "Art", "GrapeVine"))
SCALE = 1.0  # built at the strawberry bush's .blend scale
EXPORT_SCALE = 0.52  # exported at the berry bushes' in-game scale (~2 m long, ~1.15 m tall)

COLORS = {  # sRGB
    "bark_foot": (0.34, 0.21, 0.13), "bark": (0.47, 0.30, 0.18), "bark_tip": (0.55, 0.38, 0.22),
    "shoot": (0.50, 0.50, 0.24), "shoot_tip": (0.47, 0.63, 0.25), "petiole": (0.48, 0.66, 0.25),
    "leaf_base": (0.34, 0.58, 0.15), "leaf_mid": (0.48, 0.74, 0.22), "leaf_tip": (0.60, 0.84, 0.30),
    "leaf_back": (0.66, 0.78, 0.45),
    "purple_light": (0.63, 0.43, 0.82), "purple": (0.45, 0.26, 0.65), "purple_dark": (0.28, 0.14, 0.44),
    "stalk": (0.42, 0.48, 0.20),
    "wood_light": (0.68, 0.45, 0.27), "wood": (0.55, 0.34, 0.19), "wood_dark": (0.38, 0.22, 0.12),
    "wire_light": (0.48, 0.50, 0.52), "wire": (0.34, 0.35, 0.37),
    "dirt_light": (0.50, 0.34, 0.25), "dirt": (0.42, 0.28, 0.20), "dirt_dark": (0.30, 0.20, 0.14),
    "rock_light": (0.67, 0.48, 0.37), "rock": (0.56, 0.39, 0.29), "rock_dark": (0.40, 0.27, 0.20),
}
ATLAS = "T_GrapeVine_Albedo"

rng = random.Random(3)
Z = Vector((0, 0, 1))
KEY = Vector((-0.4, -0.5, 0.75)).normalized()
HALF_L, HALF_W, BED_TOP = 1.95, 0.62, 0.2
POSTS_X = (-1.55, 0.0, 1.55)
POST_TOP = 2.15
WIRES_Z = (1.22, 1.9)
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


def tint(c, t):
    return tuple(min(x * y, 1.0) for x, y in zip(c, t))


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


def bezier(p0, p1, p2, p3, steps):
    p0, p1, p2, p3 = (Vector(p) for p in (p0, p1, p2, p3))
    pts = []
    for i in range(steps + 1):
        u = i / steps
        a = 1 - u
        pts.append(p0 * a ** 3 + p1 * 3 * a * a * u + p2 * 3 * a * u * u + p3 * u ** 3)
    return pts


def tube(part, points, radii, sides, foot, tip, cap_end=True, shade=0.2, wobble=0.0, twist=0.0):
    """A tube along `points` (parallel-transported rings, so it never twists unless asked), coloured foot -> tip.
    wobble: rings a little uneven (gnarled bark); twist: radians the rings turn per ring."""
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
        ring = r * (1 + rng.uniform(-wobble, wobble))
        for k in range(sides):
            a = roll + twist * i + 2 * math.pi * k / sides
            verts.append(p + (s * math.cos(a) + b * math.sin(a)) * ring * rng.uniform(0.94, 1.06))
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


# ----------------------------------------------------------------------------------------------- bed

def bed_u(x, y):
    """0 in the middle of the bed, 1 on its rounded-rectangle rim."""
    return (abs(x / HALF_L) ** 4 + abs(y / HALF_W) ** 4) ** 0.25


def bed_height(u):
    return BED_TOP * (1 - min(u, 1.0) ** 5)


def rim(u, phi):
    c, s = math.cos(phi), math.sin(phi)
    return Vector((HALF_L * u * math.copysign(abs(c) ** 0.5, c), HALF_W * u * math.copysign(abs(s) ** 0.5, s), 0))


_RING_TABLES = {}


def ring_point(u, f):
    """A point of the bed's ring u, a fraction f of the way round it by length. The rings are the rim drawn in by the
    same distance all round (u = 1 the rim, u -> 0 a line down the middle), not shrunk copies of it, so they keep an
    even spacing along the long sides instead of closing in on the ends; and points go round by length, not angle,
    which would bunch them at the ends. Either way the dirt would break into long thin slivers."""
    if u not in _RING_TABLES:
        inset = (1 - u) * HALF_W
        a, b = HALF_L - inset, HALF_W - inset
        samples = []
        for i in range(2001):
            c, s = math.cos(2 * math.pi * i / 2000), math.sin(2 * math.pi * i / 2000)
            samples.append(Vector((a * math.copysign(abs(c) ** 0.5, c), b * math.copysign(abs(s) ** 0.5, s), 0)))
        lengths = [0.0]
        for p, q in zip(samples, samples[1:]):
            lengths.append(lengths[-1] + (q - p).length)
        _RING_TABLES[u] = (samples, lengths)
    samples, lengths = _RING_TABLES[u]
    target = (f % 1.0) * lengths[-1]
    i = min(max(bisect.bisect_left(lengths, target), 1), len(lengths) - 1)
    t = (target - lengths[i - 1]) / max(lengths[i] - lengths[i - 1], 1e-9)
    return samples[i - 1].lerp(samples[i], t)


def around(u, count, jitter):
    """`count` points spread evenly (by length) round the rim at u, a little uneven."""
    samples = [rim(u, 2 * math.pi * i / 600) for i in range(601)]
    lengths = [0.0]
    for a, b in zip(samples, samples[1:]):
        lengths.append(lengths[-1] + (b - a).length)
    out = []
    for k in range(count):
        target = lengths[-1] * (k + rng.uniform(-jitter, jitter)) / count % lengths[-1]
        i = next(i for i, l in enumerate(lengths) if l >= target)
        out.append(samples[max(i, 1)])
    return out


def icosphere(subdiv):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    bm.verts.index_update()
    verts = [v.co.copy() for v in bm.verts]
    faces = [[v.index for v in f.verts] for f in bm.faces]
    bm.free()
    return verts, faces


ICO1 = icosphere(1)


def rock(part, center, radii, sink=0.4):
    """A chunky angular rock: a jittered, flattened icosphere, lighter on top, darker underneath."""
    verts, faces = ICO1
    c = Vector(center)
    rot = Matrix.Rotation(rng.uniform(0, math.pi), 3, "Z") @ Matrix.Rotation(rng.uniform(-0.25, 0.25), 3, "X")
    t = jit((1, 1, 1), 0.08)
    pts, cols = [], []
    for d in verts:
        d = d * (1 + rng.uniform(-0.2, 0.2))
        d = Vector((d.x * radii[0], d.y * radii[1], max(d.z, -sink) * radii[2]))
        pts.append(c + rot @ d)
        h = d.z / radii[2]
        col = ramp(((-0.5, COLORS["rock_dark"]), (0.1, COLORS["rock"]), (0.8, COLORS["rock_light"])), (h + 0.5) / 1.3)
        cols.append(jit(tint(col, t), 0.04))
    part.add(pts, faces, cols, shade=0.32)


def bed(part, keep_clear):
    rings = [0.18, 0.36, 0.52, 0.66, 0.77, 0.86, 0.93, 0.98, 1.03]
    seg = 60
    verts, cols = [], []
    for ri, u in enumerate(rings):
        off = 0.5 * (ri % 2)
        for k in range(seg):
            spread = rng.uniform(0.97, 1.03)
            p = ring_point(u, (k + off + rng.uniform(-0.2, 0.2)) / seg) * spread
            p.z = bed_height(u) + rng.uniform(-0.016, 0.016) if ri < len(rings) - 1 else -0.03
            verts.append(p)
            cols.append(jit(ramp(((0, COLORS["dirt_light"]), (0.6, COLORS["dirt"]), (1, COLORS["dirt_dark"])), u),
                            0.07))
    # The innermost ring is a long narrow strip down the middle: close it by zipping its two long sides (point k with
    # its mirror seg - k), so no fan of slivers meets in one point.
    faces = []
    half = seg // 2
    for k in range(half):
        a, b, c, d = k, k + 1, (seg - k - 1) % seg, (seg - k) % seg
        if d != a:
            faces.append((d, a, b))
        if b != c:
            faces.append((d, b, c))
    for i in range(len(rings) - 1):
        b0, b1 = i * seg, (i + 1) * seg
        for k in range(seg):
            j = (k + 1) % seg
            if (i + 1) % 2:  # the next ring is turned half a segment: zig-zag triangles
                faces += [(b0 + k, b1 + k, b0 + j), (b0 + j, b1 + k, b1 + j)]
            else:
                faces += [(b0 + k, b1 + k, b1 + j), (b0 + k, b1 + j, b0 + j)]
    part.add(verts, faces, cols, shade=0.22)
    # Big rocks round the rim, medium ones just inside, small ones on top, clear of the posts and trunks.
    for u, count, (s0, s1), z_off in ((0.95, 26, (0.17, 0.26), 0.02), (0.8, 16, (0.1, 0.15), 0.0),
                                      (0.5, 12, (0.05, 0.08), 0.0)):
        for p in around(u, count, 0.3):
            if u < 0.9:
                p = p * rng.uniform(0.7, 1.05)
            if any((Vector((p.x, p.y)) - Vector(c)).length < 0.22 for c in keep_clear):
                continue
            size = rng.uniform(s0, s1)
            p.z = bed_height(bed_u(p.x, p.y)) + z_off + size * 0.1
            rock(part, p, (size * rng.uniform(1.0, 1.35), size * rng.uniform(0.8, 1.1), size * 0.55 * rng.uniform(0.9, 1.2)))


# ----------------------------------------------------------------------------------------------- trellis

def post(part, x):
    """Square wooden post with chamfered corners, a slight lean and a pointed top."""
    w = 0.065
    ring2d = []
    for qx, qy in ((1, 1), (-1, 1), (-1, -1), (1, -1)):  # each corner cut: two points
        ring2d += [Vector((qx * w * 0.6, qy * w)), Vector((qx * w, qy * w * 0.6))] if qx * qy < 0 else \
                  [Vector((qx * w, qy * w * 0.6)), Vector((qx * w * 0.6, qy * w))]
    ring2d = sorted(ring2d, key=lambda v: math.atan2(v.y, v.x))
    top = POST_TOP + rng.uniform(-0.04, 0.03)
    lean = Vector((rng.uniform(-0.03, 0.03), rng.uniform(-0.02, 0.02), 0))
    levels = [(-0.05, 1.0), (0.6, 0.98), (1.4, 0.96), (top - 0.13, 0.94)]
    verts, cols = [], []
    for z, k in levels:
        f = z / top
        for v in ring2d:
            verts.append(Vector((x + v.x * k, v.y * k, z)) + lean * f)
            cols.append(jit(ramp(((0, COLORS["wood_dark"]), (0.3, COLORS["wood"]), (1, COLORS["wood_light"])), f), 0.06))
    n = len(ring2d)
    faces = []
    for i in range(len(levels) - 1):
        for k in range(n):
            j = (k + 1) % n
            faces.append((i * n + k, i * n + j, (i + 1) * n + j, (i + 1) * n + k))
    faces.append(tuple(reversed(range(n))))
    verts.append(Vector((x, 0, top)) + lean)
    cols.append(COLORS["wood_light"])
    last = (len(levels) - 1) * n
    faces += [(last + k, last + (k + 1) % n, len(verts) - 1) for k in range(n)]
    part.add(verts, faces, cols, shade=0.36)


def wires(part):
    """Two wires along the front of the posts, sagging a little between them, coiled round each post."""
    r = 0.013
    for z in WIRES_Z:
        for x0, x1 in zip(POSTS_X, POSTS_X[1:]):
            pts = [Vector((x0 + (x1 - x0) * i / 8, -0.074, z - 0.03 * math.sin(math.pi * i / 8))) for i in range(9)]
            tube(part, pts, [r] * 9, 5, COLORS["wire"], COLORS["wire"], cap_end=False, shade=0.3)
        for x in POSTS_X:
            coil = [Vector((x + 0.082 * math.cos(a), 0.082 * math.sin(a), z - 0.02 + 0.04 * a / (4 * math.pi)))
                    for a in (i * math.pi / 6 for i in range(25))]
            tube(part, coil, [r] * 25, 5, COLORS["wire_light"], COLORS["wire"], cap_end=False, shade=0.3)


# ----------------------------------------------------------------------------------------------- leaves

LOBES = [(-108, 0.52), (-54, 0.82), (0, 1.0), (54, 0.82), (108, 0.52)]


def leaf_shape(length):
    """A grape leaf in its own plane (x along the leaf from the petiole, y to its left): five pointed lobes round
    the point the veins spread from, a small tooth on every edge, a heart-shaped base. Returns the vein centre,
    reach, outline and vein points."""
    centre, reach = Vector((0.2 * length, 0.0)), 0.8 * length
    lobes = [(math.radians(a + rng.uniform(-5, 5)), l * rng.uniform(0.94, 1.05)) for a, l in LOBES]
    tips = [centre + Vector((math.cos(a), math.sin(a))) * (reach * l) for a, l in lobes]
    sinuses = []
    for (a0, l0), (a1, l1) in zip(lobes, lobes[1:]):
        a = (a0 + a1) / 2
        sinuses.append(centre + Vector((math.cos(a), math.sin(a))) * (reach * 0.33 * (l0 + l1) * rng.uniform(0.92, 1.08)))
    basal = [centre + Vector((math.cos(math.radians(a)), math.sin(math.radians(a)))) * (reach * 0.42)
             for a in (-150, 150)]
    seq = [basal[0]]
    for k in range(5):
        seq.append(tips[k])
        if k < 4:
            seq.append(sinuses[k])
    seq.append(basal[1])
    outline = [Vector((0.0, 0.0))]
    for a, b in zip(seq, seq[1:]):
        outline.append(a)
        d = b - a
        out = Vector((-d.y, d.x)).normalized()
        if out.dot(a.lerp(b, 0.5) - centre) < 0:
            out = -out
        outline.append(a.lerp(b, 0.5) + out * (0.035 * length))  # one small tooth on every edge
    outline.append(seq[-1])
    veins = [centre] + [centre.lerp(t, 0.55) for t in tips]
    return centre, reach, outline, veins, set(map(tuple, sinuses))


def leaf(part, base, direction, facing, length):
    """A grape leaf at `base` whose blade runs along `direction` and faces towards `facing`, every lobe folded along
    its vein so one half catches the light and the other does not (the reference's two-tone lobes); plus its paler
    underside a few millimetres below."""
    t = Vector(direction).normalized()
    n = Vector(facing) - t * Vector(facing).dot(t)
    n = n.normalized() if n.length > 1e-4 else t.orthogonal().normalized()
    s = n.cross(t)
    centre, reach, outline, veins, sinuses = leaf_shape(length)
    flat = outline + veins
    tris = geometry.delaunay_2d_cdt(flat, [], [list(range(len(outline)))], 1, 1e-6)[2]
    leaf_tint = jit((1, 1, 1), 0.06)
    stops = ((0.0, COLORS["leaf_base"]), (0.55, COLORS["leaf_mid"]), (1.0, COLORS["leaf_tip"]))
    droop = rng.uniform(0.04, 0.16)
    cup = rng.uniform(0.04, 0.12)  # the lobes curl up a little round the middle, so no leaf lies flat
    verts, cols = [], []
    for i, p in enumerate(flat):
        r = (p - centre).length / reach
        z = cup * length * r * r - droop * length * max(p.x / length, 0.0) ** 2
        if i > len(outline):
            z += 0.06 * length  # vein ridge
        elif i == len(outline):
            z += 0.03 * length
        elif tuple(p) in sinuses:
            z -= 0.02 * length
        verts.append(Vector(base) + t * p.x + s * p.y + n * z)
        cols.append(jit(tint(ramp(stops, r), leaf_tint), 0.02))
    faces = [tuple(f) for f in tris]
    part.add(verts, faces, cols, shade=0.34)
    back_v, back_f, back_c = [], [], []
    for f in faces:
        k = len(back_v)
        for i in reversed(f):
            back_v.append(verts[i] - n * LEAF_THICKNESS)
            back_c.append(mix(cols[i], COLORS["leaf_back"], 0.35))
        back_f.append((k, k + 1, k + 2))
    part.add(back_v, back_f, back_c, shade=0.1)


def leaf_on(leaves, stems, middle, outward, size, branches):
    """A leaf on the outside of a vine's crown: its blade middle at `middle`, facing out along `outward` (some
    leaves tilted a lot, turned sideways or lying back), its tip running round the crown and mostly upwards, on a
    petiole curving in from the nearest branch."""
    outward = Vector(outward).normalized()
    tip = Z * 0.7 + Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-0.6, 0.6)))
    tip -= outward * (tip.dot(outward) * 0.8)
    tip.normalize()
    steep = rng.random() < 0.35
    face = outward + Z * 0.25 + Vector([rng.uniform(-0.7, 0.7) if steep else rng.uniform(-0.25, 0.25) for _ in range(3)])
    base = Vector(middle) - tip * (size * 0.45)
    hook = min((q for path in branches for q in path), key=lambda q: (q - base).length)
    tube(stems, bezier(hook, hook.lerp(base, 0.4) + Z * 0.05, base - tip * 0.06, base, 4),
         [0.012, 0.011, 0.01, 0.009, 0.008], 5, COLORS["shoot"], COLORS["petiole"], cap_end=False)
    leaf(leaves, base, tip, face, size)


# ----------------------------------------------------------------------------------------------- grapes

def bunch(part, top, size):
    """A bunch of faceted purple grapes hanging from `top`: rings of berries narrowing to one at the bottom, a little
    lopsided, on a short stalk. Lighter on top of every berry, darker underneath."""
    light, mid, dark = COLORS["purple_light"], COLORS["purple"], COLORS["purple_dark"]
    top = Vector(top)
    br = 0.062 * size
    rows = 5
    lean = Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), 0)) * br * 1.3
    centres = []
    for i in range(rows):
        f = i / (rows - 1)
        radius = br * 2.0 * (1 - f) ** 0.7
        z = top.z - br * 1.1 - i * br * 1.35
        count = 1 if i == rows - 1 else max(2, round(2 * math.pi * radius / (br * 2.05)))
        start = rng.uniform(0, 2 * math.pi)
        for k in range(count):
            a = start + 2 * math.pi * (k + rng.uniform(-0.12, 0.12)) / count
            c = Vector((top.x, top.y, z)) + Vector((math.cos(a), math.sin(a), 0)) * radius + lean * f
            centres.append(c + Vector([rng.uniform(-0.25, 0.25) * br for _ in range(3)]))
        if radius > br * 1.6:  # fill the middle of the wide rows
            centres.append(Vector((top.x, top.y, z - br * 0.3)) + lean * f)
    verts0, faces0 = ICO1
    for c in centres:
        r = br * rng.uniform(0.88, 1.1)
        rot = Matrix.Rotation(rng.uniform(0, math.pi), 3, Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), 1)).normalized())
        berry_tint = jit((1, 1, 1), 0.08)
        verts, cols = [], []
        for d in verts0:
            d = rot @ d
            verts.append(c + Vector((d.x * r, d.y * r, d.z * r * 1.08)))
            cols.append(jit(tint(ramp(((-1, dark), (0, mid), (0.8, light)), d.z), berry_tint), 0.03))
        part.add(verts, list(faces0), cols, shade=0.3)
    tube(part, [top + Z * 0.02, top - Z * br * 1.6], [0.013, 0.011], 5, COLORS["stalk"], COLORS["stalk"])


# ----------------------------------------------------------------------------------------------- vines

# Three separate vines, as in the reference picture: each trunk S-bends up to its own crown, where branches spread
# every way (sideways, up, to the front and back, a little down) and the leaves gather in a round clump, with gaps
# between the three. Per vine: the points its trunk passes through (foot to crown), its crown (centre, half sizes
# along x, y, z) and how many leaves. The bunches hang in among the leaves all over the crown (BUNCH_SPOTS).
VINES = [
    {"trunk": [(-0.87, -0.1, 0.12), (-0.72, -0.08, 0.6), (-0.86, -0.06, 0.98), (-1.08, -0.04, 1.3),
               (-1.2, -0.02, 1.45)],
     "crown": ((-1.2, 0.0, 1.38), (0.68, 0.42, 0.72)), "leaves": 23},
    {"trunk": [(0.18, -0.1, 0.12), (0.34, -0.08, 0.6), (0.42, -0.06, 1.0), (0.28, -0.04, 1.35), (0.12, -0.02, 1.55)],
     "crown": ((0.1, 0.0, 1.52), (0.58, 0.42, 0.78)), "leaves": 21},
    {"trunk": [(1.21, -0.09, 0.12), (1.15, -0.08, 0.62), (1.08, -0.06, 1.0), (1.14, -0.04, 1.3), (1.25, -0.02, 1.5)],
     "crown": ((1.27, 0.0, 1.42), (0.62, 0.42, 0.7)), "leaves": 22},
]
# Where the bunches hang in each crown, on its front and on its back: across (-1 left .. 1 right) and up (-1 bottom
# .. 1 top) the crown, spread over all of it, top included, so they sit in among the leaves.
BUNCH_SPOTS = {-1: [(-0.55, 0.2), (0.15, 0.5), (0.5, -0.1), (-0.2, -0.45)],
               1: [(0.5, 0.25), (-0.15, 0.5), (-0.5, -0.15), (0.2, -0.45)]}
# A leaf whose middle comes this close to a bunch would cover it: it is left out (only those).
BUNCH_CLEARANCE = 0.2
# Leaves closer than this (between their middles) crowd into clumps: the later one is left out.
LEAF_SPACING = 0.3
# Which way the branches leave a crown (x, y, z; scaled to the crown's size).
BRANCH_DIRS = [(-1, 0, 0.15), (1, 0, 0.1), (-0.7, 0, 0.8), (0.7, 0, 0.8), (0, 0, 1), (-0.4, -0.9, 0.3),
               (0.4, 0.9, 0.3), (0.3, -0.8, -0.3), (-0.3, 0.8, -0.3), (-0.8, -0.3, -0.5), (0.8, 0.3, -0.5)]


def spline(points, steps):
    """Catmull-Rom curve through `points`."""
    pts = [Vector(p) for p in points]
    ext = [pts[0] * 2 - pts[1]] + pts + [pts[-1] * 2 - pts[-2]]
    out = []
    for i in range(1, len(ext) - 2):
        p0, p1, p2, p3 = ext[i - 1:i + 3]
        for k in range(steps):
            t = k / steps
            out.append(0.5 * (p1 * 2 + (p2 - p0) * t + (p0 * 2 - p1 * 5 + p2 * 4 - p3) * t * t +
                              (p1 * 3 - p0 - p2 * 3 + p3) * t ** 3))
    out.append(pts[-1])
    return out


def build_vines():
    stems, leaves = Part("Stems"), Part("Leaves")
    bunches, planned = [], []
    golden = math.pi * (3 - math.sqrt(5))
    for vine in VINES:
        points = [Vector(q) for q in vine["trunk"]]
        points[0].z = bed_height(bed_u(points[0].x, points[0].y)) - 0.08
        trunk = spline(points, 4)
        n = len(trunk)
        tube(stems, trunk, [0.08 - 0.035 * i / (n - 1) for i in range(n)], 7, COLORS["bark_foot"], COLORS["bark"],
             shade=0.3, wobble=0.14, twist=0.12)
        branches = [trunk]  # this vine's: its leaves and bunches are stalked to their nearest point
        centre, radii = Vector(vine["crown"][0]), Vector(vine["crown"][1])
        for d in BRANCH_DIRS:
            d = Vector(d).normalized() + Vector([rng.uniform(-0.15, 0.15) for _ in range(3)])
            start = trunk[-1 - rng.randrange(0, 4)]
            end = centre + Vector((d.x * radii.x, d.y * radii.y, d.z * radii.z)) * rng.uniform(0.75, 0.9)
            arch = Z * ((end - start).length * 0.25)
            path = bezier(start, start.lerp(end, 0.33) + arch, start.lerp(end, 0.66) + arch * 0.7, end, 6)
            tube(stems, path, [0.034 * (1 - 0.6 * i / 6) for i in range(7)], 6, COLORS["bark"], COLORS["bark_tip"],
                 shade=0.28, wobble=0.1, twist=0.08)
            branches.append(path)

        # Bunches first, so the leaves can leave them in view: in among the leaves all over the crown, on its front
        # and on its back, a little proud of the leaves so they show.
        shown = []  # each bunch's middle
        for side, spots in BUNCH_SPOTS.items():
            for u, v in spots:
                u, v = u + rng.uniform(-0.1, 0.1), v + rng.uniform(-0.1, 0.1)
                d = Vector((u, side * math.sqrt(max(0.15, 1 - u * u - v * v)), v)).normalized()
                size = rng.uniform(0.92, 1.1)
                middle = centre + Vector((d.x * radii.x, d.y * radii.y * rng.uniform(1.05, 1.18), d.z * radii.z))
                top = middle + Z * (0.18 * size)
                candidates = [q for path in branches for q in path if q.z > top.z + 0.04] or \
                    [q for path in branches for q in path]
                hook = min(candidates, key=lambda q: (q - top).length)
                tube(stems, bezier(hook, hook + Vector((0, side * 0.08, 0.03)), top + Z * 0.08, top, 5),
                     [0.016, 0.015, 0.014, 0.013, 0.012, 0.012], 5, COLORS["bark_tip"], COLORS["stalk"], cap_end=False)
                bunches.append((top, size))
                shown.append(middle)

        # Leaves spread evenly round the crown (a Fibonacci sphere, the very bottom left open), each facing out.
        count, start = vine["leaves"], rng.uniform(0, 2 * math.pi)
        for i in range(int(count * 1.3)):
            u = 1 - 2 * (i + 0.5) / (count * 1.3)
            if u < -0.7:
                continue
            ring = math.sqrt(1 - u * u)
            d = Vector((ring * math.cos(start + i * golden), ring * math.sin(start + i * golden), u))
            d = (d + Vector([rng.uniform(-0.15, 0.15) for _ in range(3)])).normalized()
            middle = centre + Vector((d.x * radii.x, d.y * radii.y, d.z * radii.z)) * rng.uniform(0.88, 1.08)
            if any((middle - c).length < BUNCH_CLEARANCE for c in shown):
                continue  # would cover a bunch
            outward = Vector((d.x / radii.x, d.y / radii.y, d.z / radii.z))
            planned.append((middle, outward, 0.56 * rng.uniform(0.9, 1.08), branches))
    # Thin out the clumps only: a leaf crowding one already placed is left out, the open parts keep theirs.
    placed = []
    for middle, outward, size, branches in planned:
        if all((middle - other).length >= LEAF_SPACING for other in placed):
            placed.append(middle)
            leaf_on(leaves, stems, middle, outward, size, branches)
    return stems, leaves, bunches, len(planned) - len(placed)


# ----------------------------------------------------------------------------------------------- assembly

def paint_material(image):
    """The strawberry plants' material: Paint x object-space noise (0.88..1.1) -> HSV (sat .95, val .92), with an
    image node as the bake target."""
    mat = bpy.data.materials.get("GV_Paint") or bpy.data.materials.new("GV_Paint")
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
    mat = bpy.data.materials.get("M_GrapeVine") or bpy.data.materials.new("M_GrapeVine")
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
    rng = random.Random(3)
    mat = paint_material(atlas_image())
    coll = bpy.data.collections.get("GrapeVine")
    if coll:
        for o in list(coll.objects):
            bpy.data.objects.remove(o, do_unlink=True)
    else:
        coll = bpy.data.collections.new("GrapeVine")
        bpy.context.scene.collection.children.link(coll)
    root = bpy.data.objects.new("GrapeVine_Root", None)
    coll.objects.link(root)
    ground, trellis = Part("Mound"), Part("Trellis")
    bed(ground, [(x, 0) for x in POSTS_X] + [v["trunk"][0][:2] for v in VINES])
    for x in POSTS_X:
        post(trellis, x)
    wires(trellis)
    stems, leaves, bunches, thinned = build_vines()
    for part in (ground, trellis, stems, leaves):
        to_object(part, coll, mat, root)
    for n, (top, size) in enumerate(sorted(bunches, key=lambda b: (b[0].x, -b[0].z)), 1):
        part = Part("Grape_%02d" % n)
        bunch(part, top, size)
        centre = sum((Vector(v) for v in part.verts), Vector()) / len(part.verts)
        to_object(part, coll, mat, root, origin=centre)
    for me in [m for m in bpy.data.meshes if m.users == 0]:
        bpy.data.meshes.remove(me)
    objs = [o for o in coll.objects if o.type == "MESH"]
    return {"bunches": len(bunches), "leaves_thinned": thinned, "faces": {o.name: len(o.data.polygons) for o in objs if "Grape_" not in o.name},
            "grape_faces": sum(len(o.data.polygons) for o in objs if "Grape_" in o.name),
            "total": sum(len(o.data.polygons) for o in objs)}


def _meshes():
    return [o for o in bpy.data.collections["GrapeVine"].objects if o.type == "MESH"]


def _select(objs):
    bpy.context.view_layer.update()
    for o in bpy.context.scene.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]


def bake():
    """Smart-project every piece into one UV atlas and bake the painted colour into T_GrapeVine_Albedo."""
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
    """FBX of the vine's meshes (axis conversion baked) and the baked atlas into Assets/Art/GrapeVine."""
    textures = os.path.join(OUT, "Textures")
    os.makedirs(textures, exist_ok=True)
    shutil.copyfile(os.path.join(HERE, ATLAS + ".png"), os.path.join(textures, ATLAS + ".png"))
    objs = _meshes()
    _select(objs)
    path = os.path.join(OUT, "GrapeVine.fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"}, global_scale=EXPORT_SCALE,
                             apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z",
                             axis_up="Y", bake_space_transform=True, mesh_smooth_type="FACE",
                             use_mesh_modifiers=True, bake_anim=False, path_mode="STRIP", embed_textures=False,
                             colors_type="NONE")
    return {"fbx": path, "objects": len(objs)}
