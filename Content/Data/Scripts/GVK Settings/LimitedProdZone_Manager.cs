using System.Collections.Generic;
using VRage.ModAPI;
using VRage.Game.ModAPI;
using Sandbox.ModAPI;
using VRageMath;

namespace LimitedProdZone
{
    public static class LimitedProdZone_Manager
    {
        // Beacon storage
        private static readonly List<IMyBeacon> beacons = new List<IMyBeacon>();
        private static readonly object beaconLock = new object();

        // Shared center coordinate and radius constants (squared values for performance)
        public static readonly Vector3D LimitedProdCenterCoord = new Vector3D(62495, 28019, 37195); //[Coordinates:{X:62495.55 Y:28019.04 Z:37195.71}]
        public const double ProductionRadiusSquared = 1225000000d; // 35,000^2
        public const double WeaponRadiusSquared = 400000000d; // 20,000^2

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
                        if (b != null && b.Enabled)
                            return true;
                    }
                }
                return false;
            }
        }
    }
}
