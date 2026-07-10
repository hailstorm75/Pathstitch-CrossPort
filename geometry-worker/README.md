# Packaged geometry worker

Release artifacts contain an app-owned Python 3.11 and pythonOCC/OCCT worker. The
runtime is created only from the RID-specific `@EXPLICIT` lock, then pruned,
smoke-tested on its native host, and described by a per-file SHA-256 manifest.

## Native release builds

Install micromamba 2.8.1 and run PowerShell 7 from the repository root:

```powershell
./geometry-worker/build-runtime.ps1 -RuntimeIdentifier win-x64 -Micromamba micromamba
./geometry-worker/build-runtime.ps1 -RuntimeIdentifier osx-arm64 -Micromamba micromamba
```

Only run the RID matching the release host. Native smoke testing is mandatory for
a releasable package. CI cross-publishes may explicitly set the MSBuild property
`RequirePackagedStepGeometryWorker=false`, but those outputs are build evidence,
not release-ready packages.

## Windows cross-build inspection

Windows cannot directly extract the symbolic links in macOS conda packages. For
local cross-RID inspection, `build-runtime.ps1` automatically uses the pinned
Linux micromamba bootstrap in an installed WSL Ubuntu distribution and exports a
dereferenced prefix. Pass `-SkipSmokeTest` because Windows cannot execute Mach-O
arm64 binaries:

```powershell
./geometry-worker/build-runtime.ps1 -RuntimeIdentifier osx-arm64 -SkipSmokeTest
```

This path requires `wsl.exe`, an Ubuntu distribution, `bash`, `curl`, `python3`,
`sha256sum`, and `tar`. GitHub Windows runners must not assume a WSL distribution
is installed; release workflows build and execute the worker on the native RID.

Every completed runtime contains `environment.lock`, `runtime-spec.json`,
`sbom.spdx.json`, `third-party-notices.json`, `sitecustomize.py`, and
`runtime-manifest.json`. Compliance generation fails unless each installed conda
metadata URL exactly matches one explicit lock URL.
