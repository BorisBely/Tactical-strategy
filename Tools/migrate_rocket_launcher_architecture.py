#!/usr/bin/env python3
"""Migrate shoulder rocket launchers to WeaponPoseDefinition + full GripRig RightHand tree."""
from __future__ import annotations

import re
import uuid
from pathlib import Path

ROOT = Path(r"d:\Unity project\My project 001")
POSE_FOLDER = ROOT / "Assets/GameData/WeaponPoses"
INV = ROOT / "Assets/GameData/Inventory/RocketLaunchers"
PREFABS = ROOT / "Assets/Prefabs/Weapons/RocketLaunchers/Equipped"
GRIP_GUID = "a8f3c1e29b7d4e6a9c0d2f4b6183e5a1"
POSE_SCRIPT_GUID = "23d2715b4bca4e31b44975f27c825066"

WEAPONS = (
	("Item_Weapon_Rpg7", "Equipped_Rpg7.prefab", "Equipped_Rpg7"),
	("Item_Weapon_DisposableRocketLauncher", "Equipped_DisposableLauncher.prefab", "Equipped_DisposableLauncher"),
)

_uid = 9220000000000000


def uid() -> int:
	global _uid
	_uid += 1
	return _uid


def parse_vec(text, key):
	m = re.search(rf"{re.escape(key)}: \{{x: ([-\d.eE+]+), y: ([-\d.eE+]+), z: ([-\d.eE]+)\}}", text)
	return (float(m.group(1)), float(m.group(2)), float(m.group(3))) if m else (0.0, 0.0, 0.0)


def find_go_block(text: str, name: str) -> str | None:
	for block in re.split(r"(?=^--- !u!1 &)", text, flags=re.M):
		if re.search(rf"^  m_Name: {re.escape(name)}$", block, re.M):
			return block
	return None


def find_named_tr_id(text: str, name: str):
	block = find_go_block(text, name)
	if not block:
		return None
	for cid in re.findall(r"- component: \{fileID: (\d+)\}", block):
		cid = int(cid)
		if f"--- !u!4 &{cid}" in text:
			return cid
	return None


def find_named_go_id(text: str, name: str):
	block = find_go_block(text, name)
	if not block:
		return None
	m = re.match(r"--- !u!1 &(\d+)\n", block)
	return int(m.group(1)) if m else None


def find_named_transform_local(text: str, name: str):
	tr = find_named_tr_id(text, name)
	if tr is None:
		return (0.0, 0.0, 0.0), (0.0, 0.0, 0.0), (0.0, 0.0, 0.0, 1.0)
	tm = re.search(
		rf"--- !u!4 &{tr}\nTransform:\n(?:.*\n)*?"
		rf"  m_LocalRotation: \{{x: ([-\d.eE+]+), y: ([-\d.eE+]+), z: ([-\d.eE+]+), w: ([-\d.eE+]+)\}}\n"
		rf"  m_LocalPosition: \{{x: ([-\d.eE+]+), y: ([-\d.eE+]+), z: ([-\d.eE]+)\}}\n"
		rf"(?:.*\n)*?"
		rf"  m_LocalEulerAnglesHint: \{{x: ([-\d.eE+]+), y: ([-\d.eE+]+), z: ([-\d.eE]+)\}}",
		text,
	)
	if not tm:
		return (0.0, 0.0, 0.0), (0.0, 0.0, 0.0), (0.0, 0.0, 0.0, 1.0)
	quat = (float(tm.group(1)), float(tm.group(2)), float(tm.group(3)), float(tm.group(4)))
	pos = (float(tm.group(5)), float(tm.group(6)), float(tm.group(7)))
	eu = (float(tm.group(8)), float(tm.group(9)), float(tm.group(10)))
	return pos, eu, quat


