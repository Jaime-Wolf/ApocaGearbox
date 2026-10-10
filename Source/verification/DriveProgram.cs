using System;
using System.Collections.Generic;
using System.Reflection;
using ApocaGearbox;
using NWH.Common.Vehicles;
using NWH.VehiclePhysics2;
using NWH.VehiclePhysics2.Powertrain;
using UnityEngine;

internal static class DriveProgram
{
    private static int passed, failed, nativeSplits;
    private static float splitAngularA, splitAngularB, splitInertiaA, splitInertiaB, splitDelta;
    private sealed class World
    {
        internal VehicleController Vehicle;
        internal DriveControl Control;
        internal TransmissionComponent Transmission;
        internal DifferentialComponent Root, Front, Rear;
        internal WheelComponent[] Wheels;
    }
    private static DifferentialComponent Diff(string name, PowertrainComponent a, PowertrainComponent b)
    {
        DifferentialComponent result = new DifferentialComponent { name = name, inertia = 0.5f, Output = a, OutputB = b };
        result.DifferentialType = DifferentialComponent.Type.Open; return result;
    }
    private static World Build(string mode)
    {
        World w = new World(); w.Vehicle = new GameObject().AddComponent<VehicleController>();
        w.Transmission = w.Vehicle.powertrain.transmission;
        string[] names = mode == "middle" ? new string[] { "hinge_wheel_F", "hinge_wheel_M", "hinge_wheel_RL", "hinge_wheel_RR" } :
            new string[] { "hinge_wheel_FL", "hinge_wheel_FR", "hinge_wheel_RL", "hinge_wheel_RR" };
        float[] angular = { 100f, 120f, 10f, 30f };
        w.Wheels = new WheelComponent[4];
        for (int i = 0; i < 4; i++) {
            w.Wheels[i] = new WheelComponent { name = names[i], InitialInertia = i + 1f,
                wheelUAPI = new WheelUAPI { Inertia = i + 1f, AngularVelocity = angular[i], CounterTorque = (i + 1f) * 10f, BrakeTorque = 99f } };
            w.Vehicle.powertrain.wheels.Add(w.Wheels[i]);
        }
        w.Front = Diff("Front Differential", w.Wheels[0], w.Wheels[1]);
        w.Rear = Diff("Rear Differential", w.Wheels[2], w.Wheels[3]);
        w.Root = mode == "awd" ? Diff("Center Differential", w.Front, w.Rear) :
            mode == "front" ? w.Front : mode == "middle" ? Diff("Rear Differential", w.Wheels[1], w.Wheels[1]) : w.Rear;
        w.Transmission.Output = w.Root;
        w.Control = DriveControl.GetOrCreate(w.Vehicle);
        Assert(w.Control != null, "Stock fixture could not register."); return w;
    }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Near(float actual, float expected, string message)
    { Assert(Math.Abs(actual - expected) < 0.0001f, message + ": got " + actual + ", expected " + expected); }
    private static void Run(string name, Action<World> action)
    {
        try {
            Time.fixedTime += 0.02f;
            DriveControl.SuspendAll(); DriveControl.Controls.Clear(); Plugin.Enabled.Value = true; nativeSplits = 0;
            action(Build("rear")); passed++; Console.WriteLine("PASS " + name);
        }
        catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
    }
    private static MethodInfo Patch(string name)
    { return typeof(DriveControl).GetNestedType(name, BindingFlags.NonPublic).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic); }
    private static bool Angular(World w, out float value)
    {
        object[] args = { w.Root, 200f, 0.02f, -999f };
        bool native = (bool)Patch("AngularPatch").Invoke(null, args); value = (float)args[3]; return native;
    }
    private static bool Inertia(World w, out float value)
    {
        object[] args = { w.Root, -999f };
        bool native = (bool)Patch("InertiaPatch").Invoke(null, args); value = (float)args[1]; return native;
    }
    private static bool Forward(World w, float torque, float inertia, out float value)
    {
        object[] args = { w.Root, torque, inertia, 0.02f, -999f };
        bool native = (bool)Patch("ForwardPatch").Invoke(null, args); value = (float)args[4]; return native;
    }
    private static void Enable(World w, bool four)
    {
        string error; Assert(w.Control.CommitEnabled(true, out error), error);
        Assert(w.Control.TrySetFourWheel(four, out error), error);
    }
    private static void NextPhysics(World w)
    {
        Time.fixedTime += 0.02f;
        typeof(DriveControl).GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(w.Control, null);
    }
    private static void Routed(World w, float torque, float upstream, float[] expected, float feedback)
    {
        float result; Assert(!Forward(w, torque, upstream, out result), "Expected active routing."); Near(result, feedback, "Countertorque feedback");
        for (int i = 0; i < 4; i++) {
            Assert(w.Wheels[i].Steps == 1, "Wheel " + i + " did not step exactly once.");
            Near(w.Wheels[i].LastTorque, expected[i], "Wheel " + i + " torque");
            Near(w.Wheels[i].LastDelta, 0.02f, "Wheel timestep");
            Near(w.Wheels[i].wheelUAPI.BrakeTorque, 99f, "Driver wheel brake was changed");
        }
    }
    private static void QuarterSplit(float torque, float a, float b, float ia, float ib, float dt,
        float bias, float stiffness, float power, float coast, float slip, out float ta, out float tb)
    {
        nativeSplits++; splitAngularA = a; splitAngularB = b; splitInertiaA = ia; splitInertiaB = ib; splitDelta = dt;
        ta = torque * 0.25f; tb = torque * 0.75f;
    }
    private static void RejectSwitch(World w, bool four)
    {
        string before = w.Control.Export(), error;
        Assert(!w.Control.TrySetFourWheel(four, out error) && !String.IsNullOrEmpty(error), "Unsafe mode change accepted.");
        Assert(w.Control.Export() == before, "Rejected mode change mutated persistence.");
        foreach (WheelComponent wheel in w.Wheels) Assert(wheel.Steps == 0, "Rejected switch ran physics.");
    }
    private static int Main()
    {
        Run("calculator samples selected torque shares without running wheel physics", delegate(World w) {
            Dictionary<WheelComponent,float> shares=new Dictionary<WheelComponent,float>();
            Assert(!DriveControl.TryCalculatorShares(w.Vehicle,shares),"Disabled selector reported override shares.");
            Enable(w,true);Assert(DriveControl.TryCalculatorShares(w.Vehicle,shares)&&shares.Count==4,"4WD shares omitted wheels.");
            foreach(WheelComponent wheel in w.Wheels) { Near(shares[wheel],0.25f,"4WD share");Assert(wheel.Steps==0,"Calculator stepped a wheel."); }
            Enable(w,false);shares.Clear();Assert(DriveControl.TryCalculatorShares(w.Vehicle,shares)&&shares.Count==2,"2WD shares omitted axle.");
            Near(shares[w.Wheels[2]],0.5f,"Rear left share");Near(shares[w.Wheels[3]],0.5f,"Rear right share");
            World middle=Build("middle");Enable(middle,false);shares.Clear();
            Assert(DriveControl.TryCalculatorShares(middle.Vehicle,shares)&&shares.Count==1,"Shared middle output duplicated.");Near(shares[middle.Wheels[1]],1f,"Middle wheel share");
            foreach(WheelComponent wheel in middle.Wheels)Assert(wheel.Steps==0,"Middle wheel was simulated by calculator.");
        });
        Run("optional control defaults off and leaves every native query untouched", delegate(World w) {
            float value; Assert(!w.Control.Enabled && Angular(w, out value) && Inertia(w, out value) && Forward(w, 100f, 8f, out value), "Default changed the stock graph.");
            foreach (WheelComponent wheel in w.Wheels) Assert(wheel.Steps == 0 && wheel.wheelUAPI.AutoSimulate, "Default claimed wheel physics.");
        });
        Run("registry is vehicle owned and survives an engine swap without propagation", delegate(World w) {
            Enable(w, false); World second = Build("front"); DriveControl found;
            Assert(DriveControl.TryGet(w.Vehicle, out found) && Object.ReferenceEquals(found, w.Control), "Wrong vehicle registration.");
            w.Vehicle.powertrain.engine = new EngineComponent(); DriveControl.TickAll();
            Assert(w.Control.Enabled && !w.Control.FourWheelSelected && !second.Control.Enabled, "Engine swap transferred or cleared chassis mode.");
            Assert(Object.ReferenceEquals(DriveControl.GetOrCreate(w.Vehicle), w.Control), "Repeated lookup created a second control.");
        });
        Run("unsupported trailers duplicate wheel identities and unknown wheel names cannot enable", delegate(World w) {
            VehicleController trailer = new GameObject().AddComponent<VehicleController>();
            DriveControl pending = DriveControl.GetOrCreate(trailer); string error;
            Assert(pending != null && !pending.CommitEnabled(true, out error) && !pending.Enabled, "Empty trailer conversion accepted.");
            w.Vehicle.powertrain.wheels[1].name = w.Wheels[0].name;
            w.Control.Restore(String.Empty);
            Assert(!w.Control.CommitEnabled(true, out error), "Duplicate identity accepted.");
            w.Wheels[1].name = "modded_axle"; w.Control.Restore(String.Empty);
            Assert(!w.Control.CommitEnabled(true, out error), "Unknown layout accepted.");
        });
        Run("four wheel conversion uses all four native tire steps with balanced torque", delegate(World w) {
            Enable(w, true); Routed(w, 200f, 8f, new float[] { 50f, 50f, 50f, 50f }, 100f);
        });
        Run("rear two wheel routing retains native splitter and ignores passive axle feedback", delegate(World w) {
            w.Root.splitTorqueDelegate = QuarterSplit; Enable(w, false);
            float value; Assert(!Angular(w, out value), "2WD query used stock graph."); Near(value, 20f, "Rear shaft feedback");
            Assert(!Inertia(w, out value), "2WD inertia used stock graph."); Near(value, 7.5f, "Rear driveline inertia");
            Routed(w, 200f, 8f, new float[] { 0f, 0f, 50f, 150f }, 70f);
            Assert(nativeSplits == 1, "Native axle splitter was bypassed.");
            Near(splitAngularA, 10f, "Native left angular input"); Near(splitAngularB, 30f, "Native right angular input");
            Near(splitInertiaA, 3f, "Native left inertia input"); Near(splitInertiaB, 4f, "Native right inertia input"); Near(splitDelta, 0.02f, "Native delegate timestep");
            Near(w.Wheels[0].LastUpstreamInertia, 0f, "Passive front reflected engine inertia");
            Near(w.Wheels[2].LastUpstreamInertia, 7f, "Rear branch native inertia formula"); Near(w.Wheels[2].wheelUAPI.Inertia, 10f, "Native effective wheel inertia");
        });
        Run("front wheel vehicle retains front drive in two wheel mode", delegate(World unused) {
            World w = Build("front"); w.Root.splitTorqueDelegate = QuarterSplit; Enable(w, false);
            float value; Assert(!Angular(w, out value), "Front feedback not routed."); Near(value, 110f, "Front shaft feedback");
            Routed(w, 200f, 8f, new float[] { 50f, 150f, 0f, 0f }, 30f);
        });
        Run("negative engine coast torque follows the same driven wheels", delegate(World w) {
            w.Root.splitTorqueDelegate = QuarterSplit; Enable(w, false);
            Routed(w, -200f, 8f, new float[] { 0f, 0f, -50f, -150f }, 70f);
        });
        Run("negative four wheel coast torque reaches all four tires", delegate(World w) {
            Enable(w, true); Routed(w, -200f, 8f, new float[] { -50f, -50f, -50f, -50f }, 100f);
        });
        Run("factory four wheel selection preserves native center behavior", delegate(World unused) {
            World w = Build("awd"); Enable(w, true); float value;
            Assert(Angular(w, out value) && Inertia(w, out value) && Forward(w, 200f, 8f, out value), "Factory AWD was replaced.");
            foreach (WheelComponent wheel in w.Wheels) Assert(wheel.Steps == 0 && wheel.wheelUAPI.AutoSimulate, "Factory AWD tire was claimed.");
        });
        Run("factory four wheel vehicle changes to rear drive with single native steps", delegate(World unused) {
            World w = Build("awd"); w.Rear.splitTorqueDelegate = QuarterSplit; Enable(w, false);
            float value; Assert(!Angular(w, out value), "Rear transfer query not routed."); Near(value, 20f, "Rear transfer feedback");
            Assert(!Inertia(w, out value), "Rear transfer inertia not routed."); Near(value, 8f, "Center plus rear inertia");
            Routed(w, 200f, 8f, new float[] { 0f, 0f, 50f, 150f }, 70f);
        });
        Run("duplicate middle outputs become one full torque physical step", delegate(World unused) {
            World w = Build("middle"); Enable(w, false); float value;
            Assert(!Angular(w, out value), "Middle feedback not routed."); Near(value, 120f, "Middle shaft speed");
            Routed(w, 200f, 8f, new float[] { 0f, 200f, 0f, 0f }, 20f);
        });
        Run("Crossbreed four wheel mode powers every actual wheel once", delegate(World unused) {
            World w = Build("middle"); Enable(w, true); Routed(w, 200f, 8f, new float[] { 50f, 50f, 50f, 50f }, 100f);
        });
        Run("shift forecast uses only effective driven wheel sensors", delegate(World w) {
            Enable(w, false); float angular;
            Assert(DriveControl.TryWheelShaftAngularVelocity(w.Transmission, out angular), "Effective sensors unavailable."); Near(angular, 20f, "Passive fast axle polluted RPM forecast");
            string error; Assert(w.Control.TrySetFourWheel(true, out error), error);
            Assert(DriveControl.TryWheelShaftAngularVelocity(w.Transmission, out angular), "4WD sensors unavailable."); Near(angular, 65f, "Four wheel RPM average");
            Assert(!DriveControl.TryWheelShaftAngularVelocity(new TransmissionComponent(), out angular), "Foreign transmission claimed.");
        });
        Run("native graph links hashes and differential settings remain unchanged", delegate(World w) {
            PowertrainComponent a = w.Root.Output, b = w.Root.OutputB; int ah = w.Root.outputNameHash, bh = w.Root.outputBNameHash;
            DifferentialComponent.SplitTorque splitter = w.Root.splitTorqueDelegate; float bias = w.Root.biasAB;
            Enable(w, true); float value; Forward(w, 200f, 8f, out value); string error; Assert(w.Control.TrySetFourWheel(false, out error), error);
            Assert(Object.ReferenceEquals(w.Transmission.Output, w.Root) && Object.ReferenceEquals(w.Root.Output, a) && Object.ReferenceEquals(w.Root.OutputB, b), "Shared links rewired.");
            Assert(w.Root.outputNameHash == ah && w.Root.outputBNameHash == bh && w.Root.biasAB == bias && w.Root.splitTorqueDelegate == splitter, "Native settings changed.");
        });
        Run("claimed wheels stop auto simulation before the first routed tick", delegate(World w) {
            Enable(w, true); foreach (WheelComponent wheel in w.Wheels) Assert(!wheel.wheelUAPI.AutoSimulate && wheel.Steps == 0, "Wheel could auto-step before its routed tick.");
            string error; Assert(w.Control.CommitEnabled(false, out error), error);
            foreach (WheelComponent wheel in w.Wheels) Assert(wheel.wheelUAPI.AutoSimulate && wheel.wheelUAPI.Inertia == wheel.InitialInertia, "Unstepped prepared wheel was not restored.");
        });
        Run("disable restores still owned wheel outputs and leaves native routing available", delegate(World w) {
            Enable(w, true); float value; Forward(w, 200f, 8f, out value); string error; Assert(w.Control.CommitEnabled(false, out error), error);
            NextPhysics(w);
            foreach (WheelComponent wheel in w.Wheels) Assert(wheel.wheelUAPI.AutoSimulate && wheel.wheelUAPI.Inertia == wheel.InitialInertia && wheel.wheelUAPI.MotorTorque == 0f, "Owned physics outputs remained.");
            Assert(Forward(w, 200f, 8f, out value), "Disabled control still intercepted native physics.");
        });
        Run("disable preserves foreign torque and inertia writes", delegate(World w) {
            Enable(w, true); float value; Forward(w, 200f, 8f, out value);
            w.Wheels[0].wheelUAPI.MotorTorque = 777f; w.Wheels[0].wheelUAPI.Inertia = 888f; string error;
            Assert(w.Control.CommitEnabled(false, out error), error);
            NextPhysics(w);
            Near(w.Wheels[0].wheelUAPI.MotorTorque, 777f, "Foreign torque was overwritten"); Near(w.Wheels[0].wheelUAPI.Inertia, 888f, "Foreign inertia was overwritten");
        });
        Run("suspend restores physics but retains per vehicle selection for resuming", delegate(World w) {
            Enable(w, false); string before = w.Control.Export(); Plugin.Enabled.Value = false; DriveControl.SuspendAll(); float value;
            Assert(w.Control.Export() == before && Forward(w, 200f, 8f, out value), "Suspend lost selection or intercepted native graph.");
            foreach (WheelComponent wheel in w.Wheels) Assert(wheel.wheelUAPI.AutoSimulate, "Suspend retained auto simulation ownership.");
            Plugin.Enabled.Value = true; DriveControl.TickAll(); Assert(!Forward(w, 200f, 8f, out value), "Resume failed to restore selected route.");
        });
        Run("inactive native powertrain is never claimed by the selector", delegate(World w) {
            Enable(w, true); w.Vehicle.powertrain.IsActive = false; DriveControl.TickAll(); float value;
            Assert(Forward(w, 200f, 8f, out value), "Disabled native drivetrain was intercepted.");
            foreach (WheelComponent wheel in w.Wheels) Assert(wheel.wheelUAPI.AutoSimulate && wheel.Steps == 0, "Disabled vehicle retained selector physics ownership.");
            w.Vehicle.powertrain.IsActive = true; DriveControl.TickAll();
            foreach (WheelComponent wheel in w.Wheels) Assert(!wheel.wheelUAPI.AutoSimulate && wheel.Steps == 0, "Waking vehicle was not prepared before physics.");
        });
        Run("native wake immediately reclaims wheel simulation before the periodic tick", delegate(World w) {
            Enable(w, true); float value; Forward(w, 200f, 8f, out value);
            w.Vehicle.powertrain.IsActive = false;
            foreach (WheelComponent wheel in w.Wheels) {
                wheel.wheelUAPI.MotorTorque = 0f; wheel.wheelUAPI.AutoSimulate = true;
                wheel.Steps = 0;
            }
            w.Vehicle.powertrain.IsActive = true;
            MethodInfo wake = typeof(DriveControl).GetNestedType("WakePatch", BindingFlags.NonPublic)
                .GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic);
            wake.Invoke(null, new object[] { new ManagerVehicleComponent(), true });
            wake.Invoke(null, new object[] { w.Vehicle.powertrain, false });
            foreach (WheelComponent wheel in w.Wheels) Assert(wheel.wheelUAPI.AutoSimulate && wheel.Steps == 0, "Unrelated or refused wake claimed wheels.");
            wake.Invoke(null, new object[] { w.Vehicle.powertrain, true });
            foreach (WheelComponent wheel in w.Wheels) Assert(!wheel.wheelUAPI.AutoSimulate && wheel.Steps == 0, "Wake did not reclaim simulation before physics.");
            Routed(w, 200f, 8f, new float[] { 50f, 50f, 50f, 50f }, 100f);
        });
        Run("unsafe mode changes and enablement reject atomically", delegate(World w) {
            Enable(w, false); w.Vehicle.VelocityMagnitude = 1f; RejectSwitch(w, true);
            w.Vehicle.VelocityMagnitude = 0f; w.Transmission.Gear = 1; RejectSwitch(w, true);
            w.Transmission.Gear = 0; w.Transmission.isShifting = true; RejectSwitch(w, true);
            w.Transmission.isShifting = false; w.Vehicle.VelocityMagnitude = Single.NaN; RejectSwitch(w, true);
            World second = Build("front"); second.Transmission.Gear = 1; string error;
            Assert(!second.Control.CommitEnabled(true, out error) && !second.Control.Enabled, "Unsafe first enable was accepted.");
        });
        Run("manual and automatic modes share the stopped neutral requirement", delegate(World w) {
            foreach (TransmissionComponent.TransmissionShiftType mode in new TransmissionComponent.TransmissionShiftType[] {
                TransmissionComponent.TransmissionShiftType.Manual, TransmissionComponent.TransmissionShiftType.Automatic }) {
                w.Transmission.transmissionType = mode; Enable(w, false); string error;
                Assert(w.Control.TrySetFourWheel(true, out error), error); Assert(w.Control.CommitEnabled(false, out error), error);
            }
            w.Transmission.transmissionType = TransmissionComponent.TransmissionShiftType.CVT; string invalid;
            Assert(!w.Control.CommitEnabled(true, out invalid), "CVT conversion accepted.");
        });
        Run("saved modes round trip independently for two vehicles", delegate(World w) {
            Enable(w, false); string saved = w.Control.Export(); World second = Build("front"); Enable(second, true);
            w.Control.Restore(saved); Assert(w.Control.Enabled && !w.Control.FourWheelSelected && second.Control.FourWheelSelected, "Per vehicle save mixed choices.");
            float value; Assert(!Angular(w, out value), "Restored route inactive."); Near(value, 20f, "Restored 2WD shaft");
            foreach (string bad in new string[] { null, "", "v2|1|0", "v1|true|0", "v1|1|false", "v1|1|0|extra" }) {
                w.Control.Restore(bad); Assert(!w.Control.Enabled && w.Control.FourWheelSelected, "Malformed save retained conversion.");
            }
        });
        Run("load before native assembly defers saved selection until graph is complete", delegate(World w) {
            Powertrain native = w.Vehicle.powertrain; w.Vehicle.powertrain = null;
            w.Control.Restore("v1|1|0"); Assert(w.Control.Enabled && !w.Control.FourWheelSelected && !w.Control.IsReady, "Early load lost saved selection or claimed incomplete graph.");
            w.Vehicle.powertrain = native; DriveControl.TickAll(); float value;
            Assert(!Angular(w, out value), "Completed native assembly did not bind deferred selection."); Near(value, 20f, "Deferred rear shaft feedback");
        });
        Run("foreign graph change falls back safely and can still be disabled", delegate(World w) {
            Enable(w, true); float value; Forward(w, 200f, 8f, out value);
            WheelComponent foreign = new WheelComponent(); w.Root.Output = foreign; DriveControl.TickAll();
            Assert(Forward(w, 200f, 8f, out value) && !w.Control.IsReady, "Foreign route was overwritten or intercepted.");
            string error; Assert(w.Control.CommitEnabled(false, out error), "Could not opt out of invalid route: " + error);
            Assert(Object.ReferenceEquals(w.Root.Output, foreign), "Disable rewired a foreign edit.");
        });
        Run("replaced transmission and sensor cannot silently inherit physics ownership", delegate(World w) {
            Enable(w, true); TransmissionComponent original = w.Transmission; w.Vehicle.powertrain.transmission = new TransmissionComponent { Output = w.Root };
            float value; Assert(Forward(w, 200f, 8f, out value), "Old transmission route remained active.");
            w.Vehicle.powertrain.transmission = original; WheelUAPI replacement = new WheelUAPI { Inertia = 99f, MotorTorque = 66f };
            w.Wheels[0].wheelUAPI = replacement; DriveControl.TickAll();
            Assert(Forward(w, 200f, 8f, out value) && replacement.Inertia == 99f && replacement.MotorTorque == 66f && replacement.AutoSimulate,
                "Replacement sensor was claimed or overwritten.");
        });
        Run("throwing angular query stops override without escaping input hook", delegate(World w) {
            Enable(w, false); w.Wheels[2].ThrowAngular = true; float value;
            Assert(Angular(w, out value) && !w.Control.IsReady, "Angular API failure remained active.");
            foreach (WheelComponent wheel in w.Wheels) Assert(wheel.wheelUAPI.AutoSimulate, "Failed angular query retained wheel ownership.");
        });
        Run("throwing inertia query stops override without escaping input hook", delegate(World w) {
            Enable(w, false); w.Wheels[2].ThrowInertia = true; float value;
            Assert(Inertia(w, out value) && !w.Control.IsReady, "Inertia API failure remained active.");
        });
        Run("invalid native feedback fails safely", delegate(World w) {
            Enable(w, false); w.Wheels[2].wheelUAPI.AngularVelocity = Single.NaN; float value;
            Assert(Angular(w, out value) && !w.Control.IsReady, "Nonfinite angular feedback accepted.");
        });
        Run("partial wheel step failure never falls through and double steps tires", delegate(World w) {
            Enable(w, false); w.Wheels[1].ThrowStep = true; float value;
            Assert(!Forward(w, 200f, 8f, out value) && value == 0f && !w.Control.IsReady, "Partial step fell through to native graph.");
            foreach (WheelComponent wheel in w.Wheels) {
                Assert(wheel.Steps <= 1, "Wheel was stepped twice after failure.");
                if (wheel.wheelUAPI.AutoSimulate) wheel.Steps++;
                Assert(wheel.Steps <= 1, "Later WheelController auto update double-stepped a completed tire.");
                Assert(wheel.wheelUAPI.MotorTorque == 0f && wheel.wheelUAPI.Inertia == wheel.InitialInertia,
                    "Failed native step retained partial motor outputs.");
            }
            NextPhysics(w);
            foreach (WheelComponent wheel in w.Wheels) Assert(wheel.wheelUAPI.AutoSimulate, "Next tick did not restore automatic wheel stepping.");
            Assert(Forward(w, 200f, 8f, out value), "Failed route did not relinquish later physics ticks.");
        });
        Run("root map releases stale graph and transmission entries", delegate(World w) {
            Enable(w, true); string error; Assert(w.Control.CommitEnabled(false, out error), error);
            object roots = typeof(DriveControl).GetField("roots", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            object transmissions = typeof(DriveControl).GetField("transmissions", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            Assert(!((Dictionary<DifferentialComponent,DriveControl>)roots).ContainsKey(w.Root), "Disabled root still registered.");
            Assert(!((Dictionary<TransmissionComponent,DriveControl>)transmissions).ContainsKey(w.Transmission), "Disabled transmission still registered.");
            Enable(w, true); w.Transmission.Output = Diff("replacement", w.Wheels[0], w.Wheels[1]); float value;
            Assert(Forward(w, 10f, 2f, out value), "Stale root still intercepted native physics.");
            Assert(!((Dictionary<DifferentialComponent,DriveControl>)roots).ContainsKey(w.Root), "Invalidated root remains registered.");
            Assert(!((Dictionary<TransmissionComponent,DriveControl>)transmissions).ContainsKey(w.Transmission), "Invalidated transmission remains registered.");
        });
        Run("disabled unsupported controls never retry discovery", delegate(World w) {
            VehicleController empty = new GameObject().AddComponent<VehicleController>();
            DriveControl control = DriveControl.GetOrCreate(empty);
            FieldInfo next = typeof(DriveControl).GetField("nextCapture", BindingFlags.Instance | BindingFlags.NonPublic);
            float scheduled = (float)next.GetValue(control);
            Time.unscaledTime += 100f;
            for (int i = 0; i < 30; i++) DriveControl.TickAll();
            Near((float)next.GetValue(control), scheduled, "Disabled unsupported vehicle retried capture");
            control.Restore("v1|1|0"); float delay = (float)next.GetValue(control);
            for (int i = 0; i < 30; i++) DriveControl.TickAll();
            Near((float)next.GetValue(control), delay, "Failed enabled capture retried before its deadline");
            Plugin.Enabled.Value = false; Time.unscaledTime += 100f; DriveControl.TickAll();
            Near((float)next.GetValue(control), delay, "Globally disabled mod retried capture");
        });
        Run("reclaiming a deferred wheel retains its original auto setting", delegate(World w) {
            Enable(w, true); float value; Forward(w, 100f, 2f, out value);
            string error; Assert(w.Control.TrySetFourWheel(false, out error), error);
            NextPhysics(w);
            foreach (WheelComponent wheel in w.Wheels) Assert(!wheel.wheelUAPI.AutoSimulate, "Pending restore undid a fresh routing claim.");
            Assert(w.Control.CommitEnabled(false, out error), error); NextPhysics(w);
            foreach (WheelComponent wheel in w.Wheels) Assert(wheel.wheelUAPI.AutoSimulate, "Reclaim lost the original automatic setting.");
        });
        Console.WriteLine("RESULT: " + passed + " passed, " + failed + " failed.");
        return failed == 0 ? 0 : 1;
    }
}
