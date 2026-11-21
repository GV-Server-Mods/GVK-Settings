"""Kharak C.U. labels on the Construction Units pack (CopperIngot.mwm), in Keen's ingot style: the copper bars move
to Keen's ingot sheet (its dark red-brown tile), and full-size paper shipping labels with a barcode (Keen's ingot
sheet) go on the top bar and, centered, on both sides (-X and +X), folded over the groove between two bars the way
Keen folds his over a step. Each label has "C.U." in Keen's element-symbol font and a blue triangle (BlueFrostedMetal) beside
the barcode. Only new vertices and two mesh parts are appended; the copper part's two texture paths and UVs change.
    python cu_mark.py <in.mwm> <out.mwm>
The input must be the unmarked model (tools/models/README.md says where to get it).
"""
import math, os, struct, sys
sys.path.insert(0, os.path.dirname(__file__))
import mwmlib, arrays
from ru_mark import clip

CONTENT = mwmlib.MODELS
SHEET = (r"Textures\Models\Physical_item\ingots_cm.dds", r"Textures\Models\Physical_item\ingots_ng.dds")
TILE_SHIFT = -0.75                      # copper UVs: bottom-left tile -> top-left (dark red-brown)

# label design in metres; u, v on Keen's ingot sheet, mwm v (down)
K = 0.6135                              # metres per UV unit of the paper
V0, V1 = 0.751, 0.997                   # paper rows (Keen's crop)
BAR = (0.5632, 0.6729, 0.9832)          # barcode left, right, bottom
LINE, BELOW = 0.91431, 0.9840           # top of the printed line; just below the barcode
SEAM, UE = 0.6895, 0.740                # right of the printed line the paper is blank: stretch it to the edge
TRI, GAP = 0.040, 0.006                 # triangle width, gap between barcode and triangle
GLYPH = {"C": (0.8735, 0.7295, 0.9199, 0.79), "U": (0.8735, 0.9341, 0.9189, 0.9951)}   # from the Co and U cells
DOT = (0.8750, 0.950, 0.8808, 0.956)    # inside U's left stroke: solid ink for the periods
STROKE = 0.0086                         # the font's stroke width (UV)
PAD = 0.002
BLUE = (0.40, 0.40, 0.43, 0.43)         # a small patch of BlueFrostedMetal
H_PAPER, H_INK = 0.0010, 0.0020         # height above the surface: paper, then letters and triangle
H = (V1 - V0) * K                       # label height, 151 mm


def norm(v):
    l = math.sqrt(sum(c * c for c in v)); return tuple(c / l for c in v)


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def add(a, b, k=1.0):
    return tuple(a[i] + k * b[i] for i in range(3))


def material(path, name, tech=None):
    for raw, mat, idx, tex, t in mwmlib.parts(mwmlib.load(path)[2]["MeshParts"]):
        if mat == name and (tech is None or t == tech):
            return raw[:4], raw[8 + 4 * len(idx):]
    raise SystemExit(f"{name} not in {path}")


def ray(tris, o, d):
    """Distance along d to the first triangle (Moller-Trumbore), or None."""
    best = None
    for a, b, c in tris:
        e1 = tuple(b[i] - a[i] for i in range(3)); e2 = tuple(c[i] - a[i] for i in range(3))
        p = cross(d, e2); det = sum(e1[i] * p[i] for i in range(3))
        if abs(det) < 1e-12:
            continue
        t0 = tuple(o[i] - a[i] for i in range(3)); u = sum(t0[i] * p[i] for i in range(3)) / det
        if u < 0 or u > 1:
            continue
        q = cross(t0, e1); v = sum(d[i] * q[i] for i in range(3)) / det
        if v < 0 or u + v > 1:
            continue
        t = sum(e2[i] * q[i] for i in range(3)) / det
        if t > -0.01 and (best is None or t < best):
            best = t
    return best


