#!/usr/bin/env python3
"""Bake the satellite map's edge bands: one texture per safety zone ring and per alliance territory size.

An edge band is a soft band of colour just inside a border, full strength on it and fading to nothing 45 px in (at the
default zoom, 1080p). The game's screen API draws only rectangles, so a band drawn live seams wherever two pieces meet
on a curve; baked, it is one smooth picture per ring, drawn as one sprite over the map and tinted live (zone colour from
the Colors setting, territory colour from the player's standing).

Each texture is white with the band in its alpha, premultiplied (colour = alpha) and its colour sRGB-encoded: the game
samples every texture but normal maps as sRGB and alpha as linear (MyFileTexture, MakeSrgb). It is cropped to where the
band is; a crop that crosses the map's wrap seam is stored rolled, its U range running past 1.

Distance to a ring is measured on the map, not the ground, so a band is the same width on screen everywhere, near the
poles included: f = (angle from the ring's centre) - (the ring's angular radius), and depth = f / |grad f| in texels.

Reads:
  ModScripts/GVK_ZoneNavCommand.cs         planet, zone centres and radii, MAP_LON_SHIFT (as WorldToMapUV uses them)
  <derelicts>/.../GVK-Alliance-Zones.sbc   each alliance's zone centre and starting radius
  <derelicts>/.../GVK-Alliance-Events-ZoneSize.sbc   every radius the Influence ladder sets
Writes:
  Content/Textures/GUI/Bands/*.dds, Content/Data/Material_MapBands.sbc, ModScripts/GVK_MapBands.cs

Usage (from the repo root):
  python tools/kharak_bands.py [--derelicts ../GVK_Derelicts] [--width 2048] [--band-px 45]
Rerun it whenever a zone centre or radius, an alliance zone centre or a ladder size changes.
"""
import argparse
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

import numpy as np
from PIL import Image

TEXCONV = r'C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineersModSDK\Tools\TexturePacking\Tools\texconv.exe'
MAP_SCREEN_PX = 1344.0   # The map at the default zoom at 1080p 16:9: frameW 1.40 of 2 HUD units, times 1920
FADE_POWER = 1.6         # Strength at depth t (0 on the border, 1 at the band's far edge): (1 - t)^1.6
ALLIANCE_DIR = Path('Content/Data/Encounters/Alliance')
TEX_DIR = Path('Content/Textures/GUI/Bands')
SBC_OUT = Path('Content/Data/Material_MapBands.sbc')
CS_OUT = Path('ModScripts/GVK_MapBands.cs')
ZONE_RINGS = (  # (name, centre constant, radius constant, band side): in GVK_ZoneNavCommand's ring order
    ('Zone0', 'CROSSROADS_BEACON', 'ZONE_0_RADIUS', 1),   # Zone 0, inside Crossroads' ring
    ('Zone1', 'Z3_CENTER', 'ZONE_2_RADIUS', -1),          # Zone 1, outside Zone 2's outer edge
    ('Zone3', 'Z3_CENTER', 'ZONE_3_RADIUS', 1),           # Zone 3, inside its ring
)


def read_cs_constants(path):
    text = path.read_text(encoding='utf-8-sig')

    def vec(name):
        m = re.search(name + r'\s*=\s*new Vector3D\(([-\d.]+),\s*([-\d.]+),\s*([-\d.]+)\)', text)
        return np.array([float(m.group(i)) for i in (1, 2, 3)])

    def num(name):
        return float(re.search(name + r'\s*=\s*([-\d.]+)', text).group(1))

    consts = {n: vec(n) for n in ('PLANET_CENTER', 'CROSSROADS_BEACON', 'Z3_CENTER')}
    consts.update({n: num(n) for n in ('PLANET_RADIUS', 'MAP_LON_SHIFT', 'ZONE_0_RADIUS', 'ZONE_2_RADIUS', 'ZONE_3_RADIUS')})
    return consts


