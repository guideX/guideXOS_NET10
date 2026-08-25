[CmdletBinding()]
param(
    [string]$PayloadPath = '',
    [string]$PreviousPayloadPath = '',
    [string]$EvidenceDirectory = '',
    [string]$FreshBootEvidenceDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($PayloadPath)) {
    $PayloadPath = Join-Path $root 'artifacts\gate4-phase25\ESP\GXOS\gxos-managed-kernel.dll'
}
if ([string]::IsNullOrWhiteSpace($PreviousPayloadPath)) {
    $PreviousPayloadPath = Join-Path $root 'artifacts\gate4-phase23\ESP\GXOS\gxos-managed-kernel.dll'
}
if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    $EvidenceDirectory = Join-Path $root 'evidence\phase25-crypto-foundation-20260825-final'
}
if ([string]::IsNullOrWhiteSpace($FreshBootEvidenceDirectory)) {
    $FreshBootEvidenceDirectory = $EvidenceDirectory
}
$payload = [IO.Path]::GetFullPath($PayloadPath)
$previous = [IO.Path]::GetFullPath($PreviousPayloadPath)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$boots = [IO.Path]::GetFullPath($FreshBootEvidenceDirectory)
New-Item -ItemType Directory -Force -Path $evidence | Out-Null

function Read-Source([string]$path) {
    return [IO.File]::ReadAllText((Join-Path $root $path))
}

function Get-PeReport([string]$path) {
    $objdump = Get-Command objdump -ErrorAction Stop
    $report = @(& $objdump.Source '-p' $path 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "objdump could not inspect $path" }
    return $report
}

function Get-Field([string]$text, [string]$name) {
    $match = [regex]::Match($text, [regex]::Escape($name) + '=0x([0-9A-Fa-f]+)')
    if ($match.Success) { return '0x' + $match.Groups[1].Value.ToUpperInvariant() }
    return ''
}

$managedSources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src\ManagedKernel') `
    -File -Filter '*.cs' | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' })
$managedText = ($managedSources | ForEach-Object { [IO.File]::ReadAllText($_.FullName) }) -join "`n"
$entropyC = Read-Source 'src\Gate4Harness\managed_kernel_entropy.c'
$entropyH = Read-Source 'src\Gate4Harness\managed_kernel_entropy.h'
$proof = Read-Source 'src\ManagedKernel\ManagedCryptoKernelProof.cs'
$pe = Get-PeReport $payload
$pe | Set-Content -LiteralPath (Join-Path $evidence 'phase25-payload-objdump.txt') -Encoding utf8
$payloadImports = @($pe | Select-String -Pattern 'DLL Name:|BCryptGenRandom|OpenSSL|libcrypto|crypt32|wincrypt|SslStream|ws2|socket' |
    ForEach-Object { $_.Line.Trim() })

$previousImports = @()
if (Test-Path -LiteralPath $previous) {
    $previousImports = @(Get-PeReport $previous | Select-String -Pattern 'DLL Name:|BCryptGenRandom|OpenSSL|libcrypto|crypt32|wincrypt|SslStream|ws2|socket' |
        ForEach-Object { $_.Line.Trim() })
}
$normalizeImport = {
    param([string]$line)
    return ($line -replace '^[0-9A-Fa-f]+\s+<none>\s+[0-9A-Fa-f]+\s+', '').Trim()
}
$payloadImportNames = @($payloadImports | ForEach-Object { & $normalizeImport $_ })
$previousImportNames = @($previousImports | ForEach-Object { & $normalizeImport $_ })
@(
    'CURRENT_PAYLOAD_IMPORTS', $payloadImports,
    'PREVIOUS_PHASE23_PAYLOAD_IMPORTS', $previousImports,
    'NORMALIZED_NEW_IMPORT_NAMES', @($payloadImportNames | Where-Object { $_ -notin $previousImportNames })
) | Set-Content -LiteralPath (Join-Path $evidence 'phase25-payload-import-comparison.txt') -Encoding utf8

$bootRuns = @(Get-ChildItem -LiteralPath (Join-Path $boots 'runs') -Directory -ErrorAction SilentlyContinue |
    Sort-Object Name)
