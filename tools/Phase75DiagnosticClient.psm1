Set-StrictMode -Version Latest

function Get-GxdcCrc32([byte[]]$Bytes, [int]$Count) {
    [long]$crc = 4294967295L
    for ($index = 0; $index -lt $Count; $index++) {
        $crc = $crc -bxor [long]$Bytes[$index]
        for ($bit = 0; $bit -lt 8; $bit++) {
            if (($crc -band 1) -ne 0) {
                $crc = ($crc -shr 1) -bxor 3988292384L
            } else {
                $crc = $crc -shr 1
            }
        }
    }
    return [uint32](($crc -bxor 4294967295L) -band 4294967295L)
}

function Set-GxdcU16([byte[]]$Bytes, [int]$Offset, [uint16]$Value) {
    $Bytes[$Offset] = [byte]($Value -band 0xFF)
    $Bytes[$Offset + 1] = [byte](($Value -shr 8) -band 0xFF)
}

function Set-GxdcU32([byte[]]$Bytes, [int]$Offset, [uint32]$Value) {
    for ($index = 0; $index -lt 4; $index++) {
        $Bytes[$Offset + $index] = [byte](([long]$Value -shr (8 * $index)) -band 0xFF)
    }
}

function Get-GxdcU16([byte[]]$Bytes, [int]$Offset) {
    return [uint16]([long]$Bytes[$Offset] -bor ([long]$Bytes[$Offset + 1] -shl 8))
}

function Get-GxdcU32([byte[]]$Bytes, [int]$Offset) {
    [long]$value = 0
    for ($index = 0; $index -lt 4; $index++) {
        $value = $value -bor ([long]$Bytes[$Offset + $index] -shl (8 * $index))
    }
    return [uint32]($value -band 4294967295L)
}

function New-GxdcRequest {
    param(
        [Parameter(Mandatory)] [ValidateSet(1, 2)] [uint16]$CommandId,
        [Parameter(Mandatory)] [uint32]$RequestId,
        [uint32]$ExpectedFailedIdentity = 0,
        [uint16]$ExpectedFailedGeneration = 0,
        [uint32]$TargetDeviceIdentity = 1
    )
    [byte[]]$frame = New-Object byte[] 32
    $frame[0] = [byte][char]'G'; $frame[1] = [byte][char]'X'
    $frame[2] = [byte][char]'D'; $frame[3] = [byte][char]'C'
    Set-GxdcU16 $frame 4 1
    Set-GxdcU16 $frame 6 32
    Set-GxdcU32 $frame 8 $RequestId
    Set-GxdcU16 $frame 12 $CommandId
    Set-GxdcU16 $frame 14 0
    Set-GxdcU32 $frame 16 $TargetDeviceIdentity
    Set-GxdcU32 $frame 20 $ExpectedFailedIdentity
    Set-GxdcU16 $frame 24 $ExpectedFailedGeneration
    Set-GxdcU16 $frame 26 0
    Set-GxdcU32 $frame 28 (Get-GxdcCrc32 $frame 28)
    return ,$frame
}

function Read-GxdcExact {
    param(
        [Parameter(Mandatory)] [System.Net.Sockets.NetworkStream]$Stream,
        [Parameter(Mandatory)] [int]$Count,
        [Parameter(Mandatory)] [datetime]$Deadline
    )
    [byte[]]$result = New-Object byte[] $Count
    $offset = 0
    while ($offset -lt $Count -and (Get-Date) -lt $Deadline) {
        if ($Stream.DataAvailable) {
            $read = $Stream.Read($result, $offset, $Count - $offset)
            if ($read -le 0) { throw 'GXDC response stream closed.' }
            $offset += $read
        } else {
            Start-Sleep -Milliseconds 2
        }
    }
    if ($offset -ne $Count) { throw 'Timed out waiting for bounded GXDC response bytes.' }
    return ,$result
}

