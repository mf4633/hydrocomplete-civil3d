# Automated release-readiness checks that do NOT require Civil 3D.
# Exits non-zero on any failure.
param(
    [switch]$SkipBuild,
    [switch]$SkipHttp,
    [string]$ApiBase = 'https://hydrocomplete.com'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$failures = New-Object System.Collections.Generic.List[string]
$passes = New-Object System.Collections.Generic.List[string]

function Add-Failure {
    param([string]$Message)
    $failures.Add($Message) | Out-Null
    Write-Host "FAIL: $Message" -ForegroundColor Red
}

function Add-Pass {
    param([string]$Message)
    $passes.Add($Message) | Out-Null
    Write-Host "OK:   $Message" -ForegroundColor Green
}

Write-Host "HydroComplete validation-preflight (no Civil 3D required)"
Write-Host "Root: $root"
Write-Host ""

Push-Location $root
try {
    Write-Host "==> Engine unit tests"
    dotnet test -c Release --no-restore 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        dotnet test -c Release
        Add-Failure "dotnet test failed (exit $LASTEXITCODE)"
    }
    else {
        Add-Pass "dotnet test passed"
    }

    if (-not $SkipBuild) {
        Write-Host ""
        Write-Host "==> CI script (build + manifest)"
        & (Join-Path $root 'scripts\ci.ps1') -Configuration Release
        if ($LASTEXITCODE -ne 0) {
            Add-Failure "ci.ps1 failed (exit $LASTEXITCODE)"
        }
        else {
            Add-Pass "ci.ps1 passed"
        }
    }

    Write-Host ""
    Write-Host "==> App Store preflight"
    & (Join-Path $root 'scripts\app-store-preflight.ps1')
    if ($LASTEXITCODE -ne 0) {
        Add-Failure "app-store-preflight.ps1 failed (exit $LASTEXITCODE)"
    }
    else {
        Add-Pass "app-store-preflight.ps1 passed"
    }

    Write-Host ""
    Write-Host "==> Bundle file sizes"
    $c3dDll = Join-Path $root 'dist\HydroComplete.bundle\Contents\HydroComplete.Civil3D.dll'
    $engDll = Join-Path $root 'dist\HydroComplete.bundle\Contents\HydroComplete.Engine.dll'
    foreach ($dll in @($c3dDll, $engDll)) {
        if (-not (Test-Path $dll)) {
            Add-Failure "Missing $dll"
        }
        elseif ((Get-Item $dll).Length -lt 4096) {
            Add-Failure "$(Split-Path $dll -Leaf) suspiciously small"
        }
        else {
            Add-Pass "$(Split-Path $dll -Leaf) present ($([math]::Round((Get-Item $dll).Length / 1KB)) KB)"
        }
    }

    if (-not $SkipHttp) {
        Write-Host ""
        Write-Host "==> HTTP smoke (ApiBase: $ApiBase)"

        try {
            $civil3d = Invoke-WebRequest -Uri 'https://hydrocomplete.com/civil3d' -UseBasicParsing -TimeoutSec 20
            if ($civil3d.StatusCode -eq 200) {
                Add-Pass 'hydrocomplete.com/civil3d returns 200'
            }
            else {
                Add-Failure "hydrocomplete.com/civil3d returned $($civil3d.StatusCode)"
            }
        }
        catch {
            Add-Failure "hydrocomplete.com/civil3d unreachable: $($_.Exception.Message)"
        }

        try {
            $privacy = Invoke-WebRequest -Uri 'https://hydrocomplete.com/privacy.html' -UseBasicParsing -TimeoutSec 20
            if ($privacy.StatusCode -eq 200) {
                Add-Pass 'hydrocomplete.com/privacy.html returns 200'
            }
            else {
                Add-Failure "privacy.html returned $($privacy.StatusCode)"
            }
        }
        catch {
            Add-Failure "privacy.html unreachable: $($_.Exception.Message)"
        }

        try {
            $body = '{"licenseKey":"hc_live_beta_tester01","features":["reports","export","civil3d"]}'
            $lic = Invoke-WebRequest -Uri "$ApiBase/api/licensing/validate" `
                -Method POST `
                -ContentType 'application/json' `
                -Body $body `
                -UseBasicParsing `
                -TimeoutSec 20
            if ($lic.StatusCode -eq 200) {
                $json = $lic.Content | ConvertFrom-Json
                if ($json.valid -eq $true) {
                    Add-Pass 'licensing API accepts hc_live_beta_tester01'
                }
                else {
                    Add-Failure 'licensing API returned 200 but valid:false for hc_live_beta_tester01'
                }
            }
            else {
                Add-Failure "licensing validate returned $($lic.StatusCode)"
            }
        }
        catch {
            $status = $null
            if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
            if ($status -eq 403) {
                Add-Failure 'licensing API rejects hc_live_beta_tester01 (deploy updated licensing.js)'
            }
            else {
                Add-Failure "licensing API unreachable: $($_.Exception.Message)"
            }
        }
    }
    else {
        Write-Host "INFO: HTTP checks skipped (-SkipHttp)"
    }
}
finally {
    Pop-Location
}

Write-Host ""
Write-Host "Summary: $($passes.Count) passed, $($failures.Count) failed"
if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

Write-Host "validation-preflight PASSED" -ForegroundColor Green
exit 0