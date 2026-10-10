using System;

namespace ApocaGearbox
{
    // Rise time is the duration of a full 0-to-1 pedal movement. Reductions
    // follow the driver's command immediately, including partial analog input.
    internal sealed class ThrottleRamp
    {
        internal float Value { get; private set; }

        internal void Reset() { Value = 0f; }

        internal float Step(float target, float riseSeconds, float deltaTime)
        {
            if (!TuneData.Finite(target)) { Reset(); return Value; }
            target = Math.Max(0f, Math.Min(1f, target));
            if (!TuneData.Finite(riseSeconds) || riseSeconds <= 0f || riseSeconds > 3f)
                return Value = target;
            if (target <= Value) return Value = target;
            if (!TuneData.Finite(deltaTime) || deltaTime <= 0f) return Value;
            return Value = Math.Min(target, Value + deltaTime / riseSeconds);
        }
    }
}
