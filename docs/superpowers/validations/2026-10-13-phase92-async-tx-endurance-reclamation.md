# Phase 92 — Asynchronous E1000 TX Endurance and Reclamation

**Outcome: E — environment blocker. Phase 92 is not accepted.**

The deterministic hosted endurance qualification passed. The managed source and both Gate4 C fixtures compile. A final NativeAOT payload could not be produced because this machine has no Windows native linker (link.exe), so the Phase 92 Gate4 image could not be built from the current source. No Phase 92 QEMU boot or host packet capture was started. The worktree is preserved and uncommitted.

That Outcome E text records the initial Phase 92 attempt. The later Phase 92R recovery audit and current qualification status are recorded at the end of this report.

## Starting repository state

- Repository: D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration.
- Branch: nativeaot-managed-kernel-integration.
- Starting HEAD: fea0d44477be4787399d8ba1f7ec50f02c6cea57, matching the requested expected HEAD.
- Upstream: origin/nativeaot-managed-kernel-integration; starting state was ahead 1, behind 0.
- Worktree was clean at preflight. It is now dirty with the Phase 92 fixture and tests; no existing changes were reset or overwritten.
- Phase 91 was already committed and accepted. Its qualification is [2026-10-13-phase91r3-deterministic-txdw-race-qualification.md](2026-10-13-phase91r3-deterministic-txdw-race-qualification.md).
- No .phase file or AGENTS.md was present. Existing PowerShell host-test and Gate4 conventions were followed.
- QEMU inventory was empty before work. The final inventory is recorded at artifacts/phase92-build/qemu-inventory-no-acceptance-boots.txt; no QEMU process or acceptance boot was started during this phase.

## Ownership-model audit

The production implementation in ManagedE1000Driver and ManagedE1000AsyncTxLedger retains separate physical descriptor and logical request ownership:

1. A free physical descriptor is usable only when its ledger ownership is Free; the async ledger has four fixed logical slots while the TX ring has sixteen descriptors.
2. Submission prepares a ledger slot with generation and driver-episode identity, writes the DMA buffer and descriptor, marks ownership HardwareOwned, then publishes by advancing TDT. A failed final TDT publication rolls the prepared ledger state back.
3. Hardware owns the descriptor after TDT publication. A request remains outstanding even if a descriptor’s DD bit changes.
4. DD is the completion authority. A bounded TXDW-driven descriptor scan may process several DD descriptors; TXDW is only a possibly coalesced notification.
5. A DD result is published to the ledger as Completed, but the request slot and descriptor are still occupied until explicit consumption.
6. Completion query is nonblocking and does not consume. Successful or hardware-error completion is released by one explicit consume; a second consume sees a stale identity.
7. Reuse requires both hardware completion and consumption. Request generation and driver-episode identity prevent a reused slot or restarted driver from aliasing an old request.
8. Teardown closes admission, removes the interrupt route, quiesces/reset hardware and bus mastering, and only then invalidates remaining requests. Phase 91 route-specific stale-event containment remains in force.

Resources can remain occupied while the descriptor is hardware-owned, while DD has completed but the request has not been consumed, or while teardown has not yet established DMA quiescence. The Phase 92 tests do not treat DD completion as logical reclamation. Synchronous TX continues to use its existing DD polling contract. No scheduler wait, wakeup, asynchronous timeout, background completion thread, or new pending queue was introduced.

## Hosted endurance results

The dedicated host suite is src/ManagedKernelPhase92HostTests/Program.cs, run by tools/Run-ManagedKernelPhase92HostTests.ps1.

- Result: MANAGED_KERNEL_PHASE92_HOST_TESTS=PASS checks=178774.
- Fixed seed: 0x92C0FFEE.
- Successful submit, DD completion, publication, single consumption, and reuse lifecycles: 10,000.
- Fifth-request Busy probes: 2,500.
- The schedule cycles through initial occupancies of one, two, three, and four; it tests partial DD completion, DD-before-dispatch, one scan processing multiple DD descriptors after a coalesced-notification model, out-of-order completion and consumption, and consuming one request while others remain hardware-owned.
- Busy probes preserve cursor, ledger occupancy and ownership, completion counts, and descriptor statuses. Request IDs are checked across repeated same-slot reuse.
- The sixteen-entry descriptor ring repeatedly wraps; the suite requires at least 600 physical wrap transitions. All sixteen ledger descriptor entries are free after the final drain.
- A focused restart-boundary case rejects old episode IDs without changing the new request’s ownership, then completes and consumes the new request. DD error completion is also consumed without leaving an occupied slot.

The host suite drives the production protocol descriptor builder and async ledger. Its DD bits are simulated host inputs; this is hosted ownership qualification, not evidence of hardware transmission.

## Phase 92 Gate4 fixture and hardware status

The existing Phase 11 Gate4 builder and fresh-boot runner now accept the exclusive EnablePhase92AsyncTxEndurance mode. It composes the Phase 90 asynchronous proof with the Phase 91 teardown/restart lifecycle and two 128-frame Phase 92 episodes. Each episode submits four requests per batch, probes the fifth request for Busy, drains and consumes, and checks bounded resource counters. The host runner expects 284 total Phase 90 signature frames per boot: 256 Phase 92 endurance frames plus the existing Phase 90/91 lifecycle frames. Four DD-complete but unconsumed requests at the teardown boundary are recorded as invalidated after DMA quiescence, separately from successful consumed requests.

The host capture path validates unique sequence identifiers, Ethernet addresses, exact 60-byte frames, signature and payload padding, and independently reparses the persisted length-prefixed capture file. It probes for late duplicates after capture and guest liveness. The exact PHASE92_ASYNC_TX_ENDURANCE_PASS=1 marker is emitted only after host capture, serial accounting, guest resource drain, and route/liveness checks succeed.

Hardware acceptance did not start:

- The staged Phase 92 NativeAOT publish compiled the managed C# sources but stopped at Microsoft.NETCore.Native.Windows.targets: Platform linker not found.
- The installed Visual Studio 18 Community tree and Windows SDK were inspected; link.exe is absent. The repository-pinned .NET SDK 10.0.302 was staged under ignored artifacts and its archive SHA-512 was verified against Microsoft release metadata. No machine-wide installation was changed.
- The Phase 92 Gate4 C loader was built as a syntax/build smoke using the accepted Phase 91 NativeAOT payload, not the current Phase 92 source. This smoke image is not an acceptance candidate and was not booted.
- Phase 92 acceptance boots: 0 of 3. Per-boot accepted transmissions and captured frames: not measured. Duplicate capture count, guest ledger drain, native Phase 92 TXDW/COM1 observations, RX liveness, and post-endurance synchronous TX: not measured.

Non-candidate C smoke artifacts:

| Smoke artifact | Bytes | SHA-256 | Embedded payload |
|---|---:|---|---|
| Normal Gate4 C smoke BOOTX64.EFI | 746,194 | AA07FA905FE6C284989C735DFC7A07A59CCB2AF56AAEBF7F43E464AB7DD7470B | Accepted Phase 91 payload, 8C4DF010039756BEF928344F96EFA53EEE84574341933829314B8929E40DFAFB |
| Phase 92 Gate4 C smoke BOOTX64.EFI | 749,887 | 73F3B545CD5A6E449BDD67D89B5A96919644461B383A44FCBE1E59D4BA6E3267 | Accepted Phase 91 payload, 8C4DF010039756BEF928344F96EFA53EEE84574341933829314B8929E40DFAFB |

No current-source Phase 92 NativeAOT payload or final Gate4 EFI hash exists. These smoke hashes must not be used as acceptance artifact identities.

## Host regression matrix

These hosted regressions passed in fresh Phase 92 evidence directories:

- Phase 14 E1000: PASS.
- Phase 16 networking: PASS, 139 cases.
- Phase 90 standalone host runner: none available; Phase 14 covers the Phase 90 frame and TX protocol helpers, and Phase 92/91 host suites cover async ledger behavior.
- Phase 91 lifecycle host suite: PASS, 60 checks.
- Phase 92 endurance host suite: PASS, 178,774 checks / 10,000 lifecycles.
- Managed interrupts: PASS, 36/36 assertions; source completeness check passed.
- Native interrupts: PASS.
- Driver binding: PASS.
- Network service (Phase 23 HTTP/service host suite): PASS, 60 cases. The existing Phase 23 project omits `ManagedInterruptEvent.cs` even though the current `ManagedE1000Protocol.cs` references that type, so its runner fails to compile. The suite was run with an isolated project under ignored `artifacts/` that included this existing source dependency; no tracked test or production file was changed for the adaptation.
- Managed host services: PASS.
- Service host: PASS.
- Service owner and status: PASS.
- Service worker: PASS.

The hardware regression matrix (Phases 14, 16, 91, 92, 85, 87, and 89) was not run because the final NativeAOT candidate could not be linked. The three-boot hardware acceptance and all per-boot packet reconciliation remain incomplete.

## Builds, checks, repairs, and remaining limits

- ManagedKernel Release C# source build with NativeAOT disabled: PASS. This verified managed compilation only; it is not a NativeAOT payload build.
- ManagedKernel NativeAOT Release build: BLOCKED at the missing Windows native linker prerequisite.
- Normal Gate4 C smoke build and Phase 92 Gate4 C smoke build: PASS with the prior Phase 91 payload, for C fixture compilation only.
- Phase 92 host harness build and execution: PASS.
- PowerShell parser validation for the modified build and runner scripts: PASS.
- git diff --check: PASS; Git only reported its existing LF-to-CRLF working-copy notices.
- Initial managed compilation found two uint array-index errors in the new fixture code; they were corrected to ring-array indices. No async ownership, DMA, completion, or synchronous-TX behavioral defect was found or repaired.
- New production-source changes add a gated Phase 92 qualification path and counters. The accepted asynchronous ownership implementation and synchronous DD-polling behavior were not changed.
- No final hardware result, host capture evidence, final NativeAOT payload, or Phase 92 candidate EFI was produced. Phase 92 is therefore not accepted.

## Final repository state

- HEAD remains fea0d44477be4787399d8ba1f7ec50f02c6cea57; branch and upstream are unchanged.
- Current ahead/behind remains 1/0. No Phase 92 commit was made and no push was attempted because the result is Outcome E.
- The worktree remains dirty with the Phase 92 fixture, host suite, runner/build updates, and this report. Generated evidence and smoke artifacts remain under artifacts/; they were not staged.

Phase 81 accepted: No. Phase 81R2 accepted: No. Scheduler waits added: No. Async timeouts introduced: No. Phase 93 started: No.

## Phase 92R recovery audit — 2026-10-15

**Current result: Outcome C — partial qualification.** The NativeAOT toolchain was recovered, the hosted suites passed, and Phase 92 passed three fresh isolated hardware boots. Phase 14, Phase 16, Phase 85, Phase 87, and Phase 89 hardware regressions each passed three fresh boots. The formal three-boot Phase 91 hardware regression remains incomplete: its runner refused preflight after detecting a QEMU process owned by a separate workspace. That process was left untouched. The earlier Outcome E remains the historical result of the initial attempt above.

### Starting repository state and toolchain recovery

- Recovery started from branch `nativeaot-managed-kernel-integration`, HEAD `066bb2ac580145bf766fab1fb95010c062220351`, with a clean worktree and upstream tracking at 0/0. This is the user’s committed baseline. HEAD remains unchanged because the complete required regression matrix did not finish; no commit or push was made.
- `link.exe` was installed in Visual Studio Community 2026 but absent from the base shell’s PATH. VS instance `18.10.12306.391` includes the C++ x64 tools and Windows SDK. A process-local `VsDevCmd.bat -arch=x64 -host_arch=x64` environment exposed `cl.exe` 19.51.36260 and Hostx64/x64 `link.exe` 14.51.36260.0 at `C:\Program Files\Microsoft Visual Studio\18\Community\VC\Tools\MSVC\14.51.36231\bin\HostX64\x64\link.exe`. Windows SDK 10.0.26100.0 and its x64 UCRT/UM libraries were present. No global PATH or system installation was changed.
- The repository pins .NET SDK 10.0.302, which was not installed under `C:\Program Files\dotnet`. The complete SDK archive was already staged under ignored `artifacts/phase92-toolchain-10.0.302/`: 297,545,270 bytes, SHA-512 `7D170ED75FA9AF34C00646621D92011DBD71943952E2787CD15DF9BE78E6452B55DADEF34D7EFF77B802E6AF4959E071A55855AC649AFEAC70901C3A2A258716` (the staged archive had previously been checked against Microsoft release metadata). MSBuild 18.6.11.33009 built the final candidate using an explicit SDK-directory override. `tools/Build-ManagedKernel.ps1` now accepts that override through the existing host-toolchain resolver.
- A separate build using installed SDK 10.0.402 linked successfully, but its QEMU startup attempt stalled at the managed driver-worker readiness wait and timed out after 300 seconds. This is recorded as an observed SDK/environment difference; it does not establish that SDK 10.0.402 caused the stall. Its payload was 4,946,432 bytes, SHA-256 `0354ACA643CCDB1096B0967C62458CA4E5379E8F9EAC2F010523D47BE1DB6F96` and was not used for acceptance.

### Final Phase 92 candidate and hardware results

