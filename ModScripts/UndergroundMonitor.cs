using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

// =========================================================================
// GV: Deserts of Kharak (GVK) Server Settings & Mechanics
// Script: UndergroundMonitor.cs
// Original Author: Kamikaze (https://steamcommunity.com/sharedfiles/filedetails/?id=2713098288)
// Adapted for GVK: Mike Dude
// Description: Monitors grid subterranean depth against voxel surfaces, enforcing
// server limits against illegal underground bases with automated SPRT transfer.
// =========================================================================

namespace GVK.UndergroundMonitor
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class Session : MySessionComponentBase
    {
        private const int UPDATE_INTERVAL_TICKS = 30; // Check a batch twice per second
        private const int PROCESS_GRIDS_PER_BATCH = 10;
        private const int FLAGGED_TIME_MINUTES = 5;

        // Depth limits relative to original procedural heightmap elevation
        private const double DEPTH_ATMOSPHERE_POWER = 5.0; // Wind & Solar (disabled below 5m)
        private const double DEPTH_GENERAL_BLOCKS = 25.0;   // All other functional/terminal blocks (disabled below 25m)

        private bool isServer;
        private int ticks;
        private IMyFaction npcFaction;
        private long npcFactionId;

        private readonly Queue<IMyCubeGrid> gridQueue = new Queue<IMyCubeGrid>();
        private readonly HashSet<long> registeredGridEntityIds = new HashSet<long>();
        private readonly ConcurrentDictionary<MyCubeBlock, int> blockCache = new ConcurrentDictionary<MyCubeBlock, int>();
        private readonly Dictionary<long, int> gridAlertCooldowns = new Dictionary<long, int>();

        public override void LoadData()
        {
            isServer = MyAPIGateway.Session.IsServer;
            if (!isServer) return;

            MyAPIGateway.Entities.OnEntityAdd += EntityAdd;
            MyAPIGateway.Entities.OnEntityRemove += EntityRemoved;
        }

        public override void BeforeStart()
        {
            if (!isServer) return;

            FindNpcFaction();

            // Populate all existing grids loaded with world save
            HashSet<IMyEntity> entities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entities, e => e is IMyCubeGrid);
            foreach (var entity in entities)
            {
                EntityAdd(entity);
            }
        }

        private void FindNpcFaction()
        {
            // 1. Primary: GAALSIEN
            npcFaction = MyAPIGateway.Session.Factions.TryGetFactionByTag("GAALSIEN");
            if (npcFaction != null)
            {
                npcFactionId = npcFaction.FactionId;
                return;
            }

            // 2. Failsafe: SPRT (Default Keen Space Pirates)
            npcFaction = MyAPIGateway.Session.Factions.TryGetFactionByTag("SPRT");
            if (npcFaction != null)
            {
                npcFactionId = npcFaction.FactionId;
                return;
            }

            // 3. Fallback: First available NPC faction
            npcFaction = MyAPIGateway.Session.Factions.Factions.Values.FirstOrDefault(f => f.IsEveryoneNpc() || f.Tag.Length > 3);
            if (npcFaction != null)
            {
                npcFactionId = npcFaction.FactionId;
            }
        }

        public override void UpdateBeforeSimulation()
        {
            if (!isServer) return;

            ticks++;

            if (ticks % UPDATE_INTERVAL_TICKS == 0)
            {
                ProcessGridBatch();
            }

            if (ticks % 60 == 0)
            {
                CheckFlaggedBlocks();
                UpdateAlertCooldowns();
            }
        }

        private void ProcessGridBatch()
        {
            if (gridQueue.Count == 0) return;

            int count = Math.Min(PROCESS_GRIDS_PER_BATCH, gridQueue.Count);
            for (int i = 0; i < count; i++)
            {
                IMyCubeGrid grid = gridQueue.Dequeue();
                if (grid == null || grid.MarkedForClose || grid.Closed)
                {
                    if (grid != null) registeredGridEntityIds.Remove(grid.EntityId);
                    continue;
                }

                if (CheckGrid(grid))
                {
                    gridQueue.Enqueue(grid);
                }
                else
                {
                    registeredGridEntityIds.Remove(grid.EntityId);
                }
            }
        }

        private bool CheckGrid(IMyCubeGrid grid)
        {
            if (grid == null || grid.MarkedForClose || grid.Closed) return false;
            var cubeGrid = grid as MyCubeGrid;
            if (cubeGrid == null) return false;

            // Only monitor static grids anchored into the ground
            if (!grid.IsStatic) return true;

            // Ignore NPC/Admin grids
            long ownerId = cubeGrid.BigOwners.FirstOrDefault();
            if (ownerId != 0)
            {
                if (npcFaction != null && ownerId == npcFactionId) return true;
                IMyFaction gridFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(ownerId);
                if (gridFaction != null && (gridFaction.FactionId == npcFactionId || gridFaction.Tag.Length > 3))
                    return true;
            }

            MyPlanet planet = MyGamePruningStructure.GetClosestPlanet(grid.GetPosition());
            if (planet == null) return true;

            Vector3D planetCenter = planet.PositionComp.GetPosition();
            var gravityComp = planet.Components.Get<MyGravityProviderComponent>();
            if (gravityComp == null || !gravityComp.IsPositionInRange(grid.GetPosition()))
                return true;

            var blocks = cubeGrid.GetFatBlocks();
            int newFlaggedOnGrid = 0;
            string sampleBlockName = null;
            double sampleDepth = 0;
            bool sampleIsWindOrSolar = false;

            foreach (var block in blocks)
            {
                if (block == null || block.MarkedForClose || block.Closed) continue;
                if (block.OwnerId == npcFactionId) continue;

                // Exempt ship drills and drill-rig mechanics (pistons/rotors) from being shut down
                if (IsExemptMiningBlock(block))
                    continue;

                // Check allowable depth for this block type
                double maxAllowedDepth = GetMaxAllowedDepth(block);
                if (maxAllowedDepth < 0)
                    continue; // Allowed at any depth (e.g. non-terminal structural blocks)

                Vector3D blockPos = block.PositionComp.GetPosition();
                Vector3D surfacePoint = planet.GetClosestSurfacePointGlobal(ref blockPos);
                double surfaceDist = Vector3D.Distance(surfacePoint, planetCenter);
                double blockDist = Vector3D.Distance(blockPos, planetCenter);
                double depth = surfaceDist - blockDist;

                if (depth > maxAllowedDepth)
                {
                    var functionalBlock = block as IMyFunctionalBlock;
                    if (functionalBlock != null && functionalBlock.Enabled)
                    {
                        functionalBlock.Enabled = false;
                    }

                    if (!blockCache.ContainsKey(block))
                    {
                        blockCache.TryAdd(block, 0);
                        newFlaggedOnGrid++;
                        sampleBlockName = block.BlockDefinition.DisplayNameText ?? block.BlockDefinition.Id.SubtypeName;
                        sampleDepth = depth;
                        sampleIsWindOrSolar = (maxAllowedDepth == DEPTH_ATMOSPHERE_POWER);
                    }
                }
            }

            if (newFlaggedOnGrid > 0)
            {
                SendGridChatAlert(grid, ownerId, sampleBlockName, sampleDepth, newFlaggedOnGrid, sampleIsWindOrSolar);
            }

            return true;
        }

        private bool IsExemptMiningBlock(MyCubeBlock block)
        {
            // Drills and mechanical drill extensions (pistons, rotors, hinges) are exempt so mining rigs don't get shut down
            if (block is IMyShipDrill) return true;
            if (block is IMyPistonBase) return true;
            if (block is IMyMotorStator) return true;
            return false;
        }

        private double GetMaxAllowedDepth(MyCubeBlock block)
        {
            // 1. Wind Turbines & Solar Panels: disabled below 5m
            if (block is IMyWindTurbine || block is IMySolarPanel ||
                block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_WindTurbine) ||
                block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_SolarPanel))
            {
                return DEPTH_ATMOSPHERE_POWER;
            }

            // 2. All other functional or terminal blocks (production, power, containers, etc.): disabled below 25m
            if (block is IMyFunctionalBlock || block is IMyTerminalBlock)
            {
                return DEPTH_GENERAL_BLOCKS;
            }

            // 3. Non-terminal structural blocks (armor blocks, plain conveyor tubes): No restriction
            return -1.0;
        }

        private void CheckFlaggedBlocks()
        {
            if (blockCache.IsEmpty) return;

            List<MyCubeBlock> toRemove = new List<MyCubeBlock>();

            foreach (var kvp in blockCache)
            {
                MyCubeBlock block = kvp.Key;
                if (block == null || block.MarkedForClose || block.Closed)
                {
                    toRemove.Add(block);
                    continue;
                }

                int elapsedSeconds = kvp.Value + 1;
                blockCache[block] = elapsedSeconds;

                if (elapsedSeconds >= FLAGGED_TIME_MINUTES * 60)
                {
                    // Re-verify before transferring ownership
                    MyPlanet planet = MyGamePruningStructure.GetClosestPlanet(block.PositionComp.GetPosition());
                    if (planet != null)
                    {
                        Vector3D planetCenter = planet.PositionComp.GetPosition();
                        Vector3D blockPos = block.PositionComp.GetPosition();
                        Vector3D surfacePoint = planet.GetClosestSurfacePointGlobal(ref blockPos);
                        double surfaceDist = Vector3D.Distance(surfacePoint, planetCenter);
                        double blockDist = Vector3D.Distance(blockPos, planetCenter);
                        double depth = surfaceDist - blockDist;

                        double maxDepth = GetMaxAllowedDepth(block);
                        if (maxDepth >= 0 && depth > maxDepth)
                        {
                            if (npcFaction == null)
                                FindNpcFaction();

                            long targetOwner = (npcFaction != null && npcFaction.FounderId != 0) ? npcFaction.FounderId : 0;
                            block.ChangeOwner(targetOwner, MyOwnershipShareModeEnum.Faction);
                        }
                    }

                    toRemove.Add(block);
                }
            }

            foreach (var block in toRemove)
            {
                int val;
                blockCache.TryRemove(block, out val);
            }
        }

        private void SendGridChatAlert(IMyCubeGrid grid, long ownerId, string blockName, double depth, int count, bool isWindOrSolar)
        {
            if (gridAlertCooldowns.ContainsKey(grid.EntityId))
                return;

            gridAlertCooldowns[grid.EntityId] = 60; // 60s cooldown per grid

            string gridName = string.IsNullOrEmpty(grid.CustomName) ? "Static Grid" : grid.CustomName;
            string countText = count > 1 ? $" ({count} prohibited blocks)" : "";
            string msg;

            if (isWindOrSolar)
            {
                msg = $"[Server] ⚠️ Prohibited underground power '{blockName}'{countText} on '{gridName}' ({depth:0}m deep). Wind turbines & solar panels must be within 5m of the surface. Disabled.";
            }
            else
            {
                msg = $"[Server] ⚠️ Prohibited underground block '{blockName}'{countText} on '{gridName}' ({depth:0}m deep). Base blocks must be within 25m of surface due to voxel resets. Move above ground within {FLAGGED_TIME_MINUTES} mins.";
            }

            if (ownerId != 0)
            {
                IMyPlayer player = GetPlayerFromId(ownerId);
                if (player != null)
                {
                    MyVisualScriptLogicProvider.SendChatMessageColored(msg, Color.Red, "[Server]", ownerId, "Red");
                    return;
                }

                IMyFaction faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(ownerId);
                if (faction != null)
                {
                    foreach (var member in faction.Members)
                    {
                        if (GetPlayerFromId(member.Key) != null)
                        {
                            MyVisualScriptLogicProvider.SendChatMessageColored(msg, Color.Red, "[Server]", member.Key, "Red");
                        }
                    }
                    return;
                }
            }
        }

        private void UpdateAlertCooldowns()
        {
            if (gridAlertCooldowns.Count == 0) return;

            List<long> expired = new List<long>();
            var keys = gridAlertCooldowns.Keys.ToList();
            foreach (var key in keys)
            {
                gridAlertCooldowns[key]--;
                if (gridAlertCooldowns[key] <= 0)
                {
                    expired.Add(key);
                }
            }

            foreach (var key in expired)
            {
                gridAlertCooldowns.Remove(key);
            }
        }

        private IMyPlayer GetPlayerFromId(long playerId)
        {
            List<IMyPlayer> players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);
            foreach (var player in players)
            {
                if (player.IdentityId == playerId)
                    return player;
            }
            return null;
        }

        public void EntityAdd(IMyEntity entity)
        {
            IMyCubeGrid grid = entity as IMyCubeGrid;
            if (grid == null) return;
            if (registeredGridEntityIds.Contains(grid.EntityId)) return;

            registeredGridEntityIds.Add(grid.EntityId);
            gridQueue.Enqueue(grid);
        }

        public void EntityRemoved(IMyEntity entity)
        {
            IMyCubeGrid grid = entity as IMyCubeGrid;
            if (grid == null) return;

            registeredGridEntityIds.Remove(grid.EntityId);
            gridAlertCooldowns.Remove(grid.EntityId);
        }

        protected override void UnloadData()
        {
            if (isServer)
            {
                MyAPIGateway.Entities.OnEntityAdd -= EntityAdd;
                MyAPIGateway.Entities.OnEntityRemove -= EntityRemoved;
            }

            gridQueue.Clear();
            registeredGridEntityIds.Clear();
            blockCache.Clear();
            gridAlertCooldowns.Clear();
        }
    }
}

