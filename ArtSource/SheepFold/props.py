"""Sheep pasture props: timber sheep shelter, plank water trough with a hand pump, covered hay rack,
hay bales and the pasture gate.

Each prop's origin sits on the ground at its footprint centre (the gate leaf pivots on its hinge).
Fronts face -Y (Unity +Z after export). Unity model space is (-x, z, -y) of these Blender coordinates.
The trough's water surface is not modelled: Unity lays the animated pond water over it (see TROUGH_*).
"""
import math

from mathutils import Euler, Matrix, Vector

import sheepfold_lib as L

STONES = ("stone_light", "stone_mid", "stone_dark", "stone_warm")
WOODS = ("wood_light", "wood_mid", "wood_grey")
SHINGLES = ("shingle", "shingle", "shingle_dark", "shingle_light")
HAYS = ("hay", "hay", "hay_light", "hay_dark", "hay_green")

# Trough measurements Unity relies on (Blender metres): inside of the box, rim top, and the pump spout tip.
TROUGH_LENGTH, TROUGH_WIDTH, TROUGH_WALL = 1.8, .5, .055
TROUGH_FLOOR, TROUGH_RIM = .11, .235
TROUGH_SPOUT = (.76, 0.0, .58)


def frame(x_axis, up):
    """Rotation whose local X runs along x_axis and local Z is as close to 'up' as possible."""
    x = Vector(x_axis).normalized()
    z = Vector(up).normalized()
    y = z.cross(x).normalized()
    z = x.cross(y).normalized()
    return Matrix((x, y, z)).transposed()


def stone_footing(b, start, end, height, depth):
    """Low dry stone footing along a line: staggered courses of irregular stones."""
    rng = b.rng
    a, c = Vector((start[0], start[1], 0)), Vector((end[0], end[1], 0))
    axis = c - a
    length = axis.length
    x = axis.normalized()
    side = Vector((-x.y, x.x, 0))
    yaw = math.atan2(x.y, x.x)
    z, course = 0.0, 0
    while z < height - .03:
        h = min(rng.uniform(.1, .14), height - z)
        t = -(course % 2) * rng.uniform(.06, .14)
        while t < length:
            w = rng.uniform(.18, .34)
            t0, t1 = max(t, 0.0), min(t + w, length)
            if t1 - t0 > .05:
                mid = a + x * ((t0 + t1) / 2) + side * rng.uniform(-.012, .012)
                swatch = "moss" if rng.random() < .07 else rng.choice(STONES)
                b.box((mid.x, mid.y, z + h / 2), (t1 - t0 + .012, depth + rng.uniform(-.02, .03), h + .012), swatch,
                      rotation=Euler((rng.uniform(-.04, .04), rng.uniform(-.04, .04), yaw + rng.uniform(-.04, .04))),
                      bevel=.02, jitter=.01)
            t += w
        z += h
        course += 1


def straws(b, center, extent, count, length=(.07, .16), lift=.35):
    """Loose straw stalks scattered over a box-shaped region (extent = half sizes)."""
    rng = b.rng
    for _ in range(count):
        p = Vector(center) + Vector((rng.uniform(-1, 1) * extent[0], rng.uniform(-1, 1) * extent[1],
                                     rng.uniform(-1, 1) * extent[2]))
        b.box(p, (rng.uniform(*length), .007, .007), rng.choice(HAYS),
              rotation=Euler((0, rng.uniform(-lift, lift), rng.uniform(0, math.tau))))


def straw_bedding(b, center, extent, count):
    """A trodden straw layer: one low mound with ragged edges, a few lighter tufts and loose stalks."""
    rng = b.rng
    c = Vector(center)
    balls = []
    for _ in range(count):
        p = c + Vector((rng.uniform(-1, 1) * extent[0], rng.uniform(-1, 1) * extent[1], -.07))
        balls.append((tuple(p), (rng.uniform(.3, .5), rng.uniform(.25, .4), rng.uniform(.1, .14))))
    hay_heap(b, balls, "straw_bed", resolution=.06, decimate=.35)
    hay_lumps(b, (c.x, c.y, .04), (extent[0] * .8, extent[1] * .8, .01), count // 3, size=(.07, .12))
    straws(b, (c.x, c.y, .055), (extent[0], extent[1], .015), count * 3, lift=.15)


def hay_lumps(b, center, extent, count, size=(.08, .14)):
    rng = b.rng
    for _ in range(count):
        p = Vector(center) + Vector((rng.uniform(-1, 1) * extent[0], rng.uniform(-1, 1) * extent[1],
                                     rng.uniform(-1, 1) * extent[2]))
        s = rng.uniform(*size)
        b.ico(Matrix.Translation(p) @ Matrix.Rotation(rng.uniform(0, math.tau), 4, "Z") @
              Matrix.Diagonal((s * 1.35, s, s * .7, 1)), rng.choice(HAYS), subdivisions=1, jitter=.018)


def hay_heap(b, elements, swatch="hay", resolution=.045, decimate=.5, matrix=None):
    """One soft continuous mound of hay from metaball (centre, (sx, sy, sz)) ellipsoids."""
    # Metaball surfaces sit well inside the element sizes at this threshold; grow them to the sizes given.
    grown = [(c, tuple(v * 1.7 for v in size)) for c, size in elements]
    mesh = L.metaball_mesh("hay_heap", grown, resolution=resolution, threshold=.6, decimate=decimate)
    rng = b.rng
    for v in mesh.vertices:
        v.co += Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))) * resolution * .25
    b.mesh(mesh, matrix or Matrix.Identity(4), swatch, smooth=False)
    import bpy
    bpy.data.meshes.remove(mesh)


