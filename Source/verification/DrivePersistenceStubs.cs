using System;
using System.Collections.Generic;
namespace UnityEngine
{
    public class GameObject
    {
        public readonly Dictionary<Type, object> Components = new Dictionary<Type, object>();
        public bool Engine;
        public T GetComponent<T>() where T : class
        { object result; return Components.TryGetValue(typeof(T), out result) ? result as T : null; }
    }
}
namespace NWH.VehiclePhysics2 { public class VehicleController { public UnityEngine.GameObject gameObject; } }
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public class HarmonyPatch : Attribute
    { public HarmonyPatch(Type type, string method) { } }
}
namespace HutongGames.PlayMaker
{
    public class FsmString
    { public string Name; public string Value; public FsmString(string name) { Name = name; } }
    public class FsmBool { public bool Value; }
    public class FsmVariables
    {
        public FsmString[] StringVariables = new FsmString[0]; public int Refreshes;
        public void Reinitialize() { Refreshes++; }
    }
    public class Owner { public UnityEngine.GameObject gameObject; }
    public class Fsm { public string Name; public Owner Owner; public FsmVariables Variables = new FsmVariables(); }
}
namespace ES3PlayMaker
{
    public class SaveAll { public HutongGames.PlayMaker.Fsm Fsm; public HutongGames.PlayMaker.FsmBool saveFsmVariables; }
    public class LoadAll { public HutongGames.PlayMaker.Fsm Fsm; public HutongGames.PlayMaker.FsmBool loadFsmVariables; }
}
namespace ApocaGearbox
{
    public class TestLog
    { public int Errors; public void LogError(string text) { Errors++; } }
    public static class Plugin { public static TestLog Log = new TestLog(); }
    public sealed class GearboxState
    {
        public string Tune, Stock;
        public static GearboxState GetOrCreate(UnityEngine.GameObject engine)
        {
            if (!engine.Engine) return null;
            GearboxState state = engine.GetComponent<GearboxState>();
            if (state == null) { state = new GearboxState(); engine.Components[typeof(GearboxState)] = state; }
            return state;
        }
        public void Restore(string tune, string stock) { Tune = tune; Stock = stock; }
        public string ExportTuning() { return Tune; }
        public string ExportStock() { return Stock; }
    }
    public sealed class DriveControl
    {
        public string Saved = String.Empty;
        public int Loads;
        public static DriveControl GetOrCreate(NWH.VehiclePhysics2.VehicleController vehicle)
        {
            DriveControl state = vehicle.gameObject.GetComponent<DriveControl>();
            if (state == null) { state = new DriveControl(); vehicle.gameObject.Components[typeof(DriveControl)] = state; }
            return state;
        }
        public void Restore(string saved) { Saved = saved; Loads++; }
        public string Export() { return Saved; }
    }
}
