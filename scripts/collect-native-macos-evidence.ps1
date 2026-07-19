[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$EvidenceRoot,
    [Parameter(Mandatory)]
    [string]$PackageAcceptanceDirectory,
    [Parameter(Mandatory)]
    [string]$FileActivationDirectory,
    [Parameter(Mandatory)]
    [string]$QuickLookDirectory,
    [string]$RuntimeProbeDirectory = '',
    [Parameter(Mandatory)]
    [string]$PackageZip,
    [Parameter(Mandatory)]
    [string]$ExpectedActivationFixture,
    [string]$AppPath = '',
    [switch]$SkipPlatformCollection
)

$ErrorActionPreference = 'Stop'
$EvidenceRoot = [System.IO.Path]::GetFullPath($EvidenceRoot)
$volumeRoot = [System.IO.Path]::GetPathRoot($EvidenceRoot)
if ([string]::IsNullOrWhiteSpace($volumeRoot) -or
    $EvidenceRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) -eq
        $volumeRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar)) {
    throw 'EvidenceRoot must be a dedicated non-root directory.'
}
if ((Test-Path -LiteralPath $EvidenceRoot -PathType Container) -and -not $SkipPlatformCollection) {
    foreach ($item in Get-ChildItem -LiteralPath $EvidenceRoot -Force) {
        Remove-Item -LiteralPath $item.FullName -Recurse -Force
    }
} else {
    New-Item -ItemType Directory -Force -Path $EvidenceRoot | Out-Null
}
$validationErrors = [System.Collections.Generic.List[string]]::new()

function Add-ValidationError([string]$Message) {
    $validationErrors.Add($Message)
}

function Copy-IfPresent([string]$Source, [string]$DestinationName = '') {
    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) { return }
    $name = if ([string]::IsNullOrWhiteSpace($DestinationName)) {
        [System.IO.Path]::GetFileName($Source)
    } else { $DestinationName }
    Copy-Item -LiteralPath $Source -Destination (Join-Path $EvidenceRoot $name) -Force
}

function Get-FileEvidence([string]$Path) {
    $item = Get-Item -LiteralPath $Path
    [ordered]@{
        name = $item.Name
        bytes = $item.Length
        sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

function Read-JsonEvidence([string]$Path, [string]$Label) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Add-ValidationError "Missing $Label evidence: $([System.IO.Path]::GetFileName($Path))"
        return $null
    }
    try { return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json }
    catch {
        Add-ValidationError "Invalid $Label JSON: $($_.Exception.Message)"
        return $null
    }
}

function Test-TrueField($Value, [string]$Label) {
    if ($Value -ne $true) { Add-ValidationError "$Label must be true." }
}

function Test-Hash([string]$Value, [string]$Label) {
    if ($Value -notmatch '^[0-9a-fA-F]{64}$') {
        Add-ValidationError "$Label must contain a SHA-256 value."
        return $false
    }
    return $true
}

function Test-ZipArchive([string]$Path, [string]$Label) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return }
    try {
        $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
        try {
            if ($archive.Entries.Count -eq 0) { throw 'archive has no entries' }
            foreach ($entry in $archive.Entries) {
                if ([string]::IsNullOrEmpty($entry.Name)) { continue }
                $input = $entry.Open()
                try { $input.CopyTo([System.IO.Stream]::Null) }
                finally { $input.Dispose() }
            }
        } finally { $archive.Dispose() }
    } catch {
        Add-ValidationError "$Label is not a readable ZIP archive: $($_.Exception.Message)"
    }
}
function Test-DescribedArtifact($Descriptor, [string]$Label, [int64]$MinimumBytes = 101) {
    if ($null -eq $Descriptor) {
        Add-ValidationError "$Label descriptor is missing."
        return
    }
    $sourcePath = [string]$Descriptor.path
    $name = [System.IO.Path]::GetFileName($sourcePath)
    if ([string]::IsNullOrWhiteSpace($name)) {
        Add-ValidationError "$Label descriptor has no path."
        return
    }
    $path = Join-Path $EvidenceRoot $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        Add-ValidationError "$Label artifact is missing: $name"
        return
    }
    $actual = Get-FileEvidence $path
    if ($actual.bytes -lt $MinimumBytes) { Add-ValidationError "$Label artifact is trivial: $($actual.bytes) bytes." }
    if ([int64]$Descriptor.bytes -ne $actual.bytes) { Add-ValidationError "$Label byte count does not match $name." }
    if (Test-Hash ([string]$Descriptor.sha256) "$Label sha256") {
        if ([string]$Descriptor.sha256 -ne $actual.sha256) { Add-ValidationError "$Label SHA-256 does not match $name." }
    }
}

