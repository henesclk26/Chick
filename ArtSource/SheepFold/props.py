"""Sheep fold props: dry stone wall segments, lean-to shelter, water trough, hay rack, hay bales, fold gate.

Each prop's origin sits on the ground at its footprint centre (the gate leaf pivots on its hinge).
"""
import math

from mathutils import Euler, Matrix, Vector

import sheepfold_lib as L

STONES = ("stone_light", "stone_mid", "stone_dark", "stone_warm")
WOODS = ("wood_light", "wood_mid", "wood_grey")


def stone_wall(name, collection, material, location, seed, length=2.08):
    """Dry stone wall: courses of irregular stones, battered sides, upright coping stones on top.

    Laid out on a 2 m grid; the extra 8 cm overlaps the neighbour so angled joints in a curved wall stay closed.
    """
    b = L.Builder(seed)
    rng = b.rng
    height, base_depth, top_depth = .7, .56, .42
    z = 0.0
    course = 0
    while z < height - .04:
        h = rng.uniform(.12, .17)
        depth = base_depth + (top_depth - base_depth) * min(1.0, (z + h * .5) / height)
        x = -length / 2 + (course % 2) * rng.uniform(.08, .16) - .1
        while x < length / 2:
            w = rng.uniform(.2, .4)
            x0, x1 = max(x, -length / 2), min(x + w, length / 2)
            if x1 - x0 > .06:
                swatch = "moss" if rng.random() < .06 else rng.choice(STONES)
                rot = Euler((rng.uniform(-.04, .04), rng.uniform(-.05, .05), rng.uniform(-.04, .04)))
                b.box(((x0 + x1) / 2, rng.uniform(-.015, .015), z + h / 2),
                      (x1 - x0 + .015, depth + rng.uniform(-.02, .03), h + .012), swatch,
                      rotation=rot, bevel=.022, jitter=.012)
            x += w
        z += h
        course += 1
    # Coping: a row of stones stood on edge.
    x = -length / 2 + .04
    while x < length / 2 - .03:
        t = rng.uniform(.07, .12)
        swatch = rng.choice(("moss", "moss_dark")) if rng.random() < .12 else rng.choice(STONES)
        lean = Euler((0, rng.uniform(-.12, .12), rng.uniform(-.06, .06)))
        hh = rng.uniform(.14, .21)
        b.box((x + t / 2, 0, z + hh / 2 - .02), (t, top_depth + .04, hh), swatch, rotation=lean, bevel=.018, jitter=.01)
        x += t + rng.uniform(.0, .015)
    return b.finish(name, collection, material, location)


def hay_bale(b, center, yaw=0.0, size=(.9, .46, .38)):
    rot = Euler((0, 0, yaw))
    b.box(center, size, "hay", rotation=rot, bevel=.05, jitter=.008)
    m = rot.to_matrix()
    for offset in (-.24, .24):
        band = Vector(center) + m @ Vector((offset * size[0] / .9, 0, 0))
        b.box(band, (.025, size[1] + .012, size[2] + .012), "twine", rotation=rot, bevel=.006)
    # A few loose straws sticking out of the ends.
    for _ in range(5):
        end = b.rng.choice((-1, 1))
        tip = Vector(center) + m @ Vector((end * size[0] * .5, b.rng.uniform(-.17, .17), b.rng.uniform(-.13, .13)))
        b.box(tip, (.06, .008, .008), b.rng.choice(("hay_light", "hay_dark")),
              rotation=Euler((0, b.rng.uniform(-.5, .5), yaw + b.rng.uniform(-.6, .6))))


def hay_bales(collection, material, location):
    single = L.Builder(31)
    hay_bale(single, (0, 0, .19))
    out = [single.finish("HayBale", collection, material, location)]
    stack = L.Builder(37)
    hay_bale(stack, (-.47, 0, .19), .02)
    hay_bale(stack, (.47, .02, .19), -.03)
    hay_bale(stack, (.02, -.03, .57), .12)
    out.append(stack.finish("HayBaleStack", collection, material, Vector(location) + Vector((0, 1.3, 0))))
    return out


def straw_patch(b, center, radius, count):
    for _ in range(count):
        a, r = b.rng.uniform(0, math.tau), radius * math.sqrt(b.rng.random())
        p = Vector(center) + Vector((math.cos(a) * r, math.sin(a) * r * .8, 0))
        s = b.rng.uniform(.22, .4)
        b.ico(Matrix.Translation(p) @ Matrix.Diagonal((s * 1.3, s, .03, 1)),
              b.rng.choice(("hay", "hay_light", "hay_dark")), subdivisions=1, jitter=.01)


