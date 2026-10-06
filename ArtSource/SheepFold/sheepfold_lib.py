"""Shared helpers for the sheep fold models: palette texture, part-coloured bmesh building.

Style matches the farm: flat-colour low poly, one small palette texture (like Assets/Art/Barn/Palette.png).
Every face samples a single palette swatch, so the models need no unwrapping and stay one material.
Units are metres; Blender Z is up and characters face -Y.
"""
import math
import random

import bmesh
import bpy
from mathutils import Matrix, Vector

# sRGB swatches, laid out row by row in an 8 x 8 grid of 16 px cells.
PALETTE = [
    ("wool", (0.95, 0.92, 0.85)), ("wool_shade", (0.9, 0.87, 0.8)), ("face", (0.27, 0.24, 0.23)),
    ("face_light", (0.38, 0.34, 0.32)), ("hoof", (0.16, 0.14, 0.13)), ("ear_inner", (0.86, 0.62, 0.60)),
    ("eye", (0.05, 0.05, 0.06)), ("eye_glint", (0.98, 0.98, 0.98)),
    ("lamb_face", (0.90, 0.84, 0.76)), ("nose", (0.62, 0.45, 0.43)),
    ("stone_light", (0.74, 0.72, 0.66)), ("stone_mid", (0.62, 0.60, 0.55)), ("stone_dark", (0.49, 0.47, 0.44)),
    ("stone_warm", (0.68, 0.62, 0.53)), ("moss", (0.46, 0.53, 0.28)), ("moss_dark", (0.34, 0.42, 0.22)),
    ("wood_light", (0.69, 0.51, 0.33)), ("wood_mid", (0.55, 0.38, 0.24)), ("wood_dark", (0.39, 0.27, 0.18)),
    ("wood_grey", (0.60, 0.55, 0.48)), ("roof", (0.63, 0.31, 0.23)), ("roof_dark", (0.50, 0.24, 0.18)),
    ("hay", (0.88, 0.76, 0.43)), ("hay_dark", (0.75, 0.61, 0.31)), ("hay_light", (0.95, 0.86, 0.56)),
    ("twine", (0.56, 0.42, 0.25)), ("water", (0.33, 0.58, 0.66)), ("metal", (0.46, 0.48, 0.50)),
    ("metal_dark", (0.32, 0.33, 0.35)), ("soil", (0.47, 0.36, 0.25)),
]
GRID = 8
CELL = 16
INDEX = {name: i for i, (name, _) in enumerate(PALETTE)}
MATERIAL_NAME = "M_SheepFold"
IMAGE_NAME = "T_SheepFold_Palette"


