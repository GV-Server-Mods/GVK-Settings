using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Input;
using VRage.Utils;
using VRageMath;

namespace GVK.Navigation
{
    /// <summary>
    /// The /gvk settings screen (MyModScreen, SE 1.211): every navigation setting in a game screen used with
    /// the mouse (it replaced Text HUD API's F2 menu). Opened with /gvk settings or the map's Settings button.
    /// GVK_ZoneNavCommand does the layout, input and drawing; this class only forwards the screen's callbacks.
    /// </summary>
    public class GvkSettingsScreen : MyModScreen
    {
        private readonly GVK_ZoneNavCommand _owner;

        public GvkSettingsScreen(GVK_ZoneNavCommand owner) : base("GVK Navigation Settings")
        {
            _owner = owner;
        }

        protected override void OnHandleInput()
        {
            _owner.HandleSettingsInput();
        }

        protected override bool OnCancelling()
        {
            return _owner.OnSettingsCancelling();
        }

        protected override void OnDraw(float transitionAlpha)
        {
            _owner.DrawSettingsScreen(transitionAlpha);
        }

        protected override void OnClosed(bool isUnloading)
        {
            _owner.OnSettingsClosed(isUnloading);
        }
    }

    public partial class GVK_ZoneNavCommand
    {
        // Row ids. Each tab lists its rows in order; labels, values and actions switch on the id.
        private const int S_OPEN_MAP = 1, S_MINIMAP = 2, S_COMPASS = 3, S_ZONE_BAR = 4;
        private const int S_MM_MODE = 10, S_MM_RANGE = 11, S_MM_SIZE = 12, S_MM_ZONE_LINES = 13, S_MM_FOV = 14;
        private const int S_MM_GPS = 15, S_MM_SIGNALS = 16, S_MM_OWN = 17, S_MM_KOTH = 18, S_MM_SNAP = 19;
        private const int S_MM_X = 20, S_MM_Y = 21, S_MM_RESET = 22;
        private const int S_C_TAPE = 30, S_C_GPS = 31, S_C_SIGNALS = 32, S_C_OWN = 33, S_C_KOTH = 34, S_C_SIZE = 35;
        private const int S_C_SNAP = 36, S_C_X = 37, S_C_Y = 38, S_C_RESET = 39;
        private const int S_MAP_OWN = 50, S_MAP_KOTH = 51, S_MAP_GPS_LABELS = 52, S_MAP_SIGNAL_LABELS = 53;
        private const int S_MAP_STACK = 54, S_MAP_KEY = 55;
        private const int S_RATE = 70, S_DEFAULT_GPS = 72, S_FACTORY_RESET = 73;
        private const int HIT_CLOSE = -1;       // Hit codes below zero: the Close button, then the tabs
        private const int HIT_TAB = -100;       // HIT_TAB - tab index

        private static readonly string[] SETTINGS_TABS = { "General", "Radar & Minimap", "Compass", "Satellite Map", "Performance" };
        private static readonly int[][] SETTINGS_ROWS =
        {
            new[] { S_OPEN_MAP, S_MINIMAP, S_COMPASS, S_ZONE_BAR },
            new[] { S_MM_MODE, S_MM_RANGE, S_MM_SIZE, S_MM_ZONE_LINES, S_MM_FOV, S_MM_GPS, S_MM_SIGNALS, S_MM_OWN, S_MM_KOTH,
                    S_MM_SNAP, S_MM_X, S_MM_Y, S_MM_RESET },
            new[] { S_C_TAPE, S_C_GPS, S_C_SIGNALS, S_C_OWN, S_C_KOTH, S_C_SIZE, S_C_SNAP, S_C_X, S_C_Y, S_C_RESET },
            new[] { S_MAP_OWN, S_MAP_KOTH, S_MAP_GPS_LABELS, S_MAP_SIGNAL_LABELS, S_MAP_STACK, S_MAP_KEY },
            new[] { S_RATE, S_DEFAULT_GPS, S_FACTORY_RESET },
        };
        private const int SETTINGS_MAX_ROWS = 13;