def hay_bale(b, center, yaw=0.0, size=(.9, .46, .38)):
    """A square bale pressed from flakes, tied with two twine loops, loose stalks poking out."""
    rng = b.rng
    rot = Euler((0, 0, yaw))
    m = rot.to_matrix()
    c = Vector(center)
    sx, sy, sz = size
    flakes = 7
    fw = sx / flakes
    for i in range(flakes):
        off = Vector((-sx / 2 + fw * (i + .5), rng.uniform(-.006, .006), rng.uniform(-.004, .004)))
        b.box(c + m @ off, (fw + .006, sy + rng.uniform(-.012, .004), sz + rng.uniform(-.01, .004)),
              rng.choice(("hay", "hay", "hay_warm", "hay_pale")),
              rotation=Euler((rng.uniform(-.02, .02), 0, yaw + rng.uniform(-.015, .015))), bevel=.014, jitter=.005)
    for f in (-.27, .27):
        x = f * sx
        for cy, cz, ly, lz in ((0, sz / 2 + .004, sy + .016, .01), (0, -sz / 2 - .002, sy + .016, .01),
                               (sy / 2 + .006, 0, .01, sz + .012), (-sy / 2 - .006, 0, .01, sz + .012)):
            b.box(c + m @ Vector((x, cy, cz)), (.016, ly, lz), "twine", rotation=rot)
        b.box(c + m @ Vector((x + .012, -sy / 2 - .01, sz * .1)), (.03, .014, .03), "twine", rotation=rot)
    for _ in range(16):
        r = rng.random()
        if r < .45:
            p = Vector((rng.uniform(-.45, .45) * sx, rng.uniform(-.45, .45) * sy, sz / 2))
        elif r < .8:
            p = Vector((rng.choice((-.5, .5)) * sx, rng.uniform(-.45, .45) * sy, rng.uniform(-.45, .45) * sz))
        else:
            p = Vector((rng.uniform(-.45, .45) * sx, rng.choice((-.5, .5)) * sy, rng.uniform(-.4, .45) * sz))
        b.box(c + m @ p, (rng.uniform(.06, .14), .007, .007), rng.choice(HAYS),
              rotation=Euler((0, rng.uniform(-.6, .6), yaw + rng.uniform(0, math.tau))))


def hay_bales(collection, material, location):
    single = L.Builder(31)
    hay_bale(single, (0, 0, .19))
    out = [single.finish("HayBale", collection, material, location)]
    stack = L.Builder(37)
    hay_bale(stack, (-.47, 0, .19), .02)
    hay_bale(stack, (.47, .02, .19), -.03)
    hay_bale(stack, (.02, -.03, .57), .12)
    straws(stack, (0, -.1, .015), (.9, .45, .005), 24, lift=.1)
    out.append(stack.finish("HayBaleStack", collection, material, Vector(location) + Vector((0, 1.3, 0))))
    return out


def shingle_roof(b, half_w, half_d, eave, ridge, over_x, over_y):
    """Gable roof (ridge along X): rafters, laths, staggered wooden shingles, ridge cap and barge boards."""
    rng = b.rng
    k = (ridge - eave) / half_d
    for sign in (-1, 1):
        up_slope = Vector((0, -sign, k)).normalized()
        normal = Vector((0, sign * k, 1)).normalized()
        start = Vector((0, sign * (half_d + over_y), eave - over_y * k))
        top = Vector((0, 0, ridge))
        slope_len = (top - start).length
        for x in [-half_w + i * (2 * half_w) / 5 for i in range(6)]:
            a = start + Vector((x, 0, 0)) - normal * .055
            b.plank(a - up_slope * .05, Vector((x, 0, ridge)) - normal * .055, .07, .1, "wood_dark", up=normal)
        s = .15
        while s < slope_len - .05:
            p = start + up_slope * s + normal * .015
            b.plank((-half_w - over_x, p.y, p.z), (half_w + over_x, p.y, p.z), .06, .025, "wood_mid", up=normal)
            s += .34
        row, s = 0, 0.0
        exposure, shingle_len = .16, .27
        while s < slope_len - .12:
            x = -half_w - over_x - (row % 2) * .08
            lift = .04 + .006 * (row % 2)
            while x < half_w + over_x - .02:
                w = min(rng.uniform(.13, .21), half_w + over_x - x)
                if w > .06:
                    swatch = "moss" if (row < 3 and sign > 0 and rng.random() < .1) else rng.choice(SHINGLES)
                    centre = start + up_slope * min(s + shingle_len / 2, slope_len - shingle_len / 2 + .02) + \
                        normal * lift + Vector((x + w / 2, 0, 0))
                    rot = frame((1, 0, 0), normal) @ Matrix.Rotation(sign * -.06 + rng.uniform(-.02, .02), 3, "X")
                    b.box(centre, (w - .012, shingle_len, .022), swatch,
                          rotation=(rot @ Matrix.Rotation(rng.uniform(-.03, .03), 3, "Z")).to_euler(), jitter=.003)
                x += w
            s += exposure
            row += 1
        # Ridge cap board and the barge boards along both gable edges.
        cap = top - up_slope * .09 + normal * .085
        b.plank((-half_w - over_x - .04, cap.y, cap.z), (half_w + over_x + .04, cap.y, cap.z), .2, .035, "wood_dark",
                up=normal)
        for x in (-half_w - over_x - .03, half_w + over_x + .03):
            b.plank(start + Vector((x, 0, 0)) + normal * .02, top + Vector((x, 0, .04)), .2, .04, "wood_mid",
                    up=(1, 0, 0))
        fascia = start + normal * .0 - up_slope * .01
        b.plank((-half_w - over_x, fascia.y, fascia.z), (half_w + over_x, fascia.y, fascia.z), .14, .04, "wood_mid",
                up=(0, sign, 0))


