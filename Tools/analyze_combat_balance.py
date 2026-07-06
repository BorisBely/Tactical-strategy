"""Analyze min/avg/max combat stats for weapon + attachment + rank combinations."""
from __future__ import annotations

import math
import re
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SHOOTING = ROOT / "Assets" / "GameData" / "Shooting"
RANKS_DIR = ROOT / "Assets" / "GameData" / "Combat" / "Ranks"

BASE_SPREAD_TO_DEGREES = 0.35
DISTANCES = [0, 25, 50, 75, 100]

# From WeaponDistanceCurveLibrary (aim softened)
ROLE_AIM = {
    "CqbShort": [(0, 0.92), (25, 1.08), (50, 2.55), (75, 4.15), (100, 5.85)],
    "CqbControlled": [(0, 0.84), (25, 1.10), (50, 2.36), (75, 3.79), (100, 5.33)],
    "Carbine": [(0, 0.85), (25, 1.15), (50, 2.14), (75, 3.29), (100, 4.46)],
    "CarbineModA1": [(0, 0.87), (25, 1.13), (50, 2.05), (75, 3.17), (100, 4.34)],
    "CarbineModA2": [(0, 0.90), (25, 1.12), (50, 1.98), (75, 3.05), (100, 4.20)],
    "BattleRifle762": [(0, 0.95), (25, 1.35), (50, 2.45), (75, 3.69), (100, 4.93)],
    "BattleRifle762Default": [(0, 0.96), (25, 1.38), (50, 2.52), (75, 3.80), (100, 5.06)],
    "BattleRifle762WoodHandguard": [(0, 0.98), (25, 1.34), (50, 2.38), (75, 3.58), (100, 4.78)],
    "BattleRifle762Mod1": [(0, 1.02), (25, 1.36), (50, 2.34), (75, 3.48), (100, 4.62)],
    "Intermediate545": [(0, 0.90), (25, 1.25), (50, 2.16), (75, 3.22), (100, 4.29)],
    "MidRifle": [(0, 1.25), (25, 1.12), (50, 1.62), (75, 2.24), (100, 2.99)],
    "Marksman": [(0, 1.50), (25, 1.30), (50, 1.64), (75, 1.91), (100, 2.47)],
    "Dmr": [(0, 1.80), (25, 1.60), (50, 1.74), (75, 1.65), (100, 1.84)],
    "Support762": [(0, 1.55), (25, 1.35), (50, 1.69), (75, 2.00), (100, 2.59)],
    "Support545": [(0, 1.50), (25, 1.28), (50, 1.61), (75, 1.86), (100, 2.37)],
}

ROLE_DISP = {
    "CqbShort": [(0, 0.58), (25, 0.78), (50, 1.75), (75, 3.25), (100, 5.00)],
    "CqbControlled": [(0, 0.62), (25, 0.82), (50, 1.55), (75, 2.80), (100, 4.30)],
    "Carbine": [(0, 0.72), (25, 0.86), (50, 1.12), (75, 1.70), (100, 2.50)],
    "CarbineModA1": [(0, 0.73), (25, 0.86), (50, 1.08), (75, 1.64), (100, 2.42)],
    "CarbineModA2": [(0, 0.75), (25, 0.84), (50, 1.03), (75, 1.57), (100, 2.35)],
    "BattleRifle762": [(0, 0.78), (25, 0.95), (50, 1.25), (75, 2.00), (100, 3.00)],
    "BattleRifle762Default": [(0, 0.80), (25, 0.98), (50, 1.32), (75, 2.12), (100, 3.18)],
    "BattleRifle762WoodHandguard": [(0, 0.79), (25, 0.94), (50, 1.22), (75, 1.92), (100, 2.88)],
    "BattleRifle762Mod1": [(0, 0.82), (25, 0.93), (50, 1.18), (75, 1.82), (100, 2.72)],
    "Intermediate545": [(0, 0.74), (25, 0.88), (50, 1.10), (75, 1.65), (100, 2.35)],
    "MidRifle": [(0, 0.90), (25, 0.75), (50, 0.65), (75, 1.00), (100, 1.70)],
    "Marksman": [(0, 1.00), (25, 0.82), (50, 0.58), (75, 0.70), (100, 1.20)],
    "Dmr": [(0, 1.15), (25, 1.00), (50, 0.70), (75, 0.50), (100, 0.62)],
    "Support762": [(0, 1.05), (25, 0.90), (50, 0.74), (75, 0.82), (100, 1.30)],
    "Support545": [(0, 1.00), (25, 0.85), (50, 0.66), (75, 0.70), (100, 1.05)],
}