def read_alliances(derelicts):
    """{tag: (centre, starting radius)} from the alliance zone profiles."""
    text = (derelicts / ALLIANCE_DIR / 'GVK-Alliance-Zones.sbc').read_text(encoding='utf-8-sig')
    alliances = {}
    for m in re.finditer(r'<SubtypeId>GVK-Alliance-Zone-(\w+)</SubtypeId>(.*?)</Description>', text, re.S):
        c = re.search(r'\[Coordinates:\{X:([-\d.]+) Y:([-\d.]+) Z:([-\d.]+)\}\]', m.group(2))
        r = re.search(r'\[Radius:([\d.]+)\]', m.group(2))
        alliances[m.group(1)] = (np.array([float(c.group(i)) for i in (1, 2, 3)]), float(r.group(1)))
    return alliances


def read_ladder_radii(derelicts):
    text = (derelicts / ALLIANCE_DIR / 'GVK-Alliance-Events-ZoneSize.sbc').read_text(encoding='utf-8-sig')
    return sorted({float(v) for v in re.findall(r'\[ZoneRadiusChangeAmounts:([\d.]+)\]', text)})


def map_directions(width, lon_shift):
    """Unit ground direction at each texel centre, inverting WorldToMapUV."""
    height = width // 2
    u = (np.arange(width) + 0.5) / width
    v = (np.arange(height) + 0.5) / height
    lon = u * 2.0 * np.pi - np.pi + lon_shift * 2.0 * np.pi
    lat = np.pi * 0.5 - v * np.pi
    cos_lat = np.cos(lat)[:, None]
    x = cos_lat * np.cos(lon)[None, :]
    z = cos_lat * np.sin(lon)[None, :]
    y = np.broadcast_to(-np.sin(lat)[:, None], x.shape)
    return np.stack([x, y, z], axis=-1)


def band_alpha(dirs, consts, center, radius, side, band_texels):
    """Band strength per texel (0..1), or None when the sphere misses the ground or covers all of it."""
    to_center = center - consts['PLANET_CENTER']
    d = np.linalg.norm(to_center)
    big_r = consts['PLANET_RADIUS']
    cos_a = (big_r * big_r + d * d - radius * radius) / (2.0 * big_r * d)
    if not -1.0 < cos_a < 1.0:
        return None
    angle = np.arccos(np.clip(dirs @ (to_center / d), -1.0, 1.0))
    f = angle - np.arccos(cos_a)                       # Negative inside the ring
    fx = (np.roll(f, -1, axis=1) - np.roll(f, 1, axis=1)) * 0.5   # The map wraps east-west...
    fy = np.gradient(f, axis=0)                                   # ...but not north-south
    depth = -side * f / np.maximum(np.hypot(fx, fy), 1e-12)      # Texels into the band
    t = np.clip(depth / band_texels, 0.0, 1.0)
    edge = np.clip(depth + 0.5, 0.0, 1.0)                         # Antialiased border
    return np.where(depth <= band_texels, edge * (1.0 - t) ** FADE_POWER, 0.0)


def crop(alpha):
    """(rows, cols, u0, v0, u1, v1) round the band; cols may wrap past the map's right edge (u1 > 1)."""
    width = alpha.shape[1]
    used_rows = np.flatnonzero(alpha.max(axis=1) > 0.0)
    used = alpha.max(axis=0) > 0.0
    r0, r1 = max(used_rows[0] - 2, 0), min(used_rows[-1] + 3, alpha.shape[0])
    if used.all():
        c0, c1 = 0, width
    else:
        # The widest run of empty columns, going round the seam; the crop is everything else
        empty = ~used
        best, best_start, run, start = 0, 0, 0, 0
        for i in range(2 * width):
            if empty[i % width]:
                if run == 0:
                    start = i
                run += 1
                if run > best:
                    best, best_start = run, start
            else:
                run = 0
        best = min(best, width)
        c0 = (best_start + best) % width - 2
        c1 = c0 + (width - best) + 4
        if c1 - c0 >= width:
            c0, c1 = 0, width
    pad = (-(r1 - r0)) % 4                     # BC7 works in 4 x 4 blocks
    if r1 + pad <= alpha.shape[0]:
        r1 += pad
    else:
        r0 -= pad
    c1 += (-(c1 - c0)) % 4
    if c1 - c0 > width:
        c0, c1 = 0, width
    cols = np.arange(c0, c1) % width
    h = alpha.shape[0]
    return np.arange(r0, r1), cols, c0 / width, r0 / h, c1 / width, r1 / h


