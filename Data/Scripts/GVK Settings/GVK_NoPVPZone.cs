using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;


namespace StarterGrinder
{
    /// <summary>
    /// Enforces automated governance for Zone 0 (0 – 20km) and Zone 1 (20km – 35km) around Crossroads Tower.
    /// Zone 0 provides complete starter immunity (all block damage zeroed).
    /// Zone 1 provides PvE &amp; salvage protection: players and NPCs can engage hostile derelicts/wrecks,
    /// but player-versus-player damage and cross-faction player grinding are strictly blocked.
    /// </summary>
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class StarterGrinder : MySessionComponentBase
    {
        private IMySlimBlock reuse_slim;
        private IMyAngleGrinder reuse_grinder;
        private IMyShipGrinder ship_grinder;
        private IMyFaction reuse_faction;
        private IMyFaction reuse_faction_grinder;
        private readonly string grinder_sub = "AngleGrinder";
        private readonly float multiplier = 1.2f;
        private readonly MyStringHash grindHash = MyStringHash.GetOrCompute("Grind");

        /// <summary>
        /// Crossroads Tower Beacon coordinate at top of tower (GPS: Zone 0 Center).
        /// </summary>
        private readonly Vector3D NO_DAMAGE_AREA = new Vector3D(62495.55, 28019.04, 37195.71);

        /// <summary>
        /// Zone 0 outer boundary in meters (20km). Complete damage immunity for all blocks.
        /// </summary>
        private const float ZONE_0_RADIUS = 20000f;
        private const double ZONE_0_RADIUS_SQ = 400000000.0; // 20,000^2

        /// <summary>
        /// Zone 1 outer boundary in meters (35km). PvE only; zero PvP damage or cross-faction player grinding.
        /// </summary>
        private const float NO_DAMAGE_RADIUS = 35000f;
        private const double NO_DAMAGE_RADIUS_SQ = 1225000000.0; // 35,000^2

        /// <summary>
        /// Registers the damage handler on the server during session initialization.
        /// </summary>
        /// <param name="sessionComponent">The session component builder.</param>
        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            if (MyAPIGateway.Session.IsServer)
            {
                MyAPIGateway.Session.DamageSystem.RegisterBeforeDamageHandler(0, grinder_handler);
            }
        }

