using System.Collections.Generic;
using UnityEngine;
using NWH.VehiclePhysics2.Powertrain;
using NWH.VehiclePhysics2;

namespace ApocaGearbox
{
    internal sealed class TestFlag { internal bool Value = true; }
    internal sealed class TestLog
    {
        internal void LogWarning(object value) { }
        internal void LogError(object value) { }
    }
    internal static class Plugin
    {
        internal static readonly TestFlag Enabled = new TestFlag();
        internal static readonly TestLog Log = new TestLog();
        internal static void ShowNotice(string text) { }
    }
    internal sealed class DrivingAssist
    {
        internal static readonly Dictionary<VehicleController, DrivingAssist> Instances = new Dictionary<VehicleController, DrivingAssist>();
        internal bool Applied;
        internal int Updates, Restores;
        internal DrivingAssist(VehicleController vehicle) { Instances[vehicle] = this; }
        internal void Update(TuneData tune) { Updates++; Applied = true; }
        internal void Restore() { Restores++; Applied = false; }
    }
    // Existing engine suites use the unchanged native wheel graph. The separate
    // Drive suite exercises the real vehicle routing helper.
    internal sealed class DriveControl
    {
        internal static readonly Dictionary<VehicleController, DriveControl> States = new Dictionary<VehicleController, DriveControl>();
        internal VehicleController Vehicle;
        internal bool Enabled;
        internal bool FourWheelSelected = true;
        internal bool IsReady = true;
        internal int CommitCalls, ValidateCalls;
        internal static DriveControl GetOrCreate(VehicleController vehicle)
        {
            if (vehicle == null) return null;
            DriveControl control;
            if (!States.TryGetValue(vehicle, out control)) {
                control = new DriveControl { Vehicle = vehicle }; States.Add(vehicle, control);
            }
            return control;
        }
        internal static bool TryGet(VehicleController vehicle, out DriveControl control)
        { control = null; return vehicle != null && States.TryGetValue(vehicle, out control); }
        internal bool ValidateOption(bool enabled, out string error)
        {
            ValidateCalls++; error = null;
            if (enabled && !IsReady) { error = "The vehicle's drive system is not ready."; return false; }
            return enabled == Enabled || RangeControl.CanChange(Vehicle, out error);
        }
        internal bool CommitEnabled(bool enabled, out string error)
        {
            CommitCalls++;
            if (!ValidateOption(enabled, out error)) return false;
            if (Enabled == enabled) return true;
            Enabled = enabled; if (!enabled) FourWheelSelected = true;
            return true;
        }
        internal static bool TryWheelShaftAngularVelocity(TransmissionComponent transmission, out float angular)
        { angular = 0f; return false; }
    }
    internal sealed class LegacyFinalDrive
    {
        private readonly GameObject engine;
        internal static readonly Dictionary<GameObject, float> Edits = new Dictionary<GameObject, float>();
        internal LegacyFinalDrive(GameObject engine) { this.engine = engine; }
        internal bool Refresh(ref float tunedValue)
        {
            float value; if (!Edits.TryGetValue(engine, out value)) return false;
            Edits.Remove(engine); bool changed = tunedValue != value; tunedValue = value; return changed;
        }
        // Model only the helper's write to the engine-native FSM variable.
        // The separate Legacy suite tests its actual observers and UI mirrors.
        internal void Set(float value) { GearboxNativeDefaults.NativeFinalDrive[engine] = value; }
        internal void Restore(float original) { Set(original); }
    }
    internal static class GearboxNativeDefaults
    {
        internal static readonly Dictionary<GameObject, TuneData> Defaults = new Dictionary<GameObject, TuneData>();
        internal static readonly Dictionary<GameObject, float> NativeFinalDrive = new Dictionary<GameObject, float>();
        internal static int Captures;
        internal static TuneData Capture(GameObject engine, TransmissionComponent transmission)
        { return Capture(engine); }
        internal static TuneData Capture(GameObject engine)
        {
            Captures++;
            TuneData value;
            if (!Defaults.TryGetValue(engine, out value)) return null;
            TuneData native = value.Clone(); float current;
            if (NativeFinalDrive.TryGetValue(engine, out current)) native.FinalDrive = current;
            return native;
        }
    }
    internal static class CalculatorEngineSpecs
    {
        internal static int RpmReads;
        internal static bool ReadRpm(GameObject engine, out float idle, out float redline)
        { RpmReads++; idle = 800f; redline = 5000f; return engine != null; }
    }
}
