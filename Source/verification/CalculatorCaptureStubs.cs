using System;
using System.Collections.Generic;
namespace UnityEngine
{
    internal struct Vector3 { internal float x, y, z; internal Vector3(float a, float b, float c) { x=a; y=b; z=c; } internal static float Distance(Vector3 a, Vector3 b) { return (float)Math.Sqrt((a.x-b.x)*(a.x-b.x)+(a.y-b.y)*(a.y-b.y)+(a.z-b.z)*(a.z-b.z)); } }
    internal enum WrapMode { Default, Loop }
    internal enum WeightedMode { None, Both }
    internal struct Keyframe { internal float time,value,inTangent,outTangent,inWeight,outWeight; internal WeightedMode weightedMode; }
    internal sealed class AnimationCurve
    {
        internal Func<float,float> Sample = x => (float)Math.Sin(x * Math.PI);
        internal Keyframe[] Points = new [] { new Keyframe {time=0f,value=0f},new Keyframe {time=0.5f,value=1f},new Keyframe {time=1f,value=0f} };
        internal Keyframe[] keys { get { return (Keyframe[])Points.Clone(); } }
        internal Keyframe this[int index] { get { return Points[index]; } }
        internal int length { get { return Points.Length; } }
        internal WrapMode preWrapMode,postWrapMode;
        internal float Evaluate(float x) { return Sample(x); }
    }
    internal sealed class Rigidbody { internal float mass = 2400f; }
    internal sealed class GameObject
    {
        internal string name,tag="item"; internal bool activeInHierarchy=true; internal Transform transform = new Transform();
        internal GameObject(string value) { name=value;transform.name=value;transform.gameObject=this; }
        internal bool CompareTag(string value) { return tag==value; }
        internal T[] GetComponentsInChildren<T>(bool inactive) where T:class
        {
            List<T> matches=new List<T>();
            foreach(var entry in ApocaGearbox.GearboxTarget.Fsms)
            {
                bool child=false;for(Transform p=entry.Key.transform;p!=null;p=p.parent)if(p.gameObject==this){child=true;break;}
                if(!child)continue;
                foreach(var fsm in entry.Value) { fsm.Value.FsmName=fsm.Key;fsm.Value.gameObject=entry.Key;if(fsm.Value is T)matches.Add(fsm.Value as T); }
            }return matches.ToArray();
        }
    }
    internal sealed class Transform { internal string name; internal GameObject gameObject; internal Transform parent; internal Vector3 position; internal NWH.VehiclePhysics2.VehicleController Owner; internal T GetComponentInParent<T>() where T:class { return Owner as T; } }
}
namespace NWH.Common.Vehicles
{
    internal struct Parameters { internal float z; }
    internal sealed class FrictionPreset { internal Parameters BCDE = new Parameters { z = 1f }; }
    internal sealed class WheelUAPI
    {
        internal bool IsGrounded = true;
        internal float Radius = 0.52f, MaxLoad = 10000f, Load = 6000f, LongitudinalFrictionGrip = 1f, RollingResistanceTorque = 15f;
        internal FrictionPreset FrictionPreset = new FrictionPreset();
    }
}
namespace NWH.VehiclePhysics2.Powertrain
{
    internal class PowertrainComponent { internal PowertrainComponent Output; }
    internal sealed class DifferentialComponent : PowertrainComponent { internal float biasAB = 0.5f; internal PowertrainComponent OutputB; }
    internal sealed class WheelComponent : PowertrainComponent { internal NWH.Common.Vehicles.WheelUAPI wheelUAPI = new NWH.Common.Vehicles.WheelUAPI(); }
    internal sealed class TransmissionComponent : PowertrainComponent
    { internal enum TransmissionShiftType { Manual, CVT } internal TransmissionShiftType transmissionType; }
    internal sealed class EngineComponent
    {
        internal enum EngineType { ICE, Electric }
        internal sealed class ForcedInduction
        { internal enum ForcedInductionType { Turbocharger, Supercharger } internal bool useForcedInduction; internal float powerGainMultiplier = 1f; internal ForcedInductionType forcedInductionType; }
        internal EngineType engineType;
        internal ForcedInduction forcedInduction = new ForcedInduction();
        internal UnityEngine.AnimationCurve powerCurve = new UnityEngine.AnimationCurve();
          internal void UpdatePeakPowerAndTorque() { }
        internal float maxPower = 170f, idleRPM = 900f, revLimiterRPM = 4500f;
    }
    internal sealed class Powertrain
    {
        internal EngineComponent engine = new EngineComponent();
        internal TransmissionComponent transmission = new TransmissionComponent();
        internal List<WheelComponent> wheels = new List<WheelComponent>();
    }
}
namespace NWH.VehiclePhysics2.Modules.Aerodynamics
{
    internal sealed class AerodynamicsModule
    { internal bool IsActive = true, simulateDrag = true; internal UnityEngine.Vector3 dimensions = new UnityEngine.Vector3(2f,2f,5f); internal float frontalCd = 0.6f; }
    internal sealed class AerodynamicsModuleWrapper { internal AerodynamicsModule module = new AerodynamicsModule(); }
}
namespace NWH.VehiclePhysics2
{
    internal sealed class VehicleController
    {
        internal UnityEngine.GameObject gameObject = new UnityEngine.GameObject("Preview car");
        internal UnityEngine.Transform transform { get { return gameObject.transform; } }
        internal string name { get { return gameObject.name; } }
        internal UnityEngine.Transform[] Parts;
        internal VehicleController() { Parts = new [] { new UnityEngine.Transform { name="hinge_engine", Owner=this } }; }
        internal T[] GetComponentsInChildren<T>(bool inactive) where T:class { return Parts as T[]; }
        internal Powertrain.Powertrain powertrain = new Powertrain.Powertrain();
        internal UnityEngine.Rigidbody vehicleRigidbody = new UnityEngine.Rigidbody();
        internal float Speed;
        internal Modules.Aerodynamics.AerodynamicsModuleWrapper Wrapper = new Modules.Aerodynamics.AerodynamicsModuleWrapper();
        internal T GetComponent<T>() where T:class { return Wrapper as T; }
    }
}
namespace ApocaGearbox
{
    internal static class GearboxTarget
    {
        internal static Dictionary<UnityEngine.GameObject,Dictionary<string,HutongGames.PlayMaker.PlayMakerFSM>> Fsms = new Dictionary<UnityEngine.GameObject,Dictionary<string,HutongGames.PlayMaker.PlayMakerFSM>>();
        internal static bool IsEngine(UnityEngine.GameObject item) { return Installed.ContainsKey(item)||Fsms.ContainsKey(item)&&Fsms[item].ContainsKey("CheckTag")&&Fsms[item].ContainsKey("Condition"); }
        internal static HutongGames.PlayMaker.PlayMakerFSM FindFsm(UnityEngine.GameObject item,string name) { Dictionary<string,HutongGames.PlayMaker.PlayMakerFSM> fsms; HutongGames.PlayMaker.PlayMakerFSM fsm; return Fsms.TryGetValue(item,out fsms)&&fsms.TryGetValue(name,out fsm) ? fsm : null; }
        internal static Dictionary<UnityEngine.GameObject,NWH.VehiclePhysics2.VehicleController> Installed = new Dictionary<UnityEngine.GameObject,NWH.VehiclePhysics2.VehicleController>();
        internal static NWH.VehiclePhysics2.VehicleController InstalledVehicle(UnityEngine.GameObject item)
        { NWH.VehiclePhysics2.VehicleController vehicle; return Installed.TryGetValue(item,out vehicle) ? vehicle : null; }
    }
    internal static class DriveControl
    {
        internal static Dictionary<NWH.VehiclePhysics2.Powertrain.WheelComponent,float> Override;
        internal static bool TryCalculatorShares(NWH.VehiclePhysics2.VehicleController vehicle, Dictionary<NWH.VehiclePhysics2.Powertrain.WheelComponent,float> shares)
        { if (Override == null) return false; foreach(var pair in Override) shares[pair.Key]=pair.Value; return true; }
    }
}
namespace HutongGames.PlayMaker
{
    internal class FsmStateAction { }
    internal sealed class FsmFloat { internal string Name; internal float Value; }
    internal sealed class FsmProperty { internal string PropertyName; internal FsmFloat FloatParameter; internal FsmEnum EnumParameter; }
    internal sealed class FsmEnum { internal System.Enum Value; }
    internal sealed class FsmString { internal string Value; }
    internal sealed class FsmVariables { internal Dictionary<string,FsmString> Strings=new Dictionary<string,FsmString>(); internal FsmString GetFsmString(string name) { FsmString value;return Strings.TryGetValue(name,out value)?value:null; } }
    internal sealed class FsmEvent { internal string Name; }
    internal sealed class FsmTransition { internal string EventName, ToState; }
    internal sealed class FsmState { internal string Name; internal FsmStateAction[] Actions; internal FsmTransition[] Transitions; }
    internal sealed class Fsm { internal FsmState[] States; }
    internal sealed class PlayMakerFSM { internal Fsm Fsm; internal string ActiveStateName,FsmName; internal UnityEngine.GameObject gameObject; internal FsmVariables FsmVariables=new FsmVariables(); }
}
namespace HutongGames.PlayMaker.Actions
{
    internal sealed class SetProperty : HutongGames.PlayMaker.FsmStateAction { internal HutongGames.PlayMaker.FsmProperty targetProperty; }
    internal sealed class SetFloatValue : HutongGames.PlayMaker.FsmStateAction { internal HutongGames.PlayMaker.FsmFloat floatVariable, floatValue; }
    internal sealed class FloatCompare : HutongGames.PlayMaker.FsmStateAction { internal HutongGames.PlayMaker.FsmFloat float1,float2,tolerance; internal HutongGames.PlayMaker.FsmEvent equal,lessThan,greaterThan; }
}
