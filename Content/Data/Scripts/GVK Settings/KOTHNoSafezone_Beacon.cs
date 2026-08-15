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

namespace KOTHNoSafezone
{
    [MyEntityComponentDescriptor(typeof(Sandbox.Common.ObjectBuilders.MyObjectBuilder_Beacon), false, new string[] { "ZoneBlock" })]
    public class KOTHNoSafezone_Beacon : MyGameLogicComponent
    {
        private MyObjectBuilder_EntityBase _objectBuilder;
        private IMyBeacon beacon;
        //private IMyPlayer client;
        private bool playerInZone;
        private IMyCharacter character;
        private VRage.Game.ModAPI.Interfaces.IMyControllableEntity controller;

        private bool logicEnabled = false;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            beacon = (Entity as IMyBeacon);
            KOTHNoSafezone_SafeZoneBlock.beaconList.Add(beacon);
			if (beacon != null)
            {
                logicEnabled = true;
                NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;
                NeedsUpdate |= MyEntityUpdateEnum.EACH_10TH_FRAME;
            }

            //client = MyAPIGateway.Session.LocalHumanPlayer;
        }

        /*public override void UpdateBeforeSimulation10()
        {
            base.UpdateBeforeSimulation10();

            MyAPIGateway.Parallel.Start(delegate {

                try
                {
                    if (!logicEnabled || beacon == null || !beacon.IsWorking || client == null)
                    {
                        return;
                    }                       

                    if (Vector3D.Distance(client.GetPosition(), beacon.GetPosition()) < beacon.Radius)
                    {
                        playerInZone = true;
                        character = client.Character;
                        controller = character as VRage.Game.ModAPI.Interfaces.IMyControllableEntity;
                    }
                    else
                    {
                        playerInZone = false;
                    }
                }
                catch (Exception e)
                {
                    MyAPIGateway.Utilities.ShowMessage("KOTHNoSafezone", "An error happened in the mod" + e);
                }
            });
        }*/

        public override void Close()
        {
            if (Entity == null)
            {
                return;
            }
                

            if (KOTHNoSafezone_SafeZoneBlock.beaconList.Contains(beacon))
            {
                KOTHNoSafezone_SafeZoneBlock.beaconList.Remove(beacon);
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

