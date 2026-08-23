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
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Drill), false)]
    public class LimitedProdZone_StaticDrill : MyGameLogicComponent
    {
        private IMyShipDrill staticDrill;
        private bool isServer;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            staticDrill = (Entity as IMyShipDrill);
            if (staticDrill != null)
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
                staticDrill.IsWorkingChanged += WorkingStateChange;
            }
        }

        public override void UpdateBeforeSimulation100()
        {
            base.UpdateBeforeSimulation100();

            try
            {
                if (isServer)
                {
                    if (!staticDrill.Enabled) return;

                    string strSubBlockType = staticDrill.BlockDefinition.SubtypeId.ToString();
                    bool isBasicStaticDrill = strSubBlockType.Contains("BasicStaticDrill");
                    if (isBasicStaticDrill) return;

                    if (LimitedProdZone_Manager.IsPositionInWeaponZone(staticDrill.GetPosition()))
                    {
                        staticDrill.Enabled = false;
                        return;
                    }
                }
            }
            catch (Exception exc)
            {
                MyLog.Default.WriteLineAndConsole($"Failed checking LimitedProdZone ship drill position: {exc}");
            }
        }

        private void WorkingStateChange(IMyCubeBlock block)
        {
            if (staticDrill.Enabled)
            {
                string strSubBlockType = staticDrill.BlockDefinition.SubtypeId.ToString();
                bool isBasicStaticDrill = strSubBlockType.Contains("BasicStaticDrill");
                if (isBasicStaticDrill) return;

                if (LimitedProdZone_Manager.IsPositionInWeaponZone(staticDrill.GetPosition()))
                {
                    staticDrill.Enabled = false;
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

            var Block = Entity as IMyShipDrill;

            if (Block == null) return;

            try
            {
                if (isServer)
                {
                    staticDrill.IsWorkingChanged -= WorkingStateChange;
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

