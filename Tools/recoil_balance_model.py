"""Stage-1 recoil balance contract — mirrors WeaponRecoilMath / WeaponRecoilBalanceContract.

Used by future Excel export (stage 2). Does not change weapon assets or xlsx.
"""

from __future__ import annotations

import math
import re
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SHOOTING = ROOT / "Assets" / "GameData" / "Shooting"

REFERENCE_WEAPON_ASSET = "Weapon_M4_ModA_1"
ACCUMULATED_SHOT_COUNTS = (3, 5, 8, 10)
PAUSE_RECOVERY_AFTER_SHOT_COUNT = 5
PAUSE_RECOVERY_SECONDS = (0.2, 0.4, 0.8)
EVALUATION_DISTANCE_METERS = 100.0

PATTERN_SMOOTH = 0.35
VERTICAL_VARIATION_MIN = 0.85
VERTICAL_VARIATION_MAX = 1.15
MAX_HORIZONTAL_STEP_SCALE = 1.25
MAX_VERTICAL_STEP_SCALE = 1.20
DEFAULT_MAX_OFFSET_DEGREES = 12.0
PATTERN_FREQ1 = 0.73
PATTERN_WEIGHT1 = 0.65
PATTERN_FREQ2 = 1.31
PATTERN_WEIGHT2 = 0.35
PATTERN_SEED2_SCALE = 1.7
RECOVERY_WHILE_FIRING_FOR_PREDICTION = 0.7

RECOIL_CLASS_PHILOSOPHY: dict[str, str] = {
    "M4_M16": "низкий/средний vertical, низкий horizontal, хороший recovery — легко контролируемая очередь",
    "AK47": "выше vertical и horizontal, слабее recovery — короткие очереди эффективны, длинная уходит",
    "AK74": "между AK-47 и M4 — ниже vertical/horizontal чем AK-47, лучше контроль",
    "M249": "низкий/средний vertical, высокий horizontal — очередь расползается в стороны, не только вверх",
    "PKM": "сильнее M249 — высокий vertical/horizontal, медленнее recovery",
    "DMR_Sniper": "сильный kick одной пули, медленный recovery; очередь не актуальна (semi)",
    "Heavy_Turret": "очень сильный kick, низкий recovery; отдельная стойка, не пехота",
}


@dataclass(frozen=True)
class WeaponRecoilParams:
    name: str
    rpm: float
    vertical_recoil: float
    horizontal_recoil: float
    recovery_per_second: float
    semi_recoil_mult: float
    auto_recoil_mult: float
    pattern_seed: float
    fire_mode: str = "FullAuto"


@dataclass(frozen=True)
class RecoilBalanceMetrics:
    vertical_recoil_degrees: float
    horizontal_recoil_degrees: float
    recovery_per_second: float
    offset_after_3_shots: float
    offset_after_5_shots: float
    offset_after_8_shots: float
    offset_after_10_shots: float
    offset_after_pause_020: float
    offset_after_pause_040: float
    offset_after_pause_080: float

    def displacement_after_5_at_100m(self) -> float:
        return offset_to_displacement_meters(self.offset_after_5_shots, EVALUATION_DISTANCE_METERS)

    def displacement_after_pause_040_at_100m(self) -> float:
        return offset_to_displacement_meters(self.offset_after_pause_040, EVALUATION_DISTANCE_METERS)


def _hash01(seed: float, shot_index: int, channel: int) -> float:
    x = math.sin(seed * 12.9898 + shot_index * 78.233 + channel * 37.719) * 43758.5453
    return x - math.floor(x)


def _evaluate_raw_pattern(seed: float, shot_index: int) -> float:
    return (
        math.sin(seed + shot_index * PATTERN_FREQ1) * PATTERN_WEIGHT1
        + math.sin(seed * PATTERN_SEED2_SCALE + shot_index * PATTERN_FREQ2) * PATTERN_WEIGHT2
    )


def _resolve_fire_mode_multiplier(params: WeaponRecoilParams) -> float:
    if params.fire_mode in ("FullAuto", "Burst", "Auto"):
        return params.auto_recoil_mult
    return params.semi_recoil_mult


