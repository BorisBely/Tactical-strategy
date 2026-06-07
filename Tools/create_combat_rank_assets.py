"""Create UnitCombatRankDefinition assets from CombatBalanceTables.md."""
from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets" / "GameData" / "Combat" / "Ranks"
SCRIPT_GUID = "a8f3c2e14b5d6479e0a1b2c3d4e5f607"

RANKS = [
    ("Rank_Recruit", "Новобранец", 35, 40, 35),
    ("Rank_Soldier", "Боец", 50, 50, 50),
    ("Rank_Veteran", "Ветеран", 65, 62, 65),
    ("Rank_Elite", "Элита", 80, 75, 82),
    ("Rank_Specialist", "Специалист", 72, 85, 70),
]


def asset_yaml(name: str, display: str, mark: float, handling: float, recoil: float) -> str:
    return f"""%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 0}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {SCRIPT_GUID}, type: 3}}
  m_Name: {name}
  m_EditorClassIdentifier: Assembly-CSharp::UnitCombatRankDefinition
  m_DisplayName: {display}
  m_Marksmanship: {mark}
  m_WeaponHandling: {handling}
  m_RecoilControl: {recoil}
"""


def meta_yaml(guid: str) -> str:
    return f"""fileFormatVersion: 2
guid: {guid}
NativeFormatImporter:
  externalObjects: {{}}
  mainObjectFileID: 11400000
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""


GUIDS = {
    "Rank_Recruit": "c1a2b3d4e5f60718293a4b5c6d7e8f90",
    "Rank_Soldier": "d2b3c4e5f60718293a4b5c6d7e8f9011",
    "Rank_Veteran": "e3c4d5f60718293a4b5c6d7e8f901122",
    "Rank_Elite": "f4d5e60718293a4b5c6d7e8f90112233",
    "Rank_Specialist": "05e60718293a4b5c6d7e8f9011223344",
}


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    for name, display, mark, handling, recoil in RANKS:
        path = OUT / f"{name}.asset"
        path.write_text(asset_yaml(name, display, mark, handling, recoil), encoding="utf-8", newline="\n")
        meta = OUT / f"{name}.asset.meta"
        if not meta.exists():
            meta.write_text(meta_yaml(GUIDS[name]), encoding="utf-8", newline="\n")
        print(f"Created {name}")


if __name__ == "__main__":
    main()
