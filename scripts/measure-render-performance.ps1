[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $GameExe,
    [Parameter(Mandatory)][string] $DataDirectory,
    [string] $PresentMonExe = 'PresentMon',
    [string] $OutputDirectory = (Join-Path $PSScriptRoot "../artifacts/render-performance-$(Get-Date -Format 'yyyyMMdd-HHmmss')"),
    [ValidateRange(1, 100)][int] $Pairs = 5,
    [ValidateRange(1, 120)][int] $WarmupSeconds = 10,
    [ValidateRange(3, 600)][int] $MeasureSeconds = 30,
    [ValidateRange(0, 255)][int] $Map = 0,
    [ValidatePattern('^\d{1,3},\d{1,3}$')][string] $Position = '135,128',
    [switch] $Profile,
    [switch] $Wpr,
    [switch] $GameOnly,
    [switch] $CompareStaticTransparentUploadCache
)

$ErrorActionPreference = 'Stop'
if ($Wpr -and !$Profile) { throw 'Use -Profile with -Wpr so that trace overhead stays out of scoring runs.' }
if (@($Position.Split(',') | Where-Object { [int]$_ -notin (4..251) }).Count) {
    throw 'The benchmark position must leave space for the monster grid. Use tiles from 4 to 251.'
}
$gamePath = (Resolve-Path -LiteralPath $GameExe).Path
$dataPath = (Resolve-Path -LiteralPath $DataDirectory).Path
if (!$GameOnly) {
    $presentMonPath = if (Test-Path -LiteralPath $PresentMonExe -PathType Leaf) {
        (Resolve-Path -LiteralPath $PresentMonExe).Path
    } else {
        (Get-Command -Name $PresentMonExe -CommandType Application -ErrorAction Stop).Source
    }
}
$out = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
if (Test-Path -LiteralPath $out) { throw "Choose a new output directory. '$out' already exists." }
New-Item -Path $out -ItemType Directory | Out-Null

function Start-CollectorProcess($executable, $workingDirectory, $arguments, $variables) {
    $info = [System.Diagnostics.ProcessStartInfo]::new($executable)
    $info.WorkingDirectory = $workingDirectory
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($arg in $arguments) { $info.ArgumentList.Add($arg) }
    if ($null -ne $variables) {
        foreach ($key in @($info.Environment.Keys)) {
            if ($key -like 'MU_STRESS*' -or $key -eq 'MU_GPU_SYNC') { [void]$info.Environment.Remove($key) }
        }
        foreach ($entry in $variables.GetEnumerator()) { $info.Environment[$entry.Key] = [string]$entry.Value }
    }
    $process = [System.Diagnostics.Process]::Start($info)
    [pscustomobject]@{ Process = $process; Output = $process.StandardOutput.ReadToEndAsync(); ErrorText = $process.StandardError.ReadToEndAsync() }
}

function Save-CollectorOutput($capture, $stem) {
    if ($null -eq $capture) { return }
    if (!$capture.Process.HasExited) {
        $capture.Process.Kill()
        $capture.Process.WaitForExit()
    }
    [IO.File]::WriteAllText("$stem.log", $capture.Output.GetAwaiter().GetResult())
    [IO.File]::WriteAllText("$stem.err.log", $capture.ErrorText.GetAwaiter().GetResult())
    $capture.Process.Dispose()
}

function Get-MeanMetric($rows, $name) {
    $sum = 0.0
    $count = 0
    foreach ($row in $rows) {
        $number = 0.0
        if ([double]::TryParse($row.$name, [Globalization.NumberStyles]::Float,
                [Globalization.CultureInfo]::InvariantCulture, [ref]$number) -and [double]::IsFinite($number)) {
            $sum += $number
            $count++
        }
    }
    if ($count) { [Math]::Round($sum / $count, 4) }
}

function Get-Percentile($values, [double] $fraction) {
    $sorted = @($values | Sort-Object)
    if ($sorted.Count) { $sorted[[int][Math]::Ceiling($sorted.Count * $fraction) - 1] }
}

function Get-Median($values) {
    $sorted = @($values | Sort-Object)
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2) { return $sorted[$middle] }
    return ($sorted[$middle - 1] + $sorted[$middle]) / 2
}

