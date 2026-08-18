#!/usr/bin/env python3
"""Seed GripRig/RightHand/{Stance}/{Ready|NotReady} on Equipped prefabs from Item RightHandIk YAML."""
from __future__ import annotations

import re
from pathlib import Path

from scipy.spatial.transform import Rotation as SciR

ROOT = Path(r"d:\Unity project\My project 001")
INVENTORY = ROOT / "Assets/GameData/Inventory"
PREFABS = ROOT / "Assets/Prefabs/Weapons"
GRIP_GUID = "a8f3c1e29b7d4e6a9c0d2f4b6183e5a1"
SCOPES = ("AK", "M4", "Standalone")

_uid = 890000


def uid():
	global _uid
	_uid += 1
	return _uid


def parse_vec(text, key):
	m = re.search(rf"{re.escape(key)}: \{{x: ([-\d.eE+]+), y: ([-\d.eE+]+), z: ([-\d.eE]+)\}}", text)
	return (float(m.group(1)), float(m.group(2)), float(m.group(3))) if m else (0.0, 0.0, 0.0)


def euler_to_quat(eu):
	q = SciR.from_euler("zxy", [eu[2], eu[0], eu[1]], degrees=True).as_quat()
	return q[0], q[1], q[2], q[3]


def make_empty(name, go, tr, father, pos, eu, children=None):
	qx, qy, qz, qw = euler_to_quat(eu)
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


def find_named_tr(text, name):
	lines = text.splitlines()
	i = 0
	while i < len(lines):
		if lines[i].startswith("--- !u!1 &"):
			go_id = int(lines[i].split("&", 1)[1])
			j = i + 1
			comp_ids = []
			found_name = False
			while j < len(lines) and not lines[j].startswith("--- "):
				if lines[j].startswith("  m_Name: "):
					found_name = lines[j][len("  m_Name: ") :].strip() == name
				if "fileID:" in lines[j] and "m_Component" not in lines[j - 1] if j else False:
					pass
				m = re.search(r"- component: \{fileID: (\d+)\}", lines[j])
				if m:
					comp_ids.append(int(m.group(1)))
				j += 1
			if found_name:
				for tid in comp_ids:
					marker = f"--- !u!4 &{tid}"
					if any(l.startswith(marker) for l in lines):
						return tid
			i = j
			continue
		i += 1
	return None


def prefab_guid(path: Path):
	meta = Path(str(path) + ".meta")
	if not meta.exists():
		return None
	m = re.search(r"guid: ([a-f0-9]+)", meta.read_text(encoding="utf-8"))
	return m.group(1) if m else None


def ik(text, key, fb=None):
	v = parse_vec(text, key)
	if v != (0.0, 0.0, 0.0):
		return v
	return parse_vec(text, fb) if fb else (0.0, 0.0, 0.0)


def patch_grip_children(text, grip_tr, rh_tr):
	marker = f"--- !u!4 &{grip_tr}\nTransform:"
	i = text.find(marker)
	if i < 0:
		return None
	j = text.find("\n--- ", i + 5)
	block = text[i:j] if j > 0 else text[i:]
	if f"fileID: {rh_tr}" in block:
		return text
	if "m_Children: []" in block:
		new_block = block.replace("m_Children: []", f"m_Children:\n  - {{fileID: {rh_tr}}}", 1)
	else:
		# insert before m_Father
		new_block = block.replace(
			"  m_Father:",
			f"  - {{fileID: {rh_tr}}}\n  m_Father:",
			1,
		)
	return text[:i] + new_block + (text[j:] if j > 0 else "")


