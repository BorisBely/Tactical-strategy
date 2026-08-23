"""Stage 10 source of truth: Accuracy / AimTime distance keys inside 150/300.

Does not write ScopeVisionRange, E, BaseDamage, recoil, burst-by-shot, or Q.
Bake: python Tools/bake_accuracy_aim_curves.py
"""
from __future__ import annotations

from combat_balance_model import WEAPON_ROLE

# Working vision envelopes. 1x / HipFire = 150. Magnified = ScopeVisionRange.
OPTIC_VISION_HIGH: dict[str, float] = {
    "Attachment_M4_Reddot1": 150,
    "Attachment_M4_Reddot3": 150,
    "Attachment_M4_RDC": 150,
    "Attachment_M4_Reddot2": 150,
    "Attachment_AK_Reddot4_Rail": 150,
    "Attachment_M4_Aimpoint": 175,
    "Attachment_M4_EOTech_G33": 200,
    "Attachment_M4_Scope1_3x": 200,
    "Attachment_Mosin_Scope8": 210,
    "Attachment_M4_ACOG_RMR": 210,
    "Attachment_M4_ACOG": 220,
    "Attachment_M4_SUSAT": 220,
    "Attachment_AK_Scope11": 220,
    "Attachment_M4_ELCAN_SpecterDR": 220,
    "Attachment_M4_Vortex_Razor": 250,
    "Attachment_M4_Scope4": 260,
    "Attachment_M4_Scope5": 280,
    "Attachment_M4_Scope9": 300,
}

# Frozen Stage 8 flat AimTimeModifier. Stage 10 must not rewrite these.
OPTIC_AIM_TIME_MODIFIER: dict[str, float] = {
    "Attachment_M4_Reddot1": 0.98,
    "Attachment_M4_Reddot3": 0.98,
    "Attachment_M4_RDC": 0.98,
    "Attachment_M4_Reddot2": 0.98,
    "Attachment_AK_Reddot4_Rail": 0.95,
    "Attachment_M4_Aimpoint": 1.00,
    "Attachment_M4_EOTech_G33": 1.14,
    "Attachment_M4_Scope1_3x": 1.14,
    "Attachment_Mosin_Scope8": 1.22,
    "Attachment_M4_ACOG_RMR": 1.22,
    "Attachment_M4_ACOG": 1.20,
    "Attachment_M4_SUSAT": 1.24,
    "Attachment_AK_Scope11": 1.24,
    "Attachment_M4_ELCAN_SpecterDR": 1.24,
    "Attachment_M4_Vortex_Razor": 1.34,
    "Attachment_M4_Scope4": 1.46,
    "Attachment_M4_Scope5": 1.56,
    "Attachment_M4_Scope9": 1.55,
}

