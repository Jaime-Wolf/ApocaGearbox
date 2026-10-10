using System;
using System.Collections.Generic;
using System.Globalization;
using HutongGames.PlayMaker;
using InsaneSystems.InputManager;
using NWH.VehiclePhysics2;
using UnityEngine;
using UnityEngine.UI;

namespace ApocaGearbox
{
    // The two blank factory levers share one aim, input claim, and native HUD
    // label. Range belongs to the engine; drive selection belongs to the car.
    internal static class DashboardRangeSwitch
    {
        internal const float Reach = 2f;
        internal const int PickMask = 68353;
        internal enum InputPhase { Down, Held, Up }
        private enum SelectorKind { Range, Drive }
        private const string RangeSwitchPath = "switch_panel/switch_fan";
        private const string DriveSwitchPath = "switch_panel/switch_fuelpump";
        private static readonly List<Binding> bindings = new List<Binding>();
        private static Binding active;
        private static int syncedFrame = -1;
        private static float nextDiscovery;
        private static int discoveredEngines = -1, discoveredControls = -1;
        private static int consumedFrame = -1;
        private static bool suppressMouse;
        private static float nextWarning;
        private static Text itemName;
        private static GameObject itemNameOwner;
        private static string ownedName;

        private sealed class Binding
        {
            internal VehicleController Vehicle;
            internal SelectorKind Kind;
            internal PlayMakerFSM Drive;
            internal GameObject Input;
            internal GameObject Fan;
            internal BoxCollider Collider;
            internal Quaternion OriginalRotation;
            internal int OriginalLayer;
            internal GearboxState State;
            internal DriveControl DriveState;
            internal bool Animated;
            internal bool Eligible;
            internal bool Discovered;
            internal bool PoseSelection;
            internal string Error;
            internal float ErrorUntil;
            internal string ErrorLabel;
            internal string NameText;
            internal bool ShownSelection;
            internal float ShownReduction = Single.NaN;

            internal void Deactivate()
            {
                Eligible = false;
                if (Collider != null && Collider.enabled) Collider.enabled = false;
                if (Fan == null) return;
                if (Animated)
                {
                    if (Fan.layer == 8) Fan.layer = OriginalLayer;
                    Fan.transform.localRotation = OriginalRotation;
                }
                Animated = false;
            }
        }

        internal static bool GameplayReady()
        {
            return Plugin.Enabled != null && Plugin.Enabled.Value && Application.isFocused &&
                Apocasetter.GameMenu.InGame && !Apocasetter.GameMenu.Paused &&
                !Apocasetter.InputBlocker.Active && !TuningPanel.IsOpen && Time.timeScale > 0f &&
                Cursor.lockState == CursorLockMode.Locked;
        }

        // Player's body is parented to this car's DriveTrigger/sitPos by the
        // native Drive.enter state. The camera is independently reparented.
        internal static bool DriverMatches(VehicleController vehicle, PlayMakerFSM drive, GameObject input, GameObject player)
        {
            return vehicle != null && vehicle.gameObject.activeInHierarchy && player != null &&
                player.GetComponentInParent<VehicleController>() == vehicle && drive != null &&
                drive.isActiveAndEnabled && drive.ActiveStateName == "inCar" &&
                drive.gameObject.name == "DriveTrigger" && drive.transform.parent == vehicle.transform &&
                input != null && input.activeInHierarchy && input.name == "INPUT" && input.transform.parent == drive.transform;
        }

        internal static bool OwnedHit(GameObject fan, Collider ownedCollider, Collider hitCollider, float distance)
        {
            return fan != null && fan.activeInHierarchy && ownedCollider != null && ownedCollider.enabled &&
                hitCollider == ownedCollider && ownedCollider.gameObject == fan &&
                TuneData.Finite(distance) && distance >= 0f && distance <= Reach;
        }

        internal static bool VehicleAllowed(VehicleController vehicle, PlayMakerFSM drive, GameObject input, GameObject player)
        {
            if (vehicle == null || !vehicle.gameObject.activeInHierarchy || player == null) return false;
            VehicleController driver = player.GetComponentInParent<VehicleController>();
            return driver == null || DriverMatches(vehicle, drive, input, player);
        }

