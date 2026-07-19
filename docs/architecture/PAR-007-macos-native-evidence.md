# PAR-007 macOS native evidence

**Status:** Pending native run  
**Target:** Apple-silicon macOS, osx-arm64  
**Runtime workflow:** .github/workflows/macos-release.yml
**Distribution workflow:** .github/workflows/macos-distribution.yml
**Evidence artifact:** Pathstitch-native-evidence-osx-arm64

Source-level implementation and Windows-hosted verification are complete: the evidence collector, independent reviewer, packaged app-group writer/collector, and real-extension runtime attestation paths parse or build cleanly; warnings-as-errors build passes with 0 warnings/errors; focused native-evidence/delivery/document contracts pass 46/46; full .NET suite passes 1156/1156; unit/import/export contracts pass 80/80 plus Python 7/7; and packaged OCCT mesh/import/unfold tests pass 23/23. The executable evidence-validator suite passes 33/33, including thirty-one corrupted, tampered, identity-mismatched, fallback, or preference-mismatched bundles that cannot report passed. This document must not be changed to Passed until a macOS run from the exact reviewed commit completes and its self-contained evidence artifact is retained.

## Native gates

The macOS workflow must prove all following against packaged Pathstitch.app:

1. Bundle, Swift bridge, static ICNS plus runtime icon resources, Quick Look extensions, packaged Node, and packaged Python/OCCT runtime exist and are arm64 where executable; Swift binaries declare the macOS 13 deployment floor.
2. The app and extensions pass strict code-sign verification, and every nested Mach-O is recorded. App-group entitlement exists on all three bundles; extensions also carry sandbox entitlement. The evidence validator checks the verification transcript and entitlement contents.
3. Real Avalonia window initializes native WebView and native open/save storage provider.
4. Packaged Swift bridge applies and reads back independent Finder preview values, restores and reads back all formats enabled, and applies Light, Dark, and Automatic Dock icons.
5. Packaged OCCT imports STEP, OBJ, and STL; validates two-triangle mesh topology; combines STEP + OBJ + STL into four bodies; reimports the mixed result; projects OBJ; unfolds connected and separate nets; forces a manual seam; emits glue-tab and sewing-hole decorations; and proves every retained DXF has exactly one HEADER $INSUNITS=4 plus exactly one HEADER $MEASUREMENT=1. The collector independently parses all copied .dxf files using literal protocol values.
6. STCH save/reopen preserves viewport JSON, serialized STEP topology, embedded source bytes, and generated DXF bytes. The evidence validator opens project.json, decodes and unit-validates dxfDataBase64, then requires its SHA-256 to match the external STEP unfold plus both round-trip hashes.
7. Running app receives the exact Finder file-activation fixture and navigates to the editor.
8. Finder registers both extensions; thumbnail path produces exactly one attributable, non-trivial STCH, DXF, and STEP image. Each map entry matches PNG size/SHA-256, all outputs are unique, and all qlmanage logs are retained. Separate `qlmanage -p` execution drives real preview extension with retained STEP. Preview attestation must prove expected bundle/process identity, exact fixture hash, interactive SceneKit view installation, non-fallback mesh, positive vertex/triangle counts, and `allowsCameraControl=true`; thumbnail attestation proves real thumbnail extension rendering.
9. Schema-5 collection is executable and fail-closed. Packaged app writes nonce plus nondefault `dxf=false, step=true, stch=false` values into app-group defaults; separate preview/thumbnail extension processes read them and atomically attest inside app-group container; second app process collects both. Collector binds nonce, exact preference vector, distinct process identities, bundle IDs, retained STEP hash, SceneKit/camera state, and attestation descriptors. Manifest also records exact commit/run/runner, readable package ZIP hash, size/SHA-256 inventory, dxfUnitEvidence, and embeddedProjectDxfEvidence.

PAR-012 changed the packaged Python/DXF unit contract, so native evidence must come from a post-PAR-012 exact commit; earlier artifacts are stale.

Runtime-probe source proves cross-process app-group access only when packaged app and real sandboxed extensions successfully exchange nonce-bound evidence. Ad-hoc signing may fail that gate; Developer ID execution remains required for release-authority sign-off. Apple documents com.apple.security.application-groups as unrestricted for macOS Developer ID distribution, so separate provisioning profiles are not required solely for this entitlement. Source presence alone is not proof: exact native artifact must contain passed writer, preview, thumbnail, and collector records.

## Required evidence files

