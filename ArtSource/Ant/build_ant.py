# Stylised hand-painted ant (model + previews only, no export).
# Run: blender -b --factory-startup --python build_ant.py -- <blend> <previewDir>
import bpy, bmesh, math, sys
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
BLEND, PREVIEW = argv[0], argv[1]

LD = Vector((-0.45, -0.55, 0.75)).normalized()
def lin(c): return tuple((x/12.92) if x <= 0.04045 else ((x+0.055)/1.055)**2.4 for x in c)
def mix(a, b, t): t = max(0.0, min(1.0, t)); return tuple(a[i]+(b[i]-a[i])*t for i in range(3))
def mul(a, k): return tuple(max(0.0, min(1.0, x*k)) for x in a)
def hsh(v): return (math.sin(v.x*1298.98 + v.y*7823.3 + v.z*3771.9)*43758.5453) % 1.0
def Rx(a): return Matrix.Rotation(a, 3, 'X')
def Rz(a): return Matrix.Rotation(a, 3, 'Z')

# palette: warm chestnut ant that sits with the red coop / warm farm colours
SHELL, SHELL_HI, SHELL_LO = (0.74, 0.34, 0.19), (0.97, 0.62, 0.38), (0.38, 0.15, 0.09)
LEG, LEG_HI = (0.44, 0.20, 0.12), (0.70, 0.40, 0.24)
EYE, EYE_HI = (0.07, 0.05, 0.08), (0.36, 0.34, 0.46)
JAW, MOUTH = (0.40, 0.18, 0.10), (0.22, 0.08, 0.06)

def shell(base=SHELL, hi=SHELL_HI, lo=SHELL_LO, bands=None):
    """Hand-painted shading: dark belly, warm top, soft painted highlight facing the key light."""
    def fn(p, n, f):
        top = max(0.0, min(1.0, (p.z - 0.010)/0.075))
        c = mix(lo, base, 0.35 + 0.65*top)
        c = mix(c, hi, max(0.0, n.dot(LD))**2.2*0.55)
        c = mix(c, lo, max(0.0, -n.z)*0.45)
        if bands is not None: c = mul(c, bands(p))
        return c
    return fn
def eye(p, n, f): return mix(EYE, EYE_HI, max(0.0, n.dot(LD))**1.6*0.8)
flat = lambda c: (lambda p, n, f: c)

class Builder:
    def __init__(self):
        self.bm = bmesh.new(); self.cl = self.bm.loops.layers.float_color.new("Paint")
    def paint(self, faces, fn, var=0.06):
        for f in faces:
            f.normal_update(); fv = 1.0 - var/2 + var*hsh(f.calc_center_median())
            for l in f.loops:
                l[self.cl] = (*lin(mul(fn(l.vert.co, f.normal, f), fv)), 1.0)
    def ell(self, center, scale, fn, rot=None, subdiv=2, shape=None, var=0.06):
        res = bmesh.ops.create_icosphere(self.bm, subdivisions=subdiv, radius=1.0)
        for v in res['verts']:
            co = v.co.copy()
            if shape is not None: co = shape(co)
            co = Vector((co.x*scale[0], co.y*scale[1], co.z*scale[2]))
            if rot is not None: co = rot @ co
            v.co = co + Vector(center)
        faces = list({f for v in res['verts'] for f in v.link_faces}); self.paint(faces, fn, var); return faces
    def seg(self, a, b, r1, r2, fn, segs=6, var=0.04):
        a, b = Vector(a), Vector(b); d = b - a
        res = bmesh.ops.create_cone(self.bm, cap_ends=True, segments=segs, radius1=r1, radius2=r2, depth=d.length)
        q = d.normalized().to_track_quat('Z', 'Y').to_matrix()
        for v in res['verts']: v.co = q @ v.co + (a + b)/2
        faces = list({f for v in res['verts'] for f in v.link_faces}); self.paint(faces, fn, var); return faces
    def chain(self, pts, radii, fn, joint=True, segs=6):
        for i in range(len(pts) - 1):
            self.seg(pts[i], pts[i+1], radii[i], radii[i+1], fn, segs)
            if joint and 0 < i + 1 < len(pts) - 1:
                self.ell(pts[i+1], (radii[i+1]*1.25,)*3, fn, subdiv=1, var=0.0)
    def obj(self, name, coll, pivot=(0, 0, 0)):
        pv = Vector(pivot)
        for v in self.bm.verts: v.co -= pv
        me = bpy.data.meshes.new(name); self.bm.normal_update(); self.bm.to_mesh(me); self.bm.free()
        for p in me.polygons: p.use_smooth = False
        ca = me.color_attributes.get("Paint"); me.color_attributes.active_color = ca
        me.color_attributes.render_color_index = list(me.color_attributes).index(ca)
        o = bpy.data.objects.new(name, me); o.location = pv; coll.objects.link(o); return o

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
ANT = bpy.data.collections.new("Ant"); scene.collection.children.link(ANT)
FIG = bpy.data.collections.new("PreviewFigures"); scene.collection.children.link(FIG)

