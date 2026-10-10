using System;
using System.Collections.Generic;
using System.Globalization;
using NWH.Common.Vehicles;
using NWH.VehiclePhysics2;
using NWH.VehiclePhysics2.Modules.Aerodynamics;
using NWH.VehiclePhysics2.Powertrain;
using UnityEngine;

namespace ApocaGearbox
{
    internal sealed class CalculatorSnapshot
    {
        internal CalculatorInputs Inputs;
        internal string EngineDescription, VehicleDescription, Notes;
        internal EngineComponent Engine;
        internal VehicleController Vehicle;
        internal float BoostGain, BasePower;
        internal float BaseRedline;
        internal string BoostKind;
        internal GameObject BoostPart;
        internal AnimationCurve PowerCurve;
        internal Keyframe[] CurveKeys;
        internal WrapMode CurvePreWrap, CurvePostWrap;
        internal Dictionary<WheelComponent, float> Shares;
        internal readonly Dictionary<WheelComponent, float> CurrentShares = new Dictionary<WheelComponent, float>();
        internal readonly HashSet<PowertrainComponent> CurrentPath = new HashSet<PowertrainComponent>();
        internal bool IsProjection;
        internal WheelUAPI[] Wheels;
        internal float[] Radii, Grips, FrictionPeaks, LoadRatings, Loads;
        internal bool[] EstimatedLoads;
        internal WheelComponent[] RollingWheels;
        internal WheelUAPI[] RollingSensors;
        internal float[] RollingRadii, RollingTorques;
    }

    internal static class GearCalculator
    {
        private static bool Positive(float value) { return GearCalculatorMath.Positive(value); }

        internal static bool CanLink(GameObject item, VehicleController vehicle)
        {
            if (item == null || vehicle == null || !vehicle.gameObject.activeInHierarchy ||
                vehicle.powertrain == null || vehicle.powertrain.engine == null || vehicle.powertrain.transmission == null) return false;
            float distance = Vector3.Distance(item.transform.position, vehicle.transform.position);
            if (!TuneData.Finite(distance) || distance > 8f) return false;
            // The stock trailers and push cart also have VehicleController and
            // isPlayerControllable=true. The native engine mount distinguishes
            // every engine-capable chassis without a model-name allowlist.
            foreach (Transform part in vehicle.GetComponentsInChildren<Transform>(true))
                if (part.name == "hinge_engine" && part.GetComponentInParent<VehicleController>() == vehicle) return true;
            return false;
        }

        internal static bool Capture(GameObject item, out CalculatorSnapshot snapshot, out string error)
        { return Capture(item, null, out snapshot, out error); }

        internal static bool Capture(GameObject item, VehicleController previewVehicle, out CalculatorSnapshot snapshot, out string error)
        { return Capture(item, previewVehicle, null, out snapshot, out error); }

