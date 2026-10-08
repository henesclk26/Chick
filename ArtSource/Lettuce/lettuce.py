# Low-poly lettuce on a soil mound, as game parts, saved to ArtSource/Lettuce.blend: Mound, Lettuce_NN (the leaves,
# each in its own frame), Half_NN (a fallen leaf after its first peck: the tip half eaten, same frame) and Stub_NN
# (the chewed stub a leaf leaves behind on the plant when it is knocked off).
#
# Look: built like a decimated sculpt. Each leaf is a smooth high-res grid (fan pleats, cupped base, sides rolling
# back, frilly top edge, crinkle noise) that is decimated on its own, given 3 mm thickness and flat-coloured per face
# (the "Paint" corner colours; lettuce_export.py turns them into a palette texture for Unity).
# Leaves come out in phyllotactic order and each later leaf is nudged just outside the earlier ones (seen from the
# heart), so leaves layer instead of cutting through each other.
# In the game (BerryPlant) a peck knocks the nearest leaf off; it slides down off its own side, its stub shows, and
# on the ground it is eaten in two pecks. Once every leaf is gone only the stubs are left on the mound.
# Real-world metres, Z up, pivot at the ground centre.
#
# Usage: blender -b --python ArtSource/Lettuce/lettuce.py -- <out.blend>
import bpy, bmesh, math, random, sys
import numpy as np
from mathutils import Vector, Matrix, noise
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else None

RATIO = 0.1          # decimate (collapse) ratio
SOIL_TOP = 0.05
CENTRE = Vector((0.0, 0.0, 0.09))   # inside the heart; leaves nest as seen from here
RAMP = 0.85          # within a ring, each leaf opens a little further toward the next ring's tilt
SHELL_GAP, SHELL_CAP = 0.006, 0.09

rng = random.Random(11)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene

def smoothstep(e0, e1, x):
    t = max(0.0, min(1.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)

def lerp(a, b, t):
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(len(a)))

# ---------------------------------------------------------------- leaves
bm = bmesh.new()
L_U = bm.verts.layers.float.new("lu")    # along the leaf, 0 base .. 1 tip
L_V = bm.verts.layers.float.new("lv")    # across, 0 midrib .. 1 edge
L_R = bm.verts.layers.float.new("lr")    # ring, 0 heart .. 1 outer
L_K = bm.verts.layers.float.new("lk")    # 1 leaf, 2 soil, 3 clod
L_ID = bm.verts.layers.float.new("lid")  # leaf index (generation order)
LEAF_GRID, RING_OF, CONCAVE = {}, {}, {}

