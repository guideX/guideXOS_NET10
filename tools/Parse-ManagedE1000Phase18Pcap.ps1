[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$PcapPath,
    [Parameter(Mandatory = $true)] [string]$GuestMac
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Require18Pcap([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}

function Read-U16-18([byte[]]$bytes, [int]$offset) {
    return (([int]$bytes[$offset] -shl 8) -bor [int]$bytes[$offset + 1])
}

function Read-U16-Le18([byte[]]$bytes, [int]$offset) {
    return ([int]$bytes[$offset] -bor ([int]$bytes[$offset + 1] -shl 8))
}

function Read-U32-Le18([byte[]]$bytes, [int]$offset) {
    return [uint32]([int]$bytes[$offset] -bor
        ([int]$bytes[$offset + 1] -shl 8) -bor
        ([int]$bytes[$offset + 2] -shl 16) -bor
        ([int]$bytes[$offset + 3] -shl 24))
}

function Equal18([byte[]]$left, [int]$offset, [byte[]]$right) {
    if ($offset -lt 0 -or $offset + $right.Length -gt $left.Length) { return $false }
    for ($index = 0; $index -lt $right.Length; $index++) {
        if ($left[$offset + $index] -ne $right[$index]) { return $false }
    }
    return $true
}

function Checksum18([byte[]]$bytes, [int]$offset, [int]$length) {
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

function UdpChecksum18([byte[]]$sourceIp, [byte[]]$destinationIp,
                        [byte[]]$udp) {
    [uint32]$sum = 0
    foreach ($offset in @(0, 2)) {
        $sum += (([uint32]$sourceIp[$offset] -shl 8) -bor
            [uint32]$sourceIp[$offset + 1])
        $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
        $sum += (([uint32]$destinationIp[$offset] -shl 8) -bor
            [uint32]$destinationIp[$offset + 1])
        $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    }
    $sum += 17
    $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    $sum += $udp.Length
    $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    $index = 0
    while ($index + 1 -lt $udp.Length) {
        $sum += (([uint32]$udp[$index] -shl 8) -bor [uint32]$udp[$index + 1])
        $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
        $index += 2
    }
    if ($index -lt $udp.Length) {
        $sum += [uint32]$udp[$index] -shl 8
        $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    }
    $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    return [uint16]((-bnot [int]$sum) -band 0xFFFF)
}

function Parse-Mac18([string]$text) {
    Require18Pcap ($text -match '^[0-9A-Fa-f]{12}$') 'Guest MAC is invalid.'
    $mac = New-Object byte[] 6
    for ($index = 0; $index -lt 6; $index++) {
        $mac[$index] = [Convert]::ToByte($text.Substring($index * 2, 2), 16)
    }
    return $mac
}

function Is-Arp18([byte[]]$frame, [byte[]]$destination, [byte[]]$source,
                  [int]$operation, [byte[]]$senderIp, [byte[]]$targetMac,
                  [byte[]]$targetIp) {
    if ($frame.Length -ne 60 -or !(Equal18 $frame 0 $destination) -or
        !(Equal18 $frame 6 $source) -or $frame[12] -ne 8 -or
        $frame[13] -ne 6 -or $frame[14] -ne 0 -or $frame[15] -ne 1 -or
        $frame[16] -ne 8 -or $frame[17] -ne 0 -or $frame[18] -ne 6 -or
        $frame[19] -ne 4 -or $frame[20] -ne 0 -or $frame[21] -ne $operation) {
        return $false
    }
    return (Equal18 $frame 22 $source) -and (Equal18 $frame 28 $senderIp) -and
           (Equal18 $frame 32 $targetMac) -and (Equal18 $frame 38 $targetIp) -and
           (@($frame[42..59] | Where-Object { $_ -ne 0 }).Count -eq 0)
}

function Is-Icmp18([byte[]]$frame, [byte[]]$destination, [byte[]]$source,
                   [byte[]]$sourceIp, [byte[]]$destinationIp, [int]$type,
                   [int]$identifier, [int]$sequence, [byte[]]$payload) {
    $totalLength = 20 + 8 + $payload.Length
    if ($frame.Length -ne [Math]::Max(60, 14 + $totalLength) -or
        !(Equal18 $frame 0 $destination) -or !(Equal18 $frame 6 $source) -or
        $frame[12] -ne 8 -or $frame[13] -ne 0 -or $frame[14] -ne 0x45 -or
        (Read-U16-18 $frame 16) -ne $totalLength -or
        (Read-U16-18 $frame 20) -ne 0 -or $frame[22] -ne 64 -or
        $frame[23] -ne 1 -or !(Equal18 $frame 26 $sourceIp) -or
        !(Equal18 $frame 30 $destinationIp) -or
        (Checksum18 $frame 14 20) -ne 0) { return $false }
    $icmp = 34
    return $frame[$icmp] -eq $type -and $frame[$icmp + 1] -eq 0 -and
        (Read-U16-18 $frame ($icmp + 4)) -eq $identifier -and
        (Read-U16-18 $frame ($icmp + 6)) -eq $sequence -and
        (Checksum18 $frame $icmp (8 + $payload.Length)) -eq 0 -and
        (Equal18 $frame ($icmp + 8) $payload)
}

function Is-Udp18([byte[]]$frame, [byte[]]$destination, [byte[]]$source,
                  [byte[]]$sourceIp, [byte[]]$destinationIp, [int]$sourcePort,
                  [int]$destinationPort, [byte[]]$payload,
                  [bool]$allowZero = $false) {
    $totalLength = 20 + 8 + $payload.Length
    if ($frame.Length -ne [Math]::Max(60, 14 + $totalLength) -or
        !(Equal18 $frame 0 $destination) -or !(Equal18 $frame 6 $source) -or
        $frame[12] -ne 8 -or $frame[13] -ne 0 -or $frame[14] -ne 0x45 -or
        (Read-U16-18 $frame 16) -ne $totalLength -or
        (Read-U16-18 $frame 20) -ne 0 -or $frame[22] -ne 64 -or
        $frame[23] -ne 17 -or !(Equal18 $frame 26 $sourceIp) -or
        !(Equal18 $frame 30 $destinationIp) -or
        (Checksum18 $frame 14 20) -ne 0) { return $false }
    $udp = 34
    $udpLength = Read-U16-18 $frame ($udp + 4)
    if ($udpLength -ne 8 + $payload.Length -or
        (Read-U16-18 $frame $udp) -ne $sourcePort -or
        (Read-U16-18 $frame ($udp + 2)) -ne $destinationPort -or
        !(Equal18 $frame ($udp + 8) $payload)) { return $false }
    $checksum = Read-U16-18 $frame ($udp + 6)
    if ($checksum -eq 0) { return $allowZero }
    $udpBytes = [byte[]]$frame[$udp..($udp + $udpLength - 1)]
    return (UdpChecksum18 $sourceIp $destinationIp $udpBytes) -eq 0
}

function Frame-Hash18([byte[]]$frame) {
    $hash = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($hash.ComputeHash($frame))).Replace('-', '').ToUpperInvariant() }
    finally { $hash.Dispose() }
}

$bytes = [IO.File]::ReadAllBytes([IO.Path]::GetFullPath($PcapPath))
Require18Pcap ($bytes.Length -ge 24) 'PCAP global header is truncated.'
Require18Pcap ($bytes[0] -eq 0xD4 -and $bytes[1] -eq 0xC3 -and
               $bytes[2] -eq 0xB2 -and $bytes[3] -eq 0xA1) 'PCAP byte order is unsupported.'
Require18Pcap ((Read-U16-Le18 $bytes 4) -eq 2 -and (Read-U16-Le18 $bytes 6) -eq 4) `
    'PCAP version is unsupported.'
Require18Pcap ((Read-U32-Le18 $bytes 20) -eq 1) 'PCAP link type is not Ethernet.'

$guest = Parse-Mac18 $GuestMac
$hostMac = [byte[]](2, 21, 0, 0, 0, 2)
$broadcast = [byte[]](255, 255, 255, 255, 255, 255)
$zeroMac = [byte[]](0, 0, 0, 0, 0, 0)
$guestIp = [byte[]](10, 15, 0, 1)
$hostIp = [byte[]](10, 15, 0, 2)
$otherIp = [byte[]](10, 15, 0, 9)
$pingPayload = [Text.Encoding]::ASCII.GetBytes('guideXOS Phase17 ping payload')
$peerPayload = [Text.Encoding]::ASCII.GetBytes('peer-to-guideXOS Phase17 responder')
$udpManagedPayload = [Text.Encoding]::ASCII.GetBytes('PHASE18-MANAGED-HELLO')
$udpPeerAckPayload = [Text.Encoding]::ASCII.GetBytes('PHASE18-PEER-ACK')
$udpPeerRequestPayload = [Text.Encoding]::ASCII.GetBytes('PHASE18-PEER-HELLO')
$udpManagedAckPayload = [Text.Encoding]::ASCII.GetBytes('PHASE18-MANAGED-ACK')

$packets = @()
$offset = 24
while ($offset -lt $bytes.Length) {
    Require18Pcap ($offset + 16 -le $bytes.Length) 'PCAP packet header is truncated.'
    $captured = [int](Read-U32-Le18 $bytes ($offset + 8))
    $original = [int](Read-U32-Le18 $bytes ($offset + 12))
    Require18Pcap ($captured -ge 0 -and $original -ge $captured) 'PCAP packet lengths are invalid.'
    $frameOffset = $offset + 16
    Require18Pcap ($frameOffset + $captured -le $bytes.Length) 'PCAP packet payload is truncated.'
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
$arpMatches = @{}
foreach ($spec in $arpSpecs) {
    $arpMatches[$spec.Name] = @($packets | Where-Object {
        Is-Arp18 $_.Frame $spec.D $spec.S $spec.Op $spec.Sip $spec.Tm $spec.Tip
    })
    Require18Pcap ($arpMatches[$spec.Name].Count -eq 1) `
        "$($spec.Name) expected exactly once; found $($arpMatches[$spec.Name].Count)."
}

