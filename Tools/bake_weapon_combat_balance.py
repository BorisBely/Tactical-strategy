"""Bake weapon distance curves, auto-burst multipliers and base dispersion into WeaponDefinition assets."""
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SHOOTING = ROOT / "Assets" / "GameData" / "Shooting"

WEAPONS: dict[str, dict] = {
    "Weapon_AK47.asset": {
        "base_dispersion": 1.15,
        "disp": [
            (0, 0.75), (10, 0.82), (20, 0.90), (30, 1.00), (40, 1.12),
            (50, 1.26), (60, 1.42), (70, 1.60), (80, 1.80), (90, 2.02), (100, 2.25),
        ],
        "aim": [
            (0, 0.82), (10, 0.86), (20, 0.92), (30, 1.00), (40, 1.08),
            (50, 1.18), (60, 1.30), (70, 1.44), (80, 1.60), (90, 1.78), (100, 2.00),
        ],
        "auto": [
            (1, 1.00), (2, 1.15), (3, 1.35), (4, 1.65), (5, 2.00),
            (6, 2.35), (7, 2.70), (8, 3.05), (9, 3.40), (10, 3.75),
        ],
    },
    "Weapon_M4_ModA_1.asset": {
        "base_dispersion": 0.90,
        "disp": [
            (0, 0.68), (10, 0.74), (20, 0.82), (30, 0.92), (40, 1.03),
            (50, 1.15), (60, 1.29), (70, 1.44), (80, 1.60), (90, 1.78), (100, 1.98),
        ],
        "aim": [
            (0, 0.78), (10, 0.82), (20, 0.88), (30, 0.94), (40, 1.00),
            (50, 1.06), (60, 1.14), (70, 1.22), (80, 1.30), (90, 1.38), (100, 1.46),
        ],
        "auto": [
            (1, 1.00), (2, 1.10), (3, 1.25), (4, 1.45), (5, 1.68),
            (6, 1.92), (7, 2.16), (8, 2.40), (9, 2.64), (10, 2.88),
        ],
    },
    "Weapon_M4_ModA_2.asset": {
        "base_dispersion": 0.90,
        "disp": [
            (0, 0.68), (10, 0.74), (20, 0.82), (30, 0.92), (40, 1.03),
            (50, 1.15), (60, 1.29), (70, 1.44), (80, 1.60), (90, 1.78), (100, 1.98),
        ],
        "aim": [
            (0, 0.78), (10, 0.82), (20, 0.88), (30, 0.94), (40, 1.00),
            (50, 1.06), (60, 1.14), (70, 1.22), (80, 1.30), (90, 1.38), (100, 1.46),
        ],
        "auto": [
            (1, 1.00), (2, 1.10), (3, 1.25), (4, 1.45), (5, 1.68),
            (6, 1.92), (7, 2.16), (8, 2.40), (9, 2.64), (10, 2.88),
        ],
    },
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


def patch_weapon(path: Path, data: dict) -> None:
    text = path.read_text(encoding="utf-8")

    text = re.sub(r"\n  m_DistanceAimProfile:.*?(?=\n  m_[A-Z]|\n  m_Recoil|\Z)", "", text, flags=re.S)
    text = re.sub(r"\n  m_AutoBurstSpreadMultiplierByShot:.*?(?=\n  m_[A-Z]|\n  m_Recoil|\Z)", "", text, flags=re.S)

    text = re.sub(
        r"  m_BaseShotDispersion: [0-9.]+",
        f"  m_BaseShotDispersion: {data['base_dispersion']:g}",
        text,
        count=1,
    )

    block = profile_yaml(data["disp"], data["aim"]) + auto_curve_yaml(data["auto"])
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
    print(f"Baked {path.name}")


def main() -> None:
    for rel, data in WEAPONS.items():
        matches = list(SHOOTING.rglob(rel.split("/")[-1]))
        if not matches:
            print(f"Missing {rel}")
            continue
        for match in matches:
            patch_weapon(match, data)


if __name__ == "__main__":
    main()
