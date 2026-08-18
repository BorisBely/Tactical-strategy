#!/usr/bin/env python3
"""Strip orphaned *HandIk* / ForeGripLeftIk YAML from Item_* assets (GripRig + Pose SO are SoT)."""
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(r"d:\Unity project\My project 001\Assets\GameData\Inventory")

# Keep flat Hand_R pose fields (fallback); remove IK / child-name / foregrip-ik leftovers.
DROP = re.compile(
	r"^  m_(?:Crouch|Vehicle)?"
	r"(?:Left|Right)HandIk.*\n"
	r"|^  m_(?:Left|Right)HandIkTarget.*ChildName:.*\n"
	r"|^  m_(?:Crouch|Vehicle)?ForeGrip\d+LeftHandIk.*\n"
	r"|^  m_ForeGrip\d+LeftHandIk.*\n",
	re.M,
)


def main():
	n = 0
	for asset in ROOT.rglob("Item_*.asset"):
		text = asset.read_text(encoding="utf-8")
		new, c = DROP.subn("", text)
		if c:
			asset.write_text(new, encoding="utf-8", newline="\n")
			n += 1
			print(f"stripped {c} lines: {asset.relative_to(ROOT)}")
	print(f"Updated {n} assets")


if __name__ == "__main__":
	main()