def board_wall(b, start, end, bottom, top_at, outward, battens=True):
    """Vertical board-and-batten cladding from start to end; top_at(point) gives each board's top height."""
    rng = b.rng
    a, c = Vector((start[0], start[1], 0)), Vector((end[0], end[1], 0))
    run = c - a
    length = run.length
    along = run.normalized()
    out = Vector(outward).normalized()
    t = 0.0
    while t < length - .02:
        w = min(rng.uniform(.17, .25), length - t)
        p = a + along * (t + w / 2)
        top = top_at(p)
        b.plank((p.x, p.y, bottom), (p.x, p.y, top), w - .008, .035, rng.choice(WOODS), up=out, jitter=.004)
        if battens and t > 0:
            q = a + along * t + out * .022
            b.plank((q.x, q.y, bottom + .02), (q.x, q.y, top_at(q) - .02), .05, .02, "wood_dark", up=out)
        t += w


def lantern(b, hang):
    """Hurricane lantern hanging on a hook from 'hang'."""
    h = Vector(hang)
    b.box(h - Vector((0, 0, .03)), (.015, .015, .06), "metal_dark")
    b.cylinder(h - Vector((0, 0, .06)), h - Vector((0, 0, .1)), .025, .025, "metal_dark", segments=6, caps=False)
    base = h - Vector((0, 0, .38))
    b.box(base, (.13, .13, .03), "metal_dark", bevel=.006)
    b.box(base + Vector((0, 0, .1)), (.09, .09, .16), "lantern_glass", bevel=.01)
    for dx in (-.055, .055):
        for dy in (-.055, .055):
            b.box(base + Vector((dx, dy, .1)), (.012, .012, .19), "metal_dark")
    b.cylinder(base + Vector((0, 0, .19)), base + Vector((0, 0, .26)), .08, .025, "metal_dark", segments=8, smooth=False)


def bucket(b, base):
    p = Vector(base)
    b.cylinder(p, p + Vector((0, 0, .3)), .135, .165, "wood_mid", segments=12, smooth=False)
    b.cylinder(p + Vector((0, 0, .3)), p + Vector((0, 0, .302)), .155, .155, "water", segments=12, smooth=False)
    for z, r in ((.05, .142), (.25, .162)):
        b.cylinder(p + Vector((0, 0, z)), p + Vector((0, 0, z + .03)), r + .006, r + .009, "metal_dark", segments=12,
                   smooth=False)
    # Wire bail: a continuous arc over the top, hooked into the rim on both sides.
    arc = [p + Vector((math.cos(math.pi * i / 8) * .17, 0, .3 + math.sin(math.pi * i / 8) * .15)) for i in range(9)]
    for q0, q1 in zip(arc, arc[1:]):
        b.cylinder(q0, q1, .007, .007, "metal_dark", segments=5)


def pitchfork(b, foot, top):
    f, t = Vector(foot), Vector(top)
    b.cylinder(f, t, .017, .017, "wood_light", segments=6)
    d = (t - f).normalized()
    side = d.cross(Vector((0, 1, 0))).normalized()
    b.box(t, (.02, .02, .02), "metal_dark")
    b.plank(t - side * .1, t + side * .1, .025, .02, "metal_dark", up=Vector((0, 1, 0)))
    for o in (-.09, 0, .09):
        root = t + side * o
        b.cylinder(root, root + d * .28, .008, .004, "metal", segments=5)


