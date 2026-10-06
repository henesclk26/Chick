"""Stylised sheep and lamb: lumpy metaball wool, dark face and legs, sideways ears.

Rig part ids stored per face (layer 'part'): 0 body, 1 head, 2 ear L, 3 ear R,
4 leg front L, 5 leg front R, 6 leg back L, 7 leg back R, 8 tail.
Faces -Y, feet at Z = 0.
"""
import math

from mathutils import Euler, Matrix, Vector

import sheepfold_lib as L

PARTS = {"body": 0, "head": 1, "ear.L": 2, "ear.R": 3, "leg.FL": 4, "leg.FR": 5, "leg.BL": 6, "leg.BR": 7, "tail": 8}

ADULT = dict(scale=1.0, body_center=(0, .02, .53), body_axes=(.27, .47, .24), lumps=48, lump_radius=(.11, .155),
             head_center=(0, -.60, .68), head_axes=(.105, .165, .115), head_pitch=20, leg_top=.36,
             leg_x=.15, leg_front=-.28, leg_back=.30, leg_radius=.042, tail=(0, .50, .56), face="face")
LAMB = dict(scale=.6, body_center=(0, .02, .62), body_axes=(.26, .41, .23), lumps=34, lump_radius=(.11, .15),
            head_center=(0, -.58, .80), head_axes=(.125, .175, .13), head_pitch=12, leg_top=.47,
            leg_x=.14, leg_front=-.25, leg_back=.27, leg_radius=.05, tail=(0, .45, .64), face="face")


def _scaled(v, k):
    return Vector(v) * k