        internal static bool Capture(GameObject item, VehicleController previewVehicle, float? requestedRevLimit, out CalculatorSnapshot snapshot, out string error)
        {
            snapshot = null; error = null;
            VehicleController vehicle = GearboxTarget.InstalledVehicle(item);
            bool projected = vehicle == null;
            if (projected) vehicle = previewVehicle;
            if (vehicle == null || vehicle.powertrain == null || vehicle.powertrain.engine == null || vehicle.vehicleRigidbody == null)
            { error = "Install this engine or choose a nearby vehicle for the calculator. Manual tuning still works on loose engines."; return false; }
            if (projected && !CanLink(item, vehicle))
            { error = "Choose an active vehicle with an engine mount within 8 metres of this engine."; return false; }
            if (!TuneData.Finite(vehicle.Speed) || vehicle.Speed > 0.5f)
            { error = "Stop the vehicle before calculating its gearing."; return false; }
            EngineComponent engine = vehicle.powertrain.engine;
            float power = engine.maxPower, idle = engine.idleRPM, redline = engine.revLimiterRPM;
            if (projected && !CalculatorEngineSpecs.ReadLoose(item, out power, out idle, out redline))
            { error = "This loose engine's native power and RPM settings could not be read. Install it before calculating."; return false; }
            float baseRedline = redline;
            if (requestedRevLimit.HasValue)
            {
                float requested = requestedRevLimit.Value;
                if (!RevLimitControl.Validate(requested, idle, out error)) return false;
                if (requested > 0f) redline = requested;
                else {
                    float nativeIdle, factoryLimit;
                    if (CalculatorEngineSpecs.ReadRpm(item, out nativeIdle, out factoryLimit)) redline = factoryLimit;
                }
            }
            TransmissionComponent transmission = vehicle.powertrain.transmission;
            if (transmission == null || transmission.transmissionType == TransmissionComponent.TransmissionShiftType.CVT ||
                engine.engineType != EngineComponent.EngineType.ICE || engine.powerCurve == null ||
                !Positive(power) || !Positive(idle) || !Positive(redline) ||
                redline <= idle * 1.4f || !Positive(vehicle.vehicleRigidbody.mass))
            { error = "The engine's power curve or gearbox is not supported by this calculator."; return false; }
            CalculatorBoost boost;
            if (!CalculatorBoostSpecs.TryRead(item, engine, projected, out boost, out error)) return false;
            float gain = boost.Gain;
            CalculatorInputs input = new CalculatorInputs { Mass = vehicle.vehicleRigidbody.mass,
                IdleRpm = idle, RedlineRpm = redline };
            // Sample the live engine curve, including condition-adjusted maxPower.
            // Configured full boost is applied exactly once, not the current idle spool.
            for (int i = 1; i <= 190; i++)
            {
                float rpm = redline * i / 200f;
                if (rpm < idle * 1.2f) continue;
                float sampledPower = engine.powerCurve.Evaluate(rpm / redline) * power * gain;
                if (!TuneData.Finite(sampledPower) || sampledPower < 0f) { error = "The engine power curve contains invalid values."; return false; }
                float torque = sampledPower * 9549.2966f / rpm;
                if (sampledPower > input.PeakPowerKw) { input.PeakPowerKw = sampledPower; input.PowerRpm = rpm; }
                if (torque > input.PeakTorqueNm) input.PeakTorqueNm = torque;
            }
            if (!Positive(input.PeakPowerKw) || !Positive(input.PeakTorqueNm))
            { error = "This engine currently has no usable power. Check its condition before calculating."; return false; }
            input.BandLowRpm = input.PowerRpm;
            for (int i = 1; i <= 190; i++)
            {
                float rpm = redline * i / 200f;
                if (rpm < idle * 1.25f || rpm > input.PowerRpm) continue;
                float sampledPower = engine.powerCurve.Evaluate(rpm / redline) * power * gain;
                float torque = sampledPower * 9549.2966f / rpm;
                if (sampledPower >= input.PeakPowerKw * 0.55f && torque >= input.PeakTorqueNm * 0.75f)
                { input.BandLowRpm = rpm; break; }
            }
            Dictionary<WheelComponent, float> shares = new Dictionary<WheelComponent, float>();
            if (!DriveControl.TryCalculatorShares(vehicle, shares) &&
                !Walk(transmission.Output, 1f, shares, new HashSet<PowertrainComponent>(), 0))
            { error = "The driven-wheel layout is not ready or is unsupported."; return false; }
            if (shares.Count == 0 || vehicle.powertrain.wheels == null || vehicle.powertrain.wheels.Count == 0)
            { error = "No driven wheels are available for this calculation."; return false; }
            List<WheelUAPI> sensors = new List<WheelUAPI>();
            List<float> radii = new List<float>(), grips = new List<float>(), frictionPeaks = new List<float>(), loadRatings = new List<float>();
            List<float> loads = new List<float>();
            List<bool> estimatedLoads = new List<bool>();
            float minRadius = Single.MaxValue, maxRadius = 0f, capacity = Single.MaxValue;
            bool estimatedLoad = false;
            foreach (KeyValuePair<WheelComponent, float> wheel in shares)
            {
                if (wheel.Value <= 0.00001f) continue;
                WheelUAPI sensor = wheel.Key != null ? wheel.Key.wheelUAPI : null;
                if (sensor == null || !sensor.IsGrounded || !Positive(sensor.Radius) || !Positive(sensor.MaxLoad) ||
                    !Positive(sensor.LongitudinalFrictionGrip) || sensor.FrictionPreset == null || !Positive(sensor.FrictionPreset.BCDE.z))
                { error = "Put the vehicle on the ground with usable tires on every driven wheel before calculating."; return false; }
                float load = sensor.Load;
                bool estimate = !Positive(load);
                if (estimate) { load = input.Mass * 9.81f / vehicle.powertrain.wheels.Count; estimatedLoad = true; }
                loads.Add(Math.Min(load, sensor.MaxLoad)); estimatedLoads.Add(estimate);
                float force = 1.35f * Math.Min(load, sensor.MaxLoad) * sensor.FrictionPreset.BCDE.z * sensor.LongitudinalFrictionGrip;
                capacity = Math.Min(capacity, force * sensor.Radius / wheel.Value);
                minRadius = Math.Min(minRadius, sensor.Radius); maxRadius = Math.Max(maxRadius, sensor.Radius);
                sensors.Add(sensor); radii.Add(sensor.Radius); grips.Add(sensor.LongitudinalFrictionGrip);
                frictionPeaks.Add(sensor.FrictionPreset.BCDE.z); loadRatings.Add(sensor.MaxLoad);
            }
            if (sensors.Count == 0 || maxRadius > minRadius * 1.02f)
            { error = "Fit matching tire sizes on the driven wheels before calculating. Mixed sizes cannot use one reliable speed-to-RPM ratio."; return false; }
            input.Radius = (minRadius + maxRadius) * 0.5f; input.DrivenTorqueCapacity = capacity;
            WheelComponent[] rollingWheels = vehicle.powertrain.wheels.ToArray();
            WheelUAPI[] rollingSensors = new WheelUAPI[rollingWheels.Length];
            float[] rollingRadii = new float[rollingWheels.Length], rollingTorques = new float[rollingWheels.Length];
            for (int i = 0; i < rollingWheels.Length; i++)
            {
                WheelComponent wheel = rollingWheels[i];
                rollingSensors[i] = wheel != null ? wheel.wheelUAPI : null;
                if (wheel != null && wheel.wheelUAPI != null && Positive(wheel.wheelUAPI.Radius))
                {
                    rollingRadii[i] = wheel.wheelUAPI.Radius;
                    rollingTorques[i] = RollingTorque(wheel.wheelUAPI);
                    input.RollingForce += rollingTorques[i] / rollingRadii[i];
                }
            }
            ReadDrag(vehicle, out input.DragKnown, out input.DragCoefficient);
            snapshot = new CalculatorSnapshot { Inputs = input, Engine = engine, Vehicle = vehicle,
                BoostGain = gain, BasePower = power, IsProjection = projected, Wheels = sensors.ToArray(), Radii = radii.ToArray(), Grips = grips.ToArray(),
                BoostKind = boost.Kind, BoostPart = boost.Part,
                PowerCurve = engine.powerCurve, Shares = shares, FrictionPeaks = frictionPeaks.ToArray(), LoadRatings = loadRatings.ToArray(),
                CurveKeys = engine.powerCurve.keys, CurvePreWrap = engine.powerCurve.preWrapMode, CurvePostWrap = engine.powerCurve.postWrapMode,
                Loads = loads.ToArray(), EstimatedLoads = estimatedLoads.ToArray(), RollingWheels = rollingWheels,
                RollingSensors = rollingSensors, RollingRadii = rollingRadii, RollingTorques = rollingTorques,
                EngineDescription = "Detected engine: " + item.name.Replace("(Clone)", "").Trim() + "\nDetected boost: " + boost.Description + "\nCurrent estimate: " +
                    (input.PeakPowerKw * 1.341022f).ToString("0", CultureInfo.InvariantCulture) + " HP / " +
                    input.PeakTorqueNm.ToString("0", CultureInfo.InvariantCulture) + " Nm",
                VehicleDescription = (projected ? "Preview vehicle: " + vehicle.name + " / " : "") + input.Mass.ToString("0", CultureInfo.InvariantCulture) + " kg / tire radius " +
                    input.Radius.ToString("0.###", CultureInfo.InvariantCulture) + " m / " + sensors.Count + " driven wheels",
                BaseRedline = baseRedline,
                Notes = "Calculated rev limit: " + redline.ToString("0", CultureInfo.InvariantCulture) + " RPM. " + (projected ? "Uses this engine and its own fitted boost with the preview vehicle's power curve, current weight and driven tires. Recalculate after installation if weight, boost or drive mode changes. " : "") +
                    (estimatedLoad ? "Resting wheel loads were estimated because the vehicle was asleep. " : "") +
                    "Launch grip uses the currently active tire/surface settings. Differential load transfer and turbo spool vary while driving." };
            return true;
        }

