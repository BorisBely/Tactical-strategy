#!/usr/bin/env python3
"""Stage 9 weapon/ammo range proposal catalog. Does not bake combat assets."""

from __future__ import annotations

import csv
import re
import sys
from dataclasses import dataclass
from pathlib import Path

from weapon_damage_range_model import (
    MAX_HITSCAN_ENVELOPE_METERS,
    PROPOSED_OPTIC_EFFECTIVE_RANGE_MODIFIER,
    compute_falloff_multiplier,
    resolve_effective_range_meters,
)

ROOT = Path(__file__).resolve().parents[1]
TOOLS = Path(__file__).resolve().parent
WEAPON_CSV = TOOLS / "weapon_range_catalog.csv"
AMMO_CSV = TOOLS / "ammo_range_catalog.csv"
SHOOTING = ROOT / "Assets" / "GameData" / "Shooting"

CATEGORIES = ("RegularHitscan", "ShotgunCurve", "HeavyHitscan", "ProjectileSupport")
LINEAR_CATEGORIES = ("RegularHitscan", "HeavyHitscan")
EXPECTED_WEAPON_COUNT = 26
EXPECTED_AMMO_COUNT = 8


@dataclass(frozen=True)
class WeaponRangeRow:
    weapon: str
    asset_path: str
    ammo: str
    caliber: str
    role: str
    damage_model: str
    category: str
    compact_hint: float | None
    current_weapon_range: float
    proposed_weapon_range: float
    engagement_edge: float
    min_multiplier_at_edge: float | None
    notes: str

    @property
    def abs_asset_path(self) -> Path:
        return ROOT / self.asset_path.replace("\\", "/")


@dataclass(frozen=True)
class AmmoRangeRow:
    ammo: str
    asset_path: str
    caliber: str
    damage_model: str
    current_range: float
    proposed_range: float
    notes: str

    @property
    def abs_asset_path(self) -> Path:
        return ROOT / self.asset_path.replace("\\", "/")


def _optional_float(raw: str | None) -> float | None:
    text = (raw or "").strip()
    if not text:
        return None
    return float(text)


def load_weapon_catalog(path: Path | None = None) -> list[WeaponRangeRow]:
    csv_path = path or WEAPON_CSV
    rows: list[WeaponRangeRow] = []
    with csv_path.open(encoding="utf-8", newline="") as handle:
        reader = csv.DictReader(
            (line for line in handle if line.strip() and not line.lstrip().startswith("#"))
        )
        for raw in reader:
            rows.append(
                WeaponRangeRow(
                    weapon=raw["weapon"].strip(),
                    asset_path=raw["asset_path"].strip(),
                    ammo=raw["ammo"].strip(),
                    caliber=raw["caliber"].strip(),
                    role=raw["role"].strip(),
                    damage_model=raw["damage_model"].strip(),
                    category=raw["category"].strip(),
                    compact_hint=_optional_float(raw.get("compact_hint")),
                    current_weapon_range=float(raw["current_weapon_range"]),
                    proposed_weapon_range=float(raw["proposed_weapon_range"]),
                    engagement_edge=float(raw["engagement_edge"]),
                    min_multiplier_at_edge=_optional_float(raw.get("min_multiplier_at_edge")),
                    notes=(raw.get("notes") or "").strip(),
                )
            )
    if not rows:
        raise RuntimeError(f"Empty weapon range catalog: {csv_path}")
    return rows


def load_ammo_catalog(path: Path | None = None) -> list[AmmoRangeRow]:
    csv_path = path or AMMO_CSV
    rows: list[AmmoRangeRow] = []
    with csv_path.open(encoding="utf-8", newline="") as handle:
        reader = csv.DictReader(
            (line for line in handle if line.strip() and not line.lstrip().startswith("#"))
        )
        for raw in reader:
            rows.append(
                AmmoRangeRow(
                    ammo=raw["ammo"].strip(),
                    asset_path=raw["asset_path"].strip(),
                    caliber=raw["caliber"].strip(),
                    damage_model=raw["damage_model"].strip(),
                    current_range=float(raw["current_range"]),
                    proposed_range=float(raw["proposed_range"]),
                    notes=(raw.get("notes") or "").strip(),
                )
            )
    if not rows:
        raise RuntimeError(f"Empty ammo range catalog: {csv_path}")
    return rows


def read_yaml_float(path: Path, field_name: str) -> float:
    match = re.search(rf"^  {re.escape(field_name)}: ([0-9.+-]+)\s*$", path.read_text(encoding="utf-8"), re.M)
    if not match:
        raise RuntimeError(f"{path}: missing {field_name}")
    return float(match.group(1))


