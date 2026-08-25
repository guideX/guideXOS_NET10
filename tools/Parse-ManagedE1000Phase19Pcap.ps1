[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$PcapPath,
    [Parameter(Mandatory = $true)] [string]$GuestMac
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Require19([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}

function U16-19([byte[]]$bytes, [int]$offset) {
    return (([int]$bytes[$offset] -shl 8) -bor [int]$bytes[$offset + 1])
}

function U16LE-19([byte[]]$bytes, [int]$offset) {
    return ([int]$bytes[$offset] -bor ([int]$bytes[$offset + 1] -shl 8))
}

function U32LE-19([byte[]]$bytes, [int]$offset) {
    return [uint32]([int]$bytes[$offset] -bor
        ([int]$bytes[$offset + 1] -shl 8) -bor
        ([int]$bytes[$offset + 2] -shl 16) -bor
        ([int]$bytes[$offset + 3] -shl 24))
}

function U32-19([byte[]]$bytes, [int]$offset) {
    return [uint32](([uint64]$bytes[$offset] * 16777216) +
        ([uint64]$bytes[$offset + 1] * 65536) +
        ([uint64]$bytes[$offset + 2] * 256) +
        [uint64]$bytes[$offset + 3])
}

function Eq19([byte[]]$left, [int]$offset, [byte[]]$right) {
    if ($null -eq $left -or $null -eq $right -or $offset -lt 0 -or
        $offset + $right.Length -gt $left.Length) { return $false }
    for ($index = 0; $index -lt $right.Length; ++$index) {
        if ($left[$offset + $index] -ne $right[$index]) { return $false }
    }
    return $true
}

function Sum19([byte[]]$bytes, [int]$offset, [int]$length) {
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

function UdpChecksum19([byte[]]$source, [byte[]]$destination,
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

function ParseUdp19([byte[]]$frame) {
    if ($frame.Length -lt 42 -or $frame[12] -ne 8 -or $frame[13] -ne 0 -or
        $frame[14] -ne 0x45 -or $frame[23] -ne 17) { return $null }
    $totalLength = U16-19 $frame 16
    $udpLength = U16-19 $frame 38
    if ($totalLength -ne 20 + $udpLength -or $udpLength -lt 8 -or
        34 + $udpLength -gt $frame.Length -or (Sum19 $frame 14 20) -ne 0) {
        return $null
    }
    $source = [byte[]]$frame[26..29]
    $destination = [byte[]]$frame[30..33]
    $udp = [byte[]]$frame[34..(33 + $udpLength)]
    $udpChecksum = U16-19 $udp 6
    if ($udpChecksum -ne 0 -and
        (UdpChecksum19 $source $destination $udp) -ne 0) {
        return $null
    }
    [pscustomobject]@{
        Frame = $frame
        DestinationMac = [byte[]]$frame[0..5]
        SourceMac = [byte[]]$frame[6..11]
        SourceIp = $source
        DestinationIp = $destination
        SourcePort = U16-19 $udp 0
        DestinationPort = U16-19 $udp 2
        Payload = if ($udpLength -gt 8) { [byte[]]$udp[8..($udpLength - 1)] } else { [byte[]]@() }
    }
}

function DhcpOption19([byte[]]$payload, [int]$wanted) {
    if ($payload.Length -lt 241 -or (U32-19 $payload 236) -ne 0x63825363) {
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
            return ,([byte[]]$payload[$offset..($offset + $length - 1)])
        }
        $offset += $length
    }
    return $null
}

function ParseDhcp19([object]$udp) {
    if ($null -eq $udp) { return $null }
    $payloadItems = @($udp.Payload)
    if ($payloadItems.Count -lt 240) { return $null }
    $payload = New-Object byte[] $payloadItems.Count
    for ($index = 0; $index -lt $payloadItems.Count; ++$index) {
        $payload[$index] = [byte]$payloadItems[$index]
    }
    if ((U32-19 $payload 236) -ne 0x63825363) {
        return $null
    }
    $typeItems = @(DhcpOption19 $payload 53)
    if ($typeItems.Count -ne 1) { return $null }
    $typeValue = $typeItems[0]
    if ($typeValue -is [Array]) {
        if ($typeValue.Count -ne 1) { return $null }
        $typeValue = $typeValue[0]
    }
    [byte]$typeByte = $typeValue
    [pscustomobject]@{
        Udp = $udp
        Type = $typeByte
        Xid = U32-19 $payload 4
        Op = $payload[0]
        HardwareType = $payload[1]
        HardwareLength = $payload[2]
        Flags = U16-19 $payload 10
        Yiaddr = [byte[]]$payload[16..19]
        Chaddr = [byte[]]$payload[28..33]
        ServerIdentifier = DhcpOption19 $payload 54
        RequestedIp = DhcpOption19 $payload 50
        SubnetMask = DhcpOption19 $payload 1
        LeaseTime = DhcpOption19 $payload 51
    }
}

function ParseMac19([string]$text) {
    Require19 ($text -match '^[0-9A-Fa-f]{12}$') 'Guest MAC is invalid.'
    $mac = New-Object byte[] 6
    for ($index = 0; $index -lt 6; ++$index) {
        $mac[$index] = [Convert]::ToByte($text.Substring($index * 2, 2), 16)
    }
    return $mac
}

$bytes = [IO.File]::ReadAllBytes([IO.Path]::GetFullPath($PcapPath))
Require19 ($bytes.Length -ge 24 -and $bytes[0] -eq 0xD4 -and
           $bytes[1] -eq 0xC3 -and $bytes[2] -eq 0xB2 -and $bytes[3] -eq 0xA1) `
           'PCAP header is missing or has unsupported byte order.'
Require19 ((U16LE-19 $bytes 4) -eq 2 -and (U16LE-19 $bytes 6) -eq 4 -and
           (U32LE-19 $bytes 20) -eq 1) 'PCAP header fields are unsupported.'

$frames = @()
$offset = 24
while ($offset -lt $bytes.Length) {
    Require19 ($offset + 16 -le $bytes.Length) 'PCAP record header is truncated.'
    $included = [int](U32LE-19 $bytes ($offset + 8))
    $original = [int](U32LE-19 $bytes ($offset + 12))
    Require19 ($included -ge 60 -and $included -le $original -and
               $offset + 16 + $included -le $bytes.Length) 'PCAP record length is invalid.'
    $frames += ,([byte[]]$bytes[($offset + 16)..($offset + 15 + $included)])
    $offset += 16 + $included
}

$guest = ParseMac19 $GuestMac
$hostMac = [byte[]](2, 21, 0, 0, 0, 2)
$broadcastMac = [byte[]](255, 255, 255, 255, 255, 255)
$zeroIp = [byte[]](0, 0, 0, 0)
$broadcastIp = [byte[]](255, 255, 255, 255)
$hostIp = [byte[]](10, 15, 0, 2)
$leaseIp = [byte[]](10, 15, 0, 42)
$staticIp = [byte[]](10, 15, 0, 1)
$parsedUdp = @($frames | ForEach-Object { ParseUdp19 $_ } | Where-Object { $null -ne $_ })
$dhcp = @($parsedUdp | ForEach-Object { ParseDhcp19 $_ } | Where-Object { $null -ne $_ })
Require19 ($dhcp.Count -ge 4) 'PCAP has fewer than four valid DHCP messages.'

$discover = @($dhcp | Where-Object { $_.Type -eq 1 -and $_.Udp.SourcePort -eq 68 -and $_.Udp.DestinationPort -eq 67 } | Select-Object -First 1)
Require19 ($discover.Count -eq 1) 'DHCPDISCOVER was not found.'
$xid = $discover[0].Xid
$offer = @($dhcp | Where-Object { $_.Type -eq 2 -and $_.Xid -eq $xid -and $_.Udp.SourcePort -eq 67 -and $_.Udp.DestinationPort -eq 68 -and $_.Op -eq 2 -and (Eq19 $_.Yiaddr 0 $leaseIp) -and (Eq19 $_.Chaddr 0 $guest) } | Select-Object -First 1)
$request = @($dhcp | Where-Object { $_.Type -eq 3 -and $_.Xid -eq $xid -and $_.Udp.SourcePort -eq 68 -and $_.Udp.DestinationPort -eq 67 } | Select-Object -First 1)
$ack = @($dhcp | Where-Object { $_.Type -eq 5 -and $_.Xid -eq $xid -and $_.Udp.SourcePort -eq 67 -and $_.Udp.DestinationPort -eq 68 -and $_.Op -eq 2 -and (Eq19 $_.Yiaddr 0 $leaseIp) -and (Eq19 $_.Chaddr 0 $guest) } | Select-Object -First 1)
Require19 ($offer.Count -eq 1 -and $request.Count -eq 1 -and $ack.Count -eq 1) `
           'DORA valid-message sequence is incomplete.'
foreach ($message in @($discover[0], $request[0])) {
    Require19 ((Eq19 $message.Udp.SourceMac 0 $guest) -and
               (Eq19 $message.Udp.DestinationMac 0 $broadcastMac) -and
               (Eq19 $message.Udp.SourceIp 0 $zeroIp) -and
               (Eq19 $message.Udp.DestinationIp 0 $broadcastIp) -and
               $message.Flags -eq 0x8000 -and $message.HardwareType -eq 1 -and
               $message.HardwareLength -eq 6 -and (Eq19 $message.Chaddr 0 $guest)) `
               'Client DHCP BOOTP fields are invalid.'
}
Require19 ($discover[0].Type -eq 1 -and $request[0].Type -eq 3) 'DHCP message types are invalid.'
Require19 ($request[0].RequestedIp -ne $null -and
           (Eq19 $request[0].RequestedIp 0 $leaseIp) -and
           $request[0].ServerIdentifier -ne $null -and
           (Eq19 $request[0].ServerIdentifier 0 $hostIp)) `
           'DHCPREQUEST requested-IP/server-ID options are invalid.'

foreach ($message in @($offer[0], $ack[0])) {
    Require19 ((Eq19 $message.Udp.SourceMac 0 $hostMac) -and
               (Eq19 $message.Udp.DestinationMac 0 $broadcastMac) -and
               (Eq19 $message.Udp.SourceIp 0 $hostIp) -and
               (Eq19 $message.Udp.DestinationIp 0 $broadcastIp) -and
               $message.Xid -eq $xid -and (Eq19 $message.Chaddr 0 $guest) -and
               $message.SubnetMask -ne $null -and
               (Eq19 $message.SubnetMask 0 ([byte[]](255,255,255,0))) -and
               (Eq19 $message.ServerIdentifier 0 $hostIp) -and
               $message.LeaseTime -ne $null -and
               (U32-19 $message.LeaseTime 0) -eq 3600) `
               'DHCP server response fields are invalid.'
}

$serverDhcpFrames = @($parsedUdp | Where-Object {
    $_.SourcePort -eq 67 -and $_.DestinationPort -eq 68 -and
    (Eq19 $_.SourceIp 0 $hostIp) -and (Eq19 $_.DestinationIp 0 $broadcastIp)
}).Count
$malformedDhcpCount = $serverDhcpFrames - $offer.Count - $ack.Count
Require19 ($malformedDhcpCount -ge 5) 'Malformed DHCP controls are missing from PCAP.'

$arpFrames = @($frames | Where-Object {
    $_.Length -ge 42 -and $_[12] -eq 8 -and $_[13] -eq 6
})
Require19 ($arpFrames.Count -ge 2) 'Post-bind ARP proof is missing.'
$leasedArp = @($arpFrames | Where-Object {
    (Eq19 $_ 6 $guest) -and (Eq19 $_ 28 $leaseIp)
} | Select-Object -First 1)
Require19 ($leasedArp.Count -eq 1) 'ARP request does not use the leased IPv4 identity.'

$leasedIcmp = @($frames | Where-Object {
    $_.Length -ge 34 -and $_[12] -eq 8 -and $_[13] -eq 0 -and $_[23] -eq 1 -and
    (Eq19 $_ 6 $guest) -and (Eq19 $_ 26 $leaseIp)
})
$leasedUdp = @($parsedUdp | Where-Object {
    (Eq19 $_.SourceMac 0 $guest) -and (Eq19 $_.SourceIp 0 $leaseIp) -and
    $_.SourcePort -eq 15180
})
Require19 ($leasedIcmp.Count -ge 2 -and $leasedUdp.Count -ge 2) `
           'Post-bind ICMP/UDP proof does not use the leased source IPv4.'

$staleStatic = @($frames | Where-Object {
    $_.Length -ge 34 -and $_[12] -eq 8 -and $_[13] -eq 0 -and
    (Eq19 $_ 6 $guest) -and (Eq19 $_ 26 $staticIp)
})
Require19 ($staleStatic.Count -eq 0) 'A post-bind guest frame retained the static IPv4 identity.'

Write-Output ('MANAGED_E1000_PHASE19_PCAP=PASS packets={0} dhcp={1} malformed_dhcp={2} arp={3} leased_icmp={4} leased_udp={5}' -f `
    $frames.Count, $dhcp.Count, $malformedDhcpCount, $arpFrames.Count,
    $leasedIcmp.Count, $leasedUdp.Count)
