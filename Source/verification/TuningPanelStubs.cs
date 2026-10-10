using System;
using System.Collections;
using System.Collections.Generic;
namespace UnityEngine
{
    internal sealed class GameObject { internal bool activeInHierarchy=true;internal string name="Test engine";internal Transform transform=new Transform(); }
    internal sealed class Transform { internal Vector3 position; }
    internal struct Vector3 { internal static float Distance(Vector3 a,Vector3 b){return 0f;} }
    internal struct Vector2 { internal static Vector2 zero {get{return new Vector2();}} }
    internal struct Rect {internal float x,y,width,height;internal Rect(float a,float b,float c,float d){x=a;y=b;width=c;height=d;} }
    internal enum CursorLockMode { None,Locked }
    internal static class Cursor {internal static CursorLockMode lockState;internal static bool visible;}
    internal static class Screen {internal static int width=1920,height=1080;}
    internal static class Time {internal static float unscaledTime;internal static int frameCount;}
    internal static class Mathf
    {
        internal static float Min(float a,float b){return Math.Min(a,b);}internal static float Max(float a,float b){return Math.Max(a,b);}
        internal static float Clamp(float v,float a,float b){return Math.Max(a,Math.Min(b,v));}
        internal static bool Approximately(float a,float b){return Math.Abs(a-b)<0.00001f;}
        internal static float Round(float v){return (float)Math.Round(v);}
    }
    internal sealed class GUIStyle { }
    internal sealed class GUISkin {internal GUIStyle label=new GUIStyle();}
    internal static class GUI {internal static GUISkin skin=new GUISkin();internal static int depth;internal static bool enabled=true;internal static void DragWindow(Rect rect){} }
    internal sealed class GUILayoutOption { }
    internal static class GUILayout
    {
        internal static bool Button(string s,params GUILayoutOption[] options){return false;}
        internal static bool Toggle(bool value,string label){return value;}
        internal static string TextField(string s,int max,params GUILayoutOption[] options){return s;}
        internal static int Toolbar(int selected,string[] labels){return selected;}
        internal static void Label(string s,params GUILayoutOption[] options){}internal static void Label(string s,GUIStyle style){}
        internal static void Space(float value){}internal static void BeginHorizontal(){}internal static void EndHorizontal(){}
        internal static Vector2 BeginScrollView(Vector2 scroll,params GUILayoutOption[] options){return scroll;}internal static void EndScrollView(){}
        internal static float HorizontalSlider(float v,float low,float high,params GUILayoutOption[] options){return v;}
        internal static Rect Window(int id,Rect rect,Action<int> draw,string title,params GUILayoutOption[] options){return rect;}
        internal static GUILayoutOption Width(float v){return new GUILayoutOption();}internal static GUILayoutOption Height(float v){return new GUILayoutOption();}
        internal static GUILayoutOption ExpandWidth(bool v){return new GUILayoutOption();}internal static GUILayoutOption ExpandHeight(bool v){return new GUILayoutOption();}
    }
    internal static class Object {internal static T[] FindObjectsOfType<T>(){return new T[0];}}
}
namespace BepInEx.Configuration
{
    internal struct ConfigDefinition {internal string Section,Key;}
    internal sealed class ConfigEntryBase {internal object BoxedValue;}
    internal sealed class ConfigEntry<T>{internal T Value;}
    internal sealed class ConfigFile : IEnumerable<KeyValuePair<ConfigDefinition,ConfigEntryBase>>
    {
        internal ConfigEntry<T> Bind<T>(string section,string key,T value,string description){return new ConfigEntry<T>{Value=value};}
        public IEnumerator<KeyValuePair<ConfigDefinition,ConfigEntryBase>> GetEnumerator(){return new List<KeyValuePair<ConfigDefinition,ConfigEntryBase>>().GetEnumerator();}
        IEnumerator IEnumerable.GetEnumerator(){return GetEnumerator();}
    }
}
namespace NWH.VehiclePhysics2.Powertrain
{
    internal sealed class TransmissionComponent {internal enum TransmissionShiftType {Manual,CVT}internal TransmissionShiftType transmissionType;}
    internal sealed class EngineComponent {internal float revLimiterRPM=6500f;}
    internal sealed class Powertrain {internal TransmissionComponent transmission=new TransmissionComponent();internal EngineComponent engine=new EngineComponent();}
}
namespace NWH.VehiclePhysics2
{
    internal sealed class VehicleController {internal UnityEngine.GameObject gameObject=new UnityEngine.GameObject();internal string name="Car";internal UnityEngine.Transform transform=new UnityEngine.Transform();internal Powertrain.Powertrain powertrain=new Powertrain.Powertrain();}
}
namespace Apocasetter
{
    internal static class Theme {internal static UnityEngine.GUIStyle Header,SubLabel;internal static void Apply(){} }
    internal static class InputBlocker {internal static void Set(bool value){} }
}
namespace ApocaGearbox
{
    internal sealed class TestLog {internal void LogWarning(object value){}internal void LogError(object value){} }
    internal static class Plugin {internal static TestLog Log=new TestLog();internal static BepInEx.Configuration.ConfigFile Settings=new BepInEx.Configuration.ConfigFile();}
    internal static class GearboxRunner {internal static int ConsumedFrame;}
    internal sealed class GearboxState
    {
        internal static GearboxState Shared=new GearboxState();internal TuneData Tune;internal string Status;
        internal static GearboxState GetOrCreate(UnityEngine.GameObject target){return Shared;}internal void RefreshLegacyFinalDrive(){}
        internal bool Reset(NWH.VehiclePhysics2.VehicleController v,out string error){error=null;return true;}
        internal bool Commit(NWH.VehiclePhysics2.VehicleController v,TuneData desired,out string error){return desired.Validate(null,out error);}
    }
    internal static class GearboxNativeDefaults {internal static TuneData Native;internal static TuneData Capture(UnityEngine.GameObject target,NWH.VehiclePhysics2.Powertrain.TransmissionComponent t){return Native.Clone();}}
    internal static class CalculatorEngineSpecs {internal static bool ReadRpm(UnityEngine.GameObject target,out float idle,out float limit){idle=900f;limit=6500f;return true;}}
    internal static class GearboxTarget {internal static NWH.VehiclePhysics2.VehicleController Mounted;internal static bool IsEngine(UnityEngine.GameObject g){return true;}internal static NWH.VehiclePhysics2.VehicleController InstalledVehicle(UnityEngine.GameObject g){return Mounted;}}
    internal static class GearboxPrompt {internal static void Clear(){} }
    internal sealed class DriveControl {internal bool Enabled;internal static bool TryGet(NWH.VehiclePhysics2.VehicleController v,out DriveControl d){d=null;return false;}}
    internal sealed class CalculatorSnapshot {internal CalculatorInputs Inputs;internal string EngineDescription="Engine",VehicleDescription="Vehicle",Notes="Notes";}
    internal static class GearCalculator
    {
        internal static int Checks;internal static bool Matches=true;
        internal static bool Capture(UnityEngine.GameObject item,NWH.VehiclePhysics2.VehicleController preview,float? requested,out CalculatorSnapshot snapshot,out string error)
        {
            snapshot=new CalculatorSnapshot {Inputs=new CalculatorInputs {Radius=0.5f,Mass=2000f,PeakPowerKw=150f,PeakTorqueNm=320f,
                PowerRpm=4500f,BandLowRpm=2500f,IdleRpm=900f,RedlineRpm=requested.Value>0f?requested.Value:6500f,DrivenTorqueCapacity=7000f}};
            error=null;return true;
        }
        internal static bool StillMatches(UnityEngine.GameObject item,CalculatorSnapshot snapshot){Checks++;return Matches;}
        internal static bool CanLink(UnityEngine.GameObject item,NWH.VehiclePhysics2.VehicleController vehicle){return true;}
    }
}