        private static Binding Sync()
        {
            if (!GameplayReady()) { DeactivateAll(); syncedFrame = -1; return null; }
            if (syncedFrame == Time.frameCount) return active;
            syncedFrame = Time.frameCount;
            FsmGameObject playerVariable = FsmVariables.GlobalVariables.GetFsmGameObject("Player");
            GameObject player = playerVariable == null ? null : playerVariable.Value;
            VehicleController driver = player == null ? null : player.GetComponentInParent<VehicleController>();
            if (Time.unscaledTime >= nextDiscovery || discoveredEngines != GearboxState.TunedEngines.Count || discoveredControls != DriveControl.Controls.Count)
                Discover();
            bool anyEligible = false;
            for (int i = 0; i < bindings.Count; i++)
            {
                Binding candidate = bindings[i];
                if (!BindingEligible(candidate, player, driver)) { candidate.Deactivate(); continue; }
                candidate.Eligible = true; anyEligible = true;
                ApplyPose(candidate);
            }
            active = null;
            RaycastHit hit;
            if (!anyEligible || !Pick(out hit)) return null;
            for (int i = 0; i < bindings.Count; i++)
                if (bindings[i].Eligible && OwnedHit(bindings[i].Fan, bindings[i].Collider, hit.collider, hit.distance))
                { active = bindings[i]; return active; }
            return null;
        }

        private static bool BindingEligible(Binding candidate, GameObject player, VehicleController driver)
        {
            if (candidate.Vehicle == null || !candidate.Vehicle.gameObject.activeInHierarchy || player == null ||
                candidate.Fan == null || candidate.Collider == null || !candidate.Fan.activeInHierarchy) return false;
            bool configured = candidate.Kind == SelectorKind.Range ? candidate.State != null && candidate.State.Vehicle == candidate.Vehicle &&
                candidate.State.Tune != null && candidate.State.Tune.LowRangeEnabled && candidate.Vehicle.powertrain != null &&
                GearboxState.ForTransmission(candidate.Vehicle.powertrain.transmission) == candidate.State : candidate.DriveState != null && candidate.DriveState.Enabled;
            if (!configured) return false;
            return driver == null || (driver == candidate.Vehicle && DriverBindingMatches(candidate));
        }

        private static bool DriverBindingMatches(Binding candidate)
        {
            return candidate.Drive != null && candidate.Drive.isActiveAndEnabled && candidate.Drive.ActiveStateName == "inCar" &&
                candidate.Drive.transform.parent == candidate.Vehicle.transform && candidate.Input != null && candidate.Input.activeInHierarchy &&
                candidate.Input.transform.parent == candidate.Drive.transform;
        }

        private static void ApplyPose(Binding candidate)
        {
            bool selected = Selected(candidate);
            if (candidate.Fan.layer != 8) candidate.Fan.layer = 8;
            if (!candidate.Collider.enabled) candidate.Collider.enabled = true;
            if (!candidate.Animated || candidate.PoseSelection != selected)
            {
                candidate.Fan.transform.localRotation = Quaternion.Euler(selected ? 15f : -15f, 0f, 0f);
                candidate.PoseSelection = selected; candidate.Animated = true;
            }
        }

        private static void Discover()
        {
            nextDiscovery = Time.unscaledTime + 0.2f;
            discoveredEngines = GearboxState.TunedEngines.Count; discoveredControls = DriveControl.Controls.Count;
            for (int i = 0; i < bindings.Count; i++) bindings[i].Discovered = false;
            for (int i = 0; i < GearboxState.TunedEngines.Count; i++)
            {
                GearboxState state = GearboxState.TunedEngines[i];
                VehicleController vehicle = state == null ? null : state.Vehicle;
                if (vehicle == null || vehicle.powertrain == null || vehicle.powertrain.transmission == null ||
                    state.Tune == null || !state.Tune.LowRangeEnabled ||
                    GearboxState.ForTransmission(vehicle.powertrain.transmission) != state) continue;
                BindSelector(SelectorKind.Range, vehicle, state, null);
            }
            // Vehicle controls are registered separately: the drive lever can
            // work with an untuned engine and with high/low entirely disabled.
            for (int i = 0; i < DriveControl.Controls.Count; i++)
            {
                DriveControl registered = DriveControl.Controls[i];
                VehicleController vehicle = registered == null ? null : registered.Vehicle;
                DriveControl control;
                if (vehicle == null || !DriveControl.TryGet(vehicle, out control) || control == null ||
                    control != registered || !control.Enabled || !control.IsReady) continue;
                BindSelector(SelectorKind.Drive, vehicle, null, control);
            }
            for (int i = bindings.Count - 1; i >= 0; i--)
            {
                if (!bindings[i].Discovered)
                {
                    bindings[i].Deactivate();
                    if (bindings[i].Collider != null) UnityEngine.Object.Destroy(bindings[i].Collider);
                    bindings.RemoveAt(i);
                }
            }
        }

