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
        public const float lightArmorLargeDamageMod = 0.75f; //1.0 Vanilla
        public const float lightArmorLargeDeformationMod = 0.4f; //varies for every block
        public const float lightArmorSmallDamageMod = 0.75f; //1.0 Vanilla
        public const float lightArmorSmallDeformationMod = 0.4f; //varies for every block

        public const float heavyArmorLargeDamageMod = 1f; //0.5 Vanilla ONLY for full cube, 1.0 all else
        public const float heavyArmorLargeDeformationMod = 0.2f; //varies for every block
        public const float heavyArmorSmallDamageMod = 1f; //0.5 Vanilla ONLY for full cube, 1.0 all else
        public const float heavyArmorSmallDeformationMod = 0.2f; //varies for every block

        public const float blockExplosionResistanceMod = 1f; //DamageMultiplierExplosion

        public const float realWheelDamageMod = 0.75f; //1.0 Vanilla
        public const float realWheel5x5DamageMod = 0.75f; //1.0 Vanilla
        public const float suspensionDamageMod = 0.75f; //1.0 Vanilla
        public const float rotorDamageMod = 0.5f; //1.0 Vanilla
        public const float hingeDamageMod = 0.5f; //1.0 Vanilla
        public const float gyroDamageMod = 2; //1.0 Vanilla
        public const float thrusterDamageMod = 0.5f; //1.0 Vanilla
        public const float cockpitDamageMod = 0.5f; //1.0 Vanilla

        public const int drillPCU = 20000;
        public const int welderPCU = 10000;
        public const int pistonBasePCU = 20000;
        public const float beaconMaxRadius = 100000;
        public const double hydroTankH2Density = 15000000 / (2.5 * 2.5 * 2.5 * 27); // LG Large hydro tank capacity divided by its volume in meters

        private readonly MyPhysicalItemDefinition genericScrap = MyDefinitionManager.Static.GetPhysicalItemDefinition(new MyDefinitionId(typeof(MyObjectBuilder_Ore), "Scrap"));

        private readonly MyComponentDefinition unobtainiumComponent = MyDefinitionManager.Static.GetComponentDefinition(new MyDefinitionId(typeof(MyObjectBuilder_Component), "GVK_Unobtanium"));

        private void DoWork()
        {
            var unobtainiumBlockComponent = new MyCubeBlockDefinition.Component()
            {
                Count = 1,
                Definition = unobtainiumComponent,
                DeconstructItem = genericScrap
            };

            foreach (var blockDef in MyDefinitionManager.Static.GetDefinitionsOfType<MyCubeBlockDefinition>())
            {
                var turretDef = blockDef as MyLargeTurretBaseDefinition;
                var weaponDef = blockDef as MyWeaponBlockDefinition;
                var sorterDef = blockDef as MyConveyorSorterDefinition;
                var drillDef = blockDef as MyShipDrillDefinition;
                var pistonBaseDef = blockDef as MyPistonBaseDefinition;
                var beaconDef = blockDef as MyBeaconDefinition;
                var suspensionDef = blockDef as MyMotorSuspensionDefinition;
                var statorDef = blockDef as MyMotorStatorDefinition; //Motor stator is the base
                var advStatorDef = blockDef as MyMotorAdvancedStatorDefinition; //Motor stator is the base
                var thrustDef = blockDef as MyThrustDefinition;
                var gyroDef = blockDef as MyGyroDefinition;
                var cockpitDef = blockDef as MyCockpitDefinition;
                var remoteControlDef = blockDef as MyRemoteControlDefinition;
                var timerBlockDef = blockDef as MyTimerBlockDefinition;
                var hydroTankDef = blockDef as MyGasTankDefinition;
                var welderDef = blockDef as MyShipWelderDefinition;
                var oxygenGeneratorDef = blockDef as MyOxygenGeneratorDefinition;
                var batteryDef = blockDef as MyBatteryBlockDefinition;
                var laserAntennaDef = blockDef as MyLaserAntennaDefinition;

                blockDef.DamageMultiplierExplosion = blockExplosionResistanceMod;

                if (turretDef != null || weaponDef != null || (sorterDef != null && !sorterDef.Id.SubtypeName.Contains("ConveyorSorter")))
                {
                    blockDef.GeneralDamageMultiplier = 0.5f;
                }
                else
                {
                    blockDef.PCU = 0;
                }

                //light armor
                if (blockDef.EdgeType == "Light" && blockDef.BlockTopology != MyBlockTopology.TriangleMesh)
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
                if (blockDef.EdgeType == "Heavy")
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

                // Beam blocks and heat vents
                if (blockDef.EdgeType == "Light" && (blockDef.Id.SubtypeName.Contains("BeamBlock") || blockDef.Id.SubtypeName.Contains("HeatVentBlock")))
                {
                    if (blockDef.CubeSize == MyCubeSize.Large)
                    {
                        blockDef.GeneralDamageMultiplier = lightArmorLargeDamageMod;
                    }

                    if (blockDef.CubeSize == MyCubeSize.Small)
                    {
                        blockDef.GeneralDamageMultiplier = lightArmorSmallDamageMod;
                    }
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
                if (blockDef.Id.SubtypeName.Contains("Real"))
                {
                    blockDef.GeneralDamageMultiplier = realWheelDamageMod;

                    if (blockDef.Id.SubtypeName.Contains("5x5"))
                    {
                        blockDef.GeneralDamageMultiplier = realWheel5x5DamageMod;
                    }
                }

                //rotor and hinge top parts
                if (blockDef.Id.SubtypeName.Contains("Rotor") || blockDef.Id.SubtypeName.Contains("HingeHead"))
                {
                    blockDef.GeneralDamageMultiplier = rotorDamageMod;
                }

                //drills
                if (drillDef != null)
                {
                    drillDef.PCU = drillPCU;
                }

                //welders
                if (welderDef != null)
                {
                    welderDef.PCU = welderPCU;
                }

                //pistons
                if (pistonBaseDef != null)
                {
                    pistonBaseDef.PCU = pistonBasePCU;
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

                    if (!thrustDef.Id.SubtypeName.Contains("NPC") && !thrustDef.Id.SubtypeName.Contains("Hover"))
                    {
                        if (thrustDef.FuelConverter != null &&
                            !thrustDef.FuelConverter.FuelId.IsNull() &&
                            thrustDef.FuelConverter.FuelId.SubtypeId.Contains("Hydrogen"))
                        {
                            thrustDef.MinPlanetaryInfluence = 0f;
                            thrustDef.MaxPlanetaryInfluence = 0.25f;
                            thrustDef.EffectivenessAtMaxInfluence = 1f;
                            thrustDef.EffectivenessAtMinInfluence = 0f;
                            //thrustDef.NeedsAtmosphereForInfluence = false; //partially useless because it always searches for atmosphere regardless
                            //thrustDef.InvDiffMinMaxPlanetaryInfluence = 1f; 
                            thrustDef.ConsumptionFactorPerG = 0f;
                            thrustDef.SlowdownFactor = 1f;
                            //thrustDef.FuelConverter.Efficiency = 1f; //not using this because it now varies for large and small
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
                    hydroTankDef.Capacity = (float)Math.Ceiling(hydroTankDef.Size.Volume() * Math.Pow(hydroTankDef.CubeSize == MyCubeSize.Large ? 2.5 : 0.5, 3) * hydroTankH2Density);
                }

                // Fix the upgradeable O2/H2 gen
                if (oxygenGeneratorDef != null && oxygenGeneratorDef.Id.SubtypeId.String == "MA_O2")
                {
                    oxygenGeneratorDef.IceConsumptionPerSecond = 150;
                    // Make the generator exactly as efficient as normal gens, otherwise it's even more OP
                    oxygenGeneratorDef.OperationalPowerConsumption = 3;
                }

                if (batteryDef != null)
                {
                    batteryDef.InitialStoredPowerRatio = 0.05f;
                    foreach (var component in batteryDef.Components)
                    {
                        component.DeconstructItem = component.Definition;
                    }
                }

                if (laserAntennaDef != null)
                {
                    laserAntennaDef.RequireLineOfSight = false;
                }
            }
        }

        public override void LoadData()
        {
            DoWork();
        }
    }
}