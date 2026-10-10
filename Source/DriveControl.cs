using System;
using System.Collections.Generic;
using HarmonyLib;
using NWH.Common.Vehicles;
using NWH.VehiclePhysics2;
using NWH.VehiclePhysics2.Powertrain;
using UnityEngine;

namespace ApocaGearbox
{
    // Vehicle-owned transfer routing. Shared native graph references and axle
    // settings remain intact; each routed wheel uses its native physics step.
    [DefaultExecutionOrder(80)]
    internal sealed class DriveControl : MonoBehaviour
    {
        internal static readonly List<DriveControl> Controls = new List<DriveControl>();
        private static readonly Dictionary<DifferentialComponent, DriveControl> roots = new Dictionary<DifferentialComponent, DriveControl>();
        private static readonly Dictionary<TransmissionComponent, DriveControl> transmissions = new Dictionary<TransmissionComponent, DriveControl>();
        internal VehicleController Vehicle { get; private set; }
        internal bool Enabled { get; private set; }
        internal bool FourWheelSelected { get; private set; }
        internal bool IsReady { get { return Ready(); } }
        private TransmissionComponent transmission;
        private DifferentialComponent nativeRoot;
        private WheelComponent[] wheels;
        private WheelUAPI[] sensors;
        private Node twoWheel, fourWheel;
        private bool[] twoWheelDriven, fourWheelDriven;
        private bool factoryFourWheel;
        private bool suspended;
        private bool failed;
        private int transmissionOutputHash;
        private readonly List<WheelOwnership> wheelOwnership = new List<WheelOwnership>();
        private readonly List<WheelOwnership> pendingAutoSimulation = new List<WheelOwnership>();
        private readonly List<Link> links = new List<Link>();
        private float nextCapture, captureDelay = 0.2f;
        private Powertrain attemptedPowertrain;
        private TransmissionComponent attemptedTransmission;
        private PowertrainComponent attemptedRoot;
        private int attemptedWheelCount = -1;
        private static float nextWarning;

        private sealed class Link
        {
            internal DifferentialComponent Component;
            internal PowertrainComponent A, B;
            internal int AHash, BHash;
        }

        private sealed class WheelOwnership
        {
            internal WheelUAPI Sensor;
            internal bool AutoSimulate;
            internal float Inertia, LastInertia, LastTorque;
            internal bool Stepped;
            internal float StepTime;
        }

        private sealed class Node
        {
            internal WheelComponent Wheel;
            internal DifferentialComponent Differential;
            internal Node A, B;

            internal float Angular(float input, float dt)
            {
                if (Wheel != null) return Wheel.QueryAngularVelocity(input, dt);
                if (B == null) return A.Angular(input, dt);
                return (A.Angular(input, dt) + B.Angular(input, dt)) * 0.5f;
            }

            internal float Inertia()
            {
                if (Wheel != null) return Wheel.QueryInertia();
                return Differential.inertia + A.Inertia() + (B == null ? 0f : B.Inertia());
            }

            internal float Forward(float torque, float upstreamInertia, float angular, float dt,
                DriveControl owner)
            {
                if (Wheel != null) return owner.StepWheel(Wheel, torque, upstreamInertia, dt);
                if (B == null) return A.Forward(torque, upstreamInertia + A.Inertia(), angular, dt, owner);
                float torqueA, torqueB;
                float inertiaA = A.Inertia(), inertiaB = B.Inertia();
                Differential.splitTorqueDelegate(torque, A.Angular(angular, dt), B.Angular(angular, dt),
                    inertiaA, inertiaB, dt, Differential.biasAB, Differential.stiffness,
                    Differential.powerRamp, Differential.coastRamp, Differential.slipTorque,
                    out torqueA, out torqueB);
                if (!TuneData.Finite(torqueA) || !TuneData.Finite(torqueB))
                    throw new InvalidOperationException("Differential produced invalid torque.");
                return A.Forward(torqueA, upstreamInertia * 0.5f + inertiaA, angular, dt, owner) +
                    B.Forward(torqueB, upstreamInertia * 0.5f + inertiaB, angular, dt, owner);
            }

            internal void Driven(HashSet<WheelComponent> set)
            {
                if (Wheel != null) { set.Add(Wheel); return; }
                A.Driven(set); if (B != null) B.Driven(set);
            }

