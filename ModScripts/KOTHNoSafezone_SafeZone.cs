using System;
using VRage.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using Sandbox.ModAPI;
using Sandbox.Common.ObjectBuilders;
using SpaceEngineers.Game.ModAPI;
using ObjectBuilders.SafeZone;
using VRage.ObjectBuilders;
using VRage.Utils;


namespace KOTHNoSafezone
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_SafeZoneBlock), false)]
    public class KOTHNoSafezone_SafeZoneBlock : MyGameLogicComponent
    {
        private IMySafeZoneBlock safezoneblock;
        private bool isServer;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            safezoneblock = (Entity as IMySafeZoneBlock);
            if (safezoneblock != null)
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
                safezoneblock.IsWorkingChanged += WorkingStateChange;
            }
        }

        public override void UpdateBeforeSimulation100()
        {
            base.UpdateBeforeSimulation100();

            try
            {
                if (isServer)
                {
                    if (!safezoneblock.Enabled) return;

                    if (KOTHNoSafezone_Manager.IsPositionInZone(safezoneblock.GetPosition()))
                    {
                        safezoneblock.Enabled = false;
                        return;
                    }
                }
            }
            catch (Exception exc)
            {
                MyLog.Default.WriteLineAndConsole($"Failed checking KOTH safezone position: {exc}");
            }
        }

        private void WorkingStateChange(IMyCubeBlock block)
        {
            if (safezoneblock.Enabled)
            {
                if (KOTHNoSafezone_Manager.IsPositionInZone(safezoneblock.GetPosition()))
                {
                    safezoneblock.Enabled = false;
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

            var Block = Entity as IMySafeZoneBlock;

            if (Block == null) return;

            try
            {
                if (isServer)
                {
                    safezoneblock.IsWorkingChanged -= WorkingStateChange;
                }
            }
            catch (Exception exc)
            {
                MyLog.Default.WriteLineAndConsole($"Failed to deregister event: {exc}");
                return;
            }
        }
    }
}

