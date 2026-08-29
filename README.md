# GV: Deserts of Kharak (GVK) — Server Settings & Systems Design Document

> **Primary Reference & Mod Documentation**  
> **Steam Server Rules & Gameplay Guide**: [Steam Guide #2781522559](https://steamcommunity.com/sharedfiles/filedetails/?id=2781522559)  
> **Steam Workshop Mod Collection**: [Steam Collection #2650582206](https://steamcommunity.com/sharedfiles/filedetails/?id=2650582206)  
> **KSH Modding Reference**: [Space Engineers Modding Wiki](https://spaceengineers.wiki.gg/wiki/Modding)

---

## 1. Design Philosophy & Core Pillars

1. **100% Automated Governance (Zero "Trust Me Bro™" Rules)**:
   Every server constraint (zone boundaries, KOTH restrictions, safezone anti-stacking, grid limits, illegal block counts) is enforced programmatically via active ModAPI systems rather than admin policing or Discord tickets. If a rule exists, the code makes violating it mechanically impossible.
2. **Sim-Speed & GC Optimization (Target: 60 TPS Server Health)**:
   - Zero allocations in hot simulation paths (`UpdateBeforeSimulation`, `UpdateAfterSimulation`).
   - Reusable static object buffers, squared distances (`Vector3D.DistanceSquared`) to avoid expensive square root operations, and stepped update intervals (`10th` or `100th` frame).
3. **Anti-Mod-Bloat Architecture**:
   Aggressively minimize third-party mod dependencies. Prefer lightweight, highly optimized internal C# scripts and SBC overrides within `GVK_Settings` to maintain fast client join times, low memory footprints, and engine stability.
4. **Low Cognitive Overhead**:
   Keep rules intuitive with clean numbers (e.g. 20 / 35 / 50 km boundaries) so players spend time driving, salvaging, and fighting rather than memorizing a complex rulebook.

---

## 2. Planetary Zone Architecture (Pertam Rover-Centric)

All distance zones originate from the **Crossroads Tower Beacon** at `{X: 62495.55, Y: 28019.04, Z: 37195.71}`.

| Zone | Radius (from Crossroads) | Designation | Production Permitted | PvP / Combat Status | Shields & Sieges |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Zone 0** | $0 – 20\text{ km}$ | **Safe Starter Hub** | Basic Assembler, Basic Refinery, Basic Static Drill | **Strict PvE**: Player block & character damage zeroed out. NPCs deal **0 damage**. Hostile NPC wrecks & unowned derelicts can be ground for salvage. Military grid weapons disabled. | **NON-SIEGABLE** |
| **Zone 1** | $20 – 35\text{ km}$ | **PvE & Salvage Frontier** | Basic Production Only, All Static Drills (Large Prod disabled) | **Strict PvE**: Weapons, ship drills & ship grinders unlocked for fighting hostile NPC derelicts and wreck salvage. Hostile NPCs can damage players. Player-vs-player damage and cross-faction player grinding blocked (`info.Amount = 0f`). | **NON-SIEGABLE** |
| **Zone 2** | $35 – 50\text{ km}$ | **Contested Desert** | Full Large Production & Upgrade Modules Unlocked | **Full PvP & Warfare**: Uncapped player combat, medium defended wrecks, convoys, and cargo ships. | **SIEGABLE** via Siege Drives |
| **Zone 3** | $> 50\text{ km}$ | **Deep Desert / Gaalsien Heart** | Full Uncapped Production | **High-Threat PvPvE**: Ancient relics, Gaalsien battlecruisers, convoys, and relic ammunition. | **SIEGABLE** via Siege Drives |

> [!NOTE]
> **Boundary Symmetry**: In `GVK_Derelicts`, the MES wreck spawn zones are centered at Pertam's exact surface antipode `{X: 3569.33, Y: 36772.94, Z: 26952.63}` with radii `57,000m` (Z1), `49,300m` (Z2), and `34,000m` (Z3), aligning to Crossroads zone borders with millimeter precision in every direction.

---

## 3. Core Mod Systems & Scripts (`Data/Scripts/GVK Settings/`)

### `GVK_NoPVPZone.cs`
- **Damage Interception Hook**: Hooks `MyAPIGateway.Session.DamageSystem.RegisterBeforeDamageHandler(0, grinder_handler)` server-side.
- **Zone 0 (0 – 20km)**: Complete block and character damage immunity (`info.Amount = 0f`). NPCs deal zero damage. Players can safely grind hostile NPC wrecks and unowned derelicts.
- **Zone 1 (20 – 35km)**: 
  - Allows weapon, drill, and grinder damage to unowned blocks (`BigOwners.Count == 0`) and hostile NPC factions (`IsEveryoneNpc()` or tags `GAALSIEN`, `DERELICT`, `SPRT`, `KHAANEPH`).
  - Allows hostile NPCs to damage player grids.
- **Basic Grinder Anti-Hack & Anti-Hydroman Protection**: Basic starter hand grinders (`AngleGrinder`) are globally blocked from grinding or hacking enemy player blocks OR hostile NPC blocks (`info.Amount = 0f`), completely eliminating the naked "hydroman" respawn-rush exploit against NPCs and player bases. Players must construct upgraded hand grinders (Tiers 2–4) or rover ship grinders to dismantle or hack NPC assets.

### `GVK_ZoneNavCommand.cs` & `API/HudAPIv2.cs` (Kharak Tactical Navigation Suite)
- **Top-Center Tactical Compass Ribbon**:
  - Full $360^\circ$ planetary heading tape (N, NE, E, SE, S, SW, W, NW and degree marks) projected along Pertam's local horizon.
  - **Dynamic Bearing Badges**: Directly marks tactical points of interest when looking in their direction:
    - 🟢 **`[CR]`**: Crossroads Tower (Z0 Safe Hub).
    - 🔴 **`[Z3]`**: Gaalsien Deep Desert Heart (Antipode Center).
    - 🟡 **`[KOTH]`**: Active King of the Hill objectives (Khar Toba, Kalash Site, Crashed Starship).
    - 🔵 **`[TRADE]`**: Trade Stations (Coalition Base, Rusty's, Skyport, Mastodon, Sevastopol).
    - 🟠 **`[WRECK]`**: Detected radio/beacon distress signals from hostile/derelict wrecks within 10km.
- **Integrated Zone Telemetry Status Bar**:
  - Anchored directly below the compass reticle, color-coded by zone threat level:
    - **Zone 0**: Lime Green `[ ZONE 0: SAFE HUB ] 12.4 km to Crossroads (Z1 in 7.6 km)`
    - **Zone 1**: Cyan `[ ZONE 1: PVE FRONTIER ] 26.4 km to Crossroads (PvP in 8.6 km)`
    - **Zone 2**: Orange `[ ZONE 2: CONTESTED (PVP) ] 41.2 km to Crossroads (Z3 in 8.8 km)`
    - **Zone 3**: Red `[ ZONE 3: GAALSIEN HEART ] 54.0 km to Crossroads | Z3 Core: 12.1 km`
- **Live Corner Minimap (Top-Right)**:
  - Compact tactical radar card showing local topographical terrain, live player position blip, heading indicator, nearby personal GPS waypoints (rendered in their exact in-game custom GPS colors), and detected broadcast signals.
  - Toggleable via `/minimap` or the F2 TextHUDAPI menu.
- **Interactive Full-Screen Satellite Map (`M` Key / `/map`)**:
  - Toggled with hotkey **`M`** (or `/map`) with zero plugin or LCD screen requirements.
  - Displays high-resolution topographical Kharak map with 20 / 35 / 50 km zone boundaries.
  - **Real-Time "You Are Here" Blip**: Blinking player reticle tracking spherical lat/long on Pertam.
  - **Auto-Plotted Personal GPS List**: Reads `MyAPIGateway.Session.GPS` and plots player waypoints using their exact custom colors and names.
  - **Detected Radio Broadcast Signals**: Scans active antennas/beacons within communication range and plots them with relation colors (Allied green, Derelict cyan, Hostile red).
  - Pressing **`M`** or **Esc** closes the map overlay.
- **Keen Mission Screen Objective Popups (`/zone`, `/whereami`, `/loc`)**:
  - Pops up the official Keen scenario **Mission Screen** modal window (`ShowMissionScreen`) for sector briefing and rules matrix without chat spam.
  - `/zones` opens the full planetary 4-zone matrix directory.

### `LimitedProdZone_*.cs`
Enforces industrial and military tiering across the planetary surface:
- **`LimitedProdZone_Assembler.cs` & `Refinery.cs`**: Disables full-sized assemblers and refineries within $35\text{km}$ of Crossroads (unlocks in Zone 2).
- **`LimitedProdZone_ShipDrill.cs`**: Disables mobile ship drills within $20\text{km}$ (Zone 0) to preserve starter hub terrain. Unlocks in Zone 1.
- **`LimitedProdZone_LargeGatlingTurret.cs`, `LargeMissileTurret.cs`, `SmallGatlingGun.cs`, `SmallMissileLauncher*.cs`, `InteriorTurret.cs`**: Disables military weapons within $20\text{km}$ (Zone 0). Unlocks in Zone 1 for PvE defense and wreck clearance.
- **`LimitedProdZone_ConveyorSorter.cs` & `Beacon.cs`**: Zone-based utility governors.

### King of the Hill (KOTH) Anti-Abuse
- **`KOTHNoSafezone_*.cs`**: Detects proximity to KOTH capture structures (e.g. Khar Toba, Kalash Site, Crashed Starship). Shuts off player-built safezone generators and projectors within range.
- **`NoLargeGridZone_Beacon.cs`**: Shuts off large grid power within 3km of small-grid-only KOTH sites.
- **`NoThrusterZone_Beacon.cs`**: Shuts off non-NPC thrusters within 3km of rover-only KOTH sites to enforce ground vehicle combat.

### Player Safezone Governors
- **`SafezoneAnimated.cs`**: Manages visual shield animations and logic for player-built safezone generators (Kamikaze's Siegable Shields). 250m radius, 1W power.
- **`SafezoneH2.cs`**: Provides free, unlimited jetpack hydrogen to players within their own active safezone bubble.
- **`Safezone3kmCheck_*.cs`**: Prevents players from activating overlapping safezones within 3km of another player shield generator.

### Armor Rebalancing
- **`GVK_ArmorBalance.cs`**: Dynamic armor deformation and resistance adjustments to balance rover-on-rover combat and missile impacts.

---

## 4. Grid Classes, Speeds & Spec Core Framework

Grid class is determined by the installed Core Beacon block. Points: Utility Points (**UPs**), Mobility Points (**MPs**).

| Grid Class | Grid Type | Max Speed | Max Blocks | Utility Points (UPs) | Mobility Points (MPs) | Interior Turret Cap | Special Traits |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **Small Grid Core** | Small Grid Mobile | $120\text{ m/s}$ | 2,000 | 40 UPs | 140 MPs | 0 | Fast scouts & interceptors |
| **Large Grid Light** | Large Grid Mobile | $90\text{ m/s}$ | 750 | 40 UPs | 80 MPs | 2 | Light logistics & patrol rovers |
| **Large Grid Medium**| Large Grid Mobile | $80\text{ m/s}$ | 2,000 | 60 UPs | 140 MPs | 4 | Mainline combat rovers & mobile bases |
| **Large Grid Heavy** | Large Grid Mobile | $70\text{ m/s}$ | 4,000 | 80 UPs | 200 MPs | 6 | Heavy battlecruisers & land ironclads |
| **Fortress Station** | Static Station Only| Station | 6,000 | 120 UPs | 200 MPs | 8 | 50% passive damage reduction |
| **Pipeline Station** | Static Station Only| Station | 50 | 10 UPs | 0 MPs | 1 | Wireless logistics hub |
| **Shield Station**   | Static Station Only| Station | 50 | 10 UPs | 0 MPs | 0 | Dedicated shield outpost anchor |

### Point Allocations & Hard Caps
- **Mobility Points (MPs)**: Wheel Suspensions (10 MPs each, hard cap 20), Thrusters / Hovers (1 MP each, hard cap 200).
- **Utility Points (UPs)**:
  - Wind Turbines / Solar Panels: 1 UP (hard cap 20)
  - Ship Drills (Regular / Advanced): 1 / 3 UPs (hard cap 10 / 3)
  - Ship Welders: 2 UPs (cap 10) | Ship Grinders: 1 UP (cap 20)
  - Pistons / Rotors / Hinges: 2 UPs (hard cap 10 each)
  - Production (Basic / Regular / Advanced): 1 / 2 / 5 UPs (hard cap 20 / 10 / 4)
  - Weapons: 1+ UPs (cap 50) | Relic Weapon Slots: 10 UPs (hard cap 2)
  - H2 Engines / H2-O2 Gens: 0 UPs (cap 10 regular / 1 advanced)
  - Shield Generator: 2 per Faction | SRBM / Odin: 14 / 16 UPs (1 per Faction) | Drill Blocker: 1 per Grid

---

## 5. Siegable Shields & Siege Drives

- **Shield Generators (SafeZoneBlockReskin)**:
  - Replaces vanilla safezones. 250m radius, 1W operating power.
  - Provides unlimited jetpack hydrogen to grid owners.
  - **Zone 0 and Zone 1**: **100% NON-SIEGABLE**. Safe from all sieges.
  - **Zone 2 and Zone 3**: **SIEGABLE** via player Siege Drives.
- **Siege Drives (LargeJumpDrive & LargePrototechJumpDrive)**:
  - Built on mobile large grids within $3\text{km}$ of the target shield generator.
  - Activation cost: **100 Siege Chips** (acquired via trade, missions, and derelict salvage).
  - Emits a high-visibility red laser pointing directly at the defending shield generator.
- **Siege Cadence**:
  - **Drain Time**: 30 minutes (shield drops from 100% to 0%). Attackers must protect the siege rover.
  - **Recharge Time**: 15 minutes (shield charges back from 0% to 100% once the siege drive is neutralized).

---

## 6. Factions & Dynamic Alliance System

- **Core Factions**:
  - `COALITION` (Northern Kiithid, Founder: Rachel S'jet): Starter safe hub, free scrap refining, trade stations.
  - `GAALSIEN` (Kiith Gaalsien, Founder: Khagaan): Hostile NPC desert raiders, convoys, and battlecruisers.
  - `DERELICT`: Hostile automated defense wrecks and ancient technology caches.
  - `KOTH`: King of the Hill objective sites.
- **Dynamic NPC Alliances**:
  - `SOBAN` (Kiith Soban, Founder: Soban the Red): Heavy mining and logistics Kiith.
  - `KHAANEPH` (Khaaneph Scavengers): Clanless southern nomads and salvage specialists.
  - **Alignment**: Factions choose a one-time alignment via `/alliance SOBAN` or `/alliance KHAANEPH`. NPC territories dynamically expand or contract based on asset defense, bounty missions, and convoy ambushes.

---

## 7. Economy, Components & Scrap Refining

- **Core Currencies & Ingots**:
  - **CUs (Construction Units)**: Used for advanced hull armor, weapon assemblies, and heavy blocks.
  - **RUs (Resource Units)**: Primary economic currency for trading, ship parts, and specialized utilities.
- **Tech Components**:
  - `[Tech] Igniter`, `[Tech] Grav. Reflector`, `[Tech] Bolt Carrier`, `[Tech] Gun Cradle`, `[Tech] Launch Assem.`, `[Tech] Particle Emit.`, `[Tech] Data Core`, `Turbo Encabulator`.
  - Tech components drop exclusively from derelicts, boss wrecks, and military convoys.
- **Scrap Tech Conversion**:
  - NPC weapons and tech grind down into `[Scrap]` Tech components.
  - 100% refined into CUs for free at Coalition Base (Z0), Skyport, Mastodon, Sevastopol, or Coalition mobile trade cruisers.

---

## 8. Server Logistics, Maintenance & Cleanup Cadence

- **Daily Voxel Resets**:
  - Pertam voxels reset daily to smooth crater gouging and Havok physics pits.
  - Subterranean bases lower than $50\text{m}$ below the surface are automatically transferred to SPRT ownership.
- **Offline Faction Safezones (FSZ)**:
  - Automatically protects faction large grids (> 30 blocks) 60 seconds after the last faction member logs off.
  - Requires no hostile/neutral player grids within $1,000\text{m}$.
- **Cleanup Automation**:
  - **Hourly Cleanup**: Any grid without an active beacon is deleted.
  - **Debris Sweep (Every 30 Mins)**: Any grid split smaller than 3 blocks is deleted immediately.
  - **Ejected Items**: Floating objects and connector-ejected items are purged instantly.
  - **Auto-Hangar**: Inactive faction grids are archived to the server hangar after 8–14 days of player absence.
- **Custom Logistics**:
  - **Manufacturing & Maintenance (MnM)**: Performance-friendly projector welders. 1 active per construct, stationary ($< 2\text{ m/s}$). 1 Uranium = 3x boost for 20 minutes.
  - **Pipelines**: Point-to-point wireless logistics connections between bases (`/pipeline toggle`).
  - **Static Drills**: Passive resource wells with a 50m proximity penalty for duplicate ore wells.

---

## 9. In-Game Commands Reference

| Category | Command | Description |
| :--- | :--- | :--- |
| **Navigation & Map** | `[M]` Key or `/map` | Toggles full-screen interactive Kharak Satellite Map with live GPS & signals. |
| | `/minimap` | Toggles top-right corner minimap radar card on/off. |
| | `/compass` | Toggles top-center heading ribbon and tactical waypoint bearing tape on/off. |
| | `/zone hud` | Toggles the live 2D on-screen zone status bar on/off. |
| | `/zone` or `/whereami` | Opens Keen Mission Screen popup with current sector status, distances, and rules. |
| | `/zones` | Opens full 4-zone planetary matrix directory in Mission Screen modal. |
| **Alliances** | `/alliance SOBAN` | Align your faction with Kiith Soban (one-time selection). |
| | `/alliance KHAANEPH` | Align your faction with Khaaneph Scavengers. |
| **Logistics** | `/pipeline toggle` | Connects or disconnects wireless logistics between pipeline stations. |
| **Hangar** | `/hangar save` | Stores the aimed grid into your faction hangar. |
| | `/hangar load <id>` | Spawns a stored grid from the hangar. |
| | `/hangar list` | Lists all stored faction grids. |
| **Beacon / Limits** | `/core check` | Displays current UP, MP, block count, and turret limit utilization. |
| **Bounties** | `/bounty list` | Displays active player and NPC bounties. |

---

## 10. Audio Engineering Reference

When producing or modifying audio assets (`.sbc` SoundCategories and SoundRules):
- **Tooling**: Use *Yakitori Audio Converter* for fast conversion to `.xwm`.
  - Sampling Frequency: `44.1 kHz` | Channels: `1 Mono` | Codec: `Unsigned 8-bit`
- **D2 vs. D3 Space Engineers Audio Architecture**:
  - **D2 Sounds**: Non-directional stereo ambient sounds.
  - **D3 Sounds**: Fully 3D directional in-game mono audio. **Must be Mono format**.
  - Volume calibration: D2 sounds should be approximately `-12 dB` quieter than their D3 equivalents.
- **Distant Sounds**:
  - Sounds with attached `DistantSounds` must have `MaxDistance` equal to the `DistantSound` `MaxDistance`.
  - Add 1.0 second of silence to the start of all distant sound files for atmospheric sound-travel delay (except loopables).
- **Loopables**:
  - Looping sounds must be `.wav` 32-bit float `44,100 Hz`.
  - Start, Loop, and End sound files must all share the exact same `.wav` audio format.
  - All non-looping sound effects should be `.xwm` at `48 kbps`.
- **Engine Timing**:
  - `PreventSynchronization`: Minimum time in game ticks (60 ticks = 1 sec) before the sound definition can be re-triggered.

---

## 11. External Mod Credits & Attributions

- [Steam Server Rules & Gameplay Guide](https://steamcommunity.com/sharedfiles/filedetails/?id=2781522559)
- **Lifted Wheels Suspension**: Credit to *Zantulo* ([Workshop #2727185097](https://steamcommunity.com/sharedfiles/filedetails/?id=2727185097)).
- **Animated Safezone Generator**: Credit to *TwitchingPsycho* ([Workshop #2202391036](https://steamcommunity.com/sharedfiles/filedetails/?id=2202391036)).
- **3x3x3 Small Grid Hydrogen Tank**: Credit to *RavenBolt* ([Workshop #540440994](https://steamcommunity.com/sharedfiles/filedetails/?id=540440994)).
- **Klime Utility Mechanics**: Incorporates concepts and code (with *Klime's* permission) from:
  - [Workshop #1871733117](https://steamcommunity.com/sharedfiles/filedetails/?id=1871733117)
  - [Workshop #2533952116](https://steamcommunity.com/sharedfiles/filedetails/?id=2533952116)
  - [Workshop #1844150178](https://steamcommunity.com/sharedfiles/filedetails/?id=1844150178)

