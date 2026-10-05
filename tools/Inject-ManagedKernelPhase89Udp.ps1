[CmdletBinding()]
param(
    [int]$GuestPort = 0,
    [string]$GuestMac = '525400123456',
    [Net.Sockets.UdpClient]$PeerUdp,
    [switch]$ValidateOnly
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
    $pseudo = New-Object byte[] (12 + $udp.Length)
    [Array]::Copy($sourceIp, 0, $pseudo, 0, 4)
    [Array]::Copy($destinationIp, 0, $pseudo, 4, 4)
    $pseudo[9] = 17
    Write-U16 $pseudo 10 $udp.Length
    [Array]::Copy($udp, 0, $pseudo, 12, $udp.Length)
    $pseudo[18] = 0
    $pseudo[19] = 0
    $value = Checksum $pseudo 0 $pseudo.Length
    if ($value -eq 0) { return [uint16]0xFFFF }
    return $value
}

function Build-Frame([byte[]]$guestMacBytes, [int]$sequence) {
    [byte[]]$sourceMac = @(0x02, 0x15, 0, 0, 0, 1)
    [byte[]]$sourceIp = @(10, 15, 0, ($sequence + 1))
    [byte[]]$destinationIp = @(10, 15, 0, 1)
    [byte[]]$payload = @(0x89, $sequence, 0xA5, (0x50 + $sequence), 0xC3, 0x3C)
    $destinationPort = if ($sequence -le 3) { 15890 } else { 15891 }
    $udpLength = 8 + $payload.Length
    [byte[]]$udp = New-Object byte[] $udpLength
    Write-U16 $udp 0 (24000 + $sequence)
    Write-U16 $udp 2 $destinationPort
    Write-U16 $udp 4 $udpLength
    [Array]::Copy($payload, 0, $udp, 8, $payload.Length)
    Write-U16 $udp 6 (Udp-Checksum $sourceIp $destinationIp $udp)

    [byte[]]$frame = New-Object byte[] 60
    [Array]::Copy($guestMacBytes, 0, $frame, 0, 6)
    [Array]::Copy($sourceMac, 0, $frame, 6, 6)
    $frame[12] = 0x08; $frame[13] = 0x00
    $frame[14] = 0x45
    Write-U16 $frame 16 (20 + $udpLength)
    Write-U16 $frame 18 (0x8900 + $sequence)
    $frame[22] = 63
    $frame[23] = 17
    [Array]::Copy($sourceIp, 0, $frame, 26, 4)
    [Array]::Copy($destinationIp, 0, $frame, 30, 4)
    Write-U16 $frame 24 (Checksum $frame 14 20)
    [Array]::Copy($udp, 0, $frame, 34, $udp.Length)
    return ,$frame
}

