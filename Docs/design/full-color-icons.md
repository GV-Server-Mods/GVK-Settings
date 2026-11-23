---
title: Full-color icons for the remaining GVK and Workshop mods
covers:
  - Content/Data/ModAdjuster/ModAdjusterFiles.txt
  - Content/Data/ModAdjuster/CubeBlocks/CubeBlocks_ExtraReinforcedConveyors.xml
  - Content/Data/ModAdjuster/CubeBlocks/CubeBlocks_Thrusters.xml
  - tools/icon_inventory.py
  - tools/models/mwm_upgrade.py
  - tools/models/build_render.py
checked: 2026-11-23
---

# Full-color icons for the remaining GVK and Workshop mods

Status: **in progress** (owner, 2026-11-23: "Go"). Tracked on hub #766. Method decided 2026-11-18: Keen's Icon Studio, with Blender only for the icons it gets wrong (gvk-wiki `wiki/decisions/2026-11-18-icons-from-icon-studio.md`). Owner, 2026-11-23: "I want to use the icon studio as much as I can", and all three groups below.

Already done: GVK_Weapons (5d2e2ad), GVK_Suspension (SEUT renders, Icon Studio can't put a wheel on a suspension), and GVK_Settings' core beacons, scrap beacons, hydrogen tank and runway (00c7987).

## Scope

Blue-icon counts are from the hub #766 inventory (2026-11-20); step 1 recounts them.

| Group | Mod | Blue icons | How it ships |
| --- | --- | --- | --- |
| GVK mods | GVK_HoverEngines | 8 | Overwrite the files in the repo, keeping each name and format; Workshop upload later |
| | GVK_ResourceNodes | 3 | same |
| | GVK_PipelinesNoPhysics | 1 | same |
| | GV_DrillBlocker | 1 | same |
| | GVK_ToolCore_Tools | 1 | same |
| | GV_MnM | 1 | same |
| | GVK_Settings | what step 1 finds left of its 18 | same |
| Home-tab Workshop mods | P.O.M XL Blocks (2943729620) | 26 | Icons in GVK_Settings, ModAdjuster `<Icon>` overrides |
| | MA Buster Blocks (2449073590) | 6 | same |
| | AQD Conveyor Expansion (1864380341) | 4 | same |
| | SC Blockers (2661300547) | 1 | same |
| | Water Tank (3633905517) | 1 | same |
| | Build Info (514062285) | 1 | same |
| | Extra Reinforced Conveyors (2800974378) | 0 | Its own icons are full color; GVK_Settings overrides them with blue ones. Drop the four `<Icon>` lines in `CubeBlocks_ExtraReinforcedConveyors.xml` |
| Other Workshop mods | King of the Hill | 2 | ModAdjuster. The inventory names 2388326362; the S11 world loads 2863919629, so step 1 checks which |
| | Cowboy Hats (2233651927) | 3 | same |
| | AQD Upgradeable Gyroscopes (2621169600) | 1 | same |
| | HydrogenThing (2493525535) | 1 (item) | same, item override |
| | Modular Encounters Systems | 1 | same |
| Vanilla redo | Keen's base ion and atmospheric thrusters, large and small, both grids (8 blocks) | 6 files: `thrust`, `thrust_large`, `AtmosphericThrusterLargeLarge`, `AtmosphericThrusterSmall`, `SmallAtmosphericThrusterLargeLarge`, `SmallAtmosphericThrusterSmall`. Full color already (blue score about -1), but poor renders (owner, 2026-11-23: "redo some of the vanilla ones that look like hot garbage, mainly the vanilla base ion engines and atmospheric engines") | Icons in GVK_Settings `Content/Textures/GUI/Icons/Vanilla/`, `<Icon>` lines on those 8 subtypes in the existing ModAdjuster `CubeBlocks_Thrusters.xml`. A game update that redraws them won't show until those lines go |

**Recount, 2026-11-23** (`tools/icon_inventory.py`, blue icon files per mod; several blocks can share one file): GVK_HoverEngines 8 (33 definitions; every icon has "1x1/3x3 HOVER ARMORED" text baked in), GVK_ResourceNodes 3, GVK_PipelinesNoPhysics 1, GV_DrillBlocker 1, GVK_ToolCore_Tools 1, GV_MnM 1, GVK_Settings 2, MA Buster 6, AQD Conveyor Expansion 5, SC Blockers 1 (shared by its 8 shapes), Water Tank 1, Build Info 1, King of the Hill 2 (the S11 and NPC Dev worlds load 2863919629), Cowboy Hats 3, AQD Gyroscopes 2, HydrogenThing 1, MES 1. **P.O.M XL: 0**, its icons are already full color, so it drops out. Extra Reinforced Conveyors' own icons: 0 blue, confirmed.

**Icon Studio crashes on old models (2026-11-23):** it stopped with a memory error on GVK_HoverEngines' `SmallAtmosphericHoverEngine_SmallBlockV2` (owner's screenshot), after rendering only the four 3x3 icons whose large-grid models are in the current `.mwm` format (`01157001`). The other 16 hover models are `01097001`, which keeps 7 material numbers per mesh part that the current format dropped; read as the current format, a part's index count comes out in the millions. `tools/models/mwm_upgrade.py` copies a mod to a scratch folder and rewrites its old models in the current format (same vertices, parts and materials, checked with `mwm_mesh.py`). Icon Studio loads that copy; the mod itself is never changed. On the converted copy it saved two more renders and then froze the owner's PC (owner, 2026-11-23: "this tool seems extremely unstable"), so mods with old models go to Blender instead.

