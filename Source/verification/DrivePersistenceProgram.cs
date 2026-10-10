using System;
using System.Reflection;
using HutongGames.PlayMaker;
using NWH.VehiclePhysics2;
using UnityEngine;
namespace ApocaGearbox
{
    internal static class DrivePersistenceProgram
    {
        private static int checks;
        private static void Check(bool pass, string text)
        { checks++; if (!pass) throw new Exception(text); }
        private static Fsm Create(bool vehicle)
        {
            GameObject owner = new GameObject();
            if (vehicle) owner.Components[typeof(VehicleController)] = new VehicleController { gameObject = owner };
            return new Fsm { Name = "saveItemVar", Owner = new Owner { gameObject = owner } };
        }
        private static void Save(Fsm fsm, bool enabled)
        {
            ES3PlayMaker.SaveAll action = new ES3PlayMaker.SaveAll { Fsm = fsm, saveFsmVariables = new FsmBool { Value = enabled } };
            typeof(DriveSavePatch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { action });
        }
        private static void Load(Fsm fsm, string payload, bool hasKey)
        {
            ES3PlayMaker.LoadAll action = new ES3PlayMaker.LoadAll { Fsm = fsm, loadFsmVariables = new FsmBool { Value = true } };
            object[] args = { action, null };
            typeof(DriveLoadPatch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
            if (hasKey) SavePersistence.EnsureString(fsm.Variables, DrivePersistence.StateName).Value = payload;
            typeof(DriveLoadPatch).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
        }
        private static void Main()
        {
            Fsm a = Create(true), b = Create(true), item = Create(false);
            Check(DrivePersistence.IsVehicleSave(a), "root vehicle save accepted");
            Check(!DrivePersistence.IsVehicleSave(item), "ordinary item cannot gain chassis state");
            item.Owner.gameObject.Engine = true;
            Check(!DrivePersistence.IsVehicleSave(item), "engine tune record remains separate");
            a.Name = "Drive"; Check(!DrivePersistence.IsVehicleSave(a), "non-save vehicle FSM rejected"); a.Name = "saveItemVar";
            Check(!DrivePersistence.IsVehicleSave(null), "null FSM safe");
            DriveControl ca = DriveControl.GetOrCreate(a.Owner.gameObject.GetComponent<VehicleController>());
            ca.Saved = "v1|1|0";
            SavePersistence.EnsureString(a.Variables, "NativeFuel").Value = "12.5";
            SavePersistence.EnsureString(a.Variables, SavePersistence.TuningName).Value = "engine tune";
            Save(a, true);
            Check(SavePersistence.Read(a.Variables, DrivePersistence.StateName) == "v1|1|0", "vehicle-specific state prepared for native save");
            Check(SavePersistence.Read(a.Variables, "NativeFuel") == "12.5", "native fuel preserved");
            Check(SavePersistence.Read(a.Variables, SavePersistence.TuningName) == "engine tune", "drive save leaves engine key alone");
            Save(b, true);
            Check(b.Owner.gameObject.GetComponent<DriveControl>() == null, "saving untouched vehicle does not add control");
            Check(SavePersistence.Read(b.Variables, DrivePersistence.StateName) == String.Empty, "other vehicle does not inherit mode");
            ca.Saved = "v1|1|1"; Save(a, false);
            Check(SavePersistence.Read(a.Variables, DrivePersistence.StateName) == "v1|1|0", "disabled native variable-save action is respected");
            ca.Saved = String.Empty; Save(a, true);
            Check(SavePersistence.Read(a.Variables, DrivePersistence.StateName) == String.Empty, "reset state removes only owned key");
            Check(SavePersistence.Read(a.Variables, "NativeFuel") == "12.5", "reset preserves other variables");
            ca.Saved = "stale"; SavePersistence.EnsureString(a.Variables, DrivePersistence.StateName).Value = "stale";
            Load(a, null, false);
            Check(ca.Saved == String.Empty, "older save with missing key resets previous selection");
            Check(ca.Loads == 2, "restore clears before native load then restores after native load");
            Load(b, null, false);
            Check(b.Owner.gameObject.GetComponent<DriveControl>() == null, "loading untouched vehicle does not add a drive controller");
            Load(b, "v1|0|1", true);
            Check(b.Owner.gameObject.GetComponent<DriveControl>() == null, "old disabled vehicle record does not add an unnecessary controller");
            Load(a, "v1|1|0", true);
            Check(ca.Saved == "v1|1|0", "saved 2WD restores on its vehicle");
            Load(b, "v1|1|1", true);
            DriveControl cb = b.Owner.gameObject.GetComponent<DriveControl>();
            Check(cb != ca && cb.Saved == "v1|1|1" && ca.Saved == "v1|1|0", "two vehicle saves retain independent selection");
            int before = Plugin.Log.Errors; ca.Saved = new string('x', 129); Save(a, true);
            Check(Plugin.Log.Errors == before + 1 && SavePersistence.Read(a.Variables, DrivePersistence.StateName) == String.Empty, "oversized save is omitted and reported");
            Load(a, new string('x', 129), true);
            Check(ca.Saved == String.Empty && Plugin.Log.Errors == before + 2, "oversized load leaves native drive disabled");
            Save(item, true); Load(item, "1|1|1", true);
            Check(item.Owner.gameObject.GetComponent<DriveControl>() == null, "engine and ordinary item load cannot create drive control");
            Console.WriteLine("Vehicle drive persistence: " + checks + " assertions passed.");
        }
    }
}
