using System;
using ApocaGearbox;
using HutongGames.PlayMaker;
using NWH.VehiclePhysics2;
using UnityEngine;

internal static class SelectionProgram
{
    private static int passed, failed;
    private sealed class World
    {
        internal GameObject Engine, Gearbox, Mount, Vehicle, Camera;
        internal VehicleController Controller;
        internal PlayMakerFSM Adjust, Drive, Grab, Check, Ratio, Id;
    }
    private static PlayMakerFSM Fsm(GameObject owner, string name)
    { PlayMakerFSM fsm = owner.AddComponent<PlayMakerFSM>(); fsm.FsmName = name; return fsm; }
    private static World BuildWorld()
    {
        Time.unscaledTime += 2f; Time.timeScale = 1f; Application.isFocused = true; Cursor.lockState = CursorLockMode.Locked;
        World w = new World();
        w.Camera = new GameObject("PlayerCamera"); w.Camera.AddComponent<Camera>();
        w.Adjust = Fsm(w.Camera, "UseAdjustTool"); w.Drive = Fsm(w.Camera, "DriveUse"); w.Grab = Fsm(w.Camera, "GrabItem");
        FsmVariables.GlobalVariables.Objects["PlayerCamera"] = new FsmGameObject { Value = w.Camera };
        w.Vehicle = new GameObject("Car"); w.Controller = w.Vehicle.AddComponent<VehicleController>();
        w.Mount = new GameObject("hinge_engine"); w.Mount.transform.parent = w.Vehicle.transform;
        w.Engine = new GameObject("Engine V8(Clone)"); w.Engine.tag = "vehPart"; w.Engine.transform.parent = w.Mount.transform;
        w.Id = Fsm(w.Engine, "ID"); w.Id.FsmVariables.Strings["ID"] = new FsmString { Value = "engine" };
        w.Check = Fsm(w.Engine, "CheckTag"); w.Check.ActiveStateName = "vehPart";
        w.Check.FsmVariables.Objects["vehicle"] = new FsmGameObject { Value = w.Vehicle };
        w.Gearbox = new GameObject("transmission"); w.Gearbox.transform.parent = w.Engine.transform;
        w.Ratio = Fsm(w.Gearbox, "FinalDriveRatio");
        ActionHelpers.PickResult = new RaycastHit { collider = w.Gearbox.AddComponent<Collider>(), distance = 1f };
        return w;
    }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Run(string name, Action<World> test)
    {
        try { test(BuildWorld()); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }
    private static void AssertNoAim()
    {
        GameObject engine; VehicleController vehicle; string label;
        Assert(!GearboxTarget.TryAim(out engine, out vehicle, out label), "Unsafe target accepted.");
        Assert(engine == null && vehicle == null && label == null, "Rejected target returned live engine data.");
    }
    private static void AssertAim(World w, VehicleController expectedVehicle)
    {
        GameObject engine; VehicleController vehicle; string label;
        Assert(GearboxTarget.TryAim(out engine, out vehicle, out label), "Tunable engine gearbox was rejected.");
        Assert(engine == w.Engine && vehicle == expectedVehicle && label == "Engine V8", "Wrong selected engine or mounted vehicle.");
    }
    private static int Main()
    {
        Run("installed engine gearbox accepted", delegate(World w) {
            GameObject engine; VehicleController vehicle; string label;
            Assert(GearboxTarget.TryAim(out engine, out vehicle, out label), "Valid target rejected.");
            Assert(engine == w.Engine && vehicle == w.Controller && label == "Engine V8", "Wrong selected engine or vehicle.");
            Assert(ActionHelpers.RequestedReach == 2f && ActionHelpers.RequestedMask == 66305, "Native pick contract changed.");
        });
        Run("cargo engine can be tuned without altering cargo vehicle", delegate(World w) {
            w.Mount.name = "CargoMount";
            Assert(GearboxTarget.InstalledVehicle(w.Engine) == null, "Cargo mistaken for installed engine."); AssertAim(w, null);
        });
        Run("loose engine can be tuned before installation", delegate(World w) {
            w.Engine.transform.parent = null; w.Engine.tag = "item";
            w.Check.ActiveStateName = "vehPartRemoved"; w.Ratio.isActiveAndEnabled = false; w.Ratio.ActiveStateName = "over";
            AssertAim(w, null);
        });
        Run("noninstalled engine tag prevents runtime vehicle binding", delegate(World w) {
            w.Engine.tag = "item";
            Assert(GearboxTarget.InstalledVehicle(w.Engine) == null, "Noninstalled engine bound to a vehicle."); AssertAim(w, null);
        });
        Run("nonengine ID excluded", delegate(World w) { w.Id.FsmVariables.Strings["ID"].Value = "wheel"; AssertNoAim(); });
        Run("mismatched CheckTag vehicle cannot bind runtime tune", delegate(World w) {
            w.Check.FsmVariables.Objects["vehicle"].Value = new GameObject("Other car");
            Assert(GearboxTarget.InstalledVehicle(w.Engine) == null, "Engine bound to a mismatched vehicle."); AssertAim(w, null);
        });
        Run("disabled mounted check cannot bind runtime tune", delegate(World w) {
            w.Check.isActiveAndEnabled = false;
            Assert(GearboxTarget.InstalledVehicle(w.Engine) == null, "Disabled native mounting check bound a vehicle."); AssertAim(w, null);
        });
        Run("native state name does not hide physically mounted gearbox", delegate(World w) {
            w.Check.ActiveStateName = "vehPartRemoved"; w.Ratio.ActiveStateName = "over";
            Assert(GearboxTarget.InstalledVehicle(w.Engine) == w.Controller, "Physical mount ignored because of state name."); AssertAim(w, w.Controller);
        });
        Run("missing native vehicle variable cannot bind runtime tune", delegate(World w) {
            w.Check.FsmVariables.Objects.Remove("vehicle");
            Assert(GearboxTarget.InstalledVehicle(w.Engine) == null, "Missing native vehicle link was accepted."); AssertAim(w, null);
        });
        Run("uninitialized native vehicle link cannot bind runtime tune", delegate(World w) {
            w.Check.ActiveStateName = "start"; w.Check.FsmVariables.Objects["vehicle"].Value = null;
            Assert(GearboxTarget.InstalledVehicle(w.Engine) == null, "Uninitialized native vehicle link was accepted."); AssertAim(w, null);
        });
        Run("inactive vehicle cannot receive runtime tune", delegate(World w) {
            w.Vehicle.activeInHierarchy = false; Assert(GearboxTarget.InstalledVehicle(w.Engine) == null, "Inactive vehicle accepted.");
        });
        Run("engine body collider is an alternate access point", delegate(World w) {
            ActionHelpers.PickResult.collider=w.Engine.AddComponent<Collider>(); AssertAim(w,w.Controller);
        });
        Run("engine child collider is an alternate access point", delegate(World w) { w.Gearbox.name="engineBlock"; AssertAim(w,w.Controller); });
        Run("loose engine body selects no vehicle", delegate(World w) {
            w.Engine.transform.parent=null;w.Engine.tag="item";ActionHelpers.PickResult.collider=w.Engine.AddComponent<Collider>();AssertAim(w,null);
        });
        Run("chassis collider does not select its installed engine", delegate(World w) { ActionHelpers.PickResult.collider=w.Vehicle.AddComponent<Collider>();AssertNoAim(); });
        Run("nested gearbox under wrong parent excluded", delegate(World w) {
            GameObject wrapper = new GameObject("decorative wrapper"); wrapper.transform.parent = w.Engine.transform;
            w.Gearbox.transform.parent = wrapper.transform; AssertNoAim();
        });
        Run("disabled gearbox FSM still identifies tunable engine", delegate(World w) { w.Ratio.isActiveAndEnabled = false; AssertAim(w, w.Controller); });
        Run("missing gearbox FSM excludes unrelated transmission collider", delegate(World w) { w.Ratio.FsmName = "OtherFSM"; AssertNoAim(); });
        Run("empty raycast excluded", delegate(World w) { ActionHelpers.PickResult = new RaycastHit(); AssertNoAim(); });
        Run("out of reach gearbox excluded", delegate(World w) { ActionHelpers.PickResult.distance = 2.01f; AssertNoAim(); });
        Run("unselected adjuster excluded", delegate(World w) { w.Adjust.isActiveAndEnabled = false; AssertNoAim(); });
        Run("ongoing native adjustment excluded", delegate(World w) { w.Adjust.ActiveStateName = "adjust"; AssertNoAim(); });
        Run("driving input excluded", delegate(World w) { w.Drive.ActiveStateName = "Driving"; AssertNoAim(); });
        Run("holding an item excluded", delegate(World w) { w.Grab.ActiveStateName = "ItemInHand"; AssertNoAim(); });
        Run("paused game excluded", delegate(World w) { Time.timeScale = 0f; AssertNoAim(); });
        Run("unfocused app excluded", delegate(World w) { Application.isFocused = false; AssertNoAim(); });
        Run("unlocked cursor excluded", delegate(World w) { Cursor.lockState = CursorLockMode.None; AssertNoAim(); });
        Run("unchanged prompt aim avoids component-array searches", delegate(World w) {
            Assert(GearboxTarget.HasAim(), "Initial prompt aim failed.");
            int engineReads=w.Engine.ComponentArrayReads,cameraReads=w.Camera.ComponentArrayReads,gearboxReads=w.Gearbox.ComponentArrayReads;
            for(int i=0;i<20;i++) Assert(GearboxTarget.HasAim(), "Cached prompt aim failed.");
            Assert(w.Engine.ComponentArrayReads==engineReads&&w.Camera.ComponentArrayReads==cameraReads&&w.Gearbox.ComponentArrayReads==gearboxReads,"Repeated prompt aim allocated component arrays.");
        });
        Run("cached aim immediately rejects changed engine identity", delegate(World w) {
            Assert(GearboxTarget.HasAim(), "Initial prompt aim failed.");
            w.Id.FsmVariables.Strings["ID"].Value="wheel";
            Assert(!GearboxTarget.HasAim(), "Cached engine identity was not checked.");
        });
        Run("cached transmission rejects changed ratio FSM", delegate(World w) {
            Assert(GearboxTarget.HasAim(), "Initial prompt aim failed.");
            w.Ratio.FsmName="OtherFSM";
            Assert(!GearboxTarget.HasAim(), "Cached gearbox no longer has its native ratio FSM.");
        });
        Run("cached aim revalidates changed collider parent", delegate(World w) {
            Assert(GearboxTarget.HasAim(), "Initial prompt aim failed.");
            w.Gearbox.transform.parent=w.Vehicle.transform;
            Assert(!GearboxTarget.HasAim(), "Reparented gearbox retained old owner.");
        });
        Run("cached aim rejects ancestor reparenting away from engine", delegate(World w) {
            w.Gearbox.name="engineBlock";
            GameObject wrapper=new GameObject("wrapper");wrapper.transform.parent=w.Engine.transform;w.Gearbox.transform.parent=wrapper.transform;
            Assert(GearboxTarget.HasAim(), "Initial nested engine-body aim failed.");
            wrapper.transform.parent=w.Vehicle.transform;
            Assert(!GearboxTarget.HasAim(), "Changed ancestor retained stale engine owner.");
        });
        Run("surviving camera owner picks replacement FSM and camera", delegate(World w) {
            Assert(GearboxTarget.HasAim(), "Initial prompt aim failed.");
            w.Drive.ActiveStateName="Driving";
            Fsm(w.Camera,"DriveUse").ActiveStateName="Idle";
            w.Camera.GetComponent<Camera>().isActiveAndEnabled=false;
            w.Camera.AddComponent<Camera>();
            Time.unscaledTime+=0.3f;
            Assert(GearboxTarget.HasAim(), "Surviving owner kept old FSM/camera bindings.");
        });
        Run("camera refresh does not ignore changed live replacement state", delegate(World w) {
            Assert(GearboxTarget.HasAim(), "Initial prompt aim failed.");
            Fsm(w.Camera,"DriveUse").ActiveStateName="Driving";
            Time.unscaledTime+=0.3f;
            Assert(!GearboxTarget.HasAim(), "Replacement driving FSM was ignored.");
        });
        Console.WriteLine("RESULT: " + passed + " passed, " + failed + " failed.");
        return failed == 0 ? 0 : 1;
    }
}