function Read-GxdcResponse {
    param(
        [Parameter(Mandatory)] [System.Net.Sockets.NetworkStream]$Stream,
        [Parameter(Mandatory)] [uint32]$ExpectedRequestId,
        [int]$TimeoutMilliseconds = 2000
    )
    $deadline = (Get-Date).AddMilliseconds($TimeoutMilliseconds)
    [byte[]]$header = New-Object byte[] 20
    $matched = 0
    $scanned = 0
    while ($matched -lt 4 -and (Get-Date) -lt $deadline) {
        if (!$Stream.DataAvailable) {
            Start-Sleep -Milliseconds 2
            continue
        }
        $next = $Stream.ReadByte()
        if ($next -lt 0) { throw 'GXDC response stream closed before GXDR magic.' }
        $scanned++
        if ($next -eq @(0x47, 0x58, 0x44, 0x52)[$matched]) {
            $header[$matched] = [byte]$next
            $matched++
        } elseif ($next -eq 0x47) {
            $header[0] = 0x47
            $matched = 1
        } else {
            $matched = 0
        }
        if ($scanned -gt 4096) { throw 'Exceeded bounded GXDR response resynchronization.' }
    }
    if ($matched -ne 4) { throw 'Timed out scanning for GXDR response magic.' }
    [byte[]]$rest = Read-GxdcExact $Stream 16 $deadline
    [Array]::Copy($rest, 0, $header, 4, 16)
    $length = Get-GxdcU16 $header 6
    if ($header[0] -ne 0x47 -or $header[1] -ne 0x58 -or
        $header[2] -ne 0x44 -or $header[3] -ne 0x52 -or
        (Get-GxdcU16 $header 4) -ne 1 -or $length -lt 24 -or $length -gt 100) {
        throw ('Invalid GXDR response header bytes={0} length={1}' -f
            [Convert]::ToHexString($header), $length)
    }
    [byte[]]$frame = New-Object byte[] $length
    [Array]::Copy($header, 0, $frame, 0, 20)
    [byte[]]$tail = Read-GxdcExact $Stream ($length - 20) $deadline
    [Array]::Copy($tail, 0, $frame, 20, $tail.Length)
    if ((Get-GxdcU32 $frame 8) -ne $ExpectedRequestId) {
        throw 'GXDR response request ID did not match.'
    }
    if ((Get-GxdcU32 $frame ($length - 4)) -ne (Get-GxdcCrc32 $frame ($length - 4))) {
        throw 'GXDR response CRC did not match.'
    }
    $response = [ordered]@{
        Bytes = $frame
        Length = [int]$length
        Version = Get-GxdcU16 $frame 4
        RequestId = Get-GxdcU32 $frame 8
        CommandId = Get-GxdcU16 $frame 12
        ApiResult = Get-GxdcU32 $frame 16
    }
    if ($length -eq 100) {
        $body = 20
        $response.Status = [ordered]@{
            StructureSize = Get-GxdcU32 $frame ($body + 0)
            Version = Get-GxdcU32 $frame ($body + 4)
            Slot = Get-GxdcU32 $frame ($body + 8)
            DeviceIdentity = Get-GxdcU32 $frame ($body + 12)
            OwnerState = Get-GxdcU32 $frame ($body + 16)
            CurrentValid = Get-GxdcU32 $frame ($body + 20)
            CurrentIdentity = Get-GxdcU32 $frame ($body + 24)
            CurrentGeneration = Get-GxdcU16 $frame ($body + 28)
            RouteEnabled = Get-GxdcU32 $frame ($body + 32)
            RuntimeAttached = Get-GxdcU32 $frame ($body + 36)
            FailureReason = Get-GxdcU32 $frame ($body + 40)
            LastFailedIdentity = Get-GxdcU32 $frame ($body + 44)
            LastFailedGeneration = Get-GxdcU16 $frame ($body + 48)
            LastFailedDeviceIdentity = Get-GxdcU32 $frame ($body + 52)
            RestartBudgetRemaining = Get-GxdcU32 $frame ($body + 56)
            AutomaticRestartAttempts = Get-GxdcU32 $frame ($body + 60)
            RestartFailed = Get-GxdcU32 $frame ($body + 64)
            ExplicitRestartAllowed = Get-GxdcU32 $frame ($body + 68)
            ExplicitRestartInProgress = Get-GxdcU32 $frame ($body + 72)
        }
    } elseif ($length -eq 36) {
        $response.Handle = [ordered]@{
            Identity = Get-GxdcU32 $frame 20
            DeviceIdentity = Get-GxdcU32 $frame 24
            Generation = Get-GxdcU16 $frame 28
            Slot = Get-GxdcU16 $frame 30
        }
    }
    return $response
}

function Send-GxdcRequest {
    param(
        [Parameter(Mandatory)] [System.Net.Sockets.NetworkStream]$Stream,
        [Parameter(Mandatory)] [byte[]]$Frame,
        [Parameter(Mandatory)] [uint32]$RequestId,
        [int]$TimeoutMilliseconds = 2000
    )
    if ($Frame.Length -ne 32) { throw 'GXDC v1 requests are exactly 32 bytes.' }
    Write-GxdcBytesPaced -Stream $Stream -Bytes $Frame
    return Read-GxdcResponse $Stream $RequestId $TimeoutMilliseconds
}

function Wait-GxdcSilence {
    param(
        [Parameter(Mandatory)] [System.Net.Sockets.NetworkStream]$Stream,
        [int]$Milliseconds = 200
    )
    $deadline = (Get-Date).AddMilliseconds($Milliseconds)
    while ((Get-Date) -lt $deadline) {
        if ($Stream.DataAvailable) { throw 'Malformed GXDC frame unexpectedly produced a response.' }
        Start-Sleep -Milliseconds 5
    }
}

function Write-GxdcBytesPaced {
    param(
        [Parameter(Mandatory)] [System.Net.Sockets.NetworkStream]$Stream,
        [Parameter(Mandatory)] [byte[]]$Bytes,
        [int]$InterByteDelayMilliseconds = 1
    )
    for ($index = 0; $index -lt $Bytes.Length; $index++) {
        $Stream.Write($Bytes, $index, 1)
        if ($InterByteDelayMilliseconds -gt 0 -and
            $index + 1 -lt $Bytes.Length) {
            Start-Sleep -Milliseconds $InterByteDelayMilliseconds
        }
    }
    $Stream.Flush()
}

Export-ModuleMember -Function New-GxdcRequest, Read-GxdcResponse, Send-GxdcRequest, Write-GxdcBytesPaced, Wait-GxdcSilence, Get-GxdcCrc32, Get-GxdcU16, Get-GxdcU32, Set-GxdcU16, Set-GxdcU32
