using System;
using System.Collections.Generic;
using ApocaGearbox;
using NWH.VehiclePhysics2;
using NWH.VehiclePhysics2.Powertrain;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;

internal static class CalculatorCaptureProgram
{
    private static int passed;
    private static void Check(bool value,string message) { if(!value) throw new Exception(message); passed++; }
    private static bool Near(float a,float b) { return Math.Abs(a-b)<0.002f; }
    private static GameObject Build()
    {
        GameObject item=new GameObject("5.2L V8 230HP 340Nm Gasoline");
        VehicleController v=new VehicleController();
        for(int i=0;i<4;i++)v.powertrain.wheels.Add(new WheelComponent());
        v.powertrain.transmission.Output=new DifferentialComponent { Output=v.powertrain.wheels[2], OutputB=v.powertrain.wheels[3] };
        GearboxTarget.Installed[item]=v; DriveControl.Override=null; return item;
    }
    private static CalculatorSnapshot Capture(GameObject item)
    { CalculatorSnapshot s; string error; Check(GearCalculator.Capture(item,out s,out error),error); return s; }
    private static void Reject(GameObject item)
    { CalculatorSnapshot s; string error; Check(!GearCalculator.Capture(item,out s,out error)&&s==null&&!String.IsNullOrEmpty(error),"unsupported setup was accepted"); }
    private static FsmFloat Loose(GameObject item,float power,float idle,float redline)
    {
        FsmFloat condition=new FsmFloat { Name="Condition",Value=100f };
        FsmState[] tiers=new FsmState[4];
        float[] powers={power,power*0.7f,power*0.3f,1f};
        float[] thresholds={50f,20f,1f,1f};
        for(int i=0;i<4;i++)
        {
            tiers[i]=new FsmState { Name="tier"+i,Actions=new FsmStateAction[] {
                new SetFloatValue { floatVariable=new FsmFloat { Name="maxPower",Value=85f },floatValue=new FsmFloat { Value=powers[i] } },
                new FloatCompare { float1=condition,float2=new FsmFloat { Value=thresholds[i] },tolerance=new FsmFloat(),
                    lessThan=i<3?new FsmEvent { Name="lower" }:null,
                    greaterThan=i>0?new FsmEvent { Name="higher" }:null }
            },Transitions=new [] {
                new FsmTransition { EventName="lower",ToState="tier"+Math.Min(3,i+1) },
                new FsmTransition { EventName="higher",ToState="tier"+Math.Max(0,i-1) }
            } };
            // Repair threshold belongs to the upper bound of the current tier.
            if(i>0) tiers[i].Actions=new FsmStateAction[] { tiers[i].Actions[0],
                new FloatCompare { float1=condition,float2=new FsmFloat { Value=thresholds[i] },lessThan=i<3?new FsmEvent {Name="lower"}:null },
                new FloatCompare { float1=condition,float2=new FsmFloat { Value=thresholds[i-1] },greaterThan=new FsmEvent {Name="higher"} } };
        }
        GearboxTarget.Fsms[item]=new Dictionary<string,PlayMakerFSM> {
            {"CheckTag",new PlayMakerFSM { Fsm=new Fsm { States=new [] { new FsmState { Actions=new FsmStateAction[] {
                new SetProperty { targetProperty=new FsmProperty { PropertyName="powertrain.engine.idleRPM",FloatParameter=new FsmFloat {Value=idle} } },
                new SetProperty { targetProperty=new FsmProperty { PropertyName="powertrain.engine.revLimiterRPM",FloatParameter=new FsmFloat {Value=redline} } }
            } } } } }},
            {"Condition",new PlayMakerFSM { ActiveStateName="tier0",Fsm=new Fsm { States=tiers } }}
        };return condition;
    }
    private static GameObject FittedBoost(GameObject item,EngineComponent.ForcedInduction.ForcedInductionType kind,float gain,string label)
    {
        GameObject attacher=new GameObject("forcedinduction_attacher");attacher.transform.parent=item.transform;
        GameObject hinge=new GameObject("hinge");hinge.transform.parent=attacher.transform;
        GameObject part=new GameObject("boost part");part.tag="vehPart";part.transform.parent=hinge.transform;
        PlayMakerFSM id=new PlayMakerFSM();id.FsmVariables.Strings["ID"]=new FsmString {Value="forcedinduction"};
        PlayMakerFSM name=new PlayMakerFSM();name.FsmVariables.Strings["ItemName"]=new FsmString {Value=label};
        PlayMakerFSM mount=new PlayMakerFSM {Fsm=new Fsm {States=new [] {new FsmState {Actions=new FsmStateAction[] {
            new SetProperty {targetProperty=new FsmProperty {PropertyName="powertrain.engine.forcedInduction.powerGainMultiplier",FloatParameter=new FsmFloat {Value=gain}}},
            new SetProperty {targetProperty=new FsmProperty {PropertyName="powertrain.engine.forcedInduction.forcedInductionType",EnumParameter=new FsmEnum {Value=kind}}}
        }}}}};
        GearboxTarget.Fsms[part]=new Dictionary<string,PlayMakerFSM> {{"ID",id},{"ItemName",name},{"CheckTag",mount}};return part;
    }
    private static int Main()
    {
        try {
            GameObject item=Build(); VehicleController v=GearboxTarget.Installed[item];
            CalculatorSnapshot normal=Capture(item);
            Check(normal.Wheels.Length==2,"factory driven wheels not detected");
            Check(normal.EngineDescription.Contains("5.2L V8")&&normal.EngineDescription.Contains("Nm"),"engine identity or torque omitted");
            Check(Near(normal.Inputs.PeakPowerKw,170f),"power curve sampled incorrectly");
            Check(Near(normal.Inputs.DragCoefficient,0.6125f*4f*0.85f*0.6f),"aero formula differs from installed code");
            Check(Near(normal.Inputs.RollingForce,4f*15f/0.52f),"rolling load ignored non-driven wheels");
            v.powertrain.engine.forcedInduction.useForcedInduction=true;v.powertrain.engine.forcedInduction.powerGainMultiplier=1.2f;
            v.powertrain.engine.forcedInduction.forcedInductionType=EngineComponent.ForcedInduction.ForcedInductionType.Supercharger;
            CalculatorSnapshot charged=Capture(item);
            Check(Near(charged.Inputs.PeakPowerKw,normal.Inputs.PeakPowerKw*1.2f),"boost power counted twice or omitted");
            Check(Near(charged.Inputs.PeakTorqueNm,normal.Inputs.PeakTorqueNm*1.2f),"boost torque counted twice or omitted");
            Check(!GearCalculator.StillMatches(item,normal)&&GearCalculator.StillMatches(item,charged),"changed boost did not invalidate preview");
            v.powertrain.engine.maxPower*=0.5f;
            Check(!GearCalculator.StillMatches(item,charged),"engine-condition change did not invalidate preview");
            Check(Near(Capture(item).Inputs.PeakPowerKw,102f),"condition-adjusted engine power ignored");
            v=GearboxTarget.Installed[item=Build()];
            v.powertrain.wheels[3].wheelUAPI.Radius=0.39f;Reject(item);
            v.powertrain.wheels[3].wheelUAPI.Radius=0.52f;v.powertrain.wheels[3].wheelUAPI.IsGrounded=false;Reject(item);
            v.powertrain.wheels[3].wheelUAPI.IsGrounded=true;v.powertrain.wheels[3].wheelUAPI.Load=0f;
            Check(Capture(item).Notes.Contains("estimated"),"sleeping load estimation not disclosed");
            v.Speed=2f;Reject(item);v.Speed=0f;
            v.powertrain.transmission.transmissionType=TransmissionComponent.TransmissionShiftType.CVT;Reject(item);
            v.powertrain.transmission.transmissionType=TransmissionComponent.TransmissionShiftType.Manual;
            v.powertrain.engine.powerCurve.Sample=x=>Single.NaN;Reject(item);
            Reject(new GameObject("loose engine"));
            v=GearboxTarget.Installed[item=Build()];
            DifferentialComponent center=new DifferentialComponent();center.Output=center;center.OutputB=v.powertrain.wheels[3];
            v.powertrain.transmission.Output=center;Reject(item);
            v.powertrain.transmission.Output=new DifferentialComponent {Output=v.powertrain.wheels[1],OutputB=v.powertrain.wheels[1]};
            Check(Capture(item).Wheels.Length==1,"shared middle-wheel output was double counted");
            DriveControl.Override=new Dictionary<WheelComponent,float>();
            foreach(WheelComponent wheel in v.powertrain.wheels)DriveControl.Override[wheel]=0.25f;
            Check(Capture(item).Wheels.Length==4,"4WD conversion selected wheels were ignored");
            CalculatorSnapshot four=Capture(item);DriveControl.Override=null;
            Check(!GearCalculator.StillMatches(item,four),"changed drive mode left an applicable preview");
            CalculatorSnapshot fresh=Capture(item);v.vehicleRigidbody.mass+=1f;
            Check(!GearCalculator.StillMatches(item,fresh),"changed vehicle weight did not invalidate preview");v.vehicleRigidbody.mass-=1f;
            v.powertrain.wheels[1].wheelUAPI.FrictionPreset.BCDE=new NWH.Common.Vehicles.Parameters {z=0.7f};
            Check(!GearCalculator.StillMatches(item,fresh),"changed surface coefficient did not invalidate preview");

            item=Build();v=GearboxTarget.Installed[item];fresh=Capture(item);
            v.powertrain.engine.powerCurve.Points[1].value=0.8f;
            Check(!GearCalculator.StillMatches(item,fresh),"in-place engine curve edit retained preview");
            fresh=Capture(item);v.powertrain.engine.powerCurve.Points[1].outTangent=1f;
            Check(!GearCalculator.StillMatches(item,fresh),"in-place curve tangent edit retained preview");
            fresh=Capture(item);v.powertrain.engine.powerCurve.Points[1].inWeight=0.4f;
            Check(!GearCalculator.StillMatches(item,fresh),"in-place curve weight edit retained preview");
            fresh=Capture(item);v.powertrain.engine.powerCurve.postWrapMode=WrapMode.Loop;
            Check(!GearCalculator.StillMatches(item,fresh),"curve wrap change retained preview");
            fresh=Capture(item);v.Wrapper.module.frontalCd+=0.1f;
            Check(!GearCalculator.StillMatches(item,fresh),"aero coefficient change retained preview");
            fresh=Capture(item);v.Wrapper.module.simulateDrag=false;
            Check(!GearCalculator.StillMatches(item,fresh),"aero drag toggle retained preview");
            fresh=Capture(item);v.Wrapper.module.IsActive=false;
            Check(!GearCalculator.StillMatches(item,fresh),"aero availability change retained preview");
            fresh=Capture(item);v.powertrain.wheels[0].wheelUAPI.RollingResistanceTorque+=10f;
            Check(!GearCalculator.StillMatches(item,fresh),"non-driven tire rolling resistance change retained preview");
            fresh=Capture(item);float restingTorque=v.powertrain.wheels[0].wheelUAPI.RollingResistanceTorque;
            v.powertrain.wheels[0].wheelUAPI.RollingResistanceTorque+=0.1f;
            Check(GearCalculator.StillMatches(item,fresh),"small live rolling resistance jitter invalidated preview");
            v.powertrain.wheels[0].wheelUAPI.RollingResistanceTorque=restingTorque;
            Check(GearCalculator.StillMatches(item,fresh),"returning resting resistance invalidated preview");
            fresh=Capture(item);v.powertrain.wheels[0].wheelUAPI.Radius+=0.1f;
            Check(!GearCalculator.StillMatches(item,fresh),"non-driven rolling radius change retained preview");
            fresh=Capture(item);v.powertrain.wheels.Add(new WheelComponent());
            Check(!GearCalculator.StillMatches(item,fresh),"wheel layout changed without invalidating preview");
            item=Build();v=GearboxTarget.Installed[item];fresh=Capture(item);
            v.powertrain.wheels[2].wheelUAPI.Load*=1.02f;
            Check(GearCalculator.StillMatches(item,fresh),"small suspension load jitter invalidated preview");
            v.powertrain.wheels[2].wheelUAPI.Load*=1.2f;
            Check(!GearCalculator.StillMatches(item,fresh),"material tire load change retained preview");
            v.powertrain.wheels[2].wheelUAPI.Load=0f;fresh=Capture(item);
            Check(fresh.EstimatedLoads[0]||fresh.EstimatedLoads[1],"estimated tire loads not tracked individually");
            Check(GearCalculator.StillMatches(item,fresh),"unchanged asleep load estimate invalidated preview");
            v.powertrain.wheels[2].wheelUAPI.Load=2400f*9.81f/4f;
            Check(GearCalculator.StillMatches(item,fresh),"equivalent estimate-to-measured load transition invalidated preview");
            v.powertrain.wheels[2].wheelUAPI.Load*=1.3f;
            Check(!GearCalculator.StillMatches(item,fresh),"material measured load change retained the estimated preview");
            v.powertrain.wheels[2].wheelUAPI.Load=12000f;fresh=Capture(item);
            v.powertrain.wheels[2].wheelUAPI.Load=15000f;
            Check(GearCalculator.StillMatches(item,fresh),"load above the unchanged tire rating altered calculated capacity");

            item=Build();v=GearboxTarget.Installed[item];GearboxTarget.Installed.Remove(item);
            FsmFloat condition=Loose(item,208f,600f,3400f);
            CalculatorSnapshot projected;string linkError;
            Check(GearCalculator.Capture(item,v,out projected,out linkError),linkError);
            Check(projected.IsProjection&&Near(projected.BasePower,208f)&&Near(projected.Inputs.IdleRpm,600f)&&Near(projected.Inputs.RedlineRpm,3400f),"loose native engine settings replaced by linked vehicle's engine");
            Check(Near(v.powertrain.engine.maxPower,170f)&&Near(v.powertrain.engine.idleRPM,900f),"preview modified linked vehicle's installed engine");
            Check(projected.VehicleDescription.Contains("Preview vehicle")&&projected.Notes.Contains("Recalculate after installation"),"preview assumptions not disclosed");
            Check(GearCalculator.StillMatches(item,projected),"fresh loose preview rejected");
            condition.Value=35f;float power,idle,redline;
            Check(CalculatorEngineSpecs.ReadLoose(item,out power,out idle,out redline)&&Near(power,145.6f),"worn engine native power tier ignored");
            Check(!GearCalculator.StillMatches(item,projected),"loose condition change left preview applicable");
            condition.Value=10f;Check(CalculatorEngineSpecs.ReadLoose(item,out power,out idle,out redline)&&Near(power,62.4f),"low condition tier not followed");
            condition.Value=50f;GearboxTarget.Fsms[item]["Condition"].ActiveStateName="tier1";
            Check(CalculatorEngineSpecs.ReadLoose(item,out power,out idle,out redline)&&Near(power,145.6f),"native active tier at exact condition boundary ignored");
            condition.Value=100f;Check(CalculatorEngineSpecs.ReadLoose(item,out power,out idle,out redline)&&Near(power,208f),"repair transitions were ignored");
            condition.Value=Single.NaN;Check(!CalculatorEngineSpecs.ReadLoose(item,out power,out idle,out redline),"invalid condition accepted");condition.Value=100f;
            v.transform.position=new Vector3(9f,0f,0f);Check(!GearCalculator.Capture(item,v,out projected,out linkError),"out of reach vehicle linked");
            v.transform.position=new Vector3(8f,0f,0f);Check(GearCalculator.CanLink(item,v),"8 metre boundary vehicle excluded");
            v.gameObject.activeInHierarchy=false;Check(!GearCalculator.CanLink(item,v),"inactive vehicle linked");v.gameObject.activeInHierarchy=true;
            v.Parts=new Transform[0];Check(!GearCalculator.CanLink(item,v),"trailer or push cart linked");
            foreach(string name in new [] {"Duke","Crossbreed","Vulture","TinyTyrant","Junker","Rustliner","Outrider","Rustallion","Rustcargo","PipeRat","Poloska","PigPen","Rustchief","Halfbreed"})
            { v=new VehicleController();v.gameObject.name=name;Check(GearCalculator.CanLink(item,v),"stock vehicle excluded: "+name); }
            v=new VehicleController();v.gameObject.name="Custom engine-capable chassis";Check(GearCalculator.CanLink(item,v),"vehicle names used as an allowlist");
            v.Parts[0].Owner=new VehicleController();Check(!GearCalculator.CanLink(item,v),"another nested vehicle's mount accepted");
            // Native healthy power tiers read from all shipped engine prefabs.
            foreach(float nativePower in new [] {20f,55f,97f,87f,71f,208f,71f,136f,209f,37f,38f,56f,265f,97f,235f,173f})
            { Loose(item,nativePower,700f,4500f);Check(CalculatorEngineSpecs.ReadLoose(item,out power,out idle,out redline)&&Near(power,nativePower),"shared 85 kW placeholder replaced the engine's actual power"); }

            item=Build();v=GearboxTarget.Installed[item];GearboxTarget.Installed.Remove(item);Loose(item,136f,900f,5800f);
            v.powertrain.engine.forcedInduction.useForcedInduction=true;v.powertrain.engine.forcedInduction.powerGainMultiplier=2f;
            Check(GearCalculator.Capture(item,v,out projected,out linkError),linkError);
            Check(projected.BoostGain==1f&&projected.EngineDescription.Contains("No turbo or supercharger fitted"),"loose engine inherited unrelated linked vehicle boost");
            GameObject fitted=FittedBoost(item,EngineComponent.ForcedInduction.ForcedInductionType.Supercharger,1.2f,"Supercharger - Road test model - Power x1.2");
            Check(GearCalculator.Capture(item,v,out projected,out linkError),linkError);
            Check(projected.BoostGain==1.2f&&projected.BoostKind=="Supercharger"&&projected.BoostPart==fitted,"attached loose-engine supercharger not automatically detected");
            Check(projected.EngineDescription.Contains("Road test model")&&projected.EngineDescription.Contains("5.2L V8"),"auto detection omitted engine identity or boost model");
            Check(Near(projected.Inputs.PeakPowerKw,136f*1.2f)&&v.powertrain.engine.forcedInduction.powerGainMultiplier==2f,"loose boost counted twice or linked engine changed");
            TuneData roadTune=new TuneData {Ratios=new [] {-2.59f,0f,2.28f,1.65f,1.23f,0.91f,0.72f},FinalDrive=4f,ShiftUp=3200f,ShiftDown=1500f};
            CalculatorResult roadResult;string roadError;
            Check(GearCalculatorMath.Calculate(roadTune,projected.Inputs,200f,1,true,out roadResult,out roadError),roadError);
            Check(roadResult.Tune.Ratios[2]>2.5f&&roadResult.Tune.Ratios[2]/roadResult.Tune.Ratios[6]>3.5f,"boosted V8 road gearing collapsed into nearly all overdrive ratios");
            Console.WriteLine("Boosted V8 fixture at 200 km/h: first "+roadResult.Tune.Ratios[2].ToString("0.###")+", top "+roadResult.Tune.Ratios[6].ToString("0.###")+", final drive "+roadResult.Tune.FinalDrive.ToString("0.###"));
            Check(GearCalculator.StillMatches(item,projected),"fresh own-boost preview not usable");
            fitted.transform.parent=null;Check(!GearCalculator.StillMatches(item,projected),"detached own booster did not invalidate preview");
            fitted=FittedBoost(item,EngineComponent.ForcedInduction.ForcedInductionType.Turbocharger,1.35f,"Turbo - War Whistle - Power x1.35");
            Check(GearCalculator.Capture(item,v,out projected,out linkError),linkError);
            Check(projected.BoostKind=="Turbocharger"&&projected.BoostGain==1.35f&&projected.EngineDescription.Contains("War Whistle"),"turbo model and multiplier not detected");
            fitted.tag="item";Check(!GearCalculator.StillMatches(item,projected),"unmounted cargo booster counted as installed");fitted.tag="vehPart";
            FittedBoost(item,EngineComponent.ForcedInduction.ForcedInductionType.Supercharger,1.2f,"Second booster");
            Check(!GearCalculator.Capture(item,v,out projected,out linkError),"multiple fitted boosters silently combined");
            item=Build();v=GearboxTarget.Installed[item];FittedBoost(item,EngineComponent.ForcedInduction.ForcedInductionType.Supercharger,1.2f,"Fitted supercharger");
            v.powertrain.engine.forcedInduction.useForcedInduction=true;v.powertrain.engine.forcedInduction.forcedInductionType=EngineComponent.ForcedInduction.ForcedInductionType.Supercharger;v.powertrain.engine.forcedInduction.powerGainMultiplier=1.4f;
            CalculatorSnapshot detected=Capture(item);Check(detected.BoostGain==1.4f&&detected.EngineDescription.Contains("Fitted supercharger"),"installed engine did not use actual live boost configuration");
            v.powertrain.engine.forcedInduction.forcedInductionType=EngineComponent.ForcedInduction.ForcedInductionType.Turbocharger;
            Check(!GearCalculator.StillMatches(item,detected),"changing boost type at the same multiplier retained preview");
            float[] nativeGains={1.05f,1.09f,1.2f,1.5f,1.6f,1.7f,1.8f,1.9f,1.05f,1.09f,1.5f,1.6f,1.7f};
            for(int i=0;i<nativeGains.Length;i++)
            {
                item=Build();GearboxTarget.Installed.Remove(item);Loose(item,136f,900f,5800f);
                EngineComponent.ForcedInduction.ForcedInductionType kind=i<8?EngineComponent.ForcedInduction.ForcedInductionType.Supercharger:EngineComponent.ForcedInduction.ForcedInductionType.Turbocharger;
                FittedBoost(item,kind,nativeGains[i],"Native boost fixture "+i);
                CalculatorBoost nativeBoost;Check(CalculatorBoostSpecs.TryRead(item,v.powertrain.engine,true,out nativeBoost,out linkError)&&nativeBoost.Gain==nativeGains[i]&&nativeBoost.Kind==kind.ToString(),"native boost variant not read automatically");
            }
            item=Build();v=GearboxTarget.Installed[item];CalculatorSnapshot custom;string rpmError;
            Check(GearCalculator.Capture(item,null,6500f,out custom,out rpmError),rpmError);
            Check(custom.Inputs.RedlineRpm==6500f&&v.powertrain.engine.revLimiterRPM==4500f,"Draft calculator limit mutated vehicle or was ignored");
            Check(GearCalculator.StillMatches(item,custom),"Unapplied custom redline immediately invalidated preview");
            v.powertrain.engine.revLimiterRPM=4700f;Check(!GearCalculator.StillMatches(item,custom),"Live redline change retained preview");
            GearboxTarget.Installed.Remove(item);Loose(item,170f,900f,5800f);
            Check(GearCalculator.Capture(item,v,7000f,out custom,out rpmError),rpmError);
            Check(custom.Inputs.RedlineRpm==7000f&&v.powertrain.engine.revLimiterRPM==4700f,"Loose custom redline changed linked vehicle");
            Check(GearCalculator.StillMatches(item,custom),"Loose custom redline preview freshness is incorrect");
            Check(!GearCalculator.Capture(item,v,1000f,out custom,out rpmError),"Calculator accepted cutoff too near idle");
            Console.WriteLine("Calculator capture: "+passed+" checks passed.");return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