def shelter(collection, material, location):
    """Open-fronted lean-to (opening faces -Y), plank walls on three sides, sloped plank roof."""
    b = L.Builder(43)
    rng = b.rng
    w, d, front_h, back_h = 3.2, 2.2, 1.95, 1.45
    hx, hy = w / 2, d / 2
    post = .13

    def roof_z(y):
        return front_h + (back_h - front_h) * (y + hy) / d

    for x in (-hx, 0.0, hx):
        for y in (-hy, hy):
            if x == 0.0 and y < 0:
                continue
            b.box((x, y, roof_z(y) / 2), (post, post, roof_z(y)), "wood_mid", bevel=.012)
    # Back wall and the back half of both sides.
    z = .08
    while z < back_h - .12:
        b.plank((-hx, hy + .02, z + .1), (hx, hy + .02, z + .1), .2, .04, rng.choice(WOODS), up=(0, 1, 0), jitter=.006)
        z += .22
    for x in (-hx - .02, hx + .02):
        z = .08
        while z < back_h - .2:
            b.plank((x, -.1, z + .1), (x, hy, z + .1), .2, .04, rng.choice(WOODS), up=(1, 0, 0), jitter=.006)
            z += .22
    # Beams carrying the roof, then the roof planks running down the slope with an overhang.
    for y in (-hy, hy):
        b.plank((-hx - .1, y, roof_z(y) - .08), (hx + .1, y, roof_z(y) - .08), .16, .12, "wood_dark", up=(0, 1, 0))
    over = .3
    y0, y1 = -hy - over, hy + over
    x = -hx - .25
    while x < hx + .25:
        pw = rng.uniform(.2, .26)
        b.plank((x + pw / 2, y0, roof_z(y0) + .02), (x + pw / 2, y1, roof_z(y1) + .02), pw - .015, .05,
                rng.choice(("roof", "roof", "roof_dark")), up=(0, (front_h - back_h) / d, 1), jitter=.008)
        x += pw
    straw_patch(b, (0, .25, .015), 1.25, 16)
    hay_bale(b, (-.9, .55, .19), .1)
    hay_bale(b, (-.85, .58, .57), -.06)
    return b.finish("FoldShelter", collection, material, location)


def water_trough(collection, material, location):
    b = L.Builder(53)
    length, width, height = 1.6, .52, .42
    t = .05
    b.box((0, 0, .08), (length, width, .06), "wood_dark", bevel=.01)
    for y in (-1, 1):
        b.box((0, y * (width / 2 - t / 2), height / 2 + .05), (length, t, height - .1), "wood_mid", bevel=.01, jitter=.004)
    for x in (-1, 1):
        b.box((x * (length / 2 - t / 2), 0, height / 2 + .05), (t, width, height - .1), "wood_light", bevel=.01)
        b.box((x * (length / 2 - .12), 0, height + .0), (.05, width + .03, .03), "metal_dark", bevel=.004)
        b.box((x * (length / 2 - .2), 0, .03), (.12, width + .12, .06), "wood_dark", bevel=.01)
    b.box((0, 0, height - .06), (length - 2 * t - .01, width - 2 * t - .01, .01), "water")
    return b.finish("FoldWaterTrough", collection, material, location)


def hay_rack(collection, material, location):
    """Free-standing V rack on four legs, heaped with hay."""
    b = L.Builder(61)
    rng = b.rng
    length, top, bottom, half = 1.6, 1.05, .5, .4
    for x in (-length / 2, length / 2):
        for y in (-half, half):
            b.box((x, y, top / 2), (.09, .09, top), "wood_mid", bevel=.01)
        b.plank((x, -half, top - .05), (x, half, top - .05), .08, .06, "wood_dark", up=(1, 0, 0))
    for y in (-half, half):
        b.plank((-length / 2, y, top - .02), (length / 2, y, top - .02), .07, .06, "wood_light", up=(0, 0, 1))
    b.plank((-length / 2, 0, bottom), (length / 2, 0, bottom), .08, .07, "wood_dark", up=(0, 0, 1))
    x = -length / 2 + .1
    while x < length / 2 - .05:
        for y in (-half, half):
            b.plank((x, y, top - .03), (x, 0, bottom + .02), .035, .03, "wood_grey", up=(1, 0, 0))
        x += .13
    # Board tray under the rack catches dropped hay.
    b.box((0, 0, .18), (length - .1, half * 2 - .05, .04), "wood_grey", bevel=.008)
    for _ in range(46):
        p = Vector((rng.uniform(-.68, .68), rng.uniform(-.2, .2), top + rng.uniform(-.14, .05)))
        s = rng.uniform(.08, .13)
        b.ico(Matrix.Translation(p) @ Matrix.Diagonal((s * 1.3, s * .9, s * .6, 1)),
              rng.choice(("hay", "hay", "hay_light", "hay_dark")), subdivisions=1, jitter=.02)
    straw_patch(b, (0, 0, .2), .5, 5)
    return b.finish("FoldHayRack", collection, material, location)


