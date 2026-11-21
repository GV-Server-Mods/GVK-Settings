"""Add a Kharak R.U. mark to the Resource Units barrel (LithiumBarrel.mwm): an orange triangle (Keen's Atlas2,
a solid orange patch) beside stencil "R.U." and a small ID code (Keen's TextAtlasDark), as decal quads 1.5 mm
off the body's facets. Everything else in the file stays byte for byte; only new vertices and two mesh parts
are appended, and ModelInfo's counts are updated.
    python ru_mark.py <in.mwm> <out.mwm>
The input is the barrel after mwm_retexture.py, without a mark (tools/models/README.md). Env: RU_BANDS
(black, grey or none; GVK uses grey), RU_CODE (the ID code, default KHR-0417).
"""
import math, os, struct, sys
sys.path.insert(0, os.path.dirname(__file__))
import mwmlib, arrays

VAN = os.path.join(mwmlib.MODELS, "Cubes", "large")
R_BODY, OFF = 0.192, 0.0015
CENTER = math.radians(-135)            # faces the icon camera (SE -X, -Z)
CODE = os.environ.get("RU_CODE", "KHR-0417")

# TextAtlas glyph boxes (u0, v0, u1, v1), Blender v (up), se-dev-seut atlas-regions.md; 1024 px, cap 123 px
G = {"R": (0.4668, 0.5283, 0.5439, 0.6484), "U": (0.8984, 0.5283, 0.9746, 0.6504), "K": (0.3857, 0.6943, 0.46, 0.8164),
     "H": (0.0205, 0.6943, 0.0918, 0.8164), "0": (0.3027, 0.0215, 0.3877, 0.1416), "1": (0.041, 0.1963, 0.0752, 0.3164),
     "2": (0.1602, 0.1963, 0.2461, 0.3164), "3": (0.3037, 0.1963, 0.3867, 0.3164), "4": (0.4521, 0.1963, 0.5342, 0.3164),
     "5": (0.5879, 0.1963, 0.6768, 0.3164), "6": (0.7402, 0.1963, 0.8281, 0.3164), "7": (0.8779, 0.1963, 0.9648, 0.3164),
     "8": (0.0156, 0.0215, 0.1016, 0.1416), "9": (0.1572, 0.0215, 0.2422, 0.1416)}
STEM = (0.1700, 0.1816)                # inside letter I's stem: a solid patch for dots and dashes
PAD = 2 / 1024
ORANGE = (0.7340, 0.2569, 0.7480, 0.2709)   # inside a 45 px solid orange square of Atlas2 #58


def material(path, name):
    pre, tags, bl = mwmlib.load(path)
    for raw, mat, idx, tex, tech in mwmlib.parts(bl["MeshParts"]):
        if mat == name:
            return raw[:4], raw[8 + 4 * len(idx):]
    raise SystemExit(f"{name} not in {path}")


def glyph_rects(text, cap, x, base):
    """-> [(s0, y0, s1, y1, u0, vtop, u1, vbot)] in label space (s = arc length to the right, y up), mwm v."""
    k, out, gap = cap / (123 / 1024), [], 0.15 * cap
    for ch in text:
        if ch in ".-":
            w = (STEM[1] - STEM[0]) * k
            if ch == ".":
                y0, y1, v0, v1 = base, base + w, 0.7486, 0.7486 + (STEM[1] - STEM[0])
                out.append((x, y0, x + w, y1, STEM[0], 1 - v1, STEM[1], 1 - v0))
                x += w + gap
            else:                       # a dash: the stem laid on its side (u runs down the dash)
                L = 0.4 * cap; y0 = base + 0.42 * cap
                out.append(("rot", x, y0, x + L, y0 + w, STEM[0], 1 - 0.78, STEM[1], 1 - 0.78 + L / k))
                x += L + gap
            continue
        u0, v0, u1, v1 = G[ch]
        u0, v0, u1, v1 = u0 - PAD, v0 - PAD, u1 + PAD, v1 + PAD
        w, h = (u1 - u0) * k, (v1 - v0) * k
        out.append((x - PAD * k, base - PAD * k, x - PAD * k + w, base - PAD * k + h, u0, 1 - v1, u1, 1 - v0))
        x += w - 2 * PAD * k + gap
    return out, x - gap


def facets(P, idx):
    """Corner angles of the body cylinder at R_BODY (from its vertices)."""
    a = sorted({round(math.degrees(math.atan2(P[i][2], P[i][0])), 2) for i in idx
                if abs(math.hypot(P[i][0], P[i][2]) - R_BODY) < 0.0008 and abs(P[i][1] - 0.105) < 0.002})
    return [math.radians(x) for x in a]