def make_empty(name, go, tr, father, pos, eu, quat=None, children=None):
	if quat is None:
		quat = (0.0, 0.0, 0.0, 1.0)
	qx, qy, qz, qw = quat
	if children:
		ch = "\n".join(f"  - {{fileID: {c}}}" for c in children)
		children_block = f"m_Children:\n{ch}"
	else:
		children_block = "m_Children: []"
	return (
		f"--- !u!1 &{go}\nGameObject:\n  m_ObjectHideFlags: 0\n"
		f"  m_CorrespondingSourceObject: {{fileID: 0}}\n  m_PrefabInstance: {{fileID: 0}}\n"
		f"  m_PrefabAsset: {{fileID: 0}}\n  serializedVersion: 6\n  m_Component:\n"
		f"  - component: {{fileID: {tr}}}\n  m_Layer: 0\n  m_Name: {name}\n"
		f"  m_TagString: Untagged\n  m_Icon: {{fileID: 0}}\n  m_NavMeshLayer: 0\n"
		f"  m_StaticEditorFlags: 0\n  m_IsActive: 1\n"
		f"--- !u!4 &{tr}\nTransform:\n  m_ObjectHideFlags: 0\n"
		f"  m_CorrespondingSourceObject: {{fileID: 0}}\n  m_PrefabInstance: {{fileID: 0}}\n"
		f"  m_PrefabAsset: {{fileID: 0}}\n  m_GameObject: {{fileID: {go}}}\n  serializedVersion: 2\n"
		f"  m_LocalRotation: {{x: {qx:.7g}, y: {qy:.7g}, z: {qz:.7g}, w: {qw:.7g}}}\n"
		f"  m_LocalPosition: {{x: {pos[0]:.7g}, y: {pos[1]:.7g}, z: {pos[2]:.7g}}}\n"
		f"  m_LocalScale: {{x: 1, y: 1, z: 1}}\n  m_ConstrainProportionsScale: 0\n"
		f"  {children_block}\n  m_Father: {{fileID: {father}}}\n"
		f"  m_LocalEulerAnglesHint: {{x: {eu[0]:.7g}, y: {eu[1]:.7g}, z: {eu[2]:.7g}}}\n"
	)


def write_pose_asset(name: str, item_text: str) -> str:
	pose_name = "WeaponPose_" + name.replace("Item_Weapon_", "")
	pose_path = POSE_FOLDER / f"{pose_name}.asset"
	meta_path = Path(str(pose_path) + ".meta")
	guid = uuid.uuid4().hex
	if meta_path.exists():
		m = re.search(r"guid: ([a-f0-9]+)", meta_path.read_text(encoding="utf-8"))
		if m:
			guid = m.group(1)

	sn = parse_vec(item_text, "m_RightHandLocalPosition")
	se = parse_vec(item_text, "m_RightHandLocalEulerAngles")
	srn = parse_vec(item_text, "m_RightHandReadyLocalPosition")
	sre = parse_vec(item_text, "m_RightHandReadyLocalEulerAngles")
	cn = parse_vec(item_text, "m_CrouchRightHandLocalPosition")
	ce = parse_vec(item_text, "m_CrouchRightHandLocalEulerAngles")
	crn = parse_vec(item_text, "m_CrouchRightHandReadyLocalPosition")
	cre = parse_vec(item_text, "m_CrouchRightHandReadyLocalEulerAngles")
	vn, ve, vrn, vre = sn, se, srn, sre

	pose_path.write_text(
		f"""%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 0}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {POSE_SCRIPT_GUID}, type: 3}}
  m_Name: {pose_name}
  m_EditorClassIdentifier: Assembly-CSharp::WeaponPoseDefinition
  m_Poses:
  - Stance: 0
    ReadyState: 0
    Position: {{x: {sn[0]}, y: {sn[1]}, z: {sn[2]}}}
    EulerAngles: {{x: {se[0]}, y: {se[1]}, z: {se[2]}}}
  - Stance: 0
    ReadyState: 1
    Position: {{x: {srn[0]}, y: {srn[1]}, z: {srn[2]}}}
    EulerAngles: {{x: {sre[0]}, y: {sre[1]}, z: {sre[2]}}}
  - Stance: 1
    ReadyState: 0
    Position: {{x: {cn[0]}, y: {cn[1]}, z: {cn[2]}}}
    EulerAngles: {{x: {ce[0]}, y: {ce[1]}, z: {ce[2]}}}
  - Stance: 1
    ReadyState: 1
    Position: {{x: {crn[0]}, y: {crn[1]}, z: {crn[2]}}}
    EulerAngles: {{x: {cre[0]}, y: {cre[1]}, z: {cre[2]}}}
  - Stance: 2
    ReadyState: 0
    Position: {{x: {vn[0]}, y: {vn[1]}, z: {vn[2]}}}
    EulerAngles: {{x: {ve[0]}, y: {ve[1]}, z: {ve[2]}}}
  - Stance: 2
    ReadyState: 1
    Position: {{x: {vrn[0]}, y: {vrn[1]}, z: {vrn[2]}}}
    EulerAngles: {{x: {vre[0]}, y: {vre[1]}, z: {vre[2]}}}
""",
		encoding="utf-8",
		newline="\n",
	)
	meta_path.write_text(
		f"""fileFormatVersion: 2
guid: {guid}
NativeFormatImporter:
  externalObjects: {{}}
  mainObjectFileID: 11400000
  userData: 
  assetBundleName: 
  assetBundleVariant: 
""",
		encoding="utf-8",
		newline="\n",
	)
	print(f"wrote {pose_path.name} guid={guid}")
	return guid


