"""Build a model from mwm_mesh.py JSON files (the main model, and subparts placed at their dummies), link SEUT's
vanilla materials, and render an icon PNG with Icon Studio's look. Run in a background Blender:
    blender.exe -b --python build_render.py -- <json dir> <out.png> <hatch deg> <yaw deg> <pitch deg> [res] [save.blend]
<hatch deg> turns subparts whose dummy name holds "Hatch" about their X axis (0 for none). Icon Studio's camera is
yaw 225, pitch 35.
Env: MAIN (the main model's JSON name; default: the only JSON in the folder), LAYFLAT=1 (turn the model's +Y face
up), SIZE (camera distance factor), EXPOSURE, DENOISE, ORTHO, GLOSS, FLIPV, DROP_TURRET_ROT, FRAME_EXCLUDE,
NO_PAINT (regex of materials that skip the grey paint), DECALS (regex of decal materials: camera-only, no shadows),
ALIAS ("Part=SeutMaterial[:cm.png];...": render a part with another library material), SEUT_MATERIALS (SEUT's
Materials.blend).
"""
import bpy, bmesh, glob, json, math, os, re, sys
from mathutils import Matrix, Vector

argv = sys.argv[sys.argv.index("--") + 1:]
JD, OUT = argv[0], argv[1]
HATCH, YAW, PITCH = float(argv[2]), float(argv[3]), float(argv[4])
RES = int(argv[5]) if len(argv) > 5 else 512
SAVE = argv[6] if len(argv) > 6 else None
LIB = os.environ.get("SEUT_MATERIALS", r"C:\SE_Assets\Materials\Materials.blend")
PAINT = (0.62, 0.62, 0.62, 1.0)

C = Matrix(((1, 0, 0), (0, 0, -1), (0, 1, 0)))          # mwm (+Y up, -Z forward) -> Blender (+Z up, +Y forward)
CI = C.inverted()
MAIN = os.environ.get("MAIN", "")
G = Matrix.Rotation(math.radians(90), 4, "X") if os.environ.get("LAYFLAT", "0") == "1" else Matrix()   # +Y face up
SIZE = float(os.environ.get("SIZE", "1.12"))
DROP = os.environ.get("DROP_TURRET_ROT", "0") == "1"   # MyLargeTurretBase.RotateModels
def mkey(n):
    return re.sub(r"\.\d{3}$", "", n) if n else n
# ALIAS="Part=SeutMaterial[:cm.png];...": render a mesh part with another library material, optionally with its CM swapped
ALIAS = {}
for item in filter(None, os.environ.get("ALIAS", "").split(";")):
    part, rest = item.split("=", 1)
    tmpl, _, cm = rest.partition(":")
    ALIAS[part] = (tmpl, cm or None)

for o in list(bpy.data.objects):
    bpy.data.objects.remove(o)

need = set()
models = {}
for fp in glob.glob(os.path.join(JD, "*.json")):
    n = os.path.splitext(os.path.basename(fp))[0]
    models[n] = json.load(open(fp))
    need |= {mkey(p["material"]) for p in models[n]["MeshParts"] if p["material"]}
if not MAIN:
    assert len(models) == 1, "several JSON files: set MAIN"
    MAIN = next(iter(models))
need |= {t for t, _ in ALIAS.values()}
with bpy.data.libraries.load(LIB, link=True) as (src, dst):
    dst.materials = [m for m in src.materials if m in need]
mats = {m.name: m for m in dst.materials if m}
print("linked", len(mats), "of", len(need))


