# Managed Kernel Phase 51 — bounded external stylesheet fetching and page orchestration

Phase 51 adds a bounded page-resource coordinator to the managed kernel. It
fetches one HTTPS document, discovers stylesheet sources in document order,
fetches external CSS one resource at a time, streams each decoded stylesheet
into the fixed-arena CSS engine, then runs cascade, layout, paint, raster, and
GOP presentation. The implementation is deterministic and synchronous from
the kernel’s point of view: it uses no `Task`, `async`, thread pool, dynamic
CSS body buffer, `@import` recursion, or general browser networking layer.

## Orchestration contract

`ManagedPageResourceOrchestrator` in
`src/ManagedKernel/ManagedPageResourceOrchestration.cs` owns the page state
machine and the fixed source table. Its states are:

`Idle → FetchingDocument → DiscoveringSources → FetchingStylesheet →
ParsingStylesheet → Cascade → Layout → Paint → Raster → Presenting → Complete`

The HTML tree is built by the existing streaming tokenizer/tree builder. After
the document body completes, the coordinator polls one source at a time. A
`<link>` is eligible when `rel` contains `stylesheet`, `alternate` sources are
ignored, and the `type` attribute is absent or is `text/css` (ASCII
case-insensitive). Inline `<style>` nodes are parsed at their source position.
Consequently, equal-specificity rules obey document order across inline and
external sources. The proof fixture has external A, inline CSS, then external
B; B wins and produces the visible blue target.

The source table is fixed-capacity. The Phase 51 proof allows two external
stylesheets per page while the CSS arena reserves four external records. The
engine’s general maximum is 16 records. Hrefs are copied through fixed
scratch buffers and are limited to 512 scalars. Relative URLs resolve against
the final document URL, including a redirected document URL. HTTPS is
required for the document and every stylesheet; HTTP downgrade redirects and
unsupported URL forms are rejected. Redirect counts, final URLs, status, MIME,
charset, content encoding, encoded bytes, decoded bytes, scalar counts, rule
counts, declaration counts, and per-sheet digests are retained as bounded
telemetry.

## HTTP, decoding, and CSS limits

The document request requires a successful 2xx status. External stylesheet
requests require both a successful 2xx status and `text/css` MIME. The shared
resource stack validates the response framing and streams the body through
the existing bounded gzip decoder before text decoding. UTF-8 is the default;
explicit UTF-8, US-ASCII, and ISO-8859-1 declarations are supported by the
bounded decoder, with charset metadata and source (default, BOM, or explicit)
recorded. CSS does not restart on `@charset`, and `@import` is deliberately
unsupported so a stylesheet cannot create an unbounded recursive fetch graph.

The CSS parser consumes decoded scalars incrementally. It keeps comment and
brace state across delivery windows and retains only one completed rule in
fixed scratch storage before committing it to the arena. The stylesheet scalar
limit is 16,384. The page proof uses fixed capacities of 8 stylesheets, 64
rules, 128 selectors, 256 selector steps, 256 declarations, and 128 computed
styles, with an external record capacity of 4. Capacity overflow is a
terminal, observable failure rather than an allocation fallback.

## Host coverage

`src/ManagedKernelPhase51HostTests` exercises 1,083 cases. The suite covers
document-order cascade, inline/external source discovery, `rel` tokenization,
mixed-case `type`, alternate suppression, href resolution and length bounds,
fragmented CSS delivery, comments, rule/declaration limits, URL and HTTPS
policy, reset/cancel behavior, and layout/paint/raster output. The preserved
Phase 44 CSS suite passes 66 cases; the Phase 50 extended-font suite passes
683 cases.

## NativeAOT and authoritative QEMU proof

The payload was built with the installed .NET 10.0.400 SDK fallback because
the repository’s pinned 10.0.302 SDK is not installed. The resulting
NativeAOT payload is 4,680,192 bytes with SHA-256
`711C3598F44974C282DF7EFC2B8C90FD127750A88DCEDB0715643BB72CA85CB1`.
QEMU is 11.0.0. The reusable proof wrapper is
`tools/Run-ManagedKernelPhase51ExternalStylesheetProof.ps1`; it builds the
managed payload and Gate 4 image, runs the Phase 11 fresh-boot harness, checks
the external-sheet and visual invariants, and requires at least three boots.

