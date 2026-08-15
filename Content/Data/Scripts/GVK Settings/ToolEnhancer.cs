using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

// This increases ship tool inventory size a little to reduce clogging

namespace GVTweaks.ToolEnhancer
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_ShipGrinder), false)]
    public class ToolEnhancer : MyGameLogicComponent
    {
        const float cubeDimensionsMultiplier = 0.6f; // Keen default is 0.5f;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override void UpdateOnceBeforeFrame()
        {
            var tool = Entity as IMyShipToolBase;
            if (tool == null || tool.MarkedForClose || tool.Closed)
            {
                return;
            }
            var cubeBlockDefinition = MyDefinitionManager.Static.GetCubeBlockDefinition(tool.BlockDefinition);
            if (cubeBlockDefinition == null)
            {
                return;
            }
            var cubeGrid = tool.CubeGrid;
            if (cubeGrid == null || cubeGrid.MarkedForClose || cubeGrid.Closed)
            {
                return;
            }

            MyInventory myInventory = Entity.GetInventory(0) as MyInventory;
            if (myInventory == null)
            {
                return;
            }
            float maxVolume = (float)cubeBlockDefinition.Size.X * cubeGrid.GridSize * (float)cubeBlockDefinition.Size.Y * cubeGrid.GridSize * (float)cubeBlockDefinition.Size.Z * cubeGrid.GridSize * cubeDimensionsMultiplier;

            if ((float)myInventory.MaxVolume < maxVolume) // Never shrink an inventory, or do anything if it's already the right size
            {
                myInventory.ResetVolume();
                myInventory.FixInventoryVolume(maxVolume);
                myInventory.Refresh();
            }
        }
    }
}