function Test-DxfDescriptorArtifact($Descriptor, [string]$Label) {
    Test-DescribedArtifact $Descriptor $Label
    if ($null -eq $Descriptor) { return }
    $properties = @($Descriptor.PSObject.Properties.Name)
    if ($properties -notcontains 'insUnitsCode' -or [int]$Descriptor.insUnitsCode -ne 4) {
        Add-ValidationError "$Label descriptor must report INSUNITS code 4."
    }
    if ($properties -notcontains 'measurementCode' -or [int]$Descriptor.measurementCode -ne 1) {
        Add-ValidationError "$Label descriptor must report MEASUREMENT code 1."
    }
}

function Get-DxfHeaderUnitEvidence([string[]]$Lines, [string]$Name, [string]$Label) {
    $insUnitsValues = [System.Collections.Generic.List[int]]::new()
    $measurementValues = [System.Collections.Generic.List[int]]::new()
    $inHeader = $false
    for ($index = 0; $index + 1 -lt $Lines.Count; $index += 2) {
        $code = $Lines[$index].Trim()
        $value = $Lines[$index + 1].Trim()
        if ($code -eq '0' -and $value -ieq 'SECTION') {
            $inHeader = $index + 3 -lt $Lines.Count -and
                $Lines[$index + 2].Trim() -eq '2' -and
                $Lines[$index + 3].Trim() -ieq 'HEADER'
            continue
        }
        if ($code -eq '0' -and $value -ieq 'ENDSEC') {
            $inHeader = $false
            continue
        }
        if (-not $inHeader -or $code -ne '9' -or $index + 3 -ge $Lines.Count -or
            $Lines[$index + 2].Trim() -ne '70') {
            continue
        }
        $integerValue = 0
        if (-not [int]::TryParse(
            $Lines[$index + 3].Trim(),
            [Globalization.NumberStyles]::Integer,
            [Globalization.CultureInfo]::InvariantCulture,
            [ref]$integerValue)) {
            continue
        }
        if ($value -ieq '$INSUNITS') { $insUnitsValues.Add($integerValue) }
        elseif ($value -ieq '$MEASUREMENT') { $measurementValues.Add($integerValue) }
    }

    if ($insUnitsValues.Count -ne 1 -or $insUnitsValues[0] -ne 4) {
        Add-ValidationError "$Label must contain exactly one HEADER $([char]36)INSUNITS=4 declaration."
    }
    if ($measurementValues.Count -ne 1 -or $measurementValues[0] -ne 1) {
        Add-ValidationError "$Label must contain exactly one HEADER $([char]36)MEASUREMENT=1 declaration."
    }
    [ordered]@{
        name = $Name
        insUnitsCode = if ($insUnitsValues.Count -eq 1) { $insUnitsValues[0] } else { $null }
        measurementCode = if ($measurementValues.Count -eq 1) { $measurementValues[0] } else { $null }
    }
}

function Get-DxfFileUnitEvidence([string]$Path, [string]$Label) {
    try {
        Get-DxfHeaderUnitEvidence ([System.IO.File]::ReadAllLines($Path)) ([System.IO.Path]::GetFileName($Path)) $Label
    } catch {
        Add-ValidationError "$Label could not be parsed as DXF: $($_.Exception.Message)"
        [ordered]@{
            name = [System.IO.Path]::GetFileName($Path)
            insUnitsCode = $null
            measurementCode = $null
        }
    }
}

$packageArtifactNames = @(
    'packaged-app-acceptance.json',
    'packaged-roundtrip.stch',
    'packaged-input.step',
    'packaged-input.obj',
    'packaged-input.stl',
    'packaged-projection.dxf',
    'packaged-unfold.dxf',
    'packaged-mixed.step',
    'packaged-obj-projection.dxf',
    'packaged-obj-connected.dxf',
    'packaged-obj-separate.dxf',
    'packaged-obj-tabs.dxf',
    'packaged-obj-holes.dxf'
)
foreach ($name in $packageArtifactNames) {
    Copy-IfPresent (Join-Path $PackageAcceptanceDirectory $name)
}
if (Test-Path -LiteralPath $PackageAcceptanceDirectory -PathType Container) {
    foreach ($dxfFile in Get-ChildItem -LiteralPath $PackageAcceptanceDirectory -Filter '*.dxf' -File) {
        Copy-IfPresent $dxfFile.FullName
    }
}
Copy-IfPresent (Join-Path $FileActivationDirectory 'file-activation-acceptance.json')
if (Test-Path -LiteralPath $QuickLookDirectory -PathType Container) {
    foreach ($file in Get-ChildItem -LiteralPath $QuickLookDirectory -File) {
        if ($file.Extension -eq '.png' -or
            $file.Name -eq 'quicklook-output-map.json' -or
            $file.Name -eq 'pluginkit-inventory.txt' -or
            $file.Name -like '*-qlmanage.log') {
            Copy-IfPresent $file.FullName
        }
    }
}
if (-not [string]::IsNullOrWhiteSpace($RuntimeProbeDirectory) -and
    (Test-Path -LiteralPath $RuntimeProbeDirectory -PathType Container)) {
    foreach ($file in Get-ChildItem -LiteralPath $RuntimeProbeDirectory -File) {
        if ($file.Extension -in @('.json', '.log', '.step')) {
            Copy-IfPresent $file.FullName
        }
    }
}
Copy-IfPresent $PackageZip 'Pathstitch-osx-arm64.zip'

