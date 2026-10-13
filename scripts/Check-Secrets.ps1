param([string[]]$Files)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$checkIndex = -not $PSBoundParameters.ContainsKey('Files')
$tracked = @()
if (-not $Files) {
    $Files = @(git -C $root ls-files --cached --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) { throw 'Run the scan from a Git checkout, or supply -Files explicitly.' }
    $tracked = @(git -C $root ls-files --cached)
}
$patterns = @{
    'Private key' = '-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----'
    'GitHub token' = '(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,})'
    'OpenAI-style token' = 'sk-(?:proj-|svcacct-)?[A-Za-z0-9_-]{32,}'
    'AWS access key' = '(?:AKIA|ASIA)[A-Z0-9]{16}'
    'Slack token' = 'xox[baprs]-[A-Za-z0-9-]{20,}'
    'Embedded credential' = '(?i)(?:api[_-]?key|password|access[_-]?token|client[_-]?secret)\s*[:=]\s*["''][A-Za-z0-9_+/.=-]{20,}["'']'
    'Local Windows profile path' = '(?i)[A-Z]:[\\/]+Users[\\/]+[^\\/\s"'']+'
}
$findings = @()
foreach ($file in ($Files | Sort-Object -Unique)) {
    $absolute = if ([IO.Path]::IsPathRooted($file)) { $file } else { Join-Path $root $file }
    if ($file -match '(?i)(^|[\\/])(?:\.env(?:\..*)?|activity\.json.*|[^\\/]*\.(?:pfx|p12|pem|key|dmp)|[^\\/]*(?:credentials|secrets)\.json)$' -and $file -notmatch '\.env\.example$') {
        $findings += "Private file name: $file"
        continue
    }
    if ([IO.Path]::GetExtension($file) -match '^\.(exe|dll|zip|png|jpg|pdb)$') { $findings += "Unexpected binary in source: $file"; continue }
    $versions = @()
    if (Test-Path -LiteralPath $absolute -PathType Leaf) { $versions += [IO.File]::ReadAllText($absolute) }
    if ($checkIndex -and $tracked -contains $file) {
        $indexContent = @(git -C $root show ":$file")
        if ($LASTEXITCODE -ne 0) { throw "Could not scan staged content: $file" }
        $versions += ($indexContent -join "`n")
    }
    foreach ($content in $versions) {
        foreach ($rule in $patterns.Keys) {
            if ([regex]::IsMatch($content, $patterns[$rule])) { $findings += "${rule}: $file (value redacted)" }
        }
    }
}
if ($findings.Count) { $findings | Write-Output; throw 'Secret/private-file scan failed. Review findings before publishing.' }
Write-Output "Scanned $($Files.Count) source files; no matching credentials or local profile paths found."
