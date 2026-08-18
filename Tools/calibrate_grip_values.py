#!/usr/bin/env python3
"""Calibrate GripRig locals on equipped weapon prefabs from mesh + legacy IK targets.

Does NOT touch ItemDefinition weapon poses (already match measured when IK healthy).
RightHandGrip → near pistol-grip mesh (palm offset).
LeftHandGrip  → legacy LeftHandIkTarget when it has real rotation, else handguard mesh + family euler.
"""
from __future__ import annotations

import math
import re
from pathlib import Path

ROOT = Path(r"d:\Unity project\My project 001")
EQUIPPED = list((ROOT / "Assets/Prefabs/Weapons").rglob("Equipped_*.prefab"))
REPORT = ROOT / "Assets/_DebugLogs/WeaponPoseCalibration/calibrate_grip_values_report.txt"

# Family reference eulers (deg) when legacy IK target has identity rotation.
M4_RIGHT_EU = (8.034843, 7.1254754, 263.0723)
M4_LEFT_EU = (1.41893, 44.277588, 163.04305)
AK_RIGHT_EU = (347.7769, 328.60794, 265.94165)
AK_LEFT_EU = (19.565, 33.029, 143.007)


def is_near_identity_euler(eu, tol=1.0) -> bool:
	if eu is None:
		return True
	x, y, z = eu
	def wrap(a):
		a = a % 360
		return a if a <= 180 else a - 360
	return abs(wrap(x)) < tol and abs(wrap(y)) < tol and abs(wrap(z)) < tol


def find_go_transform(text: str, name: str):
	"""Return (pos, euler, transform_file_id) for first GameObject named exactly name."""
	for m in re.finditer(
		rf"--- !u!1 &(\d+)\nGameObject:\n((?:  .*\n)+)  m_Name: {re.escape(name)}\n",
		text,
	):
		comp_ids = re.findall(r"- component: \{fileID: (\d+)\}", m.group(2))
		for tid in comp_ids:
			tm = re.search(rf"--- !u!4 &{tid}\nTransform:\n((?:  .*\n)+)", text)
			if not tm:
				continue
			tblock = tm.group(1)
			pos_m = re.search(
				r"m_LocalPosition: \{x: ([-\d.eE+]+), y: ([-\d.eE+]+), z: ([-\d.eE]+)\}",
				tblock,
			)
			eu_m = re.search(
				r"m_LocalEulerAnglesHint: \{x: ([-\d.eE+]+), y: ([-\d.eE+]+), z: ([-\d.eE]+)\}",
				tblock,
			)
			if not pos_m:
				continue
			pos = tuple(float(pos_m.group(i)) for i in range(1, 4))
			eu = tuple(float(eu_m.group(i)) for i in range(1, 4)) if eu_m else (0.0, 0.0, 0.0)
			return pos, eu, tid
	return None, None, None


def list_names(text: str) -> list[str]:
	return re.findall(r"^\s+m_Name: (.+)$", text, re.M)


def find_mesh_local(text: str, patterns: list[str]):
	"""Find first mesh-like child name matching any regex, return its local pos (as authored under parent)."""
	names = list_names(text)
	for pat in patterns:
		rx = re.compile(pat, re.I)
		for n in names:
			if n in ("RightHandGrip", "LeftHandGrip", "GripRig"):
				continue
			if rx.search(n):
				pos, eu, _ = find_go_transform(text, n)
				if pos is not None:
					return n, pos, eu
	return None, None, None


