#!/usr/bin/env python3
"""Offline migration: WeaponPoseDefinition SO + RightHand stance targets on Equipped prefabs.

WeaponPoseDefinition script guid: 23d2715b4bca4e31b44975f27c825066
Prefer Unity menu Polygone/Weapons/Architecture/Migrate… when Editor is available.
"""
from __future__ import annotations

import re
import uuid
from pathlib import Path

ROOT = Path(r"d:\Unity project\My project 001")
INVENTORY = ROOT / "Assets/GameData/Inventory"
POSE_DIR = ROOT / "Assets/GameData/WeaponPoses"
PREFABS = ROOT / "Assets/Prefabs/Weapons"
POSE_SCRIPT_GUID = "23d2715b4bca4e31b44975f27c825066"
SCOPES = ("AK", "M4", "Standalone")

STANCE = {"Standing": 0, "Crouching": 1, "Vehicle": 2}
READY = {"NotReady": 0, "Ready": 1}


def parse_vec(text: str, key: str):
	m = re.search(rf"{re.escape(key)}: \{{x: ([-\d.eE+]+), y: ([-\d.eE+]+), z: ([-\d.eE]+)\}}", text)
	return (float(m.group(1)), float(m.group(2)), float(m.group(3))) if m else (0.0, 0.0, 0.0)


def is_zero(v):
	return abs(v[0]) < 1e-12 and abs(v[1]) < 1e-12 and abs(v[2]) < 1e-12


def prefab_guid(path: Path):
	meta = Path(str(path) + ".meta")
	if not meta.exists():
		return None
	m = re.search(r"guid: ([a-f0-9]+)", meta.read_text(encoding="utf-8"))
	return m.group(1) if m else None


def write_pose_asset(name: str, poses: list[tuple[int, int, tuple, tuple]]) -> str:
	POSE_DIR.mkdir(parents=True, exist_ok=True)
	asset_guid = uuid.uuid4().hex
	path = POSE_DIR / f"WeaponPose_{name}.asset"
	meta = Path(str(path) + ".meta")
	entries = []
	for stance, ready, pos, eu in poses:
		entries.append(
			f"  - Stance: {stance}\n"
			f"    ReadyState: {ready}\n"
			f"    Position: {{x: {pos[0]:.7g}, y: {pos[1]:.7g}, z: {pos[2]:.7g}}}\n"
			f"    EulerAngles: {{x: {eu[0]:.7g}, y: {eu[1]:.7g}, z: {eu[2]:.7g}}}"
		)
	body = (
		"%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n"
		"--- !u!114 &11400000\nMonoBehaviour:\n"
		"  m_ObjectHideFlags: 0\n"
		"  m_CorrespondingSourceObject: {fileID: 0}\n"
		"  m_PrefabInstance: {fileID: 0}\n"
		"  m_PrefabAsset: {fileID: 0}\n"
		"  m_GameObject: {fileID: 0}\n"
		"  m_Enabled: 1\n"
		"  m_EditorHideFlags: 0\n"
		f"  m_Script: {{fileID: 11500000, guid: {POSE_SCRIPT_GUID}, type: 3}}\n"
		f"  m_Name: WeaponPose_{name}\n"
		"  m_EditorClassIdentifier: Assembly-CSharp::WeaponPoseDefinition\n"
		"  m_Poses:\n" + "\n".join(entries) + "\n"
	)
	path.write_text(body, encoding="utf-8", newline="\n")
	meta.write_text(
		"fileFormatVersion: 2\n"
		f"guid: {asset_guid}\n"
		"NativeFormatImporter:\n"
		"  externalObjects: {}\n"
		"  mainObjectFileID: 11400000\n"
		"  userData: \n"
		"  assetBundleName: \n"
		"  assetBundleVariant: \n",
		encoding="utf-8",
		newline="\n",
	)
	return asset_guid


def link_item(asset: Path, pose_guid: str):
	text = asset.read_text(encoding="utf-8")
	line = f"  m_WeaponPoseDefinition: {{fileID: 11400000, guid: {pose_guid}, type: 2}}\n"
	if "m_WeaponPoseDefinition:" in text:
		text = re.sub(
			r"  m_WeaponPoseDefinition: \{fileID: \d+, guid: [a-f0-9]+, type: 2\}\n",
			line,
			text,
			count=1,
		)
	else:
		text = text.replace(
			"  m_EquippedVisualPrefab:",
			line + "  m_EquippedVisualPrefab:",
			1,
		)
	asset.write_text(text, encoding="utf-8", newline="\n")


