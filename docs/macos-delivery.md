# macOS delivery

Pathstitch publishes an Apple-silicon, self-contained `.app` with no global .NET, Node, npm, or Python requirement.

## Development bundle

On an Apple-silicon Mac with .NET 10 and PowerShell 7:

```powershell
./scripts/package-avalonia-macos.ps1 -SigningIdentity "-"
```

This creates `artifacts/macos/Pathstitch.app` and `Pathstitch-osx-arm64.zip`. The `-` identity applies an ad-hoc signature suitable for local development. Gatekeeper distribution requires Developer ID signing and notarization.

## Signed and notarized bundle

Import the Developer ID Application certificate into the build keychain, then pass its identity:

```powershell
$env:APPLE_ID = "build@example.com"
$env:APPLE_TEAM_ID = "TEAMID"
$env:APPLE_APP_PASSWORD = "app-specific-password"
./scripts/package-avalonia-macos.ps1 `
  -SigningIdentity "Developer ID Application: Example (TEAMID)" `
  -Notarize
```

The script pins ZIPFoundation 0.9.20 source for in-process STCH decoding, generates the registered ICNS, targets arm64-apple-macos13.0, signs every nested Mach-O before both Quick Look extensions and the app, verifies the sealed bundle, submits the ZIP with notarytool, and staples the result. App and extensions share the group.com.pathstitch.crossport application group; distribution signing must authorize that entitlement. CI intentionally uses an ad-hoc signature and does not claim notarization or provisioned cross-process app-group authority.

## CI gates

.github/workflows/macos-release.yml runs on the current macos-15 arm64 image, prunes duplicate multi-gigabyte publish/runtime trees before later phases, creates and verifies the bundle, and runs the real packaged app. Acceptance covers Avalonia/WebView, native storage capability, preference write/readback, Dock icons, STEP import/project/unfold, byte-exact source/output plus serialized-topology STCH round-trip, running-app Finder activation, renderer-level STCH/DXF/STEP smoke, and one mapped qlmanage thumbnail per fixture. CI uploads the ZIP plus a self-validating Pathstitch-native-evidence-osx-arm64 manifest with exact run identity and hashes for acceptance, signing, entitlements, provider inventory/logs, output mapping, and PNGs. See docs/architecture/PAR-007-macos-native-evidence.md for the remaining preview-extension and provisioned-sharing sign-off rules.