ROLE_BURST = {
    "CqbShort": [(1, 1.00), (3, 1.50), (6, 3.10), (10, 6.00)],
    "CqbControlled": [(1, 1.00), (3, 1.42), (6, 2.75), (10, 5.20)],
    "Carbine": [(1, 1.00), (3, 1.25), (6, 1.90), (10, 3.20)],
    "CarbineModA1": [(1, 1.00), (3, 1.24), (6, 1.84), (10, 3.08)],
    "CarbineModA2": [(1, 1.00), (3, 1.23), (6, 1.82), (10, 3.02)],
    "BattleRifle762": [(1, 1.00), (3, 1.45), (6, 2.60), (10, 4.40)],
    "BattleRifle762Default": [(1, 1.00), (3, 1.49), (6, 2.72), (10, 4.62)],
    "BattleRifle762WoodHandguard": [(1, 1.00), (3, 1.42), (6, 2.48), (10, 4.12)],
    "BattleRifle762Mod1": [(1, 1.00), (3, 1.40), (6, 2.38), (10, 3.92)],
    "Intermediate545": [(1, 1.00), (3, 1.30), (6, 2.10), (10, 3.50)],
    "MidRifle": [(1, 1.00), (3, 1.15), (6, 1.65), (10, 2.60)],
    "Marksman": [(1, 1.00), (3, 1.10), (6, 1.45), (10, 2.20)],
    "Dmr": [(1, 1.00), (3, 1.08), (6, 1.32), (10, 1.90)],
    "Support762": [(1, 1.00), (3, 1.18), (6, 1.55), (10, 2.50)],
    "Support545": [(1, 1.00), (3, 1.12), (6, 1.42), (10, 2.20)],
}

WEAPON_ROLE = {
    "Weapon_AK47": "BattleRifle762Default",
    "Weapon_AK47_1": "BattleRifle762WoodHandguard",
    "Weapon_AK47MOD1": "BattleRifle762Mod1",
    "Weapon_AK47S": "CqbControlled",
    "Weapon_AK74": "Intermediate545",
    "Weapon_AK74MOD1": "Intermediate545",
    "Weapon_AK74U": "CqbShort",
    "Weapon_AK74UMOD1": "CqbControlled",
    "Weapon_RPK47": "Support762",
    "Weapon_RPK47MOD1": "Support762",
    "Weapon_RPK74": "Support545",
    "Weapon_RPK74MOD1": "Support545",
    "Weapon_M4_ModA_1": "CarbineModA1",
    "Weapon_M4_ModA_2": "CarbineModA2",
    "Weapon_M16A_ModA_1": "MidRifle",
    "Weapon_M16A4_ModA_2": "Marksman",
    "Weapon_MK12": "Dmr",
    "Weapon_MK18": "CqbShort",
}

# Optic curves from OpticDistanceCurveLibrary (disp, aim)
OPTIC_CURVES = {
    "Collimator": {
        "disp": [(0, 0.74), (10, 0.72), (15, 0.76), (25, 1.00), (40, 1.08), (100, 1.12)],
        "aim": [(0, 0.94), (15, 0.96), (25, 0.98), (40, 1.02), (100, 1.06)],
    },
    "Holographic": {
        "disp": [(0, 0.78), (20, 0.82), (35, 1.00), (100, 1.00)],
        "aim": [(0, 0.94), (20, 0.98), (35, 1.02), (100, 1.04)],
    },
    "Scope4x": {
        "disp": [(0, 1.00), (40, 0.84), (50, 0.76), (60, 1.00), (100, 1.00)],
        "aim": [(0, 1.20), (40, 1.10), (50, 1.02), (55, 1.00), (100, 1.00)],
    },
    "Scope9Long": {
        "disp": [(0, 1.00), (70, 0.94), (80, 0.74), (100, 0.62)],
        "aim": [(0, 1.70), (70, 1.58), (100, 1.42)],
    },
    "VariableMagnification": {
        "disp": [(0, 0.92), (20, 0.86), (40, 0.80), (60, 0.82), (65, 1.00), (100, 1.00)],
        "aim": [(0, 1.34), (20, 1.28), (40, 1.18), (55, 1.06), (65, 1.00), (100, 1.00)],
    },
    "AkCollimator": {
        "disp": [(0, 0.80), (15, 0.84), (30, 1.00), (100, 1.00)],
        "aim": [(0, 0.96), (15, 0.98), (30, 1.02), (100, 1.04)],
    },
    "AkPso": {
        "disp": [(0, 1.00), (40, 0.88), (50, 0.78), (60, 1.00), (100, 1.00)],
        "aim": [(0, 1.22), (40, 1.12), (50, 1.04), (60, 1.00), (100, 1.00)],
    },
}