def leaf(phi, length, width, a0, a1, cp, cup, flare, pleat, ruffle, ring, r0, z0, nu=26, nv=22):
    k = len(LEAF_GRID)
    R = Vector((math.cos(phi), math.sin(phi), 0.0)); Z = Vector((0, 0, 1)); S = Z.cross(R)
    steps = 80
    pts = []
    p = R * r0 + Z * z0
    for i in range(steps + 1):
        u = i / steps
        a = math.radians(a0 + (a1 - a0) * (u ** cp))
        T = R * math.sin(a) + Z * math.cos(a)
        N = -R * math.cos(a) + Z * math.sin(a)
        pts.append((p.copy(), N))
        p = p + T * (length / steps)
    def spine(u):
        f = min(max(u, 0.0), 1.0) * steps; i = min(int(f), steps - 1); t = f - i
        return pts[i][0].lerp(pts[i + 1][0], t), pts[i][1].lerp(pts[i + 1][1], t).normalized()
    seed = Vector((rng.uniform(-50, 50), rng.uniform(-50, 50), rng.uniform(-50, 50)))
    npl = rng.choice([2, 3])
    ph = rng.uniform(0, 6.28)
    kr = rng.uniform(1.6, 2.2)
    grid = []
    for i in range(nu):
        row = []
        ui = i / (nu - 1)
        for j in range(nv):
            v = -1.0 + 2.0 * j / (nv - 1)
            av = abs(v)
            u = ui * (1.0 - 0.28 * av ** 2.2)                       # rounded top
            P, N = spine(u)
            hw = 0.5 * width * (0.16 + 0.84 * math.sin(0.5 * math.pi * min(u / 0.62, 1.0)) ** 0.9)
            # frilly outline: perimeter waves, strongest at the top and outer edge
            t = u * 1.1 + (1.0 - av) * 0.45
            fr = math.sin(2 * math.pi * kr * t + ph) * smoothstep(0.35, 1.0, ui) * smoothstep(0.3, 1.0, av)
            hw *= 1.0 + 0.05 * fr
            x = v * hw
            z = cup * (1.0 - 0.75 * u) * av ** 1.8 * hw                # spoon near the base
            z -= flare * u ** 2 * av ** 2 * hw                          # sides roll back at the top
            z += pleat * hw * math.sin(npl * math.pi * v + ph) * u ** 1.3 * (0.35 + 0.65 * av)   # fan pleats
            z += ruffle * width * 0.09 * fr                             # frilly edge out of plane
            z -= 0.035 * width * (1.0 - smoothstep(0.0, 0.18, av)) * (1.0 - 0.6 * u)          # midrib
            q = P + S * x
            z += noise.noise(seed + q * 14.0) * 0.005 * (0.3 + u)      # crinkle
            vert = bm.verts.new(q + N * z)
            vert[L_U] = ui; vert[L_V] = av; vert[L_R] = ring; vert[L_K] = 1.0; vert[L_ID] = k
            row.append(vert)
        grid.append(row)
    LEAF_GRID[k] = grid; RING_OF[k] = ring; CONCAVE[k] = spine(0.5)[1]
    for i in range(nu - 1):
        for j in range(nv - 1):
            bm.faces.new((grid[i][j], grid[i][j + 1], grid[i + 1][j + 1], grid[i + 1][j]))

GOLDEN = math.radians(137.508)
# count, length, width, a0, a1, curl_pow, cup, flare, pleat, ruffle, ring, r0, built
RINGS = [
    (3, 0.160, 0.150, 2, -50, 1.5, 1.30, 0.00, 0.04, 0.20, 0.00, 0.004, True),   # tight heart
    (4, 0.210, 0.190, 6, -20, 1.5, 1.15, 0.05, 0.06, 0.40, 0.25, 0.010, True),   # heart wrappers
    (5, 0.270, 0.225, 12, 16, 1.8, 0.95, 0.25, 0.08, 0.70, 0.50, 0.016, True),   # inner, upright
    (6, 0.295, 0.255, 24, 58, 2.2, 0.75, 0.45, 0.09, 0.95, 0.75, 0.022, True),   # middle, opening out
    (7, 0.275, 0.255, 42, 90, 2.4, 0.65, 0.55, 0.09, 1.10, 1.00, 0.028, True),   # outer, tips flopping over
    # A low skirt on the soil was dropped from the design. Its row stays: the outer ring eases toward its tilt, and
    # its random numbers are still drawn so every other leaf and the soil keep their shape.
    (6, 0.165, 0.210, 56, 92, 2.0, 0.60, 0.45, 0.08, 1.00, 1.25, 0.030, False),
]
idx = 0
for ri, (count, L, W, a0, a1, cp, cup, flare, pleat, ruf, ring, r0, built) in enumerate(RINGS):
    nxt = RINGS[ri + 1] if ri + 1 < len(RINGS) else None
    for k in range(count):
        phi = idx * GOLDEN + rng.uniform(-0.1, 0.1); idx += 1
        s_len, s_wid, j0, j1 = rng.uniform(0.92, 1.06), rng.uniform(0.92, 1.08), rng.uniform(-4, 4), rng.uniform(-7, 7)
        if not built:
            rng.uniform(-50, 50); rng.uniform(-50, 50); rng.uniform(-50, 50)   # the draws leaf() would make
            rng.choice([2, 3]); rng.uniform(0, 6.28); rng.uniform(1.6, 2.2)
            continue
        # Each leaf a little more open than the one before it, easing towards the next ring, so successive leaves
        # nest; the random tilt jitter is kept small so it cannot undo that order.
        f = k / count
        n0, n1 = (nxt[3], nxt[4]) if nxt else (a0 + 8, a1 + 10)
        b0 = a0 + (n0 - a0) * f * RAMP + j0 * 0.25
        b1 = a1 + (n1 - a1) * f * RAMP + j1 * 0.25
        leaf(phi, L * s_len, W * s_wid, b0, b1, cp, cup, flare, pleat, ruf, ring, r0, SOIL_TOP - 0.015)

