$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testDirectory = Join-Path $PSScriptRoot '.local\tests'
New-Item -ItemType Directory -Force -Path $testDirectory | Out-Null
$testExecutable = Join-Path $testDirectory 'EngineTests.exe'
& $compilerPath /nologo /utf8output /codepage:65001 /platform:x86 "/out:$testExecutable" (Join-Path $PSScriptRoot 'src\Contracts.cs') (Join-Path $PSScriptRoot 'src\FishingEngine.cs') (Join-Path $PSScriptRoot 'tests\EngineTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& $testExecutable
if ($LASTEXITCODE -ne 0) { throw 'Engine tests failed.' }
$logTestExecutable = Join-Path $testDirectory 'SessionLogTests.exe'
& $compilerPath /nologo /utf8output /codepage:65001 /platform:x86 "/out:$logTestExecutable" (Join-Path $PSScriptRoot 'src\SessionLog.cs') (Join-Path $PSScriptRoot 'tests\SessionLogTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Log test compilation failed.' }
& $logTestExecutable
if ($LASTEXITCODE -ne 0) { throw 'Persistent log tests failed.' }
