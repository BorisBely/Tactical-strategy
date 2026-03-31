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

	[Header("Ввод")]
	[SerializeField] private Key m_ToggleReadyKey = Key.E;

	[Header("Слой рук (no aim)")]
	[SerializeField, Min(0f)] private float m_LayerBlendSeconds = 0.08f;

	private static readonly int s_Stance = Animator.StringToHash(UnitAnimatorWeaponMode.ParamStance);
	private static readonly int s_LocomotionTier = Animator.StringToHash(UnitClickToMove.ParamLocomotionTier);

	private int m_LayerIndex = -1;
	private bool m_IsReady;
	private ItemDefinition m_LastEquipped;
	private bool m_WasNoAimLayerActive;
	private WeaponType m_LastNoAimWeaponTypePlayed;
	#endregion

	#region Public Methods
	/// <summary>
	/// Нужно ли играть безоружную локомоцию при том, что в руках оружие (не на готове в допустимом контексте).
	/// </summary>
	public bool ShouldUseUnarmedLocomotionBranch()
	{
		if (m_Equipment == null || m_Animator == null)
			return false;

		ItemDefinition current = m_Equipment.EquippedDefinition;
		if (current == null || !current.IsEquipment || current.EquipmentKind != EquipmentKind.Weapon)
			return false;

		if (m_IsReady)
			return false;

		return IsUnarmedNotReadyContextAllowed();
	}

	/// <summary>
	/// Стоя — любой tier; присед — только Walk (стоя/шаг). Лёжа и присед с Run/Sprint — нет.
	/// </summary>
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
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();
		if (m_Equipment == null)
			m_Equipment = GetComponent<UnitEquipment>();

		if (m_Animator != null)
			m_LayerIndex = m_Animator.GetLayerIndex(c_LayerName);
	}

	private void OnEnable()
	{
		m_IsReady = false;
		m_LastEquipped = null;
		m_WasNoAimLayerActive = false;
		if (m_Animator != null && m_LayerIndex >= 0)
			m_Animator.SetLayerWeight(m_LayerIndex, 0f);
	}

	private void Update()
	{
		ItemDefinition current = m_Equipment != null ? m_Equipment.EquippedDefinition : null;
		if (!ReferenceEquals(current, m_LastEquipped))
		{
			m_LastEquipped = current;
			m_IsReady = false;
		}

		if (WasToggleReadyPressedThisFrame() && IsWeaponEquipped())
			m_IsReady = !m_IsReady;
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

		bool shouldShow = ShouldUseUnarmedLocomotionBranch();
		float targetWeight = shouldShow ? 1f : 0f;
		m_Animator.SetLayerWeight(m_LayerIndex, targetWeight);

		if (!shouldShow)
		{
			m_WasNoAimLayerActive = false;
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
	#endregion
}

