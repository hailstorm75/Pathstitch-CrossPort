[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$OutputDirectory = "",
    [string]$SigningIdentity = "-",
    [switch]$Notarize,
    [string]$DistributionEvidenceDirectory = "",
    [switch]$PruneNativeInputsBeforeArchive
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repo "artifacts/macos"
}
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
$publish = Join-Path $output "publish"
$app = Join-Path $output "Pathstitch.app"
$contents = Join-Path $app "Contents"
$macos = Join-Path $contents "MacOS"
$resources = Join-Path $contents "Resources"
$frameworks = Join-Path $contents "Frameworks"
$swiftTarget = "arm64-apple-macos13.0"
$zipFoundationRevision = "22787ffb59de99e5dc1fbfe80b19c97a904ad48d"
$zipFoundationRoot = Join-Path $output "swift-packages/ZIPFoundation"
$distributionEvidence = if ([string]::IsNullOrWhiteSpace($DistributionEvidenceDirectory)) {
    Join-Path $output "distribution-evidence"
} else {
    [IO.Path]::GetFullPath($DistributionEvidenceDirectory)
}

if ($Notarize) {
    if ($SigningIdentity -notmatch '^Developer ID Application: .+ \([A-Z0-9]+\)$') {
        throw "-Notarize requires an exact Developer ID Application identity."
    }
    foreach ($name in "APPLE_ID", "APPLE_TEAM_ID", "APPLE_APP_SPECIFIC_PASSWORD") {
        if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
            throw "$name is required for notarization"
        }
    }
    $identityTeamIdentifier = [regex]::Match(
        $SigningIdentity,
        '\((?<team>[A-Z0-9]+)\)$').Groups["team"].Value
    if ($identityTeamIdentifier -ne $env:APPLE_TEAM_ID) {
        throw "Developer ID identity team does not match APPLE_TEAM_ID."
    }
}

if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
New-Item -ItemType Directory -Path $macos -Force | Out-Null
New-Item -ItemType Directory -Path $resources -Force | Out-Null
New-Item -ItemType Directory -Path $frameworks -Force | Out-Null
& git init --quiet $zipFoundationRoot
if ($LASTEXITCODE -ne 0) { throw "ZIPFoundation checkout initialization failed" }
& git -C $zipFoundationRoot remote add origin "https://github.com/weichsel/ZIPFoundation.git"
if ($LASTEXITCODE -ne 0) { throw "ZIPFoundation remote configuration failed" }
& git -C $zipFoundationRoot fetch --quiet --depth 1 origin $zipFoundationRevision
if ($LASTEXITCODE -ne 0) { throw "ZIPFoundation pinned source fetch failed" }
& git -C $zipFoundationRoot checkout --quiet --detach FETCH_HEAD
if ($LASTEXITCODE -ne 0) { throw "ZIPFoundation pinned source checkout failed" }
$resolvedZipFoundationRevision = (& git -C $zipFoundationRoot rev-parse HEAD).Trim()
if ($resolvedZipFoundationRevision -ne $zipFoundationRevision) {
    throw "ZIPFoundation revision mismatch: $resolvedZipFoundationRevision"
}
$zipFoundationSources = @(
    Get-ChildItem -LiteralPath (Join-Path $zipFoundationRoot "Sources/ZIPFoundation") -Filter "*.swift" -File |
        Sort-Object Name |
        ForEach-Object FullName
)
if ($zipFoundationSources.Count -eq 0) { throw "ZIPFoundation Swift sources were not resolved" }


dotnet publish (Join-Path $repo "src/Pathstitch.App/Pathstitch.App.csproj") `
    -r osx-arm64 `
    -p:PublishProfile=osx-arm64 `
    -p:Configuration=$Configuration `
    -p:PublishDir=$publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Get-ChildItem -LiteralPath $publish -Force | Move-Item -Destination $macos -Force
