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

The script enables hardened runtime, signs nested native libraries, verifies the sealed bundle, submits the ZIP with `notarytool`, and staples the result. CI intentionally uses an ad-hoc signature unless release secrets and a certificate are configured.

## CI gates

`.github/workflows/macos-release.yml` restores and tests `osx-arm64`, creates and verifies the bundle, checks its packaged runtimes, starts the real executable long enough to initialize Avalonia/WebView, and uploads the ZIP. The .NET suite includes file-dialog routing and `.stch` save/reopen tests.
