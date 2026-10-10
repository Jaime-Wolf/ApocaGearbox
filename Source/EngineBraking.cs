using System;
using HarmonyLib;
using NWH.VehiclePhysics2;
using NWH.VehiclePhysics2.Powertrain;
using UnityEngine;

namespace ApocaGearbox
{
    public static class EngineBrakingMath
    {
        private const float ExtraBrakingGain = 1.25f;

        public static float Multiplier(float strength, float throttle, float rpm, float idleRpm,
            float engagement, bool inGear, bool shifting)
        {
            if (!TuneData.Finite(strength) || !TuneData.Finite(throttle) || !TuneData.Finite(rpm) ||
                !TuneData.Finite(idleRpm) || !TuneData.Finite(engagement) || strength <= 0f ||
                idleRpm < 0f || rpm <= idleRpm || engagement <= 0f || !inGear || shifting)
                return 1f;

            // Fade the extra coast drag out as the driver opens the throttle and
            // as the engine approaches idle. The clutch applies its own physical
            // partial-engagement scaling; do not apply that factor twice here.
            float throttleFade = Math.Max(0f, Math.Min(1f, (0.15f - throttle) / 0.1f));
            float idleFade = Math.Max(0f, Math.Min(1f, (rpm - idleRpm) / 300f));
            return 1f + Math.Min(strength, 5f) * ExtraBrakingGain * throttleFade * idleFade;
        }
    }

    // Native negative engine torque passes through the clutch, the selected
    // gearbox ratio and the differential to the driven wheels' motor torque.
    // Scale only that native coast loss; leave power, pedals and brakes intact.
    [HarmonyPatch(typeof(EngineComponent), "CalculateICELossTorqueFromPowerCurve")]
    internal static class EngineBraking
    {
        private static float nextWarning;

        private static void Postfix(EngineComponent __instance, ref float __result)
        {
            try
            {
                if (__instance == null || !TuneData.Finite(__result) || __result >= 0f ||
                    Plugin.Enabled == null || !Plugin.Enabled.Value ||
                    __instance.engineType != EngineComponent.EngineType.ICE ||
                    !__instance.ignition || !__instance.IsRunning) return;

                GearboxState state = GearboxState.ForEngine(__instance);
                if (state == null || state.Tune == null || state.Tune.EngineBrakingStrength <= 0f) return;
                VehicleController vehicle = state.Vehicle;
                if (vehicle == null || vehicle.powertrain == null || vehicle.input == null ||
                    !System.Object.ReferenceEquals(vehicle.powertrain.engine, __instance)) return;
                TransmissionComponent transmission = vehicle.powertrain.transmission;
                ClutchComponent clutch = vehicle.powertrain.clutch;
                if (transmission == null || clutch == null) return;

                // InputSwappedThrottle is the driver's command. ThrottlePosition
                // can be zeroed internally during shifts and limiter operation.
                float multiplier = EngineBrakingMath.Multiplier(state.Tune.EngineBrakingStrength,
                    vehicle.input.InputSwappedThrottle, __instance.OutputRPM, __instance.idleRPM,
                    clutch.Engagement, transmission.Gear != 0, transmission.isShifting);
                float scaledLoss = __result * multiplier;
                if (TuneData.Finite(scaledLoss)) __result = scaledLoss;
            }
            catch (Exception exception)
            {
                if (Plugin.Log != null && Time.unscaledTime >= nextWarning)
                {
                    nextWarning = Time.unscaledTime + 5f;
                    Plugin.Log.LogWarning("Could not apply engine coast braking; native torque was kept: " + exception);
                }
            }
        }
    }
}