def save_dds(alpha, path, texconv):
    srgb = np.where(alpha <= 0.0031308, 12.92 * alpha, 1.055 * alpha ** (1 / 2.4) - 0.055)
    img = np.zeros(alpha.shape + (4,), np.uint8)
    img[..., 0] = img[..., 1] = img[..., 2] = np.round(srgb * 255).astype(np.uint8)
    img[..., 3] = np.round(alpha * 255).astype(np.uint8)
    with tempfile.TemporaryDirectory() as tmp:
        png = Path(tmp) / (path.stem + '.png')
        Image.fromarray(img, 'RGBA').save(png)
        subprocess.run([texconv, '-nologo', '-y', '-srgb', '-f', 'BC7_UNORM_SRGB', '-m', '0', '-o', tmp, str(png)],
                       check=True, stdout=subprocess.DEVNULL)
        shutil.copyfile(Path(tmp) / (path.stem + '.DDS'), path)


MATERIAL_TEMPLATE = '''\t\t<TransparentMaterial>
\t\t\t<Id>
\t\t\t\t<TypeId>TransparentMaterialDefinition</TypeId>
\t\t\t\t<SubtypeId>{name}</SubtypeId>
\t\t\t</Id>
\t\t\t<AlphaMistingEnable>false</AlphaMistingEnable>
\t\t\t<CanBeAffectedByOtherLights>false</CanBeAffectedByOtherLights>
\t\t\t<AlphaSaturation>1</AlphaSaturation>
\t\t\t<Emissivity>1</Emissivity>
\t\t\t<IgnoreDepth>false</IgnoreDepth>
\t\t\t<NeedSort>false</NeedSort>
\t\t\t<SoftParticleDistanceScale>0</SoftParticleDistanceScale>
\t\t\t<Texture>Textures\\GUI\\Bands\\{name}.dds</Texture>
\t\t\t<UseAtlas>false</UseAtlas>
\t\t\t<UVOffset>
\t\t\t\t<X>0</X>
\t\t\t\t<Y>0</Y>
\t\t\t</UVOffset>
\t\t\t<UVSize>
\t\t\t\t<X>1</X>
\t\t\t\t<Y>1</Y>
\t\t\t</UVSize>
\t\t</TransparentMaterial>
'''


def cs_band(b):
    c = b['center']
    return (f'new Band("{b["name"]}", {b["u0"]:.6f}, {b["v0"]:.6f}, {b["u1"]:.6f}, {b["v1"]:.6f}, '
            f'new Vector3D({c[0]:.2f}, {c[1]:.2f}, {c[2]:.2f}), {b["radius"]:.1f})')


