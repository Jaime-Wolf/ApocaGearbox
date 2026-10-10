using System;
using System.Reflection;
using HutongGames.PlayMaker;
using NWH.VehiclePhysics2;
using UnityEngine;

namespace ApocaGearbox
{
    /// <summary>Uses the native gearbox selection collider and the existing adjustment tool.</summary>
    public static class GearboxTarget
    {
        public const float Reach = 2f;
        public const int NativePickMask = 66305;

        private static GameObject cameraOwner;
        private static Camera playerCamera;
        private static PlayMakerFSM adjustTool;
        private static PlayMakerFSM drive;
        private static PlayMakerFSM grab;
        private static float nextRefresh;
        private static FieldInfo otherSession;
        private static bool checkedOtherSession;
        private static Collider aimedCollider;
        private static GameObject aimedEngine;
        private static Transform aimedParent, aimedEngineParent;
        private static PlayMakerFSM aimedId, aimedCheck;
        private static PlayMakerFSM aimedRatio;
        private static bool aimedTransmission;
        private static FsmString aimedIdentity;
        private static float nextAimRefresh;

        private static void Refresh()
        {
            FsmGameObject variable = FsmVariables.GlobalVariables.GetFsmGameObject("PlayerCamera");
            GameObject owner = variable == null ? null : variable.Value;
            if (owner == cameraOwner && Time.unscaledTime < nextRefresh)
                return;

            if (owner != cameraOwner) aimedCollider = null;
            cameraOwner = owner;
            // Components can be substituted on a surviving camera object.
            // Reconcile at most four times per second, including missing bindings.
            nextRefresh = Time.unscaledTime + 0.25f;
            playerCamera = null;
            adjustTool = drive = grab = null;
            if (owner == null) return;
            foreach (Camera camera in owner.GetComponents<Camera>())
                if (camera != null && (playerCamera == null || camera.isActiveAndEnabled)) playerCamera = camera;
            foreach (PlayMakerFSM fsm in owner.GetComponents<PlayMakerFSM>())
            {
                if (fsm == null) continue;
                // Prefer the enabled replacement if the old component survives.
                if (fsm.FsmName == "UseAdjustTool" && (adjustTool == null || fsm.isActiveAndEnabled)) adjustTool = fsm;
                if (fsm.FsmName == "DriveUse" && (drive == null || fsm.isActiveAndEnabled)) drive = fsm;
                if (fsm.FsmName == "GrabItem" && (grab == null || fsm.isActiveAndEnabled)) grab = fsm;
            }
        }

        public static PlayMakerFSM FindFsm(GameObject owner, string name)
        {
            if (owner == null) return null;
            PlayMakerFSM[] fsms = owner.GetComponents<PlayMakerFSM>();
            for (int i = 0; i < fsms.Length; i++)
                if (fsms[i] != null && fsms[i].FsmName == name) return fsms[i];
            return null;
        }

        public static bool ToolSelected()
        {
            Refresh();
            return adjustTool != null && adjustTool.isActiveAndEnabled;
        }

        public static bool NativeAdjustmentActive()
        {
            Refresh();
            return adjustTool != null && adjustTool.isActiveAndEnabled && adjustTool.ActiveStateName == "adjust";
        }

