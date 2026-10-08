using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.ModAPI;

// ==================================================================================================
// Multiplayer Prediction Switcher
//
// Original mod: "MultiplayerPredictionSwitcher (Interpolated serverside grid control)"
// Original Author: Invalid (Steam Workshop ID: 3032417765, SteamID: 76561198071098415)
// Workshop URL: https://steamcommunity.com/sharedfiles/filedetails/?id=3032417765
//
// Integrated into GVK_Settings with performance optimizations:
// - Event-driven grid tracking via OnEntityAdd / OnEntityRemove / OnClose (zero per-tick allocations)
// - NoUpdate session component (no simulation tick overhead)
// - Runs client-side only (ignored on dedicated server)
// - Supports in-game toggle via chat command: /toggleprediction
// ==================================================================================================

namespace GVK.PredictionSwitcher
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class PredictionSwitcher : MySessionComponentBase
    {
        private bool _isPredictionDisabled = true;
        private readonly HashSet<MyCubeGrid> _trackedGrids = new HashSet<MyCubeGrid>();
        private bool _isInitialized;

        public override void LoadData()
        {
            if (MyAPIGateway.Utilities == null || MyAPIGateway.Utilities.IsDedicated)
                return;

            MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;
            MyAPIGateway.Entities.OnEntityAdd += OnEntityAdd;
            MyAPIGateway.Entities.OnEntityRemove += OnEntityRemove;
        }

        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            base.Init(sessionComponent);

            if (MyAPIGateway.Utilities == null || MyAPIGateway.Utilities.IsDedicated || _isInitialized)
                return;

            _isInitialized = true;

            var existingEntities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(existingEntities, null);

            foreach (var entity in existingEntities)
            {
                var grid = entity as MyCubeGrid;
                if (grid != null)
                {
                    TrackGrid(grid);
                }
            }
            existingEntities.Clear();
        }

        private void TrackGrid(MyCubeGrid grid)
        {
            if (grid == null)
                return;

            if (_trackedGrids.Add(grid))
            {
                grid.ForceDisablePrediction = _isPredictionDisabled;
                grid.OnClose += OnGridClose;
            }
        }

        private void UntrackGrid(MyCubeGrid grid)
        {
            if (grid == null)
                return;

            if (_trackedGrids.Remove(grid))
            {
                grid.OnClose -= OnGridClose;
            }
        }

        private void OnEntityAdd(IMyEntity entity)
        {
            var grid = entity as MyCubeGrid;
            if (grid != null)
            {
                TrackGrid(grid);
            }
        }

        private void OnEntityRemove(IMyEntity entity)
        {
            var grid = entity as MyCubeGrid;
            if (grid != null)
            {
                UntrackGrid(grid);
            }
        }

        private void OnGridClose(IMyEntity entity)
        {
            var grid = entity as MyCubeGrid;
            if (grid != null)
            {
                UntrackGrid(grid);
            }
        }

        private void OnMessageEntered(string messageText, ref bool sendToOthers)
        {
            if (string.IsNullOrEmpty(messageText))
                return;

            if (messageText.Trim().Equals("/toggleprediction", StringComparison.OrdinalIgnoreCase))
            {
                sendToOthers = false;
                _isPredictionDisabled = !_isPredictionDisabled;

                foreach (var grid in _trackedGrids)
                {
                    if (grid != null && !grid.MarkedForClose && !grid.Closed)
                    {
                        grid.ForceDisablePrediction = _isPredictionDisabled;
                    }
                }

                var msg = "ForceDisablePrediction: " + _isPredictionDisabled;
                var font = _isPredictionDisabled ? MyFontEnum.Green : MyFontEnum.Red;
                MyAPIGateway.Utilities.ShowNotification(msg, 2000, font);
            }
        }

        protected override void UnloadData()
        {
            if (MyAPIGateway.Utilities != null && !MyAPIGateway.Utilities.IsDedicated)
            {
                MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;
            }

            if (MyAPIGateway.Entities != null)
            {
                MyAPIGateway.Entities.OnEntityAdd -= OnEntityAdd;
                MyAPIGateway.Entities.OnEntityRemove -= OnEntityRemove;
            }

            foreach (var grid in _trackedGrids)
            {
                if (grid != null)
                {
                    grid.OnClose -= OnGridClose;
                }
            }
            _trackedGrids.Clear();

            base.UnloadData();
        }
    }
}
