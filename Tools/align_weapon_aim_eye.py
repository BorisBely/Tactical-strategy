#!/usr/bin/env python3
"""Align weapon Ready/NotReady poses: barrel straight (unit forward, 0 pitch/yaw),
Ready height near right eye; restore LeftHandGrip from ItemDefinition Ready IK."""
from __future__ import annotations

import json
import math
import re
from pathlib import Path

import numpy as np
from scipy.spatial.transform import Rotation as SciR

ROOT = Path(r"d:\Unity project\My project 001")
JSONL = ROOT / "Assets/_DebugLogs/WeaponPoseCalibration/WeaponPoseCalib_20260810_232059.jsonl"
INVENTORY = ROOT / "Assets/GameData/Inventory"
PREFABS = ROOT / "Assets/Prefabs/Weapons"
REPORT = ROOT / "Assets/_DebugLogs/WeaponPoseCalibration/align_aim_eye_report.txt"

# Right-eye height above unit root (standing adult ~1.55–1.62 for typical unit scale).
EYE_HEIGHT_M = 1.58
# Slight right offset for right eye vs unit centerline (meters, unit space).
EYE_RIGHT_M = 0.04


def unity_euler_to_rot(eu):
	x, y, z = eu
	return SciR.from_euler("YXZ", [y, x, z], degrees=True)


def rot_to_unity_euler(rot: SciR):
	y, x, z = rot.as_euler("YXZ", degrees=True)
	# normalize to 0..360 like Unity often shows
	def n(a):
		a = a % 360.0
		return a if a >= 0 else a + 360.0

	return (n(x), n(y), n(z))


def parse_vec_asset(text: str, key: str):
	m = re.search(
		rf"{key}: \{{x: ([-\d.eE+]+), y: ([-\d.eE+]+), z: ([-\d.eE]+)\}}", text
	)
	if not m:
		return None
	return tuple(float(m.group(i)) for i in range(1, 4))


def set_vec_asset(text: str, key: str, pos) -> str:
	pat = rf"({re.escape(key)}: )\{{x: [-\d.eE+]+, y: [-\d.eE+]+, z: [-\d.eE]+\}}"
	repl = rf"\1{{x: {pos[0]:.7g}, y: {pos[1]:.7g}, z: {pos[2]:.7g}}}"
	new, n = re.subn(pat, repl, text, count=1)
	return new if n else text


def prefab_guid(path: Path):
	meta = Path(str(path) + ".meta")
	if not meta.exists():
		return None
	m = re.search(r"guid: ([a-f0-9]+)", meta.read_text(encoding="utf-8"))
	return m.group(1) if m else None


def build_item_to_prefab():
	guid_to_prefab = {}
	for prefab in PREFABS.rglob("Equipped_*.prefab"):
		g = prefab_guid(prefab)
		if g:
			guid_to_prefab[g] = prefab
	mapping = {}
	for asset in INVENTORY.rglob("Item_Weapon_*.asset"):
		text = asset.read_text(encoding="utf-8")
		m = re.search(
			r"m_EquippedVisualPrefab: \{fileID: \d+, guid: ([a-f0-9]+), type: 3\}", text
		)
		if not m:
			continue
		prefab = guid_to_prefab.get(m.group(1))
		if prefab:
			mapping[asset.stem] = (asset, prefab)
	return mapping


