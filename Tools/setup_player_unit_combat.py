"""Add UnitCombatStats and UnitCombatCondition to PlayerUnit prefab."""
from __future__ import annotations

from pathlib import Path

PREFAB = Path(__file__).resolve().parents[1] / "Assets" / "Prefabs" / "Characters" / "PlayerUnit.prefab"
STATS_ID = "9988776655443322110"
CONDITION_ID = "9988776655443322111"
STATS_GUID = "7cbb459443b20c04f918012203624e35"
CONDITION_GUID = "df15498df5a70f34cad81fca70031dfc"
RANK_GUID = "d2b3c4e5f60718293a4b5c6d7e8f9011"

STATS_BLOCK = f"""--- !u!114 &{STATS_ID}
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 9176879924895076622}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {STATS_GUID}, type: 3}}
  m_Name: 
  m_EditorClassIdentifier: Assembly-CSharp::UnitCombatStats
  m_RankPreset: {{fileID: 11400000, guid: {RANK_GUID}, type: 2}}
  m_ApplyRankPresetOnAwake: 1
  m_Marksmanship: 50
  m_WeaponHandling: 50
  m_RecoilControl: 50
  m_WorstMarksmanshipDispersionMultiplier: 1.25
  m_BestMarksmanshipDispersionMultiplier: 0.75
  m_WorstHandlingAimTimeMultiplier: 1.25
  m_BestHandlingAimTimeMultiplier: 0.75
  m_WorstRecoilAddedMultiplier: 1.2
  m_BestRecoilAddedMultiplier: 0.8
  m_WorstRecoilRecoveryMultiplier: 0.8
  m_BestRecoilRecoveryMultiplier: 1.2
"""

CONDITION_BLOCK = f"""--- !u!114 &{CONDITION_ID}
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 9176879924895076622}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {CONDITION_GUID}, type: 3}}
  m_Name: 
  m_EditorClassIdentifier: Assembly-CSharp::UnitCombatCondition
  m_ArmsWounded: 0
  m_LegsWounded: 0
  m_HeavyPain: 0
  m_Suppressed: 0
  m_ArmsWoundedDispersionMultiplier: 1.35
  m_ArmsWoundedAimTimeMultiplier: 1.25
  m_LegsWoundedMovingAimTimeMultiplier: 1.2
  m_HeavyPainDispersionMultiplier: 1.2
  m_HeavyPainAimTimeMultiplier: 1.15
  m_SuppressedDispersionMultiplier: 1.15
  m_SuppressedAimTimeMultiplier: 1.2
  m_ArmsWoundedRecoilAddedMultiplier: 1.25
  m_ArmsWoundedRecoilRecoveryMultiplier: 0.85
  m_HeavyPainRecoilAddedMultiplier: 1.1
  m_HeavyPainRecoilRecoveryMultiplier: 0.9
"""


def main() -> None:
    text = PREFAB.read_text(encoding="utf-8")
    if "UnitCombatStats" in text:
        print("PlayerUnit already has UnitCombatStats")
        return

    text = text.replace(
        "  - component: {fileID: 9123456789012345678}\n  m_Layer: 7\n  m_Name: PlayerUnit",
        "  - component: {fileID: 9123456789012345678}\n"
        f"  - component: {{fileID: {STATS_ID}}}\n"
        f"  - component: {{fileID: {CONDITION_ID}}}\n"
        "  m_Layer: 7\n  m_Name: PlayerUnit",
        1,
    )
    text = text.rstrip() + "\n" + STATS_BLOCK + CONDITION_BLOCK
    PREFAB.write_text(text, encoding="utf-8", newline="\n")
    print("PlayerUnit prefab updated")


if __name__ == "__main__":
    main()
