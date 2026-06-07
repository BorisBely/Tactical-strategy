"""Remove legacy m_Optic* fields and redundant DistanceAimProfile from non-optic attachments."""
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SHOOTING = ROOT / "Assets" / "GameData" / "Shooting"


def patch(path: Path) -> None:
    text = path.read_text(encoding="utf-8")
    changed = False

    if "m_AttachmentType: 0" not in text:
        new_text = re.sub(
            r"\n  m_DistanceAimProfile:.*?(?=\n  m_[A-Z]|\n  m_Suppressed|\Z)",
            "",
            text,
            flags=re.S,
        )
        if new_text != text:
            text = new_text
            changed = True

    if "m_Optic" in text:
        new_text = re.sub(r"\n  m_Optic[A-Za-z]+:.*", "", text)
        if new_text != text:
            text = new_text
            changed = True

    if changed:
        path.write_text(text, encoding="utf-8", newline="\n")
        print(f"Stripped legacy fields: {path.name}")


def main() -> None:
    for path in SHOOTING.rglob("Attachment_*.asset"):
        patch(path)


if __name__ == "__main__":
    main()