if (-not $SkipPlatformCollection) {
    if ([string]::IsNullOrWhiteSpace($AppPath) -or -not (Test-Path -LiteralPath $AppPath -PathType Container)) {
        Add-ValidationError 'Packaged application bundle is unavailable for signature collection.'
    } else {
        $verificationLines = [System.Collections.Generic.List[string]]::new()
        $appVerification = ((& codesign --verify --deep --strict --verbose=4 $AppPath 2>&1) -join [Environment]::NewLine)
        $appVerificationExitCode = $LASTEXITCODE
        $verificationLines.Add(('## Pathstitch.app' + [Environment]::NewLine + $appVerification))
        if ($appVerificationExitCode -ne 0) {
            Add-ValidationError "Packaged application signature verification failed with exit code $appVerificationExitCode."
        }
        ((& codesign -dvvv $AppPath 2>&1) -join [Environment]::NewLine) |
            Set-Content (Join-Path $EvidenceRoot 'codesign-details.txt') -Encoding utf8
        ((& codesign -d --entitlements :- $AppPath 2>&1) -join [Environment]::NewLine) |
            Set-Content (Join-Path $EvidenceRoot 'app-entitlements.plist') -Encoding utf8
        foreach ($extension in @('PathstitchQuickLook', 'PathstitchThumbnail')) {
            $extensionPath = Join-Path $AppPath "Contents/PlugIns/$extension.appex"
            $extensionVerification = ((& codesign --verify --strict --verbose=4 $extensionPath 2>&1) -join [Environment]::NewLine)
            $extensionVerificationExitCode = $LASTEXITCODE
            $verificationLines.Add(('## ' + $extension + '.appex' + [Environment]::NewLine + $extensionVerification))
            if ($extensionVerificationExitCode -ne 0) {
                Add-ValidationError "$extension signature verification failed with exit code $extensionVerificationExitCode."
            }
            ((& codesign -d --entitlements :- $extensionPath 2>&1) -join [Environment]::NewLine) |
                Set-Content (Join-Path $EvidenceRoot "$extension-entitlements.plist") -Encoding utf8
        }
        $verificationLines | Set-Content (Join-Path $EvidenceRoot 'codesign-verification.txt') -Encoding utf8
        $nestedSignatures = foreach ($binary in Get-ChildItem -LiteralPath $AppPath -Recurse -File) {
            if (((& file -b $binary.FullName 2>$null) -join ' ') -notmatch 'Mach-O') { continue }
            "## $($binary.FullName.Substring((Resolve-Path $AppPath).Path.Length))"
            ((& codesign -dvvv $binary.FullName 2>&1) -join [Environment]::NewLine)
            ''
        }
        $nestedSignatures | Set-Content (Join-Path $EvidenceRoot 'nested-codesign-details.txt') -Encoding utf8
    }
}

$requiredNames = @(
    'packaged-app-acceptance.json',
    'file-activation-acceptance.json',
    'packaged-roundtrip.stch',
    'packaged-input.step',
    'packaged-input.obj',
    'packaged-input.stl',
    'packaged-projection.dxf',
    'packaged-unfold.dxf',
    'codesign-details.txt',
    'codesign-verification.txt',
    'nested-codesign-details.txt',
    'app-entitlements.plist',
    'PathstitchQuickLook-entitlements.plist',
    'PathstitchThumbnail-entitlements.plist',
    'quicklook-output-map.json',
    'pluginkit-inventory.txt',
    'sample-dxf-qlmanage.log',
    'sample-step-qlmanage.log',
    'sample-stch-qlmanage.log',
    'packaged-mixed.step',
    'packaged-obj-projection.dxf',
    'packaged-obj-connected.dxf',
    'packaged-obj-separate.dxf',
    'packaged-obj-tabs.dxf',
    'packaged-obj-holes.dxf',
    'Pathstitch-osx-arm64.zip',
    'app-group-writer-probe.json',
    'app-group-collector-probe.json',
    'quicklook-preview-runtime-probe.json',
    'quicklook-thumbnail-runtime-probe.json',
    'runtime-probe.step'
)
$missingRequiredFiles = @(
    foreach ($name in $requiredNames) {
        $path = Join-Path $EvidenceRoot $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) {
            $name
            Add-ValidationError "Required evidence file is missing or empty: $name"
        }
    }
)

