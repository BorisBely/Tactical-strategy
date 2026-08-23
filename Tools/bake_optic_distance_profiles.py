"""Bake WeaponDistanceAimProfile YAML into optic attachment assets."""
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SHOOTING = ROOT / "Assets" / "GameData" / "Shooting"

# Per-attachment curves — must stay aligned with OpticDistanceCurveLibrary.cs fallbacks.
# Collimators: sweet spot dispersion ~0.90-0.92 (~±10% quality). Long scopes: capped so
# modded carbines do not exceed bare DMR/marksman accuracy at role distances.
CURVES: dict[str, tuple[list[tuple[float, float]], list[tuple[float, float]]]] = {
    "Attachment_M4_Reddot1.asset": (
        [(0, 0.91), (15, 0.92), (25, 0.98), (40, 1.06), (60, 1.12), (100, 1.14)],
        [(0, 0.96), (15, 0.98), (25, 1.00), (40, 1.06), (60, 1.10), (100, 1.12)],
    ),
    "Attachment_M4_Reddot3.asset": (
        [(0, 0.92), (15, 0.91), (25, 0.98), (40, 1.06), (60, 1.12), (100, 1.14)],
        [(0, 0.96), (15, 0.98), (25, 1.00), (40, 1.06), (60, 1.10), (100, 1.12)],
    ),
    "Attachment_M4_RDC.asset": (
        [(0, 0.92), (15, 0.90), (25, 0.98), (40, 1.06), (60, 1.12), (100, 1.14)],
        [(0, 0.96), (15, 0.98), (25, 1.00), (40, 1.06), (60, 1.10), (100, 1.12)],
    ),
    "Attachment_M4_Aimpoint.asset": (
        [(0, 0.98), (20, 0.92), (35, 0.90), (45, 1.00), (60, 1.06), (100, 1.08)],
        [(0, 0.98), (20, 1.00), (35, 0.98), (45, 1.00), (60, 1.04), (100, 1.06)],
    ),
    "Attachment_M4_Reddot2.asset": (
        [(0, 0.92), (20, 0.93), (35, 1.00), (50, 1.06), (70, 1.10), (100, 1.12)],
        [(0, 0.96), (20, 0.98), (35, 1.02), (50, 1.06), (70, 1.08), (100, 1.10)],
    ),
    "Attachment_M4_EOTech_G33.asset": (
        [(0, 0.92), (15, 0.93), (20, 0.94), (35, 1.04), (45, 0.92), (55, 0.90), (75, 0.96), (100, 1.04)],
        [(0, 1.02), (20, 1.08), (35, 1.12), (45, 1.08), (55, 1.02), (75, 1.06), (100, 1.10)],
    ),
    "Attachment_M4_Vortex_Razor.asset": (
        [(0, 1.06), (10, 1.03), (25, 0.96), (40, 0.94), (60, 0.92), (80, 0.98), (100, 1.06)],
        [(0, 1.22), (10, 1.13), (25, 1.07), (40, 1.03), (60, 1.03), (80, 1.09), (100, 1.13)],
    ),
    "Attachment_M4_ELCAN_SpecterDR.asset": (
        [(0, 1.04), (10, 0.98), (25, 0.92), (45, 0.86), (60, 0.88), (80, 0.96), (100, 1.06)],
        [(0, 1.16), (10, 1.10), (25, 1.04), (45, 0.98), (60, 1.00), (80, 1.06), (100, 1.10)],
    ),
    "Attachment_M4_Scope1_3x.asset": (
        [(0, 1.12), (15, 1.06), (20, 0.96), (40, 0.82), (55, 0.88), (75, 1.04), (100, 1.12)],
        [(0, 1.20), (15, 1.12), (20, 1.04), (40, 0.98), (55, 1.02), (75, 1.08), (100, 1.12)],
    ),
    "Attachment_M4_ACOG.asset": (
        [(0, 1.12), (30, 1.02), (40, 0.88), (50, 0.84), (65, 0.90), (85, 0.96), (100, 1.02)],
        [(0, 1.24), (30, 1.12), (40, 1.04), (50, 0.98), (65, 1.00), (85, 1.04), (100, 1.08)],
    ),
    "Attachment_M4_SUSAT.asset": (
        [(0, 1.10), (30, 1.04), (40, 0.90), (50, 0.86), (65, 0.88), (85, 0.94), (100, 1.00)],
        [(0, 1.22), (30, 1.14), (40, 1.06), (50, 1.00), (65, 1.02), (85, 1.04), (100, 1.08)],
    ),
    "Attachment_M4_ACOG_RMR.asset": (
        [(0, 0.98), (20, 0.94), (40, 0.88), (50, 0.84), (65, 0.88), (85, 0.96), (100, 1.02)],
        [(0, 1.08), (20, 1.02), (40, 1.04), (50, 0.98), (65, 1.02), (85, 1.06), (100, 1.08)],
    ),
    "Attachment_Mosin_Scope8.asset": (
        [(0, 1.16), (30, 1.06), (40, 0.90), (50, 0.84), (65, 0.82), (85, 0.88), (100, 0.92)],
        [(0, 1.28), (30, 1.16), (40, 1.06), (50, 0.98), (65, 0.96), (85, 0.98), (100, 1.02)],
    ),
    "Attachment_M4_Scope4.asset": (
        [(0, 1.28), (40, 1.08), (60, 0.88), (70, 0.86), (85, 0.92), (100, 0.94)],
        [(0, 1.40), (40, 1.24), (60, 1.08), (70, 1.02), (85, 1.04), (100, 1.06)],
    ),
    "Attachment_M4_Scope5.asset": (
        [(0, 1.34), (50, 1.06), (70, 0.86), (80, 0.84), (95, 0.90), (100, 0.82)],
        [(0, 1.44), (50, 1.18), (70, 1.04), (80, 0.98), (100, 0.94)],
    ),
    "Attachment_M4_Scope9.asset": (
        [(0, 1.40), (60, 1.06), (80, 0.88), (100, 0.86)],
        [(0, 1.39), (60, 1.12), (80, 1.02), (100, 0.97)],
    ),
    "Attachment_AK_Reddot4_Rail.asset": (
        [(0, 1.00), (25, 1.00), (40, 1.02), (60, 1.06), (100, 1.10)],
        [(0, 0.90), (15, 0.92), (25, 0.94), (40, 1.00), (60, 1.04), (100, 1.08)],
    ),
    "Attachment_AK_Scope11.asset": (
        [(0, 1.06), (35, 0.98), (50, 0.86), (60, 0.84), (75, 0.90), (100, 0.94)],
        [(0, 1.22), (35, 1.10), (50, 1.00), (60, 0.98), (75, 1.00), (100, 1.04)],
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
    raise SystemExit(
        "bake_optic_distance_profiles.py is retired (0–100 keys). "
        "Stage 10 owner: python Tools/bake_accuracy_aim_curves.py"
    )
        matches = list(SHOOTING.rglob(rel.split("/")[-1]))
        if not matches:
            print(f"Missing {rel}")
            continue
        for match in matches:
            patch_file(match, disp, aim)


if __name__ == "__main__":
    main()
