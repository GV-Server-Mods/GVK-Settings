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
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_HydrogenEngine), false)]
    public class KOTHNoLargeGrid_HydrogenEngine : MyGameLogicComponent
    {
        private IMyPowerProducer fueled;
        private bool isServer;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            fueled = (Entity as IMyPowerProducer);
            if (fueled != null)
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
                fueled.IsWorkingChanged += WorkingStateChange;
            }
        }

        public override void UpdateBeforeSimulation100()
        {
            base.UpdateBeforeSimulation100();

            try
            {
                if (isServer)
                {
                    if (!fueled.Enabled) return;
                    if (fueled.CubeGrid.GridSizeEnum.Equals(MyCubeSize.Small)) return;

                    var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(fueled.OwnerId);
                    if (faction != null && faction.IsEveryoneNpc()) return; // Skip if owned by NPC

                    if (KOTHNoLargeGrid_Manager.IsBlockInZone(fueled))
                    {
                        fueled.Enabled = false;
                        return;
                    }
                }
            }
            catch (Exception exc)
            {
                MyLog.Default.WriteLineAndConsole($"Failed checking KOTH hydrogen engine position: {exc}");
            }
        }

        private void WorkingStateChange(IMyCubeBlock block)
        {
            if (fueled.Enabled)
            {
                if (fueled.CubeGrid.GridSizeEnum.Equals(MyCubeSize.Small)) return;

                var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(fueled.OwnerId);
                if (faction != null && faction.IsEveryoneNpc()) return;

                if (KOTHNoLargeGrid_Manager.IsBlockInZone(fueled))
                {
                    fueled.Enabled = false;
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
                    fueled.IsWorkingChanged -= WorkingStateChange;
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

