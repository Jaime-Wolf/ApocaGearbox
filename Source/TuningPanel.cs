using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BepInEx.Configuration;
using NWH.VehiclePhysics2;
using NWH.VehiclePhysics2.Powertrain;
using UnityEngine;

namespace ApocaGearbox
{
    internal static class TuningPanel
    {
        internal static bool IsOpen;
        private static GameObject engine;
        private static VehicleController vehicle;
        private static GearboxState state;
        private static TuneData draft;
        private static string title;
        private static string[] ratios;
        private static string finalDrive, shiftUp, shiftDown, speedLimit, engineBraking, lowRange, throttleRise, revLimit;
        private static string brakeStrength, handbrakeStrength;
        private static bool revLimitEnabled;
        private static float factoryRedline;
        private static bool limitSpeed;
        private static bool engineBrakingEnabled;
        private static bool throttleResponseEnabled;
        private static bool driveEnabled;
        private static bool calculatorOpen, calculatorFinalDrive = true, calculatorShifts;
        private static int calculatorProfile = 1;
        private static string calculatorSpeed = "200", calculatorError;
        private static string previewSpeed, previewLaunch, previewRevLimit;
        private static int previewProfile;
        private static bool previewFinalDrive, previewRevLimitEnabled, previewShifts;
        private static VehicleController previewVehicle;
        private static readonly string[] CalculatorProfiles = { "Gentle", "Balanced", "Pulling" };
        private static readonly CalculatorFreshnessCache calculatorFreshness = new CalculatorFreshnessCache();
        private static string[] calculatorGearLabels;
        private static string calculatorFinalDriveLabel, calculatorShiftLabel;
        private static string calculatorLaunch = "100";
        private static CalculatorSnapshot calculatorSnapshot;
        private static CalculatorResult calculatorResult;
        private static TuneData calculatorSource;
        private static VehicleController calculatorVehicle;
        private static VehicleController[] nearbyVehicles;
        private static string error;
        private static string presetName = "My tune";
        private static string presetMessage;
        private static string pendingPresetKey, pendingPresetValue;
        private static TuneData pendingTune;
        private static string pendingTuneMessage;
        private static Vector2 scroll;
        private static CursorLockMode previousLock;
        private static bool previousVisible;
        private static Rect window;
        private const int WindowId = 414676061;

        internal static bool TargetStillAvailable()
        { return engine != null && engine.activeInHierarchy && GearboxTarget.IsEngine(engine); }

        internal static void Open(GameObject target, VehicleController mounted, string label)
        {
            if (IsOpen || target == null) return;
            state = GearboxState.GetOrCreate(target);
            if (state == null) return;
            engine = target; vehicle = mounted; title = label;
            float nativeIdle;
            if (!CalculatorEngineSpecs.ReadRpm(target, out nativeIdle, out factoryRedline))
                factoryRedline = mounted != null && mounted.powertrain != null && mounted.powertrain.engine != null ? mounted.powertrain.engine.revLimiterRPM : 4500f;
            DriveControl existingDrive;
            driveEnabled = vehicle != null && DriveControl.TryGet(vehicle, out existingDrive) && existingDrive.Enabled;
            state.RefreshLegacyFinalDrive();
            TransmissionComponent transmission = vehicle != null && vehicle.powertrain != null ? vehicle.powertrain.transmission : null;
            draft = GearboxNativeDefaults.Capture(target, transmission);
            string layoutError;
            if (draft == null || !draft.Validate(null, out layoutError))
            { Plugin.Log.LogWarning("This gearbox has an unsupported native layout."); engine = null; vehicle = null; state = null; draft = null; return; }
            error = null; presetMessage = null; scroll = Vector2.zero;
            if (transmission != null && transmission.transmissionType == TransmissionComponent.TransmissionShiftType.CVT)
                error = "This vehicle uses a continuously variable gearbox. Its ratios cannot be tuned here.";
            if (state.Tune != null && state.Tune.Validate(draft.Ratios, out error)) draft = state.Tune.Clone();
            SetFields();
            calculatorOpen = false; calculatorResult = null; calculatorSnapshot = null; calculatorSource = null; calculatorError = null;
            calculatorFreshness.Clear(); calculatorGearLabels = null;
            calculatorVehicle = null; nearbyVehicles = null;
            calculatorSpeed = (draft.SpeedLimitKph > 0f ? draft.SpeedLimitKph : 200f).ToString("0.###", CultureInfo.InvariantCulture);
            previousLock = Cursor.lockState; previousVisible = Cursor.visible;
            GearboxPrompt.Clear();
            Apocasetter.InputBlocker.Set(true);
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            window = new Rect((Screen.width - 690) / 2f, (Screen.height - 650) / 2f, 690, 650);
            IsOpen = true;
        }

