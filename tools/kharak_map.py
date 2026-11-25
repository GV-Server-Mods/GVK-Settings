#!/usr/bin/env python3
"""Build the nav HUD's satellite map picture (KharakMercator) from the planet's own data files.

Renders an equirectangular map of PertamKharak in the look of the hand-made map (tan plains, olive basins, cream to
white highlands, thin dark contours, dark red roads), with nothing baked in that the HUD draws live (zones, stores).
Roads come from the material maps (Stone_03_Roads, red channel 225). They are thinned to centre lines on each cube
face at full resolution, where a junction keeps its painted shape, then projected onto the map and drawn at one width
everywhere, so they don't stretch east-west towards the poles as raw road pixels do.

Layout matches GVK_ZoneNavCommand.WorldToMapUV: left edge faces -Z, the middle +Z, top row -Y.

The cube-map lookup, the face order and the bilinear height sample are Merii's (planet_map.py, 2026-11), which follow
MyCubemapHelpers.CalculateSampleTexcoord exactly.

Usage:
  python tools/kharak_map.py <PlanetDataFiles/PertamKharak> --out map.png [--width 4096] [--fit <old map>]
--fit prints a colour ramp fitted to an old map, by altitude, for the RAMP table below.
"""
import argparse
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

FACES = ('front', 'back', 'left', 'right', 'up', 'down')  # SE face order 0..5
RADIUS = 30000.0
HILL_MIN, HILL_MAX = -0.05, 0.05      # PertamKharak <HillParams>, times the radius
ROAD_VALUE = 225                      # Stone_03_Roads in <CustomMaterialTable>

# Terrain colour by altitude (m), fitted to the hand-made map with --fit and smoothed
RAMP = [
    (-1100, (138, 126, 54)),
    (-700, (150, 138, 64)),
    (-400, (152, 141, 82)),
    (-200, (170, 157, 104)),
    (0, (179, 167, 112)),
    (200, (188, 176, 126)),
    (500, (196, 184, 146)),
    (800, (212, 203, 176)),
    (1100, (228, 222, 204)),
    (1400, (248, 247, 240)),
]
CONTOUR_M = 125.0                     # Contour interval (owner, 2026-11-24: 100 m showed the dry riverbeds but was busy)
CONTOUR_INDEX = 4                     # Every 4th contour (500 m) is drawn heavier...
CONTOUR_INDEX_OFFSET = 250.0          # ...from +250 m, so the plains' own 0 m line stays light
CONTOUR_SMOOTH_PX = 1.0               # Height blur for contours only (px at 2160 wide): drops tiny rings in the plains
CONTOUR_COLOR = (40, 38, 30)
LABEL_FONT = 'arial.ttf'              # Windows font folder
LABEL_PX = 15                         # Height label size, px at 4096 wide (the map is about 4096 px across at 3x zoom)
LABEL_GAP_PX = 300                    # Least distance between contour numbers, px at 4096 wide
PEAK_MIN_M = 600.0                    # Spot heights only on peaks at least this high...
PEAK_RADIUS_M = 3000.0                # ...and the highest point this far around
PEAK_MARK_PX = 5                      # Spot height triangle size, px at 4096 wide
WRAP_W = 4096                         # Set to the map width in main(), for spread()
ROAD_COLOR = (115, 10, 8)


def directions(W, H):
    """Unit direction per output pixel (Merii). Map centre faces +Z; top row = -Y."""
    lam = ((np.arange(W, dtype=np.float32) + 0.5) * 360.0 / W) * (np.pi / 180)
    phi = (-90.0 + (np.arange(H, dtype=np.float32) + 0.5) * 180.0 / H) * (np.pi / 180)
    cosphi, sinphi = np.cos(phi)[:, None], np.sin(phi)[:, None]
    ones = np.ones((1, W), np.float32)
    return cosphi * np.sin(lam)[None, :], sinphi * ones, -cosphi * np.cos(lam)[None, :]


