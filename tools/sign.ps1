# Sign RemoteTxtViewer.exe with the self-signed code signing cert in CurrentUser\My
# Run after build.bat. Safe to re-run.
param(
    [string]$Exe = "RemoteTxtViewer.exe",
    [string]$SubjectLike = "*RemoteTxtViewer*"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$exePath = Join-Path $root $Exe
if (-not (Test-Path $exePath)) { Write-Output "[CEX] exe not found: $exePath"; exit 1 }

$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -like $SubjectLike } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1

if (-not $cert) {
    $cert = New-SelfSignedCertificate -Type CodeSigningCert `
        -Subject "CN=RemoteTxtViewer Local Build, O=Personal" `
        -KeyUsage DigitalSignature `
        -FriendlyName "RemoteTxtViewer self-signed" `
        -CertStoreLocation Cert:\CurrentUser\My `
        -NotAfter (Get-Date).AddYears(10)
    Write-Output "[CEX] created cert $($cert.Thumbprint)"
} else {
    Write-Output "[CEX] using cert $($cert.Thumbprint)"
}

Export-Certificate -Cert $cert -FilePath (Join-Path $root "RemoteTxtViewer.cer") -Force | Out-Null

$sig = Set-AuthenticodeSignature -FilePath $exePath -Certificate $cert
Write-Output "[CEX] sign status: $($sig.Status)"

$trusted = Get-ChildItem Cert:\CurrentUser\Root -ErrorAction SilentlyContinue |
           Where-Object { $_.Thumbprint -eq $cert.Thumbprint }
if ($trusted) {
    Write-Output "[CEX] cert is already trusted by CurrentUser\Root"
} else {
    Write-Output "[CEX] NOTE: cert not in CurrentUser\Root - Windows will still call it unknown publisher."
    Write-Output "[CEX]       run: tools\trust-cert.bat  (as admin is NOT needed, but a UAC-like confirm appears)"
}
