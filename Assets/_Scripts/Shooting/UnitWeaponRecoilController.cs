using UnityEngine;

/// <summary>
/// Gameplay recoil: accumulated RecoilOffset in degrees.
/// Does not write bones or weapon local TRS. Does not widen the hitscan cone.
/// Visual recoil (UnitWeaponRecoil) subscribes to ShotFired directly.
/// Pause A: StopFiring does not clear RecoilOffset; recovery continues at full rate.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(58)]
public sealed class UnitWeaponRecoilController : MonoBehaviour
{
	[Tooltip("Runtime оружия, где хранится RecoilOffset.")]
	[SerializeField] private UnitWeaponRuntime m_WeaponRuntime;
	[Tooltip("Источник события успешного выстрела.")]
	[SerializeField] private UnitWeaponFireController m_FireController;
	[Tooltip("Проверка ready для логики восстановления отдачи.")]
	[SerializeField] private UnitWeaponReadyHandsLayer m_ReadyHands;
	[SerializeField] private UnitCombatStats m_CombatStats;
	[SerializeField] private UnitIndividualTraits m_IndividualTraits;
	[SerializeField] private UnitCombatCondition m_CombatCondition;
	[SerializeField] private UnitStanceCombatModifiers m_StanceCombatModifiers;
	[SerializeField] private UnitEquipment m_Equipment;

	[Header("Recovery")]
	[Tooltip("Страховочный потолок |RecoilOffset| в градусах.")]
	[SerializeField, Min(0.1f)] private float m_MaxRecoilOffsetDegrees = WeaponRecoilMath.DefaultMaxOffsetDegrees;
	[Tooltip("Множитель восстановления отдачи, пока удерживается огонь.")]
	[SerializeField, Min(0f)] private float m_RecoveryWhileFiringMultiplier = 0.7f;
	[Tooltip("Множитель восстановления отдачи, если оружие сейчас не на ready.")]
	[SerializeField, Min(0f)] private float m_RecoveryWhenNotReadyMultiplier = 1.2f;

	[Header("Debug")]
	[SerializeField] private Vector2 m_DebugLastKick;
	[SerializeField, Min(0f)] private float m_DebugLastRecoveryPerSecond;
	[SerializeField, Min(0.01f)] private float m_DebugSkillRecoilAddedMultiplier = 1f;
	[SerializeField, Min(0.01f)] private float m_DebugConditionRecoilAddedMultiplier = 1f;
	[SerializeField, Min(0.01f)] private float m_DebugSkillRecoveryMultiplier = 1f;
	[SerializeField, Min(0.01f)] private float m_DebugConditionRecoveryMultiplier = 1f;

	public float MaxRecoilOffsetDegrees => m_MaxRecoilOffsetDegrees;
	public int RecoilShotIndex =>
		m_WeaponRuntime != null && m_WeaponRuntime.TransientState != null
			? m_WeaponRuntime.TransientState.RecoilShotIndex
			: 0;

	public Vector2 RecoilOffset =>
		m_WeaponRuntime != null && m_WeaponRuntime.TransientState != null
			? m_WeaponRuntime.TransientState.RecoilOffset
			: Vector2.zero;
	public float RecoilOffsetMagnitude => RecoilOffset.magnitude;
	public float Stability01 => 1f - Mathf.Clamp01(RecoilOffsetMagnitude / Mathf.Max(0.01f, m_MaxRecoilOffsetDegrees));
	public float RecoveryWhileFiringMultiplier => m_RecoveryWhileFiringMultiplier;
	public bool IsRecoveringWhileFiring =>
		m_FireController != null && m_FireController.IsFiringCommandActive;
	public Vector2 LastKick { get; private set; }
	public float LastVisualImpulse { get; private set; }

	private void Awake()
	{
		if (m_WeaponRuntime == null)
			m_WeaponRuntime = GetComponent<UnitWeaponRuntime>();
		if (m_FireController == null)
			m_FireController = GetComponent<UnitWeaponFireController>();
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
		if (m_CombatStats == null)
			m_CombatStats = GetComponent<UnitCombatStats>();
		if (m_IndividualTraits == null)
			m_IndividualTraits = GetComponent<UnitIndividualTraits>();
		if (m_CombatCondition == null)
			m_CombatCondition = GetComponent<UnitCombatCondition>();
		if (m_StanceCombatModifiers == null)
			m_StanceCombatModifiers = GetComponent<UnitStanceCombatModifiers>();
		if (m_Equipment == null)
			m_Equipment = GetComponent<UnitEquipment>();
	}

