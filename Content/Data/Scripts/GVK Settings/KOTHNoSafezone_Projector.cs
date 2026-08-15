using SpaceEngineers.Game.ModAPI;
using System;
using VRage.ModAPI;
using VRage.Game.Components;
using VRageMath;
using VRage.Game.ModAPI;
using Sandbox.ModAPI;
using Sandbox.Common.ObjectBuilders;
using VRage.ObjectBuilders;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;
using ObjectBuilders.SafeZone;
using ProtoBuf;
using VRage;
using VRage.Game;
using VRage.Game.ObjectBuilders;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Network;
using VRage.Serialization;
using VRage.Utils;

namespace KOTHNoSafezone
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Projector), false)]
    public class KOTHNoSafezone_ProjectorBlock : MyGameLogicComponent
    {
        private IMyProjector projectorblock;
        private bool isServer;
        public static List<IMyBeacon> beaconList = new List<IMyBeacon>();

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            projectorblock = (Entity as IMyProjector);
            if (projectorblock != null)
            {
                NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
                NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
            }
        }

        public override void UpdateOnceBeforeFrame()
        {
            base.UpdateOnceBeforeFrame();

            isServer = MyAPIGateway.Multiplayer.IsServer;
            //client = MyAPIGateway.Session.LocalHumanPlayer;

            if (isServer)
            {
                projectorblock.IsWorkingChanged += WorkingStateChange;
            }
        }

        public override void UpdateBeforeSimulation100()
        {
            base.UpdateBeforeSimulation100();

            try
            {
                if (isServer)
                {
                    if (!projectorblock.Enabled) return;

                    foreach (var beacon in beaconList)
                    {                        
						if (beacon == null || !beacon.Enabled) continue;
 						if (Vector3D.DistanceSquared(projectorblock.GetPosition(), beacon.GetPosition()) < 16000000) // use squared of 4000m for better performance
                        {
							string strSubBlockType = projectorblock.BlockDefinition.SubtypeId.ToString();
							bool isMnM = false;
							isMnM = strSubBlockType.Contains("MnM");
							if (isMnM == true && projectorblock.CubeGrid.IsStatic)
							{
								projectorblock.Enabled = false;
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
            if (projectorblock.Enabled)
            {
                foreach (var beacon in beaconList)
                {
					if (beacon == null || !beacon.Enabled) continue;
 						if (Vector3D.DistanceSquared(projectorblock.GetPosition(), beacon.GetPosition()) < 16000000) // use squared of 4000m for better performance
                    {
						string strSubBlockType = projectorblock.BlockDefinition.SubtypeId.ToString();
						bool isMnM = false;
						isMnM = strSubBlockType.Contains("MnM");
						if (isMnM == true && projectorblock.CubeGrid.IsStatic)
						{
							projectorblock.Enabled = false;
							return;
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

            var Block = Entity as IMyProjector;

            if (Block == null) return;

            try
            {
                if (isServer)
                {
                    projectorblock.IsWorkingChanged -= WorkingStateChange;
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
