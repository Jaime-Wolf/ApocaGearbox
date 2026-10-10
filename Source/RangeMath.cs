using System;

namespace ApocaGearbox
{
    // Transfer reduction is applied once to the runtime gear list, using the
    // saved base ratios each time. High range leaves the base tune unchanged.
    public static class RangeMath
    {
        public const float MinimumReduction = 1f;
        public const float MaximumReduction = 10f;
        public const float StoppedSpeedKph = 1f;

        public static float Factor(bool enabled, bool selected, float reduction)
        {
            if (!enabled || !selected || !TuneData.Finite(reduction) ||
                reduction < MinimumReduction || reduction > MaximumReduction) return 1f;
            return reduction;
        }

        public static bool CanSwitch(float speedKph, int gear, bool isShifting, out string error)
        {
            error = null;
            if (!TuneData.Finite(speedKph))
                error = "The vehicle's speed could not be verified. Range was kept.";
            else if (Math.Abs(speedKph) > StoppedSpeedKph)
                error = "Stop the vehicle before changing range.";
            else if (isShifting)
                error = "Wait for the gear change to finish before changing range.";
            else if (gear != 0)
                error = "Select neutral before changing range.";
            return error == null;
        }
    }
}
