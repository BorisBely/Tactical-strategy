using UnityEngine;

/// <summary>
/// Отдача визуала после позы аниматора: в LateUpdate берётся локальный поворот (прицел/IK),
/// затем умножается накопленный kick. Позицию <c>localPosition</c> не меняем — сдвиг по осям давал накопление и конфликт с анимацией.
/// Цель: override на юните, иначе <see cref="EquippedWeapon.VisualRecoilKickPivot"/>, иначе корень инстанса.
/// Импульс — <see cref="WeaponDefinition.ComputeAddedRecoilPenalty"/>; потолок pitch привязан к <see cref="UnitWeaponRecoilController.MaxRecoilPenalty"/>.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(62)]
public sealed class UnitWeaponVisualRecoilKick : MonoBehaviour
{
	#region Serialized Fields
	[Tooltip("Снаряжение: корень оружия в руке.")]
	[SerializeField] private UnitEquipment m_Equipment;
	[Tooltip("Режим огня и определение оружия для расчёта импульса.")]
	[SerializeField] private UnitWeaponRuntime m_WeaponRuntime;
	[SerializeField] private UnitWeaponFireController m_FireController;
	[SerializeField] private UnitWeaponRecoilController m_RecoilController;
	[Tooltip("Редко: явная цель kick. Иначе — Visual Recoil Kick Pivot на EquippedWeapon, иначе корень оружия целиком.")]
	[SerializeField] private Transform m_KickTransformOverride;

	[Header("Импульс от штрафа отдачи")]
	[Tooltip("Градусы подъёма (локальный Euler X) на единицу добавленного RecoilPenalty за выстрел. Ограничивается потолком штрафа из RecoilController.")]
	[SerializeField, Min(0f)] private float m_PitchDegreesPerPenaltyUnit = 2.1f;
	[Tooltip("Случайный yaw (локальный Y) как доля от pitch-импульса этого выстрела.")]
	[SerializeField, Range(0f, 1f)] private float m_YawJitterFraction = 0.28f;

	[Header("Возврат")]
	[Tooltip("Множитель к WeaponDefinition.RecoilRecoveryPerSecond для затухания kick.")]
	[SerializeField, Min(0.01f)] private float m_VisualRecoveryFromWeaponScale = 0.85f;
	[Tooltip("Множитель восстановления kick, пока удерживается огонь (как у штрафа отдачи).")]
	[SerializeField, Min(0f)] private float m_RecoveryWhileFiringMultiplier = 0.7f;
	[Tooltip("Если нет WeaponDefinition — скорость затухания kick.")]
	[SerializeField, Min(0.01f)] private float m_FallbackVisualRecovery = 14f;

	[Header("Стабилизация")]
	[Tooltip("Если локальный поворот совпадает с прошлым кадром после нашего kick — вычитаем отображённый kick (иначе накапливается ошибка). Только угол; поза позиции не трогается.")]
	[SerializeField, Min(0.01f)] private float m_AnimatorReplaceAngleSlopDegrees = 0.35f;

	[Header("Debug")]
	[SerializeField] private bool m_LogVisualKick;
	#endregion

