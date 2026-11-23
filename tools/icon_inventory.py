r"""List the icons a mod ships for its blocks, items and variant groups, and flag the old blue-tinted ones.

Usage: python tools/icon_inventory.py <mod folder> [<mod folder> ...] --out <scratch dir> [--all]
A mod folder is the one that holds Data\ (a deployed mod, a Workshop item or a repo's Content folder).
Writes <out>\icons.csv (every shipped icon, with its blue score) and <out>\<mod>_blue.png, a contact
sheet of the flagged icons. Vanilla icon paths are skipped (Keen updates those). Hub #766.
"""
import argparse, csv, os, re, subprocess, xml.etree.ElementTree as ET
from PIL import Image, ImageDraw

TEXCONV = os.path.expandvars(r"%APPDATA%\Blender Foundation\Blender\4.5\scripts\addons\space-engineers-utilities\tools\texconv.exe")
# Mean of (blue - (red + green) / 2) over opaque pixels. Calibrated 2026-11-23 on Keen's retired blue
# ShortWheelSuspension icons and current full-color Warfare III icons (see --calibrate).
BLUE_LIMIT = 12.0


def strip_ns(root):
    for el in root.iter():
        if isinstance(el.tag, str) and '}' in el.tag:
            el.tag = el.tag.split('}', 1)[1]
    return root


def definitions(mod):
    """Yield (kind, type, subtype, icon path, model) for each definition with an <Icon> in the mod's Data."""
    for root_dir, _, files in os.walk(os.path.join(mod, 'Data')):
        for f in files:
            if not f.lower().endswith('.sbc'):
                continue
            try:
                root = strip_ns(ET.parse(os.path.join(root_dir, f)).getroot())
            except ET.ParseError:
                continue
            for el in root.iter():
                icons = [i.text.strip() for i in el.findall('Icon') if i.text and i.text.strip()]
                if not icons:
                    continue
                idn = el.find('Id')
                if idn is None:
                    continue
                t = (idn.findtext('TypeId') or idn.get('Type') or '').replace('MyObjectBuilder_', '')
                s = idn.findtext('SubtypeId') or idn.get('Subtype') or ''
                kind = 'group' if el.tag == 'BlockVariantGroup' else ('block' if el.find('CubeSize') is not None else 'item')
                yield kind, t, s, icons[0], el.findtext('Model') or '', f


def to_png(path, cache):
    if path.lower().endswith('.png'):
        return path
    os.makedirs(cache, exist_ok=True)
    out = os.path.join(cache, os.path.splitext(os.path.basename(path))[0] + '.png')
    if not os.path.exists(out) or os.path.getmtime(out) < os.path.getmtime(path):
        # texconv builds a broken output path from a forward-slash input path
        subprocess.run([TEXCONV, os.path.normpath(path), '-ft', 'png', '-y', '-o', os.path.normpath(cache)], capture_output=True, check=False)
    return out if os.path.exists(out) else None


def blue_score(png):
    im = Image.open(png).convert('RGBA').resize((64, 64))
    tot, n = 0.0, 0
    for r, g, b, a in (im.get_flattened_data() if hasattr(im, 'get_flattened_data') else im.getdata()):
        if a > 128:
            tot += b - (r + g) / 2
            n += 1
    return tot / n if n else 0.0


def sheet(rows, out, title):
    cell, cols = 140, 8
    h = ((len(rows) + cols - 1) // cols) * (cell + 28) + 30
    img = Image.new('RGB', (cols * cell, max(h, 60)), (40, 44, 52))
    d = ImageDraw.Draw(img)
    d.text((6, 6), title, fill=(255, 255, 255))
    for i, r in enumerate(rows):
        x, y = (i % cols) * cell, 30 + (i // cols) * (cell + 28)
        icon = Image.open(r['png']).convert('RGBA').resize((128, 128))
        img.paste(icon, (x + 6, y), icon)
        d.text((x + 4, y + 130), r['subtype'][:22], fill=(220, 220, 220))
    img.save(out)


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument('mods', nargs='+')
    ap.add_argument('--out', required=True)
    ap.add_argument('--all', action='store_true', help='sheet every shipped icon, not only the blue ones')
    ap.add_argument('--calibrate', nargs='*', help='print the blue score of these icon files and exit')
    a = ap.parse_args()
    if a.calibrate is not None:
        for p in a.mods + a.calibrate:
            png = to_png(p, os.path.join(a.out, '_calibrate'))
            print(f'{blue_score(png):7.1f}  {p}' if png else f'  (no png) {p}')
        return
    rows = []
    for mod in a.mods:
        name = os.path.basename(os.path.normpath(mod))
        if name.lower() == 'content':  # a repo's Content folder: name it after the repo
            name = os.path.basename(os.path.dirname(os.path.normpath(mod)))
        seen = set()
        for kind, t, s, icon, model, f in definitions(mod):
            path = os.path.join(mod, icon.replace('\\', os.sep))
            if not os.path.isfile(path) or (t, s, kind) in seen:
                continue
            seen.add((t, s, kind))
            png = to_png(path, os.path.join(a.out, '_png', name))
            score = blue_score(png) if png else None
            rows.append(dict(mod=name, kind=kind, type=t, subtype=s, icon=icon, model=model, file=f, png=png or '',
                             score='' if score is None else round(score, 1), blue=score is not None and score > BLUE_LIMIT))
        mine = [r for r in rows if r['mod'] == name and (a.all or r['blue']) and r['png']]
        if mine:
            sheet(mine, os.path.join(a.out, f'{name}_{"all" if a.all else "blue"}.png'), f'{name}: {len(mine)} icons')
        blue = sum(1 for r in rows if r['mod'] == name and r['blue'])
        print(f'{name}: {sum(1 for r in rows if r["mod"] == name)} shipped icons, {blue} blue')
    with open(os.path.join(a.out, 'icons.csv'), 'w', newline='', encoding='utf-8') as fh:
        w = csv.DictWriter(fh, fieldnames=list(rows[0].keys()) if rows else ['mod'])
        w.writeheader()
        w.writerows(rows)


if __name__ == '__main__':
    main()
