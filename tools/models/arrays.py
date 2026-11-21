"""Unpack and pack the vertex arrays of an .mwm (PositionPacker, VF_Packer)."""
import math, struct
import mwmlib


def body(blob):
    return mwmlib.rstr(blob, 0)[1]


def unpack_pos(blob):
    p = body(blob); n = struct.unpack_from("<i", blob, p)[0]
    r = struct.unpack_from("<%de" % (4 * n), blob, p + 4)
    return [(r[i] * r[i + 3], r[i + 1] * r[i + 3], r[i + 2] * r[i + 3]) for i in range(0, 4 * n, 4)]


def unpack_nrm(blob):
    p = body(blob); n = struct.unpack_from("<i", blob, p)[0]
    out = []
    for (v,) in struct.iter_unpack("<I", blob[p + 4:p + 4 + 4 * n]):
        x = (v & 0x7FFF) / 32767 * 2 - 1; s = 1 if v & 0x8000 else -1; y = (v >> 16) / 32767 * 2 - 1
        out.append((x, y, s * math.sqrt(max(0.0, 1 - x * x - y * y))))
    return out


def unpack_uv(blob):
    p = body(blob); n = struct.unpack_from("<i", blob, p)[0]
    r = struct.unpack_from("<%de" % (2 * n), blob, p + 4)
    return [(r[i], r[i + 1]) for i in range(0, 2 * n, 2)]


def pack_pos(v):
    m = min(math.floor(max(abs(c) for c in v)), 2048.0)
    s = 1.0 / m if m > 0 else 1.0
    if m <= 0:
        m = 1.0
    return struct.pack("<4e", v[0] * s, v[1] * s, v[2] * s, m)


def pack_nrm(n):
    x = int((0.5 * (n[0] + 1)) * 32767) & 0xFFFF; y = int((0.5 * (n[1] + 1)) * 32767) & 0xFFFF
    return struct.pack("<I", (x | ((1 if n[2] > 0 else 0) << 15)) | (y << 16))


def append(blob, items):
    """Add packed entries to an array tag blob (name, int count, data)."""
    p = body(blob); n = struct.unpack_from("<i", blob, p)[0]
    return blob[:p] + struct.pack("<i", n + len(items)) + blob[p + 4:] + b"".join(items)
