#!/usr/bin/env python3
"""
Estimate ItemDefinition weapon poses for GripRig from legacy per-posture IK.

Old: Pose_posture * Ik_posture  (hand target in parent space)
New: Pose' * Ik_ready           (single GripRig = standing Ready IK)

Solve: Pose' = Pose * Ik_posture * inv(Ik_ready)

Standing Ready pose is unchanged. Left-hand per-posture deltas are reported only
(cannot be absorbed into one LeftHandGrip without breaking right-hand match).

Usage:
  python Tools/compensate_weapon_pose_from_legacy_ik.py           # dry-run
  python Tools/compensate_weapon_pose_from_legacy_ik.py --apply   # write assets
"""
from __future__ import annotations

import math
import re
import sys
from pathlib import Path

import numpy as np
from scipy.spatial.transform import Rotation as SciRot

ROOT = Path(r"d:\Unity project\My project 001")
INVENTORY = ROOT / "Assets/GameData/Inventory"
APPLY = "--apply" in sys.argv
# Skip pathological legacy IK (e.g. RPG NotReady) — do not invent wild poses.
MAX_SHIFT_M = 0.25


def git_show(path: Path) -> str | None:
	import subprocess

	try:
		rel = path.resolve().relative_to(ROOT.resolve()).as_posix()
		return subprocess.check_output(
			["git", "show", f"HEAD:{rel}"],
			text=True,
			stderr=subprocess.DEVNULL,
			cwd=str(ROOT),
		)
	except Exception:
		return None


def unity_euler_to_matrix(euler_xyz: tuple[float, float, float]) -> np.ndarray:
	"""Unity Quaternion.Euler = AngleAxis(y,up)*AngleAxis(x,right)*AngleAxis(z,forward)."""
	x, y, z = euler_xyz
	# scipy intrinsic YXZ angles are ordered (y, x, z)
	return SciRot.from_euler("YXZ", [y, x, z], degrees=True).as_matrix()


def matrix_to_unity_euler(m: np.ndarray) -> tuple[float, float, float]:
	y, x, z = SciRot.from_matrix(m).as_euler("YXZ", degrees=True)
	# Match Unity eulerAngles style (0..360)
	return (x % 360.0, y % 360.0, z % 360.0)


def trs(pos, euler) -> np.ndarray:
	m = np.eye(4)
	m[:3, :3] = unity_euler_to_matrix(euler)
	m[:3, 3] = np.asarray(pos, dtype=float)
	return m


def decompose(m: np.ndarray):
	pos = (float(m[0, 3]), float(m[1, 3]), float(m[2, 3]))
	euler = matrix_to_unity_euler(m[:3, :3])
	return pos, euler


def parse_vec(text: str, key: str):
	m = re.search(rf"{key}: \{{x: ([-\d.eE+]+), y: ([-\d.eE+]+), z: ([-\d.eE]+)\}}", text)
	if not m:
		return None
	return (float(m.group(1)), float(m.group(2)), float(m.group(3)))


def fmt_vec(v) -> str:
	return f"{{x: {v[0]:.7g}, y: {v[1]:.7g}, z: {v[2]:.7g}}}"


def replace_vec(text: str, key: str, value) -> str:
	pat = rf"({re.escape(key)}: )\{{x: [-\d.eE+]+, y: [-\d.eE+]+, z: [-\d.eE]+\}}"
	new, n = re.subn(pat, rf"\g<1>{fmt_vec(value)}", text, count=1)
	if n != 1:
		raise RuntimeError(f"replace failed for {key} (n={n})")
	return new


def is_zero(v) -> bool:
	return v is None or all(abs(x) < 1e-8 for x in v)


def ang_delta(a, b) -> float:
	"""Max absolute euler delta in degrees (shortest)."""
	d = 0.0
	for i in range(3):
		x = abs((a[i] - b[i] + 180.0) % 360.0 - 180.0)
		d = max(d, x)
	return d


