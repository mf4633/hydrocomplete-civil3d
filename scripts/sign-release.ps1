# Sign HydroComplete bundle DLLs with an Authenticode certificate.
# Run AFTER Release build, BEFORE release.ps1 zips the bundle.
#
# Prerequisites:
#   - Code-signing cert installed (Cert:\CurrentUser\My or Cert:\LocalMachine\My)
#   - Windows SDK signtool.exe on PATH (or set $env:SIGNTOOL)
#
# Usage:
#   $env:HC_SIGN_CERT_THUMBPRINT = 'YOUR_CERT_THUMBPRINT'
#   .\scripts\sign-release.ps1
#   .\scripts\release.ps1
param(
    [string]$CertThumbprint = $env:HC_SIGN_CERT_THUMBPRINT,
    [string]$TimestampUrl = 'http://timestamp.digicert.com',
    [string]$BundleContents = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

if (-not $BundleContents) {
    $BundleContents = Join-Path $root 'dist\HydroComplete.bundle\Contents'
}

function Find-SignTool {
    if ($env:SIGNTOOL -and (Test-Path $env:SIGNTOOL)) { return $env:SIGNTOOL }
    $kits = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
    if (Test-Path $kits) {
        $latest = Get-ChildItem $kits -Directory | Sort-Object Name -Descending | Select-Object -First 1
        $candidate = Join-Path $latest.FullName 'x64\signtool.exe'
        if (Test-Path $candidate) { return $candidate }
    }
    $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

if (-not $CertThumbprint) {
    Write-Host @"
HC_SIGN_CERT_THUMBPRINT is not set.

To list available code-signing certs:
  Get-ChildItem Cert:\CurrentUser\My | Where-Object { `$_.EnhancedKeyUsageList -match 'Code Signing' } | Format-List Subject, Thumbprint, NotAfter

Then:
  `$env:HC_SIGN_CERT_THUMBPRINT = 'THUMBPRINT'
  .\scripts\sign-release.ps1
  .\scripts\release.ps1
"@
    exit 2
}

$signtool = Find-SignTool
if (-not $signtool) {
    Write-Error 'signtool.exe not found. Install Windows SDK or set $env:SIGNTOOL.'
}

$dlls = @(
    (Join-Path $BundleContents 'HydroComplete.Civil3D.dll'),
    (Join-Path $BundleContents 'HydroComplete.Engine.dll'),
    (Join-Path $BundleContents 'net48\HydroComplete.Civil3D.dll'),
    (Join-Path $BundleContents 'net48\HydroComplete.Engine.dll')
) | Where-Object { Test-Path $_ }

if ($dlls.Count -eq 0) {
    Write-Error "No DLLs to sign under $BundleContents. Run ci.ps1 or release.ps1 build step first."
}

Write-Host "Signing $($dlls.Count) DLL(s) with thumbprint $CertThumbprint"
foreach ($dll in $dlls) {
    Write-Host "  -> $(Split-Path $dll -Leaf)"
    & $signtool sign /fd SHA256 /tr $TimestampUrl /td SHA256 /sha1 $CertThumbprint $dll
    if ($LASTEXITCODE -ne 0) {
        Write-Error "signtool sign failed for $dll (exit $LASTEXITCODE)"
    }
    & $signtool verify /pa $dll
    if ($LASTEXITCODE -ne 0) {
        Write-Error "signtool verify failed for $dll (exit $LASTEXITCODE)"
    }
}

Write-Host ""
Write-Host "All DLLs signed and verified. Run .\scripts\release.ps1 to rebuild the zip." -ForegroundColor Green