        private static readonly Color SET_ON = new Color(80, 255, 120);
        private static readonly Color SET_OFF = new Color(255, 90, 90);
        private static readonly Color SET_VALUE = new Color(255, 230, 40);
        private static readonly Color SET_CHOICE = new Color(187, 233, 246);
        private static readonly Color SET_LABEL = new Color(220, 230, 240);
        private static readonly Color SET_ACTION = new Color(255, 180, 50);

        private GvkSettingsScreen _settingsScreen;
        private readonly GvkScreenLayer _ssSettings = new GvkScreenLayer();
        private readonly List<MapHit> _settingsHits = new List<MapHit>(24); // Wp holds the row id or hit code
        private readonly List<MyKeys> _settingsKeys = new List<MyKeys>(8);
        private int _settingsTab;
        private int _settingsHover = int.MinValue;   // Hit under the cursor at the last build
        private bool _settingsDirty = true;
        private Vector2 _settingsViewport;
        private int _settingsDragRow;                // Slider row being dragged, 0 for none
        private double _settingsTrackMin;            // Slider track, HUD units, from the last build
        private double _settingsTrackMax;
        private bool _settingsCapturing;             // Waiting for the new map key
        private bool _settingsCaptureSkipFrame;      // The click that started the capture is not the key
        private bool _settingsResetArmed;            // Factory reset asks for a second click
        private bool _settingsReturnToMap;           // Opened from the map's Settings button; reopen the map on close
        private bool _settingsFailed;                // An error turned the settings screen off for this session
        private int _mapReopenTicks;                 // Ticks left to reopen the map once its fade-out has finished
        private const double SETTINGS_TRACK_GRAB = 0.02; // Slack around the slider track for the press, HUD units
        private const int MAP_REOPEN_TICKS = 60;

        private bool SettingsScreenOpen => _settingsScreen != null && _settingsScreen.IsOpen;

        /// <summary>
        /// Opens the settings screen. A new mod screen hides the one under it (the game's CanHideOthers),
        /// so the map's Settings button closes the map first and the map reopens, same view, when settings closes.
        /// With the map closed the HUD draws, so the position sliders move the minimap and compass where the player can see them.
        /// </summary>
        private void OpenSettingsScreen(bool fromMap = false)
        {
            if (SettingsScreenOpen) return;
            if (_settingsFailed)
            {
                MyAPIGateway.Utilities.ShowNotification("[GVK NAV] The settings screen hit an error and is off until you rejoin", 2500, MyFontEnum.Red);
                return;
            }
            _settingsScreen = new GvkSettingsScreen(this); // A new screen object each time, as for the map (see OpenFullMap)
            _settingsReturnToMap = false;
            if (fromMap)
            {
                // Close before opening: a closing screen is not the one the new screen hides
                _settingsReturnToMap = true;
                CloseFullMap();
            }
            _mapReopenTicks = 0;
            _settingsDragRow = 0;
            _settingsCapturing = false;
            _settingsResetArmed = false;
            _settingsDirty = true;
            _settingsScreen.Open();
            if (SettingsScreenOpen)
            {
                try
                {
                    BuildSettingsScreen(false, Vector2D.Zero);
                }
                catch (Exception ex)
                {
                    FailSettingsScreen("open", ex); // Blamed on settings, not on the map or command that opened it
                }
                return;
            }
            MyAPIGateway.Utilities.ShowNotification("[GVK NAV] Can't open settings: too many screens are open", 2500, MyFontEnum.Red);
            if (_settingsReturnToMap)
            {
                _settingsReturnToMap = false; // The game refused the screen; put the map back
                _mapReopenTicks = MAP_REOPEN_TICKS;
            }
        }

        /// <summary>Called every tick: reopens the map after settings, once the map's own fade-out is done.</summary>
        private void UpdateMapReopen()
        {
            if (_mapReopenTicks <= 0) return;
            _mapReopenTicks--;
            if (SettingsScreenOpen) { _mapReopenTicks = 0; return; }
            if (showFullMap) return; // Still fading out (a map opened since then has cleared the countdown)
            _mapReopenTicks = 0;
            OpenFullMap(true);
        }

        public bool OnSettingsCancelling()
        {
            if (_settingsCapturing)
            {
                _settingsCapturing = false; // Esc cancels the key capture, not the screen
                _settingsDirty = true;
                return false;
            }
            return true;
        }

