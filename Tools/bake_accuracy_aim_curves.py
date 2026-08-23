"""Bake Stage 10 Accuracy / AimTime curves into weapon and optic assets.

Does not write ScopeVisionRange, EffectiveRange, BaseDamage, recoil, burst-by-shot.

Usage: python Tools/bake_accuracy_aim_curves.py
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "Tools"))

from accuracy_aim_curves_catalog import OPTIC_CURVES, weapon_curves_for_asset  # noqa: E402
from combat_balance_model import WEAPON_ROLE  # noqa: E402

SHOOTING = ROOT / "Assets" / "GameData" / "Shooting"


def key_block(time: float, value: float) -> str:
    time_s = f"{time:g}"
    value_s = f"{value:g}"
    return (
        "      - serializedVersion: 3\n"
        f"        time: {time_s}\n"
        f"        value: {value_s}\n"
        "        inSlope: 0\n"
        "        outSlope: 0\n"
        "        tangentMode: 0\n"
        "        weightedMode: 0\n"
        "        inWeight: 0\n"
        "        outWeight: 0\n"
    )


def curve_yaml(field_name: str, keys: list[tuple[float, float]]) -> str:
    body = "".join(key_block(t, v) for t, v in keys)
    return (
        f"    {field_name}:\n"
        "      serializedVersion: 2\n"
        "      m_Curve:\n"
        f"{body}"
        "      m_PreInfinity: 2\n"
        "      m_PostInfinity: 2\n"
        "      m_RotationOrder: 4\n"
    )


def patch_profile_curves(path: Path, disp: list[tuple[float, float]], aim: list[tuple[float, float]]) -> None:
    text = path.read_text(encoding="utf-8")
    disp_pat = r"    m_DispersionMultiplierByDistance:\n(?:      .*\n)+?      m_RotationOrder: 4\n"
    aim_pat = r"    m_AimTimeMultiplierByDistance:\n(?:      .*\n)+?      m_RotationOrder: 4\n"
    new_text, disp_count = re.subn(disp_pat, curve_yaml("m_DispersionMultiplierByDistance", disp), text, count=1)
    if disp_count != 1:
        raise RuntimeError(f"Failed to patch dispersion in {path}")
    new_text, aim_count = re.subn(aim_pat, curve_yaml("m_AimTimeMultiplierByDistance", aim), new_text, count=1)
    if aim_count != 1:
        raise RuntimeError(f"Failed to patch aim-time in {path}")
    path.write_text(new_text, encoding="utf-8", newline="\n")


def main() -> None:
    baked = 0
    for stem, (disp, aim) in OPTIC_CURVES.items():
        matches = list(SHOOTING.rglob(f"{stem}.asset"))
        if not matches:
            raise RuntimeError(f"Missing optic {stem}")
        for match in matches:
            patch_profile_curves(match, disp, aim)
            print(f"optic {match.name}")
            baked += 1

    for stem in WEAPON_ROLE:
        disp, aim = weapon_curves_for_asset(stem)
        matches = list(SHOOTING.rglob(f"{stem}.asset"))
        if not matches:
            raise RuntimeError(f"Missing weapon {stem}")
        for match in matches:
            patch_profile_curves(match, disp, aim)
            print(f"weapon {match.name}")
            baked += 1

    print(f"Done: {baked} distance profiles baked (Stage 10).")


if __name__ == "__main__":
    main()
