param(
    [string]$GamePath,
    [string]$GameConfig
)
$ErrorActionPreference = 'Stop'
$GamePath = & (Join-Path $PSScriptRoot 'tools\Find-Game.ps1') -GamePath $GamePath
$probeDirectory = Join-Path $PSScriptRoot '.local\window-test'
& (Join-Path $PSScriptRoot 'build.ps1') -GamePath $GamePath -Check -OutputDirectory $probeDirectory
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$xnaDirectory = Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_32'
$xnaCore = Get-ChildItem -LiteralPath (Join-Path $xnaDirectory 'Microsoft.Xna.Framework') -Recurse -Filter Microsoft.Xna.Framework.dll | Select-Object -First 1 -ExpandProperty FullName
$xnaGame = Get-ChildItem -LiteralPath (Join-Path $xnaDirectory 'Microsoft.Xna.Framework.Game') -Recurse -Filter Microsoft.Xna.Framework.Game.dll | Select-Object -First 1 -ExpandProperty FullName
$probeExecutable = Join-Path $probeDirectory 'StartupWindowProbe.exe'
& $compilerPath /nologo /platform:x86 /utf8output /codepage:65001 "/out:$probeExecutable" "/r:$probeDirectory\FishingContracts.dll" "/r:$probeDirectory\0Harmony.dll" "/r:$GamePath" "/r:$xnaCore" "/r:$xnaGame" (Join-Path $PSScriptRoot 'src\GameLoader.cs') (Join-Path $PSScriptRoot 'tests\StartupWindowProbe.cs')
if ($LASTEXITCODE -ne 0) { throw 'Window probe compilation failed.' }
& (Join-Path $PSScriptRoot 'tools\Set-LargeAddressAware.ps1') -ExecutablePath $probeExecutable
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\Launcher.exe.config') -Destination ($probeExecutable + '.config') -Force
$probeArguments = @('"' + $GamePath + '"')
if ($GameConfig) { $probeArguments += '"' + $GameConfig + '"' }
$probeProcess = Start-Process -FilePath $probeExecutable -ArgumentList $probeArguments -PassThru -WindowStyle Hidden
try {
    if (!$probeProcess.WaitForExit(180000)) { throw 'Window probe did not exit within 180 seconds.' }
    Get-Content -LiteralPath (Join-Path $probeDirectory 'window-check.txt') -Encoding UTF8
    if ($probeProcess.ExitCode -ne 0) { throw 'Window startup probe failed.' }
}
finally {
    if (!$probeProcess.HasExited) { Stop-Process -Id $probeProcess.Id }
}
