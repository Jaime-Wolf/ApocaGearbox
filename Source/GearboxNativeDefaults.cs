using System;
using System.Globalization;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using NWH.VehiclePhysics2.Powertrain;
using UnityEngine;

namespace ApocaGearbox
{
    // Read the engine's own native mount actions, not a chassis that may still
    // contain another engine's tune. Never modify the FSM or its action values.
    public static class GearboxNativeDefaults
    {
        public static TuneData Capture(GameObject engine, TransmissionComponent transmission)
        {
            // Retained for modders using the original public API. Defaults
            // belong to the engine's native actions, never the live chassis.
            return Capture(engine);
        }

        internal static TuneData Capture(GameObject engine)
        {
            if (engine == null) return null;
            PlayMakerFSM check = GearboxTarget.FindFsm(engine, "CheckTag");
            if (check == null || check.Fsm == null || check.Fsm.States == null) return null;
            FsmFloat finalDrive = check.FsmVariables.GetFsmFloat("finalGearRatio");
            if (finalDrive == null || !TuneData.Finite(finalDrive.Value)) return null;

            TuneData result = new TuneData { FinalDrive = finalDrive.Value, TuneShifts = false };
            bool foundUp = false, foundDown = false;
            try
            {
                foreach (FsmState state in check.Fsm.States)
                {
                    if (state == null || state.Actions == null) continue;
                    foreach (FsmStateAction action in state.Actions)
                    {
                        CallMethod call = action as CallMethod;
                        if (call != null && call.methodName != null && call.methodName.Value == "SetGearArrayByValue" &&
                            call.parameters != null && call.parameters.Length == 1 && call.parameters[0] != null &&
                            call.parameters[0].arrayValue != null)
                        {
                            object[] values = call.parameters[0].arrayValue.Values;
                            if (values != null && values.Length >= 2 && values.Length <= 32)
                            {
                                float[] ratios = new float[values.Length];
                                for (int i = 0; i < values.Length; i++)
                                {
                                    if (values[i] == null) return null;
                                    ratios[i] = Convert.ToSingle(values[i], CultureInfo.InvariantCulture);
                                }
                                result.Ratios = ratios;
                            }
                        }

                        SetProperty set = action as SetProperty;
                        if (set == null || set.targetProperty == null || set.targetProperty.FloatParameter == null) continue;
                        FsmProperty property = set.targetProperty;
                        string path = property.PropertyName;
                        if (String.IsNullOrEmpty(path)) continue;
                        int dot = path.LastIndexOf('.');
                        string member = dot < 0 ? path : path.Substring(dot + 1);
                        if (String.Equals(member, "UpshiftRPM", StringComparison.OrdinalIgnoreCase))
                        { result.ShiftUp = property.FloatParameter.Value; foundUp = true; }
                        else if (String.Equals(member, "DownshiftRPM", StringComparison.OrdinalIgnoreCase))
                        { result.ShiftDown = property.FloatParameter.Value; foundDown = true; }
                    }
                }

                string error;
                // Missing native actions are an unsupported engine, not a reason
                // to record a possibly modified live transmission as its defaults.
                if (result.Ratios == null || !foundUp || !foundDown || !result.Validate(null, out error)) return null;
                return result;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
