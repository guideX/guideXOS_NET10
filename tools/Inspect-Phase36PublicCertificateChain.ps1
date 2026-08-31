[CmdletBinding()]
param(
    [string]$HostName = 'www.cloudflare.com',
    [int]$Port = 443,
    [string]$OutputPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $root 'artifacts\phase36-certificate-audit\public-certificate-chain.log'
}
$output = [IO.Path]::GetFullPath($OutputPath)
$parent = Split-Path -Parent $output
New-Item -ItemType Directory -Force -Path $parent | Out-Null

function Add-Bytes {
    param([Collections.Generic.List[byte]]$List, [byte[]]$Bytes)
    foreach ($value in $Bytes) { $List.Add($value) }
}

function Add-U16 {
    param([Collections.Generic.List[byte]]$List, [int]$Value)
    if ($Value -lt 0 -or $Value -gt 65535) { throw 'u16 value out of range' }
    $List.Add([byte](($Value -shr 8) -band 0xFF))
    $List.Add([byte]($Value -band 0xFF))
}

function Add-U24 {
    param([Collections.Generic.List[byte]]$List, [int]$Value)
    if ($Value -lt 0 -or $Value -gt 16777215) { throw 'u24 value out of range' }
    $List.Add([byte](($Value -shr 16) -band 0xFF))
    $List.Add([byte](($Value -shr 8) -band 0xFF))
    $List.Add([byte]($Value -band 0xFF))
}

function Read-Exact {
    param([IO.Stream]$Stream, [byte[]]$Buffer)
    $offset = 0
    while ($offset -lt $Buffer.Length) {
        $read = $Stream.Read($Buffer, $offset, $Buffer.Length - $offset)
        if ($read -le 0) { return $false }
        $offset += $read
    }
    return $true
}

function Get-Hex {
    param([byte[]]$Bytes)
    return ([BitConverter]::ToString($Bytes)).Replace('-', '')
}

function Get-DerOid {
    param([byte[]]$Bytes)
    if ($Bytes.Length -lt 3 -or $Bytes[0] -ne 6) { return '' }
    $length = $Bytes[1]
    if (($length -band 0x80) -ne 0 -or $length -ne $Bytes.Length - 2) { return '' }
    $content = $Bytes[2..($Bytes.Length - 1)]
    $first = [Math]::Min(2, [int]([int]$content[0] / 40))
    $values = [Collections.Generic.List[string]]::new()
    $values.Add([string]$first)
    $values.Add([string]([int]$content[0] - $first * 40))
    $value = 0L
    $count = 0
    for ($index = 1; $index -lt $content.Length; $index++) {
        $octet = $content[$index]
        $value = ($value -shl 7) -bor ($octet -band 0x7F)
        if (++$count -gt 5) { return '' }
        if (($octet -band 0x80) -eq 0) {
            $values.Add([string]$value)
            $value = 0
            $count = 0
        }
    }
    if ($count -ne 0) { return '' }
    return ($values -join '.')
}