- SDK 10.0.302 NativeAOT Release publish completed with 0 warnings and 0 errors. The candidate `gxos-managed-kernel.dll` is 4,946,432 bytes, SHA-256 `3F0E921E05E6C24434EDD5D76DFFBFED9E4378E5AF536B030D70118DF4145E26`. The Phase 92 Gate4 ESP contains a byte-identical payload with the same size and hash.
- The normal Gate4 EFI built with the `ManagedKernelPhase11` scenario is 746,194 bytes, SHA-256 `CE5011C4D226251EE87D1EE57D50B187EF2ABDF4443E23C6E88E41ACCB3BBAF0`. The Phase 92 Gate4 EFI is 749,887 bytes, SHA-256 `1F669B27625057D44924966A5F98FFC2C75309E8B8F14BA82809FEAA4769346A`.
- After the first `.302` boot exposed a host-runner ordering defect, the runner’s Phase 92 final-counter check was moved until after the post-restart RX frame injection, RX-liveness markers, and lifecycle reconciliation. It still checks the Phase 91 stop counters and expected capture count before packet capture. This was an acceptance-harness repair; no production TX ownership or lifecycle behavior changed.
- The repaired Phase 92 runner passed three consecutive fresh isolated boots against the same payload. Every boot emitted `PHASE92_ASYNC_TX_ENDURANCE_PASS=1`; each persisted capture is 18,176 bytes and independently reconciles 284 unique frames, including all 256 Phase 92 frames. Each episode accepted, completed, transmitted, exposed, and consumed 128 frames; exercised 32 Busy probes; wrapped the descriptor ring 9 times; reached but did not exceed four outstanding requests; and drained to zero. Capacity was restored after each drain. At the stop boundary, four DD-completed unconsumed requests were invalidated only after DMA quiescence, with zero hardware-owned requests. The final ledger reconciled 133 exposed/consumed restart requests and zero outstanding, hardware-owned, or completed-unconsumed resources.
- The three boots also passed TXDW route, bounded interrupt, COM1 independence, synchronous TX DD-polling, driver restart, stale-ID rejection, and post-restart RX-liveness checks. Phase 92 evidence is under ignored `artifacts/phase92r-20261015-run-2/phase92-hardware-3boot-pinned-fixed/`.
- The SDK 10.0.302 Phase 91 startup diagnostic passed one boot, including restart RX injection and TX/RX liveness. The requested three-boot Phase 91 regression was not run because of the later isolation gate.

### Hosted and completed hardware regressions

- Phase 14 hosted E1000: PASS. Phase 16 hosted networking: PASS, 139 cases. Phase 90 has no standalone host-test runner; Phase 14 covers its frame helper and Phase 92 covers asynchronous ownership.
- Phase 91 hosted lifecycle: PASS, 60 checks. Phase 92 hosted endurance: PASS, 178,774 checks, seed `0x92C0FFEE`, 10,000 lifecycles, and 2,500 Busy probes.
- Managed interrupts: PASS, 36/36 assertions and source-completeness check. Native interrupts: PASS. Driver binding: PASS. Network service / Phase 23: PASS, 60 cases, using the existing narrow isolated project adaptation to include `ManagedInterruptEvent.cs`. Host services, service host, owner/status, and worker suites: PASS.
- Phase 14 E1000 hardware: PASS, 3/3 fresh boots. Phase 16 networking hardware: PASS, 3/3 fresh boots with all four ARP exchange frames validated in each PCAP. Both used the exact `.302` NativeAOT payload hash above.
- Phase 85, Phase 87, and Phase 89 Gate4 regression images were built from that same payload and their staged hashes matched. After the unrelated QEMU process was no longer present, each regression passed three fresh isolated boots with the common runner validating its guest markers and packet exchanges. Evidence is under ignored `artifacts/phase92r-20261015-run-2/regressions/hardware/phase85-arp-resumed/`, `phase87-icmp-resumed/`, and `phase89-udp-resumed/`. Phase 85 validated hardware ARP ingress and replies, Phase 87 validated three captured ICMP Echo Replies per boot, and Phase 89 validated UDP ingress on all three boots.
- The first Phase 14 and Phase 16 diagnostic attempts used a normal Gate4 image without the specific Phase 11 / Phase 15 regression compile options; their timeouts were fixture-selection errors. Those logs were retained and are not counted. Rebuilt scenario-specific fixtures passed Phase 14 and Phase 16 as listed above.

### Isolation, checks, and final state

- Phase 92 and the completed Phase 14/16/85/87/89 formal boot preflight and postflight inventories were clear. At the later Phase 91 runner preflight, QEMU process PID 25832 was classified `UNRELATED`; its command line used `out/validation/settings-s14-20261015/production/guest-acceptance-complete`. The Phase 91 runner refused to start, wrote the inventory to `artifacts/phase92r-20261015-run-2/regressions/hardware/phase91-lifecycle-resumed/qemu-process-preflight.log`, and did not stop or modify the process. The formal Phase 91 three-boot regression remains at the isolation gate.
- PowerShell parsing passed for the five modified scripts, and `git diff --check` passed (Git reported only the repository’s LF-to-CRLF working-copy notices). The Phase 14, Phase 16, and service-host runners gained optional output-directory parameters so fresh evidence did not overwrite prior output locations.
- Phase 91 remains accepted from its prior qualification. Phase 81 accepted: No. Phase 81R2 accepted: No. Synchronous TX still uses DD polling. No scheduler wait work or asynchronous timeout was added. Phase 93 was not started.

## Phase 92R2 closure — 2026-10-16

**Current result: Outcome E — environment blocker. Phase 92 is not accepted.** The Phase 91 current-source three-boot regression did not start because another workspace held QEMU throughout the permitted 20-minute isolation wait. Its process was not attached to, stopped, or modified. The prior Phase 92 3/3 hardware evidence and its artifact hashes remain valid. The prior Phase 91 acceptance remains intact, but the required Phase 91 rerun against the Phase 92 production source is still incomplete.

### Preflight and source provenance

- R2 started and ended at HEAD `066bb2ac580145bf766fab1fb95010c062220351` on `nativeaot-managed-kernel-integration`, tracking `origin/nativeaot-managed-kernel-integration` at 0/0 divergence. The Phase 92 commit is a direct child of the historical accepted Phase 91 commit `fea0d44477be4787399d8ba1f7ec50f02c6cea57`; that Phase 91 commit has parent `4f9e99421383fe098c787b520505e6f5e6ebf32e`. No branch or repository topology changed.
- The six Phase 92 worktree modifications from the prior recovery were preserved. R2 added one small harness-only change to `Run-ManagedKernelPhase91.ps1`: an optional `-SdkDirectory` is forwarded to the existing build script, allowing the accepted pinned SDK to be selected explicitly. No production source or acceptance predicate changed. The Phase 91 build/boot wrapper was not invoked because QEMU remained occupied.
- The recovered toolchain is process-local Visual Studio Community 2026: MSVC linker 14.51.36260.0, compiler 19.51.36260, and Windows SDK 10.0.26100.0. The final NativeAOT candidate was produced with .NET SDK 10.0.302 via an explicit SDK-directory override; its authoritative `publish.stdout.log` reports 0 warnings and 0 errors. The older `toolchain-provenance.txt` in the recovery directory records an earlier SDK 10.0.402 attempt and is not provenance for the final candidate.
- Production sources under `src/ManagedKernel` have no worktree diff from the qualified source commit. The 4,946,432-byte payload at that commit is SHA-256 `3F0E921E05E6C24434EDD5D76DFFBFED9E4378E5AF536B030D70118DF4145E26`, matching the payload embedded beside the Phase 92 Gate4 fixture and the payload recorded by each prior Phase 92 host-capture validation. It is distinct from the historical Phase 91 payload `8C4DF010039756BEF928344F96EFA53EEE84574341933829314B8929E40DFAFB`.
- Full hashes were recalculated from the retained files and recorded in `artifacts/phase92r2-20261016/phase92-final-artifact-manifest.txt`; no separate named manifest existed in the earlier Phase 92 run directory. Verified identities are:

