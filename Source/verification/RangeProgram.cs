using System;
using ApocaGearbox;
using NWH.VehiclePhysics2;
using NWH.VehiclePhysics2.Powertrain;

internal static class RangeProgram
{
    private static int passed, failed;
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Close(float actual, float expected, string message)
    { Assert(Math.Abs(actual - expected) < 0.0001f, message + ": " + actual + " versus " + expected); }
    private static void Run(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }
    private static void Safety(float speed, int gear, bool shifting, bool expected)
    {
        string error;
        Assert(RangeMath.CanSwitch(speed, gear, shifting, out error) == expected, "Unexpected range safety decision.");
        Assert(expected ? error == null : !String.IsNullOrEmpty(error), "Safety decision lacks the expected explanation.");
    }
    private static VehicleController Vehicle()
    {
        VehicleController vehicle = new VehicleController(); vehicle.powertrain.transmission.Gear = 0; return vehicle;
    }
    private static void NativeSafety(VehicleController vehicle, bool expected)
    {
        string error;
        Assert(RangeControl.CanChange(vehicle, out error) == expected, "Unexpected native range decision.");
        Assert(expected ? error == null : !String.IsNullOrEmpty(error), "Native decision lacks the expected explanation.");
    }
    private static int Main()
    {
        Run("high range and disabled transfer preserve direct drive", delegate {
            Close(RangeMath.Factor(true, false, 2.5f), 1f, "High range applied reduction");
            Close(RangeMath.Factor(false, false, 2.5f), 1f, "Disabled range applied reduction");
            Close(RangeMath.Factor(false, true, 2.5f), 1f, "Disabled range applied corrupted selection");
        });
        Run("low reduction produces expected forward, reverse and neutral ratios", delegate {
            float reduction = RangeMath.Factor(true, true, 2.5f);
            Close(-3f * reduction, -7.5f, "Reverse ratio"); Close(0f * reduction, 0f, "Neutral ratio");
            Close(3.2f * reduction, 8f, "First ratio"); Close(1.8f * reduction, 4.5f, "Second ratio");
            Close(1f * reduction, 2.5f, "Top ratio");
            Close(DrivingMath.ProjectedRpm(10f, 8f * 3.5f), 2673.803f, "Wheel-to-engine RPM with base final drive");
        });
        Run("supported reduction boundaries preserve their exact factor", delegate {
            Close(RangeMath.Factor(true, true, 1f), 1f, "Minimum reduction");
            Close(RangeMath.Factor(true, true, 10f), 10f, "Maximum reduction");
        });
        Run("invalid reductions fall back to high range", delegate {
            foreach (float reduction in new float[] { 0f, 0.999f, 10.001f, Single.NaN, Single.PositiveInfinity, Single.NegativeInfinity })
                Close(RangeMath.Factor(true, true, reduction), 1f, "Invalid reduction was applied");
        });
        Run("stopped and threshold speed permit switching only in neutral", delegate {
            foreach (float speed in new float[] { 0f, 0.999f, 1f, -1f }) Safety(speed, 0, false, true);
        });
        Run("moving in either direction blocks range changes", delegate {
            foreach (float speed in new float[] { 1.001f, -1.001f, 50f, -50f }) Safety(speed, 0, false, false);
        });
        Run("every forward and reverse gear blocks switching while stationary", delegate {
            foreach (int gear in new int[] { -2, -1, 1, 2, 5, Int32.MinValue, Int32.MaxValue }) Safety(0f, gear, false, false);
        });
        Run("an unfinished shift blocks neutral range changes", delegate { Safety(0f, 0, true, false); });
        Run("unverifiable speed blocks changes rather than assuming stopped", delegate {
            foreach (float speed in new float[] { Single.NaN, Single.PositiveInfinity, Single.NegativeInfinity }) Safety(speed, 0, false, false);
        });
        Run("native full chassis speed is converted from meters per second", delegate {
            VehicleController vehicle = Vehicle(); vehicle.TestVelocityMagnitude = 0.27f; NativeSafety(vehicle, true);
            // A sideways slide is still represented by the full magnitude.
            vehicle.TestVelocityMagnitude = 0.28f; NativeSafety(vehicle, false);
        });
        Run("neutral switching works for every manual and automatic clutch mode", delegate {
            foreach (TransmissionComponent.TransmissionShiftType mode in new TransmissionComponent.TransmissionShiftType[] {
                TransmissionComponent.TransmissionShiftType.Manual, TransmissionComponent.TransmissionShiftType.Automatic })
            foreach (ClutchComponent.ClutchControlType clutchMode in new ClutchComponent.ClutchControlType[] {
                ClutchComponent.ClutchControlType.Automatic, ClutchComponent.ClutchControlType.Manual, ClutchComponent.ClutchControlType.ManualWithAutoClutch }) {
                VehicleController vehicle = Vehicle(); vehicle.powertrain.transmission.transmissionType = mode;
                vehicle.powertrain.clutch.controlType = clutchMode; vehicle.powertrain.clutch.Engagement = 1f;
                NativeSafety(vehicle, true);
                vehicle.powertrain.transmission.Gear = 1; vehicle.powertrain.clutch.Engagement = 0f;
                NativeSafety(vehicle, false);
            }
        });
        Run("CVT and missing drivetrain remain unsupported", delegate {
            NativeSafety(null, false); VehicleController vehicle = Vehicle(); vehicle.powertrain = null; NativeSafety(vehicle, false);
            vehicle = Vehicle(); vehicle.powertrain.transmission = null; NativeSafety(vehicle, false);
            vehicle = Vehicle(); vehicle.powertrain.transmission.transmissionType = TransmissionComponent.TransmissionShiftType.CVT; NativeSafety(vehicle, false);
        });
        Run("native speed read failure is safely rejected", delegate {
            VehicleController vehicle = Vehicle(); vehicle.ThrowVelocityRead = true; NativeSafety(vehicle, false);
            vehicle.ThrowVelocityRead = false; vehicle.TestVelocityMagnitude = Single.NaN; NativeSafety(vehicle, false);
        });
        Console.WriteLine("RESULT: " + passed + " passed, " + failed + " failed.");
        return failed == 0 ? 0 : 1;
    }
}