        private static void SetFields()
        {
            ratios = new string[draft.Ratios.Length];
            for (int i = 0; i < ratios.Length; i++) ratios[i] = Math.Abs(draft.Ratios[i]).ToString("R", CultureInfo.InvariantCulture);
            finalDrive = draft.FinalDrive.ToString("R", CultureInfo.InvariantCulture);
            shiftUp = draft.ShiftUp.ToString("0", CultureInfo.InvariantCulture);
            shiftDown = draft.ShiftDown.ToString("0", CultureInfo.InvariantCulture);
            limitSpeed = draft.SpeedLimitKph > 0f;
            speedLimit = (limitSpeed ? draft.SpeedLimitKph : 150f).ToString("0.###", CultureInfo.InvariantCulture);
            engineBrakingEnabled = draft.EngineBrakingStrength > 0f;
            engineBraking = (engineBrakingEnabled ? draft.EngineBrakingStrength : 2f).ToString("0.###", CultureInfo.InvariantCulture);
            brakeStrength = draft.BrakeStrength.ToString("R", CultureInfo.InvariantCulture);
            handbrakeStrength = draft.HandbrakeStrength.ToString("R", CultureInfo.InvariantCulture);
            throttleResponseEnabled = draft.ThrottleRiseSeconds > 0f;
            throttleRise = (throttleResponseEnabled ? draft.ThrottleRiseSeconds : 0.75f).ToString("R", CultureInfo.InvariantCulture);
            revLimitEnabled = draft.RevLimitRpm > 0f;
            revLimit = (revLimitEnabled ? draft.RevLimitRpm : factoryRedline).ToString("0", CultureInfo.InvariantCulture);
            lowRange = draft.LowRangeRatio.ToString("R", CultureInfo.InvariantCulture);
            // Older tunes have no selector preference. Keep the current car's
            // availability (or the current edit when loading an old preset).
            if (draft.DriveSelectorEnabled.HasValue) driveEnabled = draft.DriveSelectorEnabled.Value;
        }

        internal static void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            Apocasetter.InputBlocker.Set(false);
            Cursor.lockState = previousLock; Cursor.visible = previousVisible;
            GearboxRunner.ConsumedFrame = Time.frameCount;
            engine = null; vehicle = null; state = null; draft = null; ratios = null;
            pendingTune = null; pendingTuneMessage = null;
            calculatorResult = null; calculatorSnapshot = null; calculatorSource = null;
            calculatorFreshness.Clear(); calculatorGearLabels = null; previewVehicle = null;
            calculatorVehicle = null; nearbyVehicles = null;
        }

        internal static void Tick()
        {
            if (!String.IsNullOrEmpty(pendingPresetKey))
            {
                Plugin.Settings.Bind("Presets", pendingPresetKey, String.Empty, "Gearbox preset saved from the tuning panel.").Value = pendingPresetValue;
                pendingPresetKey = pendingPresetValue = null;
            }
            if (pendingTune != null && IsOpen)
            {
                draft = pendingTune; pendingTune = null;
                SetFields(); error = null; presetMessage = pendingTuneMessage;
                pendingTuneMessage = null;
            }
        }

