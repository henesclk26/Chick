"""Paints T_FarmBarn_Atlas.png: hand-painted swatches in the style of chicken_coop_handpainted, at the game's brightness.

Coop style, not realism: broad soft brush strokes, painted light (lit top edge, shaded lower edge), wobbly dark
outlines, shadows shifted towards purple and highlights towards orange. No wood grain, knots or cracks.

The 2048 x 2048 atlas holds 16 horizontal strips of 128 px (strip 0 at the top).
- Board strips tile along U: a board maps U along its length (world metres) and V across its width (0..1).
- Cell strips hold 8 separate painted pieces (stones, glass panes, leaves...) of 256 px each; a face maps one cell.
Run inside Blender: exec(...); paint_atlas(folder) -> writes T_FarmBarn_Atlas.png and T_FarmBarn_Emission.png.
"""
import os

import numpy as np

SIZE, STRIP, PAD = 2048, 128, 6
CELLS = 8

# name, kind, (shadow, base, highlight) sRGB 0-255
STRIPS = [
    ("red_a", "board", ((116, 30, 46), (196, 58, 48), (236, 108, 74))),
    ("red_b", "board", ((124, 36, 50), (206, 74, 54), (242, 128, 84))),
    ("cream", "board", ((168, 146, 146), (236, 222, 196), (253, 247, 230))),
    ("roof_a", "board", ((80, 42, 44), (142, 78, 52), (198, 124, 74))),
    ("roof_b", "board", ((90, 48, 48), (160, 92, 58), (210, 140, 86))),
    ("dark", "board", ((40, 28, 34), (86, 56, 44), (132, 92, 64))),
    ("floor", "board", ((118, 80, 62), (186, 134, 88), (226, 182, 122))),
    ("lining", "board", ((138, 98, 78), (208, 160, 108), (238, 202, 144))),
    ("stone", "cells", ((92, 94, 124), (160, 160, 170), (216, 212, 200))),
    ("hay", "hay", ((174, 116, 44), (236, 184, 72), (255, 228, 134))),
    ("glass", "glass", ((56, 104, 168), (118, 178, 226), (222, 244, 255))),
    ("metal", "board", ((34, 36, 52), (70, 72, 90), (132, 136, 152))),
    ("leaf", "cells", ((38, 92, 64), (96, 164, 64), (172, 216, 94))),
    ("sack", "cells", ((150, 122, 104), (216, 198, 162), (242, 232, 206))),
    ("glow", "glow", ((232, 150, 60), (255, 214, 110), (255, 250, 210))),
    ("barrel", "board", ((92, 50, 40), (158, 98, 60), (206, 148, 92))),
]
INDEX = {name: i for i, (name, _, _) in enumerate(STRIPS)}


def _rng(seed):
    return np.random.default_rng(seed)


def value_noise(w, h, cx, cy, rng):
    """Smooth value noise, periodic over w (cx cells) and h (cy cells)."""
    grid = rng.random((cy + 1, cx + 1))
    grid[:, -1] = grid[:, 0]
    grid[-1, :] = grid[0, :]
    xs = np.linspace(0, cx, w, endpoint=False)
    ys = np.linspace(0, cy, h, endpoint=False)
    x0, y0 = np.floor(xs).astype(int), np.floor(ys).astype(int)
    fx, fy = xs - x0, ys - y0
    fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
    a, b = grid[np.ix_(y0, x0)], grid[np.ix_(y0, x0 + 1)]
    c, d = grid[np.ix_(y0 + 1, x0)], grid[np.ix_(y0 + 1, x0 + 1)]
    top = a + (b - a) * fx[None, :]
    bot = c + (d - c) * fx[None, :]
    return top + (bot - top) * fy[:, None]


