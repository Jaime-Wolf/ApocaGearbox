using System;
using System.Collections.Generic;
using ApocaGearbox;
using HutongGames.PlayMaker;
using NWH.VehiclePhysics2;
using NWH.VehiclePhysics2.Powertrain;
using UnityEngine;

internal static class StateProgram
{
    private static int passed, failed;
    private sealed class World
    {
        internal GameObject Engine, Mount, Car;
        internal VehicleController Vehicle;
        internal TransmissionComponent Transmission;
        internal TuneData Native;
        internal GearboxState State;
    }
    private static PlayMakerFSM Fsm(GameObject owner, string name)
    { PlayMakerFSM fsm = owner.AddComponent<PlayMakerFSM>(); fsm.FsmName = name; return fsm; }
    private static World Create()
    {
        GearboxState.Suspend(); GearboxState.TunedEngines.Clear(); DriveControl.States.Clear(); Plugin.Enabled.Value = true;
        World w = new World();
        w.Car = new GameObject("Car"); w.Vehicle = w.Car.AddComponent<VehicleController>();
        w.Mount = new GameObject("hinge_engine"); w.Mount.transform.parent = w.Car.transform;
        w.Engine = new GameObject("Engine"); w.Engine.tag = "vehPart"; w.Engine.transform.parent = w.Mount.transform;
        Fsm(w.Engine, "ID").FsmVariables.Strings["ID"] = new FsmString { Value = "engine" };
        PlayMakerFSM check = Fsm(w.Engine, "CheckTag"); check.ActiveStateName = "vehPart";
        check.FsmVariables.Objects["vehicle"] = new FsmGameObject { Value = w.Car };
        w.Native = new TuneData { Ratios = new float[] { -3f, 0f, 3.2f, 1.8f, 1f }, FinalDrive = 3.5f, ShiftUp = 4200f, ShiftDown = 1500f, TuneShifts = false };
        GearboxNativeDefaults.Defaults[w.Engine] = w.Native;
        w.Transmission = w.Vehicle.powertrain.transmission;
        NativeMount(w);
        w.State = GearboxState.GetOrCreate(w.Engine);
        return w;
    }
    private static void NativeMount(World w)
    {
        // Model native engine mounting: it replaces all ratios and shift values.
        w.Engine.transform.parent = w.Mount.transform; w.Engine.tag = "vehPart";
        w.Transmission.gears = new List<float>(w.Native.Ratios);
        w.Transmission.finalGearRatio = w.Native.FinalDrive;
        w.Transmission.UpshiftRPM = w.Native.ShiftUp;
        w.Transmission.DownshiftRPM = w.Native.ShiftDown;
    }
    private static TuneData Desired(World w)
    {
        TuneData tune = w.Native.Clone();
        tune.Ratios[2] = 2.7f; tune.Ratios[3] = 1.55f; tune.Ratios[4] = 0.75f;
        tune.FinalDrive = 2.8f; tune.TuneShifts = true; tune.ShiftUp = 5200f; tune.ShiftDown = 2100f;
        return tune;
    }
    private static TuneData LowTune(World w)
    {
        TuneData tune = Desired(w); tune.LowRangeEnabled = true; tune.LowRangeRatio = 2.5f; tune.LowRangeSelected = true;
        return tune;
    }
    private static void Assert(bool result, string message) { if (!result) throw new Exception(message); }
    private static void Run(string name, Action<World> test)
    {
        try { test(Create()); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }
    private static void Commit(World w, TuneData tune)
    {
        string error;
        Assert(w.State.Commit(w.Vehicle, tune, out error), error ?? "Commit rejected.");
    }
    private static void AssertApplied(World w, TuneData tune)
    {
        float reduction = tune.LowRangeEnabled && tune.LowRangeSelected ? tune.LowRangeRatio : 1f;
        for (int i = 0; i < tune.Ratios.Length; i++) Assert(w.Transmission.gears[i] == tune.Ratios[i] * reduction, "Ratio " + i + " was not applied.");
        Assert(w.Transmission.finalGearRatio == tune.FinalDrive, "Final drive was not applied.");
        if (tune.TuneShifts) Assert(w.Transmission.UpshiftRPM == tune.ShiftUp && w.Transmission.DownshiftRPM == tune.ShiftDown, "Custom shifts were not applied.");
    }
    private static void RejectRange(World w, bool low)
    {
        string saved = w.State.ExportTuning(); float[] ratios = w.Transmission.gears.ToArray(); string error;
        Assert(!w.State.TrySetRange(low, out error), "Unsafe range switch accepted.");
        Assert(!String.IsNullOrEmpty(error), "Rejected range switch has no explanation.");
        Assert(w.State.ExportTuning() == saved, "Rejected range switch changed saved state.");
        for (int i = 0; i < ratios.Length; i++) Assert(w.Transmission.gears[i] == ratios[i], "Rejected range switch changed native gearing.");
    }
    private static void RejectCommit(World w, TuneData desired)
    {
        string saved = w.State.ExportTuning(), stock = w.State.ExportStock(); float[] ratios = w.Transmission.gears.ToArray();
        float finalDrive = w.Transmission.finalGearRatio, nativeFinalDrive = GearboxNativeDefaults.NativeFinalDrive[w.Engine]; string error;
        Assert(!w.State.Commit(w.Vehicle, desired, out error), "Unsafe editor change accepted.");
        Assert(!String.IsNullOrEmpty(error), "Rejected editor change has no explanation.");
        Assert(w.State.ExportTuning() == saved && w.State.ExportStock() == stock, "Rejected editor change modified persistence.");
        Assert(w.Transmission.finalGearRatio == finalDrive && GearboxNativeDefaults.NativeFinalDrive[w.Engine] == nativeFinalDrive,
            "Rejected editor change modified final drive.");
        for (int i = 0; i < ratios.Length; i++) Assert(w.Transmission.gears[i] == ratios[i], "Rejected editor change modified native gearing.");
    }
    private static int Main()
    {
        Run("commit updates mounted engine without changing list identity", delegate(World w) {
            List<float> originalList = w.Transmission.gears; TuneData tune = Desired(w); Commit(w, tune); AssertApplied(w, tune);
            Assert(Object.ReferenceEquals(originalList, w.Transmission.gears), "Commit replaced native list identity.");
        });
        Run("committed tune owns its ratio array", delegate(World w) {
            TuneData tune = Desired(w); Commit(w, tune); tune.Ratios[2] = 90f;
            GearboxState.BeforeRatio(w.Transmission); Assert(w.Transmission.gears[2] == 2.7f, "Caller array mutation changed committed tune.");
        });
        Run("loose tune is deferred until native engine mount", delegate(World w) {
            w.Engine.transform.parent = null; w.Engine.tag = "item"; TuneData tune = Desired(w); Commit(w, tune);
            Assert(w.State.ExportTuning().Length > 0, "Loose tune was not retained.");
            Assert(w.Transmission.gears[2] == w.Native.Ratios[2], "Loose tune wrote to unrelated car.");
            NativeMount(w); GearboxState.RefreshMounted(); AssertApplied(w, tune);
        });
        Run("native mounted assignments are followed by tune reapplication", delegate(World w) {
            TuneData tune = Desired(w); Commit(w, tune); NativeMount(w); GearboxState.BeforeRatio(w.Transmission); AssertApplied(w, tune);
        });
        Run("detached tuned engine cannot overwrite replacement native gearbox", delegate(World w) {
            Commit(w, Desired(w)); w.Engine.transform.parent = null; w.Engine.tag = "item";
            w.Transmission.gears = new List<float>(new float[] { -2f, 0f, 4f, 2f, 1.25f });
            w.Transmission.finalGearRatio = 4.1f; w.Transmission.UpshiftRPM = 6000f;
            GearboxState.BeforeRatio(w.Transmission);
            Assert(w.Transmission.gears[2] == 4f && w.Transmission.finalGearRatio == 4.1f && w.Transmission.UpshiftRPM == 6000f, "Detached engine overwrote replacement.");
        });
        Run("invalid commit leaves existing tune and native values intact", delegate(World w) {
            TuneData tune = Desired(w); Commit(w, tune); string previous = w.State.ExportTuning();
            TuneData bad = Desired(w); bad.Ratios[2] = -3f; string error;
            Assert(!w.State.Commit(w.Vehicle, bad, out error), "Invalid commit accepted.");
            Assert(w.State.ExportTuning() == previous, "Rejected commit discarded previous tune."); AssertApplied(w, tune);
        });
        Run("suspend restores native settings while preserving saved tune", delegate(World w) {
            TuneData tune = Desired(w); Commit(w, tune); string previous = w.State.ExportTuning();
            Plugin.Enabled.Value = false; GearboxState.Suspend(); AssertApplied(w, w.Native);
            Assert(w.Transmission.UpshiftRPM == w.Native.ShiftUp && w.Transmission.DownshiftRPM == w.Native.ShiftDown, "Suspend retained custom shifts.");
            Assert(w.State.ExportTuning() == previous, "Suspend discarded saved tune.");
            Plugin.Enabled.Value = true; GearboxState.RefreshMounted(); AssertApplied(w, tune);
        });
        Run("reset restores engine-native settings and clears persistence", delegate(World w) {
            Commit(w, Desired(w)); string error; Assert(w.State.Reset(w.Vehicle, out error), error);
            AssertApplied(w, w.Native);
            Assert(w.Transmission.UpshiftRPM == w.Native.ShiftUp && w.Transmission.DownshiftRPM == w.Native.ShiftDown, "Reset retained custom shifts.");
            Assert(w.State.ExportTuning() == String.Empty && w.State.ExportStock() == String.Empty, "Reset kept persisted tuning.");
        });
        Run("malformed restore clears prior tune state", delegate(World w) {
            Commit(w, Desired(w)); w.State.Restore("corrupt saved tune", w.Native.Serialize());
            Assert(w.State.ExportTuning() == String.Empty && !GearboxState.TunedEngines.Contains(w.State), "Bad save retained prior tune.");
        });
        Run("loaded tune without stock captures original final drive before native mirror", delegate(World w) {
            TuneData saved = Desired(w); saved.FinalDrive = 2.25f;
            w.State.Restore(saved.Serialize(), String.Empty); w.State.RefreshLegacyFinalDrive();
            Assert(w.State.Stock != null && w.State.Stock.FinalDrive == 3.5f, "Publishing the loaded tune polluted its original reset baseline.");
            Assert(GearboxNativeDefaults.NativeFinalDrive[w.Engine] == 2.25f, "Compatible loaded tune was not mirrored.");
            string error; Assert(w.State.Reset(w.Vehicle, out error), error);
            Assert(w.Transmission.finalGearRatio == 3.5f && GearboxNativeDefaults.NativeFinalDrive[w.Engine] == 3.5f, "Reset did not restore original native final drive.");
        });
        Run("incompatible loaded tune cannot publish native final drive", delegate(World w) {
            TuneData incompatible = Desired(w); incompatible.Ratios = new float[] { -2f, 0f, 3f }; incompatible.FinalDrive = 9f;
            w.State.Restore(incompatible.Serialize(), String.Empty); w.State.RefreshLegacyFinalDrive();
            float published;
            Assert(!GearboxNativeDefaults.NativeFinalDrive.TryGetValue(w.Engine, out published) || published == 3.5f,
                "Unsupported saved layout overwrote the engine's native final drive.");
            Assert(w.Transmission.finalGearRatio == 3.5f, "Unsupported saved layout wrote to vehicle transmission.");
        });
        Run("disabling custom shifts restores native thresholds immediately", delegate(World w) {
            TuneData tune = Desired(w); Commit(w, tune); tune.TuneShifts = false; Commit(w, tune);
            Assert(w.Transmission.UpshiftRPM == w.Native.ShiftUp && w.Transmission.DownshiftRPM == w.Native.ShiftDown,
                "Unchecked shift tuning still uses prior custom RPM thresholds.");
            Assert(w.Transmission.gears[2] == tune.Ratios[2], "Turning off custom shifts lost ratio tune.");
        });
        Run("low range applies physical reduction while preserving engine base values", delegate(World w) {
            w.Transmission.Gear = 0; List<float> original = w.Transmission.gears; Commit(w, LowTune(w));
            float[] expected = { -7.5f, 0f, 6.75f, 3.875f, 1.875f };
            for (int i = 0; i < expected.Length; i++) Assert(w.Transmission.gears[i] == expected[i], "Unexpected physical ratio " + i + ".");
            Assert(Object.ReferenceEquals(original, w.Transmission.gears), "Low range replaced the native gear list.");
            Assert(w.Transmission.finalGearRatio == 2.8f && GearboxNativeDefaults.NativeFinalDrive[w.Engine] == 2.8f,
                "Low range polluted the base final drive or its native editor.");
            Assert(w.State.Tune.Ratios[2] == 2.7f && w.State.Stock.Ratios[2] == 3.2f, "Physical reduction changed saved base or original gearing.");
            TuneData saved; string error; Assert(TuneData.TryParse(w.State.ExportTuning(), out saved, out error), error);
            Assert(saved.LowRangeSelected && saved.Ratios[2] == 2.7f && saved.FinalDrive == 2.8f, "Persistence baked physical reduction into the base tune.");
        });
        Run("repeated physical updates cannot compound low range", delegate(World w) {
            w.Transmission.Gear = 0; TuneData tune = LowTune(w); Commit(w, tune);
            for (int cycle = 0; cycle < 20; cycle++) { GearboxState.BeforeRatio(w.Transmission); GearboxState.RefreshMounted(); AssertApplied(w, tune); }
            Assert(w.State.Tune.Ratios[2] == 2.7f && w.State.Stock.Ratios[2] == 3.2f, "Repeated updates contaminated saved ratios.");
        });
        Run("high and low switches preserve base gearing across manual and automatic modes", delegate(World w) {
            w.Transmission.Gear = 0; TuneData tune = LowTune(w); Commit(w, tune); string error;
            foreach (TransmissionComponent.TransmissionShiftType mode in new TransmissionComponent.TransmissionShiftType[] {
                TransmissionComponent.TransmissionShiftType.Manual, TransmissionComponent.TransmissionShiftType.Automatic }) {
                w.Transmission.transmissionType = mode;
                for (int cycle = 0; cycle < 5; cycle++) {
                    Assert(w.State.TrySetRange(false, out error), error); tune.LowRangeSelected = false; AssertApplied(w, tune);
                    Assert(w.State.TrySetRange(true, out error), error); tune.LowRangeSelected = true; AssertApplied(w, tune);
                }
            }
            Assert(w.State.Tune.Ratios[0] == -3f && w.State.Tune.Ratios[2] == 2.7f && w.State.Tune.FinalDrive == 2.8f,
                "Range cycles changed the base tune.");
        });
        Run("unsafe driving switches keep selection and ratios unchanged", delegate(World w) {
            TuneData tune = Desired(w); tune.LowRangeEnabled = true; Commit(w, tune); w.Transmission.Gear = 0;
            w.Vehicle.TestVelocityMagnitude = 0.28f; RejectRange(w, true);
            w.Vehicle.TestVelocityMagnitude = 0f; w.Transmission.Gear = 1; w.Vehicle.powertrain.clutch.Engagement = 0f; RejectRange(w, true);
            w.Transmission.Gear = -1; RejectRange(w, true);
            w.Transmission.Gear = 0; w.Transmission.isShifting = true; RejectRange(w, true);
            w.Transmission.isShifting = false; w.Vehicle.TestVelocityMagnitude = Single.NaN; RejectRange(w, true);
        });
        Run("selected low range also cannot be released unsafely", delegate(World w) {
            w.Transmission.Gear = 0; Commit(w, LowTune(w)); w.Transmission.Gear = 1; RejectRange(w, false);
            w.Transmission.Gear = 0; w.Vehicle.TestVelocityMagnitude = 2f; RejectRange(w, false);
        });
        Run("untuned disabled and detached engines cannot switch a vehicle range", delegate(World w) {
            RejectRange(w, true); Commit(w, Desired(w)); w.Transmission.Gear = 0; RejectRange(w, true);
            TuneData tune = Desired(w); tune.LowRangeEnabled = true; Commit(w, tune);
            Plugin.Enabled.Value = false; RejectRange(w, true); Plugin.Enabled.Value = true;
            w.Engine.transform.parent = null; w.Engine.tag = "item"; RejectRange(w, true);
        });
        Run("loose low range tune applies once on the next native mount", delegate(World w) {
            w.Engine.transform.parent = null; w.Engine.tag = "item"; TuneData tune = LowTune(w); Commit(w, tune);
            Assert(w.Transmission.gears[2] == 3.2f && w.Transmission.finalGearRatio == 3.5f, "Loose tuning altered an unrelated vehicle.");
            NativeMount(w); GearboxState.RefreshMounted(); AssertApplied(w, tune);
            NativeMount(w); GearboxState.BeforeRatio(w.Transmission); AssertApplied(w, tune);
        });
        Run("low range suspend restores native high while retaining the saved choice", delegate(World w) {
            w.Transmission.Gear = 0; TuneData tune = LowTune(w); Commit(w, tune); string saved = w.State.ExportTuning();
            Plugin.Enabled.Value = false; GearboxState.Suspend(); AssertApplied(w, w.Native);
            Assert(w.State.ExportTuning() == saved && w.State.Tune.LowRangeSelected, "Suspend discarded the engine's chosen range.");
            Plugin.Enabled.Value = true; GearboxState.RefreshMounted(); AssertApplied(w, tune);
        });
        Run("reset from low restores original high range and clears both saved records", delegate(World w) {
            w.Transmission.Gear = 0; Commit(w, LowTune(w)); string error; Assert(w.State.Reset(w.Vehicle, out error), error);
            AssertApplied(w, w.Native); Assert(w.State.ExportTuning() == String.Empty && w.State.ExportStock() == String.Empty, "Reset retained range persistence.");
            Assert(GearboxNativeDefaults.NativeFinalDrive[w.Engine] == 3.5f, "Reset retained tuned final drive in native editor.");
        });
        Run("loaded low range retains base originals and applies the saved choice", delegate(World w) {
            TuneData saved = LowTune(w); w.State.Restore(saved.Serialize(), w.Native.Serialize()); GearboxState.RefreshMounted(); AssertApplied(w, saved);
            Assert(w.State.Stock.Ratios[2] == 3.2f && !w.State.Stock.LowRangeSelected, "Loaded reduction contaminated the reset baseline.");
            TuneData reread; string error; Assert(TuneData.TryParse(w.State.ExportTuning(), out reread, out error), error);
            Assert(reread.LowRangeSelected && reread.Ratios[2] == 2.7f && reread.FinalDrive == 2.8f, "Loaded low range was not preserved independently of base gearing.");
        });
        Run("loaded low range with missing originals captures a native high reset baseline", delegate(World w) {
            TuneData saved = LowTune(w); w.Transmission.Gear = 0; w.State.Restore(saved.Serialize(), String.Empty);
            GearboxState.RefreshMounted(); AssertApplied(w, saved);
            Assert(w.State.Stock != null && w.State.Stock.Ratios[0] == -3f && w.State.Stock.Ratios[2] == 3.2f &&
                w.State.Stock.FinalDrive == 3.5f && !w.State.Stock.LowRangeEnabled && !w.State.Stock.LowRangeSelected,
                "Loaded low range contaminated missing original settings.");
            string error; Assert(w.State.Reset(w.Vehicle, out error), error); AssertApplied(w, w.Native);
        });
        Run("chosen range follows the same engine to another native vehicle", delegate(World w) {
            w.Transmission.Gear = 0; TuneData tune = LowTune(w); Commit(w, tune); TransmissionComponent old = w.Transmission;
            GameObject second = new GameObject("SecondCar"); VehicleController vehicle = second.AddComponent<VehicleController>();
            GameObject mount = new GameObject("hinge_engine"); mount.transform.parent = second.transform; w.Engine.transform.parent = mount.transform;
            foreach (PlayMakerFSM fsm in w.Engine.GetComponents<PlayMakerFSM>()) if (fsm.FsmName == "CheckTag") fsm.FsmVariables.Objects["vehicle"].Value = second;
            w.Mount = mount; w.Vehicle = vehicle; w.Car = second; w.Transmission = vehicle.powertrain.transmission;
            NativeMount(w); GearboxState.RefreshMounted(); AssertApplied(w, tune);
            old.gears = new List<float>(new float[] { -2f, 0f, 4f, 2f, 1f }); old.finalGearRatio = 4f;
            GearboxState.BeforeRatio(old); Assert(old.gears[2] == 4f && old.finalGearRatio == 4f, "Engine's old vehicle still received its saved range.");
        });
        Run("unsafe editor range selection cannot bypass driving safety", delegate(World w) {
            TuneData high = Desired(w); high.LowRangeEnabled = true; Commit(w, high);
            TuneData low = high.Clone(); low.LowRangeSelected = true; RejectCommit(w, low);
            w.Transmission.Gear = 0; w.Vehicle.TestVelocityMagnitude = 1f; RejectCommit(w, low);
            w.Vehicle.TestVelocityMagnitude = 0f; w.Transmission.isShifting = true; RejectCommit(w, low);
            w.Transmission.isShifting = false; Commit(w, low); AssertApplied(w, low);
        });
        Run("editing or disabling active low reduction is also an atomic safe switch", delegate(World w) {
            w.Transmission.Gear = 0; TuneData tune = LowTune(w); Commit(w, tune); w.Transmission.Gear = 1;
            TuneData changed = tune.Clone(); changed.LowRangeRatio = 4f; changed.FinalDrive = 4.4f; RejectCommit(w, changed);
            TuneData disabled = tune.Clone(); disabled.LowRangeEnabled = false; disabled.LowRangeSelected = false; RejectCommit(w, disabled);
            w.Transmission.Gear = 0; Commit(w, changed); AssertApplied(w, changed); Commit(w, disabled); AssertApplied(w, disabled);
        });
        Run("high range configuration can be edited without switching moving vehicle gearing", delegate(World w) {
            TuneData tune = Desired(w); tune.LowRangeEnabled = true; tune.LowRangeRatio = 2.5f; Commit(w, tune);
            w.Vehicle.TestVelocityMagnitude = 20f; tune.LowRangeRatio = 6f; Commit(w, tune); AssertApplied(w, tune);
            Assert(!w.State.Tune.LowRangeSelected && w.State.Tune.LowRangeRatio == 6f, "High range setup inadvertently selected low.");
        });
        Run("unsafe reset from low preserves the tune and native editor", delegate(World w) {
            w.Transmission.Gear = 0; TuneData tune = LowTune(w); Commit(w, tune);
            string saved = w.State.ExportTuning(), stock = w.State.ExportStock(); w.Transmission.Gear = 1; string error;
            Assert(!w.State.Reset(w.Vehicle, out error) && !String.IsNullOrEmpty(error), "Unsafe reset accepted.");
            AssertApplied(w, tune); Assert(w.State.ExportTuning() == saved && w.State.ExportStock() == stock, "Rejected reset deleted saved tuning.");
            Assert(GearboxNativeDefaults.NativeFinalDrive[w.Engine] == 2.8f, "Rejected reset changed the native final drive.");
        });
        Run("detaching removes only the owned low layer from the old gearbox", delegate(World w) {
            w.Transmission.Gear = 0; TuneData tune = LowTune(w); Commit(w, tune);
            w.Engine.transform.parent = null; w.Engine.tag = "item"; GearboxState.RefreshMounted();
            tune.LowRangeSelected = false; AssertApplied(w, tune);
            Assert(w.State.Tune.LowRangeSelected && w.State.Tune.Ratios[2] == 2.7f, "Detaching discarded the engine's saved range.");
        });
        Run("detaching respects a replacement native list even with identical low values", delegate(World w) {
            w.Transmission.Gear = 0; Commit(w, LowTune(w)); List<float> foreign = new List<float>(w.Transmission.gears);
            w.Transmission.gears = foreign; w.Engine.transform.parent = null; w.Engine.tag = "item"; GearboxState.RefreshMounted();
            Assert(Object.ReferenceEquals(foreign, w.Transmission.gears) && foreign[2] == 6.75f && foreign[0] == -7.5f,
                "Unbind overwrote a replacement writer's list.");
        });
        Run("detaching respects external writes within the existing native list", delegate(World w) {
            w.Transmission.Gear = 0; Commit(w, LowTune(w)); w.Transmission.gears[3] = 99f;
            w.Engine.transform.parent = null; w.Engine.tag = "item"; GearboxState.RefreshMounted();
            Assert(w.Transmission.gears[3] == 99f && w.Transmission.gears[2] == 6.75f, "Unbind partially rewrote another writer's gearing.");
        });
        Run("rejected switch after native topology change cannot persist a new choice", delegate(World w) {
            TuneData tune = Desired(w); tune.LowRangeEnabled = true; Commit(w, tune); w.Transmission.Gear = 0;
            w.Transmission.gears.RemoveAt(4); RejectRange(w, true);
            Assert(!w.State.Tune.LowRangeSelected, "Apply failure left low range selected.");
        });
        Run("replaced transmission rejects stale range switches until native rebinding", delegate(World w) {
            TuneData tune = Desired(w); tune.LowRangeEnabled = true; Commit(w, tune); w.Transmission.Gear = 0;
            TransmissionComponent replacement = new TransmissionComponent { Gear = 0, gears = new List<float>(w.Native.Ratios), finalGearRatio = 3.5f };
            w.Vehicle.powertrain.transmission = replacement; RejectRange(w, true);
            Assert(replacement.gears[2] == 3.2f && replacement.finalGearRatio == 3.5f, "Stale switch altered the replacement gearbox.");
            w.Transmission = replacement; GearboxState.RefreshMounted(); AssertApplied(w, tune);
            string error; Assert(w.State.TrySetRange(true, out error), error); tune.LowRangeSelected = true; AssertApplied(w, tune);
        });
        Run("old ratio callback after replacement removes its owned layer without touching the new gearbox", delegate(World w) {
            w.Transmission.Gear = 0; TuneData tune = LowTune(w); Commit(w, tune); TransmissionComponent old = w.Transmission;
            TransmissionComponent replacement = new TransmissionComponent { Gear = 0, gears = new List<float>(w.Native.Ratios), finalGearRatio = 3.5f };
            w.Vehicle.powertrain.transmission = replacement; GearboxState.BeforeRatio(old);
            Assert(old.gears[2] == 2.7f && old.gears[0] == -3f, "Old callback retained or reapplied its low reduction.");
            Assert(replacement.gears[2] == 3.2f && replacement.finalGearRatio == 3.5f, "Old callback altered the replacement gearbox.");
            Assert(GearboxState.ForTransmission(old) == null && w.State.Tune.LowRangeSelected, "Stale callback retained registration or discarded saved range.");
            w.Transmission = replacement; GearboxState.RefreshMounted(); AssertApplied(w, tune);
        });
        Run("reset cannot undo stock when low gearing coincides exactly with stock ratios", delegate(World w) {
            w.Transmission.Gear = 0; TuneData tune = LowTune(w); tune.LowRangeRatio = 2f;
            for (int i = 0; i < tune.Ratios.Length; i++) tune.Ratios[i] = w.Native.Ratios[i] / 2f;
            Commit(w, tune);
            for (int i = 0; i < tune.Ratios.Length; i++) Assert(w.Transmission.gears[i] == w.Native.Ratios[i], "Fixture does not reproduce the equality collision.");
            string error; Assert(w.State.Reset(w.Vehicle, out error), error); AssertApplied(w, w.Native);
            Assert(w.State.ExportTuning() == String.Empty, "Reset collision retained saved low range.");
        });
        Run("suspend cannot undo stock when low gearing coincides exactly with stock ratios", delegate(World w) {
            w.Transmission.Gear = 0; TuneData tune = LowTune(w); tune.LowRangeRatio = 2f;
            for (int i = 0; i < tune.Ratios.Length; i++) tune.Ratios[i] = w.Native.Ratios[i] / 2f;
            Commit(w, tune);
            for (int i = 0; i < tune.Ratios.Length; i++) Assert(w.Transmission.gears[i] == w.Native.Ratios[i], "Fixture does not reproduce the equality collision.");
            Plugin.Enabled.Value = false; GearboxState.Suspend(); AssertApplied(w, w.Native);
            Assert(w.State.Tune.LowRangeSelected && w.State.Tune.Ratios[2] == 1.6f, "Suspend collision discarded base tuning.");
            Plugin.Enabled.Value = true; GearboxState.RefreshMounted(); AssertApplied(w, tune);
        });
        Run("loose engine selector availability saves without changing its former car", delegate(World w) {
            DriveControl existing = DriveControl.GetOrCreate(w.Vehicle); existing.Enabled = true; existing.FourWheelSelected = false;
            w.Engine.transform.parent = null; w.Engine.tag = "item";
            foreach (bool enabled in new bool[] { false, true }) {
                TuneData desired = Desired(w); desired.DriveSelectorEnabled = enabled; Commit(w, desired);
                TuneData saved; string error; Assert(TuneData.TryParse(w.State.ExportTuning(), out saved, out error), error);
                Assert(saved.DriveSelectorEnabled == enabled, "Loose engine did not save its requested availability.");
                Assert(existing.Enabled && !existing.FourWheelSelected && existing.CommitCalls == 0,
                    "Loose engine changed a car it was no longer installed in.");
                AssertApplied(w, w.Native);
            }
        });
        Run("saved loose engine enables its installed selector once without continuously overwriting the car", delegate(World w) {
            w.Engine.transform.parent = null; w.Engine.tag = "item";
            TuneData desired = Desired(w); desired.DriveSelectorEnabled = true; Commit(w, desired);
            string saved = w.State.ExportTuning(); w.State.Restore(saved, w.State.ExportStock());
            w.Transmission.Gear = 0; NativeMount(w); GearboxState.RefreshMounted();
            DriveControl control; Assert(DriveControl.TryGet(w.Vehicle, out control) && control.Enabled, "Installed engine did not enable the selector.");
            Assert(control.CommitCalls == 1, "Installation did not apply exactly one availability request.");
            control.Enabled = false;
            for (int i = 0; i < 5; i++) { GearboxState.RefreshMounted(); GearboxState.BeforeRatio(w.Transmission); }
            Assert(!control.Enabled && control.CommitCalls == 1, "Consumed engine request continuously overwrote the vehicle.");
        });
        Run("unsafe mounting defers selector activation until stopped neutral and not shifting", delegate(World w) {
            w.Engine.transform.parent = null; w.Engine.tag = "item";
            TuneData desired = Desired(w); desired.DriveSelectorEnabled = true; Commit(w, desired);
            NativeMount(w); w.Transmission.Gear = 1; w.Vehicle.TestVelocityMagnitude = 2f;
            GearboxState.RefreshMounted(); DriveControl control = DriveControl.GetOrCreate(w.Vehicle);
            Assert(!control.Enabled && control.CommitCalls == 0, "Unsafe first mount activated drivetrain routing.");
            AssertApplied(w, desired);
            w.Transmission.Gear = 0; GearboxState.BeforeRatio(w.Transmission);
            Assert(!control.Enabled, "Moving neutral activated routing.");
            w.Vehicle.TestVelocityMagnitude = 0f; w.Transmission.isShifting = true; GearboxState.RefreshMounted();
            Assert(!control.Enabled, "Pending native shift activated routing.");
            w.Transmission.isShifting = false; GearboxState.RefreshMounted();
            Assert(control.Enabled && control.CommitCalls == 1, "Safe parked mount did not consume the deferred choice.");
        });
        Run("unready vehicle conversion stays pending and retries without losing the engine choice", delegate(World w) {
            w.Engine.transform.parent = null; w.Engine.tag = "item";
            TuneData desired = Desired(w); desired.DriveSelectorEnabled = true; Commit(w, desired);
            DriveControl control = DriveControl.GetOrCreate(w.Vehicle); control.IsReady = false;
            NativeMount(w); w.Transmission.Gear = 0; GearboxState.RefreshMounted();
            Assert(!control.Enabled && w.State.Tune.DriveSelectorEnabled == true && control.CommitCalls == 0,
                "Unavailable native graph discarded the request or applied it.");
            control.IsReady = true; GearboxState.RefreshMounted();
            Assert(control.Enabled && control.CommitCalls == 1, "Completed native graph failed to consume the pending request.");
        });
        Run("legacy v4 engine tune preserves the existing vehicle selector and selected two wheel mode", delegate(World w) {
            DriveControl existing = DriveControl.GetOrCreate(w.Vehicle); existing.Enabled = true; existing.FourWheelSelected = false;
            TuneData desired = Desired(w); string[] fields = desired.Serialize().Split(';'); fields[0] = "4";
            w.State.Restore(String.Join(";", fields, 0, 13), w.Native.Serialize()); GearboxState.RefreshMounted();
            Assert(existing.Enabled && !existing.FourWheelSelected && existing.CommitCalls == 0 && existing.ValidateCalls == 0,
                "Legacy engine tune changed the saved 0.1.8 vehicle selector.");
            Assert(w.State.Tune.DriveSelectorEnabled == null, "Legacy tune acquired an explicit engine preference.");
        });
        Run("unspecified engine tune does not introduce a drive control on an untouched vehicle", delegate(World w) {
            TuneData desired = Desired(w); Assert(desired.DriveSelectorEnabled == null, "Fixture does not represent native or legacy capture.");
            Commit(w, desired); GearboxState.RefreshMounted(); GearboxState.BeforeRatio(w.Transmission);
            Assert(!DriveControl.States.ContainsKey(w.Vehicle), "Unspecified availability created a vehicle selector.");
        });
        Run("explicit loose disable preserves former car and disables only the next installed car", delegate(World w) {
            DriveControl existing = DriveControl.GetOrCreate(w.Vehicle); existing.Enabled = true; existing.FourWheelSelected = false;
            w.Engine.transform.parent = null; w.Engine.tag = "item";
            TuneData desired = Desired(w); desired.DriveSelectorEnabled = false; Commit(w, desired);
            Assert(existing.Enabled && !existing.FourWheelSelected, "Loose disable changed its former car.");
            w.Transmission.Gear = 0; NativeMount(w); GearboxState.RefreshMounted();
            Assert(!existing.Enabled && existing.CommitCalls == 1, "Installed explicit disable was not applied.");
        });
        Run("engine transfer applies availability to the new car without transferring its selected mode", delegate(World w) {
            w.Transmission.Gear = 0; TuneData desired = Desired(w); desired.DriveSelectorEnabled = true; Commit(w, desired);
            DriveControl first = DriveControl.GetOrCreate(w.Vehicle); first.FourWheelSelected = false; int prior = first.CommitCalls;
            TransmissionComponent oldTransmission = w.Transmission;
            GameObject secondCar = new GameObject("SecondCar"); VehicleController second = secondCar.AddComponent<VehicleController>();
            DriveControl destination = DriveControl.GetOrCreate(second); destination.Enabled = true; destination.FourWheelSelected = true;
            GameObject mount = new GameObject("hinge_engine"); mount.transform.parent = secondCar.transform; w.Engine.transform.parent = mount.transform;
            GearboxState.RefreshMounted();
            Assert(destination.CommitCalls == 0 && first.CommitCalls == prior, "Stale native vehicle link changed either car.");
            foreach (PlayMakerFSM fsm in w.Engine.GetComponents<PlayMakerFSM>()) if (fsm.FsmName == "CheckTag") fsm.FsmVariables.Objects["vehicle"].Value = secondCar;
            w.Mount = mount; w.Car = secondCar; w.Vehicle = second; w.Transmission = second.powertrain.transmission; w.Transmission.Gear = 0;
            NativeMount(w); GearboxState.RefreshMounted(); GearboxState.BeforeRatio(oldTransmission);
            Assert(first.Enabled && !first.FourWheelSelected && first.CommitCalls == prior, "Engine transfer changed its previous car's mode.");
            Assert(destination.Enabled && destination.FourWheelSelected, "Previous car's 2WD mode followed the engine.");
        });
        Run("mounted selector availability changes reject before any gearbox or native save writes", delegate(World w) {
            TuneData prior = Desired(w); Commit(w, prior); DriveControl control = DriveControl.GetOrCreate(w.Vehicle);
            TuneData desired = prior.Clone(); desired.DriveSelectorEnabled = true; desired.FinalDrive = 8f;
            RejectCommit(w, desired); Assert(!control.Enabled && control.CommitCalls == 0, "Unsafe editor change touched the drive control.");
            w.Transmission.Gear = 0; control.IsReady = false; RejectCommit(w, desired);
            control.IsReady = true; Commit(w, desired); Assert(control.Enabled, "Safe valid editor request was not applied.");
        });
        Run("loose engine reset cannot disable the previously installed vehicle selector", delegate(World w) {
            w.Transmission.Gear = 0; TuneData desired = Desired(w); desired.DriveSelectorEnabled = true; Commit(w, desired);
            DriveControl control = DriveControl.GetOrCreate(w.Vehicle); control.FourWheelSelected = false; int prior = control.CommitCalls;
            w.Engine.transform.parent = null; w.Engine.tag = "item"; string error;
            Assert(w.State.Reset(w.Vehicle, out error), error);
            Assert(control.Enabled && !control.FourWheelSelected && control.CommitCalls == prior, "Loose reset modified an unrelated car.");
        });
        Run("installed Reset validates drive disable before changing either gearbox or saved tune", delegate(World w) {
            w.Transmission.Gear = 0; TuneData desired = Desired(w); desired.DriveSelectorEnabled = true; Commit(w, desired);
            DriveControl control = DriveControl.GetOrCreate(w.Vehicle); control.FourWheelSelected = false;
            string saved = w.State.ExportTuning(), stock = w.State.ExportStock(); int prior = control.CommitCalls;
            w.Transmission.Gear = 1; string error;
            Assert(!w.State.Reset(w.Vehicle, out error) && !String.IsNullOrEmpty(error), "Unsafe installed reset disabled routing.");
            AssertApplied(w, desired);
            Assert(w.State.ExportTuning() == saved && w.State.ExportStock() == stock &&
                GearboxNativeDefaults.NativeFinalDrive[w.Engine] == desired.FinalDrive && control.Enabled &&
                !control.FourWheelSelected && control.CommitCalls == prior, "Rejected Reset changed native or saved values.");
            w.Transmission.Gear = 0; Assert(w.State.Reset(w.Vehicle, out error), error);
            Assert(!control.Enabled && w.State.ExportTuning() == String.Empty, "Safe Reset retained conversion or engine tuning.");
            AssertApplied(w, w.Native);
        });
        Run("incompatible saved gearbox cannot apply its engine selector preference", delegate(World w) {
            TuneData incompatible = Desired(w); incompatible.Ratios = new float[] { -2f, 0f, 3f }; incompatible.DriveSelectorEnabled = true;
            w.Transmission.Gear = 0; w.State.Restore(incompatible.Serialize(), String.Empty); GearboxState.RefreshMounted();
            DriveControl control;
            Assert(!DriveControl.TryGet(w.Vehicle, out control) || (!control.Enabled && control.CommitCalls == 0),
                "Unsupported saved gearbox changed the car's drive selector.");
            AssertApplied(w, w.Native);
        });
        Run("rev limit changes actual engine setting and survives saved tune restoration",delegate(World w){
            TuneData tune=Desired(w);tune.RevLimitRpm=6000f;Commit(w,tune);
            Assert(w.Vehicle.powertrain.engine.revLimiterRPM==6000f,"Native cutoff not updated.");
            string saved=w.State.ExportTuning(),stock=w.State.ExportStock();w.State.Restore(saved,stock);GearboxState.RefreshMounted();
            Assert(w.Vehicle.powertrain.engine.revLimiterRPM==6000f,"Saved rev limit was not applied.");
        });
        Run("lower rev limit caps native automatic shifts and disabling restores them",delegate(World w){
            TuneData tune=Desired(w);tune.TuneShifts=false;tune.RevLimitRpm=3000f;Commit(w,tune);
            Assert(w.Transmission.UpshiftRPM==2850f&&w.Transmission.DownshiftRPM<2850f,"Automatic shifts can hit the cutoff before shifting.");
            tune.RevLimitRpm=0f;Commit(w,tune);Assert(w.Vehicle.powertrain.engine.revLimiterRPM==5000f,"Checkbox off retained custom cutoff.");
            Assert(w.Transmission.UpshiftRPM==w.Native.ShiftUp&&w.Transmission.DownshiftRPM==w.Native.ShiftDown,"Native shifts were not restored.");
        });
        Run("engine step reapplies cutoff after native reset without writing RPM",delegate(World w){
            TuneData tune=Desired(w);tune.RevLimitRpm=6500f;Commit(w,tune);EngineComponent e=w.Vehicle.powertrain.engine;float rpm=e.OutputRPM;
            e.revLimiterRPM=5000f;GearboxState.BeforeEngineStep(e);Assert(e.revLimiterRPM==6500f&&e.OutputRPM==rpm,"Engine integration cutoff failed or RPM changed.");
        });
        Run("loose engine rev limit does not affect nearby vehicle",delegate(World w){
            w.Engine.transform.parent=null;w.Engine.tag="item";TuneData tune=Desired(w);tune.RevLimitRpm=6500f;Commit(w,tune);
            Assert(w.Vehicle.powertrain.engine.revLimiterRPM==5000f,"Loose engine changed chassis cutoff.");
            NativeMount(w);GearboxState.RefreshMounted();Assert(w.Vehicle.powertrain.engine.revLimiterRPM==6500f,"Installation lost saved engine limit.");
        });
        Run("reset and global suspension restore factory rev limit",delegate(World w){
            TuneData tune=Desired(w);tune.RevLimitRpm=6500f;Commit(w,tune);string error;
            Assert(w.State.Reset(w.Vehicle,out error),error);Assert(w.Vehicle.powertrain.engine.revLimiterRPM==5000f,"Reset retained limit.");
            Commit(w,tune);GearboxState.Suspend();Assert(w.Vehicle.powertrain.engine.revLimiterRPM==5000f,"Global disable retained limit.");
        });
        Run("invalid engine-specific rev limit is rejected before any tuning changes",delegate(World w){
            TuneData initial=Desired(w);Commit(w,initial);TuneData lower=initial.Clone();lower.TuneShifts=false;lower.RevLimitRpm=1000f;RejectCommit(w,lower);
            w.Vehicle.powertrain.engine.engineType=EngineComponent.EngineType.Electric;lower.RevLimitRpm=6000f;RejectCommit(w,lower);
        });
        Run("replacement engine sharing previous custom cutoff is not overwritten",delegate(World w){
            TuneData tune=Desired(w);tune.RevLimitRpm=6000f;Commit(w,tune);w.Engine.transform.parent=null;w.Engine.tag="item";
            GameObject other=new GameObject("Replacement engine");other.transform.parent=w.Mount.transform;other.tag="vehPart";
            Fsm(other,"ID").FsmVariables.Strings["ID"]=new FsmString{Value="engine"};Fsm(other,"CheckTag").FsmVariables.Objects["vehicle"]=new FsmGameObject{Value=w.Car};
            w.Vehicle.powertrain.engine.revLimiterRPM=6000f;GearboxState.RefreshMounted();
            Assert(w.Vehicle.powertrain.engine.revLimiterRPM==6000f,"Old engine overwrote replacement's identical native cutoff.");
        });
        Run("rev limiter preserves other writers and refreshes native power cache",delegate(World w){
            EngineComponent e=w.Vehicle.powertrain.engine;RevLimitControl c=new RevLimitControl();int before=e.PeakRefreshes;
            c.Update(e,6500f,5000f);Assert(e.PeakRefreshes==before+1,"Power/torque cache was not refreshed.");
            c.Update(e,6500f,5000f);Assert(e.PeakRefreshes==before+1,"Unchanged cutoff caused redundant cache work.");
            e.revLimiterRPM=4800f;c.Restore();Assert(e.revLimiterRPM==4800f,"Restore overwrote another writer.");
            c.Update(e,6000f,5000f);e.revLimiterRPM=4900f;c.Update(e,6000f,5000f);c.Restore();Assert(e.revLimiterRPM==4900f,"New native baseline was lost.");
        });
        Run("missing native defaults fail closed before drive and cutoff overrides", delegate(World w) {
            TuneData tune = Desired(w); tune.DriveSelectorEnabled = true; tune.RevLimitRpm = 6500f;
            w.Transmission.Gear = 0; GearboxNativeDefaults.Defaults.Remove(w.Engine);
            w.State.Restore(tune.Serialize(), String.Empty); GearboxState.RefreshMounted();
            Assert(!String.IsNullOrEmpty(w.State.Status), "Unsupported native actions have no failure message.");
            DriveControl control;
            Assert(!DriveControl.TryGet(w.Vehicle, out control) || !control.Enabled, "Rejected tune enabled the drive selector.");
            Assert(GearboxState.ForEngine(w.Vehicle.powertrain.engine) == null && GearboxState.ForTransmission(w.Transmission) == null,
                "Rejected native defaults remain available to assist consumers.");
            GearboxState.BeforeEngineStep(w.Vehicle.powertrain.engine);
            Assert(w.Vehicle.powertrain.engine.revLimiterRPM == 5000f && w.Transmission.gears[2] == w.Native.Ratios[2],
                "Unsupported native actions changed physical tuning.");
        });
        Run("saved stock cannot authorize an engine with missing native actions", delegate(World w) {
            TuneData tune = Desired(w); tune.RevLimitRpm = 6500f;
            GearboxNativeDefaults.Defaults.Remove(w.Engine); w.State.Restore(tune.Serialize(), w.Native.Serialize());
            GearboxState.RefreshMounted(); Assert(GearboxState.ForEngine(w.Vehicle.powertrain.engine) == null,
                "Saved stock authorized unsupported live engine actions.");
        });
        Run("native actions becoming ready recover the deferred tune safely", delegate(World w) {
            TuneData tune = Desired(w); tune.DriveSelectorEnabled = true; tune.RevLimitRpm = 6500f;
            w.Transmission.Gear = 0; GearboxNativeDefaults.Defaults.Remove(w.Engine);
            w.State.Restore(tune.Serialize(), String.Empty); GearboxState.RefreshMounted();
            GearboxNativeDefaults.Defaults[w.Engine] = w.Native; GearboxState.RefreshMounted();
            DriveControl control; AssertApplied(w, tune);
            Assert(GearboxState.ForEngine(w.Vehicle.powertrain.engine) == w.State &&
                DriveControl.TryGet(w.Vehicle, out control) && control.Enabled && w.Vehicle.powertrain.engine.revLimiterRPM == 6500f,
                "Ready native actions did not recover tuning and deferred drive selection.");
        });
        Run("CVT transition rejects every consumer and restores owned limits and assists", delegate(World w) {
            TuneData tune = Desired(w); tune.RevLimitRpm = 6500f; tune.SpeedLimitKph = 100f; Commit(w, tune);
            DrivingAssist assist = DrivingAssist.Instances[w.Vehicle]; Assert(assist.Applied, "Assist fixture was not applied.");
            w.Transmission.transmissionType = TransmissionComponent.TransmissionShiftType.CVT;
            GearboxState.BeforeEngineStep(w.Vehicle.powertrain.engine);
            Assert(w.Vehicle.powertrain.engine.revLimiterRPM == 5000f && !assist.Applied &&
                w.Transmission.UpshiftRPM == w.Native.ShiftUp && w.Transmission.DownshiftRPM == w.Native.ShiftDown,
                "Rejected CVT retained owned cutoff, automatic shifts or assist.");
            Assert(GearboxState.ForEngine(w.Vehicle.powertrain.engine) == null && GearboxState.ForTransmission(w.Transmission) == null,
                "Rejected CVT still publishes tuning.");
            w.Transmission.transmissionType = TransmissionComponent.TransmissionShiftType.Manual;
            GearboxState.BeforeRatio(w.Transmission); AssertApplied(w, tune);
            Assert(GearboxState.ForEngine(w.Vehicle.powertrain.engine) == w.State && assist.Applied && w.Vehicle.powertrain.engine.revLimiterRPM == 6500f,
                "Recovered supported gearbox did not reapply owned overrides.");
        });
        Run("live gear count mutation rejects cutoff and preserves the replacement list", delegate(World w) {
            TuneData tune = Desired(w); tune.RevLimitRpm = 6500f; Commit(w, tune);
            List<float> replacement = new List<float>(new float[] {-2f,0f,4f}); w.Transmission.gears = replacement;
            Assert(GearboxState.ForEngine(w.Vehicle.powertrain.engine) == null && w.Vehicle.powertrain.engine.revLimiterRPM == 5000f,
                "Changed gear count retained its cutoff.");
            Assert(Object.ReferenceEquals(w.Transmission.gears, replacement) && replacement[2] == 4f,
                "Rejecting a new native list overwrote its ratios.");
        });
        Run("same-count gear sign mutation invalidates cached tuning immediately", delegate(World w) {
            TuneData tune = Desired(w); tune.RevLimitRpm = 6500f; Commit(w, tune);
            w.Transmission.gears[2] = -4f; Assert(GearboxState.ForTransmission(w.Transmission) == null,
                "Changed native signs retained cached validation.");
            Assert(w.Transmission.gears[2] == -4f && !DrivingAssist.Instances[w.Vehicle].Applied && w.Vehicle.powertrain.engine.revLimiterRPM == 5000f,
                "Invalid signs retained assists or overwrote native values.");
        });
        Run("binding rejection preserves newer external cutoff and shift writes", delegate(World w) {
            TuneData tune = Desired(w); tune.RevLimitRpm = 6500f; Commit(w, tune);
            w.Vehicle.powertrain.engine.revLimiterRPM = 4800f; w.Transmission.UpshiftRPM = 3900f; w.Transmission.DownshiftRPM = 1200f;
            w.Transmission.transmissionType = TransmissionComponent.TransmissionShiftType.CVT; GearboxState.BeforeRatio(w.Transmission);
            Assert(w.Vehicle.powertrain.engine.revLimiterRPM == 4800f && w.Transmission.UpshiftRPM == 3900f && w.Transmission.DownshiftRPM == 1200f,
                "Failure cleanup overwrote another writer's settings.");
        });
        Run("engine mapping follows component replacement and removes the old owner", delegate(World w) {
            TuneData tune = Desired(w); tune.RevLimitRpm = 6500f; Commit(w, tune);
            EngineComponent old = w.Vehicle.powertrain.engine, replacement = new EngineComponent { revLimiterRPM = 5100f };
            w.Vehicle.powertrain.engine = replacement;
            Assert(GearboxState.ForEngine(old) == null && GearboxState.ForEngine(replacement) == null,
                "Stale component swap published a tune before reconciliation.");
            Assert(old.revLimiterRPM == 5000f && replacement.revLimiterRPM == 5100f,
                "Stale cleanup altered the replacement engine.");
            GearboxState.RefreshMounted(); Assert(GearboxState.ForEngine(old) == null && GearboxState.ForEngine(replacement) == w.State && replacement.revLimiterRPM == 6500f,
                "Reconciliation failed to map the new engine component.");
        });
        Run("detachment invalidates mapped engine before the periodic runner", delegate(World w) {
            TuneData tune = Desired(w); tune.RevLimitRpm = 6500f; Commit(w, tune);
            w.Engine.transform.parent = null;
            Assert(GearboxState.ForEngine(w.Vehicle.powertrain.engine) == null && w.Vehicle.powertrain.engine.revLimiterRPM == 5000f,
                "Detached engine retained its direct mapping or cutoff.");
        });
        Run("live idle changes invalidate rev-limit validation without FSM rescanning", delegate(World w) {
            TuneData tune = Desired(w); tune.TuneShifts = false; tune.RevLimitRpm = 3000f; Commit(w, tune);
            int reads = CalculatorEngineSpecs.RpmReads; w.Vehicle.powertrain.engine.idleRPM = 2500f;
            Assert(GearboxState.ForEngine(w.Vehicle.powertrain.engine) == null && w.Vehicle.powertrain.engine.revLimiterRPM == 5000f,
                "Higher native idle retained an invalid cutoff.");
            Assert(CalculatorEngineSpecs.RpmReads == reads, "Live idle validation rescanned native FSM actions.");
            w.Vehicle.powertrain.engine.idleRPM = 800f; GearboxState.BeforeRatio(w.Transmission);
            Assert(GearboxState.ForEngine(w.Vehicle.powertrain.engine) == w.State && w.Vehicle.powertrain.engine.revLimiterRPM == 3000f,
                "Valid idle recovery failed.");
        });
        Run("cached validation detects scalar and ratio mutations in place", delegate(World w) {
            TuneData tune = Desired(w); tune.RevLimitRpm = 6500f; Commit(w, tune);
            w.State.Tune.SpeedLimitKph = 700f;
            Assert(GearboxState.ForEngine(w.Vehicle.powertrain.engine) == null && !DrivingAssist.Instances[w.Vehicle].Applied,
                "Invalid scalar retained the cached tune.");
            w.State.Tune.SpeedLimitKph = 0f; GearboxState.BeforeRatio(w.Transmission);
            Assert(GearboxState.ForEngine(w.Vehicle.powertrain.engine) == w.State, "Corrected scalar did not recover validation.");
            w.State.Tune.Ratios[2] = Single.NaN;
            Assert(GearboxState.ForTransmission(w.Transmission) == null, "NaN ratio retained the cached tune.");
        });
        Run("steady physics reuses validation snapshot and native metadata", delegate(World w) {
            TuneData tune = Desired(w); tune.RevLimitRpm = 6500f; Commit(w, tune);
            var field = typeof(GearboxState).GetField("validatedSnapshot", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            object snapshot = field.GetValue(w.State); int reads = CalculatorEngineSpecs.RpmReads, captures = GearboxNativeDefaults.Captures;
            for (int i = 0; i < 40; i++) {
                GearboxState.BeforeRatio(w.Transmission); GearboxState.BeforeEngineStep(w.Vehicle.powertrain.engine);
                Assert(GearboxState.ForEngine(w.Vehicle.powertrain.engine) == w.State, "Stable engine mapping was lost.");
            }
            Assert(Object.ReferenceEquals(snapshot, field.GetValue(w.State)) && CalculatorEngineSpecs.RpmReads == reads && GearboxNativeDefaults.Captures == captures,
                "Steady physics rebuilt its tune snapshot or rescanned native actions.");
        });
        Run("native final drive edit updates saved and physical ratios immediately", delegate(World w) {
            TuneData tune = Desired(w); Commit(w, tune); LegacyFinalDrive.Edits[w.Engine] = 4.6f;
            GearboxState.NativeFinalDriveEdited(w.Engine);
            Assert(w.State.Tune.FinalDrive == 4.6f && w.Transmission.finalGearRatio == 4.6f && GearboxNativeDefaults.NativeFinalDrive[w.Engine] == 4.6f,
                "Native final drive edit waited for periodic runner synchronization.");
            LegacyFinalDrive.Edits[w.Engine] = 3.5f; GearboxState.NativeFinalDriveEdited(w.Engine);
            Assert(w.State.Tune.FinalDrive == 3.5f && w.Transmission.finalGearRatio == 3.5f,
                "Latest native reset edit did not win immediately.");
        });
        Run("temporary rejection and recovery retain a newer external cutoff baseline", delegate(World w) {
            TuneData tune = Desired(w); tune.RevLimitRpm = 6500f; Commit(w, tune);
            w.Vehicle.powertrain.engine.revLimiterRPM = 4800f;
            w.Transmission.transmissionType = TransmissionComponent.TransmissionShiftType.CVT;
            GearboxState.BeforeRatio(w.Transmission); Assert(w.Vehicle.powertrain.engine.revLimiterRPM == 4800f, "Rejection lost external cutoff.");
            w.Transmission.transmissionType = TransmissionComponent.TransmissionShiftType.Manual;
            GearboxState.BeforeRatio(w.Transmission); tune.RevLimitRpm = 0f; Commit(w, tune);
            Assert(w.Vehicle.powertrain.engine.revLimiterRPM == 4800f, "Recovery forgot the external cutoff restore baseline.");
        });
        Console.WriteLine("RESULT: " + passed + " passed, " + failed + " failed.");
        return failed == 0 ? 0 : 1;
    }
}
