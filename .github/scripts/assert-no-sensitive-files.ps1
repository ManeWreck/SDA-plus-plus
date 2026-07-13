param(
    [string]$ScanRoot = ".",
    [switch]$TrackedFiles
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path -LiteralPath $ScanRoot).Path

if ($TrackedFiles) {
    $files = git ls-files | ForEach-Object {
        [PSCustomObject]@{
            FullName = Join-Path $root $_
            Relative = $_ -replace "\\", "/"
        }
    }
} else {
    $files = Get-ChildItem -LiteralPath $root -Recurse -File | ForEach-Object {
        [PSCustomObject]@{
            FullName = $_.FullName
            Relative = $_.FullName.Substring($root.Length).TrimStart([char[]]"\/") -replace "\\", "/"
        }
    }
}

$blockedPatterns = @(
    '(?i)\.mafile$',
    '(?i)(^|/)manifest\.json$',
    '(?i)(^|/)credentials(?:\.secure)?\.json$',
    '(?i)(^|/)(?:cloud|webdav)\.secret\.json$',
    '(?i)(^|/)[^/]*(?:cookie|token)[^/]*\.json$',
    '(?i)(^|/)backups/'
)

$violations = foreach ($file in $files) {
    foreach ($pattern in $blockedPatterns) {
        if ($file.Relative -match $pattern) {
            $file.Relative
            break
        }
    }
}

if ($violations) {
    Write-Error ("Sensitive account data must not be committed or packaged:`n" + (($violations | Sort-Object -Unique) -join "`n"))
}

Write-Host "Sensitive-file check passed for $($files.Count) files."
