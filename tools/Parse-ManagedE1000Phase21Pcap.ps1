[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$PcapPath,
    [Parameter(Mandatory = $true)] [string]$GuestMac
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Require21([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}
function U16-21([byte[]]$b, [int]$o) { return (([int]$b[$o] -shl 8) -bor [int]$b[$o + 1]) }
function U16LE-21([byte[]]$b, [int]$o) { return ([int]$b[$o] -bor ([int]$b[$o + 1] -shl 8)) }
function U32LE-21([byte[]]$b, [int]$o) { return [uint32](([int]$b[$o]) -bor ([int]$b[$o + 1] -shl 8) -bor ([int]$b[$o + 2] -shl 16) -bor ([int]$b[$o + 3] -shl 24)) }
function U32-21([byte[]]$b, [int]$o) { return [uint32]($b[$o] * 16777216 + $b[$o + 1] * 65536 + $b[$o + 2] * 256 + $b[$o + 3]) }
function Eq21([byte[]]$a, [int]$o, [byte[]]$b) {
    if ($o -lt 0 -or $o + $b.Length -gt $a.Length) { return $false }
    for ($i = 0; $i -lt $b.Length; ++$i) { if ($a[$o + $i] -ne $b[$i]) { return $false } }
    return $true
}
function Sum21([byte[]]$b, [int]$o, [int]$length) {
    [uint32]$sum = 0
    for ($i = 0; $i + 1 -lt $length; $i += 2) {
        $sum += (([int]$b[$o + $i] -shl 8) -bor [int]$b[$o + $i + 1]); $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    }
    if (($length -band 1) -ne 0) { $sum += [int]$b[$o + $length - 1] -shl 8 }
    $sum = ($sum -band 0xFFFF) + ($sum -shr 16); $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    return [uint16]((-bnot [int]$sum) -band 0xFFFF)
}
function UdpChecksum21([byte[]]$source, [byte[]]$destination, [byte[]]$udp) {
    [uint32]$sum = 0
    foreach ($o in @(0, 2)) {
        $sum += (([int]$source[$o] -shl 8) -bor [int]$source[$o + 1]); $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
        $sum += (([int]$destination[$o] -shl 8) -bor [int]$destination[$o + 1]); $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    }
    $sum += 17; $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    $sum += $udp.Length; $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    for ($i = 0; $i + 1 -lt $udp.Length; $i += 2) {
        $sum += (([int]$udp[$i] -shl 8) -bor [int]$udp[$i + 1]); $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    }
    if (($udp.Length -band 1) -ne 0) { $sum += [int]$udp[$udp.Length - 1] -shl 8 }
    $sum = ($sum -band 0xFFFF) + ($sum -shr 16); $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    return [uint16]((-bnot [int]$sum) -band 0xFFFF)
}
function ParseUdp21([byte[]]$frame) {
    if ($frame.Length -lt 42 -or $frame[12] -ne 8 -or $frame[13] -ne 0 -or $frame[14] -ne 0x45) { return $null }
    $total = U16-21 $frame 16
    if ($total -lt 28 -or 14 + $total -gt $frame.Length -or (Sum21 $frame 14 20) -ne 0 -or $frame[23] -ne 17) { return $null }
    $src = [byte[]]$frame[26..29]; $dst = [byte[]]$frame[30..33]; $udpLength = U16-21 $frame 38
    if ($udpLength -lt 8 -or $udpLength -ne $total - 20 -or 34 + $udpLength -gt $frame.Length) { return $null }
    $udp = [byte[]]$frame[34..(33 + $udpLength)]; $checksum = U16-21 $udp 6
    if ($checksum -ne 0 -and (UdpChecksum21 $src $dst $udp) -ne 0) { return $null }
    [pscustomobject]@{ Frame = $frame; SourceIp = $src; DestinationIp = $dst; SourcePort = U16-21 $udp 0; DestinationPort = U16-21 $udp 2; Payload = if ($udpLength -gt 8) { [byte[]]$udp[8..($udpLength - 1)] } else { [byte[]]@() } }
}
function GetOption21([byte[]]$payload, [int]$wanted) {
    if ($payload.Length -lt 241 -or (U32-21 $payload 236) -ne 0x63825363) { return $null }
    $o = 240
    while ($o -lt $payload.Length) {
        $code = $payload[$o++]; if ($code -eq 0) { continue }; if ($code -eq 255) { return $null }
        if ($o -ge $payload.Length) { return $null }; $length = $payload[$o++]
        if ($o + $length -gt $payload.Length) { return $null }
        if ($code -eq $wanted) {
            if ($length -eq 0) { return [byte[]]@() }
            return [byte[]]$payload[$o..($o + $length - 1)]
        }
        $o += $length
    }
    return $null
}
function ParseMac21([string]$text) {
    Require21 ($text -match '^[0-9A-Fa-f]{12}$') 'Guest MAC is invalid.'
    $mac = New-Object byte[] 6
    for ($i = 0; $i -lt 6; ++$i) { $mac[$i] = [Convert]::ToByte($text.Substring($i * 2, 2), 16) }
    return $mac
}

$bytes = [IO.File]::ReadAllBytes([IO.Path]::GetFullPath($PcapPath))
Require21 ($bytes.Length -ge 24 -and $bytes[0] -eq 0xD4 -and $bytes[1] -eq 0xC3 -and $bytes[2] -eq 0xB2 -and $bytes[3] -eq 0xA1) 'PCAP header is invalid.'
Require21 ((U16LE-21 $bytes 4) -eq 2 -and (U16LE-21 $bytes 6) -eq 4 -and (U32LE-21 $bytes 20) -eq 1) 'PCAP link type is invalid.'
$frames = @(); $offset = 24
while ($offset -lt $bytes.Length) {
    Require21 ($offset + 16 -le $bytes.Length) 'PCAP record header is truncated.'
    $included = [int](U32LE-21 $bytes ($offset + 8)); $original = [int](U32LE-21 $bytes ($offset + 12))
    Require21 ($included -ge 60 -and $included -le $original -and $offset + 16 + $included -le $bytes.Length) 'PCAP record length is invalid.'
    $frames += ,([byte[]]$bytes[($offset + 16)..($offset + 15 + $included)]); $offset += 16 + $included
}
Require21 ($frames.Count -gt 0) 'PCAP contains no frames.'
foreach ($frame in $frames) {
    Require21 ($frame.Length -ge 60) 'Frame is shorter than minimum Ethernet length.'
    if ($frame[12] -eq 8 -and $frame[13] -eq 0 -and $frame.Length -ge 34) {
        $total = U16-21 $frame 16
        Require21 ($frame[14] -eq 0x45 -and $total -ge 20 -and 14 + $total -le $frame.Length -and (Sum21 $frame 14 20) -eq 0) 'IPv4 checksum or length is invalid.'
    }
}

$guest = ParseMac21 $GuestMac
$peerIp = [byte[]](10,15,0,2); $leaseIp = [byte[]](10,15,0,42); $staticIp = [byte[]](10,15,0,1)
$hostIp = [byte[]](10,15,0,2); $broadcastIp = [byte[]](255,255,255,255)
$udp = @($frames | ForEach-Object { ParseUdp21 $_ } | Where-Object { $null -ne $_ })
$dhcp = @($udp | Where-Object { ($_.SourcePort -eq 67 -or $_.SourcePort -eq 68) -and (GetOption21 $_.Payload 53) -ne $null })
Require21 ($dhcp.Count -ge 4) 'DHCP DORA is incomplete.'
$discover = @($dhcp | Where-Object { $_.SourcePort -eq 68 -and $_.DestinationPort -eq 67 -and (GetOption21 $_.Payload 53)[0] -eq 1 } | Select-Object -First 1)
Require21 ($discover.Count -eq 1) 'DHCPDISCOVER is missing.'
$xid = U32-21 $discover[0].Payload 4
$offer = @($dhcp | Where-Object { $_.SourcePort -eq 67 -and $_.DestinationPort -eq 68 -and (GetOption21 $_.Payload 53)[0] -eq 2 -and (U32-21 $_.Payload 4) -eq $xid -and (Eq21 $_.Payload 16 $leaseIp) } | Select-Object -First 1)
$request = @($dhcp | Where-Object { $_.SourcePort -eq 68 -and $_.DestinationPort -eq 67 -and (GetOption21 $_.Payload 53)[0] -eq 3 -and (U32-21 $_.Payload 4) -eq $xid } | Select-Object -First 1)
$ack = @($dhcp | Where-Object { $_.SourcePort -eq 67 -and $_.DestinationPort -eq 68 -and (GetOption21 $_.Payload 53)[0] -eq 5 -and (U32-21 $_.Payload 4) -eq $xid -and (Eq21 $_.Payload 16 $leaseIp) } | Select-Object -First 1)
Require21 ($offer.Count -eq 1 -and $request.Count -eq 1 -and $ack.Count -eq 1) 'DHCP DORA xid/order is invalid.'
Require21 ((GetOption21 $offer[0].Payload 6).Length -eq 4 -and (Eq21 (GetOption21 $offer[0].Payload 6) 0 $peerIp) -and (GetOption21 $ack[0].Payload 6).Length -eq 4 -and (Eq21 (GetOption21 $ack[0].Payload 6) 0 $peerIp)) 'DHCP Option 6 DNS server is invalid.'

$question = [byte[]](7,112,104,97,115,101,50,49,4,116,101,115,116,0,0,1,0,1)
$dnsQueries = @($udp | Where-Object { $_.SourcePort -eq 15200 -and $_.DestinationPort -eq 53 -and $_.Payload.Length -eq 30 -and (Eq21 $_.Payload 12 $question) })
$dnsResponses = @($udp | Where-Object { $_.SourcePort -eq 53 -and $_.DestinationPort -eq 15200 -and $_.Payload.Length -eq 46 -and (Eq21 $_.Payload 12 $question) -and (Eq21 $_.Payload 42 $peerIp) })
Require21 ($dnsQueries.Count -ge 2 -and $dnsResponses.Count -ge 2) 'Phase 21 DNS query/response proof is incomplete.'
$icmpRequests = @($frames | Where-Object { $_.Length -ge 42 -and $_[12] -eq 8 -and $_[13] -eq 0 -and $_[23] -eq 1 -and (Eq21 $_ 26 $leaseIp) -and (Eq21 $_ 30 $peerIp) })
$icmpReplies = @($frames | Where-Object { $_.Length -ge 42 -and $_[12] -eq 8 -and $_[13] -eq 0 -and $_[23] -eq 1 -and (Eq21 $_ 26 $peerIp) -and (Eq21 $_ 30 $leaseIp) })
Require21 ($icmpRequests.Count -ge 2 -and $icmpReplies.Count -ge 2) 'Phase 21 ICMP resolver-output proof is incomplete.'
$hello = [byte[]](80,72,65,83,69,50,49,45,65,80,73,45,72,69,76,76,79)
$ackPayload = [byte[]](80,72,65,83,69,50,49,45,65,80,73,45,65,67,75)
$appRequests = @($udp | Where-Object { $_.SourcePort -eq 15210 -and $_.DestinationPort -eq 15211 -and (Eq21 $_.Payload 0 $hello) })
$appReplies = @($udp | Where-Object { $_.SourcePort -eq 15211 -and $_.DestinationPort -eq 15210 -and (Eq21 $_.Payload 0 $ackPayload) })
Require21 ($appRequests.Count -ge 2 -and $appReplies.Count -ge 2) 'Phase 21 UDP application exchange proof is incomplete.'
Require21 (@($frames | Where-Object { $_.Length -ge 34 -and $_[12] -eq 8 -and $_[13] -eq 0 -and (Eq21 $_ 26 $staticIp) }).Count -eq 0) 'Static pre-DHCP IPv4 identity leaked onto the wire.'
Write-Output ('MANAGED_E1000_PHASE21_PCAP=PASS packets={0} dhcp={1} dns_queries={2} dns_responses={3} icmp_requests={4} icmp_replies={5} udp_requests={6} udp_replies={7} resolved_ipv4=0A0F0002 payload=PHASE21-API-HELLO reply=PHASE21-API-ACK' -f $frames.Count, $dhcp.Count, $dnsQueries.Count, $dnsResponses.Count, $icmpRequests.Count, $icmpReplies.Count, $appRequests.Count, $appReplies.Count)
