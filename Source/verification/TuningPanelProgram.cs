using System;
using System.Reflection;
using ApocaGearbox;
using UnityEngine;
using NWH.VehiclePhysics2;

internal static class TuningPanelProgram
{
    private static int passed;
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);passed++;}
    private static void Set(string name,object value){typeof(TuningPanel).GetField(name,BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,value);}
    private static T Get<T>(string name){return (T)typeof(TuningPanel).GetField(name,BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);}
    private static void Invoke(string name){typeof(TuningPanel).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);}
    private static int Main()
    {
        try
        {
            GearboxNativeDefaults.Native=new TuneData {Ratios=new [] {-3f,0f,3f,2f,1f},FinalDrive=4f,TuneShifts=true,ShiftUp=6000f,ShiftDown=3000f,RevLimitRpm=6500f,SpeedLimitKph=150f};
            GearboxState.Shared.Tune=GearboxNativeDefaults.Native.Clone();
            VehicleController vehicle=new VehicleController();GearboxTarget.Mounted=vehicle;
            TuningPanel.Open(new GameObject(),vehicle,"Test engine");Check(TuningPanel.IsOpen,"Panel did not open.");
            Check(typeof(TuningPanel).GetField("tractionEnabled",BindingFlags.Static|BindingFlags.NonPublic)==null, "Removed traction control remains in popup.");
            Check(Get<string>("brakeStrength")=="1" && Get<string>("handbrakeStrength")=="1", "Native brake defaults were not loaded into popup.");
            Set("brakeStrength","2.5"); Set("handbrakeStrength","0.75");
            object[] draftArgs = {null};
            MethodInfo readDraft = typeof(TuningPanel).GetMethod("ReadDraft",BindingFlags.Static|BindingFlags.NonPublic,null,new[] {typeof(TuneData).MakeByRefType()},null);
            Check((bool)readDraft.Invoke(null,draftArgs), "Valid brake draft was rejected.");
            Check(((TuneData)draftArgs[0]).BrakeStrength==2.5f && ((TuneData)draftArgs[0]).HandbrakeStrength==0.75f, "Popup did not capture independent brake settings.");
            Set("brakeStrength","NaN"); draftArgs[0]=null;
            Check(!(bool)readDraft.Invoke(null,draftArgs), "Popup accepted invalid brake strength.");
            Set("brakeStrength","2.5"); Set("handbrakeStrength","5.1"); draftArgs[0]=null;
            Check(!(bool)readDraft.Invoke(null,draftArgs), "Popup accepted excessive handbrake strength.");
            Set("handbrakeStrength","0.75");
            Set("revLimit","5500");Set("shiftUp","invalid old shift text");Set("calculatorShifts",true);Set("calculatorOpen",true);
            Invoke("CalculateGearing");
            Check(Get<CalculatorResult>("calculatorResult")!=null&&Get<string>("calculatorError")==null,"Panel calculator blocked replacement of invalid old shifts after lowering redline.");
            GearCalculator.Checks=0;Time.unscaledTime=1f;
            Invoke("DrawCalculator");Invoke("DrawCalculator");
            Check(GearCalculator.Checks==1,"Layout and repaint repeated expensive freshness checks.");
            Time.unscaledTime=1.1f;Invoke("DrawCalculator");Check(GearCalculator.Checks==1,"Freshness cache not reused within interval.");
            Time.unscaledTime=1.25f;Invoke("DrawCalculator");Check(GearCalculator.Checks==2,"Freshness did not refresh after interval.");
            GearCalculator.Matches=false;Invoke("UseCalculatedGearing");
            Check(Get<TuneData>("pendingTune")==null&&GearCalculator.Checks==3,"Final Use did not authoritatively reject changed setup despite cached fresh display.");
            GearCalculator.Matches=true;Invoke("UseCalculatedGearing");
            TuneData pending=Get<TuneData>("pendingTune");
            Check(pending!=null&&pending.TuneShifts&&pending.ShiftUp<5500f&&pending.ShiftUp>pending.ShiftDown,"Panel failed to load replacement shifts below lowered redline.");
            Check(pending.SpeedLimitKph==150f,"Panel changed manual driving support settings.");
            Check(pending.BrakeStrength==2.5f && pending.HandbrakeStrength==0.75f,"Calculator overwrote brake settings.");
            TuningPanel.Tick();Check(Get<string>("shiftUp")!="invalid old shift text","Loaded calculation did not update old invalid shift fields.");
            Invoke("CalculateGearing");Set("calculatorLaunch","125");Invoke("UseCalculatedGearing");
            Check(Get<TuneData>("pendingTune")==null,"Changed calculator input bypassed authoritative acceptance guard.");
            TuningPanel.Close();
            GearboxState.Shared.Tune=GearboxNativeDefaults.Native.Clone();TuningPanel.Open(new GameObject(),vehicle,"Test engine");
            Set("revLimit","5500");Set("calculatorShifts",false);Invoke("CalculateGearing");
            Check(Get<CalculatorResult>("calculatorResult")==null&&Get<string>("calculatorError")!=null,"Manual shifts bypassed rev-limit validation when not replacing shifts.");
            TuningPanel.Close();
            Console.WriteLine("Tuning panel integration: "+passed+" checks passed.");return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