        private static void BindSelector(SelectorKind kind, VehicleController vehicle,
            GearboxState rangeState, DriveControl driveState)
        {
            Binding candidate = null;
            for (int i = 0; i < bindings.Count; i++)
                if (bindings[i].Vehicle == vehicle && bindings[i].Kind == kind) { candidate = bindings[i]; break; }
            Transform trigger = vehicle.transform.Find("DriveTrigger");
            Transform input = trigger == null ? null : trigger.Find("INPUT");
            PlayMakerFSM drive = trigger == null ? null : GearboxTarget.FindFsm(trigger.gameObject, "Drive");
            Transform fan = vehicle.transform.Find(kind == SelectorKind.Range ? RangeSwitchPath : DriveSwitchPath);
            if (candidate != null && (candidate.Fan == null || candidate.Collider == null || fan != candidate.Fan.transform))
            {
                candidate.Deactivate();
                if (candidate.Collider != null) UnityEngine.Object.Destroy(candidate.Collider);
                bindings.Remove(candidate); candidate = null;
            }
            if (candidate == null)
            {
                MeshFilter filter = fan == null ? null : fan.GetComponent<MeshFilter>();
                // A different mod's occupied lever is not ours to claim.
                if (fan == null || filter == null || filter.sharedMesh == null || fan.GetComponent<Collider>() != null) return;
                Bounds bounds = filter.sharedMesh.bounds;
                Vector3 size = bounds.size;
                size.x = Mathf.Clamp(size.x + 0.004f, 0.006f, 0.024f);
                size.y = Mathf.Clamp(size.y + 0.004f, 0.006f, 0.060f);
                size.z = Mathf.Clamp(size.z + 0.004f, 0.006f, 0.060f);
                BoxCollider collider = fan.gameObject.AddComponent<BoxCollider>();
                collider.hideFlags = HideFlags.HideAndDontSave;
                collider.center = bounds.center; collider.size = size;
                collider.isTrigger = true; collider.enabled = false;
                candidate = new Binding { Vehicle = vehicle, Kind = kind, Drive = drive, Input = input == null ? null : input.gameObject,
                    Fan = fan.gameObject, Collider = collider, OriginalRotation = fan.localRotation,
                    OriginalLayer = fan.gameObject.layer };
                bindings.Add(candidate);
            }
            candidate.Drive = drive; candidate.Input = input == null ? null : input.gameObject;
            candidate.State = rangeState; candidate.DriveState = driveState;
            candidate.Discovered = true;
        }

        private static bool Selected(Binding candidate)
        {
            return candidate.Kind == SelectorKind.Range ? candidate.State.Tune.LowRangeSelected : candidate.DriveState.FourWheelSelected;
        }

        private static bool Pick(out RaycastHit hit)
        {
            hit = new RaycastHit();
            FsmGameObject cameraVariable = FsmVariables.GlobalVariables.GetFsmGameObject("PlayerCamera");
            GameObject owner = cameraVariable == null ? null : cameraVariable.Value;
            Camera camera = owner == null ? null : owner.GetComponent<Camera>();
            if (camera == null || !camera.isActiveAndEnabled) return false;
            hit = ActionHelpers.MousePick(Reach, PickMask);
            return hit.collider != null;
        }

        internal static bool TryUse()
        {
            ExpireMouseClaim();
            if (consumedFrame == Time.frameCount) return true;
            if (!GameplayReady()) return false;
            try
            {
                if (!Input.GetMouseButtonDown(0)) return false;
                Binding candidate = Sync();
                FsmGameObject playerVariable = FsmVariables.GlobalVariables.GetFsmGameObject("Player");
                GameObject player = playerVariable == null ? null : playerVariable.Value;
                if (candidate == null || !BindingEligible(candidate, player, player == null ? null : player.GetComponentInParent<VehicleController>())) return false;
                consumedFrame = Time.frameCount; suppressMouse = true;
                bool previous = Selected(candidate);
                string error;
                bool accepted = candidate.Kind == SelectorKind.Range ? candidate.State.TrySetRange(!previous, out error) :
                    candidate.DriveState.TrySetFourWheel(!previous, out error);
                if (!accepted)
                {
                    candidate.Error = error ?? "Stop and select neutral before changing the selector.";
                    candidate.ErrorUntil = Time.unscaledTime + 3f;
                    candidate.ErrorLabel = (candidate.Kind == SelectorKind.Range ? "High / Low: " : "2WD / 4WD: ") + candidate.Error;
                }
                else
                {
                    candidate.Error = candidate.ErrorLabel = null;
                    candidate.ErrorUntil = 0f;
                    bool selected = Selected(candidate);
                    ApplyPose(candidate);
                    if (previous != selected) DashboardSwitchAudio.Play(candidate.Fan);
                }
                return true;
            }
            catch (Exception error) { Warn(error); return false; }
        }

