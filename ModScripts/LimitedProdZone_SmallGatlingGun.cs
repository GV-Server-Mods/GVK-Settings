using SpaceEngineers.Game.ModAPI;
using System;
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
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_SmallGatlingGun), false)]
    public class LimitedProdZone_SmallGatlingGun : MyGameLogicComponent
    {
        private IMySmallGatlingGun weapon;
        private bool isServer;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            weapon = (Entity as IMySmallGatlingGun);
            if (weapon != null)
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
                weapon.IsWorkingChanged += WorkingStateChange;
            }
        }

        public override void UpdateBeforeSimulation100()
        {
            base.UpdateBeforeSimulation100();

            try
            {
                if (isServer)
                {
                    if (!weapon.Enabled) return;

                    if (LimitedProdZone_Manager.IsPositionInWeaponZone(weapon.GetPosition()))
                    {
                        weapon.Enabled = false;
                        return;
                    }
                }
            }
            catch (Exception exc)
            {
                MyLog.Default.WriteLineAndConsole($"Failed checking LimitedProdZone small gatling gun position: {exc}");
            }
        }

        private void WorkingStateChange(IMyCubeBlock block)
        {
            if (weapon.Enabled)
            {
                if (LimitedProdZone_Manager.IsPositionInWeaponZone(weapon.GetPosition()))
                {
                    weapon.Enabled = false;
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

            var Block = Entity as IMySmallGatlingGun;

            if (Block == null) return;

            try
            {
                if (isServer)
                {
                    weapon.IsWorkingChanged -= WorkingStateChange;
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