def compensate(pose_pos, pose_eu, ik_pos, ik_eu, ready_ik_pos, ready_ik_eu):
	if (is_zero(ik_pos) and is_zero(ik_eu)) or (is_zero(ready_ik_pos) and is_zero(ready_ik_eu)):
		return pose_pos, pose_eu, 0.0, 0.0

	pose = trs(pose_pos, pose_eu)
	ik = trs(ik_pos, ik_eu)
	ready = trs(ready_ik_pos, ready_ik_eu)
	new_m = pose @ ik @ np.linalg.inv(ready)
	new_pos, new_eu = decompose(new_m)

	target = (pose @ ik)[:3, 3]
	got = (new_m @ ready)[:3, 3]
	grip_err = float(np.linalg.norm(target - got))
	shift = float(np.linalg.norm(np.asarray(new_pos) - np.asarray(pose_pos)))
	if grip_err > 1e-5:
		raise RuntimeError(f"grip reconstruct err {grip_err}")
	return new_pos, new_eu, shift, grip_err


SLOTS = [
	(
		"m_RightHandLocalPosition",
		"m_RightHandLocalEulerAngles",
		"m_RightHandIkNotReadyLocalPosition",
		"m_RightHandIkNotReadyLocalEulerAngles",
		"Standing/NotReady",
	),
	(
		"m_CrouchRightHandLocalPosition",
		"m_CrouchRightHandLocalEulerAngles",
		"m_CrouchRightHandIkNotReadyLocalPosition",
		"m_CrouchRightHandIkNotReadyLocalEulerAngles",
		"Crouch/NotReady",
	),
	(
		"m_CrouchRightHandReadyLocalPosition",
		"m_CrouchRightHandReadyLocalEulerAngles",
		"m_CrouchRightHandIkReadyLocalPosition",
		"m_CrouchRightHandIkReadyLocalEulerAngles",
		"Crouch/Ready",
	),
	(
		"m_VehicleRightHandLocalPosition",
		"m_VehicleRightHandLocalEulerAngles",
		"m_VehicleRightHandIkNotReadyLocalPosition",
		"m_VehicleRightHandIkNotReadyLocalEulerAngles",
		"Vehicle/NotReady",
	),
	(
		"m_VehicleRightHandReadyLocalPosition",
		"m_VehicleRightHandReadyLocalEulerAngles",
		"m_VehicleRightHandIkReadyLocalPosition",
		"m_VehicleRightHandIkReadyLocalEulerAngles",
		"Vehicle/Ready",
	),
]


