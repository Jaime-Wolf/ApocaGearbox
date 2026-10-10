// Identity and input substitutes for exercising the real dashboard selector.
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        public static void Destroy(Object value)
        {
            Component component = value as Component;
            if (component != null) component.gameObject.Components.Remove(component);
        }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
        public string name { get { return gameObject.name; } }
        public bool isActiveAndEnabled = true;
        public T GetComponent<T>() where T : Component { return gameObject.GetComponent<T>(); }
        public T GetComponentInParent<T>() where T : Component { return gameObject.GetComponentInParent<T>(); }
    }
    public class Transform : Component
    {
        private Transform owner;
        public readonly List<Transform> Children = new List<Transform>();
        public static int FindCalls, PoseWrites;
        private Quaternion rotation;
        public Quaternion localRotation { get { return rotation; } set { rotation = value; PoseWrites++; } }
        public Transform parent
        {
            get { return owner; }
            set { if (owner != null) owner.Children.Remove(this); owner = value; if (owner != null) owner.Children.Add(this); }
        }
        public Transform Find(string path)
        {
            FindCalls++;
            int slash = path.IndexOf('/'); string first = slash < 0 ? path : path.Substring(0, slash);
            foreach (Transform child in Children)
                if (child.name == first) return slash < 0 ? child : child.Find(path.Substring(slash + 1));
            return null;
        }
    }
    public class GameObject : Object
    {
        public string name;
        public bool activeInHierarchy = true;
        public int layer;
        public Transform transform;
        public readonly List<Component> Components = new List<Component>();
        public GameObject(string value) { name = value; transform = new Transform { gameObject = this }; }
        public T AddComponent<T>() where T : Component, new() { T value = new T { gameObject = this }; Components.Add(value); return value; }
        public T GetComponent<T>() where T : Component
        { foreach (Component component in Components) if (component is T) return (T)component; return null; }
        public T GetComponentInParent<T>() where T : Component
        { T value = GetComponent<T>(); return value != null ? value : transform.parent == null ? null : transform.parent.gameObject.GetComponentInParent<T>(); }
    }
    public struct Quaternion
    {
        public float X;
        public static Quaternion Euler(float x, float y, float z) { return new Quaternion { X = x }; }
    }
    public struct Vector3 { public float x, y, z; }
    public struct Bounds { public Vector3 center, size; }
    public sealed class Mesh { public Bounds bounds; }
    public sealed class MeshFilter : Component { public Mesh sharedMesh = new Mesh { bounds = new Bounds { size = new Vector3 { x = .01f, y = .03f, z = .02f } } }; }
    public class Collider : Component { public bool enabled = true; }
    public sealed class BoxCollider : Collider { public HideFlags hideFlags; public Vector3 center, size; public bool isTrigger; }
    public sealed class Camera : Component { }
    public struct RaycastHit { public Collider collider; public float distance; }
    public enum HideFlags { HideAndDontSave }
    public enum CursorLockMode { None, Locked, Confined }
    public static class Cursor { public static CursorLockMode lockState = CursorLockMode.Locked; }
    public static class Application { public static bool isFocused = true; }
    public static class Mathf { public static float Clamp(float x, float min, float max) { return Math.Max(min, Math.Min(max, x)); } }
    public static class Time { public static int frameCount; public static float unscaledTime, timeScale = 1f; }
    public enum KeyCode { None, Mouse0, Mouse1, E }
    public static class Input
    {
        public static bool MouseDown, MouseHeld, MouseUp, OtherDown, OtherHeld, OtherUp;
        public static bool GetMouseButtonDown(int value) { return MouseDown; }
        public static bool GetMouseButton(int value) { return MouseHeld; }
        public static bool GetMouseButtonUp(int value) { return MouseUp; }
        public static bool GetKeyDown(KeyCode key) { return OtherDown; }
        public static bool GetKey(KeyCode key) { return OtherHeld; }
        public static bool GetKeyUp(KeyCode key) { return OtherUp; }
    }
}
namespace UnityEngine.UI { public sealed class Text : UnityEngine.Component { public string text = String.Empty; } }
namespace HutongGames.PlayMaker
{
    public sealed class FsmGameObject { public UnityEngine.GameObject Value; }
    public sealed class FsmVariables
    {
        public static FsmVariables GlobalVariables = new FsmVariables();
        public readonly Dictionary<string,FsmGameObject> Objects = new Dictionary<string,FsmGameObject>();
        public FsmGameObject GetFsmGameObject(string name) { FsmGameObject value; return Objects.TryGetValue(name, out value) ? value : null; }
    }
    public static class ActionHelpers
    {
        public static int Picks;
        public static Func<UnityEngine.RaycastHit> Pick;
        public static UnityEngine.RaycastHit MousePick(float reach, int mask) { Picks++; return Pick == null ? new UnityEngine.RaycastHit() : Pick(); }
    }
}
public sealed class PlayMakerFSM : UnityEngine.Component { public string FsmName = "Drive", ActiveStateName = "inCar"; }
namespace InsaneSystems.InputManager
{
    public sealed class KeyAction { public UnityEngine.KeyCode Key, AlternativeKey; }
    public static class InputController
    {
        public static readonly Dictionary<string,KeyAction> Actions = new Dictionary<string,KeyAction>();
        public static KeyAction GetKeyAction(string name) { KeyAction value; return Actions.TryGetValue(name, out value) ? value : null; }
    }
}
namespace NWH.VehiclePhysics2
{
    public sealed class Powertrain { public Transmission transmission = new Transmission(); }
    public sealed class Transmission { }
    public sealed class VehicleController : UnityEngine.Component { public Powertrain powertrain = new Powertrain(); }
}
namespace Apocasetter
{
    public static class GameMenu { public static bool InGame = true, Paused; }
    public static class InputBlocker { public static bool Active; }
}
namespace ApocaGearbox
{
    public sealed class Flag { public bool Value = true; }
    public sealed class Log { public void LogWarning(object value) { } }
    public static class Plugin { public static Flag Enabled = new Flag(); public static Log Log = new Log(); }
    public static class TuningPanel { public static bool IsOpen; }
    public sealed class TuneData
    {
        public bool LowRangeEnabled = true, LowRangeSelected;
        public float LowRangeRatio = 2f;
        public static bool Finite(float value) { return !Single.IsNaN(value) && !Single.IsInfinity(value); }
    }
    public sealed class GearboxState
    {
        public static readonly List<GearboxState> TunedEngines = new List<GearboxState>();
        public NWH.VehiclePhysics2.VehicleController Vehicle;
        public TuneData Tune = new TuneData();
        public static GearboxState ForTransmission(NWH.VehiclePhysics2.Transmission transmission)
        { foreach (GearboxState state in TunedEngines) if (state.Vehicle.powertrain.transmission == transmission) return state; return null; }
        public bool Reject;
        public bool TrySetRange(bool selected, out string error)
        { error = Reject ? "Select neutral before changing range." : null; if (!Reject) Tune.LowRangeSelected = selected; return !Reject; }
    }
    public sealed class DriveControl
    {
        public static readonly List<DriveControl> Controls = new List<DriveControl>();
        public NWH.VehiclePhysics2.VehicleController Vehicle;
        public bool Enabled = true, IsReady = true, FourWheelSelected = true;
        public static bool TryGet(NWH.VehiclePhysics2.VehicleController vehicle, out DriveControl result)
        { result = Controls.Find(delegate(DriveControl value) { return value.Vehicle == vehicle; }); return result != null; }
        public bool TrySetFourWheel(bool selected, out string error) { error = null; FourWheelSelected = selected; return true; }
    }
    public static class GearboxTarget
    {
        public static PlayMakerFSM FindFsm(UnityEngine.GameObject owner, string name)
        { foreach (UnityEngine.Component component in owner.Components) { PlayMakerFSM fsm = component as PlayMakerFSM; if (fsm != null && fsm.FsmName == name) return fsm; } return null; }
    }
    public static class DashboardSwitchAudio { public static int Plays; public static void Play(UnityEngine.GameObject value) { Plays++; } }
}
