"""Bake attachment flat modifiers (foregrips, collimator aim flat) into WeaponAttachmentDefinition assets."""
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SHOOTING = ROOT / "Assets" / "GameData" / "Shooting"

FOREGRIP_MODIFIERS: dict[str, dict[str, float]] = {
    "Attachment_M4_ForeGrip1.asset": {"aim": 1.05, "recoil": 0.90},
    "Attachment_M4_ForeGrip2.asset": {"aim": 1.02, "recoil": 0.92},
    "Attachment_M4_ForeGrip3.asset": {"aim": 0.92, "recoil": 0.97},
    "Attachment_M4_ForeGrip4.asset": {"aim": 0.95, "recoil": 0.95},
    "Attachment_M4_ForeGrip5.asset": {"aim": 0.93, "recoil": 0.94},
}

OPTIC_AIM_FLAT: dict[str, float] = {
    "Attachment_M4_Reddot1.asset": 0.98,
    "Attachment_M4_Reddot2.asset": 0.98,
    "Attachment_M4_Reddot3.asset": 0.98,
    "Attachment_M4_RDC.asset": 0.98,
    "Attachment_M4_Aimpoint.asset": 1.00,
    "Attachment_AK_Reddot4_Rail.asset": 0.95,
    "Attachment_M4_Scope9.asset": 1.55,
}


def patch_float_field(path: Path, field_name: str, value: float) -> None:
    text = path.read_text(encoding="utf-8")
    pattern = rf"(  {re.escape(field_name)}: )[0-9.]+"
    if not re.search(pattern, text):
        raise ValueError(f"{path.name}: missing {field_name}")
    text = re.sub(pattern, rf"\g<1>{value:g}", text, count=1)
    path.write_text(text, encoding="utf-8", newline="\n")


def main() -> None:
    baked = 0
    for rel, fields in FOREGRIP_MODIFIERS.items():
        for match in SHOOTING.rglob(rel.split("/")[-1]):
            patch_float_field(match, "m_AimTimeModifier", fields["aim"])
            patch_float_field(match, "m_RecoilModifier", fields["recoil"])
            print(f"Baked foregrip {match.name}: aim={fields['aim']}, recoil={fields['recoil']}")
            baked += 1

    for rel, aim in OPTIC_AIM_FLAT.items():
        for match in SHOOTING.rglob(rel.split("/")[-1]):
            patch_float_field(match, "m_AimTimeModifier", aim)
            print(f"Baked optic flat aim {match.name}: {aim}")
            baked += 1

    print(f"Done: {baked} attachment modifier patches.")


if __name__ == "__main__":
    main()
