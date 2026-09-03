# Managed Kernel Phase 44: bounded CSS parsing, selector matching, and cascade

Phase 44 adds a bounded CSS layer over the retained Phase 43
`ManagedHtmlDocument`. It parses the document's direct `<style>` text children,
discovers external stylesheet links as metadata, parses inline `style`
attributes, matches the supported selector subset, applies the cascade, and
stores one fixed-size computed-style record per element. It deliberately stops
before layout, painting, resource fetching, navigation, DOM mutation, or CSSOM.

## Architecture and source boundaries

`ManagedCssEngine` owns fixed arrays of packed records and scalar scratch
buffers. It does not create a managed selector/declaration object graph and it
does not use `string`, `List<T>`, `Dictionary<TKey,TValue>`, or unbounded CSS
source aggregation.

The three source paths are intentionally separate:

| Source | Phase 44 behavior |
| --- | --- |
| Embedded `<style>` | Reads the direct style element's text children incrementally from the Phase 43 scalar arena. Multiple text siblings are accepted. |
| Inline `style` attribute | Reads the attribute-value scalar slice through the document API and creates an inline-origin declaration slice. |
| External `<link>` | Recognizes a `stylesheet` rel token, including `alternate stylesheet`, and records the link handle/href. It does not fetch or parse the remote sheet. |

The public entry points are `TryStyle`, `TryParseStylesheet`,
`TryGetComputedStyle`, `TryCopyCanonicalStyleHash`, and
`TryGetExternalStylesheet`. `Reset` permits deterministic restyling with the
same arenas.

## Capacity contract

The default engine used by the guest proof has the following capacities. The
maximum column is the constructor-validated ceiling, not an allocation made by
the default proof.

| Arena | Default | Maximum |
| --- | ---: | ---: |
| Stylesheets | 8 | 64 |
| Rules | 256 | 2,048 |
| Selector headers | 512 | 4,096 |
| Selector steps | 1,024 | 8,192 |
| Declarations | 1,024 | 16,384 |
| Computed styles | 1,024 | 4,096 |
| Selector name scalars | 16,384 | 16,384 |
| External link records | 16 | 16 |

Per-selector limits are eight steps, eight classes per step, two attribute
selectors per step, eight selector groups per rule, 64 scalars per selector
name, 256 scalars per selector/value scratch slice, and 64 declarations per
rule. All fixed-array exhaustion paths have a typed failure reason and are
observable through `ManagedCssTelemetry`.

`ManagedCssParseFailureReason` distinguishes stylesheet, rule, selector,
declaration, external-link, computed-style, and traversal capacity failures
from malformed input and unsupported complexity. The Phase 44 negative proof
sets the effective rule limit to one on the preallocated default engine and
requires `RuleCapacityExceeded`; the positive proof uses the same engine shape
with its normal 256-rule limit. This avoids late guest-heap allocation and does
not introduce a second CSS arena.

## Selector grammar

The supported selector subset is:

- universal and HTML element selectors;
- `#id` and `.class` compounds, including multiple classes;
- comma-separated selector groups;
- descendant whitespace and child `>` combinators;
- `[name]` and `[name=value]` attribute selectors;
- `:root`, `:first-child`, and `:last-child`.

Matching walks the Phase 43 parent/sibling/attribute slices with bounded
scratch state. `+` and `~` sibling combinators are recognized as recoverable
unsupported selectors and are skipped with telemetry; they cannot consume
unbounded work or corrupt the remaining stylesheet. Selector specificity is
stored as the bounded `(id, class/pseudo/attribute, type)` tuple and source
order is stored explicitly.

## Values and computed styles

The typed property set covers display and visibility, colors and backgrounds,
font size/weight/style, text alignment and whitespace, width/height/min/max
constraints, four-sided margin and padding, border width/style/color,
positioning offsets, overflow, opacity, and z-index. `ManagedCssLength` stores
an integer fixed-point value and a unit (`px`, `%`, `em`, `rem`, or `auto`);
for example, `20px` is stored as 2,000 and `50%` as 5,000. Colors are packed
ARGB values, so `#1234` becomes `0x44112233`.

The parser accepts the bounded keyword/value forms needed by those properties,
including `inherit`, `initial`, `auto`, common display/position/overflow/font
keywords, hexadecimal colors, `transparent`, and a small named-color set.
Margin and padding shorthands expand into four declarations. `!important`
is retained on the declaration record rather than reparsed during matching.
Unknown properties, custom properties, malformed declarations, malformed rules,
overlong values, and unsupported selectors are recoverable and counted; a
capacity or invalid-document failure stops styling deterministically.

The cascade order is importance, origin, specificity, and source/declaration
order. Inline normal declarations override author normal declarations; author
important declarations override inline normal declarations; inline important
declarations have the highest supported origin precedence. The selected
inherited properties are color, font size, font weight, font style, text align,
visibility, and whitespace. UA defaults provide deterministic display values
for common HTML elements, with `head`, `style`, `script`, `meta`, `link`, and
`title` defaulting to `display:none`.

