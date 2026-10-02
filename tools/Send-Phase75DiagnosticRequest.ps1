param(
    [Parameter(Mandatory)] [ValidateRange(1, 65535)] [int]$Port,
    [Parameter(Mandatory)] [ValidateSet('STATUS', 'RESTART')] [string]$Command,
    [uint32]$RequestId = 1,
    [uint32]$ExpectedFailedIdentity = 0,
    [uint16]$ExpectedFailedGeneration = 0,
    [int]$TimeoutMilliseconds = 2000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Phase75DiagnosticClient.psm1') -Force
$commandId = if ($Command -eq 'STATUS') { 1 } else { 2 }
$request = New-GxdcRequest -CommandId $commandId -RequestId $RequestId `
    -ExpectedFailedIdentity $ExpectedFailedIdentity `
    -ExpectedFailedGeneration $ExpectedFailedGeneration
$client = [Net.Sockets.TcpClient]::new()
try {
    $client.NoDelay = $true
    $connect = $client.ConnectAsync('127.0.0.1', $Port)
    if (!$connect.Wait($TimeoutMilliseconds) -or !$client.Connected) {
        throw "Timed out connecting to loopback diagnostic endpoint on port $Port."
    }
    $client.ReceiveTimeout = $TimeoutMilliseconds
    $response = Send-GxdcRequest -Stream $client.GetStream() -Frame $request -RequestId $RequestId
    $response.Remove('Bytes')
    $response | ConvertTo-Json -Depth 6
} finally {
    $client.Dispose()
}
