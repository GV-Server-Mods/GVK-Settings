using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.ModAPI;
using VRage.ObjectBuilders;

// =========================================================================
// GV: Deserts of Kharak (GVK) Server Settings & Mechanics
// Script: ToolEnhancer.cs
// Original Author: Merii (GV Tweaks)
// Description: Dynamically expands internal inventory volumes of vanilla ship tools
// to prevent inventory bottlenecking and clogging during salvaging and excavation.
// =========================================================================

namespace GVK.Tools
{
    /// <summary>
    /// Base game logic component that dynamically expands the internal inventory volume of vanilla ship tools
    /// (welders, grinders, drills) on legacy NPC grids and existing blueprints.
    /// Addresses a vanilla Keen quirk where ship tool inventory volumes are exactly the same size as small cargos
    /// causing them to clog before all contents can be pulled, then causing dropped floating objects.
    /// </summary>
    public class ToolEnhancer : MyGameLogicComponent
    {
        private const float CubeDimensionsMultiplier = 0.6f; // Keen default is 0.5f;

        /// <summary>
        /// Initializes the game logic component and schedules a single update before the next frame.
        /// </summary>
        /// <param name="objectBuilder">The serialized entity object builder.</param>
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        /// <summary>
        /// Evaluates the tool entity, calculates the target inventory volume accounting for block size,
        /// grid size, and world inventory multiplier settings, and adjusts the inventory volume if needed.
        /// </summary>
        public override void UpdateOnceBeforeFrame()
        {
            base.UpdateOnceBeforeFrame();

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

            var myInventory = tool.GetInventory() as MyInventory;
            if (myInventory == null)
            {
                return;
            }

            float baseMaxVolume = (float)cubeBlockDefinition.Size.X * cubeGrid.GridSize *
                                  (float)cubeBlockDefinition.Size.Y * cubeGrid.GridSize *
                                  (float)cubeBlockDefinition.Size.Z * cubeGrid.GridSize *
                                  CubeDimensionsMultiplier;

            float inventoryMultiplier = MyAPIGateway.Session?.BlocksInventorySizeMultiplier ?? 1f;
            float targetMultipliedVolume = baseMaxVolume * inventoryMultiplier;

            // Never shrink an inventory, or do anything if it's already the right size
            if ((float)myInventory.MaxVolume < targetMultipliedVolume - 0.001f)
            {
                myInventory.ResetVolume();
                myInventory.FixInventoryVolume(baseMaxVolume);
                myInventory.Refresh();
            }
        }
    }

    /// <summary>
    /// Game logic component descriptor attaching ToolEnhancer to vanilla Ship Grinders.
    /// </summary>
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_ShipGrinder), false)]
    public class ShipGrinderEnhancer : ToolEnhancer { }

    /// <summary>
    /// Game logic component descriptor attaching ToolEnhancer to vanilla Ship Welders.
    /// </summary>
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_ShipWelder), false)]
    public class ShipWelderEnhancer : ToolEnhancer { }

    /// <summary>
    /// Game logic component descriptor attaching ToolEnhancer to vanilla Ship Drills.
    /// </summary>
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Drill), false)]
    public class ShipDrillEnhancer : ToolEnhancer { }
}

