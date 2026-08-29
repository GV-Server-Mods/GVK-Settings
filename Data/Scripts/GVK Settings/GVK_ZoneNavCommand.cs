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
    /// - High-readability distance badges centered dynamically below each POI.
    /// - Perfectly centered Zone Status Bar below the compass.
    /// - Live Corner Minimap (top-right, true 2:1 ratio) with accurate UV player and POI icons.
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

        // Keen Vanilla HUD Markers (from Textures\HUD\)
        private static readonly MyStringId MATERIAL_MARKER_GPS = MyStringId.GetOrCompute("marker_gps");
        private static readonly MyStringId MATERIAL_MARKER_FRIENDLY = MyStringId.GetOrCompute("marker_friendly");
        private static readonly MyStringId MATERIAL_MARKER_ENEMY = MyStringId.GetOrCompute("marker_enemy");
        private static readonly MyStringId MATERIAL_MARKER_NEUTRAL = MyStringId.GetOrCompute("marker_neutral");
        private static readonly MyStringId MATERIAL_MARKER_SELF = MyStringId.GetOrCompute("marker_self");
        private static readonly MyStringId MATERIAL_MARKER_ALERT = MyStringId.GetOrCompute("marker_alert");
        private static readonly MyStringId MATERIAL_NAV_ARROW = MyStringId.GetOrCompute("nav_arrow");


        // Compass Tape Definition
        private struct CompassMarker
        {
            public string Label;
            public float Offset;
            public double HalfWidth;

            public CompassMarker(string label, float offset)
            {
                Label = label;
                Offset = offset;
                HalfWidth = -1.0;
            }
        }

        private static readonly CompassMarker[] COMPASS_TAPE = new CompassMarker[]
        {
            new CompassMarker("S", 0f),
            new CompassMarker("•", -0.025f),
            new CompassMarker("•", -0.05f),
            new CompassMarker("•", -0.075f),
            new CompassMarker("•", -0.1f),
            new CompassMarker("SSE", -0.125f),
            new CompassMarker("•", -0.15f),
            new CompassMarker("•", -0.175f),
            new CompassMarker("•", -0.2f),
            new CompassMarker("•", -0.225f),
            new CompassMarker("SE", -0.25f),
            new CompassMarker("•", -0.275f),
            new CompassMarker("•", -0.3f),
            new CompassMarker("•", -0.325f),
            new CompassMarker("•", -0.35f),
            new CompassMarker("ESE", -0.375f),
            new CompassMarker("•", -0.4f),
            new CompassMarker("•", -0.425f),
            new CompassMarker("•", -0.45f),
            new CompassMarker("•", -0.475f),
            new CompassMarker("E", -0.5f),
            new CompassMarker("•", -0.525f),
            new CompassMarker("•", -0.55f),
            new CompassMarker("•", -0.575f),
            new CompassMarker("•", -0.6f),
            new CompassMarker("ENE", -0.625f),
            new CompassMarker("•", -0.65f),
            new CompassMarker("•", -0.675f),
            new CompassMarker("•", -0.7f),
            new CompassMarker("•", -0.725f),
            new CompassMarker("NE", -0.75f),
            new CompassMarker("•", -0.775f),
            new CompassMarker("•", -0.8f),
            new CompassMarker("•", -0.825f),
            new CompassMarker("•", -0.85f),
            new CompassMarker("NNE", -0.875f),
            new CompassMarker("•", -0.9f),
            new CompassMarker("•", -0.925f),
            new CompassMarker("•", -0.95f),
            new CompassMarker("•", -0.975f),
            new CompassMarker("N", -1.0f),
            new CompassMarker("•", 0.975f),
            new CompassMarker("•", 0.95f),
            new CompassMarker("•", 0.925f),
            new CompassMarker("•", 0.9f),
            new CompassMarker("NNW", 0.875f),
            new CompassMarker("•", 0.85f),
            new CompassMarker("•", 0.825f),
            new CompassMarker("•", 0.8f),
            new CompassMarker("•", 0.775f),
            new CompassMarker("NW", 0.75f),
            new CompassMarker("•", 0.725f),
            new CompassMarker("•", 0.7f),
            new CompassMarker("•", 0.675f),
            new CompassMarker("•", 0.65f),
            new CompassMarker("WNW", 0.625f),
            new CompassMarker("•", 0.6f),
            new CompassMarker("•", 0.575f),
            new CompassMarker("•", 0.55f),
            new CompassMarker("•", 0.525f),
            new CompassMarker("W", 0.5f),
            new CompassMarker("•", 0.475f),
            new CompassMarker("•", 0.45f),
            new CompassMarker("•", 0.425f),
            new CompassMarker("•", 0.4f),
            new CompassMarker("WSW", 0.375f),
            new CompassMarker("•", 0.35f),
            new CompassMarker("•", 0.325f),
            new CompassMarker("•", 0.3f),
            new CompassMarker("•", 0.275f),
            new CompassMarker("SW", 0.25f),
            new CompassMarker("•", 0.225f),
            new CompassMarker("•", 0.2f),
            new CompassMarker("•", 0.175f),
            new CompassMarker("•", 0.15f),
            new CompassMarker("SSW", 0.125f),
            new CompassMarker("•", 0.1f),
            new CompassMarker("•", 0.075f),
            new CompassMarker("•", 0.05f),
            new CompassMarker("•", 0.025f)
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

        // 1. Compass Elements
        private HudAPIv2.BillBoardHUDMessage compassFrame;
        private List<HudAPIv2.HUDMessage> compassTapePool = new List<HudAPIv2.HUDMessage>();
        private List<HudAPIv2.BillBoardHUDMessage> waypointSpritePool = new List<HudAPIv2.BillBoardHUDMessage>();
        private List<HudAPIv2.HUDMessage> waypointDistPool = new List<HudAPIv2.HUDMessage>();
        private bool showCompass = true;

        // 2. Zone Bar Elements (Centered at Y = 0.895, below compass frame)
        private HudAPIv2.HUDMessage zoneMsg;
        private HudAPIv2.BillBoardHUDMessage zoneBg;
        private HudAPIv2.BillBoardHUDMessage zoneAccent;
        private StringBuilder zoneText = new StringBuilder(128);
        private bool showZoneBar = true;

        // 3. Corner Minimap Elements (Top-Right: true 2:1 ratio for KharakMap)
        private HudAPIv2.BillBoardHUDMessage minimapBg;
        private HudAPIv2.BillBoardHUDMessage minimapTerrain;
        private HudAPIv2.BillBoardHUDMessage minimapPlayerDot;
        private HudAPIv2.HUDMessage minimapLabel;
        private List<HudAPIv2.BillBoardHUDMessage> minimapMarkerPool = new List<HudAPIv2.BillBoardHUDMessage>();
        private Vector2D minimapPosition = new Vector2D(0.81, 0.73);
        private Vector2D minimapSize = new Vector2D(0.26, 0.23); // Dynamically set with aspect ratio
        private bool showMinimap = true;

        // 4. Interactive Full-Screen Satellite Map ([M] Key, true 2:1 ratio)
        private bool showFullMap = false;
        private HudAPIv2.BillBoardHUDMessage mapDimmer;
        private HudAPIv2.BillBoardHUDMessage mapFrame;
        private HudAPIv2.BillBoardHUDMessage mapTerrain;
        private HudAPIv2.BillBoardHUDMessage mapPlayerDot;
        private HudAPIv2.HUDMessage mapFooterMsg;
        private StringBuilder mapFooterText = new StringBuilder(128);
        private List<HudAPIv2.BillBoardHUDMessage> fullMapMarkerPool = new List<HudAPIv2.BillBoardHUDMessage>();
        private List<HudAPIv2.HUDMessage> fullMapLabelPool = new List<HudAPIv2.HUDMessage>();

        // Dynamic State
        private int currentZoneIndex = 0;
        private double lastDistKm = 0.0;
        private double lastRemainingKm = 0.0;
        private double lastDistZ3Km = 0.0;
        private int tickCounter = 0;
        private float playerHeadingRad = 0f;
        private bool hasCheckedDefaultGps = false;
        private bool _refreshMinimapNextFrame = false;
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


        public override void LoadData()
        {
            if (!MyAPIGateway.Utilities.IsDedicated)
            {
                MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;
                hudApi = new HudAPIv2(OnHudApiRegistered);
            }
        }

        protected override void UnloadData()
        {
            if (!MyAPIGateway.Utilities.IsDedicated)
            {
                MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;

                if (hudApi != null)
                {
                    compassFrame?.DeleteMessage();
                    ClearTapePool();
                    ClearSpritePool();

                    zoneMsg?.DeleteMessage();
                    zoneBg?.DeleteMessage();
                    zoneAccent?.DeleteMessage();

                    minimapBg?.DeleteMessage();
                    minimapTerrain?.DeleteMessage();
                    minimapPlayerDot?.DeleteMessage();
                    minimapLabel?.DeleteMessage();
                    ClearMinimapPool();

                    mapDimmer?.DeleteMessage();
                    mapFrame?.DeleteMessage();
                    mapTerrain?.DeleteMessage();
                    mapPlayerDot?.DeleteMessage();
                    mapFooterMsg?.DeleteMessage();
                    ClearFullMapPool();

                    hudApi.Close();
                    hudApi = null;
                }
            }
        }

        private void ClearTapePool()
        {
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

                // 1. Compass Frame Graphic (compass.dds)
                compassFrame = new HudAPIv2.BillBoardHUDMessage(
                    Material: MATERIAL_COMPASS,
                    Origin: new Vector2D(0f, 0.5125f),
                    BillBoardColor: Color.White,
                    Offset: Vector2D.Zero,
                    TimeToLive: -1,
                    Scale: 1.0,
                    Width: 1.0f,
                    Height: 1.0f,
                    HideHud: true,
                    Shadowing: false,
                    Blend: BlendTypeEnum.PostPP
                );

                // Pre-allocate 45 Tape character HUDMessages (45 handles wider FOVs without pool overflow)
                for (int i = 0; i < 45; i++)
                {
                    var msg = new HudAPIv2.HUDMessage(
                        Message: new StringBuilder(""),
                        Origin: Vector2D.Zero,
                        Offset: Vector2D.Zero,
                        TimeToLive: -1,
                        Scale: 0.90,
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

                // 2. Zone Bar (Horizontally Centered at Y = 0.895, below the compass frame)
                zoneBg = new HudAPIv2.BillBoardHUDMessage(
                    Material: MATERIAL_SQUARE,
                    Origin: new Vector2D(0.0, 0.895),
                    BillBoardColor: new Color(10, 15, 22, 225),
                    Offset: Vector2D.Zero,
                    TimeToLive: -1,
                    Scale: 1.0,
                    Width: 0.54f,
                    Height: 0.034f,
                    HideHud: true,
                    Shadowing: true,
                    Blend: BlendTypeEnum.PostPP
                );

                zoneAccent = new HudAPIv2.BillBoardHUDMessage(
                    Material: MATERIAL_SQUARE,
                    Origin: new Vector2D(0.0, 0.895),
                    BillBoardColor: Color.LimeGreen,
                    Offset: new Vector2D(-0.267, 0.0),
                    TimeToLive: -1,
                    Scale: 1.0,
                    Width: 0.006f,
                    Height: 0.034f,
                    HideHud: true,
                    Shadowing: false,
                    Blend: BlendTypeEnum.PostPP
                );

                zoneMsg = new HudAPIv2.HUDMessage(
                    Message: zoneText,
                    Origin: new Vector2D(0.0, 0.895),
                    Offset: Vector2D.Zero,
                    TimeToLive: -1,
                    Scale: 0.72,
                    HideHud: true,
                    Shadowing: true,
                    ShadowColor: Color.Black,
                    Blend: BlendTypeEnum.PostPP
                );

                // 3. Corner Minimap (Aspect-corrected true 2:1 image ratio for KharakMap.dds)
                float mWidth = 0.26f;
                float mHeight = (mWidth * 0.5f) * aspect;
                minimapSize = new Vector2D(mWidth, mHeight);
                minimapPosition = new Vector2D(0.81, 0.95 - mHeight * 0.5 - 0.02);

                minimapBg = new HudAPIv2.BillBoardHUDMessage(
                    Material: MATERIAL_SQUARE,
                    Origin: minimapPosition,
                    BillBoardColor: new Color(10, 16, 24, 240),
                    Offset: Vector2D.Zero,
                    TimeToLive: -1,
                    Scale: 1.0,
                    Width: (float)minimapSize.X + 0.012f,
                    Height: (float)minimapSize.Y + 0.006f * aspect,
                    HideHud: true,
                    Shadowing: true,
                    Blend: BlendTypeEnum.PostPP
                );

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

                minimapLabel = new HudAPIv2.HUDMessage(
                    Message: new StringBuilder("<color=200,220,255>TACTICAL RADAR"),
                    Origin: new Vector2D(minimapPosition.X - minimapSize.X * 0.5, minimapPosition.Y + minimapSize.Y * 0.5 + 0.015),
                    Offset: Vector2D.Zero,
                    TimeToLive: -1,
                    Scale: 0.60,
                    HideHud: true,
                    Shadowing: true,
                    ShadowColor: Color.Black
                );

                // Pre-allocate 30 Minimap Marker Billboards (Zero GC allocations, 100% reliable)
                for (int i = 0; i < 30; i++)
                {
                    var mDot = new HudAPIv2.BillBoardHUDMessage(
                        Material: MATERIAL_MARKER_GPS,
                        Origin: minimapPosition,
                        BillBoardColor: Color.White,
                        Offset: Vector2D.Zero,
                        TimeToLive: -1,
                        Scale: 1.0,
                        Width: 0.010f,
                        Height: 0.010f * aspect,
                        HideHud: true,
                        Shadowing: false,
                        Blend: BlendTypeEnum.PostPP
                    );
                    mDot.Visible = false;
                    minimapMarkerPool.Add(mDot);
                }

                // Player arrow is registered AFTER the marker pool so HudAPI draws it on top.
                // This eliminates the per-frame delete/recreate hack that was causing GC pressure.
                minimapPlayerDot = new HudAPIv2.BillBoardHUDMessage(
                    Material: MATERIAL_NAV_ARROW,
                    Origin: minimapPosition,
                    BillBoardColor: Color.LimeGreen,
                    Offset: Vector2D.Zero,
                    TimeToLive: -1,
                    Scale: 1.0,
                    Width: 0.007f,
                    Height: 0.007f * aspect,
                    HideHud: true,
                    Shadowing: true,
                    Blend: BlendTypeEnum.PostPP
                );
                minimapPlayerDot.Visible = false;

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



                mapFooterMsg = new HudAPIv2.HUDMessage(
                    Message: mapFooterText,
                    Origin: new Vector2D(-0.68, -0.44),
                    Offset: Vector2D.Zero,
                    TimeToLive: -1,
                    Scale: 0.85,
                    HideHud: false,
                    Shadowing: true,
                    ShadowColor: Color.Black
                );
                mapFooterMsg.Visible = false;

                // Pre-allocate 50 Full Map Marker Billboards & Labels (Zero GC allocations)
                for (int i = 0; i < 50; i++)
                {
                    var sprite = new HudAPIv2.BillBoardHUDMessage(
                        Material: MATERIAL_MARKER_GPS,
                        Origin: Vector2D.Zero,
                        BillBoardColor: Color.White,
                        Offset: Vector2D.Zero,
                        TimeToLive: -1,
                        Scale: 1.0,
                        Width: 0.015f,
                        Height: 0.015f * aspect,
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

                // Player arrow registered AFTER the marker pool so HudAPI draws it on top.
                mapPlayerDot = new HudAPIv2.BillBoardHUDMessage(
                    Material: MATERIAL_NAV_ARROW,
                    Origin: Vector2D.Zero,
                    BillBoardColor: Color.LimeGreen,
                    Offset: Vector2D.Zero,
                    TimeToLive: -1,
                    Scale: 1.0,
                    Width: 0.012f,
                    Height: 0.012f * aspect,
                    HideHud: false,
                    Shadowing: true,
                    Blend: BlendTypeEnum.PostPP
                );
                mapPlayerDot.Visible = false;

                // 5. Register TextHUDAPI Mod Menu
                var rootCategory = new HudAPIv2.MenuRootCategory("GVK Navigation Suite", HudAPIv2.MenuRootCategory.MenuFlag.PlayerMenu, "GVK Navigation & Map Settings");
                new HudAPIv2.MenuItem("Toggle Tactical Map (Key: M)", rootCategory, () => { ToggleFullMap(); });
                new HudAPIv2.MenuItem("Toggle Corner Minimap", rootCategory, () => { ToggleMinimap(); });
                new HudAPIv2.MenuItem("Toggle Compass Tape", rootCategory, () => { ToggleCompass(); });
                new HudAPIv2.MenuItem("Toggle Zone Status Bar", rootCategory, () => { ToggleZoneBar(); });
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

            // Per-frame: Compass tape tracks head rotation so it must stay per-frame.
            // Minimap and zone bar throttled to every 10 frames (~6 Hz) — saves significant
            // per-frame work without any perceptible lag at normal rover speeds.
            if (hudApi != null && hudApi.Heartbeat)
            {
                UpdateCompassAndWaypoints(pos.Value);

                if (tickCounter % 10 == 0 || _refreshMinimapNextFrame)
                {
                    _refreshMinimapNextFrame = false;
                    UpdateZoneBar();
                    UpdateMinimap(pos.Value);
                }

                if (showFullMap)
                {
                    if (tickCounter % 10 == 0 || _fullMapNeedsRedraw)
                    {
                        _fullMapNeedsRedraw = false;
                        UpdateFullMap(pos.Value);
                    }
                    else if (mapPlayerDot != null)
                    {
                        // Keep player arrow rotation and blink responsive at 60 FPS while throttling waypoint/label plotting
                        mapPlayerDot.Rotation = -playerHeadingRad;
                        mapPlayerDot.BillBoardColor = (tickCounter % 20 < 10) ? Color.LimeGreen : Color.Cyan;
                    }
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
                        MapUV = WorldToMapUV(gps.Coords) // Pre-computed once; reused by minimap+map every frame.
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
                                MapUV = WorldToMapUV(blockPos)
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
                                MapUV = WorldToMapUV(blockPos)
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
            playerHeadingRad = compass;

            if (compassFrame == null || !showCompass || showFullMap)
            {
                if (compassFrame != null) compassFrame.Visible = false;
                HideTapePool();
                HideSpritePool();
                return;
            }

            compassFrame.Visible = true;
            // compass is already clamped to [0, 2π] above (line 1005-1006). Normalize to [-1, 1] for tape rendering.
            compass = (compass - (float)Math.PI) / (float)Math.PI;

            float FOV = camera.FovWithZoom;
            // Precompute FOV polynomial once per frame instead of calling Math.Pow in hot loops
            float fovCoeff = FOV * (5.596f * FOV * FOV - 18.43f * FOV + 16.16f);
            float fovCubic = FOV * 12f;

            // 2. Render Tape Characters (N, •, NE, E, etc.)
            int tapeMsgIndex = 0;
            for (int i = 0; i < COMPASS_TAPE.Length; i++)
            {
                var marker = COMPASS_TAPE[i];
                float offset = compass + marker.Offset;
                if (offset < -1f) offset += 2f;
                else if (offset > 1f) offset -= 2f;

                // Screen offset range is [-0.33, 0.33]. Skip polynomial math if offset is way outside the visible ribbon.
                if (Math.Abs(offset) > 0.35f) continue;

                float screenOffset = (fovCoeff * offset) + (fovCubic * offset * offset * offset);

                if (screenOffset > 0.33f || screenOffset < -0.33f) continue;
                if (tapeMsgIndex >= compassTapePool.Count) break;

                var msg = compassTapePool[tapeMsgIndex++];
                msg.Message.Clear().Append(marker.Label);
                msg.Origin = new Vector2D(screenOffset, 0.984f);

                // Cache text half-width on first measurement to avoid 900-1500 GetTextLength() calls/sec
                double halfWidth = marker.HalfWidth;
                if (halfWidth < 0)
                {
                    var charLen = msg.GetTextLength();
                    halfWidth = charLen.X * 0.5;
                    COMPASS_TAPE[i].HalfWidth = halfWidth;
                }

                msg.Offset = new Vector2D(-halfWidth, 0.0);
                msg.Visible = true;
            }

            for (int i = tapeMsgIndex; i < compassTapePool.Count; i++)
                compassTapePool[i].Visible = false;

            // 3. Render Graphical HUD Waypoints (Only targets marked as IsCompassTarget)
            int spriteIndex = 0;

            for (int i = 0; i < activeHudWaypoints.Count && spriteIndex < waypointSpritePool.Count; i++)
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

                if (Math.Abs(targetCompass) > 0.30f) continue;

                float poiScreenOffset = (fovCoeff * targetCompass) + (fovCubic * targetCompass * targetCompass * targetCompass);

                if (poiScreenOffset >= -0.31f && poiScreenOffset <= 0.31f)
                {
                    double distKm = wp.DistanceMeters * 0.001;
                    Color wpColor = wp.DisplayColor;

                    var sprite = waypointSpritePool[spriteIndex];
                    sprite.Material = wp.Sprite;
                    sprite.BillBoardColor = wpColor;
                    sprite.Origin = new Vector2D(poiScreenOffset, 0.970f);
                    sprite.Offset = Vector2D.Zero;
                    sprite.Visible = true;

                    // Zero-allocation distance text formatting: direct int/char appending avoids hundreds of heap string allocs per frame
                    var dist = waypointDistPool[spriteIndex];
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

                    dist.Origin = new Vector2D(poiScreenOffset, 0.948f);
                    var distLen = dist.GetTextLength();
                    dist.Offset = new Vector2D(-distLen.X * 0.5, 0.0);
                    dist.Visible = true;

                    spriteIndex++;
                }
            }

            for (int i = spriteIndex; i < waypointSpritePool.Count; i++)
            {
                waypointSpritePool[i].Visible = false;
                waypointDistPool[i].Visible = false;
            }
        }

        private void HideTapePool()
        {
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
        /// Updates the Zone Bar perfectly centered below the compass frame (Y = 0.895).
        /// </summary>
        private void UpdateZoneBar()
        {
            if (zoneMsg == null || !showZoneBar)
            {
                if (zoneBg != null) zoneBg.Visible = false;
                if (zoneAccent != null) zoneAccent.Visible = false;
                if (zoneMsg != null) zoneMsg.Visible = false;
                return;
            }

            // Dirty check: skip rebuilding StringBuilder and measuring text length if values haven't changed
            if (_lastZoneBarZoneIndex == currentZoneIndex &&
                Math.Abs(_lastZoneBarDistKm - lastDistKm) < 0.05 &&
                Math.Abs(_lastZoneBarRemainingKm - lastRemainingKm) < 0.05 &&
                Math.Abs(_lastZoneBarDistZ3Km - lastDistZ3Km) < 0.05)
            {
                zoneBg.Visible = true;
                zoneAccent.Visible = true;
                zoneMsg.Visible = true;
                return;
            }

            _lastZoneBarZoneIndex = currentZoneIndex;
            _lastZoneBarDistKm = lastDistKm;
            _lastZoneBarRemainingKm = lastRemainingKm;
            _lastZoneBarDistZ3Km = lastDistZ3Km;

            zoneText.Clear();
            switch (currentZoneIndex)
            {
                case 0:
                    zoneAccent.BillBoardColor = Color.LimeGreen;
                    zoneText.Append("<color=50,255,100>[ ZONE 0: SAFE HUB ]<color=255,255,255>  ")
                            .Append(lastDistKm.ToString("F1")).Append(" km to Crossroads | Z1 Frontier: ")
                            .Append(lastRemainingKm.ToString("F1")).Append(" km");
                    break;
                case 1:
                    zoneAccent.BillBoardColor = Color.DeepSkyBlue;
                    zoneText.Append("<color=60,200,255>[ ZONE 1: PVE FRONTIER ]<color=255,255,255>  ")
                            .Append(lastDistKm.ToString("F1")).Append(" km to Crossroads | PvP War: ")
                            .Append(lastRemainingKm.ToString("F1")).Append(" km");
                    break;
                case 2:
                    zoneAccent.BillBoardColor = Color.Orange;
                    zoneText.Append("<color=255,165,0>[ ZONE 2: CONTESTED (PVP) ]<color=255,255,255>  ")
                            .Append(lastDistKm.ToString("F1")).Append(" km to Crossroads | Z3 Deep: ")
                            .Append(lastRemainingKm.ToString("F1")).Append(" km");
                    break;
                default:
                    zoneAccent.BillBoardColor = Color.Red;
                    zoneText.Append("<color=255,50,50>[ ZONE 3: GAALSIEN HEART ]<color=255,255,255>  ")
                            .Append(lastDistKm.ToString("F1")).Append(" km to Crossroads | Z3 Core: ")
                            .Append(lastDistZ3Km.ToString("F1")).Append(" km");
                    break;
            }

            zoneBg.Visible = true;
            zoneAccent.Visible = true;
            zoneMsg.Visible = true;

            Vector2D len = zoneMsg.GetTextLength();
            zoneMsg.Origin = new Vector2D(0.0, 0.895);
            zoneMsg.Offset = new Vector2D(-len.X * 0.5, 0.0);
        }

        /// <summary>
        /// Updates the corner minimap using pre-allocated billboard pool (Zero GC allocations).
        /// Player arrow is drawn on top of markers because it was registered after the pool in OnHudApiRegistered.
        /// </summary>
        private void UpdateMinimap(Vector3D playerPos)
        {
            if (minimapTerrain == null || !showMinimap || showFullMap)
            {
                if (minimapBg != null) minimapBg.Visible = false;
                if (minimapTerrain != null) minimapTerrain.Visible = false;
                if (minimapPlayerDot != null) minimapPlayerDot.Visible = false;
                if (minimapLabel != null) minimapLabel.Visible = false;
                HideMinimapPool();
                return;
            }

            minimapBg.Visible = true;
            minimapTerrain.Visible = true;
            minimapLabel.Visible = true;

            // Map UV position of player relative to center of minimap box
            Vector2 uv = WorldToMapUV(playerPos);
            Vector2D dotOffset = new Vector2D(
                (uv.X - 0.5) * minimapSize.X,
                (0.5 - uv.Y) * minimapSize.Y
            );

            // Render all active waypoints using pre-allocated pool
            int mIdx = 0;
            for (int i = 0; i < activeHudWaypoints.Count && mIdx < minimapMarkerPool.Count; i++)
            {
                var wp = activeHudWaypoints[i];
                Vector2 wpUV = wp.MapUV;
                Vector2D wpOffset = new Vector2D(
                    (wpUV.X - 0.5) * minimapSize.X,
                    (0.5 - wpUV.Y) * minimapSize.Y
                );

                var icon = minimapMarkerPool[mIdx++];
                icon.Material = wp.Sprite;
                icon.BillBoardColor = wp.DisplayColor;
                icon.Offset = wpOffset;
                icon.Visible = true;
            }

            // Hide unused pool slots
            for (int i = mIdx; i < minimapMarkerPool.Count; i++)
                minimapMarkerPool[i].Visible = false;

            // Update player arrow — no delete/recreate needed; registered after pool so it renders on top.
            minimapPlayerDot.Offset = dotOffset;
            minimapPlayerDot.Rotation = -playerHeadingRad;
            minimapPlayerDot.BillBoardColor = (tickCounter % 30 < 15) ? Color.LimeGreen : Color.Yellow;
            minimapPlayerDot.Visible = true;
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
            mapFooterMsg.Visible = true;

            Vector2 uv = WorldToMapUV(playerPos);
            Vector2D playerOffset = new Vector2D(
                (uv.X - 0.5) * fullMapWidth,
                (0.5 - uv.Y) * fullMapHeight
            );
            mapPlayerDot.Offset = playerOffset;
            mapPlayerDot.BillBoardColor = (tickCounter % 20 < 10) ? Color.LimeGreen : Color.Cyan;

            // Plot all active waypoints using pre-allocated pool
            int fIdx = 0;
            for (int i = 0; i < activeHudWaypoints.Count && fIdx < fullMapMarkerPool.Count; i++)
            {
                var wp = activeHudWaypoints[i];
                Vector2 wpUV = wp.MapUV;
                Vector2D wpOffset = new Vector2D(
                    (wpUV.X - 0.5) * fullMapWidth,
                    (0.5 - wpUV.Y) * fullMapHeight
                );

                var sprite = fullMapMarkerPool[fIdx];
                sprite.Material = wp.Sprite;
                sprite.BillBoardColor = wp.DisplayColor;
                sprite.Offset = wpOffset;
                sprite.Visible = true;

                var label = fullMapLabelPool[fIdx];
                label.Message.Clear().Append(wp.Name);
                label.Offset = wpOffset + new Vector2D(0.012, 0.005);
                label.Visible = true;

                fIdx++;
            }

            for (int i = fIdx; i < fullMapMarkerPool.Count; i++)
            {
                fullMapMarkerPool[i].Visible = false;
                fullMapLabelPool[i].Visible = false;
            }

            // Ensure player arrow is drawn on top of map markers and rotates with heading
            mapPlayerDot.Rotation = -playerHeadingRad;
            mapPlayerDot.Visible = true;

            // Update footer without string allocations
            int distWhole = (int)lastDistKm;
            int distTenths = (int)((lastDistKm - distWhole) * 10.0);

            mapFooterText.Clear();
            mapFooterText.Append("<color=255,220,0>KHARAK TACTICAL SATELLITE MAP<color=255,255,255> | Current Sector: ")
                         .Append(GetZoneName(currentZoneIndex)).Append(" | Distance to Crossroads: ")
                         .Append(distWhole).Append('.').Append(distTenths)
                         .Append(" km | Press [M] or [ESC] to Close");
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

            if (msg.Equals("/minimap", StringComparison.OrdinalIgnoreCase) ||
                msg.Equals("/zone minimap", StringComparison.OrdinalIgnoreCase))
            {
                sendToOthers = false;
                ToggleMinimap();
                return;
            }

            if (msg.Equals("/compass", StringComparison.OrdinalIgnoreCase) ||
                msg.Equals("/zone compass", StringComparison.OrdinalIgnoreCase))
            {
                sendToOthers = false;
                ToggleCompass();
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
                if (mapFooterMsg != null) mapFooterMsg.Visible = false;
                HideFullMapPool();
                _refreshMinimapNextFrame = true;
            }
            else
            {
                _fullMapNeedsRedraw = true;

                // Immediately hide compass and minimap elements when pulling up the satellite map
                if (compassFrame != null) compassFrame.Visible = false;
                HideTapePool();
                HideSpritePool();

                if (minimapBg != null) minimapBg.Visible = false;
                if (minimapTerrain != null) minimapTerrain.Visible = false;
                if (minimapPlayerDot != null) minimapPlayerDot.Visible = false;
                if (minimapLabel != null) minimapLabel.Visible = false;
                HideMinimapPool();
            }
        }

        private void ToggleMinimap()
        {
            showMinimap = !showMinimap;
            string status = showMinimap ? "ENABLED" : "DISABLED";
            MyAPIGateway.Utilities.ShowNotification($"[GVK NAV] Corner Minimap: {status}", 2500, showMinimap ? MyFontEnum.Green : MyFontEnum.Red);
        }

        private void ToggleCompass()
        {
            showCompass = !showCompass;
            string status = showCompass ? "ENABLED" : "DISABLED";
            MyAPIGateway.Utilities.ShowNotification($"[GVK NAV] Compass Ribbon: {status}", 2500, showCompass ? MyFontEnum.Green : MyFontEnum.Red);
        }

        private void ToggleZoneBar()
        {
            showZoneBar = !showZoneBar;
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
            _missionSb.AppendLine("• /zone gps - Restore default Kharak GPS waypoints");

            MyAPIGateway.Utilities.ShowMissionScreen(
                screenTitle: "DESERTS OF KHARAK — ZONE DIRECTORY",
                currentObjectivePrefix: "Reference Guide:",
                currentObjective: "Planetary Zone Boundaries & Governance Matrix",
                screenDescription: _missionSb.ToString(),
                callback: null,
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