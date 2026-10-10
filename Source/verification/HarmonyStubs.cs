using System;
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class HarmonyPatch : Attribute
    {
        internal HarmonyPatch(Type target, string name, Type[] parameters) { }
        internal HarmonyPatch(Type target, string name) { }
    }
}
