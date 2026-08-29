using System;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

// =========================================================================
// GV: Deserts of Kharak (GVK) Server Settings & Mechanics
// Script: KOTHNoThrusters.cs
// Author: Mike Dude
// Description: Shuts down non-NPC thrusters within 3km of active KOTH zones
// to enforce rover and ground-based vehicle combat objectives.
// =========================================================================

namespace GVK.KOTH
{
    /// <summary>
    /// Static manager tracking active KOTH No-Thruster beacons and testing coordinate positions.
    /// Non-NPC thrusters are prohibited within 3km of active KOTH zones.
    /// </summary>
    public static class KOTHNoThrusters_Manager
    {
        private static readonly List<IMyBeacon> beacons = new List<IMyBeacon>();
        private static readonly object beaconLock = new object();

        public const double DefaultRadius = 3000d;
        public const double DefaultRadiusSquared = 9000000d;

        public static void AddBeacon(IMyBeacon beacon)
        {
            if (beacon == null) return;
            lock (beaconLock)
            {
                if (!beacons.Contains(beacon))
                    beacons.Add(beacon);
            }
        }

        public static void RemoveBeacon(IMyBeacon beacon)
        {
            if (beacon == null) return;
            lock (beaconLock)
            {
                if (beacons.Contains(beacon))
                    beacons.Remove(beacon);
            }
        }

        public static bool AnyEnabled
        {
            get
            {
                lock (beaconLock)
                {
                    for (int i = 0; i < beacons.Count; i++)
                    {
                        var b = beacons[i];
                        if (b != null && !b.Closed && b.IsWorking && b.Enabled)
                            return true;
                    }
                }
                return false;
            }
        }

        public static bool IsPositionInZone(Vector3D position, double radiusSquared = DefaultRadiusSquared)
        {
            lock (beaconLock)
            {
                for (int i = 0; i < beacons.Count; i++)
                {
                    var b = beacons[i];
                    if (b != null && !b.Closed && b.IsWorking && b.Enabled)
                    {
                        if (Vector3D.DistanceSquared(position, b.GetPosition()) < radiusSquared)
                            return true;
                    }
                }
            }
            return false;
        }

        public static bool IsBlockInZone(IMyCubeBlock block, double radiusSquared = DefaultRadiusSquared)
        {
            if (block == null) return false;
            return IsPositionInZone(block.GetPosition(), radiusSquared);
        }
    }

    /// <summary>
    /// Game logic component for KOTH beacons tagged with "GVK_NoThrusterZone".
    /// </summary>
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Beacon), false, new string[] { "GVK_NoThrusterZone" })]
    public class KOTHNoThrusters_Beacon : MyGameLogicComponent
    {
        private IMyBeacon beacon;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            beacon = Entity as IMyBeacon;
            KOTHNoThrusters_Manager.AddBeacon(beacon);
        }

        public override void Close()
        {
            base.Close();
            if (beacon != null)
            {
                KOTHNoThrusters_Manager.RemoveBeacon(beacon);
            }
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();
            if (beacon != null)
            {
                KOTHNoThrusters_Manager.RemoveBeacon(beacon);
            }
        }
    }

    /// <summary>
    /// Game logic component enforcing thruster restrictions within 3km of active KOTH zones.
    /// Non-NPC thrusters are forced off upon entering the zone.
    /// </summary>
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Thrust), false)]
    public class KOTHNoThrusters_Thruster : MyGameLogicComponent
    {
        private IMyThrust thrusterblock;
        private bool isServer;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            thrusterblock = Entity as IMyThrust;
            if (thrusterblock != null)
            {
                NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
                NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
            }
        }

        public override void UpdateOnceBeforeFrame()
        {
            base.UpdateOnceBeforeFrame();

            isServer = MyAPIGateway.Multiplayer.IsServer;
            if (isServer && thrusterblock != null)
            {
                thrusterblock.IsWorkingChanged += WorkingStateChange;
            }
        }

        public override void UpdateBeforeSimulation100()
        {
            base.UpdateBeforeSimulation100();

            try
            {
                if (!isServer || thrusterblock == null || !thrusterblock.Enabled) return;
                if (thrusterblock.BlockDefinition.SubtypeId.Contains("NPC")) return;

                if (KOTHNoThrusters_Manager.IsPositionInZone(thrusterblock.GetPosition()))
                {
                    thrusterblock.Enabled = false;
                }
            }
            catch (Exception exc)
            {
                MyLog.Default.WriteLineAndConsole($"Failed checking KOTHNoThrusters thruster position: {exc}");
            }
        }

        private void WorkingStateChange(IMyCubeBlock block)
        {
            try
            {
                if (!isServer || thrusterblock == null || !thrusterblock.Enabled) return;
                if (thrusterblock.BlockDefinition.SubtypeId.Contains("NPC")) return;

                if (KOTHNoThrusters_Manager.IsPositionInZone(thrusterblock.GetPosition()))
                {
                    thrusterblock.Enabled = false;
                }
            }
            catch (Exception exc)
            {
                MyLog.Default.WriteLineAndConsole($"Failed checking KOTHNoThrusters thruster working state change: {exc}");
            }
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();
            if (thrusterblock != null)
            {
                thrusterblock.IsWorkingChanged -= WorkingStateChange;
            }
        }

        public override void Close()
        {
            base.Close();
            if (thrusterblock != null)
            {
                thrusterblock.IsWorkingChanged -= WorkingStateChange;
            }
        }
    }
}