The authoritative evidence is in
`artifacts/phase51-final-evidence-6`. All three fresh dgram boots reached
`MANAGED_KERNEL_PHASE51_PASS`, loaded exactly two gzip external stylesheets,
parsed one inline stylesheet, resolved the target to external blue
(`0xFF0000FF`), completed layout/paint/raster, presented through GOP, and
passed the visible-page checks. The QEMU PPM screen hash was identical across
all three boots:

`5ABE0CBAD302CEB63906CC47AFBEEEFA83C9D1DF43356B63F19F8BD8B96985F9`

The fresh-boot harness also uses expected TCP tuple matching for each new
Phase 34 SYN, so late frames from a prior sequential stylesheet connection
cannot be mistaken for the next resource handshake. No CPU exception, page
fault, unexpected import call, or managed proof failure was present in the
three successful serial logs.

This phase intentionally does not claim JavaScript, media fetching, CSS
`@import`, selector expansion beyond the bounded CSS engine, or an unbounded
browser cache. Those remain outside the Phase 51 contract.

## Phase 51A acceptance closure

The acceptance-closure result is **Outcome B — core Phase 51 proven, acceptance
evidence incomplete**. The implementation remains bounded and the host suites
pass from the final source tree, but the required fresh guest matrix was not
completed in this environment.

The final-source NativeAOT build used the installed .NET 10.0.400 SDK fallback
and MSBuild 18.9.6. It produced a 4,683,264-byte payload with SHA-256
`F6730C6FA17E4C14E08E52D7E5110046BC16031809918530ABA67E4B3A444614`.
The build had one pre-existing CS0169 warning for `KernelLog.s_hexScratch` and
zero errors.

The host closure counts are Phase 47 = 955, Phase 48 = 397, Phase 49 = 694,
Phase 50 = 683, and Phase 51 = 1,083, for an aggregate of 3,812 cases. All
five suites passed. The Phase 51 source still permits two external resources in
the guest proof, reserves four external CSS records, limits href scratch to 512
scalars, CSS delivery to a 16,384-scalar stylesheet, and retains the fixed CSS
arena capacities documented above.
The guest proof also deliberately uses the existing 256-byte compatibility
entity/body bound and a 16 KiB decoded stylesheet bound; these are proof-fixture
limits, not a relaxed MIME or unbounded-body policy.

The wrong-MIME control uses the production DNS → TCP → TLS → HTTP response
metadata path and leaves the strict `text/css` gate intact. A completed
final-source boot is retained in
`artifacts/phase51a-final-wrong-mime-7`: it reached HTTP 200,
`text/html; charset=utf-8`, `ExternalStylesheetContentTypeRejected`, zero CSS
scalars/rules/declarations, `MANAGED_KERNEL_PHASE51_WRONG_MIME_CONTROL_PASS`,
and the Phase 14 accounting/health markers without a fault. The negative
harness marks this as a control result rather than a visible-page success.
However, the required 3/3 fresh negative guest runs were not completed because
the deterministic TLS fixture intermittently stalled before the response
boundary on subsequent boots. The negative harness includes only proof-fixture
changes: the wrong-MIME response, explicit acceptance markers, and early
negative teardown after MIME rejection; it does not weaken production policy.

The prior three-boot positive evidence remains in
`artifacts/phase51a-final-positive-2` and retains the complete two-resource
proof: external A → embedded style → external B, with B winning equal-specificity
`color: #0000FF` and external geometry affecting width, padding, and border.
Its payload was the immediately preceding closure build, not the final payload
hash above. A fresh positive run against the final payload was not completed
after the final negative-control propagation change, so that directory is
supporting evidence rather than final-authoritative evidence for Outcome A.

The production close path remains bounded polling with normal FIN teardown for
successful resources. The MIME-negative path terminates at metadata rejection
and uses the normal driver teardown/accounting path; it does not wait for the
successful-page FIN sequence. No Phase 47–50 fresh QEMU regression matrix was
completed during this closure, and the full guest successful-load/reset/
wrong-MIME reuse sequence therefore remains open even though host reset/cancel
coverage passes.

No Phase 52 work was started. Phase 51A should be reopened for the missing
fresh guest matrix and authoritative final-source positive/negative 3/3 runs
before Phase 52 begins.

## Phase 51D acceptance closure — 2026-09-06

