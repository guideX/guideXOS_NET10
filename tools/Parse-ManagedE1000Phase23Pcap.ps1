[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$PcapPath,
    [Parameter(Mandatory = $true)] [string]$GuestMac
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Require23([bool]$condition, [string]$message) { if (!$condition) { throw $message } }
function U16-23([byte[]]$b, [int]$o) { return (([int]$b[$o] -shl 8) -bor [int]$b[$o + 1]) }
function U16LE-23([byte[]]$b, [int]$o) { return ([int]$b[$o] -bor ([int]$b[$o + 1] -shl 8)) }
function U32LE-23([byte[]]$b, [int]$o) {
    return [uint32](([int]$b[$o]) -bor ([int]$b[$o + 1] -shl 8) -bor
        ([int]$b[$o + 2] -shl 16) -bor ([int]$b[$o + 3] -shl 24))
}
function U32-23([byte[]]$b, [int]$o) {
    return [uint32]($b[$o] * 16777216 + $b[$o + 1] * 65536 + $b[$o + 2] * 256 + $b[$o + 3])
}
function Eq23([byte[]]$a, [int]$o, [byte[]]$b) {
    if ($null -eq $a -or $null -eq $b -or $o -lt 0 -or $o + $b.Length -gt $a.Length) { return $false }
    for ($i = 0; $i -lt $b.Length; ++$i) { if ($a[$o + $i] -ne $b[$i]) { return $false } }
    return $true
}
function Sum23([byte[]]$b, [int]$o, [int]$length) {
    [uint32]$sum = 0
    for ($i = 0; $i + 1 -lt $length; $i += 2) {
        $sum += (([int]$b[$o + $i] -shl 8) -bor [int]$b[$o + $i + 1])
        $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    }
    if (($length -band 1) -ne 0) { $sum += [int]$b[$o + $length - 1] -shl 8 }
    $sum = ($sum -band 0xFFFF) + ($sum -shr 16); $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    return [uint16]((-bnot [int]$sum) -band 0xFFFF)
}
function TcpSum23([byte[]]$source, [byte[]]$destination, [byte[]]$tcp) {
    [uint32]$sum = 0
    foreach ($o in @(0, 2)) {
        $sum += (([int]$source[$o] -shl 8) -bor $source[$o + 1]); $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
        $sum += (([int]$destination[$o] -shl 8) -bor $destination[$o + 1]); $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    }
    $sum += 6; $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    $sum += $tcp.Length; $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    for ($i = 0; $i + 1 -lt $tcp.Length; $i += 2) {
        $sum += (([int]$tcp[$i] -shl 8) -bor $tcp[$i + 1]); $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    }
    if (($tcp.Length -band 1) -ne 0) { $sum += [int]$tcp[$tcp.Length - 1] -shl 8 }
    $sum = ($sum -band 0xFFFF) + ($sum -shr 16); $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    return [uint16]((-bnot [int]$sum) -band 0xFFFF)
}
function UdpSum23([byte[]]$source, [byte[]]$destination, [byte[]]$udp) {
    [uint32]$sum = 0
    foreach ($o in @(0, 2)) {
        $sum += (([int]$source[$o] -shl 8) -bor $source[$o + 1]); $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
        $sum += (([int]$destination[$o] -shl 8) -bor $destination[$o + 1]); $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    }
    $sum += 17; $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    $sum += $udp.Length; $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    for ($i = 0; $i + 1 -lt $udp.Length; $i += 2) {
        $sum += (([int]$udp[$i] -shl 8) -bor $udp[$i + 1]); $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    }
    if (($udp.Length -band 1) -ne 0) { $sum += [int]$udp[$udp.Length - 1] -shl 8 }
    $sum = ($sum -band 0xFFFF) + ($sum -shr 16); $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    return [uint16]((-bnot [int]$sum) -band 0xFFFF)
}
function GetOption23([byte[]]$payload, [int]$wanted) {
    if ($payload.Length -lt 241 -or (U32-23 $payload 236) -ne 0x63825363) { return $null }
    $o = 240
    while ($o -lt $payload.Length) {
        $code = $payload[$o++]
        if ($code -eq 0) { continue }
        if ($code -eq 255 -or $o -ge $payload.Length) { return $null }
        $length = [int]$payload[$o++]
        if ($o + $length -gt $payload.Length) { return $null }
        if ($code -eq $wanted) { return [byte[]]$payload[$o..($o + $length - 1)] }
        $o += $length
    }
    return $null
}
function ParseIp23([byte[]]$frame) {
    if ($frame.Length -lt 34 -or $frame[12] -ne 8 -or $frame[13] -ne 0 -or ($frame[14] -shr 4) -ne 4) { return $null }
    $ihl = ($frame[14] -band 0x0F) * 4; $total = U16-23 $frame 16
    if ($ihl -lt 20 -or $total -lt $ihl -or 14 + $total -gt $frame.Length -or (Sum23 $frame 14 $ihl) -ne 0) { return $null }
    [pscustomobject]@{ Frame = $frame; HeaderLength = $ihl; TotalLength = $total; Protocol = [int]$frame[23]; SourceIp = [byte[]]$frame[26..29]; DestinationIp = [byte[]]$frame[30..33] }
}
function ParseUdp23([pscustomobject]$ip) {
    if ($null -eq $ip -or $ip.Protocol -ne 17 -or $ip.TotalLength -lt $ip.HeaderLength + 8) { return $null }
    $offset = 14 + $ip.HeaderLength; $length = U16-23 $ip.Frame ($offset + 4)
    if ($length -lt 8 -or $length -ne $ip.TotalLength - $ip.HeaderLength -or $offset + $length -gt $ip.Frame.Length) { return $null }
    $udp = [byte[]]$ip.Frame[$offset..($offset + $length - 1)]
    Require23 ((UdpSum23 $ip.SourceIp $ip.DestinationIp $udp) -eq 0) 'UDP checksum or pseudo-header is invalid.'
    [pscustomobject]@{ SourcePort = U16-23 $udp 0; DestinationPort = U16-23 $udp 2; Payload = if ($length -gt 8) { [byte[]]$udp[8..($length - 1)] } else { [byte[]]@() } }
}
function ParseTcp23([pscustomobject]$ip) {
    if ($null -eq $ip -or $ip.Protocol -ne 6) { return $null }
    if ($ip.TotalLength -lt $ip.HeaderLength + 20) { return [pscustomobject]@{ IsTcp = $true; Valid = $false } }
    $offset = 14 + $ip.HeaderLength; $length = $ip.TotalLength - $ip.HeaderLength
    $tcp = [byte[]]$ip.Frame[$offset..($offset + $length - 1)]
    $header = ($tcp[12] -shr 4) * 4
    if ($header -lt 20 -or $header -gt 60 -or $header -gt $tcp.Length -or ($tcp[12] -band 0x0F) -ne 0 -or ($tcp[13] -band 0xE0) -ne 0) { return [pscustomobject]@{ IsTcp = $true; Valid = $false } }
    $valid = (TcpSum23 $ip.SourceIp $ip.DestinationIp $tcp) -eq 0
    [pscustomobject]@{ IsTcp = $true; Valid = $valid; SourceIp = $ip.SourceIp; DestinationIp = $ip.DestinationIp; SourcePort = U16-23 $tcp 0; DestinationPort = U16-23 $tcp 2; Sequence = U32-23 $tcp 4; Acknowledgment = U32-23 $tcp 8; Flags = [int]$tcp[13]; Payload = if ($length -gt $header) { [byte[]]$tcp[$header..($tcp.Length - 1)] } else { [byte[]]@() }; Mss = if ($header -eq 24 -and $tcp[20] -eq 2 -and $tcp[21] -eq 4) { U16-23 $tcp 22 } else { 0 } }
}
function ParseMac23([string]$text) {
    Require23 ($text -match '^[0-9A-Fa-f]{12}$') 'Guest MAC is invalid.'
    $mac = New-Object byte[] 6
    for ($i = 0; $i -lt 6; ++$i) { $mac[$i] = [Convert]::ToByte($text.Substring($i * 2, 2), 16) }
    return $mac
}

$bytes = [IO.File]::ReadAllBytes([IO.Path]::GetFullPath($PcapPath))
Require23 ($bytes.Length -ge 24 -and $bytes[0] -eq 0xD4 -and $bytes[1] -eq 0xC3 -and $bytes[2] -eq 0xB2 -and $bytes[3] -eq 0xA1) 'PCAP header is invalid.'
Require23 ((U16LE-23 $bytes 4) -eq 2 -and (U16LE-23 $bytes 6) -eq 4 -and (U32LE-23 $bytes 20) -eq 1) 'PCAP link type is invalid.'
$frames = @(); $offset = 24
while ($offset -lt $bytes.Length) {
    Require23 ($offset + 16 -le $bytes.Length) 'PCAP record header is truncated.'
    $included = [int](U32LE-23 $bytes ($offset + 8)); $original = [int](U32LE-23 $bytes ($offset + 12))
    Require23 ($included -ge 60 -and $included -le $original -and $offset + 16 + $included -le $bytes.Length) 'PCAP record length is invalid.'
    $frames += ,([byte[]]$bytes[($offset + 16)..($offset + 15 + $included)]); $offset += 16 + $included
}
Require23 ($frames.Count -gt 0) 'PCAP contains no frames.'
$guest = ParseMac23 $GuestMac; $guestIp = [byte[]](10,15,0,42); $hostIp = [byte[]](10,15,0,2)
$ips = @($frames | ForEach-Object { ParseIp23 $_ } | Where-Object { $null -ne $_ })
$udp = @($ips | ForEach-Object { ParseUdp23 $_ } | Where-Object { $null -ne $_ })
$dhcp = @($udp | Where-Object { ($_.SourcePort -eq 67 -or $_.SourcePort -eq 68) -and $null -ne (GetOption23 $_.Payload 53) })
Require23 ($dhcp.Count -ge 4) 'DHCP DORA is incomplete.'
$dnsQuestion = [byte[]](7,112,104,97,115,101,50,51,4,116,101,115,116,0,0,1,0,1)
$dnsQueries = @($udp | Where-Object { $_.SourcePort -eq 15200 -and $_.DestinationPort -eq 53 -and $_.Payload.Length -eq 30 -and (Eq23 $_.Payload 12 $dnsQuestion) })
$dnsResponses = @($udp | Where-Object { $_.SourcePort -eq 53 -and $_.DestinationPort -eq 15200 -and $_.Payload.Length -eq 46 -and (Eq23 $_.Payload 12 $dnsQuestion) -and (Eq23 $_.Payload 42 $hostIp) })
Require23 ($dnsQueries.Count -ge 1 -and $dnsResponses.Count -ge 1) 'Phase 23 DNS resolution proof is incomplete.'
$tcpRecords = @($ips | ForEach-Object { ParseTcp23 $_ } | Where-Object { $null -ne $_ -and $_.IsTcp })
$validTcp = @($tcpRecords | Where-Object { $_.Valid -and ((Eq23 $_.SourceIp 0 $guestIp -and Eq23 $_.DestinationIp 0 $hostIp) -or (Eq23 $_.SourceIp 0 $hostIp -and Eq23 $_.DestinationIp 0 $guestIp)) })
$clientPort = 15221; $serverPort = 15222; [uint32]$clientIsn = 0x22000001; [uint32]$serverIsn = 0x23010001
$clientNext = [uint32]($clientIsn + 1); $serverNext = [uint32]($serverIsn + 1)
$request = [Text.Encoding]::ASCII.GetBytes("GET /phase23 HTTP/1.1`r`nHost: phase23.test`r`nConnection: close`r`n`r`n")
$part1 = [Text.Encoding]::ASCII.GetBytes('HTTP/1.1 200')
$part2 = [Text.Encoding]::ASCII.GetBytes(" OK`r`nContent-Length: 17`r`nConnection: close`r`nContent-Type: text/plain`r`n`r`nphase23-")
$part3 = [Text.Encoding]::ASCII.GetBytes('http-pass')
$requestNext = [uint32]($clientNext + $request.Length); $serverPart2 = [uint32]($serverNext + $part1.Length); $serverPart3 = [uint32]($serverPart2 + $part2.Length); $serverFin = [uint32]($serverPart3 + $part3.Length); $serverFinNext = [uint32]($serverFin + 1); $clientFin = [uint32]($requestNext + 1)
$expected = @(
    [pscustomobject]@{ C = $true; Seq = $clientIsn; Ack = 0; Flags = 2; Payload = [byte[]]@(); Mss = 512 },
    [pscustomobject]@{ C = $false; Seq = $serverIsn; Ack = $clientNext; Flags = 0x12; Payload = [byte[]]@(); Mss = 512 },
    [pscustomobject]@{ C = $true; Seq = $clientNext; Ack = $serverNext; Flags = 0x10; Payload = [byte[]]@(); Mss = 0 },
    [pscustomobject]@{ C = $true; Seq = $clientNext; Ack = $serverNext; Flags = 0x18; Payload = $request; Mss = 0 },
    [pscustomobject]@{ C = $false; Seq = $serverNext; Ack = $requestNext; Flags = 0x10; Payload = [byte[]]@(); Mss = 0 },
    [pscustomobject]@{ C = $false; Seq = $serverNext; Ack = $requestNext; Flags = 0x18; Payload = $part1; Mss = 0 },
    [pscustomobject]@{ C = $true; Seq = $requestNext; Ack = $serverPart2; Flags = 0x10; Payload = [byte[]]@(); Mss = 0 },
    [pscustomobject]@{ C = $false; Seq = $serverPart2; Ack = $requestNext; Flags = 0x18; Payload = $part2; Mss = 0 },
    [pscustomobject]@{ C = $true; Seq = $requestNext; Ack = $serverPart3; Flags = 0x10; Payload = [byte[]]@(); Mss = 0 },
    [pscustomobject]@{ C = $false; Seq = $serverPart3; Ack = $requestNext; Flags = 0x18; Payload = $part3; Mss = 0 },
    [pscustomobject]@{ C = $true; Seq = $requestNext; Ack = $serverFin; Flags = 0x10; Payload = [byte[]]@(); Mss = 0 },
    [pscustomobject]@{ C = $false; Seq = $serverFin; Ack = $requestNext; Flags = 0x11; Payload = [byte[]]@(); Mss = 0 },
    [pscustomobject]@{ C = $true; Seq = $requestNext; Ack = $serverFinNext; Flags = 0x10; Payload = [byte[]]@(); Mss = 0 },
    [pscustomobject]@{ C = $true; Seq = $requestNext; Ack = $serverFinNext; Flags = 0x11; Payload = [byte[]]@(); Mss = 0 },
    [pscustomobject]@{ C = $false; Seq = $serverFinNext; Ack = $clientFin; Flags = 0x10; Payload = [byte[]]@(); Mss = 0 })
function Match23([pscustomobject]$record, [pscustomobject]$want) {
    $clientDirection = Eq23 $record.SourceIp 0 $guestIp -and Eq23 $record.DestinationIp 0 $hostIp
    $sourcePort = if ($clientDirection) { $clientPort } else { $serverPort }; $destinationPort = if ($clientDirection) { $serverPort } else { $clientPort }
    $recordPayloadLength = if ($null -eq $record.Payload) { 0 } else { @($record.Payload).Count }
    $wantedPayloadLength = if ($null -eq $want.Payload) { 0 } else { @($want.Payload).Count }
    $payloadMatches = if ($wantedPayloadLength -eq 0) {
        $recordPayloadLength -eq 0
    } else {
        $recordPayload = [byte[]]$record.Payload
        $wantedPayload = [byte[]]$want.Payload
        $recordPayloadLength -eq $wantedPayloadLength -and (Eq23 $recordPayload 0 $wantedPayload)
    }
    return ($clientDirection -eq $want.C -and $record.SourcePort -eq $sourcePort -and $record.DestinationPort -eq $destinationPort -and $record.Sequence -eq $want.Seq -and $record.Acknowledgment -eq $want.Ack -and $record.Flags -eq $want.Flags -and $record.Mss -eq $want.Mss -and $payloadMatches)
}
$flow = 0
foreach ($record in $validTcp) { if ($flow -lt $expected.Count -and (Match23 $record $expected[$flow])) { $flow++ } }
Require23 ($flow -eq $expected.Count) "HTTP TCP flow is incomplete: matched $flow of $($expected.Count)."
Require23 ($validTcp.Count -eq $expected.Count) "Unexpected valid TCP controls or retransmissions: $($validTcp.Count - $expected.Count)."
Require23 ((@($tcpRecords | Where-Object { !$_.Valid })).Count -ge 0) 'TCP parser result is invalid.'
Require23 ((@($frames | Where-Object { $_.Length -ge 34 -and $_[12] -eq 8 -and $_[13] -eq 0 -and (Eq23 $_ 26 ([byte[]](10,15,0,1))) })).Count -eq 0) 'Static pre-DHCP IPv4 identity leaked onto the wire.'
Write-Output ('MANAGED_E1000_PHASE23_PCAP=PASS packets={0} dhcp={1} dns_queries={2} dns_responses={3} tcp_valid={4} tcp_flow={5} tcp_malformed={6} response_segments=3 request_bytes={7} response_body=phase23-http-pass' -f $frames.Count, $dhcp.Count, $dnsQueries.Count, $dnsResponses.Count, $validTcp.Count, $flow, (@($tcpRecords | Where-Object { !$_.Valid })).Count, $request.Length)