Remove-Item -LiteralPath $publish -Force
Copy-Item -LiteralPath (Join-Path $repo "scripts/macos/Info.plist") -Destination (Join-Path $contents "Info.plist")
Copy-Item -LiteralPath (Join-Path $repo "Pathstitch/Pathstitch/Assets.xcassets/AppIconLight.imageset/icon_512x512_2x.png") -Destination (Join-Path $resources "AppIconLight.png")
Copy-Item -LiteralPath (Join-Path $repo "Pathstitch/Pathstitch/Assets.xcassets/AppIconDark.imageset/icon_512x512_2x_dark.png") -Destination (Join-Path $resources "AppIconDark.png")
$iconSource = Join-Path $repo "Pathstitch/Pathstitch/Assets.xcassets/AppIconLight.imageset/icon_512x512_2x.png"
$iconset = Join-Path $output "Pathstitch.iconset"
New-Item -ItemType Directory -Path $iconset -Force | Out-Null
foreach ($baseSize in @(16, 32, 128, 256, 512)) {
    foreach ($scale in @(1, 2)) {
        $pixels = $baseSize * $scale
        $suffix = if ($scale -eq 2) { "@2x" } else { "" }
        $destination = Join-Path $iconset (("icon_{0}x{0}{1}.png" -f $baseSize, $suffix))
        & sips -z $pixels $pixels $iconSource --out $destination | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Application icon raster generation failed at $pixels px"
        }
    }
}
& iconutil -c icns -o (Join-Path $resources "Pathstitch.icns") $iconset
if ($LASTEXITCODE -ne 0) { throw "Application .icns generation failed" }
Remove-Item -LiteralPath $iconset -Recurse -Force