def push_shells(grids, gap, cap, dilate=3, blur=10):
    """Leaves come out in phyllotactic order, each new one inside the older ones, so the generation order is the
    nesting order. Seen along rays from CENTRE, every leaf is moved just beyond all leaves before it (at most `cap`),
    with the push dilated and blurred over the leaf grid so it bends smoothly instead of denting."""
    order = sorted(grids)
    for pos, k in enumerate(order):
        if pos == 0:
            continue
        vi, coords, polys = {}, [], []
        for j in order[:pos]:
            g = grids[j]
            for i in range(len(g) - 1):
                for jj in range(len(g[0]) - 1):
                    ids = []
                    for v in (g[i][jj], g[i][jj + 1], g[i + 1][jj + 1], g[i + 1][jj]):
                        if v not in vi:
                            vi[v] = len(coords); coords.append(v.co.copy())
                        ids.append(vi[v])
                    polys.append(ids)
        tree = BVHTree.FromPolygons(coords, polys)
        g = grids[k]; nu, nv = len(g), len(g[0])
        need = np.zeros((nu, nv)); dirs = np.zeros((nu, nv, 3)); wts = np.zeros((nu, nv))
        for i in range(nu):
            for j in range(nv):
                v = g[i][j]
                rel = v.co - CENTRE; dist = rel.length
                if dist < 1e-5:
                    continue
                d = rel / dist
                dirs[i, j] = d
                wts[i, j] = smoothstep(0.06, 0.2, v[L_U])     # the bases, hidden in the heart, stay put
                far = None
                origin = CENTRE.copy(); travelled = 0.0
                for _ in range(24):
                    hit, _, _, t = tree.ray_cast(origin, d, dist + cap - travelled)
                    if hit is None:
                        break
                    travelled += t
                    far = travelled
                    origin = hit + d * 1e-5; travelled += 1e-5
                if far is not None and far + gap > dist:
                    need[i, j] = min(far + gap - dist, cap)
        if not need.any():
            continue
        sm = need.copy()
        for _ in range(dilate):
            P = np.pad(sm, 1, mode="edge")
            sm = np.maximum.reduce([P[1:-1, 1:-1], P[:-2, 1:-1], P[2:, 1:-1], P[1:-1, :-2], P[1:-1, 2:]])
        for _ in range(blur):
            P = np.pad(sm, 1, mode="edge")
            sm = (P[:-2, 1:-1] + P[2:, 1:-1] + P[1:-1, :-2] + P[1:-1, 2:] + 2 * P[1:-1, 1:-1]) / 6.0
        sm = np.maximum(sm, need)
        for i in range(nu):
            for j in range(nv):
                if sm[i, j] > 0 and wts[i, j] > 0:
                    g[i][j].co += Vector(dirs[i, j]) * float(sm[i, j] * wts[i, j])

push_shells(LEAF_GRID, SHELL_GAP, SHELL_CAP)
bm_leaves = bm

# ---------------------------------------------------------------- soil disc and clods
bm = bmesh.new()
for name in ("lu", "lv", "lr", "lk", "lid"):
    bm.verts.layers.float.new(name)
L_K = bm.verts.layers.float["lk"]

