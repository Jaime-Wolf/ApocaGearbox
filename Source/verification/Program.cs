using System;
using System.Globalization;
using System.Threading;
using ApocaGearbox;

internal static class Program
{
    private static int passed;
    private static int failed;
    private static readonly float[] Stock = { -3.2f, 0f, 3.5f, 2.1f, 1.4f, 1f, 0.8f };

    private static TuneData ValidTune()
    {
        return new TuneData {
            Ratios = new float[] { -2.987654f, 0f, 3.456789f, 2.123456f, 1.345678f, 0.987654f, 0.7654321f },
            FinalDrive = 3.876543f, ShiftUp = 6543.21f, ShiftDown = 2345.678f, TuneShifts = true,
            CompensateBoost = false, SpeedLimitKph = 150.123f,
            EngineBrakingStrength = 2.678901f,
            LowRangeEnabled = true, LowRangeRatio = 2.567891f, LowRangeSelected = true,
            DriveSelectorEnabled = true, ThrottleRiseSeconds = 0.7654321f
        };
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Run(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }

    private static void AssertValid(TuneData tune, float[] layout)
    {
        string error;
        Assert(tune.Validate(layout, out error), error ?? "Expected a valid tune.");
        Assert(error == null, "Valid data retained an error message.");
    }

    private static void AssertRejected(TuneData tune, float[] layout)
    {
        string error;
        Assert(!tune.Validate(layout, out error), "Invalid tune was accepted.");
        Assert(!String.IsNullOrEmpty(error), "Rejected tune has no explanation.");
    }

    private static void RejectMutation(string name, Action<TuneData> mutation)
    {
        Run(name, delegate { TuneData tune = ValidTune(); mutation(tune); AssertRejected(tune, Stock); });
    }

    private static void AssertSame(TuneData expected, TuneData actual)
    {
        Assert(actual != null, "Parsed tune is null.");
        Assert(expected.Ratios.Length == actual.Ratios.Length, "Gear count changed.");
        for (int i = 0; i < expected.Ratios.Length; i++)
            Assert(expected.Ratios[i] == actual.Ratios[i], "Ratio " + i + " lost precision.");
        Assert(expected.FinalDrive == actual.FinalDrive, "Final drive lost precision.");
        Assert(expected.ShiftUp == actual.ShiftUp, "Upshift RPM lost precision.");
        Assert(expected.ShiftDown == actual.ShiftDown, "Downshift RPM lost precision.");
        Assert(expected.TuneShifts == actual.TuneShifts, "Shift tuning flag changed.");
        Assert(expected.CompensateBoost == actual.CompensateBoost, "Boost compensation flag changed.");
        Assert(expected.SpeedLimitKph == actual.SpeedLimitKph, "Road speed limit lost precision.");
        Assert(expected.EngineBrakingStrength == actual.EngineBrakingStrength, "Engine braking strength lost precision.");
        Assert(expected.LowRangeEnabled == actual.LowRangeEnabled, "Low range availability changed.");
        Assert(expected.LowRangeRatio == actual.LowRangeRatio, "Low range reduction lost precision.");
        Assert(expected.LowRangeSelected == actual.LowRangeSelected, "Saved range selection changed.");
        Assert(expected.DriveSelectorEnabled == actual.DriveSelectorEnabled, "Engine selector availability changed.");
        Assert(expected.ThrottleRiseSeconds == actual.ThrottleRiseSeconds, "Throttle buildup changed.");
        Assert(expected.RevLimitRpm == actual.RevLimitRpm, "Rev limit changed.");
        Assert(expected.BrakeStrength == actual.BrakeStrength, "Brake strength changed.");
        Assert(expected.HandbrakeStrength == actual.HandbrakeStrength, "Handbrake strength changed.");
    }

    private static void AssertParseRejected(string payload)
    {
        TuneData parsed;
        string error;
        Assert(!TuneData.TryParse(payload, out parsed, out error), "Malformed saved data was accepted.");
        Assert(parsed == null, "Failed parsing returned usable data.");
        Assert(!String.IsNullOrEmpty(error), "Failed parsing has no explanation.");
    }

    private static int Main()
    {
        Run("valid tune and matching native layout", delegate { AssertValid(ValidTune(), Stock); });
        Run("invariant serialization and full precision in four locales", delegate {
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            CultureInfo previousUi = Thread.CurrentThread.CurrentUICulture;
            try {
                TuneData original = ValidTune();
                string expected = original.Serialize();
                string[] locales = { "en-US", "de-DE", "fr-FR", "tr-TR" };
                foreach (string locale in locales) {
                    Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo(locale);
                    Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
                    Assert(original.Serialize() == expected, locale + " changed serialization.");
                    TuneData parsed; string error;
                    Assert(TuneData.TryParse(expected, out parsed, out error), locale + " could not parse: " + error);
                    AssertSame(original, parsed);
                    AssertValid(parsed, Stock);
                }
            }
            finally {
                Thread.CurrentThread.CurrentCulture = previous;
                Thread.CurrentThread.CurrentUICulture = previousUi;
            }
        });
        Run("clone owns a separate gear array", delegate {
            TuneData original = ValidTune(), clone = original.Clone();
            AssertSame(original, clone);
            clone.Ratios[2] = 7f;
            clone.FinalDrive = 9f;
            Assert(original.Ratios[2] == 3.456789f && original.FinalDrive == 3.876543f, "Clone changed its source.");
            clone.LowRangeSelected = false; clone.LowRangeRatio = 4f;
            Assert(original.LowRangeSelected && original.LowRangeRatio == 2.567891f, "Clone changed source range settings.");
        });
        Run("untuned native shift snapshots may use zero RPM", delegate {
            TuneData native = ValidTune(); native.TuneShifts = false; native.ShiftUp = 0f; native.ShiftDown = 0f;
            AssertValid(native, Stock);
            TuneData parsed; string error;
            Assert(TuneData.TryParse(native.Serialize(), out parsed, out error), error);
            AssertSame(native, parsed);
        });
        Run("two distinct native gearbox counts are supported", delegate {
            float[][] nativeLayouts = {
                new float[] { -3f, 0f, 2.5f },
                new float[] { -4f, -2f, 0f, 4f, 2.5f, 1.5f, 1f, 0.75f }
            };
            foreach (float[] layout in nativeLayouts) {
                TuneData tune = ValidTune(); tune.Ratios = (float[])layout.Clone();
                AssertValid(tune, layout);
                TuneData parsed; string error;
                Assert(TuneData.TryParse(tune.Serialize(), out parsed, out error), error);
                AssertValid(parsed, layout);
                AssertRejected(parsed, Stock);
            }
        });
        Run("different engine gear count rejected", delegate {
            AssertRejected(ValidTune(), new float[] { -3f, 0f, 3f, 2f, 1f });
        });
        Run("nonfinite native ratios rejected without throwing", delegate {
            float[] corruptValues = { Single.NaN, Single.PositiveInfinity, Single.NegativeInfinity };
            foreach (float corrupt in corruptValues) {
                float[] layout = (float[])Stock.Clone(); layout[2] = corrupt;
                AssertRejected(ValidTune(), layout);
            }
        });
        Run("legacy v1 saves gain safe defaults and migrate to v6", delegate {
            TuneData parsed; string error;
            Assert(TuneData.TryParse("1;-3,0,3,2,1;3.5;4200;1500;0", out parsed, out error), error);
            Assert(parsed.CompensateBoost && parsed.SpeedLimitKph == 0f && parsed.EngineBrakingStrength == 0f, "Legacy defaults changed.");
            Assert(!parsed.LowRangeEnabled && !parsed.LowRangeSelected && parsed.LowRangeRatio == 2.5f, "Legacy save enabled low range.");
            Assert(parsed.DriveSelectorEnabled == null, "Legacy save could overwrite a vehicle selector.");
            Assert(parsed.Serialize().StartsWith("9;"), "Legacy save did not migrate to v9.");
            TuneData reread; Assert(TuneData.TryParse(parsed.Serialize(), out reread, out error), error); AssertSame(parsed, reread);
        });
        Run("legacy v2 saves preserve driving settings with native braking", delegate {
            TuneData parsed; string error;
            Assert(TuneData.TryParse("2;-3,0,3,2,1;3.5;4200;1500;1;0;1;150", out parsed, out error), error);
            Assert(parsed.TuneShifts && !parsed.CompensateBoost && parsed.SpeedLimitKph == 150f && parsed.EngineBrakingStrength == 0f, "V2 driving settings or native braking migration changed.");
            Assert(!parsed.LowRangeEnabled && !parsed.LowRangeSelected && parsed.LowRangeRatio == 2.5f, "V2 migration changed road gearing.");
            Assert(parsed.DriveSelectorEnabled == null, "V2 migration could overwrite a vehicle selector.");
            TuneData migrated; Assert(TuneData.TryParse(parsed.Serialize(), out migrated, out error), error); AssertSame(parsed, migrated);
        });
        Run("legacy v3 preserves braking and starts in high range", delegate {
            TuneData parsed; string error;
            Assert(TuneData.TryParse("3;-3,0,3,2,1;3.5;4200;1500;1;0;1;150;3", out parsed, out error), error);
            Assert(parsed.TuneShifts && !parsed.CompensateBoost && parsed.SpeedLimitKph == 150f && parsed.EngineBrakingStrength == 3f,
                "V3 migration changed established driving settings.");
            Assert(!parsed.LowRangeEnabled && !parsed.LowRangeSelected && parsed.LowRangeRatio == 2.5f, "V3 migration changed road gearing.");
            Assert(parsed.DriveSelectorEnabled == null, "V3 migration could overwrite a vehicle selector.");
            TuneData migrated; Assert(TuneData.TryParse(parsed.Serialize(), out migrated, out error), error); AssertSame(parsed, migrated);
        });
        Run("old current-schema limiter flags are ignored without changing other settings", delegate {
            foreach (bool boost in new bool[] { false, true }) foreach (bool protect in new bool[] { false, true }) {
                TuneData tune = ValidTune(); tune.CompensateBoost = boost;
                string[] fields=tune.Serialize().Split(';');fields[7]=protect?"1":"0";
                TuneData parsed; string error; Assert(TuneData.TryParse(String.Join(";",fields), out parsed, out error), error); AssertSame(tune, parsed);
                Assert(parsed.Serialize().Split(';')[7]=="0","Removed limiter setting was retained when saving.");
            }
        });
        Run("old schemas 2 through 6 preserve mechanics while retiring their limiter flag", delegate {
            int[] counts={9,10,13,14,15};
            for(int version=2;version<=6;version++)foreach(string flag in new [] {"0","1"}) {
                TuneData original=ValidTune();string[] fields=original.Serialize().Split(';');fields[0]=version.ToString();fields[7]=flag;
                TuneData parsed;string error;Assert(TuneData.TryParse(String.Join(";",fields,0,counts[version-2]),out parsed,out error),error);
                if(version<3)original.EngineBrakingStrength=0f;
                if(version<4){original.LowRangeEnabled=false;original.LowRangeSelected=false;original.LowRangeRatio=2.5f;}
                if(version<5)original.DriveSelectorEnabled=null;
                if(version<6)original.ThrottleRiseSeconds=0f;
                AssertSame(original,parsed);Assert(parsed.Serialize().Split(';')[7]=="0","Legacy limiter flag survived migration.");
            }
        });
        Run("speed limit boundaries include off and 10 through 500", delegate {
            foreach (float limit in new float[] { 0f, 10f, 150f, 500f }) { TuneData tune = ValidTune(); tune.SpeedLimitKph = limit; AssertValid(tune, Stock); }
        });
        Run("engine braking strength permits native zero through maximum five", delegate {
            foreach (float strength in new float[] { 0f, 0.01f, 1f, 2.75f, 5f }) {
                TuneData tune = ValidTune(); tune.EngineBrakingStrength = strength; AssertValid(tune, Stock);
                TuneData parsed; string error; Assert(TuneData.TryParse(tune.Serialize(), out parsed, out error), error); AssertSame(tune, parsed);
            }
        });
        Run("range selection round-trips off, high and low without changing base gearing", delegate {
            foreach (int selection in new int[] { 0, 1, 2 }) {
                TuneData tune = ValidTune(); tune.LowRangeEnabled = selection != 0; tune.LowRangeSelected = selection == 2;
                string saved = tune.Serialize(); Assert(saved.Split(';').Length == 19, "V9 has an unexpected field count.");
                TuneData parsed; string error; Assert(TuneData.TryParse(saved, out parsed, out error), error);
                AssertSame(tune, parsed); AssertValid(parsed, Stock);
                Assert(parsed.Ratios[2] == 3.456789f && parsed.FinalDrive == 3.876543f, "Saved low range was baked into base gearing.");
            }
        });
        Run("low range ratio boundaries include direct drive and ten to one", delegate {
            foreach (float reduction in new float[] { 1f, 1.001f, 2.5f, 10f }) {
                TuneData tune = ValidTune(); tune.LowRangeRatio = reduction; AssertValid(tune, Stock);
                TuneData parsed; string error; Assert(TuneData.TryParse(tune.Serialize(), out parsed, out error), error); AssertSame(tune, parsed);
            }
        });
        Run("new native snapshots leave low range disabled and high selected", delegate {
            TuneData tune = new TuneData();
            Assert(!tune.LowRangeEnabled && !tune.LowRangeSelected && tune.LowRangeRatio == 2.5f, "New tune defaults apply an unsolicited reduction.");
            Assert(tune.DriveSelectorEnabled == null, "Native capture could overwrite an existing vehicle selector.");
        });
        Run("v4 engine saves preserve range without prescribing vehicle selector availability", delegate {
            TuneData original = ValidTune(); string[] fields = original.Serialize().Split(';'); fields[0] = "4";
            string legacy = String.Join(";", fields, 0, 13);
            TuneData parsed; string error; Assert(TuneData.TryParse(legacy, out parsed, out error), error);
            original.DriveSelectorEnabled = null; original.ThrottleRiseSeconds = 0f; AssertSame(original, parsed);
            Assert(parsed.Serialize().StartsWith("9;"), "V4 was not upgraded to V9.");
        });
        Run("selector availability round trips enabled disabled and legacy preserve distinctly", delegate {
            foreach (bool? option in new bool?[] { null, false, true }) {
                TuneData tune = ValidTune(); tune.DriveSelectorEnabled = option; AssertValid(tune, Stock);
                TuneData parsed; string error; Assert(TuneData.TryParse(tune.Serialize(), out parsed, out error), error);
                AssertSame(tune, parsed);
            }
        });
        Run("preset style clones retain selector availability without changing their source", delegate {
            foreach (bool? option in new bool?[] { null, false, true }) {
                TuneData original = ValidTune(); original.DriveSelectorEnabled = option;
                TuneData preset = original.Clone(); preset.FinalDrive = 3.4f; preset.Ratios[2] = 2.8f;
                Assert(preset.DriveSelectorEnabled == option, "A cloned preset lost selector availability.");
                preset.DriveSelectorEnabled = option == true ? (bool?)false : true;
                Assert(original.DriveSelectorEnabled == option && original.Ratios[2] == 3.456789f, "Preset edits changed the existing tune.");
            }
        });
        Run("v5 selector field is required and rejects ambiguous boolean spellings", delegate {
            string[] fields = ValidTune().Serialize().Split(';');
            AssertParseRejected(String.Join(";", fields, 0, 13));
            foreach (string invalid in new string[] { "", "true", "false", "2", "null", "NaN", "-2" }) {
                fields[13] = invalid; AssertParseRejected(String.Join(";", fields));
            }
        });

        Run("v5 existing tunes keep throttle instantaneous", delegate {
            TuneData original = ValidTune(); string[] fields = original.Serialize().Split(';'); fields[0] = "5";
            TuneData parsed; string error;
            Assert(TuneData.TryParse(String.Join(";", fields, 0, 14), out parsed, out error), error);
            original.ThrottleRiseSeconds = 0f; AssertSame(original, parsed);
            Assert(parsed.Serialize().StartsWith("9;"), "V5 did not migrate to V9.");
        });
        Run("throttle buildup round trips off minimum default and maximum", delegate {
            foreach (float seconds in new float[] { 0f, 0.1f, 0.75f, 3f }) {
                TuneData tune = ValidTune(); tune.ThrottleRiseSeconds = seconds; AssertValid(tune, Stock);
                TuneData parsed; string error; Assert(TuneData.TryParse(tune.Serialize(), out parsed, out error), error); AssertSame(tune, parsed);
            }
        });
        RejectMutation("negative throttle buildup", delegate(TuneData t) { t.ThrottleRiseSeconds = -0.01f; });
        RejectMutation("oversized throttle buildup", delegate(TuneData t) { t.ThrottleRiseSeconds = 3.01f; });
        RejectMutation("NaN throttle buildup", delegate(TuneData t) { t.ThrottleRiseSeconds = Single.NaN; });
        RejectMutation("infinite throttle buildup", delegate(TuneData t) { t.ThrottleRiseSeconds = Single.PositiveInfinity; });

        RejectMutation("NaN gear ratio", delegate(TuneData t) { t.Ratios[2] = Single.NaN; });
        RejectMutation("positive infinity gear ratio", delegate(TuneData t) { t.Ratios[2] = Single.PositiveInfinity; });
        RejectMutation("negative infinity reverse ratio", delegate(TuneData t) { t.Ratios[0] = Single.NegativeInfinity; });
        RejectMutation("zero forward gear", delegate(TuneData t) { t.Ratios[2] = 0f; });
        RejectMutation("zero reverse gear", delegate(TuneData t) { t.Ratios[0] = 0f; });
        RejectMutation("reverse sign change", delegate(TuneData t) { t.Ratios[0] = 3f; });
        RejectMutation("forward sign change", delegate(TuneData t) { t.Ratios[3] = -2f; });
        RejectMutation("neutral made a forward gear", delegate(TuneData t) { t.Ratios[1] = 1f; });
        RejectMutation("reordered neutral and first gear", delegate(TuneData t) { t.Ratios[1] = t.Ratios[2]; t.Ratios[2] = 0f; });
        RejectMutation("tiny forward ratio", delegate(TuneData t) { t.Ratios[2] = 0.001f; });
        RejectMutation("oversized ratio", delegate(TuneData t) { t.Ratios[2] = 101f; });
        RejectMutation("null gear array", delegate(TuneData t) { t.Ratios = null; });
        RejectMutation("zero final drive", delegate(TuneData t) { t.FinalDrive = 0f; });
        RejectMutation("negative final drive", delegate(TuneData t) { t.FinalDrive = -1f; });
        RejectMutation("NaN final drive", delegate(TuneData t) { t.FinalDrive = Single.NaN; });
        RejectMutation("infinite final drive", delegate(TuneData t) { t.FinalDrive = Single.PositiveInfinity; });
        RejectMutation("NaN shift RPM", delegate(TuneData t) { t.ShiftUp = Single.NaN; });
        RejectMutation("infinite shift RPM", delegate(TuneData t) { t.ShiftDown = Single.PositiveInfinity; });
        RejectMutation("negative shift RPM", delegate(TuneData t) { t.ShiftDown = -1f; });
        RejectMutation("oversized shift RPM", delegate(TuneData t) { t.ShiftUp = 30001f; });
        RejectMutation("downshift greater than upshift", delegate(TuneData t) { t.ShiftDown = 8000f; });
        RejectMutation("equal shift RPM", delegate(TuneData t) { t.ShiftDown = t.ShiftUp; });
        RejectMutation("enabled upshift below 100 RPM", delegate(TuneData t) { t.ShiftUp = 99f; t.ShiftDown = 0f; });
        RejectMutation("negative speed limit", delegate(TuneData t) { t.SpeedLimitKph = -1f; });
        RejectMutation("speed limit below minimum", delegate(TuneData t) { t.SpeedLimitKph = 9.99f; });
        RejectMutation("speed limit above maximum", delegate(TuneData t) { t.SpeedLimitKph = 500.01f; });
        RejectMutation("NaN speed limit", delegate(TuneData t) { t.SpeedLimitKph = Single.NaN; });
        RejectMutation("infinite speed limit", delegate(TuneData t) { t.SpeedLimitKph = Single.PositiveInfinity; });
        RejectMutation("negative engine braking strength", delegate(TuneData t) { t.EngineBrakingStrength = -0.001f; });
        RejectMutation("engine braking strength above maximum", delegate(TuneData t) { t.EngineBrakingStrength = 5.001f; });
        RejectMutation("NaN engine braking strength", delegate(TuneData t) { t.EngineBrakingStrength = Single.NaN; });
        RejectMutation("infinite engine braking strength", delegate(TuneData t) { t.EngineBrakingStrength = Single.PositiveInfinity; });
        RejectMutation("low range ratio below direct drive", delegate(TuneData t) { t.LowRangeRatio = 0.999f; });
        RejectMutation("low range ratio above ten", delegate(TuneData t) { t.LowRangeRatio = 10.001f; });
        RejectMutation("NaN low range ratio", delegate(TuneData t) { t.LowRangeRatio = Single.NaN; });
        RejectMutation("positive infinity low range ratio", delegate(TuneData t) { t.LowRangeRatio = Single.PositiveInfinity; });
        RejectMutation("negative infinity low range ratio", delegate(TuneData t) { t.LowRangeRatio = Single.NegativeInfinity; });
        RejectMutation("disabled low range cannot remain selected", delegate(TuneData t) { t.LowRangeEnabled = false; });

        Run("without expected layout missing neutral rejected", delegate {
            TuneData t = ValidTune(); t.Ratios = new float[] { -2f, 3f, 2f }; AssertRejected(t, null);
        });
        Run("without expected layout duplicate neutrals rejected", delegate {
            TuneData t = ValidTune(); t.Ratios = new float[] { -2f, 0f, 0f, 2f }; AssertRejected(t, null);
        });
        Run("without expected layout forward before neutral rejected", delegate {
            TuneData t = ValidTune(); t.Ratios = new float[] { 2f, 0f, 1f }; AssertRejected(t, null);
        });
        Run("without expected layout reverse after neutral rejected", delegate {
            TuneData t = ValidTune(); t.Ratios = new float[] { 0f, -2f, 1f }; AssertRejected(t, null);
        });
        Run("without expected layout no forward gear rejected", delegate {
            TuneData t = ValidTune(); t.Ratios = new float[] { -2f, 0f }; AssertRejected(t, null);
        });

        Run("custom rev limit round trips and presets preserve exact values", delegate {
            foreach(float limit in new[]{0f,7000f,7523.45f,15000f}) {
                TuneData original=ValidTune();original.RevLimitRpm=limit;AssertValid(original,Stock);
                TuneData parsed;string error;Assert(TuneData.TryParse(original.Serialize(),out parsed,out error),error);AssertSame(original,parsed);
                TuneData clone=original.Clone();AssertSame(original,clone);clone.RevLimitRpm=0f;Assert(original.RevLimitRpm==limit,"Preset edit changed engine limit.");
            }
        });
        Run("removed traction values are ignored in v8 and v9 saves and presets", delegate {
            foreach (string strength in new[] {"0", "0.65", "1", "NaN", "obsolete"})
            foreach (int schema in new[] {8, 9}) {
                TuneData original = ValidTune();
                if (schema == 9) { original.BrakeStrength = 2.5f; original.HandbrakeStrength = 0.75f; }
                string[] fields = original.Serialize().Split(';'); fields[0] = schema.ToString(); fields[16] = strength;
                TuneData parsed; string error;
                Assert(TuneData.TryParse(String.Join(";", fields, 0, schema == 8 ? 17 : 19), out parsed, out error), error);
                AssertSame(original, parsed); AssertSame(original, parsed.Clone());
                Assert(parsed.Serialize().Split(';')[16] == "0", "Retired traction value survived saving.");
            }
        });
        Run("independent brake strengths round trip and invalidate cached tunes", delegate {
            foreach (float brake in new[] {0.1f, 1f, 2.345678f, 5f})
            foreach (float handbrake in new[] {0.1f, 1f, 3.456789f, 5f}) {
                TuneData original = ValidTune(); original.BrakeStrength = brake; original.HandbrakeStrength = handbrake;
                TuneData parsed; string error; Assert(TuneData.TryParse(original.Serialize(), out parsed, out error), error);
                AssertSame(original, parsed); AssertSame(original, original.Clone());
                parsed.BrakeStrength = brake == 1f ? 2f : 1f;
                Assert(!original.SameValues(parsed), "Brake change retained a valid snapshot.");
                parsed = original.Clone(); parsed.HandbrakeStrength = handbrake == 1f ? 2f : 1f;
                Assert(!original.SameValues(parsed), "Handbrake change retained a valid snapshot.");
            }
        });
        Run("v8 saves retain previous settings with normal brakes", delegate {
            TuneData original = ValidTune();
            string[] fields = original.Serialize().Split(';'); fields[0] = "8"; fields[16] = "0.65";
            TuneData parsed; string error;
            Assert(TuneData.TryParse(String.Join(";", fields, 0, 17), out parsed, out error), error);
            AssertSame(original, parsed); Assert(parsed.BrakeStrength == 1f && parsed.HandbrakeStrength == 1f, "Old brakes changed.");
        });
        foreach (float badStrength in new[] {0f, 0.099f, 5.001f, float.NaN, float.PositiveInfinity, float.NegativeInfinity}) {
            float bad = badStrength;
            RejectMutation("invalid regular brake strength " + bad, delegate(TuneData t) { t.BrakeStrength = bad; });
            RejectMutation("invalid handbrake strength " + bad, delegate(TuneData t) { t.HandbrakeStrength = bad; });
        }
        Run("v7 engine saves retain all previous options with traction off", delegate {
            TuneData original = ValidTune(); original.RevLimitRpm = 8000f;
            string[] fields = original.Serialize().Split(';'); fields[0] = "7";
            TuneData parsed; string error; Assert(TuneData.TryParse(String.Join(";", fields, 0, 16), out parsed, out error), error);
            AssertSame(original, parsed); Assert(parsed.Serialize().Split(';')[16] == "0", "Retired field is not off.");
        });
        Run("v6 engine saves retain every option with native rev limit", delegate {
            TuneData original=ValidTune();original.RevLimitRpm=8000f;string[] fields=original.Serialize().Split(';');fields[0]="6";
            TuneData parsed;string error;Assert(TuneData.TryParse(String.Join(";",fields,0,15),out parsed,out error),error);
            original.RevLimitRpm=0f;AssertSame(original,parsed);
        });
        RejectMutation("nonfinite rev limit rejected",delegate(TuneData t){t.RevLimitRpm=float.NaN;});
        RejectMutation("rev limit above supported maximum rejected",delegate(TuneData t){t.RevLimitRpm=15001f;});
        RejectMutation("positive rev limit below minimum rejected",delegate(TuneData t){t.RevLimitRpm=999f;});
        RejectMutation("automatic upshift at or above limit rejected",delegate(TuneData t){t.RevLimitRpm=t.ShiftUp;});

        Run("malformed save payloads rejected", delegate {
            string good = ValidTune().Serialize();
            string[] malformed = {
                null, String.Empty, new string('x', 8193),
                "99" + good.Substring(1), good + ";extra",
                good.Substring(0, good.Length - 1) + "true",
                "1;;3;6000;2000;1",
                "1;-3,0,NaN;3;6000;2000;1",
                "1;-3,0,Infinity;3;6000;2000;1",
                "1;-3,0,3;-Infinity;6000;2000;1",
                "1;-3,0,3;3;2000;6000;1",
                "1;-3,0,3;3;6000;6000;1",
                "2;-3,0,3;3;6000;2000;1",
                "2;-3,0,3;3;6000;2000;1;true;1;150",
                "2;-3,0,3;3;6000;2000;1;1;false;150",
                "2;-3,0,3;3;6000;2000;1;1;1;NaN",
                "3;-3,0,3;3;6000;2000;1;1;1;150",
                "3;-3,0,3;3;6000;2000;1;1;1;150;NaN",
                "3;-3,0,3;3;6000;2000;1;1;1;150;5.01",
                "4;-3,0,3;3;6000;2000;1;1;1;150;2;1;2.5",
                "4;-3,0,3;3;6000;2000;1;1;1;150;2;true;2.5;0",
                "4;-3,0,3;3;6000;2000;1;1;1;150;2;1;2.5;false",
                "4;-3,0,3;3;6000;2000;1;1;1;150;2;1;NaN;0",
                "4;-3,0,3;3;6000;2000;1;1;1;150;2;1;Infinity;0",
                "4;-3,0,3;3;6000;2000;1;1;1;150;2;1;0;1",
                "4;-3,0,3;3;6000;2000;1;1;1;150;2;1;10.001;1",
                "4;-3,0,3;3;6000;2000;1;1;1;150;2;0;2.5;1"
            };
            foreach (string bad in malformed) AssertParseRejected(bad);
        });
        Run("more than 32 gears rejected", delegate {
            TuneData t = ValidTune(); t.Ratios = new float[33];
            t.Ratios[0] = -3f;
            for (int i = 2; i < t.Ratios.Length; i++) t.Ratios[i] = 1f;
            AssertRejected(t, null);
            AssertParseRejected(t.Serialize());
        });

        Console.WriteLine("RESULT: " + passed + " passed, " + failed + " failed.");
        return failed == 0 ? 0 : 1;
    }
}
