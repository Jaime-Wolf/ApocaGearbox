using System;
using ApocaGearbox;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

internal static class LegacyProgram
{
    private sealed class World
    {
        internal GameObject Engine, Gearbox;
        internal FsmFloat Native, Shown;
        internal FsmString Label;
        internal LegacyFinalDrive Editor;
        internal FsmFloatAdd Add;
        internal SetFsmFloat Reset;
        internal FloatClamp Clamp;
        internal UnityEngine.UI.Text Hud;
        internal PlayMakerFSM Display;
    }
    private static int passed;
    private static World Create()
    {
        World w = new World();
        GearboxTarget.Selected = false; GearboxTarget.Aim = null;
        w.Engine = new GameObject("V8"); w.Gearbox = new GameObject("transmission");
        w.Gearbox.transform.parent = w.Engine.transform;
        w.Engine.transform.Children["transmission"] = w.Gearbox.transform;
        PlayMakerFSM check = new PlayMakerFSM(), display = new PlayMakerFSM();
        w.Native = new FsmFloat { Value = 3.4f }; w.Shown = new FsmFloat { Name = "finalGearRatio" }; w.Label = new FsmString { Name = "finalGearRatio_String" };
        check.FsmVariables.Floats["finalGearRatio"] = w.Native;
        display.FsmVariables.Floats["finalGearRatio"] = w.Shown;
        display.FsmVariables.Strings["finalGearRatio_String"] = w.Label;
        w.Engine.Fsms["CheckTag"] = check; w.Gearbox.Fsms["FinalDriveRatio"] = display;
        Fsm fsm = new Fsm { Name = "FinalDriveRatio", Owner = new Component { gameObject = w.Gearbox } };
        w.Hud = new UnityEngine.UI.Text { text = "3.40" };
        GameObject hudObject = new GameObject("UI_ItemName_2"); hudObject.ExtraComponent = w.Hud;
        UiTextSetText textAction = new UiTextSetText { Fsm = fsm, text = w.Label, gameObject = new FsmOwnerDefault { Target = hudObject } };
        fsm.ActiveState = new FsmState { Name = "over", Actions = new FsmStateAction[] { textAction } };
        display.Fsm = fsm; w.Display = display;
        w.Clamp = new FloatClamp { Fsm = fsm, floatVariable = w.Shown };
        w.Add = new FsmFloatAdd { Fsm = fsm, fsmName = new FsmString { Value = "CheckTag" }, variableName = new FsmString { Value = "finalGearRatio" } };
        w.Reset = new SetFsmFloat { Fsm = fsm, fsmName = new FsmString { Value = "CheckTag" }, variableName = new FsmString { Value = "finalGearRatio" } };
        w.Editor = new LegacyFinalDrive(w.Engine);
        return w;
    }
    private static void Assert(bool okay, string message) { if (!okay) throw new Exception(message); }
    private static void Test(string name, Action test)
    { test(); passed++; Console.WriteLine("PASS " + name); }
    private static void Edit(World w, float value, FsmStateAction action)
    { w.Native.Value = value; LegacyFinalDrive.NativeEdited(action); }
    private static void Main()
    {
        Test("first read retains saved tune", delegate { World w = Create(); float tune = 5f; Assert(!w.Editor.Refresh(ref tune) && tune == 5f, "First read copied stock."); });
        Test("Set updates both native floats and string", delegate { World w = Create(); w.Editor.Set(5.25f); Assert(w.Native.Value == 5.25f && w.Shown.Value == 5.25f && w.Label.Value == "5.25", "Editors differ."); });
        Test("bootstrap writes ignored", delegate { World w = Create(); w.Editor.Set(5f); w.Native.Value = 3.4f; float tune = 5f; Assert(!w.Editor.Refresh(ref tune) && tune == 5f, "Bootstrap replaced saved tune."); });
        Test("increase consumed once", delegate { World w = Create(); w.Editor.Set(5f); Edit(w, 5.1f, w.Add); float tune = 5f; Assert(w.Editor.Refresh(ref tune) && tune == 5.1f && !w.Editor.Refresh(ref tune), "Increase missing/repeated."); });
        Test("decrease consumed", delegate { World w = Create(); w.Editor.Set(5f); Edit(w, 4.9f, w.Add); float tune = 5f; Assert(w.Editor.Refresh(ref tune) && tune == 4.9f, "Decrease missing."); });
        Test("reset to native baseline consumed", delegate { World w = Create(); w.Editor.Set(5f); Edit(w, 3.4f, w.Reset); float tune = 5f; Assert(w.Editor.Refresh(ref tune) && tune == 3.4f, "Reset to stock ignored."); });
        Test("last native edit wins", delegate { World w = Create(); w.Editor.Set(5f); Edit(w, 5.1f, w.Add); Edit(w, 4.8f, w.Add); float tune = 5f; Assert(w.Editor.Refresh(ref tune) && tune == 4.8f, "Last edit lost."); });
        Test("unchanged native edit is consumed", delegate { World w = Create(); w.Editor.Set(5f); Edit(w, 5f, w.Add); float tune = 5f; Assert(!w.Editor.Refresh(ref tune), "Unchanged edit changed tune."); w.Native.Value = 7f; Assert(!w.Editor.Refresh(ref tune), "Queued edit leaked."); });
        Test("new editor purges earlier edits", delegate { World w = Create(); w.Editor.Set(5f); Edit(w, 7f, w.Add); w.Editor = new LegacyFinalDrive(w.Engine); w.Editor.Set(6f); float tune = 6f; Assert(!w.Editor.Refresh(ref tune) && tune == 6f, "Old queued edit leaked."); });
        Test("fresh custom apply clears native queue", delegate { World w = Create(); w.Editor.Set(5f); Edit(w, 7f, w.Add); w.Editor.Set(6f); float tune = 6f; Assert(!w.Editor.Refresh(ref tune) && tune == 6f, "Native queue replaced editor apply."); });
        Test("wrong source FSM ignored", delegate { World w = Create(); w.Editor.Set(5f); w.Add.Fsm.Name = "saveItemVar"; Edit(w, 7f, w.Add); float tune = 5f; Assert(!w.Editor.Refresh(ref tune), "Load counted as edit."); });
        Test("wrong target FSM ignored", delegate { World w = Create(); w.Editor.Set(5f); w.Add.fsmName.Value = "Other"; Edit(w, 7f, w.Add); float tune = 5f; Assert(!w.Editor.Refresh(ref tune), "Other FSM counted."); });
        Test("wrong target variable ignored", delegate { World w = Create(); w.Editor.Set(5f); w.Add.variableName.Value = "temperature"; Edit(w, 7f, w.Add); float tune = 5f; Assert(!w.Editor.Refresh(ref tune), "Other variable counted."); });
        Test("other engine isolated", delegate { World a = Create(), b = Create(); a.Editor.Set(5f); b.Editor.Set(6f); Edit(b, 7f, b.Add); float tune = 5f; Assert(!a.Editor.Refresh(ref tune), "Other engine edited this tune."); tune = 6f; Assert(b.Editor.Refresh(ref tune) && tune == 7f, "Other engine edit lost."); });
        Test("invalid native edits ignored", delegate { World w = Create(); w.Editor.Set(5f); float tune = 5f; foreach (float invalid in new float[] { Single.NaN, Single.PositiveInfinity, -1f, 0f, 0.01f, 101f }) { Edit(w, invalid, w.Add); Assert(!w.Editor.Refresh(ref tune) && tune == 5f, "Invalid edit accepted."); } });
        Test("invalid Set ignored", delegate { World w = Create(); w.Editor.Set(5f); foreach (float invalid in new float[] { Single.NaN, -1f, 101f }) w.Editor.Set(invalid); Assert(w.Native.Value == 5f && w.Shown.Value == 5f, "Invalid Set written."); });
        Test("Restore updates original and stops tracking", delegate { World w = Create(); w.Editor.Set(5f); Edit(w, 7f, w.Add); w.Editor.Restore(3.4f); Assert(w.Native.Value == 3.4f && w.Shown.Value == 3.4f && w.Label.Value == "3.40", "Original not restored."); Edit(w, 7f, w.Add); float tune = 5f; Assert(!w.Editor.Refresh(ref tune), "Disabled edit changed saved tune."); });
        Test("Set reactivates restored editor", delegate { World w = Create(); w.Editor.Set(5f); w.Editor.Restore(3.4f); w.Editor.Set(5f); Edit(w, 5.1f, w.Add); float tune = 5f; Assert(w.Editor.Refresh(ref tune) && tune == 5.1f, "Restored editor did not reactivate."); });
        Test("untuned native edits ignored", delegate { World w = Create(); Edit(w, 7f, w.Add); float tune = 5f; Assert(!w.Editor.Refresh(ref tune), "Untuned edit queued."); });
        Test("missing native UI tolerated", delegate { World w = Create(); w.Gearbox.Fsms.Clear(); w.Editor.Set(5f); Assert(w.Native.Value == 5f, "Missing display prevented native write."); });
        Test("clamp mirror preserves queued native edit", delegate { World w = Create(); w.Editor.Set(5f); Edit(w, 5.1f, w.Add); w.Shown.Value = 1f; LegacyFinalDrive.NativeDisplay(w.Clamp); float tune = 5f; Assert(w.Editor.Refresh(ref tune) && tune == 5.1f && w.Shown.Value == 5.1f, "Clamp consumed queued edit."); });
        Test("native hover shows full custom range", delegate { World w = Create(); w.Editor.Set(0.5f); w.Shown.Value = 1f; LegacyFinalDrive.NativeDisplay(w.Clamp); Assert(w.Shown.Value == 0.5f && w.Label.Value == ".50", "Native lower clamp won."); w.Editor.Set(20f); w.Shown.Value = 15f; LegacyFinalDrive.NativeDisplay(w.Clamp); Assert(w.Shown.Value == 20f && w.Label.Value == "20.00", "Native upper clamp won."); });
        Test("hover HUD updates immediately", delegate { World w = Create(); GearboxTarget.Selected = true; GearboxTarget.Aim = w.Engine; w.Editor.Set(5.25f); Assert(w.Hud.text == "5.25", "HUD kept old value."); });
        Test("paused then resumed reflects same value", delegate { World w = Create(); GearboxTarget.Selected = true; w.Editor.Set(5.25f); Assert(w.Hud.text == "3.40", "Paused HUD was written."); GearboxTarget.Aim = w.Engine; w.Editor.Set(5.25f); Assert(w.Hud.text == "5.25", "Same value stayed stale after resume."); });
        Test("other engine HUD not overwritten", delegate { World w = Create(); GearboxTarget.Selected = true; GearboxTarget.Aim = new GameObject("Other engine"); w.Editor.Set(5.25f); Assert(w.Hud.text == "3.40" && w.Label.Value == "5.25", "Other engine HUD was overwritten."); });
        Test("disabled native FSM HUD not overwritten", delegate { World w = Create(); GearboxTarget.Selected = true; GearboxTarget.Aim = w.Engine; w.Display.enabled = false; w.Editor.Set(5.25f); Assert(w.Hud.text == "3.40", "Disabled native HUD was written."); });
        Test("unrelated clamp ignored", delegate { World w = Create(); w.Editor.Set(20f); w.Clamp.floatVariable = new FsmFloat { Name = "temperature", Value = 1f }; w.Shown.Value = 15f; LegacyFinalDrive.NativeDisplay(w.Clamp); Assert(w.Shown.Value == 15f, "Unrelated clamp remirrored final drive."); });
        Test("untuned clamp unchanged", delegate { World w = Create(); w.Native.Value = 20f; w.Shown.Value = 15f; LegacyFinalDrive.NativeDisplay(w.Clamp); Assert(w.Shown.Value == 15f, "Untuned native clamp was bypassed."); });
        Test("native edits notify the owning engine immediately", delegate {
            World w = Create(); w.Editor.Set(5f); int notifications = GearboxState.NativeNotifications;
            Edit(w, 5.1f, w.Add); Assert(GearboxState.NativeNotifications == notifications + 1 && GearboxState.NativeEngine == w.Engine,
                "Native increase did not notify its engine-owned tuning.");
            Edit(w, 3.4f, w.Reset); Assert(GearboxState.NativeNotifications == notifications + 2,
                "Native reset did not notify its engine-owned tuning.");
        });
        Test("unrelated native actions do not notify gearbox state", delegate {
            World w = Create(); w.Editor.Set(5f); int notifications = GearboxState.NativeNotifications;
            w.Add.variableName.Value = "temperature"; Edit(w, 7f, w.Add);
            Assert(GearboxState.NativeNotifications == notifications, "Unrelated action notified gearbox state.");
        });
        Console.WriteLine("Legacy final drive: " + passed + "/" + passed + " passed.");
    }
}
