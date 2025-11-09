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

namespace LimitedProdZone
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Drill), false)]
    public class LimitedProdZone_StaticDrill : MyGameLogicComponent
    {
        private IMyShipDrill staticDrill;
        private bool isServer;
        public static List<IMyBeacon> beaconList = new List<IMyBeacon>();
        private Vector3D limitedProdCenterCoord = new Vector3D(62495, 28019, 37195); //[Coordinates:{X:62495.55 Y:28019.04 Z:37195.71}]

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            staticDrill = (Entity as IMyShipDrill);
            if (staticDrill != null)
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
                staticDrill.IsWorkingChanged += WorkingStateChange;
            }
        }

        public override void UpdateBeforeSimulation100()
        {
            base.UpdateBeforeSimulation100();

            try
            {
                if (isServer)
                {
                    if (!staticDrill.Enabled) return;

                    foreach (var beacon in beaconList)
                    {                        
						if (beacon == null || !beacon.Enabled) continue;
						if (Vector3D.DistanceSquared(staticDrill.GetPosition(), limitedProdCenterCoord) < 400000000) // use squared of 20,000m for better performance
                        {
                            string strSubBlockType = staticDrill.BlockDefinition.SubtypeId.ToString();
                            bool isBasicStaticDrill = false;
                            isBasicStaticDrill = strSubBlockType.Contains("BasicStaticDrill");
                            if (isBasicStaticDrill == false)
                            {
								staticDrill.Enabled = false;
								return;
                            }
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
            if (!staticDrill.Enabled)
            {
                foreach (var beacon in beaconList)
                {
					if (beacon == null || !beacon.Enabled) continue;
					if (Vector3D.DistanceSquared(staticDrill.GetPosition(), limitedProdCenterCoord) < 400000000) // use squared of 20,000m for better performance
                    {
                        string strSubBlockType = staticDrill.BlockDefinition.SubtypeId.ToString();
                        Boolean isBasicStaticDrill = false;
                        isBasicStaticDrill = strSubBlockType.Contains("BasicStaticDrill");
                        if (isBasicStaticDrill == false)
                        {
							staticDrill.Enabled = false;
                        }
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

            var Block = Entity as IMyShipDrill;

            if (Block == null) return;

            try
            {
                if (isServer)
                {
                    staticDrill.IsWorkingChanged -= WorkingStateChange;
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
