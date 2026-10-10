using System;
using System.Collections.Generic;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string method) { } }
}
namespace UnityEngine
{
    public sealed class GameObject
    {
        private static int nextId;
        private readonly int id = ++nextId;
        public string name;
        public readonly Transform transform;
        public readonly Dictionary<string, PlayMakerFSM> Fsms = new Dictionary<string, PlayMakerFSM>();
        public object ExtraComponent;
        public GameObject(string value) { name = value; transform = new Transform(this); }
        public int GetInstanceID() { return id; }
        public T GetComponent<T>() where T : class { return ExtraComponent as T; }
    }
    public sealed class Transform
    {
        public readonly GameObject gameObject;
        public Transform parent;
        public readonly Dictionary<string, Transform> Children = new Dictionary<string, Transform>();
        public Transform(GameObject owner) { gameObject = owner; }
        public Transform Find(string name) { Transform found; return Children.TryGetValue(name, out found) ? found : null; }
    }
    public sealed class Component { public GameObject gameObject; }
    public static class Time { public static float unscaledTime; }
}
namespace UnityEngine.UI { public sealed class Text { public string text; } }
namespace NWH.VehiclePhysics2 { public sealed class VehicleController { } }
public sealed class PlayMakerFSM
{
    public readonly HutongGames.PlayMaker.FsmVariables FsmVariables = new HutongGames.PlayMaker.FsmVariables();
    public bool enabled = true;
    public HutongGames.PlayMaker.Fsm Fsm;
    public string ActiveStateName { get { return Fsm != null && Fsm.ActiveState != null ? Fsm.ActiveState.Name : null; } }
}
namespace HutongGames.PlayMaker
{
    public sealed class FsmFloat { public float Value; public string Name; }
    public sealed class FsmString { public string Value; public string Name; }
    public sealed class FsmVariables
    {
        public readonly Dictionary<string, FsmFloat> Floats = new Dictionary<string, FsmFloat>();
        public readonly Dictionary<string, FsmString> Strings = new Dictionary<string, FsmString>();
        public FsmFloat GetFsmFloat(string name) { FsmFloat value; return Floats.TryGetValue(name, out value) ? value : null; }
        public FsmString GetFsmString(string name) { FsmString value; return Strings.TryGetValue(name, out value) ? value : null; }
    }
    public sealed class FsmOwnerDefault { public UnityEngine.GameObject Target; }
    public sealed class FsmState { public string Name; public FsmStateAction[] Actions; }
    public sealed class Fsm
    {
        public string Name; public UnityEngine.Component Owner; public FsmState ActiveState;
        public UnityEngine.GameObject GetOwnerDefaultTarget(FsmOwnerDefault target) { return target.Target; }
    }
    public class FsmStateAction { public Fsm Fsm; }
}
namespace HutongGames.PlayMaker.Actions
{
    public sealed class FsmFloatAdd : HutongGames.PlayMaker.FsmStateAction
    { public HutongGames.PlayMaker.FsmString fsmName, variableName; }
    public sealed class SetFsmFloat : HutongGames.PlayMaker.FsmStateAction
    { public HutongGames.PlayMaker.FsmString fsmName, variableName; }
    public sealed class FloatClamp : HutongGames.PlayMaker.FsmStateAction
    { public HutongGames.PlayMaker.FsmFloat floatVariable; }
    public sealed class UiTextSetText : HutongGames.PlayMaker.FsmStateAction
    { public HutongGames.PlayMaker.FsmString text; public HutongGames.PlayMaker.FsmOwnerDefault gameObject; }
}
namespace ApocaGearbox
{
    internal static class GearboxState
    {
        internal static int NativeNotifications;
        internal static UnityEngine.GameObject NativeEngine;
        internal static void NativeFinalDriveEdited(UnityEngine.GameObject engine)
        { NativeNotifications++; NativeEngine = engine; }
    }
    internal static class GearboxTarget
    {
        internal static bool Selected;
        internal static UnityEngine.GameObject Aim;
        internal static PlayMakerFSM FindFsm(UnityEngine.GameObject owner, string name)
        { PlayMakerFSM found; return owner.Fsms.TryGetValue(name, out found) ? found : null; }
        internal static bool ToolSelected() { return Selected; }
        internal static bool TryAim(out UnityEngine.GameObject engine, out NWH.VehiclePhysics2.VehicleController vehicle, out string title)
        { engine = Aim; vehicle = null; title = null; return Selected && Aim != null; }
    }
    internal sealed class LegacyLogger { internal void LogWarning(string text) { } }
    internal static class Plugin { internal static LegacyLogger Log = new LegacyLogger(); }
}