def face_and_texcoord(dx, dy, dz):
    """MyCubemapHelpers.CalculateSampleTexcoord, vectorised (Merii)."""
    ax, ay, az = np.abs(dx), np.abs(dy), np.abs(dz)
    u = np.empty_like(dx)
    v = np.empty_like(dx)
    face = np.empty(dx.shape, np.int8)
    m = (ax > ay) & (ax > az)
    s = 1.0 / ax[m]
    u[m] = np.where(dx[m] > 0, -dz[m] * s, dz[m] * s)
    v[m] = -dy[m] * s
    face[m] = np.where(dx[m] > 0, 2, 3)
    m = (ax > ay) & ~(ax > az)
    s = 1.0 / az[m]
    u[m] = np.where(dz[m] > 0, dx[m] * s, -dx[m] * s)
    v[m] = -dy[m] * s
    face[m] = np.where(dz[m] > 0, 1, 0)
    m = ~(ax > ay) & (ay > az)
    s = 1.0 / ay[m]
    u[m] = np.where(dy[m] > 0, -dx[m] * s, dx[m] * s)
    v[m] = -dz[m] * s
    face[m] = np.where(dy[m] > 0, 4, 5)
    m = ~(ax > ay) & ~(ay > az)
    s = 1.0 / az[m]
    u[m] = np.where(dz[m] > 0, dx[m] * s, -dx[m] * s)
    v[m] = -dy[m] * s
    face[m] = np.where(dz[m] > 0, 1, 0)
    return face, np.clip((u + 1) * 0.5, 0.0, 0.999999), np.clip((v + 1) * 0.5, 0.0, 0.999999)


def gather_height(face, u, v, heights6):
    """Bilinear heightmap sample, 0..65535 (Merii)."""
    res = heights6.shape[1]
    tx = np.clip(u * (res - 1), 0, res - 1)
    ty = np.clip(v * (res - 1), 0, res - 1)
    x0 = np.clip(tx.astype(np.int32), 0, res - 2)
    y0 = np.clip(ty.astype(np.int32), 0, res - 2)
    fx, fy = tx - x0, ty - y0
    out = np.empty(u.shape, np.float32)
    for f in range(6):
        m = face == f
        if not m.any():
            continue
        M = heights6[f]
        a, b = M[y0[m], x0[m]], M[y0[m], x0[m] + 1]
        c, d = M[y0[m] + 1, x0[m]], M[y0[m] + 1, x0[m] + 1]
        out[m] = a * (1 - fx[m]) * (1 - fy[m]) + b * fx[m] * (1 - fy[m]) + c * (1 - fx[m]) * fy[m] + d * fx[m] * fy[m]
    return out


def gather_nearest(face, u, v, maps6):
    res = maps6.shape[1]
    tx = np.clip((u * (res - 1)).round(), 0, res - 1).astype(np.int32)
    ty = np.clip((v * (res - 1)).round(), 0, res - 1).astype(np.int32)
    return maps6[face, ty, tx]


def load(planet_dir):
    hs, ms = [], []
    for f in FACES:
        hs.append(np.asarray(Image.open(planet_dir / f'{f}.png')).astype(np.float32))
        ms.append(np.asarray(Image.open(planet_dir / f'{f}_mat.png').convert('RGB'))[..., 0])
    return np.stack(hs), np.stack(ms)


def shift(a, dy, dx):
    """a moved by (dy, dx): wraps east-west (the map seam), clamps north-south."""
    out = np.roll(a, dx, axis=1)
    if dy > 0:
        out = np.concatenate([np.repeat(out[:1], dy, axis=0), out[:-dy]], axis=0)
    elif dy < 0:
        out = np.concatenate([out[-dy:], np.repeat(out[-1:], -dy, axis=0)], axis=0)
    return out


