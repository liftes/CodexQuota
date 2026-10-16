param([Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string]$Destination, [switch]$Launch)
$ErrorActionPreference = 'Stop'
$sourceRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$targetRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Destination).TrimEnd('\')
if ($sourceRoot.TrimEnd('\') -eq $targetRoot) { Write-Output 'The project is already in the requested folder.'; return }
$items = @('Build.ps1','App.config','app.manifest','CodexQuota.csproj','CopyToRequestedFolder.ps1','InstallToRequestedFolder.cmd','UpdateInstalledVersion.ps1','UpdateInstalledVersion.cmd','Install.ps1','Package.ps1','README.md','.gitignore','VALIDATION.md')
foreach ($optionalItem in @('.gitattributes','CHANGELOG.md','CONTRIBUTING.md','SECURITY.md','LICENSE')) {
    if (Test-Path -LiteralPath (Join-Path $sourceRoot $optionalItem) -PathType Leaf) { $items += $optionalItem }
}
foreach ($directory in @('src','tests','Release','assets','docs','.github')) {
    $directoryPath = Join-Path $sourceRoot $directory
    if (-not (Test-Path -LiteralPath $directoryPath -PathType Container)) { continue }
    foreach ($sourceFile in Get-ChildItem -LiteralPath $directoryPath -File -Recurse) {
        if ($directory -eq 'tests' -and $sourceFile.Extension -ne '.cs') { continue }
        if ($directory -eq 'Release' -and $sourceFile.Name -notin @('CodexQuota.exe','CodexQuota.exe.config')) { continue }
        $items += $sourceFile.FullName.Substring($sourceRoot.Length + 1)
    }
}
foreach ($item in $items) {
    if (-not (Test-Path -LiteralPath (Join-Path $sourceRoot $item))) { throw "Source item is missing: $item" }
    if (Test-Path -LiteralPath (Join-Path $targetRoot $item)) { throw "Existing item will not be overwritten: $item. Choose a new destination folder." }
}
New-Item -ItemType Directory -Path $targetRoot -Force | Out-Null
foreach ($item in $items) {
    $targetFilePath = [System.IO.Path]::GetFullPath((Join-Path $targetRoot $item))
    if (-not $targetFilePath.StartsWith($targetRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Destination escaped the project directory.' }
    New-Item -ItemType Directory -Path ([System.IO.Path]::GetDirectoryName($targetFilePath)) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $sourceRoot $item) -Destination $targetFilePath
}
Write-Output "Copied project to: $targetRoot"
Write-Output "Run: $targetRoot\Release\CodexQuota.exe"
if ($Launch) { Start-Process -FilePath (Join-Path $targetRoot 'Release\CodexQuota.exe') -WindowStyle Hidden }
