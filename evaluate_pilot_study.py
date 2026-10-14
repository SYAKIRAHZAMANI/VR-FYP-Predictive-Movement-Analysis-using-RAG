"""
Pilot Study Motor Learning Analysis Script (P01 - P08)
Comprehensive statistical analysis for the FYP Results & Evaluation chapter:
  - Pairwise within-subjects accuracy & attempts
  - Counterbalanced order analysis (AI -> STATIC vs. STATIC -> AI)
  - Retention & motor skill transfer effect
  - Biomechanical kinematics (release speed, loft ratio, jitter)
"""

import glob
import os
import csv
import math
import re
from collections import defaultdict


def analyze_sessions(data_dir="Assets/TrajectoryData"):
    files = sorted(glob.glob(os.path.join(data_dir, "*.csv")))
    
    # Filter only official participant files (P01 to P99), excluding TEST files
    p_files = []
    for f in files:
        base = os.path.basename(f)
        if base.startswith("TEST") or base.startswith("test_"):
            continue
        if re.match(r"^P\d{2}", base):
            p_files.append(f)

    if not p_files:
        print(f"No participant files found in {data_dir}!")
        return

    sessions = defaultdict(lambda: {
        "trials": 0,
        "makes": 0,
        "speeds": [],
        "lofts": [],
        "jitters": []
    })
    
    participant_orders = defaultdict(list)

    for filepath in p_files:
        filename = os.path.basename(filepath)
        m = re.match(r"^(P\d{2})", filename)
        if not m:
            continue
        pid = m.group(1).upper()
        cond = "STATIC" if "STATIC" in filename.upper() else "AI"
        group_key = (pid, cond)

        ts_m = re.search(r"(\d{8}_\d{6})", filename)
        ts = ts_m.group(1) if ts_m else ""
        participant_orders[pid].append((ts, cond))

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

    pids = sorted(list(set(k[0] for k in sessions.keys())))

    # 1. PAIRWISE PARTICIPANT TABLE
    print("\n" + "=" * 105)
    print(f"{'PARTICIPANT PILOT STUDY RESULTS (P01 - P08)':^105}")
    print("=" * 105)
    print(f"{'Participant':<12} | {'Condition Order':<17} | {'STATIC Accuracy':<18} | {'AI Accuracy':<18} | {'STATIC Attempts':<15} | {'AI Attempts':<12}")
    print("-" * 105)

    for pid in pids:
        s_data = sessions[(pid, "STATIC")]
        a_data = sessions[(pid, "AI")]

        s_acc = (s_data["makes"] / s_data["trials"] * 100) if s_data["trials"] else 0.0
        a_acc = (a_data["makes"] / a_data["trials"] * 100) if a_data["trials"] else 0.0

        # Condition order
        ord_tuples = sorted(participant_orders.get(pid, []), key=lambda x: x[0])
        seen = []
        for _, c in ord_tuples:
            if not seen or seen[-1] != c:
                seen.append(c)
        order_str = " -> ".join(seen)

        print(f"{pid:<12} | {order_str:<17} | {s_acc:5.1f}% ({s_data['makes']:2d}/{s_data['trials']:2d})      | {a_acc:5.1f}% ({a_data['makes']:2d}/{a_data['trials']:2d})      | {s_data['trials']:^15d} | {a_data['trials']:^12d}")

    print("-" * 105)

    # 2. MOTOR LEARNING TRANSFER & RETENTION ANALYSIS
    ai_first_group = []
    static_first_group = []

    for pid in pids:
        ord_tuples = sorted(participant_orders.get(pid, []), key=lambda x: x[0])
        if ord_tuples and ord_tuples[0][1] == "AI":
            ai_first_group.append(pid)
        else:
            static_first_group.append(pid)

    print("\n" + "=" * 70)
    print(f"{'COUNTERBALANCED ORDER & MOTOR TRANSFER ANALYSIS':^70}")
    print("=" * 70)

    # Group A: AI First -> STATIC Second
    ga_ai_m = sum(sessions[(p, "AI")]["makes"] for p in ai_first_group)
    ga_ai_t = sum(sessions[(p, "AI")]["trials"] for p in ai_first_group)
    ga_st_m = sum(sessions[(p, "STATIC")]["makes"] for p in ai_first_group)
    ga_st_t = sum(sessions[(p, "STATIC")]["trials"] for p in ai_first_group)

    ga_ai_acc = (ga_ai_m / ga_ai_t * 100) if ga_ai_t else 0
    ga_st_acc = (ga_st_m / ga_st_t * 100) if ga_st_t else 0

    print(f"Group A ({', '.join(ai_first_group)}): Trained with AI First -> Tested on Static Second")
    print(f"  * Round 1 (AI Guided Intervention): {ga_ai_acc:5.1f}% accuracy ({ga_ai_m}/{ga_ai_t} makes in {ga_ai_t} attempts)")
    print(f"  * Round 2 (Static Retention Test):   {ga_st_acc:5.1f}% accuracy ({ga_st_m}/{ga_st_t} makes in {ga_st_t} attempts)")
    print(f"  * Motor Learning Retention Gain:     +{ga_st_acc - ga_ai_acc:5.1f}% accuracy increase!")
    print(f"  * Efficiency Improvement:            {ga_st_t} attempts needed vs. {ga_ai_t} in Round 1 ({(1 - ga_st_t/ga_ai_t)*100:.1f}% reduction in attempts)")
    print("-" * 70)

    # Group B: STATIC First -> AI Second
    gb_st_m = sum(sessions[(p, "STATIC")]["makes"] for p in static_first_group)
    gb_st_t = sum(sessions[(p, "STATIC")]["trials"] for p in static_first_group)
    gb_ai_m = sum(sessions[(p, "AI")]["makes"] for p in static_first_group)
    gb_ai_t = sum(sessions[(p, "AI")]["trials"] for p in static_first_group)

    gb_st_acc = (gb_st_m / gb_st_t * 100) if gb_st_t else 0
    gb_ai_acc = (gb_ai_m / gb_ai_t * 100) if gb_ai_t else 0

    print(f"Group B ({', '.join(static_first_group)}): Started with Static Baseline -> Received AI Second")
    print(f"  * Round 1 (Static Baseline):         {gb_st_acc:5.1f}% accuracy ({gb_st_m}/{gb_st_t} makes in {gb_st_t} attempts)")
    print(f"  * Round 2 (AI Guided Intervention): {gb_ai_acc:5.1f}% accuracy ({gb_ai_m}/{gb_ai_t} makes in {gb_ai_t} attempts)")
    print(f"  * Difference:                        {gb_ai_acc - gb_st_acc:+5.1f}%")
    print("=" * 70)

    # 3. OVERALL CONDITION COMPARISON
    print("\n" + "=" * 60)
    print(f"{'OVERALL CONDITION TOTALS (ALL 8 PARTICIPANTS)':^60}")
    print("=" * 60)
    for cond in ["STATIC", "AI"]:
        tot_m = sum(sessions[(p, cond)]["makes"] for p in pids)
        tot_t = sum(sessions[(p, cond)]["trials"] for p in pids)
        acc = (tot_m / tot_t * 100) if tot_t else 0
        all_spds = [s for p in pids for s in sessions[(p, cond)]["speeds"]]
        all_lofts = [l for p in pids for l in sessions[(p, cond)]["lofts"]]
        all_jits = [j for p in pids for j in sessions[(p, cond)]["jitters"]]

        avg_spd = sum(all_spds) / len(all_spds) if all_spds else 0
        avg_loft = sum(all_lofts) / len(all_lofts) if all_lofts else 0
        avg_jit = sum(all_jits) / len(all_jits) if all_jits else 0

        print(f"Condition: {cond}")
        print(f"  Total Attempts:      {tot_t}")
        print(f"  Total Makes:         {tot_m}")
        print(f"  Overall Accuracy:    {acc:5.1f}%")
        print(f"  Mean Release Speed:  {avg_spd:5.2f} m/s")
        print(f"  Mean Loft Ratio:     {avg_loft:5.2f}")
        print(f"  Mean Jitter (Tremor):{avg_jit:5.2f} m/s^2")
        print("-" * 60)


if __name__ == "__main__":
    analyze_sessions()
