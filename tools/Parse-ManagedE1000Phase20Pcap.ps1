[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$PcapPath,
    [Parameter(Mandatory = $true)] [string]$GuestMac
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Require20([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}

function U16-20([byte[]]$bytes, [int]$offset) {
    return (([int]$bytes[$offset] -shl 8) -bor [int]$bytes[$offset + 1])
}

function U16LE-20([byte[]]$bytes, [int]$offset) {
    return ([int]$bytes[$offset] -bor ([int]$bytes[$offset + 1] -shl 8))
}

function U32LE-20([byte[]]$bytes, [int]$offset) {
    return [uint32]([int]$bytes[$offset] -bor
        ([int]$bytes[$offset + 1] -shl 8) -bor
        ([int]$bytes[$offset + 2] -shl 16) -bor
        ([int]$bytes[$offset + 3] -shl 24))
}

function U32-20([byte[]]$bytes, [int]$offset) {
    return [uint32](([uint64]$bytes[$offset] * 16777216) +
        ([uint64]$bytes[$offset + 1] * 65536) +
        ([uint64]$bytes[$offset + 2] * 256) + [uint64]$bytes[$offset + 3])
}

function Eq20([byte[]]$left, [int]$offset, [byte[]]$right) {
    if ($null -eq $left -or $null -eq $right -or $offset -lt 0 -or
        $offset + $right.Length -gt $left.Length) { return $false }
    for ($index = 0; $index -lt $right.Length; ++$index) {
        if ($left[$offset + $index] -ne $right[$index]) { return $false }
    }
    return $true
}

function Sum20([byte[]]$bytes, [int]$offset, [int]$length) {
    [uint32]$sum = 0
    $index = 0
    while ($index + 1 -lt $length) {
        $sum += [uint32](([int]$bytes[$offset + $index] -shl 8) -bor
            [int]$bytes[$offset + $index + 1])
        $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
        $index += 2
    }
    if ($index -lt $length) {
        $sum += [uint32]$bytes[$offset + $index] -shl 8
        $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    }
    $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    return [uint16]((-bnot [int]$sum) -band 0xFFFF)
}

function UdpChecksum20([byte[]]$source, [byte[]]$destination,
                        [byte[]]$udp) {
    [uint32]$sum = 0
    foreach ($offset in @(0, 2)) {
        $sum += ([uint32]$source[$offset] -shl 8) -bor $source[$offset + 1]
        $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
        $sum += ([uint32]$destination[$offset] -shl 8) -bor $destination[$offset + 1]
        $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    }
    $sum += 17; $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    $sum += $udp.Length; $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    $index = 0
    while ($index + 1 -lt $udp.Length) {
        $sum += ([uint32]$udp[$index] -shl 8) -bor $udp[$index + 1]
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

function ParseIpv4Header20([byte[]]$frame) {
    if ($frame.Length -lt 34 -or $frame[12] -ne 8 -or $frame[13] -ne 0 -or
        $frame[14] -ne 0x45 -or (U16-20 $frame 20) -ne 0) { return $null }
    $totalLength = U16-20 $frame 16
    if ($totalLength -lt 20 -or 14 + $totalLength -gt $frame.Length -or
        (Sum20 $frame 14 20) -ne 0) { return $null }
    [pscustomobject]@{
        Frame = $frame
        DestinationMac = [byte[]]$frame[0..5]
        SourceMac = [byte[]]$frame[6..11]
        SourceIp = [byte[]]$frame[26..29]
        DestinationIp = [byte[]]$frame[30..33]
        Protocol = $frame[23]
        TotalLength = $totalLength
    }
}

function ParseUdp20([byte[]]$frame) {
    $ip = ParseIpv4Header20 $frame
    if ($null -eq $ip -or $ip.Protocol -ne 17 -or $ip.TotalLength -lt 28) { return $null }
    $udpLength = U16-20 $frame 38
    if ($udpLength -lt 8 -or 14 + 20 + $udpLength -gt $frame.Length -or
        $udpLength -ne $ip.TotalLength - 20) { return $null }
    $source = $ip.SourceIp
    $destination = $ip.DestinationIp
    $udp = [byte[]]$frame[34..(33 + $udpLength)]
    $checksum = U16-20 $udp 6
    if ($checksum -ne 0 -and (UdpChecksum20 $source $destination $udp) -ne 0) {
        return $null
    }
    [pscustomobject]@{
        Frame = $frame
        DestinationMac = $ip.DestinationMac
        SourceMac = $ip.SourceMac
        SourceIp = $source
        DestinationIp = $destination
        SourcePort = U16-20 $udp 0
        DestinationPort = U16-20 $udp 2
        Checksum = $checksum
        Payload = if ($udpLength -gt 8) { [byte[]]$udp[8..($udpLength - 1)] } else { [byte[]]@() }
    }
}

function DhcpOption20([byte[]]$payload, [int]$wanted) {
    if ($payload.Length -lt 241 -or (U32-20 $payload 236) -ne 0x63825363) {
        return $null
    }
    $offset = 240
    while ($offset -lt $payload.Length) {
        $code = $payload[$offset++]
        if ($code -eq 0) { continue }
        if ($code -eq 255) { return $null }
        if ($offset -ge $payload.Length) { return $null }
        $length = [int]$payload[$offset++]
        if ($offset + $length -gt $payload.Length) { return $null }
        if ($code -eq $wanted) {
            if ($length -eq 0) { return [byte[]]@() }
            return ,([byte[]]$payload[$offset..($offset + $length - 1)])
        }
        $offset += $length
    }
    return $null
}

function ParseDhcp20([object]$udp) {
    if ($null -eq $udp -or $udp.SourcePort -ne 68 -and $udp.SourcePort -ne 67) {
        return $null
    }
    $payload = [byte[]]$udp.Payload
    if ($payload.Length -lt 240 -or (U32-20 $payload 236) -ne 0x63825363) {
        return $null
    }
    $type = DhcpOption20 $payload 53
    if ($null -eq $type -or $type.Length -ne 1) { return $null }
    [pscustomobject]@{
        Udp = $udp
        Type = $type[0]
        Xid = U32-20 $payload 4
        Op = $payload[0]
        HardwareType = $payload[1]
        HardwareLength = $payload[2]
        Flags = U16-20 $payload 10
        Yiaddr = [byte[]]$payload[16..19]
        Chaddr = [byte[]]$payload[28..33]
        ServerIdentifier = DhcpOption20 $payload 54
        RequestedIp = DhcpOption20 $payload 50
        SubnetMask = DhcpOption20 $payload 1
        LeaseTime = DhcpOption20 $payload 51
        DnsServer = DhcpOption20 $payload 6
    }
}

function ParseMac20([string]$text) {
    Require20 ($text -match '^[0-9A-Fa-f]{12}$') 'Guest MAC is invalid.'
    $mac = New-Object byte[] 6
    for ($index = 0; $index -lt 6; ++$index) {
        $mac[$index] = [Convert]::ToByte($text.Substring($index * 2, 2), 16)
    }
    return $mac
}

function ParseDnsAnswer20([byte[]]$message, [byte[]]$question,
                           [int]$expectedId) {
    if ($message.Length -ne 46 -or (U16-20 $message 0) -ne $expectedId -or
        (U16-20 $message 2) -ne 0x8180 -or (U16-20 $message 4) -ne 1 -or
        (U16-20 $message 6) -ne 1 -or (U16-20 $message 8) -ne 0 -or
        (U16-20 $message 10) -ne 0 -or !(Eq20 $message 12 $question)) {
        return $null
    }
    $answer = 12 + $question.Length
    if ($message[$answer] -ne 0xC0 -or $message[$answer + 1] -ne 0x0C -or
        (U16-20 $message ($answer + 2)) -ne 1 -or
        (U16-20 $message ($answer + 4)) -ne 1 -or
        (U32-20 $message ($answer + 6)) -ne 300 -or
        (U16-20 $message ($answer + 10)) -ne 4 -or
        !(Eq20 $message ($answer + 12) ([byte[]](10, 15, 0, 2))) ) {
        return $null
    }
    [pscustomobject]@{ Address = [byte[]](10, 15, 0, 2); Ttl = 300; Pointer = 0x0C }
}

function ParseDnsNxDomain20([byte[]]$message, [byte[]]$question,
                            [int]$expectedId) {
    return $message.Length -eq 12 + $question.Length -and
        (U16-20 $message 0) -eq $expectedId -and
        (U16-20 $message 2) -eq 0x8183 -and
        (U16-20 $message 4) -eq 1 -and (U16-20 $message 6) -eq 0 -and
        (U16-20 $message 8) -eq 0 -and (U16-20 $message 10) -eq 0 -and
        (Eq20 $message 12 $question)
}

$bytes = [IO.File]::ReadAllBytes([IO.Path]::GetFullPath($PcapPath))
Require20 ($bytes.Length -ge 24 -and $bytes[0] -eq 0xD4 -and
    $bytes[1] -eq 0xC3 -and $bytes[2] -eq 0xB2 -and $bytes[3] -eq 0xA1) `
    'PCAP header is missing or has unsupported byte order.'
Require20 ((U16LE-20 $bytes 4) -eq 2 -and (U16LE-20 $bytes 6) -eq 4 -and
    (U32LE-20 $bytes 20) -eq 1) 'PCAP header fields are unsupported.'

$frames = @()
$offset = 24
while ($offset -lt $bytes.Length) {
    Require20 ($offset + 16 -le $bytes.Length) 'PCAP record header is truncated.'
    $included = [int](U32LE-20 $bytes ($offset + 8))
    $original = [int](U32LE-20 $bytes ($offset + 12))
    Require20 ($included -ge 60 -and $included -le $original -and
        $offset + 16 + $included -le $bytes.Length) 'PCAP record length is invalid.'
    $frames += ,([byte[]]$bytes[($offset + 16)..($offset + 15 + $included)])
    $offset += 16 + $included
}
Require20 ($frames.Count -gt 0) 'PCAP contains no Ethernet frames.'
foreach ($frame in $frames) {
    Require20 ($frame.Length -ge 60) 'Captured Ethernet frame is shorter than the minimum wire frame.'
    Require20 (($frame[12] -eq 8 -and ($frame[13] -eq 0 -or $frame[13] -eq 6)) -or
        ($frame[12] -eq 0x88 -and $frame[13] -eq 0xB5)) `
        'PCAP contains an unsupported EtherType.'
    if ($frame[12] -eq 8 -and $frame[13] -eq 0) {
        Require20 ($null -ne (ParseIpv4Header20 $frame)) 'An IPv4 header/checksum/length is invalid.'
    }
}

$guest = ParseMac20 $GuestMac
$hostMac = [byte[]](2, 21, 0, 0, 0, 2)
$broadcastMac = [byte[]](255, 255, 255, 255, 255, 255)
$zeroMac = [byte[]](0, 0, 0, 0, 0, 0)
$zeroIp = [byte[]](0, 0, 0, 0)
$broadcastIp = [byte[]](255, 255, 255, 255)
$hostIp = [byte[]](10, 15, 0, 2)
$leaseIp = [byte[]](10, 15, 0, 42)
$staticIp = [byte[]](10, 15, 0, 1)
$phaseQuestion = [byte[]](7,112,104,97,115,101,50,48,4,116,101,115,116,0,0,1,0,1)
$missingQuestion = [byte[]](7,109,105,115,115,105,110,103,7,112,104,97,115,101,50,48,4,116,101,115,116,0,0,1,0,1)

$parsedUdp = @($frames | ForEach-Object { ParseUdp20 $_ } | Where-Object { $null -ne $_ })
$dhcp = @($parsedUdp | ForEach-Object { ParseDhcp20 $_ } | Where-Object { $null -ne $_ })
Require20 ($dhcp.Count -ge 4) 'PCAP has fewer than four valid DHCP messages.'
$discover = @($dhcp | Where-Object { $_.Type -eq 1 -and $_.Udp.SourcePort -eq 68 -and $_.Udp.DestinationPort -eq 67 } | Select-Object -First 1)
Require20 ($discover.Count -eq 1) 'DHCPDISCOVER was not found.'
$xid = $discover[0].Xid
$offer = @($dhcp | Where-Object { $_.Type -eq 2 -and $_.Xid -eq $xid -and $_.Udp.SourcePort -eq 67 -and $_.Udp.DestinationPort -eq 68 -and $_.Op -eq 2 -and (Eq20 $_.Yiaddr 0 $leaseIp) -and (Eq20 $_.Chaddr 0 $guest) } | Select-Object -First 1)
$request = @($dhcp | Where-Object { $_.Type -eq 3 -and $_.Xid -eq $xid -and $_.Udp.SourcePort -eq 68 -and $_.Udp.DestinationPort -eq 67 } | Select-Object -First 1)
$ack = @($dhcp | Where-Object { $_.Type -eq 5 -and $_.Xid -eq $xid -and $_.Udp.SourcePort -eq 67 -and $_.Udp.DestinationPort -eq 68 -and $_.Op -eq 2 -and (Eq20 $_.Yiaddr 0 $leaseIp) -and (Eq20 $_.Chaddr 0 $guest) } | Select-Object -First 1)
Require20 ($offer.Count -eq 1 -and $request.Count -eq 1 -and $ack.Count -eq 1) 'DHCP DORA sequence is incomplete.'
foreach ($message in @($discover[0], $request[0])) {
    Require20 ((Eq20 $message.Udp.SourceMac 0 $guest) -and (Eq20 $message.Udp.DestinationMac 0 $broadcastMac) -and
        (Eq20 $message.Udp.SourceIp 0 $zeroIp) -and (Eq20 $message.Udp.DestinationIp 0 $broadcastIp) -and
        $message.Flags -eq 0x8000 -and $message.HardwareType -eq 1 -and $message.HardwareLength -eq 6 -and
        (Eq20 $message.Chaddr 0 $guest)) 'DHCP client BOOTP fields are invalid.'
}
Require20 ($request[0].RequestedIp -ne $null -and (Eq20 $request[0].RequestedIp 0 $leaseIp) -and
    $request[0].ServerIdentifier -ne $null -and (Eq20 $request[0].ServerIdentifier 0 $hostIp)) `
    'DHCPREQUEST requested-IP/server-ID options are invalid.'
foreach ($message in @($offer[0], $ack[0])) {
    Require20 ((Eq20 $message.Udp.SourceMac 0 $hostMac) -and (Eq20 $message.Udp.DestinationMac 0 $broadcastMac) -and
        (Eq20 $message.Udp.SourceIp 0 $hostIp) -and (Eq20 $message.Udp.DestinationIp 0 $broadcastIp) -and
        $message.Xid -eq $xid -and (Eq20 $message.Chaddr 0 $guest) -and
        (Eq20 $message.SubnetMask 0 ([byte[]](255,255,255,0))) -and
        (Eq20 $message.ServerIdentifier 0 $hostIp) -and $message.LeaseTime -ne $null -and
        (U32-20 $message.LeaseTime 0) -eq 3600 -and $message.DnsServer.Length -eq 4 -and
        (Eq20 $message.DnsServer 0 $hostIp)) 'DHCP response or Option 6 fields are invalid.'
}

$arpFrames = @($frames | Where-Object { $_.Length -ge 42 -and $_[12] -eq 8 -and $_[13] -eq 6 })
Require20 ($arpFrames.Count -ge 2) 'DNS ARP proof is missing.'
$dnsArpRequest = @($arpFrames | Where-Object { (Eq20 $_ 0 $broadcastMac) -and (Eq20 $_ 6 $guest) -and
    (U16-20 $_ 20) -eq 1 -and (Eq20 $_ 22 $guest) -and (Eq20 $_ 28 $leaseIp) -and
    (Eq20 $_ 32 $zeroMac) -and (Eq20 $_ 38 $hostIp) } | Select-Object -First 1)
$dnsArpReply = @($arpFrames | Where-Object { (Eq20 $_ 0 $guest) -and (Eq20 $_ 6 $hostMac) -and
    (U16-20 $_ 20) -eq 2 -and (Eq20 $_ 22 $hostMac) -and (Eq20 $_ 28 $hostIp) -and
    (Eq20 $_ 32 $guest) -and (Eq20 $_ 38 $leaseIp) } | Select-Object -First 1)
Require20 ($dnsArpRequest.Count -eq 1 -and $dnsArpReply.Count -eq 1) 'DNS ARP fields are invalid.'

$dnsQueries = @($parsedUdp | Where-Object { $_.SourcePort -eq 15200 -and $_.DestinationPort -eq 53 -and
    (Eq20 $_.SourceMac 0 $guest) -and (Eq20 $_.SourceIp 0 $leaseIp) -and (Eq20 $_.DestinationIp 0 $hostIp) })
Require20 ($dnsQueries.Count -ge 4) 'Fewer than four managed DNS queries reached the DHCP-provided server.'
foreach ($query in $dnsQueries) {
    Require20 ($query.Checksum -ne 0) 'Managed DNS query used a zero UDP checksum.'
    Require20 ($query.Payload.Length -ge 17 -and (U16-20 $query.Payload 2) -eq 0x0100 -and
        (U16-20 $query.Payload 4) -eq 1 -and (U16-20 $query.Payload 6) -eq 0 -and
        (U16-20 $query.Payload 8) -eq 0 -and (U16-20 $query.Payload 10) -eq 0) `
        'Managed DNS query header is invalid.'
}
$phaseQuery = @($dnsQueries | Where-Object { $_.Payload.Length -eq 30 -and (Eq20 $_.Payload 12 $phaseQuestion) } | Select-Object -First 1)
$missingQuery = @($dnsQueries | Where-Object { $_.Payload.Length -eq 38 -and (Eq20 $_.Payload 12 $missingQuestion) } | Select-Object -First 1)
Require20 ($phaseQuery.Count -ge 1 -and $missingQuery.Count -eq 1) 'Required DNS QNAMEs were not captured.'
$phaseIds = @($dnsQueries | ForEach-Object { U16-20 $_.Payload 0 })
Require20 ($phaseIds | Select-Object -Unique).Count -eq $phaseIds.Count 'DNS transaction IDs were reused across queries.'
$firstId = U16-20 $phaseQuery[0].Payload 0
$dnsResponses = @($parsedUdp | Where-Object { $_.SourcePort -eq 53 -and $_.DestinationPort -eq 15200 -and
    (Eq20 $_.SourceMac 0 $hostMac) -and (Eq20 $_.SourceIp 0 $hostIp) -and (Eq20 $_.DestinationIp 0 $leaseIp) })
Require20 ($dnsResponses.Count -ge 4) 'Fewer than four DNS responses reached the managed client.'
Require20 (@($dnsResponses | Where-Object { $_.Checksum -eq 0 }).Count -eq 0) 'Authoritative DNS response used a zero UDP checksum.'
$resolvedResponse = $null
$resolvedAnswer = $null
foreach ($response in $dnsResponses) {
    if ((U16-20 $response.Payload 0) -eq $firstId) {
        $candidate = ParseDnsAnswer20 $response.Payload $phaseQuestion $firstId
        if ($null -ne $candidate) { $resolvedResponse = $response; $resolvedAnswer = $candidate; break }
    }
}
Require20 ($null -ne $resolvedResponse -and $null -ne $resolvedAnswer -and $resolvedAnswer.Pointer -eq 0x0C) `
    'A valid compressed DNS response was not captured.'
Require20 ((U32-20 $resolvedAnswer.Address 0) -eq 0x0A0F0002) 'DNS A record did not resolve to 10.15.0.2.'
$nxId = U16-20 $missingQuery[0].Payload 0
$nxResponse = @($dnsResponses | Where-Object { (U16-20 $_.Payload 0) -eq $nxId -and (ParseDnsNxDomain20 $_.Payload $missingQuestion $nxId) } | Select-Object -First 1)
Require20 ($nxResponse.Count -eq 1) 'NXDOMAIN response was not independently validated.'
$resolvedIp = $resolvedAnswer.Address
$resolvedIcmp = @($frames | Where-Object { $_.Length -ge 34 -and $_[12] -eq 8 -and $_[13] -eq 0 -and $_[23] -eq 1 -and
    (Eq20 $_ 6 $guest) -and (Eq20 $_ 26 $leaseIp) -and (Eq20 $_ 30 $resolvedIp) })
$resolvedUdp = @($parsedUdp | Where-Object { (Eq20 $_.SourceMac 0 $guest) -and (Eq20 $_.SourceIp 0 $leaseIp) -and
    (Eq20 $_.DestinationIp 0 $resolvedIp) -and $_.SourcePort -eq 15180 -and $_.DestinationPort -eq 15181 })
Require20 ($resolvedIcmp.Count -ge 2 -and $resolvedUdp.Count -ge 2) `
    'Post-resolution ICMP/UDP destinations do not prove use of the DNS A record.'
$staleStatic = @($frames | Where-Object { $_.Length -ge 34 -and $_[12] -eq 8 -and $_[13] -eq 0 -and
    (Eq20 $_ 6 $guest) -and (Eq20 $_ 26 $staticIp) })
Require20 ($staleStatic.Count -eq 0) 'A guest IPv4 frame retained the pre-DHCP static identity.'

Write-Output ('MANAGED_E1000_PHASE20_PCAP=PASS packets={0} dhcp={1} arp={2} dns_queries={3} dns_responses={4} resolved_icmp={5} resolved_udp={6} resolved_ipv4=0A0F0002' -f
    $frames.Count, $dhcp.Count, $arpFrames.Count, $dnsQueries.Count, $dnsResponses.Count,
    $resolvedIcmp.Count, $resolvedUdp.Count)