def shelter(collection, material, location):
    """Open-fronted timber sheep shelter on a stone footing: board-and-batten walls on three sides, gable roof
    of wooden shingles, straw bedding, a hay manger on the back wall, lantern, bucket, pitchfork and bales."""
    b = L.Builder(43)
    rng = b.rng
    w, d, eave, ridge, plinth = 4.4, 3.0, 2.05, 3.0, .28
    hx, hy = w / 2, d / 2
    post = .16
    gap = post / 2 + .018

    def roof_z(p):
        return ridge - (ridge - eave) * abs(p.y) / hy - .05

    stone_footing(b, (-hx - .12, hy + .04), (hx + .12, hy + .04), plinth, .34)
    for x in (-hx - .04, hx + .04):
        stone_footing(b, (x, -hy - .1), (x, hy - .1), plinth, .34)
    for x in (-hx, -.75, hx):
        b.box((x, -hy, .1), (.34, .32, .2), rng.choice(STONES), bevel=.03, jitter=.012)
    # Frame: posts, sill beams, wall plates at the eaves and tie beams across.
    for x, y in ((-hx, hy), (0, hy), (hx, hy), (-hx, 0), (hx, 0)):
        b.box((x, y, (plinth + eave) / 2), (post, post, eave - plinth), "wood_mid", bevel=.015, jitter=.004)
    for x in (-hx, -.75, hx):
        b.box((x, -hy, (.2 + eave) / 2), (post, post, eave - .2), "wood_mid", bevel=.015, jitter=.004)
    b.plank((-hx - .1, hy, plinth + .05), (hx + .1, hy, plinth + .05), .14, .1, "wood_dark", up=(0, 0, 1))
    for x in (-hx, hx):
        b.plank((x, -hy, plinth + .05), (x, hy, plinth + .05), .14, .1, "wood_dark", up=(0, 0, 1))
    for y in (-hy, hy):
        b.plank((-hx - .25, y, eave - .07), (hx + .25, y, eave - .07), .16, .14, "wood_dark", up=(0, 1, 0))
    for x in (-hx, 0, hx):
        b.plank((x, -hy - .12, eave - .02), (x, hy + .12, eave - .02), .12, .12, "wood_dark", up=(1, 0, 0))
        b.box((x, 0, (eave + ridge) / 2 - .05), (.1, .1, ridge - eave - .1), "wood_dark", bevel=.01)
    # Knee braces where the open front's posts meet the wall plate.
    for x, s in ((-.75, 1), (-.75, -1), (hx, -1)):
        b.plank((x, -hy, eave - .55), (x + s * .45, -hy, eave - .1), .09, .07, "wood_mid", up=(0, 1, 0))
    # Cladding: back wall, both gable walls (boards reach up under the roof), half wall left of the opening.
    board_wall(b, (-hx - .08, hy + gap), (hx + .08, hy + gap), plinth + .1, lambda p: eave - .14, (0, 1, 0))
    for s in (-1, 1):
        board_wall(b, (s * (hx + gap), -hy - .08), (s * (hx + gap), hy + .08), plinth + .1, roof_z, (s, 0, 0))
    z = .25
    while z < 1.1:
        b.plank((-hx, -hy + gap, z + .1), (-.75, -hy + gap, z + .1), .19, .04, rng.choice(WOODS), up=(0, 1, 0),
                jitter=.005)
        z += .21
    b.plank((-hx, -hy + gap + .03, 1.2), (-.75, -hy + gap + .03, 1.2), .1, .06, "wood_dark", up=(0, 0, 1))
    shingle_roof(b, hx, hy, eave + .05, ridge + .05, .32, .42)
    # Inside.
    straw_bedding(b, (.2, .15, .015), (hx - .45, hy - .45), 70)
    mx0, mx1, my = -1.75, .35, hy - .05
    for x in (mx0, mx1):
        b.plank((x, my, .5), (x, my - .45, 1.25), .06, .05, "wood_mid", up=(1, 0, 0))
    b.plank((mx0, my - .45, 1.25), (mx1, my - .45, 1.25), .07, .06, "wood_light", up=(0, 0, 1))
    b.plank((mx0, my - .02, .55), (mx1, my - .02, .55), .08, .06, "wood_dark", up=(0, 0, 1))
    x = mx0 + .1
    while x < mx1 - .05:
        b.plank((x, my - .03, .57), (x, my - .43, 1.23), .03, .03, "wood_grey", up=(1, 0, 0))
        x += .12
    hay_heap(b, [((x, my - .2 + rng.uniform(-.05, .05), rng.uniform(.95, 1.15)), (rng.uniform(.18, .24), .16, .16))
                 for x in [mx0 + .2 + i * .17 for i in range(12)]], "hay")
    hay_lumps(b, ((mx0 + mx1) / 2, my - .22, 1.22), ((mx1 - mx0) / 2 - .15, .1, .04), 10, size=(.06, .1))
    straws(b, ((mx0 + mx1) / 2, my - .35, .95), ((mx1 - mx0) / 2 - .05, .1, .2), 26, lift=1.2)
    hay_bale(b, (1.45, .95, .19), 1.57 + .05)
    hay_bale(b, (1.5, .95, .57), 1.57 - .08)
    hay_bale(b, (.95, 1.0, .19), 1.57 + .02, )
    lantern(b, (.95, -hy + .02, eave - .14))
    bucket(b, (-1.6, -1.0, .0))
    pitchfork(b, (-hx + .45, .55, .02), (-hx + .17, .6, 1.55))
    return b.finish("FoldShelter", collection, material, location)


