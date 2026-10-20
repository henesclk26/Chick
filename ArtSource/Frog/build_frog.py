"""Makes the pond frog: "Frog" by johnrife48 (Sketchfab, CC-BY-4.0), Assets/Animal/frog.glb, ready for Unity.

    blender -b --factory-startup --python ArtSource/Frog/build_frog.py -- <project root>

- One mesh "Frog", scaled to FROG_LENGTH nose to tail, origin under the middle of its belly, facing Blender -Y
  (exported with -Z forward / Y up, so it faces Unity's +Z).
- Its textures are written next to it (base colour, normal, metallic-roughness); FrogSetup.cs builds the URP material.
Writes Assets/Art/Frog/Frog.fbx and Assets/Art/Frog/T_Frog_*.png.
"""
import bpy, os, sys
from mathutils import Vector, Matrix

PROJECT = sys.argv[sys.argv.index("--") + 1]
SOURCE = os.path.join(PROJECT, "Assets", "Animal", "frog.glb")
OUT = os.path.join(PROJECT, "Assets", "Art", "Frog")
FROG_LENGTH = 0.18   # m: a little shorter than the chick is tall

os.makedirs(OUT, exist_ok=True)
for o in list(bpy.data.objects): bpy.data.objects.remove(o)
bpy.ops.import_scene.gltf(filepath=SOURCE)
bpy.context.view_layer.update()
meshes = [o for o in bpy.data.objects if o.type == "MESH"]
for o in meshes:
    o.data = o.data.copy()
    o.data.transform(o.matrix_world)
    o.parent = None
    o.matrix_world = Matrix.Identity(4)
for o in list(bpy.data.objects):
    if o.type != "MESH": bpy.data.objects.remove(o)
bpy.ops.object.select_all(action="DESELECT")
for o in meshes: o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1: bpy.ops.object.join()
frog = bpy.context.view_layer.objects.active
frog.name = frog.data.name = "Frog"

me = frog.data
xs = [v.co.x for v in me.vertices]; ys = [v.co.y for v in me.vertices]; zs = [v.co.z for v in me.vertices]
length = max(ys) - min(ys)
k = FROG_LENGTH / length
centre = Vector(((min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2, min(zs)))
me.transform(Matrix.Diagonal((k, k, k, 1)) @ Matrix.Translation(-centre))
me.update()

# Which way does it look? The eyes stick up highest at the front: the front is the end with the highest vertices.
top = max(v.co.z for v in me.vertices)
front = [v.co for v in me.vertices if v.co.z > top - 0.15 * (top - 0)]
front_y = sum(c.y for c in front) / len(front)
if front_y > 0:                                   # looking towards +Y: turn round to face -Y
    me.transform(Matrix.Rotation(3.14159265, 4, "Z"))
    me.update()
print("FROG size %.3f x %.3f x %.3f m, %d triangles, front was %s" % (
    frog.dimensions.x, frog.dimensions.y, frog.dimensions.z, sum(len(p.vertices) - 2 for p in me.polygons), "+Y" if front_y > 0 else "-Y"))

# Textures out of the glb.
mat = me.materials[0]
for node in mat.node_tree.nodes:
    if node.type != "TEX_IMAGE" or node.image is None: continue
    target = [l.to_socket.name for l in node.outputs["Color"].links] + [l.to_node.type for l in node.outputs["Color"].links]
    if "Base Color" in target: name = "BaseColor"
    elif "NORMAL_MAP" in target: name = "Normal"
    else: name = "MetallicRoughness"
    path = os.path.join(OUT, "T_Frog_%s.png" % name)
    node.image.filepath_raw = path; node.image.file_format = "PNG"; node.image.save()
    print("TEXTURE", name, tuple(node.image.size))

bpy.ops.object.select_all(action="DESELECT")
frog.select_set(True)
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, "Frog.fbx"), use_selection=True, object_types={"MESH"},
                         apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
                         bake_space_transform=True, mesh_smooth_type="FACE", use_triangles=True, add_leaf_bones=False,
                         path_mode="STRIP", embed_textures=False, colors_type="NONE")
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(PROJECT, "ArtSource", "Frog", "Frog.blend"))
