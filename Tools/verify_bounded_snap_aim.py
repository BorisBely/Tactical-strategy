"""Verify Bounded Snap Aim thresholds for representative weapons."""
from __future__ import annotations

import math

SNAP_PROGRESS = 0.35
QUICK_PROGRESS = 0.68
SNAP_MIN = 0.11
QUICK_MIN = 0.22
FULL_MIN = 0.32
ELITE_AIM_MULT = 0.935
BASE_SPREAD_TO_DEG = 0.35


def required_time(full_aim: float, mode: str) -> float:
    if mode == "full":
        return FULL_MIN if full_aim < 0.15 else full_aim
    progress = SNAP_PROGRESS if mode == "snap" else QUICK_PROGRESS
    scaled = full_aim * progress
    minimum = SNAP_MIN if mode == "snap" else QUICK_MIN
    return max(scaled, minimum)


def required_progress(full_aim: float, mode: str) -> float:
    return min(1.0, required_time(full_aim, mode) / full_aim)


def incomplete_spread_mult(progress: float, distance: float) -> float:
    if progress >= 1.0:
        return 1.0
    if progress >= 0.85:
        base = 1.15 + (1.0 - 1.15) * ((progress - 0.85) / 0.15)
    elif progress >= QUICK_PROGRESS:
        base = 1.45 + (1.15 - 1.45) * ((progress - QUICK_PROGRESS) / (0.85 - QUICK_PROGRESS))
    elif progress >= SNAP_PROGRESS:
        base = 2.20 + (1.45 - 2.20) * ((progress - SNAP_PROGRESS) / (QUICK_PROGRESS - SNAP_PROGRESS))
    else:
        base = 3.00 + (2.20 - 3.00) * (progress / SNAP_PROGRESS)

    if base <= 1.0:
        return 1.0
    if distance <= 10:
        scale = 0.60
    elif distance <= 25:
        scale = 0.60 + (1.00 - 0.60) * ((distance - 10) / 15)
    elif distance <= 50:
        scale = 1.00 + (1.25 - 1.00) * ((distance - 25) / 25)
    elif distance <= 100:
        scale = 1.25 + (1.50 - 1.25) * ((distance - 50) / 50)
    else:
        scale = 1.50
    return 1.0 + (base - 1.0) * scale


def spread_diam(distance: float, half_angle_deg: float) -> float:
    if distance <= 0:
        return 0.0
    return 2 * distance * math.tan(math.radians(half_angle_deg))


cases = [
    ("MK18 CQB Elite @0m", 0.19 * ELITE_AIM_MULT, 0.726, 0),
    ("MK18 CQB Elite @50m", 0.58 * ELITE_AIM_MULT, 0.726, 50),
    ("AK74U CQB Elite @25m", 0.26 * 1.08 * ELITE_AIM_MULT, 0.822, 25),
    ("M4 Mid Elite @50m", 0.62 * ELITE_AIM_MULT, 0.243, 50),
    ("MK12 Long Elite @100m", 1.33 * ELITE_AIM_MULT, 0.121, 100),
]

print("=== Required time / progress ===")
for label, full_aim, _, dist in cases:
    for mode in ("snap", "quick", "full"):
        t = required_time(full_aim, mode)
        p = required_progress(full_aim, mode)
        print(f"{label:28} {mode:5} time={t:.3f}s progress={p:.2f}")

print("\n=== Snap spread multiplier by distance (at snap threshold progress) ===")
for label, full_aim, base_spread_factor, dist in cases:
    p = required_progress(full_aim, "snap")
    mult = incomplete_spread_mult(p, dist)
    half = base_spread_factor * mult * BASE_SPREAD_TO_DEG
    print(f"{label:28} snapMult={mult:.2f} halfAngle={half:.3f} diam@dist={spread_diam(dist if dist > 0 else 25, half):.2f}m")