def _compute_kick(
    params: WeaponRecoilParams,
    shot_index: int,
    previous_pattern: float,
    impulse_multiplier: float,
) -> tuple[float, float, float]:
    i = max(1, shot_index)
    vertical = max(0.0, params.vertical_recoil)
    horizontal = max(0.0, params.horizontal_recoil)
    vertical_variation = VERTICAL_VARIATION_MIN + (
        VERTICAL_VARIATION_MAX - VERTICAL_VARIATION_MIN
    ) * _hash01(params.pattern_seed, i, 0)
    raw_pattern = _evaluate_raw_pattern(params.pattern_seed, i)
    pattern = previous_pattern + (raw_pattern - previous_pattern) * PATTERN_SMOOTH

    delta_y = vertical * vertical_variation * impulse_multiplier
    delta_x = horizontal * pattern * impulse_multiplier
    max_y = vertical * MAX_VERTICAL_STEP_SCALE * impulse_multiplier
    max_x = horizontal * MAX_HORIZONTAL_STEP_SCALE * impulse_multiplier
    delta_y = min(max(delta_y, 0.0), max_y)
    delta_x = min(max(delta_x, -max_x), max_x)
    return delta_x, delta_y, pattern


def _apply_kick(offset_x: float, offset_y: float, delta_x: float, delta_y: float) -> tuple[float, float]:
    next_x = offset_x + delta_x
    next_y = offset_y + delta_y
    magnitude = math.hypot(next_x, next_y)
    if magnitude > DEFAULT_MAX_OFFSET_DEGREES:
        scale = DEFAULT_MAX_OFFSET_DEGREES / magnitude
        next_x *= scale
        next_y *= scale
    return next_x, next_y


def _recover_towards_zero(offset_x: float, offset_y: float, rate: float, delta_time: float) -> tuple[float, float]:
    if rate <= 0.0 or delta_time <= 0.0:
        return offset_x, offset_y
    magnitude = math.hypot(offset_x, offset_y)
    if magnitude <= 1e-10:
        return 0.0, 0.0
    step = min(magnitude, rate * delta_time)
    scale = (magnitude - step) / magnitude
    return offset_x * scale, offset_y * scale


def predict_offset_after_shots(
    params: WeaponRecoilParams,
    shot_count: int,
    *,
    recovery_while_firing_multiplier: float = RECOVERY_WHILE_FIRING_FOR_PREDICTION,
) -> tuple[float, float]:
    if shot_count <= 0:
        return 0.0, 0.0

    impulse = _resolve_fire_mode_multiplier(params)
    interval = 60.0 / max(1.0, params.rpm)
    recovery_per_shot = max(0.0, params.recovery_per_second) * recovery_while_firing_multiplier * interval

    offset_x = 0.0
    offset_y = 0.0
    pattern = 0.0
    for n in range(1, shot_count + 1):
        delta_x, delta_y, pattern = _compute_kick(params, n, pattern, impulse)
        offset_x, offset_y = _apply_kick(offset_x, offset_y, delta_x, delta_y)
        offset_x, offset_y = _recover_towards_zero(offset_x, offset_y, recovery_per_shot, 1.0)
    return offset_x, offset_y


def predict_offset_magnitude_after_shots(
    params: WeaponRecoilParams,
    shot_count: int,
    *,
    recovery_while_firing_multiplier: float = RECOVERY_WHILE_FIRING_FOR_PREDICTION,
) -> float:
    x, y = predict_offset_after_shots(
        params,
        shot_count,
        recovery_while_firing_multiplier=recovery_while_firing_multiplier,
    )
    return math.hypot(x, y)


def predict_offset_magnitude_after_burst_and_pause(
    params: WeaponRecoilParams,
    burst_shot_count: int,
    pause_seconds: float,
    *,
    recovery_while_firing_multiplier: float = RECOVERY_WHILE_FIRING_FOR_PREDICTION,
    pause_recovery_multiplier: float = 1.0,
) -> float:
    offset_x, offset_y = predict_offset_after_shots(
        params,
        burst_shot_count,
        recovery_while_firing_multiplier=recovery_while_firing_multiplier,
    )
    if pause_seconds <= 0.0:
        return math.hypot(offset_x, offset_y)
    rate = max(0.0, params.recovery_per_second) * max(0.0, pause_recovery_multiplier)
    offset_x, offset_y = _recover_towards_zero(offset_x, offset_y, rate, pause_seconds)
    return math.hypot(offset_x, offset_y)


