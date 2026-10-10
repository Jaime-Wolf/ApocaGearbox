using System;
using ApocaGearbox;
using HutongGames.PlayMaker;
using InsaneSystems.InputManager;
using NWH.VehiclePhysics2;
using UnityEngine;
using UnityEngine.UI;

internal static class DashboardProgram
{
    private static int checks;
    private sealed class World
    {
        internal VehicleController Vehicle;
        internal GearboxState State;
        internal GameObject Player, Fan, Trigger, Input;
        internal Text Hud;
        internal PlayMakerFSM Drive;
    }
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static GameObject Child(GameObject owner, string name)
    { GameObject child = new GameObject(name); child.transform.parent = owner.transform; return child; }
    private static void Global(string name, GameObject value) { FsmVariables.GlobalVariables.Objects[name] = new FsmGameObject { Value = value }; }
    private static World Create()
    {
        DashboardRangeSwitch.Reset(); GearboxState.TunedEngines.Clear(); DriveControl.Controls.Clear();
        Time.frameCount++; Time.unscaledTime += 1f; Plugin.Enabled.Value = true;
        Input.MouseDown = Input.MouseHeld = Input.MouseUp = Input.OtherDown = Input.OtherHeld = Input.OtherUp = false;
        World world = new World(); world.Vehicle = new GameObject("truck").AddComponent<VehicleController>();
        world.Trigger = Child(world.Vehicle.gameObject, "DriveTrigger"); world.Drive = world.Trigger.AddComponent<PlayMakerFSM>();
        world.Input = Child(world.Trigger, "INPUT"); world.Player = Child(world.Trigger, "Player");
        GameObject panel = Child(world.Vehicle.gameObject, "switch_panel"); world.Fan = Child(panel, "switch_fan"); world.Fan.AddComponent<MeshFilter>();
        world.State = new GearboxState { Vehicle = world.Vehicle }; GearboxState.TunedEngines.Add(world.State);
        world.Hud = new GameObject("ItemName").AddComponent<Text>(); Global("UI_ItemName", world.Hud.gameObject);
        Global("Player", world.Player); GameObject camera = new GameObject("PlayerCamera"); camera.AddComponent<Camera>(); Global("PlayerCamera", camera);
        ActionHelpers.Picks = 0; Transform.FindCalls = Transform.PoseWrites = 0; DashboardSwitchAudio.Plays = 0;
        ActionHelpers.Pick = delegate { return new RaycastHit { collider = world.Fan.GetComponent<BoxCollider>(), distance = 1f }; };
        InputController.Actions["Use"] = new KeyAction { Key = KeyCode.Mouse0 };
        return world;
    }
    private static void Frame(float elapsed) { Time.frameCount++; Time.unscaledTime += elapsed; }
    private static void Main()
    {
        World w = Create(); DashboardRangeSwitch.Tick(); int finds = Transform.FindCalls, poses = Transform.PoseWrites;
        DashboardRangeSwitch.LateTick(); DashboardRangeSwitch.Tick();
        Check(ActionHelpers.Picks == 1, "Update and LateUpdate must share one pick.");
        Check(Transform.FindCalls == finds && Transform.PoseWrites == poses, "Same-frame sync repeats discovery or pose writes.");
        Check(w.Hud.text.Contains("HIGH"), "Native HUD missing high range label.");
        Frame(.016f); Input.MouseDown = Input.MouseHeld = true;
        Check(DashboardRangeSwitch.BlockMouseAction("Use", DashboardRangeSwitch.InputPhase.Down), "Mouse action failed to claim selector press.");
        DashboardRangeSwitch.Tick(); DashboardRangeSwitch.LateTick();
        Check(w.State.Tune.LowRangeSelected && w.Fan.transform.localRotation.X == 15f, "Accepted click did not update range and pose immediately.");
        Check(w.Hud.text.Contains("LOW") && DashboardSwitchAudio.Plays == 1, "Click did not update label or duplicated switch audio.");
        Check(ActionHelpers.Picks == 2 && Transform.FindCalls == finds, "Click repeats same-frame pick or discovers between refreshes.");
        Input.MouseDown = false; Frame(.016f); DashboardRangeSwitch.Tick(); DashboardRangeSwitch.LateTick();
        Check(DashboardRangeSwitch.BlockMouseAction("Use", DashboardRangeSwitch.InputPhase.Held), "Held Mouse0 leaked into native action.");
        int stablePoses = Transform.PoseWrites;
        Input.MouseHeld = false; Input.MouseUp = true; Frame(.016f);
        Check(DashboardRangeSwitch.BlockMouseAction("Use", DashboardRangeSwitch.InputPhase.Up), "Owned release leaked into native action.");
        Input.MouseUp = false; Frame(.016f); DashboardRangeSwitch.Tick();
        Check(Transform.PoseWrites == stablePoses, "Stable selector rewrites lever transform every frame.");
        Check(!DashboardRangeSwitch.BlockMouseAction("Use", DashboardRangeSwitch.InputPhase.Held), "Claim persisted beyond its release.");

        // A different native INPUT replaces the old object while the chassis survives.
        w.Input.transform.parent = null; w.Input.activeInHierarchy = false;
        w.Input = Child(w.Trigger, "INPUT"); Frame(.25f); DashboardRangeSwitch.Tick(); DashboardRangeSwitch.LateTick();
        Check(w.Fan.GetComponent<BoxCollider>().enabled && w.Hud.text.Contains("LOW"), "Replaced INPUT was not rebound.");
        w.Trigger.transform.parent = null; w.Trigger.activeInHierarchy = false;
        w.Trigger = Child(w.Vehicle.gameObject, "DriveTrigger"); w.Drive = w.Trigger.AddComponent<PlayMakerFSM>(); w.Input = Child(w.Trigger, "INPUT");
        w.Player.transform.parent = w.Trigger.transform; Frame(.25f); DashboardRangeSwitch.Tick();
        Check(w.Fan.GetComponent<BoxCollider>().enabled, "Replaced Drive FSM/trigger was not rebound.");
        Text previousHud = w.Hud; w.Hud = new GameObject("replacement ItemName").AddComponent<Text>(); Global("UI_ItemName", w.Hud.gameObject);
        DashboardRangeSwitch.LateTick();
        Check(previousHud.text == String.Empty && w.Hud.text.Contains("LOW"), "Replacement HUD ownership was not refreshed and cleared.");

        InputController.Actions["Use"].AlternativeKey = KeyCode.E;
        Input.MouseDown = Input.MouseHeld = true; Input.OtherDown = true; Frame(.016f);
        Check(!DashboardRangeSwitch.BlockMouseAction("Use", DashboardRangeSwitch.InputPhase.Down), "Genuine alternative keyboard input was suppressed.");
        Input.OtherDown = false; Input.MouseDown = Input.MouseHeld = false;
        w.State.Tune.LowRangeEnabled = false; int picks = ActionHelpers.Picks; Frame(.25f); DashboardRangeSwitch.Tick(); DashboardRangeSwitch.LateTick();
        Check(ActionHelpers.Picks == picks && w.Hud.text == String.Empty, "Empty selector set still picked or retained prompt.");

        w = Create(); w.State.Reject = true; Input.MouseDown = Input.MouseHeld = true; DashboardRangeSwitch.Tick(); DashboardRangeSwitch.LateTick();
        Check(!w.State.Tune.LowRangeSelected && w.Hud.text.Contains("neutral") && DashboardSwitchAudio.Plays == 0, "Rejected switch mutated range, played audio, or lost its error label.");
        Plugin.Enabled.Value = false; Frame(.016f); DashboardRangeSwitch.Tick();
        Check(!w.Fan.GetComponent<BoxCollider>().enabled && w.Hud.text == String.Empty, "Disabled gameplay retained collider/prompt.");
        Console.WriteLine("Dashboard selector: " + checks + " assertions passed.");
    }
}
