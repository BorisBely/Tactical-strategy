# Generates Assets/Animations/UnitAnimController.controller (Unity YAML).
from __future__ import annotations

import pathlib


def clip(guid: str) -> str:
    return "{fileID: 7400000, guid: %s, type: 2}" % guid


def bt_ref(fid: int) -> str:
    return "{fileID: %d}" % fid


def cond(mode: int, evt: str, thresh: float) -> str:
    return f"""  - m_ConditionMode: {mode}
    m_ConditionEvent: {evt}
    m_EventTreshold: {thresh}"""


def transition(
    fid: int,
    dst: int,
    lines: list[str],
    duration: float = 0.18,
    exit_time: float = 0.0,
    has_exit: int = 0,
    can_self: int = 0,
) -> str:
    body = "\n".join(lines)
    return f"""--- !u!1101 &{fid}
AnimatorStateTransition:
  m_ObjectHideFlags: 1
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: 
  m_Conditions:
{body}
  m_DstStateMachine: {{fileID: 0}}
  m_DstState: {{fileID: {dst}}}
  m_Solo: 0
  m_Mute: 0
  m_IsExit: 0
  serializedVersion: 3
  m_TransitionDuration: {duration}
  m_TransitionOffset: 0
  m_ExitTime: {exit_time}
  m_HasExitTime: {has_exit}
  m_HasFixedDuration: 1
  m_InterruptionSource: 0
  m_OrderedInterruption: 1
  m_CanTransitionToSelf: {can_self}
"""


def animator_state(fid: int, name: str, motion: str, trans_ids: list[int]) -> str:
    tr = "\n".join(f"  - {{fileID: {t}}}" for t in trans_ids) if trans_ids else ""
    return f"""--- !u!1102 &{fid}
AnimatorState:
  serializedVersion: 6
  m_ObjectHideFlags: 1
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: {name}
  m_Speed: 1
  m_CycleOffset: 0
  m_Transitions:
{tr if tr else " []"}
  m_StateMachineBehaviours: []
  m_Position: {{x: 50, y: 50, z: 0}}
  m_IKOnFeet: 0
  m_WriteDefaultValues: 1
  m_Mirror: 0
  m_SpeedParameterActive: 0
  m_MirrorParameterActive: 0
  m_CycleOffsetParameterActive: 0
  m_TimeParameterActive: 0
  m_Motion: {motion}
  m_Tag: 
  m_SpeedParameter: 
  m_MirrorParameter: 
  m_CycleOffsetParameter: 
  m_TimeParameter: 
"""


def blend_tree_2d_free(fid: int, name: str, children: list[tuple[str, float, float]], px: str, py: str) -> str:
    parts = []
    for guid, x, y in children:
        parts.append(
            f"""  - serializedVersion: 2
    m_Motion: {clip(guid)}
    m_Threshold: 0
    m_Position: {{x: {x}, y: {y}, z: 0}}
    m_TimeScale: 1
    m_CycleOffset: 0
    m_DirectBlendParameter: 
    m_Mirror: 0"""
        )
    childs = "\n".join(parts)
    return f"""--- !u!206 &{fid}
BlendTree:
  m_ObjectHideFlags: 1
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: {name}
  m_Childs:
{childs}
  m_BlendParameter: {px}
  m_BlendParameterY: {py}
  m_MinThreshold: 0
  m_MaxThreshold: 1
  m_UseAutomaticThresholds: 0
  m_NormalizedBlendValues: 0
  m_BlendType: 1
"""


def blend_tree_1d_nested(fid: int, name: str, pairs: list[tuple[float, int]]) -> str:
    parts = []
    for thresh, child_fid in pairs:
        parts.append(
            f"""  - serializedVersion: 2
    m_Motion: {{fileID: {child_fid}}}
    m_Threshold: {thresh}
    m_Position: {{x: 0, y: 0, z: 0}}
    m_TimeScale: 1
    m_CycleOffset: 0
    m_DirectBlendParameter: 
    m_Mirror: 0"""
        )
    childs = "\n".join(parts)
    return f"""--- !u!206 &{fid}
BlendTree:
  m_ObjectHideFlags: 1
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: {name}
  m_Childs:
{childs}
  m_BlendParameter: LocomotionTierBlend
  m_BlendParameterY: Blend
  m_MinThreshold: 0
  m_MaxThreshold: 1
  m_UseAutomaticThresholds: 0
  m_NormalizedBlendValues: 0
  m_BlendType: 0
"""


def blend_tree_1d_simple(fid: int, name: str, pairs: list[tuple[float, str]]) -> str:
    parts = []
    for thresh, guid in pairs:
        parts.append(
            f"""  - serializedVersion: 2
    m_Motion: {clip(guid)}
    m_Threshold: {thresh}
    m_Position: {{x: 0, y: 0, z: 0}}
    m_TimeScale: 1
    m_CycleOffset: 0
    m_DirectBlendParameter: 
    m_Mirror: 0"""
        )
    childs = "\n".join(parts)
    return f"""--- !u!206 &{fid}
BlendTree:
  m_ObjectHideFlags: 1
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: {name}
  m_Childs:
{childs}
  m_BlendParameter: LocomotionTierBlend
  m_BlendParameterY: Blend
  m_MinThreshold: 0
  m_MaxThreshold: 1
  m_UseAutomaticThresholds: 0
  m_NormalizedBlendValues: 0
  m_BlendType: 0
"""


