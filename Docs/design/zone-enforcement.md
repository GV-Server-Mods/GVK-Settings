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
- **Zone borders**: the MES zone spheres from GVK_Derelicts `GVK-Universal-Zone.sbc`, mirrored in the script with a keep-in-sync comment. Zone 0 is the 20,000 m sphere around Crossroads; Zone 1 is outside both the Zone 0 sphere and the Zone 2 sphere (49,300 m around the Z3 centre), so the PvE border matches the `/gvk` zone bar at any height. On the ground that is about 20 – 35 km from Crossroads.
- **PvP needs both sides in Zone 2 or 3**: player-vs-player damage and grinding land only when the target and the attacker are both outside Zones 0 and 1, so nobody can shoot out of the PvE zones at players who can't shoot back (the Zone 1 sphere leans out over Zone 2 ground with height, about 2.7 km at 2 km up). The attacker's zone comes from its grid for blocks, otherwise from the attacking entity (grid, character, hand tool or gun). Missile explosions report the owner's character, not the launcher, so for missiles that is where the owner stands.
- **Attacker identity**: blocks, grids, characters, hand tools (`IMyHandheldGunObject<MyToolBase>`) and hand rifles and pistols (`IMyAutomaticRifleGun`; their bullets name the gun as the attacker). Before 2026-11-13 guns were missed, so their shots counted as environment damage and were never blocked. A rocket's direct hit on a character reports no attacker (AttackerId 0), so it is treated as environment; the explosion is still checked.
- **Block damage (Zone 0)**: All block damage zeroed (`info.Amount = 0f`) — total starter hub immunity for player grids.
- **Block damage (Zone 1)**:
  - Unowned blocks (`BigOwners.Count == 0`) and NPC-faction grids (`IsEveryoneNpc()` or tags `GAALSIEN`, `DERELICT`, `SPRT`, `KHAANEPH`) take weapon/drill/grinder damage. Friendly NPC structures (COALITION starter hub, Crossroads Tower) are protected by **MES invulnerability flags** in GVK_Derelicts — cross-system dependency.
  - NPC attackers can damage player grids; self-damage and same-faction friendly fire allowed; cross-faction PvP damage zeroed.
- **Character damage matrix (weapon, kinetic AND grinder damage vs. player bodies)**:
  - **Fall / collision / environment damage (no attacker entity): allowed in ALL zones.** The desert is the tutorial — no false security in lower zones.
  - **Self-inflicted damage (own ship's grinders/turrets/rams, own hand tools): allowed in ALL zones** so players don't develop bad habits that bite them in the deep desert.
  - Same-faction friendly damage: allowed (Keen's friendly-fire toggle governs the rest).
  - Zone 0: NPC and enemy-player damage to characters zeroed. Zone 1: NPC attackers allowed, enemy players zeroed. Zones 2 – 3: vanilla (full PvP) unless the enemy player attacks from Zone 0 or 1.
  - NPCs on foot count as characters, so players can't hurt them in Zones 0 and 1 (NPC grids and wrecks still take player damage there).
- **Grinder governance**:
  - **Basic Grinder Anti-Hack & Anti-Hydroman (global)**: T1 starter hand grinders (`AngleGrinder`) are blocked from grinding/hacking enemy player blocks AND NPC blocks (`info.Amount = 0f`), eliminating the naked respawn-rush exploit. Allowed targets (self, same-faction, fully unowned debris) get a **1.2x QoL speed boost** in all zones.
  - **NPC Hand Grinder Boost (global)**: T2–4 hand grinders grind NPC-owned blocks at **2x speed** in all zones. Hand drills and ship grinders are not boosted.
  - Cross-faction grinding of player grids blocked unless the grinder and the target are both in Zone 2 or 3; self/same-faction grinding allowed.
- **Balancing stack (Suit Combat Balancer plugin interplay)**: The balancer scales hand tools vs. neutral/enemy factions (hack speeds 0.25/0.5/0.15, hand drills 1.0/1.0/0.5) and tool→player damage (25%); ship tools are exempt (full speed — the rover salvage path). Both handlers multiply `info.Amount`, so effects stack commutatively with no ordering hazards. Net effective hand-grinder speeds vs NPC blocks: terminal above functional **0.50x**, below functional **1.0x**, armor **0.30x** of vanilla.

## Limited production zones
Enforces industrial and military tiering across the planetary surface:
- **`LimitedProdZone_Assembler.cs` & `Refinery.cs`**: Disables full-sized assemblers and refineries in Zones 0 and 1, by the same MES spheres as `GVK_NoPVPZone.cs` (`LimitedProdZone_Zones.cs`); they unlock inside the Zone 2 sphere.
- **`LimitedProdZone_ShipDrill.cs`**: Disables mobile ship drills within $20\text{km}$ (Zone 0) to preserve starter hub terrain. Unlocks in Zone 1.
- **`LimitedProdZone_LargeGatlingTurret.cs`, `LargeMissileTurret.cs`, `SmallGatlingGun.cs`, `SmallMissileLauncher*.cs`, `InteriorTurret.cs`**: Disables military weapons within $20\text{km}$ (Zone 0). Unlocks in Zone 1 for PvE defense and wreck clearance.
- **`LimitedProdZone_ConveyorSorter.cs`**: Disables weaponised conveyor sorters (such as the missile battery) within $20\text{km}$ (Zone 0); plain conveyor sorters stay on.
- All `LimitedProdZone_*.cs` scripts measure from fixed coordinates (Crossroads, and the Z3 centre for the assembler and refinery), so they work whether or not the tower grid is loaded or concealed. The 20 km scripts' Crossroads sphere is the MES Zone 0 sphere. The tower's `LimitedProdZone` beacon no longer does anything.

## King of the Hill anti-abuse
- **`KOTHNoSafezone_*.cs`**: Detects proximity to KOTH capture structures (e.g. Khar Toba, Kalash Site, Crashed Starship). Shuts off player-built safezone generators and projectors within range.
- **`NoLargeGridZone_Beacon.cs`**: Shuts off large grid power within 3km of small-grid-only KOTH sites.
- **`NoThrusterZone_Beacon.cs`**: Shuts off non-NPC thrusters within 3km of rover-only KOTH sites to enforce ground vehicle combat.
