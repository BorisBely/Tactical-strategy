"""Audit mission prep available equipment vs all Item_ assets."""
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
INVENTORY = ROOT / "Assets" / "GameData" / "Inventory"
SET_ASSET = INVENTORY / "M4" / "MissionPrepM4AvailableEquipmentSet.asset"


def meta_guid(path: Path) -> str | None:
    meta = path.with_suffix(path.suffix + ".meta")
    if not meta.exists():
        return None
    m = re.search(r"^guid: ([0-9a-f]+)", meta.read_text(encoding="utf-8"), re.M)
    return m.group(1) if m else None


def item_info(path: Path) -> dict:
    text = path.read_text(encoding="utf-8")
    display = re.search(r"m_DisplayName: (.+)", text)
    loc = re.search(r"m_LocalizationKey: (.+)", text)
    icon = re.search(r"m_Icon: \{fileID: (\d+)", text)
    return {
        "file": path.name,
        "display": display.group(1).strip() if display else "?",
        "loc": loc.group(1).strip() if loc else "",
        "has_icon": bool(icon and icon.group(1) != "0"),
        "guid": meta_guid(path),
    }


def main() -> None:
    items = sorted(INVENTORY.rglob("Item_*.asset"))
    by_guid = {item_info(p)["guid"]: item_info(p) for p in items if meta_guid(p)}

    set_text = SET_ASSET.read_text(encoding="utf-8")
    set_guids = re.findall(r"guid: ([0-9a-f]+)", set_text)[1:]  # skip script

    print("=== In set ===")
    for g in set_guids:
        if g in by_guid:
            i = by_guid[g]
            print(f"  {i['file']:45} {i['display'][:50]:50} {'ICON' if i['has_icon'] else 'no icon'}")
        else:
            print(f"  STALE GUID: {g}")

    in_set = set(set_guids)
    missing = [by_guid[g] for g in by_guid if g not in in_set]
    print(f"\n=== Missing from set ({len(missing)}) ===")
    for i in missing:
        print(f"  {i['file']:45} {i['display'][:50]:50} {'ICON' if i['has_icon'] else 'no icon'}")

    no_icon = [i for i in by_guid.values() if not i["has_icon"]]
    print(f"\n=== Items without icon ({len(no_icon)}) ===")
    for i in no_icon:
        print(f"  {i['file']}")


if __name__ == "__main__":
    main()
