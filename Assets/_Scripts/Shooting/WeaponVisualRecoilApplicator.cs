using UnityEngine;

/// <summary>
/// Applies <see cref="WeaponVisualRecoilState"/> to Hand_R as an absolute overlay on this frame's animation pose.
/// Does not write weapon local, pose, or aim. Left IK (order 250) follows the kicked weapon child.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(UnitWeaponRecoil))]
[DefaultExecutionOrder(200)]
public sealed class WeaponVisualRecoilApplicator : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private UnitWeaponRecoil m_Recoil;
	[SerializeField] private UnitEquipment m_Equipment;
	#endregion

	#region Public API
	public Quaternion LastHandBaseLocalRotation { get; private set; } = Quaternion.identity;
	public Quaternion LastHandFinalLocalRotation { get; private set; } = Quaternion.identity;
	public Vector3 LastHandBaseLocalPosition { get; private set; }
	public Vector3 LastHandFinalLocalPosition { get; private set; }
	public bool AppliedThisFrame { get; private set; }
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Recoil == null)
			m_Recoil = GetComponent<UnitWeaponRecoil>();
		if (m_Equipment == null)
			m_Equipment = GetComponent<UnitEquipment>();
	}

	private void LateUpdate()
	{
		AppliedThisFrame = false;
		if (m_Recoil == null || !m_Recoil.isActiveAndEnabled ||
		    !m_Recoil.ShouldApplyOverlayThisFrame() ||
		    !m_Recoil.CurrentState.isActive)
		{
			LastHandFinalLocalRotation = LastHandBaseLocalRotation;
			LastHandFinalLocalPosition = LastHandBaseLocalPosition;
			return;
		}

		Transform hand = m_Equipment != null ? m_Equipment.RightHandAnchor : null;
		if (hand == null)
			return;

		Quaternion recoilRot = m_Recoil.BuildHandRotationOffset();
		Vector3 punchParent = m_Recoil.BuildHandParentSpaceTranslation(hand);

		Quaternion baseRot = hand.localRotation;
		Vector3 basePos = hand.localPosition;
		Quaternion finalRot = baseRot * recoilRot;
		Vector3 finalPos = basePos + punchParent;

		hand.localRotation = finalRot;
		hand.localPosition = finalPos;

		LastHandBaseLocalRotation = baseRot;
		LastHandFinalLocalRotation = finalRot;
		LastHandBaseLocalPosition = basePos;
		LastHandFinalLocalPosition = finalPos;
		AppliedThisFrame = true;
	}
	#endregion
}
