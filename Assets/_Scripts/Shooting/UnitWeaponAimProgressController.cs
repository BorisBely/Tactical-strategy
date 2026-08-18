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
	[Tooltip("Selected/engageable combat target (TargetSelector).")]
	[SerializeField] private TargetSelector m_TargetSelector;
	[Tooltip("Detection scan defer after clearing non-engageable selection.")]
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
		if (m_TargetSelector == null)
			m_TargetSelector = GetComponent<TargetSelector>();
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
		if (m_TargetSelector != null)
			m_TargetSelector.SelectedTargetChanged += HandleSelectedTargetChanged;
		if (m_FireController != null)
			m_FireController.ShotFired += HandleShotFired;

		m_LastVisibleTarget = m_TargetSelector != null ? m_TargetSelector.GetEngageableSelectedTarget() : null;
		m_LastValidAimPointWorld = m_LastVisibleTarget != null && m_TargetSelector != null
			? m_TargetSelector.GetEngageableAimPointWorld()
			: Vector3.zero;
	}

	private void OnDisable()
	{
		if (m_TargetSelector != null)
			m_TargetSelector.SelectedTargetChanged -= HandleSelectedTargetChanged;
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
		m_DebugCurrentTarget = m_TargetSelector != null ? m_TargetSelector.SelectedTarget : null;
	}
	#endregion

	#region Private Methods
	private bool CanAccumulateAim()
	{
		if (m_WeaponRuntime == null || m_WeaponRuntime.CurrentWeaponDefinition == null)
			return false;

		if (m_RequireReady)
		{
			if (m_ReadyHands == null)
				return false;
			if (!m_ReadyHands.EffectivePoseState.CanAccumulateAimFromPose() &&
			    !m_ReadyHands.IsWeaponEquippedAndReady())
				return false;
		}

		if (m_RequireVisibleTarget)
		{
			if (m_TargetSelector == null)
				return false;
			if (m_TargetSelector.GetEngageableSelectedTarget() == null)
				return false;
		}

		if (m_BlockDuringStanceTransition &&
		    m_BusyState != null &&
		    m_BusyState.IsBusy &&
		    (m_BusyState.Reasons & UnitBusyState.BusyReason.StanceTransition) != 0)
			return false;

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
		float distance = EstimateTargetDistanceMeters();

		WeaponPoseState pose = m_ReadyHands != null
			? m_ReadyHands.EffectivePoseState
			: WeaponPoseState.Aiming;
		bool includeOptics = pose == WeaponPoseState.Aiming;
		WeaponAttachmentDefinition[] attachments = weaponState != null ? weaponState.EquippedAttachments : null;
		WeaponAttachmentDefinition[] filtered = WeaponPoseAutoCapabilityBaker.FilterAttachments(attachments, includeOptics);

		float weaponAimTimeSeconds = WeaponDistanceAimEvaluator.GetRequiredAimTimeSeconds(
			weaponDefinition,
			filtered,
			distance);
		float poseDistanceAimMult = WeaponPoseDistanceCurves.GetAimTimeMultiplier(pose, distance);

		// Prefer baked pose aim mult (includes unit+flat attach+pose factor) — avoid double-counting skills.
		if (m_ReadyHands != null && m_ReadyHands.PoseCapabilityCache.IsValid)
		{
			float bakedPoseAim = m_ReadyHands.PoseCapabilityCache.GetAimTimeMult(pose);
			float weaponDistOnly = weaponDefinition != null
				? Mathf.Max(0.01f, weaponDefinition.GetDistanceAimTimeMultiplier(distance))
				: 1f;
			float baseAim = weaponDefinition != null ? weaponDefinition.AimTimeSeconds : 0.28f;
			float postureMultiplier = m_StanceCombatModifiers != null
				? m_StanceCombatModifiers.GetAimTimeMultiplier()
				: 1f;
			float seconds = baseAim * weaponDistOnly * bakedPoseAim * poseDistanceAimMult * postureMultiplier;
			return Mathf.Max(0.01f, ApplyLaserAimTime(seconds, pose, attachments, distance, _cacheIncludesAimingLaser: true));
		}

		float unitMultiplier = m_CombatStats != null ? m_CombatStats.GetAimTimeMultiplier() : 1f;
		float individualMultiplier = m_IndividualTraits != null ? m_IndividualTraits.GetAimTimeMultiplier() : 1f;
		float conditionMultiplier = m_CombatCondition != null
			? m_CombatCondition.GetAimTimeMultiplier(IsMoving())
			: 1f;
		float postureMult = m_StanceCombatModifiers != null
			? m_StanceCombatModifiers.GetAimTimeMultiplier()
			: 1f;
		float poseScale = pose.IsHipFireHold() ? 0.55f
			: pose == WeaponPoseState.PointAim ? 0.85f
			: pose == WeaponPoseState.PreAim ? PreAimPoseUtility.AimTimeMult
			: 1f;
		float fallbackSeconds = weaponAimTimeSeconds * unitMultiplier * individualMultiplier * conditionMultiplier * postureMult * poseScale * poseDistanceAimMult;
		return Mathf.Max(0.01f, ApplyLaserAimTime(fallbackSeconds, pose, attachments, distance, _cacheIncludesAimingLaser: false));
	}

	private static float ApplyLaserAimTime(
		float _seconds,
		WeaponPoseState _pose,
		WeaponAttachmentDefinition[] _attachments,
		float _distanceMeters,
		bool _cacheIncludesAimingLaser)
	{
		if (_pose == WeaponPoseState.PointAim)
			return _seconds * WeaponLaserModifiers.GetPointAimAimTimeProduct(_attachments, _distanceMeters);
		if (_pose == WeaponPoseState.Aiming && !_cacheIncludesAimingLaser)
			return _seconds * WeaponLaserModifiers.GetAimingAimTimeProduct(_attachments);
		return _seconds;
	}

	private bool IsMoving()
	{
		if (m_FallenDragController != null && m_FallenDragController.IsDragging)
			return false;

		if (m_LocomotionDriver != null && m_LocomotionDriver.enabled)
			return m_LocomotionDriver.HasMoveIntent;
		return m_ClickToMove != null && m_ClickToMove.enabled && m_ClickToMove.HasMoveIntent;
	}

	private float EstimateTargetDistanceMeters()
	{
		Transform target = m_TargetSelector != null ? m_TargetSelector.GetEngageableSelectedTarget() : null;
		if (target == null)
			return 0f;

		Vector3 targetPoint = m_TargetSelector.GetEngageableAimPointWorld();
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

	private void HandleSelectedTargetChanged(Transform _newSelectedTarget)
	{
		TrySyncEngagementTarget();
	}

	private void TrySyncEngagementTarget()
	{
		Transform engageableTarget = m_TargetSelector != null ? m_TargetSelector.GetEngageableSelectedTarget() : null;

		Vector3 oldAimPoint = m_LastValidAimPointWorld;
		if (engageableTarget != null)
		{
			Vector3 currentAimPoint = m_TargetSelector.GetEngageableAimPointWorld();
			if (currentAimPoint != Vector3.zero)
				m_LastValidAimPointWorld = currentAimPoint;
		}

		if (engageableTarget == m_LastVisibleTarget)
			return;

		// Selected but not engageable: clear selection and wait for next planned scan.
		if (m_LastVisibleTarget != null && engageableTarget == null &&
		    m_TargetSelector != null && m_TargetSelector.SelectedTarget != null)
		{
			m_LastVisibleTarget = null;
			m_TargetSelector.ClearSelectionAndNotifyIfHadTarget();
			m_Vision?.DeferNextScan();
			return;
		}

		Transform previousTarget = m_LastVisibleTarget;
		m_LastVisibleTarget = engageableTarget;

		if (m_TargetSelector != null &&
			m_TargetSelector.ShouldReacquireAimAfterSwitch(previousTarget, engageableTarget) &&
			m_WeaponRuntime != null)
		{
			bool isFullAuto = m_FireController != null && m_FireController.IsCurrentEffectiveFireModeAutomatic()
				&& m_FireController.ResolveEffectiveFireMode() == WeaponFireMode.FullAuto;
			if (isFullAuto)
			{
				m_WeaponRuntime.SetAimProgress(0f);
			}
			else
			{
				float carryover = CalculateAimCarryover(oldAimPoint);
				float currentAim = m_WeaponRuntime.TransientState.AimProgress01;
				m_WeaponRuntime.SetAimProgress(currentAim * carryover);
			}
		}
	}

	private float CalculateAimCarryover(Vector3 _oldAimPointWorld)
	{
		if (m_TargetSelector == null || _oldAimPointWorld == Vector3.zero)
			return 0f;

		Vector3 newAimPoint = m_TargetSelector.GetEngageableAimPointWorld();
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
