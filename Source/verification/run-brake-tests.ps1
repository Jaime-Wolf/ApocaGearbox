param([string]$GameDir='D:\Steam\steamapps\common\Apocalypter',
      [string]$OutputDirectory=(Join-Path $PSScriptRoot 'brake-bin'))
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskHarmony=Join-Path $GameDir 'BepInEx\core\0Harmony.dll'
Copy-Item -LiteralPath $taskHarmony -Destination $OutputDirectory -Force
$taskSource=Split-Path -Parent $PSScriptRoot
$taskExe=Join-Path $OutputDirectory 'BrakeTests.exe'
& $taskCompiler /nologo /langversion:5 /warnaserror+ ('/reference:'+$taskHarmony) ('/out:'+$taskExe) (Join-Path $taskSource 'TuneData.cs') (Join-Path $taskSource 'BrakeStrength.cs') (Join-Path $PSScriptRoot 'BrakeProgram.cs')
if($LASTEXITCODE -ne 0){throw 'Brake tests did not compile.'}
& $taskExe
if($LASTEXITCODE -ne 0){throw 'Brake tests failed.'}
