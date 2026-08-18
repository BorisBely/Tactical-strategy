#!/usr/bin/env python3
"""Revert GripRig locals to Standing_Ready values from first good calib log (pre-snap)."""
from __future__ import annotations

import json
import re
from collections import defaultdict
from pathlib import Path

ROOT = Path(r"d:\Unity project\My project 001")
JSONL = ROOT / "Assets/_DebugLogs/WeaponPoseCalibration/WeaponPoseCalib_20260810_223412.jsonl"
INVENTORY = ROOT / "Assets/GameData/Inventory"


def prefab_guid(path: Path) -> str | None:
	meta = Path(str(path) + ".meta")
	if not meta.exists():
		return None
	m = re.search(r"guid: ([a-f0-9]+)", meta.read_text(encoding="utf-8"))
	return m.group(1) if m else None


def build_item_to_prefab():
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


def set_named_local_pos(prefab_text: str, name: str, pos) -> tuple[str, bool]:
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
			pat = (
				rf"(--- !u!4 &{tid}\nTransform:\n(?:.*\n)*?\s*m_LocalPosition: )"
				rf"\{{x: [-\d.eE+]+, y: [-\d.eE+]+, z: [-\d.eE]+\}}"
			)
			repl = rf"\g<1>{{x: {pos[0]:.7g}, y: {pos[1]:.7g}, z: {pos[2]:.7g}}}"
			new_text, n = re.subn(pat, repl, prefab_text, count=1)
			if n:
				return new_text, True
	return prefab_text, False


def main():
	rows = [
		json.loads(line)
		for line in JSONL.read_text(encoding="utf-8-sig").splitlines()
		if line.strip()
	]
	by = defaultdict(list)
	for r in rows:
		by[r["weapon"]].append(r)

	item_to_prefab = build_item_to_prefab()
	updated = 0
	lines = []
	for weapon, samples in sorted(by.items()):
		s = next((x for x in samples if x["posture"] == "Standing_Ready"), None)
		if s is None:
			continue
		prefab = item_to_prefab.get(weapon)
		if prefab is None:
			continue
		rp = s["gripRightLocalPos"]
		lp = s["gripLeftLocalPos"]
		rpos = (rp["x"], rp["y"], rp["z"])
		lpos = (lp["x"], lp["y"], lp["z"])
		text = prefab.read_text(encoding="utf-8")
		new_text, ok_r = set_named_local_pos(text, "RightHandGrip", rpos)
		new_text, ok_l = set_named_local_pos(new_text, "LeftHandGrip", lpos)
		if ok_r or ok_l:
			prefab.write_text(new_text, encoding="utf-8", newline="\n")
			updated += 1
			lines.append(f"REVERTED {weapon} R={rpos} L={lpos} ({prefab.name})")
		else:
			lines.append(f"FAIL {weapon}")

	report = ROOT / "Assets/_DebugLogs/WeaponPoseCalibration/revert_grip_report.txt"
	report.write_text("\n".join(lines) + f"\n\nUpdated: {updated}\n", encoding="utf-8")
	print("\n".join(lines))
	print(f"Updated: {updated}")


if __name__ == "__main__":
	main()