$icmpSpecs = @(
    @{ Name = 'GuestPing1'; D = $hostMac; S = $guest; Sip = $guestIp; Dip = $hostIp; T = 8; Id = 0x1701; Seq = 1; P = $pingPayload },
    @{ Name = 'HostReply1'; D = $guest; S = $hostMac; Sip = $hostIp; Dip = $guestIp; T = 0; Id = 0x1701; Seq = 1; P = $pingPayload },
    @{ Name = 'HostEchoRequest'; D = $guest; S = $hostMac; Sip = $hostIp; Dip = $guestIp; T = 8; Id = 0xBEEF; Seq = 7; P = $peerPayload },
    @{ Name = 'GuestEchoReply'; D = $hostMac; S = $guest; Sip = $guestIp; Dip = $hostIp; T = 0; Id = 0xBEEF; Seq = 7; P = $peerPayload },
    @{ Name = 'GuestPing2'; D = $hostMac; S = $guest; Sip = $guestIp; Dip = $hostIp; T = 8; Id = 0x1702; Seq = 2; P = $pingPayload },
    @{ Name = 'HostReply2'; D = $guest; S = $hostMac; Sip = $hostIp; Dip = $guestIp; T = 0; Id = 0x1702; Seq = 2; P = $pingPayload }
)
$icmpMatches = @{}
foreach ($spec in $icmpSpecs) {
    $icmpMatches[$spec.Name] = @($packets | Where-Object {
        Is-Icmp18 $_.Frame $spec.D $spec.S $spec.Sip $spec.Dip $spec.T $spec.Id $spec.Seq $spec.P
    })
    Require18Pcap ($icmpMatches[$spec.Name].Count -eq 1) `
        "$($spec.Name) expected exactly once; found $($icmpMatches[$spec.Name].Count)."
}

$udpSpecs = @(
    @{ Name = 'GuestUdpRequest'; D = $hostMac; S = $guest; Sip = $guestIp; Dip = $hostIp; Sp = 15180; Dp = 15181; P = $udpManagedPayload; Zero = $false; Id = 0x1900 },
    @{ Name = 'HostUdpResponse'; D = $guest; S = $hostMac; Sip = $hostIp; Dip = $guestIp; Sp = 15181; Dp = 15180; P = $udpPeerAckPayload; Zero = $false; Id = 0x1902; Occurrence = 1 },
    @{ Name = 'HostUdpRequest'; D = $guest; S = $hostMac; Sip = $hostIp; Dip = $guestIp; Sp = 15181; Dp = 15180; P = $udpPeerRequestPayload; Zero = $false; Id = 0x1903 },
    @{ Name = 'GuestUdpEndpointResponse'; D = $hostMac; S = $guest; Sip = $guestIp; Dip = $hostIp; Sp = 15180; Dp = 15181; P = $udpManagedAckPayload; Zero = $false; Id = 0x1901 },
    @{ Name = 'HostUdpZeroChecksumRequest'; D = $guest; S = $hostMac; Sip = $hostIp; Dip = $guestIp; Sp = 15181; Dp = 15180; P = $udpPeerRequestPayload; Zero = $true; Id = 0x1905 },
    @{ Name = 'GuestUdpZeroChecksumResponse'; D = $hostMac; S = $guest; Sip = $guestIp; Dip = $hostIp; Sp = 15180; Dp = 15181; P = $udpManagedAckPayload; Zero = $false; Id = 0x1902 },
    @{ Name = 'HostUdpPostMalformedRequest'; D = $guest; S = $hostMac; Sip = $hostIp; Dip = $guestIp; Sp = 15181; Dp = 15180; P = $udpPeerRequestPayload; Zero = $false; Id = 0x9301 },
    @{ Name = 'GuestUdpPostMalformedResponse'; D = $hostMac; S = $guest; Sip = $guestIp; Dip = $hostIp; Sp = 15180; Dp = 15181; P = $udpManagedAckPayload; Zero = $false; Id = 0x1903 },
    @{ Name = 'GuestUdpPostGcRequest'; D = $hostMac; S = $guest; Sip = $guestIp; Dip = $hostIp; Sp = 15180; Dp = 15181; P = $udpManagedPayload; Zero = $false; Id = 0x1904 },
    @{ Name = 'HostUdpPostGcResponse'; D = $guest; S = $hostMac; Sip = $hostIp; Dip = $guestIp; Sp = 15181; Dp = 15180; P = $udpPeerAckPayload; Zero = $false; Id = 0x1902; Occurrence = 2 },
    @{ Name = 'HostUdpPostGcRequest'; D = $guest; S = $hostMac; Sip = $hostIp; Dip = $guestIp; Sp = 15181; Dp = 15180; P = $udpPeerRequestPayload; Zero = $false; Id = 0x9302 },
    @{ Name = 'GuestUdpPostGcResponse'; D = $hostMac; S = $guest; Sip = $guestIp; Dip = $hostIp; Sp = 15180; Dp = 15181; P = $udpManagedAckPayload; Zero = $false; Id = 0x1905 }
)
$udpMatches = @{}
foreach ($spec in $udpSpecs) {
    $candidates = @($packets | Where-Object {
        (Read-U16-18 $_.Frame 18) -eq $spec.Id -and
        (Is-Udp18 $_.Frame $spec.D $spec.S $spec.Sip $spec.Dip $spec.Sp $spec.Dp $spec.P $spec.Zero)
    })
    $udpMatches[$spec.Name] = if ($spec.ContainsKey('Occurrence')) {
        if ($candidates.Count -ge $spec.Occurrence) {
            @($candidates[$spec.Occurrence - 1])
        } else { @() }
    } else { $candidates }
    Require18Pcap ($udpMatches[$spec.Name].Count -eq 1) `
        "$($spec.Name) expected exactly once; found $($udpMatches[$spec.Name].Count)."
}

