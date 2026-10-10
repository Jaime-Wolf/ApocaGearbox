using System;
using System.Globalization;
using System.Text;

namespace ApocaGearbox
{
    // Independent of Unity so saved data can be checked before touching a gearbox.
    public sealed class TuneData
    {
        public float[] Ratios;
        public float FinalDrive;
        public float ShiftUp;
        public float ShiftDown;
        public bool TuneShifts;
        public bool CompensateBoost = true;
        public float SpeedLimitKph;
        public float EngineBrakingStrength;
        public float BrakeStrength = 1f;
        public float HandbrakeStrength = 1f;
        public float ThrottleRiseSeconds;
        public float RevLimitRpm;
        public bool LowRangeEnabled;
        public float LowRangeRatio = 2.5f;
        public bool LowRangeSelected;
        // Null preserves vehicle availability for tunes made before this option.
        public bool? DriveSelectorEnabled;

        public TuneData Clone()
        {
            return new TuneData {
                Ratios = Ratios == null ? null : (float[])Ratios.Clone(),
                FinalDrive = FinalDrive, ShiftUp = ShiftUp, ShiftDown = ShiftDown,
                TuneShifts = TuneShifts, CompensateBoost = CompensateBoost,
                SpeedLimitKph = SpeedLimitKph,
                EngineBrakingStrength = EngineBrakingStrength,
                BrakeStrength = BrakeStrength, HandbrakeStrength = HandbrakeStrength,
                ThrottleRiseSeconds = ThrottleRiseSeconds,
                RevLimitRpm = RevLimitRpm,
                LowRangeEnabled = LowRangeEnabled, LowRangeRatio = LowRangeRatio,
                LowRangeSelected = LowRangeSelected, DriveSelectorEnabled = DriveSelectorEnabled
            };
        }

        // Tunes are mutable for the editor and native final-drive observer. A
        // reusable snapshot detects those changes without parsing or allocating
        // on every physics callback, including an invalid NaN edited in place.
        internal bool SameValues(TuneData other)
        {
            if (other == null || !FinalDrive.Equals(other.FinalDrive) || !ShiftUp.Equals(other.ShiftUp) ||
                !ShiftDown.Equals(other.ShiftDown) || TuneShifts != other.TuneShifts ||
                CompensateBoost != other.CompensateBoost || !SpeedLimitKph.Equals(other.SpeedLimitKph) ||
                !EngineBrakingStrength.Equals(other.EngineBrakingStrength) || !BrakeStrength.Equals(other.BrakeStrength) ||
                !HandbrakeStrength.Equals(other.HandbrakeStrength) || !ThrottleRiseSeconds.Equals(other.ThrottleRiseSeconds) ||
                !RevLimitRpm.Equals(other.RevLimitRpm) || LowRangeEnabled != other.LowRangeEnabled ||
                !LowRangeRatio.Equals(other.LowRangeRatio) || LowRangeSelected != other.LowRangeSelected ||
                DriveSelectorEnabled != other.DriveSelectorEnabled) return false;
            if (Ratios == null || other.Ratios == null) return Ratios == other.Ratios;
            if (Ratios.Length != other.Ratios.Length) return false;
            for (int i = 0; i < Ratios.Length; i++) if (!Ratios[i].Equals(other.Ratios[i])) return false;
            return true;
        }

