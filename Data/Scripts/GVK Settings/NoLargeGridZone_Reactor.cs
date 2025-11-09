using System;
using VRage.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRageMath;
using VRage.Game.ModAPI;
using Sandbox.ModAPI;
using Sandbox.Common.ObjectBuilders;
using VRage.ObjectBuilders;
using VRage.Utils;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.Game.Screens.Helpers.RadialMenuActions;

namespace NoLargeGridZone
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Reactor), false)]
    public class NoLargeGridZone_Reactor : MyGameLogicComponent
    {
        private IMyReactor reactor;
        private bool isServer;

        public static List<IMyBeacon> beaconList = new List<IMyBeacon>();

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

                    foreach (var beacon in beaconList)
                    {
						if (beacon == null || !beacon.Enabled) continue;
                        if (reactor.CubeGrid.GridSizeEnum.Equals(MyCubeSize.Small)) continue;
						var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(reactor.OwnerId);
						if (faction != null && faction.IsEveryoneNpc()) continue; //Skip if owned by NPC
                        if (reactor.IsSameConstructAs(beacon)) continue; //Skip if powerblock is attached to NoPowerZoneBlock
						if (Vector3D.DistanceSquared(reactor.GetPosition(), beacon.GetPosition()) < 9000000) // use squared of 3000m for better performance
                        {                 
                                reactor.Enabled = false;
                                return;
                        }
                    }
                }
            }
            catch (Exception exc)
            {
                MyLog.Default.WriteLineAndConsole($"Failed looping through beacon list: {exc}");
            }
        }

        private void WorkingStateChange(IMyCubeBlock block)
        {
            if (!reactor.Enabled)
            {
                foreach (var beacon in beaconList)
                {
					if (beacon == null || !beacon.Enabled) continue;
                    if (reactor.CubeGrid.GridSizeEnum.Equals(MyCubeSize.Small)) continue;
					var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(reactor.OwnerId);
					if (faction != null && faction.IsEveryoneNpc()) continue; //Skip if owned by NPC
                    if (reactor.IsSameConstructAs(beacon)) continue; //Skip if powerblock is attached to NoPowerZoneBlock
						if (Vector3D.DistanceSquared(reactor.GetPosition(), beacon.GetPosition()) < 9000000) // use squared of 3000m for better performance
                    {
                        reactor.Enabled = false;
                    }
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
            //Unregister any handlers here
        }
    }
}