$packageAcceptancePath = Join-Path $EvidenceRoot 'packaged-app-acceptance.json'
$packageAcceptance = Read-JsonEvidence $packageAcceptancePath 'packaged application acceptance'
if ($null -ne $packageAcceptance) {
    if ([int]$packageAcceptance.schemaVersion -lt 4) { Add-ValidationError 'Packaged acceptance schemaVersion must be at least 4.' }
    if ($packageAcceptance.status -ne 'passed') { Add-ValidationError 'Packaged acceptance status is not passed.' }
    Test-TrueField $packageAcceptance.webView.navigationCompleted 'webView.navigationCompleted'
    Test-TrueField $packageAcceptance.fileDialogs.CanOpen 'fileDialogs.CanOpen'
    Test-TrueField $packageAcceptance.fileDialogs.CanSave 'fileDialogs.CanSave'
    foreach ($field in @(
        'independentFinderPatternApplied',
        'independentFinderPatternReadBack',
        'finderDefaultsRestored',
        'finderDefaultsReadBack',
        'lightIconApplied',
        'darkIconApplied',
        'automaticIconApplied'
    )) { Test-TrueField $packageAcceptance.macIntegration.$field "macIntegration.$field" }
    foreach ($field in @('imported', 'projected', 'unfolded')) {
        Test-TrueField $packageAcceptance.stepWorkflow.$field "stepWorkflow.$field"
    }
    Test-DescribedArtifact $packageAcceptance.stepWorkflow.fixtureEvidence 'STEP input fixture'
    Test-DxfDescriptorArtifact $packageAcceptance.stepWorkflow.projection 'STEP projection'
    Test-DxfDescriptorArtifact $packageAcceptance.stepWorkflow.unfold 'STEP unfold'
    foreach ($field in @('viewportPreserved', 'topologyPreserved', 'sourceModelPreserved', 'generatedOutputPreserved')) {
        Test-TrueField $packageAcceptance.projectRoundTrip.$field "projectRoundTrip.$field"
    }
    Test-DescribedArtifact $packageAcceptance.projectRoundTrip 'Project round-trip STCH'
    Test-ZipArchive (Join-Path $EvidenceRoot 'packaged-roundtrip.stch') 'Project round-trip STCH'
    Test-Hash ([string]$packageAcceptance.projectRoundTrip.sourceSha256) 'projectRoundTrip.sourceSha256' | Out-Null
    Test-Hash ([string]$packageAcceptance.projectRoundTrip.reopenedSourceSha256) 'projectRoundTrip.reopenedSourceSha256' | Out-Null
    Test-Hash ([string]$packageAcceptance.projectRoundTrip.generatedOutputSha256) 'projectRoundTrip.generatedOutputSha256' | Out-Null
    Test-Hash ([string]$packageAcceptance.projectRoundTrip.reopenedGeneratedOutputSha256) 'projectRoundTrip.reopenedGeneratedOutputSha256' | Out-Null
    if ([string]$packageAcceptance.projectRoundTrip.sourceSha256 -ne
        [string]$packageAcceptance.projectRoundTrip.reopenedSourceSha256) {
        Add-ValidationError 'Project round-trip source hashes do not match.'
    }
    if ([string]$packageAcceptance.projectRoundTrip.generatedOutputSha256 -ne
        [string]$packageAcceptance.projectRoundTrip.reopenedGeneratedOutputSha256) {
        Add-ValidationError 'Project round-trip generated-output hashes do not match.'
    }
    if ([string]$packageAcceptance.projectRoundTrip.generatedOutputSha256 -ne
        [string]$packageAcceptance.stepWorkflow.unfold.sha256) {
        Add-ValidationError 'Project round-trip generated-output hash does not match the STEP unfold artifact.'
    }
    foreach ($field in @(
        'objImported',
        'stlImported',
        'mixedCombined',
        'projected',
        'connectedUnfolded',
        'separateUnfolded',
        'tabsDecorated',
        'holesDecorated'
    )) { Test-TrueField $packageAcceptance.meshWorkflow.$field "meshWorkflow.$field" }
    if ([int]$packageAcceptance.meshWorkflow.objBodyCount -ne 1 -or
        [int]$packageAcceptance.meshWorkflow.objFaceCount -ne 2 -or
        [int]$packageAcceptance.meshWorkflow.objEdgeCount -ne 5) {
        Add-ValidationError 'OBJ topology evidence must report one body, two faces, and five edges.'
    }
    if ([int]$packageAcceptance.meshWorkflow.stlBodyCount -ne 1 -or
        [int]$packageAcceptance.meshWorkflow.stlFaceCount -ne 2 -or
        [int]$packageAcceptance.meshWorkflow.stlEdgeCount -ne 5) {
        Add-ValidationError 'STL topology evidence must report one body, two faces, and five edges.'
    }
    if ([int]$packageAcceptance.meshWorkflow.mixedBodyCount -ne 4) {
        Add-ValidationError 'Mixed STEP + OBJ + STL evidence must report four bodies.'
    }
    Test-DescribedArtifact $packageAcceptance.meshWorkflow.objFixtureEvidence 'OBJ input fixture'
    Test-DescribedArtifact $packageAcceptance.meshWorkflow.stlFixtureEvidence 'STL input fixture'
    Test-DescribedArtifact $packageAcceptance.meshWorkflow.mixed 'Mixed normalized STEP'
    Test-DxfDescriptorArtifact $packageAcceptance.meshWorkflow.projection 'OBJ projection'
    Test-DxfDescriptorArtifact $packageAcceptance.meshWorkflow.connected 'OBJ connected unfold'
    Test-DxfDescriptorArtifact $packageAcceptance.meshWorkflow.separate 'OBJ separate unfold'
    Test-DxfDescriptorArtifact $packageAcceptance.meshWorkflow.tabs 'OBJ glue-tab unfold'
    Test-DxfDescriptorArtifact $packageAcceptance.meshWorkflow.holes 'OBJ sew-hole unfold'
}