        public void OnSettingsClosed(bool isUnloading)
        {
            bool returnToMap = _settingsReturnToMap;
            _settingsReturnToMap = false;
            try
            {
                if (_settingsDragRow != 0 && !isUnloading) EndSettingsDrag();
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[GVK_ZoneNavCommand] Settings screen close failed: {ex}");
            }
            _settingsDragRow = 0;
            _settingsCapturing = false;
            _ssSettings.Clear();
            if (isUnloading) return;
            // Settings may have changed what the map shows
            _mapLayoutDirty = true;
            _fullMapNeedsRedraw = true;
            if (returnToMap) _mapReopenTicks = MAP_REOPEN_TICKS;
        }

        public void DrawSettingsScreen(float alpha)
        {
            if (_settingsFailed) return;
            try
            {
                _ssSettings.Draw(MyScreenSpaceLayer.BelowHud, alpha);
            }
            catch (Exception ex)
            {
                FailSettingsScreen("draw", ex);
            }
        }

        /// <summary>An error in the settings screen's draw or input: log it, close the screen and keep it off for the session.</summary>
        private void FailSettingsScreen(string where, Exception ex)
        {
            if (_settingsFailed) return; // Already closing from an earlier error
            _settingsFailed = true;
            MyLog.Default.WriteLineAndConsole($"[GVK_ZoneNavCommand] Settings screen {where} failed, settings screen off for this session: {ex}");
            _settingsDragRow = 0; // No slider save from a half-done input
            _settingsCapturing = false;
            try
            {
                _ssSettings.Clear();
                if (SettingsScreenOpen) _settingsScreen.CloseNow(); // No fade, as for the map: a fading close of a just-opened screen is lost
                MyAPIGateway.Utilities.ShowNotification("[GVK NAV] The settings screen hit an error and closed", 2500, MyFontEnum.Red);
            }
            catch (Exception ex2)
            {
                MyLog.Default.WriteLineAndConsole($"[GVK_ZoneNavCommand] Settings screen close after an error failed: {ex2}");
            }
        }

        public void HandleSettingsInput()
        {
            if (_settingsFailed) return; // Fading out after an error
            try
            {
                HandleSettingsInputCore();
            }
            catch (Exception ex)
            {
                FailSettingsScreen("input", ex);
            }
        }

        private void HandleSettingsInputCore()
        {
            var input = MyAPIGateway.Input;
            Vector2D cursor;
            bool hasCursor = _ssSettings.TryGetCursorHud(out cursor);
            int hit = hasCursor ? FindSettingsHit(cursor) : int.MinValue;

            if (_settingsCapturing)
            {
                if (_settingsCaptureSkipFrame) _settingsCaptureSkipFrame = false;
                else if (input.IsNewLeftMousePressed() || input.IsNewRightMousePressed())
                {
                    _settingsCapturing = false;
                    _settingsDirty = true;
                }
                else TryCaptureMapKey(input);
            }
            else if (_settingsDragRow != 0)
            {
                if (!input.IsLeftMousePressed()) EndSettingsDrag();
                else if (hasCursor) DragSettingsSlider(cursor.X);
            }
            else if (hasCursor && input.IsNewLeftMousePressed() && hit != int.MinValue)
            {
                ClickSettings(hit, cursor.X);
            }

            if (hit != _settingsHover)
            {
                if (_settingsResetArmed && hit != S_FACTORY_RESET) _settingsResetArmed = false;
                _settingsDirty = true;
            }
            if (_settingsScreen != null && _settingsScreen.IsOpen && (_settingsDirty || _settingsViewport != (MyAPIGateway.Session?.Camera?.ViewportSize ?? _settingsViewport)))
                BuildSettingsScreen(hasCursor, cursor);
        }

        private int FindSettingsHit(Vector2D cursor)
        {
            for (int i = _settingsHits.Count - 1; i >= 0; i--)
            {
                var h = _settingsHits[i];
                if (IsInMapHit(ref h, cursor)) return h.Wp;
            }
            return int.MinValue;
        }

