[CmdletBinding()]
param(
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputDirectory = "",
    [switch]$SkipArchive
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$project = Join-Path $repoRoot "MuWinDX\MuWinDX.csproj"

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "artifacts\publish\$RuntimeIdentifier-performance"
}

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$archivePath = "$OutputDirectory.zip"

if (Test-Path $OutputDirectory) {
    Remove-Item $OutputDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

Write-Host "Restoring $project for $RuntimeIdentifier..."
& dotnet restore $project `
    -r $RuntimeIdentifier `
    -p:MonoGameFramework=MonoGame.Framework.WindowsDX `
    -p:MonoGamePlatform=Windows
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE"
}

Write-Host "Publishing maximum-performance Release build..."
& dotnet publish $project `
    -c Release `
    -r $RuntimeIdentifier `
    --self-contained true `
    --no-restore `
    -o $OutputDirectory `
    -p:MonoGameFramework=MonoGame.Framework.WindowsDX `
    -p:MonoGamePlatform=Windows `
    -p:PerformanceRelease=true `
    -p:ContinuousIntegrationBuild=true `
    -p:PublishReadyToRun=true `
    -p:PublishReadyToRunComposite=false `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -p:PublishAot=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$runtimeConfigPath = Join-Path $OutputDirectory "MuMono.runtimeconfig.json"
if (-not (Test-Path $runtimeConfigPath)) {
    throw "Published runtime configuration was not found: $runtimeConfigPath"
}

$runtimeConfig = Get-Content $runtimeConfigPath -Raw | ConvertFrom-Json
$configProperties = $runtimeConfig.runtimeOptions.configProperties

function Assert-RuntimeSetting {
    param(
        [Parameter(Mandatory = $true)] [string]$Name,
        [Parameter(Mandatory = $true)] $Expected
    )

    $property = $configProperties.PSObject.Properties[$Name]
    if ($null -eq $property -or $property.Value -ne $Expected) {
        $actual = if ($null -eq $property) { "<missing>" } else { [string]$property.Value }
        throw "Runtime setting '$Name' is '$actual'; expected '$Expected'."
    }
}

Assert-RuntimeSetting "System.Runtime.TieredCompilation" $true
Assert-RuntimeSetting "System.Runtime.TieredPGO" $true
Assert-RuntimeSetting "System.Runtime.TieredCompilation.QuickJit" $true
Assert-RuntimeSetting "System.Runtime.TieredCompilation.QuickJitForLoops" $false
Assert-RuntimeSetting "System.GC.Server" $false
Assert-RuntimeSetting "System.GC.Concurrent" $true

$performanceSettingsPath = Join-Path $OutputDirectory "appsettings.performance.json"
if (-not (Test-Path $performanceSettingsPath)) {
    throw "Performance settings file was not copied to the publish directory."
}

# Symbols and XML documentation are useful in a separate symbol artifact, not in the runtime folder.
Get-ChildItem $OutputDirectory -Recurse -File -Include *.pdb,*.xml | Remove-Item -Force

$manifestPath = Join-Path $OutputDirectory "SHA256SUMS.txt"
$files = Get-ChildItem $OutputDirectory -Recurse -File | Where-Object { $_.FullName -ne $manifestPath }
$manifestLines = foreach ($file in $files) {
    $relativePath = [System.IO.Path]::GetRelativePath($OutputDirectory, $file.FullName).Replace('\\', '/')
    $hash = (Get-FileHash $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $relativePath"
}
$manifestLines | Sort-Object | Set-Content $manifestPath -Encoding UTF8

if (-not $SkipArchive) {
    if (Test-Path $archivePath) {
        Remove-Item $archivePath -Force
    }
    Compress-Archive -Path (Join-Path $OutputDirectory '*') -DestinationPath $archivePath -CompressionLevel Optimal
    Write-Host "Archive: $archivePath"
}

Write-Host "Performance build: $OutputDirectory"
