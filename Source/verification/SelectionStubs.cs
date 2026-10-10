// Minimal substitutes for exercising the real GearboxTarget.cs outside Unity.
// They model identity, component lookup, hierarchy and native FSM state only.
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Component
    {
        public GameObject gameObject;
        public string name { get { return gameObject.name; } }
        public bool isActiveAndEnabled = true;
        public T GetComponentInParent<T>() where T : Component { return gameObject.GetComponentInParent<T>(); }
        public T[] GetComponentsInChildren<T>(bool inactive) where T : Component { return gameObject.GetComponentsInChildren<T>(inactive); }
    }
    public class MonoBehaviour : Component { }
    public class Transform : Component
    {
        public Transform parent;
        public new string name { get { return gameObject.name; } }
        public bool IsChildOf(Transform ancestor)
        { for (Transform current = this; current != null; current = current.parent) if (current == ancestor) return true; return false; }
    }
    public class GameObject
    {
        private static readonly List<GameObject> all = new List<GameObject>();
        private readonly List<Component> components = new List<Component>();
        public string name;
        public string tag = "Untagged";
        public bool activeInHierarchy = true;
        public Transform transform;
        public GameObject(string value) { name = value; transform = new Transform(); transform.gameObject = this; all.Add(this); }
        public T AddComponent<T>() where T : Component, new() { T value = new T(); value.gameObject = this; components.Add(value); return value; }
        public bool CompareTag(string value) { return tag == value; }
        public T GetComponent<T>() where T : Component
        {
            foreach (Component component in components) if (component is T) return (T)component;
            return null;
        }
        public T[] GetComponents<T>() where T : Component
        {
            ComponentArrayReads++;
            List<T> found = new List<T>();
            foreach (Component component in components) if (component is T) found.Add((T)component);
            return found.ToArray();
        }
        public int ComponentArrayReads;
        public T GetComponentInParent<T>() where T : Component
        {
            T value = GetComponent<T>();
            return value != null ? value : (transform.parent == null ? null : transform.parent.gameObject.GetComponentInParent<T>());
        }
        public T[] GetComponentsInChildren<T>(bool inactive) where T : Component
        {
            List<T> found = new List<T>();
            foreach(GameObject go in all) {
                bool child=false;for(Transform p=go.transform;p!=null;p=p.parent)if(p.gameObject==this){child=true;break;}
                if(!child||(!inactive&&!go.activeInHierarchy))continue;
                if(go.transform is T)found.Add((T)(Component)go.transform);
                foreach(Component component in go.components)if(component is T)found.Add((T)component);
            }
            return found.ToArray();
        }
    }
    public class Camera : Component { }
    public class Collider : Component { }
    public struct RaycastHit { public Collider collider; public float distance; }
    public enum CursorLockMode { None, Locked, Confined }
    public static class Cursor { public static CursorLockMode lockState = CursorLockMode.Locked; }
    public static class Application { public static bool isFocused = true; }
    public static class Time { public static float timeScale = 1f; public static float unscaledTime = 100f; }
}

namespace HutongGames.PlayMaker
{
    public class FsmString { public string Value; }
    public class FsmGameObject { public UnityEngine.GameObject Value; }
    public class FsmVariables
    {
        public static readonly FsmVariables GlobalVariables = new FsmVariables();
        public readonly Dictionary<string, FsmString> Strings = new Dictionary<string, FsmString>();
        public readonly Dictionary<string, FsmGameObject> Objects = new Dictionary<string, FsmGameObject>();
        public FsmString GetFsmString(string key) { FsmString value; return Strings.TryGetValue(key, out value) ? value : null; }
        public FsmGameObject GetFsmGameObject(string key) { FsmGameObject value; return Objects.TryGetValue(key, out value) ? value : null; }
    }
    public class PlayMakerFSM : UnityEngine.Component
    {
        public string FsmName;
        public string ActiveStateName = "Idle";
        public readonly FsmVariables FsmVariables = new FsmVariables();
    }
    public static class ActionHelpers
    {
        public static UnityEngine.RaycastHit PickResult;
        public static float RequestedReach;
        public static int RequestedMask;
        public static UnityEngine.RaycastHit MousePick(float reach, int mask) { RequestedReach = reach; RequestedMask = mask; return PickResult; }
    }
}

namespace NWH.VehiclePhysics2
{
    public class VehicleController : UnityEngine.Component
    {
        public NWH.VehiclePhysics2.Powertrain.Powertrain powertrain = new NWH.VehiclePhysics2.Powertrain.Powertrain();
        public readonly NWH.VehiclePhysics2.Input.VehicleInputHandler input = new NWH.VehiclePhysics2.Input.VehicleInputHandler();
        public float TestVelocityMagnitude;
        public bool ThrowVelocityRead;
        public float VelocityMagnitude { get { if (ThrowVelocityRead) throw new InvalidOperationException("Unavailable chassis velocity"); return TestVelocityMagnitude; } }
    }
}

namespace NWH.VehiclePhysics2.Powertrain
{
    public class Powertrain
    {
        public TransmissionComponent transmission = new TransmissionComponent();
        public EngineComponent engine = new EngineComponent();
        public ClutchComponent clutch = new ClutchComponent();
    }
    public class PowertrainComponent
    {
        public int outputNameHash = 1;
        public PowertrainComponent Output;
    }
    public class EngineComponent
    {
        public enum EngineType { ICE, Electric }
        public EngineType engineType = EngineType.ICE;
        public float revLimiterRPM = 5000f;
        public float idleRPM = 800f;
        public float OutputRPM = 3000f;
        public bool ignition = true;
        public bool IsRunning = true;
        public object powerCurve = new object();
        public int PeakRefreshes;
        public void UpdatePeakPowerAndTorque() { PeakRefreshes++; }
    }
    public class ClutchComponent
    {
        public enum ClutchControlType { Automatic, Manual, ManualWithAutoClutch }
        public ClutchControlType controlType;
        public float Engagement = 1f;
    }
    public class DifferentialComponent : PowertrainComponent
    {
        public int outputBNameHash = 1;
        public PowertrainComponent OutputB;
        public float biasAB = 0.5f;
    }
    public class WheelComponent : PowertrainComponent { public TestWheelSensor wheelUAPI; }
    public class TestWheelSensor
    {
        public float Value;
        public bool Throw;
        public float AngularVelocity { get { if (Throw) throw new InvalidOperationException("Missing native sensor"); return Value; } }
    }
    public class TransmissionComponent : PowertrainComponent
    {
        public enum TransmissionShiftType { Manual, Automatic, CVT }
        public TransmissionShiftType transmissionType = TransmissionShiftType.Manual;
        public List<float> gears = new List<float>();
        public float finalGearRatio;
        public float UpshiftRPM;
        public float DownshiftRPM;
        public int Gear = 2;
        public bool isShifting;
        public int reverseGearCount = 1;
        public float GetGearRatio(int gear) { return gears[gear + reverseGearCount] * finalGearRatio; }
    }
}

namespace NWH.VehiclePhysics2.Input
{
    public class VehicleInputHandler
    {
        public float InputSwappedThrottle;
        public float Brakes;
        public float Clutch;
    }
}