function Validate-Frame([byte[]]$frame, [byte[]]$guestMacBytes,
                        [int]$sequence) {
    for ($index = 0; $index -lt 6; $index++) {
        if ($frame[$index] -ne $guestMacBytes[$index]) {
            throw "Phase 89 injector packet $sequence has the wrong destination MAC."
        }
    }
    $expectedDestinationPort = if ($sequence -le 3) { 15890 } else { 15891 }
    [byte[]]$udpForChecksum = $frame[34..47]
    $udpForChecksum[6] = 0; $udpForChecksum[7] = 0
    $expectedUdpChecksum = Udp-Checksum ([byte[]]$frame[26..29]) `
        ([byte[]]$frame[30..33]) $udpForChecksum
    if ($frame.Length -ne 60 -or
        $frame[12] -ne 8 -or $frame[13] -ne 0 -or
        $frame[14] -ne 0x45 -or (Read-U16 $frame 16) -ne 34 -or
        $frame[23] -ne 17 -or (Checksum $frame 14 20) -ne 0 -or
        $frame[26] -ne 10 -or $frame[27] -ne 15 -or $frame[28] -ne 0 -or
        $frame[29] -ne ($sequence + 1) -or
        $frame[30] -ne 10 -or $frame[31] -ne 15 -or $frame[32] -ne 0 -or
        $frame[33] -ne 1 -or
        (Read-U16 $frame 34) -ne (24000 + $sequence) -or
        (Read-U16 $frame 36) -ne $expectedDestinationPort -or
        (Read-U16 $frame 38) -ne 14 -or (Read-U16 $frame 40) -eq 0 -or
        (Read-U16 $frame 40) -ne $expectedUdpChecksum) {
        throw "Phase 89 injector packet $sequence failed independent byte validation."
    }
    if ($frame[42] -ne 0x89 -or $frame[43] -ne $sequence -or
        $frame[44] -ne 0xA5 -or $frame[45] -ne (0x50 + $sequence) -or
        $frame[46] -ne 0xC3 -or $frame[47] -ne 0x3C) {
        throw "Phase 89 injector packet $sequence has the wrong payload sentinel."
    }
}

if ($GuestMac -notmatch '^(?:[0-9A-Fa-f]{12})$') {
    throw 'GuestMac must be twelve hexadecimal digits without separators.'
}
if (-not $ValidateOnly -and ($GuestPort -lt 1 -or $GuestPort -gt 65535)) {
    throw 'GuestPort must be a valid QEMU dgram UDP port.'
}
if (-not $ValidateOnly -and $null -eq $PeerUdp) {
    throw 'PeerUdp must be the already-bound QEMU dgram peer endpoint.'
}
[byte[]]$guestMacBytes = for ($index = 0; $index -lt 12; $index += 2) {
    [Convert]::ToByte($GuestMac.Substring($index, 2), 16)
}

$frames = @()
for ($sequence = 1; $sequence -le 4; $sequence++) {
    [byte[]]$frame = Build-Frame $guestMacBytes $sequence
    Validate-Frame $frame $guestMacBytes $sequence
    $frames += ,$frame
}
if ($ValidateOnly) {
    Write-Output 'PHASE89_UDP_INJECTOR_VALIDATION=PASS packets=4 ethernet=0800 ipv4=45 protocol=17 ipv4_checksum=valid udp_checksum=nonzero-valid payload=89-sequence-A5-5x-C3-3C'
    return
}

for ($index = 0; $index -lt $frames.Count; $index++) {
    $sequence = $index + 1
    [byte[]]$frame = $frames[$index]
    $sent = $PeerUdp.Send($frame, $frame.Length, '127.0.0.1', $GuestPort)
    if ($sent -ne $frame.Length) { throw "Phase 89 frame $sequence send was short." }
    $hexHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($frame))
    $sourceIp = '10.15.0.{0}' -f ($sequence + 1)
    $destinationPort = if ($sequence -le 3) { 15890 } else { 15891 }
    $qemuSourcePort = ([Net.IPEndPoint]$PeerUdp.Client.LocalEndPoint).Port
    Write-Output ('PHASE89_UDP_FRAME_INJECTED=PASS sequence={0} length={1} destination_mac={2} source_mac=021500000001 ethertype=0800 source_ipv4={3} destination_ipv4=10.15.0.1 ipv4_total_length=34 ipv4_header_length=20 ipv4_protocol=17 ipv4_checksum=0x{4:X4} source_port={5} destination_port={6} udp_length=14 udp_checksum=0x{7:X4} checksum_valid=true payload=89{0:X2}A5{8:X2}C33C sha256={9} endpoint=127.0.0.1:{10} qemu_peer_source_port={11}' -f `
        $sequence, $frame.Length, $GuestMac.ToUpperInvariant(), $sourceIp,
        (Read-U16 $frame 24), (Read-U16 $frame 34), $destinationPort,
        (Read-U16 $frame 40), (0x50 + $sequence), $hexHash, $GuestPort,
        $qemuSourcePort)
    Start-Sleep -Milliseconds 40
}
Write-Output 'PHASE89_UDP_INJECTION_SUMMARY=PASS frames=4 bound=3 unbound=1 external_network=false'
