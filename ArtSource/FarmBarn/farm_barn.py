"""FarmBarn: the farm's main barn as a storybook "upgrade" of the old one (Assets/Art/Barn/LowPolyFarmBarn.fbx keeps
only its colours and mood), hand-painted like chicken_coop_handpainted (see barn_paint.py).

Design: a hall under a tall pointed-arch roof with flared eaves; the gable facing the path (+X) carries big arched
double doors, an open hayloft door under a hay hood with a hoist and a hanging bale; an octagonal tower with a
witch-hat roof and the rooster weathervane stands at the +Y side; an open lean-to shed with posts runs along -Y.

It is a place the chick walks into (upgrades, villager interactions), so:
- the floor sits at ground level and the doorway has a gentle ramp (the chick's CharacterController has no step);
- the middle of the hall stays clear; the villager's counter stands near the back gable, a loft over the entrance
  holds hay (seen through the hayloft door), lanterns hang from tie beams;
- Barn_Collision is a separate simple mesh for a MeshCollider on the CameraBlocker layer (renderer off in Unity).

Every board/stone/pane maps into T_FarmBarn_Atlas (16 painted strips): boards run U along their length in world
metres and V across their width; stones, panes and leaves take one whole painted cell per face.
Units are metres, Z up; the doors face +X, the origin is the hall's footprint centre on the ground.
Children: Barn_Door_Left / Barn_Door_Right (origins on their hinges, swing about Z), Barn_Weathervane (pivot at the
foot of its post), Barn_Props, Barn_Collision.
Run inside Blender: exec(code, {"__file__": path}); build(); export().
"""
import importlib
import math
import os
import random
import shutil
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector
from mathutils.geometry import tessellate_polygon

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.abspath(os.path.join(HERE, "..", "..", "Assets", "Art", "FarmBarn"))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import barn_paint  # noqa: E402

importlib.reload(barn_paint)

KIND = {name: kind for name, kind, _ in barn_paint.STRIPS}
VARIANTS = {"red": ("red_a", "red_a", "red_b"), "roof": ("roof_a", "roof_b")}
TILE = 4.0  # metres of board length per texture repeat

# ----------------------------------------------------------------------------------------------- layout

HX, HY = 4.2, 3.6          # hall: gables at x = +-HX, long walls at y = +-HY
FLOOR_Z = 0.06
EAVE_Z = 3.4
DW, DS = 1.6, 2.3          # big door: half width, height where the arch springs (top = DS + DW)
LOFT_Y = 0.533             # hayloft opening half width (= one door board)
LOFT_Z0, LOFT_Z1 = 5.0, 6.5
FRONT_LOFT_X, FRONT_LOFT_Z = HX - 2.4, 4.5
TOWER_C, TOWER_A, TOWER_TOP = Vector((1.3, HY + 1.9, 0.0)), 1.75, 6.6
SHED_Y, SHED_Z0, SHED_Z1 = -HY - 3.9, 3.25, 2.25   # lean-to: outer post line, roof height at the wall / at the posts
ROOF_FRONT, ROOF_BACK = HX + 0.9, -HX - 0.6
RIGHT = [(HY + 0.75, 2.85), (HY, 3.4), (3.35, 4.5), (2.9, 5.7), (2.2, 7.0), (1.2, 8.15), (0.0, 8.9)]
LEFT = [(-HY - 0.15, 3.3), (-3.35, 4.5), (-2.9, 5.7), (-2.2, 7.0), (-1.2, 8.15), (0.0, 8.9)]

rng = random.Random(5)
X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))
AXES = (X, Y, Z)


def _catmull(points, steps=12):
    pts = [Vector((0, y, z)) for y, z in points]
    ext = [pts[0] * 2 - pts[1]] + pts + [pts[-1] * 2 - pts[-2]]
    out = []
    for i in range(1, len(ext) - 2):
        p0, p1, p2, p3 = ext[i - 1], ext[i], ext[i + 1], ext[i + 2]
        for k in range(steps):
            t = k / steps
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t
                              + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3))
    out.append(pts[-1])
    return out


class Curve:
    """Dense polyline in the YZ plane with arc-length lookups."""

    def __init__(self, points):
        self.pts = _catmull(points) if len(points) > 2 else [Vector((0, y, z)) for y, z in points]
        self.acc = [0.0]
        for a, b in zip(self.pts, self.pts[1:]):
            self.acc.append(self.acc[-1] + (b - a).length)
        self.length = self.acc[-1]

    def at(self, s):
        s = min(max(s, 0.0), self.length)
        for i in range(len(self.acc) - 1):
            if self.acc[i + 1] >= s:
                a, b = self.pts[i], self.pts[i + 1]
                t = (s - self.acc[i]) / max(self.acc[i + 1] - self.acc[i], 1e-9)
                return a.lerp(b, t), (b - a).normalized()
        return self.pts[-1], (self.pts[-1] - self.pts[-2]).normalized()

    def z_at_y(self, y):
        for a, b in zip(self.pts, self.pts[1:]):
            if min(a.y, b.y) - 1e-9 <= y <= max(a.y, b.y) + 1e-9 and abs(b.y - a.y) > 1e-9:
                return a.z + (b.z - a.z) * (y - a.y) / (b.y - a.y)
        return self.pts[0].z


CURVE_R, CURVE_L = Curve(RIGHT), Curve(LEFT)
CURVE_SHED = Curve([(SHED_Y - 0.45, SHED_Z1 - 0.12), (-HY - 0.12, SHED_Z0)])


def roof_z(y):
    return CURVE_R.z_at_y(y) if y >= 0 else CURVE_L.z_at_y(y)


def door_top(y):
    """Height of the big door's arch at y, None outside the opening."""
    return DS + math.sqrt(DW * DW - y * y) if abs(y) < DW - 1e-6 else None


# ----------------------------------------------------------------------------------------------- mesh helpers

def pick(name):
    return rng.choice(VARIANTS[name]) if name in VARIANTS else name


def is_cell(strip):
    return KIND[strip] in ("cells", "glass", "glow")


class Part:
    def __init__(self, name):
        self.name = name
        self.verts, self.faces, self.uvs = [], [], []

    def add(self, pts, faces, uvs):
        base = len(self.verts)
        self.verts += [tuple(p) for p in pts]
        for f, uv in zip(faces, uvs):
            self.faces.append([base + i for i in f])
            self.uvs.append(uv)


BOX_FACES = (((0, 3, 2, 1), "big"), ((4, 5, 6, 7), "big"), ((0, 1, 5, 4), "vlow"),
             ((3, 7, 6, 2), "vhigh"), ((0, 4, 7, 3), "uend"), ((1, 2, 6, 5), "uend"))


