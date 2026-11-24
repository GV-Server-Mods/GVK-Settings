r"""Hide one material's faces past a plane in a Space Engineers .mwm, keeping the file the same size.

Each hidden triangle gets all three corners on one vertex, so it has no area and never draws; vertices, UVs,
other parts and every offset stay byte for byte. Used for decal faces wrapped onto a curved surface, which
render broken (a DECAL_CUTOUT material works on flat faces only; se-dev-seut SKILL.md).
    python mwm_hide_faces.py <in.mwm> <out.mwm> <material> <axis x|y|z> <min> [--max N] [--dry-run]
A triangle is hidden when all its corners lie between min and max on that axis (model metres). Hub #766:
the Griffin missile's nose decal (owner, 2026-11-23).
"""
import argparse, os, struct, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mwmlib


def vertices(blob):
    p = mwmlib.rstr(blob, 0)[1]
    n = struct.unpack_from("<i", blob, p)[0]; p += 4
    raw = struct.unpack_from("<%de" % (4 * n), blob, p)    # HalfVector4, position = xyz * w
    return [(raw[i] * raw[i + 3], raw[i + 1] * raw[i + 3], raw[i + 2] * raw[i + 3]) for i in range(0, 4 * n, 4)]


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("src"); ap.add_argument("dst"); ap.add_argument("material")
    ap.add_argument("axis", choices="xyz"); ap.add_argument("min", type=float)
    ap.add_argument("--max", type=float, default=float("inf"))
    ap.add_argument("--dry-run", action="store_true")
    a = ap.parse_args()
    prefix, tags, blobs = mwmlib.load(a.src)
    v, ax = vertices(blobs["Vertices"]), "xyz".index(a.axis)
    blob = bytearray(blobs["MeshParts"])
    p = mwmlib.rstr(blob, 0)[1]
    count = struct.unpack_from("<i", blob, p)[0]; p += 4
    hidden = total = 0
    for raw, mat, idx, tex, tech in mwmlib.parts(bytes(blob)):
        start = p + 8                                       # material hash, index count
        if mat == a.material:
            for t in range(0, len(idx), 3):
                tri = idx[t:t + 3]
                total += 1
                if all(a.min <= v[i][ax] <= a.max for i in tri) and len(set(tri)) > 1:
                    struct.pack_into("<3i", blob, start + 4 * t, tri[0], tri[0], tri[0])
                    hidden += 1
        p += len(raw)
    print(f"{os.path.basename(a.src)}: {a.material}, {hidden} of {total} triangles hidden")
    if not hidden:
        sys.exit("nothing matched")
    if not a.dry_run:
        blobs["MeshParts"] = bytes(blob)
        mwmlib.save(a.dst, prefix, tags, blobs)


if __name__ == "__main__":
    main()