ATTACHMENT_OPTIC_KIND = {
    "Attachment_M4_Reddot1": "Collimator",
    "Attachment_M4_Reddot2": "Holographic",
    "Attachment_M4_ACOG": "Scope4x",
    "Attachment_M4_Scope9": "Scope9Long",
    "Attachment_M4_Vortex_Razor": "VariableMagnification",
    "Attachment_AK_Reddot4_Rail": "AkCollimator",
    "Attachment_AK_Scope11": "AkPso",
}

RANKS = {
    "Recruit": {"marksmanship": 35, "handling": 40, "recoil": 35, "reaction": 0.38},
    "Soldier": {"marksmanship": 50, "handling": 50, "recoil": 50, "reaction": 0.32},
    "Veteran": {"marksmanship": 58, "handling": 56, "recoil": 58, "reaction": 0.27},
    "Specialist": {"marksmanship": 61, "handling": 68, "recoil": 60, "reaction": 0.23},
    "Elite": {"marksmanship": 65, "handling": 63, "recoil": 66, "reaction": 0.20},
}

SKILL_WORST_DISP = 1.25
SKILL_BEST_DISP = 0.75
SKILL_WORST_AIM = 1.25
SKILL_BEST_AIM = 0.75
SKILL_WORST_RECOIL = 1.2
SKILL_BEST_RECOIL = 0.8

LOADOUTS = {
    "Iron/Bare": [],
    "CQB": ["foregrip", "reddot"],
    "Mid": ["foregrip", "acog", "muzzle_brake"],
    "Long": ["foregrip", "scope9", "muzzle_brake"],
}

ATTACHMENT_FLAT = {
    "foregrip": {"aim": 0.97, "recoil": 0.88, "auto_recoil": 0.90},
    "reddot": {"aim": 0.94, "recoil": 1.0, "optic": "Collimator"},
    "acog": {"aim": 1.05, "recoil": 1.0, "optic": "Scope4x"},
    "scope9": {"aim": 1.12, "recoil": 1.02, "optic": "Scope9Long"},
    "muzzle_brake": {"aim": 1.0, "recoil": 0.82, "auto_recoil": 0.85},
    "silencer": {"aim": 1.04, "recoil": 0.95, "auto_recoil": 0.96},
    "ak_reddot": {"aim": 1.0, "recoil": 1.0, "optic": "AkCollimator"},
    "ak_pso": {"aim": 1.08, "recoil": 1.0, "optic": "AkPso"},
}


def lerp_curve(keys: list[tuple[float, float]], x: float) -> float:
    if x <= keys[0][0]:
        return keys[0][1]
    if x >= keys[-1][0]:
        return keys[-1][1]
    for i in range(len(keys) - 1):
        x0, y0 = keys[i]
        x1, y1 = keys[i + 1]
        if x0 <= x <= x1:
            t = (x - x0) / (x1 - x0) if x1 != x0 else 0
            return y0 + t * (y1 - y0)
    return keys[-1][1]


def skill_mult(skill: float, worst: float, best: float) -> float:
    t = skill / 100.0
    return worst + (best - worst) * t


