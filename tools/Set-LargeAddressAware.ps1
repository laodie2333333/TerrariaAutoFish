param([Parameter(Mandatory = $true)][string]$ExecutablePath)
$ErrorActionPreference = 'Stop'
$resolvedExecutable = (Resolve-Path -LiteralPath $ExecutablePath).Path
$bytes = [System.IO.File]::ReadAllBytes($resolvedExecutable)
if ($bytes.Length -lt 64 -or $bytes[0] -ne 0x4d -or $bytes[1] -ne 0x5a) { throw 'Invalid DOS header.' }
$peOffset = [BitConverter]::ToInt32($bytes, 0x3c)
if ($peOffset -lt 64 -or $peOffset + 24 -gt $bytes.Length -or [BitConverter]::ToUInt32($bytes, $peOffset) -ne 0x4550) { throw 'Invalid PE header.' }
if ([BitConverter]::ToUInt16($bytes, $peOffset + 4) -ne 0x14c) { throw 'Expected an x86 executable.' }
# Terraria 1.4.5.8 itself has this flag. Its XNA assets exceed the default 2 GB limit.
$characteristics = [BitConverter]::ToUInt16($bytes, $peOffset + 22) -bor 0x20
$updated = [BitConverter]::GetBytes([uint16]$characteristics)
$bytes[$peOffset + 22] = $updated[0]
$bytes[$peOffset + 23] = $updated[1]
[System.IO.File]::WriteAllBytes($resolvedExecutable, $bytes)