# ---------------------------------------------------------------- body (front faces -Y, ~15 cm long, feet on z=0)
body = Builder()
gaster_c = Vector((0, 0.052, 0.040))
def gaster_bands(p):
    t = (p.y - (gaster_c.y - 0.030))/0.016
    return 0.84 + 0.16*(0.5 + 0.5*math.cos(t*math.pi*2))
taper = lambda co: Vector((co.x*(1 - 0.22*max(0.0, -co.y)), co.y, co.z*(1 - 0.18*max(0.0, -co.y))))
body.ell(gaster_c, (0.029, 0.037, 0.026), shell(bands=gaster_bands), rot=Rx(-0.22), shape=taper, subdiv=3)   # abdomen
body.ell((0, 0.017, 0.035), (0.0065, 0.0065, 0.0095), shell(), subdiv=1)                           # petiole knob
body.ell((0, -0.008, 0.036), (0.014, 0.022, 0.0145), shell(), rot=Rx(0.28))                         # thorax
body.ell((0, -0.026, 0.042), (0.008, 0.008, 0.008), shell(), subdiv=1)                              # neck
head_c = Vector((0, -0.044, 0.047))
body.ell(head_c, (0.026, 0.023, 0.023), shell(), subdiv=3)                                       # big round head
for s in (-1, 1):
    ec = head_c + Vector((s*0.0135, -0.0150, 0.0045))
    body.ell(ec, (0.0100, 0.0085, 0.0115), eye)                                                     # glossy eye
    body.ell(ec + Vector((s*0.0022 - 0.0020, -0.0074, 0.0045)), (0.0024,)*3, flat((0.96, 0.94, 0.90)), subdiv=1, var=0.0)  # catch light
body.chain([head_c + Vector((-0.0055, -0.0208, -0.0060)), head_c + Vector((-0.0025, -0.0222, -0.0085)),
            head_c + Vector((0.0025, -0.0222, -0.0085)), head_c + Vector((0.0055, -0.0208, -0.0060))],
           [0.0010, 0.0012, 0.0012, 0.0010], flat(MOUTH), joint=False, segs=4)                     # smile
body_obj = body.obj("Ant_Body", ANT)

# ---------------------------------------------------------------- mandibles: own objects pivoting at their base so they can nibble
mandibles = []
for s, side in ((-1, "L"), (1, "R")):
    m = Builder(); base = head_c + Vector((s*0.0065, -0.0165, -0.0135))
    m.ell(base, (0.0026,)*3, flat(JAW), subdiv=1, var=0.0)
    m.chain([base, head_c + Vector((s*0.0082, -0.0228, -0.0166)), head_c + Vector((s*0.0024, -0.0262, -0.0172))],
            [0.0024, 0.0018, 0.0007], flat(JAW), joint=False, segs=5)
    mandibles.append(m.obj("Ant_Mandible_" + side, ANT, base))

