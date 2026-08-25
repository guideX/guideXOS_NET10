[CmdletBinding()]
param(
    [string]$PayloadPath = '',
    [string]$EvidenceDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($PayloadPath)) {
    $PayloadPath = Join-Path $root 'artifacts\gate4-phase23\ESP\GXOS\gxos-managed-kernel.dll'
}
$payload = [IO.Path]::GetFullPath($PayloadPath)
if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    $EvidenceDirectory = Join-Path $root 'evidence\phase24-crypto-audit-20260825'
}
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Force -Path $evidence | Out-Null

function Find-FrameworkType([string]$assemblyQualifiedName) {
    $type = [Type]::GetType($assemblyQualifiedName, $false)
    return [ordered]@{
        Name = $assemblyQualifiedName
        AvailableInHost = ($null -ne $type)
        Assembly = if ($null -ne $type) { $type.Assembly.GetName().Name } else { '' }
    }
}

$cryptoTypes = @(
    (Find-FrameworkType 'System.Security.Cryptography.SHA256, System.Security.Cryptography.Algorithms'),
    (Find-FrameworkType 'System.Security.Cryptography.HMACSHA256, System.Security.Cryptography.Algorithms'),
    (Find-FrameworkType 'System.Security.Cryptography.RandomNumberGenerator, System.Security.Cryptography'),
    (Find-FrameworkType 'System.Security.Cryptography.Aes, System.Security.Cryptography.Algorithms'),
    (Find-FrameworkType 'System.Security.Cryptography.AesGcm, System.Security.Cryptography'),
    (Find-FrameworkType 'System.Security.Cryptography.RSA, System.Security.Cryptography.Algorithms'),
    (Find-FrameworkType 'System.Security.Cryptography.ECDsa, System.Security.Cryptography.Algorithms'),
    (Find-FrameworkType 'System.Security.Cryptography.ECDiffieHellman, System.Security.Cryptography.Algorithms'),
    (Find-FrameworkType 'System.Security.Cryptography.CryptographicOperations, System.Security.Cryptography.Primitives'),
    (Find-FrameworkType 'System.Numerics.BigInteger, System.Runtime.Numerics')
)

$sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $root 'src\ManagedKernel') -File -Filter '*.cs' |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' })
$source = ($sourceFiles | ForEach-Object { [IO.File]::ReadAllText($_.FullName) }) -join "`n"
$sourceFindings = [ordered]@{
    RepositoryOwnedSha256 = [regex]::IsMatch($source, '(?i)class\s+.*Sha256|Sha256')
    RepositoryOwnedHmacSha256 = [regex]::IsMatch($source, '(?i)Hmac|HMAC')
    RepositoryOwnedAes = [regex]::IsMatch($source, '(?i)\bAes(Gcm)?\b|AES')
    RepositoryOwnedRsa = [regex]::IsMatch($source, '(?i)\bRsa\b|RSA')
    RepositoryOwnedEcc = [regex]::IsMatch($source, '(?i)\b(Ecdsa|Ecdh|Ecc|P256)\b')
    FrameworkCryptoReferences = [regex]::IsMatch($source, '(?i)System\.Security\.Cryptography|SslStream|RandomNumberGenerator')
    EntropyBoundaryReferences = [regex]::IsMatch($source, '(?i)Rdrand|Rdseed|EFI_RNG|RandomNumber|Entropy|BCryptGenRandom')
}

$payloadImports = @()
$payloadHash = ''
$payloadSize = 0
if (Test-Path -LiteralPath $payload) {
    $payloadItem = Get-Item -LiteralPath $payload
    $payloadSize = $payloadItem.Length
    $payloadHash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant()
    $objdump = Get-Command objdump -ErrorAction Stop
    $peReport = & $objdump.Source '-p' $payload 2>&1
    if ($LASTEXITCODE -ne 0) { throw "objdump could not inspect $payload" }
    $payloadImports = @($peReport | Select-String -Pattern 'DLL Name:|BCryptGenRandom|RAND|OpenSSL|libcrypto|crypt32|wincrypt|SslStream' |
        ForEach-Object { $_.Line.Trim() })
    $peReport | Set-Content -LiteralPath (Join-Path $evidence 'payload-objdump.txt') -Encoding utf8
}

