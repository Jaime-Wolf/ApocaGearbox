using System;
using System.Collections.Generic;
using System.Globalization;
using NWH.VehiclePhysics2;
using NWH.VehiclePhysics2.Powertrain;
using UnityEngine;

namespace ApocaGearbox
{
    // Owns only its clutch-capacity writes and its one native power modifier.
    // Engagement, wheel physics, engine RPM and the player's pedals stay native.
    internal sealed class DrivingAssist
    {
        private readonly VehicleController vehicle;
        private readonly EngineComponent.PowerModifier speedModifier;
        private EngineComponent engine;
        private ClutchComponent clutch;
        private List<EngineComponent.PowerModifier> modifierList;
        private float nativeCapacity;
        private float lastApplied;
        private bool wroteCapacity;
        private float speedLimitKph;
        private float nextDiagnostic;
        private object torqueCurve;
        private float torquePower, torqueIdle, torqueLimit, torqueGain, unboostedPeak;
        private float nextTorqueRefresh;
        private bool torqueCached;

        internal DrivingAssist(VehicleController owner)
        {
            vehicle = owner;
            speedModifier = SpeedPowerMultiplier;
            RefreshComponents();
        }

        internal void Update(TuneData tune)
        {
            if (vehicle == null || !vehicle.gameObject.activeInHierarchy || tune == null)
            { Restore(); return; }
            RefreshComponents();
            if (engine == null) return;

            speedLimitKph = TuneData.Finite(tune.SpeedLimitKph) && tune.SpeedLimitKph >= 10f && tune.SpeedLimitKph <= 500f
                ? tune.SpeedLimitKph : 0f;
            UpdateModifier();

            float configuredGain = 1f, actualGain = 1f;
            EngineComponent.ForcedInduction induction = engine.forcedInduction;
            if (induction != null && induction.useForcedInduction)
            {
                configuredGain = induction.powerGainMultiplier;
                actualGain = induction.PowerGainMultiplier;
            }

            if (clutch != null)
            {
                // Another native FSM or mod may have updated capacity since our
                // last physics step. Keep that new value as the restore baseline.
                float current = clutch.slipTorque;
                if ((!wroteCapacity || current != lastApplied) && ValidCapacity(current))
                    nativeCapacity = current;

                if (!tune.CompensateBoost || !ValidGain(configuredGain) || !ValidGain(actualGain) || actualGain <= 1f)
                    RestoreCapacity();
                else
                {
                    // GetPeakTorque includes MAX configured boost. Divide it out
                    // before using the ACTUAL spooled gain; never count boost twice.
                    float unboostedPeak = UnboostedPeakTorque(configuredGain);
                    float capacity = nativeCapacity;
                    if (TuneData.Finite(unboostedPeak) && unboostedPeak > 0f)
                        capacity = Mathf.Max(nativeCapacity, unboostedPeak * 1.5f * actualGain);
                    if (ValidCapacity(capacity) && capacity > nativeCapacity)
                    {
                        clutch.slipTorque = capacity;
                        lastApplied = capacity;
                        wroteCapacity = true;
                    }
                    else RestoreCapacity();
                }
            }
            DiagnoseBoost(actualGain);
        }

        private void RefreshComponents()
        {
            EngineComponent nextEngine = vehicle != null && vehicle.powertrain != null ? vehicle.powertrain.engine : null;
            ClutchComponent nextClutch = vehicle != null && vehicle.powertrain != null ? vehicle.powertrain.clutch : null;
            if (System.Object.ReferenceEquals(engine, nextEngine) && System.Object.ReferenceEquals(clutch, nextClutch)) return;
            RemoveModifier();
            RestoreCapacity();
            engine = nextEngine;
            clutch = nextClutch;
            torqueCached = false; torqueCurve = null;
            nativeCapacity = clutch != null && ValidCapacity(clutch.slipTorque) ? clutch.slipTorque : 0f;
            lastApplied = 0f;
            wroteCapacity = false;
        }

        private static bool ValidCapacity(float value)
        { return TuneData.Finite(value) && value > 0f && value <= 1000000f; }

        private static bool ValidGain(float value)
        { return TuneData.Finite(value) && value >= 1f && value <= 10f; }

