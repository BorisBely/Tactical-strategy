"""Attachment combat modifiers for Excel export — mirrors WeaponDistanceAimEvaluator."""

from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SHOOTING = ROOT / "Assets" / "GameData" / "Shooting"

ATTACHMENT_TYPE_LABELS = {
    0: "Optic",
    1: "Suppressor",
    2: "Compensator",
    3: "FlashHider",
    4: "Foregrip",
    5: "Bipod",
    6: "GL",
    7: "Flashlight",
    8: "Laser",
    9: "Stock",
    10: "RailCover",
}

SLOT_LABELS = {
    0: "Muzzle",
    1: "UnderBarrel",
    2: "Rail",
    3: "Optic",
    4: "Stock",
    5: "SideRail",
}

# OpticDistanceCurveLibrary fallback when asset curves are flat neutral.
OPTIC_LIBRARY_CURVES: dict[str, dict[str, list[tuple[float, float]]]] = {
    "Attachment_M4_Reddot1": {
        "disp": [(0, 0.91), (10, 0.90), (15, 0.92), (25, 1.00), (40, 1.06), (100, 1.10)],
        "aim": [(0, 0.96), (15, 0.98), (25, 1.00), (40, 1.06), (100, 1.08)],
    },
    "Attachment_M4_Reddot3": {
        "disp": [(0, 0.92), (10, 0.91), (15, 0.91), (30, 1.00), (45, 1.06), (100, 1.10)],
        "aim": [(0, 0.96), (15, 0.98), (30, 1.00), (45, 1.06), (100, 1.08)],
    },
    "Attachment_M4_RDC": {
        "disp": [(0, 0.92), (10, 0.90), (20, 0.90), (30, 1.00), (100, 1.06)],
        "aim": [(0, 0.96), (10, 0.98), (20, 1.00), (35, 1.04), (100, 1.06)],
    },
    "Attachment_M4_Aimpoint": {
        "disp": [(0, 0.98), (20, 0.92), (35, 0.90), (45, 1.00), (100, 1.04)],
        "aim": [(0, 0.98), (20, 1.00), (35, 0.98), (45, 1.00), (100, 1.04)],
    },
    "Attachment_M4_Reddot2": {
        "disp": [(0, 0.92), (20, 0.93), (35, 1.00), (100, 1.06)],
        "aim": [(0, 0.96), (20, 0.98), (35, 1.02), (100, 1.06)],
    },
    "Attachment_M4_EOTech_G33": {
        "disp": [(0, 0.92), (20, 0.94), (35, 1.04), (45, 0.92), (55, 0.90), (75, 0.96), (100, 1.04)],
        "aim": [(0, 1.02), (20, 1.08), (35, 1.12), (45, 1.08), (55, 1.02), (75, 1.06), (100, 1.10)],
    },
    "Attachment_M4_Vortex_Razor": {
        "disp": [(0, 1.06), (10, 1.03), (25, 0.96), (40, 0.94), (60, 0.92), (80, 0.98), (100, 1.06)],
        "aim": [(0, 1.22), (10, 1.13), (25, 1.07), (40, 1.03), (60, 1.03), (80, 1.09), (100, 1.13)],
    },
    "Attachment_M4_ELCAN_SpecterDR": {
        "disp": [(0, 1.04), (20, 0.98), (45, 0.86), (60, 0.88), (80, 0.96), (100, 1.06)],
        "aim": [(0, 1.16), (20, 1.10), (45, 0.98), (60, 1.00), (80, 1.06), (100, 1.10)],
    },
    "Attachment_M4_Scope1_3x": {
        "disp": [(0, 1.12), (20, 0.96), (40, 0.82), (55, 0.88), (100, 1.04)],
        "aim": [(0, 1.20), (20, 1.04), (40, 0.98), (55, 1.02), (100, 1.08)],
    },
    "Attachment_M4_ACOG": {
        "disp": [(0, 1.12), (40, 0.88), (50, 0.84), (60, 0.90), (100, 1.02)],
        "aim": [(0, 1.24), (40, 1.04), (50, 0.98), (65, 1.00), (100, 1.06)],
    },
    "Attachment_M4_SUSAT": {
        "disp": [(0, 1.10), (15, 1.04), (40, 0.90), (50, 0.86), (60, 0.88), (100, 1.00)],
        "aim": [(0, 1.22), (13, 1.14), (40, 1.06), (50, 1.00), (55, 1.02), (100, 1.06)],
    },
    "Attachment_M4_ACOG_RMR": {
        "disp": [(0, 0.98), (20, 0.94), (40, 0.88), (50, 0.84), (65, 0.88), (100, 1.02)],
        "aim": [(0, 1.08), (20, 1.02), (40, 1.04), (50, 0.98), (65, 1.02), (100, 1.06)],
    },
    "Attachment_M4_Scope4": {
        "disp": [(0, 1.28), (40, 1.08), (60, 0.88), (70, 0.86), (85, 0.92), (100, 0.94)],
        "aim": [(0, 1.40), (40, 1.24), (60, 1.08), (70, 1.02), (100, 1.06)],
    },
    "Attachment_M4_Scope5": {
        "disp": [(0, 1.34), (50, 1.06), (70, 0.86), (80, 0.84), (95, 0.90), (100, 0.82)],
        "aim": [(0, 1.44), (50, 1.18), (70, 1.04), (80, 0.98), (100, 0.94)],
    },
    "Attachment_M4_Scope9": {
        "disp": [(0, 1.40), (60, 1.06), (80, 0.88), (100, 0.86)],
        "aim": [(0, 1.39), (60, 1.12), (80, 1.02), (100, 0.97)],
    },
    "Attachment_AK_Reddot4_Rail": {
        "disp": [(0, 1.00), (25, 1.00), (60, 1.06), (100, 1.10)],
        "aim": [(0, 0.90), (25, 0.94), (60, 1.04), (100, 1.08)],
    },
    "Attachment_AK_Scope11": {
        "disp": [(0, 1.06), (35, 0.98), (50, 0.86), (60, 0.84), (100, 0.94)],
        "aim": [(0, 1.22), (35, 1.10), (50, 1.00), (60, 0.98), (100, 1.04)],
    },
}

