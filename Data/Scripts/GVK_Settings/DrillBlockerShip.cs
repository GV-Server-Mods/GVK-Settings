/*using System;
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
using Sandbox.Game.Weapons;

namespace DrillBlocker_NoDrill
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Drill), false)]
    public class DrillBlocker_Drill : MyGameLogicComponent
    {

        private IMyShipDrill shipDrill;
        private IMyPlayer client;
        private bool isServer;
        private bool inZone;
        public static List<IMyBeacon> beaconList = new List<IMyBeacon>();

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            shipDrill = (Entity as IMyShipDrill);
            if (shipDrill != null)
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
                shipDrill.IsWorkingChanged += shipDrillWorkingStateChange;
            }
        }

        public override void UpdateBeforeSimulation10()
        {
            base.UpdateBeforeSimulation10();

            try
            {
                if (isServer)
                {
                    //if (!shipDrill.Enabled) return;

                    foreach (var beacon in beaconList)
                    {

                        if (beacon == null) continue;
                        if (!beacon.Enabled) continue;

                        if (Vector3D.Distance(shipDrill.GetPosition(), beacon.GetPosition()) < beacon.Radius)
                        {
                            inZone = true;
                            shipDrill.Enabled = false;
                            ApplyDamageDrill();
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

        private void shipDrillWorkingStateChange(IMyCubeBlock block)
        {
            if (!shipDrill.Enabled)
            {
                foreach (var beacon in beaconList)
                {
                    if (beacon == null) continue;
                    if (!beacon.Enabled) continue;
                    if (Vector3D.Distance(shipDrill.GetPosition(), beacon.GetPosition()) < beacon.Radius)
                    {
                        shipDrill.Enabled = false;
                        ApplyDamageDrill();
                    }

                }

            }

        }

        private void ApplyDamageDrill()
        {
            try
            {
                IMySlimBlock b = shipDrill.SlimBlock;
                IMyEntity entity = shipDrill.Parent;
                IMyCubeGrid grid = entity as IMyCubeGrid;
                var damage = grid.GridSizeEnum.Equals(MyCubeSize.Large) ? 1.0f : 0.5f;
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

            var D_Block = Entity as IMyShipDrill;

            if (D_Block == null) return;

            try
            {
                if (isServer)
                {
                    shipDrill.IsWorkingChanged -= shipDrillWorkingStateChange;
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
*/