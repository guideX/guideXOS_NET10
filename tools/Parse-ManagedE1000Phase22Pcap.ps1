[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$PcapPath,
    [Parameter(Mandatory = $true)] [string]$GuestMac
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Require22([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}
function U16-22([byte[]]$b, [int]$o) {
    return (([int]$b[$o] -shl 8) -bor [int]$b[$o + 1])
}
function U16LE-22([byte[]]$b, [int]$o) {
    return ([int]$b[$o] -bor ([int]$b[$o + 1] -shl 8))
}
function U32LE-22([byte[]]$b, [int]$o) {
    return [uint32](([int]$b[$o]) -bor ([int]$b[$o + 1] -shl 8) -bor
        ([int]$b[$o + 2] -shl 16) -bor ([int]$b[$o + 3] -shl 24))
}
function U32-22([byte[]]$b, [int]$o) {
    return [uint32]($b[$o] * 16777216 + $b[$o + 1] * 65536 +
        $b[$o + 2] * 256 + $b[$o + 3])
}
function Eq22([byte[]]$a, [int]$o, [byte[]]$b) {
    $aLength = if ($null -eq $a) { 0 } else { $a.Length }
    $bLength = if ($null -eq $b) { 0 } else { $b.Length }
    if ($o -lt 0 -or $o + $bLength -gt $aLength) { return $false }
    for ($i = 0; $i -lt $bLength; ++$i) {
        if ($a[$o + $i] -ne $b[$i]) { return $false }
    }
    return $true
}
function Sum22([byte[]]$b, [int]$o, [int]$length) {
    [uint32]$sum = 0
    for ($i = 0; $i + 1 -lt $length; $i += 2) {
        $sum += (([int]$b[$o + $i] -shl 8) -bor [int]$b[$o + $i + 1])
        $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    }
    if (($length -band 1) -ne 0) {
        $sum += [int]$b[$o + $length - 1] -shl 8
    }
    $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    return [uint16]((-bnot [int]$sum) -band 0xFFFF)
}
function TcpSum22([byte[]]$source, [byte[]]$destination, [byte[]]$tcp) {
    [uint32]$sum = 0
    foreach ($o in @(0, 2)) {
        $sum += (([int]$source[$o] -shl 8) -bor [int]$source[$o + 1])
        $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
        $sum += (([int]$destination[$o] -shl 8) -bor [int]$destination[$o + 1])
        $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    }
    $sum += 6; $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    $sum += $tcp.Length; $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    for ($i = 0; $i + 1 -lt $tcp.Length; $i += 2) {
        $sum += (([int]$tcp[$i] -shl 8) -bor [int]$tcp[$i + 1])
        $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    }
    if (($tcp.Length -band 1) -ne 0) { $sum += [int]$tcp[$tcp.Length - 1] -shl 8 }
    $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    return [uint16]((-bnot [int]$sum) -band 0xFFFF)
}
function ParseMac22([string]$text) {
    Require22 ($text -match '^[0-9A-Fa-f]{12}$') 'Guest MAC is invalid.'
    $mac = New-Object byte[] 6
    for ($i = 0; $i -lt 6; ++$i) {
        $mac[$i] = [Convert]::ToByte($text.Substring($i * 2, 2), 16)
    }
    return $mac
}
function GetOption22([byte[]]$payload, [int]$wanted) {
    if ($payload.Length -lt 241 -or (U32-22 $payload 236) -ne 0x63825363) { return $null }
    $o = 240
    while ($o -lt $payload.Length) {
        $code = $payload[$o++]
        if ($code -eq 0) { continue }
        if ($code -eq 255 -or $o -ge $payload.Length) { return $null }
        $length = [int]$payload[$o++]
        if ($o + $length -gt $payload.Length) { return $null }
        if ($code -eq $wanted) {
            if ($length -eq 0) { return [byte[]]@() }
            return [byte[]]$payload[$o..($o + $length - 1)]
        }
        $o += $length
    }
    return $null
}
function ParseIp22([byte[]]$frame) {
    if ($frame.Length -lt 34 -or $frame[12] -ne 8 -or $frame[13] -ne 0 -or
        ($frame[14] -shr 4) -ne 4) { return $null }
    $ihl = ($frame[14] -band 0x0F) * 4
    $total = U16-22 $frame 16
    if ($ihl -lt 20 -or $total -lt $ihl -or 14 + $total -gt $frame.Length -or
        (Sum22 $frame 14 $ihl) -ne 0) { return $null }
    [pscustomobject]@{
        Frame = $frame
        HeaderLength = $ihl
        TotalLength = $total
        Protocol = [int]$frame[23]
        SourceIp = [byte[]]$frame[26..29]
        DestinationIp = [byte[]]$frame[30..33]
    }
}
function ParseUdp22([pscustomobject]$ip) {
    if ($null -eq $ip -or $ip.Protocol -ne 17 -or $ip.TotalLength -lt $ip.HeaderLength + 8) { return $null }
    $udp = 14 + $ip.HeaderLength
    $length = U16-22 $ip.Frame ($udp + 4)
    if ($length -lt 8 -or $length -ne $ip.TotalLength - $ip.HeaderLength -or
        $udp + $length -gt $ip.Frame.Length) { return $null }
    $bytes = [byte[]]$ip.Frame[$udp..($udp + $length - 1)]
    if ((U16-22 $bytes 6) -ne 0) {
        # UDP checksum is mandatory for the deterministic peer traffic.
        $checksumBytes = [byte[]]$bytes.Clone(); $checksumBytes[6] = 0; $checksumBytes[7] = 0
        # Calculate the UDP pseudo-header checksum independently.
        [uint32]$sum = 0
        foreach ($o in @(0, 2)) {
            $sum += (([int]$ip.SourceIp[$o] -shl 8) -bor [int]$ip.SourceIp[$o + 1])
            $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
            $sum += (([int]$ip.DestinationIp[$o] -shl 8) -bor [int]$ip.DestinationIp[$o + 1])
            $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
        }
        $sum += 17; $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
        $sum += $bytes.Length; $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
        for ($i = 0; $i + 1 -lt $checksumBytes.Length; $i += 2) {
            $sum += (([int]$checksumBytes[$i] -shl 8) -bor [int]$checksumBytes[$i + 1])
            $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
        }
        if (($checksumBytes.Length -band 1) -ne 0) { $sum += [int]$checksumBytes[$checksumBytes.Length - 1] -shl 8 }
        $sum = ($sum -band 0xFFFF) + ($sum -shr 16); $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
        if (([uint16]((-bnot [int]$sum) -band 0xFFFF)) -ne (U16-22 $bytes 6)) { return $null }
    }
    [pscustomobject]@{
        SourcePort = U16-22 $bytes 0
        DestinationPort = U16-22 $bytes 2
        Payload = if ($length -gt 8) { [byte[]]$bytes[8..($length - 1)] } else { [byte[]]@() }
    }
}
function ParseTcp22([pscustomobject]$ip) {
    if ($null -eq $ip) {
        return [pscustomobject]@{ IsTcp = $false; Valid = $false }
    }
    if ($null -eq $ip -or $ip.Protocol -ne 6 -or $ip.TotalLength -lt $ip.HeaderLength + 20) {
        return [pscustomobject]@{ IsTcp = $true; Valid = $false; SourceIp = $ip.SourceIp; DestinationIp = $ip.DestinationIp }
    }
    $tcpOffset = 14 + $ip.HeaderLength
    $tcpLength = $ip.TotalLength - $ip.HeaderLength
    $tcp = [byte[]]$ip.Frame[$tcpOffset..($tcpOffset + $tcpLength - 1)]
    $headerLength = ($tcp[12] -shr 4) * 4
    if ($headerLength -lt 20 -or $headerLength -gt 60 -or $headerLength -gt $tcp.Length -or
        ($tcp[12] -band 0x0F) -ne 0 -or ($tcp[13] -band 0xE0) -ne 0) {
        return [pscustomobject]@{ IsTcp = $true; Valid = $false; SourceIp = $ip.SourceIp; DestinationIp = $ip.DestinationIp }
    }
    $mss = 0; $mssCount = 0; $o = 20
    while ($o -lt $headerLength) {
        $kind = $tcp[$o]
        if ($kind -eq 0) { break }
        if ($kind -eq 1) { $o += 1; continue }
        if ($o + 1 -ge $headerLength) {
            return [pscustomobject]@{ IsTcp = $true; Valid = $false; SourceIp = $ip.SourceIp; DestinationIp = $ip.DestinationIp }
        }
        $length = [int]$tcp[$o + 1]
        if ($length -lt 2 -or $o + $length -gt $headerLength) {
            return [pscustomobject]@{ IsTcp = $true; Valid = $false; SourceIp = $ip.SourceIp; DestinationIp = $ip.DestinationIp }
        }
        if ($kind -eq 2 -and $length -eq 4) { $mss = U16-22 $tcp ($o + 2); $mssCount += 1 }
        $o += $length
    }
    $checksumValid = (TcpSum22 $ip.SourceIp $ip.DestinationIp $tcp) -eq 0
    [pscustomobject]@{
        IsTcp = $true; Valid = $checksumValid
        SourceIp = $ip.SourceIp; DestinationIp = $ip.DestinationIp
        SourcePort = U16-22 $tcp 0; DestinationPort = U16-22 $tcp 2
        Sequence = U32-22 $tcp 4; Acknowledgment = U32-22 $tcp 8
        Flags = [int]$tcp[13]; Payload = if ($tcpLength -gt $headerLength) { [byte[]]$tcp[$headerLength..($tcp.Length - 1)] } else { [byte[]]@() }
        Mss = $mss; MssCount = $mssCount; HeaderLength = $headerLength
    }
}

$bytes = [IO.File]::ReadAllBytes([IO.Path]::GetFullPath($PcapPath))
Require22 ($bytes.Length -ge 24 -and $bytes[0] -eq 0xD4 -and $bytes[1] -eq 0xC3 -and
    $bytes[2] -eq 0xB2 -and $bytes[3] -eq 0xA1) 'PCAP header is invalid.'
Require22 ((U16LE-22 $bytes 4) -eq 2 -and (U16LE-22 $bytes 6) -eq 4 -and
    (U32LE-22 $bytes 20) -eq 1) 'PCAP link type is invalid.'
$frames = @(); $offset = 24
while ($offset -lt $bytes.Length) {
    Require22 ($offset + 16 -le $bytes.Length) 'PCAP record header is truncated.'
    $included = [int](U32LE-22 $bytes ($offset + 8)); $original = [int](U32LE-22 $bytes ($offset + 12))
    Require22 ($included -ge 60 -and $included -le $original -and
        $offset + 16 + $included -le $bytes.Length) 'PCAP record length is invalid.'
    $frames += ,([byte[]]$bytes[($offset + 16)..($offset + 15 + $included)])
    $offset += 16 + $included
}
Require22 ($frames.Count -gt 0) 'PCAP contains no frames.'

$guest = ParseMac22 $GuestMac
$hostIp = [byte[]](10,15,0,2); $leaseIp = [byte[]](10,15,0,42)
$staticIp = [byte[]](10,15,0,1)
$parsedIp = @($frames | ForEach-Object { ParseIp22 $_ })
foreach ($ip in $parsedIp) {
    if ($null -ne $ip) {
        Require22 ((Sum22 $ip.Frame 14 $ip.HeaderLength) -eq 0) 'IPv4 checksum is invalid.'
    }
}
$udp = @($parsedIp | ForEach-Object { ParseUdp22 $_ } | Where-Object { $null -ne $_ })
$dhcp = @($udp | Where-Object { ($_.SourcePort -eq 67 -or $_.SourcePort -eq 68) -and
    (GetOption22 $_.Payload 53) -ne $null })
Require22 ($dhcp.Count -ge 4) 'DHCP DORA is incomplete.'
$discover = @($dhcp | Where-Object { $_.SourcePort -eq 68 -and $_.DestinationPort -eq 67 -and
    (GetOption22 $_.Payload 53)[0] -eq 1 } | Select-Object -First 1)
Require22 ($discover.Count -eq 1) 'DHCPDISCOVER is missing.'
$xid = U32-22 $discover[0].Payload 4
$offer = @($dhcp | Where-Object { $_.SourcePort -eq 67 -and $_.DestinationPort -eq 68 -and
    (GetOption22 $_.Payload 53)[0] -eq 2 -and (U32-22 $_.Payload 4) -eq $xid -and
    (Eq22 $_.Payload 16 $leaseIp) } | Select-Object -First 1)
$request = @($dhcp | Where-Object { $_.SourcePort -eq 68 -and $_.DestinationPort -eq 67 -and
    (GetOption22 $_.Payload 53)[0] -eq 3 -and (U32-22 $_.Payload 4) -eq $xid } | Select-Object -First 1)
$ack = @($dhcp | Where-Object { $_.SourcePort -eq 67 -and $_.DestinationPort -eq 68 -and
    (GetOption22 $_.Payload 53)[0] -eq 5 -and (U32-22 $_.Payload 4) -eq $xid -and
    (Eq22 $_.Payload 16 $leaseIp) } | Select-Object -First 1)
Require22 ($offer.Count -eq 1 -and $request.Count -eq 1 -and $ack.Count -eq 1) 'DHCP DORA xid/order is invalid.'
Require22 ((GetOption22 $offer[0].Payload 6).Length -eq 4 -and
    (Eq22 (GetOption22 $offer[0].Payload 6) 0 $hostIp) -and
    (GetOption22 $ack[0].Payload 6).Length -eq 4 -and
    (Eq22 (GetOption22 $ack[0].Payload 6) 0 $hostIp)) 'DHCP DNS option is invalid.'

$question = [byte[]](7,112,104,97,115,101,50,50,4,116,101,115,116,0,0,1,0,1)
$dnsQueries = @($udp | Where-Object { $_.SourcePort -eq 15200 -and $_.DestinationPort -eq 53 -and
    $_.Payload.Length -eq 30 -and (Eq22 $_.Payload 12 $question) })
$dnsResponses = @($udp | Where-Object { $_.SourcePort -eq 53 -and $_.DestinationPort -eq 15200 -and
    $_.Payload.Length -eq 46 -and (Eq22 $_.Payload 12 $question) -and
    (Eq22 $_.Payload 42 $hostIp) })
Require22 ($dnsQueries.Count -ge 1 -and $dnsResponses.Count -ge 1) 'Phase 22 DNS proof is incomplete.'

$tcpRecords = @($parsedIp | ForEach-Object { ParseTcp22 $_ } | Where-Object { $_.IsTcp })
$validTcp = @($tcpRecords | Where-Object { $_.Valid })
$clientIp = $leaseIp; $clientPort = 15221; $serverPort = 15222
[uint32]$clientIsn = 0x22000001; [uint32]$serverIsn = 0x22010001
$clientNext = [uint32]($clientIsn + 1); $serverNext = [uint32]($serverIsn + 1)
$firstRequest = [Text.Encoding]::ASCII.GetBytes('PHASE22-MANAGED-HELLO')
$firstReply = [Text.Encoding]::ASCII.GetBytes('PHASE22-PEER-ACK')
$secondRequest = [Text.Encoding]::ASCII.GetBytes('PHASE22-POSTGC-HELLO')
$secondReply = [Text.Encoding]::ASCII.GetBytes('PHASE22-POSTGC-ACK')
$firstRequestNext = [uint32]($clientNext + $firstRequest.Length)
$firstPeerNext = [uint32]($serverNext + $firstReply.Length)
$secondRequestNext = [uint32]($firstRequestNext + $secondRequest.Length)
$secondPeerNext = [uint32]($firstPeerNext + $secondReply.Length)
$expected = @(
    [pscustomobject]@{ C = $true; Seq = $clientIsn; Ack = 0; Flags = 2; Payload = [byte[]]@(); Mss = 512 },
    [pscustomobject]@{ C = $false; Seq = $serverIsn; Ack = $clientNext; Flags = 0x12; Payload = [byte[]]@(); Mss = 512 },
    [pscustomobject]@{ C = $true; Seq = $clientNext; Ack = $serverNext; Flags = 0x10; Payload = [byte[]]@(); Mss = 0 },
    [pscustomobject]@{ C = $true; Seq = $clientNext; Ack = $serverNext; Flags = 0x18; Payload = $firstRequest; Mss = 0 },
    [pscustomobject]@{ C = $false; Seq = $serverNext; Ack = $firstRequestNext; Flags = 0x10; Payload = [byte[]]@(); Mss = 0 },
    [pscustomobject]@{ C = $false; Seq = $serverNext; Ack = $firstRequestNext; Flags = 0x18; Payload = $firstReply; Mss = 0 },
    [pscustomobject]@{ C = $true; Seq = $firstRequestNext; Ack = $firstPeerNext; Flags = 0x10; Payload = [byte[]]@(); Mss = 0 },
    [pscustomobject]@{ C = $true; Seq = $firstRequestNext; Ack = $firstPeerNext; Flags = 0x18; Payload = $secondRequest; Mss = 0 },
    [pscustomobject]@{ C = $false; Seq = $firstPeerNext; Ack = $secondRequestNext; Flags = 0x10; Payload = [byte[]]@(); Mss = 0 },
    [pscustomobject]@{ C = $false; Seq = $firstPeerNext; Ack = $secondRequestNext; Flags = 0x18; Payload = $secondReply; Mss = 0 },
    [pscustomobject]@{ C = $true; Seq = $secondRequestNext; Ack = $secondPeerNext; Flags = 0x10; Payload = [byte[]]@(); Mss = 0 },
    [pscustomobject]@{ C = $true; Seq = $secondRequestNext; Ack = $secondPeerNext; Flags = 0x11; Payload = [byte[]]@(); Mss = 0 },
    [pscustomobject]@{ C = $false; Seq = $secondPeerNext; Ack = [uint32]($secondRequestNext + 1); Flags = 0x10; Payload = [byte[]]@(); Mss = 0 },
    [pscustomobject]@{ C = $false; Seq = $secondPeerNext; Ack = [uint32]($secondRequestNext + 1); Flags = 0x11; Payload = [byte[]]@(); Mss = 0 },
    [pscustomobject]@{ C = $true; Seq = [uint32]($secondRequestNext + 1); Ack = [uint32]($secondPeerNext + 1); Flags = 0x10; Payload = [byte[]]@(); Mss = 0 })

function Matches22([pscustomobject]$record, [pscustomobject]$want) {
    $clientDirection = (Eq22 $record.SourceIp 0 $clientIp) -and (Eq22 $record.DestinationIp 0 $hostIp)
    $directionOk = $clientDirection -eq $want.C
    $sourcePort = if ($clientDirection) { $clientPort } else { $serverPort }
    $destinationPort = if ($clientDirection) { $serverPort } else { $clientPort }
    $recordPayloadLength = if ($null -eq $record.Payload) { 0 } else { $record.Payload.Length }
    $wantPayloadLength = if ($null -eq $want.Payload) { 0 } else { $want.Payload.Length }
    return $directionOk -and $record.SourcePort -eq $sourcePort -and
        $record.DestinationPort -eq $destinationPort -and
        $record.Sequence -eq $want.Seq -and $record.Acknowledgment -eq $want.Ack -and
        $record.Flags -eq $want.Flags -and $recordPayloadLength -eq $wantPayloadLength -and
        (Eq22 $record.Payload 0 $want.Payload) -and
        $record.Mss -eq $want.Mss
}

$flowIndex = 0; $flowMatches = 0; $controlCount = 0
foreach ($record in $validTcp) {
    Require22 ((Eq22 $record.SourceIp 0 $clientIp -and Eq22 $record.DestinationIp 0 $hostIp) -or
        (Eq22 $record.SourceIp 0 $hostIp -and Eq22 $record.DestinationIp 0 $clientIp)) `
        'TCP packet used an unexpected IPv4 tuple.'
    $matched = $false
    if ($flowIndex -lt $expected.Count -and (Matches22 $record $expected[$flowIndex])) {
        $flowIndex++; $flowMatches++; $matched = $true
    }
    if (!$matched) { $controlCount++ }
}
Require22 ($flowIndex -eq $expected.Count) "TCP flow is incomplete: matched $flowIndex of $($expected.Count)."
$invalidTcp = @($tcpRecords | Where-Object { !$_.Valid }).Count
$malformedControls = $controlCount + $invalidTcp
Require22 ($malformedControls -ge 8) "TCP malformed-control proof is incomplete: $malformedControls."
Require22 ($validTcp.Count -ge $expected.Count) 'Valid TCP packet count is too small.'
Require22 (@($frames | Where-Object { $_.Length -ge 34 -and $_[12] -eq 8 -and $_[13] -eq 0 -and
    (Eq22 $_ 26 $staticIp) }).Count -eq 0) 'Static pre-DHCP IPv4 identity leaked onto the wire.'

Write-Output ('MANAGED_E1000_PHASE22_PCAP=PASS packets={0} dhcp={1} dns_queries={2} dns_responses={3} tcp_valid={4} tcp_flow={5} malformed_tcp_controls={6} ports={7}/{8} client_isn=0x{9:X8} server_isn=0x{10:X8} payloads=PHASE22-MANAGED-HELLO,PHASE22-POSTGC-HELLO replies=PHASE22-PEER-ACK,PHASE22-POSTGC-ACK' -f
    $frames.Count, $dhcp.Count, $dnsQueries.Count, $dnsResponses.Count,
    $validTcp.Count, $flowMatches, $malformedControls, $clientPort, $serverPort,
    $clientIsn, $serverIsn)