def patch_item(item_path: Path, pose_guid: str):
	text = item_path.read_text(encoding="utf-8")
	line = f"m_WeaponPoseDefinition: {{fileID: 11400000, guid: {pose_guid}, type: 2}}"
	if "m_WeaponPoseDefinition:" in text:
		text = re.sub(r"m_WeaponPoseDefinition: .*", line, text, count=1)
	else:
		text = text.replace(
			"  m_EquippedVisualPrefab:",
			f"  {line}\n  m_EquippedVisualPrefab:",
			1,
		)
	item_path.write_text(text, encoding="utf-8", newline="\n")
	print(f"linked pose on {item_path.name}")


def seed_prefab(prefab_path: Path, root_name: str):
	text = prefab_path.read_text(encoding="utf-8")
	root_go = find_named_go_id(text, root_name)
	root_tr = find_named_tr_id(text, root_name)
	if root_go is None or root_tr is None:
		print(f"ERROR no root {root_name} in {prefab_path.name}")
		return

	r_pos, r_eu, r_q = find_named_transform_local(text, "RightHandIkTarget")
	n_pos, n_eu, n_q = find_named_transform_local(text, "RightHandIkTarget_NotReady")
	l_pos, l_eu, l_q = find_named_transform_local(text, "LeftHandIkTarget")
	zero = (0.0, 0.0, 0.0)
	iq = (0.0, 0.0, 0.0, 1.0)

	print(f"  {prefab_path.name}: RReady={r_pos} RNotReady={n_pos} Left={l_pos}")

	# IDs
	grip_go, grip_tr = uid(), uid()
	rhg_go, rhg_tr = uid(), uid()
	lhg_go, lhg_tr = uid(), uid()
	rh_go, rh_tr = uid(), uid()
	st_go, st_tr = uid(), uid()
	cr_go, cr_tr = uid(), uid()
	ve_go, ve_tr = uid(), uid()
	sr_go, sr_tr = uid(), uid()
	sn_go, sn_tr = uid(), uid()
	crr_go, crr_tr = uid(), uid()
	crn_go, crn_tr = uid(), uid()
	ver_go, ver_tr = uid(), uid()
	ven_go, ven_tr = uid(), uid()
	comp_id = uid()

	blocks = []
	blocks.append(make_empty("GripRig", grip_go, grip_tr, root_tr, zero, zero, iq, [rhg_tr, lhg_tr, rh_tr]))
	blocks.append(make_empty("RightHandGrip", rhg_go, rhg_tr, grip_tr, r_pos, r_eu, r_q))
	blocks.append(make_empty("LeftHandGrip", lhg_go, lhg_tr, grip_tr, l_pos, l_eu, l_q))
	blocks.append(make_empty("RightHand", rh_go, rh_tr, grip_tr, zero, zero, iq, [st_tr, cr_tr, ve_tr]))
	blocks.append(make_empty("Standing", st_go, st_tr, rh_tr, zero, zero, iq, [sr_tr, sn_tr]))
	blocks.append(make_empty("Crouch", cr_go, cr_tr, rh_tr, zero, zero, iq, [crr_tr, crn_tr]))
	blocks.append(make_empty("Vehicle", ve_go, ve_tr, rh_tr, zero, zero, iq, [ver_tr, ven_tr]))
	blocks.append(make_empty("Ready", sr_go, sr_tr, st_tr, r_pos, r_eu, r_q))
	blocks.append(make_empty("NotReady", sn_go, sn_tr, st_tr, n_pos, n_eu, n_q))
	blocks.append(make_empty("Ready", crr_go, crr_tr, cr_tr, r_pos, r_eu, r_q))
	blocks.append(make_empty("NotReady", crn_go, crn_tr, cr_tr, n_pos, n_eu, n_q))
	blocks.append(make_empty("Ready", ver_go, ver_tr, ve_tr, r_pos, r_eu, r_q))
	blocks.append(make_empty("NotReady", ven_go, ven_tr, ve_tr, n_pos, n_eu, n_q))

	blocks.append(
		f"--- !u!114 &{comp_id}\nMonoBehaviour:\n  m_ObjectHideFlags: 0\n"
		f"  m_CorrespondingSourceObject: {{fileID: 0}}\n  m_PrefabInstance: {{fileID: 0}}\n"
		f"  m_PrefabAsset: {{fileID: 0}}\n  m_GameObject: {{fileID: {root_go}}}\n"
		f"  m_Enabled: 1\n  m_EditorHideFlags: 0\n"
		f"  m_Script: {{fileID: 11500000, guid: {GRIP_GUID}, type: 3}}\n  m_Name: \n"
		f"  m_EditorClassIdentifier: Assembly-CSharp::WeaponGripRig\n"
		f"  m_RightHandGrip: {{fileID: {rhg_tr}}}\n"
		f"  m_LeftHandGrip: {{fileID: {lhg_tr}}}\n"
		f"  m_RightHandRoot: {{fileID: {rh_tr}}}\n"
		f"  m_StandingReady: {{fileID: {sr_tr}}}\n"
		f"  m_StandingNotReady: {{fileID: {sn_tr}}}\n"
		f"  m_CrouchReady: {{fileID: {crr_tr}}}\n"
		f"  m_CrouchNotReady: {{fileID: {crn_tr}}}\n"
		f"  m_VehicleReady: {{fileID: {ver_tr}}}\n"
		f"  m_VehicleNotReady: {{fileID: {ven_tr}}}\n"
	)

	# Add GripRig to root children + component on root GO
	text2, n = re.subn(
		rf"(--- !u!4 &{root_tr}\nTransform:.*?m_Children:\n(?:  - \{{fileID: \d+\}}\n)+)",
		lambda m: m.group(0).rstrip("\n") + f"\n  - {{fileID: {grip_tr}}}\n",
		text,
		count=1,
		flags=re.S,
	)
	if n == 0:
		print(f"ERROR patch root children {prefab_path.name}")
		return
	text = text2

	text2, n = re.subn(
		rf"(--- !u!1 &{root_go}\nGameObject:.*?m_Component:\n(?:  - component: \{{fileID: \d+\}}\n)+)",
		lambda m: m.group(0).rstrip("\n") + f"\n  - component: {{fileID: {comp_id}}}\n",
		text,
		count=1,
		flags=re.S,
	)
	if n == 0:
		print(f"ERROR patch root components {prefab_path.name}")
		return
	text = text2

	# Remove old GripRig if any leftover names from partial runs (none expected)
	text = text.rstrip() + "\n" + "".join(blocks)
	prefab_path.write_text(text, encoding="utf-8", newline="\n")
	print(f"seeded full GripRig on {prefab_path.name}")


def main():
	POSE_FOLDER.mkdir(parents=True, exist_ok=True)
	for item_name, prefab_name, root_name in WEAPONS:
		item_path = INV / f"{item_name}.asset"
		item_text = item_path.read_text(encoding="utf-8")
		guid = write_pose_asset(item_name, item_text)
		patch_item(item_path, guid)
		seed_prefab(PREFABS / prefab_name, root_name)


if __name__ == "__main__":
	main()
