param([string]$GamePath)
$ErrorActionPreference = 'Stop'
if ($GamePath) {
    return (Resolve-Path -LiteralPath $GamePath).Path
}
$steamRoots = [System.Collections.Generic.List[string]]::new()
$steamKey = Get-ItemProperty -LiteralPath 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue
if ($steamKey -and $steamKey.SteamPath) { $steamRoots.Add($steamKey.SteamPath) }
$steamRoots.Add((Join-Path ${env:ProgramFiles(x86)} 'Steam'))
$steamRoots.Add((Join-Path $env:ProgramFiles 'Steam'))
foreach ($steamRoot in $steamRoots.ToArray()) {
    $libraryFile = Join-Path $steamRoot 'steamapps\libraryfolders.vdf'
    if (Test-Path -LiteralPath $libraryFile) {
        $libraryText = Get-Content -LiteralPath $libraryFile -Raw
        foreach ($match in [regex]::Matches($libraryText, '"path"\s+"([^"]+)"')) {
            $steamRoots.Add($match.Groups[1].Value.Replace('\\', '\'))
        }
    }
}
foreach ($steamRoot in ($steamRoots | Select-Object -Unique)) {
    $candidate = Join-Path $steamRoot 'steamapps\common\Terraria\Terraria.exe'
    if (Test-Path -LiteralPath $candidate) { return (Resolve-Path -LiteralPath $candidate).Path }
}
throw 'Terraria.exe was not found. Pass -GamePath with your installed Terraria.exe path.'
