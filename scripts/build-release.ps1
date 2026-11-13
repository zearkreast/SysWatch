# Builds the release files into dist\:
#   SysWatch_x64-setup.exe     installer (needs Inno Setup 6; skipped with a warning when ISCC is missing)
#   SysWatch_x64-portable.zip  SysWatch.exe + portable.txt + licences
# The build uses its own artifacts folder, so a running SysWatch (which locks bin\) does not get in the way.
[CmdletBinding()]
param(
    [string]$Version,
    [string]$Iscc
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $Version) {
    $Version = ([xml](Get-Content SysWatch.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Unexpected version '$Version'." }
Write-Host "SysWatch $Version"

$dist = Join-Path $root 'dist'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
$publish = Join-Path $dist 'publish'

# Framework-dependent single file: about 12 MB instead of ~150 MB self-contained (the desktop runtime brings WPF too).
dotnet publish SysWatch.csproj -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:DebugType=none -p:Version=$Version `
    --artifacts-path (Join-Path $dist 'artifacts') -o $publish
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
$published = @(Get-ChildItem $publish -File)
if ($published.Count -ne 1 -or $published[0].Name -ne 'SysWatch.exe') {
    throw "Expected only SysWatch.exe in the publish folder, found: $($published.Name -join ', ')"
}

$portable = Join-Path $dist 'portable\SysWatch'
New-Item -ItemType Directory -Force $portable | Out-Null
Copy-Item (Join-Path $publish 'SysWatch.exe') $portable
Copy-Item installer\portable.txt $portable
Copy-Item LICENSE (Join-Path $portable 'LICENSE.txt')
Copy-Item THIRD-PARTY-NOTICES.md (Join-Path $portable 'THIRD-PARTY-NOTICES.txt')
Copy-Item licenses (Join-Path $portable 'licenses') -Recurse
Compress-Archive -Path $portable -DestinationPath (Join-Path $dist 'SysWatch_x64-portable.zip')

if (-not $Iscc) {
    $Iscc = @(
        (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source,
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
}
if ($Iscc) {
    & $Iscc "/DAppVersion=$Version" "/DPublishDir=$publish" "/DOutputDir=$dist" installer\SysWatch.iss
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed.' }
} else {
    Write-Warning 'Inno Setup 6 (ISCC.exe) not found; the installer was not built.'
}

Get-ChildItem $dist -File | Format-Table Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
