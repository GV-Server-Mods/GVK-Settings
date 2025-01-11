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
        private IMyPlayer client;
        private bool isServer;
        private bool inZone;
        public static List<IMyBeacon> beaconList = new List<IMyBeacon>();

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            battery = (Entity as IMyBatteryBlock);
            if (battery != null)
            {
                NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
                NeedsUpdate |= MyEntityUpdateEnum.EACH_10TH_FRAME;
            }
        }

        public override void UpdateOnceBeforeFrame()
        {
            base.UpdateOnceBeforeFrame();

            isServer = MyAPIGateway.Multiplayer.IsServer;
            client = MyAPIGateway.Session.LocalHumanPlayer;

            if (isServer)
            {
                battery.IsWorkingChanged += WorkingStateChange;
            }
        }

        public override void UpdateBeforeSimulation10()
        {
            base.UpdateBeforeSimulation10();

            try
            {
                if (isServer)
                {
                    if (!battery.Enabled) return;

                    foreach (var beacon in beaconList)
                    {
                        if (beacon == null) continue;
                        if (!beacon.Enabled) continue;
                        if (battery.CubeGrid.GridSizeEnum.Equals(MyCubeSize.Small)) continue;
						var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(battery.OwnerId);
						if (faction != null && faction.IsEveryoneNpc()) continue;
                        //if (Vector3D.Distance(battery.GetPosition(), beacon.GetPosition()) < beacon.Radius)
                        if (Vector3D.Distance(battery.GetPosition(), beacon.GetPosition()) < 3000) //1km + SZ radius buffer
                        {
                            inZone = true;
                            battery.Enabled = false;
                            //ApplyDamage();
                            return;
                        }
                    }

                    inZone = false;
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
                    if (beacon == null) continue;
                    if (!beacon.Enabled) continue;
					if (battery.CubeGrid.GridSizeEnum.Equals(MyCubeSize.Small)) continue;
					var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(battery.OwnerId);
					if (faction != null && faction.IsEveryoneNpc()) continue;
                    //if (Vector3D.Distance(battery.GetPosition(), beacon.GetPosition()) < beacon.Radius)
                    if (Vector3D.Distance(battery.GetPosition(), beacon.GetPosition()) < 3000) //1km + SZ radius buffer
                    {
                        battery.Enabled = false;
                        //ApplyDamage();
                    }

                }

            }

        }

        private void ApplyDamage()
        {
            try
            {
                IMySlimBlock b = battery.SlimBlock;
                IMyEntity entity = battery.Parent;
                IMyCubeGrid grid = entity as IMyCubeGrid;
                var damage = grid.GridSizeEnum.Equals(MyCubeSize.Large) ? 0.5f : 0.05f;
                b.DecreaseMountLevel(damage, null, true);
                b.ApplyAccumulatedDamage();
            }
            catch (Exception exc)
            {
                MyLog.Default.WriteLineAndConsole($"Failed to apply damage: {exc}");
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
