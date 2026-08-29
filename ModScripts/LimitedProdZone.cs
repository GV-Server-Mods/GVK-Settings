using System;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

// =========================================================================
// GV: Deserts of Kharak (GVK) Server Settings & Mechanics
// Script: LimitedProdZone.cs
// Foundation / Concept: Based on NoFlyZone by Kamikaze
// Adaptation & Multi-Tiered Zone Governance: Mike Dude
// Description: Manages the 35km Production Zone (restricting advanced refineries & assemblers)
// and the 20km Weapon/Drill Zone (restricting military weapons, sorters, and ship drills).
// =========================================================================

namespace GVK.LimitedProdZone
{
    /// <summary>
    /// Static manager tracking Zone 0 (Crossroads Tower) boundaries and active beacons.
    /// Manages the 35km Production Zone (restricting heavy assemblers/refineries)
    /// and the 20km Weapon & Drill Zone (restricting military weapons, sorters, and ship drills).
    /// </summary>
    public static class LimitedProdZone_Manager
    {
        private static readonly List<IMyBeacon> beacons = new List<IMyBeacon>();
        private static readonly object beaconLock = new object();

        // Crossroads Tower Center Coordinates
        public static readonly Vector3D ZoneCenter = new Vector3D(62495.48, 28019.26, 37195.34);

        // Radii
        public const double ProductionRadius = 34500d;
        public const double ProductionRadiusSquared = 1190250000d; // 34.5km

        public const double WeaponRadius = 20000d;
        public const double WeaponRadiusSquared = 400000000d;     // 20.0km

        public static void AddBeacon(IMyBeacon beacon)
        {
            if (beacon == null) return;
            lock (beaconLock)
            {
                if (!beacons.Contains(beacon))
                    beacons.Add(beacon);
            }
        }

        public static void RemoveBeacon(IMyBeacon beacon)
        {
            if (beacon == null) return;
            lock (beaconLock)
            {
                if (beacons.Contains(beacon))
                    beacons.Remove(beacon);
            }
        }

        public static bool AnyEnabled
        {
            get
            {
                lock (beaconLock)
                {
                    for (int i = 0; i < beacons.Count; i++)
                    {
                        var b = beacons[i];
                        if (b != null && !b.Closed && b.IsWorking && b.Enabled)
                            return true;
                    }
                }
                return false;
            }
        }

        public static bool IsPositionInProductionZone(Vector3D position)
        {
            // Primary check: distance from static Zone 0 center
            if (Vector3D.DistanceSquared(position, ZoneCenter) < ProductionRadiusSquared)
                return true;

            // Secondary check: distance from active beacons
            lock (beaconLock)
            {
                for (int i = 0; i < beacons.Count; i++)
                {
                    var b = beacons[i];
                    if (b != null && !b.Closed && b.IsWorking && b.Enabled)
                    {
                        if (Vector3D.DistanceSquared(position, b.GetPosition()) < ProductionRadiusSquared)
                            return true;
                    }
                }
            }
            return false;
        }

        public static bool IsPositionInWeaponZone(Vector3D position)
        {
            // Primary check: distance from static Zone 0 center
            if (Vector3D.DistanceSquared(position, ZoneCenter) < WeaponRadiusSquared)
                return true;

            // Secondary check: distance from active beacons
            lock (beaconLock)
            {
                for (int i = 0; i < beacons.Count; i++)
                {
                    var b = beacons[i];
                    if (b != null && !b.Closed && b.IsWorking && b.Enabled)
                    {
                        if (Vector3D.DistanceSquared(position, b.GetPosition()) < WeaponRadiusSquared)
                            return true;
                    }
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Game logic component for beacons managing LimitedProdZone boundaries.
    /// </summary>
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Beacon), false, new string[] { "LimitedProdZone" })]
    public class LimitedProdZone_Beacon : MyGameLogicComponent
    {
        private IMyBeacon beacon;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            beacon = Entity as IMyBeacon;
            LimitedProdZone_Manager.AddBeacon(beacon);
        }

        public override void Close()
        {
            base.Close();
            if (beacon != null)
            {
                LimitedProdZone_Manager.RemoveBeacon(beacon);
            }
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();
            if (beacon != null)
            {
                LimitedProdZone_Manager.RemoveBeacon(beacon);
            }
        }
    }

