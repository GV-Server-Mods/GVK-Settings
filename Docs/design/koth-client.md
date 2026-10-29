---
title: KOTH client (HUD panel, boundary, fireworks)
covers:
  - ModScripts/GVK_KothClient.cs
scripts:
  - ModScripts/GVK_KothClient.cs: Client only: KOTH capture panel, zone boundary and fireworks from the GVK KOTH plugin
---

# KOTH client (HUD panel, boundary, fireworks)

The player side of the GVK KOTH Torch plugin ([GV-Server-Mods/GVK-KOTH-Plugin](https://github.com/GV-Server-Mods/GVK-KOTH-Plugin), hub #741). The plugin runs KOTH on the server; this script only shows it. Without the plugin it receives nothing and draws nothing.

## Zone state from the server
- Listens on network channel `47410` (`RegisterSecureMessageHandler`, server messages only). The format is in the plugin's `docs/CLIENT_PROTOCOL.md`.
- About once a second while a KOTH is open (every 10 seconds otherwise) the plugin sends every open zone (name, position, radius, bar %, players counted) and the next slot's zone with its start time. Each message replaces the whole list in `KothClientData`.
- No message for 30 seconds (plugin stopped or not installed): the list is cleared.
- `KothClientData` is shared with the `/gvk` navigation HUD, which shows KOTH markers from it (see [Zone navigation HUD](zone-nav-hud.md)).

## Capture panel
- Shown while the player is inside an open KOTH: name, bar %, a 20-step bar and the number of players counted. Left side of the screen, under Text HUD API.
- Rebuilt every 10 ticks, and the text only when the %, the count or the zone changes.

## Zone boundary
- A faint orange wireframe sphere at the capture radius, drawn only when the camera is within the radius plus 1 km. Zones are visual only; nothing about them is a safe zone.
- For NPC zones (Flying KOTH) the server sends the grid's entity id, and the sphere and the inside-zone check follow that grid every frame while it is loaded on the client, so a moving zone's boundary moves smoothly. If the grid isn't loaded, they use the last position the server sent (about once a second).

## Fireworks
- When a capture bar completes, players within 5 km see 8 vanilla firework bursts (`Explosion_Firework_*`) above the zone over a few seconds.

## Performance notes
- Parsing runs once per message (about once a second). Per-tick work is the firework queue (empty almost always) and a distance-squared check against open zones every 10 ticks.
- The boundary is drawn in `Draw()` only near an open zone.
