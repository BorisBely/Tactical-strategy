#!/usr/bin/env python3
"""Inject WeaponGripRig hierarchy into equipped weapon prefabs and foregrip visuals."""
from __future__ import annotations

import os
import re
import random
from pathlib import Path

ROOT = Path(r"d:\Unity project\My project 001")
GRIP_GUID = "a8f3c1e29b7d4e6a9c0d2f4b6183e5a1"
FORE_GUID = "b9e4d2f30c8e5f7b0d1e3a5c7294f6b2"

EQUIPPED_DIRS = [
    ROOT / "Assets/Prefabs/Weapons/M4/Equipped",
    ROOT / "Assets/Prefabs/Weapons/AK/Equipped",
    ROOT / "Assets/Prefabs/Weapons/Standalone/Equipped",
    ROOT / "Assets/Prefabs/Weapons/RocketLaunchers/Equipped",
]

FOREGRIP_DIRS = [
    ROOT / "Assets/Prefabs/Weapons/M4/Visuals/Attachments",
]

INVENTORY = ROOT / "Assets/GameData/Inventory"


def uid() -> int:
    # Unity fileIDs are signed 64-bit-ish positive ints used in YAML
    return random.randint(10**15, 9 * 10**17)


def parse_vec(text: str, key: str):
    m = re.search(
        rf"{key}: \{{x: ([-\d.eE+]+), y: ([-\d.eE+]+), z: ([-\d.eE]+)\}}",
        text,
    )
    if not m:
        return (0.0, 0.0, 0.0)
    return tuple(float(m.group(i)) for i in range(1, 4))


def build_item_map():
    """guid of equipped prefab -> ik seed vectors from ItemDefinition"""
    mapping = {}
    for asset in INVENTORY.rglob("Item_Weapon_*.asset"):
        text = asset.read_text(encoding="utf-8")
        m = re.search(r"m_EquippedVisualPrefab: \{fileID: \d+, guid: ([a-f0-9]+), type: 3\}", text)
        if not m:
            continue
        guid = m.group(1)
        rp = parse_vec(text, "m_RightHandIkReadyLocalPosition")
        reu = parse_vec(text, "m_RightHandIkReadyLocalEulerAngles")
        if rp == (0.0, 0.0, 0.0) and reu == (0.0, 0.0, 0.0):
            rp = parse_vec(text, "m_RightHandIkNotReadyLocalPosition")
            reu = parse_vec(text, "m_RightHandIkNotReadyLocalEulerAngles")
        lp = parse_vec(text, "m_LeftHandIkReadyLocalPosition")
        leu = parse_vec(text, "m_LeftHandIkReadyLocalEulerAngles")
        if lp == (0.0, 0.0, 0.0) and leu == (0.0, 0.0, 0.0):
            lp = parse_vec(text, "m_LeftHandIkNotReadyLocalPosition")
            leu = parse_vec(text, "m_LeftHandIkNotReadyLocalEulerAngles")
        fg = {}
        for i in range(1, 6):
            fg[i] = (
                parse_vec(text, f"m_ForeGrip{i}LeftHandIkReadyLocalPosition"),
                parse_vec(text, f"m_ForeGrip{i}LeftHandIkReadyLocalEulerAngles"),
            )
        mapping[guid] = {"right": (rp, reu), "left": (lp, leu), "fg": fg, "path": str(asset)}
    return mapping


def prefab_guid(prefab: Path) -> str | None:
    meta = Path(str(prefab) + ".meta")
    if not meta.exists():
        return None
    m = re.search(r"guid: ([a-f0-9]+)", meta.read_text(encoding="utf-8"))
    return m.group(1) if m else None


def find_root_transform_block(text: str):
    """Return (root_go_id, root_transform_id, root_transform_match) for prefab root (m_Father: {fileID: 0})."""
    # Find Transform with m_Father: {fileID: 0}
    for m in re.finditer(r"--- !u!4 &(\d+)\nTransform:\n(.*?)(?=\n--- |\Z)", text, re.S):
        block = m.group(2)
        if "m_Father: {fileID: 0}" in block:
            go_m = re.search(r"m_GameObject: \{fileID: (\d+)\}", block)
            if not go_m:
                continue
            return go_m.group(1), m.group(1), m
    return None, None, None


def fmt_vec(v):
    return f"{{x: {v[0]}, y: {v[1]}, z: {v[2]}}}"


