[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$PcapPath,
    [Parameter(Mandatory = $true)] [string]$GuestMac
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Require17Pcap([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}

function Read-U16-17([byte[]]$bytes, [int]$offset) {
    return (([int]$bytes[$offset] -shl 8) -bor [int]$bytes[$offset + 1])
}

function Read-U16-Le17([byte[]]$bytes, [int]$offset) {
    return ([int]$bytes[$offset] -bor ([int]$bytes[$offset + 1] -shl 8))
}

function Read-U32-Le17([byte[]]$bytes, [int]$offset) {
    return [uint32]([int]$bytes[$offset] -bor
        ([int]$bytes[$offset + 1] -shl 8) -bor
        ([int]$bytes[$offset + 2] -shl 16) -bor
        ([int]$bytes[$offset + 3] -shl 24))
}

function Checksum17([byte[]]$bytes, [int]$offset, [int]$length) {
    [uint32]$sum = 0
    $index = 0
    while ($index + 1 -lt $length) {
        $sum += [uint32](([int]$bytes[$offset + $index] -shl 8) -bor
            [int]$bytes[$offset + $index + 1])
        $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
        $index += 2
    }
    if ($index -lt $length) {
        $sum += [uint32]([int]$bytes[$offset + $index] -shl 8)
        $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    }
    $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    return [uint16]((-bnot [int]$sum) -band 0xFFFF)
}

function Parse-Mac17([string]$text) {
    Require17Pcap ($text -match '^[0-9A-Fa-f]{12}$') 'Guest MAC is invalid.'
    $mac = New-Object byte[] 6
    for ($index = 0; $index -lt 6; $index++) {
        $mac[$index] = [Convert]::ToByte($text.Substring($index * 2, 2), 16)
    }
    return $mac
}

function Equal17([byte[]]$left, [int]$offset, [byte[]]$right) {
    if ($offset -lt 0 -or $offset + $right.Length -gt $left.Length) { return $false }
    for ($index = 0; $index -lt $right.Length; $index++) {
        if ($left[$offset + $index] -ne $right[$index]) { return $false }
    }
    return $true
}

function Is-Arp17([byte[]]$frame, [byte[]]$destination,
                  [byte[]]$source, [int]$operation,
                  [byte[]]$senderIp, [byte[]]$targetMac,
                  [byte[]]$targetIp) {
    if ($frame.Length -ne 60) { return $false }
    if (!(Equal17 $frame 0 $destination) -or !(Equal17 $frame 6 $source)) { return $false }
    if ($frame[12] -ne 8 -or $frame[13] -ne 6 -or
        $frame[14] -ne 0 -or $frame[15] -ne 1 -or
        $frame[16] -ne 8 -or $frame[17] -ne 0 -or
        $frame[18] -ne 6 -or $frame[19] -ne 4 -or
        $frame[20] -ne 0 -or $frame[21] -ne $operation) { return $false }
    if (!(Equal17 $frame 22 $source) -or !(Equal17 $frame 28 $senderIp) -or
        !(Equal17 $frame 32 $targetMac) -or !(Equal17 $frame 38 $targetIp)) { return $false }
    for ($index = 42; $index -lt 60; $index++) {
        if ($frame[$index] -ne 0) { return $false }
    }
    return $true
}

function Is-Icmp17([byte[]]$frame, [byte[]]$destination,
                   [byte[]]$source, [byte[]]$sourceIp,
                   [byte[]]$destinationIp, [int]$type,
    [int]$identifier, [int]$sequence,
                   [byte[]]$payload) {
    $totalLength = 20 + 8 + $payload.Length
    if ($frame.Length -ne 14 + $totalLength) { return $false }
    if (!(Equal17 $frame 0 $destination) -or !(Equal17 $frame 6 $source)) { return $false }
    if ($frame[12] -ne 8 -or $frame[13] -ne 0 -or $frame[14] -ne 0x45) { return $false }
    if ((Read-U16-17 $frame 16) -ne $totalLength -or
        (Read-U16-17 $frame 20) -ne 0 -or $frame[22] -ne 64 -or
        $frame[23] -ne 1) { return $false }
    if (!(Equal17 $frame 26 $sourceIp) -or !(Equal17 $frame 30 $destinationIp)) { return $false }
    if ((Checksum17 $frame 14 20) -ne 0) { return $false }
    $icmp = 34
    if ($frame[$icmp] -ne $type -or $frame[$icmp + 1] -ne 0) { return $false }
    if ((Read-U16-17 $frame ($icmp + 4)) -ne $identifier -or
        (Read-U16-17 $frame ($icmp + 6)) -ne $sequence) { return $false }
    if ((Checksum17 $frame $icmp (8 + $payload.Length)) -ne 0) { return $false }
    if (!(Equal17 $frame ($icmp + 8) $payload)) { return $false }
    return $true
}

function Frame-Hash17([byte[]]$frame) {
    $hash = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($hash.ComputeHash($frame))).Replace('-', '').ToUpperInvariant() }
    finally { $hash.Dispose() }
}

