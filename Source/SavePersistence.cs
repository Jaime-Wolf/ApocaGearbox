using System;
using System.Collections.Generic;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;

namespace ApocaGearbox
{
    // ES3's PlayMaker wrapper only loads variables already present in the FSM.
    // Register these names before LoadAll.Enter and read them after it returns.
    // The engine item's existing saveItemVar key gives each engine its own data.
    internal static class SavePersistence
    {
        internal const string TuningName = "ApocaGearbox_Tuning_v1";
        internal const string StockName = "ApocaGearbox_Stock_v1";
        private const int MaximumPayloadLength = 8192;

        internal static bool IsEngineSave(Fsm fsm)
        {
            return fsm != null && fsm.Name == "saveItemVar" && fsm.Owner != null && fsm.Variables != null;
        }

        internal static FsmString EnsureString(FsmVariables variables, string name)
        {
            FsmString[] existing = variables.StringVariables ?? new FsmString[0];
            for (int i = 0; i < existing.Length; i++)
            {
                if (existing[i] != null && existing[i].Name == name)
                    return existing[i];
            }

            FsmString added = new FsmString(name);
            added.Value = String.Empty;
            FsmString[] replacement = new FsmString[existing.Length + 1];
            Array.Copy(existing, replacement, existing.Length);
            replacement[existing.Length] = added;
            variables.StringVariables = replacement;
            variables.Reinitialize();
            return added;
        }

        internal static string Read(FsmVariables variables, string name)
        {
            FsmString[] strings = variables.StringVariables;
            if (strings == null)
                return String.Empty;
            for (int i = 0; i < strings.Length; i++)
            {
                if (strings[i] != null && strings[i].Name == name)
                    return strings[i].Value ?? String.Empty;
            }
            return String.Empty;
        }

        internal static void Remove(FsmVariables variables)
        {
            FsmString[] existing = variables.StringVariables;
            if (existing == null)
                return;

            List<FsmString> kept = new List<FsmString>(existing.Length);
            for (int i = 0; i < existing.Length; i++)
            {
                FsmString value = existing[i];
                if (value == null || (value.Name != TuningName && value.Name != StockName))
                    kept.Add(value);
            }

            if (kept.Count == existing.Length)
                return;
            variables.StringVariables = kept.ToArray();
            variables.Reinitialize();
        }

        internal static bool InBounds(string value)
        {
            return value != null && value.Length <= MaximumPayloadLength;
        }

        internal static void Report(string operation, Exception exception)
        {
            if (Plugin.Log != null)
                Plugin.Log.LogError("Could not " + operation + " engine gearbox tuning: " + exception);
        }
    }

    [HarmonyPatch(typeof(ES3PlayMaker.SaveAll), "Enter")]
    internal static class GearboxSavePatch
    {
        private static void Prefix(ES3PlayMaker.SaveAll __instance)
        {
            try
            {
                Fsm fsm = __instance.Fsm;
                if (!SavePersistence.IsEngineSave(fsm) || __instance.saveFsmVariables == null || !__instance.saveFsmVariables.Value)
                    return;

                // Saving ordinary items must not create adjustment components.
                GearboxState state = fsm.Owner.gameObject.GetComponent<GearboxState>();
                string tuning = state != null ? state.ExportTuning() : String.Empty;
                if (String.IsNullOrEmpty(tuning))
                {
                    SavePersistence.Remove(fsm.Variables);
                    return;
                }

                string stock = state.ExportStock() ?? String.Empty;
                if (!SavePersistence.InBounds(tuning) || !SavePersistence.InBounds(stock))
                    throw new InvalidOperationException("Serialized tuning exceeds the permitted size.");

                SavePersistence.EnsureString(fsm.Variables, SavePersistence.TuningName).Value = tuning;
                SavePersistence.EnsureString(fsm.Variables, SavePersistence.StockName).Value = stock;
            }
            catch (Exception exception)
            {
                if (__instance.Fsm != null && __instance.Fsm.Variables != null)
                    SavePersistence.Remove(__instance.Fsm.Variables);
                SavePersistence.Report("save", exception);
            }
        }
    }

    [HarmonyPatch(typeof(ES3PlayMaker.LoadAll), "Enter")]
    internal static class GearboxLoadPatch
    {
        private static void Prefix(ES3PlayMaker.LoadAll __instance, out GearboxState __state)
        {
            __state = null;
            try
            {
                Fsm fsm = __instance.Fsm;
                if (!SavePersistence.IsEngineSave(fsm) || __instance.loadFsmVariables == null || !__instance.loadFsmVariables.Value)
                    return;

                // GetOrCreate validates that the owner's object is an engine item.
                __state = GearboxState.GetOrCreate(fsm.Owner.gameObject);
                if (__state == null)
                    return;
                __state.Restore(String.Empty, String.Empty);

                // Reset before each load so a save with no custom keys cannot
                // inherit tuning left in a reused item/FSM from another save.
                SavePersistence.EnsureString(fsm.Variables, SavePersistence.TuningName).Value = String.Empty;
                SavePersistence.EnsureString(fsm.Variables, SavePersistence.StockName).Value = String.Empty;
            }
            catch (Exception exception)
            {
                __state = null;
                SavePersistence.Report("prepare to load", exception);
            }
        }

        private static void Postfix(ES3PlayMaker.LoadAll __instance, GearboxState __state)
        {
            if (__state == null)
                return;
            try
            {
                Fsm fsm = __instance.Fsm;
                if (!SavePersistence.IsEngineSave(fsm))
                    return;

                string tuning = SavePersistence.Read(fsm.Variables, SavePersistence.TuningName);
                string stock = SavePersistence.Read(fsm.Variables, SavePersistence.StockName);
                if (!SavePersistence.InBounds(tuning) || !SavePersistence.InBounds(stock))
                    throw new InvalidOperationException("Saved tuning exceeds the permitted size.");

                // Restore is responsible for validating the serialized ratios and
                // applying/deferring them until this engine is mounted to a vehicle.
                // Empty strings deliberately clear old state for an untouched save.
                __state.Restore(tuning, stock);
                if (String.IsNullOrEmpty(tuning))
                    SavePersistence.Remove(fsm.Variables);
            }
            catch (Exception exception)
            {
                __state.Restore(String.Empty, String.Empty);
                SavePersistence.Report("restore", exception);
            }
        }
    }
}
