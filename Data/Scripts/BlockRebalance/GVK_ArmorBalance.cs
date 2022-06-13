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
	[MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class ArmorBalance : MySessionComponentBase
    {
        public const float lightArmorLargeDamageMod = 1f; //1.0 Vanilla
        public const float lightArmorLargeDeformationMod = 0.4f; //varies for every block
        public const float lightArmorSmallDamageMod = 1f; //1.0 Vanilla
        public const float lightArmorSmallDeformationMod = 0.4f; //varies for every block

        public const float heavyArmorLargeDamageMod = 1f; //0.5 Vanilla ONLY for full cube, 1.0 all else
        public const float heavyArmorLargeDeformationMod = 0.2f; //varies for every block
        public const float heavyArmorSmallDamageMod = 1f; //0.5 Vanilla ONLY for full cube, 1.0 all else
        public const float heavyArmorSmallDeformationMod = 0.2f; //varies for every block
	
		public const float blockExplosionResistanceMod = 1f; //DamageMultiplierExplosion

        public const float realWheelDamageMod = 0.5f; //1.0 Vanilla
        public const float realWheel5x5DamageMod = 0.5f; //1.0 Vanilla
        public const float suspensionDamageMod = 0.5f; //1.0 Vanilla
        public const float rotorDamageMod = 0.5f; //1.0 Vanilla
        public const float hingeDamageMod = 0.5f; //1.0 Vanilla
		public const float gyroDamageMod = 2; //1.0 Vanilla
		public const float thrusterDamageMod = 0.5f; //1.0 Vanilla
		public const float cockpitDamageMod = 0.5f; //1.0 Vanilla

		public const int drillPCU = 20000;
		public const int pistonBasePCU = 20000;
		public const float beaconMaxRadius = 150000;
		public const float hydroTankH2Density = 35555f;

		private readonly MyPhysicalItemDefinition genericScrap =
			MyDefinitionManager.Static.GetPhysicalItemDefinition(
				new MyDefinitionId(typeof(MyObjectBuilder_Ore), "Scrap"));

		private readonly MyComponentDefinition unobtainiumComponent = MyDefinitionManager.Static.GetComponentDefinition(
			new MyDefinitionId(typeof(MyObjectBuilder_Component), "GVK_Unobtanium"));

		private void DoWork()
        {
	        MyCubeBlockDefinition.Component unobtainiumBlockComponent = new MyCubeBlockDefinition.Component()
	        {
		        Count = 1,
		        Definition = unobtainiumComponent,
		        DeconstructItem = genericScrap
	        };
	        
	        foreach (MyDefinitionBase def in MyDefinitionManager.Static.GetAllDefinitions())
            {
	            MyCubeBlockDefinition blockDef = def as MyCubeBlockDefinition;
				MyLargeTurretBaseDefinition turretDef = def as MyLargeTurretBaseDefinition;
				MyWeaponBlockDefinition weaponDef = def as MyWeaponBlockDefinition;
				MyConveyorSorterDefinition sorterDef = def as MyConveyorSorterDefinition;
				MyShipDrillDefinition drillDef = def as MyShipDrillDefinition;
				MyPistonBaseDefinition pistonBaseDef = def as MyPistonBaseDefinition;
				MyBeaconDefinition beaconDef = def as MyBeaconDefinition;
				MyMotorSuspensionDefinition suspensionDef = def as MyMotorSuspensionDefinition;
				MyMotorStatorDefinition statorDef = def as MyMotorStatorDefinition; //Motor stator is the base
				MyMotorAdvancedStatorDefinition	advStatorDef = def as MyMotorAdvancedStatorDefinition; //Motor stator is the base
				MyThrustDefinition thrustDef = def as MyThrustDefinition;
				MyGyroDefinition gyroDef = def as MyGyroDefinition;
				MyCockpitDefinition cockpitDef = def as MyCockpitDefinition;
				MyRemoteControlDefinition remoteControlDef = def as MyRemoteControlDefinition;
				MyTimerBlockDefinition timerBlockDef = def as MyTimerBlockDefinition;
				MyGasTankDefinition hydroTankDef = def as MyGasTankDefinition;

                if (blockDef != null)
                {
					blockDef.DamageMultiplierExplosion = blockExplosionResistanceMod;	
				}					

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
                //suspension
				if (suspensionDef != null)
                {
                    suspensionDef.GeneralDamageMultiplier = suspensionDamageMod;
                }
                //rotors (includes hinges)
				if (statorDef != null)
                {
                    statorDef.GeneralDamageMultiplier = rotorDamageMod;
                }
                //adv rotors
				if (advStatorDef != null)
                {
                    advStatorDef.GeneralDamageMultiplier = rotorDamageMod;
                }
                //suspension wheels
				if (blockDef != null && blockDef.Id.SubtypeName.Contains("Real"))
                {
                    blockDef.GeneralDamageMultiplier = realWheelDamageMod;
					
					if (blockDef.Id.SubtypeName.Contains("5x5"))
					{
						blockDef.GeneralDamageMultiplier = realWheel5x5DamageMod;
					}
                }
                //rotor and hinge top parts
				if (blockDef != null && (blockDef.Id.SubtypeName.Contains("Rotor") || blockDef.Id.SubtypeName.Contains("HingeHead"))) 
                {
                    blockDef.GeneralDamageMultiplier = rotorDamageMod;
                }
                //drills
				if (drillDef != null)
                {
					drillDef.PCU = drillPCU;			
                }
                //pistons
				if (pistonBaseDef != null)
                {
					pistonBaseDef.PCU = pistonBasePCU;			
                }
                //actual conveyor sorters (non weapons)
				if (sorterDef != null && sorterDef.Id.SubtypeName.Contains("ConveyorSorter"))
                {
                    sorterDef.PCU = 0;
                }
                //Drillblocker
				if (beaconDef != null)
                {
					if (!beaconDef.Id.SubtypeName.Contains("DrillBlocker"))
					{
						beaconDef.MaxBroadcastRadius = beaconMaxRadius;
					}
					if (beaconDef.Id.SubtypeName.Contains("BlockBeacon"))
					{
						beaconDef.PCU = 1; //this is so TopGrid doesn't pick random numbers when parent grid has 0 PCU.
					}
                }
				//Thrusters
                if (thrustDef != null)
                {
                    thrustDef.GeneralDamageMultiplier = thrusterDamageMod;

                    if (!thrustDef.Id.SubtypeName.Contains("NPC"))
                    {
	                    if (thrustDef.FuelConverter != null &&
	                        !thrustDef.FuelConverter.FuelId.IsNull() &&
	                        thrustDef.FuelConverter.FuelId.SubtypeId.Contains("Hydrogen"))
	                    {
		                    thrustDef.MinPlanetaryInfluence = 0.5f;
		                    thrustDef.MaxPlanetaryInfluence = 1f;
		                    thrustDef.EffectivenessAtMaxInfluence = 1f;
		                    thrustDef.EffectivenessAtMinInfluence = 0.75f;
		                    //thrustDef.NeedsAtmosphereForInfluence = false; //partially useless because it always searches for atmosphere regardless
		                    //thrustDef.InvDiffMinMaxPlanetaryInfluence = 1f; 
		                    thrustDef.ConsumptionFactorPerG = -9.1f;
		                    thrustDef.SlowdownFactor = 1f;
		                    thrustDef.FuelConverter.Efficiency = 0.019f;
	                    }
	                    else
	                    {
		                    blockDef.Enabled = false;
		                    blockDef.Public = false;
		                    blockDef.GuiVisible = false;
		                    if (unobtainiumBlockComponent.Definition != null)
		                    {
			                    var thrusterComponents = new MyCubeBlockDefinition.Component[blockDef.Components.Length + 1];
			                    thrusterComponents[0] = unobtainiumBlockComponent;
			                    blockDef.Components.CopyTo(thrusterComponents, 1);
			                    blockDef.Components = thrusterComponents;
		                    }
	                    }
                    }
                }
                //gyros
				if (blockDef != null && blockDef.Id.SubtypeName.Contains("Gyro")) //using blockdef because gyro upgrades are not gyro type
                {
                    blockDef.GeneralDamageMultiplier = gyroDamageMod;
                }
                //cockpits (but not desks, or chairs)
				if (cockpitDef != null && cockpitDef.Id.SubtypeName.Contains("Cockpit")) 
                {
                    cockpitDef.GeneralDamageMultiplier = cockpitDamageMod;
                }
                //remote controls
				if (remoteControlDef != null) 
                {
                    remoteControlDef.GeneralDamageMultiplier = cockpitDamageMod;
                }
                //timer blocks 
				if (timerBlockDef != null) 
                {
	                timerBlockDef.GeneralDamageMultiplier = cockpitDamageMod;
                }

				//H2 tanks
				if (hydroTankDef != null && hydroTankDef.StoredGasId.SubtypeName == "Hydrogen")
				{
					hydroTankDef.Capacity = (float)(hydroTankDef.Size.Volume() * Math.Pow(hydroTankDef.CubeSize == MyCubeSize.Large ? 2.5 : 0.5, 3) * hydroTankH2Density);
				}
            }
        }

        public override void LoadData()
        {
	        DoWork();
        }
    }
}