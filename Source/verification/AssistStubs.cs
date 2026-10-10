using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public sealed class GameObject { public bool activeInHierarchy = true; }
    public static class Time { public static float unscaledTime, fixedTime; public static float fixedDeltaTime = 0.02f, timeScale = 1f; }
    public static class Application { public static bool isFocused = true; }
    public static class Mathf
    {
        public const float PI = (float)Math.PI;
        public static float Max(float a, float b) { return Math.Max(a, b); }
    }
}

namespace NWH.VehiclePhysics2.Powertrain
{
    public sealed class EngineComponent
    {
        public delegate float PowerModifier();
        public sealed class ForcedInduction
        {
            public bool useForcedInduction;
            public float powerGainMultiplier = 1f;
            public float boost;
            public float PowerGainMultiplier
            { get { return useForcedInduction ? 1f + (powerGainMultiplier - 1f) * boost : 1f; } }
        }
        public ForcedInduction forcedInduction = new ForcedInduction();
        public object powerCurve = new object();
        public float UnboostedPeakTorque;
        public int PeakReads;
        public float maxPower = 100f;
        public float idleRPM = 700f;
        public float EstimatedPeakTorque
        { get { PeakReads++; return UnboostedPeakTorque * (forcedInduction.useForcedInduction ? forcedInduction.powerGainMultiplier : 1f); } }
        public float revLimiterRPM = 4500f;
        public float OutputRPM;
        public float ThrottlePosition = 1f;
        public List<PowerModifier> powerModifiers = new List<PowerModifier>();
    }
    public sealed class ClutchComponent { public float slipTorque; public float Engagement = 1f; }
    public class PowertrainComponent { public PowertrainComponent Output; }
    public sealed class DifferentialComponent : PowertrainComponent { public PowertrainComponent OutputB; public float biasAB = 0.5f; }
    public sealed class WheelComponent : PowertrainComponent { public TestWheel wheelUAPI = new TestWheel(); }
    public sealed class TestWheel { public bool IsGrounded = true; public float Radius = 0.5f, AngularVelocity, LongitudinalSpeed; }
    public sealed class TransmissionComponent : PowertrainComponent
    {
        public List<float> gears = new List<float>();
        public int gearIndex;
        public float finalGearRatio;
        public bool isShifting;
        public float Gear { get { return gearIndex >= 0 && gearIndex < gears.Count ? gears[gearIndex] : 0f; } }
    }
    public sealed class Powertrain
    {
        public EngineComponent engine;
        public ClutchComponent clutch;
        public TransmissionComponent transmission;
    }
}

namespace NWH.VehiclePhysics2
{
    public sealed class VehicleController
    {
        public UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
        public Powertrain.Powertrain powertrain = new Powertrain.Powertrain();
        public float SpeedSigned;
    }
}

namespace ApocaGearbox
{
    internal sealed class TestLog { internal void LogInfo(object value) { } internal void LogWarning(object value) { } }
    internal static class Plugin { internal static TestLog Log; }
    internal static class WheelShaft
    {
        internal static bool TryWheelShaftAngularVelocity(NWH.VehiclePhysics2.Powertrain.TransmissionComponent transmission, out float angularVelocity)
        { angularVelocity = 0f; return false; }
    }
}

namespace Apocasetter
{
    public static class GameMenu { public static bool InGame = true, Paused; }
    public static class InputBlocker { public static bool Active; }
}
