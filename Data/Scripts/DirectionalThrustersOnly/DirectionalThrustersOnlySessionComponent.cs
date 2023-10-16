using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;

namespace DirectionalThrustersOnly
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class DirectionalThrustersOnlySessionComponent : MySessionComponentBase
    {
        public static DirectionalThrustersOnlySessionComponent Instance { get; private set; }
        public DirectionalThrustersOnlyConfiguration Config { get; private set; }

        private readonly List<long> npcFactionMembers = new List<long>();

        public override void LoadData()
        {
            if (MyAPIGateway.Session.IsServer || !MyAPIGateway.Multiplayer.MultiplayerActive)
            {
                Config = DirectionalThrustersOnlyConfiguration.LoadSettings();
            }

            Instance = this;

            foreach (var faction in MyAPIGateway.Session.Factions.Factions.Values)
            {
                if (!faction.IsEveryoneNpc())
                {
                    continue;
                }

                foreach (var memberId in faction.Members.Keys)
                {
                    npcFactionMembers.Add(memberId);
                }
            }

        }

        protected override void UnloadData()
        {
            Instance = null;
        }

        internal bool IsGridNPCOwned(IMyCubeGrid cubeGrid)
        {
            foreach (var owner in cubeGrid.BigOwners)
            {
                if (npcFactionMembers.Contains(owner))
                {
                    return true;
                }
            }
            return false;
        }
    }
}