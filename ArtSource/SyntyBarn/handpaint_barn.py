# Repaints Synty's SM_Bld_Barn_01 in the game's hand-painted style (chicken_coop_handpainted / FarmBarn) without
# touching its shape: every vertex, face and pivot is kept; only new UVs and new albedo textures are made, plus
# SM_Bld_Barn_01_Interior: bare-wood backs for the one-sided walls and roof and a floor for the hall.
# Run: blender -b --factory-startup --python handpaint_barn.py -- <SM_Bld_Barn_01.fbx> <PolygonFarm_01_A.png> <assetDir> [previewDir]
import bpy, bmesh, math, os, sys
import numpy as np
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
FBX, ATLAS, ASSET_DIR = argv[0], argv[1], argv[2]
PREVIEW = argv[3] if len(argv) > 3 else None
NAME = "SM_Bld_Barn_01_HandPainted"
TEX_SIZE = 4096
TEX_DIR = os.path.join(ASSET_DIR, "Textures"); os.makedirs(TEX_DIR, exist_ok=True)

# Child pivots as Synty's prefab has them, in Unity space (x, y, z). Blender's FBX importer bakes the FBX pivots into
# the meshes, so they are restored here to keep the parts exactly where the original prefab puts them.
UNITY_PIVOTS = {
    "SM_Bld_Barn_01_Door_01": (5.8309684, -0.000011608123, 0.9491305),
    "SM_Bld_Barn_01_Door_02": (5.8309684, -0.000011608123, -0.94508284),
    "SM_Bld_Barn_01_Door_03": (-5.8266516, -0.000011608123, 0.9491305),
    "SM_Bld_Barn_01_Door_04": (-5.8266516, -0.000011608123, -0.94508284),
    "SM_Bld_Barn_01_Shutter_01": (5.7360983, 5.179363, 0.70615774),
    "SM_Bld_Barn_01_Shutter_02": (-5.7585826, 5.179363, -0.6853938),
}
def unity_to_blender(p): return Vector((-p[0], -p[2], p[1]))

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
bpy.ops.import_scene.fbx(filepath=FBX)
objs = [o for o in bpy.data.objects if o.type == 'MESH']
root = next(o for o in objs if o.parent is None)
parts = [o for o in objs if o is not root]

# Bake every transform into the meshes (the root carries the cm->m scale and the Y-up rotation), then give the
# children their original pivots back.
for o in objs:
    o.data.transform(o.matrix_world)
for o in objs:
    o.parent = None
    o.matrix_world = Matrix.Identity(4)
for o in parts:
    pivot = unity_to_blender(UNITY_PIVOTS[o.name])
    o.data.transform(Matrix.Translation(-pivot))
    o.location = pivot
    o.parent = root
for o in objs: o.data.update()

# ---------------------------------------------------------------- empty side room
# The side room off the hall (Synty's -Y lean-to) is kept empty for the chick's upgrade stand: the two posts on the
# hall's open side and the log rails running from them to the outer wall (stall dividers) are removed, from the model
# and from the collision mesh, which is exported with the barn as SM_Bld_Barn_01_Collision.
def in_side_room(mn, mx):
    c = (mn + mx)/2
    return 1.4 < abs(c.x) < 2.2 and -6.3 < c.y < -3.0 and mx.z < 5.0   # not the trim posts on the outer wall (y -6.5)
def drop_parts(me, matrix, test):
    bm = bmesh.new(); bm.from_mesh(me)
    seen, doomed = set(), []
    for f in bm.faces:
        if f.index in seen: continue
        comp, stack = [], [f]; seen.add(f.index)
        while stack:
            g = stack.pop(); comp.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in seen: seen.add(h.index); stack.append(h)
        vs = [matrix @ v.co for g in comp for v in g.verts]
        mn = Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs)))
        mx = Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
        if test(mn, mx): doomed.extend(comp)
    bmesh.ops.delete(bm, geom=list({v for f in doomed for v in f.verts}), context='VERTS')
    bm.to_mesh(me); bm.free(); me.update()
    return len(doomed)
print("REMOVED_FACES", drop_parts(root.data, root.matrix_world, in_side_room))