# (disp keys, aim-time keys). Lower = better. Last key <= optic vision.
OPTIC_CURVES: dict[str, tuple[list[tuple[float, float]], list[tuple[float, float]]]] = {
    "Attachment_M4_Reddot1": (
        [(0, 0.91), (75, 0.92), (125, 0.98), (150, 1.00)],
        [(0, 0.96), (75, 0.98), (125, 1.00), (150, 1.02)],
    ),
    "Attachment_M4_Reddot3": (
        [(0, 0.92), (75, 0.91), (125, 0.98), (150, 1.00)],
        [(0, 0.96), (75, 0.98), (125, 1.00), (150, 1.02)],
    ),
    "Attachment_M4_RDC": (
        [(0, 0.92), (75, 0.90), (125, 0.98), (150, 1.00)],
        [(0, 0.96), (75, 0.98), (125, 1.00), (150, 1.02)],
    ),
    "Attachment_M4_Reddot2": (
        [(0, 0.92), (100, 0.93), (150, 0.98)],
        [(0, 0.96), (100, 0.98), (150, 1.01)],
    ),
    "Attachment_AK_Reddot4_Rail": (
        [(0, 1.00), (125, 1.00), (150, 1.01)],
        [(0, 0.90), (75, 0.92), (125, 0.94), (150, 0.96)],
    ),
    "Attachment_M4_Aimpoint": (
        [(0, 0.98), (100, 0.92), (175, 0.90)],
        [(0, 0.98), (100, 1.00), (175, 0.98)],
    ),
    "Attachment_M4_EOTech_G33": (
        [(0, 0.92), (75, 0.93), (100, 0.94), (150, 1.04), (185, 0.92), (200, 0.90)],
        [(0, 1.02), (100, 1.08), (150, 1.12), (185, 1.06), (200, 1.02)],
    ),
    "Attachment_M4_Scope1_3x": (
        [(0, 1.12), (75, 1.06), (100, 0.96), (200, 0.82)],
        [(0, 1.20), (75, 1.12), (100, 1.04), (200, 0.98)],
    ),
    "Attachment_Mosin_Scope8": (
        [(0, 1.16), (80, 1.10), (150, 0.96), (200, 0.84), (210, 0.82)],
        [(0, 1.28), (80, 1.18), (150, 1.08), (200, 0.98), (210, 0.96)],
    ),
    "Attachment_M4_ACOG_RMR": (
        [(0, 0.98), (80, 0.94), (150, 0.88), (200, 0.84), (210, 0.85)],
        [(0, 1.08), (80, 1.04), (150, 1.02), (200, 0.98), (210, 0.99)],
    ),
    "Attachment_M4_ACOG": (
        [(0, 1.12), (100, 1.06), (160, 0.94), (210, 0.84), (220, 0.86)],
        [(0, 1.24), (100, 1.16), (160, 1.08), (210, 0.98), (220, 1.00)],
    ),
    "Attachment_M4_SUSAT": (
        [(0, 1.10), (100, 1.06), (160, 0.96), (210, 0.86), (220, 0.88)],
        [(0, 1.22), (100, 1.14), (160, 1.08), (210, 1.00), (220, 1.02)],
    ),
    "Attachment_AK_Scope11": (
        [(0, 1.06), (100, 1.02), (160, 0.94), (210, 0.84), (220, 0.86)],
        [(0, 1.22), (100, 1.14), (160, 1.06), (210, 0.98), (220, 1.00)],
    ),
    "Attachment_M4_ELCAN_SpecterDR": (
        [(0, 1.04), (50, 0.98), (125, 0.92), (210, 0.86), (220, 0.87)],
        [(0, 1.16), (50, 1.10), (125, 1.04), (210, 0.98), (220, 0.99)],
    ),
    "Attachment_M4_Vortex_Razor": (
        [(0, 1.06), (50, 1.03), (125, 0.96), (200, 0.94), (240, 0.92), (250, 0.93)],
        [(0, 1.22), (50, 1.13), (125, 1.07), (200, 1.03), (240, 1.03), (250, 1.04)],
    ),
    "Attachment_M4_Scope4": (
        [(0, 1.28), (120, 1.16), (200, 1.08), (240, 0.88), (255, 0.86), (260, 0.87)],
        [(0, 1.40), (120, 1.30), (200, 1.24), (240, 1.08), (255, 1.02), (260, 1.03)],
    ),
    "Attachment_M4_Scope5": (
        [(0, 1.34), (150, 1.16), (220, 1.00), (260, 0.86), (280, 0.82)],
        [(0, 1.44), (150, 1.28), (220, 1.12), (260, 1.00), (280, 0.94)],
    ),
    "Attachment_M4_Scope9": (
        [(0, 1.40), (120, 1.22), (200, 1.10), (260, 0.92), (300, 0.86)],
        [(0, 1.39), (120, 1.28), (200, 1.16), (260, 1.02), (300, 0.97)],
    ),
}

