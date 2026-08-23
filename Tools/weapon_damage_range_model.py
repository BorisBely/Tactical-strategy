#!/usr/bin/env python3
"""Mirror of WeaponDamageRangeMath. Keep in lockstep with the C# class."""

from __future__ import annotations

DEFAULT_FALLOFF_ZERO_RANGE_MULTIPLIER = 2.0
AMMO_CAP_EPSILON = 0.1
MAX_HITSCAN_ENVELOPE_METERS = 300.0
PROPOSED_OPTIC_EFFECTIVE_RANGE_MODIFIER = 1.0


def resolve_effective_range_meters(
    weapon_range: float,
    attachment_product: float = 1.0,
    ammo_range: float = 0.0,
) -> float:
    effective = max(0.0, float(weapon_range)) * max(0.0, float(attachment_product))
    if ammo_range > AMMO_CAP_EPSILON:
        effective = min(effective, float(ammo_range))
    return effective


def compute_falloff_multiplier(
    distance: float,
    effective_range: float,
    falloff_zero: float = DEFAULT_FALLOFF_ZERO_RANGE_MULTIPLIER,
) -> float:
    if effective_range <= AMMO_CAP_EPSILON:
        return 1.0
    if distance <= effective_range:
        return 1.0
    zero_at = compute_zero_damage_distance(effective_range, falloff_zero)
    if distance >= zero_at:
        return 0.0
    return 1.0 - (distance - effective_range) / (zero_at - effective_range)


def compute_zero_damage_distance(
    effective_range: float,
    falloff_zero: float = DEFAULT_FALLOFF_ZERO_RANGE_MULTIPLIER,
) -> float:
    return max(0.0, float(effective_range)) * max(1.01, float(falloff_zero))