col_path = os.path.join(os.path.dirname(FBX), "Collision", "SM_Bld_Barn_01_Collision.fbx")
before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=col_path)
collision = next(o for o in bpy.data.objects if o not in before and o.type == 'MESH')
for o in [x for x in bpy.data.objects if x not in before and x is not collision]: bpy.data.objects.remove(o)
collision.data.transform(collision.matrix_world); collision.parent = None; collision.matrix_world = Matrix.Identity(4)
collision.name = collision.data.name = "SM_Bld_Barn_01_Collision"
collision.data.materials.clear()
print("REMOVED_COLLISION_FACES", drop_parts(collision.data, collision.matrix_world, in_side_room))
collision.parent = root
collision.hide_render = True   # exported for the collider only; never baked onto or rendered

# ---------------------------------------------------------------- paint style
# Same look as chicken_coop_handpainted and the earlier FarmBarn (ArtSource/FarmBarn/barn_paint.py): broad soft brush
# strokes, painted light (lit top edge, shaded lower edge on every board and shingle), wobbly dark outlines, shadows
# leaning purple and highlights orange, no wood grain or cracks. Each Synty face is sorted by the atlas colour it used
# (red walls, white trim, grey roof...) and repainted from that part's (shadow, base, highlight) swatch.
def srgb(c): return tuple(((x/255)/12.92) if x/255 <= 0.04045 else ((x/255 + 0.055)/1.055)**2.4 for x in c)
PALETTE = {                                                    # shadow, base, highlight (sRGB 0-255, FarmBarn swatches)
    "red":    ((116, 30, 46), (196, 58, 48), (236, 108, 74)),
    "cream":  ((168, 146, 146), (236, 222, 196), (253, 247, 230)),
    "roof":   ((80, 42, 44), (142, 78, 52), (198, 124, 74)),
    "dark":   ((40, 28, 34), (86, 56, 44), (132, 92, 64)),
    "metal":  ((34, 36, 52), (70, 72, 90), (132, 136, 152)),
    "wood":   ((92, 50, 40), (158, 98, 60), (206, 148, 92)),
    "lining": ((138, 98, 78), (208, 160, 108), (238, 202, 144)),
    "rafter": ((92, 56, 50), (150, 100, 68), (196, 146, 98)),
    "floor":  ((118, 80, 62), (186, 134, 88), (226, 182, 122)),
}
PALETTE = {k: tuple(srgb(c) for c in v) for k, v in PALETTE.items()}
BOARDED = {"red", "lining"}                                     # parts that get painted vertical board seams

atlas_img = bpy.data.images.load(ATLAS)
AW, AH = atlas_img.size
atlas_px = np.array(atlas_img.pixels[:], dtype=np.float32).reshape(AH, AW, 4)
def classify(c):
    """Part of the barn from the (linear) Synty atlas colour a face uses."""
    r, g, b = c
    lum = 0.3*r + 0.59*g + 0.11*b
    if r > 1.8*g and r > 0.3: return "red"
    if min(r, g, b) > 0.48: return "cream"
    if abs(r - g) < 0.03 and abs(g - b) < 0.03: return "dark" if lum < 0.22 else "metal"
    if r > g > b + 0.05: return "wood"
    return "roof"

def surface_groups(me, categories, world):
    """Faces joined into flat surfaces: neighbours (sharing an edge, matched by position so loose duplicate vertices
    still join) that face the same way and are the same part. A whole wall is one surface, each shingle its own."""
    def key(i): return tuple(round(c*1000) for c in (world @ me.vertices[i].co))
    normals = [(world.to_3x3() @ p.normal).normalized() for p in me.polygons]
    parent = list(range(len(me.polygons)))
    def find(i):
        while parent[i] != i: parent[i] = parent[parent[i]]; i = parent[i]
        return i
    edges = {}
    for p in me.polygons:
        vs = [key(v) for v in p.vertices]
        for a, b in zip(vs, vs[1:] + vs[:1]):
            edges.setdefault(frozenset((a, b)), []).append(p.index)
    for faces in edges.values():
        for i in faces[1:]:
            f = faces[0]
            if categories[f] == categories[i] and normals[f].dot(normals[i]) > 0.97: parent[find(i)] = find(f)
    return [find(i) for i in range(len(me.polygons))], normals