def build(in_path, out_path):
    pre, tags, bl = mwmlib.load(in_path)
    P = arrays.unpack_pos(bl["Vertices"])
    parts = mwmlib.parts(bl["MeshParts"])
    if any(p[1] in ("TextAtlasDark", "Atlas1", "Atlas2") for p in parts):
        sys.exit(in_path + " already has the R.U. mark: start from the unmarked model")
    corners = facets(P, next(p[2] for p in parts if p[1] == "BlackMetal"))
    print("body facets", len(corners))

    def facet_of(th):
        for i in range(len(corners)):
            a, b = corners[i], corners[(i + 1) % len(corners)] + (2 * math.pi if i + 1 == len(corners) else 0)
            t = th if th >= a else th + 2 * math.pi
            if a <= t <= b:
                return a, b
        raise ValueError(th)

    # the side's profile: corner radius per outer ring (rings inside the rim, round the recessed lid, are skipped)
    body = next(p[2] for p in parts if p[1] == "BlackMetal")
    ring = {}
    for i in set(body):
        y = round(P[i][1], 4)
        if abs(y) <= 0.249:
            ring.setdefault(y, []).append(math.hypot(P[i][0], P[i][2]))
    prof = sorted((y, max(rs)) for y, rs in ring.items() if max(rs) > 0.19)

    def profile(y):
        """-> (corner radius, dr/dy) at height y."""
        for (y0, r0), (y1, r1) in zip(prof, prof[1:]):
            if y0 <= y <= y1:
                k = (r1 - r0) / (y1 - y0)
                return r0 + k * (y - y0), k
        raise ValueError(y)

    def to3d(s, y, a, b):
        th = CENTER - s / R_BODY
        mid, half = (a + b) / 2, (b - a) / 2
        rc, k = profile(y)
        r = rc * math.cos(half) / math.cos(th - mid)
        c, sn = math.cos(th), math.sin(th)
        L = math.sqrt(1 + k * k)
        n = (c / L, -k / L, sn / L)                     # the body's own smooth normal there (cone-aware)
        down = (-k * c / L, -1 / L, -k * sn / L)        # down the side
        return (r * c + OFF * n[0], y + OFF * n[1], r * sn + OFF * n[2]), n, down

    verts, tris = [], {"TextAtlasDark": [], "Atlas2": [], "Atlas1": []}

    def add_poly(mat, poly, uvf, rot=False):
        """poly: convex [(s, y)] in label space; split at facet edges into flat pieces."""
        ths = [CENTER - s / R_BODY for s, _ in poly]
        lo, hi = min(ths), max(ths)
        cuts = sorted({lo, hi} | {c for c in corners if lo < c < hi} | {c - 2 * math.pi for c in corners if lo < c - 2 * math.pi < hi})
        for t0, t1 in zip(cuts, cuts[1:]):
            s_hi, s_lo = (CENTER - t0) * R_BODY, (CENTER - t1) * R_BODY
            piece = clip(clip(poly, 0, s_lo, 1), 0, s_hi, -1)
            if len(piece) < 3:
                continue
            a, b = facet_of((t0 + t1) / 2)
            base = len(verts)
            for s, y in piece:
                p, n, down = to3d(s, y, a, b)
                u, v = uvf(s, y)
                right = (math.sin(CENTER - s / R_BODY), 0.0, -math.cos(CENTER - s / R_BODY))
                verts.append((p, n, (u, v), down if rot else right, right if rot else down))
            for i in range(1, len(piece) - 1):          # fan; winding clockwise about the outward normal
                tris[mat] += [base, base + i + 1, base + i]

    def rect_uv(s0, y0, s1, y1, u0, vt, u1, vb, rot=False):
        if not rot:
            return lambda s, y: (u0 + (s - s0) / (s1 - s0) * (u1 - u0), vt + (y1 - y) / (y1 - y0) * (vb - vt))
        return lambda s, y: (u0 + (y1 - y) / (y1 - y0) * (u1 - u0), vt + (s - s0) / (s1 - s0) * (vb - vt))

    def add_text(text, cap, x, base):
        rects, end = glyph_rects(text, cap, x, base)
        for r in rects:
            rot = r[0] == "rot"
            s0, y0, s1, y1, u0, vt, u1, vb = r[1:] if rot else r
            add_poly("TextAtlasDark", [(s0, y0), (s1, y0), (s1, y1), (s0, y1)], rect_uv(s0, y0, s1, y1, u0, vt, u1, vb, rot), rot)
        return end

    # layout, centred on the middle band (y -0.073 to 0.105); s = 0 is the label's centre line
    code_cap, ru_cap, tri_h = 0.018, 0.050, 0.078
    yb = 0.016 - tri_h / 2
    tri_w = tri_h * 2 / math.sqrt(3)
    w_ru = glyph_rects("R.U.", ru_cap, 0, 0)[1]; w_code = glyph_rects(CODE, code_cap, 0, 0)[1]
    total = tri_w + 0.012 + max(w_ru, w_code)
    x0 = -total / 2
    tri = [(x0, yb), (x0 + tri_w, yb), (x0 + tri_w / 2, yb + tri_h)]
    ou0, ov0, ou1, ov1 = ORANGE
    add_poly("Atlas2", tri, rect_uv(x0, yb, x0 + tri_w, yb + tri_h, ou0, 1 - ov1, ou1, 1 - ov0))
    xt = x0 + tri_w + 0.012
    add_text("R.U.", ru_cap, xt, yb + code_cap + 0.010)
    add_text(CODE, code_cap, xt, yb)

    # warning slashes round the top and bottom sections, a whole number of tiles round the body
    bands = os.environ.get("RU_BANDS", "black")
    if bands != "none":
        circ, ins = 2 * math.pi * R_BODY, 2 / 2048
        if bands == "black":           # Atlas2 #69: black slashes, 512 x 102 px, tileable
            mat, n, u0, u1, bv0, bv1, aspect = "Atlas2", 7, 0.75, 1.0, ins, 0.0498 - ins, 98 / 512
        else:                          # Atlas1 #10: grey slashes every 181 px from x = 14, 4 per tile; rows 832-971
            mat, n, u0, u1, bv0, bv1, aspect = "Atlas1", 24, 14 / 2048, 738 / 2048, 1 - 972 / 2048, 1 - 832 / 2048, 140 / 724
        L = circ / n; h = L * aspect   # (owner, 2026-11-21: grey, no border lines, near the rims, 1/3 size)
        for yc in ((0.240 - h / 2, -0.240 + h / 2) if bands == "grey" else (0.188, -0.173)):
            y0, y1 = yc - h / 2, yc + h / 2
            for k in range(n):
                s0 = -circ / 2 + k * L
                add_poly(mat, [(s0, y0), (s0 + L, y0), (s0 + L, y1), (s0, y1)], rect_uv(s0, y0, s0 + L, y1, u0, 1 - bv1, u1, 1 - bv0))
        print(f"bands {bands}: {n} tiles, {h * 1000:.1f} mm tall")
    print(f"label {total * 1000:.0f} mm wide ({math.degrees(total / R_BODY):.0f} deg), {len(verts)} vertices")

    def nrm(v):
        l = math.sqrt(sum(c * c for c in v)); return tuple(c / l for c in v)
    bl["Vertices"] = arrays.append(bl["Vertices"], [arrays.pack_pos(v[0]) for v in verts])
    bl["Normals"] = arrays.append(bl["Normals"], [arrays.pack_nrm(nrm(v[1])) for v in verts])
    bl["TexCoords0"] = arrays.append(bl["TexCoords0"], [struct.pack("<2e", *v[2]) for v in verts])
    bl["Tangents"] = arrays.append(bl["Tangents"], [arrays.pack_nrm(nrm(v[3])) for v in verts])
    bl["Binormals"] = arrays.append(bl["Binormals"], [arrays.pack_nrm(nrm(v[4])) for v in verts])
    first = len(P)
    mats = {"TextAtlasDark": material(os.path.join(VAN, "BridgeButtonPanel_LOD0.mwm"), "TextAtlasDark"),
            "Atlas2": material(os.path.join(VAN, "Barrel01ExplosiveLarge_LOD0.mwm"), "Atlas2"),
            "Atlas1": material(os.path.join(VAN, "Barrel01ExplosiveLarge_LOD0.mwm"), "Atlas1")}
    tris = {k: v for k, v in tris.items() if v}
    mp = bl["MeshParts"]; p = arrays.body(mp); n = struct.unpack_from("<i", mp, p)[0]
    extra = b""
    for name, ix in tris.items():
        h, desc = mats[name]
        extra += h + struct.pack("<i", len(ix)) + struct.pack("<%di" % len(ix), *[first + i for i in ix]) + desc
    bl["MeshParts"] = mp[:p] + struct.pack("<i", n + len(tris)) + mp[p + 4:] + extra
    mi = bl["ModelInfo"]; p = arrays.body(mi); t, v = struct.unpack_from("<ii", mi, p)
    bl["ModelInfo"] = mi[:p] + struct.pack("<ii", t + sum(len(x) for x in tris.values()) // 3, v + len(verts)) + mi[p + 8:]
    mwmlib.save(out_path, pre, tags, bl)
    print("wrote", out_path)


def clip(poly, axis, value, sign):
    """Keep the part of a polygon where sign * (coord - value) >= 0 (Sutherland-Hodgman, one plane)."""
    out = []
    for i, a in enumerate(poly):
        b = poly[(i + 1) % len(poly)]
        da, db = sign * (a[axis] - value), sign * (b[axis] - value)
        if da >= 0:
            out.append(a)
        if (da >= 0) != (db >= 0):
            t = da / (da - db)
            out.append(tuple(a[k] + t * (b[k] - a[k]) for k in range(2)))
    dedup = []
    for q in out:
        if not dedup or abs(q[0] - dedup[-1][0]) > 1e-9 or abs(q[1] - dedup[-1][1]) > 1e-9:
            dedup.append(q)
    while len(dedup) > 1 and abs(dedup[0][0] - dedup[-1][0]) < 1e-9 and abs(dedup[0][1] - dedup[-1][1]) < 1e-9:
        dedup.pop()
    return dedup


if __name__ == "__main__":
    build(sys.argv[1], sys.argv[2])
