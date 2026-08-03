#!/usr/bin/env python3
"""Patch Light_Armored_Car.prefab with M2 combat socket transforms + EquippedWeapon."""

from pathlib import Path

PREFAB = Path(r"d:/Unity project/My project 001/Assets/Prefabs/Vehicles/Light_Armored_Car.prefab")

PITCH_GO = "6169199702788700712"
PITCH_TR = "8804073608841962906"
GUN_MESH_TR = "6595512600787406683"

# New file IDs (must not collide)
MUZZLE_GO, MUZZLE_TR = "9205610000000000201", "9205610000000000202"
SHELL_GO, SHELL_TR = "9205610000000000203", "9205610000000000204"
BELT_GO, BELT_TR = "9205610000000000205", "9205610000000000206"
BARREL_GO, BARREL_TR = "9205610000000000207", "9205610000000000208"
EQUIPPED_WEAPON = "9205610000000000210"

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
--- !u!114 &{EQUIPPED_WEAPON}
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {PITCH_GO}}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: c7a2b91f4e3d4051a8f6e2d9c0b1a4e7, type: 3}}
  m_Name: 
  m_EditorClassIdentifier: Assembly-CSharp::EquippedWeapon
  m_Barrel: {{fileID: {MUZZLE_TR}}}
  m_ShellEject: {{fileID: {SHELL_TR}}}
  m_SightPivot: {{fileID: 0}}
  m_MagazineSocket: {{fileID: 0}}
  m_SecondaryMagazineSocket: {{fileID: 0}}
  m_MuzzleModuleVisualSocket: {{fileID: 0}}
  m_OpticModuleVisualSocket: {{fileID: 0}}
  m_SideRailModuleVisualSocket: {{fileID: 0}}
  m_StockSocket: {{fileID: 0}}
  m_UnderBarrelSocket: {{fileID: 0}}
  m_RailSockets: []
  m_EquippedAttachments: []
  m_DefaultOpticVisuals: []
  m_DefaultStockVisuals: []
  m_OpticRailMountVisuals: []
  m_SideRailMountVisuals: []
  m_VisualRecoilKickPivot: {{fileID: 0}}
  m_BoltCarrier: {{fileID: 0}}
  m_BoltOpenLocalOffset: {{x: 0, y: 0, z: -0.08}}
  m_BoltHandleOpenLocalEulerAngles: {{x: 0, y: 0, z: 0}}
  m_BoltHandleRotatePhaseNormalized: 0.25
  m_BoltCycleSeconds: 0.085
  m_BoltCycleSecondsSingleShot: 0.16
  m_BoltActionCycleSeconds: 0.55
  m_BoltShellEjectNormalizedTime: 0.5
  m_DustCoverHinge: {{fileID: 0}}
  m_DustCoverClosedDegrees: -160
  m_DustCoverHingeAxis: {{x: 0, y: 0, z: 1}}
  m_DustCoverTweenSeconds: 0.12
  m_LmgTopCoverHinge: {{fileID: 0}}
  m_LmgBeltMeshVisual: {{fileID: 0}}
  m_LmgCoverOpenDegrees: 110
  m_LmgCoverTweenSeconds: 0.3
  m_DrawBarrelDebugRay: 0
  m_BarrelDebugRayLength: 4
  m_BarrelDebugRayColor: {{r: 0, g: 0.92, b: 1, a: 1}}
"""


def patch_children_block(text: str, transform_id: str, new_ids: list[str]) -> str:
    needle = f"--- !u!4 &{transform_id}\n"
    start = text.find(needle)
    if start < 0:
        raise RuntimeError(f"Transform {transform_id} not found")
    children_start = text.find("  m_Children:\n", start)
    if children_start < 0:
        raise RuntimeError(f"m_Children not found for {transform_id}")
    line_end = text.find("\n", children_start)
    insert_at = line_end + 1
    additions = ""
    for fid in new_ids:
        token = f"{{fileID: {fid}}}"
        if token in text:
            continue
        additions += f"  - {token}\n"
    if not additions:
        return text
    return text[:insert_at] + additions + text[insert_at:]


def patch_pitch_components(text: str) -> str:
    needle = f"--- !u!1 &{PITCH_GO}\n"
    start = text.find(needle)
    if start < 0:
        raise RuntimeError("GameObjectGun.12.7 GO not found")
    comp_start = text.find("  m_Component:\n", start)
    comp_line_end = text.find("\n", comp_start)
    if f"{{fileID: {EQUIPPED_WEAPON}}}" in text:
        return text
    transform_line = text.find("  - component: {fileID: " + PITCH_TR + "}", comp_start)
    if transform_line < 0:
        raise RuntimeError("Pitch Transform component line not found")
    transform_line_end = text.find("\n", transform_line)
    insert = f"  - component: {{fileID: {EQUIPPED_WEAPON}}}\n"
    return text[: transform_line_end + 1] + insert + text[transform_line_end + 1 :]


def main() -> None:
    text = PREFAB.read_text(encoding="utf-8")
    if "m_Name: MuzzleExit" in text and f"&{MUZZLE_TR}" in text:
        print("Combat sockets already present; skipping.")
        return

    for fid in [MUZZLE_TR, SHELL_TR, BELT_TR, BARREL_TR, EQUIPPED_WEAPON]:
        if f"&{fid}" in text:
            raise RuntimeError(f"fileID collision: {fid}")

    text = patch_pitch_components(text)
    text = patch_children_block(
        text,
        PITCH_TR,
        [MUZZLE_TR, SHELL_TR, BELT_TR],
    )
    text = patch_children_block(text, GUN_MESH_TR, [BARREL_TR])
    if not text.endswith("\n"):
        text += "\n"
    text += APPEND.lstrip("\n")
    PREFAB.write_text(text, encoding="utf-8")
    print(f"Patched {PREFAB}")


if __name__ == "__main__":
    main()