def seed_prefab(prefab: Path, item_text: str) -> bool:
	text = prefab.read_text(encoding="utf-8")
	if "m_StandingReady:" in text:
		print(f"SKIP already: {prefab.name}")
		return False

	grip_tr = find_named_tr(text, "GripRig")
	if grip_tr is None:
		print(f"SKIP no GripRig: {prefab.name}")
		return False

	seeds = {
		("Standing", "Ready"): (
			ik(item_text, "m_RightHandIkReadyLocalPosition"),
			ik(item_text, "m_RightHandIkReadyLocalEulerAngles"),
		),
		("Standing", "NotReady"): (
			ik(item_text, "m_RightHandIkNotReadyLocalPosition"),
			ik(item_text, "m_RightHandIkNotReadyLocalEulerAngles"),
		),
		("Crouch", "Ready"): (
			ik(item_text, "m_CrouchRightHandIkReadyLocalPosition", "m_RightHandIkReadyLocalPosition"),
			ik(item_text, "m_CrouchRightHandIkReadyLocalEulerAngles", "m_RightHandIkReadyLocalEulerAngles"),
		),
		("Crouch", "NotReady"): (
			ik(item_text, "m_CrouchRightHandIkNotReadyLocalPosition", "m_RightHandIkNotReadyLocalPosition"),
			ik(item_text, "m_CrouchRightHandIkNotReadyLocalEulerAngles", "m_RightHandIkNotReadyLocalEulerAngles"),
		),
		("Vehicle", "Ready"): (
			ik(item_text, "m_VehicleRightHandIkReadyLocalPosition", "m_RightHandIkReadyLocalPosition"),
			ik(item_text, "m_VehicleRightHandIkReadyLocalEulerAngles", "m_RightHandIkReadyLocalEulerAngles"),
		),
		("Vehicle", "NotReady"): (
			ik(item_text, "m_VehicleRightHandIkNotReadyLocalPosition", "m_RightHandIkNotReadyLocalPosition"),
			ik(item_text, "m_VehicleRightHandIkNotReadyLocalEulerAngles", "m_RightHandIkNotReadyLocalEulerAngles"),
		),
	}

	rh_go, rh_tr = uid(), uid()
	leaf = {}
	parts = []
	stance_trs = []
	for stance in ("Standing", "Crouch", "Vehicle"):
		sg, st = uid(), uid()
		stance_trs.append(st)
		leaf_trs = []
		leaf_parts = []
		for ready in ("Ready", "NotReady"):
			lg, lt = uid(), uid()
			leaf[(stance, ready)] = lt
			pos, eu = seeds[(stance, ready)]
			leaf_parts.append(make_empty(ready, lg, lt, st, pos, eu))
			leaf_trs.append(lt)
		parts.append(make_empty(stance, sg, st, rh_tr, (0, 0, 0), (0, 0, 0), children=leaf_trs))
		parts.extend(leaf_parts)

	parts.insert(0, make_empty("RightHand", rh_go, rh_tr, grip_tr, (0, 0, 0), (0, 0, 0), children=stance_trs))

	patched = patch_grip_children(text, grip_tr, rh_tr)
	if patched is None:
		print(f"FAIL grip children: {prefab.name}")
		return False
	text = patched + "\n" + "".join(parts)

	sr, snr = leaf[("Standing", "Ready")], leaf[("Standing", "NotReady")]
	cr, cnr = leaf[("Crouch", "Ready")], leaf[("Crouch", "NotReady")]
	vr, vnr = leaf[("Vehicle", "Ready")], leaf[("Vehicle", "NotReady")]
	wire = (
		f"\n  m_RightHandRoot: {{fileID: {rh_tr}}}\n"
		f"  m_StandingReady: {{fileID: {sr}}}\n"
		f"  m_StandingNotReady: {{fileID: {snr}}}\n"
		f"  m_CrouchReady: {{fileID: {cr}}}\n"
		f"  m_CrouchNotReady: {{fileID: {cnr}}}\n"
		f"  m_VehicleReady: {{fileID: {vr}}}\n"
		f"  m_VehicleNotReady: {{fileID: {vnr}}}"
	)
	# Find WeaponGripRig mono block and insert after m_LeftHandGrip
	pat = rf"(guid: {GRIP_GUID}, type: 3\}}(?:\n  m_[^\n]+){{0,8}}\n  m_LeftHandGrip: \{{fileID: \d+\}})"
	text2, n = re.subn(pat, rf"\1{wire}", text, count=1)
	if n != 1:
		# fallback: after m_LeftHandGrip anywhere near GripRig script
		idx = text.find(f"guid: {GRIP_GUID}")
		if idx < 0:
			print(f"FAIL no WeaponGripRig mono: {prefab.name}")
			return False
		lg = text.find("m_LeftHandGrip:", idx)
		if lg < 0:
			print(f"FAIL no LeftHandGrip field: {prefab.name}")
			return False
		eol = text.find("\n", lg)
		text = text[:eol] + wire + text[eol:]
	else:
		text = text2

	prefab.write_text(text, encoding="utf-8", newline="\n")
	print(f"OK {prefab.name}")
	return True


def main():
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
			if not m or m.group(1) not in g2p:
				continue
			if seed_prefab(g2p[m.group(1)], text):
				n += 1
	print(f"Seeded {n} prefabs")


if __name__ == "__main__":
	main()
