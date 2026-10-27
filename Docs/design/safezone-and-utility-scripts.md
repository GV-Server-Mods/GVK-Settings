---
title: Safezone, armor and utility scripts
covers:
  - ModScripts/SafeZoneAnimated.cs
  - ModScripts/SafezoneH2.cs
  - ModScripts/GVK_ArmorBalance.cs
  - ModScripts/UndergroundMonitor.cs
  - ModScripts/ToolEnhancer.cs
  - ModScripts/NoMinPrice.cs
  - ModScripts/FixUnknownSignal.cs
  - ModScripts/GVK_PredictionSwitcher.cs
scripts:
  - ModScripts/SafeZoneAnimated.cs: Safezone generator animation and logic
  - ModScripts/SafezoneH2.cs: Refills jetpack hydrogen inside any enabled safezone
  - ModScripts/GVK_ArmorBalance.cs: Armor deformation and resistance tuning
  - ModScripts/UndergroundMonitor.cs: Switches off blocks buried under the terrain (ToolCore drills exempt)
  - ModScripts/ToolEnhancer.cs: Slightly larger ship grinder inventory so they clog less
  - ModScripts/NoMinPrice.cs: Lets custom player store listings go down to a price of 1
  - ModScripts/FixUnknownSignal.cs: Server only: reassigns an unknown signal that picked a real player's ID
  - ModScripts/GVK_PredictionSwitcher.cs: Client only: the integrated Multiplayer Prediction Switcher
commands:
  - /toggleprediction: Client only: switch the multiplayer prediction mode
---

# Safezone, armor and utility scripts

## Player safezone governors
- **`SafeZoneAnimated.cs`**: Manages visual shield animations and logic for player-built safezone generators (Kamikaze's Siegable Shields). 250m radius, 1W power.
- **`SafezoneH2.cs`**: Refills the jetpack hydrogen of any player inside an enabled safezone bubble (any owner, NPC safezones included). Safezone blocks are tracked as they enter and leave the world, so pasted, hangared and MES-spawned zones work without a restart.

## Utility scripts
- **`UndergroundMonitor.cs`**: Switches off functional blocks buried more than 50 m below the planet surface (wind turbines and solar panels: 5 m), so nobody can hide a production base inside the terrain. ToolCore ship drills are exempt, since underground mining rigs use them.
- **`ToolEnhancer.cs`**: Gives ship grinders a slightly larger inventory (cube dimensions multiplier 0.6, Keen default 0.5) so they clog less.
- **`NoMinPrice.cs`**: Lets custom player store listings go down to a minimum price of 1. The listing and transaction fees can't be set from the mod API; they live in `SessionComponents_Economy.sbc`.
- **`FixUnknownSignal.cs`** (server only): Reassigns the owner of an unknown signal if it ever picks a real player's entity ID.
- **`GVK_PredictionSwitcher.cs`** (client only): The integrated Multiplayer Prediction Switcher (original mod by Invalid, Workshop 3032417765), with a `/toggleprediction` chat command.

## Armor rebalancing
- **`GVK_ArmorBalance.cs`**: Dynamic armor deformation and resistance adjustments to balance rover-on-rover combat and missile impacts.