$malformed = @{}
$malformed.ZeroSourcePort = @($packets | Where-Object {
    $_.Frame.Length -ge 42 -and (Read-U16-18 $_.Frame 18) -eq 0x9201 -and
    (Read-U16-18 $_.Frame 34) -eq 0
})
$malformed.ZeroDestinationPort = @($packets | Where-Object {
    $_.Frame.Length -ge 42 -and (Read-U16-18 $_.Frame 18) -eq 0x9202 -and
    (Read-U16-18 $_.Frame 36) -eq 0
})
$malformed.InvalidPayload = @($packets | Where-Object {
    $_.Frame.Length -ge 42 -and (Read-U16-18 $_.Frame 18) -eq 0x9203 -and
    (Read-U16-18 $_.Frame 38) -eq 11 -and
    (UdpChecksum18 $hostIp $guestIp ([byte[]]$_.Frame[34..44])) -eq 0 -and
    (Equal18 $_.Frame 42 ([byte[]](1, 2, 3)) )
})
$malformed.UnknownPort = @($packets | Where-Object {
    $_.Frame.Length -ge 42 -and (Read-U16-18 $_.Frame 18) -eq 0x9204 -and
    (Read-U16-18 $_.Frame 36) -eq 15182
})
$malformed.OversizedPayload = @($packets | Where-Object {
    $_.Frame.Length -ge 42 -and (Read-U16-18 $_.Frame 18) -eq 0x9205 -and
    (Read-U16-18 $_.Frame 38) -eq 521
})
foreach ($name in $malformed.Keys) {
    Require18Pcap ($malformed[$name].Count -eq 1) `
        "Malformed UDP control $name expected exactly once; found $($malformed[$name].Count)."
}

$ordered = @(
    $icmpMatches.GuestPing1[0].Number, $icmpMatches.HostReply1[0].Number,
    $icmpMatches.HostEchoRequest[0].Number, $icmpMatches.GuestEchoReply[0].Number,
    $icmpMatches.GuestPing2[0].Number, $icmpMatches.HostReply2[0].Number,
    $udpMatches.GuestUdpRequest[0].Number, $udpMatches.HostUdpResponse[0].Number,
    $udpMatches.HostUdpRequest[0].Number, $udpMatches.GuestUdpEndpointResponse[0].Number,
    $udpMatches.HostUdpZeroChecksumRequest[0].Number, $udpMatches.GuestUdpZeroChecksumResponse[0].Number,
    $malformed.ZeroSourcePort[0].Number, $malformed.ZeroDestinationPort[0].Number,
    $malformed.InvalidPayload[0].Number, $malformed.UnknownPort[0].Number,
    $malformed.OversizedPayload[0].Number,
    $udpMatches.HostUdpPostMalformedRequest[0].Number,
    $udpMatches.GuestUdpPostMalformedResponse[0].Number,
    $udpMatches.GuestUdpPostGcRequest[0].Number,
    $udpMatches.HostUdpPostGcResponse[0].Number,
    $udpMatches.HostUdpPostGcRequest[0].Number,
    $udpMatches.GuestUdpPostGcResponse[0].Number)
for ($index = 1; $index -lt $ordered.Count; $index++) {
    Require18Pcap ($ordered[$index] -gt $ordered[$index - 1]) `
        'Phase 18 valid and malformed UDP frames are not in expected order.'
}