def soil():
    seed = Vector((3.1, 7.7, 1.3))
    nr, na = 10, 48
    rad = [0.205 + 0.018 * noise.noise(Vector((math.cos(2 * math.pi * a / na) * 2, math.sin(2 * math.pi * a / na) * 2, 0.5)))
           for a in range(na)]
    rows = []
    for ri in range(nr + 1):                      # top surface rings, centre to rim
        t = ri / nr
        row = []
        for ai in range(na):
            a = 2 * math.pi * ai / na
            r = rad[ai] * 0.9 * t
            z = SOIL_TOP * (1.15 - 0.25 * t * t) + noise.noise(seed + Vector((math.cos(a) * r, math.sin(a) * r, 0)) * 30) * 0.006
            v = bm.verts.new((math.cos(a) * r, math.sin(a) * r, z)); v[L_K] = 2.0
            row.append(v)
        rows.append(row)
    for h in (0.6, 0.0):                           # rim side down to the ground
        row = []
        for ai in range(na):
            a = 2 * math.pi * ai / na
            r = rad[ai] * (0.97 if h > 0 else 1.0)
            v = bm.verts.new((math.cos(a) * r, math.sin(a) * r, SOIL_TOP * h * (0.8 + 0.2 * rng.random()))); v[L_K] = 2.0
            row.append(v)
        rows.append(row)
    for ri in range(1, len(rows) - 1):
        for ai in range(na):
            b = (ai + 1) % na
            bm.faces.new((rows[ri][ai], rows[ri][b], rows[ri + 1][b], rows[ri + 1][ai]))
    c = bm.verts.new((0, 0, SOIL_TOP * 1.15)); c[L_K] = 2.0
    for ai in range(na):
        bm.faces.new((c, rows[1][ai], rows[1][(ai + 1) % na]))
    for v in rows[0]:
        bm.verts.remove(v)
    cb = bm.verts.new((0, 0, 0)); cb[L_K] = 2.0
    for ai in range(na):
        bm.faces.new((rows[-1][(ai + 1) % na], rows[-1][ai], cb))

def clod(center, size, squash):
    g = bmesh.ops.create_icosphere(bm, subdivisions=1, radius=1.0)
    rot = Matrix.Rotation(rng.uniform(0, 6.28), 3, 'Z') @ Matrix.Rotation(rng.uniform(-0.35, 0.35), 3, 'X')
    seed = Vector((rng.uniform(-9, 9), rng.uniform(-9, 9), rng.uniform(-9, 9)))
    for v in g["verts"]:
        co = v.co * (1.0 + 0.28 * noise.noise(seed + v.co * 1.6))
        co = Vector((co.x * size[0], co.y * size[1], co.z * size[2] * squash))
        v.co = rot @ co + center
        v[L_K] = 3.0

soil()
for i in range(18):
    a = 2 * math.pi * i / 18 + rng.uniform(-0.1, 0.1)
    r = rng.uniform(0.16, 0.19)
    s = rng.uniform(0.026, 0.04)
    clod(Vector((math.cos(a) * r, math.sin(a) * r, SOIL_TOP * rng.uniform(0.55, 0.8))),
         (s * rng.uniform(1.1, 1.5), s * rng.uniform(1.1, 1.5), s), rng.uniform(0.6, 0.85))
for i in range(10):
    a = 2 * math.pi * i / 10 + rng.uniform(-0.25, 0.25)
    r = rng.uniform(0.11, 0.145)
    s = rng.uniform(0.018, 0.028)
    clod(Vector((math.cos(a) * r, math.sin(a) * r, SOIL_TOP * 1.0)), (s * 1.3, s * 1.3, s), 0.6)

def decimate_object(name, bm_src, ratio):
    me = bpy.data.meshes.new(name); bm_src.to_mesh(me)
    ob = bpy.data.objects.new(name, me); scene.collection.objects.link(ob)
    bpy.context.view_layer.objects.active = ob
    for o in scene.objects:
        o.select_set(o == ob)
    if len(me.polygons) > 8:
        d = ob.modifiers.new("Dec", 'DECIMATE'); d.decimate_type = 'COLLAPSE'; d.ratio = ratio
        d.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=d.name)
    return ob

soil_ob = decimate_object("SoilTmp", bm, RATIO)
bm.free()


# ---------------------------------------------------------------- game parts
# The game knocks leaves off one at a time (BerryPlant): each leaf is its own object; its half-eaten version and its
# chewed stub (both hidden in the game until used) are others. Each leaf's mesh is in its own frame — length +Y, cupped face +Z, origin
# at the centre of its bounds — and the object transform puts it on the plant, so in Unity a leaf at identity
# rotation lies on its back.

