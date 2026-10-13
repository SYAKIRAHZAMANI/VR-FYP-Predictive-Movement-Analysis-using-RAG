"""
Pilot Study Motor Learning Analysis Script
Calculates key evaluation metrics comparing STATIC (baseline) vs AI (adaptive RAG) conditions:
  - Accuracy (% Made shots)
  - Biomechanical Consistency (std dev of release speed, loft ratio)
  - Movement Jitter (norm of acceleration standard deviation)
  - Learning Progression (first half vs second half accuracy)
"""

import glob
import os
import csv
import math
from collections import defaultdict


def analyze_sessions(data_dir="Assets/TrajectoryData"):
    files = sorted(glob.glob(os.path.join(data_dir, "P*.csv")))
    if not files:
        print(f"No participant files found in {data_dir}!")
        return

    sessions = defaultdict(lambda: {
        "trials": 0,
        "makes": 0,
        "speeds": [],
        "lofts": [],
        "jitters": [],
        "outcomes": []
    })

    for filepath in files:
        filename = os.path.basename(filepath)
        cond = "STATIC" if "STATIC" in filename.upper() else ("AI" if "AI" in filename.upper() else "OTHER")
        pid = filename.split()[0].upper()
        group_key = f"{pid}_{cond}"

        trials = defaultdict(list)
        with open(filepath, "r", encoding="utf-8") as fp:
            reader = csv.DictReader(fp)
            for row in reader:
                tid = row.get("trial_id")
                if tid:
                    trials[tid].append(row)

        for tid, rows in trials.items():
            if len(rows) < 5:
                continue

            outcome = rows[-1].get("outcome", "Miss")
            is_made = (outcome == "Made")
            sessions[group_key]["trials"] += 1
            if is_made:
                sessions[group_key]["makes"] += 1
            sessions[group_key]["outcomes"].append(1 if is_made else 0)

            speeds, acc_mags = [], []
            for r in rows:
                vx = float(r.get("linear_vx") or 0)
                vy = float(r.get("linear_vy") or 0)
                vz = float(r.get("linear_vz") or 0)
                ax = float(r.get("linear_ax") or 0)
                ay = float(r.get("linear_ay") or 0)
                az = float(r.get("linear_az") or 0)

                spd = math.sqrt(vx * vx + vy * vy + vz * vz)
                speeds.append(spd)
                acc_mags.append(math.sqrt(ax * ax + ay * ay + az * az))

            peak_idx = speeds.index(max(speeds)) if speeds else 0
            peak_vx = float(rows[peak_idx].get("linear_vx") or 0)
            peak_vy = float(rows[peak_idx].get("linear_vy") or 0)
            peak_vz = float(rows[peak_idx].get("linear_vz") or 0)
            v_horiz = math.hypot(peak_vx, peak_vz)
            loft = peak_vy / max(v_horiz, 1e-4)

            mean_acc = sum(acc_mags) / len(acc_mags)
            jitter = math.sqrt(sum((x - mean_acc) ** 2 for x in acc_mags) / len(acc_mags))

            sessions[group_key]["speeds"].append(max(speeds))
            sessions[group_key]["lofts"].append(loft)
            sessions[group_key]["jitters"].append(jitter)

    print("\n" + "=" * 90)
    print(f"{'PARTICIPANT PILOT STUDY RESULTS':^90}")
    print("=" * 90)
    print(f"{'Session':<14} | {'Attempts':<9} | {'Makes':<6} | {'Accuracy':<10} | {'Speed (Mean+/-Std)':<18} | {'Loft (Mean+/-Std)':<16} | {'Jitter':<8}")
    print("-" * 90)

    cond_aggregates = defaultdict(lambda: {"trials": 0, "makes": 0, "speeds": [], "lofts": [], "jitters": []})

    for key, data in sorted(sessions.items()):
        n = data["trials"]
        if n == 0:
            continue
        acc = (data["makes"] / n) * 100

        def mean_std(arr):
            if not arr: return 0.0, 0.0
            m = sum(arr) / len(arr)
            var = sum((x - m) ** 2 for x in arr) / len(arr)
            return m, math.sqrt(var)

        m_spd, s_spd = mean_std(data["speeds"])
        m_loft, s_loft = mean_std(data["lofts"])
        m_jit, _ = mean_std(data["jitters"])

        cond = "STATIC" if "STATIC" in key else "AI"
        cond_aggregates[cond]["trials"] += n
        cond_aggregates[cond]["makes"] += data["makes"]
        cond_aggregates[cond]["speeds"].extend(data["speeds"])
        cond_aggregates[cond]["lofts"].extend(data["lofts"])
        cond_aggregates[cond]["jitters"].extend(data["jitters"])

        print(f"{key:<14} | {n:<9} | {data['makes']:<6} | {acc:>7.1f}%   | {m_spd:>5.1f} +/- {s_spd:<5.1f} m/s  | {m_loft:>5.2f} +/- {s_loft:<5.2f}    | {m_jit:>6.1f}")

    print("-" * 90)
    print("\n" + "=" * 60)
    print(f"{'OVERALL CONDITION COMPARISON':^60}")
    print("=" * 60)
    for cond in ["STATIC", "AI"]:
        agg = cond_aggregates[cond]
        n = agg["trials"]
        if n > 0:
            acc = (agg["makes"] / n) * 100
            m_spd = sum(agg["speeds"]) / n
            m_loft = sum(agg["lofts"]) / n
            m_jit = sum(agg["jitters"]) / n
            print(f"Condition: {cond}")
            print(f"  Total Attempts:  {n}")
            print(f"  Total Makes:     {agg['makes']}")
            print(f"  Overall Accuracy:{acc:6.1f}%")
            print(f"  Mean Speed:      {m_spd:6.2f} m/s")
            print(f"  Mean Loft Ratio: {m_loft:6.2f}")
            print(f"  Mean Jitter:     {m_jit:6.2f}")
            print("-" * 60)


if __name__ == "__main__":
    analyze_sessions()
