"""Point a mesh part's textures in a Space Engineers .mwm at other files, keeping everything else byte for byte.
Strings are length-prefixed, so the MeshParts section changes size; the tag index's absolute offsets after it
are shifted to match (VRageRender.Import.MyModelImporter layout, version 1157001).
    python mwm_retexture.py <in.mwm> <out.mwm> Material:TextureKey=New\\Path.dds [...]
"""
import struct, sys


def r7(b, p):
    v = s = 0
    while True:
        c = b[p]; p += 1; v |= (c & 0x7F) << s
        if c < 0x80:
            return v, p
        s += 7


def w7(n):
    out = bytearray()
    while True:
        c = n & 0x7F; n >>= 7
        out.append(c | (0x80 if n else 0))
        if not n:
            return bytes(out)


def rstr(b, p):
    n, p = r7(b, p)
    return b[p:p + n].decode("utf-8"), p + n


def main(src, dst, maps):
    want = {}
    for m in maps:
        k, new = m.split("=", 1); mat, key = k.split(":")
        want[(mat, key)] = new
    b = open(src, "rb").read()
    p = rstr(b, 0)[1]
    nh = struct.unpack_from("<i", b, p)[0]; p += 4
    header = []
    for _ in range(nh):
        s, p = rstr(b, p); header.append(s)
    version = int(header[0].replace("Version:", ""))
    assert version >= 1157001, version
    index = {}
    n = struct.unpack_from("<i", b, p)[0]; p += 4
    for _ in range(n):
        tag, p = rstr(b, p); index[tag] = (struct.unpack_from("<i", b, p)[0], p); p += 4
    start = index["MeshParts"][0]
    p = rstr(b, start)[1]
    edits = []                                   # (pos, end, new bytes)
    count = struct.unpack_from("<i", b, p)[0]; p += 4
    for _ in range(count):
        p += 4                                   # material hash
        ni = struct.unpack_from("<i", b, p)[0]; p += 4 + 4 * ni
        has = b[p] != 0; p += 1
        if not has:
            continue
        mat, p = rstr(b, p)
        nt = struct.unpack_from("<i", b, p)[0]; p += 4
        for _ in range(nt):
            key, p = rstr(b, p)
            vpos = p; val, p = rstr(b, p)
            new = want.pop((mat, key), None)
            if new is not None:
                enc = new.encode("utf-8")
                edits.append((vpos, p, w7(len(enc)) + enc))
                print(f"{mat} {key}: {val} -> {new}")
        nu = struct.unpack_from("<i", b, p)[0]; p += 4
        for _ in range(2 * nu):
            p = rstr(b, p)[1]
        tech, p = rstr(b, p)
        if tech == "GLASS":
            p = rstr(b, p)[1]; p = rstr(b, p)[1]; p += 1
    if want:
        sys.exit(f"not found: {sorted(want)}")
    out, last, delta = bytearray(), 0, 0
    for a, e, nb in edits:
        out += b[last:a] + nb; last = e; delta += len(nb) - (e - a)
    out += b[last:]
    for tag, (off, ipos) in index.items():
        if off > start:
            struct.pack_into("<i", out, ipos, off + delta)
    open(dst, "wb").write(out)
    print("delta", delta, "bytes; wrote", dst)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], sys.argv[3:])