| Artifact | Bytes | SHA-256 | Use |
|---|---:|---|---|
| Current-source NativeAOT payload | 4,946,432 | `3F0E921E05E6C24434EDD5D76DFFBFED9E4378E5AF536B030D70118DF4145E26` | Phase 92 production source |
| Normal Gate4 EFI | 746,194 | `CE5011C4D226251EE87D1EE57D50B187EF2ABDF4443E23C6E88E41ACCB3BBAF0` | Phase 11 fixture |
| Phase 92 Gate4 EFI | 749,887 | `1F669B27625057D44924966A5F98FFC2C75309E8B8F14BA82809FEAA4769346A` | Prior 3-boot Phase 92 acceptance |
| Existing Phase 91 Gate4 EFI | 749,887 | `154F17D57143F1B3059727C092AF4E7CDBE86D1082739724539FC36B9CFAECC3` | Prior one-boot current-source diagnostic only; not R2 acceptance |

The Phase 92 payload and EFI hashes match the saved build outputs and are not reconstructed from abbreviated values. The Phase 91 fixture's EFI identity is listed separately from the production payload identity.

### Preserved Phase 92 hardware evidence and ownership totals

- The existing pinned-SDK Phase 92 image passed three fresh isolated boots. Each saved host-validation record reports `PHASE92_ASYNC_TX_ENDURANCE_PASS=1`, 284 unique captured frames, all 256 endurance frames, zero duplicate frame identifiers, valid payload padding, reconciled request accounting, guest resource drain, TXDW/COM1 checks, RX liveness, and teardown. Each capture is 18,176 bytes with SHA-256 `F1311EA75197285EADF7A2BFE495DE893C0360FA6B1617083C15D8D9E6258621`.
- Each of the two endurance episodes accepted, completed, transmitted, exposed, and consumed 128 requests; recorded 32 Busy probes and nine descriptor wraps; reached four outstanding requests and drained to zero. At the teardown boundary, four completed-but-unconsumed requests were invalidated after DMA quiescence and zero hardware-owned requests remained. The final restart ledger reported 133 exposed and consumed requests, with zero outstanding, hardware-owned, or completed-unconsumed requests.
- The existing Phase 92 SDK 10.0.302 run directory remains `artifacts/phase92r-20261015-run-2/phase92-hardware-3boot-pinned-fixed/`. R2 did not rebuild Phase 92 or rerun its endurance boots.

### Phase 91 isolation attempt

- Fresh inventories at 16:24:27 and 16:44:33 PDT, plus the intervening checks listed in `artifacts/phase92r2-20261016/qemu-isolation-hold-inventory.txt`, continued to show one QEMU process: PID 10488, created 16:21:08, executable `C:\Program Files\qemu\qemu-system-x86_64.exe`, owner `LEON2_RDT\guideX`, with its ESP and serial/trace paths under `D:\dev\guideXOSServer_DiskManagerImprovements`. The final snapshot was six seconds after the 20-minute deadline because the preceding check at 16:44:03 still showed the process; no QEMU work was started during that interval.
- The process was classified `UNRELATED` to this repository. It remained active through every recorded hold check and was still present on the 16:44:33 final snapshot, six seconds after the 20-minute deadline. It was left untouched. No Phase 91 build, QEMU launch, or hardware boot was attempted during the hold. The Phase 91 three-boot result for the current Phase 92 source is therefore **0/3; blocked before boot**, not failed and not passed. No R2 Phase 91 serial log, host capture, guest failure stage, or per-boot counters exist.
- The existing Phase 91 runner continues to enforce clean QEMU isolation before each boot and checks for unexpected QEMU processes during the run. The modified scripts, including the SDK forwarding change, all passed PowerShell parser validation. `git diff --check` passed.

### Complete regression matrix at R2 closure

| Gate | Result |
|---|---|
| Phase 14 hosted E1000 | PASS |
| Phase 16 hosted networking | PASS, 139 cases |
| Phase 91 hosted lifecycle/proof | PASS, 60 checks |
| Phase 92 hosted endurance | PASS, 10,000 lifecycles and 178,774 checks |
| Managed interrupts | PASS, 36/36 assertions and source completeness |
| Native interrupts; driver binding | PASS |
| Network service / Phase 23 | PASS, 60 cases |
| Host services; service host; owner/status; service worker | PASS |
| Phase 90 standalone host suite | No standalone runner; frame/TX helpers covered by Phase 14, async ownership by Phases 91/92 host suites |
| Phase 14 hardware | PASS, 3/3 fresh boots |
| Phase 16 hardware | PASS, 3/3 fresh boots |
| Phase 85 ARP hardware | PASS, 3/3 fresh boots |
| Phase 87 ICMP hardware | PASS, 3/3 fresh boots |
| Phase 89 UDP hardware | PASS, 3/3 fresh boots |
| Phase 91 historical accepted candidate | PASS, 3/3 in Phase 91R3; this remains the prior Phase 91 acceptance |
| Phase 91 rerun against Phase 92 source | BLOCKED before build/boot, 0/3; unrelated QEMU occupied through the bounded wait |
| Phase 92 hardware endurance | PASS, 3/3; 284 unique frames per boot |
| PowerShell parser checks; `git diff --check` | PASS |

### R2 final state and remaining limits

- Current classification is Outcome E because QEMU isolation prevented the only outstanding qualification run. No production repair was made or supported by new failure evidence. Previously passing hosted and hardware evidence was preserved without rerunning it.
- The worktree remains uncommitted. HEAD is unchanged at `066bb2ac580145bf766fab1fb95010c062220351`; the seven coherent Phase 92 tracked files are modified, and generated verification records remain under ignored `artifacts/`. No commit or push was attempted because Phase 92 did not reach Outcome A or B.
- Phase 91 remains accepted from its earlier qualification. Phase 81 remains unaccepted. Phase 81R2 remains unaccepted. Normal synchronous TX retains DD polling. No scheduler wait implementation or asynchronous timeout was added. Phase 93 was not started.

## Phase 92R3 QAC retry audit — 2026-10-20

