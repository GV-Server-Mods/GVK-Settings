using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Draygo.API;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Utils;
using VRageMath;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;

namespace GVK.Navigation
{
    /// <summary>
    /// KOTH zone state from the GVK KOTH Torch plugin (GV-Server-Mods/GVK-KOTH-Plugin, docs/CLIENT_PROTOCOL.md).
    /// Filled by <see cref="GVK_KothClient"/>; the /gvk navigation HUD reads it for its KOTH markers.
    /// </summary>
    public static class KothClientData
    {
        public struct Zone
        {
            public string Name;
            public bool Open;           // false = the next slot's zone, not open yet
            public Vector3D Position;
            public double Radius;
            public double Fraction;     // Capture bar, 0-1 (open zones)
            public int Participants;    // Counted at the last server check (open zones)
            public DateTime StartUtc;   // When it opens (next zone)
            public long EntityId;       // NPC zones: the grid the zone follows (0 for fixed zones)
        }

        /// <summary>
        /// Where the zone is this frame: the centre of its grid if that grid is loaded on this client (smooth for
        /// moving zones), otherwise the last position the server sent (updated about once a second).
        /// </summary>
        public static Vector3D LivePosition(ref Zone zone)
        {
            if (zone.EntityId != 0)
            {
                var entity = MyAPIGateway.Entities.GetEntityById(zone.EntityId);
                if (entity != null && !entity.MarkedForClose)
                    return entity.PositionComp.WorldAABB.Center;
            }
            return zone.Position;
        }

        public static readonly List<Zone> Zones = new List<Zone>();

        /// <summary>Set on every message; the navigation HUD rebuilds its markers and clears it.</summary>
        public static bool Changed;
    }