def build_mesh(name, d):
    verts = [C @ Vector(v) for v in d["Vertices"]]
    nrm = [(C @ Vector(v)).normalized() for v in d["Normals"]]
    uv = d["TexCoords0"]
    me = bpy.data.meshes.new(name)
    faces, fmat, slots, flips = [], [], [], 0
    seen, skipped = set(), 0
    for p in d["MeshParts"]:
        key = mkey(p["material"]) or "NONE"
        if key not in slots:
            slots.append(key)
        si = slots.index(key)
        ix = p["indices"]
        for i in range(0, len(ix), 3):
            a, b, c = ix[i], ix[i + 1], ix[i + 2]
            k = tuple(sorted((a, b, c)))
            if a == b or b == c or a == c or k in seen:   # degenerate or duplicate triangles crash custom normals
                skipped += 1
                continue
            seen.add(k)
            fn = (verts[b] - verts[a]).cross(verts[c] - verts[a])
            if fn.dot(nrm[a] + nrm[b] + nrm[c]) < 0:
                a, b = b, a
                flips += 1
            faces.append((a, b, c)); fmat.append(si)
    me.from_pydata([tuple(v) for v in verts], [], faces)
    for key in slots:
        m = mats.get(key)
        if key in ALIAS:
            tmpl, cm = ALIAS[key]
            m = bpy.data.materials.get(key + "_alias")
            if m is None:
                m = mats[tmpl].copy(); m.name = key + "_alias"; m["alias"] = True
                if cm:
                    img = bpy.data.images.load(cm)
                    for nd in m.node_tree.nodes:
                        if nd.type == "TEX_IMAGE" and nd.image and "_cm" in nd.image.name.lower():
                            img.colorspace_settings.name = nd.image.colorspace_settings.name
                            img.alpha_mode = nd.image.alpha_mode      # CM alpha is metalness: keep RGB where it is 0
                            print("alias", key, "CM", nd.image.name, "->", os.path.basename(cm)); nd.image = img
                print("alias", key, "uses", tmpl)
        if m is None:
            metal = {"Copper": (0.72, 0.42, 0.20, 1)}.get(key)     # texture the game lacks: plain metal of that colour
            m = bpy.data.materials.get("Fallback_" + key) or bpy.data.materials.new("Fallback_" + key)
            m.use_nodes = True
            bsdf = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
            bsdf.inputs["Base Color"].default_value = metal or (0.08, 0.08, 0.08, 1)
            if metal:
                bsdf.inputs["Metallic"].default_value = 1.0; bsdf.inputs["Roughness"].default_value = 0.35
            print("fallback material for", key)
        me.materials.append(m)
    for poly, mi in zip(me.polygons, fmat):
        poly.material_index = mi
        poly.use_smooth = True
    uvl = me.uv_layers.new(name="UVMap")
    for loop in me.loops:
        u, v = uv[loop.vertex_index]
        uvl.data[loop.index].uv = (u, 1.0 - v) if os.environ.get("FLIPV", "1") == "1" else (u, v)
    me.normals_split_custom_set_from_vertices([tuple(n) for n in nrm])
    me.validate(clean_customdata=False)
    print(name, "faces", len(faces), "flipped", flips, "skipped", skipped)
    return me


meshes = {}
root = bpy.data.objects.new("Root", meshes.setdefault(MAIN, build_mesh(MAIN, models[MAIN])))
bpy.context.scene.collection.objects.link(root)
root.matrix_world = G


def dummy_matrix(m16):
    M = Matrix([m16[0:4], m16[4:8], m16[8:12], m16[12:16]]).transposed()   # XNA row vectors -> column matrix
    R = C @ M.to_3x3() @ CI
    out = R.to_4x4()
    out.translation = C @ M.translation
    return out


placed = 0
def place(model, parent_mw):
    global placed
    for dm in models[model].get("Dummies", []):
        f = dm["data"].get("file")
        if not dm["name"].startswith("subpart_") or not f or f not in models:
            continue
        if f not in meshes:
            meshes[f] = build_mesh(f, models[f])
        o = bpy.data.objects.new(dm["name"], meshes[f])
        bpy.context.scene.collection.objects.link(o)
        dmx = dummy_matrix(dm["matrix"])
        if DROP and re.search(r"TurretBase\d|TurretBarrels", dm["name"]):   # vanilla turrets keep only the position
            dmx = Matrix.Translation(dmx.translation)
        mw = parent_mw @ dmx
        if "Hatch" in f and HATCH:
            mw = mw @ (C @ Matrix.Rotation(math.radians(HATCH), 3, "X") @ CI).to_4x4()   # WeaponCore turns about the subpart X
        o.matrix_world = mw
        placed += 1
        place(f, mw)
place(MAIN, G)
print("subparts placed", placed)

# Linked materials are read-only; override the paint through local copies.
for o in bpy.data.objects:
    for i, slot in enumerate(o.material_slots):
        m = slot.material
        if m and (m.library or m.get("alias")):
            loc = m if m.get("alias") else (bpy.data.materials.get(m.name + "_local") or m.copy())
            if not m.get("alias"):
                loc.name = m.name + "_local"
            g = next((n for n in loc.node_tree.nodes if n.type == "GROUP"), None)
            if g and "Color Override" in g.inputs and not re.match(os.environ.get("NO_PAINT", "^$"), m.name):
                g.inputs["Color Override"].default_value = PAINT
                g.inputs["Color Override Alpha"].default_value = 1.0
            if g and "Color Overlay Alpha" in g.inputs:
                g.inputs["Color Overlay Alpha"].default_value = 0.0
            if not loc.get("culled"):                       # the game culls back faces; Cycles draws them
                nt = loc.node_tree
                out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL" and n.is_active_output)
                src = out.inputs["Surface"].links[0].from_socket
                geo = nt.nodes.new("ShaderNodeNewGeometry"); tr = nt.nodes.new("ShaderNodeBsdfTransparent")
                mix = nt.nodes.new("ShaderNodeMixShader")
                nt.links.new(geo.outputs["Backfacing"], mix.inputs[0]); nt.links.new(src, mix.inputs[1])
                nt.links.new(tr.outputs[0], mix.inputs[2]); nt.links.new(mix.outputs[0], out.inputs["Surface"])
                loc["culled"] = True
            o.material_slots[i].link = "OBJECT"
            o.material_slots[i].material = loc