The Phase 51D result is **Outcome B**. The Phase 48 proof contract was
reconstructed and the pre-DHCP report was isolated; no production networking
regression was demonstrated. The current-source Phase 48, 49, and 50 guest
proofs now reach their respective CSS, layout, and paint semantic markers, but
the fresh guest runs remain blocked at the first software-rasterizer call, so
the required current-source 3/3 guest closure is not claimed. No Phase 52 work
was started and Phase 52 is not yet safe to begin.

### Phase 48 reconstruction and boundary

The latest historical 3/3 Phase 48 evidence is
`artifacts/phase48-font-20260905-185234395/evidence-clean3`. Its metadata records
the current-source-independent historical payload as 3,475,968 bytes with
SHA-256
`94E7C59CCBAB5D6731CE0EFEC24FDB86ECAD57E2B9BCBD47869BDD03F73CE3D4`, the
1,574-byte decoded resource hash
`887E4B1A2DCD1E319D3D678ADF516A25F16C8CADEAE66E7559BDDFBCC138CD83`, and a
900-second timeout. Its serial hashes are
`E93C4D183098E9A064FA01429B5B8107346EBA3EF9E1F9FCC069AC96DA6FD725`,
`71EDF92F87F2C2C5C40FDC4816ADF3E09748A7BB2E1B38D96C8F80065CCAE126`, and
`007660AFE3DED23E097539A802D71FACAC14367169F8C999037BFAFA08D158E6`.

The historical invocation was `Build-ManagedKernel.ps1`, followed by a fresh
`Build-Gate4Harness.ps1` invocation with
`PayloadMode=ManagedKernel`, `Scenario=ManagedKernelPhase48`,
`EnableNativeAotStartup`, `EnableManagedKernelPhase48`, and
`AssumeUnspecifiedTimezoneUtc`. The boot wrapper used
`EnablePhase15Rx`, `EnablePhase48Protocol`, and `EnablePhase26VirtioRng` over
the dgram backend. QEMU used `-nic none`, `-netdev dgram`,
`-device e1000e,netdev=net0,addr=2`, and built-in virtio-rng. The OVMF code and
vars identity recorded by the evidence was
`33090CC07675BAA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A` and
`5D2AC383371B408398ACCEE7EC27C8C09EA5B74A0DE0CEEA6513388B15BE5D1E`.

The current Phase 48 wrapper has the same explicit network prerequisites,
packet mode, NIC, serial mode, framebuffer mode, OVMF, and timeout. It has no
separate DHCP-enable switch, packet-filter dump, or optional PCAP gate; DHCP is
entered by the managed IPv4 path after UDP registration. The exact current
pre-DHCP diagnostic is
`artifacts/phase51d-phase48-final-20260906`, payload
`D4628B016EAD96A6260AAB1B374366C980F7909C593C8AD045383BC88FEA22A6`,
4,686,848 bytes. A fresh Gate 4 staging check passed for that payload, but the
boot stopped at `MANAGED_KERNEL_PHASE48_STARTING` followed by
`MANAGED_E1000_RX_FRAME_OK`.

At that exact boundary: DHCP client startup did not reach DISCOVER emission;
the fixture observed no DISCOVER, injected no OFFER, the guest received no
OFFER, and neither IPv4/UDP nor the DHCP parser/state machine received one.
The fixture was already bound and marked ready. The valid known-good packet
contract was independently checked: DISCOVER was 292 bytes, transaction ID
`0x00000001`, UDP 68→67, guest MAC/chaddr `525400123456`; OFFER was 310 bytes,
UDP 67→68, server `0A0F0002`, yiaddr `0A0F002A`, the same chaddr, DHCP type 2,
valid IPv4/UDP checksums, mask `FFFFFF00`, DNS `0A0F0002`, and lease
`0xE10`.

The generic-runner audit found that Phase 48 enters the modern HTTPS helper,
which owns DHCP, DNS, TCP, TLS, and HTTPS. The legacy Phase 11 serial wait and
Phase 14 accounting wait are not consumed as Phase 48 proof prerequisites;
Phase 15 RX is required, the legacy Phase 16 ARP wait is skipped for modern
modes, and graphics waits occur only after the network/resource proof. The
same modern path is used by Phase 51. The audit also found and corrected a
Phase 50 marker fall-through: the runner and `ManagedIpv4Layer` previously
waited/emitted Phase 49 configuration markers when Phase 50 was selected.