def write_outputs(zones, territories, args):
    materials = ''.join(MATERIAL_TEMPLATE.format(name=b['name']) for b in zones + territories)
    sbc = ('<?xml version="1.0"?>\n<!-- Generated by tools/kharak_bands.py: do not edit, rerun the script. -->\n'
           '<Definitions xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" '
           'xmlns:xsd="http://www.w3.org/2001/XMLSchema">\n\t<TransparentMaterials>\n' + materials +
           '\t</TransparentMaterials>\n</Definitions>\n')
    SBC_OUT.write_text(sbc.replace('\n', '\r\n'), encoding='utf-8', newline='')
    zone_lines = ',\n'.join('            ' + cs_band(b) for b in zones)
    terr_lines = ',\n'.join('            ' + cs_band(b) for b in territories)
    tags = ', '.join(f'"{b["tag"]}"' for b in territories)
    kms = ', '.join(str(b['km']) for b in territories)
    cs = f'''// Generated by tools/kharak_bands.py (--width {args.width} --band-px {args.band_px:g}): do not edit, rerun the script.
using VRage.Utils;
using VRageMath;

namespace GVK.Navigation
{{
    /// <summary>
    /// The satellite map's edge bands, baked: one texture per safety zone ring and per alliance territory size, each
    /// white with the band in its alpha, drawn over the map and tinted live. A band holds the centre and radius it was
    /// baked for, so a zone or territory that has moved since draws no band rather than a wrong one.
    /// </summary>
    public static class GvkMapBands
    {{
        public struct Band
        {{
            public readonly MyStringId Material;
            public readonly double U0, V0, U1, V1; // Map UV the texture covers; U1 past 1 runs on round the wrap seam
            public readonly Vector3D Center;
            public readonly double Radius;

            public Band(string material, double u0, double v0, double u1, double v1, Vector3D center, double radius)
            {{
                Material = MyStringId.GetOrCompute(material);
                U0 = u0;
                V0 = v0;
                U1 = u1;
                V1 = v1;
                Center = center;
                Radius = radius;
            }}
        }}

        /// <summary>The safety zone rings in GvkZoneNav's order: Zone 0's edge, Zone 2's outer edge, Zone 3's edge.</summary>
        public static readonly Band[] Zones =
        {{
{zone_lines}
        }};

        private static readonly string[] TERRITORY_TAGS = {{ {tags} }};
        private static readonly int[] TERRITORY_KM = {{ {kms} }};
        private static readonly Band[] TERRITORIES =
        {{
{terr_lines}
        }};

        /// <summary>The band baked for an alliance's territory at a radius in whole km, if there is one.</summary>
        public static bool TryGetTerritory(string tag, int km, out Band band)
        {{
            for (int i = 0; i < TERRITORY_TAGS.Length; i++)
            {{
                if (TERRITORY_KM[i] != km || TERRITORY_TAGS[i] != tag) continue;
                band = TERRITORIES[i];
                return true;
            }}
            band = default(Band);
            return false;
        }}
    }}
}}
'''
    CS_OUT.write_text(cs.replace('\n', '\r\n'), encoding='utf-8', newline='')


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    parser.add_argument('--derelicts', type=Path, default=Path('../GVK_Derelicts'))
    parser.add_argument('--width', type=int, default=2048, help='Width of the whole map the bands are baked on, texels')
    parser.add_argument('--band-px', type=float, default=45.0, help='Band width at the default zoom, 1080p, pixels')
    parser.add_argument('--texconv', default=TEXCONV)
    args = parser.parse_args()

    consts = read_cs_constants(Path('ModScripts/GVK_ZoneNavCommand.cs'))
    alliances = read_alliances(args.derelicts)
    radii = read_ladder_radii(args.derelicts)
    band_texels = args.band_px * args.width / MAP_SCREEN_PX
    dirs = map_directions(args.width, consts['MAP_LON_SHIFT'])

    if TEX_DIR.exists():
        for old in TEX_DIR.glob('*.dds'):
            old.unlink()
    TEX_DIR.mkdir(parents=True, exist_ok=True)

    def bake(name, center, radius, side):
        alpha = band_alpha(dirs, consts, center, radius, side, band_texels)
        if alpha is None:
            print(f'{name}: no edge on the ground, skipped')
            return None
        rows, cols, u0, v0, u1, v1 = crop(alpha)
        save_dds(alpha[np.ix_(rows, cols)], TEX_DIR / (name + '.dds'), args.texconv)
        print(f'{name}: {len(cols)} x {len(rows)}, U {u0:.4f}..{u1:.4f}, V {v0:.4f}..{v1:.4f}')
        return {'name': name, 'center': center, 'radius': radius, 'u0': u0, 'v0': v0, 'u1': u1, 'v1': v1}

    zones = [bake('GVK_Band_' + name, consts[c], consts[r], side) for name, c, r, side in ZONE_RINGS]
    if any(b is None for b in zones):
        sys.exit('A safety zone ring has no edge on the ground: GvkMapBands.Zones would be out of order')
    territories = []
    for tag, (center, base_radius) in sorted(alliances.items()):
        for radius in sorted(set(radii) | {base_radius}):
            km = int(round(radius / 1000.0))
            b = bake(f'GVK_Band_{tag}_{km}', center, radius, 1)
            if b:
                b.update(tag=tag, km=km)
                territories.append(b)
    write_outputs(zones, territories, args)
    total = sum(p.stat().st_size for p in TEX_DIR.glob('*.dds'))
    print(f'{len(zones) + len(territories)} textures, {total / 1e6:.1f} MB')


if __name__ == '__main__':
    main()