# Stub cuts are fixed per leaf (chewed outline a little above the base).
brng = random.Random(23)
STUB = {}
for k in sorted(LEAF_GRID):
    n = brng.choice([1, 2, 2, 3])
    for _ in range(n):                                  # (draws once used for bite notches; kept so stubs stay)
        brng.uniform(-0.65, 0.65); brng.uniform(0.32, 0.5); brng.uniform(0.2, 0.34)
    STUB[k] = (brng.uniform(0.07, 0.11), brng.uniform(0, 6.28), brng.choice([2, 3]))

def stub_edge(k, v):
    s0, ph, n = STUB[k]
    return s0 + 0.03 * abs(math.sin(n * math.pi * (v + 1) * 0.5 + ph))

# A fallen leaf takes two pecks: the first eats its tip half, leaving the base half with two or three round bites.
hrng = random.Random(31)
HALF = {k: [(c + hrng.uniform(-0.12, 0.12), hrng.uniform(0.45, 0.6), hrng.uniform(0.08, 0.14))
            for c in hrng.choice([(-0.4, 0.35), (-0.55, 0.0, 0.5)])] for k in sorted(LEAF_GRID)}

def half_edge(k, v):
    top = 0.6
    for (c, w, d) in HALF[k]:
        t = (v - c) / w
        if abs(t) < 1:
            top = min(top, 0.6 - d * math.sqrt(1 - t * t))
    return top

def eat_leaf(b, grid, edge):
    """Cut a leaf grid back to u <= edge(v): vertices past the edge slide down their column onto it (smooth chewed
    outline), faces entirely past it are removed, and the new edge is flagged so it colours paler."""
    lu = b.verts.layers.float["lu"]; bite = b.verts.layers.float["bite"]
    nu, nv = len(grid), len(grid[0])
    orig = [[grid[i][j].co.copy() for j in range(nv)] for i in range(nu)]
    beyond = set()
    for j in range(nv):
        v = -1.0 + 2.0 * j / (nv - 1)
        e = max(0.0, min(1.0, edge(v)))
        f = e * (nu - 1); i0 = min(int(f), nu - 2); t = f - i0
        cut = orig[i0][j].lerp(orig[i0 + 1][j], t)
        for i in range(nu):
            if i / (nu - 1) > e + 1e-6:
                beyond.add(grid[i][j])
                grid[i][j].co = cut
                grid[i][j][lu] = e
                grid[i][j][bite] = 1.0
            elif (i + 1) / (nu - 1) > e + 1e-6:
                grid[i][j][bite] = 1.0                      # the last row below the cut
    doomed = set()
    for i in range(nu - 1):
        for j in range(nv - 1):
            quad = (grid[i][j], grid[i][j + 1], grid[i + 1][j + 1], grid[i + 1][j])
            if all(q in beyond for q in quad):
                doomed |= set(quad[0].link_faces) & set(quad[2].link_faces)
    bmesh.ops.delete(b, geom=list(doomed), context='FACES')

bm_leaves.verts.ensure_lookup_table(); bm_leaves.verts.index_update()
GRID_IDX = {k: [[v.index for v in row] for row in grid] for k, grid in LEAF_GRID.items()}
FRAME = {}
for k, grid in LEAF_GRID.items():
    # The leaf's own frame, from its shape after nesting: length base -> tip, cupped face as generated.
    base = sum((v.co for v in grid[0]), Vector()) / len(grid[0])
    tip = sum((v.co for v in grid[-1]), Vector()) / len(grid[-1])
    y = (tip - base).normalized()
    z = (CONCAVE[k] - y * CONCAVE[k].dot(y)).normalized()
    FRAME[k] = Matrix((y.cross(z), y, z)).transposed()   # columns: across, length, cupped face
bm_leaves.verts.layers.float.new("bite")   # (invalidates the BMVert refs in LEAF_GRID; use GRID_IDX from here)