New-Item -ItemType Directory -Path (Join-Path $resources "ThirdPartyLicenses") -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $zipFoundationRoot "LICENSE") -Destination `
    (Join-Path $resources "ThirdPartyLicenses/ZIPFoundation-LICENSE")
$macBridge = Join-Path $frameworks "libPathstitchMacBridge.dylib"
$macBridgeArguments = @(
    "-O",
    "-parse-as-library",
    "-emit-library",
    (Join-Path $repo "scripts/macos/PathstitchMacBridge.swift"),
    "-target",
    $swiftTarget,
    "-o",
    $macBridge
)
& xcrun swiftc @macBridgeArguments
if ($LASTEXITCODE -ne 0) { throw "macOS integration bridge compilation failed" }
& chmod +x (Join-Path $macos "Pathstitch.App")
& chmod +x (Join-Path $macos "Assets/OpenGeometry/runtime/node")
& chmod +x (Join-Path $macos "GeometryWorker/osx-arm64/bin/python3.11")

function Assert-Arm64MachO([string]$Path) {
    $architectures = (& lipo -archs $Path 2>&1) -join " "
    if ($LASTEXITCODE -ne 0 -or $architectures -notmatch '(^|\s)arm64(\s|$)') {
        throw "Packaged executable is not arm64: $Path ($architectures)"
    }
}

function Assert-NoDeveloperRuntimeDependencies([string]$BundleRoot) {
    $forbiddenPrefixes = @('/opt/homebrew/', '/usr/local/', '/opt/local/', '/Users/', '/private/var/folders/')
    Get-ChildItem -LiteralPath $BundleRoot -Recurse -File | ForEach-Object {
        $kind = (& file -b $_.FullName 2>$null) -join " "
        if ($kind -notmatch 'Mach-O') { return }
        $dependencies = & otool -L $_.FullName 2>$null
        if ($LASTEXITCODE -ne 0) { throw "Could not inspect Mach-O dependencies: $($_.FullName)" }
        foreach ($dependency in $dependencies | Select-Object -Skip 1) {
            $installName = ($dependency.Trim() -split '\s+\(')[0]
            if ($forbiddenPrefixes | Where-Object { $installName.StartsWith($_, [StringComparison]::Ordinal) }) {
                throw "Developer-machine dependency '$installName' is embedded in $($_.FullName)"
            }
        }
    }
}

function Build-QuickLookExtension {
    param(
        [string]$Name,
        [string]$ModuleName,
        [string]$SourceName,
        [string]$PlistName,
        [string[]]$ExtraSourceNames = @()
    )

    $extension = Join-Path $contents "PlugIns/$Name.appex"
    $extensionContents = Join-Path $extension "Contents"
    $extensionMacOS = Join-Path $extensionContents "MacOS"
    New-Item -ItemType Directory -Path $extensionMacOS -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repo "scripts/macos/quicklook/$PlistName") -Destination (Join-Path $extensionContents "Info.plist")
    $executable = Join-Path $extensionMacOS $Name
    $extensionSources = @(
        (Join-Path $repo "scripts/macos/quicklook/$SourceName")
    )
    $extensionSources += @(
        $ExtraSourceNames | ForEach-Object {
            Join-Path $repo "scripts/macos/quicklook/$_"
        }
    )
    $sharedSources = @(
        (Join-Path $repo "Pathstitch/DxfPreviewer/DxfPreviewShared.swift"),
        (Join-Path $repo "Pathstitch/DxfPreviewer/StepPreviewShared.swift"),
        (Join-Path $repo "scripts/macos/quicklook/QuickLookPreferences.swift"),
        (Join-Path $repo "scripts/macos/quicklook/QuickLookRuntimeProbe.swift")
    )
    & xcrun swiftc `
        -O `
        -target $swiftTarget `
        -whole-module-optimization `
        -parse-as-library `
        -emit-executable `
        -Xlinker -e `
        -Xlinker _NSExtensionMain `
        -module-name $ModuleName `
        $extensionSources `
        $sharedSources `
        -import-objc-header (Join-Path $repo "native/step_mesh/include/StepMesh-Bridging.h") `
        $zipFoundationSources `
        (Join-Path $repo "native/step_mesh/lib/libstep_mesh.a") `
        -o $executable
    if ($LASTEXITCODE -ne 0) { throw "$Name Swift compilation failed" }
    & chmod +x $executable
    & plutil -lint (Join-Path $extensionContents "Info.plist")
    if ($LASTEXITCODE -ne 0) { throw "$Name Info.plist validation failed" }
}

Build-QuickLookExtension `
    -Name "PathstitchQuickLook" `
    -ModuleName "PathstitchQuickLook" `
    -SourceName "PreviewProvider.swift" `
    -PlistName "PreviewInfo.plist" `
    -ExtraSourceNames @("InteractiveStepPreview.swift")
Build-QuickLookExtension `
    -Name "PathstitchThumbnail" `
    -ModuleName "PathstitchThumbnail" `
    -SourceName "ThumbnailProvider.swift" `
    -PlistName "ThumbnailInfo.plist"

Assert-Arm64MachO (Join-Path $macos "Pathstitch.App")
Assert-Arm64MachO $macBridge
Assert-Arm64MachO (Join-Path $macos "Assets/OpenGeometry/runtime/node")
Assert-Arm64MachO (Join-Path $macos "GeometryWorker/osx-arm64/bin/python3.11")
Assert-Arm64MachO (Join-Path $contents "PlugIns/PathstitchQuickLook.appex/Contents/MacOS/PathstitchQuickLook")
Assert-Arm64MachO (Join-Path $contents "PlugIns/PathstitchThumbnail.appex/Contents/MacOS/PathstitchThumbnail")
Assert-NoDeveloperRuntimeDependencies $app