$embeddedProjectDxfEvidence = $null
if ($null -ne $packageAcceptance) {
    $roundTripArchivePath = Join-Path $EvidenceRoot 'packaged-roundtrip.stch'
    if (Test-Path -LiteralPath $roundTripArchivePath -PathType Leaf) {
        try {
            $archive = [System.IO.Compression.ZipFile]::OpenRead($roundTripArchivePath)
            try {
                $projectEntry = $archive.GetEntry('project.json')
                if ($null -eq $projectEntry) { throw 'project.json is missing' }
                $reader = [System.IO.StreamReader]::new($projectEntry.Open())
                try { $projectJson = $reader.ReadToEnd() | ConvertFrom-Json }
                finally { $reader.Dispose() }
            } finally { $archive.Dispose() }

            if ([string]::IsNullOrWhiteSpace([string]$projectJson.dxfDataBase64)) {
                throw 'project.json.dxfDataBase64 is missing'
            }
            $embeddedBytes = [Convert]::FromBase64String([string]$projectJson.dxfDataBase64)
            $embeddedLines = @([Text.Encoding]::UTF8.GetString($embeddedBytes) -split '\r?\n')
            if ($embeddedLines.Count -gt 0 -and $embeddedLines[-1] -eq '') {
                $embeddedLines = @($embeddedLines[0..($embeddedLines.Count - 2)])
            }
            $embeddedProjectDxfEvidence = Get-DxfHeaderUnitEvidence $embeddedLines 'packaged-roundtrip.stch/project.json.dxfDataBase64' 'STCH embedded generated DXF'
            $embeddedHash = [Convert]::ToHexString(
                [Security.Cryptography.SHA256]::HashData($embeddedBytes)).ToLowerInvariant()
            $embeddedProjectDxfEvidence['bytes'] = $embeddedBytes.Length
            $embeddedProjectDxfEvidence['sha256'] = $embeddedHash
            foreach ($expected in @(
                [string]$packageAcceptance.stepWorkflow.unfold.sha256,
                [string]$packageAcceptance.projectRoundTrip.generatedOutputSha256,
                [string]$packageAcceptance.projectRoundTrip.reopenedGeneratedOutputSha256
            )) {
                if ($embeddedHash -ne $expected) {
                    Add-ValidationError 'STCH embedded generated DXF hash does not match external and round-trip evidence.'
                    break
                }
            }
        } catch {
            Add-ValidationError "STCH embedded generated DXF evidence is invalid: $($_.Exception.Message)"
        }
    }
}

$signatureVerificationPath = Join-Path $EvidenceRoot 'codesign-verification.txt'
if (Test-Path -LiteralPath $signatureVerificationPath -PathType Leaf) {
    $signatureVerification = Get-Content -LiteralPath $signatureVerificationPath -Raw
    foreach ($label in @('Pathstitch.app', 'PathstitchQuickLook.appex', 'PathstitchThumbnail.appex')) {
        if ($signatureVerification -notmatch [regex]::Escape($label)) {
            Add-ValidationError "Signature verification transcript does not cover $label."
        }
    }
}
$appEntitlementsPath = Join-Path $EvidenceRoot 'app-entitlements.plist'
if (Test-Path -LiteralPath $appEntitlementsPath -PathType Leaf) {
    $appEntitlements = Get-Content -LiteralPath $appEntitlementsPath -Raw
    if ($appEntitlements -notmatch 'group\.com\.pathstitch\.crossport') {
        Add-ValidationError 'Application entitlement evidence lacks the Pathstitch app group.'
    }
}
foreach ($extension in @('PathstitchQuickLook', 'PathstitchThumbnail')) {
    $entitlementsPath = Join-Path $EvidenceRoot "$extension-entitlements.plist"
    if (-not (Test-Path -LiteralPath $entitlementsPath -PathType Leaf)) { continue }
    $entitlements = Get-Content -LiteralPath $entitlementsPath -Raw
    if ($entitlements -notmatch 'group\.com\.pathstitch\.crossport' -or
        $entitlements -notmatch 'com\.apple\.security\.app-sandbox') {
        Add-ValidationError "$extension entitlement evidence lacks the app group or sandbox."
    }
}

