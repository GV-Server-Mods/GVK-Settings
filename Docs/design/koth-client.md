---
title: KOTH client (HUD panel, boundary, fireworks)
covers:
  - ModScripts/GVK_KothClient.cs
  - ModScripts/GVK_ScreenLayer.cs
scripts:
  - ModScripts/GVK_KothClient.cs: Client only: KOTH capture panel, zone boundary and fireworks from the GVK KOTH plugin
checked: 2026-11-24
---

# KOTH client (HUD panel, boundary, fireworks)

The player side of the GVK KOTH Torch plugin ([GV-Server-Mods/GVK-KOTH-Plugin](https://github.com/GV-Server-Mods/GVK-KOTH-Plugin), hub #741). The plugin runs KOTH on the server; this script only shows it. Without the plugin it receives nothing and draws nothing.

## Zone state from the server
- Listens on network channel `47410` (`RegisterSecureMessageHandler`, server messages only). The format is in the plugin's `docs/CLIENT_PROTOCOL.md`.
- About once a second while a KOTH is open (every 10 seconds otherwise) the plugin sends every open zone (name, position, radius, bar %, players counted) and the next slot's zone with its start time. Each message replaces the whole list in `KothClientData`.
- No message for 30 seconds (plugin stopped or not installed): the list is cleared.
- `KothClientData` is shared with the `/gvk` navigation HUD, which shows KOTH markers from it (see [Zone navigation HUD](zone-nav-hud.md)). On the satellite map's game screen, pointing at one says whether it is open now or opens next.

## Capture panel
- Shown while the player is inside an open KOTH: name, bar %, a 20-step bar and the number of players counted, on the left of the screen. Drawn with the game's screen API from `Draw()` (`GvkScreenLayer`, see [Zone navigation HUD](zone-nav-hud.md); its sprites and text only, not the pixel-snapped rectangles the territory tint uses), at the text size `GvkScreenLayer.MeasureTextScale` gives, and hidden with the HUD and while the satellite map is open (`KothClientData.MapOpen`, set each tick by the nav HUD). A draw error turns the panel off for the session.
- Rebuilt every 10 ticks.

## Zone boundary
- A faint wireframe sphere at the capture radius, drawn only when the camera is within the radius plus 1 km. Zones are visual only; nothing about them is a safe zone.
- KOTH markers and the sphere take the KOTH colour from the nav HUD's **Colors** setting (`GvkPalette.Koth`): orange in Standard, pink in Red-green safe, violet in Blue-yellow safe.
- For NPC zones (Flying KOTH) the server sends the grid's entity id, and the sphere and the inside-zone check follow that grid every frame while it is loaded on the client, so a moving zone's boundary moves smoothly. If the grid isn't loaded, they use the last position the server sent (about once a second).

## Fireworks
- When a capture bar completes, players within 5 km see 8 vanilla firework bursts (`Explosion_Firework_*`) above the zone over a few seconds.

## Performance notes
- Parsing runs once per message (about once a second). Per-tick work is the firework queue (empty almost always) and a distance-squared check against open zones every 10 ticks.
- The boundary is drawn in `Draw()` only near an open zone.
