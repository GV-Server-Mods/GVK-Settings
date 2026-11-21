"""Read the mesh of a Space Engineers .mwm (version 1052002+) into JSON: positions, normals, UVs, mesh parts
(material, technique, textures, indices) and dummies, in the model's own space (+Y up, forward -Z).
Formats from VRageRender.Import.MyModelImporter, MyMeshPartInfo, MyMaterialDescriptor, VRage.Import.PositionPacker
and VF_Packer.UnpackNormal.
    python mwm_mesh.py <model.mwm> <out.json>
"""
import json, math, struct, sys
import os
# mwm_dummies.py comes from the se-dev-seut skill's scripts folder (SEUT_SCRIPTS overrides)
sys.path.insert(0, os.environ.get("SEUT_SCRIPTS", os.path.expanduser(r"~\.agents\skills\se-dev-seut\scripts")))
from mwm_dummies import read_string, read_int, read_floats, read_dummies


def read_bool(f):
    return f.read(1) != b"\x00"


def positions(f):
    n = read_int(f)
    raw = struct.unpack("<%de" % (4 * n), f.read(8 * n))
    return [[raw[i] * raw[i + 3], raw[i + 1] * raw[i + 3], raw[i + 2] * raw[i + 3]] for i in range(0, 4 * n, 4)]


def normals(f):
    n = read_int(f)
    out = []
    for x, y, z, w in struct.iter_unpack("<4B", f.read(4 * n)):
        s = 1.0 if y > 127.5 else -1.0
        if s > 0:
            y -= 128
        a = 2 * ((x + 256 * y) / 32767) - 1
        b = 2 * ((z + 256 * w) / 32767) - 1
        out.append([a, b, s * math.sqrt(max(0.0, 1 - a * a - b * b))])
    return out


def uvs(f):
    n = read_int(f)
    raw = struct.unpack("<%de" % (2 * n), f.read(4 * n))
    return [[raw[i], raw[i + 1]] for i in range(0, 2 * n, 2)]


def mesh_parts(f, version):
    parts = []
    for _ in range(read_int(f)):
        read_int(f)                                       # material hash
        n = read_int(f)
        idx = list(struct.unpack("<%di" % n, f.read(4 * n)))
        part = {"indices": idx, "material": None, "technique": None, "textures": {}}
        if read_bool(f):
            part["material"] = read_string(f) or None
            for _ in range(read_int(f)):
                k = read_string(f)
                part["textures"][k] = read_string(f)
            if version >= 1068001:
                for _ in range(read_int(f)):
                    read_string(f); read_string(f)
            if version < 1157001:
                read_floats(f, 7)
            part["technique"] = read_string(f)
            if part["technique"] == "GLASS" and version >= 1043001:
                read_string(f); read_string(f); read_bool(f)
        parts.append(part)
    return parts


def main(path, out):
    with open(path, "rb") as f:
        read_string(f)
        header = [read_string(f) for _ in range(read_int(f))]
        version = int(header[0].replace("Version:", ""))
        assert version >= 1052002, version
        index = {}
        for _ in range(read_int(f)):
            tag = read_string(f)
            index[tag] = read_int(f)
        data = {"version": version, "tags": sorted(index)}
        for tag, fn in (("Vertices", positions), ("Normals", normals), ("TexCoords0", uvs)):
            f.seek(index[tag]); read_string(f); data[tag] = fn(f)
        f.seek(index["MeshParts"]); read_string(f); data["MeshParts"] = mesh_parts(f, version)
        if "SwapWindingOrder" in index:
            f.seek(index["SwapWindingOrder"]); read_string(f); data["SwapWindingOrder"] = read_bool(f)
        if "Dummies" in index:
            f.seek(index["Dummies"]); read_string(f)
            data["Dummies"] = [{"name": n, "matrix": list(m), "data": d} for n, m, d in read_dummies(f)]
    json.dump(data, open(out, "w"))
    print(path.split("\\")[-1], "v", version, "verts", len(data["Vertices"]), "parts",
          [(p["material"], p["technique"], len(p["indices"]) // 3) for p in data["MeshParts"]],
          "swap", data.get("SwapWindingOrder"))


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