def validate_catalogs(
    weapons: list[WeaponRangeRow] | None = None,
    ammo: list[AmmoRangeRow] | None = None,
    live_must_match: str | None = "proposed",
) -> list[str]:
    weapon_rows = weapons if weapons is not None else load_weapon_catalog()
    ammo_rows = ammo if ammo is not None else load_ammo_catalog()
    errors: list[str] = []

    if len(weapon_rows) != EXPECTED_WEAPON_COUNT:
        errors.append(f"expected {EXPECTED_WEAPON_COUNT} weapons, got {len(weapon_rows)}")
    if len(ammo_rows) != EXPECTED_AMMO_COUNT:
        errors.append(f"expected {EXPECTED_AMMO_COUNT} ammo, got {len(ammo_rows)}")

    ammo_by_name = {row.ammo: row for row in ammo_rows}
    if len(ammo_by_name) != len(ammo_rows):
        errors.append("duplicate ammo catalog names")

    caliber_ammo: dict[str, str] = {}
    for row in ammo_rows:
        if row.caliber in caliber_ammo:
            errors.append(f"caliber {row.caliber} maps to both {caliber_ammo[row.caliber]} and {row.ammo}")
        caliber_ammo[row.caliber] = row.ammo
        if not row.abs_asset_path.is_file():
            errors.append(f"missing ammo asset {row.asset_path}")
        else:
            live = read_yaml_float(row.abs_asset_path, "m_EffectiveRangeMeters")
            if live_must_match == "current" and abs(live - row.current_range) > 0.01:
                errors.append(f"{row.ammo}: catalog current {row.current_range} != live {live}")
            if live_must_match == "proposed" and abs(live - row.proposed_range) > 0.01:
                errors.append(f"{row.ammo}: catalog proposed {row.proposed_range} != live {live}")

    seen_weapons: set[str] = set()
    longest_proposed_by_caliber: dict[str, float] = {}
    for row in weapon_rows:
        if row.weapon in seen_weapons:
            errors.append(f"duplicate weapon {row.weapon}")
        seen_weapons.add(row.weapon)
        if row.category not in CATEGORIES:
            errors.append(f"{row.weapon}: unknown category {row.category}")
        if row.ammo not in ammo_by_name:
            errors.append(f"{row.weapon}: ammo {row.ammo} not in ammo catalog")
        elif ammo_by_name[row.ammo].caliber != row.caliber:
            errors.append(f"{row.weapon}: caliber {row.caliber} != ammo {row.ammo}")
        if not row.abs_asset_path.is_file():
            errors.append(f"missing weapon asset {row.asset_path}")
        else:
            live = read_yaml_float(row.abs_asset_path, "m_EffectiveRangeMeters")
            if live_must_match == "current" and abs(live - row.current_weapon_range) > 0.01:
                errors.append(f"{row.weapon}: catalog current {row.current_weapon_range} != live {live}")
            if live_must_match == "proposed" and abs(live - row.proposed_weapon_range) > 0.01:
                errors.append(f"{row.weapon}: catalog proposed {row.proposed_weapon_range} != live {live}")

        longest_proposed_by_caliber[row.caliber] = max(
            longest_proposed_by_caliber.get(row.caliber, 0.0),
            row.proposed_weapon_range,
        )

        ammo_row = ammo_by_name.get(row.ammo)
        if ammo_row is None:
            continue

        current_e = resolve_effective_range_meters(row.current_weapon_range, 1.0, ammo_row.current_range)
        proposed_e = resolve_effective_range_meters(
            row.proposed_weapon_range,
            PROPOSED_OPTIC_EFFECTIVE_RANGE_MODIFIER,
            ammo_row.proposed_range,
        )

        if row.category in LINEAR_CATEGORIES:
            if not 0.0 < proposed_e <= MAX_HITSCAN_ENVELOPE_METERS:
                errors.append(f"{row.weapon}: proposed E {proposed_e} outside 0..{MAX_HITSCAN_ENVELOPE_METERS}")
            if row.min_multiplier_at_edge is None:
                errors.append(f"{row.weapon}: linear model needs min_multiplier_at_edge")
            else:
                actual = compute_falloff_multiplier(row.engagement_edge, proposed_e)
                if actual + 0.002 < row.min_multiplier_at_edge:
                    errors.append(
                        f"{row.weapon}: edge {row.engagement_edge} m multiplier {actual:.3f} "
                        f"< catalog {row.min_multiplier_at_edge:.3f}"
                    )
                if abs(actual - row.min_multiplier_at_edge) > 0.002:
                    errors.append(
                        f"{row.weapon}: catalog min_multiplier {row.min_multiplier_at_edge:.3f} "
                        f"!= math {actual:.3f}"
                    )
        elif row.category == "ProjectileSupport":
            if proposed_e > MAX_HITSCAN_ENVELOPE_METERS:
                errors.append(f"{row.weapon}: projectile ceiling {proposed_e} > envelope")
            if row.min_multiplier_at_edge is not None:
                errors.append(f"{row.weapon}: projectile must not advertise a hitscan multiplier")
        elif row.category == "ShotgunCurve":
            if abs(proposed_e - 40.0) > 0.01:
                errors.append(f"{row.weapon}: shotgun proposal should stay at 40 m")

        _ = current_e

    for ammo_row in ammo_rows:
        longest = longest_proposed_by_caliber.get(ammo_row.caliber, 0.0)
        if ammo_row.damage_model != "ProjectileSupport" and ammo_row.proposed_range + 1e-4 < longest:
            errors.append(
                f"{ammo_row.ammo}: proposed {ammo_row.proposed_range} would cap {ammo_row.caliber} weapon {longest}"
            )

    live_weapons = {path.stem for path in SHOOTING.rglob("Weapon_*.asset")}
    catalog_weapons = {row.weapon for row in weapon_rows}
    missing_live = sorted(live_weapons - catalog_weapons)
    extra = sorted(catalog_weapons - live_weapons)
    if missing_live:
        errors.append(f"catalog missing live weapons: {missing_live}")
    if extra:
        errors.append(f"catalog extra weapons: {extra}")

    live_ammo = {path.stem for path in SHOOTING.rglob("Ammo_*.asset")}
    catalog_ammo = {row.ammo for row in ammo_rows}
    missing_ammo = sorted(live_ammo - catalog_ammo)
    extra_ammo = sorted(catalog_ammo - live_ammo)
    if missing_ammo:
        errors.append(f"catalog missing live ammo: {missing_ammo}")
    if extra_ammo:
        errors.append(f"catalog extra ammo: {extra_ammo}")

    return errors


