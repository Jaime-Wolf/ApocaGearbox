using System;
using ApocaGearbox;

internal static class CalculatorDraftProgram
{
    private static int passed;
    private static void Check(bool condition,string message) { if(!condition)throw new Exception(message);passed++; }
    private static TuneData Draft()
    { return new TuneData { Ratios=new [] {-3f,0f,3f,2f,1f},FinalDrive=4f,TuneShifts=true,ShiftUp=6000f,ShiftDown=3000f,
        RevLimitRpm=5500f,SpeedLimitKph=150f,ThrottleRiseSeconds=0.8f,DriveSelectorEnabled=true }; }
    private static int Main()
    {
        try
        {
            string error;TuneData input=Draft();
            Check(!input.Validate(null,out error),"Old upshift above the lowered limit should be invalid for manual application.");
            TuneData manual=input.Clone();CalculatorDraftEdits.PrepareInput(manual,false);
            Check(!manual.Validate(null,out error)&&manual.TuneShifts,"Manual shifts should retain full validation.");
            CalculatorDraftEdits.PrepareInput(input,true);
            Check(input.Validate(null,out error)&&!input.TuneShifts,"Calculator cannot replace invalid old shifts after reducing limiter.");
            CalculatorInputs readings=new CalculatorInputs {Radius=0.5f,Mass=2000f,PeakPowerKw=150f,PeakTorqueNm=320f,
                PowerRpm=4500f,BandLowRpm=2500f,IdleRpm=900f,RedlineRpm=5500f,DrivenTorqueCapacity=7000f,DragKnown=false};
            CalculatorResult result;
            Check(GearCalculatorMath.Calculate(input,readings,180f,1,true,out result,out error),error);
            TuneData output;
            Check(CalculatorDraftEdits.TryUse(input,result,true,out output,out error),error);
            Check(output.TuneShifts&&output.ShiftUp<5500f&&output.ShiftUp>output.ShiftDown,"Replacement shift output was not valid for new limiter.");
            Check(output.RevLimitRpm==5500f&&output.SpeedLimitKph==150f&&output.ThrottleRiseSeconds==0.8f&&output.DriveSelectorEnabled==true,"Calculator discarded unrelated manual edits.");
            Check(input.FinalDrive==4f&&input.Ratios[2]==3f,"Using result mutated original input.");
            result.ShiftRpm=5500f;
            Check(!CalculatorDraftEdits.TryUse(input,result,true,out output,out error)&&output==null,"Invalid final suggested shifts bypassed validation.");
            input=Draft();input.RevLimitRpm=6500f;
            CalculatorDraftEdits.PrepareInput(input,false);result.ShiftRpm=4500f;
            Check(CalculatorDraftEdits.TryUse(input,result,false,out output,out error)&&output.ShiftUp==6000f&&output.TuneShifts,"Gearing-only calculation replaced manual shift preferences.");
            CalculatorFreshnessCache cache=new CalculatorFreshnessCache();
            Check(cache.Due(10f),"First freshness check missing.");cache.Record(10f,true);
            Check(cache.Matches&&!cache.Due(10f)&&!cache.Due(10.1f),"Layout/repaint repeated fresh check.");
            Check(cache.Due(10.25f),"Freshness check was not bounded to four per second.");cache.Record(10.25f,false);
            Check(!cache.Matches&&!cache.Due(10.3f),"Stale cached state not retained.");
            cache.Record(10.5f,true);
            Check(!cache.Matches,"Changed snapshot re-enabled the button when sensor readings returned");
            cache.Clear();cache.Record(10.6f,true);
            Check(cache.Matches,"Recalculation could not enable a new valid snapshot");
            Check(cache.Due(9f),"Time reset did not refresh immediately.");cache.Clear();
            Check(cache.Due(10.3f)&&!cache.Matches,"New preview retained prior freshness.");
            Console.WriteLine("Calculator draft and freshness: "+passed+" checks passed.");return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
