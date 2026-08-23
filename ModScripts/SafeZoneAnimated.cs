using ObjectBuilders.SafeZone;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using Sandbox.ModAPI.Interfaces.Terminal;
using SpaceEngineers.Game.Entities.Blocks.SafeZone;
using SpaceEngineers.Game.ModAPI;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace PSYCHO.SafeZoneAnimated
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_SafeZoneBlock), false)]
    public class AnimateSafeZone : MyGameLogicComponent
    {

        // =========
        // VARIABLES
        // =========
        // Defines the axis for switch.
        static readonly int X = 1;
        static readonly int Y = 2;
        static readonly int Z = 3;

        public bool DoOnce = false;
        public IMySafeZoneBlock block;
        public MyEntitySubpart subpart;

        // These are static as their identities shouldnt change across different instances
        private float HingePosX = 0f; // Hinge position on the X axis. 0 is center.
        private float HingePosY = 0f; // Hinge position on the Y axis. 0 is center.
        private float HingePosZ = 0f; // Hinge position on the Z axis. 0 is center.
        private float RotX = 0f; // Rotation on the X axis. 0 is no rotation.
        private float RotY = 0f; // Rotation on the Y axis. 0 is no rotation.
        private float RotZ = 0f; // Rotation on the Z axis. 0 is no rotation.

        // These are static as their identities shouldnt change across different instances
        private Matrix _rotMatrixX;
        private Matrix _rotMatrixY;
        private Matrix _rotMatrixZ;

        public float Rotator = 0f;

        float SafeZoneShieldRadius = 200f;
        float SafeZoneShieldMaxRadius = 200f;
        float AppliedRotSpeedByShieldSize = 0.5f;

        public bool SafeZoneIncomplete = false;
        public bool CheckForSubpart = false;

        // ========================
        // User changable variables
        // ========================
        static readonly int   RotationAxis = Z;            // X, Y, Z
        static readonly float MaxRotationSpeed = 0.1f;   // The max speed when shield is at max radius.
        static readonly float SpoolDownFactor = 0.0025f; // Slowdown factor.
        static readonly float SpoolUpFactor = 0.001f;    // Speedup factor.

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            block = (IMySafeZoneBlock)Entity;

            // Init the rot matricies here as you shouldnt need to recompute them
            // If you want an angle to be zero, use Matrix.Identity instead :)
            _rotMatrixX = Matrix.CreateRotationX(RotX);
            _rotMatrixY = Matrix.CreateRotationY(RotY);
            _rotMatrixZ = Matrix.CreateRotationZ(RotZ);

            NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override void UpdateOnceBeforeFrame()
        {
            SafeZoneShieldRadius = block.GetValueFloat("SafeZoneSlider");
            AppliedRotSpeedByShieldSize = (MaxRotationSpeed / SafeZoneShieldMaxRadius) * SafeZoneShieldRadius;

            block.IsWorkingChanged += IsWorkingChanged;
            block.PropertiesChanged += PropertiesChanged;

            block.TryGetSubpart("Empty", out subpart);

            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME;
        }

        public override void Close()
        {
            block.IsWorkingChanged -= IsWorkingChanged;
            block.PropertiesChanged -= PropertiesChanged;
            NeedsUpdate = MyEntityUpdateEnum.NONE;
        }

        private void IsWorkingChanged(IMyCubeBlock block)
        {
            if (!block.IsWorking)
            {
                SafeZoneIncomplete = true;
                //NeedsUpdate = MyEntityUpdateEnum.NONE;
            }
            else
            {
                CheckForSubpart = true;
                SafeZoneIncomplete = false;
                //NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME;
            }
        }

        public void PropertiesChanged(IMyTerminalBlock block)
        {
            SafeZoneShieldRadius = block.GetValueFloat("SafeZoneSlider");
            AppliedRotSpeedByShieldSize = (MaxRotationSpeed / SafeZoneShieldMaxRadius) * SafeZoneShieldRadius;
        }

        public override void UpdateBeforeSimulation()
        {
			if (MyAPIGateway.Utilities.IsDedicated) return;

            if (subpart == null && CheckForSubpart)
            {
                if (block.TryGetSubpart("Empty", out subpart))
                {
                    CheckForSubpart = false;
                    SafeZoneIncomplete = false;
                }
                else
                {
                    SafeZoneIncomplete = true;
                }

                Rotator = 0f;
            }

            if (subpart == null)
                return;

            try
            {
                //if (!block.IsFunctional && !SafeZoneDamaged) return;                // Ignore damaged or build progress blocks.
                if (SafeZoneIncomplete) return;                // Ignore build progress blocks.
                if (block.CubeGrid.Physics == null) return;     // Ignore ghost grids (projections).
				//if (block.IsWorking) newRotMatrixX = _rotMatrixX;
                //else newRotMatrixX = Matrix.Identity;
				
                if (DoOnce)
                {
                    DoOnce = true;
                    block.TryGetSubpart("Empty", out subpart);
                }

                // Checks if subpart is removed (i.e. when changing block color).
                if (subpart.Closed.Equals(true))
                {
                    if (!block.IsFunctional)
                    {
                        Rotator = 0f;
                        SafeZoneIncomplete = true;
                        return;
                    }
                    else
                        ResetLostSubpart();
                }

                bool Recompute = false;

                if (subpart != null)
                {
                    if (!block.IsSafeZoneEnabled())
                    {
                        if (Rotator <= 0f)
                        {
                            Rotator = 0f;
                            return;
                        }

                        Rotator -= MathHelper.Clamp(SpoolDownFactor, 0f, float.MaxValue);
                        Recompute = true;
                    }
                    else
                    {
                        if (Rotator < AppliedRotSpeedByShieldSize)
                        {
                            Rotator += MathHelper.Clamp(SpoolUpFactor, 0f, AppliedRotSpeedByShieldSize);
                            Recompute = true;
                        }
                        else if (Rotator > AppliedRotSpeedByShieldSize)
                        {
                            Rotator -= MathHelper.Clamp(SpoolUpFactor, 0f, float.MaxValue);
                            Recompute = true;
                        }
                    }

                    if (Recompute)
                    {
                        RotateMatrix();
                    }

                    ApplyRotations();
                }
            }
            catch (Exception e)
            {
                // This is for your benefit. Remove or change with your logging option before publishing.
                MyAPIGateway.Utilities.ShowNotification("Error: " + e.Message, 16);
            }
            /*catch (Exception e)
            {
                Logging.Instance.WriteLine("Error: " + e.Message);
            }*/
        }

        public override void UpdateAfterSimulation()
        {
            if (MyAPIGateway.Utilities.IsDedicated) return;

            //if (block != null && block.IsWorking && block.IsFunctional && subpart != null)
            //else if (block != null && subpart != null)

            if (block != null && subpart != null)
            {
                if (block.IsFunctional)
                {
                    Color color;
                    //if (!block.Enabled || !block.IsWorking)
                    if (!block.IsWorking)
                    {
                        color = Color.Red;
                    }
                    else if (block.IsSafeZoneEnabled())
                        color = Color.Green;
                    else
                        color = Color.Yellow;

                    subpart.SetEmissiveParts("Emissive", color, 1f);
                    block.SetEmissiveParts("Emissive", color, 1f);
                    SafeZoneIncomplete = false;
                }
                else
                {
                    subpart.SetEmissiveParts("Emissive", Color.Black, 0f);
                    block.SetEmissiveParts("Emissive", Color.Black, 0f);
                    SafeZoneIncomplete = false;
                }
            }
        }

        public void ApplyRotations()
        {
            var hingePos = new Vector3(HingePosX, HingePosY, HingePosZ); // This defines the location of a new pivot point.
            var MatrixTransl1 = Matrix.CreateTranslation(-(hingePos));
            var MatrixTransl2 = Matrix.CreateTranslation(hingePos);
            var rotMatrix = subpart.PositionComp.LocalMatrix;
            //rotMatrix *= (MatrixTransl1 * (Matrix.CreateRotationX(Rotator) * Matrix.CreateRotationY(RotY) * Matrix.CreateRotationZ(RotZ)) * MatrixTransl2);
            rotMatrix *= (MatrixTransl1 * _rotMatrixX * _rotMatrixY * _rotMatrixZ * MatrixTransl2);
            subpart.PositionComp.LocalMatrix = rotMatrix;
            //MyAPIGateway.Utilities.ShowNotification(Rotator.ToString(), 1);
        }

        public void RotateMatrix(bool neutral = false)
        {
            if (!neutral)
            {
                switch (RotationAxis)
                {
                    default:
                        _rotMatrixX = Matrix.CreateRotationX(Rotator);
                        break;
                    case 2:
                        _rotMatrixY = Matrix.CreateRotationY(Rotator);
                        break;
                    case 3:
                        _rotMatrixZ = Matrix.CreateRotationZ(Rotator);
                        break;
                }
            }
            else
            {
                switch (RotationAxis)
                {
                    default:
                        _rotMatrixX = Matrix.Identity;
                        break;
                    case 2:
                        _rotMatrixY = Matrix.Identity;
                        break;
                    case 3:
                        _rotMatrixZ = Matrix.Identity;
                        break;
                }
            }
        }

        private void ResetLostSubpart()
        {
            subpart.Subparts.Clear();
            block.TryGetSubpart("Empty", out subpart);
        }
    }
}