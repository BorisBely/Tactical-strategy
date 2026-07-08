"""Bake weapon distance curves, auto-burst multipliers and base stats into WeaponDefinition assets."""
from __future__ import annotations

import re
from pathlib import Path

from combat_balance_model import (
    DISTANCE_KEYFRAMES,
    WEAPON_ROLE,
    build_accuracy_reference,
    build_adjusted_disp_curve,
    densify_curve,
)

ROOT = Path(__file__).resolve().parents[1]
SHOOTING = ROOT / "Assets" / "GameData" / "Shooting"


def disp_role(d0: float, d25: float, d50: float, d75: float, d100: float) -> list[tuple[float, float]]:
    return [(0, d0), (25, d25), (50, d50), (75, d75), (100, d100)]


def aim_role(d0: float, d25: float, d50: float, d75: float, d100: float) -> list[tuple[float, float]]:
    return [(0, d0), (25, d25), (50, d50), (75, d75), (100, d100)]


def burst_role(b1: float, b3: float, b6: float, b10: float) -> list[tuple[float, float]]:
    return [(1, b1), (3, b3), (6, b6), (10, b10)]


ROLE_CURVES: dict[str, dict[str, list[tuple[float, float]]]] = {
    "CqbShort": {
        "disp": disp_role(0.58, 0.78, 1.75, 3.25, 5.00),
        "aim": aim_role(0.92, 1.08, 2.55, 4.15, 5.85),
        "auto": burst_role(1.00, 1.50, 3.10, 6.00),
    },
    "CqbControlled": {
        "disp": disp_role(0.62, 0.82, 1.55, 2.80, 4.30),
        "aim": aim_role(0.84, 1.10, 2.36, 3.79, 5.33),
        "auto": burst_role(1.00, 1.42, 2.75, 5.20),
    },
    "Carbine": {
        "disp": disp_role(0.72, 0.86, 1.05, 1.22, 1.50),
        "aim": aim_role(0.85, 1.15, 2.14, 3.29, 4.46),
        "auto": burst_role(1.00, 1.25, 1.90, 3.20),
    },
    "CarbineModA1": {
        "disp": disp_role(0.73, 0.86, 1.02, 1.18, 1.45),
        "aim": aim_role(0.87, 1.13, 2.05, 3.17, 4.34),
        "auto": burst_role(1.00, 1.24, 1.84, 3.08),
    },
    "CarbineModA2": {
        "disp": disp_role(0.75, 0.84, 1.00, 1.10, 1.40),
        "aim": aim_role(0.90, 1.12, 1.98, 3.05, 4.20),
        "auto": burst_role(1.00, 1.23, 1.82, 3.02),
    },
    "BattleRifle762": {
        "disp": disp_role(0.78, 0.95, 1.20, 1.48, 1.95),
        "aim": aim_role(0.95, 1.35, 2.45, 3.69, 4.93),
        "auto": burst_role(1.00, 1.45, 2.60, 4.40),
    },
    "BattleRifle762Default": {
        "disp": disp_role(0.80, 0.98, 1.22, 1.52, 1.95),
        "aim": aim_role(0.96, 1.38, 2.52, 3.80, 5.06),
        "auto": burst_role(1.00, 1.49, 2.72, 4.62),
    },
    "BattleRifle762WoodHandguard": {
        "disp": disp_role(0.79, 0.94, 1.12, 1.38, 1.72),
        "aim": aim_role(0.98, 1.34, 2.38, 3.58, 4.78),
        "auto": burst_role(1.00, 1.42, 2.48, 4.12),
    },
    "BattleRifle762Mod1": {
        "disp": disp_role(0.82, 0.93, 1.10, 1.30, 1.65),
        "aim": aim_role(1.02, 1.36, 2.34, 3.48, 4.62),
        "auto": burst_role(1.00, 1.40, 2.38, 3.92),
    },
    "Intermediate545": {
        "disp": disp_role(0.74, 0.88, 1.02, 1.12, 1.40),
        "aim": aim_role(0.90, 1.25, 2.16, 3.22, 4.29),
        "auto": burst_role(1.00, 1.30, 2.10, 3.50),
    },
    "MidRifle": {
        "disp": disp_role(0.90, 0.75, 0.65, 0.88, 1.45),
        "aim": aim_role(1.25, 1.12, 1.62, 2.24, 2.99),
        "auto": burst_role(1.00, 1.15, 1.65, 2.60),
    },
    "Marksman": {
        "disp": disp_role(1.00, 0.82, 0.58, 0.70, 1.20),
        "aim": aim_role(1.50, 1.30, 1.64, 1.91, 2.47),
        "auto": burst_role(1.00, 1.10, 1.45, 2.20),
    },
    "Dmr": {
        "disp": disp_role(1.15, 1.00, 0.70, 0.50, 0.62),
        "aim": aim_role(1.80, 1.60, 1.74, 1.65, 1.84),
        "auto": burst_role(1.00, 1.08, 1.32, 1.90),
    },
    "Support762": {
        "disp": disp_role(1.05, 0.90, 0.74, 0.82, 1.30),
        "aim": aim_role(1.55, 1.35, 1.69, 2.00, 2.59),
        "auto": burst_role(1.00, 1.18, 1.55, 2.50),
    },
    "Support545": {
        "disp": disp_role(1.00, 0.85, 0.66, 0.70, 1.05),
        "aim": aim_role(1.50, 1.28, 1.61, 1.86, 2.37),
        "auto": burst_role(1.00, 1.12, 1.42, 2.20),
    },
}


