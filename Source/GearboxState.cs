using System;
using System.Collections.Generic;
using UnityEngine;
using NWH.VehiclePhysics2;
using NWH.VehiclePhysics2.Powertrain;
using HutongGames.PlayMaker;

namespace ApocaGearbox
{
    // Lives on the engine item, which has its own native saveItemVar record.
    public sealed class GearboxState : MonoBehaviour
    {
        internal static readonly List<GearboxState> TunedEngines = new List<GearboxState>();
        private static readonly Dictionary<TransmissionComponent, GearboxState> Mounted = new Dictionary<TransmissionComponent, GearboxState>();
        private static readonly Dictionary<EngineComponent, GearboxState> EngineOwners = new Dictionary<EngineComponent, GearboxState>();
        public string SavedTuning = String.Empty;
        public string StockTuning = String.Empty;
        internal TuneData Tune;
        internal TuneData Stock;
        private VehicleController boundVehicle;
        private TransmissionComponent boundTransmission;
        private float[] boundLayout;
        private EngineComponent boundEngine;
        private Transform boundMount;
        private PlayMakerFSM boundIdentity, boundCheck;
        private TuneData boundNative, validatedSnapshot;
        private string validationError;
        private bool bindingValid;
        private float nativeIdle;
        private float validatedIdle = Single.NaN, validatedLimit = Single.NaN;
        private EngineComponent.EngineType validatedEngineType;
        private string rpmValidationError;
        private float lastShiftUp, lastShiftDown;
        private bool wroteShifts;
        private DrivingAssist drivingAssist;
        private RevLimitControl revLimitControl;
        private float factoryRevLimit;
        private LegacyFinalDrive legacyFinalDrive;
        private List<float> rangeList;
        private float[] rangeApplied;
        private float[] rangeBase;
        internal VehicleController Vehicle { get { return boundVehicle; } }
        internal string Status;
        internal string DriveSelectorStatus { get; private set; }
        private bool driveRequestPending;

        public static GearboxState GetOrCreate(GameObject engine)
        {
            if (engine == null || !GearboxTarget.IsEngine(engine)) return null;
            GearboxState state = engine.GetComponent<GearboxState>();
            return state != null ? state : engine.AddComponent<GearboxState>();
        }

        public void Restore(string tuning, string stock)
        {
            Unbind();
            legacyFinalDrive = null;
            Tune = null; Stock = null; Status = null;
            validatedSnapshot = null;
            SavedTuning = StockTuning = String.Empty;
            TunedEngines.Remove(this);
            if (String.IsNullOrEmpty(tuning)) return;
            string error;
            TuneData saved;
            if (!TuneData.TryParse(tuning, out saved, out error))
            { Status = error; Plugin.Log.LogWarning("Ignored invalid tuning on " + name + ": " + error); return; }
            Tune = saved; SavedTuning = tuning;
            TuneData original;
            if (!String.IsNullOrEmpty(stock) && TuneData.TryParse(stock, out original, out error) && original.Validate(saved.Ratios, out error))
            { Stock = original; StockTuning = stock; }
            TunedEngines.Add(this);
            // Vehicle assembly may still be running; binding is deferred to the runner.
        }

        public string ExportTuning() { RefreshLegacyFinalDrive(); return Tune != null ? Tune.Serialize() : String.Empty; }
        public string ExportStock() { return Stock != null && Tune != null ? Stock.Serialize() : String.Empty; }

        internal bool Commit(VehicleController vehicle, TuneData desired, out string error)
        { return Commit(desired, out error); }

