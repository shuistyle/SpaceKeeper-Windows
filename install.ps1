<#
  install.ps1 - installs (or updates, or removes) SpaceKeeper for Windows.

  INSTALL or UPDATE - paste this into PowerShell (no administrator needed):
      powershell -c "irm https://raw.githubusercontent.com/shuistyle/SpaceKeeper-Windows/main/install.ps1 | iex"

  REMOVE:
      & ([scriptblock]::Create((irm https://raw.githubusercontent.com/shuistyle/SpaceKeeper-Windows/main/install.ps1))) -Uninstall

  WHAT IT DOES
    1. Finds the latest release on GitHub (or the one you ask for with -Version v1.0.7).
    2. Picks the right download for this PC (x64 for Intel/AMD, arm64 for Arm).
    3. Downloads it and CHECKS it against the published SHA-256 checksum.
       If they don't match, it stops and installs nothing.
    4. Installs to %LOCALAPPDATA%\Programs\SpaceKeeper - a private folder only
       your Windows account can change (the same place SpaceKeeper's own
       Install button uses). Your settings are kept.
    5. Adds SpaceKeeper to the Start menu and starts it.

  WHY THIS AVOIDS THE "WINDOWS PROTECTED YOUR PC" WARNING
    That warning appears because web browsers mark downloaded files as "from
    the internet". PowerShell doesn't add that mark, and the checksum check
    above replaces the protection the warning was meant to give.

  Nothing is sent anywhere except the downloads from github.com.
#>
[CmdletBinding()]
param(
    [string]$Version = "latest",   # e.g. v1.0.7
    [switch]$Uninstall,
    [switch]$RemoveSettings        # with -Uninstall: also delete saved settings
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"   # makes downloads much faster in Windows PowerShell
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$Repo        = "shuistyle/SpaceKeeper-Windows"
$InstallDir  = Join-Path $env:LOCALAPPDATA "Programs\SpaceKeeper"
$SettingsDir = Join-Path $env:LOCALAPPDATA "SpaceKeeper"
$Shortcut    = Join-Path ([Environment]::GetFolderPath("Programs")) "SpaceKeeper.lnk"
$RunKey      = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"

function Say($text)  { Write-Host "> $text" -ForegroundColor Cyan }
function Done($text) { Write-Host "OK $text" -ForegroundColor Green }

function Stop-SpaceKeeper {
    $running = Get-Process -Name "SpaceKeeper" -ErrorAction SilentlyContinue
    if ($running) {
        Say "Closing SpaceKeeper..."
        $running | Stop-Process -Force
        Start-Sleep -Milliseconds 800
    }
}

# ------------------------------------------------------------------ remove
if ($Uninstall) {
    Stop-SpaceKeeper
    if (Test-Path $InstallDir) { Remove-Item $InstallDir -Recurse -Force }
    if (Test-Path $Shortcut)   { Remove-Item $Shortcut -Force }
    Remove-ItemProperty -Path $RunKey -Name "SpaceKeeper" -ErrorAction SilentlyContinue
    if ($RemoveSettings -and (Test-Path $SettingsDir)) { Remove-Item $SettingsDir -Recurse -Force }
    Done "SpaceKeeper has been removed$(if (-not $RemoveSettings) { ' (your settings were kept)' })."
    return
}

# ------------------------------------------------------------------ which download?
$arch = $null
try { $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString() } catch { }
if (-not $arch) { $arch = $env:PROCESSOR_ARCHITECTURE }
$rid = if ($arch -match "Arm64") { "win-arm64" } else { "win-x64" }
$zipName = "SpaceKeeper-$rid.zip"

Say "Looking up the $Version release..."
$apiUrl = if ($Version -eq "latest") { "https://api.github.com/repos/$Repo/releases/latest" }
          else { "https://api.github.com/repos/$Repo/releases/tags/$Version" }
$release = Invoke-RestMethod -Uri $apiUrl -Headers @{ "User-Agent" = "SpaceKeeper-installer" }
$zipAsset  = $release.assets | Where-Object { $_.name -eq $zipName }
$sumsAsset = $release.assets | Where-Object { $_.name -eq "SHA256SUMS.txt" }
if (-not $zipAsset)  { throw "Release $($release.tag_name) has no $zipName." }
if (-not $sumsAsset) { throw "Release $($release.tag_name) has no SHA256SUMS.txt, so the download can't be checked. Nothing was installed." }

# ------------------------------------------------------------------ download and check
$work = Join-Path ([IO.Path]::GetTempPath()) ("SpaceKeeper-install-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    $zipPath = Join-Path $work $zipName
    Say "Downloading SpaceKeeper $($release.tag_name) for $rid..."
    Invoke-WebRequest -Uri $zipAsset.browser_download_url -OutFile $zipPath -UseBasicParsing
    $sums = (Invoke-WebRequest -Uri $sumsAsset.browser_download_url -UseBasicParsing).Content
    if ($sums -is [byte[]]) { $sums = [Text.Encoding]::UTF8.GetString($sums) }

    $expected = $null
    foreach ($line in ($sums -split "`n")) {
        $parts = $line.Trim() -split "\s+"
        if ($parts.Count -ge 2 -and $parts[-1].TrimStart("*") -eq $zipName) { $expected = $parts[0].ToUpperInvariant() }
    }
    if (-not $expected) { throw "SHA256SUMS.txt has no line for $zipName. Nothing was installed." }
    $actual = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($actual -ne $expected) {
        throw "The download doesn't match its published checksum (expected $expected, got $actual). It may be damaged or tampered with. Nothing was installed."
    }
    Done "Download checked (SHA-256 matches)."

    # -------------------------------------------------------------- install
    $staging = Join-Path $work "app"
    Expand-Archive -Path $zipPath -DestinationPath $staging
    if (-not (Test-Path (Join-Path $staging "SpaceKeeper.exe"))) { throw "The download doesn't contain SpaceKeeper.exe. Nothing was installed." }

    Stop-SpaceKeeper
    if (Test-Path $InstallDir) { Remove-Item $InstallDir -Recurse -Force }
    New-Item -ItemType Directory -Path (Split-Path $InstallDir) -Force | Out-Null
    Move-Item -Path $staging -Destination $InstallDir
    # Belt and braces: make sure no file carries a "downloaded from the internet" mark.
    Get-ChildItem $InstallDir -Recurse -File | Unblock-File
    Done "Installed to $InstallDir"

    $exe = Join-Path $InstallDir "SpaceKeeper.exe"
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($Shortcut)
    $link.TargetPath = $exe
    $link.WorkingDirectory = $InstallDir
    $link.Description = "Name, pin and reorder your virtual desktops"
    $link.Save()
    Done "Added to the Start menu."

    Start-Process -FilePath $exe -WorkingDirectory $InstallDir
    Done "SpaceKeeper $($release.tag_name) is running - look for its icon at the bottom-right of the taskbar (it may be under the ^ arrow)."
    Write-Host "  To update later, run the same command again."
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
