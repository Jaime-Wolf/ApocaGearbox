param(
    [string]$GameDir = 'D:\Steam\steamapps\common\Apocalypter',
    [string]$OutputDir = (Join-Path $PSScriptRoot 'build')
)
$ErrorActionPreference = 'Stop'
$taskManaged = Join-Path $GameDir 'Apocalypter_Data\Managed'
$taskCore = Join-Path $GameDir 'BepInEx\core'
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $taskCompiler)) { $taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (!(Test-Path -LiteralPath $taskCompiler)) { throw 'The Windows .NET Framework C# compiler was not found.' }
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$taskReferences = @(
    (Join-Path $taskManaged 'mscorlib.dll'),
    (Join-Path $taskManaged 'System.dll'),
    (Join-Path $taskManaged 'System.Core.dll'),
    (Join-Path $taskManaged 'netstandard.dll'),
    (Join-Path $taskManaged 'UnityEngine.dll'),
    (Join-Path $taskManaged 'UnityEngine.CoreModule.dll'),
    (Join-Path $taskManaged 'UnityEngine.AudioModule.dll'),
    (Join-Path $taskManaged 'UnityEngine.PhysicsModule.dll'),
    (Join-Path $taskManaged 'UnityEngine.InputLegacyModule.dll'),
    (Join-Path $taskManaged 'UnityEngine.IMGUIModule.dll'),
    (Join-Path $taskManaged 'UnityEngine.TextRenderingModule.dll'),
    (Join-Path $taskManaged 'UnityEngine.UI.dll'),
    (Join-Path $taskManaged 'Unity.InputSystem.dll'),
    (Join-Path $taskManaged 'PlayMaker.dll'),
    (Join-Path $taskManaged 'NWH.VehiclePhysics2.dll'),
    (Join-Path $taskManaged 'NWH.Common.dll'),
    (Join-Path $taskManaged 'Assembly-CSharp.dll'),
    (Join-Path $taskManaged 'Assembly-CSharp-firstpass.dll'),
    (Join-Path $taskCore 'BepInEx.dll'),
    (Join-Path $taskCore '0Harmony.dll'),
    (Join-Path $GameDir 'BepInEx\plugins\Apocasetter\Apocasetter.dll')
)
foreach ($taskReference in $taskReferences) {
    if (!(Test-Path -LiteralPath $taskReference)) { throw "Missing build reference: $taskReference" }
}
$taskArgs = @('/nologo','/noconfig','/nostdlib+','/target:library','/langversion:5','/optimize+','/warn:4',('/out:' + (Join-Path $OutputDir 'ApocaGearbox.dll')))
$taskArgs += $taskReferences | ForEach-Object { '/reference:' + $_ }
$taskArgs += Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' -File | ForEach-Object { $_.FullName }
& $taskCompiler @taskArgs
if ($LASTEXITCODE -ne 0) { throw "Mod compilation failed with exit code $LASTEXITCODE" }
Write-Output ('Built ' + (Join-Path $OutputDir 'ApocaGearbox.dll'))
