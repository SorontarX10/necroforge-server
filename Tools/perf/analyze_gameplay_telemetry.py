#!/usr/bin/env python3
import argparse
import csv
import glob
import json
import statistics
from pathlib import Path
from typing import Any


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Analyze gameplay telemetry JSONL files and produce balance-friendly summaries."
    )
    parser.add_argument(
        "--input",
        default="results/telemetry/gameplay_*.jsonl",
        help="Glob pattern for telemetry JSONL files.",
    )
    parser.add_argument(
        "--out-dir",
        default="results/telemetry",
        help="Directory for report outputs.",
    )
    parser.add_argument(
        "--csv-name",
        default="gameplay_telemetry_runs.csv",
        help="Output CSV file name.",
    )
    parser.add_argument(
        "--md-name",
        default="gameplay_telemetry_report.md",
        help="Output Markdown file name.",
    )
    return parser.parse_args()


def as_float(value: Any, default: float = 0.0) -> float:
    if value is None:
        return default
    try:
        return float(value)
    except (TypeError, ValueError):
        return default


def as_int(value: Any, default: int = 0) -> int:
    if value is None:
        return default
    try:
        return int(value)
    except (TypeError, ValueError):
        return default


def load_events(pattern: str) -> list[dict[str, Any]]:
    events: list[dict[str, Any]] = []
    for path in sorted(glob.glob(pattern)):
        with open(path, "r", encoding="utf-8") as handle:
            for line_number, line in enumerate(handle, start=1):
                line = line.strip()
                if not line:
                    continue
                try:
                    payload = json.loads(line)
                except json.JSONDecodeError:
                    continue
                payload["_source_file"] = path
                payload["_line_number"] = line_number
                events.append(payload)
    return events


def group_events_by_run(events: list[dict[str, Any]]) -> dict[str, list[dict[str, Any]]]:
    grouped: dict[str, list[dict[str, Any]]] = {}
    for event in events:
        run_id = event.get("run_id")
        if not run_id:
            continue
        grouped.setdefault(run_id, []).append(event)

    for run_events in grouped.values():
        run_events.sort(
            key=lambda e: (
                as_float(e.get("run_time_s"), 0.0),
                as_int(e.get("frame"), 0),
                as_int(e.get("_line_number"), 0),
            )
        )
    return grouped


def find_last_event(run_events: list[dict[str, Any]], event_type: str) -> dict[str, Any] | None:
    for event in reversed(run_events):
        if event.get("event_type") == event_type:
            return event
    return None


def find_first_event(run_events: list[dict[str, Any]], event_type: str) -> dict[str, Any] | None:
    for event in run_events:
        if event.get("event_type") == event_type:
            return event
    return None


def summarize_run(run_id: str, run_events: list[dict[str, Any]]) -> dict[str, Any]:
    run_started = find_first_event(run_events, "run_started")
    run_ended = find_last_event(run_events, "run_ended")
    latest_snapshot = find_last_event(run_events, "periodic_snapshot")

    terminal_event = run_ended or latest_snapshot or (run_events[-1] if run_events else {})
    counters = terminal_event.get("counters") or {}
    player = terminal_event.get("player") or {}
    world = terminal_event.get("world") or {}
    enemy_summary = terminal_event.get("enemy_hit_summary") or []

    lifesteal_events = [e for e in run_events if e.get("event_type") == "lifesteal_applied"]
    incoming_events = [e for e in run_events if e.get("event_type") == "incoming_damage"]

    total_raw_incoming = sum(
        as_float((e.get("incoming_damage") or {}).get("raw_damage"), 0.0)
        for e in incoming_events
    )
    total_final_incoming = sum(
        as_float((e.get("incoming_damage") or {}).get("final_damage"), 0.0)
        for e in incoming_events
    )
    mitigated_pct = 0.0
    if total_raw_incoming > 0:
        mitigated_pct = max(0.0, min(1.0, 1.0 - (total_final_incoming / total_raw_incoming)))

    total_lifesteal_applied = sum(
        as_float((e.get("lifesteal") or {}).get("applied_heal"), 0.0)
        for e in lifesteal_events
    )
    total_lifesteal_overheal = sum(
        as_float((e.get("lifesteal") or {}).get("overheal"), 0.0)
        for e in lifesteal_events
    )
    lifesteal_overheal_pct = 0.0
    if (total_lifesteal_applied + total_lifesteal_overheal) > 0:
        lifesteal_overheal_pct = total_lifesteal_overheal / (
            total_lifesteal_applied + total_lifesteal_overheal
        )

    top_enemy_type = ""
    top_enemy_hits = -1
    top_enemy_p90 = 0.0
    for item in enemy_summary:
        hits = as_int(item.get("hits"), 0)
        if hits > top_enemy_hits:
            top_enemy_hits = hits
            top_enemy_type = str(item.get("enemy_type") or "")
            top_enemy_p90 = as_float(item.get("p90_hits_per_kill"), 0.0)

    run_duration = as_float(terminal_event.get("run_time_s"), 0.0)
    summary = {
        "run_id": run_id,
        "source_file": terminal_event.get("_source_file", ""),
        "schema_version": as_int(terminal_event.get("schema_version"), 0),
        "utc_started": (run_started or {}).get("utc_timestamp", ""),
        "utc_ended": (run_ended or {}).get("utc_timestamp", ""),
        "end_reason": (run_ended or {}).get("reason", "incomplete"),
        "duration_s": round(run_duration, 3),
        "difficulty": as_int(world.get("difficulty"), 0),
        "level": as_int(player.get("level"), 0),
        "max_health": round(as_float(player.get("max_health"), 0.0), 3),
        "damage": round(as_float(((player.get("effective_stats") or {}).get("damage")), 0.0), 3),
        "crit_chance": round(
            as_float(((player.get("effective_stats") or {}).get("crit_chance")), 0.0), 4
        ),
        "life_steal": round(
            as_float(((player.get("effective_stats") or {}).get("life_steal")), 0.0), 4
        ),
        "upgrades_applied": as_int(counters.get("upgrades_applied"), 0),
        "relics_applied": as_int(counters.get("relics_applied"), 0),
        "enhancers_applied": as_int(counters.get("enhancers_applied"), 0),
        "melee_hits": as_int(counters.get("melee_hits"), 0),
        "melee_kills": as_int(counters.get("melee_kills"), 0),
        "avg_hits_to_kill": round(as_float(counters.get("average_hits_to_kill"), 0.0), 3),
        "incoming_raw_damage": round(total_raw_incoming, 3),
        "incoming_final_damage": round(total_final_incoming, 3),
        "incoming_mitigated_pct": round(mitigated_pct, 4),
        "lifesteal_applied_heal": round(total_lifesteal_applied, 3),
        "lifesteal_overheal": round(total_lifesteal_overheal, 3),
        "lifesteal_overheal_pct": round(lifesteal_overheal_pct, 4),
        "low_hp_warning_s": round(as_float(counters.get("time_below_warning_s"), 0.0), 3),
        "low_hp_critical_s": round(as_float(counters.get("time_below_critical_s"), 0.0), 3),
        "relic_rolls": as_int(counters.get("relic_rolls"), 0),
        "relic_roll_rejections": as_int(counters.get("relic_roll_rejections"), 0),
        "top_enemy_type": top_enemy_type,
        "top_enemy_p90_hits_to_kill": round(top_enemy_p90, 3),
    }
    return summary