        private void ClickSettings(int hit, double cursorX)
        {
            _settingsDirty = true;
            if (hit == HIT_CLOSE)
            {
                _settingsScreen.Close();
                return;
            }
            if (hit <= HIT_TAB)
            {
                _settingsTab = MathHelper.Clamp(HIT_TAB - hit, 0, SETTINGS_TABS.Length - 1);
                _settingsResetArmed = false;
                return;
            }
            if (IsSettingsSlider(hit))
            {
                // Only a press on the track drags; a click on the label or value would throw the element to the edge
                if (cursorX < _settingsTrackMin - SETTINGS_TRACK_GRAB || cursorX > _settingsTrackMax + SETTINGS_TRACK_GRAB) return;
                _settingsDragRow = hit;
                DragSettingsSlider(cursorX);
                return;
            }
            DoSetting(hit);
        }

        private static bool IsSettingsSlider(int id) => id == S_MM_X || id == S_MM_Y || id == S_C_X || id == S_C_Y;

        /// <summary>Moves the HUD element live while dragging; the config is saved once, on release.</summary>
        private void DragSettingsSlider(double cursorX)
        {
            double span = _settingsTrackMax - _settingsTrackMin;
            if (span <= 0.0) return;
            double v = MathHelper.Clamp((cursorX - _settingsTrackMin) / span, 0.0, 1.0) * 2.0 - 1.0;
            switch (_settingsDragRow)
            {
                case S_MM_X: minimapOffsetX = v; break;
                case S_MM_Y: minimapOffsetY = v; break;
                case S_C_X: compassOffsetX = v; break;
                case S_C_Y: compassOffsetY = v; break;
            }
            if (_settingsDragRow == S_MM_X || _settingsDragRow == S_MM_Y)
            {
                ApplyMinimapScale(minimapScale);
                _refreshMinimapNextFrame = true;
            }
            else
            {
                ApplyCompassScale(compassScale);
                _refreshCompassNextFrame = true;
            }
            _settingsDirty = true;
        }

        private void EndSettingsDrag()
        {
            int row = _settingsDragRow;
            _settingsDragRow = 0;
            _settingsDirty = true;
            if (row == S_MM_X || row == S_MM_Y) SetMinimapOffset(minimapOffsetX, minimapOffsetY);
            else if (row == S_C_X || row == S_C_Y) SetCompassOffset(compassOffsetX, compassOffsetY);
        }

        private void TryCaptureMapKey(VRage.ModAPI.IMyInput input)
        {
            _settingsKeys.Clear();
            input.GetPressedKeys(_settingsKeys);
            for (int i = 0; i < _settingsKeys.Count; i++)
            {
                MyKeys key = _settingsKeys[i];
                if (IsCaptureModifierOrMouse(key) || !input.IsNewKeyPressed(key)) continue;
                _settingsCapturing = false;
                _settingsDirty = true;
                SetMapKey(key, input.IsAnyShiftKeyPressed(), input.IsAnyCtrlKeyPressed(), input.IsAnyAltKeyPressed());
                return;
            }
        }

        /// <summary>Mouse buttons and modifier keys can't be the map key on their own; Esc cancels the capture.</summary>
        private static bool IsCaptureModifierOrMouse(MyKeys key)
        {
            int k = (int)key;
            return k <= 6 || key == MyKeys.Escape
                || key == MyKeys.Shift || key == MyKeys.Control || key == MyKeys.Alt
                || key == MyKeys.LeftShift || key == MyKeys.RightShift || key == MyKeys.LeftControl || key == MyKeys.RightControl
                || key == MyKeys.LeftAlt || key == MyKeys.RightAlt || key == MyKeys.LeftWindows || key == MyKeys.RightWindows || key == MyKeys.Apps;
        }