def thin(mask):
    """Zhang-Suen thinning to one-pixel centre lines (numpy, wraps east-west)."""
    img = mask.copy()
    while True:
        changed = False
        for step in (0, 1):
            p2, p3, p4 = shift(img, 1, 0), shift(img, 1, -1), shift(img, 0, -1)
            p5, p6, p7 = shift(img, -1, -1), shift(img, -1, 0), shift(img, -1, 1)
            p8, p9 = shift(img, 0, 1), shift(img, 1, 1)
            nb = [p2, p3, p4, p5, p6, p7, p8, p9]
            b = sum(n.astype(np.uint8) for n in nb)
            a = sum(((~nb[i]) & nb[(i + 1) % 8]).astype(np.uint8) for i in range(8))
            if step == 0:
                c = ~(p2 & p4 & p6) & ~(p4 & p6 & p8)
            else:
                c = ~(p2 & p4 & p8) & ~(p2 & p6 & p8)
            kill = img & (b >= 2) & (b <= 6) & (a == 1) & c
            if kill.any():
                img &= ~kill
                changed = True
        if not changed:
            return img


def prune(skel, length):
    """Removes spurs up to length pixels: thinning a wide blob leaves short branches off its centre line."""
    s = skel.copy()
    for _ in range(length):
        n = sum(shift(s, dy, dx).astype(np.uint8) for dy in (-1, 0, 1) for dx in (-1, 0, 1) if dy or dx)
        ends = s & (n <= 1)  # Lone pixels too: the stub left of a short spur
        if not ends.any():
            break
        s &= ~ends
    # Grow the kept lines back along the original skeleton so real road ends keep their length
    for _ in range(length):
        grown = s.copy()
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                if dy or dx:
                    grown |= shift(s, dy, dx)
        grown &= skel
        if (grown == s).all():
            break
        s = grown
    return s


def face_centrelines(mats6):
    """Road centre lines per cube face, at the material map's own resolution (about 30 m a texel)."""
    out = np.zeros(mats6.shape, bool)
    for f in range(6):
        # A zero border keeps shift()'s east-west wrap from joining opposite face edges
        m = np.pad(mats6[f] == ROAD_VALUE, 1)
        pin = ~m & shift(m, 1, 0) & shift(m, -1, 0) & shift(m, 0, 1) & shift(m, 0, -1)
        out[f] = prune(thin(m | pin), 4)[1:-1, 1:-1]
    return out


def texel_to_map(f, ty, tx, res, W, H):
    """Map pixel (x, y) of face texels: the inverse of face_and_texcoord, then the projection in directions()."""
    a = 2.0 * tx / (res - 1) - 1.0
    b = 2.0 * ty / (res - 1) - 1.0
    one = np.ones_like(a)
    dx = np.select([f == 0, f == 1, f == 2, f == 3, f == 4, f == 5], [-a, a, one, -one, -a, a])
    dy = np.select([f <= 3, f == 4, f == 5], [-b, one, -one])
    dz = np.select([f == 0, f == 1, f == 2, f == 3, f >= 4], [-one, one, -a, a, -b])
    n = np.sqrt(dx * dx + dy * dy + dz * dz)
    lam = np.mod(np.arctan2(dx / n, -dz / n), 2 * np.pi)
    phi = np.arcsin(np.clip(dy / n, -1, 1))
    return lam / (2 * np.pi) * W - 0.5, (phi + np.pi / 2) / np.pi * H - 0.5


