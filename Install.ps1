# Install or update portable binaries. No administrator access is required.
[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()][string]$Destination = (Join-Path $env:LOCALAPPDATA 'Programs\CodexQuota'),
    [switch]$Launch
)
$ErrorActionPreference = 'Stop'
$sourceRoot = [System.IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
$binaryRoot = $sourceRoot
if (-not (Test-Path -LiteralPath (Join-Path $binaryRoot 'CodexQuota.exe') -PathType Leaf)) { $binaryRoot = Join-Path $sourceRoot 'Release' }
$targetRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Destination).TrimEnd('\')
if (-not $targetRoot -or $targetRoot -eq [System.IO.Path]::GetPathRoot($targetRoot).TrimEnd('\')) { throw 'Choose an application folder, not a drive root.' }
if ($targetRoot -eq $sourceRoot -or $targetRoot -eq $binaryRoot) { throw 'The installation folder must differ from the package folder.' }
$files = @('CodexQuota.exe','CodexQuota.exe.config')
foreach ($file in $files) {
    if (-not (Test-Path -LiteralPath (Join-Path $binaryRoot $file) -PathType Leaf)) { throw "Missing $file. Build the source or extract the complete release ZIP first." }
}
$targetExecutable = Join-Path $targetRoot 'CodexQuota.exe'
$packageVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $binaryRoot 'CodexQuota.exe')).FileVersion
$backupRoot = Join-Path $targetRoot ('_Backups\' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$backedUpFiles = New-Object 'System.Collections.Generic.List[string]'
$createdFiles = New-Object 'System.Collections.Generic.List[string]'
foreach ($file in $files) {
    $targetFilePath = [System.IO.Path]::GetFullPath((Join-Path $targetRoot $file))
    if (-not $targetFilePath.StartsWith($targetRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Destination escaped the installation directory.' }
    if (Test-Path -LiteralPath $targetFilePath -PathType Leaf) {
        New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
        Copy-Item -LiteralPath $targetFilePath -Destination (Join-Path $backupRoot $file)
        $backedUpFiles.Add($file)
    }
}
foreach ($installedProcess in Get-Process -Name 'CodexQuota' -ErrorAction SilentlyContinue) {
    $installedProcessPath = $null
    try { $installedProcessPath = $installedProcess.Path } catch { continue }
    if ($installedProcessPath -and [string]::Equals([System.IO.Path]::GetFullPath($installedProcessPath), $targetExecutable, [StringComparison]::OrdinalIgnoreCase)) {
        $null = $installedProcess.CloseMainWindow()
        if (-not $installedProcess.WaitForExit(1500)) { Stop-Process -Id $installedProcess.Id -Force -ErrorAction Stop }
    }
}
try {
    New-Item -ItemType Directory -Path $targetRoot -Force | Out-Null
    foreach ($file in $files) {
        $targetFilePath = Join-Path $targetRoot $file
        if (-not (Test-Path -LiteralPath $targetFilePath)) { $createdFiles.Add($file) }
        Copy-Item -LiteralPath (Join-Path $binaryRoot $file) -Destination $targetFilePath -Force
    }
} catch {
    foreach ($file in $backedUpFiles) { Copy-Item -LiteralPath (Join-Path $backupRoot $file) -Destination (Join-Path $targetRoot $file) -Force }
    foreach ($file in $createdFiles) {
        $targetFilePath = [System.IO.Path]::GetFullPath((Join-Path $targetRoot $file))
        if ($targetFilePath.StartsWith($targetRoot + '\', [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $targetFilePath -PathType Leaf)) { Remove-Item -LiteralPath $targetFilePath -Force }
    }
    throw 'Installation failed; previous application files were restored.'
}
Write-Output "Installed CodexQuota $packageVersion to: $targetRoot"
if ($backedUpFiles.Count -gt 0) { Write-Output "Previous binaries: $backupRoot" }
Write-Output 'Saved preferences and startup registration were not changed. Moving an existing installation requires re-enabling startup in the new location.'
if ($Launch) { Start-Process -FilePath $targetExecutable -WindowStyle Hidden }