        private void DoSetting(int id)
        {
            switch (id)
            {
                case S_OPEN_MAP:
                    _settingsScreen.Close();
                    if (!showFullMap) OpenFullMap(_settingsReturnToMap); // Back from the map: keep its view
                    break;
                case S_MINIMAP: ToggleMinimap(); break;
                case S_COMPASS:
                case S_C_TAPE: ToggleCompass(); break;
                case S_ZONE_BAR: CycleZoneBarMode(); break;
                case S_MM_MODE: ToggleMinimapMode(); break;
                case S_MM_RANGE: CycleRadarRange(); break;
                case S_MM_SIZE: CycleMinimapScale(); break;
                case S_MM_ZONE_LINES: ToggleRadarZoneLines(); break;
                case S_MM_FOV: ToggleRadarFovLines(); break;
                case S_MM_GPS: ToggleMarkerFilter(ref showMinimapGps, "Minimap GPS"); break;
                case S_MM_SIGNALS: ToggleMarkerFilter(ref showMinimapSignals, "Minimap Signals"); break;
                case S_MM_OWN: ToggleMarkerFilter(ref showMinimapOwnSignals, "Minimap Own & Faction Signals"); break;
                case S_MM_KOTH: ToggleMarkerFilter(ref showMinimapKoth, "Minimap KOTH"); break;
                case S_MM_SNAP: CycleMinimapPreset(); break;
                case S_MM_RESET: ResetMinimapOffset(); break;
                case S_C_GPS: ToggleMarkerFilter(ref showCompassGps, "Compass GPS"); break;
                case S_C_SIGNALS: ToggleMarkerFilter(ref showCompassSignals, "Compass Signals"); break;
                case S_C_OWN: ToggleMarkerFilter(ref showCompassOwnSignals, "Compass Own & Faction Signals"); break;
                case S_C_KOTH: ToggleMarkerFilter(ref showCompassKoth, "Compass KOTH"); break;
                case S_C_SIZE: CycleCompassScale(); break;
                case S_C_SNAP: CycleCompassPreset(); break;
                case S_C_RESET: ResetCompassOffset(); break;
                case S_MAP_OWN: ToggleMarkerFilter(ref showMapOwnSignals, "Map Own & Faction Signals"); break;
                case S_MAP_KOTH: ToggleMarkerFilter(ref showMapKoth, "Map KOTH"); break;
                case S_MAP_GPS_LABELS: ToggleMarkerFilter(ref showMapGpsLabels, "Map GPS Labels"); break;
                case S_MAP_SIGNAL_LABELS: ToggleMarkerFilter(ref showMapSignalLabels, "Map Signal Labels"); break;
                case S_MAP_STACK: ToggleMarkerFilter(ref stackMapIcons, "Map Icon Stacking"); break;
                case S_MAP_KEY:
                    _settingsCapturing = true;
                    _settingsCaptureSkipFrame = true;
                    break;
                case S_RATE: CycleUpdateTickRate(); break;
                case S_DEFAULT_GPS: PopulateDefaultGps(true); break;
                case S_FACTORY_RESET:
                    if (_settingsResetArmed)
                    {
                        _settingsResetArmed = false;
                        ResetAllToFactoryDefaults();
                    }
                    else _settingsResetArmed = true;
                    break;
            }
        }

        private static string GetSettingLabel(int id)
        {
            switch (id)
            {
                case S_OPEN_MAP: return "Open Satellite Map";
                case S_MINIMAP: return "Minimap";
                case S_COMPASS: return "Compass Ribbon";
                case S_ZONE_BAR: return "Zone Telemetry";
                case S_MM_MODE: return "Mode";
                case S_MM_RANGE: return "Radar Range";
                case S_MM_SIZE: return "Minimap Size";
                case S_MM_ZONE_LINES: return "Zone Direction Lines";
                case S_MM_FOV: return "Camera View Lines";
                case S_MM_GPS:
                case S_C_GPS: return "GPS";
                case S_MM_SIGNALS:
                case S_C_SIGNALS: return "Signals";
                case S_MM_OWN:
                case S_C_OWN:
                case S_MAP_OWN: return "Own & Faction Signals";
                case S_MM_KOTH:
                case S_C_KOTH:
                case S_MAP_KOTH: return "KOTH";
                case S_MM_SNAP:
                case S_C_SNAP: return "Snap Position";
                case S_MM_X:
                case S_C_X: return "Position (left - right)";
                case S_MM_Y:
                case S_C_Y: return "Position (down - up)";
                case S_MM_RESET: return "Reset Position to Top-Right";
                case S_C_TAPE: return "Compass Tape";
                case S_C_SIZE: return "Compass Size";
                case S_C_RESET: return "Reset Position to Top-Center";
                case S_MAP_GPS_LABELS: return "GPS Labels";
                case S_MAP_SIGNAL_LABELS: return "Signal Labels";
                case S_MAP_STACK: return "Stack Icons";
                case S_MAP_KEY: return "Map Key";
                case S_RATE: return "Update Frequency";
                case S_DEFAULT_GPS: return "Restore Default Kharak GPS";
                case S_FACTORY_RESET: return "Factory Reset All Settings";
                default: return string.Empty;
            }
        }