def euler_to_quat_approx(euler):
    # Store identity quat; Unity uses m_LocalEulerAnglesHint for display; rotation from euler applied via hint
    # Better: compute quaternion from euler XYZ in degrees
    import math

    x, y, z = (math.radians(a) for a in euler)
    cx, sx = math.cos(x * 0.5), math.sin(x * 0.5)
    cy, sy = math.cos(y * 0.5), math.sin(y * 0.5)
    cz, sz = math.cos(z * 0.5), math.sin(z * 0.5)
    # Unity ZXY? Actually Unity uses ZXY intrinsic for euler. Common approximation XYZ:
    qw = cx * cy * cz + sx * sy * sz
    qx = sx * cy * cz - cx * sy * sz
    qy = cx * sy * cz + sx * cy * sz
    qz = cx * cy * sz - sx * sy * cz
    return qx, qy, qz, qw


def make_empty_go(name, go_id, tr_id, father_id, pos, euler):
    qx, qy, qz, qw = euler_to_quat_approx(euler)
    return f"""--- !u!1 &{go_id}
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  serializedVersion: 6
  m_Component:
  - component: {{fileID: {tr_id}}}
  m_Layer: 0
  m_Name: {name}
  m_TagString: Untagged
  m_Icon: {{fileID: 0}}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!4 &{tr_id}
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {go_id}}}
  serializedVersion: 2
  m_LocalRotation: {{x: {qx}, y: {qy}, z: {qz}, w: {qw}}}
  m_LocalPosition: {fmt_vec(pos)}
  m_LocalScale: {{x: 1, y: 1, z: 1}}
  m_ConstrainProportionsScale: 0
  m_Children: []
  m_Father: {{fileID: {father_id}}}
  m_LocalEulerAnglesHint: {fmt_vec(euler)}
"""


def inject_weapon(prefab: Path, seeds: dict | None):
    text = prefab.read_text(encoding="utf-8")
    if "WeaponGripRig" in text or "m_Name: GripRig" in text:
        return False

    go_id, tr_id, tr_match = find_root_transform_block(text)
    if not tr_id:
        print(f"SKIP no root: {prefab}")
        return False

    right_pos, right_eu = ((0, 0, 0), (0, 0, 0))
    left_pos, left_eu = ((0, 0, 0), (0, 0, 0))
    if seeds:
        right_pos, right_eu = seeds["right"]
        left_pos, left_eu = seeds["left"]

    # Prefer legacy dummy locals if present in prefab
    rh = re.search(
        r"m_Name: RightHandIkTarget\n.*?m_LocalPosition: (\{x: .*?\})",
        text,
        re.S,
    )
    # fallback keep asset seeds

    grip_go, grip_tr = uid(), uid()
    right_go, right_tr = uid(), uid()
    left_go, left_tr = uid(), uid()
    mono_id = uid()

    # Update root GameObject components list to include WeaponGripRig
    go_pat = rf"(--- !u!1 &{go_id}\nGameObject:\n.*?m_Component:\n)((?:  - component: \{{fileID: \d+\}}\n)+)"

    def add_comp(m):
        return m.group(1) + m.group(2) + f"  - component: {{fileID: {mono_id}}}\n"

    text2, n = re.subn(go_pat, add_comp, text, count=1, flags=re.S)
    if n != 1:
        print(f"SKIP cannot patch GO components: {prefab}")
        return False
    text = text2

    # Add GripRig child to root transform children
    tr_block_pat = rf"(--- !u!4 &{tr_id}\nTransform:\n.*?m_Children:\n)((?:  - \{{fileID: \d+\}}\n)*)(  m_Father: \{{fileID: 0\}})"

    def add_child(m):
        return m.group(1) + m.group(2) + f"  - {{fileID: {grip_tr}}}\n" + m.group(3)

    text2, n = re.subn(tr_block_pat, add_child, text, count=1, flags=re.S)
    if n != 1:
        # root may have empty children
        tr_block_pat2 = rf"(--- !u!4 &{tr_id}\nTransform:\n.*?m_Children: \[\]\n)(  m_Father: \{{fileID: 0\}})"

        def add_child2(m):
            return (
                m.group(0).replace("m_Children: []", f"m_Children:\n  - {{fileID: {grip_tr}}}")
            )

        text2, n = re.subn(tr_block_pat2, add_child2, text, count=1, flags=re.S)
        if n != 1:
            print(f"SKIP cannot patch root children: {prefab}")
            return False
    text = text2

    # GripRig parent transform with children right/left
    qx, qy, qz, qw = 0, 0, 0, 1
    grip_block = f"""--- !u!1 &{grip_go}
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  serializedVersion: 6
  m_Component:
  - component: {{fileID: {grip_tr}}}
  m_Layer: 0
  m_Name: GripRig
  m_TagString: Untagged
  m_Icon: {{fileID: 0}}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!4 &{grip_tr}
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {grip_go}}}
  serializedVersion: 2
  m_LocalRotation: {{x: 0, y: 0, z: 0, w: 1}}
  m_LocalPosition: {{x: 0, y: 0, z: 0}}
  m_LocalScale: {{x: 1, y: 1, z: 1}}
  m_ConstrainProportionsScale: 0
  m_Children:
  - {{fileID: {right_tr}}}
  - {{fileID: {left_tr}}}
  m_Father: {{fileID: {tr_id}}}
  m_LocalEulerAnglesHint: {{x: 0, y: 0, z: 0}}
"""
    right_block = make_empty_go("RightHandGrip", right_go, right_tr, grip_tr, right_pos, right_eu)
    # fix children line in make_empty - already []
    left_block = make_empty_go("LeftHandGrip", left_go, left_tr, grip_tr, left_pos, left_eu)
    mono = f"""--- !u!114 &{mono_id}
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {go_id}}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {GRIP_GUID}, type: 3}}
  m_Name: 
  m_EditorClassIdentifier: Assembly-CSharp::WeaponGripRig
  m_RightHandGrip: {{fileID: {right_tr}}}
  m_LeftHandGrip: {{fileID: {left_tr}}}
"""
    text = text + "\n" + grip_block + right_block + left_block + mono
    prefab.write_text(text, encoding="utf-8")
    print(f"OK weapon: {prefab.name}")
    return True