def water_trough(collection, material, location):
    """Plank trough banded with iron, fed by a cast iron hand pump at the +X end, with a stepping stone on the
    -Y side low enough for a chick to hop onto the rim."""
    b = L.Builder(53)
    rng = b.rng
    length, width, t = TROUGH_LENGTH, TROUGH_WIDTH, TROUGH_WALL
    hl, hw = length / 2, width / 2
    rim = TROUGH_RIM
    for x in (-hl + .25, hl - .25):
        b.box((x, 0, .03), (.12, width + .18, .06), "wood_dark", bevel=.012, jitter=.004)
    b.box((0, 0, (TROUGH_FLOOR + .06) / 2), (length - .01, width - .01, TROUGH_FLOOR - .06), "wood_dark", bevel=.008)
    for s in (-1, 1):
        y = s * (hw - t / 2)
        for z0, z1 in ((.06, .145), (.145, rim - .025)):
            b.box((0, y + rng.uniform(-.003, .003), (z0 + z1) / 2), (length, t, z1 - z0 - .004), rng.choice(WOODS),
                  bevel=.008, jitter=.004)
        b.plank((-hl - .03, s * (hw - .03), rim - .0125), (hl + .03, s * (hw - .03), rim - .0125), .1, .025,
                "wood_light", up=(0, 0, 1), jitter=.003)
        x = s * (hl - t / 2)
        b.box((x, 0, (.06 + rim - .025) / 2), (t, width - 2 * t, rim - .085), rng.choice(WOODS), bevel=.008)
        b.plank((s * (hl - .03), -hw - .03, rim - .0125), (s * (hl - .03), hw + .03, rim - .0125), .1, .025,
                "wood_light", up=(0, 0, 1))
    # Iron bands and corner straps with rivet heads.
    for x in (-hl + .45, hl - .45):
        for s in (-1, 1):
            b.box((x, s * (hw + .004), .13), (.04, .008, .17), "metal_dark")
            for z in (.08, .18):
                b.box((x, s * (hw + .01), z), (.014, .008, .014), "metal")
        b.box((x, 0, .062), (.04, width + .016, .008), "metal_dark")
    for sx in (-1, 1):
        for sy in (-1, 1):
            b.box((sx * (hl - .06), sy * (hw + .005), .14), (.11, .008, .03), "rust")
            b.box((sx * (hl + .005), sy * (hw - .06), .14), (.008, .11, .03), "rust")
    # Hand pump on a timber block at the +X end; its spout reaches over the water.
    px = hl + .25
    stone_footing(b, (px - .24, 0), (px + .24, 0), .14, .44)
    b.box((px, 0, .14 + .13), (.3, .3, .26), "wood_dark", bevel=.02, jitter=.005)
    base = .4
    b.cylinder((px, 0, base), (px, 0, base + .06), .1, .085, "iron_green_dark", segments=10, smooth=False)
    b.cylinder((px, 0, base + .06), (px, 0, .86), .065, .06, "iron_green", segments=12)
    for z in (.62, .84):
        b.cylinder((px, 0, z), (px, 0, z + .03), .08, .08, "iron_green_dark", segments=12, smooth=False)
    b.sphere(Matrix.Translation((px, 0, .89)) @ Matrix.Diagonal((.07, .07, .05, 1)), "iron_green", segments=12, rings=6)
    b.box((px, 0, .95), (.03, .03, .05), "metal_dark")
    spout = Vector(TROUGH_SPOUT)
    elbow = Vector((spout.x + .05, 0, spout.z + .07))
    b.cylinder((px - .05, 0, elbow.z + .02), elbow, .028, .026, "iron_green", segments=8)
    b.cylinder(elbow, spout, .026, .03, "iron_green", segments=8)
    b.cylinder(spout + Vector((0, 0, .01)), spout - Vector((0, 0, .005)), .033, .033, "iron_green_dark", segments=8,
               smooth=False)
    pivot = Vector((px + .02, 0, .97))
    grip = Vector((px + .5, 0, 1.12))
    b.box(pivot, (.07, .05, .03), "metal_dark", bevel=.004)
    b.cylinder(pivot, grip, .016, .014, "metal_dark", segments=6)
    b.sphere(Matrix.Translation(grip) @ Matrix.Diagonal((.03, .03, .03, 1)), "wood_dark", segments=8, rings=5)
    b.cylinder(pivot + Vector((-.03, 0, -.02)), pivot + Vector((-.03, 0, -.12)), .008, .008, "metal_dark", segments=5)
    # Stepping stones on the -Y side: ground -> stone -> rim, each a chick hop.
    b.box((-.35, -hw - .2, .06), (.44, .3, .12), "stone_light", rotation=Euler((0, 0, .08)), bevel=.035, jitter=.012)
    b.box((-.62, -hw - .3, .03), (.26, .22, .06), "stone_mid", rotation=Euler((0, 0, -.2)), bevel=.025, jitter=.01)
    for _ in range(5):
        a = rng.uniform(0, math.tau)
        p = Vector((px, 0, 0)) + Vector((math.cos(a), math.sin(a), 0)) * rng.uniform(.3, .4)
        s = rng.uniform(.04, .07)
        b.ico(Matrix.Translation(p) @ Matrix.Diagonal((s * 1.3, s, s * .6, 1)), rng.choice(STONES), jitter=.006)
    return b.finish("FoldWaterTrough", collection, material, location)


