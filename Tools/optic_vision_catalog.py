#!/usr/bin/env python3
"""Vision Stage 8 optic catalog. Source of ScopeVisionRange bake. Does not retune Q or damage."""

from __future__ import annotations

import csv
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CATALOG_CSV = Path(__file__).resolve().parent / "optic_vision_catalog.csv"
SHOOTING = ROOT / "Assets" / "GameData" / "Shooting"

MIN_SCOPE_RANGE = 150.0
MAX_SCOPE_RANGE = 300.0


@dataclass(frozen=True)
class OpticModeRow:
    optic: str
    asset_path: str
    behavior_class: str
    mode: str
    magnification: float
    scope_vision_range: float
    has_variable: bool
    display_name: str
    notes: str

    @property
    def abs_asset_path(self) -> Path:
        return ROOT / self.asset_path.replace("\\", "/")


@dataclass(frozen=True)
class OpticBakeSpec:
    optic: str
    asset_path: Path
    has_variable: bool
    low_range: float
    high_range: float
    behavior_class: str
    display_name: str
    modes: tuple[OpticModeRow, ...]


def load_catalog(path: Path | None = None) -> list[OpticModeRow]:
    csv_path = path or CATALOG_CSV
    rows: list[OpticModeRow] = []
    with csv_path.open(encoding="utf-8", newline="") as handle:
        reader = csv.DictReader(
            (line for line in handle if line.strip() and not line.lstrip().startswith("#"))
        )
        for raw in reader:
            rows.append(
                OpticModeRow(
                    optic=raw["optic"].strip(),
                    asset_path=raw["asset_path"].strip(),
                    behavior_class=raw["behavior_class"].strip(),
                    mode=raw["mode"].strip(),
                    magnification=float(raw["magnification"]),
                    scope_vision_range=float(raw["scope_vision_range"]),
                    has_variable=raw["has_variable"].strip() in ("1", "true", "True"),
                    display_name=raw["display_name"].strip(),
                    notes=raw.get("notes", "").strip(),
                )
            )
    if not rows:
        raise RuntimeError(f"Empty optic catalog: {csv_path}")
    return rows


def group_bake_specs(rows: list[OpticModeRow] | None = None) -> list[OpticBakeSpec]:
    catalog = rows if rows is not None else load_catalog()
    grouped: dict[str, list[OpticModeRow]] = {}
    for row in catalog:
        grouped.setdefault(row.optic, []).append(row)

    specs: list[OpticBakeSpec] = []
    for optic, modes in grouped.items():
        variable = any(mode.has_variable for mode in modes)
        if variable:
            low_modes = [mode for mode in modes if mode.magnification <= 1.01]
            if not low_modes:
                raise RuntimeError(f"{optic}: variable optic needs a 1x row")
            low = low_modes[0].scope_vision_range
            high = max(mode.scope_vision_range for mode in modes)
        else:
            if len(modes) != 1:
                raise RuntimeError(f"{optic}: fixed optic must have exactly one catalog row")
            low = 0.0
            high = modes[0].scope_vision_range
        specs.append(
            OpticBakeSpec(
                optic=optic,
                asset_path=modes[0].abs_asset_path,
                has_variable=variable,
                low_range=low,
                high_range=high,
                behavior_class=modes[0].behavior_class,
                display_name=modes[0].display_name,
                modes=tuple(modes),
            )
        )
    return specs


def validate_catalog(rows: list[OpticModeRow] | None = None) -> list[str]:
    catalog = rows if rows is not None else load_catalog()
    errors: list[str] = []
    seen_300 = False
    for row in catalog:
        if not row.abs_asset_path.is_file():
            errors.append(f"{row.optic}/{row.mode}: missing asset {row.asset_path}")
        if row.scope_vision_range < MIN_SCOPE_RANGE or row.scope_vision_range > MAX_SCOPE_RANGE:
            errors.append(
                f"{row.optic}/{row.mode}: ScopeVisionRange {row.scope_vision_range} outside "
                f"{MIN_SCOPE_RANGE:g}…{MAX_SCOPE_RANGE:g}"
            )
        if row.magnification <= 1.01 and row.scope_vision_range != MIN_SCOPE_RANGE:
            errors.append(f"{row.optic}/{row.mode}: 1x must be {MIN_SCOPE_RANGE:g}")
        if row.magnification > 1.01 and row.scope_vision_range <= MIN_SCOPE_RANGE:
            errors.append(f"{row.optic}/{row.mode}: mag>1 must be >{MIN_SCOPE_RANGE:g}")
        if row.scope_vision_range == MAX_SCOPE_RANGE:
            seen_300 = True
    if not seen_300:
        errors.append("catalog must include at least one ScopeVisionRange=300")
    return errors