        // A preview/link vehicle is deliberately not a write target. Only the
        // engine's current native installation owns its physical transmission.
        internal bool Commit(TuneData desired, out string error)
        {
            error = null;
            if (!GearboxTarget.IsEngine(gameObject))
            { error = "The engine is no longer available."; return false; }
            VehicleController mounted = GearboxTarget.InstalledVehicle(gameObject);
            TransmissionComponent transmission = mounted != null && mounted.powertrain != null ? mounted.powertrain.transmission : null;
            if (transmission != null && transmission.transmissionType == TransmissionComponent.TransmissionShiftType.CVT)
            { error = "Continuously variable gearboxes are not supported."; return false; }
            TuneData native = GearboxNativeDefaults.Capture(gameObject);
            if (native == null)
            { error = "The engine's native gearbox settings could not be read."; return false; }
            float[] layout = native.Ratios;
            if (!desired.Validate(layout, out error)) return false;
            if (!ValidateRevLimit(mounted, desired, out error)) return false;
            if (mounted != null && RangeFactor(Tune) != RangeFactor(desired) &&
                !RangeControl.CanChange(mounted, out error)) return false;
            if (mounted != null && desired.DriveSelectorEnabled.HasValue)
            {
                DriveControl drive = DriveControl.GetOrCreate(mounted);
                if (drive == null)
                { error = "The vehicle's drive system is not ready."; return false; }
                // Validate an actual availability change before publishing any
                // gearbox or native final-drive edits. Matching settings leave
                // the vehicle's own selected axle mode untouched.
                if (drive.Enabled != desired.DriveSelectorEnabled.Value &&
                    !drive.ValidateOption(desired.DriveSelectorEnabled.Value, out error)) return false;
            }
            if (Stock == null || !Stock.Validate(layout, out error))
            {
                Stock = native;
                if (Stock == null || !Stock.Validate(layout, out error)) return false;
            }
            StockTuning = Stock.Serialize();
            Tune = desired.Clone(); SavedTuning = Tune.Serialize();
            validatedSnapshot = null;
            if (legacyFinalDrive == null) legacyFinalDrive = new LegacyFinalDrive(gameObject);
            legacyFinalDrive.Set(Tune.FinalDrive);
            if (!TunedEngines.Contains(this)) TunedEngines.Add(this);
            if (transmission != null)
            {
                Bind(mounted); QueueDriveRequest();
                if (Apply(transmission)) ApplyDriveRequest();
            }
            else
            {
                Unbind();
                if (Tune.DriveSelectorEnabled.HasValue)
                    DriveSelectorStatus = "2WD / 4WD selector preference saved for installation.";
            }
            error = null; return true;
        }

        internal bool Reset(VehicleController vehicle, out string error)
        { return Reset(out error); }

        internal bool Reset(out string error)
        {
            error = null;
            if (!GearboxTarget.IsEngine(gameObject))
            { error = "The engine is no longer available."; return false; }
            VehicleController mounted = GearboxTarget.InstalledVehicle(gameObject);
            TransmissionComponent transmission = mounted != null && mounted.powertrain != null ? mounted.powertrain.transmission : null;
            if (mounted != null && RangeFactor(Tune) != 1f && !RangeControl.CanChange(mounted, out error)) return false;
            DriveControl drive = null;
            if (mounted != null && DriveControl.TryGet(mounted, out drive) && drive.Enabled &&
                !drive.ValidateOption(false, out error)) return false;
            if (Stock != null && transmission != null)
            {
                float[] layout = transmission.gears == null ? null : transmission.gears.ToArray();
                if (layout == null || !Stock.Validate(layout, out error))
                { error = "The original gearbox layout differs from this vehicle. Its current native settings were kept."; Clear(); return false; }
            }
            if (drive != null && drive.Enabled && !drive.CommitEnabled(false, out error)) return false;
            if (Stock != null)
            {
                if (legacyFinalDrive == null) legacyFinalDrive = new LegacyFinalDrive(gameObject);
                legacyFinalDrive.Restore(Stock.FinalDrive);
            }
            if (Stock != null && transmission != null)
            {
                Write(transmission, Stock, true);
                ForgetRangeLayer();
            }
            Clear(); return true;
        }

        private void Clear()
        {
            Unbind(); TunedEngines.Remove(this);
            Tune = null; Stock = null; Status = null;
            validatedSnapshot = null;
            legacyFinalDrive = null;
            SavedTuning = StockTuning = String.Empty;
        }

