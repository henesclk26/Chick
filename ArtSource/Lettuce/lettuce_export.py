# Export the lettuce game parts (ArtSource/Lettuce.blend, built by lettuce.py) for Unity: one FBX with Mound,
# Lettuce_NN and Half_NN (each leaf in its own frame, placed by its object transform) and Stub_NN, plus a small
# palette texture. Every face is flat coloured (Paint), so its colour goes into a palette cell and all of the face's UVs point
# at that cell's centre.
# Usage: blender -b ArtSource/Lettuce.blend --python ArtSource/Lettuce/lettuce_export.py -- <project root>
import bpy, os, sys, shutil
import numpy as np

ROOT = sys.argv[sys.argv.index("--") + 1]
ART = os.path.join(ROOT, "Assets", "Art", "Lettuce")
SRC = os.path.join(ROOT, "ArtSource", "Lettuce")
GRID, CELL = 16, 8                                   # 16 x 16 colours, 8 px each -> 128 px texture
os.makedirs(os.path.join(ART, "Textures"), exist_ok=True)
os.makedirs(SRC, exist_ok=True)

parts = sorted((o for o in bpy.data.objects if o.type == 'MESH' and
                (o.name == "Mound" or o.name.split("_")[0] in ("Lettuce", "Half", "Stub"))), key=lambda o: o.name)
leaves = [o for o in parts if o.name.startswith("Lettuce_")]
stubs = [o for o in parts if o.name.startswith("Stub_")]
halves = [o for o in parts if o.name.startswith("Half_")]
assert parts and len(leaves) == len(stubs) == len(halves) > 0, [o.name for o in parts]

# Face colours of every part (sRGB bytes from the Paint corner attribute; a face is one colour).
face_cols = []
for o in parts:
    me = o.data
    attr = me.color_attributes["Paint"]
    c = np.zeros(len(attr.data) * 4, dtype=np.float32); attr.data.foreach_get("color_srgb", c); c = c.reshape(-1, 4)[:, :3]
    starts = np.zeros(len(me.polygons), dtype=np.int64); me.polygons.foreach_get("loop_start", starts)
    face_cols.append(c[starts])
allc = np.concatenate(face_cols)

# k-means to GRID*GRID colours (deterministic start: spread over the colours sorted by luminance).
k = GRID * GRID
order = np.argsort(allc @ np.array([0.3, 0.6, 0.1]))
centres = allc[order[np.linspace(0, len(allc) - 1, k).astype(int)]].copy()
for _ in range(25):
    d = ((allc[:, None, :] - centres[None, :, :]) ** 2).sum(-1)
    lab = d.argmin(1)
    for i in range(k):
        m = lab == i
        if m.any():
            centres[i] = allc[m].mean(0)
err = np.sqrt(((allc - centres[lab]) ** 2).sum(-1))
print("PALETTE mean err %.4f max err %.4f" % (err.mean(), err.max()))

# Palette image (Blender images are bottom-up).
px = np.ones((GRID * CELL, GRID * CELL, 4), dtype=np.float32)
for i in range(k):
    r, cidx = divmod(i, GRID)
    px[r * CELL:(r + 1) * CELL, cidx * CELL:(cidx + 1) * CELL, :3] = centres[i]
img = bpy.data.images.new("T_Lettuce_Palette", GRID * CELL, GRID * CELL, alpha=False)
img.colorspace_settings.name = 'sRGB'
img.pixels.foreach_set(px.ravel())
png = os.path.join(SRC, "T_Lettuce_Palette.png")
img.filepath_raw = png; img.file_format = 'PNG'; img.save()
shutil.copyfile(png, os.path.join(ART, "Textures", "T_Lettuce_Palette.png"))

def cell_uv(i):
    r, c = divmod(int(i), GRID)
    return ((c + 0.5) / GRID, (r + 0.5) / GRID)

off = 0
for o, cols in zip(parts, face_cols):
    me = o.data
    lab_o = lab[off:off + len(cols)]; off += len(cols)
    uv = me.uv_layers.new(name="UVMap") if "UVMap" not in me.uv_layers else me.uv_layers["UVMap"]
    for poly, li in zip(me.polygons, lab_o):
        u = cell_uv(li)
        for l in poly.loop_indices:
            uv.data[l].uv = u

# Half leaves and stubs are hidden in the .blend; unhide so they can be selected (Unity hides them until used).
for o in parts:
    o.hide_set(False)
for o in bpy.data.objects:
    o.select_set(o in parts)
bpy.context.view_layer.objects.active = parts[0]
fbx = os.path.join(ART, "Lettuce.fbx")
bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, object_types={"MESH"}, apply_unit_scale=True,
                         apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                         bake_space_transform=True, mesh_smooth_type="FACE", use_mesh_modifiers=True,
                         bake_anim=False, path_mode="STRIP", embed_textures=False)
print("EXPORTED", fbx, len(parts), "objects:", len(leaves), "leaves,", len(halves), "halves,", len(stubs), "stubs")