            internal void Shares(Dictionary<WheelComponent, float> set, float fraction)
            {
                if (Wheel != null) { set[Wheel] = fraction; return; }
                if (B == null) { A.Shares(set, fraction); return; }
                float bias = Math.Max(0f, Math.Min(1f, Differential.biasAB));
                A.Shares(set, fraction * bias); B.Shares(set, fraction * (1f - bias));
            }

            internal bool Shaft(out float angular)
            {
                angular = 0f;
                if (Wheel != null)
                {
                    if (Wheel.wheelUAPI == null) return false;
                    angular = Wheel.wheelUAPI.AngularVelocity;
                    return TuneData.Finite(angular);
                }
                float a, b;
                if (!A.Shaft(out a)) return false;
                if (B == null) { angular = a; return true; }
                if (!B.Shaft(out b)) return false;
                angular = (a + b) * 0.5f;
                return TuneData.Finite(angular);
            }
        }

        internal static bool TryGet(VehicleController vehicle, out DriveControl state)
        {
            state = null;
            if (vehicle == null) return false;
            state = vehicle.GetComponent<DriveControl>();
            return state != null && state.Vehicle == vehicle;
        }

        // Snapshot only; never step a wheel or change the selected drive mode.
        internal static bool TryCalculatorShares(VehicleController vehicle, Dictionary<WheelComponent, float> shares)
        {
            DriveControl state;
            if (!TryGet(vehicle, out state) || !state.Active()) return false;
            state.SelectedNode.Shares(shares, 1f);
            return shares.Count > 0;
        }

        internal static DriveControl GetOrCreate(VehicleController vehicle)
        {
            // Native save loading can precede powertrain initialization. Keep
            // the vehicle's saved selection until its physical graph is ready.
            if (vehicle == null) return null;
            DriveControl state;
            if (TryGet(vehicle, out state)) return state;
            state = vehicle.gameObject.AddComponent<DriveControl>();
            state.Vehicle = vehicle;
            state.FourWheelSelected = true;
            Controls.Add(state);
            return state;
        }

        private static bool HasSupportedWheels(VehicleController vehicle)
        {
            if (vehicle.powertrain.wheels == null || vehicle.powertrain.wheels.Count != 4) return false;
            HashSet<string> names = new HashSet<string>();
            foreach (WheelComponent wheel in vehicle.powertrain.wheels)
                if (wheel == null || !names.Add(wheel.name)) return false;
            return (names.Contains("hinge_wheel_FL") && names.Contains("hinge_wheel_FR") &&
                    names.Contains("hinge_wheel_RL") && names.Contains("hinge_wheel_RR")) ||
                (names.Contains("hinge_wheel_F") && names.Contains("hinge_wheel_M") &&
                    names.Contains("hinge_wheel_RL") && names.Contains("hinge_wheel_RR"));
        }

        private static Node Leaf(WheelComponent wheel) { return new Node { Wheel = wheel }; }
        private static Node Axle(DifferentialComponent differential, Node a, Node b)
        { return new Node { Differential = differential, A = a, B = b }; }

        private static DifferentialComponent NewDifferential(DifferentialComponent source, bool locked)
        {
            DifferentialComponent result = new DifferentialComponent();
            result.inertia = source.inertia;
            result.biasAB = 0.5f;
            result.stiffness = source.stiffness;
            result.powerRamp = source.powerRamp;
            result.coastRamp = source.coastRamp;
            result.slipTorque = source.slipTorque;
            result.DifferentialType = locked ? DifferentialComponent.Type.Locked : DifferentialComponent.Type.Open;
            return result;
        }