**Current result: Outcome E — blocked before execution by the Phase 91 runner's missing QAC integration.** The fresh QEMU census was clear, but the required QAC launch contract cannot run the existing PowerShell orchestrator. No Phase 91 build or QEMU boot was started.

### Fresh preflight and QAC status

- The retry started on `nativeaot-managed-kernel-integration` at HEAD `bcc25361b89b132de40217ef2d0ca6ec8552d5ab`, tracking `origin/nativeaot-managed-kernel-integration` at 0/0 divergence. The worktree was clean at preflight. This is the current baseline; the older R2 state above is retained as history.
- A fresh `Win32_Process` census found zero QEMU processes. QAC status independently reported zero QEMU processes and zero foreign processes.
- The installed CLI apphost at `D:\dev\guideXOS_QAC\src\GuideXOS.QAC.Cli\bin\Debug\net10.0-windows\GuideXOS.QAC.Cli.exe` passed `--help`, `status`, and `list` with exit code 0. The coordinator was online as QAC 2.0.0 (PID 408), with zero reservations, zero managed sessions, zero queued or blocked requests, and development limit 2. The `qac` command alias was not on PATH.
- No lease was acquired. The appropriate formal lease would be exclusive `acceptance`; it was not acquired because the existing runner cannot launch through QAC's supported child contract. No `qac run` was invoked, no runner invocation occurred, and there was no lease to release.

### QAC and runner integration constraint

- The requested runner is `tools/Run-ManagedKernelPhase91.ps1`; it builds the Phase 91 fixture from the current production payload and delegates to `tools/Run-ManagedKernelPhase11FreshBoots.ps1` for three boots.
- The existing Phase 11 runner launches QEMU directly with PowerShell `Start-Process` at line 4061 of `Run-ManagedKernelPhase11FreshBoots.ps1`. It does not accept or call a QAC CLI.
- The installed QAC CLI's `run` implementation accepts only a direct `qemu-system-*.exe` child and explicitly rejects wrappers and scripts. Its reservation is tied to that child's lifetime; it does not provide a single acceptance reservation that can attach the runner's sequential QEMU children across all three boots.
- Therefore `qac run` cannot launch the complete existing PowerShell runner while preserving its current arguments, exit behavior, and evidence flow. Running the script directly under a separate lease would bypass the required QAC child registration. QAC was not changed, and no direct QEMU fallback was used.

### Source and retained artifact provenance

- The qualified production source commit `066bb2ac580145bf766fab1fb95010c062220351` is an ancestor of current HEAD. The diff from that candidate commit to current HEAD contains no changes under `src/ManagedKernel`; the previously qualified Phase 92 production payload remains applicable.
- Recalculated files match `artifacts/phase92r2-20261016/phase92-final-artifact-manifest.txt`:

| Artifact | Bytes | SHA-256 | Qualification |
|---|---:|---|---|
| NativeAOT production payload | 4,946,432 | `3F0E921E05E6C24434EDD5D76DFFBFED9E4378E5AF536B030D70118DF4145E26` | Phase 92 production source |
| Phase 92 Gate4 EFI | 749,887 | `1F669B27625057D44924966A5F98FFC2C75309E8B8F14BA82809FEAA4769346A` | Existing Phase 92 3-boot evidence |
| Phase 91 Gate4 EFI | 749,887 | `154F17D57143F1B3059727C092AF4E7CDBE86D1082739724539FC36B9CFAECC3` | Prior one-boot current-source diagnostic, not 3-boot acceptance |

- Existing Phase 92 evidence remains 3/3 boots, with 284 unique captured frames per boot including 256 endurance frames, no duplicate identifiers, and zero outstanding requests after teardown. Existing Phase 92 hosted endurance remains 10,000 lifecycles and 178,774 checks. None of this evidence was rerun during R3.

### Regression matrix and R3 result

| Gate | Result at R3 |
|---|---|
| Phase 14 hosted E1000 | PASS |
| Phase 16 hosted networking | PASS, 139 cases |
| Phase 91 hosted lifecycle/proof | PASS, 60 checks |
| Phase 92 hosted endurance | PASS, 10,000 lifecycles and 178,774 checks |
| Managed interrupts | PASS, 36/36 assertions and source completeness |
| Native interrupts; driver binding | PASS |
| Network service / Phase 23 | PASS, 60 cases |
| Host services, service host, owner/status, and service worker | PASS |
| Phase 90 standalone host suite | No standalone runner; covered through Phase 14, 91, and 92 host suites |
| Phase 14, 16, 85, 87, and 89 hardware | PASS, 3/3 boots each |
| Phase 91 historical accepted candidate | PASS, 3/3 in Phase 91R3; prior acceptance remains intact |
| Phase 91 against current Phase 92 production source | BLOCKED before build/boot, 0/3; QAC cannot launch the unintegrated runner |
| Phase 92 hardware endurance | PASS, 3/3; 284 unique frames per boot |
| Earlier PowerShell parser and `git diff --check` qualification checks | PASS; no Phase 91 test scripts were changed in R3 |

The R3 attempt was **not a QEMU isolation failure**: the fresh census and operational QAC coordinator both showed no QEMU processes. It remains Outcome E because the test entry point has no supported QAC integration and QAC's direct-child restriction prevents an external wrapper from supplying one.

### R3 final state

- Phase 91 remains 0/3 for the current Phase 92 source, blocked before build and boot. It is neither passed nor failed.
- No production or harness source changed. No commit or push was made. The report update is uncommitted; QAC itself was not installed, modified, or reconfigured.
- QAC record: CLI apphost verified; coordinator online; intended lease type `acceptance` not acquired; `qac run` and the Phase 91 runner were not invoked; release outcome not applicable because no lease was created.
- Phase 81 remains unaccepted. Phase 81R2 remains unaccepted. Normal synchronous TX retains DD polling. No scheduler wait implementation or asynchronous timeout was added. Phase 93 was not started.

## Phase 92R4 QAC-managed Phase 91 acceptance audit — 2026-10-22

**Current result: Outcome E — the live QAC API cannot keep one exclusive acceptance reservation while registering the Phase 91 runner's three sequential QEMU children.** QAC was available and granted a legitimate childless acceptance reservation after normal queueing, but no Phase 91 build or QEMU boot was started because the granted lease could not own the required multi-boot process sequence. Phase 91 against the Phase 92 source remains 0/3, blocked before build and boot. Phase 92 is not accepted.

### Repository and source preflight

