"""Find ItemDefinition localization keys missing from russian.json."""
from __future__ import annotations

import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
INVENTORY = ROOT / "Assets" / "GameData" / "Inventory"
LOC = ROOT / "Assets" / "Resources" / "Localization" / "russian.json"


def main() -> None:
    data = json.loads(LOC.read_text(encoding="utf-8"))
    keys = {e["key"] for e in data.get("entries", [])}

    missing = []
    ok = []
    for path in sorted(INVENTORY.rglob("Item_*.asset")):
        text = path.read_text(encoding="utf-8")
        m = re.search(r"m_LocalizationKey: (.+)", text)
        key = m.group(1).strip() if m else ""
        if key in keys:
            ok.append((path.name, key))
        else:
            missing.append((path.name, key))

    print(f"OK: {len(ok)}  MISSING: {len(missing)}")
    for name, key in missing:
        print(f"  {name:45} -> {key}")


if __name__ == "__main__":
    main()