$previewSmoke = Join-Path $output "preview-geometry-smoke"
& xcrun swiftc `
    -O `
    -module-name "PathstitchPreviewGeometrySmoke" `
    -target $swiftTarget `
    -whole-module-optimization `
    (Join-Path $repo "scripts/macos/quicklook/PreviewGeometrySmoke.swift") `
    (Join-Path $repo "Pathstitch/DxfPreviewer/DxfPreviewShared.swift") `
    (Join-Path $repo "Pathstitch/DxfPreviewer/StepPreviewShared.swift") `
    -import-objc-header (Join-Path $repo "native/step_mesh/include/StepMesh-Bridging.h") `
    (Join-Path $repo "native/step_mesh/lib/libstep_mesh.a") `
    $zipFoundationSources `
    -o $previewSmoke
if ($LASTEXITCODE -ne 0) { throw "Quick Look geometry smoke compilation failed" }
$stchSmokeSource = Join-Path $output "stch-smoke-source"
New-Item -ItemType Directory -Path $stchSmokeSource -Force | Out-Null
$dxfSmokeFixture = Join-Path $repo "scripts/macos/quicklook/Fixtures/preview-smoke.dxf"
$projectPayload = @{
    dxfDataBase64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($dxfSmokeFixture))
}
$projectPayload | ConvertTo-Json -Compress | Set-Content (Join-Path $stchSmokeSource "project.json") -Encoding utf8
$stchSmokeZip = Join-Path $output "preview-smoke.zip"
Compress-Archive -LiteralPath (Join-Path $stchSmokeSource "project.json") -DestinationPath $stchSmokeZip
$stchSmokeFixture = Move-Item -LiteralPath $stchSmokeZip -Destination (Join-Path $output "preview-smoke.stch") -PassThru
& $previewSmoke `
    (Join-Path $repo "scripts/macos/quicklook/Fixtures/preview-smoke.dxf") `
    (Join-Path $repo "tests/Pathstitch.App.Tests/Fixtures/analytic-multibody-hole.step") `
    $stchSmokeFixture.FullName `
    (Join-Path $output "preview-smoke")
if ($LASTEXITCODE -ne 0) { throw "Quick Look geometry smoke failed" }
& plutil -lint (Join-Path $contents "Info.plist")
if ($LASTEXITCODE -ne 0) { throw "Application Info.plist validation failed" }

if (Get-Command codesign -ErrorAction SilentlyContinue) {
    $previewExtension = Join-Path $contents "PlugIns/PathstitchQuickLook.appex"
    $thumbnailExtension = Join-Path $contents "PlugIns/PathstitchThumbnail.appex"
    $extensionEntitlements = Join-Path $repo "scripts/macos/quicklook/PathstitchQuickLook.entitlements"
    $timestampArguments = if ($SigningIdentity -eq "-") { @() } else { @("--timestamp") }

    if ($Notarize) {
        if (-not (Get-Command security -ErrorAction SilentlyContinue)) {
            throw "security is required to validate Developer ID identity."
        }
        $signingIdentityOutput = (& security find-identity -v -p codesigning 2>&1) -join [Environment]::NewLine
        if ($LASTEXITCODE -ne 0 -or
            -not $signingIdentityOutput.Contains('"' + $SigningIdentity + '"', [StringComparison]::Ordinal)) {
            throw "Exact Developer ID Application identity is unavailable: $SigningIdentity"
        }
    }

    $machOFiles = @(
        Get-ChildItem -LiteralPath $app -Recurse -File | Where-Object {
            ((& file -b $_.FullName 2>$null) -join " ") -match "Mach-O"
        } | Sort-Object { $_.FullName.Length } -Descending
    )
    foreach ($machOFile in $machOFiles) {
        & codesign --force --options runtime @timestampArguments --sign $SigningIdentity $machOFile.FullName
        if ($LASTEXITCODE -ne 0) {
            throw "Nested Mach-O signing failed: $($machOFile.FullName)"
        }
    }
    & codesign --force --options runtime @timestampArguments --entitlements $extensionEntitlements --sign $SigningIdentity $previewExtension
    if ($LASTEXITCODE -ne 0) { throw "Quick Look preview extension signing failed" }
    & codesign --force --options runtime @timestampArguments --entitlements $extensionEntitlements --sign $SigningIdentity $thumbnailExtension
    if ($LASTEXITCODE -ne 0) { throw "Quick Look thumbnail extension signing failed" }
    & codesign --force --options runtime @timestampArguments --entitlements (Join-Path $repo "scripts/macos/Pathstitch.entitlements") --sign $SigningIdentity $app
    if ($LASTEXITCODE -ne 0) { throw "application signing failed" }

    & codesign --verify --strict --verbose=2 $previewExtension
    if ($LASTEXITCODE -ne 0) { throw "Quick Look preview extension verification failed" }
    foreach ($machOFile in $machOFiles) {
        & codesign --verify --strict --verbose=2 $machOFile.FullName
        if ($LASTEXITCODE -ne 0) {
            throw "Nested Mach-O verification failed: $($machOFile.FullName)"
        }
    }
    & codesign --verify --strict --verbose=2 $thumbnailExtension
    if ($LASTEXITCODE -ne 0) { throw "Quick Look thumbnail extension verification failed" }
    & codesign --verify --deep --strict --verbose=2 $app
    if ($LASTEXITCODE -ne 0) { throw "application signing verification failed" }
} elseif ($Notarize) {
    throw "codesign is required for Developer ID distribution."
}

$zip = Join-Path $output "Pathstitch-osx-arm64.zip"
Remove-Item -LiteralPath (Split-Path -Parent $zipFoundationRoot) -Recurse -Force
if ($PruneNativeInputsBeforeArchive) {
    $runtimeSource = [IO.Path]::GetFullPath(
        (Join-Path $repo "artifacts/geometry-worker/osx-arm64"))
    $runtimeRoot = [IO.Path]::GetFullPath(
        (Join-Path $repo "artifacts/geometry-worker"))
    if (-not $runtimeSource.StartsWith($runtimeRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to prune packaged runtime outside $runtimeRoot"
    }
    if (Test-Path -LiteralPath $runtimeSource) {
        Remove-Item -LiteralPath $runtimeSource -Recurse -Force
    }
}

if (-not (Get-Command ditto -ErrorAction SilentlyContinue)) {
    throw "ditto is required to create the macOS archive."
}
& ditto -c -k --keepParent $app $zip
if ($LASTEXITCODE -ne 0) { throw "app archive creation failed" }
if (-not (Test-Path -LiteralPath $zip -PathType Leaf) -or
    (Get-Item -LiteralPath $zip).Length -le 0) {
    throw "app archive is missing or empty"
}
& unzip -t $zip
if ($LASTEXITCODE -ne 0) { throw "app archive readability check failed" }

if ($Notarize) {
    New-Item -ItemType Directory -Path $distributionEvidence -Force | Out-Null
    $submissionPath = Join-Path $distributionEvidence "notarytool-submit.json"
    $notarySubmissionLines = @(
        & xcrun notarytool submit $zip `
            --apple-id $env:APPLE_ID `
            --team-id $env:APPLE_TEAM_ID `
            --password $env:APPLE_APP_SPECIFIC_PASSWORD `
            --wait `
            --output-format json 2>&1
    )
    $notarySubmitExitCode = $LASTEXITCODE
    $notarySubmissionText = $notarySubmissionLines -join [Environment]::NewLine
    [IO.File]::WriteAllText(
        $submissionPath,
        $notarySubmissionText,
        [Text.UTF8Encoding]::new($false))
    if ($notarySubmitExitCode -ne 0) { throw "notarization failed" }

    try {
        $notarySubmission = $notarySubmissionText | ConvertFrom-Json
    } catch {
        throw "notarytool returned invalid JSON: $($_.Exception.Message)"
    }
    if ($notarySubmission.status -ne "Accepted" -or
        [string]::IsNullOrWhiteSpace([string]$notarySubmission.id)) {
        throw "notarization was not accepted"
    }
    $submissionId = [string]$notarySubmission.id

    $notaryLogPath = Join-Path $distributionEvidence "notarytool-log.json"
    $notaryLogLines = @(
        & xcrun notarytool log $submissionId `
            --apple-id $env:APPLE_ID `
            --team-id $env:APPLE_TEAM_ID `
            --password $env:APPLE_APP_SPECIFIC_PASSWORD `
            --output-format json 2>&1
    )
    $notaryLogExitCode = $LASTEXITCODE
    [IO.File]::WriteAllText(
        $notaryLogPath,
        ($notaryLogLines -join [Environment]::NewLine),
        [Text.UTF8Encoding]::new($false))
    if ($notaryLogExitCode -ne 0) { throw "notarization log retrieval failed" }

    & xcrun stapler staple $app
    if ($LASTEXITCODE -ne 0) { throw "notarization stapling failed" }
    & xcrun stapler validate $app
    if ($LASTEXITCODE -ne 0) { throw "notarization ticket validation failed" }

    $codeSignVerificationLines = @(
        & codesign --verify --deep --strict --verbose=4 $app 2>&1
    )
    $codeSignVerificationExitCode = $LASTEXITCODE
    [IO.File]::WriteAllText(
        (Join-Path $distributionEvidence "codesign-verification.txt"),
        ($codeSignVerificationLines -join [Environment]::NewLine),
        [Text.UTF8Encoding]::new($false))
    if ($codeSignVerificationExitCode -ne 0) {
        throw "stapled application signing verification failed"
    }

    $codeSignDetailsLines = @(& codesign -d --verbose=4 $app 2>&1)
    $codeSignDetailsExitCode = $LASTEXITCODE
    $codeSignDetails = $codeSignDetailsLines -join [Environment]::NewLine
    [IO.File]::WriteAllText(
        (Join-Path $distributionEvidence "codesign-details.txt"),
        $codeSignDetails,
        [Text.UTF8Encoding]::new($false))
    if ($codeSignDetailsExitCode -ne 0 -or
        -not $codeSignDetails.Contains(
            "Authority=$SigningIdentity",
            [StringComparison]::Ordinal) -or
        -not $codeSignDetails.Contains(
            "TeamIdentifier=$env:APPLE_TEAM_ID",
            [StringComparison]::Ordinal) -or
        $codeSignDetails -notmatch '(?m)^Timestamp=' -or
        $codeSignDetails -notmatch '(?m)^flags=.*\(runtime\)') {
        throw "Developer ID authority, team, secure timestamp, or hardened runtime is missing"
    }

    $gatekeeperLines = @(
        & spctl --assess --type execute --verbose=4 $app 2>&1
    )
    $gatekeeperExitCode = $LASTEXITCODE
    $gatekeeperText = $gatekeeperLines -join [Environment]::NewLine
    [IO.File]::WriteAllText(
        (Join-Path $distributionEvidence "gatekeeper-assessment.txt"),
        $gatekeeperText,
        [Text.UTF8Encoding]::new($false))
    if ($gatekeeperExitCode -ne 0 -or $gatekeeperText -notmatch '(?i)accepted') {
        throw "Gatekeeper assessment failed"
    }

    Remove-Item -LiteralPath $zip -Force
    & ditto -c -k --keepParent $app $zip
    if ($LASTEXITCODE -ne 0) { throw "stapled app archive recreation failed" }
    & unzip -t $zip
    if ($LASTEXITCODE -ne 0) { throw "stapled app archive readability check failed" }

    $zipInfo = Get-Item -LiteralPath $zip
    $distributionManifest = [ordered]@{
        schemaVersion = 1
        status = "passed"
        signingIdentity = $SigningIdentity
        secureTimestamp = $true
        hardenedRuntime = $true
        notarization = [ordered]@{
            status = [string]$notarySubmission.status
            submissionId = $submissionId
            submissionFile = "notarytool-submit.json"
            logFile = "notarytool-log.json"
        }
        staplerValidated = $true
        gatekeeperAccepted = $true
        applicationGroup = "group.com.pathstitch.crossport"
        package = [ordered]@{
            file = $zipInfo.Name
            bytes = $zipInfo.Length
            sha256 = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    $distributionManifest | ConvertTo-Json -Depth 6 |
        Set-Content -LiteralPath (Join-Path $distributionEvidence "distribution-evidence.json") -Encoding utf8NoBOM
}

Write-Output $app