def parse_weapon(path: Path) -> dict:
    t = path.read_text(encoding="utf-8")
    def get(name: str, default: float = 0) -> float:
        m = re.search(rf"  {name}: ([0-9.]+)", t)
        return float(m.group(1)) if m else default
    return {
        "name": path.stem,
        "aim_base": get("m_AimTimeSeconds"),
        "disp_base": get("m_BaseShotDispersion"),
        "recoil": get("m_RecoilPerShot"),
        "auto_recoil": get("m_AutoRecoilMultiplier"),
        "recovery": get("m_RecoilRecoveryPerSecond"),
        "rpm": get("m_FireRateRpm", 600),
    }


def resolve_loadout_attachments(loadout: str, platform: str) -> list[dict]:
    parts = LOADOUTS[loadout]
    result = []
    for p in parts:
        if p == "reddot":
            key = "ak_reddot" if platform == "AK" else "reddot"
        elif p == "acog":
            key = "acog"
        elif p == "scope9":
            key = "scope9"
        else:
            key = p
        result.append(ATTACHMENT_FLAT[key])
    return result


def attachment_products(atts: list[dict], distance: float) -> tuple[float, float, float]:
    aim_flat = 1.0
    disp = 1.0
    aim_dist = 1.0
    recoil = 1.0
    for a in atts:
        aim_flat *= a.get("aim", 1.0)
        recoil *= a.get("recoil", 1.0)
        optic = a.get("optic")
        if optic and optic in OPTIC_CURVES:
            disp *= lerp_curve(OPTIC_CURVES[optic]["disp"], distance)
            aim_dist *= lerp_curve(OPTIC_CURVES[optic]["aim"], distance)
    return disp, aim_flat * aim_dist, recoil


def compute_aim_time(weapon: dict, role: str, rank: dict, atts: list[dict], distance: float) -> float:
    w_aim = weapon["aim_base"] * lerp_curve(ROLE_AIM[role], distance)
    _, att_aim, _ = attachment_products(atts, distance)
    skill = skill_mult(rank["handling"], SKILL_WORST_AIM, SKILL_BEST_AIM)
    return w_aim * att_aim * skill


def compute_spread_deg(weapon: dict, role: str, rank: dict, atts: list[dict], distance: float, burst_shot: int = 1) -> float:
    w_disp = lerp_curve(ROLE_DISP[role], distance)
    att_disp, _, _ = attachment_products(atts, distance)
    burst = lerp_curve(ROLE_BURST[role], burst_shot)
    skill = skill_mult(rank["marksmanship"], SKILL_WORST_DISP, SKILL_BEST_DISP)
    raw = weapon["disp_base"] * w_disp * att_disp * burst * skill * BASE_SPREAD_TO_DEGREES
    return raw


def spread_diameter_m(distance: float, half_angle_deg: float) -> float:
    if distance <= 0:
        return 0.0
    rad = math.radians(half_angle_deg)
    return 2 * distance * math.tan(rad)


