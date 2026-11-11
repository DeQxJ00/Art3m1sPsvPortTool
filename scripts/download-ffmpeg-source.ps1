param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [Parameter(Mandatory)]
    [ValidatePattern('^[a-fA-F0-9]{64}$')]
    [string]$Sha256,

    [Parameter(Mandatory)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$archive = "ffmpeg-$Version.tar.xz"
$temporaryPath = "$OutputPath.$([guid]::NewGuid().ToString('N')).download"
$sources = @(
    "https://ffmpeg.org/releases/$archive"
    "https://ftp.fau.de/macports/distfiles/ffmpeg/$archive"
    "https://mirror.math.princeton.edu/pub/macports/distfiles/ffmpeg/$archive"
)

foreach ($source in $sources) {
    try {
        Write-Host "Downloading $source"
        curl --fail --location --silent --show-error --connect-timeout 10 --max-time 60 --output $temporaryPath $source
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Download failed with curl exit code $LASTEXITCODE from $source"
            continue
        }

        $actual = (Get-FileHash -LiteralPath $temporaryPath -Algorithm SHA256).Hash
        if ($actual -ne $Sha256) {
            Write-Warning "FFmpeg source SHA-256 mismatch from ${source}: $actual"
            continue
        }

        Move-Item -LiteralPath $temporaryPath -Destination $OutputPath -Force
        Write-Host "Verified $archive SHA-256: $actual"
        return
    } finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

throw "Unable to download $archive with the expected SHA-256 from any source."
