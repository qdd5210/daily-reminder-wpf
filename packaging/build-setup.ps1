$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$output = Join-Path $project 'release-1.4'
$payload = Join-Path $output 'app\DailyReminder.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$source = Join-Path $PSScriptRoot 'Setup.cs'
$icon = Join-Path $project 'assets\app-icon.ico'
$manifest = Join-Path $project 'app.manifest'
$uninstaller = Join-Path $output 'Uninstall.exe'
$installer = Join-Path $output 'DailyReminderSetup.exe'
if (-not (Test-Path -LiteralPath $payload)) { throw "Missing self-contained application: $payload" }
$references = @('/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll', '/reference:Microsoft.CSharp.dll')
& $compiler '/nologo' '/target:winexe' '/platform:x64' '/optimize+' '/define:UNINSTALLER' ("/out:$uninstaller") ("/win32icon:$icon") ("/win32manifest:$manifest") @references $source
if ($LASTEXITCODE -ne 0) { throw 'Uninstaller build failed' }
& $compiler '/nologo' '/target:winexe' '/platform:x64' '/optimize+' ("/out:$installer") ("/win32icon:$icon") ("/win32manifest:$manifest") ("/resource:$payload,DailyReminder.Payload") ("/resource:$uninstaller,DailyReminder.Uninstall") @references $source
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed' }
Get-Item -LiteralPath $installer | Select-Object FullName,Length,LastWriteTime
