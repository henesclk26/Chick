"""Renders the distant mountains as a panorama image for Unity (MountainBackdropV1).

A 3D landscape is built and rendered once with an orthographic camera onto a transparent sky, in the game's low-poly
style: two faceted peaks (a large one, and a smaller one in front of its right foot) made of coarse, flat-shaded
triangles, ridges that catch the light, snow on the upper facets, cool blue shadows, green lower slopes and foothills.
The painting is lit for daytime; the game tints it warm at sunset. Canvas
units are metres along the in-game arc: WIDTH x HEIGHT (a 200 degree arc at 800 m).

Run inside Blender (an empty scene is fine; it builds into its own collection):
    exec(open(r"<repo>/ArtSource/Mountains/mountain_panorama.py").read()); render(r"<repo>/Assets/Art/MountainPanorama/MountainPanorama.png")
"""
import math

import bmesh
import bpy
from mathutils import Vector, noise

WIDTH, HEIGHT = 2792.5, 240.0
PIXELS = (8192, 704)
MIDDLE = WIDTH / 2
# (x on the canvas, depth behind the foothills, height, base radius)
PEAKS = ((MIDDLE - 70, 420, 215, 330), (MIDDLE + 230, 250, 140, 230))
HAZE = (0.70, 0.79, 0.88)
SUN_ELEVATION, SUN_AZIMUTH = math.radians(30), math.radians(-60)  # azimuth from the camera's view, left is negative


def _height(x, y):
    """Mountain height at a ground point: a cone with clear ridges, coarse enough to read as low-poly facets."""
    h = 0.0
    for px, py, ph, pr in PEAKS:
        r = math.hypot(x - px, (y - py) * 1.15) / pr
        if r >= 1.0:
            continue
        cone = (1 - r) ** 1.1
        ridges = noise.ridged_multi_fractal(Vector((x, y, ph)) * (2.6 / pr), 1.0, 2.0, 4, 1.0, 2.0)
        h = max(h, ph * cone * (.55 + .38 * ridges))
    return h


def _grid(name, x0, x1, y0, y1, step, height):
    """Low-poly ground: a jittered grid of flat-shaded triangles (random diagonals), so facets look hand-made."""
    import random
    rng = random.Random(11)
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()
    nx, ny = int((x1 - x0) / step) + 1, int((y1 - y0) / step) + 1
    verts = []
    for j in range(ny):
        row = []
        for i in range(nx):
            edge = i in (0, nx - 1) or j in (0, ny - 1)
            x = x0 + i * step + (0 if edge else rng.uniform(-.35, .35) * step)
            y = y0 + j * step + (0 if edge else rng.uniform(-.35, .35) * step)
            row.append(bm.verts.new((x, y, height(x, y))))
        verts.append(row)
    for j in range(ny - 1):
        for i in range(nx - 1):
            a, b, c, d = verts[j][i], verts[j][i + 1], verts[j + 1][i + 1], verts[j + 1][i]
            if rng.random() < .5:
                bm.faces.new((a, b, c)); bm.faces.new((a, c, d))
            else:
                bm.faces.new((a, b, d)); bm.faces.new((b, c, d))
    bm.to_mesh(mesh)
    bm.free()
    return bpy.data.objects.new(name, mesh)