def hay_rack(collection, material, location):
    """Covered hay rack: slatted V rack heaped with hay over a catch trough, under a small shingled gable roof."""
    b = L.Builder(61)
    rng = b.rng
    hl, hw, plate = .85, .45, 1.5
    for x in (-hl, hl):
        for y in (-hw, hw):
            b.box((x, y, plate / 2), (.1, .1, plate), "wood_mid", bevel=.012, jitter=.004)
        b.plank((x, -hw, .45), (x, hw, plate - .3), .07, .05, "wood_dark", up=(1, 0, 0))
    for y in (-hw, hw):
        b.plank((-hl - .12, y, plate), (hl + .12, y, plate), .1, .1, "wood_dark", up=(0, 1, 0))
    # Catch trough.
    b.box((0, 0, .3), (2 * hl - .1, 2 * hw - .02, .04), "wood_grey", bevel=.008)
    for s in (-1, 1):
        b.plank((-hl, s * (hw - .02), .4), (hl, s * (hw - .02), .4), .18, .035, rng.choice(WOODS), up=(0, 1, 0))
        b.box((s * (hl - .03), 0, .4), (.035, 2 * hw - .06, .18), rng.choice(WOODS), bevel=.006)
    hay_heap(b, [((rng.uniform(-hl + .2, hl - .2), rng.uniform(-.2, .2), .3), (rng.uniform(.2, .3), .2, .1))
                 for _ in range(9)], "hay_dark")
    # V rack.
    top, bottom = 1.22, .62
    for s in (-1, 1):
        b.plank((-hl, s * (hw - .06), top), (hl, s * (hw - .06), top), .07, .06, "wood_light", up=(0, 0, 1))
    b.plank((-hl, 0, bottom), (hl, 0, bottom), .08, .07, "wood_dark", up=(0, 0, 1))
    x = -hl + .12
    while x < hl - .06:
        for s in (-1, 1):
            b.plank((x, s * (hw - .07), top - .02), (x, s * .03, bottom + .03), .03, .03, "wood_grey", up=(1, 0, 0))
        x += .12
    heap = []
    for i in range(13):
        x = -hl + .22 + i * (2 * hl - .44) / 12
        heap.append(((x, 0, .8), (.12, .1, .14)))
        heap.append(((x, rng.uniform(-.05, .05), 1.05), (.13, .19, .16)))
        heap.append(((x, rng.uniform(-.06, .06), rng.uniform(1.22, 1.3)), (.13, .21, .13)))
    hay_heap(b, heap, "hay")
    hay_lumps(b, (0, 0, 1.36), (hl - .2, .2, .04), 14, size=(.06, .1))
    straws(b, (0, 0, .95), (hl - .05, hw - .02, .25), 40, lift=1.3)
    shingle_roof(b, hl, hw, plate + .08, plate + .5, .14, .2)
    straws(b, (0, 0, .015), (hl + .2, hw + .25, .005), 30, lift=.1)
    return b.finish("FoldHayRack", collection, material, location)


def gate(collection, material, location, opening=2.8):
    """Pasture gate for one farm fence bay: two capped posts, and a five-bar leaf (child, origin on the hinge)."""
    posts = L.Builder(71)
    half = opening / 2
    for x in (-half, half):
        posts.box((x, 0, .78), (.18, .18, 1.56), "fence_wood_dark", bevel=.02, jitter=.005)
        posts.box((x, 0, 1.585), (.23, .23, .05), "fence_wood", bevel=.012)
        posts.box((x, 0, 1.63), (.14, .14, .045), "fence_wood_dark", bevel=.012)
        posts.box((x, 0, .04), (.3, .3, .08), "stone_mid", bevel=.025, jitter=.01)
    for z in (.32, 1.08):
        posts.box((-half + .1, 0, z), (.05, .04, .04), "metal_dark")
    posts.box((half - .1, 0, .98), (.04, .07, .1), "metal_dark")
    post_obj = posts.finish("PastureGate_Posts", collection, material, location)
    leaf = L.Builder(73)
    rng = leaf.rng
    lw, z0, h = opening - .25, .16, 1.12
    leaf.box((.05, 0, z0 + h / 2), (.1, .08, h + .04), "fence_wood_dark", bevel=.01)
    leaf.box((lw - .04, 0, z0 + h / 2), (.08, .07, h), "fence_wood_dark", bevel=.01)
    for i in range(5):
        z = z0 + .05 + i * (h - .1) / 4
        hgt = .11 if i == 4 else .085
        leaf.box((lw / 2, 0, z), (lw - .06, .045, hgt), rng.choice(("fence_wood", "fence_wood", "wood_grey")),
                 bevel=.008, jitter=.003)
    leaf.plank((.1, .035, z0 + .06), (lw - .08, .035, z0 + h - .06), .085, .04, "fence_wood", up=(0, 1, 0))
    for z in (z0 + .05, z0 + h - .05):
        leaf.box((.18, -.03, z), (.32, .012, .04), "metal_dark")
        leaf.box((.03, -.03, z), (.05, .03, .05), "metal_dark")
    leaf.box((lw, 0, z0 + h - .3), (.08, .02, .03), "metal_dark")
    leaf_obj = leaf.finish("PastureGate_Leaf", collection, material, Vector(location) + Vector((-half + .12, 0, 0)))
    leaf_obj.parent = post_obj
    leaf_obj.location = (-half + .12, 0, 0)
    return [post_obj, leaf_obj]