def paint_face_attrs(me, categories, world):
    """Per-corner colour attributes for the bake: the part's swatch (Shadow/Base/High) and Grad = (height across the
    surface 0..1, position along the wall for board seams, seams on/off). The height runs across the whole flat
    surface a face belongs to, not the face alone: per-face gradients broke at every triangle edge into jagged wedges."""
    groups, normals = surface_groups(me, categories, world)
    span = {}
    for p, g in zip(me.polygons, groups):
        for v in p.vertices:
            z = (world @ me.vertices[v].co).z
            lo, hi_z = span.get(g, (z, z)); span[g] = (min(lo, z), max(hi_z, z))
    layers = {n: me.color_attributes.new(n, 'FLOAT_COLOR', 'CORNER') for n in ("Shadow", "Base", "High", "Grad")}
    for p, cat, g, n in zip(me.polygons, categories, groups, normals):
        sh, ba, hi = PALETTE[cat]
        z0, z1 = span[g]
        for li in p.loop_indices:
            co = world @ me.vertices[me.loops[li].vertex_index].co
            t = 0.72 if z1 - z0 < 0.02 else (co.z - z0)/(z1 - z0)
            along = co.y if abs(n.x) > abs(n.y) else co.x
            seams = 1.0 if cat in BOARDED and abs(n.z) < 0.3 else 0.0
            layers["Shadow"].data[li].color = (*sh, 1); layers["Base"].data[li].color = (*ba, 1)
            shingled = 1.0 if cat == "roof" and n.z > 0.35 else 0.0          # painted shingle rows (alpha)
            layers["High"].data[li].color = (*hi, 1); layers["Grad"].data[li].color = (t, along, seams, shingled)

outside_cats = {}
for o in objs:
    me = o.data; uv = me.uv_layers[0].data; cats = []
    for p in me.polygons:
        u = sum(uv[i].uv.x for i in p.loop_indices)/p.loop_total
        v = sum(uv[i].uv.y for i in p.loop_indices)/p.loop_total
        cat = classify(atlas_px[min(AH - 1, max(0, int(v*AH))), min(AW - 1, max(0, int((u % 1.0)*AW))), :3])
        # Synty's roof mixes grey swatches with the brown ones; any grey or brown face up on the roofs is roof.
        n = (o.matrix_world.to_3x3() @ p.normal).normalized()
        if cat in ("metal", "dark", "roof", "wood") and n.z > 0.35 and (o.matrix_world @ p.center).z > 2.5: cat = "roof"
        cats.append(cat)
    outside_cats[o.name] = cats
    paint_face_attrs(me, cats, o.matrix_world)
from collections import Counter
print("PARTS", {k: v for k, v in Counter(c for cs in outside_cats.values() for c in cs).items()})

# ---------------------------------------------------------------- shadow roof
# Synty's roof is rows of separate, overlapping shingle pieces. Unity's shadow pass pushes every face in along its
# normal (normal bias), which opens thin slits between the rows, and the sun falls through them onto the floor in
# stripes. SM_Bld_Barn_01_ShadowRoof is one unbroken sheet following the roof just under the shingles; it is only ever
# drawn into the shadow map (set up on import), so the inside stays shaded under the whole roof.
from mathutils.bvhtree import BVHTree
roof_bm = bmesh.new(); roof_bm.from_mesh(root.data)
bmesh.ops.delete(roof_bm, geom=[f for f, c in zip(roof_bm.faces, outside_cats[root.name]) if c != "roof"], context='FACES')
roof_tree = BVHTree.FromBMesh(roof_bm); roof_bm.free()
STEP = 0.2
gx = [-7.0 + i*STEP for i in range(int(14.0/STEP) + 1)]
gy = [-7.4 + j*STEP for j in range(int(14.8/STEP) + 1)]
heights = {}
for i, x in enumerate(gx):
    for j, y in enumerate(gy):
        hit = roof_tree.ray_cast(Vector((x, y, 20.0)), Vector((0, 0, -1)), 30.0)
        if hit[0] is not None: heights[i, j] = hit[0].z - 0.05
sb = bmesh.new(); verts = {}
def sv(i, j):
    if (i, j) not in verts: verts[i, j] = sb.verts.new((gx[i], gy[j], heights[i, j]))
    return verts[i, j]
for i in range(len(gx) - 1):
    for j in range(len(gy) - 1):
        if all(k in heights for k in ((i, j), (i+1, j), (i+1, j+1), (i, j+1))):
            f = sb.faces.new((sv(i, j), sv(i+1, j), sv(i+1, j+1), sv(i, j+1)))
            f.normal_update()
            if f.normal.z < 0: f.normal_flip()           # faces the sun