- R4 started on `nativeaot-managed-kernel-integration` at HEAD `bcc25361b89b132de40217ef2d0ca6ec8552d5ab`, tracking `origin/nativeaot-managed-kernel-integration` at 0/0 divergence. The only modified tracked file was this qualification report, already carrying the uncommitted R3 audit; there were no untracked files. The earlier Phase 92 implementation files are part of the current HEAD. No branch, history, `.phase` file, or other project was changed.
- The Phase 92 production candidate commit `066bb2ac580145bf766fab1fb95010c062220351` remains an ancestor of HEAD. `git diff 066bb2ac580145bf766fab1fb95010c062220351 HEAD -- src/ManagedKernel` is empty, so the previously accepted Phase 92 production-source evidence remains applicable.
- The Phase 91 wrapper still builds the current NativeAOT payload and its Phase 91 Gate4 fixture, then calls `Run-ManagedKernelPhase11FreshBoots.ps1` for three fresh boots. The latter starts QEMU directly with `Start-Process`; it requires a single QEMU PID for isolation checks and separate per-boot firmware, endpoints, captures, and logs.

### Live QAC implementation and admission

- QAC source HEAD was `8ff500c60393e5e38fa4b8cd4d83f213b17df0f0` (`main`, two commits ahead of `origin/main`, clean worktree). The verified CLI apphost is `D:\dev\guideXOS_QAC\src\GuideXOS.QAC.Cli\bin\Debug\net10.0-windows\GuideXOS.QAC.Cli.exe`, SHA-256 `F7089D508C1D8307E11DE1B944B6E1087C038C4901333E8228EC117291CA8DF6`. `qac --help`, `status`, `list`, `queue`, and `inspect` succeeded; status identified QAC 2.0.0. Supported reservation modes are `development` and `acceptance`. QAC accepts a runtime project identifier rather than maintaining a project-registration catalog; R4 used the repository's existing identity `guideXOS_NET10`.
- The live README and QAC2 design document specify `qac run --project <name> --mode <development|acceptance> -- <qemu-system-*.exe> [arguments...]`. It starts only a direct QEMU child, preserves its argument boundaries and exit code, and rejects wrappers and scripts. `qac request` can hold a childless reservation while its CLI owner remains alive. Existing Developer Studio acceptance and Server launcher examples use the direct-child `qac run` contract for a single QEMU execution; they do not define a multi-child acceptance session.
- The IPC implementation records one child on a reservation: `prepareLaunch` moves a granted reservation to `Starting`; `registerChild` accepts only that state and records the direct child; `complete` releases the reservation after that child exits. `release` also makes a granted childless reservation terminal. There is no supported operation to clear a completed child and attach the next QEMU under the same acceptance reservation. The QAC FIFO acceptance barrier orders pending reservations, but it is not a batch/session contract that atomically spans this three-boot workload. Three separate `qac run` reservations therefore cannot be claimed as one uninterrupted exclusive Phase 91 acceptance session.
- A fresh QAC census at 14:42 PDT showed the healthy managed `guideXOSServer` acceptance reservation `8ce67ff290d2479fafcf516b145f556d`, QEMU PID 3512, and zero foreign QEMU. The independent Windows process census agreed. That session was left untouched. Following the documented occupied-session procedure, R4 submitted `qac request --project guideXOS_NET10 --mode acceptance`; reservation `435a9c5e294b43b7b1f35737cc112dd1` entered `Waiting` at queue position 1 with verified owner PID 22656. QAC granted it at `2026-10-22T21:43:51.3058393Z` after about 33 seconds. Because it had no registered QEMU child and the API could not cover the boot series, it was promptly released through `qac release`; QAC reported `Released` with reason `Released by authenticated local user`. This reservation is admission evidence only, not guest acceptance evidence.
- Final `qac list` reported zero reservations and managed sessions; `queue` reported zero queued requests; `status` reported zero QEMU processes and zero foreign processes. The final independent Windows census also reported zero QEMU. The request, queue, grant, release, and final census records are retained under ignored `artifacts/phase92r4-20261022/qac-admission/`. No `qac run` or Phase 91 runner invocation occurred.

### Preserved Phase 92 artifact provenance

The saved R2 manifest at `artifacts/phase92r2-20261016/phase92-final-artifact-manifest.txt` was checked against the retained files. Hashes and sizes match:

| Artifact | Bytes | SHA-256 | R4 use |
|---|---:|---|---|
| Current-source NativeAOT payload | 4,946,432 | `3F0E921E05E6C24434EDD5D76DFFBFED9E4378E5AF536B030D70118DF4145E26` | Existing Phase 92 production candidate |
| Phase 92 Gate4 EFI | 749,887 | `1F669B27625057D44924966A5F98FFC2C75309E8B8F14BA82809FEAA4769346A` | Existing Phase 92 3-boot acceptance |
| Phase 91 Gate4 EFI | 749,887 | `154F17D57143F1B3059727C092AF4E7CDBE86D1082739724539FC36B9CFAECC3` | Retained current-source one-boot diagnostic only; not Phase 91 acceptance |

The payload embedded in that Phase 91 diagnostic EFI is byte-identical to the saved NativeAOT payload and has the same SHA-256. No new Phase 91 artifact was built in R4. Each of the three retained Phase 92 captures is 18,176 bytes with SHA-256 `F1311EA75197285EADF7A2BFE495DE893C0360FA6B1617083C15D8D9E6258621`; each saved host-validation log contains `PHASE92_ASYNC_TX_ENDURANCE_PASS=1`, and the existing reconciliation records 284 unique frames including 256 endurance transmissions per boot.

### Regression matrix and R4 result

| Gate | Current result |
|---|---|
| Phase 14 hosted E1000 | PASS |
| Phase 16 hosted networking | PASS, 139 cases |
| Phase 91 hosted lifecycle | PASS, 60 checks |
| Phase 92 hosted endurance | PASS, 10,000 lifecycles and 178,774 checks |
| Managed interrupts; native interrupts; driver binding | PASS; managed suite 36/36 assertions |
| Network service / Phase 23; host services; service host; owner/status; service worker | PASS; Phase 23 has 60 cases |
| Phase 90 standalone host suite | No standalone runner; covered by Phase 14, 91, and 92 host suites |
| Phase 14, 16, 85, 87, and 89 hardware | PASS, 3/3 boots each |
| Historical Phase 91 accepted candidate | PASS, 3/3; prior acceptance remains intact |
| Phase 91 against current Phase 92 source | BLOCKED before build/boot, 0/3; no supported multi-child QAC acceptance contract |
| Phase 92 hardware endurance | PASS, 3/3; 284 unique frames per boot and 256 endurance transmissions per boot |
| Phase 91 runner PowerShell parse; `git diff --check` | PASS; no runner source was changed |

### R4 final state

- Outcome E is an API capability blocker, not a Phase 91 guest failure and not foreign QEMU contention. The current QAC API can admit and own one QEMU child per reservation; it cannot maintain the required single exclusive acceptance session across three sequential QEMU children. No second scheduler, unmanaged launch, QAC change, or acceptance-logic weakening was introduced.
- The QAC reservation was released, the requesting CLI exited, and no QEMU remained active. Prior Phase 92 hardware and hosted qualification were preserved without rerunning them. No focused QAC adapter test exists because no compliant adapter can be implemented against the available API.
- The worktree remains uncommitted at `bcc25361b89b132de40217ef2d0ca6ec8552d5ab`; only this report is modified. No commit or push was attempted because the outcome is E. Phase 91 remains accepted on its earlier candidate; Phase 81 and Phase 81R2 remain unaccepted. Synchronous TX still DD-polls. No scheduler waits, async timeout infrastructure, or Phase 93 work was added.

