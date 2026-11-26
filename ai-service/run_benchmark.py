import argparse
import json
import os
import sys
import time
from pathlib import Path
import numpy as np

try:
    import requests
except ImportError:
    print("Please install requests: pip install requests")
    sys.exit(1)

def run_benchmark(api_url: str, manifest_path: str, output_report: str):
    with open(manifest_path, "r", encoding="utf-8") as f:
        manifest = json.load(f)

    items = manifest.get("items", [])
    print(f"Starting AI benchmark with {len(items)} test samples...")
    print(f"Target AI Endpoint: {api_url}")

    results = []
    latencies = []
    correct_count = 0
    unreadable_count = 0
    error_count = 0

    base_dir = Path(manifest_path).parent

    for idx, item in enumerate(items):
        img_rel_path = item["image_path"]
        expected_plate = item.get("ground_truth", "").replace("-", "").replace(" ", "").upper()
        condition = item.get("condition", "normal")
        img_full_path = base_dir / img_rel_path

        if not img_full_path.exists():
            print(f"[{idx+1}/{len(items)}] File not found: {img_full_path}")
            error_count += 1
            continue

        t0 = time.perf_counter()
        try:
            with open(img_full_path, "rb") as img_file:
                files = {"file": ("test.jpg", img_file, "image/jpeg")}
                res = requests.post(f"{api_url.rstrip('/')}/api/v1/detect", files=files, timeout=30)
                elapsed_ms = (time.perf_counter() - t0) * 1000
                latencies.append(elapsed_ms)

                if res.status_code == 200:
                    data = res.json()
                    detected = data.get("data")
                    detected_plate = (detected.get("plate_number") or "").replace("-", "").replace(" ", "").upper()
                    is_correct = (detected_plate == expected_plate) if expected_plate else False
                    if is_correct:
                        correct_count += 1
                    results.append({
                        "sample": img_rel_path,
                        "condition": condition,
                        "ground_truth": expected_plate,
                        "detected": detected_plate,
                        "is_correct": is_correct,
                        "confidence": detected.get("confidence", 0),
                        "latency_ms": elapsed_ms,
                        "status": "SUCCESS"
                    })
                elif res.status_code == 400:
                    unreadable_count += 1
                    results.append({
                        "sample": img_rel_path,
                        "condition": condition,
                        "ground_truth": expected_plate,
                        "detected": None,
                        "is_correct": expected_plate == "",
                        "latency_ms": elapsed_ms,
                        "status": "UNREADABLE"
                    })
                else:
                    error_count += 1
                    results.append({
                        "sample": img_rel_path,
                        "condition": condition,
                        "ground_truth": expected_plate,
                        "error_status": res.status_code,
                        "latency_ms": elapsed_ms,
                        "status": "ERROR"
                    })
        except Exception as e:
            elapsed_ms = (time.perf_counter() - t0) * 1000
            error_count += 1
            results.append({
                "sample": img_rel_path,
                "condition": condition,
                "error": str(e),
                "latency_ms": elapsed_ms,
                "status": "EXCEPTION"
            })

    total = len(items)
    p50 = np.percentile(latencies, 50) if latencies else 0
    p95 = np.percentile(latencies, 95) if latencies else 0
    p99 = np.percentile(latencies, 99) if latencies else 0
    acc = (correct_count / total * 100) if total > 0 else 0

    report = {
        "timestamp": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "total_samples": total,
        "accuracy_rate_pct": round(acc, 2),
        "unreadable_count": unreadable_count,
        "error_count": error_count,
        "latency_stats_ms": {
            "p50": round(float(p50), 2),
            "p95": round(float(p95), 2),
            "p99": round(float(p99), 2)
        },
        "details": results
    }

    with open(output_report, "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2, ensure_ascii=False)

    print("\n========== BENCHMARK RESULTS ==========")
    print(f"Total Samples : {total}")
    print(f"Accuracy Rate : {acc:.2f}%")
    print(f"Latency p50   : {p50:.2f} ms")
    print(f"Latency p95   : {p95:.2f} ms")
    print(f"Latency p99   : {p99:.2f} ms")
    print(f"Report saved  : {output_report}")

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Run AI License Plate Benchmark")
    parser.add_argument("--url", default="https://aiservice-production-9c362d03.up.railway.app", help="AI service base URL")
    parser.add_argument("--manifest", default="benchmark_manifest.json", help="Path to manifest JSON")
    parser.add_argument("--output", default="benchmark_report.json", help="Path to output report")
    args = parser.parse_args()

    run_benchmark(args.url, args.manifest, args.output)
