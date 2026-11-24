# GVK admin blockers: build log

Plan: [Docs/design/admin-blockers.md](../../Docs/design/admin-blockers.md). Source: `GVK_Blockers.blend`, one SEUT scene per shape (`GVK_Blocker_<size>`), exported to `Content/Models/Cubes/Large/Blockers/`.

## 2026-11-23

- Stage: design doc approved; blockout signed off (owner checked the sizes match SC's: they do, to the metre, in both the block Size and the model extents).
- Shape: plain boxes, 10 m thick, no bevels (owner: "no beveled edges"). `HeavySquarePlate` at 5 m panels (0.05 UV/m across, 0.1 UV/m up). Box collision, one object per shape.
- Decal pass: none (owner picked "No decals" from a contact sheet of the Atlas1 and Atlas2 stripe decals).
- Build stages and LODs: none (owner).
- Freeze: done. The .blend is the source of truth; the scenes were built by one script and are now edited in place only.
- Pre-export checklist: saved, ASCII subtypes, real UVs (no vertex at UV 0,0), one collision box inside the bounding box, no modifiers, export folder under `Models\`. Six exports, no SEUT errors; Havok's "extra radius is 0" warning is harmless.
- Icons: Blender renders (Icon Studio camera, grey paint, 75 % fill), size text baked top left (Bahnschrift Bold 26 px), GV overlay as a second icon layer; signed off on the third round.
- Block type: `TerminalBlock/GVK_Blocker_<size>` instead of SC's `BatteryBlock` (owner). The battery did nothing in GVK; Block Restrictions needs a terminal block. The 251 blockers in the six KOTH prefabs (GVK_Derelicts) were rewritten to the new IDs; Block Restrictions defaults and GVK Admin Blocks point at the new IDs.
- Built and deployed GVK_Settings and GVK_Derelicts (0 errors; GVK_Derelicts `Test-Docs.ps1` OK). Not committed. Not yet tested in game.

- In game (owner): plates half vanilla size, and a repaint made the slab vanish with its collision still there. Cause of the scale: vanilla armor plate models carry `PatternScale` 4, which divides their UVs at load; the slabs had copied the raw span. UVs scaled to a quarter (0.0125 UV/m across, 0.025 up) and all six re-exported; checkpoint `GVK_Blockers_before_uvscale.blend`. Repaint: `SimpleUpdateVisualOnSkinChange` added to the six definitions, did not help (a plain color change hides it too), removed. SC's blockers vanish the same way, so it is not this build (owner). Rebuilt and deployed.

- E27 round 2 passed (owner). Components changed to 50,000 girders and 100,000 steel plates, dropping `ADMINKIT` and `BIGCHUNGUS` (owner). Rebuilt and deployed.
- `ADMINKIT` and `BIGCHUNGUS` removed from GVK_Settings' `Components.sbc` and `Blueprints.sbc` (owner: "I want those gone"); nothing in the GVK repos or other loaded mods uses them. SC Blockers is not in the NPC Dev world's saved mod list, and no mod in that list requires it on Steam. Rebuilt and deployed.

## Next

Switching-over step 1 in the plan: the NPC Dev world test with SC Blockers still loaded.
