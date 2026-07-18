# macOS delivery

Pathstitch publishes an Apple-silicon, self-contained `.app` with no global .NET, Node, npm, or Python requirement.

## Development bundle

On an Apple-silicon Mac with .NET 10 and PowerShell 7:

```powershell
./scripts/package-avalonia-macos.ps1 -SigningIdentity "-"
```

This creates `artifacts/macos/Pathstitch.app` and `Pathstitch-osx-arm64.zip`. The `-` identity applies an ad-hoc signature suitable for local development. Gatekeeper distribution requires Developer ID signing and notarization.

## Signed and notarized bundle

For local use, import a Developer ID Application certificate into current keychain and provide Apple notarization credentials:

```powershell
$env:APPLE_ID = "build@example.com"
$env:APPLE_TEAM_ID = "TEAMID"
$env:APPLE_APP_SPECIFIC_PASSWORD = "app-specific-password"
./scripts/package-avalonia-macos.ps1 `
  -SigningIdentity "Developer ID Application: Example (TEAMID)" `
  -Notarize
```

Script rejects non-Developer-ID identities, team mismatch, missing credentials, missing secure timestamp, absent hardened-runtime flag, rejected notarization, failed stapling, and failed Gatekeeper assessment. It retains notarytool submission/log JSON, code-sign details/verification, Gatekeeper transcript, and hash-bound distribution-evidence.json, then recreates and readability-checks stapled ZIP.

Protected CI path is .github/workflows/macos-distribution.yml. It is manual, main-branch-only, uses protected macos-distribution environment, imports certificate into temporary keychain, and always removes keychain/P12. Configure:

- Environment variable: MACOS_SIGNING_IDENTITY
- Environment secrets: MACOS_DEVELOPER_ID_P12_BASE64, MACOS_DEVELOPER_ID_P12_PASSWORD, APPLE_ID, APPLE_TEAM_ID, APPLE_APP_SPECIFIC_PASSWORD

Default .github/workflows/macos-release.yml remains ad-hoc and secret-free. It proves bundle sealing and runtime behavior, not Developer ID/notarization status. Distribution workflow proves Developer ID delivery chain, but does not replace exact schema-5 native runtime artifact and independent review required by PAR-007.

App and extensions share group.com.pathstitch.crossport. Apple documents com.apple.security.application-groups as unrestricted for macOS Developer ID distribution, so separate provisioning profiles are not required solely for this entitlement. Developer ID signing still supplies release identity needed to distinguish distribution authority from ad-hoc CI. See [Apple: Creating distribution-signed code for macOS](https://developer.apple.com/documentation/xcode/creating-distribution-signed-code-for-the-mac/) and [Apple: Notarizing macOS software before distribution](https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution).

## CI gates

.github/workflows/macos-release.yml runs on the current macos-15 arm64 image, prunes duplicate multi-gigabyte publish/runtime trees before later phases, creates and verifies the bundle, and runs the real packaged app. Acceptance covers Avalonia/WebView, native storage capability, preference write/readback, Dock icons, STEP import/project/unfold, byte-exact source/output plus serialized-topology STCH round-trip, running-app Finder activation, renderer-level STCH/DXF/STEP smoke, and one mapped qlmanage thumbnail per fixture. CI retains evidence artifact for 90 days and uploads ZIP plus self-validating schema-5 Pathstitch-native-evidence-osx-arm64 manifest. Gate includes packaged acceptance, signing/entitlements, provider inventory/logs/maps/PNGs, real `qlmanage -p` preview execution, and nonce-bound cross-process app-group attestations. Packaged app writes nondefault preview vector; real preview/thumbnail extension processes attest it inside group container; second app process collects records. Probe snapshots exact prior preference presence/value and restores it on success, terminal failure, or timeout; polling preserves active state only while attestations are pending. Collector requires interactive STEP SceneKit view, camera control, positive mesh counts, exact fixture hash, expected bundle IDs, and distinct process identities. See docs/architecture/PAR-007-macos-native-evidence.md for the remaining native-runtime and Developer ID sign-off rules.

## Independent artifact review

After downloading one exact-run `Pathstitch-native-evidence-osx-arm64` artifact, verify it without modifying producer evidence:

```powershell
./scripts/verify-native-macos-evidence.ps1 `
  -EvidenceRoot artifacts/native-review/evidence `
  -ExpectedCommit 0123456789abcdef0123456789abcdef01234567 `
  -ExpectedWorkflowRunId 8675309 `
  -ExpectedWorkflowRunAttempt 2 `
  -ReceiptPath artifacts/native-review/native-evidence-review.json
```

Take expected identity from reviewed commit and GitHub run, not downloaded manifest. Reviewer requires schema 5, validates exact producer inventory, re-runs DXF/STCH plus runtime/app-group semantics in temporary copy, and writes hash-bound receipt outside evidence root. Native status remains pending until exact Apple-silicon run produces retained passed artifact.
