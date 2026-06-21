using UnityEngine;

/// <summary>
/// Накапливает AimProgress до полного прицеливания (1.0). Время берётся только из оружия, модулей и дистанции до цели.
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
	[SerializeField] private UnitBusyState m_BusyState;
	[SerializeField] private UnitWeaponReloadController m_ReloadController;
	[SerializeField] private UnitMagazineLoadingController m_MagazineLoadingController;
	[SerializeField] private UnitWeaponFireController m_FireController;
	[SerializeField] private UnitCombatStats m_CombatStats;
	[SerializeField] private UnitIndividualTraits m_IndividualTraits;
	[SerializeField] private UnitCombatCondition m_CombatCondition;
	[SerializeField] private UnitStanceCombatModifiers m_StanceCombatModifiers;
	[SerializeField] private UnitClickToMove m_ClickToMove;
	[SerializeField] private UnitNavLocomotionDriver m_LocomotionDriver;
	[SerializeField] private UnitFallenDragController m_FallenDragController;

	[Header("Aim Conditions")]
	[Tooltip("Если true, AimProgress растёт только когда оружие в ready.")]
	[SerializeField] private bool m_RequireReady = true;
	[Tooltip("Если true, AimProgress растёт только при наличии видимой цели.")]
	[SerializeField] private bool m_RequireVisibleTarget = true;
	[Tooltip("Если стойка сейчас в переходе, прицеливание не растёт и плавно теряется.")]
	[SerializeField] private bool m_BlockDuringStanceTransition = true;
	[Tooltip("Не накапливать AimProgress во время перезарядки и передёргивания затвора (UnitWeaponReloadController.IsReloadBusy).")]
	[SerializeField] private bool m_BlockDuringReloadOrBoltCycle = true;

	[Header("Aim Loss")]
	[Tooltip("Насколько быстрее AimProgress теряется, чем набирается, когда условия прицеливания сорваны.")]
	[SerializeField, Min(0.01f)] private float m_AimLossSpeedMultiplier = 1.65f;

	[Header("Post Shot Re-Aim")]
	[Tooltip("После успешного выстрела сбрасывает прицел — следующий выстрел ждёт полного повторного наведения.")]
	[SerializeField] private bool m_ResetAimProgressAfterShot = true;

	[Header("Target Switch Aim Carryover")]
	[Tooltip("При смене цели — доля сохранения прицела при угле 0° между старой и новой точкой прицеливания.")]
	[SerializeField, Range(0f, 1f)] private float m_AimCarryoverMax = 0.8f;
	[Tooltip("При каком угле между старой и новой целью carryover падает до 0 (градусы).")]
	[SerializeField, Range(1f, 90f)] private float m_AimCarryoverHalfAngleDegrees = 25f;

	[Header("Debug")]
	[SerializeField, Min(0.01f)] private float m_DebugCurrentAimTimeSeconds = 0.25f;
	[SerializeField] private bool m_DebugCanAccumulateAim;
	[SerializeField] private Transform m_DebugCurrentTarget;
	#endregion

	#region Public Properties
	/// <summary>Полное время прицеливания: оружие × модули × дистанция до цели.</summary>
	public float CurrentAimTimeSeconds => m_DebugCurrentAimTimeSeconds;
	#endregion

	#region Private Fields
	private Transform m_LastVisibleTarget;
	private Vector3 m_LastValidAimPointWorld;
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
		if (m_IndividualTraits == null)
			m_IndividualTraits = GetComponent<UnitIndividualTraits>();
		if (m_CombatCondition == null)
			m_CombatCondition = GetComponent<UnitCombatCondition>();
		if (m_StanceCombatModifiers == null)
			m_StanceCombatModifiers = GetComponent<UnitStanceCombatModifiers>();
		if (m_ClickToMove == null)
			m_ClickToMove = GetComponent<UnitClickToMove>();
		if (m_LocomotionDriver == null)
			m_LocomotionDriver = GetComponent<UnitNavLocomotionDriver>();
		if (m_FallenDragController == null)
			m_FallenDragController = GetComponent<UnitFallenDragController>();
	}

	private void OnEnable()
	{
		if (m_Vision != null)
			m_Vision.VisibleTargetChanged += HandleVisibleTargetChanged;
		if (m_FireController != null)
			m_FireController.ShotFired += HandleShotFired;

		m_LastVisibleTarget = m_Vision != null ? m_Vision.GetEngageableVisibleTarget() : null;
		m_LastValidAimPointWorld = m_LastVisibleTarget != null && m_Vision != null
			? m_Vision.GetVisibleTargetAimPointWorld()
			: Vector3.zero;
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
		TrySyncEngagementTarget();

		if (m_WeaponRuntime == null || m_WeaponRuntime.CurrentWeaponDefinition == null)
			return;

		float currentProgress = m_WeaponRuntime.TransientState.AimProgress01;
		float aimTimeSeconds = CalculateCurrentAimTimeSeconds();
		bool canAccumulateAim = CanAccumulateAim();
		float nextProgress;

		if (canAccumulateAim)
		{
			nextProgress = Mathf.MoveTowards(currentProgress, EquippedWeaponTransientState.FullAimProgress01, Time.deltaTime / aimTimeSeconds);
			if (nextProgress >= EquippedWeaponTransientState.FullAimProgress01)
				nextProgress = EquippedWeaponTransientState.FullAimProgress01;
		}
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

		if (m_RequireVisibleTarget && (m_Vision == null || m_Vision.GetEngageableVisibleTarget() == null))
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
		WeaponRuntimeState weaponState = m_WeaponRuntime != null ? m_WeaponRuntime.RuntimeState : null;
		float weaponAimTimeSeconds = WeaponDistanceAimEvaluator.GetRequiredAimTimeSeconds(
			weaponDefinition,
			weaponState != null ? weaponState.EquippedAttachments : null,
			EstimateTargetDistanceMeters());
		float unitMultiplier = m_CombatStats != null ? m_CombatStats.GetAimTimeMultiplier() : 1f;
		float individualMultiplier = m_IndividualTraits != null ? m_IndividualTraits.GetAimTimeMultiplier() : 1f;
		float conditionMultiplier = m_CombatCondition != null
			? m_CombatCondition.GetAimTimeMultiplier(IsMoving())
			: 1f;
		float postureMultiplier = m_StanceCombatModifiers != null
			? m_StanceCombatModifiers.GetAimTimeMultiplier()
			: 1f;
		return Mathf.Max(0.01f, weaponAimTimeSeconds * unitMultiplier * individualMultiplier * conditionMultiplier * postureMultiplier);
	}

	private bool IsMoving()
	{
		if (m_FallenDragController != null && m_FallenDragController.IsDragging)
			return false;

		if (m_LocomotionDriver != null)
			return m_LocomotionDriver.HasMoveIntent;
		return m_ClickToMove != null && m_ClickToMove.HasMoveIntent;
	}

	private float EstimateTargetDistanceMeters()
	{
		Transform target = m_Vision != null ? m_Vision.GetEngageableVisibleTarget() : null;
		if (target == null)
			return 0f;

		Vector3 targetPoint = m_Vision.GetVisibleTargetAimPointWorld();
		if (targetPoint == Vector3.zero)
			targetPoint = target.position;

		return Vector3.Distance(transform.position, targetPoint);
	}

	private void HandleShotFired(AmmoDefinition _ammoDefinition)
	{
		if (!m_ResetAimProgressAfterShot || m_WeaponRuntime == null)
			return;

		if (m_FireController != null && m_FireController.IsCurrentEffectiveFireModeAutomatic())
			return;

		m_WeaponRuntime.SetAimProgress(0f);
	}

	private void HandleVisibleTargetChanged(Transform _newVisibleTarget)
	{
		TrySyncEngagementTarget();
	}

	private void TrySyncEngagementTarget()
	{
		Transform engageableTarget = m_Vision != null ? m_Vision.GetEngageableVisibleTarget() : null;

		Vector3 oldAimPoint = m_LastValidAimPointWorld;
		if (engageableTarget != null)
		{
			Vector3 currentAimPoint = m_Vision.GetVisibleTargetAimPointWorld();
			if (currentAimPoint != Vector3.zero)
				m_LastValidAimPointWorld = currentAimPoint;
		}

		if (engageableTarget == m_LastVisibleTarget)
			return;

		if (m_LastVisibleTarget != null && engageableTarget == null && m_Vision.VisibleTarget != null)
		{
			m_LastVisibleTarget = null;
			m_Vision.RequestImmediateScan();
			return;
		}

		Transform previousTarget = m_LastVisibleTarget;
		m_LastVisibleTarget = engageableTarget;

		if (m_Vision != null &&
			m_Vision.ShouldReacquireAimAfterSwitch(previousTarget, engageableTarget) &&
			m_WeaponRuntime != null)
		{
			float carryover = CalculateAimCarryover(oldAimPoint);
			float currentAim = m_WeaponRuntime.TransientState.AimProgress01;
			m_WeaponRuntime.SetAimProgress(currentAim * carryover);
		}
	}

	private float CalculateAimCarryover(Vector3 _oldAimPointWorld)
	{
		if (m_Vision == null || _oldAimPointWorld == Vector3.zero)
			return 0f;

		Vector3 newAimPoint = m_Vision.GetVisibleTargetAimPointWorld();
		if (newAimPoint == Vector3.zero)
			return 0f;

		Vector3 origin = transform.position;
		Vector3 oldDir = (_oldAimPointWorld - origin).normalized;
		Vector3 newDir = (newAimPoint - origin).normalized;

		float angle = Vector3.Angle(oldDir, newDir);
		float factor = 1f - Mathf.Clamp01(angle / m_AimCarryoverHalfAngleDegrees);
		return Mathf.Clamp01(factor * m_AimCarryoverMax);
	}
	#endregion
}