RAIL_LASER_CURVES: dict[str, list[tuple[float, float]]] = {
    "Attachment_M4_Laser2": [(0, 0.90), (15, 0.92), (25, 1.00), (100, 1.00)],
    "Attachment_M4_Laser1": [(0, 0.90), (15, 0.88), (25, 0.92), (35, 1.00), (100, 1.00)],
}

REFERENCE_WEAPONS = {
    "M4": "Weapon_M4_ModA_1",
    "AK": "Weapon_AK74",
}

RECOIL_GRAPH_RECOVERY = 0.45


def lerp_curve(keys: list[tuple[float, float]], x: float) -> float:
    if not keys:
        return 1.0
    if x <= keys[0][0]:
        return keys[0][1]
    if x >= keys[-1][0]:
        return keys[-1][1]
    for i in range(len(keys) - 1):
        x0, y0 = keys[i]
        x1, y1 = keys[i + 1]
        if x0 <= x <= x1:
            t = (x - x0) / (x1 - x0) if x1 != x0 else 0.0
            return y0 + t * (y1 - y0)
    return keys[-1][1]


def parse_curve_block(content: str, field_name: str) -> list[tuple[float, float]]:
    pattern = rf"{re.escape(field_name)}:\n(?:.*\n)*?      m_Curve:\n((?:      - serializedVersion: 3\n(?:        .+\n)+)+)"
    match = re.search(pattern, content)
    if not match:
        return []
    points: list[tuple[float, float]] = []
    for point in re.finditer(r"time: ([0-9.+-]+)\n        value: ([0-9.+-]+)", match.group(1)):
        points.append((float(point.group(1)), float(point.group(2))))
    return sorted(points)