	private void OnEnable()
	{
		if (m_FireController != null)
			m_FireController.ShotFired += HandleShotFired;
	}

	private void OnDisable()
	{
		if (m_FireController != null)
			m_FireController.ShotFired -= HandleShotFired;
	}

	private void Update()
	{
		if (m_WeaponRuntime == null || m_WeaponRuntime.TransientState == null ||
		    m_WeaponRuntime.CurrentWeaponDefinition == null)
			return;

		Vector2 current = m_WeaponRuntime.TransientState.RecoilOffset;
		if (current.sqrMagnitude <= 1e-10f)
		{
			m_DebugLastRecoveryPerSecond = 0f;
			return;
		}

		float recoveryPerSecond = CalculateCurrentRecoveryPerSecond();
		m_DebugLastRecoveryPerSecond = recoveryPerSecond;
		Vector2 next = WeaponRecoilMath.Recover(current, recoveryPerSecond, Time.deltaTime);
		if (next != current)
			m_WeaponRuntime.SetRecoilOffset(next);
	}

	public float GetCurrentRecoveryPerSecond()
	{
		return CalculateCurrentRecoveryPerSecond();
	}

	public float ComputeVisualImpulsePerShot(AmmoDefinition _ammoDefinition)
	{
		WeaponRecoilKick kick = CalculateKick(_ammoDefinition);
		return kick.VisualImpulse;
	}

	public Vector2 PredictOffsetAfterShots(int _shotCount)
	{
		WeaponRecoilContext context = BuildContext(null);
		return WeaponRecoilMath.PredictOffsetAfterShots(in context, _shotCount);
	}

	public Vector2 PredictOffsetAfterBurstAndPause(int _burstShotCount, float _pauseSeconds)
	{
		WeaponRecoilContext context = BuildContext(null);
		return WeaponRecoilMath.PredictOffsetAfterBurstAndPause(in context, _burstShotCount, _pauseSeconds);
	}

	public void ResetRecoilOffset()
	{
		if (m_WeaponRuntime == null)
			return;

		m_WeaponRuntime.SetRecoilOffset(Vector2.zero, 0f, 0);
		LastKick = Vector2.zero;
		LastVisualImpulse = 0f;
		m_DebugLastKick = Vector2.zero;
		m_DebugLastRecoveryPerSecond = 0f;
	}

	private void HandleShotFired(AmmoDefinition _ammoDefinition)
	{
		if (m_WeaponRuntime == null || m_WeaponRuntime.TransientState == null ||
		    m_WeaponRuntime.CurrentWeaponDefinition == null)
			return;

		EquippedWeaponTransientState transient = m_WeaponRuntime.TransientState;
		WeaponRecoilKick kick = CalculateKick(_ammoDefinition, transient);
		Vector2 nextOffset = WeaponRecoilMath.ApplyKick(
			transient.RecoilOffset,
			kick.Delta,
			m_MaxRecoilOffsetDegrees);
		m_WeaponRuntime.SetRecoilOffset(nextOffset, kick.PatternValue, transient.RecoilShotIndex + 1);
		LastKick = kick.Delta;
		LastVisualImpulse = kick.VisualImpulse;
		m_DebugLastKick = kick.Delta;
	}

	private WeaponRecoilKick CalculateKick(AmmoDefinition _ammoDefinition)
	{
		EquippedWeaponTransientState transient = m_WeaponRuntime != null ? m_WeaponRuntime.TransientState : null;
		return CalculateKick(_ammoDefinition, transient);
	}

	private WeaponRecoilKick CalculateKick(
		AmmoDefinition _ammoDefinition,
		EquippedWeaponTransientState _transient)
	{
		WeaponRecoilContext context = BuildContext(_ammoDefinition);
		if (context.WeaponDefinition == null)
			return new WeaponRecoilKick(Vector2.zero, 0f, 0f);

		int shotIndex = (_transient != null ? _transient.RecoilShotIndex : 0) + 1;
		float previousPattern = _transient != null ? _transient.RecoilPatternValue : 0f;
		return WeaponRecoilMath.ComputeKick(in context, shotIndex, previousPattern);
	}

