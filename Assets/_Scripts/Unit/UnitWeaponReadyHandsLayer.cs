using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// Sub-state "high ready / low ready" while weapon is equipped.
/// Base Animator layer selects Ready/NoReady via bool parameter <c>WeaponReady</c>.
/// </summary>
[DefaultExecutionOrder(50)]
[DisallowMultipleComponent]
public sealed class UnitWeaponReadyHandsLayer : MonoBehaviour
{
	#region Private Fields
	[SerializeField] private Animator m_Animator;
	[SerializeField] private UnitEquipment m_Equipment;
	[SerializeField] private UnitClickToMove m_ClickToMove;
	[SerializeField] private UnitNavLocomotionDriver m_LocomotionDriver;
	[SerializeField] private UnitTeam m_Team;
	[SerializeField] private UnitMagazineLoadingController m_MagazineLoadingController;
	[SerializeField] private UnitWeaponReloadController m_WeaponReloadController;
	[SerializeField] private UnitVision m_Vision;
	[Tooltip("For layer‑0 idle CrossFade replay in crouch on high‑ready / low‑ready change (WeaponMode stays the same there).")]
	[SerializeField] private UnitAnimatorWeaponMode m_AnimatorWeaponMode;
	[Tooltip("IK левой/правой руки на объекте Animator.")]
	[SerializeField] private AnimatorHandIk m_LeftHandIk;
	[Tooltip("Weapon pose relaxed/ready when toggling high ready.")]
	[SerializeField] private UnitEquippedWeaponPose m_EquippedWeaponPose;
	[SerializeField] private UnitBusyState m_BusyState;

	[Header("Ввод")]
	[SerializeField] private bool m_EnableKeyboardInput = true;
	[SerializeField] private Key m_ToggleReadyKey = Key.E;

	private static readonly int s_Stance = Animator.StringToHash(UnitAnimatorWeaponMode.ParamStance);
	private static readonly int s_LocomotionTier = Animator.StringToHash(UnitClickToMove.ParamLocomotionTier);
	private static readonly int s_WeaponReady = Animator.StringToHash(UnitAnimatorWeaponMode.ParamWeaponReady);

	private bool m_UserWantsReady;
	private ItemDefinition m_LastEquipped;
	private bool m_BlockToggleInput;
	private bool m_RestoreReadyAfterSprint;
	private bool m_RestoreReadyAfterRun;
	private bool m_RestoreReadyAfterTurn;
	private bool m_ProximityBlocksReady;
	#endregion

	#region Public Methods
	/// <summary>
	/// Weapon is equipped, user is in low ready (regardless of crouch for the base graph).
	/// Used for FOV and the prone‑before‑ready hack.
	/// </summary>
	public bool IsEquippedWeaponUserNotReady()
	{
		if (m_Equipment == null)
			return false;

		ItemDefinition current = m_Equipment.EquippedDefinition;
		if (current == null || !current.IsEquipment || current.EquipmentKind != EquipmentKind.Weapon)
			return false;

		return !GetEffectiveIsReady();
	}

	/// <summary>
	/// Legacy compatibility hook: weapon presence always stays in <c>WeaponMode</c>; readiness goes through <c>WeaponReady</c>.
	/// </summary>
	public bool ShouldUseUnarmedLocomotionBranch()
	{
		return false;
	}

	/// <summary>
	/// Weapon is in hands and in high ready — for root rotation toward <see cref="UnitVision.VisibleTarget"/> etc.
	/// </summary>
	public bool IsWeaponEquippedAndReady()
	{
		return IsWeaponEquipped() && GetEffectiveIsReady();
	}

	/// <summary>
	/// Firing condition: in high ready and not sprinting (sprint is ordered separately from high ready and would otherwise allow full‑speed firing).
	/// </summary>
	public bool IsWeaponReadyToFire()
	{
		return IsWeaponEquippedAndReady() && !IsSprintingNow();
	}

	/// <summary>
	/// Current desired high-ready state before accounting for forced Ready in prone.
	/// Needed by AI / behaviour scripts to control the mode without emulating the E key.
	/// </summary>
	public bool WantsReady => m_UserWantsReady;

	/// <summary>Whether a deferred high‑ready restore is pending after sprint/run/turn.</summary>
	public bool HasPendingReadyRestore =>
		m_RestoreReadyAfterSprint || m_RestoreReadyAfterRun || m_RestoreReadyAfterTurn;

