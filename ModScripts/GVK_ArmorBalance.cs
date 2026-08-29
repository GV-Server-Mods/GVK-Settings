using Sandbox.Definitions;
using VRage.Game.Components;
using System;
using VRage.Game;
using VRage.Utils;
using VRageMath;

// =========================================================================
// GV: Deserts of Kharak (GVK) Server Settings & Mechanics
// Script: GVK_ArmorBalance.cs
// Original Foundation: Gauge (Balanced Deformation)
// GVK Armor Balance & Structural Overhaul: Mike Dude
// Deformation & Damage Filter Contributions: Merii
// Description: Comprehensive armor balance, structural resistance, deformation
// damage scaling, and heavy component survival tuning for planetary rover combat.
// =========================================================================

namespace GVK.ArmorBalance
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class ArmorBalance : MySessionComponentBase
    {
        // ==================== CONFIGURATION ====================
        // Centralized configuration for all armor and block modifiers.
        // Modify these values to rebalance the server or accommodate game updates.
        public static class Config
        {
            // Armor Properties (Deformation & Damage)
            public static class Armor
            {
                public const float LightLargeDamageMultiplier = 1.0f;
                public const float LightLargeDeformationRatio = 0.4f;
                public const float LightSmallDamageMultiplier = 1.0f;
                public const float LightSmallDeformationRatio = 0.4f;

                public const float HeavyLargeDamageMultiplier = 1.0f;
                public const float HeavyLargeDeformationRatio = 0.2f;
                public const float HeavySmallDamageMultiplier = 1.0f;
                public const float HeavySmallDeformationRatio = 0.2f;
            }

            // Mechanical Connections (Rotors, Hinges, Pistons)
            public static class Rotors
            {
                public const float MotorStatorDamageMultiplier = 0.25f;
                public const float MotorAdvancedStatorDamageMultiplier = 0.25f;
                public const float RotorHeadDamageMultiplier = 0.25f;
                public const float PistonBaseDamageMultiplier = 0.25f;
                public const float PistonTopDamageMultiplier = 0.25f;
            }

            // Hydrogen Tanks
            public static class HydrogenTanks
            {
                public const float LeakPercent = 0.025f;
                public const float GasExplosionDamageMultiplier = 0.00015f;
                public const string GasExplosionSound = "HydrogenExplosion";
                public const double H2Density = 15000000 / (2.5 * 2.5 * 2.5 * 27); // LG Large hydro tank capacity divided by its volume in meters
            }

            // Reactors
            public static class Reactors
            {
                public const float LargeSingleBlockPowerOutput = 40f;
                public const float LargeMultiBlockPowerOutput = 1210f;
                public const float SmallSingleBlockPowerOutput = 1.0f;
                public const float SmallMultiBlockPowerOutput = 30f;
                public const float ProprietarySpeedMultiplier = 5f;
            }

            // Solar Panels
            public static class Solar
            {
                public const float PowerOutputMultiplier = 2f;
            }

            // Containers
            public static class Containers
            {
                public const int LargeContainerCompCount = 240; // 54+ volume
                public const int MediumContainerCompCount = 120; // 27-54 volume
                public const int SmallContainerCompCount = 40; // < 27 volume
            }

            // Structural Blocks (XL and Econ2)
            public static class Structure
            {
                public const float XLLargeDamageMultiplier = 1.0f;
                public const bool XLUsesDeformation = false;
                public const float XLDeformationRatio = 0.45f;
                public const string XLEdgeType = "Heavy";
                public const int XLIntegrityPointsPerSec = 2500;
            }

            // Beam Blocks & Heat Vents
            public static class BeamAndVent
            {
                public const float LargeDamageMultiplier = 1.0f;
                public const float SmallDamageMultiplier = 1.0f;
            }

            // Suspension
            public static class Suspension
            {
                public const float DamageMultiplier = 0.25f;
                public const int IntegrityPointsPerSec = 500;
            }

            // Wheels
            public static class Wheels
            {
                public const float DamageMultiplier = 0.5f;
                public const int IntegrityPointsPerSec = 500;
            }

            // Beacons
            public static class Beacons
            {
                public const int MaxBroadcastRadius = 100000;
                public const int BlockBeaconPCU = 1;
                public const float DisposableNpcDamageMultiplier = 0.1f;
            }

            // Thrusters
            public static class Thrusters
            {
                public const float BaseDamageMultiplier = 0.5f;

                // Hydrogen Thrusters
                public const float HydrogenMaxPlanetaryInfluence = 0.25f;
                public const float HydrogenMinPlanetaryInfluence = 0.0f;
                public const float HydrogenEffectivenessAtMaxInfluence = 1.0f;
                public const float HydrogenEffectivenessAtMinInfluence = 0.0f;
                public const float HydrogenConsumptionFactorPerG = 0.0f;
                public const float HydrogenSlowdownFactor = 1.0f;

                // Flat Atmospheric Thrusters
                public const float FlatAtmosphericMaxPlanetaryInfluence = 0.75f;
                public const float FlatAtmosphericMinPlanetaryInfluence = 0.25f;
                public const float FlatAtmosphericEffectivenessAtMaxInfluence = 1.0f;
                public const float FlatAtmosphericEffectivenessAtMinInfluence = 0.0f;
                public const float FlatAtmosphericConsumptionFactorPerG = 0.0f;
                public const float FlatAtmosphericSlowdownFactor = 1.0f;

                // Hover Engines
                public const float HoverMaxPowerMultiplier = 3f;
                public const float HoverMinPowerMultiplier = 10f;
                public const string HoverThrusterType = "Ion";
                public const float HoverDamageMultiplier = 1.0f;

                // Ion Effect
                public const string IonDestroyEffectSuffix = "_Blue";
                public const string IonDamageEffect = "Damage_WeapExpl_Damaged_Blue";
            }

            // Gyros
            public static class Gyros
            {
                public const float DamageMultiplier = 2.0f;
            }

            // Ship Control Blocks (Cockpits, Remote Controls, Timer Blocks, AI Blocks)
            public static class ShipControl
            {
                public const float CockpitDamageMultiplier = 0.5f;
                public const float RemoteControlDamageMultiplier = 0.5f;
                public const float TimerBlockDamageMultiplier = 0.5f;
                public const float AIBlockDamageMultiplier = 0.5f;
                public const float ProgrammableBlockDamageMultiplier = 0.5f;
                public const float TurretControllerDamageMultiplier = 0.5f;
                public const float EventControllerDamageMultiplier = 0.5f;
                public const float BroadcastControllerDamageMultiplier = 0.5f;
            }

            // Laser Antenna
            public static class LaserAntenna
            {
                public const bool RequireLineOfSight = false;
            }
        }

        public const double hydroTankH2Density = Config.HydrogenTanks.H2Density;

        private MyComponentDefinition steelPlateComponent;

        private void DoWork()
        {
            steelPlateComponent = MyDefinitionManager.Static.GetComponentDefinition(new MyDefinitionId(typeof(MyObjectBuilder_Component), "SteelPlate"));

            foreach (var blockDef in MyDefinitionManager.Static.GetDefinitionsOfType<MyCubeBlockDefinition>())
            {
                if (blockDef == null) continue;

                // Apply general settings to all blocks
                blockDef.UseModelIntersection = true; // Attempt to make things placeable in tight spaces
                blockDef.PCU = 1; // Default PCU

                // Process each block type
                ProcessArmorBlocks(blockDef);
                ProcessRotorBlocks(blockDef);
                ProcessHydrogenTanks(blockDef);
                ProcessReactors(blockDef);
                ProcessSolarPanels(blockDef);
                ProcessLaserAntennas(blockDef);
                ProcessContainers(blockDef);
                ProcessStructuralBlocks(blockDef);
                ProcessBeamAndVentBlocks(blockDef);
                ProcessSuspension(blockDef);
                ProcessWheels(blockDef);
                ProcessBeacons(blockDef);
                ProcessThrusters(blockDef);
                ProcessGyros(blockDef);
                ProcessShipControlBlocks(blockDef);
            }
        }

        // ==================== BLOCK TYPE HANDLERS ====================

        /// <summary>
        /// Process armor blocks (light and heavy).
        /// Applies damage multipliers and deformation ratios per armor type and size to prevent drastic deformation clipping.
        /// </summary>
        private void ProcessArmorBlocks(MyCubeBlockDefinition blockDef)
        {
            // Skip structural blocks (XL and structural meshes are handled separately in ProcessStructuralBlocks)
            if (blockDef.Id.SubtypeName.Contains("XL_") || blockDef.Id.SubtypeName.Contains("Structural"))
            {
                return;
            }

            // Light armor
            if (blockDef.EdgeType == "Light" && blockDef.BlockTopology != MyBlockTopology.TriangleMesh)
            {
                if (blockDef.CubeSize == MyCubeSize.Large)
                {
                    blockDef.GeneralDamageMultiplier = Config.Armor.LightLargeDamageMultiplier;
                    blockDef.DeformationRatio = Config.Armor.LightLargeDeformationRatio;
                }
                else if (blockDef.CubeSize == MyCubeSize.Small)
                {
                    blockDef.GeneralDamageMultiplier = Config.Armor.LightSmallDamageMultiplier;
                    blockDef.DeformationRatio = Config.Armor.LightSmallDeformationRatio;
                }
            }

            // Heavy armor
            if (blockDef.EdgeType == "Heavy" && blockDef.BlockTopology != MyBlockTopology.TriangleMesh)
            {
                if (blockDef.CubeSize == MyCubeSize.Large)
                {
                    blockDef.GeneralDamageMultiplier = Config.Armor.HeavyLargeDamageMultiplier;
                    blockDef.DeformationRatio = Config.Armor.HeavyLargeDeformationRatio;
                }
                else if (blockDef.CubeSize == MyCubeSize.Small)
                {
                    blockDef.GeneralDamageMultiplier = Config.Armor.HeavySmallDamageMultiplier;
                    blockDef.DeformationRatio = Config.Armor.HeavySmallDeformationRatio;
                }

                // Flip component order (functional component at the end) if components array is valid
                if (blockDef.Components != null && blockDef.Components.Length >= 2)
                {
                    var lastCompIdx = blockDef.Components.Length - 1;
                    if (blockDef.Components[0].Definition != null &&
                        blockDef.Components[lastCompIdx].Definition != null &&
                        blockDef.Components[0].Count > blockDef.Components[lastCompIdx].Count &&
                        blockDef.Components[0].Definition.Id == blockDef.Components[lastCompIdx].Definition.Id)
                    {
                        var temp = blockDef.Components[0];
                        blockDef.Components[0] = blockDef.Components[lastCompIdx];
                        blockDef.Components[lastCompIdx] = temp;
                    }
                }
            }
        }

        /// <summary>
        /// Process mechanical connection blocks (rotors, hinges, pistons).
        /// Apply damage resistance multiplier to prevent shearing under fire or impact.
        /// </summary>
        private void ProcessRotorBlocks(MyCubeBlockDefinition blockDef)
        {
            var statorDef = blockDef as MyMotorStatorDefinition;
            var advStatorDef = blockDef as MyMotorAdvancedStatorDefinition;
            var pistonDef = blockDef as MyPistonBaseDefinition;

            if (advStatorDef != null)
            {
                advStatorDef.GeneralDamageMultiplier = Config.Rotors.MotorAdvancedStatorDamageMultiplier;
            }
            else if (statorDef != null)
            {
                statorDef.GeneralDamageMultiplier = Config.Rotors.MotorStatorDamageMultiplier;
            }

            if (pistonDef != null)
            {
                pistonDef.GeneralDamageMultiplier = Config.Rotors.PistonBaseDamageMultiplier;
            }

            if (blockDef.Id.SubtypeName.Contains("Rotor") || blockDef.Id.SubtypeName.Contains("HingeHead"))
            {
                blockDef.GeneralDamageMultiplier = Config.Rotors.RotorHeadDamageMultiplier;
            }
            else if (blockDef.Id.SubtypeName.Contains("PistonTop"))
            {
                blockDef.GeneralDamageMultiplier = Config.Rotors.PistonTopDamageMultiplier;
            }
        }

        /// <summary>
        /// Process hydrogen tank blocks.
        /// Standardize H2 tank capacity to scale linearly with block volume.
        /// </summary>
        private void ProcessHydrogenTanks(MyCubeBlockDefinition blockDef)
        {
            var hydroTankDef = blockDef as MyGasTankDefinition;

            if (hydroTankDef != null && hydroTankDef.StoredGasId.SubtypeName == "Hydrogen")
            {
                hydroTankDef.LeakPercent = Config.HydrogenTanks.LeakPercent;
                hydroTankDef.Capacity = (float)Math.Ceiling(
                    hydroTankDef.Size.Volume() *
                    Math.Pow(hydroTankDef.CubeSize == MyCubeSize.Large ? 2.5 : 0.5, 3) *
                    hydroTankH2Density
                );
                hydroTankDef.GasExplosionMaxRadius = hydroTankDef.Size.Length() *
                    (hydroTankDef.CubeSize == MyCubeSize.Large ? 2.5f : 0.5f);
                hydroTankDef.GasExplosionDamageMultiplier = Config.HydrogenTanks.GasExplosionDamageMultiplier;

                if (string.IsNullOrEmpty(hydroTankDef.GasExplosionSound))
                {
                    hydroTankDef.GasExplosionSound = Config.HydrogenTanks.GasExplosionSound;
                }

                hydroTankDef.GasExplosionNeededVolumeToReachMaxRadius = hydroTankDef.Capacity;
            }
        }

        /// <summary>
        /// Process reactor blocks.
        /// Adjust power output based on size and make NPC reactors more powerful.
        /// </summary>
        private void ProcessReactors(MyCubeBlockDefinition blockDef)
        {
            var reactorDef = blockDef as MyReactorDefinition;

            if (reactorDef != null)
            {
                if (reactorDef.CubeSize == MyCubeSize.Large)
                {
                    if (reactorDef.Size.Volume() <= 1f)
                    {
                        reactorDef.MaxPowerOutput = Config.Reactors.LargeSingleBlockPowerOutput;
                    }
                    else
                    {
                        reactorDef.MaxPowerOutput = Config.Reactors.LargeMultiBlockPowerOutput;
                    }
                }
                else
                {
                    if (reactorDef.Size.Volume() <= 1f)
                    {
                        reactorDef.MaxPowerOutput = Config.Reactors.SmallSingleBlockPowerOutput;
                    }
                    else
                    {
                        reactorDef.MaxPowerOutput = Config.Reactors.SmallMultiBlockPowerOutput;
                    }
                }

                // Buff NPC Proprietary reactors and make them not require fuel
                if (reactorDef.Id.SubtypeName.Contains("Proprietary"))
                {
                    reactorDef.MaxPowerOutput *= Config.Reactors.ProprietarySpeedMultiplier;
                    reactorDef.FuelInfos = new MyReactorDefinition.FuelInfo[0];
                }
            }
        }

        /// <summary>
        /// Process solar panels.
        /// Double power output to compensate for banned solar tracking scripts.
        /// </summary>
        private void ProcessSolarPanels(MyCubeBlockDefinition blockDef)
        {
            var solarDef = blockDef as MySolarPanelDefinition;

            if (solarDef != null)
            {
                solarDef.MaxPowerOutput *= Config.Solar.PowerOutputMultiplier;
            }
        }

        /// <summary>
        /// Process laser antenna blocks.
        /// Remove line of sight requirement.
        /// </summary>
        private void ProcessLaserAntennas(MyCubeBlockDefinition blockDef)
        {
            var laserAntennaDef = blockDef as MyLaserAntennaDefinition;

            if (laserAntennaDef != null)
            {
                laserAntennaDef.RequireLineOfSight = Config.LaserAntenna.RequireLineOfSight;
            }
        }

        /// <summary>
        /// Process cargo container blocks.
        /// Adjust component counts to be proportional to block volume.
        /// </summary>
        private void ProcessContainers(MyCubeBlockDefinition blockDef)
        {
            var cargoDef = blockDef as MyCargoContainerDefinition;

            if (cargoDef != null && cargoDef.CubeSize == MyCubeSize.Large && cargoDef.Id.SubtypeName.Contains("Container"))
            {
                if (steelPlateComponent == null || cargoDef.Components == null || cargoDef.Components.Length == 0)
                {
                    return;
                }

                if (cargoDef.Size.Volume() >= 54)
                {
                    ReplaceComponent(cargoDef, cargoDef.Components.Length - 1, steelPlateComponent, Config.Containers.LargeContainerCompCount);
                }
                else if (cargoDef.Size.Volume() >= 27)
                {
                    ReplaceComponent(cargoDef, cargoDef.Components.Length - 1, steelPlateComponent, Config.Containers.MediumContainerCompCount);
                }
                else
                {
                    ReplaceComponent(cargoDef, cargoDef.Components.Length - 1, steelPlateComponent, Config.Containers.SmallContainerCompCount);
                }
            }
        }

        /// <summary>
        /// Process structural blocks (5x5 XL and Econ2).
        /// Make them heavy, remove deformation, and increase weld time for rigid impact resistance.
        /// </summary>
        private void ProcessStructuralBlocks(MyCubeBlockDefinition blockDef)
        {
            if (blockDef.CubeSize == MyCubeSize.Large &&
                (blockDef.Id.SubtypeName.Contains("XL_") || blockDef.Id.SubtypeName.Contains("LargeBlockStructural_")) &&
                blockDef.BlockTopology == MyBlockTopology.TriangleMesh)
            {
                blockDef.GeneralDamageMultiplier = Config.Structure.XLLargeDamageMultiplier;
                blockDef.UsesDeformation = Config.Structure.XLUsesDeformation;
                blockDef.DeformationRatio = Config.Structure.XLDeformationRatio;
                blockDef.EdgeType = Config.Structure.XLEdgeType;
                blockDef.IntegrityPointsPerSec = Config.Structure.XLIntegrityPointsPerSec;
            }
        }

        /// <summary>
        /// Process beam blocks and heat vents.
        /// Apply damage multiplier settings.
        /// </summary>
        private void ProcessBeamAndVentBlocks(MyCubeBlockDefinition blockDef)
        {
            if (blockDef.EdgeType == "Light" &&
                (blockDef.Id.SubtypeName.Contains("BeamBlock") || blockDef.Id.SubtypeName.Contains("HeatVentBlock")))
            {
                if (blockDef.CubeSize == MyCubeSize.Large)
                {
                    blockDef.GeneralDamageMultiplier = Config.BeamAndVent.LargeDamageMultiplier;
                }
                else if (blockDef.CubeSize == MyCubeSize.Small)
                {
                    blockDef.GeneralDamageMultiplier = Config.BeamAndVent.SmallDamageMultiplier;
                }
            }
        }

        /// <summary>
        /// Process suspension blocks.
        /// Apply resistance buff and weld time.
        /// </summary>
        private void ProcessSuspension(MyCubeBlockDefinition blockDef)
        {
            var suspensionDef = blockDef as MyMotorSuspensionDefinition;

            if (suspensionDef != null)
            {
                suspensionDef.GeneralDamageMultiplier = Config.Suspension.DamageMultiplier;
                suspensionDef.IntegrityPointsPerSec = Config.Suspension.IntegrityPointsPerSec;
            }
        }

        /// <summary>
        /// Process wheel blocks.
        /// Apply resistance buff and weld time.
        /// </summary>
        private void ProcessWheels(MyCubeBlockDefinition blockDef)
        {
            if (blockDef.Id.SubtypeName.Contains("Real"))
            {
                blockDef.GeneralDamageMultiplier = Config.Wheels.DamageMultiplier;
                blockDef.IntegrityPointsPerSec = Config.Wheels.IntegrityPointsPerSec;
            }
        }

        /// <summary>
        /// Process beacon blocks.
        /// Increase broadcast radius and adjust damage for specific beacon types.
        /// </summary>
        private void ProcessBeacons(MyCubeBlockDefinition blockDef)
        {
            var beaconDef = blockDef as MyBeaconDefinition;

            if (beaconDef != null)
            {
                if (!beaconDef.Id.SubtypeName.Contains("DrillBlocker"))
                {
                    beaconDef.MaxBroadcastRadius = Config.Beacons.MaxBroadcastRadius;
                }

                if (beaconDef.Id.SubtypeName.Contains("BlockBeacon"))
                {
                    beaconDef.PCU = Config.Beacons.BlockBeaconPCU;
                }

                if (beaconDef.Id.SubtypeName.Contains("DisposableNpc"))
                {
                    beaconDef.GeneralDamageMultiplier = Config.Beacons.DisposableNpcDamageMultiplier;
                }
            }
        }

        /// <summary>
        /// Process thruster blocks.
        /// Adjust planetary influence, effectiveness, and special handling for hovers and ions.
        /// </summary>
        private void ProcessThrusters(MyCubeBlockDefinition blockDef)
        {
            var thrustDef = blockDef as MyThrustDefinition;

            if (thrustDef == null)
            {
                return;
            }

            thrustDef.GeneralDamageMultiplier = Config.Thrusters.BaseDamageMultiplier;

            // Regular hydrogen and atmospheric thrusters
            if (!thrustDef.Id.SubtypeName.Contains("NPC") && !thrustDef.Id.SubtypeName.Contains("Hover"))
            {
                if (thrustDef.FuelConverter != null &&
                    !thrustDef.FuelConverter.FuelId.IsNull() &&
                    thrustDef.FuelConverter.FuelId.SubtypeId.Contains("Hydrogen"))
                {
                    ApplyHydrogenThrusterSettings(thrustDef);
                }
                else if (thrustDef.Id.SubtypeName.Contains("FlatAtmosphericThrust"))
                {
                    ApplyFlatAtmosphericThrusterSettings(thrustDef);
                }
            }

            // Hover engines
            if (thrustDef.Id.SubtypeName.Contains("Hover"))
            {
                ApplyHoverThrusterSettings(thrustDef);
            }

            // Ion effect
            if (thrustDef.ThrusterType == MyStringHash.GetOrCompute("Ion"))
            {
                if (!string.IsNullOrEmpty(thrustDef.DestroyEffect) && !thrustDef.DestroyEffect.EndsWith(Config.Thrusters.IonDestroyEffectSuffix))
                {
                    thrustDef.DestroyEffect = thrustDef.DestroyEffect + Config.Thrusters.IonDestroyEffectSuffix;
                }
                thrustDef.DamageEffectName = Config.Thrusters.IonDamageEffect;
            }
        }

        private void ApplyHydrogenThrusterSettings(MyThrustDefinition thrustDef)
        {
            thrustDef.MaxPlanetaryInfluence = Config.Thrusters.HydrogenMaxPlanetaryInfluence;
            thrustDef.MinPlanetaryInfluence = Config.Thrusters.HydrogenMinPlanetaryInfluence;
            thrustDef.InvDiffMinMaxPlanetaryInfluence = 1f /
                (thrustDef.MaxPlanetaryInfluence - thrustDef.MinPlanetaryInfluence);
            thrustDef.EffectivenessAtMaxInfluence = Config.Thrusters.HydrogenEffectivenessAtMaxInfluence;
            thrustDef.EffectivenessAtMinInfluence = Config.Thrusters.HydrogenEffectivenessAtMinInfluence;
            thrustDef.ConsumptionFactorPerG = Config.Thrusters.HydrogenConsumptionFactorPerG;
            thrustDef.SlowdownFactor = Config.Thrusters.HydrogenSlowdownFactor;
        }

        private void ApplyFlatAtmosphericThrusterSettings(MyThrustDefinition thrustDef)
        {
            thrustDef.MaxPlanetaryInfluence = Config.Thrusters.FlatAtmosphericMaxPlanetaryInfluence;
            thrustDef.MinPlanetaryInfluence = Config.Thrusters.FlatAtmosphericMinPlanetaryInfluence;
            thrustDef.InvDiffMinMaxPlanetaryInfluence = 1f /
                (thrustDef.MaxPlanetaryInfluence - thrustDef.MinPlanetaryInfluence);
            thrustDef.EffectivenessAtMaxInfluence = Config.Thrusters.FlatAtmosphericEffectivenessAtMaxInfluence;
            thrustDef.EffectivenessAtMinInfluence = Config.Thrusters.FlatAtmosphericEffectivenessAtMinInfluence;
            thrustDef.ConsumptionFactorPerG = Config.Thrusters.FlatAtmosphericConsumptionFactorPerG;
            thrustDef.SlowdownFactor = Config.Thrusters.FlatAtmosphericSlowdownFactor;
        }

        private void ApplyHoverThrusterSettings(MyThrustDefinition thrustDef)
        {
            thrustDef.ThrusterType = MyStringHash.GetOrCompute(Config.Thrusters.HoverThrusterType);
            thrustDef.MaxPowerConsumption *= Config.Thrusters.HoverMaxPowerMultiplier;
            thrustDef.MinPowerConsumption *= Config.Thrusters.HoverMinPowerMultiplier;

            if (thrustDef.Size.Volume() <= 1f)
            {
                thrustDef.DestroyEffect = "BlockDestroyedExplosion_Small";
            }
            else
            {
                thrustDef.DestroyEffect = "BlockDestroyedExplosion_Large";
            }

            thrustDef.GeneralDamageMultiplier = Config.Thrusters.HoverDamageMultiplier;
        }

        /// <summary>
        /// Process gyro blocks.
        /// Nerf gyros because they are better than armor.
        /// </summary>
        private void ProcessGyros(MyCubeBlockDefinition blockDef)
        {
            var gyroDef = blockDef as MyGyroDefinition;
            var upgradeModuleDef = blockDef as MyUpgradeModuleDefinition;

            if (gyroDef != null || (upgradeModuleDef != null && blockDef.Id.SubtypeName.Contains("Gyro")))
            {
                blockDef.GeneralDamageMultiplier = Config.Gyros.DamageMultiplier;
            }
        }

        /// <summary>
        /// Process ship control blocks (cockpits, remote controls, timer blocks, AI blocks, programmable blocks).
        /// Buff resistance on critical ship control related blocks.
        /// </summary>
        private void ProcessShipControlBlocks(MyCubeBlockDefinition blockDef)
        {
            var cockpitDef = blockDef as MyCockpitDefinition;
            var remoteControlDef = blockDef as MyRemoteControlDefinition;
            var timerBlockDef = blockDef as MyTimerBlockDefinition;
            var defensiveCombatDef = blockDef as MyDefensiveCombatBlockDefinition;
            var offensiveCombatDef = blockDef as MyOffensiveCombatBlockDefinition;
            var pathRecorderDef = blockDef as MyPathRecorderBlockDefinition;
            var basicMissionDef = blockDef as MyBasicMissionBlockDefinition;
            var flightMovementDef = blockDef as MyFlightMovementBlockDefinition;
            var eventControllerDef = blockDef as MyEventControllerBlockDefinition;
            var broadcastControllerDef = blockDef as MyBroadcastControllerDefinition;
            var programmableBlockDef = blockDef as MyProgrammableBlockDefinition;
            var turretControllerDef = blockDef as MyTurretControlBlockDefinition;

            if (cockpitDef != null)
            {
                cockpitDef.GeneralDamageMultiplier = Config.ShipControl.CockpitDamageMultiplier;
            }

            if (defensiveCombatDef != null || offensiveCombatDef != null || pathRecorderDef != null || basicMissionDef != null || flightMovementDef != null)
            {
                blockDef.GeneralDamageMultiplier = Config.ShipControl.AIBlockDamageMultiplier;
            }

            if (eventControllerDef != null)
            {
                eventControllerDef.GeneralDamageMultiplier = Config.ShipControl.EventControllerDamageMultiplier;
            }

            if (broadcastControllerDef != null)
            {
                broadcastControllerDef.GeneralDamageMultiplier = Config.ShipControl.BroadcastControllerDamageMultiplier;
            }

            if (programmableBlockDef != null)
            {
                programmableBlockDef.GeneralDamageMultiplier = Config.ShipControl.ProgrammableBlockDamageMultiplier;
            }

            if (remoteControlDef != null)
            {
                remoteControlDef.GeneralDamageMultiplier = Config.ShipControl.RemoteControlDamageMultiplier;
            }

            if (timerBlockDef != null)
            {
                timerBlockDef.GeneralDamageMultiplier = Config.ShipControl.TimerBlockDamageMultiplier;
            }

            if (turretControllerDef != null)
            {
                turretControllerDef.GeneralDamageMultiplier = Config.ShipControl.TurretControllerDamageMultiplier;
            }
        }

        // ==================== HELPER METHODS ====================

        // Main method to do the modifications
        public override void LoadData()
        {
            DoWork();
        }

        // Method to replace components in a block construction list
        private static void ReplaceComponent(MyCubeBlockDefinition blockDef, int index, MyComponentDefinition newComp, int newCount, MyPhysicalItemDefinition deconstructItem = null)
        {
            var comp = blockDef.Components[index];
            var oldCount = comp.Count;
            float intDiff;
            float massDiff;
            if (newCount > 0)
            {
                intDiff = newComp.MaxIntegrity * newCount - comp.Definition.MaxIntegrity * oldCount;
                massDiff = newComp.Mass * newCount - comp.Definition.Mass * oldCount;

                blockDef.Components[index].Count = newCount;
            }
            else
            {
                intDiff = (newComp.MaxIntegrity - comp.Definition.MaxIntegrity) * oldCount;
                massDiff = (newComp.Mass - comp.Definition.Mass) * oldCount;
            }

            comp.Definition = newComp;
            comp.DeconstructItem = deconstructItem ?? newComp;

            blockDef.MaxIntegrity += intDiff;
            blockDef.Mass += massDiff;

            SetRatios(blockDef, blockDef.CriticalGroup);
        }

        // Method to insert components into block construction list
        private static void InsertComponent(MyCubeBlockDefinition blockDef, int componentIndex, MyComponentDefinition comp, int count, MyPhysicalItemDefinition deconstructItem = null, bool makeCritical = false)
        {
            var intDiff = comp.MaxIntegrity * count;
            var massDiff = comp.Mass * count;

            if (makeCritical)
            {
                blockDef.CriticalGroup = (ushort)componentIndex;
            }
            else
                if (componentIndex <= blockDef.CriticalGroup)
                {
                    blockDef.CriticalGroup += 1;
                }

            blockDef.MaxIntegrity += intDiff;
            blockDef.Mass += massDiff;

            var newComps = new MyCubeBlockDefinition.Component[blockDef.Components.Length + 1];

            if (componentIndex == 0)
            {
                newComps[0] = new MyCubeBlockDefinition.Component
                {
                    Definition = comp,
                    DeconstructItem = deconstructItem ?? comp,
                    Count = count
                };
                blockDef.Components.CopyTo(newComps, 1);
            }
            else if (componentIndex == blockDef.Components.Length)
            {
                newComps[blockDef.Components.Length] = new MyCubeBlockDefinition.Component
                {
                    Definition = comp,
                    DeconstructItem = comp,
                    Count = count
                };
                blockDef.Components.CopyTo(newComps, 0);
            }
            else
            {
                for (var index = 0; index < newComps.Length; index++)
                {
                    if (index < componentIndex)
                    {
                        newComps[index] = blockDef.Components[index];
                    }
                    else if (index == componentIndex)
                    {
                        newComps[index] = new MyCubeBlockDefinition.Component
                        {
                            Definition = comp,
                            DeconstructItem = comp,
                            Count = count
                        };
                    }
                    else
                    {
                        newComps[index] = blockDef.Components[index - 1];
                    }
                }
            }

            blockDef.Components = newComps;

            SetRatios(blockDef, blockDef.CriticalGroup);
        }

        private void SortAndSplitArmor(MyCubeBlockDefinition blockDef)
        {
            if (blockDef.Components.Length <= 1 || blockDef.CriticalGroup == blockDef.Components.Length - 1)
            {
                return;
            }
            var nextCompIndex = MathHelper.Clamp(blockDef.CriticalGroup + 1, 0, blockDef.Components.Length - 1);
            var nextCompLow = (int)Math.Floor(blockDef.Components[nextCompIndex].Count / 2f);
            var nextCompHigh = (int)Math.Ceiling(blockDef.Components[nextCompIndex].Count / 2f);
            blockDef.Components[nextCompIndex].Count = nextCompLow;
            InsertComponent(blockDef, nextCompIndex, blockDef.Components[nextCompIndex].Definition, nextCompHigh, makeCritical: true);
        }

        // Method to set ratio of critical component and ownership of a block
        private static void SetRatios(MyCubeBlockDefinition blockDef, int criticalIndex)
        {
            if (blockDef == null || blockDef.Components == null || blockDef.Components.Length == 0 || blockDef.MaxIntegrity <= 0f)
            {
                return;
            }

            var criticalIntegrity = 0f;
            var ownershipIntegrity = 0f;
            var clampedIndex = MathHelper.Clamp(criticalIndex, 0, blockDef.Components.Length - 1);

            for (var index = 0; index <= clampedIndex; index++)
            {
                var component = blockDef.Components[index];
                if (component.Definition == null)
                {
                    continue;
                }

                if (ownershipIntegrity == 0f && component.Definition.Id.SubtypeName == "Computer")
                {
                    ownershipIntegrity = criticalIntegrity + component.Definition.MaxIntegrity;
                }

                criticalIntegrity += component.Count * component.Definition.MaxIntegrity;
                if (index == clampedIndex)
                {
                    criticalIntegrity -= component.Definition.MaxIntegrity;
                }
            }

            blockDef.CriticalIntegrityRatio = criticalIntegrity / blockDef.MaxIntegrity;
            blockDef.OwnershipIntegrityRatio = ownershipIntegrity / blockDef.MaxIntegrity;

            if (blockDef.BuildProgressModels != null)
            {
                var count = blockDef.BuildProgressModels.Length;
                for (var index = 0; index < count; index++)
                {
                    var buildPercent = (index + 1f) / count;
                    blockDef.BuildProgressModels[index].BuildRatioUpperBound = buildPercent * blockDef.CriticalIntegrityRatio;
                }
            }
        }
    }
}
