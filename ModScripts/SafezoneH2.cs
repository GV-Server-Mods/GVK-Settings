using System.Collections.Generic;
using ObjectBuilders.SafeZone;
using Sandbox.Game;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace Klime.SafezoneH2
{
    // Server only: refills the hydrogen of every player standing inside an enabled safezone.
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class fov : MySessionComponentBase
    {
        // Kept current by SafezoneH2Block, so zones that arrive by paste, hangar or MES spawn count too.
        public static readonly HashSet<IMySafeZoneBlock> Zones = new HashSet<IMySafeZoneBlock>();

        private readonly List<IMyPlayer> _players = new List<IMyPlayer>();
        private ITerminalProperty<float> _radius;
        private int _timer;

        public override void UpdateBeforeSimulation()
        {
            if (++_timer % 30 != 0 || Zones.Count == 0 || !MyAPIGateway.Session.IsServer)
                return;

            _players.Clear();
            MyAPIGateway.Multiplayer.Players.GetPlayers(_players);
            foreach (var player in _players)
            {
                var character = player.Character;
                if (character == null)
                    continue;

                var position = character.WorldMatrix.Translation;
                foreach (var zone in Zones)
                {
                    if (!zone.IsSafeZoneEnabled())
                        continue;

                    var radiusProp = _radius ?? (_radius = zone.GetProperty("SafeZoneSlider") as ITerminalProperty<float>);
                    if (radiusProp == null)
                        continue;

                    var radius = radiusProp.GetValue(zone);
                    if (Vector3D.DistanceSquared(position, zone.WorldMatrix.Translation) <= radius * radius)
                    {
                        MyVisualScriptLogicProvider.SetPlayersHydrogenLevel(player.IdentityId, 1f);
                        break;
                    }
                }
            }
        }

        protected override void UnloadData()
        {
            Zones.Clear();
        }
    }

    // Registers each safezone block while it is in the world. Other GameLogic on this type
    // (SafeZoneAnimated, KOTHNoSafezone) is combined with this one by the game.
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_SafeZoneBlock), false)]
    public class SafezoneH2Block : MyGameLogicComponent
    {
        private IMySafeZoneBlock _zone;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            _zone = Entity as IMySafeZoneBlock;
            if (_zone != null)
                NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            if (_zone != null)
                NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override void UpdateOnceBeforeFrame()
        {
            if (_zone != null && !_zone.MarkedForClose && MyAPIGateway.Multiplayer.IsServer)
                fov.Zones.Add(_zone);
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();
            if (_zone != null)
                fov.Zones.Remove(_zone);
        }

        public override void Close()
        {
            if (_zone != null)
                fov.Zones.Remove(_zone);
        }
    }
}
