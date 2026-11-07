# Copyright (c) 2026 speakertorockets
# SPDX-License-Identifier: MIT

"""Generates the Multiplot.NET application icon: several coloured curves over a plot grid."""
import math, sys
from PIL import Image, ImageDraw

SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
SS = 8  # supersampling factor

TILE_TOP = (30, 64, 140)
TILE_BOTTOM = (16, 38, 92)
PLOT_BG = (250, 252, 255)
GRID = (200, 210, 228)
AXIS = (60, 70, 90)
CURVES = [(225, 45, 45), (20, 150, 90), (245, 150, 20)]


def curve(k, x):
    """Normalised curve k at x in [0,1] -> y in [0,1] (0 = bottom)."""
    if k == 0:
        return 0.64 + 0.24 * math.sin(2 * math.pi * (x * 1.1 + 0.05))
    if k == 1:
        return 0.15 + 0.7 * x ** 1.6
    return 0.24 + 0.13 * math.sin(2 * math.pi * x * 1.4 + 1.2)


def render(size):
    S = size * SS
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    # rounded tile with a vertical gradient
    margin = 0 if size <= 24 else S * 0.03
    radius = S * (0.12 if size <= 24 else 0.18)
    grad = Image.new("RGBA", (S, S))
    gd = ImageDraw.Draw(grad)
    for y in range(S):
        t = y / (S - 1)
        gd.line([(0, y), (S, y)], fill=tuple(int(a + (b - a) * t) for a, b in zip(TILE_TOP, TILE_BOTTOM)) + (255,))
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([margin, margin, S - 1 - margin, S - 1 - margin], radius=radius, fill=255)
    img.paste(grad, (0, 0), mask)

    # plot area
    pad = S * (0.14 if size <= 24 else 0.17)
    x0, y0, x1, y1 = pad, pad, S - pad, S - pad
    d.rounded_rectangle([x0, y0, x1, y1], radius=S * 0.03, fill=PLOT_BG + (255,))

    # grid (fewer lines at small sizes)
    n = 0 if size <= 20 else 2 if size <= 32 else 4
    gw = max(SS, S * 0.008)
    for i in range(1, n):
        gx = x0 + (x1 - x0) * i / n
        gy = y0 + (y1 - y0) * i / n
        d.line([(gx, y0), (gx, y1)], fill=GRID + (255,), width=int(gw))
        d.line([(x0, gy), (x1, gy)], fill=GRID + (255,), width=int(gw))

    # axes
    aw = max(SS, S * (0.035 if size <= 24 else 0.02))
    d.line([(x0, y0), (x0, y1), (x1, y1)], fill=AXIS + (255,), width=int(aw), joint="curve")

    # curves
    inset = S * 0.04
    cx0, cx1, cy0, cy1 = x0 + inset, x1 - inset, y0 + inset, y1 - inset
    lw = S * (0.075 if size <= 16 else 0.06 if size <= 32 else 0.045)
    for k, col in enumerate(CURVES):
        pts = []
        for i in range(101):
            x = i / 100
            pts.append((cx0 + (cx1 - cx0) * x, cy1 - (cy1 - cy0) * curve(k, x)))
        d.line(pts, fill=col + (255,), width=int(lw), joint="curve")
        r = lw / 2
        for px, py in (pts[0], pts[-1]):
            d.ellipse([px - r, py - r, px + r, py + r], fill=col + (255,))

    return img.resize((size, size), Image.LANCZOS)


if __name__ == "__main__":
    out = sys.argv[1]
    frames = [render(s) for s in SIZES]
    frames[-1].save(out, format="ICO", sizes=[(s, s) for s in SIZES], append_images=frames[:-1])
    if len(sys.argv) > 2:  # preview sheet
        sheet = Image.new("RGBA", (sum(SIZES) + 10 * len(SIZES), 256), (255, 255, 255, 255))
        x = 0
        for f in frames:
            sheet.paste(f, (x, 256 - f.size[1]), f)
            x += f.size[0] + 10
        sheet.save(sys.argv[2])