def board(part, o, U, N, length, width, thick, strip, length1=None, start1=0.0, lap=0.0, tilt=1.0, wob=0.02):
    """A painted board: U along its length, V = N x U across its width, N out of its face.
    The V=0 edge runs u 0..length, the V=width edge runs u start1..length1 (slanted ends);
    `lap` pushes the V=0 edge out along N; `tilt` and `wob` give the hand-built wobble."""
    U, N = Vector(U).normalized(), Vector(N).normalized()
    V = N.cross(U)
    o = Vector(o)
    l1 = length if length1 is None else length1
    local = [(0, 0, 0), (length, 0, 0), (l1, width, 0), (start1, width, 0)]
    local += [(u, v, thick) for u, v, _ in local]
    pts = [o + U * u + V * v + N * n for u, v, n in local]
    if lap:
        for i in (0, 1, 4, 5):
            pts[i] = pts[i] + N * lap
    if tilt:
        c = sum(pts, Vector()) / 8
        R = (Matrix.Rotation(rng.gauss(0, 0.016 * tilt), 3, N) @ Matrix.Rotation(rng.gauss(0, 0.012 * tilt), 3, U)
             @ Matrix.Rotation(rng.gauss(0, 0.008 * tilt), 3, V))
        pts = [c + R @ (p - c) for p in pts]
    if wob:
        pts = [p + Vector((rng.uniform(-wob, wob), rng.uniform(-wob, wob), rng.uniform(-wob, wob))) for p in pts]
    s = pick(strip)
    si = barn_paint.INDEX[s]
    uoff = rng.random()
    span_u = max(length, l1) - min(0.0, start1)
    uvs = []
    for idx, kind in BOX_FACES:
        cell = rng.randrange(barn_paint.CELLS)
        loop = []
        for i in idx:
            u, v, n = local[i]
            if is_cell(s):
                if kind == "big":
                    a, b = u / max(span_u, 1e-6), v / width
                elif kind in ("vlow", "vhigh"):
                    a, b = u / max(span_u, 1e-6), n / thick
                else:
                    a, b = n / thick, v / width
                loop.append((barn_paint.cell_u(cell, a), barn_paint.strip_v(si, b)))
            elif kind == "big":
                loop.append((uoff + u / TILE, barn_paint.strip_v(si, v / width)))
            elif kind == "vlow":
                loop.append((uoff + u / TILE, barn_paint.strip_v(si, 0.12 + 0.1 * n / thick)))
            elif kind == "vhigh":
                loop.append((uoff + u / TILE, barn_paint.strip_v(si, 0.78 + 0.1 * n / thick)))
            else:
                loop.append((uoff + (u + n) / TILE, barn_paint.strip_v(si, v / width)))
        uvs.append(loop)
    part.add(pts, [f for f, _ in BOX_FACES], uvs)


def box(part, mn, mx, strip, tilt=1.0, wob=0.02):
    """Axis-aligned board filling [mn, mx]: grain along the longest side, face on the thinnest."""
    mn, mx = [min(a, b) for a, b in zip(mn, mx)], [max(a, b) for a, b in zip(mn, mx)]
    size = [mx[i] - mn[i] for i in range(3)]
    order = sorted(range(3), key=lambda i: size[i])
    iu, iv, inn = order[2], order[1], order[0]
    U, N = AXES[iu], AXES[inn]
    V = N.cross(U)
    o = [0.0, 0.0, 0.0]
    o[iu], o[inn] = mn[iu], mn[inn]
    o[iv] = mn[iv] if V[iv] > 0 else mx[iv]
    board(part, o, U, N, size[iu], size[iv], size[inn], strip, tilt=tilt, wob=wob)


def box_c(part, center, half, strip, tilt=1.0, wob=0.02):
    c, h = Vector(center), Vector(half)
    box(part, tuple(c - h), tuple(c + h), strip, tilt, wob)


def rect(part, center, T, N, w, h, thick, strip, tilt=0.6, wob=0.012):
    """Board of w (along T) x h centred on `center`, standing out of its plane along N by `thick`."""
    U, N = Vector(T).normalized(), Vector(N).normalized()
    V = N.cross(U)
    if V.z < -1e-6:
        U = -U
        V = N.cross(U)
    board(part, Vector(center) - U * (w / 2) - V * (h / 2), U, N, w, h, thick, strip, tilt=tilt, wob=wob)


def bar(part, a, b, width, thick, N, strip, tilt=0.4, wob=0.01):
    """Board from point a to point b, `width` wide across, standing out along N."""
    a, b, N = Vector(a), Vector(b), Vector(N).normalized()
    U = (b - a).normalized()
    V = N.cross(U)
    board(part, a - V * (width / 2), U, N, (b - a).length, width, thick, strip, tilt=tilt, wob=wob)


def planar(part, verts, faces, strip):
    """Free-form faces, each planar-mapped into the strip (whole cells for cell strips)."""
    uvs = []
    uoff = rng.random()
    for f in faces:
        s = pick(strip)
        si = barn_paint.INDEX[s]
        pts = [Vector(verts[i]) for i in f]
        n = (pts[1] - pts[0]).cross(pts[2] - pts[0])
        n = n.normalized() if n.length > 1e-9 else Z
        uf = Z.cross(n) if abs(n.z) < 0.9 else X.copy()
        uf.normalize()
        vf = n.cross(uf)
        us, vs = [p.dot(uf) for p in pts], [p.dot(vf) for p in pts]
        ulo, vlo = min(us), min(vs)
        ur, vr = max(max(us) - ulo, 1e-6), max(max(vs) - vlo, 1e-6)
        if is_cell(s):
            cell = rng.randrange(barn_paint.CELLS)
            uvs.append([(barn_paint.cell_u(cell, (pu - ulo) / ur), barn_paint.strip_v(si, (pv - vlo) / vr))
                        for pu, pv in zip(us, vs)])
        else:
            uvs.append([(uoff + pu / TILE, barn_paint.strip_v(si, 0.1 + 0.8 * (pv - vlo) / vr))
                        for pu, pv in zip(us, vs)])
    part.add([Vector(v) for v in verts], faces, uvs)


def _clean(verts, faces):
    """Consistent outward normals for a closed piece."""
    bm = bmesh.new()
    bv = [bm.verts.new(v) for v in verts]
    for f in faces:
        bm.faces.new([bv[i] for i in f])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.verts.index_update()
    out = ([tuple(v.co) for v in bm.verts], [[v.index for v in f.verts] for f in bm.faces])
    bm.free()
    return out


def prism(part, poly, a_axis, b_axis, depth_axis, d0, d1, strip):
    """Extrudes a 2D polygon (coordinates along a_axis, b_axis) from d0 to d1 along depth_axis."""
    a_axis, b_axis, depth_axis = Vector(a_axis), Vector(b_axis), Vector(depth_axis)
    n = len(poly)
    verts = [a_axis * a + b_axis * b + depth_axis * d0 for a, b in poly]
    verts += [a_axis * a + b_axis * b + depth_axis * d1 for a, b in poly]
    faces = []
    for tri in tessellate_polygon([[Vector((a, b, 0)) for a, b in poly]]):  # ear clipping copes with concave caps
        faces.append(tuple(tri))
        faces.append(tuple(n + i for i in reversed(tri)))
    for i in range(n):
        j = (i + 1) % n
        faces.append((i, j, n + j, n + i))
    planar(part, *_clean(verts, faces), strip)


def blob(part, center, radii, strip, subdiv=2, lumpy=0.12, flat_bottom=True):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    for v in bm.verts:
        d = v.co.copy()
        j = 1 + rng.uniform(-lumpy, lumpy)
        z = max(d.z, -0.15) if flat_bottom else d.z
        v.co = Vector(center) + Vector((d.x * radii[0] * j, d.y * radii[1] * j, z * radii[2] * j))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.verts.index_update()
    verts = [tuple(v.co) for v in bm.verts]
    faces = [[v.index for v in f.verts] for f in bm.faces]
    bm.free()
    planar(part, verts, faces, strip)


def split_lengths(total, lo=2.5, hi=4.5):
    cuts, s = [], 0.0
    while total - s > hi:
        step = rng.uniform(lo, hi)
        cuts.append((s, s + step))
        s += step
    cuts.append((s, total))
    return cuts


# ----------------------------------------------------------------------------------------------- hall shell

def gable_intervals():
    """Board edges across a gable: door-width boards in the middle, wider ones outside."""
    edges = [-HY + k * (HY - DW) / 3 for k in range(3)] + [-DW + k * DW / 3 for k in range(6)]
    edges += [DW + k * (HY - DW) / 3 for k in range(4)]
    return list(zip(edges, edges[1:]))


