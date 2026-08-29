using System;
using System.Linq;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

// =========================================================================
// GV: Deserts of Kharak (GVK) Server Settings & Mechanics
// Script: GVK_NoPVPZone.cs
// Original Author: Kamikaze
// Adaptation & Zone 0 Logic: Mike Dude
// Damage Filtering & Grinder Enhancements: Merii
// Description: Enforces strict PvE within 20km of Crossroads Tower (Zone 0).
// Intercepts damage events to block cross-faction grinding and unauthorized grid attacks.
// =========================================================================

namespace GVK.NoPVP
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class GVK_NoPVPZone : MySessionComponentBase
    {
        private const string StarterGrinderSubtype = "AngleGrinder";
        private const float BasicGrinderMultiplier = 1.2f;
        private static readonly MyStringHash GrindHash = MyStringHash.GetOrCompute("Grind");

        // Zone 0 No-PVP / No-Damage area (squared radius for performance: 20,000^2 = 400,000,000)
        public static readonly Vector3D Zone0CenterCoord = new Vector3D(62495, 28019, 37195); //[Coordinates:{X:62495.55 Y:28019.04 Z:37195.71}]
        public const double Zone0Radius = 20000d;
        public const double Zone0RadiusSquared = 400000000d; // 20,000^2

        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            base.Init(sessionComponent);

            if (MyAPIGateway.Session.IsServer)
            {
                MyAPIGateway.Session.DamageSystem.RegisterBeforeDamageHandler(0, OnBeforeDamage);
            }
        }

        private void OnBeforeDamage(object target, ref MyDamageInformation info)
        {
            try
            {
                // 1. Grinding Damage (Hand Grinder or Ship Grinder)
                if (info.Type.Equals(GrindHash))
                {
                    // If target is a character in Zone 0, block grinding damage
                    var characterTarget = target as IMyCharacter;
                    if (characterTarget != null)
                    {
                        if (IsInNoPvpZone(characterTarget.GetPosition()))
                        {
                            info.Amount = 0f;
                        }
                        return;
                    }

                    var slimBlock = target as IMySlimBlock;
                    if (slimBlock == null) return;

                    IMyEntity attackerEntity = MyAPIGateway.Entities.GetEntityById(info.AttackerId);
                    if (attackerEntity == null) return;

                    var handGrinder = attackerEntity as IMyAngleGrinder;
                    var shipGrinder = attackerEntity as IMyShipGrinder ?? attackerEntity as IMyCubeBlock;

                    // --- CASE A: Inside Zone 0 (No Hacking / No Grinding other factions' grids) ---
                    if (IsInNoPvpZone(slimBlock.CubeGrid.GetPosition()))
                    {
                        long gridOwner = slimBlock.CubeGrid.BigOwners.FirstOrDefault();

                        if (handGrinder != null)
                        {
                            long grinderOwner = handGrinder.OwnerIdentityId;

                            if (gridOwner != 0 && gridOwner != grinderOwner)
                            {
                                var targetFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(gridOwner);
                                var grinderFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(grinderOwner);

                                if (targetFaction == null || grinderFaction == null || targetFaction.FactionId != grinderFaction.FactionId)
                                {
                                    info.Amount = 0f;
                                    return;
                                }
                            }
                        }
                        else if (shipGrinder != null)
                        {
                            long grinderOwner = shipGrinder.OwnerId;

                            if (gridOwner != 0 && gridOwner != grinderOwner)
                            {
                                var targetFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(gridOwner);
                                var grinderFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(grinderOwner);

                                if (targetFaction == null || grinderFaction == null || targetFaction.FactionId != grinderFaction.FactionId)
                                {
                                    info.Amount = 0f;
                                    return;
                                }
                            }
                        }
                    }

                    // --- CASE B: Starter / Basic Hand Grinder Protection (Worldwide) ---
                    if (handGrinder != null && handGrinder.DefinitionId.SubtypeName == StarterGrinderSubtype)
                    {
                        long grinderOwner = handGrinder.OwnerIdentityId;
                        long blockOwner = slimBlock.OwnerId;
                        long blockBuiltBy = slimBlock.BuiltBy;

                        if (blockOwner == 0)
                        {
                            if (blockBuiltBy == 0 || blockBuiltBy == grinderOwner)
                            {
                                info.Amount *= BasicGrinderMultiplier;
                                return;
                            }

                            var targetFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(blockBuiltBy);
                            var grinderFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(grinderOwner);

                            if (targetFaction != null && grinderFaction != null && targetFaction.FactionId == grinderFaction.FactionId)
                            {
                                info.Amount *= BasicGrinderMultiplier;
                                return;
                            }

                            // Block grinding unowned blocks built by other players with starter grinder
                            info.Amount = 0f;
                            return;
                        }
                        else
                        {
                            if (blockOwner == grinderOwner)
                            {
                                info.Amount *= BasicGrinderMultiplier;
                                return;
                            }

                            var targetFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(blockOwner);
                            var grinderFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(grinderOwner);

                            if (targetFaction != null && grinderFaction != null && targetFaction.FactionId == grinderFaction.FactionId)
                            {
                                info.Amount *= BasicGrinderMultiplier;
                                return;
                            }

                            // Block grinding blocks owned by other players with starter grinder
                            info.Amount = 0f;
                            return;
                        }
                    }
                }
                else
                {
                    // 2. Non-Grind Damage (Weapons, explosions, collisions, ramming, etc.)
                    // Block non-grind damage to blocks and characters inside Zone 0
                    var slimBlock = target as IMySlimBlock;
                    if (slimBlock != null)
                    {
                        if (IsInNoPvpZone(slimBlock.CubeGrid.GetPosition()))
                        {
                            info.Amount = 0f;
                        }
                        return;
                    }

                    var character = target as IMyCharacter;
                    if (character != null)
                    {
                        if (IsInNoPvpZone(character.GetPosition()))
                        {
                            info.Amount = 0f;
                        }
                        return;
                    }
                }
            }
            catch (Exception exc)
            {
                MyLog.Default.WriteLineAndConsole($"GVK_NoPVPZone error in damage handler: {exc}");
            }
        }

        public static bool IsInNoPvpZone(Vector3D position)
        {
            return Vector3D.DistanceSquared(position, Zone0CenterCoord) <= Zone0RadiusSquared;
        }
    }
}

