param([Parameter(Mandatory=$true)][string]$Dll,
      [string]$GameDir='D:\Steam\steamapps\common\Apocalypter')
$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $GameDir 'BepInEx\core\Mono.Cecil.dll')
$fixModule=[Mono.Cecil.ModuleDefinition]::ReadModule($Dll)
$fixNative=[Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $GameDir 'Apocalypter_Data\Managed\NWH.VehiclePhysics2.dll'))
$fixChecks=0
function Assert-Native([bool]$Condition,[string]$Description) {
    if(!$Condition){throw $Description}
    $script:fixChecks++
}
try {
    $fixPlugin=$fixModule.Types | Where-Object FullName -eq 'ApocaGearbox.Plugin'
    Assert-Native (($fixPlugin.Fields | Where-Object Name -eq VERSION).Constant -eq '0.1.19') 'Plugin version is not 0.1.19.'
    $fixAttribute=$fixPlugin.CustomAttributes | Where-Object {$_.AttributeType.Name -eq 'BepInPlugin'}
    Assert-Native ($fixAttribute.ConstructorArguments[2].Value -eq '0.1.19') 'Loader metadata version differs.'
    Assert-Native ($fixAttribute.ConstructorArguments[1].Value -eq 'ApocaGearbox') 'Mod name changed.'
    Assert-Native (@($fixModule.Types | Where-Object {$_.FullName -match 'TractionControl|TractionRamp'}).Count -eq 0) 'Retired traction runtime remains in production DLL.'
    $fixTune=$fixModule.Types | Where-Object FullName -eq 'ApocaGearbox.TuneData'
    Assert-Native (@($fixTune.Fields | Where-Object Name -eq 'TractionControlStrength').Count -eq 0) 'Retired traction tune setting remains in production DLL.'
    $fixDrive=$fixModule.Types | Where-Object FullName -eq 'ApocaGearbox.DriveControl'
    $fixOrder=($fixDrive.CustomAttributes | Where-Object {$_.AttributeType.Name -eq 'DefaultExecutionOrder'}).ConstructorArguments[0].Value
    $fixVehicle=$fixNative.Types | Where-Object Name -eq VehicleController
    $fixVehicleOrder=($fixVehicle.CustomAttributes | Where-Object {$_.AttributeType.Name -eq 'DefaultExecutionOrder'}).ConstructorArguments[0].Value
    Assert-Native ($fixOrder -eq 80 -and $fixVehicleOrder -eq 90) 'Deferred wheel cleanup does not precede native vehicle physics.'
    Assert-Native ([bool]($fixDrive.Methods | Where-Object Name -eq FixedUpdate)) 'Deferred cleanup callback is missing.'
    Assert-Native ([bool]($fixDrive.Fields | Where-Object Name -eq roots)) 'Direct differential lookup is missing.'
    Assert-Native ([bool]($fixDrive.Fields | Where-Object Name -eq transmissions)) 'Direct drivetrain lookup is missing.'
    $fixState=$fixModule.Types | Where-Object FullName -eq 'ApocaGearbox.GearboxState'
    Assert-Native ([bool]($fixState.Fields | Where-Object Name -eq EngineOwners)) 'Direct engine lookup is missing.'
    Assert-Native ([bool]($fixState.Methods | Where-Object Name -eq EnsureValidBinding)) 'Shared binding validation is missing.'
    $fixAssist=$fixModule.Types | Where-Object FullName -eq 'ApocaGearbox.DrivingAssist'
    Assert-Native ([bool]($fixAssist.Methods | Where-Object Name -eq UnboostedPeakTorque)) 'Boost torque estimate cache is missing.'
    Assert-Native ([bool]($fixModule.Types | Where-Object FullName -eq 'ApocaGearbox.CalculatorDraftEdits')) 'Calculator replacement-shift validation is missing.'
    $fixRetired=@($fixModule.Types | Where-Object {$_.FullName -match 'ShiftProtection|ShiftLimiter'})
    Assert-Native ($fixRetired.Count -eq 0) 'Retired manual-shift protection returned.'
    $fixShiftCalls=0
    $fixEngineRpmWrites=0
    foreach($fixType in $fixModule.Types) {
        foreach($fixMethod in $fixType.Methods) {
            if(!$fixMethod.HasBody){continue}
            foreach($fixInstruction in $fixMethod.Body.Instructions) {
                if($fixInstruction.OpCode.Name -in @('call','callvirt') -and [string]$fixInstruction.Operand -match 'TransmissionComponent::ShiftInto'){$fixShiftCalls++}
                if($fixInstruction.OpCode.Name -in @('call','callvirt','stfld') -and [string]$fixInstruction.Operand -match 'EngineComponent::(set_OutputRPM|set_OutputAngularVelocity|_outputRPM|_outputAngularVelocity)'){$fixEngineRpmWrites++}
            }
        }
    }
    Assert-Native ($fixShiftCalls -eq 0) 'Manual shift interception appeared in the build.'
    Assert-Native ($fixEngineRpmWrites -eq 0) 'The build writes engine RPM directly.'
    $fixBrake=$fixNative.Types | Where-Object FullName -eq 'NWH.VehiclePhysics2.Brakes'
    $fixBrakeMethod=@($fixBrake.Methods | Where-Object {$_.Name -eq 'AddBrakeTorqueAllWheels' -and $_.Parameters.Count -eq 2})
    Assert-Native ($fixBrakeMethod.Count -eq 1) 'Native braking target is ambiguous or missing.'
    Assert-Native ($fixBrakeMethod[0].Parameters[0].ParameterType.FullName -eq 'System.Single' -and $fixBrakeMethod[0].Parameters[1].ParameterType.FullName -eq 'System.Boolean') 'Native brake arguments changed.'
    $fixWrites=@($fixBrakeMethod[0].Body.Instructions | Where-Object {$_.OpCode.Name -in @('starg','starg.s')})
    Assert-Native ($fixWrites.Count -eq 1 -and $fixWrites[0].Operand.Index -eq 0) 'Native clamp anchor is not the torque argument.'
    $fixDistribution=@($fixBrakeMethod[0].Body.Instructions | Where-Object {$_.OpCode.Name -in @('call','callvirt') -and [string]$_.Operand -match 'WheelComponent::AddBrakeTorque'})
    Assert-Native ($fixDistribution.Count -eq 1 -and $fixDistribution[0].Offset -gt $fixWrites[0].Offset) 'Brake hook would run after wheel distribution.'
    $fixHook=$fixModule.Types | Where-Object FullName -eq 'ApocaGearbox.BrakeStrength'
    Assert-Native ([bool]($fixHook.CustomAttributes | Where-Object {$_.AttributeType.Name -eq 'HarmonyPatch'})) 'Production brake patch metadata missing.'
    Assert-Native ([bool]($fixHook.Methods | Where-Object Name -eq Transpiler) -and [bool]($fixHook.Methods | Where-Object Name -eq Scale)) 'Production brake hook missing.'
    $fixTorqueWrites=@($fixHook.Methods | ForEach-Object {$_.Body.Instructions} | Where-Object {$_.OpCode.Name -eq 'stfld' -and [string]$_.Operand -match 'Brakes::maxTorque'})
    Assert-Native ($fixTorqueWrites.Count -eq 0) 'Brake hook mutates the shared native torque limit.'
    $fixWheel=$fixNative.Types | Where-Object FullName -eq 'NWH.VehiclePhysics2.Powertrain.WheelComponent'
    $fixWheelAdd=$fixWheel.Methods | Where-Object Name -eq 'AddBrakeTorque'
    $fixCaps=@($fixWheelAdd.Body.Instructions | Where-Object {$_.OpCode.Name -eq 'ldfld' -and [string]$_.Operand -match 'Brakes::maxTorque'})
    Assert-Native ($fixCaps.Count -eq 2) 'Native wheel brake ceiling reads changed.'
    Assert-Native ([bool]($fixModule.Types | Where-Object FullName -eq 'ApocaGearbox.BrakeStrengthWheelCap')) 'Wheel brake ceiling patch missing.'
    Write-Output ('Passed '+$fixChecks+' compiled-plugin/native integration inspections.')
} finally { $fixModule.Dispose(); $fixNative.Dispose() }
