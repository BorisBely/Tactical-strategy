#!/usr/bin/env python3
"""Add M2 combat socket transforms only (no EquippedWeapon YAML) to Light_Armored_Car.prefab."""

from pathlib import Path

PREFAB = Path(r"d:/Unity project/My project 001/Assets/Prefabs/Vehicles/Light_Armored_Car.prefab")

PITCH_TR = "8804073608841962906"
GUN_MESH_TR = "6595512600787406683"

MUZZLE_GO, MUZZLE_TR = "9205610000000000201", "9205610000000000202"
SHELL_GO, SHELL_TR = "9205610000000000203", "9205610000000000204"
BELT_GO, BELT_TR = "9205610000000000205", "9205610000000000206"
BARREL_GO, BARREL_TR = "9205610000000000207", "9205610000000000208"


def patch_children_block(text: str, transform_id: str, new_ids: list[str]) -> str:
    needle = f"--- !u!4 &{transform_id}\n"
    start = text.find(needle)
    if start < 0:
        raise RuntimeError(f"Transform {transform_id} not found")
    children_start = text.find("  m_Children:\n", start)
    line_end = text.find("\n", children_start)
    insert_at = line_end + 1
    section_end = text.find("  m_Father:", children_start)
    section = text[children_start:section_end]
    additions = ""
    for fid in new_ids:
        token = f"{{fileID: {fid}}}"
        if token in section:
            continue
        additions += f"  - {token}\n"
    if not additions:
        return text
    return text[:insert_at] + additions + text[insert_at:]


APPEND = f"""
--- !u!1 &{MUZZLE_GO}
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  serializedVersion: 6
  m_Component:
  - component: {{fileID: {MUZZLE_TR}}}
  m_Layer: 0
  m_Name: MuzzleExit
  m_TagString: Untagged
  m_Icon: {{fileID: 0}}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!4 &{MUZZLE_TR}
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {MUZZLE_GO}}}
  serializedVersion: 2
  m_LocalRotation: {{x: 0, y: 0, z: 0, w: 1}}
  m_LocalPosition: {{x: 0, y: 0.02, z: 0.65}}
  m_LocalScale: {{x: 1, y: 1, z: 1}}
  m_ConstrainProportionsScale: 0
  m_Children: []
  m_Father: {{fileID: {PITCH_TR}}}
  m_LocalEulerAnglesHint: {{x: 0, y: 0, z: 0}}
--- !u!1 &{SHELL_GO}
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  serializedVersion: 6
  m_Component:
  - component: {{fileID: {SHELL_TR}}}
  m_Layer: 0
  m_Name: ShellEject
  m_TagString: Untagged
  m_Icon: {{fileID: 0}}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!4 &{SHELL_TR}
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {SHELL_GO}}}
  serializedVersion: 2
  m_LocalRotation: {{x: 0, y: 0.7071068, z: 0, w: 0.7071068}}
  m_LocalPosition: {{x: 0.12, y: 0.05, z: 0.12}}
  m_LocalScale: {{x: 1, y: 1, z: 1}}
  m_ConstrainProportionsScale: 0
  m_Children: []
  m_Father: {{fileID: {PITCH_TR}}}
  m_LocalEulerAnglesHint: {{x: 0, y: 90, z: 0}}
--- !u!1 &{BELT_GO}
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  serializedVersion: 6
  m_Component:
  - component: {{fileID: {BELT_TR}}}
  m_Layer: 0
  m_Name: BeltEject
  m_TagString: Untagged
  m_Icon: {{fileID: 0}}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!4 &{BELT_TR}
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {BELT_GO}}}
  serializedVersion: 2
  m_LocalRotation: {{x: 0.3007058, y: 0, z: 0, w: 0.953717}}
  m_LocalPosition: {{x: -0.06, y: 0.02, z: 0.08}}
  m_LocalScale: {{x: 1, y: 1, z: 1}}
  m_ConstrainProportionsScale: 0
  m_Children: []
  m_Father: {{fileID: {PITCH_TR}}}
  m_LocalEulerAnglesHint: {{x: 35, y: 0, z: 0}}
--- !u!1 &{BARREL_GO}
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  serializedVersion: 6
  m_Component:
  - component: {{fileID: {BARREL_TR}}}
  m_Layer: 0
  m_Name: barrel
  m_TagString: Untagged
  m_Icon: {{fileID: 0}}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!4 &{BARREL_TR}
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {BARREL_GO}}}
  serializedVersion: 2
  m_LocalRotation: {{x: 0, y: 0, z: 0, w: 1}}
  m_LocalPosition: {{x: 0, y: 0, z: 0.58}}
  m_LocalScale: {{x: 1, y: 1, z: 1}}
  m_ConstrainProportionsScale: 0
  m_Children: []
  m_Father: {{fileID: {GUN_MESH_TR}}}
  m_LocalEulerAnglesHint: {{x: 0, y: 0, z: 0}}
"""


def main() -> None:
    text = PREFAB.read_text(encoding="utf-8")
    if "m_Name: MuzzleExit" in text:
        # Strip EquippedWeapon YAML if present from older patch (breaks Inspector).
        import re

        text = re.sub(
            r"  - component: \{fileID: 9205610000000000210\}\n",
            "",
            text,
            count=1,
        )
        text = re.sub(
            r"--- !u!114 &9205610000000000210\nMonoBehaviour:.*?(?=\n--- !u!|\Z)",
            "",
            text,
            count=1,
            flags=re.S,
        )
        if "m_Name: MuzzleExit" in text:
            PREFAB.write_text(text, encoding="utf-8")
            print("Removed EquippedWeapon YAML; sockets already present.")
            return

    for fid in [MUZZLE_TR, SHELL_TR, BELT_TR, BARREL_TR]:
        if f"&{fid}" in text:
            raise RuntimeError(f"fileID collision: {fid}")

    text = patch_children_block(text, PITCH_TR, [MUZZLE_TR, SHELL_TR, BELT_TR])
    text = patch_children_block(text, GUN_MESH_TR, [BARREL_TR])
    if not text.endswith("\n"):
        text += "\n"
    text += APPEND.lstrip("\n")
    PREFAB.write_text(text, encoding="utf-8")
    print(f"Patched combat socket transforms on {PREFAB}")


if __name__ == "__main__":
    main()
