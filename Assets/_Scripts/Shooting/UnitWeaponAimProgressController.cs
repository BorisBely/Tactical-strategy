using UnityEngine;

/// <summary>
/// Обновляет AimProgress оружия по времени прицеливания, стойке, движению и смене цели.
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

	[Header("Aim Conditions")]
	[Tooltip("Если true, AimProgress растёт только когда оружие в ready.")]
	[SerializeField] private bool m_RequireReady = true;
	[Tooltip("Если true, AimProgress растёт только при наличии видимой цели.")]
	[SerializeField] private bool m_RequireVisibleTarget = true;
	[Tooltip("Если стойка сейчас в переходе, прицеливание не растёт и плавно теряется.")]
	[SerializeField] private bool m_BlockDuringStanceTransition = true;

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

	[Header("Debug")]
	[SerializeField, Min(0.01f)] private float m_DebugCurrentAimTimeSeconds = 0.25f;
	[SerializeField] private bool m_DebugCanAccumulateAim;
	[SerializeField] private Transform m_DebugCurrentTarget;
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
	}

	private void OnEnable()
	{
		if (m_Vision != null)
			m_Vision.VisibleTargetChanged += HandleVisibleTargetChanged;

		m_LastVisibleTarget = m_Vision != null ? m_Vision.VisibleTarget : null;
	}

	private void OnDisable()
	{
		if (m_Vision != null)
			m_Vision.VisibleTargetChanged -= HandleVisibleTargetChanged;
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

		return true;
	}

	private float CalculateCurrentAimTimeSeconds()
	{
		WeaponDefinition weaponDefinition = m_WeaponRuntime != null ? m_WeaponRuntime.CurrentWeaponDefinition : null;
		float aimTimeSeconds = weaponDefinition != null ? weaponDefinition.AimTimeSeconds : 0.25f;
		aimTimeSeconds *= GetStanceAimTimeMultiplier();
		aimTimeSeconds *= GetMovementAimTimeMultiplier();
		return Mathf.Max(0.01f, aimTimeSeconds);
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
		bool isMoving = false;
		bool isSprinting = false;

		if (m_LocomotionDriver != null)
		{
			isMoving = m_LocomotionDriver.HasMoveIntent;
			isSprinting = m_LocomotionDriver.IsSprintMoveMode;
		}
		else if (m_ClickToMove != null)
		{
			isMoving = m_ClickToMove.HasMoveIntent;
			isSprinting = m_ClickToMove.IsSprintMoveMode;
		}

		if (isSprinting)
			return m_SprintAimTimeMultiplier;
		if (isMoving)
			return m_MovingAimTimeMultiplier;

		return 1f;
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