        internal static bool BlockMouseAction(string actionName, InputPhase phase)
        {
            ExpireMouseClaim();
            if (!GameplayReady()) { suppressMouse = false; return false; }
            if (consumedFrame != Time.frameCount && !suppressMouse && !Input.GetMouseButtonDown(0)) return false;
            try
            {
                KeyAction action = InputController.GetKeyAction(actionName);
                if (action == null || (action.Key != KeyCode.Mouse0 && action.AlternativeKey != KeyCode.Mouse0)) return false;
                // A combined binding can still carry a genuine keyboard or
                // other-button input while we own only its Mouse0 portion.
                if (OtherInput(action.Key, phase) || OtherInput(action.AlternativeKey, phase)) return false;
                if (consumedFrame == Time.frameCount) return true;
                if (suppressMouse)
                {
                    if (Input.GetMouseButton(0) || Input.GetMouseButtonUp(0)) return true;
                    suppressMouse = false;
                }
                return TryUse();
            }
            catch (Exception error) { Warn(error); return false; }
        }

        private static bool OtherInput(KeyCode key, InputPhase phase)
        {
            if (key == KeyCode.None || key == KeyCode.Mouse0) return false;
            if (phase == InputPhase.Held) return Input.GetKey(key);
            if (phase == InputPhase.Up) return Input.GetKeyUp(key);
            return Input.GetKeyDown(key);
        }

        private static void ExpireMouseClaim()
        {
            if (consumedFrame == Time.frameCount) return;
            // A claim belongs to one press, including that press's release.
            // Expire it even if no native action queried input during release,
            // and never carry it into the next physical click.
            if (Input.GetMouseButtonDown(0) || (!Input.GetMouseButton(0) && !Input.GetMouseButtonUp(0)))
                suppressMouse = false;
        }

        internal static void Tick()
        {
            try { ExpireMouseClaim(); Sync(); TryUse(); }
            catch (Exception error) { DeactivateAll(); Warn(error); }
        }

        // Let the game's normal ItemName actions finish first, then supply the
        // owned blank switch's one label in the existing ItemName field.
        internal static void LateTick()
        {
            try
            {
                Binding candidate = Sync();
                if (candidate == null) { ClearPrompt(); return; }
                ResolveHud();
                if (itemName == null) return;
                bool selected = Selected(candidate);
                float reduction = candidate.Kind == SelectorKind.Range ? candidate.State.Tune.LowRangeRatio : 0f;
                if (candidate.NameText == null || candidate.ShownSelection != selected || candidate.ShownReduction != reduction)
                {
                    candidate.ShownSelection = selected; candidate.ShownReduction = reduction;
                    candidate.NameText = candidate.Kind == SelectorKind.Range ?
                        "High / Low: " + (selected ? "LOW " + reduction.ToString("0.##", CultureInfo.InvariantCulture) + "x" : "HIGH") + " (Left click)" :
                        "2WD / 4WD: " + (selected ? "4WD" : "2WD") + " (Left click)";
                }
                string label = candidate.NameText;
                if (candidate.Error != null && Time.unscaledTime < candidate.ErrorUntil)
                {
                    label = candidate.ErrorLabel;
                }
                else candidate.Error = candidate.ErrorLabel = null;
                itemName.text = ownedName = label;
            }
            catch (Exception error) { ClearPrompt(); Warn(error); }
        }

        private static void ResolveHud()
        {
            FsmGameObject variable = FsmVariables.GlobalVariables.GetFsmGameObject("UI_ItemName");
            GameObject owner = variable == null ? null : variable.Value;
            if (owner != itemNameOwner || itemName == null)
            {
                ClearPrompt(); itemNameOwner = owner;
                itemName = owner == null ? null : owner.GetComponent<Text>();
            }
        }

        private static void ClearPrompt()
        {
            if (itemName != null && ownedName != null && itemName.text == ownedName) itemName.text = String.Empty;
            ownedName = null;
        }

        private static void DeactivateAll()
        {
            for (int i = 0; i < bindings.Count; i++) bindings[i].Deactivate();
            active = null;
            ClearPrompt();
        }

        internal static void Reset()
        {
            DeactivateAll();
            foreach (Binding binding in bindings)
            {
                if (binding.Collider != null) UnityEngine.Object.Destroy(binding.Collider);
            }
            bindings.Clear(); itemName = null; itemNameOwner = null;
            consumedFrame = -1; suppressMouse = false;
            syncedFrame = -1; nextDiscovery = 0f; discoveredEngines = discoveredControls = -1;
        }

        private static void Warn(Exception error)
        {
            if (Time.unscaledTime < nextWarning) return;
            nextWarning = Time.unscaledTime + 5f;
            if (Plugin.Log != null) Plugin.Log.LogWarning("Dashboard selector skipped: " + error.GetType().Name + ": " + error.Message);
        }
    }
}