Write-Output ('MANAGED_E1000_PHASE18_PCAP=PASS packets={0} arp={1} ipv4_icmp={2} ipv4_udp={3} malformed_udp={4}' -f
    $packets.Count, 4, $icmpSpecs.Count, $udpSpecs.Count, $malformed.Count)
foreach ($spec in $arpSpecs + $icmpSpecs + $udpSpecs) {
    $set = if ($arpSpecs -contains $spec) { $arpMatches[$spec.Name] } `
        elseif ($icmpSpecs -contains $spec) { $icmpMatches[$spec.Name] } `
        else { $udpMatches[$spec.Name] }
    foreach ($match in $set) {
        Write-Output ('MANAGED_E1000_PHASE18_PCAP_{0}=packet={1} length={2} original_length={3} frame_sha256={4}' -f
            $spec.Name.ToUpperInvariant(), $match.Number, $match.Captured,
            $match.Original, (Frame-Hash18 $match.Frame))
    }
}
foreach ($name in $malformed.Keys) {
    $match = $malformed[$name][0]
    Write-Output ('MANAGED_E1000_PHASE18_PCAP_MALFORMED_{0}=packet={1} length={2} frame_sha256={3}' -f
        $name.ToUpperInvariant(), $match.Number, $match.Captured,
        (Frame-Hash18 $match.Frame))
}
