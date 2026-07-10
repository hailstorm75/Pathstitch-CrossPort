# Release-readiness verification

Run the complete auditable gate from the repository root:

```powershell
./scripts/verify-release-readiness.ps1
```

The procedure restores both supported RIDs, treats every compiler warning as an error, runs unit/UI/kernel/persistence tests, publishes self-contained Windows and Apple-silicon artifacts, audits required package contents and forbidden npm executables, and launches the current platform artifact.

Each run leaves durable evidence under `artifacts/readiness`:

- `readiness-result.json` records the host, native versus cross-built targets, gate status, duration, and signing/notarization status.
- `verification-transcript.txt` contains the complete command transcript.
- `dotnet-info.txt` records the SDK and runtime environment.
- `win-x64-package-inventory.json` and `osx-arm64-package-inventory.json` record every packaged file, byte length, and SHA-256 digest.

Use `-SkipLaunchSmoke` only on a cross-build host that cannot execute the target binary. A cross-publish proves package construction, not native readiness. A release sign-off requires the default procedure on both a clean Windows x64 runner and a clean Apple-silicon macOS runner, plus the signed/notarized macOS bundle procedure in [macos-delivery.md](macos-delivery.md). The publish-directory smoke proves the application starts without repository-local assets; installer behavior and OS prerequisites still require the clean runner/VM evidence.

| Evidence | Windows x64 run | macOS arm64 run |
|---|---:|---:|
| Zero-warning build and complete test suite | Required | Required |
| Representative save/reopen and kernel contracts | Required | Required |
| Self-contained `win-x64` and `osx-arm64` package audits | Required | Required |
| Native launch from the clean publish directory | Required | Required |
| Developer ID signature, hardened runtime, notarization and stapling | N/A | Required for distribution |

The Windows and macOS workflows upload their `artifacts/readiness` directories. Keep both `readiness-result.json` files with the release record. A `cross-built-only`, `skipped`, or `not-run-by-this-script` value is explicitly not release sign-off for that gate.

The readiness gate must stay synchronized with the platform and kernel claims in the root README. STEP readiness additionally requires the topology, projection, unfold, persistence, failure, delivery, and parity gates in [ADR-001](architecture/ADR-001-step-brep-kernel.md). Until those gates and packaged-worker checks are present and green on both native platforms, the Avalonia README must continue to describe STEP as unavailable.