sme = bpy.data.meshes.new("SM_Bld_Barn_01_ShadowRoof"); sb.to_mesh(sme); sb.free()
shadow_roof = bpy.data.objects.new("SM_Bld_Barn_01_ShadowRoof", sme); scene.collection.objects.link(shadow_roof)
shadow_roof.parent = root
shadow_roof.hide_render = True    # never baked onto or rendered here; Unity draws it into the shadow map only
print("SHADOW_ROOF", len(sme.polygons), "cells")

# ---------------------------------------------------------------- inside faces
# The Synty barn is a shell of one-sided faces: from inside you would look straight through its walls and roof. Every
# face of the building that looks away from the middle of the hall gets a twin facing the other way, a hair further in,
# painted as bare wood. The twins only exist where Synty's walls do, so its windows, doorways and the side room stay
# open exactly as built. A packed-earth floor covers the main hall. The outside is untouched.
HALL_X, HALL_Y = 5.50, 3.18                              # floor of the main hall (inside of its walls, collision mesh)
AXIS_X, AXIS_Z = 4.0, 3.0                                # the hall's middle line, for deciding what faces "out"
INSET = 0.012
ib = bmesh.new(); ib_cats = []
src = bmesh.new(); src.from_mesh(root.data); src.normal_update()
for f, cat in zip(src.faces, outside_cats[root.name]):
    if f.calc_area() < 1e-6: continue
    c = f.calc_center_median(); n = f.normal
    away = c - Vector((max(-AXIS_X, min(AXIS_X, c.x)), 0.0, AXIS_Z))
    if away.length < 1e-4 or n.dot(away.normalized()) < 0.2: continue
    ib.faces.new([ib.verts.new(v.co - n*INSET) for v in reversed(f.verts)])
    ib_cats.append("rafter" if cat == "roof" or n.z > 0.45 else ("cream" if cat == "cream" else "lining"))
src.free()
twins = len(ib_cats)
fx = [-HALL_X + i*0.4 for i in range(int(2*HALL_X/0.4) + 1)] + [HALL_X]
for i in range(len(fx) - 1):
    for j in range(8):
        y0, y1 = -HALL_Y + j*2*HALL_Y/8, -HALL_Y + (j + 1)*2*HALL_Y/8
        f = ib.faces.new([ib.verts.new((x, y, 0.015)) for x, y in ((fx[i], y0), (fx[i+1], y0), (fx[i+1], y1), (fx[i], y1))])
        f.normal_update()
        if f.normal.z < 0: f.normal_flip()
        ib_cats.append("floor")
ime = bpy.data.meshes.new("SM_Bld_Barn_01_Interior"); ib.to_mesh(ime); ib.free()
interior = bpy.data.objects.new("SM_Bld_Barn_01_Interior", ime); scene.collection.objects.link(interior)
interior.parent = root
for p in ime.polygons: p.use_smooth = False
paint_face_attrs(ime, ib_cats, interior.matrix_world)
print("INTERIOR", twins, "twin faces,", len(ime.polygons) - twins, "floor faces")

# Painted UVs: the outside and the inside each get their own 4K atlas.
def unwrap(objects, margin):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects:
        if o.data.uv_layers: o.data.uv_layers[0].name = "Atlas"
        uv = o.data.uv_layers.new(name="Paint")
        o.data.uv_layers.active = uv
        o.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=margin, area_weight=0.0, scale_to_bounds=False)
    bpy.ops.uv.pack_islands(margin=margin*1.3, rotate=True)
    bpy.ops.object.mode_set(mode='OBJECT')
unwrap(objs, 0.003)
unwrap([interior], 0.004)

# ---------------------------------------------------------------- painterly bake material
target = bpy.data.images.new(NAME + "_Albedo", TEX_SIZE, TEX_SIZE, alpha=False)
target_in = bpy.data.images.new(NAME + "_Interior_Albedo", TEX_SIZE, TEX_SIZE, alpha=False)   # the inside is as big as the outside

