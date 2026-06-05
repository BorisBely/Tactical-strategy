using UnityEngine;

/// <summary>
/// Отдача визуала после позы аниматора: в LateUpdate берётся локальный поворот (прицел/IK),
/// затем умножается накопленный kick. Позицию <c>localPosition</c> не меняем — сдвиг по осям давал накопление и конфликт с анимацией.
/// Цель: override на юните, иначе <see cref="EquippedWeapon.VisualRecoilKickPivot"/>, иначе корень инстанса.
/// Импульс — <see cref="WeaponDefinition.ComputeAddedRecoilPenalty"/>.
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
	[Tooltip("Редко: явная цель kick. Иначе — Visual Recoil Kick Pivot на EquippedWeapon, иначе корень оружия целиком.")]
	[SerializeField] private Transform m_KickTransformOverride;

	[Header("Импульс от штрафа отдачи")]
	[Tooltip("Градусы подъёма (локальный Euler X) на единицу добавленного RecoilPenalty за выстрел.")]
	[SerializeField, Min(0f)] private float m_PitchDegreesPerPenaltyUnit = 2.1f;
	[Tooltip("Случайный yaw (локальный Y) как доля от pitch-импульса этого выстрела.")]
	[SerializeField, Range(0f, 1f)] private float m_YawJitterFraction = 0.28f;

	[Header("Возврат")]
	[Tooltip("Множитель к WeaponDefinition.RecoilRecoveryPerSecond для затухания kick.")]
	[SerializeField, Min(0.01f)] private float m_VisualRecoveryFromWeaponScale = 0.85f;
	[Tooltip("Если нет WeaponDefinition — скорость затухания kick.")]
	[SerializeField, Min(0.01f)] private float m_FallbackVisualRecovery = 14f;

	[Header("Стабилизация")]
	[Tooltip("Если локальный поворот совпадает с прошлым кадром после нашего kick — вычитаем отображённый kick (иначе накапливается ошибка). Только угол; поза позиции не трогается.")]
	[SerializeField, Min(0.01f)] private float m_AnimatorReplaceAngleSlopDegrees = 0.35f;
	#endregion

	#region Private Fields
	private Transform m_KickTarget;
	private Quaternion m_KickRotationOffset = Quaternion.identity;
	private bool m_AppliedKickLastFrame;
	private Quaternion m_LastRotationAfterOurApply = Quaternion.identity;
	/// <summary>Последний kick, реально умноженный в трансформ (после decay). Для strip — не текущее m_KickRotationOffset после ShotFired в том же кадре.</summary>
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
		m_KickRotationOffset = Quaternion.identity;
		m_LastDisplayedKickRotation = Quaternion.identity;
		m_AppliedKickLastFrame = false;
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
		m_KickRotationOffset = Quaternion.Slerp(m_KickRotationOffset, Quaternion.identity, damp);

		m_KickTarget.localRotation = animRot * m_KickRotationOffset;

		m_LastRotationAfterOurApply = m_KickTarget.localRotation;
		m_LastDisplayedKickRotation = m_KickRotationOffset;
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

		WeaponFireMode fireMode = m_WeaponRuntime.RuntimeState != null
			? m_WeaponRuntime.RuntimeState.SelectedFireMode
			: WeaponFireMode.SemiAuto;
		float attachmentRecoilModifier = m_WeaponRuntime.RuntimeState != null
			? m_WeaponRuntime.RuntimeState.GetAttachmentRecoilProduct()
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
		// Unity's positive local X tilts a forward-facing barrel down on these weapon rigs.
		Quaternion shotKick = Quaternion.Euler(-pitch, yaw, 0f);
		m_KickRotationOffset = m_KickRotationOffset * shotKick;
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
			m_KickRotationOffset = Quaternion.identity;
			m_LastDisplayedKickRotation = Quaternion.identity;
			m_AppliedKickLastFrame = false;
			return;
		}

		if (_resetKick || targetChanged)
		{
			m_KickRotationOffset = Quaternion.identity;
			m_LastDisplayedKickRotation = Quaternion.identity;
			m_AppliedKickLastFrame = false;
		}
	}

	private float ResolveVisualRecoveryPerSecond()
	{
		WeaponDefinition wd = m_WeaponRuntime != null ? m_WeaponRuntime.CurrentWeaponDefinition : null;
		if (wd != null)
			return Mathf.Max(0.01f, wd.RecoilRecoveryPerSecond * m_VisualRecoveryFromWeaponScale);
		return m_FallbackVisualRecovery;
	}

	private void StripKickOnTransform(Transform _target)
	{
		if (_target == null)
			return;

		_target.localRotation = _target.localRotation * Quaternion.Inverse(m_LastDisplayedKickRotation);
	}
	#endregion
}
