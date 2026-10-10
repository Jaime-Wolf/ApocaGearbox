using System;
using ApocaGearbox;

internal static class DrivingMathProgram
{
    private static int passed, failed;
    private static void Assert(bool result, string message) { if (!result) throw new Exception(message); }
    private static void Near(float actual, float expected)
    { Assert(Math.Abs(actual - expected) <= Math.Max(0.001f, Math.Abs(expected) * 0.000001f), "Expected " + expected + ", got " + actual); }
    private static void Run(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }
    private static int Main()
    {
        Run("one shaft revolution per second yields 60 RPM per ratio", delegate { Near(DrivingMath.ProjectedRpm((float)(2 * Math.PI), 5f), 300f); });
        Run("600 shaft RPM with total ratio 8 forecasts 4800 RPM", delegate { Near(DrivingMath.ProjectedRpm((float)(20 * Math.PI), 8f), 4800f); });
        Run("backward shaft and reverse ratio use RPM magnitude", delegate {
            Near(DrivingMath.ProjectedRpm((float)(-20 * Math.PI), 8f), 4800f);
            Near(DrivingMath.ProjectedRpm((float)(20 * Math.PI), -8f), 4800f);
        });
        Run("stationary wheels forecast zero coupled RPM", delegate { Near(DrivingMath.ProjectedRpm(0f, 20f), 0f); });
        Run("neutral and unavailable forecasts return unknown", delegate {
            Assert(Single.IsNaN(DrivingMath.ProjectedRpm(100f, 0f)), "Neutral forecast was treated as coupled.");
            foreach (float value in new float[] { Single.NaN, Single.PositiveInfinity, Single.NegativeInfinity }) {
                Assert(Single.IsNaN(DrivingMath.ProjectedRpm(value, 3f)), "Bad shaft accepted.");
                Assert(Single.IsNaN(DrivingMath.ProjectedRpm(20f, value)), "Bad ratio accepted.");
            }
        });
        Run("150 km/h limit leaves full power through 145", delegate {
            Near(DrivingMath.SpeedPowerMultiplier(0f, 150f), 1f); Near(DrivingMath.SpeedPowerMultiplier(144f, 150f), 1f); Near(DrivingMath.SpeedPowerMultiplier(145f, 150f), 1f);
        });
        Run("power fades smoothly across final 5 km/h", delegate {
            Near(DrivingMath.SpeedPowerMultiplier(146f, 150f), 0.8f); Near(DrivingMath.SpeedPowerMultiplier(147.5f, 150f), 0.5f); Near(DrivingMath.SpeedPowerMultiplier(149f, 150f), 0.2f);
        });
        Run("at and above road limit drive power is zero", delegate { Near(DrivingMath.SpeedPowerMultiplier(150f, 150f), 0f); Near(DrivingMath.SpeedPowerMultiplier(200f, 150f), 0f); });
        Run("backward motion and disabled road limit keep native power", delegate { Near(DrivingMath.SpeedPowerMultiplier(-200f, 150f), 1f); Near(DrivingMath.SpeedPowerMultiplier(250f, 0f), 1f); });
        Run("missing speed or limit keeps native power", delegate {
            Near(DrivingMath.SpeedPowerMultiplier(Single.NaN, 150f), 1f); Near(DrivingMath.SpeedPowerMultiplier(250f, Single.NaN), 1f);
            Near(DrivingMath.SpeedPowerMultiplier(Single.PositiveInfinity, 150f), 1f); Near(DrivingMath.SpeedPowerMultiplier(250f, Single.PositiveInfinity), 1f);
        });
        Run("road power multiplier stays bounded and monotonic", delegate {
            float previous = 1f;
            for (float speed = -10f; speed <= 180f; speed += 0.25f) {
                float power = DrivingMath.SpeedPowerMultiplier(speed, 150f);
                Assert(power >= 0f && power <= 1f && power <= previous, "Power increased or escaped its bounds."); previous = power;
            }
        });
        Console.WriteLine("RESULT: " + passed + " passed, " + failed + " failed.");
        return failed == 0 ? 0 : 1;
    }
}