def offset_to_displacement_meters(offset_magnitude_degrees: float, distance_meters: float) -> float:
    distance = max(0.0, distance_meters)
    angle_radians = max(0.0, offset_magnitude_degrees) * math.pi / 180.0
    return distance * math.tan(angle_radians)


def evaluate_baseline_metrics(params: WeaponRecoilParams) -> RecoilBalanceMetrics:
    return RecoilBalanceMetrics(
        vertical_recoil_degrees=params.vertical_recoil,
        horizontal_recoil_degrees=params.horizontal_recoil,
        recovery_per_second=params.recovery_per_second,
        offset_after_3_shots=predict_offset_magnitude_after_shots(params, 3),
        offset_after_5_shots=predict_offset_magnitude_after_shots(params, 5),
        offset_after_8_shots=predict_offset_magnitude_after_shots(params, 8),
        offset_after_10_shots=predict_offset_magnitude_after_shots(params, 10),
        offset_after_pause_020=predict_offset_magnitude_after_burst_and_pause(
            params, PAUSE_RECOVERY_AFTER_SHOT_COUNT, PAUSE_RECOVERY_SECONDS[0]
        ),
        offset_after_pause_040=predict_offset_magnitude_after_burst_and_pause(
            params, PAUSE_RECOVERY_AFTER_SHOT_COUNT, PAUSE_RECOVERY_SECONDS[1]
        ),
        offset_after_pause_080=predict_offset_magnitude_after_burst_and_pause(
            params, PAUSE_RECOVERY_AFTER_SHOT_COUNT, PAUSE_RECOVERY_SECONDS[2]
        ),
    )


def parse_weapon_asset(path: Path, *, fire_mode: str = "FullAuto") -> WeaponRecoilParams:
    content = path.read_text(encoding="utf-8")

    def get_float(name: str, default: float = 0.0) -> float:
        match = re.search(rf"  {name}: ([0-9.]+)", content)
        return float(match.group(1)) if match else default

    return WeaponRecoilParams(
        name=path.stem,
        rpm=get_float("m_FireRateRpm", 600.0),
        vertical_recoil=get_float("m_VerticalRecoil", 0.0),
        horizontal_recoil=get_float("m_HorizontalRecoil", 0.0),
        recovery_per_second=get_float("m_RecoilRecoveryPerSecond", 0.7),
        semi_recoil_mult=get_float("m_SemiAutoRecoilMultiplier", 0.85),
        auto_recoil_mult=get_float("m_AutoRecoilMultiplier", 1.25),
        pattern_seed=get_float("m_RecoilPatternSeed", 0.0),
        fire_mode=fire_mode,
    )


def load_weapon_params(asset_name: str, *, fire_mode: str = "FullAuto") -> WeaponRecoilParams | None:
    matches = list(SHOOTING.rglob(f"{asset_name}.asset"))
    if not matches:
        return None
    return parse_weapon_asset(matches[0], fire_mode=fire_mode)


def format_metrics_row(label: str, metrics: RecoilBalanceMetrics) -> list[str | float]:
    return [
        label,
        round(metrics.vertical_recoil_degrees, 4),
        round(metrics.horizontal_recoil_degrees, 4),
        round(metrics.recovery_per_second, 3),
        round(metrics.offset_after_3_shots, 4),
        round(metrics.offset_after_5_shots, 4),
        round(metrics.offset_after_8_shots, 4),
        round(metrics.offset_after_10_shots, 4),
        round(metrics.offset_after_pause_020, 4),
        round(metrics.offset_after_pause_040, 4),
        round(metrics.offset_after_pause_080, 4),
        round(metrics.displacement_after_5_at_100m(), 3),
    ]


METRICS_HEADER = [
    "Оружие",
    "Vertical °",
    "Horizontal °",
    "Recovery °/с",
    "|Offset| 3",
    "|Offset| 5",
    "|Offset| 8",
    "|Offset| 10",
    "5+0.2с",
    "5+0.4с",
    "5+0.8с",
    "Δ100м @5",
]
