"""Paints the barn's plank floor texture in the style of the hand-painted barn's inside walls (handpaint_barn.py):
warm red-brown boards 0.34 m wide, every board a slightly different value, lit along one edge and shaded along the
other, with soft brush strokes, faint grain, dark painted seams between the boards and staggered butt joints.
It tiles both ways; one tile is 2.72 m (8 boards) square, boards running along U.

Run (Blender 5.x, background):
    blender -b --factory-startup --python ArtSource/BarnFloor/make_floor_texture.py -- <project root>
Writes Assets/Art/Barn/T_BarnFloor.png.
"""
import bpy, os, sys
import numpy as np

PROJECT = sys.argv[sys.argv.index("--") + 1]
OUT = os.path.join(PROJECT, "Assets", "Art", "Barn", "T_BarnFloor.png")
SIZE, BOARDS = 1024, 8
rng = np.random.default_rng(1905)

# The inside walls' wood (sRGB): shaded edge, body, lit edge, seam.
SHADOW = np.array((72, 45, 33)) / 255
BASE = np.array((112, 74, 52)) / 255
HIGH = np.array((140, 98, 68)) / 255
SEAM = np.array((48, 30, 24)) / 255


def periodic_noise(cells_x, cells_y, seed):
    """Smooth value noise that wraps around both ways (cosine-interpolated random grid)."""
    g = np.random.default_rng(seed).random((cells_y, cells_x))
    u = np.arange(SIZE) / SIZE * cells_x
    v = np.arange(SIZE) / SIZE * cells_y
    x0 = np.floor(u).astype(int); fx = u - x0; x1 = (x0 + 1) % cells_x; x0 %= cells_x
    y0 = np.floor(v).astype(int); fy = v - y0; y1 = (y0 + 1) % cells_y; y0 %= cells_y
    fx = (1 - np.cos(fx * np.pi)) / 2; fy = (1 - np.cos(fy * np.pi)) / 2
    a = g[np.ix_(y0, x0)]; b = g[np.ix_(y0, x1)]; c = g[np.ix_(y1, x0)]; d = g[np.ix_(y1, x1)]
    top = a + (b - a) * fx[None, :]; bottom = c + (d - c) * fx[None, :]
    return top + (bottom - top) * fy[:, None]


v = (np.arange(SIZE) + .5) / SIZE * BOARDS          # across the boards: board index + fraction
u = (np.arange(SIZE) + .5) / SIZE                   # along the boards, 0..1 over the tile
row = np.floor(v).astype(int)
across = v - row                                    # 0 at one edge of a board, 1 at the other

# Painted light across each board: shaded edge -> body -> lit edge, the hand-over moved about by brush strokes.
strokes = periodic_noise(3, 4, 1) * .6 + periodic_noise(7, 9, 2) * .4
t = np.clip(across[:, None] + (strokes - .5) * .7, 0, 1)
lower = SHADOW + (BASE - SHADOW) * np.clip(t * 2.4, 0, 1)[..., None]
upper = BASE + (HIGH - BASE) * np.clip((t - .62) * 2.2, 0, 1)[..., None]
soft = np.clip((t - .46) / .18, 0, 1); soft = soft * soft * (3 - 2 * soft)
col = lower + (upper - lower) * soft[..., None]

# Butt joints: one or two per row of boards, staggered; each board between joints its own value.
img = np.empty((SIZE, SIZE, 3))
joint_dist = np.ones((SIZE, SIZE))
for r in range(BOARDS):
    rows = row == r
    joints = np.sort(rng.random(1)) if rng.random() < .4 else np.sort((rng.random() + np.array((0, rng.uniform(.35, .65)))) % 1)
    k = 1 + (rng.random(len(joints)) - .5) * .18
    piece = np.searchsorted(joints, u) % len(joints)          # wraps: the last piece runs on into the first
    shade = k[piece]
    img[rows] = col[rows] * shade[None, :, None]
    d = np.min(np.abs(((u[:, None] - joints[None, :]) + .5) % 1 - .5), axis=1) * 2.72   # metres to a joint
    joint_dist[rows] = d[None, :]

# Grain: faint long streaks along the boards.
grain = periodic_noise(6, BOARDS * 9, 3)
img *= (.94 + .12 * grain)[..., None]

# Dark seams between the boards and at the joints, wobbling a little as if painted.
wobble = (periodic_noise(9, 5, 4) - .5) * .05
edge = np.minimum(across[:, None] + wobble, 1 - across[:, None] - wobble) * .34   # metres to a board edge
seam = np.clip(1 - np.minimum(edge, joint_dist) / .012, 0, 1)
img = img + (SEAM - img) * (seam * .8)[..., None]

img = np.clip(img, 0, 1)
out = np.ones((SIZE, SIZE, 4), np.float32)
out[..., :3] = img[::-1]                            # Blender images start at the bottom row
image = bpy.data.images.new("T_BarnFloor", SIZE, SIZE, alpha=False)
image.colorspace_settings.name = "sRGB"
image.pixels[:] = out.ravel()
os.makedirs(os.path.dirname(OUT), exist_ok=True)
image.filepath_raw = OUT; image.file_format = "PNG"; image.save()
print("SAVED", OUT)
