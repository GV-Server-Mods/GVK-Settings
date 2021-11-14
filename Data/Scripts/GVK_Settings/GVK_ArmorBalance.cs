using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game.Components;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Sandbox.Game.AI.Pathfinding.Obsolete;
using Sandbox.Game.Entities;
using VRage.Game;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Utils;
using VRageMath;


// Code is based on Gauge's Balanced Deformation code, but heavily modified for more control. 
namespace MikeDude.ArmorBalance
{
	[MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class ArmorBalance : MySessionComponentBase
    {
        public const float lightArmorLargeDamageMod = 1f; //1.0 Vanilla
        public const float lightArmorLargeDeformationMod = 0.5f; //varies for every block
        public const float lightArmorSmallDamageMod = 1f; //1.0 Vanilla
        public const float lightArmorSmallDeformationMod = 0.5f; //varies for every block

        public const float heavyArmorLargeDamageMod = 1f; //0.5 Vanilla ONLY for full cube, 1.0 all else
        public const float heavyArmorLargeDeformationMod = 0.3f; //varies for every block
        public const float heavyArmorSmallDamageMod = 1f; //0.5 Vanilla ONLY for full cube, 1.0 all else
        public const float heavyArmorSmallDeformationMod = 0.3f; //varies for every block
		
        private bool isInit = false;

        private void DoWork()
        {

            foreach (MyDefinitionBase def in MyDefinitionManager.Static.GetAllDefinitions())
            {


                MyCubeBlockDefinition blockDef = def as MyCubeBlockDefinition;

				//light armor
                if (blockDef != null && (blockDef.EdgeType == "Light" && (blockDef.BlockTopology != MyBlockTopology.TriangleMesh)))
                {
                    if (blockDef.CubeSize == MyCubeSize.Large)
                    {
                        blockDef.GeneralDamageMultiplier = lightArmorLargeDamageMod;
                        blockDef.DeformationRatio = lightArmorLargeDeformationMod;
                    }

                    if (blockDef.CubeSize == MyCubeSize.Small)
                    {
                        blockDef.GeneralDamageMultiplier = lightArmorSmallDamageMod;
                        blockDef.DeformationRatio = lightArmorSmallDeformationMod;
                    }
                }
				//heavy armor
                if (blockDef != null && (blockDef.EdgeType == "Heavy" && (blockDef.BlockTopology != MyBlockTopology.TriangleMesh)))
                {
                    if (blockDef.CubeSize == MyCubeSize.Large)
                    {
                        blockDef.GeneralDamageMultiplier = heavyArmorLargeDamageMod;
                        blockDef.DeformationRatio = heavyArmorLargeDeformationMod;
                    }

                    if (blockDef.CubeSize == MyCubeSize.Small)
                    {
                        blockDef.GeneralDamageMultiplier = heavyArmorSmallDamageMod;
                        blockDef.DeformationRatio = heavyArmorSmallDeformationMod;
                    }
                }
            }
        }
        
        public override bool UpdatedBeforeInit()
        {
            DoWork();
            return true;
        }

        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            DoWork();
        }

        public override void UpdateBeforeSimulation()
        {
            if (!isInit && MyAPIGateway.Session == null)
            {
                DoWork();
                isInit = true;
            }
        }
    }
}
