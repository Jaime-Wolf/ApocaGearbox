using System;
using HutongGames.PlayMaker;
using NWH.VehiclePhysics2;
using UnityEngine;
using UnityEngine.UI;

namespace ApocaGearbox
{
    // Use the game's actual interaction Text, preserving its font, color,
    // outline, alignment and canvas scale. No separate overlay is drawn.
    internal static class GearboxPrompt
    {
        private static Text text;
        private static GameObject textOwner;
        private static string original, owned;
        private static KeyCode promptKey;
        private static string prompt;

        internal static void Clear()
        {
            if (text != null && owned != null && text.text == owned) text.text = original ?? String.Empty;
            original = owned = null;
        }

        internal static void Reset() { Clear(); text = null; textOwner = null; prompt = null; }

        internal static void LateTick()
        {
            if (Plugin.Enabled == null || !Plugin.Enabled.Value || TuningPanel.IsOpen ||
                !Apocasetter.GameMenu.InGame || Apocasetter.GameMenu.Paused || Apocasetter.InputBlocker.Active)
            { Clear(); return; }
            if (!GearboxTarget.HasAim()) { Clear(); return; }
            FsmGameObject variable = FsmVariables.GlobalVariables.GetFsmGameObject("UI_ItemUse");
            GameObject owner = variable == null ? null : variable.Value;
            Text current = owner == null ? null : owner.GetComponent<Text>();
            if (owner != textOwner || current != text)
            {
                Clear(); textOwner = owner; text = current;
            }
            if (text == null) return;
            if (prompt == null || promptKey != Plugin.OpenKey.Value)
            { promptKey = Plugin.OpenKey.Value; prompt = "Press " + promptKey + " to adjust gear ratios"; }
            if (owned == null || text.text != owned) original = text.text;
            text.text = owned = prompt;
        }
    }
}