function Export-BenchmarkCsv($rows, $path) {
    $rows | ForEach-Object {
        $entry = [ordered]@{}
        foreach ($field in $_.PSObject.Properties) {
            $value = $field.Value
            $entry[$field.Name] = if ($value -is [IFormattable]) {
                $value.ToString($null, [Globalization.CultureInfo]::InvariantCulture)
            } else { $value }
        }
        [pscustomobject]$entry
    } | Export-Csv -LiteralPath $path -NoTypeInformation
}

function Join-PresentCapture($gameRows, $csv, [int] $gameProcessId) {
    if (!(Test-Path -LiteralPath $csv)) { throw "PresentMon did not write '$csv'." }
    $presentRows = @(Import-Csv -LiteralPath $csv | Where-Object { $_.ProcessID -eq $gameProcessId })
    if (!$presentRows.Count -or $null -eq $presentRows[0].CPUStartQPC -or $null -eq $presentRows[0].MsBetweenPresents) {
        throw 'No PresentMon 2.x QPC samples for the benchmark process.'
    }

    $group = $presentRows |
        Where-Object { [long]$_.CPUStartQPC -ge [long]$gameRows[0].startQpc -and [long]$_.CPUStartQPC -le [long]$gameRows[-1].endQpc } |
        Group-Object SwapChainAddress | Sort-Object Count -Descending | Select-Object -First 1
    if (!$group) { throw 'No swap chain activity overlaps the game measurement.' }

    foreach ($scene in $gameRows) {
        # CPUStartQPC precedes Present. Allow the pipeline to cross the phase markers before counting intervals.
        $margin = [long]([double]$scene.qpcFrequency * 0.25)
        $start = [long]$scene.startQpc + $margin
        $end = [long]$scene.endQpc - $margin
        $samples = @($group.Group | Where-Object {
            $qpc = [long]$_.CPUStartQPC
            $qpc -ge $start -and $qpc -le $end -and [double]$_.MsBetweenPresents -gt 0
        } | Select-Object -Skip 1)
        $seconds = ($end - $start) / [double]$scene.qpcFrequency
        if ($samples.Count -lt 2 -or $seconds -lt 1 -or
            $samples.Count / $seconds -lt [double]$scene.gameFps * 0.85) {
            throw "Incomplete presentation capture for '$($scene.scenario)' on swap chain $($group.Name)."
        }
        if (@($samples | Where-Object { $_.SyncInterval -ne '0' }).Count) {
            throw "VSync was active in '$($scene.scenario)' according to PresentMon."
        }
        $intervals = @($samples | ForEach-Object { [double]$_.MsBetweenPresents })
        $duration = ($intervals | Measure-Object -Sum).Sum / 1000.0
        $gpuBusy = Get-MeanMetric $samples 'MsGPUBusy'
        [pscustomobject]@{
            Scenario = $scene.scenario
            PresentFrames = $samples.Count
            PresentSeconds = [Math]::Round($duration, 6)
            PresentedFps = [Math]::Round($samples.Count / $duration, 3)
            P95FrameMs = Get-Percentile $intervals 0.95
            GpuBusyMs = if ($gpuBusy -gt 0) { $gpuBusy }
            CpuBusyMs = Get-MeanMetric $samples 'MsCPUBusy'
            PresentApiMs = Get-MeanMetric $samples 'MsInPresentAPI'
            SwapChain = $group.Name
        }
    }
}

