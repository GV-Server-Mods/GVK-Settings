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
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Reactor), false)]
    public class KOTHNoLargeGrid_Reactor : MyGameLogicComponent
    {
        private IMyReactor reactor;
        private bool isServer;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            reactor = (Entity as IMyReactor);
            if (reactor != null)
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
                reactor.IsWorkingChanged += WorkingStateChange;
            }
        }

        public override void UpdateBeforeSimulation100()
        {
            base.UpdateBeforeSimulation100();

            try
            {
                if (isServer)
                {
                    if (!reactor.Enabled) return;
                    if (reactor.CubeGrid.GridSizeEnum.Equals(MyCubeSize.Small)) return;

                    var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(reactor.OwnerId);
                    if (faction != null && faction.IsEveryoneNpc()) return; // Skip if owned by NPC

                    if (KOTHNoLargeGrid_Manager.IsBlockInZone(reactor))
                    {
                        reactor.Enabled = false;
                        return;
                    }
                }
            }
            catch (Exception exc)
            {
                MyLog.Default.WriteLineAndConsole($"Failed checking KOTH reactor position: {exc}");
            }
        }

        private void WorkingStateChange(IMyCubeBlock block)
        {
            if (reactor.Enabled)
            {
                if (reactor.CubeGrid.GridSizeEnum.Equals(MyCubeSize.Small)) return;

                var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(reactor.OwnerId);
                if (faction != null && faction.IsEveryoneNpc()) return;

                if (KOTHNoLargeGrid_Manager.IsBlockInZone(reactor))
                {
                    reactor.Enabled = false;
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

            var Block = Entity as IMyReactor;

            if (Block == null) return;

            try
            {
                if (isServer)
                {
                    reactor.IsWorkingChanged -= WorkingStateChange;
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

