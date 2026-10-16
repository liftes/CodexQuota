param([switch]$RunTests)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
$projectRoot = $PSScriptRoot
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compilerPath = Join-Path $frameworkRoot 'csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) { throw 'Windows .NET Framework C# compiler was not found.' }
$releasePath = Join-Path $projectRoot 'Release'
New-Item -ItemType Directory -Path $releasePath -Force | Out-Null
$assemblyRoot = Join-Path $env:WINDIR 'Microsoft.NET\assembly'
$referenceNames = @('PresentationFramework', 'PresentationCore', 'WindowsBase', 'UIAutomationClient', 'UIAutomationTypes', 'System.Xaml')
$referencePaths = foreach ($referenceName in $referenceNames) {
    $matches = @(Get-ChildItem -Path "$assemblyRoot\GAC_MSIL\$referenceName\*\$referenceName.dll", "$assemblyRoot\GAC_64\$referenceName\*\$referenceName.dll" -ErrorAction SilentlyContinue)
    if ($matches.Count -eq 0) { throw "Missing framework assembly: $referenceName" }
    $matches[0].FullName
}
$compilerArguments = @('/nologo','/codepage:65001','/langversion:5','/platform:x64','/target:winexe','/optimize+', '/main:CodexQuota.QuotaApplication', "/out:$releasePath\CodexQuota.exe", "/win32manifest:$projectRoot\app.manifest")
$compilerArguments += @('/r:System.Web.Extensions.dll','/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Core.dll')
$compilerArguments += @("/win32icon:$projectRoot\assets\CodexQuota.ico", "/resource:$projectRoot\assets\CodexQuota.ico,CodexQuota.AppIcon")
$compilerArguments += $referencePaths | ForEach-Object { "/r:$_" }
$compilerArguments += (Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs').FullName
& $compilerPath @compilerArguments
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'App.config') -Destination (Join-Path $releasePath 'CodexQuota.exe.config') -Force
function RunVerification {
    param([string]$Name, [string[]]$Sources)
    $testPath_ = Join-Path $projectRoot "tests\$Name.exe"
    $arguments_ = @('/nologo','/codepage:65001','/langversion:5','/platform:x64','/target:exe', "/out:$testPath_", '/r:System.Web.Extensions.dll','/r:System.Core.dll','/r:System.Windows.Forms.dll','/r:System.Drawing.dll')
    $arguments_ += "/win32manifest:$projectRoot\app.manifest"
    if ($Name -eq 'ApplicationTests') { $arguments_ += "/r:$releasePath\CodexQuota.exe" }
    $arguments_ += $referencePaths | ForEach-Object { "/r:$_" }
    $arguments_ += $Sources | ForEach-Object { Join-Path $projectRoot $_ }
    & $compilerPath @arguments_
    if ($LASTEXITCODE -ne 0) { throw "$Name compilation failed." }
    & $testPath_
    if ($LASTEXITCODE -ne 0) { throw "$Name failed." }
}
if ($RunTests) {
    RunVerification 'CoreTests' @('src\Core.cs','tests\CoreTests.cs')
    RunVerification 'AppearanceTests' @('src\Core.cs','src\Settings.cs','src\HoverAppearance.cs','src\RingControl.cs','src\RingLayout.cs','tests\AppearanceTests.cs')
    RunVerification 'ResetRingTests' @('src\Core.cs','src\RingControl.cs','tests\ResetRingTests.cs')
    RunVerification 'PositionTests' @('src\Native.cs','src\ShellWatcher.cs','tests\PositionTests.cs')
    RunVerification 'TaskbarHostTests' @('src\TaskbarHost.cs','tests\TaskbarHostTests.cs')
    RunVerification 'ApplicationTests' @('tests\ApplicationTests.cs')
    RunVerification 'ThemeTests' @('src\UiTheme.cs','tests\ThemeTests.cs')
    RunVerification 'HoverTests' @('src\Core.cs','src\RingControl.cs','src\UiTheme.cs','src\HoverAppearance.cs','src\QuotaHoverPresenter.cs','tests\HoverTests.cs')
    RunVerification 'StartupTests' @('src\StartupRegistration.cs','tests\StartupTests.cs')
    RunVerification 'SchedulerTests' @('src\Core.cs','src\QuotaPollScheduler.cs','tests\SchedulerTests.cs')
}
Write-Output "Built: $releasePath\CodexQuota.exe"