        public bool Validate(float[] expectedLayout, out string error)
        {
            error = null;
            if (Ratios == null || Ratios.Length < 2 || Ratios.Length > 32)
                error = "This gearbox has an unsupported number of gears.";
            else if (expectedLayout != null && expectedLayout.Length != Ratios.Length)
                error = "This engine's saved tune has a different gear count. Reset it before tuning this gearbox.";
            else if (!Finite(FinalDrive) || FinalDrive < 0.05f || FinalDrive > 100f)
                error = "Final drive must be between 0.05 and 100.";
            else if (!Finite(ShiftUp) || !Finite(ShiftDown) || ShiftUp < 0f || ShiftDown < 0f || ShiftUp > 30000f || ShiftDown > 30000f)
                error = "Shift RPM must be a finite value between 0 and 30000.";
            else if (TuneShifts && (ShiftUp <= ShiftDown || ShiftUp < 100f))
                error = "Upshift RPM must be at least 100 and greater than downshift RPM.";
            else if (!Finite(SpeedLimitKph) || SpeedLimitKph < 0f || SpeedLimitKph > 500f || (SpeedLimitKph > 0f && SpeedLimitKph < 10f))
                error = "Speed limit must be 0 (off), or between 10 and 500 km/h.";
            else if (!Finite(EngineBrakingStrength) || EngineBrakingStrength < 0f || EngineBrakingStrength > 5f)
                error = "Extra engine braking must be between 0 (native) and 5.";
            else if (!Finite(BrakeStrength) || BrakeStrength < 0.1f || BrakeStrength > 5f ||
                !Finite(HandbrakeStrength) || HandbrakeStrength < 0.1f || HandbrakeStrength > 5f)
                error = "Brake and handbrake strength must be between 0.1 and 5 times normal.";
            else if (!Finite(ThrottleRiseSeconds) || ThrottleRiseSeconds < 0f || ThrottleRiseSeconds > 3f)
                error = "Throttle buildup must be between 0 (instant) and 3 seconds.";
            else if (!Finite(LowRangeRatio) || LowRangeRatio < 1f || LowRangeRatio > 10f)
                error = "Low range reduction must be between 1 and 10.";
            else if (!Finite(RevLimitRpm) || RevLimitRpm < 0f || RevLimitRpm > 15000f || (RevLimitRpm > 0f && RevLimitRpm < 1000f))
                error = "Rev limit must be 0 (native), or between 1000 and 15000 RPM.";
            else if (TuneShifts && RevLimitRpm > 0f && ShiftUp >= RevLimitRpm)
                error = "Set automatic upshift RPM below the selected rev limit.";
            else if (LowRangeSelected && !LowRangeEnabled)
                error = "Enable high / low range before selecting low range.";
            if (error != null) return false;

            int neutrals = 0, forwards = 0;
            bool seenNeutral = false;
            for (int i = 0; i < Ratios.Length; i++)
            {
                float ratio = Ratios[i];
                if (expectedLayout != null && !Finite(expectedLayout[i]))
                { error = "The native gearbox has a non-finite ratio. Custom tuning was not applied."; return false; }
                if (!Finite(ratio) || Math.Abs(ratio) > 100f || (ratio != 0f && Math.Abs(ratio) < 0.05f))
                { error = "Gear ratios must be between 0.05 and 100 in magnitude."; return false; }
                if (expectedLayout != null && Math.Sign(ratio) != Math.Sign(expectedLayout[i]))
                { error = "Gear layout changed; reverse, neutral and forward gears must keep their positions."; return false; }
                if (ratio == 0f) { neutrals++; seenNeutral = true; }
                else if (ratio > 0f)
                {
                    if (!seenNeutral) { error = "Forward gears must follow neutral."; return false; }
                    forwards++;
                }
                else if (seenNeutral)
                { error = "Reverse gears must precede neutral."; return false; }
            }
            if (neutrals != 1 || forwards == 0)
            { error = "The gearbox needs exactly one neutral and at least one forward gear."; return false; }
            return true;
        }

        public string Serialize()
        {
            StringBuilder b = new StringBuilder("9;");
            if (Ratios != null)
                for (int i = 0; i < Ratios.Length; i++)
                {
                    if (i != 0) b.Append(',');
                    b.Append(Ratios[i].ToString("R", CultureInfo.InvariantCulture));
                }
            b.Append(';').Append(FinalDrive.ToString("R", CultureInfo.InvariantCulture));
            b.Append(';').Append(ShiftUp.ToString("R", CultureInfo.InvariantCulture));
            b.Append(';').Append(ShiftDown.ToString("R", CultureInfo.InvariantCulture));
            b.Append(';').Append(TuneShifts ? "1" : "0");
            b.Append(';').Append(CompensateBoost ? "1" : "0");
            // Reserved former shift-limiter flag. Retain its position so every
            // earlier save/preset schema remains readable, always writing off.
            b.Append(';').Append('0');
            b.Append(';').Append(SpeedLimitKph.ToString("R", CultureInfo.InvariantCulture));
            b.Append(';').Append(EngineBrakingStrength.ToString("R", CultureInfo.InvariantCulture));
            b.Append(';').Append(LowRangeEnabled ? "1" : "0");
            b.Append(';').Append(LowRangeRatio.ToString("R", CultureInfo.InvariantCulture));
            b.Append(';').Append(LowRangeSelected ? "1" : "0");
            b.Append(';').Append(DriveSelectorEnabled.HasValue ? (DriveSelectorEnabled.Value ? "1" : "0") : "-");
            b.Append(';').Append(ThrottleRiseSeconds.ToString("R", CultureInfo.InvariantCulture));
            b.Append(';').Append(RevLimitRpm.ToString("R", CultureInfo.InvariantCulture));
            // Reserved former traction-control field. Preserve old save/preset
            // positions, ignore its old value on load and always write off.
            b.Append(';').Append('0');
            b.Append(';').Append(BrakeStrength.ToString("R", CultureInfo.InvariantCulture));
            b.Append(';').Append(HandbrakeStrength.ToString("R", CultureInfo.InvariantCulture));
            return b.ToString();
        }