def next_ids(prefab: str):
	ids = [int(x) for x in re.findall(r"^--- !u!\d+ &(\d+)", prefab, re.M)]
	base = max(ids) + 10 if ids else 900000
	return base, base + 1


def ensure_right_hand_hierarchy(prefab_text: str, seeds: dict) -> str:
	"""Ensure GripRig/RightHand/Stance/Ready|NotReady exist; set local TRS from seeds."""
	# If RightHand root already under GripRig, only update transforms via name match (simple pass).
	if "m_Name: RightHand" in prefab_text and "m_Name: Standing" in prefab_text:
		# Update existing named empties under GripRig by rewriting local pos/eu for Ready/NotReady leaves
		# when followed by known structure — skip complex; Unity migration preferred.
		pass
	return prefab_text


def build_poses_from_item(text: str):
	def g(key):
		return parse_vec(text, key)

	s_nr_p, s_nr_e = g("m_RightHandLocalPosition"), g("m_RightHandLocalEulerAngles")
	s_rd_p, s_rd_e = g("m_RightHandReadyLocalPosition"), g("m_RightHandReadyLocalEulerAngles")
	c_nr_p, c_nr_e = g("m_CrouchRightHandLocalPosition"), g("m_CrouchRightHandLocalEulerAngles")
	c_rd_p, c_rd_e = g("m_CrouchRightHandReadyLocalPosition"), g("m_CrouchRightHandReadyLocalEulerAngles")
	v_nr_p, v_nr_e = g("m_VehicleRightHandLocalPosition"), g("m_VehicleRightHandLocalEulerAngles")
	v_rd_p, v_rd_e = g("m_VehicleRightHandReadyLocalPosition"), g("m_VehicleRightHandReadyLocalEulerAngles")
	if is_zero(c_nr_p) and is_zero(c_nr_e):
		c_nr_p, c_nr_e = s_nr_p, s_nr_e
	if is_zero(c_rd_p) and is_zero(c_rd_e):
		c_rd_p, c_rd_e = s_rd_p, s_rd_e
	if is_zero(v_nr_p) and is_zero(v_nr_e):
		v_nr_p, v_nr_e = s_nr_p, s_nr_e
	if is_zero(v_rd_p) and is_zero(v_rd_e):
		v_rd_p, v_rd_e = s_rd_p, s_rd_e
	return [
		(0, 0, s_nr_p, s_nr_e),
		(0, 1, s_rd_p, s_rd_e),
		(1, 0, c_nr_p, c_nr_e),
		(1, 1, c_rd_p, c_rd_e),
		(2, 0, v_nr_p, v_nr_e),
		(2, 1, v_rd_p, v_rd_e),
	]


def main():
	POSE_DIR.mkdir(parents=True, exist_ok=True)
	folder_meta = POSE_DIR / ".." 
	# folder meta for WeaponPoses
	wp_meta = ROOT / "Assets/GameData/WeaponPoses.meta"
	if not wp_meta.exists():
		wp_meta.write_text(
			"fileFormatVersion: 2\n"
			f"guid: {uuid.uuid4().hex}\n"
			"folderAsset: yes\n"
			"DefaultImporter:\n"
			"  externalObjects: {}\n"
			"  userData: \n"
			"  assetBundleName: \n"
			"  assetBundleVariant: \n",
			encoding="utf-8",
		)

	g2p = {}
	for p in PREFABS.rglob("Equipped_*.prefab"):
		g = prefab_guid(p)
		if g:
			g2p[g] = p

	n = 0
	for scope in SCOPES:
		for asset in sorted((INVENTORY / scope).glob("Item_Weapon_*.asset")):
			text = asset.read_text(encoding="utf-8")
			m = re.search(
				r"m_EquippedVisualPrefab: \{fileID: \d+, guid: ([a-f0-9]+), type: 3\}", text
			)
			if not m:
				continue
			name = asset.stem.replace("Item_Weapon_", "")
			poses = build_poses_from_item(text)
			pose_guid = write_pose_asset(name, poses)
			link_item(asset, pose_guid)
			n += 1
			print(f"OK {asset.name} -> WeaponPose_{name}.asset")

	print(f"Created/linked {n} WeaponPoseDefinition assets.")
	print("Run Unity menu: Polygone/Weapons/Architecture/Migrate Pose Definitions + RightHand Targets")
	print("to seed RightHand stance transforms on prefabs.")


if __name__ == "__main__":
	main()
