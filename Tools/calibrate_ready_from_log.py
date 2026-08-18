#!/usr/bin/env python3
"""Calibrate Standing Ready weapon poses via MINIMAL world correction.

Uses calib sample: rotate weapon so barrel -> unit forward (0 yaw/pitch),
then small lift toward eye height. Does NOT rebuild pose from LookRotation
(that approach failed in 233107).
"""
from __future__ import annotations

import json
import math
import re
from pathlib import Path

import numpy as np
from scipy.spatial.transform import Rotation as SciR

ROOT = Path(r"d:\Unity project\My project 001")
JSONL = ROOT / "Assets/_DebugLogs/WeaponPoseCalibration/WeaponPoseCalib_20260810_233704.jsonl"
INVENTORY = ROOT / "Assets/GameData/Inventory"
PREFABS = ROOT / "Assets/Prefabs/Weapons"
REPORT = ROOT / "Assets/_DebugLogs/WeaponPoseCalibration/calibrate_ready_233704_report.txt"

EYE_H = 1.58
MAX_POS_SHIFT = 0.08  # meters in hand-local


def unity_eu_to_rot(eu) -> SciR:
	x, y, z = float(eu[0]), float(eu[1]), float(eu[2])
	return SciR.from_euler("YXZ", [y, x, z], degrees=True)


def rot_to_unity_eu(rot: SciR):
	y, x, z = rot.as_euler("YXZ", degrees=True)

	def n(a):
		a = float(a) % 360.0
		return a + 360.0 if a < 0 else a

	return (n(x), n(y), n(z))


def v(d):
	return np.array([float(d["x"]), float(d["y"]), float(d["z"])], dtype=float)


def parse_vec(text, key):
	m = re.search(rf"{re.escape(key)}: \{{x: ([-\d.eE+]+), y: ([-\d.eE+]+), z: ([-\d.eE]+)\}}", text)
	return tuple(float(m.group(i)) for i in range(1, 4)) if m else None


def set_vec(text, key, pos):
	pat = rf"({re.escape(key)}: )\{{x: [-\d.eE+]+, y: [-\d.eE+]+, z: [-\d.eE]+\}}"
	repl = rf"\1{{x: {pos[0]:.7g}, y: {pos[1]:.7g}, z: {pos[2]:.7g}}}"
	new, n = re.subn(pat, repl, text, count=1)
	return new if n else text


def set_grip_trs(prefab_text, name, pos, euler):
	changed = False
	for gm in re.finditer(r"(--- !u!1 &\d+\nGameObject:\n)(.*?)(?=\n--- |\Z)", prefab_text, re.S):
		block = gm.group(2)
		if not re.search(rf"m_Name: {re.escape(name)}\s*(\n|$)", block):
			continue
		cm = re.search(r"m_Component:\n((?:\s*- component: \{fileID: \d+\}\n)+)", block)
		if not cm:
			continue
		for tid in [int(x) for x in re.findall(r"fileID: (\d+)", cm.group(1))]:
			tm = re.search(rf"(--- !u!4 &{tid}\nTransform:\n)((?:.*\n)*?)(?=\n--- |\Z)", prefab_text)
			if not tm:
				continue
			fid = re.search(r"m_Father: \{fileID: (\d+)\}", tm.group(2))
			if not fid:
				continue
			ft = re.search(rf"--- !u!4 &{fid.group(1)}\nTransform:\n((?:.*\n)*?)(?=\n--- |\Z)", prefab_text)
			if not ft:
				continue
			fgid = re.search(r"m_GameObject: \{fileID: (\d+)\}", ft.group(1))
			fn = re.search(
				rf"--- !u!1 &{fgid.group(1)}\nGameObject:\n(?:.*\n)*?  m_Name: ([^\n]+)",
				prefab_text,
			) if fgid else None
			if not fn or fn.group(1).strip() != "GripRig":
				continue

			def rp(m, p=pos):
				nonlocal changed
				changed = True
				return f"{m.group(1)}{{x: {p[0]:.7g}, y: {p[1]:.7g}, z: {p[2]:.7g}}}"

			prefab_text, n = re.subn(
				rf"(--- !u!4 &{tid}\nTransform:\n(?:.*\n)*?\s*m_LocalPosition: )"
				rf"\{{x: [-\d.eE+]+, y: [-\d.eE+]+, z: [-\d.eE]+\}}",
				rp,
				prefab_text,
				count=1,
			)
			if not n:
				continue
			prefab_text, _ = re.subn(
				rf"(--- !u!4 &{tid}\nTransform:\n(?:.*\n)*?\s*m_LocalEulerAnglesHint: )"
				rf"\{{x: [-\d.eE+]+, y: [-\d.eE+]+, z: [-\d.eE]+\}}",
				lambda m, e=euler: f"{m.group(1)}{{x: {e[0]:.7g}, y: {e[1]:.7g}, z: {e[2]:.7g}}}",
				prefab_text,
				count=1,
			)
			q = SciR.from_euler("zxy", [euler[2], euler[0], euler[1]], degrees=True).as_quat()
			prefab_text, _ = re.subn(
				rf"(--- !u!4 &{tid}\nTransform:\n(?:.*\n)*?\s*m_LocalRotation: )"
				rf"\{{x: [-\d.eE+]+, y: [-\d.eE+]+, z: [-\d.eE+]+, w: [-\d.eE+]+\}}",
				lambda m: f"{m.group(1)}{{x: {q[0]:.7g}, y: {q[1]:.7g}, z: {q[2]:.7g}, w: {q[3]:.7g}}}",
				prefab_text,
				count=1,
			)
			return prefab_text, True
	return prefab_text, changed