$bytes = [IO.File]::ReadAllBytes([IO.Path]::GetFullPath($PcapPath))
Require17Pcap ($bytes.Length -ge 24) 'PCAP global header is truncated.'
Require17Pcap ($bytes[0] -eq 0xD4 -and $bytes[1] -eq 0xC3 -and
               $bytes[2] -eq 0xB2 -and $bytes[3] -eq 0xA1) 'PCAP byte order is unsupported.'
Require17Pcap ((Read-U16-Le17 $bytes 4) -eq 2 -and (Read-U16-Le17 $bytes 6) -eq 4) `
    'PCAP version is unsupported.'
Require17Pcap ((Read-U32-Le17 $bytes 20) -eq 1) 'PCAP link type is not Ethernet.'

$guest = Parse-Mac17 $GuestMac
$hostMac = [byte[]](2, 21, 0, 0, 0, 2)
$broadcast = [byte[]](255, 255, 255, 255, 255, 255)
$zeroMac = [byte[]](0, 0, 0, 0, 0, 0)
$guestIp = [byte[]](10, 15, 0, 1)
$hostIp = [byte[]](10, 15, 0, 2)
$otherIp = [byte[]](10, 15, 0, 9)
$pingPayload = [Text.Encoding]::ASCII.GetBytes('guideXOS Phase17 ping payload')
$peerPayload = [Text.Encoding]::ASCII.GetBytes('peer-to-guideXOS Phase17 responder')

$packets = @()
$offset = 24
while ($offset -lt $bytes.Length) {
    Require17Pcap ($offset + 16 -le $bytes.Length) 'PCAP packet header is truncated.'
    $captured = [int](Read-U32-Le17 $bytes ($offset + 8))
    $original = [int](Read-U32-Le17 $bytes ($offset + 12))
    Require17Pcap ($captured -ge 0 -and $original -ge $captured) 'PCAP packet lengths are invalid.'
    $frameOffset = $offset + 16
    Require17Pcap ($frameOffset + $captured -le $bytes.Length) 'PCAP packet payload is truncated.'
    $frame = New-Object byte[] $captured
    [Array]::Copy($bytes, $frameOffset, $frame, 0, $captured)
    $packets += [pscustomobject]@{ Number = $packets.Count + 1; Frame = $frame; Captured = $captured; Original = $original }
    $offset = $frameOffset + $captured
}

$arpSpecs = @(
    @{ Name = 'GuestArpRequest'; D = $broadcast; S = $guest; Op = 1; Sip = $guestIp; Tm = $zeroMac; Tip = $hostIp },
    @{ Name = 'HostArpReply'; D = $guest; S = $hostMac; Op = 2; Sip = $hostIp; Tm = $guest; Tip = $guestIp },
    @{ Name = 'HostArpRequest'; D = $broadcast; S = $hostMac; Op = 1; Sip = $hostIp; Tm = $zeroMac; Tip = $guestIp },
    @{ Name = 'GuestArpReply'; D = $hostMac; S = $guest; Op = 2; Sip = $guestIp; Tm = $hostMac; Tip = $hostIp }
)
$matches = @{}
foreach ($spec in $arpSpecs) {
    $matches[$spec.Name] = @($packets | Where-Object {
        Is-Arp17 $_.Frame $spec.D $spec.S $spec.Op $spec.Sip $spec.Tm $spec.Tip
    })
    Require17Pcap ($matches[$spec.Name].Count -eq 1) `
        "$($spec.Name) expected exactly once; found $($matches[$spec.Name].Count)."
}

