<#
.SYNOPSIS
  Install or update the SSW TimePro `tp` CLI from the latest GitHub Release.

.DESCRIPTION
  Verifies the .NET 10 SDK is present, downloads the latest released .nupkg, and
  installs it as a global tool (updating in place if it is already installed).
  Works on Windows PowerShell 5.1+ and PowerShell 7 (Windows, macOS, Linux).

.EXAMPLE
  ./scripts/install.ps1

.EXAMPLE
  irm https://raw.githubusercontent.com/SSWConsulting/TimePro.Tools/main/scripts/install.ps1 | iex

.NOTES
  Set the GITHUB_TOKEN environment variable to raise the GitHub API rate limit (optional).
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$Repo      = 'SSWConsulting/TimePro.Tools'
$PackageId = 'SSW.TimePro.Cli'
$ToolName  = 'tp'
$ApiUrl    = "https://api.github.com/repos/$Repo/releases/latest"

function Write-Info($m) { Write-Host "==> $m" -ForegroundColor Cyan }
function Write-Warn($m) { Write-Host "warning: $m" -ForegroundColor Yellow }

# --- 1. Verify the .NET 10 SDK -------------------------------------------------
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "The .NET SDK was not found on your PATH. Install the .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0"
}
if (-not ((& dotnet --list-sdks) | Where-Object { $_ -match '^10\.' })) {
    throw "No .NET 10 SDK detected. Install the .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0"
}

# --- 2. Resolve the latest release's .nupkg asset ------------------------------
Write-Info "Looking up the latest release of $Repo…"
$headers = @{ 'User-Agent' = 'timepro-install'; 'Accept' = 'application/vnd.github+json' }
if ($env:GITHUB_TOKEN) { $headers['Authorization'] = "Bearer $($env:GITHUB_TOKEN)" }

$release = Invoke-RestMethod -Uri $ApiUrl -Headers $headers
$asset = $release.assets | Where-Object { $_.name -like '*.nupkg' } | Select-Object -First 1
if (-not $asset) {
    throw "The latest release has no .nupkg asset attached. Has a non-dry-run release been published yet? See docs/Instructions-Deployment.md."
}

$version = $asset.name -replace ("^" + [regex]::Escape("$PackageId.")), '' -replace '\.nupkg$', ''
Write-Info "Latest release: $($release.tag_name) (package version $version)"

# --- 3. Download into a throwaway directory ------------------------------------
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("timepro-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp -Force | Out-Null
try {
    $dest = Join-Path $tmp $asset.name
    Write-Info "Downloading $($asset.name)…"
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $dest

    # --- 4. Install or update the global tool ----------------------------------
    # `dotnet tool update` installs when missing and updates otherwise, covering
    # both a fresh install and a reinstall.
    Write-Info "Installing $PackageId $version as a global tool…"
    & dotnet tool update --global --add-source $tmp --version $version $PackageId
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool update failed with exit code $LASTEXITCODE." }
}
finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}

# --- 5. Record installed version and check PATH --------------------------------
$toolsDir = Join-Path (Join-Path $HOME '.dotnet') 'tools'
$toolPathCandidates = @(
    (Join-Path $toolsDir $ToolName),
    (Join-Path $toolsDir "$ToolName.exe")
)
$toolPath = $toolPathCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($toolPath) {
    & $toolPath info --no-update-check *> $null
} elseif (Get-Command $ToolName -ErrorAction SilentlyContinue) {
    & $ToolName info --no-update-check *> $null
}

$onPath = ($env:PATH -split [System.IO.Path]::PathSeparator) -contains $toolsDir
if (-not $onPath) {
    Write-Warn "The .NET tools directory is not on your PATH: $toolsDir"
    Write-Warn "Open a new terminal (the .NET SDK usually adds it) or add it to PATH manually."
}

Write-Info "Installed $PackageId $version."
Write-Info "Next: $ToolName login --tenant <id>"
