"""Patch m_DispersionMultiplierByDistance in weapon assets from WeaponDistanceCurveLibrary roles."""
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SHOOTING = ROOT / "Assets" / "GameData" / "Shooting"

ROLE_DISP_500M = {
    "CqbShort": [(0, 0.58), (125, 0.78), (250, 1.75), (375, 3.25), (500, 5.00)],
    "CqbControlled": [(0, 0.62), (125, 0.82), (250, 1.55), (375, 2.80), (500, 4.30)],
    "Carbine": [(0, 0.72), (125, 0.86), (250, 1.05), (375, 1.22), (500, 1.50)],
    "CarbineModA1": [(0, 0.73), (125, 0.86), (250, 1.02), (375, 1.18), (500, 1.45)],
    "CarbineModA2": [(0, 0.75), (125, 0.84), (250, 1.00), (375, 1.10), (500, 1.40)],
    "BattleRifle762": [(0, 0.78), (125, 0.95), (250, 1.20), (375, 1.48), (500, 1.95)],
    "BattleRifle762Default": [(0, 0.80), (125, 0.98), (250, 1.22), (375, 1.52), (500, 1.95)],
    "BattleRifle762WoodHandguard": [(0, 0.79), (125, 0.94), (250, 1.12), (375, 1.38), (500, 1.72)],
    "BattleRifle762Mod1": [(0, 0.82), (125, 0.93), (250, 1.10), (375, 1.30), (500, 1.65)],
    "Intermediate545": [(0, 0.74), (125, 0.88), (250, 1.02), (375, 1.12), (500, 1.40)],
    "MidRifle": [(0, 0.90), (125, 0.75), (250, 0.65), (375, 0.88), (500, 1.45)],
    "Marksman": [(0, 1.00), (125, 0.82), (250, 0.58), (375, 0.70), (500, 1.20)],
    "Dmr": [(0, 1.15), (125, 1.00), (250, 0.70), (375, 0.50), (500, 0.62)],
    "Support762": [(0, 1.05), (125, 0.90), (250, 0.74), (375, 0.82), (500, 1.30)],
    "Support545": [(0, 1.00), (125, 0.85), (250, 0.66), (375, 0.70), (500, 1.05)],
}

WEAPON_ROLES = {
    "Weapon_AK47.asset": "BattleRifle762Default",
    "Weapon_AK47_1.asset": "BattleRifle762WoodHandguard",
    "Weapon_AK47MOD1.asset": "BattleRifle762Mod1",
    "Weapon_AK47S.asset": "CqbControlled",
    "Weapon_AK74.asset": "Intermediate545",
    "Weapon_AK74MOD1.asset": "Intermediate545",
    "Weapon_AK74U.asset": "CqbShort",
    "Weapon_AK74UMOD1.asset": "CqbControlled",
    "Weapon_RPK47.asset": "Support762",
    "Weapon_RPK47MOD1.asset": "Support762",
    "Weapon_RPK74.asset": "Support545",
    "Weapon_RPK74MOD1.asset": "Support545",
    "Weapon_M4_ModA_1.asset": "CarbineModA1",
    "Weapon_M4_ModA_2.asset": "CarbineModA2",
    "Weapon_M16A_ModA_1.asset": "MidRifle",
    "Weapon_M16A4_ModA_2.asset": "Marksman",
    "Weapon_MK12.asset": "Dmr",
    "Weapon_MK18.asset": "CqbShort",
}


def key_block(time: float, value: float) -> str:
    return (
        "      - serializedVersion: 3\n"
        f"        time: {time:g}\n"
        f"        value: {value:g}\n"
        "        inSlope: 0\n"
        "        outSlope: 0\n"
        "        tangentMode: 0\n"
        "        weightedMode: 0\n"
        "        inWeight: 0\n"
        "        outWeight: 0\n"
    )


def curve_yaml(keys: list[tuple[float, float]]) -> str:
    body = "".join(key_block(t, v) for t, v in keys)
    return (
        "    m_DispersionMultiplierByDistance:\n"
        "      serializedVersion: 2\n"
        "      m_Curve:\n"
        f"{body}"
        "      m_PreInfinity: 2\n"
        "      m_PostInfinity: 2\n"
        "      m_RotationOrder: 4\n"
    )


def patch_weapon(path: Path, role: str) -> None:
    text = path.read_text(encoding="utf-8")
    pattern = r"    m_DispersionMultiplierByDistance:\n(?:      .*\n)+?      m_RotationOrder: 4\n"
    replacement = curve_yaml(ROLE_DISP_500M[role])
    new_text, count = re.subn(pattern, replacement, text, count=1)
    if count != 1:
        raise RuntimeError(f"Failed to patch dispersion curve in {path}")
    path.write_text(new_text, encoding="utf-8", newline="\n")
    print(f"Patched {path.name} [{role}]")


def main() -> None:
    raise SystemExit(
        "patch_weapon_dispersion_curves.py is retired. "
        "Stage 10 owner: python Tools/bake_accuracy_aim_curves.py"
    )
    for rel, role in WEAPON_ROLES.items():
        matches = list(SHOOTING.rglob(rel))
        if not matches:
            print(f"Missing {rel}")
            continue
        for match in matches:
            patch_weapon(match, role)
            patched += 1
    print(f"Done: {patched} weapon dispersion curves patched.")


if __name__ == "__main__":
    main()