def gable_board(part, x, N, y0, y1, zb0, zb1, zt0, zt1, strip, thick, tilt, wob):
    """Vertical board between y0..y1 (bottom zb, top zt at each edge) on the plane x, facing N."""
    V = N.cross(Z)
    e0 = y1 if V.y < 0 else y0
    b0, b1 = (zb1, zb0) if V.y < 0 else (zb0, zb1)
    t0, t1 = (zt1, zt0) if V.y < 0 else (zt0, zt1)
    board(part, Vector((x, e0, b0)), Z, N, t0 - b0, abs(y1 - y0) + 0.02, thick, strip,
          length1=t1 - b0, start1=b1 - b0, tilt=tilt, wob=wob)


def gables(body, inside=False):
    """Front (+X, door and hayloft openings) and back gables: exterior red boards or the interior lining."""
    for side in (1, -1):
        N = Vector((side, 0, 0)) * (-1 if inside else 1)
        x = side * (HX - (0.02 if inside else 0.0))
        thick = 0.05 if inside else 0.12
        strip = "lining" if inside else "red"
        tilt, wob = (0.0, 0.004) if inside else (0.7, 0.02)
        for y0, y1 in gable_intervals():
            top0, top1 = roof_z(y0) + 0.08, roof_z(y1) + 0.08
            bot0 = bot1 = FLOOR_Z - (0.0 if inside else 0.1)
            mid = (y0 + y1) / 2
            if side == 1 and abs(mid) < DW:
                bot0, bot1 = door_top(y0) or DS, door_top(y1) or DS
            if side == 1 and abs(mid) < LOFT_Y:
                gable_board(body, x, N, y0, y1, bot0, bot1, LOFT_Z0, LOFT_Z0, strip, thick, tilt, wob)
                gable_board(body, x, N, y0, y1, LOFT_Z1, LOFT_Z1, top0, top1, strip, thick, tilt, wob)
                continue
            gable_board(body, x, N, y0, y1, bot0, bot1, top0, top1, strip, thick, tilt, wob)


def long_walls(body):
    """Lapped horizontal red boards outside, a flat light lining inside."""
    for side in (-1, 1):
        U, N = Vector((-side, 0, 0)), Vector((0, side, 0))
        start, total = side * (HX + 0.12), 2 * (HX + 0.12)
        z = FLOOR_Z - 0.1
        while z < EAVE_Z + 0.05:
            w = min(0.6, EAVE_Z + 0.12 - z)
            for c0, c1 in split_lengths(total, 3.0, 5.0):
                board(body, Vector((start, side * HY, z)) + U * c0, U, N, c1 - c0, w, 0.12, "red", lap=0.05,
                      tilt=0.7)
            z += 0.5
        Ui, Ni = Vector((side, 0, 0)), Vector((0, -side, 0))
        z = FLOOR_Z
        while z < EAVE_Z + 0.1:
            w = min(0.62, EAVE_Z + 0.12 - z)
            board(body, Vector((-side * (HX - 0.07), side * (HY - 0.02), z)), Ui, Ni, 2 * (HX - 0.07), w, 0.05,
                  "lining", tilt=0, wob=0.004)
            z += 0.62


def roof(body):
    thick, width, step = 0.17, 0.66, 0.56
    reach = TOWER_A / math.cos(math.radians(22.5)) + 0.1
    tower_x = (TOWER_C.x - reach, TOWER_C.x + reach)
    for side, curve in ((-1, CURVE_L), (1, CURVE_R)):
        U = Vector((-side, 0, 0))
        x_start, x_end = (ROOF_BACK, ROOF_FRONT) if side == -1 else (ROOF_FRONT, ROOF_BACK)
        total = abs(x_end - x_start)
        s = 0.0
        while s < curve.length - 0.2:
            p, t = curve.at(s)
            N = U.cross(t)
            w = min(width, curve.length - s + 0.08)
            spans = [(0.0, total)]
            if side == 1 and p.y > HY + 0.1:  # the eave rows outside the wall stop at the tower
                spans = [(0.0, x_start - tower_x[1]), (x_start - tower_x[0], total)]
            for a, b in spans:
                for c0, c1 in split_lengths(b - a, 3.5, 6.0):
                    c0, c1 = a + c0, a + c1
                    rag0 = rng.uniform(-0.2, 0.2) if c0 == 0 else 0.0
                    rag1 = rng.uniform(-0.2, 0.2) if c1 == total else 0.0
                    o = Vector((x_start, p.y, p.z)) + U * (c0 - rag0)
                    board(body, o, U, N, (c1 + rag1) - (c0 - rag0), w, thick, "roof", lap=0.1, tilt=1.4, wob=0.025)
            s += step
    cap = [(-0.5, 8.62), (0.5, 8.62), (0.2, 9.35), (-0.2, 9.35)]
    prism(body, cap, Y, Z, X, ROOF_BACK - 0.15, ROOF_FRONT + 0.15, "dark")
    # Dark board over the joint between the hall wall and the lean-to roof.
    box(body, (ROOF_BACK, -HY - 0.3, SHED_Z0 - 0.05), (ROOF_FRONT, -HY - 0.12, SHED_Z0 + 0.3), "dark", tilt=0.5)


def barge_boards(body):
    """Cream boards under the roof edge along both gable arches."""
    for gx in (-1, 1):
        N = Vector((gx, 0, 0))
        x = gx * (HX + 0.16)
        for curve in (CURVE_L, CURVE_R):
            s = 0.0
            while s < curve.length - 0.05:
                ln = min(0.9, curve.length - s)
                a, _ = curve.at(s)
                b, _ = curve.at(s + ln)
                A, B = Vector((x, a.y, a.z)), Vector((x, b.y, b.z))
                Ub = (B - A).normalized()
                V = N.cross(Ub)
                o = A - V * 0.3 if V.z > 0 else A
                board(body, o - Ub * 0.05, Ub, N, (B - A).length + 0.1, 0.3, 0.1, "cream", tilt=0.4, wob=0.01)
                s += ln


def corner_posts(body):
    for sx in (-1, 1):
        for sy in (-1, 1):
            cx, cy = sx * (HX + 0.05), sy * (HY + 0.05)
            box(body, (cx - 0.24, cy - 0.24, -0.05), (cx + 0.24, cy + 0.24, EAVE_Z + 0.05), "dark", tilt=1.4,
                wob=0.03)


def floor(body):
    y = -HY
    while y < HY - 1e-3:
        w = min(0.62, HY - y)
        for c0, c1 in split_lengths(2 * HX, 2.5, 4.0):
            board(body, (-HX + c0, y, FLOOR_Z - 0.16), X, Z, c1 - c0, w - 0.02, 0.16, "floor", tilt=0, wob=0)
        y += 0.62
    # Ramp from the threshold down to the ground, wider than the doorway.
    y0, y1 = -DW - 0.3, DW + 0.3
    x0, x1 = HX - 0.02, HX + 0.85
    verts = [(x0, y0, FLOOR_Z), (x0, y1, FLOOR_Z), (x1, y1, -0.03), (x1, y0, -0.03),
             (x0, y0, -0.12), (x0, y1, -0.12), (x1, y1, -0.12), (x1, y0, -0.12)]
    faces = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 4, 7, 3), (1, 2, 6, 5), (0, 1, 5, 4), (3, 7, 6, 2)]
    planar(body, *_clean(verts, faces), "floor")


def footing(body):
    """Painted stones along the outside of the walls, leaving the doorway open."""
    def stone_run(a, b, at, along_y, skip=None):
        s = a
        while s < b - 0.25:
            ln = min(rng.uniform(0.6, 1.0), b - s)
            if skip and s + ln > skip[0] and s < skip[1]:
                s = skip[1] + 0.05
                continue
            r_t, r_n, r_z = ln / 2, rng.uniform(0.2, 0.26), rng.uniform(0.28, 0.38)
            blob(body, at(s + ln / 2), (r_n, r_t, r_z) if along_y else (r_t, r_n, r_z), "stone", subdiv=1,
                 lumpy=0.1)
            s += ln - 0.06
    for sy in (-1, 1):
        stone_run(-HX, HX, lambda t, sy=sy: (t, sy * (HY + 0.2), -0.05), False)
    stone_run(-HY, HY, lambda t: (HX + 0.2, t, -0.05), True, skip=(-DW - 0.5, DW + 0.5))
    stone_run(-HY, HY, lambda t: (-HX - 0.2, t, -0.05), True)


