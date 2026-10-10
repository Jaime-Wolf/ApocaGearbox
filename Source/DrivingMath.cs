using System;

namespace ApocaGearbox
{
    // Physical forecasts, independent of the tachometer and Unity object state.
    public static class DrivingMath
    {
        public static float ProjectedRpm(float shaftRadiansPerSecond, float totalGearRatio)
        {
            if (!TuneData.Finite(shaftRadiansPerSecond) || !TuneData.Finite(totalGearRatio) || totalGearRatio == 0f)
                return Single.NaN;
            return Math.Abs(shaftRadiansPerSecond * totalGearRatio * (60f / (2f * (float)Math.PI)));
        }

        public static float SpeedPowerMultiplier(float forwardSpeedKph, float limitKph)
        {
            if (!TuneData.Finite(forwardSpeedKph) || !TuneData.Finite(limitKph) || limitKph <= 0f) return 1f;
            // Ease into the limit, leaving brakes and engine RPM to native physics.
            return Math.Max(0f, Math.Min(1f, (limitKph - forwardSpeedKph) / 5f));
        }
    }
}
