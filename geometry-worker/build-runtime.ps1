[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'osx-arm64')]
    [string]$RuntimeIdentifier = 'win-x64',
    [string]$Micromamba = 'micromamba',
    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\artifacts\geometry-worker'),
    [string]$SourcePrefix = '',
    [string]$WslDistribution = 'Ubuntu',
    [switch]$KeepDevelopmentFiles,
    [switch]$SkipSmokeTest
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$lock = (Resolve-Path (Join-Path $PSScriptRoot "locks\$RuntimeIdentifier.lock")).Path
$spec = (Resolve-Path (Join-Path $PSScriptRoot 'runtime-spec.json')).Path
$destination = [System.IO.Path]::GetFullPath((Join-Path $OutputRoot $RuntimeIdentifier))
$staging = "$destination.staging"
$isNativeRuntime = ($RuntimeIdentifier -eq 'win-x64' -and $IsWindows) -or
    ($RuntimeIdentifier -eq 'osx-arm64' -and $IsMacOS)

if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
New-Item -ItemType Directory -Force -Path $staging | Out-Null

if ([string]::IsNullOrWhiteSpace($SourcePrefix)) {
    if ($IsWindows -and $RuntimeIdentifier -eq 'osx-arm64') {
        & (Join-Path $PSScriptRoot 'build-osx-runtime-via-wsl.ps1') `
            -LockFile $lock `
            -Destination $staging `
            -Distribution $WslDistribution
    } else {
        $micromambaCommand = Get-Command $Micromamba -ErrorAction Stop
        & $micromambaCommand.Source create -y -p $staging --file $lock
        if ($LASTEXITCODE -ne 0) { throw "micromamba failed with exit code $LASTEXITCODE" }
    }
} else {
    $source = (Resolve-Path $SourcePrefix).Path
    $pythonName = if ($RuntimeIdentifier -eq 'win-x64') { 'python.exe' } else { 'bin/python3.11' }
    if (-not (Test-Path -LiteralPath (Join-Path $source $pythonName))) {
        throw "Source prefix does not contain $pythonName"
    }
    if ($IsWindows) {
        & robocopy $source $staging /E /COPY:DAT /DCOPY:DAT /R:2 /W:1 /MT:16 /NFL /NDL /NJH /NJS /NP
        if ($LASTEXITCODE -gt 7) { throw "robocopy failed with exit code $LASTEXITCODE" }
    } else {
        Copy-Item -Path (Join-Path $source '*') -Destination $staging -Recurse -Force
    }
}

Copy-Item -LiteralPath (Join-Path $repo 'pathstitch_core') -Destination (Join-Path $staging 'pathstitch_core') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'sitecustomize.py') -Destination (Join-Path $staging 'sitecustomize.py')
Copy-Item -LiteralPath $lock -Destination (Join-Path $staging 'environment.lock')
Copy-Item -LiteralPath $spec -Destination (Join-Path $staging 'runtime-spec.json')

if (-not $KeepDevelopmentFiles) {
    $developmentDirectories = @(
        'include', 'libs', 'Tools',
        'Library/include', 'Library/cmake',
        'Lib/site-packages/pip', 'Lib/site-packages/setuptools',
        'Lib/site-packages/_pytest', 'Lib/site-packages/pytest'
    )
    foreach ($relativePath in $developmentDirectories) {
        $path = Join-Path $staging $relativePath
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    }
    Get-ChildItem $staging -Recurse -Directory -Filter '__pycache__' -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force
    Get-ChildItem $staging -Recurse -File -Include '*.pdb','*.pyc','*.pyo','*.lib','*.a' -ErrorAction SilentlyContinue |
        Remove-Item -Force
    $condaHistory = Join-Path $staging 'conda-meta/history'
    if (Test-Path -LiteralPath $condaHistory) { Remove-Item -LiteralPath $condaHistory -Force }
}

& (Join-Path $PSScriptRoot 'write-compliance-metadata.ps1') `
    -RuntimeRoot $staging `
    -RuntimeIdentifier $RuntimeIdentifier `
    -LockFile $lock | Write-Host

$python = if ($RuntimeIdentifier -eq 'win-x64') {
    Join-Path $staging 'python.exe'
} else {
    Join-Path $staging 'bin/python3.11'
}
$worker = Join-Path $staging 'pathstitch_core/geometry_worker.py'
if (-not (Test-Path -LiteralPath $python)) { throw "Packaged Python is missing: $python" }
if (-not (Test-Path -LiteralPath $worker)) { throw "Geometry worker module is missing: $worker" }

function Invoke-NativeRuntimeSmoke([string]$RuntimeRoot) {
    $runtimePython = if ($RuntimeIdentifier -eq 'win-x64') {
        Join-Path $RuntimeRoot 'python.exe'
    } else {
        Join-Path $RuntimeRoot 'bin/python3.11'
    }
    $previousPythonPath = $env:PYTHONPATH
    $previousPath = $env:PATH
    $env:PYTHONPATH = $RuntimeRoot
    $env:PATH = if ($IsWindows) { Join-Path $env:SystemRoot 'System32' } else { '/usr/bin:/bin' }
    try {
        & $runtimePython -B -c "import OCC, OCC.Core.STEPControl, ezdxf, shapely, numpy, scipy; import pathstitch_core.geometry_worker; print('packaged pythonOCC worker imports ok')"
        if ($LASTEXITCODE -ne 0) { throw 'Packaged worker import smoke test failed.' }
    } finally {
        $env:PYTHONPATH = $previousPythonPath
        $env:PATH = $previousPath
    }
}

$manifest = & (Join-Path $PSScriptRoot 'write-runtime-manifest.ps1') `
    -RuntimeRoot $staging `
    -RuntimeIdentifier $RuntimeIdentifier `
    -LockFile $lock

if (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination -Recurse -Force }
Move-Item -LiteralPath $staging -Destination $destination
if ($isNativeRuntime -and -not $SkipSmokeTest) {
    Invoke-NativeRuntimeSmoke $destination
}
Write-Output ([pscustomobject]@{
    RuntimeIdentifier = $RuntimeIdentifier
    Path = $destination
    Files = $manifest.FileCount
    Bytes = $manifest.TotalBytes
    LockSha256 = $manifest.LockSha256
    NativeSmokeTested = $isNativeRuntime -and -not $SkipSmokeTest
})
