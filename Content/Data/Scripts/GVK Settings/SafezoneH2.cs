using System.Collections.Generic;
using Sandbox.Game;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;

namespace Klime.SafezoneH2
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class fov : MySessionComponentBase
    {
        private List<IMySafeZoneBlock> safedict = new List<IMySafeZoneBlock>();
        private List<IMyPlayer> allPlayer = new List<IMyPlayer>();
        List<IMySlimBlock> allb = new List<IMySlimBlock>();
        private int _timer = 0;

        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            base.Init(sessionComponent);
            MyVisualScriptLogicProvider.BlockBuilt += BlockBuilt;
        }

        private void BlockBuilt(string typeid, string subtypeid, string gridname, long blockid)
        {
            if (subtypeid.Contains("SafeZone"))
            {
                IMySafeZoneBlock testSafe = MyAPIGateway.Entities.GetEntityById(blockid) as IMySafeZoneBlock;
                if (testSafe != null && !safedict.Contains(testSafe))
                {
                    safedict.Add(testSafe);
                }
            }
        }

        public override void UpdateBeforeSimulation()
        {
            _timer += 1;
            if (MyAPIGateway.Session.IsServer)
            {
                if (_timer == 1)
                {
                    HashSet<IMyEntity> allents = new HashSet<IMyEntity>();
                    MyAPIGateway.Entities.GetEntities(allents);
                    foreach (var ent in allents)
                    {
                        IMyCubeGrid cubeg = ent as IMyCubeGrid;
                        if (cubeg != null)
                        {
                            allb.Clear();
                            cubeg.GetBlocks(allb);
                            foreach (var block in allb)
                            {
                                if (block.FatBlock != null)
                                {
                                    IMySafeZoneBlock testSafe = block.FatBlock as IMySafeZoneBlock;
                                    if (testSafe != null && !safedict.Contains(testSafe))
                                    {
                                        safedict.Add(testSafe);
                                    }
                                }
                                
                            }
                        }
                    }
                }

                if (_timer % 30 == 0)
                {
                    allPlayer.Clear();
                    MyAPIGateway.Multiplayer.Players.GetPlayers(allPlayer);
                    for (int i = 0; i < allPlayer.Count; i++)
                    {
                        for (int j = 0; j < safedict.Count; j++)
                        {
                            if (MyAPIGateway.Entities.EntityExists(safedict[j].EntityId))
                            {
                                if (safedict[j].IsSafeZoneEnabled())
                                {
                                    if (allPlayer[i].Character != null && (allPlayer[i].Character.WorldMatrix.Translation - safedict[j].WorldMatrix.Translation).Length() <= safedict[j].GetValueFloat("SafeZoneSlider"))
                                    {
                                        MyVisualScriptLogicProvider.SetPlayersHydrogenLevel(allPlayer[i].IdentityId, 1f);
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        protected override void UnloadData()
        {
            MyVisualScriptLogicProvider.BlockBuilt -= BlockBuilt;
            allb = null;
            allPlayer = null;
            safedict = null;
        }
    }
}