`ManagedComputedStyle` is a packed, fixed record. The host size probe reports:

```text
computed=168 length=5 stylesheet=16 rule=20 selector=16 step=26
declaration=16 value=12 handle=8 candidate=28
```

The canonical style hash walks elements in document order and hashes the node
index, all supported computed fields, units, specified/inherited/important
masks, and the same property order on every run. It is a compact restyle
regression signal, not a layout or paint hash.

## Memory accounting

Using the default capacities and the measured record sizes, the CSS engine's
array-element payload is 371,344 bytes (362.64 KiB), excluding managed array
headers, object headers, and the SHA-256 helper. The Phase 43 document host
probe reports 449,024 bytes of persistent document arena storage. Their
combined managed payload accounting is therefore 820,368 bytes (801.14 KiB),
before transient TLS/HTTP/gzip state and runtime overhead.

That CSS total accounts for the fixed stylesheet, rule, selector, selector-step,
class, attribute-selector, declaration, inline, external-link, computed-style,
matched-rule, selector-name, cascade-winner, match-state/visited, parser-scratch,
and hash buffers. No CSS collection grows with source size. For a conservative
active-pipeline estimate, adding the measured Phase 39 HTTPS staging
approximation (9,408 bytes, excluding lower TLS/network state) and the Phase 44
logical decompression/text/tokenizer peaks (582 + 1 + 128 bytes) gives about
830,487 bytes (811.02 KiB) of accounted document/style/network working storage;
this is a bounded accounting estimate, not guest process RSS.

At the constructor maxima, the corresponding fixed-array element payload is
1,745,424 bytes (1.665 MiB), again excluding headers and runtime overhead.
The guest proof emits protocol-buffer peaks separately. In the final positive
boot, the run-3 markers report HTTP 0, decompression 582, text 1, and tokenizer
text 128; these are logical bounded-buffer peaks, not a process-RSS claim.

## NativeAOT startup seam

The guest now primes the HTML tokenizer and one default CSS arena before the
E1000 driver starts. The custom NativeAOT/QEMU environment exposed late
construction of these larger managed arrays as a startup/runtime hazard, so
Phase 43/44 stage setup performs the allocation while the normal provider and
DMA preconditions are still active. The CSS capacity negative mode rebinds and
resets the preallocated engine and changes only its effective rule limit.

The Gate 4 harness also treats `ManagedKernelPhase26` as the explicit Phase 26
startup scenario. Later phase scenarios no longer inject the Phase 26 proof at
loader startup; their managed stage starts the already-installed RNG provider
directly. This keeps the Phase 44 route independent of the existing Phase 26
`RaiseFailFastException` import-boundary proof while preserving the explicit
Phase 26 scenario.

## Navigator donor audit

The relevant donor behavior was reviewed in
`D:\dev\guideXOSServerV0.5_DEVELOPER_STUDIO`, especially
`guide_web_html_parser.cpp/.h`, `guide_web_document.h`, and
`navigator_html_parser.cpp/.h`. The useful semantic references are selector
components, specificity, pseudo/structural checks, declaration ownership, and
style diagnostics. The donor's `std::string`/`std::vector` storage, per-node
`WebStyle` graph, arbitrary source aggregation, and layout-facing/render
structures are not compatible with this bounded NativeAOT guest seam. Phase 44
keeps those concerns outside the parser/matcher and exposes only scalar slices,
handles, packed records, computed styles, telemetry, and a canonical hash.

## Verification

Host commands and observed results:

| Command | Result |
| --- | --- |
| `ManagedKernelPhase22HostTests` | `PASS cases=56` |
| `ManagedKernelPhase23HostTests` | `PASS cases=60` |
| `ManagedKernelPhase25HostTests` | `PASS cases=113` |
| `ManagedKernelPhase26HostTests` | `PASS cases=70` |
| `ManagedKernelPhase27HostTests` | `PASS cases=100` |
| `ManagedKernelPhase28HostTests` | `PASS cases=188` |
| `ManagedKernelPhase29HostTests` | `PASS cases=209` |
| `ManagedKernelPhase30HostTests` | `PASS cases=91` |
| `ManagedKernelPhase31HostTests` | `PASS cases=33` |
| `ManagedKernelPhase32HostTests` | `PASS cases=69` |
| `ManagedKernelPhase33HostTests` | `PASS cases=185` |
| `ManagedKernelPhase34HostTests` | `PASS cases=140` |
| `ManagedKernelPhase35HostTests` | `PASS cases=6` |
| `ManagedKernelPhase36HostTests` | `PASS cases=72` |
| `ManagedKernelPhase37HostTests` | `PASS cases=34` |
| `ManagedKernelPhase38HostTests` | `PASS cases=1475` |
| `ManagedKernelPhase39HostTests` | `PASS cases=301` |
| `tools\Run-ManagedKernelPhase40HostTests.ps1` | `PASS cases=4347` |
| `tools\Run-ManagedKernelPhase41HostTests.ps1` | `PASS cases=5466` |
| `tools\Run-ManagedKernelPhase42HostTests.ps1` | `PASS cases=205` |
| `tools\Run-ManagedKernelPhase43HostTests.ps1` | `PASS cases=31` |
| `tools\Run-ManagedKernelPhase44HostTests.ps1` | `PASS cases=66` |

