[CmdletBinding()]
param(
    [string]$Transport = 'dgram',
    [Parameter(Mandatory = $true)] [int]$Port,
    [Parameter(Mandatory = $true)] [int]$SourcePort,
    [Parameter(Mandatory = $true)] [string]$DestinationMac
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Require15Inject([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}

Require15Inject ($Port -ge 1 -and $Port -le 65535) 'Network port is out of range.'
Require15Inject ($Transport -eq 'dgram') 'Only the verified QEMU dgram injector is supported.'
Require15Inject ($SourcePort -ge 1 -and $SourcePort -le 65535) 'UDP source port is out of range.'
Require15Inject ($DestinationMac -match '^[0-9A-Fa-f]{12}$') `
    'Destination MAC must be exactly twelve hexadecimal characters.'

$destination = New-Object byte[] 6
for ($index = 0; $index -lt 6; $index++) {
    $destination[$index] = [Convert]::ToByte($DestinationMac.Substring($index * 2, 2), 16)
}
$source = [byte[]](0x02, 0x15, 0x00, 0x00, 0x00, 0x01)
$signature = [Text.Encoding]::ASCII.GetBytes('guideXOS ManagedKernel Phase15 RX')
$sequence = 0x15000001
$frame = New-Object byte[] 60

[Array]::Copy($destination, 0, $frame, 0, 6)
[Array]::Copy($source, 0, $frame, 6, 6)
$frame[12] = 0x88
$frame[13] = 0xB5
[Array]::Copy($signature, 0, $frame, 14, $signature.Length)
$sequenceOffset = 14 + $signature.Length
$frame[$sequenceOffset] = [byte](($sequence -shr 24) -band 0xFF)
$frame[$sequenceOffset + 1] = [byte](($sequence -shr 16) -band 0xFF)
$frame[$sequenceOffset + 2] = [byte](($sequence -shr 8) -band 0xFF)
$frame[$sequenceOffset + 3] = [byte]($sequence -band 0xFF)

$udp = [Net.Sockets.UdpClient]::new([Net.Sockets.AddressFamily]::InterNetwork)
try {
    $udp.Client.Bind([Net.IPEndPoint]::new([Net.IPAddress]::Loopback, $SourcePort))
    $sent = $udp.Send($frame, $frame.Length, '127.0.0.1', $Port)
    Require15Inject ($sent -eq $frame.Length) 'UDP injector sent a short Ethernet datagram.'
}
finally {
    $udp.Dispose()
}

$hash = [Security.Cryptography.SHA256]::Create()
try { $frameHash = ([BitConverter]::ToString($hash.ComputeHash($frame))).Replace('-', '') }
finally { $hash.Dispose() }
Write-Output ('MANAGED_E1000_PHASE15_INJECTED=PASS transport=dgram length={0} destination={1} source=021500000001 ethertype=88B5 sequence=0x15000001 frame_sha256={2} udp_source_port={3} udp_destination_port={4}' -f `
    $frame.Length, $DestinationMac.ToUpperInvariant(), $frameHash, $SourcePort, $Port)
