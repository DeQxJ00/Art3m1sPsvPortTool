$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$propsPath = Join-Path $repository 'Directory.Build.props'
$changelogPath = Join-Path $repository 'CHANGELOG.md'

[xml]$props = Get-Content -LiteralPath $propsPath -Raw
$version = [string]$props.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Invalid application version in Directory.Build.props: $version"
}
foreach ($field in @('AssemblyVersion', 'FileVersion', 'InformationalVersion')) {
    $expected = if ($field -eq 'InformationalVersion') { $version } else { "$version.0" }
    if ([string]$props.Project.PropertyGroup.$field -ne $expected) {
        throw "Directory.Build.props $field must be $expected."
    }
}

$macosPlist = Get-Content -LiteralPath (Join-Path $repository 'packaging/macos/Info.plist') -Raw
foreach ($key in @('CFBundleShortVersionString', 'CFBundleVersion')) {
    $pattern = "<key>$key</key>\s*<string>$([regex]::Escape($version))</string>"
    if ($macosPlist -notmatch $pattern) { throw "macOS $key must be $version." }
}

$changelog = Get-Content -LiteralPath $changelogPath -Raw
$heading = "(?m)^## \[$([regex]::Escape($version))\] - \d{4}-\d{2}-\d{2}\s*$"
$versionHeadings = [regex]::Matches($changelog, $heading)
if ($versionHeadings.Count -ne 1) {
    throw "CHANGELOG.md must have exactly one dated entry for version $version."
}

$bodyStart = $versionHeadings[0].Index + $versionHeadings[0].Length
$nextHeading = [regex]::Match($changelog.Substring($bodyStart), '(?m)^## \[')
$body = if ($nextHeading.Success) { $changelog.Substring($bodyStart, $nextHeading.Index) }
        else { $changelog.Substring($bodyStart) }
if ($body -notmatch '(?m)^- \S') {
    throw "CHANGELOG.md version $version has no change items."
}

foreach ($readme in @('README.md', 'README.en.md')) {
    $content = Get-Content -LiteralPath (Join-Path $repository $readme) -Raw
    if (-not $content.Contains('(CHANGELOG.md)')) {
        throw "$readme does not link to CHANGELOG.md."
    }
}

if ($env:GITHUB_REF_TYPE -eq 'tag') {
    if ($env:GITHUB_REF_NAME -ne "v$version") {
        throw "Git tag $env:GITHUB_REF_NAME does not match application version v$version."
    }
}

Write-Output "Changelog verified for v$version."
