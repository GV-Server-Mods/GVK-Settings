"""Give flat blocks and items a steeper camera in Keen's Icon Studio, like Keen's own icons.

Icon Studio looks at every icon from 45 degrees around and 35 up, unless its override file
(default_icon_render_overrides.json, next to SEIconStudio.exe) holds a camera for that subtype. Keen set
those by hand; flat things look down further (floor slab 45, steel plate 55). This script measures each
model of a mod (height against its mean width) and writes a camera for the flat ones: 35 for anything at
least half as tall as wide, up to 60 for a thin plate, in 5 degree steps. Keen's own entries, read from the
copy built into SEIconStudio.pck, are never changed. Run it again after a Steam update resets the file.

    python tools/icon_tilt.py <mod folder> [<mod folder> ...] [--dry-run]

--frame instead gives every block and ammo magazine its own camera, kept within Keen's own weapon angles
(owner, 2026-11-23: 60 up was "way too steep"). Blocks stay at Keen's 155 degrees around and rise from 20 up
to at most 35; ammo magazines try 105 to 165 around and 25 to 45 up. A camera replaces the default only if
its outline covers at least 10% more of a square tile. --long names long guns, which keep Keen's small
autocannon camera (160 around, 20 up); --model-rot turns a model (Griffin missile, so it points down to the
right). Every block gets an entry, even at the default: a subtype without one falls back to Keen's entry
for its TypeId (`LargeMissileTurret#EMPTY_SUBTYPE`, 20 up), not to the 35 up default. _NPC copies follow
their player block. --sheet draws each outline at the old and new camera; --only limits the run.

    python tools/icon_tilt.py <mod folder> --frame [--long SUBTYPE ...] [--model-rot SUBTYPE=X,Y,Z ...]

A mod folder is a deployed mod (%APPDATA%\\SpaceEngineers\\Mods\\<mod>) or a Workshop download
(steamapps\\workshop\\content\\244850\\<id>). Then run Icon Studio on it as usual.
"""
import argparse, glob, json, math, os, re, struct, xml.etree.ElementTree as ET

STEAM = r"C:\Program Files (x86)\Steam\steamapps\common"
STUDIO = os.path.join(STEAM, r"SpaceEngineersModSDK\Tools\SEIconStudio")
YAW = -2.35619449615479            # Icon Studio's default camera yaw (-135 degrees)
BASE, MAXP, FLAT = 35, 60, 0.5     # default tilt, tilt of a thin plate, height/width that keeps the default


# --- .mwm reading (VRageRender.Import.MyModelImporter) ---
def read_7bit_int(f):
    result = shift = 0
    while True:
        byte = f.read(1)[0]
        result |= (byte & 0x7F) << shift
        if byte < 0x80:
            return result
        shift += 7


def read_string(f):
    return f.read(read_7bit_int(f)).decode("utf-8")


def read_int(f):
    return struct.unpack("<i", f.read(4))[0]


def read_floats(f, n):
    return struct.unpack("<%df" % n, f.read(4 * n))


def load(path):
    """Vertex positions (the box corners for Keen's newer models, which keep the mesh elsewhere) and dummies."""
    with open(path, "rb") as f:
        read_string(f)
        for _ in range(read_int(f)):
            read_string(f)
        index = {}
        for _ in range(read_int(f)):
            tag = read_string(f)
            index[tag] = read_int(f)
        if "Vertices" in index:                  # HalfVector4, position = xyz * w (PositionPacker)
            f.seek(index["Vertices"]); read_string(f); n = read_int(f)
            raw = struct.unpack("<%de" % (4 * n), f.read(8 * n))
            verts = [(raw[i] * raw[i + 3], raw[i + 1] * raw[i + 3], raw[i + 2] * raw[i + 3]) for i in range(0, 4 * n, 4)]
        else:
            f.seek(index["BoundingBox"]); read_string(f); b = read_floats(f, 6)
            verts = [(b[i * 3], b[j * 3 + 1], b[k * 3 + 2]) for i in (0, 1) for j in (0, 1) for k in (0, 1)]
        dummies = []
        if "Dummies" in index:
            f.seek(index["Dummies"]); read_string(f)
            for _ in range(read_int(f)):
                name, m = read_string(f), read_floats(f, 16)
                data = {read_string(f): read_string(f) for _ in range(read_int(f))}
                dummies.append((name, m, data))
    return verts, dummies


