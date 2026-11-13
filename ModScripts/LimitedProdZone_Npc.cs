using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace LimitedProdZone
{
    // Shared NPC rules for the zone scripts (hub #787, decision D12).
    public static class ZoneNpc
    {
        public const string NpcWeaponName = "NPC Weapon";
        // 20.5 km squared: 500 m past the Zone 0 line, so a ship on the line doesn't flicker.
        public const double ReArmDistanceSq = 420250000;
        private const int RemoteCacheFrames = 600;

        private static readonly Dictionary<long, KeyValuePair<int, bool>> RemoteCache = new Dictionary<long, KeyValuePair<int, bool>>();
        private static readonly List<IMyRemoteControl> Remotes = new List<IMyRemoteControl>();

        // NPC identities have no Steam id; offline players keep theirs. Owner 0 (nobody) is not NPC.
        public static bool IsNpcOwner(long ownerId)
        {
            return ownerId != 0 && MyAPIGateway.Players.TryGetSteamId(ownerId) == 0;
        }

        // An NPC-owned "NPC Weapon" that is off, outside Zone 0, on a grid an NPC still controls.
        public static bool ShouldReArm(IMyFunctionalBlock block, Vector3D zoneCenter)
        {
            if (block.Enabled || block.CustomName != NpcWeaponName || !IsNpcOwner(block.OwnerId)) return false;
            if (Vector3D.DistanceSquared(block.GetPosition(), zoneCenter) <= ReArmDistanceSq) return false;
            return HasWorkingNpcRemote(block.CubeGrid);
        }

        // Cached per grid for 10 s, so a defeated ship's weapons don't each scan the grid every 100 frames.
        private static bool HasWorkingNpcRemote(IMyCubeGrid grid)
        {
            if (grid == null) return false;
            int frame = MyAPIGateway.Session.GameplayFrameCounter;
            KeyValuePair<int, bool> cached;
            if (RemoteCache.TryGetValue(grid.EntityId, out cached) && frame - cached.Key < RemoteCacheFrames) return cached.Value;

            bool found = false;
            var terminal = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
            if (terminal != null)
            {
                Remotes.Clear();
                terminal.GetBlocksOfType(Remotes, r => r.IsWorking && IsNpcOwner(r.OwnerId));
                found = Remotes.Count > 0;
                Remotes.Clear();
            }

            if (RemoteCache.Count > 512) RemoteCache.Clear();
            RemoteCache[grid.EntityId] = new KeyValuePair<int, bool>(frame, found);
            return found;
        }
    }
}