function Get-CertificateAudit {
    param([byte[]]$Der, [int]$Index)
    $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($Der)
    $lines = [Collections.Generic.List[string]]::new()
    $lines.Add("CERT_INDEX=$Index")
    $lines.Add("DER_LENGTH=$($Der.Length)")
    $lines.Add("DER_SHA256=$(([BitConverter]::ToString([Security.Cryptography.SHA256]::HashData($Der))).Replace('-', ''))")
    $lines.Add("SUBJECT=$($certificate.Subject)")
    $lines.Add("ISSUER=$($certificate.Issuer)")
    $lines.Add("SERIAL=$($certificate.SerialNumber)")
    $lines.Add("NOTBEFORE_UTC=$($certificate.NotBefore.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))")
    $lines.Add("NOTAFTER_UTC=$($certificate.NotAfter.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))")
    $lines.Add("SIGNATURE_ALGORITHM_OID=$($certificate.SignatureAlgorithm.Value)")
    $lines.Add("PUBLIC_KEY_ALGORITHM_OID=$($certificate.PublicKey.Oid.Value)")
    $publicKeyBits = 'NA'
    $rsaKey = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPublicKey($certificate)
    if ($null -ne $rsaKey) {
        $publicKeyBits = [string]$rsaKey.KeySize
        $rsaKey.Dispose()
    }
    $lines.Add("PUBLIC_KEY_BITS=$publicKeyBits")
    $lines.Add("EC_PARAMETERS_OID=$(Get-DerOid ([byte[]]$certificate.PublicKey.EncodedParameters.RawData))")
    foreach ($extension in $certificate.Extensions) {
        $lines.Add("EXTENSION_OID=$($extension.Oid.Value) CRITICAL=$($extension.Critical) RAW_HEX=$(Get-Hex ([byte[]]$extension.RawData))")
    }
    $lines.Add("DER_BASE64=$([Convert]::ToBase64String($Der))")
    $lines.Add('')
    return $lines
}

$nameBytes = [Text.Encoding]::ASCII.GetBytes($HostName)
if ($nameBytes.Length -eq 0 -or $nameBytes.Length -gt 253) { throw 'Host name is outside the bounded diagnostic range.' }

$extensions = [Collections.Generic.List[byte]]::new()
$serverName = [Collections.Generic.List[byte]]::new()
$serverName.Add(0); Add-U16 $serverName $nameBytes.Length; Add-Bytes $serverName $nameBytes
$serverNameList = [Collections.Generic.List[byte]]::new()
Add-U16 $serverNameList $serverName.Count; Add-Bytes $serverNameList $serverName.ToArray()
Add-U16 $extensions 0; Add-U16 $extensions $serverNameList.Count; Add-Bytes $extensions $serverNameList.ToArray()

$groups = [byte[]](0, 2, 0, 23)
Add-U16 $extensions 10; Add-U16 $extensions $groups.Length; Add-Bytes $extensions $groups
$pointFormats = [byte[]](1, 0)
Add-U16 $extensions 11; Add-U16 $extensions $pointFormats.Length; Add-Bytes $extensions $pointFormats
$signatureAlgorithms = [byte[]](0, 6, 4, 3, 5, 3, 4, 1)
Add-U16 $extensions 13; Add-U16 $extensions $signatureAlgorithms.Length; Add-Bytes $extensions $signatureAlgorithms
Add-U16 $extensions 35; Add-U16 $extensions 0
Add-U16 $extensions 23; Add-U16 $extensions 0
Add-U16 $extensions 0xFF01; Add-U16 $extensions 1; $extensions.Add(0)

$helloBody = [Collections.Generic.List[byte]]::new()
Add-Bytes $helloBody ([byte[]](3, 3))
$random = [byte[]]::new(32)
$random[0] = 0x69; $random[1] = 0x7A; $random[2] = 0x2C; $random[3] = 0x01
for ($index = 4; $index -lt $random.Length; $index++) { $random[$index] = [byte](($index * 29 + 7) -band 0xFF) }
Add-Bytes $helloBody $random
$helloBody.Add(0)
Add-U16 $helloBody 2; Add-Bytes $helloBody ([byte[]](0xC0, 0x2B))
$helloBody.Add(1); $helloBody.Add(0)
Add-U16 $helloBody $extensions.Count; Add-Bytes $helloBody $extensions.ToArray()

$handshake = [Collections.Generic.List[byte]]::new()
$handshake.Add(1); Add-U24 $handshake $helloBody.Count; Add-Bytes $handshake $helloBody.ToArray()
$record = [Collections.Generic.List[byte]]::new()
$record.Add(0x16); Add-Bytes $record ([byte[]](3, 1)); Add-U16 $record $handshake.Count; Add-Bytes $record $handshake.ToArray()

