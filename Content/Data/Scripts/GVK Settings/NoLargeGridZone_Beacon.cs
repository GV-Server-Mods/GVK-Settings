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

namespace NoLargeGridZone
{
    [MyEntityComponentDescriptor(typeof(Sandbox.Common.ObjectBuilders.MyObjectBuilder_Beacon), false, new string[] { "GVK_NoLargeGridZone" })]
    public class NoLargeGridZone_Beacon : MyGameLogicComponent
    {
        private MyObjectBuilder_EntityBase _objectBuilder;
        private IMyBeacon beacon;
        private VRage.Game.ModAPI.Interfaces.IMyControllableEntity controller;

        private TextWriter logger = null;
        private String timeofload = "" + DateTime.Now.Year + "." + DateTime.Now.Month + "." + DateTime.Now.Day + " " + DateTime.Now.Hour + "." + DateTime.Now.Minute + "." + DateTime.Now.Second;
        private bool logicEnabled = false;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            beacon = (Entity as IMyBeacon);
            NoLargeGridZone_Reactor.beaconList.Add(beacon);
            NoLargeGridZone_Battery.beaconList.Add(beacon);
            NoLargeGridZone_Fueled.beaconList.Add(beacon);
            NoLargeGridZone_Solar.beaconList.Add(beacon);
            if (beacon != null)
            {
                logicEnabled = true;
                NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;
                NeedsUpdate |= MyEntityUpdateEnum.EACH_10TH_FRAME;
            }
        }

        public override void Close()
        {
            if (Entity == null)
            {
                return;
            }
                
            if (NoLargeGridZone_Reactor.beaconList.Contains(beacon))
            {
                NoLargeGridZone_Reactor.beaconList.Remove(beacon);
            }

            if (NoLargeGridZone_Battery.beaconList.Contains(beacon))
            {
                NoLargeGridZone_Battery.beaconList.Remove(beacon);
            }

            if (NoLargeGridZone_Fueled.beaconList.Contains(beacon))
            {
                NoLargeGridZone_Fueled.beaconList.Remove(beacon);
            }

            if (NoLargeGridZone_Solar.beaconList.Contains(beacon))
            {
                NoLargeGridZone_Solar.beaconList.Remove(beacon);
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