The deeper A/B evidence isolated the old apparent network stall to eager CSS
arena initialization in the Phase 43 proof constructor, before UDP endpoint
registration. The minimal evidenced correction is ordinary managed CSS-engine
construction for this proof path. With that correction, current-source Phase
48 reaches DHCP complete, HTTPS body receipt, CSS tree/engine creation, layout,
paint, and both semantic proofs. The latest isolated witness is
`artifacts/phase51d-phase48-raster-witness3-20260906`; it reaches
`MANAGED_HTTPS_PHASE48_RASTER_BEGIN=0x3B` and stops inside the first
`ManagedSoftwareRasterizer.TryRender` call after 59 paint commands. This is a
post-network rendering/runtime boundary, not a DHCP or font-policy defect.
The current-source Phase 48 build-only payload was also rebuilt from the
historical wrapper configuration in
`artifacts/phase51d-phase48-final-build-20260906` as 4,688,896 bytes with
SHA-256 `CB4F9D9520B4E3C51B1D3B43BBC2A024FCC7B14A36552A1518FB9C7E73C2C9C9`.

### Phase 49 and Phase 50 regressions

The current-source Phase 49 evidence is
`artifacts/phase51d-phase49-final-20260906-a`, payload
`8B21D5CB276577FADEC04137BCE5EBEA75400F51BEF57EE62A169C50B848A680`,
4,686,848 bytes. It reaches DHCP, resource body, CSS, layout, paint, fixed
scroll, and nested opacity proofs, but does not reach raster/GOP/PASS. No
current-source screenshot or pixel hash is therefore claimed. Historical Phase
49 3/3 evidence remains `artifacts/phase49-qemu-regression-final4` with payload
`2731DF0248FCDC25EF5D9E83A67338008735224FED024AF8C47BBD37442C7BDF` and
deterministic screenshot pixel hash
`42B515730AE5A3549341B7DCB19FD09265202C15A5FA0312A8D61A71EE3EDF91`.

The current-source Phase 50 evidence is
`artifacts/phase51d-phase50-final-20260906-b`, payload
`DD2C68962A755D2F1A410CA1F770A782060E112DDB75A8F67FC039273DA542AA`,
4,688,896 bytes. It reaches the corrected Phase 50 DHCP/configuration
markers, resource body, CSS, layout, paint, fixed scroll, and nested opacity
proofs, then times out waiting for `RESOURCE_COMPLETE`; no screenshot/GOP
PASS is claimed. Historical Phase 50 3/3 evidence remains
`artifacts/phase50-qemu-proof-final6`, payload
`2731DF0248FCDC25EF5D9E83A67338008735224FED024AF8C47BBD37442C7BDF`, with
screen hash `4359B06D9037BD3C25B9638F4810E8032517A8970F33736957B09DFEB36C9789`.

### Phase 51 positive, wrong-MIME, and reset/reuse

The already accepted positive 3/3 proof is
`artifacts/phase51-final-evidence-6`; the supporting final-source stabilization
positive is `artifacts/phase51b-final-positive-2` with its fresh Gate 4 pair in
`artifacts/phase51b-final-gate-positive`. Its staged payload is 4,683,264 bytes
with SHA-256
`3543C874452C45D0F06B069A02BED2B41F623B22319445DA8FE8811141520513`.
The wrong-MIME 3/3 proof is
`artifacts/phase51b-stabilization-final-wrong-1` with the matching negative
Gate 4 contract in `artifacts/phase51b-final-gate-wrong-mime`; it reaches HTTP
200 with `text/html; charset=utf-8`, rejects with
`ExternalStylesheetContentTypeRejected`, and records zero scalars, rules, and
declarations on all three boots.

The new reset/reuse proof is **PASS 1/1** in
`artifacts/phase51d-reset-reuse-final-20260906-e`. Its payload is 4,688,896
bytes with SHA-256
`DD2C68962A755D2F1A410CA1F770A782060E112DDB75A8F67FC039273DA542AA`.
The sequence was wrong-MIME HTTP 200 → strict MIME rejection → zero CSS state
→ `Reset()` → reset-state assertions → fresh DNS/TCP/TLS → valid document →
stylesheet A → embedded style → stylesheet B → cascade → layout → paint →
GOP presentation → visible page/resource PASS. Reset assertions recorded
terminal, failure, cancellation, current resource, and current URL cleared;
source cursor and stylesheet index zero; rules/declarations zero; style,
layout, paint, and framebuffer hashes invalid; presentation incomplete; and
network ownership released. The wrong-MIME terminal marker and the valid-page
PASS markers are present in the same fresh boot.