def mul(child, parent):            # XNA row-vector matrices: point * child * parent
    if parent is None:
        return child
    return [sum(child[r * 4 + k] * parent[k * 4 + c] for k in range(4)) for r in range(4) for c in range(4)]


def points(path, m=None, depth=0):
    """Model vertices with its subparts placed at their dummies (the subpart file is relative to the model)."""
    verts, dummies = load(path)
    step = max(1, len(verts) // 6000)
    out = [v if m is None else (v[0] * m[0] + v[1] * m[4] + v[2] * m[8] + m[12],
                                v[0] * m[1] + v[1] * m[5] + v[2] * m[9] + m[13],
                                v[0] * m[2] + v[1] * m[6] + v[2] * m[10] + m[14]) for v in verts[::step]]
    for name, dm, data in dummies:
        sub = data.get("file")
        sp = sub and os.path.join(os.path.dirname(path), sub) + ".mwm"
        if name.startswith("subpart_") and sp and depth < 5 and os.path.exists(sp):
            out += points(sp, mul(list(dm), m), depth + 1)
    return out


def tilt(pts):
    ext = [max(p[i] for p in pts) - min(p[i] for p in pts) for i in range(3)]
    ratio = ext[1] / max(1e-6, (ext[0] + ext[2]) / 2)
    raw = BASE + (MAXP - BASE) * min(1.0, max(0.0, (FLAT - ratio) / (FLAT - 0.05)))
    return int(5 * round(raw / 5)), ratio


# --- --frame: a camera per block or magazine that fills the tile ---
# Keen's weapon cameras (default_icon_render_overrides.json): turrets 20 up and 155 around, fixed guns 20 up
# and 160 around, ammo 35-40 up and 145 around, never above 40. Owner, 2026-11-23: weapon blocks start from Keen's
# turret camera and look down at most to 35; long guns (--long) take the small autocannon's camera; magazines may
# turn so a long round tips toward the camera, at most 45 up. 60 up looked "way too steep".
# kind: (default yaw, default pitch, yaws, pitches)
CAMERAS = {"block": (-155, 20, [-155], range(20, 36, 5)),
           "long": (-160, 20, [-160], [20]),
           "magazine": (-135, 35, range(-165, -104, 5), range(25, 46, 5))}
GAIN = 1.10                                               # a new camera must cover 10% more of the tile


def project(pts, yaw, pitch):
    """Image x, y of each point for a Godot camera at cam_rot (-pitch, yaw, 0): basis Ry(yaw) * Rx(-pitch)."""
    y_, p_ = math.radians(yaw), -math.radians(pitch)
    cy, sy, cp, sp = math.cos(y_), math.sin(y_), math.cos(p_), math.sin(p_)
    return [(x * cy - z * sy, x * sy * sp + y * cp + z * cy * sp) for x, y, z in pts]


def hull(pts2):
    pts2 = sorted(set(pts2))
    if len(pts2) < 3:
        return pts2
    def half(seq):
        h = []
        for p in seq:
            while len(h) >= 2 and (h[-1][0] - h[-2][0]) * (p[1] - h[-2][1]) - (h[-1][1] - h[-2][1]) * (p[0] - h[-2][0]) <= 0:
                h.pop()
            h.append(p)
        return h[:-1]
    return half(pts2) + half(pts2[::-1])


def cover(pts2):
    """Share of the square tile the outline covers, with the longest side scaled to the tile (Icon Studio's fit)."""
    h = hull(pts2)
    xs, ys = [p[0] for p in pts2], [p[1] for p in pts2]
    side = max(max(xs) - min(xs), max(ys) - min(ys), 1e-6)
    area = abs(sum(h[i][0] * h[i - 1][1] - h[i - 1][0] * h[i][1] for i in range(len(h)))) / 2
    return area / side ** 2


def frame(pts, kind):
    """(yaw, pitch, cover) of the best camera, and the default camera's cover."""
    y0, p0, yaws, pitches = CAMERAS[kind]
    pts = [(round(x, 2), round(y, 2), round(z, 2)) for x, y, z in pts]
    base = cover(project(pts, y0, p0))
    best = max(((cover(project(pts, y, p)), -abs(y - y0) - abs(p - p0), y, p) for y in yaws for p in pitches))
    if best[0] < base * GAIN:
        return y0, p0, base, base
    return best[2], best[3], best[0], base


def rotate(pts, deg):
    """Points turned by Icon Studio's model_rot (Godot Euler, basis Ry * Rx * Rz), given in degrees."""
    ax, ay, az = (math.radians(v) for v in deg)
    out = []
    for x, y, z in pts:
        x, y = x * math.cos(az) - y * math.sin(az), x * math.sin(az) + y * math.cos(az)
        y, z = y * math.cos(ax) - z * math.sin(ax), y * math.sin(ax) + z * math.cos(ax)
        x, z = x * math.cos(ay) + z * math.sin(ay), -x * math.sin(ay) + z * math.cos(ay)
        out.append((x, y, z))
    return out


def outline_cell(pts, yaw, pitch, size=128, fill=0.75):
    from PIL import Image, ImageDraw
    p2 = project(pts, yaw, pitch)
    xs, ys = [p[0] for p in p2], [p[1] for p in p2]
    s = fill * size / max(max(xs) - min(xs), max(ys) - min(ys), 1e-6)
    cx, cy = (max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2
    img = Image.new("RGB", (size, size), (40, 44, 52))
    ImageDraw.Draw(img).polygon([(size / 2 + (x - cx) * s, size / 2 - (y - cy) * s) for x, y in hull(p2)], fill=(200, 200, 200))
    return img


def sheet(rows, out):
    from PIL import Image, ImageDraw
    cols, cw, ch = 4, 270, 150
    img = Image.new("RGB", (cols * cw, ((len(rows) + cols - 1) // cols) * ch + 4), (24, 26, 30))
    d = ImageDraw.Draw(img)
    for i, (key, pts, y, p, new, old) in enumerate(rows):
        x0, y0 = (i % cols) * cw + 4, (i // cols) * ch + 4
        img.paste(outline_cell(pts, -135, BASE), (x0, y0))
        img.paste(outline_cell(pts, y, p), (x0 + 132, y0))
        d.text((x0, y0 + 130), f"{key[:26]}  {old:.2f}>{new:.2f}  {-y} {p}", fill=(230, 230, 230))
    img.save(out)


# --- definitions and Icon Studio's override file ---
def definitions(mod):
    for path in glob.glob(os.path.join(mod, "Data", "**", "*.sbc"), recursive=True):
        try:
            root = ET.parse(path).getroot()
        except ET.ParseError:
            continue                              # prefabs and other files that are not definitions
        for d in root.iter():
            idn, model, icon = d.find("Id"), (d.findtext("Model") or "").strip(), (d.findtext("Icon") or "").strip()
            if idn is None or not model or not icon:
                continue
            t = idn.findtext("TypeId") or idn.get("Type") or ""
            s = idn.findtext("SubtypeId") or idn.get("Subtype") or ""
            kind = "block" if d.find("CubeSize") is not None else (
                "magazine" if re.sub("^MyObjectBuilder_", "", t) == "AmmoMagazine" else "")
            yield (s or re.sub("^MyObjectBuilder_", "", t) + "#EMPTY_SUBTYPE"), model, icon, kind


def keen_keys():
    """Subtypes in Keen's own override file: the copy built into SEIconStudio.pck."""
    data = open(os.path.join(STUDIO, "SEIconStudio.pck"), "rb").read()
    at = data.find(b'"AdvancedHandHeldLauncherItem"')
    start = data.rfind(b"{", 0, at)
    keen, _ = json.JSONDecoder().raw_decode(data[start:start + 2_000_000].decode("utf-8", "replace"))
    return set(keen)


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("mods", nargs="+")
    ap.add_argument("--content", default=os.path.join(STEAM, r"SpaceEngineers\Content"))
    ap.add_argument("--overrides", default=os.path.join(STUDIO, "default_icon_render_overrides.json"))
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--frame", action="store_true", help="blocks and ammo magazines: pick each one's camera to fill the tile")
    ap.add_argument("--only", nargs="+", help="with --frame: only these subtypes")
    ap.add_argument("--long", nargs="+", default=[], help="with --frame: long guns (their _NPC copies too)")
    ap.add_argument("--model-rot", nargs="+", default=[], metavar="SUBTYPE=X,Y,Z",
                    help="with --frame: turn a model in degrees first, e.g. lay a standing missile down")
    ap.add_argument("--sheet", help="with --frame: write a PNG of each block's outline at the old and new camera")
    a = ap.parse_args()
    keen = keen_keys()
    cur = json.load(open(a.overrides, encoding="utf-8"))
    changed, rows = 0, []
    turns = {k: [float(v) for v in r.split(",")] for k, r in (m.split("=", 1) for m in a.model_rot)}
    for mod in a.mods:
        seen, own = set(), 0
        for key, model, icon, kind in definitions(mod):
            if key in seen or key in keen:
                continue
            seen.add(key)
            if not os.path.exists(os.path.join(mod, icon)):   # a tweaked vanilla definition keeps Keen's icon
                if key in cur:
                    print(f"  {key}: uses a vanilla icon, entry removed")
                    del cur[key]; changed += 1
                continue
            own += 1
            path = next((p for p in (os.path.join(mod, model), os.path.join(a.content, model)) if os.path.exists(p)), None)
            if not path:
                print(f"  {key}: model not found ({model})"); continue
            if a.frame and (not kind or (a.only and key not in a.only)):
                continue
            try:
                pts = points(path)
                pitch, ratio = tilt(pts)
                yaw = -135
                base_key = re.sub("_NPC$", "", key)
                turn = turns.get(key) or turns.get(base_key)
                if a.frame:
                    if kind == "block" and base_key in a.long:
                        kind = "long"
                    if turn:
                        pts = rotate(pts, turn)
                    yaw, pitch, new, old = frame(pts, kind)
                    rows.append((key, pts, yaw, pitch, new, old))
                    ratio = new / old
            except (KeyError, IndexError, struct.error, ValueError) as e:
                print(f"  {key}: cannot read {model} ({e!r})"); continue
            # Always written: without its own entry, Icon Studio falls back to Keen's entry for the vanilla block
            # of the same type (LargeMissileTurret#EMPTY_SUBTYPE is 20 up, 155 around), not 45/35 (owner, 2026-11-23).
            if a.frame:
                want = {"cam_rot": [-math.radians(pitch), math.radians(yaw), 0.0]}
                if turn:
                    want["model_rot"] = [math.radians(v) for v in turn]
                if cur.get(key) != want:
                    print(f"  {key}: around {-yaw}, up {pitch} (covers {ratio:.2f}x the tile)")
                    cur[key] = want; changed += 1
                continue
            want = {"cam_rot": [-math.radians(pitch), YAW, 0.0]} if pitch != BASE else None
            if want and cur.get(key) != want:
                print(f"  {key}: tilt {pitch} (height/width {ratio:.2f})")
                cur[key] = want; changed += 1
            elif not want and key in cur:
                print(f"  {key}: back to {BASE}")
                del cur[key]; changed += 1
        print(f"{os.path.basename(mod.rstrip(chr(92) + '/'))}: {own} of {len(seen)} subtypes have the mod's own icon")
    if changed and not a.dry_run:
        with open(a.overrides, "w", encoding="utf-8", newline="\n") as f:
            json.dump(cur, f, indent=2, ensure_ascii=False)
    print(f"{changed} entries {'would change' if a.dry_run else 'changed'} in {a.overrides}")
    if a.sheet and rows:
        sheet(rows, a.sheet)
        print(f"outlines: {a.sheet}")


if __name__ == "__main__":
    main()