def set_named_local_trs(text: str, name: str, pos, euler) -> tuple[str, bool]:
	changed = False
	for gm in re.finditer(
		r"(--- !u!1 &\d+\nGameObject:\n)(.*?)(?=\n--- |\Z)", text, re.S
	):
		block = gm.group(2)
		if not re.search(rf"m_Name: {re.escape(name)}\s*(\n|$)", block):
			continue
		# Prefer GripRig children only: father name checked via transform later
		cm = re.search(r"m_Component:\n((?:\s*- component: \{fileID: \d+\}\n)+)", block)
		if not cm:
			continue
		ids = [int(x) for x in re.findall(r"fileID: (\d+)", cm.group(1))]
		for tid in ids:
			# Confirm father is GripRig
			tm = re.search(
				rf"(--- !u!4 &{tid}\nTransform:\n)((?:.*\n)*?)(?=\n--- |\Z)", text
			)
			if not tm:
				continue
			# Check father GO name is GripRig
			father_id = re.search(r"m_Father: \{fileID: (\d+)\}", tm.group(2))
			if not father_id:
				continue
			fid = father_id.group(1)
			ft = re.search(rf"--- !u!4 &{fid}\nTransform:\n((?:.*\n)*?)(?=\n--- |\Z)", text)
			if not ft:
				continue
			fgid = re.search(r"m_GameObject: \{fileID: (\d+)\}", ft.group(1))
			if not fgid:
				continue
			fn = re.search(
				rf"--- !u!1 &{fgid.group(1)}\nGameObject:\n(?:.*\n)*?  m_Name: ([^\n]+)",
				text,
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
			text, n = re.subn(pat_pos, repl_pos, text, count=1)
			if not n:
				continue

			pat_eu = (
				rf"(--- !u!4 &{tid}\nTransform:\n(?:.*\n)*?\s*m_LocalEulerAnglesHint: )"
				rf"\{{x: [-\d.eE+]+, y: [-\d.eE+]+, z: [-\d.eE]+\}}"
			)

			def repl_eu(m, e=euler):
				return f"{m.group(1)}{{x: {e[0]:.7g}, y: {e[1]:.7g}, z: {e[2]:.7g}}}"

			text, _ = re.subn(pat_eu, repl_eu, text, count=1)

			# quaternion from Unity Euler (ZXY)
			try:
				from scipy.spatial.transform import Rotation as SciR

				q = SciR.from_euler("zxy", [euler[2], euler[0], euler[1]], degrees=True).as_quat()
				qx, qy, qz, qw = q
				pat_q = (
					rf"(--- !u!4 &{tid}\nTransform:\n(?:.*\n)*?\s*m_LocalRotation: )"
					rf"\{{x: [-\d.eE+]+, y: [-\d.eE+]+, z: [-\d.eE+]+, w: [-\d.eE+]+\}}"
				)

				def repl_q(m):
					return f"{m.group(1)}{{x: {qx:.7g}, y: {qy:.7g}, z: {qz:.7g}, w: {qw:.7g}}}"

				text, _ = re.subn(pat_q, repl_q, text, count=1)
			except Exception:
				pass

			return text, True
	return text, changed


def family_of(prefab_name: str) -> str:
	n = prefab_name.lower()
	if "benelli" in n:
		return "standalone"
	if any(x in n for x in ("ak", "rpk")):
		return "ak"
	if any(x in n for x in ("m4_moda", "m16", "mk12", "mk18", "m249", "equipped_m4")):
		return "m4"
	if n.startswith("equipped_m4") or "_m4_" in n:
		return "m4"
	return "standalone"


def calibrate_prefab(path: Path) -> list[str]:
	text = path.read_text(encoding="utf-8")
	if "RightHandGrip" not in text or "GripRig" not in text:
		return [f"SKIP {path.name}: no GripRig"]

	fam = family_of(path.name)
	lines = [f"=== {path.name} ({fam}) ==="]

	# --- Right: mesh pistol grip + family euler ---
	mesh_name, mesh_pos, _ = find_mesh_local(
		text,
		[
			r"SM_Wep.*Grip_\d+",
			r".*_Grip_\d+",
			r"Grip_0",
		],
	)
	cur_r_pos, cur_r_eu, _ = find_go_transform(text, "RightHandGrip")
	if fam == "ak":
		r_eu = AK_RIGHT_EU if is_near_identity_euler(cur_r_eu) else (cur_r_eu or AK_RIGHT_EU)
		# Palm sits slightly to +X of grip mesh for right hand
		if mesh_pos:
			r_pos = (mesh_pos[0] + 0.055, mesh_pos[1] - 0.005, mesh_pos[2] - 0.04)
			lines.append(f"  RightHandGrip from mesh {mesh_name} {mesh_pos} -> {r_pos} eu={r_eu}")
		else:
			r_pos = cur_r_pos or (0.069, -0.327, -0.326)
			lines.append(f"  RightHandGrip keep/fallback pos={r_pos} eu={r_eu}")
	elif fam == "m4":
		r_eu = M4_RIGHT_EU if is_near_identity_euler(cur_r_eu) else (cur_r_eu or M4_RIGHT_EU)
		# M4 baseline already good — keep position, ensure euler
		r_pos = cur_r_pos or (-0.006, 0.01, -0.085)
		lines.append(f"  RightHandGrip M4 baseline pos={r_pos} eu={r_eu}")
	else:
		r_eu = cur_r_eu if cur_r_eu and not is_near_identity_euler(cur_r_eu) else M4_RIGHT_EU
		if mesh_pos:
			r_pos = (mesh_pos[0] + 0.02, mesh_pos[1] + 0.01, mesh_pos[2] - 0.05)
			lines.append(f"  RightHandGrip from mesh {mesh_name} -> {r_pos} eu={r_eu}")
		else:
			r_pos = cur_r_pos or (0.01, 0.01, -0.07)
			lines.append(f"  RightHandGrip standalone pos={r_pos} eu={r_eu}")

	# --- Left: prefer LeftHandIkTarget with real rotation; else handguard mesh ---
	lik_pos, lik_eu, _ = find_go_transform(text, "LeftHandIkTarget")
	hg_name, hg_pos, _ = find_mesh_local(
		text,
		[
			r"SM_Wep.*Handguard_Lower",
			r".*Handguard_Lower",
			r"SM_Wep.*Handguard",
			r".*Handguard",
		],
	)
	cur_l_pos, cur_l_eu, _ = find_go_transform(text, "LeftHandGrip")

	if fam == "ak":
		hg_forward = (
			hg_pos is not None
			and mesh_pos is not None
			and abs(hg_pos[2] - mesh_pos[2]) > 0.05
		)
		if hg_forward:
			# MOD rails: hold on handguard, not receiver-proximal IkTarget
			l_pos = (hg_pos[0] - 0.08, hg_pos[1] + 0.01, hg_pos[2] + 0.03)
			l_eu = (
				lik_eu
				if lik_eu is not None and not is_near_identity_euler(lik_eu)
				else AK_LEFT_EU
			)
			lines.append(f"  LeftHandGrip from handguard {hg_name} -> {l_pos} eu={l_eu}")
		elif lik_pos is not None and not is_near_identity_euler(lik_eu):
			l_pos, l_eu = lik_pos, lik_eu
			lines.append(f"  LeftHandGrip from LeftHandIkTarget {l_pos} eu={l_eu}")
		elif lik_pos is not None:
			l_pos = lik_pos
			l_eu = AK_LEFT_EU
			lines.append(f"  LeftHandGrip from LeftHandIkTarget pos + AK euler {l_pos} eu={l_eu}")
		else:
			l_pos = cur_l_pos or (-0.07, -0.217, 0.027)
			l_eu = AK_LEFT_EU
			lines.append(f"  LeftHandGrip AK fallback {l_pos} eu={l_eu}")
	elif fam == "m4":
		# Keep M4 grip pos; prefer LeftHandIkTarget euler if present
		l_pos = cur_l_pos or (-0.0407, 0.001, 0.2504)
		if lik_eu and not is_near_identity_euler(lik_eu):
			l_eu = lik_eu
			# Slightly pull pos toward IkTarget if far
			if lik_pos and math.dist(l_pos, lik_pos) > 0.04:
				l_pos = (
					(l_pos[0] + lik_pos[0]) * 0.5,
					(l_pos[1] + lik_pos[1]) * 0.5,
					(l_pos[2] + lik_pos[2]) * 0.5,
				)
		else:
			l_eu = cur_l_eu if cur_l_eu and not is_near_identity_euler(cur_l_eu) else M4_LEFT_EU
		lines.append(f"  LeftHandGrip M4 baseline pos={l_pos} eu={l_eu}")
	else:
		# Standalone / broken cluster: IkTarget pos + M4-like left euler (hand bone frame)
		if lik_pos is not None:
			l_pos = lik_pos
		elif hg_pos is not None:
			l_pos = (hg_pos[0] - 0.05, hg_pos[1], hg_pos[2] + 0.05)
		else:
			l_pos = cur_l_pos or (-0.05, 0.02, 0.25)
		if cur_l_eu and not is_near_identity_euler(cur_l_eu):
			l_eu = cur_l_eu
		else:
			l_eu = M4_LEFT_EU
		lines.append(f"  LeftHandGrip standalone pos={l_pos} eu={l_eu}")

	new_text, ok_r = set_named_local_trs(text, "RightHandGrip", r_pos, r_eu)
	new_text, ok_l = set_named_local_trs(new_text, "LeftHandGrip", l_pos, l_eu)
	if ok_r or ok_l:
		path.write_text(new_text, encoding="utf-8", newline="\n")
		lines.append(f"  WROTE right={ok_r} left={ok_l}")
	else:
		lines.append("  FAIL write (GripRig child not found)")
	return lines


def main():
	report = []
	updated = 0
	for path in sorted(EQUIPPED):
		# Skip rocket launchers for this pass unless needed
		if "RocketLauncher" in str(path):
			continue
		lines = calibrate_prefab(path)
		report.extend(lines)
		report.append("")
		if any(l.startswith("  WROTE") for l in lines):
			updated += 1

	REPORT.parent.mkdir(parents=True, exist_ok=True)
	body = "\n".join(report) + f"\nUpdated prefabs: {updated}\n"
	REPORT.write_text(body, encoding="utf-8")
	print(body)


if __name__ == "__main__":
	main()
