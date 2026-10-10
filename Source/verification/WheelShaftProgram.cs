using System;
using ApocaGearbox;
using HutongGames.PlayMaker;
using NWH.VehiclePhysics2;
using NWH.VehiclePhysics2.Powertrain;
using UnityEngine;

internal static class WheelShaftProgram
{
    private static int passed, failed;
    private sealed class World
    {
        internal GameObject Engine;
        internal VehicleController Vehicle;
        internal TransmissionComponent Transmission;
        internal GearboxState State;
        internal WheelComponent Wheel;
    }
    private static PlayMakerFSM Fsm(GameObject owner, string name)
    { PlayMakerFSM value = owner.AddComponent<PlayMakerFSM>(); value.FsmName = name; return value; }
    private static WheelComponent Wheel(float angularVelocity)
    { return new WheelComponent { wheelUAPI = new TestWheelSensor { Value = angularVelocity } }; }
    private static DifferentialComponent Diff(PowertrainComponent a, PowertrainComponent b)
    { return new DifferentialComponent { Output = a, OutputB = b }; }
    private static World Build()
    {
        GearboxState.Suspend(); GearboxState.TunedEngines.Clear(); Plugin.Enabled.Value = true;
        World w = new World(); GameObject car = new GameObject("Car"); w.Vehicle = car.AddComponent<VehicleController>();
        GameObject mount = new GameObject("hinge_engine"); mount.transform.parent = car.transform;
        w.Engine = new GameObject("Engine"); w.Engine.tag = "vehPart"; w.Engine.transform.parent = mount.transform;
        Fsm(w.Engine, "ID").FsmVariables.Strings["ID"] = new FsmString { Value = "engine" };
        Fsm(w.Engine, "CheckTag").FsmVariables.Objects["vehicle"] = new FsmGameObject { Value = car };
        TuneData native = new TuneData { Ratios = new float[] { -3f, 0f, 2f, 1f }, FinalDrive = 4f, ShiftUp = 4200f, ShiftDown = 1500f };
        GearboxNativeDefaults.Defaults[w.Engine] = native; w.Transmission = w.Vehicle.powertrain.transmission;
        w.Transmission.gears.AddRange(native.Ratios); w.Transmission.finalGearRatio = native.FinalDrive;
        w.Wheel = Wheel((float)(25 * Math.PI)); w.Transmission.Output = w.Wheel;
        w.State = GearboxState.GetOrCreate(w.Engine); string error;
        Assert(w.State.Commit(w.Vehicle, native.Clone(), out error), error ?? "Fixture tune could not bind.");
        return w;
    }
    private static void Assert(bool result, string message) { if (!result) throw new Exception(message); }
    private static void Near(float actual, float expected) { Assert(Math.Abs(actual - expected) < 0.001f, "Expected " + expected + ", got " + actual); }
    private static void Run(string name, Action<World> test)
    {
        try { test(Build()); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }
    private static float Read(TransmissionComponent transmission)
    {
        float angular; Assert(WheelShaft.TryWheelShaftAngularVelocity(transmission, out angular), "Known wheel graph was unreadable."); return angular;
    }
    private static void Unknown(TransmissionComponent transmission)
    {
        float angular; Assert(!WheelShaft.TryWheelShaftAngularVelocity(transmission, out angular), "Unknown wheel graph accepted.");
    }
    private static int Main()
    {
        Run("direct wheel sensor drives shaft diagnostics", delegate(World w) { w.Wheel.wheelUAPI.Value = 10f; Near(Read(w.Transmission), 10f); });
        Run("differential averages both wheels regardless torque bias", delegate(World w) {
            DifferentialComponent diff = Diff(Wheel(10f), Wheel(30f)); diff.biasAB = 0.99f; w.Transmission.Output = diff; Near(Read(w.Transmission), 20f);
        });
        Run("nested front and rear differentials average all wheels", delegate(World w) {
            w.Transmission.Output = Diff(Diff(Wheel(100f), Wheel(200f)), Diff(Wheel(300f), Wheel(400f))); Near(Read(w.Transmission), 250f);
        });
        Run("single spinning wheel contributes native equal average", delegate(World w) { w.Transmission.Output = Diff(Wheel(0f), Wheel(1000f)); Near(Read(w.Transmission), 500f); });
        Run("opposite wheel rotations cancel native shaft speed", delegate(World w) { w.Transmission.Output = Diff(Wheel(-100f), Wheel(100f)); Near(Read(w.Transmission), 0f); });
        Run("missing wheel sensor is rejected for diagnostics", delegate(World w) { w.Wheel.wheelUAPI = null; Unknown(w.Transmission); });
        Run("nonfinite sensor is rejected for diagnostics", delegate(World w) { w.Wheel.wheelUAPI.Value = Single.NaN; Unknown(w.Transmission); });
        Run("throwing native wheel API does not escape the diagnostic reader", delegate(World w) { w.Wheel.wheelUAPI.Throw = true; Unknown(w.Transmission); });
        Run("unknown output component is rejected for diagnostics", delegate(World w) { w.Transmission.Output = new PowertrainComponent(); Unknown(w.Transmission); });
        Run("missing differential branch is rejected for diagnostics", delegate(World w) { w.Transmission.Output = Diff(Wheel(10f), null); Unknown(w.Transmission); });
        Run("unresolved native output hash is rejected for diagnostics", delegate(World w) { w.Transmission.outputNameHash = 0; Unknown(w.Transmission); });
        Run("cyclic drivetrain graph does not recurse forever", delegate(World w) {
            DifferentialComponent diff = Diff(null, Wheel(10f)); diff.Output = diff; w.Transmission.Output = diff; Unknown(w.Transmission);
        });
        Run("deep unknown drivetrain graph is bounded", delegate(World w) {
            PowertrainComponent branch = Wheel(10f);
            for (int i = 0; i < 17; i++) branch = Diff(branch, Wheel(10f));
            w.Transmission.Output = branch; Unknown(w.Transmission);
        });
        Console.WriteLine("RESULT: " + passed + " passed, " + failed + " failed.");
        return failed == 0 ? 0 : 1;
    }
}