$icmpSpecs = @(
    @{ Name = 'GuestPing1'; D = $hostMac; S = $guest; Sip = $guestIp; Dip = $hostIp; T = 8; Id = 0x1701; Seq = 1; P = $pingPayload },
    @{ Name = 'HostReply1'; D = $guest; S = $hostMac; Sip = $hostIp; Dip = $guestIp; T = 0; Id = 0x1701; Seq = 1; P = $pingPayload },
    @{ Name = 'HostEchoRequest'; D = $guest; S = $hostMac; Sip = $hostIp; Dip = $guestIp; T = 8; Id = 0xBEEF; Seq = 7; P = $peerPayload },
    @{ Name = 'GuestEchoReply'; D = $hostMac; S = $guest; Sip = $guestIp; Dip = $hostIp; T = 0; Id = 0xBEEF; Seq = 7; P = $peerPayload },
    @{ Name = 'GuestPing2'; D = $hostMac; S = $guest; Sip = $guestIp; Dip = $hostIp; T = 8; Id = 0x1702; Seq = 2; P = $pingPayload },
    @{ Name = 'HostReply2'; D = $guest; S = $hostMac; Sip = $hostIp; Dip = $guestIp; T = 0; Id = 0x1702; Seq = 2; P = $pingPayload }
)
foreach ($spec in $icmpSpecs) {
    $matches[$spec.Name] = @($packets | Where-Object {
        Is-Icmp17 $_.Frame $spec.D $spec.S $spec.Sip $spec.Dip $spec.T $spec.Id $spec.Seq $spec.P
    })
    Require17Pcap ($matches[$spec.Name].Count -eq 1) `
        "$($spec.Name) expected exactly once; found $($matches[$spec.Name].Count)."
}

$malformed = @{}
$malformed.BadHeaderChecksum = @($packets | Where-Object {
    $_.Frame.Length -ge 42 -and (Read-U16-17 $_.Frame 18) -eq 0x9001 -and
    (Checksum17 $_.Frame 14 20) -ne 0
})
$malformed.ImpossibleLength = @($packets | Where-Object {
    $_.Frame.Length -ge 42 -and (Read-U16-17 $_.Frame 18) -eq 0x9002 -and
    (Read-U16-17 $_.Frame 16) -gt $_.Frame.Length - 14
})
$malformed.Fragmented = @($packets | Where-Object {
    $_.Frame.Length -ge 42 -and (Read-U16-17 $_.Frame 18) -eq 0x9003 -and
    ((Read-U16-17 $_.Frame 20) -band 0x2000) -ne 0
})
$malformed.InvalidIcmpCode = @($packets | Where-Object {
    $_.Frame.Length -ge 42 -and (Read-U16-17 $_.Frame 18) -eq 0x9004 -and
    $_.Frame[35] -eq 1 -and (Checksum17 $_.Frame 34 ($_.Frame.Length - 34)) -eq 0
})
$malformed.WrongDestination = @($packets | Where-Object {
    $_.Frame.Length -ge 34 -and (Read-U16-17 $_.Frame 18) -eq 0x9005 -and
    (Equal17 $_.Frame 30 $otherIp)
})
foreach ($name in $malformed.Keys) {
    Require17Pcap ($malformed[$name].Count -eq 1) `
        "Malformed control $name expected exactly once; found $($malformed[$name].Count)."
}

$ordered = @(
    $matches.GuestArpRequest[0].Number, $matches.HostArpReply[0].Number,
    $matches.HostArpRequest[0].Number, $matches.GuestArpReply[0].Number,
    $matches.GuestPing1[0].Number, $matches.HostReply1[0].Number,
    $matches.HostEchoRequest[0].Number, $matches.GuestEchoReply[0].Number,
    $matches.GuestPing2[0].Number, $matches.HostReply2[0].Number)
for ($index = 1; $index -lt $ordered.Count; $index++) {
    Require17Pcap ($ordered[$index] -gt $ordered[$index - 1]) `
        'Valid Phase 17 frames are not in the expected wire order.'
}

Write-Output ('MANAGED_E1000_PHASE17_PCAP=PASS packets={0} arp={1} ipv4_icmp={2} malformed={3}' -f
    $packets.Count, $arpSpecs.Count, $icmpSpecs.Count, $malformed.Count)
foreach ($spec in $arpSpecs + $icmpSpecs) {
    $match = $matches[$spec.Name][0]
    Write-Output ('MANAGED_E1000_PHASE17_PCAP_{0}=packet={1} length={2} original_length={3} frame_sha256={4}' -f
        $spec.Name.ToUpperInvariant(), $match.Number, $match.Captured,
        $match.Original, (Frame-Hash17 $match.Frame))
}
foreach ($name in $malformed.Keys) {
    $match = $malformed[$name][0]
    Write-Output ('MANAGED_E1000_PHASE17_PCAP_MALFORMED_{0}=packet={1} length={2} frame_sha256={3}' -f
        $name.ToUpperInvariant(), $match.Number, $match.Captured,
        (Frame-Hash17 $match.Frame))
}
