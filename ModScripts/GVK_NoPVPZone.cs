using System;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

// NOTE: class/namespace name kept from the original "StarterGrinder" tool-boost script this
// governance layer grew out of, to avoid touching session component identity.
namespace StarterGrinder
{
    /// <summary>
    /// GVK automated damage governance around Crossroads Tower.
    /// Z0 (0-20km): player grids immune to entity damage; characters protected from NPC/enemy
    /// damage. Z1 (20-35km): anti-PvP - player-vs-player damage/grinding blocked; NPC, self,
    /// friendly and environment (falls/terrain) damage allowed. Global: T1 grinder anti-hack
    /// with 1.2x boost, 2x T2-4 hand-grinder NPC salvage boost (stacks with Suit Combat Balancer).
    /// </summary>
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class StarterGrinder : MySessionComponentBase
    {
        /// <summary>Crossroads Tower beacon (Zone 0 center), top of tower.</summary>
        private static readonly Vector3D CROSSROADS_TOWER = new Vector3D(62495.55, 28019.04, 37195.71);

        /// <summary>Zone 0 outer boundary in meters (20km). Safe Starter Hub.</summary>
        private const float ZONE_0_RADIUS = 20000f;

        /// <summary>Zone 1 outer boundary in meters (35km). PvE &amp; Salvage Frontier.</summary>
        private const float ZONE_1_RADIUS = 35000f;

        // Squared radii derived from the float constants above so they can never drift apart.
        private static readonly double ZONE_0_RADIUS_SQ = ZONE_0_RADIUS * ZONE_0_RADIUS;
        private static readonly double ZONE_1_RADIUS_SQ = ZONE_1_RADIUS * ZONE_1_RADIUS;

        /// <summary>Vanilla subtype id of the Tier 1 starter hand grinder.</summary>
        private const string BASIC_GRINDER_SUBTYPE = "AngleGrinder";

        /// <summary>T1 grinder QoL boost on self/faction/unowned targets (Suit Combat Balancer doesn't scale these).</summary>
        private const float BASIC_GRINDER_BOOST = 1.2f;

        /// <summary>T2-4 hand grinder boost vs NPC blocks, all zones. Stacks with Suit Combat
        /// Balancer (0.25/0.5/0.15) - net: 0.50x terminal, 1.0x below-functional, 0.30x armor.</summary>
        private const float NPC_HAND_GRINDER_BOOST = 2.0f;

        /// <summary>Vanilla damage type string hash applied by grinders (hand + ship).</summary>
        private static readonly MyStringHash GRIND_DAMAGE = MyStringHash.GetOrCompute("Grind");

        /// <summary>
        /// Registers the server damage handler in BeforeStart(): DamageSystem is not guaranteed
        /// initialized during Init() (Keen session load-order quirk; matches ExplosiveDamageFix).
        /// </summary>
        public override void BeforeStart()
        {
            if (MyAPIGateway.Session.IsServer)
            {
                var damageSystem = MyAPIGateway.Session.DamageSystem;
                if (damageSystem != null)
                {
                    damageSystem.RegisterBeforeDamageHandler(0, DamageHandler);
                }
            }
        }

        /// <summary>Keen's IMyDamageSystem has no Unregister API (verified 1.208): handler lives for the session.</summary>
        protected override void UnloadData()
        {
            base.UnloadData();
        }

        /// <summary>
        /// Damage dispatcher. Handler locals are method-scoped on purpose: MyDamageSystem raises
        /// before-handlers from multiple sim/physics threads, so shared fields would race.
        /// </summary>
        private void DamageHandler(object target, ref MyDamageInformation info)
        {
            try
            {
                if (info.Type.Equals(GRIND_DAMAGE))
                    HandleGrindDamage(target, ref info);
                else
                    HandleOtherDamage(target, ref info);
            }
            catch (Exception ex)
            {
                // Full exception (message + stack) so the log is actually diagnosable.
                MyLog.Default.WriteLineAndConsole($"[GVK_NoPVPZone] Error in damage handler: {ex}");
            }
        }

        /// <summary>
        /// Grinder damage (hand + ship grinders) against blocks and characters.
        /// </summary>
        private void HandleGrindDamage(object target, ref MyDamageInformation info)
        {
            // Grind vs player bodies: the character matrix decides (blocks ship-grinder deaths in safe zones).
            IMyCharacter groundCharacter = target as IMyCharacter;
            if (groundCharacter != null)
            {
                HandleCharacterDamage(groundCharacter, ref info);
                return;
            }

            IMySlimBlock slim = target as IMySlimBlock;
            if (slim == null) return; // voxel / tree grinding: vanilla rules

            IMyEntity attackerEntity = MyAPIGateway.Entities.GetEntityById(info.AttackerId);
            if (attackerEntity == null) return;

            IMyAngleGrinder handGrinder = attackerEntity as IMyAngleGrinder;
            IMyShipGrinder shipGrinder = attackerEntity as IMyShipGrinder;

            // GLOBAL: T1 starter grinder anti-hack ("hydroman" fix).
            if (handGrinder != null && handGrinder.DefinitionId.SubtypeName == BASIC_GRINDER_SUBTYPE)
            {
                ApplyBasicGrinderRules(slim, handGrinder.OwnerIdentityId, ref info);
                return;
            }

            // GLOBAL: NPC targets always salvageable; T2-4 hand grinders get the 2x boost.
            // Friendly NPC structures (COALITION hub) are MES-protected in GVK_Derelicts - dependency, don't remove this allow without checking that config.
            long owner = GetGridOwner(slim);
            if (owner != 0)
            {
                IMyFaction ownerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(owner);
                if (IsNpcFaction(ownerFaction))
                {
                    if (handGrinder != null)
                        info.Amount *= NPC_HAND_GRINDER_BOOST;
                    return;
                }
            }

            // ZONE RULES: cross-faction grinding of PLAYER grids blocked in Z0/Z1; self/faction allowed.
            if (!IsInZone(slim, ZONE_1_RADIUS_SQ)) return; // deep desert: vanilla rules

            if (owner == 0) return; // unowned wrecks/debris: always salvageable in Z0/Z1

            long grinderOwner = 0;
            if (handGrinder != null)
            {
                grinderOwner = handGrinder.OwnerIdentityId;
            }
            else if (shipGrinder != null)
            {
                grinderOwner = shipGrinder.OwnerId != 0 ? shipGrinder.OwnerId : GridFirstOwner(shipGrinder.CubeGrid);
            }

            if (grinderOwner != 0 && owner == grinderOwner) return; // self-owned
            if (grinderOwner != 0 && SameFaction(owner, grinderOwner)) return; // allied

            info.Amount = 0f; // cross-faction PvP grinding blocked
        }

        /// <summary>
        /// Ownership rules for the T1 basic grinder (global): allowed targets get the 1.2x boost;
        /// enemy player/NPC blocks hard-blocked (kills the "hydroman" respawn-rush exploit).
        /// </summary>
        /// <param name="slim">Block being ground.</param>
        /// <param name="grinderOwner">Identity currently wielding the basic grinder.</param>
        /// <param name="info">Damage info, modified in place.</param>
        private static void ApplyBasicGrinderRules(IMySlimBlock slim, long grinderOwner, ref MyDamageInformation info)
        {
            long slimOwner = slim.OwnerId;

            // Target has a functional block owner
            if (slimOwner != 0)
            {
                if (slimOwner == grinderOwner || SameFaction(slimOwner, grinderOwner))
                {
                    info.Amount *= BASIC_GRINDER_BOOST;
                    return;
                }

                // Basic starter grinder cannot grind/hack enemy players or NPCs
                info.Amount = 0f;
                return;
            }

            // Target has no functional owner (armor blocks, unowned components): use builder id
            long slimBuilder = slim.BuiltBy;
            if (slimBuilder != 0)
            {
                if (grinderOwner == slimBuilder || SameFaction(slimBuilder, grinderOwner))
                {
                    info.Amount *= BASIC_GRINDER_BOOST;
                    return;
                }

                // Built by an enemy player or NPC: blocked
                info.Amount = 0f;
                return;
            }

            // Completely unowned and unbuilt neutral debris/salvage: allowed with boost
            info.Amount *= BASIC_GRINDER_BOOST;
        }

        /// <summary>
        /// All non-grind damage (weapons, drills, kinetic, missiles, collisions) against
        /// blocks and characters.
        /// </summary>
        private void HandleOtherDamage(object target, ref MyDamageInformation info)
        {
            IMySlimBlock slim = target as IMySlimBlock;
            if (slim != null)
            {
                double distSq = Vector3D.DistanceSquared(slim.CubeGrid.GetPosition(), CROSSROADS_TOWER);

                // Outside the 35km protected envelope: all normal damage applies
                if (distSq > ZONE_1_RADIUS_SQ) return;

                // Zone 0 (0-20km): total starter hub immunity for all blocks
                if (distSq <= ZONE_0_RADIUS_SQ)
                {
                    info.Amount = 0f;
                    return;
                }

                // Zone 1 (20-35km): PvE & Salvage Frontier
                long targetOwner = GetGridOwner(slim);
                if (targetOwner == 0) return; // unowned derelicts take weapon damage

                IMyFaction targetFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(targetOwner);
                if (IsNpcFaction(targetFaction)) return; // NPC grids take weapon damage

                // Target is a player grid: resolve the attacker
                long attackerOwner = ResolveAttackerOwner(info.AttackerId);

                // Ownerless attacker = environment (terrain voxel-map collisions resolve to owner 0,
                // unowned debris, meteors): allowed so rovers still eat crash damage in Z1.
                if (attackerOwner == 0) return;

                if (attackerOwner == targetOwner) return; // self-damage
                if (SameFaction(attackerOwner, targetOwner)) return; // friendly fire

                IMyFaction attackerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(attackerOwner);
                if (IsNpcFaction(attackerFaction)) return; // NPCs can damage player grids in Z1

                info.Amount = 0f; // PvP blocked
                return;
            }

            IMyCharacter damagedCharacter = target as IMyCharacter;
            if (damagedCharacter != null)
            {
                HandleCharacterDamage(damagedCharacter, ref info);
            }
        }

        /// <summary>
        /// Character damage matrix (weapons, kinetic AND grinder damage vs bodies):
        /// environment/ownerless attackers and self/friendly damage always allowed;
        /// Z0 blocks NPC and enemy-player damage; Z1 allows NPCs, blocks enemy players;
        /// beyond 35km vanilla.
        /// </summary>
        private void HandleCharacterDamage(IMyCharacter targetCharacter, ref MyDamageInformation info)
        {
            double distSq = Vector3D.DistanceSquared(targetCharacter.GetPosition(), CROSSROADS_TOWER);
            if (distSq > ZONE_1_RADIUS_SQ) return; // outside zones: vanilla rules

            bool attackerEntityExists;
            long attackerOwner = ResolveAttackerOwner(info.AttackerId, out attackerEntityExists);

            // No attacker entity (AttackerId 0) = pure environment: always allowed.
            if (!attackerEntityExists) return;

            // Ownerless attacker = environment too: falling on terrain reports the VOXEL MAP as
            // the attacker (Keen passes the contact entity id; terrain resolves to owner 0).
            if (attackerOwner == 0) return;

            long victimId = targetCharacter.ControllerInfo != null
                ? targetCharacter.ControllerInfo.ControllingIdentityId
                : 0;

            // Self-inflicted (own tools/rams): allowed in all zones - bad-habit training.
            if (victimId != 0 && attackerOwner == victimId) return;

            if (victimId != 0 && attackerOwner != 0 && SameFaction(attackerOwner, victimId)) return;

            // Z0: NPC and enemy-player damage blocked
            if (distSq <= ZONE_0_RADIUS_SQ)
            {
                info.Amount = 0f;
                return;
            }

            // Z1: NPC attackers allowed; enemy players blocked as safe default
            if (attackerOwner != 0)
            {
                IMyFaction attackerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(attackerOwner);
                if (IsNpcFaction(attackerFaction)) return;
            }

            info.Amount = 0f;
        }

        /// <summary>
        /// Resolves the owning identity behind an attacking entity: cube blocks, grids (rams),
        /// characters, and handheld tools (gun entities owned by their wielder).
        /// </summary>
        /// <param name="attackerEntityId">Entity id from MyDamageInformation.AttackerId.</param>
        /// <param name="entityExists">False = environment damage (no attacker entity).</param>
        /// <returns>Attacker identity id, or 0 if the entity exists but has no resolvable owner.</returns>
        private static long ResolveAttackerOwner(long attackerEntityId, out bool entityExists)
        {
            entityExists = false;
            if (attackerEntityId == 0) return 0;

            IMyEntity attackerEntity = MyAPIGateway.Entities.GetEntityById(attackerEntityId);
            if (attackerEntity == null) return 0;
            entityExists = true;

            IMyCubeBlock attackerBlock = attackerEntity as IMyCubeBlock;
            if (attackerBlock != null)
                return attackerBlock.OwnerId != 0 ? attackerBlock.OwnerId : GridFirstOwner(attackerBlock.CubeGrid);

            IMyCubeGrid attackerGrid = attackerEntity as IMyCubeGrid;
            if (attackerGrid != null)
                return GridFirstOwner(attackerGrid);

            IMyCharacter attackerCharacter = attackerEntity as IMyCharacter;
            if (attackerCharacter != null && attackerCharacter.ControllerInfo != null)
                return attackerCharacter.ControllerInfo.ControllingIdentityId;

            // Handheld tools: gun objects whose OwnerIdentityId is the wielding player
            IMyHandheldGunObject<MyToolBase> handTool = attackerEntity as IMyHandheldGunObject<MyToolBase>;
            if (handTool != null)
                return handTool.OwnerIdentityId;

            return 0; // entity exists but ownership unresolvable
        }

        /// <summary>Overload for callers that only need the identity.</summary>
        private static long ResolveAttackerOwner(long attackerEntityId)
        {
            bool entityExists;
            return ResolveAttackerOwner(attackerEntityId, out entityExists);
        }

        /// <summary>
        /// True if both identities belong to the same faction. Zero identities never match.
        /// </summary>
        private static bool SameFaction(long identityA, long identityB)
        {
            if (identityA == 0 || identityB == 0) return false;
            IMyFaction factionA = MyAPIGateway.Session.Factions.TryGetPlayerFaction(identityA);
            if (factionA == null) return false;
            IMyFaction factionB = MyAPIGateway.Session.Factions.TryGetPlayerFaction(identityB);
            return factionB != null && factionA.FactionId == factionB.FactionId;
        }

        /// <summary>
        /// NPC faction check. IsEveryoneNpc() covers all NPCs incl. friendly COALITION - those
        /// structures are MES-protected in GVK_Derelicts (cross-system dependency, don't simplify
        /// this away). Tag list catches custom factions that misreport IsEveryoneNpc().
        /// </summary>
        private static bool IsNpcFaction(IMyFaction faction)
        {
            if (faction == null) return false;
            if (faction.IsEveryoneNpc()) return true;
            string tag = faction.Tag;
            return tag == "GAALSIEN" || tag == "DERELICT" || tag == "SPRT" || tag == "KHAANEPH";
        }

        /// <summary>Grid ownership fallback chain: BigOwners, then OwnerId, then BuiltBy. Allocation-free.</summary>
        private static long GetGridOwner(IMySlimBlock block)
        {
            if (block == null || block.CubeGrid == null) return 0;
            var bigOwners = block.CubeGrid.BigOwners;
            if (bigOwners.Count > 0) return bigOwners[0];
            if (block.OwnerId != 0) return block.OwnerId;
            return block.BuiltBy;
        }

        /// <summary>First BigOwner of a grid, or 0. Allocation-free.</summary>
        private static long GridFirstOwner(IMyCubeGrid grid)
        {
            if (grid == null) return 0;
            var bigOwners = grid.BigOwners;
            return bigOwners.Count > 0 ? bigOwners[0] : 0;
        }

        /// <summary>
        /// True if the block's grid center is within a squared radius of Crossroads Tower
        /// (one verdict per grid, consistent with the other GVK zone scripts).
        /// </summary>
        private static bool IsInZone(IMySlimBlock block, double radiusSq)
        {
            if (block == null || block.CubeGrid == null) return false;
            return Vector3D.DistanceSquared(block.CubeGrid.GetPosition(), CROSSROADS_TOWER) <= radiusSq;
        }
    }
}
