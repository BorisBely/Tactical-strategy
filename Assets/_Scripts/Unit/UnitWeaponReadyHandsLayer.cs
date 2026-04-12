using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// Подсостояние «на готове / не на готове» при экипированном оружии.
/// Не на готове: <see cref="UnitAnimatorWeaponMode"/> переключает граф на безоружную ветку (при модели с оружием в руках).
/// В этом режиме слой <c>UpperBody_NoAim</c> (вес 1) накладывает позу рук «оружие не на готове» поверх безоружной локомоции.
/// На готове: ветка локомоции по типу оружия, вес слоя 0.
/// Переключение «не на готове» возможно только стоя (любой LocomotionTier) и в присяде при шаге (tier Walk).
/// В лёже и в присяде при беге/спринте — всегда ветка оружия.
/// </summary>
[DefaultExecutionOrder(50)]
[DisallowMultipleComponent]
public sealed class UnitWeaponReadyHandsLayer : MonoBehaviour
{
	#region Constants
	private const string c_LayerName = "UpperBody_NoAim";
	private const string c_StateRifleNoAim = "Upper_Rifle_NoAim";
	private const string c_StatePistolNoAim = "Upper_Pistol_NoAim";
	#endregion

	#region Private Fields
	[SerializeField] private Animator m_Animator;
	[SerializeField] private UnitEquipment m_Equipment;
	[SerializeField] private UnitClickToMove m_ClickToMove;
	[SerializeField] private UnitNavLocomotionDriver m_LocomotionDriver;
	[SerializeField] private UnitTeam m_Team;
	[SerializeField] private UnitMagazineLoadingController m_MagazineLoadingController;

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
	private WeaponType m_LastNoAimWeaponTypePlayed;
	private float m_SmoothedLayerWeight;
	private bool m_SnapLayerWeightNextFrame;
	private bool m_BlockToggleInput;
	#endregion

	#region Public Methods
	/// <summary>
	/// Нужно ли играть безоружную локомоцию при том, что в руках оружие (не на готове).
	/// Важно: это НЕ связано с правилами слоя рук — локомоция без оружия должна переключаться и при лёжа/переходах,
	/// чтобы стойки работали так же, как в полностью безоружном состоянии.
	/// </summary>
	public bool ShouldUseUnarmedLocomotionBranch()
	{
		if (m_Equipment == null || m_Animator == null)
			return false;

		ItemDefinition current = m_Equipment.EquippedDefinition;
		if (current == null || !current.IsEquipment || current.EquipmentKind != EquipmentKind.Weapon)
			return false;

		if (GetEffectiveIsReady())
			return false;

		return true;
	}

	/// <summary>
	/// Стоя — любой tier; присед — только Walk (стоя/шаг). Лёжа и присед с Run/Sprint — нет.
	/// </summary>
	/// <summary>
	/// В руках оружие и включён «на готове» — для разворота корня на <see cref="UnitVision.VisibleTarget"/> и т.п.
	/// </summary>
	public bool IsWeaponEquippedAndReady()
	{
		return IsWeaponEquipped() && m_UserWantsReady;
	}

	/// <summary>
	/// Текущее желаемое состояние "готов" до учёта принудительного Ready в prone.
	/// Нужен ИИ/скриптам поведения, чтобы управлять режимом без эмуляции клавиши E.
	/// </summary>
	public bool WantsReady => m_UserWantsReady;

	public bool IsUnarmedNotReadyContextAllowed()
	{
		if (m_Animator == null)
			return false;

		int stance = m_Animator.GetInteger(s_Stance);
		if (stance == (int)LocomotionStance.Prone)
			return false;

		if (stance == (int)LocomotionStance.Crouch)
		{
			int tier = m_Animator.GetInteger(s_LocomotionTier);
			// В присяде только «шаг» (Walk); Run/Sprint на аниматоре не смешиваем с безоружной веткой.
			return tier == 0;
		}

		return true;
	}

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

		m_UserWantsReady = true;

		if (IsSprintingNow())
		{
			if (m_LocomotionDriver != null)
				m_LocomotionDriver.ForceWalkMoveMode();
			else if (m_ClickToMove != null)
				m_ClickToMove.ForceWalkMoveMode();
		}
	}

	/// <summary>
	/// Прямое управление состоянием "готов" для ИИ/скриптов.
	/// Если включаем ready во время спринта, можно принудительно сбросить скорость до шага.
	/// </summary>
	public void SetReadyWanted(bool _ready, bool _forceWalkIfNeeded = true)
	{
		if (!IsWeaponEquipped())
		{
			m_UserWantsReady = false;
			return;
		}

		m_UserWantsReady = _ready;

		if (_ready && _forceWalkIfNeeded && IsSprintingNow())
		{
			if (m_LocomotionDriver != null)
				m_LocomotionDriver.ForceWalkMoveMode();
			else if (m_ClickToMove != null)
				m_ClickToMove.ForceWalkMoveMode();
		}
	}

	/// <summary>Временная блокировка ввода E (готов/не готов), например для «костыльного» перехода стойки.</summary>
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

		if (m_Animator != null)
			m_LayerIndex = m_Animator.GetLayerIndex(c_LayerName);
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
			m_SnapLayerWeightNextFrame = true;
		}

		if (CanUseDirectKeyboardInput() && WasToggleReadyPressedThisFrame() && IsWeaponEquipped())
		{
			if (m_BlockToggleInput)
				return;

			bool isSprinting = IsSprintingNow();
			bool nextReady = !m_UserWantsReady;

			// Запрет: в лёже нельзя переключать "готов" → "не готов".
			// Это исключает ситуацию, когда граф локомоции уходит в безоружную ветку, пока юнит лежит.
			if (!nextReady && m_Animator != null && m_Animator.GetInteger(s_Stance) == (int)LocomotionStance.Prone)
				return;

			m_UserWantsReady = nextReady;

			// Требование: если вручную включили "готов" во время спринта — юнит сбрасывает скорость на шаг и становится готов.
			if (isSprinting && nextReady && m_ClickToMove != null)
				m_ClickToMove.ForceWalkMoveMode();
		}
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

	private static bool WasToggleReadyPressedThisFrame(Key _key)
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

	private bool WasToggleReadyPressedThisFrame()
	{
		return WasToggleReadyPressedThisFrame(m_ToggleReadyKey);
	}

	private void ApplyUpperBodyNoAimLayer()
	{
		if (m_Animator == null || m_LayerIndex < 0)
			return;

		bool isMagazineLoading = m_MagazineLoadingController != null && m_MagazineLoadingController.IsLoadingMagazine;

		// Слой рук показываем только в разрешённом контексте (стойка/скорость), даже если "не готов" активен.
		bool shouldShow = isMagazineLoading || (ShouldUseUnarmedLocomotionBranch() && IsUnarmedNotReadyContextAllowed());
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

		if (isMagazineLoading)
		{
			m_WasNoAimLayerActive = true;
			return;
		}

		ItemDefinition weapon = m_Equipment != null ? m_Equipment.EquippedDefinition : null;
		WeaponType wt = weapon != null ? weapon.WeaponType : WeaponType.Primary;

		if (!m_WasNoAimLayerActive || wt != m_LastNoAimWeaponTypePlayed)
		{
			string stateName = wt == WeaponType.Secondary ? c_StatePistolNoAim : c_StateRifleNoAim;
			m_Animator.CrossFadeInFixedTime(stateName, m_LayerBlendSeconds, m_LayerIndex);
			m_LastNoAimWeaponTypePlayed = wt;
		}

		m_WasNoAimLayerActive = true;
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
	#endregion
}