def build_all(collection, material, origin=(3.2, 0, 0)):
    o = Vector(origin)
    made = [shelter(collection, material, o + Vector((3.2, 1.5, 0)))]
    made.append(water_trough(collection, material, o + Vector((8.0, -.8, 0))))
    made.append(hay_rack(collection, material, o + Vector((8.0, 1.8, 0))))
    made += hay_bales(collection, material, o + Vector((10.6, -.6, 0)))
    made += gate(collection, material, o + Vector((13.6, .4, 0)))
    made.append(egg_basket(collection, material, o + Vector((16.4, -.4, 0))))
    made.append(wheelbarrow(collection, material, o + Vector((18.6, -.4, 0))))
    made.append(rake(collection, material, o + Vector((20.2, -.4, 0))))
    made.append(farm_bucket(collection, material, o + Vector((21.2, -.4, 0))))
    made.append(straw_patch(collection, material, o + Vector((22.8, -.4, 0))))
    return made


def diorama(origin=(0, 14, 0)):
    """Preview only (not exported): the props and a few sheep arranged as in the pasture, as linked duplicates."""
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

    place("FoldShelter", (0, 3.2, 0), 0)
    place("FoldWaterTrough", (3.8, -.6, 0), 90)
    place("FoldHayRack", (-3.9, .2, 0), 90)
    place("HayBaleStack", (3.4, 3.4, 0), -20)
    place("HayBale", (-2.6, 4.2, 0), 15)
    place("PastureGate_Posts", (0, -5.5, 0), 0)
    for name, pos, yaw in (("Sheep", (-1.2, -1.6, 0), 20), ("Sheep", (1.6, .6, 0), 150), ("Sheep", (-2.0, 1.9, 0), 250),
                           ("Lamb", (-.4, -.8, 0), -30), ("Lamb", (2.2, -2.3, 0), 110), ("Sheep", (2.9, -.9, 0), 200)):
        place(name, pos, yaw)
    return col


# Egg basket by the barn (Unity: EggBasket). Eggs rest on the straw lining at BASKET_FLOOR, inside BASKET_INNER.
BASKET_TOP_RADIUS, BASKET_BOTTOM_RADIUS, BASKET_HEIGHT = .38, .3, .28
BASKET_FLOOR, BASKET_INNER = .07, .3


def egg_basket(collection, material, location):
    """Round woven harvest basket with a braided rim, an arched handle and a straw lining."""
    b = L.Builder(81)
    rng = b.rng
    top, r0, r1 = BASKET_HEIGHT, BASKET_BOTTOM_RADIUS, BASKET_TOP_RADIUS
    b.cylinder((0, 0, 0), (0, 0, .04), r0, r0, "wicker_dark", segments=20, smooth=False)
    rows, segments = 9, 26
    for k in range(rows):
        z = .04 + (k + .5) * (top - .04) / rows
        radius = r0 + (r1 - r0) * (z / top)
        for i in range(segments):
            a = (i + .5 * (k % 2)) * math.tau / segments
            out = .007 if (i + k) % 2 else -.003
            p = Vector((math.cos(a) * (radius + out), math.sin(a) * (radius + out), z))
            seg = math.tau * radius / segments
            b.box(p, (seg * 1.08, .026, (top - .04) / rows + .004),
                  "wicker_light" if (i + k) % 3 == 0 else ("wicker" if (i + k) % 2 else "wicker_dark"),
                  rotation=Euler((0, 0, a + math.pi / 2)), bevel=.006)
    for i in range(34):
        a = i * math.tau / 34
        p = Vector((math.cos(a) * (r1 + .01), math.sin(a) * (r1 + .01), top + .012))
        b.box(p, (.075, .034, .032), "wicker_dark" if i % 2 else "wicker",
              rotation=Euler((.5 if i % 2 else -.5, 0, a + math.pi / 2)), bevel=.01)
    # Handle: two twisted strands arching over the basket along X.
    for strand in (0, 1):
        pts = []
        for j in range(17):
            t = j / 16
            a = math.pi * t
            twist = .014 * math.sin(t * math.pi * 8 + strand * math.pi)
            pts.append(Vector((-math.cos(a) * (r1 - .01), twist, top + math.sin(a) * .3)))
        for p0, p1 in zip(pts, pts[1:]):
            b.cylinder(p0, p1, .014, .014, "wicker_dark" if strand else "wicker", segments=6)
    # Straw lining the floor and walls; the eggs sit on it.
    for _ in range(26):
        a = rng.uniform(0, math.tau)
        rr = rng.uniform(0, BASKET_INNER - .14)
        s = rng.uniform(.07, .1)
        b.ico(Matrix.Translation((math.cos(a) * rr, math.sin(a) * rr, BASKET_FLOOR - .02)) @
              Matrix.Diagonal((s * 1.3, s, .025, 1)), rng.choice(("hay", "hay_light", "straw_bed")), jitter=.01)
    straws(b, (0, 0, BASKET_FLOOR), (BASKET_INNER - .12, BASKET_INNER - .12, .01), 30, length=(.06, .1), lift=.3)
    return b.finish("EggBasket", collection, material, location)