def swatch_uv(name):
    i = INDEX[name]
    return ((i % GRID + .5) / GRID, 1 - (i // GRID + .5) / GRID)


def palette_image(path):
    size = GRID * CELL
    image = bpy.data.images.get(IMAGE_NAME)
    if image is None or tuple(image.size) != (size, size):
        if image is not None:
            bpy.data.images.remove(image)
        image = bpy.data.images.new(IMAGE_NAME, size, size, alpha=False)
    pixels = [0.0] * (size * size * 4)
    for i in range(GRID * GRID):
        color = PALETTE[i][1] if i < len(PALETTE) else (1.0, 0.0, 1.0)
        cx, cy = i % GRID, GRID - 1 - i // GRID
        for y in range(cy * CELL, (cy + 1) * CELL):
            for x in range(cx * CELL, (cx + 1) * CELL):
                p = (y * size + x) * 4
                pixels[p:p + 4] = [color[0], color[1], color[2], 1.0]
    image.pixels[:] = pixels
    image.filepath_raw = path
    image.file_format = "PNG"
    image.save()
    return image


def palette_material(image):
    mat = bpy.data.materials.get(MATERIAL_NAME) or bpy.data.materials.new(MATERIAL_NAME)
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    nodes.clear()
    out = nodes.new("ShaderNodeOutputMaterial")
    bsdf = nodes.new("ShaderNodeBsdfPrincipled")
    tex = nodes.new("ShaderNodeTexImage")
    tex.image = image
    tex.interpolation = "Closest"
    bsdf.inputs["Roughness"].default_value = .85
    links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    out.location, bsdf.location, tex.location = (300, 0), (0, 0), (-350, 0)
    return mat


class Builder:
    """Accumulates coloured parts into one bmesh. Each face stores its rig part id in the 'part' layer."""

    def __init__(self, seed=1):
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new("UVMap")
        self.part = self.bm.faces.layers.int.new("part")
        self.rng = random.Random(seed)

    def _tag(self, faces, swatch, part, smooth):
        u, v = swatch_uv(swatch)
        for f in faces:
            f.smooth = smooth
            f[self.part] = part
            for loop in f.loops:
                loop[self.uv].uv = (u, v)

    def _new_faces(self, before):
        return [f for f in self.bm.faces if f not in before]

    def sphere(self, matrix, swatch, part=0, segments=12, rings=8, smooth=True):
        before = set(self.bm.faces)
        bmesh.ops.create_uvsphere(self.bm, u_segments=segments, v_segments=rings, radius=1, matrix=matrix)
        faces = self._new_faces(before)
        self._tag(faces, swatch, part, smooth)
        return faces

    def ico(self, matrix, swatch, part=0, subdivisions=1, smooth=False, jitter=0.0):
        before = set(self.bm.faces)
        geom = bmesh.ops.create_icosphere(self.bm, subdivisions=subdivisions, radius=1, matrix=matrix)
        if jitter:
            for vert in geom["verts"]:
                vert.co += Vector((self.rng.uniform(-1, 1), self.rng.uniform(-1, 1), self.rng.uniform(-1, 1))) * jitter
        faces = self._new_faces(before)
        self._tag(faces, swatch, part, smooth)
        return faces

    def box(self, center, size, swatch, part=0, rotation=None, bevel=0.0, jitter=0.0, smooth=False):
        before = set(self.bm.faces)
        m = Matrix.Translation(center)
        if rotation is not None:
            m = m @ rotation.to_matrix().to_4x4()
        m = m @ Matrix.Diagonal((size[0], size[1], size[2], 1))
        geom = bmesh.ops.create_cube(self.bm, size=1, matrix=m)
        verts = geom["verts"]
        if jitter:
            for vert in verts:
                vert.co += Vector((self.rng.uniform(-1, 1), self.rng.uniform(-1, 1), self.rng.uniform(-1, 1))) * jitter
        if bevel:
            edges = list({e for v in verts for e in v.link_edges})
            bmesh.ops.bevel(self.bm, geom=edges, offset=bevel, offset_type="OFFSET", segments=1,
                            profile=.5, affect="EDGES", clamp_overlap=True)
        faces = self._new_faces(before)
        self._tag(faces, swatch, part, smooth)
        return faces

    def cylinder(self, start, end, radius_start, radius_end, swatch, part=0, segments=8, smooth=True, caps=True):
        before = set(self.bm.faces)
        a, b = Vector(start), Vector(end)
        axis = b - a
        rot = Vector((0, 0, 1)).rotation_difference(axis.normalized()).to_matrix().to_4x4()
        m = Matrix.Translation((a + b) * .5) @ rot
        bmesh.ops.create_cone(self.bm, cap_ends=caps, cap_tris=False, segments=segments,
                              radius1=radius_start, radius2=radius_end, depth=axis.length, matrix=m)
        faces = self._new_faces(before)
        self._tag(faces, swatch, part, smooth)
        return faces

    def plank(self, start, end, width, thickness, swatch, part=0, up=(0, 0, 1), bevel=.006, jitter=0.0):
        """A board running from start to end; 'up' is the board's thin axis."""
        a, b = Vector(start), Vector(end)
        length_axis = (b - a)
        x = length_axis.normalized()
        z = Vector(up).normalized()
        y = z.cross(x).normalized()
        z = x.cross(y).normalized()
        rot = Matrix((x, y, z)).transposed()
        return self.box((a + b) * .5, (length_axis.length, width, thickness), swatch, part,
                        rotation=rot.to_euler(), bevel=bevel, jitter=jitter)

    def mesh(self, mesh, matrix, swatch, part=0, smooth=True):
        before = set(self.bm.faces)
        temp = mesh.copy()
        temp.transform(matrix)
        self.bm.from_mesh(temp)
        bpy.data.meshes.remove(temp)
        faces = self._new_faces(before)
        self._tag(faces, swatch, part, smooth)
        return faces

    def finish(self, name, collection, material, location=(0, 0, 0)):
        bmesh.ops.remove_doubles(self.bm, verts=self.bm.verts, dist=.0001)
        old = bpy.data.objects.get(name)
        if old is not None:
            bpy.data.objects.remove(old, do_unlink=True)
        mesh = bpy.data.meshes.get(name)
        if mesh is not None:
            bpy.data.meshes.remove(mesh)
        mesh = bpy.data.meshes.new(name)
        self.bm.to_mesh(mesh)
        self.bm.free()
        mesh.materials.append(material)
        obj = bpy.data.objects.new(name, mesh)
        obj.location = location
        collection.objects.link(obj)
        return obj


def metaball_mesh(name, elements, resolution=.04, threshold=.6, decimate=1.0):
    """elements: (center, radius) balls or (center, (sx, sy, sz)) ellipsoids. Returns a Mesh datablock."""
    mball = bpy.data.metaballs.new(name)
    mball.resolution = resolution
    mball.render_resolution = resolution
    mball.threshold = threshold
    for center, radius in elements:
        e = mball.elements.new()
        e.co = center
        if isinstance(radius, tuple):
            e.type = "ELLIPSOID"
            e.radius = 1.0
            e.size_x, e.size_y, e.size_z = radius
        else:
            e.radius = radius
    obj = bpy.data.objects.new(name, mball)
    bpy.context.scene.collection.objects.link(obj)
    bpy.context.view_layer.update()
    deps = bpy.context.evaluated_depsgraph_get()
    mesh = bpy.data.meshes.new_from_object(obj.evaluated_get(deps))
    bpy.data.objects.remove(obj, do_unlink=True)
    bpy.data.metaballs.remove(mball)
    if decimate < 1.0:
        holder = bpy.data.objects.new(name + "_dec", mesh)
        bpy.context.scene.collection.objects.link(holder)
        mod = holder.modifiers.new("dec", "DECIMATE")
        mod.ratio = decimate
        bpy.context.view_layer.update()
        deps = bpy.context.evaluated_depsgraph_get()
        reduced = bpy.data.meshes.new_from_object(holder.evaluated_get(deps))
        bpy.data.objects.remove(holder, do_unlink=True)
        bpy.data.meshes.remove(mesh)
        mesh = reduced
    return mesh


def fibonacci_sphere(count, rng, jitter=.15):
    pts = []
    golden = math.pi * (3 - math.sqrt(5))
    for i in range(count):
        y = 1 - (i + .5) / count * 2
        r = math.sqrt(max(0.0, 1 - y * y))
        t = golden * i + rng.uniform(-jitter, jitter)
        pts.append(Vector((math.cos(t) * r, y, math.sin(t) * r)))
    return pts


def collection(name):
    col = bpy.data.collections.get(name)
    if col is None:
        col = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(col)
    return col
