"""Preview renders of FruitTrees.blend: the three trees side by side, then a close look at each crown.

    blender -b ArtSource/FruitTrees/FruitTrees.blend --python ArtSource/FruitTrees/render_preview.py -- <out dir>
"""
import bpy, math, os, sys
from mathutils import Vector

out = sys.argv[sys.argv.index("--") + 1]
scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x, scene.render.resolution_y = 1800, 900
scene.view_settings.view_transform = "Standard"
scene.view_settings.look = "None"

world = bpy.data.worlds.new("Sky"); scene.world = world
world.use_nodes = True
bg = world.node_tree.nodes["Background"]
sky = world.node_tree.nodes.new("ShaderNodeTexGradient")
coord = world.node_tree.nodes.new("ShaderNodeTexCoord")
sep = world.node_tree.nodes.new("ShaderNodeSeparateXYZ")
ramp = world.node_tree.nodes.new("ShaderNodeValToRGB")
world.node_tree.links.new(coord.outputs["Generated"], sep.inputs[0])
world.node_tree.links.new(sep.outputs["Z"], ramp.inputs[0])
ramp.color_ramp.elements[0].position = 0.45; ramp.color_ramp.elements[0].color = (0.62, 0.70, 0.62, 1)
ramp.color_ramp.elements[1].position = 0.75; ramp.color_ramp.elements[1].color = (0.45, 0.65, 0.95, 1)
world.node_tree.links.new(ramp.outputs["Color"], bg.inputs["Color"])
bg.inputs["Strength"].default_value = 0.9

sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
sun.data.energy = 3.2; sun.data.angle = math.radians(4)
sun.rotation_euler = (math.radians(50), 0, math.radians(35))
scene.collection.objects.link(sun)

ground = bpy.data.meshes.new("Ground")
ground.from_pydata([(-60, -60, 0), (60, -60, 0), (60, 60, 0), (-60, 60, 0)], [], [(0, 1, 2, 3)])
gmat = bpy.data.materials.new("Ground"); gmat.use_nodes = True
gmat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.20, 0.32, 0.10, 1)
gmat.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 1
ground.materials.append(gmat)
scene.collection.objects.link(bpy.data.objects.new("Ground", ground))

trees = [o for o in scene.objects if o.name.endswith("Tree")]
cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); scene.collection.objects.link(cam)
scene.camera = cam


def look(cam, target, distance, yaw, pitch, lens):
    cam.data.lens = lens
    d = Vector((math.sin(math.radians(yaw)) * math.cos(math.radians(pitch)),
                -math.cos(math.radians(yaw)) * math.cos(math.radians(pitch)),
                math.sin(math.radians(pitch))))
    cam.location = target + d * distance
    cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()


def render(path):
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


xs = [o.location.x for o in trees]
centre = Vector(((min(xs) + max(xs)) / 2, 0, 3.0))
look(cam, centre, 26, 12, 6, 40)
render(os.path.join(out, "fruit_trees_overview.png"))
for o in trees:
    top = o.dimensions.z
    look(cam, o.location + Vector((0, 0, top * 0.45)), top * 1.05, 25, -8, 35)   # from below, as the chick sees it
    render(os.path.join(out, "fruit_trees_%s.png" % o.name))
