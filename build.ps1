param([string]$OutputDirectory = 'dist')
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$wpf = Join-Path $framework 'WPF'
$out = Join-Path $PSScriptRoot $OutputDirectory
New-Item -ItemType Directory -Force -Path $out | Out-Null
$references = @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Runtime.Serialization.dll', 'System.Xml.dll', 'System.Xaml.dll', 'System.Security.dll', 'System.Management.dll') | ForEach-Object { '/r:' + (Join-Path $framework $_) }
$references += @('WindowsBase.dll', 'PresentationCore.dll', 'PresentationFramework.dll', 'UIAutomationClient.dll', 'UIAutomationTypes.dll') | ForEach-Object { '/r:' + (Join-Path $wpf $_) }
$sources = Get-ChildItem (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName }
& $compiler /nologo /target:winexe /optimize+ /platform:anycpu ('/out:' + (Join-Path $out 'ScreenTime.exe')) ('/resource:' + (Join-Path $PSScriptRoot 'src\Dashboard.xaml') + ',Dashboard.xaml') @references @sources
if ($LASTEXITCODE -ne 0) { throw 'App compilation failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\ScreenTime.exe.config') -Destination $out
& $compiler /nologo /target:exe ('/out:' + (Join-Path $out 'ScreenTime.Tests.exe')) ('/r:' + (Join-Path $out 'ScreenTime.exe')) @references (Join-Path $PSScriptRoot 'tests\Tests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& (Join-Path $out 'ScreenTime.Tests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
Write-Output "Built: $out\ScreenTime.exe"
