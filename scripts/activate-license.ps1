# Write %APPDATA%\HydroComplete\license.json via production licensing API.
param(
    [string]$Email = "michaelbflynn@gmail.com",
    [string]$Token = "hc_live_beta_tester01",
    [string]$ApiBase = "https://hydrocomplete.com"
)
$ErrorActionPreference = 'Stop'
$dir = Join-Path $env:APPDATA 'HydroComplete'
$path = Join-Path $dir 'license.json'
New-Item -ItemType Directory -Force -Path $dir | Out-Null

$body = @{ licenseKey = $Token; features = @('reports', 'export', 'civil3d') } | ConvertTo-Json -Compress
$r = Invoke-RestMethod -Uri "$ApiBase/api/licensing/validate" -Method POST -ContentType 'application/json' -Body $body -TimeoutSec 30
if (-not $r.valid) { throw "License API returned valid:false" }

$record = @{
    email          = $Email
    token          = $(if ($r.accessToken) { $r.accessToken } else { $Token })
    expires        = $r.license.expires
    lastValidated  = (Get-Date).ToUniversalTime().ToString('o')
    validationMode = 'online'
}
$record | ConvertTo-Json | Set-Content -Path $path -Encoding UTF8
Write-Host "Pro license written: $path"
Write-Host "  Email:   $Email"
Write-Host "  Expires: $($r.license.expires)"
Write-Host "  Mode:    online"