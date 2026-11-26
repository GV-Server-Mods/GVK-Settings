using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Input;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace GVK.Navigation
{
    /// <summary>
    /// Kharak Tactical Navigation Suite: HUD compass ribbon, corner minimap / tactical radar,
    /// zone status bar, and full-screen satellite map on the map key (default =), all drawn with the game's
    /// screen API (SE 1.211), Keen vanilla HUD marker icons, and a calibrated WorldToMapUV projection aligned
    /// to the KharakMap material (KharakMercator.dds, built by tools/kharak_map.py). Configurable with /gvk chat commands and the /gvk settings screen.
    /// </summary>
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public partial class GVK_ZoneNavCommand : MySessionComponentBase
    {
        // Planetary Constants for Kharak (PertamKharak). The planet's corner is at 0,0,0 in the save and its voxel
        // storage is 65536 m across (radius 30 km + 5% hills, rounded up to a power of two), so its centre is exactly
        // half that on every axis. The old hand-measured centre was about 820 m off and skewed every map marker.
        private static readonly Vector3D PLANET_CENTER = new Vector3D(32768.0, 32768.0, 32768.0);
        private const double PLANET_RADIUS = 30000.0;
        private static readonly Vector3 BASE_SOUTH = new Vector3(0f, 1f, 0f);

        // Origin Points
        private static readonly Vector3D CROSSROADS_BEACON = new Vector3D(62495.55, 28019.04, 37195.71);
        private static readonly Vector3D Z3_CENTER = new Vector3D(3569.33, 36772.94, 26952.63);

        // Zone spheres mirror the GVK-Derelicts [MES Zone] profiles (GVK-Universal-Zone.sbc), which are
        // canonical; keep in sync. Zone 0 is centred on Crossroads, Zones 1-3 on Z3_CENTER.
        private const double ZONE_0_RADIUS = 20000.0;
        private const double ZONE_1_RADIUS = 57043.0;
        private const double ZONE_2_RADIUS = 49300.0;
        private const double ZONE_3_RADIUS = 34000.0;

        // Texture Materials (screen API sprites)
        private static readonly MyStringId MATERIAL_SQUARE = MyStringId.GetOrCompute("Square");
        private static readonly MyStringId MATERIAL_MAP = MyStringId.GetOrCompute("KharakMap");
        private const double MAP_LON_SHIFT = 0.25; // Turns of longitude from -X to the map's left edge (WorldToMapUV)
        private static readonly MyStringId MATERIAL_RADAR_GRID = MyStringId.GetOrCompute("RadarGrid");

        // Tactical HUD Markers (from Textures\HUD\)
        private static readonly MyStringId MATERIAL_MARKER_GPS = MyStringId.GetOrCompute("marker_gps");
        // Known-site icons: white with a black outline baked in, so a tint keeps the outline
        private static readonly MyStringId MATERIAL_SITE_TRADE = MyStringId.GetOrCompute("POI_SiteTrade");     // Shop front
        private static readonly MyStringId MATERIAL_SITE_BASE = MyStringId.GetOrCompute("POI_SiteBase");       // Watchtower
        private static readonly MyStringId MATERIAL_SITE_DEPOT = MyStringId.GetOrCompute("POI_SiteDepot");     // Supply crate
        private static readonly MyStringId MATERIAL_SITE_CARRIER = MyStringId.GetOrCompute("POI_SiteCarrier"); // Ship
        private static readonly MyStringId MATERIAL_NAV_ARROW = MyStringId.GetOrCompute("nav_arrow");
        private static readonly MyStringId MATERIAL_SIGNAL_UP = MyStringId.GetOrCompute("signal_up");
        private static readonly MyStringId MATERIAL_SIGNAL_DOWN = MyStringId.GetOrCompute("signal_down");
        private static readonly MyStringId MATERIAL_SIGNAL_LEVEL = MyStringId.GetOrCompute("signal_level");

        // Satellite map filter buttons, top to bottom: the Satellite Map settings tab's toggles except the map key (KOTH
        // first), with their icons
        private static readonly int[] MAP_FILTER_IDS =
            { S_MAP_KOTH, S_MAP_OWN, S_MAP_SITES, S_MAP_TERRITORY, S_MAP_ZONES, S_MAP_GPS_LABELS, S_MAP_SIGNAL_LABELS,
              S_MAP_STACK };
        private static readonly MyStringId[] MAP_FILTER_ICONS =
        {
            MyStringId.GetOrCompute("POI_KOTH"),
            MyStringId.GetOrCompute("MapBtn_Signals"),
            MyStringId.GetOrCompute("POI_SiteBase"),
            MyStringId.GetOrCompute("MapBtn_TerritoryBoth"), // Territory and Safety Zones show their state icon instead
            MyStringId.GetOrCompute("MapBtn_ZoneBoth"),
            MyStringId.GetOrCompute("MapBtn_GpsLabels"),
            MyStringId.GetOrCompute("MapBtn_SignalLabels"),
            MyStringId.GetOrCompute("MapBtn_Stack")
        };
        // The two four-state overlays (OverlayState): lines and fade, lines only, fade only, off
        private static readonly string[] OVERLAY_STATE_NAMES = { "Lines + Fade", "Lines", "Fade", "Off" };
        private static readonly MyStringId[] ZONE_STATE_ICONS =
        {
            MyStringId.GetOrCompute("MapBtn_ZoneBoth"), MyStringId.GetOrCompute("MapBtn_ZoneLines"),
            MyStringId.GetOrCompute("MapBtn_ZoneFade"), MyStringId.GetOrCompute("MapBtn_ZoneOff")
        };
        private static readonly MyStringId[] TERRITORY_STATE_ICONS =
        {
            MyStringId.GetOrCompute("MapBtn_TerritoryBoth"), MyStringId.GetOrCompute("MapBtn_TerritoryLines"),
            MyStringId.GetOrCompute("MapBtn_TerritoryFade"), MyStringId.GetOrCompute("MapBtn_TerritoryOff")
        };
        private const double MAP_FILTER_BTN_W = 0.036;  // Button width (HUD units; height x aspect, so square)


        /// <summary>
        /// Graduation mark classification for the tactical HUD compass ribbon.
        /// </summary>
        private enum GraduationType
        {
            Cardinal,   // N, E, S, W (Top row: tactical amber, Bottom row: degree number)
            Ordinal,    // NE, SE, SW, NW (Top row: tactical ice cyan, Bottom row: degree number)
            MajorMark,  // 15°, 30°, 60°, etc. (Bottom row: degree number)
            MediumTen,  // 10°, 20°, 40°, etc. (Medium tick)
            MinorFive   // 5°, 25°, 35°, etc. (Minor tick)
        }

        /// <summary>
        /// Represents a graduation tick with dual-tier labels (Top: Cardinals/Ordinals, Bottom: Numeric marks).
        /// </summary>
        private struct CompassGraduation
        {
            public readonly float Offset;
            public readonly GraduationType Type;
            public readonly string TopLabel;
            public readonly string BottomLabel;

            public CompassGraduation(float offset, GraduationType type, string topLabel = null, string bottomLabel = null)
            {
                Offset = offset;
                Type = type;
                TopLabel = topLabel;
                BottomLabel = bottomLabel;
            }
        }

        // Tactical Option C HUD Graduations:
        // Top Row: Cardinals (N, E, S, W) and Ordinals (NE, SE, SW, NW)
        // Bottom Row: Major Numeric marks every 15° (0, 15, 30, 45, 60, etc.)
        // Intermediate: 10° Medium ticks and 5° Minor ticks
        private static readonly CompassGraduation[] COMPASS_GRADUATIONS = new CompassGraduation[]
        {
            new CompassGraduation(-1.00000f, GraduationType.Cardinal, "N", "0"),     // 0°
            new CompassGraduation(-0.97222f, GraduationType.MinorFive, null, null),   // 5°
            new CompassGraduation(-0.94444f, GraduationType.MediumTen, null, null),   // 10°
            new CompassGraduation(-0.91667f, GraduationType.MajorMark, null, "15"),   // 15°
            new CompassGraduation(-0.88889f, GraduationType.MediumTen, null, null),   // 20°
            new CompassGraduation(-0.86111f, GraduationType.MinorFive, null, null),   // 25°
            new CompassGraduation(-0.83333f, GraduationType.MajorMark, null, "30"),   // 30°
            new CompassGraduation(-0.80556f, GraduationType.MinorFive, null, null),   // 35°
            new CompassGraduation(-0.77778f, GraduationType.MediumTen, null, null),   // 40°
            new CompassGraduation(-0.75000f, GraduationType.Ordinal, "NE", "45"),     // 45°
            new CompassGraduation(-0.72222f, GraduationType.MediumTen, null, null),   // 50°
            new CompassGraduation(-0.69444f, GraduationType.MinorFive, null, null),   // 55°
            new CompassGraduation(-0.66667f, GraduationType.MajorMark, null, "60"),   // 60°
            new CompassGraduation(-0.63889f, GraduationType.MinorFive, null, null),   // 65°
            new CompassGraduation(-0.61111f, GraduationType.MediumTen, null, null),   // 70°
            new CompassGraduation(-0.58333f, GraduationType.MajorMark, null, "75"),   // 75°
            new CompassGraduation(-0.55556f, GraduationType.MediumTen, null, null),   // 80°
            new CompassGraduation(-0.52778f, GraduationType.MinorFive, null, null),   // 85°
            new CompassGraduation(-0.50000f, GraduationType.Cardinal, "E", "90"),     // 90°
            new CompassGraduation(-0.47222f, GraduationType.MinorFive, null, null),   // 95°
            new CompassGraduation(-0.44444f, GraduationType.MediumTen, null, null),   // 100°
            new CompassGraduation(-0.41667f, GraduationType.MajorMark, null, "105"),  // 105°
            new CompassGraduation(-0.38889f, GraduationType.MediumTen, null, null),   // 110°
            new CompassGraduation(-0.36111f, GraduationType.MinorFive, null, null),   // 115°
            new CompassGraduation(-0.33333f, GraduationType.MajorMark, null, "120"),  // 120°
            new CompassGraduation(-0.30556f, GraduationType.MinorFive, null, null),   // 125°
            new CompassGraduation(-0.27778f, GraduationType.MediumTen, null, null),   // 130°
            new CompassGraduation(-0.25000f, GraduationType.Ordinal, "SE", "135"),    // 135°
            new CompassGraduation(-0.22222f, GraduationType.MediumTen, null, null),   // 140°
            new CompassGraduation(-0.19444f, GraduationType.MinorFive, null, null),   // 145°
            new CompassGraduation(-0.16667f, GraduationType.MajorMark, null, "150"),  // 150°
            new CompassGraduation(-0.13889f, GraduationType.MinorFive, null, null),   // 155°
            new CompassGraduation(-0.11111f, GraduationType.MediumTen, null, null),   // 160°
            new CompassGraduation(-0.08333f, GraduationType.MajorMark, null, "165"),  // 165°
            new CompassGraduation(-0.05556f, GraduationType.MediumTen, null, null),   // 170°
            new CompassGraduation(-0.02778f, GraduationType.MinorFive, null, null),   // 175°
            new CompassGraduation(0.00000f, GraduationType.Cardinal, "S", "180"),    // 180°
            new CompassGraduation(0.02778f, GraduationType.MinorFive, null, null),   // 185°
            new CompassGraduation(0.05556f, GraduationType.MediumTen, null, null),   // 190°
            new CompassGraduation(0.08333f, GraduationType.MajorMark, null, "195"),   // 195°
            new CompassGraduation(0.11111f, GraduationType.MediumTen, null, null),   // 200°
            new CompassGraduation(0.13889f, GraduationType.MinorFive, null, null),   // 205°
            new CompassGraduation(0.16667f, GraduationType.MajorMark, null, "210"),   // 210°
            new CompassGraduation(0.19444f, GraduationType.MinorFive, null, null),   // 215°
            new CompassGraduation(0.22222f, GraduationType.MediumTen, null, null),   // 220°
            new CompassGraduation(0.25000f, GraduationType.Ordinal, "SW", "225"),     // 225°
            new CompassGraduation(0.27778f, GraduationType.MediumTen, null, null),   // 230°
            new CompassGraduation(0.30556f, GraduationType.MinorFive, null, null),   // 235°
            new CompassGraduation(0.33333f, GraduationType.MajorMark, null, "240"),   // 240°
            new CompassGraduation(0.36111f, GraduationType.MinorFive, null, null),   // 245°
            new CompassGraduation(0.38889f, GraduationType.MediumTen, null, null),   // 250°
            new CompassGraduation(0.41667f, GraduationType.MajorMark, null, "255"),   // 255°
            new CompassGraduation(0.44444f, GraduationType.MediumTen, null, null),   // 260°
            new CompassGraduation(0.47222f, GraduationType.MinorFive, null, null),   // 265°
            new CompassGraduation(0.50000f, GraduationType.Cardinal, "W", "270"),     // 270°
            new CompassGraduation(0.52778f, GraduationType.MinorFive, null, null),   // 275°
            new CompassGraduation(0.55556f, GraduationType.MediumTen, null, null),   // 280°
            new CompassGraduation(0.58333f, GraduationType.MajorMark, null, "285"),   // 285°
            new CompassGraduation(0.61111f, GraduationType.MediumTen, null, null),   // 290°
            new CompassGraduation(0.63889f, GraduationType.MinorFive, null, null),   // 295°
            new CompassGraduation(0.66667f, GraduationType.MajorMark, null, "300"),   // 300°
            new CompassGraduation(0.69444f, GraduationType.MinorFive, null, null),   // 305°
            new CompassGraduation(0.72222f, GraduationType.MediumTen, null, null),   // 310°
            new CompassGraduation(0.75000f, GraduationType.Ordinal, "NW", "315"),     // 315°
            new CompassGraduation(0.77778f, GraduationType.MediumTen, null, null),   // 320°
            new CompassGraduation(0.80556f, GraduationType.MinorFive, null, null),   // 325°
            new CompassGraduation(0.83333f, GraduationType.MajorMark, null, "330"),   // 330°
            new CompassGraduation(0.86111f, GraduationType.MinorFive, null, null),   // 335°
            new CompassGraduation(0.88889f, GraduationType.MediumTen, null, null),   // 340°
            new CompassGraduation(0.91667f, GraduationType.MajorMark, null, "345"),   // 345°
            new CompassGraduation(0.94444f, GraduationType.MediumTen, null, null),   // 350°
            new CompassGraduation(0.97222f, GraduationType.MinorFive, null, null)    // 355°
        };

        // Active HUD Waypoint (GPS point, real broadcast signal, KOTH or known site)
        private struct ActiveHudWaypoint
        {
            public MyStringId Sprite;
            public string Name;
            public Vector3D Coords;
            public Color DisplayColor;
            public double DistanceMeters;
            // UV computed once per background scan; reused by minimap and full-map every frame.
            public Vector2 MapUV;
            public bool IsSignal;
            public bool IsOwnOrFaction; // Signals only: owned by the player or shared with their faction
            public double PlanetaryElevation;
            public int KothState;      // 0 = not a KOTH, KOTH_OPEN (flashes) or KOTH_NEXT (steady, dimmed)
            public double KothRadius;  // KOTH capture radius in metres, for the satellite map ring
            public double KothMapRadius; // That radius as a fraction of the map width (UV units)
            public byte Relation;      // Signals only: REL_OWN, REL_FACTION, REL_FRIENDLY, REL_HOSTILE, REL_NPC or REL_NEUTRAL
            public bool IsNpcOwned;    // Signals only: the grid's owner is in an NPC faction (hostile or not)
            public Vector3D GridOrigin; // Signals only: the broadcasting grid's origin, where MES places a static encounter
            public byte SiteKind;      // 0 = not a known site, else SITE_BASE, SITE_TRADE, SITE_REFINERY, SITE_DEPOT or SITE_CARRIER
            public int SiteIndex;      // Sites only: index into _knownSites
            public double StandInRadiusSq; // Sites only: a GPS point this close (squared metres) can stand in for the site
            public bool HasStandIn;    // Sites only, per scan: a GPS point in the list stands in for this site
            public string KindText;    // Sites only: the hover card's "what it is", from the server's site status
            public int StandsInFor;    // GPS points, per scan: 1 + the _knownSites index this marks, 0 = none
            public int FoldedInto;     // NPC signals, per scan: 1 + the _knownSites index whose icon shows it, 0 = none
            public bool HasSignal;     // Sites only, per scan: the site's own NPC signal is in range (folded into the icon)
        }

        private const byte REL_NEUTRAL = 0;
        private const byte REL_OWN = 1;
        private const byte REL_FACTION = 2;
        private const byte REL_HOSTILE = 3;
        private const byte REL_NPC = 4;
        private const byte REL_FRIENDLY = 5; // Another faction the player is Friendly with: by reputation for an NPC faction

        private const byte SITE_BASE = GvkKnownSites.BASE;       // Kinds and the site table: GVK_SiteStatus.cs
        private const byte SITE_TRADE = GvkKnownSites.TRADE;
        private const byte SITE_REFINERY = GvkKnownSites.REFINERY;
        private const byte SITE_DEPOT = GvkKnownSites.DEPOT;
        private const byte SITE_CARRIER = GvkKnownSites.CARRIER;
        private static Color SITE_WHITE => GvkPalette.Neutral;                     // Trade stations, and neutral or unowned sites
        private static readonly Color SITE_DIM = GvkUi.Dim;                        // No alliance Base on a Base Site, or not spawned
        private static Color SITE_FRIENDLY => GvkPalette.Friendly;  // Same colour as faction signals
        private static Color SITE_HOSTILE => GvkPalette.Hostile;    // Same colour as hostile signals
        private const int SITE_FRIENDLY_REP = 500;   // GVK's Friendly standing (contracts need 500+)
        private const int SITE_HOSTILE_REP = -500;   // Below this the game counts a faction hostile
        private const float SITE_ICON_SCALE = 0.8f;  // Site icons are a little smaller than GPS and signal markers
        private const double SITE_GRID_DEDUPE_DIST_SQ = 25.0 * 25.0;   // An NPC grid whose origin is this close is the site itself
        private const double COMPASS_SITE_MAX_DIST = 20000.0;     // The compass shows known sites only within 20 km
        private const double TERRITORY_POINT_SPACING_M = 100.0;   // Ground distance between the ring's sample points
        private const int TERRITORY_MIN_POINTS = 64;
        private const int TERRITORY_MAX_POINTS = 2048;
        // The screen API blends with straight alpha: set A alone (WithAlpha). Scaling all four channels, as the first build did,
        // drew a 16% tint as a 16% black wash, so overlaps and Contested stripes read grey (owner, 2026-11-24, in game)
        // Territory rings are dashed lines drawn like the safety zone lines, with even gaps (owner, 2026-11-25: "I said I
        // wanted them consistent other than they are dotted", "do the alliance zones the same way just with some equal
        // spacing between segments"): same thickness, alpha and glow. Until then each ring was 0.0027 square dots 0.008
        // apart at 0.95 alpha on a dark backing. Dashes are measured along the ring from its first point, so they stay put
        // while the map pans.
        private const double TERRITORY_DASH = 0.010; // Dash length on screen (HUD units, X scale; about 10 px at 1080p)
        private const double TERRITORY_GAP = 0.006;  // Gap between dashes
        // Edge bands (owner, 2026-11-25): a soft band of colour just inside each border, fading out inward, in place of
        // tinting whole areas. Every whole-area tint tried (blend, stripes, hatch, dither, additive glow, shade) washed out
        // or cluttered the map ("I like that edge band but the glow needs to extend further"; picked 45 px). The middle of
        // every territory and zone is left as the plain map. The bands are baked (tools/kharak_bands.py, GvkMapBands):
        // the screen API draws only rectangles, so every live band tried (stretched gradients, stacked layers, triangles,
        // a comb of lines) seamed on curves or looked busy (owner, 2026-11-25: "I'm leaning toward baking them").
        private const float TERRITORY_BAND_ALPHA = 0.40f;
        private const float ZONE_BAND_ALPHA = 0.40f;

        // Safety zone overlay (owner, 2026-11-24: zones off the map picture into "an overlay to turn on/off", "lines plus
        // faint tint", with a toggle for the tint). Seen from the ground, Zone 3 is a disc round Z3_CENTER, Zone 2 the
        // band past it to 49.3 km, which wraps round most of the planet, and Zone 1 the region beyond that round
        // Crossroads, with Zone 0 inside it. Zone 1's own 57 km sphere edge lies inside Zone 0, so it is never a boundary.
        // The rings are Zone 0's edge, Zone 1's edge (where Zone 2 starts) and Zone 3's edge, each line in that zone's
        // colour. With Safety Zone Tint on, each line has one baked edge band, in its own colour on its own zone's side,
        // so Zone 2 stays clear (owner, 2026-11-25: "I don't need a yellow fade touching the green fade").
        // The lines are see-through and glow (owner, 2026-11-25: "I want them more transparent, they completely block
        // everything underneath", "I thought the glow was supposed to just add color brightness"): a faint colour line
        // with the same colour added over it (GVK_Additive), so the map shows through and the line brightens it. They
        // are as thick as the territory dots were. Until then they were 0.0022 thick at 0.9 alpha, which hid the map.
        // GVK_Additive is white with alpha 0: the screen API draws colour + map * (1 - alpha) without premultiplying the
        // texture, so the map stays and the colour times the sprite's alpha is added. An all-alpha-0 uncompressed DDS
        // drew nothing in game, so it is BC7 with one opaque 4x4 block in a corner that the material's UV window
        // (texels 2-5) never reaches.
        private static readonly MyStringId MATERIAL_ADDITIVE = MyStringId.GetOrCompute("GVK_Additive");
        private const float ZONE_LINE_ALPHA = 0.35f;              // The colour line, blended normally
        private const float ZONE_LINE_GLOW = 0.6f;                // The added colour over it
        private const double ZONE_MAP_LINE_W = 0.0027;           // Line thickness on the map, zones and territories (HUD units, X scale)
        // Points closer than this on screen merge into one segment (about 19 px at 1080p). It was 0.006, then 0.012, both
        // 2026-11-25 (owner, at 12 px: "could actually make them longer"). A 19 px chord strays from a zone ring by about
        // 0.3 px zoomed out, and from the smallest territory ring (10 km) by about 0.6 px.
        private const double ZONE_MAP_MIN_SEG = 0.020;
        private static readonly int[] ZONE_OVERLAY_ZONE = { 0, 1, 3 }; // Zone number of each ring's line and band
        private static readonly Vector3D[] ZONE_RING_CENTER = { CROSSROADS_BEACON, Z3_CENTER, Z3_CENTER };
        private static readonly double[] ZONE_RING_RADIUS = { ZONE_0_RADIUS, ZONE_2_RADIUS, ZONE_3_RADIUS };
        private readonly List<Vector2>[] _zoneRingUV = { new List<Vector2>(), new List<Vector2>(), new List<Vector2>() };
        private bool _zoneOverlayBuilt;

        private const int KOTH_OPEN = 1;
        private const int KOTH_NEXT = 2;
        private static Color KOTH_COLOR => GvkPalette.Koth;

        // Default GPS entries populated for joining players. Colours are the palette's at the time they are added, as the
        // map shows the same places: Zone 0 Friendly, Zone 3 Hostile, trade stations Neutral white, KOTH sites KOTH.
        private const byte GPS_ZONE0 = 0, GPS_ZONE3 = 1, GPS_STATION = 2, GPS_KOTH = 3;

        private static Color GetDefaultGpsColor(byte kind)
        {
            return kind == GPS_ZONE0 ? GvkPalette.Friendly : kind == GPS_ZONE3 ? GvkPalette.Hostile : kind == GPS_KOTH ? GvkPalette.Koth : GvkPalette.Neutral;
        }

        private struct DefaultGpsEntry
        {
            public string Name;
            public string Description;
            public Vector3D Coords;
            public bool ShowOnHud;
            public bool AlwaysVisible;
            public byte Kind;          // GPS_ZONE0, GPS_ZONE3, GPS_STATION or GPS_KOTH: GetDefaultGpsColor
            public DefaultGpsEntry(string name, string desc, Vector3D coords, bool showOnHud, bool alwaysVis, byte kind)
            {
                Name = name;
                Description = desc;
                Coords = coords;
                ShowOnHud = showOnHud;
                AlwaysVisible = alwaysVis;
                Kind = kind;
            }
        }

        private static readonly DefaultGpsEntry[] DEFAULT_KHARAK_GPS = new DefaultGpsEntry[]
        {
            new DefaultGpsEntry("Zone 0", "Zone 0 Safe Starter Hub", new Vector3D(62495.55, 28019.04, 37195.71), true, true, GPS_ZONE0),
            new DefaultGpsEntry("Zone 3", "Zone 3 Deep Desert Center", new Vector3D(3569.33, 36772.94, 26952.63), true, false, GPS_ZONE3),
            new DefaultGpsEntry("Sevastopol", "Coalition Trade Station", new Vector3D(2990.66, 30484.89, 34060.65), false, false, GPS_STATION),
            new DefaultGpsEntry("Skyport", "Aerial Trade Station", new Vector3D(40585.81, 59619.14, 46601.96), false, false, GPS_STATION),
            new DefaultGpsEntry("Mastodon", "Southern Trade Station", new Vector3D(33475.48, 5361.51, 20848.44), false, false, GPS_STATION),
            new DefaultGpsEntry("Rusty's", "Starter Trade Outpost", new Vector3D(61686.17, 27088.08, 38160.96), false, false, GPS_STATION),
            new DefaultGpsEntry("Coalition Base", "Coalition Headquarters", new Vector3D(61784.00, 28043.10, 38552.08), false, false, GPS_STATION),
            new DefaultGpsEntry("KOTH Khar Toba", "King of the Hill Site", new Vector3D(4115.21, 38891.59, 26152.68), false, false, GPS_KOTH),
            new DefaultGpsEntry("KOTH Kalash Site", "King of the Hill Site", new Vector3D(8815.53, 14612.65, 35353.97), false, false, GPS_KOTH),
            new DefaultGpsEntry("KOTH Crashed Starship", "King of the Hill Site", new Vector3D(25893.61, 61095.89, 25703.05), false, false, GPS_KOTH)
        };

        // 1. Custom Programmatic Tactical HUD Compass Elements
        private bool showCompass = true;
        private bool showCompassGps = true;
        private bool showCompassSignals = true;
        private bool showCompassOwnSignals = true;
        private float compassScale = 1.0f;
        private double compassOffsetX = 0.0;
        private double compassOffsetY = 1.0;
        private const int COMPASS_MAX_TICKS = 25;
        private const int COMPASS_MAX_LABELS = 30;
        private const int COMPASS_MAX_WAYPOINTS = 16;

        // HUD draw lists (vanilla screen API, SE 1.211). Laid out in HUD units (-1..1, Y up, whole window),
        // the units the HUD had under Text HUD API, which this mod used until 2026-11.
        // Same layout math in the same units, so the two should match. Drawn in this order, as created before.
        private readonly GvkScreenLayer _ssCompass = new GvkScreenLayer();
        private readonly GvkScreenLayer _ssMinimap = new GvkScreenLayer();
        private readonly GvkScreenLayer _ssZoneBar = new GvkScreenLayer();
        private const int MINIMAP_MAX_MARKERS = 30;
        private const int MINIMAP_MAX_SITES = 48;          // Known sites count apart, so they never push out GPS points or signals
        private float _minimapLayoutBgWidth = -1f;         // Minimap card width as ApplyMinimapScale last laid it out
        private bool _hudLayoutInit;                       // First-tick layout done (see UpdateBeforeSimulation)
        private bool _ssHudDrawFailed;                     // A draw error turns the HUD off for the session
        private bool _ssMapDrawFailed;                     // And the satellite map
        private long _ssRangeKey = long.MinValue;          // Radar range label, re-formatted only when it changes
        private string _ssRangeText;
        private long _ssLowerKmKey = long.MinValue;
        private string _ssLowerKmText;
        private long _ssHigherKmKey = long.MinValue;
        private string _ssHigherKmText;
        private static readonly string[] ZONE_TITLES =
        {
            "[ ZONE 0: SAFE HUB ]", "[ ZONE 1: PVE FRONTIER ]", "[ ZONE 2: OPEN PVP ]", "[ ZONE 3: GAALSIEN HEART ]"
        };
        private static readonly string[] ZONE_SHORT_NAMES = { "Zone 0", "Zone 1", "Zone 2", "Zone 3" };
        private static readonly string[] ZONE_DIST_LABELS = { "Zone 0: ", "Zone 1: ", "Zone 2: ", "Zone 3: " };
        private static readonly Color SPACE_TEXT_COLOR = GvkUi.Space;
        private static readonly Color[] ZONE_TEXT_COLORS = GvkPalette.ZoneText; // The palette refills this array in place
        private static readonly Color ZONE_LABEL_COLOR = GvkUi.Text;
        private static readonly Color ZONE_SEPARATOR_COLOR = GvkUi.Separator;
        private bool _ssMaterialsChecked = false;
        private static readonly string[] _ssDistanceTenths = new string[100]; // "0.0k".."9.9k", filled on first use
        private static readonly string[] _ssDistanceWhole = new string[1000]; // "10k".."999k", filled on first use

        // Satellite map as a game screen (screen API): zoom about the cursor, drag to pan, hover for details,
        // right-click to add a GPS. The view is the map UV at the frame centre and a zoom of 1 (whole map) to 3.
        private GvkMapScreen _mapScreen;
        private readonly GvkScreenLayer _ssMap = new GvkScreenLayer();
        private readonly GvkScreenLayer _ssMapTip = new GvkScreenLayer();
        private const float MAP_MAX_ZOOM = 3f;          // The 4096 px map picture is about 1:1 here at 1080p
        private const double MAP_ZOOM_STEP = 1.25;      // Per mouse-wheel notch
        private const double MAP_HOVER_HALF = 0.009;    // Hover box half width around a marker (height scaled by aspect)
        private const float MAP_LABEL_SCALE = 0.60f;    // HUD text scale of the map labels
        private struct MapHit
        {
            public int Wp;
            public double MinX, MaxX, MinY, MaxY;
        }
        private readonly List<MapHit> _mapHits = new List<MapHit>(128); // Markers and labels drawn, for hover
        private readonly List<string> _ssMapLabelSrc = new List<string>(64);  // Name each cached label was cut from
        private readonly List<string> _ssMapLabelText = new List<string>(64);
        private readonly List<float> _ssMapLabelWidth = new List<float>(64);
        private readonly string[] _ssMapOverflowText = new string[100];
        private static readonly string[] MAP_BORDER_LABELS = { "Z1 Border in: ", "PvP Border in: ", "Z3 Border in: ", "Core Dist: " };
        private static readonly Color MAP_HEADER_GREY = GvkUi.TextDim;
        private static readonly Color MAP_HINT_COLOR = GvkUi.TextDim;
        private const string MAP_CONTROLS_HINT = "Wheel: zoom   |   Drag: pan   |   Right-click: add, hide or delete a GPS   |   Point at the map for details";
        private const string MAP_CONTROLS_HINT_TERRITORY = "Wheel: zoom   |   Drag: pan   |   Right-click: add, hide or delete a GPS   |   Point for details   |   Two colours: contested territory";
        private const string MAP_TITLE_TEXT = "KHARAK TACTICAL SATELLITE MAP";
        private const string MAP_SECTOR_TEXT = " | Current Sector: ";
        private const string MAP_TERRITORY_TEXT = " | Territory: ";
        private const string MAP_CONTESTED_TEXT = " | Contested: ";
        private const string CONTESTED_LABEL = "Contested territory";
        private const float MAP_HINT_SCALE = 0.62f;     // HUD text scale of the controls line (was 0.46, too small)

        // Right-click menu: asks before adding a GPS at the click, or offers to hide or delete the GPS clicked
        private readonly GvkScreenLayer _ssMapMenu = new GvkScreenLayer();
        private bool _mapMenuOpen;
        private bool _mapMenuRemove;        // False: add a GPS at the click; true: hide or delete the GPS clicked
        private Vector2D _mapMenuAt;        // Click point, HUD units
        private double _mapMenuU;           // Map UV of the click, for adding
        private double _mapMenuV;
        private string _mapMenuGpsName;     // GPS to remove, found again by name and position
        private Vector3D _mapMenuGpsCoords;
        private string _mapMenuTitle;
        private MapHit _mapMenuOk;          // Button boxes from the last menu build: Add GPS, or Hide for a GPS
        private MapHit _mapMenuDelete;      // Delete, on a GPS's menu only
        private int _mapPointNext = 1;      // Next "Map Point N", so quick adds don't wait on the server's GPS list

        // Settings button at the right end of the map header; drawn every frame so it lights up under the cursor
        private readonly GvkScreenLayer _ssMapButton = new GvkScreenLayer();
        private MapHit _mapSettingsBox;
        private readonly MapHit[] _mapFilterBoxes = new MapHit[MAP_FILTER_IDS.Length]; // Filter buttons right of the frame
        private float _mapSettingsScale;
        private const string MAP_SETTINGS_TEXT = "Settings";
        private Vector3D _mapPlayerPos;
        private float _mapZoom = 1f;
        private double _mapViewU = 0.5;
        private double _mapViewV = 0.5;
        private float _ssMapW;              // Frame size in HUD units from the last build; 0 before the first
        private float _ssMapH;
        private Vector2 _ssMapViewport;
        private float _ssMapTextScale;
        private bool _mapDragging;
        private Vector2D _mapDragLast;
        private bool _mapKeyLatch;          // Ignore the map key until the press that opened the map is let go
        private int _gpsRescanTicks;        // Re-read the GPS list this many ticks after adding one from the map
        private long _ssMapCrossKey = long.MinValue;
        private string _ssMapCrossText;
        private long _ssMapBorderKey = long.MinValue;
        private string _ssMapBorderText;
        private long _ssTipDistKey = long.MinValue;
        private string _ssTipDistText;
        private string _ssMapCloseKeyLabel;
        private string _ssMapCloseText;

        // 2. Zone Bar Elements (Docked directly beneath Corner Minimap in Top-Right)
        public enum ZoneBarMode
        {
            CompassDock = 0,
            RadarDock = 1,
            Off = 2
        }

        private ZoneBarMode zoneBarMode = ZoneBarMode.CompassDock;

        // 3. Corner Minimap Elements (Top-Right: true 2:1 ratio for KharakMap, enlarged +20%)
        private Vector2D minimapPosition = new Vector2D(0.81, 0.73);
        private Vector2D minimapSize = new Vector2D(0.312, 0.277); // Dynamically set with aspect ratio (+20%)
        private bool showMinimap = true;
        private bool showMinimapGps = true;
        private bool showMinimapSignals = true;
        private bool showMinimapOwnSignals = true;
        private float minimapScale = 1.0f;
        private double minimapOffsetX = 1.0;
        private double minimapOffsetY = 1.0;

        public enum RadarScaleMode
        {
            Linear,
            Logarithmic
        }

        private RadarScaleMode radarScale = RadarScaleMode.Linear;
        private const double LOG_THIRD = 1.0 / 3.0;
        private double radarRangeMeters = 3000.0;
        private float cachedHudBkOpacity = -1f;
        private bool showRadarZoneLines = true;
        private bool showRadarFovLines = true;


        private struct HudPreset
        {
            public readonly string Name;
            public readonly double PosX;
            public readonly double PosY;

            public HudPreset(string name, double posX, double posY)
            {
                Name = name;
                PosX = posX;
                PosY = posY;
            }
        }

        private static readonly HudPreset[] CompassPresets = new HudPreset[]
        {
            new HudPreset("Top-Center (Default)", 0.0, 1.0),
            new HudPreset("Above Toolbar Center", 0.0, -0.71)
        };

        private static readonly HudPreset[] MinimapPresets = new HudPreset[]
        {
            new HudPreset("Top-Right (Default)", 1.0, 1.0),
            new HudPreset("Upper-Mid Right (Below WC HUD)", 1.0, 0.72),
            new HudPreset("Middle-Right", 1.0, 0.0),
            new HudPreset("Bottom-Right (Above Toolbar)", 0.67, -1.0),
            new HudPreset("Top-Left", -1.0, 1.0),
            new HudPreset("Upper-Mid Left", -1.0, 0.72),
            new HudPreset("Middle-Left", -1.0, 0.0),
            new HudPreset("Bottom-Left (Above Health)", -0.48, -1.0)
        };

        public class ZoneNavConfig
        {
            public bool ShowMinimap { get; set; } = true;
            public int RadarScale { get; set; } = 0;
            public double RadarRangeMeters { get; set; } = 3000.0;
            public float MinimapScale { get; set; } = 1.0f;
            public double MinimapOffsetX { get; set; } = 1.0;
            public double MinimapOffsetY { get; set; } = 1.0;
            public bool ShowMinimapGps { get; set; } = true;
            public bool ShowMinimapSignals { get; set; } = true;
            public bool ShowMinimapOwnSignals { get; set; } = true;
            public bool ShowMinimapKoth { get; set; } = true;
            public bool ShowMinimapSites { get; set; } = true;

            public bool ShowCompass { get; set; } = true;
            public bool ShowCompassKoth { get; set; } = true;
            public bool ShowMapKoth { get; set; } = true;
            public bool ShowCompassSites { get; set; } = true;
            public bool ShowMapSites { get; set; } = true;
            public bool ShowMapTerritories { get; set; } = true;   // The dotted rings
            public bool ShowTerritoryFade { get; set; } = true;   // Their baked edge bands
            public bool ShowMapZones { get; set; } = true;
            public bool ShowZoneTint { get; set; } = true;         // The zones' baked edge bands (kept its old name)
            public int ColorPalette { get; set; } = 0;   // GvkPalette: 0 Standard, 1 Red-green safe, 2 Blue-yellow safe
            public bool ShowCompassGps { get; set; } = true;
            public bool ShowCompassSignals { get; set; } = true;
            public bool ShowCompassOwnSignals { get; set; } = true;

            public bool ShowMapGpsLabels { get; set; } = true;
            public bool ShowMapSignalLabels { get; set; } = true;
            public bool ShowMapOwnSignals { get; set; } = true;
            public bool StackMapIcons { get; set; } = true;
            public int MapKey { get; set; } = (int)DEFAULT_MAP_KEY;
            public bool MapKeyShift { get; set; } = false;
            public bool MapKeyCtrl { get; set; } = false;
            public bool MapKeyAlt { get; set; } = false;
            public int MapKeyVersion { get; set; } = 0; // Missing in configs saved while the default was M
            public float CompassScale { get; set; } = 1.0f;
            public double CompassOffsetX { get; set; } = 0.0;
            public double CompassOffsetY { get; set; } = 1.0;

            public bool ShowZoneBar { get; set; } = true; // Legacy compat only — ZoneBarMode is the source of truth
            public int ZoneBarDockMode { get; set; } = 0;
            public int ZoneBarMode { get; set; } = 0;
            public int UpdateTickRate { get; set; } = 5;
            public bool ShowRadarZoneLines { get; set; } = true;
            public bool ShowRadarFovLines { get; set; } = true;
            // UseScreenApi (the old /gvk ss switch) is gone; XmlSerializer skips it in older config files

            public ZoneNavConfig() { }
        }

        private const string CONFIG_FILENAME = "GVK_ZoneNavConfig.xml";

        // 4. Interactive Full-Screen Satellite Map (map key, true 2:1 ratio)
        // Default key: =, free in the game's default bindings and on 60% keyboards. It was M until 2026-11-22,
        // which clashed with mirror mode; configs still on M from before then move to = once (MapKeyVersion).
        private const MyKeys DEFAULT_MAP_KEY = MyKeys.OemPlus;
        private const int MAP_KEY_VERSION = 1;
        private bool showFullMap = false;
        private MyKeys mapKey = DEFAULT_MAP_KEY;
        private bool mapKeyShift = false;
        private bool mapKeyCtrl = false;
        private bool mapKeyAlt = false;
        private string mapKeyLabel = "="; // e.g. "Ctrl+M"; rebuilt only when the binding changes

        // Dynamic State
        private int currentZoneIndex = 0;
        private double lastDistKm = 0.0;
        private double lastRemainingKm = 0.0; // to the next-higher zone
        private double lastToLowerKm = 0.0;   // to the next-lower zone
        private double lastDistZ3Km = 0.0;
        private bool inSpace = false; // outside Kharak's gravity well
        private int tickCounter = 0;
        private int updateTickRate = 5;
        private float playerHeadingRad = 0f;
        private bool hasCheckedDefaultGps = false;
        private bool _refreshMinimapNextFrame = false;
        private bool _refreshCompassNextFrame = false;
        private bool _fullMapNeedsRedraw = false;

        // Cached orientation matrix & planet north azimuth — recomputed only when player moves > 1m
        private Vector3D _lastCompassPos = Vector3D.Zero;
        private Matrix _cachedRelativeOffset = Matrix.Identity;
        private float _cachedNorthAzimuth = 0f;

        // Cached aspect ratio, to avoid per-frame camera queries
        private float cachedAspect = 16f / 9f;
        private Vector2 cachedViewportSize = Vector2.Zero;

        // Active HUD Waypoints Buffer
        private List<ActiveHudWaypoint> activeHudWaypoints = new List<ActiveHudWaypoint>();
        private readonly List<ActiveHudWaypoint> _knownSites = new List<ActiveHudWaypoint>(48); // Built once, see LoadKnownSites
        private bool[] _knownSiteHasStandIn = new bool[0];  // Per scan: a GPS point stands in for this site
        private bool[] _siteHasSignal = new bool[0];        // Per scan: the site's own NPC signal is in range
        private byte[] _siteSignalRel = new byte[0];        // Per scan: that signal's REL_* (live colour for trade stations)
        private string[] _siteSignalName = new string[0];   // Per scan: that signal's name, for the hover card
        private bool[] _siteStandInDrawn = new bool[0];     // Per view draw: this site's stand-in got a slot, so the site is skipped
        private bool _knownSitesLoaded;
        private int _siteStatusVersion = int.MinValue;      // GvkSiteStatus.Version last read into the arrays below
        private byte[] _siteState = new byte[0];            // GvkSiteStatus state per site
        private long[] _siteFaction = new long[0];          // Owner faction per site (0 = none)
        private string[] _siteKindText = new string[0];     // Hover text per site, rebuilt only when the status changes
        private readonly List<List<Vector2>> _territoryUV = new List<List<Vector2>>(2); // Ring points per territory (map UV)
        private double[] _territoryRadius = new double[0];  // Radius each ring was built for (0 = no size from the server yet)
        private Color[] _territoryColor = new Color[0];     // Standing colour per territory, every scan
        private Vector3D[] _territoryDir = new Vector3D[0]; // Planet centre to zone centre, unit (ground hover)
        private double[] _territoryCosA = new double[0];    // Cosine of the ring's angular radius on the ground (2 = none)
        private string[] _territoryName = new string[0];    // Alliance name per territory, every scan
        private string[] _territoryLabel = new string[0];   // "<name> territory", for the hover card
        private string[] _territoryUpper = new string[0];   // Upper-case name, for the zone bar title
        private string[] _territoryStanding = new string[0]; // "Friendly", "Hostile" or "Neutral", every scan
        private int _playerTerritoryMask;                   // Territories the player is inside (bit per territory), every scan
        private const byte STANDING_NEUTRAL = 0, STANDING_FRIENDLY = 1, STANDING_HOSTILE = 2;
        private static readonly string[] STANDING_WORDS = { "Neutral", "Friendly", "Hostile" };
        private List<IMyGps> cachedGpsList = new List<IMyGps>();
        private HashSet<IMyEntity> entityBuffer = new HashSet<IMyEntity>();
        private List<IMySlimBlock> blockBuffer = new List<IMySlimBlock>();

        // Cached static delegates — avoid per-tick lambda allocations in hot paths.
        // Mono/SE does NOT cache non-capturing lambdas the way modern .NET does.
        private static readonly Func<IMyEntity, bool> IsGridPredicate =
            e => e is IMyCubeGrid;
        private static readonly Func<IMySlimBlock, bool> IsBroadcastBlockPredicate =
            b => b.FatBlock is IMyBeacon || b.FatBlock is IMyRadioAntenna;

        // Comparison<T> is prohibited by the SE ModAPI whitelist on Mono.
        // Use a struct IComparer<T> singleton instead — zero allocation, whitelisted.
        private struct WaypointDistanceComparer : IComparer<ActiveHudWaypoint>
        {
            public int Compare(ActiveHudWaypoint a, ActiveHudWaypoint b)
                => a.DistanceMeters.CompareTo(b.DistanceMeters);
        }
        private static readonly WaypointDistanceComparer WaypointComparer = new WaypointDistanceComparer();


        // Compass tape visible waypoint entry (pre-sorted Painter's algorithm buffer)
        private struct CompassVisibleWp
        {
            public int WpIndex;
            public float ScreenOffset;
            public CompassVisibleWp(int wpIndex, float screenOffset)
            {
                WpIndex = wpIndex;
                ScreenOffset = screenOffset;
            }
        }
        private readonly List<CompassVisibleWp> _compassVisibleWps = new List<CompassVisibleWp>(16);

        // Satellite Map Marker Cluster (Deconfliction / Waterfall Stacking up to 5+)
        private struct MapCluster
        {
            public Vector2D Sum;      // Sum of member map offsets; Position = Sum / Count
            public Vector2D Position;
            public int Count;         // 0 once merged into another cluster
            public int Parent;        // Self, or the cluster this one was merged into
            public float LabelWidth;  // Widest member label
            public bool Labelled;     // False: every stack placement would overlap a nearer cluster's, so draw dots only
            public float DirY;        // Stack direction: -1 down, 1 up, 0 = default for its position
            public bool FlipX;        // Labels on the left of the icons
            public double NudgeY;     // Lone markers only: label shifted up or down from its icon
        }
        private List<MapCluster> _cachedMapClusters = new List<MapCluster>(32);

        // Map layout cache: rebuilt only when waypoints, label toggles or screen size change, not every redraw.
        private bool _mapLayoutDirty = true;
        private readonly List<int> _mapPlainWps = new List<int>(64);    // Waypoints drawn as a dot with no label
        private readonly List<int> _mapLabelWps = new List<int>(64);    // Labelled waypoints, nearest first
        private readonly List<int> _mapLabelRoots = new List<int>(64);  // Final cluster of each labelled waypoint
        private readonly List<float> _mapLabelWidths = new List<float>(64);   // Measured width of each labelled waypoint's name
        private readonly List<bool> _mapLabelHasText = new List<bool>(64);   // False: label toggled off, the icon stacks without a name
        private float _mapOverflowWidth;
        private double _mapTextHeight = 0.016; // Measured height of one label line
        private readonly List<int> _mapClusterShown = new List<int>(32);
        private bool showMapGpsLabels = true;
        private bool showMapSignalLabels = true;
        private bool showMapOwnSignals = true;
        private bool stackMapIcons = true; // False: every icon at its true position; stacks list names only
        private bool showMapKoth = true;
        private bool showMinimapKoth = true;
        private bool showCompassKoth = true;
        private bool showMapSites = true;
        private bool showMinimapSites = true;
        private bool showCompassSites = true;
        private bool showMapTerritories = true; // Territory dotted rings
        private bool showTerritoryFade = true;  // Territory edge bands
        private bool showMapZones = true;      // Safety zone lines (the map picture has none baked in since 2026-11-24)
        private bool showZoneTint = true;      // Safety zone edge bands
        private const int KOTH_RING_DOTS = 24;
        private static readonly double[] KOTH_RING_COS = BuildRingTable(true);
        private static readonly double[] KOTH_RING_SIN = BuildRingTable(false);
        private bool _kothFlashBright = true;


        private void LoadConfig()
        {
            try
            {
                if (MyAPIGateway.Utilities == null) return;
                if (MyAPIGateway.Utilities.FileExistsInLocalStorage(CONFIG_FILENAME, typeof(GVK_ZoneNavCommand)))
                {
                    using (var reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(CONFIG_FILENAME, typeof(GVK_ZoneNavCommand)))
                    {
                        string xml = reader.ReadToEnd();
                        if (!string.IsNullOrWhiteSpace(xml))
                        {
                            var cfg = MyAPIGateway.Utilities.SerializeFromXML<ZoneNavConfig>(xml);
                            if (cfg != null)
                            {
                                showMinimap = cfg.ShowMinimap;
                                radarScale = (RadarScaleMode)cfg.RadarScale;
                                radarRangeMeters = (cfg.RadarRangeMeters >= 500.0 && cfg.RadarRangeMeters <= 50000.0) ? cfg.RadarRangeMeters : 3000.0;
                                minimapScale = (cfg.MinimapScale >= HUD_SCALE_MIN && cfg.MinimapScale <= HUD_SCALE_MAX) ? cfg.MinimapScale : 1.0f;
                                showCompass = cfg.ShowCompass;
                                showCompassGps = cfg.ShowCompassGps;
                                showCompassSignals = cfg.ShowCompassSignals;
                                showCompassOwnSignals = cfg.ShowCompassOwnSignals;
                                showMapGpsLabels = cfg.ShowMapGpsLabels;
                                showMapSignalLabels = cfg.ShowMapSignalLabels;
                                showMapOwnSignals = cfg.ShowMapOwnSignals;
                                stackMapIcons = cfg.StackMapIcons;
                                showMapKoth = cfg.ShowMapKoth;
                                showMinimapKoth = cfg.ShowMinimapKoth;
                                showCompassKoth = cfg.ShowCompassKoth;
                                showMapSites = cfg.ShowMapSites;
                                showMinimapSites = cfg.ShowMinimapSites;
                                showCompassSites = cfg.ShowCompassSites;
                                showMapTerritories = cfg.ShowMapTerritories;
                                showTerritoryFade = cfg.ShowTerritoryFade;
                                showMapZones = cfg.ShowMapZones;
                                showZoneTint = cfg.ShowZoneTint;
                                GvkPalette.Apply(cfg.ColorPalette);
                                bool oldDefaultKey = cfg.MapKeyVersion < MAP_KEY_VERSION && cfg.MapKey == (int)MyKeys.M
                                    && !cfg.MapKeyShift && !cfg.MapKeyCtrl && !cfg.MapKeyAlt;
                                if (IsValidMapKey((MyKeys)cfg.MapKey) && !oldDefaultKey)
                                {
                                    mapKey = (MyKeys)cfg.MapKey;
                                    mapKeyShift = cfg.MapKeyShift;
                                    mapKeyCtrl = cfg.MapKeyCtrl;
                                    mapKeyAlt = cfg.MapKeyAlt;
                                }
                                showMinimapGps = cfg.ShowMinimapGps;
                                showMinimapSignals = cfg.ShowMinimapSignals;
                                showMinimapOwnSignals = cfg.ShowMinimapOwnSignals;
                                compassScale = (cfg.CompassScale >= HUD_SCALE_MIN && cfg.CompassScale <= HUD_SCALE_MAX) ? cfg.CompassScale : 1.0f;
                                if (cfg.ZoneBarMode >= 0 && cfg.ZoneBarMode <= 2)
                                    zoneBarMode = (ZoneBarMode)cfg.ZoneBarMode;
                                else if (!cfg.ShowZoneBar) // Legacy: pre-ZoneBarMode configs — disabled
                                    zoneBarMode = ZoneBarMode.Off;
                                else if (cfg.ZoneBarDockMode == 1) // Legacy: old DockMode 1 mapped to CompassDock
                                    zoneBarMode = ZoneBarMode.CompassDock;
                                else
                                    zoneBarMode = ZoneBarMode.RadarDock;

                                updateTickRate = (cfg.UpdateTickRate >= 1 && cfg.UpdateTickRate <= 60) ? cfg.UpdateTickRate : 5;
                                showRadarZoneLines = cfg.ShowRadarZoneLines;
                                showRadarFovLines = cfg.ShowRadarFovLines;
                                minimapOffsetX = MathHelper.Clamp(cfg.MinimapOffsetX, -1.0, 1.0);
                                minimapOffsetY = MathHelper.Clamp(cfg.MinimapOffsetY, -1.0, 1.0);
                                compassOffsetX = MathHelper.Clamp(cfg.CompassOffsetX, -1.0, 1.0);
                                compassOffsetY = MathHelper.Clamp(cfg.CompassOffsetY, -1.0, 1.0);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[GVK_ZoneNavCommand] Error loading config: {ex.Message}");
            }
        }

        private void SaveConfig()
        {
            try
            {
                if (MyAPIGateway.Utilities == null) return;
                var cfg = new ZoneNavConfig
                {
                    ShowMinimap = showMinimap,
                    RadarScale = (int)radarScale,
                    RadarRangeMeters = radarRangeMeters,
                    MinimapScale = minimapScale,
                    MinimapOffsetX = minimapOffsetX,
                    MinimapOffsetY = minimapOffsetY,
                    ShowCompass = showCompass,
                    ShowCompassGps = showCompassGps,
                    ShowCompassSignals = showCompassSignals,
                    ShowCompassOwnSignals = showCompassOwnSignals,
                    ShowMapGpsLabels = showMapGpsLabels,
                    ShowMapSignalLabels = showMapSignalLabels,
                    ShowMapOwnSignals = showMapOwnSignals,
                    StackMapIcons = stackMapIcons,
                    ShowMapKoth = showMapKoth,
                    ShowMinimapKoth = showMinimapKoth,
                    ShowCompassKoth = showCompassKoth,
                    ShowMapSites = showMapSites,
                    ShowMinimapSites = showMinimapSites,
                    ShowCompassSites = showCompassSites,
                    ShowMapTerritories = showMapTerritories,
                    ShowTerritoryFade = showTerritoryFade,
                    ShowMapZones = showMapZones,
                    ShowZoneTint = showZoneTint,
                    ColorPalette = GvkPalette.Current,
                    MapKey = (int)mapKey,
                    MapKeyShift = mapKeyShift,
                    MapKeyCtrl = mapKeyCtrl,
                    MapKeyAlt = mapKeyAlt,
                    MapKeyVersion = MAP_KEY_VERSION,
                    ShowMinimapGps = showMinimapGps,
                    ShowMinimapSignals = showMinimapSignals,
                    ShowMinimapOwnSignals = showMinimapOwnSignals,
                    CompassScale = compassScale,
                    CompassOffsetX = compassOffsetX,
                    CompassOffsetY = compassOffsetY,
                    ShowZoneBar = (zoneBarMode != ZoneBarMode.Off),
                    ZoneBarDockMode = (zoneBarMode == ZoneBarMode.CompassDock ? 1 : 0),
                    ZoneBarMode = (int)zoneBarMode,
                    UpdateTickRate = updateTickRate,
                    ShowRadarZoneLines = showRadarZoneLines,
                    ShowRadarFovLines = showRadarFovLines
                };

                string xml = MyAPIGateway.Utilities.SerializeToXML(cfg);
                using (var writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(CONFIG_FILENAME, typeof(GVK_ZoneNavCommand)))
                {
                    writer.Write(xml);
                    writer.Flush();
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[GVK_ZoneNavCommand] Error saving config: {ex.Message}");
            }
        }

        public override void LoadData()
        {
            if (!MyAPIGateway.Utilities.IsDedicated)
            {
                LoadConfig();
                MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;
            }
        }

        protected override void UnloadData()
        {
            if (!MyAPIGateway.Utilities.IsDedicated)
            {
                SaveConfig();
                MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;

                _compassVisibleWps.Clear();
                _cachedMapClusters.Clear();
            }
        }

        /// <summary>
        /// Gets the current monitor aspect ratio (Width / Height) so screen sprites keep their shape.
        /// </summary>
        private float GetScreenAspect()
        {
            var camera = MyAPIGateway.Session.Camera;
            if (camera != null && camera.ViewportSize.Y > 0)
            {
                if (camera.ViewportSize != cachedViewportSize)
                {
                    cachedViewportSize = camera.ViewportSize;
                    cachedAspect = camera.ViewportSize.X / camera.ViewportSize.Y;
                    _mapLayoutDirty = true;
                }
                return cachedAspect;
            }
            return cachedAspect;
        }

        public override void UpdateBeforeSimulation()
        {
            if (MyAPIGateway.Utilities.IsDedicated) return;

            // Handle the map key (default =); modifiers must match exactly so Ctrl+M doesn't also fire on M
            // (The map's game screen shows the cursor, so it handles its own key and Esc.)
            if (!MyAPIGateway.Gui.ChatEntryVisible && !MyAPIGateway.Gui.IsCursorVisible && IsMapKeyNewPress())
            {
                ToggleFullMap();
            }

            UpdateMapReopen();
            KothClientData.MapOpen = showFullMap;

            Vector3D? pos = GetPlayerPosition();
            if (!pos.HasValue) return;

            if (!_hudLayoutInit)
            {
                // First tick with a player (the camera and input are up by now): lay out the minimap for this
                // window, which the zone bar's Radar Dock reads even with the minimap off, and name the map key
                // loaded from the config for the map's close hint and the settings screen
                _hudLayoutInit = true;
                ApplyMinimapScale(minimapScale);
                UpdateMapKeyLabel();
            }

            if (!hasCheckedDefaultGps)
            {
                CheckAndPopulateDefaultGps();
            }

            if (++tickCounter > 120000) tickCounter = 0; // LCM of all modulo values; prevents int.MaxValue wrap

            // 100-Tick (~1.67s) Background Scan: entity iteration, GPS list, zone distance.
            // Players don't cross zone boundaries fast enough to notice the extra latency.
            // KOTH flash phase for this tick: bright for 36 of every 60 ticks (0.6 s of each second)
            _kothFlashBright = tickCounter % 60 < 36;

            // A GPS added from the map reaches the GPS list after a server round trip; re-read it soon after
            bool gpsRescan = _gpsRescanTicks > 0 && --_gpsRescanTicks == 0;
            if (tickCounter % 100 == 0 || gpsRescan)
            {
                KothClientData.Changed = false;
                UpdateBackgroundData(pos.Value);

                float currentHudOpacity = GetUserHudOpacity();
                if (Math.Abs(currentHudOpacity - cachedHudBkOpacity) > 0.005f)
                {
                    UpdateCardBackgroundOpacities();
                }
            }
            else if (KothClientData.Changed)
            {
                // New KOTH state (about once a second): refresh only the KOTH markers, not the full entity scan
                KothClientData.Changed = false;
                RefreshKothWaypoints(pos.Value);
            }

            // Configurable HUD Cadence (default 5 ticks / 12 Hz):
            // Compass, Minimap, Zone Bar, and the satellite map all rebuild on the player's updateTickRate
            // (/gvk rate or the settings screen), trading smoothness against CPU time.
            if (tickCounter % updateTickRate == 0 || _refreshCompassNextFrame)
            {
                _refreshCompassNextFrame = false;
                UpdateCompassAndWaypoints(pos.Value);
            }

            if (showFullMap)
            {
                if (MapScreenOpen && !_ssMapDrawFailed)
                {
                    _mapPlayerPos = pos.Value;
                    if (tickCounter % updateTickRate == 0 || _fullMapNeedsRedraw)
                    {
                        _fullMapNeedsRedraw = false;
                        try
                        {
                            BuildScreenSpaceFullMap();
                        }
                        catch (Exception ex)
                        {
                            FailMapScreen("build", ex);
                        }
                    }
                }
            }
            else if (tickCounter % updateTickRate == 0 || _refreshMinimapNextFrame)
            {
                _refreshMinimapNextFrame = false;
                UpdateZoneBar();
                UpdateMinimap(pos.Value);
            }
        }
        private void CheckAndPopulateDefaultGps()
        {
            var player = MyAPIGateway.Session.Player;
            if (player == null) return;
            hasCheckedDefaultGps = true;

            // Reuse cachedGpsList to avoid allocation — called once on first player position tick.
            cachedGpsList.Clear();
            MyAPIGateway.Session.GPS.GetGpsList(player.IdentityId, cachedGpsList);

            bool hasZone0 = false;
            bool hasZone3 = false;
            for (int i = 0; i < cachedGpsList.Count; i++)
            {
                if (cachedGpsList[i].Name.Equals("Zone 0", StringComparison.OrdinalIgnoreCase)) hasZone0 = true;
                if (cachedGpsList[i].Name.Equals("Zone 3", StringComparison.OrdinalIgnoreCase)) hasZone3 = true;
            }

            if (!hasZone0 || !hasZone3)
            {
                PopulateDefaultGps(notify: false);
            }
        }

        public void PopulateDefaultGps(bool notify = true)
        {
            var player = MyAPIGateway.Session.Player;
            if (player == null) return;

            // Reuse cachedGpsList to avoid allocation — called from commands/menu, never concurrently with scan.
            cachedGpsList.Clear();
            MyAPIGateway.Session.GPS.GetGpsList(player.IdentityId, cachedGpsList);

            int addedCount = 0;
            for (int i = 0; i < DEFAULT_KHARAK_GPS.Length; i++)
            {
                var entry = DEFAULT_KHARAK_GPS[i];
                bool exists = false;
                for (int j = 0; j < cachedGpsList.Count; j++)
                {
                    string gpsName = cachedGpsList[j].Name;
                    if (gpsName.Equals(entry.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    var gps = MyAPIGateway.Session.GPS.Create(entry.Name, entry.Description, entry.Coords, entry.ShowOnHud, entry.AlwaysVisible);
                    gps.GPSColor = GetDefaultGpsColor(entry.Kind);
                    MyAPIGateway.Session.GPS.AddLocalGps(gps);
                    addedCount++;
                }
            }

            if (notify)
            {
                string msg = addedCount > 0
                    ? $"[GVK NAV] Added {addedCount} default Kharak GPS waypoints."
                    : "[GVK NAV] All default Kharak GPS waypoints are already present.";
                MyAPIGateway.Utilities.ShowNotification(msg, 3000, MyFontEnum.Green);
            }
        }

        /// <summary>
        /// Zone index (0-3) from the MES zone spheres, plus straight-line distances to the next-lower and
        /// next-higher zone. Zone 0 wins where it overlaps Zone 1; otherwise the highest containing sphere wins.
        /// Outside every sphere (the sky just past the Zone 0 edge, high altitude) counts as Zone 1, as in
        /// GVK_NoPVPZone.cs and the production limits.
        /// </summary>
        private static int ClassifyZone(Vector3D pos, out double toLowerM, out double toHigherM)
        {
            double d0 = Vector3D.Distance(pos, CROSSROADS_BEACON);
            double d3 = Vector3D.Distance(pos, Z3_CENTER);

            int zone;
            if (d0 <= ZONE_0_RADIUS) zone = 0;
            else if (d3 <= ZONE_3_RADIUS) zone = 3;
            else if (d3 <= ZONE_2_RADIUS) zone = 2;
            else zone = 1; // inside the Zone 1 sphere, or outside every sphere

            switch (zone)
            {
                case 0:
                    toLowerM = 0.0;
                    toHigherM = ZONE_0_RADIUS - d0; // leave the Zone 0 sphere
                    break;
                case 1:
                    toLowerM = d0 - ZONE_0_RADIUS;  // enter the Zone 0 sphere
                    toHigherM = d3 - ZONE_2_RADIUS;
                    break;
                case 2:
                    toLowerM = ZONE_2_RADIUS - d3;
                    toHigherM = d3 - ZONE_3_RADIUS;
                    break;
                default:
                    toLowerM = ZONE_3_RADIUS - d3;
                    toHigherM = 0.0;
                    break;
            }

            toLowerM = Math.Max(0.0, toLowerM);
            toHigherM = Math.Max(0.0, toHigherM);
            return zone;
        }

        /// <summary>
        /// Scans all player GPS entries and in-range radio signals:
        /// 1. GPS coordinates: uses marker_gps and GPSColor.
        /// 2. Active broadcasting antennas / beacons: uses marker_friendly, marker_enemy, marker_neutral, marker_self.
        /// </summary>
        private void UpdateBackgroundData(Vector3D playerPos)
        {
            double toLowerM, toHigherM;
            currentZoneIndex = ClassifyZone(playerPos, out toLowerM, out toHigherM);
            lastToLowerKm = toLowerM / 1000.0;
            lastRemainingKm = toHigherM / 1000.0;
            lastDistKm = Vector3D.Distance(playerPos, CROSSROADS_BEACON) / 1000.0;
            lastDistZ3Km = Vector3D.Distance(playerPos, Z3_CENTER) / 1000.0;

            float gravityInterference;
            inSpace = MyAPIGateway.Physics.CalculateNaturalGravityAt(playerPos, out gravityInterference).LengthSquared() < 1e-4f;

            activeHudWaypoints.Clear();

            // 1. Scan Player GPS entries
            cachedGpsList.Clear();
            var playerId = MyAPIGateway.Session.Player?.IdentityId ?? 0;
            if (playerId != 0)
            {
                MyAPIGateway.Session.GPS.GetGpsList(playerId, cachedGpsList);

                for (int i = 0; i < cachedGpsList.Count; i++)
                {
                    var gps = cachedGpsList[i];
                    if (!gps.ShowOnHud) continue; // Strictly only show what is toggled to Show On HUD!

                    double dist = Vector3D.Distance(playerPos, gps.Coords);

                    activeHudWaypoints.Add(new ActiveHudWaypoint
                    {
                        Name = gps.Name,
                        Coords = gps.Coords,
                        Sprite = MATERIAL_MARKER_GPS,
                        DisplayColor = gps.GPSColor,
                        DistanceMeters = dist,
                        MapUV = WorldToMapUV(gps.Coords), // Pre-computed once; reused by minimap+map every frame.
                        IsSignal = false,
                        PlanetaryElevation = Vector3D.Distance(gps.Coords, PLANET_CENTER) - PLANET_RADIUS
                    });
                }
            }

            // 2. Scan Real In-Range Radio Broadcasts (Beacons & Antennas)
            entityBuffer.Clear();
            // Use cached predicate delegate — avoids a new delegate allocation every 30 ticks on Mono/SE.
            MyAPIGateway.Entities.GetEntities(entityBuffer, IsGridPredicate);

            var controlled = MyAPIGateway.Session.ControlledObject?.Entity;
            var playerGrid = (controlled as IMyCubeBlock)?.CubeGrid ?? (controlled as IMyCubeGrid);
            long localPlayerId = MyAPIGateway.Session.Player?.IdentityId ?? 0;
            IMyFaction localFaction = localPlayerId != 0 ? MyAPIGateway.Session.Factions.TryGetPlayerFaction(localPlayerId) : null;

            foreach (var ent in entityBuffer)
            {
                var grid = ent as IMyCubeGrid;
                if (grid == null || grid.MarkedForClose) continue;

                // Never track own vehicle construct when actively controlling it
                if (playerGrid != null && (grid == playerGrid || grid.IsSameConstructAs(playerGrid)))
                    continue;

                // Distance culling: vanilla max beacon/antenna is 50km + 10km grid bounding margin (60km).
                // Skip extracting block buffers on distant pirate bases and derelicts that can never broadcast to player.
                if (Vector3D.DistanceSquared(playerPos, grid.PositionComp.GetPosition()) > 60000.0 * 60000.0)
                    continue;

                // Scan broadcasting blocks on this grid using cached predicate delegate.
                blockBuffer.Clear();
                grid.GetBlocks(blockBuffer, IsBroadcastBlockPredicate);

                long gridOwner = grid.BigOwners.Count > 0 ? grid.BigOwners[0] : 0;
                bool isOwner = (gridOwner == localPlayerId && localPlayerId != 0);

                // gridOwner == 0 means the grid is UNOWNED (no player set ownership), not necessarily an NPC.
                // Rely solely on the faction NPC check for correct NPC gold color; unowned shows as neutral white.
                IMyFaction ownerFaction = gridOwner != 0 ? MyAPIGateway.Session.Factions.TryGetPlayerFaction(gridOwner) : null;
                bool isNpc = ownerFaction != null && ownerFaction.IsEveryoneNpc();
                bool sameFaction = !isOwner && localFaction != null && ownerFaction == localFaction;

                // One marker per grid (de-clutter rule): track the NEAREST in-range broadcasting block.
                // GetBlocks() ordering is arbitrary, so the old first-found 'break' could pin a far beacon
                // while ignoring a much closer antenna on the same grid. Tracked via locals — zero allocations.
                double bestDistSq = double.MaxValue;
                Vector3D bestPos = Vector3D.Zero;
                string bestName = null;
                IMyCubeBlock bestBlock = null;

                for (int bIdx = 0; bIdx < blockBuffer.Count; bIdx++)
                {
                    var fat = blockBuffer[bIdx].FatBlock;

                    // Resolve broadcast radius / position / name for beacons vs. actively broadcasting antennas.
                    double radius;
                    Vector3D blockPos;
                    string blockName;

                    var beacon = fat as IMyBeacon;
                    if (beacon != null)
                    {
                        if (!beacon.IsWorking || !beacon.Enabled) continue;
                        radius = beacon.Radius;
                        blockPos = beacon.GetPosition();
                        blockName = string.IsNullOrEmpty(beacon.CustomName) ? grid.CustomName : beacon.CustomName;
                    }
                    else
                    {
                        var antenna = fat as IMyRadioAntenna;
                        if (antenna == null || !antenna.IsWorking || !antenna.Enabled || !antenna.EnableBroadcasting) continue;
                        radius = antenna.Radius;
                        blockPos = antenna.GetPosition();
                        blockName = string.IsNullOrEmpty(antenna.CustomName) ? grid.CustomName : antenna.CustomName;
                    }

                    double distSq = Vector3D.DistanceSquared(playerPos, blockPos);
                    // distSq >= 1.0 keeps the marker from pinning exactly onto the player position.
                    if (distSq <= radius * radius && distSq >= 1.0 && distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestPos = blockPos;
                        bestName = blockName;
                        bestBlock = fat;
                    }
                }

                if (bestDistSq < double.MaxValue)
                {
                    // Friend or foe as the vanilla HUD works it out (MyIDModule.GetRelationPlayerBlock): the broadcasting
                    // block's owner against the player, with the player's own reputation for an NPC faction.
                    // IMyPlayer.GetRelationTo compares factions only, so it showed Friendly NPC stations red.
                    var relation = localPlayerId != 0 ? bestBlock.GetUserRelationToOwner(localPlayerId) : MyRelationsBetweenPlayerAndBlock.NoOwnership;
                    bool isOwnOrFaction = isOwner || sameFaction || relation == MyRelationsBetweenPlayerAndBlock.FactionShare || relation == MyRelationsBetweenPlayerAndBlock.Owner;

                    Color signalColor;
                    byte signalRelation;
                    if (isOwner || relation == MyRelationsBetweenPlayerAndBlock.Owner)
                    {
                        signalColor = GvkPalette.Own;
                        signalRelation = REL_OWN;
                    }
                    else if (sameFaction || relation == MyRelationsBetweenPlayerAndBlock.FactionShare)
                    {
                        signalColor = GvkPalette.Friendly;
                        signalRelation = REL_FACTION;
                    }
                    else if (relation == MyRelationsBetweenPlayerAndBlock.Friends)
                    {
                        signalColor = GvkPalette.Friendly;
                        signalRelation = REL_FRIENDLY;
                    }
                    else if (relation == MyRelationsBetweenPlayerAndBlock.Enemies)
                    {
                        signalColor = GvkPalette.Hostile;
                        signalRelation = REL_HOSTILE;
                    }
                    else
                    {
                        signalColor = GvkPalette.Neutral; // Neutral NPCs too, as in vanilla (owner, 2026-11-25)
                        signalRelation = isNpc ? REL_NPC : REL_NEUTRAL;
                    }

                    activeHudWaypoints.Add(new ActiveHudWaypoint
                    {
                        Name = bestName,
                        Coords = bestPos,
                        Sprite = MATERIAL_SIGNAL_LEVEL,
                        DisplayColor = signalColor,
                        DistanceMeters = Math.Sqrt(bestDistSq),
                        MapUV = WorldToMapUV(bestPos),
                        IsSignal = true,
                        IsOwnOrFaction = isOwnOrFaction,
                        Relation = signalRelation,
                        IsNpcOwned = isNpc,
                        GridOrigin = grid.PositionComp.GetPosition(),
                        PlanetaryElevation = Vector3D.Distance(bestPos, PLANET_CENTER) - PLANET_RADIUS
                    });
                }
            }

            // 3. Known sites from the MES spawn groups, and the alliance territory rings
            AddKnownSiteWaypoints(playerPos);
            UpdateTerritories(playerPos);

            // 4. KOTH zones from the GVK KOTH plugin
            AddKothWaypoints(playerPos);

            // Sort by distance using cached IComparer struct — zero allocation, whitelisted by SE ModAPI.
            activeHudWaypoints.Sort(WaypointComparer);
            _mapLayoutDirty = true;

            // Flag full satellite map for redraw on next frame if currently open
            if (showFullMap)
                _fullMapNeedsRedraw = true;
        }

        /// <summary>
        /// Adds the known sites (bases, trade stations, depots, Carriers) to the waypoint list. Each GPS point stands in
        /// for the one site nearest it within that site's radius (150 m, more for long stations); a view skips a site only
        /// once it has drawn that GPS point. An NPC signal whose grid origin sits on a site (MES places these grids by
        /// origin; their beacons can be 150 m off it) is the site's own beacon: it folds into the site icon, which stays
        /// (owner, 2026-11-24), and a view that shows sites hides the signal. A trade station or the refinery then takes
        /// the signal's live colour, and the hover card adds the beacon's name. Player signals fold into nothing.
        /// </summary>
        private void AddKnownSiteWaypoints(Vector3D playerPos)
        {
            if (!_knownSitesLoaded) LoadKnownSites();
            int siteCount = _knownSites.Count;
            if (siteCount == 0) return;
            if (_siteStatusVersion != GvkSiteStatus.Version) RefreshSiteStatus();

            Array.Clear(_knownSiteHasStandIn, 0, siteCount);
            Array.Clear(_siteHasSignal, 0, siteCount);
            int before = activeHudWaypoints.Count;
            for (int i = 0; i < before; i++)
            {
                var wp = activeHudWaypoints[i];
                if (wp.IsSignal && !wp.IsNpcOwned) continue;
                Vector3D from = wp.IsSignal ? wp.GridOrigin : wp.Coords;
                int nearest = -1;
                double nearestSq = double.MaxValue;
                for (int s = 0; s < siteCount; s++)
                {
                    double dSq = Vector3D.DistanceSquared(from, _knownSites[s].Coords);
                    double limitSq = wp.IsSignal ? SITE_GRID_DEDUPE_DIST_SQ : _knownSites[s].StandInRadiusSq;
                    if (dSq <= limitSq && dSq < nearestSq)
                    {
                        nearestSq = dSq;
                        nearest = s;
                    }
                }
                if (nearest < 0) continue;
                if (wp.IsSignal)
                {
                    wp.FoldedInto = nearest + 1;
                    _siteHasSignal[nearest] = true;
                    _siteSignalRel[nearest] = wp.Relation;
                    _siteSignalName[nearest] = wp.Name;
                }
                else
                {
                    wp.StandsInFor = nearest + 1;
                    _knownSiteHasStandIn[nearest] = true;
                }
                activeHudWaypoints[i] = wp;
            }

            for (int s = 0; s < siteCount; s++)
            {
                var site = _knownSites[s];
                site.DistanceMeters = Vector3D.Distance(playerPos, site.Coords);
                site.HasStandIn = _knownSiteHasStandIn[s];
                site.HasSignal = _siteHasSignal[s];
                bool live = site.HasSignal && (site.SiteKind == SITE_TRADE || site.SiteKind == SITE_REFINERY);
                site.DisplayColor = live ? GetSignalSiteColor(_siteSignalRel[s]) : GetSiteColor(s); // Every scan: standing can change
                site.KindText = site.HasSignal && !string.IsNullOrEmpty(_siteSignalName[s])
                    ? _siteKindText[s] + ", " + _siteSignalName[s]
                    : _siteKindText[s];
                activeHudWaypoints.Add(site);
            }
        }

        /// <summary>Builds the HUD's site markers once, from the shared table (<see cref="GvkKnownSites"/>).</summary>
        private void LoadKnownSites()
        {
            _knownSitesLoaded = true;
            _knownSites.Clear();
            GvkKnownSites.EnsureLoaded();
            var sites = GvkKnownSites.Sites;
            for (int i = 0; i < sites.Count; i++)
            {
                var site = sites[i];
                _knownSites.Add(new ActiveHudWaypoint
                {
                    Name = site.Name,
                    Coords = site.Coords,
                    Sprite = GetSiteMaterial(site.Kind),
                    DisplayColor = SITE_WHITE,
                    MapUV = WorldToMapUV(site.Coords),
                    PlanetaryElevation = Vector3D.Distance(site.Coords, PLANET_CENTER) - PLANET_RADIUS,
                    SiteKind = site.Kind,
                    SiteIndex = i,
                    StandInRadiusSq = site.GpsRadius * site.GpsRadius
                });
            }
            int n = _knownSites.Count;
            _knownSiteHasStandIn = new bool[n];
            _siteHasSignal = new bool[n];
            _siteSignalRel = new byte[n];
            _siteSignalName = new string[n];
            _siteStandInDrawn = new bool[n];
            _siteState = new byte[n];
            _siteFaction = new long[n];
            _siteKindText = new string[n];
            _siteStatusVersion = int.MinValue;
        }

        private static MyStringId GetSiteMaterial(byte kind)
        {
            switch (kind)
            {
                case SITE_BASE: return MATERIAL_SITE_BASE;
                case SITE_DEPOT: return MATERIAL_SITE_DEPOT;
                case SITE_CARRIER: return MATERIAL_SITE_CARRIER;
                default: return MATERIAL_SITE_TRADE;
            }
        }

        /// <summary>Reads the server's site status (<see cref="GvkSiteStatus"/>) into the per-site arrays and hover text.</summary>
        private void RefreshSiteStatus()
        {
            _siteStatusVersion = GvkSiteStatus.Version;
            var sites = GvkKnownSites.Sites;
            for (int s = 0; s < _knownSites.Count && s < sites.Count; s++)
            {
                GvkSiteStatus.Entry e;
                if (!GvkSiteStatus.BySubtypeId.TryGetValue(sites[s].SubtypeId, out e)) e = new GvkSiteStatus.Entry();
                _siteState[s] = e.State;
                _siteFaction[s] = e.FactionId;
                _siteKindText[s] = GetSiteKindText(_knownSites[s].SiteKind, e.State, e.Tag);
            }
        }

        private static string GetSiteKindText(byte kind, byte state, string tag)
        {
            bool missing = state == GvkSiteStatus.MISSING;
            bool alliance = !string.IsNullOrEmpty(tag) && tag != "UNKN";
            switch (kind)
            {
                case SITE_BASE:
                    if (state == GvkSiteStatus.HELD) return alliance ? tag + " base" : "Alliance base";
                    if (state == GvkSiteStatus.UNCLAIMED) return "Base site, unclaimed";
                    return missing ? "Base site, not spawned" : "Alliance base site";
                case SITE_TRADE: return missing ? "Trade station, not spawned" : "Trade station";
                case SITE_REFINERY: return missing ? "Scrap refinery, not spawned" : "Scrap refinery";
                case SITE_DEPOT: return missing ? "Supply depot, not spawned" : alliance ? tag + " supply depot" : "Supply depot";
                default: return missing ? "Carrier, not spawned" : alliance ? tag + " carrier" : "Alliance carrier";
            }
        }

        /// <summary>
        /// Site colour for this scan: trade stations white (owner, 2026-11-24); a Base Site without an alliance Base, or a
        /// site not spawned, dimmed; otherwise the player's standing with the owner: green Friendly, red hostile, white neutral.
        /// </summary>
        private Color GetSiteColor(int s)
        {
            byte state = _siteState[s];
            byte kind = _knownSites[s].SiteKind;
            if (state == GvkSiteStatus.MISSING) return SITE_DIM;
            if (kind == SITE_TRADE || kind == SITE_REFINERY) return SITE_WHITE;
            if (kind == SITE_BASE && state == GvkSiteStatus.UNCLAIMED) return SITE_DIM; // UNKNOWN (no status yet) stays white
            if (state != GvkSiteStatus.HELD || _siteFaction[s] == 0) return SITE_WHITE;
            return GetStandingColor(MyAPIGateway.Session.Factions.TryGetFactionById(_siteFaction[s]));
        }

        /// <summary>A site icon's colour from its own signal's relation, as its signal shows: own, faction or Friendly, hostile, else white.</summary>
        private static Color GetSignalSiteColor(byte relation)
        {
            if (relation == REL_HOSTILE) return SITE_HOSTILE;
            if (relation == REL_OWN) return GvkPalette.Own;
            if (relation == REL_FACTION || relation == REL_FRIENDLY) return SITE_FRIENDLY;
            return SITE_WHITE;
        }

        /// <summary>The player's standing with an NPC faction: green Friendly, red hostile, white neutral, unowned (UNKN) or unknown.</summary>
        private static Color GetStandingColor(IMyFaction faction)
        {
            return GetStandingColor(GetStanding(faction));
        }

        private static Color GetStandingColor(byte standing)
        {
            return standing == STANDING_FRIENDLY ? SITE_FRIENDLY : standing == STANDING_HOSTILE ? SITE_HOSTILE : SITE_WHITE;
        }

        /// <summary>The player's standing with a faction as STANDING_*; GVK's Friendly is 500 and up, hostile below -500.</summary>
        private static byte GetStanding(IMyFaction faction)
        {
            long me = MyAPIGateway.Session.Player?.IdentityId ?? 0;
            if (faction == null || faction.Tag == "UNKN" || me == 0) return STANDING_NEUTRAL;
            int rep = MyAPIGateway.Session.Factions.GetReputationBetweenPlayerAndFaction(me, faction.FactionId);
            if (rep >= SITE_FRIENDLY_REP) return STANDING_FRIENDLY;
            if (rep < SITE_HOSTILE_REP) return STANDING_HOSTILE;
            return STANDING_NEUTRAL;
        }

        /// <summary>
        /// Rebuilds a territory ring when the server reports a new size, and recolours it by standing. A ring shows only
        /// once the server has sent its size, so a client never shows the zone profile's 10 km before the real one.
        /// </summary>
        private void UpdateTerritories(Vector3D playerPos)
        {
            var territories = GvkKnownSites.Territories;
            int n = Math.Min(territories.Count, 31); // Overlap masks hold a bit per territory
            if (_territoryUV.Count != n)
            {
                _territoryUV.Clear();
                for (int t = 0; t < n; t++)
                    _territoryUV.Add(new List<Vector2>());
                _territoryRadius = new double[n];
                _territoryColor = new Color[n];
                _territoryDir = new Vector3D[n];
                _territoryCosA = new double[n];
                _territoryName = new string[n];
                _territoryLabel = new string[n];
                _territoryUpper = new string[n];
                _territoryStanding = new string[n];
                for (int t = 0; t < n; t++) _territoryCosA[t] = 2.0;
            }
            _playerTerritoryMask = 0;
            for (int t = 0; t < n; t++)
            {
                var territory = territories[t];
                int km;
                double radius = 0.0;
                if (GvkSiteStatus.TerritoryKm.TryGetValue(territory.Tag, out km))
                    radius = km > 0 ? km * 1000.0 : territory.BaseRadius; // 0: the ladder hasn't set a size yet
                if (radius != _territoryRadius[t])
                {
                    _territoryRadius[t] = radius;
                    _territoryCosA[t] = BuildTerritoryRing(territory.Center, radius, _territoryUV[t]);
                    Vector3D toCenter = territory.Center - PLANET_CENTER;
                    _territoryDir[t] = toCenter.LengthSquared() > 1.0 ? Vector3D.Normalize(toCenter) : Vector3D.Zero;
                    _refreshMinimapNextFrame = true;
                }
                var faction = MyAPIGateway.Session.Factions.TryGetFactionByTag(territory.Tag);
                byte standing = GetStanding(faction);
                _territoryColor[t] = GetStandingColor(standing);
                _territoryStanding[t] = STANDING_WORDS[standing];
                string name = string.IsNullOrEmpty(faction?.Name) ? territory.Tag : faction.Name;
                if (!ReferenceEquals(name, _territoryName[t]))
                {
                    _territoryName[t] = name;
                    _territoryLabel[t] = name + " territory";
                    _territoryUpper[t] = name.ToUpperInvariant();
                }
                if (radius > 0.0 && Vector3D.DistanceSquared(playerPos, territory.Center) <= radius * radius)
                    _playerTerritoryMask |= 1 << t;
            }
        }

        /// <summary>The colour at opacity a: the screen API blends with straight alpha, so RGB stays as it is.</summary>
        private static Color WithAlpha(Color c, float a)
        {
            return new Color(c.R, c.G, c.B, (byte)MathHelper.Clamp((int)(a * 255f + 0.5f), 0, 255));
        }

        /// <summary>Your arrow on the radar and map: the Own colour, pulsing toward white (owner, 2026-11-25; was gold).</summary>
        private Color GetPlayerArrowColor()
        {
            return tickCounter % 40 < 20 ? GvkPalette.Own : Color.Lerp(GvkPalette.Own, Color.White, 0.5f);
        }

        /// <summary>Territories whose ground circle holds map point (u, v) (bit per territory).</summary>
        private int GetTerritoryMaskAt(double u, double v)
        {
            double lat = Math.PI * 0.5 - v * Math.PI;
            double lon = u * 2.0 * Math.PI - Math.PI + MAP_LON_SHIFT * 2.0 * Math.PI; // As AddGpsFromMap
            var dir = new Vector3D(Math.Cos(lat) * Math.Cos(lon), -Math.Sin(lat), Math.Cos(lat) * Math.Sin(lon));
            int mask = 0;
            for (int t = 0; t < _territoryCosA.Length; t++)
                if (_territoryCosA[t] <= 1.0 && Vector3D.Dot(dir, _territoryDir[t]) >= _territoryCosA[t]) mask |= 1 << t;
            return mask;
        }

        /// <summary>
        /// Where a territory sphere meets the ground: a circle on the planet (sea level, PLANET_RADIUS) whose angular
        /// radius A comes from the triangle planet centre, zone centre, edge point: cos A = (R² + d² - r²) / 2Rd.
        /// Points are spaced evenly round that circle on the globe and put through <see cref="WorldToMapUV"/>, so the ring
        /// takes the map's own stretch (east-west by 1 / cos latitude) and wraps at its edges like everything else on it.
        /// </summary>
        private double BuildTerritoryRing(Vector3D center, double radius, List<Vector2> uv)
        {
            uv.Clear();
            if (radius <= 0.0) return 2.0;
            Vector3D toCenter = center - PLANET_CENTER;
            double d = toCenter.Length();
            if (d < 1.0) return 2.0;
            Vector3D c = toCenter / d;
            double r2 = PLANET_RADIUS * PLANET_RADIUS;
            double cosA = (r2 + d * d - radius * radius) / (2.0 * PLANET_RADIUS * d);
            if (cosA >= 1.0) return 2.0;  // The sphere misses the ground
            if (cosA <= -1.0) return -2.0; // It covers the whole planet: no edge
            double sinA = Math.Sqrt(1.0 - cosA * cosA);

            Vector3D axis = Math.Abs(c.Y) < 0.9 ? Vector3D.Up : Vector3D.Right;
            Vector3D e1 = Vector3D.Normalize(Vector3D.Cross(axis, c));
            Vector3D e2 = Vector3D.Cross(c, e1);
            int count = MathHelper.Clamp((int)(2.0 * Math.PI * PLANET_RADIUS * sinA / TERRITORY_POINT_SPACING_M),
                TERRITORY_MIN_POINTS, TERRITORY_MAX_POINTS);
            for (int i = 0; i < count; i++)
            {
                double a = 2.0 * Math.PI * i / count;
                Vector3D dir = c * cosA + (e1 * Math.Cos(a) + e2 * Math.Sin(a)) * sinA;
                uv.Add(WorldToMapUV(PLANET_CENTER + dir * PLANET_RADIUS));
            }
            return cosA;
        }

        /// <summary>
        /// Draws each territory's ring as an evenly dashed line in the player's standing colour, like the safety zone lines,
        /// on a map drawn w x h with map UV (viewU, viewV) at (ox, oy).
        /// </summary>
        private void AddTerritoryLines(GvkScreenLayer layer, double ox, double oy, double w, double h, double viewU, double viewV,
            double halfW, double halfH, float aspect, double lineW, double minSeg)
        {
            for (int t = 0; t < _territoryUV.Count; t++)
                AddGlowRing(layer, _territoryUV[t], ox, oy, w, h, viewU, viewV, halfW, halfH, aspect, lineW, minSeg, _territoryColor[t],
                    TERRITORY_DASH, TERRITORY_GAP);
        }

        /// <summary>The safety zone rings, once: the zones never move.</summary>
        private void BuildZoneOverlay()
        {
            _zoneOverlayBuilt = true;
            BuildTerritoryRing(CROSSROADS_BEACON, ZONE_0_RADIUS, _zoneRingUV[0]);
            BuildTerritoryRing(Z3_CENTER, ZONE_2_RADIUS, _zoneRingUV[1]);
            BuildTerritoryRing(Z3_CENTER, ZONE_3_RADIUS, _zoneRingUV[2]);
        }

        /// <summary>Draws the safety zone boundary lines, solid, each in its zone's colour (see <see cref="AddGlowRing"/>).</summary>
        private void AddZoneOverlay(GvkScreenLayer layer, double ox, double oy, double w, double h, double viewU, double viewV,
            double halfW, double halfH, float aspect, double lineW, double minSeg)
        {
            if (!_zoneOverlayBuilt) BuildZoneOverlay();
            for (int z = 0; z < _zoneRingUV.Length; z++)
                AddGlowRing(layer, _zoneRingUV[z], ox, oy, w, h, viewU, viewV, halfW, halfH, aspect, lineW, minSeg,
                    GetZoneColor(ZONE_OVERLAY_ZONE[z]), 0.0, 0.0);
        }

        /// <summary>
        /// One ring line in two passes: a faint colour line, then the same colour added over it (glow).
        /// </summary>
        private static void AddGlowRing(GvkScreenLayer layer, List<Vector2> points, double ox, double oy, double w, double h,
            double viewU, double viewV, double halfW, double halfH, float aspect, double lineW, double minSeg, Color color,
            double dash, double gap)
        {
            if (points.Count < 2) return;
            AddRingPass(layer, points, ox, oy, w, h, viewU, viewV, halfW, halfH, aspect, lineW, minSeg, MATERIAL_SQUARE,
                WithAlpha(color, ZONE_LINE_ALPHA), dash, gap);
            AddRingPass(layer, points, ox, oy, w, h, viewU, viewV, halfW, halfH, aspect, lineW, minSeg, MATERIAL_ADDITIVE,
                WithAlpha(color, ZONE_LINE_GLOW), dash, gap);
        }

        /// <summary>
        /// One pass of a ring line on a map drawn w x h with map UV (viewU, viewV) at (ox, oy): rotated thin rectangles
        /// between ring points at least minSeg apart on screen. Each piece is exactly as long as its stretch, so neighbours
        /// never overlap and brighten or darken at the joins. With dash above 0 only dash-long stretches are drawn, gap
        /// apart, counted along the ring from its first point. Parts outside halfW x halfH are left out; nothing is drawn
        /// across the wrap seam.
        /// </summary>
        private static void AddRingPass(GvkScreenLayer layer, List<Vector2> points, double ox, double oy, double w, double h,
            double viewU, double viewV, double halfW, double halfH, float aspect, double lineW, double minSeg, MyStringId material,
            Color color, double dash, double gap)
        {
            double minSegSq = minSeg * minSeg;
            double period = dash + gap;
            double phase = 0.0; // Distance into the current dash and gap
            bool inDashStarted = false;
            double sx = 0.0, sy = 0.0; // Where the current dash starts
            double ax = (points[0].X - viewU) * w, ay = (viewV - points[0].Y) * h;
            double prevU = points[0].X;
            int count = points.Count;
            for (int k = 1; k <= count; k++) // k == count closes the ring back to the first point
            {
                int i = k == count ? 0 : k;
                double x = (points[i].X - viewU) * w;
                double y = (viewV - points[i].Y) * h;
                bool seam = Math.Abs(points[i].X - prevU) >= 0.5;
                prevU = points[i].X;
                if (seam)
                {
                    if (inDashStarted) AddChord(layer, sx, sy, ax, ay, ox, oy, halfW, halfH, aspect, lineW, material, color);
                    inDashStarted = false;
                    ax = x;
                    ay = y;
                    continue;
                }
                double dx = x - ax;
                double dy = (y - ay) / aspect; // HUD Y units are aspect times shorter on screen
                double lenSq = dx * dx + dy * dy;
                if (lenSq < minSegSq && k < count) continue;
                if (lenSq > 0.0)
                {
                    double len = Math.Sqrt(lenSq);
                    if (dash <= 0.0)
                    {
                        AddChord(layer, ax, ay, x, y, ox, oy, halfW, halfH, aspect, lineW, material, color);
                    }
                    else
                    {
                        // Walk the segment through the dash pattern. A dash that runs on past a ring point is drawn as one
                        // straight piece from where it starts to where it ends, so each dash is a single sprite.
                        double at = 0.0;
                        while (at < len)
                        {
                            bool inDash = phase < dash;
                            if (inDash && !inDashStarted)
                            {
                                double f0 = at / len;
                                sx = ax + (x - ax) * f0;
                                sy = ay + (y - ay) * f0;
                                inDashStarted = true;
                            }
                            double run = Math.Min((inDash ? dash : period) - phase, len - at);
                            at += run;
                            phase += run;
                            if (inDash && phase >= dash - 1e-9) // The tolerance stops a rounding leftover from looping
                            {
                                phase = dash;
                                double f1 = at / len;
                                AddChord(layer, sx, sy, ax + (x - ax) * f1, ay + (y - ay) * f1, ox, oy, halfW, halfH, aspect, lineW, material, color);
                                inDashStarted = false;
                            }
                            if (phase >= period - 1e-9) phase = 0.0;
                        }
                    }
                }
                ax = x;
                ay = y;
            }
            if (inDashStarted) AddChord(layer, sx, sy, ax, ay, ox, oy, halfW, halfH, aspect, lineW, material, color); // Last, short dash
        }

        /// <summary>One straight piece of a ring line from (x0, y0) to (x1, y1), map-relative HUD units; skipped when wholly outside halfW x halfH.</summary>
        private static void AddChord(GvkScreenLayer layer, double x0, double y0, double x1, double y1, double ox, double oy,
            double halfW, double halfH, float aspect, double lineW, MyStringId material, Color color)
        {
            if (Math.Min(x0, x1) > halfW || Math.Max(x0, x1) < -halfW || Math.Min(y0, y1) > halfH || Math.Max(y0, y1) < -halfH) return;
            double dx = x1 - x0;
            double dy = (y1 - y0) / aspect; // HUD Y units are aspect times shorter on screen
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len <= 0.0) return;
            layer.AddSprite(material, ox + (x0 + x1) * 0.5, oy + (y0 + y1) * 0.5, len, lineW * aspect, color, (float)-Math.Atan2(dy, dx));
        }

        /// <summary>
        /// The baked edge bands (<see cref="GvkMapBands"/>) on the satellite map, drawn mapW x mapH with map UV (viewU, viewV)
        /// at the HUD centre: the safety zones' with Safety Zone Tint on, each in its zone colour, and each territory's in
        /// the player's standing colour. A band baked for another centre or radius, or a territory size with none, draws
        /// nothing; the ring still shows.
        /// </summary>
        private void AddBakedBands(GvkScreenLayer layer, double viewU, double viewV, double mapW, double mapH, bool zones, bool territories)
        {
            if (zones)
            {
                for (int z = 0; z < GvkMapBands.Zones.Length && z < ZONE_RING_CENTER.Length; z++)
                {
                    var band = GvkMapBands.Zones[z];
                    if (Vector3D.DistanceSquared(band.Center, ZONE_RING_CENTER[z]) > 1.0 || Math.Abs(band.Radius - ZONE_RING_RADIUS[z]) > 1.0) continue;
                    AddBakedBand(layer, ref band, viewU, viewV, mapW, mapH, WithAlpha(GetZoneColor(ZONE_OVERLAY_ZONE[z]), ZONE_BAND_ALPHA));
                }
            }
            if (!territories) return;
            var list = GvkKnownSites.Territories;
            for (int t = 0; t < _territoryUV.Count && t < list.Count; t++)
            {
                if (_territoryUV[t].Count == 0) continue;
                GvkMapBands.Band band;
                if (!GvkMapBands.TryGetTerritory(list[t].Tag, (int)Math.Round(_territoryRadius[t] * 0.001), out band)) continue;
                if (Vector3D.DistanceSquared(band.Center, list[t].Center) > 1.0 || Math.Abs(band.Radius - _territoryRadius[t]) > 1.0) continue;
                AddBakedBand(layer, ref band, viewU, viewV, mapW, mapH, WithAlpha(_territoryColor[t], TERRITORY_BAND_ALPHA));
            }
        }

        /// <summary>One baked band over its map UV rectangle, and again one map width left when it runs on round the wrap seam.</summary>
        private static void AddBakedBand(GvkScreenLayer layer, ref GvkMapBands.Band band, double viewU, double viewV, double mapW, double mapH, Color color)
        {
            double w = (band.U1 - band.U0) * mapW, h = (band.V1 - band.V0) * mapH;
            double x = ((band.U0 + band.U1) * 0.5 - viewU) * mapW;
            double y = (viewV - (band.V0 + band.V1) * 0.5) * mapH;
            layer.AddSprite(band.Material, x, y, w, h, color);
            if (band.U1 > 1.0) layer.AddSprite(band.Material, x - mapW, y, w, h, color);
        }

        /// <summary>
        /// Selects the tactical icon for a HUD waypoint.
        /// GPS points retain their standard pin, while broadcast signals dynamically display
        /// altitude chevrons: Up arrow (> 200m above), Down arrow (> 200m below), or Equal icon (within 200m),
        /// comparing true spherical planetary elevations from the center of Pertam.
        /// </summary>
        private MyStringId GetWaypointMaterial(ref ActiveHudWaypoint wp, double playerElevation)
        {
            if (!wp.IsSignal) return wp.Sprite;
            double vertDist = wp.PlanetaryElevation - playerElevation;
            if (vertDist > 200.0) return MATERIAL_SIGNAL_UP;
            if (vertDist < -200.0) return MATERIAL_SIGNAL_DOWN;
            return MATERIAL_SIGNAL_LEVEL;
        }

        /// <summary>
        /// Updates the HUD Compass tape and projects graphical POI sprites (Keen marker_gps and relation markers).
        /// </summary>
        private void UpdateCompassAndWaypoints(Vector3D playerPos)
        {
            var camera = MyAPIGateway.Session.Camera;
            if (camera == null) return;

            // 1. Calculate Player Relative North and Pitch/Roll Correction.
            // Caching: Recompute orientation matrix and north azimuth only when moving > 1m across Pertam.
            // When stationary or rotating head/wheels, planet-relative north and horizon tilt do not change.
            if (Vector3D.DistanceSquared(playerPos, _lastCompassPos) > 1.0)
            {
                _lastCompassPos = playerPos;

                Vector3D relativePlayerPos = playerPos - PLANET_CENTER;
                Vector3 relativePlayerPosNormal = (Vector3)(relativePlayerPos / relativePlayerPos.Length());

                Vector3D relativeNorth = PLANET_CENTER + new Vector3D(0f, PLANET_RADIUS, 0f);
                Vector3D side1 = relativeNorth - playerPos;
                Vector3D side2 = PLANET_CENTER - playerPos;
                Vector3D cross = Vector3D.Cross(side1, side2);
                Vector3D playerRelativeNorth = Vector3D.Cross(cross, side2);
                Vector3 playerRelativeNorthNormal = (Vector3)(playerRelativeNorth / playerRelativeNorth.Length());

                Vector3 baseSouth = BASE_SOUTH;
                Matrix.CreateRotationFromTwoVectors(ref relativePlayerPosNormal, ref baseSouth, out _cachedRelativeOffset);

                Vector3 northCorrected = Vector3.Transform(playerRelativeNorthNormal, _cachedRelativeOffset);
                float northElev = 0f;
                Vector3.GetAzimuthAndElevation(northCorrected, out _cachedNorthAzimuth, out northElev);
            }

            Vector3 forwardCorrected = Vector3.Transform(camera.WorldMatrix.Forward, _cachedRelativeOffset);
            float playerAzimuth = 0f, playerElev = 0f;
            Vector3.GetAzimuthAndElevation(forwardCorrected, out playerAzimuth, out playerElev);

            float compass = (playerAzimuth + (float)Math.PI) - (_cachedNorthAzimuth + (float)Math.PI);
            if (compass < 0) compass += (float)Math.PI * 2f;
            else if (compass > (float)Math.PI * 2f) compass -= (float)Math.PI * 2f;

            // Vehicle / character true heading for map displays (tracks rover/cockpit facing direction, not camera orbit)
            Vector3D vehForward = Vector3D.Forward;
            var controlledObj = MyAPIGateway.Session.ControlledObject;
            var cb = controlledObj as IMyCubeBlock;
            var pGrid = cb?.CubeGrid;
            if (cb != null)
                vehForward = cb.WorldMatrix.Forward;
            else if (pGrid != null)
                vehForward = pGrid.WorldMatrix.Forward;
            else if (controlledObj?.Entity != null)
                vehForward = controlledObj.Entity.WorldMatrix.Forward;
            else
                vehForward = camera.WorldMatrix.Forward;

            Vector3 vehCorrected = Vector3.Transform((Vector3)vehForward, _cachedRelativeOffset);
            float vehAzimuth = 0f, vehElev = 0f;
            Vector3.GetAzimuthAndElevation(vehCorrected, out vehAzimuth, out vehElev);

            float vehCompass = (vehAzimuth + (float)Math.PI) - (_cachedNorthAzimuth + (float)Math.PI);
            if (vehCompass < 0) vehCompass += (float)Math.PI * 2f;
            else if (vehCompass > (float)Math.PI * 2f) vehCompass -= (float)Math.PI * 2f;
            playerHeadingRad = vehCompass;

            // The heading above feeds the minimap and map arrows, so it is worked out even with the compass off
            if (!showCompass || showFullMap)
            {
                _ssCompass.Clear();
                return;
            }

            Color zoneCol = GetCurrentAccentColor();

            // compass is already clamped to [0, 2π] above. Normalize to [-1, 1] for tape rendering.
            compass = (compass - (float)Math.PI) / (float)Math.PI;

            float FOV = camera.FovWithZoom;
            // Precompute FOV polynomial once per frame instead of calling Math.Pow in hot loops
            float fovCoeff = FOV * (5.596f * FOV * FOV - 18.43f * FOV + 16.16f);
            float fovCubic = FOV * 12f;
            float baseWidth = GetCompassBaseWidth(compassScale);
            float tapeSpan = (baseWidth * 0.5f) - (0.012f * compassScale);
            float aspect = GetScreenAspect();

            float baseHeight = GetCompassBaseHeight(compassScale);
            double maxCompassCenterX = 0.999 - (baseWidth * 0.5);
            double compassCenterX = compassOffsetX * maxCompassCenterX;

            double maxCompassCenterY = 0.999 - (baseHeight * 0.5);
            double minCompassCenterY = -0.999 + (baseHeight * 0.5);
            double compassT = (compassOffsetY + 1.0) * 0.5;
            double centerY = minCompassCenterY + compassT * (maxCompassCenterY - minCompassCenterY);
            double topY = centerY + (baseHeight * 0.5);

            // Top Row: Cardinals & Ordinals (N, NE, E, SE, S, SW, W, NW)
            float topLabelY = (float)(topY - 0.006f * compassScale);
            // Bottom Row: Major numeric bearing marks (0, 15, 30, 45, 60, etc.) on the same line as waypoint markers
            float bottomLabelY = (float)(topY - 0.020f * compassScale);

            BuildScreenSpaceCompass(playerPos, camera, compass, playerAzimuth, fovCoeff, fovCubic, tapeSpan, aspect,
                baseWidth, baseHeight, compassCenterX, centerY, topY, topLabelY, bottomLabelY, zoneCol);
        }

        /// <summary>
        /// Fills _compassVisibleWps with up to maxCount shown waypoints that fall on the compass tape, nearest first
        /// (activeHudWaypoints is sorted by distance), then known sites in what room is left, so they never push off a GPS point,
        /// signal or KOTH, and a site whose stand-in made the tape is skipped.
        /// </summary>
        private void CollectCompassWaypoints(Vector3D playerPos, IMyCamera camera, float playerAzimuth, float fovCoeff, float fovCubic, float tapeSpan, int maxCount)
        {
            _compassVisibleWps.Clear();
            Array.Clear(_siteStandInDrawn, 0, _siteStandInDrawn.Length);
            for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < activeHudWaypoints.Count && _compassVisibleWps.Count < maxCount; i++)
            {
                var wp = activeHudWaypoints[i];
                if (SkipForPass(ref wp, pass)) continue;
                if (!IsMarkerShown(ref wp, showCompassGps, showCompassSignals, showCompassOwnSignals, showCompassKoth, showCompassSites)) continue;
                if (wp.SiteKind != 0 && wp.DistanceMeters > COMPASS_SITE_MAX_DIST && !wp.HasSignal) continue; // In beacon range it shows like the signal did

                Vector3 toWp = (Vector3)(wp.Coords - playerPos);

                // Early out: Cull waypoints behind the camera view plane.
                // The compass tape covers only a forward arc (~35 deg). Anything behind the camera cannot be on the tape.
                if (Vector3.Dot(toWp, camera.WorldMatrix.Forward) <= 0)
                    continue;

                Vector3 toWpCorrected = Vector3.Transform(toWp, _cachedRelativeOffset);

                float wpAzimuth = 0f, wpElev = 0f;
                Vector3.GetAzimuthAndElevation(toWpCorrected, out wpAzimuth, out wpElev);

                float angleDiff = playerAzimuth - wpAzimuth;
                while (angleDiff > (float)Math.PI) angleDiff -= (float)(Math.PI * 2.0);
                while (angleDiff < -(float)Math.PI) angleDiff += (float)(Math.PI * 2.0);

                float targetCompass = angleDiff / (float)Math.PI;

                if (Math.Abs(targetCompass) > 0.35f) continue;

                float poiScreenOffset = (fovCoeff * targetCompass) + (fovCubic * targetCompass * targetCompass * targetCompass);

                if (poiScreenOffset >= -tapeSpan && poiScreenOffset <= tapeSpan)
                {
                    _compassVisibleWps.Add(new CompassVisibleWp(i, poiScreenOffset));
                    MarkStandInDrawn(ref wp);
                }
            }

            // Sites came in a second pass: sort back to nearest first (at most maxCount items, no allocation),
            // so the farthest-first draw still puts nearer icons on top
            for (int j = 1; j < _compassVisibleWps.Count; j++)
            {
                var item = _compassVisibleWps[j];
                double dist = activeHudWaypoints[item.WpIndex].DistanceMeters;
                int k = j - 1;
                while (k >= 0 && activeHudWaypoints[_compassVisibleWps[k].WpIndex].DistanceMeters > dist)
                {
                    _compassVisibleWps[k + 1] = _compassVisibleWps[k];
                    k--;
                }
                _compassVisibleWps[k + 1] = item;
            }
        }

        /// <summary>
        /// Compass ribbon, written to _ssCompass for Draw() to replay every frame (the layout the Text HUD API
        /// compass had, in the same order and units).
        /// </summary>
        private void BuildScreenSpaceCompass(Vector3D playerPos, IMyCamera camera, float compass, float playerAzimuth,
            float fovCoeff, float fovCubic, float tapeSpan, float aspect, float baseWidth, float baseHeight,
            double centerX, double centerY, double topY, float topLabelY, float bottomLabelY, Color zoneCol)
        {
            if (!_ssCompass.Begin(camera.ViewportSize)) return;
            CheckScreenSpaceAssets();
            float textScale = GetScreenSpaceTextScale(camera.ViewportSize);
            float s = compassScale;
            const MyGuiDrawAlignEnum TOP_CENTER = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP;

            // Frame: card, zone accents (right one hidden when the zone bar docks there), gold pointer and pip
            _ssCompass.AddSprite(MATERIAL_SQUARE, centerX, centerY, baseWidth, baseHeight, GetCardBgColor());
            float accentWidth = 0.005f * s;
            float accentHeight = baseHeight - 0.006f * s;
            _ssCompass.AddSprite(MATERIAL_SQUARE, centerX - baseWidth * 0.5 + 0.005 * s, centerY, accentWidth, accentHeight, zoneCol);
            if (zoneBarMode != ZoneBarMode.CompassDock)
                _ssCompass.AddSprite(MATERIAL_SQUARE, centerX + baseWidth * 0.5 - 0.005 * s, centerY, accentWidth, accentHeight, zoneCol);
            var gold = GvkUi.Title;
            _ssCompass.AddSprite(MATERIAL_NAV_ARROW, centerX, centerY + baseHeight * 0.5, 0.010 * s, 0.010 * s * aspect, gold, (float)Math.PI);
            _ssCompass.AddSprite(MATERIAL_NAV_ARROW, centerX, centerY - baseHeight * 0.5, 0.008 * s, 0.008 * s * aspect, gold);

            // Ticks first, then labels, so labels draw over ticks as they did with Text HUD API
            float tickWidth = 0.0016f * s;
            int ticks = 0;
            int labels = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < COMPASS_GRADUATIONS.Length; i++)
                {
                    var grad = COMPASS_GRADUATIONS[i];
                    if (pass == 1 && grad.TopLabel == null && grad.BottomLabel == null) continue;

                    float offset = compass + grad.Offset;
                    if (offset < -1f) offset += 2f;
                    else if (offset > 1f) offset -= 2f;
                    if (Math.Abs(offset) > 0.35f) continue;

                    float screenOffset = (fovCoeff * offset) + (fovCubic * offset * offset * offset);
                    if (screenOffset > tapeSpan || screenOffset < -tapeSpan) continue;
                    double x = centerX + screenOffset;

                    if (pass == 0)
                    {
                        if (ticks >= COMPASS_MAX_TICKS) continue;
                        ticks++;
                        float h;
                        Color c;
                        switch (grad.Type)
                        {
                            case GraduationType.Cardinal: h = 0.007f * s; c = WithAlpha(GvkUi.Title, 240f / 255f); break;
                            case GraduationType.Ordinal:
                            case GraduationType.MajorMark: h = 0.007f * s; c = new Color(255, 255, 255, 240); break;
                            case GraduationType.MediumTen: h = 0.005f * s; c = new Color(255, 255, 255, 200); break;
                            default: h = 0.003f * s; c = new Color(255, 255, 255, 140); break;
                        }
                        _ssCompass.AddSprite(MATERIAL_SQUARE, x, topY - h * 0.5, tickWidth, h, c);
                        continue;
                    }

                    bool cardinal = grad.Type == GraduationType.Cardinal;
                    if (grad.TopLabel != null && labels < COMPASS_MAX_LABELS)
                    {
                        labels++;
                        _ssCompass.AddText(grad.TopLabel, x, topLabelY, 0.58f * s * textScale,
                            cardinal ? GvkUi.Title : Color.White, TOP_CENTER, true);
                    }
                    if (grad.BottomLabel != null && labels < COMPASS_MAX_LABELS)
                    {
                        labels++;
                        _ssCompass.AddText(grad.BottomLabel, x, bottomLabelY, 0.48f * s * textScale,
                            cardinal ? GvkUi.Title : Color.White, TOP_CENTER, true);
                    }
                }
            }

            // Waypoints farthest first, so nearer icons draw on top
            CollectCompassWaypoints(playerPos, camera, playerAzimuth, fovCoeff, fovCubic, tapeSpan, COMPASS_MAX_WAYPOINTS);
            double playerElevation = Vector3D.Distance(playerPos, PLANET_CENTER) - PLANET_RADIUS;
            for (int j = _compassVisibleWps.Count - 1; j >= 0; j--)
            {
                var vis = _compassVisibleWps[j];
                var wp = activeHudWaypoints[vis.WpIndex];
                double x = centerX + vis.ScreenOffset;
                Color wpColor = GetMarkerColor(ref wp);

                // Full size at 100 m or closer, 70% at 30 km or farther
                float distT = (float)MathHelper.Clamp((wp.DistanceMeters - 100.0) / (30000.0 - 100.0), 0.0, 1.0);
                float distFactor = 1.0f - 0.30f * distT;
                float wWidth = 0.011f * s * distFactor * (wp.SiteKind != 0 ? SITE_ICON_SCALE : 1f);
                float wHeight = wWidth * aspect;
                double spriteCenterY = topY - 0.026 * s;
                double textY = spriteCenterY - wHeight * 0.5 - 0.0015 * s;

                _ssCompass.AddSprite(GetWaypointMaterial(ref wp, playerElevation), x, spriteCenterY, wWidth, wHeight, wpColor);
                // Text HUD API colour tags carried RGB only, so the distance text stays opaque while the icon fades
                _ssCompass.AddText(GetCompassDistanceText(wp.DistanceMeters * 0.001), x, textY, 0.42f * s * distFactor * textScale,
                    new Color(wpColor.R, wpColor.G, wpColor.B), TOP_CENTER, true);
            }
        }

        /// <summary>"1.5k" below 10 km, "12k" above; strings are cached so redraws don't allocate.</summary>
        private static string GetCompassDistanceText(double distKm)
        {
            if (distKm < 10.0)
            {
                int whole = (int)distKm;
                int tenths = (int)((distKm - whole) * 10.0);
                int idx = MathHelper.Clamp(whole * 10 + tenths, 0, 99);
                return _ssDistanceTenths[idx] ?? (_ssDistanceTenths[idx] = (idx / 10) + "." + (idx % 10) + "k");
            }
            int km = (int)distKm;
            if (km >= _ssDistanceWhole.Length) return km + "k";
            return _ssDistanceWhole[km] ?? (_ssDistanceWhole[km] = km + "k");
        }

        /// <summary>Screen API text scale per HUD text scale unit (see GvkScreenLayer.MeasureTextScale).</summary>
        private static float GetScreenSpaceTextScale(Vector2 viewport) => GvkScreenLayer.MeasureTextScale(viewport);

        /// <summary>Logs once if the screen API can't resolve a compass material or the font.</summary>
        private void CheckScreenSpaceAssets()
        {
            if (_ssMaterialsChecked) return;
            _ssMaterialsChecked = true;
            LogIfMaterialMissing(MATERIAL_SQUARE);
            LogIfMaterialMissing(MATERIAL_NAV_ARROW);
            LogIfMaterialMissing(MATERIAL_MARKER_GPS);
            LogIfMaterialMissing(MATERIAL_SITE_TRADE);
            LogIfMaterialMissing(MATERIAL_SITE_BASE);
            LogIfMaterialMissing(MATERIAL_SITE_DEPOT);
            LogIfMaterialMissing(MATERIAL_SITE_CARRIER);
            LogIfMaterialMissing(MATERIAL_SIGNAL_UP);
            LogIfMaterialMissing(MATERIAL_SIGNAL_DOWN);
            LogIfMaterialMissing(MATERIAL_SIGNAL_LEVEL);
            LogIfMaterialMissing(MATERIAL_MAP);
            LogIfMaterialMissing(MATERIAL_RADAR_GRID);
            if (!MyScreenSpaceText.IsFontAvailable(GvkScreenLayer.FONT))
                MyLog.Default.WriteLineAndConsole($"[GVK_ZoneNavCommand] Screen API: font '{GvkScreenLayer.FONT}' not found");
        }

        private static void LogIfMaterialMissing(MyStringId material)
        {
            if (!MyScreenSpaceSprite.IsMaterialAvailable(material))
                MyLog.Default.WriteLineAndConsole($"[GVK_ZoneNavCommand] Screen API: material '{material.String}' not found");
        }

        /// <summary>Keen HUD slate blue at the player's HUD background opacity, as UpdateCardBackgroundOpacities sets.</summary>
        private Color GetCardBgColor()
        {
            float opacity = cachedHudBkOpacity >= 0f ? cachedHudBkOpacity : GetUserHudOpacity();
            return WithAlpha(GvkUi.Button, opacity);
        }

        public override void Draw()
        {
            if (_ssHudDrawFailed || showFullMap) return;
            if (GvkScreenLayer.IsHudHidden()) return;
            try
            {
                // Drawn compass, minimap, zone bar: the order the Text HUD API elements were created in
                if (showCompass && _ssCompass.Count > 0) _ssCompass.Draw();
                if (showMinimap && _ssMinimap.Count > 0) _ssMinimap.Draw();
                if (zoneBarMode != ZoneBarMode.Off && _ssZoneBar.Count > 0) _ssZoneBar.Draw();
            }
            catch (Exception ex)
            {
                // A draw error must not take the game down: the HUD stays off for the rest of the session
                MyLog.Default.WriteLineAndConsole($"[GVK_ZoneNavCommand] HUD draw failed, HUD off for this session: {ex}");
                _ssHudDrawFailed = true;
                ClearScreenSpaceHud();
            }
        }

        private void ClearScreenSpaceHud()
        {
            _ssCompass.Clear();
            _ssMinimap.Clear();
            _ssZoneBar.Clear();
            _refreshCompassNextFrame = true;
            _refreshMinimapNextFrame = true;
        }

        /// <summary>
        /// Updates the Zone Status Panel docked directly below the minimap in the top-right corner.
        /// Keeps the area below the compass ribbon clear for WeaponCore target lock and lead indicator HUDs.
        /// </summary>
        private void UpdateZoneBar()
        {
            if (zoneBarMode == ZoneBarMode.Off || showFullMap)
            {
                _ssZoneBar.Clear();
                return;
            }

            float currentScale;
            float zoneWidth;
            float zoneHeight;
            bool isCompact;
            Vector2D targetPos;

            if (zoneBarMode == ZoneBarMode.CompassDock)
            {
                currentScale = compassScale;
                float compassBaseWidth = GetCompassBaseWidth(currentScale);
                float compassBaseHeight = GetCompassBaseHeight(currentScale);
                zoneWidth = 0.139f * currentScale;
                zoneHeight = compassBaseHeight;
                isCompact = true;

                double maxCompassCenterX = 0.999 - (compassBaseWidth * 0.5);
                double compassCenterX = compassOffsetX * maxCompassCenterX;

                double maxCompassCenterY = 0.999 - (compassBaseHeight * 0.5);
                double minCompassCenterY = -0.999 + (compassBaseHeight * 0.5);
                double compassT = (compassOffsetY + 1.0) * 0.5;
                double compassCenterY = minCompassCenterY + compassT * (maxCompassCenterY - minCompassCenterY);

                float compassGap = 0.006f * currentScale;
                double posX = compassCenterX + (compassBaseWidth * 0.5) + compassGap + (zoneWidth * 0.5);
                double posY = compassCenterY;
                targetPos = new Vector2D(posX, posY);
            }
            else
            {
                currentScale = minimapScale;
                float aspect = GetScreenAspect();
                float radarGridDiameter = (float)minimapSize.Y * 0.95f;
                float radarBoxWidth = (radarGridDiameter / aspect) + 0.024f * currentScale;
                zoneWidth = radarBoxWidth;
                zoneHeight = 0.056f * currentScale;
                isCompact = true;

                if (showMinimap)
                {
                    float minimapBgHeight = (float)minimapSize.Y + (0.008f * currentScale) * aspect;
                    float gap = 0.004f * currentScale;
                    float minimapBgTop = (float)(minimapPosition.Y + minimapBgHeight * 0.5f);
                    float zoneCenterY = minimapBgTop + gap + (zoneHeight * 0.5f);
                    targetPos = new Vector2D(minimapPosition.X, zoneCenterY);
                }
                else
                {
                    double maxCenterX = 0.999 - (zoneWidth * 0.5);
                    double posX = minimapOffsetX * maxCenterX;
                    double maxCenterY = 0.999 - (zoneHeight * 0.5);
                    double minCenterY = -0.995 + (zoneHeight * 0.5);
                    double t = (minimapOffsetY + 1.0) * 0.5;
                    double posY = minCenterY + t * (maxCenterY - minCenterY);
                    targetPos = new Vector2D(posX, posY);
                }
            }

            BuildScreenSpaceZoneBar(targetPos, zoneWidth, zoneHeight, currentScale, isCompact);
        }

        /// <summary>
        /// Zone bar card (the Text HUD API zone bar's layout, in the same units). Each colour that was a colour
        /// tag under Text HUD API is its own text run. pos is the card centre.
        /// </summary>
        private void BuildScreenSpaceZoneBar(Vector2D pos, float zoneWidth, float zoneHeight, float s, bool isCompact)
        {
            var camera = MyAPIGateway.Session?.Camera;
            if (camera == null || !_ssZoneBar.Begin(camera.ViewportSize)) return;
            float textScale = GetScreenSpaceTextScale(camera.ViewportSize);

            double left = pos.X - zoneWidth * 0.5;
            _ssZoneBar.AddSprite(MATERIAL_SQUARE, pos.X, pos.Y, zoneWidth, zoneHeight, GetCardBgColor());
            _ssZoneBar.AddSprite(MATERIAL_SQUARE, left + 0.005 * s, pos.Y, 0.005f * s, zoneHeight - 0.006f * s, GetCurrentAccentColor());

            double textX, line1Y, line2Y;
            float scale1, scale2;
            if (zoneBarMode == ZoneBarMode.CompassDock)
            {
                textX = left + 0.010 * s;
                line1Y = pos.Y + 0.014 * s;
                line2Y = pos.Y - 0.006 * s;
                scale1 = 0.48f * s;
                scale2 = 0.40f * s;
            }
            else
            {
                textX = left + 0.016 * s;
                line1Y = pos.Y + 0.018 * s;
                line2Y = pos.Y - 0.008 * s;
                scale1 = (isCompact ? 0.54f : 0.60f) * s;
                scale2 = (isCompact ? 0.46f : 0.50f) * s;
            }

            int zone = MathHelper.Clamp(currentZoneIndex, 0, 3);
            var white = new Color(255, 255, 255);
            const string SEPARATOR = " | ";
            const string NO_ZONE = "------";

            double room = zoneWidth - (textX - left) - 0.006 * s; // Both lines shrink to this on narrow (4:3) windows

            _ssZoneBar.BeginRuns(textX, line1Y, scale1 * textScale, true);
            int mark = _ssZoneBar.Count;
            if (inSpace) _ssZoneBar.AddRun("[ SPAAAAAACE!!! ]", SPACE_TEXT_COLOR);
            else if (_playerTerritoryMask != 0) AddZoneBarTerritoryTitle(zone, room, scale1 * textScale);
            else _ssZoneBar.AddRun(ZONE_TITLES[zone], ZONE_TEXT_COLORS[zone]);
            _ssZoneBar.FitRuns(mark, room);

            _ssZoneBar.BeginRuns(textX, line2Y, scale2 * textScale, true);
            mark = _ssZoneBar.Count;
            if (inSpace)
            {
                _ssZoneBar.AddRun("Kharak gravity: ", ZONE_LABEL_COLOR);
                _ssZoneBar.AddRun("LOST", SPACE_TEXT_COLOR);
                _ssZoneBar.AddRun(SEPARATOR, ZONE_SEPARATOR_COLOR);
                _ssZoneBar.AddRun("Below: ", ZONE_LABEL_COLOR);
                _ssZoneBar.AddRun(ZONE_SHORT_NAMES[zone], ZONE_TEXT_COLORS[zone]);
                _ssZoneBar.FitRuns(mark, room);
                return;
            }

            // Lower zone and its distance, or a dash in Zone 0
            if (zone == 0) _ssZoneBar.AddRun(NO_ZONE, white);
            else
            {
                _ssZoneBar.AddRun(ZONE_DIST_LABELS[zone - 1], ZONE_LABEL_COLOR);
                _ssZoneBar.AddRun(FormatZoneKm(lastToLowerKm, ref _ssLowerKmKey, ref _ssLowerKmText), ZONE_TEXT_COLORS[zone - 1]);
            }
            _ssZoneBar.AddRun(SEPARATOR, ZONE_SEPARATOR_COLOR);
            // Higher zone and its distance, or a dash in Zone 3
            if (zone == 3) _ssZoneBar.AddRun(NO_ZONE, white);
            else
            {
                _ssZoneBar.AddRun(ZONE_DIST_LABELS[zone + 1], ZONE_LABEL_COLOR);
                _ssZoneBar.AddRun(FormatZoneKm(lastRemainingKm, ref _ssHigherKmKey, ref _ssHigherKmText), ZONE_TEXT_COLORS[zone + 1]);
            }
            _ssZoneBar.FitRuns(mark, room);
        }

        private static readonly string[] ZONE_BAR_PREFIXES = { "[ ZONE 0: ", "[ ZONE 1: ", "[ ZONE 2: ", "[ ZONE 3: " };
        private const string ZONE_BAR_SUFFIX = " ]";

        /// <summary>
        /// Zone bar line 1 inside alliance territory: "[ ZONE 2: <ALLIANCE> ]" in place of the zone's nickname
        /// (owner, 2026-11-24), the name in the player's standing colour. The tag stands in when the name won't fit;
        /// where territories overlap, each tag, split by " / ".
        /// </summary>
        private void AddZoneBarTerritoryTitle(int zone, double room, float scale)
        {
            int mask = _playerTerritoryMask;
            bool contested = (mask & (mask - 1)) != 0;
            Color zoneColor = ZONE_TEXT_COLORS[zone];
            _ssZoneBar.AddRun(ZONE_BAR_PREFIXES[zone], zoneColor);
            bool first = true;
            for (int t = 0; t < _territoryUpper.Length; t++)
            {
                if ((mask & (1 << t)) == 0) continue;
                if (!first) _ssZoneBar.AddRun(" / ", ZONE_SEPARATOR_COLOR);
                first = false;
                string tag = GvkKnownSites.Territories[t].Tag;
                string name = _territoryUpper[t] ?? tag;
                if (contested || _ssZoneBar.MeasureWidthHud(ZONE_BAR_PREFIXES[zone], scale) + _ssZoneBar.MeasureWidthHud(name, scale)
                                 + _ssZoneBar.MeasureWidthHud(ZONE_BAR_SUFFIX, scale) > room)
                    name = tag;
                _ssZoneBar.AddRun(name, _territoryColor[t]);
            }
            _ssZoneBar.AddRun(ZONE_BAR_SUFFIX, zoneColor);
        }

        /// <summary>"12.3 km", re-formatted only when the shown tenth changes.</summary>
        private static string FormatZoneKm(double km, ref long key, ref string text)
        {
            long k = (long)Math.Round(km * 10.0);
            if (text == null || k != key)
            {
                key = k;
                text = (k / 10.0).ToString("F1") + " km";
            }
            return text;
        }

        /// <summary>
        /// Updates the corner tactical vector radar.
        /// </summary>
        private void UpdateMinimap(Vector3D playerPos)
        {
            if (!showMinimap || showFullMap)
            {
                _ssMinimap.Clear();
                return;
            }

            float aspect = GetScreenAspect();
            float radarGridDiameter = (float)minimapSize.Y * 0.95f;
            float radarBoxWidth = (radarGridDiameter / aspect) + 0.024f * minimapScale;
            float minimapBgWidth = radarBoxWidth;
            float minimapBgHeight = (float)minimapSize.Y + (0.008f * minimapScale) * aspect;

            // Ensure minimapPosition and minimapBg match mode width and offsets
            float zoneHeight = 0.056f * minimapScale;
            float gap = 0.004f * minimapScale;

            double maxCenterX = 0.999 - (minimapBgWidth * 0.5);
            double targetPosX = minimapOffsetX * maxCenterX;

            bool zoneBarOnMinimap = (zoneBarMode == ZoneBarMode.RadarDock);
            float topBuffer = (zoneBarOnMinimap ? (zoneHeight + gap) : 0f) + (minimapBgHeight * 0.5f);
            float bottomBuffer = minimapBgHeight * 0.5f;
            double maxCenterY = 0.999 - topBuffer;
            double minCenterY = -0.995 + bottomBuffer;
            double t = (minimapOffsetY + 1.0) * 0.5;
            double targetPosY = minCenterY + t * (maxCenterY - minCenterY);

            if (Math.Abs(minimapPosition.X - targetPosX) > 0.0005 || Math.Abs(minimapPosition.Y - targetPosY) > 0.0005 || Math.Abs(_minimapLayoutBgWidth - minimapBgWidth) > 0.001f)
            {
                ApplyMinimapScale(minimapScale);
            }

            Color zoneCol = GetCurrentAccentColor();
            BuildScreenSpaceMinimap(playerPos, aspect, minimapBgWidth, minimapBgHeight, radarBoxWidth, zoneCol);
        }

        /// <summary>
        /// Radar frame on the ground: forward is the camera's look flattened against Kharak's gravity.
        /// cameraForward is the unflattened look, for GetRadarVehicleAngle.
        /// </summary>
        private void GetRadarFrame(Vector3D playerPos, IMyCamera camera, out Vector3D upNormal, out Vector3D fwdTangent,
            out Vector3D rightTangent, out Vector3D cameraForward)
        {
            upNormal = Vector3D.Normalize(playerPos - PLANET_CENTER);
            cameraForward = Vector3D.Forward;
            if (camera != null)
            {
                cameraForward = camera.WorldMatrix.Forward;
            }
            else
            {
                var controlled = MyAPIGateway.Session.ControlledObject;
                var playerGrid = (controlled as IMyCubeBlock)?.CubeGrid;
                if (playerGrid != null)
                    cameraForward = playerGrid.WorldMatrix.Forward;
                else if (controlled?.Entity != null)
                    cameraForward = controlled.Entity.WorldMatrix.Forward;
            }

            fwdTangent = cameraForward - Vector3D.Dot(cameraForward, upNormal) * upNormal;
            if (fwdTangent.LengthSquared() < 0.001)
            {
                Vector3D altVec = camera != null ? camera.WorldMatrix.Up : Vector3D.Up;
                fwdTangent = altVec - Vector3D.Dot(altVec, upNormal) * upNormal;
            }
            if (fwdTangent.LengthSquared() < 0.001) fwdTangent = Vector3D.Forward;
            fwdTangent.Normalize();
            rightTangent = Vector3D.Cross(fwdTangent, upNormal);
            rightTangent.Normalize();
        }

        /// <summary>
        /// A marker's offset from the radar centre in HUD units. False when it isn't plotted: beyond
        /// 100 km, or a signal within 30 m (own-construct clutter). clamped is true when it's pinned to the rim.
        /// </summary>
        private bool TryGetRadarMarkerOffset(ref ActiveHudWaypoint wp, Vector3D playerPos, Vector3D fwdTangent, Vector3D rightTangent,
            float radiusX, float radiusY, out Vector2D offset, out bool clamped)
        {
            const double MAX_RADAR_TRACK_DIST = 100000.0;
            offset = Vector2D.Zero;
            clamped = false;

            Vector3D toTarget = wp.Coords - playerPos;
            double fwdDist = Vector3D.Dot(toTarget, fwdTangent);
            double rightDist = Vector3D.Dot(toTarget, rightTangent);
            double horizDist = Math.Sqrt(fwdDist * fwdDist + rightDist * rightDist);
            if (horizDist > MAX_RADAR_TRACK_DIST || (wp.IsSignal && horizDist < 30.0)) return false;

            double normDist;
            if (radarScale == RadarScaleMode.Logarithmic)
            {
                // 3-decade log scale: inner ring (1/3) 300 m, middle ring (2/3) 3 km, outer ring 30 km
                if (horizDist <= 300.0)
                {
                    normDist = (horizDist / 300.0) * LOG_THIRD;
                }
                else if (horizDist <= 30000.0)
                {
                    normDist = LOG_THIRD + (LOG_THIRD * Math.Log10(horizDist / 300.0));
                    if (normDist > 0.98) normDist = 0.98;
                }
                else
                {
                    normDist = 0.98; // Beyond 30 km (up to 100 km): on the rim
                    clamped = true;
                }
            }
            else if (horizDist <= radarRangeMeters)
            {
                normDist = horizDist / radarRangeMeters;
            }
            else
            {
                normDist = 0.98; // Beyond range: on the rim
                clamped = true;
            }

            double angle = Math.Atan2(rightDist, fwdDist); // 0 = fwd, pi/2 = right
            offset = new Vector2D(Math.Sin(angle) * radiusX * normDist, Math.Cos(angle) * radiusY * normDist);
            return true;
        }

        /// <summary>Heading of the player's vehicle or character on the radar, clockwise from camera forward.</summary>
        private float GetRadarVehicleAngle(Vector3D upNormal, Vector3D fwdTangent, Vector3D rightTangent, Vector3D cameraForward)
        {
            Vector3D vehForward;
            var controlledObj = MyAPIGateway.Session.ControlledObject;
            var pGrid = (controlledObj as IMyCubeBlock)?.CubeGrid;
            if (pGrid != null)
                vehForward = pGrid.WorldMatrix.Forward;
            else if (controlledObj?.Entity != null)
                vehForward = controlledObj.Entity.WorldMatrix.Forward;
            else
                vehForward = cameraForward;

            Vector3D vehTangent = vehForward - Vector3D.Dot(vehForward, upNormal) * upNormal;
            if (vehTangent.LengthSquared() <= 0.001) return 0f;
            vehTangent.Normalize();
            return (float)Math.Atan2(Vector3D.Dot(vehTangent, rightTangent), Vector3D.Dot(vehTangent, fwdTangent));
        }

        /// <summary>Radar angles toward the Crossroads (lower zone) and away from it (higher zone). False on top of it.</summary>
        private bool TryGetRadarZoneAngles(Vector3D playerPos, Vector3D upNormal, Vector3D fwdTangent, Vector3D rightTangent,
            out double angleToLower, out double angleToHigher)
        {
            angleToLower = 0.0;
            angleToHigher = 0.0;
            Vector3D toCrossroads = CROSSROADS_BEACON - playerPos;
            Vector3D toCrossroadsTangent = toCrossroads - Vector3D.Dot(toCrossroads, upNormal) * upNormal;
            if (toCrossroadsTangent.LengthSquared() <= 0.001) return false;

            toCrossroadsTangent.Normalize();
            // 0 = forward (up), pi/2 = right, -pi/2 = left, pi = rear
            angleToLower = Math.Atan2(Vector3D.Dot(toCrossroadsTangent, rightTangent), Vector3D.Dot(toCrossroadsTangent, fwdTangent));
            angleToHigher = angleToLower + Math.PI;
            if (angleToHigher > Math.PI) angleToHigher -= 2.0 * Math.PI;
            return true;
        }

        /// <summary>
        /// Tactical radar, drawn in the layout and order the Text HUD API minimap had (card, accent, map or grid, FOV lines, zone lines, label, markers, arrow).
        /// </summary>
        private void BuildScreenSpaceMinimap(Vector3D playerPos, float aspect, float bgWidth, float bgHeight, float radarBoxWidth, Color zoneCol)
        {
            var camera = MyAPIGateway.Session?.Camera;
            if (camera == null || !_ssMinimap.Begin(camera.ViewportSize)) return;
            CheckScreenSpaceAssets();
            float s = minimapScale;
            double cx = minimapPosition.X;
            double cy = minimapPosition.Y;

            _ssMinimap.AddSprite(MATERIAL_SQUARE, cx, cy, bgWidth, bgHeight, GetCardBgColor());
            _ssMinimap.AddSprite(MATERIAL_SQUARE, cx - bgWidth * 0.5 + 0.005 * s, cy, 0.005f * s, bgHeight - 0.006f * s, zoneCol);

            float markerSize = 0.012f * s;
            float arrowSize = 0.017f * s;
            Color arrowColor = GetPlayerArrowColor();
            double playerElevation = Vector3D.Distance(playerPos, PLANET_CENTER) - PLANET_RADIUS;
            int markers = 0;
            int sites = 0;

            // Tactical radar
            float gridDiameter = (float)minimapSize.Y * 0.95f;
            _ssMinimap.AddSprite(MATERIAL_RADAR_GRID, cx, cy, gridDiameter / aspect, gridDiameter, WithAlpha(GvkUi.Accent, 200f / 255f));

            // Radar circle radius on screen (aspect corrected for a 1:1 circle)
            float radiusY = (float)minimapSize.Y * 0.46f;
            float radiusX = radiusY / aspect;

            // Camera view "V": widens and narrows with game FOV and zoom
            if (showRadarFovLines)
            {
                float halfHFov = (float)Math.Atan(aspect * Math.Tan(camera.FovWithZoom * 0.5f));
                double armX = Math.Sin(halfHFov) * radiusX;
                double armY = Math.Cos(halfHFov) * radiusY;
                var fovLineColor = WithAlpha(GvkUi.Accent, 110f / 255f);
                _ssMinimap.AddSprite(MATERIAL_SQUARE, cx - armX * 0.5, cy + armY * 0.5, 0.0008, radiusY, fovLineColor, -halfHFov);
                _ssMinimap.AddSprite(MATERIAL_SQUARE, cx + armX * 0.5, cy + armY * 0.5, 0.0008, radiusY, fovLineColor, halfHFov);
            }

            Vector3D upNormal, fwdTangent, rightTangent, forward;
            GetRadarFrame(playerPos, camera, out upNormal, out fwdTangent, out rightTangent, out forward);

            // Lines toward the lower zone and away to the higher one, on the outer third of the radar
            double angleToLower, angleToHigher;
            if (showRadarZoneLines && TryGetRadarZoneAngles(playerPos, upNormal, fwdTangent, rightTangent, out angleToLower, out angleToHigher))
            {
                const double MID_RADIUS_FACTOR = 5.0 / 6.0;
                float lineWidth = 0.0014f * s;
                float lineHeight = radiusY * (1f / 3f);
                if (currentZoneIndex > 0)
                {
                    Color c = GetZoneColor(currentZoneIndex - 1);
                    _ssMinimap.AddSprite(MATERIAL_SQUARE,
                        cx + Math.Sin(angleToLower) * radiusX * MID_RADIUS_FACTOR, cy + Math.Cos(angleToLower) * radiusY * MID_RADIUS_FACTOR,
                        lineWidth, lineHeight, new Color(c.R, c.G, c.B, 220), (float)angleToLower);
                }
                if (currentZoneIndex < 3)
                {
                    Color c = GetZoneColor(currentZoneIndex + 1);
                    _ssMinimap.AddSprite(MATERIAL_SQUARE,
                        cx + Math.Sin(angleToHigher) * radiusX * MID_RADIUS_FACTOR, cy + Math.Cos(angleToHigher) * radiusY * MID_RADIUS_FACTOR,
                        lineWidth, lineHeight, new Color(c.R, c.G, c.B, 220), (float)angleToHigher);
                }
            }

            // Range label, bottom right of the card
            float textScale = GetScreenSpaceTextScale(camera.ViewportSize);
            var labelGrey = GvkUi.TextDim;
            // Right-aligned by measure: a fixed start ran off the card on narrow (4:3) windows, where text is wider in HUD units
            float rangeScale = 0.46f * s * textScale;
            bool logScale = radarScale == RadarScaleMode.Logarithmic;
            double rangeW = logScale ? _ssMinimap.MeasureWidthHud(RADAR_LOG_TEXT, rangeScale) + _ssMinimap.MeasureWidthHud(RADAR_LOG_TAG, rangeScale)
                                     : _ssMinimap.MeasureWidthHud(GetRadarRangeText(), rangeScale);
            _ssMinimap.BeginRuns(cx + radarBoxWidth * 0.5 - 0.008 * s - rangeW, cy - bgHeight * 0.5 + 0.016 * s, rangeScale, true);
            if (logScale)
            {
                _ssMinimap.AddRun(RADAR_LOG_TEXT, labelGrey);
                _ssMinimap.AddRun(RADAR_LOG_TAG, GvkUi.Accent);
            }
            else
            {
                _ssMinimap.AddRun(GetRadarRangeText(), labelGrey);
            }

            // Pass 0 picks the markers and marks the sites they stand in for, pass 1 draws the sites, pass 2 draws
            // pass 0's picks again on top (same order and tests, so the same picks)
            Array.Clear(_siteStandInDrawn, 0, _siteStandInDrawn.Length);
            for (int pass = 0; pass < 3; pass++)
            {
                if (pass == 2) markers = 0;
                for (int i = 0; i < activeHudWaypoints.Count; i++)
                {
                    if (pass == 1 ? sites >= MINIMAP_MAX_SITES : markers >= MINIMAP_MAX_MARKERS) break;
                    var wp = activeHudWaypoints[i];
                    if (SkipForPass(ref wp, pass)) continue;
                    if (!IsMarkerShown(ref wp, showMinimapGps, showMinimapSignals, showMinimapOwnSignals, showMinimapKoth, showMinimapSites)) continue;
                    Vector2D offset;
                    bool clamped;
                    if (!TryGetRadarMarkerOffset(ref wp, playerPos, fwdTangent, rightTangent, radiusX, radiusY, out offset, out clamped)) continue;
                    if (clamped && wp.SiteKind != 0) continue; // Sites show only inside the radar range, not pinned to the rim
                    if (!TakeMinimapSlot(ref wp, ref markers, ref sites)) continue;
                    if (pass == 0)
                    {
                        MarkStandInDrawn(ref wp);
                        continue;
                    }
                    Color c = GetMarkerColor(ref wp);
                    float ms = wp.SiteKind != 0 ? markerSize * SITE_ICON_SCALE : markerSize;
                    _ssMinimap.AddSprite(GetWaypointMaterial(ref wp, playerElevation), cx + offset.X, cy + offset.Y,
                        ms, ms * aspect, clamped ? c * 0.80f : c);
                }
            }

            _ssMinimap.AddSprite(MATERIAL_NAV_ARROW, cx, cy, arrowSize, arrowSize * aspect, arrowColor,
                GetRadarVehicleAngle(upNormal, fwdTangent, rightTangent, forward));
        }

        private const string RADAR_LOG_TEXT = "30 KM ", RADAR_LOG_TAG = "(LOG)";

        /// <summary>Linear radar range label, "3 KM" or "1.5 KM"; re-formatted only when the range changes.</summary>
        private string GetRadarRangeText()
        {
            int rangeKmInt = (int)(radarRangeMeters * 0.001);
            int rangeKmDec = (int)((radarRangeMeters * 0.001 - rangeKmInt) * 10.0);
            long key = rangeKmInt * 10L + rangeKmDec;
            if (_ssRangeText == null || key != _ssRangeKey)
            {
                _ssRangeKey = key;
                _ssRangeText = rangeKmDec > 0 ? rangeKmInt + "." + rangeKmDec + " KM" : rangeKmInt + " KM";
            }
            return _ssRangeText;
        }

        private const double MAP_CLUSTER_DIST_SQ = 0.038 * 0.038;   // ~2-3 km: markers this close share one stack
        private const double MAP_MERGE_MAX_DIST_SQ = 0.076 * 0.076; // Overlapping stacks closer than this merge; farther ones fall back to dots
        private const float MAP_LINE_SPACING = 0.024f;
        private const double MAP_LABEL_OFFSET_X = 0.015;
        private const double MAP_LABEL_OFFSET_Y = 0.005;
        private const double MAP_ICON_HALF_WIDTH = 0.005;  // Visible dot, not the full 0.0225 sprite
        private const double MAP_ICON_HALF_HEIGHT = 0.008;
        private const double MAP_LABEL_GAP = 0.003;        // Minimum clear space between two stacks

        /// <summary>
        /// Groups labelled waypoints into label stacks so no two stacks overlap. Filtered-out signals are skipped;
        /// waypoints whose label is toggled off become plain dots. Nearby stacks that would overlap merge; a stack that would still overlap a nearer one
        /// draws its markers as dots at their true positions. Runs only when <see cref="_mapLayoutDirty"/> is set.
        /// The map is drawn mapWidth x mapHeight with map UV (viewU, viewV) at the frame centre; waypoints outside
        /// the frame are left out. Labels are measured with the screen API font.
        /// </summary>
        private void BuildMapLayout(float mapWidth, float mapHeight, double viewU, double viewV, float frameWidth, float frameHeight)
        {
            _mapPlainWps.Clear();
            _mapLabelWps.Clear();
            _mapLabelRoots.Clear();
            _mapLabelWidths.Clear();
            _mapLabelHasText.Clear();
            _cachedMapClusters.Clear();
            double halfW = frameWidth * 0.5;
            double halfH = frameHeight * 0.5;

            // Proximity clustering around each cluster's running centre (waypoints arrive nearest first)
            for (int i = 0; i < activeHudWaypoints.Count; i++)
            {
                var wp = activeHudWaypoints[i];
                if (!IsMarkerShown(ref wp, true, true, showMapOwnSignals, showMapKoth, showMapSites)) continue;
                if (wp.SiteKind != 0 && wp.HasStandIn) continue; // Its GPS point or NPC signal shows here instead (the map shows every one)
                Vector2D wpOffset = GetMapOffset(wp.MapUV, mapWidth, mapHeight, viewU, viewV);
                if (Math.Abs(wpOffset.X) > halfW || Math.Abs(wpOffset.Y) > halfH) continue;
                // KOTH labels always show; sites follow GPS Labels. With its label off a marker still joins a stack when
                // Stack Icons is on, as an icon with no name (owner, 2026-11-24: "icons without labels should be allowed
                // to stack if needed"); with stacking off too it is a plain dot at its true position
                bool hasText = wp.KothState != 0 || (wp.IsSignal ? showMapSignalLabels : showMapGpsLabels);
                if (!hasText && !stackMapIcons)
                {
                    _mapPlainWps.Add(i);
                    continue;
                }

                float width = 0f;
                if (hasText) GetSsMapLabel(i, out width);

                int found = -1;
                for (int c = 0; c < _cachedMapClusters.Count; c++)
                {
                    if (Vector2D.DistanceSquared(wpOffset, _cachedMapClusters[c].Position) < MAP_CLUSTER_DIST_SQ)
                    {
                        found = c;
                        break;
                    }
                }

                if (found < 0)
                {
                    found = _cachedMapClusters.Count;
                    _cachedMapClusters.Add(new MapCluster { Parent = found });
                }

                var cluster = _cachedMapClusters[found];
                cluster.Sum += wpOffset;
                cluster.Count++;
                cluster.Position = cluster.Sum / cluster.Count;
                if (width > cluster.LabelWidth) cluster.LabelWidth = width;
                _cachedMapClusters[found] = cluster;

                _mapLabelWps.Add(i);
                _mapLabelRoots.Add(found);
                _mapLabelWidths.Add(width);
                _mapLabelHasText.Add(hasText);
            }

            float labelScale = MAP_LABEL_SCALE * _ssMapTextScale;
            float overflowWidth = (float)_ssMap.MeasureWidthHud(GetSsMapOverflowText(99), labelScale);
            if (overflowWidth <= 0f) overflowWidth = 0.15f; // About the width of a full-length label
            double textHeight = _ssMap.LineHeightHud(labelScale);
            _mapOverflowWidth = overflowWidth;
            _mapTextHeight = textHeight > 0.0 ? textHeight : 0.016;

            // Merge nearby clusters whose stacks overlap; the nearer (lower index) cluster absorbs the other
            bool merged = true;
            for (int pass = 0; merged && pass < 32; pass++)
            {
                merged = false;
                for (int a = 0; a < _cachedMapClusters.Count; a++)
                {
                    var ca = _cachedMapClusters[a];
                    if (ca.Count == 0) continue;

                    for (int b = a + 1; b < _cachedMapClusters.Count; b++)
                    {
                        var cb = _cachedMapClusters[b];
                        if (cb.Count == 0) continue;
                        if (Vector2D.DistanceSquared(ca.Position, cb.Position) > MAP_MERGE_MAX_DIST_SQ) continue;
                        if (!MapStacksOverlap(ref ca, true, ref cb, true, overflowWidth)) continue;

                        ca.Sum += cb.Sum;
                        ca.Count += cb.Count;
                        ca.Position = ca.Sum / ca.Count;
                        if (cb.LabelWidth > ca.LabelWidth) ca.LabelWidth = cb.LabelWidth;
                        cb.Count = 0;
                        cb.Parent = a;
                        _cachedMapClusters[b] = cb;
                        merged = true;
                    }

                    _cachedMapClusters[a] = ca;
                }
            }

            for (int j = 0; j < _mapLabelRoots.Count; j++)
            {
                int r = _mapLabelRoots[j];
                while (_cachedMapClusters[r].Parent != r) r = _cachedMapClusters[r].Parent;
                _mapLabelRoots[j] = r;
            }

            // Place stacks nearest first. A stack that would overlap an already placed one tries the other stack
            // direction, then labels on the left; a lone marker instead tries its name half a line lower or higher
            // on each side. If every placement overlaps, the cluster shows dots only.
            _mapClusterShown.Clear();
            for (int c = 0; c < _cachedMapClusters.Count; c++)
            {
                _mapClusterShown.Add(0);
                var cluster = _cachedMapClusters[c];
                cluster.Labelled = false;
                if (cluster.Count == 1)
                {
                    cluster.DirY = GetMapStackDir(cluster.Position);
                    for (int option = 0; option < 6 && !cluster.Labelled; option++)
                    {
                        cluster.FlipX = option >= 3;
                        int nudge = option % 3;
                        cluster.NudgeY = nudge == 0 ? 0.0 : (nudge == 1 ? -0.5 : 0.5) * MAP_LINE_SPACING;
                        cluster.Labelled = !OverlapsPlacedStack(ref cluster, c, overflowWidth);
                    }
                }
                else if (cluster.Count > 1)
                {
                    float preferredDir = GetMapStackDir(cluster.Position);
                    for (int option = 0; option < 4 && !cluster.Labelled; option++)
                    {
                        cluster.FlipX = option >= 2;
                        cluster.DirY = (option % 2 == 0) ? preferredDir : -preferredDir;
                        cluster.Labelled = !OverlapsPlacedStack(ref cluster, c, overflowWidth);
                    }
                }
                _cachedMapClusters[c] = cluster;
            }
        }

        private bool OverlapsPlacedStack(ref MapCluster cluster, int clusterIndex, float overflowWidth)
        {
            // A lone marker's icon stays at its true position whether or not it gets a label, so only its text
            // can collide. Placed stacks keep their icons in the box so later labels don't cover them.
            bool candidateIcons = cluster.Count > 1;

            // Text running past the frame's right edge would go under the filter buttons: try the other side instead
            double minX, maxX, minY, maxY;
            GetMapStackBox(ref cluster, overflowWidth, false, out minX, out maxX, out minY, out maxY);
            if (maxX > _ssMapW * 0.5) return true;

            for (int p = 0; p < clusterIndex; p++)
            {
                var placed = _cachedMapClusters[p];
                if (placed.Labelled && MapStacksOverlap(ref cluster, candidateIcons, ref placed, true, overflowWidth))
                    return true;
            }
            return false;
        }

        private bool MapStacksOverlap(ref MapCluster a, bool aIcons, ref MapCluster b, bool bIcons, float overflowWidth)
        {
            double aMinX, aMaxX, aMinY, aMaxY, bMinX, bMaxX, bMinY, bMaxY;
            GetMapStackBox(ref a, overflowWidth, aIcons, out aMinX, out aMaxX, out aMinY, out aMaxY);
            GetMapStackBox(ref b, overflowWidth, bIcons, out bMinX, out bMaxX, out bMinY, out bMaxY);
            return aMinX < bMaxX + MAP_LABEL_GAP && bMinX < aMaxX + MAP_LABEL_GAP
                && aMinY < bMaxY + MAP_LABEL_GAP && bMinY < aMaxY + MAP_LABEL_GAP;
        }

        /// <summary>
        /// Screen rectangle covered by a cluster's label text (up to 5 lines) and, if requested, its icon column.
        /// Uses the measured text height and the visible dot size, not the full sprite size.
        /// </summary>
        private void GetMapStackBox(ref MapCluster c, float overflowWidth, bool includeIcons, out double minX, out double maxX, out double minY, out double maxY)
        {
            int lines = c.Count <= 5 ? c.Count : 5;
            float width = (c.Count > 5 && overflowWidth > c.LabelWidth) ? overflowWidth : c.LabelWidth;
            float dirY = c.DirY != 0f ? c.DirY : GetMapStackDir(c.Position);
            double lastSlotY = c.Position.Y + dirY * MAP_LINE_SPACING * (lines - 1);
            double topSlotY = Math.Max(c.Position.Y, lastSlotY);
            double bottomSlotY = Math.Min(c.Position.Y, lastSlotY);

            // Each text line hangs down from MAP_LABEL_OFFSET_Y above its slot, shifted by any nudge
            maxY = topSlotY + MAP_LABEL_OFFSET_Y + c.NudgeY;
            minY = bottomSlotY + MAP_LABEL_OFFSET_Y - _mapTextHeight + c.NudgeY;
            if (c.FlipX)
            {
                maxX = c.Position.X - MAP_LABEL_OFFSET_X;
                minX = maxX - width;
            }
            else
            {
                minX = c.Position.X + MAP_LABEL_OFFSET_X;
                maxX = minX + width;
            }

            if (includeIcons)
            {
                minX = Math.Min(minX, c.Position.X - MAP_ICON_HALF_WIDTH);
                maxX = Math.Max(maxX, c.Position.X + MAP_ICON_HALF_WIDTH);
                minY = Math.Min(minY, bottomSlotY - MAP_ICON_HALF_HEIGHT);
                maxY = Math.Max(maxY, topSlotY + MAP_ICON_HALF_HEIGHT);
            }
        }

        /// <summary>Label text starts right of the icon, or ends left of it when the stack is flipped.</summary>
        private static double GetMapLabelOffsetX(ref MapCluster c, float labelWidth)
        {
            return c.FlipX ? -MAP_LABEL_OFFSET_X - labelWidth : MAP_LABEL_OFFSET_X;
        }

        /// <summary>Stacks grow downward, or upward when the cluster is near the bottom edge of the map.</summary>
        private float GetMapStackDir(Vector2D pos) => pos.Y < -0.40 ? 1.0f : -1.0f;

        /// <summary>Map UV to a point on the map frame, the map drawn width x height with (viewU, viewV) at the centre.</summary>
        private static Vector2D GetMapOffset(Vector2 uv, float fullMapWidth, float fullMapHeight, double viewU = 0.5, double viewV = 0.5)
            => new Vector2D((uv.X - viewU) * fullMapWidth, (viewV - uv.Y) * fullMapHeight);

        /// <summary>
        /// Draw list for the satellite map's game screen: the map's layout seen through the zoom and
        /// pan view. Rebuilt on the HUD tick and whenever the view moves. The map picture and KOTH rings are clipped
        /// to the frame; markers outside it are left out, so labels near an edge stay whole.
        /// </summary>
        private void BuildScreenSpaceFullMap()
        {
            var camera = MyAPIGateway.Session?.Camera;
            if (camera == null || !_ssMap.Begin(camera.ViewportSize)) return;
            CheckScreenSpaceAssets();
            Vector2 viewport = camera.ViewportSize;
            float aspect = viewport.X / viewport.Y;
            float textScale = GetScreenSpaceTextScale(viewport);
            if (viewport != _ssMapViewport || textScale != _ssMapTextScale)
            {
                _ssMapViewport = viewport;
                _ssMapTextScale = textScale;
                _ssMapLabelSrc.Clear(); // Label widths were measured at the old size
                _ssMapLabelText.Clear();
                _ssMapLabelWidth.Clear();
                _mapLayoutDirty = true;
            }

            float frameW = 1.40f;
            float frameH = 0.70f * aspect;
            _ssMapW = frameW;
            _ssMapH = frameH;
            ClampMapView();
            float mapW = frameW * _mapZoom;
            float mapH = frameH * _mapZoom;
            double viewU = _mapViewU;
            double viewV = _mapViewV;
            double playerElevation = Vector3D.Distance(_mapPlayerPos, PLANET_CENTER) - PLANET_RADIUS;

            if (_mapLayoutDirty)
            {
                _mapLayoutDirty = false;
                BuildMapLayout(mapW, mapH, viewU, viewV, frameW, frameH);
            }

            Color card = GetCardBgColor();
            _ssMap.AddSprite(MATERIAL_SQUARE, 0.0, 0.0, 2.0, 2.0, new Color(0, 0, 0, 190));
            _ssMap.AddSprite(MATERIAL_SQUARE, 0.0, 0.0, frameW + 0.02f, frameH + 0.01f * aspect, card);

            _ssMap.BeginClip(0.0, 0.0, frameW, frameH);
            _ssMap.AddSprite(MATERIAL_MAP, (0.5 - viewU) * mapW, (viewV - 0.5) * mapH, mapW, mapH, Color.White);
            AddBakedBands(_ssMap, viewU, viewV, mapW, mapH, showZoneTint, showTerritoryFade);
            if (showMapZones)
                AddZoneOverlay(_ssMap, 0.0, 0.0, mapW, mapH, viewU, viewV, frameW * 0.5, frameH * 0.5, aspect,
                    ZONE_MAP_LINE_W, ZONE_MAP_MIN_SEG);
            if (showMapTerritories)
                AddTerritoryLines(_ssMap, 0.0, 0.0, mapW, mapH, viewU, viewV, frameW * 0.5, frameH * 0.5, aspect,
                    ZONE_MAP_LINE_W, ZONE_MAP_MIN_SEG);
            AddSsKothRings(mapW, mapH, viewU, viewV, aspect);
            _ssMap.EndClip();

            // Unlabelled markers first, then label stacks on top
            _mapHits.Clear();
            for (int k = 0; k < _mapPlainWps.Count; k++)
            {
                int wpIdx = _mapPlainWps[k];
                AddSsMapMarker(wpIdx, GetMapOffset(activeHudWaypoints[wpIdx].MapUV, mapW, mapH, viewU, viewV), playerElevation, aspect);
            }
            for (int j = 0; j < _mapLabelWps.Count; j++)
            {
                if (stackMapIcons && _cachedMapClusters[_mapLabelRoots[j]].Labelled) continue;
                int wpIdx = _mapLabelWps[j];
                AddSsMapMarker(wpIdx, GetMapOffset(activeHudWaypoints[wpIdx].MapUV, mapW, mapH, viewU, viewV), playerElevation, aspect);
            }

            for (int c = 0; c < _mapClusterShown.Count; c++)
                _mapClusterShown[c] = 0;
            float labelScale = MAP_LABEL_SCALE * textScale;
            for (int j = 0; j < _mapLabelWps.Count; j++)
            {
                int root = _mapLabelRoots[j];
                var cluster = _cachedMapClusters[root];
                if (!cluster.Labelled) continue;

                int slot = _mapClusterShown[root]++;
                int itemsToShow = (cluster.Count <= 5) ? cluster.Count : 4;
                if (slot >= itemsToShow) continue;

                int wpIdx = _mapLabelWps[j];
                Vector2D slotPos = cluster.Position + new Vector2D(0.0, cluster.DirY * MAP_LINE_SPACING * slot);
                if (stackMapIcons) AddSsMapMarker(wpIdx, slotPos, playerElevation, aspect);
                if (!_mapLabelHasText[j]) continue;

                float width;
                string text = GetSsMapLabel(wpIdx, out width);
                double lx = slotPos.X + GetMapLabelOffsetX(ref cluster, width);
                double ly = slotPos.Y + MAP_LABEL_OFFSET_Y + cluster.NudgeY;
                _ssMap.AddText(text, lx, ly, labelScale, Color.White, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP, true);
                AddMapHit(wpIdx, lx, lx + width, ly - _mapTextHeight, ly);
            }

            var overflowColor = GvkUi.Accent;
            for (int c = 0; c < _cachedMapClusters.Count; c++)
            {
                var cluster = _cachedMapClusters[c];
                if (!cluster.Labelled || cluster.Count <= 5) continue;
                Vector2D slotPos = cluster.Position + new Vector2D(0.0, cluster.DirY * MAP_LINE_SPACING * 4f);
                _ssMap.AddText(GetSsMapOverflowText(cluster.Count - 4), slotPos.X + GetMapLabelOffsetX(ref cluster, _mapOverflowWidth),
                    slotPos.Y + MAP_LABEL_OFFSET_Y, labelScale, overflowColor, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP, true);
            }

            // Player arrow on top, while it is inside the frame
            Vector2D playerOffset = GetMapOffset(WorldToMapUV(_mapPlayerPos), mapW, mapH, viewU, viewV);
            if (Math.Abs(playerOffset.X) <= frameW * 0.5 && Math.Abs(playerOffset.Y) <= frameH * 0.5)
            {
                Color arrowColor = GetPlayerArrowColor();
                _ssMap.AddSprite(MATERIAL_NAV_ARROW, playerOffset.X, playerOffset.Y, 0.018f, 0.018f * aspect, arrowColor, -playerHeadingRad);
            }

            AddSsMapHeader(frameW, frameH, aspect, textScale, card);
        }

        /// <summary>Header docked above the frame, and the controls below it.</summary>
        private void AddSsMapHeader(float frameW, float frameH, float aspect, float textScale, Color card)
        {
            float topOfFrame = (frameH + 0.01f * aspect) * 0.5f;
            float headerH = 0.056f;
            float headerW = frameW + 0.02f;
            float headerY = topOfFrame + 0.004f + headerH * 0.5f;
            int zone = MathHelper.Clamp(currentZoneIndex, 0, 3);

            _ssMap.AddSprite(MATERIAL_SQUARE, 0.0, headerY, headerW, headerH, card);
            _ssMap.AddSprite(MATERIAL_SQUARE, -headerW * 0.5f + 0.005f, headerY, 0.005f, headerH - 0.006f,
                inSpace ? GvkUi.Space : GetZoneColor(zone));

            double textX = -headerW * 0.5f + 0.016f;
            float line1Scale = 0.62f * textScale;
            _ssMap.BeginRuns(textX, headerY + 0.013f, line1Scale, true);
            _ssMap.AddRun(MAP_TITLE_TEXT, GvkUi.Title);
            _ssMap.AddRun(MAP_SECTOR_TEXT, MAP_HEADER_GREY);
            _ssMap.AddRun(ZONE_TITLES[zone], ZONE_TEXT_COLORS[zone]);
            if (TerritoriesShown && _playerTerritoryMask != 0)
            {
                double used = _ssMap.MeasureWidthHud(MAP_TITLE_TEXT, line1Scale) + _ssMap.MeasureWidthHud(MAP_SECTOR_TEXT, line1Scale)
                            + _ssMap.MeasureWidthHud(ZONE_TITLES[zone], line1Scale);
                double buttonLeft = headerW * 0.5f - 0.008f - (_ssMap.MeasureWidthHud(MAP_SETTINGS_TEXT, 0.58f * textScale) + 0.05);
                AddHeaderTerritory(buttonLeft - 0.012 - textX - used, line1Scale);
            }

            _ssMap.BeginRuns(textX, headerY - 0.011f, 0.50f * textScale, true);
            _ssMap.AddRun("Crossroads: ", GvkUi.Text);
            _ssMap.AddRun(FormatZoneKm(lastDistKm, ref _ssMapCrossKey, ref _ssMapCrossText), Color.White);
            _ssMap.AddRun(" | ", MAP_HEADER_GREY);
            _ssMap.AddRun(MAP_BORDER_LABELS[zone], MAP_HEADER_GREY);
            _ssMap.AddRun(FormatZoneKm(zone == 3 ? lastDistZ3Km : lastRemainingKm, ref _ssMapBorderKey, ref _ssMapBorderText),
                ZONE_TEXT_COLORS[zone == 3 ? 3 : zone + 1]);
            if (_ssMapCloseText == null || !ReferenceEquals(_ssMapCloseKeyLabel, mapKeyLabel))
            {
                _ssMapCloseKeyLabel = mapKeyLabel;
                _ssMapCloseText = "          [ Press '" + mapKeyLabel + "' or ESC to Close ]";
            }
            _ssMap.AddRun(_ssMapCloseText, MAP_HINT_COLOR);

            // Settings button box; BuildMapSettingsButton draws it
            _mapSettingsScale = 0.58f * textScale;
            double buttonW = _ssMap.MeasureWidthHud(MAP_SETTINGS_TEXT, _mapSettingsScale) + 0.05;
            double buttonH = headerH - 0.012f;
            double buttonRight = headerW * 0.5f - 0.008f;
            _mapSettingsBox = new MapHit
            {
                Wp = -1, MinX = buttonRight - buttonW, MaxX = buttonRight,
                MinY = headerY - buttonH * 0.5, MaxY = headerY + buttonH * 0.5
            };

            // Filter buttons: a column on its own card docked to the right of the frame, top aligned with it
            double btnW = MAP_FILTER_BTN_W;
            double btnH = btnW * aspect;
            double gapY = 0.005 * aspect;
            double colW = btnW + 0.010;
            double colH = MAP_FILTER_IDS.Length * (btnH + gapY) + gapY;
            double colX = headerW * 0.5 + 0.004 + colW * 0.5;
            _ssMap.AddSprite(MATERIAL_SQUARE, colX, topOfFrame - colH * 0.5, colW, colH, card);
            for (int f = 0; f < MAP_FILTER_IDS.Length; f++)
            {
                double cyBtn = topOfFrame - gapY - btnH * 0.5 - f * (btnH + gapY);
                _mapFilterBoxes[f] = new MapHit
                {
                    Wp = -1, MinX = colX - btnW * 0.5, MaxX = colX + btnW * 0.5,
                    MinY = cyBtn - btnH * 0.5, MaxY = cyBtn + btnH * 0.5
                };
            }

            // Controls line on its own card docked under the frame, mirroring the header
            float hintScale = MAP_HINT_SCALE * textScale;
            string hint = TerritoriesShown && _territoryUV.Count > 1 ? MAP_CONTROLS_HINT_TERRITORY : MAP_CONTROLS_HINT;
            double hintW = _ssMap.MeasureWidthHud(hint, hintScale) + 0.04;
            if (hintW > headerW && !ReferenceEquals(hint, MAP_CONTROLS_HINT)) // The key is the first thing to go
            {
                hint = MAP_CONTROLS_HINT;
                hintW = _ssMap.MeasureWidthHud(hint, hintScale) + 0.04;
            }
            double hintH = _ssMap.LineHeightHud(hintScale) + 0.012;
            double hintTop = -topOfFrame - 0.004;
            _ssMap.AddSprite(MATERIAL_SQUARE, 0.0, hintTop - hintH * 0.5, hintW, hintH, card);
            _ssMap.AddText(hint, 0.0, hintTop - 0.006, hintScale, GvkUi.Text,
                MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP, true);
        }

        /// <summary>
        /// Header line 1's tail: the alliance territory the player is in, or "Contested:" and each alliance where they
        /// overlap, names in the player's standing colour. Alliance tags stand in where names would run under the
        /// Settings button; with no room even for those, it is left out.
        /// </summary>
        private void AddHeaderTerritory(double room, float scale)
        {
            int mask = _playerTerritoryMask;
            bool contested = (mask & (mask - 1)) != 0;
            for (int pass = 0; pass < 2; pass++)
            {
                bool tags = pass == 1;
                if (MeasureHeaderTerritory(mask, contested, tags, scale) > room) continue;
                _ssMap.AddRun(contested ? MAP_CONTESTED_TEXT : MAP_TERRITORY_TEXT, MAP_HEADER_GREY);
                bool first = true;
                for (int t = 0; t < _territoryName.Length; t++)
                {
                    if ((mask & (1 << t)) == 0) continue;
                    if (!first) _ssMap.AddRun(" / ", MAP_HEADER_GREY);
                    first = false;
                    _ssMap.AddRun(tags ? GvkKnownSites.Territories[t].Tag : _territoryName[t], _territoryColor[t]);
                }
                return;
            }
        }

        private double MeasureHeaderTerritory(int mask, bool contested, bool tags, float scale)
        {
            double w = _ssMap.MeasureWidthHud(contested ? MAP_CONTESTED_TEXT : MAP_TERRITORY_TEXT, scale);
            bool first = true;
            for (int t = 0; t < _territoryName.Length; t++)
            {
                if ((mask & (1 << t)) == 0) continue;
                if (!first) w += _ssMap.MeasureWidthHud(" / ", scale);
                first = false;
                w += _ssMap.MeasureWidthHud(tags ? GvkKnownSites.Territories[t].Tag : _territoryName[t] ?? string.Empty, scale);
            }
            return w;
        }

        private void AddSsMapMarker(int wpIdx, Vector2D pos, double playerElevation, float aspect)
        {
            var wp = activeHudWaypoints[wpIdx];
            float iconSize = wp.SiteKind != 0 ? 0.0225f * SITE_ICON_SCALE : 0.0225f;
            _ssMap.AddSprite(GetWaypointMaterial(ref wp, playerElevation), pos.X, pos.Y, iconSize, iconSize * aspect, GetMarkerColor(ref wp));
            AddMapHit(wpIdx, pos.X - MAP_HOVER_HALF, pos.X + MAP_HOVER_HALF, pos.Y - MAP_HOVER_HALF * aspect, pos.Y + MAP_HOVER_HALF * aspect);
        }

        private void AddMapHit(int wpIdx, double minX, double maxX, double minY, double maxY)
        {
            _mapHits.Add(new MapHit { Wp = wpIdx, MinX = minX, MaxX = maxX, MinY = minY, MaxY = maxY });
        }

        private void AddSsKothRings(float mapW, float mapH, double viewU, double viewV, float aspect)
        {
            if (!showMapKoth) return;
            for (int w = 0; w < activeHudWaypoints.Count; w++)
            {
                var wp = activeHudWaypoints[w];
                if (wp.KothState == 0 || wp.KothRadius <= 0) continue;
                Vector2D center = GetMapOffset(wp.MapUV, mapW, mapH, viewU, viewV);
                double radius = Math.Max(wp.KothMapRadius * mapW, 0.004);
                Color color = GetMarkerColor(ref wp);
                for (int i = 0; i < KOTH_RING_DOTS; i++)
                {
                    _ssMap.AddSprite(MATERIAL_SQUARE, center.X + KOTH_RING_COS[i] * radius, center.Y + KOTH_RING_SIN[i] * radius * aspect,
                        0.0025f, 0.0025f * aspect, color);
                }
            }
        }

        /// <summary>A waypoint's map label (names past 20 characters cut to first 12...last 4) and its width, cached.</summary>
        private string GetSsMapLabel(int wpIdx, out float width)
        {
            while (_ssMapLabelSrc.Count <= wpIdx)
            {
                _ssMapLabelSrc.Add(null);
                _ssMapLabelText.Add(null);
                _ssMapLabelWidth.Add(0f);
            }

            string name = activeHudWaypoints[wpIdx].Name ?? string.Empty;
            if (_ssMapLabelSrc[wpIdx] != name)
            {
                string text = name.Length > 20 ? name.Substring(0, 12) + "..." + name.Substring(name.Length - 4) : name;
                float measured = (float)_ssMap.MeasureWidthHud(text, MAP_LABEL_SCALE * _ssMapTextScale);
                _ssMapLabelSrc[wpIdx] = name;
                _ssMapLabelText[wpIdx] = text;
                _ssMapLabelWidth[wpIdx] = measured > 0f ? measured : 0.15f;
            }
            width = _ssMapLabelWidth[wpIdx];
            return _ssMapLabelText[wpIdx];
        }

        private string GetSsMapOverflowText(int hiddenCount)
        {
            if (hiddenCount < 0 || hiddenCount >= _ssMapOverflowText.Length) return "+" + hiddenCount + " more...";
            return _ssMapOverflowText[hiddenCount] ?? (_ssMapOverflowText[hiddenCount] = "+" + hiddenCount + " more...");
        }

        /// <summary>Keeps the zoom in range and the view inside the map picture, so the frame is always full.</summary>
        private void ClampMapView()
        {
            _mapZoom = MathHelper.Clamp(_mapZoom, 1f, MAP_MAX_ZOOM);
            double half = 0.5 / _mapZoom;
            _mapViewU = MathHelper.Clamp(_mapViewU, half, 1.0 - half);
            _mapViewV = MathHelper.Clamp(_mapViewV, half, 1.0 - half);
        }

        /// <summary>Zooms by mouse-wheel notches, keeping the map point under <paramref name="at"/> in place.</summary>
        private bool ZoomMap(int wheelDelta, Vector2D at)
        {
            float oldZoom = _mapZoom;
            float newZoom = MathHelper.Clamp((float)(oldZoom * Math.Pow(MAP_ZOOM_STEP, wheelDelta / 120.0)), 1f, MAP_MAX_ZOOM);
            if (newZoom == oldZoom) return false;
            double u = _mapViewU + at.X / (_ssMapW * oldZoom);
            double v = _mapViewV - at.Y / (_ssMapH * oldZoom);
            _mapZoom = newZoom;
            _mapViewU = u - at.X / (_ssMapW * newZoom);
            _mapViewV = v + at.Y / (_ssMapH * newZoom);
            ClampMapView();
            return true;
        }

        private bool IsMapKeyNewPress()
        {
            var input = MyAPIGateway.Input;
            return input.IsNewKeyPressed(mapKey)
                && input.IsAnyShiftKeyPressed() == mapKeyShift
                && input.IsAnyCtrlKeyPressed() == mapKeyCtrl
                && input.IsAnyAltKeyPressed() == mapKeyAlt;
        }

        /// <summary>Map screen input, every frame it has focus: close key, wheel zoom, drag pan, right-click GPS, hover.</summary>
        public void HandleMapScreenInput()
        {
            if (_ssMapDrawFailed) return; // Fading out after an error
            try
            {
                HandleMapScreenInputCore();
            }
            catch (Exception ex)
            {
                FailMapScreen("input", ex);
            }
        }

        private void HandleMapScreenInputCore()
        {
            if (!showFullMap || _ssMapW <= 0f) return;
            var input = MyAPIGateway.Input;

            if (_mapKeyLatch)
            {
                if (!input.IsKeyPress(mapKey)) _mapKeyLatch = false;
            }
            else if (IsMapKeyNewPress())
            {
                CloseFullMap();
                return;
            }

            Vector2D cursor;
            bool hasCursor = _ssMap.TryGetCursorHud(out cursor);
            bool inFrame = hasCursor && Math.Abs(cursor.X) <= _ssMapW * 0.5 && Math.Abs(cursor.Y) <= _ssMapH * 0.5;
            bool viewMoved = false;
            int wheel = input.DeltaMouseScrollWheelValue();

            // Settings button in the header swaps the map for the settings screen
            bool onSettings = hasCursor && !_mapMenuOpen && IsInMapHit(ref _mapSettingsBox, cursor);
            if (onSettings && input.IsNewLeftMousePressed())
            {
                _ssMapTip.Clear();
                _mapDragging = false;
                OpenSettingsScreen(true); // Closes the map; it reopens when settings closes
                return;
            }
            // Filter buttons right of the frame flip the Satellite Map settings in place
            int onFilter = -1;
            if (hasCursor && !_mapMenuOpen && !_mapDragging)
            {
                for (int f = 0; f < _mapFilterBoxes.Length; f++)
                {
                    if (_mapFilterBoxes[f].MaxX > _mapFilterBoxes[f].MinX && IsInMapHit(ref _mapFilterBoxes[f], cursor))
                    {
                        onFilter = f;
                        break;
                    }
                }
            }
            if (onFilter >= 0 && input.IsNewLeftMousePressed())
            {
                _ssMapTip.Clear();
                DoSetting(MAP_FILTER_IDS[onFilter]);
                _mapLayoutDirty = true;
                BuildScreenSpaceFullMap();
                BuildMapSettingsButton(onSettings, onFilter);
                return;
            }
            BuildMapSettingsButton(onSettings, onFilter);

            // The right-click menu takes the clicks while it is open; Esc is handled in OnMapScreenCancelling
            if (_mapMenuOpen)
            {
                bool leftClick = hasCursor && input.IsNewLeftMousePressed();
                if (input.IsNewKeyPressed(MyKeys.Enter) || (leftClick && IsInMapHit(ref _mapMenuOk, cursor)))
                {
                    ConfirmMapMenu();
                    return;
                }
                if (leftClick && _mapMenuRemove && IsInMapHit(ref _mapMenuDelete, cursor))
                {
                    ConfirmMapMenu(true);
                    return;
                }
                if (leftClick)
                {
                    CloseMapMenu(); // Cancel, or a click anywhere else
                    return;
                }
                if (input.IsNewRightMousePressed())
                {
                    CloseMapMenu();
                    if (inFrame) OpenMapMenu(cursor);
                }
                else if (wheel != 0 || !hasCursor)
                {
                    CloseMapMenu(); // Zooming moves the map out from under the menu
                }

                if (_mapMenuOpen)
                {
                    BuildMapMenu(hasCursor, cursor);
                    return;
                }
            }

            if (wheel != 0) viewMoved = ZoomMap(wheel, inFrame ? cursor : Vector2D.Zero);

            if (_mapDragging)
            {
                if (!hasCursor || !input.IsLeftMousePressed())
                {
                    _mapDragging = false;
                }
                else if (cursor != _mapDragLast)
                {
                    _mapViewU -= (cursor.X - _mapDragLast.X) / (_ssMapW * _mapZoom);
                    _mapViewV += (cursor.Y - _mapDragLast.Y) / (_ssMapH * _mapZoom);
                    _mapDragLast = cursor;
                    ClampMapView();
                    viewMoved = true;
                }
            }
            else if (inFrame && input.IsNewLeftMousePressed())
            {
                _mapDragging = true;
                _mapDragLast = cursor;
            }

            if (viewMoved)
            {
                _mapLayoutDirty = true;
                BuildScreenSpaceFullMap();
            }

            if (inFrame && !_mapDragging && input.IsNewRightMousePressed())
            {
                OpenMapMenu(cursor);
                BuildMapMenu(true, cursor);
                return;
            }

            UpdateMapTooltip(inFrame && !_mapDragging, cursor);
        }

        /// <summary>
        /// The header's Settings button and the filter buttons, rebuilt every input frame for their hover look. A filter
        /// button that is on shows its icon white, off dimmed; hovering one names its setting and state beside it.
        /// </summary>
        private void BuildMapSettingsButton(bool hover, int hoverFilter)
        {
            if (!_ssMapButton.Begin(_ssMapViewport)) return;
            float aspect = _ssMapViewport.X / _ssMapViewport.Y;
            for (int f = 0; f < _mapFilterBoxes.Length; f++)
            {
                var fb = _mapFilterBoxes[f];
                if (fb.MaxX <= fb.MinX) continue;
                bool on = IsMapFilterOn(MAP_FILTER_IDS[f]);
                bool over = f == hoverFilter;
                double fx = (fb.MinX + fb.MaxX) * 0.5;
                double fy = (fb.MinY + fb.MaxY) * 0.5;
                double fw = fb.MaxX - fb.MinX;
                _ssMapButton.AddSprite(MATERIAL_SQUARE, fx, fy, fw, fb.MaxY - fb.MinY,
                    over ? GvkUi.ButtonHover : on ? GvkUi.ButtonDim : GvkUi.ButtonOff);
                if (on) _ssMapButton.AddSprite(MATERIAL_SQUARE, fb.MinX + 0.002, fy, 0.003, (fb.MaxY - fb.MinY) * 0.7, GvkUi.Accent);
                double icon = fw * 0.72;
                _ssMapButton.AddSprite(GetMapFilterIcon(f), fx, fy, icon, icon * aspect, on ? Color.White : Color.White * 0.32f);
            }
            if (hoverFilter >= 0 && hoverFilter < _mapFilterBoxes.Length)
                AddMapFilterTip(hoverFilter, aspect);

            var b = _mapSettingsBox;
            double cx = (b.MinX + b.MaxX) * 0.5;
            double cy = (b.MinY + b.MaxY) * 0.5;
            _ssMapButton.AddSprite(MATERIAL_SQUARE, cx, cy, b.MaxX - b.MinX, b.MaxY - b.MinY,
                hover ? GvkUi.ButtonHover : GvkUi.ButtonDim);
            if (hover) _ssMapButton.AddSprite(MATERIAL_SQUARE, cx, b.MinY + 0.002, b.MaxX - b.MinX, 0.004, GvkUi.Accent);
            _ssMapButton.AddText(MAP_SETTINGS_TEXT, cx, cy, _mapSettingsScale, hover ? Color.White : GvkUi.Accent,
                MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, true);
        }

        private bool IsMapFilterOn(int id)
        {
            switch (id)
            {
                case S_MAP_KOTH: return showMapKoth;
                case S_MAP_OWN: return showMapOwnSignals;
                case S_MAP_SITES: return showMapSites;
                case S_MAP_TERRITORY: return TerritoriesShown;
                case S_MAP_ZONES: return showMapZones || showZoneTint;
                case S_MAP_GPS_LABELS: return showMapGpsLabels;
                case S_MAP_SIGNAL_LABELS: return showMapSignalLabels;
                case S_MAP_STACK: return stackMapIcons;
            }
            return false;
        }

        /// <summary>A filter button's icon; the two four-state overlays show one per state.</summary>
        private MyStringId GetMapFilterIcon(int f)
        {
            int id = MAP_FILTER_IDS[f];
            if (id == S_MAP_ZONES) return ZONE_STATE_ICONS[OverlayState(showMapZones, showZoneTint)];
            if (id == S_MAP_TERRITORY) return TERRITORY_STATE_ICONS[OverlayState(showMapTerritories, showTerritoryFade)];
            return MAP_FILTER_ICONS[f];
        }

        private bool TerritoriesShown { get { return showMapTerritories || showTerritoryFade; } }

        /// <summary>0 lines and fade, 1 lines, 2 fade, 3 off: the order a click steps through.</summary>
        private static int OverlayState(bool lines, bool fade)
        {
            return lines ? (fade ? 0 : 1) : (fade ? 2 : 3);
        }

        /// <summary>"Setting: On/Off" in a box to the left of a hovered filter button.</summary>
        private void AddMapFilterTip(int f, float aspect)
        {
            var fb = _mapFilterBoxes[f];
            int id = MAP_FILTER_IDS[f];
            Color valueColor;
            string label = GetSettingLabel(id);
            string value = GetSettingValue(id, out valueColor);
            const string SEP = ": ";
            float scale = _mapSettingsScale;
            double textW = _ssMapButton.MeasureWidthHud(label, scale) + _ssMapButton.MeasureWidthHud(SEP, scale)
                         + _ssMapButton.MeasureWidthHud(value, scale);
            double lineH = _ssMapButton.LineHeightHud(scale);
            double padX = 0.008;
            double padY = 0.005 * aspect;
            double boxW = textW + padX * 2.0;
            double boxH = lineH + padY * 2.0;
            double right = fb.MinX - 0.008;
            double cy = (fb.MinY + fb.MaxY) * 0.5;
            _ssMapButton.AddSprite(MATERIAL_SQUARE, right - boxW * 0.5, cy, boxW, boxH, GvkUi.Panel);
            _ssMapButton.BeginRuns(right - boxW + padX, cy + lineH * 0.5, scale, true);
            _ssMapButton.AddRun(label, Color.White);
            _ssMapButton.AddRun(SEP, MAP_HEADER_GREY);
            _ssMapButton.AddRun(value, valueColor);
        }

        /// <summary>The marker or label under a point, last drawn first; -1 if none.</summary>
        private int FindMapHit(Vector2D cursor)
        {
            for (int i = _mapHits.Count - 1; i >= 0; i--)
            {
                var h = _mapHits[i];
                if (IsInMapHit(ref h, cursor))
                    return h.Wp >= 0 && h.Wp < activeHudWaypoints.Count ? h.Wp : -1;
            }
            return -1;
        }

        private static bool IsInMapHit(ref MapHit h, Vector2D p)
        {
            return p.X >= h.MinX && p.X <= h.MaxX && p.Y >= h.MinY && p.Y <= h.MaxY;
        }

        /// <summary>
        /// Right-click menu at a point: on a GPS marker or label it offers to hide or delete that GPS, anywhere else to add
        /// one there. Signals, KOTH and known-site markers aren't GPS points, so a click on them offers to add one.
        /// </summary>
        private void OpenMapMenu(Vector2D cursor)
        {
            _ssMapTip.Clear();
            _mapDragging = false;
            int wpIdx = FindMapHit(cursor);
            _mapMenuRemove = wpIdx >= 0 && !activeHudWaypoints[wpIdx].IsSignal && activeHudWaypoints[wpIdx].KothState == 0
                && activeHudWaypoints[wpIdx].SiteKind == 0;
            _mapMenuAt = cursor;
            if (_mapMenuRemove)
            {
                var wp = activeHudWaypoints[wpIdx];
                _mapMenuGpsName = wp.Name ?? string.Empty;
                _mapMenuGpsCoords = wp.Coords;
                _mapMenuTitle = "GPS '" + _mapMenuGpsName + "'";
            }
            else
            {
                _mapMenuU = MathHelper.Clamp(_mapViewU + cursor.X / (_ssMapW * _mapZoom), 0.0, 1.0);
                _mapMenuV = MathHelper.Clamp(_mapViewV - cursor.Y / (_ssMapH * _mapZoom), 0.0, 1.0);
                _mapMenuTitle = "Add a GPS here?";
            }
            _mapMenuOpen = true;
        }

        private void CloseMapMenu()
        {
            _mapMenuOpen = false;
            _ssMapMenu.Clear();
        }

        /// <summary>The menu's first button (Enter too): Add GPS, or Hide on a GPS's menu. delete: its Delete button.</summary>
        private void ConfirmMapMenu(bool delete = false)
        {
            bool gpsMenu = _mapMenuRemove;
            CloseMapMenu();
            if (!gpsMenu) AddGpsFromMap(_mapMenuU, _mapMenuV);
            else if (delete) RemoveGpsFromMap(_mapMenuGpsName, _mapMenuGpsCoords);
            else HideGpsFromMap(_mapMenuGpsName, _mapMenuGpsCoords);
        }

        /// <summary>Esc closes the right-click menu first, then the map.</summary>
        public bool OnMapScreenCancelling()
        {
            if (!_mapMenuOpen)
            {
                // The map fades out without input frames, so drop hover looks and tooltips now
                _ssMapTip.Clear();
                BuildMapSettingsButton(false, -1);
                return true;
            }
            CloseMapMenu();
            return false;
        }

        /// <summary>
        /// The right-click menu card: a title, then the buttons (Add GPS and Cancel, or for a GPS Hide, Delete and Cancel);
        /// the one under the cursor lights up.
        /// </summary>
        private void BuildMapMenu(bool hasCursor, Vector2D cursor)
        {
            _ssMapTip.Clear();
            if (!_ssMapMenu.Begin(_ssMapViewport)) return;
            float aspect = _ssMapViewport.X / _ssMapViewport.Y;
            float titleScale = 0.60f * _ssMapTextScale;
            float buttonScale = 0.60f * _ssMapTextScale;
            string okText = _mapMenuRemove ? "Hide" : "Add GPS";
            const string DELETE_TEXT = "Delete";
            const string CANCEL_TEXT = "Cancel";
            int buttons = _mapMenuRemove ? 3 : 2;

            // Where the new GPS goes, so the player sees the spot before confirming
            if (!_mapMenuRemove)
                _ssMapMenu.AddSprite(MATERIAL_MARKER_GPS, _mapMenuAt.X, _mapMenuAt.Y, 0.0225f, 0.0225f * aspect, GvkUi.Accent);

            double titleW = _ssMapMenu.MeasureWidthHud(_mapMenuTitle, titleScale);
            double titleH = _ssMapMenu.LineHeightHud(titleScale);
            double buttonTextH = _ssMapMenu.LineHeightHud(buttonScale);
            double buttonW = Math.Max(Math.Max(_ssMapMenu.MeasureWidthHud(okText, buttonScale), _ssMapMenu.MeasureWidthHud(CANCEL_TEXT, buttonScale)),
                _ssMapMenu.MeasureWidthHud(DELETE_TEXT, buttonScale)) + 0.03;
            double buttonH = buttonTextH + 0.008 * aspect;
            double padX = 0.010;
            double padY = 0.008 * aspect;
            double gap = 0.010;
            double boxW = 0.004 + padX * 2.0 + Math.Max(titleW, buttonW * buttons + gap * (buttons - 1));
            double boxH = padY * 3.0 + titleH + buttonH;

            double left = _mapMenuAt.X + 0.016;
            double top = _mapMenuAt.Y - 0.016 * aspect;
            if (left + boxW > 0.99) left = _mapMenuAt.X - 0.016 - boxW;
            if (top - boxH < -0.99) top = _mapMenuAt.Y + 0.016 * aspect + boxH;

            Color accent = GvkUi.Accent;
            _ssMapMenu.AddSprite(MATERIAL_SQUARE, left + boxW * 0.5, top - boxH * 0.5, boxW, boxH, GvkUi.Panel);
            _ssMapMenu.AddSprite(MATERIAL_SQUARE, left + 0.002, top - boxH * 0.5, 0.003, boxH, accent);
            double textX = left + 0.004 + padX;
            _ssMapMenu.AddText(_mapMenuTitle, textX, top - padY, titleScale, Color.White, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP, true);

            double buttonTop = top - padY * 2.0 - titleH;
            double x = textX;
            _mapMenuOk = AddMapMenuButton(okText, x, buttonTop, buttonW, buttonH, buttonScale, accent, hasCursor, cursor);
            x += buttonW + gap;
            if (_mapMenuRemove)
            {
                _mapMenuDelete = AddMapMenuButton(DELETE_TEXT, x, buttonTop, buttonW, buttonH, buttonScale, GvkPalette.SettingOff, hasCursor, cursor);
                x += buttonW + gap;
            }
            AddMapMenuButton(CANCEL_TEXT, x, buttonTop, buttonW, buttonH, buttonScale, GvkUi.TextDim, hasCursor, cursor);
        }

        private MapHit AddMapMenuButton(string text, double left, double top, double width, double height, float scale, Color color, bool hasCursor, Vector2D cursor)
        {
            var box = new MapHit { Wp = -1, MinX = left, MaxX = left + width, MinY = top - height, MaxY = top };
            bool hover = hasCursor && IsInMapHit(ref box, cursor);
            _ssMapMenu.AddSprite(MATERIAL_SQUARE, left + width * 0.5, top - height * 0.5, width, height,
                hover ? GvkUi.ButtonHover : GvkUi.Button);
            if (hover) _ssMapMenu.AddSprite(MATERIAL_SQUARE, left + width * 0.5, top - height + 0.002, width, 0.004, color);
            _ssMapMenu.AddText(text, left + width * 0.5, top - height * 0.5, scale, hover ? Color.White : color,
                MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, true);
            return box;
        }

        /// <summary>Card beside the cursor for the marker or label under it: full name, what it is, how far.</summary>
        private void UpdateMapTooltip(bool active, Vector2D cursor)
        {
            _ssMapTip.Clear();
            if (!active) return;

            int wpIdx = FindMapHit(cursor);
            if (wpIdx < 0)
            {
                AddGroundTooltip(cursor);
                return;
            }
            if (!_ssMapTip.Begin(_ssMapViewport)) return;

            var wp = activeHudWaypoints[wpIdx];
            float nameScale = 0.60f * _ssMapTextScale;
            float infoScale = 0.50f * _ssMapTextScale;
            string name = wp.Name ?? string.Empty;
            string kind = GetMapMarkerKind(ref wp);
            string dist = FormatZoneKm(Vector3D.Distance(_mapPlayerPos, wp.Coords) * 0.001, ref _ssTipDistKey, ref _ssTipDistText);
            const string GAP = "   ";

            double nameW = _ssMapTip.MeasureWidthHud(name, nameScale);
            double infoW = _ssMapTip.MeasureWidthHud(kind, infoScale) + _ssMapTip.MeasureWidthHud(GAP, infoScale)
                         + _ssMapTip.MeasureWidthHud(dist, infoScale);
            double nameH = _ssMapTip.LineHeightHud(nameScale);
            double infoH = _ssMapTip.LineHeightHud(infoScale);
            float aspect = _ssMapViewport.X / _ssMapViewport.Y;
            double padX = 0.008;
            double padY = 0.006 * aspect;
            double boxW = 0.004 + padX * 2.0 + Math.Max(nameW, infoW);
            double boxH = padY * 2.0 + nameH + infoH;

            // Below and right of the cursor, flipped where it would run off the window
            double left = cursor.X + 0.016;
            double top = cursor.Y - 0.016 * aspect;
            if (left + boxW > 0.99) left = cursor.X - 0.016 - boxW;
            if (top - boxH < -0.99) top = cursor.Y + 0.016 * aspect + boxH;

            _ssMapTip.AddSprite(MATERIAL_SQUARE, left + boxW * 0.5, top - boxH * 0.5, boxW, boxH, GvkUi.Panel);
            _ssMapTip.AddSprite(MATERIAL_SQUARE, left + 0.002, top - boxH * 0.5, 0.003, boxH, wp.DisplayColor);
            double textX = left + 0.004 + padX;
            _ssMapTip.AddText(name, textX, top - padY, nameScale, Color.White, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP, true);
            _ssMapTip.BeginRuns(textX, top - padY - nameH, infoScale, true);
            _ssMapTip.AddRun(kind, wp.DisplayColor);
            _ssMapTip.AddRun(GAP, Color.White);
            _ssMapTip.AddRun(dist, GvkUi.TextDim);
        }

        /// <summary>
        /// Hover card for bare ground (the player's own arrow included): the alliance territory under the cursor and the
        /// player's standing with it, or "Contested territory" and each alliance's standing where they overlap.
        /// </summary>
        private void AddGroundTooltip(Vector2D cursor)
        {
            if (!TerritoriesShown || _territoryCosA.Length == 0) return;
            double u = _mapViewU + cursor.X / (_ssMapW * _mapZoom);
            double v = _mapViewV - cursor.Y / (_ssMapH * _mapZoom);
            if (u < 0.0 || u > 1.0 || v < 0.0 || v > 1.0) return;
            int mask = GetTerritoryMaskAt(u, v);
            if (mask == 0 || !_ssMapTip.Begin(_ssMapViewport)) return;

            bool contested = (mask & (mask - 1)) != 0;
            int only = 0;
            if (!contested) while ((mask >> only) != 1) only++;
            float nameScale = 0.60f * _ssMapTextScale;
            float infoScale = 0.50f * _ssMapTextScale;
            string name = contested ? CONTESTED_LABEL : _territoryLabel[only] ?? string.Empty;
            const string GAP = "   ";

            // Info line: the standing alone, or "<alliance> <standing>" per alliance where they overlap
            double infoW = 0.0;
            bool first = true;
            for (int t = 0; t < _territoryCosA.Length; t++)
            {
                if ((mask & (1 << t)) == 0) continue;
                if (!first) infoW += _ssMapTip.MeasureWidthHud(GAP, infoScale);
                first = false;
                if (contested) infoW += _ssMapTip.MeasureWidthHud(_territoryName[t] ?? string.Empty, infoScale)
                                      + _ssMapTip.MeasureWidthHud(" ", infoScale);
                infoW += _ssMapTip.MeasureWidthHud(_territoryStanding[t] ?? string.Empty, infoScale);
            }
            double nameW = _ssMapTip.MeasureWidthHud(name, nameScale);
            double nameH = _ssMapTip.LineHeightHud(nameScale);
            double infoH = _ssMapTip.LineHeightHud(infoScale);
            float aspect = _ssMapViewport.X / _ssMapViewport.Y;
            double padX = 0.008;
            double padY = 0.006 * aspect;
            double boxW = 0.004 + padX * 2.0 + Math.Max(nameW, infoW);
            double boxH = padY * 2.0 + nameH + infoH;

            double left = cursor.X + 0.016;
            double top = cursor.Y - 0.016 * aspect;
            if (left + boxW > 0.99) left = cursor.X - 0.016 - boxW;
            if (top - boxH < -0.99) top = cursor.Y + 0.016 * aspect + boxH;

            _ssMapTip.AddSprite(MATERIAL_SQUARE, left + boxW * 0.5, top - boxH * 0.5, boxW, boxH, GvkUi.Panel);
            _ssMapTip.AddSprite(MATERIAL_SQUARE, left + 0.002, top - boxH * 0.5, 0.003, boxH,
                contested ? Color.White : _territoryColor[only]);
            double textX = left + 0.004 + padX;
            _ssMapTip.AddText(name, textX, top - padY, nameScale, Color.White, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP, true);
            _ssMapTip.BeginRuns(textX, top - padY - nameH, infoScale, true);
            first = true;
            for (int t = 0; t < _territoryCosA.Length; t++)
            {
                if ((mask & (1 << t)) == 0) continue;
                if (!first) _ssMapTip.AddRun(GAP, Color.White);
                first = false;
                if (contested)
                {
                    _ssMapTip.AddRun(_territoryName[t] ?? string.Empty, GvkUi.TextDim);
                    _ssMapTip.AddRun(" ", Color.White);
                }
                _ssMapTip.AddRun(_territoryStanding[t] ?? string.Empty, _territoryColor[t]);
            }
        }

        private static string GetMapMarkerKind(ref ActiveHudWaypoint wp)
        {
            if (wp.KothState == KOTH_OPEN) return "KOTH, open now";
            if (wp.KothState == KOTH_NEXT) return "KOTH, opens next";
            if (wp.SiteKind != 0) return wp.KindText ?? "Known site";
            if (!wp.IsSignal) return "GPS point";
            switch (wp.Relation)
            {
                case REL_OWN: return "Your signal";
                case REL_FACTION: return "Faction signal";
                case REL_FRIENDLY: return "Friendly signal";
                case REL_HOSTILE: return "Hostile signal";
                case REL_NPC: return "NPC signal";
                default: return "Neutral signal";
            }
        }

        /// <summary>
        /// Adds a GPS on the ground at a point on the map: the map projection run backwards gives the direction
        /// from the planet centre, then the planet's surface along it. Named "Map Point N", the next free N.
        /// </summary>
        private void AddGpsFromMap(double u, double v)
        {
            var player = MyAPIGateway.Session?.Player;
            if (player == null) return;

            double lat = Math.PI * 0.5 - v * Math.PI;
            double lon = u * 2.0 * Math.PI - Math.PI + MAP_LON_SHIFT * 2.0 * Math.PI; // Undo WorldToMapUV's prime meridian shift
            var dir = new Vector3D(Math.Cos(lat) * Math.Cos(lon), -Math.Sin(lat), Math.Cos(lat) * Math.Sin(lon));
            Vector3D point = PLANET_CENTER + dir * PLANET_RADIUS;
            var planet = Sandbox.Game.Entities.MyGamePruningStructure.GetClosestPlanet(point);
            if (planet != null) point = planet.GetClosestSurfacePointGlobal(ref point);

            cachedGpsList.Clear();
            MyAPIGateway.Session.GPS.GetGpsList(player.IdentityId, cachedGpsList);
            int next = _mapPointNext;
            for (int i = 0; i < cachedGpsList.Count; i++)
            {
                string gpsName = cachedGpsList[i].Name;
                int n;
                if (gpsName != null && gpsName.StartsWith("Map Point ", StringComparison.Ordinal)
                    && int.TryParse(gpsName.Substring(10), out n) && n >= next)
                    next = n + 1;
            }

            string name = "Map Point " + next;
            _mapPointNext = next + 1;
            var gps = MyAPIGateway.Session.GPS.Create(name, "Added from the GVK satellite map", point, true);
            MyAPIGateway.Session.GPS.AddGps(player.IdentityId, gps);
            _gpsRescanTicks = 30;
            double km = Vector3D.Distance(_mapPlayerPos, point) * 0.001;
            MyAPIGateway.Utilities.ShowNotification($"[GVK NAV] Added GPS '{name}', {km:0.0} km away", 2500, MyFontEnum.Green);
        }

        /// <summary>Removes the player's GPS with this name at this position (the map's copy may be up to a scan old).</summary>
        private void RemoveGpsFromMap(string name, Vector3D coords)
        {
            var player = MyAPIGateway.Session?.Player;
            if (player == null) return;

            cachedGpsList.Clear();
            MyAPIGateway.Session.GPS.GetGpsList(player.IdentityId, cachedGpsList);
            for (int i = 0; i < cachedGpsList.Count; i++)
            {
                var gps = cachedGpsList[i];
                if (gps.Name == name && Vector3D.DistanceSquared(gps.Coords, coords) < 1.0)
                {
                    MyAPIGateway.Session.GPS.RemoveGps(player.IdentityId, gps);
                    _gpsRescanTicks = 30;
                    MyAPIGateway.Utilities.ShowNotification($"[GVK NAV] Removed GPS '{name}'", 2500, MyFontEnum.Green);
                    return;
                }
            }
            MyAPIGateway.Utilities.ShowNotification($"[GVK NAV] GPS '{name}' is already gone", 2500, MyFontEnum.Red);
        }

        /// <summary>
        /// Turns off Show on HUD for the player's GPS with this name at this position: it leaves the map, compass and
        /// minimap (they show only Show on HUD points) but stays in the GPS list, where the player can turn it back on.
        /// </summary>
        private void HideGpsFromMap(string name, Vector3D coords)
        {
            var player = MyAPIGateway.Session?.Player;
            if (player == null) return;

            cachedGpsList.Clear();
            MyAPIGateway.Session.GPS.GetGpsList(player.IdentityId, cachedGpsList);
            for (int i = 0; i < cachedGpsList.Count; i++)
            {
                var gps = cachedGpsList[i];
                if (gps.Name == name && Vector3D.DistanceSquared(gps.Coords, coords) < 1.0)
                {
                    MyAPIGateway.Session.GPS.SetShowOnHud(player.IdentityId, gps, false);
                    _gpsRescanTicks = 30;
                    MyAPIGateway.Utilities.ShowNotification($"[GVK NAV] Hid GPS '{name}' (Show on HUD is off; it stays in your GPS list)", 3500, MyFontEnum.Green);
                    return;
                }
            }
            MyAPIGateway.Utilities.ShowNotification($"[GVK NAV] GPS '{name}' is already gone", 2500, MyFontEnum.Red);
        }

        /// <summary>The map screen's draw, every frame while it is open, faded with the screen.</summary>
        public void DrawMapScreen(float alpha)
        {
            if (!showFullMap || _ssMapDrawFailed) return;
            try
            {
                _ssMap.Draw(MyScreenSpaceLayer.BelowHud, alpha);
                _ssMapButton.Draw(MyScreenSpaceLayer.BelowHud, alpha);
                _ssMapTip.Draw(MyScreenSpaceLayer.BelowHud, alpha);
                _ssMapMenu.Draw(MyScreenSpaceLayer.BelowHud, alpha);
            }
            catch (Exception ex)
            {
                FailMapScreen("draw", ex);
            }
        }

        /// <summary>An error in the map screen's draw or input: log it, close the map and keep it off for the session.</summary>
        private void FailMapScreen(string where, Exception ex)
        {
            if (_ssMapDrawFailed) return; // Already closing from an earlier error
            _ssMapDrawFailed = true;
            MyLog.Default.WriteLineAndConsole($"[GVK_ZoneNavCommand] Satellite map screen {where} failed, map off for this session: {ex}");
            try
            {
                _ssMap.Clear();
                _ssMapTip.Clear();
                _ssMapButton.Clear();
                CloseMapMenu();
                // No fade: a screen opened this frame is still queued, and a fading close of it is lost when the game adds it.
                // CloseNow delivers OnMapScreenClosed before it returns, which finishes CloseFullMap.
                if (MapScreenOpen) _mapScreen.CloseNow();
                CloseFullMap();
                MyAPIGateway.Utilities.ShowNotification("[GVK NAV] The satellite map hit an error and closed", 2500, MyFontEnum.Red);
            }
            catch (Exception ex2)
            {
                MyLog.Default.WriteLineAndConsole($"[GVK_ZoneNavCommand] Satellite map close after an error failed: {ex2}");
                showFullMap = false; // Let the HUD draw again
            }
        }

        public void OnMapScreenClosed(bool isUnloading)
        {
            try
            {
                if (isUnloading)
                {
                    showFullMap = false; // The session is ending
                    return;
                }
                CloseFullMap();
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[GVK_ZoneNavCommand] Satellite map close failed: {ex}");
                showFullMap = false; // Let the HUD draw again
            }
        }


        private Color GetCurrentAccentColor()
        {
            return inSpace ? GvkUi.Space : GetZoneColor(currentZoneIndex);
        }

        private Color GetZoneColor(int zone)
        {
            return GvkPalette.ZoneAccent[MathHelper.Clamp(zone, 0, 3)];
        }

        /// <summary>
        /// Reads the user's HUD background opacity setting from game config (HUDBkOpacity).
        /// </summary>
        private float GetUserHudOpacity()
        {
            try
            {
                if (MyAPIGateway.Session?.Config != null)
                {
                    return MathHelper.Clamp(MyAPIGateway.Session.Config.HUDBkOpacity, 0f, 1f);
                }
            }
            catch
            {
                // Fallback if config is unavailable
            }
            return 1.0f;
        }

        /// <summary>Re-reads the player's HUD background opacity, which GetCardBgColor uses for every card.</summary>
        private void UpdateCardBackgroundOpacities()
        {
            cachedHudBkOpacity = GetUserHudOpacity();
        }

        /// <summary>Compass ribbon total width at the given scale factor. Single source of truth for all callers.</summary>
        private float GetCompassBaseWidth(float scale) => 0.54f + 0.08f * scale;

        /// <summary>Compass ribbon total height at the given scale factor. Single source of truth for all callers (reduced by 25%).</summary>
        private float GetCompassBaseHeight(float scale) => 0.057f * scale;

        /// <summary>
        /// Converts planetary 3D world position to UV [0, 1] on KharakMercator.dds (equirectangular).
        /// The map's left edge is a quarter turn round from -X (MAP_LON_SHIFT). Fitted against the planet heightmaps
        /// sampled the way the game does; the old hand-fitted 0.254 put every marker about 9 map pixels west.
        /// Inverts Y to match the map texture's North/South latitude orientation.
        /// </summary>
        private Vector2 WorldToMapUV(Vector3D worldPos)
        {
            Vector3D R = worldPos - PLANET_CENTER;
            double len = R.Length();
            if (len < 1.0) return new Vector2(0.5f, 0.5f);

            Vector3D u = R / len;
            double lat = Math.Asin(MathHelper.Clamp(-u.Y, -1.0, 1.0)); // Invert Y for map orientation
            double lon = Math.Atan2(u.Z, u.X) - MAP_LON_SHIFT * 2.0 * Math.PI; // Align to map prime meridian

            while (lon > Math.PI) lon -= 2.0 * Math.PI;
            while (lon < -Math.PI) lon += 2.0 * Math.PI;

            float U = (float)((lon + Math.PI) / (Math.PI * 2.0));
            float V = (float)((Math.PI * 0.5 - lat) / Math.PI);

            return new Vector2(MathHelper.Clamp(U, 0f, 1f), MathHelper.Clamp(V, 0f, 1f));
        }

        private void OnMessageEntered(string messageText, ref bool sendToOthers)
        {
            if (string.IsNullOrWhiteSpace(messageText)) return;

            string msg = messageText.Trim();

            // All commands use /gvk prefix to avoid collision with other mods.
            if (msg.Equals("/gvk", StringComparison.OrdinalIgnoreCase) || msg.Equals("/gvk help", StringComparison.OrdinalIgnoreCase))
            {
                sendToOthers = false;
                ShowAvionicsStatusCard();
                return;
            }

            if (msg.Equals("/gvk map", StringComparison.OrdinalIgnoreCase))
            {
                sendToOthers = false;
                ToggleFullMap();
                return;
            }

            if (msg.Equals("/gvk radar", StringComparison.OrdinalIgnoreCase))
            {
                sendToOthers = false;
                ToggleMinimap();
                return;
            }

            if (msg.StartsWith("/gvk range", StringComparison.OrdinalIgnoreCase))
            {
                sendToOthers = false;
                string[] parts = msg.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    double r;
                    if (double.TryParse(parts[2], out r))
                    {
                        if (r >= 25.0)
                        {
                            radarScale = RadarScaleMode.Logarithmic;
                            radarRangeMeters = 30000.0;
                        }
                        else
                        {
                            radarScale = RadarScaleMode.Linear;
                            radarRangeMeters = MathHelper.Clamp(r * 1000.0, 500.0, 20000.0);
                        }
                        _refreshMinimapNextFrame = true;
                        SaveConfig();
                        string rStr = radarScale == RadarScaleMode.Logarithmic ? "30.0 km (Logarithmic)" : $"{(radarRangeMeters * 0.001):0.#} km (Linear)";
                        MyAPIGateway.Utilities.ShowNotification($"[GVK NAV] Tactical Radar Range: {rStr}", 2500, MyFontEnum.Green);
                        return;
                    }
                }
                CycleRadarRange();
                return;
            }

            if (msg.Equals("/gvk compass", StringComparison.OrdinalIgnoreCase))
            {
                sendToOthers = false;
                ToggleCompass();
                return;
            }

            if (msg.Equals("/gvk settings", StringComparison.OrdinalIgnoreCase))
            {
                sendToOthers = false;
                OpenSettingsScreen(showFullMap); // Over an open map, swap it out the same way the map's button does
                return;
            }

            if (msg.Equals("/gvk zone", StringComparison.OrdinalIgnoreCase))
            {
                sendToOthers = false;
                CycleZoneBarMode();
                return;
            }

            if (msg.Equals("/gvk reset", StringComparison.OrdinalIgnoreCase))
            {
                sendToOthers = false;
                ResetAllToFactoryDefaults();
                return;
            }

            if (msg.Equals("/gvk gps", StringComparison.OrdinalIgnoreCase))
            {
                sendToOthers = false;
                PopulateDefaultGps(notify: true);
                return;
            }

            if (msg.Equals("/gvk rate", StringComparison.OrdinalIgnoreCase))
            {
                sendToOthers = false;
                CycleUpdateTickRate();
                return;
            }

            // Any other /gvk command (a typo, or the removed /gvk ss) stays out of public chat
            if (msg.StartsWith("/gvk ", StringComparison.OrdinalIgnoreCase))
            {
                sendToOthers = false;
                MyAPIGateway.Utilities.ShowMessage("GVK", "Unknown command. Type /gvk for the list.");
            }
        }

        private void ToggleFullMap()
        {
            if (showFullMap) CloseFullMap();
            else OpenFullMap();
        }

        private bool MapScreenOpen => _mapScreen != null && _mapScreen.IsOpen;

        private void CloseFullMap()
        {
            if (!showFullMap) return;
            if (MapScreenOpen)
            {
                _ssMapTip.Clear();
                BuildMapSettingsButton(false, -1); // No input frames during the fade: drop the hover look and tooltip
                CloseMapMenu();
                _mapScreen.Close(); // Fades out; OnMapScreenClosed comes back here to finish
                return;
            }

            showFullMap = false;
            _ssMap.Clear();
            _ssMapTip.Clear();
            _ssMapButton.Clear();
            CloseMapMenu();
            _mapHits.Clear();
            _mapDragging = false;
            _refreshMinimapNextFrame = true;
            _refreshCompassNextFrame = true;
        }

        /// <summary>
        /// Opens the satellite map as a game screen. If the game refuses it (four mod screens already open),
        /// the player is told and nothing changes.
        /// </summary>
        private void OpenFullMap(bool keepView = false)
        {
            if (showFullMap) return;
            if (_ssMapDrawFailed)
            {
                MyAPIGateway.Utilities.ShowNotification("[GVK NAV] The satellite map hit an error and is off until you rejoin", 2500, MyFontEnum.Red);
                return;
            }
            // A new screen object each time: the game removes a closed screen a frame after OnClosed, and that removal
            // closes whatever the same MyModScreen has opened since (MyModScreen.DeliverClosed doesn't check the host),
            // so reusing it for a reopen in that frame (settings refused or failing right after the map closed) leaves a blank screen
            _mapScreen = new GvkMapScreen(this);
            _mapScreen.Open();
            if (!_mapScreen.IsOpen)
            {
                MyAPIGateway.Utilities.ShowNotification("[GVK NAV] Can't open the satellite map: too many screens are open", 2500, MyFontEnum.Red);
                return;
            }

            showFullMap = true;
            _mapReopenTicks = 0;          // This map is the one a pending reopen after settings was for
            _settingsReturnToMap = false;
            _mapLayoutDirty = true;
            _fullMapNeedsRedraw = false;

            // Open on the player at the last zoom; the map key that opened it must be let go before it closes it
            // (keepView: back from the settings screen, so it keeps the view the player left)
            Vector3D? pos = GetPlayerPosition();
            if (pos.HasValue) _mapPlayerPos = pos.Value;
            if (!keepView)
            {
                Vector2 uv = WorldToMapUV(_mapPlayerPos);
                _mapViewU = uv.X;
                _mapViewV = uv.Y;
            }
            _mapKeyLatch = true;
            _mapDragging = false;
            try
            {
                BuildScreenSpaceFullMap();
            }
            catch (Exception ex)
            {
                FailMapScreen("open", ex); // Blamed on the map, not on the screen or command that opened it
            }
        }

        private void ToggleMinimap()
        {
            showMinimap = !showMinimap;
            _refreshMinimapNextFrame = true;
            SaveConfig();
            string status = showMinimap ? "ENABLED" : "DISABLED";
            MyAPIGateway.Utilities.ShowNotification($"[GVK NAV] Radar: {status}", 2500, showMinimap ? MyFontEnum.Green : MyFontEnum.Red);
        }

        private void CycleMinimapScale()
        {
            minimapScale = NextHudScale(minimapScale);

            ApplyMinimapScale(minimapScale);
            _refreshMinimapNextFrame = true;
            SaveConfig();
            int pct = (int)Math.Round(minimapScale * 100);
            MyAPIGateway.Utilities.ShowNotification($"[GVK NAV] Radar Size: {pct}%", 2500, MyFontEnum.Green);
        }

        private void ApplyMinimapScale(float scale)
        {
            minimapScale = scale;
            float aspect = GetScreenAspect();
            float baseWidth = 0.312f;
            float mWidth = baseWidth * scale;
            float mHeight = (mWidth * 0.5f) * aspect;
            minimapSize = new Vector2D(mWidth, mHeight);

            float radarGridDiameter = (float)minimapSize.Y * 0.95f;
            float radarBoxWidth = (radarGridDiameter / aspect) + 0.024f * scale;
            float minimapBgWidth = radarBoxWidth;
            float minimapBgHeight = (float)minimapSize.Y + (0.008f * scale) * aspect;

            // Horizontal position: -1.0 (flush left -0.999) to +1.0 (flush right +0.999)
            double maxCenterX = 0.999 - (minimapBgWidth * 0.5);
            double posX = minimapOffsetX * maxCenterX;

            // Vertical position: -1.0 (flush bottom -0.995) to +1.0 (flush top +0.999)
            float zoneHeight = 0.056f * scale;
            float gap = 0.004f * scale;

            bool zoneBarOnMinimap = (zoneBarMode == ZoneBarMode.RadarDock);
            float topBuffer = (zoneBarOnMinimap ? (zoneHeight + gap) : 0f) + (minimapBgHeight * 0.5f);
            float bottomBuffer = minimapBgHeight * 0.5f;
            double maxCenterY = 0.999 - topBuffer;
            double minCenterY = -0.995 + bottomBuffer;
            double t = (minimapOffsetY + 1.0) * 0.5;
            double posY = minCenterY + t * (maxCenterY - minCenterY);

            minimapPosition = new Vector2D(posX, posY);
            _minimapLayoutBgWidth = minimapBgWidth;
        }

        private void CycleMinimapPreset()
        {
            int currentIdx = -1;
            for (int i = 0; i < MinimapPresets.Length; i++)
            {
                if (Math.Abs(minimapOffsetX - MinimapPresets[i].PosX) < 0.08 &&
                    Math.Abs(minimapOffsetY - MinimapPresets[i].PosY) < 0.08)
                {
                    currentIdx = i;
                    break;
                }
            }

            int nextIdx = (currentIdx + 1) % MinimapPresets.Length;
            SetMinimapOffset(MinimapPresets[nextIdx].PosX, MinimapPresets[nextIdx].PosY);
            MyAPIGateway.Utilities.ShowNotification($"[GVK NAV] Radar Position: {MinimapPresets[nextIdx].Name}", 2500, MyFontEnum.Green);
        }

        private void SetMinimapOffset(double x, double y)
        {
            minimapOffsetX = MathHelper.Clamp(x, -1.0, 1.0);
            minimapOffsetY = MathHelper.Clamp(y, -1.0, 1.0);
            ApplyMinimapScale(minimapScale);
            _refreshMinimapNextFrame = true;
            SaveConfig();
        }

        private void ResetMinimapOffset()
        {
            SetMinimapOffset(1.0, 1.0);
            MyAPIGateway.Utilities.ShowNotification("[GVK NAV] Radar Position reset to Top-Right (1.0, 1.0)", 2500, MyFontEnum.Green);
        }

        private void CycleRadarRange()
        {
            if (radarScale == RadarScaleMode.Logarithmic)
            {
                radarScale = RadarScaleMode.Linear;
                radarRangeMeters = 1500.0;
                MyAPIGateway.Utilities.ShowNotification("[GVK NAV] Tactical Radar Range: 1.5 km (Linear)", 2500, MyFontEnum.Green);
            }
            else if (radarRangeMeters < 2000.0)
            {
                radarScale = RadarScaleMode.Linear;
                radarRangeMeters = 3000.0;
                MyAPIGateway.Utilities.ShowNotification("[GVK NAV] Tactical Radar Range: 3.0 km (Linear)", 2500, MyFontEnum.Green);
            }
            else if (radarRangeMeters < 4000.0)
            {
                radarScale = RadarScaleMode.Linear;
                radarRangeMeters = 5000.0;
                MyAPIGateway.Utilities.ShowNotification("[GVK NAV] Tactical Radar Range: 5.0 km (Linear)", 2500, MyFontEnum.Green);
            }
            else
            {
                radarScale = RadarScaleMode.Logarithmic;
                radarRangeMeters = 30000.0;
                MyAPIGateway.Utilities.ShowNotification("[GVK NAV] Tactical Radar Range: 30.0 km (Logarithmic)", 2500, MyFontEnum.Green);
            }

            _refreshMinimapNextFrame = true;
            SaveConfig();
        }


        private void SetUpdateTickRate(int ticks)
        {
            updateTickRate = Math.Max(1, Math.Min(60, ticks));
            SaveConfig();
            double hz = 60.0 / updateTickRate;
            MyAPIGateway.Utilities.ShowNotification($"[GVK NAV] HUD Refresh Rate: {updateTickRate} ticks ({hz:F1} Hz)", 2500, MyFontEnum.Green);
        }

        private void CycleUpdateTickRate()
        {
            // Cycle common stepped divisors starting from 5 (12 Hz default):
            // 5 (12 Hz) -> 4 (15 Hz) -> 2 (30 Hz) -> 1 (60 Hz) -> 10 (6 Hz) -> 6 (10 Hz) -> 5
            if (updateTickRate == 5) SetUpdateTickRate(4);
            else if (updateTickRate == 4) SetUpdateTickRate(2);
            else if (updateTickRate == 2) SetUpdateTickRate(1);
            else if (updateTickRate == 1) SetUpdateTickRate(10);
            else if (updateTickRate == 10) SetUpdateTickRate(6);
            else SetUpdateTickRate(5);
        }

        /// <summary>Next Minimap/Compass Size step: 75, 100, 125, 150, 175, 200%, then back to 75%. 175 and 200 are for low resolutions.</summary>
        private static float NextHudScale(float scale)
        {
            for (int i = 0; i < HUD_SCALE_STEPS.Length; i++)
                if (scale < HUD_SCALE_STEPS[i] - 0.1f) return HUD_SCALE_STEPS[i];
            return HUD_SCALE_STEPS[0];
        }

        private static readonly float[] HUD_SCALE_STEPS = { 0.75f, 1.0f, 1.25f, 1.5f, 1.75f, 2.0f };
        private const float HUD_SCALE_MIN = 0.70f, HUD_SCALE_MAX = 2.10f; // Saved sizes outside this load as 100%

        private void CycleCompassScale()
        {
            compassScale = NextHudScale(compassScale);

            ApplyCompassScale(compassScale);
            SaveConfig();
            int pct = (int)Math.Round(compassScale * 100);
            MyAPIGateway.Utilities.ShowNotification($"[GVK NAV] Compass Ribbon Size: {pct}%", 2500, MyFontEnum.Green);
        }

        private void CycleCompassPreset()
        {
            int currentIdx = -1;
            for (int i = 0; i < CompassPresets.Length; i++)
            {
                if (Math.Abs(compassOffsetX - CompassPresets[i].PosX) < 0.08 &&
                    Math.Abs(compassOffsetY - CompassPresets[i].PosY) < 0.08)
                {
                    currentIdx = i;
                    break;
                }
            }

            int nextIdx = (currentIdx + 1) % CompassPresets.Length;
            SetCompassOffset(CompassPresets[nextIdx].PosX, CompassPresets[nextIdx].PosY);
            MyAPIGateway.Utilities.ShowNotification($"[GVK NAV] Compass Position: {CompassPresets[nextIdx].Name}", 2500, MyFontEnum.Green);
        }

        private void SetCompassOffset(double x, double y)
        {
            compassOffsetX = MathHelper.Clamp(x, -1.0, 1.0);
            compassOffsetY = MathHelper.Clamp(y, -1.0, 1.0);
            ApplyCompassScale(compassScale);
            _refreshCompassNextFrame = true;
            SaveConfig();
        }

        private void ResetCompassOffset()
        {
            SetCompassOffset(0.0, 1.0);
            MyAPIGateway.Utilities.ShowNotification("[GVK NAV] Compass Position reset to Top-Center (0.0, 1.0)", 2500, MyFontEnum.Green);
        }

        private void ApplyCompassScale(float scale)
        {
            compassScale = scale; // The compass lays itself out from this on its next build
            _refreshCompassNextFrame = true;
        }

        private void ToggleCompass()
        {
            showCompass = !showCompass;
            _refreshCompassNextFrame = true;
            SaveConfig();
            string status = showCompass ? "ENABLED" : "DISABLED";
            MyAPIGateway.Utilities.ShowNotification($"[GVK NAV] Compass Ribbon: {status}", 2500, showCompass ? MyFontEnum.Green : MyFontEnum.Red);
        }

        private static bool IsValidMapKey(MyKeys key)
        {
            // Escape always closes the map, so it can't also be the toggle key
            return key != MyKeys.None && key != MyKeys.Escape;
        }

        private void SetMapKey(MyKeys key, bool shift, bool ctrl, bool alt)
        {
            if (!IsValidMapKey(key))
            {
                MyAPIGateway.Utilities.ShowNotification("[GVK NAV] That key can't open the map; keeping " + mapKeyLabel, 2500, MyFontEnum.Red);
                return;
            }

            mapKey = key;
            mapKeyShift = shift;
            mapKeyCtrl = ctrl;
            mapKeyAlt = alt;
            UpdateMapKeyLabel();
            SaveConfig();
            MyAPIGateway.Utilities.ShowNotification("[GVK NAV] Satellite Map Key: " + mapKeyLabel, 2500, MyFontEnum.Green);
        }

        private void UpdateMapKeyLabel()
        {
            // The game names OemPlus "Plus"; its keycap reads = (with + above) on US and UK keyboards
            string name = mapKey == MyKeys.OemPlus ? "=" : MyAPIGateway.Input?.GetKeyName(mapKey);
            if (string.IsNullOrEmpty(name)) name = mapKey.ToString();
            mapKeyLabel = (mapKeyCtrl ? "Ctrl+" : "") + (mapKeyShift ? "Shift+" : "") + (mapKeyAlt ? "Alt+" : "") + name;
        }

        /// <summary>Flips one compass, minimap or satellite map marker filter and redraws every display.</summary>
        private void ToggleMarkerFilter(ref bool setting, string name)
        {
            setting = !setting;
            _refreshCompassNextFrame = true;
            _refreshMinimapNextFrame = true;
            _mapLayoutDirty = true;
            _fullMapNeedsRedraw = true;
            SaveConfig();
            MyAPIGateway.Utilities.ShowNotification($"[GVK NAV] {name}: {(setting ? "ENABLED" : "DISABLED")}", 2500, setting ? MyFontEnum.Green : MyFontEnum.Red);
        }

        /// <summary>Steps a four-state overlay to its next state: lines and fade, lines, fade, off, then round again.</summary>
        private void CycleMapOverlay(ref bool lines, ref bool fade, string name)
        {
            int next = (OverlayState(lines, fade) + 1) % 4;
            lines = next <= 1;
            fade = next == 0 || next == 2;
            _refreshCompassNextFrame = true;
            _refreshMinimapNextFrame = true;
            _mapLayoutDirty = true;
            _fullMapNeedsRedraw = true;
            SaveConfig();
            MyAPIGateway.Utilities.ShowNotification($"[GVK NAV] {name}: {OVERLAY_STATE_NAMES[next].ToUpperInvariant()}", 2500,
                next == 3 ? MyFontEnum.Red : MyFontEnum.Green);
        }

        private static bool IsMarkerShown(ref ActiveHudWaypoint wp, bool showGps, bool showSignals, bool showOwnSignals, bool showKoth, bool showSites)
        {
            if (wp.KothState != 0) return showKoth;
            if (wp.SiteKind != 0) return showSites; // Views also skip a site that has a stand-in: SkipForPass, or HasStandIn on the map
            if (wp.FoldedInto != 0 && showSites) return false; // A site's own beacon: the site icon shows it
            return wp.IsSignal ? showSignals && (showOwnSignals || !wp.IsOwnOrFaction) : showGps;
        }

        /// <summary>
        /// Multi-pass marker loops (pass 1 known sites, every other pass everything else): true if this marker belongs to
        /// another pass, or is a site whose GPS point or signal this view has already picked (MarkStandInDrawn).
        /// </summary>
        private bool SkipForPass(ref ActiveHudWaypoint wp, int pass)
        {
            if ((wp.SiteKind != 0) != (pass == 1)) return true;
            return pass == 1 && wp.SiteIndex < _siteStandInDrawn.Length && _siteStandInDrawn[wp.SiteIndex];
        }

        private void MarkStandInDrawn(ref ActiveHudWaypoint wp)
        {
            int s = wp.StandsInFor - 1;
            if (s >= 0 && s < _siteStandInDrawn.Length) _siteStandInDrawn[s] = true;
        }

        /// <summary>Counts a minimap marker against its budget. Known sites have their own, so they never push out GPS points or signals.</summary>
        private static bool TakeMinimapSlot(ref ActiveHudWaypoint wp, ref int markers, ref int sites)
        {
            if (wp.SiteKind != 0)
            {
                if (sites >= MINIMAP_MAX_SITES) return false;
                sites++;
                return true;
            }
            if (markers >= MINIMAP_MAX_MARKERS) return false;
            markers++;
            return true;
        }

        /// <summary>
        /// Marker colour for this frame: open KOTHs flash (bright 0.6 s, faint 0.4 s), the next KOTH is dimmed,
        /// everything else uses its own colour.
        /// </summary>
        private Color GetMarkerColor(ref ActiveHudWaypoint wp)
        {
            if (wp.KothState == KOTH_OPEN && !_kothFlashBright)
                return wp.DisplayColor * 0.25f;
            return wp.DisplayColor;
        }

        /// <summary>
        /// A KOTH's capture radius as a distance in map UV units: projects a point one radius away along the
        /// planet surface. Computed when KOTH markers refresh, not on every map redraw.
        /// </summary>
        private double KothRingMapRadius(Vector3D center, double radiusMeters)
        {
            Vector3D up = center - PLANET_CENTER;
            up.Normalize();
            Vector3D edge = center + Vector3D.CalculatePerpendicularVector(up) * radiusMeters;
            Vector2 uvCenter = WorldToMapUV(center);
            Vector2 uvEdge = WorldToMapUV(edge);
            double d = (uvEdge - uvCenter).Length();
            return d > 0.25 ? 0.0 : d; // Across the map's wrap seam the projection jumps; fall back to the minimum ring
        }

        private static double[] BuildRingTable(bool cos)
        {
            var table = new double[KOTH_RING_DOTS];
            for (int i = 0; i < KOTH_RING_DOTS; i++)
            {
                double angle = MathHelper.TwoPi * i / KOTH_RING_DOTS;
                table[i] = cos ? Math.Cos(angle) : Math.Sin(angle);
            }
            return table;
        }

        /// <summary>
        /// Replaces only the KOTH markers (removed from the end backwards, then re-added and re-sorted), so new
        /// KOTH state doesn't trigger the full grid and broadcast scan in <see cref="UpdateBackgroundData"/>.
        /// </summary>
        private void RefreshKothWaypoints(Vector3D playerPos)
        {
            for (int i = activeHudWaypoints.Count - 1; i >= 0; i--)
            {
                if (activeHudWaypoints[i].KothState != 0)
                    activeHudWaypoints.RemoveAtFast(i);
            }

            AddKothWaypoints(playerPos);
            activeHudWaypoints.Sort(WaypointComparer);
            _mapLayoutDirty = true;
            _refreshCompassNextFrame = true;
            _refreshMinimapNextFrame = true;
            if (showFullMap)
                _fullMapNeedsRedraw = true;
        }

        /// <summary>Adds the open KOTHs and the next one from <see cref="KothClientData"/> as markers.</summary>
        private void AddKothWaypoints(Vector3D playerPos)
        {
            for (int i = 0; i < KothClientData.Zones.Count; i++)
            {
                var zone = KothClientData.Zones[i];
                string label = zone.Open
                    ? "KOTH " + zone.Name + " " + (int)Math.Round(MathHelper.Clamp(zone.Fraction, 0.0, 1.0) * 100.0) + "%"
                    : "KOTH " + zone.Name + " " + zone.StartUtc.ToLocalTime().ToString("ddd HH:mm");

                activeHudWaypoints.Add(new ActiveHudWaypoint
                {
                    Name = label,
                    Coords = zone.Position,
                    Sprite = MATERIAL_MARKER_GPS,
                    DisplayColor = zone.Open ? KOTH_COLOR : KOTH_COLOR * 0.55f,
                    DistanceMeters = Vector3D.Distance(playerPos, zone.Position),
                    MapUV = WorldToMapUV(zone.Position),
                    IsSignal = false,
                    PlanetaryElevation = Vector3D.Distance(zone.Position, PLANET_CENTER) - PLANET_RADIUS,
                    KothState = zone.Open ? KOTH_OPEN : KOTH_NEXT,
                    KothRadius = zone.Radius,
                    KothMapRadius = KothRingMapRadius(zone.Position, zone.Radius)
                });
            }
        }

        /// <summary>Next colour palette. Signal, site and territory colours are set at scan time, so it rescans next tick.</summary>
        private void CycleColorPalette()
        {
            GvkPalette.Apply((GvkPalette.Current + 1) % GvkPalette.COUNT);
            _gpsRescanTicks = 1;
            _refreshCompassNextFrame = true;
            _refreshMinimapNextFrame = true;
            _mapLayoutDirty = true;
            _fullMapNeedsRedraw = true;
            SaveConfig();
            MyAPIGateway.Utilities.ShowNotification("[GVK NAV] Colors: " + GvkPalette.NAMES[GvkPalette.Current], 2500, MyFontEnum.White);
        }

        private void CycleZoneBarMode()
        {
            if (zoneBarMode == ZoneBarMode.CompassDock)
                zoneBarMode = ZoneBarMode.RadarDock;
            else if (zoneBarMode == ZoneBarMode.RadarDock)
                zoneBarMode = ZoneBarMode.Off;
            else
                zoneBarMode = ZoneBarMode.CompassDock;

            ApplyMinimapScale(minimapScale);
            _refreshMinimapNextFrame = true;
            _refreshCompassNextFrame = true;
            SaveConfig();

            string modeName;
            switch (zoneBarMode)
            {
                case ZoneBarMode.CompassDock: modeName = "Compass Dock"; break;
                case ZoneBarMode.RadarDock: modeName = "Radar Dock"; break;
                default: modeName = "OFF"; break;
            }
            MyAPIGateway.Utilities.ShowNotification($"[GVK NAV] Zone Status: {modeName}", 2500, zoneBarMode != ZoneBarMode.Off ? MyFontEnum.Green : MyFontEnum.Red);
        }

        /// <summary>The preset the minimap sits at, or -1 for a custom position.</summary>
        private int FindMinimapPresetIndex()
        {
            for (int i = 0; i < MinimapPresets.Length; i++)
            {
                if (Math.Abs(minimapOffsetX - MinimapPresets[i].PosX) < 0.08 &&
                    Math.Abs(minimapOffsetY - MinimapPresets[i].PosY) < 0.08)
                    return i;
            }
            return -1;
        }

        /// <summary>The preset the compass sits at, or -1 for a custom position.</summary>
        private int FindCompassPresetIndex()
        {
            for (int i = 0; i < CompassPresets.Length; i++)
            {
                if (Math.Abs(compassOffsetX - CompassPresets[i].PosX) < 0.08 &&
                    Math.Abs(compassOffsetY - CompassPresets[i].PosY) < 0.08)
                    return i;
            }
            return -1;
        }

        private void ToggleRadarZoneLines()
        {
            showRadarZoneLines = !showRadarZoneLines;
            SaveConfig();
            _refreshMinimapNextFrame = true;
            MyAPIGateway.Utilities.ShowNotification($"[GVK NAV] Zone Direction Vectors: {(showRadarZoneLines ? "ENABLED" : "DISABLED")}", 2500, showRadarZoneLines ? MyFontEnum.Green : MyFontEnum.Red);
        }

        private void ToggleRadarFovLines()
        {
            showRadarFovLines = !showRadarFovLines;
            SaveConfig();
            _refreshMinimapNextFrame = true;
            MyAPIGateway.Utilities.ShowNotification($"[GVK NAV] Camera Frustum FOV: {(showRadarFovLines ? "ENABLED" : "DISABLED")}", 2500, showRadarFovLines ? MyFontEnum.Green : MyFontEnum.Red);
        }

        private void ResetAllToFactoryDefaults()
        {
            showMinimap = true;
            radarScale = RadarScaleMode.Linear;
            radarRangeMeters = 3000.0;
            minimapScale = 1.0f;
            minimapOffsetX = 1.0;
            minimapOffsetY = 1.0;

            showCompass = true;
            showCompassGps = true;
            showCompassSignals = true;
            showCompassOwnSignals = true;
            showMapGpsLabels = true;
            showMapSignalLabels = true;
            showMapOwnSignals = true;
            stackMapIcons = true;
            showMapKoth = true;
            showMinimapKoth = true;
            showCompassKoth = true;
            showMapSites = true;
            showMinimapSites = true;
            showCompassSites = true;
            showMapTerritories = true;
            showTerritoryFade = true;
            showMapZones = true;
            showZoneTint = true;
            GvkPalette.Apply(GvkPalette.STANDARD);
            _gpsRescanTicks = 1; // Signal and site colours are set at scan time
            mapKey = DEFAULT_MAP_KEY;
            mapKeyShift = false;
            mapKeyCtrl = false;
            mapKeyAlt = false;
            UpdateMapKeyLabel();
            showMinimapGps = true;
            showMinimapSignals = true;
            showMinimapOwnSignals = true;
            _mapLayoutDirty = true;
            compassScale = 1.0f;
            compassOffsetX = 0.0;
            compassOffsetY = 1.0;

            zoneBarMode = ZoneBarMode.CompassDock;
            updateTickRate = 5;
            showRadarZoneLines = true;
            showRadarFovLines = true;

            ApplyMinimapScale(minimapScale);
            ApplyCompassScale(compassScale);
            UpdateCardBackgroundOpacities();
            _refreshMinimapNextFrame = true;
            _refreshCompassNextFrame = true;
            SaveConfig();

            MyAPIGateway.Utilities.ShowNotification("[GVK NAV] All Navigation & Radar settings reset to factory defaults.", 3000, MyFontEnum.Green);
        }

        private void ShowAvionicsStatusCard()
        {
            string mStatus = showMinimap ? "ON" : "OFF";
            string mRange = radarScale == RadarScaleMode.Logarithmic ? "30km Log" : $"{(radarRangeMeters * 0.001):0.#}km";
            string cStatus = showCompass ? "ON" : "OFF";
            string zMode = zoneBarMode == ZoneBarMode.RadarDock ? "Radar Dock" : (zoneBarMode == ZoneBarMode.CompassDock ? "Compass Dock" : "Off");
            double hz = 60.0 / updateTickRate;

            MyAPIGateway.Utilities.ShowMessage("GVK", $"Radar [{mStatus} {mRange}] | Compass [{cStatus}] | Zone [{zMode}] | {hz:0.#}Hz");
            MyAPIGateway.Utilities.ShowMessage("GVK", "Commands: /gvk settings, map, radar, range, compass, zone, gps, rate, reset.");
        }

        private Vector3D? GetPlayerPosition()
        {
            Vector3D? pos = MyAPIGateway.Session.Player?.GetPosition();
            if (!pos.HasValue && MyAPIGateway.Session.ControlledObject?.Entity != null)
            {
                pos = MyAPIGateway.Session.ControlledObject.Entity.GetPosition();
            }
            return pos;
        }
    }
}