# Builds CheetosOreSpawnFix.dll against your copy of A Township Tale.
#   .\build.ps1                                   uses C:\Games\Alta\A Township Tale
#   .\build.ps1 -GamePath "D:\Games\A Township Tale"
#   .\build.ps1 -Install                          also copies the DLL into the game's Mods folder (stop the server first)
[CmdletBinding()]
param(
    [string]$GamePath = 'C:\Games\Alta\A Township Tale',
    [switch]$Install
)
$ErrorActionPreference = 'Stop'

$managed = Join-Path $GamePath 'A Township Tale_Data\Managed'
$melon   = Join-Path $GamePath 'MelonLoader\net35'
$csc     = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$here    = Split-Path -Parent $MyInvocation.MyCommand.Path

if (-not (Test-Path $managed)) { throw "Can't find the game at '$GamePath'. Pass -GamePath with the folder that holds 'A Township Tale.exe'." }
if (-not (Test-Path $melon))   { throw "MelonLoader isn't installed in '$GamePath'." }
if (-not (Test-Path $csc))     { throw "Can't find the C# compiler at $csc (.NET Framework 4.x)." }

$refs = @(Get-ChildItem (Join-Path $managed '*.dll') | ForEach-Object { "/r:$($_.FullName)" })
$refs += "/r:$(Join-Path $melon 'MelonLoader.dll')", "/r:$(Join-Path $melon '0Harmony.dll')"
$src = @(Join-Path $here 'Main.cs') + @(Get-ChildItem (Join-Path $here 'Core'), (Join-Path $here 'Game') -Recurse -Filter '*.cs' | ForEach-Object { $_.FullName })
$out = Join-Path $here 'CheetosOreSpawnFix.dll'

& $csc /nologo /warn:4 /warnaserror+ /target:library /nostdlib+ /noconfig "/out:$out" $refs $src
if ($LASTEXITCODE -ne 0) { throw "Build failed." }
Write-Host "Built $out"

if ($Install) {
    Copy-Item $out (Join-Path $GamePath 'Mods\CheetosOreSpawnFix.dll') -Force
    Write-Host "Copied to $(Join-Path $GamePath 'Mods')"
}