# ---------------------------------------------------------------- antennae (elbowed), pivots at the head for animation
antennae = []
for s, side in ((-1, "L"), (1, "R")):
    a = Builder(); base = head_c + Vector((s*0.009, -0.010, 0.016))
    pts = [base, base + Vector((s*0.010, -0.004, 0.022)),                           # scape up
           base + Vector((s*0.016, -0.014, 0.030)), base + Vector((s*0.020, -0.026, 0.034)),
           base + Vector((s*0.022, -0.037, 0.033))]                                  # funiculus forward
    a.chain(pts, [0.0030, 0.0025, 0.0022, 0.0021, 0.0021], flat(LEG), segs=5)
    a.ell(pts[-1] + Vector((s*0.0005, -0.003, 0.0)), (0.0035, 0.0045, 0.0035), shell(LEG, LEG_HI, LEG), subdiv=1)  # club tip
    antennae.append(a.obj("Ant_Antenna_" + side, ANT, base))

# ---------------------------------------------------------------- six legs, each its own object pivoting at the hip
legs = []
LEGS = [("1", -0.019, -0.016, -0.034), ("2", -0.008, 0.000, 0.004), ("3", 0.003, 0.016, 0.044)]
legfn = shell(LEG, LEG_HI, (0.24, 0.10, 0.06))
for s, side in ((-1, "L"), (1, "R")):
    for idx, hy, ky, fy in LEGS:
        g = Builder(); hip = Vector((s*0.008, hy, 0.029))
        knee = Vector((s*0.034, ky, 0.047)); ankle = Vector((s*0.047, (ky + fy)/2 + (fy - ky)*0.25, 0.010))
        foot = Vector((s*0.060, fy, 0.0020))
        g.ell(hip, (0.0050,)*3, legfn, subdiv=1, var=0.0)
        g.chain([hip, knee, ankle, foot], [0.0042, 0.0033, 0.0024, 0.0018], legfn, segs=6)
        g.ell(foot, (0.0030, 0.0038, 0.0020), flat(SHELL_LO), subdiv=1, var=0.0)
        legs.append(g.obj("Ant_Leg_" + side + idx, ANT, hip))
for o in antennae + legs + mandibles:
    o.parent = body_obj

stats = {o.name: len(o.data.polygons) for o in ANT.objects}
print("ANT_STATS", stats, "total", sum(stats.values()))

# ---------------------------------------------------------------- ant nest: low dirt mound with a crater entrance (~0.85 m wide)
import random
NEST = bpy.data.collections.new("AntNest"); scene.collection.children.link(NEST)
NR, NH, HOLE = 0.42, 0.19, 0.05
DIRT, DIRT_TOP, DIRT_FRESH, DIRT_EDGE, HOLE_DARK = (0.52, 0.37, 0.24), (0.74, 0.57, 0.37), (0.80, 0.64, 0.43), (0.40, 0.31, 0.19), (0.10, 0.07, 0.05)
def nest_h(r, a):
    t = max(0.0, min(1.0, (r - HOLE)/(NR - HOLE)))
    z = NH*math.cos(t*math.pi/2)**1.3
    z += 0.014*math.exp(-((r - 0.078)/0.022)**2)                                   # raised crater rim of dug-out soil
    z += (0.010*math.sin(3*a + r*17) + 0.006*math.sin(7*a - r*29) + 0.004*math.sin(11*a + 1.3))*(1 - 0.7*t)*min(1.0, r/0.12)
    return z if r < NR - 1e-4 else -0.012
def dirt(p, n, f):
    r = math.hypot(p.x, p.y); t = max(0.0, min(1.0, (r - HOLE)/(NR - HOLE)))
    if p.z < NH - 0.015 and r < HOLE + 0.002:                                        # inside the entrance
        return mix(HOLE_DARK, DIRT, max(0.0, (p.z - (NH - 0.11))/0.10)*0.55)
    c = mix(DIRT_TOP, DIRT, t**0.8)
    c = mix(c, DIRT_FRESH, math.exp(-((r - 0.08)/0.035)**2)*0.7)
    c = mix(c, DIRT_EDGE, max(0.0, (t - 0.75)/0.25))
    return mul(c, 0.80 + 0.32*max(0.0, n.dot(LD)))
