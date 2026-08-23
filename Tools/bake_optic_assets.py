#!/usr/bin/env python3
"""Write OpticDistanceCurveLibrary keyframes into WeaponAttachmentDefinition .asset YAML files."""

from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1] / "Assets" / "GameData" / "Shooting"

CURVES: dict[str, tuple[list[tuple[float, float]], list[tuple[float, float]]]] = {
    "Attachment_M4_Reddot1": (
        [(0, 0.72), (15, 0.76), (25, 0.88), (40, 1.08), (60, 1.28), (100, 1.48)],
        [(0, 0.86), (15, 0.92), (25, 1.02), (40, 1.15), (60, 1.32), (100, 1.50)],
    ),
    "Attachment_M4_Reddot3": (
        [(0, 0.71), (15, 0.75), (25, 0.90), (40, 1.10), (60, 1.30), (100, 1.50)],
        [(0, 0.87), (15, 0.93), (25, 1.03), (40, 1.16), (60, 1.34), (100, 1.52)],
    ),
    "Attachment_M4_RDC": (
        [(0, 0.70), (15, 0.74), (25, 0.92), (40, 1.12), (60, 1.32), (100, 1.52)],
        [(0, 0.88), (15, 0.94), (25, 1.05), (40, 1.18), (60, 1.35), (100, 1.54)],
    ),
    "Attachment_M4_Aimpoint": (
        [(0, 0.73), (15, 0.77), (25, 0.86), (40, 1.06), (60, 1.26), (100, 1.45)],
        [(0, 0.84), (15, 0.90), (25, 1.00), (40, 1.13), (60, 1.30), (100, 1.48)],
    ),
    "Attachment_M4_Reddot2": (
        [(0, 0.74), (20, 0.78), (35, 0.98), (50, 1.12), (70, 1.28), (100, 1.42)],
        [(0, 0.88), (20, 0.94), (35, 1.04), (50, 1.14), (70, 1.26), (100, 1.38)],
    ),
    "Attachment_M4_EOTech_G33": (
        [(0, 0.80), (15, 0.82), (20, 0.86), (35, 1.12), (45, 0.78), (60, 0.74), (75, 0.88), (100, 1.05)],
        [(0, 0.98), (20, 1.06), (35, 1.20), (45, 1.14), (60, 1.10), (75, 1.18), (100, 1.28)],
    ),
    "Attachment_M4_Vortex_Razor": (
        [(0, 1.10), (10, 0.94), (25, 0.82), (40, 0.74), (60, 0.76), (80, 0.90), (100, 1.06)],
        [(0, 1.30), (10, 1.16), (25, 1.04), (40, 1.00), (60, 1.02), (80, 1.12), (100, 1.22)],
    ),
    "Attachment_M4_ELCAN_SpecterDR": (
        [(0, 1.06), (10, 0.92), (25, 0.80), (45, 0.72), (60, 0.74), (80, 0.94), (100, 1.10)],
        [(0, 1.24), (10, 1.10), (25, 0.98), (45, 0.92), (60, 0.96), (80, 1.06), (100, 1.16)],
    ),
    "Attachment_M4_Scope1_3x": (
        [(0, 1.20), (15, 1.10), (20, 0.86), (40, 0.74), (55, 0.94), (75, 1.12), (100, 1.28)],
        [(0, 1.32), (15, 1.20), (20, 1.06), (40, 0.94), (55, 1.04), (75, 1.16), (100, 1.28)],
    ),
    "Attachment_M4_ACOG": (
        [(0, 1.24), (30, 1.06), (40, 0.78), (50, 0.72), (65, 0.80), (85, 0.92), (100, 1.02)],
        [(0, 1.36), (30, 1.16), (40, 1.04), (50, 0.96), (65, 1.00), (85, 1.06), (100, 1.12)],
    ),
    "Attachment_M4_SUSAT": (
        [(0, 1.26), (30, 1.08), (40, 0.80), (50, 0.74), (65, 0.78), (85, 0.90), (100, 1.00)],
        [(0, 1.38), (30, 1.18), (40, 1.06), (50, 0.98), (65, 1.02), (85, 1.04), (100, 1.10)],
    ),
    "Attachment_M4_ACOG_RMR": (
        [(0, 0.92), (20, 0.88), (40, 0.80), (50, 0.76), (65, 0.82), (85, 0.94), (100, 1.04)],
        [(0, 1.10), (20, 1.02), (40, 1.08), (50, 1.00), (65, 1.04), (85, 1.08), (100, 1.14)],
    ),
    "Attachment_Mosin_Scope8": (
        [(0, 1.30), (30, 1.12), (40, 0.82), (50, 0.76), (65, 0.74), (85, 0.78), (100, 0.86)],
        [(0, 1.42), (30, 1.22), (40, 1.10), (50, 1.02), (65, 0.96), (85, 0.98), (100, 1.04)],
    ),
    "Attachment_M4_Scope4": (
        [(0, 1.42), (40, 1.18), (60, 0.76), (70, 0.72), (85, 0.80), (100, 0.86)],
        [(0, 1.52), (40, 1.30), (60, 1.06), (70, 1.00), (85, 1.04), (100, 1.08)],
    ),
    "Attachment_M4_Scope5": (
        [(0, 1.50), (50, 1.12), (70, 0.74), (80, 0.70), (95, 0.68), (100, 0.66)],
        [(0, 1.58), (50, 1.24), (70, 1.00), (80, 0.94), (100, 0.90)],
    ),
    "Attachment_M4_Scope9": (
        [(0, 1.60), (60, 1.08), (80, 0.66), (100, 0.58)],
        [(0, 1.65), (60, 1.20), (80, 0.90), (100, 0.82)],
    ),
    "Attachment_AK_Reddot4_Rail": (
        [(0, 0.76), (15, 0.80), (25, 0.94), (40, 1.12), (60, 1.32), (100, 1.52)],
        [(0, 0.92), (15, 0.98), (25, 1.08), (40, 1.20), (60, 1.37), (100, 1.54)],
    ),
    "Attachment_AK_Scope11": (
        [(0, 1.14), (35, 1.00), (50, 0.78), (60, 0.76), (75, 0.80), (100, 0.86)],
        [(0, 1.32), (35, 1.14), (50, 1.00), (60, 0.96), (75, 1.00), (100, 1.08)],
    ),
}


