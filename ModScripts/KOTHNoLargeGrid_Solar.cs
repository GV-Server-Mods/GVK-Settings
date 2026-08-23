using System;
using VRage.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using Sandbox.ModAPI;
using Sandbox.Common.ObjectBuilders;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace KOTHNoLargeGrid
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_SolarPanel), false)]
    public class KOTHNoLargeGrid_Solar : MyGameLogicComponent
    {
        private IMyPowerProducer solar;
        private bool isServer;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            solar = (Entity as IMyPowerProducer);
            if (solar != null)
            {
                NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
                NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
            }
        }

        public override void UpdateOnceBeforeFrame()
        {
            base.UpdateOnceBeforeFrame();

            isServer = MyAPIGateway.Multiplayer.IsServer;

            if (isServer)
            {
                solar.IsWorkingChanged += WorkingStateChange;
            }
        }

        public override void UpdateBeforeSimulation100()
        {
            base.UpdateBeforeSimulation100();

            try
            {
                if (isServer)
                {
                    if (!solar.Enabled) return;
                    if (solar.CubeGrid.GridSizeEnum.Equals(MyCubeSize.Small)) return;

                    var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(solar.OwnerId);
                    if (faction != null && faction.IsEveryoneNpc()) return; // Skip if owned by NPC

                    if (KOTHNoLargeGrid_Manager.IsBlockInZone(solar))
                    {
                        solar.Enabled = false;
                        return;
                    }
                }
            }
            catch (Exception exc)
            {
                MyLog.Default.WriteLineAndConsole($"Failed checking KOTH solar position: {exc}");
            }
        }

        private void WorkingStateChange(IMyCubeBlock block)
        {
            if (solar.Enabled)
            {
                if (solar.CubeGrid.GridSizeEnum.Equals(MyCubeSize.Small)) return;

                var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(solar.OwnerId);
                if (faction != null && faction.IsEveryoneNpc()) return;

                if (KOTHNoLargeGrid_Manager.IsBlockInZone(solar))
                {
                    solar.Enabled = false;
                }
            }
        }

        public override void Close()
        {
            if (Entity == null)
                return;
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();

            if (Entity == null || Entity.MarkedForClose)
            {
                return;
            }

            var Block = Entity as IMyPowerProducer;

            if (Block == null) return;

            try
            {
                if (isServer)
                {
                    solar.IsWorkingChanged -= WorkingStateChange;
                }
            }
            catch (Exception exc)
            {
                MyLog.Default.WriteLineAndConsole($"Failed to deregister event: {exc}");
                return;
            }
        }
    }
}

