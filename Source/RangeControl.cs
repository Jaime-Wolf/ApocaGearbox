using System;
using NWH.VehiclePhysics2;
using NWH.VehiclePhysics2.Powertrain;

namespace ApocaGearbox
{
    internal static class RangeControl
    {
        internal static bool CanChange(VehicleController vehicle, out string error)
        {
            error = null;
            try
            {
                if (vehicle == null || vehicle.powertrain == null || vehicle.powertrain.transmission == null)
                { error = "The vehicle's gearbox is not available for a range change."; return false; }
                TransmissionComponent transmission = vehicle.powertrain.transmission;
                if (transmission.transmissionType == TransmissionComponent.TransmissionShiftType.CVT)
                { error = "Continuously variable gearboxes are not supported."; return false; }

                // Native Speed is only absolute forward velocity. Use full
                // chassis velocity so sideways sliding also blocks a switch.
                return RangeMath.CanSwitch(vehicle.VelocityMagnitude * 3.6f,
                    transmission.Gear, transmission.isShifting, out error);
            }
            catch (Exception)
            {
                error = "The drivetrain is not ready for a range change. Range was kept.";
                return false;
            }
        }
    }
}
