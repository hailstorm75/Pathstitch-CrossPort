# Release-readiness verification

Run the complete auditable gate from the repository root:

```powershell
./scripts/verify-release-readiness.ps1
```

Prerequisites for each verification host are .NET 10, PowerShell 7+, micromamba 2.8.1-0 on `PATH`, network access for pinned first-time runtime downloads, and enough disk space for that host's worker and self-contained publish output. End-user launch remains offline and uses only packaged runtimes.

The procedure statically validates both locked Python/OCCT runtime specifications, then builds, smoke-tests, publishes, audits, and launches only the host-native RID. This is intentional: Windows cannot faithfully extract the macOS conda payload's Unix symlinks. Run the same command once on clean Windows x64 and once on clean Apple-silicon macOS; those two native results together are the release record. The gate also treats every compiler warning as an error and runs the complete unit/UI/kernel/persistence suite plus representative workflows.

Use `./scripts/verify-release-readiness.ps1 -EvidenceLabel windows-ci` on Windows and `./scripts/verify-release-readiness.ps1 -EvidenceLabel macos-ci` on macOS. Both commands must finish successfully without `-SkipLaunchSmoke`.

Each run leaves durable evidence under `artifacts/readiness`:

- `readiness-result.json` records the host, native versus cross-built targets, gate status, duration, and signing/notarization status.
- `verification-transcript.txt` contains the complete command transcript.
- `dotnet-info.txt` records the SDK and runtime environment.
- `runtime-inputs.json` records the SHA-256 digest and size of both explicit platform locks and the shared runtime specification.
- `all-tests.trx` and `representative-workflows.trx` contain machine-readable test results.
- `<native-rid>-package-inventory.json` records every packaged file, byte length, and SHA-256 digest for that host's release artifact.
- `step-worker-performance.json` records the fixture hash, exact budgets, native worker startup/import timings, peak working set, and representative topology counts. The release budgets are 15 seconds to handshake, 45 seconds to import `box-cylinder.step`, and 1.5 GiB peak working set.

Use `-SkipLaunchSmoke` only for diagnosis; a result containing `skipped` is not release evidence. The procedure does not cross-publish because that cannot validate the other platform's packaged native dependencies. A release sign-off requires the default procedure on both a clean Windows x64 runner and a clean Apple-silicon macOS runner, plus the signed/notarized macOS bundle procedure in [macos-delivery.md](macos-delivery.md). The publish-directory smoke proves the application starts without repository-local assets; installer behavior and OS prerequisites still require the clean runner/VM evidence.

| Evidence | Windows x64 run | macOS arm64 run |
|---|---:|---:|
| Zero-warning build and complete test suite | Required | Required |
| Representative save/reopen and kernel contracts | Required | Required |
| Native STEP startup/import/memory budgets | Required | Required |
| Self-contained host-native package audit | `win-x64` | `osx-arm64` |
| Native launch from the clean publish directory | Required | Required |
| Developer ID signature, hardened runtime, notarization and stapling | N/A | Required for distribution |

The Windows and macOS workflows upload their `artifacts/readiness` directories. Keep both `readiness-result.json` files with the release record. A `requires-native-run`, `skipped`, or `not-run-by-this-script` value is explicitly not release sign-off for that gate; each target must have `native-launch-passed` in its own host result.

The readiness gate must stay synchronized with the platform and kernel claims in the root README. STEP readiness additionally requires the topology, projection, unfold, persistence, failure, delivery, and parity gates in [ADR-001](architecture/ADR-001-step-brep-kernel.md). The README's packaged STEP claim is releasable only when those tests and the packaged-worker checks are green on both native platforms.

Each native gate requires its RID-specific app-owned Python/OCCT worker, locked environment/specification/manifest and SBOM metadata, `sitecustomize.py`, and `pathstitch_core/geometry_worker.py` in the publish output. Both locks and the shared runtime specification are validated on every host, but only a native package and smoke test establish readiness for a RID.