    /// <summary>
    /// Client side of GVK KOTH: receives zone state, shows a capture panel while the player is inside an open
    /// KOTH, draws the zone boundary when near, and plays fireworks when a bar completes.
    /// </summary>
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class GVK_KothClient : MySessionComponentBase
    {
        private const ushort CHANNEL = 47410;
        private const int PANEL_UPDATE_TICKS = 10;
        private const double STALE_SECONDS = 30.0;      // No message for this long: the plugin stopped, clear the zones
        private const double BOUNDARY_SHOW_MARGIN = 1000.0;
        private const double FIREWORKS_RANGE = 5000.0;

        private static readonly MyStringId MATERIAL_SQUARE = MyStringId.GetOrCompute("Square");
        private static readonly MyStringId MATERIAL_LINE = MyStringId.GetOrCompute("WeaponLaser");
        private static readonly Vector3D PLANET_CENTER = new Vector3D(33032.44, 32395.99, 32074.17);
        private static readonly string[] FIREWORK_EFFECTS =
        {
            "Explosion_Firework_Red", "Explosion_Firework_Blue", "Explosion_Firework_Green",
            "Explosion_Firework_Yellow", "Explosion_Firework_Pink", "Explosion_Firework_Rainbow"
        };

        private struct PendingFirework
        {
            public int DueTick;
            public Vector3D Position;
            public string Effect;
        }

        private HudAPIv2 hudApi;
        private HudAPIv2.BillBoardHUDMessage panelBg;
        private HudAPIv2.BillBoardHUDMessage panelAccent;
        private HudAPIv2.HUDMessage panelMsg;
        private readonly StringBuilder panelText = new StringBuilder(160);

        private readonly List<PendingFirework> fireworks = new List<PendingFirework>();
        private readonly Random random = new Random();
        private DateTime lastMessageUtc = DateTime.MinValue;
        private int lastPercent = -1;
        private int lastParticipants = -1;
        private string lastName;
        private int tick;
        private bool registered;

        public override void LoadData()
        {
            if (MyAPIGateway.Utilities.IsDedicated)
                return;

            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(CHANNEL, OnMessage);
            registered = true;
            hudApi = new HudAPIv2(OnHudApiRegistered);
        }

        protected override void UnloadData()
        {
            if (registered)
                MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(CHANNEL, OnMessage);
            registered = false;

            if (hudApi != null)
            {
                panelBg?.DeleteMessage();
                panelAccent?.DeleteMessage();
                panelMsg?.DeleteMessage();
                hudApi.Close();
                hudApi = null;
            }

            KothClientData.Zones.Clear();
            KothClientData.Changed = true;
        }

        private void OnHudApiRegistered()
        {
            var origin = new Vector2D(-0.80, 0.45);
            panelBg = new HudAPIv2.BillBoardHUDMessage(
                Material: MATERIAL_SQUARE,
                Origin: origin,
                BillBoardColor: new Color(10, 16, 24, 220),
                Width: 0.36f,
                Height: 0.075f,
                HideHud: true,
                Shadowing: true,
                Blend: BlendTypeEnum.PostPP);
            panelBg.Visible = false;

            panelAccent = new HudAPIv2.BillBoardHUDMessage(
                Material: MATERIAL_SQUARE,
                Origin: origin,
                BillBoardColor: new Color(255, 128, 31),
                Offset: new Vector2D(-0.18 + 0.004, 0.0),
                Width: 0.005f,
                Height: 0.069f,
                HideHud: true,
                Shadowing: false,
                Blend: BlendTypeEnum.PostPP);
            panelAccent.Visible = false;

            panelMsg = new HudAPIv2.HUDMessage(
                Message: panelText,
                Origin: new Vector2D(origin.X - 0.18 + 0.014, origin.Y + 0.028),
                Scale: 0.62,
                HideHud: true,
                Shadowing: true,
                ShadowColor: Color.Black,
                Blend: BlendTypeEnum.PostPP);
            panelMsg.Visible = false;
        }

        // --- Messages from the plugin ------------------------------------------------------------------------

        private void OnMessage(ushort channel, byte[] data, ulong sender, bool fromServer)
        {
            if (!fromServer || data == null)
                return;

            try
            {
                Parse(Encoding.UTF8.GetString(data));
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("[GVK_KothClient] Bad zone message: " + ex.Message);
            }
        }

        private void Parse(string text)
        {
            var lines = text.Split('\n');
            if (lines.Length == 0 || lines[0].Trim() != "GVKKOTH|1")
                return;

            var c = CultureInfo.InvariantCulture;
            KothClientData.Zones.Clear();

            for (int i = 1; i < lines.Length; i++)
            {
                var f = lines[i].Trim().Split('|');
                if (f.Length < 2)
                    continue;

                if ((f[0] == "O" && f.Length >= 8) || (f[0] == "N" && f.Length >= 7))
                {
                    var zone = new KothClientData.Zone
                    {
                        Name = f[1],
                        Open = f[0] == "O",
                        Position = new Vector3D(double.Parse(f[2], c), double.Parse(f[3], c), double.Parse(f[4], c)),
                        Radius = double.Parse(f[5], c)
                    };
                    if (zone.Open)
                    {
                        zone.Fraction = double.Parse(f[6], c);
                        zone.Participants = int.Parse(f[7], c);
                        if (f.Length >= 9)
                            zone.EntityId = long.Parse(f[8], c);
                    }
                    else
                    {
                        zone.StartUtc = new DateTime(long.Parse(f[6], c), DateTimeKind.Utc);
                    }
                    KothClientData.Zones.Add(zone);
                }
                else if (f[0] == "C" && f.Length >= 5)
                {
                    var position = new Vector3D(double.Parse(f[2], c), double.Parse(f[3], c), double.Parse(f[4], c));
                    QueueFireworks(position);
                }
            }

            lastMessageUtc = DateTime.UtcNow;
            KothClientData.Changed = true;
        }

        // --- Panel and fireworks -------------------------------------------------------------------------------

        public override void UpdateBeforeSimulation()
        {
            if (MyAPIGateway.Utilities.IsDedicated)
                return;

            tick++;
            PlayDueFireworks();

            if (tick % PANEL_UPDATE_TICKS != 0)
                return;

            if (KothClientData.Zones.Count > 0 && (DateTime.UtcNow - lastMessageUtc).TotalSeconds > STALE_SECONDS)
            {
                KothClientData.Zones.Clear();
                KothClientData.Changed = true;
            }

            UpdatePanel();
        }

        private void UpdatePanel()
        {
            if (panelMsg == null)
                return;

            var player = MyAPIGateway.Session?.Player?.Character;
            int inside = -1;
            if (player != null)
            {
                var pos = player.GetPosition();
                for (int i = 0; i < KothClientData.Zones.Count; i++)
                {
                    var zone = KothClientData.Zones[i];
                    if (zone.Open && Vector3D.DistanceSquared(pos, KothClientData.LivePosition(ref zone)) <= zone.Radius * zone.Radius)
                    {
                        inside = i;
                        break;
                    }
                }
            }

            bool show = inside >= 0;
            panelBg.Visible = show;
            panelAccent.Visible = show;
            panelMsg.Visible = show;
            if (!show)
            {
                lastPercent = -1; // Rebuild the text next time the panel shows
                return;
            }

            var z = KothClientData.Zones[inside];
            int percent = (int)Math.Round(MathHelper.Clamp(z.Fraction, 0.0, 1.0) * 100.0);

            // Rebuild the text only when something shown has changed
            if (percent == lastPercent && z.Participants == lastParticipants && string.Equals(z.Name, lastName))
                return;
            lastPercent = percent;
            lastParticipants = z.Participants;
            lastName = z.Name;

            int filled = percent / 5;
            panelText.Clear();
            panelText.Append("<color=255,128,31>KOTH ").Append(z.Name).Append("<color=180,195,210>  |  <color=255,255,255>")
                     .Append(percent).Append("%\n<color=255,200,60>");
            for (int i = 0; i < 20; i++)
            {
                if (i == filled) panelText.Append("<color=90,100,110>");
                panelText.Append('|');
            }
            panelText.Append("<color=180,195,210>  counted: <color=255,255,255>").Append(z.Participants);
        }

        private void QueueFireworks(Vector3D center)
        {
            var player = MyAPIGateway.Session?.Player?.Character;
            if (player == null || Vector3D.DistanceSquared(player.GetPosition(), center) > FIREWORKS_RANGE * FIREWORKS_RANGE)
                return;

            var up = Vector3D.Normalize(center - PLANET_CENTER);
            for (int i = 0; i < 8; i++)
            {
                var side = Vector3D.CalculatePerpendicularVector(up);
                var around = Vector3D.Transform(side, MatrixD.CreateFromAxisAngle(up, random.NextDouble() * MathHelper.TwoPi));
                fireworks.Add(new PendingFirework
                {
                    DueTick = tick + i * 25 + random.Next(15),
                    Position = center + up * (150 + random.Next(150)) + around * random.Next(120),
                    Effect = FIREWORK_EFFECTS[random.Next(FIREWORK_EFFECTS.Length)]
                });
            }
        }

        private void PlayDueFireworks()
        {
            for (int i = fireworks.Count - 1; i >= 0; i--)
            {
                if (fireworks[i].DueTick > tick)
                    continue;

                var firework = fireworks[i];
                fireworks.RemoveAt(i);

                MyParticleEffect effect;
                var matrix = MatrixD.CreateTranslation(firework.Position);
                var position = firework.Position;
                MyParticlesManager.TryCreateParticleEffect(firework.Effect, ref matrix, ref position, uint.MaxValue, out effect);
            }
        }

        // --- Boundary ------------------------------------------------------------------------------------------

        public override void Draw()
        {
            if (MyAPIGateway.Utilities.IsDedicated || KothClientData.Zones.Count == 0)
                return;

            var camera = MyAPIGateway.Session?.Camera;
            if (camera == null)
                return;

            var cameraPos = camera.WorldMatrix.Translation;
            for (int i = 0; i < KothClientData.Zones.Count; i++)
            {
                var zone = KothClientData.Zones[i];
                if (!zone.Open)
                    continue;

                // Follow the zone's grid every frame when it's loaded, so a moving zone's boundary moves smoothly
                Vector3D center = KothClientData.LivePosition(ref zone);
                double showWithin = zone.Radius + BOUNDARY_SHOW_MARGIN;
                if (Vector3D.DistanceSquared(cameraPos, center) > showWithin * showWithin)
                    continue;

                var matrix = MatrixD.CreateTranslation(center);
                var color = new Color(255, 128, 31) * 0.35f;
                MySimpleObjectDraw.DrawTransparentSphere(ref matrix, (float)zone.Radius, ref color,
                    MySimpleObjectRasterizer.Wireframe, 16, null, MATERIAL_LINE, 1.0f, -1, null, BlendTypeEnum.Standard);
            }
        }
    }
}
