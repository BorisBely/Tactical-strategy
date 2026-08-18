#!/usr/bin/env python3
"""Validate latest WeaponPoseCalib JSONL against hold-value calibration goals."""
from __future__ import annotations

import json
import sys
from pathlib import Path

CALIB_DIR = Path(r"d:\Unity project\My project 001\Assets\_DebugLogs\WeaponPoseCalibration")
# After left-only snap + grip value calib:
R_MM_OK = 60.0
R_DEG_SOFT = 180.0  # right soft IK; angle can stay large — flag only if weapon pose drifts
L_MM_OK = 80.0
L_DEG_OK = 45.0
WEAPON_POS_MM_OK = 5.0
WEAPON_ANG_OK = 5.0


def latest_jsonl() -> Path | None:
	files = sorted(CALIB_DIR.glob("WeaponPoseCalib_*.jsonl"), key=lambda p: p.stat().st_mtime)
	return files[-1] if files else None


def main() -> int:
	path = Path(sys.argv[1]) if len(sys.argv) > 1 else latest_jsonl()
	if path is None or not path.exists():
		print("No WeaponPoseCalib_*.jsonl found. Enter Play and press L first.")
		return 2

	rows = [json.loads(l) for l in path.read_text(encoding="utf-8-sig").splitlines() if l.strip()]
	print(f"Source: {path.name}  samples={len(rows)}")

	fail = 0
	ready = [r for r in rows if r.get("posture") == "Standing_Ready"]
	print(f"\n{'weapon':35s} {'Rmm':>7} {'Rdeg':>6} {'Lmm':>7} {'Ldeg':>6} {'Wpos':>7} {'Wang':>6} flags")
	for r in sorted(ready, key=lambda x: x["weapon"]):
		er = r.get("rightHandToGripError", 0) * 1000
		el = r.get("leftHandToGripError", 0) * 1000
		ar = r.get("rightHandToGripAngleDeg", 0)
		al = r.get("leftHandToGripAngleDeg", 0)
		# weapon authored vs measured
		want = r.get("authoredReadyPos") or {}
		got = r.get("weaponLocalPos") or {}
		dpos = (
			(want.get("x", 0) - got.get("x", 0)) ** 2
			+ (want.get("y", 0) - got.get("y", 0)) ** 2
			+ (want.get("z", 0) - got.get("z", 0)) ** 2
		) ** 0.5 * 1000
		# euler distance rough
		we = r.get("weaponLocalEuler") or {}
		ae = r.get("authoredReadyEuler") or {}
		# use simple abs delta clamped
		def dang(a, b):
			d = abs(a - b) % 360
			return min(d, 360 - d)

		wang = max(
			dang(we.get("x", 0), ae.get("x", 0)),
			dang(we.get("y", 0), ae.get("y", 0)),
			dang(we.get("z", 0), ae.get("z", 0)),
		)
		flags = []
		if er > R_MM_OK:
			flags.append("R_POS")
		if el > L_MM_OK:
			flags.append("L_POS")
		if al > L_DEG_OK:
			flags.append("L_ANG")
		if dpos > WEAPON_POS_MM_OK:
			flags.append("W_POS")
		if wang > WEAPON_ANG_OK:
			flags.append("W_ANG")
		if flags:
			fail += 1
		print(
			f"{r['weapon']:35s} {er:7.1f} {ar:6.1f} {el:7.1f} {al:6.1f} {dpos:7.1f} {wang:6.1f} "
			f"{','.join(flags) if flags else 'OK'}"
		)

	print(f"\nStanding_Ready flagged: {fail}/{len(ready)}")
	print(
		f"Goals: R<{R_MM_OK}mm, L<{L_MM_OK}mm/<{L_DEG_OK}deg, "
		f"weapon dPos<{WEAPON_POS_MM_OK}mm dAng<{WEAPON_ANG_OK}deg"
	)
	if fail:
		print("FAIL — adjust GripRig / weapon pose in tuner, then L again.")
		return 1
	print("PASS")
	return 0


if __name__ == "__main__":
	raise SystemExit(main())