nb = Builder(); bm = nb.bm; SEG = 20
radii = [HOLE, 0.062, 0.078, 0.095, 0.12, 0.15, 0.185, 0.225, 0.27, 0.32, 0.37, NR]
nr = random.Random(5)
rings = []
for i, r in enumerate(radii):
    ring = []
    for k in range(SEG):
        a = (k + (0.5 if i % 2 else 0.0))/SEG*2*math.pi + nr.uniform(-0.06, 0.06)
        rr = r*(1 + (nr.uniform(-0.05, 0.05) if 0 < i < len(radii) - 1 else 0))
        ring.append(bm.verts.new((rr*math.cos(a), rr*math.sin(a), nest_h(rr, a))))
    rings.append(ring)
tube = []
for r, dz in ((0.046, 0.035), (0.040, 0.080)):
    tube.append([bm.verts.new((r*math.cos(k/SEG*2*math.pi), r*math.sin(k/SEG*2*math.pi), NH - dz)) for k in range(SEG)])
bottom = bm.verts.new((0, 0, NH - 0.11))
faces = []
def bridge(A, B, flip=False):
    for k in range(SEG):
        q = (A[k], A[(k+1) % SEG], B[(k+1) % SEG], B[k])
        faces.append(bm.faces.new(q if not flip else tuple(reversed(q))))
for i in range(len(rings) - 1): bridge(rings[i], rings[i+1], flip=True)
bridge(tube[0], rings[0], flip=True); bridge(tube[1], tube[0], flip=True)
for k in range(SEG): faces.append(bm.faces.new((tube[1][(k+1) % SEG], tube[1][k], bottom)))
bmesh.ops.recalc_face_normals(bm, faces=faces)
for f in faces:
    if f.calc_center_median().z > 0.0 and f.normal.z < -0.2 and math.hypot(*f.calc_center_median().xy) > HOLE + 0.01: f.normal_flip()
nb.paint(faces, dirt, var=0.16)
def on_mound(r, a, sink=0.004): return Vector((r*math.cos(a), r*math.sin(a), nest_h(r, a) - sink))
pebble = lambda c: (lambda p, n, f: mul(c, 0.78 + 0.35*max(0.0, n.dot(LD))))
for _ in range(16):                                                                   # pebbles
    r, a, s = nr.uniform(0.12, 0.40), nr.uniform(0, 2*math.pi), nr.uniform(0.010, 0.022)
    nb.ell(on_mound(r, a, s*0.35), (s, s*nr.uniform(0.8, 1.2), s*0.7), pebble(mix((0.62, 0.60, 0.56), (0.70, 0.62, 0.50), nr.random())), subdiv=1, var=0.10)
for _ in range(22):                                                                   # dug-out soil crumbs round the rim
    r, a, s = nr.uniform(0.065, 0.13), nr.uniform(0, 2*math.pi), nr.uniform(0.005, 0.009)
    nb.ell(on_mound(r, a, s*0.3), (s, s, s*0.8), pebble(DIRT_FRESH), subdiv=1, var=0.12)
for _ in range(8):                                                                    # twigs lying on the slope
    r, a = nr.uniform(0.16, 0.36), nr.uniform(0, 2*math.pi); L = nr.uniform(0.06, 0.12); ta = a + math.pi/2 + nr.uniform(-0.6, 0.6)
    p0 = on_mound(r, a, -0.003); d = Vector((math.cos(ta), math.sin(ta), 0))*L/2
    p1 = on_mound(math.hypot(*(p0 + d).xy), math.atan2((p0 + d).y, (p0 + d).x), -0.004)
    p2 = on_mound(math.hypot(*(p0 - d).xy), math.atan2((p0 - d).y, (p0 - d).x), -0.004)
    nb.seg(p2, p1, 0.0035, 0.0025, pebble((0.42, 0.28, 0.17)), segs=5)
for _ in range(14):                                                                   # straw / pine-needle bits
    r, a = nr.uniform(0.10, 0.38), nr.uniform(0, 2*math.pi); ta = nr.uniform(0, 2*math.pi); L = nr.uniform(0.04, 0.07)
    p0 = on_mound(r, a, -0.002); d = Vector((math.cos(ta), math.sin(ta), 0))*L/2
    nb.seg(p0 - d + Vector((0, 0, -0.002)), p0 + d + Vector((0, 0, 0.004)), 0.0018, 0.0012, pebble((0.86, 0.70, 0.38)), segs=4)