function Measure-Variant($directory, $variant) {
    $gpuModels = $variant -ne 'control'
    $staticTransparentUploadCache = $variant -eq 'static-transparent-upload-cache'
    New-Item -Path $directory -ItemType Directory | Out-Null
    $gameCsv = Join-Path $directory 'game.csv'
    $presentCsv = Join-Path $directory 'presentmon.csv'
    $gameVariables = @{
        MU_DATA_PATH = $dataPath
        MU_STRESS = '1'
        MU_STRESS_SUITE = '1'
        MU_STRESS_SUITE_PROFILE = [int][bool]$Profile
        MU_STRESS_LOW_GPU_MODELS = [int]$gpuModels
        MU_STRESS_RENDER_SCALE = '1'
        MU_STRESS_SOUND = '0'
        MU_STRESS_MAP = $Map
        MU_STRESS_POS = $Position
        MU_STRESS_WARMUP = $WarmupSeconds
        MU_STRESS_SECONDS = $MeasureSeconds
        MU_STRESS_OUT = $gameCsv
        MU_STRESS_LABEL = 'low'
    }
    if ($Profile) {
        $gameVariables.MU_STRESS_SHOTS = Join-Path $directory 'shots'
    }
    if ($CompareStaticTransparentUploadCache) {
        $gameVariables.MU_STRESS_STATIC_TRANSPARENT_UPLOAD_CACHE = [int]$staticTransparentUploadCache
    }
    $client = $present = $null
    $wprStarted = $false
    $processorSamples = [Collections.Generic.List[object]]::new()
    $gpuSamples = [Collections.Generic.List[object]]::new()
    try {
        if ($Wpr) {
            & wpr.exe -start CPU -start GPU -filemode | Out-Host
            if ($LASTEXITCODE -ne 0) { throw 'WPR could not start CPU and GPU profiling. Check privileges and active recordings.' }
            $wprStarted = $true
        }
        $client = Start-CollectorProcess $gamePath (Split-Path -Parent $gamePath) @() $gameVariables
        $gameProcessId = $client.Process.Id
        if (!$GameOnly) {
            $present = Start-CollectorProcess $presentMonPath $directory @(
                '--process_id', $gameProcessId, '--qpc_time', '--v2_metrics', '--no_console_stats', '--no_track_input',
                '--session_name', "MuRender-$gameProcessId", '--terminate_on_proc_exit', '--output_file', $presentCsv
            ) $null
        }

        $deadline = [DateTime]::UtcNow.AddSeconds(120 + (3 * ($WarmupSeconds + $MeasureSeconds)))
        while (!$client.Process.WaitForExit(1000)) {
            if ([DateTime]::UtcNow -gt $deadline) { throw 'The game benchmark timed out.' }
            if ($present -and $present.Process.HasExited -and !$client.Process.HasExited) {
                throw "PresentMon exited before the game. See '$directory/presentmon.err.log'."
            }
            if ($Profile) {
                $qpc = [Diagnostics.Stopwatch]::GetTimestamp()
                foreach ($core in Get-CimInstance -ClassName Win32_PerfFormattedData_Counters_ProcessorInformation -Property Name, PercentProcessorTime, ProcessorFrequency) {
                    if ($core.Name -notlike '*_Total') {
                        $processorSamples.Add([pscustomobject]@{
                            Qpc = $qpc; LogicalProcessor = $core.Name
                            PercentProcessorTime = $core.PercentProcessorTime; FrequencyMHz = $core.ProcessorFrequency
                        })
                    }
                }
                if ($GameOnly) {
                    $engineFilter = "Name LIKE 'pid_$($gameProcessId)_%'"
                    $engines = Get-CimInstance -ClassName Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine -Filter $engineFilter -ErrorAction SilentlyContinue
                    foreach ($engine in $engines) {
                        if ($engine.Name -notlike "pid_${gameProcessId}_*") { continue }
                        $gpuSamples.Add([pscustomobject]@{
                            Qpc = $qpc; Engine = $engine.Name; UtilizationPercentage = $engine.UtilizationPercentage
                        })
                    }
                }
            }
        }
        if ($client.Process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $gameCsv)) {
            throw "The benchmark produced no valid workload in '$directory'. See game.err.log and game.log."
        }
        if ($present -and (!$present.Process.WaitForExit(15000) -or $present.Process.ExitCode -ne 0)) {
            throw "PresentMon did not finish successfully. See '$directory/presentmon.err.log'."
        }
    } finally {
        Save-CollectorOutput $client (Join-Path $directory 'game')
        Save-CollectorOutput $present (Join-Path $directory 'presentmon')
        if ($processorSamples.Count) {
            $processorSamples | Export-Csv -LiteralPath (Join-Path $directory 'processors.csv') -NoTypeInformation
        }
        if ($gpuSamples.Count) {
            $gpuSamples | Export-Csv -LiteralPath (Join-Path $directory 'gpu-engines.csv') -NoTypeInformation
        } elseif ($Profile -and $GameOnly -and $client) {
            Write-Warning "Windows returned no GPU engine samples for '$directory'."
        }
        if ($wprStarted) {
            & wpr.exe -stop (Join-Path $directory 'cpu-gpu.etl') | Out-Host
            if ($LASTEXITCODE -ne 0) { throw 'WPR could not save the CPU and GPU trace.' }
        }
    }

    $settings = Get-Content -LiteralPath (Join-Path $directory 'game.json') -Raw | ConvertFrom-Json
    if ($settings.IsVSyncEnabled -or $settings.MsaaSamples -ne 0 -or $settings.Fxaa -or $settings.Bloom -or
        $settings.Ssao -or $settings.DynamicLights -or $settings.RenderScale -ne 1 -or !$settings.GpuSkinning -or
        $settings.DynamicLightingShader -ne $gpuModels -or $settings.Profile -ne [bool]$Profile) {
        throw "Unexpected effective graphics settings in '$directory/game.json'."
    }
    if ($CompareStaticTransparentUploadCache -and
        [bool]$settings.StaticTransparentUploadCache -ne $staticTransparentUploadCache) {
        throw "Unexpected transparent upload cache setting in '$directory/game.json'."
    }
    $gameRows = @(Import-Csv -LiteralPath $gameCsv -Delimiter ';')
    if (($gameRows.scenario -join ',') -ne 'idle,walk,combat') { throw "Incomplete scenarios in '$gameCsv'." }
    $presentStats = if (!$GameOnly) { @(Join-PresentCapture $gameRows $presentCsv $gameProcessId) }
    if (!$GameOnly -and @($presentStats | Where-Object { $null -eq $_.GpuBusyMs }).Count) {
        Write-Warning "PresentMon GPU timing was unavailable for at least one scenario in '$directory'."
    }

    for ($i = 0; $i -lt $gameRows.Count; $i++) {
        $scene = $gameRows[$i]
        $entry = [ordered]@{
            Scenario = $scene.scenario
            FpsSource = if ($GameOnly) { 'ClientPresent' } else { 'PresentMon' }
            Frames = if ($GameOnly) { [int]$scene.frames } else { $presentStats[$i].PresentFrames }
            Seconds = if ($GameOnly) { ([long]$scene.endQpc - [long]$scene.startQpc) / [double]$scene.qpcFrequency } else { $presentStats[$i].PresentSeconds }
            Fps = if ($GameOnly) { [double]$scene.gameFps } else { $presentStats[$i].PresentedFps }
            P95FrameMs = if (!$GameOnly) { $presentStats[$i].P95FrameMs }
            GpuBusyMs = if (!$GameOnly) { $presentStats[$i].GpuBusyMs }
            CpuBusyMs = if (!$GameOnly) { $presentStats[$i].CpuBusyMs }
            PresentApiMs = if ($GameOnly) { [double]$scene.presentApiMs } else { $presentStats[$i].PresentApiMs }
        }
        foreach ($field in @('cpuMs','updateMs','drawMs','worldObjectsMs','worldQueueMs','staticQueueMs','terrainMs','interfaceMs',
                'postProcessMs','mainCpuMsPerSecond','visibleObjects','drawCalls','triangles','textureChanges','targetChanges',
                'staticDrawCalls','gpuSkinnedMeshes','modelFallbackDrawCalls','cpuSkinRequests','cameraTravel','attackWaves',
                'staticTransparentUploadMs','staticTransparentUploads','staticTransparentUploadReuses')) {
            $entry[$field] = $scene.$field
        }
        [pscustomobject]$entry
    }
}

