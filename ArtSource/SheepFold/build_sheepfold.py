"""Builds every sheep fold model into ArtSource/SheepFold.blend.

Run inside Blender:  exec(open(r"<repo>/ArtSource/SheepFold/build_sheepfold.py").read())
Re-running rebuilds all models in place (deterministic seeds).
"""
import importlib
import math
import os
import sys

import bpy
from mathutils import Vector

HERE = os.path.join(os.path.dirname(bpy.data.filepath) if bpy.data.filepath else r"C:\unityProjects\Chick\ArtSource", "SheepFold")
if not os.path.isdir(HERE):
    HERE = r"C:\unityProjects\Chick\ArtSource\SheepFold"
if HERE not in sys.path:
    sys.path.append(HERE)

import sheepfold_lib as L  # noqa: E402
import sheep  # noqa: E402
import props  # noqa: E402
import rig  # noqa: E402

for module in (L, sheep, props, rig):
    importlib.reload(module)

PALETTE_PATH = os.path.join(HERE, L.IMAGE_NAME + ".png")


def setup_preview(focus, distance):
    scene = bpy.context.scene
    world = scene.world or bpy.data.worlds.new("World")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.62, 0.74, 0.86, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = .55
    sun = bpy.data.objects.get("Preview Sun")
    if sun is None:
        sun = bpy.data.objects.new("Preview Sun", bpy.data.lights.new("Preview Sun", "SUN"))
        scene.collection.objects.link(sun)
    sun.data.energy = 2.4
    sun.data.angle = math.radians(8)
    sun.rotation_euler = (math.radians(52), 0, math.radians(-38))
    cam = bpy.data.objects.get("Preview Camera")
    if cam is None:
        cam = bpy.data.objects.new("Preview Camera", bpy.data.cameras.new("Preview Camera"))
        scene.collection.objects.link(cam)
    cam.data.lens = 50
    target = Vector(focus)
    cam.location = target + Vector((-distance * .55, -distance * .8, distance * .38))
    cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x, scene.render.resolution_y = 1600, 900
    scene.view_settings.view_transform = "Standard"
    ground = bpy.data.objects.get("Preview Ground")
    if ground is None:
        mesh = bpy.data.meshes.new("Preview Ground")
        mesh.from_pydata([(-40, -40, 0), (40, -40, 0), (40, 40, 0), (-40, 40, 0)], [], [(0, 1, 2, 3)])
        ground = bpy.data.objects.new("Preview Ground", mesh)
        scene.collection.objects.link(ground)
        mat = bpy.data.materials.new("Preview Grass")
        mat.use_nodes = True
        mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.25, 0.42, 0.12, 1)
        mesh.materials.append(mat)
    return cam


def build(which=("sheep", "props")):
    image = L.palette_image(PALETTE_PATH)
    material = L.palette_material(image)
    made = []
    if "sheep" in which:
        col = L.collection("Animals")
        made.append(sheep.build("Sheep", col, material, (0, 0, 0), sheep.ADULT, seed=7))
        made.append(sheep.build("Lamb", col, material, (1.3, -.2, 0), sheep.LAMB, seed=11))
        rig.rig_all()
    if "props" in which:
        made += props.build_all(L.collection("Fold Props"), material)
    return made
