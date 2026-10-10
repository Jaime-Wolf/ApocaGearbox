using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace ApocaGearbox
{
    internal static class CalculatorEngineSpecs
    {
        internal static bool ReadLoose(GameObject item, out float power, out float idle, out float redline)
        {
            power = idle = redline = 0f;
            if (!ReadRpm(item, out idle, out redline)) return false;
            PlayMakerFSM condition = GearboxTarget.FindFsm(item, "Condition");
            if (condition == null || condition.Fsm == null || condition.Fsm.States == null) return false;
            // A detached engine's maxPower variable is not reliable: stock
            // prefabs initialize it to a shared placeholder. Read its own native
            // power-setting actions and follow condition comparisons read-only.
            FsmState start = null;
            foreach (FsmState state in condition.Fsm.States)
            {
                float candidate = StatePower(state);
                if (candidate > power) { power = candidate; start = state; }
            }
            foreach (FsmState state in condition.Fsm.States)
                if (state != null && state.Name == condition.ActiveStateName && GearCalculatorMath.Positive(StatePower(state))) start = state;
            if (start == null) return false;
            HashSet<FsmState> visited = new HashSet<FsmState>();
            FsmState current = start;
            while (current != null && visited.Add(current))
            {
                float candidate = StatePower(current);
                if (!GearCalculatorMath.Positive(candidate)) return false;
                power = candidate;
                FsmState next = null;
                foreach (FsmStateAction action in current.Actions)
                {
                    FloatCompare compare = action as FloatCompare;
                    if (compare == null || compare.float1 == null || compare.float2 == null || compare.float1.Name != "Condition") continue;
                    float a = compare.float1.Value, b = compare.float2.Value;
                    if (!TuneData.Finite(a) || !TuneData.Finite(b)) return false;
                    float tolerance = compare.tolerance != null ? Math.Abs(compare.tolerance.Value) : 0f;
                    FsmEvent selected = Math.Abs(a - b) <= tolerance ? compare.equal : (a < b ? compare.lessThan : compare.greaterThan);
                    if (selected == null || current.Transitions == null) continue;
                    foreach (FsmTransition transition in current.Transitions)
                        if (transition != null && transition.EventName == selected.Name)
                            foreach (FsmState state in condition.Fsm.States)
                                if (state != null && state.Name == transition.ToState) next = state;
                }
                if (next == null || next == current) break;
                if (visited.Contains(next)) return false;
                if (!GearCalculatorMath.Positive(StatePower(next))) return false;
                current = next;
            }
            return GearCalculatorMath.Positive(power) && GearCalculatorMath.Positive(idle) && redline > idle * 1.4f;
        }

        internal static bool ReadRpm(GameObject item, out float idle, out float redline)
        {
            idle = redline = 0f;
            PlayMakerFSM mount = GearboxTarget.FindFsm(item, "CheckTag");
            if (mount == null || mount.Fsm == null || mount.Fsm.States == null) return false;
            foreach (FsmState state in mount.Fsm.States)
                if (state != null && state.Actions != null)
                    foreach (FsmStateAction action in state.Actions)
                    {
                        SetProperty set = action as SetProperty;
                        if (set == null || set.targetProperty == null || set.targetProperty.FloatParameter == null) continue;
                        string path = set.targetProperty.PropertyName;
                        if (path == "powertrain.engine.idleRPM") idle = set.targetProperty.FloatParameter.Value;
                        if (path == "powertrain.engine.revLimiterRPM") redline = set.targetProperty.FloatParameter.Value;
                    }
            return TuneData.Finite(idle) && idle > 0f && TuneData.Finite(redline) && redline > idle;
        }

        private static float StatePower(FsmState state)
        {
            if (state == null || state.Actions == null) return 0f;
            foreach (FsmStateAction action in state.Actions)
            {
                SetFloatValue set = action as SetFloatValue;
                if (set != null && set.floatVariable != null && set.floatVariable.Name == "maxPower" && set.floatValue != null)
                    return set.floatValue.Value;
            }
            return 0f;
        }
    }
}