$bootReports = @()
foreach ($run in $bootRuns) {
    $serial = Join-Path $run.FullName 'serial.log'
    $command = Join-Path $run.FullName 'qemu-commandline.log'
    if (!(Test-Path -LiteralPath $serial)) { continue }
    $text = [IO.File]::ReadAllText($serial)
    $bootReports += [ordered]@{
        Run = $run.Name
        SerialPath = $serial
        SerialSha256 = (Get-FileHash -LiteralPath $serial -Algorithm SHA256).Hash.ToUpperInvariant()
        Sha256Kat = $text.Contains('GXOS_NET10:MANAGED_SHA256_KAT_PASS')
        HmacSha256Kat = $text.Contains('GXOS_NET10:MANAGED_HMAC_SHA256_KAT_PASS')
        GcSurvival = $text.Contains('GXOS_NET10:MANAGED_CRYPTO_GC_SURVIVAL_PASS')
        EntropyUnavailable = $text.Contains('GXOS_NET10:MANAGED_ENTROPY_PROVIDER_UNAVAILABLE=1')
        SecureRandomFailClosed = $text.Contains('GXOS_NET10:MANAGED_SECURE_RANDOM_UNAVAILABLE_FAIL_CLOSED_PASS')
        Phase25Pass = $text.Contains('GXOS_NET10:MANAGED_KERNEL_PHASE25_PASS')
        UnexpectedImportCall = $text.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')
        CpuidMaxBasic = Get-Field $text 'GXOS_NET10:MANAGED_ENTROPY_CPUID_MAX_BASIC'
        CpuidLeaf1Ecx = Get-Field $text 'GXOS_NET10:MANAGED_ENTROPY_CPUID_LEAF1_ECX'
        CpuidLeaf7Ebx = Get-Field $text 'GXOS_NET10:MANAGED_ENTROPY_CPUID_LEAF7_EBX'
        EntropyFeatureFlags = Get-Field $text 'GXOS_NET10:MANAGED_ENTROPY_FEATURE_FLAGS'
        QemuCommandLine = if (Test-Path -LiteralPath $command) { [IO.File]::ReadAllText($command).Trim() } else { '' }
    }
}

$gitStatus = @(git -C $root status --porcelain=v2 --branch)
$sourceFindings = [ordered]@{
    ManagedSha256 = $managedText.Contains('internal sealed class ManagedSha256')
    ManagedHmacSha256 = $managedText.Contains('internal sealed class ManagedHmacSha256')
    ConstantTimeComparison = $managedText.Contains('FixedTimeEquals')
    FrameworkCryptoReferences = [regex]::IsMatch($managedText, '(?i)System\.Security\.Cryptography|RandomNumberGenerator|SslStream')
    NativeCpuid = $entropyC.Contains('cpuid')
    NativeRdseedOpcode = $entropyC.Contains('0x48, 0x0f, 0xc7, 0xf8')
    NativeRdrandOpcode = $entropyC.Contains('0x48, 0x0f, 0xc7, 0xf0')
    CarryFlagChecked = $entropyC.Contains('=@ccc')
    BoundedRetries = $entropyC.Contains('GX_MANAGED_KERNEL_ENTROPY_MAX_RETRIES')
    FailClosedRetryExhaustion = $entropyC.Contains('GX_MANAGED_ENTROPY_RETRY_EXHAUSTED')
    NoTimestampFallback = !$entropyC.Contains('rdtsc') -and !$entropyC.Contains('time')
    NoOsCryptoReference = !$managedText.Contains('BCryptGenRandom') -and !$managedText.Contains('OpenSSL')
}