def gate(collection, material, location):
    """Two posts plus a separate leaf whose origin is the hinge, so it can swing open in Unity."""
    posts = L.Builder(71)
    opening = 1.6
    for x in (0.0, opening + .14):
        posts.box((x, 0, .7), (.15, .15, 1.4), "wood_dark", bevel=.015, jitter=.004)
        posts.box((x, 0, 1.42), (.18, .18, .05), "wood_mid", bevel=.01)
    post_obj = posts.finish("FoldGate_Posts", collection, material, location)
    leaf = L.Builder(73)
    w, h, z0 = opening - .02, 1.0, .14
    for x in (.09, w - .02):
        leaf.box((x, 0, z0 + h / 2), (.08, .07, h), "wood_mid", bevel=.008)
    for z in (z0 + .06, z0 + h * .5, z0 + h - .05):
        leaf.box((w / 2 + .03, 0, z), (w - .05, .05, .1), "wood_light", bevel=.008, jitter=.003)
    leaf.plank((.12, 0, z0 + .08), (w - .05, 0, z0 + h - .08), .09, .045, "wood_light", up=(0, 1, 0))
    for z in (z0 + .2, z0 + h - .2):
        leaf.box((.12, .045, z), (.2, .015, .05), "metal_dark", bevel=.004)
    leaf_obj = leaf.finish("FoldGate_Leaf", collection, material, Vector(location) + Vector((.08, 0, 0)))
    leaf_obj.parent = post_obj
    leaf_obj.location = (.08, 0, 0)
    return [post_obj, leaf_obj]


def build_all(collection, material, origin=(3.2, 0, 0)):
    o = Vector(origin)
    made = [stone_wall("FoldWall_A", collection, material, o + Vector((1.0, 0, 0)), 101),
            stone_wall("FoldWall_B", collection, material, o + Vector((1.0, 1.1, 0)), 211)]
    made.append(shelter(collection, material, o + Vector((5.2, .6, 0))))
    made.append(water_trough(collection, material, o + Vector((8.6, -.4, 0))))
    made.append(hay_rack(collection, material, o + Vector((8.6, 1.2, 0))))
    made += hay_bales(collection, material, o + Vector((10.8, -.4, 0)))
    made += gate(collection, material, o + Vector((12.2, .2, 0)))
    return made


def diorama(origin=(0, 14, 0)):
    """Preview only (not exported): the props and sheep arranged as a fold, using linked duplicates."""
    import bpy
    col = L.collection("Fold Preview")
    for obj in list(col.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    c = Vector(origin)

    def place(source, pos, yaw):
        src = bpy.data.objects[source]
        obj = bpy.data.objects.new(source + " preview", src.data)
        obj.location = c + Vector(pos)
        obj.rotation_euler = (0, 0, math.radians(yaw))
        col.objects.link(obj)
        for child in src.children:
            sub = bpy.data.objects.new(child.name + " preview", child.data)
            sub.parent = obj
            sub.location = child.location
            col.objects.link(sub)
        return obj

    n, radius = 19, 6.0
    for i in range(n):
        angle = math.radians(-90 + i * 360 / n)
        pos = (math.cos(angle) * radius, math.sin(angle) * radius, 0)
        if i == 0:
            place("FoldGate_Posts", (pos[0] - .96, pos[1], 0), 0)
            continue
        place("FoldWall_A" if i % 2 else "FoldWall_B", pos, math.degrees(angle) + 90)
    place("FoldShelter", (0, 3.4, 0), 0)
    place("FoldWaterTrough", (3.1, -1.4, 0), 70)
    place("FoldHayRack", (-3.2, .4, 0), 90)
    place("HayBaleStack", (2.7, 3.4, 0), -20)
    place("HayBale", (-2.2, 3.9, 0), 15)
    for name, pos, yaw in (("Sheep", (-1.2, -1.6, 0), 20), ("Sheep", (1.6, .6, 0), 150), ("Sheep", (-2.0, 1.9, 0), 250),
                           ("Lamb", (-.4, -.8, 0), -30), ("Lamb", (2.2, -2.3, 0), 110), ("Sheep", (2.4, -9.5, 0), 200),
                           ("Lamb", (3.4, -10.1, 0), 160), ("Sheep", (-3.5, -8.8, 0), 60)):
        place(name, pos, yaw)
    return col