def blend_tree_1d_navspeed(fid: int, name: str, pairs: list[tuple[float, str]]) -> str:
    parts = []
    for thresh, guid in pairs:
        parts.append(
            f"""  - serializedVersion: 2
    m_Motion: {clip(guid)}
    m_Threshold: {thresh}
    m_Position: {{x: 0, y: 0, z: 0}}
    m_TimeScale: 1
    m_CycleOffset: 0
    m_DirectBlendParameter: NavSpeed
    m_Mirror: 0"""
        )
    childs = "\n".join(parts)
    return f"""--- !u!206 &{fid}
BlendTree:
  m_ObjectHideFlags: 1
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: {name}
  m_Childs:
{childs}
  m_BlendParameter: NavSpeed
  m_BlendParameterY: NavSpeed
  m_MinThreshold: 0
  m_MaxThreshold: 1
  m_UseAutomaticThresholds: 0
  m_NormalizedBlendValues: 0
  m_BlendType: 0
"""


def blend_tree_stance_idle(fid: int) -> str:
    # Standing unarmed idle vs prone placeholder (Stance 0 / 2).
    return f"""--- !u!206 &{fid}
BlendTree:
  m_ObjectHideFlags: 1
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: Stand_Idle_ByStance
  m_Childs:
  - serializedVersion: 2
    m_Motion: {clip("d5872b177af21bd4eb5f630ae9b56770")}
    m_Threshold: 0
    m_Position: {{x: 0, y: 0, z: 0}}
    m_TimeScale: 1
    m_CycleOffset: 0
    m_DirectBlendParameter: 
    m_Mirror: 0
  - serializedVersion: 2
    m_Motion: {clip("b10e54e150039dd42a6ab6b95880b2c0")}
    m_Threshold: 2
    m_Position: {{x: 0, y: 0, z: 0}}
    m_TimeScale: 1
    m_CycleOffset: 0
    m_DirectBlendParameter: 
    m_Mirror: 0
  m_BlendParameter: StanceBlend
  m_BlendParameterY: Blend
  m_MinThreshold: 0
  m_MaxThreshold: 2
  m_UseAutomaticThresholds: 0
  m_NormalizedBlendValues: 0
  m_BlendType: 0
"""


def blend_rifle_stand_idle_relax(fid: int) -> str:
    return f"""--- !u!206 &{fid}
BlendTree:
  m_ObjectHideFlags: 1
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: RifleStand_Idle_Relaxed_ByStance
  m_Childs:
  - serializedVersion: 2
    m_Motion: {clip("7713ad82c3c7a924a8dc8002e97869d5")}
    m_Threshold: 0
    m_Position: {{x: 0, y: 0, z: 0}}
    m_TimeScale: 1
    m_CycleOffset: 0
    m_DirectBlendParameter: 
    m_Mirror: 0
  - serializedVersion: 2
    m_Motion: {clip("5fee6100822823242950a95f55b6d8db")}
    m_Threshold: 2
    m_Position: {{x: 0, y: 0, z: 0}}
    m_TimeScale: 1
    m_CycleOffset: 0
    m_DirectBlendParameter: 
    m_Mirror: 0
  m_BlendParameter: StanceBlend
  m_BlendParameterY: Blend
  m_MinThreshold: 0
  m_MaxThreshold: 2
  m_UseAutomaticThresholds: 0
  m_NormalizedBlendValues: 0
  m_BlendType: 0
"""


def blend_rifle_stand_idle_ready(fid: int) -> str:
    return f"""--- !u!206 &{fid}
BlendTree:
  m_ObjectHideFlags: 1
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: RifleStand_Idle_Ready_ByStance
  m_Childs:
  - serializedVersion: 2
    m_Motion: {clip("06fd48e0f9a5d4e4bb88bb0c0fba2993")}
    m_Threshold: 0
    m_Position: {{x: 0, y: 0, z: 0}}
    m_TimeScale: 1
    m_CycleOffset: 0
    m_DirectBlendParameter: 
    m_Mirror: 0
  - serializedVersion: 2
    m_Motion: {clip("02310368c2eb6a64ca8d1e59650f1a6d")}
    m_Threshold: 2
    m_Position: {{x: 0, y: 0, z: 0}}
    m_TimeScale: 1
    m_CycleOffset: 0
    m_DirectBlendParameter: 
    m_Mirror: 0
  m_BlendParameter: StanceBlend
  m_BlendParameterY: Blend
  m_MinThreshold: 0
  m_MaxThreshold: 2
  m_UseAutomaticThresholds: 0
  m_NormalizedBlendValues: 0
  m_BlendType: 0
"""


def blend_aim_pitch(fid: int, name: str, guids: tuple[str, str, str]) -> str:
    down, mid, up = guids
    return f"""--- !u!206 &{fid}
BlendTree:
  m_ObjectHideFlags: 1
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: {name}
  m_Childs:
  - serializedVersion: 2
    m_Motion: {clip(down)}
    m_Threshold: -1
    m_Position: {{x: 0, y: 0, z: 0}}
    m_TimeScale: 1
    m_CycleOffset: 0
    m_DirectBlendParameter: 
    m_Mirror: 0
  - serializedVersion: 2
    m_Motion: {clip(mid)}
    m_Threshold: 0
    m_Position: {{x: 0, y: 0, z: 0}}
    m_TimeScale: 1
    m_CycleOffset: 0
    m_DirectBlendParameter: 
    m_Mirror: 0
  - serializedVersion: 2
    m_Motion: {clip(up)}
    m_Threshold: 1
    m_Position: {{x: 0, y: 0, z: 0}}
    m_TimeScale: 1
    m_CycleOffset: 0
    m_DirectBlendParameter: 
    m_Mirror: 0
  m_BlendParameter: AimPitch
  m_BlendParameterY: Blend
  m_MinThreshold: -1
  m_MaxThreshold: 1
  m_UseAutomaticThresholds: 0
  m_NormalizedBlendValues: 0
  m_BlendType: 0
"""