$fileActivation = Read-JsonEvidence (Join-Path $EvidenceRoot 'file-activation-acceptance.json') 'file activation'
if ($null -ne $fileActivation) {
    if ($fileActivation.status -ne 'passed') { Add-ValidationError 'File activation status is not passed.' }
    $activationFiles = @($fileActivation.files)
    if ($activationFiles.Count -ne 1) { Add-ValidationError 'File activation must contain exactly one file.' }
    else {
        $expectedPath = [System.IO.Path]::GetFullPath($ExpectedActivationFixture)
        $actualPath = [System.IO.Path]::GetFullPath([string]$activationFiles[0])
        if ($actualPath -ne $expectedPath) { Add-ValidationError 'File activation fixture path does not match the expected DXF.' }
    }
    if ([string]$fileActivation.activePage -notmatch 'EditorPageViewModel') {
        Add-ValidationError 'File activation did not reach EditorPageViewModel.'
    }
}

$pluginInventoryPath = Join-Path $EvidenceRoot 'pluginkit-inventory.txt'
if (Test-Path -LiteralPath $pluginInventoryPath -PathType Leaf) {
    $pluginInventory = Get-Content -LiteralPath $pluginInventoryPath -Raw
    foreach ($identifier in @('com.pathstitch.crossport.quicklook', 'com.pathstitch.crossport.thumbnail')) {
        if ($pluginInventory -notmatch [regex]::Escape($identifier)) {
            Add-ValidationError "Plugin inventory does not contain $identifier."
        }
    }
}

$quickLookMap = Read-JsonEvidence (Join-Path $EvidenceRoot 'quicklook-output-map.json') 'Quick Look output map'
$expectedFixtures = @('sample.dxf', 'sample.step', 'sample.stch')
$quickLookEntries = @($quickLookMap)
if ($null -ne $quickLookMap) {
    if ($quickLookEntries.Count -ne 3) { Add-ValidationError 'Quick Look output map must contain exactly three entries.' }
    $actualFixtures = @($quickLookEntries.fixture | Sort-Object)
    if (($actualFixtures -join ',') -ne (($expectedFixtures | Sort-Object) -join ',')) {
        Add-ValidationError 'Quick Look fixture map does not cover exactly STCH, DXF, and STEP.'
    }
    $outputs = @($quickLookEntries.output)
    if (@($outputs | Sort-Object -Unique).Count -ne $outputs.Count) {
        Add-ValidationError 'Quick Look output map contains duplicate PNG outputs.'
    }
    foreach ($entry in $quickLookEntries) {
        $outputName = [System.IO.Path]::GetFileName([string]$entry.output)
        if ($outputName -ne [string]$entry.output -or [string]::IsNullOrWhiteSpace($outputName)) {
            Add-ValidationError "Quick Look map contains an invalid output path: $($entry.output)"
            continue
        }
        $pngPath = Join-Path $EvidenceRoot $outputName
        if (-not (Test-Path -LiteralPath $pngPath -PathType Leaf)) {
            Add-ValidationError "Quick Look PNG is missing: $outputName"
            continue
        }
        $actual = Get-FileEvidence $pngPath
        if ($actual.bytes -lt 1024) { Add-ValidationError "Quick Look PNG is trivial: $outputName ($($actual.bytes) bytes)." }
        if ([int64]$entry.bytes -ne $actual.bytes) { Add-ValidationError "Quick Look PNG byte count mismatch: $outputName" }
        if (Test-Hash ([string]$entry.sha256) "Quick Look $outputName sha256") {
            if ([string]$entry.sha256 -ne $actual.sha256) { Add-ValidationError "Quick Look PNG SHA-256 mismatch: $outputName" }
        }
    }
}
$quickLookPngs = @(Get-ChildItem -LiteralPath $EvidenceRoot -Filter '*.png' -File)
if ($quickLookPngs.Count -ne 3) { Add-ValidationError 'Native evidence must contain exactly three Quick Look PNGs.' }

function Test-RuntimeProbePreferences($Preferences, [string]$Label) {
    if ($null -eq $Preferences -or
        $Preferences.dxf -ne $false -or
        $Preferences.step -ne $true -or
        $Preferences.stch -ne $false) {
        Add-ValidationError "$Label must report dxf=false, step=true, stch=false."
    }
}

$writerProbe = Read-JsonEvidence (
    Join-Path $EvidenceRoot 'app-group-writer-probe.json') 'app-group writer runtime probe'
$collectorProbe = Read-JsonEvidence (
    Join-Path $EvidenceRoot 'app-group-collector-probe.json') 'app-group collector runtime probe'
$previewProbe = Read-JsonEvidence (
    Join-Path $EvidenceRoot 'quicklook-preview-runtime-probe.json') 'Quick Look preview runtime probe'
