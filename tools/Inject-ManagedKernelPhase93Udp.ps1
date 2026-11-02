[CmdletBinding()]
param(
    [Net.Sockets.UdpClient]$PeerUdp,
    [int]$GuestPort = 0,
    [string]$GuestMac = '525400123456',
    [int]$FirstIdentifier = 1,
    [int]$Count = 32,
    [switch]$IncludeNegativeControls,
    [switch]$ValidateOnly,
    [string]$CapturePath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-U16([byte[]]$bytes, [int]$offset, [int]$value) {
    $bytes[$offset] = [byte](($value -shr 8) -band 0xFF)
    $bytes[$offset + 1] = [byte]($value -band 0xFF)
}

function Read-U16([byte[]]$bytes, [int]$offset) {
    return (([int]$bytes[$offset] -shl 8) -bor [int]$bytes[$offset + 1])
}

function Checksum([byte[]]$bytes, [int]$offset, [int]$length) {
    [uint32]$sum = 0
    for ($index = 0; $index + 1 -lt $length; $index += 2) {
        $sum += ([uint32]([int]$bytes[$offset + $index] -shl 8) -bor
                 [uint32]$bytes[$offset + $index + 1])
        $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    }
    if (($length -band 1) -ne 0) {
        $sum += [uint32]([int]$bytes[$offset + $length - 1] -shl 8)
        $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    }
    while (($sum -shr 16) -ne 0) { $sum = ($sum -band 0xFFFF) + ($sum -shr 16) }
    return [uint16]((-bnot [int]$sum) -band 0xFFFF)
}

function Udp-Checksum([byte[]]$sourceIp, [byte[]]$destinationIp,
                      [byte[]]$udp) {
    [byte[]]$pseudo = New-Object byte[] (12 + $udp.Length)
    [Array]::Copy($sourceIp, 0, $pseudo, 0, 4)
    [Array]::Copy($destinationIp, 0, $pseudo, 4, 4)
    $pseudo[9] = 17
    Write-U16 $pseudo 10 $udp.Length
    [Array]::Copy($udp, 0, $pseudo, 12, $udp.Length)
    $pseudo[18] = 0; $pseudo[19] = 0
    [uint16]$value = Checksum $pseudo 0 $pseudo.Length
    if ($value -eq 0) { return [uint16]0xFFFF }
    return $value
}

function Payload([uint32]$identifier) {
    return ,([byte[]]@(
        0x93, 0xA5, 0x5A, 0xC3,
        (($identifier -shr 24) -band 0xFF),
        (($identifier -shr 16) -band 0xFF),
        (($identifier -shr 8) -band 0xFF), ($identifier -band 0xFF),
        0x91, 0x17, 0x00, 0xFF))
}

function Build-Frame([byte[]]$guestMacBytes, [uint32]$identifier,
                     [int]$destinationPort, [string]$destinationAddress,
                     [switch]$CorruptUdpChecksum) {
    [byte[]]$sourceMac = @(0x02, 0x15, 0, 0, 0, 1)
    [byte[]]$sourceIp = @(10, 15, 0, 2)
    [byte[]]$destinationIp = [Net.IPAddress]::Parse($destinationAddress).GetAddressBytes()
    [byte[]]$payloadBytes = Payload $identifier
    [int]$udpLength = 8 + $payloadBytes.Length
    [byte[]]$udp = New-Object byte[] $udpLength
    Write-U16 $udp 0 24001
    Write-U16 $udp 2 $destinationPort
    Write-U16 $udp 4 $udpLength
    [Array]::Copy($payloadBytes, 0, $udp, 8, $payloadBytes.Length)
    Write-U16 $udp 6 (Udp-Checksum $sourceIp $destinationIp $udp)
    if ($CorruptUdpChecksum) { $udp[6] = $udp[6] -bxor 0x01 }

    [byte[]]$frame = New-Object byte[] 60
    [Array]::Copy($guestMacBytes, 0, $frame, 0, 6)
    [Array]::Copy($sourceMac, 0, $frame, 6, 6)
    $frame[12] = 0x08; $frame[13] = 0x00
    $frame[14] = 0x45
    Write-U16 $frame 16 (20 + $udpLength)
    Write-U16 $frame 18 (0x9300 + ($identifier -band 0xFF))
    $frame[22] = 63; $frame[23] = 17
    [Array]::Copy($sourceIp, 0, $frame, 26, 4)
    [Array]::Copy($destinationIp, 0, $frame, 30, 4)
    Write-U16 $frame 24 (Checksum $frame 14 20)
    [Array]::Copy($udp, 0, $frame, 34, $udp.Length)
    return ,$frame
}

function Validate-ValidFrame([byte[]]$frame, [byte[]]$guestMacBytes,
                             [uint32]$identifier) {
    [byte[]]$sourceIp = @(10, 15, 0, 2)
    [byte[]]$destinationIp = @(10, 15, 0, 1)
    [byte[]]$udp = $frame[34..53]
    $expectedUdp = Udp-Checksum $sourceIp $destinationIp $udp
    $macMatches = $true
    for ($index = 0; $index -lt 6; ++$index) {
        if ($frame[$index] -ne $guestMacBytes[$index]) { $macMatches = $false }
    }
    if ($frame.Length -ne 60 -or -not $macMatches -or
        $frame[12] -ne 8 -or $frame[13] -ne 0 -or $frame[14] -ne 0x45 -or
        (Read-U16 $frame 16) -ne 40 -or $frame[22] -ne 63 -or
        $frame[23] -ne 17 -or (Checksum $frame 14 20) -ne 0 -or
        (Read-U16 $frame 34) -ne 24001 -or (Read-U16 $frame 36) -ne 15930 -or
        (Read-U16 $frame 38) -ne 20 -or (Read-U16 $frame 40) -ne $expectedUdp -or
        (Read-U16 $frame 40) -eq 0) {
        throw "Phase 93 valid request $identifier failed independent header validation."
    }
    [byte[]]$expectedPayload = Payload $identifier
    for ($index = 0; $index -lt $expectedPayload.Length; ++$index) {
        if ($frame[42 + $index] -ne $expectedPayload[$index]) {
            throw "Phase 93 valid request $identifier payload changed."
        }
    }
}

if ($GuestMac -notmatch '^(?:[0-9A-Fa-f]{12})$') {
    throw 'GuestMac must be twelve hexadecimal digits without separators.'
}
if ($Count -lt 1 -or $Count -gt 32) { throw 'Count must be in the range 1 through 32.' }
if ($FirstIdentifier -lt 1) {
    throw 'FirstIdentifier must be positive.'
}
if (-not $ValidateOnly -and ($GuestPort -lt 1 -or $GuestPort -gt 65535)) {
    throw 'GuestPort must be a valid QEMU dgram UDP port.'
}
[byte[]]$guestMacBytes = for ($index = 0; $index -lt 12; $index += 2) {
    [Convert]::ToByte($GuestMac.Substring($index, 2), 16)
}

$frames = [Collections.Generic.List[byte[]]]::new()
$negativeCount = if ($IncludeNegativeControls) { 3 } else { 0 }
if ($IncludeNegativeControls) {
    $frames.Add((Build-Frame $guestMacBytes ([Convert]::ToUInt32('93FFFF01', 16)) 15931 '10.15.0.1'))
    $frames.Add((Build-Frame $guestMacBytes ([Convert]::ToUInt32('93FFFF02', 16)) 15930 '10.15.0.1' -CorruptUdpChecksum))
    $frames.Add((Build-Frame $guestMacBytes ([Convert]::ToUInt32('93FFFF03', 16)) 15930 '10.15.0.99'))
}
for ($index = 0; $index -lt $Count; ++$index) {
    [uint32]$identifier = [uint32]($FirstIdentifier + $index)
    [byte[]]$frame = Build-Frame $guestMacBytes $identifier 15930 '10.15.0.1'
    Validate-ValidFrame $frame $guestMacBytes $identifier
    $frames.Add($frame)
}

if ($ValidateOnly) {
    Write-Output ('PHASE93_UDP_INJECTOR_VALIDATION=PASS valid={0} negative={1} port=15930 ipv4_checksum=valid udp_checksum=valid payload=93-A5-5A-C3-id-911700FF' -f `
        $Count, $negativeCount)
    return
}
if ($null -eq $PeerUdp) { throw 'PeerUdp must be the already-bound QEMU dgram peer endpoint.' }
if (-not [string]::IsNullOrWhiteSpace($CapturePath)) {
    $capturePath = [IO.Path]::GetFullPath($CapturePath)
    $captureDirectory = Split-Path -Parent $capturePath
    if (-not (Test-Path -LiteralPath $captureDirectory -PathType Container)) {
        throw "CapturePath parent directory does not exist: $captureDirectory"
    }
}

for ($index = 0; $index -lt $frames.Count; ++$index) {
    [byte[]]$frame = $frames[$index]
    $sent = $PeerUdp.Send($frame, $frame.Length, '127.0.0.1', $GuestPort)
    if ($sent -ne $frame.Length) { throw "Phase 93 packet $index send was short." }
    if (-not [string]::IsNullOrWhiteSpace($CapturePath)) {
        $capture = [IO.File]::Open($capturePath, [IO.FileMode]::Append,
            [IO.FileAccess]::Write, [IO.FileShare]::Read)
        try {
            [byte[]]$lengthPrefix = [BitConverter]::GetBytes([uint32]$frame.Length)
            $capture.Write($lengthPrefix, 0, $lengthPrefix.Length)
            $capture.Write($frame, 0, $frame.Length)
            $capture.Flush()
        } finally { $capture.Dispose() }
    }
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($frame))
    $kind = if ($index -lt $negativeCount) { 'negative' } else { 'echo' }
    $id = if ($kind -eq 'negative') {
        [Convert]::ToUInt32('93FFFF01', 16) + $index
    } else { $FirstIdentifier + $index - $negativeCount }
    Write-Output ('PHASE93_UDP_REQUEST_INJECTED=PASS kind={0} identifier=0x{1:X8} length={2} source_mac=021500000001 destination_mac={3} source_ipv4=10.15.0.2 destination_ipv4={4} ipv4_total_length=40 ipv4_checksum=0x{5:X4} source_port=24001 destination_port={6} udp_length=20 udp_checksum=0x{7:X4} sha256={8} guest_port={9}' -f `
        $kind, $id, $frame.Length, $GuestMac.ToUpperInvariant(),
        $(if ($kind -eq 'negative' -and $index -eq 2) { '10.15.0.99' } else { '10.15.0.1' }),
        (Read-U16 $frame 24), (Read-U16 $frame 36), (Read-U16 $frame 40), $hash,
        $GuestPort)
    Start-Sleep -Milliseconds 40
}
Write-Output ('PHASE93_UDP_INJECTION_SUMMARY=PASS valid={0} negative={1} local_endpoint=10.15.0.1:15930 external_network=false' -f `
    $Count, $negativeCount)