# The game draws decals with no shadows or bounce light; in Cycles their quads darken the paint behind them.
# Split the decal faces into their own camera-only object.
DEC = re.compile(os.environ.get("DECALS", r"Atlas|Decal|Paneling|Doodads"))
for o in [o for o in bpy.data.objects if o.type == "MESH"]:
    slots = {i for i, sl in enumerate(o.material_slots) if sl.material and DEC.search(sl.material.name)}
    if not slots:
        continue
    o2 = bpy.data.objects.new(o.name + "_decals", o.data.copy()); bpy.context.scene.collection.objects.link(o2)
    o2.matrix_world = o.matrix_world
    o.data = o.data.copy()                       # the mesh may be shared with another object
    for ob, keep in ((o, False), (o2, True)):
        bm = bmesh.new(); bm.from_mesh(ob.data)
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if (f.material_index in slots) != keep], context="FACES")
        bm.to_mesh(ob.data); bm.free()
    for i, sl in enumerate(o.material_slots):
        o2.material_slots[i].link = "OBJECT"; o2.material_slots[i].material = sl.material
    for k in ("visible_shadow", "visible_diffuse", "visible_glossy", "visible_transmission", "visible_volume_scatter"):
        setattr(o2, k, False)
    print("decals camera-only on", o.name, len(slots), "slots")

sc = bpy.context.scene
try:
    sc.render.engine = "CYCLES"
except TypeError as e:
    print(e)
sc.cycles.samples = 96
sc.cycles.use_denoising = os.environ.get("DENOISE", "1") == "1"
sc.render.film_transparent = True
sc.render.resolution_x = sc.render.resolution_y = RES
sc.view_settings.view_transform = "Standard"
sc.view_settings.exposure = float(os.environ.get("EXPOSURE", "0"))
sc.render.image_settings.file_format = "PNG"
sc.render.image_settings.color_mode = "RGBA"

# camera: orthographic-like long lens looking at the bbox centre from yaw/pitch
pts = [o.matrix_world @ Vector(c) for o in bpy.data.objects if o.type == "MESH" for c in o.bound_box]
lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
ctr = (lo + hi) / 2
rad = (hi - lo).length / 2
cam_d = bpy.data.cameras.new("ICON"); cam_d.lens = 85
cam = bpy.data.objects.new("ICON", cam_d); sc.collection.objects.link(cam); sc.camera = cam
yaw, pitch = math.radians(YAW), math.radians(PITCH)
dirv = Vector((math.cos(pitch) * math.sin(yaw), -math.cos(pitch) * math.cos(yaw), math.sin(pitch)))
dist = rad / math.sin(math.atan(18 / 85)) * SIZE
cam.location = ctr + dirv * dist
cam.rotation_euler = (-dirv).to_track_quat("-Z", "Y").to_euler()
if os.environ.get("ORTHO", "0") == "1":                    # isometric: no perspective
    cam_d.type = "ORTHO"; cam_d.ortho_scale = 2.3 * rad

world = bpy.data.worlds.new("W"); sc.world = world; world.use_nodes = True
bg = next(n for n in world.node_tree.nodes if n.type == "BACKGROUND")
bg.inputs[0].default_value = (1, 1, 1, 1); bg.inputs[1].default_value = 0.9
GLOSS = float(os.environ.get("GLOSS", "1"))
if GLOSS != 1:                                             # brighter sky in reflections only, like a studio environment
    nt = world.node_tree
    lp = nt.nodes.new("ShaderNodeLightPath")
    mul = nt.nodes.new("ShaderNodeMath"); mul.operation = "MULTIPLY_ADD"
    nt.links.new(lp.outputs["Is Glossy Ray"], mul.inputs[0])
    mul.inputs[1].default_value = 0.9 * (GLOSS - 1); mul.inputs[2].default_value = 0.9
    nt.links.new(mul.outputs[0], bg.inputs[1])


def light(name, kind, rot, energy):
    ld = bpy.data.lights.new(name, kind); ld.energy = energy
    if kind == "SUN":
        ld.angle = math.radians(8)
    lo_ = bpy.data.objects.new(name, ld); sc.collection.objects.link(lo_)
    lo_.rotation_euler = rot


light("Key", "SUN", (math.radians(50), 0, math.radians(-35) + yaw), 3.0)
light("Fill", "SUN", (math.radians(70), 0, math.radians(120) + yaw), 1.5)

sc.render.filepath = OUT
bpy.ops.render.render(write_still=True)
print("wrote", OUT)
EXCL = os.environ.get("FRAME_EXCLUDE", "").lower()
if EXCL:                                                  # second pass: alpha of everything but the excluded parts, for framing
    for o in bpy.data.objects:
        if o.type == "MESH" and EXCL in o.name.lower():
            o.hide_render = True
    sc.cycles.samples = 8
    sc.render.filepath = os.path.splitext(OUT)[0] + "_frame.png"
    bpy.ops.render.render(write_still=True)
    print("wrote", sc.render.filepath)
if SAVE:
    bpy.ops.wm.save_as_mainfile(filepath=SAVE)