def format_proposal_table(
    weapons: list[WeaponRangeRow] | None = None,
    ammo: list[AmmoRangeRow] | None = None,
) -> str:
    weapon_rows = weapons if weapons is not None else load_weapon_catalog()
    ammo_rows = ammo if ammo is not None else load_ammo_catalog()
    ammo_by_name = {row.ammo: row for row in ammo_rows}
    lines = [
        "weapon | role | model | current W/A/E | proposed W/A/E | edge | min x",
        "---|---|---|---:|---:|---:|---:",
    ]
    for row in weapon_rows:
        ammo_row = ammo_by_name[row.ammo]
        current_e = resolve_effective_range_meters(row.current_weapon_range, 1.0, ammo_row.current_range)
        proposed_e = resolve_effective_range_meters(
            row.proposed_weapon_range,
            PROPOSED_OPTIC_EFFECTIVE_RANGE_MODIFIER,
            ammo_row.proposed_range,
        )
        min_mult = "N/A" if row.min_multiplier_at_edge is None else f"{row.min_multiplier_at_edge:.2f}"
        lines.append(
            f"{row.weapon} | {row.role} | {row.category} | "
            f"{row.current_weapon_range:.0f}/{ammo_row.current_range:.0f}/{current_e:.0f} | "
            f"{row.proposed_weapon_range:.0f}/{ammo_row.proposed_range:.0f}/{proposed_e:.0f} | "
            f"{row.engagement_edge:.0f} | {min_mult}"
        )
    return "\n".join(lines)


def main() -> int:
    weapon_rows = load_weapon_catalog()
    ammo_rows = load_ammo_catalog()
    errors = validate_catalogs(weapon_rows, ammo_rows)
    if errors:
        print("VALIDATION FAILED:", file=sys.stderr)
        for error in errors:
            print(f"  {error}", file=sys.stderr)
        return 1
    print(format_proposal_table(weapon_rows, ammo_rows))
    print()
    print(f"optic Range x proposal: {PROPOSED_OPTIC_EFFECTIVE_RANGE_MODIFIER:.1f}")
    print("silencers stay 1.1 until a separate physical-module pass")
    print("VALIDATION OK - live YAML matches proposed catalog")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
