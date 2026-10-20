"""Makes the apple caterpillar: 10_PinkCaterpillar from "Low Poly Cartoon Bugs (Free Sample)" by POLYGROVE Studio
(CC BY 4.0), Assets/Animal/bugs_fbx/Bugs-fbx/10_PinkCaterpillar.fbx, ready for Unity.

    blender -b --factory-startup --python ArtSource/Caterpillar/build_caterpillar.py -- <project root>

- One mesh, LENGTH long, origin under the middle of its belly, head towards Blender -Y (exported -Z forward / Y up,
  so the head points along Unity's +Z). The head is the end that stands higher (it rears its face up).
- Its palette texture (packed in the FBX) is written next to it.
Writes Assets/Art/Caterpillar/Caterpillar.fbx and T_BugsPalette.png.
"""
import bpy, os, sys
from mathutils import Vector, Matrix

PROJECT = sys.argv[sys.argv.index("--") + 1]
SOURCE = os.path.join(PROJECT, "Assets", "Animal", "bugs_fbx", "Bugs-fbx", "10_PinkCaterpillar.fbx")
OUT = os.path.join(PROJECT, "Assets", "Art", "Caterpillar")
LENGTH = 0.09   # m: a third of an apple (22 cm) long

os.makedirs(OUT, exist_ok=True)
for o in list(bpy.data.objects): bpy.data.objects.remove(o)
bpy.ops.import_scene.fbx(filepath=SOURCE)
bpy.context.view_layer.update()
mesh = next(o for o in bpy.data.objects if o.type == "MESH")
mesh.data.transform(mesh.matrix_world)
mesh.parent = None
mesh.matrix_world = Matrix.Identity(4)
for o in list(bpy.data.objects):
    if o is not mesh: bpy.data.objects.remove(o)
mesh.name = mesh.data.name = "Caterpillar"
me = mesh.data

xs = [v.co.x for v in me.vertices]; ys = [v.co.y for v in me.vertices]; zs = [v.co.z for v in me.vertices]
# Long axis: x or y.
along_y = (max(ys) - min(ys)) >= (max(xs) - min(xs))
if not along_y:
    me.transform(Matrix.Rotation(1.5707963, 4, "Z"))
    me.update()
    xs = [v.co.x for v in me.vertices]; ys = [v.co.y for v in me.vertices]
length = max(ys) - min(ys)
mid = (max(ys) + min(ys)) / 2
# Head: the end whose part stands higher.
high_minus = max((v.co.z for v in me.vertices if v.co.y < mid - length * 0.3), default=0)
high_plus = max((v.co.z for v in me.vertices if v.co.y > mid + length * 0.3), default=0)
if high_plus > high_minus:                       # head towards +Y: turn round to face -Y
    me.transform(Matrix.Rotation(3.14159265, 4, "Z"))
    me.update()
xs = [v.co.x for v in me.vertices]; ys = [v.co.y for v in me.vertices]; zs = [v.co.z for v in me.vertices]
k = LENGTH / (max(ys) - min(ys))
centre = Vector(((max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2, min(zs)))
me.transform(Matrix.Diagonal((k, k, k, 1)) @ Matrix.Translation(-centre))
me.update()
print("CATERPILLAR head was %s, now %.3f x %.3f x %.3f m, %d triangles" % (
    "+Y" if high_plus > high_minus else "-Y",
    (max(xs) - min(xs)) * k, (max(ys) - min(ys)) * k, (max(zs) - min(zs)) * k, sum(len(p.vertices) - 2 for p in me.polygons)))

for img in bpy.data.images:
    if img.size[0] == 0: continue
    path = os.path.join(OUT, "T_BugsPalette.png")
    img.filepath_raw = path; img.file_format = "PNG"; img.save()
    print("TEXTURE", img.name, tuple(img.size), "packed" if img.packed_file else "external")
    break

bpy.ops.object.select_all(action="DESELECT")
mesh.select_set(True)
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, "Caterpillar.fbx"), use_selection=True, object_types={"MESH"},
                         apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
                         bake_space_transform=True, mesh_smooth_type="FACE", use_triangles=True, add_leaf_bones=False,
                         path_mode="STRIP", embed_textures=False, colors_type="NONE")
