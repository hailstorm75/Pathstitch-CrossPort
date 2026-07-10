[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$OutputDirectory = "",
    [string]$SigningIdentity = "-",
    [switch]$Notarize
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

if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
New-Item -ItemType Directory -Path $macos -Force | Out-Null

dotnet publish (Join-Path $repo "src/Pathstitch.App/Pathstitch.App.csproj") `
    -r osx-arm64 `
    -p:PublishProfile=osx-arm64 `
    -p:Configuration=$Configuration `
    -p:PublishDir=$publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Copy-Item -Path (Join-Path $publish "*") -Destination $macos -Recurse -Force
Copy-Item -LiteralPath (Join-Path $repo "scripts/macos/Info.plist") -Destination (Join-Path $contents "Info.plist")
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
        [string]$PlistName
    )

    $extension = Join-Path $contents "PlugIns/$Name.appex"
    $extensionContents = Join-Path $extension "Contents"
    $extensionMacOS = Join-Path $extensionContents "MacOS"
    New-Item -ItemType Directory -Path $extensionMacOS -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repo "scripts/macos/quicklook/$PlistName") -Destination (Join-Path $extensionContents "Info.plist")
    $executable = Join-Path $extensionMacOS $Name
    $sharedSources = @(
        (Join-Path $repo "Pathstitch/DxfPreviewer/DxfPreviewShared.swift"),
        (Join-Path $repo "Pathstitch/DxfPreviewer/StepPreviewShared.swift")
    )
    & xcrun swiftc `
        -O `
        -parse-as-library `
        -emit-executable `
        -Xlinker -e `
        -Xlinker _NSExtensionMain `
        -module-name $ModuleName `
        (Join-Path $repo "scripts/macos/quicklook/$SourceName") `
        $sharedSources `
        -import-objc-header (Join-Path $repo "native/step_mesh/include/StepMesh-Bridging.h") `
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
    -PlistName "PreviewInfo.plist"
Build-QuickLookExtension `
    -Name "PathstitchThumbnail" `
    -ModuleName "PathstitchThumbnail" `
    -SourceName "ThumbnailProvider.swift" `
    -PlistName "ThumbnailInfo.plist"

Assert-Arm64MachO (Join-Path $macos "Pathstitch.App")
Assert-Arm64MachO (Join-Path $macos "Assets/OpenGeometry/runtime/node")
Assert-Arm64MachO (Join-Path $macos "GeometryWorker/osx-arm64/bin/python3.11")
Assert-Arm64MachO (Join-Path $contents "PlugIns/PathstitchQuickLook.appex/Contents/MacOS/PathstitchQuickLook")
Assert-Arm64MachO (Join-Path $contents "PlugIns/PathstitchThumbnail.appex/Contents/MacOS/PathstitchThumbnail")
Assert-NoDeveloperRuntimeDependencies $app

$previewSmoke = Join-Path $output "preview-geometry-smoke"
& xcrun swiftc `
    -O `
    -module-name "PathstitchPreviewGeometrySmoke" `
    (Join-Path $repo "scripts/macos/quicklook/PreviewGeometrySmoke.swift") `
    (Join-Path $repo "Pathstitch/DxfPreviewer/DxfPreviewShared.swift") `
    (Join-Path $repo "Pathstitch/DxfPreviewer/StepPreviewShared.swift") `
    -import-objc-header (Join-Path $repo "native/step_mesh/include/StepMesh-Bridging.h") `
    (Join-Path $repo "native/step_mesh/lib/libstep_mesh.a") `
    -o $previewSmoke
if ($LASTEXITCODE -ne 0) { throw "Quick Look geometry smoke compilation failed" }
& $previewSmoke `
    (Join-Path $repo "scripts/macos/quicklook/Fixtures/preview-smoke.dxf") `
    (Join-Path $repo "tests/Pathstitch.App.Tests/Fixtures/analytic-multibody-hole.step") `
    (Join-Path $output "preview-smoke")
if ($LASTEXITCODE -ne 0) { throw "Quick Look geometry smoke failed" }
& plutil -lint (Join-Path $contents "Info.plist")
if ($LASTEXITCODE -ne 0) { throw "Application Info.plist validation failed" }

if (Get-Command codesign -ErrorAction SilentlyContinue) {
    & codesign --force --deep --options runtime --entitlements (Join-Path $repo "scripts/macos/Pathstitch.entitlements") --sign $SigningIdentity $app
    if ($LASTEXITCODE -ne 0) { throw "codesign failed" }
    & codesign --verify --deep --strict --verbose=2 $app
    if ($LASTEXITCODE -ne 0) { throw "codesign verification failed" }
}

$zip = Join-Path $output "Pathstitch-osx-arm64.zip"
if (Get-Command ditto -ErrorAction SilentlyContinue) {
    & ditto -c -k --keepParent $app $zip
    if ($LASTEXITCODE -ne 0) { throw "app archive creation failed" }
}

if ($Notarize) {
    foreach ($name in "APPLE_ID", "APPLE_TEAM_ID", "APPLE_APP_PASSWORD") {
        if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
            throw "$name is required for notarization"
        }
    }
    & xcrun notarytool submit $zip --apple-id $env:APPLE_ID --team-id $env:APPLE_TEAM_ID --password $env:APPLE_APP_PASSWORD --wait
    if ($LASTEXITCODE -ne 0) { throw "notarization failed" }
    & xcrun stapler staple $app
    if ($LASTEXITCODE -ne 0) { throw "notarization stapling failed" }
    & xcrun stapler validate $app
    if ($LASTEXITCODE -ne 0) { throw "notarization ticket validation failed" }
    & ditto -c -k --keepParent $app $zip
    if ($LASTEXITCODE -ne 0) { throw "stapled app archive recreation failed" }
}

Write-Output $app