$tcp = [Net.Sockets.TcpClient]::new()
$tcp.ReceiveTimeout = 30000
$tcp.SendTimeout = 30000
$tcp.Connect($HostName, $Port)
$stream = $tcp.GetStream()
try {
    $stream.Write($record.ToArray(), 0, $record.Count)
    $handshakeBytes = [Collections.Generic.List[byte]]::new()
    $certificates = [Collections.Generic.List[byte[]]]::new()
    for ($recordIndex = 0; $recordIndex -lt 32 -and $certificates.Count -eq 0; $recordIndex++) {
        $header = [byte[]]::new(5)
        if (-not (Read-Exact $stream $header)) { throw 'TLS diagnostic connection ended before the certificate message.' }
        $contentType = $header[0]
        $length = ([int]$header[3] -shl 8) -bor [int]$header[4]
        if ($length -gt 16384) { throw 'TLS diagnostic record exceeded 16 KiB.' }
        $payload = [byte[]]::new($length)
        if (-not (Read-Exact $stream $payload)) { throw 'TLS diagnostic record was truncated.' }
        if ($contentType -eq 22) {
            Add-Bytes $handshakeBytes $payload
            $offset = 0
            while ($handshakeBytes.Count - $offset -ge 4) {
                $messageType = $handshakeBytes[$offset]
                $messageLength = ([int]$handshakeBytes[$offset + 1] -shl 16) -bor
                    ([int]$handshakeBytes[$offset + 2] -shl 8) -bor [int]$handshakeBytes[$offset + 3]
                if ($messageLength -gt 49152) { throw 'TLS diagnostic handshake exceeded certificate bound.' }
                if ($handshakeBytes.Count - $offset - 4 -lt $messageLength) { break }
                if ($messageType -eq 11) {
                    $body = $handshakeBytes.GetRange($offset + 4, $messageLength).ToArray()
                    if ($body.Length -lt 3) { throw 'Certificate body was truncated.' }
                    $listLength = ([int]$body[0] -shl 16) -bor ([int]$body[1] -shl 8) -bor [int]$body[2]
                    if ($listLength -ne $body.Length - 3 -or $listLength -gt 49152) { throw 'Certificate list length was invalid.' }
                    $bodyOffset = 3
                    while ($bodyOffset -lt $body.Length) {
                        if ($body.Length - $bodyOffset -lt 3) { throw 'Certificate entry length was truncated.' }
                        $derLength = ([int]$body[$bodyOffset] -shl 16) -bor ([int]$body[$bodyOffset + 1] -shl 8) -bor [int]$body[$bodyOffset + 2]
                        $bodyOffset += 3
                        if ($derLength -le 0 -or $derLength -gt 16384 -or $derLength -gt $body.Length - $bodyOffset) { throw 'Certificate DER length was invalid.' }
                        $certificates.Add($body[$bodyOffset..($bodyOffset + $derLength - 1)])
                        $bodyOffset += $derLength
                        if ($certificates.Count -gt 4) { throw 'Certificate count exceeded four.' }
                    }
                    if ($bodyOffset -ne $body.Length) { throw 'Certificate list did not end cleanly.' }
                    break
                }
                $offset += 4 + $messageLength
            }
        }
        if ($contentType -eq 21) { throw 'TLS peer sent an alert before its certificate message.' }
    }
    if ($certificates.Count -eq 0) { throw 'TLS certificate message was not observed.' }

    $lines = [Collections.Generic.List[string]]::new()
    $lines.Add("TARGET=$HostName`:$Port")
    $lines.Add("CAPTURE_UTC=$([DateTime]::UtcNow.ToString('o'))")
    $lines.Add("SERVER_CERTIFICATE_COUNT=$($certificates.Count)")
    for ($index = 0; $index -lt $certificates.Count; $index++) {
        foreach ($line in (Get-CertificateAudit $certificates[$index] $index)) {
            $lines.Add($line)
        }
    }
    Set-Content -LiteralPath $output -Value $lines -Encoding ascii
    Get-Content -LiteralPath $output
}
finally {
    $stream.Dispose()
    $tcp.Dispose()
}
