using System;

namespace ApocaGearbox
{
    internal sealed class CalculatorInputs
    {
        internal float Radius, Mass, PeakPowerKw, PeakTorqueNm, PowerRpm, BandLowRpm, IdleRpm, RedlineRpm;
        internal float DrivenTorqueCapacity, DragCoefficient, RollingForce;
        internal bool DragKnown;
    }

    internal sealed class CalculatorResult
    {
        internal TuneData Tune;
        internal float ShiftRpm, DownshiftRpm, RequiredPowerKw;
        internal string Warning;
    }

    internal static class GearCalculatorMath
    {
        internal static float Speed(float rpm, float radius, float ratio, float finalDrive, float range)
        {
            if (!Positive(rpm) || !Positive(radius) || !Positive(ratio) || !Positive(finalDrive) || !Positive(range)) return 0f;
            return (float)(rpm * 2.0 * Math.PI * radius * 0.06 / (ratio * finalDrive * range));
        }

        internal static bool Positive(float value) { return TuneData.Finite(value) && value > 0f; }
        internal static float Clamp(float value, float low, float high) { return Math.Max(low, Math.Min(high, value)); }

        internal static bool Calculate(TuneData source, CalculatorInputs input, float targetKph,
            int profile, bool chooseFinalDrive, out CalculatorResult result, out string error)
        { return Calculate(source, input, targetKph, profile, chooseFinalDrive, 100f, out result, out error); }

        internal static bool Calculate(TuneData source, CalculatorInputs input, float targetKph,
            int profile, bool chooseFinalDrive, float launchPercent, out CalculatorResult result, out string error)
        {
            result = null; error = null;
            if (source == null || !source.Validate(null, out error)) return false;
            if (input == null || !Positive(input.Radius) || !Positive(input.Mass) || !Positive(input.PeakPowerKw) ||
                !Positive(input.PeakTorqueNm) || !Positive(input.PowerRpm) || !Positive(input.BandLowRpm) ||
                !Positive(input.RedlineRpm) || !Positive(input.DrivenTorqueCapacity) || !Positive(input.IdleRpm) ||
                input.IdleRpm >= input.RedlineRpm || !TuneData.Finite(input.DragCoefficient) || input.DragCoefficient < 0f ||
                !TuneData.Finite(input.RollingForce) || input.RollingForce < 0f)
            { error = "The engine, tire or vehicle readings are not ready. Try again with the vehicle stopped on the ground."; return false; }
            if (!TuneData.Finite(targetKph) || targetKph < 20f || targetKph > 500f || profile < 0 || profile > 2)
            { error = "Choose a target speed between 20 and 500 km/h."; return false; }
            if (!TuneData.Finite(launchPercent) || launchPercent < 50f || launchPercent > 200f)
            { error = "Launch gearing must be between 50 and 200 percent."; return false; }

            int count = 0;
            foreach (float ratio in source.Ratios) if (ratio > 0f) count++;
            float shift = Clamp(input.PowerRpm, input.IdleRpm * 1.25f, input.RedlineRpm * 0.95f);
            if (shift <= input.IdleRpm) { error = "This engine does not have a usable RPM range."; return false; }
            float topTotal = (float)(shift * 2.0 * Math.PI * input.Radius * 0.06 / targetKph);
            float powerToWeight = input.PeakPowerKw * 1000f / input.Mass;
            float acceleration = new float[] { 1.5f, 2.5f, 3.5f }[profile] *
                Clamp((float)Math.Sqrt(powerToWeight / 50f), 0.5f, 1.5f);
            float tractionMargin = new float[] { 0.65f, 0.8f, 0.95f }[profile];
            float launchTotal = input.Mass * acceleration * input.Radius / input.PeakTorqueNm;
            float tractionTotal = input.DrivenTorqueCapacity * tractionMargin / input.PeakTorqueNm;
            float minimumFirst = topTotal * (float)Math.Pow(1.12, count - 1);
            // Peak engine torque and conservative acceleration alone can make
            // every gear nearly as tall as top gear. Give first gear a usable
            // road-speed ceiling before adding extra reduction for weak/heavy
            // setups. Boost cannot collapse this baseline launch gearing.
            float launchSpeed = new float[] { 65f, 55f, 45f }[profile];
            float roadFirst = (float)(shift * 2.0 * Math.PI * input.Radius * 0.06 / launchSpeed);
            float requestedFirst = Math.Max(roadFirst, Math.Min(launchTotal, tractionTotal)) * launchPercent / 100f;
            float bandLow = Clamp(input.BandLowRpm, input.IdleRpm * 1.25f, shift / 1.12f);
            float widestStep = Clamp(shift / bandLow, 1.12f, 2f);
            float maximumFirst = topTotal * (float)Math.Pow(widestStep, count - 1);
            float firstTotal = Math.Max(minimumFirst, requestedFirst);
            if (count == 1) firstTotal = topTotal;

            TuneData tune = source.Clone();
            if (chooseFinalDrive)
            {
                float topAnchor = count <= 4 ? 1f : (count <= 5 ? 0.75f : 0.65f);
                tune.FinalDrive = Clamp(topTotal / topAnchor, 1f, 8f);
            }
            float step = count > 1 ? (float)Math.Pow(topTotal / firstTotal, 1.0 / (count - 1)) : 1f;
            int gear = 0;
            for (int i = 0; i < tune.Ratios.Length; i++)
                if (tune.Ratios[i] > 0f)
                {
                    // Set the last ratio directly to avoid accumulating error in
                    // the requested speed/RPM relationship. Neutral and reverse stay intact.
                    tune.Ratios[i] = (++gear == count ? topTotal : firstTotal * (float)Math.Pow(step, gear - 1)) / tune.FinalDrive;
                }
            if (!tune.Validate(source.Ratios, out error)) return false;
            float v = targetKph / 3.6f;
            float required = (input.DragCoefficient * v * v * v + input.RollingForce * v) / 1000f;
            if (!TuneData.Finite(required)) { error = "The road-load estimate is invalid."; return false; }
            string warning = "";
            if (firstTotal > tractionTotal * 1.01f)
                warning += "This speed target needs more launch reduction than the current tires can comfortably handle. Wheelspin is possible. ";
            if (count > 1 && firstTotal > maximumFirst * 1.01f)
                warning += "This target and launch gearing create wide RPM drops between gears. Reduce the target speed or launch gearing if the engine struggles after shifting. ";
            if (input.DragKnown && required > input.PeakPowerKw)
                warning += "The estimated road load exceeds available engine power at this target. Taller gearing cannot supply the missing power. ";
            if (!input.DragKnown) warning += "Air-resistance data is unavailable; maximum road speed cannot be estimated. ";
            result = new CalculatorResult { Tune = tune, ShiftRpm = shift,
                DownshiftRpm = Math.Max(input.IdleRpm * 1.2f, shift * step * 0.72f),
                RequiredPowerKw = required, Warning = warning.Trim() };
            return true;
        }
    }
}