**GVK_HoverEngines pilot, 2026-11-23: all 8 icons in Blender, engines lit.** Icon Studio's six renders passed review, and so did Blender's two 1x1 Mil icons, matched to Icon Studio's camera, fill and look (owner: "All pass"). The owner then asked for the engines' green glow ("just to add some more color"), which Icon Studio can't show, so all 8 were re-rendered in Blender with `tools/models/build_render.py` and passed (owner: "all pass", after the 1x1 Civ was turned to face left like the others). No text labels (owner: "No text"). Settings:
- Glow: only `emissive10`, the material the mod's script lights, in its default green at its emissivity (`EMISSIVE_TINT='0,1,0,5'`, `EMISSIVE='^emissive10'`). The script gives `emissive11` dark red at 1, which reads as grey, so it stays unlit.
- Chrome: `SHINY_ROUGH=0.3`, `REFLECT=<Blender>\datafiles\studiolights\world\studio.exr`, `REFLECT_STRENGTH=0.6`, so the chrome reflects like Icon Studio's (owner asked for its reflections).
- Camera: yaw 225, pitch 35, 75% fill. Materials: each mod material linked to the SEUT material with its textures (`ALIAS`, some by texture name).

## Steps

1. **Recount** (me). A new GVK_Settings `tools/icon_inventory.py` lists, for a mod folder, each block, item and variant group whose `<Icon>` file the mod ships, converts it with SEUT's texconv into a scratch folder, and flags the blue ones (blue channel well above red and green over the icon's opaque pixels, checked first on one known blue and one known full-color icon). It writes a CSV and an old-icon contact sheet per mod.
2. **Flat models** (me): run `tools/icon_tilt.py` on the mod folder, so Icon Studio gives plates and slabs a steeper camera.
3. **Render** (owner): Icon Studio, Mod Directory = the deployed mod (`%APPDATA%\SpaceEngineers\Mods\<mod>`) for GVK mods, or the Workshop folder (`steamapps\workshop\content\244850\<id>`) for the others; Load Mod, batch render. Tell me when it's done.
4. **Collect and check** (me): copy the renders out of `%APPDATA%\Godot\app_userdata\SE Icon Studio\temp\renders` before the next launch, keep the ones step 1 flagged, check framing (128 px, about 75% fill, nothing clipped), and make an old-versus-new contact sheet.
5. **Review** (owner): pass or reject each icon on the sheet. A reject gets a second Icon Studio try first; Blender only if Icon Studio can't do it.
6. **Ship** (me):
   - GVK mods: replace each icon file in place (DDS as BC7 sRGB with SEUT's texconv preset, PNG stays PNG), so no SBC changes.
   - Workshop mods: icons into GVK_Settings `Content/Textures/GUI/Icons/<Mod>/`, `<Icon>` overrides in `Content/Data/ModAdjuster/CubeBlocks/CubeBlocks_<Mod>.xml` (and a variant group file where a mod's group has its own icon), each listed in `ModAdjusterFiles.txt`. ModAdjuster puts GVK_Settings' folder in front of the path.
   - Build, check the deployed copy has the new files, one runbook entry per batch.
7. **In game** (owner): reload the NPC Dev world and look over the G menu.

**Order:** pilot GVK_HoverEngines (8 icons, local copy, the test world loads it) through every step, then the other GVK mods in one batch, then P.O.M XL, then the rest of the Workshop mods in one batch, then the vanilla thrusters (Icon Studio has to be pointed at Keen's thruster definitions; step 3 for that batch is settled when we get there). Nothing is committed or uploaded until the owner says so.