def is_flat_neutral_curve(curve: list[tuple[float, float]]) -> bool:
    if not curve:
        return True
    return all(abs(value - 1.0) < 0.0001 for _, value in curve)


def attachment_label(name: str) -> str:
    label = name.replace("Attachment_M4_", "").replace("Attachment_AK_", "").replace("Attachment_Mosin_", "")
    return label.replace("_", " ")


def attachment_platform(name: str) -> str | None:
    if name.startswith("Attachment_M4_"):
        return "M4"
    if name.startswith("Attachment_AK_"):
        return "AK"
    return None


def parse_attachment(path: Path) -> dict:
    content = path.read_text(encoding="utf-8")

    def get_float(name: str, default: float = 1.0) -> float:
        match = re.search(rf"  {name}: ([0-9.]+)", content)
        return float(match.group(1)) if match else default

    def get_int(name: str, default: int = 0) -> int:
        match = re.search(rf"  {name}: ([0-9]+)", content)
        return int(match.group(1)) if match else default

    name = path.stem
    attachment_type = get_int("m_AttachmentType")
    disp_curve = parse_curve_block(content, "m_DispersionMultiplierByDistance")
    aim_curve = parse_curve_block(content, "m_AimTimeMultiplierByDistance")
    return {
        "name": name,
        "label": attachment_label(name),
        "platform": attachment_platform(name),
        "attachment_type": attachment_type,
        "type_label": ATTACHMENT_TYPE_LABELS.get(attachment_type, str(attachment_type)),
        "required_slot": get_int("m_RequiredSlot"),
        "slot_label": SLOT_LABELS.get(get_int("m_RequiredSlot"), "?"),
        "aim_time_modifier": get_float("m_AimTimeModifier"),
        "recoil_modifier": get_float("m_RecoilModifier"),
        "semi_recoil_modifier": get_float("m_SemiAutoRecoilModifier", 1.0),
        "auto_recoil_modifier": get_float("m_AutomaticRecoilModifier", 1.0),
        "reload_time_modifier": get_float("m_ReloadTimeModifier"),
        "effective_range_modifier": get_float("m_EffectiveRangeModifier"),
        "disp_curve": disp_curve,
        "aim_curve": aim_curve,
        "uses_optic_library": attachment_type == 0
        and is_flat_neutral_curve(disp_curve)
        and is_flat_neutral_curve(aim_curve),
        "uses_rail_library": attachment_type == 8 and is_flat_neutral_curve(disp_curve),
    }


def load_attachments(platform: str | None = None) -> list[dict]:
    attachments: list[dict] = []
    for path in sorted(SHOOTING.rglob("Attachment_*.asset")):
        attachment = parse_attachment(path)
        if platform is not None and attachment["platform"] != platform:
            continue
        attachments.append(attachment)
    return attachments


def _attachment_disp_curve(attachment: dict) -> list[tuple[float, float]]:
    if attachment["uses_rail_library"]:
        return RAIL_LASER_CURVES.get(attachment["name"], [(0, 1.0), (100, 1.0)])
    if attachment["uses_optic_library"]:
        library = OPTIC_LIBRARY_CURVES.get(attachment["name"])
        if library:
            return library["disp"]
    return attachment["disp_curve"] or [(0, 1.0), (100, 1.0)]


def _attachment_aim_curve(attachment: dict) -> list[tuple[float, float]]:
    if attachment["uses_optic_library"]:
        library = OPTIC_LIBRARY_CURVES.get(attachment["name"])
        if library:
            return library["aim"]
    return attachment["aim_curve"] or [(0, 1.0), (100, 1.0)]


def attachment_disp_multiplier(attachment: dict, distance: float) -> float:
    return max(0.01, lerp_curve(_attachment_disp_curve(attachment), distance))


