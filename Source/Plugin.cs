using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using InsaneSystems.InputManager;
using NWH.VehiclePhysics2;
using NWH.VehiclePhysics2.Powertrain;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ApocaGearbox
{
    [BepInPlugin(GUID, "ApocaGearbox", VERSION)]
    [BepInDependency(Apocasetter.Plugin.GUID, BepInDependency.DependencyFlags.HardDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string GUID = "local.apocalypter.gearboxtuner";
        public const string VERSION = "0.1.19";
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<KeyCode> OpenKey;
        internal static ConfigFile Settings;
        private static GameObject runner;
        private Harmony harmony;

        private void Awake()
        {
            Log = Logger; Settings = Config;
            Config.Bind("General", "Apocasetter", true, "Show this mod in the MODS menu.");
            Enabled = Config.Bind("General", "Enabled", true, "Enable gearbox tuning with the Part Adjuster. Disabling restores captured settings until re-enabled; saved tunes are retained.");
            OpenKey = Config.Bind("Controls", "OpenKey", KeyCode.E, "With the Part Adjuster selected, aim at an engine body or gearbox and press this key to tune it. Loose engines can be tuned before installation.");
            LoadPresetEntries();
            Enabled.SettingChanged += OnEnabledChanged;
            harmony = new Harmony(GUID);
            harmony.PatchAll(typeof(Plugin).Assembly);
            SceneManager.sceneLoaded += OnSceneLoaded;
            EnsureRunner();
            Log.LogInfo("ApocaGearbox " + VERSION + " loaded. Select Part Adjuster, aim at engine body, press " + OpenKey.Value + ".");
        }

        private static void LoadPresetEntries()
        {
            if (!File.Exists(Settings.ConfigFilePath)) return;
            bool inPresets = false;
            // BepInEx leaves unknown keys unbound after a restart. Bind only the
            // preset names; ConfigFile supplies and parses their stored values.
            foreach (string raw in File.ReadAllLines(Settings.ConfigFilePath))
            {
                string line = raw.Trim();
                if (line.StartsWith("[")) { inPresets = line == "[Presets]"; continue; }
                if (!inPresets || line.StartsWith("#")) continue;
                int equals = line.IndexOf('=');
                if (equals <= 0) continue;
                string key = line.Substring(0, equals).Trim();
                if (key.Length > 0 && key.Length <= 48)
                    Settings.Bind("Presets", key, String.Empty, "Gearbox preset saved from the tuning panel.");
            }
        }

        private static void OnEnabledChanged(object sender, EventArgs args)
        {
            if (!Enabled.Value)
            {
                ThrottleResponse.ResetAll();
                GearboxPrompt.Reset();
                TuningPanel.Close();
                DashboardRangeSwitch.Reset();
                DriveControl.SuspendAll();
                GearboxState.Suspend();
            }
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        { ThrottleResponse.ResetAll(); GearboxPrompt.Reset(); TuningPanel.Close(); DashboardRangeSwitch.Reset(); EnsureRunner(); Log.LogInfo("Scene ready: " + scene.name + "; gearbox runner active."); }

        internal static void EnsureRunner()
        {
            if (runner != null && runner.activeInHierarchy) return;
            if (runner != null) UnityEngine.Object.Destroy(runner);
            runner = new GameObject("ApocaGearbox.Runner");
            runner.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(runner);
            runner.AddComponent<GearboxRunner>();
            Log.LogInfo("Gearbox runner created.");
        }

        internal static void ShowNotice(string message)
        {
            if (runner == null) return;
            GearboxRunner component = runner.GetComponent<GearboxRunner>();
            if (component != null) component.Notice(message);
        }

        // The game cleans up BepInEx plugin host objects during loading. Static
        // hooks and the hidden runner must survive that, as the other mods do.
        // Full teardown is only appropriate when the application really exits.
        private void OnApplicationQuit()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (Enabled != null) Enabled.SettingChanged -= OnEnabledChanged;
            ThrottleResponse.ResetAll();
            GearboxPrompt.Reset();
            TuningPanel.Close(); DashboardRangeSwitch.Reset(); DriveControl.SuspendAll(); GearboxState.Suspend();
            if (runner != null) UnityEngine.Object.Destroy(runner);
            if (harmony != null) harmony.UnpatchSelf();
        }
    }

    public sealed class GearboxRunner : MonoBehaviour
    {
        internal static int ConsumedFrame = -1;
        internal static bool SuppressHeldKey;
        private float nextBindingRefresh;
        private float noticeUntil;
        private string notice;
        private static int diagnosticFrame = -1;

        internal static bool KeyPressed()
        { return Plugin.OpenKey != null && Plugin.OpenKey.Value != KeyCode.None && Input.GetKeyDown(Plugin.OpenKey.Value); }

        internal static bool BlockNativeInput()
        { return TuningPanel.IsOpen || ConsumedFrame == Time.frameCount || SuppressHeldKey; }

        internal static bool TryOpen()
        {
            if (!KeyPressed()) return false;
            if (Plugin.Enabled == null || !Plugin.Enabled.Value || TuningPanel.IsOpen || Apocasetter.InputBlocker.Active || Apocasetter.GameMenu.Paused || !Apocasetter.GameMenu.InGame) return false;
            Plugin.EnsureRunner();
            GameObject engine; VehicleController vehicle; string label;
            if (!GearboxTarget.TryAim(out engine, out vehicle, out label))
            {
                if (GearboxTarget.ToolSelected() && diagnosticFrame != Time.frameCount)
                { diagnosticFrame = Time.frameCount; Plugin.Log.LogInfo("Gearbox key: " + GearboxTarget.DescribeSelection()); }
                return false;
            }
            try
            {
                TuningPanel.Open(engine, vehicle, label);
                if (!TuningPanel.IsOpen) return false;
                ConsumedFrame = Time.frameCount; SuppressHeldKey = true;
                Plugin.Log.LogInfo("Gearbox panel opened for " + label + ".");
                return true;
            }
            catch (Exception exception)
            { Plugin.Log.LogError("Could not open gearbox tuning: " + exception); TuningPanel.Close(); return false; }
        }

        private void Update()
        {
            if (Plugin.OpenKey != null && !Input.GetKey(Plugin.OpenKey.Value)) SuppressHeldKey = false;
            if (TuningPanel.IsOpen)
            {
                TuningPanel.Tick();
                if (!Application.isFocused || !Apocasetter.GameMenu.InGame || !GearboxTarget.ToolSelected() || !TuningPanel.TargetStillAvailable())
                { TuningPanel.Close(); return; }
                if (Apocasetter.Plugin.Pressed(UnityEngine.InputSystem.Key.Escape))
                { TuningPanel.Close(); ConsumedFrame = Time.frameCount; SuppressHeldKey = true; }
            }
            else TryOpen();

            if (Time.unscaledTime >= nextBindingRefresh)
            {
                nextBindingRefresh = Time.unscaledTime + 0.2f;
                if (Plugin.Enabled.Value) GearboxState.RefreshMounted();
                DriveControl.TickAll();
            }
            DashboardRangeSwitch.Tick();
        }

        private void OnGUI()
        {
            if (TuningPanel.IsOpen) { TuningPanel.Draw(); return; }
            if (Time.unscaledTime < noticeUntil && notice != null)
                GUI.Box(new Rect(Screen.width / 2f - 250, Screen.height - 90, 500, 45), notice);
        }

        internal void Notice(string text) { notice = text; noticeUntil = Time.unscaledTime + 6f; }
        private void LateUpdate() { DashboardRangeSwitch.LateTick(); GearboxPrompt.LateTick(); }
        private void OnDisable() { GearboxPrompt.Reset(); TuningPanel.Close(); DashboardRangeSwitch.Reset(); }
        private void OnDestroy() { GearboxPrompt.Reset(); TuningPanel.Close(); DashboardRangeSwitch.Reset(); }
    }

    // Before the existing adjustment mod, consume E only for our gearbox action.
    // Native bindings and every other part target retain their normal input.
    [HarmonyPatch(typeof(InputController), "GetKeyActionIsDown")]
    internal static class GearboxUseDownPatch
    {
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore("com.denis.apocalypter.partadjustment")]
        private static bool Prefix(string __0, ref bool __result)
        {
            if (TuningPanel.IsOpen || DashboardRangeSwitch.BlockMouseAction(__0, DashboardRangeSwitch.InputPhase.Down) ||
                (__0 == "Use_2" && (GearboxRunner.BlockNativeInput() || GearboxRunner.TryOpen())))
            { __result = false; return false; }
            return true;
        }
    }

    [HarmonyPatch(typeof(InputController), "GetKeyActionIsActive")]
    internal static class GearboxUseHeldPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(string __0, ref bool __result)
        {
            if (TuningPanel.IsOpen || DashboardRangeSwitch.BlockMouseAction(__0, DashboardRangeSwitch.InputPhase.Held) || (__0 == "Use_2" && GearboxRunner.BlockNativeInput()))
            { __result = false; return false; }
            return true;
        }
    }

    [HarmonyPatch(typeof(InputController), "GetKeyActionIsUp")]
    internal static class GearboxUseUpPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(string __0, ref bool __result)
        {
            if (TuningPanel.IsOpen || DashboardRangeSwitch.BlockMouseAction(__0, DashboardRangeSwitch.InputPhase.Up) || (__0 == "Use_2" && GearboxRunner.BlockNativeInput()))
            { __result = false; return false; }
            return true;
        }
    }

    [HarmonyPatch(typeof(TransmissionComponent), "CalculateTotalGearRatio")]
    internal static class GearboxRatioPatch
    {
        private static float nextWarning;
        private static void Prefix(TransmissionComponent __instance)
        {
            try { GearboxState.BeforeRatio(__instance); }
            catch (Exception exception)
            {
                if (Plugin.Log != null && Time.unscaledTime >= nextWarning)
                { nextWarning = Time.unscaledTime + 5f; Plugin.Log.LogError("Could not apply gearbox tuning: " + exception); }
            }
        }
    }

    // Native engine integration refreshes its cached cutoff from revLimiterRPM.
    [HarmonyPatch(typeof(EngineComponent), "IntegrateDownwards")]
    internal static class EngineRevLimitPatch
    {
        private static float nextWarning;
        private static void Prefix(EngineComponent __instance)
        {
            try { GearboxState.BeforeEngineStep(__instance); }
            catch (Exception exception)
            {
                if (Time.unscaledTime >= nextWarning)
                { nextWarning = Time.unscaledTime + 5f; Plugin.Log.LogWarning("Could not apply rev limit: " + exception); }
            }
        }
    }

    // Apocasetter has one shared input/pause blocker. Hand off the modal first
    // so closing either window cannot unpause underneath the other one.
    [HarmonyPatch]
    internal static class GearboxSettingsModalPatch
    {
        private static System.Reflection.MethodBase TargetMethod()
        { return AccessTools.Method(AccessTools.TypeByName("Apocasetter.SettingsUI"), "Open"); }
        private static void Prefix() { TuningPanel.Close(); }
    }
}
