# Bakes the painted ant and ant nest into their own albedo atlases and exports Ant.fbx (body with legs/antennae as
# child objects, pivots at the hips for the walk cycle), AntNest.fbx and AntBush.fbx (the bush on the trail).
# Run: blender --background --factory-startup <Ant.blend> --python bake_ant.py -- <assetDir>
import bpy, os, sys
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:]
ASSET_DIR = argv[0]
TEX_DIR = os.path.join(ASSET_DIR, "Textures"); os.makedirs(TEX_DIR, exist_ok=True)
scene = bpy.context.scene

def unwrap(objs):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
        if not o.data.uv_layers: o.data.uv_layers.new(name="UVMap")
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    # Few, well separated islands: tiny islands packed tight let neighbouring colours bleed into each other's seams.
    bpy.ops.uv.smart_project(angle_limit=1.45, island_margin=0.01, area_weight=0.0, scale_to_bounds=False)
    bpy.ops.uv.pack_islands(margin=0.012, rotate=True)
    bpy.ops.object.mode_set(mode='OBJECT')

def paint_material(name):
    m = bpy.data.materials.new(name); N = m.node_tree.nodes
    ca = N.new("ShaderNodeVertexColor"); ca.layer_name = "Paint"
    m.node_tree.links.new(ca.outputs["Color"], N["Principled BSDF"].inputs["Base Color"])
    return m

def dilate(img, rounds=96):
    """Grow the baked islands over the empty (black) background so mipmaps never mix in black at the seams."""
    w, h = img.size
    px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
    filled = px[:, :, :3].sum(axis=2) > 1e-5
    for _ in range(rounds):
        if filled.all(): break
        acc = np.zeros((h, w, 3), np.float32); cnt = np.zeros((h, w), np.float32)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            m = np.roll(filled, (dy, dx), axis=(0, 1)); c = np.roll(px[:, :, :3], (dy, dx), axis=(0, 1))
            acc += c*m[:, :, None]; cnt += m
        grow = (~filled) & (cnt > 0)
        px[grow, :3] = acc[grow]/cnt[grow][:, None]; filled |= grow
    px[:, :, 3] = 1.0
    img.pixels[:] = px.ravel()

def bake(objs, tex_name, size):
    unwrap(objs)
    img = bpy.data.images.new(tex_name, size, size, alpha=False)
    mat = paint_material(tex_name + "_Paint")
    N = mat.node_tree.nodes; n = N.new("ShaderNodeTexImage"); n.image = img
    for x in N: x.select = False
    n.select = True; N.active = n
    for o in objs: o.data.materials.clear(); o.data.materials.append(mat)
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    scene.render.engine = 'CYCLES'; scene.cycles.device = 'CPU'; scene.cycles.samples = 8
    bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'}, margin=4, use_clear=True)
    dilate(img)
    path = os.path.join(TEX_DIR, tex_name + ".png"); img.filepath_raw = path; img.file_format = 'PNG'; img.save()
    baked = bpy.data.materials.new(tex_name.replace("T_", "M_").replace("_Albedo", ""))
    t = baked.node_tree.nodes.new("ShaderNodeTexImage"); t.image = img
    baked.node_tree.links.new(t.outputs["Color"], baked.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
    for o in objs: o.data.materials.clear(); o.data.materials.append(baked)
    print("BAKED", path)

def export(objs, name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(filepath=os.path.join(ASSET_DIR, name + ".fbx"), use_selection=True, object_types={'MESH'},
        global_scale=1.0, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
        bake_space_transform=True, mesh_smooth_type='FACE', colors_type='NONE', add_leaf_bones=False, bake_anim=False,
        path_mode='STRIP', embed_textures=False)
    print("EXPORTED", name, sum(len(o.data.polygons) for o in objs), "faces")

ant = sorted([o for o in bpy.data.collections["Ant"].objects if o.type == 'MESH'], key=lambda o: o.name != "Ant_Body")
nest = [o for o in bpy.data.collections["AntNest"].objects if o.type == 'MESH']
bush = [o for o in bpy.data.collections["AntBush"].objects if o.type == 'MESH']
for c in ("PreviewFigures",):
    if c in bpy.data.collections: bpy.data.collections[c].hide_render = True
bake(ant, "T_Ant_Albedo", 1024)
bake(nest, "T_AntNest_Albedo", 1024)
bake(bush, "T_AntBush_Albedo", 1024)
export(ant, "Ant")
export(nest, "AntNest")
export(bush, "AntBush")
scene.render.engine = 'BLENDER_WORKBENCH'
bpy.ops.wm.save_mainfile()
