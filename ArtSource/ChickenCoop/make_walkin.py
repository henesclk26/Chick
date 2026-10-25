"""Makes the hand-painted chicken coop (Assets/Assets/chicken_coop_handpainted.glb) walk-in for the chick without
changing how it looks outside: the back of the pop door's recess (two faces) is removed so the doorway opens into the
coop, and a room is built inside the body (floor level with the door's sill, plank walls, plank ceiling), its faces
looking inward and textured from wood areas of the coop's own texture. The front wall of the room has a hole exactly
the size of the doorway. The coop's texture is repainted in the barn's colours (variant B: red boards, warm brown
wood, white posts and frames; see recolor notes below).

Writes, for the Unity tool (Assets/Editor/FarmReference/ChickenCoopWalkIn.cs):
  ArtSource/ChickenCoop/coop_body_walkin.json  the coop body mesh ("Object_4", the KURNIK material) in its node's
                                               own space: per-corner positions, normals, UVs and triangles, plus the
                                               original mesh's bounds (to line the axes up) and a walk path for tests
  Assets/Art/Coop/T_ChickenCoop_Barn.png       the recoloured texture
Run (Blender 5.x, background):
    blender -b --factory-startup --python ArtSource/ChickenCoop/make_walkin.py -- <project root>
"""
import bpy, bmesh, json, os, sys
import numpy as np
from mathutils import Vector

PROJECT = sys.argv[sys.argv.index("--") + 1]
GLB = os.path.join(PROJECT, "Assets", "Assets", "chicken_coop_handpainted.glb")
BARN_TEX = os.path.join(PROJECT, "Assets", "Art", "SyntyBarn", "Textures", "SM_Bld_Barn_01_HandPainted_Albedo.png")
OUT_JSON = os.path.join(PROJECT, "ArtSource", "ChickenCoop", "coop_body_walkin.json")
OUT_TEX = os.path.join(PROJECT, "Assets", "Art", "Coop", "T_ChickenCoop_Barn.png")

# The pop door's recess and the room behind it, in the imported model's world space (Blender, z up). Measured on the
# model: the recess runs x 9.67..10.32 at y -0.524..0.308, z 1.542..2.613; inside, the body's walls stand at
# x 7.1 / 10.3, y -1.72 / 4.79, under the roof at z 4.28.
DOOR_Y, DOOR_Z = (-0.524, 0.308), (1.542, 2.613)
ROOM_X, ROOM_Y, ROOM_Z = (7.2, 9.672), (-1.62, 4.69), (1.542, 4.15)

# Wood areas of the texture (UV, v up): the plank panel for the walls and the floor, dark planks above.
UV_WALL = ((0.422, 0.025), (0.598, 0.236))   # the plank panel too (planks stand upright on the walls)
UV_FLOOR = ((0.420, 0.023), (0.600, 0.238))
UV_CEILING = ((0.527, 0.658), (0.693, 0.980))

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=GLB)
bpy.context.view_layer.update()
body = bpy.data.objects["Object_4"]
M = body.matrix_world.copy()
Mi = M.inverted()
orig = [Mi @ (M @ v.co) for v in body.data.vertices]
lo = Vector((min(v.x for v in orig), min(v.y for v in orig), min(v.z for v in orig)))
hi = Vector((max(v.x for v in orig), max(v.y for v in orig), max(v.z for v in orig)))

bm = bmesh.new()
bm.from_mesh(body.data)
bm.transform(M)                      # work in world space
uv = bm.loops.layers.uv.active

# 1. Open the doorway: the recess's back faces go.
gone = [f for f in bm.faces if 9.6 < f.calc_center_median().x < 9.75 and f.normal.x > 0.8
        and DOOR_Y[0] - 0.05 < f.calc_center_median().y < DOOR_Y[1] + 0.05 and DOOR_Z[0] - 0.05 < f.calc_center_median().z < DOOR_Z[1] + 0.05]
assert len(gone) == 2, "expected the recess's two back faces, found %d" % len(gone)
bmesh.ops.delete(bm, geom=gone, context='FACES')

# 2. The room: quads facing inward, UVs stretched over a wood area of the texture.
def quad(corners, uv_rect):
    vs = [bm.verts.new(Vector(c)) for c in corners]
    f = bm.faces.new(vs)
    (u0, v0), (u1, v1) = uv_rect
    for loop, (a, b) in zip(f.loops, ((0, 0), (1, 0), (1, 1), (0, 1))):
        loop[uv].uv = (u0 + (u1 - u0) * a, v0 + (v1 - v0) * b)
    return f

