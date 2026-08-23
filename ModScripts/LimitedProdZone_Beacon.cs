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

namespace LimitedProdZone
{
    [MyEntityComponentDescriptor(typeof(Sandbox.Common.ObjectBuilders.MyObjectBuilder_Beacon), false, new string[] { "LimitedProdZone" })]
    public class LimitedProdZone_Beacon : MyGameLogicComponent
    {
        private IMyBeacon beacon;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            beacon = (Entity as IMyBeacon);
            LimitedProdZone_Manager.AddBeacon(beacon);
        }

        public override void Close()
        {
            if (Entity == null)
            {
                return;
            }

            // Remove from centralized manager
            LimitedProdZone_Manager.RemoveBeacon(beacon);
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();

            if (Entity == null || Entity.MarkedForClose)
            {
                return;
            }

            LimitedProdZone_Manager.RemoveBeacon(beacon);
        }
    }
}


