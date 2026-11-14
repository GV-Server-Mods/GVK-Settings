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
using VRage.Game.ObjectBuilders.Components;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

// NOTE: class/namespace name kept from the original "StarterGrinder" tool-boost script this
// governance layer grew out of, to avoid touching session component identity.
namespace StarterGrinder
{
    /// <summary>
    /// GVK automated damage governance by MES zone sphere.
    /// Z0: player grids immune to entity damage; characters protected from NPC/enemy
    /// damage. Z1: anti-PvP - player-vs-player damage/grinding blocked; NPC, self,
    /// friendly and environment (falls/terrain) damage allowed. Global: T1 grinder anti-hack
    /// with 1.2x boost, 2x T2-4 hand-grinder NPC salvage boost (stacks with Suit Combat Balancer),
    /// and no weather, radiation, asphyxia, low-pressure or temperature damage to characters inside
    /// safe zones that block damage.
    /// </summary>
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class StarterGrinder : MySessionComponentBase
    {
        // Zone spheres mirror the GVK-Derelicts [MES Zone] profiles (GVK-Universal-Zone.sbc), which are
        // canonical, and GVK_ZoneNavCommand.cs; keep in sync. Zone 0 is centred on Crossroads, Zones 1-3
        // on Z3_CENTER. Zone 1 = outside the Zone 0 and Zone 2 spheres, so it matches the /gvk zone bar.
        private static readonly Vector3D CROSSROADS_TOWER = new Vector3D(62495.55, 28019.04, 37195.71);
        private static readonly Vector3D Z3_CENTER = new Vector3D(3569.33, 36772.94, 26952.63);
        private const double ZONE_0_RADIUS = 20000.0;
        private const double ZONE_2_RADIUS = 49300.0;
        private const double ZONE_0_RADIUS_SQ = ZONE_0_RADIUS * ZONE_0_RADIUS;
        private const double ZONE_2_RADIUS_SQ = ZONE_2_RADIUS * ZONE_2_RADIUS;

        /// <summary>Vanilla subtype id of the Tier 1 starter hand grinder.</summary>
        private const string BASIC_GRINDER_SUBTYPE = "AngleGrinder";

        /// <summary>T1 grinder QoL boost on self/faction/unowned targets (Suit Combat Balancer doesn't scale these).</summary>
        private const float BASIC_GRINDER_BOOST = 1.2f;

        /// <summary>T2-4 hand grinder boost vs NPC blocks, all zones. Stacks with Suit Combat
        /// Balancer (0.25/0.5/0.15) - net: 0.50x terminal, 1.0x below-functional, 0.30x armor.</summary>
        private const float NPC_HAND_GRINDER_BOOST = 2.0f;

        /// <summary>Vanilla damage type string hash applied by grinders (hand + ship).</summary>
        private static readonly MyStringHash GRIND_DAMAGE = MyStringHash.GetOrCompute("Grind");

        /// <summary>Environment hazard damage types that vanilla MyCharacter.DoDamage lets through safe zones
        /// (Suicide and Hunger stay vanilla).</summary>
        private static readonly MyStringHash WEATHER_DAMAGE = MyDamageType.Weather;
        private static readonly MyStringHash RADIOACTIVITY_DAMAGE = MyDamageType.Radioactivity;
        private static readonly MyStringHash ASPHYXIA_DAMAGE = MyDamageType.Asphyxia;
        private static readonly MyStringHash LOW_PRESSURE_DAMAGE = MyDamageType.LowPressure;
        private static readonly MyStringHash TEMPERATURE_DAMAGE = MyDamageType.Temperature;

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
                else if (IsSafeZoneHazard(info.Type))
                    HandleHazardDamage(target, ref info);
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

            // ZONE RULES: cross-faction grinding of PLAYER grids blocked unless grinder and target
            // are both in Zones 2-3; self/faction allowed.
            if (ProtectedZone(slim.CubeGrid.GetPosition()) >= 2 && ProtectedZone(AttackerPosition(attackerEntity)) >= 2) return;

            if (owner == 0) return; // unowned wrecks/debris: always salvageable

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

        /// <summary>True for the hazard types safe zones shelter characters from.</summary>
        private static bool IsSafeZoneHazard(MyStringHash type)
        {
            return type.Equals(WEATHER_DAMAGE) || type.Equals(RADIOACTIVITY_DAMAGE) || type.Equals(ASPHYXIA_DAMAGE)
                || type.Equals(LOW_PRESSURE_DAMAGE) || type.Equals(TEMPERATURE_DAMAGE);
        }

        /// <summary>
        /// Weather, radiation, asphyxia, low-pressure and temperature damage: cancelled for characters
        /// inside a safe zone that blocks damage. Outside such zones it stays vanilla.
        /// </summary>
        private static void HandleHazardDamage(object target, ref MyDamageInformation info)
        {
            if (!(target is IMyCharacter)) return;
            MyEntity character = target as MyEntity;
            if (character != null && !MySessionComponentSafeZones.IsActionAllowed(character, MySafeZoneAction.Damage))
                info.Amount = 0f;
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
        /// blocks and characters. PvP lands only when target AND attacker are in Zone 2 or 3,
        /// so nobody can shoot out of Zone 0/1 at players who can't shoot back.
        /// </summary>
        private void HandleOtherDamage(object target, ref MyDamageInformation info)
        {
            IMySlimBlock slim = target as IMySlimBlock;
            if (slim != null)
            {
                int zone = ProtectedZone(slim.CubeGrid.GetPosition());

                // Zone 0: total starter hub immunity for all blocks
                if (zone == 0)
                {
                    info.Amount = 0f;
                    return;
                }

                // No attacker entity = environment (meteors, debris): normal damage
                IMyEntity attacker = GetAttacker(info.AttackerId);
                if (attacker == null) return;

                // Target and attacker both in Zones 2-3: all normal damage applies
                if (zone >= 2 && ProtectedZone(AttackerPosition(attacker)) >= 2) return;

                // Target in Zone 1, or attacker in Zone 0/1: only PvP is blocked
                long targetOwner = GetGridOwner(slim);
                if (targetOwner == 0) return; // unowned derelicts take weapon damage

                IMyFaction targetFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(targetOwner);
                if (IsNpcFaction(targetFaction)) return; // NPC grids take weapon damage

                long attackerOwner = ResolveOwner(attacker);

                // Ownerless attacker = environment (terrain voxel-map collisions resolve to owner 0,
                // unowned debris): allowed so rovers still eat crash damage in Z1.
                if (attackerOwner == 0) return;

                if (attackerOwner == targetOwner) return; // self-damage
                if (SameFaction(attackerOwner, targetOwner)) return; // friendly fire

                IMyFaction attackerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(attackerOwner);
                if (IsNpcFaction(attackerFaction)) return; // NPCs can damage player grids

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
        /// Zones 2-3 vanilla unless an enemy player attacks from Zone 0/1.
        /// </summary>
        private void HandleCharacterDamage(IMyCharacter targetCharacter, ref MyDamageInformation info)
        {
            // No attacker entity (AttackerId 0) = pure environment: always allowed.
            IMyEntity attacker = GetAttacker(info.AttackerId);
            if (attacker == null) return;

            // Victim and attacker both in Zones 2-3: vanilla rules
            int zone = ProtectedZone(targetCharacter.GetPosition());
            if (zone >= 2 && ProtectedZone(AttackerPosition(attacker)) >= 2) return;

            // Ownerless attacker = environment too: falling on terrain reports the VOXEL MAP as
            // the attacker (Keen passes the contact entity id; terrain resolves to owner 0).
            long attackerOwner = ResolveOwner(attacker);
            if (attackerOwner == 0) return;

            long victimId = targetCharacter.ControllerInfo != null
                ? targetCharacter.ControllerInfo.ControllingIdentityId
                : 0;

            // Self-inflicted (own tools/rams): allowed in all zones - bad-habit training.
            if (victimId != 0 && attackerOwner == victimId) return;

            if (victimId != 0 && attackerOwner != 0 && SameFaction(attackerOwner, victimId)) return;

            // Z0: NPC and enemy-player damage blocked
            if (zone == 0)
            {
                info.Amount = 0f;
                return;
            }

            // Z1 victim, or attacker in Z0/Z1: NPC attackers allowed; enemy players blocked
            IMyFaction attackerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(attackerOwner);
            if (IsNpcFaction(attackerFaction)) return;

            info.Amount = 0f;
        }

        /// <summary>Attacking entity, or null for environment damage (AttackerId 0 or entity gone).</summary>
        private static IMyEntity GetAttacker(long attackerEntityId)
        {
            return attackerEntityId == 0 ? null : MyAPIGateway.Entities.GetEntityById(attackerEntityId);
        }

        /// <summary>
        /// Position that decides the attacker's zone: its grid for blocks (one verdict per grid,
        /// as for targets), otherwise the entity itself (grid, character, hand tool or gun).
        /// </summary>
        private static Vector3D AttackerPosition(IMyEntity attacker)
        {
            IMyCubeBlock block = attacker as IMyCubeBlock;
            return block != null && block.CubeGrid != null ? block.CubeGrid.GetPosition() : attacker.GetPosition();
        }

        /// <summary>
        /// Owning identity behind an attacking entity: cube blocks, grids (rams), characters,
        /// hand tools, and hand rifles/pistols (their bullets name the gun as the attacker).
        /// </summary>
        /// <returns>Attacker identity id, or 0 if the entity has no resolvable owner.</returns>
        private static long ResolveOwner(IMyEntity attackerEntity)
        {
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

            // Hand rifles and pistols are MyGunBase guns, not tools
            IMyAutomaticRifleGun handGun = attackerEntity as IMyAutomaticRifleGun;
            if (handGun != null)
                return handGun.OwnerIdentityId;

            return 0; // entity exists but ownership unresolvable
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
        /// Zone rules at a position: 0 = Zone 0 sphere, 1 = Zone 1 (outside the Zone 0 and Zone 2
        /// spheres), 2 = Zone 2 or 3 (vanilla). Zone 0 wins where it overlaps Zone 2 deep
        /// underground, as on the /gvk zone bar. Blocks are judged by their grid's position (one
        /// verdict per grid). Squared distances, allocation-free.
        /// </summary>
        private static int ProtectedZone(Vector3D position)
        {
            if (Vector3D.DistanceSquared(position, CROSSROADS_TOWER) <= ZONE_0_RADIUS_SQ) return 0;
            return Vector3D.DistanceSquared(position, Z3_CENTER) > ZONE_2_RADIUS_SQ ? 1 : 2;
        }
    }
}