        private Node NativeNode(PowertrainComponent component, HashSet<PowertrainComponent> path, int depth)
        {
            if (component == null || depth > 8 || !path.Add(component)) return null;
            try
            {
                WheelComponent wheel = component as WheelComponent;
                if (wheel != null) return Array.IndexOf(wheels, wheel) >= 0 ? Leaf(wheel) : null;
                DifferentialComponent differential = component as DifferentialComponent;
                if (differential == null || differential.splitTorqueDelegate == null) return null;
                PowertrainComponent a = differential.Output, b = differential.OutputB;
                links.Add(new Link { Component = differential, A = a, B = b,
                    AHash = differential.outputNameHash, BHash = differential.outputBNameHash });
                Node nodeA = NativeNode(a, path, depth + 1);
                if (nodeA == null) return null;
                // Crossbreed/Halfbreed point both native outputs at the same
                // middle wheel. Route its full torque in one physical step.
                if (System.Object.ReferenceEquals(a, b)) return Axle(differential, nodeA, null);
                Node nodeB = NativeNode(b, path, depth + 1);
                return nodeB == null ? null : Axle(differential, nodeA, nodeB);
            }
            finally { path.Remove(component); }
        }

        private bool Capture()
        {
            UnregisterGraph();
            attemptedPowertrain = Vehicle == null ? null : Vehicle.powertrain;
            attemptedTransmission = attemptedPowertrain == null ? null : attemptedPowertrain.transmission;
            attemptedRoot = attemptedTransmission == null ? null : attemptedTransmission.Output;
            attemptedWheelCount = attemptedPowertrain == null || attemptedPowertrain.wheels == null ? -1 : attemptedPowertrain.wheels.Count;
            bool captured = false;
            try
            {
                captured = CaptureCore();
                if (captured)
                {
                    twoWheelDriven = DrivenMask(twoWheel); fourWheelDriven = DrivenMask(fourWheel);
                    roots[nativeRoot] = this; transmissions[transmission] = this;
                }
                return captured;
            }
            catch (Exception error)
            {
                twoWheel = null; fourWheel = null; nativeRoot = null; sensors = null; links.Clear();
                Warn(error); return false;
            }
            finally
            {
                captureDelay = captured && sensors != null ? 0.2f : Math.Min(30f, captureDelay * 2f);
                nextCapture = Time.unscaledTime + captureDelay;
            }
        }

        private bool[] DrivenMask(Node node)
        {
            HashSet<WheelComponent> driven = new HashSet<WheelComponent>(); node.Driven(driven);
            bool[] mask = new bool[wheels.Length];
            for (int i = 0; i < wheels.Length; i++) mask[i] = driven.Contains(wheels[i]);
            return mask;
        }

        private void UnregisterGraph()
        {
            DriveControl owner;
            if (nativeRoot != null && roots.TryGetValue(nativeRoot, out owner) && System.Object.ReferenceEquals(owner, this)) roots.Remove(nativeRoot);
            if (transmission != null && transmissions.TryGetValue(transmission, out owner) && System.Object.ReferenceEquals(owner, this)) transmissions.Remove(transmission);
            twoWheelDriven = fourWheelDriven = null;
        }

        private bool AssemblyChanged()
        {
            Powertrain current = Vehicle == null ? null : Vehicle.powertrain;
            TransmissionComponent currentTransmission = current == null ? null : current.transmission;
            return !System.Object.ReferenceEquals(current, attemptedPowertrain) ||
                !System.Object.ReferenceEquals(currentTransmission, attemptedTransmission) ||
                !System.Object.ReferenceEquals(currentTransmission == null ? null : currentTransmission.Output, attemptedRoot) ||
                (current == null || current.wheels == null ? -1 : current.wheels.Count) != attemptedWheelCount;
        }

