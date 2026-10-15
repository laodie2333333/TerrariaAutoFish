param(
    [string]$OutputDirectory = '.local\sonar-release',
    [string]$PreviewPath,
    [string]$CatalogPath
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$panelOutput = [System.IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$panelTestDirectory = Join-Path $projectRoot '.local\panel-tests'
$null = New-Item -ItemType Directory -Force -Path $panelTestDirectory
$panelTestPath = Join-Path $panelTestDirectory 'PanelTests.exe'
Copy-Item -LiteralPath (Join-Path $panelOutput 'FishingContracts.dll') -Destination $panelTestDirectory -Force
& $compilerPath /nologo /utf8output /codepage:65001 /platform:x86 "/out:$panelTestPath" "/r:$panelOutput\FishingContracts.dll" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll (Join-Path $projectRoot 'tests\PanelTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Panel test compilation failed.' }
$panelArgs = @((Join-Path $panelOutput 'TerrariaAutoFish.exe'), $panelTestDirectory)
if ($PreviewPath) { $panelArgs += [System.IO.Path]::GetFullPath((Join-Path $projectRoot $PreviewPath)) }
if ($PreviewPath -and $CatalogPath) { $panelArgs += [System.IO.Path]::GetFullPath((Join-Path $projectRoot $CatalogPath)) }
& $panelTestPath @panelArgs
if ($LASTEXITCODE -ne 0) { throw 'Panel tests failed.' }
