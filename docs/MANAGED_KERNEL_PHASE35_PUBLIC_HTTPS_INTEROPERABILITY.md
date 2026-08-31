# guideXOS .NET 10 Managed Kernel Phase 35

Observed 2026-08-30. This document records the real-network interoperability
boundary reached by the Phase 35 implementation. The public run is an
additional proof; the deterministic Phase 22–34 fixtures remain authoritative.

## 1. Objective

Phase 35 adds a distinct NativeAOT guest boot arm that performs a managed
public HTTPS GET through the managed E1000 driver, QEMU user-mode NAT, DHCP,
ARP, IPv4 routing, DNS, TCP, the existing managed TLS 1.2 client, certificate
validation, and HTTP. The host does not perform DNS, TLS, HTTP, or proxy work
for the guest.

## 2. Phase 34 starting point

Phase 34 was the successful deterministic URL/redirect baseline. Its
fixture-injected network path and redirect-chain acceptance are preserved. The
Phase 35 arm is selected only by the explicit native build switch and boot
stage; the Phase 34 runner is unchanged.

## 3. Deterministic versus public-network architecture

Fixture mode continues to receive controlled frames from the Phase 15 dgram
backend. Public mode uses a separate `GXOS_ENABLE_MANAGED_KERNEL_PHASE35`
build, starts the managed public consumer after Phase 11, and never waits for
an injected fixture frame. The normal regression runners do not require
Internet access.

## 4. QEMU real-network backend

The public runner launches QEMU 11.0.0 with the explicit pair:

```text
-netdev user,id=net0
-device e1000e,netdev=net0,addr=2
```

The user backend provides QEMU SLIRP/NAT. Public mode does not add `-nic none`.
The exact command lines are retained below the public evidence directory.

## 5. E1000 ownership

The managed E1000e driver owns PCI function `0:0:2:0`, its BAR, MAC, DMA
rings, interrupt path, transmit path, and receive path. The native command
service has two guarded command slots only for the Phase 35 coexistence case:
one for E1000 and one for the persistent virtio RNG provider. Deterministic
mode retains its original single-owner behavior.

## 6. DHCP behavior

The managed DHCP client completed DISCOVER, REQUEST, ACK, and lease binding
against QEMU user networking. Every public boot recorded the lease, subnet,
router, DNS option, and lease duration. The observed lease was IPv4
`10.0.2.15`, mask `255.255.255.0`, gateway `10.0.2.2`, DNS `10.0.2.3`, and
lease duration `0x15180` seconds.

## 7. Default-gateway and routing behavior

`ManagedIpv4Protocol.TrySelectNextHop` now separates the IP destination from
the L2 next hop. Same-subnet destinations select themselves; off-subnet
destinations select the DHCP router only when that router is directly
reachable on the configured subnet.

## 8. ARP behavior for off-subnet destinations

The public DNS server (`10.0.2.3`) is ARPed directly. The resolved public
address is sent with an ARP request for gateway `10.0.2.2`; the resulting
Ethernet destination is the gateway MAC while the IPv4 destination remains
the public server address. Serial evidence includes both direct and gateway
next-hop markers.

## 9. DNS behavior

The DNS server comes from DHCP option 6 and is queried by the managed DNS
client. No host resolver participates. `www.cloudflare.com` resolved in the
three-boot proof to `104.16.123.96` on run 1 and `104.16.124.96` on runs 2–3
(the answer can vary on later runs).

## 10. TCP behavior

The managed TCP client completed SYN, SYN/ACK, ACK, and connection
establishment to public port 443. Public serial evidence records
`PUBLIC_TCP_CONNECTED`; no host TCP client carries the guest request.

## 11. TLS profile used

The unchanged Phase 31 profile is used: TLS 1.2 (`0x0303`),
`TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256` (`C02B`), P-256 ECDHE, ECDSA with
SHA-256, SNI, and extended master secret required. No TLS 1.3, RSA key
exchange, extra cipher suite, or opportunistic algorithm was enabled.