        internal static bool StillMatches(GameObject item, CalculatorSnapshot snapshot)
        {
            if (item == null || snapshot == null || snapshot.Vehicle == null || !snapshot.Vehicle.gameObject.activeInHierarchy ||
                snapshot.Vehicle.powertrain == null || snapshot.Vehicle.powertrain.engine != snapshot.Engine ||
                snapshot.Vehicle.vehicleRigidbody == null || snapshot.Vehicle.vehicleRigidbody.mass != snapshot.Inputs.Mass ||
                !TuneData.Finite(snapshot.Vehicle.Speed) || snapshot.Vehicle.Speed > 0.5f || snapshot.Engine.powerCurve != snapshot.PowerCurve ||
                snapshot.Vehicle.powertrain.transmission == null || snapshot.Vehicle.powertrain.transmission.transmissionType == TransmissionComponent.TransmissionShiftType.CVT ||
                snapshot.Engine.engineType != EngineComponent.EngineType.ICE || !CurveMatches(snapshot)) return false;
            if (snapshot.IsProjection)
            {
                float power, idle, redline;
                if (GearboxTarget.InstalledVehicle(item) != null || !CanLink(item, snapshot.Vehicle) ||
                    !CalculatorEngineSpecs.ReadLoose(item, out power, out idle, out redline) || power != snapshot.BasePower ||
                    idle != snapshot.Inputs.IdleRpm || redline != snapshot.BaseRedline) return false;
            }
            else if (GearboxTarget.InstalledVehicle(item) != snapshot.Vehicle || snapshot.Engine.maxPower != snapshot.BasePower ||
                snapshot.Engine.idleRPM != snapshot.Inputs.IdleRpm || snapshot.Engine.revLimiterRPM != snapshot.BaseRedline) return false;
            CalculatorBoost boost; string error;
            if (!CalculatorBoostSpecs.TryRead(item, snapshot.Engine, snapshot.IsProjection, out boost, out error) ||
                boost.Gain != snapshot.BoostGain || boost.Kind != snapshot.BoostKind || boost.Part != snapshot.BoostPart) return false;
            Dictionary<WheelComponent, float> current = snapshot.CurrentShares;
            current.Clear(); snapshot.CurrentPath.Clear();
            if (!DriveControl.TryCalculatorShares(snapshot.Vehicle, current) &&
                !Walk(snapshot.Vehicle.powertrain.transmission.Output, 1f, current, snapshot.CurrentPath, 0)) return false;
            if (current.Count != snapshot.Shares.Count) return false;
            foreach (KeyValuePair<WheelComponent, float> pair in current)
            {
                float previous;
                if (!snapshot.Shares.TryGetValue(pair.Key, out previous) || pair.Value != previous ||
                    pair.Key == null || Array.IndexOf(snapshot.Wheels, pair.Key.wheelUAPI) < 0 && pair.Value > 0.00001f) return false;
            }
            if (snapshot.Vehicle.powertrain.wheels == null || snapshot.Vehicle.powertrain.wheels.Count != snapshot.RollingWheels.Length) return false;
            for (int i = 0; i < snapshot.Wheels.Length; i++)
            {
                if (snapshot.Wheels[i] == null || !snapshot.Wheels[i].IsGrounded || snapshot.Wheels[i].Radius != snapshot.Radii[i] ||
                    snapshot.Wheels[i].LongitudinalFrictionGrip != snapshot.Grips[i] || snapshot.Wheels[i].MaxLoad != snapshot.LoadRatings[i] ||
                    snapshot.Wheels[i].FrictionPreset == null || snapshot.Wheels[i].FrictionPreset.BCDE.z != snapshot.FrictionPeaks[i]) return false;
                float rawLoad = snapshot.Wheels[i].Load;
                bool estimate = !Positive(rawLoad);
                float load = Math.Min(estimate ? snapshot.Inputs.Mass * 9.81f / snapshot.RollingWheels.Length : rawLoad, snapshot.LoadRatings[i]);
                // Resting suspension can settle slightly. Compare the capped load
                // actually used by the grip calculation, allowing 5% / 50 N jitter.
                if (Math.Abs(load - snapshot.Loads[i]) > Math.Max(50f, snapshot.Loads[i] * 0.05f)) return false;
            }
            for (int i = 0; i < snapshot.RollingWheels.Length; i++)
            {
                WheelComponent wheel = snapshot.Vehicle.powertrain.wheels[i];
                WheelUAPI sensor = wheel != null ? wheel.wheelUAPI : null;
                if (wheel != snapshot.RollingWheels[i] || sensor != snapshot.RollingSensors[i]) return false;
                float radius = sensor != null && Positive(sensor.Radius) ? sensor.Radius : 0f;
                if (radius != snapshot.RollingRadii[i]) return false;
                // Resistance is a live contact reading, not a fixed tire setting.
                // Allow the same small settling tolerance as resting wheel loads.
                float torque = radius > 0f ? RollingTorque(sensor) : 0f;
                if (Math.Abs(torque - snapshot.RollingTorques[i]) > Math.Max(0.5f, snapshot.RollingTorques[i] * 0.05f)) return false;
            }
            bool dragKnown; float drag;
            ReadDrag(snapshot.Vehicle, out dragKnown, out drag);
            if (dragKnown != snapshot.Inputs.DragKnown || drag != snapshot.Inputs.DragCoefficient) return false;
            return true;
        }