        private static string OnOff(bool on, out Color color)
        {
            color = on ? SET_ON : SET_OFF;
            return on ? "On" : "Off";
        }

        /// <summary>A row's current value and its colour; null for buttons that only do something.</summary>
        private string GetSettingValue(int id, out Color color)
        {
            color = SET_VALUE;
            switch (id)
            {
                case S_MINIMAP: return OnOff(showMinimap, out color);
                case S_COMPASS:
                case S_C_TAPE: return OnOff(showCompass, out color);
                case S_ZONE_BAR:
                    color = zoneBarMode == ZoneBarMode.Off ? SET_OFF : SET_CHOICE;
                    return zoneBarMode == ZoneBarMode.RadarDock ? "Radar Dock" : zoneBarMode == ZoneBarMode.CompassDock ? "Compass Dock" : "Off";
                case S_MM_MODE:
                    color = SET_CHOICE;
                    return minimapMode == MinimapDisplayMode.TacticalRadar ? "Tactical Radar" : "Sector Map";
                case S_MM_RANGE:
                    return radarScale == RadarScaleMode.Logarithmic ? "30 km (log)" : (radarRangeMeters * 0.001).ToString("0.#") + " km";
                case S_MM_SIZE: return (int)Math.Round(minimapScale * 100) + "%";
                case S_MM_ZONE_LINES: return OnOff(showRadarZoneLines, out color);
                case S_MM_FOV: return OnOff(showRadarFovLines, out color);
                case S_MM_GPS: return OnOff(showMinimapGps, out color);
                case S_MM_SIGNALS: return OnOff(showMinimapSignals, out color);
                case S_MM_OWN: return OnOff(showMinimapOwnSignals, out color);
                case S_MM_KOTH: return OnOff(showMinimapKoth, out color);
                case S_MM_SNAP:
                    color = SET_CHOICE;
                    { int mi = FindMinimapPresetIndex(); return mi < 0 ? "Custom" : MinimapPresets[mi].Name; }
                case S_MM_X: return minimapOffsetX.ToString("+0.00;-0.00;0.00");
                case S_MM_Y: return minimapOffsetY.ToString("+0.00;-0.00;0.00");
                case S_C_GPS: return OnOff(showCompassGps, out color);
                case S_C_SIGNALS: return OnOff(showCompassSignals, out color);
                case S_C_OWN: return OnOff(showCompassOwnSignals, out color);
                case S_C_KOTH: return OnOff(showCompassKoth, out color);
                case S_C_SIZE: return (int)Math.Round(compassScale * 100) + "%";
                case S_C_SNAP:
                    color = SET_CHOICE;
                    { int ci = FindCompassPresetIndex(); return ci < 0 ? "Custom" : CompassPresets[ci].Name; }
                case S_C_X: return compassOffsetX.ToString("+0.00;-0.00;0.00");
                case S_C_Y: return compassOffsetY.ToString("+0.00;-0.00;0.00");
                case S_MAP_OWN: return OnOff(showMapOwnSignals, out color);
                case S_MAP_KOTH: return OnOff(showMapKoth, out color);
                case S_MAP_GPS_LABELS: return OnOff(showMapGpsLabels, out color);
                case S_MAP_SIGNAL_LABELS: return OnOff(showMapSignalLabels, out color);
                case S_MAP_STACK: return OnOff(stackMapIcons, out color);
                case S_MAP_KEY:
                    if (_settingsCapturing) return "Press a key (Esc cancels)";
                    return mapKeyLabel;
                case S_RATE: return (60.0 / updateTickRate).ToString("0.#") + " Hz (" + updateTickRate + " ticks)";
                case S_FACTORY_RESET:
                    color = SET_OFF;
                    return _settingsResetArmed ? "Click again to reset everything" : null;
                default: return null;
            }
        }