        private float UnboostedPeakTorque(float configuredGain)
        {
            if (engine == null || engine.powerCurve == null) return 0f;
            // EstimatedPeakTorque resamples both native curves. Scalar changes
            // invalidate immediately; a bounded refresh also observes in-place
            // curve edits without allocating/inspecting keys every physics step.
            if (!torqueCached || !System.Object.ReferenceEquals(torqueCurve, engine.powerCurve) ||
                torquePower != engine.maxPower || torqueIdle != engine.idleRPM ||
                torqueLimit != engine.revLimiterRPM || torqueGain != configuredGain ||
                Time.unscaledTime >= nextTorqueRefresh)
            {
                unboostedPeak = engine.EstimatedPeakTorque / configuredGain;
                torqueCurve = engine.powerCurve; torquePower = engine.maxPower;
                torqueIdle = engine.idleRPM; torqueLimit = engine.revLimiterRPM; torqueGain = configuredGain;
                nextTorqueRefresh = Time.unscaledTime + 0.5f; torqueCached = true;
            }
            return unboostedPeak;
        }

        private void RestoreCapacity()
        {
            if (clutch != null && wroteCapacity && clutch.slipTorque == lastApplied && ValidCapacity(nativeCapacity))
                clutch.slipTorque = nativeCapacity;
            wroteCapacity = false;
        }

        private void UpdateModifier()
        {
            if (engine == null || speedLimitKph <= 0f)
            { RemoveModifier(); return; }
            if (engine.powerModifiers == null)
                engine.powerModifiers = new List<EngineComponent.PowerModifier>();
            if (!System.Object.ReferenceEquals(modifierList, engine.powerModifiers))
            {
                RemoveModifier();
                modifierList = engine.powerModifiers;
            }
            if (!modifierList.Contains(speedModifier)) modifierList.Add(speedModifier);
        }

        private void RemoveModifier()
        {
            if (modifierList != null)
                modifierList.Remove(speedModifier);
            // Preserve every native/other-mod modifier and the collection itself.
            modifierList = null;
        }

        private float SpeedPowerMultiplier()
        {
            if (vehicle == null || engine == null || vehicle.powertrain == null ||
                !System.Object.ReferenceEquals(vehicle.powertrain.engine, engine)) return 1f;
            // Signed chassis velocity avoids cutting power just because a wheel
            // spins, and permits normal reverse movement. No brakes are applied.
            float speed = speedLimitKph > 0f ? DrivingMath.SpeedPowerMultiplier(vehicle.SpeedSigned * 3.6f, speedLimitKph) : 1f;
            return speed;
        }

        private void DiagnoseBoost(float gain)
        {
            if (engine == null || vehicle == null || Plugin.Log == null || !ValidGain(gain) || gain <= 1.01f ||
                !TuneData.Finite(engine.revLimiterRPM) || engine.revLimiterRPM <= 0f ||
                engine.OutputRPM < engine.revLimiterRPM * 0.9f || Time.unscaledTime < nextDiagnostic) return;
            nextDiagnostic = Time.unscaledTime + 2f;
            TransmissionComponent transmission = vehicle.powertrain != null ? vehicle.powertrain.transmission : null;
            float shaftVelocity;
            string coupledRpm = "unavailable";
            if (transmission != null && transmission.gears != null &&
                transmission.gearIndex >= 0 && transmission.gearIndex < transmission.gears.Count &&
                WheelShaft.TryWheelShaftAngularVelocity(transmission, out shaftVelocity))
            {
                float ratio = transmission.gears[transmission.gearIndex] * transmission.finalGearRatio;
                coupledRpm = Math.Abs(shaftVelocity * ratio * (60f / (2f * Mathf.PI))).ToString("0", CultureInfo.InvariantCulture);
            }
            Plugin.Log.LogInfo("Boost driving diagnostic: road=" + (vehicle.SpeedSigned * 3.6f).ToString("0.0", CultureInfo.InvariantCulture) +
                " kph, engine=" + engine.OutputRPM.ToString("0", CultureInfo.InvariantCulture) +
                " RPM, coupled wheel-shaft=" + coupledRpm + " RPM, gain=" + gain.ToString("0.00", CultureInfo.InvariantCulture) +
                ", clutch capacity=" + (clutch != null ? clutch.slipTorque.ToString("0", CultureInfo.InvariantCulture) : "unavailable") +
                ", engagement=" + (clutch != null ? clutch.Engagement.ToString("0.00", CultureInfo.InvariantCulture) : "unavailable") +
                ", shifting=" + (transmission != null && transmission.isShifting));
        }

        internal void Restore()
        {
            speedLimitKph = 0f;
            RemoveModifier();
            RestoreCapacity();
            engine = null;
            clutch = null;
            nativeCapacity = 0f;
            torqueCached = false; torqueCurve = null; unboostedPeak = 0f;
        }
    }
}