def painterly(name, image):
    mat = bpy.data.materials.new(name); nt = mat.node_tree; N, L = nt.nodes, nt.links
    def node(kind, **kw):
        n = N.new(kind)
        for k, v in kw.items(): setattr(n, k, v)
        return n
    def math_node(op, a, b=None, clamp=False):
        n = node("ShaderNodeMath", operation=op, use_clamp=clamp)
        for i, v in enumerate((a, b)):
            if v is None: continue
            if isinstance(v, (int, float)): n.inputs[i].default_value = v
            else: L.new(v, n.inputs[i])
        return n.outputs[0]
    def mix(a, b, fac):
        n = node("ShaderNodeMix", data_type='RGBA', blend_type='MIX')
        for sock, v in ((n.inputs[0], fac), (n.inputs[6], a), (n.inputs[7], b)):
            if isinstance(v, (int, float)): sock.default_value = v
            elif isinstance(v, tuple): sock.default_value = (*v, 1.0)
            else: L.new(v, sock)
        return n.outputs[2]
    def scale_colour(c, k):
        n = node("ShaderNodeMix", data_type='RGBA', blend_type='MULTIPLY'); n.inputs[0].default_value = 1.0
        L.new(c, n.inputs[6])
        if isinstance(k, (int, float)): n.inputs[7].default_value = (k, k, k, 1.0)
        else:
            comb = node("ShaderNodeCombineColor")
            for i in range(3): L.new(k, comb.inputs[i])
            L.new(comb.outputs[0], n.inputs[7])
        return n.outputs[2]
    shadow = node("ShaderNodeAttribute", attribute_name="Shadow").outputs["Color"]
    base = node("ShaderNodeAttribute", attribute_name="Base").outputs["Color"]
    high = node("ShaderNodeAttribute", attribute_name="High").outputs["Color"]
    grad_attr = node("ShaderNodeAttribute", attribute_name="Grad")
    grad = node("ShaderNodeSeparateColor"); L.new(grad_attr.outputs["Color"], grad.inputs[0])
    t_face, along, seams, shingled = grad.outputs[0], grad.outputs[1], grad.outputs[2], grad_attr.outputs["Alpha"]
    geo = node("ShaderNodeNewGeometry")

    # Broad soft brush strokes move the light up and down the face.
    mapping = node("ShaderNodeMapping"); mapping.inputs["Scale"].default_value = (1.6, 1.6, 0.7)
    L.new(geo.outputs["Position"], mapping.inputs["Vector"])
    strokes = node("ShaderNodeTexNoise"); strokes.inputs["Scale"].default_value = 1.8; strokes.inputs["Detail"].default_value = 2.0
    strokes.inputs["Roughness"].default_value = 0.45
    L.new(mapping.outputs[0], strokes.inputs["Vector"])
    # Roofs: rows of shingles, each lit along its lower edge and shaded at the top where the row above overlaps it,
    # with the joints staggered from row to row.
    pos = node("ShaderNodeSeparateXYZ"); L.new(geo.outputs["Position"], pos.inputs[0])
    row_f = math_node('DIVIDE', pos.outputs["Z"], 0.24)
    row = math_node('FLOOR', row_f)
    row_t = math_node('SUBTRACT', 1.0, math_node('FRACT', row_f))          # 1 at the row's lower edge, 0 under the next
    t_face = math_node('ADD', t_face, math_node('MULTIPLY', math_node('SUBTRACT', math_node('ADD', 0.12, math_node('MULTIPLY', row_t, 0.82)), t_face), shingled))
    t = math_node('ADD', t_face, math_node('MULTIPLY', math_node('SUBTRACT', strokes.outputs["Fac"], 0.5), 0.6), clamp=True)
    # Painted light: shaded lower edge -> base -> lit top edge.
    lower = mix(shadow, base, math_node('MULTIPLY', t, 2.4, clamp=True))
    upper = mix(base, high, math_node('MULTIPLY', math_node('SUBTRACT', t, 0.62), 2.2, clamp=True))
    # A soft hand-over rather than a hard edge: a hard edge follows the brush noise in jagged steps on the texels.
    soft = node("ShaderNodeMapRange", interpolation_type='SMOOTHSTEP')
    L.new(t, soft.inputs["Value"]); soft.inputs["From Min"].default_value = 0.46; soft.inputs["From Max"].default_value = 0.64
    col = mix(lower, upper, soft.outputs["Result"])

    # Boards: every board a slightly different value, with a painted seam line between boards.
    board = math_node('FLOOR', math_node('DIVIDE', along, 0.34))
    wn = node("ShaderNodeTexWhiteNoise", noise_dimensions='1D'); L.new(board, wn.inputs["W"])
    board_k = math_node('ADD', 1.0, math_node('MULTIPLY', math_node('MULTIPLY', math_node('SUBTRACT', wn.outputs["Value"], 0.5), 0.16), seams))
    col = scale_colour(col, board_k)
    frac = math_node('FRACT', math_node('DIVIDE', along, 0.34))
    dist = math_node('MINIMUM', frac, math_node('SUBTRACT', 1.0, frac))
    seam = math_node('MULTIPLY', math_node('SUBTRACT', 1.0, math_node('MULTIPLY', dist, 22.0, clamp=True)), seams)
    col = mix(col, scale_colour(shadow, 0.75), math_node('MULTIPLY', seam, 0.75))

    # Shingle joints: short dark gaps between neighbouring shingles, offset by half a shingle on every other row.
    joint_f = math_node('FRACT', math_node('DIVIDE', math_node('ADD', along, math_node('MULTIPLY', row, 0.17)), 0.34))
    joint = math_node('SUBTRACT', 1.0, math_node('MULTIPLY', math_node('MINIMUM', joint_f, math_node('SUBTRACT', 1.0, joint_f)), 26.0, clamp=True))
    row_line = math_node('SUBTRACT', 1.0, math_node('MULTIPLY', math_node('FRACT', row_f), 30.0, clamp=True))
    col = mix(col, scale_colour(shadow, 0.7), math_node('MULTIPLY', math_node('MAXIMUM', joint, row_line), math_node('MULTIPLY', shingled, 0.8)))

    # Wobbly dark outlines along the edges of every piece.
    bevel = node("ShaderNodeBevel", samples=8); bevel.inputs["Radius"].default_value = 0.03
    edge_dot = node("ShaderNodeVectorMath", operation='DOT_PRODUCT')
    L.new(bevel.outputs[0], edge_dot.inputs[0]); L.new(geo.outputs["Normal"], edge_dot.inputs[1])
    wobble = node("ShaderNodeTexNoise"); wobble.inputs["Scale"].default_value = 6.0
    L.new(geo.outputs["Position"], wobble.inputs["Vector"])
    edge = math_node('MULTIPLY', math_node('SUBTRACT', 1.0, edge_dot.outputs["Value"]),
                     math_node('ADD', 5.0, math_node('MULTIPLY', wobble.outputs["Fac"], 8.0)), clamp=True)
    col = mix(col, scale_colour(shadow, 0.6), math_node('MULTIPLY', edge, 0.7))

    # Soft purple shadow in creases and under the eaves.
    ao = node("ShaderNodeAmbientOcclusion", samples=12); ao.inputs["Distance"].default_value = 0.7
    L.new(col, ao.inputs["Color"])
    col = mix(ao.outputs["Color"], scale_colour(shadow, 0.9), math_node('MULTIPLY', math_node('SUBTRACT', 1.0, ao.outputs["AO"]), 0.6, clamp=True))
    L.new(col, N["Principled BSDF"].inputs["Base Color"])

    uv_paint = node("ShaderNodeUVMap", uv_map="Paint")
    bake_node = node("ShaderNodeTexImage", image=image); L.new(uv_paint.outputs[0], bake_node.inputs[0])
    for n in N: n.select = False
    bake_node.select = True; N.active = bake_node
    return mat

