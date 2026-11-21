"""Read and rebuild a Space Engineers .mwm tag by tag (VRageRender.Import.MyModelImporter, version 1157001)."""
import os, struct

# the game's Content\Models folder, where vanilla material descriptors are copied from (SE_CONTENT overrides Content)
MODELS = os.path.join(os.environ.get("SE_CONTENT", r"C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers\Content"), "Models")


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


def wstr(s):
    e = s.encode("utf-8")
    return w7(len(e)) + e


def load(path):
    """-> (prefix bytes up to the index, [tag names], {tag: blob bytes incl. its name})"""
    b = open(path, "rb").read()
    p = rstr(b, 0)[1]
    nh = struct.unpack_from("<i", b, p)[0]; p += 4
    for _ in range(nh):
        p = rstr(b, p)[1]
    prefix = b[:p]
    n = struct.unpack_from("<i", b, p)[0]; p += 4
    tags, offs = [], []
    for _ in range(n):
        t, p = rstr(b, p); tags.append(t); offs.append(struct.unpack_from("<i", b, p)[0]); p += 4
    assert offs[0] == p and offs == sorted(offs), "index is not followed by its tags in order"
    blobs = {t: b[o:(offs[i + 1] if i + 1 < n else len(b))] for i, (t, o) in enumerate(zip(tags, offs))}
    return prefix, tags, blobs


def save(path, prefix, tags, blobs):
    head = len(prefix) + 4 + sum(len(wstr(t)) + 4 for t in tags)
    index, data, off = bytearray(struct.pack("<i", len(tags))), bytearray(), head
    for t in tags:
        index += wstr(t) + struct.pack("<i", off); data += blobs[t]; off += len(blobs[t])
    open(path, "wb").write(prefix + index + data)


def parts(blob, version=1157001):
    """MeshParts blob -> list of (raw bytes, material name, indices, textures dict, technique)."""
    p = rstr(blob, 0)[1]
    n = struct.unpack_from("<i", blob, p)[0]; p += 4
    out = []
    for _ in range(n):
        s = p; p += 4
        ni = struct.unpack_from("<i", blob, p)[0]; p += 4
        idx = list(struct.unpack_from("<%di" % ni, blob, p)); p += 4 * ni
        has = blob[p] != 0; p += 1
        mat, tex, tech = None, {}, None
        if has:
            mat, p = rstr(blob, p)
            nt = struct.unpack_from("<i", blob, p)[0]; p += 4
            for _ in range(nt):
                k, p = rstr(blob, p); v, p = rstr(blob, p); tex[k] = v
            nu = struct.unpack_from("<i", blob, p)[0]; p += 4
            for _ in range(2 * nu):
                p = rstr(blob, p)[1]
            tech, p = rstr(blob, p)
            if tech == "GLASS":
                p = rstr(blob, p)[1]; p = rstr(blob, p)[1]; p += 1
        out.append((blob[s:p], mat, idx, tex, tech))
    assert p == len(blob), (p, len(blob))
    return out