## Phase 92R5 QAC per-boot admission and final closeout — 2026-10-22

**Final result: Outcome B — Accepted with runner/adapter repair.** The current-source Phase 91 E1000 lifecycle regression passed three independent cold boots. Each QEMU process was QAC 2.0.0's direct child under its own `guideXOS_NET10` acceptance reservation, each used the same freshly built candidate, and each reservation was released after a zero-exit child. The earlier Phase 92 endurance candidate and its retained 3/3 hardware evidence remain valid. No production driver or QAC code was changed.

### Repository, build, and candidate

- R5 started on `nativeaot-managed-kernel-integration` at `bcc25361b89b132de40217ef2d0ca6ec8552d5ab`, tracking its upstream at 0/0 divergence. The only tracked change before R5 was this report, which retained the complete R3/R4 blocker history. No branch, worktree, history, or unrelated project was altered. At closeout, `src/ManagedKernel` still has no diff.
- The Phase 91 candidate was built once from current source with the recovered process-local Visual Studio environment: .NET SDK 10.0.302 and MSVC linker 14.51.36260.0. The build succeeded with 0 errors and 4 compiler warnings at `ManagedIpv4Layer.cs:386,395`, `ManagedKernel.cs:2869`, and `ManagedKernel.cs:4081`; those source lines were not changed. Boot 2 and boot 3 reused the built files and did not rebuild production or fixture binaries.
- This fresh Phase 91 regression candidate is distinct from the retained Phase 92 candidate. Its NativeAOT payload is 4,946,432 bytes, SHA-256 `DA65B072CDA3E62565B03086F18B449ACC076619631A7133076EAAF12716EC5D`; the staged payload has the same size and hash. The Phase 91 Gate4 EFI is 749,887 bytes, SHA-256 `154F17D57143F1B3059727C092AF4E7CDBE86D1082739724539FC36B9CFAECC3`.
- The common Phase 91 ESP manifest hash is `F383D53521695BD0BEB9091931D0A4C1854B782C0D66B2A08F3E386B9D6FEC34`. It contains `EFI\BOOT\BOOTX64.EFI` (749,887 bytes, hash above), `GXOS\gxos-managed-kernel.dll` (4,946,432 bytes, hash above), and `startup.nsh` (27 bytes, SHA-256 `6A231F5CF395B72E9A38EF0A8B00B69CDB410F160E9E941414EA53E04E969CCE`). QEMU used this FAT ESP with the same immutable OVMF code input (3,653,632 bytes, SHA-256 `33090CC07675BAA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A`) and pristine OVMF vars template (540,672 bytes, SHA-256 `5D2AC383371B408398ACCEE7EC27C8C09EA5B74A0DE0CEEA6513388B15BE5D1E`). Each boot received a separate writable copy of the vars template; only those per-run copies changed during guest execution. The source payload, staged payload, EFI, ESP, OVMF code, and pristine template remained at their recorded hashes throughout all three boots.
- The freshly produced Phase 91 EFI is a regression fixture, not the retained Phase 92 EFI. No Phase 92 endurance tests were repeated.

### QAC contract and runner repair

- The verified QAC CLI apphost is `D:\dev\guideXOS_QAC\src\GuideXOS.QAC.Cli\bin\Debug\net10.0-windows\GuideXOS.QAC.Cli.exe`, SHA-256 `F7089D508C1D8307E11DE1B944B6E1087C038C4901333E8228EC117291CA8DF6`; the coordinator reported version 2.0.0. The runner used the documented `qac run --project guideXOS_NET10 --mode acceptance -- <qemu-system-x86_64.exe> [arguments...]` form. It passed the exact QEMU argument list via `ProcessStartInfo.ArgumentList`; QAC, not PowerShell or another wrapper, launched QEMU as its direct child. `qac --help`, `status`, `list`, `queue`, and per-reservation `inspect` were used and retained.
- A narrow QAC-only guard incorrectly rejected the supported one-reservation/one-QEMU Phase 91 run form. It now permits one per-boot lifecycle invocation or the existing three-boot suite, while retaining the Phase 91 mode, freshly built source identity, QAC project, direct-child, exclusive-isolation, guest, and host validation checks. The post-exit owner-record path now reads the persisted QAC release verification rather than relying on a transient completion object. The prelaunch failure path records a cancelled/failed admission with its reservation and owner details and does not run child-release verification when no child was launched.
- Boot 1's QEMU and QAC processes both exited 0 and its reservation was `Released`; the first outer invocation then exited 1 while finalizing the owner JSON. Its raw serial, capture, direct-child ownership, QAC release, and exit evidence were retained. The owner record was reconciled from the captured ownership and release JSON, then all unchanged outer Phase 91 markers, counters, and 28-frame assertions were independently rechecked and passed. The narrow owner-record repair was applied to the runner before boots 2 and 3, and both subsequent runs completed through the runner's normal success path.
- One later request was cancelled before admission because its invocation waited for the already-emitted Phase 11 marker instead of the original wrapper's final `GXOS_NET10:MANAGED_KERNEL_PHASE12_PASS` marker. QAC reservation `24d93af17abb49219c98ae41837698e7` remained queued at position 1, was cancelled through QAC, ended `Cancelled`, and never launched a QEMU child. That prelaunch request is preserved under `artifacts/phase92r5-20261022-boot2-evidence/` and is not counted as a boot or a guest failure. The corrected boot 2 request used the original Phase 12 completion marker.

### Three independent QAC-managed Phase 91 boots

| Boot | QAC acceptance reservation | Admission | Direct QAC owner → QEMU child | Reservation/exit | Guest and host result |
|---|---|---|---|---|---|
| 1 | `0f37ceae580d4f3bbbbae088133a5ef6` | Queue 1 | PID 10040 → PID 5244; owner `2026-10-22T22:08:26.5634777Z`, QEMU `2026-10-22T22:09:26.6044722Z` | Released; owner 0, QEMU 0 | PASS; 28/28 unique frames |
| 2 | `122f34dbf0714718a56acc40ef95ec0b` | Queue 2 | PID 26348 → PID 15768; owner `2026-10-22T22:21:44.3320098Z`, QEMU `2026-10-22T22:29:29.6430917Z` | Released; owner 0, QEMU 0 | PASS; 28/28 unique frames |
| 3 | `1aa745169b6c446cb82897b69286d685` | Immediate; no queued position | PID 8588 → PID 17944; owner `2026-10-22T22:30:23.1153695Z`, QEMU `2026-10-22T22:30:23.2868458Z` | Released; owner 0, QEMU 0 | PASS; 28/28 unique frames |