        private void Bind(VehicleController vehicle)
        {
            TransmissionComponent transmission = vehicle.powertrain.transmission;
            if (boundVehicle == vehicle && boundTransmission == transmission &&
                System.Object.ReferenceEquals(boundEngine, vehicle.powertrain.engine))
            {
                RefreshBindingSources();
                return;
            }
            Unbind();
            boundVehicle = vehicle; boundTransmission = transmission;
            boundEngine = vehicle.powertrain.engine;
            drivingAssist = new DrivingAssist(vehicle);
            revLimitControl = new RevLimitControl();
            RefreshBindingSources();
            GearboxState previous;
            if (Mounted.TryGetValue(transmission, out previous) && previous != null && previous != this) previous.Unbind();
            Mounted[transmission] = this;
            QueueDriveRequest();
        }

        private void RefreshBindingSources()
        {
            PlayMakerFSM identity = GearboxTarget.FindFsm(gameObject, "ID");
            PlayMakerFSM check = GearboxTarget.FindFsm(gameObject, "CheckTag");
            bool changed = identity != boundIdentity || check != boundCheck || boundMount != gameObject.transform.parent;
            boundIdentity = identity; boundCheck = check; boundMount = gameObject.transform.parent;
            // Missing/invalid native actions can become ready during assembly.
            // Retry here at the runner's bounded refresh, never in a physics loop.
            if (changed || boundNative == null || !bindingValid)
            {
                boundNative = GearboxNativeDefaults.Capture(gameObject);
                boundLayout = boundNative != null ? boundNative.Ratios : null;
                validatedSnapshot = null;
                if (!CalculatorEngineSpecs.ReadRpm(gameObject, out nativeIdle, out factoryRevLimit))
                {
                    nativeIdle = boundEngine != null ? boundEngine.idleRPM : 0f;
                    factoryRevLimit = boundEngine != null ? boundEngine.revLimiterRPM : 0f;
                }
                validatedIdle = validatedLimit = Single.NaN;
            }
        }

        private bool StillInstalled()
        {
            if (boundVehicle == null || boundVehicle.powertrain == null || !boundVehicle.gameObject.activeInHierarchy ||
                !System.Object.ReferenceEquals(boundVehicle.powertrain.transmission, boundTransmission) ||
                !System.Object.ReferenceEquals(boundVehicle.powertrain.engine, boundEngine) ||
                boundMount == null || gameObject.transform.parent != boundMount || !gameObject.CompareTag("vehPart") ||
                !boundMount.IsChildOf(boundVehicle.gameObject.transform) ||
                boundIdentity == null || boundIdentity.FsmName != "ID" || boundCheck == null ||
                boundCheck.FsmName != "CheckTag" || !boundCheck.isActiveAndEnabled) return false;
            FsmString id = boundIdentity.FsmVariables.GetFsmString("ID");
            FsmGameObject linked = boundCheck.FsmVariables.GetFsmGameObject("vehicle");
            return id != null && id.Value == "engine" && linked != null && linked.Value == boundVehicle.gameObject;
        }

        private void Unbind()
        {
            if (revLimitControl != null)
            {
                // A newly mounted engine owns the same chassis powertrain. Even
                // an identical RPM value must not let the old engine restore over it.
                bool replacement = false;
                if (boundVehicle != null && GearboxTarget.InstalledVehicle(gameObject) != boundVehicle)
                    foreach (Transform part in boundVehicle.GetComponentsInChildren<Transform>(true))
                        if (part.gameObject != gameObject && GearboxTarget.InstalledVehicle(part.gameObject) == boundVehicle)
                        { replacement = true; break; }
                if (replacement) revLimitControl.Forget(); else revLimitControl.Restore();
                revLimitControl = null; factoryRevLimit = 0f;
            }
            if (drivingAssist != null) { drivingAssist.Restore(); drivingAssist = null; }
            RestoreRangeLayer();
            GearboxState registered;
            if (boundTransmission != null && Mounted.TryGetValue(boundTransmission, out registered) && registered == this)
                Mounted.Remove(boundTransmission);
            if (boundEngine != null && EngineOwners.TryGetValue(boundEngine, out registered) && registered == this)
                EngineOwners.Remove(boundEngine);
            boundVehicle = null; boundTransmission = null; boundLayout = null;
            boundEngine = null; boundMount = null; boundIdentity = boundCheck = null; boundNative = null;
            validatedSnapshot = null; bindingValid = false; nativeIdle = 0f;
            validatedIdle = validatedLimit = Single.NaN;
            wroteShifts = false;
            driveRequestPending = false; DriveSelectorStatus = null;
        }

