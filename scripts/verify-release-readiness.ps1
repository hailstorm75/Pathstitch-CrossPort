[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [switch]$SkipLaunchSmoke,
    [string]$EvidenceLabel = "local"
)

$ErrorActionPreference = "Stop"
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
            winX64 = if ($IsWindows) { "native-launch-$launchStatus" } else { "cross-built-only" }
            osxArm64 = if ($IsMacOS) { "native-launch-$launchStatus" } else { "cross-built-only" }
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
    Invoke-Checked "Restore Windows runtime" { dotnet restore PathstitchCross.slnx -r win-x64 }
    Invoke-Checked "Restore macOS runtime" { dotnet restore PathstitchCross.slnx -r osx-arm64 }
    Invoke-Checked "Zero-warning solution build" { dotnet build PathstitchCross.slnx -c $Configuration --no-restore -warnaserror }
    Invoke-Checked "Unit and UI coordination tests" { dotnet test PathstitchCross.slnx -c $Configuration --no-build --no-restore }

    $winPublish = Join-Path $artifacts "win-x64"
    $macPublish = Join-Path $artifacts "osx-arm64"
    Invoke-Checked "Windows publish" { dotnet publish src/Pathstitch.App/Pathstitch.App.csproj -c $Configuration -r win-x64 --self-contained true --no-restore -o $winPublish }
    Invoke-Checked "macOS publish" { dotnet publish src/Pathstitch.App/Pathstitch.App.csproj -c $Configuration -r osx-arm64 --self-contained true --no-restore -o $macPublish }

    Invoke-Checked "Release package content audit" {
      $required = @(
        (Join-Path $winPublish "Pathstitch.App.exe"),
        (Join-Path $winPublish "Assets/OpenGeometry/runtime/node.exe"),
        (Join-Path $winPublish "Assets/OpenGeometry/runtime/LICENSE.node.txt"),
        (Join-Path $winPublish "Assets/OpenGeometry/runtime-manifest.json"),
        (Join-Path $winPublish "Assets/OpenGeometry/opengeometry-worker.mjs"),
        (Join-Path $winPublish "Assets/OpenGeometry/node_modules/opengeometry/package.json"),
        (Join-Path $winPublish "Assets/OpenGeometry/node_modules/opengeometry/opengeometry_bg.wasm"),
        (Join-Path $macPublish "Pathstitch.App"),
        (Join-Path $macPublish "libAvaloniaNative.dylib"),
        (Join-Path $macPublish "Assets/OpenGeometry/runtime/node"),
        (Join-Path $macPublish "Assets/OpenGeometry/runtime/LICENSE.node.txt"),
        (Join-Path $macPublish "Assets/OpenGeometry/runtime-manifest.json"),
        (Join-Path $macPublish "Assets/OpenGeometry/opengeometry-worker.mjs"),
        (Join-Path $macPublish "Assets/OpenGeometry/node_modules/opengeometry/package.json"),
        (Join-Path $macPublish "Assets/OpenGeometry/node_modules/opengeometry/opengeometry_bg.wasm")
      )
      foreach ($path in $required) {
          if (-not (Test-Path -LiteralPath $path)) { throw "Missing release package item: $path" }
      }

      $npmExecutables = Get-ChildItem -LiteralPath $artifacts -Recurse -File | Where-Object { $_.Name -match '^npm(\.cmd|\.exe)?$' }
      if ($npmExecutables) { throw "Release artifacts must not contain npm executables." }
      Write-PackageInventory $winPublish "win-x64-package-inventory.json"
      Write-PackageInventory $macPublish "osx-arm64-package-inventory.json"
    }

    Invoke-Checked "Representative save/reopen and kernel contracts" {
        dotnet test tests/Pathstitch.App.Tests/Pathstitch.App.Tests.csproj -c $Configuration --no-build --no-restore `
            --filter "FullyQualifiedName~WorkspaceSemanticRoundTripTests|FullyQualifiedName~OperationServiceConformanceTests|FullyQualifiedName~Project3DStateServiceTests"
    }

    if (-not $SkipLaunchSmoke) {
        if ($IsWindows) {
            Invoke-Checked "Windows clean-publish launch smoke" {
                $process = Start-Process (Join-Path $winPublish "Pathstitch.App.exe") -PassThru -WindowStyle Hidden
                Start-Sleep -Seconds 8
                if ($process.HasExited) { throw "Windows clean-artifact launch exited with $($process.ExitCode)" }
                Stop-Process -Id $process.Id
            }
        }
        elseif ($IsMacOS) {
            Invoke-Checked "macOS clean-publish launch smoke" {
                $process = Start-Process (Join-Path $macPublish "Pathstitch.App") -PassThru
                Start-Sleep -Seconds 8
                if ($process.HasExited) { throw "macOS clean-artifact launch exited with $($process.ExitCode)" }
                Stop-Process -Id $process.Id
            }
        }
    }

    $succeeded = $true
    Write-Host "Current-host release readiness verified. Cross-built targets still require their native launch/signing gates. Evidence: $artifacts"
}
finally {
    Write-ReadinessResult
    Stop-Transcript | Out-Null
    Pop-Location
}
