using System.Collections.Generic;
using VRage.ModAPI;
using VRage.Game.ModAPI;
using Sandbox.ModAPI;
using VRageMath;

namespace KOTHNoSafezone
{
    public static class KOTHNoSafezone_Manager
    {
        // Beacon storage
        private static readonly List<IMyBeacon> beacons = new List<IMyBeacon>();
        private static readonly object beaconLock = new object();

        // 4000m radius (squared = 16,000,000 for better performance)
        public const double DefaultRadius = 4000d;
        public const double DefaultRadiusSquared = 16000000d;

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
    }
}

