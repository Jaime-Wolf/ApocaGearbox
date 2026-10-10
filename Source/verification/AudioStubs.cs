using System;
using System.Collections.Generic;
namespace UnityEngine
{
    public class Object { }
    public struct Vector3 { public float x,y,z; public Vector3(float a,float b,float c) { x=a;y=b;z=c; } }
    public class GameObject : Object
    {
        public string name; public bool activeInHierarchy=true; public Transform transform;
        public readonly List<object> Components=new List<object>();
        public GameObject(string n) { name=n;transform=new Transform(this); }
        public T[] GetComponents<T>() where T:class
        { List<T> result=new List<T>();foreach(object item in Components) { T match=item as T;if(match!=null)result.Add(match); }return result.ToArray(); }
    }
    public class Transform : Object
    {
        public GameObject gameObject; public string name { get { return gameObject.name; } }
        public Transform parent; public Vector3 position; public readonly List<Transform> Children=new List<Transform>();
        public Transform(GameObject owner) { gameObject=owner; }
        public Transform Find(string n) { foreach(Transform child in Children)if(child.name==n)return child;return null; }
        public T[] GetComponents<T>() where T:class { return gameObject.GetComponents<T>(); }
        public void Add(GameObject child) { Children.Add(child.transform);child.transform.parent=this; }
    }
    public class AudioClip : Object { public string name; public AudioClip(string n) { name=n; } }
    public static class Time { public static float unscaledTime=10f; }
    public static class AudioSource
    {
        public static int Calls; public static AudioClip Clip; public static Vector3 Position; public static float Volume;
        public static bool Throw;
        public static void PlayClipAtPoint(AudioClip clip,Vector3 position,float volume)
        { if(Throw)throw new InvalidOperationException("audio unavailable");Calls++;Clip=clip;Position=position;Volume=volume; }
    }
}
namespace HutongGames.PlayMaker
{
    public class FsmObject { public UnityEngine.Object Value; }
    public class FsmFloat { public float Value; }
    public class FsmStateAction { }
    public class FsmState { public FsmStateAction[] Actions; }
    public class Fsm { public string Name; public FsmState[] States; }
    public class PlayMakerFSM { public Fsm Fsm; public bool isActiveAndEnabled; }
}
namespace HutongGames.PlayMaker.Actions
{
    public class PlaySound : HutongGames.PlayMaker.FsmStateAction
    { public HutongGames.PlayMaker.FsmObject clip; public HutongGames.PlayMaker.FsmFloat volume; }
}
namespace ApocaGearbox
{
    public static class TuneData { public static bool Finite(float x) { return !Single.IsNaN(x)&&!Single.IsInfinity(x); } }
    public sealed class AudioLog { public int Warnings; public void LogWarning(string message) { Warnings++; } }
    public static class Plugin { public static AudioLog Log=new AudioLog(); }
}