def road_lines(skel6, W, H, ss):
    """Rasterises the face centre lines onto a map ss times finer than W x H, one pixel wide, joined across texels."""
    res = skel6.shape[1]
    grid = np.zeros((H * ss, W * ss), bool)
    f, ty, tx = np.nonzero(skel6)
    x0, y0 = texel_to_map(f, ty, tx, res, W, H)
    lookup = np.full(skel6.shape, -1, np.int64)
    lookup[f, ty, tx] = np.arange(f.size)
    for oy, ox in ((0, 1), (1, 0), (1, 1), (1, -1)):
        ny, nx = ty + oy, tx + ox
        ok = (ny < res) & (nx >= 0) & (nx < res)
        j = np.full(f.size, -1, np.int64)
        j[ok] = lookup[f[ok], ny[ok], nx[ok]]
        i = np.nonzero(j >= 0)[0]
        j = j[i]
        ax, ay, bx, by = x0[i], y0[i], x0[j], y0[j]
        bx = np.where(bx - ax > W / 2, bx - W, np.where(ax - bx > W / 2, bx + W, bx))  # Across the seam
        steps = np.ceil(np.hypot(bx - ax, by - ay) * ss * 2).astype(np.int64) + 1
        seg = np.repeat(np.arange(i.size), steps)
        t = (np.arange(seg.size) - np.repeat(np.cumsum(steps) - steps, steps)) / np.repeat(steps, steps)
        px = (ax[seg] + (bx[seg] - ax[seg]) * t + 0.5) * ss - 0.5
        py = (ay[seg] + (by[seg] - ay[seg]) * t + 0.5) * ss - 0.5
        grid[np.clip(np.round(py).astype(np.int64), 0, H * ss - 1), np.mod(np.round(px).astype(np.int64), W * ss)] = True
    # Single texels with no neighbour still mark their own spot
    grid[np.clip(np.round((y0 + 0.5) * ss - 0.5).astype(np.int64), 0, H * ss - 1),
         np.mod(np.round((x0 + 0.5) * ss - 0.5).astype(np.int64), W * ss)] = True
    return grid


def box_blur(a, r):
    """Three box passes of radius r, about a Gaussian; wraps east-west."""
    if r <= 0:
        return a
    out = a
    for _ in range(3):
        for axis in (0, 1):
            acc = np.zeros_like(out)
            for d in range(-r, r + 1):
                acc += shift(out, d, 0) if axis == 0 else shift(out, 0, d)
            out = acc / (2 * r + 1)
    return out


def soft_disk(points, radius):
    """Coverage 0..1 of lines of the given half-width (px) round skeleton points: a distance field from shifts."""
    r = int(np.ceil(radius + 1))
    cover = np.zeros(points.shape, np.float32)
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            d = (dx * dx + dy * dy) ** 0.5
            w = np.clip(radius + 0.5 - d, 0.0, 1.0)
            if w > 0:
                cover = np.maximum(cover, shift(points, dy, dx).astype(np.float32) * w)
    return cover


def ramp_color(h):
    hs = np.array([p[0] for p in RAMP], np.float32)
    cs = np.array([p[1] for p in RAMP], np.float32)
    return np.stack([np.interp(h, hs, cs[:, i]) for i in range(3)], axis=-1)


def contour_cover(h, interval, width):
    """Anti-aliased contour lines of constant pixel width: distance to the nearest level over the local slope."""
    gy, gx = np.gradient(h)
    slope = np.sqrt(gx * gx + gy * gy) + 1e-3
    q = h / interval
    dist_px = np.abs(q - np.round(q)) * interval / slope
    return np.clip(width * 0.5 + 0.5 - dist_px, 0.0, 1.0), np.round(q).astype(np.int32)


def hillshade(h, px_m):
    gy, gx = np.gradient(h, px_m)
    nx, ny, nz = -gx, -gy, np.ones_like(h)
    n = np.sqrt(nx * nx + ny * ny + nz * nz)
    lx, ly, lz = -0.5, -0.5, 0.7071  # Light from the north-west
    return (nx * lx + ny * ly + nz * lz) / n


def max_filter(a, r):
    """Maximum over a (2r+1)-pixel square: doubling windows along each axis; wraps east-west, clamps north-south."""
    out = a
    for axis in (0, 1):
        m, p = out, 1
        while p * 2 <= 2 * r + 1:
            m = np.maximum(m, shift(m, -p, 0) if axis == 0 else shift(m, 0, -p))  # m[i] = max out[i .. i+2p-1]
            p *= 2
        k = r - p + 1  # Two windows of p pixels cover [i-r, i+r]
        out = np.maximum(shift(m, r, 0), shift(m, -k, 0)) if axis == 0 else np.maximum(shift(m, 0, r), shift(m, 0, -k))
    return out