def layout(W):
    """-> [(mat, h, [(x, y)], uv function)], x right and y down from the label's top-left corner."""
    cap = 0.348 * W
    m = (W - (BAR[1] - BAR[0]) * K - GAP - TRI) / 2          # barcode and triangle centred together
    u0 = BAR[0] - m / K
    xs = (SEAM - u0) * K
    out = []

    def rect(mat, h, x0, y0, x1, y1, uv):
        f = lambda x, y: (uv[0] + (x - x0) / (x1 - x0) * (uv[2] - uv[0]), uv[1] + (y - y0) / (y1 - y0) * (uv[3] - uv[1]))
        out.append((mat, h, [(x0, y1), (x1, y1), (x1, y0), (x0, y0)], f))   # CCW seen from outside (right, up)

    # vertically: equal space above the letters, between them and the printed block, and below the barcode
    sp = (H - cap - (BAR[2] - LINE) * K) / 3
    BASE = sp + cap; yl = BASE + sp                          # baseline; the printed line
    va, yb1 = LINE - yl / K, yl + (BELOW - LINE) * K         # paper row at the top edge; label row below the barcode
    assert va >= V0
    for x0, x1, ua, ub in ((0, xs, u0, SEAM), (xs, W, SEAM, UE)):
        rect("paper", H_PAPER, x0, 0, x1, yb1, (ua, va, ub, BELOW))
        rect("paper", H_PAPER, x0, yb1, x1, H, (ua, BELOW, ub, V1))   # blank strip under the barcode, stretched
    kk = cap / (GLYPH["C"][3] - GLYPH["C"][1])
    gap, s = 0.114 * cap, STROKE * kk
    seq = ["C", ".", "U", "."]
    widths = [s if c == "." else (GLYPH[c][2] - GLYPH[c][0]) * kk for c in seq]
    x = (W - sum(widths) - gap * (len(seq) - 1)) / 2
    for c, w in zip(seq, widths):
        if c == ".":
            rect("paper", H_INK, x, BASE - s, x + s, BASE, DOT)
        else:
            u0g, v0g, u1g, v1g = GLYPH[c]; p = PAD * kk
            rect("paper", H_INK, x - p, BASE - cap - p, x + w + p, BASE + p, (u0g - PAD, v0g - PAD, u1g + PAD, v1g + PAD))
        x += w + gap
    tx = m + (BAR[1] - BAR[0]) * K + GAP; yb = yl + (BAR[2] - LINE) * K; th = TRI * math.sqrt(3) / 2
    f = lambda x, y: (BLUE[0] + (x - tx) / TRI * (BLUE[2] - BLUE[0]), BLUE[1] + (y - yb + th) / th * (BLUE[3] - BLUE[1]))
    out.append(("blue", H_INK, [(tx, yb), (tx + TRI, yb), (tx + TRI / 2, yb - th)], f))
    print(f"label {W * 1000:.0f} x {H * 1000:.0f} mm: letters {cap * 1000:.0f} mm, spacing {sp * 1000:.1f} mm, "
          f"barcode margin {m * 1000:.1f} mm; blank paper stretched {(W - xs) / ((UE - SEAM) * K):.2f}x at the right, "
          f"{(H - yb1) / ((V1 - BELOW) * K):.2f}x at the bottom")
    return out


def side_section(body, sgn):
    """A side label's centre line on the -X (sgn -1) or +X (sgn 1) face, top to bottom: down the long bar's face
    (below its grip notch), straight across the groove, then down the middle bar's end. Faces from ray hits at z = 0."""
    hit = lambda y: add((sgn * 0.4, y, 0.0), (-sgn, 0, 0), ray(body, (sgn * 0.4, y, 0.0), (-sgn, 0, 0)))
    b1, b2, a1, a2 = hit(0.07), hit(0.11), hit(-0.02), hit(0.02)
    on = lambda p, q, y: add(p, tuple(q[i] - p[i] for i in range(3)), (y - p[1]) / (q[1] - p[1]))
    top, b_edge, a_edge = on(b1, b2, 0.118), on(b1, b2, 0.0525), on(a1, a2, 0.038)
    used = sum(math.dist(p, q) for p, q in ((top, b_edge), (b_edge, a_edge)))
    d = norm(tuple(a1[i] - a2[i] for i in range(3)))
    bottom = add(a_edge, d, H - used)
    assert bottom[1] > -0.040, bottom                        # stays on the bar end (it ends at y -0.041)
    return [top, b_edge, a_edge, bottom]