def inject_foregrip(prefab: Path, m4_seeds: dict | None):
    text = prefab.read_text(encoding="utf-8")
    if "WeaponForeGrip" in text and "m_Name: LeftHandGrip" in text:
        return False
    if "ForeGrip" not in prefab.name:
        return False

    go_id, tr_id, _ = find_root_transform_block(text)
    if not tr_id:
        print(f"SKIP FG no root: {prefab}")
        return False

    idx = 0
    for i in range(5, 0, -1):
        if f"ForeGrip{i}" in prefab.name:
            idx = i
            break

    pos, eu = (0.0, 0.0, 0.0), (0.0, 0.0, 0.0)
    if m4_seeds and idx in m4_seeds.get("fg", {}):
        pos, eu = m4_seeds["fg"][idx]

    left_go, left_tr, mono_id = uid(), uid(), uid()

    go_pat = rf"(--- !u!1 &{go_id}\nGameObject:\n.*?m_Component:\n)((?:  - component: \{{fileID: \d+\}}\n)+)"

    def add_comp(m):
        return m.group(1) + m.group(2) + f"  - component: {{fileID: {mono_id}}}\n"

    text2, n = re.subn(go_pat, add_comp, text, count=1, flags=re.S)
    if n != 1:
        print(f"SKIP FG GO: {prefab}")
        return False
    text = text2

    tr_block_pat = rf"(--- !u!4 &{tr_id}\nTransform:\n.*?m_Children:\n)((?:  - \{{fileID: \d+\}}\n)*)(  m_Father: \{{fileID: 0\}})"

    def add_child(m):
        return m.group(1) + m.group(2) + f"  - {{fileID: {left_tr}}}\n" + m.group(3)

    text2, n = re.subn(tr_block_pat, add_child, text, count=1, flags=re.S)
    if n != 1:
        tr_block_pat2 = rf"(--- !u!4 &{tr_id}\nTransform:\n.*?m_Children: \[\]\n)(  m_Father: \{{fileID: 0\}})"
        text2, n = re.subn(
            tr_block_pat2,
            lambda m: m.group(0).replace("m_Children: []", f"m_Children:\n  - {{fileID: {left_tr}}}"),
            text,
            count=1,
            flags=re.S,
        )
        if n != 1:
            print(f"SKIP FG children: {prefab}")
            return False
    text = text2

    left_block = make_empty_go("LeftHandGrip", left_go, left_tr, tr_id, pos, eu)
    mono = f"""--- !u!114 &{mono_id}
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {go_id}}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {FORE_GUID}, type: 3}}
  m_Name: 
  m_EditorClassIdentifier: Assembly-CSharp::WeaponForeGrip
  m_LeftHandGrip: {{fileID: {left_tr}}}
"""
    text = text + "\n" + left_block + mono
    prefab.write_text(text, encoding="utf-8")
    print(f"OK foregrip: {prefab.name}")
    return True


def main():
    random.seed(42)
    item_map = build_item_map()
    # find M4_ModA_2 seeds for foregrips
    m4_seeds = None
    for guid, data in item_map.items():
        if "Item_Weapon_M4_ModA_2" in data["path"]:
            m4_seeds = data
            break

    count = 0
    for d in EQUIPPED_DIRS:
        if not d.exists():
            continue
        for prefab in d.glob("Equipped_*.prefab"):
            guid = prefab_guid(prefab)
            seeds = item_map.get(guid) if guid else None
            if inject_weapon(prefab, seeds):
                count += 1

    for d in FOREGRIP_DIRS:
        if not d.exists():
            continue
        for prefab in d.glob("*ForeGrip*.prefab"):
            if inject_foregrip(prefab, m4_seeds):
                count += 1

    print(f"Done. Migrated {count} prefabs.")


if __name__ == "__main__":
    main()
