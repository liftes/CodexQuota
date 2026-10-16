[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [switch]$SkipBuild,
    [switch]$RunTests
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $PSScriptRoot 'dist' }
if ($SkipBuild -and $RunTests) { throw 'Use -RunTests to build and test, or -SkipBuild to package an existing build.' }
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'Build.ps1') -RunTests:$RunTests }
$binaryRoot = Join-Path $PSScriptRoot 'Release'
$executable = Join-Path $binaryRoot 'CodexQuota.exe'
$configuration = Join-Path $binaryRoot 'CodexQuota.exe.config'
foreach ($filePath in @($executable,$configuration,(Join-Path $PSScriptRoot 'Install.ps1'),(Join-Path $PSScriptRoot 'LICENSE'))) {
    if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) { throw "Required package file missing: $filePath" }
}
$fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($executable)
$version = '{0}.{1}.{2}' -f $fileVersion.FileMajorPart, $fileVersion.FileMinorPart, $fileVersion.FileBuildPart
$outputRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory).TrimEnd('\')
if (-not $outputRoot -or $outputRoot -eq [System.IO.Path]::GetPathRoot($outputRoot).TrimEnd('\')) { throw 'Choose an output folder, not a drive root.' }
if ($outputRoot -eq $binaryRoot -or $outputRoot -eq [System.IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')) { throw 'Output must differ from the source and Release folders.' }
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$stageRoot = Join-Path $outputRoot ('.staging-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stageRoot | Out-Null
$packageFiles = @('CodexQuota.exe','CodexQuota.exe.config','Install.ps1','README.txt')
Copy-Item -LiteralPath $executable -Destination (Join-Path $stageRoot 'CodexQuota.exe')
Copy-Item -LiteralPath $configuration -Destination (Join-Path $stageRoot 'CodexQuota.exe.config')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install.ps1') -Destination (Join-Path $stageRoot 'Install.ps1')
$quickStart = @"
CodexQuota $version (Windows 11 x64, .NET Framework 4.8)

Extract the complete ZIP, then run CodexQuota.exe. Keep its .exe.config beside it.
Install the official Codex CLI and sign in with a supported ChatGPT subscription.
The application does not bundle Codex or a subscription. This is an unofficial app.
Right-click the tray icon for settings and exit. A second instance will not run.

Optional per-user installation or update, from this extracted folder:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1 -Launch
Default destination: %LOCALAPPDATA%\Programs\CodexQuota
Custom destination: add -Destination "D:\Apps\CodexQuota"
Saved preferences: %LOCALAPPDATA%\CodexQuota\settings.json
If moving an existing installation, disable startup in the old app and enable it
again in the new location. The installer never changes startup registration.

To uninstall, turn off startup in Settings, exit the app, then remove its folder.
Saved preferences remain until you choose to remove them separately.
No account credentials, settings, screenshots, or diagnostic logs are bundled.

Copyright (C) 2026 CodexQuota contributors.
Licensed under GNU GPL version 3 only (GPL-3.0-only). See LICENSE.
This program comes WITHOUT ANY WARRANTY; see the license for details.
Obtain the corresponding source and build scripts from the matching release tag
in the GitHub repository linked on the release page.
"@
[System.IO.File]::WriteAllText((Join-Path $stageRoot 'README.txt'), $quickStart, (New-Object System.Text.UTF8Encoding($false)))
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -PathType Leaf) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination (Join-Path $stageRoot 'LICENSE')
    $packageFiles += 'LICENSE'
}
$archivePath = Join-Path $outputRoot ("CodexQuota-v$version-win-x64.zip")
$stagedPaths = @($packageFiles | ForEach-Object { Join-Path $stageRoot $_ })
Compress-Archive -LiteralPath $stagedPaths -DestinationPath $archivePath -CompressionLevel Optimal -Force
$looseExecutable = Join-Path $outputRoot 'CodexQuota.exe'
$looseConfiguration = Join-Path $outputRoot 'CodexQuota.exe.config'
Copy-Item -LiteralPath $executable -Destination $looseExecutable -Force
Copy-Item -LiteralPath $configuration -Destination $looseConfiguration -Force
$checksumLines = foreach ($packagePath in @($archivePath,$looseExecutable,$looseConfiguration)) {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant(), [System.IO.Path]::GetFileName($packagePath)
}
[System.IO.File]::WriteAllLines((Join-Path $outputRoot 'SHA256SUMS.txt'), [string[]]$checksumLines, (New-Object System.Text.UTF8Encoding($false)))
# Only the allowlisted staging files above are removed. No recursive deletion.
foreach ($stagedPath in $stagedPaths) { Remove-Item -LiteralPath $stagedPath -Force }
Remove-Item -LiteralPath $stageRoot
Write-Output "Packaged version: $version"
Write-Output "Portable ZIP: $archivePath"
Write-Output "Release files: $outputRoot"
