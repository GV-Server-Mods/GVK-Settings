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

namespace KOTHNoThrusters
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Thrust), false)]
    public class KOTHNoThrusters_Thruster : MyGameLogicComponent
    {
        private IMyThrust thrusterblock;
        private bool isServer;
        private bool inZone;
        public static List<IMyBeacon> beaconList = new List<IMyBeacon>();

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            thrusterblock = (Entity as IMyThrust);
            if (thrusterblock != null)
            {
                NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
                NeedsUpdate |= MyEntityUpdateEnum.EACH_10TH_FRAME;
            }
        }

        public override void UpdateOnceBeforeFrame()
        {
            base.UpdateOnceBeforeFrame();

            isServer = MyAPIGateway.Multiplayer.IsServer;

            if (isServer)
            {
                thrusterblock.IsWorkingChanged += WorkingStateChange;
            }
        }

        public override void UpdateBeforeSimulation10()
        {
            base.UpdateBeforeSimulation10();

            try
            {
                if (isServer)
                {
                    if (!thrusterblock.Enabled) return;

                    foreach (var beacon in beaconList)
                    {                        
                        if (beacon == null) continue;
                        if (!beacon.Enabled) continue;
						if (thrusterblock.BlockDefinition.SubtypeId.Contains("NPC")) continue;
                        if (Vector3D.Distance(thrusterblock.GetPosition(), beacon.GetPosition()) < 3000) //1km + SZ radius buffer
                        {
							thrusterblock.Enabled = false;
							//ApplyDamageThrust();
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
            if (!thrusterblock.Enabled)
            {
                foreach (var beacon in beaconList)
                {
                    if (beacon == null) continue;
                    if (!beacon.Enabled) continue;
					if (thrusterblock.BlockDefinition.SubtypeId.Contains("NPC")) continue;
                    if (Vector3D.Distance(thrusterblock.GetPosition(), beacon.GetPosition()) < 3000) //1km + SZ radius buffer
                    {
						thrusterblock.Enabled = false;
                        //ApplyDamageThrust();
                    }
                }               
            }
        }

        private void ApplyDamageThrust()
        {
            try
            {
                IMySlimBlock b = thrusterblock.SlimBlock;
                IMyEntity entity = thrusterblock.Parent;
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

            var Block = Entity as IMyThrust;

            if (Block == null) return;

            try
            {
                if (isServer)
                {
                    thrusterblock.IsWorkingChanged -= WorkingStateChange;
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
