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

All distance zones originate from the **Crossroads Tower Beacon** at `{X: 62495.55, Y: 28019.04, Z: 37195.71}`. Player-facing gameplay rules per zone (production tiers, PvP status, siegability) live in the [Steam Server Rules & Gameplay Guide](https://steamcommunity.com/sharedfiles/filedetails/?id=2781522559) — this document covers what this repo implements and where.

| Zone | Radius (from Crossroads) | Designation | Enforced By (this repo) |
| :--- | :--- | :--- | :--- |
| **Zone 0** | $0 – 20\text{ km}$ | Safe Starter Hub | `GVK_NoPVPZone.cs`, `LimitedProdZone_*.cs` |
| **Zone 1** | $20 – 35\text{ km}$ | PvE & Salvage Frontier | `GVK_NoPVPZone.cs`, `LimitedProdZone_*.cs` |
| **Zone 2** | $35 – 50\text{ km}$ | Contested Desert | Vanilla rules (no zone enforcement) |
| **Zone 3** | $> 50\text{ km}$ | Deep Desert / Gaalsien Heart | `GVK_Derelicts` MES spawn zones |

> [!NOTE]
> **Boundary Symmetry**: In `GVK_Derelicts`, the MES wreck spawn zones are centered at Pertam's exact surface antipode `{X: 3569.33, Y: 36772.94, Z: 26952.63}` with radii `57,000m` (Z1), `49,300m` (Z2), and `34,000m` (Z3), aligning to Crossroads zone borders with millimeter precision in every direction.

---

## 3. Core Mod Systems & Scripts (`Data/Scripts/GVK Settings/`)

### `GVK_NoPVPZone.cs`
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
          - **Upward Arrow** (`signal_up`): Target is $> 200\text{ m}$ above you.
          - **Equal Icon** (`signal_level`): Target is within $\pm 200\text{ m}$ altitude of you.
          - **Downward Arrow** (`signal_down`): Target is $> 200\text{ m}$ below you.
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

Grid classes, speeds, UP/MP point costs, and hard caps are maintained and enforced by the dedicated Spec Core / Ship Core mod (see the `/core check` command) and documented in the Steam Guide. They are intentionally **not** duplicated here to avoid two sources of truth drifting apart.

---

## 5. Siegable Shields & Siege Drives

Shield generator mechanics (Kamikaze's Siegable Shields mod) and siege rules (chip costs, 3km build range, drain/recharge cadence) are documented in the Steam Guide. This repo contributes the safezone governor scripts listed in section 3 (`SafezoneAnimated.cs`, `SafezoneH2.cs`, `Safezone3kmCheck_*.cs`).

---

## 6. Factions & Economy (SBC Definitions)

Faction and economy SBC definitions ship in this repo (`Content/Data/`). Full gameplay details are in the Steam Guide; summary:

- **Core Factions** (`Factions.sbc`): `COALITION` (starter hub, free scrap refining, trade), `GAALSIEN` (hostile raiders), `DERELICT` (automated defense wrecks), `KOTH` (objective sites). Dynamic alliances: `SOBAN`, `KHAANEPH` via the server's `/alliance` plugin.
- **Currencies**: CUs (Construction Units) and RUs (Resource Units) ingots.
- **Tech Components**: `[Tech] Igniter`, `Grav. Reflector`, `Bolt Carrier`, `Gun Cradle`, `Launch Assem.`, `Particle Emit.`, `Data Core`, `Turbo Encabulator` — drop from derelicts, boss wrecks, and military convoys.
- **Scrap Tech Conversion**: NPC tech grinds into `[Scrap]`, refined to CUs for free at Coalition refineries.

---

## 7. Server Operations Pointer

Voxel reset cadence, offline faction safezones, cleanup automation, auto-hangar, and MnM/pipeline logistics are operated by the Torch server plugins and documented in the Steam Guide — not part of this repo.

---

## 8. In-Game Commands Reference

Commands provided by this repo's navigation suite (`GVK_ZoneNavCommand.cs`):

| Command | Description |
| :--- | :--- |
| `[M]` key or `/map` | Toggles full-screen interactive Kharak Satellite Map with live GPS & signals. |
| `/minimap` | Toggles top-right corner minimap radar card on/off. |
| `/compass` | Toggles top-center heading ribbon and tactical waypoint bearing tape on/off. |
| `/zone hud` | Toggles the top-right docked zone status and telemetry panel on/off. |
| `/zone`, `/whereami`, `/loc` | Opens Mission Screen popup with current sector status and distances. |
| `/zones` | Opens full 4-zone planetary matrix directory in Mission Screen modal. |
| `/zone gps`, `/gps defaults` | Restores default Kharak GPS waypoints. |
| `/nav rate [ticks]` | Sets or cycles HUD refresh rate (1–60 ticks). |
| `/radar range [km]`, `/radar scale`, `/minimap size`, `/compass size` | Radar/scale tuning shortcuts (also in F2 mod menu). |

Server-plugin commands (`/alliance`, `/pipeline`, `/hangar`, `/core check`, `/bounty`) belong to their respective mods/plugins and are documented in the Steam Guide.

---

## 9. Audio Engineering Reference

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

## 10. External Mod Credits & Attributions

- [Steam Server Rules & Gameplay Guide](https://steamcommunity.com/sharedfiles/filedetails/?id=2781522559)
- **Lifted Wheels Suspension**: Credit to *Zantulo* ([Workshop #2727185097](https://steamcommunity.com/sharedfiles/filedetails/?id=2727185097)).
- **Animated Safezone Generator**: Credit to *TwitchingPsycho* ([Workshop #2202391036](https://steamcommunity.com/sharedfiles/filedetails/?id=2202391036)).
- **3x3x3 Small Grid Hydrogen Tank**: Credit to *RavenBolt* ([Workshop #540440994](https://steamcommunity.com/sharedfiles/filedetails/?id=540440994)).
- **Klime Utility Mechanics**: Incorporates concepts and code (with *Klime's* permission) from:
  - [Workshop #1871733117](https://steamcommunity.com/sharedfiles/filedetails/?id=1871733117)
  - [Workshop #2533952116](https://steamcommunity.com/sharedfiles/filedetails/?id=2533952116)
  - [Workshop #1844150178](https://steamcommunity.com/sharedfiles/filedetails/?id=1844150178)

