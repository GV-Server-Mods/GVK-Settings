using System;
using System.Collections.Generic;
using VRage.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using Sandbox.ModAPI;
using Sandbox.Common.ObjectBuilders;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace KOTHNoSafezone
{
    [MyEntityComponentDescriptor(typeof(Sandbox.Common.ObjectBuilders.MyObjectBuilder_Beacon), false, new string[] { "ZoneBlock" })]
    public class KOTHNoSafezone_Beacon : MyGameLogicComponent
    {
        private IMyBeacon beacon;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            beacon = (Entity as IMyBeacon);
            KOTHNoSafezone_Manager.AddBeacon(beacon);
        }

        public override void Close()
        {
            if (Entity == null)
            {
                return;
            }

            // Remove from centralized manager
            KOTHNoSafezone_Manager.RemoveBeacon(beacon);
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();

            if (Entity == null || Entity.MarkedForClose)
            {
                return;
            }

            KOTHNoSafezone_Manager.RemoveBeacon(beacon);
        }
    }
}


