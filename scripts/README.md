# PowerShell scripts

## Publishing

- `publish/android-performance.ps1` - Android ARM64 performance packages.
- `publish/windows-ci.ps1` - Windows backend builds used by CI.
- `publish/windows-performance.ps1` - validated DirectX performance release.

Run scripts from the repository root, for example:

```powershell
./scripts/publish/windows-performance.ps1
```

## Render performance benchmark

Run the offline DX11 suite with PowerShell 7 and a Release build of MuWinDX. Pass an extracted PresentMon 2.x console executable. The suite alternates five A/B pairs and measures idle, a repeatable walk, and combat with 12 spiders. Both variants use Low at full render resolution with VSync, MSAA, FXAA, bloom, SSAO and dynamic lights disabled.

```powershell
./scripts/measure-render-performance.ps1 -GameExe 'C:\MU\MuMono.exe' -DataDirectory 'C:\MU\Data' -PresentMonExe 'C:\Tools\PresentMon.exe'
```

Use `-GameOnly` to count completed client presentations when ETW capture is unavailable. The output identifies the FPS source. The CSV records CPU and GPU time from PresentMon when available. Results are saved in `artifacts/` by default. `comparison.csv` reports the median of the runs, with overall FPS calculated from all three scenes' frame counts and durations.

Use a build with `-p:PerformanceRelease=false` and add `-Profile -Pairs 1` for pass timings, draw calls, instancing, CPU skinning requests and per-logical-processor samples. Add `-Wpr` in an elevated session to also save a CPU/GPU ETL trace for WPA. `game.csv` timestamps use QPC and `game.json` identifies the game thread. A GameOnly profile also samples the client's GPU engines through Windows performance counters when available.

## GPU model rendering in Low

On hardware DX11 with HiDef, enable this option in `appsettings.local.json` to try the faster model path. It can change how some models are lit.

```json
{
  "MuOnlineSettings": {
    "Graphics": {
      "QualityPreset": "Low",
      "EnableLowGpuModels": true
    }
  }
}
```