def build(src, dst):
    pre, tags, bl = mwmlib.load(src)
    P = arrays.unpack_pos(bl["Vertices"])
    parts = mwmlib.parts(bl["MeshParts"])
    body = [tuple(P[i] for i in p[2][j:j + 3]) for p in parts for j in range(0, len(p[2]), 3)]

    if any(p[4] == "ALPHA_MASKED" for p in parts) or parts[0][3]["ColorMetalTexture"] == SHEET[0]:
        sys.exit(src + " already has the C.U. labels: start from the unmarked model")

    # 1. copper part: Keen's ingot sheet, dark red-brown tile
    assert parts[0][1] == "Copper"
    mp = bl["MeshParts"]
    for old, new in zip((parts[0][3]["ColorMetalTexture"], parts[0][3]["NormalGlossTexture"]), SHEET):
        a, b = mwmlib.wstr(old), mwmlib.wstr(new)
        assert mp.count(a) == 1, old
        mp = mp.replace(a, b)
    bl["MeshParts"] = mp
    uvb = bytearray(bl["TexCoords0"]); p0 = arrays.body(uvb) + 4
    for i in set(parts[0][2]):
        u, v = struct.unpack_from("<2e", uvb, p0 + 4 * i)
        struct.pack_into("<2e", uvb, p0 + 4 * i, u, v + TILE_SHIFT)
    bl["TexCoords0"] = bytes(uvb)

    # 2. labels: (width, centre line on the surface top to bottom, right); the label folds where the line bends
    labels = [(0.130, [(0.005 + H / 2, 0.2472, 0.0), (0.005 - H / 2, 0.2472, 0.0)], (0, 0, 1)),   # top bar
              (0.130, side_section(body, -1), (0, 0, 1)),                                       # -X side, centred
              (0.130, side_section(body, 1), (0, 0, -1))]                                       # +X side, centred
    verts, tris = [], {"paper": [], "blue": []}
    for W, sec, R in labels:
        segs, s0 = [], 0.0                                   # (s start, s end, start point, down, normal)
        for p, q in zip(sec, sec[1:]):
            L = math.dist(p, q); d = norm(tuple(q[i] - p[i] for i in range(3)))
            segs.append((s0, s0 + L, p, d, cross(d, R))); s0 += L
        assert abs(s0 - H) < 1e-6, s0
        creases = [g[1] for g in segs[:-1]]

        def place(x, s, h, k):
            g = segs[k]; c = add(g[2], g[3], s - g[0])
            n = g[4]
            for j in (k - 1, k + 1):                         # on a crease: offset along the mitre of both faces
                if 0 <= j < len(segs) and abs(s - (segs[j][0] if j > k else segs[j][1])) < 1e-9:
                    m = norm(add(g[4], segs[j][4])); n = tuple(c_ / sum(m[i] * g[4][i] for i in range(3)) for c_ in m)
            return add(add(c, R, x - W / 2), n, h)

        gaps = []
        for mat, h, pts, uvf in layout(W):
            for k, g in enumerate(segs):
                piece = clip(clip(pts, 1, g[0], 1), 1, g[1], -1)
                if len(piece) < 3:
                    continue
                base = len(verts)
                for x, s in piece:
                    p = place(x, s, h, k)
                    gaps.append((ray(body, p, tuple(-c for c in g[4])), h, k))
                    verts.append((p, uvf(x, s), g[4], R, g[3]))
                for i in range(1, len(piece) - 1):
                    tris[mat] += [base, base + i + 1, base + i]   # clockwise about the outward normal
        flat = [gp - h for gp, h, k in gaps if gp is not None]
        assert len(flat) == len(gaps) and min(flat) > -0.0004, min(flat)   # the side faces twist about 1 mm
        print(f"  {len(segs)} faces, folds at {[round(c * 1000) for c in creases]} mm; "
              f"height over the surface minus target {min(flat) * 1000:.2f} to {max(flat) * 1000:.2f} mm")
    for j in range(0, len(tris["paper"]), 3):
        a, b, c = (verts[i][0] for i in tris["paper"][j:j + 3])
        n = cross(tuple(b[i] - a[i] for i in range(3)), tuple(c[i] - a[i] for i in range(3)))
        assert sum(n[i] * verts[tris["paper"][j]][2][i] for i in range(3)) < 0, "winding"

    first = len(P)
    bl["Vertices"] = arrays.append(bl["Vertices"], [arrays.pack_pos(v[0]) for v in verts])
    bl["Normals"] = arrays.append(bl["Normals"], [arrays.pack_nrm(v[2]) for v in verts])
    bl["TexCoords0"] = arrays.append(bl["TexCoords0"], [struct.pack("<2e", *v[1]) for v in verts])
    bl["Tangents"] = arrays.append(bl["Tangents"], [arrays.pack_nrm(v[3]) for v in verts])     # dP/du
    bl["Binormals"] = arrays.append(bl["Binormals"], [arrays.pack_nrm(v[4]) for v in verts])   # dP/dv
    mats = {"paper": material(os.path.join(CONTENT, r"Ingots\iron_ingot_LOD0.mwm"), "IngotsAtlas", "ALPHA_MASKED"),
            "blue": material(os.path.join(CONTENT, r"Cubes\large\Bathroom_LOD0.mwm"), "BlueFrostedMetal")}
    mp = bl["MeshParts"]; p = arrays.body(mp); n = struct.unpack_from("<i", mp, p)[0]
    extra = b""
    for name, ix in tris.items():
        h, desc = mats[name]
        extra += h + struct.pack("<i", len(ix)) + struct.pack("<%di" % len(ix), *[first + i for i in ix]) + desc
    bl["MeshParts"] = mp[:p] + struct.pack("<i", n + len(tris)) + mp[p + 4:] + extra
    mi = bl["ModelInfo"]; p = arrays.body(mi); t, v = struct.unpack_from("<ii", mi, p)
    bl["ModelInfo"] = mi[:p] + struct.pack("<ii", t + sum(len(x) for x in tris.values()) // 3, v + len(verts)) + mi[p + 8:]
    mwmlib.save(dst, pre, tags, bl)
    print(len(verts), "vertices; wrote", dst)


if __name__ == "__main__":
    build(sys.argv[1], sys.argv[2])
