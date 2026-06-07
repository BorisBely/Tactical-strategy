"""Fix item localization keys and rebuild mission prep available equipment set."""
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
INVENTORY = ROOT / "Assets" / "GameData" / "Inventory"
SET_ASSET = INVENTORY / "M4" / "MissionPrepM4AvailableEquipmentSet.asset"
ITEM_SET_SCRIPT_GUID = "b8f4e2a1c3d5e6f708192a3b4c5d6e7f"
MAGAZINE_AMMO_556_GUID = "a1f2e3d4c5b6a9789090ab1cd2ef3045"
MAGAZINE_AMMO_762_GUID = "a33bd9274e8567e4fbeb0a04669fabfa"

# Keys in assets -> keys in localization files
KEY_FIXES = {
    "item.attachment.acog": "item.attachment.m4_acog",
    "item.attachment.acog_rmr": "item.attachment.m4_acog_rmr",
    "item.attachment.aimpoint": "item.attachment.m4_aimpoint",
    "item.attachment.elcan_specterdr": "item.attachment.m4_elcan_specterdr",
    "item.attachment.eotech_g33": "item.attachment.m4_eotech_g33",
    "item.attachment.flashlight1": "item.attachment.m4_flashlight1",
    "item.attachment.foregrip1": "item.attachment.m4_foregrip1",
    "item.attachment.foregrip2": "item.attachment.m4_foregrip2",
    "item.attachment.foregrip3": "item.attachment.m4_foregrip3",
    "item.attachment.foregrip4": "item.attachment.m4_foregrip4",
    "item.attachment.foregrip5": "item.attachment.m4_foregrip5",
    "item.attachment.laser1": "item.attachment.m4_laser1",
    "item.attachment.laser2": "item.attachment.m4_laser2",
    "item.attachment.rdc": "item.attachment.m4_rdc",
    "item.attachment.reddot1": "item.attachment.m4_reddot1",
    "item.attachment.reddot2": "item.attachment.m4_reddot2",
    "item.attachment.reddot3": "item.attachment.m4_reddot3",
    "item.attachment.scope1_3x": "item.attachment.m4_scope1_3x",
    "item.attachment.scope4": "item.attachment.m4_scope4",
    "item.attachment.scope5": "item.attachment.m4_scope5",
    "item.attachment.scope9": "item.attachment.m4_scope9",
    "item.attachment.susat": "item.attachment.m4_susat",
    "item.attachment.vortex_razor": "item.attachment.m4_vortex_razor",
}


def meta_guid(path: Path) -> str | None:
    meta = path.with_suffix(path.suffix + ".meta")
    if not meta.exists():
        return None
    m = re.search(r"^guid: ([0-9a-f]+)", meta.read_text(encoding="utf-8"), re.M)
    return m.group(1) if m else None


def sort_key(path: Path) -> tuple[int, str]:
    name = path.name.lower()
    if "weapon" in name:
        return (0, name)
    if "attachment" in name:
        return (1, name)
    if "mag" in name:
        return (2, name)
    if "ammo" in name or "loot" in name:
        return (3, name)
    return (4, name)


def fix_localization_keys() -> int:
    fixed = 0
    for path in INVENTORY.rglob("Item_*.asset"):
        text = path.read_text(encoding="utf-8")
        original = text
        for old, new in KEY_FIXES.items():
            text = text.replace(f"m_LocalizationKey: {old}", f"m_LocalizationKey: {new}")
        if text != original:
            path.write_text(text, encoding="utf-8", newline="\n")
            fixed += 1
            print(f"Fixed key: {path.name}")
    return fixed


def rebuild_item_set() -> None:
    items = sorted(INVENTORY.rglob("Item_*.asset"), key=sort_key)
    lines = [
        "%YAML 1.1",
        "%TAG !u! tag:unity3d.com,2011:",
        "--- !u!114 &11400000",
        "MonoBehaviour:",
        "  m_ObjectHideFlags: 0",
        "  m_CorrespondingSourceObject: {fileID: 0}",
        "  m_PrefabInstance: {fileID: 0}",
        "  m_PrefabAsset: {fileID: 0}",
        "  m_GameObject: {fileID: 0}",
        "  m_Enabled: 1",
        "  m_EditorHideFlags: 0",
        f"  m_Script: {{fileID: 11500000, guid: {ITEM_SET_SCRIPT_GUID}, type: 3}}",
        "  m_Name: MissionPrepM4AvailableEquipmentSet",
        "  m_EditorClassIdentifier: Assembly-CSharp::MissionPrepAvailableEquipmentItemSet",
        "  m_Items:",
    ]
    for path in items:
        guid = meta_guid(path)
        if guid:
            lines.append(f"  - {{fileID: 11400000, guid: {guid}, type: 2}}")
    lines.append(f"  m_MagazineAmmo: {{fileID: 11400000, guid: {MAGAZINE_AMMO_762_GUID}, type: 2}}")
    lines.append(f"  m_MagazineAmmo556: {{fileID: 11400000, guid: {MAGAZINE_AMMO_556_GUID}, type: 2}}")
    lines.append(f"  m_MagazineAmmo762: {{fileID: 11400000, guid: {MAGAZINE_AMMO_762_GUID}, type: 2}}")
    lines.append("  m_RoundsPerMagazine: -1")
    SET_ASSET.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
    print(f"Rebuilt {SET_ASSET.name} with {len(items)} items")


def main() -> None:
    count = fix_localization_keys()
    print(f"Localization fixes: {count}")
    rebuild_item_set()


if __name__ == "__main__":
    main()
