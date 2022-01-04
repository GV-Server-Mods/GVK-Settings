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

        public const float realWheelDamageMod = 0.5f; //1.0 Vanilla
        public const float realWheel5x5DamageMod = 0.5f; //1.0 Vanilla
        public const float suspensionDamageMod = 0.5f; //1.0 Vanilla
        public const float rotorDamageMod = 0.5f; //1.0 Vanilla
        public const float hingeDamageMod = 0.5f; //1.0 Vanilla
		public const float gyroDamageMod = 2; //1.0 Vanilla

		public const int drillPCU = 2000;
		public const int pistonBasePCU = 2000;
		public const float beaconMaxRadius = 150000;
		
        private bool isInit = false;

        private void DoWork()
        {

            foreach (MyDefinitionBase def in MyDefinitionManager.Static.GetAllDefinitions())
            {


                MyCubeBlockDefinition blockDef = def as MyCubeBlockDefinition;
				MyLargeTurretBaseDefinition turretDef = def as MyLargeTurretBaseDefinition;
				MyWeaponBlockDefinition weaponDef = def as MyWeaponBlockDefinition;
				MyConveyorSorterDefinition sorterDef = def as MyConveyorSorterDefinition;
				MyShipDrillDefinition drillDef = def as MyShipDrillDefinition;
				MyPistonBaseDefinition pistonBaseDef = def as MyPistonBaseDefinition;
				MyBeaconDefinition beaconDef = def as MyBeaconDefinition;
				//MyWheelModelsDefinition realWheelDef = def as MyWheelModelsDefinition;
				MyMotorSuspensionDefinition suspensionDef = def as MyMotorSuspensionDefinition;
				//MyMotorRotorDefinition rotorDef = def as MyMotorRotorDefinition;
				MyMotorStatorDefinition statorDef = def as MyMotorStatorDefinition; //Motor stator is the base
				//MyMotorAdvancedRotorDefinition advRotorDef = def as MyMotorAdvancedRotorDefinition;
				MyMotorAdvancedStatorDefinition	advStatorDef = def as MyMotorAdvancedStatorDefinition; //Motor stator is the base
				MyThrustDefinition thrustDef = def as MyThrustDefinition;//thruster
				MyGyroDefinition gyroDef = def as MyGyroDefinition;//thruster


                if (blockDef != null && turretDef == null && weaponDef == null && sorterDef == null)
                {
					blockDef.PCU = (int) 0;	
				}					

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
					//blockDef.PCU = lightArmorPCU;
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
					//blockDef.PCU = blastDoorPCU;
                }
                if (suspensionDef != null)
                {
                    suspensionDef.GeneralDamageMultiplier = suspensionDamageMod;
                }
                if (statorDef != null)
                {
                    statorDef.GeneralDamageMultiplier = rotorDamageMod;
                }
                if (advStatorDef != null)
                {
                    advStatorDef.GeneralDamageMultiplier = rotorDamageMod;
                }
                if (blockDef != null && blockDef.Id.SubtypeName.Contains("Real"))
                {
                    blockDef.GeneralDamageMultiplier = realWheelDamageMod;
					
					if (blockDef.Id.SubtypeName.Contains("5x5"))
					{
						blockDef.GeneralDamageMultiplier = realWheel5x5DamageMod;
					}
                }
                if (blockDef != null && (blockDef.Id.SubtypeName.Contains("Rotor") || blockDef.Id.SubtypeName.Contains("HingeHead"))) 
                {
                    blockDef.GeneralDamageMultiplier = rotorDamageMod;
                }
                if (drillDef != null)
                {
					drillDef.PCU = drillPCU;			
                }
                if (pistonBaseDef != null)
                {
					pistonBaseDef.PCU = pistonBasePCU;			
                }
                if (sorterDef != null && sorterDef.Id.SubtypeName.Contains("ConveyorSorter"))
                {
                    sorterDef.PCU = 0;
                }
                if (beaconDef != null)
                {
					if (!beaconDef.Id.SubtypeName.Contains("DrillBlocker"))
					{
						beaconDef.MaxBroadcastRadius = beaconMaxRadius;
					}
                }
				//Thrusters
                if (thrustDef != null && thrustDef.Id.SubtypeName.Contains("Hydrogen") && !thrustDef.Id.SubtypeName.Contains("NPC"))
                {
					thrustDef.MinPlanetaryInfluence = 0.5f;
					thrustDef.MaxPlanetaryInfluence = 1f;
					thrustDef.EffectivenessAtMaxInfluence = 1f;
					thrustDef.EffectivenessAtMinInfluence = 0.5f;
					//thrustDef.NeedsAtmosphereForInfluence = false; //partially useless because it always searches for atmosphere regardless
					//thrustDef.InvDiffMinMaxPlanetaryInfluence = 1f; 
					thrustDef.ConsumptionFactorPerG = -9.1f;
					thrustDef.SlowdownFactor = 1f;
					thrustDef.FuelConverter.Efficiency = 0.019f;
                }
                if (blockDef != null && (blockDef.Id.SubtypeName.Contains("Gyro"))) //&& (gyroDef.Id.SubtypeName.Equals("LargeBlockGyro") || gyroDef.Id.SubtypeName.Equals("SmallBlockGyro"))
                {
                    blockDef.GeneralDamageMultiplier = gyroDamageMod;
                }
            }
        }
        
        /*public override bool UpdatedBeforeInit()
        {
            DoWork();
            return true;
        }*/

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