$thumbnailProbe = Read-JsonEvidence (
    Join-Path $EvidenceRoot 'quicklook-thumbnail-runtime-probe.json') 'Quick Look thumbnail runtime probe'
$runtimeFixturePath = Join-Path $EvidenceRoot 'runtime-probe.step'
$runtimeFixture = if (Test-Path -LiteralPath $runtimeFixturePath -PathType Leaf) {
    Get-FileEvidence $runtimeFixturePath
} else { $null }
$runtimeNonce = if ($null -ne $writerProbe) { [string]$writerProbe.nonce } else { '' }
if ($null -ne $writerProbe) {
    if ([int]$writerProbe.schemaVersion -lt 1 -or $writerProbe.status -ne 'passed' -or
        $writerProbe.action -ne 'prepare') {
        Add-ValidationError 'App-group writer runtime probe did not pass schema/action validation.'
    }
    if ($runtimeNonce -notmatch '^[0-9a-f]{32}$') {
        Add-ValidationError 'App-group runtime-probe nonce is invalid.'
    }
    if ([int]$writerProbe.processIdentifier -le 0) {
        Add-ValidationError 'App-group writer runtime probe lacks a process identifier.'
    }
    Test-RuntimeProbePreferences $writerProbe.expectedPreferences 'Writer expectedPreferences'
    Test-RuntimeProbePreferences $writerProbe.observedPreferences 'Writer observedPreferences'
}
if ($null -ne $collectorProbe) {
    if ([int]$collectorProbe.schemaVersion -lt 1 -or $collectorProbe.status -ne 'passed' -or
        $collectorProbe.action -ne 'collect' -or [string]$collectorProbe.nonce -ne $runtimeNonce) {
        Add-ValidationError 'App-group collector runtime probe did not pass schema/action/nonce validation.'
    }
    if ([int]$collectorProbe.processIdentifier -le 0 -or
        [int]$collectorProbe.processIdentifier -eq [int]$writerProbe.processIdentifier) {
        Add-ValidationError 'App-group collector must run in a separate identified app process.'
    }
    Test-RuntimeProbePreferences $collectorProbe.expectedPreferences 'Collector expectedPreferences'
    $collectedFiles = @($collectorProbe.collectedFiles)
    if ($collectedFiles.Count -ne 2) {
        Add-ValidationError 'App-group collector must describe exactly two extension attestations.'
    } else {
        foreach ($descriptor in $collectedFiles) {
            Test-DescribedArtifact $descriptor 'Collected extension attestation'
        }
    }
}
if ($null -ne $previewProbe) {
    if ([int]$previewProbe.schemaVersion -lt 1 -or $previewProbe.status -ne 'passed' -or
        $previewProbe.providerKind -ne 'preview' -or
        $previewProbe.bundleIdentifier -ne 'com.pathstitch.crossport.quicklook' -or
        [string]$previewProbe.nonce -ne $runtimeNonce) {
        Add-ValidationError 'Quick Look preview runtime probe failed identity validation.'
    }
    if ([int]$previewProbe.processIdentifier -le 0 -or
        [int]$previewProbe.processIdentifier -eq [int]$writerProbe.processIdentifier) {
        Add-ValidationError 'Quick Look preview must run in a separate identified extension process.'
    }
    Test-RuntimeProbePreferences $previewProbe.observedPreferences 'Preview observedPreferences'
    foreach ($field in @('rendered', 'interactiveSceneKit', 'sceneViewInstalled', 'cameraControlEnabled')) {
        Test-TrueField $previewProbe.$field "previewProbe.$field"
    }
    if ($previewProbe.fallbackImageInstalled -ne $false) {
        Add-ValidationError 'Quick Look STEP preview runtime probe must not use fallback image.'
    }
    if ([int]$previewProbe.vertexCount -le 0 -or [int]$previewProbe.triangleCount -le 0) {
        Add-ValidationError 'Quick Look STEP preview runtime probe lacks mesh geometry.'
    }
}
if ($null -ne $thumbnailProbe) {
    if ([int]$thumbnailProbe.schemaVersion -lt 1 -or $thumbnailProbe.status -ne 'passed' -or
        $thumbnailProbe.providerKind -ne 'thumbnail' -or
        $thumbnailProbe.bundleIdentifier -ne 'com.pathstitch.crossport.thumbnail' -or
        [string]$thumbnailProbe.nonce -ne $runtimeNonce) {
        Add-ValidationError 'Quick Look thumbnail runtime probe failed identity validation.'
    }
    if ([int]$thumbnailProbe.processIdentifier -le 0 -or
        [int]$thumbnailProbe.processIdentifier -eq [int]$writerProbe.processIdentifier) {
        Add-ValidationError 'Quick Look thumbnail must run in a separate identified extension process.'
    }
    Test-RuntimeProbePreferences $thumbnailProbe.observedPreferences 'Thumbnail observedPreferences'
    Test-TrueField $thumbnailProbe.rendered 'thumbnailProbe.rendered'
}
if ($null -ne $runtimeFixture -and $null -ne $previewProbe -and $null -ne $thumbnailProbe) {
    foreach ($probe in @($previewProbe, $thumbnailProbe)) {
        if ([string]$probe.fixture -ne $runtimeFixture.name -or
            [string]$probe.fixtureSha256 -ne $runtimeFixture.sha256) {
            Add-ValidationError 'Quick Look runtime probe fixture name or SHA-256 does not match retained STEP.'
        }
    }
}

