#!/usr/bin/env python3
"""Remove broken YAML-injected turret stack; keep M2 combat sockets on pitch."""

from pathlib import Path
import re

PREFAB = Path(r"d:/Unity project/My project 001/Assets/Prefabs/Vehicles/Light_Armored_Car.prefab")

TURRET_COMP_IDS = [
    "9205610000000000301",
    "9205610000000000302",
    "9205610000000000303",
    "9205610000000000304",
    "9205610000000000305",
    "9205610000000000306",
    "9205610000000000307",
    "9205610000000000308",
    "9205610000000000309",
]

VC_REFS = [
    "m_TurretHierarchy",
    "m_TurretAim",
    "m_TurretVisual",
    "m_VehicleInventory",
    "m_TurretEquipment",
    "m_TurretGunnerBridge",
    "m_TurretWeaponRecoil",
    "m_TurretShellEjection",
]


def remove_mono_block(text: str, comp_id: str) -> str:
    needle = f"--- !u!114 &{comp_id}\n"
    start = text.find(needle)
    if start < 0:
        return text
    end = text.find("\n--- !u!", start + 1)
    if end < 0:
        return text[:start]
    return text[:start] + text[end + 1 :]


def remove_component_from_go_list(text: str, go_id: str, comp_id: str) -> str:
    line = f"  - component: {{fileID: {comp_id}}}\n"
    go_needle = f"--- !u!1 &{go_id}\n"
    start = text.find(go_needle)
    if start < 0:
        return text
    comp_start = text.find("  m_Component:\n", start)
    comp_end = text.find("  m_Layer:", comp_start)
    block = text[comp_start:comp_end]
    if line not in block:
        return text
    return text[:comp_start] + block.replace(line, "") + text[comp_end:]


def reset_vehicle_controller_refs(text: str) -> str:
    for prop in VC_REFS:
        text = re.sub(
            rf"({re.escape(prop)}: )\{{fileID: [0-9]+\}}",
            rf"\1{{fileID: 0}}",
            text,
            count=1,
        )
    return text


def main() -> None:
    text = PREFAB.read_text(encoding="utf-8")
    original = text

    for comp_id in TURRET_COMP_IDS:
        text = remove_component_from_go_list(text, "3048653151834290230", comp_id)
        text = remove_mono_block(text, comp_id)

    text = reset_vehicle_controller_refs(text)

    if text != original:
        PREFAB.write_text(text, encoding="utf-8")
        print(f"Stripped turret stack from {PREFAB}")
    else:
        print("No turret stack blocks found.")


if __name__ == "__main__":
    main()
