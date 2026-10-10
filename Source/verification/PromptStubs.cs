namespace UnityEngine
{
    internal enum KeyCode { E, G }
    internal sealed class GameObject
    {
        internal UI.Text Text;
        internal T GetComponent<T>() where T:class { return Text as T; }
    }
}
namespace UnityEngine.UI
{
    internal sealed class Text { internal string text; internal object font=new object(); internal int fontSize=24; internal string alignment="Left",color="White"; }
}
namespace NWH.VehiclePhysics2 { internal sealed class VehicleController { } }
namespace HutongGames.PlayMaker
{
    internal sealed class FsmGameObject { internal UnityEngine.GameObject Value; }
    internal sealed class FsmVariables
    {
        internal static FsmVariables GlobalVariables=new FsmVariables();
        internal FsmGameObject ItemUse;
        internal FsmGameObject GetFsmGameObject(string name) { return name=="UI_ItemUse" ? ItemUse : null; }
    }
}
namespace Apocasetter
{
    internal static class GameMenu { internal static bool InGame=true,Paused; }
    internal static class InputBlocker { internal static bool Active; }
}
namespace ApocaGearbox
{
    internal sealed class Setting<T> { internal T Value; }
        internal static class Plugin { internal static Setting<bool> Enabled=new Setting<bool> {Value=true}; internal static Setting<UnityEngine.KeyCode> OpenKey=new Setting<UnityEngine.KeyCode> {Value=UnityEngine.KeyCode.E}; }
    internal static class TuningPanel { internal static bool IsOpen; }
    internal static class GearboxTarget
    {
        internal static bool Aimed=true;
        internal static bool HasAim() { return Aimed; }
        internal static bool TryAim(out UnityEngine.GameObject engine,out NWH.VehiclePhysics2.VehicleController vehicle,out string label)
        { engine=null;vehicle=null;label=null;return Aimed; }
    }
}
