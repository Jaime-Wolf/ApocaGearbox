using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;
using UnityEngine.UI;

namespace ApocaGearbox
{
    // Both editors use the engine's native finalGearRatio. Only the native
    // increase/decrease/reset actions count as edits: load/hover actions do not.
    internal sealed class LegacyFinalDrive
    {
        private static readonly Dictionary<int, WeakReference> Editors = new Dictionary<int, WeakReference>();
        private static float nextWarning;
        private readonly GameObject engine;
        private bool tracking;
        private bool pending;
        private float pendingValue;
        private PlayMakerFSM checkFsm, displayFsm;
        private FsmFloat nativeValue, shownValue;
        private FsmString shownLabel;
        private float formattedValue = Single.NaN;
        private string formattedText;
        private UiTextSetText labelAction;
        private Text hudLabel;

        internal LegacyFinalDrive(GameObject owner)
        {
            engine = owner;
            RemoveDeadRegistrations();
            if (engine != null) Editors[engine.GetInstanceID()] = new WeakReference(this);
            // A fresh editor has no prior native edit. Its first Set establishes
            // the saved/new tune, even if native load actions are still running.
        }

        internal bool Refresh(ref float tunedValue)
        {
            if (!tracking || !pending) return false;
            pending = false;
            if (!Valid(pendingValue) || pendingValue == tunedValue) return false;
            tunedValue = pendingValue;
            return true;
        }

        internal void Set(float value)
        {
            if (engine == null || !Valid(value)) return;
            pending = false;
            tracking = true;
            try
            {
                Resolve();
                if (nativeValue != null) nativeValue.Value = value;
                MirrorDisplay(value);
            }
            catch (Exception error) { Warn(error); }
        }

        internal void Restore(float original)
        {
            Set(original);
            pending = false;
            tracking = false;
        }

        internal static void NativeEdited(FsmStateAction action)
        {
            try
            {
                if (action == null || action.Fsm == null || action.Fsm.Name != "FinalDriveRatio" ||
                    action.Fsm.Owner == null) return;
                FsmString targetFsm = null, targetVariable = null;
                FsmFloatAdd add = action as FsmFloatAdd;
                SetFsmFloat set = action as SetFsmFloat;
                if (add != null) { targetFsm = add.fsmName; targetVariable = add.variableName; }
                else if (set != null) { targetFsm = set.fsmName; targetVariable = set.variableName; }
                if (targetFsm == null || targetFsm.Value != "CheckTag" || targetVariable == null ||
                    targetVariable.Value != "finalGearRatio") return;

                LegacyFinalDrive editor = RegisteredEditor(action);
                if (editor == null) return;
                editor.Resolve();
                if (editor.nativeValue == null || !Valid(editor.nativeValue.Value)) return;
                editor.pendingValue = editor.nativeValue.Value;
                editor.pending = true;
                GearboxState.NativeFinalDriveEdited(editor.engine);
            }
            catch (Exception error) { Warn(error); }
        }

        internal static void NativeDisplay(FloatClamp action)
        {
            try
            {
                if (action == null || action.floatVariable == null || action.floatVariable.Name != "finalGearRatio") return;
                LegacyFinalDrive editor = RegisteredEditor(action);
                if (editor == null) return;
                editor.Resolve();
                if (editor.nativeValue != null && Valid(editor.nativeValue.Value))
                    editor.MirrorDisplay(editor.nativeValue.Value);
            }
            catch (Exception error) { Warn(error); }
        }

        private static LegacyFinalDrive RegisteredEditor(FsmStateAction action)
        {
            if (action == null || action.Fsm == null || action.Fsm.Name != "FinalDriveRatio" || action.Fsm.Owner == null)
                return null;
            GameObject gearbox = action.Fsm.Owner.gameObject;
            Transform parent = gearbox != null && gearbox.name == "transmission" ? gearbox.transform.parent : null;
            if (parent == null) return null;
            GameObject owner = parent.gameObject;
            WeakReference reference;
            if (!Editors.TryGetValue(owner.GetInstanceID(), out reference)) return null;
            LegacyFinalDrive editor = reference.Target as LegacyFinalDrive;
            return editor != null && editor.engine != null && editor.engine == owner && editor.tracking ? editor : null;
        }