        public static bool SafeToInteract()
        {
            Refresh();
            if (Time.timeScale <= 0f || !Application.isFocused || Cursor.lockState != CursorLockMode.Locked ||
                playerCamera == null || !playerCamera.isActiveAndEnabled || !ToolSelected() || NativeAdjustmentActive())
                return false;

            if (drive != null && drive.isActiveAndEnabled)
            {
                string state = drive.ActiveStateName;
                if (state != "Idle" && state != "over" && state != "checkHand") return false;
            }
            if (grab != null && grab.isActiveAndEnabled)
            {
                string state = grab.ActiveStateName;
                if (state == "ItemInHand" || state == "Rotate" || state == "Forward" || state == "Backward")
                    return false;
            }

            // The optional Part Adjuster Tools plugin owns E while an attached-item session is active.
            // Do not borrow that key until its session has ended.
            if (!checkedOtherSession)
            {
                Type type = Type.GetType("PartAdjustment.AdjustmentRunner, PartAdjustment", false);
                if (type != null)
                    otherSession = type.GetField("Session", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                checkedOtherSession = true;
            }
            if (otherSession != null && otherSession.GetValue(null) != null) return false;
            return true;
        }

        public static bool IsEngine(GameObject owner)
        {
            if (owner == null) return false;
            PlayMakerFSM id = FindFsm(owner, "ID");
            if (id == null) return false;
            FsmString value = id.FsmVariables.GetFsmString("ID");
            return value != null && value.Value == "engine";
        }

        public static VehicleController InstalledVehicle(GameObject engine)
        {
            if (!IsEngine(engine) || !engine.CompareTag("vehPart")) return null;
            Transform parent = engine.transform.parent;
            // Native installed engines are direct children of their engine mount.
            // A locked cargo engine must not alter the cargo vehicle's transmission.
            if (parent == null || parent.name != "hinge_engine") return null;
            VehicleController vehicle = parent.GetComponentInParent<VehicleController>();
            if (vehicle == null || !vehicle.gameObject.activeInHierarchy) return null;
            PlayMakerFSM check = FindFsm(engine, "CheckTag");
            if (check == null || !check.isActiveAndEnabled) return null;
            FsmGameObject linked = check.FsmVariables.GetFsmGameObject("vehicle");
            if (linked == null || linked.Value != vehicle.gameObject) return null;
            return vehicle;
        }

        public static bool TryAim(out GameObject engine, out VehicleController vehicle, out string label)
        {
            engine = null;
            vehicle = null;
            label = null;
            GameObject selected;
            if (!TryAimEngine(out selected)) return false;
            engine = selected;
            vehicle = InstalledVehicle(selected);
            label = engine.name.Replace("(Clone)", "").Trim();
            return true;
        }

        // The prompt only needs a valid aimed engine, avoiding vehicle lookup
        // and display-name allocation on every LateUpdate.
        public static bool HasAim()
        { GameObject selected; return TryAimEngine(out selected); }

        private static bool TryAimEngine(out GameObject selected)
        {
            selected = null;
            if (!SafeToInteract()) return false;
            RaycastHit hit = ActionHelpers.MousePick(Reach, NativePickMask);
            if (hit.collider == null || hit.distance > Reach) { aimedCollider = null; return false; }
            GameObject target = hit.collider.gameObject;
            if (hit.collider == aimedCollider && target.transform.parent == aimedParent &&
                Time.unscaledTime < nextAimRefresh && aimedEngine != null && aimedEngine.activeInHierarchy &&
                aimedEngine.transform.parent == aimedEngineParent && target.transform.IsChildOf(aimedEngine.transform) && aimedId != null && aimedId.FsmName == "ID" &&
                aimedIdentity != null && aimedIdentity.Value == "engine" && aimedCheck != null && aimedCheck.FsmName == "CheckTag" &&
                (!aimedTransmission || aimedRatio != null && aimedRatio.FsmName == "FinalDriveRatio"))
            { selected = aimedEngine; return true; }
            aimedCollider = null;
            aimedRatio = null; aimedTransmission = false;
            if (target.name == "transmission")
            {
                PlayMakerFSM ratio = FindFsm(target, "FinalDriveRatio");
                if (ratio == null || target.transform.parent == null || !IsEngine(target.transform.parent.gameObject)) return false;
                selected = target.transform.parent.gameObject;
                aimedRatio = ratio; aimedTransmission = true;
            }
            else
            {
                // The engine body is an alternate access point when a mounted
                // gearbox is hidden. Stop at the owning engine item; chassis
                // parts and other loose items cannot select a nearby engine.
                for (Transform part = target.transform; part != null; part = part.parent)
                    if (IsEngine(part.gameObject)) { selected = part.gameObject; break; }
                if (selected == null || FindFsm(selected, "CheckTag") == null) return false;
            }
            if (selected == null || !selected.activeInHierarchy) return false;
            aimedId = FindFsm(selected, "ID");
            aimedCheck = FindFsm(selected, "CheckTag");
            aimedIdentity = aimedId != null && aimedId.FsmVariables != null ? aimedId.FsmVariables.GetFsmString("ID") : null;
            aimedCollider = hit.collider; aimedEngine = selected;
            aimedParent = target.transform.parent; aimedEngineParent = selected.transform.parent;
            nextAimRefresh = Time.unscaledTime + 0.25f;
            return true;
        }

        // Logged on an unsuccessful E press, never drawn over the game's UI.
        public static string DescribeSelection()
        {
            Refresh();
            string result = "camera=" + (cameraOwner == null ? "none" : cameraOwner.name) +
                "; tool=" + (adjustTool == null ? "missing" : adjustTool.isActiveAndEnabled + "/" + adjustTool.ActiveStateName) +
                "; drive=" + (drive == null ? "missing" : drive.ActiveStateName) +
                "; grab=" + (grab == null ? "missing" : grab.ActiveStateName) +
                "; focused=" + Application.isFocused + "; cursor=" + Cursor.lockState + "; timeScale=" + Time.timeScale;
            if (playerCamera == null || !playerCamera.isActiveAndEnabled) return result + "; activeCamera=false";
            RaycastHit hit = ActionHelpers.MousePick(Reach, NativePickMask);
            if (hit.collider == null) return result + "; target=none";
            GameObject target = hit.collider.gameObject;
            result += "; target=" + target.name + "; distance=" + hit.distance;
            PlayMakerFSM ratio = FindFsm(target, "FinalDriveRatio");
            result += "; ratioFSM=" + (ratio == null ? "missing" : ratio.isActiveAndEnabled + "/" + ratio.ActiveStateName);
            Transform parent = target.transform.parent;
            if (parent != null)
            {
                PlayMakerFSM check = FindFsm(parent.gameObject, "CheckTag");
                result += "; parent=" + parent.name + "; engine=" + IsEngine(parent.gameObject) +
                    "; check=" + (check == null ? "missing" : check.ActiveStateName) +
                    "; installed=" + (InstalledVehicle(parent.gameObject) != null);
            }
            return result;
        }
    }
}