        public static bool TryParse(string text, out TuneData tune, out string error)
        {
            tune = null; error = "Saved gearbox tuning is invalid.";
            if (String.IsNullOrEmpty(text) || text.Length > 8192) return false;
            string[] parts = text.Split(';');
            bool legacy = parts.Length == 6 && parts[0] == "1";
            bool versionTwo = parts.Length == 9 && parts[0] == "2";
            bool versionThree = parts.Length == 10 && parts[0] == "3";
            bool versionFour = parts.Length == 13 && parts[0] == "4";
            bool versionFive = parts.Length == 14 && parts[0] == "5";
            bool versionSix = parts.Length == 15 && parts[0] == "6";
            bool versionSeven = parts.Length == 16 && parts[0] == "7";
            bool versionEight = parts.Length == 17 && parts[0] == "8";
            bool current = parts.Length == 19 && parts[0] == "9";
            if ((!legacy && !versionTwo && !versionThree && !versionFour && !versionFive && !versionSix && !versionSeven && !versionEight && !current) || (parts[5] != "0" && parts[5] != "1")) return false;
            string[] ratios = parts[1].Split(',');
            if (ratios.Length > 32) return false;
            TuneData candidate = new TuneData { Ratios = new float[ratios.Length], TuneShifts = parts[5] == "1" };
            if (current || versionEight || versionSeven || versionSix || versionFive || versionFour || versionThree || versionTwo)
            {
                if ((parts[6] != "0" && parts[6] != "1") || (parts[7] != "0" && parts[7] != "1") || !ReadFloat(parts[8], out candidate.SpeedLimitKph)) return false;
                candidate.CompensateBoost = parts[6] == "1";
                // parts[7] belonged to the removed shift limiter; ignore it.
            }
            if ((current || versionEight || versionSeven || versionSix || versionFive || versionFour || versionThree) && !ReadFloat(parts[9], out candidate.EngineBrakingStrength)) return false;
            if (current || versionEight || versionSeven || versionSix || versionFive || versionFour)
            {
                if ((parts[10] != "0" && parts[10] != "1") || (parts[12] != "0" && parts[12] != "1") ||
                    !ReadFloat(parts[11], out candidate.LowRangeRatio)) return false;
                candidate.LowRangeEnabled = parts[10] == "1";
                candidate.LowRangeSelected = parts[12] == "1";
            }
            if (current || versionEight || versionSeven || versionSix || versionFive)
            {
                if (parts[13] != "-" && parts[13] != "0" && parts[13] != "1") return false;
                candidate.DriveSelectorEnabled = parts[13] == "-" ? (bool?)null : parts[13] == "1";
            }
            if ((current || versionEight || versionSeven || versionSix) && !ReadFloat(parts[14], out candidate.ThrottleRiseSeconds)) return false;
            if ((current || versionEight || versionSeven) && !ReadFloat(parts[15], out candidate.RevLimitRpm)) return false;
            // parts[16] belonged to removed traction control; ignore it.
            if (current && (!ReadFloat(parts[17], out candidate.BrakeStrength) ||
                !ReadFloat(parts[18], out candidate.HandbrakeStrength))) return false;
            for (int i = 0; i < ratios.Length; i++)
                if (!ReadFloat(ratios[i], out candidate.Ratios[i])) return false;
            if (!ReadFloat(parts[2], out candidate.FinalDrive) || !ReadFloat(parts[3], out candidate.ShiftUp) || !ReadFloat(parts[4], out candidate.ShiftDown)) return false;
            if (!candidate.Validate(null, out error)) return false;
            tune = candidate; error = null; return true;
        }

        public static bool ReadFloat(string value, out float result)
        {
            return Single.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) && Finite(result);
        }

        public static bool Finite(float value) { return !Single.IsNaN(value) && !Single.IsInfinity(value); }
    }
}