        private bool CaptureCore()
        {
            twoWheel = null; fourWheel = null;
            if (Vehicle == null || Vehicle.powertrain == null || !HasSupportedWheels(Vehicle)) return false;
            transmission = Vehicle.powertrain.transmission;
            nativeRoot = transmission == null ? null : transmission.Output as DifferentialComponent;
            if (nativeRoot == null || transmission.outputNameHash == 0) return false;
            transmissionOutputHash = transmission.outputNameHash;
            wheels = Vehicle.powertrain.wheels.ToArray();
            sensors = new WheelUAPI[wheels.Length];
            for (int i = 0; i < wheels.Length; i++) sensors[i] = wheels[i].wheelUAPI;
            for (int i = 0; i < sensors.Length; i++)
                if (sensors[i] == null) { sensors = null; break; }
            links.Clear();
            Node factory = NativeNode(nativeRoot, new HashSet<PowertrainComponent>(), 0);
            if (factory == null) return false;
            HashSet<WheelComponent> factoryDriven = new HashSet<WheelComponent>();
            factory.Driven(factoryDriven);
            factoryFourWheel = factoryDriven.Count == 4;
            WheelComponent fl = FindWheel("hinge_wheel_FL"), fr = FindWheel("hinge_wheel_FR");
            if (fl == null) { fl = FindWheel("hinge_wheel_F"); fr = FindWheel("hinge_wheel_M"); }
            WheelComponent rl = FindWheel("hinge_wheel_RL"), rr = FindWheel("hinge_wheel_RR");
            if (factoryFourWheel)
            {
                DifferentialComponent rear = nativeRoot.OutputB as DifferentialComponent;
                if (rear == null) return false;
                Node rearNode = NativeNode(rear, new HashSet<PowertrainComponent>(), 0);
                if (rearNode == null) return false;
                HashSet<WheelComponent> rearWheels = new HashSet<WheelComponent>(); rearNode.Driven(rearWheels);
                if (rearWheels.Count != 2 || !rearWheels.Contains(rl) || !rearWheels.Contains(rr)) return false;
                twoWheel = Axle(nativeRoot, rearNode, null);
                fourWheel = factory;
            }
            else
            {
                twoWheel = factory;
                Node front = Axle(NewDifferential(nativeRoot, false), Leaf(fl), Leaf(fr));
                Node rear = Axle(NewDifferential(nativeRoot, false), Leaf(rl), Leaf(rr));
                fourWheel = Axle(NewDifferential(nativeRoot, true), front, rear);
            }
            return GraphMatches();
        }

        private WheelComponent FindWheel(string name)
        { foreach (WheelComponent wheel in wheels) if (wheel.name == name) return wheel; return null; }

        private bool GraphMatches()
        {
            if (Vehicle == null || Vehicle.powertrain == null || transmission == null || nativeRoot == null ||
                twoWheel == null || fourWheel == null || wheels == null ||
                !System.Object.ReferenceEquals(Vehicle.powertrain.transmission, transmission) ||
                !System.Object.ReferenceEquals(transmission.Output, nativeRoot) ||
                transmission.outputNameHash != transmissionOutputHash ||
                Vehicle.powertrain.wheels == null || Vehicle.powertrain.wheels.Count != wheels.Length) return false;
            for (int i = 0; i < wheels.Length; i++)
                if (!System.Object.ReferenceEquals(Vehicle.powertrain.wheels[i], wheels[i])) return false;
            foreach (Link link in links)
                if (!System.Object.ReferenceEquals(link.Component.Output, link.A) ||
                    !System.Object.ReferenceEquals(link.Component.OutputB, link.B) ||
                    link.Component.outputNameHash != link.AHash || link.Component.outputBNameHash != link.BHash ||
                    link.Component.splitTorqueDelegate == null) return false;
            return true;
        }

        private bool Ready()
        {
            if (failed || !GraphMatches()) return false;
            for (int i = 0; i < wheels.Length; i++)
                if (sensors == null || sensors[i] == null ||
                    !System.Object.ReferenceEquals(wheels[i].wheelUAPI, sensors[i])) return false;
            return true;
        }

        private bool Active()
        {
            return Enabled && !suspended && Plugin.Enabled != null && Plugin.Enabled.Value && Ready() &&
                Vehicle.powertrain.IsActive &&
                !(factoryFourWheel && FourWheelSelected);
        }

        private Node SelectedNode { get { return FourWheelSelected ? fourWheel : twoWheel; } }

        internal bool ValidateOption(bool enabled, out string error)
        {
            error = null;
            if (!enabled)
            {
                if (!Enabled) return true;
                return CanChange(out error);
            }
            if (!GraphMatches() && !Enabled) { failed = false; Capture(); }
            if (!Ready()) { error = "The vehicle's drive system is not ready."; return false; }
            if (enabled == Enabled) return true;
            return CanChange(out error);
        }

        private bool CanChange(out string error)
        {
            if (RangeControl.CanChange(Vehicle, out error)) return true;
            if (error != null) error = error.Replace("range", "drive mode").Replace("Range", "Drive mode");
            return false;
        }

