# Hand-painted tobacco pipe for the old farmer (same painted style as the ants and FarmBarn): one curved wooden stem
# with a dark mouthpiece running into a rounded bowl with glowing tobacco. Pivot at the mouthpiece tip, stem pointing forward (-Y here,
# +Z in Unity), bowl rising up; an empty "SmokePoint" sits on the bowl for the smoke.
# Run: blender -b --factory-startup --python build_pipe.py -- <assetDir> [previewDir]
import bpy, bmesh, math, os, sys
import numpy as np
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
ASSET_DIR = argv[0]; PREVIEW = argv[1] if len(argv) > 1 else None
TEX_DIR = os.path.join(ASSET_DIR, "Textures"); os.makedirs(TEX_DIR, exist_ok=True)
LD = Vector((-0.45, -0.55, 0.75)).normalized()
def lin(c): return tuple((x/12.92) if x <= 0.04045 else ((x+0.055)/1.055)**2.4 for x in c)
def mix(a, b, t): t = max(0.0, min(1.0, t)); return tuple(a[i]+(b[i]-a[i])*t for i in range(3))
def hsh(v): return (math.sin(v.x*1298.98 + v.y*7823.3 + v.z*3771.9)*43758.5453) % 1.0

WOOD, WOOD_LO, WOOD_HI = (0.55, 0.30, 0.16), (0.30, 0.14, 0.10), (0.86, 0.54, 0.30)   # warm briar, purple-ish shadow
BIT, BIT_HI = (0.13, 0.10, 0.10), (0.42, 0.36, 0.40)
TOBACCO, EMBER = (0.24, 0.13, 0.08), (1.0, 0.52, 0.16)
def painted(base, lo, hi):
    """Lit top, shaded underside, warm highlight towards the light, as on the other hand-painted props."""
    def fn(p, n):
        c = mix(lo, base, 0.45 + 0.55*max(0.0, n.z*0.5 + 0.5))
        return mix(c, hi, max(0.0, n.dot(LD))**2*0.6)
    return fn

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
bm = bmesh.new(); col = bm.loops.layers.float_color.new("Paint")
def paint(faces, fn, var=0.06):
    for f in faces:
        f.normal_update(); k = 1.0 - var/2 + var*hsh(f.calc_center_median())
        for l in f.loops: l[col] = (*lin(tuple(min(1.0, x*k) for x in fn(l.vert.co, f.normal))), 1.0)

# Stem: one continuous swept tube from the mouthpiece tip (origin) forward (-Y), bending gently down into the bowl;
# its end sits inside the bowl wall so the two read as one piece. Rings are shared, so there are no seams.
SEGS = 12
BOWL_C = Vector((0, -0.128, -0.036))                     # centre of the bowl's foot
def stem_point(t): return Vector((0, -0.118*t, -0.022*t**1.7))   # enters the bowl a third of the way up
def stem_radius(t):
    if t < 0.20: return 0.0036 + 0.0012*t/0.20          # mouthpiece, flaring slightly
    if t < 0.24: return 0.0062                           # bone-coloured band
    return 0.0056 + 0.0014*(t - 0.24)/0.76
def stem_colour(t, n):
    if t < 0.20: return painted(BIT, BIT, BIT_HI)(None, n)
    if t < 0.24: return painted(WOOD_HI, WOOD, (1.0, 0.8, 0.55))(None, n)
    return painted(WOOD, WOOD_LO, WOOD_HI)(None, n)
ts = [i/40 for i in range(41)] + [0.1999, 0.2001, 0.2399, 0.2401]
ts = sorted(set(ts))
rings = []
for t in ts:
    p = stem_point(t)
    tangent = (stem_point(min(1.0, t + 0.01)) - stem_point(max(0.0, t - 0.01))).normalized()
    side = Vector((1, 0, 0)); up = tangent.cross(side).normalized()
    r = stem_radius(t)
    rings.append((t, [bm.verts.new(p + (side*math.cos(a) + up*math.sin(a))*r)
                      for a in (k/SEGS*2*math.pi for k in range(SEGS))]))
stem_faces = []
for (t0, a), (t1, b) in zip(rings, rings[1:]):
    for k in range(SEGS):
        f = bm.faces.new((a[k], a[(k+1) % SEGS], b[(k+1) % SEGS], b[k]))
        f.normal_update(); stem_faces.append((f, (t0 + t1)/2))