def render_keyframes(keyframes: list[tuple[float, float]]) -> str:
    lines: list[str] = []
    for time, value in keyframes:
        lines.extend(
            [
                "      - serializedVersion: 3",
                f"        time: {time:g}",
                f"        value: {value:g}",
                "        inSlope: 0",
                "        outSlope: 0",
                "        tangentMode: 0",
                "        weightedMode: 0",
                "        inWeight: 0",
                "        outWeight: 0",
            ]
        )
    return "\n".join(lines)


def render_curve_block(property_name: str, keyframes: list[tuple[float, float]]) -> str:
    return (
        f"    {property_name}:\n"
        f"      serializedVersion: 2\n"
        f"      m_Curve:\n"
        f"{render_keyframes(keyframes)}\n"
        f"      m_PreInfinity: 2\n"
        f"      m_PostInfinity: 2\n"
        f"      m_RotationOrder: 4"
    )


def render_profile(disp: list[tuple[float, float]], aim: list[tuple[float, float]]) -> str:
    return (
        "  m_DistanceAimProfile:\n"
        + render_curve_block("m_DispersionMultiplierByDistance", disp)
        + "\n"
        + render_curve_block("m_AimTimeMultiplierByDistance", aim)
    )


def patch_asset(path: Path, disp: list[tuple[float, float]], aim: list[tuple[float, float]]) -> None:
    text = path.read_text(encoding="utf-8")
    pattern = re.compile(
        r"  m_DistanceAimProfile:\n"
        r"    m_DispersionMultiplierByDistance:\n"
        r"      serializedVersion: 2\n"
        r"      m_Curve:\n"
        r"(?:      - serializedVersion: 3\n(?:        .+\n)+)+"
        r"      m_PreInfinity: 2\n"
        r"      m_PostInfinity: 2\n"
        r"      m_RotationOrder: 4\n"
        r"    m_AimTimeMultiplierByDistance:\n"
        r"      serializedVersion: 2\n"
        r"      m_Curve:\n"
        r"(?:      - serializedVersion: 3\n(?:        .+\n)+)+"
        r"      m_PreInfinity: 2\n"
        r"      m_PostInfinity: 2\n"
        r"      m_RotationOrder: 4",
        re.MULTILINE,
    )
    replacement = render_profile(disp, aim)
    new_text, count = pattern.subn(replacement, text, count=1)
    if count != 1:
        raise RuntimeError(f"Failed to patch profile block in {path}")
    path.write_text(new_text, encoding="utf-8", newline="\n")


def main() -> None:
    raise SystemExit(
        "bake_optic_assets.py is retired (0–100 keys). "
        "Stage 10 owner: python Tools/bake_accuracy_aim_curves.py"
    )
    for asset_name, (disp, aim) in CURVES.items():
        matches = list(ROOT.rglob(f"{asset_name}.asset"))
        if not matches:
            raise FileNotFoundError(asset_name)
        for path in matches:
            patch_asset(path, disp, aim)
            patched += 1
            print(f"Patched {path.relative_to(ROOT.parents[1])}")
    print(f"Done. Patched {patched} optic assets.")


if __name__ == "__main__":
    main()
