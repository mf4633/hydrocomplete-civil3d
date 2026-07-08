# Preflight + one-command net8 build of the Civil 3D add-in.
#
# Run this on a machine WITH Autodesk Civil 3D installed to produce the
# shippable net8.0-windows plugin DLL. It verifies the host assemblies are
# present, builds Release, and prints the output path — so a green net8 build
# is a single command:
#
#   .\scripts\preflight-net8.ps1
#
# For Civil 3D 2025 (or a non-default install path), pass -AcadDir:
#
#   .\scripts\preflight-net8.ps1 -AcadDir "C:\Program Files\Autodesk\AutoCAD 2025\"
#
# Exits non-zero on any missing prerequisite or build failure.
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [string]$AcadDir = 'C:\Program Files\Autodesk\AutoCAD 2026\'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$csproj = Join-Path $root 'src\HydroComplete.Civil3D\HydroComplete.Civil3D.csproj'
$AcadDir = $AcadDir.TrimEnd('\') + '\'
$c3dDir = Join-Path $AcadDir 'C3D\'

function Fail($msg) { Write-Host "  [X] $msg" -ForegroundColor Red; $script:failed = $true }
function Ok($msg)   { Write-Host "  [OK] $msg" -ForegroundColor Green }

Write-Host ""
Write-Host "== HydroComplete net8 build preflight ==" -ForegroundColor Cyan
$script:failed = $false

# 1. .NET SDK 8
Write-Host "`n[1/3] .NET SDK 8.0"
$sdk = (& dotnet --list-sdks 2>$null) | Where-Object { $_ -match '^8\.' }
if ($sdk) { Ok ".NET 8 SDK present ($([string]($sdk | Select-Object -First 1)))" }
else { Fail ".NET 8 SDK not found. Install from https://dotnet.microsoft.com/download" }

# 2. Autodesk host assemblies (compile-time references)
Write-Host "`n[2/3] Autodesk managed assemblies under $AcadDir"
$required = @(
    (Join-Path $AcadDir 'AcMgd.dll'),
    (Join-Path $AcadDir 'AcCoreMgd.dll'),
    (Join-Path $AcadDir 'AcDbMgd.dll'),
    (Join-Path $AcadDir 'AdWindows.dll'),
    (Join-Path $c3dDir  'AeccDbMgd.dll')
)
foreach ($dll in $required) {
    if (Test-Path $dll) { Ok (Split-Path $dll -Leaf) }
    else { Fail "missing: $dll" }
}

if ($script:failed) {
    Write-Host "`nPreflight failed — fix the items above and re-run." -ForegroundColor Red
    Write-Host "For Civil 3D 2025, pass -AcadDir 'C:\Program Files\Autodesk\AutoCAD 2025\'." -ForegroundColor Yellow
    exit 1
}

# 3. Build net8 only (BuildNet48=false keeps this to the shipping target)
Write-Host "`n[3/3] dotnet build ($Configuration, net8.0-windows)"
& dotnet build $csproj -c $Configuration -p:BuildNet48=false -p:Net8AcadDir="$AcadDir"
if ($LASTEXITCODE -ne 0) {
    Write-Host "`nBuild failed (exit $LASTEXITCODE)." -ForegroundColor Red
    exit $LASTEXITCODE
}

$outDll = Join-Path $root "src\HydroComplete.Civil3D\bin\$Configuration\net8.0-windows\HydroComplete.Civil3D.dll"
Write-Host ""
if (Test-Path $outDll) {
    Ok "Built: $outDll"
    Write-Host "`nnet8 build succeeded." -ForegroundColor Green
    exit 0
} else {
    Fail "Build reported success but output DLL not found at $outDll"
    exit 1
}