For each boot, QAC `list` verified the owner and direct QEMU child, and QAC `status` reported exactly one QEMU with zero foreign processes while the boot was active. The recorded QEMU executable was `C:\Program Files\qemu\qemu-system-x86_64.exe`, and its parent PID matched the QAC CLI PID. Per-reservation `inspect` reported `Released`; the QAC owner and that boot's QEMU process identity were absent after completion. QAC reported no QEMU, queued request, or managed session after the final run. Between the independent reservations, other projects were admitted in FIFO order as permitted by the corrected contract.

All three boots passed the 19 unchanged outer Phase 91 required markers, exactly one `MANAGED_KERNEL_PHASE11_PASS`, and the final `MANAGED_KERNEL_PHASE12_PASS`. Each captured 28 frames in a 1,792-byte capture file, with 28 unique sequence IDs. Each recorded the same guest accounting: pre-stop async acceptance/completion/transmission `21/21/21`; stop-boundary outstanding and completed-unconsumed `4`, hardware-owned-not-DD `0`, invalidated `4`; restart async acceptance/completion/transmission `5/5/5`; one pre-stop and one restart synchronous DD-poll TX frame; one fresh post-restart TXDW notification for synchronous TX; and 9 native interrupt entries, within the bounded no-storm envelope. The stale FIFO, safe request IDs, stale-ID rejection, same-slot descriptor reuse, restored four-request async capacity, route teardown/restoration, post-restart RX liveness, COM1 independence, TXDW routing, and no-interrupt-storm markers passed on every boot.

| Boot | Serial SHA-256 | Capture SHA-256 | Capture log SHA-256 | Injection log SHA-256 | Timeline SHA-256 |
|---|---|---|---|---|---|
| 1 | `4329788E2E7ED78CE6D29CB2588DDEF50065725C5DD329B7DEDF603A7DC6A79E` | `77D0D3A217CDE6421F732BBCF4FA4402B79B449D9E2307126BE93C85199E8AFC` | `DAC29CC1379890D701A3FC450D4E75500EBCD130911A124AD6FB34B4F00767E0` | `F3F1DAFAA81834AE069BAE18D0B21C684AEBC845CC783A35F6CEFB46F7C40246` | `E2F0EC6FAC28123BA63143A96C39C145E657C0E58454C635EA59E44E5A3E7E3F` |
| 2 | `8B753F192838724079B2C8C03625447E60F72F4B99438E52B78B1811710899FE` | `77D0D3A217CDE6421F732BBCF4FA4402B79B449D9E2307126BE93C85199E8AFC` | `DAC29CC1379890D701A3FC450D4E75500EBCD130911A124AD6FB34B4F00767E0` | `945E0AE604F5DFE103B774E87012FC2663AB14CACDAB24CDC1C0DF97BB8F25F3` | `B40B3162B0EACCB535225F38B0B36ACFE60CC21BC1DF504E90FB7B83A008758F` |
| 3 | `8DABC820AB99DF9939A524ECF9CCBBD4EAAB5D48EDF43569865D4EBF11C13D0E` | `77D0D3A217CDE6421F732BBCF4FA4402B79B449D9E2307126BE93C85199E8AFC` | `DAC29CC1379890D701A3FC450D4E75500EBCD130911A124AD6FB34B4F00767E0` | `35C744417FE7A3406ED20D7AD396E282C64415159CDECF9154357067774B8818` | `8CA1A22CD69E31CC8B855D0F6453F294B0F6DB89DC51EEE12DCC06D009FCB2EB` |

The complete per-boot serial, packet captures, QAC invocation/output, active-owner census, release verification, timelines, and post-run reconciliation are under `artifacts/phase92r5-20261022-evidence/`, `artifacts/phase92r5-20261022-boot2-accepted-evidence/`, and `artifacts/phase92r5-20261022-boot3-evidence/`. The separate prelaunch cancellation and its QAC inspect record are under `artifacts/phase92r5-20261022-boot2-evidence/`.

### Prior Phase 92 evidence and final regression matrix

The existing `artifacts/phase92r2-20261016/phase92-final-artifact-manifest.txt` was recalculated against its retained files and still matches. Its 4,946,432-byte Phase 92 NativeAOT payload remains SHA-256 `3F0E921E05E6C24434EDD5D76DFFBFED9E4378E5AF536B030D70118DF4145E26`; the accepted Phase 92 EFI remains SHA-256 `1F669B27625057D44924966A5F98FFC2C75309E8B8F14BA82809FEAA4769346A`. Each of the three retained Phase 92 captures remains 18,176 bytes with SHA-256 `F1311EA75197285EADF7A2BFE495DE893C0360FA6B1617083C15D8D9E6258621`, and its saved host validation reports PASS. The retained Phase 92 hardware evidence is still 3/3 boots and 284 unique frames per boot, including 256 endurance transmissions, with zero outstanding, hardware-owned, and completed-unconsumed requests at final teardown. Hosted Phase 92 endurance remains 10,000 async lifecycles and 178,774 checks. These results were verified from the saved manifests and validation records; the endurance suite was not repeated.

| Qualification gate | R5 result |
|---|---|
| Phase 14 hosted E1000 | PASS |
| Phase 16 hosted networking | PASS, 139 cases |
| Phase 91 hosted lifecycle/proof | PASS, 60 checks |
| Phase 92 hosted endurance | PASS, 10,000 lifecycles and 178,774 checks; retained |
| Managed interrupts | PASS, 36/36 assertions and source completeness; retained |
| Native interrupts and driver binding | PASS; retained |
| Network service / Phase 23 | PASS, 60 cases; retained |
| Host services, service host, owner/status, and service worker | PASS; retained |
| Phase 90 standalone host suite | No separate runner; covered through Phase 14, 91, and 92 host suites |
| Phase 14, 16, 85, 87, and 89 hardware | PASS, 3/3 each; retained |
| Historical accepted Phase 91 candidate | PASS, 3/3; remains accepted |
| Current-source Phase 91 lifecycle under QAC | PASS, 3/3 independent reservations and cold boots |
| Phase 92 hardware endurance | PASS, 3/3; 284 unique frames per boot; retained |
| PowerShell parsing and `git diff --check` | PASS |

### R5 closeout

Outcome B applies because narrow Phase 91 runner/adapter defects in per-boot invocation, post-exit QAC owner reporting, and no-child admission cleanup were corrected; all three hardware boots passed on one unchanged candidate. No E1000 production behavior or QAC admission/fairness semantics changed. Synchronous TX continues to use DD polling; no scheduler waits or asynchronous timeouts were added.

Phase 91 accepted: **Yes** (the prior qualification remains intact, and current-source hardware regression is now 3/3). Phase 81 accepted: **No**. Phase 81R2 accepted: **No**. Synchronous TX still DD-polls: **Yes**. Scheduler waits added: **No**. Async timeouts added: **No**. Phase 93 started: **No**.
