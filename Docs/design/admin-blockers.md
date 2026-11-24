---
title: GVK admin blockers (replacing SC Blockers)
covers:
  - Content/Data/Cubeblocks/GVK_Blockers.sbc
  - Content/Data/BlockVariantGroups.sbc
  - Content/Data/BlockRestrictions_GVK.sbc
  - ModScripts/GVK_ArmorBalance.cs
checked: -
---

# GVK admin blockers (replacing SC Blockers)

Status: **passed the NPC Dev world test and committed 2026-11-23** (GVK_Settings 543bed8, GVK_Derelicts 1f9b4b1; owner approved the plan the same day; choices under [Decisions](#decisions)). GVK_Settings published 2026-11-23 (tag `workshop-20261123-1744`); GVK_Derelicts not yet (owner). SC Blockers was removed from the NPC Dev world the same day (owner). Next: publish GVK_Derelicts on the owner's "go", and drop SC from the Steam collection. Build log: `Source/Blockers/build-log.md`. Started 2026-11-23, while the full-color icon batch (hub #766, [full-color-icons.md](full-color-icons.md)) was re-rendering SC Blockers' icon. Owner: the blocks "look like crap" and should become GVK's own, with better textures. The icon batch leaves SC Blockers out.

## What SC Blockers is

Workshop 2661300547. Data files only, with no scripts. It has 8 definitions: 7 large-grid `BatteryBlock` slabs and one variant group.

| Subtype | Size (blocks) | Size (m) | Model | Used in GVK prefabs |
| --- | --- | --- | --- | --- |
| `50x50 Square` | 20 x 20 x 4 | 50 x 50 x 10 | `50sq.mwm` | 136 |
| `100x50 Rectangle` | 40 x 20 x 4 | 100 x 50 x 10 | `50x100.mwm` | 48 |
| `200x50 Rectangle` | 80 x 20 x 4 | 200 x 50 x 10 | `50x200.mwm` | 41 |
| `100x100 Square` | 40 x 40 x 4 | 100 x 100 x 10 | `100sq.mwm` | 20 |
| `200x100 Rectangle` | 80 x 40 x 4 | 200 x 100 x 10 | `100x200.mwm` | 3 |
| `200x200 Square` | 80 x 80 x 4 | 200 x 200 x 10 | `200sq.mwm` | 3 |
| `Sphere` | 60 x 60 x 60 | 150 across | `150sphere2.mwm` | 0 |
| `BlockerCubeGroup` (variant group) | | | | |

- **Where they are used.** All 251 placed blockers are in six KOTH prefabs in GVK_Derelicts: Crashed Starship (56), Kalash Site (64), Kalash Site Chunk 1 (3), Kalash Site Chunk 2 (13), Khar Toba (63) and Khar Toba Chunk 1 (52). They are painted, and most carry the `Weldless` armor skin (some `SciFi_Armor`). They are NPC-owned and switched off. Block Restrictions allows them only for NPCs on static grids. Neither local S11 save has any placed.
- **Why they look bad.** Each slab is a plain box (24 vertices) with one vanilla `HeavySquarePlate` armor texture stretched over the whole face. That texture is drawn for one 2.5 m armor block, so on a 200 m slab every detail is 80 times too big and blurry. There are no LODs or bevels.
- **The Sphere is broken.** Its definition points at `150sphere2.mwm`, a 50 x 10 x 50 m box with no material. The mod also ships a real 150 m sphere (`150sphere.mwm`, clay texture) that nothing uses.
- **Smaller bugs.** The rectangles' and the Sphere's mount points run past the block's size. Every shape shares one icon, `Textures\Fake.dds`.
- **Components.** `ADMINKIT` (shared with the King of the Hill mod's Admin Kit) and `BIGCHUNGUS`. SC gives both 99,999,999 integrity and BIGCHUNGUS a mass of 200,000 t, to make the slabs unbreakable and immovable.

## What GVK already changes

GVK_Settings loads last, so its versions win.

- `Components.sbc` redefined `ADMINKIT` and `BIGCHUNGUS` with 1 integrity, 1 kg and the Zone Chip icon, and `Blueprints.sbc` added their missing blueprints. Both were removed on 2026-11-23, since nothing else uses them (owner: "I want those gone"); SC Blockers still defines them until it leaves the mod list.
- `GVK_ArmorBalance.cs` treats every battery as a battery. It sets the blockers' damage taken to 125 % (SC had 0 %) and scales their storage to the block's volume: a 50x50 Square becomes a 4,800 MWh battery, a 200x200 Square 76,800 MWh. They are switched off in the prefabs, so this has had no effect in play.
- So today the slabs can be destroyed: their strength is their 100 steel plates, taking 125 % damage. Whether that is wanted is open choice 4.
- `BlockVariantGroups.sbc` puts the seven shapes in GVK Admin Blocks and disables SC's own group. `GVK_HideModTabs.cs` hides SC's `Blockers` tab. `BlockRestrictions_GVK.sbc` lists all seven.

## Plan

### New block IDs: `TerminalBlock/GVK_Blocker_<size>`

The new blocks live in GVK_Settings as `TerminalBlock/GVK_Blocker_50x50`, `_100x50`, `_100x100`, `_200x50`, `_200x100` and `_200x200` (owner, 2026-11-23). SC made them batteries so stand-alone walls would count as powered and escape grid cleanup. GVK uses them as structural slabs inside the KOTH wrecks, to keep block counts low, and the prefab copies are switched off with no stored power, so the battery did nothing here (owner, 2026-11-23).

- **Why a terminal block at all:** Block Restrictions only handles terminal blocks; it skips plain `CubeBlock` armor (its `Init`). As terminal blocks, they stay hidden from non-admin players' G menu and are removed from player grids, while admins with creative tools see and place them. `TerminalBlock` is the plainest terminal type (vanilla's Control Panel): no power and no updates.
- **Ownership is not the reason:** a block gets an owner only when its components include a Computer (`MyCubeBlock.InitOwnership`, `ContainsComputer`). The blockers have none, as SC's didn't; their KOTH grids are NPC-owned through their other blocks.
- **Migration:** a block whose ID disappears is deleted from every grid on load. The 251 blockers in the six KOTH prefabs (GVK_Derelicts) were rewritten to the new IDs, with their battery-only fields dropped. KOTH grids already spawned in the live world still hold SC's battery blockers and lose them when SC is removed, so they have to be respawned then (Switching over, step 3).
- The Sphere is dropped: nothing in GVK places it.

While SC Blockers is still in the mod list, both sets of blocks exist side by side, and the KOTH prefabs use only the new ones. That lets us test the new blocks in the NPC Dev world before SC is removed (wiki runbook step E27).

### Models

Rebuilt in Blender with SEUT, source file `Source/Blockers/GVK_Blockers.blend` (one scene per shape, `GVK_Blocker_<size>`), exported to `Content/Models/Cubes/Large/Blockers/`. Per shape:

- A plain box of exactly SC's size: no bevels (owner, 2026-11-23). Checked against SC's models: same extents to the metre.
- The vanilla armor material `HeavySquarePlate`, tiled so one plate pattern covers 5 m (twice vanilla heavy armor's 2.5 m), instead of one plate per slab. Vanilla's plate model stores 0.25 x 0.5 of the 4 x 2 texture sheet per 2.5 m plate, but its `PatternScale` tag of 4 divides that at load, so a vanilla plate really shows 0.0625 x 0.125. The slabs use 0.0125 UV per metre across and 0.025 up; plate seams land on the slab edges. The first export used the raw span and showed plates half vanilla size (owner, 2026-11-23); fixed the same day. Keeping a vanilla armor material name keeps paint and armor skins working, so the prefabs' `Weldless` and `SciFi_Armor` colors stay.
- A box collision, as now.
- No decals, no build stages and no LODs (owner, 2026-11-23): each slab is 12 triangles, so a LOD would be the same box, and nobody welds them up.

The mount points are fixed to each shape's real size.

**Known issue: repainting hides a slab.** A color or skin change makes the slab vanish while its collision stays, and SC's own blockers do the same, so it is the game's handling of very large blocks, not these models (owner, 2026-11-23). `SimpleUpdateVisualOnSkinChange` was tried and did not help; removed. Cause not found yet. Paint slabs in the prefab, not in game.

### Icons

Full color, rendered in Blender with `tools/models/build_render.py` from the new models, the way hub #766 renders the icons Icon Studio cannot do: Icon Studio's camera (yaw 225, pitch 35), grey paint, framed to 75 % by `frame_icon.py`. One icon per shape, `Content/Textures/GUI/Icons/Cubes/GVK_Blocker_<size>.dds`, 128 px, premultiplied alpha, BC7_UNORM_SRGB with full mips.

Each icon has its size baked in at the top left (Bahnschrift Bold, 26 px, white with a dark outline), and the GV logo overlay (`Textures\Icons\GV_Icon_Overlay.dds`) as a second `<Icon>` layer, as on the other GVK admin blocks (owner, 2026-11-23: "size text and also the GV overlay", then "a little larger"). Render settings: `SEUT_MATERIALS=C:\SE_Assets\Materials\Armors.blend` (where SEUT keeps `HeavySquarePlate`), `EXPOSURE=-0.5`.

### Definitions

A new `Content/Data/Cubeblocks/GVK_Blockers.sbc` in GVK_Settings, with SC's six slab sizes. Components: 50,000 girders and 100,000 steel plates (two entries of 50,000: the game reads each count as a 16-bit number, at most 65,535, and one entry of 100,000 made the whole of GVK_Settings fail to load on 2026-11-23), in place of SC's `ADMINKIT` and `BIGCHUNGUS` (owner, 2026-11-23); no Computer, since nothing needs the slabs to have an owner. That is about 2,300 t per slab, which a static grid ignores. Grinding them cannot pay out: every KOTH grid spawns non-editable (`GridsAreEditable:false`), and grinders skip non-editable grids (`MyAngleGrinder`, `MyShipGrinder`). A slab an admin places on an editable grid can be ground for its full components. Other fixes: new models and icons, mount points matched to size, airtight, and display names that say what they are (for example `Blocker 50x50 m`).

They are unbreakable again, as SC meant: 0 % damage taken, no deformation. `GVK_ArmorBalance.cs` skips the six subtypes, so none of its balance changes apply to them.

`BlockRestrictions_GVK.sbc` gives the new IDs the same rules SC's had: NPC static grids only, not for players or unowned grids. The world configs have no entries for the new IDs yet, so these defaults apply on the next load. SC's entries stay until SC is removed. GVK Admin Blocks lists the six new blocks in place of SC's.

### Switching over

1. Build GVK_Settings and GVK_Derelicts (done 2026-11-23). Test in the NPC Dev world: spawn the six KOTH prefabs and check the blockers are there, with their paint, skins and collision; check the icons in GVK Admin Blocks; check a non-admin player can't see them and an admin with creative tools can place them.
2. Remove SC Blockers (2661300547) from the NPC Dev world's mod list (both `Sandbox.sbc` and `Sandbox_config.sbc`), reload, spawn the prefabs again and check nothing is missing.
3. Publish GVK_Settings and GVK_Derelicts together (owner's "go"), remove SC Blockers from the S11 world and the Steam collection, and respawn the live KOTH grids, which still hold SC's blockers.
4. Clean up: drop the disabled `BlockerCubeGroup` override and SC's seven Block Restrictions entries, drop the `ADMINKIT` and `BIGCHUNGUS` rows from CrunchEcon's `Prices.json` in gvk-server-config, update the wiki mod list and the Discord bot's mod list, and move SC Blockers out of the hub #766 icon table.

## Decisions

Owner, 2026-11-23:

1. **Where the blocks live:** GVK_Settings.
2. **Texture scale:** 5 m panels.
3. **The Sphere:** dropped.
4. **Damage:** unbreakable and inert again.
5. **Icons:** one per shape, with the size baked in and the GV overlay.
6. **Shape:** plain boxes, no bevels; blockout signed off after checking SC's sizes.
7. **Decals, build stages, LODs:** none.
8. **Block type:** a plain `TerminalBlock` with new subtypes instead of SC's batteries, with the KOTH prefabs migrated.