        internal bool CommitEnabled(bool enabled, out string error)
        {
            if (!ValidateOption(enabled, out error)) return false;
            if (Enabled == enabled) return true;
            RestoreWheels();
            Enabled = enabled; suspended = false;
            if (!enabled)
            {
                failed = false; FourWheelSelected = true;
                UnregisterGraph();
                nativeRoot = null; twoWheel = null; fourWheel = null; links.Clear();
            }
            PrepareWheels();
            return true;
        }

        internal bool TrySetFourWheel(bool selected, out string error)
        {
            error = null;
            if (!Enabled) { error = "Enable the 2WD / 4WD selector in the gearbox menu first."; return false; }
            if (!Ready()) { error = "The vehicle's drive system is not ready."; return false; }
            if (FourWheelSelected == selected) return true;
            if (!CanChange(out error)) return false;
            RestoreWheels();
            FourWheelSelected = selected; suspended = false;
            PrepareWheels();
            return true;
        }

        internal string Export()
        { return "v1|" + (Enabled ? "1" : "0") + "|" + (FourWheelSelected ? "1" : "0"); }

        internal void Restore(string saved)
        {
            RestoreWheels(); Enabled = false; FourWheelSelected = true; suspended = false; failed = false;
            UnregisterGraph(); captureDelay = 0.2f; nextCapture = 0f;
            twoWheel = null; fourWheel = null; nativeRoot = null; links.Clear();
            string[] fields = (saved ?? String.Empty).Split('|');
            if (fields.Length == 3 && fields[0] == "v1" &&
                (fields[1] == "0" || fields[1] == "1") && (fields[2] == "0" || fields[2] == "1"))
            { Enabled = fields[1] == "1"; FourWheelSelected = fields[2] == "1"; }
            if (Enabled && Plugin.Enabled != null && Plugin.Enabled.Value) Capture();
            PrepareWheels();
        }

        internal static void TickAll()
        {
            for (int i = Controls.Count - 1; i >= 0; i--)
            {
                DriveControl state = Controls[i];
                if (state == null || state.Vehicle == null)
                { if (!System.Object.ReferenceEquals(state, null)) { state.UnregisterGraph(); state.RestoreWheels(); } Controls.RemoveAt(i); continue; }
                if (Plugin.Enabled == null || !Plugin.Enabled.Value) { state.RestoreWheels(); continue; }
                if (state.Enabled && !state.failed && (state.twoWheel == null || state.fourWheel == null || state.sensors == null) &&
                    (Time.unscaledTime >= state.nextCapture || state.AssemblyChanged())) state.Capture();
                if (state.Enabled && !state.failed && state.twoWheel != null && state.fourWheel != null && (!state.GraphMatches() ||
                    (state.sensors != null && !state.Ready())))
                {
                    state.failed = true;
                    state.UnregisterGraph();
                    Warn(new InvalidOperationException("The vehicle's native drive layout changed; disable and re-enable its selector to capture it again."));
                }
                if (Plugin.Enabled != null && Plugin.Enabled.Value) state.suspended = false;
                if (!state.Active()) state.RestoreWheels();
                else state.PrepareWheels();
            }
        }

        internal static void SuspendAll()
        { foreach (DriveControl state in Controls) if (state != null) { state.suspended = true; state.RestoreWheels(); } }

        private void OnDestroy() { UnregisterGraph(); RestoreWheels(); FlushAutoSimulation(true); Controls.Remove(this); }

        // VehicleController runs at 90 and WheelController at 100. A failure
        // must keep completed wheels manual until the following physics tick.
        private void FixedUpdate() { FlushAutoSimulation(false); }

        private void FlushAutoSimulation(bool force)
        {
            for (int i = pendingAutoSimulation.Count - 1; i >= 0; i--)
            {
                WheelOwnership record = pendingAutoSimulation[i];
                if (!force && record.StepTime == Time.fixedTime) continue;
                try { if (record.Sensor != null && !record.Sensor.AutoSimulate) record.Sensor.AutoSimulate = record.AutoSimulate; }
                catch (Exception error) { Warn(error); }
                pendingAutoSimulation.RemoveAt(i);
            }
        }