def wheelbarrow(collection, material, location):
    """Old wooden wheelbarrow: plank tray on two handles, one spoked wheel at the front (-Y), a little hay inside."""
    b = L.Builder(91)
    rng = b.rng
    # Handles run from the wheel axle (front, -Y) back and up to the grips.
    for s in (-1, 1):
        b.plank((s * .2, -.62, .26), (s * .26, .62, .5), .06, .05, "wood_dark", up=(0, 0, 1))
        b.box((s * .26, .66, .52), (.05, .12, .05), "wood_light", bevel=.012)
        b.plank((s * .22, .25, .38), (s * .22, .3, 0), .05, .05, "wood_dark", up=(0, 1, 0))
    # Tray: bottom, sloped sides and a high front board.
    b.box((0, -.05, .36), (.5, .72, .04), "wood_mid", bevel=.008)
    for s in (-1, 1):
        b.plank((s * .29, -.38, .48), (s * .29, .3, .48), .22, .035, rng.choice(WOODS), up=(s, 0, .35), jitter=.004)
    b.plank((-.29, -.42, .5), (.29, -.42, .5), .26, .035, "wood_light", up=(0, -1, .4))
    b.plank((-.29, .31, .45), (.29, .31, .45), .16, .035, "wood_mid", up=(0, 1, .2))
    b.box((0, -.05, .385), (.52, .74, .012), "metal_dark")
    # Wheel with rim, hub and spokes, on its axle.
    wc = Vector((0, -.66, .2))
    b.cylinder(wc + Vector((-.035, 0, 0)), wc + Vector((.035, 0, 0)), .2, .2, "wood_dark", segments=16, smooth=False)
    b.cylinder(wc + Vector((-.04, 0, 0)), wc + Vector((.04, 0, 0)), .205, .205, "metal_dark", segments=16, smooth=False, caps=False)
    b.cylinder(wc + Vector((-.06, 0, 0)), wc + Vector((.06, 0, 0)), .045, .045, "metal", segments=8)
    b.cylinder(wc + Vector((-.24, 0, 0)), wc + Vector((.24, 0, 0)), .015, .015, "metal_dark", segments=6)
    hay_heap(b, [((rng.uniform(-.12, .12), rng.uniform(-.25, .2), .42), (.14, .16, .07)) for _ in range(6)], "hay")
    straws(b, (0, -.05, .48), (.2, .3, .03), 14, lift=.6)
    return b.finish("Wheelbarrow", collection, material, location)


def rake(collection, material, location):
    """Hay rake lying ready to lean on a wall: long handle along +Z, wooden head with pegs at the bottom."""
    b = L.Builder(93)
    b.cylinder((0, 0, .06), (0, 0, 1.55), .018, .016, "wood_light", segments=6)
    b.box((0, 0, .06), (.5, .045, .05), "wood_mid", bevel=.008)
    for i in range(9):
        x = -.22 + i * .055
        b.cylinder((x, -.01, .05), (x, -.06, -.04), .007, .005, "wood_light", segments=5)
    b.plank((-.12, 0, .08), (0, 0, .32), .03, .02, "wood_mid", up=(0, 1, 0))
    b.plank((.12, 0, .08), (0, 0, .32), .03, .02, "wood_mid", up=(0, 1, 0))
    return b.finish("Rake", collection, material, location)


def farm_bucket(collection, material, location):
    """The shelter's water bucket as its own prop."""
    b = L.Builder(95)
    bucket(b, (0, 0, 0))
    return b.finish("FarmBucket", collection, material, location)


def straw_patch(collection, material, location):
    """A loose patch of trampled straw for the ground around the shelter (about 1.4 m across)."""
    b = L.Builder(97)
    rng = b.rng
    hay_heap(b, [((rng.uniform(-.5, .5), rng.uniform(-.4, .4), -.05), (rng.uniform(.18, .3), rng.uniform(.15, .25), .07))
                 for _ in range(10)], "straw_bed", resolution=.06, decimate=.35)
    hay_lumps(b, (0, 0, .02), (.5, .4, .01), 8, size=(.05, .09))
    straws(b, (0, 0, .03), (.7, .55, .01), 60, lift=.15)
    return b.finish("StrawPatch", collection, material, location)
