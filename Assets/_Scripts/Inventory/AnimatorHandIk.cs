using UnityEngine;

/// <summary>
/// IK левой и правой кисти к целям на экипированном оружии.
/// Правая рука: координаты relaxed/ready из <see cref="ItemDefinition"/> + вес по <see cref="UnitEquippedWeaponPose"/>.
/// Компонент на том же GameObject, что <see cref="Animator"/> (Humanoid). В Animator Controller нужен <b>IK Pass</b>.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public class AnimatorHandIk : MonoBehaviour
{
	#region Serialized Fields
	[Tooltip("Снаряжение на корне юнита (родитель или сам юнит с CharacterInventory).")]
	[SerializeField] private UnitEquipment m_UnitEquipment;
	[Tooltip("Play Mode pose/IK tuner on the unit.")]
	[SerializeField] private UnitEquippedWeaponPoseRuntimeTuner m_RuntimeTuner;
	[Tooltip("Поза оружия relaxed/ready; вес IK правой руки берётся отсюда.")]
	[SerializeField] private UnitEquippedWeaponPose m_EquippedWeaponPose;
	[Tooltip("Пока идёт ручная зарядка магазина (T), IK рук отключается.")]
	[SerializeField] private UnitMagazineLoadingController m_MagazineLoading;
	[Tooltip("Пока идёт перезарядка оружия (R), IK рук отключается.")]
	[SerializeField] private UnitWeaponReloadController m_WeaponReload;
	[Tooltip("Пока идёт самостабилизация IFAK, IK рук отключается.")]
	[SerializeField] private UnitSelfStabilizationController m_SelfStabilization;
	[Tooltip("Пока идёт стабилизация другого юнита, IK рук отключается.")]
	[SerializeField] private UnitStabilizeOtherController m_StabilizeOther;
	[Tooltip("Пока юнит тащит сражённого, IK рук отключается (рука уходит на drag-слой).")]
	[SerializeField] private UnitBusyState m_BusyState;
	[Tooltip("Пока идёт бросок гранаты, IK рук отключается.")]
	[SerializeField] private UnitGrenadeThrowController m_GrenadeThrow;
	[SerializeField, Range(0f, 1f)] private float m_LeftHandPositionWeight = 1f;
	[SerializeField, Range(0f, 1f)] private float m_LeftHandRotationWeight = 1f;
	[SerializeField, Range(0f, 1f)] private float m_RightHandPositionWeight = 1f;
	[SerializeField, Range(0f, 1f)] private float m_RightHandRotationWeight = 1f;
	[Tooltip("Right-hand IK weight in low ready (not ready). Use 1 so saved RightHandIkNotReady coords apply; 0 = animation only.")]
	[SerializeField, Range(0f, 1f)] private float m_RightHandNotReadyIkWeight = 1f;
	[Header("Экипировка")]
	[SerializeField, Min(0f)] private float m_EquipBlendDuration = 0.35f;
	[SerializeField] private AnimationCurve m_EquipBlendCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
	[Header("Локоть (подсказка IK)")]
	[SerializeField] private bool m_UseLeftElbowHint;
	[SerializeField] private Transform m_LeftElbowHint;
	[SerializeField, Range(0f, 1f)] private float m_LeftElbowHintWeight = 1f;
	[Header("Отладка")]
	[SerializeField] private bool m_DrawIkTargetGizmo;
	[SerializeField] private bool m_LogProximityIk;
	#endregion

	#region Private Fields
	private Animator m_Animator;
	private bool m_ClearHandIkOnNextAnimatorIkPass;
	private bool m_IsEquipBlendActive;
	private float m_EquipBlendElapsed;
	private int m_LastEquipBlendAdvanceFrame = -1;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		m_Animator = GetComponent<Animator>();
		ResolveReferences();
	}

	private void OnEnable()
	{
		SubscribeEquipmentEvents();
	}

	private void OnDisable()
	{
		UnsubscribeEquipmentEvents();
		StopEquipBlend();
	}

	private void OnDrawGizmosSelected()
	{
		if (!m_DrawIkTargetGizmo || !Application.isPlaying)
			return;

		if (TryResolveLeftHandIkWorldPose(out Vector3 leftPos, out Quaternion leftRot))
		{
			Gizmos.color = new Color(0.2f, 0.95f, 1f, 0.95f);
			Gizmos.DrawSphere(leftPos, 0.015f);
			Gizmos.DrawLine(leftPos, leftPos + leftRot * Vector3.forward * 0.06f);
		}

		if (TryResolveRightHandIkWorldPose(out Vector3 rightPos, out Quaternion rightRot))
		{
			Gizmos.color = new Color(1f, 0.55f, 0.2f, 0.95f);
			Gizmos.DrawSphere(rightPos, 0.015f);
			Gizmos.DrawLine(rightPos, rightPos + rightRot * Vector3.forward * 0.06f);
		}
	}
	#endregion

	#region Public Methods
	public void OnWeaponReadyStateChanged()
	{
		if (IsHandIkBlocked())
			m_ClearHandIkOnNextAnimatorIkPass = true;
	}

	public void OnWeaponReadyStateApplied()
	{
		OnWeaponReadyStateChanged();
	}

	public void RequestClearLeftHandIk()
	{
		StopEquipBlend();
		m_ClearHandIkOnNextAnimatorIkPass = true;
	}
	#endregion

	#region IK
	private void OnAnimatorIK(int _layerIndex)
	{
		if (m_Animator == null)
			return;

		if (m_ClearHandIkOnNextAnimatorIkPass)
		{
			m_ClearHandIkOnNextAnimatorIkPass = false;
			StopEquipBlend();
			ClearLeftHandIk();
			ClearRightHandIk();
		}

		if (ShouldDisableAllHandIkForTuning())
		{
			StopEquipBlend();
			ClearLeftHandIk();
			ClearRightHandIk();
			return;
		}

		if (ShouldUseBoltCycleLeftHandHoldIk())
		{
			StopEquipBlend();
			ApplyLeftHandIkInternal();
			ClearRightHandIk();
			return;
		}

		if (IsHandIkBlocked())
		{
			StopEquipBlend();
			ClearLeftHandIk();
			ClearRightHandIk();
			return;
		}

		ApplyLeftHandIkInternal();
		ApplyRightHandIkInternal();
	}

	private bool ShouldUseBoltCycleLeftHandHoldIk()
	{
		return m_UnitEquipment != null && m_UnitEquipment.IsWeaponHeldForBoltCycle;
	}

	private bool ShouldDisableAllHandIkForTuning()
	{
		if (m_RuntimeTuner == null)
			m_RuntimeTuner = GetComponentInParent<UnitEquippedWeaponPoseRuntimeTuner>();
		return m_RuntimeTuner != null && m_RuntimeTuner.ShouldDisableAllHandIk;
	}

	private void ResolveReferences()
	{
		if (m_UnitEquipment == null)
			m_UnitEquipment = GetComponentInParent<UnitEquipment>();
		if (m_EquippedWeaponPose == null)
			m_EquippedWeaponPose = GetComponent<UnitEquippedWeaponPose>();
		if (m_EquippedWeaponPose == null)
			m_EquippedWeaponPose = GetComponentInParent<UnitEquippedWeaponPose>();
		if (m_MagazineLoading == null)
			m_MagazineLoading = GetComponentInParent<UnitMagazineLoadingController>();
		if (m_WeaponReload == null)
			m_WeaponReload = GetComponentInParent<UnitWeaponReloadController>();
		if (m_SelfStabilization == null)
			m_SelfStabilization = GetComponentInParent<UnitSelfStabilizationController>();
		if (m_StabilizeOther == null)
			m_StabilizeOther = GetComponentInParent<UnitStabilizeOtherController>();
		if (m_GrenadeThrow == null)
			m_GrenadeThrow = GetComponentInParent<UnitGrenadeThrowController>();
		if (m_BusyState == null)
			m_BusyState = GetComponentInParent<UnitBusyState>();
		if (m_RuntimeTuner == null)
			m_RuntimeTuner = GetComponent<UnitEquippedWeaponPoseRuntimeTuner>();
		if (m_RuntimeTuner == null)
			m_RuntimeTuner = GetComponentInParent<UnitEquippedWeaponPoseRuntimeTuner>();
	}

	private bool IsHandIkBlocked()
	{
		if (m_MagazineLoading != null && m_MagazineLoading.IsLoadingMagazine)
			return true;
		if (m_WeaponReload != null && (m_WeaponReload.IsReloadingWeapon || m_WeaponReload.IsCyclingBolt || m_WeaponReload.IsLoadingLmgBelt))
			return true;
		if (m_SelfStabilization != null && m_SelfStabilization.IsHealPresentationActive)
			return true;
		if (m_StabilizeOther != null && m_StabilizeOther.IsHealPresentationActive)
			return true;
		if (m_BusyState != null && m_BusyState.HasReason(UnitBusyState.BusyReason.DraggingFallen))
			return true;
		if (m_GrenadeThrow != null && m_GrenadeThrow.IsThrowAnimPlaying)
		{
			m_ClearHandIkOnNextAnimatorIkPass = true;
			return true;
		}
		return false;
	}

	private void ClearLeftHandIk()
	{
		m_Animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
		m_Animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
		m_Animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 0f);
	}

	private void ClearRightHandIk()
	{
		m_Animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
		m_Animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
	}

	private void SubscribeEquipmentEvents()
	{
		if (m_UnitEquipment == null)
			m_UnitEquipment = GetComponentInParent<UnitEquipment>();

		if (m_UnitEquipment != null)
			m_UnitEquipment.EquipmentChanged += HandleEquipmentChanged;
	}

	private void UnsubscribeEquipmentEvents()
	{
		if (m_UnitEquipment != null)
			m_UnitEquipment.EquipmentChanged -= HandleEquipmentChanged;
	}

	private void HandleEquipmentChanged()
	{
		if (IsHandIkBlocked() || !TryResolveLeftHandIkWorldPose(out _, out _))
		{
			StopEquipBlend();
			return;
		}

		StartEquipBlend();
	}

	private void StartEquipBlend()
	{
		if (m_EquipBlendDuration <= 0f)
		{
			StopEquipBlend();
			return;
		}

		m_IsEquipBlendActive = true;
		m_EquipBlendElapsed = 0f;
		m_LastEquipBlendAdvanceFrame = -1;
	}

	private void StopEquipBlend()
	{
		m_IsEquipBlendActive = false;
		m_EquipBlendElapsed = 0f;
		m_LastEquipBlendAdvanceFrame = -1;
	}

	private float GetEquipBlendMultiplier()
	{
		if (!m_IsEquipBlendActive)
			return 1f;

		if (m_LastEquipBlendAdvanceFrame != Time.frameCount)
		{
			m_LastEquipBlendAdvanceFrame = Time.frameCount;
			m_EquipBlendElapsed += Time.deltaTime;
		}

		if (m_EquipBlendElapsed >= m_EquipBlendDuration)
		{
			StopEquipBlend();
			return 1f;
		}

		float normalizedTime = m_EquipBlendDuration > 0f
			? Mathf.Clamp01(m_EquipBlendElapsed / m_EquipBlendDuration)
			: 1f;

		if (m_EquipBlendCurve != null && m_EquipBlendCurve.length > 0)
			return m_EquipBlendCurve.Evaluate(normalizedTime);

		return Mathf.SmoothStep(0f, 1f, normalizedTime);
	}

	private float GetEffectiveReadyBlend01()
	{
		if (m_RuntimeTuner != null && m_RuntimeTuner.IsTuningActive)
			return m_RuntimeTuner.ForcedReadyBlend01;

		return m_EquippedWeaponPose != null
			? Mathf.Clamp01(m_EquippedWeaponPose.ReadyPoseBlend01)
			: 0f;
	}

	private float GetRightHandIkWeightMultiplier()
	{
		if (m_RuntimeTuner != null && m_RuntimeTuner.ForcesRightHandIk)
			return 1f;

		if (m_EquippedWeaponPose == null)
			return 0f;

		float readyBlend = GetEffectiveReadyBlend01();
		return Mathf.Lerp(m_RightHandNotReadyIkWeight, 1f, readyBlend);
	}

	private void ApplyLeftHandIkInternal()
	{
		if (!TryResolveLeftHandIkWorldPose(out Vector3 position, out Quaternion rotation))
		{
			StopEquipBlend();
			ClearLeftHandIk();
			return;
		}

		float blend = GetEquipBlendMultiplier();
		float positionWeight = m_LeftHandPositionWeight * blend;
		float rotationWeight = m_LeftHandRotationWeight * blend;

		m_Animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, positionWeight);
		m_Animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, rotationWeight);
		m_Animator.SetIKPosition(AvatarIKGoal.LeftHand, position);
		m_Animator.SetIKRotation(AvatarIKGoal.LeftHand, rotation);

		if (m_UseLeftElbowHint && m_LeftElbowHint != null)
		{
			m_Animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, m_LeftElbowHintWeight * blend);
			m_Animator.SetIKHintPosition(AvatarIKHint.LeftElbow, m_LeftElbowHint.position);
		}
		else
			m_Animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 0f);
	}

	private void ApplyRightHandIkInternal()
	{
		if (!TryResolveRightHandIkWorldPose(out Vector3 position, out Quaternion rotation))
		{
			ClearRightHandIk();
			return;
		}

		float ikBlend = GetRightHandIkWeightMultiplier();
		if (ikBlend <= 0f)
		{
			ClearRightHandIk();
			return;
		}

		float positionWeight = m_RightHandPositionWeight * ikBlend;
		float rotationWeight = m_RightHandRotationWeight * ikBlend;

		m_Animator.SetIKPositionWeight(AvatarIKGoal.RightHand, positionWeight);
		m_Animator.SetIKRotationWeight(AvatarIKGoal.RightHand, rotationWeight);
		m_Animator.SetIKPosition(AvatarIKGoal.RightHand, position);
		m_Animator.SetIKRotation(AvatarIKGoal.RightHand, rotation);
	}

	private bool TryResolveRightHandIkWorldPose(out Vector3 _position, out Quaternion _rotation)
	{
		_position = Vector3.zero;
		_rotation = Quaternion.identity;

		if (m_UnitEquipment == null)
			return false;

		Transform weaponRoot = m_UnitEquipment.MainWeaponRoot;
		if (weaponRoot == null || !weaponRoot.gameObject.activeInHierarchy)
			return false;

		ItemDefinition equipped = m_UnitEquipment.EquippedDefinition;
		if (equipped == null)
			return false;

		float readyBlend = GetEffectiveReadyBlend01();

		if (!TryResolveRightHandIkLocalPose(equipped, weaponRoot, readyBlend, out Vector3 localPosition, out Quaternion localRotation))
			return false;

		_position = weaponRoot.TransformPoint(localPosition);
		_rotation = weaponRoot.rotation * localRotation;
		return true;
	}

	private bool TryResolveRightHandIkLocalPose(
		ItemDefinition _equipped,
		Transform _weaponRoot,
		float _readyBlend01,
		out Vector3 _localPosition,
		out Quaternion _localRotation)
	{
		_localPosition = Vector3.zero;
		_localRotation = Quaternion.identity;

		Vector3 notReadyLocalPosition = _equipped.RightHandIkNotReadyLocalPosition;
		Quaternion notReadyLocalRotation = _equipped.RightHandIkNotReadyLocalRotation;
		Vector3 readyLocalPosition = _equipped.RightHandIkReadyLocalPosition;
		Quaternion readyLocalRotation = _equipped.RightHandIkReadyLocalRotation;

		if (m_RuntimeTuner != null && m_RuntimeTuner.IsTuningActive)
		{
			// Live Hierarchy transforms — user moves RightHandIkTarget* in Scene.
			Transform notReadyChild = m_UnitEquipment.RightHandIkTargetNotReadyTransform;
			if (notReadyChild != null)
			{
				notReadyLocalPosition = _weaponRoot.InverseTransformPoint(notReadyChild.position);
				notReadyLocalRotation = Quaternion.Inverse(_weaponRoot.rotation) * notReadyChild.rotation;
			}

			Transform readyChild = m_UnitEquipment.RightHandIkTargetTransform;
			if (readyChild != null)
			{
				readyLocalPosition = _weaponRoot.InverseTransformPoint(readyChild.position);
				readyLocalRotation = Quaternion.Inverse(_weaponRoot.rotation) * readyChild.rotation;
			}
		}
		else
		{
			if (!HasConfiguredIkLocalPose(notReadyLocalPosition, _equipped.RightHandIkNotReadyLocalEulerAngles))
			{
				Transform notReadyChild = m_UnitEquipment.RightHandIkTargetNotReadyTransform;
				if (notReadyChild != null)
				{
					notReadyLocalPosition = _weaponRoot.InverseTransformPoint(notReadyChild.position);
					notReadyLocalRotation = Quaternion.Inverse(_weaponRoot.rotation) * notReadyChild.rotation;
				}
			}

			if (!HasConfiguredIkLocalPose(readyLocalPosition, _equipped.RightHandIkReadyLocalEulerAngles))
			{
				Transform readyChild = m_UnitEquipment.RightHandIkTargetTransform;
				if (readyChild != null)
				{
					readyLocalPosition = _weaponRoot.InverseTransformPoint(readyChild.position);
					readyLocalRotation = Quaternion.Inverse(_weaponRoot.rotation) * readyChild.rotation;
				}
			}
		}

		_localPosition = Vector3.Lerp(notReadyLocalPosition, readyLocalPosition, _readyBlend01);
		_localRotation = Quaternion.Slerp(notReadyLocalRotation, readyLocalRotation, _readyBlend01);
		return true;
	}

	private static bool HasConfiguredIkLocalPose(Vector3 _localPosition, Vector3 _localEulerAngles)
	{
		return _localPosition != Vector3.zero || _localEulerAngles != Vector3.zero;
	}

	private bool TryResolveLeftHandIkWorldPose(out Vector3 _position, out Quaternion _rotation)
	{
		_position = Vector3.zero;
		_rotation = Quaternion.identity;

		if (m_UnitEquipment == null)
			return false;

		Transform weaponRoot = m_UnitEquipment.MainWeaponRoot;
		if (weaponRoot == null || !weaponRoot.gameObject.activeInHierarchy)
			return false;

		ItemDefinition equipped = m_UnitEquipment.EquippedDefinition;
		if (equipped == null)
			return false;

		float readyBlend = GetEffectiveReadyBlend01();
		if (!TryResolveLeftHandIkLocalPose(equipped, weaponRoot, readyBlend, out Vector3 localPosition, out Quaternion localRotation))
			return false;

		_position = weaponRoot.TransformPoint(localPosition);
		_rotation = weaponRoot.rotation * localRotation;
		return true;
	}

	private bool TryResolveLeftHandIkLocalPose(
		ItemDefinition _equipped,
		Transform _weaponRoot,
		float _readyBlend01,
		out Vector3 _localPosition,
		out Quaternion _localRotation)
	{
		_localPosition = Vector3.zero;
		_localRotation = Quaternion.identity;

		Transform readyChild = m_UnitEquipment.LeftHandIkTargetTransform;
		Transform notReadyChild = m_UnitEquipment.LeftHandIkTargetNotReadyTransform;

		Vector3 notReadyLocalPosition = _equipped.LeftHandIkNotReadyLocalPosition;
		Quaternion notReadyLocalRotation = _equipped.LeftHandIkNotReadyLocalRotation;
		Vector3 readyLocalPosition = _equipped.LeftHandIkReadyLocalPosition;
		Quaternion readyLocalRotation = _equipped.LeftHandIkReadyLocalRotation;

		bool tuning = m_RuntimeTuner != null && m_RuntimeTuner.IsTuningActive;
		EquippedWeapon equippedWeapon = m_UnitEquipment != null ? m_UnitEquipment.EquippedWeapon : null;
		Transform foregripRoot = equippedWeapon != null ? equippedWeapon.UnderBarrelForegripVisualRoot : null;
		bool readyOnForegrip = IsUnderOrSame(foregripRoot, readyChild);
		bool notReadyOnForegrip = IsUnderOrSame(foregripRoot, notReadyChild);

		// Live Hierarchy while tuning. Foregrip empties always win for the modes they own.
		// Weapon-body empties may be stale copies — prefer ItemDefinition unless tuning.
		bool useLiveNotReady = tuning || notReadyOnForegrip;
		bool useLiveReady = tuning || readyOnForegrip;

		if (useLiveNotReady && notReadyChild != null && notReadyChild != readyChild)
		{
			notReadyLocalPosition = _weaponRoot.InverseTransformPoint(notReadyChild.position);
			notReadyLocalRotation = Quaternion.Inverse(_weaponRoot.rotation) * notReadyChild.rotation;
		}
		else if (!HasConfiguredIkLocalPose(notReadyLocalPosition, _equipped.LeftHandIkNotReadyLocalEulerAngles))
		{
			Transform fallback = notReadyChild != null ? notReadyChild : readyChild;
			if (fallback != null)
			{
				notReadyLocalPosition = _weaponRoot.InverseTransformPoint(fallback.position);
				notReadyLocalRotation = Quaternion.Inverse(_weaponRoot.rotation) * fallback.rotation;
			}
		}

		if (useLiveReady && readyChild != null)
		{
			readyLocalPosition = _weaponRoot.InverseTransformPoint(readyChild.position);
			readyLocalRotation = Quaternion.Inverse(_weaponRoot.rotation) * readyChild.rotation;
		}
		else if (!HasConfiguredIkLocalPose(readyLocalPosition, _equipped.LeftHandIkReadyLocalEulerAngles))
		{
			if (readyChild != null)
			{
				readyLocalPosition = _weaponRoot.InverseTransformPoint(readyChild.position);
				readyLocalRotation = Quaternion.Inverse(_weaponRoot.rotation) * readyChild.rotation;
			}
		}

		if (readyChild == null && notReadyChild == null &&
		    !HasConfiguredIkLocalPose(notReadyLocalPosition, _equipped.LeftHandIkNotReadyLocalEulerAngles) &&
		    !HasConfiguredIkLocalPose(readyLocalPosition, _equipped.LeftHandIkReadyLocalEulerAngles))
			return false;

		_localPosition = Vector3.Lerp(notReadyLocalPosition, readyLocalPosition, _readyBlend01);
		_localRotation = Quaternion.Slerp(notReadyLocalRotation, readyLocalRotation, _readyBlend01);
		return true;
	}

	private static bool IsUnderOrSame(Transform _root, Transform _child)
	{
		return _root != null && _child != null && (_child == _root || _child.IsChildOf(_root));
	}
	#endregion
}
