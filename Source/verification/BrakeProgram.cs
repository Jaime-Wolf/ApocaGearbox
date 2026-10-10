using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using ApocaGearbox;
using NWH.VehiclePhysics2;

namespace NWH.VehiclePhysics2
{
    public sealed class Brakes { public float maxTorque = 1000f; public VehicleController vehicleController; }
    public sealed class Engine { }
    public sealed class TestPowertrain { public Engine engine = new Engine(); }
    public sealed class VehicleController { public TestPowertrain powertrain = new TestPowertrain(); }
}
namespace NWH.VehiclePhysics2.Powertrain
{
    public sealed class WheelComponent { public VehicleController vehicleController; public Brakes brakes; }
}
namespace ApocaGearbox
{
    internal sealed class TestEnabled { internal bool Value = true; }
    internal sealed class TestLog { internal void LogWarning(string message) { } }
    internal static class Plugin { internal static TestEnabled Enabled = new TestEnabled(); internal static TestLog Log = new TestLog(); }
    internal sealed class GearboxState
    {
        internal static GearboxState Bound;
        internal TuneData Tune;
        internal VehicleController Vehicle;
        internal static GearboxState ForEngine(Engine engine)
        { return Bound != null && Bound.Vehicle.powertrain.engine == engine ? Bound : null; }
    }
}
internal static class BrakeProgram
{
    private static int passed;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); passed++; }
    private static List<CodeInstruction> NativeClamp()
    {
        return new List<CodeInstruction> {
            new CodeInstruction(OpCodes.Ldarg_1), new CodeInstruction(OpCodes.Ldc_R4, 0f),
            new CodeInstruction(OpCodes.Call, typeof(Math).GetMethod("Max", new[] {typeof(float), typeof(float)})),
            new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Ldfld, typeof(Brakes).GetField("maxTorque")),
            new CodeInstruction(OpCodes.Call, typeof(Math).GetMethod("Min", new[] {typeof(float), typeof(float)})),
            new CodeInstruction(OpCodes.Starg_S, (byte)1), new CodeInstruction(OpCodes.Ldarg_1), new CodeInstruction(OpCodes.Ret)
        };
    }
    private static Func<Brakes,float,bool,float> Compile()
    {
        DynamicMethod method = new DynamicMethod("BrakeClampWithProductionHook", typeof(float),
            new[] {typeof(Brakes),typeof(float),typeof(bool)}, typeof(BrakeProgram).Module, true);
        ILGenerator il = method.GetILGenerator();
        foreach (CodeInstruction code in BrakeStrength.Transpiler(NativeClamp())) {
            if (code.operand == null) il.Emit(code.opcode);
            else if (code.operand is byte) il.Emit(code.opcode,(byte)code.operand);
            else if (code.operand is float) il.Emit(code.opcode,(float)code.operand);
            else if (code.operand is FieldInfo) il.Emit(code.opcode,(FieldInfo)code.operand);
            else if (code.operand is MethodInfo) il.Emit(code.opcode,(MethodInfo)code.operand);
            else throw new Exception("Unsupported test instruction.");
        }
        return (Func<Brakes,float,bool,float>)method.CreateDelegate(typeof(Func<Brakes,float,bool,float>));
    }
    private static Func<NWH.VehiclePhysics2.Powertrain.WheelComponent,float,float> CompileWheel()
    {
        Type wheel = typeof(NWH.VehiclePhysics2.Powertrain.WheelComponent);
        DynamicMethod method = new DynamicMethod("WheelCeilingWithProductionHook", typeof(float),
            new[] {wheel,typeof(float)}, typeof(BrakeProgram).Module, true);
        ILGenerator il = method.GetILGenerator(); Label nativeReturn = il.DefineLabel();
        CodeInstruction returnTorque = new CodeInstruction(OpCodes.Ldarg_1); returnTorque.labels.Add(nativeReturn);
        List<CodeInstruction> codes = new List<CodeInstruction> {
            new CodeInstruction(OpCodes.Ldarg_1), new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Ldfld,wheel.GetField("brakes")),new CodeInstruction(OpCodes.Ldfld,typeof(Brakes).GetField("maxTorque")),
            new CodeInstruction(OpCodes.Ble_Un,nativeReturn),new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Ldfld,wheel.GetField("brakes")),new CodeInstruction(OpCodes.Ldfld,typeof(Brakes).GetField("maxTorque")),
            new CodeInstruction(OpCodes.Ret),returnTorque,new CodeInstruction(OpCodes.Ret)
        };
        foreach(CodeInstruction code in BrakeStrengthWheelCap.Transpiler(codes)) {
            foreach(Label label in code.labels) il.MarkLabel(label);
            if(code.operand==null) il.Emit(code.opcode);
            else if(code.operand is FieldInfo) il.Emit(code.opcode,(FieldInfo)code.operand);
            else if(code.operand is MethodInfo) il.Emit(code.opcode,(MethodInfo)code.operand);
            else if(code.operand is Label) il.Emit(code.opcode,(Label)code.operand);
            else throw new Exception("Unsupported wheel test instruction.");
        }
        return (Func<NWH.VehiclePhysics2.Powertrain.WheelComponent,float,float>)method.CreateDelegate(typeof(Func<NWH.VehiclePhysics2.Powertrain.WheelComponent,float,float>));
    }
    private static int Main()
    {
        try {
            VehicleController vehicle = new VehicleController(); Brakes brakes = new Brakes { vehicleController = vehicle };
            TuneData tune = new TuneData(); GearboxState.Bound = new GearboxState {Vehicle=vehicle,Tune=tune};
            Func<Brakes,float,bool,float> apply = Compile();
            Check(apply(brakes,1000f,false)==1000f && apply(brakes,1000f,true)==1000f,"Default changed native brakes.");
            tune.BrakeStrength=2.5f; tune.HandbrakeStrength=0.75f;
            Check(apply(brakes,1000f,false)==2500f,"Native cap swallowed increased regular braking.");
            Check(apply(brakes,1000f,true)==750f,"Handbrake used regular strength.");
            Check(apply(brakes,500f,false)==1250f,"Partial pedal strength was lost.");
            Check(apply(brakes,2000f,false)==2500f,"Native clamp was bypassed before scaling.");
            Check(apply(brakes,-10f,false)==0f && apply(brakes,0f,true)==0f,"No-input braking added torque.");
            Func<NWH.VehiclePhysics2.Powertrain.WheelComponent,float,float> cap=CompileWheel();
            NWH.VehiclePhysics2.Powertrain.WheelComponent wheel=new NWH.VehiclePhysics2.Powertrain.WheelComponent {vehicleController=vehicle,brakes=brakes};
            Check(cap(wheel,apply(brakes,1000f,false))==2500f,"Wheel ceiling swallowed increased regular brakes.");
            Check(cap(wheel,cap(wheel,apply(brakes,1000f,true))+apply(brakes,1000f,false))==2500f,"Combined brakes clipped regular torque.");
            Check(cap(wheel,cap(wheel,apply(brakes,1000f,false))+apply(brakes,1000f,true))==2500f,"Combined brake ceiling depends on input order.");
            tune.BrakeStrength=0.5f; tune.HandbrakeStrength=3f;
            Check(cap(wheel,apply(brakes,1000f,true))==3000f,"Wheel ceiling swallowed increased handbrake.");
            Check(cap(wheel,apply(brakes,1000f,false))==500f,"Stronger handbrake changed regular brake strength.");
            tune.BrakeStrength=2.5f; tune.HandbrakeStrength=0.75f;
            for (int i=0; i<10000; i++) if(cap(wheel,apply(brakes,1000f,false))!=2500f || brakes.maxTorque!=1000f) throw new Exception("Repeated ticks compounded torque or changed maxTorque.");
            Check(true,"Repeated calls completed.");
            Plugin.Enabled.Value=false; Check(apply(brakes,1000f,false)==1000f && cap(wheel,2500f)==1000f,"Disabling retained override."); Plugin.Enabled.Value=true;
            GearboxState.Bound=null; Check(apply(brakes,1000f,false)==1000f,"Untuned vehicle was changed.");
            GearboxState.Bound=new GearboxState {Vehicle=vehicle,Tune=tune};
            VehicleController other=new VehicleController(); brakes.vehicleController=other;
            Check(apply(brakes,1000f,false)==1000f,"Tune leaked to another vehicle."); brakes.vehicleController=vehicle;
            foreach(float invalid in new[] {float.NaN,float.PositiveInfinity,0f,5.1f}) {
                tune.BrakeStrength=invalid; Check(apply(brakes,1000f,false)==1000f,"Invalid setting changed native braking.");
            }
            tune.BrakeStrength=5f; Check(BrakeStrength.Scale(float.MaxValue,false,brakes)==float.MaxValue,"Overflow became invalid physics torque.");
            Check(BrakeStrength.Scale(float.NaN,false,brakes).Equals(float.NaN),"Nonfinite native torque was changed.");
            List<CodeInstruction> unmatched=NativeClamp(); unmatched.RemoveAt(6);
            Check(new List<CodeInstruction>(BrakeStrength.Transpiler(unmatched)).Count==unmatched.Count,"Missing anchor did not retain native code.");
            unmatched=NativeClamp(); unmatched[6].operand=(byte)2;
            Check(new List<CodeInstruction>(BrakeStrength.Transpiler(unmatched)).Count==unmatched.Count,"Wrong argument was patched.");
            unmatched=NativeClamp(); unmatched.Add(new CodeInstruction(OpCodes.Starg_S,(byte)1));
            Check(new List<CodeInstruction>(BrakeStrength.Transpiler(unmatched)).Count==unmatched.Count,"Ambiguous anchor was patched.");
            Console.WriteLine("Brake strength: "+passed+" checks passed (including 10000 repeated physics calls).");return 0;
        } catch(Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
