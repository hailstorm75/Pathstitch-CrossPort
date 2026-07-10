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
    & xcrun swiftc `
        -O `
        -parse-as-library `
        -emit-executable `
        -Xlinker -e `
        -Xlinker _NSExtensionMain `
        -module-name $ModuleName `
        (Join-Path $repo "scripts/macos/quicklook/$SourceName") `
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
}

Write-Output $app
