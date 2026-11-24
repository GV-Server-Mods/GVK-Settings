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
    /// to KharakMap.dds. Configurable with /gvk chat commands and the /gvk settings screen.
    /// </summary>
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public partial class GVK_ZoneNavCommand : MySessionComponentBase
    {
        // Planetary Constants for Pertam
        private static readonly Vector3D PLANET_CENTER = new Vector3D(33032.44, 32395.99, 32074.17);
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
        private static readonly MyStringId MATERIAL_RADAR_GRID = MyStringId.GetOrCompute("RadarGrid");

        // Tactical HUD Markers (from Textures\HUD\)
        private static readonly MyStringId MATERIAL_MARKER_GPS = MyStringId.GetOrCompute("marker_gps");
        // Known-site icons: white with a black outline baked in, so a tint keeps the outline
        private static readonly MyStringId MATERIAL_SITE_TRADE = MyStringId.GetOrCompute("POI_SiteTrade");     // Credits sign
        private static readonly MyStringId MATERIAL_SITE_BASE = MyStringId.GetOrCompute("POI_SiteBase");       // Watchtower
        private static readonly MyStringId MATERIAL_SITE_DEPOT = MyStringId.GetOrCompute("POI_SiteDepot");     // Supply crate
        private static readonly MyStringId MATERIAL_SITE_CARRIER = MyStringId.GetOrCompute("POI_SiteCarrier"); // Ship
        private static readonly MyStringId MATERIAL_NAV_ARROW = MyStringId.GetOrCompute("nav_arrow");
        private static readonly MyStringId MATERIAL_SIGNAL_UP = MyStringId.GetOrCompute("signal_up");
        private static readonly MyStringId MATERIAL_SIGNAL_DOWN = MyStringId.GetOrCompute("signal_down");
        private static readonly MyStringId MATERIAL_SIGNAL_LEVEL = MyStringId.GetOrCompute("signal_level");


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
            public byte Relation;      // Signals only: REL_OWN, REL_FACTION, REL_HOSTILE, REL_NPC or REL_NEUTRAL
            public bool IsNpcOwned;    // Signals only: the grid's owner is in an NPC faction (hostile or not)
            public Vector3D GridOrigin; // Signals only: the broadcasting grid's origin, where MES places a static encounter
            public byte SiteKind;      // 0 = not a known site, else SITE_BASE, SITE_TRADE, SITE_REFINERY, SITE_DEPOT or SITE_CARRIER
            public int SiteIndex;      // Sites only: index into _knownSites
            public double StandInRadiusSq; // Sites only: a GPS point this close (squared metres) can stand in for the site
            public bool HasStandIn;    // Sites only, per scan: a GPS point or NPC signal in the list stands in for this site
            public string KindText;    // Sites only: the hover card's "what it is", from the server's site status
            public int StandsInFor;    // GPS points and NPC signals, per scan: 1 + the _knownSites index this marks, 0 = none
        }

        private const byte REL_NEUTRAL = 0;
        private const byte REL_OWN = 1;
        private const byte REL_FACTION = 2;
        private const byte REL_HOSTILE = 3;
        private const byte REL_NPC = 4;

        private const byte SITE_BASE = GvkKnownSites.BASE;       // Kinds and the site table: GVK_SiteStatus.cs
        private const byte SITE_TRADE = GvkKnownSites.TRADE;
        private const byte SITE_REFINERY = GvkKnownSites.REFINERY;
        private const byte SITE_DEPOT = GvkKnownSites.DEPOT;
        private const byte SITE_CARRIER = GvkKnownSites.CARRIER;
        private static readonly Color SITE_WHITE = Color.White;                    // Trade stations, and neutral or unowned sites
        private static readonly Color SITE_DIM = new Color(150, 150, 150, 150);    // No alliance Base on a Base Site, or not spawned
        private static readonly Color SITE_FRIENDLY = new Color(100, 240, 100);    // Same green as faction signals
        private static readonly Color SITE_HOSTILE = new Color(255, 60, 60);       // Same red as hostile signals
        private const int SITE_FRIENDLY_REP = 500;   // GVK's Friendly standing (contracts need 500+)
        private const int SITE_HOSTILE_REP = -500;   // Below this the game counts a faction hostile
        private const float SITE_ICON_SCALE = 0.8f;  // Site icons are a little smaller than GPS and signal markers
        private const double SITE_GRID_DEDUPE_DIST_SQ = 25.0 * 25.0;   // An NPC grid whose origin is this close is the site itself
        private const double COMPASS_SITE_MAX_DIST = 20000.0;     // The compass shows known sites only within 20 km
        private const double TERRITORY_POINT_SPACING_M = 100.0;   // Ground distance between the ring's sample points
        private const int TERRITORY_MIN_POINTS = 64;
        private const int TERRITORY_MAX_POINTS = 2048;
        private const double TERRITORY_MAP_DOT_SPACING = 0.008;   // Least distance between two drawn dots (HUD units, X scale)
        private const float TERRITORY_RING_ALPHA = 0.85f;        // Colours are premultiplied: scale all four channels
        private const float TERRITORY_FILL_ALPHA = 0.16f;        // Faint tint inside, so a ring larger than a hemisphere reads right
        private const int TERRITORY_FILL_ROWS = 180;             // One fill strip per degree of latitude

        private const int KOTH_OPEN = 1;
        private const int KOTH_NEXT = 2;
        private static readonly Color KOTH_COLOR = new Color(255, 128, 31);

        // Default GPS entries populated for joining players
        private struct DefaultGpsEntry
        {
            public string Name;
            public string Description;
            public Vector3D Coords;
            public bool ShowOnHud;
            public bool AlwaysVisible;
            public Color GpsColor;
            public DefaultGpsEntry(string name, string desc, Vector3D coords, bool showOnHud, bool alwaysVis, Color color)
            {
                Name = name;
                Description = desc;
                Coords = coords;
                ShowOnHud = showOnHud;
                AlwaysVisible = alwaysVis;
                GpsColor = color;
            }
        }

        private static readonly DefaultGpsEntry[] DEFAULT_KHARAK_GPS = new DefaultGpsEntry[]
        {
            new DefaultGpsEntry("Zone 0", "Zone 0 Safe Starter Hub", new Vector3D(62495.55, 28019.04, 37195.71), true, true, new Color(0, 255, 0)),
            new DefaultGpsEntry("Zone 3", "Zone 3 Deep Desert Center", new Vector3D(3569.33, 36772.94, 26952.63), true, false, new Color(255, 0, 0)),
            new DefaultGpsEntry("Sevastopol", "Coalition Trade Station", new Vector3D(2990.66, 30484.89, 34060.65), false, false, new Color(239, 220, 0)),
            new DefaultGpsEntry("Skyport", "Aerial Trade Station", new Vector3D(40585.81, 59619.14, 46601.96), false, false, new Color(239, 220, 0)),
            new DefaultGpsEntry("Mastodon", "Southern Trade Station", new Vector3D(33475.48, 5361.51, 20848.44), false, false, new Color(239, 220, 0)),
            new DefaultGpsEntry("Rusty's", "Starter Trade Outpost", new Vector3D(61686.17, 27088.08, 38160.96), false, false, new Color(239, 220, 0)),
            new DefaultGpsEntry("Coalition Base", "Coalition Headquarters", new Vector3D(61784.00, 28043.10, 38552.08), false, false, new Color(239, 220, 0)),
            new DefaultGpsEntry("KOTH Khar Toba", "King of the Hill Site", new Vector3D(4115.21, 38891.59, 26152.68), false, false, new Color(255, 128, 31)),
            new DefaultGpsEntry("KOTH Kalash Site", "King of the Hill Site", new Vector3D(8815.53, 14612.65, 35353.97), false, false, new Color(255, 128, 31)),
            new DefaultGpsEntry("KOTH Crashed Starship", "King of the Hill Site", new Vector3D(25893.61, 61095.89, 25703.05), false, false, new Color(255, 128, 31))
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
            "[ ZONE 0: SAFE HUB ]", "[ ZONE 1: PVE FRONTIER ]", "[ ZONE 2: CONTESTED (PVP) ]", "[ ZONE 3: GAALSIEN HEART ]"
        };
        private static readonly string[] ZONE_SHORT_NAMES = { "Zone 0", "Zone 1", "Zone 2", "Zone 3" };
        private static readonly string[] ZONE_DIST_LABELS = { "Zone 0: ", "Zone 1: ", "Zone 2: ", "Zone 3: " };
        private static readonly Color SPACE_TEXT_COLOR = new Color(255, 80, 255);
        private static readonly Color[] ZONE_TEXT_COLORS =
        {
            new Color(50, 255, 100), new Color(255, 230, 50), new Color(255, 165, 0), new Color(255, 50, 50)
        };
        private static readonly Color ZONE_LABEL_COLOR = new Color(220, 220, 220);
        private static readonly Color ZONE_SEPARATOR_COLOR = new Color(100, 140, 180);
        private bool _ssMaterialsChecked = false;
        private static readonly string[] _ssDistanceTenths = new string[100]; // "0.0k".."9.9k", filled on first use
        private static readonly string[] _ssDistanceWhole = new string[1000]; // "10k".."999k", filled on first use

        // Satellite map as a game screen (screen API): zoom about the cursor, drag to pan, hover for details,
        // right-click to add a GPS. The view is the map UV at the frame centre and a zoom of 1 (whole map) to 3.
        private GvkMapScreen _mapScreen;
        private readonly GvkScreenLayer _ssMap = new GvkScreenLayer();
        private readonly GvkScreenLayer _ssMapTip = new GvkScreenLayer();
        private const float MAP_MAX_ZOOM = 3f;          // The 2160 px map picture is soft past this
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
        private static readonly Color MAP_HEADER_GREY = new Color(180, 195, 210);
        private static readonly Color MAP_HINT_COLOR = new Color(160, 180, 200);
        private const string MAP_CONTROLS_HINT = "Wheel: zoom   |   Drag: pan   |   Right-click: add or remove a GPS   |   Point at a marker for details";
        private const float MAP_HINT_SCALE = 0.62f;     // HUD text scale of the controls line (was 0.46, too small)

        // Right-click menu: asks before adding a GPS at the click, or before removing the GPS clicked
        private readonly GvkScreenLayer _ssMapMenu = new GvkScreenLayer();
        private bool _mapMenuOpen;
        private bool _mapMenuRemove;        // False: add a GPS at the click; true: remove the GPS clicked
        private Vector2D _mapMenuAt;        // Click point, HUD units
        private double _mapMenuU;           // Map UV of the click, for adding
        private double _mapMenuV;
        private string _mapMenuGpsName;     // GPS to remove, found again by name and position
        private Vector3D _mapMenuGpsCoords;
        private string _mapMenuTitle;
        private MapHit _mapMenuOk;          // Button boxes from the last menu build
        private int _mapPointNext = 1;      // Next "Map Point N", so quick adds don't wait on the server's GPS list

        // Settings button at the right end of the map header; drawn every frame so it lights up under the cursor
        private readonly GvkScreenLayer _ssMapButton = new GvkScreenLayer();
        private MapHit _mapSettingsBox;
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

        public enum MinimapDisplayMode
        {
            StrategicMap,
            TacticalRadar
        }

        public enum RadarScaleMode
        {
            Linear,
            Logarithmic
        }

        private MinimapDisplayMode minimapMode = MinimapDisplayMode.StrategicMap;
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
            public int MinimapMode { get; set; } = 0;
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
            public bool ShowMinimapTerritories { get; set; } = true;

            public bool ShowCompass { get; set; } = true;
            public bool ShowCompassKoth { get; set; } = true;
            public bool ShowMapKoth { get; set; } = true;
            public bool ShowCompassSites { get; set; } = true;
            public bool ShowMapSites { get; set; } = true;
            public bool ShowMapTerritories { get; set; } = true;
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
        private bool[] _knownSiteHasStandIn = new bool[0];  // Per scan: a GPS point or NPC signal stands in for this site
        private bool[] _siteStandInDrawn = new bool[0];     // Per view draw: this site's stand-in got a slot, so the site is skipped
        private bool _knownSitesLoaded;
        private int _siteStatusVersion = int.MinValue;      // GvkSiteStatus.Version last read into the arrays below
        private byte[] _siteState = new byte[0];            // GvkSiteStatus state per site
        private long[] _siteFaction = new long[0];          // Owner faction per site (0 = none)
        private string[] _siteKindText = new string[0];     // Hover text per site, rebuilt only when the status changes
        private readonly List<List<Vector2>> _territoryUV = new List<List<Vector2>>(2); // Ring points per territory (map UV)
        private double[] _territoryRadius = new double[0];  // Radius each ring was built for (0 = no size from the server yet)
        private Color[] _territoryColor = new Color[0];     // Standing colour per territory, every scan
        private readonly List<List<TerritoryRow>> _territoryRows = new List<List<TerritoryRow>>(2); // Inside, as map UV strips

        /// <summary>One strip of a territory's inside: map row Row (of TERRITORY_FILL_ROWS, from the top), from U0 to U1.</summary>
        private struct TerritoryRow
        {
            public int Row;
            public float U0, U1;
        }
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
        private bool showMapTerritories = true;
        private bool showMinimapTerritories = true;
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
                                minimapMode = (MinimapDisplayMode)cfg.MinimapMode;
                                radarScale = (RadarScaleMode)cfg.RadarScale;
                                radarRangeMeters = (cfg.RadarRangeMeters >= 500.0 && cfg.RadarRangeMeters <= 50000.0) ? cfg.RadarRangeMeters : 3000.0;
                                minimapScale = (cfg.MinimapScale >= 0.70f && cfg.MinimapScale <= 1.60f) ? cfg.MinimapScale : 1.0f;
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
                                showMinimapTerritories = cfg.ShowMinimapTerritories;
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
                                compassScale = (cfg.CompassScale >= 0.70f && cfg.CompassScale <= 1.60f) ? cfg.CompassScale : 1.0f;
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
                    MinimapMode = (int)minimapMode,
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
                    ShowMinimapTerritories = showMinimapTerritories,
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
                    gps.GPSColor = entry.GpsColor;
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
            var localPlayer = MyAPIGateway.Session.Player;

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

                // Guard: Player can be null during respawn/disconnect — fall back to NoOwnership rather than throw.
                var relation = localPlayer?.GetRelationTo(gridOwner) ?? MyRelationsBetweenPlayerAndBlock.NoOwnership;

                bool isOwner = (gridOwner == localPlayerId && localPlayerId != 0);

                // gridOwner == 0 means the grid is UNOWNED (no player set ownership), not necessarily an NPC.
                // Rely solely on the faction NPC check for correct NPC gold color; unowned shows as neutral white.
                bool isNpc = false;
                if (gridOwner != 0)
                {
                    var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(gridOwner);
                    if (faction != null && faction.IsEveryoneNpc()) isNpc = true;
                }

                bool isOwnOrFaction = isOwner || relation == MyRelationsBetweenPlayerAndBlock.FactionShare || relation == MyRelationsBetweenPlayerAndBlock.Owner;

                Color signalColor;
                byte signalRelation;
                if (isOwner)
                {
                    signalColor = new Color(100, 230, 255);
                    signalRelation = REL_OWN;
                }
                else if (relation == MyRelationsBetweenPlayerAndBlock.FactionShare || relation == MyRelationsBetweenPlayerAndBlock.Owner)
                {
                    signalColor = new Color(100, 240, 100);
                    signalRelation = REL_FACTION;
                }
                else if (relation == MyRelationsBetweenPlayerAndBlock.Enemies)
                {
                    signalColor = new Color(255, 60, 60);
                    signalRelation = REL_HOSTILE;
                }
                else
                {
                    signalColor = isNpc ? new Color(240, 180, 50) : Color.White;
                    signalRelation = isNpc ? REL_NPC : REL_NEUTRAL;
                }

                // One marker per grid (de-clutter rule): track the NEAREST in-range broadcasting block.
                // GetBlocks() ordering is arbitrary, so the old first-found 'break' could pin a far beacon
                // while ignoring a much closer antenna on the same grid. Tracked via locals — zero allocations.
                double bestDistSq = double.MaxValue;
                Vector3D bestPos = Vector3D.Zero;
                string bestName = null;

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
                    }
                }

                if (bestDistSq < double.MaxValue)
                {
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
            UpdateTerritories();

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
        /// for the one site nearest it within that site's radius (150 m, more for long stations), and an NPC signal for
        /// the site its grid origin sits on (MES places these grids by origin; their beacons can be 150 m off it).
        /// A view skips a site only once it has drawn the GPS point or signal standing in for it. Player signals stand in for nothing.
        /// </summary>
        private void AddKnownSiteWaypoints(Vector3D playerPos)
        {
            if (!_knownSitesLoaded) LoadKnownSites();
            int siteCount = _knownSites.Count;
            if (siteCount == 0) return;
            if (_siteStatusVersion != GvkSiteStatus.Version) RefreshSiteStatus();

            Array.Clear(_knownSiteHasStandIn, 0, siteCount);
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
                wp.StandsInFor = nearest + 1;
                activeHudWaypoints[i] = wp;
                _knownSiteHasStandIn[nearest] = true;
            }

            for (int s = 0; s < siteCount; s++)
            {
                var site = _knownSites[s];
                site.DistanceMeters = Vector3D.Distance(playerPos, site.Coords);
                site.HasStandIn = _knownSiteHasStandIn[s];
                site.DisplayColor = GetSiteColor(s); // Every scan, since the player's standing can change
                site.KindText = _siteKindText[s];
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

        /// <summary>The player's standing with an NPC faction: green Friendly, red hostile, white neutral, unowned (UNKN) or unknown.</summary>
        private static Color GetStandingColor(IMyFaction faction)
        {
            long me = MyAPIGateway.Session.Player?.IdentityId ?? 0;
            if (faction == null || faction.Tag == "UNKN" || me == 0) return SITE_WHITE;
            int rep = MyAPIGateway.Session.Factions.GetReputationBetweenPlayerAndFaction(me, faction.FactionId);
            if (rep >= SITE_FRIENDLY_REP) return SITE_FRIENDLY;
            if (rep < SITE_HOSTILE_REP) return SITE_HOSTILE;
            return SITE_WHITE;
        }

        /// <summary>
        /// Rebuilds a territory ring when the server reports a new size, and recolours it by standing. A ring shows only
        /// once the server has sent its size, so a client never shows the zone profile's 10 km before the real one.
        /// </summary>
        private void UpdateTerritories()
        {
            var territories = GvkKnownSites.Territories;
            int n = territories.Count;
            if (_territoryUV.Count != n)
            {
                _territoryUV.Clear();
                _territoryRows.Clear();
                for (int t = 0; t < n; t++)
                {
                    _territoryUV.Add(new List<Vector2>());
                    _territoryRows.Add(new List<TerritoryRow>());
                }
                _territoryRadius = new double[n];
                _territoryColor = new Color[n];
            }
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
                    BuildTerritoryRing(territory.Center, radius, _territoryUV[t], _territoryRows[t]);
                    _refreshMinimapNextFrame = true;
                }
                _territoryColor[t] = GetStandingColor(MyAPIGateway.Session.Factions.TryGetFactionByTag(territory.Tag));
            }
        }

        /// <summary>
        /// Where a territory sphere meets the ground: a circle on the planet (sea level, PLANET_RADIUS) whose angular
        /// radius A comes from the triangle planet centre, zone centre, edge point: cos A = (R² + d² - r²) / 2Rd.
        /// Points are spaced evenly round that circle on the globe and put through <see cref="WorldToMapUV"/>, so the ring
        /// takes the map's own stretch (east-west by 1 / cos latitude) and wraps at its edges like everything else on it.
        /// The inside is kept as one strip per map row: a point at latitude p is inside when
        /// sin p sin pc + cos p cos pc cos(dLon) >= cos A, which gives each row's longitude span exactly.
        /// </summary>
        private void BuildTerritoryRing(Vector3D center, double radius, List<Vector2> uv, List<TerritoryRow> rows)
        {
            uv.Clear();
            rows.Clear();
            if (radius <= 0.0) return;
            Vector3D toCenter = center - PLANET_CENTER;
            double d = toCenter.Length();
            if (d < 1.0) return;
            Vector3D c = toCenter / d;
            double r2 = PLANET_RADIUS * PLANET_RADIUS;
            double cosA = (r2 + d * d - radius * radius) / (2.0 * PLANET_RADIUS * d);
            if (cosA >= 1.0) return; // The sphere misses the ground
            if (cosA <= -1.0)        // It covers the whole planet: no edge, all inside
            {
                for (int r = 0; r < TERRITORY_FILL_ROWS; r++)
                    rows.Add(new TerritoryRow { Row = r, U0 = 0f, U1 = 1f });
                return;
            }
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

            // Inside strips, in the map's own lat/lon (WorldToMapUV: U linear in longitude, V linear in latitude)
            Vector2 cuv = WorldToMapUV(center);
            double latC = Math.PI * 0.5 - cuv.Y * Math.PI;
            double sinC = Math.Sin(latC), cosC = Math.Cos(latC);
            for (int r = 0; r < TERRITORY_FILL_ROWS; r++)
            {
                double v = (r + 0.5) / TERRITORY_FILL_ROWS;
                double lat = Math.PI * 0.5 - v * Math.PI;
                double denom = Math.Cos(lat) * cosC;
                double k = denom > 1e-9 ? (cosA - Math.Sin(lat) * sinC) / denom : (Math.Sin(lat) * sinC >= cosA ? -2.0 : 2.0);
                if (k >= 1.0) continue;                  // The whole row is outside
                if (k <= -1.0)                           // The whole row is inside
                {
                    rows.Add(new TerritoryRow { Row = r, U0 = 0f, U1 = 1f });
                    continue;
                }
                double half = Math.Acos(k) / (2.0 * Math.PI);
                double u0 = cuv.X - half, u1 = cuv.X + half;
                if (u0 < 0.0)
                {
                    rows.Add(new TerritoryRow { Row = r, U0 = 0f, U1 = (float)u1 });
                    rows.Add(new TerritoryRow { Row = r, U0 = (float)(u0 + 1.0), U1 = 1f });
                }
                else if (u1 > 1.0)
                {
                    rows.Add(new TerritoryRow { Row = r, U0 = (float)u0, U1 = 1f });
                    rows.Add(new TerritoryRow { Row = r, U0 = 0f, U1 = (float)(u1 - 1.0) });
                }
                else rows.Add(new TerritoryRow { Row = r, U0 = (float)u0, U1 = (float)u1 });
            }
        }

        /// <summary>
        /// Draws the territory insides (a faint tint, one strip per map row) and their rings as dots, on a map drawn
        /// w x h with map UV (viewU, viewV) at (ox, oy). Points outside
        /// halfW x halfH are left out; a point closer than spacing (on screen) to the last dot drawn is skipped, and where two
        /// neighbouring points land further apart (a ring passing close to a pole stretches far east-west) dots are added
        /// between them, so the dots stay evenly spaced at any zoom. No dots are added across the map's wrap seam.
        /// </summary>
        private void AddTerritoryDots(GvkScreenLayer layer, double ox, double oy, double w, double h, double viewU, double viewV,
            double halfW, double halfH, float aspect, float dotSize, double spacing)
        {
            double spacingSq = spacing * spacing;
            for (int t = 0; t < _territoryRows.Count; t++)
            {
                Color fill = _territoryColor[t] * TERRITORY_FILL_ALPHA;
                var rows = _territoryRows[t];
                for (int i = 0; i < rows.Count; i++)
                {
                    // Neighbouring rows compute their shared edge the same way, and AddPixelRect snaps both to the same
                    // pixel, so the strips tile exactly: a fractional overlap showed as faint grey lines that moved with zoom
                    double yTop = Math.Min((viewV - rows[i].Row / (double)TERRITORY_FILL_ROWS) * h, halfH);
                    double yBottom = Math.Max((viewV - (rows[i].Row + 1) / (double)TERRITORY_FILL_ROWS) * h, -halfH);
                    if (yBottom >= yTop) continue;
                    double x0 = Math.Max((rows[i].U0 - viewU) * w, -halfW);
                    double x1 = Math.Min((rows[i].U1 - viewU) * w, halfW);
                    if (x1 <= x0) continue;
                    layer.AddPixelRect(MATERIAL_SQUARE, ox + x0, oy + yTop, ox + x1, oy + yBottom, fill);
                }
            }
            for (int t = 0; t < _territoryUV.Count; t++)
            {
                var points = _territoryUV[t];
                if (points.Count == 0) continue;
                Color color = _territoryColor[t] * TERRITORY_RING_ALPHA;
                double lastX = 0.0, lastY = 0.0, prevX = 0.0, prevY = 0.0, prevU = 0.0;
                bool any = false, prevShown = false;
                int count = points.Count;
                for (int k = 0; k <= count; k++) // k == count closes the ring back to the first point
                {
                    int i = k == count ? 0 : k;
                    double x = (points[i].X - viewU) * w;
                    double y = (viewV - points[i].Y) * h;
                    bool shown = Math.Abs(x) <= halfW && Math.Abs(y) <= halfH;
                    if (shown && prevShown && Math.Abs(points[i].X - prevU) < 0.5) // Same side of the wrap seam
                    {
                        double gx = x - prevX;
                        double gy = (y - prevY) / aspect; // HUD Y units are aspect times shorter on screen
                        int steps = (int)(Math.Sqrt(gx * gx + gy * gy) / spacing);
                        for (int st = 1; st < steps; st++)
                        {
                            double f = (double)st / steps;
                            lastX = prevX + (x - prevX) * f;
                            lastY = prevY + (y - prevY) * f;
                            layer.AddSprite(MATERIAL_SQUARE, ox + lastX, oy + lastY, dotSize, dotSize * aspect, color);
                        }
                    }
                    prevX = x;
                    prevY = y;
                    prevU = points[i].X;
                    prevShown = shown;
                    if (!shown || k == count) continue;
                    if (any)
                    {
                        double dx = x - lastX;
                        double dy = (y - lastY) / aspect;
                        if (dx * dx + dy * dy < spacingSq) continue;
                    }
                    any = true;
                    lastX = x;
                    lastY = y;
                    layer.AddSprite(MATERIAL_SQUARE, ox + x, oy + y, dotSize, dotSize * aspect, color);
                }
            }
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
                if (wp.SiteKind != 0 && wp.DistanceMeters > COMPASS_SITE_MAX_DIST) continue;

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
            var gold = new Color(255, 215, 60, 255);
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
                            case GraduationType.Cardinal: h = 0.007f * s; c = new Color(255, 215, 60, 240); break;
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
                            cardinal ? new Color(255, 215, 60) : Color.White, TOP_CENTER, true);
                    }
                    if (grad.BottomLabel != null && labels < COMPASS_MAX_LABELS)
                    {
                        labels++;
                        _ssCompass.AddText(grad.BottomLabel, x, bottomLabelY, 0.48f * s * textScale,
                            cardinal ? new Color(255, 225, 120) : Color.White, TOP_CENTER, true);
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
            return new Color(41, 54, 62, (byte)MathHelper.Clamp((int)(255f * opacity), 0, 255));
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
                float strategicBoxWidth = (float)minimapSize.X + 0.024f * currentScale;
                zoneWidth = (minimapMode == MinimapDisplayMode.TacticalRadar) ? radarBoxWidth : strategicBoxWidth;
                zoneHeight = 0.056f * currentScale;
                isCompact = (minimapMode == MinimapDisplayMode.TacticalRadar);

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

            _ssZoneBar.BeginRuns(textX, line1Y, scale1 * textScale, true);
            if (inSpace) _ssZoneBar.AddRun("[ SPAAAAAACE!!! ]", SPACE_TEXT_COLOR);
            else _ssZoneBar.AddRun(ZONE_TITLES[zone], ZONE_TEXT_COLORS[zone]);

            _ssZoneBar.BeginRuns(textX, line2Y, scale2 * textScale, true);
            if (inSpace)
            {
                _ssZoneBar.AddRun("Kharak gravity: ", ZONE_LABEL_COLOR);
                _ssZoneBar.AddRun("LOST", SPACE_TEXT_COLOR);
                _ssZoneBar.AddRun(SEPARATOR, ZONE_SEPARATOR_COLOR);
                _ssZoneBar.AddRun("Below: ", ZONE_LABEL_COLOR);
                _ssZoneBar.AddRun(ZONE_SHORT_NAMES[zone], ZONE_TEXT_COLORS[zone]);
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
        /// Updates the corner minimap or tactical vector radar.
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
            float strategicBoxWidth = (float)minimapSize.X + 0.024f * minimapScale;
            float minimapBgWidth = (minimapMode == MinimapDisplayMode.TacticalRadar) ? radarBoxWidth : strategicBoxWidth;
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
        /// Minimap and tactical radar, drawn in the layout and order the Text HUD API minimap had (card, accent, map or grid, FOV lines, zone lines, label, markers, arrow).
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
            // High-visibility pulse: Electric Gold <-> Amber
            Color arrowColor = (tickCounter % 40 < 20) ? new Color(255, 230, 40, 255) : new Color(255, 255, 140, 255);
            double playerElevation = Vector3D.Distance(playerPos, PLANET_CENTER) - PLANET_RADIUS;
            int markers = 0;
            int sites = 0;

            if (minimapMode == MinimapDisplayMode.StrategicMap)
            {
                _ssMinimap.AddSprite(MATERIAL_MAP, cx, cy, minimapSize.X, minimapSize.Y, Color.White);
                if (showMinimapTerritories)
                    AddTerritoryDots(_ssMinimap, cx, cy, minimapSize.X, minimapSize.Y, 0.5, 0.5, minimapSize.X * 0.5, minimapSize.Y * 0.5,
                        aspect, 0.0015f * s, 0.005 * s);

                // Pass 0 picks the other markers and marks the sites they stand in for, pass 1 draws the sites,
                // pass 2 draws pass 0's picks again on top (same order and tests, so the same picks)
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
                        if (!TakeMinimapSlot(ref wp, ref markers, ref sites)) continue;
                        if (pass == 0)
                        {
                            MarkStandInDrawn(ref wp);
                            continue;
                        }
                        float ms = wp.SiteKind != 0 ? markerSize * SITE_ICON_SCALE : markerSize;
                        _ssMinimap.AddSprite(GetWaypointMaterial(ref wp, playerElevation),
                            cx + (wp.MapUV.X - 0.5) * minimapSize.X, cy + (0.5 - wp.MapUV.Y) * minimapSize.Y,
                            ms, ms * aspect, GetMarkerColor(ref wp));
                    }
                }

                Vector2 uv = WorldToMapUV(playerPos);
                _ssMinimap.AddSprite(MATERIAL_NAV_ARROW, cx + (uv.X - 0.5) * minimapSize.X, cy + (0.5 - uv.Y) * minimapSize.Y,
                    arrowSize, arrowSize * aspect, arrowColor, -playerHeadingRad);
                return;
            }

            // Tactical radar
            float gridDiameter = (float)minimapSize.Y * 0.95f;
            _ssMinimap.AddSprite(MATERIAL_RADAR_GRID, cx, cy, gridDiameter / aspect, gridDiameter, new Color(187, 233, 246, 200));

            // Radar circle radius on screen (aspect corrected for a 1:1 circle)
            float radiusY = (float)minimapSize.Y * 0.46f;
            float radiusX = radiusY / aspect;

            // Camera view "V": widens and narrows with game FOV and zoom
            if (showRadarFovLines)
            {
                float halfHFov = (float)Math.Atan(aspect * Math.Tan(camera.FovWithZoom * 0.5f));
                double armX = Math.Sin(halfHFov) * radiusX;
                double armY = Math.Cos(halfHFov) * radiusY;
                var fovLineColor = new Color(187, 233, 246, 110);
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
            var labelGrey = new Color(200, 200, 200);
            _ssMinimap.BeginRuns(cx + radarBoxWidth * 0.5 - 0.052 * s, cy - bgHeight * 0.5 + 0.016 * s, 0.46f * s * textScale, true);
            if (radarScale == RadarScaleMode.Logarithmic)
            {
                _ssMinimap.AddRun("30 KM ", labelGrey);
                _ssMinimap.AddRun("(LOG)", new Color(187, 233, 246));
            }
            else
            {
                _ssMinimap.AddRun(GetRadarRangeText(), labelGrey);
            }

            // The sector map's three passes: pick and mark, draw the sites, draw the picks on top
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
                if (wp.KothState == 0 && !(wp.IsSignal ? showMapSignalLabels : showMapGpsLabels)) // KOTH labels always show; sites follow GPS Labels
                {
                    _mapPlainWps.Add(i);
                    continue;
                }

                float width;
                GetSsMapLabel(i, out width);

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
            if (showMapTerritories)
                AddTerritoryDots(_ssMap, 0.0, 0.0, mapW, mapH, viewU, viewV, frameW * 0.5, frameH * 0.5, aspect, 0.0022f, TERRITORY_MAP_DOT_SPACING);
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

                float width;
                string text = GetSsMapLabel(wpIdx, out width);
                double lx = slotPos.X + GetMapLabelOffsetX(ref cluster, width);
                double ly = slotPos.Y + MAP_LABEL_OFFSET_Y + cluster.NudgeY;
                _ssMap.AddText(text, lx, ly, labelScale, Color.White, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP, true);
                AddMapHit(wpIdx, lx, lx + width, ly - _mapTextHeight, ly);
            }

            var overflowColor = new Color(170, 210, 245);
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
                Color arrowColor = (tickCounter % 40 < 20) ? new Color(255, 230, 40, 255) : new Color(255, 255, 140, 255);
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
                inSpace ? Color.Magenta : GetZoneColor(zone));

            double textX = -headerW * 0.5f + 0.016f;
            _ssMap.BeginRuns(textX, headerY + 0.013f, 0.62f * textScale, true);
            _ssMap.AddRun("KHARAK TACTICAL SATELLITE MAP", new Color(255, 220, 0));
            _ssMap.AddRun(" | Current Sector: ", MAP_HEADER_GREY);
            _ssMap.AddRun(ZONE_TITLES[zone], ZONE_TEXT_COLORS[zone]);

            _ssMap.BeginRuns(textX, headerY - 0.011f, 0.50f * textScale, true);
            _ssMap.AddRun("Crossroads: ", new Color(220, 230, 240));
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

            // Controls line on its own card docked under the frame, mirroring the header
            float hintScale = MAP_HINT_SCALE * textScale;
            double hintW = _ssMap.MeasureWidthHud(MAP_CONTROLS_HINT, hintScale) + 0.04;
            double hintH = _ssMap.LineHeightHud(hintScale) + 0.012;
            double hintTop = -topOfFrame - 0.004;
            _ssMap.AddSprite(MATERIAL_SQUARE, 0.0, hintTop - hintH * 0.5, hintW, hintH, card);
            _ssMap.AddText(MAP_CONTROLS_HINT, 0.0, hintTop - 0.006, hintScale, new Color(220, 230, 240),
                MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP, true);
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
            BuildMapSettingsButton(onSettings);

            // The right-click menu takes the clicks while it is open; Esc is handled in OnMapScreenCancelling
            if (_mapMenuOpen)
            {
                bool leftClick = hasCursor && input.IsNewLeftMousePressed();
                if (input.IsNewKeyPressed(MyKeys.Enter) || (leftClick && IsInMapHit(ref _mapMenuOk, cursor)))
                {
                    ConfirmMapMenu();
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

        private void BuildMapSettingsButton(bool hover)
        {
            if (!_ssMapButton.Begin(_ssMapViewport)) return;
            var b = _mapSettingsBox;
            double cx = (b.MinX + b.MaxX) * 0.5;
            double cy = (b.MinY + b.MaxY) * 0.5;
            _ssMapButton.AddSprite(MATERIAL_SQUARE, cx, cy, b.MaxX - b.MinX, b.MaxY - b.MinY,
                hover ? new Color(70, 95, 112, 255) : new Color(31, 41, 48, 255));
            if (hover) _ssMapButton.AddSprite(MATERIAL_SQUARE, cx, b.MinY + 0.002, b.MaxX - b.MinX, 0.004, new Color(187, 233, 246));
            _ssMapButton.AddText(MAP_SETTINGS_TEXT, cx, cy, _mapSettingsScale, hover ? Color.White : new Color(187, 233, 246),
                MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, true);
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
        /// Right-click menu at a point: on a GPS marker or label it offers to remove that GPS, anywhere else to add
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
                _mapMenuTitle = "Remove GPS '" + _mapMenuGpsName + "'?";
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

        private void ConfirmMapMenu()
        {
            bool remove = _mapMenuRemove;
            CloseMapMenu();
            if (remove) RemoveGpsFromMap(_mapMenuGpsName, _mapMenuGpsCoords);
            else AddGpsFromMap(_mapMenuU, _mapMenuV);
        }

        /// <summary>Esc closes the right-click menu first, then the map.</summary>
        public bool OnMapScreenCancelling()
        {
            if (!_mapMenuOpen) return true;
            CloseMapMenu();
            return false;
        }

        /// <summary>The right-click menu card: a question, then the action and Cancel buttons; the one under the cursor lights up.</summary>
        private void BuildMapMenu(bool hasCursor, Vector2D cursor)
        {
            _ssMapTip.Clear();
            if (!_ssMapMenu.Begin(_ssMapViewport)) return;
            float aspect = _ssMapViewport.X / _ssMapViewport.Y;
            float titleScale = 0.60f * _ssMapTextScale;
            float buttonScale = 0.60f * _ssMapTextScale;
            string okText = _mapMenuRemove ? "Remove" : "Add GPS";
            const string CANCEL_TEXT = "Cancel";

            // Where the new GPS goes, so the player sees the spot before confirming
            if (!_mapMenuRemove)
                _ssMapMenu.AddSprite(MATERIAL_MARKER_GPS, _mapMenuAt.X, _mapMenuAt.Y, 0.0225f, 0.0225f * aspect, new Color(187, 233, 246));

            double titleW = _ssMapMenu.MeasureWidthHud(_mapMenuTitle, titleScale);
            double titleH = _ssMapMenu.LineHeightHud(titleScale);
            double buttonTextH = _ssMapMenu.LineHeightHud(buttonScale);
            double buttonW = Math.Max(_ssMapMenu.MeasureWidthHud(okText, buttonScale), _ssMapMenu.MeasureWidthHud(CANCEL_TEXT, buttonScale)) + 0.03;
            double buttonH = buttonTextH + 0.008 * aspect;
            double padX = 0.010;
            double padY = 0.008 * aspect;
            double gap = 0.010;
            double boxW = 0.004 + padX * 2.0 + Math.Max(titleW, buttonW * 2.0 + gap);
            double boxH = padY * 3.0 + titleH + buttonH;

            double left = _mapMenuAt.X + 0.016;
            double top = _mapMenuAt.Y - 0.016 * aspect;
            if (left + boxW > 0.99) left = _mapMenuAt.X - 0.016 - boxW;
            if (top - boxH < -0.99) top = _mapMenuAt.Y + 0.016 * aspect + boxH;

            Color accent = _mapMenuRemove ? new Color(255, 80, 70) : new Color(187, 233, 246);
            _ssMapMenu.AddSprite(MATERIAL_SQUARE, left + boxW * 0.5, top - boxH * 0.5, boxW, boxH, new Color(15, 22, 30, 245));
            _ssMapMenu.AddSprite(MATERIAL_SQUARE, left + 0.002, top - boxH * 0.5, 0.003, boxH, accent);
            double textX = left + 0.004 + padX;
            _ssMapMenu.AddText(_mapMenuTitle, textX, top - padY, titleScale, Color.White, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP, true);

            double buttonTop = top - padY * 2.0 - titleH;
            _mapMenuOk = AddMapMenuButton(okText, textX, buttonTop, buttonW, buttonH, buttonScale, accent, hasCursor, cursor);
            AddMapMenuButton(CANCEL_TEXT, textX + buttonW + gap, buttonTop, buttonW, buttonH, buttonScale, new Color(200, 200, 200), hasCursor, cursor);
        }

        private MapHit AddMapMenuButton(string text, double left, double top, double width, double height, float scale, Color color, bool hasCursor, Vector2D cursor)
        {
            var box = new MapHit { Wp = -1, MinX = left, MaxX = left + width, MinY = top - height, MaxY = top };
            bool hover = hasCursor && IsInMapHit(ref box, cursor);
            _ssMapMenu.AddSprite(MATERIAL_SQUARE, left + width * 0.5, top - height * 0.5, width, height,
                hover ? new Color(70, 95, 112, 255) : new Color(41, 54, 62, 255));
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
            if (wpIdx < 0 || !_ssMapTip.Begin(_ssMapViewport)) return;

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

            _ssMapTip.AddSprite(MATERIAL_SQUARE, left + boxW * 0.5, top - boxH * 0.5, boxW, boxH, new Color(15, 22, 30, 235));
            _ssMapTip.AddSprite(MATERIAL_SQUARE, left + 0.002, top - boxH * 0.5, 0.003, boxH, wp.DisplayColor);
            double textX = left + 0.004 + padX;
            _ssMapTip.AddText(name, textX, top - padY, nameScale, Color.White, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP, true);
            _ssMapTip.BeginRuns(textX, top - padY - nameH, infoScale, true);
            _ssMapTip.AddRun(kind, wp.DisplayColor);
            _ssMapTip.AddRun(GAP, Color.White);
            _ssMapTip.AddRun(dist, new Color(200, 200, 200));
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
            double lon = u * 2.0 * Math.PI - Math.PI + 0.2540 * 2.0 * Math.PI; // Undo WorldToMapUV's prime meridian shift
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
            return inSpace ? Color.Magenta : GetZoneColor(currentZoneIndex);
        }

        private Color GetZoneColor(int zone)
        {
            switch (zone)
            {
                case 0: return Color.LimeGreen;
                case 1: return Color.Yellow;
                case 2: return Color.Orange;
                default: return Color.Red;
            }
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
        /// Converts planetary 3D world position to UV [0, 1] matching KharakMap.dds / Kharak Zone Map V3.
        /// Includes the -91.4° (-0.254) longitude shift to align Crossroads (U=0.273) and Zone 3 (U=0.749).
        /// Inverts Y to match the map texture's North/South latitude orientation.
        /// </summary>
        private Vector2 WorldToMapUV(Vector3D worldPos)
        {
            Vector3D R = worldPos - PLANET_CENTER;
            double len = R.Length();
            if (len < 1.0) return new Vector2(0.5f, 0.5f);

            Vector3D u = R / len;
            double lat = Math.Asin(MathHelper.Clamp(-u.Y, -1.0, 1.0)); // Invert Y for map orientation
            double lon = Math.Atan2(u.Z, u.X) - 0.2540 * 2.0 * Math.PI; // Align to map prime meridian

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

            if (msg.Equals("/gvk minimap", StringComparison.OrdinalIgnoreCase))
            {
                sendToOthers = false;
                ToggleMinimap();
                return;
            }

            if (msg.Equals("/gvk radar", StringComparison.OrdinalIgnoreCase))
            {
                sendToOthers = false;
                ToggleMinimapMode();
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
            MyAPIGateway.Utilities.ShowNotification($"[GVK NAV] Corner Minimap: {status}", 2500, showMinimap ? MyFontEnum.Green : MyFontEnum.Red);
        }

        private void ToggleMinimapMode()
        {
            if (minimapMode == MinimapDisplayMode.StrategicMap)
                minimapMode = MinimapDisplayMode.TacticalRadar;
            else
                minimapMode = MinimapDisplayMode.StrategicMap;

            ApplyMinimapScale(minimapScale);
            _refreshMinimapNextFrame = true;
            SaveConfig();
            string modeName = (minimapMode == MinimapDisplayMode.TacticalRadar) ? "TACTICAL RADAR (LOCAL)" : "STRATEGIC MAP (GLOBAL)";
            MyAPIGateway.Utilities.ShowNotification($"[GVK NAV] Minimap Mode: {modeName}", 2500, MyFontEnum.Green);
        }

        private void CycleMinimapScale()
        {
            if (minimapScale < 0.85f)
                minimapScale = 1.0f;
            else if (minimapScale < 1.1f)
                minimapScale = 1.25f;
            else if (minimapScale < 1.35f)
                minimapScale = 1.5f;
            else
                minimapScale = 0.75f;

            ApplyMinimapScale(minimapScale);
            _refreshMinimapNextFrame = true;
            SaveConfig();
            int pct = (int)Math.Round(minimapScale * 100);
            MyAPIGateway.Utilities.ShowNotification($"[GVK NAV] Minimap Size: {pct}%", 2500, MyFontEnum.Green);
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
            float strategicBoxWidth = (float)minimapSize.X + 0.024f * scale;
            float minimapBgWidth = (minimapMode == MinimapDisplayMode.TacticalRadar) ? radarBoxWidth : strategicBoxWidth;
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
            MyAPIGateway.Utilities.ShowNotification($"[GVK NAV] Minimap Position: {MinimapPresets[nextIdx].Name}", 2500, MyFontEnum.Green);
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
            MyAPIGateway.Utilities.ShowNotification("[GVK NAV] Minimap Position reset to Top-Right (1.0, 1.0)", 2500, MyFontEnum.Green);
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

        private void CycleCompassScale()
        {
            if (compassScale < 0.85f)
                compassScale = 1.0f;
            else if (compassScale < 1.1f)
                compassScale = 1.25f;
            else if (compassScale < 1.35f)
                compassScale = 1.5f;
            else
                compassScale = 0.75f;

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

        private static bool IsMarkerShown(ref ActiveHudWaypoint wp, bool showGps, bool showSignals, bool showOwnSignals, bool showKoth, bool showSites)
        {
            if (wp.KothState != 0) return showKoth;
            if (wp.SiteKind != 0) return showSites; // Views also skip a site that has a stand-in: SkipForPass, or HasStandIn on the map
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
            minimapMode = MinimapDisplayMode.StrategicMap;
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
            showMinimapTerritories = true;
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
            MyAPIGateway.Utilities.ShowMessage("GVK", "Commands: /gvk settings, map, minimap, radar, range, compass, zone, gps, rate, reset.");
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