namespace ApocaGearbox
{
    // Calculation can replace invalid old shift settings after lowering the
    // limiter. Manual edits still receive the normal full tune validation.
    internal static class CalculatorDraftEdits
    {
        internal static void PrepareInput(TuneData input, bool replaceShifts)
        {
            if (!replaceShifts) return;
            input.TuneShifts = false;
            input.ShiftUp = input.ShiftDown = 0f;
        }

        internal static bool TryUse(TuneData input, CalculatorResult result, bool replaceShifts,
            out TuneData output, out string error)
        {
            output = null;
            if (input == null || result == null || result.Tune == null)
            { error = "Calculate again before using the suggestion."; return false; }
            TuneData candidate = input.Clone();
            candidate.Ratios = (float[])result.Tune.Ratios.Clone();
            candidate.FinalDrive = result.Tune.FinalDrive;
            if (replaceShifts)
            {
                candidate.TuneShifts = true;
                candidate.ShiftUp = result.ShiftRpm;
                candidate.ShiftDown = result.DownshiftRpm;
            }
            if (!candidate.Validate(input.Ratios, out error)) return false;
            output = candidate;
            return true;
        }
    }

    internal sealed class CalculatorFreshnessCache
    {
        private float checkedAt;
        private bool hasCheck;
        internal bool Matches { get; private set; }

        internal bool Due(float now)
        { return !hasCheck || now < checkedAt || now - checkedAt >= 0.25f; }

        internal void Record(float now, bool matches)
        {
            // A materially changed setup stays invalid until Calculate starts
            // a new snapshot. Returning sensor values must not blink the button.
            Matches = matches && (!hasCheck || Matches);
            checkedAt = now; hasCheck = true;
        }

        internal void Clear() { hasCheck = false; Matches = false; }
    }
}
