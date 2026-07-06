"""Shared combat balance model used by Excel export and Unity asset baking."""

from __future__ import annotations

ASSAULT_ACCURACY_FACTORS = {
    "Weapon_AK47": 0.98,
    "Weapon_AK47_1": 0.99,
    "Weapon_AK47MOD1": 1.00,
    "Weapon_AK47S": 0.98,
    "Weapon_AK74": 1.00,
    "Weapon_AK74MOD1": 1.01,
    "Weapon_AK74U": 0.98,
    "Weapon_AK74UMOD1": 0.99,
    "Weapon_M4_ModA_1": 1.02,
    "Weapon_M4_ModA_2": 1.03,
    "Weapon_M16A_ModA_1": 1.04,
    "Weapon_MK18": 0.98,
}

ACCURACY_FACTORS = {
    **ASSAULT_ACCURACY_FACTORS,
    "Weapon_RPK47": 0.74,
    "Weapon_RPK47MOD1": 0.76,
    "Weapon_RPK74": 0.80,
    "Weapon_RPK74MOD1": 0.82,
    "Weapon_M16A4_ModA_2": 1.13,
    "Weapon_MK12": 1.16,
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

ACCURACY_PROFILE_PLATEAUS = {
    "CqbShort": (0, 0, 25, 45, 0.055),
    "CqbControlled": (0, 5, 35, 55, 0.045),
    "BattleRifle762Default": (5, 15, 45, 65, 0.035),
    "BattleRifle762WoodHandguard": (10, 20, 55, 75, 0.040),
    "BattleRifle762Mod1": (15, 25, 60, 80, 0.045),
    "Intermediate545": (10, 20, 55, 75, 0.040),
    "CarbineModA1": (15, 25, 60, 80, 0.045),
    "CarbineModA2": (20, 30, 65, 85, 0.050),
    "MidRifle": (25, 35, 75, 95, 0.050),
    "Marksman": (35, 45, 85, 100, 0.045),
    "Dmr": (45, 60, 100, 100, 0.040),
    "Support762": (25, 35, 75, 95, 0.045),
    "Support545": (30, 40, 80, 100, 0.045),
}

DISTANCE_KEYFRAMES = tuple(range(0, 101, 10))
EXCEL_DISTANCES = list(DISTANCE_KEYFRAMES)


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


def profile_plateau_factor(role: str, distance: float) -> float:
    plateau = ACCURACY_PROFILE_PLATEAUS.get(role)
    if plateau is None:
        return 1.0

    fade_in_start, plateau_start, plateau_end, fade_out_end, bonus = plateau
    if distance < fade_in_start or distance > fade_out_end:
        return 1.0
    if plateau_start <= distance <= plateau_end:
        return 1.0 + bonus
    if distance < plateau_start:
        span = max(0.01, plateau_start - fade_in_start)
        return 1.0 + bonus * ((distance - fade_in_start) / span)

    span = max(0.01, fade_out_end - plateau_end)
    return 1.0 + bonus * (1.0 - ((distance - plateau_end) / span))


def accuracy_quality_from_disp_curve(disp_curve: list[tuple[float, float]], distance: float) -> float:
    mult = max(0.01, lerp_curve(disp_curve, distance))
    return 1.0 / mult


def build_accuracy_reference(disp_curves_by_weapon: dict[str, list[tuple[float, float]]], distances: list[float]) -> dict[float, float]:
    assault_names = [name for name in disp_curves_by_weapon if name in ASSAULT_ACCURACY_FACTORS]
    if not assault_names:
        return {}

    reference: dict[float, float] = {}
    for distance in distances:
        values = [
            accuracy_quality_from_disp_curve(disp_curves_by_weapon[name], distance)
            for name in assault_names
        ]
        reference[distance] = sum(values) / len(values)
    return reference


def adjusted_disp_multiplier(
    weapon_name: str,
    role: str,
    disp_curve: list[tuple[float, float]],
    distance: float,
    reference_by_distance: dict[float, float],
) -> float:
    reference = reference_by_distance.get(distance)
    if reference is None:
        reference = accuracy_quality_from_disp_curve(disp_curve, distance)

    factor = ACCURACY_FACTORS.get(weapon_name, 1.0)
    plateau = profile_plateau_factor(role, distance)
    adjusted_quality = reference * factor * plateau
    return 1.0 / max(0.01, adjusted_quality)


def densify_curve(
    keys: list[tuple[float, float]],
    distances: tuple[float, ...] | None = None,
) -> list[tuple[float, float]]:
    if not keys:
        return []

    sample_distances = distances or DISTANCE_KEYFRAMES
    start = keys[0][0]
    end = keys[-1][0]
    return [
        (float(distance), round(lerp_curve(keys, distance), 4))
        for distance in sample_distances
        if start <= distance <= end
    ]


def build_adjusted_disp_curve(
    weapon_name: str,
    role: str,
    base_disp_curve: list[tuple[float, float]],
    reference_by_distance: dict[float, float],
    distances: tuple[float, ...] = DISTANCE_KEYFRAMES,
) -> list[tuple[float, float]]:
    return [
        (
            distance,
            round(
                adjusted_disp_multiplier(weapon_name, role, base_disp_curve, distance, reference_by_distance),
                4,
            ),
        )
        for distance in distances
    ]