# ----------------------------------------------------------------------------------------------- openings

def window_at(part, c, T, N, w, h, shutters=True, flowers=False):
    """Painted glass, cream frame and cross, sill, dark shutters; c sits on the outside of the boards."""
    c, T, N = Vector(c), Vector(T).normalized(), Vector(N).normalized()
    rect(part, c + N * 0.02, T, N, w, h, 0.04, "glass", tilt=0, wob=0)
    t = 0.16
    rect(part, c + Z * (h / 2 + t / 2), T, N, w + 2 * t, t, 0.12, "cream")
    rect(part, c - Z * (h / 2 + t / 2), T, N, w + 2 * t, t, 0.12, "cream")
    rect(part, c + T * (w / 2 + t / 2), T, N, t, h, 0.12, "cream")
    rect(part, c - T * (w / 2 + t / 2), T, N, t, h, 0.12, "cream")
    rect(part, c + N * 0.02, T, N, 0.09, h, 0.08, "cream", tilt=0.3)
    rect(part, c + N * 0.02 + Z * (h * 0.08), T, N, w, 0.09, 0.08, "cream", tilt=0.3)
    sill = c - Z * (h / 2 + t + 0.06)
    rect(part, sill, T, N, w + 2 * t + 0.3, 0.12, 0.28, "cream", tilt=0.8)
    if shutters:
        for s in (-1, 1):
            sc = c + T * s * (w / 2 + t + 0.3)
            rect(part, sc, T, N, 0.52, h + 0.2, 0.07, "dark", tilt=1.2, wob=0.015)
            rect(part, sc + N * 0.07 + Z * (h * 0.25), T, N, 0.5, 0.1, 0.04, "cream", tilt=0.6)
            rect(part, sc + N * 0.07 - Z * (h * 0.25), T, N, 0.5, 0.1, 0.04, "cream", tilt=0.6)
    if flowers:
        bc = sill - Z * 0.24
        rect(part, bc, T, N, w + 0.4, 0.34, 0.42, "dark", tilt=0.8)
        for k in range(4):
            p = bc + T * ((k - 1.5) * (w + 0.2) / 4) + N * 0.24 + Z * 0.16
            blob(part, p, (0.2, 0.2, 0.17), "leaf", subdiv=1, lumpy=0.2, flat_bottom=False)
            box_c(part, p + Z * 0.16 + T * rng.uniform(-0.07, 0.07), (0.06, 0.06, 0.06),
                  "red_b" if k % 2 else "hay", tilt=3, wob=0.01)


def round_window(part, c, T, N, r=0.55):
    """Ten-sided cream frame around painted glass, with a cross."""
    c, T, N = Vector(c), Vector(T).normalized(), Vector(N).normalized()
    n = 10
    base = c.dot(N)
    plane = c - N * base
    poly = [((plane + T * (r * math.cos(2 * math.pi * i / n)) + Z * (r * math.sin(2 * math.pi * i / n))).dot(T),
             (plane + Z * (r * math.sin(2 * math.pi * i / n))).dot(Z)) for i in range(n)]
    prism(part, poly, T, Z, N, base + 0.02, base + 0.06, "glass")
    ro = r + 0.09
    for i in range(n):
        a0, a1 = 2 * math.pi * i / n, 2 * math.pi * (i + 1) / n
        p0 = c + T * (ro * math.cos(a0)) + Z * (ro * math.sin(a0))
        p1 = c + T * (ro * math.cos(a1)) + Z * (ro * math.sin(a1))
        bar(part, p0 + (p0 - p1).normalized() * 0.05, p1 + (p1 - p0).normalized() * 0.05, 0.22, 0.12, N, "cream",
            tilt=0.3)
    rect(part, c + N * 0.04, T, N, 0.09, 2 * r, 0.08, "cream", tilt=0.2)
    rect(part, c + N * 0.04, T, N, 2 * r, 0.09, 0.08, "cream", tilt=0.2)


def leaf(part, o, along, N, width, height_at, glazed=True, boards=3, trim="cream", strip="red_b"):
    """Door leaf: vertical boards (top at height_at(distance from the hinge)), a frame, an X brace in the lower
    half and (glazed) a small window above. o: bottom corner at the hinge (inner face); along: across the leaf."""
    along, N = Vector(along).normalized(), Vector(N).normalized()
    V = N.cross(Z)
    bw = width / boards
    thick = 0.1
    for i in range(boards):
        d0, d1 = i * bw, (i + 1) * bw - 0.015
        if V.dot(along) < 0:
            start, h0, h1 = o + along * d1, height_at(d1), height_at(d0)
        else:
            start, h0, h1 = o + along * d0, height_at(d0), height_at(d1)
        board(part, start, Z, N, h0, d1 - d0, thick, strip, length1=h1, tilt=0.4, wob=0.012)
    fw, ft = 0.18, 0.06
    base = o + N * thick
    hmin = min(height_at(0.0), height_at(width))
    mid = hmin * 0.5
    for z in (fw / 2, mid):
        bar(part, base + Z * z, base + along * width + Z * z, fw, ft, N, trim)
    for d in (fw / 2, width - fw / 2):
        bar(part, base + along * d, base + along * d + Z * (height_at(d) - 0.05), fw, ft, N, trim)
    bl, br = base + along * fw + Z * fw, base + along * (width - fw) + Z * fw
    tl, tr = base + along * fw + Z * (mid - fw / 2), base + along * (width - fw) + Z * (mid - fw / 2)
    bar(part, bl, tr, fw * 0.85, ft, N, trim)
    bar(part, tl + N * 0.02, br + N * 0.02, fw * 0.85, ft, N, trim)
    if glazed:
        c = base + along * (width / 2) + Z * (mid + (hmin - mid) * 0.5)
        pw, ph = width - 2 * fw - 0.16, (hmin - mid) * 0.55
        rect(part, c, along, N, pw, ph, 0.03, "glass", tilt=0, wob=0)
        rect(part, c + N * 0.02, along, N, 0.07, ph, 0.04, trim, tilt=0.2)
        rect(part, c + N * 0.02, along, N, pw, 0.07, 0.04, trim, tilt=0.2)
        rect(part, c + Z * (ph / 2 + 0.05), along, N, pw + 0.1, 0.1, 0.06, trim, tilt=0.3)
        rect(part, c - Z * (ph / 2 + 0.05), along, N, pw + 0.1, 0.1, 0.06, trim, tilt=0.3)


def hay_bale(part, center, yaw, size=(1.2, 0.66, 0.56)):
    U = Vector((math.cos(yaw), math.sin(yaw), 0))
    V = Z.cross(U)
    l, w, h = size
    o = Vector(center) - U * (l / 2) - V * (w / 2) - Z * (h / 2)
    board(part, o, U, Z, l, w, h, "hay", tilt=0.8, wob=0.03)
    for f in (-0.27, 0.27):
        so = Vector(center) + U * (f * l - 0.04) - V * (w / 2 + 0.02) - Z * (h / 2 + 0.02)
        board(part, so, U, Z, 0.08, w + 0.04, h + 0.04, "dark", tilt=0.3, wob=0.01)