The Phase 44 host groups cover basic cascade/inheritance, selector coverage,
attribute and structural selectors, typed lengths/colors/keywords, shorthands,
important and inline precedence, malformed/unknown/unsupported recovery,
external stylesheet discovery and handle validation, capacity boundaries,
canonical restyling, and computed record sizes.

The deterministic guest fixture is 582 decoded UTF-8 bytes (`0x246`), 350 gzip
bytes (`0x15E`), 38 tokens (`0x26`), 24 nodes / 16 elements, 1 stylesheet / 7
rules, 18 declarations, 8 selector matches, 3 inline styles, 1 important
declaration, and 97 inherited assignments (`0x61`).

The final positive proof is preserved at
`artifacts\phase44-css-proof-final`, with 3/3 fresh QEMU boots and
`PASS_PHASE44` on every run. The final CSS capacity proof is preserved at
`artifacts\phase44-css-capacity-final`, with 3/3
`NEGATIVE_PASS_PHASE44` boots and `RuleCapacityExceeded` (`0x2`) on every run.
Both runs contain the CSS begin/tree-validated/engine-created markers and no
CPU exception, page-fault, or unexpected-import markers.

The standalone final NativeAOT artifact is
`artifacts\phase44-nativeaot-final\publish\gxos-managed-kernel.dll`, size
2,239,488 bytes, SHA-256
`7A0C969D90BEABF7A9F3385D24A57CC555545686A28C63B6CF4A4C60C489CABC`.
The positive fixture SHA-256 is
`001658A9F4D22543D619AC77E4172BCEE79FDB8810BB4BB3FC6035B1867C1F70`.
The run-3 positive style hash is
`A2EDFC7F98E6AEA58A006481A8595B04BB15A7DDF2FE36F9D5BE9CE7FF496205`.
The QEMU firmware identity used by the final runs is code SHA-256
`33090CC07675BAA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A` and
vars SHA-256
`5D2AC383371B408398ACCEE7EC27C8C09EA5B74A0DE0CEEA6513388B15BE5D1E`.

The Phase 43 regression was also completed during this continuation:
`artifacts\phase43-document-final-20260902` contains 3/3 positive boots and
`artifacts\phase43-node-capacity-final-20260902` contains 3/3 node-capacity
negative boots. The Phase 43 runner was corrected to use the actual fixture
length/hash and current body handle (`0xC`); the guest metrics and canonical
document hash remain unchanged.

The deferred Phase 43 guest gap is therefore closed. QEMU was found at
`C:\Program Files\qemu\qemu-system-x86_64.exe` during preflight. Fresh
three-boot regression evidence using the final payload is preserved at
`artifacts\phase39-resource-final-20260902`,
`artifacts\phase40-resource-final-20260902`,
`artifacts\phase41-text-final-20260902`, and
`artifacts\phase42-html-final-20260902`; each summary reports `RUNS=3` and
`BOOT_SUMMARY=PASS`. Because the shared IPv4/Ethernet/E1000 and Gate 4 routing
changed, fresh Phase 33 positive/negative and Phase 34 positive/negative
three-boot controls are also preserved at
`artifacts\phase33-positive-final-20260902`,
`artifacts\phase33-negative-final-20260902`,
`artifacts\phase34-positive-final-20260902`, and
`artifacts\phase34-negative-final-20260902`.

The fresh regression summaries report: Phase 39, 16,884-byte resource,
`0284CD23ED354023F0363678794905B285C104A2056189B36C23C0689924454F`; Phase
40, 16,384 decoded bytes, `gzip`; Phase 41, 10,496 decoded bytes and 7,680
text scalars; and Phase 42, 1,894 decoded bytes, 52 tokens, and token hash
`15967F70BB89C5AC00D73E4D4D73B057F6C48E670F3EE789C0A0B945705605B6`.
Each is `PASS` for all three boots and each serial log is fault-free.

The current authoritative host aggregate is 13,317 cases across the 22
available Phase 22–44 projects (Phase 24 has no project):
56 + 60 + 113 + 70 + 100 + 188 + 209 + 91 + 33 + 69 + 185 + 140 + 6 +
72 + 34 + 1,475 + 301 + 4,347 + 5,466 + 205 + 31 + 66. This is the
executed current-suite arithmetic, not the earlier historical aggregate.

## Deliberate limitations and next phase

The implementation does not fetch external CSS, implement CSS variables,
`calc()`, media queries, animations, layout/painting, CSSOM mutation, or
advanced pseudo-classes. Adjacent and general sibling combinators remain
recoverable unsupported cases. A natural Phase 45 boundary is either external
stylesheet streaming into the same source arena or a layout adapter consuming
`ManagedComputedStyle`; either can be added without turning the bounded CSS
records into a general-purpose browser object graph.