        /// <summary>
        /// The settings panel, centred: title bar, tabs, the current tab's rows (label left, value right, sliders
        /// for positions), and a footer with the Close button. Rebuilt only when something on it changes.
        /// </summary>
        private void BuildSettingsScreen(bool hasCursor, Vector2D cursor)
        {
            var camera = MyAPIGateway.Session?.Camera;
            if (camera == null || !_ssSettings.Begin(camera.ViewportSize)) return;
            _settingsDirty = false;
            _settingsViewport = camera.ViewportSize;
            _settingsHits.Clear();
            float aspect = _settingsViewport.X / _settingsViewport.Y;
            float textScale = GetScreenSpaceTextScale(_settingsViewport);
            float titleScale = 0.72f * textScale;
            float tabScale = 0.60f * textScale;
            float rowScale = 0.62f * textScale;
            float footScale = 0.58f * textScale;

            double rowH = _ssSettings.LineHeightHud(rowScale) * 1.6;
            double headerH = _ssSettings.LineHeightHud(titleScale) * 1.7;
            double tabH = _ssSettings.LineHeightHud(tabScale) * 1.8;
            double footH = _ssSettings.LineHeightHud(footScale) * 2.4;
            double gapY = 0.008 * aspect;
            double panelW = 1.0;
            double panelH = headerH + gapY + tabH + gapY + SETTINGS_MAX_ROWS * rowH + gapY + footH;
            double left = -panelW * 0.5;
            double right = panelW * 0.5;
            double top = panelH * 0.5;

            _ssSettings.AddSprite(MATERIAL_SQUARE, 0.0, 0.0, panelW, panelH, new Color(15, 22, 30, 242));

            // Title bar
            _ssSettings.AddSprite(MATERIAL_SQUARE, 0.0, top - headerH * 0.5, panelW, headerH, new Color(10, 16, 24, 250));
            _ssSettings.AddSprite(MATERIAL_SQUARE, left + 0.004, top - headerH * 0.5, 0.006, headerH, SET_CHOICE);
            _ssSettings.AddText("GVK NAVIGATION SETTINGS", left + 0.022, top - headerH * 0.5, titleScale, new Color(255, 220, 0),
                MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER, true);

            // Tabs
            int tabCount = SETTINGS_TABS.Length;
            double tabGap = 0.006;
            double tabW = (panelW - 0.02 - tabGap * (tabCount - 1)) / tabCount;
            double tabTop = top - headerH - gapY;
            for (int t = 0; t < tabCount; t++)
            {
                double tl = left + 0.01 + t * (tabW + tabGap);
                var box = new MapHit { Wp = HIT_TAB - t, MinX = tl, MaxX = tl + tabW, MinY = tabTop - tabH, MaxY = tabTop };
                bool active = t == _settingsTab;
                bool hover = hasCursor && IsInMapHit(ref box, cursor);
                Color bg = active ? new Color(60, 80, 95, 255) : hover ? new Color(48, 63, 74, 255) : new Color(31, 41, 48, 255);
                _ssSettings.AddSprite(MATERIAL_SQUARE, tl + tabW * 0.5, tabTop - tabH * 0.5, tabW, tabH, bg);
                if (active) _ssSettings.AddSprite(MATERIAL_SQUARE, tl + tabW * 0.5, tabTop - tabH + 0.003, tabW, 0.006, SET_CHOICE);
                _ssSettings.AddText(SETTINGS_TABS[t], tl + tabW * 0.5, tabTop - tabH * 0.5, tabScale, active ? Color.White : SET_LABEL,
                    MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, true);
                _settingsHits.Add(box);
            }

            // Rows of the current tab
            int[] rows = SETTINGS_ROWS[MathHelper.Clamp(_settingsTab, 0, SETTINGS_ROWS.Length - 1)];
            double rowTop = tabTop - tabH - gapY;
            double rowLeft = left + 0.01;
            double rowRight = right - 0.01;
            double trackW = 0.30;
            _settingsTrackMax = rowRight - 0.02;
            _settingsTrackMin = _settingsTrackMax - trackW;
            for (int r = 0; r < rows.Length; r++)
            {
                int id = rows[r];
                double y = rowTop - r * rowH;
                double cy = y - rowH * 0.5;
                var box = new MapHit { Wp = id, MinX = rowLeft, MaxX = rowRight, MinY = y - rowH, MaxY = y };
                bool hover = (hasCursor && IsInMapHit(ref box, cursor)) || _settingsDragRow == id;
                Color bg = hover ? new Color(55, 73, 86, 235) : (r % 2 == 0 ? new Color(31, 41, 48, 210) : new Color(26, 35, 42, 210));
                _ssSettings.AddSprite(MATERIAL_SQUARE, (rowLeft + rowRight) * 0.5, cy, rowRight - rowLeft, rowH - 0.002 * aspect, bg);
                if (hover) _ssSettings.AddSprite(MATERIAL_SQUARE, rowLeft + 0.002, cy, 0.004, rowH - 0.002 * aspect, SET_CHOICE);

                Color valueColor;
                string value = GetSettingValue(id, out valueColor);
                bool isAction = value == null;
                Color labelColor = id == S_FACTORY_RESET ? SET_OFF : isAction ? SET_ACTION : SET_LABEL;
                _ssSettings.AddText(GetSettingLabel(id), rowLeft + 0.02, cy, rowScale, labelColor,
                    MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER, true);

                if (IsSettingsSlider(id))
                {
                    double v = id == S_MM_X ? minimapOffsetX : id == S_MM_Y ? minimapOffsetY : id == S_C_X ? compassOffsetX : compassOffsetY;
                    double knobX = _settingsTrackMin + (MathHelper.Clamp(v, -1.0, 1.0) + 1.0) * 0.5 * trackW;
                    _ssSettings.AddSprite(MATERIAL_SQUARE, _settingsTrackMin + trackW * 0.5, cy, trackW, 0.003 * aspect, new Color(90, 110, 125));
                    _ssSettings.AddSprite(MATERIAL_SQUARE, (_settingsTrackMin + knobX) * 0.5, cy, knobX - _settingsTrackMin, 0.003 * aspect, SET_CHOICE);
                    _ssSettings.AddSprite(MATERIAL_SQUARE, knobX, cy, 0.008, 0.008 * aspect * 1.6, hover ? Color.White : SET_CHOICE);
                    _ssSettings.AddText(value, _settingsTrackMin - 0.02, cy, rowScale, valueColor,
                        MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER, true);
                }
                else if (!isAction)
                {
                    _ssSettings.AddText(value, rowRight - 0.02, cy, rowScale, valueColor,
                        MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER, true);
                }
                _settingsHits.Add(box);
            }

            // Footer: what to do, and Close
            double footTop = -top + footH;
            _ssSettings.AddSprite(MATERIAL_SQUARE, 0.0, -top + footH * 0.5, panelW, footH, new Color(10, 16, 24, 250));
            _ssSettings.AddText("Click a setting to change it   |   Drag a slider to move   |   Esc: close", left + 0.022, footTop - footH * 0.5,
                footScale, MAP_HINT_COLOR, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER, true);
            double closeW = _ssSettings.MeasureWidthHud("Close", rowScale) + 0.05;
            double closeH = footH - 0.008 * aspect;
            double closeRight = right - 0.01;
            var closeBox = new MapHit { Wp = HIT_CLOSE, MinX = closeRight - closeW, MaxX = closeRight, MinY = footTop - footH * 0.5 - closeH * 0.5, MaxY = footTop - footH * 0.5 + closeH * 0.5 };
            bool closeHover = hasCursor && IsInMapHit(ref closeBox, cursor);
            _ssSettings.AddSprite(MATERIAL_SQUARE, closeRight - closeW * 0.5, footTop - footH * 0.5, closeW, closeH,
                closeHover ? new Color(70, 95, 112, 255) : new Color(41, 54, 62, 255));
            _ssSettings.AddText("Close", closeRight - closeW * 0.5, footTop - footH * 0.5, rowScale, closeHover ? Color.White : SET_LABEL,
                MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, true);
            _settingsHits.Add(closeBox);

            _settingsHover = hasCursor ? FindSettingsHit(cursor) : int.MinValue;
        }
    }
}