        private WheelOwnership ClaimWheel(WheelUAPI sensor)
        {
            for (int i = pendingAutoSimulation.Count - 1; i >= 0; i--)
                if (System.Object.ReferenceEquals(pendingAutoSimulation[i].Sensor, sensor))
                { WheelOwnership pending = pendingAutoSimulation[i]; pendingAutoSimulation.RemoveAt(i); wheelOwnership.Add(pending); return pending; }
            WheelOwnership ownership = new WheelOwnership { Sensor = sensor, AutoSimulate = sensor.AutoSimulate, Inertia = sensor.Inertia };
            wheelOwnership.Add(ownership); return ownership;
        }

        private float StepWheel(WheelComponent wheel, float torque, float inertia, float dt)
        {
            WheelUAPI sensor = wheel.wheelUAPI;
            WheelOwnership ownership = null;
            foreach (WheelOwnership record in wheelOwnership)
                if (System.Object.ReferenceEquals(record.Sensor, sensor)) { ownership = record; break; }
            if (ownership == null)
            {
                ownership = ClaimWheel(sensor);
            }
            float remainder;
            try { remainder = wheel.ForwardStep(torque, inertia, dt); }
            finally
            {
                // Native Step can fail after applying its motor outputs. Keep
                // ownership of those partial writes for the failure cleanup.
                ownership.Stepped = true;
                ownership.StepTime = Time.fixedTime;
                ownership.LastInertia = sensor.Inertia; ownership.LastTorque = sensor.MotorTorque;
            }
            if (!TuneData.Finite(remainder)) throw new InvalidOperationException("Wheel produced invalid countertorque.");
            return remainder;
        }

        private void PrepareWheels()
        {
            if (!Active()) return;
            foreach (WheelComponent wheel in wheels)
            {
                WheelUAPI sensor = wheel.wheelUAPI;
                bool found = false;
                foreach (WheelOwnership record in wheelOwnership)
                    if (System.Object.ReferenceEquals(record.Sensor, sensor)) { found = true; break; }
                if (found) continue;
                ClaimWheel(sensor);
                // Claim formerly passive wheels before the next physics tick,
                // so their auto step cannot run in addition to our native step.
                sensor.AutoSimulate = false;
            }
        }

        private void RestoreWheels()
        {
            foreach (WheelOwnership record in wheelOwnership)
            {
                try
                {
                    if (record.Sensor == null) continue;
                    // Only revert still-owned transient physics outputs. A
                    // foreign write or replaced sensor is left untouched.
                    if (record.Stepped && record.Sensor.MotorTorque == record.LastTorque) record.Sensor.MotorTorque = 0f;
                    if (record.Stepped && record.Sensor.Inertia == record.LastInertia) record.Sensor.Inertia = record.Inertia;
                    if (!record.Sensor.AutoSimulate)
                    {
                        if (record.AutoSimulate && record.Stepped && record.StepTime == Time.fixedTime) pendingAutoSimulation.Add(record);
                        else record.Sensor.AutoSimulate = record.AutoSimulate;
                    }
                }
                catch (Exception error) { Warn(error); }
            }
            wheelOwnership.Clear();
        }

        private float Forward(float torque, float inertia, float dt)
        {
            bool[] driven = FourWheelSelected ? fourWheelDriven : twoWheelDriven;
            // Free wheels get no engine torque or reflected engine inertia,
            // but still run their actual suspension/tire physics once.
            for (int i = 0; i < wheels.Length; i++)
                if (!driven[i]) StepWheel(wheels[i], 0f, 0f, dt);
            nativeRoot.inputTorque = torque; nativeRoot.inputInertia = inertia;
            nativeRoot.outputTorque = torque; nativeRoot.outputInertia = inertia + SelectedNode.Inertia();
            return SelectedNode.Forward(torque, inertia, nativeRoot.outputAngularVelocity, dt, this);
        }

        private static DriveControl ForRoot(DifferentialComponent root)
        {
            DriveControl state;
            if (root == null || !roots.TryGetValue(root, out state)) return null;
            if (state == null)
            { if (!System.Object.ReferenceEquals(state, null)) state.UnregisterGraph(); else roots.Remove(root); return null; }
            if (!state.Enabled || state.suspended || Plugin.Enabled == null || !Plugin.Enabled.Value) return null;
            if (!state.Ready())
            {
                if (state.Vehicle == null || !state.GraphMatches() || state.sensors != null)
                { state.failed = true; state.UnregisterGraph(); state.RestoreWheels(); }
                return null;
            }
            return state.Vehicle.powertrain.IsActive && !(state.factoryFourWheel && state.FourWheelSelected) ? state : null;
        }