	/// <summary>
	/// Z key (stance change): when weapon is equipped, enables high ready (like pressing E to enter the high-ready state, without toggling).
	/// While sprinting, resets speed order to walk — same as enabling high ready via E.
	/// </summary>
	public void EnableReadyFromStanceZInput()
	{
		if (!IsWeaponEquipped())
			return;

		if (m_UserWantsReady)
			return;

		ApplyReadyWanted(true, true, true);
	}

	/// <summary>
	/// Direct control of the high‑ready state for AI / scripts.
	/// When enabling ready during sprint, optionally force speed down to walk.
	/// </summary>
	public void SetReadyWanted(bool _ready, bool _forceWalkIfNeeded = true)
	{
		if (!_ready)
			CancelDeferredReadyRestores();

		ApplyReadyWanted(_ready, _forceWalkIfNeeded, true);
	}

	/// <summary>Clears any deferred high‑ready restoration after sprint/run/turn.</summary>
	public void CancelDeferredReadyRestores()
	{
		m_RestoreReadyAfterSprint = false;
		m_RestoreReadyAfterRun = false;
		m_RestoreReadyAfterTurn = false;
	}

	public void SetProximityReadyBlock(bool _blocked)
	{
		if (m_ProximityBlocksReady == _blocked)
			return;
		m_ProximityBlocksReady = _blocked;
		PushWeaponReadyParameter();
		m_Vision?.NotifyWeaponReadyChanged(GetEffectiveIsReady());
	}

	/// <summary>
	/// Sprint temporarily disables high ready, but only if the weapon was actually in high ready before the sprint.
	/// </summary>
	public void SuppressReadyForSprintIfNeeded()
	{
		if (!IsWeaponEquipped())
		{
			m_RestoreReadyAfterSprint = false;
			return;
		}

		if (!m_UserWantsReady)
			return;

		ApplyTemporaryReadySuppression();
		m_RestoreReadyAfterSprint = true;
	}

	/// <summary>
	/// Run temporarily disables high ready (same pattern as sprint).
	/// </summary>
	public void SuppressReadyForRunIfNeeded()
	{
		if (!IsWeaponEquipped())
		{
			m_RestoreReadyAfterRun = false;
			return;
		}

		if (!m_UserWantsReady)
			return;

		ApplyTemporaryReadySuppression();
		m_RestoreReadyAfterRun = true;
	}

	/// <summary>
	/// Restores high ready after sprint, once locomotion considers the sprint finished.
	/// </summary>
	public void TryRestoreReadyAfterSprint(bool _isStillSprinting)
	{
		if (_isStillSprinting || !m_RestoreReadyAfterSprint)
			return;

		m_RestoreReadyAfterSprint = false;
		if (IsWeaponEquipped())
			ApplyReadyWanted(true, false, true);
	}

	/// <summary>
	/// Restores high ready after run.
	/// </summary>
	public void TryRestoreReadyAfterRun(bool _isStillRunning)
	{
		if (_isStillRunning || !m_RestoreReadyAfterRun)
			return;

		m_RestoreReadyAfterRun = false;
		if (IsWeaponEquipped())
			ApplyReadyWanted(true, false, true);
	}

	public void SuppressReadyForTurnIfNeeded()
	{
		if (!IsWeaponEquipped() || !m_UserWantsReady)
			return;

		ApplyTemporaryReadySuppression();
		m_RestoreReadyAfterTurn = true;
	}

	/// <summary>Temporarily disables high ready without resetting deferred restoration (turn in place etc.).</summary>
	public void ApplyTemporaryReadySuppression()
	{
		if (!IsWeaponEquipped() || !m_UserWantsReady)
			return;

		ApplyReadyWanted(false, false, true);
	}

	public void TryRestoreReadyAfterTurn(bool _isStillTurning)
	{
		if (_isStillTurning || !m_RestoreReadyAfterTurn)
			return;

		m_RestoreReadyAfterTurn = false;
		if (IsWeaponEquipped())
			ApplyReadyWanted(true, false, true);
	}

	/// <summary>Temporary block of the high-ready toggle key, e.g. during stance transition.</summary>
	public void SetToggleInputBlocked(bool _blocked)
	{
		m_BlockToggleInput = _blocked;
	}