outside = painterly("Paint", target); inside = painterly("PaintInterior", target_in)
for o in objs: o.data.materials.clear(); o.data.materials.append(outside)
interior.data.materials.append(inside)
objs.append(interior)

scene.render.engine = 'CYCLES'; scene.cycles.device = 'CPU'; scene.cycles.samples = 24
bpy.ops.object.select_all(action='DESELECT')
for o in objs: o.select_set(True)
bpy.context.view_layer.objects.active = root
bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'}, margin=8, use_clear=True)
print("BAKED")

def dilate(img, rounds=48):
    """Grow the islands over the empty background so mipmaps never mix black into the seams."""
    w, h = img.size
    px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
    filled = px[:, :, :3].sum(axis=2) > 1e-5
    for _ in range(rounds):
        if filled.all(): break
        acc = np.zeros((h, w, 3), np.float32); cnt = np.zeros((h, w), np.float32)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            m = np.roll(filled, (dy, dx), axis=(0, 1))
            acc += np.roll(px[:, :, :3], (dy, dx), axis=(0, 1))*m[:, :, None]; cnt += m
        grow = (~filled) & (cnt > 0)
        px[grow, :3] = acc[grow]/cnt[grow][:, None]; filled |= grow
    px[:, :, 3] = 1.0
    img.pixels[:] = px.ravel()
