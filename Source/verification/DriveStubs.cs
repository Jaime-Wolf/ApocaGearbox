using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Component
    {
        public GameObject gameObject;
        public T GetComponent<T>() where T : Component { return gameObject.GetComponent<T>(); }
    }
    public class MonoBehaviour : Component { }
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DefaultExecutionOrder : Attribute { public DefaultExecutionOrder(int value) { } }
    public class GameObject
    {
        private readonly List<Component> components = new List<Component>();
        public bool activeInHierarchy = true;
        public T AddComponent<T>() where T : Component, new()
        { T result = new T(); result.gameObject = this; components.Add(result); return result; }
        public T GetComponent<T>() where T : Component
        { foreach (Component value in components) if (value is T) return (T)value; return null; }
    }
    public static class Time { public static float unscaledTime = 10f, fixedTime = 1f; }
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string name) { } }
}
namespace NWH.Common.Vehicles
{
    public sealed class WheelUAPI
    {
        public bool AutoSimulate = true;
        public float Inertia;
        public float MotorTorque;
        public float AngularVelocity;
        public float CounterTorque;
        public float BrakeTorque;
    }
}
namespace NWH.VehiclePhysics2
{
    public class ManagerVehicleComponent { }
    public sealed class VehicleController : UnityEngine.Component
    {
        public Powertrain.Powertrain powertrain = new Powertrain.Powertrain();
        public float VelocityMagnitude;
    }
}
namespace NWH.VehiclePhysics2.Powertrain
{
    public sealed class Powertrain : NWH.VehiclePhysics2.ManagerVehicleComponent
    {
        public bool IsActive = true;
        public TransmissionComponent transmission = new TransmissionComponent();
        public EngineComponent engine = new EngineComponent();
        public List<WheelComponent> wheels = new List<WheelComponent>();
    }
    public sealed class EngineComponent { }
    public class PowertrainComponent
    {
        public string name;
        public float inertia;
        public float inputTorque, inputInertia, inputAngularVelocity;
        public float outputTorque, outputInertia, outputAngularVelocity;
        public int inputNameHash, outputNameHash = 101;
        public PowertrainComponent Output;
        public virtual float QueryAngularVelocity(float input, float dt) { return input; }
        public virtual float QueryInertia() { return inertia; }
        public virtual float ForwardStep(float torque, float upstreamInertia, float dt) { return torque; }
    }
    public sealed class TransmissionComponent : PowertrainComponent
    {
        public enum TransmissionShiftType { Manual, Automatic, CVT }
        public TransmissionShiftType transmissionType;
        public int Gear;
        public bool isShifting;
    }
    public sealed class DifferentialComponent : PowertrainComponent
    {
        public enum Type { Open, Locked }
        public delegate void SplitTorque(float torque, float angularA, float angularB, float inertiaA,
            float inertiaB, float dt, float bias, float stiffness, float power, float coast, float slip,
            out float torqueA, out float torqueB);
        public PowertrainComponent OutputB;
        public int outputBNameHash = 102;
        public float biasAB = 0.5f, stiffness = 0.5f, powerRamp = 1f, coastRamp = 1f, slipTorque = 400f;
        public SplitTorque splitTorqueDelegate;
        private Type type;
        public Type DifferentialType
        {
            get { return type; }
            set { type = value; splitTorqueDelegate = DefaultSplit; }
        }
        private static void DefaultSplit(float torque, float angularA, float angularB, float inertiaA,
            float inertiaB, float dt, float bias, float stiffness, float power, float coast, float slip,
            out float torqueA, out float torqueB) { torqueA = torque * 0.5f; torqueB = torque * 0.5f; }
        public override float QueryAngularVelocity(float input, float dt)
        { return (Output.QueryAngularVelocity(input, dt) + OutputB.QueryAngularVelocity(input, dt)) * 0.5f; }
        public override float QueryInertia() { return inertia + Output.QueryInertia() + OutputB.QueryInertia(); }
    }
    public sealed class WheelComponent : PowertrainComponent
    {
        public NWH.Common.Vehicles.WheelUAPI wheelUAPI;
        public float InitialInertia;
        public int Steps;
        public float LastTorque, LastUpstreamInertia, LastDelta;
        public bool ThrowAngular, ThrowInertia, ThrowStep;
        public override float QueryAngularVelocity(float input, float dt)
        { if (ThrowAngular) throw new InvalidOperationException("native angular API unavailable"); return wheelUAPI.AngularVelocity; }
        public override float QueryInertia()
        { if (ThrowInertia) throw new InvalidOperationException("native inertia API unavailable"); return InitialInertia; }
        // This records the native WheelComponent contract, independently read
        // from the installed DLL: write torque/inertia, disable auto simulation,
        // step once, then return the tire's counter torque.
        public override float ForwardStep(float torque, float upstreamInertia, float dt)
        {
            Steps++; LastTorque = torque; LastUpstreamInertia = upstreamInertia; LastDelta = dt;
            wheelUAPI.MotorTorque = torque; wheelUAPI.Inertia = InitialInertia + upstreamInertia;
            wheelUAPI.AutoSimulate = false;
            if (ThrowStep) throw new InvalidOperationException("native wheel step unavailable after motor outputs");
            return wheelUAPI.CounterTorque;
        }
    }
}
namespace ApocaGearbox
{
    internal sealed class TestFlag { internal bool Value = true; }
    internal sealed class TestLog { internal void LogWarning(object value) { } }
    internal static class Plugin
    { internal static TestFlag Enabled = new TestFlag(); internal static TestLog Log = new TestLog(); }
}