        private void QueueDriveRequest()
        {
            driveRequestPending = boundVehicle != null && Tune != null && Tune.DriveSelectorEnabled.HasValue;
            DriveSelectorStatus = null;
        }

        private void ApplyDriveRequest()
        {
            if (!driveRequestPending || Tune == null || !Tune.DriveSelectorEnabled.HasValue ||
                Plugin.Enabled == null || !Plugin.Enabled.Value) return;
            if (!bindingValid || !StillInstalled()) return;
            // A failed gearbox validation must not apply this engine's request
            // to a vehicle whose tune could not be bound safely.
            if (Status != null && Status != DriveSelectorStatus) return;
            bool enabled = Tune.DriveSelectorEnabled.Value;
            DriveControl drive = DriveControl.GetOrCreate(boundVehicle);
            string error = null;
            if (drive == null || (drive.Enabled != enabled &&
                (!drive.ValidateOption(enabled, out error) || !drive.CommitEnabled(enabled, out error))))
            {
                DriveSelectorStatus = "2WD / 4WD selector change pending: " +
                    (error ?? "The vehicle's drive system is not ready.");
                Status = DriveSelectorStatus;
                return;
            }
            driveRequestPending = false;
            if (Status == DriveSelectorStatus) Status = null;
            DriveSelectorStatus = null;
        }

        internal bool TrySetRange(bool low, out string error)
        {
            error = null;
            if (Tune == null || !Tune.LowRangeEnabled)
            { error = "Enable high / low range in this engine's gearbox editor first."; return false; }
            if (boundTransmission == null || ForTransmission(boundTransmission) != this)
            { error = "The tuned engine is no longer installed in this vehicle."; return false; }
            if (Tune.LowRangeSelected == low) return true;
            if (!RangeControl.CanChange(boundVehicle, out error)) return false;
            bool previous = Tune.LowRangeSelected;
            Tune.LowRangeSelected = low;
            validatedSnapshot = null;
            if (!Apply(boundTransmission))
            { error = Status; Tune.LowRangeSelected = previous; return false; }
            SavedTuning = Tune.Serialize();
            return true;
        }

        private static float RangeFactor(TuneData data)
        { return data == null ? 1f : RangeMath.Factor(data.LowRangeEnabled, data.LowRangeSelected, data.LowRangeRatio); }

        private void TrackRangeLayer(TransmissionComponent transmission, TuneData data)
        {
            if (RangeFactor(data) == 1f)
            { ForgetRangeLayer(); return; }
            rangeList = transmission.gears;
            if (rangeApplied == null || rangeApplied.Length != data.Ratios.Length)
            { rangeApplied = new float[data.Ratios.Length]; rangeBase = new float[data.Ratios.Length]; }
            for (int i = 0; i < data.Ratios.Length; i++)
            { rangeApplied[i] = transmission.gears[i]; rangeBase[i] = data.Ratios[i]; }
        }

        private void RestoreRangeLayer()
        {
            // Remove only our unchanged reduction when an engine leaves its
            // vehicle. A native replacement list or another writer owns itself.
            if (boundTransmission != null && rangeList != null &&
                System.Object.ReferenceEquals(boundTransmission.gears, rangeList) &&
                rangeApplied != null && rangeList.Count == rangeApplied.Length)
            {
                bool unchanged = true;
                for (int i = 0; i < rangeApplied.Length; i++)
                    if (rangeList[i] != rangeApplied[i]) { unchanged = false; break; }
                if (unchanged)
                    for (int i = 0; i < rangeBase.Length; i++) rangeList[i] = rangeBase[i];
            }
            ForgetRangeLayer();
        }