def front_details(body):
    """Arched door trim with a keystone, the hayloft frame and its open doors, the hay hood with hoist and bale."""
    x = HX + 0.12
    for s in (-1, 1):
        bar(body, (x, s * (DW + 0.12), FLOOR_Z), (x, s * (DW + 0.12), DS + 0.02), 0.26, 0.14, X, "cream", tilt=0.6)
    n, r = 9, DW + 0.12
    for i in range(n):
        a0, a1 = math.pi * i / n, math.pi * (i + 1) / n
        p0 = Vector((x, r * math.cos(a0), DS + r * math.sin(a0)))
        p1 = Vector((x, r * math.cos(a1), DS + r * math.sin(a1)))
        bar(body, p0 + (p0 - p1).normalized() * 0.06, p1 + (p1 - p0).normalized() * 0.06, 0.26, 0.14, X, "cream",
            tilt=0.3)
    box_c(body, (x + 0.1, 0, DS + r + 0.05), (0.08, 0.2, 0.24), "dark", tilt=1.0)
    # Hayloft frame and its two doors, swung open.
    for s in (-1, 1):
        bar(body, (x, s * (LOFT_Y + 0.08), LOFT_Z0 - 0.05), (x, s * (LOFT_Y + 0.08), LOFT_Z1 + 0.05), 0.18, 0.12,
            X, "cream")
        leaf(body, Vector((x + 0.02, s * (LOFT_Y + 0.17), LOFT_Z0)), X, Vector((0, s, 0)), LOFT_Y * 1.05,
             lambda d: LOFT_Z1 - LOFT_Z0, glazed=False, boards=2)
    bar(body, (x, -LOFT_Y - 0.2, LOFT_Z1 + 0.1), (x, LOFT_Y + 0.2, LOFT_Z1 + 0.1), 0.2, 0.12, X, "cream")
    bar(body, (x, -LOFT_Y - 0.25, LOFT_Z0 - 0.1), (x, LOFT_Y + 0.25, LOFT_Z0 - 0.1), 0.2, 0.2, X, "cream")
    blob(body, (HX + 0.05, 0.0, LOFT_Z0 + 0.05), (0.35, 0.45, 0.22), "hay", subdiv=2, lumpy=0.2)
    # Hay hood: a little pointed roof sticking out over the hoist.
    hood_x0, hood_x1 = HX + 0.5, HX + 2.1
    peak = Vector((0, 0, 8.95))
    for s in (-1, 1):
        B = Vector((0, s * 0.95, 8.15))
        up = (peak - B).normalized()
        U = X if s == -1 else -X
        Nn = U.cross(up)
        for k in range(2):
            o = Vector((hood_x0 if U.x > 0 else hood_x1, B.y, B.z)) + up * (k * 0.5)
            board(body, o, U, Nn, hood_x1 - hood_x0, 0.56, 0.12, "roof", lap=0.06, tilt=1.0)
        bar(body, (hood_x1 + 0.02, B.y * 1.05, B.z - 0.05), (hood_x1 + 0.02, 0, 8.9), 0.2, 0.08, X, "cream", tilt=0.3)
    beam_z = 7.75
    box(body, (HX - 0.3, -0.13, beam_z - 0.14), (hood_x1 - 0.1, 0.13, beam_z + 0.14), "dark", tilt=0.6)
    px = hood_x1 - 0.35
    wheel = [(px + 0.2 * math.cos(2 * math.pi * i / 8), beam_z - 0.32 + 0.2 * math.sin(2 * math.pi * i / 8))
             for i in range(8)]
    prism(body, wheel, X, Z, Y, -0.06, 0.06, "metal")
    box(body, (px + 0.17, -0.025, 5.75), (px + 0.22, 0.025, beam_z - 0.35), "sack", tilt=0, wob=0)
    hay_bale(body, Vector((px + 0.2, 0.0, 5.45)), math.radians(90), size=(0.9, 0.55, 0.5))


# ----------------------------------------------------------------------------------------------- tower

def tower(body):
    """Octagonal tower against the +Y wall; returns the foot of the weathervane post at its tip."""
    a, C = TOWER_A, TOWER_C
    wf = 2 * a * math.tan(math.radians(22.5))
    R = a / math.cos(math.radians(22.5))
    faces = []
    for i in range(8):
        phi = math.radians(45 * i)
        n = Vector((math.cos(phi), math.sin(phi), 0))
        faces.append((i, n, Z.cross(n)))
    for i, n, T in faces:
        if i == 6:  # this face sits against the hall wall
            continue
        fc = C + n * a
        V = n.cross(Z)
        edge = fc - V * (wf / 2)
        for k in range(2):
            board(body, edge + V * (k * wf / 2) - Z * 0.04, Z, n, TOWER_TOP + 0.12, wf / 2 + 0.03, 0.12, "red",
                  tilt=0.6)
        for z in (3.25, TOWER_TOP - 0.1):
            rect(body, fc + n * 0.12 + Z * z, T, n, wf + 0.12, 0.24, 0.1, "cream", tilt=0.4)
    for k in range(8):
        if k in (5, 6):  # the corners on the hall wall
            continue
        d = Vector((math.cos(math.radians(22.5 + 45 * k)), math.sin(math.radians(22.5 + 45 * k)), 0))
        V = d.cross(Z)
        board(body, C + d * (R - 0.08) - V * 0.14 - Z * 0.04, Z, d, TOWER_TOP + 0.1, 0.28, 0.16, "dark", tilt=1.0,
              wob=0.02)
    for i, n, T in faces:
        if i in (0, 1, 2):
            window_at(body, C + n * (a + 0.12) + Z * 4.7, T, n, 0.55, 0.85, shutters=False)
    n0, T0 = faces[0][1], faces[0][2]
    door_c = C + n0 * (a + 0.12)
    leaf(body, door_c - T0 * 0.45, T0, n0, 0.9, lambda d: 1.55 + 0.25 * math.sin(math.pi * d / 0.9), glazed=False,
         boards=2, strip="red_a")
    for s in (-1, 1):
        rect(body, door_c + T0 * s * 0.55 + Z * 0.85, T0, n0, 0.16, 1.7, 0.12, "cream")
    rect(body, door_c + Z * 1.92, T0, n0, 1.26, 0.18, 0.12, "cream")
    # Witch-hat roof: lapped shingle tiers on a concave profile whose tip bends back a little.
    profile = [(2.45, 6.5), (1.95, 7.2), (1.42, 8.05), (0.95, 9.0), (0.55, 9.95), (0.25, 10.85), (0.06, 11.6)]
    rings = []
    for r, z in profile:
        bend = ((z - 6.5) / 5.1) ** 2 * 0.45
        rings.append([C + Vector((r * math.cos(math.radians(22.5 + 45 * k)) - bend,
                                  r * math.sin(math.radians(22.5 + 45 * k)), z)) for k in range(8)])
    flat = Vector((1, 1, 0))
    for t in range(len(rings) - 1):
        lo, hi = rings[t], rings[t + 1]
        for k in range(8):
            j = (k + 1) % 8
            a0 = lo[k] + ((lo[k] - C) * flat).normalized() * 0.12 - Z * 0.08
            a1 = lo[j] + ((lo[j] - C) * flat).normalized() * 0.12 - Z * 0.08
            si = barn_paint.INDEX[pick("roof")]
            u0, span = rng.random(), (a1 - a0).length
            uv = [(u0, barn_paint.strip_v(si, 0.0)), (u0 + span / TILE, barn_paint.strip_v(si, 0.0)),
                  (u0 + span / TILE, barn_paint.strip_v(si, 1.0)), (u0, barn_paint.strip_v(si, 1.0))]
            lip = [a0 - Z * 0.06, a1 - Z * 0.06]
            # The tier's outer face plus a little lip underneath, so every tier reads as a thick shingle row.
            body.add([a0, a1, hi[j], hi[k], *lip], [(0, 1, 2, 3), (4, 5, 1, 0)], [uv, [uv[0], uv[1], uv[1], uv[0]]])
    base = [p + (p - C) * flat * (0.12 / 2.45) - Z * 0.14 for p in rings[0]]
    dark_v = barn_paint.strip_v(barn_paint.INDEX["dark"], 0.5)
    body.add(base, [tuple(reversed(range(8)))], [[(0.5, dark_v)] * 8])
    tip = sum(rings[-1], Vector()) / 8
    box_c(body, tip + Z * 0.1, (0.12, 0.12, 0.14), "dark", tilt=0, wob=0)
    return tip + Z * 0.24


