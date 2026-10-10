using System;
using System.Reflection;
using ApocaGearbox;
using HutongGames.PlayMaker;
using NWH.VehiclePhysics2;
using NWH.VehiclePhysics2.Powertrain;
using UnityEngine;

internal static class EngineBrakingProgram
{
    private static int passed, failed;
    private static readonly MethodInfo Postfix = typeof(EngineBraking).GetMethod("Postfix", BindingFlags.NonPublic | BindingFlags.Static);
    private sealed class World
    {
        internal GameObject EngineItem;
        internal VehicleController Vehicle;
        internal EngineComponent Engine;
        internal GearboxState State;
    }
    private static PlayMakerFSM Fsm(GameObject owner, string name)
    { PlayMakerFSM value = owner.AddComponent<PlayMakerFSM>(); value.FsmName = name; return value; }
    private static World Build()
    {
        GearboxState.Suspend(); GearboxState.TunedEngines.Clear(); Plugin.Enabled.Value = true;
        World w = new World(); GameObject car = new GameObject("Car"); w.Vehicle = car.AddComponent<VehicleController>();
        GameObject mount = new GameObject("hinge_engine"); mount.transform.parent = car.transform;
        w.EngineItem = new GameObject("Engine"); w.EngineItem.tag = "vehPart"; w.EngineItem.transform.parent = mount.transform;
        Fsm(w.EngineItem, "ID").FsmVariables.Strings["ID"] = new FsmString { Value = "engine" };
        Fsm(w.EngineItem, "CheckTag").FsmVariables.Objects["vehicle"] = new FsmGameObject { Value = car };
        TuneData native = new TuneData { Ratios = new float[] { -3f, 0f, 2f, 1f }, FinalDrive = 4f, ShiftUp = 4200f, ShiftDown = 1500f };
        GearboxNativeDefaults.Defaults[w.EngineItem] = native; w.Vehicle.powertrain.transmission.gears.AddRange(native.Ratios);
        w.Engine = w.Vehicle.powertrain.engine; w.State = GearboxState.GetOrCreate(w.EngineItem);
        TuneData desired = native.Clone(); desired.EngineBrakingStrength = 3f;
        string error; Assert(w.State.Commit(w.Vehicle, desired, out error), error ?? "Fixture tune could not bind.");
        return w;
    }
    private static void Assert(bool result, string message) { if (!result) throw new Exception(message); }
    private static void Near(float actual, float expected) { Assert(Math.Abs(actual - expected) < 0.001f, "Expected " + expected + ", got " + actual); }
    private static void Run(string name, Action<World> test)
    {
        try { test(Build()); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }
    private static float Torque(EngineComponent engine, float nativeTorque)
    {
        object[] arguments = { engine, nativeTorque }; Postfix.Invoke(null, arguments); return (float)arguments[1];
    }
    private static int Main()
    {
        Run("closed throttle multiplies native negative coast torque", delegate(World w) { Near(Torque(w.Engine, -20f), -95f); });
        Run("global gain scales each enabled strength and native engine loss uniformly", delegate(World w) {
            float[] strengths = { 0f, 1f, 2f, 3f, 5f };
            float[] multipliers = { 1f, 2.25f, 3.5f, 4.75f, 7.25f };
            for (int i = 0; i < strengths.Length; i++) {
                w.State.Tune.EngineBrakingStrength = strengths[i];
                Near(Torque(w.Engine, -20f), -20f * multipliers[i]);
                Near(Torque(w.Engine, -8f), -8f * multipliers[i]);
            }
        });
        Run("native zero setting and positive torque stay unchanged", delegate(World w) {
            Near(Torque(w.Engine, 50f), 50f); Near(Torque(w.Engine, 0f), 0f);
            w.State.Tune.EngineBrakingStrength = 0f; Near(Torque(w.Engine, -20f), -20f);
        });
        Run("extra engine drag fades with actual player throttle", delegate(World w) {
            w.Vehicle.input.InputSwappedThrottle = 0.1f; Near(Torque(w.Engine, -20f), -57.5f);
            w.Vehicle.input.InputSwappedThrottle = 0.15f; Near(Torque(w.Engine, -20f), -20f);
            w.Vehicle.input.InputSwappedThrottle = 1f; Near(Torque(w.Engine, -20f), -20f);
        });
        Run("idle fade preserves native idle and stall behavior", delegate(World w) {
            w.Engine.OutputRPM = 800f; Near(Torque(w.Engine, -20f), -20f);
            w.Engine.OutputRPM = 900f; Near(Torque(w.Engine, -20f), -45f);
            w.Engine.OutputRPM = 1100f; Near(Torque(w.Engine, -20f), -95f);
        });
        Run("manual and automatic transmission modes support coast braking", delegate(World w) {
            w.Vehicle.powertrain.transmission.transmissionType = TransmissionComponent.TransmissionShiftType.Manual; Near(Torque(w.Engine, -20f), -95f);
            w.Vehicle.powertrain.transmission.transmissionType = TransmissionComponent.TransmissionShiftType.Automatic; Near(Torque(w.Engine, -20f), -95f);
        });
        Run("every clutch control mode uses real physical engagement", delegate(World w) {
            foreach (ClutchComponent.ClutchControlType mode in Enum.GetValues(typeof(ClutchComponent.ClutchControlType))) {
                w.Vehicle.powertrain.clutch.controlType = mode; Near(Torque(w.Engine, -20f), -95f);
                w.Vehicle.powertrain.clutch.Engagement = 0f; Near(Torque(w.Engine, -20f), -20f);
                w.Vehicle.powertrain.clutch.Engagement = 1f;
            }
        });
        Run("partial clutch engagement is not counted twice", delegate(World w) { w.Vehicle.powertrain.clutch.Engagement = 0.25f; Near(Torque(w.Engine, -20f), -95f); });
        Run("neutral and native shift transition preserve original loss", delegate(World w) {
            w.Vehicle.powertrain.transmission.Gear = 0; Near(Torque(w.Engine, -20f), -20f);
            w.Vehicle.powertrain.transmission.Gear = 2; w.Vehicle.powertrain.transmission.isShifting = true; Near(Torque(w.Engine, -20f), -20f);
        });
        Run("reverse gear also resists motion through native drivetrain", delegate(World w) { w.Vehicle.powertrain.transmission.Gear = -1; Near(Torque(w.Engine, -20f), -95f); });
        Run("disabled mod detached engine and unrelated engine remain native", delegate(World w) {
            Plugin.Enabled.Value = false; Near(Torque(w.Engine, -20f), -20f); Plugin.Enabled.Value = true;
            Near(Torque(new EngineComponent(), -20f), -20f);
            w.EngineItem.transform.parent = null; Near(Torque(w.Engine, -20f), -20f);
        });
        Run("stopped electric and ignition off engines remain native", delegate(World w) {
            w.Engine.IsRunning = false; Near(Torque(w.Engine, -20f), -20f); w.Engine.IsRunning = true;
            w.Engine.ignition = false; Near(Torque(w.Engine, -20f), -20f); w.Engine.ignition = true;
            w.Engine.engineType = EngineComponent.EngineType.Electric; Near(Torque(w.Engine, -20f), -20f);
        });
        Run("engine braking never writes driver brake throttle or clutch inputs", delegate(World w) {
            w.Vehicle.input.Brakes = 0.4f; w.Vehicle.input.Clutch = 0.3f; w.Vehicle.input.InputSwappedThrottle = 0f;
            Torque(w.Engine, -20f); Near(w.Vehicle.input.Brakes, 0.4f); Near(w.Vehicle.input.Clutch, 0.3f); Near(w.Vehicle.input.InputSwappedThrottle, 0f);
        });
        Run("maximum extra strength is bounded at 7.25 times native", delegate(World w) {
            w.State.Tune.EngineBrakingStrength = 5f; Near(Torque(w.Engine, -20f), -145f);
            Near(EngineBrakingMath.Multiplier(100f, 0f, 3000f, 800f, 1f, true, false), 7.25f);
        });
        Run("invalid forecasts and native torque remain untouched", delegate(World w) {
            foreach (float invalid in new float[] { Single.NaN, Single.PositiveInfinity, Single.NegativeInfinity }) {
                Near(EngineBrakingMath.Multiplier(invalid, 0f, 3000f, 800f, 1f, true, false), 1f);
                Near(EngineBrakingMath.Multiplier(3f, invalid, 3000f, 800f, 1f, true, false), 1f);
                Near(EngineBrakingMath.Multiplier(3f, 0f, invalid, 800f, 1f, true, false), 1f);
                Near(EngineBrakingMath.Multiplier(3f, 0f, 3000f, invalid, 1f, true, false), 1f);
                Near(EngineBrakingMath.Multiplier(3f, 0f, 3000f, 800f, invalid, true, false), 1f);
            }
            Assert(Single.IsNaN(Torque(w.Engine, Single.NaN)), "Invalid native torque was replaced.");
        });
        Console.WriteLine("RESULT: " + passed + " passed, " + failed + " failed.");
        return failed == 0 ? 0 : 1;
    }
}
