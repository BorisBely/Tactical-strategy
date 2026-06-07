"""Bake WeaponDistanceAimProfile YAML into optic attachment assets."""
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SHOOTING = ROOT / "Assets" / "GameData" / "Shooting"

# Per-attachment curves — must match OpticDistanceCurveLibrary.cs
CURVES: dict[str, tuple[list[tuple[float, float]], list[tuple[float, float]]]] = {
    "Attachment_M4_Reddot1.asset": (
        [(0, 0.72), (15, 0.76), (25, 0.88), (40, 1.08), (60, 1.28), (100, 1.48)],
        [(0, 0.86), (15, 0.92), (25, 1.02), (40, 1.15), (60, 1.32), (100, 1.50)],
    ),
    "Attachment_M4_Reddot3.asset": (
        [(0, 0.71), (15, 0.75), (25, 0.90), (40, 1.10), (60, 1.30), (100, 1.50)],
        [(0, 0.87), (15, 0.93), (25, 1.03), (40, 1.16), (60, 1.34), (100, 1.52)],
    ),
    "Attachment_M4_RDC.asset": (
        [(0, 0.70), (15, 0.74), (25, 0.92), (40, 1.12), (60, 1.32), (100, 1.52)],
        [(0, 0.88), (15, 0.94), (25, 1.05), (40, 1.18), (60, 1.35), (100, 1.54)],
    ),
    "Attachment_M4_Aimpoint.asset": (
        [(0, 0.73), (15, 0.77), (25, 0.86), (40, 1.06), (60, 1.26), (100, 1.45)],
        [(0, 0.84), (15, 0.90), (25, 1.00), (40, 1.13), (60, 1.30), (100, 1.48)],
    ),
    "Attachment_M4_Reddot2.asset": (
        [(0, 0.74), (20, 0.78), (35, 0.98), (50, 1.12), (70, 1.28), (100, 1.42)],
        [(0, 0.88), (20, 0.94), (35, 1.04), (50, 1.14), (70, 1.26), (100, 1.38)],
    ),
    "Attachment_M4_EOTech_G33.asset": (
        [(0, 0.80), (15, 0.82), (20, 0.86), (35, 1.12), (45, 0.78), (60, 0.74), (75, 0.88), (100, 1.05)],
        [(0, 0.98), (20, 1.06), (35, 1.20), (45, 1.14), (60, 1.10), (75, 1.18), (100, 1.28)],
    ),
    "Attachment_M4_Vortex_Razor.asset": (
        [(0, 1.10), (10, 0.94), (25, 0.82), (40, 0.74), (60, 0.76), (80, 0.90), (100, 1.06)],
        [(0, 1.30), (10, 1.16), (25, 1.04), (40, 1.00), (60, 1.02), (80, 1.12), (100, 1.22)],
    ),
    "Attachment_M4_ELCAN_SpecterDR.asset": (
        [(0, 1.06), (10, 0.92), (25, 0.80), (45, 0.72), (60, 0.74), (80, 0.94), (100, 1.10)],
        [(0, 1.24), (10, 1.10), (25, 0.98), (45, 0.92), (60, 0.96), (80, 1.06), (100, 1.16)],
    ),
    "Attachment_M4_Scope1_3x.asset": (
        [(0, 1.20), (15, 1.10), (20, 0.86), (40, 0.74), (55, 0.94), (75, 1.12), (100, 1.28)],
        [(0, 1.32), (15, 1.20), (20, 1.06), (40, 0.94), (55, 1.04), (75, 1.16), (100, 1.28)],
    ),
    "Attachment_M4_ACOG.asset": (
        [(0, 1.24), (30, 1.06), (40, 0.78), (50, 0.72), (65, 0.80), (85, 0.92), (100, 1.02)],
        [(0, 1.36), (30, 1.16), (40, 1.04), (50, 0.96), (65, 1.00), (85, 1.06), (100, 1.12)],
    ),
    "Attachment_M4_SUSAT.asset": (
        [(0, 1.26), (30, 1.08), (40, 0.80), (50, 0.74), (65, 0.78), (85, 0.90), (100, 1.00)],
        [(0, 1.38), (30, 1.18), (40, 1.06), (50, 0.98), (65, 1.02), (85, 1.04), (100, 1.10)],
    ),
    "Attachment_M4_ACOG_RMR.asset": (
        [(0, 0.92), (20, 0.88), (40, 0.80), (50, 0.76), (65, 0.82), (85, 0.94), (100, 1.04)],
        [(0, 1.10), (20, 1.02), (40, 1.08), (50, 1.00), (65, 1.04), (85, 1.08), (100, 1.14)],
    ),
    "Attachment_Mosin_Scope8.asset": (
        [(0, 1.30), (30, 1.12), (40, 0.82), (50, 0.76), (65, 0.74), (85, 0.78), (100, 0.86)],
        [(0, 1.42), (30, 1.22), (40, 1.10), (50, 1.02), (65, 0.96), (85, 0.98), (100, 1.04)],
    ),
    "Attachment_M4_Scope4.asset": (
        [(0, 1.42), (40, 1.18), (60, 0.76), (70, 0.72), (85, 0.80), (100, 0.86)],
        [(0, 1.52), (40, 1.30), (60, 1.06), (70, 1.00), (85, 1.04), (100, 1.08)],
    ),
    "Attachment_M4_Scope5.asset": (
        [(0, 1.50), (50, 1.12), (70, 0.74), (80, 0.70), (95, 0.68), (100, 0.66)],
        [(0, 1.58), (50, 1.24), (70, 1.00), (80, 0.94), (100, 0.90)],
    ),
    "Attachment_M4_Scope9.asset": (
        [(0, 1.60), (60, 1.08), (80, 0.66), (100, 0.58)],
        [(0, 1.65), (60, 1.20), (80, 0.90), (100, 0.82)],
    ),
    "Attachment_AK_Reddot4_Rail.asset": (
        [(0, 0.76), (15, 0.80), (25, 0.94), (40, 1.12), (60, 1.32), (100, 1.52)],
        [(0, 0.92), (15, 0.98), (25, 1.08), (40, 1.20), (60, 1.37), (100, 1.54)],
    ),
    "Attachment_AK_Scope11.asset": (
        [(0, 1.14), (35, 1.00), (50, 0.78), (60, 0.76), (75, 0.80), (100, 0.86)],
        [(0, 1.32), (35, 1.14), (50, 1.00), (60, 0.96), (75, 1.00), (100, 1.08)],
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


def aim_curve_yaml(keys: list[tuple[float, float]]) -> str:
    body = "".join(key_block(t, v) for t, v in keys)
    return (
        "    m_AimTimeMultiplierByDistance:\n"
        "      serializedVersion: 2\n"
        "      m_Curve:\n"
        f"{body}"
        "      m_PreInfinity: 2\n"
        "      m_PostInfinity: 2\n"
        "      m_RotationOrder: 4\n"
    )


def patch_file(path: Path, disp: list[tuple[float, float]], aim: list[tuple[float, float]]) -> None:
    text = path.read_text(encoding="utf-8")
    if "m_AttachmentType: 0" not in text:
        return

    text = re.sub(r"\n  m_DistanceAimProfile:.*?(?=\n  m_[A-Z]|\n  m_Suppressed|\Z)", "", text, flags=re.S)
    text = re.sub(r"\n  m_Optic[A-Za-z]+:.*", "", text)

    block = (
        "  m_DistanceAimProfile:\n"
        + curve_yaml(disp)
        + aim_curve_yaml(aim)
    )

    anchor = "  m_ReloadTimeModifier:"
    if anchor not in text:
        anchor = "  m_JamRiskModifier:"
    text = text.replace(anchor, block + anchor, 1)

    path.write_text(text, encoding="utf-8", newline="\n")
    print(f"Baked {path.name}")


def main() -> None:
    for rel, (disp, aim) in CURVES.items():
        matches = list(SHOOTING.rglob(rel.split("/")[-1]))
        if not matches:
            print(f"Missing {rel}")
            continue
        for match in matches:
            patch_file(match, disp, aim)


if __name__ == "__main__":
    main()