$gitStatus = @(git -C $root status --porcelain=v2 --branch)
$gitHead = (git -C $root rev-parse HEAD).Trim()
$gitSubject = (git -C $root log -1 --pretty=%s).Trim()

$report = [ordered]@{
    Audit = 'guideXOS Managed TLS Phase 24 cryptographic capability audit'
    CreatedUtc = (Get-Date).ToUniversalTime().ToString('o')
    Repository = $root
    Branch = (git -C $root branch --show-current).Trim()
    Head = $gitHead
    HeadSubject = $gitSubject
    Worktree = $gitStatus
    Payload = [ordered]@{
        Path = $payload
        Size = $payloadSize
        Sha256 = $payloadHash
        Imports = $payloadImports
        BCryptGenRandomImported = ($payloadImports -match 'BCryptGenRandom').Count -gt 0
        BareMetalCryptoCallProven = $false
    }
    FrameworkSurface = $cryptoTypes
    RepositorySourceFindings = $sourceFindings
    ProvenRuntimeCapabilities = [ordered]@{
        Sha256 = $false
        HmacSha256 = $false
        SecureRandomBytes = $false
        Aes = $false
        AesGcm = $false
        AesCbc = $false
        RsaSignatureVerification = $false
        EccP256 = $false
        Ecdh = $false
        Ecdsa = $false
        ConstantTimeComparison = $false
        BigInteger = $false
    }
    HostOnlySurfaceMustNotBeUsed = $true
    FirstBlocker = 'No cryptographically credible client entropy source is installed and proven through the actual bare-metal managed/native boundary.'
    AdditionalBlocker = 'No bare-metal-proven asymmetric primitive exists for authenticating the deterministic TLS peer and performing the selected TLS 1.2 key exchange.'
    Outcome = 'C'
}

$json = $report | ConvertTo-Json -Depth 12
$json | Set-Content -LiteralPath (Join-Path $evidence 'crypto-audit.json') -Encoding utf8
@(
    'MANAGED_TLS_PHASE24_CRYPTO_AUDIT=PASS',
    "MANAGED_TLS_PHASE24_CRYPTO_AUDIT_BRANCH=$($report.Branch)",
    "MANAGED_TLS_PHASE24_CRYPTO_AUDIT_HEAD=$($report.Head)",
    "MANAGED_TLS_PHASE24_PAYLOAD_SIZE=$payloadSize",
    "MANAGED_TLS_PHASE24_PAYLOAD_SHA256=$payloadHash",
    "MANAGED_TLS_PHASE24_BCRYPT_GEN_RANDOM_IMPORTED=$($report.Payload.BCryptGenRandomImported)",
    'MANAGED_TLS_PHASE24_BARE_METAL_CRYPTO_CALL_PROVEN=False',
    'MANAGED_TLS_PHASE24_CSPRNG_PROVEN=False',
    'MANAGED_TLS_PHASE24_ASYMMETRIC_PRIMITIVE_PROVEN=False',
    'MANAGED_TLS_PHASE24_OUTCOME=C'
) | Set-Content -LiteralPath (Join-Path $evidence 'audit.markers') -Encoding utf8

Write-Output 'MANAGED_TLS_PHASE24_CRYPTO_AUDIT=PASS'
Write-Output "MANAGED_TLS_PHASE24_CRYPTO_AUDIT_EVIDENCE=$evidence"
Write-Output "MANAGED_TLS_PHASE24_PAYLOAD_SIZE=$payloadSize"
Write-Output "MANAGED_TLS_PHASE24_PAYLOAD_SHA256=$payloadHash"
Write-Output 'MANAGED_TLS_PHASE24_BARE_METAL_CRYPTO_CALL_PROVEN=False'
Write-Output 'MANAGED_TLS_PHASE24_CSPRNG_PROVEN=False'
Write-Output 'MANAGED_TLS_PHASE24_ASYMMETRIC_PRIMITIVE_PROVEN=False'
Write-Output 'MANAGED_TLS_PHASE24_OUTCOME=C'
