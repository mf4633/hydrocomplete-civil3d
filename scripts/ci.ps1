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
$expectedCommands = @(
    'HC_ABOUT',
    'HC_PIPES',
    'HC_PIPES_WRITE',
    'HC_RATIONAL',
    'HC_HGL',
    'HC_REPORT',
    'HC_REPORT_PDF',
    'HC_ATLAS14',
    'HC_LICENSE'
)

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
        dotnet test 'HydroComplete.Civil3D.sln' -c $Configuration --no-restore:$false
    }

    Invoke-Step "dotnet build HydroComplete.Civil3D ($Configuration)" {
        dotnet build 'src\HydroComplete.Civil3D\HydroComplete.Civil3D.csproj' -c $Configuration
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