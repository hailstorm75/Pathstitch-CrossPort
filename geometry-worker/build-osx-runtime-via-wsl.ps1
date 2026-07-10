[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$LockFile,
    [Parameter(Mandatory)]
    [string]$Destination,
    [string]$Distribution = 'Ubuntu'
)

$ErrorActionPreference = 'Stop'
$micromambaVersion = '2.8.1'
$micromambaArchiveSha256 = 'a934c3709c997feae403a27fd1e321c106d26ffa4f294800ffb11cbc9a3e8515'
$lock = (Resolve-Path $LockFile).Path
$destinationPath = [System.IO.Path]::GetFullPath($Destination)
$archive = "$destinationPath.wsl.tar"

if (-not (Get-Command wsl.exe -ErrorAction SilentlyContinue)) {
    throw 'Building an osx-arm64 conda prefix on Windows requires WSL with an Ubuntu distribution.'
}
New-Item -ItemType Directory -Path (Split-Path -Parent $destinationPath) -Force | Out-Null
if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }

function Convert-ToWslPath([string]$path) {
    $converted = & wsl.exe -d $Distribution -- wslpath -a $path.Replace('\', '/')
    if ($LASTEXITCODE -ne 0) { throw "Could not translate path for WSL: $path" }
    return $converted.Trim()
}

$wslLock = Convert-ToWslPath $lock
$wslArchive = Convert-ToWslPath $archive
$lockHash = (Get-FileHash -LiteralPath $lock -Algorithm SHA256).Hash.ToLowerInvariant()
$shell = @"
set -euo pipefail
tool_root=/tmp/pathstitch-micromamba-$micromambaVersion
archive=/tmp/pathstitch-micromamba-$micromambaVersion.tar.bz2
prefix=/tmp/pathstitch-osx-prefix-$lockHash
mamba_root=/tmp/pathstitch-osx-mamba-$lockHash
if [ ! -x "`$tool_root/bin/micromamba" ]; then
  rm -rf "`$tool_root"
  mkdir -p "`$tool_root"
  curl -LsS https://micro.mamba.pm/api/micromamba/linux-64/$micromambaVersion -o "`$archive"
  printf '%s  %s\n' '$micromambaArchiveSha256' "`$archive" | sha256sum -c -
  python3 -m tarfile -e "`$archive" "`$tool_root"
fi
test "`$(`$tool_root/bin/micromamba --version)" = '$micromambaVersion'
rm -rf "`$prefix" "`$mamba_root"
MAMBA_ROOT_PREFIX="`$mamba_root" "`$tool_root/bin/micromamba" create -y -p "`$prefix" --file '$wslLock'
tar --dereference -C "`$prefix" -cf '$wslArchive' .
rm -rf "`$prefix" "`$mamba_root"
"@

try {
    & wsl.exe -d $Distribution -- bash -lc $shell
    if ($LASTEXITCODE -ne 0) { throw "WSL osx-arm64 prefix build failed with exit code $LASTEXITCODE" }
    New-Item -ItemType Directory -Path $destinationPath -Force | Out-Null
    & tar -xf $archive -C $destinationPath
    if ($LASTEXITCODE -ne 0) { throw "WSL osx-arm64 prefix extraction failed with exit code $LASTEXITCODE" }
}
finally {
    if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
}
