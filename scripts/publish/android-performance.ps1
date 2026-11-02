[CmdletBinding()]
param(
    [ValidateSet("FullAot", "ProfiledAot")]
    [string]$Mode = "FullAot",

    [ValidateSet("apk", "aab", "aab;apk")]
    [string]$PackageFormats = "apk",

    [string]$RuntimeIdentifier = "android-arm64",
    [string]$OutputDirectory = "artifacts/publish/android-arm64-performance",

    [switch]$SkipWorkloadRestore,
    [switch]$SkipToolRestore,
    [switch]$NoArchive
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$project = Join-Path $repoRoot "MuAndroid/MuAndroid.csproj"
$output = Join-Path $repoRoot $OutputDirectory
$archive = Join-Path $repoRoot "muonline-android.zip"
$fullAot = $Mode -eq "FullAot"
$useLlvm = $fullAot
$androidSdk = $env:ANDROID_SDK_ROOT
if ([string]::IsNullOrWhiteSpace($androidSdk)) {
    $localAndroidSdk = Join-Path $env:LOCALAPPDATA "Android/Sdk"
    if (Test-Path $localAndroidSdk) {
        $androidSdk = $localAndroidSdk
    }
}

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]]$Arguments)

    Write-Host "dotnet $($Arguments -join ' ')" -ForegroundColor Cyan
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet command failed with exit code $LASTEXITCODE"
    }
}

function Get-Sha256Hex {
    param([Parameter(Mandatory)][string]$Path)

    $stream = [System.IO.File]::OpenRead($Path)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([System.BitConverter]::ToString($sha256.ComputeHash($stream))).Replace("-", "").ToLowerInvariant()
    }
    finally {
        $sha256.Dispose()
        $stream.Dispose()
    }
}

Push-Location $repoRoot
try {
    if (-not $SkipWorkloadRestore) {
        Invoke-DotNet -Arguments @(
            "workload", "restore", $project,
            "--skip-manifest-update"
        )
    }

    if (-not $SkipToolRestore) {
        Invoke-DotNet -Arguments @(
            "tool", "restore",
            "--tool-manifest", (Join-Path $repoRoot "MuAndroid/.config/dotnet-tools.json")
        )
    }

    $dependencyProperties = @(
        "-p:AcceptAndroidSdkLicenses=true"
    )
    if ($androidSdk) {
        $dependencyProperties += "-p:AndroidSdkDirectory=$androidSdk"
    }
    if ($env:JAVA_HOME) {
        $dependencyProperties += "-p:JavaSdkDirectory=$env:JAVA_HOME"
    }

    Invoke-DotNet -Arguments (@(
        "build", $project,
        "-t:InstallAndroidDependencies",
        "-f", "net10.0-android"
    ) + $dependencyProperties)

    if (Test-Path $output) {
        Remove-Item $output -Recurse -Force
    }
    New-Item $output -ItemType Directory -Force | Out-Null

    $commonProperties = @(
        "-p:AndroidPerformanceRelease=true",
        "-p:AndroidFullAot=$($fullAot.ToString().ToLowerInvariant())",
        "-p:AndroidUseLlvm=$($useLlvm.ToString().ToLowerInvariant())",
        "-p:AndroidPackageFormats=$PackageFormats",
        "-p:RestoreMonoGameTools=false",
        "-p:AcceptAndroidSdkLicenses=true",
        "-p:ContinuousIntegrationBuild=$($env:CI -eq 'true')"
    )

    if ($androidSdk) {
        $commonProperties += "-p:AndroidSdkDirectory=$androidSdk"
    }
    if ($env:JAVA_HOME) {
        $commonProperties += "-p:JavaSdkDirectory=$env:JAVA_HOME"
    }

    Invoke-DotNet -Arguments (@(
        "restore", $project,
        "-r", $RuntimeIdentifier,
        "--disable-parallel"
    ) + $commonProperties)

    Invoke-DotNet -Arguments (@(
        "publish", $project,
        "-c", "Release",
        "-f", "net10.0-android",
        "-r", $RuntimeIdentifier,
        "-o", $output,
        "--no-restore",
        "--disable-build-servers",
        # ProjectReference property variants can build shared libraries into the same obj directory.
        # Keep those compilations sequential to avoid CS2012 file locks.
        "-m:1"
    ) + $commonProperties)

    $expectedExtensions = @()
    if ($PackageFormats -match 'apk') { $expectedExtensions += '.apk' }
    if ($PackageFormats -match 'aab') { $expectedExtensions += '.aab' }

    $packages = Get-ChildItem $output -File -Recurse | Where-Object {
        $expectedExtensions -contains $_.Extension.ToLowerInvariant()
    }
    if (-not $packages) {
        throw "No Android package was produced in $output"
    }

    foreach ($package in $packages) {
        Write-Host "Produced $($package.Name): $([math]::Round($package.Length / 1MB, 2)) MB" -ForegroundColor Green
    }

    $mapping = Get-ChildItem (Join-Path $repoRoot "MuAndroid/bin/Release") -Filter mapping.txt -File -Recurse -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($mapping) {
        Copy-Item $mapping.FullName (Join-Path $output "mapping.txt") -Force
    }

    $checksums = foreach ($file in Get-ChildItem $output -File | Sort-Object Name) {
        "$(Get-Sha256Hex $file.FullName)  $($file.Name)"
    }
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllLines(
        (Join-Path $output "SHA256SUMS.txt"),
        [string[]]$checksums,
        $utf8NoBom)

    if (-not $NoArchive) {
        if (Test-Path $archive) {
            Remove-Item $archive -Force
        }
        Compress-Archive -Path (Join-Path $output "*") -DestinationPath $archive -CompressionLevel Optimal
        Write-Host "Archive: $archive" -ForegroundColor Green
    }
}
finally {
    Pop-Location
}
