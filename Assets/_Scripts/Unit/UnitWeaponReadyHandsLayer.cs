using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// Подсостояние «на готове / не на готове» при экипированном оружии.
/// Не на готове, стоя: <see cref="UnitAnimatorWeaponMode"/> даёт безоружную ветку локомоции; слой <c>UpperBody_NoAim</c> (вес 1)
/// накладывает позу рук «оружие не на готове» (<c>Upper_Rifle_NoAim</c> / <c>Upper_Pistol_NoAim</c>).
/// Не на готове, присед: локомоция по типу оружия (винтовка/пистолет), тот же слой — состояния
/// <c>Upper_Rifle_Crouch_NoAim</c> / <c>Upper_Pistol_Crouch_NoAim</c>.
/// На готове: ветка оружия, вес слоя 0.
/// В лёже «не готов» для графа не используется (принудительно готов для переходов).
/// </summary>
[DefaultExecutionOrder(50)]
[DisallowMultipleComponent]
public sealed class UnitWeaponReadyHandsLayer : MonoBehaviour
{
	#region Constants
	private const string c_LayerName = "UpperBody_NoAim";
	private const string c_StateRifleNoAim = "Upper_Rifle_NoAim";
	private const string c_StatePistolNoAim = "Upper_Pistol_NoAim";
	private const string c_StateRifleCrouchNoAim = "Upper_Rifle_Crouch_NoAim";
	private const string c_StatePistolCrouchNoAim = "Upper_Pistol_Crouch_NoAim";
	#endregion

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

	[Header("Ввод")]
	[SerializeField] private bool m_EnableKeyboardInput = true;
	[SerializeField] private Key m_ToggleReadyKey = Key.E;

	[Header("Слой рук (no aim)")]
	[SerializeField, Min(0f)] private float m_LayerBlendSeconds = 0.12f;
	[Tooltip("За сколько секунд плавно меняется вес слоя UpperBody_NoAim (0↔1) при смене готов/не готов.")]
	[SerializeField, Min(0.02f)] private float m_UpperLayerWeightSmoothSeconds = 0.2f;

	private static readonly int s_Stance = Animator.StringToHash(UnitAnimatorWeaponMode.ParamStance);
	private static readonly int s_LocomotionTier = Animator.StringToHash(UnitClickToMove.ParamLocomotionTier);

	private int m_LayerIndex = -1;
	private bool m_UserWantsReady;
	private ItemDefinition m_LastEquipped;
	private bool m_WasNoAimLayerActive;
	private int m_LastNoAimPoseSignature = -1;
	private float m_SmoothedLayerWeight;
	private bool m_SnapLayerWeightNextFrame;
	private bool m_BlockToggleInput;
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
	/// Нужно ли играть безоружную локомоцию при том, что в руках оружие (не на готове).
	/// В присяде — нет: базовый граф винтовки/пистолета, руки «не готов» даёт слой <c>UpperBody_NoAim</c>.
	/// </summary>
	public bool ShouldUseUnarmedLocomotionBranch()
	{
		if (!IsEquippedWeaponUserNotReady())
			return false;

		if (m_Animator == null)
			return true;

		return m_Animator.GetInteger(s_Stance) != (int)LocomotionStance.Crouch;
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

		if (m_Animator != null)
		{
			m_LayerIndex = m_Animator.GetLayerIndex(c_LayerName);
			if (m_LeftHandIk == null)
				m_LeftHandIk = m_Animator.GetComponent<AnimatorHandIk>();
		}
	}

	private void OnEnable()
	{
		m_UserWantsReady = false;
		m_LastEquipped = null;
		m_WasNoAimLayerActive = false;
		m_SmoothedLayerWeight = 0f;
		m_SnapLayerWeightNextFrame = true;
		if (m_Animator != null && m_LayerIndex >= 0)
			m_Animator.SetLayerWeight(m_LayerIndex, 0f);
	}

	private void Update()
	{
		ItemDefinition current = m_Equipment != null ? m_Equipment.EquippedDefinition : null;
		if (!ReferenceEquals(current, m_LastEquipped))
		{
			m_LastEquipped = current;
			m_UserWantsReady = false;
			SnapUpperBodyLayerAndInvalidatePose();
		}

		if (!CanUseDirectKeyboardInput() || !IsWeaponEquipped())
			return;

		if (m_BlockToggleInput)
			return;

		if (!WasKeyPressedThisFrame(m_ToggleReadyKey))
			return;

		bool isSprinting = IsSprintingNow();
		bool nextReady = !m_UserWantsReady;

		if (!nextReady && m_Animator != null && m_Animator.GetInteger(s_Stance) == (int)LocomotionStance.Prone)
			return;

		ApplyReadyWanted(nextReady, isSprinting, true);
	}

	private void LateUpdate()
	{
		ApplyUpperBodyNoAimLayer();
	}
	#endregion

	#region Private Methods
	private bool IsWeaponEquipped()
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