def set_named_local_trs(prefab_text: str, name: str, pos, euler) -> tuple[str, bool]:
	changed = False
	for gm in re.finditer(
		r"(--- !u!1 &\d+\nGameObject:\n)(.*?)(?=\n--- |\Z)", prefab_text, re.S
	):
		block = gm.group(2)
		if not re.search(rf"m_Name: {re.escape(name)}\s*(\n|$)", block):
			continue
		cm = re.search(r"m_Component:\n((?:\s*- component: \{fileID: \d+\}\n)+)", block)
		if not cm:
			continue
		ids = [int(x) for x in re.findall(r"fileID: (\d+)", cm.group(1))]
		for tid in ids:
			tm = re.search(
				rf"(--- !u!4 &{tid}\nTransform:\n)((?:.*\n)*?)(?=\n--- |\Z)", prefab_text
			)
			if not tm:
				continue
			father_id = re.search(r"m_Father: \{fileID: (\d+)\}", tm.group(2))
			if not father_id:
				continue
			fid = father_id.group(1)
			ft = re.search(
				rf"--- !u!4 &{fid}\nTransform:\n((?:.*\n)*?)(?=\n--- |\Z)", prefab_text
			)
			if not ft:
				continue
			fgid = re.search(r"m_GameObject: \{fileID: (\d+)\}", ft.group(1))
			if not fgid:
				continue
			fn = re.search(
				rf"--- !u!1 &{fgid.group(1)}\nGameObject:\n(?:.*\n)*?  m_Name: ([^\n]+)",
				prefab_text,
			)
			if not fn or fn.group(1).strip() != "GripRig":
				continue

			def repl_pos(m, p=pos):
				nonlocal changed
				changed = True
				return f"{m.group(1)}{{x: {p[0]:.7g}, y: {p[1]:.7g}, z: {p[2]:.7g}}}"

			pat_pos = (
				rf"(--- !u!4 &{tid}\nTransform:\n(?:.*\n)*?\s*m_LocalPosition: )"
				rf"\{{x: [-\d.eE+]+, y: [-\d.eE+]+, z: [-\d.eE]+\}}"
			)
			prefab_text, n = re.subn(pat_pos, repl_pos, prefab_text, count=1)
			if not n:
				continue
			pat_eu = (
				rf"(--- !u!4 &{tid}\nTransform:\n(?:.*\n)*?\s*m_LocalEulerAnglesHint: )"
				rf"\{{x: [-\d.eE+]+, y: [-\d.eE+]+, z: [-\d.eE]+\}}"
			)

			def repl_eu(m, e=euler):
				return f"{m.group(1)}{{x: {e[0]:.7g}, y: {e[1]:.7g}, z: {e[2]:.7g}}}"

			prefab_text, _ = re.subn(pat_eu, repl_eu, prefab_text, count=1)
			q = SciR.from_euler("zxy", [euler[2], euler[0], euler[1]], degrees=True).as_quat()
			qx, qy, qz, qw = q
			pat_q = (
				rf"(--- !u!4 &{tid}\nTransform:\n(?:.*\n)*?\s*m_LocalRotation: )"
				rf"\{{x: [-\d.eE+]+, y: [-\d.eE+]+, z: [-\d.eE+]+, w: [-\d.eE+]+\}}"
			)

			def repl_q(m):
				return f"{m.group(1)}{{x: {qx:.7g}, y: {qy:.7g}, z: {qz:.7g}, w: {qw:.7g}}}"

			prefab_text, _ = re.subn(pat_q, repl_q, prefab_text, count=1)
			return prefab_text, True
	return prefab_text, changed


def vec(d):
	return np.array([d["x"], d["y"], d["z"]], dtype=float)


def compute_aligned_local(sample, eye_height: float):
	"""Weapon local TRS under Hand_R so barrel = unit forward, height ~ eye."""
	hand_eu = vec(sample["rightHandBoneWorldEuler"])
	hand_pos = vec(sample["rightHandBoneWorld"])
	unit_eu = vec(sample["unitRootWorldEuler"])
	unit_pos = vec(sample["unitRootWorld"])
	weapon_pos = vec(sample["weaponWorldPos"])
	barrel_fwd_local = vec(sample.get("barrelInWeaponLocalForward") or {"x": 0, "y": 0, "z": 1})
	if np.linalg.norm(barrel_fwd_local) < 1e-6:
		barrel_fwd_local = np.array([0.0, 0.0, 1.0])
	barrel_fwd_local = barrel_fwd_local / np.linalg.norm(barrel_fwd_local)

	hand_r = unity_euler_to_rot(hand_eu)
	unit_r = unity_euler_to_rot(unit_eu)
	unit_fwd = unit_r.apply([0, 0, 1])
	unit_up = unit_r.apply([0, 1, 0])
	unit_right = unit_r.apply([1, 0, 0])

	# Desired weapon rotation: weapon local +Z (barrel) -> unit forward, up ~ unit up
	# If barrel is weapon.forward (0,0,1), desired weapon rot = LookRotation(unit_fwd, unit_up)
	desired_weapon_r = SciR.from_matrix(
		np.column_stack(
			[
				np.cross(unit_up, unit_fwd),  # right
				unit_up,
				unit_fwd,
			]
		)
	)
	# Orthonormalize in case
	mat = desired_weapon_r.as_matrix()
	fwd = mat[:, 2]
	fwd = fwd / np.linalg.norm(fwd)
	right = np.cross(unit_up, fwd)
	right = right / (np.linalg.norm(right) + 1e-9)
	up = np.cross(fwd, right)
	desired_weapon_r = SciR.from_matrix(np.column_stack([right, up, fwd]))

	# If barrel axis isn't exactly +Z, rotate so barrel_fwd_local maps to unit_fwd
	# R_weapon * barrel_local = unit_fwd
	# For barrel_local = +Z, R_weapon = desired above.

	local_rot = hand_r.inv() * desired_weapon_r
	local_eu = rot_to_unity_euler(local_rot)

	# Position: keep grip near hand — shift weapon so a reference point rises to eye.
	# Use current weapon origin offset from hand, then add world eye correction projected to hand local.
	cur_local_pos = hand_r.inv().apply(weapon_pos - hand_pos)

	barrel_world = vec(sample["barrelWorld"])
	cur_h = float(barrel_world[1] - unit_pos[1])
	# Target: eye height, slightly to the right of unit centerline, same forward depth as now
	target_barrel = unit_pos + unit_up * eye_height + unit_right * EYE_RIGHT_M
	# Keep approximate horizontal distance of barrel from unit in forward plane
	to_barrel = barrel_world - unit_pos
	fwd_dist = float(np.dot(to_barrel, unit_fwd))
	target_barrel = target_barrel + unit_fwd * max(fwd_dist, 0.35)

	delta_world = target_barrel - barrel_world
	# Moving weapon origin by same delta (rigid)
	new_weapon_world = weapon_pos + delta_world
	new_local_pos = hand_r.inv().apply(new_weapon_world - hand_pos)

	# Blend: don't yank local pos more than 12cm from current (keep hand weld)
	delta_local = new_local_pos - cur_local_pos
	max_shift = 0.12
	if np.linalg.norm(delta_local) > max_shift:
		delta_local = delta_local / np.linalg.norm(delta_local) * max_shift
		new_local_pos = cur_local_pos + delta_local

	return tuple(new_local_pos.tolist()), local_eu