        private void Resolve()
        {
            if (engine == null) return;
            if (checkFsm == null) { checkFsm = GearboxTarget.FindFsm(engine, "CheckTag"); nativeValue = null; }
            if (nativeValue == null && checkFsm != null && checkFsm.FsmVariables != null)
                nativeValue = checkFsm.FsmVariables.GetFsmFloat("finalGearRatio");
            if (displayFsm == null)
            {
                Transform gearbox = engine.transform.Find("transmission");
                displayFsm = gearbox != null ? GearboxTarget.FindFsm(gearbox.gameObject, "FinalDriveRatio") : null;
                shownValue = null; shownLabel = null; labelAction = null; hudLabel = null;
            }
            if (displayFsm == null || displayFsm.FsmVariables == null) return;
            if (shownValue == null) shownValue = displayFsm.FsmVariables.GetFsmFloat("finalGearRatio");
            if (shownLabel == null) shownLabel = displayFsm.FsmVariables.GetFsmString("finalGearRatio_String");
        }

        private void MirrorDisplay(float value)
        {
            if (formattedText == null || value != formattedValue)
            { formattedValue = value; formattedText = value.ToString(".00", CultureInfo.InvariantCulture); }
            if (shownValue != null) shownValue.Value = value;
            if (shownLabel != null) shownLabel.Value = formattedText;
            if (displayFsm == null || !displayFsm.enabled || displayFsm.ActiveStateName != "over" ||
                displayFsm.Fsm == null || displayFsm.Fsm.ActiveState == null || !GearboxTarget.ToolSelected()) return;
            // An active state can survive focus/camera changes. The HUD belongs
            // to this engine only while the real tool aim still selects it.
            if (hudLabel != null && hudLabel.text == formattedText) return;
            GameObject aimed; NWH.VehiclePhysics2.VehicleController vehicle; string title;
            if (!GearboxTarget.TryAim(out aimed, out vehicle, out title) || aimed != engine) return;
            if (labelAction == null)
            {
                FsmStateAction[] actions = displayFsm.Fsm.ActiveState.Actions;
                if (actions == null) return;
                foreach (FsmStateAction candidate in actions)
                {
                    UiTextSetText text = candidate as UiTextSetText;
                    if (text != null && text.text != null && text.text.Name == "finalGearRatio_String")
                    { labelAction = text; break; }
                }
            }
            if (labelAction == null || labelAction.gameObject == null || labelAction.Fsm != displayFsm.Fsm) return;
            if (hudLabel == null)
            {
                GameObject target = labelAction.Fsm.GetOwnerDefaultTarget(labelAction.gameObject);
                if (target != null) hudLabel = target.GetComponent<Text>();
            }
            if (hudLabel != null && hudLabel.text != formattedText) hudLabel.text = formattedText;
        }

        private static bool Valid(float value)
        { return TuneData.Finite(value) && value >= 0.05f && value <= 100f; }

        private static void RemoveDeadRegistrations()
        {
            List<int> remove = new List<int>();
            foreach (KeyValuePair<int, WeakReference> item in Editors)
            {
                LegacyFinalDrive editor = item.Value.Target as LegacyFinalDrive;
                if (editor == null || editor.engine == null) remove.Add(item.Key);
            }
            foreach (int key in remove) Editors.Remove(key);
        }

        private static void Warn(Exception error)
        {
            if (Time.unscaledTime < nextWarning) return;
            nextWarning = Time.unscaledTime + 5f;
            if (Plugin.Log != null)
                Plugin.Log.LogWarning("Native final-drive synchronization skipped: " + error.GetType().Name + ": " + error.Message);
        }
    }

    [HarmonyPatch(typeof(FsmFloatAdd), "OnEnter")]
    internal static class LegacyFinalDriveAddPatch
    {
        private static void Postfix(FsmFloatAdd __instance) { LegacyFinalDrive.NativeEdited(__instance); }
    }

    [HarmonyPatch(typeof(SetFsmFloat), "OnEnter")]
    internal static class LegacyFinalDriveResetPatch
    {
        private static void Postfix(SetFsmFloat __instance) { LegacyFinalDrive.NativeEdited(__instance); }
    }

    [HarmonyPatch(typeof(FloatClamp), "OnEnter")]
    internal static class LegacyFinalDriveDisplayPatch
    {
        private static void Postfix(FloatClamp __instance) { LegacyFinalDrive.NativeDisplay(__instance); }
    }
}
