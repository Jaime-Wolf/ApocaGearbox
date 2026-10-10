using System;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
namespace ApocaGearbox
{
    internal static class AudioProgram
    {
        private static int checks;
        private static void Check(bool passed,string label) { checks++;if(!passed)throw new Exception(label); }
        private static PlayMakerFSM Fsm(string name,PlaySound sound,bool enabled)
        { return new PlayMakerFSM { Fsm=new Fsm { Name=name,States=new[] { new FsmState { Actions=new FsmStateAction[] { sound } } } },isActiveAndEnabled=enabled }; }
        private static PlaySound Sound(AudioClip clip,float volume)
        { return new PlaySound { clip=new FsmObject { Value=clip },volume=new FsmFloat { Value=volume } }; }
        private static void Main()
        {
            GameObject panel=new GameObject("switch_panel"),fan=new GameObject("switch_fan"),lights=new GameObject("switch_lights");
            panel.transform.Add(fan);panel.transform.Add(lights);fan.transform.position=new Vector3(1,2,3);
            GameObject drive=new GameObject("switch_fuelpump");panel.transform.Add(drive);drive.transform.position=new Vector3(4,5,6);
            AudioClip stock=new AudioClip("switch1"),other=new AudioClip("other");
            PlaySound onSound=Sound(stock,1f),offSound=Sound(other,.5f);
            PlayMakerFSM off=Fsm("LightOff",offSound,false),on=Fsm("LightOn",onSound,true);
            lights.Components.Add(Fsm("ItemName",Sound(other,1f),true));lights.Components.Add(off);lights.Components.Add(on);
            AudioClip clip;float volume;
            Check(DashboardSwitchAudio.TryResolve(fan,out clip,out volume),"native light action resolves");
            Check(clip==stock&&volume==1f,"active native FSM wins over disabled and unrelated FSM");
            Check(DashboardSwitchAudio.TryResolve(drive,out clip,out volume)&&clip==stock&&volume==1f,"drive selector resolves the same native light click");
            GameObject lookalike=new GameObject("switch_fuelpump");panel.transform.Add(lookalike);
            Check(!DashboardSwitchAudio.TryResolve(lookalike,out clip,out volume),"duplicate lever name cannot borrow another target's identity");
            DashboardSwitchAudio.Play(fan);
            Check(AudioSource.Calls==1&&AudioSource.Clip==stock&&AudioSource.Volume==1f,"exactly one native API playback");
            Check(AudioSource.Position.x==1&&AudioSource.Position.y==2&&AudioSource.Position.z==3,"sound originates at fan");
            onSound.volume.Value=.25f;DashboardSwitchAudio.Play(fan);
            Check(AudioSource.Calls==2&&AudioSource.Volume==.25f,"live native volume is reread");
            lights.Components.Remove(on);DashboardSwitchAudio.Play(fan);
            Check(AudioSource.Calls==3&&AudioSource.Clip==other,"destroyed/replaced FSM has no stale cache");
            off.Fsm.States=null;
            Check(!DashboardSwitchAudio.TryResolve(fan,out clip,out volume),"wrong FSM does not supply a clip");
            Check(!DashboardSwitchAudio.TryResolve(null,out clip,out volume),"null fan is safe");
            fan.name="switch_ignition";
            Check(!DashboardSwitchAudio.TryResolve(fan,out clip,out volume),"unrelated switch is rejected");fan.name="switch_fan";
            panel.name="other_panel";
            Check(!DashboardSwitchAudio.TryResolve(fan,out clip,out volume),"unrelated panel is rejected");panel.name="switch_panel";
            lights.Components.Clear();lights.Components.Add(on);on.Fsm.States=new[] { new FsmState { Actions=new FsmStateAction[] { null,onSound } } };
            onSound.clip.Value=null;
            Check(!DashboardSwitchAudio.TryResolve(fan,out clip,out volume),"missing clip is rejected");
            onSound.clip.Value=new UnityEngine.Object();
            Check(!DashboardSwitchAudio.TryResolve(fan,out clip,out volume),"wrong object type is rejected");
            onSound.clip.Value=stock;onSound.volume.Value=Single.NaN;
            Check(!DashboardSwitchAudio.TryResolve(fan,out clip,out volume),"nonfinite volume is rejected");
            onSound.volume=null;
            Check(!DashboardSwitchAudio.TryResolve(fan,out clip,out volume),"missing volume is rejected");
            onSound.volume=new FsmFloat { Value=1f };fan.activeInHierarchy=false;DashboardSwitchAudio.Play(fan);
            Check(AudioSource.Calls==3,"inactive fan does not emit sound");fan.activeInHierarchy=true;
            AudioSource.Throw=true;DashboardSwitchAudio.Play(fan);
            Check(AudioSource.Calls==3,"audio failure stays contained");
            Check(fan.Components.Count==0&&lights.Components.Count==1,"no persistent audio components or native mutations");
            AudioSource.Throw=false;DashboardSwitchAudio.Play(drive);
            Check(AudioSource.Calls==4&&AudioSource.Clip==stock&&AudioSource.Volume==1f,"drive selector emits one native click");
            Check(AudioSource.Position.x==4&&AudioSource.Position.y==5&&AudioSource.Position.z==6,"drive click originates at its own lever");
            drive.activeInHierarchy=false;DashboardSwitchAudio.Play(drive);
            Check(AudioSource.Calls==4,"inactive drive selector does not emit sound");
            Console.WriteLine("Dashboard switch audio: "+checks+" assertions passed.");
        }
    }
}