# ----------------------------------------------------------------------------------------------- lean-to

def barrel(part, center, r=0.42, h=0.95):
    n = 10
    cx, cy, cz = center
    verts, faces = [], []
    for z, rr in ((cz, r * 0.86), (cz + h * 0.5, r), (cz + h, r * 0.86)):
        for i in range(n):
            a = 2 * math.pi * i / n
            verts.append((cx + rr * math.cos(a), cy + rr * math.sin(a), z))
    for k in range(2):
        for i in range(n):
            j = (i + 1) % n
            faces.append((k * n + i, k * n + j, (k + 1) * n + j, (k + 1) * n + i))
    faces.append(tuple(range(2 * n, 3 * n)))
    faces.append(tuple(reversed(range(n))))
    planar(part, *_clean(verts, faces), "barrel")
    for z in (cz + h * 0.18, cz + h * 0.82):
        rr = r * 0.95
        ring = [(cx + rr * math.cos(2 * math.pi * i / n), cy + rr * math.sin(2 * math.pi * i / n)) for i in range(n)]
        prism(part, ring, X, Y, Z, z - 0.05, z + 0.05, "metal")


def shed(body, props):
    s = 0.0
    while s < CURVE_SHED.length - 0.1:
        p, t = CURVE_SHED.at(s)
        N = X.cross(t)
        w = min(0.66, CURVE_SHED.length - s + 0.06)
        for c0, c1 in split_lengths(ROOF_FRONT - ROOF_BACK, 3.0, 5.0):
            rag0 = rng.uniform(-0.18, 0.18) if c0 == 0 else 0.0
            board(body, Vector((ROOF_BACK + c0 - rag0, p.y, p.z)), X, N, c1 - c0 + rag0, w, 0.15, "roof", lap=0.08,
                  tilt=1.3)
        s += 0.56
    beam_z = CURVE_SHED.z_at_y(SHED_Y) - 0.04
    for x in (-3.9, -1.3, 1.3, 3.9):
        box(body, (x - 0.15, SHED_Y - 0.15, -0.05), (x + 0.15, SHED_Y + 0.15, beam_z - 0.3), "dark", tilt=2.0,
            wob=0.03)
        for d in (-1, 1):  # knee braces
            if abs(x + d * 0.6) < HX + 0.2:
                bar(body, (x, SHED_Y, beam_z - 0.85), (x + d * 0.6, SHED_Y, beam_z - 0.32), 0.12, 0.1, Y, "dark")
    box(body, (ROOF_BACK + 0.1, SHED_Y - 0.18, beam_z - 0.32), (ROOF_FRONT - 0.1, SHED_Y + 0.18, beam_z), "dark",
        tilt=0.6)
    # Feed trough, bales and a hay pile under the shed.
    tx0, tx1, ty = -3.4, -0.6, -HY - 0.65
    for mn, mx in (((tx0, ty - 0.35, 0.0), (tx1, ty - 0.29, 0.55)), ((tx0, ty + 0.29, 0.0), (tx1, ty + 0.35, 0.55)),
                   ((tx0 - 0.06, ty - 0.35, 0.0), (tx0, ty + 0.35, 0.6)), ((tx1, ty - 0.35, 0.0), (tx1 + 0.06, ty + 0.35, 0.6))):
        box(body, mn, mx, "dark", tilt=0.6)
    box(body, (tx0, ty - 0.3, 0.0), (tx1, ty + 0.3, 0.42), "hay", tilt=0.3)
    hay_bale(props, (2.6, -HY - 1.0, 0.27), math.radians(4))
    hay_bale(props, (2.6, -HY - 1.7, 0.27), math.radians(-3))
    hay_bale(props, (2.6, -HY - 1.35, 0.8), math.radians(86))
    hay_bale(props, (1.2, -HY - 0.9, 0.27), math.radians(88))
    blob(props, (0.4, -HY - 2.6, 0.0), (1.1, 0.8, 0.6), "hay", subdiv=2, lumpy=0.16)


# ----------------------------------------------------------------------------------------------- interior

def sack(part, center):
    blob(part, center, (0.32, 0.26, 0.42), "sack", subdiv=1, lumpy=0.08)
    box_c(part, (center[0], center[1], center[2] + 0.4), (0.1, 0.1, 0.06), "dark", tilt=2)


def lantern(part, x, y, z_top, z):
    box(part, (x - 0.025, y - 0.025, z + 0.35), (x + 0.025, y + 0.025, z_top), "metal", tilt=0, wob=0)
    box_c(part, (x, y, z + 0.32), (0.17, 0.17, 0.05), "metal", tilt=0, wob=0)
    box_c(part, (x, y, z + 0.1), (0.12, 0.12, 0.18), "glow", tilt=0, wob=0)
    box_c(part, (x, y, z - 0.1), (0.15, 0.15, 0.03), "metal", tilt=0, wob=0)
    for sx in (-1, 1):
        for sy in (-1, 1):
            box_c(part, (x + sx * 0.13, y + sy * 0.13, z + 0.1), (0.02, 0.02, 0.21), "metal", tilt=0, wob=0)


TIE_BEAMS = (-1.6, 0.8)
COUNTER = (-2.5, -1.85, -1.2, 1.2)  # x0, x1, y0, y1


