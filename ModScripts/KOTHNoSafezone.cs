using System;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using ObjectBuilders.SafeZone;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

// =========================================================================
// GV: Deserts of Kharak (GVK) Server Settings & Mechanics
// Script: KOTHNoSafezone.cs
// Author: Mike Dude
// Description: Shuts down player-built safezone generators (siegable shields) and
// static MnM projectors within 3km of active KOTH zones to prevent safezone exploitation.
// =========================================================================

namespace GVK.KOTH
{
    /// <summary>
    /// Static manager tracking active KOTH No-Safezone beacons and testing positions.
    /// Player safezone blocks and static MnM projectors are prohibited within 3km of active KOTH zones.
    /// </summary>
    public static class KOTHNoSafezone_Manager
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
    /// Game logic component for KOTH beacons tagged with "ZoneBlock".
    /// </summary>
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Beacon), false, new string[] { "ZoneBlock" })]
    public class KOTHNoSafezone_Beacon : MyGameLogicComponent
    {
        private IMyBeacon beacon;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            beacon = Entity as IMyBeacon;
            KOTHNoSafezone_Manager.AddBeacon(beacon);
        }

        public override void Close()
        {
            base.Close();
            if (beacon != null)
            {
                KOTHNoSafezone_Manager.RemoveBeacon(beacon);
            }
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();
            if (beacon != null)
            {
                KOTHNoSafezone_Manager.RemoveBeacon(beacon);
            }
        }
    }

    /// <summary>
    /// Game logic component disabling player safezone blocks within 3km of KOTH beacons.
    /// </summary>
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_SafeZoneBlock), false)]
    public class KOTHNoSafezone_SafeZoneBlock : MyGameLogicComponent
    {
        private IMySafeZoneBlock safezoneblock;
        private bool isServer;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            safezoneblock = Entity as IMySafeZoneBlock;
            if (safezoneblock != null)
            {
                NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
                NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
            }
        }

        public override void UpdateOnceBeforeFrame()
        {
            base.UpdateOnceBeforeFrame();

            isServer = MyAPIGateway.Multiplayer.IsServer;
            if (isServer && safezoneblock != null)
            {
                safezoneblock.IsWorkingChanged += WorkingStateChange;
            }
        }

        public override void UpdateBeforeSimulation100()
        {
            base.UpdateBeforeSimulation100();

            try
            {
                if (!isServer || safezoneblock == null || !safezoneblock.Enabled) return;

                if (KOTHNoSafezone_Manager.IsPositionInZone(safezoneblock.GetPosition()))
                {
                    safezoneblock.Enabled = false;
                }
            }
            catch (Exception exc)
            {
                MyLog.Default.WriteLineAndConsole($"Failed checking KOTH safezone position: {exc}");
            }
        }

        private void WorkingStateChange(IMyCubeBlock block)
        {
            try
            {
                if (!isServer || safezoneblock == null || !safezoneblock.Enabled) return;

                if (KOTHNoSafezone_Manager.IsPositionInZone(safezoneblock.GetPosition()))
                {
                    safezoneblock.Enabled = false;
                }
            }
            catch (Exception exc)
            {
                MyLog.Default.WriteLineAndConsole($"Failed checking KOTH safezone working state change: {exc}");
            }
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();
            if (safezoneblock != null)
            {
                safezoneblock.IsWorkingChanged -= WorkingStateChange;
            }
        }

        public override void Close()
        {
            base.Close();
            if (safezoneblock != null)
            {
                safezoneblock.IsWorkingChanged -= WorkingStateChange;
            }
        }
    }

    /// <summary>
    /// Game logic component disabling static MnM projectors within 3km of KOTH beacons.
    /// </summary>
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Projector), false)]
    public class KOTHNoSafezone_ProjectorBlock : MyGameLogicComponent
    {
        private IMyProjector projectorblock;
        private bool isServer;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            projectorblock = Entity as IMyProjector;
            if (projectorblock != null)
            {
                NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
                NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
            }
        }

        public override void UpdateOnceBeforeFrame()
        {
            base.UpdateOnceBeforeFrame();

            isServer = MyAPIGateway.Multiplayer.IsServer;
            if (isServer && projectorblock != null)
            {
                projectorblock.IsWorkingChanged += WorkingStateChange;
            }
        }

        public override void UpdateBeforeSimulation100()
        {
            base.UpdateBeforeSimulation100();

            try
            {
                if (!isServer || projectorblock == null || !projectorblock.Enabled) return;

                string strSubBlockType = projectorblock.BlockDefinition.SubtypeId.ToString();
                if (strSubBlockType.Contains("MnM") && projectorblock.CubeGrid.IsStatic)
                {
                    if (KOTHNoSafezone_Manager.IsPositionInZone(projectorblock.GetPosition()))
                    {
                        projectorblock.Enabled = false;
                    }
                }
            }
            catch (Exception exc)
            {
                MyLog.Default.WriteLineAndConsole($"Failed checking KOTH projector position: {exc}");
            }
        }

        private void WorkingStateChange(IMyCubeBlock block)
        {
            try
            {
                if (!isServer || projectorblock == null || !projectorblock.Enabled) return;

                string strSubBlockType = projectorblock.BlockDefinition.SubtypeId.ToString();
                if (strSubBlockType.Contains("MnM") && projectorblock.CubeGrid.IsStatic)
                {
                    if (KOTHNoSafezone_Manager.IsPositionInZone(projectorblock.GetPosition()))
                    {
                        projectorblock.Enabled = false;
                    }
                }
            }
            catch (Exception exc)
            {
                MyLog.Default.WriteLineAndConsole($"Failed checking KOTH projector working state change: {exc}");
            }
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();
            if (projectorblock != null)
            {
                projectorblock.IsWorkingChanged -= WorkingStateChange;
            }
        }

        public override void Close()
        {
            base.Close();
            if (projectorblock != null)
            {
                projectorblock.IsWorkingChanged -= WorkingStateChange;
            }
        }
    }
}