def blur(img, r, wrap_x=True):
    if r <= 0:
        return img
    out = img
    k = 2 * r + 1
    if wrap_x:
        out = sum(np.roll(out, s, axis=1) for s in range(-r, r + 1)) / k
    else:
        p = np.pad(out, [(0, 0), (r, r)] + [(0, 0)] * (out.ndim - 2), mode="edge")
        out = sum(p[:, r + s:r + s + out.shape[1]] for s in range(-r, r + 1)) / k
    p = np.pad(out, [(r, r)] + [(0, 0)] * (out.ndim - 1), mode="edge")
    return sum(p[r + s:r + s + out.shape[0]] for s in range(-r, r + 1)) / k


def ramp(t, pal):
    """t: 0 shadow .. 0.5 base .. 1 highlight."""
    s, b, h = (np.array(c, dtype=np.float64) / 255.0 for c in pal)
    t = np.clip(t, 0, 1)[..., None]
    return np.where(t < 0.5, s + (b - s) * (t / 0.5), b + (h - b) * ((t - 0.5) / 0.5))


def strokes(w, h, rng, count, length, width, along=True):
    """Soft elongated brush dabs (+/-), mostly along U."""
    t = np.zeros((h, w))
    yy, xx = np.mgrid[0:h, 0:w]
    for _ in range(count):
        cx, cy = rng.uniform(0, w), rng.uniform(0, h)
        ln, wd = rng.uniform(0.5, 1.0) * length, rng.uniform(0.6, 1.0) * width
        ang = rng.normal(0, 0.08) if along else rng.uniform(0, np.pi)
        dx = (xx - cx + w / 2) % w - w / 2
        dy = yy - cy
        u = dx * np.cos(ang) + dy * np.sin(ang)
        v = -dx * np.sin(ang) + dy * np.cos(ang)
        t += np.exp(-((u / ln) ** 2 + (v / wd) ** 2) * 2.5) * rng.choice([-1, 1]) * rng.uniform(0.5, 1.0)
    return t


def paint_board(pal, seed, w=SIZE, h=STRIP - 2 * PAD):
    rng = _rng(seed)
    v = np.linspace(0, 1, h)[:, None]  # 0 bottom edge .. 1 top edge
    t = np.full((h, w), 0.5)
    t += (value_noise(w, h, 5, 1, rng) - 0.5) * 0.3                 # big colour drift
    t += blur(strokes(w, h, rng, 70, 160, 9), 2) * 0.12             # broad brush strokes
    t += (value_noise(w, h, 22, 3, rng) - 0.5) * 0.1                # dabs
    top = 0.93 + (value_noise(w, 1, 7, 1, rng)[0] - 0.5) * 0.06
    bot = 0.06 + (value_noise(w, 1, 9, 1, rng)[0] - 0.5) * 0.06
    t += np.exp(-((v - (top[None, :] - 0.1)) / 0.08) ** 2) * 0.32    # painted light along the top edge
    t -= np.clip((0.45 - v) / 0.45, 0, 1) ** 1.5 * 0.34             # shade towards the lower edge
    col = ramp(t, pal)
    outline = (v > top[None, :]) | (v < bot[None, :])
    col[outline] = np.array(pal[0]) / 255.0 * 0.78
    return blur(col, 2)


def paint_cells(pal, seed, w=SIZE, h=STRIP - 2 * PAD, rounded=0.35):
    """8 rounded painted pieces with lit tops and a dark outline (stones, leaves, sacks)."""
    rng = _rng(seed)
    cell = w // CELLS
    out = np.zeros((h, w, 3))
    yy, xx = np.mgrid[0:h, 0:cell]
    u, v = xx / (cell - 1), 1 - yy / (h - 1)
    for i in range(CELLS):
        n = value_noise(cell, h, 3, 2, rng)
        e = np.minimum.reduce([u, 1 - u, v, 1 - v]) + (n - 0.5) * 0.05
        t = 0.5 + (n - 0.5) * 0.3
        t += (v - 0.5) * 0.45 + (0.5 - u) * 0.12
        t += np.exp(-((e - 0.1) / 0.05) ** 2) * 0.2 * np.clip((v - 0.4) / 0.3, 0, 1)
        t += blur(strokes(cell, h, rng, 6, 40, 10, along=False), 2, wrap_x=False) * 0.12
        c = ramp(t, pal)
        c[e < 0.03] = np.array(pal[0]) / 255.0 * 0.75
        out[:, i * cell:(i + 1) * cell] = c
    return blur(out, 1)