def leaf_piece(k, cut):
    """One leaf on its own — whole (cut None), 'half' eaten or cut back to its 'stub' — decimated alone."""
    b = bm_leaves.copy()
    lid = b.verts.layers.float["lid"]
    b.verts.ensure_lookup_table()
    grid = [[b.verts[i] for i in row] for row in GRID_IDX[k]]
    bmesh.ops.delete(b, geom=[f for f in b.faces if int(round(f.verts[0][lid])) != k], context='FACES')
    bmesh.ops.delete(b, geom=[v for v in b.verts if not v.link_faces and int(round(v[lid])) != k], context='VERTS')
    if cut == 'stub':
        eat_leaf(b, grid, lambda v: stub_edge(k, v))
    elif cut == 'half':
        eat_leaf(b, grid, lambda v: half_edge(k, v))
    bmesh.ops.delete(b, geom=[v for v in b.verts if not v.link_faces], context='VERTS')
    ob = decimate_object("Piece_%d_%s" % (k, cut or "leaf"), b, RATIO)
    b.free()
    return ob

LEAF_BASE = (0.46, 0.68, 0.16); LEAF_MID = (0.57, 0.83, 0.20); LEAF_TIP = (0.67, 0.91, 0.27)
HEART = (0.72, 0.90, 0.33); RIB = (0.76, 0.92, 0.45); BITE = (0.80, 0.93, 0.56)
SOIL_TOPC = (0.40, 0.25, 0.15); SOIL_SIDE = (0.47, 0.30, 0.19); CLOD = (0.66, 0.46, 0.33)
def jitter(p):
    """Per-face brightness variation from the face position (plant space), so it does not depend on build order."""
    x = math.sin(p.x * 129.898 + p.y * 782.33 + p.z * 377.19) * 43758.5453
    return 0.95 + 0.1 * (x - math.floor(x))

