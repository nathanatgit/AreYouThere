param([string] $Repository = 'nathanatgit/AreYouThere')
$ErrorActionPreference = 'Stop'
# Secrets are passed through stdin, never printed or written to the repository.
gh auth status
if ($LASTEXITCODE -ne 0) { throw 'GitHub CLI authentication required.' }
$existing = @(Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.FriendlyName -eq 'AreYouThere sideload signing' -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddMonths(3) })
if ($existing.Count -gt 1) { throw 'Multiple signing certificates found; select one manually.' }
$cert = $existing | Select-Object -First 1
if (-not $cert) {
    $cert = New-SelfSignedCertificate -Type Custom -Subject 'CN=65060FB2-9417-4C15-9ABF-D5188EB4DF3A' -FriendlyName 'AreYouThere sideload signing' -CertStoreLocation Cert:\CurrentUser\My -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 -KeyExportPolicy Exportable -KeyUsage DigitalSignature -NotAfter (Get-Date).AddYears(3) -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3','2.5.29.19={text}')
}
$passwordBytes = [byte[]]::new(32)
[Security.Cryptography.RandomNumberGenerator]::Fill($passwordBytes)
$password = [Convert]::ToBase64String($passwordBytes)
$pfx = $cert.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $password)
[Convert]::ToBase64String($pfx) | gh secret set SIGNING_CERT_BASE64 --repo $Repository
if ($LASTEXITCODE -ne 0) { throw 'Certificate secret upload failed.' }
$password | gh secret set SIGNING_CERT_PASSWORD --repo $Repository
if ($LASTEXITCODE -ne 0) { throw 'Password secret upload failed; rerun this script.' }
Write-Host "Signing secrets configured. Public certificate thumbprint: $($cert.Thumbprint)"
Write-Host "Certificate expires: $($cert.NotAfter.ToString('yyyy-MM-dd'))"