	public void SetKeyboardInputEnabled(bool _enabled)
	{
		m_EnableKeyboardInput = _enabled;
	}
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();
		if (m_Equipment == null)
			m_Equipment = GetComponent<UnitEquipment>();
		if (m_ClickToMove == null)
			m_ClickToMove = GetComponent<UnitClickToMove>();
		if (m_LocomotionDriver == null)
			m_LocomotionDriver = GetComponent<UnitNavLocomotionDriver>();
		if (m_Team == null)
			m_Team = GetComponent<UnitTeam>();
		if (m_MagazineLoadingController == null)
			m_MagazineLoadingController = GetComponent<UnitMagazineLoadingController>();
		if (m_WeaponReloadController == null)
			m_WeaponReloadController = GetComponent<UnitWeaponReloadController>();
		if (m_Vision == null)
			m_Vision = GetComponent<UnitVision>();
		if (m_AnimatorWeaponMode == null)
			m_AnimatorWeaponMode = GetComponent<UnitAnimatorWeaponMode>();
		if (m_BusyState == null)
			m_BusyState = GetComponent<UnitBusyState>();

		if (m_Animator != null && m_LeftHandIk == null)
			m_LeftHandIk = m_Animator.GetComponent<AnimatorHandIk>();
		if (m_Animator != null && m_LeftHandIk == null)
			m_LeftHandIk = m_Animator.gameObject.AddComponent<AnimatorHandIk>();

		if (m_EquippedWeaponPose == null)
			m_EquippedWeaponPose = GetComponent<UnitEquippedWeaponPose>();
		if (m_EquippedWeaponPose == null)
			m_EquippedWeaponPose = GetComponentInParent<UnitEquippedWeaponPose>();
		if (m_EquippedWeaponPose == null)
			m_EquippedWeaponPose = gameObject.AddComponent<UnitEquippedWeaponPose>();

		if (GetComponent<UnitProximityReadyController>() == null)
			gameObject.AddComponent<UnitProximityReadyController>();

