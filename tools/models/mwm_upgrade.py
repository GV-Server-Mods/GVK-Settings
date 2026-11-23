r"""Copy a mod folder for Icon Studio, rewriting old-format .mwm files (before 01157001) in the current format.

Keen's Icon Studio crashes on .mwm files older than 01157001 (2026-11-23, GVK_HoverEngines' 1x1 models).
Those formats carry 7 material floats in each mesh part that 01157001 dropped (MyModelImporter, see
mwm_mesh.mesh_parts); this drops them and sets the header to 01157001. The source mod is never changed.
    python mwm_upgrade.py <mod folder> <scratch copy>
Then point Icon Studio's Mod Directory at the scratch copy. Hub #766.
"""
import os, shutil, struct, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mwmlib

NEW = 1157001


def upgrade_parts(blob, version):
    """MeshParts blob in an old layout -> the same parts in the 01157001 layout."""
    out = bytearray()
    p = mwmlib.rstr(blob, 0)[1]
    out += blob[:p + 4]
    n = struct.unpack_from("<i", blob, p)[0]; p += 4
    for _ in range(n):
        s = p
        p += 4                                                  # material hash
        ni = struct.unpack_from("<i", blob, p)[0]; p += 4 + 4 * ni
        has = blob[p] != 0; p += 1
        if has:
            p = mwmlib.rstr(blob, p)[1]                         # material name
            nt = struct.unpack_from("<i", blob, p)[0]; p += 4
            for _ in range(2 * nt):
                p = mwmlib.rstr(blob, p)[1]
            if version >= 1068001:
                nu = struct.unpack_from("<i", blob, p)[0]; p += 4
                for _ in range(2 * nu):
                    p = mwmlib.rstr(blob, p)[1]
            out += blob[s:p]
            p += 28                                             # the 7 floats 01157001 dropped
            s = p
            tech, p = mwmlib.rstr(blob, p)
            if tech == "GLASS" and version >= 1043001:
                p = mwmlib.rstr(blob, p)[1]; p = mwmlib.rstr(blob, p)[1]; p += 1
        out += blob[s:p]
    assert p == len(blob), (p, len(blob))
    return bytes(out)


def upgrade(path):
    """Rewrite one .mwm in place if it is older than 01157001; return its old version or None."""
    prefix, tags, blobs = mwmlib.load(path)
    head = mwmlib.rstr(prefix, 0)[1] + 4
    ver = mwmlib.rstr(prefix, head)[0]
    version = int(ver.replace("Version:", ""))
    if version >= NEW:
        return None
    if "MeshParts" in blobs:
        blobs["MeshParts"] = upgrade_parts(blobs["MeshParts"], version)
    new_ver = ("Version:%08d" % NEW).encode()
    assert len(new_ver) == len(ver.encode())
    prefix = prefix.replace(ver.encode(), new_ver, 1)
    mwmlib.save(path, prefix, tags, blobs)
    return version


def main(src, dst):
    if os.path.exists(dst):
        shutil.rmtree(dst)
    shutil.copytree(src, dst)
    done = 0
    for root, _, files in os.walk(dst):
        for f in files:
            if f.lower().endswith(".mwm") and upgrade(os.path.join(root, f)):
                done += 1
    print(f"{done} .mwm files upgraded to {NEW} in {dst}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