tip = bm.faces.new(list(reversed(rings[0][1]))); stem_faces.append((tip, 0.0))
bmesh.ops.recalc_face_normals(bm, faces=[f for f, _ in stem_faces])
for f, t in stem_faces:
    k = 1.0 - 0.03 + 0.06*hsh(f.calc_center_median())
    for l in f.loops: l[col] = (*lin(tuple(min(1.0, x*k) for x in stem_colour(t, f.normal))), 1.0)

# Bowl: a lathe with a rounded heel, a slight belly and a rim, the tobacco sunk just below the rim.
PROFILE = [(0.000, 0.0000), (0.003, 0.0080), (0.008, 0.0126), (0.016, 0.0152), (0.026, 0.0160),
           (0.036, 0.0157), (0.044, 0.0150)]                  # (height, outer radius)
RIM_IN, TOBACCO_Z = 0.0118, 0.039
BSEGS = 16
def ring(z, r):
    return [bm.verts.new(BOWL_C + Vector((math.cos(a)*r, math.sin(a)*r, z))) for a in (k/BSEGS*2*math.pi for k in range(BSEGS))]
outer = [ring(z, r) if r > 0 else [bm.verts.new(BOWL_C)] for z, r in PROFILE]
inner_top, inner_low = ring(PROFILE[-1][0], RIM_IN), ring(TOBACCO_Z, RIM_IN*0.97)
bowl_faces, rim_faces, tobacco_faces = [], [], []
for k in range(BSEGS):                                        # heel fan
    bowl_faces.append(bm.faces.new((outer[0][0], outer[1][(k+1) % BSEGS], outer[1][k])))
for a, b in zip(outer[1:], outer[2:]):
    for k in range(BSEGS): bowl_faces.append(bm.faces.new((a[k], a[(k+1) % BSEGS], b[(k+1) % BSEGS], b[k])))
for k in range(BSEGS):
    rim_faces.append(bm.faces.new((outer[-1][k], outer[-1][(k+1) % BSEGS], inner_top[(k+1) % BSEGS], inner_top[k])))
    rim_faces.append(bm.faces.new((inner_top[k], inner_top[(k+1) % BSEGS], inner_low[(k+1) % BSEGS], inner_low[k])))
centre = bm.verts.new(BOWL_C + Vector((0, 0, TOBACCO_Z - 0.001)))
for k in range(BSEGS): tobacco_faces.append(bm.faces.new((inner_low[k], inner_low[(k+1) % BSEGS], centre)))
for faces in (bowl_faces, rim_faces, tobacco_faces):
    for f in faces: f.normal_update()
# Outside of the bowl faces out, the rim and inside face in/up, the tobacco faces up.
for f in bowl_faces:
    if f.normal.dot(f.calc_center_median() - (BOWL_C + Vector((0, 0, 0.02)))) < 0: f.normal_flip()
for f in tobacco_faces:
    if f.normal.z < 0: f.normal_flip()
for f in rim_faces:
    c = f.calc_center_median()
    if abs(c.z - PROFILE[-1][0]) < 1e-4 and f.normal.z < 0: f.normal_flip()
    elif abs(c.z - PROFILE[-1][0]) >= 1e-4 and f.normal.dot(Vector((BOWL_C.x, BOWL_C.y, c.z)) - c) < 0: f.normal_flip()
paint(bowl_faces, painted(WOOD, WOOD_LO, WOOD_HI))
paint(rim_faces, painted(WOOD_HI, WOOD, (1.0, 0.75, 0.5)))
bowl_top = BOWL_C + Vector((0, 0, TOBACCO_Z))
def tobacco(p, n):
    r = math.hypot(p.x - bowl_top.x, p.y - bowl_top.y)
    t = max(0.0, min(1.0, (r - 0.002)/0.009)); return mix(EMBER, TOBACCO, t*t*(3 - 2*t))   # glowing centre
paint(tobacco_faces, tobacco, var=0.0)


