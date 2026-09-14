# upgrade-slideshow.ps1
# Standalone "Upgrade Slideshow" Start Menu action.
# Performs the same download/compare/elevate-install flow as pressing Ctrl+U
# inside the running screensaver app (see andyScreenSaver/UpgradeManager.cs),
# without needing to launch the app itself.

Add-Type -AssemblyName System.Windows.Forms

# GitHub requires TLS 1.2+; PowerShell 5.1 on older .NET defaults may not
# negotiate it, which causes "the connection was closed unexpectedly".
[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor [System.Net.SecurityProtocolType]::Tls12

$downloadsFolder = Join-Path $env:USERPROFILE "Downloads"
$installerPath   = Join-Path $downloadsFolder "smugAndyLatest.exe"
$checksumPath    = Join-Path $downloadsFolder "smugAndyLatest.md5"
$installerUrl    = "https://github.com/wholeCan/smugScreensaver/blob/main/nsisInstaller/andysScreensaverInstaller_small.exe?raw=true"

function Show-Info($message) {
    [System.Windows.Forms.MessageBox]::Show($message, "Upgrade Slideshow", `
        [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
}

function Show-ErrorBox($message) {
    [System.Windows.Forms.MessageBox]::Show($message, "Upgrade Slideshow", `
        [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
}

try {
    if (-not (Test-Path $downloadsFolder)) {
        New-Item -ItemType Directory -Path $downloadsFolder -Force | Out-Null
    }

    $oldChecksum = $null
    if (Test-Path $checksumPath) {
        $oldChecksum = (Get-Content -Path $checksumPath -Raw).Trim()
    }

    $notifyIcon = New-Object System.Windows.Forms.NotifyIcon
    $notifyIcon.Icon = [System.Drawing.SystemIcons]::Information
    $notifyIcon.Visible = $true
    $notifyIcon.Text = "Upgrade Slideshow"
    $notifyIcon.ShowBalloonTip(0, "Upgrade Slideshow", "Checking for updates, downloading if needed...", [System.Windows.Forms.ToolTipIcon]::Info)

    $previousProgressPreference = $ProgressPreference
    $ProgressPreference = "SilentlyContinue" # avoid PS 5.1's slow default progress-bar rendering
    try {
        Invoke-WebRequest -Uri $installerUrl -OutFile $installerPath -UseBasicParsing
    }
    finally {
        $ProgressPreference = $previousProgressPreference
        $notifyIcon.Visible = $false
        $notifyIcon.Dispose()
    }

    $newChecksum = (Get-FileHash -Path $installerPath -Algorithm SHA256).Hash.ToLower()

    if ($oldChecksum -and ($oldChecksum -eq $newChecksum)) {
        Show-Info "You already have the latest version of the slideshow."
        Remove-Item -Path $installerPath -Force -ErrorAction SilentlyContinue
        exit 0
    }

    # Record the checksum before launching, matching UpgradeManager.PerformUpgrade().
    Set-Content -Path $checksumPath -Value $newChecksum -NoNewline

    try {
        Start-Process -FilePath $installerPath -Verb RunAs -ErrorAction Stop
    }
    catch {
        Show-ErrorBox "The upgrade was cancelled or could not start elevated: $($_.Exception.Message)"
        exit 1
    }
}
catch {
    Show-ErrorBox "Could not check for or download the update: $($_.Exception.Message)`nYou may need to upgrade manually."
    exit 1
}
