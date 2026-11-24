using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace GVK.Navigation
{
    /// <summary>
    /// Known sites: MES static encounters (GVK_Derelicts) the nav HUD marks without a GPS point, read once per machine
    /// from the spawn group definitions every machine loads. Both the HUD and the server's status scan use this list.
    /// </summary>
    public static class GvkKnownSites
    {
        public const byte BASE = 1;
        public const byte TRADE = 2;
        public const byte REFINERY = 3;
        public const byte DEPOT = 4;
        public const byte CARRIER = 5;
        public const double DEFAULT_GPS_RADIUS = 150.0;  // A GPS point this close stands in for its nearest site
        public const string BASE_SITE_PREFIX = "GVK-Alliance-PresetBases-Site-SpawnGroup-"; // Every Base Site, named by its beacon text

        public struct Site
        {
            public string SubtypeId;
            public string Name;
            public byte Kind;
            public Vector3D Coords;   // The main grid's origin: MES places these with ForceExactPositionAndOrientation
            public double GpsRadius;  // Metres from the origin a GPS point on the station can be; wider for long stations
        }

        private struct Entry
        {
            public string SubtypeId;
            public string Name;
            public byte Kind;
            public double GpsRadius;
            public Entry(string subtypeId, string name, byte kind, double gpsRadius = DEFAULT_GPS_RADIUS)
            {
                SubtypeId = subtypeId;
                Name = name;
                Kind = kind;
                GpsRadius = gpsRadius;
            }
        }

        // Long stations get a wider GPS radius: their grid origin sits at one end (Coalition Base out to ~300 m,
        // Supply Depot ~250 m, SOBAN Carrier ~180 m)
        private static readonly Entry[] TABLE = new Entry[]
        {
            new Entry("GVK-StaticEncounters-TradeStations-SpawnGroup-Sevastopol", "Sevastopol", TRADE),
            new Entry("GVK-StaticEncounters-TradeStations-SpawnGroup-Skyport", "Skyport", TRADE),
            new Entry("GVK-StaticEncounters-TradeStations-SpawnGroup-Mastodon", "Mastodon", TRADE),
            new Entry("GVK-StaticEncounters-TradeStations-SpawnGroup-Rustys", "Rusty's", TRADE),
            new Entry("GVK-StaticEncounter-TradeStations-SpawnGroup-COALITIONBase", "Coalition Base", TRADE, 330.0),
            new Entry("GVK-StaticEncounters-TradeStations-SpawnGroup-RoverSales01", "Vehicle Store (Crossroads)", TRADE),
            new Entry("GVK-StaticEncounters-TradeStations-SpawnGroup-RoverSales02", "Vehicle Store (Sevastopol)", TRADE),
            new Entry("GVK-TradeStations-SpawnGroup-KOTHTradeStation1", "KOTH Camp Khar Toba", TRADE),
            new Entry("GVK-TradeStations-SpawnGroup-KOTHTradeStation2", "KOTH Camp Kalash", TRADE),
            new Entry("GVK-TradeStations-SpawnGroup-KOTHTradeStation3", "KOTH Camp Crashed Starship", TRADE),
            new Entry("GVK-TradeStations-SpawnGroup-KOTHTradeStation4", "KOTH Camp Scraptown", TRADE),
            new Entry("GVK-StaticEncounter-SpawnGroup-CURefinery01", "Scrap Refinery", REFINERY),
            new Entry("GVK-Alliance-SupplyDepot1-SpawnGroup", "Supply Depot 1", DEPOT, 250.0),
            new Entry("GVK-Alliance-SupplyDepot2-SpawnGroup", "Supply Depot 2", DEPOT, 250.0),
            new Entry("GVK-Alliance-SupplyDepot3-SpawnGroup", "Supply Depot 3", DEPOT, 250.0),
            new Entry("GVK-Alliance-SupplyDepot4-SpawnGroup", "Supply Depot 4", DEPOT, 250.0),
            new Entry("GVK-Alliance-Carrier-SpawnGroup-KHAANEPH", "KHAANEPH Carrier", CARRIER),
            new Entry("GVK-Alliance-Carrier-SpawnGroup-SOBAN", "SOBAN Carrier", CARRIER, 180.0)
        };

        public static readonly List<Site> Sites = new List<Site>(48);
        private static bool _loaded;

        /// <summary>Unload: the next session reads the definitions again (statics can outlive a world).</summary>
        public static void Reset()
        {
            _loaded = false;
            Sites.Clear();
        }

        /// <summary>
        /// Builds <see cref="Sites"/> once, from the MES spawn group definitions. Logs table entries that match no
        /// usable spawn group. Call it no earlier than the first update, once definitions are loaded.
        /// </summary>
        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            Sites.Clear();
            try
            {
                var found = new bool[TABLE.Length];
                int baseSites = 0;
                var defs = Sandbox.Definitions.MyDefinitionManager.Static.GetSpawnGroupDefinitions();
                for (int i = 0; i < defs.Count; i++)
                {
                    var def = defs[i];
                    if (def == null) continue;
                    string id = def.Id.SubtypeName;
                    if (string.IsNullOrEmpty(id)) continue;

                    byte kind = 0;
                    string name = null;
                    int entry = -1;
                    double gpsRadius = DEFAULT_GPS_RADIUS;
                    if (id.StartsWith(BASE_SITE_PREFIX, StringComparison.Ordinal))
                    {
                        kind = BASE;
                        string beacon = def.Prefabs.Count > 0 ? def.Prefabs[0].BeaconText : null;
                        name = string.IsNullOrWhiteSpace(beacon) ? "Base Site" : beacon.Trim();
                    }
                    else
                    {
                        for (int k = 0; k < TABLE.Length; k++)
                        {
                            if (TABLE[k].SubtypeId != id) continue;
                            entry = k;
                            kind = TABLE[k].Kind;
                            name = TABLE[k].Name;
                            gpsRadius = TABLE[k].GpsRadius;
                            break;
                        }
                    }
                    if (kind == 0) continue;

                    Vector3D coords;
                    if (!def.Enabled || !def.IsValid || !TryGetStaticEncounterCoords(def.DescriptionText, out coords)) continue;
                    if (entry >= 0) found[entry] = true;
                    else baseSites++;

                    Sites.Add(new Site { SubtypeId = id, Name = name, Kind = kind, Coords = coords, GpsRadius = gpsRadius });
                }

                for (int k = 0; k < TABLE.Length; k++)
                {
                    if (!found[k])
                        MyLog.Default.WriteLineAndConsole($"[GVK_SiteStatus] Known sites: no usable spawn group '{TABLE[k].SubtypeId}'");
                }
                if (baseSites == 0)
                    MyLog.Default.WriteLineAndConsole($"[GVK_SiteStatus] Known sites: no usable spawn group starting '{BASE_SITE_PREFIX}'");
            }
            catch (Exception ex)
            {
                Sites.Clear();
                MyLog.Default.WriteLineAndConsole($"[GVK_SiteStatus] Known sites failed to load: {ex}");
            }
        }

        /// <summary>
        /// Reads a spawn group's static position the way MES does: needs the MES header and [StaticEncounter:true],
        /// takes the last [StaticEncounterCoords:{X:.. Y:.. Z:..}], and skips groups placed by planet direction instead.
        /// </summary>
        private static bool TryGetStaticEncounterCoords(string description, out Vector3D coords)
        {
            coords = Vector3D.Zero;
            if (string.IsNullOrEmpty(description) || !description.Contains("[Modular Encounters SpawnGroup]")) return false;

            bool isStatic = false;
            bool byPlanetDirection = false;
            string[] lines = description.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string tag = lines[i].Trim();
                if (tag.Length > 0 && tag[0] == '[') tag = tag.Substring(1);
                if (tag.Length > 0 && tag[tag.Length - 1] == ']') tag = tag.Substring(0, tag.Length - 1);
                int colon = tag.IndexOf(':');
                if (colon <= 0) continue;
                string key = tag.Substring(0, colon);
                string value = tag.Substring(colon + 1);
                bool flag;

                if (key == "StaticEncounter")
                {
                    if (bool.TryParse(value, out flag)) isStatic = flag;
                }
                else if (key == "StaticEncounterUsePlanetDirectionAndAltitude")
                {
                    if (bool.TryParse(value, out flag)) byPlanetDirection = flag;
                }
                else if (key == "StaticEncounterCoords")
                {
                    if (value.Length > 0 && value[0] == '{') value = value.Substring(1);
                    if (value.Length > 0 && value[value.Length - 1] == '}') value = value.Substring(0, value.Length - 1);
                    Vector3D parsed;
                    if (Vector3D.TryParse(value, out parsed)) coords = parsed;
                }
            }
            return isStatic && !byPlanetDirection && coords != Vector3D.Zero;
        }
    }

    /// <summary>
    /// What stands at each known site, as the server last saw it. Filled by <see cref="GVK_SiteStatus"/>
    /// (from the server's message, or directly on a host); the nav HUD reads it for site colours and hover text.
    /// </summary>
    public static class GvkSiteStatus
    {
        public const byte UNKNOWN = 0;    // No word from the server yet
        public const byte HELD = 1;       // A grid stands there; FactionId is its owner's faction (0 = none). Base Sites: an alliance Base
        public const byte UNCLAIMED = 2;  // Base Sites only: the site's own grid, no alliance Base
        public const byte MISSING = 3;    // No grid: MES hasn't spawned it (no player has come within its trigger range) or it is gone

        public struct Entry
        {
            public byte State;
            public long FactionId;
            public string Tag;
        }

        public static readonly Dictionary<string, Entry> BySubtypeId = new Dictionary<string, Entry>();
        public static int Version;        // Bumped on every update, so the HUD knows to re-read

        public static void Reset()
        {
            BySubtypeId.Clear();
            Version++;
        }

        /// <summary>Message text: one line per site, "SubtypeId\tState\tFactionId\tTag".</summary>
        public static void Parse(string text)
        {
            BySubtypeId.Clear();
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string[] f = lines[i].Split('\t');
                if (f.Length < 4) continue;
                byte state;
                long factionId;
                if (!byte.TryParse(f[1], out state) || !long.TryParse(f[2], out factionId)) continue;
                BySubtypeId[f[0]] = new Entry { State = state, FactionId = factionId, Tag = f[3] };
            }
            Version++;
        }
    }

    /// <summary>
    /// Site status feed for the nav HUD. The server checks each known site once a minute (Base Sites: is an alliance
    /// Base standing within 600 m, and whose; other sites: does the site's grid stand at its origin, and whose)
    /// and sends the list to every client when it changes, and to a client that asks when it joins.
    /// </summary>
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class GVK_SiteStatus : MySessionComponentBase
    {
        private const ushort CHANNEL = 47411;            // The GVK KOTH plugin uses 47410
        private const int FIRST_SCAN_TICK = 600;         // 10 s after load, once MES has placed the statics it spawns at start
        private const int SCAN_TICKS = 3600;             // Then once a minute
        private const int REQUEST_TICK = 300;            // A client asks for the list 5 s after load
        private const double BASE_SEARCH_DIST_SQ = 600.0 * 600.0; // Claimed Bases spawn 200-500 m from their site
        private const double ORIGIN_MATCH_DIST_SQ = 25.0 * 25.0;  // A grid whose origin is this close is the site's own
        private const string UNCLAIMED_TAG = "UNKN";     // Owner of the Base Site grids and Supply Depots: no alliance
        private static readonly byte[] REQUEST = { 63 }; // "?"

        private static readonly Func<IMyEntity, bool> IsGrid = e => e is IMyCubeGrid;

        private readonly HashSet<IMyEntity> _entities = new HashSet<IMyEntity>();
        private readonly StringBuilder _sb = new StringBuilder(4096);
        private byte[] _payload;                          // Last list sent (server)
        private string _payloadText = "";
        private double[] _bestBaseDistSq = new double[0];
        private long[] _baseFaction = new long[0];
        private bool[] _originFound = new bool[0];
        private long[] _originFaction = new long[0];
        private bool _registered;
        private bool _failed;                             // An error turns the feed off for the session
        private int _tick;

        public override void LoadData()
        {
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(CHANNEL, OnMessage);
            _registered = true;
        }

        protected override void UnloadData()
        {
            if (_registered)
                MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(CHANNEL, OnMessage);
            _registered = false;
            GvkSiteStatus.Reset();
            GvkKnownSites.Reset();
        }

        public override void UpdateBeforeSimulation()
        {
            if (_failed) return;
            _tick++;
            try
            {
                if (MyAPIGateway.Multiplayer.IsServer)
                {
                    if (_tick == FIRST_SCAN_TICK || (_tick > FIRST_SCAN_TICK && (_tick - FIRST_SCAN_TICK) % SCAN_TICKS == 0))
                        ScanAndSend();
                }
                else if (_tick == REQUEST_TICK)
                {
                    MyAPIGateway.Multiplayer.SendMessageToServer(CHANNEL, REQUEST, true);
                }
            }
            catch (Exception ex)
            {
                _failed = true;
                MyLog.Default.WriteLineAndConsole($"[GVK_SiteStatus] Site status off for this session: {ex}");
            }
        }

        private void OnMessage(ushort channel, byte[] data, ulong sender, bool fromServer)
        {
            try
            {
                if (data == null) return;
                if (MyAPIGateway.Multiplayer.IsServer)
                {
                    // A client asking for the list on join
                    if (!fromServer && data.Length == 1 && data[0] == REQUEST[0] && _payload != null)
                        MyAPIGateway.Multiplayer.SendMessageTo(CHANNEL, _payload, sender, true);
                    return;
                }
                if (fromServer)
                    GvkSiteStatus.Parse(Encoding.UTF8.GetString(data));
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[GVK_SiteStatus] Bad site status message: {ex.Message}");
            }
        }

        /// <summary>Server: checks every known site and sends the list if it changed.</summary>
        private void ScanAndSend()
        {
            GvkKnownSites.EnsureLoaded();
            var sites = GvkKnownSites.Sites;
            int n = sites.Count;
            if (n == 0) return;
            if (_baseFaction.Length != n)
            {
                _bestBaseDistSq = new double[n];
                _baseFaction = new long[n];
                _originFound = new bool[n];
                _originFaction = new long[n];
            }
            for (int s = 0; s < n; s++)
            {
                _bestBaseDistSq[s] = BASE_SEARCH_DIST_SQ;
                _baseFaction[s] = 0;
                _originFound[s] = false;
                _originFaction[s] = 0;
            }

            var factions = MyAPIGateway.Session.Factions;
            _entities.Clear();
            MyAPIGateway.Entities.GetEntities(_entities, IsGrid);
            foreach (var ent in _entities)
            {
                var grid = ent as IMyCubeGrid;
                if (grid == null || grid.MarkedForClose) continue;
                Vector3D pos = grid.PositionComp.GetPosition();

                int origin = -1;
                int nearestBase = -1;
                double nearestBaseSq = double.MaxValue;
                for (int s = 0; s < n; s++)
                {
                    double dSq = Vector3D.DistanceSquared(pos, sites[s].Coords);
                    if (dSq <= ORIGIN_MATCH_DIST_SQ && origin < 0) origin = s;
                    if (sites[s].Kind == GvkKnownSites.BASE && dSq < nearestBaseSq)
                    {
                        nearestBaseSq = dSq;
                        nearestBase = s;
                    }
                }
                bool nearBase = nearestBase >= 0 && grid.IsStatic && nearestBaseSq < _bestBaseDistSq[nearestBase];
                if (origin < 0 && !nearBase) continue;

                var owners = grid.BigOwners;
                IMyFaction faction = owners.Count > 0 ? factions.TryGetPlayerFaction(owners[0]) : null;

                // The site's own grid: NPC-owned (a player's grid parked on the coords never counts), and on a Base Site the UNKN placeholder
                bool npc = faction != null && faction.IsEveryoneNpc();
                if (origin >= 0 && !_originFound[origin] && npc
                    && (sites[origin].Kind != GvkKnownSites.BASE || faction.Tag == UNCLAIMED_TAG))
                {
                    _originFound[origin] = true;
                    _originFaction[origin] = faction.FactionId;
                }
                // An alliance Base: a static grid of an NPC faction other than the sites' own UNKN, nearest site wins
                if (nearBase && npc && faction.Tag != UNCLAIMED_TAG)
                {
                    _bestBaseDistSq[nearestBase] = nearestBaseSq;
                    _baseFaction[nearestBase] = faction.FactionId;
                }
            }
            _entities.Clear();

            _sb.Clear();
            for (int s = 0; s < n; s++)
            {
                byte state;
                long factionId;
                if (sites[s].Kind == GvkKnownSites.BASE)
                {
                    factionId = _baseFaction[s];
                    state = factionId != 0 ? GvkSiteStatus.HELD : _originFound[s] ? GvkSiteStatus.UNCLAIMED : GvkSiteStatus.MISSING;
                }
                else
                {
                    factionId = _originFaction[s];
                    state = _originFound[s] ? GvkSiteStatus.HELD : GvkSiteStatus.MISSING;
                }
                IMyFaction faction = factionId != 0 ? factions.TryGetFactionById(factionId) : null;
                _sb.Append(sites[s].SubtypeId).Append('\t').Append(state).Append('\t').Append(factionId).Append('\t')
                    .Append(faction != null ? faction.Tag : "").Append('\n');
            }

            string text = _sb.ToString();
            if (text == _payloadText) return;
            _payloadText = text;
            _payload = Encoding.UTF8.GetBytes(text);
            if (MyAPIGateway.Multiplayer.MultiplayerActive)
                MyAPIGateway.Multiplayer.SendMessageToOthers(CHANNEL, _payload, true);
            if (!MyAPIGateway.Utilities.IsDedicated)
                GvkSiteStatus.Parse(text); // Host or single player: the HUD here reads it too
        }
    }
}
