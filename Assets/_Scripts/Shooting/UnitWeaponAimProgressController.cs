using UnityEngine;

/// <summary>
/// Обновляет AimProgress оружия по времени прицеливания, стойке, движению, смене цели и сбитию прицела после выстрела.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(57)]
public sealed class UnitWeaponAimProgressController : MonoBehaviour
{
	#region Serialized Fields
	[Tooltip("Runtime оружия, где хранится transient aim progress.")]
	[SerializeField] private UnitWeaponRuntime m_WeaponRuntime;
	[Tooltip("Состояние ready: без него боевое прицеливание не накапливается.")]
	[SerializeField] private UnitWeaponReadyHandsLayer m_ReadyHands;
	[Tooltip("Видимая цель, при необходимости обязательная для роста AimProgress.")]
	[SerializeField] private UnitVision m_Vision;
	[SerializeField] private UnitClickToMove m_ClickToMove;
	[SerializeField] private UnitNavLocomotionDriver m_LocomotionDriver;
	[SerializeField] private UnitAnimatorStance m_Stance;
	[SerializeField] private UnitBusyState m_BusyState;
	[SerializeField] private UnitWeaponReloadController m_ReloadController;
	[SerializeField] private UnitMagazineLoadingController m_MagazineLoadingController;
	[SerializeField] private UnitWeaponFireController m_FireController;
	[SerializeField] private UnitCombatStats m_CombatStats;
	[SerializeField] private UnitCombatCondition m_CombatCondition;

	[Header("Aim Conditions")]
	[Tooltip("Если true, AimProgress растёт только когда оружие в ready.")]
	[SerializeField] private bool m_RequireReady = true;
	[Tooltip("Если true, AimProgress растёт только при наличии видимой цели.")]
	[SerializeField] private bool m_RequireVisibleTarget = true;
	[Tooltip("Если стойка сейчас в переходе, прицеливание не растёт и плавно теряется.")]
	[SerializeField] private bool m_BlockDuringStanceTransition = true;
	[Tooltip("Не накапливать AimProgress во время перезарядки и передёргивания затвора (UnitWeaponReloadController.IsReloadBusy).")]
	[SerializeField] private bool m_BlockDuringReloadOrBoltCycle = true;

	[Header("Aim Time Multipliers")]
	[Tooltip("Множитель времени прицеливания стоя.")]
	[SerializeField, Min(0.01f)] private float m_StandingAimTimeMultiplier = 1f;
	[Tooltip("Множитель времени прицеливания в присяде.")]
	[SerializeField, Min(0.01f)] private float m_CrouchAimTimeMultiplier = 0.82f;
	[Tooltip("Множитель времени прицеливания лёжа.")]
	[SerializeField, Min(0.01f)] private float m_ProneAimTimeMultiplier = 0.68f;
	[Tooltip("Дополнительный множитель времени при наличии движения.")]
	[SerializeField, Min(0.01f)] private float m_MovingAimTimeMultiplier = 1.55f;
	[Tooltip("Дополнительный множитель времени в спринтовом режиме.")]
	[SerializeField, Min(0.01f)] private float m_SprintAimTimeMultiplier = 2.25f;

	[Header("Aim Loss")]
	[Tooltip("Насколько быстрее AimProgress теряется, чем набирается, когда условия прицеливания сорваны.")]
	[SerializeField, Min(0.01f)] private float m_AimLossSpeedMultiplier = 1.65f;
	[Tooltip("При смене цели текущий прогресс умножается на это значение.")]
	[SerializeField, Range(0f, 1f)] private float m_TargetSwitchRetainProgress01 = 0.25f;

	[Header("Post Shot Re-Aim")]
	[Tooltip("После успешного выстрела отдача сбивает AimProgress, и следующий выстрел ждёт повторного наведения.")]
	[SerializeField] private bool m_ReduceAimProgressAfterShot = true;
	[Tooltip("Сколько AimProgress теряется на единицу добавленного RecoilPenalty.")]
	[SerializeField, Range(0f, 1f)] private float m_AimLossPerRecoilPenaltyUnit = 0.65f;
	[Tooltip("Ниже этого значения AimProgress после выстрела не опускается.")]
	[SerializeField, Range(0f, 1f)] private float m_MinAimProgressAfterShot = 0f;

	[Header("Debug")]
	[SerializeField, Min(0.01f)] private float m_DebugCurrentAimTimeSeconds = 0.25f;
	[SerializeField] private bool m_DebugCanAccumulateAim;
	[SerializeField] private Transform m_DebugCurrentTarget;
	[SerializeField, Min(0.01f)] private float m_DebugSkillAimTimeMultiplier = 1f;
	[SerializeField, Min(0.01f)] private float m_DebugConditionAimTimeMultiplier = 1f;
	[SerializeField, Min(0f)] private float m_DebugLastShotAimLoss;
	[SerializeField, Range(0f, 1f)] private float m_DebugAimProgressAfterLastShot;
	#endregion

