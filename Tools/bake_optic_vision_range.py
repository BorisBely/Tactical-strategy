#!/usr/bin/env python3
"""Write ScopeVisionRange / variable-magnification fields into combat optic .asset YAML.

Does not touch DistanceCurve, AimTimeModifier, EffectiveRangeModifier, Q, or damage.
Source: Tools/optic_vision_catalog.csv
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(Path(__file__).resolve().parent))

from optic_vision_catalog import group_bake_specs, validate_catalog

VISION_BLOCK_RE = re.compile(
    r"(  m_EffectiveRangeModifier: [0-9.]+\n)"
    r"(?:  m_ScopeVisionRangeMeters: [0-9.]+\n)?"
    r"(?:  m_HasVariableMagnification: [01]\n)?"
    r"(?:  m_LowMagnificationScopeVisionRangeMeters: [0-9.]+\n)?"
    r"(?:  m_HighMagnificationActive: [01]\n)?",
    re.MULTILINE,
)


def render_vision_block(effective_line: str, high: float, variable: bool, low: float) -> str:
    has_var = "1" if variable else "0"
    low_value = f"{low:g}" if variable else "0"
    return (
        f"{effective_line}"
        f"  m_ScopeVisionRangeMeters: {high:g}\n"
        f"  m_HasVariableMagnification: {has_var}\n"
        f"  m_LowMagnificationScopeVisionRangeMeters: {low_value}\n"
        f"  m_HighMagnificationActive: 1\n"
    )


def patch_asset(path: Path, high: float, variable: bool, low: float) -> None:
    text = path.read_text(encoding="utf-8")
    match = VISION_BLOCK_RE.search(text)
    if match is None:
        raise RuntimeError(f"No m_EffectiveRangeModifier block in {path}")
    replacement = render_vision_block(match.group(1), high, variable, low)
    new_text, count = VISION_BLOCK_RE.subn(replacement, text, count=1)
    if count != 1:
        raise RuntimeError(f"Failed to patch vision fields in {path}")
    if new_text != text:
        path.write_text(new_text, encoding="utf-8", newline="\n")


def main() -> int:
    errors = validate_catalog()
    if errors:
        for error in errors:
            print(f"ERROR {error}")
        return 1

    patched = 0
    for spec in group_bake_specs():
        if not spec.asset_path.is_file():
            raise FileNotFoundError(spec.asset_path)
        before = spec.asset_path.read_text(encoding="utf-8")
        patch_asset(spec.asset_path, spec.high_range, spec.has_variable, spec.low_range)
        after = spec.asset_path.read_text(encoding="utf-8")
        if after != before:
            patched += 1
        print(
            f"{spec.optic}: high={spec.high_range:g} variable={int(spec.has_variable)} "
            f"low={spec.low_range:g}"
        )

    print(f"Done. Patched {patched} optic assets.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
