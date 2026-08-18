using UnityEngine;

/// <summary>
/// FINAL weapon TRS commit after pose BASE (64) and aiming solver (65), before IK.
/// Gameplay aim-correction is rejected — this writes BASE.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(UnitEquippedWeaponPose))]
[DefaultExecutionOrder(68)]
internal sealed class UnitEquippedWeaponPoseCommit : MonoBehaviour
{
	private UnitEquippedWeaponPose m_Pose;

	private void Awake() => m_Pose = GetComponent<UnitEquippedWeaponPose>();

	private void Update()
	{
		if (m_Pose == null)
			m_Pose = GetComponent<UnitEquippedWeaponPose>();
		m_Pose?.CommitWeaponTransformForFrame();
	}
}
