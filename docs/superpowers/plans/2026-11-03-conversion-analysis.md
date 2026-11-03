# Conversion analysis implementation plan

> Execute inline with superpowers:executing-plans; retain the existing checkout and pending movement fix.

**Goal:** Separate viewport dimensions, resource directory IDs and logical coordinates; validate output references and prevent repeated conversion.

**Approved specification:** The user requested the proposed unified conversion information, conservative expression diagnostics, reference checks and repeat-conversion protection. Preserve resource folder names, existing supported formats, original encodings and atomic output publication. No arbitrary Lua execution. No real commercial sample names in public evidence.

**Architecture:** Preflight reads archive indices and relevant small scripts, applying archive/loose precedence once to build a shared profile. The existing processors consume that profile. Output reports contain relative paths and reasons; every output archive carries a conversion marker. Uncertain references warn instead of being guessed.

**Tech stack:** C# / .NET 10, existing token lexer and PFS codec, xUnit.

## Tasks
- [x] Add preflight/profile and cross-archive tests. Include source/target resolution, logical-coordinate detection, original resource directory IDs and complete resource index. Preserve mixed/unknown cases with diagnostics.
- [x] Feed profile into Lua/AST/ASB/CSV processing; keep image and media ratio consistent with the run. Test split scripts, sidecars, comments, literals and idempotent path repair.
- [x] Add typed warnings and output reference audit for literal/recognized path expressions, incomplete dynamic paths, platform selectors and preserved geometry. Do not claim complete Lua reachability analysis.
- [x] Add conversion manifests to loose output and each archive; reject converted input before mutation, including archives copied without loose metadata. Test failure cleanup and original input preservation.
- [x] Run core and UI regression suites; inspect actual external script samples read-only; document limits in both READMEs and publish a local Windows build.

## Review focus
- A patch archive overrides a script/profile from the base archive; loose files override both.
- An image and coordinate CSV are in separate archives.
- Dynamic paths contain localization, magic paths or unknown expressions; preserve and report.
- Text-only/images-only runs and unknown source dimensions do not fabricate a profile.
- Invalid/cancelled conversion never leaves a completed marker in an old output or modifies the source.

## Verification

Core: 481 passed. UI: 170 passed. NativeAOT win-x64 published and exercised with a private script-only fixture; conversion succeeded, the repeated-conversion attempt was rejected before creating output. Three external script-subset probes detected 1280 resource IDs and 960 logical bases independently of a 960x540 target. This is not a full-game runtime validation. Fresh review found two issues (numeric overlay precedence and case-insensitive rename lookup); both gained failing regression tests and were fixed. No commit or push performed.