	private void ApplyUpperBodyNoAimLayer()
	{
		if (m_Animator == null || m_LayerIndex < 0)
			return;

		bool isMagazineLoading = m_MagazineLoadingController != null && m_MagazineLoadingController.IsLoadingMagazine;
		bool isWeaponReloading = m_WeaponReloadController != null && m_WeaponReloadController.IsReloadBusy;

		bool shouldShow = isMagazineLoading || isWeaponReloading || ShouldShowUpperBodyNoAimOverlay();
		float targetWeight = shouldShow ? 1f : 0f;

		if (m_SnapLayerWeightNextFrame)
		{
			m_SmoothedLayerWeight = targetWeight;
			m_SnapLayerWeightNextFrame = false;
		}
		else
		{
			float maxDelta = Time.deltaTime / Mathf.Max(0.0001f, m_UpperLayerWeightSmoothSeconds);
			m_SmoothedLayerWeight = Mathf.MoveTowards(m_SmoothedLayerWeight, targetWeight, maxDelta);
		}

		m_Animator.SetLayerWeight(m_LayerIndex, m_SmoothedLayerWeight);

		bool effectivelyShowingNoAim = m_SmoothedLayerWeight > 0.02f;
		if (!effectivelyShowingNoAim)
		{
			m_WasNoAimLayerActive = false;
			return;
		}

		if (isMagazineLoading || isWeaponReloading)
		{
			m_WasNoAimLayerActive = true;
			return;
		}

		ItemDefinition weapon = m_Equipment != null ? m_Equipment.EquippedDefinition : null;
		WeaponType wt = weapon != null ? weapon.WeaponType : WeaponType.Primary;

		int stance = m_Animator.GetInteger(s_Stance);
		bool isCrouch = stance == (int)LocomotionStance.Crouch;
		int poseSignature = ComputeNoAimPoseSignature(wt, isCrouch);
		if (!m_WasNoAimLayerActive || poseSignature != m_LastNoAimPoseSignature)
		{
			string stateName = ResolveUpperBodyNoAimStateName(wt, isCrouch);
			m_Animator.CrossFadeInFixedTime(stateName, m_LayerBlendSeconds, m_LayerIndex);
			m_LastNoAimPoseSignature = poseSignature;
		}

		m_WasNoAimLayerActive = true;
	}

	private static int ComputeNoAimPoseSignature(WeaponType _weaponType, bool _crouch)
	{
		int w = _weaponType == WeaponType.Secondary ? 1 : 0;
		return w + (_crouch ? 10 : 0);
	}

	private static string ResolveUpperBodyNoAimStateName(WeaponType _weaponType, bool _crouch)
	{
		bool pistol = _weaponType == WeaponType.Secondary;
		if (_crouch)
			return pistol ? c_StatePistolCrouchNoAim : c_StateRifleCrouchNoAim;
		return pistol ? c_StatePistolNoAim : c_StateRifleNoAim;
	}

	/// <summary>
	/// Слой «руки не на готове»: стоя при не готов + оружие, в присяде при не готов + оружие (любой tier), не в лёже.
	/// </summary>
	private bool ShouldShowUpperBodyNoAimOverlay()
	{
		if (!IsEquippedWeaponUserNotReady() || m_Animator == null)
			return false;

		int stance = m_Animator.GetInteger(s_Stance);
		return stance != (int)LocomotionStance.Prone;
	}

	private bool GetEffectiveIsReady()
	{
		if (m_Animator != null && m_Animator.GetInteger(s_Stance) == (int)LocomotionStance.Prone)
			return true;

		return m_UserWantsReady;
	}

	private bool IsSprintingNow()
	{
		if (m_LocomotionDriver != null)
			return m_LocomotionDriver.IsSprintMoveMode;

		if (m_ClickToMove != null)
			return m_ClickToMove.IsSprintMoveMode;

		// Фоллбек: по параметру аниматора (0 walk, 1 run, 2 sprint).
		if (m_Animator != null)
			return m_Animator.GetInteger(s_LocomotionTier) == 2;

		return false;
	}

	private void ApplyReadyWanted(bool _ready, bool _forceWalkIfNeeded, bool _refreshImmediately)
	{
		if (!IsWeaponEquipped())
		{
			m_UserWantsReady = false;
			if (_refreshImmediately)
				SnapUpperBodyLayerAndInvalidatePose();
			return;
		}

		bool didChange = m_UserWantsReady != _ready;
		m_UserWantsReady = _ready;

		if (_ready && _forceWalkIfNeeded && IsSprintingNow())
		{
			if (m_LocomotionDriver != null)
				m_LocomotionDriver.ForceWalkMoveMode();
			else if (m_ClickToMove != null)
				m_ClickToMove.ForceWalkMoveMode();
		}

		if (_ready && didChange)
			m_LeftHandIk?.OnWeaponReadyStateApplied();

		if (didChange && _refreshImmediately)
			ApplyVisualRefreshAfterReadyToggle();
	}

	/// <summary>Сброс без мгновенного веса: верхний слой плавно тянется через <see cref="m_UpperLayerWeightSmoothSeconds"/>.</summary>
	private void ApplyVisualRefreshAfterReadyToggle()
	{
		m_WasNoAimLayerActive = false;
		m_LastNoAimPoseSignature = -1;

		if (ShouldReplayCrouchLocomotionCrossfadeAfterReadyChange())
			m_AnimatorWeaponMode.ReplayLocomotionIdleCrossfade();
	}

	private bool ShouldReplayCrouchLocomotionCrossfadeAfterReadyChange()
	{
		if (m_Animator == null || m_AnimatorWeaponMode == null)
			return false;
		if (m_Animator.GetInteger(s_Stance) != (int)LocomotionStance.Crouch)
			return false;
		if (m_MagazineLoadingController != null && m_MagazineLoadingController.IsLoadingMagazine)
			return false;
		if (m_WeaponReloadController != null && m_WeaponReloadController.IsReloadBusy)
			return false;

		return true;
	}

	/// <summary>Сразу выставить вес слоя и сбросить позу (смена оружия, снятие оружия).</summary>
	private void SnapUpperBodyLayerAndInvalidatePose()
	{
		m_SnapLayerWeightNextFrame = true;
		m_WasNoAimLayerActive = false;
		m_LastNoAimPoseSignature = -1;
	}
	#endregion
}