def main() -> None:
    # IDs
    AC = 9100000
    BASE_SM = 110700000
    SM_UNARMED = 110700200
    SM_RIFLE_STANDING = 110700201
    SM_RIFLE_CROUCH = 110700202
    AIM_STAND_SM = 110700110
    AIM_CROUCH_SM = 110700111

    ST_STAND_IDLE = 772010001
    ST_STAND_LOCO = 772010002
    ST_CROUCH_IDLE = 772010003
    ST_CROUCH_LOCO = 772010004
    ST_PRONE_UN_MOVE = 772010005

    RF_SPRINT = 772020007
    RF_WALK_RUN = 772020005
    RF_WALK_RUN_RDY = 772020006
    RF_STAND_IDLE = 772020001
    RF_STAND_IDLE_RDY = 772020002
    RF_CROUCH_IDLE = 772020003
    RF_CROUCH_IDLE_RDY = 772020004
    RF_CROUCH_MOVE = 772020008

    AIM_STAND_ST = 110200210
    AIM_CROUCH_ST = 110200211

    # Blend trees
    BT_UNARM_ST_LOCO = 206900010
    BT_STAND_IDLE_STANCE = 206900011
    BT_RF_WALK_RELAX = 206900020
    BT_RF_WALK_AIM = 206900080
    BT_RF_CROUCH_MOV = 206900030
    BT_RF_RUN_RELAX = 206900061
    BT_RF_JOG_AIM = 206900060
    BT_RF_TIER_RELAX = 206900064
    BT_RF_TIER_READY = 206900065
    BT_AIM_STAND = 206900070
    BT_AIM_CROUCH = 206900071
    BT_RF_ST_IDLE_REL = 206900090
    BT_RF_ST_IDLE_RDY = 206900091

    walk_relax = [
        ("b9076fe714ede244d80765f9f07accbc", 0.0, 1.0),
        ("27f176de444acb7488cc6e40fbf504cc", 0.0, -1.0),
        ("54d07a40e15facf4485ad1c5b7ea913a", -1.0, 0.0),
        ("e5a4135efa331fa4295100279c146b2b", 1.0, 0.0),
        ("b912e2c7081217c4bbaf3f0bfb72136c", -0.707, 0.707),
        ("9859b65c380befc48aff8511a755cb89", 0.707, 0.707),
        ("717c9d8b0a0a24b44b2255adf9410275", -0.707, -0.707),
        ("57d580eb8946f8b409c5abcfed9fdd5b", 0.707, -0.707),
    ]
    walk_aim = [
        ("151ce115b42398040b563825b8f471d5", 0.0, 1.0),
        ("6d680166123aef345aa44442dc80d97c", 0.0, -1.0),
        ("9621160827f56014ba12f613a67fc554", -1.0, 0.0),
        ("4fe6675f0c94676458d8e25545b16d6a", 1.0, 0.0),
        ("a62c242b01c56a54c988d0333b01448f", -0.707, 0.707),
        ("58a2e0a6b9e9ec54a984cdec330c14d9", 0.707, 0.707),
        ("fedce2a5d6f31844494eec371769e45e", -0.707, -0.707),
        ("c9410be1500d89b43b3c9bf5d276a77e", 0.707, -0.707),
    ]
    jog_aim = [
        ("ef0d608f976ccdc419787abca57a97b4", 0.0, 1.0),
        ("a6bc3df506874d24980f0e5e28748a85", 0.0, -1.0),
        ("d493a77656e9f024b951ce6d2b8ff127", -1.0, 0.0),
        ("d1d2716994400624e93283a559152883", 1.0, 0.0),
        ("0047ce082f667a7489ba49993977652b", -0.707, 0.707),
        ("4401343e61030d7469817953deaadd52", 0.707, 0.707),
        ("55b1b5e03981dc143ac1cff4ee240f8d", -0.707, -0.707),
        ("1fb89354b326b664baf0e80935766e73", 0.707, -0.707),
    ]
    run_relax = [
        ("b192b0cf77cc38847b7e8603caf03edf", 0.0, 1.0),
        ("27f176de444acb7488cc6e40fbf504cc", 0.0, -1.0),
        ("54d07a40e15facf4485ad1c5b7ea913a", -1.0, 0.0),
        ("e5a4135efa331fa4295100279c146b2b", 1.0, 0.0),
        ("b912e2c7081217c4bbaf3f0bfb72136c", -0.707, 0.707),
        ("9859b65c380befc48aff8511a755cb89", 0.707, 0.707),
        ("717c9d8b0a0a24b44b2255adf9410275", -0.707, -0.707),
        ("57d580eb8946f8b409c5abcfed9fdd5b", 0.707, -0.707),
    ]
    crouch_mov = [
        ("ca2f75c942aca9c4f8a73252e60c029a", 0.0, 1.0),
        ("77707169ba2b65343acd9578c81c82b0", 0.0, -1.0),
        ("2a30bd6f40aa7024faa98b42307fa384", -1.0, 0.0),
        ("a883e5a9f5fa6894fab76568fce6cbda", 1.0, 0.0),
        ("7e6ce4a770fdd1148a50ef08328c8866", -0.707, 0.707),
        ("4f2f48e6c8c7a7e4f8de728214fc39e9", 0.707, 0.707),
        ("35d178692c57b1e4283ef9c6b45ed717", -0.707, -0.707),
        ("19b8688c7ad09da4899bdb3c83884339", 0.707, -0.707),
    ]

    chunks: list[str] = []
    chunks.append("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n")

    # NavSpeed is normalized by sprint speed in UnitClickToMove / UnitNavLocomotionDriver.
    # Keep these thresholds aligned with the default movement speeds.
    UNARMED_WALK_NAV = round(1.5 / 7.25, 3)
    UNARMED_RUN_NAV = round(3.5 / 7.25, 3)
    UNARMED_SPRINT_NAV = 1.0
    CROUCH_WALK_NAV = round(1.15 / 7.25, 3)

    # AnimatorController
    chunks.append(f"""--- !u!91 &{AC}
AnimatorController:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: UnitAnimController
  serializedVersion: 5
  m_AnimatorParameters:
  - m_Name: NavSpeed
    m_Type: 1
    m_DefaultFloat: 0
    m_DefaultInt: 0
    m_DefaultBool: 0
    m_Controller: {{fileID: {AC}}}
  - m_Name: NavStrafe
    m_Type: 1
    m_DefaultFloat: 0
    m_DefaultInt: 0
    m_DefaultBool: 0
    m_Controller: {{fileID: {AC}}}
  - m_Name: NavForward
    m_Type: 1
    m_DefaultFloat: 1
    m_DefaultInt: 0
    m_DefaultBool: 0
    m_Controller: {{fileID: {AC}}}
  - m_Name: LocomotionTier
    m_Type: 3
    m_DefaultFloat: 0
    m_DefaultInt: 0
    m_DefaultBool: 0
    m_Controller: {{fileID: {AC}}}
  - m_Name: LocomotionTierBlend
    m_Type: 1
    m_DefaultFloat: 0
    m_DefaultInt: 0
    m_DefaultBool: 0
    m_Controller: {{fileID: {AC}}}
  - m_Name: Stance
    m_Type: 3
    m_DefaultFloat: 0
    m_DefaultInt: 0
    m_DefaultBool: 0
    m_Controller: {{fileID: {AC}}}
  - m_Name: StanceBlend
    m_Type: 1
    m_DefaultFloat: 0
    m_DefaultInt: 0
    m_DefaultBool: 0
    m_Controller: {{fileID: {AC}}}
  - m_Name: WeaponMode
    m_Type: 3
    m_DefaultFloat: 0
    m_DefaultInt: 0
    m_DefaultBool: 0
    m_Controller: {{fileID: {AC}}}
  - m_Name: WeaponReady
    m_Type: 4
    m_DefaultFloat: 0
    m_DefaultInt: 0
    m_DefaultBool: 0
    m_Controller: {{fileID: {AC}}}
  - m_Name: AimPitch
    m_Type: 1
    m_DefaultFloat: 0
    m_DefaultInt: 0
    m_DefaultBool: 0
    m_Controller: {{fileID: {AC}}}
  - m_Name: IsReloadingWeapon
    m_Type: 4
    m_DefaultFloat: 0
    m_DefaultInt: 0
    m_DefaultBool: 0
    m_Controller: {{fileID: {AC}}}
  - m_Name: IsCyclingBolt
    m_Type: 4
    m_DefaultFloat: 0
    m_DefaultInt: 0
    m_DefaultBool: 0
    m_Controller: {{fileID: {AC}}}
  - m_Name: IsLoadingMagazine
    m_Type: 4
    m_DefaultFloat: 0
    m_DefaultInt: 0
    m_DefaultBool: 0
    m_Controller: {{fileID: {AC}}}
  m_AnimatorLayers:
  - serializedVersion: 5
    m_Name: Base Layer
    m_StateMachine: {{fileID: {BASE_SM}}}
    m_Mask: {{fileID: 0}}
    m_Motions: []
    m_Behaviours: []
    m_BlendingMode: 0
    m_SyncedLayerIndex: -1
    m_DefaultWeight: 0
    m_IKPass: 0
    m_SyncedLayerAffectsTiming: 0
    m_Controller: {{fileID: {AC}}}
  - serializedVersion: 5
    m_Name: Aim_Point_U90-D90
    m_StateMachine: {{fileID: {AIM_STAND_SM}}}
    m_Mask: {{fileID: 0}}
    m_Motions: []
    m_Behaviours: []
    m_BlendingMode: 0
    m_SyncedLayerIndex: -1
    m_DefaultWeight: 0
    m_IKPass: 1
    m_SyncedLayerAffectsTiming: 0
    m_Controller: {{fileID: {AC}}}
""")

    # Blend trees
    chunks.append(
        blend_tree_1d_navspeed(
            BT_UNARM_ST_LOCO,
            "UnarmedStandLocomotionFwd",
            [
                (UNARMED_WALK_NAV, "f0f402e7359a7df4aa0b5188149a303f"),
                (UNARMED_RUN_NAV, "721d5c53712fe7b4fbc838214cf349ee"),
                (UNARMED_SPRINT_NAV, "54e0e223225dfd64d8e6e78092e3f473"),
            ],
        )
    )
    chunks.append(blend_tree_2d_free(BT_RF_WALK_RELAX, "RifleStandWalkRelax", walk_relax, "NavStrafe", "NavForward"))
    chunks.append(blend_tree_2d_free(BT_RF_WALK_AIM, "RifleStandWalkAim", walk_aim, "NavStrafe", "NavForward"))
    chunks.append(blend_tree_2d_free(BT_RF_CROUCH_MOV, "RifleCrouchMoveAim", crouch_mov, "NavStrafe", "NavForward"))
    chunks.append(blend_tree_2d_free(BT_RF_RUN_RELAX, "RifleStandRunRelax", run_relax, "NavStrafe", "NavForward"))
    chunks.append(blend_tree_2d_free(BT_RF_JOG_AIM, "RifleStandJogAim", jog_aim, "NavStrafe", "NavForward"))
    chunks.append(blend_tree_1d_nested(BT_RF_TIER_RELAX, "RifleTierWalkRunRelax", [(0.0, BT_RF_WALK_RELAX), (1.0, BT_RF_RUN_RELAX)]))
    chunks.append(blend_tree_1d_nested(BT_RF_TIER_READY, "RifleTierWalkRunReady", [(0.0, BT_RF_WALK_AIM), (1.0, BT_RF_JOG_AIM)]))
    chunks.append(blend_rifle_stand_idle_relax(BT_RF_ST_IDLE_REL))
    chunks.append(blend_rifle_stand_idle_ready(BT_RF_ST_IDLE_RDY))
    chunks.append(
        blend_aim_pitch(
            BT_AIM_STAND,
            "Stand_Aim_Pitch_U90_D90",
            ("336b5266550d15249b97b624a20e6679", "06fd48e0f9a5d4e4bb88bb0c0fba2993", "b88399dc04d5b194680063d8575f7caa"),
        )
    )
    chunks.append(
        blend_aim_pitch(
            BT_AIM_CROUCH,
            "Crouch_Aim_Pitch_U90_D90",
            ("e1406503f976af9458795d6789d8cc0a", "02310368c2eb6a64ca8d1e59650f1a6d", "3c7a48e2a48fb7241addbddb88957308"),
        )
    )

    # --- Build Any State + internal transitions ---
    tid = 773000001

    def reg_trans(dst: int, conds: list[tuple[int, str, float]], dur: float = 0.18, can_self: int = 0) -> int:
        nonlocal tid
        fid = tid
        tid += 1
        lines = "\n".join(cond(c, e, t) for (c, e, t) in conds)
        chunks.append(transition(fid, dst, [lines] if lines else [], duration=dur, can_self=can_self))
        return fid

    def wm_rifle_or_pistol(wm: int) -> list[tuple[int, str, float]]:
        return [(6, "WeaponMode", float(wm))]

    any_ids: list[int] = []

    def any_to(dst: int, conds: list[tuple[int, str, float]]):
        any_ids.append(reg_trans(dst, conds, can_self=0))

    # Unarmed (WeaponMode == 0): idle and locomotion are separate states.
    any_to(ST_STAND_IDLE, [(6, "WeaponMode", 0.0), (6, "Stance", 0.0), (4, "NavSpeed", 0.05)])
    any_to(ST_STAND_LOCO, [(6, "WeaponMode", 0.0), (6, "Stance", 0.0), (3, "NavSpeed", 0.055)])
    any_to(ST_CROUCH_IDLE, [(6, "WeaponMode", 0.0), (6, "Stance", 1.0), (4, "NavSpeed", 0.05)])
    any_to(ST_CROUCH_LOCO, [(6, "WeaponMode", 0.0), (6, "Stance", 1.0), (3, "NavSpeed", 0.055)])
    any_to(ST_STAND_IDLE, [(6, "WeaponMode", 0.0), (6, "Stance", 2.0), (4, "NavSpeed", 0.05)])
    any_to(ST_STAND_LOCO, [(6, "WeaponMode", 0.0), (6, "Stance", 2.0), (3, "NavSpeed", 0.055)])

    def rifle_any(dst: int, extra: list[tuple[int, str, float]]):
        any_to(dst, wm_rifle_or_pistol(1) + extra)
        any_to(dst, wm_rifle_or_pistol(3) + extra)

    # Rifle / pistol standing
    rifle_any(RF_SPRINT, [(6, "Stance", 0.0), (6, "LocomotionTier", 2.0), (3, "NavSpeed", 0.055)])
    rifle_any(RF_WALK_RUN_RDY, [(6, "Stance", 0.0), (4, "LocomotionTier", 2.0), (3, "NavSpeed", 0.055), (1, "WeaponReady", 0.0)])
    rifle_any(RF_WALK_RUN, [(6, "Stance", 0.0), (4, "LocomotionTier", 2.0), (3, "NavSpeed", 0.055), (2, "WeaponReady", 0.0)])
    rifle_any(RF_STAND_IDLE_RDY, [(6, "Stance", 0.0), (4, "NavSpeed", 0.05), (1, "WeaponReady", 0.0)])
    rifle_any(RF_STAND_IDLE, [(6, "Stance", 0.0), (4, "NavSpeed", 0.05), (2, "WeaponReady", 0.0)])

    # Crouch rifle
    rifle_any(RF_CROUCH_MOVE, [(6, "Stance", 1.0), (3, "NavSpeed", 0.055)])
    rifle_any(RF_CROUCH_IDLE_RDY, [(6, "Stance", 1.0), (4, "NavSpeed", 0.05), (1, "WeaponReady", 0.0)])
    rifle_any(RF_CROUCH_IDLE, [(6, "Stance", 1.0), (4, "NavSpeed", 0.05), (2, "WeaponReady", 0.0)])

    # Prone with rifle (tier forced 0 in scripts): keep aim-ready idle proxy on base
    rifle_any(RF_STAND_IDLE_RDY, [(6, "Stance", 2.0), (4, "NavSpeed", 0.05), (1, "WeaponReady", 0.0)])
    rifle_any(RF_STAND_IDLE, [(6, "Stance", 2.0), (4, "NavSpeed", 0.05), (2, "WeaponReady", 0.0)])
    rifle_any(RF_CROUCH_MOVE, [(6, "Stance", 2.0), (3, "NavSpeed", 0.055)])

    # Internal transition IDs (non-Any)
    internal: list[tuple[int, int, list[tuple[int, str, float]], float]] = []

    def intern(src: int, dst: int, conds: list[tuple[int, str, float]], dur: float = 0.18):
        internal.append((src, dst, conds, dur))

    intern(RF_STAND_IDLE, RF_STAND_IDLE_RDY, [(1, "WeaponReady", 0.0)])
    intern(RF_STAND_IDLE_RDY, RF_STAND_IDLE, [(2, "WeaponReady", 0.0)])
    intern(RF_WALK_RUN, RF_WALK_RUN_RDY, [(1, "WeaponReady", 0.0)])
    intern(RF_WALK_RUN_RDY, RF_WALK_RUN, [(2, "WeaponReady", 0.0)])
    intern(RF_CROUCH_IDLE, RF_CROUCH_IDLE_RDY, [(1, "WeaponReady", 0.0)])
    intern(RF_CROUCH_IDLE_RDY, RF_CROUCH_IDLE, [(2, "WeaponReady", 0.0)])

    intern(RF_STAND_IDLE, RF_WALK_RUN, [(6, "Stance", 0.0), (3, "NavSpeed", 0.055), (4, "LocomotionTier", 2.0), (2, "WeaponReady", 0.0)])
    intern(RF_STAND_IDLE_RDY, RF_WALK_RUN_RDY, [(6, "Stance", 0.0), (3, "NavSpeed", 0.055), (4, "LocomotionTier", 2.0), (1, "WeaponReady", 0.0)])
    intern(RF_WALK_RUN, RF_STAND_IDLE, [(6, "Stance", 0.0), (4, "NavSpeed", 0.05), (2, "WeaponReady", 0.0)])
    intern(RF_WALK_RUN_RDY, RF_STAND_IDLE_RDY, [(6, "Stance", 0.0), (4, "NavSpeed", 0.05), (1, "WeaponReady", 0.0)])

    intern(RF_SPRINT, RF_WALK_RUN, [(6, "Stance", 0.0), (4, "LocomotionTier", 2.0), (2, "WeaponReady", 0.0)])
    intern(RF_SPRINT, RF_WALK_RUN_RDY, [(6, "Stance", 0.0), (4, "LocomotionTier", 2.0), (1, "WeaponReady", 0.0)])
    intern(RF_WALK_RUN, RF_SPRINT, [(6, "Stance", 0.0), (6, "LocomotionTier", 2.0), (3, "NavSpeed", 0.055), (2, "WeaponReady", 0.0)])
    intern(RF_WALK_RUN_RDY, RF_SPRINT, [(6, "Stance", 0.0), (6, "LocomotionTier", 2.0), (3, "NavSpeed", 0.055), (1, "WeaponReady", 0.0)])

    intern(RF_CROUCH_IDLE, RF_CROUCH_MOVE, [(6, "Stance", 1.0), (3, "NavSpeed", 0.055)])
    intern(RF_CROUCH_IDLE_RDY, RF_CROUCH_MOVE, [(6, "Stance", 1.0), (3, "NavSpeed", 0.055)])
    intern(RF_CROUCH_MOVE, RF_CROUCH_IDLE, [(6, "Stance", 1.0), (4, "NavSpeed", 0.05), (2, "WeaponReady", 0.0)])
    intern(RF_CROUCH_MOVE, RF_CROUCH_IDLE_RDY, [(6, "Stance", 1.0), (4, "NavSpeed", 0.05), (1, "WeaponReady", 0.0)])

    intern(ST_STAND_IDLE, ST_STAND_LOCO, [(3, "NavSpeed", 0.055)])
    intern(ST_STAND_LOCO, ST_STAND_IDLE, [(4, "NavSpeed", 0.05)])
    intern(ST_CROUCH_IDLE, ST_CROUCH_LOCO, [(3, "NavSpeed", 0.055)])
    intern(ST_CROUCH_LOCO, ST_CROUCH_IDLE, [(4, "NavSpeed", 0.05)])

    intern(ST_STAND_IDLE, ST_CROUCH_IDLE, [(6, "Stance", 1.0), (4, "NavSpeed", 0.05)])
    intern(ST_STAND_IDLE, ST_CROUCH_LOCO, [(6, "Stance", 1.0), (3, "NavSpeed", 0.055)])
    intern(ST_STAND_LOCO, ST_CROUCH_IDLE, [(6, "Stance", 1.0), (4, "NavSpeed", 0.05)])
    intern(ST_STAND_LOCO, ST_CROUCH_LOCO, [(6, "Stance", 1.0), (3, "NavSpeed", 0.055)])

    intern(ST_CROUCH_IDLE, ST_STAND_IDLE, [(6, "Stance", 0.0), (4, "NavSpeed", 0.05)])
    intern(ST_CROUCH_IDLE, ST_STAND_LOCO, [(6, "Stance", 0.0), (3, "NavSpeed", 0.055)])
    intern(ST_CROUCH_LOCO, ST_STAND_IDLE, [(6, "Stance", 0.0), (4, "NavSpeed", 0.05)])
    intern(ST_CROUCH_LOCO, ST_STAND_LOCO, [(6, "Stance", 0.0), (3, "NavSpeed", 0.055)])
    intern(ST_CROUCH_IDLE, ST_STAND_IDLE, [(6, "Stance", 2.0), (4, "NavSpeed", 0.05)])
    intern(ST_CROUCH_IDLE, ST_STAND_LOCO, [(6, "Stance", 2.0), (3, "NavSpeed", 0.055)])
    intern(ST_CROUCH_LOCO, ST_STAND_IDLE, [(6, "Stance", 2.0), (4, "NavSpeed", 0.05)])
    intern(ST_CROUCH_LOCO, ST_STAND_LOCO, [(6, "Stance", 2.0), (3, "NavSpeed", 0.055)])

    state_trans: dict[int, list[int]] = {}

    def emit_internal(src: int, dst: int, conds: list[tuple[int, str, float]], dur: float):
        nonlocal tid
        fid = tid
        tid += 1
        body = "\n".join(cond(c, e, t) for (c, e, t) in conds)
        chunks.append(transition(fid, dst, [body], duration=dur, can_self=0))
        state_trans.setdefault(src, []).append(fid)

    for s, d, c, dur in internal:
        emit_internal(s, d, c, dur)

    # Aim_Point_U90-D90 intentionally owns both stand and crouch pitch states.
    # Keep Crouch_Aim_Point_U90-D90 empty until the legacy layer is removed in Unity.
    emit_internal(AIM_STAND_ST, AIM_CROUCH_ST, [(6, "Stance", 1.0)], 0.12)
    emit_internal(AIM_CROUCH_ST, AIM_STAND_ST, [(6, "Stance", 0.0)], 0.12)
    emit_internal(AIM_CROUCH_ST, AIM_STAND_ST, [(6, "Stance", 2.0)], 0.12)

    # Prone gesture states (Unarmed_Idle2Prone, Rifle_Prone2Idle, …) are listed in C# for
    # movement blocking but are not created here until LocomotionProneFeature + clips exist.

    # States
    def emit_state(fid: int, name: str, motion: str, trs: list[int], pos: tuple[float, float]):
        if trs:
            tx = "\n".join(f"  - {{fileID: {t}}}" for t in trs)
            trans_yaml = f"  m_Transitions:\n{tx}"
        else:
            trans_yaml = "  m_Transitions: []"
        px, py = pos
        chunks.append(
            f"""--- !u!1102 &{fid}
AnimatorState:
  serializedVersion: 6
  m_ObjectHideFlags: 1
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: {name}
  m_Speed: 1
  m_CycleOffset: 0
{trans_yaml}
  m_StateMachineBehaviours: []
  m_Position: {{x: {px}, y: {py}, z: 0}}
  m_IKOnFeet: 0
  m_WriteDefaultValues: 1
  m_Mirror: 0
  m_SpeedParameterActive: 0
  m_MirrorParameterActive: 0
  m_CycleOffsetParameterActive: 0
  m_TimeParameterActive: 0
  m_Motion: {motion}
  m_Tag: 
  m_SpeedParameter: 
  m_MirrorParameter: 
  m_CycleOffsetParameter: 
  m_TimeParameter: 
"""
        )

    emit_state(ST_STAND_IDLE, "Stand_Relaxed_Rifle_Idle", clip("c90d944601868854fb7423df35d91504"), state_trans.get(ST_STAND_IDLE, []), (280, 60))
    emit_state(ST_STAND_LOCO, "Stand_Locomotion", bt_ref(BT_UNARM_ST_LOCO), state_trans.get(ST_STAND_LOCO, []), (560, 60))
    emit_state(ST_CROUCH_IDLE, "Crouch_Idle", clip("b10e54e150039dd42a6ab6b95880b2c0"), state_trans.get(ST_CROUCH_IDLE, []), (280, 240))
    emit_state(
        ST_CROUCH_LOCO,
        "Crouch_Locomotion",
        bt_ref(
            # reuse simple two-point blend same as previous controller
            8435885046551052033
        ),
        state_trans.get(ST_CROUCH_LOCO, []),
        (560, 240),
    )

    emit_state(RF_STAND_IDLE, "RifleStand_Idle", bt_ref(BT_RF_ST_IDLE_REL), state_trans.get(RF_STAND_IDLE, []), (260, -120))
    emit_state(RF_STAND_IDLE_RDY, "RifleStand_Idle_Ready", bt_ref(BT_RF_ST_IDLE_RDY), state_trans.get(RF_STAND_IDLE_RDY, []), (520, -120))
    emit_state(RF_WALK_RUN, "RifleStand_WalkRun", bt_ref(BT_RF_TIER_RELAX), state_trans.get(RF_WALK_RUN, []), (780, -120))
    emit_state(RF_WALK_RUN_RDY, "RifleStand_WalkRun_Ready", bt_ref(BT_RF_TIER_READY), state_trans.get(RF_WALK_RUN_RDY, []), (1040, -120))
    emit_state(RF_SPRINT, "RifleStand_SprintFwd", clip("54e0e223225dfd64d8e6e78092e3f473"), state_trans.get(RF_SPRINT, []), (1300, -120))
    emit_state(RF_CROUCH_IDLE, "RifleCrouch_Idle", clip("5fee6100822823242950a95f55b6d8db"), state_trans.get(RF_CROUCH_IDLE, []), (260, -280))
    emit_state(RF_CROUCH_IDLE_RDY, "RifleCrouch_Idle_Ready", clip("02310368c2eb6a64ca8d1e59650f1a6d"), state_trans.get(RF_CROUCH_IDLE_RDY, []), (520, -280))
    emit_state(RF_CROUCH_MOVE, "RifleCrouch_Move", bt_ref(BT_RF_CROUCH_MOV), state_trans.get(RF_CROUCH_MOVE, []), (780, -280))

    emit_state(AIM_STAND_ST, "Stand_Aim_Pitch_Blend", bt_ref(BT_AIM_STAND), state_trans.get(AIM_STAND_ST, []), (300, 120))
    emit_state(AIM_CROUCH_ST, "Crouch_Aim_Pitch_Blend", bt_ref(BT_AIM_CROUCH), state_trans.get(AIM_CROUCH_ST, []), (580, 120))

    # Extra blend tree for crouch locomotion (legacy id referenced above)
    chunks.append(
        blend_tree_1d_navspeed(
            8435885046551052033,
            "CrouchLocomotionBlend",
            [(CROUCH_WALK_NAV, "7000e1c512c22484f82c3bcd219ab167")],
        )
    )

    def child_states_yaml(entries: list[tuple[int, float, float]]) -> str:
        lines = []
        for fid, x, y in entries:
            lines.append(f"""  - serializedVersion: 1
    m_State: {{fileID: {fid}}}
    m_Position: {{x: {x}, y: {y}, z: 0}}""")
        return "\n".join(lines)

    sm_shell = """
  m_ChildStateMachines: []
  m_AnyStateTransitions: []
  m_EntryTransitions: []
  m_StateMachineTransitions: {}
  m_StateMachineBehaviours: []
  m_AnyStatePosition: {x: 40, y: -260, z: 0}
  m_EntryPosition: {x: 40, y: 220, z: 0}
  m_ExitPosition: {x: 1160, y: 220, z: 0}
  m_ParentStateMachinePosition: {x: 800, y: 20, z: 0}"""

    chunks.append(f"""--- !u!1107 &{SM_UNARMED}
AnimatorStateMachine:
  serializedVersion: 6
  m_ObjectHideFlags: 1
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: Locomotion_Unarmed
  m_ChildStates:
{child_states_yaml([
        (ST_STAND_IDLE, 280, 60),
        (ST_STAND_LOCO, 560, 60),
        (ST_CROUCH_IDLE, 280, 240),
        (ST_CROUCH_LOCO, 560, 240),
    ])}{sm_shell}
  m_DefaultState: {{fileID: {ST_STAND_IDLE}}}
""")

    chunks.append(f"""--- !u!1107 &{SM_RIFLE_STANDING}
AnimatorStateMachine:
  serializedVersion: 6
  m_ObjectHideFlags: 1
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: Rifle_Standing
  m_ChildStates:
{child_states_yaml([
        (RF_STAND_IDLE, 240, 40),
        (RF_STAND_IDLE_RDY, 480, 40),
        (RF_WALK_RUN, 720, 40),
        (RF_WALK_RUN_RDY, 960, 40),
        (RF_SPRINT, 1220, 40),
    ])}{sm_shell}
  m_DefaultState: {{fileID: {RF_STAND_IDLE}}}
""")

    chunks.append(f"""--- !u!1107 &{SM_RIFLE_CROUCH}
AnimatorStateMachine:
  serializedVersion: 6
  m_ObjectHideFlags: 1
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: Rifle_Crouch
  m_ChildStates:
{child_states_yaml([
        (RF_CROUCH_IDLE, 260, 80),
        (RF_CROUCH_IDLE_RDY, 520, 80),
        (RF_CROUCH_MOVE, 820, 80),
    ])}{sm_shell}
  m_DefaultState: {{fileID: {RF_CROUCH_IDLE}}}
""")

    any_yaml = "\n".join(f"  - {{fileID: {i}}}" for i in any_ids)

    chunks.append(f"""--- !u!1107 &{BASE_SM}
AnimatorStateMachine:
  serializedVersion: 6
  m_ObjectHideFlags: 1
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: Base Layer
  m_ChildStates: []
  m_ChildStateMachines:
  - serializedVersion: 1
    m_StateMachine: {{fileID: {SM_UNARMED}}}
    m_Position: {{x: -520, y: 200, z: 0}}
  - serializedVersion: 1
    m_StateMachine: {{fileID: {SM_RIFLE_STANDING}}}
    m_Position: {{x: 220, y: 60, z: 0}}
  - serializedVersion: 1
    m_StateMachine: {{fileID: {SM_RIFLE_CROUCH}}}
    m_Position: {{x: 220, y: 380, z: 0}}
  m_AnyStateTransitions:
{any_yaml}
  m_EntryTransitions: []
  m_StateMachineTransitions: {{}}
  m_StateMachineBehaviours: []
  m_AnyStatePosition: {{x: -860, y: 120, z: 0}}
  m_EntryPosition: {{x: -860, y: -240, z: 0}}
  m_ExitPosition: {{x: 1380, y: -240, z: 0}}
  m_ParentStateMachinePosition: {{x: 800, y: 20, z: 0}}
  m_DefaultState: {{fileID: {ST_STAND_IDLE}}}
""")

    chunks.append(f"""--- !u!1107 &{AIM_STAND_SM}
AnimatorStateMachine:
  serializedVersion: 6
  m_ObjectHideFlags: 1
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: Aim_Point_U90-D90
  m_ChildStates:
  - serializedVersion: 1
    m_State: {{fileID: {AIM_STAND_ST}}}
    m_Position: {{x: 300, y: 120, z: 0}}
  - serializedVersion: 1
    m_State: {{fileID: {AIM_CROUCH_ST}}}
    m_Position: {{x: 580, y: 120, z: 0}}
  m_ChildStateMachines: []
  m_AnyStateTransitions: []
  m_EntryTransitions: []
  m_StateMachineTransitions: {{}}
  m_StateMachineBehaviours: []
  m_AnyStatePosition: {{x: 40, y: 400, z: 0}}
  m_EntryPosition: {{x: 40, y: 120, z: 0}}
  m_ExitPosition: {{x: 760, y: 20, z: 0}}
  m_ParentStateMachinePosition: {{x: 760, y: 20, z: 0}}
  m_DefaultState: {{fileID: {AIM_STAND_ST}}}
""")

    out = pathlib.Path(__file__).resolve().parents[1] / "Assets" / "Animations" / "UnitAnimController.controller"
    out.write_text("\n".join(chunks), encoding="utf-8")
    print("Wrote", out)


if __name__ == "__main__":
    main()