def write_csv(path: Path, rows: list[dict[str, Any]]) -> None:
    if not rows:
        path.write_text("", encoding="utf-8")
        return

    fieldnames = list(rows[0].keys())
    with open(path, "w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)


def md_line(name: str, value: Any) -> str:
    return f"- {name}: {value}"


def safe_median(values: list[float]) -> float:
    if not values:
        return 0.0
    return float(statistics.median(values))


def build_markdown(rows: list[dict[str, Any]], input_pattern: str) -> str:
    if not rows:
        return "\n".join(
            [
                "# Gameplay Telemetry Report",
                "",
                md_line("Input pattern", input_pattern),
                md_line("Runs analyzed", 0),
                "",
                "No telemetry runs found.",
                "",
            ]
        )

    durations = [as_float(r["duration_s"]) for r in rows]
    levels = [as_float(r["level"]) for r in rows]
    avg_hits = [as_float(r["avg_hits_to_kill"]) for r in rows]
    mitigated = [as_float(r["incoming_mitigated_pct"]) for r in rows]
    lifesteal_overheal = [as_float(r["lifesteal_overheal_pct"]) for r in rows]
    low_hp_warning = [as_float(r["low_hp_warning_s"]) for r in rows]
    low_hp_critical = [as_float(r["low_hp_critical_s"]) for r in rows]

    by_reason: dict[str, int] = {}
    for row in rows:
        reason = str(row.get("end_reason") or "unknown")
        by_reason[reason] = by_reason.get(reason, 0) + 1

    lines = [
        "# Gameplay Telemetry Report",
        "",
        md_line("Input pattern", input_pattern),
        md_line("Runs analyzed", len(rows)),
        md_line("Median run duration (s)", round(safe_median(durations), 2)),
        md_line("Median final level", round(safe_median(levels), 2)),
        md_line("Median hits-to-kill", round(safe_median(avg_hits), 3)),
        md_line("Median incoming mitigation (%)", round(safe_median(mitigated) * 100.0, 2)),
        md_line("Median lifesteal overheal (%)", round(safe_median(lifesteal_overheal) * 100.0, 2)),
        md_line("Median low-HP warning time (s)", round(safe_median(low_hp_warning), 2)),
        md_line("Median low-HP critical time (s)", round(safe_median(low_hp_critical), 2)),
        "",
        "## End Reasons",
    ]

    for reason, count in sorted(by_reason.items(), key=lambda item: (-item[1], item[0])):
        lines.append(md_line(reason, count))

    lines.extend(
        [
            "",
            "## Notes",
            "- Use the CSV for filtering by run-level metrics and outliers.",
            "- Inspect raw JSONL for event-level drill-down (incoming damage, relic rolls, low HP transitions).",
            "",
        ]
    )
    return "\n".join(lines)


def main() -> int:
    args = parse_args()
    output_dir = Path(args.out_dir)
    output_dir.mkdir(parents=True, exist_ok=True)

    events = load_events(args.input)
    grouped = group_events_by_run(events)
    rows = [summarize_run(run_id, run_events) for run_id, run_events in sorted(grouped.items())]

    csv_path = output_dir / args.csv_name
    md_path = output_dir / args.md_name

    write_csv(csv_path, rows)
    md_path.write_text(build_markdown(rows, args.input), encoding="utf-8")

    print(f"Telemetry runs analyzed: {len(rows)}")
    print(f"CSV: {csv_path}")
    print(f"Markdown: {md_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