def build(name, collection, material, location, params=ADULT, seed=7):
    p = params
    k = p["scale"]
    b = L.Builder(seed)
    rng = b.rng

    # Wool body: an ellipsoid core under overlapping round tufts. Separate spheres (not fused metaballs)
    # keep a crease between tufts, which is what makes the fleece read as fluffy.
    center = _scaled(p["body_center"], k)
    axes = _scaled(p["body_axes"], k)
    b.sphere(Matrix.Translation(center) @ Matrix.Diagonal((*(axes * .9), 1)), "wool", PARTS["body"], segments=16, rings=12)
    for d in L.fibonacci_sphere(p["lumps"], rng, jitter=.4):
        if d.z < -.7:
            continue
        out = rng.uniform(.9, .98)
        pos = center + Vector((d.x * axes.x * out, d.y * axes.y * out, d.z * axes.z * out))
        r = rng.uniform(*p["lump_radius"]) * k
        squash = Matrix.Diagonal((r, r, r * rng.uniform(.82, 1.0), 1))
        b.sphere(Matrix.Translation(pos) @ squash, "wool_shade" if rng.random() < .12 else "wool", PARTS["body"],
                 segments=10, rings=7)
    head_center = _scaled(p["head_center"], k)
    # Wool collar where the neck meets the body.
    collar = center.lerp(head_center, .6) + Vector((0, 0, .03 * k))
    b.sphere(Matrix.Translation(collar) @ Matrix.Diagonal((.17 * k, .15 * k, .17 * k, 1)), "wool", PARTS["body"],
             segments=12, rings=8)

    # Head: dark face with a rounded muzzle, a wool topknot, eyes and ears.
    pitch = Euler((math.radians(p["head_pitch"]), 0, 0)).to_matrix()
    ha = _scaled(p["head_axes"], k)

    def head_point(local):
        return head_center + pitch @ Vector(local)

    head_m = Matrix.Translation(head_center) @ pitch.to_4x4() @ Matrix.Diagonal((*ha, 1))
    b.sphere(head_m, p["face"], PARTS["head"], segments=14, rings=10)
    muzzle = head_point((0, -ha.y * .62, -ha.z * .28))
    b.sphere(Matrix.Translation(muzzle) @ pitch.to_4x4() @ Matrix.Diagonal((ha.x * .72, ha.y * .5, ha.z * .66, 1)),
             p["face"], PARTS["head"], segments=12, rings=8)
    for side in (-1, 1):
        nostril = head_point((side * ha.x * .28, -ha.y * 1.02, -ha.z * .2))
        b.sphere(Matrix.Translation(nostril) @ Matrix.Diagonal((.012 * k, .008 * k, .008 * k, 1)),
                 "hoof", PARTS["head"], segments=6, rings=4)
    # Wool topknot: a few tufts on the crown.
    for local, r in (((0, ha.y * .2, ha.z * .86), .075), ((-ha.x * .5, ha.y * .38, ha.z * .7), .062),
                     ((ha.x * .5, ha.y * .38, ha.z * .7), .062), ((0, ha.y * .55, ha.z * .72), .068)):
        b.sphere(Matrix.Translation(head_point(local)) @ Matrix.Diagonal((r * k, r * k, r * k, 1)), "wool",
                 PARTS["head"], segments=10, rings=7)
    for side in (-1, 1):
        eye = head_point((side * ha.x * .74, -ha.y * .42, ha.z * .3))
        b.sphere(Matrix.Translation(eye) @ Matrix.Diagonal((.021 * k, .021 * k, .021 * k, 1)), "eye", PARTS["head"],
                 segments=10, rings=6)
        glint = eye + Vector((side * .008, -.012, .009)) * k
        b.sphere(Matrix.Translation(glint) @ Matrix.Diagonal((.006 * k, .006 * k, .006 * k, 1)), "eye_glint",
                 PARTS["head"], segments=6, rings=4)
        ear_root = head_point((side * ha.x * .95, ha.y * .12, ha.z * .45))
        ear_rot = (pitch @ Euler((0, math.radians(side * 22), math.radians(side * -18))).to_matrix()).to_4x4()
        ear_part = PARTS["ear.L"] if side < 0 else PARTS["ear.R"]
        reach = Vector((side * .075 * k, 0, 0))
        b.sphere(Matrix.Translation(ear_root) @ ear_rot @ Matrix.Translation(reach) @
                 Matrix.Diagonal((.085 * k, .016 * k, .036 * k, 1)), p["face"], ear_part, segments=10, rings=6)
        b.sphere(Matrix.Translation(ear_root) @ ear_rot @ Matrix.Translation(reach + Vector((side * .006, -.009, 0)) * k) @
                 Matrix.Diagonal((.064 * k, .008 * k, .024 * k, 1)), "ear_inner", ear_part, segments=10, rings=6)

    # Legs with darker hooves.
    leg_parts = (("leg.FL", -1, p["leg_front"]), ("leg.FR", 1, p["leg_front"]),
                 ("leg.BL", -1, p["leg_back"]), ("leg.BR", 1, p["leg_back"]))
    for part, side, y in leg_parts:
        x = side * p["leg_x"] * k
        top_z = p["leg_top"] * k
        r = p["leg_radius"] * k
        b.cylinder((x, y * k, top_z), (x, y * k, .06 * k), r, r * .85, p["face"], PARTS[part], segments=8)
        b.cylinder((x, y * k, .065 * k), (x, y * k - .006 * k, 0), r * .95, r * 1.08, "hoof", PARTS[part], segments=8,
                   smooth=False)

    tail = _scaled(p["tail"], k)
    b.sphere(Matrix.Translation(tail) @ Matrix.Diagonal((.08 * k, .07 * k, .1 * k, 1)), "wool", PARTS["tail"],
             segments=10, rings=7)
    b.sphere(Matrix.Translation(tail + Vector((0, .02, -.07)) * k) @ Matrix.Diagonal((.06 * k, .055 * k, .07 * k, 1)),
             "wool_shade", PARTS["tail"], segments=8, rings=6)

    obj = b.finish(name, collection, material, location)
    obj["rig_parts"] = ",".join(PARTS.keys())
    return obj
