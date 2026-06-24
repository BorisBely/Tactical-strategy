using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// Подсостояние «на готове / не на готове» при экипированном оружии.
/// Базовый слой Animator выбирает Ready/NoReady через bool-параметр <c>WeaponReady</c>.
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
	[Tooltip("Для повторного CrossFade базового idle в приседе при смене готов/не готов (там WeaponMode не переключается).")]
	[SerializeField] private UnitAnimatorWeaponMode m_AnimatorWeaponMode;
	[Tooltip("IK левой руки на объекте Animator; при переходе в «готов» проверяется, что зарядка магазина не блокирует IK.")]
	[SerializeField] private AnimatorHandIk m_LeftHandIk;
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
	#endregion

	#region Public Methods
	/// <summary>
	/// Оружие экипировано, пользователь в режиме «не готов» (без учёта приседа для базового графа).
	/// Для FOV и костыля prone до ready.
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
	/// Устаревший compatibility hook: наличие оружия всегда остаётся в <c>WeaponMode</c>, готовность идёт через <c>WeaponReady</c>.
	/// </summary>
	public bool ShouldUseUnarmedLocomotionBranch()
	{
		return false;
	}

	/// <summary>
	/// В руках оружие и включён «на готове» — для разворота корня на <see cref="UnitVision.VisibleTarget"/> и т.п.
	/// </summary>
	public bool IsWeaponEquippedAndReady()
	{
		return IsWeaponEquipped() && m_UserWantsReady;
	}

	/// <summary>
	/// Условие для стрельбы: на готове и не в режиме спринта (спринт заказан отдельно от «готов» и иначе даёт стрельбу на полной скорости).
	/// </summary>
	public bool IsWeaponReadyToFire()
	{
		return IsWeaponEquippedAndReady() && !IsSprintingNow();
	}

	/// <summary>
	/// Текущее желаемое состояние "готов" до учёта принудительного Ready в prone.
	/// Нужен ИИ/скриптам поведения, чтобы управлять режимом без эмуляции клавиши E.
	/// </summary>
	public bool WantsReady => m_UserWantsReady;

	/// <summary>
	/// Нажатие Z (смена стойки): при экипированном оружии включает «на готове» (как перевод E в состояние готов, без переключения).
	/// При спринте сбрасывает заказ скорости на шаг — как при включении готов по E.
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
	/// Прямое управление состоянием "готов" для ИИ/скриптов.
	/// Если включаем ready во время спринта, можно принудительно сбросить скорость до шага.
	/// </summary>
	public void SetReadyWanted(bool _ready, bool _forceWalkIfNeeded = true)
	{
		ApplyReadyWanted(_ready, _forceWalkIfNeeded, true);
	}

	/// <summary>
	/// Спринт временно снимает «готов», но только если до спринта оружие действительно было на готове.
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

		m_RestoreReadyAfterSprint = true;
		ApplyReadyWanted(false, false, true);
	}

	/// <summary>
	/// Бег временно снимает «готов» (аналогично спринту).
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

		m_RestoreReadyAfterRun = true;
		ApplyReadyWanted(false, false, true);
	}

	/// <summary>
	/// Возвращает «готов» после спринта, когда локомоция уже считает, что спринт завершён.
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
	/// Возвращает «готов» после бега.
	/// </summary>
	public void TryRestoreReadyAfterRun(bool _isStillRunning)
	{
		if (_isStillRunning || !m_RestoreReadyAfterRun)
			return;

		m_RestoreReadyAfterRun = false;
		if (IsWeaponEquipped())
			ApplyReadyWanted(true, false, true);
	}

	/// <summary>Временная блокировка клавиши готов, например при переходе стойки.</summary>
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
		if (m_AnimatorWeaponMode == null)
			m_AnimatorWeaponMode = GetComponent<UnitAnimatorWeaponMode>();
		if (m_BusyState == null)
			m_BusyState = GetComponent<UnitBusyState>();

		if (m_Animator != null && m_LeftHandIk == null)
			m_LeftHandIk = m_Animator.GetComponent<AnimatorHandIk>();
		if (m_Animator != null && m_LeftHandIk == null)
			m_LeftHandIk = m_Animator.gameObject.AddComponent<AnimatorHandIk>();
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

		bool isSprinting = IsSprintingNow();
		bool nextReady = !m_UserWantsReady;

		if (nextReady && m_BusyState != null &&
		    m_BusyState.HasReason(UnitBusyState.BusyReason.CarryingFallen))
			return;

		if (!nextReady && m_Animator != null && m_Animator.GetInteger(s_Stance) == (int)LocomotionStance.Prone)
			return;

		ApplyReadyWanted(nextReady, isSprinting, true);
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

		if (_ready && _forceWalkIfNeeded && IsSprintingNow())
			ForceWalkMoveModeOnAllLocomotionDrivers();

		if (_ready && didChange)
			m_LeftHandIk?.OnWeaponReadyStateApplied();

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

		return true;
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

