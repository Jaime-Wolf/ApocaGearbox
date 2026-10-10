using System;
using System.Collections.Generic;
using NWH.VehiclePhysics2;
using NWH.VehiclePhysics2.Powertrain;

namespace ApocaGearbox
{
    internal static class AssistProgram
    {
        private static int passed;
        private static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("FAILED: " + name);
            passed++;
        }
        private static void Near(float actual, float expected, string name)
        { Check(Math.Abs(actual - expected) < 0.001f, name + " (" + actual + " versus " + expected + ")"); }
        private static VehicleController Vehicle(float capacity, float peak, float configuredGain, float boost)
        {
            VehicleController vehicle = new VehicleController();
            vehicle.powertrain.engine = new EngineComponent { UnboostedPeakTorque = peak };
            vehicle.powertrain.engine.forcedInduction.useForcedInduction = true;
            vehicle.powertrain.engine.forcedInduction.powerGainMultiplier = configuredGain;
            vehicle.powertrain.engine.forcedInduction.boost = boost;
            vehicle.powertrain.clutch = new ClutchComponent { slipTorque = capacity };
            vehicle.powertrain.transmission = new TransmissionComponent();
            return vehicle;
        }
        private static TuneData Tune(float speedLimit)
        { return new TuneData { CompensateBoost = true, SpeedLimitKph = speedLimit }; }