def spread(cands, gap, taken=()):
    """Keeps candidates (x, y, ...) in order, dropping any within gap pixels of one already kept (east-west wraps)."""
    kept = list(taken)
    out = []
    for c in cands:
        if all(min(abs(c[0] - k[0]), WRAP_W - abs(c[0] - k[0])) ** 2 + (c[1] - k[1]) ** 2 >= gap * gap for k in kept):
            kept.append(c)
            out.append(c)
    return out


def text_sprite(text, font, fill, halo, halo_px):
    l, t, r, b = font.getbbox(text, stroke_width=halo_px)
    img = Image.new('RGBA', (r - l + 2, b - t + 2), (0, 0, 0, 0))
    ImageDraw.Draw(img).text((1 - l, 1 - t), text, font=font, fill=fill, stroke_width=halo_px, stroke_fill=halo)
    return img


def paste_centred(canvas, sprite, x, y):
    canvas.alpha_composite(sprite, (int(round(x - sprite.width / 2)), int(round(y - sprite.height / 2))))


def height_labels(img, base, h, hc, index_cover, near_road, W, H):
    """Spot heights on the peaks and elevation numbers along the heavier contours, tops facing uphill as on a
    printed topo map, but never upside down. Each label has a halo in the ground colour under it, which also breaks
    the contour line."""
    s = W / 4096.0
    font = ImageFont.truetype(LABEL_FONT, max(8, int(round(LABEL_PX * s))))
    halo_px = max(1, int(round(2 * s)))
    ink = CONTOUR_COLOR + (255,)
    canvas = img.convert('RGBA')
    draw = ImageDraw.Draw(canvas)

    def halo_at(x, y):
        return tuple(int(c) for c in base[int(y), int(x) % W]) + (255,)

    # Spot heights: the highest point within PEAK_RADIUS_M, if high enough
    r = int(round(PEAK_RADIUS_M / (2 * np.pi * RADIUS / W)))
    peak = (h >= max_filter(h, r)) & (h >= PEAK_MIN_M)
    peak[:r // 2] = peak[-(r // 2):] = False  # The poles' rows are one point stretched wide
    ys, xs = np.nonzero(peak)
    order = np.argsort(-h[ys, xs])
    peaks = spread([(int(xs[i]), int(ys[i]), float(h[ys[i], xs[i]])) for i in order], r)
    tri = PEAK_MARK_PX * s
    for x, y, z in peaks:
        draw.polygon([(x, y - tri), (x - tri * 0.9, y + tri * 0.6), (x + tri * 0.9, y + tri * 0.6)],
                     fill=ink, outline=halo_at(x, y))
        spr = text_sprite(f'{z:.0f}', font, ink, halo_at(x, y), halo_px)
        canvas.alpha_composite(spr, (int(x + tri * 1.3), int(y - spr.height / 2)))

    # Contour numbers: on an index line, where the next line is far enough off to fit the text, away from roads
    gy, gx = np.gradient(hc)
    slope = np.hypot(gx, gy) + 1e-6
    room = CONTOUR_M / slope  # Pixels to the next contour
    # Contours lie about 5 px apart at the median, so a number usually covers its neighbours a little, as on paper
    ok = (index_cover > 0.9) & (room > LABEL_PX * s * 0.6) & (room < LABEL_PX * s * 12) & ~near_road
    ok[:H // 12] = ok[-(H // 12):] = False  # Too stretched near the poles
    ys, xs = np.nonzero(ok)
    pick = np.argsort(np.abs(room[ys, xs] - LABEL_PX * s * 1.5))  # Prefer slopes where the number fits between lines
    gap = LABEL_GAP_PX * s
    clear = LABEL_PX * s * 4  # From a spot height
    cands = [(int(xs[i]), int(ys[i])) for i in pick[:200000]]
    cands = [c for c in cands if all(abs(c[0] - p[0]) > clear or abs(c[1] - p[1]) > clear for p in peaks)]
    labels = spread(cands, gap)
    period = CONTOUR_M * CONTOUR_INDEX
    for x, y in labels:
        level = np.round((hc[y, x] - CONTOUR_INDEX_OFFSET) / period) * period + CONTOUR_INDEX_OFFSET
        spr = text_sprite(f'{level:.0f}', font, ink, halo_at(x, y), halo_px)
        angle = np.degrees(np.arctan2(-gx[y, x], -gy[y, x]))  # Turns the text's top uphill...
        if abs(angle) > 90:
            angle -= 180 * np.sign(angle)  # ...unless that leaves it upside down
        paste_centred(canvas, spr.rotate(angle, resample=Image.BICUBIC, expand=True), x, y)
    print(f'height labels: {len(peaks)} spot heights, {len(labels)} contour numbers')
    return canvas.convert('RGB')


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('planet_dir', type=Path)
    ap.add_argument('--out', type=Path, required=True)
    ap.add_argument('--width', type=int, default=4096)
    ap.add_argument('--road-m', type=float, default=130.0, help='road line width in metres (painted roads are about 130 m)')
    ap.add_argument('--heights', action='store_true',
                    help='add spot heights and contour numbers (tried 2026-11-24; off for the in-game map for now)')
    ap.add_argument('--fit', type=Path, help='print a RAMP fitted to this old map instead of rendering')
    args = ap.parse_args()

    W, H = args.width, args.width // 2
    heights, mats = load(args.planet_dir)
    dx, dy, dz = directions(W, H)
    face, u, v = face_and_texcoord(dx, dy, dz)
    del dx, dy, dz
    h = RADIUS * (HILL_MIN + (HILL_MAX - HILL_MIN) * gather_height(face, u, v, heights) / 65536.0)
    if args.fit:
        road = gather_nearest(face, u, v, mats == ROAD_VALUE)
        old = np.asarray(Image.open(args.fit).convert('RGB').resize((W, H), Image.BILINEAR)).astype(np.float32)
        for lo in range(-1500, 1500, 100):
            m = (h >= lo) & (h < lo + 100) & ~road
            if m.sum() > 200:
                print(lo + 50, m.sum(), tuple(int(c) for c in np.median(old[m], axis=0)))
        return
    del heights, face, u, v

    global WRAP_W
    WRAP_W = W
    rgb = ramp_color(h)
    px_m = 2 * np.pi * RADIUS / W
    shade = hillshade(h, px_m)
    rgb *= (0.86 + 0.20 * np.clip(shade, 0, 1))[..., None]
    base = rgb.copy()  # Ground colour without lines, for label halos

    scale = W / 2160.0
    hc = box_blur(h, int(round(CONTOUR_SMOOTH_PX * scale)))
    cover, _ = contour_cover(hc, CONTOUR_M, 0.9 * scale)
    index_cover, _ = contour_cover(hc - CONTOUR_INDEX_OFFSET, CONTOUR_M * CONTOUR_INDEX, 1.5 * scale)
    a = np.maximum(cover * 0.55, index_cover * 0.8)[..., None]
    rgb = rgb * (1 - a) + np.array(CONTOUR_COLOR, np.float32) * a

    # Roads: drawn on a map twice as fine, then averaged down, for smooth edges
    ss = 2
    skel = face_centrelines(mats)
    lines = road_lines(skel, W, H, ss)
    rc = soft_disk(lines, args.road_m / px_m * 0.5 * ss)
    rc = rc.reshape(H, ss, W, ss).mean(axis=(1, 3))[..., None]
    rgb = rgb * (1 - rc) + np.array(ROAD_COLOR, np.float32) * rc

    img = Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8), 'RGB')
    if args.heights:
        near_road = max_filter(rc[..., 0], int(round(LABEL_PX * W / 4096.0))) > 0.05
        img = height_labels(img, base, h, hc, index_cover, near_road, W, H)
    img.save(args.out)
    print(f'wrote {args.out} ({W}x{H}); road centre-line texels {int(skel.sum())}, road width {args.road_m / px_m:.1f} px')


if __name__ == '__main__':
    sys.exit(main())
