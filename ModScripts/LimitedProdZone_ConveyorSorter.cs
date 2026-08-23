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
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_ConveyorSorter), false)]
    public class LimitedProdZone_ConveyorSorter : MyGameLogicComponent
    {
        private IMyConveyorSorter weapon;
        private bool isServer;
        private static readonly MyDefinitionId StaticWeaponDef = new MyDefinitionId(typeof(MyObjectBuilder_ConveyorSorter), "ARYXMissileBattery");
        private static readonly List<MyDefinitionId> ConveyorSorterDefs = new List<MyDefinitionId> 
        {
            new MyDefinitionId(typeof(MyObjectBuilder_ConveyorSorter), "LargeBlockConveyorSorter"),
            new MyDefinitionId(typeof(MyObjectBuilder_ConveyorSorter), "MediumBlockConveyorSorter"),
            new MyDefinitionId(typeof(MyObjectBuilder_ConveyorSorter), "SmallBlockConveyorSorter"),
            new MyDefinitionId(typeof(MyObjectBuilder_ConveyorSorter), "LargeBlockConveyorSorterIndustrial") 	
        };
        private static readonly MyStringHash DestructionHash = MyStringHash.GetOrCompute("Destruction");

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            weapon = (Entity as IMyConveyorSorter);
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
                    if (weapon == null) return;
                    if ((weapon.BlockDefinition == StaticWeaponDef) && (weapon.CubeGrid != null) && !weapon.CubeGrid.IsStatic)
                    {
                        weapon.SlimBlock.DoDamage(99999999999999f, DestructionHash, true, null, 0, 0, false, null);
                        return;
                    }

                    if (!weapon.Enabled) return;

                    if (LimitedProdZone_Manager.IsPositionInWeaponZone(weapon.GetPosition()))
                    {
                        if (!ConveyorSorterDefs.Contains(weapon.BlockDefinition))
                        {
                            weapon.Enabled = false;
                            return;
                        }
                    }
                }
            }
            catch (Exception exc)
            {
                MyLog.Default.WriteLineAndConsole($"Failed checking LimitedProdZone conveyor sorter position: {exc}");
            }
        }

        private void WorkingStateChange(IMyCubeBlock block)
        {
            if (weapon.Enabled)
            {
                if (LimitedProdZone_Manager.IsPositionInWeaponZone(weapon.GetPosition()))
                {
                    if (!ConveyorSorterDefs.Contains(weapon.BlockDefinition))
                    {
                        weapon.Enabled = false;
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

            var Block = Entity as IMyConveyorSorter;

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

