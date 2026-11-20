"""Give flat blocks and items a steeper camera in Keen's Icon Studio, like Keen's own icons.

Icon Studio looks at every icon from 45 degrees around and 35 up, unless its override file
(default_icon_render_overrides.json, next to SEIconStudio.exe) holds a camera for that subtype. Keen set
those by hand; flat things look down further (floor slab 45, steel plate 55). This script measures each
model of a mod (height against its mean width) and writes a camera for the flat ones: 35 for anything at
least half as tall as wide, up to 60 for a thin plate, in 5 degree steps. Keen's own entries, read from the
copy built into SEIconStudio.pck, are never changed. Run it again after a Steam update resets the file.

    python tools/icon_tilt.py <mod folder> [<mod folder> ...] [--dry-run]

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


# --- definitions and Icon Studio's override file ---
def definitions(mod):
    for path in glob.glob(os.path.join(mod, "Data", "**", "*.sbc"), recursive=True):
        try:
            root = ET.parse(path).getroot()
        except ET.ParseError:
            continue                              # prefabs and other files that are not definitions
        for d in root.iter():
            idn, model = d.find("Id"), (d.findtext("Model") or "").strip()
            if idn is None or not model or d.find("Icon") is None:
                continue
            t = idn.findtext("TypeId") or idn.get("Type") or ""
            s = idn.findtext("SubtypeId") or idn.get("Subtype") or ""
            yield (s or re.sub("^MyObjectBuilder_", "", t) + "#EMPTY_SUBTYPE"), model


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
    a = ap.parse_args()
    keen = keen_keys()
    cur = json.load(open(a.overrides, encoding="utf-8"))
    changed = 0
    for mod in a.mods:
        seen = set()
        for key, model in definitions(mod):
            if key in seen or key in keen:
                continue
            seen.add(key)
            path = next((p for p in (os.path.join(mod, model), os.path.join(a.content, model)) if os.path.exists(p)), None)
            if not path:
                print(f"  {key}: model not found ({model})"); continue
            try:
                pitch, ratio = tilt(points(path))
            except (KeyError, IndexError, struct.error) as e:
                print(f"  {key}: cannot read {model} ({e!r})"); continue
            want = {"cam_rot": [-math.radians(pitch), YAW, 0.0]} if pitch != BASE else None
            if want and cur.get(key) != want:
                print(f"  {key}: tilt {pitch} (height/width {ratio:.2f})")
                cur[key] = want; changed += 1
            elif not want and key in cur:
                print(f"  {key}: back to {BASE}")
                del cur[key]; changed += 1
        print(f"{os.path.basename(mod.rstrip(chr(92) + '/'))}: {len(seen)} subtypes checked")
    if changed and not a.dry_run:
        with open(a.overrides, "w", encoding="utf-8", newline="\n") as f:
            json.dump(cur, f, indent=2, ensure_ascii=False)
    print(f"{changed} entries {'would change' if a.dry_run else 'changed'} in {a.overrides}")


if __name__ == "__main__":
    main()
