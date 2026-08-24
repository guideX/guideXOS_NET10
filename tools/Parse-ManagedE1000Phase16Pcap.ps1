[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$PcapPath,
    [Parameter(Mandatory = $true)] [string]$GuestMac
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Require16Pcap([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}

function Read-U16-16([byte[]]$bytes, [int]$offset) {
    return [uint16]($bytes[$offset] -bor ($bytes[$offset + 1] -shl 8))
}

function Read-U32-16([byte[]]$bytes, [int]$offset) {
    return [uint32]($bytes[$offset] -bor ($bytes[$offset + 1] -shl 8) -bor
        ($bytes[$offset + 2] -shl 16) -bor ($bytes[$offset + 3] -shl 24))
}

function Parse-Mac-16([string]$text) {
    Require16Pcap ($text -match '^[0-9A-Fa-f]{12}$') `
        'Guest MAC must be exactly twelve hexadecimal characters.'
    $mac = New-Object byte[] 6
    for ($index = 0; $index -lt 6; $index++) {
        $mac[$index] = [Convert]::ToByte($text.Substring($index * 2, 2), 16)
    }
    return $mac
}

function Bytes-Equal-16([byte[]]$left, [int]$leftOffset,
                        [byte[]]$right) {
    if ($leftOffset -lt 0 -or $leftOffset + $right.Length -gt $left.Length) {
        return $false
    }
    for ($index = 0; $index -lt $right.Length; $index++) {
        if ($left[$leftOffset + $index] -ne $right[$index]) { return $false }
    }
    return $true
}

function Is-Expected-Arp-16([byte[]]$frame, [byte[]]$destination,
                            [byte[]]$source, [byte]$operation,
                            [byte[]]$senderMac, [byte[]]$senderIp,
                            [byte[]]$targetMac, [byte[]]$targetIp) {
    if ($frame.Length -ne 60 -or
        !(Bytes-Equal-16 $frame 0 $destination) -or
        !(Bytes-Equal-16 $frame 6 $source) -or
        $frame[12] -ne 0x08 -or $frame[13] -ne 0x06 -or
        $frame[14] -ne 0 -or $frame[15] -ne 1 -or
        $frame[16] -ne 0x08 -or $frame[17] -ne 0 -or
        $frame[18] -ne 6 -or $frame[19] -ne 4 -or
        $frame[20] -ne 0 -or $frame[21] -ne $operation -or
        !(Bytes-Equal-16 $frame 22 $senderMac) -or
        !(Bytes-Equal-16 $frame 28 $senderIp) -or
        !(Bytes-Equal-16 $frame 32 $targetMac) -or
        !(Bytes-Equal-16 $frame 38 $targetIp)) {
        return $false
    }
    for ($index = 42; $index -lt 60; $index++) {
        if ($frame[$index] -ne 0) { return $false }
    }
    return $true
}

function Frame-Hash-16([byte[]]$frame) {
    $hash = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($hash.ComputeHash($frame))).Replace('-', '').ToUpperInvariant() }
    finally { $hash.Dispose() }
}

$path = [IO.Path]::GetFullPath($PcapPath)
Require16Pcap (Test-Path -LiteralPath $path) "PCAP is missing: $path"
$bytes = [IO.File]::ReadAllBytes($path)
Require16Pcap ($bytes.Length -ge 24) 'PCAP global header is truncated.'
Require16Pcap ($bytes[0] -eq 0xD4 -and $bytes[1] -eq 0xC3 -and
               $bytes[2] -eq 0xB2 -and $bytes[3] -eq 0xA1) `
    'Only little-endian classic PCAP is accepted.'
Require16Pcap ((Read-U16-16 $bytes 4) -eq 2 -and
               (Read-U16-16 $bytes 6) -eq 4) 'Unsupported PCAP version.'
Require16Pcap ((Read-U32-16 $bytes 20) -eq 1) 'PCAP is not Ethernet link type 1.'

$guest = Parse-Mac-16 $GuestMac
$hostMac = [byte[]](0x02, 0x15, 0, 0, 0, 2)
$broadcast = [byte[]](0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF)
$zeroMac = [byte[]](0, 0, 0, 0, 0, 0)
$guestIp = [byte[]](10, 15, 0, 1)
$hostIp = [byte[]](10, 15, 0, 2)
$expected = [ordered]@{
    GuestRequest = @{ Destination = $broadcast; Source = $guest; Operation = [byte]1; SenderMac = $guest; SenderIp = $guestIp; TargetMac = $zeroMac; TargetIp = $hostIp }
    HostReply = @{ Destination = $guest; Source = $hostMac; Operation = [byte]2; SenderMac = $hostMac; SenderIp = $hostIp; TargetMac = $guest; TargetIp = $guestIp }
    HostRequest = @{ Destination = $broadcast; Source = $hostMac; Operation = [byte]1; SenderMac = $hostMac; SenderIp = $hostIp; TargetMac = $zeroMac; TargetIp = $guestIp }
    GuestReply = @{ Destination = $hostMac; Source = $guest; Operation = [byte]2; SenderMac = $guest; SenderIp = $guestIp; TargetMac = $hostMac; TargetIp = $hostIp }
}
$matches = @{}
foreach ($name in $expected.Keys) { $matches[$name] = @() }
$packetCount = 0
$offset = 24
while ($offset -lt $bytes.Length) {
    Require16Pcap ($offset + 16 -le $bytes.Length) 'PCAP packet header is truncated.'
    $capturedLength = [int](Read-U32-16 $bytes ($offset + 8))
    $originalLength = [int](Read-U32-16 $bytes ($offset + 12))
    Require16Pcap ($capturedLength -ge 0 -and $originalLength -ge $capturedLength) `
        'PCAP packet length fields are invalid.'
    $packetOffset = $offset + 16
    Require16Pcap ($packetOffset + $capturedLength -le $bytes.Length) `
        'PCAP packet payload is truncated.'
    $packetCount++
    if ($capturedLength -eq 60) {
        $frame = New-Object byte[] 60
        [Array]::Copy($bytes, $packetOffset, $frame, 0, 60)
        foreach ($name in $expected.Keys) {
            $spec = $expected[$name]
            if (Is-Expected-Arp-16 $frame $spec.Destination $spec.Source $spec.Operation `
                    $spec.SenderMac $spec.SenderIp $spec.TargetMac $spec.TargetIp) {
                $matches[$name] += [pscustomobject]@{
                    Packet = $packetCount
                    Length = $capturedLength
                    OriginalLength = $originalLength
                    Sha256 = Frame-Hash-16 $frame
                    Hex = ([BitConverter]::ToString($frame)).Replace('-', '')
                }
            }
        }
    }
    $offset = $packetOffset + $capturedLength
}

foreach ($name in $expected.Keys) {
    Require16Pcap ($matches[$name].Count -eq 1) `
        "Expected exactly one $name in PCAP; packets=$packetCount matches=$($matches[$name].Count)."
}
Write-Output ('MANAGED_E1000_PHASE16_PCAP=PASS packets={0} guest_request_packet={1} host_reply_packet={2} host_request_packet={3} guest_reply_packet={4}' -f `
    $packetCount, $matches.GuestRequest[0].Packet, $matches.HostReply[0].Packet,
    $matches.HostRequest[0].Packet, $matches.GuestReply[0].Packet)
foreach ($name in $expected.Keys) {
    $match = $matches[$name][0]
    Write-Output ('MANAGED_E1000_PHASE16_PCAP_{0}=length={1} original_length={2} frame_sha256={3}' -f `
        $name.ToUpperInvariant(), $match.Length, $match.OriginalLength, $match.Sha256)
    Write-Output ('MANAGED_E1000_PHASE16_PCAP_{0}_FRAME_HEX={1}' -f `
        $name.ToUpperInvariant(), $match.Hex)
}