        private void ForgetRangeLayer()
        { rangeList = null; rangeApplied = null; rangeBase = null; }

        internal static void RefreshMounted()
        {
            for (int i = TunedEngines.Count - 1; i >= 0; i--)
            {
                GearboxState state = TunedEngines[i];
                if (state == null) { TunedEngines.RemoveAt(i); continue; }
                if (state.Tune == null) { state.Unbind(); TunedEngines.RemoveAt(i); continue; }
                VehicleController vehicle = GearboxTarget.InstalledVehicle(state.gameObject);
                if (vehicle == null || vehicle.powertrain == null || vehicle.powertrain.transmission == null)
                { state.Unbind(); state.RefreshLegacyFinalDrive(); continue; }
                state.Bind(vehicle);
                state.RefreshLegacyFinalDrive();
                if (state.Apply(state.boundTransmission)) state.ApplyDriveRequest();
            }
        }

        internal static void Suspend()
        {
            for (int i = TunedEngines.Count - 1; i >= 0; i--)
            {
                GearboxState state = TunedEngines[i];
                if (state == null) continue;
                if (state.Stock != null)
                {
                    if (state.legacyFinalDrive == null) state.legacyFinalDrive = new LegacyFinalDrive(state.gameObject);
                    state.legacyFinalDrive.Restore(state.Stock.FinalDrive);
                }
                TransmissionComponent transmission = state.boundTransmission;
                if (transmission != null && state.Stock != null && state.boundVehicle != null &&
                    GearboxTarget.InstalledVehicle(state.gameObject) == state.boundVehicle)
                {
                    string error;
                    float[] layout = transmission.gears == null ? null : transmission.gears.ToArray();
                    if (layout != null && state.Stock.Validate(layout, out error))
                    { Write(transmission, state.Stock, true); state.ForgetRangeLayer(); }
                }
                state.Unbind();
            }
        }

        // Invoked just before NWH computes ratios, after native engine FSM updates.
        internal static void BeforeRatio(TransmissionComponent transmission)
        {
            GearboxState state;
            if (!Mounted.TryGetValue(transmission, out state) || state == null || state.Tune == null) return;
            if (!state.StillInstalled())
            { state.Unbind(); return; }
            state.Apply(transmission);
        }

        internal static GearboxState ForTransmission(TransmissionComponent transmission)
        {
            GearboxState state;
            if (Plugin.Enabled == null || !Plugin.Enabled.Value || transmission == null ||
                !Mounted.TryGetValue(transmission, out state) || state == null || state.Tune == null ||
                state.boundVehicle == null)
                return null;
            if (!state.StillInstalled()) { state.Unbind(); return null; }
            return state.EnsureValidBinding() ? state : null;
        }

        internal static GearboxState ForEngine(EngineComponent engine)
        {
            if (engine == null || Plugin.Enabled == null || !Plugin.Enabled.Value) return null;
            GearboxState state;
            if (!EngineOwners.TryGetValue(engine, out state)) return null;
            if (state == null) { EngineOwners.Remove(engine); return null; }
            return ForTransmission(state.boundTransmission);
        }

        private bool ValidateRevLimit(VehicleController mounted, TuneData desired, out string error)
        {
            error = null;
            if (desired.RevLimitRpm == 0f) return true;
            EngineComponent live = mounted != null && mounted.powertrain != null ? mounted.powertrain.engine : null;
            if (live != null && live.engineType != EngineComponent.EngineType.ICE)
            { error = "Adjustable rev limits require a combustion engine."; return false; }
            float idle = live != null ? live.idleRPM : 0f;
            if (!TuneData.Finite(idle) || idle <= 0f)
            {
                if (boundVehicle == mounted && TuneData.Finite(nativeIdle) && nativeIdle > 0f) idle = nativeIdle;
                else { float native; if (!CalculatorEngineSpecs.ReadRpm(gameObject, out idle, out native)) idle = 0f; }
            }
            return RevLimitControl.Validate(desired.RevLimitRpm, idle, out error);
        }

