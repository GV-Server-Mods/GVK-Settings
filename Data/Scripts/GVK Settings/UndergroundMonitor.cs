using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Underground_Monitor
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class Session : MySessionComponentBase
    {
        private int UPDATE_RATE = 10;
        private int PROCESS_AMOUNT = 20;
        private int FLAGGED_TIME = 5;
        private int METERS_BELOW_SURFACE = 50;

        private bool isServer;
        private int ticks;
        private IMyFaction npcFaction;
        private long npcFactionId;
        private MyConcurrentQueue<IMyCubeGrid> gridList = new MyConcurrentQueue<IMyCubeGrid>();
        private ConcurrentDictionary<MyCubeBlock, int> blockCache = new ConcurrentDictionary<MyCubeBlock, int>();

        public override void LoadData()
        {
            isServer = MyAPIGateway.Session.IsServer;

            if (isServer)
            {
                MyAPIGateway.Entities.OnEntityAdd += EntityAdd;
                MyAPIGateway.Entities.OnEntityRemove += EntityRemoved;
                
            }
        }

        public override void BeforeStart()
        {
            if (isServer)
            {
                // Changed from COALITION to GAALSIEN so players can still clean it up or kill it
				npcFaction = MyAPIGateway.Session.Factions.TryGetFactionByTag("GAALSIEN");
                if (npcFaction != null)
                    npcFactionId = npcFaction.FactionId;
            }
        }

        public override void UpdateBeforeSimulation()
        {
            if (!isServer) return;

            ticks++;
            RunProcess();
            CheckFlagged();

            if (ticks >= 3600)
                ticks = 0;
        }

        private void RunProcess()
        {
            if (ticks % UPDATE_RATE != 0) return;
            MyAPIGateway.Parallel.StartBackground(() =>
            {
                if (npcFaction == null) return;
                for (int i = 0; i < PROCESS_AMOUNT; i++)
                {
                    IMyCubeGrid grid;
                    if (!gridList.TryDequeue(out grid))
                        continue;

                    if (!CheckGrid(grid))
                        continue;

                    gridList.Enqueue(grid);
                }
            });
        }

        private void CheckFlagged()
        {
            if (ticks % 60 != 0) return;
            if (npcFaction == null) return;
            List<MyCubeBlock> temp = new List<MyCubeBlock>();
            foreach (var item in blockCache.Keys)
            {
                if (item == null || item.MarkedForClose)
                {
                    temp.Add(item);
                    continue;
                }

                blockCache[item]++;
                if (blockCache[item] / 60 >= FLAGGED_TIME)
                {
                    MyPlanet planet = MyGamePruningStructure.GetClosestPlanet(item.PositionComp.GetPosition());
                    if (planet == null)
                    {
                        temp.Add(item);
                        continue;
                    }

                    IMyEntity planetEntity = planet as IMyEntity;
                    if (planetEntity == null)
                    {
                        temp.Add(item);
                        continue;
                    }

                    bool inGravity = planetEntity.Components.Get<MyGravityProviderComponent>().IsPositionInRange(item.PositionComp.GetPosition());
                    if (!inGravity)
                    {
                        temp.Add(item);
                        continue;
                    }

                    if (!IsUnderground(item, planetEntity, planet))
                    {
                        temp.Add(item);
                        continue;
                    }

                    item.ChangeBlockOwnerRequest(0, MyOwnershipShareModeEnum.Faction);
                    item.ChangeBlockOwnerRequest(npcFaction.FounderId, MyOwnershipShareModeEnum.Faction);
                    temp.Add(item);
                }
            }

            foreach (var item in temp)
            {
                int value;
                blockCache.TryRemove(item, out value);
            }
        }

        private bool CheckGrid(IMyCubeGrid grid)
        {
            if (grid == null || grid.MarkedForClose) return false;
            MyCubeGrid cubeGrid = grid as MyCubeGrid;
            if (cubeGrid == null) return false;

            IMyFaction gridFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(cubeGrid.BigOwners.FirstOrDefault());
            if (gridFaction != null)
            {
                if (gridFaction.FactionId == npcFactionId || gridFaction.Tag.Length > 3)
                    return true;
            }

            if (!grid.IsStatic) return true;

            MyPlanet planet = MyGamePruningStructure.GetClosestPlanet(grid.GetPosition());
            if (planet == null) return true;

            IMyEntity planetEntity = planet as IMyEntity;
            if (planetEntity == null) return true;

            bool inGravity = planetEntity.Components.Get<MyGravityProviderComponent>().IsPositionInRange(grid.GetPosition());
            if (!inGravity) return true;

            var blocks = cubeGrid.GetFatBlocks();
            foreach(var block in blocks)
            {
                if (block.OwnerId == npcFactionId) continue;
                IMyFaction blockFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(block.OwnerId);
                if (blockFaction != null)
                    if (blockFaction.Tag.Length > 3) continue;

                if (!IsUnderground(block, planetEntity, planet))
                    continue;

                IMyCubeBlock cubeBlock = block as IMyCubeBlock;
                if (cubeBlock == null) continue;

                IMyFunctionalBlock myFunctionalBlock = block as IMyFunctionalBlock;
                if (myFunctionalBlock != null)
                {
                    MyAPIGateway.Utilities.InvokeOnGameThread(() => myFunctionalBlock.Enabled = false);
                    if (!blockCache.ContainsKey(block) && myFunctionalBlock.IsFunctional)
                    {
                        blockCache.TryAdd(block, 0);
                        SendChatAlert(block);
                    }

                    continue;
                }

                IMyTerminalBlock tBlock = block as IMyTerminalBlock;
                if (tBlock != null)
                {
                    if (!blockCache.ContainsKey(block) && tBlock.IsFunctional)
                    {
                        blockCache.TryAdd(block, 0);
                        SendChatAlert(block);
                    }

                    continue;
                }
            }

            return true;
        }

        private bool IsUnderground(MyCubeBlock block, IMyEntity planetEntity, MyPlanet planet)
        {
            if (block as IMyShipDrill != null) return false;
            Vector3D pos = block.PositionComp.GetPosition();
            var powerBlock = block as IMyPowerProducer;
            bool isWindOrSolar = false;
            if (powerBlock != null)
                isWindOrSolar = powerBlock.BlockDefinition.SubtypeName.Contains("Solar") || powerBlock.BlockDefinition.SubtypeName.Contains("Wind");
 
            Vector3D objectPlanetOffset = pos - planetEntity.GetPosition();
            double objectDistSq = objectPlanetOffset.LengthSquared();

            Vector3D objectPlanetOffsetNormal = objectPlanetOffset;
            objectPlanetOffsetNormal.Normalize();

            float distance = isWindOrSolar ? 5 : METERS_BELOW_SURFACE;

            Vector3D undergroundThreshold = planet.GetClosestSurfacePointGlobal(pos) - planetEntity.GetPosition() - objectPlanetOffsetNormal * distance;
            double undergroundThresholdDistSq = undergroundThreshold.LengthSquared();

            return objectDistSq < undergroundThresholdDistSq;
        }

        private void SendChatAlert(MyCubeBlock block)
        {
            IMyCubeBlock cubeBlock = block as IMyCubeBlock;
            if (cubeBlock == null) return;
			// Removed no owner message so it doesn't spam everyone
            /*if (block.OwnerId == 0)
            {
                MyVisualScriptLogicProvider.SendChatMessageColored($"WARNING!! Block '{block?.BlockDefinition.Id.SubtypeName}' on grid '{cubeBlock?.CubeGrid.CustomName}' has no owner and is flagged as undergound and this is prohibited. {FLAGGED_TIME} mins to fix this or it will be changed to an NPC owner.", Color.Red, "[Server]", 0, "Red");
                return;
            }*/

            if (GetPlayerFromId(block.OwnerId) != null)
            {
                MyVisualScriptLogicProvider.SendChatMessageColored($"WARNING!! Block '{block?.BlockDefinition.Id.SubtypeName}' on grid '{cubeBlock?.CubeGrid.CustomName}' is flagged as undergound and this is prohibited. You have {FLAGGED_TIME} mins to fix this or it will be changed to an NPC owner.", Color.Red, "[Server]", block.OwnerId, "Red");
                return;
            }

            IMyFaction faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(block.OwnerId);
            if (faction == null) return;

            var members = faction.Members;
            foreach(var member in members)
            {
                if (GetPlayerFromId(member.Key) == null) continue;
                MyVisualScriptLogicProvider.SendChatMessageColored($"WARNING!! Block '{block?.BlockDefinition.Id.SubtypeName}' on grid '{cubeBlock?.CubeGrid.CustomName}' is flagged as undergound and this is prohibited. You have {FLAGGED_TIME} mins to fix this or it will be changed to an NPC owner.", Color.Red, "[Server]", member.Key, "Red");
                continue;
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
            if (grid.Physics == null) return;

            gridList.Enqueue(grid);
        }

        public void EntityRemoved(IMyEntity entity)
        {
            IMyCubeGrid grid = entity as IMyCubeGrid;
            if (grid == null) return;

            //gridList.Remove(grid);
        }

        protected override void UnloadData()
        {
            if (isServer)
            {
                MyAPIGateway.Entities.OnEntityAdd -= EntityAdd;
                MyAPIGateway.Entities.OnEntityRemove -= EntityRemoved;
            }
        }
    }
}