TILE = {UV_WALL: (1.2, 1.4), UV_FLOOR: (1.0, 1.0), UV_CEILING: (1.2, 1.2)}   # model metres the wood area covers

def panel(axis, fixed, a0, a1, b0, b1, inward, uv_rect):
    """A rectangle on the plane axis = fixed spanning [a0, a1] x [b0, b1] on the other two axes, facing `inward`,
    laid as tiles of the wood area at its own size (a part tile shows part of the area), so the planks keep their
    scale instead of being stretched into a flat colour."""
    def p(a, b):
        if axis == 'x': return (fixed, a, b)
        if axis == 'y': return (a, fixed, b)
        return (a, b, fixed)
    ta, tb = TILE[uv_rect]
    (u0, v0), (u1, v1) = uv_rect
    a = a0
    while a < a1 - 1e-4:
        a_end = min(a + ta, a1)
        b = b0
        while b < b1 - 1e-4:
            b_end = min(b + tb, b1)
            part = ((u0, v0), (u0 + (u1 - u0) * (a_end - a) / ta, v0 + (v1 - v0) * (b_end - b) / tb))
            f = quad([p(a, b), p(a_end, b), p(a_end, b_end), p(a, b_end)], part)
            f.normal_update()
            if f.normal.dot(Vector(inward)) < 0:
                f.normal_flip()
            b = b_end
        a = a_end

x0, x1 = ROOM_X; y0, y1 = ROOM_Y; z0, z1 = ROOM_Z
panel('z', z0, x0, x1, y0, y1, (0, 0, 1), UV_FLOOR)
panel('z', z1, x0, x1, y0, y1, (0, 0, -1), UV_CEILING)
panel('x', x0, y0, y1, z0, z1, (1, 0, 0), UV_WALL)
panel('y', y0, x0, x1, z0, z1, (0, 1, 0), UV_WALL)
panel('y', y1, x0, x1, z0, z1, (0, -1, 0), UV_WALL)
# Door wall (x1) with the doorway left open.
dy0, dy1 = DOOR_Y; dz0, dz1 = DOOR_Z
panel('x', x1, y0, dy0, z0, z1, (-1, 0, 0), UV_WALL)
panel('x', x1, dy1, y1, z0, z1, (-1, 0, 0), UV_WALL)
panel('x', x1, dy0, dy1, dz1, z1, (-1, 0, 0), UV_WALL)
bm.normal_update()

# 3. Back to the node's own space and out as per-corner arrays (the mesh keeps the original's flat look).
bm.transform(Mi)
positions, normals, uvs, tris = [], [], [], []
for f in bm.faces:
    corners = []
    for loop in f.loops:
        corners.append(len(positions))
        positions.append(tuple(loop.vert.co))
        normals.append(tuple(f.normal) if not f.smooth else tuple(loop.vert.normal))
        uvs.append(tuple(loop[uv].uv))
    for i in range(1, len(corners) - 1):
        tris += [corners[0], corners[i], corners[i + 1]]

# Points to walk a chick-sized capsule through in the test (node space): foot of the ramp, up it, the landing, the
# doorway, the middle of the room.
walk = [Mi @ Vector(p) for p in ((13.0, -0.1, 0.4), (12.3, -0.1, 1.0), (11.5, -0.1, 1.6), (10.0, -0.1, 1.6), (9.4, -0.1, 1.6), (8.4, 1.5, 1.6))]
os.makedirs(os.path.dirname(OUT_JSON), exist_ok=True)
with open(OUT_JSON, "w") as fh:
    json.dump({"positions": positions, "normals": normals, "uvs": uvs, "triangles": tris,
               "originalBounds": [tuple(lo), tuple(hi)], "walk": [tuple(p) for p in walk],
               "room": {"x": ROOM_X, "y": ROOM_Y, "z": ROOM_Z}}, fh)