def main() -> None:
    weapons = []
    for rel in WEAPON_ROLE:
        for p in SHOOTING.rglob(f"{rel}.asset"):
            weapons.append(parse_weapon(p))
            break

    representative = [
        ("AK74U", "Weapon_AK74U", "CQB"),
        ("MK18", "Weapon_MK18", "CQB"),
        ("M4_ModA_2", "Weapon_M4_ModA_2", "Mid"),
        ("AK74MOD1", "Weapon_AK74MOD1", "Mid"),
        ("M16A4", "Weapon_M16A4_ModA_2", "Long"),
        ("MK12", "Weapon_MK12", "Long"),
        ("RPK74MOD1", "Weapon_RPK74MOD1", "Mid"),
    ]

    rank_min = RANKS["Recruit"]
    rank_avg = RANKS["Soldier"]
    rank_max = RANKS["Elite"]

    print("=== AIM TIME (sec, full aim, standing, no movement/condition penalties) ===")
    print(f"{'Combo':<42} {'0m':>6} {'25m':>6} {'50m':>6} {'75m':>6} {'100m':>6}")
    print("-" * 78)

    all_aim = []
    for label, wname, loadout in representative:
        weapon = next(w for w in weapons if w["name"] == wname)
        role = WEAPON_ROLE[wname]
        platform = "AK" if wname.startswith("Weapon_AK") or wname.startswith("Weapon_RPK") else "M4"
        atts = resolve_loadout_attachments(loadout, platform)
        for rank_name, rank in [("MIN Recruit", rank_min), ("AVG Soldier", rank_avg), ("MAX Elite", rank_max)]:
            times = [compute_aim_time(weapon, role, rank, atts, d) for d in DISTANCES]
            all_aim.extend(times)
            combo = f"{label} + {loadout} + {rank_name}"
            print(f"{combo:<42} " + " ".join(f"{t:6.2f}" for t in times))

    print()
    print("=== SPREAD half-angle (deg, semi shot 1, full aim, standing) ===")
    print(f"{'Combo':<42} {'0m':>6} {'25m':>6} {'50m':>6} {'75m':>6} {'100m':>6}  | @50m diam")
    print("-" * 95)

    all_spread = []
    for label, wname, loadout in representative:
        weapon = next(w for w in weapons if w["name"] == wname)
        role = WEAPON_ROLE[wname]
        platform = "AK" if wname.startswith("Weapon_AK") or wname.startswith("Weapon_RPK") else "M4"
        atts = resolve_loadout_attachments(loadout, platform)
        for rank_name, rank in [("MIN Recruit", rank_min), ("AVG Soldier", rank_avg), ("MAX Elite", rank_max)]:
            spreads = [compute_spread_deg(weapon, role, rank, atts, d) for d in DISTANCES]
            all_spread.extend(spreads)
            diam50 = spread_diameter_m(50, spreads[2])
            combo = f"{label} + {loadout} + {rank_name}"
            print(f"{combo:<42} " + " ".join(f"{s:6.3f}" for s in spreads) + f"  | {diam50:.2f}m")

    print()
    print("=== AUTO BURST spread @ shot 10, 50m, Elite ===")
    for label, wname, loadout in representative:
        weapon = next(w for w in weapons if w["name"] == wname)
        role = WEAPON_ROLE[wname]
        platform = "AK" if wname.startswith("Weapon_AK") or wname.startswith("Weapon_RPK") else "M4"
        atts = resolve_loadout_attachments(loadout, platform)
        s1 = compute_spread_deg(weapon, role, rank_max, atts, 50, 1)
        s10 = compute_spread_deg(weapon, role, rank_max, atts, 50, 10)
        print(f"{label:<12} {loadout:<6} shot1={s1:.3f}° shot10={s10:.3f}° (x{s10/s1:.1f}) diam10={spread_diameter_m(50,s10):.2f}m")

    print()
    print("=== GLOBAL RANGES (representative weapons, all ranks/loadouts) ===")
    print(f"Aim time:  min={min(all_aim):.2f}s  avg={sum(all_aim)/len(all_aim):.2f}s  max={max(all_aim):.2f}s")
    print(f"Spread:    min={min(all_spread):.3f}° avg={sum(all_spread)/len(all_spread):.3f}° max={max(all_spread):.3f}°")

    # Extreme combos
    print()
    print("=== EXTREME COMBOS (all 18 weapons) ===")
    extremes_aim = []
    extremes_spread = []
    for weapon in weapons:
        role = WEAPON_ROLE.get(weapon["name"], "Carbine")
        platform = "AK" if "AK" in weapon["name"] or "RPK" in weapon["name"] else "M4"
        for loadout in LOADOUTS:
            atts = resolve_loadout_attachments(loadout, platform)
            for rank in RANKS.values():
                for d in DISTANCES:
                    extremes_aim.append(compute_aim_time(weapon, role, rank, atts, d))
                    extremes_spread.append(compute_spread_deg(weapon, role, rank, atts, d))

    print("All weapons x 4 loadouts x 5 ranks x 5 distances:")
    print(f"  Aim:    min={min(extremes_aim):.2f}s  avg={sum(extremes_aim)/len(extremes_aim):.2f}s  max={max(extremes_aim):.2f}s")
    print(f"  Spread: min={min(extremes_spread):.3f}° avg={sum(extremes_spread)/len(extremes_spread):.3f}° max={max(extremes_spread):.3f}°")
    print(f"  Spread @100m max diam: {spread_diameter_m(100, max(extremes_spread)):.1f}m")


if __name__ == "__main__":
    main()
