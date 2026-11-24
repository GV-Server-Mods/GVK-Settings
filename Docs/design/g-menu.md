---
title: G menu layout across the GVK mods
covers:
  - Content/Data/BlockCategories.sbc
  - Content/Data/BlockVariantGroups.sbc
  - Content/Data/BlockPositions.sbc
  - ModScripts/GVK_HideModTabs.cs
scripts:
  - ModScripts/GVK_HideModTabs.cs: Hides other mods' G menu tabs whose blocks the GVK tabs list
checked: 2026-11-22
---

# G menu layout across the GVK mods

Status: **applied, built and deployed 2026-11-22** (owner: "Go"); in-game check is runbook E20. GVK_Suspension (runbook E18) and GVK_Weapons (E19, its own `docs/design/g-menu.md`) are done and passed in game. This doc covers every other GVK mod and the Workshop blocks GVK_Settings places for other mods.

## Rules (HARD, decompiled code)

- **Home** draws every public block pair; a pair in a variant group draws only if it is the group's primary. `GuiVisible` is not checked.
- **Primary** = the first listed block, unless a later pair has both a Large and a Small block, which moves to the front. If the first two listed blocks are the Small and the Large of one pair, the Small stays primary and the group never draws on Home (the Large is what Home checks).
- **Position** is keyed by the primary's `BlockPairName` (a block without one uses its `DisplayName`). No position means the first empty slot from row 0. Any mod can add or move a position; the last loaded wins for the same name. Past column 8 a row wraps.
- **Category tabs** draw every listed pair as its own icon. List one block per group, `StrictSearch` true.
- A variant group that names a block from a mod that isn't loaded just drops that block.
- **Load order:** definitions load in the world's `Sandbox_config.sbc` mod list order, top first, so the **last** entry wins a group or position both mods define (`MyDefinitionManager.LoadData`). GVK_Settings must be the last mod in the list. The production S11 world has it last; the NPC Dev test world had it 9th until 2026-11-23, which left Keen's bulk cargos, cargo terminals and bulk hydrogen tank, SC Blockers' group and the Extra Reinforced Conveyors ducts loose on Home (owner's screenshot, 2026-11-23).

## Owner choices (2026-11-22)

- Staff and NPC blocks: **one admin group**, also in the GV Kharak Utility tab.
- Non-GVK Workshop blocks loose on Home: **place them** from GVK_Settings (Tank Tracks Builder, GV Cargo Scan, Extra Reinforced Conveyors' duct group, AQD's 5x1 conveyor); **GV Cargo Scan also goes in the Utility tab**.

## Home positions

Each mod sets positions only for its own blocks; GVK_Settings sets the admin group and the four Workshop mods.