nest_obj = nb.obj("AntNest", NEST)
print("NEST_STATS", len(nest_obj.data.polygons))

# ---------------------------------------------------------------- bush on the ant trail: a dense leafy dome down to the ground
# (~1.5 m wide, 0.8 m tall) that the trail runs through, so the player never sees the ants pick up their wheat inside.
BUSH = bpy.data.collections.new("AntBush"); scene.collection.children.link(BUSH)
LEAF_DARK, LEAF, LEAF_HI, LEAF_WARM = (0.20, 0.36, 0.13), (0.38, 0.60, 0.22), (0.68, 0.84, 0.38), (0.56, 0.68, 0.26)
lumpy = lambda co: co*(1.0 + 0.16*(hsh(co*3.7) - 0.5))
br = random.Random(23)
def leaves(top):
    def fn(p, n, f):
        t = max(0.0, min(1.0, p.z/top))
        c = mix(LEAF_DARK, LEAF, 0.25 + 0.75*t)
        c = mix(c, LEAF_HI, max(0.0, n.dot(LD))**1.6*0.6*(0.4 + 0.6*t))
        c = mix(c, LEAF_WARM, 0.35*hsh(f.calc_center_median()*3.1))
        return mul(c, 1.0 - 0.18*max(0.0, -n.z))
    return fn
bb = Builder(); BTOP = 0.82
bb.ell((0, 0, 0.36), (0.56, 0.50, 0.42), leaves(BTOP), subdiv=3, var=0.20, shape=lumpy)
for k in range(9):                                                               # skirt of clumps resting on the ground
    a = k/9*2*math.pi + br.uniform(-0.15, 0.15); r = br.uniform(0.50, 0.60)
    s = br.uniform(0.24, 0.32)
    bb.ell((r*math.cos(a), r*math.sin(a)*0.9, s*0.55), (s*1.1, s*1.0, s*0.85), leaves(BTOP), subdiv=2, var=0.22, shape=lumpy)
for k in range(6):                                                               # crown clumps
    a = k/6*2*math.pi + br.uniform(-0.3, 0.3); r = br.uniform(0.15, 0.34)
    s = br.uniform(0.20, 0.27)
    bb.ell((r*math.cos(a), r*math.sin(a), 0.52 + br.uniform(0.0, 0.10)), (s, s, s*0.85), leaves(BTOP), subdiv=2, var=0.22, shape=lumpy)
bb.ell((0.04, -0.02, 0.70), (0.22, 0.20, 0.14), leaves(BTOP), subdiv=2, var=0.22)
from mathutils.bvhtree import BVHTree
foliage = BVHTree.FromBMesh(bb.bm)
for _ in range(16):                                                              # little cream flowers sitting on the leaves
    a = br.uniform(0, 2*math.pi); el = br.uniform(0.15, 1.3)
    out = Vector((math.cos(a)*math.cos(el), math.sin(a)*math.cos(el), math.sin(el)))
    hit, normal, _, _ = foliage.ray_cast(Vector((0, 0, 0.30)) + out*2.0, -out)
    if hit is None: continue
    rot = Vector((0, 0, 1)).rotation_difference(normal).to_matrix()
    for k in range(5):                                                           # five petals round a yellow eye
        pa = k/5*2*math.pi + a
        bb.ell(hit + rot @ Vector((math.cos(pa)*0.016, math.sin(pa)*0.016, 0.004)), (0.013, 0.008, 0.004),
               flat((0.98, 0.95, 0.84)), rot=rot @ Matrix.Rotation(pa, 3, 'Z'), subdiv=1, var=0.04)
    bb.ell(hit + rot @ Vector((0, 0, 0.007)), (0.008, 0.008, 0.005), flat((0.97, 0.76, 0.20)), rot=rot, subdiv=1, var=0.0)
bush_obj = bb.obj("AntBush", BUSH)
print("BUSH_STATS", len(bush_obj.data.polygons))