def attachment_aim_distance_multiplier(attachment: dict, distance: float) -> float:
    return max(0.01, lerp_curve(_attachment_aim_curve(attachment), distance))


def attachment_recoil_product(attachment: dict, automatic: bool = True) -> float:
    fire_mode_modifier = attachment["auto_recoil_modifier"] if automatic else attachment["semi_recoil_modifier"]
    return max(0.01, attachment["recoil_modifier"] * fire_mode_modifier)


def combined_disp_multiplier(
    weapon: dict,
    attachments: list[dict],
    distance: float,
    weapon_disp_fn,
) -> float:
    multiplier = max(0.01, weapon_disp_fn(weapon, distance))
    for attachment in attachments:
        multiplier *= attachment_disp_multiplier(attachment, distance)
    return max(0.01, multiplier)


def combined_accuracy_quality(
    weapon: dict,
    attachments: list[dict],
    distance: float,
    weapon_disp_fn,
) -> float:
    return 1.0 / combined_disp_multiplier(weapon, attachments, distance, weapon_disp_fn)


def combined_aim_time_seconds(
    weapon: dict,
    attachments: list[dict],
    distance: float,
    weapon_aim_fn,
) -> float:
    multiplier = max(0.01, weapon_aim_fn(weapon, distance))
    for attachment in attachments:
        multiplier *= max(0.01, attachment["aim_time_modifier"])
        multiplier *= attachment_aim_distance_multiplier(attachment, distance)
    return max(0.01, weapon["aim_base"] * multiplier)


def combined_recoil_control_quality(
    weapon: dict,
    attachments: list[dict],
    shot: int,
    weapon_burst_fn,
) -> float:
    recoil_product = 1.0
    for attachment in attachments:
        recoil_product *= attachment_recoil_product(attachment, automatic=True)

    base = max(0.01, weapon["recoil_per_shot"] * recoil_product)
    recoil_added = base * weapon["auto_recoil_mult"]
    if recoil_added <= 0:
        return 1.0

    fire_interval = 60.0 / max(1.0, weapon["rpm"])
    recovery_per_shot = weapon["recovery"] * RECOIL_GRAPH_RECOVERY * fire_interval
    accumulated = 0.0
    for _ in range(1, shot):
        accumulated += recoil_added
        accumulated = max(0.0, accumulated - recovery_per_shot)

    burden = (1.0 + accumulated) * weapon_burst_fn(weapon, shot)
    return (1.0 / base) / max(0.01, burden)


def describe_sweet_spot(attachment: dict, distances: list[float]) -> str:
    curve = _attachment_disp_curve(attachment)
    if is_flat_neutral_curve(curve):
        return "нейтральный"

    best_distance = min(distances, key=lambda distance: lerp_curve(curve, distance))
    best_value = lerp_curve(curve, best_distance)
    if best_value >= 0.999:
        return "нейтральный"

    window: list[float] = []
    threshold = best_value * 1.03 + 0.01
    for distance in distances:
        if lerp_curve(curve, distance) <= threshold:
            window.append(distance)
    if not window:
        return f"лучше @ {best_distance:.0f} м"

    return f"{window[0]:.0f}–{window[-1]:.0f} м"


def mod_affects_accuracy(attachment: dict) -> bool:
    if not is_flat_neutral_curve(_attachment_disp_curve(attachment)):
        return True
    return attachment["uses_optic_library"] or attachment["uses_rail_library"]


def mod_affects_aim(attachment: dict) -> bool:
    if abs(attachment["aim_time_modifier"] - 1.0) > 0.001:
        return True
    return not is_flat_neutral_curve(_attachment_aim_curve(attachment)) or attachment["uses_optic_library"]


def mod_affects_recoil(attachment: dict) -> bool:
    return (
        abs(attachment["recoil_modifier"] - 1.0) > 0.001
        or abs(attachment["semi_recoil_modifier"] - 1.0) > 0.001
        or abs(attachment["auto_recoil_modifier"] - 1.0) > 0.001
    )
