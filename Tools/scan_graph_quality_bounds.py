"""Scan min/max quality values for Mission Prep graph static Y-axis bounds."""

from __future__ import annotations

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "Tools"))

from combat_attachment_model import (  # noqa: E402
    REFERENCE_WEAPONS,
    attachment_aim_distance_multiplier,
    combined_accuracy_quality,
    combined_recoil_control_quality,
    load_attachments,
)
from export_combat_balance_excel import (  # noqa: E402
    ROLE_AIM,
    ROLE_DISP,
    burst_mult,
    build_accuracy_reference,
    curve_or_role,
    load_weapons,
    recoil_control_quality,
)

DISTANCES = list(range(101))
SHOTS = list(range(1, 13))

# Keep in sync with WeaponDistanceAimProfileGraph.cs Mission Prep constants.
STATIC_BOUNDS = {
    "accuracy": (0.20, 2.00),
    "aim": (0.10, 1.55),
    "recoil": (0.02, 3.25),
}

PADDING_RATIO = 0.08


def weapon_disp(weapon: dict, distance: float) -> float:
    return max(0.01, curve_or_role(weapon, "disp_curve", ROLE_DISP, distance))


def weapon_aim(weapon: dict, distance: float) -> float:
    return max(0.01, curve_or_role(weapon, "aim_curve", ROLE_AIM, distance))


def aim_speed_quality(weapon: dict, attachments: list[dict], distance: float) -> float:
    multiplier = weapon_aim(weapon, distance)
    for attachment in attachments:
        multiplier *= max(0.01, attachment["aim_time_modifier"])
        multiplier *= attachment_aim_distance_multiplier(attachment, distance)
    return 1.0 / max(0.01, multiplier)


class Tracker:
    def __init__(self, label: str) -> None:
        self.label = label
        self.min_value = float("inf")
        self.max_value = float("-inf")
        self.min_at = ""
        self.max_at = ""

    def include(self, value: float, context: str) -> None:
        if value < self.min_value:
            self.min_value = value
            self.min_at = context
        if value > self.max_value:
            self.max_value = value
            self.max_at = context

    def padded(self, ratio: float = 0.08) -> tuple[float, float]:
        span = max(0.01, self.max_value - self.min_value)
        pad = span * ratio
        low = max(0.05, self.min_value - pad)
        high = self.max_value + pad
        return round(low, 3), round(high, 3)


def weapon_platform(name: str) -> str | None:
    upper = name.upper()
    if "M4" in upper or "MK" in upper or "MOSIN" in upper:
        return "M4"
    if "AK" in upper or "RPK" in upper:
        return "AK"
    return None


def attachment_matches_weapon(attachment: dict, weapon: dict) -> bool:
    platform = weapon_platform(weapon["name"])
    attachment_platform = attachment.get("platform")
    if platform and attachment_platform and attachment_platform != platform:
        return False
    return True


def main() -> None:
    weapons = load_weapons()
    build_accuracy_reference(weapons)
    attachments = load_attachments()
    weapons_by_name = {weapon["name"]: weapon for weapon in weapons}

    accuracy = Tracker("accuracy")
    aim = Tracker("aim")
    recoil = Tracker("recoil")

    for weapon in weapons:
        for distance in DISTANCES:
            accuracy.include(1.0 / weapon_disp(weapon, distance), f"{weapon['name']} bare @ {distance}m")
            aim.include(aim_speed_quality(weapon, [], distance), f"{weapon['name']} bare @ {distance}m")
        for shot in SHOTS:
            recoil.include(
                recoil_control_quality(weapon, shot),
                f"{weapon['name']} bare shot {shot}",
            )

    for weapon in weapons:
        for attachment in attachments:
            if not attachment_matches_weapon(attachment, weapon):
                continue
            combo = [attachment]
            for distance in DISTANCES:
                accuracy.include(
                    combined_accuracy_quality(weapon, combo, distance, weapon_disp),
                    f"{weapon['name']} + {attachment['name']} @ {distance}m",
                )
                aim.include(
                    aim_speed_quality(weapon, combo, distance),
                    f"{weapon['name']} + {attachment['name']} @ {distance}m",
                )
            for shot in SHOTS:
                recoil.include(
                    combined_recoil_control_quality(weapon, combo, shot, burst_mult),
                    f"{weapon['name']} + {attachment['name']} shot {shot}",
                )

    for ref_name in REFERENCE_WEAPONS.values():
        weapon = weapons_by_name[ref_name]
        for attachment in attachments:
            if not attachment_matches_weapon(attachment, weapon):
                continue
            combo = [attachment]
            for distance in DISTANCES:
                accuracy.include(
                    combined_accuracy_quality(weapon, combo, distance, weapon_disp),
                    f"preview {ref_name} + {attachment['name']} @ {distance}m",
                )
                aim.include(
                    aim_speed_quality(weapon, combo, distance),
                    f"preview {ref_name} + {attachment['name']} @ {distance}m",
                )

    for tracker in (accuracy, aim, recoil):
        low, high = tracker.padded()
        static_low, static_high = STATIC_BOUNDS[tracker.label]
        fits = tracker.min_value >= static_low and tracker.max_value <= static_high
        print(f"{tracker.label.upper()} raw: {tracker.min_value:.4f} .. {tracker.max_value:.4f}")
        print(f"  min @ {tracker.min_at}")
        print(f"  max @ {tracker.max_at}")
        print(f"  suggested static bounds ({tracker.label}): {low} .. {high}")
        print(f"  configured static bounds: {static_low} .. {static_high} -> {'OK' if fits else 'OUT OF RANGE'}")
        print()

    if any(
        tracker.min_value < STATIC_BOUNDS[tracker.label][0]
        or tracker.max_value > STATIC_BOUNDS[tracker.label][1]
        for tracker in (accuracy, aim, recoil)
    ):
        raise SystemExit(1)


if __name__ == "__main__":
    main()
