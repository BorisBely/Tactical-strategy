#!/usr/bin/env python3
"""Seed Equipped_*/GripRig/LeftHandGrip from ItemDefinition Ready Left IK.

Does not modify ForeGrip attachment prefabs (authoring stays).
"""
from __future__ import annotations

import re
from pathlib import Path

from scipy.spatial.transform import Rotation as SciR

ROOT = Path(r"d:\Unity project\My project 001")
INVENTORY = ROOT / "Assets/GameData/Inventory"
PREFABS = ROOT / "Assets/Prefabs/Weapons"
FOREGRIPS = PREFABS / "M4/Visuals/Attachments"
REPORT = ROOT / "Assets/_DebugLogs/WeaponPoseCalibration/seed_left_grip_report.txt"

# Scope: AK / M4 / Standalone (same as plan)
SCOPES = ("AK", "M4", "Standalone")


def parse_vec(text, key):
	m = re.search(rf"{re.escape(key)}: \{{x: ([-\d.eE+]+), y: ([-\d.eE+]+), z: ([-\d.eE]+)\}}", text)
	return tuple(float(m.group(i)) for i in range(1, 4)) if m else None


def set_grip_trs(prefab_text, name, pos, euler):
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

			prefab_text, n = re.subn(
				rf"(--- !u!4 &{tid}\nTransform:\n(?:.*\n)*?\s*m_LocalPosition: )"
				rf"\{{x: [-\d.eE+]+, y: [-\d.eE+]+, z: [-\d.eE]+\}}",
				lambda m, p=pos: f"{m.group(1)}{{x: {p[0]:.7g}, y: {p[1]:.7g}, z: {p[2]:.7g}}}",
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
	return prefab_text, False


def prefab_guid(path: Path):
	meta = Path(str(path) + ".meta")
	if not meta.exists():
		return None
	m = re.search(r"guid: ([a-f0-9]+)", meta.read_text(encoding="utf-8"))
	return m.group(1) if m else None


def main():
	g2p = {}
	for prefab in PREFABS.rglob("Equipped_*.prefab"):
		g = prefab_guid(prefab)
		if g:
			g2p[g] = prefab

	lines = ["Seed LeftHandGrip from m_LeftHandIkReady*", ""]
	n_ok = n_skip = 0

	for folder in SCOPES:
		folder_path = INVENTORY / folder
		if not folder_path.is_dir():
			continue
		for asset in sorted(folder_path.glob("Item_Weapon_*.asset")):
			text = asset.read_text(encoding="utf-8")
			m = re.search(
				r"m_EquippedVisualPrefab: \{fileID: \d+, guid: ([a-f0-9]+), type: 3\}", text
			)
			if not m or m.group(1) not in g2p:
				lines.append(f"SKIP {asset.name}: no Equipped prefab")
				n_skip += 1
				continue
			lpos = parse_vec(text, "m_LeftHandIkReadyLocalPosition")
			leu = parse_vec(text, "m_LeftHandIkReadyLocalEulerAngles")
			if not lpos or not leu:
				lines.append(f"SKIP {asset.name}: no Ready Left IK")
				n_skip += 1
				continue
			prefab = g2p[m.group(1)]
			pt = prefab.read_text(encoding="utf-8")
			new_pt, ok = set_grip_trs(pt, "LeftHandGrip", lpos, leu)
			if not ok:
				lines.append(f"FAIL {asset.name} -> {prefab.name}: no GripRig/LeftHandGrip")
				n_skip += 1
				continue
			if new_pt != pt:
				prefab.write_text(new_pt, encoding="utf-8", newline="\n")
			lines.append(f"OK {asset.name} -> {prefab.name} LeftHandGrip {lpos} eu={leu}")
			n_ok += 1

	lines.append("")
	lines.append("=== ForeGrip verify (no rewrite) ===")
	for fg in sorted(FOREGRIPS.glob("Attachment_Visual_M4_ForeGrip*.prefab")):
		t = fg.read_text(encoding="utf-8")
		has_comp = "WeaponForeGrip" in t
		has_left = re.search(r"m_Name: LeftHandGrip\s*(\n|$)", t) is not None
		lines.append(f"{fg.name}: WeaponForeGrip={has_comp} LeftHandGrip={has_left}")

	lines.append("")
	lines.append(f"Seeded={n_ok} skip/fail={n_skip}")
	body = "\n".join(lines) + "\n"
	REPORT.parent.mkdir(parents=True, exist_ok=True)
	REPORT.write_text(body, encoding="utf-8")
	print(body)


if __name__ == "__main__":
	main()
