[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$RuntimeRoot,
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'osx-arm64')]
    [string]$RuntimeIdentifier,
    [Parameter(Mandatory)]
    [string]$LockFile
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path $RuntimeRoot).Path
$lock = (Resolve-Path $LockFile).Path
$manifestPath = Join-Path $root 'runtime-manifest.json'
$files = @(Get-ChildItem $root -Recurse -File |
    Where-Object FullName -ne $manifestPath |
    Sort-Object FullName)
$totalBytes = ($files | Measure-Object -Property Length -Sum).Sum
$fileEntries = @($files | ForEach-Object {
    [pscustomobject][ordered]@{
        path = $_.FullName.Substring($root.Length + 1).Replace('\', '/')
        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        size = $_.Length
    }
})
$manifest = [ordered]@{
    schemaVersion = 1
    runtimeIdentifier = $RuntimeIdentifier
    protocolVersion = 1
    lockSha256 = (Get-FileHash -LiteralPath $lock -Algorithm SHA256).Hash.ToLowerInvariant()
    pythonExecutable = if ($RuntimeIdentifier -eq 'win-x64') { 'python.exe' } else { 'bin/python3.11' }
    workerModule = 'pathstitch_core/geometry_worker.py'
    fileCount = $fileEntries.Count
    totalBytes = $totalBytes
    files = $fileEntries
}
$temporaryManifest = "$manifestPath.tmp"
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $temporaryManifest -Encoding utf8
Move-Item -LiteralPath $temporaryManifest -Destination $manifestPath -Force

[pscustomobject]@{
    RuntimeIdentifier = $RuntimeIdentifier
    FileCount = $fileEntries.Count
    TotalBytes = $totalBytes
    LockSha256 = $manifest.lockSha256
}