MATS = []
for name in ("M_Lettuce_Leaf", "M_Lettuce_Soil"):
    m = bpy.data.materials.new(name); m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    vc = m.node_tree.nodes.new("ShaderNodeVertexColor"); vc.layer_name = "Paint"
    m.node_tree.links.new(vc.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.7; m.use_backface_culling = True
    MATS.append(m)

def finish(source, name, frame=None, centre=None):
    """Thickness on the leaf faces, triangles, flat per-face colours (Paint); optionally re-expressed in a leaf
    frame (origin at the centre of its bounds, or at `centre` in that frame) with the object placed back where it
    was. Returns the object and the frame origin used."""
    b = bmesh.new()
    b.from_mesh(source.data)
    lk = b.verts.layers.float["lk"]
    leaf_faces = [f for f in b.faces if sum(v[lk] for v in f.verts) / len(f.verts) < 1.5]
    if leaf_faces:
        # Sliver triangles have no usable normal and make solidify throw spikes; dissolve them, and put any vertex
        # the thickness still throws off the leaf surface back onto it.
        bmesh.ops.dissolve_degenerate(b, dist=0.0015, edges=list({e for f in leaf_faces for e in f.edges}))
        leaf_faces = [f for f in b.faces if sum(v[lk] for v in f.verts) / len(f.verts) < 1.5]
        vi, coords, polys = {}, [], []
        for f in leaf_faces:
            ids = []
            for v in f.verts:
                if v not in vi:
                    vi[v] = len(coords); coords.append(v.co.copy())
                ids.append(vi[v])
            polys.append(ids)
        surface = BVHTree.FromPolygons(coords, polys)
        bmesh.ops.solidify(b, geom=leaf_faces, thickness=0.003)
        for v in {v for f in b.faces if f.verts[0][lk] < 1.5 for v in f.verts}:
            loc, _, _, dist = surface.find_nearest(v.co)
            if loc is not None and dist > 0.008:
                v.co = loc
    bmesh.ops.triangulate(b, faces=b.faces[:])
    bmesh.ops.recalc_face_normals(b, faces=b.faces[:])
    lu = b.verts.layers.float["lu"]; lv = b.verts.layers.float["lv"]; lr = b.verts.layers.float["lr"]
    bt = b.verts.layers.float["bite"] if "bite" in b.verts.layers.float else None
    col = b.loops.layers.color.new("Paint")
    for f in b.faces:
        n = len(f.verts); kind = sum(v[lk] for v in f.verts) / n
        cen = f.calc_center_median(); jit = jitter(cen)
        if kind < 1.5:
            u = sum(v[lu] for v in f.verts) / n; av = sum(v[lv] for v in f.verts) / n; ring = sum(v[lr] for v in f.verts) / n
            c = lerp(LEAF_BASE, LEAF_MID, smoothstep(0.0, 0.4, u))
            c = lerp(c, LEAF_TIP, smoothstep(0.45, 1.0, u) * smoothstep(0.15, 0.9, av))
            c = lerp(c, RIB, 0.5 * (1 - smoothstep(0.0, 0.16, av)) * (1 - smoothstep(0.5, 1.0, u)))
            c = lerp(HEART, c, smoothstep(0.0, 0.55, ring))
            if bt is not None:
                c = lerp(c, BITE, 0.75 * smoothstep(0.5, 0.95, sum(v[bt] for v in f.verts) / n))
            f.material_index = 0
        elif kind < 2.5:
            c = lerp(SOIL_SIDE, SOIL_TOPC, smoothstep(SOIL_TOP * 0.5, SOIL_TOP, cen.z)); f.material_index = 1
        else:
            c = lerp(SOIL_SIDE, CLOD, smoothstep(0.0, SOIL_TOP * 1.2, cen.z)); f.material_index = 1
        for l in f.loops:
            l[col] = (min(1, c[0] * jit), min(1, c[1] * jit), min(1, c[2] * jit), 1.0)
    place = Matrix.Identity(4)
    if frame is not None:
        inv = frame.inverted()
        local = [inv @ v.co for v in b.verts]
        if centre is None:
            lo = Vector([min(p[i] for p in local) for i in range(3)])
            hi = Vector([max(p[i] for p in local) for i in range(3)])
            centre = (lo + hi) / 2
        for v, p in zip(b.verts, local):
            v.co = p - centre
        place = Matrix.Translation(frame @ centre) @ frame.to_4x4()
    me = bpy.data.meshes.new(name)
    b.to_mesh(me); b.free()
    for attr in list(me.attributes.keys()):
        if attr in ("lu", "lv", "lr", "lk", "lid", "bite"):
            me.attributes.remove(me.attributes[attr])
    me.polygons.foreach_set("use_smooth", [False] * len(me.polygons))
    for m in MATS:
        me.materials.append(m)
    ob = bpy.data.objects.new(name, me)
    scene.collection.objects.link(ob)
    ob.matrix_world = place
    return ob, centre

parts = [finish(soil_ob, "Mound")[0]]
for k in sorted(LEAF_GRID):
    num = "%02d" % (k + 1)
    leaf_ob, centre = finish(leaf_piece(k, None), "Lettuce_" + num, FRAME[k])
    # The half-eaten leaf shares the whole leaf's frame and origin, so the game can swap the mesh in place.
    half_ob, _ = finish(leaf_piece(k, 'half'), "Half_" + num, FRAME[k], centre)
    parts += [leaf_ob, half_ob, finish(leaf_piece(k, 'stub'), "Stub_" + num)[0]]
print("PARTS", len(parts), "tris", sum(len(o.data.polygons) for o in parts),
      *["%s tris %d" % (p, sum(len(o.data.polygons) for o in parts if o.name.startswith(p)))
        for p in ("Lettuce_", "Half_", "Stub_")])

for ob in list(scene.objects):                     # drop the temporary pieces
    if ob not in parts:
        me = ob.data; bpy.data.objects.remove(ob)
        if me.users == 0:
            bpy.data.meshes.remove(me)
bm_leaves.free()
# Half leaves and stubs are hidden in the game until used; hide them here too, so the file opens on the whole lettuce.
for ob in parts:
    if ob.name.startswith("Stub_") or ob.name.startswith("Half_"):
        ob.hide_set(True)

# Open with the vertex colours visible in Solid view.
for scr in bpy.data.screens:
    for area in scr.areas:
        for sp in area.spaces:
            if sp.type == 'VIEW_3D':
                sp.shading.type = 'SOLID'; sp.shading.color_type = 'VERTEX'
if OUT:
    bpy.ops.wm.save_as_mainfile(filepath=OUT)
    print("SAVED", OUT)
