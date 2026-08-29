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
    /// - Aspect-ratio corrected billboard scaling (no more height-squished icons or maps on widescreen/ultrawide).
    /// - Calibrated WorldToMapUV projection aligned to KharakMap.dds longitude (-91.4° meridian shift) & inverted latitude.
    /// - Displays ONLY waypoints set to 'Show on HUD' and active radio broadcast signals (supports Unknown Signals).
    /// - Pinpoint Antenna/Beacon tracking: points directly at the physical block itself with no close-distance dropouts.
    /// - High-readability distance badges centered dynamically below each POI.
    /// - Perfectly centered Zone Status Bar below the compass.
    /// - Live Corner Minimap (top-right, true 2:1 ratio) with accurate UV player and GPS blips.
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

        // Compass Tape Definition
        private struct CompassMarker
        {
            public string Label;
            public float Offset;
            public CompassMarker(string label, float offset)
            {
                Label = label;
                Offset = offset;
            }
        }

        private static readonly List<CompassMarker> COMPASS_TAPE = new List<CompassMarker>()
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

        // Active HUD Waypoint (GPS with ShowOnHud = true, or real broadcast signal)
        private struct ActiveHudWaypoint
        {
            public MyStringId Sprite;
            public string Name;
            public Vector3D Coords;
            public Color DisplayColor;
            public double DistanceMeters;
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
        private List<HudAPIv2.BillBoardHUDMessage> minimapPoints = new List<HudAPIv2.BillBoardHUDMessage>();
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
        private List<HudAPIv2.BillBoardHUDMessage> fullMapMarkers = new List<HudAPIv2.BillBoardHUDMessage>();
        private List<HudAPIv2.HUDMessage> fullMapLabels = new List<HudAPIv2.HUDMessage>();

        // Dynamic State
        private int currentZoneIndex = 0;
        private double lastDistKm = 0.0;
        private double lastRemainingKm = 0.0;
        private double lastDistZ3Km = 0.0;
        private int tickCounter = 0;
        private bool hasCheckedDefaultGps = false;

        // Active HUD Waypoints Buffer
        private List<ActiveHudWaypoint> activeHudWaypoints = new List<ActiveHudWaypoint>();
        private List<IMyGps> cachedGpsList = new List<IMyGps>();
        private HashSet<IMyEntity> entityBuffer = new HashSet<IMyEntity>();
        private List<IMySlimBlock> blockBuffer = new List<IMySlimBlock>();

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
                    ClearMinimapPoints();

                    mapDimmer?.DeleteMessage();
                    mapFrame?.DeleteMessage();
                    mapTerrain?.DeleteMessage();
                    mapPlayerDot?.DeleteMessage();
                    mapFooterMsg?.DeleteMessage();
                    ClearFullMapElements();

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

        private void ClearMinimapPoints()
        {
            for (int i = 0; i < minimapPoints.Count; i++)
                minimapPoints[i]?.DeleteMessage();
            minimapPoints.Clear();
        }

        private void ClearFullMapElements()
        {
            for (int i = 0; i < fullMapMarkers.Count; i++)
                fullMapMarkers[i]?.DeleteMessage();
            fullMapMarkers.Clear();

            for (int i = 0; i < fullMapLabels.Count; i++)
                fullMapLabels[i]?.DeleteMessage();
            fullMapLabels.Clear();
        }

        /// <summary>
        /// Gets the current monitor aspect ratio (Width / Height) so 2D billboards render un-squished.
        /// </summary>
        private float GetScreenAspect()
        {
            var camera = MyAPIGateway.Session.Camera;
            if (camera != null && camera.ViewportSize.Y > 0)
                return camera.ViewportSize.X / camera.ViewportSize.Y;
            return 16f / 9f;
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

                // Pre-allocate 35 Tape character HUDMessages
                for (int i = 0; i < 35; i++)
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
                float iconHeight = iconWidth * aspect; // Un-squished 1:1 square!

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
                float mHeight = (mWidth * 0.5f) * aspect; // True uncompressed 2:1 map!
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

                minimapPlayerDot = new HudAPIv2.BillBoardHUDMessage(
                    Material: MATERIAL_SQUARE,
                    Origin: minimapPosition,
                    BillBoardColor: Color.LimeGreen,
                    Offset: Vector2D.Zero,
                    TimeToLive: -1,
                    Scale: 1.0,
                    Width: 0.007f,
                    Height: 0.007f * aspect, // Aspect-corrected square dot!
                    HideHud: true,
                    Shadowing: true,
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
                float fullMapHeight = (fullMapWidth * 0.5f) * aspect; // True 2:1 aspect ratio!

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

                mapPlayerDot = new HudAPIv2.BillBoardHUDMessage(
                    Material: MATERIAL_SQUARE,
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

            // 30-Tick (0.5s) Background Scan for HUD-visible signals & GPS
            if (tickCounter % 30 == 0)
            {
                UpdateBackgroundData(pos.Value);
            }

            // Per-frame UI update
            if (hudApi != null && hudApi.Heartbeat)
            {
                UpdateCompassAndWaypoints(pos.Value);
                UpdateZoneBar();
                UpdateMinimap(pos.Value);

                if (showFullMap)
                {
                    UpdateFullMap(pos.Value);
                }
            }
        }

        private void CheckAndPopulateDefaultGps()
        {
            var player = MyAPIGateway.Session.Player;
            if (player == null) return;
            hasCheckedDefaultGps = true;

            List<IMyGps> playerGps = new List<IMyGps>();
            MyAPIGateway.Session.GPS.GetGpsList(player.IdentityId, playerGps);

            bool hasZone0 = false;
            bool hasZone3 = false;
            for (int i = 0; i < playerGps.Count; i++)
            {
                if (playerGps[i].Name.Equals("Zone 0", StringComparison.OrdinalIgnoreCase)) hasZone0 = true;
                if (playerGps[i].Name.Equals("Zone 3", StringComparison.OrdinalIgnoreCase)) hasZone3 = true;
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

            List<IMyGps> playerGps = new List<IMyGps>();
            MyAPIGateway.Session.GPS.GetGpsList(player.IdentityId, playerGps);

            int addedCount = 0;
            for (int i = 0; i < DEFAULT_KHARAK_GPS.Length; i++)
            {
                var entry = DEFAULT_KHARAK_GPS[i];
                bool exists = false;
                for (int j = 0; j < playerGps.Count; j++)
                {
                    if (playerGps[j].Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase))
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
        /// Scans ONLY player-visible HUD items:
        /// 1. GPS coordinates where ShowOnHud == true -> marker_gps.
        /// 2. Active broadcasting antennas / beacons where broadcast radius reaches the player -> marker_friendly, marker_enemy, marker_neutral, marker_self.
        /// Points with pinpoint precision directly to the physical antenna/beacon block itself, with NO close-distance dropouts.
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

            // 1. Scan Player GPS entries (ONLY those set to ShowOnHud == true) -> Always Keen marker_gps
            cachedGpsList.Clear();
            var playerId = MyAPIGateway.Session.Player?.IdentityId ?? 0;
            if (playerId != 0)
            {
                MyAPIGateway.Session.GPS.GetGpsList(playerId, cachedGpsList);

                for (int i = 0; i < cachedGpsList.Count; i++)
                {
                    var gps = cachedGpsList[i];
                    if (!gps.ShowOnHud) continue; // Respect player HUD toggle!

                    double dist = Vector3D.Distance(playerPos, gps.Coords);

                    activeHudWaypoints.Add(new ActiveHudWaypoint
                    {
                        Name = gps.Name,
                        Coords = gps.Coords,
                        Sprite = MATERIAL_MARKER_GPS,
                        DisplayColor = gps.GPSColor, // Exact HUD color!
                        DistanceMeters = dist
                    });
                }
            }

            // 2. Scan Real In-Range Radio Broadcasts (Beacons & Antennas that actually reach player HUD)
            entityBuffer.Clear();
            MyAPIGateway.Entities.GetEntities(entityBuffer, e => e is IMyCubeGrid);

            var controlled = MyAPIGateway.Session.ControlledObject?.Entity;
            var playerGrid = (controlled as IMyCubeBlock)?.CubeGrid ?? (controlled as IMyCubeGrid);
            long localPlayerId = MyAPIGateway.Session.Player?.IdentityId ?? 0;

            foreach (var ent in entityBuffer)
            {
                var grid = ent as IMyCubeGrid;
                if (grid == null || grid.MarkedForClose) continue;

                // Never track own vehicle construct when actively controlling it
                if (playerGrid != null && (grid == playerGrid || grid.IsSameConstructAs(playerGrid)))
                    continue;

                // Scan broadcasting blocks on this grid
                blockBuffer.Clear();
                grid.GetBlocks(blockBuffer, b => b.FatBlock is IMyBeacon || b.FatBlock is IMyRadioAntenna);

                long gridOwner = grid.BigOwners.Count > 0 ? grid.BigOwners[0] : 0;
                var relation = MyAPIGateway.Session.Player.GetRelationTo(gridOwner);
                bool isOwner = (gridOwner == localPlayerId && localPlayerId != 0);
                bool isNpc = gridOwner == 0;
                if (!isNpc)
                {
                    var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(gridOwner);
                    if (faction != null && faction.IsEveryoneNpc()) isNpc = true;
                }

                // Determine Keen HUD relation marker and color
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
                        Vector3D blockPos = beacon.GetPosition(); // Exact block coordinates!
                        double blockDist = Vector3D.Distance(playerPos, blockPos);

                        if (beacon.Radius >= blockDist && blockDist >= 1.0)
                        {
                            string bName = string.IsNullOrEmpty(beacon.CustomName) ? grid.CustomName : beacon.CustomName;
                            activeHudWaypoints.Add(new ActiveHudWaypoint
                            {
                                Name = bName,
                                Coords = blockPos,
                                Sprite = markerSprite,
                                DisplayColor = signalColor,
                                DistanceMeters = blockDist
                            });
                            break; // 1 primary signal per grid
                        }
                    }

                    var antenna = fat as IMyRadioAntenna;
                    if (antenna != null && antenna.IsWorking && antenna.Enabled && antenna.EnableBroadcasting)
                    {
                        Vector3D blockPos = antenna.GetPosition(); // Exact block coordinates!
                        double blockDist = Vector3D.Distance(playerPos, blockPos);

                        if (antenna.Radius >= blockDist && blockDist >= 1.0)
                        {
                            string aName = string.IsNullOrEmpty(antenna.CustomName) ? grid.CustomName : antenna.CustomName;
                            activeHudWaypoints.Add(new ActiveHudWaypoint
                            {
                                Name = aName,
                                Coords = blockPos,
                                Sprite = markerSprite,
                                DisplayColor = signalColor,
                                DistanceMeters = blockDist
                            });
                            break; // 1 primary signal per grid
                        }
                    }
                }
            }

            // Sort so closest targets are rendered first
            activeHudWaypoints.Sort((a, b) => a.DistanceMeters.CompareTo(b.DistanceMeters));
        }

        /// <summary>
        /// Updates the HUD Compass tape and projects graphical POI sprites (Keen marker_gps and relation markers).
        /// Properly centers tape and icons between the curved compass bars.
        /// Fixed relative azimuth math: target straight ahead centers under needle; heading towards it decreases distance.
        /// </summary>
        private void UpdateCompassAndWaypoints(Vector3D playerPos)
        {
            if (compassFrame == null || !showCompass)
            {
                if (compassFrame != null) compassFrame.Visible = false;
                HideTapePool();
                HideSpritePool();
                return;
            }

            var camera = MyAPIGateway.Session.Camera;
            if (camera == null) return;

            compassFrame.Visible = true;

            // 1. Calculate Player Relative North and Pitch/Roll Correction
            Vector3D relativePlayerPos = playerPos - PLANET_CENTER;
            Vector3 relativePlayerPosNormal = (Vector3)(relativePlayerPos / relativePlayerPos.Length());

            Vector3D relativeNorth = PLANET_CENTER + new Vector3D(0f, PLANET_RADIUS, 0f);
            Vector3D side1 = relativeNorth - playerPos;
            Vector3D side2 = PLANET_CENTER - playerPos;
            Vector3D cross = Vector3D.Cross(side1, side2);
            Vector3D playerRelativeNorth = Vector3D.Cross(cross, side2);
            Vector3 playerRelativeNorthNormal = (Vector3)(playerRelativeNorth / playerRelativeNorth.Length());

            Matrix relativeOffset;
            Vector3 baseSouth = BASE_SOUTH;
            Matrix.CreateRotationFromTwoVectors(ref relativePlayerPosNormal, ref baseSouth, out relativeOffset);

            Vector3 forwardCorrected = Vector3.Transform(camera.WorldMatrix.Forward, relativeOffset);
            Vector3 northCorrected = Vector3.Transform(playerRelativeNorthNormal, relativeOffset);

            float playerAzimuth = 0f, playerElev = 0f;
            Vector3.GetAzimuthAndElevation(forwardCorrected, out playerAzimuth, out playerElev);

            float northAzimuth = 0f, northElev = 0f;
            Vector3.GetAzimuthAndElevation(northCorrected, out northAzimuth, out northElev);

            float compass = (playerAzimuth + (float)Math.PI) - (northAzimuth + (float)Math.PI);
            if (compass < 0) compass += (float)Math.PI * 2f;
            else if (compass > (float)Math.PI * 2f) compass -= (float)Math.PI * 2f;
            compass = (compass - (float)Math.PI) / (float)Math.PI;

            float FOV = camera.FovWithZoom;

            // 2. Render Tape Characters (N, •, NE, E, etc.)
            // Baseline 0.984f with Offset.Y = 0f places the font directly between the compass bars
            int tapeMsgIndex = 0;
            for (int i = 0; i < COMPASS_TAPE.Count; i++)
            {
                var marker = COMPASS_TAPE[i];
                float offset = compass + marker.Offset;
                if (offset < -1f) offset += 2f;
                else if (offset > 1f) offset -= 2f;

                float screenOffset = (FOV * (5.596f * (float)Math.Pow(FOV, 2) - 18.43f * FOV + 16.16f) * offset) + (FOV * 12f * (float)Math.Pow(offset, 3));

                if (screenOffset > 0.33f || screenOffset < -0.33f) continue;
                if (tapeMsgIndex >= compassTapePool.Count) break;

                var msg = compassTapePool[tapeMsgIndex++];
                msg.Message.Clear().Append(marker.Label);
                msg.Origin = new Vector2D(screenOffset, 0.984f);
                var charLen = msg.GetTextLength();
                msg.Offset = new Vector2D(-charLen.X * 0.5, 0.0); // Keep Y offset 0 so font baseline is centered between bars
                msg.Visible = true;
            }

            for (int i = tapeMsgIndex; i < compassTapePool.Count; i++)
                compassTapePool[i].Visible = false;

            // 3. Render Graphical HUD Waypoints (Only ShowOnHud GPS & Real In-Range Radio Signals)
            int spriteIndex = 0;

            for (int i = 0; i < activeHudWaypoints.Count && spriteIndex < waypointSpritePool.Count; i++)
            {
                var wp = activeHudWaypoints[i];
                Vector3 toWp = (Vector3)(wp.Coords - playerPos);
                Vector3 toWpCorrected = Vector3.Transform(toWp, relativeOffset);

                // Compute bearing azimuth on the planetary tangent plane
                float wpAzimuth = 0f, wpElev = 0f;
                Vector3.GetAzimuthAndElevation(toWpCorrected, out wpAzimuth, out wpElev);

                // Correct forward bearing diff in VRageMath convention:
                // When looking straight at target, diff is 0; when target is right, diff is positive; when left, diff is negative
                float angleDiff = playerAzimuth - wpAzimuth;
                while (angleDiff > (float)Math.PI) angleDiff -= (float)(Math.PI * 2.0);
                while (angleDiff < -(float)Math.PI) angleDiff += (float)(Math.PI * 2.0);

                float targetCompass = angleDiff / (float)Math.PI;

                // Only display if the bearing is within the forward ribbon window
                if (Math.Abs(targetCompass) > 0.30f) continue;

                float poiScreenOffset = (FOV * (5.596f * (float)Math.Pow(FOV, 2) - 18.43f * FOV + 16.16f) * targetCompass) + (FOV * 12f * (float)Math.Pow(targetCompass, 3));

                if (poiScreenOffset >= -0.31f && poiScreenOffset <= 0.31f)
                {
                    double distKm = wp.DistanceMeters / 1000.0;
                    string distStr = distKm < 10.0 ? $"{distKm:F1}k" : $"{(int)distKm}k";

                    Color wpColor = wp.DisplayColor;

                    var sprite = waypointSpritePool[spriteIndex];
                    sprite.Material = wp.Sprite;
                    sprite.BillBoardColor = wpColor;
                    sprite.Origin = new Vector2D(poiScreenOffset, 0.970f); // Centered vertically between the bars
                    sprite.Offset = Vector2D.Zero;
                    sprite.Visible = true;

                    var dist = waypointDistPool[spriteIndex];
                    dist.Message.Clear().Append($"<color={wpColor.R},{wpColor.G},{wpColor.B}>").Append(distStr);
                    dist.Origin = new Vector2D(poiScreenOffset, 0.948f); // Sits cleanly below the lower bar
                    var distLen = dist.GetTextLength();
                    dist.Offset = new Vector2D(-distLen.X * 0.5, 0.0);
                    dist.Visible = true;

                    spriteIndex++;
                }
            }

            // Hide unused sprite and dist messages
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
        /// Dynamic text centering ensures clean, professional telemetry formatting.
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

            // Center text dynamically within the zone bar
            Vector2D len = zoneMsg.GetTextLength();
            zoneMsg.Origin = new Vector2D(0.0, 0.895);
            zoneMsg.Offset = new Vector2D(-len.X * 0.5, 0.0);
        }

        /// <summary>
        /// Updates the corner minimap with terrain texture, player position blip, and nearby GPS dots.
        /// Uses proportional true 2:1 projection matching KharakMap.dds.
        /// </summary>
        private void UpdateMinimap(Vector3D playerPos)
        {
            if (minimapTerrain == null || !showMinimap)
            {
                if (minimapBg != null) minimapBg.Visible = false;
                if (minimapTerrain != null) minimapTerrain.Visible = false;
                if (minimapPlayerDot != null) minimapPlayerDot.Visible = false;
                if (minimapLabel != null) minimapLabel.Visible = false;
                ClearMinimapPoints();
                return;
            }

            minimapBg.Visible = true;
            minimapTerrain.Visible = true;
            minimapPlayerDot.Visible = true;
            minimapLabel.Visible = true;

            float aspect = GetScreenAspect();

            // Map UV position of player relative to center of minimap box
            Vector2 uv = WorldToMapUV(playerPos);
            Vector2D dotOffset = new Vector2D(
                (uv.X - 0.5) * minimapSize.X,
                (0.5 - uv.Y) * minimapSize.Y
            );
            minimapPlayerDot.Offset = dotOffset;
            minimapPlayerDot.BillBoardColor = (tickCounter % 30 < 15) ? Color.LimeGreen : Color.Yellow;

            // Render in-range GPS points on minimap (ONLY ShowOnHud)
            ClearMinimapPoints();
            for (int i = 0; i < cachedGpsList.Count && i < 15; i++)
            {
                var gps = cachedGpsList[i];
                if (!gps.ShowOnHud) continue;
                if (Vector3D.Distance(playerPos, gps.Coords) > 40000.0) continue;

                Vector2 gpsUV = WorldToMapUV(gps.Coords);
                Vector2D gpsOffset = new Vector2D(
                    (gpsUV.X - 0.5) * minimapSize.X,
                    (0.5 - gpsUV.Y) * minimapSize.Y
                );

                var dot = new HudAPIv2.BillBoardHUDMessage(
                    Material: MATERIAL_MARKER_GPS,
                    Origin: minimapPosition,
                    BillBoardColor: gps.GPSColor,
                    Offset: gpsOffset,
                    TimeToLive: 1,
                    Scale: 1.0,
                    Width: 0.006f,
                    Height: 0.006f * aspect, // Aspect-corrected square dot!
                    HideHud: true,
                    Shadowing: false,
                    Blend: BlendTypeEnum.PostPP
                );
                minimapPoints.Add(dot);
            }
        }

        private void UpdateFullMap(Vector3D playerPos)
        {
            if (mapTerrain == null || !showFullMap) return;

            float aspect = GetScreenAspect();
            float fullMapWidth = 1.40f;
            float fullMapHeight = (fullMapWidth * 0.5f) * aspect;

            mapDimmer.Visible = true;
            mapFrame.Visible = true;
            mapTerrain.Visible = true;
            mapPlayerDot.Visible = true;
            mapFooterMsg.Visible = true;

            Vector2 uv = WorldToMapUV(playerPos);
            Vector2D playerOffset = new Vector2D(
                (uv.X - 0.5) * fullMapWidth,
                (0.5 - uv.Y) * fullMapHeight
            );
            mapPlayerDot.Offset = playerOffset;
            mapPlayerDot.BillBoardColor = (tickCounter % 20 < 10) ? Color.LimeGreen : Color.Cyan;

            ClearFullMapElements();

            // Plot Active HUD Waypoints on Full Map with authentic Keen markers
            for (int i = 0; i < activeHudWaypoints.Count && i < 30; i++)
            {
                var wp = activeHudWaypoints[i];
                Vector2 wpUV = WorldToMapUV(wp.Coords);
                Vector2D wpOffset = new Vector2D(
                    (wpUV.X - 0.5) * fullMapWidth,
                    (0.5 - wpUV.Y) * fullMapHeight
                );

                var sprite = new HudAPIv2.BillBoardHUDMessage(
                    Material: wp.Sprite,
                    Origin: Vector2D.Zero,
                    BillBoardColor: wp.DisplayColor,
                    Offset: wpOffset,
                    TimeToLive: 1,
                    Scale: 1.0,
                    Width: 0.014f,
                    Height: 0.014f * aspect, // Aspect-corrected square icon!
                    HideHud: false,
                    Blend: BlendTypeEnum.PostPP
                );
                fullMapMarkers.Add(sprite);

                var label = new HudAPIv2.HUDMessage(
                    Message: new StringBuilder(wp.Name),
                    Origin: Vector2D.Zero,
                    Offset: wpOffset + new Vector2D(0.012, 0.005),
                    TimeToLive: 1,
                    Scale: 0.60,
                    HideHud: false,
                    Shadowing: true,
                    ShadowColor: Color.Black
                );
                fullMapLabels.Add(label);
            }

            // Update footer
            mapFooterText.Clear();
            mapFooterText.Append("<color=255,220,0>KHARAK TACTICAL SATELLITE MAP<color=255,255,255> | Current Sector: ")
                         .Append(GetZoneName(currentZoneIndex)).Append(" | Distance to Crossroads: ")
                         .Append(lastDistKm.ToString("F1")).Append(" km | Press [M] or [ESC] to Close");
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
                ClearFullMapElements();
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
            StringBuilder sb = new StringBuilder();

            sb.AppendLine($"CURRENT SECTOR: {GetZoneName(currentZoneIndex).ToUpper()}");
            sb.AppendLine("--------------------------------------------------");
            sb.AppendLine($"• Distance from Crossroads Tower: {lastDistKm:F1} km");
            if (currentZoneIndex < 3)
                sb.AppendLine($"• Next Sector Transition: {lastRemainingKm:F1} km ahead");
            else
                sb.AppendLine($"• Distance to Z3 Antipode Core: {lastDistZ3Km:F1} km");
            sb.AppendLine();
            sb.AppendLine("COMBAT & GOVERNANCE RULES:");
            if (currentZoneIndex <= 1)
            {
                sb.AppendLine("• Strict PvE Region: Player-vs-player damage is zeroed out.");
                sb.AppendLine("• Hostile NPC wrecks can be ground with upgraded/ship grinders.");
                sb.AppendLine("• Shield Generators: 100% NON-SIEGABLE.");
            }
            else
            {
                sb.AppendLine("• FULL PVP WARFARE UNLOCKED.");
                sb.AppendLine("• Full production and upgrades permitted.");
                sb.AppendLine("• Shield Generators: SIEGABLE via Siege Drives.");
            }
            sb.AppendLine("--------------------------------------------------");
            sb.AppendLine("Controls: Press [M] for Map | /minimap | /compass | /zone gps");

            MyAPIGateway.Utilities.ShowMissionScreen(
                screenTitle: title,
                currentObjectivePrefix: objectivePrefix,
                currentObjective: currentObjective,
                screenDescription: sb.ToString(),
                callback: null,
                okButtonCaption: "Close"
            );
        }

        private void OpenAllZonesMissionScreen()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("DESERTS OF KHARAK — PLANETARY ZONE DIRECTORY");
            sb.AppendLine("All zone distances measured straight-line from Crossroads Tower:");
            sb.AppendLine("==================================================");
            sb.AppendLine("• Zone 0 (0 – 20 km): Safe Starter Hub | Strict PvE | Basic Prod Only | Shields Non-Siegable");
            sb.AppendLine("• Zone 1 (20 – 35 km): PvE & Salvage | Strict PvE | Weapons/Drills/Grinders Enabled");
            sb.AppendLine("• Zone 2 (35 – 50 km): Contested Desert | Full PvP | Large Prod Unlocked | Shields Siegable");
            sb.AppendLine("• Zone 3 (> 50 km): Deep Desert | High-Threat PvPvE | Ancient Relics | Battlecruisers");
            sb.AppendLine("==================================================");
            sb.AppendLine("Hotkeys & Commands:");
            sb.AppendLine("• Press [M] to toggle Full Satellite Map");
            sb.AppendLine("• /minimap - Toggle live top-right minimap");
            sb.AppendLine("• /compass - Toggle heading tape");
            sb.AppendLine("• /zone hud - Toggle zone status bar");
            sb.AppendLine("• /zone gps - Restore default Kharak GPS waypoints");

            MyAPIGateway.Utilities.ShowMissionScreen(
                screenTitle: "DESERTS OF KHARAK — ZONE DIRECTORY",
                currentObjectivePrefix: "Reference Guide:",
                currentObjective: "Planetary Zone Boundaries & Governance Matrix",
                screenDescription: sb.ToString(),
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