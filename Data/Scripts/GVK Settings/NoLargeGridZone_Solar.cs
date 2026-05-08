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

namespace NoLargeGridZone
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_SolarPanel), false)]
    public class NoLargeGridZone_Solar : MyGameLogicComponent
    {
        private IMyPowerProducer solar;
        private bool isServer;
        public static List<IMyBeacon> beaconList = new List<IMyBeacon>();

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

                    foreach (var beacon in beaconList)
                    {
						if (beacon == null || !beacon.Enabled) continue;
                        if (solar.CubeGrid.GridSizeEnum.Equals(MyCubeSize.Small)) continue;
						var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(solar.OwnerId);
						if (faction != null && faction.IsEveryoneNpc()) continue; //Skip if owned by NPC
                        if (solar.IsSameConstructAs(beacon)) continue; //Skip if powerblock is attached to NoPowerZoneBlock
						if (Vector3D.DistanceSquared(solar.GetPosition(), beacon.GetPosition()) < 9000000) // use squared of 3000m for better performance
                        {
                            solar.Enabled = false;
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
            if (solar.Enabled)
            {
                foreach (var beacon in beaconList)
                {
					if (beacon == null || !beacon.Enabled) continue;
					if (solar.CubeGrid.GridSizeEnum.Equals(MyCubeSize.Small)) continue;
					var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(solar.OwnerId);
					if (faction != null && faction.IsEveryoneNpc()) continue; //Skip if owned by NPC
					if (solar.IsSameConstructAs(beacon)) continue; //Skip if powerblock is attached to NoPowerZoneBlock
					if (Vector3D.DistanceSquared(solar.GetPosition(), beacon.GetPosition()) < 9000000) // use squared of 3000m for better performance
                    {
                        solar.Enabled = false;
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
            //Unregister any handlers here
        }
    }
}