def interior(body, props):
    for x in TIE_BEAMS:
        box(body, (x - 0.16, -HY + 0.12, 3.3), (x + 0.16, HY - 0.12, 3.6), "dark", tilt=0, wob=0.01)
        lantern(props, x, 0.0, 3.3, 2.6)
    # Loft over the entrance (its hay shows through the hayloft door), on a beam and two posts, with a ladder.
    x = FRONT_LOFT_X
    while x < HX - 0.06:
        w = min(0.6, HX - 0.05 - x)
        board(body, (x + w - 0.02, -HY + 0.05, FRONT_LOFT_Z - 0.14), Y, Z, 2 * (HY - 0.05), w - 0.02, 0.14, "floor",
              tilt=0.3, wob=0.01)
        x += 0.6
    box(body, (FRONT_LOFT_X - 0.3, -HY + 0.05, FRONT_LOFT_Z - 0.46), (FRONT_LOFT_X, HY - 0.05, FRONT_LOFT_Z - 0.14),
        "dark", tilt=0.4)
    for y in (-2.3, 2.3):
        box(body, (FRONT_LOFT_X - 0.31, y - 0.16, FLOOR_Z), (FRONT_LOFT_X + 0.01, y + 0.16, FRONT_LOFT_Z - 0.46),
            "dark", tilt=1.2)
    rail_x = FRONT_LOFT_X - 0.02
    box(body, (rail_x - 0.06, -HY + 0.05, FRONT_LOFT_Z + 0.7), (rail_x + 0.04, HY - 0.05, FRONT_LOFT_Z + 0.84),
        "dark", tilt=0.6)
    for y in (-3.0, -1.5, 0.0, 1.5, 3.0):
        box(body, (rail_x - 0.06, y - 0.05, FRONT_LOFT_Z), (rail_x + 0.04, y + 0.05, FRONT_LOFT_Z + 0.75), "dark",
            tilt=1.0)
    foot = Vector((FRONT_LOFT_X - 1.0, -2.9, FLOOR_Z))
    d = Vector((FRONT_LOFT_X - 0.05, -2.9, FRONT_LOFT_Z + 0.75)) - foot
    Nl = Vector((-d.z, 0, d.x)).normalized()
    for s in (-0.3, 0.3):
        board(body, foot + Y * (s - 0.05), d, Nl, d.length, 0.1, 0.08, "dark", tilt=0.3, wob=0.01)
    for k in range(1, 9):
        box_c(body, foot + d * (k / 9.5), (0.04, 0.3, 0.04), "dark", tilt=0.6, wob=0.01)
    for y, yaw in ((-2.6, 2), (-1.3, -3), (1.4, 4), (2.6, -2)):
        hay_bale(props, (HX - 1.0, y, FRONT_LOFT_Z + 0.28), math.radians(90 + yaw))
    hay_bale(props, (HX - 1.0, -1.95, FRONT_LOFT_Z + 0.84), math.radians(91))
    blob(props, (HX - 0.7, 0.0, FRONT_LOFT_Z), (0.8, 0.75, 0.75), "hay", subdiv=2, lumpy=0.16)
    # Villager counter facing the door, shelves on the back gable behind it.
    x0, x1, y0, y1 = COUNTER
    box(body, (x0, y0, FLOOR_Z), (x1, y1, 0.95), "floor", tilt=0.4, wob=0.02)
    box(body, (x0 - 0.12, y0 - 0.12, 0.95), (x1 + 0.12, y1 + 0.12, 1.08), "barrel", tilt=0.4, wob=0.02)
    rect(body, (x1, 0.0, 0.5), Y, X, y1 - y0 - 0.2, 0.7, 0.06, "cream", tilt=0.4)
    for z in (1.6, 2.25):
        box(body, (-HX + 0.06, y0, z), (-HX + 0.46, y1, z + 0.08), "dark", tilt=0.6)
        for k in range(5):
            yy = y0 + 0.25 + k * (y1 - y0 - 0.5) / 4
            box_c(props, (-HX + 0.26, yy, z + 0.22), (0.11, 0.11, 0.14), ("glass", "sack", "barrel", "hay", "glass")[k],
                  tilt=1.5, wob=0.01)
    sack(props, (-1.4, 1.9, FLOOR_Z))
    sack(props, (-1.1, 2.45, FLOOR_Z))
    for x, y in ((-3.5, 2.95), (-2.65, 3.05), (-3.45, -2.95)):
        barrel(props, (x, y, FLOOR_Z))
    box(props, (-3.75, -2.4, FLOOR_Z), (-3.05, -1.7, 0.72), "floor", tilt=1.5, wob=0.02)
    box(props, (-3.6, -2.3, 0.72), (-3.15, -1.8, 1.15), "barrel", tilt=2.5, wob=0.02)


def outside_props(props):
    barrel(props, (HX + 0.75, -2.55, 0.0))
    barrel(props, (HX + 0.85, -3.25, 0.0), r=0.36, h=0.8)
    for x, y, r in ((HX + 0.75, 2.6, 0.55), (-HX - 0.7, -1.0, 0.6), (-HX - 0.75, 2.4, 0.5),
                    (TOWER_C.x - 1.4, 7.0, 0.6)):
        blob(props, (x, y, 0.0), (r, r * 0.9, r * 0.85), "leaf", subdiv=1, lumpy=0.2)
    hay_bale(props, (-1.4, HY + 0.9, 0.27), math.radians(3))
    hay_bale(props, (-2.6, HY + 0.95, 0.27), math.radians(-4))
    hay_bale(props, (-2.0, HY + 0.9, 0.8), math.radians(6))


ROOSTER = [(-0.3, 0.18), (-0.48, 0.3), (-0.7, 0.62), (-0.55, 0.55), (-0.66, 0.86), (-0.48, 0.7), (-0.44, 0.95),
           (-0.32, 0.66), (-0.22, 0.48), (-0.02, 0.5), (0.08, 0.62), (0.06, 0.8), (0.1, 0.92), (0.15, 0.86),
           (0.2, 0.96), (0.25, 0.88), (0.3, 0.94), (0.31, 0.84), (0.44, 0.78), (0.31, 0.74), (0.3, 0.64),
           (0.24, 0.58), (0.24, 0.44), (0.16, 0.28), (0.04, 0.2), (0.04, 0.0), (-0.02, 0.0), (-0.04, 0.17),
           (-0.12, 0.17), (-0.14, 0.0), (-0.2, 0.0), (-0.2, 0.17)]  # side view facing +X: tail, comb, beak, legs


def weathervane(part, base):
    bx, by, bz = base
    box(part, (bx - 0.045, by - 0.045, bz - 0.05), (bx + 0.045, by + 0.045, bz + 1.5), "metal", tilt=0, wob=0)
    zc = bz + 0.5
    box(part, (bx - 0.45, by - 0.03, zc - 0.03), (bx + 0.45, by + 0.03, zc + 0.03), "metal", tilt=0, wob=0)
    box(part, (bx - 0.03, by - 0.45, zc - 0.03), (bx + 0.03, by + 0.45, zc + 0.03), "metal", tilt=0, wob=0)
    for dx, dy in ((0.45, 0), (-0.45, 0), (0, 0.45), (0, -0.45)):
        box_c(part, (bx + dx, by + dy, zc), (0.07, 0.07, 0.07), "metal", tilt=0, wob=0)
    za = bz + 0.95
    for poly in ([(bx - 0.65, za - 0.035), (bx + 0.5, za - 0.035), (bx + 0.5, za + 0.035), (bx - 0.65, za + 0.035)],
                 [(bx + 0.5, za - 0.12), (bx + 0.75, za), (bx + 0.5, za + 0.12)],
                 [(bx - 0.65, za - 0.14), (bx - 0.42, za - 0.05), (bx - 0.42, za + 0.05), (bx - 0.65, za + 0.14)]):
        prism(part, poly, X, Z, Y, by - 0.025, by + 0.025, "metal")
    s = 1.2
    prism(part, [(bx + x * s, bz + 1.45 + z * s) for x, z in ROOSTER], X, Z, Y, by - 0.04, by + 0.04, "metal")


# ----------------------------------------------------------------------------------------------- collision