# ---------------------------------------------------------------- scale figure (preview only): chick 0.25 m
fig = Builder()
yel = lambda p, n, f: mix((0.86, 0.66, 0.18), (1.0, 0.90, 0.45), 0.4 + 0.6*max(0.0, n.dot(LD)))
fig.ell((0.30, 0.02, 0.10), (0.10, 0.11, 0.10), yel, var=0.0)
fig.ell((0.30, -0.05, 0.20), (0.065, 0.065, 0.065), yel, var=0.0)
fig.ell((0.30, -0.118, 0.20), (0.016, 0.022, 0.012), flat((0.96, 0.60, 0.16)), subdiv=1, var=0.0)
fig.obj("PreviewChick", FIG)

bpy.ops.wm.save_as_mainfile(filepath=BLEND)

# ---------------------------------------------------------------- previews
scene.render.engine = 'BLENDER_WORKBENCH'
sh = scene.display.shading; sh.light = 'STUDIO'; sh.color_type = 'VERTEX'; sh.show_shadows = True
sh.show_specular_highlight = False; sh.show_cavity = True; sh.cavity_type = 'WORLD'; sh.background_type = 'VIEWPORT'; sh.background_color = (0.72, 0.83, 0.92)
gm = bpy.data.meshes.new("PreviewGround"); gb = bmesh.new(); bmesh.ops.create_grid(gb, x_segments=1, y_segments=1, size=3.0)
gb.to_mesh(gm); gb.free(); gnd = bpy.data.objects.new("PreviewGround", gm); scene.collection.objects.link(gnd)
gc = gm.color_attributes.new("Paint", 'FLOAT_COLOR', 'POINT')
for d in gc.data: d.color = (*lin((0.50, 0.62, 0.32)), 1)
cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); scene.collection.objects.link(cam); scene.camera = cam
cam.data.clip_start = 0.001
def shoot(name, target, dist, elev, az, chick=False, lens=50, res=(1400, 1000)):
    FIG.hide_render = not chick
    scene.render.resolution_x, scene.render.resolution_y = res; cam.data.lens = lens
    d = Vector((math.cos(math.radians(elev))*math.sin(math.radians(az)), -math.cos(math.radians(elev))*math.cos(math.radians(az)), math.sin(math.radians(elev))))
    cam.location = Vector(target) + d*dist; cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    scene.render.filepath = PREVIEW + "/" + name; bpy.ops.render.render(write_still=True)
TRAIL = bpy.data.collections.new("PreviewTrail"); scene.collection.children.link(TRAIL)
def ant_copy(pos, yaw, pitch=0.0):
    root = None
    for o in [body_obj] + antennae + legs + mandibles:
        c = o.copy(); TRAIL.objects.link(c)
        if o is body_obj:
            root = c; c.location = pos; c.rotation_euler = (pitch, 0, yaw)
        else:
            c.parent = root
    return root
# ants walking single file up the mound towards the entrance (they face -Y, so yaw turns -Y towards the target)
for k, r in enumerate((0.13, 0.31, 0.50, 0.69, 0.88)):
    a = math.radians(-20); p = Vector((r*math.cos(a), r*math.sin(a), 0))
    z = nest_h(r, a) if r < NR else 0.0; slope = (nest_h(r - 0.02, a) - nest_h(r + 0.02, a))/0.04 if r < NR else 0.0
    ant_copy(p + Vector((0, 0, z)), a - math.pi/2, -math.atan(slope))
def show(nest, ant, trail, bush=False):
    NEST.hide_render = not nest; ANT.hide_render = not ant; TRAIL.hide_render = not trail; BUSH.hide_render = not bush
show(False, True, False)
shoot("ant_front34.png", (0, 0.005, 0.04), 0.36, 22, -35)
show(True, False, False)
shoot("nest_34.png", (0, 0, 0.06), 1.5, 30, -30)
shoot("nest_top.png", (0, 0, 0.0), 1.4, 75, -10)
show(True, False, True)
shoot("nest_trail.png", (0.30, -0.08, 0.06), 1.35, 24, 35)
shoot("nest_close.png", (0.10, -0.03, 0.15), 0.50, 30, 30)
show(False, False, False, True)
shoot("bush_34.png", (0, 0, 0.35), 3.2, 20, -30)
shoot("bush_low.png", (0, 0, 0.25), 2.6, 6, 60)
print("ANT_DONE")
