using System;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

// =========================================================================
// GV: Deserts of Kharak (GVK) Server Settings & Mechanics
// Script: KOTHNoLargeGrid.cs
// Author: Mike Dude
// Description: Shuts down non-NPC large-grid power generation (batteries, hydrogen engines,
// reactors, and solar panels) within 3km of active KOTH zones while preserving small grids.
// =========================================================================

namespace GVK.KOTH
{
    /// <summary>
    /// Static manager tracking active KOTH No-Large-Grid beacons and testing block positions.
    /// Non-NPC large grid power blocks are disabled within 3km of active KOTH zones.
    /// </summary>
    public static class KOTHNoLargeGrid_Manager
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

        public static bool IsBlockInZone(IMyCubeBlock block, double radiusSquared = DefaultRadiusSquared)
        {
            if (block == null) return false;
            Vector3D blockPos = block.GetPosition();

            lock (beaconLock)
            {
                for (int i = 0; i < beacons.Count; i++)
                {
                    var b = beacons[i];
                    if (b != null && !b.Closed && b.IsWorking && b.Enabled)
                    {
                        if (Vector3D.DistanceSquared(blockPos, b.GetPosition()) < radiusSquared)
                            return true;
                    }
                }
            }
            return false;
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
    }

    /// <summary>
    /// Game logic component for KOTH beacons tagged with "GVK_NoLargeGridZone".
    /// </summary>
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Beacon), false, new string[] { "GVK_NoLargeGridZone" })]
    public class KOTHNoLargeGrid_Beacon : MyGameLogicComponent
    {
        private IMyBeacon beacon;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            beacon = Entity as IMyBeacon;
            KOTHNoLargeGrid_Manager.AddBeacon(beacon);
        }

        public override void Close()
        {
            base.Close();
            if (beacon != null)
            {
                KOTHNoLargeGrid_Manager.RemoveBeacon(beacon);
            }
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();
            if (beacon != null)
            {
                KOTHNoLargeGrid_Manager.RemoveBeacon(beacon);
            }
        }
    }

    /// <summary>
    /// Base game logic component that enforces KOTH Large Grid restrictions on power blocks.
    /// Non-NPC large grid batteries, reactors, hydrogen engines, and solar panels are disabled within KOTH zones.
    /// </summary>
    public abstract class KOTHNoLargeGrid_PowerBase : MyGameLogicComponent
    {
        protected IMyFunctionalBlock powerBlock;
        protected bool isServer;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            powerBlock = Entity as IMyFunctionalBlock;
            if (powerBlock != null)
            {
                NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
                NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
            }
        }

        public override void UpdateOnceBeforeFrame()
        {
            base.UpdateOnceBeforeFrame();

            isServer = MyAPIGateway.Multiplayer.IsServer;
            if (isServer && powerBlock != null)
            {
                powerBlock.IsWorkingChanged += WorkingStateChange;
            }
        }

        public override void UpdateBeforeSimulation100()
        {
            base.UpdateBeforeSimulation100();

            try
            {
                if (!isServer || powerBlock == null || !powerBlock.Enabled) return;
                if (powerBlock.CubeGrid.GridSizeEnum.Equals(MyCubeSize.Small)) return;

                var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(powerBlock.OwnerId);
                if (faction != null && faction.IsEveryoneNpc()) return;

                if (KOTHNoLargeGrid_Manager.IsBlockInZone(powerBlock))
                {
                    powerBlock.Enabled = false;
                }
            }
            catch (Exception exc)
            {
                MyLog.Default.WriteLineAndConsole($"Failed checking KOTHNoLargeGrid power block position: {exc}");
            }
        }

        private void WorkingStateChange(IMyCubeBlock block)
        {
            try
            {
                if (!isServer || powerBlock == null || !powerBlock.Enabled) return;
                if (powerBlock.CubeGrid.GridSizeEnum.Equals(MyCubeSize.Small)) return;

                var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(powerBlock.OwnerId);
                if (faction != null && faction.IsEveryoneNpc()) return;

                if (KOTHNoLargeGrid_Manager.IsBlockInZone(powerBlock))
                {
                    powerBlock.Enabled = false;
                }
            }
            catch (Exception exc)
            {
                MyLog.Default.WriteLineAndConsole($"Failed checking KOTHNoLargeGrid power block working state change: {exc}");
            }
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();
            if (powerBlock != null)
            {
                powerBlock.IsWorkingChanged -= WorkingStateChange;
            }
        }

        public override void Close()
        {
            base.Close();
            if (powerBlock != null)
            {
                powerBlock.IsWorkingChanged -= WorkingStateChange;
            }
        }
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_BatteryBlock), false)]
    public class KOTHNoLargeGrid_Battery : KOTHNoLargeGrid_PowerBase { }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_HydrogenEngine), false)]
    public class KOTHNoLargeGrid_HydrogenEngine : KOTHNoLargeGrid_PowerBase { }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Reactor), false)]
    public class KOTHNoLargeGrid_Reactor : KOTHNoLargeGrid_PowerBase { }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_SolarPanel), false)]
    public class KOTHNoLargeGrid_Solar : KOTHNoLargeGrid_PowerBase { }
}
