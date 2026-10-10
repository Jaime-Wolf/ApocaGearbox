using System;
using System.Collections.Generic;
using NWH.VehiclePhysics2.Powertrain;
using UnityEngine;

namespace ApocaGearbox
{
    internal static class WheelShaft
    {
        private static float nextWarning;

        // NWH stores an engine-derived estimate in the transmission and
        // differential angular-velocity fields. Read the actual wheel sensors
        // using the selected vehicle drive route, or the native branch average.
        internal static bool TryWheelShaftAngularVelocity(TransmissionComponent transmission, out float angularVelocity)
        {
            angularVelocity = 0f;
            try
            {
                if (transmission == null || transmission.outputNameHash == 0) return false;
                if (DriveControl.TryWheelShaftAngularVelocity(transmission, out angularVelocity)) return true;
                return TryReadShaft(transmission.Output, new HashSet<PowertrainComponent>(), 0, out angularVelocity);
            }
            catch (Exception error)
            {
                angularVelocity = 0f;
                WarnFailure(error);
                return false;
            }
        }

        private static bool TryReadShaft(PowertrainComponent component, HashSet<PowertrainComponent> path,
            int depth, out float angularVelocity)
        {
            angularVelocity = 0f;
            if (component == null || depth >= 16 || !path.Add(component)) return false;
            try
            {
                WheelComponent wheel = component as WheelComponent;
                if (wheel != null)
                {
                    if (wheel.wheelUAPI == null) return false;
                    angularVelocity = wheel.wheelUAPI.AngularVelocity;
                    return TuneData.Finite(angularVelocity);
                }

                DifferentialComponent differential = component as DifferentialComponent;
                if (differential == null || differential.outputNameHash == 0 || differential.outputBNameHash == 0)
                    return false;
                float a, b;
                if (!TryReadShaft(differential.Output, path, depth + 1, out a) ||
                    !TryReadShaft(differential.OutputB, path, depth + 1, out b)) return false;
                angularVelocity = (a + b) * 0.5f;
                return TuneData.Finite(angularVelocity);
            }
            finally { path.Remove(component); }
        }

        private static void WarnFailure(Exception error)
        {
            if (Time.unscaledTime < nextWarning) return;
            nextWarning = Time.unscaledTime + 5f;
            if (Plugin.Log != null)
                Plugin.Log.LogWarning("Wheel RPM diagnostic skipped an unavailable drivetrain: " + error.GetType().Name + ": " + error.Message);
        }
    }
}
