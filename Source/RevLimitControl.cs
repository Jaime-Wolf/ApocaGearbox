using System;
using NWH.VehiclePhysics2.Powertrain;

namespace ApocaGearbox
{
    // Owns the native cutoff setting, never writes engine RPM or blocks shifts.
    internal sealed class RevLimitControl
    {
        private EngineComponent engine;
        private float baseline, lastApplied;
        private bool wrote;

        internal static bool Validate(float requested, float idle, out string error)
        {
            error = null;
            if (requested == 0f) return true;
            if (!TuneData.Finite(idle) || idle <= 0f)
                error = "This engine's idle RPM could not be read.";
            else if (!TuneData.Finite(requested) || requested < Math.Max(1000f, idle * 1.4f) || requested > 15000f)
                error = "Rev limit must be between " + Math.Ceiling(Math.Max(1000f, idle * 1.4f)).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " and 15000 RPM for this engine.";
            return error == null;
        }

        internal void Update(EngineComponent current, float requested, float factoryLimit)
        {
            bool changed = !System.Object.ReferenceEquals(engine, current);
            if (changed)
            {
                Restore(); engine = current;
                baseline = Valid(factoryLimit) ? factoryLimit : engine != null ? engine.revLimiterRPM : 0f;
            }
            string error;
            if (!changed && !wrote && engine != null && Valid(engine.revLimiterRPM)) baseline = engine.revLimiterRPM;
            if (engine == null || engine.engineType != EngineComponent.EngineType.ICE || requested == 0f ||
                !Validate(requested, engine.idleRPM, out error)) { RestoreValue(); return; }
            // Preserve a newer native/other-mod setting as the restore baseline.
            if (wrote && engine.revLimiterRPM != lastApplied && Valid(engine.revLimiterRPM))
                baseline = engine.revLimiterRPM;
            if (engine.revLimiterRPM == requested) return;
            engine.revLimiterRPM = requested; lastApplied = requested; wrote = true;
            RefreshPowerCache();
        }

        private static bool Valid(float rpm)
        { return TuneData.Finite(rpm) && rpm > 0f && rpm <= 30000f; }

        private void RefreshPowerCache()
        { if (engine != null && engine.powerCurve != null) engine.UpdatePeakPowerAndTorque(); }

        private void RestoreValue()
        {
            if (engine != null && wrote && engine.revLimiterRPM == lastApplied && Valid(baseline))
            { engine.revLimiterRPM = baseline; RefreshPowerCache(); }
            wrote = false;
        }

        // A temporarily invalid gearbox still belongs to this engine component.
        // Retaining it lets recovery capture a newer native/other-mod baseline.
        internal void RestoreOverride() { RestoreValue(); }
        internal void Restore() { RestoreValue(); engine = null; baseline = lastApplied = 0f; }
        internal void Forget() { engine = null; baseline = lastApplied = 0f; wrote = false; }
    }
}
