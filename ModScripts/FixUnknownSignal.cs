using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;

// =========================================================================
// GV: Deserts of Kharak (GVK) Server Settings & Mechanics
// Script: FixUnknownSignal.cs
// Original Author: Merii (highlyunavailable)
// Description: Fixes vanilla Keen quirk where Unknown Signal drop pods spawn
// assigned to real player entity IDs rather than an NPC faction.
// =========================================================================

namespace GVK.UnknownSignal
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class FixUnknownSignal : MySessionComponentBase
    {
        private bool init;
        private IMyFaction npc;

        public override void BeforeStart()
        {
            if (!MyAPIGateway.Multiplayer.IsServer)
            {
                return;
            }

            if (npc != null)
            {
                return;
            }

            npc = MyAPIGateway.Session.Factions.TryGetFactionByTag("SPRT");

            if (init)
            {
                return;
            }

            MyAPIGateway.Entities.OnEntityAdd += EntityTrigger;
            init = true;
        }

        protected override void UnloadData()
        {
            MyAPIGateway.Entities.OnEntityAdd -= EntityTrigger;
        }

        private void EntityTrigger(IMyEntity entity)
        {
            if (entity?.Physics == null)
            {
                return;
            }

            var cubeGrid = entity as IMyCubeGrid;
            if (cubeGrid == null)
            {
                return;
            }

            if (!cubeGrid.BigOwners.Contains(144115188075855895) || !cubeGrid.CustomName.Contains("Container MK"))
            {
                return;
            }

            var grid = entity as MyCubeGrid;
            grid?.ChangeGridOwnership(npc?.FounderId ?? 0, MyOwnershipShareModeEnum.All);
        }
    }
}