[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$PcapPath,
    [Parameter(Mandatory = $true)] [string]$DestinationMac,
    [string]$ExpectedFrameSha256 = 'CAFF6094F057FBBFE83BF82A83072CE36D03C40EFAF23C1F24E50D490445D68E'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function RequirePcap([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}

function Read-U16([byte[]]$bytes, [int]$offset) {
    return [uint16]($bytes[$offset] -bor ($bytes[$offset + 1] -shl 8))
}

function Read-U32([byte[]]$bytes, [int]$offset) {
    return [uint32]($bytes[$offset] -bor ($bytes[$offset + 1] -shl 8) -bor
        ($bytes[$offset + 2] -shl 16) -bor ($bytes[$offset + 3] -shl 24))
}

function Parse-Mac([string]$text) {
    RequirePcap ($text -match '^[0-9A-Fa-f]{12}$') `
        'Destination MAC must be exactly twelve hexadecimal characters.'
    $mac = New-Object byte[] 6
    for ($index = 0; $index -lt 6; $index++) {
        $mac[$index] = [Convert]::ToByte($text.Substring($index * 2, 2), 16)
    }
    return $mac
}

function Bytes-Equal([byte[]]$left, [int]$leftOffset,
                     [byte[]]$right) {
    if ($leftOffset -lt 0 -or $leftOffset + $right.Length -gt $left.Length) {
        return $false
    }
    for ($index = 0; $index -lt $right.Length; $index++) {
        if ($left[$leftOffset + $index] -ne $right[$index]) { return $false }
    }
    return $true
}

$path = [IO.Path]::GetFullPath($PcapPath)
RequirePcap (Test-Path -LiteralPath $path) "PCAP is missing: $path"
$bytes = [IO.File]::ReadAllBytes($path)
RequirePcap ($bytes.Length -ge 24) 'PCAP global header is truncated.'
RequirePcap (($bytes[0] -eq 0xD4) -and ($bytes[1] -eq 0xC3) -and
             ($bytes[2] -eq 0xB2) -and ($bytes[3] -eq 0xA1)) `
    'Only little-endian classic PCAP is accepted.'
$versionMajor = Read-U16 $bytes 4
$versionMinor = Read-U16 $bytes 6
$linkType = Read-U32 $bytes 20
RequirePcap ($versionMajor -eq 2 -and $versionMinor -eq 4) `
    'Unsupported PCAP version.'
RequirePcap ($linkType -eq 1) 'PCAP is not Ethernet link type 1.'

$destination = Parse-Mac $DestinationMac
$source = [byte[]](0x02, 0x15, 0x00, 0x00, 0x00, 0x01)
$signature = [Text.Encoding]::ASCII.GetBytes('guideXOS ManagedKernel Phase15 RX')
$expectedLength = 60
$expectedSequence = [byte[]](0x15, 0x00, 0x00, 0x01)
$expectedHash = $ExpectedFrameSha256.ToUpperInvariant()
RequirePcap ($expectedHash -match '^[0-9A-F]{64}$') 'Expected frame SHA-256 is invalid.'

$packetCount = 0
$matchCount = 0
$matches = @()
$offset = 24
while ($offset -lt $bytes.Length) {
    RequirePcap ($offset + 16 -le $bytes.Length) 'PCAP packet header is truncated.'
    $capturedLength = [int](Read-U32 $bytes ($offset + 8))
    $originalLength = [int](Read-U32 $bytes ($offset + 12))
    RequirePcap ($capturedLength -ge 0 -and $originalLength -ge $capturedLength) `
        'PCAP packet length fields are invalid.'
    $packetOffset = $offset + 16
    RequirePcap ($packetOffset + $capturedLength -le $bytes.Length) `
        'PCAP packet payload is truncated.'
    $packetCount++
    $hasIdentity = $capturedLength -eq $expectedLength
    if ($hasIdentity) {
        $hasIdentity = [bool]$(Bytes-Equal $bytes $packetOffset $destination)
    }
    if ($hasIdentity) {
        $hasIdentity = [bool]$(Bytes-Equal $bytes ($packetOffset + 6) $source)
    }
    if ($hasIdentity) {
        $hasIdentity = $bytes[$packetOffset + 12] -eq 0x88 -and $bytes[$packetOffset + 13] -eq 0xB5
    }
    if ($hasIdentity) {
        $hasIdentity = [bool]$(Bytes-Equal $bytes ($packetOffset + 14) $signature)
    }
    if ($hasIdentity) {
        $hasIdentity = [bool]$(Bytes-Equal $bytes ($packetOffset + 14 + $signature.Length) $expectedSequence)
    }
    if ($hasIdentity) {
        $frame = New-Object byte[] $capturedLength
        [Array]::Copy($bytes, $packetOffset, $frame, 0, $capturedLength)
        $hash = [Security.Cryptography.SHA256]::Create()
        try { $frameHash = ([BitConverter]::ToString($hash.ComputeHash($frame))).Replace('-', '') }
        finally { $hash.Dispose() }
        if ($frameHash.ToUpperInvariant() -eq $expectedHash) {
            $matchCount++
            $matches += [pscustomobject]@{
                Packet = $packetCount
                Length = $capturedLength
                OriginalLength = $originalLength
                Sha256 = $frameHash.ToUpperInvariant()
                Hex = ([BitConverter]::ToString($frame)).Replace('-', '')
            }
        }
    }
    $offset = $packetOffset + $capturedLength
}

RequirePcap ($matchCount -eq 1) `
    "Expected exactly one complete Phase 15 frame; packets=$packetCount matches=$matchCount."
$match = $matches[0]
Write-Output ('MANAGED_E1000_PHASE15_PCAP=PASS packets={0} match_packet={1} length={2} original_length={3} frame_sha256={4}' -f `
    $packetCount, $match.Packet, $match.Length, $match.OriginalLength, $match.Sha256)
Write-Output ('MANAGED_E1000_PHASE15_PCAP_FRAME_HEX={0}' -f $match.Hex)