		if (GetComponent<UnitEquippedWeaponPoseRuntimeTuner>() == null)
			gameObject.AddComponent<UnitEquippedWeaponPoseRuntimeTuner>();
	}

	private void OnEnable()
	{
		m_UserWantsReady = false;
		m_LastEquipped = null;
		m_RestoreReadyAfterSprint = false;
		m_RestoreReadyAfterRun = false;
		PushWeaponReadyParameter();
	}

	private void Update()
	{
		ItemDefinition current = m_Equipment != null ? m_Equipment.EquippedDefinition : null;
		if (!ReferenceEquals(current, m_LastEquipped))
		{
			m_LastEquipped = current;
			m_UserWantsReady = false;
			m_RestoreReadyAfterSprint = false;
			m_RestoreReadyAfterRun = false;
			PushWeaponReadyParameter();
		}

		if (!CanUseDirectKeyboardInput() || !IsWeaponEquipped())
			return;

		if (m_BlockToggleInput)
			return;

		if (!WasKeyPressedThisFrame(m_ToggleReadyKey))
			return;

		bool nextReady = !m_UserWantsReady;

		if (nextReady && m_BusyState != null &&
		    m_BusyState.HasReason(UnitBusyState.BusyReason.CarryingFallen))
			return;

		if (!nextReady && m_Animator != null && m_Animator.GetInteger(s_Stance) == (int)LocomotionStance.Prone)
			return;

		if (!nextReady)
			CancelDeferredReadyRestores();

		ApplyReadyWanted(nextReady, nextReady, true);
	}
	#endregion

	#region Private Methods
	public bool IsWeaponEquipped()
	{
		ItemDefinition current = m_Equipment != null ? m_Equipment.EquippedDefinition : null;
		return current != null && current.IsEquipment && current.EquipmentKind == EquipmentKind.Weapon;
	}

	private bool CanUseDirectKeyboardInput()
	{
		if (!m_EnableKeyboardInput)
			return false;
		if (m_Team == null)
			return true;

		return m_Team.Team == UnitTeamId.Player;
	}

	private static bool WasKeyPressedThisFrame(Key _key)
	{
		for (int i = 0; i < InputSystem.devices.Count; i++)
		{
			if (InputSystem.devices[i] is not Keyboard kb)
				continue;

			KeyControl key = kb[_key];
			if (key != null && key.wasPressedThisFrame)
				return true;
		}

		return false;
	}

	private bool GetEffectiveIsReady()
	{
		if (m_Animator != null && m_Animator.GetInteger(s_Stance) == (int)LocomotionStance.Prone)
			return true;

		if (m_ProximityBlocksReady)
			return false;

		return m_UserWantsReady;
	}

	private bool IsSprintingNow()
	{
		// ПКМ/Shift идёт через ClickToMove; NavLocomotionDriver на том же юните может оставаться в Walk.
		if (m_ClickToMove != null && m_ClickToMove.IsSprintMoveMode)
			return true;
		if (m_LocomotionDriver != null && m_LocomotionDriver.IsSprintMoveMode)
			return true;

		// Фоллбек по аниматору только без драйверов локомоции — иначе tier=2 может остаться после остановки.
		if (m_ClickToMove == null && m_LocomotionDriver == null && m_Animator != null)
			return m_Animator.GetInteger(s_LocomotionTier) == 2;

		return false;
	}

	private bool IsRunningNow()
	{
		if (m_ClickToMove != null && m_ClickToMove.IsRunMoveMode)
			return true;
		if (m_LocomotionDriver != null && m_LocomotionDriver.IsRunMoveMode)
			return true;

		return false;
	}

	private bool IsFastMoveModeNow()
	{
		return IsSprintingNow() || IsRunningNow();
	}

	private void ApplyReadyWanted(bool _ready, bool _forceWalkIfNeeded, bool _refreshImmediately)
	{
		if (!IsWeaponEquipped())
		{
			m_UserWantsReady = false;
			m_RestoreReadyAfterSprint = false;
			m_RestoreReadyAfterRun = false;
			PushWeaponReadyParameter();
			return;
		}

		bool didChange = m_UserWantsReady != _ready;
		m_UserWantsReady = _ready;
		PushWeaponReadyParameter();

		if (didChange && m_WeaponReloadController != null && m_WeaponReloadController.IsReloadBusy)
			m_WeaponReloadController.SyncAimReloadClipForWeaponReadyChange();

		if (_ready && _forceWalkIfNeeded && IsFastMoveModeNow())
			ForceWalkMoveModeOnAllLocomotionDrivers();

		if (didChange)
		{
			m_EquippedWeaponPose?.OnWeaponReadyStateChanged();
			m_LeftHandIk?.OnWeaponReadyStateChanged();
		}

		if (didChange)
			m_Vision?.NotifyWeaponReadyChanged(_ready);

		if (didChange && _refreshImmediately)
			ApplyVisualRefreshAfterReadyToggle();
	}

	/// <summary>Сброс без мгновенного веса: верхний слой плавно тянется через <see cref="m_UpperLayerWeightSmoothSeconds"/>.</summary>
	private void ApplyVisualRefreshAfterReadyToggle()
	{
		if (ShouldReplayLocomotionCrossfadeAfterReadyChange())
			m_AnimatorWeaponMode.ReplayLocomotionIdleCrossfade();
	}

	private bool ShouldReplayLocomotionCrossfadeAfterReadyChange()
	{
		if (m_Animator == null || m_AnimatorWeaponMode == null)
			return false;
		if (m_MagazineLoadingController != null && m_MagazineLoadingController.IsLoadingMagazine)
			return false;
		if (m_WeaponReloadController != null && m_WeaponReloadController.IsReloadBusy)
			return false;

		// В стоя переход Aim↔Relaxed уже на bool WeaponReady; CrossFade сбрасывает normalizedTime и дёргает позу.
		int stance = m_Animator.GetInteger(s_Stance);
		return stance == (int)LocomotionStance.Crouch || stance == (int)LocomotionStance.Prone;
	}

	private void ForceWalkMoveModeOnAllLocomotionDrivers()
	{
		if (m_ClickToMove != null)
			m_ClickToMove.ForceWalkMoveMode();
		if (m_LocomotionDriver != null)
			m_LocomotionDriver.ForceWalkMoveMode();
	}

	private void PushWeaponReadyParameter()
	{
		if (m_Animator == null)
			return;

		m_Animator.SetBool(s_WeaponReady, GetEffectiveIsReady() && IsWeaponEquipped());
	}
	#endregion
}