	#region Private Fields
	private Transform m_LastVisibleTarget;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_WeaponRuntime == null)
			m_WeaponRuntime = GetComponent<UnitWeaponRuntime>();
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
		if (m_Vision == null)
			m_Vision = GetComponent<UnitVision>();
		if (m_ClickToMove == null)
			m_ClickToMove = GetComponent<UnitClickToMove>();
		if (m_LocomotionDriver == null)
			m_LocomotionDriver = GetComponent<UnitNavLocomotionDriver>();
		if (m_Stance == null)
			m_Stance = GetComponent<UnitAnimatorStance>();
		if (m_BusyState == null)
			m_BusyState = GetComponent<UnitBusyState>();
		if (m_ReloadController == null)
			m_ReloadController = GetComponent<UnitWeaponReloadController>();
		if (m_MagazineLoadingController == null)
			m_MagazineLoadingController = GetComponent<UnitMagazineLoadingController>();
		if (m_FireController == null)
			m_FireController = GetComponent<UnitWeaponFireController>();
		if (m_CombatStats == null)
			m_CombatStats = GetComponent<UnitCombatStats>();
		if (m_CombatCondition == null)
			m_CombatCondition = GetComponent<UnitCombatCondition>();
	}

	private void OnEnable()
	{
		if (m_Vision != null)
			m_Vision.VisibleTargetChanged += HandleVisibleTargetChanged;
		if (m_FireController != null)
			m_FireController.ShotFired += HandleShotFired;

		m_LastVisibleTarget = m_Vision != null ? m_Vision.VisibleTarget : null;
	}

	private void OnDisable()
	{
		if (m_Vision != null)
			m_Vision.VisibleTargetChanged -= HandleVisibleTargetChanged;
		if (m_FireController != null)
			m_FireController.ShotFired -= HandleShotFired;
	}

	private void Update()
	{
		if (m_WeaponRuntime == null || m_WeaponRuntime.CurrentWeaponDefinition == null)
			return;

		float currentProgress = m_WeaponRuntime.TransientState.AimProgress01;
		float aimTimeSeconds = CalculateCurrentAimTimeSeconds();
		bool canAccumulateAim = CanAccumulateAim();
		float nextProgress;

		if (canAccumulateAim)
			nextProgress = Mathf.MoveTowards(currentProgress, 1f, Time.deltaTime / aimTimeSeconds);
		else
			nextProgress = Mathf.MoveTowards(currentProgress, 0f, (Time.deltaTime / aimTimeSeconds) * m_AimLossSpeedMultiplier);

		m_WeaponRuntime.SetAimProgress(nextProgress);
		m_DebugCurrentAimTimeSeconds = aimTimeSeconds;
		m_DebugCanAccumulateAim = canAccumulateAim;
		m_DebugCurrentTarget = m_Vision != null ? m_Vision.VisibleTarget : null;
	}
	#endregion

	#region Private Methods
	private bool CanAccumulateAim()
	{
		if (m_WeaponRuntime == null || m_WeaponRuntime.CurrentWeaponDefinition == null)
			return false;

		if (m_RequireReady && (m_ReadyHands == null || !m_ReadyHands.IsWeaponEquippedAndReady()))
			return false;

		if (m_RequireVisibleTarget && (m_Vision == null || m_Vision.VisibleTarget == null))
			return false;

		if (m_BlockDuringStanceTransition &&
			m_BusyState != null &&
			m_BusyState.IsBusy &&
			(m_BusyState.Reasons & UnitBusyState.BusyReason.StanceTransition) != 0)
		{
			return false;
		}

		if (m_BlockDuringReloadOrBoltCycle &&
			m_ReloadController != null &&
			m_ReloadController.IsReloadBusy)
			return false;

		if (m_MagazineLoadingController != null && m_MagazineLoadingController.IsLoadingMagazine)
			return false;

		return true;
	}

	private float CalculateCurrentAimTimeSeconds()
	{
		WeaponDefinition weaponDefinition = m_WeaponRuntime != null ? m_WeaponRuntime.CurrentWeaponDefinition : null;
		float aimTimeSeconds = weaponDefinition != null ? weaponDefinition.AimTimeSeconds : 0.25f;
		float targetDistanceMeters = EstimateTargetDistanceMeters();
		if (weaponDefinition != null)
			aimTimeSeconds *= weaponDefinition.GetDistanceAimTimeMultiplier(targetDistanceMeters);
		WeaponRuntimeState weaponState = m_WeaponRuntime != null ? m_WeaponRuntime.RuntimeState : null;
		if (weaponState != null)
		{
			aimTimeSeconds *= weaponState.GetAttachmentAimTimeProduct();
			aimTimeSeconds *= weaponState.GetAttachmentDistanceAimTimeProduct(targetDistanceMeters);
		}
		aimTimeSeconds *= GetStanceAimTimeMultiplier();
		aimTimeSeconds *= GetMovementAimTimeMultiplier();
		float skillMultiplier = m_CombatStats != null ? m_CombatStats.GetAimTimeMultiplier() : 1f;
		float conditionMultiplier = m_CombatCondition != null ? m_CombatCondition.GetAimTimeMultiplier(IsMoving()) : 1f;
		aimTimeSeconds *= skillMultiplier;
		aimTimeSeconds *= conditionMultiplier;
		m_DebugSkillAimTimeMultiplier = skillMultiplier;
		m_DebugConditionAimTimeMultiplier = conditionMultiplier;
		return Mathf.Max(0.01f, aimTimeSeconds);
	}

	private float EstimateTargetDistanceMeters()
	{
		Transform target = m_Vision != null ? m_Vision.VisibleTarget : null;
		if (target == null)
			return 0f;

		Vector3 targetPoint = m_Vision.GetVisibleTargetAimPointWorld();
		if (targetPoint == Vector3.zero)
			targetPoint = target.position;

		return Vector3.Distance(transform.position, targetPoint);
	}

	private float GetStanceAimTimeMultiplier()
	{
		LocomotionStance stance = m_Stance != null ? m_Stance.CurrentStance : LocomotionStance.Standing;
		return stance switch
		{
			LocomotionStance.Crouch => m_CrouchAimTimeMultiplier,
			LocomotionStance.Prone => m_ProneAimTimeMultiplier,
			_ => m_StandingAimTimeMultiplier
		};
	}

	private float GetMovementAimTimeMultiplier()
	{
		bool isMoving = IsMoving();
		bool isSprinting = IsSprinting();

		if (isSprinting)
			return m_SprintAimTimeMultiplier;
		if (isMoving)
			return m_MovingAimTimeMultiplier;

		return 1f;
	}

	private bool IsMoving()
	{
		bool isMoving = false;

		if (m_LocomotionDriver != null)
			isMoving = m_LocomotionDriver.HasMoveIntent;
		else if (m_ClickToMove != null)
			isMoving = m_ClickToMove.HasMoveIntent;

		return isMoving;
	}

	private bool IsSprinting()
	{
		if (m_LocomotionDriver != null)
			return m_LocomotionDriver.IsSprintMoveMode;
		return m_ClickToMove != null && m_ClickToMove.IsSprintMoveMode;
	}

	private void HandleShotFired(AmmoDefinition _ammoDefinition)
	{
		if (!m_ReduceAimProgressAfterShot || m_WeaponRuntime == null || m_WeaponRuntime.CurrentWeaponDefinition == null)
			return;

		float aimLoss = CalculateShotAimLoss(_ammoDefinition);
		if (aimLoss <= 0f)
			return;

		float currentProgress = m_WeaponRuntime.TransientState.AimProgress01;
		float nextProgress = Mathf.Max(m_MinAimProgressAfterShot, currentProgress - aimLoss);
		m_WeaponRuntime.SetAimProgress(nextProgress);
		m_DebugLastShotAimLoss = aimLoss;
		m_DebugAimProgressAfterLastShot = nextProgress;
	}

	private float CalculateShotAimLoss(AmmoDefinition _ammoDefinition)
	{
		WeaponDefinition weaponDefinition = m_WeaponRuntime.CurrentWeaponDefinition;
		WeaponFireMode fireMode = m_WeaponRuntime.RuntimeState != null
			? m_WeaponRuntime.RuntimeState.SelectedFireMode
			: WeaponFireMode.SemiAuto;

		float attachmentModifier = m_WeaponRuntime.RuntimeState != null
			? m_WeaponRuntime.RuntimeState.GetAttachmentRecoilProduct()
			: 1f;
		float skillMultiplier = m_CombatStats != null ? m_CombatStats.GetRecoilAddedMultiplier() : 1f;
		float conditionMultiplier = m_CombatCondition != null ? m_CombatCondition.GetRecoilAddedMultiplier() : 1f;
		float recoilAdded = WeaponDefinition.ComputeAddedRecoilPenalty(
			weaponDefinition,
			fireMode,
			_ammoDefinition,
			attachmentModifier) * skillMultiplier * conditionMultiplier;

		return Mathf.Clamp01(recoilAdded * m_AimLossPerRecoilPenaltyUnit);
	}

	private void HandleVisibleTargetChanged(Transform _newVisibleTarget)
	{
		if (_newVisibleTarget == m_LastVisibleTarget)
			return;

		m_LastVisibleTarget = _newVisibleTarget;
		if (m_WeaponRuntime == null)
			return;

		float reducedProgress = m_WeaponRuntime.TransientState.AimProgress01 * m_TargetSwitchRetainProgress01;
		m_WeaponRuntime.SetAimProgress(reducedProgress);
	}
	#endregion
}