        internal static void BeforeEngineStep(EngineComponent engine)
        {
            GearboxState state = ForEngine(engine);
            if (state == null || state.revLimitControl == null) return;
            state.revLimitControl.Update(engine, state.Tune.RevLimitRpm, state.factoryRevLimit);
        }

        internal void RefreshLegacyFinalDrive()
        {
            if (Tune == null || Plugin.Enabled == null || !Plugin.Enabled.Value || !GearboxTarget.IsEngine(gameObject)) return;
            if (legacyFinalDrive == null)
            {
                // Capture missing originals before publishing a loaded tune to
                // native variables. Unsupported saved layouts must not alter them.
                TuneData native = GearboxNativeDefaults.Capture(gameObject);
                string error;
                if (native == null || !Tune.Validate(native.Ratios, out error)) return;
                if (Stock == null) { Stock = native; StockTuning = native.Serialize(); }
                legacyFinalDrive = new LegacyFinalDrive(gameObject);
            }
            float value = Tune.FinalDrive;
            if (legacyFinalDrive.Refresh(ref value))
            {
                Tune.FinalDrive = value;
                validatedSnapshot = null;
                SavedTuning = Tune.Serialize();
            }
            legacyFinalDrive.Set(Tune.FinalDrive);
        }

        // The native increase/decrease/reset action is an explicit edit event.
        // Consume it immediately; steady physics only reapplies the saved ratio.
        internal static void NativeFinalDriveEdited(GameObject engine)
        {
            if (engine == null || Plugin.Enabled == null || !Plugin.Enabled.Value) return;
            GearboxState state = engine.GetComponent<GearboxState>();
            if (state == null || state.Tune == null) return;
            state.RefreshLegacyFinalDrive();
            if (ForTransmission(state.boundTransmission) != state) return;
            if (state.boundTransmission.finalGearRatio != state.Tune.FinalDrive)
                state.boundTransmission.finalGearRatio = state.Tune.FinalDrive;
        }

        private void RejectBinding(string error)
        {
            bindingValid = false;
            Status = error ?? "The engine's native gearbox settings could not be read.";
            GearboxState registered;
            if (boundEngine != null && EngineOwners.TryGetValue(boundEngine, out registered) && registered == this)
                EngineOwners.Remove(boundEngine);
            if (revLimitControl != null) revLimitControl.RestoreOverride();
            if (drivingAssist != null) drivingAssist.Restore();
            RestoreRangeLayer();
            RestoreOwnedShifts();
        }

        private void RestoreOwnedShifts()
        {
            if (wroteShifts && boundTransmission != null && Stock != null)
            {
                if (boundTransmission.UpshiftRPM == lastShiftUp) boundTransmission.UpshiftRPM = Stock.ShiftUp;
                if (boundTransmission.DownshiftRPM == lastShiftDown) boundTransmission.DownshiftRPM = Stock.ShiftDown;
            }
            wroteShifts = false;
        }

