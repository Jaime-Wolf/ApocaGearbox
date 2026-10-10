param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'bin'))
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$taskSource = Split-Path -Parent $PSScriptRoot
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $taskCompiler)) {
    $taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (!(Test-Path -LiteralPath $taskCompiler)) { throw 'Windows .NET Framework C# compiler not found.' }

$taskTuneExe = Join-Path $OutputDirectory 'TuneDataTests.exe'
& $taskCompiler /nologo /langversion:5 ('/out:' + $taskTuneExe) (Join-Path $taskSource 'TuneData.cs') (Join-Path $PSScriptRoot 'Program.cs')
if ($LASTEXITCODE -ne 0) { throw 'TuneData tests did not compile.' }
& $taskTuneExe
if ($LASTEXITCODE -ne 0) { throw 'TuneData tests failed.' }

$taskThrottleExe = Join-Path $OutputDirectory 'ThrottleTests.exe'
& $taskCompiler /nologo /langversion:5 ('/out:' + $taskThrottleExe) (Join-Path $taskSource 'TuneData.cs') (Join-Path $taskSource 'ThrottleRamp.cs') (Join-Path $taskSource 'ThrottleResponse.cs') (Join-Path $PSScriptRoot 'ThrottleStubs.cs') (Join-Path $PSScriptRoot 'ThrottleProgram.cs')
if ($LASTEXITCODE -ne 0) { throw 'Throttle response tests did not compile.' }
& $taskThrottleExe
if ($LASTEXITCODE -ne 0) { throw 'Throttle response tests failed.' }

$taskCalculatorExe = Join-Path $OutputDirectory 'CalculatorTests.exe'
& $taskCompiler /nologo /langversion:5 ('/out:' + $taskCalculatorExe) (Join-Path $taskSource 'TuneData.cs') (Join-Path $taskSource 'GearCalculatorMath.cs') (Join-Path $PSScriptRoot 'CalculatorProgram.cs')
if ($LASTEXITCODE -ne 0) { throw 'Gear calculator tests did not compile.' }
& $taskCalculatorExe
if ($LASTEXITCODE -ne 0) { throw 'Gear calculator tests failed.' }

$taskDraftExe = Join-Path $OutputDirectory 'CalculatorDraftTests.exe'
& $taskCompiler /nologo /langversion:5 ('/out:' + $taskDraftExe) (Join-Path $taskSource 'TuneData.cs') (Join-Path $taskSource 'GearCalculatorMath.cs') (Join-Path $taskSource 'CalculatorDraftEdits.cs') (Join-Path $PSScriptRoot 'CalculatorDraftProgram.cs')
if ($LASTEXITCODE -ne 0) { throw 'Calculator draft tests did not compile.' }
& $taskDraftExe
if ($LASTEXITCODE -ne 0) { throw 'Calculator draft tests failed.' }

$taskPanelExe = Join-Path $OutputDirectory 'TuningPanelTests.exe'
& $taskCompiler /nologo /langversion:5 ('/out:' + $taskPanelExe) (Join-Path $taskSource 'TuneData.cs') (Join-Path $taskSource 'GearCalculatorMath.cs') (Join-Path $taskSource 'CalculatorDraftEdits.cs') (Join-Path $taskSource 'TuningPanel.cs') (Join-Path $PSScriptRoot 'TuningPanelStubs.cs') (Join-Path $PSScriptRoot 'TuningPanelProgram.cs')
if ($LASTEXITCODE -ne 0) { throw 'Tuning panel tests did not compile.' }
& $taskPanelExe
if ($LASTEXITCODE -ne 0) { throw 'Tuning panel tests failed.' }

$taskCaptureExe = Join-Path $OutputDirectory 'CalculatorCaptureTests.exe'
& $taskCompiler /nologo /langversion:5 ('/out:' + $taskCaptureExe) (Join-Path $taskSource 'TuneData.cs') (Join-Path $taskSource 'GearCalculatorMath.cs') (Join-Path $taskSource 'GearCalculator.cs') (Join-Path $taskSource 'RevLimitControl.cs') (Join-Path $taskSource 'CalculatorEngineSpecs.cs') (Join-Path $taskSource 'CalculatorBoostSpecs.cs') (Join-Path $PSScriptRoot 'CalculatorCaptureStubs.cs') (Join-Path $PSScriptRoot 'CalculatorCaptureProgram.cs')
if ($LASTEXITCODE -ne 0) { throw 'Gear calculator capture tests did not compile.' }
& $taskCaptureExe
if ($LASTEXITCODE -ne 0) { throw 'Gear calculator capture tests failed.' }

$taskSelectionExe = Join-Path $OutputDirectory 'SelectionTests.exe'
& $taskCompiler /nologo /langversion:5 ('/out:' + $taskSelectionExe) (Join-Path $taskSource 'GearboxTarget.cs') (Join-Path $PSScriptRoot 'SelectionStubs.cs') (Join-Path $PSScriptRoot 'SelectionProgram.cs')
if ($LASTEXITCODE -ne 0) { throw 'Selection tests did not compile.' }
& $taskSelectionExe
if ($LASTEXITCODE -ne 0) { throw 'Selection tests failed.' }

$taskPromptExe = Join-Path $OutputDirectory 'PromptTests.exe'
& $taskCompiler /nologo /langversion:5 ('/out:' + $taskPromptExe) (Join-Path $taskSource 'GearboxPrompt.cs') (Join-Path $PSScriptRoot 'PromptStubs.cs') (Join-Path $PSScriptRoot 'PromptProgram.cs')
if ($LASTEXITCODE -ne 0) { throw 'Gearbox prompt tests did not compile.' }
& $taskPromptExe
if ($LASTEXITCODE -ne 0) { throw 'Gearbox prompt tests failed.' }

$taskMathExe = Join-Path $OutputDirectory 'DrivingMathTests.exe'
& $taskCompiler /nologo /langversion:5 ('/out:' + $taskMathExe) (Join-Path $taskSource 'TuneData.cs') (Join-Path $taskSource 'DrivingMath.cs') (Join-Path $PSScriptRoot 'DrivingMathProgram.cs')
if ($LASTEXITCODE -ne 0) { throw 'Driving math tests did not compile.' }
& $taskMathExe
if ($LASTEXITCODE -ne 0) { throw 'Driving math tests failed.' }

$taskRangeExe = Join-Path $OutputDirectory 'RangeTests.exe'
& $taskCompiler /nologo /langversion:5 ('/out:' + $taskRangeExe) (Join-Path $taskSource 'TuneData.cs') (Join-Path $taskSource 'DrivingMath.cs') (Join-Path $taskSource 'RangeMath.cs') (Join-Path $taskSource 'RangeControl.cs') (Join-Path $PSScriptRoot 'SelectionStubs.cs') (Join-Path $PSScriptRoot 'RangeProgram.cs')
if ($LASTEXITCODE -ne 0) { throw 'Range tests did not compile.' }
& $taskRangeExe
if ($LASTEXITCODE -ne 0) { throw 'Range tests failed.' }

$taskStateExe = Join-Path $OutputDirectory 'StateTests.exe'
& $taskCompiler /nologo /langversion:5 ('/out:' + $taskStateExe) (Join-Path $taskSource 'TuneData.cs') (Join-Path $taskSource 'RangeMath.cs') (Join-Path $taskSource 'RangeControl.cs') (Join-Path $taskSource 'GearboxState.cs') (Join-Path $taskSource 'RevLimitControl.cs') (Join-Path $taskSource 'GearboxTarget.cs') (Join-Path $PSScriptRoot 'SelectionStubs.cs') (Join-Path $PSScriptRoot 'StateStubs.cs') (Join-Path $PSScriptRoot 'StateProgram.cs')
if ($LASTEXITCODE -ne 0) { throw 'Gearbox state tests did not compile.' }
& $taskStateExe
if ($LASTEXITCODE -ne 0) { throw 'Gearbox state tests failed.' }

$taskShaftExe = Join-Path $OutputDirectory 'WheelShaftTests.exe'
& $taskCompiler /nologo /langversion:5 ('/out:' + $taskShaftExe) (Join-Path $taskSource 'TuneData.cs') (Join-Path $taskSource 'DrivingMath.cs') (Join-Path $taskSource 'RangeMath.cs') (Join-Path $taskSource 'RangeControl.cs') (Join-Path $taskSource 'GearboxState.cs') (Join-Path $taskSource 'RevLimitControl.cs') (Join-Path $taskSource 'GearboxTarget.cs') (Join-Path $taskSource 'WheelShaft.cs') (Join-Path $PSScriptRoot 'SelectionStubs.cs') (Join-Path $PSScriptRoot 'StateStubs.cs') (Join-Path $PSScriptRoot 'HarmonyStubs.cs') (Join-Path $PSScriptRoot 'WheelShaftProgram.cs')
if ($LASTEXITCODE -ne 0) { throw 'Wheel shaft tests did not compile.' }
& $taskShaftExe
if ($LASTEXITCODE -ne 0) { throw 'Wheel shaft tests failed.' }

$taskAssistExe = Join-Path $OutputDirectory 'AssistTests.exe'
& $taskCompiler /nologo /langversion:5 ('/out:' + $taskAssistExe) (Join-Path $taskSource 'TuneData.cs') (Join-Path $taskSource 'DrivingMath.cs') (Join-Path $taskSource 'DrivingAssist.cs') (Join-Path $PSScriptRoot 'AssistStubs.cs') (Join-Path $PSScriptRoot 'AssistProgram.cs')
if ($LASTEXITCODE -ne 0) { throw 'Driving assist tests did not compile.' }
& $taskAssistExe
if ($LASTEXITCODE -ne 0) { throw 'Driving assist tests failed.' }

$taskBrakingExe = Join-Path $OutputDirectory 'EngineBrakingTests.exe'
& $taskCompiler /nologo /langversion:5 ('/out:' + $taskBrakingExe) (Join-Path $taskSource 'TuneData.cs') (Join-Path $taskSource 'EngineBraking.cs') (Join-Path $taskSource 'RangeMath.cs') (Join-Path $taskSource 'RangeControl.cs') (Join-Path $taskSource 'GearboxState.cs') (Join-Path $taskSource 'RevLimitControl.cs') (Join-Path $taskSource 'GearboxTarget.cs') (Join-Path $PSScriptRoot 'SelectionStubs.cs') (Join-Path $PSScriptRoot 'StateStubs.cs') (Join-Path $PSScriptRoot 'HarmonyStubs.cs') (Join-Path $PSScriptRoot 'EngineBrakingProgram.cs')
if ($LASTEXITCODE -ne 0) { throw 'Engine braking tests did not compile.' }
& $taskBrakingExe
if ($LASTEXITCODE -ne 0) { throw 'Engine braking tests failed.' }

$taskLegacyExe = Join-Path $OutputDirectory 'LegacyTests.exe'
& $taskCompiler /nologo /langversion:5 ('/out:' + $taskLegacyExe) (Join-Path $taskSource 'TuneData.cs') (Join-Path $taskSource 'LegacyFinalDrive.cs') (Join-Path $PSScriptRoot 'LegacyStubs.cs') (Join-Path $PSScriptRoot 'LegacyProgram.cs')
if ($LASTEXITCODE -ne 0) { throw 'Native final drive tests did not compile.' }
& $taskLegacyExe
if ($LASTEXITCODE -ne 0) { throw 'Native final drive tests failed.' }

$taskAudioExe = Join-Path $OutputDirectory 'AudioTests.exe'
& $taskCompiler /nologo /langversion:5 ('/out:' + $taskAudioExe) (Join-Path $taskSource 'DashboardSwitchAudio.cs') (Join-Path $PSScriptRoot 'AudioStubs.cs') (Join-Path $PSScriptRoot 'AudioProgram.cs')
if ($LASTEXITCODE -ne 0) { throw 'Dashboard audio tests did not compile.' }
& $taskAudioExe
if ($LASTEXITCODE -ne 0) { throw 'Dashboard audio tests failed.' }

$taskDrivePersistenceExe = Join-Path $OutputDirectory 'DrivePersistenceTests.exe'
& $taskCompiler /nologo /langversion:5 ('/out:' + $taskDrivePersistenceExe) (Join-Path $taskSource 'SavePersistence.cs') (Join-Path $taskSource 'DrivePersistence.cs') (Join-Path $PSScriptRoot 'DrivePersistenceStubs.cs') (Join-Path $PSScriptRoot 'DrivePersistenceProgram.cs')
if ($LASTEXITCODE -ne 0) { throw 'Vehicle drive persistence tests did not compile.' }
& $taskDrivePersistenceExe
if ($LASTEXITCODE -ne 0) { throw 'Vehicle drive persistence tests failed.' }

$taskDriveExe = Join-Path $OutputDirectory 'DriveTests.exe'
& $taskCompiler /nologo /langversion:5 ('/out:' + $taskDriveExe) (Join-Path $taskSource 'TuneData.cs') (Join-Path $taskSource 'RangeMath.cs') (Join-Path $taskSource 'RangeControl.cs') (Join-Path $taskSource 'DriveControl.cs') (Join-Path $PSScriptRoot 'DriveStubs.cs') (Join-Path $PSScriptRoot 'DriveProgram.cs')
if ($LASTEXITCODE -ne 0) { throw 'Vehicle drivetrain tests did not compile.' }
& $taskDriveExe
if ($LASTEXITCODE -ne 0) { throw 'Vehicle drivetrain tests failed.' }

$taskDashboardExe = Join-Path $OutputDirectory 'DashboardTests.exe'
& $taskCompiler /nologo /langversion:5 ('/out:' + $taskDashboardExe) (Join-Path $taskSource 'DashboardRangeSwitch.cs') (Join-Path $PSScriptRoot 'DashboardStubs.cs') (Join-Path $PSScriptRoot 'DashboardProgram.cs')
if ($LASTEXITCODE -ne 0) { throw 'Dashboard dispatcher tests did not compile.' }
& $taskDashboardExe
if ($LASTEXITCODE -ne 0) { throw 'Dashboard dispatcher tests failed.' }