$runtimeProbeEvidence = [ordered]@{
    nonce = $runtimeNonce
    writerProcessIdentifier = if ($null -ne $writerProbe) {
        $writerProbe.processIdentifier
    } else { $null }
    collectorProcessIdentifier = if ($null -ne $collectorProbe) {
        $collectorProbe.processIdentifier
    } else { $null }
    preview = if ($null -ne $previewProbe) {
        [ordered]@{
            bundleIdentifier = $previewProbe.bundleIdentifier
            processIdentifier = $previewProbe.processIdentifier
            fixture = $previewProbe.fixture
            fixtureSha256 = $previewProbe.fixtureSha256
            observedPreferences = $previewProbe.observedPreferences
            interactiveSceneKit = $previewProbe.interactiveSceneKit
            sceneViewInstalled = $previewProbe.sceneViewInstalled
            cameraControlEnabled = $previewProbe.cameraControlEnabled
            fallbackImageInstalled = $previewProbe.fallbackImageInstalled
            vertexCount = $previewProbe.vertexCount
            triangleCount = $previewProbe.triangleCount
        }
    } else { $null }
    thumbnail = if ($null -ne $thumbnailProbe) {
        [ordered]@{
            bundleIdentifier = $thumbnailProbe.bundleIdentifier
            processIdentifier = $thumbnailProbe.processIdentifier
            fixture = $thumbnailProbe.fixture
            fixtureSha256 = $thumbnailProbe.fixtureSha256
            observedPreferences = $thumbnailProbe.observedPreferences
            rendered = $thumbnailProbe.rendered
        }
    } else { $null }
}

$dxfUnitEvidence = @(
    foreach ($dxfFile in Get-ChildItem -LiteralPath $EvidenceRoot -Filter '*.dxf' -File | Sort-Object Name) {
        Get-DxfFileUnitEvidence $dxfFile.FullName "Retained DXF $($dxfFile.Name)"
    }
)
if ($dxfUnitEvidence.Count -eq 0) {
    Add-ValidationError 'Native evidence contains no retained DXF files.'
}

$packagePath = Join-Path $EvidenceRoot 'Pathstitch-osx-arm64.zip'
$package = if (Test-Path -LiteralPath $packagePath -PathType Leaf) { Get-FileEvidence $packagePath } else { $null }
if ($null -eq $package -or $package.bytes -le 100) { Add-ValidationError 'Self-contained package ZIP is missing or trivial.' }
Test-ZipArchive $packagePath 'Self-contained package ZIP'

$inventory = @(
    Get-ChildItem -LiteralPath $EvidenceRoot -File |
        Where-Object Name -ne 'native-evidence-manifest.json' |
        Sort-Object Name |
        ForEach-Object { Get-FileEvidence $_.FullName }
)
$status = if ($validationErrors.Count -eq 0) { 'passed' } else { 'failed' }
$manifest = [ordered]@{
    schemaVersion = 5
    status = $status
    commit = $env:GITHUB_SHA
    workflowRunId = $env:GITHUB_RUN_ID
    workflowRunAttempt = $env:GITHUB_RUN_ATTEMPT
    workflowRunUrl = if ($env:GITHUB_REPOSITORY -and $env:GITHUB_RUN_ID) {
        "https://github.com/$env:GITHUB_REPOSITORY/actions/runs/$env:GITHUB_RUN_ID"
    } else { $null }
    runner = [ordered]@{
        imageOs = $env:ImageOS
        imageVersion = $env:ImageVersion
        architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    }
    generatedUtc = [DateTimeOffset]::UtcNow
    package = $package
    evidenceFiles = $inventory
    missingRequiredFiles = $missingRequiredFiles
    quickLookThumbnailCount = $quickLookPngs.Count
    dxfUnitEvidence = $dxfUnitEvidence
    embeddedProjectDxfEvidence = $embeddedProjectDxfEvidence
    runtimeProbeEvidence = $runtimeProbeEvidence
    validationErrors = @($validationErrors)
}
$manifestPath = Join-Path $EvidenceRoot 'native-evidence-manifest.json'
$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $manifestPath -Encoding utf8

if ($status -ne 'passed') {
    Write-Error "Native evidence validation failed: $($validationErrors -join ' | ')"
    exit 1
}
Write-Output $manifestPath