me = bpy.data.meshes.new("FarmerPipe"); bm.normal_update(); bm.to_mesh(me); bm.free()
for p in me.polygons: p.use_smooth = False
ca = me.color_attributes.get("Paint"); me.color_attributes.active_color = ca
pipe = bpy.data.objects.new("FarmerPipe", me); scene.collection.objects.link(pipe)
smoke = bpy.data.objects.new("SmokePoint", None); scene.collection.objects.link(smoke)
smoke.parent = pipe; smoke.location = bowl_top + Vector((0, 0, 0.008))
print("PIPE", len(me.polygons), "faces, length %.3f m" % pipe.dimensions.y)

# ---------------------------------------------------------------- bake to a small atlas and export
bpy.ops.object.select_all(action='DESELECT'); pipe.select_set(True); bpy.context.view_layer.objects.active = pipe
me.uv_layers.new(name="UVMap")
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=1.45, island_margin=0.02, area_weight=0.0, scale_to_bounds=False)
bpy.ops.uv.pack_islands(margin=0.02, rotate=True)
bpy.ops.object.mode_set(mode='OBJECT')
img = bpy.data.images.new("T_FarmerPipe_Albedo", 256, 256, alpha=False)
m = bpy.data.materials.new("PipePaint"); N = m.node_tree.nodes
vc = N.new("ShaderNodeVertexColor"); vc.layer_name = "Paint"
m.node_tree.links.new(vc.outputs["Color"], N["Principled BSDF"].inputs["Base Color"])
t = N.new("ShaderNodeTexImage"); t.image = img
for x in N: x.select = False
t.select = True; N.active = t
me.materials.append(m)
scene.render.engine = 'CYCLES'; scene.cycles.device = 'CPU'; scene.cycles.samples = 4
bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'}, margin=16, use_clear=True)
px = np.array(img.pixels[:], dtype=np.float32).reshape(256, 256, 4)
filled = px[:, :, :3].sum(axis=2) > 1e-5
for _ in range(64):
    if filled.all(): break
    acc = np.zeros((256, 256, 3), np.float32); cnt = np.zeros((256, 256), np.float32)
    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        mk = np.roll(filled, (dy, dx), axis=(0, 1)); acc += np.roll(px[:, :, :3], (dy, dx), axis=(0, 1))*mk[:, :, None]; cnt += mk
    grow = (~filled) & (cnt > 0); px[grow, :3] = acc[grow]/cnt[grow][:, None]; filled |= grow
px[:, :, 3] = 1.0; img.pixels[:] = px.ravel()
img.filepath_raw = os.path.join(TEX_DIR, "T_FarmerPipe_Albedo.png"); img.file_format = 'PNG'; img.save()
final = bpy.data.materials.new("M_FarmerPipe"); ft = final.node_tree.nodes.new("ShaderNodeTexImage"); ft.image = img
final.node_tree.links.new(ft.outputs["Color"], final.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
me.materials.clear(); me.materials.append(final)
bpy.ops.object.select_all(action='DESELECT'); pipe.select_set(True); smoke.select_set(True)
bpy.context.view_layer.objects.active = pipe
bpy.ops.export_scene.fbx(filepath=os.path.join(ASSET_DIR, "FarmerPipe.fbx"), use_selection=True, object_types={'MESH', 'EMPTY'},
    global_scale=1.0, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
    bake_space_transform=True, mesh_smooth_type='FACE', colors_type='NONE', add_leaf_bones=False, bake_anim=False,
    path_mode='STRIP', embed_textures=False)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(os.path.dirname(os.path.abspath(__file__)), "FarmerPipe.blend"))
print("EXPORTED")

if PREVIEW:
    scene.render.engine = 'BLENDER_WORKBENCH'
    sh = scene.display.shading; sh.light = 'FLAT'; sh.color_type = 'TEXTURE'
    scene.render.resolution_x, scene.render.resolution_y = 900, 600
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); scene.collection.objects.link(cam); scene.camera = cam
    cam.data.clip_start = 0.001
    for name, az, el in (("pipe_side.png", 90, 10), ("pipe_34.png", -40, 25)):
        d = Vector((math.cos(math.radians(el))*math.sin(math.radians(az)), -math.cos(math.radians(el))*math.cos(math.radians(az)), math.sin(math.radians(el))))
        target = Vector((0, -0.07, 0)); cam.location = target + d*0.28; cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = os.path.join(PREVIEW, name); bpy.ops.render.render(write_still=True)
