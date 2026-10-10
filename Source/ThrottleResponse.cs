using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using NWH.VehiclePhysics2;
using NWH.VehiclePhysics2.Input;
using NWH.VehiclePhysics2.Powertrain;
using UnityEngine;

namespace ApocaGearbox
{
    internal static class ThrottleResponse
    {
        private sealed class InputState
        {
            public InputState() { }
            internal readonly ThrottleRamp Ramp = new ThrottleRamp();
            internal GearboxState Owner;
            internal EngineComponent Engine;
            internal TuneData Tune;
            internal int Direction;
            internal int Frame = -1;
        }

        // Input handlers can disappear on loading or engine/vehicle swaps.
        // Weak keys do not keep their vehicle graphs alive.
        private static ConditionalWeakTable<VehicleInputHandler, InputState> inputs =
            new ConditionalWeakTable<VehicleInputHandler, InputState>();
        private static float nextWarning;

        internal static void ResetAll()
        { inputs = new ConditionalWeakTable<VehicleInputHandler, InputState>(); }

        private static GearboxState FindTune(VehicleInputHandler input)
        {
            VehicleController vehicle = input != null ? input.vehicleController : null;
            if (vehicle == null || !vehicle.gameObject.activeInHierarchy || vehicle.input != input ||
                vehicle.powertrain == null || vehicle.powertrain.engine == null) return null;
            GearboxState owner = GearboxState.ForEngine(vehicle.powertrain.engine);
            if (owner == null || owner.Vehicle != vehicle || owner.Tune == null ||
                !TuneData.Finite(owner.Tune.ThrottleRiseSeconds) || owner.Tune.ThrottleRiseSeconds <= 0f ||
                owner.Tune.ThrottleRiseSeconds > 3f) return null;
            return owner;
        }

        private static bool Blocked()
        {
            return !Application.isFocused || !Apocasetter.GameMenu.InGame ||
                Apocasetter.GameMenu.Paused || Apocasetter.InputBlocker.Active;
        }

        internal static void UpdateInput(VehicleInputHandler input, float nativeThrottle)
        {
            try
            {
                GearboxState owner = FindTune(input);
                if (owner == null || Blocked()) { if (input != null) inputs.Remove(input); return; }
                InputState state = inputs.GetOrCreateValue(input);
                EngineComponent engine = owner.Vehicle.powertrain.engine;
                TransmissionComponent gearbox = owner.Vehicle.powertrain.transmission;
                int direction = gearbox != null ? Math.Sign(gearbox.Gear) : 0;
                bool changed = state.Owner != owner || state.Engine != engine || state.Tune != owner.Tune ||
                    state.Direction != direction || Time.frameCount > state.Frame + 1;
                if (changed) { state.Ramp.Reset(); state.Frame = -1; }
                state.Owner = owner; state.Engine = engine; state.Tune = owner.Tune; state.Direction = direction;
                // Native input is collected once per render frame. Further
                // readers and physics substeps reuse this value, never advance it.
                float dt = state.Frame == Time.frameCount ? 0f : Time.deltaTime;
                state.Ramp.Step(nativeThrottle, owner.Tune.ThrottleRiseSeconds, dt);
                state.Frame = Time.frameCount;
            }
            catch (Exception exception) { if (input != null) inputs.Remove(input); Warn(exception); }
        }

        internal static float FilterInput(VehicleInputHandler input, float nativeThrottle)
        {
            try
            {
                GearboxState owner = FindTune(input);
                if (owner == null) { if (input != null) inputs.Remove(input); return nativeThrottle; }
                if (Blocked()) { inputs.Remove(input); return 0f; }
                InputState state;
                if (!inputs.TryGetValue(input, out state) || state.Owner != owner || state.Tune != owner.Tune ||
                    state.Engine != owner.Vehicle.powertrain.engine) return 0f;
                TransmissionComponent gearbox = owner.Vehicle.powertrain.transmission;
                if (state.Direction != (gearbox != null ? Math.Sign(gearbox.Gear) : 0) ||
                    Time.frameCount > state.Frame + 1) { inputs.Remove(input); return 0f; }
                // Releasing a pedal must never retain cached power, even if
                // another native input path changes it before the next update.
                if (!TuneData.Finite(nativeThrottle) || nativeThrottle <= 0f) { state.Ramp.Reset(); return 0f; }
                return Math.Min(nativeThrottle, state.Ramp.Value);
            }
            catch (Exception exception) { if (input != null) inputs.Remove(input); Warn(exception); return nativeThrottle; }
        }

        private static void Warn(Exception exception)
        {
            if (Plugin.Log != null && Time.unscaledTime >= nextWarning)
            {
                nextWarning = Time.unscaledTime + 5f;
                Plugin.Log.LogWarning("Throttle response could not be applied; native input was kept: " + exception);
            }
        }
    }

    // Keep raw input, reverse-pedal mapping and brakes native. Cache a filtered
    // throttle after collection, before engine/clutch/gearbox physics read it.
    [HarmonyPatch(typeof(VehicleInputHandler), "VC_Update")]
    internal static class ThrottleResponseUpdatePatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(VehicleInputHandler __instance, float ____inputSwappedThrottle)
        { ThrottleResponse.UpdateInput(__instance, ____inputSwappedThrottle); }
    }

    [HarmonyPatch(typeof(VehicleInputHandler), "get_InputSwappedThrottle")]
    internal static class ThrottleResponseReadPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(VehicleInputHandler __instance, ref float __result)
        { __result = ThrottleResponse.FilterInput(__instance, __result); }
    }
}
