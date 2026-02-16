#!/usr/bin/env python3
"""Validate measured runtime metrics against a performance budget."""

import argparse
import json
import sys
from pathlib import Path
from typing import Dict, Tuple


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Check measured performance metrics against budget thresholds."
    )
    parser.add_argument(
        "--metrics",
        required=True,
        help="Path to JSON file with measured metrics.",
    )
    parser.add_argument(
        "--budget",
        required=True,
        help="Path to JSON file with budget thresholds.",
    )
    parser.add_argument(
        "--allow-missing",
        action="store_true",
        help="Allow metrics missing from the measured file.",
    )
    parser.add_argument(
        "--output-json",
        help="Optional path to write machine-readable gate results.",
    )
    return parser.parse_args()


def load_json(path_str: str) -> Dict[str, float]:
    path = Path(path_str).expanduser().resolve()
    if not path.exists():
        raise FileNotFoundError(f"File not found: {path}")
    data = json.loads(path.read_text(encoding="utf-8-sig"))
    if not isinstance(data, dict):
        raise ValueError(f"Expected object JSON at {path}")
    return data


def evaluate_metric(measured: float, budget: float) -> Tuple[str, bool]:
    passed = measured <= budget
    status = "PASS" if passed else "FAIL"
    return status, passed


def main() -> int:
    args = parse_args()
    try:
        metrics = load_json(args.metrics)
        budget = load_json(args.budget)
    except (FileNotFoundError, ValueError, json.JSONDecodeError) as exc:
        print(f"[ERROR] {exc}")
        return 2

    print("=== Performance Budget Gate ===")
    failures = 0
    results = {}

    for key, budget_value in budget.items():
        if key not in metrics:
            if args.allow_missing:
                print(f"- {key}: SKIP (missing in metrics, allowed)")
                results[key] = {"status": "SKIP", "reason": "missing"}
                continue
            print(f"- {key}: FAIL (missing in metrics)")
            results[key] = {"status": "FAIL", "reason": "missing"}
            failures += 1
            continue

        measured_value = metrics[key]
        try:
            measured_num = float(measured_value)
            budget_num = float(budget_value)
        except (TypeError, ValueError):
            print(f"- {key}: FAIL (non-numeric values)")
            results[key] = {"status": "FAIL", "reason": "non-numeric"}
            failures += 1
            continue

        status, passed = evaluate_metric(measured_num, budget_num)
        print(f"- {key}: {status} (measured={measured_num}, budget={budget_num})")
        results[key] = {
            "status": status,
            "measured": measured_num,
            "budget": budget_num,
        }
        if not passed:
            failures += 1

    if args.output_json:
        output_path = Path(args.output_json).expanduser().resolve()
        output_path.parent.mkdir(parents=True, exist_ok=True)
        output_payload = {
            "failures": failures,
            "passed": failures == 0,
            "results": results,
        }
        output_path.write_text(
            json.dumps(output_payload, indent=2, ensure_ascii=True),
            encoding="utf-8",
        )
        print(f"\nWrote gate output to: {output_path}")

    if failures == 0:
        print("\nBudget gate passed.")
        return 0

    print(f"\nBudget gate failed ({failures} issue(s)).")
    return 1


if __name__ == "__main__":
    sys.exit(main())
