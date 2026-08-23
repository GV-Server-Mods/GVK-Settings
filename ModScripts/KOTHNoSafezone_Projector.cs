using System;
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
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Projector), false)]
    public class KOTHNoSafezone_ProjectorBlock : MyGameLogicComponent
    {
        private IMyProjector projectorblock;
        private bool isServer;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            projectorblock = (Entity as IMyProjector);
            if (projectorblock != null)
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
                projectorblock.IsWorkingChanged += WorkingStateChange;
            }
        }

        public override void UpdateBeforeSimulation100()
        {
            base.UpdateBeforeSimulation100();

            try
            {
                if (isServer)
                {
                    if (!projectorblock.Enabled) return;

                    string strSubBlockType = projectorblock.BlockDefinition.SubtypeId.ToString();
                    if (strSubBlockType.Contains("MnM") && projectorblock.CubeGrid.IsStatic)
                    {
                        if (KOTHNoSafezone_Manager.IsPositionInZone(projectorblock.GetPosition()))
                        {
                            projectorblock.Enabled = false;
                            return;
                        }
                    }
                }
            }
            catch (Exception exc)
            {
                MyLog.Default.WriteLineAndConsole($"Failed checking KOTH projector position: {exc}");
            }
        }

        private void WorkingStateChange(IMyCubeBlock block)
        {
            if (projectorblock.Enabled)
            {
                string strSubBlockType = projectorblock.BlockDefinition.SubtypeId.ToString();
                if (strSubBlockType.Contains("MnM") && projectorblock.CubeGrid.IsStatic)
                {
                    if (KOTHNoSafezone_Manager.IsPositionInZone(projectorblock.GetPosition()))
                    {
                        projectorblock.Enabled = false;
                    }
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

            var Block = Entity as IMyProjector;

            if (Block == null) return;

            try
            {
                if (isServer)
                {
                    projectorblock.IsWorkingChanged -= WorkingStateChange;
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

