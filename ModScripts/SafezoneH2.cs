using System.Collections.Generic;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

// =========================================================================
// GV: Deserts of Kharak (GVK) Server Settings & Mechanics
// Script: SafezoneH2.cs
// Original Author: Klime (https://steamcommunity.com/sharedfiles/filedetails/?id=1871733117)
// Description: Automatically provides infinite jetpack hydrogen to players inside
// their own faction-owned safezone bubbles.
// =========================================================================

namespace GVK.SafeZone
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class SafezoneH2Session : MySessionComponentBase
    {
        private readonly HashSet<MySafeZone> activeSafeZones = new HashSet<MySafeZone>();
        private readonly List<IMyPlayer> players = new List<IMyPlayer>();
        private int timer = 0;
        private bool isServer = false;

        public override void LoadData()
        {
            isServer = MyAPIGateway.Session.IsServer;
            if (!isServer)
            {
                SetUpdateOrder(MyUpdateOrder.NoUpdate);
                return;
            }

            MyAPIGateway.Entities.OnEntityAdd += OnEntityAdd;
            MyAPIGateway.Entities.OnEntityRemove += OnEntityRemove;
        }

        public override void BeforeStart()
        {
            if (!isServer) return;

            // Register existing safe zones at session start
            HashSet<IMyEntity> entities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entities, e => e is MySafeZone);
            foreach (var entity in entities)
            {
                OnEntityAdd(entity);
            }
        }

        private void OnEntityAdd(IMyEntity entity)
        {
            var safeZone = entity as MySafeZone;
            if (safeZone != null && !safeZone.MarkedForClose && !safeZone.Closed)
            {
                activeSafeZones.Add(safeZone);
            }
        }

        private void OnEntityRemove(IMyEntity entity)
        {
            var safeZone = entity as MySafeZone;
            if (safeZone != null)
            {
                activeSafeZones.Remove(safeZone);
            }
        }

        public override void UpdateBeforeSimulation()
        {
            if (!isServer) return;

            timer++;
            if (timer % 30 != 0) return;

            if (activeSafeZones.Count == 0) return;

            players.Clear();
            MyAPIGateway.Multiplayer.Players.GetPlayers(players);

            foreach (var player in players)
            {
                var character = player.Character;
                if (character == null || character.MarkedForClose || character.Closed)
                    continue;

                Vector3D playerPos = character.PositionComp.GetPosition();

                foreach (var safeZone in activeSafeZones)
                {
                    if (safeZone == null || safeZone.MarkedForClose || safeZone.Closed)
                        continue;

                    if (!safeZone.Enabled)
                        continue;

                    // Native check handling both Sphere and Box safe zone shapes
                    if (safeZone.Contains(playerPos))
                    {
                        MyVisualScriptLogicProvider.SetPlayersHydrogenLevel(player.IdentityId, 1f);
                        break;
                    }
                }
            }
        }

        protected override void UnloadData()
        {
            if (isServer)
            {
                MyAPIGateway.Entities.OnEntityAdd -= OnEntityAdd;
                MyAPIGateway.Entities.OnEntityRemove -= OnEntityRemove;
            }

            activeSafeZones.Clear();
            players.Clear();
        }
    }
}