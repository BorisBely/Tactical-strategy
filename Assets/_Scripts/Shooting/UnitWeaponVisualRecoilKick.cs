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
	#region Temporary
	private static readonly bool s_VisualKickEnabled = true;
	#endregion

	#region Serialized Fields
	[Tooltip("Снаряжение: корень оружия в руке.")]
	[SerializeField] private UnitEquipment m_Equipment;
	[Tooltip("Режим огня и определение оружия для расчёта импульса.")]
	[SerializeField] private UnitWeaponRuntime m_WeaponRuntime;
	[SerializeField] private UnitWeaponFireController m_FireController;
	[SerializeField] private UnitWeaponRecoilController m_RecoilController;
	[SerializeField] private UnitRagdollController m_RagdollController;
	[Tooltip("Редко: явная цель kick. Иначе — Visual Recoil Kick Pivot на EquippedWeapon, иначе корень оружия целиком.")]
	[SerializeField] private Transform m_KickTransformOverride;

	[Header("Импульс от штрафа отдачи")]
	[Tooltip("Градусы подъёма (локальный Euler X) на единицу добавленного RecoilPenalty за выстрел. Ограничивается потолком штрафа из RecoilController.")]
	[SerializeField, Min(0f)] private float m_PitchDegreesPerPenaltyUnit = 1f;
	[Tooltip("Случайный yaw (локальный Y) как доля от pitch-импульса этого выстрела.")]
	[SerializeField, Range(0f, 1f)] private float m_YawJitterFraction = 0.55f;

	[Header("Full Auto Visual Compensation")]
	[Tooltip("С какого выстрела очереди визуальный kick начинает ослаблять вертикаль (как в hitscan-паттерне).")]
	[SerializeField, Min(1)] private int m_FullAutoRecoilControlStartShot = 5;
	[Tooltip("К какому номеру выстрела компенсация выходит на полную силу.")]
	[SerializeField, Min(1)] private int m_FullAutoRecoilControlEndShot = 10;
	[Tooltip("Оставшаяся доля вертикального kick при полной компенсации.")]
	[SerializeField, Range(0.1f, 1f)] private float m_FullAutoControlledPitchScale = 0.38f;
	[Tooltip("Боковой увод при полной компенсации считается от полного pitch-импульса, не от ослабленного.")]
	[SerializeField, Range(0.5f, 1.5f)] private float m_FullAutoControlledYawReferenceScale = 1f;
	[Tooltip("Множитель бокового увода при полной компенсации.")]
	[SerializeField, Min(0.5f)] private float m_FullAutoControlledYawBoost = 1.2f;
	[Tooltip("Доля бокового увода от pitch-импульса при полной компенсации (> Yaw Jitter Fraction — доминирует горизонталь).")]
	[SerializeField, Range(0f, 2f)] private float m_FullAutoControlledYawFraction = 1.05f;
	[Tooltip("Небольшая неровность бокового покачивания в длинной очереди.")]
	[SerializeField, Range(0f, 1f)] private float m_FullAutoYawChaosFraction = 0.22f;
	[Tooltip("Насколько RecoilControl юнита усиливает компенсацию (0 = одинаково для всех).")]
	[SerializeField, Range(0f, 1f)] private float m_FullAutoRecoilControlSkillInfluence = 0.65f;
	[SerializeField] private UnitCombatStats m_CombatStats;

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
		if (m_RagdollController == null)
			m_RagdollController = GetComponent<UnitRagdollController>();
		if (m_CombatStats == null)
			m_CombatStats = UnitCombatStatsLookup.ResolveOnUnit(this);
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
		if (!s_VisualKickEnabled)
			return;

		if (m_RagdollController != null && m_RagdollController.ShouldBlockWeaponPoseScripts)
			return;

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
		if (!s_VisualKickEnabled)
			return;

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

		int shotIndex = ResolveCurrentBurstShotIndex();
		float basePitch = penaltyAdded * m_PitchDegreesPerPenaltyUnit;
		float pitch = basePitch;
		float yaw = 0f;

		bool isAutomatic = WeaponFireModeUtility.IsAutomaticEffectiveMode(fireMode);
		bool isFirstInSeries = WeaponFireModeUtility.IsFirstShotInAutomaticSeries(fireMode, shotIndex);
		if (isAutomatic && !isFirstInSeries)
		{
			float controlBlend = CalculateFullAutoRecoilControlBlend(fireMode, shotIndex);
			float pitchScale = Mathf.Lerp(1f, m_FullAutoControlledPitchScale, controlBlend);
			pitch = basePitch * pitchScale;

			float yawReferencePitch = basePitch * Mathf.Lerp(1f, m_FullAutoControlledYawReferenceScale, controlBlend);
			float yawFraction = Mathf.Lerp(m_YawJitterFraction, m_FullAutoControlledYawFraction, controlBlend);
			float yawBoost = Mathf.Lerp(1f, m_FullAutoControlledYawBoost, controlBlend);
			if (fireMode == WeaponFireMode.FullAuto && controlBlend > 0.0001f)
				yaw = CalculateProceduralVisualYaw(shotIndex, yawReferencePitch, yawFraction) * yawBoost;
			else
				yaw = CreateRandomYawImpulse(pitch, yawFraction);
		}
		else
		{
			yaw = CreateRandomYawImpulse(pitch, m_YawJitterFraction);
		}

		m_KickPitchDegrees += pitch;
		m_KickYawDegrees += yaw;
		ClampKickToGameplayCap();

		if (m_LogVisualKick)
		{
			Debug.Log(
				$"[VisualKick] shot={shotIndex} mode={fireMode} penalty={penaltyAdded:F3} pitch+={pitch:F2} yaw+={yaw:F2} totalPitch={m_KickPitchDegrees:F2} target={m_KickTarget.name}",
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

		ResolveEffectiveKickCap(maxPitch, out float effectiveMaxPitch, out float effectiveMaxYaw);
		m_KickPitchDegrees = Mathf.Clamp(m_KickPitchDegrees, 0f, effectiveMaxPitch);
		m_KickYawDegrees = Mathf.Clamp(m_KickYawDegrees, -effectiveMaxYaw, effectiveMaxYaw);
	}

	private void ResolveEffectiveKickCap(float _maxPitch, out float _effectiveMaxPitch, out float _effectiveMaxYaw)
	{
		_effectiveMaxPitch = _maxPitch;
		float yawFraction = m_YawJitterFraction;
		float yawBoost = 1f;

		if (m_FireController != null &&
		    m_FireController.IsFiringCommandActive &&
		    m_FireController.ResolveEffectiveFireMode() == WeaponFireMode.FullAuto &&
		    m_WeaponRuntime != null &&
		    m_WeaponRuntime.TransientState != null)
		{
			int shotIndex = m_WeaponRuntime.TransientState.ConsecutiveBurstShotsFired;
			float controlBlend = CalculateFullAutoRecoilControlBlend(WeaponFireMode.FullAuto, shotIndex);
			_effectiveMaxPitch = _maxPitch * Mathf.Lerp(1f, m_FullAutoControlledPitchScale, controlBlend);
			yawFraction = Mathf.Lerp(m_YawJitterFraction, m_FullAutoControlledYawFraction, controlBlend);
			yawBoost = Mathf.Lerp(1f, m_FullAutoControlledYawBoost, controlBlend);
		}

		_effectiveMaxYaw = _effectiveMaxPitch * yawFraction * yawBoost;
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

	private int ResolveCurrentBurstShotIndex()
	{
		if (m_WeaponRuntime == null || m_WeaponRuntime.TransientState == null)
			return 1;

		return Mathf.Max(1, m_WeaponRuntime.TransientState.ConsecutiveBurstShotsFired);
	}

	private float CalculateFullAutoRecoilControlBlend(WeaponFireMode _effectiveFireMode, int _shotIndex)
	{
		if (_effectiveFireMode != WeaponFireMode.FullAuto)
			return 0f;

		int startShot = Mathf.Max(1, m_FullAutoRecoilControlStartShot);
		int endShot = Mathf.Max(startShot + 1, m_FullAutoRecoilControlEndShot);
		if (_shotIndex <= startShot)
			return 0f;

		float shotBlend = Mathf.InverseLerp(startShot, endShot, _shotIndex);
		if (shotBlend <= 0f)
			return 0f;

		float skill01 = ResolveRecoilControlSkill01();
		float skillInfluence = Mathf.Clamp01(m_FullAutoRecoilControlSkillInfluence);
		return Mathf.Clamp01(Mathf.Lerp(shotBlend, shotBlend * skill01, skillInfluence));
	}

	private float ResolveRecoilControlSkill01()
	{
		if (m_CombatStats == null)
			m_CombatStats = UnitCombatStatsLookup.ResolveOnUnit(this);
		if (m_CombatStats == null)
			return 0.5f;

		return Mathf.InverseLerp(0f, 100f, m_CombatStats.RecoilControl);
	}

	private float CalculateProceduralVisualYaw(int _shotIndex, float _pitchDegrees, float _yawFraction)
	{
		WeaponDefinition weaponDefinition = m_WeaponRuntime != null ? m_WeaponRuntime.CurrentWeaponDefinition : null;
		float seed = weaponDefinition != null ? Mathf.Abs(weaponDefinition.GetInstanceID() % 997) * 0.01f : 0f;
		float mainWave = Mathf.Sin(_shotIndex * 1.73f + seed);
		float chaosWave = Mathf.Sin(_shotIndex * 0.47f + seed * 2.31f) * m_FullAutoYawChaosFraction;
		return (mainWave + chaosWave) * _pitchDegrees * _yawFraction;
	}

	private static float CreateRandomYawImpulse(float _pitchDegrees, float _yawFraction)
	{
		float yawScale = _pitchDegrees * _yawFraction;
		return yawScale == 0f ? 0f : Random.Range(-yawScale, yawScale);
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

	public void ResetVisualKick()
	{
		if (m_KickTarget != null)
			StripKickOnTransform(m_KickTarget);

		ResetKickState();
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
