using System;
using System.Collections.Generic;
using System.Text;
using Draygo.API;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Input;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;

namespace GVK.Navigation
{
    /// <summary>
    /// Kharak Tactical Navigation, Compass, Minimap & Satellite Map Suite:
    /// - Authentic HUD Compass frame (compass.dds) with tape centered between the bars.
    /// - Forward-accurate horizon azimuth tracking: driving towards a POI decreases distance to zero.
    /// - Keen Vanilla HUD Markers: marker_gps for GPS waypoints and relation markers (friendly, enemy, neutral, self) for radio signals.
    /// - Pre-allocated billboard pools for Minimap and Satellite Map (Zero GC allocation in hot paths, 100% reliable rendering).
    /// - Aspect-ratio corrected billboard scaling (no more height-squished icons or maps on widescreen/ultrawide).
    /// - Calibrated WorldToMapUV projection aligned to KharakMap.dds longitude (-91.4° meridian shift) & inverted latitude.
    /// - Displays waypoints and active radio broadcast signals with authentic Keen icons on Compass, Minimap, and Map.
    /// - Pinpoint Antenna/Beacon tracking: points directly at the physical block itself with no close-distance dropouts.
    /// - High-readability distance readouts (e.g. 1.2k, 15k) centered dynamically below each POI marker.
    /// - Live Corner Minimap (top-right, true 2:1 ratio, 20% enlarged) with accurate UV player and POI icons.
    /// - Unified Top-Right Tactical HUD: Zone Status & Border Countdown panel docked directly beneath the Minimap.
    /// - Upper-center screen clear of zone text for unobstructed WeaponCore target lock & lead indicator HUDs.
    /// - Full-Screen Interactive Satellite Map on [M] key with authentic Keen marker icons.
    /// - Auto-populating default Kharak GPS waypoints and /zone gps recovery.
    /// </summary>
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class GVK_ZoneNavCommand : MySessionComponentBase
    {
        // Planetary Constants for Pertam
        private static readonly Vector3D PLANET_CENTER = new Vector3D(33032.44, 32395.99, 32074.17);
        private const double PLANET_RADIUS = 30000.0;
        private static readonly Vector3 BASE_SOUTH = new Vector3(0f, 1f, 0f);

        // Origin Points
        private static readonly Vector3D CROSSROADS_BEACON = new Vector3D(62495.55, 28019.04, 37195.71);
        private static readonly Vector3D Z3_CENTER = new Vector3D(3569.33, 36772.94, 26952.63);

        // Zone radii from Crossroads
        private const double ZONE_0_RADIUS = 20000.0;
        private const double ZONE_1_RADIUS = 35000.0;
        private const double ZONE_2_RADIUS = 50000.0;

        // Texture Materials
        private static readonly MyStringId MATERIAL_SQUARE = MyStringId.GetOrCompute("Square");
        private static readonly MyStringId MATERIAL_COMPASS = MyStringId.GetOrCompute("compass");
        private static readonly MyStringId MATERIAL_MAP = MyStringId.GetOrCompute("KharakMap");
        private static readonly MyStringId MATERIAL_RADAR_GRID = MyStringId.GetOrCompute("RadarGrid");

        // Keen Vanilla HUD Markers (from Textures\HUD\)
        private static readonly MyStringId MATERIAL_MARKER_GPS = MyStringId.GetOrCompute("marker_gps");
        private static readonly MyStringId MATERIAL_MARKER_FRIENDLY = MyStringId.GetOrCompute("marker_friendly");
        private static readonly MyStringId MATERIAL_MARKER_ENEMY = MyStringId.GetOrCompute("marker_enemy");
        private static readonly MyStringId MATERIAL_MARKER_NEUTRAL = MyStringId.GetOrCompute("marker_neutral");
        private static readonly MyStringId MATERIAL_MARKER_SELF = MyStringId.GetOrCompute("marker_self");
        private static readonly MyStringId MATERIAL_MARKER_ALERT = MyStringId.GetOrCompute("marker_alert");
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
            public double BaseTopHalfWidth;
            public double BaseBottomHalfWidth;

            public CompassGraduation(float offset, GraduationType type, string topLabel = null, string bottomLabel = null)
            {
                Offset = offset;
                Type = type;
                TopLabel = topLabel;
                BottomLabel = bottomLabel;
                BaseTopHalfWidth = -1.0;
                BaseBottomHalfWidth = -1.0;
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

        // Active HUD Waypoint (GPS or real broadcast signal)
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
            public double PlanetaryElevation;
        }

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
            new DefaultGpsEntry("Sevastapol", "Coalition Trade Station", new Vector3D(2990.66, 30484.89, 34060.65), false, false, new Color(239, 220, 0)),
            new DefaultGpsEntry("Skyport", "Aerial Trade Station", new Vector3D(40585.81, 59619.14, 46601.96), false, false, new Color(239, 220, 0)),
            new DefaultGpsEntry("Mastodon", "Southern Trade Station", new Vector3D(33475.48, 5361.51, 20848.44), false, false, new Color(239, 220, 0)),
            new DefaultGpsEntry("Rusty's", "Starter Trade Outpost", new Vector3D(61686.17, 27088.08, 38160.96), false, false, new Color(239, 220, 0)),
            new DefaultGpsEntry("Coalition Base", "Coalition Headquarters", new Vector3D(61784.00, 28043.10, 38552.08), false, false, new Color(239, 220, 0)),
            new DefaultGpsEntry("KOTH Khar Toba", "King of the Hill Site", new Vector3D(4115.21, 38891.59, 26152.68), false, false, new Color(255, 128, 31)),
            new DefaultGpsEntry("KOTH Kalash Site", "King of the Hill Site", new Vector3D(8815.53, 14612.65, 35353.97), false, false, new Color(255, 128, 31)),
            new DefaultGpsEntry("KOTH Crashed Starship", "King of the Hill Site", new Vector3D(25893.61, 61095.89, 25703.05), false, false, new Color(255, 128, 31))
        };

        // TextHUDAPI
        private HudAPIv2 hudApi;

        // 1. Custom Programmatic Tactical HUD Compass Elements
        private const double COMPASS_TOP_Y = 0.982;
        private HudAPIv2.BillBoardHUDMessage compassBg;
        private HudAPIv2.BillBoardHUDMessage compassLeftAccent;
        private HudAPIv2.BillBoardHUDMessage compassRightAccent;
        private HudAPIv2.BillBoardHUDMessage compassCenterPointer;
        private HudAPIv2.BillBoardHUDMessage compassCenterBottomPip;
        private readonly List<HudAPIv2.BillBoardHUDMessage> compassTickPool = new List<HudAPIv2.BillBoardHUDMessage>();
        private readonly List<HudAPIv2.HUDMessage> compassTapePool = new List<HudAPIv2.HUDMessage>();
        private readonly List<HudAPIv2.BillBoardHUDMessage> waypointSpritePool = new List<HudAPIv2.BillBoardHUDMessage>();
        private readonly List<HudAPIv2.HUDMessage> waypointDistPool = new List<HudAPIv2.HUDMessage>();
        private bool showCompass = true;
        private bool _compassElementsVisible = false;
        private float compassScale = 1.0f;

        // 2. Zone Bar Elements (Docked directly beneath Corner Minimap in Top-Right)
        private HudAPIv2.HUDMessage zoneMsg;
        private HudAPIv2.HUDMessage zoneDistMsg;
        private HudAPIv2.BillBoardHUDMessage zoneBg;
        private HudAPIv2.BillBoardHUDMessage zoneAccent;
        private readonly StringBuilder zoneText = new StringBuilder(128);
        private readonly StringBuilder zoneDistText = new StringBuilder(128);
        private bool showZoneBar = true;
        private bool _zoneBarElementsVisible = false;
        private Vector2D zonePosition = Vector2D.Zero;

        // 3. Corner Minimap Elements (Top-Right: true 2:1 ratio for KharakMap, enlarged +20%)
        private HudAPIv2.BillBoardHUDMessage minimapHeaderBg;
        private HudAPIv2.BillBoardHUDMessage minimapHeaderAccent;
        private HudAPIv2.BillBoardHUDMessage minimapBg;
        private HudAPIv2.BillBoardHUDMessage minimapTerrain;
        private HudAPIv2.BillBoardHUDMessage minimapPlayerDot;
        private HudAPIv2.HUDMessage minimapLabel;
        private readonly List<HudAPIv2.BillBoardHUDMessage> minimapMarkerPool = new List<HudAPIv2.BillBoardHUDMessage>();
        private Vector2D minimapPosition = new Vector2D(0.81, 0.73);
        private Vector2D minimapSize = new Vector2D(0.312, 0.277); // Dynamically set with aspect ratio (+20%)
        private bool showMinimap = true;
        private bool _minimapElementsVisible = false;
        private float minimapScale = 1.0f;

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
        private HudAPIv2.BillBoardHUDMessage radarGrid;
        private HudAPIv2.BillBoardHUDMessage radarFovLeft;
        private HudAPIv2.BillBoardHUDMessage radarFovRight;

        public class ZoneNavConfig
        {
            public bool ShowMinimap { get; set; } = true;
            public int MinimapMode { get; set; } = 0;
            public int RadarScale { get; set; } = 0;
            public double RadarRangeMeters { get; set; } = 3000.0;
            public float MinimapScale { get; set; } = 1.0f;
            public bool ShowCompass { get; set; } = true;
            public float CompassScale { get; set; } = 1.0f;
            public bool ShowZoneBar { get; set; } = true;
            public int UpdateTickRate { get; set; } = 5;

            public ZoneNavConfig() { }
        }

        private const string CONFIG_FILENAME = "GVK_ZoneNavConfig.xml";

        // 4. Interactive Full-Screen Satellite Map ([M] Key, true 2:1 ratio)
        private bool showFullMap = false;
        private HudAPIv2.BillBoardHUDMessage mapDimmer;
        private HudAPIv2.BillBoardHUDMessage mapFrame;
        private HudAPIv2.BillBoardHUDMessage mapTerrain;
        private HudAPIv2.BillBoardHUDMessage mapPlayerDot;
        private HudAPIv2.BillBoardHUDMessage mapHeaderBg;
        private HudAPIv2.BillBoardHUDMessage mapHeaderAccent;
        private HudAPIv2.HUDMessage mapHeaderMsg;
        private HudAPIv2.HUDMessage mapHeaderSubMsg;
        private readonly StringBuilder mapHeaderText = new StringBuilder(128);
        private readonly StringBuilder mapHeaderSubText = new StringBuilder(128);
        private readonly List<HudAPIv2.BillBoardHUDMessage> fullMapMarkerPool = new List<HudAPIv2.BillBoardHUDMessage>();
        private readonly List<HudAPIv2.HUDMessage> fullMapLabelPool = new List<HudAPIv2.HUDMessage>();

        // Dynamic State
        private int currentZoneIndex = 0;
        private double lastDistKm = 0.0;
        private double lastRemainingKm = 0.0;
        private double lastDistZ3Km = 0.0;
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

        // Cached aspect ratio & satellite map dimensions to avoid per-frame camera queries
        private float cachedAspect = 16f / 9f;
        private float cachedFullMapWidth = 1.40f;
        private float cachedFullMapHeight = 0.70f * (16f / 9f);
        private Vector2 cachedViewportSize = Vector2.Zero;

        // Zone bar dirty tracking — skip text rebuild when nothing changed.
        private int _lastZoneBarZoneIndex = -1;
        private MinimapDisplayMode _lastZoneBarMinimapMode = (MinimapDisplayMode)(-1);
        private double _lastZoneBarDistKm = -1.0;
        private double _lastZoneBarRemainingKm = -1.0;
        private double _lastZoneBarDistZ3Km = -1.0;

        // Active HUD Waypoints Buffer
        private List<ActiveHudWaypoint> activeHudWaypoints = new List<ActiveHudWaypoint>();
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

