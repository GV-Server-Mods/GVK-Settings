# Credits
https://steamcommunity.com/sharedfiles/filedetails/?id=2781522559

Credit to Zantulo for letting me use his Lifted Wheels Suspension mod https://steamcommunity.com/sharedfiles/filedetails/?id=2727185097

~~Credit to Clockwork 168 for letting me use some of his component assets from the Industrial Overhaul mod: https://steamcommunity.com/sharedfiles/filedetails/?id=2344068716~~ (removed after Contact update)

Credit to TwitchingPsycho for the animated safezone generator: https://steamcommunity.com/sharedfiles/filedetails/?id=2202391036

Credit to RavenBolt for the 3x3x3 SG H2 tank: https://steamcommunity.com/sharedfiles/filedetails/?id=540440994

~~Credit to Nerd e1 for vanilla damage fix 1): https://steamcommunity.com/sharedfiles/filedetails/?id=3051216185~~ (removed for now)

Incorporates changes (With Klime's permission) from:
https://steamcommunity.com/sharedfiles/filedetails/?id=1871733117
https://steamcommunity.com/sharedfiles/filedetails/?id=2533952116
https://steamcommunity.com/sharedfiles/filedetails/?id=1844150178

# Main Functions
## Block Rebalancing
Scripts: GVK_ArmorBalance.cs

Description: TBD.
## No PVP in Zone 0
Scripts: GVK_NoPVPZone.cs

Description: This prevents pvp damage in Z0, based on an inputted GPS xyz coordinate and radius. 

NOTE: The downside to this mechanic is that players are unable to remove other faction grids that could prevent player safezone generators from activating. The workaround is to set the enemy check of the siegable safezone generators mod very low (1km)
## Limited Production (and weapons) Zones
Scripts: LimitedProdZone_Assembler.cs, LimitedProdZone_Beacon.cs, LimitedProdZone_ConveyorSorter.cs, etc.

Description: This provides the mechanic that shuts off large production and weapons in Z0, and allows them in higher zones.
## KOTH anti-abuse
Scripts: KOTHNoSafezone_Beacon.cs, KOTHNoSafezone_SafeZone.cs, KOTHNoSafezone_Projector.cs

Description: This shuts off player-built safezone generators (aka siegable shields) and projectors that are within a set distance of a KOTH block.
## Player-built safezone min radius
Scripts: Safezone3kmCheck_Beacon.cs, Safezone3kmCheck_SafeZone.cs

Description: Prevents players from activating a safezone generator within an increased distance from other blocks of the same kind, beyond its internal enemy radius check.
## Free Safezone H2
Scripts: SafezoneH2.cs

Description: Provides free unlimited H2 to players inside their built safezone.
# Audio Notes
- Use Yakitori Audio Converter for easy conversion to XWM
- D2 Sounds are stereo files that are non-directional. D3 sounds are mono files that are 3D directional in-game.
  - sounds need to be around -12dB volume than the D3 version
  - D3 Sounds MUST be mono format
- Sounds with attached DistantSounds must have a MaxDistance equal to the DistantSound MaxDistance
- Loopable sounds must be .wav 32-bit float 44100Hz, everything else should be .xwm 48kbps
- Add 1 second of silence to start of all distant sounds for extra effect, except for Loopable
- PreventSynchronization is the min time in ticks before the sound can be triggered again