	#region Private Fields
	private Transform m_KickTarget;
	private float m_KickPitchDegrees;
	private float m_KickYawDegrees;
	private bool m_AppliedKickLastFrame;
	private Quaternion m_LastRotationAfterOurApply = Quaternion.identity;
	private Quaternion m_LastDisplayedKickRotation = Quaternion.identity;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Equipment == null)
			m_Equipment = GetComponent<UnitEquipment>();
		if (m_WeaponRuntime == null)
			m_WeaponRuntime = GetComponent<UnitWeaponRuntime>();
		if (m_FireController == null)
			m_FireController = GetComponent<UnitWeaponFireController>();
		if (m_RecoilController == null)
			m_RecoilController = GetComponent<UnitWeaponRecoilController>();
	}

	private void OnEnable()
	{
		if (m_Equipment != null)
			m_Equipment.EquipmentChanged += HandleEquipmentChanged;
		if (m_FireController != null)
			m_FireController.ShotFired += HandleShotFired;

		RefreshKickTarget(true);
	}

	private void OnDisable()
	{
		if (m_Equipment != null)
			m_Equipment.EquipmentChanged -= HandleEquipmentChanged;
		if (m_FireController != null)
			m_FireController.ShotFired -= HandleShotFired;

		if (m_KickTarget != null)
			StripKickOnTransform(m_KickTarget);
		m_KickTarget = null;
		ResetKickState();
	}

	private void LateUpdate()
	{
		if (m_KickTarget == null)
			return;

		Quaternion rawRot = m_KickTarget.localRotation;

		bool sameRotationAsLastApply = m_AppliedKickLastFrame
			&& Quaternion.Angle(rawRot, m_LastRotationAfterOurApply) < m_AnimatorReplaceAngleSlopDegrees;

		Quaternion animRot = sameRotationAsLastApply
			? rawRot * Quaternion.Inverse(m_LastDisplayedKickRotation)
			: rawRot;

		float recovery = ResolveVisualRecoveryPerSecond();
		float damp = 1f - Mathf.Exp(-recovery * Time.deltaTime);
		m_KickPitchDegrees = Mathf.Lerp(m_KickPitchDegrees, 0f, damp);
		m_KickYawDegrees = Mathf.Lerp(m_KickYawDegrees, 0f, damp);
		ClampKickToGameplayCap();

		Quaternion kickRotation = Quaternion.Euler(-m_KickPitchDegrees, m_KickYawDegrees, 0f);
		m_KickTarget.localRotation = animRot * kickRotation;

		m_LastRotationAfterOurApply = m_KickTarget.localRotation;
		m_LastDisplayedKickRotation = kickRotation;
		m_AppliedKickLastFrame = true;
	}
	#endregion

	#region Private Methods
	private void HandleEquipmentChanged()
	{
		RefreshKickTarget(true);
	}

	private void HandleShotFired(AmmoDefinition _ammoDefinition)
	{
		if (m_WeaponRuntime == null || m_WeaponRuntime.CurrentWeaponDefinition == null)
			return;
		if (m_Equipment == null)
			return;

		Transform kickTarget = ResolveKickTarget();
		if (kickTarget == null)
			return;
		if (kickTarget != m_KickTarget)
			RefreshKickTarget(true);
		if (m_KickTarget == null)
			return;

		WeaponFireMode fireMode = m_FireController != null
			? m_FireController.ResolveEffectiveFireMode()
			: WeaponFireMode.SemiAuto;
		float attachmentRecoilModifier = m_WeaponRuntime.RuntimeState != null
			? m_WeaponRuntime.RuntimeState.GetAttachmentRecoilProduct(fireMode)
			: 1f;

		float penaltyAdded = WeaponDefinition.ComputeAddedRecoilPenalty(
			m_WeaponRuntime.CurrentWeaponDefinition,
			fireMode,
			ammoDefinition: _ammoDefinition,
			attachmentRecoilModifier: attachmentRecoilModifier);

		if (penaltyAdded <= 0f)
			return;

		float pitch = penaltyAdded * m_PitchDegreesPerPenaltyUnit;
		float yawScale = pitch * m_YawJitterFraction;
		float yaw = yawScale == 0f ? 0f : Random.Range(-yawScale, yawScale);
		m_KickPitchDegrees += pitch;
		m_KickYawDegrees += yaw;
		ClampKickToGameplayCap();

		if (m_LogVisualKick)
		{
			Debug.Log(
				$"[VisualKick] shot penalty={penaltyAdded:F3} pitch+={pitch:F2} totalPitch={m_KickPitchDegrees:F2} target={m_KickTarget.name}",
				this);
		}
	}

	private void ClampKickToGameplayCap()
	{
		float maxPitch = ResolveMaxVisualPitchDegrees();
		if (maxPitch <= 0f)
		{
			m_KickPitchDegrees = 0f;
			m_KickYawDegrees = 0f;
			return;
		}

		m_KickPitchDegrees = Mathf.Clamp(m_KickPitchDegrees, 0f, maxPitch);
		float maxYaw = maxPitch * m_YawJitterFraction;
		m_KickYawDegrees = Mathf.Clamp(m_KickYawDegrees, -maxYaw, maxYaw);
	}

	private float ResolveMaxVisualPitchDegrees()
	{
		float maxPenalty = m_RecoilController != null ? m_RecoilController.MaxRecoilPenalty : 0f;
		if (maxPenalty <= 0f && m_WeaponRuntime != null && m_WeaponRuntime.TransientState != null)
			maxPenalty = m_WeaponRuntime.TransientState.RecoilPenalty;

		if (maxPenalty <= 0f)
			return 0f;

		return maxPenalty * m_PitchDegreesPerPenaltyUnit;
	}

	private Transform ResolveKickTarget()
	{
		if (m_KickTransformOverride != null)
			return m_KickTransformOverride;

		EquippedWeapon equipped = m_Equipment != null ? m_Equipment.EquippedWeapon : null;
		if (equipped != null && equipped.VisualRecoilKickPivot != null)
			return equipped.VisualRecoilKickPivot;

		return m_Equipment != null ? m_Equipment.MainWeaponRoot : null;
	}

	private void RefreshKickTarget(bool _resetKick)
	{
		Transform newTarget = ResolveKickTarget();
		bool targetChanged = newTarget != m_KickTarget;

		if (targetChanged && m_KickTarget != null)
			StripKickOnTransform(m_KickTarget);

		m_KickTarget = newTarget;

		if (m_KickTarget == null)
		{
			ResetKickState();
			return;
		}

		if (_resetKick || targetChanged)
			ResetKickState();
	}

	private float ResolveVisualRecoveryPerSecond()
	{
		WeaponDefinition wd = m_WeaponRuntime != null ? m_WeaponRuntime.CurrentWeaponDefinition : null;
		float recovery = wd != null
			? Mathf.Max(0.01f, wd.RecoilRecoveryPerSecond * m_VisualRecoveryFromWeaponScale)
			: m_FallbackVisualRecovery;

		if (m_FireController != null && m_FireController.IsFiringCommandActive)
			recovery *= m_RecoveryWhileFiringMultiplier;

		return recovery;
	}

	private void StripKickOnTransform(Transform _target)
	{
		if (_target == null)
			return;

		_target.localRotation = _target.localRotation * Quaternion.Inverse(m_LastDisplayedKickRotation);
	}

	private void ResetKickState()
	{
		m_KickPitchDegrees = 0f;
		m_KickYawDegrees = 0f;
		m_LastDisplayedKickRotation = Quaternion.identity;
		m_AppliedKickLastFrame = false;
	}
	#endregion
}