        internal static void Draw()
        {
            if (!IsOpen || draft == null) return;
            GUISkin previousSkin = GUI.skin;
            int previousDepth = GUI.depth;
            try
            {
                Apocasetter.Theme.Apply(); GUI.depth = -500;
                // Scale the window to fit small screens without losing the buttons.
                float width = Mathf.Min(690f, Screen.width - 24f);
                float height = Mathf.Min(650f, Screen.height - 24f);
                window = new Rect(Mathf.Clamp(window.x, 12f, Mathf.Max(12f, Screen.width - width - 12f)), Mathf.Clamp(window.y, 12f, Mathf.Max(12f, Screen.height - height - 12f)), width, height);
                window = GUILayout.Window(WindowId, window, DrawContents, "GEARBOX TUNING", GUILayout.Width(width), GUILayout.Height(height));
            }
            catch (Exception exception)
            { Plugin.Log.LogError("Gearbox panel failed: " + exception); Close(); }
            finally { GUI.skin = previousSkin; GUI.depth = previousDepth; }
        }

        private static void DrawContents(int id)
        {
            GUILayout.Label(title, Apocasetter.Theme.Header ?? GUI.skin.label);
            if (GearboxTarget.InstalledVehicle(engine) == null)
                GUILayout.Label("This tune will apply when you install the engine.", Apocasetter.Theme.SubLabel ?? GUI.skin.label);
            GUILayout.Label("Higher ratios give more pulling power. Lower ratios give longer gears.", Apocasetter.Theme.SubLabel ?? GUI.skin.label);
            GUILayout.Space(6);
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
            DrawCalculator();
            GUILayout.Label("INDIVIDUAL GEARS");
            int reverseNumber = 0, forwardNumber = 0;
            for (int i = 0; i < draft.Ratios.Length; i++)
            {
                if (draft.Ratios[i] == 0f) continue;
                string label = draft.Ratios[i] < 0f ? "Reverse " + (++reverseNumber) : "Gear " + (++forwardNumber);
                NumberRow(label, ref ratios[i], 0.05f, 8f, 0.01f);
                if (draft.Ratios[i] > 0f && i + 1 < ratios.Length && draft.Ratios[i + 1] > 0f)
                {
                    float from, to;
                    if (TuneData.ReadFloat(ratios[i], out from) && TuneData.ReadFloat(ratios[i + 1], out to) && from > 0f && to > 0f)
                        GUILayout.Label("Next gear: 3200 RPM becomes about " + (3200f * to / from).ToString("0", CultureInfo.InvariantCulture) + " RPM.", Apocasetter.Theme.SubLabel ?? GUI.skin.label);
                }
            }
            GUILayout.Space(8);
            GUILayout.Label("FINAL DRIVE");
            NumberRow("Final drive", ref finalDrive, 0.05f, 15f, 0.01f);
            GUILayout.Label("Final drive changes every gear together. Neutral stays unchanged.", Apocasetter.Theme.SubLabel ?? GUI.skin.label);
            GUILayout.Space(10);
            GUILayout.Label("HIGH / LOW RANGE");
            draft.LowRangeEnabled = GUILayout.Toggle(draft.LowRangeEnabled, "Enable the dashboard high / low switch");
            if (draft.LowRangeEnabled)
            {
                NumberRow("Low reduction", ref lowRange, 1f, 5f, 0.1f, 10f);
                GUILayout.Label("High uses normal gearing. Low gives more pulling power in every gear, including reverse.", Apocasetter.Theme.SubLabel ?? GUI.skin.label);
                GUILayout.Label("Use the switch just left of the lights switch while stopped and in neutral.", Apocasetter.Theme.SubLabel ?? GUI.skin.label);
            }
            GUILayout.Space(10);
            GUILayout.Label("2WD / 4WD SELECTOR");
            driveEnabled = GUILayout.Toggle(driveEnabled, "Enable 2WD / 4WD selector");
            GUILayout.Label("Allows a 4WD conversion without extra parts. This option saves with the engine and activates when installed.", Apocasetter.Theme.SubLabel ?? GUI.skin.label);
            if (driveEnabled)
            {
                GUILayout.Label("Left-click the spare switch just left of high / low while stopped and in neutral. The selected mode saves with the vehicle.", Apocasetter.Theme.SubLabel ?? GUI.skin.label);
                GUILayout.Label("2WD uses the normal driven wheels, or the rear axle on a factory 4WD vehicle. 4WD powers all four wheels.", Apocasetter.Theme.SubLabel ?? GUI.skin.label);
            }
            GUILayout.Space(10);
            GUILayout.Label("DRIVING SUPPORT");
            NumberRow("Brake strength", ref brakeStrength, 0.1f, 5f, 0.1f, 5f);
            NumberRow("Handbrake strength", ref handbrakeStrength, 0.1f, 5f, 0.1f, 5f);
            GUILayout.Label("1 = normal, 2 = double. Saved with this engine's tune.", Apocasetter.Theme.SubLabel ?? GUI.skin.label);
            revLimitEnabled = GUILayout.Toggle(revLimitEnabled, "Set engine rev limit");
            if (revLimitEnabled)
            {
                NumberRow("Rev limit (RPM)", ref revLimit, 1000f, 10000f, 100f, 15000f);
                GUILayout.Label("Changes the engine's actual rev limiter. Factory limit: " + Format(factoryRedline) + " RPM. Saves with this engine and presets.", Apocasetter.Theme.SubLabel ?? GUI.skin.label);
                GUILayout.Label("Automatic upshift RPM must stay below the limit. With native shift settings, the mod lowers shift thresholds when needed. Manual shifts remain unchanged.", Apocasetter.Theme.SubLabel ?? GUI.skin.label);
            }
            throttleResponseEnabled = GUILayout.Toggle(throttleResponseEnabled, "Gradual throttle response");
            if (throttleResponseEnabled)
            {
                NumberRow("Buildup (sec)", ref throttleRise, 0.1f, 3f, 0.05f, 3f);
                GUILayout.Label("Hold the accelerator to build power. More seconds means a gentler launch; releasing cuts throttle immediately.", Apocasetter.Theme.SubLabel ?? GUI.skin.label);
            }
            draft.CompensateBoost = GUILayout.Toggle(draft.CompensateBoost, "Match clutch capacity to turbo / supercharger power");
            engineBrakingEnabled = GUILayout.Toggle(engineBrakingEnabled, "Stronger engine braking when lifting off");
            if (engineBrakingEnabled)
            {
                NumberRow("Strength", ref engineBraking, 0f, 5f, 0.25f, 5f);
                GUILayout.Label("Higher strength gives more slowing in gear. Pressing the clutch or selecting neutral lets you coast.", Apocasetter.Theme.SubLabel ?? GUI.skin.label);
            }
            limitSpeed = GUILayout.Toggle(limitSpeed, "Limit powered road speed");
            if (limitSpeed)
            {
                NumberRow("Limit (km/h)", ref speedLimit, 10f, 250f, 10f, 500f);
                GUILayout.Label("Eases off engine power near the limit. Braking and downhill coasting stay under your control.", Apocasetter.Theme.SubLabel ?? GUI.skin.label);
            }
            GUILayout.Space(10);
            draft.TuneShifts = GUILayout.Toggle(draft.TuneShifts, "Set automatic shift RPM");
            if (draft.TuneShifts)
            {
                NumberRow("Upshift RPM", ref shiftUp, 100f, 10000f, 100f, 30000f);
                NumberRow("Downshift RPM", ref shiftDown, 0f, 10000f, 100f, 30000f);
                GUILayout.Label("Upshift must be higher than downshift. These values affect automatic shifting.", Apocasetter.Theme.SubLabel ?? GUI.skin.label);
            }
            GUILayout.Space(12);
            GUILayout.Label("PRESETS");
            if (title.StartsWith("5.2L V8 ", StringComparison.Ordinal) && draft.Ratios.Length == 7 && draft.Ratios[0] < 0f && draft.Ratios[1] == 0f)
            {
                if (GUILayout.Button("Load Pigpen V8 road tune - 150 km/h"))
                {
                    pendingTune = draft.Clone();
                    pendingTune.DriveSelectorEnabled = driveEnabled;
                    pendingTune.Ratios = new float[] { -3.25f, 0f, 2.80f, 2.00f, 1.45f, 1.05f, 0.78f };
                    pendingTune.FinalDrive = 3.40f; pendingTune.TuneShifts = false;
                    pendingTune.CompensateBoost = true;
                    pendingTune.SpeedLimitKph = 150f;
                    pendingTune.EngineBrakingStrength = 2f;
                    pendingTuneMessage = "Road gearing, engine braking and a 150 km/h power limit loaded. Apply to keep this tune.";
                }
            }
            GUILayout.BeginHorizontal();
            presetName = GUILayout.TextField(presetName, 48, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Save preset", GUILayout.Width(120))) SavePreset();
            GUILayout.EndHorizontal();
            foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> item in Plugin.Settings)
            {
                if (item.Key.Section != "Presets") continue;
                GUILayout.BeginHorizontal();
                GUILayout.Label(item.Key.Key, GUILayout.ExpandWidth(true));
                if (GUILayout.Button("Load", GUILayout.Width(80))) LoadPreset(item.Value.BoxedValue as string);
                GUILayout.EndHorizontal();
            }
            if (!String.IsNullOrEmpty(presetMessage)) GUILayout.Label(presetMessage);
            GUILayout.EndScrollView();
            if (!String.IsNullOrEmpty(error)) GUILayout.Label(error);
            if (!String.IsNullOrEmpty(state.Status)) GUILayout.Label(state.Status);
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset to original", GUILayout.Height(35)))
            {
                string resetError;
                if (state.Reset(vehicle, out resetError)) Close(); else error = resetError;
            }
            if (GUILayout.Button("Cancel", GUILayout.Height(35))) Close();
            if (GUILayout.Button("Apply and close", GUILayout.Height(35))) ApplyAndClose();
            GUILayout.EndHorizontal();
            GUI.DragWindow(new Rect(0, 0, window.width, 28));
        }