def collision(col):
    """Simple shapes for a MeshCollider (CameraBlocker): floor plinth with a ramp, walls with the door gap, solids."""
    def solid(mn, mx):
        mn, mx = [min(a, b) for a, b in zip(mn, mx)], [max(a, b) for a, b in zip(mn, mx)]
        v = [(mn[0], mn[1], mn[2]), (mx[0], mn[1], mn[2]), (mx[0], mx[1], mn[2]), (mn[0], mx[1], mn[2]),
             (mn[0], mn[1], mx[2]), (mx[0], mn[1], mx[2]), (mx[0], mx[1], mx[2]), (mn[0], mx[1], mx[2])]
        f = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
        planar(col, v, f, "metal")

    r = 0.7
    verts = [(-HX, -HY, FLOOR_Z), (HX, -HY, FLOOR_Z), (HX, HY, FLOOR_Z), (-HX, HY, FLOOR_Z),
             (-HX, -HY, -0.04), (HX + r, -HY, -0.04), (HX + r, HY, -0.04), (-HX, HY, -0.04),
             (-HX, -HY, -0.3), (HX + r, -HY, -0.3), (HX + r, HY, -0.3), (-HX, HY, -0.3)]
    faces = [(0, 1, 2, 3), (8, 11, 10, 9), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0),
             (4, 8, 9, 5), (5, 9, 10, 6), (6, 10, 11, 7), (7, 11, 8, 4)]
    planar(col, *_clean(verts, faces), "metal")
    t = 0.2
    solid((-HX - t, -HY, 0.0), (-HX + 0.02, HY, 4.2))
    solid((HX - 0.02, -HY, 0.0), (HX + t, -DW, 4.2))
    solid((HX - 0.02, DW, 0.0), (HX + t, HY, 4.2))
    solid((HX - 0.02, -DW, DS + DW), (HX + t, DW, 4.2))
    solid((-HX, -HY - t, 0.0), (HX, -HY + 0.02, 3.4))
    solid((-HX, HY - 0.02, 0.0), (HX, HY + t, 3.4))
    solid((TOWER_C.x - 1.75, HY, 0.0), (TOWER_C.x + 1.75, TOWER_C.y + 1.75, 6.5))
    for x in (-3.9, -1.3, 1.3, 3.9):
        solid((x - 0.17, SHED_Y - 0.17, 0.0), (x + 0.17, SHED_Y + 0.17, 2.0))
    solid((-3.5, -HY - 1.05, 0.0), (-0.5, -HY - 0.25, 0.6))
    solid((0.55, -HY - 2.1, 0.0), (3.25, -HY - 0.5, 1.1))
    for y in (-2.3, 2.3):
        solid((FRONT_LOFT_X - 0.32, y - 0.17, 0.0), (FRONT_LOFT_X + 0.02, y + 0.17, FRONT_LOFT_Z - 0.46))
    x0, x1, y0, y1 = COUNTER
    solid((x0 - 0.12, y0 - 0.12, 0.0), (x1 + 0.12, y1 + 0.12, 1.08))
    solid((-3.95, 2.5, 0.0), (-2.2, 3.55, 0.95))
    solid((-3.9, -3.4, 0.0), (-3.0, -1.65, 1.15))
    solid((-1.75, 1.6, 0.0), (-0.8, 2.75, 0.8))


# ----------------------------------------------------------------------------------------------- assembly

def material(images):
    mat = bpy.data.materials.get("M_FarmBarn") or bpy.data.materials.new("M_FarmBarn")
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = images["T_FarmBarn_Atlas"]
    emi = nt.nodes.new("ShaderNodeTexImage")
    emi.image = images["T_FarmBarn_Emission"]
    bsdf.inputs["Roughness"].default_value = 0.9
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(emi.outputs["Color"], bsdf.inputs["Emission Color"])
    bsdf.inputs["Emission Strength"].default_value = 1.0
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    out.location, bsdf.location, tex.location, emi.location = (300, 0), (0, 0), (-400, 100), (-400, -200)
    return mat


def to_object(part, coll, mat, origin=Vector()):
    me = bpy.data.meshes.get(part.name)
    if me:
        bpy.data.meshes.remove(me)
    me = bpy.data.meshes.new(part.name)
    me.from_pydata([Vector(v) - origin for v in part.verts], [], part.faces)
    uv = me.uv_layers.new(name="UVMap")
    for poly, loop_uvs in zip(me.polygons, part.uvs):
        for li, (u, v) in zip(poly.loop_indices, loop_uvs):
            uv.data[li].uv = (u, v)
    me.materials.append(mat)
    me.validate()
    me.update()
    obj = bpy.data.objects.new(part.name, me)
    obj.location = origin
    coll.objects.link(obj)
    obj.modifiers.new("Triangulate", "TRIANGULATE")
    return obj


def build():
    global rng
    rng = random.Random(5)
    images = barn_paint.paint_atlas(HERE)
    coll = bpy.data.collections.get("FarmBarn")
    if coll:
        for o in list(coll.objects):
            bpy.data.objects.remove(o, do_unlink=True)
    else:
        coll = bpy.data.collections.new("FarmBarn")
        bpy.context.scene.collection.children.link(coll)
    mat = material(images)

    body, props = Part("FarmBarn"), Part("Barn_Props")
    floor(body)
    footing(body)
    long_walls(body)
    gables(body)
    gables(body, inside=True)
    corner_posts(body)
    for s in (-1, 1):  # front windows beside the door, with flower boxes
        window_at(body, (HX + 0.14, s * 2.75, 2.15), Y, X, 0.6, 0.9, shutters=False, flowers=True)
    round_window(body, (-HX - 0.14, 0, 5.6), Y, -X)
    for s in (-1, 1):
        window_at(body, (-HX - 0.14, s * 2.3, 2.2), Y, -X, 0.7, 1.0)
        window_at(body, (s * 2.4, -HY - 0.14, 1.9), X, -Y, 0.8, 1.0)
    window_at(body, (-2.4, HY + 0.14, 2.0), X, Y, 0.8, 1.0)
    # The same windows seen from inside, on the lining.
    li = 0.08
    for s in (-1, 1):
        window_at(body, (HX - li, s * 2.75, 2.15), Y, -X, 0.6, 0.9, shutters=False)
        window_at(body, (-HX + li, s * 2.3, 2.2), Y, X, 0.7, 1.0, shutters=False)
        window_at(body, (s * 2.4, -HY + li, 1.9), X, Y, 0.8, 1.0, shutters=False)
    window_at(body, (-2.4, HY - li, 2.0), X, -Y, 0.8, 1.0, shutters=False)
    round_window(body, (-HX + li, 0, 5.6), Y, X)
    roof(body)
    barge_boards(body)
    front_details(body)
    vane_base = tower(body)
    shed(body, props)
    interior(body, props)
    outside_props(props)

    door_l, door_r = Part("Barn_Door_Left"), Part("Barn_Door_Right")
    hinge_l = Vector((HX + 0.02, -DW, FLOOR_Z + 0.01))
    hinge_r = Vector((HX + 0.02, DW, FLOOR_Z + 0.01))
    leaf(door_l, hinge_l, Y, X, DW - 0.02, lambda d: (door_top(-DW + max(d, 0.02)) or DS) - FLOOR_Z - 0.06)
    leaf(door_r, hinge_r, -Y, X, DW - 0.02, lambda d: (door_top(DW - max(d, 0.02)) or DS) - FLOOR_Z - 0.06)
    vane = Part("Barn_Weathervane")
    weathervane(vane, vane_base)
    col = Part("Barn_Collision")
    collision(col)

    root = to_object(body, coll, mat)
    pivot_out = Vector((0.12, 0, 0))
    children = [to_object(door_l, coll, mat, hinge_l + pivot_out), to_object(door_r, coll, mat, hinge_r + pivot_out),
                to_object(vane, coll, mat, vane_base), to_object(props, coll, mat), to_object(col, coll, mat)]
    for c in children:
        c.parent = root
    children[-1].hide_render = True
    children[-1].hide_set(True)
    return {"objects": [root.name] + [c.name for c in children],
            "faces": {o.name: len(o.data.polygons) for o in [root] + children}}


def export():
    """FBX (axis conversion baked, like the sheep fold props) plus the painted atlas into Assets/Art/FarmBarn."""
    models, textures = os.path.join(OUT, "Models"), os.path.join(OUT, "Textures")
    os.makedirs(models, exist_ok=True)
    os.makedirs(textures, exist_ok=True)
    for name in ("T_FarmBarn_Atlas", "T_FarmBarn_Emission"):
        shutil.copyfile(os.path.join(HERE, name + ".png"), os.path.join(textures, name + ".png"))
    root = bpy.data.objects["FarmBarn"]
    objs = [root] + list(root.children)
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in objs:
        o.hide_set(False)
        o.select_set(True)
    bpy.context.view_layer.objects.active = root
    path = os.path.join(models, "FarmBarn.fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"}, apply_unit_scale=True,
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                             bake_space_transform=True, mesh_smooth_type="FACE", use_mesh_modifiers=True,
                             bake_anim=False, path_mode="STRIP", embed_textures=False)
    bpy.data.objects["Barn_Collision"].hide_set(True)
    return {"fbx": path, "objects": [o.name for o in objs]}
