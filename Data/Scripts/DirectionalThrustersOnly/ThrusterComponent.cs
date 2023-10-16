using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using System;
using VRage.Game.Components;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace DirectionalThrustersOnly
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Thrust), false)]
    public class DirectionalThrustersOnlyEntityComponent : MyGameLogicComponent
    {

        private IMyThrust thruster;
        private Vector3 thrusterDirection;
        private DirectionalThrustersOnlyConfigurationItem config;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            if (!MyAPIGateway.Session.IsServer && MyAPIGateway.Multiplayer.MultiplayerActive)
            {
                NeedsUpdate = MyEntityUpdateEnum.NONE;
                return;
            }

            NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME;

            thruster = (IMyThrust)Entity;
            thrusterDirection = Base6Directions.GetVector(thruster.Orientation.Forward);
        }

        public override void UpdateOnceBeforeFrame()
        {
            if (Util.IsValid(thruster))
            {
                NeedsUpdate = MyEntityUpdateEnum.NONE;
                return;
            }

            var instanceConfig = DirectionalThrustersOnlySessionComponent.Instance?.Config;
            if (instanceConfig == null)
            {
                NeedsUpdate = MyEntityUpdateEnum.NONE;
                return;
            }

            var cubeGrid = thruster.CubeGrid;

            if (!Util.IsValid(cubeGrid) || DirectionalThrustersOnlySessionComponent.Instance.IsGridNPCOwned(cubeGrid))
            {
                NeedsUpdate = MyEntityUpdateEnum.NONE;
                return;
            }

            var configForType = instanceConfig.GetConfigForType(thruster.BlockDefinition);

            if (configForType == null)
            {
                NeedsUpdate = MyEntityUpdateEnum.NONE;
                return;
            }

            config = configForType;

            NeedsUpdate = MyEntityUpdateEnum.EACH_10TH_FRAME;
        }

        public override void UpdateBeforeSimulation10()
        {
            if (Util.IsValid(thruster) || !thruster.IsFunctional)
            {
                return;
            }

            var cubeGrid = thruster.CubeGrid;

            if (!Util.IsValid(cubeGrid) || cubeGrid.Physics == null)
            {
                return;
            }

            float naturalGravityInterference;
            var gravityNormal = MyAPIGateway.Physics.CalculateNaturalGravityAt(cubeGrid.PositionComp.GetPosition(), out naturalGravityInterference).Normalized();
            var gridOrientation = cubeGrid.PositionComp.GetOrientation();
            var worldThrustDirection = Vector3.TransformNormal(thrusterDirection, gridOrientation);
            var angleFromGravity = MathHelper.ToDegrees((float)Math.Acos(worldThrustDirection.Dot(gravityNormal)));

            if (angleFromGravity < config.MinThrustDegrees)
            {
                thruster.ThrustMultiplier = config.MinThrustMultiplier;
                thruster.PowerConsumptionMultiplier = config.MinThrustMultiplier;
            }
            else if (angleFromGravity < config.FalloffStartDegrees)
            {
                var requestedThrustMultiplier = MathHelper.Clamp(MathHelper.Lerp(config.MinThrustMultiplier, 1, (angleFromGravity - config.MinThrustDegrees) / (config.FalloffStartDegrees - config.MinThrustDegrees)), 0.01f, 1);
                thruster.ThrustMultiplier = requestedThrustMultiplier;
                thruster.PowerConsumptionMultiplier = requestedThrustMultiplier;
            }
            else
            {
                thruster.ThrustMultiplier = 1;
                thruster.PowerConsumptionMultiplier = 1;
            }
        }
    }
}