- native-evidence-manifest.json
- packaged-app-acceptance.json
- file-activation-acceptance.json
- codesign-details.txt
- codesign-verification.txt
- nested-codesign-details.txt
- app-entitlements.plist
- PathstitchQuickLook-entitlements.plist
- PathstitchThumbnail-entitlements.plist
- pluginkit-inventory.txt
- quicklook-output-map.json
- sample-dxf-qlmanage.log
- sample-step-qlmanage.log
- sample-stch-qlmanage.log
- Exactly three mapped Quick Look PNGs covering .dxf, .step, and .stch
- packaged-input.step
- packaged-input.obj
- packaged-input.stl
- packaged-roundtrip.stch
- packaged-projection.dxf
- packaged-unfold.dxf
- packaged-mixed.step
- packaged-obj-projection.dxf
- packaged-obj-connected.dxf
- packaged-obj-separate.dxf
- packaged-obj-tabs.dxf
- packaged-obj-holes.dxf
- Pathstitch-osx-arm64.zip
- app-group-writer-probe.json
- app-group-collector-probe.json
- quicklook-preview-runtime-probe.json
- quicklook-thumbnail-runtime-probe.json
- runtime-probe.step

packaged-app-acceptance.json must use schema 4 and report status: passed; native WebView navigation; native file dialogs; mac integration; STEP import/projection/unfold; exact project preservation; OBJ/STL topology; four-body mixed import; connected/separate net generation; manual seam behavior; and tab/hole decoration. Every retained input/output descriptor must match the copied artifact's size and SHA-256; every DXF descriptor must also report INSUNITS code 4 and MEASUREMENT code 1. Both ZIP and STCH archives must be readable, and the embedded generated DXF must match the external unfold hash.

## Distribution candidate gate

Manual .github/workflows/macos-distribution.yml runs only from main through protected macos-distribution environment. It imports an exact Developer ID Application certificate into temporary keychain, requires secure timestamp and hardened runtime, submits with notarytool, retains submission and log JSON, staples ticket, runs strict code-sign verification plus Gatekeeper assessment, recreates readable ZIP, and uploads hash-bound schema-1 distribution evidence for 90 days. Temporary certificate/keychain cleanup runs even after failure.

Distribution evidence is distinct from schema-5 runtime evidence. Neither substitutes for the other: release sign-off needs exact native runtime artifact plus independent receipt, while distribution candidate additionally needs accepted Developer ID/notarization artifact.

## Independent review

Download exact workflow artifact into dedicated directory, then bind it to commit and run selected independently from GitHub UI or reviewed commit:

```powershell
gh run download 8675309 `
  --name Pathstitch-native-evidence-osx-arm64 `
  --dir artifacts/native-review/evidence
./scripts/verify-native-macos-evidence.ps1 `
  -EvidenceRoot artifacts/native-review/evidence `
  -ExpectedCommit 0123456789abcdef0123456789abcdef01234567 `
  -ExpectedWorkflowRunId 8675309 `
  -ExpectedWorkflowRunAttempt 2 `
  -ReceiptPath artifacts/native-review/native-evidence-review.json
```

Reviewer leaves producer evidence unchanged, requires schema 5, verifies exact file-set/size/SHA-256 inventory, rejects commit/run mismatches, copies evidence to isolated temporary storage, and re-runs DXF/STCH plus nonce-bound runtime/app-group semantic checks. Receipt binds producer and revalidated manifest hashes. Store receipt with sign-off. Portable review proves retained artifact integrity and authenticates native runtime records already captured; it cannot create missing native proof. Sign-off still requires exact native run under signing authority appropriate to release.

## Sign-off record

Fill from one exact workflow run:

- Commit:
- Workflow run URL:
- Run ID / attempt:
- Runner image:
- GitHub artifact digest:
- Reviewer receipt SHA-256:
- Package ZIP SHA-256:
- Package acceptance: Pending
- Finder activation: Pending
- STCH Quick Look: Pending
- DXF Quick Look: Pending
- STEP Quick Look: Pending
- Preview-extension runtime: Pending
- Developer ID-authorized cross-process app-group read: Pending
- Code-sign verification: Pending
- Distribution notarization: Not exercised by ad-hoc CI
- Reviewer:
- Date:

Developer ID/notarization evidence remains required only for a distribution candidate. Ad-hoc CI proves bundle sealing, nested signature shape, and runtime behavior when platform permits it; it does not prove Developer ID release authority, notarization, stapling, or Gatekeeper approval.