        private static float RollingTorque(WheelUAPI sensor)
        { float value = sensor.RollingResistanceTorque; return Positive(value) ? value : 0f; }

        private static void ReadDrag(VehicleController vehicle, out bool known, out float coefficient)
        {
            AerodynamicsModuleWrapper wrapper = vehicle.GetComponent<AerodynamicsModuleWrapper>();
            AerodynamicsModule aero = wrapper != null ? wrapper.module : null;
            known = aero != null && aero.IsActive;
            coefficient = known && aero.simulateDrag ? 0.6125f * aero.dimensions.x * aero.dimensions.y * 0.85f * aero.frontalCd : 0f;
        }

        private static bool CurveMatches(CalculatorSnapshot snapshot)
        {
            AnimationCurve curve = snapshot.PowerCurve;
            if (curve == null || curve.length != snapshot.CurveKeys.Length || curve.preWrapMode != snapshot.CurvePreWrap || curve.postWrapMode != snapshot.CurvePostWrap) return false;
            for (int i = 0; i < snapshot.CurveKeys.Length; i++)
            {
                Keyframe a = curve[i], b = snapshot.CurveKeys[i];
                if (a.time != b.time || a.value != b.value || a.inTangent != b.inTangent || a.outTangent != b.outTangent ||
                    a.inWeight != b.inWeight || a.outWeight != b.outWeight || a.weightedMode != b.weightedMode) return false;
            }
            return true;
        }

        private static bool Walk(PowertrainComponent component, float share, Dictionary<WheelComponent, float> wheels,
            HashSet<PowertrainComponent> path, int depth)
        {
            if (component == null || depth > 8 || !path.Add(component) || !TuneData.Finite(share)) return false;
            try
            {
                WheelComponent wheel = component as WheelComponent;
                if (wheel != null) { float old; wheels.TryGetValue(wheel, out old); wheels[wheel] = old + share; return true; }
                DifferentialComponent differential = component as DifferentialComponent;
                if (differential == null || !TuneData.Finite(differential.biasAB)) return false;
                if (differential.Output == differential.OutputB) return Walk(differential.Output, share, wheels, path, depth + 1);
                float bias = GearCalculatorMath.Clamp(differential.biasAB, 0f, 1f);
                return Walk(differential.Output, share * bias, wheels, path, depth + 1) &&
                    Walk(differential.OutputB, share * (1f - bias), wheels, path, depth + 1);
            }
            finally { path.Remove(component); }
        }
    }
}