def main():
	rows = [
		json.loads(l)
		for l in JSONL.read_text(encoding="utf-8-sig").splitlines()
		if l.strip()
	]
	by = {}
	for r in rows:
		by.setdefault(r["weapon"], {})[r["posture"]] = r

	item_map = build_item_to_prefab()
	report = [f"Source: {JSONL.name}", f"Eye height: {EYE_HEIGHT_M}m", ""]
	updated_assets = 0
	updated_prefabs = 0

	for weapon, postures in sorted(by.items()):
		entry = item_map.get(weapon)
		if entry is None:
			report.append(f"SKIP {weapon}: no prefab/asset map")
			continue
		asset, prefab = entry
		text = asset.read_text(encoding="utf-8")
		orig = text

		# --- Restore LeftHandGrip from authored Ready IK ---
		lpos = parse_vec_asset(text, "m_LeftHandIkReadyLocalPosition")
		leu = parse_vec_asset(text, "m_LeftHandIkReadyLocalEulerAngles")
		prefab_text = prefab.read_text(encoding="utf-8")
		if lpos and leu:
			prefab_text, ok = set_named_local_trs(prefab_text, "LeftHandGrip", lpos, leu)
			if ok:
				prefab.write_text(prefab_text, encoding="utf-8", newline="\n")
				updated_prefabs += 1
				report.append(f"{weapon} LeftHandGrip <- ReadyIK {lpos} eu={leu}")
			else:
				report.append(f"{weapon} LeftHandGrip WRITE FAIL")
		else:
			report.append(f"{weapon} no m_LeftHandIkReady* on asset")

		# --- Align Standing Ready pose ---
		ready = postures.get("Standing_Ready")
		if ready and "rightHandBoneWorldEuler" in ready:
			npos, neu = compute_aligned_local(ready, EYE_HEIGHT_M)
			text = set_vec_asset(text, "m_RightHandReadyLocalPosition", npos)
			text = set_vec_asset(text, "m_RightHandReadyLocalEulerAngles", neu)
			# Mirror to crouch ready if crouch ready was copy of standing (common)
			crouch_ready_pos = parse_vec_asset(text, "m_CrouchRightHandReadyLocalPosition")
			stand_old = parse_vec_asset(orig, "m_RightHandReadyLocalPosition")
			if crouch_ready_pos and stand_old and all(
				abs(a - b) < 1e-4 for a, b in zip(crouch_ready_pos, stand_old)
			):
				text = set_vec_asset(text, "m_CrouchRightHandReadyLocalPosition", npos)
				text = set_vec_asset(text, "m_CrouchRightHandReadyLocalEulerAngles", neu)
			report.append(f"{weapon} Ready pose -> pos={npos} eu={neu}")

		# --- Align Standing NotReady: straight ahead, lower than eye (chest / low ready height) ---
		nr = postures.get("Standing_NotReady")
		if nr and "rightHandBoneWorldEuler" in nr:
			npos, neu = compute_aligned_local(nr, eye_height=1.15)
			text = set_vec_asset(text, "m_RightHandLocalPosition", npos)
			text = set_vec_asset(text, "m_RightHandLocalEulerAngles", neu)
			report.append(f"{weapon} NotReady pose -> pos={npos} eu={neu}")

		if text != orig:
			asset.write_text(text, encoding="utf-8", newline="\n")
			updated_assets += 1

		report.append("")

	body = "\n".join(report) + f"\nUpdated assets={updated_assets} prefabs={updated_prefabs}\n"
	REPORT.write_text(body, encoding="utf-8")
	print(body)


if __name__ == "__main__":
	main()
