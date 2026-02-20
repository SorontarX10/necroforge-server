# Perf Budget Gate

Expected metrics file: `results/perf/latest_metrics.json`

Required numeric keys:
- `cpu_main_thread_ms`
- `cpu_render_thread_ms`
- `gc_alloc_kb_per_frame`
- `draw_calls`
- `setpass_calls`

Run locally:

```bash
python Tools/perf/perf_budget_gate.py \
  --metrics results/perf/latest_metrics.json \
  --budget Tools/perf/perf_budget.json
```

Generate `latest_metrics.json` from Unity (batchmode) before running the gate:

```bash
"C:/Program Files/Unity/Hub/Editor/6000.2.14f1/Editor/Unity.exe" \
  -batchmode -nographics -projectPath . \
  -executeMethod GrassSim.Editor.UnityProfilerMetricsExporter.ExportLatestMetricsForCI \
  -perfScene Assets/Scenes/Game.unity \
  -perfOutput results/perf/latest_metrics.json \
  -perfWarmupFrames 120 \
  -perfSampleFrames 360 \
  -perfTimeoutSeconds 180 \
  -logFile results/perf/unity-profiler-export.log
```

CI workflow `perf-budget-gate.yml` expects GitHub secret `UNITY_EDITOR_PATH`
pointing to Unity editor executable (for example: `C:\\Program Files\\Unity\\Hub\\Editor\\6000.2.14f1\\Editor\\Unity.exe`).

## Runtime hitch diagnostics (no manual profiler export)

Game runtime now auto-detects long frame hitches and writes JSONL events with probable cause:
- `results/perf/hitch_events.jsonl` (Editor mirror)
- `Application.persistentDataPath/hitch_events.jsonl` (always)

Each line contains frame time + snapshots from activation/simulation/query/pool/horde/relic systems and `probable_cause`.

Quick PowerShell checks:

```powershell
Get-Content -Path "results/perf/hitch_events.jsonl" -Tail 20
```

```powershell
Get-Content -Path "results/perf/hitch_events.jsonl" -Tail 1 | ConvertFrom-Json | Format-List
```

## Gameplay telemetry analyzer

Gameplay telemetry (`results/telemetry/gameplay_*.jsonl`) can be aggregated into:
- run-level CSV for balancing
- markdown summary with medians and end reasons

Run:

```bash
python Tools/perf/analyze_gameplay_telemetry.py \
  --input "results/telemetry/gameplay_*.jsonl" \
  --out-dir results/telemetry
```

Outputs:
- `results/telemetry/gameplay_telemetry_runs.csv`
- `results/telemetry/gameplay_telemetry_report.md`
