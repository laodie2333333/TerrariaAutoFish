param(
    [string]$GamePath,
    [switch]$Check,
    [string]$OutputDirectory = 'dist'
)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$GamePath = & (Join-Path $projectRoot 'tools\Find-Game.ps1') -GamePath $GamePath
$outputDirectory = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $projectRoot $OutputDirectory }
$packageDirectory = Join-Path $projectRoot '.local\packages'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$xnaDirectory = Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_32'
$xnaCore = Get-ChildItem -LiteralPath (Join-Path $xnaDirectory 'Microsoft.Xna.Framework') -Recurse -Filter Microsoft.Xna.Framework.dll | Select-Object -First 1 -ExpandProperty FullName
$xnaGame = Get-ChildItem -LiteralPath (Join-Path $xnaDirectory 'Microsoft.Xna.Framework.Game') -Recurse -Filter Microsoft.Xna.Framework.Game.dll | Select-Object -First 1 -ExpandProperty FullName
$xnaGraphics = Get-ChildItem -LiteralPath (Join-Path $xnaDirectory 'Microsoft.Xna.Framework.Graphics') -Recurse -Filter Microsoft.Xna.Framework.Graphics.dll | Select-Object -First 1 -ExpandProperty FullName
if (!(Test-Path -LiteralPath $GamePath)) { throw 'Terraria.exe does not exist. Pass -GamePath.' }
if ((Get-Item -LiteralPath $GamePath).VersionInfo.FileVersion -ne '1.4.5.8') { throw 'Only Terraria 1.4.5.8 is supported.' }
New-Item -ItemType Directory -Force -Path $outputDirectory, $packageDirectory | Out-Null
$harmonyDirectory = Join-Path $packageDirectory 'harmony'
$harmonyDll = Join-Path $harmonyDirectory 'lib\net48\0Harmony.dll'
if (!(Test-Path -LiteralPath $harmonyDll)) {
    $archivePath = Join-Path $packageDirectory 'harmony.zip'
    Invoke-WebRequest -UseBasicParsing -Uri 'https://api.nuget.org/v3-flatcontainer/lib.harmony/2.4.2/lib.harmony.2.4.2.nupkg' -OutFile $archivePath
    Expand-Archive -LiteralPath $archivePath -DestinationPath $harmonyDirectory -Force
}
function Invoke-Compiler([string[]]$CompilerArguments) {
    & $compilerPath /nologo /utf8output /codepage:65001 /optimize+ /platform:x86 @CompilerArguments (Join-Path $projectRoot 'src\AssemblyInfo.cs')
    if ($LASTEXITCODE -ne 0) { throw 'C# compilation failed.' }
}
$contracts = Join-Path $outputDirectory 'FishingContracts.dll'
Invoke-Compiler @('/target:library', "/out:$contracts", (Join-Path $projectRoot 'src\Contracts.cs'))
$runtime = Join-Path $outputDirectory 'FishingRuntime.dll'
Invoke-Compiler @('/target:library', "/out:$runtime", "/r:$contracts", "/r:$GamePath", "/r:$harmonyDll", "/r:$xnaCore", "/r:$xnaGame", "/r:$xnaGraphics", '/r:System.Windows.Forms.dll', (Join-Path $projectRoot 'src\FishingEngine.cs'), (Join-Path $projectRoot 'src\FishingRuntime.cs'))
$launcher = Join-Path $outputDirectory 'TerrariaAutoFish.exe'
$appIcon = Join-Path $projectRoot 'assets\golden-fishing-rod.ico'
Invoke-Compiler @('/target:winexe', "/out:$launcher", "/win32icon:$appIcon", "/resource:$appIcon,TerrariaAutoFish.GoldenFishingRod.ico", "/r:$contracts", '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Core.dll', (Join-Path $projectRoot 'src\GameLoader.cs'), (Join-Path $projectRoot 'src\SessionLog.cs'), (Join-Path $projectRoot 'src\Launcher.cs'))
& (Join-Path $projectRoot 'tools\Set-LargeAddressAware.ps1') -ExecutablePath $launcher
Copy-Item -LiteralPath $harmonyDll -Destination $outputDirectory -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\Launcher.exe.config') -Destination ($launcher + '.config') -Force
Copy-Item -LiteralPath (Join-Path $harmonyDirectory 'LICENSE') -Destination (Join-Path $outputDirectory 'Harmony-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $outputDirectory -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE'), (Join-Path $projectRoot 'THIRD_PARTY_NOTICES.md'), (Join-Path $projectRoot 'CHANGELOG.md') -Destination $outputDirectory -Force
if ($Check) {
    $probe = Start-Process -FilePath $launcher -ArgumentList @('--check', ('"' + $GamePath + '"')) -PassThru -Wait -WindowStyle Hidden
    Get-Content -LiteralPath (Join-Path $outputDirectory 'compatibility-check.txt') -Encoding UTF8
    if ($probe.ExitCode -ne 0) { throw 'Compatibility check failed.' }
}
$releaseItems = @('TerrariaAutoFish.exe', 'TerrariaAutoFish.exe.config', 'FishingContracts.dll', 'FishingRuntime.dll', '0Harmony.dll', 'Harmony-LICENSE.txt', 'README.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'CHANGELOG.md') | ForEach-Object { Join-Path $outputDirectory $_ }
Compress-Archive -LiteralPath $releaseItems -DestinationPath (Join-Path $outputDirectory 'TerrariaAutoFish-1.4.5.8.zip') -Force
Write-Output "Built: $launcher"