def prefab_guid(path: Path):
	meta = Path(str(path) + ".meta")
	if not meta.exists():
		return None
	m = re.search(r"guid: ([a-f0-9]+)", meta.read_text(encoding="utf-8"))
	return m.group(1) if m else None


def item_to_prefab():
	g2p = {}
	for prefab in PREFABS.rglob("Equipped_*.prefab"):
		g = prefab_guid(prefab)
		if g:
			g2p[g] = prefab
	out = {}
	for asset in INVENTORY.rglob("Item_Weapon_*.asset"):
		text = asset.read_text(encoding="utf-8")
		m = re.search(
			r"m_EquippedVisualPrefab: \{fileID: \d+, guid: ([a-f0-9]+), type: 3\}", text
		)
		if m and m.group(1) in g2p:
			out[asset.stem] = (asset, g2p[m.group(1)])
	return out


def calibrate_ready(sample):
	"""Minimal correction: align barrel to unit forward, then lift toward eye."""
	hand_r = unity_eu_to_rot(v(sample["rightHandBoneWorldEuler"]))
	hand_pos = v(sample["rightHandBoneWorld"])
	unit_r = unity_eu_to_rot(v(sample["unitRootWorldEuler"]))
	unit_pos = v(sample["unitRootWorld"])
	# Use MEASURED local (applied pose)
	local_pos = v(sample["weaponLocalPos"])
	local_eu = v(sample["weaponLocalEuler"])
	local_r = unity_eu_to_rot(local_eu)
	barrel_local = v(sample.get("barrelInWeaponLocalForward") or {"x": 0, "y": 0, "z": 1})
	n = np.linalg.norm(barrel_local)
	barrel_local = barrel_local / n if n > 1e-8 else np.array([0.0, 0.0, 1.0])

	weapon_world_r = hand_r * local_r
	barrel_world = weapon_world_r.apply(barrel_local)
	barrel_world = barrel_world / np.linalg.norm(barrel_world)

	unit_fwd = unit_r.apply([0, 0, 1])
	unit_fwd = unit_fwd / np.linalg.norm(unit_fwd)
	# Force horizontal aim (0 pitch)
	desired = np.array([unit_fwd[0], 0.0, unit_fwd[2]])
	desired = desired / max(np.linalg.norm(desired), 1e-9)

	# Minimal rotation barrel_world -> desired
	corr, _ = SciR.align_vectors(desired.reshape(1, 3), barrel_world.reshape(1, 3))
	new_weapon_world_r = corr * weapon_world_r
	new_local_r = hand_r.inv() * new_weapon_world_r
	new_eu = rot_to_unity_eu(new_local_r)

	# Height: lift weapon origin so barrel rises toward eye (approx same delta)
	barrel_w = v(sample["barrelWorld"])
	cur_h = float(barrel_w[1] - unit_pos[1])
	dy = EYE_H - cur_h
	# After pitch fix, barrel height also changes ~; use 70% of dy as conservative
	dy *= 0.7
	if abs(dy) > 1e-4:
		delta_world = np.array([0.0, dy, 0.0])
		# also nudge slightly right for right eye
		unit_right = unit_r.apply([1, 0, 0])
		delta_world = delta_world + unit_right * 0.02
		delta_local = hand_r.inv().apply(delta_world)
		if np.linalg.norm(delta_local) > MAX_POS_SHIFT:
			delta_local = delta_local / np.linalg.norm(delta_local) * MAX_POS_SHIFT
		new_pos = local_pos + delta_local
	else:
		new_pos = local_pos

	# Sanity: don't allow local pos jump > 12cm from measured
	if np.linalg.norm(new_pos - local_pos) > 0.12:
		new_pos = local_pos + (new_pos - local_pos) / np.linalg.norm(new_pos - local_pos) * 0.12

	return tuple(float(x) for x in new_pos), new_eu


