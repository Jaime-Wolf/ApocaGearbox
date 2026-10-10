using System;
using ApocaGearbox;
using NWH.VehiclePhysics2;
using NWH.VehiclePhysics2.Input;
using NWH.VehiclePhysics2.Powertrain;
using UnityEngine;

internal static class ThrottleProgram
{
    private static int passed;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); passed++; }
    private static bool Near(float a, float b) { return Math.Abs(a - b) < 0.0001f; }
    private static VehicleInputHandler Create(float seconds)
    {
        VehicleController v = new VehicleController();
        v.input = new VehicleInputHandler { vehicleController = v };
        GearboxState.Mounted[v.powertrain.engine] = new GearboxState { Vehicle = v, Tune = new TuneData { ThrottleRiseSeconds = seconds } };
        return v.input;
    }
    private static float Update(VehicleInputHandler input, float raw, float dt)
    {
        Time.frameCount++; Time.deltaTime = dt;
        ThrottleResponse.UpdateInput(input, raw);
        return ThrottleResponse.FilterInput(input, raw);
    }
    private static int Main()
    {
        try
        {
            foreach (float dt in new float[] { 0.01f, 0.02f, 0.1f }) {
                ThrottleRamp ramp = new ThrottleRamp();
                int steps = (int)Math.Round(0.5f / dt);
                for (int i = 0; i < steps; i++) ramp.Step(1f, 1f, dt);
                Check(Near(ramp.Value, 0.5f), "half-second response varies with frame rate");
                for (int i = 0; i < steps; i++) ramp.Step(1f, 1f, dt);
                Check(Near(ramp.Value, 1f), "full power was not reached after one second");
                Check(ramp.Step(0f, 1f, dt) == 0f, "release retained power");
                Check(Near(ramp.Step(1f, 1f, dt), dt), "tap restart retained previous throttle");
            }
            ThrottleRamp analog = new ThrottleRamp();
            Check(Near(analog.Step(0.4f, 1f, 0.5f), 0.4f), "analog target overshot");
            Check(Near(analog.Step(0.1f, 1f, 0.01f), 0.1f), "analog reduction was delayed");
            Check(Near(analog.Step(1f, 0f, 0.01f), 1f), "off setting delayed native throttle");
            Check(analog.Step(Single.NaN, 1f, 0.01f) == 0f, "invalid input retained throttle");
            Check(analog.Step(1f, 1f, Single.NaN) == 0f, "invalid timestep advanced throttle");

            VehicleInputHandler a = Create(1f), b = Create(2f);
            Check(Near(Update(a, 1f, 0.25f), 0.25f), "runtime first press bypassed ramp");
            for (int i = 0; i < 20; i++) Check(Near(ThrottleResponse.FilterInput(a, 1f), 0.25f), "physics reads accelerated ramp");
            ThrottleResponse.UpdateInput(a, 1f);
            Check(Near(ThrottleResponse.FilterInput(a, 1f), 0.25f), "duplicate input update advanced ramp");
            Check(a.Throttle == 1f && a.Brakes == 0.4f && a.Clutch == 0.7f, "raw pedals were modified");
            ThrottleResponse.UpdateInput(b, 1f);
            Check(Near(ThrottleResponse.FilterInput(b, 1f), 0.125f), "vehicle throttle states leaked");
            Check(Near(Update(a, 1f, 0.25f), 0.5f), "continuous input did not build throttle");
            Check(ThrottleResponse.FilterInput(a, 0f) == 0f, "release before next input update retained power");
            Check(Near(Update(a, 1f, 0.1f), 0.1f), "release did not reset buildup");
            Apocasetter.GameMenu.Paused = true;
            Check(ThrottleResponse.FilterInput(a, 1f) == 0f, "paused throttle remained active");
            Apocasetter.GameMenu.Paused = false;
            Check(Near(Update(a, 1f, 0.1f), 0.1f), "pause exit restored stale throttle");
            Application.isFocused = false;
            Check(ThrottleResponse.FilterInput(a, 1f) == 0f, "focus loss kept throttle");
            Application.isFocused = true;
            Check(Near(Update(a, 1f, 0.1f), 0.1f), "focus return kept stale throttle");
            Apocasetter.InputBlocker.Active = true;
            Check(ThrottleResponse.FilterInput(a, 1f) == 0f, "menu input block kept throttle");
            Apocasetter.InputBlocker.Active = false;
            Update(a, 1f, 0.1f);
            a.vehicleController.powertrain.transmission.Gear = -1;
            Check(ThrottleResponse.FilterInput(a, 1f) == 0f, "direction reversal kept forward throttle");
            Check(Near(Update(a, 1f, 0.1f), 0.1f), "reverse did not build throttle normally");
            Plugin.Enabled.Value = false;
            Check(ThrottleResponse.FilterInput(a, 1f) == 1f, "disabling did not restore native throttle");
            Plugin.Enabled.Value = true;
            Check(Near(Update(a, 1f, 0.1f), 0.1f), "reenabling kept stale power");
            GearboxState owner = GearboxState.Mounted[a.vehicleController.powertrain.engine];
            owner.Tune = owner.Tune.Clone(); owner.Tune.ThrottleRiseSeconds = 2f;
            Check(ThrottleResponse.FilterInput(a, 1f) == 0f, "changed tune used old throttle");
            Check(Near(Update(a, 1f, 0.1f), 0.05f), "new tune did not change rise time");
            owner.Tune.ThrottleRiseSeconds = 0f;
            Check(ThrottleResponse.FilterInput(a, 1f) == 1f, "disabled per-engine option retained ramp");
            owner.Tune.ThrottleRiseSeconds = 1f;
            Update(a, 1f, 0.1f);
            a.vehicleController.powertrain.engine = new EngineComponent();
            Check(ThrottleResponse.FilterInput(a, 1f) == 1f, "untuned replacement engine inherited throttle control");
            GearboxState.Mounted[a.vehicleController.powertrain.engine] = owner;
            Check(Near(Update(a, 1f, 0.1f), 0.1f), "replacement engine inherited accumulated throttle");
            Time.frameCount += 10;
            Check(ThrottleResponse.FilterInput(a, 1f) == 0f, "inactive vehicle retained power");
            Check(Near(Update(a, 1f, 0.1f), 0.1f), "wake-up bypassed buildup");
            ThrottleResponse.ResetAll();
            Check(ThrottleResponse.FilterInput(a, 1f) == 0f, "scene reset retained throttle");
            Check(ThrottleResponse.FilterInput(Create(0f), 0.67f) == 0.67f, "old tune changed native analog input");
            Check(Plugin.Log.Warnings == 0, "runtime filter threw exceptions");
            Console.WriteLine("Throttle response: " + passed + " checks passed."); return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }
}