$clientAssembly = Join-Path (Split-Path -Parent $gamePath) 'Client.Main.dll'
[ordered]@{
    GameExe = $gamePath
    GameSha256 = (Get-FileHash -LiteralPath $gamePath -Algorithm SHA256).Hash
    ClientAssemblySha256 = if (Test-Path -LiteralPath $clientAssembly) { (Get-FileHash -LiteralPath $clientAssembly -Algorithm SHA256).Hash }
    PresentMonExe = if (!$GameOnly) { $presentMonPath }
    PresentMonSha256 = if (!$GameOnly) { (Get-FileHash -LiteralPath $presentMonPath -Algorithm SHA256).Hash }
    CaptureMode = if ($Profile) { 'Profile' } else { 'Score' }
    CompareStaticTransparentUploadCache = [bool]$CompareStaticTransparentUploadCache
    Pairs = $Pairs
    Map = $Map
    Position = $Position
    WarmupSeconds = $WarmupSeconds
    MeasureSeconds = $MeasureSeconds
    Cpu = @(Get-CimInstance Win32_Processor | Select-Object Name, NumberOfLogicalProcessors)
    Adapters = @(Get-CimInstance Win32_VideoController | Select-Object Name, DriverVersion)
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $out 'run.json') -Encoding utf8

$results = [Collections.Generic.List[object]]::new()
$workload = $null
$controlVariant = if ($CompareStaticTransparentUploadCache) { 'gpu-models' } else { 'control' }
$testVariant = if ($CompareStaticTransparentUploadCache) { 'static-transparent-upload-cache' } else { 'gpu-models' }
$fpsColumns = @{
    'control' = 'ControlFps'
    'gpu-models' = 'GpuModelsFps'
    'static-transparent-upload-cache' = 'StaticTransparentUploadCacheFps'
}
for ($pair = 1; $pair -le $Pairs; $pair++) {
    $order = if ($pair % 2) { @($controlVariant,$testVariant) } else { @($testVariant,$controlVariant) }
    foreach ($variant in $order) {
        $directory = Join-Path $out ('{0:D2}-{1}' -f $pair, $variant)
        Write-Host "Pair $pair/$Pairs $variant"
        $scenes = @(Measure-Variant $directory $variant)
        $settings = Get-Content -LiteralPath (Join-Path $directory 'game.json') -Raw | ConvertFrom-Json
        $effective = $settings | Select-Object Adapter, GraphicsProfile, BackBufferWidth, BackBufferHeight, RenderWidth, RenderHeight, WalkDestination |
            ConvertTo-Json -Compress
        if ($workload -and $workload -ne $effective) { throw "Adapter, resolution or route changed during the benchmark in '$directory'." }
        $workload = $effective
        foreach ($row in $scenes) {
            $row | Add-Member -NotePropertyName Pair -NotePropertyValue $pair
            $row | Add-Member -NotePropertyName Variant -NotePropertyValue $variant
            $results.Add($row)
        }
        Export-BenchmarkCsv $results (Join-Path $out 'results.csv')
        $scenes | Select-Object Scenario, Fps, GpuBusyMs, worldQueueMs, staticQueueMs | Format-Table -AutoSize | Out-Host
    }
}