for img in (target, target_in):
    dilate(img)
    path = os.path.join(TEX_DIR, img.name + ".png")
    img.filepath_raw = path; img.file_format = 'PNG'; img.save()
    print("SAVED", path)

# ---------------------------------------------------------------- export: painted UVs only, same names and pivots
def baked_material(name, image):
    m = bpy.data.materials.new(name)
    t = m.node_tree.nodes.new("ShaderNodeTexImage"); t.image = image
    m.node_tree.links.new(t.outputs["Color"], m.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
    return m
final, final_in = baked_material("M_Barn_HandPainted", target), baked_material("M_Barn_Interior", target_in)
for o in objs:
    if "Atlas" in o.data.uv_layers: o.data.uv_layers.remove(o.data.uv_layers["Atlas"])
    o.data.uv_layers["Paint"].name = "UVMap"
    for n in ("Shadow", "Base", "High", "Grad"):
        if n in o.data.color_attributes: o.data.color_attributes.remove(o.data.color_attributes[n])
    o.data.materials.clear(); o.data.materials.append(final_in if o is interior else final)
# Export triangles: Unity drops concave or self-intersecting polygons it cannot triangulate itself.
for o in objs:
    tri = o.modifiers.new("Triangulate", 'TRIANGULATE')
    tri.quad_method = 'BEAUTY'; tri.ngon_method = 'BEAUTY'; tri.keep_custom_normals = True
bpy.ops.object.select_all(action='DESELECT')
for o in objs + [collision, shadow_roof]: o.select_set(True)
bpy.context.view_layer.objects.active = root
bpy.ops.export_scene.fbx(use_mesh_modifiers=True, filepath=os.path.join(ASSET_DIR, NAME + ".fbx"), use_selection=True, object_types={'MESH'},
    global_scale=1.0, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
    bake_space_transform=True, mesh_smooth_type='OFF', colors_type='NONE', add_leaf_bones=False, bake_anim=False,
    path_mode='STRIP', embed_textures=False)
print("EXPORTED", {o.name: (len(o.data.polygons), tuple(round(x, 3) for x in o.location)) for o in objs})

# ---------------------------------------------------------------- previews (doors slid open, loft shutters swung out)
if PREVIEW:
    centre = Vector((0, 0, 0))
    for o in parts:
        if "Door" in o.name:
            # Slide each leaf away from the middle of its doorway, a little proud of the trim.
            o.location.y += (1 if o.location.y > 0 else -1)*1.25
            o.location.x += (-1 if o.location.x < 0 else 1)*0.12
        else:
            best = None
            for sign in (1, -1):
                o.rotation_euler = (0, 0, sign*math.radians(100))
                bpy.context.view_layer.update()
                c = sum((o.matrix_world @ Vector(v) for v in o.bound_box), Vector())/8
                if best is None or abs(c.x) > best[0]: best = (abs(c.x), sign)
            o.rotation_euler = (0, 0, best[1]*math.radians(100))
    scene.render.engine = 'BLENDER_WORKBENCH'
    sh = scene.display.shading; sh.light = 'FLAT'; sh.color_type = 'TEXTURE'
    scene.render.resolution_x, scene.render.resolution_y = 1600, 1100
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); scene.collection.objects.link(cam); scene.camera = cam
    def shoot(name, target, dist, elev, az):
        d = Vector((math.cos(math.radians(elev))*math.sin(math.radians(az)), -math.cos(math.radians(elev))*math.cos(math.radians(az)), math.sin(math.radians(elev))))
        cam.location = Vector(target) + d*dist; cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = os.path.join(PREVIEW, name); bpy.ops.render.render(write_still=True)
    shoot("hp_a.png", (0, 0, 3.5), 21, 12, -60)
    shoot("hp_b.png", (0, 0, 3.5), 21, 12, 120)
    shoot("hp_door.png", (-5.8, 0, 2.2), 9, 6, -90)
    shoot("hp_inside.png", (0, 0, 2.4), 6.5, 4, -90)
    shoot("hp_inside2.png", (2, 0, 3.0), 3.0, 18, -70)
    shoot("hp_inside_left.png", (0, -3.5, 2.2), 4.0, 8, 180)
    shoot("hp_inside_right.png", (0, 3.5, 2.2), 4.0, 8, 0)
    shoot("hp_window_out.png", (0, -6.8, 1.8), 7.0, 10, 0)
print("DONE")
