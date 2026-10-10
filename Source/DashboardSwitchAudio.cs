using System;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace ApocaGearbox
{
    internal static class DashboardSwitchAudio
    {
        private static float nextWarning;

        // The native lights PlaySound action uses this same Unity one-shot API.
        // Resolve on each accepted click so scene changes and native volume edits
        // need no cached objects or additional source ownership/cleanup.
        internal static void Play(GameObject fan)
        {
            if (fan == null || !fan.activeInHierarchy) return;
            try
            {
                AudioClip clip;
                float volume;
                if (!TryResolve(fan, out clip, out volume))
                { Warn("The native dashboard switch click was unavailable; the selector change was kept."); return; }
                AudioSource.PlayClipAtPoint(clip, fan.transform.position, volume);
            }
            catch (Exception error)
            {
                Warn("Could not play the native dashboard switch click: " + error.GetType().Name + ": " + error.Message);
            }
        }

        internal static bool TryResolve(GameObject fan, out AudioClip clip, out float volume)
        {
            clip = null; volume = 0f;
            Transform panel = fan == null ? null : fan.transform.parent;
            if (fan == null || (fan.name != "switch_fan" && fan.name != "switch_fuelpump") ||
                panel == null || panel.name != "switch_panel") return false;
            if (panel.Find(fan.name) != fan.transform) return false;
            Transform lights = panel.Find("switch_lights");
            if (lights == null) return false;
            PlayMakerFSM[] fsms = lights.GetComponents<PlayMakerFSM>();
            // Prefer the currently enabled native light state, then the other
            // native light FSM. Inspecting actions never executes light behavior.
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < fsms.Length; i++)
                {
                    PlayMakerFSM fsm = fsms[i];
                    if (fsm == null || fsm.Fsm == null || (pass == 0) != fsm.isActiveAndEnabled ||
                        (fsm.Fsm.Name != "LightOff" && fsm.Fsm.Name != "LightOn")) continue;
                    FsmState[] states = fsm.Fsm.States;
                    if (states == null) continue;
                    for (int j = 0; j < states.Length; j++)
                    {
                        FsmState state = states[j];
                        if (state == null || state.Actions == null) continue;
                        FsmStateAction[] actions = state.Actions;
                        for (int k = 0; k < actions.Length; k++)
                        {
                            PlaySound sound = actions[k] as PlaySound;
                            if (sound == null || sound.clip == null || sound.volume == null) continue;
                            AudioClip candidate = sound.clip.Value as AudioClip;
                            float level = sound.volume.Value;
                            if (candidate == null || !TuneData.Finite(level)) continue;
                            clip = candidate; volume = level;
                            return true;
                        }
                    }
                }
            return false;
        }

        private static void Warn(string message)
        {
            if (Plugin.Log == null || Time.unscaledTime < nextWarning) return;
            nextWarning = Time.unscaledTime + 5f;
            Plugin.Log.LogWarning(message);
        }
    }
}
