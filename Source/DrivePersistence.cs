using System;
using System.Collections.Generic;
using HarmonyLib;
using HutongGames.PlayMaker;
using NWH.VehiclePhysics2;

namespace ApocaGearbox
{
    // Axle selection belongs to the vehicle's own native save record. Engine
    // swaps and gearbox presets must not carry a chassis conversion with them.
    internal static class DrivePersistence
    {
        internal const string StateName = "ApocaGearbox_Drive_v1";
        private const int MaximumLength = 128;

        internal static bool IsVehicleSave(Fsm fsm)
        {
            return fsm != null && fsm.Name == "saveItemVar" && fsm.Owner != null &&
                fsm.Variables != null && fsm.Owner.gameObject.GetComponent<VehicleController>() != null;
        }

        internal static void Remove(FsmVariables variables)
        {
            FsmString[] existing = variables.StringVariables;
            if (existing == null) return;
            List<FsmString> kept = new List<FsmString>(existing.Length);
            for (int i = 0; i < existing.Length; i++)
                if (existing[i] == null || existing[i].Name != StateName) kept.Add(existing[i]);
            if (kept.Count == existing.Length) return;
            variables.StringVariables = kept.ToArray();
            variables.Reinitialize();
        }

        internal static void PrepareSave(Fsm fsm)
        {
            DriveControl control = fsm.Owner.gameObject.GetComponent<DriveControl>();
            string saved = control == null ? String.Empty : control.Export();
            if (String.IsNullOrEmpty(saved)) { Remove(fsm.Variables); return; }
            if (saved.Length > MaximumLength)
                throw new InvalidOperationException("Vehicle drive state exceeds the permitted size.");
            SavePersistence.EnsureString(fsm.Variables, StateName).Value = saved;
        }

        internal static void Report(string operation, Exception exception)
        {
            if (Plugin.Log != null)
                Plugin.Log.LogError("Could not " + operation + " vehicle drive selection: " + exception);
        }
    }

    [HarmonyPatch(typeof(ES3PlayMaker.SaveAll), "Enter")]
    internal static class DriveSavePatch
    {
        private static void Prefix(ES3PlayMaker.SaveAll __instance)
        {
            try
            {
                if (!DrivePersistence.IsVehicleSave(__instance.Fsm) ||
                    __instance.saveFsmVariables == null || !__instance.saveFsmVariables.Value) return;
                DrivePersistence.PrepareSave(__instance.Fsm);
            }
            catch (Exception exception)
            {
                if (__instance.Fsm != null && __instance.Fsm.Variables != null)
                    DrivePersistence.Remove(__instance.Fsm.Variables);
                DrivePersistence.Report("save", exception);
            }
        }
    }

    [HarmonyPatch(typeof(ES3PlayMaker.LoadAll), "Enter")]
    internal static class DriveLoadPatch
    {
        private static void Prefix(ES3PlayMaker.LoadAll __instance, out DriveControl __state)
        {
            __state = null;
            try
            {
                if (!DrivePersistence.IsVehicleSave(__instance.Fsm) ||
                    __instance.loadFsmVariables == null || !__instance.loadFsmVariables.Value) return;
                VehicleController vehicle = __instance.Fsm.Owner.gameObject.GetComponent<VehicleController>();
                __state = vehicle.gameObject.GetComponent<DriveControl>();
                if (__state != null) __state.Restore(String.Empty);
                // A reused object loading an older save must not inherit state.
                // Reserve the variable without adding a controller to every car.
                SavePersistence.EnsureString(__instance.Fsm.Variables, DrivePersistence.StateName).Value = String.Empty;
            }
            catch (Exception exception)
            {
                __state = null;
                DrivePersistence.Report("prepare to load", exception);
            }
        }

        private static void Postfix(ES3PlayMaker.LoadAll __instance, DriveControl __state)
        {
            try
            {
                if (!DrivePersistence.IsVehicleSave(__instance.Fsm) ||
                    __instance.loadFsmVariables == null || !__instance.loadFsmVariables.Value) return;
                string saved = SavePersistence.Read(__instance.Fsm.Variables, DrivePersistence.StateName);
                if (saved.Length > 128)
                { if (__state != null) __state.Restore(String.Empty); throw new InvalidOperationException("Saved vehicle drive state is too long."); }
                if (__state == null)
                {
                    if (String.IsNullOrEmpty(saved) || saved == "v1|0|1" || saved == "v1|0|0") return;
                    __state = DriveControl.GetOrCreate(__instance.Fsm.Owner.gameObject.GetComponent<VehicleController>());
                }
                if (__state == null) return;
                // Native wheel assembly may still be running. The control binds
                // after load rather than editing an unfinished drivetrain here.
                __state.Restore(saved);
            }
            catch (Exception exception) { DrivePersistence.Report("load", exception); }
        }
    }
}
