using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using VRage.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRageMath;
using VRage.Game.ModAPI;
using Sandbox.ModAPI;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.Game.Components;
using Sandbox.Common.ObjectBuilders;
using VRage.ObjectBuilders;
using System.IO;
using System.Runtime.Remoting.Messaging;
using Sandbox.Game.Entities;
using Sandbox.Game;
using VRage.Utils;

namespace KOTHNoThrusters
{
    [MyEntityComponentDescriptor(typeof(Sandbox.Common.ObjectBuilders.MyObjectBuilder_Beacon), false, new string[] { "GVK_NoThrusterZone" })]
    public class KOTHNoThrusters_Beacon : MyGameLogicComponent
    {
        private IMyBeacon beacon;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            beacon = (Entity as IMyBeacon);
            if (beacon != null)
            {
                KOTHNoThrusters_Thruster.beaconList.Add(beacon);
            }
        }

        public override void Close()
        {
            if (Entity == null)
            {
                return;
            }
                

            if (KOTHNoThrusters_Thruster.beaconList.Contains(beacon))
            {
                KOTHNoThrusters_Thruster.beaconList.Remove(beacon);
            }
        }

        public override void OnRemovedFromScene()
        {

            base.OnRemovedFromScene();

            var Block = Entity as IMyBeacon;

            if (Block == null)
            {
                return;
            }

        }
    }
}

