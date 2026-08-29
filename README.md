# GVK Settings - Space Engineers Mod

**GV: Deserts of Kharak (GVK)** server configuration, rebalance definitions, and automated governance systems.

* **Steam Server Rules & Gameplay Guide**: https://steamcommunity.com/sharedfiles/filedetails/?id=2781522559
* **Steam Workshop Mod Collection**: https://steamcommunity.com/sharedfiles/filedetails/?id=2650582206

---

# Credits & Attributions

Special thanks to the following modders and community authors whose code, assets, and frameworks have contributed to GVK:

### Script & Mechanics Authors
* **Kamikaze**:
  * Original author of the **Underground Monitor** (`UndergroundMonitor.cs`, [Workshop #2713098288](https://steamcommunity.com/sharedfiles/filedetails/?id=2713098288)).
  * Original author of the **No PVP Zone** damage filter logic (`GVK_NoPVPZone.cs`).
  * Original author of the **NoFlyZone** script foundation adapted into **Limited Production Zones** (`LimitedProdZone.cs`).
  * Creator of the **Siegable Shield Generators & Siege Drives** mod framework.
* **Merii** (*highlyunavailable*):
  * Author of the **Tool Enhancer** script (`ToolEnhancer.cs`).
  * Author of the **Unknown Signal Owner Fix** (`FixUnknownSignal.cs`).
  * Core contributions to deformation damage handling and component overrides in **Armor Balance** (`GVK_ArmorBalance.cs`).
  * Grinder and voxel damage filter enhancements in **No PVP Zone** (`GVK_NoPVPZone.cs`).
* **Klime**:
  * Author of the **Free Safezone H2** mechanics (`SafezoneH2.cs`, [Workshop #1871733117](https://steamcommunity.com/sharedfiles/filedetails/?id=1871733117)).
  * Incorporates safezone mechanics and inspiration from Klime's workshop mods ([#2533952116](https://steamcommunity.com/sharedfiles/filedetails/?id=2533952116), [#1844150178](https://steamcommunity.com/sharedfiles/filedetails/?id=1844150178)).
* **TwitchingPsycho**:
  * Author of the **Animated Safezone Generator** model and rotation logic (`SafeZoneAnimated.cs`, [Workshop #2202391036](https://steamcommunity.com/sharedfiles/filedetails/?id=2202391036)).
* **Gauge**:
  * Author of the original **Balanced Deformation** foundation adapted into `GVK_ArmorBalance.cs`.
* **Steam Workshop #1907404695** ("No Limits for 'PRICE PER UNIT'"):
  * Original concept for removing minimum store listing price floors (`NoMinPrice.cs`).
* **Mike Dude**:
  * Server owner, systems engineer, and lead maintainer.
  * Author of the **KOTH anti-abuse systems** (`KOTHNoLargeGrid.cs`, `KOTHNoSafezone.cs`, `KOTHNoThrusters.cs`).
  * Overhaul and expansion of **Armor & Structural Balance** (`GVK_ArmorBalance.cs`).
  * Expansion and adaptation of **Limited Production & Weapon Zones** (`LimitedProdZone.cs`).

### Model & Asset Credits
* **Zantulo**: Lifted Wheels Suspension mod ([Workshop #2727185097](https://steamcommunity.com/sharedfiles/filedetails/?id=2727185097)).
* **RavenBolt**: 3x3x3 Small Grid Hydrogen Tank ([Workshop #540440994](https://steamcommunity.com/sharedfiles/filedetails/?id=540440994)).

---

# Main Functions

## 1. Block & Structural Rebalancing
* **Script**: `ModScripts/GVK_ArmorBalance.cs`
* **Description**: Custom armor resistance curves, blast resistance tuning for heavy structural components, deformation damage mitigation, and combat survivability rebalance for rover combat.

## 2. Zone 0 (Crossroads Tower) No-PVP & Anti-Griefing
* **Script**: `ModScripts/GVK_NoPVPZone.cs`
* **Description**: Enforces strict PvE within 20km of Crossroads Tower (62495, 28019, 37195). Blocks cross-faction grinding damage, prevents hostile grid tampering, and mitigates griefing without requiring admin intervention.

## 3. Limited Production & Weapon Zones
* **Script**: `ModScripts/LimitedProdZone.cs`
* **Description**: Multi-tiered radius governance:
  * **35km Production Zone**: Automatically powers down advanced refineries and assemblers (basic assemblers, blast furnaces, and food producers permitted).
  * **20km Weapon & Drill Zone**: Shuts down military turrets, rocket launchers, WeaponCore conveyor sorters, and ship drills (basic static drills permitted). Standard logistics and ToolCore utility tools are whitelisted.

## 4. KOTH Anti-Abuse Protections
* **Scripts**:
  * `ModScripts/KOTHNoLargeGrid.cs`: Shuts down non-NPC large-grid power generation (batteries, hydrogen engines, reactors, solar panels) within 3km of active KOTH zones. Small grids remain powered.
  * `ModScripts/KOTHNoSafezone.cs`: Shuts down player-built safezone generators and static MnM projectors within 3km of active KOTH zones.
  * `ModScripts/KOTHNoThrusters.cs`: Shuts down non-NPC thrusters within 3km of active KOTH zones.

## 5. Underground Base Monitor
* **Script**: `ModScripts/UndergroundMonitor.cs`
* **Description**: Monitors grid elevations relative to voxel terrain meshes. Automatically detects illegal subterranean bases dug deeper than server limits and handles automated warnings and faction transfer to SPRT.

## 6. Safezone Enhancements
* **Scripts**:
  * `ModScripts/SafezoneH2.cs`: Provides free, automatic jetpack hydrogen replenishment to players located inside their own active safezone bubble.
  * `ModScripts/SafeZoneAnimated.cs`: Drives continuous mechanical rotation animation on the custom Siegable Shield Generator block subpart.

## 7. Economy Price Floor Adjuster
* **Script**: `ModScripts/NoMinPrice.cs`
* **Description**: Sets the minimum price per unit on player store listings to 1 Space Credit, allowing custom player-run trade hubs and free-market pricing.

## 8. Tool Enhancer
* **Script**: `ModScripts/ToolEnhancer.cs`
* **Description**: Adjusts internal inventory capacities for ship tools to prevent clogging during salvage operations.

## 9. Unknown Signal Ownership Correction
* **Script**: `ModScripts/FixUnknownSignal.cs`
* **Description**: Ensures spawned Unknown Signal drop pods are correctly assigned to NPC ownership rather than player entity IDs.

---

# Audio Notes
* Use Yakitori Audio Converter for conversion to XWM:
  * Sampling frequency: 44.1kHz, Channel: 1 Mono, Volume: Default, Codec: Unsigned 8-bit.
* D2 Sounds are stereo files that are non-directional. D3 sounds are mono files that are 3D directional in-game:
  * Stereo sounds need to be approximately -12dB lower volume than the D3 mono version.
  * D3 Sounds **must** be mono format.
* Sounds with attached DistantSounds must have a `MaxDistance` equal to the DistantSound `MaxDistance`.
* Loopable sounds must be `.wav` 32-bit float 44100Hz; all one-shot sounds should be `.xwm` 48kbps. Start, Loop, and End files must all share the same format.
* Add 1 second of silence to the start of all distant sounds for realistic travel delay (except Loopable).
* `PreventSynchronization` defines the minimum interval in ticks before a sound can re-trigger.

---

# Space Engineers Modding Reference
* https://spaceengineers.wiki.gg/wiki/Modding
