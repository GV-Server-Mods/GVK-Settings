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
  - **Horizon Bearing Tracking with Authentic Keen HUD Icons**:
    - **GPS Waypoints (`marker_gps`)**: Displays personal GPS waypoints set to "Show On HUD" in their exact user-configured GPS colors.
    - **Active Radio Broadcasts (Antennas & Beacons)**: Automatically tracks broadcasting signals within communication range using Keen relation markers:
      - 🟢 **Friendly / Faction**: `marker_friendly` (Green)
      - 🔵 **Personal Grids**: `marker_self` (Cyan)
      - 🔴 **Hostile NPCs & Enemy Grids**: `marker_enemy` (Red)
      - 🟡 **Neutral NPCs & Trade Stations**: `marker_neutral` (Gold)
      - ⚪ **Unowned Grids / Derelicts**: `marker_neutral` (White)
    - **Dynamic Distance Badges**: High-readability distance readouts (e.g., `1.2k`, `15k`) dynamically centered below each icon in its matching marker color.
- **Unified Top-Right Tactical Radar & Zone HUD**:
  - **Live Corner Minimap (Top-Right, Dual Modes)**:
    - **Strategic Map Mode (Global)**: High-resolution topographical map (true 2:1 ratio) displaying whole-planet position blip, heading indicator, personal GPS waypoints, and detected broadcast signals.
    - **Tactical Vector Radar Mode (Local)**: Player-centered combat vector radar with:
      - **Camera-Facing Orientation & High-Visibility Player Reticle**: The radar aligns directly to your live camera view / line-of-sight in real time. Your rover/character forward heading is designated by an enlarged, high-visibility **Electric Gold / Amber pulsing chevron** layered over a **deep black contrast backing**, ensuring instantaneous visual distinction against all terrains and signals. Own-grid cockpit and antenna waypoints (< 30m) are automatically suppressed to keep the center reticle 100% clutter-free.
      - **Dynamic FOV View Cone ("V" Indicator)**: Two razor-thin tactical cyan HUD rays (`0.0008f`) radiate upward from the center crosshair out to the radar perimeter, forming an authentic avionics "V" frustum that precisely tracks your camera's active on-screen viewport. Dynamically widens and narrows in real time with your in-game Field of View and camera/sniper/turret zoom levels! Any contact inside the "V" is directly visible through your windshield/screen.
      - **Concentric Range Rings**: 1km, 2km, and 3km rings with cardinal crosshairs and heading indicator.
      - **Dual Scaling Modes (Linear vs Logarithmic)**:
        - **Linear Scale**: Selectable combat scanning ranges: 1.5 km, 3.0 km, and 5.0 km (`/radar range`).
        - **Logarithmic Scale (0 - 30 km)**: Compresses the desert combat theater out to the planetary horizon using a 3-decade logarithmic scale: Inner ring $= 300\text{ m}$ (dogfight/CQB range), Middle ring $= 3.0\text{ km}$ (visual line-of-sight / standard engagement), Outer ring $= 30.0\text{ km}$ (planetary horizon / edge of planet curvature) (`/radar scale` or `/radar log`). Contacts beyond 30 km (up to 100 km) max out to the outer perimeter ring.
      - **360° Outer-Edge Clamping**: Contacts beyond the active radar range (beyond 30 km in Log mode, or beyond selected range in Linear mode, up to 100 km) are pinned to the outer perimeter ring along their exact relative bearing at 75% scale and 80% opacity, providing complete 360-degree threat awareness without losing target bearings.
      - **Dynamic Altitude Signal Icons**:
        - Broadcast radio contacts (beacons and antennas) dynamically update their tactical icons based on true spherical planetary elevation relative to your vehicle (comparing radial altitude from Pertam's core, preventing planetary curvature from incorrectly flagging distant airborne contacts as 'below'):
          - **Upward Arrow** (`signal_up`): Target is $> 50\text{ m}$ above you.
          - **Equal Icon** (`signal_level`): Target is within $\pm 50\text{ m}$ altitude of you.
          - **Downward Arrow** (`signal_down`): Target is $> 50\text{ m}$ below you.
        - Faction relation colors (Allied green, Enemy red, Neutral white, NPC gold, Self cyan) remain active to show alignment, while GPS waypoints continue using their distinct GPS marker pins.
        - **Subtle Drop Shadow & Contrast Halo**: Every HUD contact texture (`signal_up`, `signal_down`, `signal_level`, `nav_arrow`, `marker_friendly`, `marker_enemy`, `marker_neutral`, `marker_self`, `marker_alert`) features an avionics-grade soft black drop shadow halo engineered to match Keen's `marker_gps.dds`. This guarantees instant, crystal-clear readability against blinding desert sands, harsh sunlight, terrain clutter, and night skies alike.
      - **Local Tangent Projection**: Projects true relative distances along Pertam's local horizon plane.
    - **Integrated Tactical Header Box**: The mode and range telemetry (`TACTICAL RADAR (LOG: 30 KM)` or `SECTOR MAP`) is cleanly framed in crisp white text inside a docked header card directly above the minimap, matching the exact width of the minimap card with a tactical grey accent strip on the left edge.
    - **Persistent Client Configuration**: All player preferences (Minimap visibility, Strategic/Radar mode, Linear/Log scale, Radar range, Compass ribbon, and Zone status bar) automatically save to local storage (`GVK_ZoneNavConfig.xml`) and persist seamlessly across world reloads, server restarts, and game reconnects.
    - **Instant Toggling**: Switch between Strategic Map and Tactical Radar modes via **`/radar`**, **`/minimap mode`**, or the **`F2` TextHUDAPI menu**.
    - **Scale Toggling**: Switch between Linear and Logarithmic zoom via **`/radar scale`** or the **`F2` TextHUDAPI menu**.
    - Toggle visibility on/off via `/minimap` or the F2 menu.
  - **Docked Zone Telemetry Status Panel**:
    - Seamlessly docked directly beneath the minimap card in the top-right corner, leaving the upper-center area below the compass completely clear for WeaponCore target lock and lead indicator HUDs.
    - Features a color-coded vertical threat accent strip and clear 2-line countdown telemetry:
      - **Zone 0**: Lime Green `[ ZONE 0: SAFE HUB ]` | `Crossroads: 12.4 km | Z1 Border in: 7.6 km`
      - **Zone 1**: Yellow `[ ZONE 1: PVE FRONTIER ]` | `Crossroads: 24.1 km | PvP Border in: 10.9 km`
      - **Zone 2**: Orange `[ ZONE 2: CONTESTED (PVP) ]` | `Crossroads: 41.3 km | Z3 Border in: 8.7 km`
      - **Zone 3**: Red `[ ZONE 3: GAALSIEN HEART ]` | `Crossroads: 54.2 km | Core Dist: 12.8 km`
    - Toggleable via `/zone hud` or the F2 TextHUDAPI menu.
- **Interactive Full-Screen Satellite Map (`M` Key / `/map`)**:
  - Toggled with hotkey **`M`** (or `/map`) with zero plugin or LCD screen requirements.
  - Displays high-resolution topographical Kharak map with 20 / 35 / 50 km zone boundaries.
  - **Real-Time "You Are Here" Blip**: Blinking player reticle tracking spherical lat/long on Pertam.
  - **Auto-Plotted Personal GPS List & Signals**: Reads `MyAPIGateway.Session.GPS` and active radio broadcasts, plotting them with 50% enlarged Keen icons and relation colors (Allied green, Derelict white, Hostile red, Neutral gold).
  - **Smart Tactical Label Truncation**: Waypoint and signal names exceeding 20 characters are compactly truncated (`[first 12]...[last 4]`, e.g. `KOTH Crashed...ship`) to prevent map text clutter while allowing common outpost names to show in full.
  - **Waterfall Signal Deconfliction**: Dense clusters of nearby signals (bases with multiple beacons/antennas) are automatically grouped. Up to 5 signals waterfall vertically with individual icons, while 6+ signals cap at 5 lines displaying the top 4 signals plus an overflow `+N more...` tag to prevent screen-space clutter.
  - **Docked Tactical Header Panel**: Features a color-accented status bar seamlessly integrated above the top of the map frame, displaying sector classification, Crossroads distance, border countdown, and keybindings in the matching zone threat color (Green/Yellow/Orange/Red).
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
| | `/zone hud` | Toggles the top-right docked zone status and telemetry panel on/off. |
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