def process_asset(path: Path):
	text = path.read_text(encoding="utf-8")
	# Always compensate from git HEAD poses so re-runs are idempotent.
	baseline = git_show(path) or text
	ready_p = parse_vec(baseline, "m_RightHandIkReadyLocalPosition") or parse_vec(text, "m_RightHandIkReadyLocalPosition")
	ready_e = parse_vec(baseline, "m_RightHandIkReadyLocalEulerAngles") or parse_vec(text, "m_RightHandIkReadyLocalEulerAngles")
	if ready_p is None or ready_e is None:
		return False, [f"SKIP {path.name}: no Ready IK"]

	stand_p = parse_vec(baseline, "m_RightHandLocalPosition")
	if is_zero(stand_p) and is_zero(parse_vec(baseline, "m_RightHandReadyLocalPosition")):
		return False, [f"SKIP {path.name}: empty standing pose"]

	lines = [f"=== {path.as_posix()} ==="]
	changed = False
	new_text = text
	max_shift = 0.0

	for pose_pk, pose_ek, ik_pk, ik_ek, label in SLOTS:
		pose_p = parse_vec(baseline, pose_pk)
		pose_e = parse_vec(baseline, pose_ek)
		ik_p = parse_vec(baseline, ik_pk) or parse_vec(text, ik_pk)
		ik_e = parse_vec(baseline, ik_ek) or parse_vec(text, ik_ek)
		if pose_p is None or pose_e is None or ik_p is None or ik_e is None:
			lines.append(f"  {label}: missing fields — skip")
			continue

		new_p, new_e, shift, _ = compensate(pose_p, pose_e, ik_p, ik_e, ready_p, ready_e)
		same = (
			all(abs(new_p[i] - pose_p[i]) < 1e-5 for i in range(3))
			and ang_delta(new_e, pose_e) < 0.05
		)
		if same:
			lines.append(f"  {label}: unchanged")
			continue

		if shift > MAX_SHIFT_M:
			lines.append(
				f"  {label}: SKIP large shift {shift * 1000:.0f}mm "
				f"(legacy IK likely unused/bad) — keep old pose"
			)
			continue

		# Compare against CURRENT file — skip write if already compensated
		cur_p = parse_vec(text, pose_pk)
		cur_e = parse_vec(text, pose_ek)
		already = (
			cur_p is not None
			and cur_e is not None
			and all(abs(new_p[i] - cur_p[i]) < 1e-5 for i in range(3))
			and ang_delta(new_e, cur_e) < 0.05
		)
		if already:
			lines.append(f"  {label}: already compensated")
			continue

		max_shift = max(max_shift, shift)
		lines.append(
			f"  {label}: pos {tuple(round(x, 4) for x in pose_p)} -> {tuple(round(x, 4) for x in new_p)} "
			f"| dPos={shift * 1000:.1f}mm | dEu={ang_delta(new_e, pose_e):.1f}deg"
		)
		new_text = replace_vec(new_text, pose_pk, new_p)
		new_text = replace_vec(new_text, pose_ek, new_e)
		changed = True

	# Left residual (info): old left IK vs Ready left — hands will differ by this in parent-weapon space
	for label, lkey in (
		("Standing/NotReady", "m_LeftHandIkNotReadyLocalPosition"),
		("Crouch/Ready", "m_CrouchLeftHandIkReadyLocalPosition"),
		("Vehicle/Ready", "m_VehicleLeftHandIkReadyLocalPosition"),
	):
		lp = parse_vec(baseline, lkey)
		lr = parse_vec(baseline, "m_LeftHandIkReadyLocalPosition")
		if lp and lr and not is_zero(lp):
			d = float(np.linalg.norm(np.asarray(lp) - np.asarray(lr)))
			if d > 0.005:
				lines.append(f"  left residual ~{label}: {d * 1000:.1f}mm vs Standing Ready Left IK")

	if APPLY and changed:
		path.write_text(new_text, encoding="utf-8", newline="\n")
		lines.append(f"  WROTE (max |dPos|={max_shift * 1000:.1f}mm)")
	elif changed:
		lines.append(f"  dry-run (max |dPos|={max_shift * 1000:.1f}mm) — pass --apply to write")

	return changed, lines


def main():
	# Self-check roundtrip
	for eu in (
		(8.034843, 7.1254754, 263.0723),
		(359.5424, 94.97751, 276.26413),
		(337.84955, 94.18131, 257.7798),
	):
		m = unity_euler_to_matrix(eu)
		back = matrix_to_unity_euler(m)
		err = np.linalg.norm(unity_euler_to_matrix(back) - m)
		if err > 1e-8:
			raise SystemExit(f"euler roundtrip failed {eu} -> {back} err={err}")

	assets = sorted(INVENTORY.rglob("Item_Weapon_*.asset"))
	changed_n = 0
	printed = []
	for asset in assets:
		ch, lines = process_asset(asset)
		if lines and lines[0].startswith("SKIP"):
			continue
		if ch:
			changed_n += 1
		printed.extend(lines)
		printed.append("")

	print("\n".join(printed))
	print(f"{'APPLIED' if APPLY else 'DRY-RUN'}: {changed_n} assets changed (of {len(assets)} scanned)")
	print("Standing Ready pose not touched (GripRig already = Ready IK).")
	print("Left-hand posture IK cannot be fully restored — single LeftHandGrip.")


if __name__ == "__main__":
	main()
