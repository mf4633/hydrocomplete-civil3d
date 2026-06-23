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
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $Action
        $code = if ($null -ne $LASTEXITCODE) { $LASTEXITCODE } elseif ($?) { 0 } else { 1 }
        if ($code -ne 0) {
            throw "$Name failed with exit code $code"
        }
    }
    finally {
        $ErrorActionPreference = $prevEap
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

    # Trim trailing slash — PowerShell eats the closing quote when the path ends in \
    $net8AcadDir = if ($env:ACAD_DIR) { $env:ACAD_DIR } else { 'C:\Program Files\Autodesk\AutoCAD 2026' }
    $net48AcadDir = if ($env:ACAD_2024_DIR) { $env:ACAD_2024_DIR } else { 'C:\Program Files\Autodesk\AutoCAD 2024' }
    $net8AcadDir = $net8AcadDir.TrimEnd('\')
    $net48AcadDir = $net48AcadDir.TrimEnd('\')
    $acadMgd = Join-Path $net8AcadDir 'AcMgd.dll'
    if (Test-Path $acadMgd) {
        Invoke-Step "dotnet build HydroComplete.Civil3D net8 ($Configuration)" {
            $buildArgs = @(
                'build', 'src\HydroComplete.Civil3D\HydroComplete.Civil3D.csproj',
                '-c', $Configuration,
                '-p:BuildNet48=false'
            )
            # Only override Net8AcadDir when non-default — paths with spaces need quoting
            if ($env:ACAD_DIR) {
                $buildArgs += "-p:Net8AcadDir=$net8AcadDir\"
            }
            dotnet @buildArgs
        }
    }
    else {
        Write-Host ""
        Write-Host "==> Skipping Civil3D net8 build (AutoCAD 2025/2026 not installed on this runner)"
        Write-Host "    Expected: $acadMgd"
    }

    # net48 compiles offline via AutoCAD.NET 24.3.0 NuGet when AutoCAD 2024 is not installed.
    Invoke-Step "dotnet build HydroComplete.Civil3D net48 ($Configuration)" {
        $net48Args = @(
            'build', 'src\HydroComplete.Civil3D\HydroComplete.Civil3D.csproj',
            '-c', $Configuration,
            '-p:BuildNet48=true'
        )
        if ($env:ACAD_2024_DIR) {
            $net48Args += "-p:Net48AcadDir=$net48AcadDir\"
        }
        dotnet @net48Args
        $net48StageDir = if ($env:ACAD_2024_DIR) { $net48AcadDir + '\' } else { $null }
        if ($net48StageDir) {
            & (Join-Path $root 'scripts\build-net48.ps1') -Configuration $Configuration -Net48AcadDir $net48StageDir
        }
        else {
            & (Join-Path $root 'scripts\build-net48.ps1') -Configuration $Configuration
        }
    }

    Invoke-Step 'Verify PackageContents.xml' {
        Test-BundleManifest
    }

    # Build the Rust WASM DAG editor if the crate exists alongside this repo
    $dagCrate = Join-Path (Split-Path -Parent $root) 'hydrocomplete-dag'
    if (Test-Path (Join-Path $dagCrate 'Cargo.toml')) {
        $wasmPack = Get-Command wasm-pack -ErrorAction SilentlyContinue
        if ($wasmPack) {
            Invoke-Step 'Build DAG WASM editor' {
                Push-Location $dagCrate
                try {
                    & cargo test --lib --quiet 2>&1 | Out-Host
                    if ($LASTEXITCODE -ne 0) { throw 'cargo test (hydrocomplete-dag) failed' }
                    & wasm-pack build --target web --out-dir www/pkg --release 2>&1 |
                        Where-Object { $_ -notmatch '^\[INFO\]' } |
                        ForEach-Object { Write-Host $_ }
                    # Copy into bundle
                    $dagDst = Join-Path $root 'dist\HydroComplete.bundle\Contents\dag'
                    if (Test-Path $dagDst) { Remove-Item $dagDst -Recurse -Force }
                    New-Item -ItemType Directory -Force -Path $dagDst | Out-Null
                    Copy-Item 'www\index.html' $dagDst -Force
                    Copy-Item 'www\pkg' $dagDst -Recurse -Force
                    Write-Host "  DAG editor staged to $dagDst"
                } finally { Pop-Location }
            }
        } else {
            Write-Host ""
            Write-Host "==> Skipping DAG WASM build (wasm-pack not on PATH)"
        }
    } else {
        Write-Host ""
        Write-Host "==> Skipping DAG WASM build (hydrocomplete-dag not found at $dagCrate)"
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