def main():
	rows = [json.loads(l) for l in JSONL.read_text(encoding="utf-8-sig").splitlines() if l.strip()]
	ready = {r["weapon"]: r for r in rows if r["posture"] == "Standing_Ready"}
	imap = item_to_prefab()
	report = [f"Source: {JSONL.name}", f"EyeH={EYE_H}", "Method: align_vectors barrel correction + lift", ""]
	n_assets = n_prefabs = 0

	for weapon, sample in sorted(ready.items()):
		entry = imap.get(weapon)
		if not entry:
			report.append(f"SKIP {weapon}")
			continue
		asset, prefab = entry
		text = asset.read_text(encoding="utf-8")
		orig = text

		# LeftHandGrip from authored Ready IK
		lpos = parse_vec(text, "m_LeftHandIkReadyLocalPosition")
		leu = parse_vec(text, "m_LeftHandIkReadyLocalEulerAngles")
		pt = prefab.read_text(encoding="utf-8")
		if lpos and leu:
			pt, ok = set_grip_trs(pt, "LeftHandGrip", lpos, leu)
			if ok:
				prefab.write_text(pt, encoding="utf-8", newline="\n")
				n_prefabs += 1
				report.append(f"{weapon} LeftHandGrip <- ReadyIK")

		if "rightHandBoneWorldEuler" not in sample:
			report.append(f"{weapon} SKIP ready: no hand euler in log")
			continue

		npos, neu = calibrate_ready(sample)
		old_pos = parse_vec(text, "m_RightHandReadyLocalPosition")
		old_eu = parse_vec(text, "m_RightHandReadyLocalEulerAngles")
		text = set_vec(text, "m_RightHandReadyLocalPosition", npos)
		text = set_vec(text, "m_RightHandReadyLocalEulerAngles", neu)

		# Mirror crouch ready if it was identical to standing ready
		crp = parse_vec(text, "m_CrouchRightHandReadyLocalPosition")
		if crp and old_pos and all(abs(a - b) < 1e-5 for a, b in zip(crp, old_pos)):
			text = set_vec(text, "m_CrouchRightHandReadyLocalPosition", npos)
			text = set_vec(text, "m_CrouchRightHandReadyLocalEulerAngles", neu)

		if text != orig:
			asset.write_text(text, encoding="utf-8", newline="\n")
			n_assets += 1
		report.append(
			f"{weapon} Ready {old_pos} / {old_eu} -> {tuple(round(x,4) for x in npos)} / "
			f"{tuple(round(x,3) for x in neu)}"
		)
		report.append("")

	body = "\n".join(report) + f"\nUpdated assets={n_assets} prefabs={n_prefabs}\n"
	REPORT.write_text(body, encoding="utf-8")
	print(body)


if __name__ == "__main__":
	main()
