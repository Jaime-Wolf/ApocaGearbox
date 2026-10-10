using System;
using System.Globalization;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using NWH.VehiclePhysics2.Powertrain;
using UnityEngine;

namespace ApocaGearbox
{
    internal sealed class CalculatorBoost
    {
        internal float Gain = 1f;
        internal string Kind = "Naturally aspirated", Description = "No turbo or supercharger fitted";
        internal GameObject Part;
    }

    internal static class CalculatorBoostSpecs
    {
        internal static bool TryRead(GameObject item, EngineComponent live, bool loose, out CalculatorBoost boost, out string error)
        {
            boost = new CalculatorBoost(); error = null;
            foreach (PlayMakerFSM id in item.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                if (id == null || id.FsmName != "ID" || id.FsmVariables == null) continue;
                FsmString identity = id.FsmVariables.GetFsmString("ID");
                if (identity == null || identity.Value != "forcedinduction") continue;
                GameObject part = id.gameObject;
                Transform mount = part.transform.parent;
                if (!part.activeInHierarchy || !part.CompareTag("vehPart") || mount == null || mount.name != "hinge" ||
                    mount.parent == null || mount.parent.name != "forcedinduction_attacher") continue;
                GameObject owner = null;
                for (Transform parent = mount.parent; parent != null; parent = parent.parent)
                    if (GearboxTarget.IsEngine(parent.gameObject)) { owner = parent.gameObject; break; }
                if (owner != item) continue;
                if (boost.Part != null) { error = "More than one fitted boost system was found on this engine."; return false; }
                boost.Part = part;
            }
            if (!loose)
            {
                if (live.forcedInduction != null && live.forcedInduction.useForcedInduction)
                {
                    boost.Gain = live.forcedInduction.powerGainMultiplier;
                    boost.Kind = live.forcedInduction.forcedInductionType.ToString();
                }
            }
            else if (boost.Part != null)
            {
                PlayMakerFSM mount = GearboxTarget.FindFsm(boost.Part, "CheckTag");
                bool foundGain = false, foundKind = false;
                if (mount != null && mount.Fsm != null && mount.Fsm.States != null)
                    foreach (FsmState state in mount.Fsm.States)
                    {
                        if (state == null || state.Actions == null) continue;
                        foreach (FsmStateAction action in state.Actions)
                        {
                            SetProperty set = action as SetProperty;
                            if (set == null || set.targetProperty == null) continue;
                            if (set.targetProperty.PropertyName == "powertrain.engine.forcedInduction.powerGainMultiplier" && set.targetProperty.FloatParameter != null)
                            { boost.Gain = set.targetProperty.FloatParameter.Value; foundGain = true; }
                            if (set.targetProperty.PropertyName == "powertrain.engine.forcedInduction.forcedInductionType" && set.targetProperty.EnumParameter != null)
                            {
                                Enum value = set.targetProperty.EnumParameter.Value;
                                if (value != null && Enum.IsDefined(typeof(EngineComponent.ForcedInduction.ForcedInductionType), Convert.ToInt32(value)))
                                { boost.Kind = ((EngineComponent.ForcedInduction.ForcedInductionType)Convert.ToInt32(value)).ToString(); foundKind = true; }
                            }
                        }
                    }
                if (!foundGain || !foundKind)
                { error = "The boost part fitted to this loose engine could not be read. Install it before calculating."; return false; }
            }
            if (!GearCalculatorMath.Positive(boost.Gain) || boost.Gain < 1f || boost.Gain > 20f)
            { error = "The fitted boost system has an unsupported power multiplier."; return false; }
            if (boost.Kind != "Naturally aspirated")
            {
                string model = boost.Kind;
                if (boost.Part != null)
                {
                    PlayMakerFSM name = GearboxTarget.FindFsm(boost.Part, "ItemName");
                    FsmString label = name != null && name.FsmVariables != null ? name.FsmVariables.GetFsmString("ItemName") : null;
                    model = label != null && !String.IsNullOrEmpty(label.Value) ? label.Value : boost.Part.name.Replace("(Clone)", "").Trim();
                }
                boost.Description = model + " / " + boost.Kind + " / " + boost.Gain.ToString("0.##", CultureInfo.InvariantCulture) + "x power";
            }
            return true;
        }
    }
}