	private float CalculateCurrentRecoveryPerSecond()
	{
		WeaponRecoilContext context = BuildContext(null);
		if (context.WeaponDefinition == null)
			return 0f;

		bool isFiring = m_FireController != null && m_FireController.IsFiringCommandActive;
		bool isReady = m_ReadyHands == null || m_ReadyHands.IsWeaponEquippedAndReady();
		return WeaponRecoilMath.ComposeRecoveryPerSecond(in context, isFiring, isReady);
	}

	private WeaponRecoilContext BuildContext(AmmoDefinition _ammoDefinition)
	{
		WeaponDefinition weaponDefinition = m_WeaponRuntime != null
			? m_WeaponRuntime.CurrentWeaponDefinition
			: null;
		WeaponFireMode fireMode = m_FireController != null
			? m_FireController.ResolveEffectiveFireMode()
			: WeaponFireMode.SemiAuto;
		WeaponRecoilContext context = WeaponRecoilContext.CreateBaseline(weaponDefinition, fireMode);
		context.AmmoDefinition = _ammoDefinition;
		context.MaxOffsetDegrees = m_MaxRecoilOffsetDegrees;
		context.InstanceHash = GetEntityId().GetHashCode();
		context.RecoveryWhileFiringMultiplier = m_RecoveryWhileFiringMultiplier;
		context.RecoveryWhenNotReadyMultiplier = m_RecoveryWhenNotReadyMultiplier;

		WeaponRuntimeState runtimeState = m_WeaponRuntime != null ? m_WeaponRuntime.RuntimeState : null;
		if (runtimeState != null)
		{
			context.AttachmentKickProduct = runtimeState.GetAttachmentRecoilProduct(fireMode);
			context.AttachmentVerticalProduct = runtimeState.GetAttachmentRecoilVerticalProduct();
			context.AttachmentHorizontalProduct = runtimeState.GetAttachmentRecoilHorizontalProduct();
			context.AttachmentRecoveryProduct = runtimeState.GetAttachmentRecoilRecoveryProduct();
		}

		float skillKick = m_CombatStats != null ? m_CombatStats.GetRecoilAddedMultiplier() : 1f;
		float traitsKick = m_IndividualTraits != null ? m_IndividualTraits.GetRecoilAddedMultiplier() : 1f;
		float conditionKick = m_CombatCondition != null ? m_CombatCondition.GetRecoilAddedMultiplier() : 1f;
		context.SkillKickMultiplier = skillKick;
		context.TraitsKickMultiplier = traitsKick;
		context.ConditionKickMultiplier = conditionKick;
		m_DebugSkillRecoilAddedMultiplier = skillKick * traitsKick;
		m_DebugConditionRecoilAddedMultiplier = conditionKick;

		float skillRecovery = m_CombatStats != null ? m_CombatStats.GetRecoilRecoveryMultiplier() : 1f;
		float traitsRecovery = m_IndividualTraits != null ? m_IndividualTraits.GetRecoilRecoveryMultiplier() : 1f;
		float conditionRecovery = m_CombatCondition != null ? m_CombatCondition.GetRecoilRecoveryMultiplier() : 1f;
		context.SkillRecoveryMultiplier = skillRecovery;
		context.TraitsRecoveryMultiplier = traitsRecovery;
		context.ConditionRecoveryMultiplier = conditionRecovery;
		m_DebugSkillRecoveryMultiplier = skillRecovery * traitsRecovery;
		m_DebugConditionRecoveryMultiplier = conditionRecovery;

		bool turret = m_Equipment != null && m_Equipment.IsOperatingVehicleTurret;
		if (!turret)
		{
			if (m_StanceCombatModifiers != null)
			{
				context.StanceKickMultiplier = m_StanceCombatModifiers.GetRecoilAddedMultiplier();
				context.StanceRecoveryMultiplier = m_StanceCombatModifiers.GetRecoilRecoveryMultiplier();
			}

			WeaponPoseState pose = m_ReadyHands != null
				? m_ReadyHands.EffectivePoseState
				: WeaponPoseState.Aiming;
			context.PoseKickMultiplier = WeaponPoseCombatModifiers.GetKickMultiplier(pose);
			context.PoseRecoveryMultiplier = WeaponPoseCombatModifiers.GetRecoveryMultiplier(pose);
		}

		return context;
	}
}
