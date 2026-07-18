[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [switch]$SkipLaunchSmoke,
    [switch]$PruneNativePublish,
    [string]$EvidenceLabel = "local"
)

$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw "Release readiness requires PowerShell 7 or newer."
}
$hostArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
if ($IsWindows -and $hostArchitecture -ne [System.Runtime.InteropServices.Architecture]::X64) {
    throw "win-x64 release readiness requires a native x64 Windows host; detected $hostArchitecture."
}
if ($IsMacOS -and $hostArchitecture -ne [System.Runtime.InteropServices.Architecture]::Arm64) {
    throw "osx-arm64 release readiness requires a native Apple-silicon host; detected $hostArchitecture."
}
$nativeRid = if ($IsWindows) { "win-x64" } elseif ($IsMacOS) { "osx-arm64" } else {
    throw "Release readiness supports only Windows x64 and macOS arm64 hosts."
}
$repo = Split-Path -Parent $PSScriptRoot
$artifacts = [System.IO.Path]::GetFullPath((Join-Path $repo "artifacts/readiness"))
$expectedRoot = [System.IO.Path]::GetFullPath((Join-Path $repo "artifacts"))
if (-not $artifacts.StartsWith($expectedRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean readiness output outside $expectedRoot"
}
if (Test-Path -LiteralPath $artifacts) { Remove-Item -LiteralPath $artifacts -Recurse -Force }
New-Item -ItemType Directory -Path $artifacts | Out-Null
$transcriptPath = Join-Path $artifacts "verification-transcript.txt"
$resultsPath = Join-Path $artifacts "readiness-result.json"
$gateResults = [System.Collections.Generic.List[object]]::new()
$startedUtc = [DateTimeOffset]::UtcNow
$succeeded = $false
Start-Transcript -LiteralPath $transcriptPath -Force | Out-Null

function Invoke-Checked([string]$label, [scriptblock]$command) {
    Write-Host "== $label =="
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        & $command
        if ($LASTEXITCODE -ne 0) { throw "$label failed with exit code $LASTEXITCODE" }
        $gateResults.Add([pscustomobject]@{ name = $label; status = "passed"; durationSeconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 3) })
    }
    catch {
        $gateResults.Add([pscustomobject]@{ name = $label; status = "failed"; durationSeconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 3); error = $_.Exception.Message })
        throw
    }
}