| Row (Keen's) | X | Icon | Pair key | Set by |
| --- | --- | --- | --- | --- |
| 5 structure | 4 | Runway Middle Wide (single) | `RunwayMiddleSectionWide` | GVK_Settings |
| 15 thrusters | 5 | Hover Engines (group) | `SmallHoverEngine` | GVK_HoverEngines |
| 16 suspension | 13 | Tank Track Builder (single) | `TankTrackBuilder` | GVK_Settings |
| 18 production | 3 | Ship Tools (GVK drills, welders, grinders; replaces Keen's grinder spot) | `GVK_Drill` | GVK_ToolCore_Tools |
| 18 | 4 | Salvage Beam Turret (single) | `GVK_SalvageBeamTurret` | GVK_ToolCore_Tools |
| 18 | 5 | Static Drills (group) | `Static Drill` | GVK_ResourceNodes |
| 18 | 6 | Keen's Projector (moved from 4) | `Projector` | GV_MnM |
| 18 | 7 | MnM projector (single) | `MnM` | GV_MnM |
| 20 beacons | 5 | Drill Blocker (single) | `DrillBlocker` | GV_DrillBlocker |
| 20 | 6 | Cargo Scanner (single) | `Cargo Scanner` | GVK_Settings |
| 20 | 7 | GVK Admin Blocks (group) | `LimitedProdZone` | GVK_Settings |
| 21 cargo | 6 | Shared cargo containers (group) | `SmallCargoContainer_Shared` | GVK_Settings |
| 21 | 7 | Pipeline cargo (single) | `Pipeline_Cargo` | GVK_PipelinesNoPhysics |
| 22 conveyors | 6 | Reinforced ducts (GVK_Settings' copy of the group, with Extra Reinforced Conveyors' four ducts) | `ConveyorTubeDuct` | GVK_Settings |
| 22 | 7 | AQD 5x1 conveyor (single) | `AQD_ConveyorStraight5x1` | GVK_Settings |

The Medium Hydrogen Tank (`RB_HydrogenTankMedium_Small`, small grid only) joins Keen's tank group instead of getting its own icon. GVK_Settings redefines `TankGroup` as Keen's list plus that tank, the way GVK_Suspension added the 7x7 to Keen's suspension groups. The Water Tank mod (3633905517) also redefines `TankGroup` (adding `WaterTank`, dropping the bulk tanks) and the last loaded wins, so the GVK copy keeps `WaterTank` too, and the tank also gets a fallback Home spot (row 23, X 5) in case Water Tank loads last.

## Per mod

### GVK_Settings

- **New group `GVK_AdminBlocks`, "GVK Admin Blocks"**: Limited Prod Zone, NoLargeGridZone Beacon, NoThrusterZone Beacon, CU Refinery (`LargeRefinery_NPC_CU`) and SafeZoneNPC (`SafeZoneBlockNPC`, from the siegable safe zones mod 3605260551). Limited Prod Zone is first, so it is the icon.
- **`BeaconGroup`, "Core Beacons"**: keeps Keen's Id so Keen's beacon slot shows the GVK core beacons; its icon is the upright Light core (`Core_Light.dds`), not Keen's beacon (owner, 2026-11-23).
- **`TankGroup`**: Keen's list plus the Medium Hydrogen Tank.
- **New `BlockPositions.sbc`** with the GVK_Settings rows in the table.
- **GV Kharak Items (`GVK`)**: add the admin group; drop the duplicate second size of MnM and the Cargo Scanner (same pair, one icon either way). The two prototech blocks (Prototech Reactor, Prototech Oxygen Generator) stay as two single icons: they are members of Keen's 10-block Prototech group, so listing the group's head would show the jump drive instead.
- **Utility (`GVK_Utility`)**: add the admin group and the Cargo Scanner.
- **Keen's tabs** (Large Blocks, Small Blocks, Nonarmor, Utility, Logistics): trim to one block per group. The 8 beacon variants become the one Beacon group head, the 3 or 4 shared cargo sizes become the shared cargo head, and the reinforced ducts become the duct group head.

### GVK_HoverEngines

- **Bug fixed on the way:** the group lists the small-grid engine first, so its Large twin is never the primary and the Hover Engines icon is missing from Home today. List `SmallAtmosphericHoverEngine_LargeBlock` first.
- New `BlockPositions.sbc` (thruster row, X 5).
- `GVK` and `GVK_Propulsion`: the group head only, plus the Tank Track Builder (already added to Propulsion this session); the shared `GVK` category's `StrictSearch` set to true, matching every other GVK mod.
- Keen's Large and Small tabs: the group head only.

### GVK_ToolCore_Tools

- New `BlockPositions.sbc` (production row, X 3 and 4).
- Keen's Large Blocks, Small Blocks and Ship Tools tabs: the Ship Tools group head and the Salvage Beam Turret only. The cockpit toolbar `ShipTools` category is left alone (not a G menu tab).

### GVK_ResourceNodes

- New `BlockPositions.sbc` (production row, X 5).
- `GVK` and `GVK_Production`: the group head only; `GVK` `StrictSearch` true.

### GV_MnM

- New `BlockPositions.sbc` (Keen's Projector to X 6, MnM X 7).
- Its own `GV_` tab and Keen's tabs: one MnM entry each.

### GV_DrillBlocker and GVK_PipelinesNoPhysics

- New `BlockPositions.sbc` each (beacon row X 5; cargo row X 7).
- `GVK` and `GVK_Utility` `StrictSearch` true; Pipelines' duplicate entry removed.

### Deliberate single icons in the GVK tabs

These are members of non-GVK groups, listed on their own so the GVK tab shows the GVK block rather than another group's head: Prototech Reactor and Prototech Oxygen Generator (Keen's Prototech group), the Medium Hydrogen Tank (tank group) and GVK_Suspension's 7x7 pair.

### No change

GVK_Derelicts, GVK_ShipCoreFramework, GVK_SiegeableSafeZones_NPC (its block joins the admin group from GVK_Settings), GVK_Zebedee, GVK_TreeNoPhysics, GV_FactionHangar, GVK_Pertam_Planet: no public loose blocks.

## Round 2: the other Workshop mods

Applied, built and deployed 2026-11-22 (owner: "Go"); in-game check is runbook E20 (f) to (h). The Tank Track Builder's Propulsion and GV Kharak Items entries moved from GVK_HoverEngines to GVK_Settings the same day, since the test world loads GVK_HoverEngines from the Workshop and the owner saw no Tank Track Builder there.

The owner asked for these too (2026-11-22: "yeah we need to do these too"). All set from GVK_Settings; a mod that isn't loaded leaves an unused position or a dropped group member.

**Staff blocks into GVK Admin Blocks** (owner, 2026-11-22): KOTH's `ZoneBlock` and `Prizebox` (Golden Cargo Container), and the six GVK admin blockers (`GVK_Blocker_<size>`, `Content/Data/Cubeblocks/GVK_Blockers.sbc`), which replaced SC Blockers' six shapes and its `Sphere` on 2026-11-23 (plan: `Docs/design/admin-blockers.md`). SC Blockers' own `BlockerCubeGroup` stays disabled from GVK_Settings, which relies on GVK_Settings loading after SC Blockers; if not, the SC group shows as its own icon.

**New groups** (other mods' blocks, GVK-defined):
- `GVK_SpawnPoints`, "Spawn Points": Spawn_Point_IR's large and small spawn point. Medical row (17), X 5.
- `GVK_LongHangarDoors`, "Long Hangar Doors": Longer Hangar Doors' x3, x4 and x5. Door row (11), X 7.

**Positions:**

| Row | X | Icon | Pair key |
| --- | --- | --- | --- |
| 1 armor | 7 | MA Buster group | `MA_Buster_ArmorBlock` |
| 2 (empty in Keen's) | 0 to 3 | P.O.M XL: XL Block, XL Half Block, XL 1x, XL Passage Corner groups | `XL_Block`, `XL_HalfBlock`, `XL_1x`, `XL_PassageCorner` |
| 5 structure | 5 | MA 15m Pole | `MA_Pole` |
| 14 power | 5 | MA Stackable Wind Turbine | `MA_Windmill` |
| 19 lights | 6 | MA Lantern | `MA_Lantern` |
| 25 control | 5 | Build Info's Multi Tool | `MultiTool` |

**Keen's conveyors back on the conveyor row:** AQD Conveyor Expansion moves Keen's conveyor (`Conveyor`) and conveyor tube (`ConveyorTube`) to the cockpit row (13). GVK_Settings puts them back at row 22, X 0 and 1. Same load-order caveat.

**Conveyor groups** (2026-11-23): AQD and Extra Reinforced Conveyors both redefine `ConveyorThickGroup`, and AQD loads later, so AQD's Endcap, Junction Tubes and Conveyor X sit in Keen's conveyor tube icon, and Extra Reinforced's four ducts (4-way, elbow, 4-way alt, 5-way) would be loose. GVK_Settings redefines `ConveyorReinforcedGroup` as AQD's copy plus those four ducts (owner, 2026-11-23: "Into reinforced ducts"). The GVK tabs list the conveyor tube and reinforced duct group heads instead of the seven blocks.

## Round 3: other mods' blocks in the GVK tabs, their own tabs hidden (applied 2026-11-22; runbook E22 passed in game the same day)

Owner, 2026-11-22: "include most of these modded blocks in the GVK Items tabs too, and then even better if we can hide their modded tabs that dont match up".

**Hiding a tab (HARD):** the G menu lists a tab only when its category's `IsBlockCategory` is true (`MyGuiScreenToolbarConfigBase.cs:1107`). Tabs with one `Name` merge and the first loaded keeps its flags (`MyDefinitionManager.cs:466-478`), so a data file can't switch another mod's tab off. The GVK_Settings session script `ModScripts/GVK_HideModTabs.cs` sets `IsBlockCategory` false at `LoadData` for the tabs below, the way `NoMinPrice.cs` edits definitions. The blocks stay searchable and keep their Home spots.

| Tab hidden | Name | From |
| --- | --- | --- |
| MA | `AutoMcD_Astronautical` | MA Stackable Wind Turbine, MA Buster Blocks |
| Spawn_Point_IR | `Spawn_Point_IR` | Spawn_Point_IR |
| Blockers | `Blockers` | SC Blockers |
| GV Items | `GV_` | GV Cargo Scan, GV_MnM |
| P.O.M - XL Blocks | `XLBlocks` | P.O.M XL Blocks |

**New sub-tab `GVK_Structure`, "   Structure"** (owner, 2026-11-22), group heads only: P.O.M XL's four groups, MA Buster, Long Hangar Doors, MA 15m Pole, MA Lantern and GVK's Runway Middle Wide.

**Utility gets:** Spawn Points, MA Stackable Wind Turbine, Build Info's Multi Tool, the MnM projector, the reinforced ducts group, AQD's 5x1 and the conveyor tube group (with AQD's Endcap, Junction Tubes and Conveyor X; changed 2026-11-23). The admin group (with KOTH and the GVK admin blockers) and the Cargo Scanner are already there.

**GV Kharak Items gets** everything above, one block per group.

## Check in game

One runbook entry in gvk-wiki `wiki/seasons/season-11-test-runbook.md` once built. The test world loads GVK_HoverEngines, GVK_ResourceNodes, GV_DrillBlocker and GV_MnM from the Workshop, not the local builds, and does not load GVK_PipelinesNoPhysics (2840967825) at all, so their part needs the world switched to the local copies (or a Workshop upload) first.
