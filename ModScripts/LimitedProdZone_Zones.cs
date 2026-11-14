using VRageMath;

namespace LimitedProdZone
{
    // Zone spheres mirror the GVK-Derelicts [MES Zone] profiles (GVK-Universal-Zone.sbc), which are
    // canonical, and GVK_ZoneNavCommand.cs / GVK_NoPVPZone.cs; keep in sync. Zone 0 is centred on
    // Crossroads, Zones 1-3 on Z3Center.
    public static class ZoneSpheres
    {
        private static readonly Vector3D Crossroads = new Vector3D(62495.55, 28019.04, 37195.71);
        private static readonly Vector3D Z3Center = new Vector3D(3569.33, 36772.94, 26952.63);
        private const double Zone0RadiusSq = 20000.0 * 20000.0;
        private const double Zone2RadiusSq = 49300.0 * 49300.0;

        // Zone 0 or 1: inside the Zone 0 sphere, or outside the Zone 2 sphere (matches the /gvk zone bar).
        public static bool IsZone0Or1(Vector3D position)
        {
            return Vector3D.DistanceSquared(position, Crossroads) <= Zone0RadiusSq
                || Vector3D.DistanceSquared(position, Z3Center) > Zone2RadiusSq;
        }
    }
}