        private bool EnsureValidBinding()
        {
            if (Plugin.Enabled == null || !Plugin.Enabled.Value || Tune == null || boundTransmission == null) return false;
            string error;
            if (boundTransmission.transmissionType == TransmissionComponent.TransmissionShiftType.CVT)
            { RejectBinding("Continuously variable gearboxes are not supported."); return false; }
            if (boundNative == null || boundLayout == null)
            { RejectBinding("The engine's native gearbox settings could not be read."); return false; }
            if (validatedSnapshot == null || !Tune.SameValues(validatedSnapshot))
            {
                validationError = null;
                Tune.Validate(boundLayout, out validationError);
                validatedSnapshot = Tune.Clone();
            }
            if (validationError != null) { RejectBinding(validationError); return false; }
            if (boundTransmission.gears == null || boundTransmission.gears.Count != Tune.Ratios.Length)
            { RejectBinding("Native gear count changed. Custom tuning was not applied."); return false; }
            for (int i = 0; i < boundTransmission.gears.Count; i++)
                if (!TuneData.Finite(boundTransmission.gears[i]) || Math.Sign(boundTransmission.gears[i]) != Math.Sign(Tune.Ratios[i]))
                { RejectBinding("Native gear layout changed. Custom tuning was not applied."); return false; }
            if (Stock == null)
            {
                Stock = boundNative.Clone();
                if (!Stock.Validate(Tune.Ratios, out error)) { Stock = null; RejectBinding(error); return false; }
                StockTuning = Stock.Serialize();
            }
            float idle = boundEngine != null && TuneData.Finite(boundEngine.idleRPM) && boundEngine.idleRPM > 0f
                ? boundEngine.idleRPM : nativeIdle;
            EngineComponent.EngineType type = boundEngine != null ? boundEngine.engineType : EngineComponent.EngineType.ICE;
            if (!idle.Equals(validatedIdle) || !Tune.RevLimitRpm.Equals(validatedLimit) || type != validatedEngineType)
            {
                validatedIdle = idle; validatedLimit = Tune.RevLimitRpm; validatedEngineType = type;
                rpmValidationError = null;
                if (Tune.RevLimitRpm != 0f && type != EngineComponent.EngineType.ICE)
                    rpmValidationError = "Adjustable rev limits require a combustion engine.";
                else RevLimitControl.Validate(Tune.RevLimitRpm, idle, out rpmValidationError);
            }
            if (rpmValidationError != null) { RejectBinding(rpmValidationError); return false; }
            if (!bindingValid && boundEngine != null) EngineOwners[boundEngine] = this;
            bindingValid = true;
            if (Status != DriveSelectorStatus) Status = null;
            return true;
        }

        private bool Apply(TransmissionComponent transmission)
        {
            if (!Plugin.Enabled.Value || transmission == null || Tune == null || !EnsureValidBinding()) return false;
            if (revLimitControl != null && boundVehicle != null && boundVehicle.powertrain != null)
                revLimitControl.Update(boundVehicle.powertrain.engine, Tune.RevLimitRpm, factoryRevLimit);
            Write(transmission, Tune, false);
            TrackRangeLayer(transmission, Tune);
            // Turning custom automatic shifts off restores the engine's native
            // thresholds immediately, including without removing the engine.
            if (!Tune.TuneShifts && Stock != null)
            {
                float up = Stock.ShiftUp, down = Stock.ShiftDown;
                if (Tune.RevLimitRpm > 0f && up > 0f)
                { up = Math.Min(up, Tune.RevLimitRpm * 0.95f); down = Math.Min(down, up * 0.8f); }
                transmission.UpshiftRPM = up; transmission.DownshiftRPM = down;
            }
            lastShiftUp = transmission.UpshiftRPM; lastShiftDown = transmission.DownshiftRPM; wroteShifts = true;
            if (drivingAssist != null) drivingAssist.Update(Tune);
            if (Status != DriveSelectorStatus) Status = null;
            return true;
        }

        private static void Write(TransmissionComponent transmission, TuneData data, bool restoreShifts)
        {
            // Preserve list identity, signs, neutral and count. NWH's private
            // count/index caches therefore remain valid.
            float range = RangeFactor(data);
            for (int i = 0; i < data.Ratios.Length; i++)
            {
                float ratio = data.Ratios[i] * range;
                if (transmission.gears[i] != ratio) transmission.gears[i] = ratio;
            }
            if (transmission.finalGearRatio != data.FinalDrive) transmission.finalGearRatio = data.FinalDrive;
            if (data.TuneShifts || restoreShifts)
            {
                if (transmission.UpshiftRPM != data.ShiftUp) transmission.UpshiftRPM = data.ShiftUp;
                if (transmission.DownshiftRPM != data.ShiftDown) transmission.DownshiftRPM = data.ShiftDown;
            }
        }

        private void OnDestroy() { Unbind(); TunedEngines.Remove(this); }
    }
}
