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
$lockHash = (Get-FileHash -LiteralPath $lock -Algorithm SHA256).Hash.ToLowerInvariant()
$lockedUrls = @(Get-Content -LiteralPath $lock | Where-Object {
    $_ -and -not $_.StartsWith('#') -and $_ -ne '@EXPLICIT'
} | ForEach-Object { ($_ -split '#', 2)[0] })
$metadata = @(Get-ChildItem (Join-Path $root 'conda-meta') -Filter '*.json' -File |
    ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json } |
    Sort-Object name, version, build)
$wheelLockPath = Join-Path $root 'python-wheel-lock.json'
$wheelMetadata = if (Test-Path -LiteralPath $wheelLockPath) {
    @((Get-Content -LiteralPath $wheelLockPath -Raw | ConvertFrom-Json).packages.$RuntimeIdentifier)
} else { @() }

if ($metadata.Count -ne $lockedUrls.Count) {
    throw "Conda metadata count $($metadata.Count) does not match explicit lock count $($lockedUrls.Count)."
}
$metadataByUrl = @{}
foreach ($component in $metadata) { $metadataByUrl[$component.url] = $component }
foreach ($url in $lockedUrls) {
    if (-not $metadataByUrl.ContainsKey($url)) { throw "Installed conda metadata is missing locked URL: $url" }
}
foreach ($component in $metadata) {
    if ([string]::IsNullOrWhiteSpace($component.sha256)) {
        throw "Installed conda metadata is missing a SHA-256 checksum: $($component.name) $($component.version)"
    }
}

$condaPackages = for ($index = 0; $index -lt $metadata.Count; $index++) {
    $component = $metadata[$index]
    $safeName = $component.name -replace '[^A-Za-z0-9.-]', '-'
    [ordered]@{
        SPDXID = "SPDXRef-Package-$('{0:D4}' -f ($index + 1))-$safeName"
        name = $component.name
        versionInfo = $component.version
        downloadLocation = $component.url
        filesAnalyzed = $false
        checksums = @([ordered]@{ algorithm = 'SHA256'; checksumValue = $component.sha256 })
        licenseConcluded = 'NOASSERTION'
        licenseDeclared = 'NOASSERTION'
        copyrightText = 'NOASSERTION'
        comment = "Conda build $($component.build); declared license: $($component.license); family: $($component.license_family)"
    }
}
$wheelPackages = for ($index = 0; $index -lt $wheelMetadata.Count; $index++) {
    $component = $wheelMetadata[$index]
    $safeName = $component.name -replace '[^A-Za-z0-9.-]', '-'
    [ordered]@{
        SPDXID = "SPDXRef-Package-Wheel-$('{0:D4}' -f ($index + 1))-$safeName"
        name = $component.name
        versionInfo = $component.version
        downloadLocation = $component.url
        filesAnalyzed = $false
        checksums = @([ordered]@{ algorithm = 'SHA256'; checksumValue = $component.sha256 })
        licenseConcluded = $component.license
        licenseDeclared = $component.license
        copyrightText = 'NOASSERTION'
        comment = 'Locked Python wheel'
    }
}
$packages = @($condaPackages) + @($wheelPackages)
$sbom = [ordered]@{
    spdxVersion = 'SPDX-2.3'
    dataLicense = 'CC0-1.0'
    SPDXID = 'SPDXRef-DOCUMENT'
    name = "pathstitch-geometry-worker-$RuntimeIdentifier"
    documentNamespace = "https://pathstitch.local/spdx/geometry-worker/$RuntimeIdentifier/$lockHash"
    creationInfo = [ordered]@{
        created = '1970-01-01T00:00:00Z'
        creators = @('Tool: Pathstitch geometry-worker build-runtime.ps1')
    }
    packages = $packages
    relationships = @($packages | ForEach-Object {
        [ordered]@{ spdxElementId = 'SPDXRef-DOCUMENT'; relationshipType = 'DESCRIBES'; relatedSpdxElement = $_.SPDXID }
    })
}
$sbom | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root 'sbom.spdx.json') -Encoding utf8

$licenseFiles = @(Get-ChildItem $root -Recurse -File | Where-Object {
    $_.Name -match '(?i)^(license|licence|copying|notice|copyright)(\.|$)'
} | Sort-Object FullName | ForEach-Object {
    [ordered]@{
        path = $_.FullName.Substring($root.Length + 1).Replace('\', '/')
        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
})
$notices = [ordered]@{
    schemaVersion = 1
    runtimeIdentifier = $RuntimeIdentifier
    lockSha256 = $lockHash
    componentCount = $packages.Count
    components = @($metadata | ForEach-Object {
        [ordered]@{
            name = $_.name
            version = $_.version
            build = $_.build
            declaredLicense = if ($_.license) { $_.license } else { 'NOASSERTION' }
            licenseFamily = if ($_.license_family) { $_.license_family } else { 'NOASSERTION' }
            sourceUrl = $_.url
            sha256 = $_.sha256
        }
    }) + @($wheelMetadata | ForEach-Object {
        [ordered]@{
            name = $_.name
            version = $_.version
            build = 'wheel'
            declaredLicense = $_.license
            licenseFamily = $_.license
            sourceUrl = $_.url
            sha256 = $_.sha256
        }
    })
    licenseFiles = $licenseFiles
}
$notices | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $root 'third-party-notices.json') -Encoding utf8

[pscustomobject]@{
    RuntimeIdentifier = $RuntimeIdentifier
    Components = $packages.Count
    LicenseFiles = $licenseFiles.Count
    LockSha256 = $lockHash
}