function Write-PackageInventory([string]$packageRoot, [string]$outputName) {
    Get-ChildItem -LiteralPath $packageRoot -Recurse -File |
        Sort-Object FullName |
        ForEach-Object {
            [pscustomobject]@{
                path = [System.IO.Path]::GetRelativePath($packageRoot, $_.FullName).Replace('\', '/')
                length = $_.Length
                sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        } | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $artifacts $outputName) -Encoding utf8
}

function Write-ReadinessResult {
    $nativePlatform = if ($IsWindows) { "win-x64" } elseif ($IsMacOS) { "osx-arm64" } else { "unsupported-host" }
    $launchStatus = if ($SkipLaunchSmoke) { "skipped" } elseif ($IsWindows -or $IsMacOS) { if ($succeeded) { "passed" } else { "failed-or-not-reached" } } else { "not-supported" }
    $result = [ordered]@{
        schemaVersion = 1
        evidenceLabel = $EvidenceLabel
        status = if ($succeeded) { "passed" } else { "failed" }
        startedUtc = $startedUtc
        completedUtc = [DateTimeOffset]::UtcNow
        configuration = $Configuration
        host = [ordered]@{
            os = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
            architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
            powerShell = $PSVersionTable.PSVersion.ToString()
            nativeReleaseTarget = $nativePlatform
        }
        targets = [ordered]@{
            winX64 = if ($IsWindows) { "native-launch-$launchStatus" } else { "requires-native-run" }
            osxArm64 = if ($IsMacOS) { "native-launch-$launchStatus" } else { "requires-native-run" }
        }
        signing = [ordered]@{
            macosDeveloperId = "not-run-by-this-script"
            macosNotarization = "not-run-by-this-script"
        }
        gates = $gateResults
    }
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resultsPath -Encoding utf8
}

Push-Location $repo
try {
    dotnet --info | Set-Content -LiteralPath (Join-Path $artifacts "dotnet-info.txt") -Encoding utf8
    $workerBuilder = Join-Path $repo "geometry-worker/build-runtime.ps1"
    Invoke-Checked "Validate both release runtime specifications" {
        $runtimeInputs = [System.Collections.Generic.List[object]]::new()
        foreach ($rid in @("win-x64", "osx-arm64")) {
            $lock = Join-Path $repo "geometry-worker/locks/$rid.lock"
            if (-not (Test-Path -LiteralPath $lock)) { throw "Missing release runtime lock: $lock" }
            $lockText = Get-Content -LiteralPath $lock -Raw
            $lockDirective = Get-Content -LiteralPath $lock |
                Where-Object { $_.Trim().Length -gt 0 -and -not $_.TrimStart().StartsWith('#') } |
                Select-Object -First 1
            if ($lockDirective -ne "@EXPLICIT") { throw "$rid lock is not explicit." }
            if ($lockText -notmatch "pythonocc-core") { throw "$rid lock does not contain pythonocc-core." }
            $runtimeInputs.Add([ordered]@{
                path = "geometry-worker/locks/$rid.lock"
                sha256 = (Get-FileHash -LiteralPath $lock -Algorithm SHA256).Hash.ToLowerInvariant()
                length = (Get-Item -LiteralPath $lock).Length
            })
        }
        $specPath = Join-Path $repo "geometry-worker/runtime-spec.json"
        $spec = Get-Content -LiteralPath $specPath -Raw | ConvertFrom-Json
        foreach ($rid in @("win-x64", "osx-arm64")) {
            if ($spec.runtimeIdentifiers -notcontains $rid) { throw "Runtime specification omits $rid." }
        }
        $runtimeInputs.Add([ordered]@{
            path = "geometry-worker/runtime-spec.json"
            sha256 = (Get-FileHash -LiteralPath $specPath -Algorithm SHA256).Hash.ToLowerInvariant()
            length = (Get-Item -LiteralPath $specPath).Length
        })
        $runtimeInputs | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $artifacts "runtime-inputs.json") -Encoding utf8
    }
    Invoke-Checked "Build and smoke-test native STEP worker runtime" {
        & $workerBuilder -RuntimeIdentifier $nativeRid -Micromamba micromamba
    }
    Invoke-Checked "Restore native runtime" { dotnet restore PathstitchCross.slnx -r $nativeRid }
    Invoke-Checked "Zero-warning solution build" { dotnet build PathstitchCross.slnx -c $Configuration --no-restore -warnaserror }
    Invoke-Checked "Unit and UI coordination tests" {
        dotnet test PathstitchCross.slnx -c $Configuration --no-build --no-restore `
            --logger "trx;LogFileName=all-tests.trx" --results-directory $artifacts
    }

    $nativePublish = Join-Path $artifacts $nativeRid
    Invoke-Checked "Native self-contained publish" {
        dotnet publish src/Pathstitch.App/Pathstitch.App.csproj -c $Configuration -r $nativeRid --self-contained true -o $nativePublish
    }

    Invoke-Checked "Release package content audit" {
      $appExecutable = if ($IsWindows) { "Pathstitch.App.exe" } else { "Pathstitch.App" }
      $nodeExecutable = if ($IsWindows) { "node.exe" } else { "node" }
      $pythonExecutable = if ($IsWindows) { "python.exe" } else { "bin/python3.11" }
      $required = @(
        (Join-Path $nativePublish $appExecutable),
        (Join-Path $nativePublish "Assets/OpenGeometry/runtime/$nodeExecutable"),
        (Join-Path $nativePublish "Assets/OpenGeometry/runtime/LICENSE.node.txt"),
        (Join-Path $nativePublish "Assets/OpenGeometry/runtime-manifest.json"),
        (Join-Path $nativePublish "Assets/OpenGeometry/opengeometry-worker.mjs"),
        (Join-Path $nativePublish "Assets/OpenGeometry/node_modules/opengeometry/package.json"),
        (Join-Path $nativePublish "Assets/OpenGeometry/node_modules/opengeometry/opengeometry_bg.wasm")
      )
      if ($IsMacOS) { $required += (Join-Path $nativePublish "libAvaloniaNative.dylib") }
      foreach ($path in $required) {
          if (-not (Test-Path -LiteralPath $path)) { throw "Missing release package item: $path" }
      }

      $workerRequired = @(
          (Join-Path $nativePublish "GeometryWorker/$nativeRid/$pythonExecutable"),
          (Join-Path $nativePublish "GeometryWorker/$nativeRid/pathstitch_core/geometry_worker.py"),
          (Join-Path $nativePublish "GeometryWorker/$nativeRid/runtime-manifest.json"),
          (Join-Path $nativePublish "GeometryWorker/$nativeRid/environment.lock"),
          (Join-Path $nativePublish "GeometryWorker/$nativeRid/runtime-spec.json"),
          (Join-Path $nativePublish "GeometryWorker/$nativeRid/sitecustomize.py"),
          (Join-Path $nativePublish "GeometryWorker/$nativeRid/sbom.spdx.json"),
          (Join-Path $nativePublish "GeometryWorker/$nativeRid/third-party-notices.json")
      )
      foreach ($path in $workerRequired) {
          if (-not (Test-Path -LiteralPath $path)) { throw "Missing packaged STEP geometry-worker item: $path" }
      }

      $npmExecutables = Get-ChildItem -LiteralPath $artifacts -Recurse -File | Where-Object { $_.Name -match '^npm(\.cmd|\.exe)?$' }
      if ($npmExecutables) { throw "Release artifacts must not contain npm executables." }
      Write-PackageInventory $nativePublish "$nativeRid-package-inventory.json"
    }

    Invoke-Checked "Native STEP startup, import, and memory budgets" {
        $workerRoot = Join-Path $nativePublish "GeometryWorker/$nativeRid"
        $workerPython = if ($IsWindows) { Join-Path $workerRoot "python.exe" } else { Join-Path $workerRoot "bin/python3.11" }
        $fixture = Join-Path $repo "tests/Pathstitch.App.Tests/Fixtures/box-cylinder.step"
        $benchmark = Join-Path $repo "scripts/measure-step-worker.py"
        $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = $workerPython
        $startInfo.WorkingDirectory = $workerRoot
        $startInfo.UseShellExecute = $false
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $startInfo.CreateNoWindow = $true
        foreach ($argument in @("-B", $benchmark, $workerPython, $workerRoot, $fixture)) {
            [void]$startInfo.ArgumentList.Add($argument)
        }
        $process = [System.Diagnostics.Process]::Start($startInfo)
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw "STEP performance harness failed: $stderr" }
        $measurement = $stdout | ConvertFrom-Json
        $budgets = [ordered]@{
            startupSeconds = 15.0
            representativeImportSeconds = 45.0
            peakWorkingSetBytes = 1610612736
        }
        $performance = [ordered]@{
            schemaVersion = 1
            runtimeIdentifier = $nativeRid
            fixture = "box-cylinder.step"
            fixtureSha256 = (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash.ToLowerInvariant()
            budgets = $budgets
            measurements = $measurement
            passed = ($measurement.startupSeconds -le $budgets.startupSeconds `
                -and $measurement.importSeconds -le $budgets.representativeImportSeconds `
                -and $measurement.peakWorkingSetBytes -le $budgets.peakWorkingSetBytes `
                -and $measurement.bodyCount -eq 2 `
                -and $measurement.faceCount -eq 9)
        }
        $performance | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $artifacts "step-worker-performance.json") -Encoding utf8
        if (-not $performance.passed) {
            throw "STEP worker exceeded a release budget or returned unexpected representative topology."
        }
    }

    Invoke-Checked "Representative save/reopen and kernel contracts" {
        dotnet test tests/Pathstitch.App.Tests/Pathstitch.App.Tests.csproj -c $Configuration --no-build --no-restore `
            --filter "FullyQualifiedName~WorkspaceSemanticRoundTripTests|FullyQualifiedName~OperationServiceConformanceTests|FullyQualifiedName~Project3DStateServiceTests|FullyQualifiedName~StepGeometry" `
            --logger "trx;LogFileName=representative-workflows.trx" --results-directory $artifacts
    }

    if (-not $SkipLaunchSmoke) {
        if ($IsWindows) {
            Invoke-Checked "Windows clean-publish launch smoke" {
                $process = Start-Process (Join-Path $nativePublish "Pathstitch.App.exe") -PassThru -WindowStyle Hidden
                Start-Sleep -Seconds 8
                if ($process.HasExited) { throw "Windows clean-artifact launch exited with $($process.ExitCode)" }
                Stop-Process -Id $process.Id
            }
        }
        elseif ($IsMacOS) {
            Invoke-Checked "macOS clean-publish launch smoke" {
                $process = Start-Process (Join-Path $nativePublish "Pathstitch.App") -PassThru
                Start-Sleep -Seconds 8
                if ($process.HasExited) { throw "macOS clean-artifact launch exited with $($process.ExitCode)" }
                Stop-Process -Id $process.Id
            }
        }
    }

    if ($PruneNativePublish -and (Test-Path -LiteralPath $nativePublish)) {
        $resolvedNativePublish = [IO.Path]::GetFullPath($nativePublish)
        if (-not $resolvedNativePublish.StartsWith(
                $artifacts,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to prune native publish outside $artifacts"
        }
        Remove-Item -LiteralPath $resolvedNativePublish -Recurse -Force
    }

    $succeeded = $true
    Write-Host "Current-host $nativeRid release readiness verified. The other target requires its own native run. Evidence: $artifacts"
}
finally {
    Write-ReadinessResult
    Stop-Transcript | Out-Null
    Pop-Location
}
