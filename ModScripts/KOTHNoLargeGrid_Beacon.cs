using System;
using VRage.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using Sandbox.ModAPI;
using Sandbox.Common.ObjectBuilders;
using VRage.ObjectBuilders;

namespace KOTHNoLargeGrid
{
    [MyEntityComponentDescriptor(typeof(Sandbox.Common.ObjectBuilders.MyObjectBuilder_Beacon), false, new string[] { "GVK_NoLargeGridZone" })]
    public class KOTHNoLargeGrid_Beacon : MyGameLogicComponent
    {
        private IMyBeacon beacon;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            beacon = (Entity as IMyBeacon);
            KOTHNoLargeGrid_Manager.AddBeacon(beacon);
        }

        public override void Close()
        {
            if (Entity == null)
            {
                return;
            }

            // Remove from centralized manager
            KOTHNoLargeGrid_Manager.RemoveBeacon(beacon);
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();

            if (Entity == null || Entity.MarkedForClose)
            {
                return;
            }

            KOTHNoLargeGrid_Manager.RemoveBeacon(beacon);
        }
    }
}