def _material(name, foothills=False):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    n, l = nt.nodes.new, nt.links.new
    out = n("ShaderNodeOutputMaterial")
    bsdf = n("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Roughness"].default_value = .9
    geo = n("ShaderNodeNewGeometry")
    pos = n("ShaderNodeSeparateXYZ")
    l(geo.outputs["Position"], pos.inputs["Vector"])
    nrm = n("ShaderNodeSeparateXYZ")
    l(geo.outputs["Normal"], nrm.inputs["Vector"])
    coord = n("ShaderNodeTexCoord")
    detail = n("ShaderNodeTexNoise")
    detail.inputs["Scale"].default_value = .06
    detail.inputs["Detail"].default_value = 8
    l(coord.outputs["Object"], detail.inputs["Vector"])

    def ramp(stops, fac):
        node = n("ShaderNodeValToRGB")
        elements = node.color_ramp.elements
        elements[0].position, elements[0].color = stops[0][0], (*stops[0][1], 1)
        elements[1].position, elements[1].color = stops[-1][0], (*stops[-1][1], 1)
        for p, c in stops[1:-1]:
            elements.new(p).color = (*c, 1)
        l(fac, node.inputs["Fac"])
        return node.outputs["Color"]

    def op(kind, a, b):
        node = n("ShaderNodeMath")
        node.operation = kind
        l(a, node.inputs[0])
        if isinstance(b, float):
            node.inputs[1].default_value = b
        else:
            l(b, node.inputs[1])
        return node.outputs[0]

    def mix(factor, a, b):
        node = n("ShaderNodeMix")
        node.data_type = "RGBA"
        node.clamp_factor = True
        l(factor, node.inputs["Factor"])
        l(a, node.inputs["A"])
        if isinstance(b, tuple):
            node.inputs["B"].default_value = (*b, 1)
        else:
            l(b, node.inputs["B"])
        return node.outputs["Result"]

    if foothills:
        color = ramp([(0, (.10, .19, .06)), (.5, (.17, .30, .08)), (1, (.26, .38, .11))], detail.outputs["Fac"])
    else:
        # Height on the mountain (0 base .. 1 summit), roughened so the bands are ragged.
        rel = op("ADD", op("DIVIDE", pos.outputs["Z"], 215.0), op("MULTIPLY", detail.outputs["Fac"], .08))
        flat = nrm.outputs["Z"]
        strata = n("ShaderNodeTexWave")
        strata.wave_type = "BANDS"
        strata.bands_direction = "Z"
        strata.inputs["Scale"].default_value = .012
        strata.inputs["Distortion"].default_value = 14
        strata.inputs["Detail"].default_value = 6
        strata.inputs["Detail Scale"].default_value = 2
        l(coord.outputs["Object"], strata.inputs["Vector"])
        # Faint, broken layering under broad noisy colour changes, not stripes.
        layered = op("ADD", op("MULTIPLY", strata.outputs["Fac"], .25), op("MULTIPLY", detail.outputs["Fac"], .75))
        # Darker than it looks right in Blender: the game's grading lifts it, and the snow cap must stand out.
        rock = ramp([(.2, (.30, .27, .27)), (.5, (.38, .34, .33)), (.8, (.46, .41, .38))], layered)
        low = ramp([(0, (.09, .17, .05)), (.6, (.16, .27, .08)), (1, (.27, .35, .12))], detail.outputs["Fac"])
        # Forest and meadow cling to the gentler lower slopes.
        vegetation = op("SUBTRACT", op("SUBTRACT", op("MULTIPLY", flat, 2.2), op("MULTIPLY", rel, 3.0)), .05)
        color = mix(vegetation, rock, low)
        # Snow settles on the upper, flatter ground; steep faces stay bare.
        # A rounded snow cap on the upper slopes, with a soft, slightly ragged edge.
        snow = op("SUBTRACT", op("ADD", op("MULTIPLY", rel, 3.4), op("MULTIPLY", flat, 1.6)), 3.05)
        color = mix(op("MULTIPLY", snow, 6.0), color, (.95, .96, .99))
    l(color, bsdf.inputs["Base Color"])
    # Aerial perspective: low ground, seen through more air, fades towards the sky's haze.
    air = n("ShaderNodeEmission")
    air.inputs["Color"].default_value = (*HAZE, 1)
    blend = n("ShaderNodeMixShader")
    l(op("ADD", op("MULTIPLY", pos.outputs["Z"], -.0007), .12), blend.inputs["Fac"])
    l(bsdf.outputs["BSDF"], blend.inputs[1])
    l(air.outputs["Emission"], blend.inputs[2])
    l(blend.outputs["Shader"], out.inputs["Surface"])
    return mat


def build():
    col = bpy.data.collections.get("Mountain Panorama")
    if col is None:
        col = bpy.data.collections.new("Mountain Panorama")
        bpy.context.scene.collection.children.link(col)
    for obj in list(col.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    mountains = _grid("Mountains", MIDDLE - 480, MIDDLE + 560, 0, 760, 16, _height)
    mountains.data.materials.append(_material("Mountain Rock Snow"))
    col.objects.link(mountains)

    def hills(x, y):
        return 10 + 7 * noise.noise(Vector((x * .004, y * .004, 0))) + 4 * noise.noise(Vector((x * .013, y * .013, 3)))

    foothills = _grid("Foothills", -40, WIDTH + 40, -60, 40, 24, hills)
    foothills.data.materials.append(_material("Foothill Meadow", True))
    col.objects.link(foothills)

    sun = bpy.data.objects.get("Panorama Sun")
    if sun is None:
        sun = bpy.data.objects.new("Panorama Sun", bpy.data.lights.new("Panorama Sun", "SUN"))
        bpy.context.scene.collection.objects.link(sun)
    sun.data.energy = 2.4
    sun.data.angle = math.radians(1)
    sun.data.color = (1.0, .96, .9)
    # The camera looks along +Y; the light comes from the upper left, slightly from the front.
    sun.rotation_euler = (math.radians(90) - SUN_ELEVATION, 0, SUN_AZIMUTH)
    world = bpy.context.scene.world or bpy.data.worlds.new("Panorama World")
    bpy.context.scene.world = world
    world.use_nodes = True
    # A blue sky fill gives the shaded facets their cool blue-violet tone.
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (.38, .48, .85, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = .5
    return col


def render(out_path):
    build()
    scene = bpy.context.scene
    cam = bpy.data.objects.get("Panorama Camera")
    if cam is None:
        cam = bpy.data.objects.new("Panorama Camera", bpy.data.cameras.new("Panorama Camera"))
        scene.collection.objects.link(cam)
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = WIDTH
    cam.data.clip_end = 5000
    cam.location = (WIDTH / 2, -1500, HEIGHT / 2)
    cam.rotation_euler = (math.radians(90), 0, 0)
    scene.camera = cam
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x, scene.render.resolution_y = PIXELS
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = True
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    scene.render.filepath = out_path
    bpy.ops.render.render(write_still=True)
    return out_path
