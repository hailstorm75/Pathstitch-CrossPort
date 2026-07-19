[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$EvidenceRoot,
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string]$ExpectedCommit,
    [Parameter(Mandatory)]
    [ValidatePattern('^[1-9][0-9]*$')]
    [string]$ExpectedWorkflowRunId,
    [ValidatePattern('^[1-9][0-9]*$')]
    [string]$ExpectedWorkflowRunAttempt = '',
    [string]$ReceiptPath = ''
)

$ErrorActionPreference = 'Stop'
$EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
$volumeRoot = [IO.Path]::GetPathRoot($EvidenceRoot)
if ([string]::IsNullOrWhiteSpace($volumeRoot) -or
    $EvidenceRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) -eq
        $volumeRoot.TrimEnd([IO.Path]::DirectorySeparatorChar)) {
    throw 'EvidenceRoot must be a dedicated non-root directory.'
}
if (-not (Test-Path -LiteralPath $EvidenceRoot -PathType Container)) {
    throw "EvidenceRoot does not exist: $EvidenceRoot"
}
if ([string]::IsNullOrWhiteSpace($ReceiptPath)) {
    $ReceiptPath = Join-Path ([IO.Directory]::GetParent($EvidenceRoot).FullName) 'native-evidence-review.json'
}
$ReceiptPath = [IO.Path]::GetFullPath($ReceiptPath)
$evidencePrefix = $EvidenceRoot.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if ($ReceiptPath.StartsWith($evidencePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'ReceiptPath must be outside EvidenceRoot so review cannot mutate producer evidence.'
}

$validationErrors = [Collections.Generic.List[string]]::new()
function Add-ReviewError([string]$Message) { $validationErrors.Add($Message) }
function Get-Sha256([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

$producerManifestPath = Join-Path $EvidenceRoot 'native-evidence-manifest.json'
$producerManifest = $null
if (-not (Test-Path -LiteralPath $producerManifestPath -PathType Leaf)) {
    Add-ReviewError 'Producer manifest is missing: native-evidence-manifest.json'
} else {
    try { $producerManifest = Get-Content -LiteralPath $producerManifestPath -Raw | ConvertFrom-Json }
    catch { Add-ReviewError "Producer manifest is invalid JSON: $($_.Exception.Message)" }
}

if ($null -ne $producerManifest) {
    if ([int]$producerManifest.schemaVersion -lt 5) {
        Add-ReviewError 'Producer manifest schemaVersion must be at least 5.'
    }
    if ([string]$producerManifest.status -ne 'passed') {
        Add-ReviewError 'Producer manifest status is not passed.'
    }
    if ([string]$producerManifest.commit -ine $ExpectedCommit) {
        Add-ReviewError 'Producer manifest commit does not match ExpectedCommit.'
    }
    if ([string]$producerManifest.workflowRunId -ne $ExpectedWorkflowRunId) {
        Add-ReviewError 'Producer manifest workflowRunId does not match ExpectedWorkflowRunId.'
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedWorkflowRunAttempt) -and
        [string]$producerManifest.workflowRunAttempt -ne $ExpectedWorkflowRunAttempt) {
        Add-ReviewError 'Producer manifest workflowRunAttempt does not match ExpectedWorkflowRunAttempt.'
    }

    $describedFiles = @{}
    foreach ($descriptor in @($producerManifest.evidenceFiles)) {
        $name = [string]$descriptor.name
        if ([string]::IsNullOrWhiteSpace($name) -or [IO.Path]::GetFileName($name) -ne $name) {
            Add-ReviewError "Producer inventory contains invalid file name: $name"
            continue
        }
        if ($describedFiles.ContainsKey($name)) {
            Add-ReviewError "Producer inventory contains duplicate file: $name"
            continue
        }
        $describedFiles[$name] = $descriptor
    }

    $actualFiles = @(
        Get-ChildItem -LiteralPath $EvidenceRoot -File |
            Where-Object Name -ne 'native-evidence-manifest.json'
    )
    foreach ($file in $actualFiles) {
        if (-not $describedFiles.ContainsKey($file.Name)) {
            Add-ReviewError "Producer inventory omits retained file: $($file.Name)"
            continue
        }
        $descriptor = $describedFiles[$file.Name]
        if ([int64]$descriptor.bytes -ne $file.Length) {
            Add-ReviewError "Producer inventory byte count differs for $($file.Name)."
        }
        $expectedHash = [string]$descriptor.sha256
        if ($expectedHash -notmatch '^[0-9a-fA-F]{64}$' -or (Get-Sha256 $file.FullName) -ne $expectedHash) {
            Add-ReviewError "Producer inventory SHA-256 differs for $($file.Name)."
        }
    }
    foreach ($name in $describedFiles.Keys) {
        if (-not (Test-Path -LiteralPath (Join-Path $EvidenceRoot $name) -PathType Leaf)) {
            Add-ReviewError "Producer inventory references missing file: $name"
        }
    }
}

$collectorExitCode = $null
$revalidatedManifestPath = $null
$revalidatedManifest = $null
$temporaryRoot = $null
if ($validationErrors.Count -eq 0) {
    try {
        $temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) (
            'Pathstitch-Native-Evidence-Review-' + [Guid]::NewGuid().ToString('N'))
        $inputRoot = Join-Path $temporaryRoot 'input'
        $validationRoot = Join-Path $temporaryRoot 'validated'
        New-Item -ItemType Directory -Force -Path $inputRoot, $validationRoot | Out-Null
        Get-ChildItem -LiteralPath $EvidenceRoot -Force |
            Copy-Item -Destination $inputRoot -Recurse -Force
        Get-ChildItem -LiteralPath $inputRoot -Force |
            Copy-Item -Destination $validationRoot -Recurse -Force

        $activation = Get-Content -LiteralPath (
            Join-Path $inputRoot 'file-activation-acceptance.json') -Raw | ConvertFrom-Json
        $activationFiles = @($activation.files)
        if ($activationFiles.Count -ne 1 -or
            [string]::IsNullOrWhiteSpace([string]$activationFiles[0])) {
            throw 'File activation evidence must identify exactly one fixture.'
        }

        $repositoryRoot = [IO.Directory]::GetParent($PSScriptRoot).FullName
        $collectorPath = Join-Path $repositoryRoot 'scripts/collect-native-macos-evidence.ps1'
        $start = [Diagnostics.ProcessStartInfo]::new((Get-Command pwsh).Source)
        $start.WorkingDirectory = $repositoryRoot
        $start.UseShellExecute = $false
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $start.CreateNoWindow = $true
        foreach ($argument in @(
            '-NoLogo',
            '-NoProfile',
            '-File', $collectorPath,
            '-EvidenceRoot', $validationRoot,
            '-PackageAcceptanceDirectory', $inputRoot,
            '-FileActivationDirectory', $inputRoot,
            '-QuickLookDirectory', $inputRoot,
            '-PackageZip', (Join-Path $inputRoot 'Pathstitch-osx-arm64.zip'),
            '-ExpectedActivationFixture', [string]$activationFiles[0],
            '-SkipPlatformCollection'
        )) { $start.ArgumentList.Add($argument) }
        $start.Environment['GITHUB_SHA'] = [string]$producerManifest.commit
        $start.Environment['GITHUB_RUN_ID'] = [string]$producerManifest.workflowRunId
        $start.Environment['GITHUB_RUN_ATTEMPT'] = [string]$producerManifest.workflowRunAttempt

        $process = [Diagnostics.Process]::Start($start)
        $standardOutput = $process.StandardOutput.ReadToEndAsync()
        $standardError = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $collectorExitCode = $process.ExitCode
        $collectorError = $standardError.GetAwaiter().GetResult().Trim()
        $null = $standardOutput.GetAwaiter().GetResult()
        $process.Dispose()

        $revalidatedManifestPath = Join-Path $validationRoot 'native-evidence-manifest.json'
        if (-not (Test-Path -LiteralPath $revalidatedManifestPath -PathType Leaf)) {
            Add-ReviewError 'Portable collector did not produce revalidated manifest.'
        } else {
            $revalidatedManifest = Get-Content -LiteralPath $revalidatedManifestPath -Raw | ConvertFrom-Json
            if ([string]$revalidatedManifest.status -ne 'passed' -or $collectorExitCode -ne 0) {
                Add-ReviewError "Portable collector rejected evidence (exit $collectorExitCode): $collectorError"
            }
            if ([string]$revalidatedManifest.commit -ine $ExpectedCommit -or
                [string]$revalidatedManifest.workflowRunId -ne $ExpectedWorkflowRunId) {
                Add-ReviewError 'Revalidated manifest lost expected commit or workflow-run identity.'
            }
        }
    } catch {
        Add-ReviewError "Portable revalidation failed: $($_.Exception.Message)"
    }
}

$receiptDirectory = [IO.Path]::GetDirectoryName($ReceiptPath)
if (-not [string]::IsNullOrWhiteSpace($receiptDirectory)) {
    New-Item -ItemType Directory -Force -Path $receiptDirectory | Out-Null
}
$status = if ($validationErrors.Count -eq 0) { 'passed' } else { 'failed' }
$receipt = [ordered]@{
    schemaVersion = 1
    status = $status
    reviewedUtc = [DateTimeOffset]::UtcNow
    expectedCommit = $ExpectedCommit.ToLowerInvariant()
    expectedWorkflowRunId = $ExpectedWorkflowRunId
    expectedWorkflowRunAttempt = if ([string]::IsNullOrWhiteSpace($ExpectedWorkflowRunAttempt)) {
        $null
    } else { $ExpectedWorkflowRunAttempt }
    producer = [ordered]@{
        manifestSha256 = if (Test-Path -LiteralPath $producerManifestPath -PathType Leaf) {
            Get-Sha256 $producerManifestPath
        } else { $null }
        schemaVersion = if ($null -ne $producerManifest) { $producerManifest.schemaVersion } else { $null }
        commit = if ($null -ne $producerManifest) { $producerManifest.commit } else { $null }
        workflowRunId = if ($null -ne $producerManifest) { $producerManifest.workflowRunId } else { $null }
        workflowRunAttempt = if ($null -ne $producerManifest) { $producerManifest.workflowRunAttempt } else { $null }
        evidenceFileCount = if ($null -ne $producerManifest) { @($producerManifest.evidenceFiles).Count } else { 0 }
    }
    revalidation = [ordered]@{
        collectorExitCode = $collectorExitCode
        manifestSha256 = if ($revalidatedManifestPath -and
            (Test-Path -LiteralPath $revalidatedManifestPath -PathType Leaf)) {
            Get-Sha256 $revalidatedManifestPath
        } else { $null }
        evidenceFileCount = if ($null -ne $revalidatedManifest) {
            @($revalidatedManifest.evidenceFiles).Count
        } else { 0 }
    }
    validationErrors = @($validationErrors)
}
[IO.File]::WriteAllText(
    $ReceiptPath,
    ($receipt | ConvertTo-Json -Depth 8) + [Environment]::NewLine,
    [Text.UTF8Encoding]::new($false))

if ($temporaryRoot -and (Test-Path -LiteralPath $temporaryRoot -PathType Container)) {
    Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
}
if ($status -ne 'passed') {
    Write-Error "Native evidence review failed: $($validationErrors -join ' | ')"
    exit 1
}
Write-Output $ReceiptPath