def paint_glass(pal, seed, w=SIZE, h=STRIP - 2 * PAD):
    rng = _rng(seed)
    cell = w // CELLS
    out = np.zeros((h, w, 3))
    yy, xx = np.mgrid[0:h, 0:cell]
    u, v = xx / (cell - 1), 1 - yy / (h - 1)
    for i in range(CELLS):
        t = 0.3 + v * 0.45 + (1 - u) * 0.15
        for off, wd in ((0.0, 0.08), (0.22, 0.04)):  # two diagonal shine strokes
            d = (u - 0.25 - off) + (v - 0.5) * 0.6
            t += np.exp(-(d / wd) ** 2) * 0.55 * np.clip(v * 1.4, 0, 1)
        c = ramp(t, pal)
        e = np.minimum.reduce([u, 1 - u, v, 1 - v])
        c[e < 0.04] = np.array(pal[0]) / 255.0 * 0.7
        out[:, i * cell:(i + 1) * cell] = c
    return blur(out, 1)


def paint_hay(pal, seed, w=SIZE, h=STRIP - 2 * PAD):
    rng = _rng(seed)
    t = 0.5 + (value_noise(w, h, 8, 2, rng) - 0.5) * 0.3
    t += blur(strokes(w, h, rng, 260, 50, 2.5), 1) * 0.35
    v = np.linspace(0, 1, h)[:, None]
    t += (v - 0.5) * 0.3
    return blur(ramp(t, pal), 1)


def paint_glow(pal, seed, w=SIZE, h=STRIP - 2 * PAD):
    yy, xx = np.mgrid[0:h, 0:w // CELLS]
    u, v = xx / (w // CELLS - 1), yy / (h - 1)
    t = 1.0 - np.sqrt((u - 0.5) ** 2 + (v - 0.5) ** 2) * 1.3
    cell = ramp(t, pal)
    return np.tile(cell, (1, CELLS, 1))


def paint_atlas(folder):
    import bpy

    img = np.zeros((SIZE, SIZE, 3))
    glow = np.zeros((SIZE, SIZE, 3))
    for i, (name, kind, pal) in enumerate(STRIPS):
        seed = 100 + i
        band = {"board": paint_board, "cells": paint_cells, "glass": paint_glass, "hay": paint_hay,
                "glow": paint_glow}[kind](pal, seed)
        y0 = i * STRIP
        img[y0 + PAD:y0 + STRIP - PAD] = band
        img[y0:y0 + PAD] = band[:1]
        img[y0 + STRIP - PAD:y0 + STRIP] = band[-1:]
        if name == "glow":
            glow[y0:y0 + STRIP] = img[y0:y0 + STRIP]
    out = {}
    for name, data in (("T_FarmBarn_Atlas", img), ("T_FarmBarn_Emission", glow)):
        rgba = np.concatenate([np.clip(data, 0, 1), np.ones((SIZE, SIZE, 1))], axis=2)[::-1]
        im = bpy.data.images.get(name)
        if im is not None and tuple(im.size) != (SIZE, SIZE):
            bpy.data.images.remove(im)
            im = None
        im = im or bpy.data.images.new(name, SIZE, SIZE, alpha=False)
        im.colorspace_settings.name = "sRGB"
        im.pixels.foreach_set(rgba.astype(np.float32).ravel())
        im.filepath_raw = os.path.join(folder, name + ".png")
        im.file_format = "PNG"
        im.save()
        out[name] = im
    return out


def strip_v(index, v):
    """Atlas V for v (0 bottom .. 1 top of the painted piece) inside strip `index`."""
    top = 1.0 - (index * STRIP + PAD) / SIZE
    bottom = 1.0 - ((index + 1) * STRIP - PAD) / SIZE
    return bottom + (top - bottom) * min(max(v, 0.0), 1.0)


def cell_u(cell, a):
    return (cell + 0.04 + 0.92 * min(max(a, 0.0), 1.0)) / CELLS
