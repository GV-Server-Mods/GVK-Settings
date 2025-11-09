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
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_BatteryBlock), false)]
    public class NoLargeGridZone_Battery : MyGameLogicComponent
    {
        private IMyBatteryBlock battery;
        private bool isServer;
        public static List<IMyBeacon> beaconList = new List<IMyBeacon>();

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            battery = (Entity as IMyBatteryBlock);
            if (battery != null)
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
                battery.IsWorkingChanged += WorkingStateChange;
            }
        }

        public override void UpdateBeforeSimulation100()
        {
            base.UpdateBeforeSimulation100();

            try
            {
                if (isServer)
                {
                    if (!battery.Enabled) return;

                    foreach (var beacon in beaconList)
                    {
						if (beacon == null || !beacon.Enabled) continue;
                        if (battery.CubeGrid.GridSizeEnum.Equals(MyCubeSize.Small)) continue;
						var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(battery.OwnerId);
						if (faction != null && faction.IsEveryoneNpc()) continue;
                        if (Vector3D.DistanceSquared(battery.GetPosition(), beacon.GetPosition()) < 9000000) // use squared of 3000m for better performance
                        {
                            battery.Enabled = false;
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
            if (!battery.Enabled)
            {
                foreach (var beacon in beaconList)
                {
					if (beacon == null || !beacon.Enabled) continue;
					if (battery.CubeGrid.GridSizeEnum.Equals(MyCubeSize.Small)) continue;
					var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(battery.OwnerId);
					if (faction != null && faction.IsEveryoneNpc()) continue;
					if (Vector3D.DistanceSquared(battery.GetPosition(), beacon.GetPosition()) < 9000000) // use squared of 3000m for better performance
                    {
                        battery.Enabled = false;
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

            var Block = Entity as IMyBatteryBlock;

            if (Block == null) return;

            try
            {
                if (isServer)
                {
                    battery.IsWorkingChanged -= WorkingStateChange;
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