ROLE_CURVES: dict[str, tuple[list[tuple[float, float]], list[tuple[float, float]]]] = {
    "CqbShort": (
        [(0, 0.58), (50, 0.66), (100, 0.74), (150, 0.97), (220, 1.52), (300, 2.35)],
        [(0, 0.92), (50, 0.98), (100, 1.05), (150, 1.37), (220, 2.20), (300, 3.19)],
    ),
    "CqbControlled": (
        [(0, 0.62), (50, 0.70), (100, 0.78), (150, 0.97), (220, 1.38), (300, 2.05)],
        [(0, 0.84), (50, 0.94), (100, 1.05), (150, 1.35), (220, 2.06), (300, 2.93)],
    ),
    "ShotgunCqb": (
        [(0, 0.72), (15, 0.95), (25, 1.45), (40, 2.40), (60, 3.90), (100, 6.00), (150, 7.00)],
        [(0, 1.05), (15, 1.18), (25, 1.45), (40, 1.95), (60, 2.80), (100, 4.20), (150, 4.87)],
    ),
    "Carbine": (
        [(0, 0.72), (75, 0.80), (150, 0.90), (220, 1.01), (300, 1.12)],
        [(0, 0.85), (75, 1.03), (150, 1.35), (220, 1.90), (300, 2.60)],
    ),
    "CarbineModA1": (
        [(0, 0.73), (75, 0.81), (150, 0.89), (220, 0.98), (300, 1.08)],
        [(0, 0.87), (75, 1.03), (150, 1.31), (220, 1.84), (300, 2.50)],
    ),
    "CarbineModA2": (
        [(0, 0.75), (75, 0.80), (150, 0.87), (220, 0.96), (300, 1.04)],
        [(0, 0.90), (75, 1.03), (150, 1.29), (220, 1.78), (300, 2.41)],
    ),
    "BattleRifle762": (
        [(0, 0.78), (75, 0.88), (150, 1.00), (220, 1.14), (300, 1.30)],
        [(0, 0.95), (75, 1.19), (150, 1.57), (220, 2.19), (300, 2.95)],
    ),
    "BattleRifle762Default": (
        [(0, 0.80), (75, 0.91), (150, 1.03), (220, 1.16), (300, 1.34)],
        [(0, 0.96), (75, 1.21), (150, 1.61), (220, 2.25), (300, 3.04)],
    ),
    "BattleRifle762WoodHandguard": (
        [(0, 0.79), (75, 0.88), (150, 0.98), (220, 1.08), (300, 1.22)],
        [(0, 0.98), (75, 1.20), (150, 1.55), (220, 2.12), (300, 2.86)],
    ),
    "BattleRifle762Mod1": (
        [(0, 0.82), (75, 0.89), (150, 0.96), (220, 1.06), (300, 1.18)],
        [(0, 1.02), (75, 1.22), (150, 1.56), (220, 2.10), (300, 2.80)],
    ),
    "Intermediate545": (
        [(0, 0.74), (75, 0.82), (150, 0.91), (220, 1.00), (300, 1.06)],
        [(0, 0.90), (75, 1.11), (150, 1.43), (220, 1.98), (300, 2.58)],
    ),
    "MidRifle": (
        [(0, 0.90), (75, 0.80), (150, 0.65), (200, 0.70), (250, 0.82), (300, 1.00)],
        [(0, 1.25), (75, 1.15), (150, 1.12), (200, 1.30), (250, 1.55), (300, 1.90)],
    ),
    "Marksman": (
        [(0, 1.00), (75, 0.88), (150, 0.62), (200, 0.58), (250, 0.60), (300, 0.78)],
        [(0, 1.50), (80, 1.32), (150, 1.28), (200, 1.30), (250, 1.45), (300, 1.70)],
    ),
    "Dmr": (
        [(0, 1.15), (80, 1.05), (150, 0.80), (220, 0.58), (260, 0.50), (300, 0.55)],
        [(0, 1.80), (80, 1.65), (150, 1.60), (220, 1.55), (260, 1.58), (300, 1.70)],
    ),
    "Support762": (
        [(0, 1.05), (80, 0.92), (150, 0.74), (200, 0.76), (250, 0.86), (300, 1.05)],
        [(0, 1.55), (80, 1.38), (150, 1.35), (200, 1.45), (250, 1.70), (300, 2.10)],
    ),
    "Support545": (
        [(0, 1.00), (80, 0.88), (150, 0.66), (200, 0.68), (250, 0.78), (300, 0.95)],
        [(0, 1.50), (80, 1.32), (150, 1.28), (200, 1.38), (250, 1.60), (300, 1.95)],
    ),
    "HeavySupport": (
        [(0, 1.10), (80, 0.90), (150, 0.75), (200, 0.70), (250, 0.78), (300, 0.95)],
        [(0, 0.85), (80, 0.92), (150, 1.05), (200, 1.15), (250, 1.30), (300, 1.50)],
    ),
    "GrenadeSupport": (
        [(0, 1.10), (80, 0.90), (150, 0.75), (200, 0.70), (250, 0.78), (300, 0.95)],
        [(0, 0.85), (80, 0.92), (150, 1.05), (200, 1.15), (250, 1.30), (300, 1.50)],
    ),
}

# Character class for contract tests.
WEAPON_CHARACTER: dict[str, str] = {
    "CqbShort": "CQB",
    "CqbControlled": "CQB",
    "ShotgunCqb": "CQB",
    "Carbine": "Assault",
    "CarbineModA1": "Assault",
    "CarbineModA2": "Assault",
    "BattleRifle762": "Assault",
    "BattleRifle762Default": "Assault",
    "BattleRifle762WoodHandguard": "Assault",
    "BattleRifle762Mod1": "Assault",
    "Intermediate545": "Assault",
    "MidRifle": "Assault",
    "Marksman": "Marksman",
    "Dmr": "Sniper",
    "Support762": "LMG",
    "Support545": "LMG",
    "HeavySupport": "LMG",
    "GrenadeSupport": "LMG",
}


def weapon_curves_for_asset(asset_stem: str) -> tuple[list[tuple[float, float]], list[tuple[float, float]]]:
    role = WEAPON_ROLE[asset_stem]
    return ROLE_CURVES[role]