        internal static bool TryWheelShaftAngularVelocity(TransmissionComponent target, out float angular)
        {
            angular = 0f;
            DriveControl state;
            if (target != null && transmissions.TryGetValue(target, out state) && state != null && state.Active())
                return state.SelectedNode.Shaft(out angular);
            return false;
        }

        private static void Warn(Exception error)
        {
            if (Plugin.Log == null || Time.unscaledTime < nextWarning) return;
            nextWarning = Time.unscaledTime + 5f;
            Plugin.Log.LogWarning("Drive selector could not use an available wheel: " + error.GetType().Name + ": " + error.Message);
        }

        private static void QueryFailed(DriveControl state, Exception error)
        {
            if (state != null)
            { state.failed = true; state.suspended = true; state.UnregisterGraph(); state.RestoreWheels(); }
            Warn(error);
        }

        // Powertrain inherits this manager method. It enables all child wheels
        // before returning, so reclaim their simulation before the first step
        // after a distance wake rather than waiting for the periodic UI tick.
        [HarmonyPatch(typeof(ManagerVehicleComponent), "VC_Enable")]
        private static class WakePatch
        {
            private static void Postfix(ManagerVehicleComponent __instance, bool __result)
            {
                Powertrain powertrain = __instance as Powertrain;
                if (!__result || powertrain == null) return;
                foreach (DriveControl state in Controls)
                {
                    if (state == null || state.Vehicle == null ||
                        !System.Object.ReferenceEquals(state.Vehicle.powertrain, powertrain)) continue;
                    try
                    {
                        state.RestoreWheels();
                        if (state.Enabled && Plugin.Enabled != null && Plugin.Enabled.Value && !state.failed &&
                            (state.twoWheel == null || state.fourWheel == null || state.sensors == null) &&
                            (Time.unscaledTime >= state.nextCapture || state.AssemblyChanged()))
                            state.Capture();
                        state.PrepareWheels();
                    }
                    catch (Exception error) { QueryFailed(state, error); }
                }
            }
        }

        [HarmonyPatch(typeof(DifferentialComponent), "QueryAngularVelocity")]
        private static class AngularPatch
        {
            private static bool Prefix(DifferentialComponent __instance, float __0, float __1, ref float __result)
            {
                DriveControl state = ForRoot(__instance);
                if (state == null) return true;
                try
                {
                    __instance.inputAngularVelocity = __0;
                    __instance.outputAngularVelocity = __0;
                    __result = state.SelectedNode.Angular(__0, __1);
                    if (!TuneData.Finite(__result)) throw new InvalidOperationException("Invalid wheel angular velocity.");
                    return false;
                }
                catch (Exception error) { QueryFailed(state, error); return true; }
            }
        }

        [HarmonyPatch(typeof(DifferentialComponent), "QueryInertia")]
        private static class InertiaPatch
        {
            private static bool Prefix(DifferentialComponent __instance, ref float __result)
            {
                DriveControl state = ForRoot(__instance);
                if (state == null) return true;
                try
                {
                    __result = state.SelectedNode.Inertia();
                    if (!TuneData.Finite(__result) || __result <= 0f) throw new InvalidOperationException("Invalid wheel inertia.");
                    return false;
                }
                catch (Exception error) { QueryFailed(state, error); return true; }
            }
        }

        [HarmonyPatch(typeof(DifferentialComponent), "ForwardStep")]
        private static class ForwardPatch
        {
            private static bool Prefix(DifferentialComponent __instance, float __0, float __1, float __2, ref float __result)
            {
                DriveControl state = ForRoot(__instance);
                if (state == null) return true;
                try { __result = state.Forward(__0, __1, __2); }
                catch (Exception error)
                {
                    // A wheel may already have stepped. Do not double-step it
                    // by falling through to the native graph in this frame.
                    state.suspended = true; state.failed = true; state.UnregisterGraph(); state.RestoreWheels(); Warn(error); __result = 0f;
                }
                return false;
            }
        }
    }
}