        /// <summary>
        /// Intercepts damage events on grids and characters prior to application.
        /// Enforces Zone 0 god-mode, Zone 1 PvE protections, and global basic grinder hack-prevention.
        /// </summary>
        /// <param name="target">The entity or slim block taking damage.</param>
        /// <param name="info">The damage information struct, modified to 0f if blocked.</param>
        private void grinder_handler(object target, ref MyDamageInformation info)
        {
            try
            {
                if (info.Type.Equals(grindHash))
                {
                    reuse_slim = target as IMySlimBlock;
                    if (reuse_slim == null) return;

                    IMyEntity ent = MyAPIGateway.Entities.GetEntityById(info.AttackerId);
                    if (ent == null) return;

                    reuse_grinder = ent as IMyAngleGrinder;
                    ship_grinder = ent as IMyShipGrinder;

                    // -------------------------------------------------------------------------
                    // RULE 1: BASIC STARTER GRINDER ANTI-HACK & ANTI-HYDROMAN (GLOBAL)
                    // The basic spawn grinder (AngleGrinder) cannot hack enemy players OR hostile NPCs.
                    // This eliminates the hydroman respawn-rush exploit.
                    // Players must build an upgraded grinder (Tiers 2-4) or a ship grinder to hack/salvage NPCs.
                    // -------------------------------------------------------------------------
                    if (reuse_grinder != null && reuse_grinder.DefinitionId.SubtypeName == grinder_sub)
                    {
                        var slim_owner_id = reuse_slim.OwnerId;
                        var slim_built_id = reuse_slim.BuiltBy;
                        var grinder_owner = reuse_grinder.OwnerIdentityId;

                        // Target has a functional block owner
                        if (slim_owner_id != 0)
                        {
                            // Self-owned block: allow with boost
                            if (slim_owner_id == grinder_owner)
                            {
                                info.Amount *= multiplier;
                                return;
                            }

                            // Same-faction / allied player block: allow with boost
                            reuse_faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(slim_owner_id);
                            reuse_faction_grinder = MyAPIGateway.Session.Factions.TryGetPlayerFaction(grinder_owner);
                            if (reuse_faction != null && reuse_faction_grinder != null && reuse_faction.FactionId == reuse_faction_grinder.FactionId)
                            {
                                info.Amount *= multiplier;
                                return;
                            }

                            // Blocked: Basic starter grinder cannot grind/hack enemy players OR hostile NPCs!
                            info.Amount = 0f;
                            return;
                        }

                        // Target has no functional owner (armor blocks, unowned components)
                        if (slim_built_id != 0)
                        {
                            // Self-built block: allow with boost
                            if (grinder_owner == slim_built_id)
                            {
                                info.Amount *= multiplier;
                                return;
                            }

                            // Same-faction / allied builder: allow with boost
                            reuse_faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(slim_built_id);
                            reuse_faction_grinder = MyAPIGateway.Session.Factions.TryGetPlayerFaction(grinder_owner);
                            if (reuse_faction != null && reuse_faction_grinder != null && reuse_faction.FactionId == reuse_faction_grinder.FactionId)
                            {
                                info.Amount *= multiplier;
                                return;
                            }

                            // Blocked: Built by an enemy player or hostile NPC!
                            info.Amount = 0f;
                            return;
                        }

                        // Completely unowned and unbuilt neutral debris/salvage: allow with boost
                        info.Amount *= multiplier;
                        return;
                    }

                    // -------------------------------------------------------------------------
                    // RULE 2: ZONE 0 & ZONE 1 PVE GOVERNANCE (0 – 35km FROM CROSSROADS)
                    // Upgraded hand grinders (Tiers 2-4) and ship grinders are fully authorized to
                    // salvage unowned derelicts and hostile NPC wrecks in Z0 and Z1.
                    // Cross-faction grinding of other player grids remains strictly blocked.
                    // -------------------------------------------------------------------------
                    if (IsInZone(reuse_slim, NO_DAMAGE_RADIUS_SQ))
                    {
                        long owner = reuse_slim.CubeGrid.BigOwners.FirstOrDefault();
                        if (owner == 0) owner = reuse_slim.OwnerId;
                        if (owner == 0) owner = reuse_slim.BuiltBy;

                        // 1. Unowned blocks (wrecks, neutral derelicts) can ALWAYS be ground by upgraded/ship grinders
                        if (owner == 0) return;

                        // 2. Hostile NPC derelicts/wrecks can ALWAYS be ground down by upgraded/ship grinders
                        IMyFaction targetFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(owner);
                        if (targetFaction != null && (targetFaction.IsEveryoneNpc() || targetFaction.Tag == "GAALSIEN" || targetFaction.Tag == "DERELICT" || targetFaction.Tag == "SPRT" || targetFaction.Tag == "KHAANEPH"))
                        {
                            return;
                        }

                        // 3. Target is a player grid: resolve grinder owner (upgraded hand grinder or ship grinder)
                        long grinderOwner = 0;
                        if (reuse_grinder != null)
                        {
                            grinderOwner = reuse_grinder.OwnerIdentityId;
                        }
                        else if (ship_grinder != null)
                        {
                            grinderOwner = ship_grinder.OwnerId != 0 ? ship_grinder.OwnerId : ship_grinder.CubeGrid.BigOwners.FirstOrDefault();
                        }

                        // Self-owned blocks can always be ground
                        if (grinderOwner != 0 && owner == grinderOwner) return;

                        // Allied or same faction check
                        if (grinderOwner != 0 && targetFaction != null)
                        {
                            IMyFaction grinderFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(grinderOwner);
                            if (grinderFaction != null && targetFaction.FactionId == grinderFaction.FactionId) return;
                        }

                        // Block cross-faction grinding of player grids in Zone 0 and Zone 1
                        info.Amount = 0f;
                        return;
                    }
                }
                else
                {
                    // Non-grind damage (weapons, kinetic, collisions, missiles)
                    reuse_slim = target as IMySlimBlock;
                    if (reuse_slim != null)
                    {
                        double distSq = Vector3D.DistanceSquared(reuse_slim.CubeGrid.GetPosition(), NO_DAMAGE_AREA);

                        // If outside the 35km protected envelope, allow all normal damage
                        if (distSq > NO_DAMAGE_RADIUS_SQ) return;

                        // Zone 0 (0 – 20km): Total starter hub immunity for all blocks
                        if (distSq <= ZONE_0_RADIUS_SQ)
                        {
                            info.Amount = 0f;
                            return;
                        }

                        // Zone 1 (20km – 35km): PvE & Salvage Region
                        // Target check: Unowned blocks (derelict wrecks) can take weapon damage
                        long targetOwner = reuse_slim.CubeGrid.BigOwners.FirstOrDefault();
                        if (targetOwner == 0) return;

                        // Target check: Hostile NPC grids can take weapon damage
                        IMyFaction targetFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(targetOwner);
                        if (targetFaction != null && targetFaction.IsEveryoneNpc()) return;

                        // Target is a player grid: Check attacker
                        IMyEntity attackerEnt = MyAPIGateway.Entities.GetEntityById(info.AttackerId);
                        if (attackerEnt != null)
                        {
                            long attackerOwner = 0;
                            IMyCubeBlock attackerBlock = attackerEnt as IMyCubeBlock;
                            if (attackerBlock != null)
                            {
                                attackerOwner = attackerBlock.OwnerId != 0 ? attackerBlock.OwnerId : attackerBlock.CubeGrid.BigOwners.FirstOrDefault();
                            }
                            else
                            {
                                IMyCubeGrid attackerGrid = attackerEnt as IMyCubeGrid;
                                if (attackerGrid != null)
                                {
                                    attackerOwner = attackerGrid.BigOwners.FirstOrDefault();
                                }
                                else
                                {
                                    IMyCharacter attackerChar = attackerEnt as IMyCharacter;
                                    if (attackerChar != null)
                                    {
                                        attackerOwner = attackerChar.ControllerInfo?.ControllingIdentityId ?? 0;
                                    }
                                }
                            }

                            if (attackerOwner != 0)
                            {
                                IMyFaction attackerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(attackerOwner);

                                // If attacker is an NPC faction, NPCs can damage players in Zone 1
                                if (attackerFaction != null && attackerFaction.IsEveryoneNpc()) return;

                                // Allow self-damage and same-faction friendly fire
                                if (attackerOwner == targetOwner) return;
                                if (targetFaction != null && attackerFaction != null && targetFaction.FactionId == attackerFaction.FactionId) return;
                            }
                        }

                        // Cross-faction player vs player damage is blocked in Zone 1
                        info.Amount = 0f;
                        return;
                    }

                    // Character damage protection in Zone 0 and Zone 1
                    IMyCharacter targetChar = target as IMyCharacter;
                    if (targetChar != null)
                    {
                        double distSq = Vector3D.DistanceSquared(targetChar.GetPosition(), NO_DAMAGE_AREA);
                        if (distSq <= NO_DAMAGE_RADIUS_SQ)
                        {
                            // In Zone 0: absolute character immunity
                            if (distSq <= ZONE_0_RADIUS_SQ)
                            {
                                info.Amount = 0f;
                                return;
                            }

                            // In Zone 1: allow NPCs to shoot players, but block player-on-player PvP
                            IMyEntity attackerEnt = MyAPIGateway.Entities.GetEntityById(info.AttackerId);
                            if (attackerEnt != null)
                            {
                                long attackerIdentity = 0;
                                IMyCharacter attackerChar = attackerEnt as IMyCharacter;
                                if (attackerChar != null)
                                {
                                    attackerIdentity = attackerChar.ControllerInfo?.ControllingIdentityId ?? 0;
                                }
                                else
                                {
                                    IMyCubeBlock attackerBlock = attackerEnt as IMyCubeBlock;
                                    if (attackerBlock != null)
                                        attackerIdentity = attackerBlock.OwnerId;
                                }

                                if (attackerIdentity != 0)
                                {
                                    IMyFaction attackerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(attackerIdentity);
                                    if (attackerFaction != null && attackerFaction.IsEveryoneNpc()) return;
                                }
                            }

                            // Block PvP player-on-player damage
                            info.Amount = 0f;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[GVK_NoPVPZone] Error in damage handler: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks whether a block's parent grid is within a squared radius from Crossroads Beacon.
        /// </summary>
        /// <param name="block">The target block to test.</param>
        /// <param name="radiusSq">The distance squared threshold.</param>
        /// <returns>True if within the radius; otherwise false.</returns>
        private bool IsInZone(IMySlimBlock block, double radiusSq)
        {
            if (block?.CubeGrid == null) return false;
            return Vector3D.DistanceSquared(block.CubeGrid.GetPosition(), NO_DAMAGE_AREA) <= radiusSq;
        }
    }
}