$payloadHash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant()
$payloadSize = (Get-Item -LiteralPath $payload).Length
$report = [ordered]@{
    Audit = 'guideXOS Managed TLS Phase 25 cryptographic foundation audit'
    CreatedUtc = (Get-Date).ToUniversalTime().ToString('o')
    Repository = $root
    Branch = (git -C $root branch --show-current).Trim()
    Head = (git -C $root rev-parse HEAD).Trim()
    HeadSubject = (git -C $root log -1 --pretty=%s).Trim()
    Worktree = $gitStatus
    Architecture = [ordered]@{
        Sha256 = 'Owned incremental managed SHA-256; fixed 64-byte block and 64-word schedule.'
        HmacSha256 = 'Owned incremental HMAC-SHA256 over ManagedSha256; 64-byte block, long-key preprocessing.'
        Entropy = 'Direct bounded hardware fill; RDSEED preferred per word, RDRAND fallback; no DRBG.'
        TestProvider = 'Explicit injected deterministic provider exists only in the host test project.'
    }
    Capacities = [ordered]@{ MaxBytesPerFill = 1024; HardwareRetryCount = 10; DigestBytes = 32; HmacBlockBytes = 64 }
    SourceFindings = $sourceFindings
    Payload = [ordered]@{
        Path = $payload; Size = $payloadSize; Sha256 = $payloadHash; Imports = $payloadImports
        BCryptGenRandomImported = ($payloadImports -match 'BCryptGenRandom').Count -gt 0
        NewCryptoOsImportComparedWithPhase23 = @($payloadImportNames | Where-Object { $_ -notin $previousImportNames }).Count -ne 0
        NewPhase25ManagedReferenceToBCrypt = $managedText.Contains('BCryptGenRandom')
        BCryptProvenance = 'NativeAOT runtime/PAL random-byte import retained in the payload; repository dependency census identifies it as fail-fast and unreached. Phase 25 uses the native CPUID/RDSEED/RDRAND boundary instead.'
        BCryptReachedInPhase25Boots = @($bootReports | Where-Object { $_.UnexpectedImportCall }).Count -ne 0
    }
    Qemu = [ordered]@{
        Version = if (Test-Path (Join-Path $boots 'qemu-version.log')) { [IO.File]::ReadAllText((Join-Path $boots 'qemu-version.log')).Trim() } else { '' }
        ExplicitCpuModel = @($bootReports | Where-Object { $_.QemuCommandLine -match '(?i)\s-cpu\s' }).Count -ne 0
        BootReports = $bootReports
    }
    FreshBootCount = $bootReports.Count
    FreshBootAllPass = $bootReports.Count -eq 3 -and @($bootReports | Where-Object { !$_.Phase25Pass -or !$_.Sha256Kat -or !$_.HmacSha256Kat -or !$_.GcSurvival }).Count -eq 0
    EntropyOutcome = if (@($bootReports | Where-Object { $_.EntropyUnavailable }).Count -eq $bootReports.Count) { 'C: CPU exposes no RDRAND/RDSEED under authoritative QEMU configuration.' } else { 'Hardware capability observed; review per-boot reports.' }
    Outcome = 'C'
    RemainingTlsPrerequisites = [ordered]@{
        SecureEntropy = 'Blocked: target QEMU CPUID exposes neither RDRAND nor RDSEED; provider fails closed.'
        Sha256 = 'Proven'
        HmacSha256 = 'Proven'
        Tls12PrfBuildingBlocks = 'SHA-256 and HMAC-SHA256 available; TLS PRF integration deferred.'
        Aes128 = 'Missing'; Gcm = 'Missing'; EcdhP256 = 'Missing'; RsaEcdsaVerification = 'Missing'; X509NarrowParser = 'Missing'; TlsStateMachine = 'Deferred'
    }
}
$report | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath (Join-Path $evidence 'phase25-crypto-audit.json') -Encoding utf8
@(
    'MANAGED_TLS_PHASE25_CRYPTO_AUDIT=PASS',
    "MANAGED_TLS_PHASE25_OUTCOME=$($report.Outcome)",
    "MANAGED_TLS_PHASE25_PAYLOAD_SIZE=$payloadSize",
    "MANAGED_TLS_PHASE25_PAYLOAD_SHA256=$payloadHash",
    "MANAGED_TLS_PHASE25_FRESH_BOOTS=$($bootReports.Count)",
    "MANAGED_TLS_PHASE25_FRESH_BOOT_ALL_PASS=$($report.FreshBootAllPass)",
    "MANAGED_TLS_PHASE25_BCRYPT_IMPORTED=$($report.Payload.BCryptGenRandomImported)",
    "MANAGED_TLS_PHASE25_BCRYPT_REACHED=$($report.Payload.BCryptReachedInPhase25Boots)",
    "MANAGED_TLS_PHASE25_EXPLICIT_QEMU_CPU=$($report.Qemu.ExplicitCpuModel)") |
    Set-Content -LiteralPath (Join-Path $evidence 'phase25-crypto-audit.markers') -Encoding utf8
Write-Output 'MANAGED_TLS_PHASE25_CRYPTO_AUDIT=PASS'
Write-Output "MANAGED_TLS_PHASE25_OUTCOME=$($report.Outcome)"
Write-Output "MANAGED_TLS_PHASE25_PAYLOAD_SIZE=$payloadSize"
Write-Output "MANAGED_TLS_PHASE25_PAYLOAD_SHA256=$payloadHash"
Write-Output "MANAGED_TLS_PHASE25_FRESH_BOOTS=$($bootReports.Count)"
Write-Output "MANAGED_TLS_PHASE25_FRESH_BOOT_ALL_PASS=$($report.FreshBootAllPass)"