        private static void NumberRow(string label, ref string text, float minimum, float sliderMaximum, float step, float hardMaximum = 100f)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(118));
            float value;
            bool valid = TuneData.ReadFloat(text, out value);
            if (GUILayout.Button("-", GUILayout.Width(32)) && valid) text = Format(Mathf.Max(minimum, value - step));
            text = GUILayout.TextField(text, 24, GUILayout.Width(100));
            if (GUILayout.Button("+", GUILayout.Width(32)) && valid) text = Format(Mathf.Min(hardMaximum, value + step));
            valid = TuneData.ReadFloat(text, out value);
            if (valid)
            {
                float maximum = Mathf.Max(sliderMaximum, value);
                float selected = GUILayout.HorizontalSlider(value, minimum, maximum, GUILayout.ExpandWidth(true));
                if (!Mathf.Approximately(selected, value)) text = Format(step >= 100f ? Mathf.Round(selected / 100f) * 100f : Mathf.Round(selected * 100f) / 100f);
            }
            else GUILayout.Label("Enter a number", GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();
        }

        private static void RememberCalculatorInputs()
        {
            previewSpeed = calculatorSpeed; previewLaunch = calculatorLaunch; previewRevLimit = revLimit;
            previewProfile = calculatorProfile; previewFinalDrive = calculatorFinalDrive;
            previewRevLimitEnabled = revLimitEnabled; previewShifts = calculatorShifts; previewVehicle = calculatorVehicle;
        }

        private static bool CalculatorInputsMatch()
        { return previewSpeed == calculatorSpeed && previewLaunch == calculatorLaunch && previewRevLimit == revLimit &&
            previewProfile == calculatorProfile && previewFinalDrive == calculatorFinalDrive && previewRevLimitEnabled == revLimitEnabled &&
            previewShifts == calculatorShifts && previewVehicle == calculatorVehicle; }

        private static bool CalculatorDraftMatches()
        {
            float value;
            if (calculatorSource == null || !TuneData.ReadFloat(finalDrive, out value) || value != calculatorSource.FinalDrive) return false;
            for (int i = 0; i < ratios.Length; i++)
                if (draft.Ratios[i] != 0f && (!TuneData.ReadFloat(ratios[i], out value) || value != Math.Abs(calculatorSource.Ratios[i]))) return false;
            return true;
        }

        private static void DrawCalculator()
        {
            calculatorOpen = GUILayout.Toggle(calculatorOpen, "Show optional gear calculator");
            if (!calculatorOpen) { GUILayout.Space(8); return; }
            if (GearboxTarget.InstalledVehicle(engine) == null) DrawNearbyVehicle();
            GUILayout.Label("Engine and fitted turbo/supercharger are detected automatically. Choose a target speed and driving style, then preview the suggestion.", Apocasetter.Theme.SubLabel ?? GUI.skin.label);
            NumberRow("Target (km/h)", ref calculatorSpeed, 20f, 300f, 10f, 500f);
            calculatorProfile = GUILayout.Toolbar(calculatorProfile, CalculatorProfiles);
            NumberRow("Launch gearing (%)", ref calculatorLaunch, 50f, 150f, 5f, 200f);
            GUILayout.Label("100% is the recommended starting point. Raise it for higher ratios in the lower gears; top gear keeps the chosen target speed.", Apocasetter.Theme.SubLabel ?? GUI.skin.label);
            calculatorFinalDrive = GUILayout.Toggle(calculatorFinalDrive, "Calculate final drive too");
            calculatorShifts = GUILayout.Toggle(calculatorShifts, "Use suggested automatic shift RPM too");
            if (GUILayout.Button("Calculate suggested gearing")) CalculateGearing();
            if (!String.IsNullOrEmpty(calculatorError)) GUILayout.Label(calculatorError);
            if (calculatorResult != null && calculatorSnapshot != null)
            {
                GUILayout.Label(calculatorSnapshot.EngineDescription);
                GUILayout.Label(calculatorSnapshot.VehicleDescription);
                if (calculatorFreshness.Due(Time.unscaledTime))
                    calculatorFreshness.Record(Time.unscaledTime, GearCalculator.StillMatches(engine, calculatorSnapshot));
                bool fresh = CalculatorInputsMatch() && CalculatorDraftMatches() && calculatorFreshness.Matches;
                GUILayout.Label(calculatorFinalDriveLabel);
                foreach (string gearLabel in calculatorGearLabels) GUILayout.Label(gearLabel);
                GUILayout.Label(calculatorShiftLabel);
                GUILayout.Label("Calculated for HIGH range. Reverse ratios stay as set. Target speed is gearing, not a guarantee of road speed.", Apocasetter.Theme.SubLabel ?? GUI.skin.label);
                if (!String.IsNullOrEmpty(calculatorResult.Warning)) GUILayout.Label(calculatorResult.Warning);
                GUILayout.Label(calculatorSnapshot.Notes, Apocasetter.Theme.SubLabel ?? GUI.skin.label);
                if (!fresh) GUILayout.Label("The inputs changed. Calculate again before using this suggestion.");
                bool enabledBefore = GUI.enabled; GUI.enabled = enabledBefore && fresh;
                if (GUILayout.Button("Use these ratios")) UseCalculatedGearing();
                GUI.enabled = enabledBefore;
            }
            GUILayout.Space(12);
        }

        private static void CalculateGearing()
        {
            calculatorResult = null; calculatorSnapshot = null; calculatorSource = null; calculatorError = null;
            calculatorFreshness.Clear(); calculatorGearLabels = null;
            try
            {
                TuneData source; float target, launch;
                if (!ReadDraft(out source, calculatorShifts)) { calculatorError = error; return; }
                if (!TuneData.ReadFloat(calculatorSpeed, out target)) { calculatorError = "Enter a valid target speed."; return; }
                if (!TuneData.ReadFloat(calculatorLaunch, out launch)) { calculatorError = "Enter a valid launch gearing percentage."; return; }
                CalculatorSnapshot snapshot; CalculatorResult result;
                if (!GearCalculator.Capture(engine, calculatorVehicle, source.RevLimitRpm, out snapshot, out calculatorError) ||
                    !GearCalculatorMath.Calculate(source, snapshot.Inputs, target, calculatorProfile, calculatorFinalDrive, launch, out result, out calculatorError)) return;
                calculatorSnapshot = snapshot; calculatorResult = result; calculatorSource = source;
                RememberCalculatorInputs(); calculatorFreshness.Record(Time.unscaledTime, true);
                BuildCalculatorLabels();
                error = null;
            }
            catch (Exception exception)
            { calculatorError = "The calculator could not read this setup. Manual tuning remains available."; Plugin.Log.LogWarning("Gear calculator: " + exception); }
        }

        private static void BuildCalculatorLabels()
        {
            calculatorFinalDriveLabel = "Suggested final drive: " + calculatorResult.Tune.FinalDrive.ToString("0.###", CultureInfo.InvariantCulture);
            calculatorShiftLabel = "Automatic RPM suggestion: up " + calculatorResult.ShiftRpm.ToString("0", CultureInfo.InvariantCulture) +
                " / down " + calculatorResult.DownshiftRpm.ToString("0", CultureInfo.InvariantCulture);
            List<string> labels = new List<string>();
            int number = 0;
            foreach (float ratio in calculatorResult.Tune.Ratios)
                if (ratio > 0f)
                    labels.Add("Gear " + (++number) + ": " + ratio.ToString("0.###", CultureInfo.InvariantCulture) + " / " +
                        GearCalculatorMath.Speed(calculatorResult.ShiftRpm, calculatorSnapshot.Inputs.Radius, ratio, calculatorResult.Tune.FinalDrive, 1f).ToString("0", CultureInfo.InvariantCulture) +
                        " km/h at " + calculatorResult.ShiftRpm.ToString("0", CultureInfo.InvariantCulture) + " RPM");
            calculatorGearLabels = labels.ToArray();
        }

        private static void DrawNearbyVehicle()
        {
            GUILayout.Label("Use a nearby vehicle to preview this loose engine's gearing before installing it.", Apocasetter.Theme.SubLabel ?? GUI.skin.label);
            if (calculatorVehicle != null)
            {
                GUILayout.Label("Linked for preview: " + calculatorVehicle.name + " (" +
                    Vector3.Distance(engine.transform.position, calculatorVehicle.transform.position).ToString("0.0", CultureInfo.InvariantCulture) + " m)");
                if (GUILayout.Button("Clear vehicle link"))
                { calculatorVehicle = null; calculatorResult = null; calculatorSnapshot = null; }
            }
            if (GUILayout.Button("Find nearby vehicles (8 metres)"))
            {
                List<VehicleController> matches = new List<VehicleController>();
                foreach (VehicleController candidate in UnityEngine.Object.FindObjectsOfType<VehicleController>())
                    if (GearCalculator.CanLink(engine, candidate)) matches.Add(candidate);
                matches.Sort(delegate(VehicleController a, VehicleController b) {
                    return Vector3.Distance(engine.transform.position, a.transform.position).CompareTo(Vector3.Distance(engine.transform.position, b.transform.position));
                });
                nearbyVehicles = matches.ToArray();
                calculatorError = null;
                if (nearbyVehicles.Length == 0) calculatorError = "No suitable vehicle found within 8 metres. Move this engine closer to the intended vehicle.";
            }
            if (nearbyVehicles != null)
                foreach (VehicleController candidate in nearbyVehicles)
                    if (candidate != null && GUILayout.Button("Use " + candidate.name + " (" +
                        Vector3.Distance(engine.transform.position, candidate.transform.position).ToString("0.0", CultureInfo.InvariantCulture) + " m)"))
                    {
                        calculatorVehicle = candidate; nearbyVehicles = null; calculatorResult = null; calculatorSnapshot = null; calculatorError = null;
                        break;
                    }
            GUILayout.Label("This link is only for the calculator. It does not install the engine or change the linked vehicle. The tune saves on this engine when you Apply.", Apocasetter.Theme.SubLabel ?? GUI.skin.label);
        }

        private static void UseCalculatedGearing()
        {
            TuneData current;
            if (calculatorResult == null || calculatorSnapshot == null || calculatorSource == null ||
                !CalculatorInputsMatch() || !GearCalculator.StillMatches(engine, calculatorSnapshot))
            { calculatorError = "Calculate again before using the suggestion."; return; }
            if (!ReadDraft(out current, calculatorShifts)) { calculatorError = error; return; }
            if (!CalculatorDraftMatches()) { calculatorError = "The gearing fields changed. Calculate again before using the suggestion."; return; }
            // Copy only the selected outputs into the current draft. Other manual
            // edits and driving-support preferences remain draft-owned.
            TuneData suggested;
            if (!CalculatorDraftEdits.TryUse(current, calculatorResult, calculatorShifts, out suggested, out calculatorError)) return;
            pendingTune = suggested;
            pendingTuneMessage = "Suggested gearing loaded into the editor. You can still adjust it; Apply and close saves it to this engine.";
            calculatorResult = null; calculatorSnapshot = null; calculatorSource = null;
            calculatorFreshness.Clear(); calculatorGearLabels = null;
        }

        private static string Format(float value) { return value.ToString("0.###", CultureInfo.InvariantCulture); }

        private static bool ReadDraft(out TuneData result)
        { return ReadDraft(out result, false); }

        private static bool ReadDraft(out TuneData result, bool replaceShifts)
        {
            result = draft.Clone();
            CalculatorDraftEdits.PrepareInput(result, replaceShifts);
            for (int i = 0; i < ratios.Length; i++)
            {
                if (draft.Ratios[i] == 0f) { result.Ratios[i] = 0f; continue; }
                float value;
                if (!TuneData.ReadFloat(ratios[i], out value) || value <= 0f)
                { error = "Enter a positive ratio for each gear."; return false; }
                result.Ratios[i] = draft.Ratios[i] < 0f ? -value : value;
            }
            if (!TuneData.ReadFloat(finalDrive, out result.FinalDrive))
            { error = "Enter a valid final-drive ratio."; return false; }
            if (result.TuneShifts && (!TuneData.ReadFloat(shiftUp, out result.ShiftUp) || !TuneData.ReadFloat(shiftDown, out result.ShiftDown)))
            { error = "Enter valid shift RPM values."; return false; }
            result.SpeedLimitKph = 0f;
            if (limitSpeed && !TuneData.ReadFloat(speedLimit, out result.SpeedLimitKph))
            { error = "Enter a valid speed limit in km/h."; return false; }
            if (limitSpeed && result.SpeedLimitKph < 10f)
            { error = "Enter a speed limit of at least 10 km/h, or turn the limit off."; return false; }
            result.EngineBrakingStrength = 0f;
            if (!TuneData.ReadFloat(brakeStrength, out result.BrakeStrength) ||
                !TuneData.ReadFloat(handbrakeStrength, out result.HandbrakeStrength))
            { error = "Enter valid brake and handbrake strengths from 0.1 to 5."; return false; }
            if (engineBrakingEnabled && !TuneData.ReadFloat(engineBraking, out result.EngineBrakingStrength))
            { error = "Enter a valid engine-braking strength."; return false; }
            result.ThrottleRiseSeconds = 0f;
            if (throttleResponseEnabled && (!TuneData.ReadFloat(throttleRise, out result.ThrottleRiseSeconds) || result.ThrottleRiseSeconds < 0.1f))
            { error = "Enter a throttle buildup between 0.1 and 3 seconds."; return false; }
            result.RevLimitRpm = 0f;
            if (revLimitEnabled && (!TuneData.ReadFloat(revLimit, out result.RevLimitRpm) || result.RevLimitRpm < 1000f))
            { error = "Enter a rev limit from 1000 to 15000 RPM, or turn the option off."; return false; }
            if (!result.LowRangeEnabled) result.LowRangeSelected = false;
            else if (!TuneData.ReadFloat(lowRange, out result.LowRangeRatio))
            { error = "Enter a valid low-range reduction."; return false; }
            result.DriveSelectorEnabled = driveEnabled;
            return result.Validate(draft.Ratios, out error);
        }

        private static void ApplyAndClose()
        {
            TuneData desired;
            if (!ReadDraft(out desired)) return;
            if (state.Commit(vehicle, desired, out error)) Close();
        }

        private static void SavePreset()
        {
            TuneData desired;
            if (!ReadDraft(out desired)) return;
            StringBuilder key = new StringBuilder();
            foreach (char c in presetName.Trim())
                if (Char.IsLetterOrDigit(c) || c == ' ' || c == '_' || c == '-') key.Append(c);
            if (key.Length == 0) { error = "Give the preset a name."; return; }
            // Add the row between GUI frames, after the current layout is finished.
            pendingPresetKey = key.ToString(); pendingPresetValue = desired.Serialize();
            presetMessage = "Preset saved. Apply it to keep this engine's tune."; error = null;
        }

        private static void LoadPreset(string text)
        {
            TuneData saved;
            if (!TuneData.TryParse(text, out saved, out error) || !saved.Validate(draft.Ratios, out error)) return;
            pendingTune = saved.Clone(); pendingTuneMessage = "Preset loaded. Apply to tune this engine.";
        }
    }
}
