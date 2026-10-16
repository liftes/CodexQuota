param([Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string]$Destination, [switch]$Launch)
$ErrorActionPreference = 'Stop'
$sourceRoot = [System.IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
$targetRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Destination).TrimEnd('\')
if ($sourceRoot -eq $targetRoot) { throw 'Updater must run from the new package, outside the installed project.' }
$targetExecutable = Join-Path $targetRoot 'Release\CodexQuota.exe'
if (-not (Test-Path -LiteralPath (Join-Path $targetRoot 'CodexQuota.csproj')) -or -not (Test-Path -LiteralPath $targetExecutable)) {
    throw 'Installed CodexQuota project was not found. Run InstallToRequestedFolder.cmd for a first install.'
}
$topFiles = @('Build.ps1','App.config','app.manifest','CodexQuota.csproj','CopyToRequestedFolder.ps1','InstallToRequestedFolder.cmd','UpdateInstalledVersion.ps1','UpdateInstalledVersion.cmd','Install.ps1','Package.ps1','README.md','VALIDATION.md','.gitignore')
foreach ($optionalFile in @('.gitattributes','CHANGELOG.md','CONTRIBUTING.md','SECURITY.md','LICENSE')) {
    if (Test-Path -LiteralPath (Join-Path $sourceRoot $optionalFile) -PathType Leaf) { $topFiles += $optionalFile }
}
$relativeFiles = [System.Collections.Generic.List[string]]::new()
foreach ($topFile in $topFiles) { $relativeFiles.Add($topFile) }
foreach ($directory in @('src','tests','Release','assets','docs','.github')) {
    $directoryPath = Join-Path $sourceRoot $directory
    if (-not (Test-Path -LiteralPath $directoryPath -PathType Container)) { continue }
    foreach ($sourceFile in Get-ChildItem -LiteralPath $directoryPath -File -Recurse) {
        if ($directory -eq 'tests' -and $sourceFile.Extension -ne '.cs') { continue }
        if ($directory -eq 'Release' -and $sourceFile.Name -notin @('CodexQuota.exe','CodexQuota.exe.config')) { continue }
        $relativeFiles.Add($sourceFile.FullName.Substring($sourceRoot.Length + 1))
    }
}
foreach ($relativeFile in $relativeFiles) {
    $sourceFilePath = Join-Path $sourceRoot $relativeFile
    $targetFilePath = [System.IO.Path]::GetFullPath((Join-Path $targetRoot $relativeFile))
    if (-not (Test-Path -LiteralPath $sourceFilePath -PathType Leaf)) { throw "Package file missing: $relativeFile" }
    if (-not $targetFilePath.StartsWith($targetRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Destination escaped the project directory.' }
}
$packageVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $sourceRoot 'Release\CodexQuota.exe')).FileVersion
$backupRoot = Join-Path $targetRoot ('_Backups\' + $packageVersion + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$backedUpFiles = [System.Collections.Generic.List[string]]::new()
$createdFiles = [System.Collections.Generic.List[string]]::new()
# Back up before stopping the running application. Settings in LocalAppData are never touched.
foreach ($relativeFile in $relativeFiles) {
    $targetFilePath = Join-Path $targetRoot $relativeFile
    if (Test-Path -LiteralPath $targetFilePath -PathType Leaf) {
        $backupFilePath = Join-Path $backupRoot $relativeFile
        New-Item -ItemType Directory -Path ([System.IO.Path]::GetDirectoryName($backupFilePath)) -Force | Out-Null
        Copy-Item -LiteralPath $targetFilePath -Destination $backupFilePath -Force
        $backedUpFiles.Add($relativeFile)
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
    foreach ($relativeFile in $relativeFiles) {
        $targetFilePath = Join-Path $targetRoot $relativeFile
        if (-not (Test-Path -LiteralPath $targetFilePath)) { $createdFiles.Add($relativeFile) }
        New-Item -ItemType Directory -Path ([System.IO.Path]::GetDirectoryName($targetFilePath)) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $sourceRoot $relativeFile) -Destination $targetFilePath -Force
    }
} catch {
    foreach ($relativeFile in $backedUpFiles) { Copy-Item -LiteralPath (Join-Path $backupRoot $relativeFile) -Destination (Join-Path $targetRoot $relativeFile) -Force }
    foreach ($relativeFile in $createdFiles) {
        $targetFilePath = [System.IO.Path]::GetFullPath((Join-Path $targetRoot $relativeFile))
        if ($targetFilePath.StartsWith($targetRoot + '\', [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $targetFilePath -PathType Leaf)) { Remove-Item -LiteralPath $targetFilePath -Force }
    }
    if ($Launch) { Start-Process -FilePath $targetExecutable -WindowStyle Hidden }
    throw 'Update failed; previous files were restored. See the project backup directory.'
}
Write-Output "Updated CodexQuota to $packageVersion`: $targetRoot"
Write-Output "Previous files: $backupRoot"
Write-Output 'Existing account, position offsets and other saved settings are preserved.'
if ($Launch) { Start-Process -FilePath $targetExecutable -WindowStyle Hidden }
