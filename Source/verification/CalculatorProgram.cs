using System;
using ApocaGearbox;

internal static class CalculatorProgram
{
    private static int passed;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); passed++; }
    private static bool Near(float a, float b) { return Math.Abs(a - b) < 0.001f; }
    private static TuneData Source(int forwards)
    {
        float[] ratios = new float[forwards + 2]; ratios[0] = -2.59f;
        for (int i = 0; i < forwards; i++) ratios[i + 2] = 3f / (i + 1);
        return new TuneData { Ratios = ratios, FinalDrive = 4f, ShiftUp = 3200f, ShiftDown = 1500f,
            SpeedLimitKph = 200f, EngineBrakingStrength = 2f, ThrottleRiseSeconds = 0.75f,
            LowRangeEnabled = true, LowRangeRatio = 2.5f, DriveSelectorEnabled = true };
    }
    private static CalculatorInputs Truck()
    {
        return new CalculatorInputs { Radius = 0.52f, Mass = 2400f, PeakPowerKw = 170f, PeakTorqueNm = 340f,
            PowerRpm = 3800f, BandLowRpm = 2100f, IdleRpm = 900f, RedlineRpm = 4500f,
            DrivenTorqueCapacity = 8000f, DragCoefficient = 1.4f, RollingForce = 180f, DragKnown = true };
    }
    private static CalculatorResult Calculate(TuneData source, CalculatorInputs input, float speed, int profile, bool fd)
    {
        CalculatorResult result; string error;
        Check(GearCalculatorMath.Calculate(source, input, speed, profile, fd, out result, out error), error);
        return result;
    }
    private static int Main()
    {
        try
        {
            for (int count = 1; count <= 10; count++) {
                foreach (float speed in new float[] { 20f, 100f, 200f, 300f, 500f }) {
                    TuneData source = Source(count); string before = source.Serialize();
                    CalculatorResult r = Calculate(source, Truck(), speed, 1, true);
                    Check(source.Serialize() == before, "preview changed the source tune");
                    Check(r.Tune.Ratios.Length == source.Ratios.Length && r.Tune.Ratios[0] == -2.59f && r.Tune.Ratios[1] == 0f, "gear layout or reverse changed");
                    float top = r.Tune.Ratios[r.Tune.Ratios.Length - 1];
                    Check(Near(GearCalculatorMath.Speed(r.ShiftRpm, 0.52f, top, r.Tune.FinalDrive, 1f), speed), "top-speed units or final drive incorrect");
                    for (int i = 2; i < r.Tune.Ratios.Length - 1; i++) Check(r.Tune.Ratios[i] > r.Tune.Ratios[i + 1], "forward ratios not descending");
                    Check(r.ShiftRpm < 4500f && r.DownshiftRpm < r.ShiftRpm, "unsafe or hunting shift RPM suggestion");
                    Check(r.Tune.ThrottleRiseSeconds == 0.75f && r.Tune.SpeedLimitKph == 200f && r.Tune.EngineBrakingStrength == 2f &&
                        r.Tune.LowRangeRatio == 2.5f && r.Tune.DriveSelectorEnabled == true && !r.Tune.TuneShifts, "calculator altered unrelated settings");
                }
            }
            TuneData existing = Source(5);
            CalculatorResult keep = Calculate(existing, Truck(), 200f, 1, false);
            Check(keep.Tune.FinalDrive == 4f, "keep-final-drive changed final drive");
            CalculatorInputs boosted = Truck(); boosted.PeakPowerKw *= 1.2f; boosted.PeakTorqueNm *= 1.2f;
            CalculatorResult boost = Calculate(existing, boosted, 200f, 1, false);
            Check(boost.Tune.Ratios[2] <= keep.Tune.Ratios[2], "boost made the launch gearing harsher");
            Check(keep.Tune.Ratios[2] > 3f && boost.Tune.Ratios[2] > 3f, "boost or peak torque collapsed launch gears close to top gear");
            Check(GearCalculatorMath.Speed(keep.ShiftRpm,0.52f,keep.Tune.Ratios[2],4f,1f)<=55.01f,"Balanced first gear is too tall for a useful launch");
            Check(boost.Tune.Ratios[6] == keep.Tune.Ratios[6], "boost changed the mechanical top-speed relationship");
            CalculatorResult gentle = Calculate(existing, Truck(), 200f, 0, false);
            CalculatorResult pulling = Calculate(existing, Truck(), 200f, 2, false);
            Check(gentle.Tune.Ratios[2] < keep.Tune.Ratios[2] && keep.Tune.Ratios[2] < pulling.Tune.Ratios[2], "driving styles have no distinct launch behavior");
            CalculatorResult harder,softer;string launchError;
            Check(GearCalculatorMath.Calculate(existing,Truck(),200f,1,false,125f,out harder,out launchError),launchError);
            Check(GearCalculatorMath.Calculate(existing,Truck(),200f,1,false,75f,out softer,out launchError),launchError);
            Check(Near(harder.Tune.Ratios[2],keep.Tune.Ratios[2]*1.25f)&&Near(softer.Tune.Ratios[2],keep.Tune.Ratios[2]*0.75f),"launch adjustment does not scale first gear");
            Check(harder.Tune.Ratios[6]==keep.Tune.Ratios[6]&&softer.Tune.Ratios[6]==keep.Tune.Ratios[6]&&harder.Tune.FinalDrive==4f,"launch adjustment changed target top gear or final drive");
            Check(harder.Tune.Ratios[0]==existing.Ratios[0]&&harder.Tune.Ratios[1]==0f,"launch adjustment changed reverse or neutral");
            for(int i=3;i<6;i++)Check(harder.Tune.Ratios[i]>keep.Tune.Ratios[i],"launch adjustment omitted an intermediate gear");
            CalculatorInputs highTarget=Truck();CalculatorResult fast=Calculate(existing,highTarget,300f,1,false);
            Check(Near(fast.Tune.Ratios[2],keep.Tune.Ratios[2]),"raising target speed made first gear too tall");
            CalculatorInputs narrow=Truck();narrow.BandLowRpm=3600f;
            Check(Calculate(Source(2),narrow,200f,1,false).Warning.Contains("wide RPM drops"),"insufficient gears for power band not explained");
            foreach(float invalidLaunch in new [] {Single.NaN,Single.PositiveInfinity,49f,201f})
            {CalculatorResult invalidResult;Check(!GearCalculatorMath.Calculate(existing,Truck(),200f,1,false,invalidLaunch,out invalidResult,out launchError)&&invalidResult==null,"invalid launch percentage accepted");}
            CalculatorInputs broken = Truck(); broken.Radius = 0.35f; broken.DrivenTorqueCapacity = 1000f;
            CalculatorResult brokenResult = Calculate(existing, broken, 200f, 1, false);
            Check(brokenResult.Tune.Ratios[6] < keep.Tune.Ratios[6], "broken tire size was ignored");
            Check(!String.IsNullOrEmpty(brokenResult.Warning), "broken tire grip had no warning");
            CalculatorInputs weak = Truck(); weak.PeakPowerKw = 20f; weak.PeakTorqueNm = 80f;
            Check(Calculate(existing, weak, 200f, 1, false).Warning.Contains("exceeds available"), "unreachable speed had no power warning");
            CalculatorInputs unknownDrag = Truck(); unknownDrag.DragKnown = false;
            Check(Calculate(existing, unknownDrag, 200f, 1, false).Warning.Contains("unavailable"), "missing aero data was presented as certain");
            CalculatorInputs diesel = Truck(); diesel.IdleRpm = 400f; diesel.RedlineRpm = 2500f; diesel.PowerRpm = 2050f;
            diesel.BandLowRpm = 1100f; diesel.PeakPowerKw = 246f; diesel.PeakTorqueNm = 1700f;
            CalculatorResult dieselResult = Calculate(existing, diesel, 160f, 1, true);
            Check(dieselResult.ShiftRpm <= 2375f, "diesel inherited gasoline RPM");
            Check(dieselResult.Tune.FinalDrive != keep.Tune.FinalDrive, "different engine power band did not affect final drive");
            TuneData twoReverse = Source(3); twoReverse.Ratios = new float[] { -4f, -2f, 0f, 3f, 2f, 1f };
            CalculatorResult multi = Calculate(twoReverse, Truck(), 150f, 1, true);
            Check(multi.Tune.Ratios[0] == -4f && multi.Tune.Ratios[1] == -2f && multi.Tune.Ratios[2] == 0f, "multiple reverse gears changed");
            foreach (float invalid in new float[] { Single.NaN, Single.PositiveInfinity, -1f, 19f, 501f }) {
                CalculatorResult result; string error;
                Check(!GearCalculatorMath.Calculate(existing, Truck(), invalid, 1, true, out result, out error) && result == null && !String.IsNullOrEmpty(error), "invalid target was accepted");
            }
            foreach (int field in new int[] { 0, 1, 2, 3, 4, 5 }) {
                CalculatorInputs bad = Truck();
                if (field == 0) bad.Radius = 0f; if (field == 1) bad.Mass = Single.NaN;
                if (field == 2) bad.PeakTorqueNm = 0f; if (field == 3) bad.DrivenTorqueCapacity = -1f;
                if (field == 4) bad.DragCoefficient = Single.PositiveInfinity; if (field == 5) bad.RollingForce = -1f;
                CalculatorResult result; string error;
                Check(!GearCalculatorMath.Calculate(existing, bad, 200f, 1, true, out result, out error), "invalid vehicle readings accepted");
            }
            Check(Near(GearCalculatorMath.Speed(2938.24f, 0.52f, 0.72f, 4f, 1f), 200f), "known truck gearing example does not agree");
            Check(Near(GearCalculatorMath.Speed(2938.24f, 0.52f, 0.72f, 4f, 2.5f), 80f), "low range was counted incorrectly");
            Console.WriteLine("Gear calculator math: " + passed + " checks passed."); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
