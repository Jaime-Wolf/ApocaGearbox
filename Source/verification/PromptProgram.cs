using System;
using ApocaGearbox;
using HutongGames.PlayMaker;
using UnityEngine;
using UnityEngine.UI;
internal static class PromptProgram
{
    private static int passed;
    private static void Check(bool value,string message) { if(!value)throw new Exception(message);passed++; }
    private static int Main()
    {
        try {
            Text text=new Text {text="Native interaction"};object font=text.font;
            FsmVariables.GlobalVariables.ItemUse=new FsmGameObject {Value=new GameObject {Text=text}};
            GearboxPrompt.LateTick();Check(text.text=="Press E to adjust gear ratios","prompt missing");
            Check(text.font==font&&text.fontSize==24&&text.color=="White"&&text.alignment=="Left","native text style modified");
            GearboxPrompt.LateTick();GearboxTarget.Aimed=false;GearboxPrompt.LateTick();Check(text.text=="Native interaction","moving away did not restore native text");
            GearboxTarget.Aimed=true;GearboxPrompt.LateTick();text.text="Other interaction";GearboxPrompt.Clear();Check(text.text=="Other interaction","foreign native write overwritten");
            GearboxPrompt.LateTick();text.text="New native frame";GearboxPrompt.LateTick();GearboxPrompt.Clear();Check(text.text=="New native frame","latest native frame not retained");
            Plugin.OpenKey.Value=KeyCode.G;GearboxPrompt.LateTick();Check(text.text=="Press G to adjust gear ratios","configured key ignored");
            TuningPanel.IsOpen=true;GearboxPrompt.LateTick();Check(text.text=="New native frame","open editor retained prompt");TuningPanel.IsOpen=false;
            GearboxPrompt.LateTick();Plugin.Enabled.Value=false;GearboxPrompt.LateTick();Check(text.text=="New native frame","disabled mod retained prompt");Plugin.Enabled.Value=true;
            GearboxPrompt.LateTick();Apocasetter.GameMenu.Paused=true;GearboxPrompt.LateTick();Check(text.text=="New native frame","pause retained prompt");Apocasetter.GameMenu.Paused=false;
            GearboxPrompt.LateTick();Apocasetter.InputBlocker.Active=true;GearboxPrompt.LateTick();Check(text.text=="New native frame","blocked input retained prompt");Apocasetter.InputBlocker.Active=false;
            GearboxPrompt.LateTick();GearboxPrompt.Reset();Check(text.text=="New native frame","scene reset retained prompt");
            Text next=new Text {text="Next scene"};FsmVariables.GlobalVariables.ItemUse.Value=new GameObject {Text=next};GearboxPrompt.LateTick();
            Check(next.text=="Press G to adjust gear ratios"&&text.text=="New native frame","old scene text reused");
            Text replacement=new Text {text="Replacement native"};
            FsmVariables.GlobalVariables.ItemUse.Value=new GameObject {Text=replacement};
            GearboxPrompt.LateTick();
            Check(replacement.text=="Press G to adjust gear ratios"&&next.text=="Next scene","live HUD replacement did not restore and rebind old prompt");
            Text componentReplacement=new Text {text="Replacement component"};
            FsmVariables.GlobalVariables.ItemUse.Value.Text=componentReplacement;
            GearboxPrompt.LateTick();
            Check(componentReplacement.text=="Press G to adjust gear ratios"&&replacement.text=="Replacement native","text component replaced on surviving HUD owner was missed");
            FsmVariables.GlobalVariables.ItemUse.Value=null;GearboxPrompt.LateTick();
            Check(componentReplacement.text=="Replacement component","removed HUD did not restore owned text");
            Console.WriteLine("Gearbox prompt: "+passed+" checks passed.");return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