        // Reusable StringBuilder for mission screen text (avoids per-call allocation).
        private readonly StringBuilder _missionSb = new StringBuilder(512);

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
            public Vector2D Position;
            public int Count;
            public int WpIndex0;
            public int WpIndex1;
            public int WpIndex2;
            public int WpIndex3;
            public int WpIndex4;
        }
        private List<MapCluster> _cachedMapClusters = new List<MapCluster>(32);


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
                                compassScale = (cfg.CompassScale >= 0.70f && cfg.CompassScale <= 1.60f) ? cfg.CompassScale : 1.0f;
                                showZoneBar = cfg.ShowZoneBar;
                                updateTickRate = (cfg.UpdateTickRate >= 1 && cfg.UpdateTickRate <= 60) ? cfg.UpdateTickRate : 5;
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
                    ShowCompass = showCompass,
                    CompassScale = compassScale,
                    ShowZoneBar = showZoneBar,
                    UpdateTickRate = updateTickRate
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
                hudApi = new HudAPIv2(OnHudApiRegistered);
            }
        }

        protected override void UnloadData()
        {
            if (!MyAPIGateway.Utilities.IsDedicated)
            {
                SaveConfig();
                MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;

                if (hudApi != null)
                {
                    compassBg?.DeleteMessage();
                    compassLeftAccent?.DeleteMessage();
                    compassRightAccent?.DeleteMessage();
                    compassCenterPointer?.DeleteMessage();
                    compassCenterBottomPip?.DeleteMessage();
                    ClearTapePool();
                    ClearSpritePool();
                    _compassVisibleWps.Clear();

                    zoneMsg?.DeleteMessage();
                    zoneDistMsg?.DeleteMessage();
                    zoneBg?.DeleteMessage();
                    zoneAccent?.DeleteMessage();

                    minimapHeaderBg?.DeleteMessage();
                    minimapHeaderAccent?.DeleteMessage();
                    minimapBg?.DeleteMessage();
                    minimapTerrain?.DeleteMessage();
                    minimapPlayerDot?.DeleteMessage();
                    minimapLabel?.DeleteMessage();
                    radarGrid?.DeleteMessage();
                    radarFovLeft?.DeleteMessage();
                    radarFovRight?.DeleteMessage();
                    ClearMinimapPool();

                    mapDimmer?.DeleteMessage();
                    mapFrame?.DeleteMessage();
                    mapTerrain?.DeleteMessage();
                    mapPlayerDot?.DeleteMessage();
                    mapHeaderBg?.DeleteMessage();
                    mapHeaderAccent?.DeleteMessage();
                    mapHeaderMsg?.DeleteMessage();
                    mapHeaderSubMsg?.DeleteMessage();
                    ClearFullMapPool();
                    _cachedMapClusters.Clear();

                    hudApi.Close();
                    hudApi = null;
                }
            }
        }

        private void ClearTapePool()
        {
            for (int i = 0; i < compassTickPool.Count; i++)
                compassTickPool[i]?.DeleteMessage();
            compassTickPool.Clear();

            for (int i = 0; i < compassTapePool.Count; i++)
                compassTapePool[i]?.DeleteMessage();
            compassTapePool.Clear();
        }

        private void ClearSpritePool()
        {
            for (int i = 0; i < waypointSpritePool.Count; i++)
                waypointSpritePool[i]?.DeleteMessage();
            waypointSpritePool.Clear();

            for (int i = 0; i < waypointDistPool.Count; i++)
                waypointDistPool[i]?.DeleteMessage();
            waypointDistPool.Clear();
        }

        private void ClearMinimapPool()
        {
            for (int i = 0; i < minimapMarkerPool.Count; i++)
                minimapMarkerPool[i]?.DeleteMessage();
            minimapMarkerPool.Clear();
        }

        private void ClearFullMapPool()
        {
            for (int i = 0; i < fullMapMarkerPool.Count; i++)
                fullMapMarkerPool[i]?.DeleteMessage();
            fullMapMarkerPool.Clear();

            for (int i = 0; i < fullMapLabelPool.Count; i++)
                fullMapLabelPool[i]?.DeleteMessage();
            fullMapLabelPool.Clear();
        }

        /// <summary>
        /// Gets the current monitor aspect ratio (Width / Height) so 2D billboards render un-squished.
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
                    cachedFullMapWidth = 1.40f;
                    cachedFullMapHeight = (cachedFullMapWidth * 0.5f) * cachedAspect;
                }
                return cachedAspect;
            }
            return cachedAspect;
        }

        private void OnHudApiRegistered()
        {
            try
            {
                float aspect = GetScreenAspect();

                // 1. Custom Programmatic Tactical HUD Compass Frame
                float baseHeight = 0.076f;
                Vector2D compassOrigin = new Vector2D(0.0, COMPASS_TOP_Y - baseHeight * 0.5);

                compassBg = new HudAPIv2.BillBoardHUDMessage(
                    Material: MATERIAL_SQUARE,
                    Origin: compassOrigin,
                    BillBoardColor: new Color(10, 16, 24, 240), // Matches minimapBg and zoneBg card background
                    Offset: Vector2D.Zero,
                    TimeToLive: -1,
                    Scale: 1.0,
                    Width: 0.62f,
                    Height: baseHeight,
                    HideHud: true,
                    Shadowing: true,
                    Blend: BlendTypeEnum.PostPP
                );
                compassBg.Visible = false;

                // Simple vertical accent bars on both left and right ends (matches zone status card accent)
                float accentWidth = 0.005f;
                float accentHeight = baseHeight - 0.006f;

                compassLeftAccent = new HudAPIv2.BillBoardHUDMessage(
                    Material: MATERIAL_SQUARE,
                    Origin: compassOrigin,
                    BillBoardColor: Color.LimeGreen,
                    Offset: new Vector2D(-0.31f + 0.005f, 0.0),
                    TimeToLive: -1,
                    Scale: 1.0,
                    Width: accentWidth,
                    Height: accentHeight,
                    HideHud: true,
                    Shadowing: false,
                    Blend: BlendTypeEnum.PostPP
                );
                compassLeftAccent.Visible = false;

                compassRightAccent = new HudAPIv2.BillBoardHUDMessage(
                    Material: MATERIAL_SQUARE,
                    Origin: compassOrigin,
                    BillBoardColor: Color.LimeGreen,
                    Offset: new Vector2D(0.31f - 0.005f, 0.0),
                    TimeToLive: -1,
                    Scale: 1.0,
                    Width: accentWidth,
                    Height: accentHeight,
                    HideHud: true,
                    Shadowing: false,
                    Blend: BlendTypeEnum.PostPP
                );
                compassRightAccent.Visible = false;

                // Precision Center Reticle: Top pointer (▼), bottom pip (▲), and central vertical lubber tick
                compassCenterPointer = new HudAPIv2.BillBoardHUDMessage(
                    Material: MATERIAL_NAV_ARROW,
                    Origin: compassOrigin,
                    BillBoardColor: new Color(255, 215, 60, 255),
                    Offset: new Vector2D(0.0, baseHeight * 0.5),
                    TimeToLive: -1,
                    Scale: 1.0,
                    Width: 0.010f,
                    Height: 0.010f * aspect,
                    HideHud: true,
                    Shadowing: false,
                    Blend: BlendTypeEnum.PostPP
                );
                compassCenterPointer.Rotation = (float)Math.PI; // Rotated downward pointing at tape center
                compassCenterPointer.Visible = false;

                compassCenterBottomPip = new HudAPIv2.BillBoardHUDMessage(
                    Material: MATERIAL_NAV_ARROW,
                    Origin: compassOrigin,
                    BillBoardColor: new Color(255, 215, 60, 255),
                    Offset: new Vector2D(0.0, -baseHeight * 0.5),
                    TimeToLive: -1,
                    Scale: 1.0,
                    Width: 0.008f,
                    Height: 0.008f * aspect,
                    HideHud: true,
                    Shadowing: false,
                    Blend: BlendTypeEnum.PostPP
                );
                compassCenterBottomPip.Rotation = 0f; // Pointing upward into tape center
                compassCenterBottomPip.Visible = false;

                // Pre-allocate 25 Graduation Tick BillBoards
                for (int i = 0; i < 25; i++)
                {
                    var tick = new HudAPIv2.BillBoardHUDMessage(
                        Material: MATERIAL_SQUARE,
                        Origin: Vector2D.Zero,
                        BillBoardColor: Color.White,
                        Offset: Vector2D.Zero,
                        TimeToLive: -1,
                        Scale: 1.0,
                        Width: 0.0016f,
                        Height: 0.010f,
                        HideHud: true,
                        Shadowing: false,
                        Blend: BlendTypeEnum.PostPP
                    );
                    tick.Visible = false;
                    compassTickPool.Add(tick);
                }

                // Pre-allocate 30 Tape Label HUDMessages (for dual-row top/bottom labels)
                for (int i = 0; i < 30; i++)
                {
                    var msg = new HudAPIv2.HUDMessage(
                        Message: new StringBuilder(""),
                        Origin: Vector2D.Zero,
                        Offset: Vector2D.Zero,
                        TimeToLive: -1,
                        Scale: 0.78,
                        HideHud: true,
                        Shadowing: true,
                        ShadowColor: Color.Black,
                        Blend: BlendTypeEnum.PostPP
                    );
                    msg.Visible = false;
                    compassTapePool.Add(msg);
                }

                // Pre-allocate 16 Marker Sprite BillBoards (Aspect-corrected 1:1 square: Width * aspect = Height)
                float iconWidth = 0.015f;
                float iconHeight = iconWidth * aspect;

                for (int i = 0; i < 16; i++)
                {
                    var sprite = new HudAPIv2.BillBoardHUDMessage(
                        Material: MATERIAL_MARKER_GPS,
                        Origin: Vector2D.Zero,
                        BillBoardColor: Color.White,
                        Offset: Vector2D.Zero,
                        TimeToLive: -1,
                        Scale: 1.0,
                        Width: iconWidth,
                        Height: iconHeight,
                        HideHud: true,
                        Shadowing: true,
                        Blend: BlendTypeEnum.PostPP
                    );
                    sprite.Visible = false;
                    waypointSpritePool.Add(sprite);

                    var distMsg = new HudAPIv2.HUDMessage(
                        Message: new StringBuilder(""),
                        Origin: Vector2D.Zero,
                        Offset: Vector2D.Zero,
                        TimeToLive: -1,
                        Scale: 0.68,
                        HideHud: true,
                        Shadowing: true,
                        ShadowColor: Color.Black,
                        Blend: BlendTypeEnum.PostPP
                    );
                    distMsg.Visible = false;
                    waypointDistPool.Add(distMsg);
                }
                ApplyCompassScale(compassScale);

                // 2. Corner Minimap (Aspect-corrected true 2:1 image ratio for KharakMap.dds, enlarged +20%)
                float mWidth = 0.312f;
                float mHeight = (mWidth * 0.5f) * aspect;
                minimapSize = new Vector2D(mWidth, mHeight);
                minimapPosition = new Vector2D(0.81, 0.95 - mHeight * 0.5 - 0.045);

                float minimapBgWidth = (float)minimapSize.X + 0.012f;
                float minimapBgHeight = (float)minimapSize.Y + 0.006f * aspect;

                // Minimap Header Card (Docked seamlessly directly above minimapBg)
                float minimapHeaderWidth = minimapBgWidth;
                float minimapHeaderHeight = 0.026f;
                float minimapBgTop = (float)(minimapPosition.Y + minimapBgHeight * 0.5f);
                float minimapHeaderCenterY = minimapBgTop + 0.003f + (minimapHeaderHeight * 0.5f);
                Vector2D minimapHeaderPos = new Vector2D(minimapPosition.X, minimapHeaderCenterY);

                minimapHeaderBg = new HudAPIv2.BillBoardHUDMessage(
                    Material: MATERIAL_SQUARE,
                    Origin: minimapHeaderPos,
                    BillBoardColor: new Color(10, 16, 24, 240),
                    Offset: Vector2D.Zero,
                    TimeToLive: -1,
                    Scale: 1.0,
                    Width: minimapHeaderWidth,
                    Height: minimapHeaderHeight,
                    HideHud: true,
                    Shadowing: true,
                    Blend: BlendTypeEnum.PostPP
                );
                minimapHeaderBg.Visible = false;

                minimapHeaderAccent = new HudAPIv2.BillBoardHUDMessage(
                    Material: MATERIAL_SQUARE,
                    Origin: minimapHeaderPos,
                    BillBoardColor: new Color(160, 170, 180, 240),
                    Offset: new Vector2D(-minimapHeaderWidth * 0.5f + 0.004f, 0.0),
                    TimeToLive: -1,
                    Scale: 1.0,
                    Width: 0.004f,
                    Height: minimapHeaderHeight - 0.004f,
                    HideHud: true,
                    Shadowing: false,
                    Blend: BlendTypeEnum.PostPP
                );
                minimapHeaderAccent.Visible = false;

                minimapBg = new HudAPIv2.BillBoardHUDMessage(
                    Material: MATERIAL_SQUARE,
                    Origin: minimapPosition,
                    BillBoardColor: new Color(10, 16, 24, 240),
                    Offset: Vector2D.Zero,
                    TimeToLive: -1,
                    Scale: 1.0,
                    Width: minimapBgWidth,
                    Height: minimapBgHeight,
                    HideHud: true,
                    Shadowing: true,
                    Blend: BlendTypeEnum.PostPP
                );
                minimapBg.Visible = false;

                minimapTerrain = new HudAPIv2.BillBoardHUDMessage(
                    Material: MATERIAL_MAP,
                    Origin: minimapPosition,
                    BillBoardColor: Color.White,
                    Offset: Vector2D.Zero,
                    TimeToLive: -1,
                    Scale: 1.0,
                    Width: (float)minimapSize.X,
                    Height: (float)minimapSize.Y,
                    HideHud: true,
                    Shadowing: false,
                    Blend: BlendTypeEnum.PostPP
                );
                minimapTerrain.Visible = false;

                float radarGridDiameter = (float)minimapSize.Y * 0.95f;
                radarGrid = new HudAPIv2.BillBoardHUDMessage(
                    Material: MATERIAL_RADAR_GRID,
                    Origin: minimapPosition,
                    BillBoardColor: new Color(100, 220, 255, 200),
                    Offset: Vector2D.Zero,
                    TimeToLive: -1,
                    Scale: 1.0,
                    Width: radarGridDiameter / aspect,
                    Height: radarGridDiameter,
                    HideHud: true,
                    Shadowing: false,
                    Blend: BlendTypeEnum.PostPP
                );
                radarGrid.Visible = false;

                radarFovLeft = new HudAPIv2.BillBoardHUDMessage(
                    Material: MATERIAL_SQUARE,
                    Origin: minimapPosition,
                    BillBoardColor: new Color(100, 220, 255, 140),
                    Offset: Vector2D.Zero,
                    TimeToLive: -1,
                    Scale: 1.0,
                    Width: 0.0018f,
                    Height: 0.1f,
                    HideHud: true,
                    Shadowing: false,
                    Blend: BlendTypeEnum.PostPP
                );
                radarFovLeft.Visible = false;

                radarFovRight = new HudAPIv2.BillBoardHUDMessage(
                    Material: MATERIAL_SQUARE,
                    Origin: minimapPosition,
                    BillBoardColor: new Color(100, 220, 255, 140),
                    Offset: Vector2D.Zero,
                    TimeToLive: -1,
                    Scale: 1.0,
                    Width: 0.0018f,
                    Height: 0.1f,
                    HideHud: true,
                    Shadowing: false,
                    Blend: BlendTypeEnum.PostPP
                );
                radarFovRight.Visible = false;

                minimapLabel = new HudAPIv2.HUDMessage(
                    Message: new StringBuilder("<color=255,255,255>TACTICAL RADAR"),
                    Origin: new Vector2D(minimapPosition.X - minimapHeaderWidth * 0.5f + 0.014f, minimapHeaderCenterY + 0.007f),
                    Offset: Vector2D.Zero,
                    TimeToLive: -1,
                    Scale: 0.60,
                    HideHud: true,
                    Shadowing: true,
                    ShadowColor: Color.Black
                );
                minimapLabel.Visible = false;

                // Pre-allocate 30 Minimap Marker Billboards (+20% scale: 0.012f, Zero GC allocations)
                for (int i = 0; i < 30; i++)
                {
                    var mDot = new HudAPIv2.BillBoardHUDMessage(
                        Material: MATERIAL_MARKER_GPS,
                        Origin: minimapPosition,
                        BillBoardColor: Color.White,
                        Offset: Vector2D.Zero,
                        TimeToLive: -1,
                        Scale: 1.0,
                        Width: 0.012f,
                        Height: 0.012f * aspect,
                        HideHud: true,
                        Shadowing: false,
                        Blend: BlendTypeEnum.PostPP
                    );
                    mDot.Visible = false;
                    minimapMarkerPool.Add(mDot);
                }

                // Player arrow registered on very top of stack (High-visibility Electric Gold: 0.017f)
                minimapPlayerDot = new HudAPIv2.BillBoardHUDMessage(
                    Material: MATERIAL_NAV_ARROW,
                    Origin: minimapPosition,
                    BillBoardColor: new Color(255, 230, 40, 255),
                    Offset: Vector2D.Zero,
                    TimeToLive: -1,
                    Scale: 1.0,
                    Width: 0.017f,
                    Height: 0.017f * aspect,
                    HideHud: true,
                    Shadowing: false,
                    Blend: BlendTypeEnum.PostPP
                );
                minimapPlayerDot.Visible = false;
                ApplyMinimapScale(minimapScale);

                // 3. Zone Status & Telemetry Panel (Docked seamlessly below Corner Minimap in Top-Right)
                float zoneWidth = (float)minimapSize.X + 0.012f;
                float zoneHeight = 0.056f;
                float minimapBgBottom = (float)(minimapPosition.Y - ((float)minimapSize.Y + 0.006f * aspect) * 0.5f);
                float zoneCenterY = minimapBgBottom - 0.004f - (zoneHeight * 0.5f);
                zonePosition = new Vector2D(minimapPosition.X, zoneCenterY);

                zoneBg = new HudAPIv2.BillBoardHUDMessage(
                    Material: MATERIAL_SQUARE,
                    Origin: zonePosition,
                    BillBoardColor: new Color(10, 16, 24, 240),
                    Offset: Vector2D.Zero,
                    TimeToLive: -1,
                    Scale: 1.0,
                    Width: zoneWidth,
                    Height: zoneHeight,
                    HideHud: true,
                    Shadowing: true,
                    Blend: BlendTypeEnum.PostPP
                );
                zoneBg.Visible = false;

                zoneAccent = new HudAPIv2.BillBoardHUDMessage(
                    Material: MATERIAL_SQUARE,
                    Origin: zonePosition,
                    BillBoardColor: Color.LimeGreen,
                    Offset: new Vector2D(-zoneWidth * 0.5f + 0.005f, 0.0),
                    TimeToLive: -1,
                    Scale: 1.0,
                    Width: 0.005f,
                    Height: zoneHeight - 0.006f,
                    HideHud: true,
                    Shadowing: false,
                    Blend: BlendTypeEnum.PostPP
                );
                zoneAccent.Visible = false;

                // Line 1: Zone Classification Header
                zoneMsg = new HudAPIv2.HUDMessage(
                    Message: zoneText,
                    Origin: new Vector2D(zonePosition.X - zoneWidth * 0.5f + 0.016f, zonePosition.Y + 0.013f),
                    Offset: Vector2D.Zero,
                    TimeToLive: -1,
                    Scale: 0.60,
                    HideHud: true,
                    Shadowing: true,
                    ShadowColor: Color.Black,
                    Blend: BlendTypeEnum.PostPP
                );
                zoneMsg.Visible = false;

                // Line 2: Distance & Border Countdown Telemetry
                zoneDistMsg = new HudAPIv2.HUDMessage(
                    Message: zoneDistText,
                    Origin: new Vector2D(zonePosition.X - zoneWidth * 0.5f + 0.016f, zonePosition.Y - 0.013f),
                    Offset: Vector2D.Zero,
                    TimeToLive: -1,
                    Scale: 0.50,
                    HideHud: true,
                    Shadowing: true,
                    ShadowColor: Color.Black,
                    Blend: BlendTypeEnum.PostPP
                );
                zoneDistMsg.Visible = false;

                // 4. Interactive Full-Screen Satellite Map ([M] Key, true 2:1 ratio)
                mapDimmer = new HudAPIv2.BillBoardHUDMessage(
                    Material: MATERIAL_SQUARE,
                    Origin: Vector2D.Zero,
                    BillBoardColor: new Color(0, 0, 0, 190),
                    Offset: Vector2D.Zero,
                    TimeToLive: -1,
                    Scale: 1.0,
                    Width: 2.0f,
                    Height: 2.0f,
                    HideHud: false,
                    Blend: BlendTypeEnum.PostPP
                );
                mapDimmer.Visible = false;

                float fullMapWidth = 1.40f;
                float fullMapHeight = (fullMapWidth * 0.5f) * aspect;

                mapFrame = new HudAPIv2.BillBoardHUDMessage(
                    Material: MATERIAL_SQUARE,
                    Origin: Vector2D.Zero,
                    BillBoardColor: new Color(15, 22, 30, 245),
                    Offset: Vector2D.Zero,
                    TimeToLive: -1,
                    Scale: 1.0,
                    Width: fullMapWidth + 0.02f,
                    Height: fullMapHeight + 0.01f * aspect,
                    HideHud: false,
                    Shadowing: true,
                    Blend: BlendTypeEnum.PostPP
                );
                mapFrame.Visible = false;

                mapTerrain = new HudAPIv2.BillBoardHUDMessage(
                    Material: MATERIAL_MAP,
                    Origin: Vector2D.Zero,
                    BillBoardColor: Color.White,
                    Offset: Vector2D.Zero,
                    TimeToLive: -1,
                    Scale: 1.0,
                    Width: fullMapWidth,
                    Height: fullMapHeight,
                    HideHud: false,
                    Blend: BlendTypeEnum.PostPP
                );
                mapTerrain.Visible = false;



                float mapHeaderWidth = fullMapWidth + 0.02f;
                float mapHeaderHeight = 0.056f;
                float topOfFrame = (fullMapHeight + 0.01f * aspect) * 0.5f;
                float mapHeaderCenterY = topOfFrame + 0.004f + (mapHeaderHeight * 0.5f);

                mapHeaderBg = new HudAPIv2.BillBoardHUDMessage(
                    Material: MATERIAL_SQUARE,
                    Origin: new Vector2D(0.0, mapHeaderCenterY),
                    BillBoardColor: new Color(10, 16, 24, 240),
                    Offset: Vector2D.Zero,
                    TimeToLive: -1,
                    Scale: 1.0,
                    Width: mapHeaderWidth,
                    Height: mapHeaderHeight,
                    HideHud: false,
                    Shadowing: true,
                    Blend: BlendTypeEnum.PostPP
                );
                mapHeaderBg.Visible = false;

                mapHeaderAccent = new HudAPIv2.BillBoardHUDMessage(
                    Material: MATERIAL_SQUARE,
                    Origin: new Vector2D(0.0, mapHeaderCenterY),
                    BillBoardColor: Color.LimeGreen,
                    Offset: new Vector2D(-mapHeaderWidth * 0.5f + 0.005f, 0.0),
                    TimeToLive: -1,
                    Scale: 1.0,
                    Width: 0.005f,
                    Height: mapHeaderHeight - 0.006f,
                    HideHud: false,
                    Shadowing: false,
                    Blend: BlendTypeEnum.PostPP
                );
                mapHeaderAccent.Visible = false;

                mapHeaderMsg = new HudAPIv2.HUDMessage(
                    Message: mapHeaderText,
                    Origin: new Vector2D(-mapHeaderWidth * 0.5f + 0.016f, mapHeaderCenterY + 0.013f),
                    Offset: Vector2D.Zero,
                    TimeToLive: -1,
                    Scale: 0.62,
                    HideHud: false,
                    Shadowing: true,
                    ShadowColor: Color.Black
                );
                mapHeaderMsg.Visible = false;

                mapHeaderSubMsg = new HudAPIv2.HUDMessage(
                    Message: mapHeaderSubText,
                    Origin: new Vector2D(-mapHeaderWidth * 0.5f + 0.016f, mapHeaderCenterY - 0.011f),
                    Offset: Vector2D.Zero,
                    TimeToLive: -1,
                    Scale: 0.50,
                    HideHud: false,
                    Shadowing: true,
                    ShadowColor: Color.Black
                );
                mapHeaderSubMsg.Visible = false;

                // Pre-allocate 50 Full Map Marker Billboards & Labels (+50% scale: 0.0225f, Zero GC allocations)
                for (int i = 0; i < 50; i++)
                {
                    var sprite = new HudAPIv2.BillBoardHUDMessage(
                        Material: MATERIAL_MARKER_GPS,
                        Origin: Vector2D.Zero,
                        BillBoardColor: Color.White,
                        Offset: Vector2D.Zero,
                        TimeToLive: -1,
                        Scale: 1.0,
                        Width: 0.0225f,
                        Height: 0.0225f * aspect,
                        HideHud: false,
                        Blend: BlendTypeEnum.PostPP
                    );
                    sprite.Visible = false;
                    fullMapMarkerPool.Add(sprite);

                    var label = new HudAPIv2.HUDMessage(
                        Message: new StringBuilder(""),
                        Origin: Vector2D.Zero,
                        Offset: Vector2D.Zero,
                        TimeToLive: -1,
                        Scale: 0.60,
                        HideHud: false,
                        Shadowing: true,
                        ShadowColor: Color.Black
                    );
                    label.Visible = false;
                    fullMapLabelPool.Add(label);
                }

                // Player arrow registered on very top of stack (High-visibility Electric Gold: 0.018f)
                mapPlayerDot = new HudAPIv2.BillBoardHUDMessage(
                    Material: MATERIAL_NAV_ARROW,
                    Origin: Vector2D.Zero,
                    BillBoardColor: new Color(255, 230, 40, 255),
                    Offset: Vector2D.Zero,
                    TimeToLive: -1,
                    Scale: 1.0,
                    Width: 0.018f,
                    Height: 0.018f * aspect,
                    HideHud: false,
                    Shadowing: false,
                    Blend: BlendTypeEnum.PostPP
                );
                mapPlayerDot.Visible = false;

                // 5. Register TextHUDAPI Mod Menu
                var rootCategory = new HudAPIv2.MenuRootCategory("GVK Navigation Suite", HudAPIv2.MenuRootCategory.MenuFlag.PlayerMenu, "GVK Navigation & Map Settings");
                new HudAPIv2.MenuItem("Toggle Tactical Map (Key: M)", rootCategory, () => { ToggleFullMap(); });
                new HudAPIv2.MenuItem("Toggle Corner Minimap", rootCategory, () => { ToggleMinimap(); });
                new HudAPIv2.MenuItem("Toggle Minimap Mode (Map / Radar)", rootCategory, () => { ToggleMinimapMode(); });
                new HudAPIv2.MenuItem("Cycle Minimap Size (75% / 100% / 125% / 150%)", rootCategory, () => { CycleMinimapScale(); });
                new HudAPIv2.MenuItem("Cycle Radar Range (1.5k / 3k / 5k / Log 30k)", rootCategory, () => { CycleRadarRange(); });
                new HudAPIv2.MenuItem("Toggle Compass Tape", rootCategory, () => { ToggleCompass(); });
                new HudAPIv2.MenuItem("Cycle Compass Size (75% / 100% / 125% / 150%)", rootCategory, () => { CycleCompassScale(); });
                new HudAPIv2.MenuItem("Toggle Zone Status Bar", rootCategory, () => { ToggleZoneBar(); });
                new HudAPIv2.MenuItem("Cycle HUD Refresh Rate (12Hz / 15Hz / 30Hz / 60Hz / 6Hz / 10Hz)", rootCategory, () => { CycleUpdateTickRate(); });

                var rateCategory = new HudAPIv2.MenuSubCategory("HUD Refresh Rate Presets", rootCategory, "Select HUD Update Frequency");
                new HudAPIv2.MenuItem("5 Ticks (12 Hz) - Recommended", rateCategory, () => { SetUpdateTickRate(5); });
                new HudAPIv2.MenuItem("6 Ticks (10 Hz) - Balanced", rateCategory, () => { SetUpdateTickRate(6); });
                new HudAPIv2.MenuItem("4 Ticks (15 Hz) - Ultra Smooth", rateCategory, () => { SetUpdateTickRate(4); });
                new HudAPIv2.MenuItem("2 Ticks (30 Hz) - Half Framerate", rateCategory, () => { SetUpdateTickRate(2); });
                new HudAPIv2.MenuItem("1 Tick (60 Hz) - Uncapped", rateCategory, () => { SetUpdateTickRate(1); });
                new HudAPIv2.MenuItem("10 Ticks (6 Hz) - Battery / Sim Saver", rateCategory, () => { SetUpdateTickRate(10); });

                new HudAPIv2.MenuItem("Restore Default Kharak GPS Waypoints", rootCategory, () => { PopulateDefaultGps(true); });
                new HudAPIv2.MenuItem("Open Zone Advisory Mission Screen", rootCategory, () => { OpenZoneMissionScreen(); });
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[GVK_ZoneNavCommand] Error in OnHudApiRegistered: {ex.Message}");
            }
        }

        public override void UpdateBeforeSimulation()
        {
            if (MyAPIGateway.Utilities.IsDedicated) return;

            // Handle [M] Keypress for Full Map
            if (!MyAPIGateway.Gui.ChatEntryVisible && !MyAPIGateway.Gui.IsCursorVisible)
            {
                if (MyAPIGateway.Input.IsNewKeyPressed(MyKeys.M))
                {
                    ToggleFullMap();
                }
            }

            if (showFullMap && MyAPIGateway.Input.IsNewKeyPressed(MyKeys.Escape))
            {
                ToggleFullMap();
            }

            Vector3D? pos = GetPlayerPosition();
            if (!pos.HasValue) return;

            if (!hasCheckedDefaultGps)
            {
                CheckAndPopulateDefaultGps();
            }

            tickCounter++;

            // 100-Tick (~1.67s) Background Scan: entity iteration, GPS list, zone distance.
            // Players don't cross zone boundaries fast enough to notice the extra latency.
            if (tickCounter % 100 == 0)
            {
                UpdateBackgroundData(pos.Value);
            }

            // Configurable HUD Cadence (default 5 ticks / 12 Hz):
            // Compass, Minimap, Zone Bar, and FullMap all update on the user-configured updateTickRate.
            // Saves significant per-frame CPU cycles and text formatting overhead while letting players
            // dial in their preferred smoothness vs performance balance via /nav rate or F2 Mod Menu.
            if (hudApi != null && hudApi.Heartbeat)
            {
                if (tickCounter % updateTickRate == 0 || _refreshCompassNextFrame)
                {
                    _refreshCompassNextFrame = false;
                    UpdateCompassAndWaypoints(pos.Value);
                }

                if (showFullMap)
                {
                    if (tickCounter % updateTickRate == 0 || _fullMapNeedsRedraw)
                    {
                        _fullMapNeedsRedraw = false;
                        UpdateFullMap(pos.Value);
                    }
                }
                else if (tickCounter % updateTickRate == 0 || _refreshMinimapNextFrame)
                {
                    _refreshMinimapNextFrame = false;
                    UpdateZoneBar();
                    UpdateMinimap(pos.Value);
                }
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
                    if (cachedGpsList[j].Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase))
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
        /// Scans all player GPS entries and in-range radio signals:
        /// 1. GPS coordinates: uses marker_gps and GPSColor.
        /// 2. Active broadcasting antennas / beacons: uses marker_friendly, marker_enemy, marker_neutral, marker_self.
        /// </summary>
        private void UpdateBackgroundData(Vector3D playerPos)
        {
            double distMeters = Vector3D.Distance(playerPos, CROSSROADS_BEACON);
            lastDistKm = distMeters / 1000.0;
            lastDistZ3Km = Vector3D.Distance(playerPos, Z3_CENTER) / 1000.0;

            if (distMeters <= ZONE_0_RADIUS)
            {
                currentZoneIndex = 0;
                lastRemainingKm = (ZONE_0_RADIUS - distMeters) / 1000.0;
            }
            else if (distMeters <= ZONE_1_RADIUS)
            {
                currentZoneIndex = 1;
                lastRemainingKm = (ZONE_1_RADIUS - distMeters) / 1000.0;
            }
            else if (distMeters <= ZONE_2_RADIUS)
            {
                currentZoneIndex = 2;
                lastRemainingKm = (ZONE_2_RADIUS - distMeters) / 1000.0;
            }
            else
            {
                currentZoneIndex = 3;
                lastRemainingKm = 0.0;
            }

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

                MyStringId markerSprite;
                Color signalColor;

                if (isOwner)
                {
                    markerSprite = MATERIAL_MARKER_SELF;
                    signalColor = new Color(100, 230, 255);
                }
                else if (relation == MyRelationsBetweenPlayerAndBlock.FactionShare || relation == MyRelationsBetweenPlayerAndBlock.Owner)
                {
                    markerSprite = MATERIAL_MARKER_FRIENDLY;
                    signalColor = new Color(100, 240, 100);
                }
                else if (relation == MyRelationsBetweenPlayerAndBlock.Enemies)
                {
                    markerSprite = MATERIAL_MARKER_ENEMY;
                    signalColor = new Color(255, 60, 60);
                }
                else
                {
                    markerSprite = MATERIAL_MARKER_NEUTRAL;
                    signalColor = isNpc ? new Color(240, 180, 50) : Color.White;
                }

                for (int bIdx = 0; bIdx < blockBuffer.Count; bIdx++)
                {
                    var fat = blockBuffer[bIdx].FatBlock;

                    var beacon = fat as IMyBeacon;
                    if (beacon != null && beacon.IsWorking && beacon.Enabled)
                    {
                        Vector3D blockPos = beacon.GetPosition();
                        double distSq = Vector3D.DistanceSquared(playerPos, blockPos);
                        double radius = beacon.Radius;

                        if (distSq <= radius * radius && distSq >= 1.0)
                        {
                            double blockDist = Math.Sqrt(distSq);
                            string bName = string.IsNullOrEmpty(beacon.CustomName) ? grid.CustomName : beacon.CustomName;
                            activeHudWaypoints.Add(new ActiveHudWaypoint
                            {
                                Name = bName,
                                Coords = blockPos,
                                Sprite = markerSprite,
                                DisplayColor = signalColor,
                                DistanceMeters = blockDist,
                                MapUV = WorldToMapUV(blockPos),
                                IsSignal = true,
                                PlanetaryElevation = Vector3D.Distance(blockPos, PLANET_CENTER) - PLANET_RADIUS
                            });
                            break;
                        }
                    }

                    var antenna = fat as IMyRadioAntenna;
                    if (antenna != null && antenna.IsWorking && antenna.Enabled && antenna.EnableBroadcasting)
                    {
                        Vector3D blockPos = antenna.GetPosition();
                        double distSq = Vector3D.DistanceSquared(playerPos, blockPos);
                        double radius = antenna.Radius;

                        if (distSq <= radius * radius && distSq >= 1.0)
                        {
                            double blockDist = Math.Sqrt(distSq);
                            string aName = string.IsNullOrEmpty(antenna.CustomName) ? grid.CustomName : antenna.CustomName;
                            activeHudWaypoints.Add(new ActiveHudWaypoint
                            {
                                Name = aName,
                                Coords = blockPos,
                                Sprite = markerSprite,
                                DisplayColor = signalColor,
                                DistanceMeters = blockDist,
                                MapUV = WorldToMapUV(blockPos),
                                IsSignal = true,
                                PlanetaryElevation = Vector3D.Distance(blockPos, PLANET_CENTER) - PLANET_RADIUS
                            });
                            break;
                        }
                    }
                }
            }

            // Sort by distance using cached IComparer struct — zero allocation, whitelisted by SE ModAPI.
            activeHudWaypoints.Sort(WaypointComparer);

            // Flag full satellite map for redraw on next frame if currently open
            if (showFullMap)
                _fullMapNeedsRedraw = true;
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

            if (compassBg == null || !showCompass || showFullMap)
            {
                if (_compassElementsVisible)
                {
                    _compassElementsVisible = false;
                    if (compassBg != null) compassBg.Visible = false;
                    if (compassLeftAccent != null) compassLeftAccent.Visible = false;
                    if (compassRightAccent != null) compassRightAccent.Visible = false;
                    if (compassCenterPointer != null) compassCenterPointer.Visible = false;
                    if (compassCenterBottomPip != null) compassCenterBottomPip.Visible = false;
                    HideTapePool();
                    HideSpritePool();
                }
                return;
            }

            _compassElementsVisible = true;
            if (compassBg != null) compassBg.Visible = true;
            Color zoneCol = GetZoneColor(currentZoneIndex);
            if (compassLeftAccent != null)
            {
                compassLeftAccent.BillBoardColor = zoneCol;
                compassLeftAccent.Visible = true;
            }
            if (compassRightAccent != null)
            {
                compassRightAccent.BillBoardColor = zoneCol;
                compassRightAccent.Visible = true;
            }
            if (compassCenterPointer != null) compassCenterPointer.Visible = true;
            if (compassCenterBottomPip != null) compassCenterBottomPip.Visible = true;

            // compass is already clamped to [0, 2π] above. Normalize to [-1, 1] for tape rendering.
            compass = (compass - (float)Math.PI) / (float)Math.PI;

            float FOV = camera.FovWithZoom;
            // Precompute FOV polynomial once per frame instead of calling Math.Pow in hot loops
            float fovCoeff = FOV * (5.596f * FOV * FOV - 18.43f * FOV + 16.16f);
            float fovCubic = FOV * 12f;
            float baseWidth = 0.54f + 0.08f * compassScale;
            float tapeSpan = (baseWidth * 0.5f) - (0.012f * compassScale);
            float aspect = GetScreenAspect();

            float baseHeight = 0.076f * compassScale;
            double topY = COMPASS_TOP_Y;
            double centerY = topY - baseHeight * 0.5;
            double bottomY = topY - baseHeight;

            float tickWidth = 0.0016f * compassScale;
            float majorTickH = 0.007f * compassScale;
            float mediumTickH = 0.005f * compassScale;
            float minorTickH = 0.003f * compassScale;

            // Dual row text layout:
            // Top Row: Cardinals & Ordinals (N, NE, E, SE, S, SW, W, NW)
            float topLabelY = (float)(topY - 0.008f * compassScale);
            // Bottom Row: Major numeric bearing marks (0, 15, 30, 45, 60, etc.) lowered so they never overlap letters
            float bottomLabelY = (float)(topY - 0.027f * compassScale);

            // 2. Render Tactical Graduation Ticks and Dual-Row Labels
            int tickIndex = 0;
            int tapeMsgIndex = 0;

            for (int i = 0; i < COMPASS_GRADUATIONS.Length; i++)
            {
                var grad = COMPASS_GRADUATIONS[i];
                float offset = compass + grad.Offset;
                if (offset < -1f) offset += 2f;
                else if (offset > 1f) offset -= 2f;

                // Screen offset range is [-0.35, 0.35]. Skip polynomial math if offset is way outside the visible ribbon.
                if (Math.Abs(offset) > 0.35f) continue;

                float screenOffset = (fovCoeff * offset) + (fovCubic * offset * offset * offset);

                if (screenOffset > tapeSpan || screenOffset < -tapeSpan) continue;

                // Render graduation tick mark hanging down from the top rail
                if (tickIndex < compassTickPool.Count)
                {
                    var tick = compassTickPool[tickIndex++];
                    float tHeight;
                    Color tColor;

                    switch (grad.Type)
                    {
                        case GraduationType.Cardinal:
                            tHeight = majorTickH;
                            tColor = new Color(255, 215, 60, 240); // Kharak tactical amber
                            break;
                        case GraduationType.Ordinal:
                        case GraduationType.MajorMark:
                            tHeight = majorTickH;
                            tColor = new Color(255, 255, 255, 240); // Crisp white
                            break;
                        case GraduationType.MediumTen:
                            tHeight = mediumTickH;
                            tColor = new Color(255, 255, 255, 200); // Clean white
                            break;
                        default:
                            tHeight = minorTickH;
                            tColor = new Color(255, 255, 255, 140); // Subtle white
                            break;
                    }

                    tick.Origin = new Vector2D(screenOffset, topY - tHeight * 0.5f);
                    tick.Width = tickWidth;
                    tick.Height = tHeight;
                    tick.BillBoardColor = tColor;
                    tick.Visible = true;
                }

                // Top Row: Cardinals (N, E, S, W) and Ordinals (NE, SE, SW, NW)
                if (grad.TopLabel != null && tapeMsgIndex < compassTapePool.Count)
                {
                    var msg = compassTapePool[tapeMsgIndex++];
                    msg.Message.Clear();
                    msg.Scale = 0.58 * compassScale;

                    if (grad.Type == GraduationType.Cardinal)
                        msg.Message.Append("<color=255,215,60>").Append(grad.TopLabel);
                    else
                        msg.Message.Append("<color=255,255,255>").Append(grad.TopLabel);

                    msg.Origin = new Vector2D(screenOffset, topLabelY);

                    double baseHalfWidth = grad.BaseTopHalfWidth;
                    if (baseHalfWidth < 0)
                    {
                        var charLen = msg.GetTextLength();
                        baseHalfWidth = (charLen.X * 0.5) / compassScale;
                        COMPASS_GRADUATIONS[i].BaseTopHalfWidth = baseHalfWidth;
                    }

                    msg.Offset = new Vector2D(-baseHalfWidth * compassScale, 0.0);
                    msg.Visible = true;
                }

                // Bottom Row: Major numeric bearing marks (0, 15, 30, 45, 60, etc.)
                if (grad.BottomLabel != null && tapeMsgIndex < compassTapePool.Count)
                {
                    var msg = compassTapePool[tapeMsgIndex++];
                    msg.Message.Clear();
                    msg.Scale = 0.48 * compassScale;

                    if (grad.Type == GraduationType.Cardinal)
                        msg.Message.Append("<color=255,225,120>").Append(grad.BottomLabel);
                    else
                        msg.Message.Append("<color=255,255,255>").Append(grad.BottomLabel);

                    msg.Origin = new Vector2D(screenOffset, bottomLabelY);

                    double baseHalfWidth = grad.BaseBottomHalfWidth;
                    if (baseHalfWidth < 0)
                    {
                        var charLen = msg.GetTextLength();
                        baseHalfWidth = (charLen.X * 0.5) / compassScale;
                        COMPASS_GRADUATIONS[i].BaseBottomHalfWidth = baseHalfWidth;
                    }

                    msg.Offset = new Vector2D(-baseHalfWidth * compassScale, 0.0);
                    msg.Visible = true;
                }
            }

            for (int i = tickIndex; i < compassTickPool.Count; i++)
                compassTickPool[i].Visible = false;

            for (int i = tapeMsgIndex; i < compassTapePool.Count; i++)
                compassTapePool[i].Visible = false;

            // 3. Render Graphical HUD Waypoints (Painter's Algorithm: Distant waypoints render underneath, Closer waypoints render on top)
            _compassVisibleWps.Clear();
            double playerElevation = Vector3D.Distance(playerPos, PLANET_CENTER) - PLANET_RADIUS;

            // Pass 1: Collect up to waypointSpritePool.Count visible waypoints from activeHudWaypoints (which is pre-sorted closest-first).
            for (int i = 0; i < activeHudWaypoints.Count && _compassVisibleWps.Count < waypointSpritePool.Count; i++)
            {
                var wp = activeHudWaypoints[i];

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
                }
            }

            // Pass 2: Render in reverse order (from farthest down to closest) so closer waypoints are assigned higher
            // TextHUDAPI pool indices and cleanly draw ON TOP of further waypoints.
            int spriteIndex = 0;
            for (int j = _compassVisibleWps.Count - 1; j >= 0; j--)
            {
                var vis = _compassVisibleWps[j];
                var wp = activeHudWaypoints[vis.WpIndex];
                float poiScreenOffset = vis.ScreenOffset;

                double distKm = wp.DistanceMeters * 0.001;
                Color wpColor = wp.DisplayColor;

                // Distance-based perspective scaling for compass tape:
                // 100m or closer: 100% full size
                // 30km (30,000m) or farther: 70% size
                // Smooth linear attenuation between 100m and 30,000m
                float distT = (float)MathHelper.Clamp((wp.DistanceMeters - 100.0) / (30000.0 - 100.0), 0.0, 1.0);
                float distFactor = 1.0f - 0.30f * distT;

                float wWidth = 0.011f * compassScale * distFactor;
                float wHeight = wWidth * aspect;

                double spriteCenterY = topY - 0.046 * compassScale;
                double spriteBottom = spriteCenterY - (wHeight * 0.5);
                double textY = spriteBottom - (0.002 * compassScale);

                var sprite = waypointSpritePool[spriteIndex];
                sprite.Material = GetWaypointMaterial(ref wp, playerElevation);
                sprite.Rotation = 0f;
                sprite.BillBoardColor = wpColor;
                sprite.Width = wWidth;
                sprite.Height = wHeight;
                sprite.Origin = new Vector2D(poiScreenOffset, spriteCenterY);
                sprite.Offset = Vector2D.Zero;
                sprite.Visible = true;

                // Zero-allocation distance text formatting: direct int/char appending avoids hundreds of heap string allocs per frame
                var dist = waypointDistPool[spriteIndex];
                dist.Scale = 0.46 * compassScale * distFactor;
                dist.Message.Clear()
                    .Append("<color=").Append(wpColor.R).Append(',').Append(wpColor.G).Append(',').Append(wpColor.B).Append('>');

                if (distKm < 10.0)
                {
                    int whole = (int)distKm;
                    int tenths = (int)((distKm - whole) * 10.0);
                    dist.Message.Append(whole).Append('.').Append(tenths).Append('k');
                }
                else
                {
                    dist.Message.Append((int)distKm).Append('k');
                }

                dist.Origin = new Vector2D(poiScreenOffset, textY);
                var distLen = dist.GetTextLength();
                dist.Offset = new Vector2D(-distLen.X * 0.5, 0.0);
                dist.Visible = true;
                spriteIndex++;
            }

            for (int i = spriteIndex; i < waypointSpritePool.Count; i++)
            {
                waypointSpritePool[i].Visible = false;
                waypointDistPool[i].Visible = false;
            }
        }

        private void HideTapePool()
        {
            for (int i = 0; i < compassTickPool.Count; i++)
                compassTickPool[i].Visible = false;

            for (int i = 0; i < compassTapePool.Count; i++)
                compassTapePool[i].Visible = false;
        }

        private void HideSpritePool()
        {
            for (int i = 0; i < waypointSpritePool.Count; i++)
            {
                waypointSpritePool[i].Visible = false;
                waypointDistPool[i].Visible = false;
            }
        }

        /// <summary>
        /// Updates the Zone Status Panel docked directly below the minimap in the top-right corner.
        /// Keeps the area below the compass ribbon clear for WeaponCore target lock and lead indicator HUDs.
        /// </summary>
        private void UpdateZoneBar()
        {
            if (zoneMsg == null || !showZoneBar || showFullMap)
            {
                if (_zoneBarElementsVisible)
                {
                    _zoneBarElementsVisible = false;
                    if (zoneBg != null) zoneBg.Visible = false;
                    if (zoneAccent != null) zoneAccent.Visible = false;
                    if (zoneMsg != null) zoneMsg.Visible = false;
                    if (zoneDistMsg != null) zoneDistMsg.Visible = false;
                }
                return;
            }
            _zoneBarElementsVisible = true;

            // Dynamically adjust position and width depending on minimap mode
            float aspect = GetScreenAspect();
            float radarGridDiameter = (float)minimapSize.Y * 0.95f;
            float radarBoxWidth = (radarGridDiameter / aspect) + 0.024f * minimapScale;
            float strategicBoxWidth = (float)minimapSize.X + 0.012f * minimapScale;
            float zoneWidth = (minimapMode == MinimapDisplayMode.TacticalRadar) ? radarBoxWidth : strategicBoxWidth;
            float zoneHeight = 0.056f * minimapScale;
            bool isCompact = (minimapMode == MinimapDisplayMode.TacticalRadar);
            Vector2D targetPos;
            if (showMinimap)
            {
                float minimapBgHeight = (float)minimapSize.Y + (0.008f * minimapScale) * aspect;
                float minimapBgBottom = (float)(minimapPosition.Y - minimapBgHeight * 0.5f);
                targetPos = new Vector2D(minimapPosition.X, minimapBgBottom - (0.005f * minimapScale) - (zoneHeight * 0.5f));
            }
            else
            {
                targetPos = new Vector2D(0.97 - (zoneWidth * 0.5), 0.92);
            }

            if (zonePosition != targetPos || Math.Abs(zoneBg.Width - zoneWidth) > 0.001f || Math.Abs(zoneBg.Height - zoneHeight) > 0.001f)
            {
                zonePosition = targetPos;
                zoneBg.Origin = zonePosition;
                zoneBg.Width = zoneWidth;
                zoneBg.Height = zoneHeight;
                zoneAccent.Origin = zonePosition;
                zoneAccent.Width = 0.005f * minimapScale;
                zoneAccent.Height = zoneHeight - 0.006f * minimapScale;
                zoneAccent.Offset = new Vector2D(-zoneWidth * 0.5f + 0.005f * minimapScale, 0.0);
                zoneMsg.Origin = new Vector2D(zonePosition.X - zoneWidth * 0.5f + 0.016f * minimapScale, zonePosition.Y + 0.018f * minimapScale);
                zoneMsg.Scale = (isCompact ? 0.54 : 0.60) * minimapScale;
                zoneDistMsg.Origin = new Vector2D(zonePosition.X - zoneWidth * 0.5f + 0.016f * minimapScale, zonePosition.Y - 0.008f * minimapScale);
                zoneDistMsg.Scale = (isCompact ? 0.46 : 0.50) * minimapScale;
            }

            // Dirty check: skip rebuilding StringBuilder if values haven't changed
            if (_lastZoneBarZoneIndex == currentZoneIndex &&
                _lastZoneBarMinimapMode == minimapMode &&
                Math.Abs(_lastZoneBarDistKm - lastDistKm) < 0.05 &&
                Math.Abs(_lastZoneBarRemainingKm - lastRemainingKm) < 0.05 &&
                Math.Abs(_lastZoneBarDistZ3Km - lastDistZ3Km) < 0.05)
            {
                zoneBg.Visible = true;
                zoneAccent.Visible = true;
                zoneMsg.Visible = true;
                zoneDistMsg.Visible = true;
                return;
            }

            _lastZoneBarZoneIndex = currentZoneIndex;
            _lastZoneBarMinimapMode = minimapMode;
            _lastZoneBarDistKm = lastDistKm;
            _lastZoneBarRemainingKm = lastRemainingKm;
            _lastZoneBarDistZ3Km = lastDistZ3Km;

            zoneText.Clear();
            zoneDistText.Clear();
            switch (currentZoneIndex)
            {
                case 0:
                    zoneAccent.BillBoardColor = Color.LimeGreen;
                    zoneText.Append("<color=50,255,100>[ ZONE 0: SAFE HUB ]");
                    if (isCompact)
                    {
                        zoneDistText.Append("<color=220,220,220>Tower: <color=255,255,255>")
                                    .Append(lastDistKm.ToString("F1")).Append("k <color=100,140,180>| <color=220,220,220>Next: <color=255,230,50>")
                                    .Append(lastRemainingKm.ToString("F1")).Append("k");
                    }
                    else
                    {
                        zoneDistText.Append("<color=220,220,220>Crossroads: <color=255,255,255>")
                                    .Append(lastDistKm.ToString("F1")).Append(" km <color=100,140,180>| <color=220,220,220>Z1 Border in: <color=255,230,50>")
                                    .Append(lastRemainingKm.ToString("F1")).Append(" km");
                    }
                    break;
                case 1:
                    zoneAccent.BillBoardColor = Color.Yellow;
                    zoneText.Append("<color=255,230,50>[ ZONE 1: PVE FRONTIER ]");
                    if (isCompact)
                    {
                        zoneDistText.Append("<color=220,220,220>Tower: <color=255,255,255>")
                                    .Append(lastDistKm.ToString("F1")).Append("k <color=100,140,180>| <color=220,220,220>PvP: <color=255,165,0>")
                                    .Append(lastRemainingKm.ToString("F1")).Append("k");
                    }
                    else
                    {
                        zoneDistText.Append("<color=220,220,220>Crossroads: <color=255,255,255>")
                                    .Append(lastDistKm.ToString("F1")).Append(" km <color=100,140,180>| <color=220,220,220>PvP Border in: <color=255,165,0>")
                                    .Append(lastRemainingKm.ToString("F1")).Append(" km");
                    }
                    break;
                case 2:
                    zoneAccent.BillBoardColor = Color.Orange;
                    zoneText.Append("<color=255,165,0>[ ZONE 2: CONTESTED (PVP) ]");
                    if (isCompact)
                    {
                        zoneDistText.Append("<color=220,220,220>Tower: <color=255,255,255>")
                                    .Append(lastDistKm.ToString("F1")).Append("k <color=100,140,180>| <color=220,220,220>Z3: <color=255,50,50>")
                                    .Append(lastRemainingKm.ToString("F1")).Append("k");
                    }
                    else
                    {
                        zoneDistText.Append("<color=220,220,220>Crossroads: <color=255,255,255>")
                                    .Append(lastDistKm.ToString("F1")).Append(" km <color=100,140,180>| <color=220,220,220>Z3 Border in: <color=255,50,50>")
                                    .Append(lastRemainingKm.ToString("F1")).Append(" km");
                    }
                    break;
                default:
                    zoneAccent.BillBoardColor = Color.Red;
                    zoneText.Append("<color=255,50,50>[ ZONE 3: GAALSIEN HEART ]");
                    if (isCompact)
                    {
                        zoneDistText.Append("<color=220,220,220>Tower: <color=255,255,255>")
                                    .Append(lastDistKm.ToString("F1")).Append("k <color=100,140,180>| <color=220,220,220>Core: <color=255,50,50>")
                                    .Append(lastDistZ3Km.ToString("F1")).Append("k");
                    }
                    else
                    {
                        zoneDistText.Append("<color=220,220,220>Crossroads: <color=255,255,255>")
                                    .Append(lastDistKm.ToString("F1")).Append(" km <color=100,140,180>| <color=220,220,220>Core Dist: <color=255,50,50>")
                                    .Append(lastDistZ3Km.ToString("F1")).Append(" km");
                    }
                    break;
            }

            zoneBg.Visible = true;
            zoneAccent.Visible = true;
            zoneMsg.Visible = true;
            zoneDistMsg.Visible = true;
        }

        /// <summary>
        /// Updates the corner minimap or tactical vector radar using pre-allocated billboard pool (Zero GC allocations).
        /// </summary>
        private void UpdateMinimap(Vector3D playerPos)
        {
            if (minimapTerrain == null || !showMinimap || showFullMap)
            {
                if (_minimapElementsVisible)
                {
                    _minimapElementsVisible = false;
                    if (minimapHeaderBg != null) minimapHeaderBg.Visible = false;
                    if (minimapHeaderAccent != null) minimapHeaderAccent.Visible = false;
                    if (minimapBg != null) minimapBg.Visible = false;
                    if (minimapTerrain != null) minimapTerrain.Visible = false;
                    if (radarGrid != null) radarGrid.Visible = false;
                    if (radarFovLeft != null) radarFovLeft.Visible = false;
                    if (radarFovRight != null) radarFovRight.Visible = false;
                    if (minimapPlayerDot != null) minimapPlayerDot.Visible = false;
                    if (minimapLabel != null) minimapLabel.Visible = false;
                    HideMinimapPool();
                }
                return;
            }
            _minimapElementsVisible = true;

            if (minimapHeaderBg != null) minimapHeaderBg.Visible = true;
            if (minimapHeaderAccent != null) minimapHeaderAccent.Visible = true;
            minimapBg.Visible = true;
            minimapLabel.Visible = true;

            float aspect = GetScreenAspect();
            float radarGridDiameter = (float)minimapSize.Y * 0.95f;
            float radarBoxWidth = (radarGridDiameter / aspect) + 0.024f * minimapScale;
            float strategicBoxWidth = (float)minimapSize.X + 0.012f * minimapScale;
            float minimapBgWidth = (minimapMode == MinimapDisplayMode.TacticalRadar) ? radarBoxWidth : strategicBoxWidth;
            float minimapBgHeight = (float)minimapSize.Y + (0.008f * minimapScale) * aspect;
            float minimapHeaderWidth = minimapBgWidth;
            float minimapHeaderHeight = 0.026f * minimapScale;
            float headerGap = 0.005f * minimapScale;

            // Ensure minimapPosition and minimapBg match mode width
            double targetPosX = 0.97 - (minimapBgWidth * 0.5);
            if (Math.Abs(minimapPosition.X - targetPosX) > 0.0005 || Math.Abs(minimapBg.Width - minimapBgWidth) > 0.001f)
            {
                ApplyMinimapScale(minimapScale);
            }

            float minimapBgTop = (float)(minimapPosition.Y + minimapBgHeight * 0.5f);
            float minimapHeaderCenterY = minimapBgTop + headerGap + (minimapHeaderHeight * 0.5f);
            Vector2D minimapHeaderPos = new Vector2D(minimapPosition.X, minimapHeaderCenterY);

            if (minimapHeaderBg != null && (minimapHeaderBg.Origin != minimapHeaderPos || Math.Abs(minimapHeaderBg.Height - minimapHeaderHeight) > 0.001f || Math.Abs(minimapHeaderBg.Width - minimapHeaderWidth) > 0.001f))
            {
                minimapHeaderBg.Origin = minimapHeaderPos;
                minimapHeaderBg.Width = minimapHeaderWidth;
                minimapHeaderBg.Height = minimapHeaderHeight;
                minimapHeaderAccent.Origin = minimapHeaderPos;
                minimapHeaderAccent.Width = 0.004f * minimapScale;
                minimapHeaderAccent.Height = minimapHeaderHeight - 0.004f * minimapScale;
                minimapHeaderAccent.Offset = new Vector2D(-minimapHeaderWidth * 0.5f + 0.004f * minimapScale, 0.0);
                minimapLabel.Origin = new Vector2D(minimapPosition.X - minimapHeaderWidth * 0.5f + 0.014f * minimapScale, minimapHeaderCenterY + 0.007f * minimapScale);
                minimapLabel.Scale = (minimapMode == MinimapDisplayMode.TacticalRadar ? 0.55 : 0.60) * minimapScale;
            }
            int mIdx = 0;

            if (minimapMode == MinimapDisplayMode.StrategicMap)
            {
                minimapTerrain.Visible = true;
                if (radarGrid != null) radarGrid.Visible = false;
                if (radarFovLeft != null) radarFovLeft.Visible = false;
                if (radarFovRight != null) radarFovRight.Visible = false;

                minimapLabel.Message.Clear();
                minimapLabel.Message.Append("<color=255,255,255>SECTOR MAP");

                // Map UV position of player relative to center of minimap box
                Vector2 uv = WorldToMapUV(playerPos);
                Vector2D dotOffset = new Vector2D(
                    (uv.X - 0.5) * minimapSize.X,
                    (0.5 - uv.Y) * minimapSize.Y
                );

                double playerElevation = Vector3D.Distance(playerPos, PLANET_CENTER) - PLANET_RADIUS;

                // Render all active waypoints using pre-allocated pool
                for (int i = 0; i < activeHudWaypoints.Count && mIdx < minimapMarkerPool.Count; i++)
                {
                    var wp = activeHudWaypoints[i];
                    Vector2 wpUV = wp.MapUV;
                    Vector2D wpOffset = new Vector2D(
                        (wpUV.X - 0.5) * minimapSize.X,
                        (0.5 - wpUV.Y) * minimapSize.Y
                    );

                    var icon = minimapMarkerPool[mIdx++];
                    icon.Material = GetWaypointMaterial(ref wp, playerElevation);
                    icon.Rotation = 0f;
                    float mSize = 0.012f * minimapScale;
                    icon.Width = mSize;
                    icon.Height = mSize * aspect;
                    icon.BillBoardColor = wp.DisplayColor;
                    icon.Offset = wpOffset;
                    icon.Visible = true;
                }

                // Update player arrow with gold pulse
                minimapPlayerDot.Offset = dotOffset;
                minimapPlayerDot.Rotation = -playerHeadingRad;
                minimapPlayerDot.BillBoardColor = (tickCounter % 40 < 20) ? new Color(255, 230, 40, 255) : new Color(255, 255, 140, 255);
                minimapPlayerDot.Visible = true;
            }
            else // MinimapDisplayMode.TacticalRadar
            {
                minimapTerrain.Visible = false;
                if (radarGrid != null) radarGrid.Visible = true;

                minimapLabel.Message.Clear();
                if (radarScale == RadarScaleMode.Logarithmic)
                {
                    minimapLabel.Message.Append("<color=255,255,255>RADAR (LOG: 30K)");
                }
                else
                {
                    int rangeKmInt = (int)(radarRangeMeters * 0.001);
                    int rangeKmDec = (int)((radarRangeMeters * 0.001 - rangeKmInt) * 10.0);
                    minimapLabel.Message.Append("<color=255,255,255>RADAR (")
                                        .Append(rangeKmInt);
                    if (rangeKmDec > 0)
                        minimapLabel.Message.Append('.').Append(rangeKmDec);
                    minimapLabel.Message.Append(" KM)");
                }

                // Radar circular radius on screen (aspect corrected for 1:1 circle)
                float radarRadiusY = (float)minimapSize.Y * 0.46f;
                float radarRadiusX = radarRadiusY / aspect;

                var camera = MyAPIGateway.Session.Camera;

                // Dynamic Camera View Frustum "V" Indicator (widens/narrows with game FOV and zoom)
                if (radarFovLeft != null && radarFovRight != null)
                {
                    float vFov = camera != null ? camera.FovWithZoom : 1.2217f;
                    float halfVFov = vFov * 0.5f;
                    float halfHFov = (float)Math.Atan(aspect * Math.Tan(halfVFov));

                    // Screen-space vector for right arm from (0,0) to perimeter
                    double armX = Math.Sin(halfHFov) * radarRadiusX;
                    double armY = Math.Cos(halfHFov) * radarRadiusY;
                    float armLen = (float)Math.Sqrt(armX * armX + armY * armY);
                    float armRot = (float)Math.Atan2(armX, armY);

                    // Sleek tactical HUD line: 0.0008f width with semi-transparent cyan glow
                    Color fovLineColor = new Color(120, 220, 255, 100);

                    // Right arm of V (opens upward-right)
                    radarFovRight.Origin = minimapPosition;
                    radarFovRight.Height = armLen;
                    radarFovRight.Width = 0.0008f;
                    radarFovRight.Offset = new Vector2D(armX * 0.5, armY * 0.5);
                    radarFovRight.Rotation = armRot;
                    radarFovRight.BillBoardColor = fovLineColor;
                    radarFovRight.Visible = true;

                    // Left arm of V (opens upward-left)
                    radarFovLeft.Origin = minimapPosition;
                    radarFovLeft.Height = armLen;
                    radarFovLeft.Width = 0.0008f;
                    radarFovLeft.Offset = new Vector2D(-armX * 0.5, armY * 0.5);
                    radarFovLeft.Rotation = -armRot;
                    radarFovLeft.BillBoardColor = fovLineColor;
                    radarFovLeft.Visible = true;
                }

                // Tangent plane orientation relative to Pertam's local gravity (Camera-Facing orientation)
                Vector3D upNormal = Vector3D.Normalize(playerPos - PLANET_CENTER);
                double playerElevation = Vector3D.Distance(playerPos, PLANET_CENTER) - PLANET_RADIUS;
                Vector3D forward = Vector3D.Forward;

                if (camera != null)
                {
                    forward = camera.WorldMatrix.Forward;
                }
                else
                {
                    var controlled = MyAPIGateway.Session.ControlledObject;
                    var cubeBlock = controlled as IMyCubeBlock;
                    var playerGrid = cubeBlock?.CubeGrid;
                    if (playerGrid != null)
                        forward = playerGrid.WorldMatrix.Forward;
                    else if (controlled?.Entity != null)
                        forward = controlled.Entity.WorldMatrix.Forward;
                }

                Vector3D fwdTangent = forward - Vector3D.Dot(forward, upNormal) * upNormal;
                if (fwdTangent.LengthSquared() < 0.001)
                {
                    Vector3D altVec = camera != null ? camera.WorldMatrix.Up : Vector3D.Up;
                    fwdTangent = altVec - Vector3D.Dot(altVec, upNormal) * upNormal;
                }
                if (fwdTangent.LengthSquared() < 0.001) fwdTangent = Vector3D.Forward;
                fwdTangent.Normalize();
                Vector3D rightTangent = Vector3D.Cross(fwdTangent, upNormal);
                rightTangent.Normalize();

                // Plot contacts relative to player rover heading (up to 100 km)
                const double MAX_RADAR_TRACK_DIST = 100000.0;

                for (int i = 0; i < activeHudWaypoints.Count && mIdx < minimapMarkerPool.Count; i++)
                {
                    var wp = activeHudWaypoints[i];
                    Vector3D toTarget = wp.Coords - playerPos;
                    double fwdDist = Vector3D.Dot(toTarget, fwdTangent);
                    double rightDist = Vector3D.Dot(toTarget, rightTangent);
                    double horizDist = Math.Sqrt(fwdDist * fwdDist + rightDist * rightDist);

                    // Skip contacts beyond 100 km limit, or own-grid contacts directly at dead-center (< 30m)
                    if (horizDist > MAX_RADAR_TRACK_DIST || horizDist < 30.0) continue;

                    double normDist;
                    bool isClamped = false;

                    if (radarScale == RadarScaleMode.Logarithmic)
                    {
                        // 3-Decade Log Scale:
                        // Inner Ring  (r = 1/3): 300 m  (CQB / rover proximity)
                        // Middle Ring (r = 2/3): 3 km   (Visual line-of-sight / standard artillery)
                        // Outer Ring  (r = 1.0): 30 km  (Planetary horizon)
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
                            // Beyond 30km: max out to outer edge perimeter (up to 100km)
                            normDist = 0.98;
                            isClamped = true;
                        }
                    }
                    else // Linear Mode
                    {
                        if (horizDist <= radarRangeMeters)
                        {
                            normDist = horizDist / radarRangeMeters;
                        }
                        else
                        {
                            normDist = 0.98; // Clamp to outer rim
                            isClamped = true;
                        }
                    }

                    double angle = Math.Atan2(rightDist, fwdDist); // 0 = fwd, pi/2 = right
                    Vector2D wpOffset = new Vector2D(
                        Math.Sin(angle) * radarRadiusX * normDist,
                        Math.Cos(angle) * radarRadiusY * normDist
                    );

                    var icon = minimapMarkerPool[mIdx++];
                    icon.Material = GetWaypointMaterial(ref wp, playerElevation);
                    icon.Rotation = 0f;

                    float rSize = 0.012f * minimapScale;
                    icon.Width = rSize;
                    icon.Height = rSize * aspect;
                    icon.BillBoardColor = isClamped ? wp.DisplayColor * 0.80f : wp.DisplayColor;

                    icon.Offset = wpOffset;
                    icon.Visible = true;
                }

                // Player vehicle/character orientation arrow relative to camera heading
                Vector3D vehForward = Vector3D.Forward;
                var controlledObj = MyAPIGateway.Session.ControlledObject;
                var cb = controlledObj as IMyCubeBlock;
                var pGrid = cb?.CubeGrid;
                if (pGrid != null)
                    vehForward = pGrid.WorldMatrix.Forward;
                else if (controlledObj?.Entity != null)
                    vehForward = controlledObj.Entity.WorldMatrix.Forward;
                else
                    vehForward = forward;

                Vector3D vehTangent = vehForward - Vector3D.Dot(vehForward, upNormal) * upNormal;
                float vehAngleRad = 0f;
                if (vehTangent.LengthSquared() > 0.001)
                {
                    vehTangent.Normalize();
                    double vehFwd = Vector3D.Dot(vehTangent, fwdTangent);
                    double vehRight = Vector3D.Dot(vehTangent, rightTangent);
                    vehAngleRad = (float)Math.Atan2(vehRight, vehFwd);
                }

                minimapPlayerDot.Offset = Vector2D.Zero;
                minimapPlayerDot.Rotation = vehAngleRad;
                // High-visibility pulse: Electric Gold <-> Amber
                minimapPlayerDot.BillBoardColor = (tickCounter % 40 < 20) ? new Color(255, 230, 40, 255) : new Color(255, 255, 140, 255);
                minimapPlayerDot.Visible = true;
            }

            // Hide unused pool slots
            for (int i = mIdx; i < minimapMarkerPool.Count; i++)
                minimapMarkerPool[i].Visible = false;
        }

        private void HideMinimapPool()
        {
            for (int i = 0; i < minimapMarkerPool.Count; i++)
                minimapMarkerPool[i].Visible = false;
        }

        /// <summary>
        /// Updates the Full Satellite Map using pre-allocated billboard and text pool (Zero GC allocations).
        /// </summary>
        private void UpdateFullMap(Vector3D playerPos)
        {
            if (mapTerrain == null || !showFullMap) return;

            float fullMapWidth = cachedFullMapWidth;
            float fullMapHeight = cachedFullMapHeight;

            mapDimmer.Visible = true;
            mapFrame.Visible = true;
            mapTerrain.Visible = true;
            mapPlayerDot.Visible = false;
            mapHeaderBg.Visible = true;
            mapHeaderAccent.Visible = true;
            mapHeaderMsg.Visible = true;
            mapHeaderSubMsg.Visible = true;

            double playerElevation = Vector3D.Distance(playerPos, PLANET_CENTER) - PLANET_RADIUS;
            Vector2 uv = WorldToMapUV(playerPos);
            Vector2D playerOffset = new Vector2D(
                (uv.X - 0.5) * fullMapWidth,
                (0.5 - uv.Y) * fullMapHeight
            );
            mapPlayerDot.Offset = playerOffset;
            mapPlayerDot.Rotation = -playerHeadingRad;
            mapPlayerDot.BillBoardColor = (tickCounter % 40 < 20) ? new Color(255, 230, 40, 255) : new Color(255, 255, 140, 255);

            // 1. Proximity Clustering (Radius ~0.038 screen units, ~2-3km on planetary surface)
            _cachedMapClusters.Clear();
            const double CLUSTER_DIST_SQ = 0.038 * 0.038;

            for (int i = 0; i < activeHudWaypoints.Count; i++)
            {
                var wp = activeHudWaypoints[i];
                Vector2 wpUV = wp.MapUV;
                Vector2D wpOffset = new Vector2D(
                    (wpUV.X - 0.5) * fullMapWidth,
                    (0.5 - wpUV.Y) * fullMapHeight
                );

                int foundCluster = -1;
                for (int c = 0; c < _cachedMapClusters.Count; c++)
                {
                    if (Vector2D.DistanceSquared(wpOffset, _cachedMapClusters[c].Position) < CLUSTER_DIST_SQ)
                    {
                        foundCluster = c;
                        break;
                    }
                }

                if (foundCluster >= 0)
                {
                    var cluster = _cachedMapClusters[foundCluster];
                    if (cluster.Count == 1) cluster.WpIndex1 = i;
                    else if (cluster.Count == 2) cluster.WpIndex2 = i;
                    else if (cluster.Count == 3) cluster.WpIndex3 = i;
                    else if (cluster.Count == 4) cluster.WpIndex4 = i;
                    cluster.Count++;
                    _cachedMapClusters[foundCluster] = cluster;
                }
                else
                {
                    _cachedMapClusters.Add(new MapCluster
                    {
                        Position = wpOffset,
                        Count = 1,
                        WpIndex0 = i,
                        WpIndex1 = -1,
                        WpIndex2 = -1,
                        WpIndex3 = -1,
                        WpIndex4 = -1
                    });
                }
            }

            // 2. Render Clusters with Vertical Waterfall Deconfliction (capped at 5 lines max)
            int sIdx = 0; // Sprite pool index
            int lIdx = 0; // Label pool index
            const float LINE_SPACING = 0.024f;

            for (int c = 0; c < _cachedMapClusters.Count && sIdx < fullMapMarkerPool.Count && lIdx < fullMapLabelPool.Count; c++)
            {
                var cluster = _cachedMapClusters[c];
                Vector2D basePos = cluster.Position;
                // Invert waterfall upwards if marker is near the bottom edge of the satellite map
                float dirY = (basePos.Y < -0.40) ? 1.0f : -1.0f;

                int itemsToShow = (cluster.Count <= 5) ? cluster.Count : 4;
                for (int slot = 0; slot < itemsToShow && sIdx < fullMapMarkerPool.Count && lIdx < fullMapLabelPool.Count; slot++)
                {
                    int wpIdx = -1;
                    switch (slot)
                    {
                        case 0: wpIdx = cluster.WpIndex0; break;
                        case 1: wpIdx = cluster.WpIndex1; break;
                        case 2: wpIdx = cluster.WpIndex2; break;
                        case 3: wpIdx = cluster.WpIndex3; break;
                        case 4: wpIdx = cluster.WpIndex4; break;
                    }

                    if (wpIdx >= 0 && wpIdx < activeHudWaypoints.Count)
                    {
                        var wp = activeHudWaypoints[wpIdx];
                        Vector2D slotPos = basePos + new Vector2D(0.0, dirY * LINE_SPACING * slot);

                        var sprite = fullMapMarkerPool[sIdx++];
                        sprite.Material = GetWaypointMaterial(ref wp, playerElevation);
                        sprite.Rotation = 0f;
                        sprite.BillBoardColor = wp.DisplayColor;
                        sprite.Offset = slotPos;
                        sprite.Visible = true;

                        var label = fullMapLabelPool[lIdx++];
                        FormatWaypointLabel(label, wp.Name);
                        label.Offset = slotPos + new Vector2D(0.015, 0.005);
                        label.Visible = true;
                    }
                }

                // If > 5 signals, slot 5 displays the overflow tag: "+N more..."
                if (cluster.Count > 5 && lIdx < fullMapLabelPool.Count)
                {
                    Vector2D slotPosOverflow = basePos + new Vector2D(0.0, dirY * LINE_SPACING * 4f);
                    var labelOverflow = fullMapLabelPool[lIdx++];
                    labelOverflow.Message.Clear()
                                 .Append("<color=170,210,245>+")
                                 .Append(cluster.Count - 4)
                                 .Append(" more...");
                    labelOverflow.Offset = slotPosOverflow + new Vector2D(0.015, 0.005);
                    labelOverflow.Visible = true;
                }
            }

            // Hide unused slots in the pools
            for (int i = sIdx; i < fullMapMarkerPool.Count; i++)
                fullMapMarkerPool[i].Visible = false;

            for (int i = lIdx; i < fullMapLabelPool.Count; i++)
                fullMapLabelPool[i].Visible = false;

            // Ensure player arrow is drawn on top of map markers and rotates with heading
            mapPlayerDot.Rotation = -playerHeadingRad;
            mapPlayerDot.Visible = true;

            // Update Tactical Header Panel docked seamlessly above top of the map frame
            float topOfFrame = (fullMapHeight + 0.01f * cachedAspect) * 0.5f;
            float mapHeaderHeight = 0.056f;
            float mapHeaderWidth = fullMapWidth + 0.02f;
            float mapHeaderCenterY = topOfFrame + 0.004f + (mapHeaderHeight * 0.5f);

            mapHeaderBg.Origin = new Vector2D(0.0, mapHeaderCenterY);
            mapHeaderAccent.Origin = new Vector2D(0.0, mapHeaderCenterY);
            mapHeaderAccent.Offset = new Vector2D(-mapHeaderWidth * 0.5f + 0.005f, 0.0);
            mapHeaderMsg.Origin = new Vector2D(-mapHeaderWidth * 0.5f + 0.016f, mapHeaderCenterY + 0.013f);
            mapHeaderSubMsg.Origin = new Vector2D(-mapHeaderWidth * 0.5f + 0.016f, mapHeaderCenterY - 0.011f);

            int distWhole = (int)lastDistKm;
            int distTenths = (int)((lastDistKm - distWhole) * 10.0);

            Color zoneAccentColor;
            mapHeaderText.Clear();
            mapHeaderText.Append("<color=255,220,0>KHARAK TACTICAL SATELLITE MAP<color=180,195,210> | Current Sector: ");

            mapHeaderSubText.Clear();
            mapHeaderSubText.Append("<color=220,230,240>Crossroads: <color=255,255,255>")
                            .Append(distWhole).Append('.').Append(distTenths).Append(" km<color=180,195,210> | ");

            switch (currentZoneIndex)
            {
                case 0:
                    zoneAccentColor = Color.LimeGreen;
                    mapHeaderText.Append("<color=50,255,100>[ ZONE 0: SAFE HUB ]");
                    double rem0 = Math.Max(0.0, 20.0 - lastDistKm);
                    int rem0Whole = (int)rem0;
                    int rem0Tenths = (int)((rem0 - rem0Whole) * 10.0);
                    mapHeaderSubText.Append("Z1 Border in: <color=255,230,50>")
                                    .Append(rem0Whole).Append('.').Append(rem0Tenths).Append(" km");
                    break;
                case 1:
                    zoneAccentColor = Color.Yellow;
                    mapHeaderText.Append("<color=255,230,50>[ ZONE 1: PVE FRONTIER ]");
                    double rem1 = Math.Max(0.0, 35.0 - lastDistKm);
                    int rem1Whole = (int)rem1;
                    int rem1Tenths = (int)((rem1 - rem1Whole) * 10.0);
                    mapHeaderSubText.Append("PvP Border in: <color=255,165,0>")
                                    .Append(rem1Whole).Append('.').Append(rem1Tenths).Append(" km");
                    break;
                case 2:
                    zoneAccentColor = Color.Orange;
                    mapHeaderText.Append("<color=255,165,0>[ ZONE 2: CONTESTED (PVP) ]");
                    double rem2 = Math.Max(0.0, 50.0 - lastDistKm);
                    int rem2Whole = (int)rem2;
                    int rem2Tenths = (int)((rem2 - rem2Whole) * 10.0);
                    mapHeaderSubText.Append("Z3 Border in: <color=255,50,50>")
                                    .Append(rem2Whole).Append('.').Append(rem2Tenths).Append(" km");
                    break;
                case 3:
                default:
                    zoneAccentColor = Color.Red;
                    mapHeaderText.Append("<color=255,50,50>[ ZONE 3: GAALSIEN HEART ]");
                    int z3Whole = (int)lastDistZ3Km;
                    int z3Tenths = (int)((lastDistZ3Km - z3Whole) * 10.0);
                    mapHeaderSubText.Append("Core Dist: <color=255,50,50>")
                                    .Append(z3Whole).Append('.').Append(z3Tenths).Append(" km");
                    break;
            }

            mapHeaderSubText.Append("          <color=160,180,200>[ Press 'M' or ESC to Close ]");
            mapHeaderAccent.BillBoardColor = zoneAccentColor;
        }

        /// <summary>
        /// Slices waypoint name if longer than 20 characters into [First 12]...[Last 4] with zero heap allocations.
        /// </summary>
        private void FormatWaypointLabel(HudAPIv2.HUDMessage label, string wpName)
        {
            label.Message.Clear();
            string name = wpName ?? string.Empty;
            if (name.Length > 20)
            {
                label.Message.Append(name, 0, 12)
                             .Append("...")
                             .Append(name, name.Length - 4, 4);
            }
            else
            {
                label.Message.Append(name);
            }
        }

        private void HideFullMapPool()
        {
            for (int i = 0; i < fullMapMarkerPool.Count; i++)
            {
                fullMapMarkerPool[i].Visible = false;
                fullMapLabelPool[i].Visible = false;
            }
        }

        private string GetZoneName(int zone)
        {
            switch (zone)
            {
                case 0: return "Zone 0 (Safe Hub)";
                case 1: return "Zone 1 (PvE Frontier)";
                case 2: return "Zone 2 (Contested PvP)";
                default: return "Zone 3 (Gaalsien Deep Desert)";
            }
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

            if (msg.Equals("/map", StringComparison.OrdinalIgnoreCase))
            {
                sendToOthers = false;
                ToggleFullMap();
                return;
            }

            if (msg.Equals("/radar", StringComparison.OrdinalIgnoreCase) ||
                msg.Equals("/minimap mode", StringComparison.OrdinalIgnoreCase) ||
                msg.Equals("/zone radar", StringComparison.OrdinalIgnoreCase))
            {
                sendToOthers = false;
                ToggleMinimapMode();
                return;
            }

            if (msg.StartsWith("/radar range", StringComparison.OrdinalIgnoreCase))
            {
                sendToOthers = false;
                string[] parts = msg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    double parsedKm;
                    if (double.TryParse(parts[2], out parsedKm) && parsedKm >= 0.5 && parsedKm <= 20.0)
                    {
                        radarRangeMeters = parsedKm * 1000.0;
                        _refreshMinimapNextFrame = true;
                        SaveConfig();
                        MyAPIGateway.Utilities.ShowNotification($"[GVK NAV] Radar Range set to {parsedKm:F1} km", 2500, MyFontEnum.Green);
                        return;
                    }
                }
                CycleRadarRange();
                return;
            }

            if (msg.Equals("/radar scale", StringComparison.OrdinalIgnoreCase) ||
                msg.Equals("/radar log", StringComparison.OrdinalIgnoreCase) ||
                msg.Equals("/radar linear", StringComparison.OrdinalIgnoreCase))
            {
                sendToOthers = false;
                ToggleRadarScale();
                return;
            }

            if (msg.Equals("/minimap", StringComparison.OrdinalIgnoreCase) ||
                msg.Equals("/zone minimap", StringComparison.OrdinalIgnoreCase))
            {
                sendToOthers = false;
                ToggleMinimap();
                return;
            }

            if (msg.Equals("/minimap size", StringComparison.OrdinalIgnoreCase) ||
                msg.Equals("/minimap scale", StringComparison.OrdinalIgnoreCase) ||
                msg.Equals("/radar size", StringComparison.OrdinalIgnoreCase) ||
                msg.Equals("/radar scale-size", StringComparison.OrdinalIgnoreCase))
            {
                sendToOthers = false;
                CycleMinimapScale();
                return;
            }

            if (msg.Equals("/compass", StringComparison.OrdinalIgnoreCase) ||
                msg.Equals("/zone compass", StringComparison.OrdinalIgnoreCase))
            {
                sendToOthers = false;
                ToggleCompass();
                return;
            }

            if (msg.Equals("/compass size", StringComparison.OrdinalIgnoreCase) ||
                msg.Equals("/compass scale", StringComparison.OrdinalIgnoreCase) ||
                msg.Equals("/zone compass-size", StringComparison.OrdinalIgnoreCase))
            {
                sendToOthers = false;
                CycleCompassScale();
                return;
            }

            if (msg.Equals("/zone hud", StringComparison.OrdinalIgnoreCase) ||
                msg.Equals("/zone bar", StringComparison.OrdinalIgnoreCase))
            {
                sendToOthers = false;
                ToggleZoneBar();
                return;
            }

            if (msg.Equals("/zone gps", StringComparison.OrdinalIgnoreCase) ||
                msg.Equals("/gps defaults", StringComparison.OrdinalIgnoreCase))
            {
                sendToOthers = false;
                PopulateDefaultGps(notify: true);
                return;
            }

            if (msg.Equals("/zone", StringComparison.OrdinalIgnoreCase) ||
                msg.Equals("/whereami", StringComparison.OrdinalIgnoreCase) ||
                msg.Equals("/loc", StringComparison.OrdinalIgnoreCase))
            {
                sendToOthers = false;
                OpenZoneMissionScreen();
                return;
            }

            if (msg.Equals("/zones", StringComparison.OrdinalIgnoreCase) ||
                msg.Equals("/zone all", StringComparison.OrdinalIgnoreCase) ||
                msg.Equals("/zone help", StringComparison.OrdinalIgnoreCase))
            {
                sendToOthers = false;
                OpenAllZonesMissionScreen();
                return;
            }

            if (msg.StartsWith("/nav rate", StringComparison.OrdinalIgnoreCase) ||
                msg.StartsWith("/zone rate", StringComparison.OrdinalIgnoreCase) ||
                msg.StartsWith("/hud rate", StringComparison.OrdinalIgnoreCase))
            {
                sendToOthers = false;
                string[] parts = msg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    int parsedTicks;
                    if (int.TryParse(parts[2], out parsedTicks) && parsedTicks >= 1 && parsedTicks <= 60)
                    {
                        SetUpdateTickRate(parsedTicks);
                        return;
                    }
                }
                CycleUpdateTickRate();
                return;
            }
        }

        private void ToggleFullMap()
        {
            showFullMap = !showFullMap;
            if (!showFullMap)
            {
                if (mapDimmer != null) mapDimmer.Visible = false;
                if (mapFrame != null) mapFrame.Visible = false;
                if (mapTerrain != null) mapTerrain.Visible = false;
                if (mapPlayerDot != null) mapPlayerDot.Visible = false;
                if (mapHeaderBg != null) mapHeaderBg.Visible = false;
                if (mapHeaderAccent != null) mapHeaderAccent.Visible = false;
                if (mapHeaderMsg != null) mapHeaderMsg.Visible = false;
                if (mapHeaderSubMsg != null) mapHeaderSubMsg.Visible = false;
                HideFullMapPool();
                _refreshMinimapNextFrame = true;
                _refreshCompassNextFrame = true;
            }
            else
            {
                _fullMapNeedsRedraw = true;

                // Immediately hide compass and minimap elements when pulling up the satellite map
                if (compassBg != null) compassBg.Visible = false;
                if (compassLeftAccent != null) compassLeftAccent.Visible = false;
                if (compassRightAccent != null) compassRightAccent.Visible = false;
                if (compassCenterPointer != null) compassCenterPointer.Visible = false;
                if (compassCenterBottomPip != null) compassCenterBottomPip.Visible = false;
                HideTapePool();
                HideSpritePool();

                if (minimapHeaderBg != null) minimapHeaderBg.Visible = false;
                if (minimapHeaderAccent != null) minimapHeaderAccent.Visible = false;
                if (minimapBg != null) minimapBg.Visible = false;
                if (minimapTerrain != null) minimapTerrain.Visible = false;
                if (radarGrid != null) radarGrid.Visible = false;
                if (radarFovLeft != null) radarFovLeft.Visible = false;
                if (radarFovRight != null) radarFovRight.Visible = false;
                if (minimapPlayerDot != null) minimapPlayerDot.Visible = false;
                if (minimapLabel != null) minimapLabel.Visible = false;
                HideMinimapPool();

                if (zoneBg != null) zoneBg.Visible = false;
                if (zoneAccent != null) zoneAccent.Visible = false;
                if (zoneMsg != null) zoneMsg.Visible = false;
                _compassElementsVisible = false;
                _minimapElementsVisible = false;
                _zoneBarElementsVisible = false;
            }
        }

        private void ToggleMinimap()
        {
            showMinimap = !showMinimap;
            if (!showMinimap)
            {
                _minimapElementsVisible = false;
                if (minimapHeaderBg != null) minimapHeaderBg.Visible = false;
                if (minimapHeaderAccent != null) minimapHeaderAccent.Visible = false;
                if (radarGrid != null) radarGrid.Visible = false;
                if (radarFovLeft != null) radarFovLeft.Visible = false;
                if (radarFovRight != null) radarFovRight.Visible = false;
                if (minimapBg != null) minimapBg.Visible = false;
                if (minimapTerrain != null) minimapTerrain.Visible = false;
                if (minimapPlayerDot != null) minimapPlayerDot.Visible = false;
                if (minimapLabel != null) minimapLabel.Visible = false;
                HideMinimapPool();
            }
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
            _lastZoneBarZoneIndex = -1;
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
            float strategicBoxWidth = (float)minimapSize.X + 0.012f * scale;
            float minimapBgWidth = (minimapMode == MinimapDisplayMode.TacticalRadar) ? radarBoxWidth : strategicBoxWidth;
            float minimapBgHeight = (float)minimapSize.Y + (0.008f * scale) * aspect;
            float minimapHeaderHeight = 0.026f * scale;
            float headerGap = 0.005f * scale;

            // Anchor neatly to the top-right screen corner: right edge fixed at ~0.97
            double posX = 0.97 - (minimapBgWidth * 0.5);
            double posY = 0.95 - (minimapBgHeight * 0.5) - minimapHeaderHeight - headerGap - 0.006;
            minimapPosition = new Vector2D(posX, posY);

            float minimapBgTop = (float)(minimapPosition.Y + minimapBgHeight * 0.5f);
            float minimapHeaderCenterY = minimapBgTop + headerGap + (minimapHeaderHeight * 0.5f);
            Vector2D minimapHeaderPos = new Vector2D(minimapPosition.X, minimapHeaderCenterY);

            if (minimapHeaderBg != null)
            {
                minimapHeaderBg.Origin = minimapHeaderPos;
                minimapHeaderBg.Width = minimapBgWidth;
                minimapHeaderBg.Height = minimapHeaderHeight;
            }
            if (minimapHeaderAccent != null)
            {
                minimapHeaderAccent.Origin = minimapHeaderPos;
                minimapHeaderAccent.Width = 0.004f * scale;
                minimapHeaderAccent.Height = minimapHeaderHeight - 0.004f * scale;
                minimapHeaderAccent.Offset = new Vector2D(-minimapBgWidth * 0.5f + 0.004f * scale, 0.0);
            }
            if (minimapLabel != null)
            {
                minimapLabel.Origin = new Vector2D(minimapPosition.X - minimapBgWidth * 0.5f + 0.014f * scale, minimapHeaderCenterY + 0.007f * scale);
                minimapLabel.Scale = (minimapMode == MinimapDisplayMode.TacticalRadar ? 0.55 : 0.60) * scale;
            }
            if (minimapBg != null)
            {
                minimapBg.Origin = minimapPosition;
                minimapBg.Width = minimapBgWidth;
                minimapBg.Height = minimapBgHeight;
            }
            if (minimapTerrain != null)
            {
                minimapTerrain.Origin = minimapPosition;
                minimapTerrain.Width = (float)minimapSize.X;
                minimapTerrain.Height = (float)minimapSize.Y;
            }
            if (radarGrid != null)
            {
                radarGrid.Origin = minimapPosition;
                radarGrid.Width = radarGridDiameter / aspect;
                radarGrid.Height = radarGridDiameter;
            }
            if (radarFovLeft != null)
            {
                radarFovLeft.Origin = minimapPosition;
            }
            if (radarFovRight != null)
            {
                radarFovRight.Origin = minimapPosition;
            }
            if (minimapPlayerDot != null)
            {
                minimapPlayerDot.Origin = minimapPosition;
                float dotSize = 0.017f * scale;
                minimapPlayerDot.Width = dotSize;
                minimapPlayerDot.Height = dotSize * aspect;
            }
            for (int i = 0; i < minimapMarkerPool.Count; i++)
            {
                minimapMarkerPool[i].Origin = minimapPosition;
                float mSize = 0.012f * scale;
                minimapMarkerPool[i].Width = mSize;
                minimapMarkerPool[i].Height = mSize * aspect;
            }
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

        private void ToggleRadarScale()
        {
            CycleRadarRange();
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

        private void ApplyCompassScale(float scale)
        {
            compassScale = scale;
            float aspect = GetScreenAspect();

            float baseWidth = 0.54f + 0.08f * scale;
            float baseHeight = 0.076f * scale;
            Vector2D compassOrigin = new Vector2D(0.0, COMPASS_TOP_Y - baseHeight * 0.5);

            float accentWidth = 0.005f * scale;
            float accentHeight = baseHeight - 0.006f * scale;

            if (compassBg != null)
            {
                compassBg.Origin = compassOrigin;
                compassBg.Width = baseWidth;
                compassBg.Height = baseHeight;
            }
            if (compassLeftAccent != null)
            {
                compassLeftAccent.Origin = compassOrigin;
                compassLeftAccent.Width = accentWidth;
                compassLeftAccent.Height = accentHeight;
                compassLeftAccent.Offset = new Vector2D(-baseWidth * 0.5f + 0.005f * scale, 0.0);
            }
            if (compassRightAccent != null)
            {
                compassRightAccent.Origin = compassOrigin;
                compassRightAccent.Width = accentWidth;
                compassRightAccent.Height = accentHeight;
                compassRightAccent.Offset = new Vector2D(baseWidth * 0.5f - 0.005f * scale, 0.0);
            }
            if (compassCenterPointer != null)
            {
                float pWidth = 0.010f * scale;
                float pHeight = pWidth * aspect;
                compassCenterPointer.Origin = compassOrigin;
                compassCenterPointer.Width = pWidth;
                compassCenterPointer.Height = pHeight;
                compassCenterPointer.Offset = new Vector2D(0.0, baseHeight * 0.5);
            }
            if (compassCenterBottomPip != null)
            {
                float pWidth = 0.008f * scale;
                float pHeight = pWidth * aspect;
                compassCenterBottomPip.Origin = compassOrigin;
                compassCenterBottomPip.Width = pWidth;
                compassCenterBottomPip.Height = pHeight;
                compassCenterBottomPip.Offset = new Vector2D(0.0, -baseHeight * 0.5);
            }
            for (int i = 0; i < compassTapePool.Count; i++)
            {
                if (compassTapePool[i] != null)
                {
                    compassTapePool[i].Scale = 0.55 * scale;
                }
            }
            for (int i = 0; i < waypointSpritePool.Count; i++)
            {
                if (waypointSpritePool[i] != null)
                {
                    float wWidth = 0.011f * scale;
                    float wHeight = wWidth * aspect;
                    waypointSpritePool[i].Width = wWidth;
                    waypointSpritePool[i].Height = wHeight;
                }
                if (waypointDistPool[i] != null)
                {
                    waypointDistPool[i].Scale = 0.46 * scale;
                }
            }
            for (int i = 0; i < COMPASS_GRADUATIONS.Length; i++)
            {
                COMPASS_GRADUATIONS[i].BaseTopHalfWidth = -1.0;
                COMPASS_GRADUATIONS[i].BaseBottomHalfWidth = -1.0;
            }
            _refreshCompassNextFrame = true;
        }

        private void ToggleCompass()
        {
            showCompass = !showCompass;
            if (!showCompass)
            {
                _compassElementsVisible = false;
                if (compassBg != null) compassBg.Visible = false;
                if (compassLeftAccent != null) compassLeftAccent.Visible = false;
                if (compassRightAccent != null) compassRightAccent.Visible = false;
                if (compassCenterPointer != null) compassCenterPointer.Visible = false;
                if (compassCenterBottomPip != null) compassCenterBottomPip.Visible = false;
                HideTapePool();
                HideSpritePool();
            }
            else
            {
                _refreshCompassNextFrame = true;
            }
            SaveConfig();
            string status = showCompass ? "ENABLED" : "DISABLED";
            MyAPIGateway.Utilities.ShowNotification($"[GVK NAV] Compass Ribbon: {status}", 2500, showCompass ? MyFontEnum.Green : MyFontEnum.Red);
        }

        private void ToggleZoneBar()
        {
            showZoneBar = !showZoneBar;
            if (!showZoneBar)
            {
                _zoneBarElementsVisible = false;
                if (zoneBg != null) zoneBg.Visible = false;
                if (zoneAccent != null) zoneAccent.Visible = false;
                if (zoneMsg != null) zoneMsg.Visible = false;
                if (zoneDistMsg != null) zoneDistMsg.Visible = false;
            }
            else
            {
                _refreshMinimapNextFrame = true;
            }
            SaveConfig();
            string status = showZoneBar ? "ENABLED" : "DISABLED";
            MyAPIGateway.Utilities.ShowNotification($"[GVK NAV] Zone Status Bar: {status}", 2500, showZoneBar ? MyFontEnum.Green : MyFontEnum.Red);
        }

        private void OpenZoneMissionScreen()
        {
            Vector3D? pos = GetPlayerPosition();
            if (!pos.HasValue) return;

            string title = "DESERTS OF KHARAK — ZONE ADVISORY";
            string objectivePrefix = "Current Location:";
            string currentObjective = $"{GetZoneName(currentZoneIndex)} ({lastDistKm:F1} km from Crossroads)";

            // Reuse _missionSb to avoid per-call StringBuilder allocation.
            _missionSb.Clear();
            _missionSb.AppendLine($"CURRENT SECTOR: {GetZoneName(currentZoneIndex).ToUpper()}");
            _missionSb.AppendLine("--------------------------------------------------");
            _missionSb.AppendLine($"• Distance from Crossroads Tower: {lastDistKm:F1} km");
            if (currentZoneIndex < 3)
                _missionSb.AppendLine($"• Next Sector Transition: {lastRemainingKm:F1} km ahead");
            else
                _missionSb.AppendLine($"• Distance to Z3 Antipode Core: {lastDistZ3Km:F1} km");
            _missionSb.AppendLine();
            _missionSb.AppendLine("COMBAT & GOVERNANCE RULES:");
            if (currentZoneIndex <= 1)
            {
                _missionSb.AppendLine("• Strict PvE Region: Player-vs-player damage is zeroed out.");
                _missionSb.AppendLine("• Hostile NPC wrecks can be ground with upgraded/ship grinders.");
                _missionSb.AppendLine("• Shield Generators: 100% NON-SIEGABLE.");
            }
            else
            {
                _missionSb.AppendLine("• FULL PVP WARFARE UNLOCKED.");
                _missionSb.AppendLine("• Full production and upgrades permitted.");
                _missionSb.AppendLine("• Shield Generators: SIEGABLE via Siege Drives.");
            }
            _missionSb.AppendLine("--------------------------------------------------");
            _missionSb.AppendLine("Controls: Press [M] for Map | /minimap | /compass | /zone gps");

            MyAPIGateway.Utilities.ShowMissionScreen(
                screenTitle: title,
                currentObjectivePrefix: objectivePrefix,
                currentObjective: currentObjective,
                screenDescription: _missionSb.ToString(),
                callback: null,
                okButtonCaption: "Close"
            );
        }

        private void OpenAllZonesMissionScreen()
        {
            // Reuse _missionSb to avoid per-call StringBuilder allocation.
            _missionSb.Clear();
            _missionSb.AppendLine("DESERTS OF KHARAK — PLANETARY ZONE DIRECTORY");
            _missionSb.AppendLine("All zone distances measured straight-line from Crossroads Tower:");
            _missionSb.AppendLine("==================================================");
            _missionSb.AppendLine("• Zone 0 (0 – 20 km): Safe Starter Hub | Strict PvE | Basic Prod Only | Shields Non-Siegable");
            _missionSb.AppendLine("• Zone 1 (20 – 35 km): PvE & Salvage | Strict PvE | Weapons/Drills/Grinders Enabled");
            _missionSb.AppendLine("• Zone 2 (35 – 50 km): Contested Desert | Full PvP | Large Prod Unlocked | Shields Siegable");
            _missionSb.AppendLine("• Zone 3 (> 50 km): Deep Desert | High-Threat PvPvE | Ancient Relics | Battlecruisers");
            _missionSb.AppendLine("==================================================");
            _missionSb.AppendLine("Hotkeys & Commands:");
            _missionSb.AppendLine("• Press [M] to toggle Full Satellite Map");
            _missionSb.AppendLine("• /minimap - Toggle live top-right minimap");
            _missionSb.AppendLine("• /compass - Toggle heading tape");
            _missionSb.AppendLine("• /zone hud - Toggle zone status bar");
            _missionSb.AppendLine("• /nav rate [ticks] - Set or cycle HUD refresh rate (e.g. 6 = 10 Hz, 5 = 12 Hz, 1 = 60 Hz)");
            _missionSb.AppendLine("• /zone gps - Restore default Kharak GPS waypoints");

            MyAPIGateway.Utilities.ShowMissionScreen(
                screenTitle: "DESERTS OF KHARAK — ZONE DIRECTORY",
                currentObjectivePrefix: "Reference Guide:",
                currentObjective: "Planetary Zone Boundaries & Governance Matrix",
                screenDescription: _missionSb.ToString(),
                okButtonCaption: "Close"
            );
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