#!/usr/bin/env python3
"""Stage 9B: bake only damage-range fields.

Writes:
  WeaponDefinition.m_EffectiveRangeMeters
  AmmoDefinition.m_EffectiveRangeMeters
  combat optic WeaponAttachmentDefinition.m_EffectiveRangeModifier = 1

Does not touch BaseDamage, accuracy, recoil, Q, ScopeVisionRange, or silencers.
Source: Tools/weapon_range_catalog.csv + ammo_range_catalog.csv + optic_vision_catalog.csv
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(Path(__file__).resolve().parent))

from optic_vision_catalog import group_bake_specs
from weapon_damage_range_model import PROPOSED_OPTIC_EFFECTIVE_RANGE_MODIFIER
from weapon_range_catalog import load_ammo_catalog, load_weapon_catalog, validate_catalogs

RANGE_RE = re.compile(r"(  m_EffectiveRangeMeters: )[0-9.]+")
OPTIC_MOD_RE = re.compile(r"(  m_EffectiveRangeModifier: )[0-9.]+")


def format_range(value: float) -> str:
    text = f"{value:g}"
    return text


def set_first_scalar(text: str, pattern: re.Pattern[str], value: str, path: Path) -> str:
    new_text, count = pattern.subn(rf"\g<1>{value}", text, count=1)
    if count != 1:
        raise RuntimeError(f"{path}: failed to patch {pattern.pattern}")
    return new_text


def differing_range_lines(before: str, after: str) -> list[str]:
    before_lines = before.splitlines()
    after_lines = after.splitlines()
    if len(before_lines) != len(after_lines):
        return [f"line-count {len(before_lines)} -> {len(after_lines)}"]
    changed: list[str] = []
    for old, new in zip(before_lines, after_lines):
        if old == new:
            continue
        stripped = new.strip()
        if stripped.startswith("m_EffectiveRangeMeters:") or stripped.startswith("m_EffectiveRangeModifier:"):
            continue
        changed.append(f"{old!r} -> {new!r}")
    return changed


def patch_file(path: Path, pattern: re.Pattern[str], value: float) -> bool:
    before = path.read_text(encoding="utf-8")
    after = set_first_scalar(before, pattern, format_range(value), path)
    illegal = differing_range_lines(before, after)
    if illegal:
        raise RuntimeError(f"{path}: bake would change non-range fields: {illegal[:3]}")
    if after == before:
        return False
    path.write_text(after, encoding="utf-8")
    return True


def main() -> int:
    errors = validate_catalogs(live_must_match=None)
    if errors:
        for error in errors:
            print(f"ERROR {error}")
        return 1

    patched = 0
    for row in load_weapon_catalog():
        if patch_file(row.abs_asset_path, RANGE_RE, row.proposed_weapon_range):
            patched += 1
        print(f"{row.weapon}: {row.proposed_weapon_range:g}")

    for row in load_ammo_catalog():
        if patch_file(row.abs_asset_path, RANGE_RE, row.proposed_range):
            patched += 1
        print(f"{row.ammo}: {row.proposed_range:g}")

    seen_optics: set[Path] = set()
    for spec in group_bake_specs():
        if spec.asset_path in seen_optics:
            continue
        seen_optics.add(spec.asset_path)
        if patch_file(spec.asset_path, OPTIC_MOD_RE, PROPOSED_OPTIC_EFFECTIVE_RANGE_MODIFIER):
            patched += 1
        print(f"{spec.optic}: Range x={PROPOSED_OPTIC_EFFECTIVE_RANGE_MODIFIER:g}")

    baked_errors = validate_catalogs(live_must_match="proposed")
    if baked_errors:
        for error in baked_errors:
            print(f"ERROR {error}")
        return 1

    print(f"Done. Patched {patched} assets. Silencers untouched. ScopeVisionRange untouched.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