        private static void Main()
        {
            VehicleController vehicle = Vehicle(700f, 500f, 2f, 0.5f);
            DrivingAssist assist = new DrivingAssist(vehicle);
            TuneData tune = Tune(0f);
            assist.Update(tune);
            Near(vehicle.powertrain.clutch.slipTorque, 1125f, "boost estimator not multiplied twice");
            assist.Update(tune);
            Near(vehicle.powertrain.clutch.slipTorque, 1125f, "repeated updates do not compound capacity");
            vehicle.powertrain.engine.forcedInduction.boost = 1f;
            assist.Update(tune);
            Near(vehicle.powertrain.clutch.slipTorque, 1500f, "live spool updates physical capacity");
            vehicle.powertrain.engine.forcedInduction.boost = 0f;
            assist.Update(tune);
            Near(vehicle.powertrain.clutch.slipTorque, 700f, "no boost restores native capacity");
            vehicle.powertrain.engine.forcedInduction.boost = 0.5f;
            assist.Update(tune);
            tune.CompensateBoost = false;
            assist.Update(tune);
            Near(vehicle.powertrain.clutch.slipTorque, 700f, "checkbox off restores native capacity");
            tune.CompensateBoost = true;
            assist.Update(tune);
            vehicle.powertrain.clutch.slipTorque = 950f;
            assist.Update(tune);
            Near(vehicle.powertrain.clutch.slipTorque, 1125f, "new native baseline still permits boost floor");
            assist.Restore();
            Near(vehicle.powertrain.clutch.slipTorque, 950f, "restore keeps latest observed native baseline");

            assist = new DrivingAssist(vehicle);
            assist.Update(tune);
            vehicle.powertrain.clutch.slipTorque = 875f;
            assist.Restore();
            Near(vehicle.powertrain.clutch.slipTorque, 875f, "restore does not overwrite an external intervening write");

            vehicle = Vehicle(2000f, 500f, 2f, 1f);
            assist = new DrivingAssist(vehicle);
            assist.Update(tune);
            Near(vehicle.powertrain.clutch.slipTorque, 2000f, "adequate native clutch never reduced or multiplied");
            assist.Restore();
            Near(vehicle.powertrain.clutch.slipTorque, 2000f, "unchanged native clutch survives restore");

            EngineComponent.PowerModifier other = delegate { return 0.8f; };
            vehicle.powertrain.engine.powerModifiers.Add(other);
            tune = Tune(100f);
            assist = new DrivingAssist(vehicle);
            assist.Update(tune);
            Check(vehicle.powertrain.engine.powerModifiers.Count == 2, "one speed modifier added beside existing modifier");
            assist.Update(tune);
            Check(vehicle.powertrain.engine.powerModifiers.Count == 2, "repeated updates do not duplicate speed modifier");
            EngineComponent.PowerModifier speed = vehicle.powertrain.engine.powerModifiers[1];
            vehicle.SpeedSigned = 95f / 3.6f;
            Near(speed(), 1f, "full power before final five kph");
            vehicle.SpeedSigned = 97.5f / 3.6f;
            Near(speed(), 0.5f, "power tapers inside final five kph");
            vehicle.SpeedSigned = 100f / 3.6f;
            Near(speed(), 0f, "power reaches zero at limit");
            vehicle.SpeedSigned = -100f / 3.6f;
            Near(speed(), 1f, "reverse power remains native");

            List<EngineComponent.PowerModifier> oldModifiers = vehicle.powertrain.engine.powerModifiers;
            vehicle.powertrain.engine.powerModifiers = new List<EngineComponent.PowerModifier> { other };
            assist.Update(tune);
            Check(oldModifiers.Count == 1 && oldModifiers[0] == other, "replaced list loses only owned modifier");
            Check(vehicle.powertrain.engine.powerModifiers.Count == 2, "replacement list receives exactly one speed modifier");
            assist.Restore();
            Check(vehicle.powertrain.engine.powerModifiers.Count == 1 && vehicle.powertrain.engine.powerModifiers[0] == other,
                "restore preserves every other power modifier");

            vehicle = Vehicle(700f, 500f, 2f, 0.5f);
            vehicle.powertrain.engine.powerModifiers.Add(other);
            assist = new DrivingAssist(vehicle);
            assist.Update(tune);
            EngineComponent oldEngine = vehicle.powertrain.engine;
            ClutchComponent oldClutch = vehicle.powertrain.clutch;
            vehicle.powertrain.engine = new EngineComponent { UnboostedPeakTorque = 300f };
            vehicle.powertrain.engine.forcedInduction.useForcedInduction = true;
            vehicle.powertrain.engine.forcedInduction.powerGainMultiplier = 2f;
            vehicle.powertrain.engine.forcedInduction.boost = 1f;
            vehicle.powertrain.clutch = new ClutchComponent { slipTorque = 500f };
            assist.Update(tune);
            Near(oldClutch.slipTorque, 700f, "changed clutch object restores previous component");
            Check(oldEngine.powerModifiers.Count == 1 && oldEngine.powerModifiers[0] == other,
                "changed engine object removes modifier from previous component");
            Near(vehicle.powertrain.clutch.slipTorque, 900f, "new engine computes its own boost capacity");
            Check(vehicle.powertrain.engine.powerModifiers.Count == 1, "new engine receives one limiter");
            assist.Restore();
            Near(vehicle.powertrain.clutch.slipTorque, 500f, "new engine baseline restores independently");
            Check(vehicle.powertrain.engine.powerModifiers.Count == 0, "new engine limiter removed on restore");

            assist = new DrivingAssist(null);
            assist.Update(tune);
            assist.Restore();
            Check(true, "null vehicle safely ignored");

            UnityEngine.Time.unscaledTime = 0f;
            vehicle = Vehicle(700f, 500f, 2f, 1f);
            assist = new DrivingAssist(vehicle);
            tune = Tune(0f);
            assist.Update(tune);
            for (int i = 0; i < 100; i++) assist.Update(tune);
            Check(vehicle.powertrain.engine.PeakReads == 1, "stable physics steps repeatedly sampled native curves");
            vehicle.powertrain.engine.maxPower = 150f;
            assist.Update(tune);
            Check(vehicle.powertrain.engine.PeakReads == 2, "power change did not refresh torque immediately");
            vehicle.powertrain.engine.revLimiterRPM = 5000f;
            assist.Update(tune);
            Check(vehicle.powertrain.engine.PeakReads == 3, "rev-limit change did not refresh torque immediately");
            vehicle.powertrain.engine.idleRPM = 800f;
            assist.Update(tune);
            Check(vehicle.powertrain.engine.PeakReads == 4, "idle change did not refresh torque immediately");
            vehicle.powertrain.engine.forcedInduction.powerGainMultiplier = 3f;
            assist.Update(tune);
            Check(vehicle.powertrain.engine.PeakReads == 5, "boost configuration change did not refresh torque immediately");
            Near(vehicle.powertrain.clutch.slipTorque, 2250f, "cached torque still counts configured boost once");
            vehicle.powertrain.engine.powerCurve = new object();
            assist.Update(tune);
            Check(vehicle.powertrain.engine.PeakReads == 6, "replacement curve did not refresh torque immediately");
            vehicle.powertrain.engine.UnboostedPeakTorque = 600f;
            UnityEngine.Time.unscaledTime = 0.49f;
            assist.Update(tune);
            Check(vehicle.powertrain.engine.PeakReads == 6, "stable inputs refreshed before bounded interval");
            UnityEngine.Time.unscaledTime = 0.5f;
            assist.Update(tune);
            Check(vehicle.powertrain.engine.PeakReads == 7, "in-place curve change was never refreshed");
            Near(vehicle.powertrain.clutch.slipTorque, 2700f, "bounded refresh did not update actual clutch support");
            Console.WriteLine("Passed " + passed + " DrivingAssist ownership and physics contract assertions.");
        }
    }
}