    /// <summary>
    /// Game logic component disabling non-basic assemblers within the 35km Production Zone.
    /// Basic and food assemblers are exempt.
    /// </summary>
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Assembler), false)]
    public class LimitedProdZone_Assembler : MyGameLogicComponent
    {
        private IMyAssembler assembler;
        private bool isServer;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            assembler = Entity as IMyAssembler;
            if (assembler != null)
            {
                NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
                NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
            }
        }

        public override void UpdateOnceBeforeFrame()
        {
            base.UpdateOnceBeforeFrame();

            isServer = MyAPIGateway.Multiplayer.IsServer;
            if (isServer && assembler != null)
            {
                assembler.IsWorkingChanged += WorkingStateChange;
            }
        }

        public override void UpdateBeforeSimulation100()
        {
            base.UpdateBeforeSimulation100();

            try
            {
                if (!isServer || assembler == null || !assembler.Enabled) return;

                string subtype = assembler.BlockDefinition.SubtypeId.ToString();
                if (subtype.Contains("Basic") || subtype.Contains("Food")) return;

                if (LimitedProdZone_Manager.IsPositionInProductionZone(assembler.GetPosition()))
                {
                    assembler.Enabled = false;
                }
            }
            catch (Exception exc)
            {
                MyLog.Default.WriteLineAndConsole($"Failed checking LimitedProdZone assembler position: {exc}");
            }
        }

        private void WorkingStateChange(IMyCubeBlock block)
        {
            try
            {
                if (!isServer || assembler == null || !assembler.Enabled) return;

                string subtype = assembler.BlockDefinition.SubtypeId.ToString();
                if (subtype.Contains("Basic") || subtype.Contains("Food")) return;

                if (LimitedProdZone_Manager.IsPositionInProductionZone(assembler.GetPosition()))
                {
                    assembler.Enabled = false;
                }
            }
            catch (Exception exc)
            {
                MyLog.Default.WriteLineAndConsole($"Failed checking LimitedProdZone assembler working state change: {exc}");
            }
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();
            if (assembler != null)
            {
                assembler.IsWorkingChanged -= WorkingStateChange;
            }
        }

        public override void Close()
        {
            base.Close();
            if (assembler != null)
            {
                assembler.IsWorkingChanged -= WorkingStateChange;
            }
        }
    }

    /// <summary>
    /// Game logic component disabling non-basic refineries within the 35km Production Zone.
    /// Blast furnaces and NPC scrap refineries are exempt.
    /// </summary>
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Refinery), false)]
    public class LimitedProdZone_Refinery : MyGameLogicComponent
    {
        private IMyRefinery refinery;
        private bool isServer;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            refinery = Entity as IMyRefinery;
            if (refinery != null)
            {
                NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
                NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
            }
        }

        public override void UpdateOnceBeforeFrame()
        {
            base.UpdateOnceBeforeFrame();

            isServer = MyAPIGateway.Multiplayer.IsServer;
            if (isServer && refinery != null)
            {
                refinery.IsWorkingChanged += WorkingStateChange;
            }
        }

        public override void UpdateBeforeSimulation100()
        {
            base.UpdateBeforeSimulation100();

            try
            {
                if (!isServer || refinery == null || !refinery.Enabled) return;

                string subtype = refinery.BlockDefinition.SubtypeId.ToString();
                bool isBasic = subtype.Contains("Blast Furnace") || subtype.Contains("LargeRefinery_NPC_CU");
                if (isBasic) return;

                if (LimitedProdZone_Manager.IsPositionInProductionZone(refinery.GetPosition()))
                {
                    refinery.Enabled = false;
                }
            }
            catch (Exception exc)
            {
                MyLog.Default.WriteLineAndConsole($"Failed checking LimitedProdZone refinery position: {exc}");
            }
        }

        private void WorkingStateChange(IMyCubeBlock block)
        {
            try
            {
                if (!isServer || refinery == null || !refinery.Enabled) return;

                string subtype = refinery.BlockDefinition.SubtypeId.ToString();
                bool isBasic = subtype.Contains("Blast Furnace") || subtype.Contains("LargeRefinery_NPC_CU");
                if (isBasic) return;

                if (LimitedProdZone_Manager.IsPositionInProductionZone(refinery.GetPosition()))
                {
                    refinery.Enabled = false;
                }
            }
            catch (Exception exc)
            {
                MyLog.Default.WriteLineAndConsole($"Failed checking LimitedProdZone refinery working state change: {exc}");
            }
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();
            if (refinery != null)
            {
                refinery.IsWorkingChanged -= WorkingStateChange;
            }
        }

        public override void Close()
        {
            base.Close();
            if (refinery != null)
            {
                refinery.IsWorkingChanged -= WorkingStateChange;
            }
        }
    }

    /// <summary>
    /// Game logic component disabling ship drills within the 20km Zone 0 perimeter.
    /// Static resource well drills (BasicStaticDrill) are exempt.
    /// </summary>
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Drill), false)]
    public class LimitedProdZone_StaticDrill : MyGameLogicComponent
    {
        private IMyShipDrill staticDrill;
        private bool isServer;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            staticDrill = Entity as IMyShipDrill;
            if (staticDrill != null)
            {
                NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
                NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
            }
        }

        public override void UpdateOnceBeforeFrame()
        {
            base.UpdateOnceBeforeFrame();

            isServer = MyAPIGateway.Multiplayer.IsServer;
            if (isServer && staticDrill != null)
            {
                staticDrill.IsWorkingChanged += WorkingStateChange;
            }
        }

        public override void UpdateBeforeSimulation100()
        {
            base.UpdateBeforeSimulation100();

            try
            {
                if (!isServer || staticDrill == null || !staticDrill.Enabled) return;

                string subtype = staticDrill.BlockDefinition.SubtypeId.ToString();
                if (subtype.Contains("BasicStaticDrill")) return;

                if (LimitedProdZone_Manager.IsPositionInWeaponZone(staticDrill.GetPosition()))
                {
                    staticDrill.Enabled = false;
                }
            }
            catch (Exception exc)
            {
                MyLog.Default.WriteLineAndConsole($"Failed checking LimitedProdZone ship drill position: {exc}");
            }
        }

        private void WorkingStateChange(IMyCubeBlock block)
        {
            try
            {
                if (!isServer || staticDrill == null || !staticDrill.Enabled) return;

                string subtype = staticDrill.BlockDefinition.SubtypeId.ToString();
                if (subtype.Contains("BasicStaticDrill")) return;

                if (LimitedProdZone_Manager.IsPositionInWeaponZone(staticDrill.GetPosition()))
                {
                    staticDrill.Enabled = false;
                }
            }
            catch (Exception exc)
            {
                MyLog.Default.WriteLineAndConsole($"Failed checking LimitedProdZone ship drill working state change: {exc}");
            }
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();
            if (staticDrill != null)
            {
                staticDrill.IsWorkingChanged -= WorkingStateChange;
            }
        }

        public override void Close()
        {
            base.Close();
            if (staticDrill != null)
            {
                staticDrill.IsWorkingChanged -= WorkingStateChange;
            }
        }
    }

    /// <summary>
    /// Base component enforcing Zone 0 (20km) restrictions on vanilla turrets and missile launchers.
    /// </summary>
    public abstract class LimitedProdZone_WeaponBase : MyGameLogicComponent
    {
        protected IMyFunctionalBlock weapon;
        protected bool isServer;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            weapon = Entity as IMyFunctionalBlock;
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
            if (isServer && weapon != null)
            {
                weapon.IsWorkingChanged += WorkingStateChange;
            }
        }

        public override void UpdateBeforeSimulation100()
        {
            base.UpdateBeforeSimulation100();

            try
            {
                if (!isServer || weapon == null || !weapon.Enabled) return;

                if (LimitedProdZone_Manager.IsPositionInWeaponZone(weapon.GetPosition()))
                {
                    weapon.Enabled = false;
                }
            }
            catch (Exception exc)
            {
                MyLog.Default.WriteLineAndConsole($"Failed checking LimitedProdZone weapon position: {exc}");
            }
        }

        private void WorkingStateChange(IMyCubeBlock block)
        {
            try
            {
                if (!isServer || weapon == null || !weapon.Enabled) return;

                if (LimitedProdZone_Manager.IsPositionInWeaponZone(weapon.GetPosition()))
                {
                    weapon.Enabled = false;
                }
            }
            catch (Exception exc)
            {
                MyLog.Default.WriteLineAndConsole($"Failed checking LimitedProdZone weapon working state change: {exc}");
            }
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();
            if (weapon != null)
            {
                weapon.IsWorkingChanged -= WorkingStateChange;
            }
        }

        public override void Close()
        {
            base.Close();
            if (weapon != null)
            {
                weapon.IsWorkingChanged -= WorkingStateChange;
            }
        }
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_InteriorTurret), false)]
    public class LimitedProdZone_InteriorTurret : LimitedProdZone_WeaponBase { }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_LargeGatlingTurret), false)]
    public class LimitedProdZone_LargeGatlingTurret : LimitedProdZone_WeaponBase { }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_LargeMissileTurret), false)]
    public class LimitedProdZone_LargeMissileTurret : LimitedProdZone_WeaponBase { }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_SmallGatlingGun), false)]
    public class LimitedProdZone_SmallGatlingGun : LimitedProdZone_WeaponBase { }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_SmallMissileLauncher), false)]
    public class LimitedProdZone_SmallMissileLauncher : LimitedProdZone_WeaponBase { }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_SmallMissileLauncherReload), false)]
    public class LimitedProdZone_SmallMissileLauncherReload : LimitedProdZone_WeaponBase { }

    /// <summary>
    /// Enforces Zone 0 restrictions on ConveyorSorter blocks.
    /// WeaponCore weapons and ToolCore drills built as ConveyorSorter blocks are disabled in Zone 0.
    /// Standard logistics sorters, ToolCore ship welders/grinders, and the Salvage Beam Turret are whitelisted.
    /// </summary>
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_ConveyorSorter), false)]
    public class LimitedProdZone_ConveyorSorter : MyGameLogicComponent
    {
        private IMyConveyorSorter weapon;
        private bool isServer;
        private static readonly MyDefinitionId StaticWeaponDef = new MyDefinitionId(typeof(MyObjectBuilder_ConveyorSorter), "ARYXMissileBattery");
        private static readonly List<MyDefinitionId> ConveyorSorterDefs = new List<MyDefinitionId>
        {
            // Vanilla Logistics Sorters
            new MyDefinitionId(typeof(MyObjectBuilder_ConveyorSorter), "LargeBlockConveyorSorter"),
            new MyDefinitionId(typeof(MyObjectBuilder_ConveyorSorter), "MediumBlockConveyorSorter"),
            new MyDefinitionId(typeof(MyObjectBuilder_ConveyorSorter), "SmallBlockConveyorSorter"),
            new MyDefinitionId(typeof(MyObjectBuilder_ConveyorSorter), "LargeBlockConveyorSorterIndustrial"),

            // GVK ToolCore: Permitted Utility Tools & Salvage Turret in Zone 0
            // Note: ToolCore Drills (GVK_*Drill*) remain omitted so they are disabled in Zone 0 per server rules.
            new MyDefinitionId(typeof(MyObjectBuilder_ConveyorSorter), "GVK_LargeSalvageBeamTurret"),
            new MyDefinitionId(typeof(MyObjectBuilder_ConveyorSorter), "GVK_LargeShipWelder"),
            new MyDefinitionId(typeof(MyObjectBuilder_ConveyorSorter), "GVK_SmallShipWelder"),
            new MyDefinitionId(typeof(MyObjectBuilder_ConveyorSorter), "GVK_LargeShipWelderReskin"),
            new MyDefinitionId(typeof(MyObjectBuilder_ConveyorSorter), "GVK_SmallShipWelderReskin"),
            new MyDefinitionId(typeof(MyObjectBuilder_ConveyorSorter), "GVK_LargeShipGrinder"),
            new MyDefinitionId(typeof(MyObjectBuilder_ConveyorSorter), "GVK_SmallShipGrinder"),
            new MyDefinitionId(typeof(MyObjectBuilder_ConveyorSorter), "GVK_LargeShipGrinderReskin"),
            new MyDefinitionId(typeof(MyObjectBuilder_ConveyorSorter), "GVK_SmallShipGrinderReskin")
        };
        private static readonly MyStringHash DestructionHash = MyStringHash.GetOrCompute("Destruction");

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            weapon = Entity as IMyConveyorSorter;
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
            if (isServer && weapon != null)
            {
                weapon.IsWorkingChanged += WorkingStateChange;
            }
        }

        public override void UpdateBeforeSimulation100()
        {
            base.UpdateBeforeSimulation100();

            try
            {
                if (!isServer || weapon == null) return;

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
            try
            {
                if (!isServer || weapon == null || !weapon.Enabled) return;

                if (LimitedProdZone_Manager.IsPositionInWeaponZone(weapon.GetPosition()))
                {
                    if (!ConveyorSorterDefs.Contains(weapon.BlockDefinition))
                    {
                        weapon.Enabled = false;
                    }
                }
            }
            catch (Exception exc)
            {
                MyLog.Default.WriteLineAndConsole($"Failed checking LimitedProdZone conveyor sorter working state change: {exc}");
            }
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();
            if (weapon != null)
            {
                weapon.IsWorkingChanged -= WorkingStateChange;
            }
        }

        public override void Close()
        {
            base.Close();
            if (weapon != null)
            {
                weapon.IsWorkingChanged -= WorkingStateChange;
            }
        }
    }
}
