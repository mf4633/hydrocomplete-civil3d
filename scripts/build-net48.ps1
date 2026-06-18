# Build the net48 target for Civil 3D 2024 (R24.3) and stage Contents/net48 in the bundle.
# Compiles without a local AutoCAD 2024 install (uses AutoCAD.NET 24.3.0 NuGet stubs).
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [string]$Net48AcadDir = 'C:\Program Files\Autodesk\AutoCAD 2024\'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$csproj = Join-Path $root 'src\HydroComplete.Civil3D\HydroComplete.Civil3D.csproj'
$bundleContents = Join-Path $root 'dist\HydroComplete.bundle\Contents'
$net48Out = Join-Path $root "src\HydroComplete.Civil3D\bin\$Configuration\net48"
$eng = Join-Path $root "src\HydroComplete.Engine\bin\$Configuration\netstandard2.0"

Push-Location $root
try {
    Write-Host "Building HydroComplete.Civil3D net48 ($Configuration)..."
    dotnet build $csproj -c $Configuration -p:BuildNet48=true -p:Net48AcadDir="$Net48AcadDir"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $dll = Join-Path $net48Out 'HydroComplete.Civil3D.dll'
    if (-not (Test-Path $dll)) {
        throw "net48 build output not found at $dll"
    }

    $net48Contents = Join-Path $bundleContents 'net48'
    New-Item -ItemType Directory -Force -Path $net48Contents | Out-Null
    Copy-Item $dll $net48Contents -Force
    Copy-Item (Join-Path $eng 'HydroComplete.Engine.dll') $net48Contents -Force
    Get-ChildItem $net48Out -Filter '*.dll' |
        Where-Object {
            $_.Name -notmatch '^HydroComplete\.' -and
            $_.Name -notmatch '^(Ac|Aec|Ad)' -and
            $_.Name -notmatch '^acdbmgdbrep\.dll$'
        } |
        Copy-Item -Destination $net48Contents -Force

    Write-Host "net48 bundle staged at $net48Contents"
    Write-Host "PackageContents.xml R24.3 entry points at ./Contents/net48/*.dll"
    exit 0
}
catch {
    Write-Error $_
    exit 1
}
finally {
    Pop-Location
}