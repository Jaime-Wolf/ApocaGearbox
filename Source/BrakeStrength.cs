using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using NWH.VehiclePhysics2;
using NWH.VehiclePhysics2.Powertrain;

namespace ApocaGearbox
{
    // Scale after the native maximum-torque clamp, but before native axle
    // coefficients / handbrake distribution. Never change maxTorque itself.
    [HarmonyPatch(typeof(Brakes), "AddBrakeTorqueAllWheels", new Type[] { typeof(float), typeof(bool) })]
    internal static class BrakeStrength
    {
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            int anchor = -1;
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode != OpCodes.Starg && codes[i].opcode != OpCodes.Starg_S) continue;
                // The supported native method has exactly one argument write:
                // storing the clamped torque. Fail closed if its layout changes.
                if (anchor != -1) { anchor = -2; break; }
                ParameterInfo parameter = codes[i].operand as ParameterInfo;
                bool torqueArgument = parameter != null ? parameter.Position == 0 :
                    codes[i].operand != null && Convert.ToInt32(codes[i].operand) == 1;
                if (!torqueArgument) { anchor = -2; break; }
                anchor = i;
            }
            if (anchor < 0)
            {
                if (Plugin.Log != null) Plugin.Log.LogWarning("Brake strength hook did not match this game version; native braking retained.");
                return codes;
            }
            codes.InsertRange(anchor + 1, new CodeInstruction[] {
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Ldarg_2),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, typeof(BrakeStrength).GetMethod("Scale", BindingFlags.Static | BindingFlags.NonPublic)),
                new CodeInstruction(OpCodes.Starg_S, (byte)1)
            });
            return codes;
        }

        internal static float Scale(float nativeTorque, bool handbrake, Brakes brakes)
        {
            if (brakes == null || nativeTorque <= 0f ||
                !TuneData.Finite(nativeTorque)) return nativeTorque;
            TuneData tune = ForVehicle(brakes.vehicleController);
            if (tune == null) return nativeTorque;
            float strength = handbrake ? tune.HandbrakeStrength : tune.BrakeStrength;
            if (!TuneData.Finite(strength) || strength < 0.1f || strength > 5f) return nativeTorque;
            float scaled = nativeTorque * strength;
            return TuneData.Finite(scaled) ? scaled : nativeTorque;
        }

        private static TuneData ForVehicle(VehicleController vehicle)
        {
            if (Plugin.Enabled == null || !Plugin.Enabled.Value || vehicle == null || vehicle.powertrain == null) return null;
            GearboxState state = GearboxState.ForEngine(vehicle.powertrain.engine);
            return state != null && state.Vehicle == vehicle ? state.Tune : null;
        }

        internal static float WheelCap(float nativeCap, WheelComponent wheel)
        {
            if (wheel == null || !TuneData.Finite(nativeCap) || nativeCap <= 0f) return nativeCap;
            TuneData tune = ForVehicle(wheel.vehicleController);
            if (tune == null || !TuneData.Finite(tune.BrakeStrength) || !TuneData.Finite(tune.HandbrakeStrength) ||
                tune.BrakeStrength < 0.1f || tune.BrakeStrength > 5f || tune.HandbrakeStrength < 0.1f || tune.HandbrakeStrength > 5f) return nativeCap;
            // Regular and handbrake torque accumulate on a wheel in the same
            // tick. Use a shared ceiling high enough for either control so the
            // second native call cannot clip torque from the first one.
            float cap = nativeCap * Math.Max(1f, Math.Max(tune.BrakeStrength, tune.HandbrakeStrength));
            return TuneData.Finite(cap) ? cap : nativeCap;
        }
    }

    [HarmonyPatch(typeof(WheelComponent), "AddBrakeTorque", new Type[] { typeof(float), typeof(bool) })]
    internal static class BrakeStrengthWheelCap
    {
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            FieldInfo capField = typeof(Brakes).GetField("maxTorque");
            int matches = 0;
            foreach (CodeInstruction code in codes)
                if (code.opcode == OpCodes.Ldfld && Object.Equals(code.operand, capField)) matches++;
            if (matches != 2)
            {
                if (Plugin.Log != null) Plugin.Log.LogWarning("Wheel brake ceiling hook did not match this game version; native ceiling retained.");
                return codes;
            }
            for (int i = codes.Count - 1; i >= 0; i--)
                if (codes[i].opcode == OpCodes.Ldfld && Object.Equals(codes[i].operand, capField))
                    codes.InsertRange(i + 1, new CodeInstruction[] {
                        new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(OpCodes.Call, typeof(BrakeStrength).GetMethod("WheelCap", BindingFlags.Static | BindingFlags.NonPublic))
                    });
            return codes;
        }
    }
}
