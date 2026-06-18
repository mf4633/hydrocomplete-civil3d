# Continuous integration: test, build, and verify the auto-load bundle manifest.
# Exits non-zero on any failure.
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$manifest = Join-Path $root 'dist\HydroComplete.bundle\PackageContents.xml'

$expectedSeries = @('R25.0', 'R25.1')
$net48BundleDll = Join-Path $root 'dist\HydroComplete.bundle\Contents\net48\HydroComplete.Civil3D.dll'
$commandsDir = Join-Path $root 'src\HydroComplete.Civil3D\Commands'
$commandPattern = '\[CommandMethod\("([^"]+)"\)\]'
$expectedCommands = Get-ChildItem $commandsDir -Filter '*.cs' -Recurse |
    ForEach-Object {
        [regex]::Matches((Get-Content $_.FullName -Raw), $commandPattern) |
            ForEach-Object { $_.Groups[1].Value }
    } |
    Where-Object { $_ -like 'HC_*' } |
    Sort-Object -Unique

function Invoke-Step {
    param(
        [string]$Name,
        [scriptblock]$Action
    )
    Write-Host ""
    Write-Host "==> $Name"
    & $Action
    if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE"
    }
}

function Test-BundleManifest {
    if (-not (Test-Path $manifest)) {
        throw "PackageContents.xml not found at $manifest"
    }

    $text = Get-Content $manifest -Raw
    foreach ($series in $expectedSeries) {
        if ($text -notmatch "SeriesMin=`"$series`"") {
            throw "PackageContents.xml missing ComponentEntry for $series"
        }
        Write-Host "  Series $series : OK"
    }

    if (Test-Path $net48BundleDll) {
        if ($text -notmatch 'SeriesMin="R24.3"') {
            throw 'PackageContents.xml missing ComponentEntry for R24.3 (net48 bundle present)'
        }
        Write-Host '  Series R24.3 : OK'
    }
    else {
        Write-Host '  Series R24.3 : skipped (no net48 bundle staged)'
    }

    foreach ($command in $expectedCommands) {
        if ($text -notmatch "Local=`"$command`"") {
            throw "PackageContents.xml missing command $command"
        }
        Write-Host "  Command $command : OK"
    }

    Write-Host "  Manifest: OK ($manifest)"
}

Push-Location $root
try {
    Invoke-Step "dotnet test ($Configuration)" {
        dotnet restore 'HydroComplete.Civil3D.sln'
        dotnet test 'HydroComplete.Civil3D.sln' -c $Configuration --no-restore
    }

    $net8AcadDir = if ($env:ACAD_DIR) { $env:ACAD_DIR } else { 'C:\Program Files\Autodesk\AutoCAD 2026\' }
    $net48AcadDir = if ($env:ACAD_2024_DIR) { $env:ACAD_2024_DIR } else { 'C:\Program Files\Autodesk\AutoCAD 2024\' }
    $acadMgd = Join-Path $net8AcadDir 'AcMgd.dll'
    if (Test-Path $acadMgd) {
        Invoke-Step "dotnet build HydroComplete.Civil3D net8 ($Configuration)" {
            dotnet build 'src\HydroComplete.Civil3D\HydroComplete.Civil3D.csproj' -c $Configuration -p:BuildNet48=false -p:Net8AcadDir="$net8AcadDir"
        }
    }
    else {
        Write-Host ""
        Write-Host "==> Skipping Civil3D net8 build (AutoCAD 2025/2026 not installed on this runner)"
        Write-Host "    Expected: $acadMgd"
    }

    # net48 compiles offline via AutoCAD.NET 24.3.0 NuGet when AutoCAD 2024 is not installed.
    Invoke-Step "dotnet build HydroComplete.Civil3D net48 ($Configuration)" {
        dotnet build 'src\HydroComplete.Civil3D\HydroComplete.Civil3D.csproj' -c $Configuration -p:BuildNet48=true -p:Net48AcadDir="$net48AcadDir"
        & (Join-Path $root 'scripts\build-net48.ps1') -Configuration $Configuration -Net48AcadDir $net48AcadDir
    }

    Invoke-Step 'Verify PackageContents.xml' {
        Test-BundleManifest
    }

    Write-Host ""
    Write-Host "CI passed."
    exit 0
}
catch {
    Write-Error $_
    exit 1
}
finally {
    Pop-Location
}