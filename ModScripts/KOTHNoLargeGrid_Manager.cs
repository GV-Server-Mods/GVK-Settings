using System.Collections.Generic;
using VRage.ModAPI;
using VRage.Game.ModAPI;
using Sandbox.ModAPI;
using VRageMath;

namespace KOTHNoLargeGrid
{
    public static class KOTHNoLargeGrid_Manager
    {
        // Beacon storage
        private static readonly List<IMyBeacon> beacons = new List<IMyBeacon>();
        private static readonly object beaconLock = new object();

        // 3000m radius (squared = 9,000,000 for better performance)
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
                        var termBlock = block as IMyTerminalBlock;
                        if (termBlock != null && termBlock.IsSameConstructAs(b)) continue; // Skip if power block is attached to the beacon's own grid construct

                        if (Vector3D.DistanceSquared(blockPos, b.GetPosition()) < radiusSquared)
                            return true;
                    }
                }
            }
            return false;
        }
    }
}
