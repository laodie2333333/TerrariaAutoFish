param([string]$OutputPath)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (!$OutputPath) { $OutputPath = Join-Path $projectRoot 'dist\TerrariaAutoFish-0.4.0-source.zip' }
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputPath)
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
# An explicit allowlist keeps hidden development caches and game inspection output out.
$rootFiles = @('.gitignore', '.gitattributes', 'README.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'CHANGELOG.md', 'build.ps1', 'test.ps1', 'test-startup.ps1')
$sourceFiles = @($rootFiles | ForEach-Object { Get-Item -LiteralPath (Join-Path $projectRoot $_) })
foreach ($directory in @('src', 'tests', 'tools', 'assets', 'docs', '.github')) {
    $sourceFiles += Get-ChildItem -LiteralPath (Join-Path $projectRoot $directory) -File -Recurse -Force
}
$outputStream = [System.IO.File]::Open($OutputPath, [System.IO.FileMode]::Create)
try {
    $archive = [System.IO.Compression.ZipArchive]::new($outputStream, [System.IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($file in $sourceFiles) {
            $relativePath = $file.FullName.Substring($projectRoot.Length + 1).Replace('\', '/')
            $null = [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $relativePath)
        }
    }
    finally { $archive.Dispose() }
}
finally { $outputStream.Dispose() }
Write-Output "Source package: $OutputPath ($($sourceFiles.Count) files)"
