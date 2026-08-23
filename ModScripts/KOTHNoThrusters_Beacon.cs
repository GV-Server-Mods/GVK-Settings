using System;
using VRage.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using Sandbox.ModAPI;
using Sandbox.Common.ObjectBuilders;
using VRage.ObjectBuilders;

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
            KOTHNoThrusters_Manager.AddBeacon(beacon);
        }

        public override void Close()
        {
            if (Entity == null)
            {
                return;
            }

            // Remove from centralized manager
            KOTHNoThrusters_Manager.RemoveBeacon(beacon);
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();

            if (Entity == null || Entity.MarkedForClose)
            {
                return;
            }

            KOTHNoThrusters_Manager.RemoveBeacon(beacon);
        }
    }
}
