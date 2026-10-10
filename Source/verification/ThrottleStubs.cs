using System;
using System.Collections.Generic;
namespace HarmonyLib
{
    internal sealed class HarmonyPatch : Attribute { internal HarmonyPatch(Type type, string method) { } }
    internal sealed class HarmonyPriority : Attribute { internal HarmonyPriority(int priority) { } }
    internal static class Priority { internal const int Last = 0; }
}
namespace UnityEngine
{
    internal sealed class GameObject { internal bool activeInHierarchy = true; }
    internal static class Time { internal static int frameCount; internal static float deltaTime, unscaledTime; }
    internal static class Application { internal static bool isFocused = true; }
}
namespace Apocasetter
{
    internal static class GameMenu { internal static bool InGame = true, Paused; }
    internal static class InputBlocker { internal static bool Active; }
}
namespace NWH.VehiclePhysics2.Input
{
    internal sealed class VehicleInputHandler
    {
        internal NWH.VehiclePhysics2.VehicleController vehicleController;
        internal float Throttle = 1f, Brakes = 0.4f, Clutch = 0.7f;
    }
}
namespace NWH.VehiclePhysics2.Powertrain
{
    internal sealed class EngineComponent { }
    internal sealed class TransmissionComponent { internal int Gear = 1; }
    internal sealed class Powertrain
    {
        internal EngineComponent engine = new EngineComponent();
        internal TransmissionComponent transmission = new TransmissionComponent();
    }
}
namespace NWH.VehiclePhysics2
{
    internal sealed class VehicleController
    {
        internal UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
        internal NWH.VehiclePhysics2.Input.VehicleInputHandler input;
        internal NWH.VehiclePhysics2.Powertrain.Powertrain powertrain = new NWH.VehiclePhysics2.Powertrain.Powertrain();
    }
}
namespace ApocaGearbox
{
    internal sealed class Flag { internal bool Value = true; }
    internal sealed class LogStub { internal int Warnings; internal void LogWarning(object value) { Warnings++; } }
    internal static class Plugin { internal static Flag Enabled = new Flag(); internal static LogStub Log = new LogStub(); }
    internal sealed class GearboxState
    {
        internal NWH.VehiclePhysics2.VehicleController Vehicle;
        internal TuneData Tune;
        internal static Dictionary<NWH.VehiclePhysics2.Powertrain.EngineComponent, GearboxState> Mounted =
            new Dictionary<NWH.VehiclePhysics2.Powertrain.EngineComponent, GearboxState>();
        internal static GearboxState ForEngine(NWH.VehiclePhysics2.Powertrain.EngineComponent engine)
        { GearboxState result; return Plugin.Enabled.Value && Mounted.TryGetValue(engine, out result) ? result : null; }
    }
}