$comparison = foreach ($name in @('idle','walk','combat','overall')) {
    $median = @{}
    foreach ($variant in @($controlVariant,$testVariant)) {
        $rows = @($results | Where-Object Variant -eq $variant)
        $values = if ($name -eq 'overall') {
            foreach ($group in ($rows | Group-Object Pair)) {
                ($group.Group | Measure-Object Frames -Sum).Sum / ($group.Group | Measure-Object Seconds -Sum).Sum
            }
        } else {
            $rows | Where-Object Scenario -eq $name | ForEach-Object Fps
        }
        $median[$variant] = Get-Median @($values)
    }
    $row = [ordered]@{
        Scenario = $name
    }
    $row[$fpsColumns[$controlVariant]] = [Math]::Round($median[$controlVariant], 2)
    $row[$fpsColumns[$testVariant]] = [Math]::Round($median[$testVariant], 2)
    $row.ChangePercent = [Math]::Round(100 * ($median[$testVariant] / $median[$controlVariant] - 1), 2)
    [pscustomobject]$row
}
Export-BenchmarkCsv $comparison (Join-Path $out 'comparison.csv')
$comparison | Format-Table -AutoSize
if ($controlVariant -eq 'control' -and !$Profile -and $Pairs -ge 5 -and $MeasureSeconds -ge 30) {
    $targetMet = ($comparison[-1].ChangePercent -ge 20 -and @($comparison | Where-Object ChangePercent -lt -3).Count -eq 0)
    Write-Host "FPS target met: $targetMet"
}
