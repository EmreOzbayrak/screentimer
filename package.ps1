param(
    [string]$BuildDirectory = 'dist',
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.2.2',
    [string]$CertificateThumbprint,
    [string]$TimestampServer = 'http://timestamp.digicert.com'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$root = $PSScriptRoot
$sourceFiles = @('.gitignore', '.gitattributes', 'README.md', 'LICENSE', 'PRIVACY.md', 'SECURITY.md', 'PUBLISHING.md', 'build.ps1', 'install.ps1', 'package.ps1', 'dist/.gitkeep')
foreach ($folder in @('src', 'tests', 'scripts', '.github', '.githooks')) {
    $sourceFiles += Get-ChildItem -LiteralPath (Join-Path $root $folder) -Recurse -File -Force | ForEach-Object { $_.FullName.Substring($root.Length + 1).Replace('\','/') }
}
foreach ($file in $sourceFiles) { if (-not (Test-Path -LiteralPath (Join-Path $root $file) -PathType Leaf)) { throw "Missing source: $file" } }
& (Join-Path $root 'scripts/Check-Secrets.ps1') -Files $sourceFiles
$build = Join-Path $root $BuildDirectory
$output = Join-Path $root 'artifacts'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$stage = Join-Path $output ('stage-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
$releaseFiles = @('ScreenTime.exe', 'ScreenTime.exe.config', 'install.ps1', 'README.md', 'LICENSE', 'PRIVACY.md', 'SECURITY.md')
foreach ($name in $releaseFiles) {
    $from = if ($name -like 'ScreenTime.exe*') { Join-Path $build $name } else { Join-Path $root $name }
    Copy-Item -LiteralPath $from -Destination (Join-Path $stage $name)
}
if ($CertificateThumbprint) {
    $certificate = Get-Item -LiteralPath ('Cert:\CurrentUser\My\' + $CertificateThumbprint)
    if (-not $certificate.HasPrivateKey -or $certificate.NotAfter -le (Get-Date) -or $certificate.NotBefore -gt (Get-Date)) { throw 'A current code-signing certificate with its private key is required.' }
    if (-not ($certificate.EnhancedKeyUsageList | Where-Object { $_.ObjectId -eq '1.3.6.1.5.5.7.3.3' })) { throw 'Certificate does not permit code signing.' }
    foreach ($name in @('ScreenTime.exe', 'install.ps1')) {
        $signature = Set-AuthenticodeSignature -LiteralPath (Join-Path $stage $name) -Certificate $certificate -HashAlgorithm SHA256 -TimestampServer $TimestampServer
        if ($signature.Status -ne 'Valid' -or -not $signature.TimeStamperCertificate) { throw "Signing or trusted timestamp failed for $name." }
    }
} else { Write-Output 'Unsigned build: no signing certificate supplied. No publisher identity claim is made.' }
function Write-Zip([string]$Base, [string[]]$Names, [string]$Path) {
    $file = [IO.File]::Open($Path, [IO.FileMode]::Create)
    $zip = New-Object IO.Compression.ZipArchive($file, [IO.Compression.ZipArchiveMode]::Create, $false)
    try {
        foreach ($name in ($Names | Sort-Object -Unique)) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $Base $name), $name.Replace('\','/'), [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $zip.Dispose(); $file.Dispose() }
}
$release = Join-Path $output "ScreenTime-$Version-windows.zip"
$source = Join-Path $output "ScreenTime-$Version-source.zip"
Write-Zip $stage $releaseFiles $release
Write-Zip $root $sourceFiles $source
$standalone = Join-Path $output 'ScreenTime.exe'
Copy-Item -LiteralPath (Join-Path $stage 'ScreenTime.exe') -Destination $standalone -Force
@($standalone, $release, $source) | ForEach-Object { $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256; "$($hash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_))" } | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
Write-Output "Standalone app: $standalone"
Write-Output "Release ZIP: $release"
Write-Output "Source ZIP: $source"
