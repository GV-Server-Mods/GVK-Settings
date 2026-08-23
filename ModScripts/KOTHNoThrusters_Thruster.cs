using System;
using VRage.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using Sandbox.ModAPI;
using Sandbox.Common.ObjectBuilders;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace KOTHNoThrusters
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Thrust), false)]
    public class KOTHNoThrusters_Thruster : MyGameLogicComponent
    {
        private IMyThrust thrusterblock;
        private bool isServer;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            thrusterblock = (Entity as IMyThrust);
            if (thrusterblock != null)
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
                thrusterblock.IsWorkingChanged += WorkingStateChange;
            }
        }

        public override void UpdateBeforeSimulation100()
        {
            base.UpdateBeforeSimulation100();

            try
            {
                if (isServer)
                {
                    if (!thrusterblock.Enabled) return;

                    if (thrusterblock.BlockDefinition.SubtypeId.Contains("NPC")) return; // skip if NPC thruster subtype

                    if (KOTHNoThrusters_Manager.IsPositionInZone(thrusterblock.GetPosition()))
                    {
                        thrusterblock.Enabled = false;
                        return;
                    }
                }
            }
            catch (Exception exc)
            {
                MyLog.Default.WriteLineAndConsole($"Failed checking KOTH thruster position: {exc}");
            }
        }

        private void WorkingStateChange(IMyCubeBlock block)
        {
            if (thrusterblock.Enabled)
            {
                if (thrusterblock.BlockDefinition.SubtypeId.Contains("NPC")) return; // skip if NPC thruster subtype

                if (KOTHNoThrusters_Manager.IsPositionInZone(thrusterblock.GetPosition()))
                {
                    thrusterblock.Enabled = false;
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

            var Block = Entity as IMyThrust;

            if (Block == null) return;

            try
            {
                if (isServer)
                {
                    thrusterblock.IsWorkingChanged -= WorkingStateChange;
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

