"""Satellite map button icons for the two four-state overlays, Safety Zones (a solid ring) and Alliance Territory
(a dashed ring round a dot): Both (ring plus inward fade), Lines, Fade, Off (both, struck through).
White on a baked black outline, 64 x 64 RGBA, like the other MapBtn_* icons.

Usage: python tools/map_state_icons.py [--preview preview.png]
Writes Content/Textures/GUI/MapBtn_{Zone,Territory}{Both,Lines,Fade,Off}.dds (materials in Content/Data/Material.sbc).
"""
import argparse
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

GUI = Path(__file__).resolve().parent.parent / 'Content' / 'Textures' / 'GUI'
N = 256        # Drawn at 256, saved at 64
R = 100        # Ring centre-line radius
W = 20         # Ring width
OUT = 12       # Outline width (3 px at 64)
FADE = 62      # Fade depth inward from the ring's inner edge
FADE_A = 0.85  # Fade alpha at the ring
C = N / 2
_y, _x = np.mgrid[0:N, 0:N] + 0.5
RR = np.hypot(_x - C, _y - C)


def mask(draw_fn):
    m = Image.new('L', (N, N), 0)
    draw_fn(ImageDraw.Draw(m))
    return np.asarray(m, np.float32) / 255


def grow(m, px):
    img = Image.fromarray((m * 255).astype(np.uint8), 'L').filter(ImageFilter.MaxFilter(px * 2 + 1))
    return np.asarray(img, np.float32) / 255


def solid_ring(d):
    d.ellipse((C - R - W / 2, C - R - W / 2, C + R + W / 2, C + R + W / 2), outline=255, width=W)


def centre_dot(d):
    d.ellipse((C - 22, C - 22, C + 22, C + 22), fill=255)


def dashed_ring(d):
    box = (C - R - W / 2, C - R - W / 2, C + R + W / 2, C + R + W / 2)
    for i in range(10):
        d.arc(box, i * 36 + 6, i * 36 + 26, fill=255, width=W + 4)
    centre_dot(d)


def slash(w):
    return lambda d: d.line((44, 212, 212, 44), fill=255, width=w)


def over(dst, col, a):
    """Composite grey col (0..1) at alpha a over the (grey, alpha) image dst."""
    dc, da = dst
    oa = a + da * (1 - a)
    oc = np.where(oa > 0, (col * a + dc * da * (1 - a)) / np.maximum(oa, 1e-6), 0)
    return oc, oa


def icon(ring_fn, lines, fade, off):
    img = (np.zeros((N, N), np.float32), np.zeros((N, N), np.float32))
    inner = R - W / 2
    if fade:
        t = np.clip((RR - (inner - FADE)) / FADE, 0, 1) * (RR <= inner + 1)
        disc = (RR <= inner + 1).astype(np.float32)
        if not lines:
            img = over(img, 0.0, np.clip(grow(disc, OUT) - disc, 0, 1))
        img = over(img, 1.0, FADE_A * t ** 1.6)
        if not lines and ring_fn is dashed_ring:  # Territory keeps its centre dot, so its Fade differs from the zones'
            dot = mask(centre_dot)
            img = over(img, 0.0, grow(dot, OUT))
            img = over(img, 1.0, dot)
    if lines:
        ring = mask(ring_fn)
        img = over(img, 0.0, grow(ring, OUT))
        img = over(img, 1.0, ring)
    if off:
        img = over(img, 0.0, mask(slash(22 + OUT * 2)))
        img = over(img, 1.0, mask(slash(22)))
    col, a = img
    rgba = np.dstack([col, col, col, a])
    return Image.fromarray((rgba * 255 + 0.5).astype(np.uint8), 'RGBA').resize((64, 64), Image.LANCZOS)


STATES = (('Both', True, True, False), ('Lines', True, False, False), ('Fade', False, True, False), ('Off', True, True, True))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--preview', type=Path, help='Also write a preview sheet on the button backgrounds')
    args = ap.parse_args()
    made = []
    for kind, fn in (('Zone', solid_ring), ('Territory', dashed_ring)):
        for state, lines, fade, off in STATES:
            name = f'MapBtn_{kind}{state}'
            im = icon(fn, lines, fade, off)
            im.save(GUI / f'{name}.dds')
            made.append((name, im, state != 'Off'))
    if args.preview:
        cell = 160
        prev = Image.new('RGBA', (cell * 4 + 8, cell * 2 + 8), (20, 24, 28, 255))
        for k, (name, im, on) in enumerate(made):
            btn = Image.new('RGBA', (152, 152), (31, 41, 48, 255) if on else (18, 23, 27, 255))
            ic = im.resize((112, 112), Image.LANCZOS)
            if not on:  # The HUD dims a button that is off
                ic = Image.fromarray((np.asarray(ic, np.float32) * 0.32).astype(np.uint8), 'RGBA')
            btn.alpha_composite(ic, (20, 20))
            prev.alpha_composite(btn, (8 + (k % 4) * cell, 8 + (k // 4) * cell))
        prev.save(args.preview)
    print('wrote', ', '.join(m[0] for m in made))


if __name__ == '__main__':
    main()
