[CmdletBinding(DefaultParameterSetName = 'GitHub')]
param(
    [Parameter(Mandatory, ParameterSetName = 'File')]
    [string]$MessageFile,
    [Parameter(Mandatory, ParameterSetName = 'GitHub')]
    [string]$EventFile,
    [Parameter(Mandatory, ParameterSetName = 'GitHub')]
    [ValidateSet('push', 'pull_request')]
    [string]$EventName
)

$ErrorActionPreference = 'Stop'

function Assert-ConventionalCommit([string]$Message, [string]$Source) {
    $lines = $Message.TrimEnd() -split '\r?\n'
    if ($lines[0] -notmatch '^[a-z][a-z0-9-]*(?:\([\w./-]+\))?!?: \S.*$') {
        throw "$Source must use a Conventional Commit header, for example: fix(tracker): preserve foreground time"
    }
    if ($lines.Count -gt 1 -and -not [string]::IsNullOrWhiteSpace($lines[1])) {
        throw "$Source must have a blank line after the header."
    }
}

if ($PSCmdlet.ParameterSetName -eq 'File') {
    $text = Get-Content -LiteralPath $MessageFile -Encoding UTF8
    $commentChar = git config --get core.commentChar
    if (-not $commentChar -or $commentChar -eq 'auto') { $commentChar = '#' }
    $text = ($text | Where-Object { -not $_.StartsWith($commentChar) }) -join "`n"
    Assert-ConventionalCommit $text 'Commit message'
    return
}

$event = Get-Content -LiteralPath $EventFile -Raw -Encoding UTF8 | ConvertFrom-Json
if ($EventName -eq 'pull_request') {
    Assert-ConventionalCommit $event.pull_request.title 'Pull request title'
    $range = "$($event.pull_request.base.sha)..$($event.pull_request.head.sha)"
} else {
    if ($event.deleted -or $event.ref -notlike 'refs/heads/*') { return }
    $base = $event.before
    if ($base -match '^0+$') {
        $defaultRef = "refs/remotes/origin/$($event.repository.default_branch)"
        $base = git merge-base $defaultRef $event.after
        if ($LASTEXITCODE -ne 0) { throw 'Could not establish the new branch base.' }
    }
    $range = "$base..$($event.after)"
}

$commits = @(git log --no-merges --format=%H $range --)
if ($LASTEXITCODE -ne 0) { throw 'Could not read the commit range.' }
foreach ($commit in $commits) {
    $message = (git show -s --format=%B $commit --) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw "Could not read commit $commit." }
    Assert-ConventionalCommit $message "Commit $commit"
}
Write-Output "Checked $($commits.Count) commit messages."