print("BODY", len(positions), "corners,", len(tris) // 3, "triangles; removed", len(gone), "faces")

# 4. The texture in the barn's colours (variant B). Groups of the painted texture are moved onto the colours measured
# on the barn's own texture: red boards to its red, wood to its warm brown (a touch less orange), the dark posts and
# frames to its white trim (with a soft edge so no dark flecks are left), keeping every brush stroke's light and shade.
def pixels(path):
    im = bpy.data.images.load(path)
    im.colorspace_settings.name = 'Non-Color'
    w, h = im.size
    return np.array(im.pixels[:], dtype=np.float32).reshape(h, w, 4), w, h

barn, _, _ = pixels(BARN_TEX)
bp = barn.reshape(-1, 4)[::5, :3]
r, g, b = bp[:, 0], bp[:, 1], bp[:, 2]
target_red = bp[(r > g * 1.6) & (r > 0.25) & (g < 0.35)]
target_wood = bp[(r > g * 1.15) & (r < g * 1.6) & (g > b * 1.1) & (r > 0.3)]
target_white = bp[(r > 0.7) & (g > 0.7) & (b > 0.65)]

image = next(n.image for m in body.data.materials if m and m.use_nodes for n in m.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image)
image.colorspace_settings.name = 'Non-Color'
w, h = image.size
src = np.array(image.pixels[:], dtype=np.float32).reshape(h, w, 4)
mx = src[..., :3].max(-1); mn = src[..., :3].min(-1)
sat = (mx - mn) / (mx + 1e-6)
hue_r = src[..., 0] >= src[..., 1]
grey_bg = (sat < 0.06) & (mx > 0.45) & (mx < 0.62)
red = hue_r & (sat > 0.35) & ((src[..., 0] - src[..., 1]) > 0.18 * mx) & (mx > 0.12) & ~grey_bg
dark = (mx < 0.2) & ~red & ~grey_bg
wood = ~red & ~dark & ~grey_bg

def transfer(px, mask, target, lift=1.0):
    s = px[mask][:, :3]
    mu_s, sd_s = s.mean(0), s.std(0) + 1e-4
    k = np.clip(target.std(0) / sd_s, 0.5, 1.6)
    px[mask, :3] = np.clip((s - mu_s) * k + target.mean(0) * lift, 0, 1)

out = src.copy()
transfer(out, red, target_red, lift=1.15)
transfer(out, wood, target_wood, lift=1.05)
transfer(out, dark, target_wood, lift=1.05)
lum = out[wood][:, :3].mean(1, keepdims=True)
out[wood, :3] = out[wood][:, :3] * 0.78 + lum * 0.22
frame = hue_r & (sat > 0.35) & ((src[..., 0] - src[..., 1]) > 0.18 * mx) & (mx > 0.24)
t = np.clip((0.3 - mx) / 0.12, 0, 1); t = t * t * (3 - 2 * t); t[frame] = 0
sel = t > 0.5
shade = 0.9 + 0.1 * np.clip((mx - mx[sel].mean()) / (mx[sel].std() + 1e-4), -1.5, 1.5)
white = target_white.mean(0)[None, None, :] * shade[..., None]
out[..., :3] = out[..., :3] * (1 - t[..., None]) + np.clip(white, 0, 1) * t[..., None]
os.makedirs(os.path.dirname(OUT_TEX), exist_ok=True)
tex = bpy.data.images.new("T_ChickenCoop_Barn", w, h)
tex.colorspace_settings.name = 'Non-Color'
tex.pixels[:] = out.ravel()
tex.filepath_raw = OUT_TEX; tex.file_format = 'PNG'; tex.save()
print("TEXTURE", OUT_TEX, w, h)

# 5. Collision for the hay (the SENO mesh): the same shape without the thin straw strewn on the landing and in the
# doorway, which would otherwise stop the chick at the door. Only the collider changes; the straw is still drawn.
HAY_JSON = os.path.join(PROJECT, "ArtSource", "ChickenCoop", "coop_hay_collider.json")
hay = next(o for o in bpy.data.objects if o.type == 'MESH' and o.parent is not None and o.parent.name == body.parent.name and o is not body
           and any(m and m.name == "SENO" for m in o.data.materials))
H = hay.matrix_world
hay_lo = Vector(tuple(min(v.co[i] for v in hay.data.vertices) for i in range(3)))
hay_hi = Vector(tuple(max(v.co[i] for v in hay.data.vertices) for i in range(3)))
keep = []
dropped = 0
for p in hay.data.polygons:
    c = H @ p.center
    if 9.5 < c.x < 12.2 and -1.0 < c.y < 0.8 and 1.2 < c.z < 2.9:
        dropped += 1
        continue
    keep.append(list(p.vertices))
tris = []
for poly in keep:
    for i in range(1, len(poly) - 1):
        tris += [poly[0], poly[i], poly[i + 1]]
with open(HAY_JSON, "w") as fh:
    json.dump({"positions": [tuple(v.co) for v in hay.data.vertices], "triangles": tris,
               "originalBounds": [tuple(hay_lo), tuple(hay_hi)]}, fh)
print("HAY", hay.name, "kept", len(keep), "faces, left out", dropped, "on the landing and in the doorway")