The ServerHello from the selected target chose TLS 1.2 and `C02B`, and
advertised EMS. Standard ServerHello acknowledgements for SNI, EMS,
`ec_point_formats`, and the empty session-ticket/renegotiation forms are
accepted without changing the cryptographic profile.

## 12. TLS workspace profile

The general TLS record ceiling remains 16 KiB, with a 16 KiB plaintext
fragment ceiling and bounded handshake growth. The public arm uses the
general profile rather than the compact fixture-only profile.

## 13. Certificate workspace

The public certificate message is bounded at 49,152 bytes. Four certificate
slots at 16 KiB each give a 65,536-byte certificate workspace. No unbounded
certificate allocation is introduced. The HTTP proof uses a 4,096-byte
response bound and 512-byte streaming delivery chunks.

## 14. Public certificate-validation path

The public consumer uses the production `ManagedHttpsClient` and the real
Phase 30 chain/time/hostname validation path. The observed Cloudflare chain
was not leaf-pinned and validation was not skipped. The supplied public trust
anchor is the audited GTS Root R4 DER seen in the chain; matching the anchor
does not bypass parsing or signature validation.

## 15. Trusted-time behavior

The public run uses the project validation-time model with a fixed, explicit
UTC validation instant (`2026-08-30 12:00:00`) for reproducibility. No
certificate is accepted without validity checks. The QEMU RTC is configured
as UTC in the runner; the current proof avoids silently depending on a host
time API.

## 16. Selected public target(s)

The selected safe target was:

```text
https://www.cloudflare.com/llms.txt
```

The target is bounded, HTTPS-only, and suitable for a small public GET. The
body digest is evidence for that run rather than a hard-coded webpage-content
regression. Redirect policy remains the Phase 34 HTTPS-only policy if a future
target redirects.

## 17. Public DNS evidence

All three final public boots recorded DHCP DNS configuration, a managed DNS
response, and a managed `PUBLIC_DNS_RESOLVED_IPV4` marker. The observed
answers were `104.16.123.96` and `104.16.124.96`; the DNS query and answer
traveled through the guest network stack.

## 18. Public TCP evidence

All three final public boots recorded `PUBLIC_TCP_CONNECTED` after the
managed gateway ARP and TCP handshake. The successful TCP boundary is
independent of the subsequent TLS certificate result.

## 19. TLS negotiation evidence

All three boots recorded a genuine ServerHello with version `0303`, suite
`C02B`, EMS enabled, and a certificate handshake that reached the managed
validator. The raw secret-bearing handshake material is never printed.

## 20. Certificate and hostname-validation result

Outcome B was reached at certificate validation. The live chain contains the
ECDSA public HTTPS leaf/intermediate path and a cross-signed GTS Root R4 whose
RSA signature form is outside the current ECDSA/SHA-256, P-256 certificate
validator. The managed validator reports `UnsupportedAlgorithm` (`0x2`).
Hostname validation therefore was not reported as successful, and no
hostname check was weakened to force progress.

## 21. HTTP result

No HTTP request was sent because TLS authentication did not complete. There
is consequently no public HTTP 200 marker and no body-success claim.

## 22. Redirect result

The selected `/llms.txt` probe did not reach HTTP, so it produced no redirect
chain. The existing Phase 34 redirect implementation and its HTTPS downgrade
policy remain the deterministic authority.

## 23. Body-verification method

The public consumer is ready to require final status 200, a non-empty bounded
body, exact streamed byte accounting, and a streamed SHA-256 digest. Since
Outcome B stops before HTTP, no body digest is asserted for this run.

## 24. Packet and QEMU evidence

Every public run preserves `serial.log`, `qemu-commandline.log`, QEMU stdout
and stderr, firmware identity, injection log, and timeline. The final receive
traces at
`artifacts/phase35-public-final4/evidence/runs/run-1/qemu-trace.log` (and the
corresponding run-2/run-3 files) show the QEMU E1000 RX descriptor path
receiving the real DHCP response. The final public run uses the same explicit
user backend and does not use a host proxy.

## 25. Public failure classifications

The public outcome markers are A (full success), B (network path proven with
precise TLS-profile incompatibility), C (DHCP/DNS proven but TCP incomplete),
and D (network integration incomplete). The final run is B with:

```text
PUBLIC_TLS_PROFILE_INCOMPATIBLE=RSA-CROSS-SIGNED-ROOT-UNSUPPORTED
PUBLIC_TLS_CERTIFICATE_PARSE_INDEX=0x1
PUBLIC_TLS_CERTIFICATE_PARSE_STATUS=0x2
```

This is not collapsed into a generic `TLS_FAILED` result.

## 26. Deterministic regressions

The focused deterministic expectations remain Phase 22: 56, Phase 23: 60,
Phase 30: 91, Phase 31: 33, Phase 32: 69, Phase 33: 185, and Phase 34: 140.
The Phase 35 route host proof adds six cases and passed, for a post-change
focused total of 640/640. No broader aggregate runner is established in this
repository; the earlier Phase 15–29 aggregate remains documented separately.
The final report records each authoritative evidence path.

## 27. Phase 34 positive and negative regressions

The required controls remain three of three deterministic positive
redirect-chain boots ending in `phase34-redirect-pass`, recorded under
`artifacts/phase35-phase34-positive-final6`, and three of three
hostname-mismatch negative boots with no final HTTP success marker, recorded
under `artifacts/phase35-phase34-negative-final6`. Public Internet access is
not substituted for either control.

## 28. Memory observations

Known public bounds are: 16 KiB TLS record ceiling; 49,152-byte certificate
message bound; 65,536-byte four-certificate workspace; 4,096-byte response
bound; 512-byte HTTP delivery chunk; and 2,048-byte pending TLS application
window. The payload identity and QEMU memory size are retained in the run
metadata. No forced guest GC dependency was added.

## 29. GC policy

The public consumer does not require explicit guest `GC.Collect()` pressure.
The host-side GC-survival tests remain authoritative; guest forced GC pressure
is deferred. The proof only preserves bounded object lifetimes and reports
survival when TLS authentication reaches that point.

## 30. Teardown and reuse

On Outcome B, the managed HTTPS client performs its failure snapshot and the
E1000/virtio providers are stopped through the Phase 14 lifecycle. The public
runner then closes its QEMU serial/monitor clients and stops only QEMU
processes whose command line belongs to that run. The deterministic service
reuse and teardown paths are unchanged.

## 31. Payload identity

The dedicated runner builds the NativeAOT DLL, hashes it, stages the exact
bytes into the Gate 4 ESP, verifies the staged hash and size, and passes that
identity into the fresh-boot runner. The final values are recorded in
`artifacts/phase35-public-final4/phase35-run-metadata.log` and
`artifacts/phase35-public-final4/phase35-summary.log`:

```text
size=1755648
sha256=4567ACDCCFC7EF7C4E38362C4BB5481D54245A0B9CCE4D0CCFE80726A6613E2B
```

## 32. Known limitations

The selected live endpoint demonstrates the real network path and the exact
current certificate-parser boundary, but it cannot complete HTTP until the
validator supports the audited RSA cross-sign/root form. The implementation
does not broaden TLS or add RSA merely to turn this result into A. Public DNS
answers, certificate chains, and content can vary; the runner records each
run rather than hard-coding them.

## 33. Recommendation for Phase 36

Keep Outcome B as the acceptance result for this phase. Phase 36 should first
audit and extend the certificate trust/parser model for the observed
cross-signed root and any SHA-384/RSA signature forms, with host vectors and
negative controls, before considering additional public endpoints. It should
not weaken hostname, time, chain, or trust validation and should not add
unrelated TLS suites as a workaround.

## Reproduction

From the repository root:

```powershell
pwsh -NoProfile -File .\tools\Run-ManagedKernelPhase35PublicHttps.ps1 `
    -OutputDirectory .\artifacts\phase35-public-final4 `
    -RunCount 3 -TimeoutSeconds 180
```

Add `-EnableQemuReceiveTrace` when a bounded QEMU E1000 receive trace is
needed. The runner performs no host DNS, HTTP, TLS, proxy, or synthetic
Internet-packet injection.
