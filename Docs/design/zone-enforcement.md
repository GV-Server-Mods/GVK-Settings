---
title: Zone enforcement scripts
covers:
  - ModScripts/GVK_NoPVPZone.cs
  - ModScripts/LimitedProdZone_*.cs
  - ModScripts/NoLargeGridZone_*.cs
  - ModScripts/NoThrusterZone_*.cs
  - ModScripts/KOTHNoSafezone_*.cs
scripts:
  - ModScripts/GVK_NoPVPZone.cs: Damage rules by zone: no PvP in the starter zones, fall and self damage everywhere
  - ModScripts/LimitedProdZone_*.cs: Switch off heavy production, ship drills and military weapons near the Crossroads, by zone
  - ModScripts/NoLargeGridZone_*.cs: Switch off large grid power near small-grid-only KOTH sites
  - ModScripts/NoThrusterZone_*.cs: Switch off non-NPC thrusters near rover-only KOTH sites
  - ModScripts/KOTHNoSafezone_*.cs: Switch off player safezone generators and projectors near KOTH sites
---

# Zone enforcement scripts

## GVK_NoPVPZone.cs
- **Damage Interception Hook**: Hooks `MyAPIGateway.Session.DamageSystem.RegisterBeforeDamageHandler(0, DamageHandler)` server-side in `BeforeStart()` (DamageSystem is not guaranteed ready during `Init()`). All handler state is method-local — damage handlers fire inline from multiple simulation/physics threads, so shared instance fields would be a race condition. Keen's `IMyDamageSystem` has no Unregister API (verified vs. game 1.208), so handler lifetime equals session lifetime by necessity.
- **Block damage (Zone 0, 0 – 20km)**: All block damage zeroed (`info.Amount = 0f`) — total starter hub immunity for player grids.
- **Block damage (Zone 1, 20 – 35km)**:
  - Unowned blocks (`BigOwners.Count == 0`) and NPC-faction grids (`IsEveryoneNpc()` or tags `GAALSIEN`, `DERELICT`, `SPRT`, `KHAANEPH`) take weapon/drill/grinder damage. Friendly NPC structures (COALITION starter hub, Crossroads Tower) are protected by **MES invulnerability flags** in GVK_Derelicts — cross-system dependency.
  - NPC attackers can damage player grids; self-damage and same-faction friendly fire allowed; cross-faction PvP damage zeroed.
- **Character damage matrix (weapon, kinetic AND grinder damage vs. player bodies)**:
  - **Fall / collision / environment damage (no attacker entity): allowed in ALL zones.** The desert is the tutorial — no false security in lower zones.
  - **Self-inflicted damage (own ship's grinders/turrets/rams, own hand tools): allowed in ALL zones** so players don't develop bad habits that bite them in the deep desert.
  - Same-faction friendly damage: allowed (Keen's friendly-fire toggle governs the rest).
  - Zone 0: NPC and enemy-player damage to characters zeroed. Zone 1: NPC attackers allowed, enemy players zeroed. Beyond 35km: vanilla (full PvP).
- **Grinder governance**:
  - **Basic Grinder Anti-Hack & Anti-Hydroman (global)**: T1 starter hand grinders (`AngleGrinder`) are blocked from grinding/hacking enemy player blocks AND NPC blocks (`info.Amount = 0f`), eliminating the naked respawn-rush exploit. Allowed targets (self, same-faction, fully unowned debris) get a **1.2x QoL speed boost** in all zones.
  - **NPC Hand Grinder Boost (global)**: T2–4 hand grinders grind NPC-owned blocks at **2x speed** in all zones. Hand drills and ship grinders are not boosted.
  - Cross-faction grinding of player grids blocked in Z0/Z1; self/same-faction grinding allowed.
- **Balancing stack (Suit Combat Balancer plugin interplay)**: The balancer scales hand tools vs. neutral/enemy factions (hack speeds 0.25/0.5/0.15, hand drills 1.0/1.0/0.5) and tool→player damage (25%); ship tools are exempt (full speed — the rover salvage path). Both handlers multiply `info.Amount`, so effects stack commutatively with no ordering hazards. Net effective hand-grinder speeds vs NPC blocks: terminal above functional **0.50x**, below functional **1.0x**, armor **0.30x** of vanilla.

## Limited production zones
Enforces industrial and military tiering across the planetary surface:
- **`LimitedProdZone_Assembler.cs` & `Refinery.cs`**: Disables full-sized assemblers and refineries within $35\text{km}$ of Crossroads (unlocks in Zone 2).
- **`LimitedProdZone_ShipDrill.cs`**: Disables mobile ship drills within $20\text{km}$ (Zone 0) to preserve starter hub terrain. Unlocks in Zone 1.
- **`LimitedProdZone_LargeGatlingTurret.cs`, `LargeMissileTurret.cs`, `SmallGatlingGun.cs`, `SmallMissileLauncher*.cs`, `InteriorTurret.cs`**: Disables military weapons within $20\text{km}$ (Zone 0). Unlocks in Zone 1 for PvE defense and wreck clearance.
- **`LimitedProdZone_ConveyorSorter.cs`**: Disables weaponised conveyor sorters (such as the missile battery) within $20\text{km}$ (Zone 0); plain conveyor sorters stay on.
- All `LimitedProdZone_*.cs` scripts measure from the fixed Crossroads Tower coordinate, so they work whether or not the tower grid is loaded or concealed. The tower's `LimitedProdZone` beacon no longer does anything.

## King of the Hill anti-abuse
- **`KOTHNoSafezone_*.cs`**: Detects proximity to KOTH capture structures (e.g. Khar Toba, Kalash Site, Crashed Starship). Shuts off player-built safezone generators and projectors within range.
- **`NoLargeGridZone_Beacon.cs`**: Shuts off large grid power within 3km of small-grid-only KOTH sites.
- **`NoThrusterZone_Beacon.cs`**: Shuts off non-NPC thrusters within 3km of rover-only KOTH sites to enforce ground vehicle combat.