The accepted Phase 51 source-order telemetry remains: source node 3 external
A, node 4 embedded style, node 6 external B; one embedded stylesheet parsed;
external B wins the equal-specificity `color` property with target color
`0xFF0000FF` and supplies the winning geometry. External A recorded status 200,
MIME/charset/charset-source `3/1/1`, gzip encoding `2`, 125 encoded bytes, 140
decoded scalars, 1 rule, and 7 declarations. External B recorded the same
metadata, 175 encoded bytes, 222 decoded scalars, 1 rule, and 11 declarations.
The document, style, layout, paint, framebuffer, and physical-destination
hashes are respectively:

`9B20FEC68FEFB0EF49A4ED91C8D77BEA16C35613A3CCAD376AA2F0E697B46107`

`A7C26D4AE91334D2FA89018254A027E9F827678C21EF36531FFD52716D6197B5`

`A9653C355B93AE8DFBC6B5C12D13CC067CBC81E74624D70C79B7B5849E857A13`

`643178D94EFCED13ACBD1647409D244653FB19C7FD9C22442FD09F5FD823B6F8`

`4A0BE12669919BB7D3309AC9F11513706E61C5818076C6AB9908A96721FEA0C5`

`0FCBB759E9EA8166D6CC8F8976025EE933A82D21BBE8D897B8894E5805D9E862`.

No display-list hash was emitted by the Phase 51 guest proof. The physical
destination hash matched the source framebuffer (`SOURCE_UNCHANGED=1`). The
screen was 1280×800, with 57,600 presented pixels and 12 mapped external-CSS
sample points in the presentation region; the raw PPM hash was
`5ABE0CBAD302CEB63906CC47AFBEEEFA83C9D1DF43356B63F19F8BD8B96985F9` and the
RGB pixel-stream hash was
`B26DD5EDED47E5A870AE0E91D938511AE08F5A7093010A03AE3BBE7A22C40AB8`.
The font semantic hash was the host/guest Phase 48/50 value
`4184C857A49DABEF9ED19BA97EB0DBF87879EAD873F835336149BADB0F7D094A`.

### Host and tool closure

Fresh host closure from
`artifacts/phase51d-host-final-20260906` passed Phase 47 = 955, Phase 48 =
397, Phase 49 = 694, Phase 50 = 683, and Phase 51 = 1,083, for an arithmetic
aggregate of **3,812**. The Phase 50 host raster result was 15 glyph requests,
15 glyphs rendered, 1 fallback, 25 non-ASCII requests, 256 pixels written,
and framebuffer hash
`438115B73294E23C0A6FC33051B8B421AB000FA3ADC84681308C70526FEEAE61`.

The requested SDK is 10.0.302, which is not installed. The selected fallback
is .NET SDK 10.0.400 with MSBuild 18.9.6 through the repository build wrapper.
The NativeAOT build emitted zero errors and the known warning is CS0169 for
`KernelLog.s_hexScratch`; the host Phase 51 build reports that warning and no
errors. `git diff --check` passed. PowerShell parser checks passed for
`tools/Build-Gate4Harness.ps1`,
`tools/Run-ManagedKernelPhase11FreshBoots.ps1`, and
`tools/Run-ManagedKernelPhase51ResetReuseProof.ps1`. Final source scans found
no temporary witness/one-shot instrumentation; the remaining
`PHASE47_RASTER_BEGIN` line is a permanent proof marker. The evidence OVMF
identities remained code
`33090CC07675BAA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A` and vars
`5D2AC383371B408398ACCEE7EC27C8C09EA5B74A0DE0CEEA6513388B15BE5D1E`.

The source tree has no DHCP, checksum, MIME, TLS-validation, CSS source-order,
or streaming-memory weakening. Production limits remain bounded CSS, bounded
stylesheet sources, bounded resource decoding, and bounded raster arenas;
proof-only limits remain the fixed Phase 48–51 fixture sizes and the bounded
font/Unicode fixtures. No repository-owned QEMU remains after cleanup; any
remaining QEMU process is unrelated to this repository and is reported by the
final process inventory.