def weapon(role: str, **stats: float) -> dict:
    curves = ROLE_CURVES[role]
    return {
        "role": role,
        "disp": curves["disp"],
        "aim_curve": curves["aim"],
        "auto": curves["auto"],
        **stats,
    }


WEAPONS: dict[str, dict] = {
    # AK platform
    "Weapon_AK47.asset": weapon(
        "BattleRifle762Default",
        fire_rate=600, aim=0.33, reload=2.45, range_m=95, base_dispersion=1.18,
        recoil=0.54, semi_recoil=0.86, auto_recoil=1.34, recovery=3.2, reliability=0.86,
    ),
    "Weapon_AK47_1.asset": weapon(
        "BattleRifle762WoodHandguard",
        fire_rate=600, aim=0.35, reload=2.45, range_m=105, base_dispersion=1.10,
        recoil=0.50, semi_recoil=0.84, auto_recoil=1.24, recovery=3.6, reliability=0.87,
    ),
    "Weapon_AK47MOD1.asset": weapon(
        "BattleRifle762Mod1",
        fire_rate=600, aim=0.37, reload=2.45, range_m=110, base_dispersion=1.08,
        recoil=0.49, semi_recoil=0.84, auto_recoil=1.20, recovery=3.8, reliability=0.84,
    ),
    "Weapon_AK47S.asset": weapon(
        "CqbControlled",
        fire_rate=600, aim=0.26, reload=2.20, range_m=75, base_dispersion=1.25,
        recoil=0.56, semi_recoil=0.90, auto_recoil=1.45, recovery=3.0, reliability=0.83,
    ),
    "Weapon_AK74.asset": weapon(
        "Intermediate545",
        fire_rate=650, aim=0.30, reload=2.30, range_m=105, base_dispersion=1.00,
        recoil=0.42, semi_recoil=0.84, auto_recoil=1.12, recovery=4.1, reliability=0.86,
    ),
    "Weapon_AK74MOD1.asset": weapon(
        "Intermediate545",
        fire_rate=650, aim=0.33, reload=2.30, range_m=115, base_dispersion=0.96,
        recoil=0.40, semi_recoil=0.83, auto_recoil=1.08, recovery=4.3, reliability=0.84,
    ),
    "Weapon_AK74U.asset": weapon(
        "CqbShort",
        fire_rate=650, aim=0.26, reload=1.95, range_m=55, base_dispersion=1.45,
        recoil=0.58, semi_recoil=0.92, auto_recoil=1.55, recovery=2.8, reliability=0.81,
    ),
    "Weapon_AK74UMOD1.asset": weapon(
        "CqbControlled",
        fire_rate=650, aim=0.28, reload=2.00, range_m=65, base_dispersion=1.35,
        recoil=0.55, semi_recoil=0.90, auto_recoil=1.45, recovery=3.1, reliability=0.80,
    ),
    "Weapon_RPK47.asset": weapon(
        "Support762",
        fire_rate=600, aim=0.44, reload=2.95, range_m=125, base_dispersion=1.02,
        recoil=0.46, semi_recoil=0.86, auto_recoil=1.08, recovery=4.7, reliability=0.88,
    ),
    "Weapon_RPK47MOD1.asset": weapon(
        "Support762",
        fire_rate=600, aim=0.47, reload=2.95, range_m=135, base_dispersion=0.98,
        recoil=0.45, semi_recoil=0.85, auto_recoil=1.04, recovery=5.0, reliability=0.86,
    ),
    "Weapon_RPK74.asset": weapon(
        "Support545",
        fire_rate=650, aim=0.41, reload=2.85, range_m=135, base_dispersion=0.92,
        recoil=0.39, semi_recoil=0.84, auto_recoil=1.00, recovery=5.2, reliability=0.88,
    ),
    "Weapon_RPK74MOD1.asset": weapon(
        "Support545",
        fire_rate=650, aim=0.44, reload=2.85, range_m=145, base_dispersion=0.88,
        recoil=0.38, semi_recoil=0.83, auto_recoil=0.96, recovery=5.5, reliability=0.86,
    ),
    # M4/AR platform
    "Weapon_M4_ModA_1.asset": weapon(
        "CarbineModA1",
        fire_rate=600, aim=0.27, reload=2.15, range_m=95, base_dispersion=0.92,
        recoil=0.48, semi_recoil=0.84, auto_recoil=1.22, recovery=3.7, reliability=0.82,
    ),
    "Weapon_M4_ModA_2.asset": weapon(
        "CarbineModA2",
        fire_rate=600, aim=0.30, reload=2.20, range_m=105, base_dispersion=0.88,
        recoil=0.46, semi_recoil=0.83, auto_recoil=1.15, recovery=4.0, reliability=0.82,
    ),
    "Weapon_M16A_ModA_1.asset": weapon(
        "MidRifle",
        fire_rate=600, aim=0.35, reload=2.30, range_m=125, base_dispersion=0.80,
        recoil=0.46, semi_recoil=0.83, auto_recoil=1.12, recovery=3.9, reliability=0.84,
    ),
    "Weapon_M16A4_ModA_2.asset": weapon(
        "Marksman",
        fire_rate=600, aim=0.39, reload=2.35, range_m=140, base_dispersion=0.72,
        recoil=0.43, semi_recoil=0.82, auto_recoil=1.06, recovery=4.3, reliability=0.84,
    ),
    "Weapon_MK12.asset": weapon(
        "Dmr",
        fire_rate=450, aim=0.50, reload=2.50, range_m=160, base_dispersion=0.56,
        recoil=0.38, semi_recoil=0.80, auto_recoil=1.00, recovery=4.8, reliability=0.86,
    ),
    "Weapon_MK18.asset": weapon(
        "CqbShort",
        fire_rate=700, aim=0.26, reload=1.95, range_m=60, base_dispersion=1.18,
        recoil=0.60, semi_recoil=0.88, auto_recoil=1.50, recovery=3.0, reliability=0.82,
    ),
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


def curve_yaml(keys: list[tuple[float, float]], field_name: str) -> str:
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


def profile_yaml(disp: list[tuple[float, float]], aim: list[tuple[float, float]]) -> str:
    return (
        "  m_DistanceAimProfile:\n"
        + curve_yaml(disp, "m_DispersionMultiplierByDistance")
        + curve_yaml(aim, "m_AimTimeMultiplierByDistance")
    )


def auto_curve_yaml(keys: list[tuple[float, float]]) -> str:
    body = "".join(key_block(t, v) for t, v in keys)
    return (
        "  m_AutoBurstSpreadMultiplierByShot:\n"
        "    serializedVersion: 2\n"
        "    m_Curve:\n"
        f"{body}"
        "    m_PreInfinity: 2\n"
        "    m_PostInfinity: 2\n"
        "    m_RotationOrder: 4\n"
    )


def set_scalar(text: str, prop: str, value: float) -> str:
    pattern = rf"  {re.escape(prop)}: [0-9.]+"
    replacement = f"  {prop}: {value:g}"
    if re.search(pattern, text):
        return re.sub(pattern, replacement, text, count=1)
    return text


def patch_weapon(path: Path, data: dict) -> None:
    text = path.read_text(encoding="utf-8")

    text = re.sub(r"\n  m_DistanceAimProfile:.*?(?=\n  m_[A-Z]|\n  m_Recoil|\Z)", "", text, flags=re.S)
    text = re.sub(r"\n  m_AutoBurstSpreadMultiplierByShot:.*?(?=\n  m_[A-Z]|\n  m_Recoil|\Z)", "", text, flags=re.S)

    text = set_scalar(text, "m_FireRateRpm", data["fire_rate"])
    text = set_scalar(text, "m_AimTimeSeconds", data["aim"])
    text = set_scalar(text, "m_ReloadTimeSeconds", data["reload"])
    text = set_scalar(text, "m_EffectiveRangeMeters", data["range_m"])
    text = set_scalar(text, "m_BaseShotDispersion", data["base_dispersion"])
    text = set_scalar(text, "m_RecoilPerShot", data["recoil"])
    text = set_scalar(text, "m_SemiAutoRecoilMultiplier", data["semi_recoil"])
    text = set_scalar(text, "m_AutoRecoilMultiplier", data["auto_recoil"])
    text = set_scalar(text, "m_RecoilRecoveryPerSecond", data["recovery"])
    text = set_scalar(text, "m_Reliability", data["reliability"])

    block = profile_yaml(data["disp"], data["aim_curve"]) + auto_curve_yaml(data["auto"])
    anchor = "  m_RecoilPerShot:"
    if anchor not in text:
        anchor = "  m_BaseShotDispersion:"
        text = re.sub(
            r"(  m_BaseShotDispersion: [0-9.]+)",
            r"\1\n" + block.rstrip("\n"),
            text,
            count=1,
        )
    else:
        text = text.replace(anchor, block + anchor, 1)

    path.write_text(text, encoding="utf-8", newline="\n")
    print(f"Baked {path.name} [{data['role']}]")


def apply_excel_accuracy_curves() -> None:
    disp_curves_by_weapon: dict[str, list[tuple[float, float]]] = {}
    for rel, data in WEAPONS.items():
        weapon_name = rel.replace(".asset", "")
        disp_curves_by_weapon[weapon_name] = data["disp"]

    reference = build_accuracy_reference(disp_curves_by_weapon, list(DISTANCE_KEYFRAMES))
    for rel, data in WEAPONS.items():
        weapon_name = rel.replace(".asset", "")
        role = WEAPON_ROLE.get(weapon_name, data["role"])
        data["disp"] = build_adjusted_disp_curve(
            weapon_name,
            role,
            data["disp"],
            reference,
        )
        data["aim_curve"] = densify_curve(data["aim_curve"])


def main() -> None:
    apply_excel_accuracy_curves()
    baked = 0
    for rel, data in WEAPONS.items():
        matches = list(SHOOTING.rglob(rel.split("/")[-1]))
        if not matches:
            print(f"Missing {rel}")
            continue
        for match in matches:
            patch_weapon(match, data)
            baked += 1
    print(f"Done: {baked} weapons rebalance baked.")


if __name__ == "__main__":
    main()
