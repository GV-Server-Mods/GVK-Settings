# Item model tools

These scripts rebuild the item models GVK_Settings restyles and render their icons. They edit the vanilla-based `.mwm` files directly, with no Blender export, and use only vanilla textures, so the mod ships no texture files for the markings.

| Model | Built with | Icon |
|---|---|---|
| `Content/Models/LithiumBarrel.mwm` (Resource Units) | `mwm_retexture.py`, then `ru_mark.py` | `Content/Textures/Icons/WhiteBarrel.dds` |
| `Content/Models/CopperIngot.mwm` (Construction Units) | `cu_mark.py` | `Content/Textures/Icons/CopperIngot.dds` |
| `Content/Models/Components/AlkalineCell.mwm` | `mwm_retexture.py` | `Content/Textures/GUI/Icons/Components/AlkalineCell.dds` |

## Scripts

- `mwmlib.py`, `arrays.py`: read and write an `.mwm` tag by tag, and pack its vertex arrays the way the game does.
- `mwm_retexture.py`: points one material's textures at other vanilla files.
- `ru_mark.py`: the barrel's R.U. mark and warning slashes. `cu_mark.py`: the pack's bar color and C.U. labels. The layout is in the constants at the top of each.
- `mwm_mesh.py`: turns an `.mwm` into JSON for the renderer.
- `build_render.py`: builds the JSON in a background Blender and renders it with Icon Studio's look.
- `frame_icon.py`: crops the render to a 128 px icon.

## Requirements

- Python 3 with Pillow, Blender 4.5, and texconv from the Space Engineers ModSDK (`Tools\TexturePacking\Tools\texconv.exe`).
- The game's `Content` folder. Set `SE_CONTENT` if it is not in the default Steam folder.
- For icons, SEUT's `Materials.blend` (set `SEUT_MATERIALS` if it is not `C:\SE_Assets\Materials\Materials.blend`) and `mwm_dummies.py` from the se-dev-seut skill (set `SEUT_SCRIPTS` if the skill is not in `~\.agents\skills\se-dev-seut`).

## Rebuild the models

The mark scripts start from the unmarked models, which git keeps in the commit before this folder was added. They refuse a model that already has its mark. Run from the repo root:

```powershell
$o = "$env:TEMP\gvk-models"; New-Item -ItemType Directory -Force $o | Out-Null
$rev = (git log --diff-filter=A --format=%h -- tools/models/cu_mark.py) + '~1'
foreach ($m in 'LithiumBarrel', 'CopperIngot', 'Components/AlkalineCell') { cmd /c "git show ${rev}:Content/Models/$m.mwm > `"$o\$(Split-Path $m -Leaf).mwm`"" }
python tools\models\cu_mark.py "$o\CopperIngot.mwm" Content\Models\CopperIngot.mwm
python tools\models\mwm_retexture.py "$o\LithiumBarrel.mwm" "$o\LithiumBarrel_tex.mwm" 'BlackMetal:ColorMetalTexture=Textures\Models\Cubes\PaintedMetalLighter_cm.dds' 'BlackMetal:NormalGlossTexture=Textures\Models\Cubes\PaintedMetalColorable_ng.dds'
$env:RU_BANDS = 'grey'; python tools\models\ru_mark.py "$o\LithiumBarrel_tex.mwm" Content\Models\LithiumBarrel.mwm
python tools\models\mwm_retexture.py "$o\AlkalineCell.mwm" Content\Models\Components\AlkalineCell.mwm 'Copper:ColorMetalTexture=Textures\Models\Cubes\Aluminium_cm.dds' 'Copper:NormalGlossTexture=Textures\Models\Cubes\AluminiumDull_ng.dds'
```

`git show` goes through `cmd` because PowerShell redirection can change binary output. With the scripts unchanged, this writes the same bytes as the committed models.

## Render an icon

Each icon is rendered from 225 degrees around and 35 up, with the model filling 75 percent of the frame, the way Icon Studio frames its icons. For each item, with `$w` an empty folder:

```powershell
python tools\models\mwm_mesh.py Content\Models\CopperIngot.mwm "$w\CopperIngot.json"
$env:EXPOSURE = '-0.5'   # plus the item's settings from the table below
& 'C:\Program Files\Blender Foundation\Blender 4.5\blender.exe' -b --python tools\models\build_render.py -- $w "$w\render.png" 0 225 35 512
python tools\models\frame_icon.py "$w\render.png" "$w\render.png" "$w\CopperIngot.png" 0.75
& "<ModSDK>\Tools\TexturePacking\Tools\texconv.exe" -nologo -y -f BC7_UNORM -m 1 -o Content\Textures\Icons "$w\CopperIngot.png"
```

Name the PNG after the icon file it replaces, and send texconv's output to that icon's folder.

| Item | Settings | Before rendering |
|---|---|---|
| Barrel | `NO_PAINT='TextAtlas\|Atlas'`, `ALIAS="BlackMetal=PaintedMetal_VeryDark:$w\PaintedMetalLighter_cm.png"` | Convert the game's `Textures\Models\Cubes\PaintedMetalLighter_cm.DDS` into `$w` with texconv `-ft png -f R8G8B8A8_UNORM` |
| C.U. pack | `NO_PAINT='Atlas\|Copper\|Blue'`, `ALIAS='Copper=IngotsAtlas;IngotsAtlasLabel=IngotsAtlas'`, `DECALS='Label\|Blue'` | Rename the label part in the JSON (below) |
| Alkaline Cell | `ALIAS='Copper=Metal_Dull'` | None |

`ALIAS` renders a retextured part with a SEUT library material that has the same textures, since the library has none under the new name. Cycles shades whatever sits behind an alpha-masked quad darker, which the game does not, so the C.U. labels are renamed and drawn as decals:

```powershell
python -c "import json,sys; p=sys.argv[1]; d=json.load(open(p)); [q.update(material='IngotsAtlasLabel') for q in d['MeshParts'] if q['technique']=='ALPHA_MASKED']; json.dump(d,open(p,'w'))" "$w\CopperIngot.json"
```

`build_render.py`'s docstring lists its other settings.
