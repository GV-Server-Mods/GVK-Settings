using Sandbox.Definitions;
using System.Linq;
using VRage.Game.Components;

// =========================================================================
// GV: Deserts of Kharak (GVK) Server Settings & Mechanics
// Script: NoMinPrice.cs
// Based on: "No Limits for '' PRICE PER UNIT ''" (Workshop #1907404695)
// Integration for GVK: Mike Dude
// Description: Sets the minimum price per unit on all physical item definitions
// to 1 Space Credit, allowing custom player-owned trade stations and free-market pricing.
// Note: Listing and transaction fees are defined in SessionComponents_Economy.sbc.
// =========================================================================

namespace GVK.Economy
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    class NoMinPrice : MySessionComponentBase
    {
        public override void LoadData()
        {
            base.LoadData();
            var allDefs = MyDefinitionManager.Static.GetAllDefinitions();

            foreach (var component in allDefs.OfType<MyPhysicalItemDefinition>())
            {
                component.MinimalPricePerUnit = 1;
            }
        }
    }
}
