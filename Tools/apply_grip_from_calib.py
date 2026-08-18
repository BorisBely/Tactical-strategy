#!/usr/bin/env python3
"""Apply GripRig local TRS from WeaponPoseCalib JSONL (Standing_Ready hand-in-weapon).

Uses full pos+rotation from logger fields rightHandInWeaponLocal* / leftHandInWeaponLocal*.
Never does position-only snap (that broke orientation / pulled grips off mesh).
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from collections import defaultdict
from pathlib import Path

ROOT = Path(r"d:\Unity project\My project 001")
CALIB_DIR = ROOT / "Assets/_DebugLogs/WeaponPoseCalibration"
INVENTORY = ROOT / "Assets/GameData/Inventory"
MAX_ERR_M = 0.05
MAX_ANG_DEG = 25.0


def parse_vec(obj) -> tuple[float, float, float] | None:
	if not isinstance(obj, dict):
		return None
	try:
		return float(obj["x"]), float(obj["y"]), float(obj["z"])
	except (KeyError, TypeError, ValueError):
		return None


def prefab_guid(path: Path) -> str | None:
	meta = Path(str(path) + ".meta")
	if not meta.exists():
		return None
	m = re.search(r"guid: ([a-f0-9]+)", meta.read_text(encoding="utf-8"))
	return m.group(1) if m else None


def build_item_to_prefab():
	"""Item_Weapon name -> equipped prefab Path."""
	guid_to_prefab = {}
	for prefab in (ROOT / "Assets/Prefabs/Weapons").rglob("Equipped_*.prefab"):
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
			mapping[asset.stem] = prefab
	return mapping


def set_named_local_trs(
	prefab_text: str, name: str, pos, euler
) -> tuple[str, bool]:
	"""Replace m_LocalPosition + m_LocalEulerAngles for Transform of GameObject named `name`."""
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
			# Position
			pat_pos = (
				rf"(--- !u!4 &{tid}\nTransform:\n(?:.*\n)*?\s*m_LocalPosition: )"
				rf"\{{x: [-\d.eE+]+, y: [-\d.eE+]+, z: [-\d.eE]+\}}"
			)

			def repl_pos(m, p=pos):
				nonlocal changed
				changed = True
				return f"{m.group(1)}{{x: {p[0]:.7g}, y: {p[1]:.7g}, z: {p[2]:.7g}}}"

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

			# Unity stores rotation as quaternion; set m_LocalRotation from euler (YXZ Unity order approx via scipy optional).
			# Prefer writing EulerAnglesHint; also rewrite quaternion if present so Play Mode picks it up.
			try:
				from scipy.spatial.transform import Rotation as SciR

				# Unity inspector euler is applied as Z*X*Y in some docs; localEulerAngles use the same
				# as Transform.localEulerAngles which is typically ZXY. SciR 'xyz' with degrees matches
				# Unity's Quaternion.Euler(x,y,z) when using 'xyz' intrinsic... Unity uses ZXY intrinsic.
				q = SciR.from_euler("zxy", [euler[2], euler[0], euler[1]], degrees=True).as_quat()
				# SciPy quat is x,y,z,w
				qx, qy, qz, qw = q
				pat_q = (
					rf"(--- !u!4 &{tid}\nTransform:\n(?:.*\n)*?\s*m_LocalRotation: )"
					rf"\{{x: [-\d.eE+]+, y: [-\d.eE+]+, z: [-\d.eE+]+, w: [-\d.eE+]+\}}"
				)

				def repl_q(m):
					return (
						f"{m.group(1)}{{x: {qx:.7g}, y: {qy:.7g}, z: {qz:.7g}, w: {qw:.7g}}"
					)

				prefab_text, _ = re.subn(pat_q, repl_q, prefab_text, count=1)
			except Exception:
				pass

			return prefab_text, True
	return prefab_text, changed


def latest_jsonl() -> Path | None:
	files = sorted(CALIB_DIR.glob("WeaponPoseCalib_*.jsonl"), key=lambda p: p.stat().st_mtime)
	return files[-1] if files else None


def main():
	ap = argparse.ArgumentParser(description=__doc__)
	ap.add_argument(
		"jsonl",
		nargs="?",
		type=Path,
		help="Calib JSONL path (default: latest under Assets/_DebugLogs/WeaponPoseCalibration)",
	)
	ap.add_argument("--dry-run", action="store_true", help="Report only, do not write prefabs")
	ap.add_argument("--max-err-mm", type=float, default=MAX_ERR_M * 1000.0)
	ap.add_argument("--max-ang-deg", type=float, default=MAX_ANG_DEG)
	args = ap.parse_args()

	jsonl = args.jsonl or latest_jsonl()
	if jsonl is None or not jsonl.exists():
		print("No JSONL found. Pass path or run Play → L first.", file=sys.stderr)
		sys.exit(1)

	max_err = args.max_err_mm / 1000.0
	max_ang = args.max_ang_deg

	rows = [
		json.loads(line)
		for line in jsonl.read_text(encoding="utf-8-sig").splitlines()
		if line.strip()
	]
	by = defaultdict(list)
	for r in rows:
		by[r["weapon"]].append(r)

	item_to_prefab = build_item_to_prefab()
	report = [f"Source: {jsonl}", f"Gates: err<={args.max_err_mm:.0f}mm ang<={max_ang:.0f}°", ""]
	updated = 0

	for weapon, samples in sorted(by.items()):
		s = next((x for x in samples if x["posture"] == "Standing_Ready"), None)
		if s is None:
			continue
		prefab = item_to_prefab.get(weapon)
		if prefab is None:
			report.append(f"SKIP {weapon}: no equipped prefab")
			continue

		text = prefab.read_text(encoding="utf-8")
		if "RightHandGrip" not in text:
			report.append(f"SKIP {weapon}: no RightHandGrip in {prefab.name}")
			continue

		# Prefer logger hand-in-weapon TRS (full orientation).
		r_pos = parse_vec(s.get("rightHandInWeaponLocalPos"))
		r_eu = parse_vec(s.get("rightHandInWeaponLocalEuler"))
		l_pos = parse_vec(s.get("leftHandInWeaponLocalPos"))
		l_eu = parse_vec(s.get("leftHandInWeaponLocalEuler"))
		if r_pos is None or r_eu is None:
			report.append(
				f"SKIP {weapon}: missing rightHandInWeaponLocal* — re-run calib with new logger"
			)
			continue

		er = float(s.get("rightHandToGripError", 999))
		el = float(s.get("leftHandToGripError", 999))
		ar = float(s.get("rightHandToGripAngleDeg", 999))
		al = float(s.get("leftHandToGripAngleDeg", 999))

		new_text = text
		did = False

		if er <= max_err and ar <= max_ang:
			new_text, ok = set_named_local_trs(new_text, "RightHandGrip", r_pos, r_eu)
			if ok:
				did = True
				report.append(
					f"{weapon} RightHandGrip TRS pos={r_pos} eu={r_eu} "
					f"(was {er*1000:.1f}mm / {ar:.1f}°)"
				)
			else:
				report.append(f"FAIL {weapon} RightHandGrip replace")
		else:
			report.append(
				f"SKIP {weapon} RightHandGrip err={er*1000:.0f}mm ang={ar:.0f}° (gate)"
			)

		if l_pos is not None and l_eu is not None and el <= max_err and al <= max_ang:
			new_text, ok = set_named_local_trs(new_text, "LeftHandGrip", l_pos, l_eu)
			if ok:
				did = True
				report.append(
					f"{weapon} LeftHandGrip TRS pos={l_pos} eu={l_eu} "
					f"(was {el*1000:.1f}mm / {al:.1f}°)"
				)
			else:
				report.append(f"FAIL {weapon} LeftHandGrip replace")
		else:
			report.append(
				f"SKIP {weapon} LeftHandGrip err={el*1000:.0f}mm ang={al:.0f}° "
				f"(left IK / gate — keep seed)"
			)

		if did and new_text != text and not args.dry_run:
			prefab.write_text(new_text, encoding="utf-8", newline="\n")
			updated += 1
		elif did and args.dry_run:
			report.append(f"  (dry-run, not written: {prefab.name})")

	out = CALIB_DIR / "apply_grip_from_calib_report.txt"
	CALIB_DIR.mkdir(parents=True, exist_ok=True)
	out.write_text("\n".join(report) + f"\n\nUpdated prefabs: {updated}\n", encoding="utf-8")
	print("\n".join(report))
	print(f"\nUpdated prefabs: {updated}")
	print(f"Report: {out}")


if __name__ == "__main__":
	main()
